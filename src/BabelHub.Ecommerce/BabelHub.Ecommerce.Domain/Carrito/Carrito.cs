namespace BabelHub.Ecommerce.Domain.Carrito;

public enum EstadoDeCarrito
{
    Abierto,

    /// <summary>El carrito de un visitante que ya se sumó al de una cuenta (CAR-24): no admite líneas y la cookie que lo identificaba deja de valer.</summary>
    Fusionado,
}

public enum RechazoDeCarrito
{
    /// <summary>La cantidad no está entre 1 y 99 (CAR-02, D14).</summary>
    CantidadInvalida,

    /// <summary>El carrito ya tiene 50 líneas (CAR-29, D14).</summary>
    TopeDeLineas,

    /// <summary>La suma de unidades de un impreso pasaría de 99 (D14).</summary>
    TopeDeUnidades,

    EdicionNoEstaEnElCarrito,

    /// <summary>El carrito ya se fusionó con el de una cuenta: no admite líneas.</summary>
    CarritoFusionado,
}

/// <summary>El resultado de agregar, cambiar o quitar una línea: el rechazo del Dominio o la cantidad con que quedó la línea (0 si se quitó).</summary>
public sealed class ResultadoDeLinea
{
    private ResultadoDeLinea(RechazoDeCarrito? rechazo, int cantidadFinal)
    {
        Rechazo = rechazo;
        CantidadFinal = cantidadFinal;
    }

    public bool EsExito => Rechazo is null;

    public RechazoDeCarrito? Rechazo { get; }

    /// <summary>La cantidad con que quedó la línea; una digital siempre queda en 1 aunque se pidieran más.</summary>
    public int CantidadFinal { get; }

    internal static ResultadoDeLinea Exito(int cantidadFinal) => new(null, cantidadFinal);

    internal static ResultadoDeLinea Rechazar(RechazoDeCarrito motivo) => new(motivo, 0);
}

/// <summary>
/// El carrito de una cuenta o de un visitante (CAR-01, CAR-29, D01, D02): una línea por edición, un impreso de 1 a 99 unidades y una digital siempre una, hasta 50 líneas (D14), con su total. El de un visitante caduca a los 30 días
/// de la última acción <b>de la persona</b> (un <c>GET</c> no la mueve: aquí solo la mueve una operación que cambia el carrito). No lleva precios (los lee el servidor del Catálogo): el total los recibe. Los instantes llegan como parámetros.
/// </summary>
public sealed class Carrito
{
    public const int MaximoDeLineas = 50;

    public static readonly TimeSpan DuracionDelVisitante = TimeSpan.FromDays(30);

    private readonly List<LineaDeCarrito> _lineas;

    private Carrito(
        Guid id,
        PropietarioDelCarrito propietario,
        EstadoDeCarrito estado,
        int version,
        DateTimeOffset creadoEn,
        DateTimeOffset modificadoEn,
        DateTimeOffset? venceEn,
        List<LineaDeCarrito> lineas)
    {
        Id = id;
        Propietario = propietario;
        Estado = estado;
        Version = version;
        CreadoEn = creadoEn;
        ModificadoEn = modificadoEn;
        VenceEn = venceEn;
        _lineas = lineas;
    }

    public Guid Id { get; }

    public PropietarioDelCarrito Propietario { get; }

    public EstadoDeCarrito Estado { get; private set; }

    /// <summary>Sube con cada cambio: es el control de concurrencia optimista del repositorio.</summary>
    public int Version { get; private set; }

    public DateTimeOffset CreadoEn { get; }

    public DateTimeOffset ModificadoEn { get; private set; }

    /// <summary>La última acción de la persona más 30 días; nulo en el carrito de una cuenta.</summary>
    public DateTimeOffset? VenceEn { get; private set; }

    public IReadOnlyList<LineaDeCarrito> Lineas => _lineas;

    public static Carrito Crear(Guid id, PropietarioDelCarrito propietario, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(propietario);
        if (id == Guid.Empty)
        {
            throw new ArgumentException("El carrito necesita su identificador.", nameof(id));
        }

        return new Carrito(id, propietario, EstadoDeCarrito.Abierto, 1, ahora, ahora, propietario.EsDeVisitante ? ahora + DuracionDelVisitante : null, []);
    }

    public static Carrito Restaurar(
        Guid id,
        PropietarioDelCarrito propietario,
        EstadoDeCarrito estado,
        int version,
        DateTimeOffset creadoEn,
        DateTimeOffset modificadoEn,
        DateTimeOffset? venceEn,
        IEnumerable<LineaDeCarrito> lineas)
    {
        ArgumentNullException.ThrowIfNull(propietario);
        ArgumentNullException.ThrowIfNull(lineas);
        return new Carrito(id, propietario, estado, version, creadoEn, modificadoEn, venceEn, [.. lineas]);
    }

    /// <summary>
    /// Un visitante pierde su carrito a los 30 días de su última acción (CAR-29, AC-CAR-25): a los 30 días exactos ya caducó. El de una cuenta no caduca. Quien lo abre trata uno caducado como «sin carrito».
    /// </summary>
    public bool EstaCaducado(DateTimeOffset ahora) => VenceEn is { } vence && ahora >= vence;

    /// <summary>
    /// Agrega una edición (CAR-02): un impreso nuevo crea su línea y uno que ya está suma las unidades (hasta 99); una digital queda siempre en una unidad aunque se pidan más (CAR-03, AC-CAR-02). Rechaza una cantidad
    /// fuera de 1 a 99, la línea 51 y un carrito ya fusionado, sin cambiar nada.
    /// </summary>
    public ResultadoDeLinea Agregar(Guid edicionId, bool esDigital, int cantidad, DateTimeOffset ahora)
    {
        if (Estado == EstadoDeCarrito.Fusionado)
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.CarritoFusionado);
        }

        if (!Cantidad.TryCrear(cantidad, out var pedida))
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.CantidadInvalida);
        }

        var indice = _lineas.FindIndex(l => l.EdicionId == edicionId);
        if (indice < 0)
        {
            if (_lineas.Count >= MaximoDeLineas)
            {
                return ResultadoDeLinea.Rechazar(RechazoDeCarrito.TopeDeLineas);
            }

            var nueva = LineaDeCarrito.Crear(edicionId, esDigital, esDigital ? Cantidad.Uno : pedida, ahora);
            _lineas.Add(nueva);
            Tocar(ahora);
            return ResultadoDeLinea.Exito(nueva.Cantidad.Valor);
        }

        var existente = _lineas[indice];
        if (existente.EsDigital)
        {
            Tocar(ahora);
            return ResultadoDeLinea.Exito(Cantidad.Uno.Valor);
        }

        if (!Cantidad.TryCrear(existente.Cantidad.Valor + pedida.Valor, out var suma))
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.TopeDeUnidades);
        }

        _lineas[indice] = existente.ConCantidad(suma);
        Tocar(ahora);
        return ResultadoDeLinea.Exito(suma.Valor);
    }

    /// <summary>Cambia la cantidad de una línea (CAR-02): una digital queda en una unidad.</summary>
    public ResultadoDeLinea Modificar(Guid edicionId, int cantidad, DateTimeOffset ahora)
    {
        if (Estado == EstadoDeCarrito.Fusionado)
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.CarritoFusionado);
        }

        if (!Cantidad.TryCrear(cantidad, out var nueva))
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.CantidadInvalida);
        }

        var indice = _lineas.FindIndex(l => l.EdicionId == edicionId);
        if (indice < 0)
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.EdicionNoEstaEnElCarrito);
        }

        var linea = _lineas[indice];
        _lineas[indice] = linea.ConCantidad(linea.EsDigital ? Cantidad.Uno : nueva);
        Tocar(ahora);
        return ResultadoDeLinea.Exito(_lineas[indice].Cantidad.Valor);
    }

    /// <summary>Quita la línea de una edición (CAR-02).</summary>
    public ResultadoDeLinea Eliminar(Guid edicionId, DateTimeOffset ahora)
    {
        if (Estado == EstadoDeCarrito.Fusionado)
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.CarritoFusionado);
        }

        if (_lineas.RemoveAll(l => l.EdicionId == edicionId) == 0)
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.EdicionNoEstaEnElCarrito);
        }

        Tocar(ahora);
        return ResultadoDeLinea.Exito(0);
    }

    /// <summary>El carrito se sumó al de una cuenta (CAR-24): queda <c>fusionado</c> y sin líneas.</summary>
    public void MarcarFusionado(DateTimeOffset ahora)
    {
        _lineas.Clear();
        Estado = EstadoDeCarrito.Fusionado;
        Tocar(ahora);
    }

    /// <summary>
    /// El total de las líneas (CAR-02): el importe de cada una (precio por cantidad, a dos decimales) y su suma. El precio de cada edición lo da el Catálogo; si falta el de una línea no se calcula (no se adivina un precio).
    /// </summary>
    public decimal Total(IReadOnlyDictionary<Guid, decimal> preciosPorEdicion)
    {
        ArgumentNullException.ThrowIfNull(preciosPorEdicion);
        var total = 0m;
        foreach (var linea in _lineas)
        {
            if (!preciosPorEdicion.TryGetValue(linea.EdicionId, out var precio))
            {
                throw new ArgumentException("Falta el precio de una línea del carrito.", nameof(preciosPorEdicion));
            }

            total += Soles.Importe(precio, linea.Cantidad.Valor);
        }

        return total;
    }

    private void Tocar(DateTimeOffset ahora)
    {
        Version++;
        ModificadoEn = ahora;
        if (Propietario.EsDeVisitante)
        {
            VenceEn = ahora + DuracionDelVisitante;
        }
    }
}
