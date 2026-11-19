using System.Globalization;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using ContactoDeEntregaDeDominio = BabelHub.Ecommerce.Domain.Pedidos.ContactoDeEntrega;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>
/// La implementación de <see cref="IConfirmacionDeCompra"/> (T11; PED-01, PED-03, PED-12, PED-32, PED-35, PED-36, PED-38): crea el pedido desde <see cref="CompraAprobada"/> <b>dentro de la transacción de Pagos</b> (no abre otra) y pide los accesos digitales a Biblioteca en la misma, de modo que una falla
/// revierte ambos y Pagos compensa. Orden: (a) <b>sin bloqueo</b>, si el pedido ya existe por <c>id</c> devuelve <c>Creado</c> (una repetición nunca produce <c>CuentaEnCancelacion</c>: Pagos compensaría un pedido que sí existe); (b) si no existe, lee con bloqueo la marca de cancelación y, si la cuenta se
/// está cancelando, responde <c>CuentaEnCancelacion</c> sin crear nada (PED-38); (c) arma el pedido (el contacto de una compra a domicilio es obligatorio: sin él, <c>Fallo</c> y nada se crea, PED-36); (d) lo inserta con <c>ON CONFLICT DO NOTHING</c> (una colisión de <c>numero</c> se reintenta con un
/// número nuevo y una de <c>id</c> es <c>Creado</c>); (e) concede los accesos de las líneas digitales (<c>YaPoseidoPorOtraCompra</c> responde <c>DigitalYaPoseida</c>); (f) registra la liberación del contacto pendiente (PED-35) y (g) audita la creación en la misma transacción. No solicita avisos: Pagos ya crea el de «aprobado».
/// </summary>
public sealed class ConfirmarCompra(
    IRepositorioDePedidos pedidos,
    IMarcaDeCancelacionDeCuenta marcas,
    IAccesosDeBiblioteca accesos,
    IGeneradorDeNumerosDePedido numeros,
    IAuditoria auditoria,
    IReloj reloj) : IConfirmacionDeCompra
{
    public const int IntentosDeNumero = 5;

    public async Task<ResultadoDeConfirmarCompra> Confirmar(CompraAprobada compra, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(compra);

        // (a) Una repetición sobre un pedido que ya existe es Creado, aunque la cuenta ya se esté cancelando.
        if (await pedidos.Existe(compra.PedidoId, cancelacion).ConfigureAwait(false))
        {
            return ResultadoDeConfirmarCompra.Creado;
        }

        // (b) La marca de cancelación se lee con bloqueo antes de crear nada (PED-38).
        var marca = await marcas.LeerConBloqueo(compra.CuentaId, cancelacion).ConfigureAwait(false);
        if (marca is EstadoDeLaMarca.EnEjecucion or EstadoDeLaMarca.Ejecutada)
        {
            return ResultadoDeConfirmarCompra.CuentaEnCancelacion;
        }

        // (c) El pedido, con su contacto obligatorio a domicilio.
        var ahora = reloj.Ahora;
        var datos = ArmarLosDatos(compra, ahora);
        if (datos is null)
        {
            return ResultadoDeConfirmarCompra.Fallo;
        }

        // (d) Insertar con reintento de la colisión del número.
        Pedido? pedido = null;
        for (var intento = 0; intento < IntentosDeNumero && pedido is null; intento++)
        {
            var creado = Pedido.Crear(datos with { Numero = numeros.Siguiente().Valor });
            if (!creado.Creado)
            {
                return ResultadoDeConfirmarCompra.Fallo;
            }

            if (await pedidos.Agregar(creado.Pedido!, cancelacion).ConfigureAwait(false))
            {
                pedido = creado.Pedido;
            }
            else if (await pedidos.Existe(compra.PedidoId, cancelacion).ConfigureAwait(false))
            {
                return ResultadoDeConfirmarCompra.Creado;
            }
        }

        if (pedido is null)
        {
            return ResultadoDeConfirmarCompra.Fallo;
        }

        // (e) Los accesos digitales, en la misma transacción.
        foreach (var linea in compra.Lineas.Where(l => l.EsDigital))
        {
            var acceso = await accesos.Conceder(new ConcederAccesoEntrada(compra.CuentaId, compra.PedidoId, linea.EdicionId), cancelacion).ConfigureAwait(false);
            if (!acceso.EsExito)
            {
                return ResultadoDeConfirmarCompra.Fallo;
            }

            if (acceso.Valor == ResultadoDeConcederAcceso.YaPoseidoPorOtraCompra)
            {
                return ResultadoDeConfirmarCompra.DigitalYaPoseida;
            }
        }

        // (f) La liberación del contacto de la intención se pide después de confirmar y se reintenta (PED-35).
        if (pedido.Contacto is not null)
        {
            await pedidos.RegistrarLiberacionDeContacto(pedido.Id, ahora, cancelacion).ConfigureAwait(false);
        }

        // (g) La creación se audita en la misma transacción; si no se puede, no se crea (AUD-04).
        try
        {
            await auditoria.Registrar(
                new SolicitudDeRegistro(AccionesAuditables.EstadoDePedidoCambiado)
                {
                    ObjetoTipo = "pedido",
                    ObjetoId = pedido.Id.ToString("D"),
                    Detalle = new Dictionary<string, string?> { ["estadoAnterior"] = "ninguno", ["estadoNuevo"] = NombreDelEstado(pedido.Estado) },
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (AuditoriaNoDisponibleException)
        {
            return ResultadoDeConfirmarCompra.Fallo;
        }

        return ResultadoDeConfirmarCompra.Creado;
    }

    private static DatosDePedidoNuevo? ArmarLosDatos(CompraAprobada compra, DateTimeOffset ahora)
    {
        var tipo = compra.TipoDeEntrega switch
        {
            TipoDeEntregaDeCompra.Recojo => TipoDeEntregaDelPedido.Recojo,
            TipoDeEntregaDeCompra.Domicilio => TipoDeEntregaDelPedido.Domicilio,
            TipoDeEntregaDeCompra.Digital => TipoDeEntregaDelPedido.Digital,
            _ => (TipoDeEntregaDelPedido?)null,
        };
        if (tipo is null)
        {
            return null;
        }

        ContactoDeEntregaDeDominio? contacto = null;
        if (compra.Contacto is { } entregado)
        {
            contacto = ContactoDeEntregaDeDominio.Crear(entregado.Nombre, entregado.Telefono, entregado.Direccion);
            if (contacto is null)
            {
                // Un contacto incompleto o inválido no se guarda: el pedido a domicilio no se crea (PED-36).
                return null;
            }
        }

        AceptacionDeTexto? Aceptacion(TextoLegalDeCompra texto) =>
            compra.Aceptaciones.FirstOrDefault(a => a.Texto == texto) is { } a ? new AceptacionDeTexto(a.Version, a.AceptadoEn) : null;

        return new DatosDePedidoNuevo(
            compra.PedidoId,
            string.Empty,
            compra.CuentaId,
            tipo.Value,
            compra.CodigoDeSucursalDeRecojo,
            compra.RegionDeEntrega,
            compra.Envio,
            compra.Total,
            contacto,
            Aceptacion(TextoLegalDeCompra.Terminos)!,
            Aceptacion(TextoLegalDeCompra.Privacidad)!,
            Aceptacion(TextoLegalDeCompra.ReembolsoDigital),
            [.. compra.Lineas.Select(l => new DatosDeLineaNueva(l.LineaId, l.EdicionId, l.EsDigital, l.Titulo, l.Isbn, l.Cantidad, l.PrecioUnitario, l.Importe, l.CodigoDeFuente, l.RequiereTraslado, l.TrasladoId, l.FechaEstimadaDeLlegada))],
            ahora);
    }

    private static string NombreDelEstado(EstadoDePedido estado) => estado switch
    {
        EstadoDePedido.Entregado => "entregado",
        _ => "confirmado",
    };
}
