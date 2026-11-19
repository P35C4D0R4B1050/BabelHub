using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>
/// Preparar y marcar listas las líneas de un pedido (T15; PED-09, PED-10, PED-19, PED-28; AC-PED-07, 08, 23, 25): en <b>recojo</b> solo el encargado de su sucursal; a <b>domicilio</b>, logística y la administración central. El orden es el de plan §4 (rol → sucursal de la ruta → límite de gestión →
/// <b>transacción</b>: pedido con bloqueo y su alcance → pertenencia de las líneas → ninguna operación en curso → estado). Una línea que espera el traslado se rechaza y las demás se preparan (AC-PED-08); si ninguna se aplica, la operación es el error del motivo común. Al marcar listas un pedido <b>a domicilio</b> se crea el
/// envío por <see cref="IDespachoDeEnvios"/> <b>dentro de la misma transacción</b> (Envíos lee las líneas recién marcadas): un <c>Fallo</c> o una excepción de Envíos <b>revierte toda la operación</b>. Cada cambio de estado del pedido se audita y se avisa una vez por operación.
/// </summary>
internal sealed class OperarLineasDelPedido(
    AutorizadorDePedidos autorizador,
    ControlesDeGestionDePedidos controles,
    IRepositorioDePedidos pedidos,
    IUnidadDeTrabajo unidad,
    IDespachoDeEnvios despacho,
    IAuditoria auditoria,
    INotificador notificador,
    IReloj reloj)
{
    public const int MaximoDeLineasPorOperacion = 50;

    public Task<Resultado<LineasOperadas>> PrepararDeRecojo(string codigoDeSucursal, OperarLineasEntrada entrada, CancellationToken cancelacion) =>
        Operar(OperacionDePedidos.GestionDeRecojo, codigoDeSucursal, entrada, TipoDeOperacionDeLineas.Preparar, cancelacion);

    public Task<Resultado<LineasOperadas>> MarcarListasDeRecojo(string codigoDeSucursal, OperarLineasEntrada entrada, CancellationToken cancelacion) =>
        Operar(OperacionDePedidos.GestionDeRecojo, codigoDeSucursal, entrada, TipoDeOperacionDeLineas.MarcarListas, cancelacion);

    public Task<Resultado<LineasOperadas>> PrepararADomicilio(OperarLineasEntrada entrada, CancellationToken cancelacion) =>
        Operar(OperacionDePedidos.GestionDeDespacho, null, entrada, TipoDeOperacionDeLineas.Preparar, cancelacion);

    public Task<Resultado<LineasOperadas>> MarcarListasADomicilio(OperarLineasEntrada entrada, CancellationToken cancelacion) =>
        Operar(OperacionDePedidos.GestionDeDespacho, null, entrada, TipoDeOperacionDeLineas.MarcarListas, cancelacion);

    private async Task<Resultado<LineasOperadas>> Operar(OperacionDePedidos operacion, string? codigoDeSucursal, OperarLineasEntrada entrada, TipoDeOperacionDeLineas tipo, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var quien = await autorizador.ExigirRol(operacion, cancelacion).ConfigureAwait(false);
        if (!quien.EsExito)
        {
            return Resultado<LineasOperadas>.Fallo(quien.Error!);
        }

        if (operacion == OperacionDePedidos.GestionDeRecojo && await autorizador.ExigirSucursalDeLaRuta(quien.Valor, codigoDeSucursal ?? string.Empty, cancelacion).ConfigureAwait(false) is { } fueraDeSucursal)
        {
            return Resultado<LineasOperadas>.Fallo(fueraDeSucursal);
        }

        if (await controles.LimiteDeGestion(quien.Valor, cancelacion).ConfigureAwait(false) is { } limite)
        {
            return Resultado<LineasOperadas>.Fallo(limite);
        }

        // Los repetidos se desduplican sin evento; un vacío o más de 50 es validación (plan §6).
        var lineaIds = (entrada.LineaIds ?? []).Distinct().ToList();
        if (lineaIds.Count is 0 or > MaximoDeLineasPorOperacion)
        {
            return Resultado<LineasOperadas>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDePedidos.ValidacionFallida, [new ErrorDeCampo("lineaIds", lineaIds.Count == 0 ? "obligatorio" : "demasiado-largo")]));
        }

        try
        {
            return await unidad.Ejecutar(token => Aplicar(quien.Valor, entrada.PedidoId, lineaIds, tipo, token), cancelacion).ConfigureAwait(false);
        }
        catch (OperacionRechazadaException rechazo)
        {
            return Resultado<LineasOperadas>.Fallo(rechazo.Error);
        }
        catch (EsperaDeBloqueoDeUnPedidoAgotadaException)
        {
            return Resultado<LineasOperadas>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.Conflicto));
        }
    }

    private async Task<Resultado<LineasOperadas>> Aplicar(SolicitanteDePedidos solicitante, Guid pedidoId, IReadOnlyList<Guid> lineaIds, TipoDeOperacionDeLineas tipo, CancellationToken cancelacion)
    {
        var pedido = await pedidos.Obtener(pedidoId, conBloqueo: true, cancelacion).ConfigureAwait(false);
        if (await autorizador.ComprobarAlcance(pedido, solicitante, cancelacion).ConfigureAwait(false) is { } ajeno)
        {
            return Resultado<LineasOperadas>.Fallo(ajeno);
        }

        if (await autorizador.ComprobarLineas(pedido!, lineaIds, cancelacion).ConfigureAwait(false) is { } lineaAjena)
        {
            return Resultado<LineasOperadas>.Fallo(lineaAjena);
        }

        if (await controles.ExigirSinOperacionEnCurso(pedido!.Id, lineaIds, cancelacion).ConfigureAwait(false) is { } enCurso)
        {
            return Resultado<LineasOperadas>.Fallo(enCurso);
        }

        var ahora = reloj.Ahora;
        var anterior = pedido.Estado;
        var resultados = tipo == TipoDeOperacionDeLineas.Preparar ? pedido.PrepararLineas(lineaIds, ahora) : pedido.MarcarListas(lineaIds, ahora);
        var aplicadas = resultados.Where(r => r.Resultado == ResultadoDeOperacionDeLinea.Aplicada).Select(r => r.LineaId).ToList();
        if (aplicadas.Count == 0)
        {
            // Ninguna línea cambió: el motivo común (espera de traslado o estado inválido) es el error.
            var espera = resultados.Any(r => r.Resultado == ResultadoDeOperacionDeLinea.EsperaElTraslado);
            return Resultado<LineasOperadas>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, espera ? ClavesDePedidos.LineaEsperaTraslado : ClavesDePedidos.EstadoInvalido));
        }

        await pedidos.Actualizar(pedido, cancelacion).ConfigureAwait(false);

        if (tipo == TipoDeOperacionDeLineas.MarcarListas && pedido.Tipo == TipoDeEntregaDelPedido.Domicilio)
        {
            await CrearLosEnvios(pedido, aplicadas, cancelacion).ConfigureAwait(false);
            await pedidos.Actualizar(pedido, cancelacion).ConfigureAwait(false);
        }

        if (pedido.Estado != anterior)
        {
            await Auditar(pedido, anterior, cancelacion).ConfigureAwait(false);
        }

        // El aviso es por operación y solo cuando una línea cambia de estado (el «listo» de domicilio es un hito: lo avisa el envío con su guía).
        if (!(tipo == TipoDeOperacionDeLineas.MarcarListas && pedido.Tipo == TipoDeEntregaDelPedido.Domicilio))
        {
            var destino = tipo == TipoDeOperacionDeLineas.Preparar ? "en-preparacion" : "listo-para-recojo";
            await new AvisosDePedidos(notificador).Solicitar(pedido, destino, $"{destino}-{AvisosDePedidos.HuellaDeLineas(aplicadas)}", AvisosDePedidos.SucursalesResponsables(pedido, aplicadas), cancelacion).ConfigureAwait(false);
        }

        return Resultado<LineasOperadas>.Exito(new LineasOperadas(
            pedido.Id,
            ProyeccionesDePedidos.AEstadoVisible(pedido.Estado),
            [.. resultados.Select(r => new ResultadoDeLineaOperada(r.LineaId, r.Resultado switch
            {
                ResultadoDeOperacionDeLinea.Aplicada => ResultadoVisibleDeLinea.Aplicada,
                ResultadoDeOperacionDeLinea.EsperaElTraslado => ResultadoVisibleDeLinea.EsperaElTraslado,
                _ => ResultadoVisibleDeLinea.EstadoInvalido,
            }))]));
    }

    /// <summary>
    /// Crea un envío por cada fuente distinta de las líneas marcadas listas (PED-28), <b>dentro de la transacción</b> y con el pedido ya bloqueado. Un <c>Fallo</c> o una excepción de Envíos lanza y <b>revierte todo</b> (con «servicio no disponible», reintentable); un envío ya cancelado o existente es idempotente por
    /// <c>(pedido, fuente, líneas)</c>.
    /// </summary>
    private async Task CrearLosEnvios(Pedido pedido, IReadOnlyList<Guid> aplicadas, CancellationToken cancelacion)
    {
        foreach (var grupo in pedido.Lineas.Where(l => aplicadas.Contains(l.Id)).GroupBy(l => l.CodigoDeFuente!, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Resultado<ResultadoDeCrearEnvio> creado;
            try
            {
                creado = await despacho.Crear(new CrearEnvioEntrada(pedido.Id, grupo.Key, [.. grupo.Select(l => l.Id)]), cancelacion).ConfigureAwait(false);
            }
            catch (Exception excepcion) when (excepcion is not (OperationCanceledException or OperacionRechazadaException))
            {
                throw new OperacionRechazadaException(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
            }

            if (!creado.EsExito)
            {
                throw new OperacionRechazadaException(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
            }

            pedido.AsignarEnvio(creado.Valor.LineaIds, creado.Valor.EnvioId);
        }
    }

    private Task Auditar(Pedido pedido, EstadoDePedido anterior, CancellationToken cancelacion) =>
        auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.EstadoDePedidoCambiado)
            {
                ObjetoTipo = "pedido",
                ObjetoId = pedido.Id.ToString("D"),
                Detalle = new Dictionary<string, string?> { ["estadoAnterior"] = AvisosDePedidos.NombreDelEstado(anterior), ["estadoNuevo"] = AvisosDePedidos.NombreDelEstado(pedido.Estado) },
            },
            cancelacion);
}

internal enum TipoDeOperacionDeLineas
{
    Preparar,
    MarcarListas,
}
