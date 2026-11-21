using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Reclamaciones;

namespace BabelHub.Ecommerce.Application.Reclamaciones.Interno;

internal sealed record ObtenerMiHojaEntrada(string? Numero);

/// <summary>
/// Las hojas del titular (T08 y T27, REC-10, REC-14, REC-22, REC-23, REC-27, REC-31). Orden fijo: sesión → rol → límite → correo de la cuenta (<see cref="ICorreoVerificadoDeLaCuenta"/>, leído en Identidad en ese instante) → reclamo de las hojas
/// sin cuenta de ese correo, <b>solo si está verificado</b> (disparador <c>consulta</c>, en una transacción con su auditoría) → lista de las hojas vinculadas a la cuenta, también las descartadas. Una cuenta sin correo verificado ve solo las que
/// registró con sesión y la página lo indica (<c>CorreoSinVerificar</c>). Si no se puede leer el correo, vincular o auditar, la consulta <b>falla</b> como «servicio no disponible»: nunca una lista vacía (REC-27). Sin documento ni domicilio.
/// </summary>
internal sealed class ListarMisHojas(AutorizadorDeReclamaciones autorizador, ICorreoVerificadoDeLaCuenta correos, VinculadorDeHojas vinculador, IVinculosDeHojas vinculos, IUnidadDeTrabajo unidad, LimiteDeConsultaDeHojas limite) : ICasoDeUso<ListarMisHojasEntrada, PaginaDeMisHojas>
{
    public const int TamanoMaximoDePagina = 50;

    /// <summary>Una paginación profunda sería un <c>OFFSET</c> enorme sobre el índice de la cuenta: el tope acota el costo de cada consulta.</summary>
    public const int PaginaMaxima = 10_000;

    public async Task<Resultado<PaginaDeMisHojas>> Ejecutar(ListarMisHojasEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirConsumidor(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReclamaciones.Fallo<PaginaDeMisHojas>(rechazo);
        }

        var cuenta = autorizador.ConsumidorId!.Value;
        if (await limite.Reservar(cuenta, cancelacion).ConfigureAwait(false) is { } excedido)
        {
            return FallosDeReclamaciones.Fallo<PaginaDeMisHojas>(excedido);
        }

        if (entrada.Pagina is < 1 or > PaginaMaxima || entrada.Tamano is < 1 or > TamanoMaximoDePagina)
        {
            return FallosDeReclamaciones.De<PaginaDeMisHojas>(CategoriaDeError.Validacion, ClavesDeReclamaciones.ValidacionFallida, [new ErrorDeCampo(entrada.Pagina is < 1 or > PaginaMaxima ? "pagina" : "tamano", "invalido")]);
        }

        try
        {
            var correo = CorreosDeSesion.Normalizar(await correos.De(cuenta, cancelacion).ConfigureAwait(false));
            if (correo is not null)
            {
                _ = await vinculador.Reclamar(cuenta, correo, DisparadoresDeVinculo.Consulta, cancelacion).ConfigureAwait(false);
            }

            var listado = await unidad.Ejecutar(token => vinculos.ListarDeLaCuenta(cuenta, entrada.Pagina, entrada.Tamano, token), cancelacion).ConfigureAwait(false);
            return Resultado<PaginaDeMisHojas>.Exito(new PaginaDeMisHojas(listado.Hojas.Select(MapeoDeHojas.MiHojaDe).ToList(), entrada.Pagina, entrada.Tamano, listado.Total, CorreoSinVerificar: correo is null));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Identidad, la base o la auditoría no respondieron: falla cerrada, sin lista vacía ni resultado parcial (REC-27).
            return FallosDeReclamaciones.De<PaginaDeMisHojas>(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }
    }
}

/// <summary>
/// Una hoja propia por su número (T08 y T27): valida el formato del número <b>antes</b> de reclamar nada; una hoja ajena, inexistente, de una cuenta cancelada o un número mal escrito reciben <b>el mismo</b> «no encontrada» con un acceso denegado por
/// cada intento. Reclama primero las hojas sin cuenta del correo verificado (REC-22) y solo es titular quien tiene la hoja vinculada a su cuenta (REC-23, REC-31).
/// </summary>
internal sealed class ObtenerMiHoja(AutorizadorDeReclamaciones autorizador, ICorreoVerificadoDeLaCuenta correos, VinculadorDeHojas vinculador, IRepositorioDeHojas hojas, IUnidadDeTrabajo unidad, LimiteDeConsultaDeHojas limite) : ICasoDeUso<ObtenerMiHojaEntrada, MiHoja>
{
    public async Task<Resultado<MiHoja>> Ejecutar(ObtenerMiHojaEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirConsumidor(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReclamaciones.Fallo<MiHoja>(rechazo);
        }

        var cuenta = autorizador.ConsumidorId!.Value;
        if (await limite.Reservar(cuenta, cancelacion).ConfigureAwait(false) is { } excedido)
        {
            return FallosDeReclamaciones.Fallo<MiHoja>(excedido);
        }

        // Un número que no es de la forma AAAA-NNNNNN no consulta nada ni reclama nada: la misma respuesta que una hoja ajena.
        if (!NumeroDeHoja.TryParse(entrada.Numero, out var numero))
        {
            return FallosDeReclamaciones.Fallo<MiHoja>(await autorizador.RechazarHojaAjena(cancelacion).ConfigureAwait(false));
        }

        HojaDeReclamacion? hoja;
        try
        {
            var correo = CorreosDeSesion.Normalizar(await correos.De(cuenta, cancelacion).ConfigureAwait(false));
            if (correo is not null)
            {
                _ = await vinculador.Reclamar(cuenta, correo, DisparadoresDeVinculo.Consulta, cancelacion).ConfigureAwait(false);
            }

            hoja = await unidad.Ejecutar(token => hojas.ObtenerPorNumero(numero, token), cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FallosDeReclamaciones.De<MiHoja>(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }

        if (hoja is null || hoja.DeCuentaCancelada || hoja.EstaAnonimizada || hoja.CuentaId != cuenta)
        {
            return FallosDeReclamaciones.Fallo<MiHoja>(await autorizador.RechazarHojaAjena(cancelacion).ConfigureAwait(false));
        }

        return Resultado<MiHoja>.Exito(MapeoDeHojas.MiHojaDe(hoja));
    }
}

internal sealed class HojasDelConsumidorDeLaAplicacion(ICasoDeUso<ListarMisHojasEntrada, PaginaDeMisHojas> listar, ICasoDeUso<ObtenerMiHojaEntrada, MiHoja> obtener) : IHojasDelConsumidor
{
    public Task<Resultado<PaginaDeMisHojas>> Listar(ListarMisHojasEntrada entrada, CancellationToken cancelacion = default) => listar.Ejecutar(entrada, cancelacion);

    public Task<Resultado<MiHoja>> Obtener(string? numero, CancellationToken cancelacion = default) => obtener.Ejecutar(new ObtenerMiHojaEntrada(numero), cancelacion);
}

internal static class CorreosDeSesion
{
    /// <summary>El correo que devuelve Identidad, normalizado de nuevo (recortado y en minúsculas). <c>null</c> solo significa «la cuenta no tiene correo verificado»; un valor que no es una dirección lanza.</summary>
    public static string? Normalizar(string? correo)
    {
        if (correo is null)
        {
            return null;
        }

        // Un valor que no es una dirección no es «sin correo verificado»: es que no se pudo resolver, y se falla cerrado (REC-20, IDE-27: nunca se asume cero ni «no hay hojas»).
        return CorreoNormalizado.TryCrear(correo, out var normalizado) ? normalizado : throw new InvalidOperationException("Identidad devolvió un correo verificado que no es una dirección válida.");
    }
}

/// <summary>
/// El límite de las consultas del consumidor (T09, REC-10): 60 por usuario y minuto (valor inicial configurable), un límite general con respaldo local. Se reserva después de comprobar la sesión y el rol y antes de leer
/// ninguna hoja; el exceso es un 429 con su espera y, si el motor de límites no responde, la consulta es «servicio no disponible» (falla cerrada).
/// </summary>
internal sealed class LimiteDeConsultaDeHojas(IControlDeLimites limites)
{
    public async Task<ErrorDeAplicacion?> Reservar(Guid usuarioId, CancellationToken cancelacion)
    {
        ResultadoDeLimite resultado;
        try
        {
            resultado = await limites.Reservar(PoliticasDeLimite.ConsultasDeHojasPorUsuario, usuarioId.ToString("N"), cancelacion: cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }

        switch (resultado.Estado)
        {
            case EstadoDeLimite.Excedido:
                return new ErrorDeAplicacion(CategoriaDeError.Limite, ClavesDeReclamaciones.LimiteExcedido, [new ErrorDeCampo(ClavesDeReclamaciones.EsperaEnSegundos, (resultado.EsperaEnSegundos ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture))]);
            case EstadoDeLimite.Permitido:
                return null;
            default:
                return new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }
    }
}
