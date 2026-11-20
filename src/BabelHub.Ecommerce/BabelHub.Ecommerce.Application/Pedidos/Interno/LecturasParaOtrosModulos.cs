using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using ContactoDeEntregaPublico = BabelHub.Ecommerce.Application.Pedidos.ContactoDeEntrega;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>
/// La lectura de pedidos para Envíos (D01 de Envíos; <see cref="IPedidosParaEnvios"/>): sin bloqueo y sin transacción propia (corre en la del llamador: ve las líneas que Pedidos acaba de marcar listas), solo identificadores, la fuente y si cada línea está lista; <b>sin precios, edición ni contacto</b>. Un pedido que no existe es <c>NoEncontrado</c>.
/// </summary>
internal sealed class PedidosParaEnviosDeLaAplicacion(IRepositorioDePedidos pedidos) : IPedidosParaEnvios
{
    public async Task<Resultado<PedidoParaEnvios>> Obtener(Guid pedidoId, CancellationToken cancelacion = default)
    {
        var pedido = pedidoId == Guid.Empty ? null : await pedidos.Obtener(pedidoId, conBloqueo: false, cancelacion).ConfigureAwait(false);
        if (pedido is null)
        {
            return Resultado<PedidoParaEnvios>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, ClavesDePedidos.NoEncontrado));
        }

        return Resultado<PedidoParaEnvios>.Exito(new PedidoParaEnvios(
            pedido.Id,
            pedido.Numero.Valor,
            pedido.CuentaId,
            pedido.Tipo == TipoDeEntregaDelPedido.Domicilio,
            [.. pedido.Lineas.Select(l => new LineaDePedidoParaEnvios(l.Id, l.EsDigital, l.CodigoDeFuente, !l.EsDigital && l.ListoEn is not null))]));
    }
}

/// <summary>
/// La lectura de pedidos para Devoluciones (D01 de Devoluciones; <see cref="IPedidosParaDevoluciones"/>): sin bloqueo ni transacción propia; <b>sin título, fuente, contacto ni el envío</b> (el envío no se reembolsa en una devolución). El listado de una cuenta está acotado a <see cref="IPedidosParaDevoluciones.TamanoMaximo"/> por página y va del más reciente al más antiguo.
/// </summary>
internal sealed class PedidosParaDevolucionesDeLaAplicacion(IRepositorioDePedidos pedidos) : IPedidosParaDevoluciones
{
    public async Task<Resultado<PedidoParaDevoluciones>> Obtener(Guid pedidoId, CancellationToken cancelacion = default)
    {
        var pedido = pedidoId == Guid.Empty ? null : await pedidos.Obtener(pedidoId, conBloqueo: false, cancelacion).ConfigureAwait(false);
        if (pedido is null)
        {
            return Resultado<PedidoParaDevoluciones>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, ClavesDePedidos.NoEncontrado));
        }

        return Resultado<PedidoParaDevoluciones>.Exito(new PedidoParaDevoluciones(
            pedido.Id,
            pedido.Numero.Valor,
            pedido.CuentaId,
            pedido.CreadoEn,
            [.. pedido.Lineas.Select(l => new LineaDePedidoParaDevoluciones(l.Id, l.EdicionId, l.EsDigital, l.Cantidad, l.PrecioUnitario, l.Importe, ProyeccionesDePedidos.EstadoVisible(l), l.EntregadaEn))]));
    }

    public async Task<Resultado<PaginaDePedidosDeUnaCuenta>> ListarDeUnaCuenta(Guid cuentaId, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        if (cuentaId == Guid.Empty)
        {
            return Resultado<PaginaDePedidosDeUnaCuenta>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDePedidos.ValidacionFallida, [new ErrorDeCampo("cuentaId", "obligatorio")]));
        }

        var paginaPedida = Math.Clamp(pagina, 1, 10_000);
        var tamanoPedido = Math.Clamp(tamano, 1, IPedidosParaDevoluciones.TamanoMaximo);
        var resultado = await pedidos.Listar(new CriterioDeListado(cuentaId, null, false, null, paginaPedida, tamanoPedido), cancelacion).ConfigureAwait(false);
        return Resultado<PaginaDePedidosDeUnaCuenta>.Exito(new PaginaDePedidosDeUnaCuenta([.. resultado.Pedidos.Select(p => p.Id)], paginaPedida, tamanoPedido, resultado.Total));
    }
}

/// <summary>
/// La lectura del contacto de entrega para armar la guía (D01 de Envíos; <see cref="IContactoDeEntregaDelPedido"/>; PED-40): solo logística y la administración central (un trabajo de «sistema» o cualquier otro actor no lo lee: «no disponible»); <b>cada lectura se audita con el actor de la solicitud y falla cerrada</b>, igual que su presupuesto de lecturas por persona y hora. Distingue «anonimizado» (la cuenta se canceló o pasaron
/// dos años), «faltante» (el pedido a domicilio no tiene un campo) y «no disponible ahora» (reintentable). Nunca lanza ni deja el contacto en un mensaje.
/// </summary>
internal sealed class ContactoDeEntregaDelPedidoDeLaAplicacion(
    AutorizadorDePedidos autorizador,
    ControlesDeGestionDePedidos controles,
    IRepositorioDePedidos pedidos,
    IAuditoriaDeContacto auditoriaDeContacto) : IContactoDeEntregaDelPedido
{
    public async Task<LecturaDelContactoDeEntrega> Leer(Guid pedidoId, CancellationToken cancelacion = default)
    {
        try
        {
            var quien = await autorizador.ExigirRol(OperacionDePedidos.GestionDeDespacho, cancelacion).ConfigureAwait(false);
            if (!quien.EsExito)
            {
                return LecturaDelContactoDeEntrega.NoDisponible();
            }

            var pedido = pedidoId == Guid.Empty ? null : await pedidos.Obtener(pedidoId, conBloqueo: false, cancelacion).ConfigureAwait(false);
            if (pedido is null || pedido.Tipo != TipoDeEntregaDelPedido.Domicilio)
            {
                return LecturaDelContactoDeEntrega.NoDisponible();
            }

            if (pedido.ContactoEstaAnonimizado)
            {
                return LecturaDelContactoDeEntrega.Anonimizado();
            }

            // El presupuesto de lecturas y la auditoría fallan cerrado: sin ellos no se entrega el contacto.
            if (!await controles.PuedeLeerElContacto(quien.Valor, cancelacion).ConfigureAwait(false))
            {
                return LecturaDelContactoDeEntrega.NoDisponible();
            }

            await auditoriaDeContacto.ContactoConsultado(pedido.Id, quien.Valor.Rol, cancelacion).ConfigureAwait(false);
            if (pedido.Contacto is not { } contacto)
            {
                return LecturaDelContactoDeEntrega.Faltante(CampoDelContactoDeEntrega.Nombre);
            }

            return string.IsNullOrWhiteSpace(pedido.RegionDeEntrega)
                ? LecturaDelContactoDeEntrega.Faltante(CampoDelContactoDeEntrega.Region)
                : LecturaDelContactoDeEntrega.Disponible(new ContactoDeEntregaPublico(contacto.Nombre, contacto.Telefono, contacto.Direccion, pedido.RegionDeEntrega));
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return LecturaDelContactoDeEntrega.NoDisponible();
        }
    }
}
