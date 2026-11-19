using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;
using DomMedioDePago = BabelHub.Ecommerce.Domain.Pagos.MedioDePago;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>El repositorio de pagos en memoria: guarda copias (como una base), respeta el índice único del intento activo y se restaura al revertir la unidad de trabajo.</summary>
internal sealed class RepositorioDePagosEnMemoria : IRepositorioDePagos, IInstantanea
{
    private Dictionary<Guid, IntencionDePago> _intenciones = [];
    private Dictionary<Guid, IntentoDeCobro> _intentos = [];
    private Dictionary<Guid, Compensacion> _compensaciones = [];
    private List<AvisoACarrito> _avisosACarrito = [];
    private List<AvisoDePago> _avisosDePago = [];

    public IReadOnlyCollection<IntencionDePago> Intenciones => _intenciones.Values;

    public IReadOnlyCollection<IntentoDeCobro> Intentos => _intentos.Values;

    /// <summary>Cuántas intenciones se leyeron con <c>FOR UPDATE</c>.</summary>
    public int LecturasConBloqueo { get; private set; }

    public int Operaciones { get; private set; }

    /// <summary>El orden de los bloqueos, compartido con la cuenta del pagador (marca de cancelación y luego intención).</summary>
    public List<string>? Orden { get; set; }

    public bool BloqueoNoDisponible { get; set; }

    public void Sembrar(IntencionDePago intencion) => _intenciones[intencion.Id] = intencion.Copiar();

    public Task<IntencionDePago?> ObtenerIntencion(Guid pedidoId, bool paraActualizar, CancellationToken cancelacion = default)
    {
        Operaciones++;
        if (paraActualizar)
        {
            if (BloqueoNoDisponible)
            {
                throw new TimeoutException("lock_timeout");
            }

            LecturasConBloqueo++;
            Orden?.Add("intencion-con-bloqueo");
        }

        return Task.FromResult(_intenciones.TryGetValue(pedidoId, out var i) ? i.Copiar() : null);
    }

    public Task AgregarIntencion(IntencionDePago intencion, CancellationToken cancelacion = default)
    {
        Operaciones++;
        _intenciones.Add(intencion.Id, intencion.Copiar());
        return Task.CompletedTask;
    }

    public Task GuardarIntencion(IntencionDePago intencion, CancellationToken cancelacion = default)
    {
        Operaciones++;
        _intenciones[intencion.Id] = intencion.Copiar();
        return Task.CompletedTask;
    }

    public Task<IntentoDeCobro?> ObtenerIntento(Guid intentoId, CancellationToken cancelacion = default)
    {
        Operaciones++;
        return Task.FromResult(_intentos.TryGetValue(intentoId, out var i) ? i.Copiar() : null);
    }

    public Task<IntentoDeCobro?> ObtenerIntentoActivo(Guid intencionId, CancellationToken cancelacion = default)
    {
        Operaciones++;
        return Task.FromResult(_intentos.Values.FirstOrDefault(i => i.IntencionId == intencionId && i.EsActivo) is { } a ? a.Copiar() : null);
    }

    public Task<ResultadoDeAgregarIntento> AgregarIntento(IntentoDeCobro intento, CancellationToken cancelacion = default)
    {
        Operaciones++;
        if (_intentos.ContainsKey(intento.Id))
        {
            return Task.FromResult(ResultadoDeAgregarIntento.ClaveRepetida);
        }

        if (_intentos.Values.Any(i => i.IntencionId == intento.IntencionId && i.EsActivo))
        {
            return Task.FromResult(ResultadoDeAgregarIntento.IntentoActivoExistente);
        }

        _intentos[intento.Id] = intento.Copiar();
        return Task.FromResult(ResultadoDeAgregarIntento.Agregado);
    }

    public Task GuardarIntento(IntentoDeCobro intento, CancellationToken cancelacion = default)
    {
        Operaciones++;
        _intentos[intento.Id] = intento.Copiar();
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<Compensacion> Compensaciones => _compensaciones.Values;

    public Task<Compensacion?> ObtenerCompensacion(Guid intentoId, CancellationToken cancelacion = default)
    {
        Operaciones++;
        return Task.FromResult(_compensaciones.TryGetValue(intentoId, out var c) ? c.Copiar() : null);
    }

    public Task<bool> AgregarCompensacion(Compensacion compensacion, CancellationToken cancelacion = default)
    {
        Operaciones++;
        return Task.FromResult(_compensaciones.TryAdd(compensacion.IntentoId, compensacion.Copiar()));
    }

    public Task GuardarCompensacion(Compensacion compensacion, CancellationToken cancelacion = default)
    {
        Operaciones++;
        _compensaciones[compensacion.IntentoId] = compensacion.Copiar();
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<AvisoACarrito> AvisosACarrito => _avisosACarrito;

    public IReadOnlyCollection<AvisoDePago> AvisosDePago => _avisosDePago;

    /// <summary>Hace fallar la inserción de los avisos (para probar que la transición se revierte entera).</summary>
    public bool FallaAlAgregarAvisos { get; set; }

    public Task<bool> AgregarAvisoACarrito(AvisoACarrito aviso, CancellationToken cancelacion = default)
    {
        Operaciones++;
        if (FallaAlAgregarAvisos)
        {
            throw new InvalidOperationException("La base no acepta el aviso.");
        }

        if (_avisosACarrito.Any(a => a.Clave == aviso.Clave))
        {
            return Task.FromResult(false);
        }

        _avisosACarrito.Add(aviso.Copiar());
        return Task.FromResult(true);
    }

    public Task<bool> AgregarAvisoDePago(AvisoDePago aviso, CancellationToken cancelacion = default)
    {
        Operaciones++;
        if (FallaAlAgregarAvisos)
        {
            throw new InvalidOperationException("La base no acepta el aviso.");
        }

        if (_avisosDePago.Any(a => a.Clave == aviso.Clave))
        {
            return Task.FromResult(false);
        }

        _avisosDePago.Add(aviso.Copiar());
        return Task.FromResult(true);
    }

    public object Capturar() => (
        _intenciones.ToDictionary(p => p.Key, p => p.Value.Copiar()),
        _intentos.ToDictionary(p => p.Key, p => p.Value.Copiar()),
        _compensaciones.ToDictionary(p => p.Key, p => p.Value.Copiar()),
        _avisosACarrito.Select(a => a.Copiar()).ToList(),
        _avisosDePago.Select(a => a.Copiar()).ToList());

    public void Restaurar(object instantanea)
    {
        var (intenciones, intentos, compensaciones, avisosACarrito, avisosDePago) = ((Dictionary<Guid, IntencionDePago>, Dictionary<Guid, IntentoDeCobro>, Dictionary<Guid, Compensacion>, List<AvisoACarrito>, List<AvisoDePago>))instantanea;
        _intenciones = intenciones;
        _intentos = intentos;
        _compensaciones = compensaciones;
        _avisosACarrito = avisosACarrito;
        _avisosDePago = avisosDePago;
    }
}

/// <summary>Constructores de lo que Carrito entrega a Pagos para las pruebas del módulo.</summary>
internal static class InstantaneasDeCompraDePrueba
{
    public static readonly Guid Cuenta = IntencionesDePrueba.Cuenta;

    public static readonly Guid Pedido = IntencionesDePrueba.PedidoId;

    /// <summary>Una compra mixta a domicilio: 2 impresos de S/ 50, 1 digital de S/ 25 y envío S/ 15 = S/ 140.</summary>
    public static InstantaneaDeCompra Mixta(string huella = "huella-a", decimal envio = 15m, Guid? cuenta = null, Guid? pedido = null)
    {
        var lineas = new[]
        {
            new LineaDeInstantanea(IntencionesDePrueba.LineaImpresa, Guid.Parse("44444444-4444-4444-4444-444444444441"), false, "La ciudad y los perros", "9780306406157", 2, 50m, 100m, "AYA-01", false, null),
            new LineaDeInstantanea(IntencionesDePrueba.LineaDigital, Guid.Parse("44444444-4444-4444-4444-444444444442"), true, "Rayuela", "9780306406164", 1, 25m, 25m, null, null, null),
        };
        return new InstantaneaDeCompra(
            pedido ?? Pedido,
            cuenta ?? Cuenta,
            TipoDeEntregaDeCompra.Domicilio,
            null,
            "AYA",
            125m,
            envio,
            125m + envio,
            new ContactoDeCompra("Ana Pérez Quispe", "987654321", "Av. Los Olivos 123, Ayacucho", "AYA"),
            Aceptaciones(),
            lineas,
            huella);
    }

    public static InstantaneaDeCompra SoloDigital(string huella = "huella-d", Guid? cuenta = null, Guid? pedido = null) =>
        new(
            pedido ?? Pedido,
            cuenta ?? Cuenta,
            TipoDeEntregaDeCompra.Digital,
            null,
            null,
            25m,
            0m,
            25m,
            null,
            Aceptaciones(),
            [new LineaDeInstantanea(IntencionesDePrueba.LineaDigital, Guid.Parse("44444444-4444-4444-4444-444444444442"), true, "Rayuela", "9780306406164", 1, 25m, 25m, null, null, null)],
            huella);

    private static IReadOnlyList<AceptacionDeCompra> Aceptaciones() =>
    [
        new(TextoLegalDeCompra.Terminos, "2026-11", IntencionesDePrueba.Inicio.AddMinutes(-5)),
        new(TextoLegalDeCompra.Privacidad, "2026-11", IntencionesDePrueba.Inicio.AddMinutes(-5)),
        new(TextoLegalDeCompra.ReembolsoDigital, "2026-11", IntencionesDePrueba.Inicio.AddMinutes(-5)),
    ];
}

/// <summary>Todo lo que necesitan los casos de uso de Pagos, ya cableado.</summary>
internal sealed class EntornoDePagos
{
    public EntornoDePagos(DateTimeOffset? ahora = null)
    {
        Reloj = new RelojFijo(ahora ?? IntencionesDePrueba.Inicio);
        Repositorio = new RepositorioDePagosEnMemoria();
        Unidad = new UnidadDeTrabajoReversible(Repositorio);
        Denegaciones = new DenegacionesDelCatalogoEspia();
        Creador = new IntencionesDePagoDeLaAplicacion(Repositorio, Unidad, Reloj, Denegaciones);
        Actor = ActorDePrueba.DeCliente(InstantaneasDeCompraDePrueba.Cuenta);
        Checkout = new CheckoutDeCarritoDoble();
        Firmador = new FirmadorDeClavesDeIntento(new OpcionesDeClavesDeIntento(System.Text.Encoding.UTF8.GetBytes("clave-de-intentos-de-prueba-de-32-bytes-o-mas")));
        Autorizador = new AutorizadorDePagos(Actor, Denegaciones);
        EmitirClave = new EmitirClaveDeIntento(Autorizador, Repositorio, Unidad, Checkout, Firmador, Reloj);
        Orden = [];
        Repositorio.Orden = Orden;
        Cuenta = new CuentaDelPagadorDoble(Unidad, Orden);
        Posesion = new PosesionDeEdicionesDoble();
        Reservas = new ReservasDelPagoDoble(Unidad, Reloj);
        LimitesDeCobro = new LimitesDeCobroDoble();
        Auditoria = new AuditoriaDelCatalogoEspia();
        RegistrarIntento = new RegistrarIntento(Autorizador, Repositorio, Unidad, Cuenta, Posesion, Reservas, Checkout, new LimitadorDeCobros(LimitesDeCobro), Auditoria, Reloj);
        Pasarela = new PasarelaDePagoDoble(Unidad);
        Confirmador = new ConfirmadorDeCompraDoble(Unidad);
        Alertas = new AlertasTecnicasEspia();
        Cobrar = CrearIniciarCobro(new OpcionesDelCobro(TimeSpan.FromMilliseconds(300)));
    }

    public List<string> Orden { get; }

    public CuentaDelPagadorDoble Cuenta { get; }

    public PosesionDeEdicionesDoble Posesion { get; }

    public ReservasDelPagoDoble Reservas { get; }

    public LimitesDeCobroDoble LimitesDeCobro { get; }

    public AuditoriaDelCatalogoEspia Auditoria { get; }

    public RegistrarIntento RegistrarIntento { get; }

    public PasarelaDePagoDoble Pasarela { get; }

    public ConfirmadorDeCompraDoble Confirmador { get; }

    public AlertasTecnicasEspia Alertas { get; }

    public ICasoDeUso<CobrarEntrada, ResultadoDelCobroDelCliente> Cobrar { get; private set; }

    /// <summary>El orquestador con otras opciones (por ejemplo, la pasarela que exige el correo del titular).</summary>
    public IniciarCobro CrearIniciarCobro(OpcionesDelCobro opciones) =>
        new(Autorizador, Firmador, Repositorio, Unidad, RegistrarIntento, Pasarela, Cuenta, Reservas, Confirmador, Auditoria, Alertas, Reloj, opciones);

    public ActorDePrueba Actor { get; }

    public CheckoutDeCarritoDoble Checkout { get; }

    public FirmadorDeClavesDeIntento Firmador { get; }

    public AutorizadorDePagos Autorizador { get; }

    public ICasoDeUso<EmitirClaveDeIntentoEntrada, ClaveDeIntentoEmitida> EmitirClave { get; }

    public RelojFijo Reloj { get; }

    public RepositorioDePagosEnMemoria Repositorio { get; }

    public UnidadDeTrabajoReversible Unidad { get; }

    public DenegacionesDelCatalogoEspia Denegaciones { get; }

    public ICreadorDeIntencionesDePago Creador { get; }

    /// <summary>La reserva de la compra con impresos: 15 minutos desde el inicio.</summary>
    public DateTimeOffset ReservaExpiraEn => Reloj.Ahora.AddMinutes(15);

    /// <summary>Crea la intención mixta y devuelve el resultado.</summary>
    public async Task<ResultadoDeCrearIntencion> CrearMixta(string huella = "huella-a") =>
        await Creador.Crear(InstantaneasDeCompraDePrueba.Cuenta, InstantaneasDeCompraDePrueba.Mixta(huella), huella, ReservaExpiraEn);
}

internal static class EntornoDePagosExtensiones
{
    /// <summary>Una entrada de registro con una clave nueva (o la dada), para el medio elegido.</summary>
    public static EntradaDeRegistroDeIntento Entrada(this EntornoDePagos e, Guid? intentoId = null, DomMedioDePago medio = DomMedioDePago.Yape, string huella = "huella-a", long total = 14_000) =>
        new(InstantaneasDeCompraDePrueba.Cuenta, InstantaneasDeCompraDePrueba.Pedido, new DatosDeLaClaveDeIntento(intentoId ?? Guid.NewGuid(), InstantaneasDeCompraDePrueba.Pedido, InstantaneasDeCompraDePrueba.Cuenta, huella, total, e.Reloj.Ahora.AddMinutes(30)), medio);

    /// <summary>La intención mixta creada, con el reloj a los 30 segundos del inicio (como cuando el cliente aprieta pagar).</summary>
    public static async Task<EntornoDePagos> ConIntencionLista()
    {
        var e = new EntornoDePagos();
        await e.CrearMixta();
        e.Reloj.Ahora = IntencionesDePrueba.Inicio.AddSeconds(30);
        return e;
    }
}

internal static class EmisionDeClavesDePrueba
{
    /// <summary>Una clave firmada con el total y la huella que el cliente vio antes de que el carrito cambiara en otra pestaña.</summary>
    public static Task<string> EmitirClaveConTotalViejo(this EntornoDePagos e) =>
        Task.FromResult(e.Firmador.Emitir(new DatosDeLaClaveDeIntento(Guid.NewGuid(), InstantaneasDeCompraDePrueba.Pedido, InstantaneasDeCompraDePrueba.Cuenta, "huella-a", 14_000, e.Reloj.Ahora.AddMinutes(30))));
}
