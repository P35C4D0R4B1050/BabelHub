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

    /// <summary>Se pidió cambiar una línea a más de 20 unidades (D14, CAR-38).</summary>
    TopeDeUnidades,

    LineaNoEstaEnElCarrito,

    /// <summary>Un impreso se agrega con la sucursal que lo atiende (CAR-39).</summary>
    SucursalRequerida,

    /// <summary>Una línea anterior a la venta por sucursal no se puede cambiar: se quita y se vuelve a agregar eligiendo sucursal (CAR-45).</summary>
    LineaSinSucursal,

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
    private ResultadoDeLinea(RechazoDeCarrito? rechazo, Guid? lineaId, int cantidadFinal)
    {
        Rechazo = rechazo;
        LineaId = lineaId;
        CantidadFinal = cantidadFinal;
    }

    public bool EsExito => Rechazo is null;

    public RechazoDeCarrito? Rechazo { get; }

    /// <summary>La línea sobre la que quedó el cambio (nueva o ya existente); nulo si se rechazó.</summary>
    public Guid? LineaId { get; }

    /// <summary>La cantidad con que quedó la línea; una digital siempre queda en 1 aunque se pidieran más.</summary>
    public int CantidadFinal { get; }

    internal static ResultadoDeLinea Exito(Guid lineaId, int cantidadFinal) => new(null, lineaId, cantidadFinal);

    internal static ResultadoDeLinea Rechazar(RechazoDeCarrito motivo) => new(motivo, null, 0);
}

/// <summary>
/// El carrito de una cuenta o de un visitante (CAR-01, CAR-29, D01, D02): una línea por libro impreso y sucursal (o varias, en lotes, CAR-40) de 1 a 20 unidades, y una digital siempre de una, hasta 30 líneas (D14, CAR-38), con su total. El de un visitante caduca a los 30 días
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
    /// Agrega una edición (CAR-02, CAR-39, CAR-40). Un impreso exige la sucursal que lo atiende: la cantidad se suma a la línea más antigua de esa edición y sucursal que la admita entera sin pasar de 20 y, si ninguna la admite, se abre
    /// otra línea con la cantidad completa (la suma nunca se reparte ni se une una línea con otra, CAR-41). Una digital no lleva sucursal y queda siempre en una unidad (CAR-03, AC-CAR-02). Rechaza una cantidad fuera de 1 a 20, la línea 31 y un carrito
    /// ya fusionado, sin cambiar nada. <paramref name="lineaIdNueva"/> es el identificador de la línea si hace falta abrir una.
    /// </summary>
    public ResultadoDeLinea Agregar(Guid lineaIdNueva, Guid edicionId, bool esDigital, string? codigoDeSucursal, int cantidad, DateTimeOffset ahora)
    {
        if (Estado == EstadoDeCarrito.Fusionado)
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.CarritoFusionado);
        }

        if (!Cantidad.TryCrear(cantidad, out var pedida))
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.CantidadInvalida);
        }

        if (!esDigital && !CodigosDeSucursal.EsValido(codigoDeSucursal))
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.SucursalRequerida);
        }

        if (_lineas.Exists(l => l.EdicionId == edicionId && l.RetiroPendiente))
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.EdicionRetirada);
        }

        if (esDigital)
        {
            if (_lineas.Find(l => l.EdicionId == edicionId) is { } poseida)
            {
                Tocar(ahora);
                return ResultadoDeLinea.Exito(poseida.LineaId, Cantidad.Uno.Valor);
            }
        }
        else if (LineaQueAdmite(_lineas, edicionId, codigoDeSucursal!, pedida.Valor) is { } indice)
        {
            var existente = _lineas[indice];
            var suma = Cantidad.Crear(existente.Cantidad.Valor + pedida.Valor);
            _lineas[indice] = existente.ConCantidad(suma);
            Tocar(ahora);
            return ResultadoDeLinea.Exito(existente.LineaId, suma.Valor);
        }

        if (_lineas.Count >= MaximoDeLineas)
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.TopeDeLineas);
        }

        var nueva = LineaDeCarrito.Crear(lineaIdNueva, edicionId, esDigital, esDigital ? null : codigoDeSucursal, esDigital ? Cantidad.Uno : pedida, ahora);
        _lineas.Add(nueva);
        Tocar(ahora);
        return ResultadoDeLinea.Exito(nueva.LineaId, nueva.Cantidad.Valor);
    }

    /// <summary>La posición de la línea más antigua de esa edición y sucursal (sin retiro pendiente) que admite <paramref name="cantidad"/> unidades enteras sin pasar de 20 (CAR-40); nulo si ninguna.</summary>
    internal static int? LineaQueAdmite(IReadOnlyList<LineaDeCarrito> lineas, Guid edicionId, string codigoDeSucursal, int cantidad)
    {
        int? elegida = null;
        for (var i = 0; i < lineas.Count; i++)
        {
            var l = lineas[i];
            if (l.EdicionId == edicionId && !l.EsDigital && !l.RetiroPendiente && l.CodigoDeSucursal == codigoDeSucursal && l.Cantidad.Valor + cantidad <= Cantidad.Maxima
                && (elegida is null || l.AgregadaEn < lineas[elegida.Value].AgregadaEn))
            {
                elegida = i;
            }
        }

        return elegida;
    }

    /// <summary>Las unidades que suman las líneas de un libro en una sucursal (CAR-04), sin contar las retiradas del catálogo.</summary>
    public int Unidades(Guid edicionId, string codigoDeSucursal) =>
        _lineas.Where(l => l.EdicionId == edicionId && !l.RetiroPendiente && l.CodigoDeSucursal == codigoDeSucursal).Sum(l => l.Cantidad.Valor);

    /// <summary>Cambia la cantidad de una línea (CAR-02, CAR-44): una digital queda en una unidad y una línea anterior sin sucursal solo se puede quitar (CAR-45).</summary>
    public ResultadoDeLinea Modificar(Guid lineaId, int cantidad, DateTimeOffset ahora)
    {
        if (Estado == EstadoDeCarrito.Fusionado)
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.CarritoFusionado);
        }

        if (cantidad < Cantidad.Minima)
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.CantidadInvalida);
        }

        var indice = _lineas.FindIndex(l => l.LineaId == lineaId);
        if (indice < 0)
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.LineaNoEstaEnElCarrito);
        }

        var linea = _lineas[indice];
        if (linea.RetiroPendiente)
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.EdicionRetirada);
        }

        if (linea.EsImpresoSinSucursal)
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.LineaSinSucursal);
        }

        if (!Cantidad.TryCrear(linea.EsDigital ? 1 : cantidad, out var nueva))
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.TopeDeUnidades);
        }

        _lineas[indice] = linea.ConCantidad(nueva);
        Tocar(ahora);
        return ResultadoDeLinea.Exito(lineaId, nueva.Valor);
    }

    /// <summary>Quita una línea (CAR-02).</summary>
    public ResultadoDeLinea Eliminar(Guid lineaId, DateTimeOffset ahora)
    {
        if (Estado == EstadoDeCarrito.Fusionado)
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.CarritoFusionado);
        }

        if (_lineas.RemoveAll(l => l.LineaId == lineaId) == 0)
        {
            return ResultadoDeLinea.Rechazar(RechazoDeCarrito.LineaNoEstaEnElCarrito);
        }

        Tocar(ahora);
        return ResultadoDeLinea.Exito(lineaId, 0);
    }

    /// <summary>
    /// Quita de una línea la cantidad que se acaba de comprar (D16), también de una anterior sin sucursal (CAR-50): sin unidades, la línea se elimina. No es una acción de la persona. Una línea que no está no cambia nada.
    /// </summary>
    public void QuitarComprado(Guid lineaId, int cantidad, DateTimeOffset ahora)
    {
        var indice = _lineas.FindIndex(l => l.LineaId == lineaId);
        if (Estado == EstadoDeCarrito.Fusionado || indice < 0 || cantidad < 1)
        {
            return;
        }

        if (_lineas[indice].Cantidad.Valor <= cantidad)
        {
            _lineas.RemoveAt(indice);
        }
        else
        {
            _lineas[indice] = _lineas[indice].ConCantidad(Cantidad.Crear(_lineas[indice].Cantidad.Valor - cantidad));
        }

        Tocar(ahora, porLaPersona: false);
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

        var anteriores = _lineas.ToDictionary(l => l.LineaId);
        _lineas.Clear();
        foreach (var fusionada in resultado.Lineas)
        {
            _lineas.Add(anteriores.TryGetValue(fusionada.LineaId, out var anterior)
                ? anterior.ConCantidad(fusionada.Cantidad)
                : LineaDeCarrito.Crear(fusionada.LineaId, fusionada.EdicionId, fusionada.EsDigital, fusionada.CodigoDeSucursal, fusionada.Cantidad, ahora));
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
    /// Catálogo retiró una edición (CAT-18, <c>IRetiroDeEdicionDeCarritos</c>) y afecta a <b>todas</b> sus líneas: con <paramref name="diferir"/> falso (no hay checkout vivo que lo impida) las quita y devuelve <see cref="ResultadoDeRetiro.Quitada"/>; con
    /// <paramref name="diferir"/> verdadero (el checkout está <c>reservando</c>, <c>en_pago</c> o <c>pago_pendiente</c>) solo las marca para quitarlas después (<see cref="ResultadoDeRetiro.Anotada"/>, idempotente). Nunca falla ni mueve
    /// la caducidad del visitante: no es una acción de la persona.
    /// </summary>
    public ResultadoDeRetiro RetirarEdicion(Guid edicionId, bool diferir, DateTimeOffset ahora)
    {
        if (!_lineas.Exists(l => l.EdicionId == edicionId))
        {
            return ResultadoDeRetiro.NoEstaba;
        }

        if (!diferir)
        {
            _lineas.RemoveAll(l => l.EdicionId == edicionId);
            Tocar(ahora, porLaPersona: false);
            return ResultadoDeRetiro.Quitada;
        }

        var cambio = false;
        for (var i = 0; i < _lineas.Count; i++)
        {
            if (_lineas[i].EdicionId == edicionId && !_lineas[i].RetiroPendiente)
            {
                _lineas[i] = _lineas[i].ConRetiroPendiente();
                cambio = true;
            }
        }

        if (cambio)
        {
            Tocar(ahora, porLaPersona: false);
        }

        return ResultadoDeRetiro.Anotada;
    }

    /// <summary>
    /// Quita las líneas con retiro pendiente (<c>AplicarRetirosPendientes</c>, D21) y devuelve las ediciones retiradas (una vez cada una) para dejar su aviso. Es lo que hace toda salida del checkout de un estado vivo hacia <c>en_edicion</c>, <c>cancelado</c>,
    /// <c>reiniciado</c> o <c>pagado</c>. Sin retiros pendientes no cambia nada.
    /// </summary>
    public IReadOnlyList<EdicionRetirada> AplicarRetirosPendientes(DateTimeOffset ahora)
    {
        var retiradas = _lineas.Where(l => l.RetiroPendiente).Select(l => new EdicionRetirada(l.EdicionId, l.EsDigital)).Distinct().ToList();
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
