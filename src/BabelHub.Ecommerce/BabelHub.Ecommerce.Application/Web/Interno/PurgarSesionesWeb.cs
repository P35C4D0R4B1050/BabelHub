using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>Lo que hizo una corrida de la purga: cuántas sesiones cerró por vencimiento, cuántas revocaciones confirmó o descartó, cuántas filas y borradores borró.</summary>
public sealed record ResultadoDePurga(int Cerradas, int Revocadas, int Descartadas, int SesionesBorradas, int BorradoresBorrados, int RevocacionesPendientes)
{
    public int Cambios => Cerradas + Revocadas + Descartadas + SesionesBorradas + BorradoresBorrados;
}

/// <summary>
/// La purga de sesiones y borradores (WEB-27, WEB-28, D05, plan §1 y §5; la corre un trabajo de fondo cada 5 minutos, que es el plazo real de revocación de una sesión vencida
/// sin solicitudes). Por cada fila toma <c>FOR UPDATE SKIP LOCKED</c> (nunca espera a una solicitud ni se pisa con otra réplica) y **revoca la renovación antes de borrar**:
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Una sesión abierta que ya venció por inactividad o por tope sin que nadie la usara se cierra, se borra su credencial y se revoca su renovación.</item>
/// <item>Una fila cerrada con la revocación pendiente la reintenta; si Identidad confirma, borra la renovación cifrada; si no, cuenta el reintento y, al llegar a
/// <see cref="ReintentosParaAlertar"/>, deja una alerta técnica. Si la renovación ya venció con la sesión (<c>expira_absoluta</c>) no hay nada que revocar y se descarta.</item>
/// <item>Una fila cerrada hace 30 minutos o más, sin revocación pendiente, se borra (ya no sirve ni para el borrador). Los borradores vencidos se borran siempre.</item>
/// </list>
/// Si Identidad no responde en una corrida, no se la vuelve a llamar con las demás filas (se las deja con la revocación pendiente): la purga no se arrastra con una dependencia caída.
/// </remarks>
public sealed class PurgarSesionesWeb
{
    /// <summary>Con cuántos reintentos fallidos seguidos se avisa por una alerta técnica (a 5 minutos por corrida, unos 25 minutos).</summary>
    public const int ReintentosParaAlertar = 5;

    public const string MotivoDeAlertaPorRevocacionPersistente = "revocacion-de-renovaciones-persistente";

    /// <summary>Cuántas filas atiende cada transacción: las llamadas a Identidad ocurren con las filas bloqueadas, así que el lote es corto.</summary>
    public const int TamanoDelLote = 20;

    /// <summary>Tope de lotes por corrida: la siguiente corrida sigue donde esta quedó.</summary>
    public const int LotesPorCorrida = 50;

    private readonly ServiciosDeSesion _comun;
    private readonly IAlmacenDeBorradores _borradores;
    private readonly IPublicadorDeAlertasTecnicas _alertas;
    private readonly IUnidadDeTrabajo _unidad;

    public PurgarSesionesWeb(
        IAlmacenDeSesionesWeb sesiones,
        IAlmacenDeBorradores borradores,
        ICifradorDeSecretosDeSesion cifrador,
        ISesionesDeIdentidad identidad,
        IRegistroDeSeguridadDeLaWeb seguridad,
        IPublicadorDeAlertasTecnicas alertas,
        IUnidadDeTrabajo unidad,
        IReloj reloj)
    {
        _comun = new ServiciosDeSesion(sesiones, cifrador, identidad, seguridad, reloj);
        _borradores = borradores;
        _alertas = alertas;
        _unidad = unidad;
    }

    public async Task<ResultadoDePurga> Ejecutar(CancellationToken cancelacion = default)
    {
        var total = new Acumulado();
        var identidadCaida = false;

        for (var lote = 0; lote < LotesPorCorrida; lote++)
        {
            var (atendidas, cambios) = await _unidad.Ejecutar(
                async ct =>
                {
                    var ahora = _comun.Reloj.Ahora;
                    var filas = await _comun.Sesiones.CargarParaPurgar(ahora, TamanoDelLote, ct).ConfigureAwait(false);
                    var acumulado = new Acumulado();
                    foreach (var fila in filas)
                    {
                        identidadCaida = await Atender(fila, ahora, identidadCaida, acumulado, ct).ConfigureAwait(false);
                    }

                    total.Sumar(acumulado);
                    return (filas.Count, acumulado.Cambios);
                },
                cancelacion).ConfigureAwait(false);

            // Sin cambios (solo quedan revocaciones pendientes que no se pueden atender ahora) o un lote incompleto: no hay más por hacer en esta corrida.
            if (atendidas < TamanoDelLote || cambios == 0)
            {
                break;
            }
        }

        var borradores = await _unidad.Ejecutar(async ct => await _borradores.BorrarVencidos(_comun.Reloj.Ahora, 1000, ct).ConfigureAwait(false), cancelacion).ConfigureAwait(false);
        return new ResultadoDePurga(total.Cerradas, total.Revocadas, total.Descartadas, total.SesionesBorradas, borradores, total.Pendientes);
    }

    /// <summary>Atiende una fila y devuelve si Identidad quedó caída (para no llamarla más en la corrida).</summary>
    private async Task<bool> Atender(SesionGuardada fila, DateTimeOffset ahora, bool identidadCaida, Acumulado acumulado, CancellationToken cancelacion)
    {
        var sesion = fila.Sesion;

        if (sesion.Estado == EstadoDeSesion.Abierta)
        {
            var evaluacion = sesion.Evaluar(ahora);
            if (evaluacion == EvaluacionDeSesion.Vigente)
            {
                return identidadCaida;
            }

            var motivo = evaluacion == EvaluacionDeSesion.VencidaPorTopeAbsoluto ? MotivoDeCierre.VencidaPorTopeAbsoluto : MotivoDeCierre.VencidaPorInactividad;
            await _comun.CerrarYRevocar(fila, motivo, cancelacion, intentarRevocar: !identidadCaida).ConfigureAwait(false);
            await _comun.Sesiones.Guardar(fila, cancelacion).ConfigureAwait(false);
            acumulado.Cerradas++;
            if (fila.RevocacionPendiente)
            {
                acumulado.Pendientes++;
                return true;
            }

            return identidadCaida;
        }

        if (fila.RevocacionPendiente)
        {
            return await ReintentarRevocacion(fila, ahora, identidadCaida, acumulado, cancelacion).ConfigureAwait(false);
        }

        if (sesion.CerradaEn is { } cerradaEn && ahora - cerradaEn >= PoliticaDeBorradores.Vigencia)
        {
            await _comun.Sesiones.Borrar(sesion.Id, cancelacion).ConfigureAwait(false);
            acumulado.SesionesBorradas++;
        }

        return identidadCaida;
    }

    private async Task<bool> ReintentarRevocacion(SesionGuardada fila, DateTimeOffset ahora, bool identidadCaida, Acumulado acumulado, CancellationToken cancelacion)
    {
        var sesion = fila.Sesion;

        // La renovación vive hasta el tope de la sesión: pasado ese instante ya no sirve y no hay nada que revocar.
        if (ahora >= sesion.ExpiraAbsoluta)
        {
            fila.RenovacionCifrada = null;
            fila.RevocacionPendiente = false;
            fila.RenovacionIncierta = false;
            await _comun.Sesiones.Guardar(fila, cancelacion).ConfigureAwait(false);
            acumulado.Descartadas++;
            return identidadCaida;
        }

        if (identidadCaida)
        {
            acumulado.Pendientes++;
            return true;
        }

        var confirmada = await _comun.Revocar(fila, cancelacion).ConfigureAwait(false);
        if (confirmada)
        {
            await _comun.Sesiones.Guardar(fila, cancelacion).ConfigureAwait(false);
            acumulado.Revocadas++;
            return false;
        }

        fila.ReintentosDeRevocacion++;
        await _comun.Sesiones.Guardar(fila, cancelacion).ConfigureAwait(false);
        acumulado.Pendientes++;
        if (fila.ReintentosDeRevocacion == ReintentosParaAlertar)
        {
            await _alertas.Publicar(new AlertaTecnica(MotivoDeAlertaPorRevocacionPersistente, ahora), cancelacion).ConfigureAwait(false);
        }

        return true;
    }

    private sealed class Acumulado
    {
        public int Cerradas { get; set; }

        public int Revocadas { get; set; }

        public int Descartadas { get; set; }

        public int SesionesBorradas { get; set; }

        public int Pendientes { get; set; }

        public int Cambios => Cerradas + Revocadas + Descartadas + SesionesBorradas;

        public void Sumar(Acumulado otro)
        {
            Cerradas += otro.Cerradas;
            Revocadas += otro.Revocadas;
            Descartadas += otro.Descartadas;
            SesionesBorradas += otro.SesionesBorradas;
            Pendientes += otro.Pendientes;
        }
    }
}
