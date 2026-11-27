namespace BabelHub.Ecommerce.Domain.Devoluciones;

/// <summary>Una línea de una devolución con su propio estado y su historial de cambios con fecha y responsable (DEV-10).</summary>
public sealed class LineaDeDevolucion
{
    private readonly List<CambioDeEstado> _cambios;

    private LineaDeDevolucion(
        Guid id,
        Guid lineaDePedidoId,
        Guid edicionId,
        int cantidad,
        decimal precioUnitario,
        decimal monto,
        Guid? accesoId,
        List<CambioDeEstado> cambios,
        DateTimeOffset? reembolsoRegistradoEn)
    {
        Id = id;
        LineaDePedidoId = lineaDePedidoId;
        EdicionId = edicionId;
        Cantidad = cantidad;
        PrecioUnitario = precioUnitario;
        Monto = monto;
        AccesoId = accesoId;
        _cambios = cambios;
        ReembolsoRegistradoEn = reembolsoRegistradoEn;
    }

    public Guid Id { get; }

    /// <summary>La línea del pedido (la clave del cupo y del reembolso <c>dev:&lt;devolucionId&gt;:&lt;lineaId&gt;</c>).</summary>
    public Guid LineaDePedidoId { get; }

    public Guid EdicionId { get; }

    public int Cantidad { get; }

    public decimal PrecioUnitario { get; }

    /// <summary>Precio unitario por la cantidad, sin el envío (DEV-08).</summary>
    public decimal Monto { get; }

    /// <summary>La referencia al acceso digital del reporte; se quita al cancelar la cuenta (DEV-24).</summary>
    public Guid? AccesoId { get; private set; }

    public IReadOnlyList<CambioDeEstado> Cambios => _cambios;

    public EstadoDeLineaDeDevolucion Estado => _cambios[^1].Estado;

    public DateTimeOffset EstadoEn => _cambios[^1].Instante;

    /// <summary>Cuándo pasó a aprobada (el plazo de recepción de un impreso cuenta desde aquí, DEV-14).</summary>
    public DateTimeOffset? AprobadaEn => _cambios.Where(c => c.Estado == EstadoDeLineaDeDevolucion.Aprobada).Select(c => (DateTimeOffset?)c.Instante).LastOrDefault();

    public DateTimeOffset? ReembolsoRegistradoEn { get; private set; }

    public DateTimeOffset? ReembolsadaEn => Estado == EstadoDeLineaDeDevolucion.Reembolsada ? EstadoEn : null;

    internal static LineaDeDevolucion Nueva(Guid lineaDePedidoId, Guid edicionId, int cantidad, decimal precioUnitario, Guid? accesoId, DateTimeOffset ahora, ResponsableDelCambio responsable)
    {
        if (lineaDePedidoId == Guid.Empty || edicionId == Guid.Empty)
        {
            throw new ArgumentException("La línea necesita sus identificadores.");
        }

        if (cantidad < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidad));
        }

        var monto = PoliticaDeDevolucion.Monto(precioUnitario, cantidad);
        return new LineaDeDevolucion(Guid.NewGuid(), lineaDePedidoId, edicionId, cantidad, precioUnitario, monto, accesoId, [new CambioDeEstado(EstadoDeLineaDeDevolucion.Solicitada, ahora, responsable)], null);
    }

    internal static LineaDeDevolucion Rehidratar(Guid id, Guid lineaDePedidoId, Guid edicionId, int cantidad, decimal precioUnitario, decimal monto, Guid? accesoId, IReadOnlyList<CambioDeEstado> cambios, DateTimeOffset? reembolsoRegistradoEn)
    {
        if (id == Guid.Empty || lineaDePedidoId == Guid.Empty || edicionId == Guid.Empty || cantidad < 1 || monto < 0m || precioUnitario < 0m || cambios.Count == 0 || cambios[0].Estado != EstadoDeLineaDeDevolucion.Solicitada)
        {
            throw new ArgumentException("La línea de devolución guardada no es coherente.");
        }

        for (var i = 1; i < cambios.Count; i++)
        {
            if (!Admite(cambios[i - 1].Estado, cambios[i].Estado) || cambios[i].Instante < cambios[i - 1].Instante)
            {
                throw new ArgumentException("El historial de la línea de devolución guardada no es coherente.");
            }
        }

        return new LineaDeDevolucion(id, lineaDePedidoId, edicionId, cantidad, precioUnitario, monto, accesoId, [.. cambios], reembolsoRegistradoEn);
    }

    /// <summary>La máquina de estados (DEV-10): solicitada a aprobada o rechazada; aprobada a recibida, reembolsada o rechazada (por el plazo de recepción); recibida a reembolsada. Rechazada y reembolsada son finales.</summary>
    private static bool Admite(EstadoDeLineaDeDevolucion desde, EstadoDeLineaDeDevolucion hacia) => (desde, hacia) switch
    {
        (EstadoDeLineaDeDevolucion.Solicitada, EstadoDeLineaDeDevolucion.Aprobada) => true,
        (EstadoDeLineaDeDevolucion.Solicitada, EstadoDeLineaDeDevolucion.Rechazada) => true,
        (EstadoDeLineaDeDevolucion.Aprobada, EstadoDeLineaDeDevolucion.Recibida) => true,
        (EstadoDeLineaDeDevolucion.Aprobada, EstadoDeLineaDeDevolucion.Reembolsada) => true,
        (EstadoDeLineaDeDevolucion.Aprobada, EstadoDeLineaDeDevolucion.Rechazada) => true,
        (EstadoDeLineaDeDevolucion.Recibida, EstadoDeLineaDeDevolucion.Reembolsada) => true,
        _ => false,
    };

    internal void Cambiar(EstadoDeLineaDeDevolucion nuevo, DateTimeOffset ahora, ResponsableDelCambio responsable)
    {
        if (!Admite(Estado, nuevo))
        {
            throw new InvalidOperationException($"Una línea {Estado.Codigo()} no pasa a {nuevo.Codigo()}.");
        }

        _cambios.Add(new CambioDeEstado(nuevo, ahora < EstadoEn ? EstadoEn : ahora, responsable));
    }

    internal void RegistrarReembolso(DateTimeOffset ahora) => ReembolsoRegistradoEn ??= ahora;

    /// <summary>Quita la referencia al acceso y el identificador del cliente de cada cambio (DEV-24, DEV-27); conserva estados, fechas y el responsable del personal. Devuelve si cambió algo.</summary>
    internal bool QuitarDatosDelCliente(bool quitarElAcceso)
    {
        var cambio = false;
        for (var i = 0; i < _cambios.Count; i++)
        {
            var sin = _cambios[i].Responsable.SinIdentidadDelCliente();
            if (sin != _cambios[i].Responsable)
            {
                _cambios[i] = _cambios[i] with { Responsable = sin };
                cambio = true;
            }
        }

        if (quitarElAcceso && AccesoId is not null)
        {
            AccesoId = null;
            cambio = true;
        }

        return cambio;
    }
}

/// <summary>
/// La devolución (DEV-01, DEV-10): las líneas de un pedido de <b>un solo tipo</b> (impreso o digital) con su sucursal receptora (impresos), el motivo y, en un reporte, el texto. <b>Aprobar, rechazar, iniciar el reingreso y recibir operan sobre
/// la devolución completa</b> (D02); solo el reembolso avanza línea a línea. No guarda la cuenta: solo el responsable de cada cambio (D01). El reingreso se marca en tres pasos que no se repiten (D07): iniciado (el marcador que la revisión de plazos
/// respeta), incierto (un resultado del Inventario que no se conoce; no se borra) y confirmado.
/// </summary>
public sealed class Devolucion
{
    public const int LongitudMaximaDeLaSucursal = 40;

    private readonly List<LineaDeDevolucion> _lineas;

    private Devolucion(
        Guid id,
        Guid pedidoId,
        TipoDeDevolucion tipo,
        OrigenDeDevolucion origen,
        string? sucursal,
        TextoRedactado motivo,
        TextoRedactado? texto,
        DateTimeOffset creadaEn,
        DateTimeOffset? redactadaEn,
        DateTimeOffset? reingresoIniciadoEn,
        DateTimeOffset? reingresoIncertoEn,
        DateTimeOffset? reingresoConfirmadoEn,
        List<LineaDeDevolucion> lineas)
    {
        Id = id;
        PedidoId = pedidoId;
        Tipo = tipo;
        Origen = origen;
        CodigoDeSucursalReceptora = sucursal;
        Motivo = motivo;
        Texto = texto;
        CreadaEn = creadaEn;
        RedactadaEn = redactadaEn;
        ReingresoIniciadoEn = reingresoIniciadoEn;
        ReingresoIncertoEn = reingresoIncertoEn;
        ReingresoConfirmadoEn = reingresoConfirmadoEn;
        _lineas = lineas;
    }

    public Guid Id { get; }

    public Guid PedidoId { get; }

    public TipoDeDevolucion Tipo { get; }

    public OrigenDeDevolucion Origen { get; }

    /// <summary>La sucursal receptora que eligió el cliente (obligatoria en impresos, nula en digitales, DEV-01).</summary>
    public string? CodigoDeSucursalReceptora { get; }

    public TextoRedactado Motivo { get; private set; }

    /// <summary>El texto de un reporte (solo con origen reporte).</summary>
    public TextoRedactado? Texto { get; private set; }

    public DateTimeOffset CreadaEn { get; }

    public DateTimeOffset? RedactadaEn { get; private set; }

    public DateTimeOffset? ReingresoIniciadoEn { get; private set; }

    /// <summary>Se fija en el primer resultado incierto del Inventario y no se borra (D07).</summary>
    public DateTimeOffset? ReingresoIncertoEn { get; private set; }

    public DateTimeOffset? ReingresoConfirmadoEn { get; private set; }

    public IReadOnlyList<LineaDeDevolucion> Lineas => _lineas;

    /// <summary>La solicitud de la persona o el reporte: crea la devolución con cada línea en <c>solicitada</c> (DEV-01). Rechaza (con excepción: son errores de programación, el caso de uso ya validó) lo que rompe una invariante.</summary>
    public static Devolucion Solicitar(
        Guid pedidoId,
        TipoDeDevolucion tipo,
        OrigenDeDevolucion origen,
        string? codigoDeSucursalReceptora,
        TextoRedactado motivo,
        TextoRedactado? texto,
        IReadOnlyList<NuevaLineaDeDevolucion> lineas,
        DateTimeOffset ahora,
        ResponsableDelCambio solicitante)
    {
        ArgumentNullException.ThrowIfNull(motivo);
        ArgumentNullException.ThrowIfNull(lineas);
        if (pedidoId == Guid.Empty)
        {
            throw new ArgumentException("La devolución necesita un pedido.", nameof(pedidoId));
        }

        ValidarLasLineas(tipo, origen, lineas);
        var sucursal = codigoDeSucursalReceptora?.Trim();
        if (tipo == TipoDeDevolucion.Impreso)
        {
            if (string.IsNullOrEmpty(sucursal) || sucursal.Length > LongitudMaximaDeLaSucursal)
            {
                throw new ArgumentException("Un impreso necesita su sucursal receptora.", nameof(codigoDeSucursalReceptora));
            }
        }
        else if (sucursal is not null)
        {
            throw new ArgumentException("Un digital no tiene sucursal receptora.", nameof(codigoDeSucursalReceptora));
        }

        if ((origen == OrigenDeDevolucion.Reporte) != (texto is not null))
        {
            throw new ArgumentException("El texto es solo de los reportes.", nameof(texto));
        }

        var nuevas = lineas.Select(l => LineaDeDevolucion.Nueva(l.LineaDePedidoId, l.EdicionId, l.Cantidad, l.PrecioUnitario, l.AccesoId, ahora, solicitante)).ToList();
        return new Devolucion(Guid.NewGuid(), pedidoId, tipo, origen, tipo == TipoDeDevolucion.Impreso ? sucursal : null, motivo, texto, ahora, null, null, null, null, nuevas);
    }

    /// <summary>Lo que lee el repositorio; valida la coherencia que las restricciones de la base también exigen.</summary>
    public static Devolucion Rehidratar(
        Guid id,
        Guid pedidoId,
        TipoDeDevolucion tipo,
        OrigenDeDevolucion origen,
        string? codigoDeSucursalReceptora,
        TextoRedactado motivo,
        TextoRedactado? texto,
        DateTimeOffset creadaEn,
        DateTimeOffset? redactadaEn,
        DateTimeOffset? reingresoIniciadoEn,
        DateTimeOffset? reingresoIncertoEn,
        DateTimeOffset? reingresoConfirmadoEn,
        IReadOnlyList<LineaRehidratada> lineas)
    {
        if (id == Guid.Empty || pedidoId == Guid.Empty || lineas.Count is < 1 or > PoliticaDeDevolucion.MaximoDeLineas)
        {
            throw new ArgumentException("La devolución guardada no es coherente.");
        }

        if ((tipo == TipoDeDevolucion.Impreso) != (codigoDeSucursalReceptora is not null) || (origen == OrigenDeDevolucion.Reporte) != (texto is not null))
        {
            throw new ArgumentException("La devolución guardada no es coherente.");
        }

        if (tipo == TipoDeDevolucion.Digital && (reingresoIniciadoEn is not null || reingresoIncertoEn is not null || reingresoConfirmadoEn is not null))
        {
            throw new ArgumentException("Un digital no tiene reingreso.");
        }

        if ((reingresoIncertoEn is not null || reingresoConfirmadoEn is not null) && reingresoIniciadoEn is null)
        {
            throw new ArgumentException("El reingreso incierto o confirmado exige el marcador iniciado.");
        }

        var rehidratadas = lineas.Select(l => LineaDeDevolucion.Rehidratar(l.Id, l.LineaDePedidoId, l.EdicionId, l.Cantidad, l.PrecioUnitario, l.Monto, l.AccesoId, l.Cambios, l.ReembolsoRegistradoEn)).ToList();
        if (tipo == TipoDeDevolucion.Digital && rehidratadas.Any(l => l.Estado == EstadoDeLineaDeDevolucion.Recibida || l.Cambios.Any(c => c.Estado == EstadoDeLineaDeDevolucion.Recibida)))
        {
            throw new ArgumentException("Un digital nunca pasa a recibida.");
        }

        return new Devolucion(id, pedidoId, tipo, origen, codigoDeSucursalReceptora, motivo, texto, creadaEn, redactadaEn, reingresoIniciadoEn, reingresoIncertoEn, reingresoConfirmadoEn, rehidratadas);
    }

    private static void ValidarLasLineas(TipoDeDevolucion tipo, OrigenDeDevolucion origen, IReadOnlyList<NuevaLineaDeDevolucion> lineas)
    {
        if (lineas.Count is < 1 or > PoliticaDeDevolucion.MaximoDeLineas)
        {
            throw new ArgumentException("Una devolución lleva de 1 a 20 líneas.", nameof(lineas));
        }

        // Un impreso admite dos líneas de una edición (una por sucursal o lote de la compra); un digital, una por edición.
        if (lineas.Select(l => l.LineaDePedidoId).Distinct().Count() != lineas.Count || (tipo == TipoDeDevolucion.Digital && lineas.Select(l => l.EdicionId).Distinct().Count() != lineas.Count))
        {
            throw new ArgumentException("Cada línea del pedido va una sola vez y los digitales son de ediciones distintas.", nameof(lineas));
        }

        if (origen == OrigenDeDevolucion.Reporte && (tipo != TipoDeDevolucion.Digital || lineas.Count != 1 || lineas[0].AccesoId is null))
        {
            throw new ArgumentException("Un reporte es de una sola línea digital con su acceso.", nameof(lineas));
        }

        if (origen == OrigenDeDevolucion.Solicitud && lineas.Any(l => l.AccesoId is not null))
        {
            throw new ArgumentException("La referencia al acceso es solo de los reportes.", nameof(lineas));
        }
    }

    /// <summary>El último cambio de cualquier línea (lo que cuenta el plazo de retención).</summary>
    public DateTimeOffset UltimoCambioEn => _lineas.Max(l => l.EstadoEn);

    public bool TodasFinales => _lineas.All(l => l.Estado.EsFinal());

    private bool Todas(EstadoDeLineaDeDevolucion estado) => _lineas.All(l => l.Estado == estado);

    /// <summary>Aprueba la devolución completa (todas las líneas <c>solicitada</c>, D02); repetirla es estado inválido sin efectos (DEV-17).</summary>
    public ResultadoDeTransicionDeDevolucion Aprobar(DateTimeOffset ahora, ResponsableDelCambio responsable)
    {
        if (!Todas(EstadoDeLineaDeDevolucion.Solicitada))
        {
            return ResultadoDeTransicionDeDevolucion.EstadoInvalido;
        }

        foreach (var linea in _lineas)
        {
            linea.Cambiar(EstadoDeLineaDeDevolucion.Aprobada, ahora, responsable);
        }

        return ResultadoDeTransicionDeDevolucion.Aplicada;
    }

    /// <summary>Rechaza por decisión solo líneas <c>solicitada</c> (una aprobada solo pasa a rechazada por el plazo de recepción, nunca con el reingreso iniciado). El caso de uso devuelve el cupo (DEV-18).</summary>
    public ResultadoDeTransicionDeDevolucion Rechazar(DateTimeOffset ahora, ResponsableDelCambio responsable)
    {
        if (!Todas(EstadoDeLineaDeDevolucion.Solicitada))
        {
            return ResultadoDeTransicionDeDevolucion.EstadoInvalido;
        }

        foreach (var linea in _lineas)
        {
            linea.Cambiar(EstadoDeLineaDeDevolucion.Rechazada, ahora, responsable);
        }

        return ResultadoDeTransicionDeDevolucion.Aplicada;
    }

    /// <summary>
    /// El plazo de recepción de un impreso (DEV-14): pasa a rechazada, sin reembolso y con el responsable «sistema», la devolución aprobada hace más de 10 días <b>sin el marcador de reingreso</b> (DEV-16: la recepción ya iniciada se respeta).
    /// </summary>
    public ResultadoDeTransicionDeDevolucion RechazarPorPlazoDeRecepcion(DateTimeOffset ahora)
    {
        if (Tipo != TipoDeDevolucion.Impreso)
        {
            return ResultadoDeTransicionDeDevolucion.TipoInvalido;
        }

        if (!Todas(EstadoDeLineaDeDevolucion.Aprobada))
        {
            return ResultadoDeTransicionDeDevolucion.EstadoInvalido;
        }

        if (ReingresoIniciadoEn is not null)
        {
            return ResultadoDeTransicionDeDevolucion.ReingresoIniciado;
        }

        var aprobada = _lineas.Min(l => l.AprobadaEn)!.Value;
        if (!PoliticaDeDevolucion.RecepcionVencida(aprobada, ahora))
        {
            return ResultadoDeTransicionDeDevolucion.NoVencida;
        }

        foreach (var linea in _lineas)
        {
            linea.Cambiar(EstadoDeLineaDeDevolucion.Rechazada, ahora, ResponsableDelCambio.Sistema);
        }

        return ResultadoDeTransicionDeDevolucion.Aplicada;
    }

    /// <summary>Fija el marcador de reingreso bajo el bloqueo de la devolución, antes de llamar al Inventario (D07). Un marcador ya fijado es un reintento, no un error.</summary>
    public ResultadoDeInicioDeReingreso IniciarReingreso(DateTimeOffset ahora)
    {
        if (Tipo != TipoDeDevolucion.Impreso)
        {
            return ResultadoDeInicioDeReingreso.TipoInvalido;
        }

        if (!Todas(EstadoDeLineaDeDevolucion.Aprobada))
        {
            return ResultadoDeInicioDeReingreso.EstadoInvalido;
        }

        if (ReingresoIniciadoEn is not null)
        {
            return ResultadoDeInicioDeReingreso.YaIniciado;
        }

        ReingresoIniciadoEn = ahora;
        return ResultadoDeInicioDeReingreso.Iniciado;
    }

    /// <summary>Anota el primer resultado incierto del Inventario: el reingreso pudo aplicarse, así que el marcador ya no se limpia (D07). No se borra.</summary>
    public void MarcarReingresoIncierto(DateTimeOffset ahora)
    {
        if (ReingresoIniciadoEn is null)
        {
            throw new InvalidOperationException("No hay un reingreso iniciado.");
        }

        ReingresoIncertoEn ??= ahora;
    }

    /// <summary>
    /// Limpia el marcador tras un rechazo de negocio del Inventario (garantiza que no se reingresó nada) <b>solo si ningún intento anterior quedó incierto</b> y el reingreso no está confirmado, para que la revisión de plazos vuelva a aplicar (D07).
    /// Devuelve si lo limpió.
    /// </summary>
    public bool LimpiarMarcadorDeReingreso()
    {
        if (ReingresoIniciadoEn is null || ReingresoIncertoEn is not null || ReingresoConfirmadoEn is not null)
        {
            return false;
        }

        ReingresoIniciadoEn = null;
        return true;
    }

    /// <summary>El Inventario confirmó el reingreso (también <c>yaAplicado</c>): se marca aparte y no se repite.</summary>
    public void ConfirmarReingreso(DateTimeOffset ahora)
    {
        if (ReingresoIniciadoEn is null)
        {
            throw new InvalidOperationException("No hay un reingreso iniciado.");
        }

        ReingresoConfirmadoEn ??= ahora;
    }

    /// <summary>Pasa las líneas a recibida solo con el reingreso confirmado (DEV-07: «solo si lo logra»).</summary>
    public ResultadoDeTransicionDeDevolucion Recibir(DateTimeOffset ahora, ResponsableDelCambio responsable)
    {
        if (Tipo != TipoDeDevolucion.Impreso)
        {
            return ResultadoDeTransicionDeDevolucion.TipoInvalido;
        }

        if (!Todas(EstadoDeLineaDeDevolucion.Aprobada))
        {
            return ResultadoDeTransicionDeDevolucion.EstadoInvalido;
        }

        if (ReingresoConfirmadoEn is null)
        {
            return ResultadoDeTransicionDeDevolucion.ReingresoSinConfirmar;
        }

        foreach (var linea in _lineas)
        {
            linea.Cambiar(EstadoDeLineaDeDevolucion.Recibida, ahora, responsable);
        }

        return ResultadoDeTransicionDeDevolucion.Aplicada;
    }

    private LineaDeDevolucion Linea(Guid lineaDePedidoId) => _lineas.SingleOrDefault(l => l.LineaDePedidoId == lineaDePedidoId) ?? throw new ArgumentException("La devolución no tiene esa línea.", nameof(lineaDePedidoId));

    /// <summary>Anota que el reembolso de la línea ya se registró en Pagos (cada paso se marca y no se repite, D07); repetirlo no cambia la fecha.</summary>
    public void RegistrarReembolsoDeLinea(Guid lineaDePedidoId, DateTimeOffset ahora) => Linea(lineaDePedidoId).RegistrarReembolso(ahora);

    /// <summary>Pasa una línea a reembolsada <b>solo cuando la pasarela confirma</b> (DEV-08): una recibida de un impreso o una aprobada de un digital; repetirlo es estado inválido.</summary>
    public ResultadoDeTransicionDeDevolucion Reembolsar(Guid lineaDePedidoId, DateTimeOffset ahora, ResponsableDelCambio responsable)
    {
        var linea = Linea(lineaDePedidoId);
        // Solo cuando la pasarela confirma, y con el reembolso ya registrado en Pagos (DEV-08, D07).
        if (linea.ReembolsoRegistradoEn is null)
        {
            return ResultadoDeTransicionDeDevolucion.EstadoInvalido;
        }

        var origenValido = Tipo == TipoDeDevolucion.Impreso ? linea.Estado == EstadoDeLineaDeDevolucion.Recibida : linea.Estado == EstadoDeLineaDeDevolucion.Aprobada;
        if (!origenValido)
        {
            return ResultadoDeTransicionDeDevolucion.EstadoInvalido;
        }

        linea.Cambiar(EstadoDeLineaDeDevolucion.Reembolsada, ahora, responsable);
        return ResultadoDeTransicionDeDevolucion.Aplicada;
    }

    /// <summary>La devolución cumplió dos años desde su último cambio con todas sus líneas finales (DEV-27) y todavía no se redactó.</summary>
    public bool EstaVencidaParaDepurar(DateOnly hoyEnLima) => RedactadaEn is null && TodasFinales && PoliticaDeRetencion.EstaVencida(UltimoCambioEn, hoyEnLima);

    /// <summary>
    /// Redacta el motivo y el texto, quita el identificador del cliente de los cambios y, si <paramref name="quitarElAcceso"/>, la referencia al acceso de los reportes (DEV-24: la cancelación de la cuenta; la depuración de los dos años
    /// conserva la referencia, DEV-27). Conserva pedido, líneas, cantidades, importes, estados y fechas. <b>Idempotente</b>: devuelve si cambió algo.
    /// </summary>
    public bool Redactar(DateTimeOffset ahora, bool quitarElAcceso)
    {
        var cambio = false;
        var motivo = Motivo.Redactar();
        if (!ReferenceEquals(motivo, Motivo))
        {
            Motivo = motivo;
            cambio = true;
        }

        if (Texto is not null)
        {
            var texto = Texto.Redactar();
            if (!ReferenceEquals(texto, Texto))
            {
                Texto = texto;
                cambio = true;
            }
        }

        foreach (var linea in _lineas)
        {
            cambio |= linea.QuitarDatosDelCliente(quitarElAcceso);
        }

        if (cambio)
        {
            RedactadaEn ??= ahora;
        }

        return cambio;
    }
}

/// <summary>Una línea que se pide en una solicitud o un reporte: la del pedido, la edición, la cantidad, el precio unitario que lee Pedidos y, en un reporte, la referencia al acceso.</summary>
public sealed record NuevaLineaDeDevolucion(Guid LineaDePedidoId, Guid EdicionId, int Cantidad, decimal PrecioUnitario, Guid? AccesoId = null);

/// <summary>Una línea guardada tal como la lee el repositorio.</summary>
public sealed record LineaRehidratada(Guid Id, Guid LineaDePedidoId, Guid EdicionId, int Cantidad, decimal PrecioUnitario, decimal Monto, Guid? AccesoId, IReadOnlyList<CambioDeEstado> Cambios, DateTimeOffset? ReembolsoRegistradoEn);
