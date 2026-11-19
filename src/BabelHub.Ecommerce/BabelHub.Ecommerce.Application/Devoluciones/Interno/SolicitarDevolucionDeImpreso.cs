using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;

namespace BabelHub.Ecommerce.Application.Devoluciones.Interno;

/// <summary>Un rechazo que ocurre <b>dentro</b> de la transacción y debe revertirla entera (por ejemplo, el cupo no alcanzó después de comprometer otras líneas): se lanza para revertir y el caso de uso lo traduce al error, sin dejar ningún registro.</summary>
internal sealed class RechazoQueRevierteException(ErrorDeAplicacion error) : Exception("La operación se rechazó y revierte su transacción.")
{
    public ErrorDeAplicacion Error { get; } = error;
}

internal static class MapeoDeDevoluciones
{
    public static EstadoDeLaLineaDeDevolucion Estado(EstadoDeLineaDeDevolucion estado) => estado switch
    {
        EstadoDeLineaDeDevolucion.Solicitada => EstadoDeLaLineaDeDevolucion.Solicitada,
        EstadoDeLineaDeDevolucion.Aprobada => EstadoDeLaLineaDeDevolucion.Aprobada,
        EstadoDeLineaDeDevolucion.Rechazada => EstadoDeLaLineaDeDevolucion.Rechazada,
        EstadoDeLineaDeDevolucion.Recibida => EstadoDeLaLineaDeDevolucion.Recibida,
        EstadoDeLineaDeDevolucion.Reembolsada => EstadoDeLaLineaDeDevolucion.Reembolsada,
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };

    /// <summary>La devolución para quien la ve: sin el responsable de ningún cambio, sin la cuenta y sin la referencia al acceso (DEV-28).</summary>
    public static DevolucionVista Vista(Devolucion d) =>
        new(
            d.Id,
            d.PedidoId,
            d.Tipo == TipoDeDevolucion.Impreso ? TipoDeLaDevolucion.Impreso : TipoDeLaDevolucion.Digital,
            d.Origen == OrigenDeDevolucion.Solicitud ? OrigenDeLaDevolucion.Solicitud : OrigenDeLaDevolucion.Reporte,
            d.CodigoDeSucursalReceptora,
            new TextoLibre(d.Motivo.Valor),
            d.Texto is null ? null : new TextoLibre(d.Texto.Valor),
            d.RedactadaEn is not null,
            d.CreadaEn,
            [.. d.Lineas.Select(l => new LineaDeDevolucionVista(l.LineaDePedidoId, l.EdicionId, l.Cantidad, l.Monto, Estado(l.Estado), l.EstadoEn, ReembolsoPendiente(d, l)))]);

    /// <summary>Un reembolso pendiente: una línea recibida de un impreso o aprobada de un digital hasta que la pasarela confirma (DEV-21).</summary>
    public static bool ReembolsoPendiente(Devolucion d, LineaDeDevolucion l) =>
        d.Tipo == TipoDeDevolucion.Impreso ? l.Estado == EstadoDeLineaDeDevolucion.Recibida : l.Estado == EstadoDeLineaDeDevolucion.Aprobada;

    public static ErrorDeAplicacion ValidacionDeCampo(string campo, string clave) =>
        new(CategoriaDeError.Validacion, ClavesDeDevoluciones.ValidacionFallida, [new ErrorDeCampo(campo, clave)]);
}

/// <summary>
/// <c>SolicitarDevolucionDeImpreso</c> (T06; DEV-01, DEV-02, DEV-03, DEV-15, DEV-18, DEV-19, DEV-26; plan §1): con el orden fijo rol → textos → límite → <b>propiedad del pedido</b> (antes de cualquier llamada al Inventario) → sucursal receptora
/// contra las activas del Inventario <b>fuera de transacción</b> → líneas y plazos → <b>una transacción</b> que lee con bloqueo la marca de cancelación (si ya se autorizó ejecutar, rechaza sin escribir), toma los cupos en orden de <c>linea_id</c>, inserta la
/// devolución con sus líneas solicitadas y audita. Un rechazo por política no crea fila (D06). El motivo nunca llega a la auditoría ni a un error.
/// </summary>
internal sealed class SolicitarDevolucionDeImpreso(
    AutorizadorDeDevoluciones autorizador,
    LimitesDeDevoluciones limites,
    IPedidosParaDevoluciones pedidos,
    IInventarioDeDevoluciones inventario,
    IMarcaDeCancelacionDeCuenta marca,
    IRepositorioDeDevoluciones repositorio,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IReloj reloj) : ICasoDeUso<SolicitarDevolucionDeImpresoEntrada, DevolucionVista>
{
    public async Task<Resultado<DevolucionVista>> Ejecutar(SolicitarDevolucionDeImpresoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirCliente(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(rechazo);
        }

        if (await limites.Operacion(cancelacion).ConfigureAwait(false) is { } excedido)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(excedido);
        }

        if (Validar(entrada) is { } invalida)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(invalida);
        }

        try
        {
            // (1) Pedidos sin bloqueo; la propiedad se comprueba antes de cualquier otra llamada.
            var lectura = await pedidos.Obtener(entrada.PedidoId, cancelacion).ConfigureAwait(false);
            if (!lectura.EsExito)
            {
                return lectura.Error!.Categoria == CategoriaDeError.NoEncontrado
                    ? FallosDeDevoluciones.Fallo<DevolucionVista>(await autorizador.NoEncontrado(MotivosDeDenegacionDeDevoluciones.RecursoAjeno, cancelacion).ConfigureAwait(false))
                    : FallosDeDevoluciones.NoDisponible<DevolucionVista>();
            }

            var pedido = lectura.Valor;
            if (await autorizador.ExigirQueElPedidoEsDelCliente(pedido.CuentaId, cancelacion).ConfigureAwait(false) is { } ajeno)
            {
                return FallosDeDevoluciones.Fallo<DevolucionVista>(ajeno);
            }

            // (2) La sucursal receptora debe existir y estar activa (D09), fuera de toda transacción.
            var sucursales = await ListarSucursalesActivas(cancelacion).ConfigureAwait(false);
            if (sucursales is null)
            {
                return FallosDeDevoluciones.NoDisponible<DevolucionVista>();
            }

            var codigo = entrada.CodigoDeSucursalReceptora.Trim();
            if (!sucursales.Contains(codigo, StringComparer.Ordinal))
            {
                return FallosDeDevoluciones.Fallo<DevolucionVista>(MapeoDeDevoluciones.ValidacionDeCampo("sucursalReceptora", "desconocida"));
            }

            // (3) Las líneas y sus plazos: lo que no cumple la política se rechaza sin crear nada.
            var ahora = reloj.Ahora;
            var evaluadas = Evaluar(entrada, pedido, ahora, out var rechazoDePolitica);
            if (rechazoDePolitica is not null)
            {
                return FallosDeDevoluciones.Fallo<DevolucionVista>(rechazoDePolitica);
            }

            var motivo = TextoRedactado.Crear(entrada.Motivo.Valor, TextoRedactado.LongitudMaximaDelMotivo)!;
            var solicitante = ResponsableDelCambio.Cliente(autorizador.Actor!.UsuarioId!.Value);
            var devolucion = Devolucion.Solicitar(pedido.PedidoId, TipoDeDevolucion.Impreso, OrigenDeDevolucion.Solicitud, codigo, motivo, null, [.. evaluadas.Select(e => e.Nueva)], ahora, solicitante);

            // (4) Una sola transacción: marca de cancelación con bloqueo → cupos en orden de línea → devolución → auditoría.
            await unidad.Ejecutar(
                async token =>
                {
                    await Registrar(devolucion, pedido, evaluadas, token).ConfigureAwait(false);
                    return true;
                },
                cancelacion).ConfigureAwait(false);
            return Resultado<DevolucionVista>.Exito(MapeoDeDevoluciones.Vista(devolucion));
        }
        catch (RechazoQueRevierteException rechazoRevertido)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(rechazoRevertido.Error);
        }
        catch (DevolucionesNoDisponiblesException)
        {
            return FallosDeDevoluciones.NoDisponible<DevolucionVista>();
        }
    }

    private async Task<IReadOnlyCollection<string>?> ListarSucursalesActivas(CancellationToken cancelacion)
    {
        try
        {
            return await inventario.ListarSucursalesActivas(cancelacion).ConfigureAwait(false) is ResultadoDeSucursalesActivas.Lista lista ? lista.Codigos : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private async Task Registrar(Devolucion devolucion, PedidoParaDevoluciones pedido, IReadOnlyList<LineaEvaluada> evaluadas, CancellationToken token)
    {
        // La marca de cancelación se lee con bloqueo al inicio: si la cancelación ya se autorizó a ejecutar, no se escribe nada (DEV-26). Si no se puede leer, no se asume libre.
        EstadoDeLaMarca estado;
        try
        {
            estado = await marca.LeerConBloqueo(pedido.CuentaId, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeDevoluciones.ServicioNoDisponible));
        }

        if (estado is EstadoDeLaMarca.EnEjecucion or EstadoDeLaMarca.Ejecutada)
        {
            throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDeDevoluciones.CuentaEnCancelacion));
        }

        var faltante = await repositorio.TomarCupos([.. evaluadas.Select(e => e.Cupo)], token).ConfigureAwait(false);
        if (faltante is { } linea)
        {
            throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDeDevoluciones.CantidadExcedida, [new ErrorDeCampo("lineas", linea.ToString("D"))]));
        }

        await repositorio.Agregar(devolucion, token).ConfigureAwait(false);
        await auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.DevolucionSolicitada)
            {
                ObjetoTipo = "devolucion",
                ObjetoId = devolucion.Id.ToString(),
                Detalle = new Dictionary<string, string?>
                {
                    ["pedidoId"] = devolucion.PedidoId.ToString(),
                    ["cantidadDeLineas"] = devolucion.Lineas.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
            },
            token).ConfigureAwait(false);
    }

    /// <summary>Valida la forma de la entrada sin repetir ningún valor: líneas, cantidades, sucursal y el motivo (que no se devuelve).</summary>
    private static ErrorDeAplicacion? Validar(SolicitarDevolucionDeImpresoEntrada e)
    {
        if (e.PedidoId == Guid.Empty)
        {
            return MapeoDeDevoluciones.ValidacionDeCampo("pedidoId", "obligatorio");
        }

        if (e.Lineas is null || e.Lineas.Count is 0 or > PoliticaDeDevolucion.MaximoDeLineas)
        {
            return MapeoDeDevoluciones.ValidacionDeCampo("lineas", "cantidad-invalida");
        }

        if (e.Lineas.Any(l => l is null || l.LineaId == Guid.Empty || l.Cantidad < 1) || e.Lineas.Select(l => l.LineaId).Distinct().Count() != e.Lineas.Count)
        {
            return MapeoDeDevoluciones.ValidacionDeCampo("lineas", "formato-invalido");
        }

        if (string.IsNullOrWhiteSpace(e.CodigoDeSucursalReceptora) || e.CodigoDeSucursalReceptora.Trim().Length > Devolucion.LongitudMaximaDeLaSucursal)
        {
            return MapeoDeDevoluciones.ValidacionDeCampo("sucursalReceptora", "obligatorio");
        }

        return e.Motivo?.Valor is null || TextoRedactado.Crear(e.Motivo.Valor, TextoRedactado.LongitudMaximaDelMotivo) is null
            ? MapeoDeDevoluciones.ValidacionDeCampo("motivo", "formato-invalido")
            : null;
    }

    private sealed record LineaEvaluada(NuevaLineaDeDevolucion Nueva, CupoPedido Cupo);

    /// <summary>Evalúa la política de cada línea (DEV-02, DEV-03, DEV-15, DEV-19) con la lectura de Pedidos: el primer incumplimiento rechaza toda la solicitud.</summary>
    private static List<LineaEvaluada> Evaluar(SolicitarDevolucionDeImpresoEntrada entrada, PedidoParaDevoluciones pedido, DateTimeOffset ahora, out ErrorDeAplicacion? rechazo)
    {
        rechazo = null;
        var evaluadas = new List<LineaEvaluada>();
        var lineasDelPedido = pedido.Lineas.ToDictionary(l => l.LineaId);
        if (entrada.Lineas.Any(l => !lineasDelPedido.ContainsKey(l.LineaId)))
        {
            rechazo = MapeoDeDevoluciones.ValidacionDeCampo("lineas", "no-es-del-pedido");
            return evaluadas;
        }

        if (entrada.Lineas.Any(l => lineasDelPedido[l.LineaId].EsDigital))
        {
            rechazo = new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDeDevoluciones.TiposMezclados);
            return evaluadas;
        }

        foreach (var pedida in entrada.Lineas)
        {
            var linea = lineasDelPedido[pedida.LineaId];
            if (linea.Estado == EstadoVisibleDeLaLinea.Anulado)
            {
                rechazo = new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDeDevoluciones.LineaNoDevolvible);
                return evaluadas;
            }

            if (linea.Estado != EstadoVisibleDeLaLinea.Entregado || linea.EntregadaEn is not { } entregada)
            {
                rechazo = new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDeDevoluciones.LineaNoEntregada);
                return evaluadas;
            }

            if (!PoliticaDeDevolucion.SolicitudDeImpresoEnPlazo(entregada, ahora))
            {
                rechazo = new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDeDevoluciones.PlazoVencido, [new ErrorDeCampo("plazo-en-dias", "15")]);
                return evaluadas;
            }

            if (pedida.Cantidad > linea.Cantidad)
            {
                rechazo = new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDeDevoluciones.CantidadExcedida, [new ErrorDeCampo("lineas", pedida.LineaId.ToString("D"))]);
                return evaluadas;
            }

            evaluadas.Add(new LineaEvaluada(new NuevaLineaDeDevolucion(linea.LineaId, linea.EdicionId, pedida.Cantidad, linea.PrecioUnitario), new CupoPedido(linea.LineaId, pedido.PedidoId, linea.Cantidad, pedida.Cantidad)));
        }

        // Dos líneas de la misma edición no caben en una devolución (de 1 a 20 con ediciones distintas).
        if (evaluadas.Select(e => e.Nueva.EdicionId).Distinct().Count() != evaluadas.Count)
        {
            rechazo = MapeoDeDevoluciones.ValidacionDeCampo("lineas", "ediciones-repetidas");
        }

        return evaluadas;
    }
}
