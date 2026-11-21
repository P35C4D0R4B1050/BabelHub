namespace BabelHub.Ecommerce.Domain.Carrito;

public enum EstadoDeCarrito
{
    Abierto,

    /// <summary>El carrito de un visitante que ya se sumó al de una cuenta (CAR-24): no admite líneas y la cookie que lo identificaba deja de valer.</summary>
    Fusionado,
}

public enum RechazoDeCarrito
{
    /// <summary>La cantidad no está entre 1 y 20 (CAR-02, D14).</summary>
    CantidadInvalida,

    /// <summary>El carrito ya tiene 30 líneas (CAR-29, D14).</summary>
    TopeDeLineas,

    /// <summary>La suma de unidades de un impreso pasaría de 20 (D14).</summary>
    TopeDeUnidades,

    EdicionNoEstaEnElCarrito,

    /// <summary>La edición se retiró del catálogo y la línea espera a que el checkout salga de su estado vivo: no se puede cambiar (solo quitar).</summary>
    EdicionRetirada,

    /// <summary>El carrito ya se fusionó con el de una cuenta: no admite líneas.</summary>
    CarritoFusionado,
}

/// <summary>Qué hizo el retiro de una edición (CAT-18) sobre un carrito.</summary>
public enum ResultadoDeRetiro
{
    /// <summary>La edición no estaba en el carrito.</summary>
    NoEstaba,

    /// <summary>La línea se quitó; queda el aviso para la persona.</summary>
    Quitada,

    /// <summary>El checkout está en un estado vivo que no admite editar: la línea sigue, marcada, hasta que salga de él.</summary>
    Anotada,
}

/// <summary>Una edición cuya línea se quitó por un retiro del catálogo: para dejar el aviso <c>edicion_retirada</c> (D15).</summary>
public sealed record EdicionRetirada(Guid EdicionId, bool EsDigital);

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
/// El carrito de una cuenta o de un visitante (CAR-01, CAR-29, D01, D02): una línea por edición, un impreso de 1 a 20 unidades y una digital siempre una, hasta 30 líneas (D14, CAR-38), con su total. El de un visitante caduca a los 30 días
/// de la última acción <b>de la persona</b> (un <c>GET</c> no la mueve: aquí solo la mueve una operación que cambia el carrito). No lleva precios (los lee el servidor del Catálogo): el total los recibe. Los instantes llegan como parámetros.
/// </summary>
public sealed class Carrito
{
    public const int MaximoDeLineas = 30;

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
    /// Agrega una edición (CAR-02): un impreso nuevo crea su línea y uno que ya está suma las unidades (hasta 20); una digital queda siempre en una unidad aunque se pidan más (CAR-03, AC-CAR-02). Rechaza una cantidad
    /// fuera de 1 a 20, la línea 31 y un carrito ya fusionado, sin cambiar nada.
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
        if (existente.RetiroPendiente)
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.EdicionRetirada);
        }

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

        if (_lineas[indice].RetiroPendiente)
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.EdicionRetirada);
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

    /// <summary>
    /// Deja en el carrito de la cuenta las líneas que resultan de <see cref="Fusion.Fusionar"/> (CAR-24): las que ya estaban conservan su fecha y su orden y las nuevas llevan la de ahora. Un carrito fusionado no la admite.
    /// </summary>
    public bool AplicarFusion(ResultadoDeFusion resultado, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        if (Estado == EstadoDeCarrito.Fusionado)
        {
            return false;
        }

        var anteriores = _lineas.ToDictionary(l => l.EdicionId);
        _lineas.Clear();
        foreach (var fusionada in resultado.Lineas)
        {
            _lineas.Add(anteriores.TryGetValue(fusionada.EdicionId, out var anterior)
                ? anterior.ConCantidad(fusionada.Cantidad)
                : LineaDeCarrito.Crear(fusionada.EdicionId, fusionada.EsDigital, fusionada.Cantidad, ahora));
        }

        Tocar(ahora);
        return true;
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

    /// <summary>Hay una edición retirada del catálogo que espera a que el checkout salga de su estado vivo (D21).</summary>
    public bool TieneRetirosPendientes => _lineas.Exists(l => l.RetiroPendiente);

    /// <summary>
    /// Catálogo retiró una edición (CAT-18, <c>IRetiroDeEdicionDeCarritos</c>): con <paramref name="diferir"/> falso (no hay checkout vivo que lo impida) quita la línea y devuelve <see cref="ResultadoDeRetiro.Quitada"/>; con
    /// <paramref name="diferir"/> verdadero (el checkout está <c>reservando</c>, <c>en_pago</c> o <c>pago_pendiente</c>) solo la marca para quitarla después (<see cref="ResultadoDeRetiro.Anotada"/>, idempotente). Nunca falla ni mueve
    /// la caducidad del visitante: no es una acción de la persona.
    /// </summary>
    public ResultadoDeRetiro RetirarEdicion(Guid edicionId, bool diferir, DateTimeOffset ahora)
    {
        var indice = _lineas.FindIndex(l => l.EdicionId == edicionId);
        if (indice < 0)
        {
            return ResultadoDeRetiro.NoEstaba;
        }

        if (!diferir)
        {
            _lineas.RemoveAt(indice);
            Tocar(ahora, porLaPersona: false);
            return ResultadoDeRetiro.Quitada;
        }

        if (!_lineas[indice].RetiroPendiente)
        {
            _lineas[indice] = _lineas[indice].ConRetiroPendiente();
            Tocar(ahora, porLaPersona: false);
        }

        return ResultadoDeRetiro.Anotada;
    }

    /// <summary>
    /// Quita las líneas con retiro pendiente (<c>AplicarRetirosPendientes</c>, D21) y devuelve cuáles eran para dejar su aviso. Es lo que hace toda salida del checkout de un estado vivo hacia <c>en_edicion</c>, <c>cancelado</c>,
    /// <c>reiniciado</c> o <c>pagado</c>. Sin retiros pendientes no cambia nada.
    /// </summary>
    public IReadOnlyList<EdicionRetirada> AplicarRetirosPendientes(DateTimeOffset ahora)
    {
        var retiradas = _lineas.Where(l => l.RetiroPendiente).Select(l => new EdicionRetirada(l.EdicionId, l.EsDigital)).ToList();
        if (retiradas.Count == 0)
        {
            return retiradas;
        }

        _lineas.RemoveAll(l => l.RetiroPendiente);
        Tocar(ahora, porLaPersona: false);
        return retiradas;
    }

    /// <summary>
    /// Una anotación <b>huérfana</b> (sin checkout vivo, o con el checkout ya <c>en_edicion</c> o terminado porque la transición se confirmó entre la lectura del retiro y su bloqueo del carrito) la quita la primera lectura con bloqueo del
    /// carrito: la misma regla que <see cref="AplicarRetirosPendientes"/>, solo si el checkout no la sostiene. Con el checkout <c>reservando</c>, <c>en_pago</c> o <c>pago_pendiente</c> no hace nada.
    /// </summary>
    public IReadOnlyList<EdicionRetirada> QuitarRetirosHuerfanos(EstadoDelCheckout? estadoDelCheckout, DateTimeOffset ahora) =>
        estadoDelCheckout is { } e && Checkout.SostieneRetirosPendientes(e) ? [] : AplicarRetirosPendientes(ahora);

    private void Tocar(DateTimeOffset ahora, bool porLaPersona = true)
    {
        Version++;
        ModificadoEn = ahora;
        if (porLaPersona && Propietario.EsDeVisitante)
        {
            VenceEn = ahora + DuracionDelVisitante;
        }
    }
}
