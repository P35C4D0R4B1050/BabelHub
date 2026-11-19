using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

/// <summary>
/// El paso (c) de la confirmación (T15, D01, D02, D23): <b>una sola transacción</b> que lee con bloqueo la marca de cancelación de la cuenta, bloquea la intención y comprueba que siga aprobada y vinculada, entrega la compra a
/// Pedidos (<see cref="IConfirmacionDeCompra"/>, que crea el pedido y pide los accesos en esta misma transacción), pasa la intención a <c>completada</c> e inserta, también aquí, el aviso <c>PagoAprobado</c> a Carrito (nunca antes) y
/// el aviso de «aprobado» al cliente. Todo resultado distinto de «creado» fuerza la reversión con una excepción controlada <b>sin datos</b>: no queda pedido, accesos ni avisos. Una excepción no controlada se reintenta y no
/// compensa hasta los 30 minutos de <c>aprobado_en</c> (T20).
/// </summary>
internal sealed class CompletarCompra(IRepositorioDePagos repositorio, IUnidadDeTrabajo unidad, BloqueoDeIntenciones bloqueo, IConfirmacionDeCompra confirmacion, IReloj reloj) : ICompletadorDeCompra
{
    public async Task<ResultadoDeCompletarCompra> Completar(Guid pedidoId, Guid intentoId, CancellationToken cancelacion = default)
    {
        try
        {
            return await unidad.Ejecutar(token => CompletarEnLaTransaccion(pedidoId, intentoId, token), cancelacion).ConfigureAwait(false);
        }
        catch (CompraNoCompletableException ex)
        {
            return new ResultadoDeCompletarCompra(TipoDeCompletar.Compensar, ex.Motivo);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una falla no controlada (la base, Pedidos o Biblioteca): la transacción se revirtió y la compra sigue aprobada para la retoma.
            return new ResultadoDeCompletarCompra(TipoDeCompletar.Reintentar);
        }
    }

    private async Task<ResultadoDeCompletarCompra> CompletarEnLaTransaccion(Guid pedidoId, Guid intentoId, CancellationToken token)
    {
        var (intencion, cancelacionEnCurso) = await bloqueo.Bloquear(pedidoId, token).ConfigureAwait(false);
        switch (intencion?.Estado)
        {
            case null:
                return new ResultadoDeCompletarCompra(TipoDeCompletar.Reintentar);
            case EstadoDeIntencion.Completada:
                return new ResultadoDeCompletarCompra(TipoDeCompletar.Completada);
            case not EstadoDeIntencion.Aprobada:
                return new ResultadoDeCompletarCompra(TipoDeCompletar.Reintentar);
        }

        // Desvinculada (PAG-29) o con la cancelación de la cuenta en ejecución (PAG-32): no se crea el pedido y se compensa sin avisar.
        if (Compensacion.Clasificar(intencion, cancelacionEnCurso) is { } motivo)
        {
            return new ResultadoDeCompletarCompra(TipoDeCompletar.Compensar, motivo);
        }

        var ahora = reloj.Ahora;
        var resultado = await confirmacion.Confirmar(CompraAprobadaDe(intencion), token).ConfigureAwait(false);
        switch (resultado)
        {
            case ResultadoDeConfirmarCompra.Creado:
                break;
            case ResultadoDeConfirmarCompra.CuentaEnCancelacion:
                throw new CompraNoCompletableException(MotivoDeCompensacion.CancelacionDeCuentaEnCurso);
            default:
                throw new CompraNoCompletableException(MotivoDeCompensacion.FallaPosteriorAlPago);
        }

        if (intencion.Completar(ahora) != ResultadoDeCompletar.Completada)
        {
            throw new CompraNoCompletableException(MotivoDeCompensacion.IntencionDesvinculada);
        }

        await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);
        await repositorio.AgregarAvisoACarrito(AvisoACarrito.Crear(pedidoId, intentoId, TransicionDeAviso.Aprobado, retenido: false, ahora), token).ConfigureAwait(false);
        await repositorio.AgregarAvisoDePago(AvisoDePago.Crear(pedidoId, intentoId, EventoDeAvisoDePago.Aprobado, ahora), token).ConfigureAwait(false);
        return new ResultadoDeCompletarCompra(TipoDeCompletar.Completada);
    }

    /// <summary>Lo que Pagos entrega a Pedidos (D02): las líneas con su importe, la fuente y los traslados que devolvió el Inventario, el envío, el total, el contacto y los textos aceptados.</summary>
    private static CompraAprobada CompraAprobadaDe(IntencionDePago intencion)
    {
        var cuentaId = intencion.CuentaId ?? throw new InvalidOperationException("Una compra aprobada sin cuenta no se puede completar.");
        if (intencion.TieneImpresos && intencion.ReservasConfirmadasEn is null)
        {
            throw new InvalidOperationException("Las reservas de los impresos aún no se confirmaron.");
        }

        var confirmadas = intencion.LineasConfirmadas.ToDictionary(l => l.LineaId);
        var lineas = new List<LineaDeCompraAprobada>(intencion.Lineas.Count);
        foreach (var linea in intencion.Lineas)
        {
            ReservaConfirmadaDeLinea? reserva = null;
            if (!linea.EsDigital && !confirmadas.TryGetValue(linea.LineaId, out reserva))
            {
                throw new InvalidOperationException("Falta la fuente confirmada de una línea impresa.");
            }

            lineas.Add(new LineaDeCompraAprobada(
                linea.LineaId,
                linea.EdicionId ?? throw new InvalidOperationException("La línea perdió su edición."),
                linea.EsDigital,
                linea.Titulo ?? throw new InvalidOperationException("La línea perdió su título."),
                linea.Isbn,
                linea.Cantidad,
                linea.PrecioUnitario.Valor,
                linea.Importe.Valor,
                reserva?.CodigoDeFuente,
                reserva?.RequiereTraslado ?? false,
                reserva?.TrasladoId,
                reserva?.FechaEstimadaDeLlegada));
        }

        var entrega = intencion.TipoDeEntrega ?? throw new InvalidOperationException("La compra perdió su tipo de entrega.");
        ContactoDeLaCompra? contacto = null;
        if (entrega == TipoDeEntrega.Domicilio)
        {
            var datos = intencion.Contacto ?? throw new InvalidOperationException("La compra a domicilio perdió su contacto.");
            contacto = new ContactoDeLaCompra(datos.Nombre, datos.Telefono, datos.Direccion, intencion.RegionDeEntrega ?? throw new InvalidOperationException("La compra a domicilio perdió su región."));
        }

        return new CompraAprobada(
            intencion.Id,
            cuentaId,
            entrega switch
            {
                TipoDeEntrega.Recojo => TipoDeEntregaDeCompra.Recojo,
                TipoDeEntrega.Domicilio => TipoDeEntregaDeCompra.Domicilio,
                _ => TipoDeEntregaDeCompra.Digital,
            },
            intencion.CodigoDeSucursalDeRecojo,
            intencion.RegionDeEntrega,
            intencion.Envio.Valor,
            intencion.Total.Valor,
            contacto,
            Aceptaciones(intencion),
            lineas);
    }

    private static List<AceptacionDeCompra> Aceptaciones(IntencionDePago intencion)
    {
        var aceptaciones = new List<AceptacionDeCompra>(3);
        Agregar(aceptaciones, TextoLegalDeCompra.Terminos, intencion.Terminos);
        Agregar(aceptaciones, TextoLegalDeCompra.Privacidad, intencion.Privacidad);
        Agregar(aceptaciones, TextoLegalDeCompra.ReembolsoDigital, intencion.ReembolsoDigital);
        return aceptaciones;
    }

    private static void Agregar(List<AceptacionDeCompra> aceptaciones, TextoLegalDeCompra texto, TextoAceptado? aceptado)
    {
        if (aceptado is not null)
        {
            aceptaciones.Add(new AceptacionDeCompra(texto, aceptado.Version, aceptado.AceptadoEn));
        }
    }

    /// <summary>La excepción controlada que fuerza la reversión: lleva solo el motivo, ningún dato de la compra ni de la persona.</summary>
    private sealed class CompraNoCompletableException(MotivoDeCompensacion motivo) : Exception("La compra no se pudo completar.")
    {
        public MotivoDeCompensacion Motivo { get; } = motivo;
    }
}
