using System.Globalization;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Reclamaciones;

namespace BabelHub.Ecommerce.Application.Reclamaciones.Interno;

/// <summary>Los disparadores cerrados de la auditoría de la vinculación (AUD-46, REC-29): el mismo conjunto que el catálogo de auditoría.</summary>
internal static class DisparadoresDeVinculo
{
    public const string Registro = "registro";

    public const string Consulta = "consulta";

    public const string Pendientes = "pendientes";

    public const string CambioDeCorreo = "cambio-de-correo";

    public const string VinculacionInicial = "vinculacion-inicial";
}

/// <summary>El estado del recorrido único de la vinculación inicial (REC-26, D17): el cursor (un correo normalizado), cuándo empezó y cuándo terminó.</summary>
internal sealed record EstadoDeLaVinculacionInicial(string? UltimoCorreo, DateTimeOffset? IniciadaEn, DateTimeOffset? CompletadaEn);

/// <summary>
/// Las operaciones de las hojas que cambian o leen el vínculo con la cuenta (D14, D15; T25): cada una es <b>una sola sentencia</b> condicional y atómica en la Infraestructura, bajo <c>EjecutarComo(reclamaciones)</c> y dentro de la
/// transacción actual. Es un puerto aparte de <see cref="IRepositorioDeHojas"/>: las hojas se vinculan o se desvinculan por conjunto, nunca por una entidad leída antes.
/// </summary>
internal interface IVinculosDeHojas
{
    /// <summary>
    /// Vincula a la cuenta las hojas aún sin cuenta, que no son de una cuenta cancelada ni están anonimizadas, cuyo correo normalizado de contacto es el dado (una sola sentencia; gana una sola de dos operaciones simultáneas,
    /// REC-28). Devuelve cuántas vinculó.
    /// </summary>
    Task<int> ReclamarDelCorreo(Guid cuentaId, string correoNormalizado, CancellationToken cancelacion = default);

    /// <summary>Las hojas vinculadas a la cuenta, también las descartadas, por fecha de registro descendente (REC-14).</summary>
    Task<ListadoDeHojas> ListarDeLaCuenta(Guid cuentaId, int pagina, int tamano, CancellationToken cancelacion = default);

    /// <summary>Cuántas hojas pendientes tiene vinculadas la cuenta, sin las descartadas ni las de una cuenta cancelada (REC-19).</summary>
    Task<int> ContarPendientesDeLaCuenta(Guid cuentaId, CancellationToken cancelacion = default);

    /// <summary>
    /// Marca como de una cuenta cancelada y desvincula, en una sola sentencia, las hojas vinculadas a la cuenta y, si se da el correo verificado de la cuenta, las aún sin cuenta de ese correo (REC-20). Todo o nada dentro de la
    /// transacción de Identidad; idempotente. Devuelve cuántas cambió.
    /// </summary>
    Task<int> MarcarYDesvincularDeLaCuenta(Guid cuentaId, string? correoVerificadoNormalizado, CancellationToken cancelacion = default);

    /// <summary>Desvincula las hojas de la cuenta, que quedan como las no vinculadas de su correo de contacto (REC-30). Devuelve cuántas.</summary>
    Task<int> DesvincularDeLaCuenta(Guid cuentaId, CancellationToken cancelacion = default);

    /// <summary>El estado del recorrido de la vinculación inicial (una fila).</summary>
    Task<EstadoDeLaVinculacionInicial> EstadoDeLaVinculacionInicial(CancellationToken cancelacion = default);

    /// <summary>Hasta <paramref name="maximo"/> correos normalizados distintos, ordenados, mayores que el cursor, de hojas sin cuenta que no son de una cuenta cancelada ni están anonimizadas.</summary>
    Task<IReadOnlyList<string>> CorreosSinVincularDespuesDe(string? cursor, int maximo, CancellationToken cancelacion = default);

    /// <summary>Guarda el cursor del recorrido; con <paramref name="completada"/>, lo pone en nulo y marca la fecha de término. Fija el inicio la primera vez. Con un bloqueo asesor: dos instancias no corren a la vez.</summary>
    Task GuardarElAvanceDeLaVinculacionInicial(string? ultimoCorreo, bool completada, DateTimeOffset ahora, CancellationToken cancelacion = default);
}

/// <summary>
/// Vincula hojas a una cuenta y lo audita (REC-22, REC-29, D15; T25): <b>una sola transacción</b> con el reclamo condicional y atómico y, si vinculó alguna, la acción <c>HojaDeReclamacionVinculada</c> con la cuenta como identificador
/// opaco, el disparador y el conteo, sin el correo ni el número de ninguna hoja. Si no se puede auditar, la transacción se revierte y no se vincula nada (AUD-04). El actor lo da el contexto de la operación.
/// </summary>
internal sealed class VinculadorDeHojas(IVinculosDeHojas vinculos, IUnidadDeTrabajo unidad, IAuditoria auditoria)
{
    /// <summary>Reclama las hojas sin cuenta del correo verificado vigente de la cuenta. Lanza <see cref="AuditoriaNoDisponibleException"/> o <see cref="HojasNoDisponiblesException"/> si no puede (la operación que lo llama falla cerrada).</summary>
    public Task<int> Reclamar(Guid cuentaId, string correoVerificadoNormalizado, string disparador, CancellationToken cancelacion = default)
    {
        if (cuentaId == Guid.Empty)
        {
            throw new ArgumentException("La cuenta necesita su identificador.", nameof(cuentaId));
        }

        return unidad.Ejecutar(
            async token =>
            {
                var vinculadas = await vinculos.ReclamarDelCorreo(cuentaId, correoVerificadoNormalizado, token).ConfigureAwait(false);
                if (vinculadas > 0)
                {
                    await Auditar(cuentaId, disparador, vinculadas, token).ConfigureAwait(false);
                }

                return vinculadas;
            },
            cancelacion);
    }

    /// <summary>Audita la vinculación de <paramref name="conteo"/> hojas (la usa también el registro con sesión, en la transacción del <c>INSERT</c>).</summary>
    public Task Auditar(Guid cuentaId, string disparador, int conteo, CancellationToken cancelacion = default) =>
        AuditoriaDeVinculo.Registrar(auditoria, cuentaId, disparador, conteo, cancelacion);
}

internal static class AuditoriaDeVinculo
{
    public static Task Registrar(IAuditoria auditoria, Guid cuentaId, string disparador, int conteo, CancellationToken cancelacion) =>
        auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.HojaDeReclamacionVinculada)
            {
                ObjetoTipo = "usuario",
                ObjetoId = cuentaId.ToString("D", CultureInfo.InvariantCulture),
                Detalle = new Dictionary<string, string?>
                {
                    ["disparador"] = disparador,
                    ["conteo"] = conteo.ToString(CultureInfo.InvariantCulture),
                },
            },
            cancelacion);
}

/// <summary>
/// La parte de Reclamaciones de los cambios de cuenta (IDE-99, DD-26; REC-25, REC-30; T28): Identidad lo llama dentro de su transacción y <b>antes</b> de liberar el correo anterior de un cliente o de reemplazar una cuenta sin verificar.
/// <see cref="AlCambiarElCorreo"/> vincula a la cuenta las hojas del correo anterior (disparador <c>cambio-de-correo</c>); si el correo no es válido para el módulo no hace nada (ninguna hoja puede llevarlo) y <b>solo lanza</b> ante un error de base
/// o de auditoría, para que el cambio no se aplique. El correo anterior viaja solo en memoria: ni se guarda, ni se audita, ni se escribe en una excepción. <see cref="AlReemplazarLaCuenta"/> desvincula las hojas de la cuenta.
/// </summary>
internal sealed class ReceptorDeCambiosDeCuentaDeReclamaciones(VinculadorDeHojas vinculador, IVinculosDeHojas vinculos, IUnidadDeTrabajo unidad) : IReceptorDeCambiosDeCuenta
{
    public string Modulo => "reclamaciones";

    public async Task AlCambiarElCorreo(Guid usuarioId, string correoAnteriorNormalizado, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La cuenta necesita su identificador.", nameof(usuarioId));
        }

        if (!CorreoNormalizado.TryCrear(correoAnteriorNormalizado, out var correo))
        {
            return;
        }

        await vinculador.Reclamar(usuarioId, correo, DisparadoresDeVinculo.CambioDeCorreo, cancelacion).ConfigureAwait(false);
    }

    public async Task AlReemplazarLaCuenta(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La cuenta necesita su identificador.", nameof(usuarioId));
        }

        _ = await unidad.Ejecutar(token => vinculos.DesvincularDeLaCuenta(usuarioId, token), cancelacion).ConfigureAwait(false);
    }
}

/// <summary>
/// La vinculación inicial de las hojas existentes (REC-26, REC-34, D17; T29): un paso del trabajo diario, con el actor «sistema», que recorre <b>una sola vez</b> y por lotes los correos de contacto de las hojas sin cuenta, ordenados, con un
/// cursor; por cada correo pregunta a Identidad por la cuenta de <b>cliente</b> con ese correo verificado y, si existe, reclama las hojas de ese correo para ella (disparador <c>vinculacion-inicial</c>). Si Identidad o la auditoría fallan,
/// <b>no avanza el cursor</b> y la siguiente ejecución continúa; al recorrer todo, marca la fecha de término y no vuelve a correr. Las hojas de un visitante registradas después las reclama REC-22.
/// </summary>
internal sealed class VincularHojasExistentes(AutorizadorDeReclamaciones autorizador, IVinculosDeHojas vinculos, IClientePorCorreoVerificadoDeSistema clientes, VinculadorDeHojas vinculador, IUnidadDeTrabajo unidad, IReloj reloj)
{
    public const int TamanoDelLote = 50;

    /// <summary>Los lotes por ejecución: acota cuánto trabaja una corrida diaria; el cursor sigue en la siguiente.</summary>
    public const int LotesPorEjecucion = 20;

    public async Task<Resultado<int>> Ejecutar(CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReclamaciones.Fallo<int>(rechazo);
        }

        var total = 0;
        try
        {
            var estado = await unidad.Ejecutar(token => vinculos.EstadoDeLaVinculacionInicial(token), cancelacion).ConfigureAwait(false);
            if (estado.CompletadaEn is not null)
            {
                return Resultado<int>.Exito(0);
            }

            var cursor = estado.UltimoCorreo;
            for (var lote = 0; lote < LotesPorEjecucion; lote++)
            {
                var correos = await unidad.Ejecutar(token => vinculos.CorreosSinVincularDespuesDe(cursor, TamanoDelLote, token), cancelacion).ConfigureAwait(false);
                if (correos.Count == 0)
                {
                    await unidad.Ejecutar(
                        async token =>
                        {
                            await vinculos.GuardarElAvanceDeLaVinculacionInicial(null, completada: true, reloj.Ahora, token).ConfigureAwait(false);
                            return true;
                        },
                        cancelacion).ConfigureAwait(false);
                    return Resultado<int>.Exito(total);
                }

                foreach (var correo in correos)
                {
                    if (await clientes.Consultar(correo, cancelacion).ConfigureAwait(false) is { } cuenta)
                    {
                        total += await vinculador.Reclamar(cuenta, correo, DisparadoresDeVinculo.VinculacionInicial, cancelacion).ConfigureAwait(false);
                    }
                }

                cursor = correos[^1];
                await unidad.Ejecutar(
                    async token =>
                    {
                        await vinculos.GuardarElAvanceDeLaVinculacionInicial(cursor, completada: false, reloj.Ahora, token).ConfigureAwait(false);
                        return true;
                    },
                    cancelacion).ConfigureAwait(false);
            }

            return Resultado<int>.Exito(total);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Identidad, la auditoría o la base fallaron: el cursor no avanzó más allá del último lote completo y la siguiente ejecución continúa (REC-34). Nada del correo se registra.
            return FallosDeReclamaciones.De<int>(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }
    }
}

/// <summary>
/// La revisión de que la vinculación inicial termina (T29): si pasaron <b>7 días desde que empezó</b> sin completarse, publica una alerta técnica; no cambia nada. La alerta lleva solo el motivo y el instante.
/// </summary>
internal sealed class RevisarVinculacionInicial(IVinculosDeHojas vinculos, IUnidadDeTrabajo unidad, IPublicadorDeAlertasTecnicas alertas, IReloj reloj)
{
    public const string MotivoDeAlerta = "vinculacion-inicial-de-hojas-sin-completar";

    public static readonly TimeSpan EsperaMaxima = TimeSpan.FromDays(7);

    /// <summary>Devuelve si publicó la alerta. Si no puede leer el estado, alerta (no se asume que terminó).</summary>
    public async Task<Resultado<bool>> Ejecutar(CancellationToken cancelacion = default)
    {
        var ahora = reloj.Ahora;
        EstadoDeLaVinculacionInicial? estado;
        try
        {
            estado = await unidad.Ejecutar(token => vinculos.EstadoDeLaVinculacionInicial(token), cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            estado = null;
        }

        if (estado is { CompletadaEn: not null } || estado is { IniciadaEn: null } || (estado is { IniciadaEn: { } inicio } && ahora - inicio < EsperaMaxima))
        {
            return Resultado<bool>.Exito(false);
        }

        await alertas.Publicar(new AlertaTecnica(MotivoDeAlerta, ahora), CancellationToken.None).ConfigureAwait(false);
        return Resultado<bool>.Exito(true);
    }
}
