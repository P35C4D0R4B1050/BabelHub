namespace BabelHub.Ecommerce.Domain.Envios;

/// <summary>
/// El envío (ENV-01): un grupo de líneas impresas de una misma fuente y de un mismo pedido a domicilio, con su guía y su ciclo de estados (pendiente de guía, guía generada, en reparto, entregado;
/// cancelado solo desde pendiente o guía generada sin el marcador de despacho). <b>No guarda nombre, dirección ni teléfono</b> (ENV-15). El Dominio no lee la hora ni habla con el operador, el
/// Inventario ni Pedidos: la hora llega por parámetro.
/// </summary>
public sealed class Envio
{
    /// <summary>El arrendamiento de la solicitud de guía (ENV-18): una segunda solicitud simultánea recibe «en curso» mientras vive.</summary>
    public static readonly TimeSpan ArrendamientoDeLaGuia = TimeSpan.FromSeconds(90);

    /// <summary>El arrendamiento del marcador de despacho (AC-ENV-16): la primera solicitud que confirma gana.</summary>
    public static readonly TimeSpan ArrendamientoDelDespacho = TimeSpan.FromSeconds(90);

    private const int LongitudMaximaDeLaFuente = 64;

    private const int LongitudMaximaDelOperador = 32;

    private readonly List<LineaDeEnvio> _lineas;

    private Envio(
        Guid id,
        Guid pedidoId,
        string codigoDeFuente,
        List<LineaDeEnvio> lineas,
        EstadoDeEnvio estado,
        NumeroDeGuia? numeroDeGuia,
        string? operador,
        Guid? claveDeSolicitud,
        DateTimeOffset? guiaEnCursoHasta,
        DateTimeOffset? despachoIniciadoEn,
        DateTimeOffset? despachoEnCursoHasta,
        DateTimeOffset? despachoIncertoEn,
        DateTimeOffset creadoEn,
        DateTimeOffset? guiaGeneradaEn,
        DateTimeOffset? enRepartoEn,
        DateTimeOffset? entregadoEn,
        DateTimeOffset? canceladoEn,
        DateTimeOffset? progresoEnRepartoInformadoEn,
        DateTimeOffset? progresoEntregadoInformadoEn,
        int version)
    {
        Id = id;
        PedidoId = pedidoId;
        CodigoDeFuente = codigoDeFuente;
        _lineas = lineas;
        Estado = estado;
        NumeroDeGuia = numeroDeGuia;
        Operador = operador;
        ClaveDeSolicitud = claveDeSolicitud;
        GuiaEnCursoHasta = guiaEnCursoHasta;
        DespachoIniciadoEn = despachoIniciadoEn;
        DespachoEnCursoHasta = despachoEnCursoHasta;
        DespachoIncertoEn = despachoIncertoEn;
        CreadoEn = creadoEn;
        GuiaGeneradaEn = guiaGeneradaEn;
        EnRepartoEn = enRepartoEn;
        EntregadoEn = entregadoEn;
        CanceladoEn = canceladoEn;
        ProgresoEnRepartoInformadoEn = progresoEnRepartoInformadoEn;
        ProgresoEntregadoInformadoEn = progresoEntregadoInformadoEn;
        Version = version;
    }

    public Guid Id { get; }

    public Guid PedidoId { get; }

    public string CodigoDeFuente { get; }

    public IReadOnlyList<LineaDeEnvio> Lineas => _lineas;

    public EstadoDeEnvio Estado { get; private set; }

    public NumeroDeGuia? NumeroDeGuia { get; private set; }

    /// <summary>El operador que generó la guía, tomado de la configuración y no de su respuesta.</summary>
    public string? Operador { get; private set; }

    /// <summary>La clave de solicitud (un GUID aleatorio fijado la primera vez y reutilizado en cada reintento): hace que el operador devuelva la misma guía (ENV-18).</summary>
    public Guid? ClaveDeSolicitud { get; private set; }

    public DateTimeOffset? GuiaEnCursoHasta { get; private set; }

    /// <summary>El marcador de despacho iniciado, confirmado antes de llamar al Inventario: con él <c>Cancelar</c> mantiene el envío (ENV-19).</summary>
    public DateTimeOffset? DespachoIniciadoEn { get; private set; }

    public DateTimeOffset? DespachoEnCursoHasta { get; private set; }

    /// <summary>Se fija en el primer resultado incierto del Inventario y no se borra (D05): con él, el marcador ya no se limpia aunque después llegue un rechazo de negocio.</summary>
    public DateTimeOffset? DespachoIncertoEn { get; private set; }

    public DateTimeOffset CreadoEn { get; }

    public DateTimeOffset? GuiaGeneradaEn { get; private set; }

    public DateTimeOffset? EnRepartoEn { get; private set; }

    public DateTimeOffset? EntregadoEn { get; private set; }

    public DateTimeOffset? CanceladoEn { get; private set; }

    public DateTimeOffset? ProgresoEnRepartoInformadoEn { get; private set; }

    public DateTimeOffset? ProgresoEntregadoInformadoEn { get; private set; }

    public int Version { get; }

    public bool TieneMarcadorDeDespacho => DespachoIniciadoEn is not null;

    public static Envio Crear(Guid id, Guid pedidoId, string codigoDeFuente, IEnumerable<Guid> lineaIds, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(codigoDeFuente);
        ArgumentNullException.ThrowIfNull(lineaIds);
        var fuente = codigoDeFuente.Trim();
        if (id == Guid.Empty || pedidoId == Guid.Empty)
        {
            throw new ArgumentException("El envío y el pedido deben tener un identificador.");
        }

        if (fuente.Length is 0 or > LongitudMaximaDeLaFuente)
        {
            throw new ArgumentException("El envío necesita el código de su fuente.", nameof(codigoDeFuente));
        }

        var lineas = lineaIds.Select(l => new LineaDeEnvio(l)).ToList();
        ValidarLineas(lineas);
        return new Envio(id, pedidoId, fuente, lineas, EstadoDeEnvio.PendienteDeGuia, null, null, null, null, null, null, null, ahora, null, null, null, null, null, null, 1);
    }

    /// <summary>Reconstruye un envío guardado. Rechaza un estado incoherente con sus fechas o su guía, como los <c>CHECK</c> de la base de datos.</summary>
    public static Envio Rehidratar(
        Guid id,
        Guid pedidoId,
        string codigoDeFuente,
        IEnumerable<Guid> lineaIds,
        EstadoDeEnvio estado,
        NumeroDeGuia? numeroDeGuia,
        string? operador,
        Guid? claveDeSolicitud,
        DateTimeOffset? guiaEnCursoHasta,
        DateTimeOffset? despachoIniciadoEn,
        DateTimeOffset? despachoEnCursoHasta,
        DateTimeOffset? despachoIncertoEn,
        DateTimeOffset creadoEn,
        DateTimeOffset? guiaGeneradaEn,
        DateTimeOffset? enRepartoEn,
        DateTimeOffset? entregadoEn,
        DateTimeOffset? canceladoEn,
        DateTimeOffset? progresoEnRepartoInformadoEn,
        DateTimeOffset? progresoEntregadoInformadoEn,
        int version)
    {
        var lineas = lineaIds.Select(l => new LineaDeEnvio(l)).ToList();
        ValidarLineas(lineas);
        var conGuia = estado is EstadoDeEnvio.GuiaGenerada or EstadoDeEnvio.EnReparto or EstadoDeEnvio.Entregado;
        var coherente =
            (estado != EstadoDeEnvio.PendienteDeGuia || (numeroDeGuia is null && operador is null && guiaGeneradaEn is null && despachoIniciadoEn is null))
            && (!conGuia || (numeroDeGuia is not null && operador is not null && guiaGeneradaEn is not null))
            && (estado != EstadoDeEnvio.GuiaGenerada || (enRepartoEn is null && entregadoEn is null))
            && ((estado is EstadoDeEnvio.EnReparto or EstadoDeEnvio.Entregado) == (enRepartoEn is not null))
            && ((estado == EstadoDeEnvio.Entregado) == (entregadoEn is not null))
            && ((estado == EstadoDeEnvio.Cancelado) == (canceladoEn is not null))
            && (despachoIniciadoEn is null || estado is EstadoDeEnvio.GuiaGenerada or EstadoDeEnvio.EnReparto or EstadoDeEnvio.Entregado)
            && (despachoIncertoEn is null || despachoIniciadoEn is not null || estado == EstadoDeEnvio.Cancelado);
        if (!coherente)
        {
            throw new ArgumentException("El estado del envío no es coherente con su guía o sus fechas.");
        }

        return new Envio(
            id, pedidoId, codigoDeFuente, lineas, estado, numeroDeGuia, operador, claveDeSolicitud, guiaEnCursoHasta, despachoIniciadoEn, despachoEnCursoHasta, despachoIncertoEn, creadoEn,
            guiaGeneradaEn, enRepartoEn, entregadoEn, canceladoEn, progresoEnRepartoInformadoEn, progresoEntregadoInformadoEn, version);
    }

    /// <summary>
    /// Fija la clave de solicitud la primera vez y el arrendamiento (ENV-18): una segunda solicitud con el arrendamiento vivo recibe «en curso»; si el anterior se cayó, el arrendamiento vence
    /// y el reintento continúa con la misma clave.
    /// </summary>
    public ResultadoDeSolicitudDeGuia TomarSolicitudDeGuia(DateTimeOffset ahora, Guid claveNueva)
    {
        if (Estado != EstadoDeEnvio.PendienteDeGuia)
        {
            return ResultadoDeSolicitudDeGuia.EstadoInvalido;
        }

        if (GuiaEnCursoHasta is { } hasta && hasta > ahora)
        {
            return ResultadoDeSolicitudDeGuia.EnCurso;
        }

        if (ClaveDeSolicitud is null)
        {
            if (claveNueva == Guid.Empty)
            {
                throw new ArgumentException("La clave de solicitud no puede ser vacía.", nameof(claveNueva));
            }

            ClaveDeSolicitud = claveNueva;
        }

        GuiaEnCursoHasta = ahora.Add(ArrendamientoDeLaGuia);
        return ResultadoDeSolicitudDeGuia.Tomada;
    }

    /// <summary>Libera el arrendamiento tras un fallo manejado, conservando la clave, para que el reintento no espere el vencimiento (ENV-04).</summary>
    public void LiberarArrendamientoDeLaGuia() => GuiaEnCursoHasta = null;

    /// <summary>Guarda el número y el operador y pasa a guía generada (ENV-02). El número es inmutable: la misma guía repetida es idempotente y otra distinta se rechaza.</summary>
    public ResultadoDeRegistroDeGuia RegistrarGuia(NumeroDeGuia numero, string operador, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(operador);
        var nombre = operador.Trim();
        if (nombre.Length is 0 or > LongitudMaximaDelOperador)
        {
            throw new ArgumentException("El operador debe ser un nombre corto tomado de la configuración.", nameof(operador));
        }

        switch (Estado)
        {
            case EstadoDeEnvio.Cancelado:
                return ResultadoDeRegistroDeGuia.GuiaSinUso;
            case EstadoDeEnvio.PendienteDeGuia:
                NumeroDeGuia = numero;
                Operador = nombre;
                GuiaGeneradaEn = ahora;
                GuiaEnCursoHasta = null;
                Estado = EstadoDeEnvio.GuiaGenerada;
                return ResultadoDeRegistroDeGuia.Registrada;
            default:
                return NumeroDeGuia == numero ? ResultadoDeRegistroDeGuia.YaRegistrada : ResultadoDeRegistroDeGuia.OtroNumero;
        }
    }

    /// <summary>
    /// Confirma el marcador de despacho iniciado y su arrendamiento antes de llamar al Inventario (ENV-11, AC-ENV-16): con el arrendamiento vivo, la otra solicitud recibe «en curso»; vencido,
    /// se retoma el mismo marcador.
    /// </summary>
    public ResultadoDeInicioDeDespacho IniciarDespacho(DateTimeOffset ahora)
    {
        if (Estado != EstadoDeEnvio.GuiaGenerada)
        {
            return ResultadoDeInicioDeDespacho.EstadoInvalido;
        }

        if (DespachoIniciadoEn is not null && DespachoEnCursoHasta is { } hasta && hasta > ahora)
        {
            return ResultadoDeInicioDeDespacho.EnCurso;
        }

        DespachoIniciadoEn ??= ahora;
        DespachoEnCursoHasta = ahora.Add(ArrendamientoDelDespacho);
        return ResultadoDeInicioDeDespacho.Iniciado;
    }

    /// <summary>Libera el arrendamiento del despacho tras un fallo manejado y conserva el marcador, salvo el rechazo de negocio que lo limpia aparte.</summary>
    public void LiberarArrendamientoDelDespacho() => DespachoEnCursoHasta = null;

    /// <summary>Anota el primer resultado incierto del Inventario; no se borra ni se pisa (D05).</summary>
    public void MarcarDespachoIncierto(DateTimeOffset ahora)
    {
        if (DespachoIniciadoEn is null)
        {
            throw new InvalidOperationException("No hay un despacho iniciado que pueda quedar incierto.");
        }

        DespachoIncertoEn ??= ahora;
    }

    /// <summary>
    /// Limpia el marcador solo ante un rechazo de negocio de la lista cerrada y <b>solo si ningún intento anterior quedó incierto</b>: así <c>Cancelar</c> vuelve a poder cancelar el envío (D05).
    /// </summary>
    public bool LimpiarMarcadorDeDespacho()
    {
        if (Estado != EstadoDeEnvio.GuiaGenerada || DespachoIniciadoEn is null || DespachoIncertoEn is not null)
        {
            return false;
        }

        DespachoIniciadoEn = null;
        DespachoEnCursoHasta = null;
        return true;
    }

    /// <summary>Pasa a en reparto con el marcador confirmado (la tercera transacción). Un envío que ya está en reparto es éxito sin repetir nada.</summary>
    public ResultadoDeTransicion PasarAEnReparto(DateTimeOffset ahora)
    {
        if (Estado == EstadoDeEnvio.EnReparto)
        {
            return ResultadoDeTransicion.YaEnReparto;
        }

        if (Estado != EstadoDeEnvio.GuiaGenerada || DespachoIniciadoEn is null)
        {
            return ResultadoDeTransicion.EstadoInvalido;
        }

        Estado = EstadoDeEnvio.EnReparto;
        EnRepartoEn = ahora;
        DespachoEnCursoHasta = null;
        return ResultadoDeTransicion.Hecha;
    }

    /// <summary>Marca la entrega (ENV-07): exige en reparto; un entregado no vuelve atrás ni se repite.</summary>
    public ResultadoDeTransicion MarcarEntregado(DateTimeOffset ahora)
    {
        if (Estado != EstadoDeEnvio.EnReparto)
        {
            return ResultadoDeTransicion.EstadoInvalido;
        }

        Estado = EstadoDeEnvio.Entregado;
        EntregadoEn = ahora;
        return ResultadoDeTransicion.Hecha;
    }

    /// <summary>Cancela por la anulación del pedido (ENV-13): el pendiente y el de guía generada sin el marcador de despacho; mantiene el resto. Un cancelado previo es éxito idempotente.</summary>
    public ResultadoDeCancelacion Cancelar(DateTimeOffset ahora)
    {
        if (Estado == EstadoDeEnvio.Cancelado)
        {
            return ResultadoDeCancelacion.YaEstabaCancelado;
        }

        var cancelable = Estado == EstadoDeEnvio.PendienteDeGuia || (Estado == EstadoDeEnvio.GuiaGenerada && DespachoIniciadoEn is null);
        if (!cancelable)
        {
            return ResultadoDeCancelacion.Mantenido;
        }

        Estado = EstadoDeEnvio.Cancelado;
        CanceladoEn = ahora;
        GuiaEnCursoHasta = null;
        return ResultadoDeCancelacion.Cancelado;
    }

    /// <summary>Anota que Pedidos ya recibió «en reparto». Devuelve falso si ya estaba informado o el envío no salió.</summary>
    public bool RegistrarProgresoEnRepartoInformado(DateTimeOffset ahora)
    {
        if (ProgresoEnRepartoInformadoEn is not null || EnRepartoEn is null)
        {
            return false;
        }

        ProgresoEnRepartoInformadoEn = ahora;
        return true;
    }

    /// <summary>Anota que Pedidos ya recibió «entregado»; <b>no antes de informar «en reparto»</b> (el orden que Pedidos exige).</summary>
    public bool RegistrarProgresoEntregadoInformado(DateTimeOffset ahora)
    {
        if (ProgresoEntregadoInformadoEn is not null || EntregadoEn is null || ProgresoEnRepartoInformadoEn is null)
        {
            return false;
        }

        ProgresoEntregadoInformadoEn = ahora;
        return true;
    }

    private static void ValidarLineas(List<LineaDeEnvio> lineas)
    {
        if (lineas.Count == 0)
        {
            throw new ArgumentException("Un envío lleva al menos una línea.");
        }

        if (lineas.Any(l => l.LineaId == Guid.Empty) || lineas.Select(l => l.LineaId).Distinct().Count() != lineas.Count)
        {
            throw new ArgumentException("Las líneas de un envío deben tener identificador y no repetirse.");
        }
    }
}
