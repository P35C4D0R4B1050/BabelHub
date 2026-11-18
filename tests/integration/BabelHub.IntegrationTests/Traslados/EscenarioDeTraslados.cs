using BabelHub.IntegrationTests.Reservas;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Traslados;

/// <summary>
/// El escenario común de las pruebas de integración de <c>Traslados</c> con PostgreSQL real, el rol real de la aplicación y los módulos reales de <c>Fuentes</c>, <c>Reservas</c>, <c>Ajustes</c> y <c>Traslados</c> (la auditoría, las
/// denegaciones y el reloj son dobles): Lima es la fuente de recepción, y Cusco y Ayacucho las otras sucursales; las llamadas corren en su propio flujo asíncrono con su actor, como solicitudes distintas.
/// </summary>
public sealed class EscenarioDeTraslados(PostgresDeReservasFixture bd) : IDisposable
{
    public static readonly ActorActual Servicio = ActorActual.DeIntegracion("expiracion-de-reservas");

    public PostgresDeReservasFixture Bd => bd;

    public PilaDeReservas Pila { get; private set; } = null!;

    public Guid Lima { get; private set; }

    public Guid Cusco { get; private set; }

    public Guid Ayacucho { get; private set; }

    public Guid Libro { get; private set; }

    public static ActorActual Administracion() => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.AdministradorCentral);

    public static ActorActual Encargado(Guid sucursal) => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, sucursal);

    public static ActorActual Logistica() => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.PersonalDeLogistica);

    /// <summary>Crea las tres fuentes y una edición impresa, con las rutas de Cusco y Ayacucho hacia Lima y de Cusco hacia Ayacucho, y levanta la pila con <c>Traslados</c>.</summary>
    public async Task<EscenarioDeTraslados> Preparar(int stockDeLima = 0, int stockDeCusco = 10, int stockDeAyacucho = 0, Action<IServiceCollection>? pila = null)
    {
        Lima = await bd.AgregarFuente("lima", recepcion: true);
        Cusco = await bd.AgregarFuente("cusco");
        Ayacucho = await bd.AgregarFuente("ayacucho");
        Libro = await bd.AgregarEdicion();
        await bd.PonerStock(Lima, Libro, stockDeLima, 0);
        await bd.PonerStock(Cusco, Libro, stockDeCusco, 0);
        await bd.PonerStock(Ayacucho, Libro, stockDeAyacucho, 0);
        await bd.DefinirRuta(Cusco, Lima, 2);
        await bd.DefinirRuta(Cusco, Ayacucho, 3);
        Pila = new PilaDeReservas(bd, pila, conTraslados: true);
        return this;
    }

    public Task<Resultado<TrasladoRegistrado>> Ejecutar<TEntrada>(ActorActual? actor, TEntrada entrada) => Pila.Ejecutar<TEntrada, TrasladoRegistrado>(actor, entrada);

    public Task<Resultado<TSalida>> Consultar<TEntrada, TSalida>(ActorActual? actor, TEntrada entrada) => Pila.Ejecutar<TEntrada, TSalida>(actor, entrada);

    public async Task<TrasladoRegistrado> SolicitarComoAdministracion(int cantidad = 3, Guid? origen = null, Guid? destino = null, string? motivo = null)
    {
        var codigoOrigen = await bd.Consultar<string>($"SELECT codigo FROM fuentes.fuentes WHERE id = '{origen ?? Cusco}'");
        var codigoDestino = await bd.Consultar<string>($"SELECT codigo FROM fuentes.fuentes WHERE id = '{destino ?? Ayacucho}'");
        var resultado = await Ejecutar(Administracion(), new SolicitarTrasladoEntrada(Libro, cantidad, codigoOrigen!, codigoDestino!, motivo));
        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        return resultado.Valor;
    }

    /// <summary>Una reposición de Cusco a Ayacucho ya aprobada por la administración.</summary>
    public async Task<TrasladoRegistrado> ReposicionAprobada(int cantidad = 3, Guid? origen = null, Guid? destino = null)
    {
        var solicitado = await SolicitarComoAdministracion(cantidad, origen, destino);
        var aprobado = await Ejecutar(Administracion(), new AprobarTrasladoEntrada(solicitado.TrasladoId));
        Assert.True(aprobado.EsExito, aprobado.Error?.Clave);
        return aprobado.Valor;
    }

    /// <summary>Una reposición en tránsito (aprobada y despachada).</summary>
    public async Task<TrasladoRegistrado> ReposicionEnTransito(int cantidad = 3)
    {
        var aprobado = await ReposicionAprobada(cantidad);
        var despachado = await Ejecutar(Administracion(), new DespacharTrasladoEntrada(aprobado.TrasladoId));
        Assert.True(despachado.EsExito, despachado.Error?.Clave);
        return despachado.Valor;
    }

    /// <summary>
    /// Un pedido de recojo en Lima, reservado por un cliente y confirmado, cuya mercadería sale de Cusco: devuelve la reserva confirmada con su traslado vinculado (solicitado). Cusco queda con físico 1 y reservado 1 si el stock
    /// inicial era 1.
    /// </summary>
    public async Task<(Guid PedidoId, ReservaDelPedido Reserva, Guid TrasladoId)> PedidoConTraslado(int cantidad = 1)
    {
        var entrada = ARecojo("lima", [Linea(Libro, cantidad)]);
        var reservada = await Pila.Reservar(Cliente(), entrada);
        Assert.True(reservada.EsExito, reservada.Error?.Clave);
        var confirmada = await Pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(Servicio, new ConfirmarReservasEntrada(entrada.PedidoId, Guid.NewGuid().ToString("N")));
        Assert.True(confirmada.EsExito, confirmada.Error?.Clave);
        return (entrada.PedidoId, confirmada.Valor, confirmada.Valor.Traslados.Single().TrasladoId);
    }

    public async Task<(Guid PedidoId, Guid TrasladoId)> VinculadoAprobado(int cantidad = 1)
    {
        var (pedido, _, traslado) = await PedidoConTraslado(cantidad);
        var aprobado = await Ejecutar(Administracion(), new AprobarTrasladoEntrada(traslado));
        Assert.True(aprobado.EsExito, aprobado.Error?.Clave);
        return (pedido, traslado);
    }

    public Task<Resultado<ReservaDelPedido>> AnularElPedido(Guid pedidoId, MotivoDeLiberacionDeReserva motivo = MotivoDeLiberacionDeReserva.PedidoAnulado) =>
        Pila.Ejecutar<LiberarReservasEntrada, ReservaDelPedido>(Servicio, new LiberarReservasEntrada(pedidoId, motivo, null));

    public Task<string> EstadoDe(Guid trasladoId) => bd.Consultar<string>($"SELECT estado FROM traslados.traslados WHERE id = '{trasladoId}'")!;

    public async Task<bool> PedidoAnuladoDe(Guid trasladoId) => await bd.Consultar<bool>($"SELECT pedido_anulado FROM traslados.traslados WHERE id = '{trasladoId}'");

    public async Task<long> VersionDe(Guid trasladoId) => await bd.Consultar<long>($"SELECT version FROM traslados.traslados WHERE id = '{trasladoId}'");

    public async Task<long> Interbloqueos() => await bd.Consultar<long>("SELECT deadlocks FROM pg_stat_database WHERE datname = current_database()");

    public static readonly TimeSpan Limite = TimeSpan.FromSeconds(120);

    /// <summary>Lanza todas las operaciones a la vez, detrás de una barrera, cada una en su propio flujo y con su actor.</summary>
    public static async Task<T[]> Simultaneas<T>(params Func<Task<T>>[] trabajos)
    {
        var compuerta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tareas = trabajos.Select(t => Task.Run(async () =>
        {
            await compuerta.Task;
            return await t();
        })).ToList();
        compuerta.SetResult();
        return await Task.WhenAll(tareas).WaitAsync(Limite);
    }

    public void Dispose() => Pila?.Dispose();
}
