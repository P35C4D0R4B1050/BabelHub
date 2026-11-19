using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Pedidos;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// Los dobles provisionales de los contratos de Pedidos que consume Envíos (<c>ecommerce-pedidos</c> T07/T08, D01 de <c>ecommerce-envios</c>): <b>fallan cerrado</b>, un registro real anterior se respeta y en producción la guarda
/// de arranque impide publicar con cualquiera de ellos.
/// </summary>
public class DoblesProvisionalesDePedidosTests
{
    [Fact]
    public async Task Los_dobles_fallan_cerrado_y_ninguna_operacion_avanza_sin_la_implementacion_real()
    {
        var pedido = Guid.NewGuid();
        var lineas = new[] { Guid.NewGuid() };

        var crear = await new DespachoDeEnviosProvisional().Crear(new CrearEnvioEntrada(pedido, "LIM", lineas));
        var cancelar = await new DespachoDeEnviosProvisional().Cancelar(pedido);
        var enReparto = await new ProgresoDeEnviosProvisional().EnvioEnReparto(new EnvioEnRepartoEntrada(pedido, Guid.NewGuid(), lineas));
        var entregado = await new ProgresoDeEnviosProvisional().EnvioEntregado(new EnvioEntregadoEntrada(pedido, Guid.NewGuid(), lineas, DateTimeOffset.UnixEpoch));
        var leido = await new PedidosParaEnviosProvisional().Obtener(pedido);
        var contacto = await new ContactoDeEntregaProvisional().Leer(pedido);

        Assert.All(new[] { crear.Error, cancelar.Error, enReparto.Error, entregado.Error, leido.Error }, error =>
        {
            Assert.NotNull(error);
            Assert.Equal((CategoriaDeError.NoDisponible, "servicio-no-disponible"), (error.Categoria, error.Clave));
        });
        Assert.Equal(EstadoDeLecturaDelContacto.NoDisponible, contacto.Estado);
        Assert.Null(contacto.Contacto);

        var acceso = await new AccesosDeBibliotecaProvisional().Conceder(new ConcederAccesoEntrada(Guid.NewGuid(), pedido, Guid.NewGuid()));
        Assert.Equal(CategoriaDeError.NoDisponible, acceso.Error!.Categoria);
        Assert.Equal(ResultadoDeCerrarReserva.Incierto, await new ReservasDelPedidoProvisional().Cerrar(pedido, lineas[0], "cierre-x"));
        Assert.Equal(ResultadoDeLiberarReserva.NoSePudo, await new ReservasDelPedidoProvisional().Liberar(pedido, "pedido-anulado", lineas));
        Assert.Null(await new ResolucionDeSucursalProvisional().IdentificadorDe("LIM"));
    }

    [Fact]
    public void El_contacto_de_entrega_se_redacta_al_imprimirlo_y_al_serializarlo()
    {
        var contacto = new ContactoDeEntrega("Ana Pérez", "987654321", "Jr. Lima 123", "ayacucho");
        var lectura = LecturaDelContactoDeEntrega.Disponible(contacto);

        var texto = System.Text.Json.JsonSerializer.Serialize(new { contacto, lectura });

        Assert.Equal(ContactoDeEntrega.Redactado, contacto.ToString());
        Assert.DoesNotContain("Ana", lectura.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("987654321", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Jr. Lima", texto, StringComparison.Ordinal);
        Assert.Equal(CampoDelContactoDeEntrega.Telefono, LecturaDelContactoDeEntrega.Faltante(CampoDelContactoDeEntrega.Telefono).CampoFaltante);
    }

    [Fact]
    public void En_desarrollo_los_dobles_se_registran_sin_pisar_a_los_reales()
    {
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IProgresoDeEnvios, ProgresoReal>();

        servicios.AgregarDoblesProvisionalesDePedidos(esProduccion: false);
        using var proveedor = servicios.BuildServiceProvider();

        Assert.IsType<ProgresoReal>(proveedor.GetRequiredService<IProgresoDeEnvios>());
        Assert.IsType<DespachoDeEnviosProvisional>(proveedor.GetRequiredService<IDespachoDeEnvios>());
        Assert.IsType<ContactoDeEntregaProvisional>(proveedor.GetRequiredService<IContactoDeEntregaDelPedido>());
        Assert.IsType<PedidosParaEnviosProvisional>(proveedor.GetRequiredService<IPedidosParaEnvios>());
        Assert.IsType<AccesosDeBibliotecaProvisional>(proveedor.GetRequiredService<IAccesosDeBiblioteca>());
        Assert.IsType<ReservasDelPedidoProvisional>(proveedor.GetRequiredService<IReservasDelPedido>());
        Assert.IsType<ResolucionDeSucursalProvisional>(proveedor.GetRequiredService<IResolucionDeSucursal>());
    }

    [Fact]
    public void En_produccion_el_arranque_falla_si_algun_contrato_queda_con_un_doble_provisional()
    {
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IProgresoDeEnvios, ProgresoReal>();

        var error = Assert.Throws<InvalidOperationException>(() => servicios.AgregarDoblesProvisionalesDePedidos(esProduccion: true));

        Assert.Contains(nameof(IDespachoDeEnvios), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IContactoDeEntregaDelPedido), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IPedidosParaEnvios), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IAccesosDeBiblioteca), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IReservasDelPedido), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IResolucionDeSucursal), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(IProgresoDeEnvios), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void En_produccion_el_arranque_pasa_cuando_todos_los_contratos_tienen_su_implementacion_real()
    {
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IDespachoDeEnvios, DespachoReal>();
        servicios.AddSingleton<IProgresoDeEnvios, ProgresoReal>();
        servicios.AddSingleton<IContactoDeEntregaDelPedido, ContactoReal>();
        servicios.AddSingleton<IPedidosParaEnvios, LecturaReal>();
        servicios.AddSingleton<IAccesosDeBiblioteca, AccesosReales>();
        servicios.AddSingleton<IReservasDelPedido, ReservasReales>();
        servicios.AddSingleton<IResolucionDeSucursal, ResolucionReal>();

        var resultado = servicios.AgregarDoblesProvisionalesDePedidos(esProduccion: true);

        Assert.Same(servicios, resultado);
    }

    private sealed class DespachoReal : IDespachoDeEnvios
    {
        public Task<Resultado<ResultadoDeCrearEnvio>> Crear(CrearEnvioEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<IReadOnlyList<ResultadoDeCancelarEnvio>>> Cancelar(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class ProgresoReal : IProgresoDeEnvios
    {
        public Task<Resultado<ProgresoDeEnvioAplicado>> EnvioEnReparto(EnvioEnRepartoEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ProgresoDeEnvioAplicado>> EnvioEntregado(EnvioEntregadoEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class ContactoReal : IContactoDeEntregaDelPedido
    {
        public Task<LecturaDelContactoDeEntrega> Leer(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class AccesosReales : IAccesosDeBiblioteca
    {
        public Task<Resultado<ResultadoDeConcederAcceso>> Conceder(ConcederAccesoEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResultadoDeRevocarAcceso>> Revocar(RevocarAccesoEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class ReservasReales : IReservasDelPedido
    {
        public Task<ResultadoDeCerrarReserva> Cerrar(Guid pedidoId, Guid lineaId, string claveDeCierre, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDeLiberarReserva> Liberar(Guid pedidoId, string motivo, IReadOnlyList<Guid> lineaIds, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class ResolucionReal : IResolucionDeSucursal
    {
        public Task<Guid?> IdentificadorDe(string codigoDeSucursal, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class LecturaReal : IPedidosParaEnvios
    {
        public Task<Resultado<PedidoParaEnvios>> Obtener(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }
}
