using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Reservas;

namespace BabelHub.Inventario.Application.Reservas.Interno;

internal sealed class ValidadorDeCerrar : IValidador<CerrarReservasEntrada>
{
    public ResultadoDeValidacion Validar(CerrarReservasEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        if (entrada.PedidoId == Guid.Empty)
        {
            errores.Add(new ErrorDeCampo("pedidoId", ConstructorDeValidacion.Obligatorio));
        }

        if (!Enum.IsDefined(entrada.Motivo))
        {
            errores.Add(new ErrorDeCampo("motivo", ConstructorDeValidacion.FormatoInvalido));
        }
        else if (entrada.Motivo == MotivoDeCierreDeReserva.Reparto && (entrada.EnvioId is null || entrada.EnvioId == Guid.Empty))
        {
            errores.Add(new ErrorDeCampo("envioId", ConstructorDeValidacion.Obligatorio));
        }
        else if (entrada.Motivo == MotivoDeCierreDeReserva.Entrega && entrada.EnvioId is not null)
        {
            errores.Add(new ErrorDeCampo("envioId", ConstructorDeValidacion.FormatoInvalido));
        }

        if (entrada.LineaIds is null || entrada.LineaIds.Count == 0)
        {
            errores.Add(new ErrorDeCampo("lineaIds", ConstructorDeValidacion.Obligatorio));
        }
        else if (entrada.LineaIds.Count > LimitesDeReservas.MaximoDeLineas)
        {
            errores.Add(new ErrorDeCampo("lineaIds", ConstructorDeValidacion.DemasiadoLargo));
        }
        else if (entrada.LineaIds.Any(i => i == Guid.Empty) || entrada.LineaIds.Distinct().Count() != entrada.LineaIds.Count)
        {
            errores.Add(new ErrorDeCampo("lineaIds", ConstructorDeValidacion.FormatoInvalido));
        }

        ReglasDeEntradaDeReservas.Clave(errores, entrada.ClaveDeIdempotencia);
        return ReglasDeEntradaDeReservas.Resultado(errores);
    }
}

/// <summary>
/// Cerrar una reserva confirmada descontando el físico: la <b>entrega</b> en la sucursal de recojo o el paso a <b>en reparto</b> de un envío a domicilio (SEL-11, SEL-21, SEL-28, SEL-29, SEL-38, SEL-39). Orden fijo:
/// (1) el <b>rol</b> antes de leer nada, (2) el bloqueo del pedido, (3) el <b>alcance sobre la reserva</b> (la sucursal de recojo de su cabecera; un encargado que consulta algo ajeno o inexistente recibe 403 sin pistas), (4) la
/// <b>clave</b> en <c>operaciones_idempotentes</c> (misma carga: el resultado original sin descontar otra vez; otra: conflicto), (5) el estado de cada línea y (6) el descuento: <c>Aplicar(−c, −c)</c> agregado y ordenado
/// con las filas de stock ya bloqueadas, y las líneas pasan a cerradas con una sentencia condicional (<b>la primera operación que llega gana</b>: la otra recibe estado inválido sin cambios parciales). Una línea ya
/// cerrada por esta misma causa responde <c>ya-cerrada</c> aunque la clave sea nueva y el físico no baja otra vez. Una línea liberada, expirada, pendiente, cerrada por otra causa o cuyas unidades siguen en la fuente
/// de origen de un traslado es estado inválido y se rechaza todo. Cada cierre se audita en la misma transacción (si no se puede, no se descuenta).
/// </summary>
internal sealed class CerrarReservas(
    AutorizadorDeReservas autorizador,
    IValidador<CerrarReservasEntrada> validador,
    IBloqueosAsesores bloqueos,
    IRepositorioDeReservas reservas,
    IMovimientosDeStock stock,
    IAuditoria auditoria,
    IReloj reloj) : ICasoDeUso<CerrarReservasEntrada, CierreDeReserva>
{
    private const string TipoDeOperacion = "cerrar";

    public async Task<Resultado<CierreDeReserva>> Ejecutar(CerrarReservasEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        // El rol primero: sin él no se mira la reserva, la clave ni el estado (SEL-39).
        if (Enum.IsDefined(entrada.Motivo) && await autorizador.ExigirRolParaCerrar(entrada.Motivo, cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReservas.Fallo<CierreDeReserva>(rechazo);
        }

        if (!Enum.IsDefined(entrada.Motivo) && await autorizador.ExigirContexto(cancelacion).ConfigureAwait(false) is { } sinContexto)
        {
            return FallosDeReservas.Fallo<CierreDeReserva>(sinContexto);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeReservas.Validacion<CierreDeReserva>(validacion);
        }

        try
        {
            return await Cerrar(entrada, cancelacion).ConfigureAwait(false);
        }
        catch (EsperaDeBloqueoAgotadaException)
        {
            return FallosDeReservas.BloqueoOcupado<CierreDeReserva>();
        }
        catch (ReservaDuplicadaException)
        {
            return FallosDeReservas.De<CierreDeReserva>(CategoriaDeError.Conflicto, ClavesDeReservas.IdempotenciaEnConflicto);
        }
    }

    private async Task<Resultado<CierreDeReserva>> Cerrar(CerrarReservasEntrada entrada, CancellationToken cancelacion)
    {
        await bloqueos.Pedido(entrada.PedidoId, cancelacion).ConfigureAwait(false);

        var reserva = await reservas.UltimaDelPedido(entrada.PedidoId, cancelacion).ConfigureAwait(false);
        var esEntrega = entrada.Motivo == MotivoDeCierreDeReserva.Entrega;

        // El alcance sobre la reserva, antes de la clave y del estado. Entrega: la sucursal de recojo de la cabecera; reparto: el rol ya basta.
        if (esEntrega && await autorizador.ExigirSucursalDeRecojo(reserva?.SucursalDeRecojoId, cancelacion).ConfigureAwait(false) is { } fueraDeSucursal)
        {
            return FallosDeReservas.Fallo<CierreDeReserva>(fueraDeSucursal);
        }

        if (reserva is null)
        {
            return FallosDeReservas.De<CierreDeReserva>(CategoriaDeError.NoEncontrado, ClavesDeReservas.ReservaNoEncontrada);
        }

        var huella = Huella(entrada);
        if (await reservas.ObtenerOperacion(entrada.PedidoId, TipoDeOperacion, entrada.ClaveDeIdempotencia, cancelacion).ConfigureAwait(false) is { } previa)
        {
            return previa.HuellaDeLaCarga == huella
                ? Resultado<CierreDeReserva>.Exito(LeerResultado(entrada.PedidoId, previa.Resultado))
                : FallosDeReservas.De<CierreDeReserva>(CategoriaDeError.Conflicto, ClavesDeReservas.IdempotenciaEnConflicto);
        }

        // El tipo de entrega debe corresponder a la operación: la entrega es de recojo y el reparto, de domicilio.
        if (esEntrega != reserva.EsDeRecojo)
        {
            return FallosDeReservas.De<CierreDeReserva>(CategoriaDeError.Conflicto, ClavesDeReservas.ReservaEstadoInvalido);
        }

        var causa = esEntrega ? CausaDeCierre.Entrega : CausaDeCierre.Reparto;
        var aCerrar = new List<LineaDeReserva>();
        var resultados = new List<ResultadoDeCierreDeLinea>();
        foreach (var lineaId in entrada.LineaIds)
        {
            var delPedido = reserva.Lineas.Where(l => l.LineaId == lineaId).ToList();
            if (delPedido.Count == 0)
            {
                return FallosDeReservas.De<CierreDeReserva>(CategoriaDeError.NoEncontrado, ClavesDeReservas.ReservaNoEncontrada);
            }

            // La línea activa es la confirmada; si no hay, vale que ya esté cerrada con esta misma causa (ya-cerrada). Cualquier otra cosa es estado inválido y se rechaza todo.
            var activa = delPedido.FirstOrDefault(l => l.Estado == EstadoDeLinea.Confirmada);
            if (activa is not null)
            {
                // En entrega las unidades deben estar ya en la sucursal de recojo: si siguen en el origen de un traslado, es estado inválido y no un acceso denegado.
                if (esEntrega && activa.FuenteId != reserva.SucursalDeRecojoId)
                {
                    return FallosDeReservas.De<CierreDeReserva>(CategoriaDeError.Conflicto, ClavesDeReservas.ReservaEstadoInvalido);
                }

                aCerrar.Add(activa);
                resultados.Add(new ResultadoDeCierreDeLinea(lineaId, YaCerrada: false));
            }
            else if (delPedido.Any(l => l.Estado == EstadoDeLinea.Cerrada && l.CerradaPor == causa))
            {
                resultados.Add(new ResultadoDeCierreDeLinea(lineaId, YaCerrada: true));
            }
            else
            {
                return FallosDeReservas.De<CierreDeReserva>(CategoriaDeError.Conflicto, ClavesDeReservas.ReservaEstadoInvalido);
            }
        }

        if (aCerrar.Count > 0)
        {
            var bloqueo = await stock.Bloquear([.. aCerrar.Select(l => l.EdicionId).Distinct()], cancelacion).ConfigureAwait(false);
            if (!bloqueo.EstaBloqueado)
            {
                return FallosDeReservas.De<CierreDeReserva>(CategoriaDeError.ReglaDeNegocio, ClavesDeReservas.EdicionNoImpresa);
            }

            // La primera operación que llega gana: lo que no cambia aquí (otra operación cerró o liberó la línea) rechaza todo, sin cambios parciales (SEL-29).
            var cerradas = await reservas.CerrarLineas([.. aCerrar.Select(l => l.Id)], causa, reloj.Ahora, cancelacion).ConfigureAwait(false);
            if (cerradas.Count != aCerrar.Count)
            {
                return FallosDeReservas.De<CierreDeReserva>(CategoriaDeError.Conflicto, ClavesDeReservas.ReservaEstadoInvalido);
            }

            if (await Descontar(cerradas, esEntrega, cancelacion).ConfigureAwait(false) is { } falloAlDescontar)
            {
                return FallosDeReservas.Fallo<CierreDeReserva>(falloAlDescontar);
            }
        }

        var cierre = new CierreDeReserva(entrada.PedidoId, resultados);
        await reservas.GuardarOperacion(entrada.PedidoId, TipoDeOperacion, entrada.ClaveDeIdempotencia, huella, Serializar(cierre), cancelacion).ConfigureAwait(false);
        return Resultado<CierreDeReserva>.Exito(cierre);
    }

    /// <summary>Descuenta el físico y lo reservado con <c>Aplicar(−c, −c)</c> agregado y ordenado por (edición, fuente), y audita cada línea cerrada con el físico anterior y posterior de su fila.</summary>
    private async Task<ErrorDeAplicacion?> Descontar(IReadOnlyList<LineaCerrada> cerradas, bool esEntrega, CancellationToken cancelacion)
    {
        var movimientos = cerradas
            .GroupBy(l => (l.FuenteId, l.EdicionId))
            .Select(g => new MovimientoDeStock(g.Key.FuenteId, g.Key.EdicionId, -checked(g.Sum(l => l.Cantidad)), -checked(g.Sum(l => l.Cantidad))))
            .OrderBy(m => m.EdicionId, OrdenDeBloqueo.DeIdentificadores)
            .ThenBy(m => m.FuenteId, OrdenDeBloqueo.DeIdentificadores);

        var fisicoPorFila = new Dictionary<(Guid FuenteId, Guid EdicionId), (int Anterior, int Posterior)>();
        foreach (var movimiento in movimientos)
        {
            var resultado = await stock.Aplicar(movimiento, cancelacion).ConfigureAwait(false);
            if (resultado.Resultado == ResultadoDeMovimiento.DeniegadoPorAlcance)
            {
                return await autorizador.DenegadoPorAlcance(cancelacion).ConfigureAwait(false);
            }

            if (!resultado.Aplicado || resultado.Anterior is null || resultado.Posterior is null)
            {
                // Descontar lo que la reserva ya apartaba nunca debería fallar: un estado imposible aborta y la transacción se revierte.
                throw new InvalidOperationException("No se pudo descontar el físico de una reserva confirmada.");
            }

            fisicoPorFila[(movimiento.FuenteId, movimiento.EdicionId)] = (resultado.Anterior.Fisico, resultado.Posterior.Fisico);
        }

        foreach (var linea in cerradas)
        {
            var (anterior, posterior) = fisicoPorFila[(linea.FuenteId, linea.EdicionId)];
            await auditoria.Registrar(
                new SolicitudDeRegistro(esEntrega ? AccionesAuditables.StockDescontadoPorEntrega : AccionesAuditables.StockDescontadoPorReparto)
                {
                    ObjetoTipo = "pedido",
                    ObjetoId = linea.PedidoId.ToString("D"),
                    SucursalOrigen = linea.FuenteId,
                    Detalle = new Dictionary<string, string?>
                    {
                        ["edicionId"] = linea.EdicionId.ToString("D"),
                        ["cantidad"] = linea.Cantidad.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["cantidadAnterior"] = anterior.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["cantidadPosterior"] = posterior.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    },
                },
                cancelacion).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>La huella de la carga canónica: motivo, envío y líneas ordenadas (el pedido ya es parte de la clave).</summary>
    internal static string Huella(CerrarReservasEntrada entrada)
    {
        var canonica = $"cerrar|{(int)entrada.Motivo}|{entrada.EnvioId:N}|{string.Join(';', entrada.LineaIds.OrderBy(i => i).Select(i => i.ToString("N")))}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonica)));
    }

    /// <summary>El resultado mínimo que se guarda: solo el desenlace por línea (nunca datos de clientes).</summary>
    private static string Serializar(CierreDeReserva cierre) =>
        JsonSerializer.Serialize(cierre.Lineas.Select(l => new { lineaId = l.LineaId, yaCerrada = l.YaCerrada }));

    private static CierreDeReserva LeerResultado(Guid pedidoId, string json)
    {
        using var documento = JsonDocument.Parse(json);
        var lineas = documento.RootElement.EnumerateArray()
            .Select(e => new ResultadoDeCierreDeLinea(e.GetProperty("lineaId").GetGuid(), e.GetProperty("yaCerrada").GetBoolean()))
            .ToList();

        // Repetir la operación con la misma clave devuelve el resultado original: lo que se cerró entonces sigue siendo «cerrada» en la respuesta (no se descuenta otra vez).
        return new CierreDeReserva(pedidoId, lineas);
    }
}
