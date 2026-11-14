using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>
/// Asegura una credencial vigente para la solicitud (WEB-32, T07, D04, D05): la renueva cuando le quedan menos de 60 segundos <b>bajo el bloqueo de la
/// fila</b> (y relee antes de llamar, de modo que la solicitud siguiente ve la ya renovada), y la verifica. Un <b>rechazo</b> de Identidad cierra la sesión
/// (el borrador se conserva); un <b>fallo transitorio</b> responde servicio no disponible, no cierra, marca la renovación como incierta y no usa una credencial
/// vencida; una credencial <b>inválida</b> intenta una renovación y, si se rechaza, cierra; un verificador <b>sin claves vigentes</b> no cierra ninguna sesión.
/// </summary>
internal sealed class AsegurarCredencialDeSesion(
    ServiciosDeSesion comun,
    IUnidadDeTrabajo unidad,
    IVerificadorDeCredencial verificador,
    IPublicadorDeAlertasTecnicas alertas,
    LimitadorDeRenovaciones? limitador = null)
{
    public static readonly TimeSpan MargenDeRenovacion = TimeSpan.FromSeconds(60);

    private readonly LimitadorDeRenovaciones _limitador = limitador ?? new LimitadorDeRenovaciones();

    public async Task<ResultadoDeCredencial> Ejecutar(string valorDeLaCookie, HostWeb host, CancellationToken cancelacion)
    {
        try
        {
            return await EjecutarInterno(valorDeLaCookie, host, cancelacion).ConfigureAwait(false);
        }
        catch (BloqueoDeSesionNoDisponibleException)
        {
            // Otra solicitud retiene la fila más allá del plazo: servicio no disponible; la sesión sigue y la solicitud siguiente lo reintenta (plan §5).
            return Sin(EstadoDeCredencial.ServicioNoDisponible);
        }
    }

    private async Task<ResultadoDeCredencial> EjecutarInterno(string valorDeLaCookie, HostWeb host, CancellationToken cancelacion)
    {
        if (!ValorDeCookieDeSesion.TieneFormaValida(valorDeLaCookie))
        {
            return Sin(EstadoDeCredencial.SinSesion);
        }

        var id = ValorDeCookieDeSesion.Hash(valorDeLaCookie);
        var fila = await comun.Sesiones.Cargar(id, cancelacion).ConfigureAwait(false);
        if (fila is null || fila.Sesion.Estado != EstadoDeSesion.Abierta || fila.Sesion.Host != AreasDeInterfaz.Aplicar(host))
        {
            return Sin(EstadoDeCredencial.SinSesion);
        }

        // La política de la sesión también rige aquí: una sesión vencida por inactividad o por tope no renueva ni verifica nada (CTL-22, WEB-27, WEB-31).
        if (fila.Sesion.Evaluar(comun.Reloj.Ahora) != EvaluacionDeSesion.Vigente)
        {
            return await CerrarPorVencimiento(id, cancelacion).ConfigureAwait(false);
        }

        if (NecesitaRenovarse(fila))
        {
            var resultado = await Renovar(id, cancelacion).ConfigureAwait(false);
            if (resultado is not null)
            {
                return resultado;
            }

            fila = await comun.Sesiones.Cargar(id, cancelacion).ConfigureAwait(false);
            if (fila is null || fila.Sesion.Estado != EstadoDeSesion.Abierta)
            {
                return Sin(EstadoDeCredencial.SesionCerrada);
            }
        }

        return await VerificarYDevolver(fila, id, true, cancelacion).ConfigureAwait(false);
    }

    /// <summary>
    /// Renueva por vencimiento próximo, por renovación incierta o porque la fila se cifró con otra clave que la vigente: la renovación guarda credencial y renovación
    /// nuevas cifradas con la vigente (rotación de la clave de sesiones en dos pasos). Una fila ya cifrada con la vigente nunca se renueva por esto.
    /// </summary>
    private bool NecesitaRenovarse(SesionGuardada fila) =>
        fila.RenovacionIncierta
        || fila.CredencialCifrada is null
        || fila.CredencialVenceEn is null
        || fila.CredencialVenceEn.Value - comun.Reloj.Ahora < MargenDeRenovacion
        || fila.VersionDeClave != comun.Cifrador.VersionVigente;

    /// <summary>Renueva bajo el bloqueo de la fila. Devuelve <see langword="null"/> si la credencial quedó vigente y hay que verificarla; si no, el resultado final.</summary>
    private async Task<ResultadoDeCredencial?> Renovar(HashDeSesion id, CancellationToken cancelacion)
    {
        // Un tope de renovaciones simultáneas por proceso protege el grupo de conexiones: sin turno a tiempo, servicio no disponible.
        using var turno = await _limitador.Entrar(cancelacion).ConfigureAwait(false);
        return turno is null ? Sin(EstadoDeCredencial.ServicioNoDisponible) : await RenovarConTurno(id, cancelacion).ConfigureAwait(false);
    }

    private async Task<ResultadoDeCredencial?> RenovarConTurno(HashDeSesion id, CancellationToken cancelacion) =>
        await unidad.Ejecutar<ResultadoDeCredencial?>(
            async ct =>
            {
                var fila = await comun.Sesiones.CargarParaRenovar(id, ct).ConfigureAwait(false);
                if (fila is null || fila.Sesion.Estado != EstadoDeSesion.Abierta)
                {
                    return Sin(EstadoDeCredencial.SinSesion);
                }

                var evaluacion = fila.Sesion.Evaluar(comun.Reloj.Ahora);
                if (evaluacion != EvaluacionDeSesion.Vigente)
                {
                    await comun.CerrarYRevocar(fila, evaluacion == EvaluacionDeSesion.VencidaPorTopeAbsoluto ? MotivoDeCierre.VencidaPorTopeAbsoluto : MotivoDeCierre.VencidaPorInactividad, ct).ConfigureAwait(false);
                    await comun.Sesiones.Guardar(fila, ct).ConfigureAwait(false);
                    return Sin(EstadoDeCredencial.SinSesion);
                }

                // Otra solicitud pudo renovarla mientras esperaba el bloqueo: se relee antes de llamar a Identidad.
                if (!NecesitaRenovarse(fila))
                {
                    return null;
                }

                string renovacion;
                try
                {
                    renovacion = comun.DescifrarRenovacion(fila) ?? throw new SecretoDeSesionIlegibleException();
                }
                catch (SecretoDeSesionIlegibleException)
                {
                    return await CerrarPorSecretoIlegible(fila, ct).ConfigureAwait(false);
                }

                ResultadoDeRenovacion respuesta;
                try
                {
                    respuesta = await comun.Identidad.Renovar(renovacion, ct).ConfigureAwait(false);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    respuesta = new ResultadoDeRenovacion.Transitoria();
                }

                switch (respuesta)
                {
                    case ResultadoDeRenovacion.Renovada renovada:
                        Guardar(fila, renovada);
                        await comun.Sesiones.Guardar(fila, ct).ConfigureAwait(false);
                        return null;

                    case ResultadoDeRenovacion.Rechazada:
                        await comun.CerrarYRevocar(fila, MotivoDeCierre.RenovacionRechazada, ct).ConfigureAwait(false);
                        await comun.Sesiones.Guardar(fila, ct).ConfigureAwait(false);
                        return Sin(EstadoDeCredencial.SesionCerrada);

                    default:
                        // Identidad pudo haber rotado la renovación: la solicitud siguiente reintenta con la guardada (R10).
                        fila.RenovacionIncierta = true;
                        await comun.Sesiones.Guardar(fila, ct).ConfigureAwait(false);
                        return Sin(EstadoDeCredencial.ServicioNoDisponible);
                }
            },
            cancelacion).ConfigureAwait(false);

    private void Guardar(SesionGuardada fila, ResultadoDeRenovacion.Renovada renovada)
    {
        var id = fila.Sesion.Id;
        var usuario = fila.Sesion.UsuarioId;
        var credencial = comun.Cifrador.Cifrar(renovada.Credencial, PropositoDeSecreto.Credencial, ServiciosDeSesion.DatosAsociados(id, PropositoDeSecreto.Credencial, usuario));
        var renovacion = comun.Cifrador.Cifrar(renovada.Renovacion, PropositoDeSecreto.Renovacion, ServiciosDeSesion.DatosAsociados(id, PropositoDeSecreto.Renovacion, usuario));
        fila.CredencialCifrada = credencial.Bytes;
        fila.CredencialVenceEn = renovada.CredencialVenceEn;
        fila.RenovacionCifrada = renovacion.Bytes;
        fila.VersionDeClave = credencial.VersionDeClave;
        fila.RenovacionIncierta = false;
    }

    private async Task<ResultadoDeCredencial> VerificarYDevolver(SesionGuardada fila, HashDeSesion id, bool puedeReintentar, CancellationToken cancelacion)
    {
        string texto;
        try
        {
            texto = comun.Cifrador.Descifrar(
                fila.CredencialCifrada ?? throw new SecretoDeSesionIlegibleException(),
                fila.VersionDeClave,
                PropositoDeSecreto.Credencial,
                ServiciosDeSesion.DatosAsociados(id, PropositoDeSecreto.Credencial, fila.Sesion.UsuarioId));
        }
        catch (SecretoDeSesionIlegibleException)
        {
            return await unidad.Ejecutar(
                async ct => await CerrarPorSecretoIlegible(await comun.Sesiones.CargarParaRenovar(id, ct).ConfigureAwait(false) ?? fila, ct).ConfigureAwait(false),
                cancelacion).ConfigureAwait(false);
        }

        var verificacion = verificador.Verificar(texto, comun.Reloj.Ahora);
        switch (verificacion.Estado)
        {
            case EstadoDeVerificacion.Valida when verificacion.Actor is not null:
                return new ResultadoDeCredencial(EstadoDeCredencial.Asegurada, verificacion.Actor, new CredencialEnMemoria(texto));

            case EstadoDeVerificacion.SinClavesVigentes:
                // El JWKS no responde (IDE-61): no es culpa de la sesión, no se cierra ninguna.
                await alertas.Publicar(new AlertaTecnica("verificador-de-credenciales-sin-claves", comun.Reloj.Ahora), cancelacion).ConfigureAwait(false);
                return Sin(EstadoDeCredencial.ServicioNoDisponible);

            case EstadoDeVerificacion.Vencida or EstadoDeVerificacion.Invalida when puedeReintentar:
                // Una credencial inválida o vencida intenta una renovación; si Identidad la rechaza, la sesión se cierra.
                var renovada = await ForzarRenovacion(id, fila.CredencialCifrada, cancelacion).ConfigureAwait(false);
                if (renovada.Resultado is not null)
                {
                    return renovada.Resultado;
                }

                return await VerificarYDevolver(renovada.Fila!, id, false, cancelacion).ConfigureAwait(false);

            case EstadoDeVerificacion.Vencida:
                return Sin(EstadoDeCredencial.ServicioNoDisponible);

            default:
                return await unidad.Ejecutar(
                    async ct =>
                    {
                        var bloqueada = await comun.Sesiones.CargarParaRenovar(id, ct).ConfigureAwait(false) ?? fila;
                        await comun.CerrarYRevocar(bloqueada, MotivoDeCierre.RenovacionRechazada, ct).ConfigureAwait(false);
                        await comun.Sesiones.Guardar(bloqueada, ct).ConfigureAwait(false);
                        return Sin(EstadoDeCredencial.SesionCerrada);
                    },
                    cancelacion).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Fuerza una renovación porque la credencial observada no se pudo verificar. La lectura bajo bloqueo, el marcado y su escritura son <b>una sola transacción</b> (si no, el
    /// bloqueo se liberaría entre la lectura y la escritura y se pisaría la credencial que otra solicitud acaba de rotar), y si otra solicitud ya renovó la credencial
    /// (la fila ya no tiene la observada) no se marca ni se renueva otra vez.
    /// </summary>
    private async Task<(ResultadoDeCredencial? Resultado, SesionGuardada? Fila)> ForzarRenovacion(HashDeSesion id, byte[]? credencialObservada, CancellationToken cancelacion)
    {
        var yaRenovada = false;
        var cerrada = await unidad.Ejecutar(
            async ct =>
            {
                var marcada = await comun.Sesiones.CargarParaRenovar(id, ct).ConfigureAwait(false);
                if (marcada is null || marcada.Sesion.Estado != EstadoDeSesion.Abierta)
                {
                    return true;
                }

                if (credencialObservada is null || marcada.CredencialCifrada is null || !marcada.CredencialCifrada.AsSpan().SequenceEqual(credencialObservada))
                {
                    yaRenovada = true;
                    return false;
                }

                marcada.RenovacionIncierta = true;
                await comun.Sesiones.Guardar(marcada, ct).ConfigureAwait(false);
                return false;
            },
            cancelacion).ConfigureAwait(false);
        if (cerrada)
        {
            return (Sin(EstadoDeCredencial.SesionCerrada), null);
        }

        if (yaRenovada)
        {
            return (null, await comun.Sesiones.Cargar(id, cancelacion).ConfigureAwait(false));
        }

        var resultado = await Renovar(id, cancelacion).ConfigureAwait(false);
        if (resultado is not null)
        {
            return (resultado, null);
        }

        return (null, await comun.Sesiones.Cargar(id, cancelacion).ConfigureAwait(false));
    }

    /// <summary>Cierra por vencimiento bajo el bloqueo de la fila (si otra solicitud ya la cerró, se respeta).</summary>
    private async Task<ResultadoDeCredencial> CerrarPorVencimiento(HashDeSesion id, CancellationToken cancelacion) =>
        await unidad.Ejecutar(
            async ct =>
            {
                var fila = await comun.Sesiones.CargarParaRenovar(id, ct).ConfigureAwait(false);
                if (fila is not null && fila.Sesion.Estado == EstadoDeSesion.Abierta)
                {
                    var evaluacion = fila.Sesion.Evaluar(comun.Reloj.Ahora);
                    if (evaluacion != EvaluacionDeSesion.Vigente)
                    {
                        await comun.CerrarYRevocar(fila, evaluacion == EvaluacionDeSesion.VencidaPorTopeAbsoluto ? MotivoDeCierre.VencidaPorTopeAbsoluto : MotivoDeCierre.VencidaPorInactividad, ct).ConfigureAwait(false);
                        await comun.Sesiones.Guardar(fila, ct).ConfigureAwait(false);
                    }
                }

                return Sin(EstadoDeCredencial.SinSesion);
            },
            cancelacion).ConfigureAwait(false);

    private async Task<ResultadoDeCredencial> CerrarPorSecretoIlegible(SesionGuardada fila, CancellationToken cancelacion)
    {
        // Un fallo de descifrado equivale a una sesión inválida: cierre limpio y un evento, nunca un error 500 (D02). Se revoca lo que se pueda leer.
        await comun.Seguridad.AccesoDenegado(MotivoDeDenegacionWeb.SecretoIlegible, fila.Sesion.UsuarioId, cancelacion).ConfigureAwait(false);
        await comun.CerrarYRevocar(fila, MotivoDeCierre.CerradaPorRevocacion, cancelacion).ConfigureAwait(false);
        await comun.Sesiones.Guardar(fila, cancelacion).ConfigureAwait(false);
        return Sin(EstadoDeCredencial.SesionCerrada);
    }

    private static ResultadoDeCredencial Sin(EstadoDeCredencial estado) => new(estado, null, null);
}
