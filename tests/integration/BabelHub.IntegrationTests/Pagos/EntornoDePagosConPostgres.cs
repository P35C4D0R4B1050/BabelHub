using System.Reflection;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using BabelHub.IntegrationTests.Reclamaciones;
using Microsoft.Extensions.DependencyInjection;
using RolesDeUsuario = BabelHub.Shared.Contratos.RolesDeUsuario;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>Un doble por método: cada operación de la interfaz responde lo que la prueba le pone (y falla si no se la espera). Lo usan los contratos públicos de Carrito, que son anchos y de los que Pagos usa una operación.</summary>
public class DobleDeMetodos : DispatchProxy
{
    private readonly Dictionary<string, Func<object?[], object?>> _metodos = [];

    public List<string> Llamadas { get; } = [];

    public static T Crear<T>(out DobleDeMetodos doble)
        where T : class
    {
        var proxy = Create<T, DobleDeMetodos>();
        doble = (DobleDeMetodos)(object)proxy;
        return proxy;
    }

    public void Con(string metodo, Func<object?[], object?> respuesta) => _metodos[metodo] = respuesta;

    protected override object? Invoke(MethodInfo? metodo, object?[]? argumentos)
    {
        ArgumentNullException.ThrowIfNull(metodo);
        lock (Llamadas)
        {
            Llamadas.Add(metodo.Name);
        }

        return _metodos.TryGetValue(metodo.Name, out var respuesta) ? respuesta(argumentos ?? []) : throw new NotSupportedException($"El doble no espera {metodo.Name}.");
    }
}

/// <summary>El reloj de la prueba: avanza solo cuando la prueba lo manda.</summary>
internal sealed class RelojManualDePagos : IReloj
{
    private readonly Lock _candado = new();
    private DateTimeOffset _ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    public DateTimeOffset Ahora
    {
        get
        {
            lock (_candado)
            {
                return _ahora;
            }
        }
    }

    public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.UtcDateTime.AddHours(-5));

    public void Avanzar(TimeSpan tiempo)
    {
        lock (_candado)
        {
            _ahora += tiempo;
        }
    }
}

/// <summary>El actor de la solicitud: un cliente o el sistema, cambiable a voluntad.</summary>
internal sealed class ActorDePagos : IActorActual
{
    public static readonly Guid CuentaPorOmision = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public ActorActual? Actual { get; set; } = ActorActual.DeUsuario(CuentaPorOmision, RolesDeUsuario.Cliente, null);

    public void ComoCliente(Guid cuenta) => Actual = ActorActual.DeUsuario(cuenta, RolesDeUsuario.Cliente, null);

    public void ComoSistema() => Actual = ActorActual.DelSistema();
}

/// <summary>El motor de límites en memoria, seguro entre hilos: 5 por intención, 10 por cuenta y 20 por origen y hora, idempotente por la clave del llamador (el intento), y que puede caerse.</summary>
internal sealed class LimitesDeCobrosEnMemoria : IControlDeLimites
{
    private readonly Lock _candado = new();
    private readonly Dictionary<string, HashSet<string>> _usados = [];

    public bool Caido { get; set; }

    public int Reservas { get; private set; }

    public Task<ResultadoDeLimite> Reservar(ClaveDePolitica politica, string? sujeto = null, string? claveDelLlamador = null, HuellaDeSujeto? huellaDelLlamador = null, ClaveDeIdempotencia? idempotencia = null, CancellationToken cancelacion = default)
    {
        if (Caido)
        {
            throw new InvalidOperationException("El almacén de contadores no responde.");
        }

        var maximo = politica == PoliticasDeLimite.CobrosPorIntencion ? 5 : politica == PoliticasDeLimite.CobrosPorCuenta ? 10 : politica == PoliticasDeLimite.CobrosPorOrigen ? 20 : int.MaxValue;
        var contador = $"{politica.Valor}|{sujeto}";
        lock (_candado)
        {
            Reservas++;
            if (!_usados.TryGetValue(contador, out var claves))
            {
                claves = [];
                _usados[contador] = claves;
            }

            var clave = claveDelLlamador ?? Guid.NewGuid().ToString("N");
            if (!claves.Contains(clave) && claves.Count >= maximo)
            {
                return Task.FromResult(ResultadoDeLimite.Excedido("limite-excedido", 3600, DateTimeOffset.UtcNow.AddHours(1)));
            }

            claves.Add(clave);
            return Task.FromResult(ResultadoDeLimite.Permitido(new ReservaDeLimite(politica.Valor, contador, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, claveDelLlamador)));
        }
    }

    public Task Liberar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;
}

/// <summary>Identidad vista por Pagos: una cuenta que puede pagar salvo que la prueba diga otra cosa, con la marca de cancelación y el correo del titular.</summary>
internal sealed class CuentaDelPagadorDePrueba : ICuentaDelPagador
{
    public EstadoDeLaCuentaParaPagar? Estado { get; set; } = new(true, null);

    public bool CancelacionEnEjecucion { get; set; }

    public string? Correo { get; set; } = "ana.perez@correo.example";

    public Task<EstadoDeLaCuentaParaPagar?> Consultar(Guid cuentaId, CancellationToken cancelacion = default) => Task.FromResult(Estado);

    Task<bool> ICuentaDelPagador.CancelacionEnEjecucion(Guid cuentaId, bool bloquear, CancellationToken cancelacion) => Task.FromResult(CancelacionEnEjecucion);

    public Task<string?> CorreoDelTitular(Guid cuentaId, CancellationToken cancelacion = default) => Task.FromResult(Correo);
}

/// <summary>Pedidos visto por Pagos: crea el pedido (y lo cuenta) salvo que la prueba lo haga fallar; repetir con el mismo pedido devuelve el mismo resultado sin crear otro.</summary>
internal sealed class ConfirmacionDeCompraDePrueba : IConfirmacionDeCompra
{
    private readonly Lock _candado = new();
    private readonly HashSet<Guid> _creados = [];

    public ResultadoDeConfirmarCompra Resultado { get; set; } = ResultadoDeConfirmarCompra.Creado;

    public Exception? Falla { get; set; }

    public IReadOnlyCollection<Guid> PedidosCreados
    {
        get
        {
            lock (_candado)
            {
                return [.. _creados];
            }
        }
    }

    public int Llamadas { get; private set; }

    public Task<ResultadoDeConfirmarCompra> Confirmar(CompraAprobada compra, CancellationToken cancelacion = default)
    {
        lock (_candado)
        {
            Llamadas++;
            if (Falla is not null)
            {
                return Task.FromException<ResultadoDeConfirmarCompra>(Falla);
            }

            if (Resultado == ResultadoDeConfirmarCompra.Creado)
            {
                _creados.Add(compra.PedidoId);
            }

            return Task.FromResult(Resultado);
        }
    }
}

/// <summary>
/// Pagos completo sobre PostgreSQL real (T30 a T32): los casos de uso, los repositorios y el simulador compuestos por <see cref="ComposicionDePagos"/>, con el reloj en manos de la prueba y dobles para lo que no es de Pagos (el actor, los límites, la auditoría, el correo, Carrito, Identidad,
/// Pedidos y el Inventario simulado). Cada <see cref="Crear"/> arma un entorno nuevo sobre las mismas tablas; la prueba las vacía antes.
/// </summary>
internal sealed class EntornoDePagosConPostgres : IAsyncDisposable
{
    private const string ClaveDeIntentos = "clave-de-intentos-de-prueba-0123456789-abcdef";

    private static readonly List<string> _alertas = [];

    private EntornoDePagosConPostgres(ServiceProvider proveedor, PostgresDePagosFixture bd)
    {
        Proveedor = proveedor;
        Bd = bd;
    }

    public ServiceProvider Proveedor { get; }

    public PostgresDePagosFixture Bd { get; }

    public RelojManualDePagos Reloj => (RelojManualDePagos)Proveedor.GetRequiredService<IReloj>();

    public ActorDePagos Actor => (ActorDePagos)Proveedor.GetRequiredService<IActorActual>();

    public LimitesDeCobrosEnMemoria Limites => (LimitesDeCobrosEnMemoria)Proveedor.GetRequiredService<IControlDeLimites>();

    public AuditoriaDeHojasEspia Auditoria => (AuditoriaDeHojasEspia)Proveedor.GetRequiredService<IAuditoria>();

    public CorreoDeHojasEspia Correo => (CorreoDeHojasEspia)Proveedor.GetRequiredService<IEnviadorDeCorreo>();

    public SimuladorPasarelaDePago Pasarela => (SimuladorPasarelaDePago)Proveedor.GetRequiredService<IPasarelaDePago>();

    public CuentaDelPagadorDePrueba Cuenta => Proveedor.GetRequiredService<CuentaDelPagadorDePrueba>();

    public ConfirmacionDeCompraDePrueba Pedidos => Proveedor.GetRequiredService<ConfirmacionDeCompraDePrueba>();

    /// <summary>Los motivos de las alertas técnicas que Pagos publicó.</summary>
    public IReadOnlyList<string> Alertas { get { lock (_alertas) { return [.. _alertas]; } } }

    public DobleDeMetodos Checkout { get; private set; } = null!;

    public DobleDeMetodos Progreso { get; private set; } = null!;

    public static EntornoDePagosConPostgres Crear(PostgresDePagosFixture bd, Action<IServiceCollection>? ajustes = null)
    {
        _alertas.Clear();
        var servicios = new ServiceCollection();
        var reloj = new RelojManualDePagos();
        servicios.AddSingleton<IReloj>(reloj);
        servicios.AddSingleton<IActorActual>(new ActorDePagos());
        servicios.AddSingleton<IUnidadDeTrabajo>(bd.Unidad);
        servicios.AddSingleton<IConexionDeLaUnidadDeTrabajo>(bd.Unidad);
        servicios.AddSingleton<IDenegacionDeAcceso>(new DenegacionesDeHojasEspia());
        servicios.AddSingleton<IControlDeLimites>(new LimitesDeCobrosEnMemoria());
        servicios.AddSingleton<IAuditoria>(new AuditoriaDeHojasEspia());
        servicios.AddSingleton<IEnviadorDeCorreo>(new CorreoDeHojasEspia());
        servicios.AddSingleton(Falso<IProcesadorDeEntradasDeIntegracion>(out var entradas));
        servicios.AddSingleton(Falso<IPublicadorDeAlertasTecnicas>(out var alertas));
        servicios.AddSingleton(Falso<IRegistroDeTrabajosDeFondo>(out var trabajos));
        servicios.AddSingleton(Falso<IClienteDelInventario>(out _));
        servicios.AddSingleton(Falso<IPosesionDeEdiciones>(out var posesion));
        var checkout = Falso<ICheckoutDelCliente>(out var dobleCheckout);
        var progreso = Falso<IProgresoDelCheckout>(out var dobleProgreso);
        servicios.AddSingleton(checkout);
        servicios.AddSingleton(progreso);

        // Lo que aún no es de Pagos y se escribe a la medida: la cuenta, el pedido y las reservas del Inventario simulado.
        servicios.AddScoped<ICuentaDelPagador>(sp => sp.GetRequiredService<CuentaDelPagadorDePrueba>());
        servicios.AddSingleton<CuentaDelPagadorDePrueba>();
        servicios.AddScoped<IConfirmacionDeCompra>(sp => sp.GetRequiredService<ConfirmacionDeCompraDePrueba>());
        servicios.AddSingleton<ConfirmacionDeCompraDePrueba>();
        servicios.AddSingleton<ReservasSimuladasDelPago>();
        servicios.AddScoped<IReservasDelPago>(sp => sp.GetRequiredService<ReservasSimuladasDelPago>());

        var vistos = new HashSet<string>();
        entradas.Con(
            nameof(IProcesadorDeEntradasDeIntegracion.Recibir),
            argumentos =>
            {
                // Como el procesador real: una entrada sin identificador o con la firma forjada se rechaza; la misma entrada dos veces es repetida.
                var entrada = (EntradaDeIntegracion)argumentos[0]!;
                lock (vistos)
                {
                    var estado = entrada.IdDeEntrada.Length == 0 || entrada.Firma == "forjada" ? EstadoDeEntradaDeIntegracion.Rechazada : vistos.Add(entrada.IdDeEntrada) ? EstadoDeEntradaDeIntegracion.Aceptada : EstadoDeEntradaDeIntegracion.Repetida;
                    return Task.FromResult(new ResultadoDeEntradaDeIntegracion(estado));
                }
            });
        entradas.Con(nameof(IProcesadorDeEntradasDeIntegracion.Completar), _ => Task.CompletedTask);
        alertas.Con(nameof(IPublicadorDeAlertasTecnicas.Publicar), argumentos => { lock (_alertas) { _alertas.Add(((AlertaTecnica)argumentos[0]!).Motivo); } return Task.CompletedTask; });
        trabajos.Con(nameof(IRegistroDeTrabajosDeFondo.Registrar), _ => Task.CompletedTask);
        posesion.Con(nameof(IPosesionDeEdiciones.Posee), _ => Task.FromResult(ResultadoDePosesion.NoPosee));
        dobleProgreso.Con(nameof(IProgresoDelCheckout.PagoPendiente), _ => Task.FromResult(ResultadoDelAvisoDePago.Aplicado));
        dobleProgreso.Con(nameof(IProgresoDelCheckout.PagoAprobado), _ => Task.FromResult(ResultadoDelAvisoDePago.Aplicado));
        dobleProgreso.Con(nameof(IProgresoDelCheckout.PagoResuelto), _ => Task.FromResult(ResultadoDelAvisoDePago.Aplicado));

        // El plazo de respuesta de la pasarela es corto en las pruebas: el cobro lento (11 s) se corta a los 300 ms y el reloj de la prueba hace el resto.
        servicios.AddSingleton(new OpcionesDelCobro(TimeSpan.FromMilliseconds(300)));
        ajustes?.Invoke(servicios);
        servicios.AgregarPagos(new OpcionesDeArranqueDePagos("simulador", "Test", false, false, PedidosDisponible: true, null, ClaveDeIntentos, ClaveDeIntentosAnterior: null), esProduccion: false);
        var proveedor = servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var entorno = new EntornoDePagosConPostgres(proveedor, bd) { Checkout = dobleCheckout, Progreso = dobleProgreso };
        return entorno;
    }

    /// <summary>Un alcance de solicitud: los casos de uso de Pagos son por alcance.</summary>
    public IServiceScope Alcance() => Proveedor.CreateScope();

    public T Obtener<T>(IServiceScope alcance)
        where T : notnull => alcance.ServiceProvider.GetRequiredService<T>();

    public ReservasSimuladasDelPago Reservas => Proveedor.GetRequiredService<ReservasSimuladasDelPago>();

    public async ValueTask DisposeAsync() => await Proveedor.DisposeAsync();

    private static T Falso<T>(out DobleDeMetodos doble)
        where T : class => DobleDeMetodos.Crear<T>(out doble);
}
