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
/// (1) el <b>rol</b> antes de leer nada, (2) el bloqueo del pedido, (3) el <b>alcance sobre la reserva</b> (la sucursal de recojo de su cabecera o, en un recojo por sucursal, la de todas las líneas pedidas, SEL-45; un encargado que consulta algo ajeno o inexistente recibe 403 sin pistas), (4) la
/// <b>clave</b> (su forma se valida después del alcance de sucursal en la entrega: una entrada mal formada de otra sucursal recibe el mismo 403) en <c>operaciones_idempotentes</c> (misma carga: el resultado original sin descontar otra vez; otra: conflicto), (5) el estado de cada línea y (6) el descuento: <c>Aplicar(−c, −c)</c> agregado y ordenado
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
            // El alcance va antes que la forma de la clave (SEL-39): un encargado de otra sucursal recibe el mismo 403 con su evento aunque la entrada esté mal formada, y no un 400 que dejaría pasar el intento sin registro.
            // Solo se lee la cabecera de la reserva (sin bloquear ni escribir) y no se revela si existe.
            if (entrada.Motivo == MotivoDeCierreDeReserva.Entrega)
            {
                var cabecera = entrada.PedidoId == Guid.Empty ? null : await reservas.UltimaDelPedido(entrada.PedidoId, cancelacion).ConfigureAwait(false);
                if (await ExigirAlcanceDeEntrega(cabecera, entrada.LineaIds, cancelacion).ConfigureAwait(false) is { } fueraDeAlcance)
                {
                    return FallosDeReservas.Fallo<CierreDeReserva>(fueraDeAlcance);
                }
            }

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

        // El alcance sobre la reserva, antes de la clave y del estado. Entrega: la sucursal de recojo; reparto: el rol ya basta.
        if (esEntrega && await ExigirAlcanceDeEntrega(reserva, entrada.LineaIds, cancelacion).ConfigureAwait(false) is { } fueraDeSucursal)
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
                // Con una sucursal de recojo común las unidades deben estar ya en ella: si siguen en el origen de un traslado, es estado inválido y no un acceso denegado. Por sucursal, cada línea se entrega en su fuente.
                if (esEntrega && reserva.SucursalDeRecojoId is { } sucursalDeRecojo && activa.FuenteId != sucursalDeRecojo)
                {
                    return FallosDeReservas.De<CierreDeReserva>(CategoriaDeError.Conflicto, ClavesDeReservas.ReservaEstadoInvalido);
                }

                aCerrar.Add(activa);
                resultados.Add(new ResultadoDeCierreDeLinea(lineaId, YaCerrada: false));
            }
            else if (delPedido.FirstOrDefault(l => l.Estado == EstadoDeLinea.Cerrada && l.CerradaPor == causa) is { } yaCerrada && await MismoEnvio(yaCerrada, esEntrega, entrada.EnvioId, cancelacion).ConfigureAwait(false))
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
            // Jerarquía D04: las líneas por id antes que las filas de stock.
            await reservas.BloquearLineas([.. aCerrar.Select(l => l.Id)], cancelacion).ConfigureAwait(false);
            var bloqueo = await stock.Bloquear([.. aCerrar.Select(l => l.EdicionId).Distinct()], cancelacion).ConfigureAwait(false);
            if (!bloqueo.EstaBloqueado)
            {
                return FallosDeReservas.De<CierreDeReserva>(CategoriaDeError.ReglaDeNegocio, ClavesDeReservas.EdicionNoImpresa);
            }

            // La primera operación que llega gana: lo que no cambia aquí (otra operación cerró o liberó la línea) rechaza todo, sin cambios parciales (SEL-29).
            var cerradas = await reservas.CerrarLineas([.. aCerrar.Select(l => l.Id)], causa, entrada.EnvioId, reloj.Ahora, cancelacion).ConfigureAwait(false);
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

    /// <summary>
    /// Quién puede entregar (SEL-39, SEL-45): en un recojo por sucursal, el encargado cuyas sean todas las líneas pedidas; en cualquier otro caso (sucursal de cabecera, domicilio o reserva inexistente), el de la sucursal
    /// de recojo de la cabecera, que en los dos últimos no hay. La administración central, siempre.
    /// </summary>
    private Task<ErrorDeAplicacion?> ExigirAlcanceDeEntrega(ReservaDePedido? reserva, IReadOnlyList<Guid>? lineaIds, CancellationToken cancelacion) =>
        reserva is { EsRecojoPorSucursal: true }
            ? autorizador.ExigirSucursalesDeLasLineas(reserva, lineaIds, cancelacion)
            : autorizador.ExigirSucursalDeRecojo(reserva?.SucursalDeRecojoId, cancelacion);

    /// <summary>Una línea ya cerrada por esta causa solo responde <c>ya-cerrada</c> si, en el reparto, se cerró con el mismo envío (SEL-38); un envío distinto es estado inválido. La entrega no lleva envío.</summary>
    private async Task<bool> MismoEnvio(LineaDeReserva cerrada, bool esEntrega, Guid? envioId, CancellationToken cancelacion)
    {
        if (esEntrega)
        {
            return true;
        }

        var envios = await reservas.EnviosDeCierre([cerrada.Id], cancelacion).ConfigureAwait(false);
        return envios.GetValueOrDefault(cerrada.Id) == envioId;
    }

    /// <summary>Descuenta el físico y lo reservado con <c>Aplicar(−c, −c)</c> agregado y ordenado por (edición, fuente), y audita cada línea cerrada con el físico anterior y posterior, encadenados línea a línea dentro de cada fila.</summary>
    private async Task<ErrorDeAplicacion?> Descontar(IReadOnlyList<LineaCerrada> cerradas, bool esEntrega, CancellationToken cancelacion)
    {
        var movimientos = MovimientosAgregados.Ordenados(cerradas.Select(l => (l.FuenteId, l.EdicionId, l.Cantidad)), -1, -1);

        var fisicoPorFila = new Dictionary<(Guid FuenteId, Guid EdicionId), int>();
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

            fisicoPorFila[(movimiento.FuenteId, movimiento.EdicionId)] = resultado.Anterior.Fisico;
        }

        foreach (var linea in cerradas)
        {
            // El físico se encadena línea a línea dentro de la misma fila: el anterior de una es el posterior de la anterior, y los números cuadran con cada cantidad.
            var anterior = fisicoPorFila[(linea.FuenteId, linea.EdicionId)];
            var posterior = anterior - linea.Cantidad;
            fisicoPorFila[(linea.FuenteId, linea.EdicionId)] = posterior;
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
