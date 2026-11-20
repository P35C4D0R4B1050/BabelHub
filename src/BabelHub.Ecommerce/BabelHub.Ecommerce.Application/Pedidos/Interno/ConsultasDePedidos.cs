using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using ContactoDeEntregaPublico = BabelHub.Ecommerce.Application.Pedidos.ContactoDeEntrega;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>
/// Las consultas de pedidos por rol (T12; PED-04, PED-07, PED-08, PED-14, PED-26, PED-40, PED-41): el listado del cliente, el de recojo del encargado, el de domicilio de logística y el de la administración, y el detalle de cada uno, con el orden fijo de plan §4 (rol → límite → pedido y alcance). <b>Los listados
/// nunca llevan contacto</b>; el detalle lo lleva solo para el titular, logística y la administración central, y para el personal la lectura se <b>audita y falla cerrada</b>: si no se puede auditar o se agotó el presupuesto de lecturas, se oculta solo el contacto y el resto del detalle se muestra (D09). El encargado
/// recibe un tipo sin contacto. Un pedido ajeno o inexistente es el mismo «no encontrado» con su evento (PED-41).
/// </summary>
internal sealed class ConsultasDePedidos(
    AutorizadorDePedidos autorizador,
    ControlesDeGestionDePedidos controles,
    IRepositorioDePedidos pedidos,
    IReembolsos reembolsos,
    IAuditoriaDeContacto auditoriaDeContacto)
{
    // ---------- Listados ----------

    public Task<Resultado<PaginaDePedidos>> ListarDelCliente(FiltroDePedidos filtro, CancellationToken cancelacion) =>
        Listar(OperacionDePedidos.PedidosDelCliente, filtro, s => new CriterioDeListado(s.UsuarioId, null, false, null, 1, 1), codigoDeSucursal: null, cancelacion);

    public Task<Resultado<PaginaDePedidos>> ListarDeRecojo(string codigoDeSucursal, FiltroDePedidos filtro, CancellationToken cancelacion) =>
        Listar(OperacionDePedidos.GestionDeRecojo, filtro, _ => new CriterioDeListado(null, codigoDeSucursal?.Trim(), false, null, 1, 1), codigoDeSucursal ?? string.Empty, cancelacion);

    public Task<Resultado<PaginaDePedidos>> ListarADomicilio(FiltroDePedidos filtro, CancellationToken cancelacion) =>
        Listar(OperacionDePedidos.GestionDeDespacho, filtro, _ => new CriterioDeListado(null, null, true, null, 1, 1), codigoDeSucursal: null, cancelacion);

    public Task<Resultado<PaginaDePedidos>> ListarDeLaAdministracion(FiltroDePedidos filtro, CancellationToken cancelacion) =>
        Listar(OperacionDePedidos.Administracion, filtro, _ => new CriterioDeListado(null, null, false, null, 1, 1), codigoDeSucursal: null, cancelacion);

    private async Task<Resultado<PaginaDePedidos>> Listar(OperacionDePedidos operacion, FiltroDePedidos filtro, Func<SolicitanteDePedidos, CriterioDeListado> alcance, string? codigoDeSucursal, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var quien = await autorizador.ExigirRol(operacion, cancelacion).ConfigureAwait(false);
        if (!quien.EsExito)
        {
            return Resultado<PaginaDePedidos>.Fallo(quien.Error!);
        }

        if (codigoDeSucursal is not null && await autorizador.ExigirSucursalDeLaRuta(quien.Valor, codigoDeSucursal, cancelacion).ConfigureAwait(false) is { } fueraDeSucursal)
        {
            return Resultado<PaginaDePedidos>.Fallo(fueraDeSucursal);
        }

        if (await controles.LimiteDeConsultas(quien.Valor, cancelacion).ConfigureAwait(false) is { } limite)
        {
            return Resultado<PaginaDePedidos>.Fallo(limite);
        }

        var pagina = Math.Clamp(filtro.Pagina, 1, 10_000);
        var tamano = Math.Clamp(filtro.Tamano, 1, FiltroDePedidos.TamanoMaximo);
        var criterio = alcance(quien.Valor) with { Estado = filtro.Estado is { } e ? ProyeccionesDePedidos.AEstadoDeDominio(e) : null, Pagina = pagina, Tamano = tamano };
        var resultado = await pedidos.Listar(criterio, cancelacion).ConfigureAwait(false);
        return Resultado<PaginaDePedidos>.Exito(new PaginaDePedidos([.. resultado.Pedidos.Select(ProyeccionesDePedidos.Resumen)], pagina, tamano, resultado.Total));
    }

    // ---------- Detalles ----------

    public async Task<Resultado<DetalleDelPedidoDelCliente>> ObtenerParaElCliente(Guid pedidoId, CancellationToken cancelacion)
    {
        var (pedido, error) = await Cargar(OperacionDePedidos.PedidosDelCliente, pedidoId, codigoDeSucursal: null, cancelacion).ConfigureAwait(false);
        if (pedido is null)
        {
            return Resultado<DetalleDelPedidoDelCliente>.Fallo(error!);
        }

        var plan = pedido.PlanearCancelacionDelCliente();
        var alternativa = plan.Rechazo switch
        {
            RechazoDeAnulacion.IncluyeDigital => AlternativaDeCancelacion.PoliticaDeReembolsoDigital,
            RechazoDeAnulacion.ImpresoYaEnCamino => AlternativaDeCancelacion.Devolucion,
            _ => AlternativaDeCancelacion.Ninguna,
        };
        return Resultado<DetalleDelPedidoDelCliente>.Exito(new DetalleDelPedidoDelCliente(
            ProyeccionesDePedidos.Resumen(pedido),
            pedido.CostoDeEnvio,
            pedido.RegionDeEntrega,
            [.. pedido.Lineas.Select(ProyeccionesDePedidos.Linea)],
            ProyeccionesDePedidos.Contacto(pedido),
            plan.EsValido,
            alternativa,
            await ReembolsoPendiente(pedido, cancelacion).ConfigureAwait(false)));
    }

    public async Task<Resultado<DetalleDelPedidoDeSucursal>> ObtenerParaLaSucursal(string codigoDeSucursal, Guid pedidoId, CancellationToken cancelacion)
    {
        var (pedido, error) = await Cargar(OperacionDePedidos.GestionDeRecojo, pedidoId, codigoDeSucursal ?? string.Empty, cancelacion).ConfigureAwait(false);
        return pedido is null
            ? Resultado<DetalleDelPedidoDeSucursal>.Fallo(error!)
            : Resultado<DetalleDelPedidoDeSucursal>.Exito(new DetalleDelPedidoDeSucursal(ProyeccionesDePedidos.Resumen(pedido), [.. pedido.Lineas.Select(ProyeccionesDePedidos.Linea)]));
    }

    /// <summary>El detalle de logística o de la administración central: con el contacto auditado, que falla cerrado (D09).</summary>
    public async Task<Resultado<DetalleDelPedidoDePersonal>> ObtenerParaElPersonal(OperacionDePedidos operacion, Guid pedidoId, CancellationToken cancelacion)
    {
        if (operacion is not (OperacionDePedidos.GestionDeDespacho or OperacionDePedidos.Administracion))
        {
            throw new ArgumentOutOfRangeException(nameof(operacion), "El detalle con contacto es solo de logística y la administración central.");
        }

        var (pedido, error, solicitante) = await CargarConSolicitante(operacion, pedidoId, null, cancelacion).ConfigureAwait(false);
        if (pedido is null)
        {
            return Resultado<DetalleDelPedidoDePersonal>.Fallo(error!);
        }

        ContactoDeEntregaPublico? contacto = null;
        var oculto = false;
        if (pedido.Contacto is not null)
        {
            contacto = await LeerElContactoAuditado(pedido, solicitante!, cancelacion).ConfigureAwait(false);
            oculto = contacto is null;
        }

        return Resultado<DetalleDelPedidoDePersonal>.Exito(new DetalleDelPedidoDePersonal(
            ProyeccionesDePedidos.Resumen(pedido),
            pedido.CostoDeEnvio,
            pedido.RegionDeEntrega,
            [.. pedido.Lineas.Select(ProyeccionesDePedidos.Linea)],
            contacto,
            oculto,
            await ReembolsoPendiente(pedido, cancelacion).ConfigureAwait(false)));
    }

    private async Task<ContactoDeEntregaPublico?> LeerElContactoAuditado(Pedido pedido, SolicitanteDePedidos solicitante, CancellationToken cancelacion)
    {
        if (!await controles.PuedeLeerElContacto(solicitante, cancelacion).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            await auditoriaDeContacto.ContactoConsultado(pedido.Id, solicitante.Rol, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            // No se puede auditar: se oculta solo el contacto y el resto del detalle se muestra.
            return null;
        }

        return ProyeccionesDePedidos.Contacto(pedido);
    }

    private async Task<(Pedido? Pedido, ErrorDeAplicacion? Error)> Cargar(OperacionDePedidos operacion, Guid pedidoId, string? codigoDeSucursal, CancellationToken cancelacion)
    {
        var (pedido, error, _) = await CargarConSolicitante(operacion, pedidoId, codigoDeSucursal, cancelacion).ConfigureAwait(false);
        return (pedido, error);
    }

    private async Task<(Pedido? Pedido, ErrorDeAplicacion? Error, SolicitanteDePedidos? Solicitante)> CargarConSolicitante(OperacionDePedidos operacion, Guid pedidoId, string? codigoDeSucursal, CancellationToken cancelacion)
    {
        var quien = await autorizador.ExigirRol(operacion, cancelacion).ConfigureAwait(false);
        if (!quien.EsExito)
        {
            return (null, quien.Error, null);
        }

        if (codigoDeSucursal is not null && await autorizador.ExigirSucursalDeLaRuta(quien.Valor, codigoDeSucursal, cancelacion).ConfigureAwait(false) is { } fueraDeSucursal)
        {
            return (null, fueraDeSucursal, null);
        }

        if (await controles.LimiteDeConsultas(quien.Valor, cancelacion).ConfigureAwait(false) is { } limite)
        {
            return (null, limite, null);
        }

        var pedido = await pedidos.Obtener(pedidoId, conBloqueo: false, cancelacion).ConfigureAwait(false);
        if (await autorizador.ComprobarAlcance(pedido, quien.Valor, cancelacion).ConfigureAwait(false) is { } ajeno)
        {
            return (null, ajeno, null);
        }

        return (pedido, null, quien.Valor);
    }

    /// <summary>El reembolso de una anulación que quedó pendiente (la pasarela falló, PED-24): se consulta solo en un pedido anulado y, si Pagos no responde, simplemente no se muestra.</summary>
    private async Task<ReembolsoPendienteDelPedido?> ReembolsoPendiente(Pedido pedido, CancellationToken cancelacion)
    {
        if (pedido.Estado != EstadoDePedido.Anulado)
        {
            return null;
        }

        foreach (var (origen, clave) in new[] { (OrigenDelReembolso.Anulacion, OrquestadorDeAnulaciones.ClaveDelReembolso(OrigenDeLaAnulacion.Administracion, pedido.Id)), (OrigenDelReembolso.CancelacionDelCliente, OrquestadorDeAnulaciones.ClaveDelReembolso(OrigenDeLaAnulacion.Cliente, pedido.Id)) })
        {
            Resultado<ReembolsoPendienteVista?> respuesta;
            try
            {
                respuesta = await reembolsos.ConsultarPorOrigen(origen, clave, cancelacion).ConfigureAwait(false);
            }
            catch (Exception excepcion) when (excepcion is not OperationCanceledException)
            {
                return null;
            }

            if (respuesta.EsExito && respuesta.Valor is { Estado: EstadoDelReembolso.Pendiente or EstadoDelReembolso.Solicitado } pendiente)
            {
                return new ReembolsoPendienteDelPedido(pendiente.Monto);
            }
        }

        return null;
    }
}

/// <summary>Las proyecciones del Dominio a los DTO públicos: solo presentación, ninguna regla. El contacto solo sale por <see cref="Contacto"/> y los listados nunca lo llaman.</summary>
internal static class ProyeccionesDePedidos
{
    public static EstadoDePedido AEstadoDeDominio(EstadoVisibleDelPedido estado) => Enum.Parse<EstadoDePedido>(estado.ToString());

    public static EstadoVisibleDelPedido AEstadoVisible(EstadoDePedido estado) => Enum.Parse<EstadoVisibleDelPedido>(estado.ToString());

    public static TipoDeEntregaVisible Tipo(TipoDeEntregaDelPedido tipo) => Enum.Parse<TipoDeEntregaVisible>(tipo.ToString());

    public static ResumenDePedido Resumen(Pedido pedido) => new(
        pedido.Id,
        pedido.Numero.Valor,
        pedido.CreadoEn,
        AEstadoVisible(pedido.Estado),
        Tipo(pedido.Tipo),
        pedido.CodigoDeSucursalDeRecojo,
        pedido.Total,
        pedido.Lineas.Count);

    public static EstadoVisibleDeLaLinea EstadoVisible(LineaDePedido linea) =>
        linea.EsDigital
            ? linea.EstadoDigital == EstadoDeLineaDigital.Disponible ? EstadoVisibleDeLaLinea.DisponibleEnLaBiblioteca : EstadoVisibleDeLaLinea.Anulado
            : Enum.Parse<EstadoVisibleDeLaLinea>(linea.EstadoImpreso!.Value.ToString());

    public static LineaVisibleDelPedido Linea(LineaDePedido linea) => new(
        linea.Id,
        linea.Titulo,
        linea.Isbn,
        linea.EsDigital,
        linea.Cantidad,
        linea.PrecioUnitario,
        linea.Importe,
        EstadoVisible(linea),
        linea.CodigoDeFuente,
        TrasladoEnCurso(linea),
        linea.ListoEn,
        linea.EntregadaEn,
        linea.AnuladaEn);

    /// <summary>El traslado mientras la línea lo espera: confirmada o en tránsito, con su fecha estimada («a confirmar» si no hay).</summary>
    private static TrasladoVisible? TrasladoEnCurso(LineaDePedido linea) =>
        linea.RequiereTraslado && linea.EstadoImpreso is EstadoDeLineaImpresa.Confirmado or EstadoDeLineaImpresa.EnTransito
            ? new TrasladoVisible(linea.FechaEstimadaDeLlegada, linea.EstadoImpreso == EstadoDeLineaImpresa.EnTransito)
            : null;

    public static ContactoDeEntregaPublico? Contacto(Pedido pedido) =>
        pedido.Contacto is { } c ? new ContactoDeEntregaPublico(c.Nombre, c.Telefono, c.Direccion, pedido.RegionDeEntrega ?? "-") : null;
}
