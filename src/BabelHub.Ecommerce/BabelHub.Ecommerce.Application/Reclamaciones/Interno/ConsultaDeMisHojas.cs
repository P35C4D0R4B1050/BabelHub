using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Reclamaciones;

namespace BabelHub.Ecommerce.Application.Reclamaciones.Interno;

internal sealed record ObtenerMiHojaEntrada(string? Numero);

/// <summary>
/// Las hojas del consumidor con una sesión de correo verificado (T08, REC-10, REC-14, REC-20). Orden fijo: sesión → rol → correo verificado de la sesión (<see cref="ICorreoVerificadoDeLaCuenta"/>; si Identidad no responde,
/// falla cerrada) → hoja y alcance. Solo se ven las hojas del <b>correo normalizado</b> igual al de la sesión y que no son de una cuenta cancelada; también las descartadas. Sin documento ni domicilio.
/// </summary>
internal sealed class ListarMisHojas(AutorizadorDeReclamaciones autorizador, ICorreoVerificadoDeLaCuenta correos, IRepositorioDeHojas hojas, IUnidadDeTrabajo unidad, LimiteDeConsultaDeHojas limite) : ICasoDeUso<ListarMisHojasEntrada, PaginaDeMisHojas>
{
    public const int TamanoMaximoDePagina = 50;

    public async Task<Resultado<PaginaDeMisHojas>> Ejecutar(ListarMisHojasEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirConsumidor(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReclamaciones.Fallo<PaginaDeMisHojas>(rechazo);
        }

        if (await limite.Reservar(autorizador.ConsumidorId!.Value, cancelacion).ConfigureAwait(false) is { } excedido)
        {
            return FallosDeReclamaciones.Fallo<PaginaDeMisHojas>(excedido);
        }

        string? correo;
        try
        {
            correo = CorreosDeSesion.Normalizar(await correos.De(autorizador.ConsumidorId!.Value, cancelacion).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FallosDeReclamaciones.De<PaginaDeMisHojas>(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }

        if (correo is null)
        {
            return FallosDeReclamaciones.Fallo<PaginaDeMisHojas>(await autorizador.RechazarCorreoNoVerificado(cancelacion).ConfigureAwait(false));
        }

        if (entrada.Pagina is < 1 or > 1_000_000 || entrada.Tamano is < 1 or > TamanoMaximoDePagina)
        {
            return FallosDeReclamaciones.De<PaginaDeMisHojas>(CategoriaDeError.Validacion, ClavesDeReclamaciones.ValidacionFallida, [new ErrorDeCampo(entrada.Pagina is < 1 or > 1_000_000 ? "pagina" : "tamano", "invalido")]);
        }

        try
        {
            var listado = await unidad.Ejecutar(token => hojas.ListarDelCorreo(correo, entrada.Pagina, entrada.Tamano, token), cancelacion).ConfigureAwait(false);
            return Resultado<PaginaDeMisHojas>.Exito(new PaginaDeMisHojas(listado.Hojas.Select(MapeoDeHojas.MiHojaDe).ToList(), entrada.Pagina, entrada.Tamano, listado.Total));
        }
        catch (HojasNoDisponiblesException)
        {
            return FallosDeReclamaciones.De<PaginaDeMisHojas>(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }
    }
}

/// <summary>Una hoja propia por su número (T08): una hoja ajena, inexistente, de una cuenta cancelada, de otro correo o un número mal escrito reciben <b>el mismo</b> «no encontrada» con un acceso denegado por cada intento.</summary>
internal sealed class ObtenerMiHoja(AutorizadorDeReclamaciones autorizador, ICorreoVerificadoDeLaCuenta correos, IRepositorioDeHojas hojas, IUnidadDeTrabajo unidad, LimiteDeConsultaDeHojas limite) : ICasoDeUso<ObtenerMiHojaEntrada, MiHoja>
{
    public async Task<Resultado<MiHoja>> Ejecutar(ObtenerMiHojaEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirConsumidor(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReclamaciones.Fallo<MiHoja>(rechazo);
        }

        if (await limite.Reservar(autorizador.ConsumidorId!.Value, cancelacion).ConfigureAwait(false) is { } excedido)
        {
            return FallosDeReclamaciones.Fallo<MiHoja>(excedido);
        }

        string? correo;
        try
        {
            correo = CorreosDeSesion.Normalizar(await correos.De(autorizador.ConsumidorId!.Value, cancelacion).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FallosDeReclamaciones.De<MiHoja>(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }

        // Sin correo verificado, o con un número que no es de la forma AAAA-NNNNNN, no se consulta nada: la misma respuesta que una hoja ajena.
        if (correo is null || !NumeroDeHoja.TryParse(entrada.Numero, out var numero))
        {
            return FallosDeReclamaciones.Fallo<MiHoja>(await autorizador.RechazarHojaAjena(cancelacion).ConfigureAwait(false));
        }

        HojaDeReclamacion? hoja;
        try
        {
            hoja = await unidad.Ejecutar(token => hojas.ObtenerPorNumero(numero, token), cancelacion).ConfigureAwait(false);
        }
        catch (HojasNoDisponiblesException)
        {
            return FallosDeReclamaciones.De<MiHoja>(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }

        if (hoja is null || hoja.DeCuentaCancelada || !string.Equals(hoja.Datos.CorreoNormalizado, correo, StringComparison.Ordinal))
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
    /// <summary>El correo que devuelve Identidad, normalizado de nuevo (recortado y en minúsculas): sin una dirección válida cuenta como «sin correo verificado».</summary>
    public static string? Normalizar(string? correo) => correo is not null && CorreoNormalizado.TryCrear(correo, out var normalizado) ? normalizado : null;
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
                try
                {
                    await limites.Completar(resultado.Reserva!, new ResultadoMinimo("consultada"), cancelacion).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Una consulta ya contada no se devuelve por un fallo del almacén de contadores.
                }

                return null;
            default:
                return new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }
    }
}
