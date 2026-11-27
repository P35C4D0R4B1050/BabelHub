using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Fuentes;

/// <summary>T42: un pedido con la misma edición en varias sucursales cuenta en cada una; lo devuelto se resta una sola vez por pedido y edición, repartido siempre igual, y la cifra de una sucursal es la de la red (SEL-46).</summary>
public sealed class UnidadesVendidasPorSucursalTests
{
    private sealed class Escenario
    {
        public Escenario() => Entorno = new EntornoDeFuentes(ActorDePrueba.DeAdministradorCentral());

        public EntornoDeFuentes Entorno { get; }

        public OrigenDeUnidadesVendidasDoble Ventas { get; } = new();

        public ReingresosPorDevolucionDoble Reingresos { get; } = new();

        public Guid Pedido { get; } = Guid.NewGuid();

        public Guid Libro { get; } = Guid.NewGuid();

        public Escenario Vender(Guid fuente, int unidades)
        {
            Ventas.Ventas.Add(new VentaAtribuida(Pedido, Libro, fuente, unidades));
            return this;
        }

        public Escenario Devolver(int unidades)
        {
            Reingresos.Reingresos.Add(new UnidadesReingresadas(Pedido, Libro, unidades));
            return this;
        }

        public async Task<UnidadesVendidasConsultadas> Consultar(string? fuente = null)
        {
            var resultado = await new ConsultarUnidadesVendidas(Entorno.Autorizador, new ValidadorDeConsultarUnidadesVendidas(), Entorno.Fuentes, Ventas, Reingresos, Entorno.Reloj)
                .Ejecutar(new ConsultarUnidadesVendidasEntrada(fuente));
            Assert.True(resultado.EsExito, resultado.Error?.Clave);
            return resultado.Valor;
        }
    }

    private static Dictionary<string, int> PorCodigo(UnidadesVendidasConsultadas consulta) => consulta.PorFuente.ToDictionary(f => f.CodigoDeFuente, f => f.Unidades);

    [Fact]
    [Trait("Criterio", "AC-SEL-49")]
    public async Task DieciochoEnCuscoYSeisEnAyacucho_FiguranEnCadaSucursalYVeinticuatroEnLaRed()
    {
        var escenario = new Escenario();
        escenario.Vender(escenario.Entorno.Cusco.Id, 18).Vender(escenario.Entorno.Ayacucho.Id, 6);

        var red = await escenario.Consultar();
        var cusco = await escenario.Consultar("cusco");
        var ayacucho = await escenario.Consultar("ayacucho");

        Assert.Equal((24, 18, 6), (red.Total, cusco.Total, ayacucho.Total));
        Assert.Equal((18, 6), (PorCodigo(red)["cusco"], PorCodigo(red)["ayacucho"]));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-49")]
    public async Task ConVeinteDevueltas_QuedanCuatroEnLaRed_RepartidasSiempreIgual_YCadaSucursalDaLaMismaCifraQueEnLaRed()
    {
        var escenario = new Escenario();
        escenario.Vender(escenario.Entorno.Cusco.Id, 18).Vender(escenario.Entorno.Ayacucho.Id, 6).Devolver(20);

        var red = await escenario.Consultar();
        escenario.Ventas.Ventas.Reverse();
        var redConOtroOrden = await escenario.Consultar();
        var porSucursal = new Dictionary<string, int>
        {
            ["cusco"] = (await escenario.Consultar("cusco")).Total,
            ["ayacucho"] = (await escenario.Consultar("ayacucho")).Total,
            ["lima"] = (await escenario.Consultar("lima")).Total,
        };

        Assert.Equal(4, red.Total);
        Assert.All(red.PorFuente, f => Assert.True(f.Unidades >= 0));
        Assert.Equal(PorCodigo(red), PorCodigo(redConOtroOrden));
        Assert.Equal(PorCodigo(red), porSucursal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-49")]
    public async Task LoDevueltoSeRepartePorIdentificadorDeFuenteAscendente_SinPasarDeLoVendidoEnCadaUna()
    {
        var escenario = new Escenario();
        var enOrden = new[] { escenario.Entorno.Cusco, escenario.Entorno.Ayacucho }.OrderBy(f => f.Id.ToString("D"), StringComparer.Ordinal).ToList();
        escenario.Vender(enOrden[1].Id, 5).Vender(enOrden[0].Id, 5).Devolver(7);

        var red = PorCodigo(await escenario.Consultar());

        Assert.Equal((0, 3), (red[enOrden[0].Codigo], red[enOrden[1].Codigo]));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-49")]
    public async Task DevolverMasDeLoVendidoNoDejaNegativos()
    {
        var escenario = new Escenario();
        escenario.Vender(escenario.Entorno.Cusco.Id, 2).Vender(escenario.Entorno.Ayacucho.Id, 1).Devolver(99);

        var red = await escenario.Consultar();

        Assert.Equal(0, red.Total);
        Assert.All(red.PorFuente, f => Assert.Equal(0, f.Unidades));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-49")]
    public async Task UnaTernaRepetidaPorElPuertoCuentaUnaSolaVez_LaDeMasUnidades()
    {
        var escenario = new Escenario();
        escenario.Vender(escenario.Entorno.Cusco.Id, 2).Vender(escenario.Entorno.Cusco.Id, 3);

        Assert.Equal(3, (await escenario.Consultar()).Total);
    }
}
