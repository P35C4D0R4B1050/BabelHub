using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Reservas.Interno;

/// <summary>Los plazos de retención del módulo (spec §6, plan §5): cada uno es la regla de un trabajo de fondo de mantenimiento.</summary>
internal static class PlazosDeReservas
{
    /// <summary>La huella del origen de una reserva sirve para investigar un abuso y se anula a las 24 horas, el mismo plazo de los eventos de límite (SEG-25).</summary>
    public static readonly TimeSpan HuellaDeOrigen = TimeSpan.FromHours(24);

    /// <summary>Las operaciones idempotentes (la clave de confirmar o cerrar) se conservan 30 días.</summary>
    public static readonly TimeSpan OperacionesIdempotentes = TimeSpan.FromDays(30);

    /// <summary>Una baja de titular se conserva hasta que <c>Auditoria</c> la consume y 30 días más (la retención de <c>por_anonimizar</c>).</summary>
    public static readonly TimeSpan BajasEntregadas = TimeSpan.FromDays(30);

    /// <summary>El <c>cliente_id</c> de las reservas se anonimiza 2 años después de la última transición de sus líneas, como los pedidos (PED-34).</summary>
    public static DateTimeOffset LimiteDeLaDepuracion(DateTimeOffset ahora) => ahora.AddYears(-2);

    /// <summary>Cuántos pedidos con líneas vencidas procesa una corrida de la expiración (la siguiente sigue con el resto).</summary>
    public const int PedidosPorCorrida = 500;

    /// <summary>Cuántas cabeceras anonimiza una corrida de la depuración diaria.</summary>
    public const int CabecerasPorCorrida = 1000;
}

/// <summary>Los pedidos con líneas pendientes vencidas que la próxima corrida de la expiración debe procesar (T25).</summary>
internal sealed record BuscarPedidosConVencidasEntrada(int Maximo);

internal sealed record PedidosConVencidas(IReadOnlyList<Guid> PedidoIds);

internal sealed record PurgarHuellasDeOrigenEntrada();

internal sealed record PurgarRegistrosCaducadosEntrada();

internal sealed record DepurarReservasAntiguasEntrada(int Maximo);

/// <summary>Cuántas filas cambió una corrida de mantenimiento (solo conteos: nunca datos del cliente).</summary>
internal sealed record ResultadoDeMantenimiento(int Filas, int Otras = 0);


/// <summary>Busca los candidatos de la expiración (los pedidos con alguna línea pendiente vencida), sin bloquear: cada uno se expira después con su propio caso de uso y su propio bloqueo (T22, T25).</summary>
internal sealed class BuscarPedidosConVencidas(AutorizadorDeReservas autorizador, IRepositorioDeReservas reservas, IReloj reloj) : ICasoDeUso<BuscarPedidosConVencidasEntrada, PedidosConVencidas>
{
    public async Task<Resultado<PedidosConVencidas>> Ejecutar(BuscarPedidosConVencidasEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReservas.Fallo<PedidosConVencidas>(rechazo);
        }

        if (entrada.Maximo <= 0)
        {
            return FallosDeReservas.Validacion<PedidosConVencidas>(ResultadoDeValidacion.Invalido([new ErrorDeCampo("maximo", ConstructorDeValidacion.Obligatorio)]));
        }

        var pedidos = await reservas.PedidosConVencidas(null, reloj.Ahora, cancelacion).ConfigureAwait(false);
        return Resultado<PedidosConVencidas>.Exito(new PedidosConVencidas([.. pedidos.Take(entrada.Maximo)]));
    }
}

/// <summary>Anula la huella de origen de las reservas de más de 24 horas (SEG-25, plan §5): una sola sentencia, idempotente.</summary>
internal sealed class PurgarHuellasDeOrigen(AutorizadorDeReservas autorizador, IRepositorioDeReservas reservas, IReloj reloj) : ICasoDeUso<PurgarHuellasDeOrigenEntrada, ResultadoDeMantenimiento>
{
    public async Task<Resultado<ResultadoDeMantenimiento>> Ejecutar(PurgarHuellasDeOrigenEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReservas.Fallo<ResultadoDeMantenimiento>(rechazo);
        }

        var filas = await reservas.PurgarHuellasDeOrigen(reloj.Ahora - PlazosDeReservas.HuellaDeOrigen, cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeMantenimiento>.Exito(new ResultadoDeMantenimiento(filas));
    }
}

/// <summary>Purga las operaciones idempotentes de más de 30 días y las bajas que <c>Auditoria</c> consumió hace más de 30 días (plan §5): las no consumidas nunca se borran.</summary>
internal sealed class PurgarRegistrosCaducados(AutorizadorDeReservas autorizador, IRepositorioDeReservas reservas, IReloj reloj) : ICasoDeUso<PurgarRegistrosCaducadosEntrada, ResultadoDeMantenimiento>
{
    public async Task<Resultado<ResultadoDeMantenimiento>> Ejecutar(PurgarRegistrosCaducadosEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReservas.Fallo<ResultadoDeMantenimiento>(rechazo);
        }

        var ahora = reloj.Ahora;
        var operaciones = await reservas.PurgarOperacionesIdempotentes(ahora - PlazosDeReservas.OperacionesIdempotentes, cancelacion).ConfigureAwait(false);
        var bajas = await reservas.PurgarBajasEntregadas(ahora - PlazosDeReservas.BajasEntregadas, cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeMantenimiento>.Exito(new ResultadoDeMantenimiento(operaciones, bajas));
    }
}

/// <summary>
/// La depuración diaria: anonimiza <c>reservas.cliente_id</c> con el <b>mismo identificador anónimo que la baja</b> (<see cref="IGeneradorDeIdentificadorAnonimo"/>) cuando el pedido lleva 2 años quieto, sin línea viva (PED-34).
/// Acotada por corrida y repetible: la cabecera ya anónima queda marcada y no se vuelve a calcular.
/// </summary>
internal sealed class DepurarReservasAntiguas(AutorizadorDeReservas autorizador, IRepositorioDeReservas reservas, IGeneradorDeIdentificadorAnonimo generador, IReloj reloj) : ICasoDeUso<DepurarReservasAntiguasEntrada, ResultadoDeMantenimiento>
{
    public async Task<Resultado<ResultadoDeMantenimiento>> Ejecutar(DepurarReservasAntiguasEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReservas.Fallo<ResultadoDeMantenimiento>(rechazo);
        }

        if (entrada.Maximo <= 0)
        {
            return FallosDeReservas.Validacion<ResultadoDeMantenimiento>(ResultadoDeValidacion.Invalido([new ErrorDeCampo("maximo", ConstructorDeValidacion.Obligatorio)]));
        }

        var filas = await reservas.AnonimizarClientesAntiguos(PlazosDeReservas.LimiteDeLaDepuracion(reloj.Ahora), entrada.Maximo, generador.Generar, cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeMantenimiento>.Exito(new ResultadoDeMantenimiento(filas));
    }
}
