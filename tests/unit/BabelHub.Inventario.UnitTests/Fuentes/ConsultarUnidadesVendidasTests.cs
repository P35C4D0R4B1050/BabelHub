using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Fuentes;

/// <summary>T15: las unidades vendidas de los últimos 30 días (FUE-21, FUE-22) a partir de los puertos de ventas y de reingresos por devolución.</summary>
public sealed class ConsultarUnidadesVendidasTests
{
    private sealed class Escenario
    {
        public Escenario(ActorDePrueba actor) => Entorno = new EntornoDeFuentes(actor);

        public EntornoDeFuentes Entorno { get; }

        public OrigenDeUnidadesVendidasDoble Ventas { get; } = new();

        public ReingresosPorDevolucionDoble Reingresos { get; } = new();

        public ConsultarUnidadesVendidas Caso() =>
            new(Entorno.Autorizador, new ValidadorDeConsultarUnidadesVendidas(), Entorno.Fuentes, Ventas, Reingresos, Entorno.Reloj);
    }

    /// <summary>Un entorno cuyo actor es el encargado de la fuente que el propio entorno crea (el actor necesita el identificador de la fuente).</summary>
    private static (EntornoDeFuentes Entorno, OrigenDeUnidadesVendidasDoble Ventas, ReingresosPorDevolucionDoble Reingresos, ConsultarUnidadesVendidas Caso) ComoEncargadoDeAyacucho()
    {
        var baseDeFuentes = new EntornoDeFuentes();
        var actor = ActorDePrueba.DeEncargadoDe(baseDeFuentes.Ayacucho.Id);
        var ventas = new OrigenDeUnidadesVendidasDoble();
        var reingresos = new ReingresosPorDevolucionDoble();
        var caso = new ConsultarUnidadesVendidas(
            new AutorizadorDeFuentes(actor, baseDeFuentes.Denegaciones),
            new ValidadorDeConsultarUnidadesVendidas(),
            baseDeFuentes.Fuentes,
            ventas,
            reingresos,
            baseDeFuentes.Reloj);
        return (baseDeFuentes, ventas, reingresos, caso);
    }

    private static ConsultarUnidadesVendidas Administracion(EntornoDeFuentes e, OrigenDeUnidadesVendidasDoble ventas, ReingresosPorDevolucionDoble reingresos) =>
        new(e.Autorizador, new ValidadorDeConsultarUnidadesVendidas(), e.Fuentes, ventas, reingresos, e.Reloj);

    [Fact]
    [Trait("Criterio", "AC-FUE-17")]
    public async Task ElEncargadoVeSoloLasSuyas_YLaAdministracionLaRedCompleta()
    {
        var (entorno, ventas, reingresos, comoEncargado) = ComoEncargadoDeAyacucho();
        var edicion = Guid.NewGuid();
        ventas.Ventas.Add(new VentaAtribuida(Guid.NewGuid(), edicion, entorno.Ayacucho.Id, 3));
        ventas.Ventas.Add(new VentaAtribuida(Guid.NewGuid(), edicion, entorno.Cusco.Id, 4));

        var delEncargado = await comoEncargado.Ejecutar(new ConsultarUnidadesVendidasEntrada("ayacucho"));
        var delaRed = await Administracion(entorno, ventas, reingresos).Ejecutar(new ConsultarUnidadesVendidasEntrada(null));

        Assert.Equal(new UnidadesVendidasConsultadas(30, 3, [new UnidadesVendidasDeFuente("ayacucho", 3)]), delEncargado.Valor, ComparadorDeVentas.Instancia);
        Assert.Equal(7, delaRed.Valor.Total);
        Assert.Equal([("ayacucho", 3), ("cusco", 4), ("lima", 0)], delaRed.Valor.PorFuente.Select(f => (f.CodigoDeFuente, f.Unidades)));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-17")]
    public async Task LaVentanaSonLosUltimos30Dias_YElEncargadoPideSoloSuFuenteAlPuerto()
    {
        var (entorno, ventas, reingresos, comoEncargado) = ComoEncargadoDeAyacucho();

        await comoEncargado.Ejecutar(new ConsultarUnidadesVendidasEntrada("ayacucho"));
        await Administracion(entorno, ventas, reingresos).Ejecutar(new ConsultarUnidadesVendidasEntrada(null));

        var (fuenteDelEncargado, desde, hasta) = ventas.Consultas[0];
        Assert.Equal(entorno.Ayacucho.Id, fuenteDelEncargado);
        Assert.Equal(EntornoDeFuentes.Ahora, hasta);
        Assert.Equal(EntornoDeFuentes.Ahora.AddDays(-30), desde);
        Assert.Null(ventas.Consultas[1].FuenteId);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-17")]
    public async Task LasDevolucionesParcialesSeRestan_YLoDevueltoNuncaDejaUnValorNegativo()
    {
        var (entorno, ventas, reingresos, _) = ComoEncargadoDeAyacucho();
        var pedidoA = Guid.NewGuid();
        var pedidoB = Guid.NewGuid();
        var libro = Guid.NewGuid();
        ventas.Ventas.Add(new VentaAtribuida(pedidoA, libro, entorno.Ayacucho.Id, 3));
        ventas.Ventas.Add(new VentaAtribuida(pedidoB, libro, entorno.Ayacucho.Id, 2));
        reingresos.Reingresos.Add(new UnidadesReingresadas(pedidoA, libro, 1));
        reingresos.Reingresos.Add(new UnidadesReingresadas(pedidoB, libro, 5));

        var resultado = await Administracion(entorno, ventas, reingresos).Ejecutar(new ConsultarUnidadesVendidasEntrada(null));

        Assert.Equal(2, resultado.Valor.Total);
        Assert.Equal(new[] { pedidoA, pedidoB }.Order(), reingresos.Consultas.Single().Order());
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-18")]
    public async Task CadaPedidoYEdicionCuentaUnaSolaVez_AunqueElPuertoRepitaLaPareja()
    {
        var (entorno, ventas, reingresos, _) = ComoEncargadoDeAyacucho();
        var pedido = Guid.NewGuid();
        var libro = Guid.NewGuid();
        ventas.Ventas.Add(new VentaAtribuida(pedido, libro, entorno.Ayacucho.Id, 2));
        ventas.Ventas.Add(new VentaAtribuida(pedido, libro, entorno.Ayacucho.Id, 2));
        ventas.Ventas.Add(new VentaAtribuida(pedido, libro, entorno.Cusco.Id, 2));

        var resultado = await Administracion(entorno, ventas, reingresos).Ejecutar(new ConsultarUnidadesVendidasEntrada(null));

        Assert.Equal(2, resultado.Valor.Total);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-18")]
    public async Task LaVentaDeRecojoCuentaParaLaSucursalQueEntrega_YElTrasladoDeReposicionNoSuma()
    {
        // El puerto ya atribuye cada unidad a una sola fuente (la que despacha o entrega); el traslado de Cusco hacia Ayacucho no es una venta y no aparece.
        var (entorno, ventas, reingresos, _) = ComoEncargadoDeAyacucho();
        var libro = Guid.NewGuid();
        ventas.Ventas.Add(new VentaAtribuida(Guid.NewGuid(), libro, entorno.Lima.Id, 1));
        ventas.Ventas.Add(new VentaAtribuida(Guid.NewGuid(), libro, entorno.Ayacucho.Id, 1));

        var resultado = await Administracion(entorno, ventas, reingresos).Ejecutar(new ConsultarUnidadesVendidasEntrada(null));

        Assert.Equal([("ayacucho", 1), ("cusco", 0), ("lima", 1)], resultado.Valor.PorFuente.Select(f => (f.CodigoDeFuente, f.Unidades)));
        Assert.Equal(2, resultado.Valor.Total);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-17")]
    public async Task SinVentas_TodoEsCeroYNoSeConsultanLosReingresos()
    {
        var (entorno, ventas, reingresos, _) = ComoEncargadoDeAyacucho();

        var resultado = await Administracion(entorno, ventas, reingresos).Ejecutar(new ConsultarUnidadesVendidasEntrada(null));

        Assert.Equal(0, resultado.Valor.Total);
        Assert.Empty(reingresos.Consultas);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-17")]
    public async Task LaAdministracionPuedePedirUnaFuenteConcreta_YUnaInexistenteEsNoEncontrada()
    {
        var (entorno, ventas, reingresos, _) = ComoEncargadoDeAyacucho();
        ventas.Ventas.Add(new VentaAtribuida(Guid.NewGuid(), Guid.NewGuid(), entorno.Cusco.Id, 4));
        var admin = Administracion(entorno, ventas, reingresos);

        var cusco = await admin.Ejecutar(new ConsultarUnidadesVendidasEntrada("cusco"));
        var inexistente = await admin.Ejecutar(new ConsultarUnidadesVendidasEntrada("piura"));
        var invalido = await admin.Ejecutar(new ConsultarUnidadesVendidasEntrada("Cusco!"));

        Assert.Equal(4, cusco.Valor.Total);
        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, invalido.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-17")]
    public async Task ElEncargadoSinCodigoDeFuente_OPidiendoLaDeOtraSucursal_SeRechazaConSuEvento()
    {
        var (entorno, ventas, _, comoEncargado) = ComoEncargadoDeAyacucho();
        ventas.Ventas.Add(new VentaAtribuida(Guid.NewGuid(), Guid.NewGuid(), entorno.Cusco.Id, 4));

        var sinCodigo = await comoEncargado.Ejecutar(new ConsultarUnidadesVendidasEntrada(null));
        var deCusco = await comoEncargado.Ejecutar(new ConsultarUnidadesVendidasEntrada("cusco"));
        var inexistente = await comoEncargado.Ejecutar(new ConsultarUnidadesVendidasEntrada("piura"));

        Assert.All([sinCodigo, deCusco, inexistente], r => Assert.Equal(CategoriaDeError.Prohibido, r.Error!.Categoria));
        Assert.Equal(["fuera-de-sucursal", "fuera-de-sucursal", "fuera-de-sucursal"], entorno.MotivosDeDenegacion);
        Assert.Empty(ventas.Consultas);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-17")]
    [InlineData("logistica")]
    [InlineData("cliente")]
    [InlineData("integracion")]
    [InlineData("visitante")]
    [InlineData("sin-contexto")]
    public async Task LogisticaClienteYLosQueNoTienenAlcance_SeRechazanConSuEvento(string quien)
    {
        var actor = quien switch
        {
            "logistica" => ActorDePrueba.DePersonalDeLogistica(),
            "cliente" => ActorDePrueba.DeCliente(),
            "integracion" => ActorDePrueba.DeIntegracion("e-commerce"),
            "visitante" => ActorDePrueba.Anonimo(),
            _ => ActorDePrueba.SinContexto(),
        };
        var escenario = new Escenario(actor);

        var conCodigo = await escenario.Caso().Ejecutar(new ConsultarUnidadesVendidasEntrada("cusco"));
        var sinCodigo = await escenario.Caso().Ejecutar(new ConsultarUnidadesVendidasEntrada(null));

        Assert.False(conCodigo.EsExito);
        Assert.False(sinCodigo.EsExito);
        Assert.Equal(2, escenario.Entorno.Denegaciones.Eventos.Count);
        Assert.Empty(escenario.Ventas.Consultas);
    }

    private sealed class ComparadorDeVentas : IEqualityComparer<UnidadesVendidasConsultadas>
    {
        public static ComparadorDeVentas Instancia { get; } = new();

        public bool Equals(UnidadesVendidasConsultadas? x, UnidadesVendidasConsultadas? y) =>
            x is not null && y is not null && x.Dias == y.Dias && x.Total == y.Total && x.PorFuente.SequenceEqual(y.PorFuente);

        public int GetHashCode(UnidadesVendidasConsultadas obj) => obj.Total;
    }
}
