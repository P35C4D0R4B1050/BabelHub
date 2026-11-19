using System.Globalization;
using System.Text;
using Tipo = BabelHub.Ecommerce.Domain.Carrito.TipoDeEntrega;

namespace BabelHub.Ecommerce.Domain.Carrito;

/// <summary>
/// La compra en curso de una cuenta (D02, CAR-01): a lo más una viva por cuenta, con sus líneas, su entrega, su estado y los datos para reservar y pagar. <b>No cobra, no crea el pedido y no selecciona la fuente</b>: pasa al pago por
/// <see cref="PrepararPasoAlPago"/> y <see cref="ConfirmarEnPago"/> y recibe de Pagos <see cref="PagoPendiente"/>, <see cref="PagoAprobado"/> y <see cref="PagoResuelto"/>. Todo cambio de estado es una transición que devuelve su
/// resultado en vez de lanzar una excepción, y toda salida de un estado vivo quita del carrito las líneas con retiro pendiente (<see cref="Carrito.AplicarRetirosPendientes"/>, D21). Los instantes llegan como parámetros.
/// </summary>
public sealed class Checkout
{
    /// <summary>Un checkout <c>reservando</c> de más de dos minutos lo devuelve a <c>en_edicion</c> el trabajo de recuperación (D20).</summary>
    public static readonly TimeSpan DuracionMaximaDeReservando = TimeSpan.FromMinutes(2);

    /// <summary>El contacto se elimina a los 30 días de la última modificación hecha por la persona (CAR-34, D08).</summary>
    public static readonly TimeSpan RetencionDelContacto = TimeSpan.FromDays(30);

    /// <summary>El tope del código de una sucursal (el de <c>sucursales.listar</c> es corto): se valida antes de que entre al texto canónico.</summary>
    public const int LongitudMaximaDelCodigoDeSucursal = 40;

    private readonly List<LineaDelCheckout> _lineas;
    private PasoDelCheckout _pasoDeEdicion = PasoDelCheckout.Revision;

    private Checkout(Guid id, Guid cuentaId, Guid carritoId, List<LineaDelCheckout> lineas, DateTimeOffset creadoEn)
    {
        Id = id;
        CuentaId = cuentaId;
        CarritoId = carritoId;
        _lineas = lineas;
        CreadoEn = creadoEn;
        ModificadoPorLaPersonaEn = creadoEn;
        Version = 1;
        TipoDeEntrega = _lineas.TrueForAll(l => l.EsDigital) ? Tipo.Digital : null;
    }

    public Guid Id { get; }

    public Guid CuentaId { get; }

    public Guid CarritoId { get; }

    public EstadoDelCheckout Estado { get; private set; }

    /// <summary>Sube con cada cambio: es el control de concurrencia optimista del repositorio.</summary>
    public int Version { get; private set; }

    public DateTimeOffset CreadoEn { get; }

    /// <summary>La última vez que <b>la persona</b> cambió su entrega, su contacto, sus aceptaciones o el estado (D08): leerlo, renovar la sesión o los avisos de Pagos y los trabajos no la mueven. De ella cuenta la retención de 30 días.</summary>
    public DateTimeOffset ModificadoPorLaPersonaEn { get; private set; }

    public IReadOnlyList<LineaDelCheckout> Lineas => _lineas;

    /// <summary>Las aceptaciones de los textos legales con su versión y su fecha (CAR-22); solo se cambian por <see cref="Aceptar"/> y <see cref="RevalidarTextos"/>.</summary>
    public AceptacionesDelCheckout Aceptaciones { get; } = new();

    public TipoDeEntrega? TipoDeEntrega { get; private set; }

    public string? CodigoDeSucursalDeRecojo { get; private set; }

    /// <summary>La región de la entrega a domicilio (dato no personal que viaja a la selección de fuente); nula si no es a domicilio.</summary>
    public string? RegionDeEntrega { get; private set; }

    /// <summary>El contacto de una entrega a domicilio; solo existe con el checkout vivo y a domicilio (CAR-34).</summary>
    public ContactoDeEntrega? Contacto { get; private set; }

    public decimal? EnvioCongelado { get; private set; }

    public decimal? TotalCongelado { get; private set; }

    /// <summary>El identificador de la intención de pago, de la reserva y del pedido (D04); nulo hasta pasar al pago y tras un aborto.</summary>
    public Guid? PedidoId { get; private set; }

    /// <summary>El último <c>pedidoId</c> abortado: sirve para liberar su reserva y para reconocer una pestaña obsoleta.</summary>
    public Guid? PedidoDescartado { get; private set; }

    public DateTimeOffset? ReservandoDesde { get; private set; }

    public string? HuellaDeReserva { get; private set; }

    /// <summary>Sube cada vez que cambia la huella de reserva y en cada aborto: la clave de idempotencia de la reserva es <c>res-&lt;pedidoId&gt;-&lt;generación&gt;</c> (D04).</summary>
    public int GeneracionDeReserva { get; private set; }

    public string? HuellaDeInstantanea { get; private set; }

    public DateTimeOffset? ReservaExpiraEn { get; private set; }

    /// <summary>Desde cuándo falta liberar la reserva tras volver, reiniciar o abortar; se direcciona por <see cref="PedidoId"/> o <see cref="PedidoDescartado"/>.</summary>
    public DateTimeOffset? ReservaALiberarDesde { get; private set; }

    /// <summary>El límite de 30 minutos del pago pendiente, que fija Pagos (D13).</summary>
    public DateTimeOffset? LimitePendienteEn { get; private set; }

    public DateTimeOffset? PagoPendienteDesde { get; private set; }

    public bool EsVivo => Estado is EstadoDelCheckout.EnEdicion or EstadoDelCheckout.Reservando or EstadoDelCheckout.EnPago or EstadoDelCheckout.PagoPendiente;

    public bool EsSoloDigital => _lineas.TrueForAll(l => l.EsDigital);

    /// <summary>Un carrito solo digital no tiene entrega (CAR-07).</summary>
    public bool RequiereEntrega => !EsSoloDigital;

    /// <summary>Solo los impresos se reservan; un carrito solo digital no consulta ni reserva en el Inventario (CAR-07, regla 12).</summary>
    public bool RequiereReserva => _lineas.Exists(l => !l.EsDigital);

    /// <summary>Cinco pasos, o cuatro si el carrito es solo digital (CAR-15).</summary>
    public int TotalDePasos => EsSoloDigital ? 4 : 5;

    public PasoDelCheckout Paso => Estado switch
    {
        EstadoDelCheckout.EnEdicion => _pasoDeEdicion,
        EstadoDelCheckout.Reservando or EstadoDelCheckout.EnPago or EstadoDelCheckout.PagoPendiente => PasoDelCheckout.Pago,
        EstadoDelCheckout.Pagado => PasoDelCheckout.Confirmacion,
        _ => PasoDelCheckout.Revision,
    };

    /// <summary>El número del paso actual: en un carrito solo digital la entrega no cuenta y los pasos siguientes se corren uno.</summary>
    public int NumeroDePaso => EsSoloDigital && Paso >= PasoDelCheckout.DatosYAceptaciones ? (int)Paso - 1 : (int)Paso;

    /// <summary>Un retiro de edición se aplica de inmediato salvo que el checkout esté en uno de estos estados (D21).</summary>
    public static bool SostieneRetirosPendientes(EstadoDelCheckout estado) =>
        estado is EstadoDelCheckout.Reservando or EstadoDelCheckout.EnPago or EstadoDelCheckout.PagoPendiente;

    /// <summary>
    /// Arma el checkout de una cuenta a partir de las líneas del carrito (CAR-05): un <c>lineaId</c> propio por línea, el estado <c>en_edicion</c> y, si todas son digitales, la entrega <c>digital</c>. Una edición no puede repetirse.
    /// </summary>
    public static Checkout Iniciar(Guid id, Guid cuentaId, Guid carritoId, IEnumerable<LineaParaCheckout> lineas, Func<Guid> nuevoLineaId, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        ArgumentNullException.ThrowIfNull(nuevoLineaId);
        if (id == Guid.Empty || cuentaId == Guid.Empty || carritoId == Guid.Empty)
        {
            throw new ArgumentException("El checkout necesita su identificador, su cuenta y su carrito.");
        }

        var armadas = lineas.Select(l => LineaDelCheckout.Crear(nuevoLineaId(), l)).ToList();
        if (armadas.Count == 0)
        {
            throw new ArgumentException("Un checkout necesita al menos una línea.", nameof(lineas));
        }

        if (armadas.Select(l => l.EdicionId).Distinct().Count() != armadas.Count)
        {
            throw new ArgumentException("Una edición no puede repetirse en el checkout.", nameof(lineas));
        }

        return new Checkout(id, cuentaId, carritoId, armadas, ahora);
    }

    /// <summary>
    /// Vuelve a armar las líneas desde el carrito (solo en <c>en_edicion</c>): conserva el <c>lineaId</c> y lo informado de las ediciones que siguen y da uno nuevo a las demás. Sin líneas se rechaza; si ahora es solo digital quita la
    /// entrega y el contacto, y si dejó de serlo la entrega queda por elegir.
    /// </summary>
    public ResultadoDeCheckout SincronizarLineas(IEnumerable<LineaParaCheckout> nuevas, Func<Guid> nuevoLineaId, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(nuevas);
        ArgumentNullException.ThrowIfNull(nuevoLineaId);
        if (RechazoPorNoEstarEnEdicion() is { } rechazo)
        {
            return ResultadoDeCheckout.Rechazar(rechazo);
        }

        var lista = nuevas.ToList();
        if (lista.Count == 0)
        {
            return ResultadoDeCheckout.Rechazar(RechazoDeCheckout.SinLineas);
        }

        if (lista.Count == _lineas.Count && lista.Zip(_lineas).All(par => par.Second.MismosDatosQue(par.First)))
        {
            return ResultadoDeCheckout.SinCambios();
        }

        var anteriores = _lineas.ToDictionary(l => l.EdicionId);
        var armadas = lista.Select(l => anteriores.TryGetValue(l.EdicionId, out var anterior) ? anterior.ConDatos(l) : LineaDelCheckout.Crear(nuevoLineaId(), l)).ToList();
        _lineas.Clear();
        _lineas.AddRange(armadas);

        if (EsSoloDigital)
        {
            TipoDeEntrega = Tipo.Digital;
            LimpiarEntrega();
        }
        else if (TipoDeEntrega == Tipo.Digital)
        {
            TipoDeEntrega = null;
        }

        Cambiar(porLaPersona: true, ahora);
        return ResultadoDeCheckout.Aplicada();
    }

    /// <summary>Los textos requeridos que la persona aún no aceptó (o cuya aceptación se invalidó): términos y privacidad, y el reembolso digital si hay digitales (CAR-12, CAR-13).</summary>
    public IReadOnlyList<TextoLegal> TextosPorAceptar() => Aceptaciones.PorAceptar(_lineas.Exists(l => l.EsDigital));

    /// <summary>
    /// La persona acepta un texto con su versión (CAR-12, CAR-13, CAR-22): se guarda la versión y la fecha, y aceptar de nuevo la misma versión no cambia nada (conserva la primera fecha). Solo en <c>en_edicion</c>: con <c>en_pago</c> hay
    /// que reabrir la intención antes (<see cref="Editar"/>). Aceptar el reembolso digital sin digitales no hace nada: no se exige.
    /// </summary>
    public ResultadoDeCheckout Aceptar(TextoLegal texto, string version, DateTimeOffset ahora)
    {
        if (RechazoPorNoEstarEnEdicion() is { } rechazo)
        {
            return ResultadoDeCheckout.Rechazar(rechazo);
        }

        if (texto == TextoLegal.ReembolsoDigital && !_lineas.Exists(l => l.EsDigital))
        {
            return ResultadoDeCheckout.SinCambios();
        }

        if (!Aceptaciones.Aceptar(texto, version, ahora))
        {
            return ResultadoDeCheckout.SinCambios();
        }

        Cambiar(porLaPersona: true, ahora);
        return ResultadoDeCheckout.Aplicada();
    }

    /// <summary>
    /// Compara las aceptaciones con las versiones vigentes de los textos (CAR-28): las que ya no coinciden se invalidan y se vuelven a pedir antes de pagar. Solo en <c>en_edicion</c>; no es una acción de la persona (no mueve su
    /// retención). Sin cambios no toca la versión del checkout.
    /// </summary>
    public ResultadoDeRevalidacion RevalidarTextos(IReadOnlyDictionary<TextoLegal, string> vigentes, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(vigentes);
        if (RechazoPorNoEstarEnEdicion() is { } rechazo)
        {
            return new ResultadoDeRevalidacion([], rechazo);
        }

        var invalidadas = Aceptaciones.InvalidarLasQueCambiaron(vigentes, _lineas.Exists(l => l.EsDigital));
        if (invalidadas.Count > 0)
        {
            Cambiar(porLaPersona: false, ahora);
        }

        return new ResultadoDeRevalidacion(invalidadas, null);
    }

    /// <summary>La persona confirmó la revisión del carrito: pasa a la entrega, o directo a los datos si el carrito es solo digital (CAR-07, CAR-15).</summary>
    public ResultadoDeCheckout ConfirmarRevision(DateTimeOffset ahora)
    {
        if (RechazoPorNoEstarEnEdicion() is { } rechazo)
        {
            return ResultadoDeCheckout.Rechazar(rechazo);
        }

        var siguiente = EsSoloDigital ? PasoDelCheckout.DatosYAceptaciones : PasoDelCheckout.Entrega;
        if (_pasoDeEdicion >= siguiente)
        {
            return ResultadoDeCheckout.SinCambios();
        }

        _pasoDeEdicion = siguiente;
        Cambiar(porLaPersona: true, ahora);
        return ResultadoDeCheckout.Aplicada();
    }

    /// <summary>Recojo en una sucursal (CAR-08): quita el contacto y la región si antes era a domicilio. Un carrito solo digital no tiene entrega. Elegir lo mismo no cambia nada.</summary>
    public ResultadoDeCheckout ElegirRecojo(string codigoDeSucursal, DateTimeOffset ahora)
    {
        if (!TextoDeEntrega.TryNormalizar(codigoDeSucursal, LongitudMaximaDelCodigoDeSucursal, out var codigo))
        {
            throw new ArgumentException("El código de la sucursal de recojo es obligatorio, de hasta 40 caracteres y sin caracteres de control.", nameof(codigoDeSucursal));
        }

        if (RechazarEntrega() is { } rechazo)
        {
            return rechazo;
        }

        if (TipoDeEntrega == Tipo.Recojo && CodigoDeSucursalDeRecojo == codigo)
        {
            return ResultadoDeCheckout.SinCambios();
        }

        LimpiarEntrega();
        TipoDeEntrega = Tipo.Recojo;
        CodigoDeSucursalDeRecojo = codigo;
        AvanzarADatos();
        Cambiar(porLaPersona: true, ahora);
        return ResultadoDeCheckout.Aplicada();
    }

    /// <summary>Despacho a domicilio con su contacto (CAR-08, CAR-30): guarda el contacto y la región de su dirección. Elegir lo mismo no cambia nada.</summary>
    public ResultadoDeCheckout ElegirDomicilio(ContactoDeEntrega contacto, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(contacto);
        if (RechazarEntrega() is { } rechazo)
        {
            return rechazo;
        }

        if (TipoDeEntrega == Tipo.Domicilio && contacto.Equals(Contacto))
        {
            return ResultadoDeCheckout.SinCambios();
        }

        LimpiarEntrega();
        TipoDeEntrega = Tipo.Domicilio;
        Contacto = contacto;
        RegionDeEntrega = contacto.Direccion.Region;
        AvanzarADatos();
        Cambiar(porLaPersona: true, ahora);
        return ResultadoDeCheckout.Aplicada();
    }

    /// <summary>
    /// Guarda lo que la consulta previa informó para un impreso: la fuente, si requiere traslado y el plazo en días (nulo si «a confirmar», CAR-09). Es lo que se compara al reservar (CAR-27); tras un aborto por cambio de plazo guarda lo
    /// que devolvió <c>Reservar</c>. No es una acción de la persona. Una edición que no está en el checkout no cambia nada.
    /// </summary>
    public ResultadoDeCheckout RegistrarLoInformado(Guid edicionId, string? codigoDeFuente, bool? requiereTraslado, int? plazoDias, DateTimeOffset consultadoEn)
    {
        if (RechazoPorNoEstarEnEdicion() is { } rechazo)
        {
            return ResultadoDeCheckout.Rechazar(rechazo);
        }

        var indice = _lineas.FindIndex(l => l.EdicionId == edicionId);
        if (indice < 0 || _lineas[indice].EsDigital)
        {
            return ResultadoDeCheckout.SinCambios();
        }

        _lineas[indice] = _lineas[indice].ConLoInformado(codigoDeFuente, requiereTraslado, plazoDias, consultadoEn);
        Cambiar(porLaPersona: false, consultadoEn);
        return ResultadoDeCheckout.Aplicada();
    }

    /// <summary>El total de las líneas con el envío fijo solo si la entrega es a domicilio (CAR-23); sin entrega elegida no lleva envío.</summary>
    public TotalDeCompra CalcularTotal(CostoDeEnvio costoConfigurado) =>
        TotalDeCompra.Calcular(_lineas.Select(l => new ImporteDeLinea(l.PrecioUnitario, l.Cantidad.Valor)), TipoDeEntrega ?? Tipo.Recojo, costoConfigurado);

    // ------------------------------------------------------------------ pasar al pago

    /// <summary>
    /// El paso 1 de pasar al pago (D18): con la entrega elegida y sin retiros pendientes, congela el envío y el total, calcula las dos huellas, asigna el <c>pedidoId</c> (conserva el que ya tiene tras editar con el pago abierto), sube la
    /// generación de reserva <b>solo si cambió la huella de reserva</b> y pasa a <c>reservando</c>. Una repetición con lo mismo continúa sin cambiar nada (<see cref="EfectoDeTransicion.SinCambios"/>: el doble clic y el atajo de «ya en
    /// pago»). Las comprobaciones de cuenta, licencia, posesión y aceptaciones las hace la Aplicación antes. Las huellas las calcula quien llama a partir del texto canónico que se le da: ese texto lleva datos personales y no se guarda ni se registra.
    /// </summary>
    public ResultadoDeCheckout PrepararPasoAlPago(
        Guid pedidoIdNuevo,
        CostoDeEnvio costoConfigurado,
        Func<string, string> calcularHuellaDeReserva,
        Func<string, string> calcularHuellaDeInstantanea,
        Carrito carrito,
        DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(costoConfigurado);
        ArgumentNullException.ThrowIfNull(calcularHuellaDeReserva);
        ArgumentNullException.ThrowIfNull(calcularHuellaDeInstantanea);
        ExigirCarritoPropio(carrito);
        if (pedidoIdNuevo == Guid.Empty)
        {
            throw new ArgumentException("El pedido necesita su identificador.", nameof(pedidoIdNuevo));
        }

        switch (Estado)
        {
            case EstadoDelCheckout.PagoPendiente:
                return ResultadoDeCheckout.Rechazar(RechazoDeCheckout.PagoPendiente);
            case EstadoDelCheckout.Pagado or EstadoDelCheckout.Cancelado or EstadoDelCheckout.Reiniciado:
                return ResultadoDeCheckout.Rechazar(RechazoDeCheckout.YaTerminado);
            case EstadoDelCheckout.Reservando or EstadoDelCheckout.EnPago:
                var misma = HuellaDeReserva == calcularHuellaDeReserva(CanonicoDeReserva()) && HuellaDeInstantanea == calcularHuellaDeInstantanea(CanonicoDeInstantanea(EnvioCongelado ?? 0m, TotalCongelado ?? 0m));
                return misma
                    ? ResultadoDeCheckout.SinCambios()
                    : ResultadoDeCheckout.Rechazar(Estado == EstadoDelCheckout.Reservando ? RechazoDeCheckout.EnCurso : RechazoDeCheckout.EstadoNoPermite);
        }

        if (_lineas.Count == 0)
        {
            return ResultadoDeCheckout.Rechazar(RechazoDeCheckout.SinLineas);
        }

        if (carrito.TieneRetirosPendientes)
        {
            return ResultadoDeCheckout.Rechazar(RechazoDeCheckout.HayRetiroPendiente);
        }

        if (TextosPorAceptar().Count > 0)
        {
            return ResultadoDeCheckout.Rechazar(RechazoDeCheckout.AceptacionRequerida);
        }

        if (RequiereEntrega && TipoDeEntrega is null)
        {
            return ResultadoDeCheckout.Rechazar(RechazoDeCheckout.EntregaNoElegida);
        }

        if (TipoDeEntrega == Tipo.Domicilio && Contacto is null)
        {
            return ResultadoDeCheckout.Rechazar(RechazoDeCheckout.FaltaElContacto);
        }

        var total = CalcularTotal(costoConfigurado);
        EnvioCongelado = total.Envio;
        TotalCongelado = total.Total;

        var huellaDeReserva = calcularHuellaDeReserva(CanonicoDeReserva());
        if (huellaDeReserva != HuellaDeReserva)
        {
            GeneracionDeReserva++;
            HuellaDeReserva = huellaDeReserva;
        }

        HuellaDeInstantanea = calcularHuellaDeInstantanea(CanonicoDeInstantanea(total.Envio, total.Total));
        PedidoId ??= pedidoIdNuevo;
        ReservandoDesde = ahora;
        ReservaExpiraEn = null;
        Estado = EstadoDelCheckout.Reservando;
        Cambiar(porLaPersona: true, ahora);
        return ResultadoDeCheckout.Aplicada();
    }

    /// <summary>
    /// El paso 3b (D18): comprueba que el checkout siga <c>reservando</c> con el mismo <c>pedidoId</c>, generación y huella y pasa a <c>en_pago</c> con la expiración de la reserva. Ya <c>en_pago</c> con lo mismo es un éxito idempotente; con
    /// otra cosa, <see cref="EfectoDeTransicion.NoAplica"/>. Con una línea con retiro pendiente <b>no cambia el estado</b> (<see cref="EfectoDeTransicion.ConRetiroPendiente"/>): quien llama reabre la intención y vuelve a <c>en_edicion</c>.
    /// </summary>
    public ResultadoDeCheckout ConfirmarEnPago(Guid pedidoId, int generacionDeReserva, string huellaDeInstantanea, DateTimeOffset reservaExpiraEn, Carrito carrito)
    {
        ArgumentNullException.ThrowIfNull(huellaDeInstantanea);
        ExigirCarritoPropio(carrito);
        var coincide = PedidoId == pedidoId && GeneracionDeReserva == generacionDeReserva && HuellaDeInstantanea == huellaDeInstantanea;
        if (Estado == EstadoDelCheckout.EnPago && coincide)
        {
            return ResultadoDeCheckout.SinCambios();
        }

        if (Estado != EstadoDelCheckout.Reservando || !coincide)
        {
            return ResultadoDeCheckout.De(EfectoDeTransicion.NoAplica);
        }

        if (carrito.TieneRetirosPendientes)
        {
            return ResultadoDeCheckout.De(EfectoDeTransicion.ConRetiroPendiente);
        }

        Estado = EstadoDelCheckout.EnPago;
        ReservaExpiraEn = reservaExpiraEn;
        ReservandoDesde = null;
        Cambiar(porLaPersona: false);
        return ResultadoDeCheckout.Aplicada();
    }

    // ------------------------------------------------------------------ editar, volver y reiniciar

    /// <summary>
    /// La persona edita el carrito, la entrega, el contacto o las aceptaciones (CAR-26, D21): con <c>en_edicion</c> no hay nada que cambiar; con <c>en_pago</c> (y ya reabierta la intención) lo devuelve a <c>en_edicion</c> conservando el
    /// <c>pedidoId</c> y la reserva y aplicando los retiros pendientes; con <c>pago_pendiente</c> o <c>reservando</c> se rechaza.
    /// </summary>
    public ResultadoDeCheckout Editar(Carrito carrito, DateTimeOffset ahora)
    {
        ExigirCarritoPropio(carrito);
        return Estado switch
        {
            EstadoDelCheckout.EnEdicion => ResultadoDeCheckout.SinCambios(),
            EstadoDelCheckout.EnPago => VolverAEnEdicion(carrito, ahora, porLaPersona: true),
            _ => ResultadoDeCheckout.Rechazar(RechazoPorEstado()!.Value),
        };
    }

    /// <summary>
    /// Después de reabrir la intención de pago (<c>Reabrir</c>), devuelve el checkout a <c>en_edicion</c> desde <c>en_pago</c> o <c>pago_pendiente</c> conservando el <c>pedidoId</c> y aplicando los retiros pendientes: lo que hacen el
    /// siguiente <c>POST</c> autenticado y <c>PagoResuelto</c> con un retiro pendiente (D21). No es una acción de la persona.
    /// </summary>
    public ResultadoDeCheckout VolverAEnEdicionTrasReabrir(Carrito carrito, DateTimeOffset ahora)
    {
        ExigirCarritoPropio(carrito);
        return Estado switch
        {
            EstadoDelCheckout.EnEdicion => ResultadoDeCheckout.SinCambios(),
            EstadoDelCheckout.EnPago or EstadoDelCheckout.PagoPendiente => VolverAEnEdicion(carrito, ahora, porLaPersona: false),
            _ => ResultadoDeCheckout.De(EfectoDeTransicion.NoAplica),
        };
    }

    /// <summary>«Volver al carrito» (CAR-19, CAR-26): de <c>en_edicion</c> o <c>en_pago</c> a <c>cancelado</c>, quitando los retiros pendientes y anotando la reserva que liberar; con el pago pendiente o <c>reservando</c> se rechaza.</summary>
    public ResultadoDeCheckout Volver(Carrito carrito, DateTimeOffset ahora) => Terminar(EstadoDelCheckout.Cancelado, carrito, ahora, porLaPersona: true);

    /// <summary>Reiniciar el checkout (CAR-11): lo mismo que <see cref="Volver"/> pero deja el checkout <c>reiniciado</c>; la persona empieza otro con un <c>pedidoId</c> nuevo.</summary>
    public ResultadoDeCheckout Reiniciar(Carrito carrito, DateTimeOffset ahora) => Terminar(EstadoDelCheckout.Reiniciado, carrito, ahora, porLaPersona: true);

    // ------------------------------------------------------------------ abortar y recuperar

    /// <summary>
    /// Aborta el paso al pago (D21), <b>condicional</b> al estado, el <c>pedidoId</c> y la generación que la solicitud tenía al empezar: si el checkout ya cambió, no hace nada. Vuelve a <c>en_edicion</c>, descarta el <c>pedidoId</c> a
    /// <see cref="PedidoDescartado"/> (el siguiente intento genera uno nuevo), sube la generación, anota la reserva que liberar (solo si hay impresos) y aplica los retiros pendientes. Desde <c>en_pago</c> quien llama debe haber cancelado antes
    /// la intención con éxito; desde <c>reservando</c> lo hace después.
    /// </summary>
    public ResultadoDeCheckout AbortarPasoAlPago(EstadoDelCheckout desde, Guid pedidoId, int generacionDeReserva, Carrito carrito, DateTimeOffset ahora)
    {
        ExigirCarritoPropio(carrito);
        if (desde is not (EstadoDelCheckout.Reservando or EstadoDelCheckout.EnPago))
        {
            throw new ArgumentException("Solo se aborta desde reservando o en_pago.", nameof(desde));
        }

        if (Estado != desde || PedidoId != pedidoId || GeneracionDeReserva != generacionDeReserva)
        {
            return ResultadoDeCheckout.De(EfectoDeTransicion.NoAplica);
        }

        PedidoDescartado = PedidoId;
        PedidoId = null;
        GeneracionDeReserva++;
        ReservaALiberarDesde = RequiereReserva ? ahora : null;
        ReservaExpiraEn = null;
        ReservandoDesde = null;
        Estado = EstadoDelCheckout.EnEdicion;
        var retiradas = carrito.AplicarRetirosPendientes(ahora);
        Cambiar(porLaPersona: false, ahora);
        return ResultadoDeCheckout.Aplicada(retiradas);
    }

    /// <summary>
    /// El Inventario respondió 429 al reservar (DD-04, SEL-36, SEL-37): <b>no se aborta</b>, porque el Inventario conserva la reserva vigente (si la hay) y abortar la liberaría. Vuelve de <c>reservando</c> a <c>en_edicion</c> conservando el
    /// <c>pedidoId</c>, la generación y la reserva anotada (nada que liberar): la persona espera al instante de reintento o reinicia. Condicional al <c>pedidoId</c> y la generación de la solicitud: si el checkout ya cambió, no hace nada.
    /// </summary>
    public ResultadoDeCheckout VolverALaRevisionPorLimite(Guid pedidoId, int generacionDeReserva, DateTimeOffset ahora)
    {
        if (Estado != EstadoDelCheckout.Reservando || PedidoId != pedidoId || GeneracionDeReserva != generacionDeReserva)
        {
            return ResultadoDeCheckout.De(EfectoDeTransicion.NoAplica);
        }

        ReservandoDesde = null;
        Estado = EstadoDelCheckout.EnEdicion;
        Cambiar(porLaPersona: false, ahora);
        return ResultadoDeCheckout.Aplicada();
    }

    /// <summary>Un <c>reservando</c> de dos minutos o más está atascado (un corte entre los pasos): lo recupera el trabajo de fondo (D20).</summary>
    public bool ReservandoVencido(DateTimeOffset ahora) => Estado == EstadoDelCheckout.Reservando && ReservandoDesde is { } desde && ahora - desde >= DuracionMaximaDeReservando;

    /// <summary>
    /// El contacto solo existe con el checkout vivo y a domicilio (el <c>CHECK</c> de la tabla, CAR-34): un checkout terminado, a recojo o solo digital nunca lo conserva. Toda transición mantiene esta regla.
    /// </summary>
    public bool CumpleLaRetencionDelContacto => Contacto is null || (EsVivo && TipoDeEntrega == Tipo.Domicilio);

    /// <summary>
    /// Pasaron 30 días desde la última modificación hecha por la persona (CAR-34, D08, AC-CAR-30): a los 30 días exactos vence y a 29 días y 23 horas no. Sin contacto no hay nada que vencer.
    /// </summary>
    public bool ContactoVencido(DateTimeOffset ahora) => Contacto is not null && ahora - ModificadoPorLaPersonaEn >= RetencionDelContacto;

    /// <summary>
    /// Elimina el contacto de entrega y anula la huella de instantánea (CAR-34): por vencimiento (solo en <c>en_edicion</c> y si ya venció; deja la entrega por elegir) o por cancelar o reemplazar la cuenta (en cualquier estado; el
    /// checkout se elimina después). Sin contacto no cambia nada. No mueve la retención de la persona. Los demás motivos los aplican por sí solas las transiciones (aprobar el pago, cancelar, reiniciar, cambiar a recojo o a solo digital).
    /// </summary>
    public ResultadoDeCheckout EliminarContacto(MotivoDeEliminacionDeContacto motivo, DateTimeOffset ahora)
    {
        if (motivo is not (MotivoDeEliminacionDeContacto.Vencido or MotivoDeEliminacionDeContacto.CuentaCancelada or MotivoDeEliminacionDeContacto.CuentaReemplazada))
        {
            throw new ArgumentException("Ese motivo lo aplica la propia transición del checkout; aquí solo valen el vencimiento y la cuenta cancelada o reemplazada.", nameof(motivo));
        }

        if (Contacto is null)
        {
            return ResultadoDeCheckout.SinCambios();
        }

        if (motivo == MotivoDeEliminacionDeContacto.Vencido && (Estado is not (EstadoDelCheckout.EnEdicion or EstadoDelCheckout.PagoPendiente) || !ContactoVencido(ahora)))
        {
            return ResultadoDeCheckout.De(EfectoDeTransicion.NoAplica);
        }

        QuitarContacto();
        if (Estado == EstadoDelCheckout.EnEdicion && TipoDeEntrega == Tipo.Domicilio)
        {
            TipoDeEntrega = null;
            RegionDeEntrega = null;
        }

        Cambiar(porLaPersona: false, ahora);
        return ResultadoDeCheckout.Aplicada();
    }

    /// <summary>
    /// Un checkout abandonado con el cobro sin empezar (<c>reservando</c> o <c>en_pago</c>) que lleva 30 días sin que la persona lo toque se termina: pasa a <c>cancelado</c>, quita el contacto, anota la reserva que liberar y aplica los
    /// retiros pendientes (CAR-34, AC-CAR-30). Lo invoca el trabajo de fondo, que antes cancela la intención de pago. Con <c>en_edicion</c> el contacto se quita con <see cref="EliminarContacto"/>; un <c>pago_pendiente</c> lo resuelve Pagos.
    /// </summary>
    public ResultadoDeCheckout CancelarPorVencimiento(Carrito carrito, DateTimeOffset ahora)
    {
        ExigirCarritoPropio(carrito);
        if (Estado is not (EstadoDelCheckout.Reservando or EstadoDelCheckout.EnPago) || ahora - ModificadoPorLaPersonaEn < RetencionDelContacto)
        {
            return ResultadoDeCheckout.De(EfectoDeTransicion.NoAplica);
        }

        return Terminar(EstadoDelCheckout.Cancelado, carrito, ahora, porLaPersona: false, desdeReservando: true);
    }

    /// <summary>La reserva se liberó en el Inventario (o ya no existía): quita la anotación.</summary>
    public void LiberacionConfirmada()
    {
        if (ReservaALiberarDesde is not null)
        {
            ReservaALiberarDesde = null;
            Version++;
        }
    }

    // ------------------------------------------------------------------ lo que informa Pagos

    /// <summary>
    /// <c>PagoPendiente</c> (D13), condicional al <c>pedidoId</c>: de <c>en_pago</c> a <c>pago_pendiente</c> con los instantes que fija Pagos (el límite de 30 minutos y la expiración de la reserva). Mientras el checkout está <c>reservando</c> responde
    /// <see cref="EfectoDeTransicion.AunNo"/> y Pagos reintenta; si ya está pendiente o pagado, es idempotente.
    /// </summary>
    public ResultadoDeCheckout PagoPendiente(Guid pedidoId, DateTimeOffset limiteEn, DateTimeOffset reservaExpiraEn, DateTimeOffset ahora)
    {
        if (DeOtroPedido(pedidoId) is { } ajeno)
        {
            return ajeno;
        }

        switch (Estado)
        {
            case EstadoDelCheckout.Reservando:
                return ResultadoDeCheckout.De(EfectoDeTransicion.AunNo);
            case EstadoDelCheckout.PagoPendiente or EstadoDelCheckout.Pagado:
                return ResultadoDeCheckout.SinCambios();
            case EstadoDelCheckout.EnPago:
                Estado = EstadoDelCheckout.PagoPendiente;
                LimitePendienteEn = limiteEn;
                ReservaExpiraEn = reservaExpiraEn;
                PagoPendienteDesde = ahora;
                Cambiar(porLaPersona: false, ahora);
                return ResultadoDeCheckout.Aplicada();
            default:
                return ResultadoDeCheckout.De(EfectoDeTransicion.NoAplica);
        }
    }

    /// <summary>
    /// <c>PagoAprobado</c> (D13, D16), condicional al <c>pedidoId</c>: de <c>pago_pendiente</c> o <c>en_pago</c> a <c>pagado</c>, quitando los retiros pendientes. Quitar del carrito lo comprado lo hace la Aplicación con las líneas del checkout. Mientras reserva responde
    /// <see cref="EfectoDeTransicion.AunNo"/>; ya pagado, es idempotente.
    /// </summary>
    public ResultadoDeCheckout PagoAprobado(Guid pedidoId, Carrito carrito, DateTimeOffset ahora)
    {
        ExigirCarritoPropio(carrito);
        if (DeOtroPedido(pedidoId) is { } ajeno)
        {
            return ajeno;
        }

        switch (Estado)
        {
            case EstadoDelCheckout.Reservando:
                return ResultadoDeCheckout.De(EfectoDeTransicion.AunNo);
            case EstadoDelCheckout.Pagado:
                return ResultadoDeCheckout.SinCambios();
            case EstadoDelCheckout.PagoPendiente or EstadoDelCheckout.EnPago:
                Estado = EstadoDelCheckout.Pagado;
                QuitarContacto();
                LimitePendienteEn = null;
                PagoPendienteDesde = null;
                var retiradas = carrito.AplicarRetirosPendientes(ahora);
                Cambiar(porLaPersona: false, ahora);
                return ResultadoDeCheckout.Aplicada(retiradas);
            default:
                return ResultadoDeCheckout.De(EfectoDeTransicion.NoAplica);
        }
    }

    /// <summary>
    /// <c>PagoResuelto</c> (D13, D21), condicional al <c>pedidoId</c>: un pago <c>vencido</c> o <c>rechazado</c> vuelve a <c>en_pago</c> si la reserva sigue vigente, o a <c>cancelado</c> (anotando la liberación) si expiró; con un retiro pendiente no cambia el estado
    /// (<see cref="EfectoDeTransicion.ConRetiroPendiente"/>: quien llama reabre la intención y vuelve a <c>en_edicion</c>). <c>Compensado</c> cancela el checkout desde <c>pago_pendiente</c> o <c>en_pago</c>. Mientras reserva
    /// responde <see cref="EfectoDeTransicion.AunNo"/>; si el checkout ya resolvió, es idempotente.
    /// </summary>
    public ResultadoDeCheckout PagoResuelto(Guid pedidoId, MotivoDePagoResuelto motivo, Carrito carrito, DateTimeOffset ahora)
    {
        ExigirCarritoPropio(carrito);
        if (DeOtroPedido(pedidoId) is { } ajeno)
        {
            return ajeno;
        }

        if (Estado == EstadoDelCheckout.Reservando)
        {
            return ResultadoDeCheckout.De(EfectoDeTransicion.AunNo);
        }

        if (motivo == MotivoDePagoResuelto.Compensado)
        {
            return Estado switch
            {
                EstadoDelCheckout.Cancelado => ResultadoDeCheckout.SinCambios(),
                EstadoDelCheckout.PagoPendiente or EstadoDelCheckout.EnPago => Terminar(EstadoDelCheckout.Cancelado, carrito, ahora, porLaPersona: false),
                _ => ResultadoDeCheckout.De(EfectoDeTransicion.NoAplica),
            };
        }

        switch (Estado)
        {
            case EstadoDelCheckout.PagoPendiente:
                if (!ReservaVigente(ahora))
                {
                    return Terminar(EstadoDelCheckout.Cancelado, carrito, ahora, porLaPersona: false);
                }

                if (carrito.TieneRetirosPendientes)
                {
                    return ResultadoDeCheckout.De(EfectoDeTransicion.ConRetiroPendiente);
                }

                Estado = EstadoDelCheckout.EnPago;
                LimitePendienteEn = null;
                PagoPendienteDesde = null;
                Cambiar(porLaPersona: false, ahora);
                return ResultadoDeCheckout.Aplicada();
            case EstadoDelCheckout.EnPago or EstadoDelCheckout.EnEdicion:
                return ResultadoDeCheckout.SinCambios();
            default:
                return ResultadoDeCheckout.De(EfectoDeTransicion.NoAplica);
        }
    }

    /// <summary>
    /// <c>PagoResuelto</c> con un retiro pendiente y <c>Reabrir</c> respondió <c>NoCancelable</c> (hay un intento activo, D21): el pago pendiente vuelve a <c>en_pago</c> <b>conservando el retiro pendiente</b> (no hay estado atascado: un cobro
    /// nuevo exige <c>en_pago</c> sin retiro). Condicional al <c>pedidoId</c>; ya en <c>en_pago</c> es idempotente; mientras reserva, <see cref="EfectoDeTransicion.AunNo"/>.
    /// </summary>
    public ResultadoDeCheckout VolverAEnPagoConservandoElRetiro(Guid pedidoId, DateTimeOffset ahora)
    {
        if (DeOtroPedido(pedidoId) is { } ajeno)
        {
            return ajeno;
        }

        switch (Estado)
        {
            case EstadoDelCheckout.Reservando:
                return ResultadoDeCheckout.De(EfectoDeTransicion.AunNo);
            case EstadoDelCheckout.EnPago:
                return ResultadoDeCheckout.SinCambios();
            case EstadoDelCheckout.PagoPendiente:
                Estado = EstadoDelCheckout.EnPago;
                LimitePendienteEn = null;
                PagoPendienteDesde = null;
                Cambiar(porLaPersona: false, ahora);
                return ResultadoDeCheckout.Aplicada();
            default:
                return ResultadoDeCheckout.De(EfectoDeTransicion.NoAplica);
        }
    }

    /// <summary>
    /// <c>Reabrir</c> respondió <c>Cerrada</c> o <c>NoExiste</c> (la intención ya no admite cobro, D21): cancela el checkout desde <c>en_pago</c> o <c>pago_pendiente</c> sin que lo pida la persona, anotando la reserva que liberar, quitando el
    /// contacto y aplicando los retiros pendientes. Condicional al <c>pedidoId</c>; ya cancelado es idempotente; mientras reserva, <see cref="EfectoDeTransicion.AunNo"/>.
    /// </summary>
    public ResultadoDeCheckout CancelarPorIntencionCerrada(Guid pedidoId, Carrito carrito, DateTimeOffset ahora)
    {
        ExigirCarritoPropio(carrito);
        if (DeOtroPedido(pedidoId) is { } ajeno)
        {
            return ajeno;
        }

        return Estado switch
        {
            EstadoDelCheckout.Reservando => ResultadoDeCheckout.De(EfectoDeTransicion.AunNo),
            EstadoDelCheckout.Cancelado => ResultadoDeCheckout.SinCambios(),
            EstadoDelCheckout.PagoPendiente or EstadoDelCheckout.EnPago => Terminar(EstadoDelCheckout.Cancelado, carrito, ahora, porLaPersona: false),
            _ => ResultadoDeCheckout.De(EfectoDeTransicion.NoAplica),
        };
    }

    // ------------------------------------------------------------------ tiempo

    /// <summary>
    /// La reserva sigue vigente (CAR-11, AC-CAR-10): antes de su expiración; al vencer, el pago se bloquea y se invita a reiniciar. Un carrito solo digital no tiene reserva que expire y mientras no hay una expiración guardada sigue vigente.
    /// </summary>
    public bool ReservaVigente(DateTimeOffset ahora) => ReservaExpiraEn is { } expira ? ahora < expira : !RequiereReserva;

    /// <summary>
    /// El tiempo que se muestra hacia atrás (CAR-10, CAR-25): con <c>en_pago</c>, hasta la expiración de la reserva; con <c>pago_pendiente</c>, hasta el límite de 30 minutos que fijó Pagos; nunca negativo. En otro estado, nulo.
    /// </summary>
    public TimeSpan? CuentaRegresiva(DateTimeOffset ahora)
    {
        var hasta = Estado switch
        {
            EstadoDelCheckout.EnPago => ReservaExpiraEn,
            EstadoDelCheckout.PagoPendiente => LimitePendienteEn,
            _ => null,
        };

        return hasta is { } h ? (h > ahora ? h - ahora : TimeSpan.Zero) : null;
    }

    // ------------------------------------------------------------------ interior

    private ResultadoDeCheckout VolverAEnEdicion(Carrito carrito, DateTimeOffset ahora, bool porLaPersona)
    {
        Estado = EstadoDelCheckout.EnEdicion;
        LimitePendienteEn = null;
        PagoPendienteDesde = null;
        var retiradas = carrito.AplicarRetirosPendientes(ahora);
        Cambiar(porLaPersona, ahora);
        return ResultadoDeCheckout.Aplicada(retiradas);
    }

    private ResultadoDeCheckout Terminar(EstadoDelCheckout destino, Carrito carrito, DateTimeOffset ahora, bool porLaPersona, bool desdeReservando = false)
    {
        ExigirCarritoPropio(carrito);
        var admitido = Estado is EstadoDelCheckout.EnEdicion or EstadoDelCheckout.EnPago or EstadoDelCheckout.PagoPendiente || (desdeReservando && Estado == EstadoDelCheckout.Reservando);
        if (!admitido || (porLaPersona && Estado == EstadoDelCheckout.PagoPendiente))
        {
            return ResultadoDeCheckout.Rechazar(RechazoPorEstado() ?? RechazoDeCheckout.EstadoNoPermite);
        }

        Estado = destino;
        QuitarContacto();
        LimitePendienteEn = null;
        PagoPendienteDesde = null;
        ReservaALiberarDesde = PedidoId is not null && RequiereReserva ? ahora : ReservaALiberarDesde;
        var retiradas = carrito.AplicarRetirosPendientes(ahora);
        Cambiar(porLaPersona, ahora);
        return ResultadoDeCheckout.Aplicada(retiradas);
    }

    /// <summary>
    /// Un aviso de Pagos de otro <c>pedidoId</c> (por ejemplo el de un pedido ya descartado, que llega tarde) no debe tocar este checkout: si es el último descartado es un éxito idempotente y si no, no aplica. Es nulo cuando el aviso es de este pedido.
    /// </summary>
    private ResultadoDeCheckout? DeOtroPedido(Guid pedidoId) =>
        PedidoId is { } actual && actual == pedidoId ? null : PedidoDescartado == pedidoId ? ResultadoDeCheckout.SinCambios() : ResultadoDeCheckout.De(EfectoDeTransicion.NoAplica);

    /// <summary>Defensa en profundidad: el carrito que se pasa debe ser el de este checkout y de su cuenta (el alcance real lo comprueba la Aplicación); si no, se lanzaría sobre un carrito ajeno.</summary>
    private void ExigirCarritoPropio(Carrito carrito)
    {
        ArgumentNullException.ThrowIfNull(carrito);
        if (carrito.Id != CarritoId || carrito.Propietario.CuentaId != CuentaId)
        {
            throw new ArgumentException("El carrito no es el de este checkout.", nameof(carrito));
        }
    }

    private RechazoDeCheckout? RechazoPorEstado() => Estado switch
    {
        EstadoDelCheckout.PagoPendiente => RechazoDeCheckout.PagoPendiente,
        EstadoDelCheckout.Reservando => RechazoDeCheckout.EnCurso,
        EstadoDelCheckout.Pagado or EstadoDelCheckout.Cancelado or EstadoDelCheckout.Reiniciado => RechazoDeCheckout.YaTerminado,
        _ => null,
    };

    /// <summary>Una operación que cambia lo que se va a pagar solo vale en <c>en_edicion</c>; en <c>en_pago</c> antes hay que reabrir la intención y editar (<see cref="Editar"/>).</summary>
    private RechazoDeCheckout? RechazoPorNoEstarEnEdicion() => Estado switch
    {
        EstadoDelCheckout.EnEdicion => null,
        EstadoDelCheckout.EnPago => RechazoDeCheckout.EstadoNoPermite,
        _ => RechazoPorEstado(),
    };

    private ResultadoDeCheckout? RechazarEntrega()
    {
        if (RechazoPorNoEstarEnEdicion() is { } rechazo)
        {
            return ResultadoDeCheckout.Rechazar(rechazo);
        }

        return EsSoloDigital ? ResultadoDeCheckout.Rechazar(RechazoDeCheckout.EsSoloDigital) : null;
    }

    private void AvanzarADatos()
    {
        if (_pasoDeEdicion < PasoDelCheckout.DatosYAceptaciones)
        {
            _pasoDeEdicion = PasoDelCheckout.DatosYAceptaciones;
        }
    }

    /// <summary>
    /// Quita el contacto y anula la huella de instantánea junto con él (T06): la huella se calculó con el teléfono y no debe sobrevivirle. Es lo que hace toda salida a un estado terminal, el cambio a recojo o a solo digital y
    /// <see cref="EliminarContacto"/>.
    /// </summary>
    private void QuitarContacto()
    {
        if (Contacto is null)
        {
            return;
        }

        Contacto = null;
        HuellaDeInstantanea = null;
    }

    private void LimpiarEntrega()
    {
        CodigoDeSucursalDeRecojo = null;
        RegionDeEntrega = null;
        QuitarContacto();
    }

    private void Cambiar(bool porLaPersona, DateTimeOffset? ahora = null)
    {
        Version++;
        if (porLaPersona)
        {
            ModificadoPorLaPersonaEn = ahora ?? ModificadoPorLaPersonaEn;
        }
    }

    // ------------------------------------------------------------------ texto canónico de las huellas

    /// <summary>La huella de reserva cubre lineas impresas, cantidades y entrega: nunca el contacto, los precios ni las digitales (que no se reservan).</summary>
    private string CanonicoDeReserva()
    {
        var texto = new StringBuilder("reserva-v1");
        Campo(texto, TipoDeEntrega?.ToString());
        Campo(texto, CodigoDeSucursalDeRecojo);
        Campo(texto, RegionDeEntrega);
        foreach (var l in _lineas.Where(l => !l.EsDigital).OrderBy(l => l.EdicionId))
        {
            Campo(texto, l.LineaId.ToString("N"));
            Campo(texto, l.EdicionId.ToString("N"));
            Campo(texto, l.Cantidad.Valor.ToString(CultureInfo.InvariantCulture));
        }

        return texto.ToString();
    }

    /// <summary>La huella de instantánea cubre todo lo congelado: entrega, contacto, envío, total y cada línea con lo informado. Lleva datos personales: solo es la entrada de un cálculo y no se guarda.</summary>
    private string CanonicoDeInstantanea(decimal envio, decimal total)
    {
        var texto = new StringBuilder("instantanea-v1");
        Campo(texto, TipoDeEntrega?.ToString());
        Campo(texto, CodigoDeSucursalDeRecojo);
        Campo(texto, RegionDeEntrega);
        Campo(texto, Contacto?.Nombre.Valor);
        Campo(texto, Contacto?.Telefono.Valor);
        Campo(texto, Contacto?.Direccion.Texto);
        foreach (var requerida in AceptacionesDelCheckout.Requeridas(_lineas.Exists(l => l.EsDigital)))
        {
            // Solo la versión de cada texto: la fecha de aceptación conserva la primera y no entra, para que un doble clic no cambie la huella.
            Campo(texto, Aceptaciones.Aceptadas.FirstOrDefault(a => a.Texto == requerida)?.Version);
        }

        Campo(texto, envio.ToString("0.00", CultureInfo.InvariantCulture));
        Campo(texto, total.ToString("0.00", CultureInfo.InvariantCulture));
        foreach (var l in _lineas.OrderBy(l => l.EdicionId))
        {
            Campo(texto, l.LineaId.ToString("N"));
            Campo(texto, l.EdicionId.ToString("N"));
            Campo(texto, l.EsDigital ? "digital" : "impreso");
            Campo(texto, l.Titulo);
            Campo(texto, l.Isbn);
            Campo(texto, l.Cantidad.Valor.ToString(CultureInfo.InvariantCulture));
            Campo(texto, l.PrecioUnitario.ToString("0.00", CultureInfo.InvariantCulture));
            Campo(texto, l.CodigoDeFuenteInformado);
            Campo(texto, l.RequiereTrasladoInformado?.ToString());
            Campo(texto, l.PlazoInformadoDias?.ToString(CultureInfo.InvariantCulture));
        }

        return texto.ToString();
    }

    /// <summary>Cada campo con su largo delante (o <c>~</c> si es nulo): ningún texto, por más separadores que lleve, se confunde con el campo vecino.</summary>
    private static void Campo(StringBuilder texto, string? valor) =>
        texto.Append('|').Append(valor is null ? "~" : string.Create(CultureInfo.InvariantCulture, $"{valor.Length}:{valor}"));
}
