using System.Diagnostics;
using System.Globalization;

namespace BabelHub.Ecommerce.Domain.Pedidos;

public enum TipoDeEntregaDelPedido
{
    Recojo,
    Domicilio,
    Digital,
}

/// <summary>La versión de un texto legal que la persona aceptó y cuándo (CAR-12): se copia del checkout y no cambia.</summary>
public sealed record AceptacionDeTexto(string Version, DateTimeOffset AceptadoEn);

/// <summary>Una línea de la compra aprobada tal como llega a Pedidos: la copia del título, el ISBN, el precio y la cantidad, y para los impresos la fuente, si requiere traslado, el traslado vinculado y su fecha estimada (<see langword="null"/> es «a confirmar»).</summary>
public sealed record DatosDeLineaNueva(
    Guid LineaId,
    Guid EdicionId,
    bool EsDigital,
    string Titulo,
    string? Isbn,
    int Cantidad,
    decimal PrecioUnitario,
    decimal Importe,
    string? CodigoDeFuente,
    bool RequiereTraslado,
    Guid? TrasladoId,
    DateOnly? FechaEstimadaDeLlegada);

/// <summary>Todo lo que hace falta para crear un pedido desde una compra aprobada (PED-01, PED-32). El contacto solo existe en un pedido a domicilio.</summary>
public sealed record DatosDePedidoNuevo(
    Guid PedidoId,
    string Numero,
    Guid CuentaId,
    TipoDeEntregaDelPedido Tipo,
    string? CodigoDeSucursalDeRecojo,
    string? RegionDeEntrega,
    decimal CostoDeEnvio,
    decimal Total,
    ContactoDeEntrega? Contacto,
    AceptacionDeTexto Terminos,
    AceptacionDeTexto Privacidad,
    AceptacionDeTexto? ReembolsoDigital,
    IReadOnlyList<DatosDeLineaNueva> Lineas,
    DateTimeOffset CreadoEn);

/// <summary>Por qué no se crea un pedido: la Aplicación lo traduce a un fallo para que Pagos compense el cobro (PED-36). Ninguno repite el contacto.</summary>
public enum RechazoDeCrearPedido
{
    SinLineas,
    NumeroInvalido,
    AceptacionFaltante,
    LineaInvalida,
    LineaRepetida,
    FuenteFaltante,
    TrasladoIncoherente,
    EntregaIncoherente,

    /// <summary>Un pedido a domicilio sin el nombre, el teléfono y la dirección completos (PED-36).</summary>
    ContactoFaltante,

    /// <summary>Un pedido de recojo o digital que trae contacto: el contacto solo existe a domicilio (PED-32).</summary>
    ContactoNoCorresponde,
    TotalIncoherente,
}

public sealed class ResultadoDeCrearPedido
{
    private ResultadoDeCrearPedido(Pedido? pedido, RechazoDeCrearPedido? rechazo)
    {
        Pedido = pedido;
        Rechazo = rechazo;
    }

    public bool Creado => Pedido is not null;

    public Pedido? Pedido { get; }

    public RechazoDeCrearPedido? Rechazo { get; }

    internal static ResultadoDeCrearPedido Con(Pedido pedido) => new(pedido, null);

    internal static ResultadoDeCrearPedido Rechazado(RechazoDeCrearPedido rechazo) => new(null, rechazo);
}

/// <summary>Qué pasó con cada línea de una operación (preparar, marcar lista, despachar o entregar): se aplicó, la línea espera un traslado sin recibir (PED-10), el estado no lo permite o la línea no es del pedido.</summary>
public enum ResultadoDeOperacionDeLinea
{
    Aplicada,
    EsperaElTraslado,
    EstadoInvalido,
    NoEncontrada,
}

public sealed record ResultadoPorLinea(Guid LineaId, ResultadoDeOperacionDeLinea Resultado);

/// <summary>
/// La política de no recogidos (PED-15, D07): una línea de recojo que lleva <b>más de 360 horas</b> (15 días) en listo para recojo pasa a no recogida. Se mide en instantes, no en días de calendario: 14 días no vencen y 15 días y 1 hora sí.
/// </summary>
public static class PoliticaDeNoRecogido
{
    public const int PlazoEnHoras = 360;

    public static bool EstaVencida(DateTimeOffset listoEn, DateTimeOffset ahora) => ahora - listoEn > TimeSpan.FromHours(PlazoEnHoras);
}

/// <summary>
/// Una línea de un pedido (una por edición, PED-01): la copia del título, el ISBN, el precio y la cantidad con su propio ciclo (PED-02). Una impresa nace <see cref="EstadoDeLineaImpresa.Confirmado"/> y una digital <see cref="EstadoDeLineaDigital.Disponible"/> (el acceso
/// ya lo concedió la biblioteca, PED-03). Los cambios de estado pasan por <see cref="PasarA"/>, que solo aplica una transición que la máquina permite y fija la fecha que le corresponde (PED-29).
/// </summary>
[DebuggerDisplay("Linea {Id}")]
public sealed class LineaDePedido
{
    private readonly bool _esDeRecojo;

    internal LineaDePedido(DatosDeLineaNueva datos, Guid pedidoId, bool esDeRecojo)
    {
        Id = datos.LineaId;
        PedidoId = pedidoId;
        EdicionId = datos.EdicionId;
        EsDigital = datos.EsDigital;
        Titulo = datos.Titulo.Trim();
        Isbn = string.IsNullOrWhiteSpace(datos.Isbn) ? null : datos.Isbn.Trim();
        Cantidad = datos.Cantidad;
        PrecioUnitario = datos.PrecioUnitario;
        Importe = datos.Importe;
        CodigoDeFuente = datos.EsDigital ? null : datos.CodigoDeFuente!.Trim();
        RequiereTraslado = datos.RequiereTraslado;
        TrasladoId = datos.TrasladoId;
        FechaEstimadaDeLlegada = datos.FechaEstimadaDeLlegada;
        EstadoImpreso = datos.EsDigital ? null : EstadoDeLineaImpresa.Confirmado;
        EstadoDigital = datos.EsDigital ? EstadoDeLineaDigital.Disponible : null;
        _esDeRecojo = esDeRecojo;
    }

    public Guid Id { get; }

    public Guid PedidoId { get; }

    public Guid EdicionId { get; }

    public bool EsDigital { get; }

    public string Titulo { get; }

    public string? Isbn { get; }

    public int Cantidad { get; }

    public decimal PrecioUnitario { get; }

    public decimal Importe { get; }

    public string? CodigoDeFuente { get; }

    public bool RequiereTraslado { get; }

    public Guid? TrasladoId { get; }

    public DateOnly? FechaEstimadaDeLlegada { get; private set; }

    /// <summary>El estado de una línea impresa (<see langword="null"/> en una digital).</summary>
    public EstadoDeLineaImpresa? EstadoImpreso { get; private set; }

    /// <summary>El estado de una línea digital (<see langword="null"/> en una impresa).</summary>
    public EstadoDeLineaDigital? EstadoDigital { get; private set; }

    /// <summary>Cuándo se marcó lista (en recojo, la entrada a listo para recojo; en domicilio, el hito sin estado propio, PED-28).</summary>
    public DateTimeOffset? ListoEn { get; private set; }

    public DateTimeOffset? EntregadaEn { get; private set; }

    public DateTimeOffset? NoRecogidaEn { get; private set; }

    public DateTimeOffset? AnuladaEn { get; private set; }

    /// <summary>El envío al que pertenece una línea a domicilio una vez creado (<see langword="null"/> mientras tanto).</summary>
    public Guid? EnvioId { get; private set; }

    public ContextoDeLinea Contexto => new(RequiereTraslado, _esDeRecojo);

    /// <summary>
    /// Pasa una línea impresa a <paramref name="hacia"/> si la máquina lo permite (PED-02, PED-19): devuelve <see langword="false"/> y no cambia nada si no. Fija la fecha del estado (entregada, no recogida, anulada o lista) y, al pasar a listo para recojo,
    /// el instante de listo que usa la política de no recogidos (PED-15).
    /// </summary>
    public bool PasarA(EstadoDeLineaImpresa hacia, DateTimeOffset ahora)
    {
        if (EstadoImpreso is not { } actual || !MaquinaDeEstadosDeLinea.Permite(actual, hacia, Contexto))
        {
            return false;
        }

        // A domicilio una línea solo sale (despachada) si logística la marcó lista (PED-28).
        if (hacia == EstadoDeLineaImpresa.Despachado && ListoEn is null)
        {
            return false;
        }

        EstadoImpreso = hacia;
        switch (hacia)
        {
            case EstadoDeLineaImpresa.ListoParaRecojo:
                ListoEn = ahora;
                break;
            case EstadoDeLineaImpresa.Entregado:
                EntregadaEn = ahora;
                break;
            case EstadoDeLineaImpresa.NoRecogido:
                NoRecogidaEn = ahora;
                break;
            case EstadoDeLineaImpresa.Anulado:
                AnuladaEn = ahora;
                break;
        }

        return true;
    }

    /// <summary>
    /// A domicilio, marcar lista es un hito sin estado propio (PED-28): la línea sigue en preparación y solo guarda la fecha de listo, una vez. Una línea que no está en preparación, ya marcada o de un pedido de recojo no cambia.
    /// </summary>
    internal bool MarcarListaSinEstado(DateTimeOffset ahora)
    {
        if (_esDeRecojo || EstadoImpreso != EstadoDeLineaImpresa.EnPreparacion || ListoEn is not null)
        {
            return false;
        }

        ListoEn = ahora;
        return true;
    }

    /// <summary>Anula una línea digital disponible (la biblioteca ya revocó el acceso); una ya anulada o una impresa no cambia.</summary>
    public bool AnularDigital(DateTimeOffset ahora)
    {
        if (EstadoDigital is not { } actual || !MaquinaDeEstadosDeLinea.PermiteDigital(actual, EstadoDeLineaDigital.Anulado))
        {
            return false;
        }

        EstadoDigital = EstadoDeLineaDigital.Anulado;
        AnuladaEn = ahora;
        return true;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"LineaDePedido {{ Id = {Id} }}");
}

/// <summary>
/// El pedido (PED-01): la raíz que agrupa las líneas de una compra aprobada con la copia del contacto de entrega solo a domicilio (PED-32), el número aleatorio, las aceptaciones de textos y el estado <b>derivado</b> de sus líneas (PED-20), recalculado en la misma operación que cambia una línea
/// (<see cref="RecalcularEstado"/>). Un pedido solo digital nace entregado (PED-12). <see cref="FechaEstadoFinal"/> es la del último cambio a entregado o anulado y de ella cuentan los dos años de la retención (PED-34). Su texto nunca incluye el contacto.
/// </summary>
[DebuggerDisplay("Pedido {Id}")]
public sealed class Pedido
{
    private readonly List<LineaDePedido> _lineas;

    private Pedido(DatosDePedidoNuevo datos, NumeroDePedido numero, List<LineaDePedido> lineas)
    {
        Id = datos.PedidoId;
        Numero = numero;
        CuentaId = datos.CuentaId;
        Tipo = datos.Tipo;
        CodigoDeSucursalDeRecojo = datos.CodigoDeSucursalDeRecojo?.Trim();
        RegionDeEntrega = datos.RegionDeEntrega?.Trim();
        CostoDeEnvio = datos.CostoDeEnvio;
        Total = datos.Total;
        Contacto = datos.Contacto;
        Terminos = datos.Terminos;
        Privacidad = datos.Privacidad;
        ReembolsoDigital = datos.ReembolsoDigital;
        CreadoEn = datos.CreadoEn;
        _lineas = lineas;
        Estado = Derivar();
        FechaEstadoFinal = Estado.EsFinal() ? datos.CreadoEn : null;
    }

    public Guid Id { get; }

    public NumeroDePedido Numero { get; }

    /// <summary>El identificador opaco de la cuenta que entrega Identidad (aleatorio, sin mapa): se conserva aunque la cuenta se cancele.</summary>
    public Guid CuentaId { get; }

    public TipoDeEntregaDelPedido Tipo { get; }

    public string? CodigoDeSucursalDeRecojo { get; }

    public string? RegionDeEntrega { get; }

    public decimal CostoDeEnvio { get; }

    public decimal Total { get; }

    public EstadoDePedido Estado { get; private set; }

    public DateTimeOffset CreadoEn { get; }

    /// <summary>El último cambio a entregado o anulado: de aquí cuentan los dos años de PED-34.</summary>
    public DateTimeOffset? FechaEstadoFinal { get; private set; }

    /// <summary>El contacto de entrega (solo a domicilio y mientras no se anonimice); nunca aparece en el texto del pedido.</summary>
    public ContactoDeEntrega? Contacto { get; private set; }

    public AceptacionDeTexto Terminos { get; }

    public AceptacionDeTexto Privacidad { get; }

    public AceptacionDeTexto? ReembolsoDigital { get; }

    public IReadOnlyList<LineaDePedido> Lineas => _lineas;

    /// <summary>
    /// Crea el pedido desde una compra aprobada si es coherente; si no, indica por qué sin repetir ningún dato personal (PED-36). Las comprobaciones van de lo más general a lo más particular: líneas, número, aceptaciones, cada línea, la entrega, el contacto y el total.
    /// </summary>
    public static ResultadoDeCrearPedido Crear(DatosDePedidoNuevo datos)
    {
        ArgumentNullException.ThrowIfNull(datos);
        if (datos.Lineas is null || datos.Lineas.Count == 0)
        {
            return ResultadoDeCrearPedido.Rechazado(RechazoDeCrearPedido.SinLineas);
        }

        if (NumeroDePedido.Crear(datos.Numero) is not { } numero)
        {
            return ResultadoDeCrearPedido.Rechazado(RechazoDeCrearPedido.NumeroInvalido);
        }

        var hayDigitales = datos.Lineas.Any(l => l.EsDigital);
        if (datos.Terminos is null || datos.Privacidad is null || (hayDigitales && datos.ReembolsoDigital is null))
        {
            return ResultadoDeCrearPedido.Rechazado(RechazoDeCrearPedido.AceptacionFaltante);
        }

        if (datos.Lineas.Select(l => l.LineaId).Distinct().Count() != datos.Lineas.Count || datos.Lineas.Select(l => l.EdicionId).Distinct().Count() != datos.Lineas.Count)
        {
            return ResultadoDeCrearPedido.Rechazado(RechazoDeCrearPedido.LineaRepetida);
        }

        foreach (var linea in datos.Lineas)
        {
            if (RechazoDeLinea(linea) is { } rechazo)
            {
                return ResultadoDeCrearPedido.Rechazado(rechazo);
            }
        }

        if (!EntregaEsCoherente(datos))
        {
            return ResultadoDeCrearPedido.Rechazado(RechazoDeCrearPedido.EntregaIncoherente);
        }

        if (datos.Tipo == TipoDeEntregaDelPedido.Domicilio && datos.Contacto is null)
        {
            return ResultadoDeCrearPedido.Rechazado(RechazoDeCrearPedido.ContactoFaltante);
        }

        if (datos.Tipo != TipoDeEntregaDelPedido.Domicilio && datos.Contacto is not null)
        {
            return ResultadoDeCrearPedido.Rechazado(RechazoDeCrearPedido.ContactoNoCorresponde);
        }

        if (datos.Total <= 0m || datos.Total != datos.Lineas.Sum(l => l.Importe) + datos.CostoDeEnvio)
        {
            return ResultadoDeCrearPedido.Rechazado(RechazoDeCrearPedido.TotalIncoherente);
        }

        var esDeRecojo = datos.Tipo == TipoDeEntregaDelPedido.Recojo;
        var lineas = datos.Lineas.Select(l => new LineaDePedido(l, datos.PedidoId, esDeRecojo)).ToList();
        return ResultadoDeCrearPedido.Con(new Pedido(datos, numero, lineas));
    }

    /// <summary>
    /// Recalcula el estado del pedido desde sus líneas (PED-20) y, si pasa a entregado o anulado, fija <see cref="FechaEstadoFinal"/> con <paramref name="ahora"/>. Se llama en la misma operación que cambia una línea. Devuelve <see langword="true"/> si el estado cambió.
    /// </summary>
    public bool RecalcularEstado(DateTimeOffset ahora)
    {
        var nuevo = Derivar();
        if (nuevo == Estado)
        {
            return false;
        }

        Estado = nuevo;
        if (nuevo.EsFinal())
        {
            FechaEstadoFinal = ahora;
        }

        return true;
    }

    // ---------- Gestión de las líneas (PED-09, PED-10, PED-11, PED-28, PED-29) ----------

    /// <summary>
    /// Pasa a en preparación las líneas pedidas (PED-09, PED-19): una confirmada sin traslado o una recibida; <b>una con traslado sin recibir no se prepara</b> y se informa que espera el traslado (PED-10) mientras las demás sí se preparan. Devuelve el resultado de cada línea;
    /// el pedido se recalcula en la misma operación.
    /// </summary>
    public IReadOnlyList<ResultadoPorLinea> PrepararLineas(IReadOnlyCollection<Guid> lineaIds, DateTimeOffset ahora) => Aplicar(lineaIds, ahora, (linea, instante) =>
    {
        if (linea.RequiereTraslado && linea.EstadoImpreso is EstadoDeLineaImpresa.Confirmado or EstadoDeLineaImpresa.EnTransito)
        {
            return ResultadoDeOperacionDeLinea.EsperaElTraslado;
        }

        return Resultado(linea.PasarA(EstadoDeLineaImpresa.EnPreparacion, instante));
    });

    /// <summary>
    /// Marca listas las líneas en preparación: en recojo pasan a listo para recojo y arranca su plazo (PED-09, PED-15); a domicilio es un <b>hito sin estado propio</b> (la línea sigue en preparación con su fecha de listo) que habilita crear el envío (PED-28). Marcar de nuevo una
    /// línea ya lista es estado inválido.
    /// </summary>
    public IReadOnlyList<ResultadoPorLinea> MarcarListas(IReadOnlyCollection<Guid> lineaIds, DateTimeOffset ahora) => Aplicar(lineaIds, ahora, (linea, instante) =>
        Tipo == TipoDeEntregaDelPedido.Recojo
            ? Resultado(linea.PasarA(EstadoDeLineaImpresa.ListoParaRecojo, instante))
            : Resultado(linea.MarcarListaSinEstado(instante)));

    /// <summary>Pasa a despachadas las líneas a domicilio marcadas listas cuando su envío pasa a en reparto (PED-29): una no marcada lista no sale.</summary>
    public IReadOnlyList<ResultadoPorLinea> DespacharLineas(IReadOnlyCollection<Guid> lineaIds, DateTimeOffset ahora) =>
        Aplicar(lineaIds, ahora, (linea, instante) => Resultado(linea.PasarA(EstadoDeLineaImpresa.Despachado, instante)));

    /// <summary>
    /// Entrega las líneas en <paramref name="fecha"/> (PED-11, PED-29): en recojo desde listo para recojo (o no recogido), a domicilio desde despachado con la fecha de entrega del envío. El que el Inventario haya descontado el físico lo decide la Aplicación antes de llamarlo.
    /// </summary>
    public IReadOnlyList<ResultadoPorLinea> EntregarLineas(IReadOnlyCollection<Guid> lineaIds, DateTimeOffset fecha) =>
        Aplicar(lineaIds, fecha, (linea, instante) => Resultado(linea.PasarA(EstadoDeLineaImpresa.Entregado, instante)));

    /// <summary>
    /// Marca no recogidas, en un pedido de recojo, las líneas que llevan más de 360 horas en listo para recojo (PED-15, D07), <b>omitiendo las que tienen una operación en curso</b> (<paramref name="enCurso"/>). Devuelve las líneas marcadas y recalcula el pedido; repetir la revisión no
    /// cambia nada.
    /// </summary>
    public IReadOnlyList<Guid> MarcarNoRecogidas(DateTimeOffset ahora, IReadOnlySet<Guid> enCurso)
    {
        ArgumentNullException.ThrowIfNull(enCurso);
        if (Tipo != TipoDeEntregaDelPedido.Recojo)
        {
            return [];
        }

        var marcadas = _lineas
            .Where(l => l.EstadoImpreso == EstadoDeLineaImpresa.ListoParaRecojo && l.ListoEn is { } listoEn && PoliticaDeNoRecogido.EstaVencida(listoEn, ahora) && !enCurso.Contains(l.Id))
            .Where(l => l.PasarA(EstadoDeLineaImpresa.NoRecogido, ahora))
            .Select(l => l.Id)
            .ToList();
        if (marcadas.Count > 0)
        {
            RecalcularEstado(ahora);
        }

        return marcadas;
    }

    private IReadOnlyList<ResultadoPorLinea> Aplicar(IReadOnlyCollection<Guid> lineaIds, DateTimeOffset ahora, Func<LineaDePedido, DateTimeOffset, ResultadoDeOperacionDeLinea> operacion)
    {
        ArgumentNullException.ThrowIfNull(lineaIds);
        var resultados = new List<ResultadoPorLinea>(lineaIds.Count);
        foreach (var id in lineaIds)
        {
            var linea = _lineas.FirstOrDefault(l => l.Id == id);
            resultados.Add(new ResultadoPorLinea(id, linea is null ? ResultadoDeOperacionDeLinea.NoEncontrada : linea.EsDigital ? ResultadoDeOperacionDeLinea.EstadoInvalido : operacion(linea, ahora)));
        }

        RecalcularEstado(ahora);
        return resultados;
    }

    private static ResultadoDeOperacionDeLinea Resultado(bool aplicada) => aplicada ? ResultadoDeOperacionDeLinea.Aplicada : ResultadoDeOperacionDeLinea.EstadoInvalido;

    private EstadoDePedido Derivar() => DerivacionDelEstadoDelPedido.Derivar(
        [.. _lineas.Where(l => !l.EsDigital).Select(l => l.EstadoImpreso!.Value)],
        [.. _lineas.Where(l => l.EsDigital).Select(l => l.EstadoDigital!.Value)]);

    private static RechazoDeCrearPedido? RechazoDeLinea(DatosDeLineaNueva linea)
    {
        if (linea.LineaId == Guid.Empty || linea.EdicionId == Guid.Empty || string.IsNullOrWhiteSpace(linea.Titulo) || linea.Cantidad < 1 || linea.PrecioUnitario < 0m || linea.Importe != linea.PrecioUnitario * linea.Cantidad)
        {
            return RechazoDeCrearPedido.LineaInvalida;
        }

        if (linea.EsDigital)
        {
            return linea.CodigoDeFuente is not null || linea.RequiereTraslado || linea.TrasladoId is not null || linea.FechaEstimadaDeLlegada is not null
                ? RechazoDeCrearPedido.LineaInvalida
                : null;
        }

        if (string.IsNullOrWhiteSpace(linea.CodigoDeFuente))
        {
            return RechazoDeCrearPedido.FuenteFaltante;
        }

        var conTraslado = linea.TrasladoId is { } traslado && traslado != Guid.Empty;
        return linea.RequiereTraslado != conTraslado || (!linea.RequiereTraslado && linea.FechaEstimadaDeLlegada is not null) || (linea.TrasladoId is { } t && t == Guid.Empty)
            ? RechazoDeCrearPedido.TrasladoIncoherente
            : null;
    }

    private static bool EntregaEsCoherente(DatosDePedidoNuevo datos)
    {
        var hayImpresas = datos.Lineas.Any(l => !l.EsDigital);
        var haySucursal = !string.IsNullOrWhiteSpace(datos.CodigoDeSucursalDeRecojo);
        var hayRegion = !string.IsNullOrWhiteSpace(datos.RegionDeEntrega);
        return datos.Tipo switch
        {
            TipoDeEntregaDelPedido.Digital => !hayImpresas && !haySucursal && !hayRegion && datos.CostoDeEnvio == 0m,
            TipoDeEntregaDelPedido.Recojo => hayImpresas && haySucursal && !hayRegion && datos.CostoDeEnvio == 0m,
            TipoDeEntregaDelPedido.Domicilio => hayImpresas && hayRegion && !haySucursal && datos.CostoDeEnvio >= 0m,
            _ => false,
        };
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"Pedido {{ Id = {Id}, Numero = {Numero} }}");
}
