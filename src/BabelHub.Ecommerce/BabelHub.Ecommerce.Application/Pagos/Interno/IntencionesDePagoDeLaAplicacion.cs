using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

/// <summary>Convierte la instantánea que Carrito congela (modelo público de Carrito) en la del Dominio de Pagos (D02): importes en soles, entrega y textos aceptados.</summary>
internal static class MapeoDeInstantaneas
{
    public static InstantaneaDeIntencion ADominio(InstantaneaDeCompra instantanea, string huella)
    {
        ArgumentNullException.ThrowIfNull(instantanea);
        var lineas = instantanea.Lineas
            .Select(l => new LineaDeIntencion(l.LineaId, l.EdicionId, l.EsDigital, l.Titulo, l.Isbn, l.Cantidad, Importe.De(l.PrecioUnitario), Importe.De(l.Importe)))
            .ToArray();
        var entrega = instantanea.TipoDeEntrega switch
        {
            TipoDeEntregaDeCompra.Recojo => TipoDeEntrega.Recojo,
            TipoDeEntregaDeCompra.Domicilio => TipoDeEntrega.Domicilio,
            TipoDeEntregaDeCompra.Digital => TipoDeEntrega.SinEntrega,
            _ => throw new ArgumentOutOfRangeException(nameof(instantanea)),
        };

        return InstantaneaDeIntencion.Crear(
            instantanea.CuentaId,
            entrega,
            entrega == TipoDeEntrega.Recojo ? instantanea.CodigoDeSucursalDeRecojo : null,
            entrega == TipoDeEntrega.Domicilio ? instantanea.RegionDeEntrega ?? instantanea.Contacto?.Region : null,
            lineas,
            Importe.De(instantanea.Envio),
            Importe.De(instantanea.Total),
            huella,
            Texto(instantanea, TextoLegalDeCompra.Terminos) ?? throw new ArgumentException("La instantánea no trae los términos aceptados.", nameof(instantanea)),
            Texto(instantanea, TextoLegalDeCompra.Privacidad) ?? throw new ArgumentException("La instantánea no trae la política de privacidad aceptada.", nameof(instantanea)),
            Texto(instantanea, TextoLegalDeCompra.ReembolsoDigital),
            entrega == TipoDeEntrega.Domicilio && instantanea.Contacto is { } c ? new ContactoDeEntrega(c.Nombre, c.Telefono, c.Direccion) : null,
            // Tal como llega (también donde no corresponde, para que el Dominio lo rechace): solo el código y el importe; un importe inválido no llega a serlo.
            instantanea.EnviosPorSucursal?.Select(e => new EnvioDeSucursalDeIntencion(e.CodigoDeSucursal, Importe.De(e.Importe))).ToArray());
    }

    private static TextoAceptado? Texto(InstantaneaDeCompra instantanea, TextoLegalDeCompra texto) =>
        instantanea.Aceptaciones.FirstOrDefault(a => a.Texto == texto) is { } a ? new TextoAceptado(a.Version, a.AceptadoEn) : null;
}

/// <summary>
/// Pagos implementa lo que Carrito declara (D01, D07, D21): <c>Crear</c>, <c>Reabrir</c> y <c>Cancelar</c> la intención de pago de una compra. Los tres comprueban la propiedad por la cuenta y bloquean la intención
/// (<c>FOR UPDATE</c>) en una transacción corta, sin ninguna llamada de red. Una intención ajena responde igual que una inexistente y deja un acceso denegado (SEG-09).
/// </summary>
internal sealed class IntencionesDePagoDeLaAplicacion(IRepositorioDePagos repositorio, IUnidadDeTrabajo unidad, IReloj reloj, IDenegacionDeAcceso denegaciones) : ICreadorDeIntencionesDePago
{
    public async Task<ResultadoDeCrearIntencion> Crear(Guid cuentaId, InstantaneaDeCompra instantanea, string huellaDeInstantanea, DateTimeOffset reservaExpiraEn, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(instantanea);
        if (cuentaId == Guid.Empty || instantanea.CuentaId != cuentaId || string.IsNullOrWhiteSpace(huellaDeInstantanea) || instantanea.PedidoId == Guid.Empty)
        {
            throw new ArgumentException("La instantánea es de otra cuenta o le faltan su pedido o su huella.", nameof(instantanea));
        }

        var dominio = MapeoDeInstantaneas.ADominio(instantanea, huellaDeInstantanea);
        var ahora = reloj.Ahora;
        DateTimeOffset? reserva = dominio.TieneImpresos ? reservaExpiraEn : null;

        var (resultado, ajena) = await unidad.Ejecutar(
            async token =>
            {
                var existente = await repositorio.ObtenerIntencion(instantanea.PedidoId, paraActualizar: true, token).ConfigureAwait(false);
                if (existente is null)
                {
                    await repositorio.AgregarIntencion(IntencionDePago.Crear(instantanea.PedidoId, dominio, reserva, ahora), token).ConfigureAwait(false);
                    return (ResultadoDeCrearIntencion.Creada, false);
                }

                if (!existente.EstaDesvinculada && !existente.EsDeLaCuenta(cuentaId))
                {
                    return (ResultadoDeCrearIntencion.Cerrada, true);
                }

                var resultadoDeDominio = existente.Reemplazar(dominio, reserva, ahora);
                if (resultadoDeDominio == ResultadoDeCrear.Reemplazada)
                {
                    await repositorio.GuardarIntencion(existente, token).ConfigureAwait(false);
                }

                return (Traducir(resultadoDeDominio), false);
            },
            cancelacion).ConfigureAwait(false);

        if (ajena)
        {
            await Denegar(cancelacion).ConfigureAwait(false);
            throw new InvalidOperationException("El pedido de la intención es de otra cuenta.");
        }

        return resultado;
    }

    public async Task<ResultadoDeReabrirIntencion> Reabrir(Guid cuentaId, Guid pedidoId, CancellationToken cancelacion = default)
    {
        var (resultado, ajena) = await unidad.Ejecutar(
            async token =>
            {
                var intencion = await repositorio.ObtenerIntencion(pedidoId, paraActualizar: true, token).ConfigureAwait(false);
                if (intencion is null)
                {
                    return (ResultadoDeReabrirIntencion.NoExiste, false);
                }

                if (!intencion.EsDeLaCuenta(cuentaId))
                {
                    return (ResultadoDeReabrirIntencion.NoExiste, true);
                }

                var reapertura = intencion.Reabrir();
                if (reapertura == ResultadoDeReapertura.Reabierta)
                {
                    await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);
                }

                return (reapertura switch
                {
                    ResultadoDeReapertura.Reabierta => ResultadoDeReabrirIntencion.Reabierta,
                    ResultadoDeReapertura.Cerrada => ResultadoDeReabrirIntencion.Cerrada,
                    _ => ResultadoDeReabrirIntencion.NoCancelable,
                }, false);
            },
            cancelacion).ConfigureAwait(false);

        if (ajena)
        {
            await Denegar(cancelacion).ConfigureAwait(false);
        }

        return resultado;
    }

    public async Task<ResultadoDeCancelarIntencion> Cancelar(Guid cuentaId, Guid pedidoId, CancellationToken cancelacion = default)
    {
        var (resultado, ajena) = await unidad.Ejecutar(
            async token =>
            {
                var intencion = await repositorio.ObtenerIntencion(pedidoId, paraActualizar: true, token).ConfigureAwait(false);
                if (intencion is null)
                {
                    return (ResultadoDeCancelarIntencion.NoExiste, false);
                }

                if (!intencion.EsDeLaCuenta(cuentaId))
                {
                    return (ResultadoDeCancelarIntencion.NoExiste, true);
                }

                var cancelacionDeDominio = intencion.Cancelar(reloj.Ahora);
                if (cancelacionDeDominio == ResultadoDeCancelacion.Cancelada)
                {
                    await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);
                }

                return (cancelacionDeDominio switch
                {
                    ResultadoDeCancelacion.Cancelada => ResultadoDeCancelarIntencion.Cancelada,
                    ResultadoDeCancelacion.Cerrada => ResultadoDeCancelarIntencion.Cerrada,
                    _ => ResultadoDeCancelarIntencion.NoCancelable,
                }, false);
            },
            cancelacion).ConfigureAwait(false);

        if (ajena)
        {
            await Denegar(cancelacion).ConfigureAwait(false);
        }

        return resultado;
    }

    private static ResultadoDeCrearIntencion Traducir(ResultadoDeCrear resultado) => resultado switch
    {
        ResultadoDeCrear.Creada or ResultadoDeCrear.Reemplazada => ResultadoDeCrearIntencion.Creada,
        ResultadoDeCrear.Conflicto => ResultadoDeCrearIntencion.Conflicto,
        ResultadoDeCrear.YaEnCurso => ResultadoDeCrearIntencion.YaEnCurso,
        ResultadoDeCrear.Cerrada => ResultadoDeCrearIntencion.Cerrada,
        _ => throw new ArgumentOutOfRangeException(nameof(resultado)),
    };

    /// <summary>Una intención ajena se trata como inexistente y deja un acceso denegado, siempre después de cerrar la transacción.</summary>
    private Task Denegar(CancellationToken cancelacion) =>
        denegaciones.Registrar(new DenegacionDeAcceso("recurso-ajeno", ruta: null) { CodigoDeError = "ECO-NO-ENCONTRADO" }, cancelacion);
}
