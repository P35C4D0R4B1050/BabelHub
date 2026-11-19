using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using Tipo = BabelHub.Ecommerce.Domain.Carrito.TipoDeEntrega;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

public enum DesenlaceDeLaIntencion
{
    /// <summary>La intención se creó o se reemplazó (o ya estaba con la misma huella: idempotente).</summary>
    Creada,

    /// <summary>Hay un intento activo y la instantánea es otra u obsoleta: se devuelve el estado actual, sin abortar.</summary>
    Conflicto,

    /// <summary>La intención ya está aprobada o completada: se redirige a la pantalla de estado de Pagos, sin abortar ni liberar la reserva.</summary>
    YaEnCurso,

    /// <summary>La intención está cerrada sin cobro o compensada: se aborta y el siguiente intento usa un <c>pedidoId</c> nuevo.</summary>
    Cerrada,

    /// <summary>La cuenta no está activa, la cancelación está en marcha, el checkout ya no existe o cambió de pedido, generación o huella, o Pagos no respondió: se aborta como indica el plan (condicional a lo que la solicitud tenía).</summary>
    Abortar,
}

public sealed record ResultadoDeLaIntencion(DesenlaceDeLaIntencion Desenlace, ErrorDeAplicacion? Error = null, string? MotivoDelAborto = null, bool IntencionCreada = false);

/// <summary>
/// El paso 3a de pasar al pago (D18; T19a; CAR-10): en <b>su propia transacción corta</b> lee con bloqueo la marca de cancelación de la cuenta (lo primero, D10) y llama a <c>ICreadorDeIntencionesDePago.Crear</c> con la cuenta, la instantánea, su
/// huella y la expiración de la reserva, que inserta o reemplaza la intención y renueva sus plazos. <b>No retiene el bloqueo del checkout</b>: solo lo lee, sin bloqueo, para comprobar que sigue <c>reservando</c> con el mismo <c>pedidoId</c>, generación
/// y huella (3b lo bloquea después). Toda falla (cuenta no activa, checkout cambiado, Pagos sin respuesta) es <see cref="DesenlaceDeLaIntencion.Abortar"/>; <c>Crear</c> con <c>Cerrada</c> lleva al aborto con un <c>pedidoId</c> nuevo en el siguiente intento.
/// </summary>
internal sealed class SolicitarIntencionDePago(ServiciosDelCheckout servicios)
{
    public async Task<ResultadoDeLaIntencion> Ejecutar(PasoAlPagoPreparado preparado, DateTimeOffset? reservaExpiraEn, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(preparado);
        var carrito = servicios.Carrito;
        var expira = reservaExpiraEn ?? carrito.Reloj.Ahora.AddMinutes(15);
        var creada = false;
        try
        {
            return await carrito.Unidad.Ejecutar(
                async token =>
                {
                    var marca = await servicios.Marca.LeerConBloqueo(preparado.CuentaId, token).ConfigureAwait(false);
                    if (marca != EstadoDeLaMarcaDeCancelacion.Libre)
                    {
                        return Abortar("cuenta-no-activa", new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDelCarrito.CuentaNoPuedeComprar, [new ErrorDeCampo("motivo", "cancelacion-en-curso")]));
                    }

                    var checkout = await carrito.Checkouts.ObtenerVivoDeCuenta(preparado.CuentaId, conBloqueo: false, token).ConfigureAwait(false);
                    if (checkout is not { Estado: EstadoDelCheckout.Reservando or EstadoDelCheckout.EnPago }
                        || checkout.PedidoId != preparado.PedidoId || checkout.GeneracionDeReserva != preparado.Generacion || checkout.HuellaDeInstantanea != preparado.HuellaDeInstantanea)
                    {
                        return Abortar("checkout-cambio", new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.EnCurso));
                    }

                    creada = true;
                    var respuesta = await carrito.Edicion.Pagos.Crear(preparado.CuentaId, preparado.Instantanea!, preparado.HuellaDeInstantanea!, expira, token).ConfigureAwait(false);
                    return new ResultadoDeLaIntencion(
                        respuesta switch
                        {
                            ResultadoDeCrearIntencion.Creada => DesenlaceDeLaIntencion.Creada,
                            ResultadoDeCrearIntencion.Conflicto => DesenlaceDeLaIntencion.Conflicto,
                            ResultadoDeCrearIntencion.YaEnCurso => DesenlaceDeLaIntencion.YaEnCurso,
                            _ => DesenlaceDeLaIntencion.Cerrada,
                        },
                        IntencionCreada: respuesta == ResultadoDeCrearIntencion.Creada);
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            // Pagos no respondió o la transacción falló: no se sabe si la intención llegó a crearse, así que el aborto la cancela por si acaso.
            return Abortar("pagos-no-respondio", new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDelCarrito.ServicioNoDisponible), intencionCreada: creada);
        }
    }

    private static ResultadoDeLaIntencion Abortar(string motivo, ErrorDeAplicacion error, bool intencionCreada = false) =>
        new(DesenlaceDeLaIntencion.Abortar, error, motivo, intencionCreada);
}

public enum DesenlaceDeLaConfirmacion
{
    /// <summary>El checkout está <c>en_pago</c> con la expiración de la reserva (o ya lo estaba con el mismo pedido y huella: idempotente).</summary>
    EnPago,

    /// <summary>Hay un retiro pendiente: el checkout NO cambió de estado; quien llama reabre la intención fuera de la transacción y hace el cambio local (<c>reservando → en_edicion</c>).</summary>
    ConRetiroPendiente,

    /// <summary>La cuenta ya no está activa, la cancelación está en marcha o el checkout cambió de pedido, generación o huella: se aborta (condicional).</summary>
    Abortar,
}

public sealed record ResultadoDeLaConfirmacion(DesenlaceDeLaConfirmacion Desenlace, ErrorDeAplicacion? Error = null, string? MotivoDelAborto = null);

/// <summary>
/// El paso 3b de pasar al pago (D18; T19a; CAR-10): en su transacción corta relee la marca de cancelación con bloqueo, bloquea el checkout y el carrito (D10), comprueba que siga <c>reservando</c> con el mismo <c>pedidoId</c>, generación y huella, pasa a
/// <c>en_pago</c> con la expiración de la reserva y <b>audita en la misma transacción</b> (si no se puede, falla). Ya <c>en_pago</c> con el mismo pedido y huella es éxito idempotente (no audita otra vez). Con una línea con <c>retiro_pendiente</c> <b>no
/// cambia el estado</b> y <b>nunca llama a Pagos</b> dentro de esta transacción: el orquestador reabre fuera de ella y hace el cambio local condicional.
/// </summary>
internal sealed class ConfirmarEnPago(ServiciosDelCheckout servicios)
{
    public async Task<ResultadoDeLaConfirmacion> Ejecutar(PasoAlPagoPreparado preparado, DateTimeOffset? reservaExpiraEn, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(preparado);
        var carrito = servicios.Carrito;
        var expira = reservaExpiraEn ?? carrito.Reloj.Ahora.AddMinutes(15);
        return await carrito.Unidad.Ejecutar(
            async token =>
            {
                var ahora = carrito.Reloj.Ahora;
                var marca = await servicios.Marca.LeerConBloqueo(preparado.CuentaId, token).ConfigureAwait(false);
                if (marca != EstadoDeLaMarcaDeCancelacion.Libre)
                {
                    return Abortar("cuenta-no-activa", new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDelCarrito.CuentaNoPuedeComprar, [new ErrorDeCampo("motivo", "cancelacion-en-curso")]));
                }

                var checkout = await carrito.Checkouts.ObtenerVivoDeCuenta(preparado.CuentaId, conBloqueo: true, token).ConfigureAwait(false);
                var delCarrito = await carrito.Carritos.ObtenerDeCuenta(preparado.CuentaId, conBloqueo: true, token).ConfigureAwait(false);
                if (checkout is null || delCarrito is null
                    || checkout.PedidoId != preparado.PedidoId || checkout.GeneracionDeReserva != preparado.Generacion || checkout.HuellaDeInstantanea != preparado.HuellaDeInstantanea)
                {
                    return Abortar("checkout-cambio", new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.EnCurso));
                }

                var efecto = checkout.ConfirmarEnPago(preparado.PedidoId!.Value, preparado.Generacion, preparado.HuellaDeInstantanea!, expira, delCarrito);
                switch (efecto.Efecto)
                {
                    case EfectoDeTransicion.Aplicada:
                        await carrito.Checkouts.Guardar(checkout, token).ConfigureAwait(false);
                        await carrito.Auditoria.PasoAlPagoIniciado(!checkout.EsSoloDigital, checkout.TipoDeEntrega == Tipo.Domicilio, token).ConfigureAwait(false);
                        return new ResultadoDeLaConfirmacion(DesenlaceDeLaConfirmacion.EnPago);
                    case EfectoDeTransicion.SinCambios:
                        return new ResultadoDeLaConfirmacion(DesenlaceDeLaConfirmacion.EnPago);
                    case EfectoDeTransicion.ConRetiroPendiente:
                        return new ResultadoDeLaConfirmacion(DesenlaceDeLaConfirmacion.ConRetiroPendiente);
                    default:
                        return Abortar("checkout-cambio", new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.EnCurso));
                }
            },
            cancelacion).ConfigureAwait(false);
    }

    private static ResultadoDeLaConfirmacion Abortar(string motivo, ErrorDeAplicacion error) => new(DesenlaceDeLaConfirmacion.Abortar, error, motivo);
}
