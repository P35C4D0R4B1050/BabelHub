using System.Net;
using System.Text.Json;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Inventario;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>
/// T40: la venta por sucursal por la Api con PostgreSQL real: crear y la consulta previa aceptan <c>codigoDeSucursal</c> por línea con el esquema estricto, y el problema de falta de stock y la consulta previa devuelven
/// el vendible de la sucursal (SEL-41 a SEL-44).
/// </summary>
public sealed class ApiDeReservasPorSucursalTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private ApiDeReservasHost _api = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _api = await ApiDeReservasHost.Crear(bd);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) => JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();

    private static string Linea(Guid linea, Guid edicion, int cantidad, string? sucursal) =>
        $$"""{"lineaId":"{{linea}}","edicionId":"{{edicion}}","cantidad":{{cantidad}}{{(sucursal is null ? string.Empty : $",\"codigoDeSucursal\":\"{sucursal}\"")}}}""";

    private static string Recojo(Guid pedido, string? comun, params string[] lineas) =>
        $$"""{"pedidoId":"{{pedido}}","tipoDeEntrega":"recojo",{{(comun is null ? string.Empty : $"\"codigoDeSucursalDeRecojo\":\"{comun}\",")}}"lineas":[{{string.Join(',', lineas)}}]}""";

    private static string Previa(string? comun, params string[] lineas) =>
        $$"""{"tipoDeEntrega":"recojo",{{(comun is null ? string.Empty : $"\"codigoDeSucursalDeRecojo\":\"{comun}\",")}}"lineas":[{{string.Join(',', lineas)}}]}""";

    private Task<HttpResponseMessage> Crear(string json) =>
        _api.Enviar(HttpMethod.Post, "/reservas", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: Guid.NewGuid(), json: json, clave: Guid.NewGuid().ToString("N"));

    private Task<HttpResponseMessage> Consultar(string json) => _api.Enviar(HttpMethod.Post, "/reservas/consulta-previa", ApiDeReservasHost.CredencialDeDisponibilidad, json: json);

    private async Task<(Guid Ayacucho, Guid Cusco, Guid Libro)> Escenario()
    {
        var ayacucho = await bd.AgregarFuente("ayacucho", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(ayacucho, libro, 5, 0);
        await bd.PonerStock(cusco, libro, 30, 0);
        return (ayacucho, cusco, libro);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-42")]
    public async Task Crear_ConSucursalPorLinea_Devuelve201NombrandoCadaSucursalSinTraslado()
    {
        var (ayacucho, cusco, libro) = await Escenario();
        var deAyacucho = Guid.NewGuid();
        var deCusco = Guid.NewGuid();

        var respuesta = await Crear(Recojo(Guid.NewGuid(), null, Linea(deAyacucho, libro, 5, "ayacucho"), Linea(deCusco, libro, 18, "cusco")));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var lineas = (await Cuerpo(respuesta)).GetProperty("lineas").EnumerateArray().ToDictionary(l => l.GetProperty("lineaId").GetGuid());
        Assert.Equal(("ayacucho", false), (lineas[deAyacucho].GetProperty("codigoDeFuente").GetString(), lineas[deAyacucho].GetProperty("requiereTraslado").GetBoolean()));
        Assert.Equal(("cusco", false), (lineas[deCusco].GetProperty("codigoDeFuente").GetString(), lineas[deCusco].GetProperty("requiereTraslado").GetBoolean()));
        Assert.Equal((5, 5), await bd.StockDe(ayacucho, libro));
        Assert.Equal((30, 18), await bd.StockDe(cusco, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-43")]
    public async Task Crear_SinStockEnLaSucursal_Es409ConLaLineaYElVendibleDeLaSucursal()
    {
        var (ayacucho, cusco, libro) = await Escenario();
        var segunda = Guid.NewGuid();

        var respuesta = await Crear(Recojo(Guid.NewGuid(), null, Linea(Guid.NewGuid(), libro, 3, "ayacucho"), Linea(segunda, libro, 3, "ayacucho")));

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var cuerpo = await Cuerpo(respuesta);
        Assert.Equal(CodigosDeErrorDelInventario.StockInsuficiente, cuerpo.GetProperty("code").GetString());
        var falta = Assert.Single(cuerpo.GetProperty("lineas").EnumerateArray());
        Assert.Equal((segunda, 3, 35, 5), (falta.GetProperty("lineaId").GetGuid(), falta.GetProperty("solicitada").GetInt32(), falta.GetProperty("disponibleEnLaRed").GetInt32(), falta.GetProperty("disponibleEnLaSucursal").GetInt32()));
        Assert.Equal((5, 0), await bd.StockDe(ayacucho, libro));
        Assert.Equal((30, 0), await bd.StockDe(cusco, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-43")]
    public async Task LaFaltaDeStockDeUnaSolicitudSinSucursalNoLlevaElCampoNuevo()
    {
        var (_, _, libro) = await Escenario();

        var respuesta = await Crear(Recojo(Guid.NewGuid(), "ayacucho", Linea(Guid.NewGuid(), libro, 5, null)));
        var agotado = await bd.AgregarEdicion();
        var sinStock = await Crear(Recojo(Guid.NewGuid(), "ayacucho", Linea(Guid.NewGuid(), agotado, 1, null)));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, sinStock.StatusCode);
        Assert.DoesNotContain("disponibleEnLaSucursal", await sinStock.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-44")]
    [InlineData("mezcla", "lineas.codigoDeSucursal")]
    [InlineData("inexistente", "lineas.codigoDeSucursal")]
    [InlineData("vacia", "lineas.codigoDeSucursal")]
    [InlineData("comun", "codigoDeSucursalDeRecojo")]
    public async Task UnaFormaInvalida_Es400ConElCampo_EnCrearYEnLaConsultaPrevia_SinMoverStock(string caso, string campo)
    {
        var (ayacucho, _, libro) = await Escenario();
        var otro = await bd.AgregarEdicion();
        await bd.PonerStock(ayacucho, otro, 5, 0);
        var (comun, lineas) = caso switch
        {
            "mezcla" => ((string?)null, new[] { Linea(Guid.NewGuid(), libro, 1, "ayacucho"), Linea(Guid.NewGuid(), otro, 1, null) }),
            "inexistente" => (null, [Linea(Guid.NewGuid(), libro, 1, "tacna-9")]),
            "vacia" => (null, [Linea(Guid.NewGuid(), libro, 1, string.Empty)]),
            _ => ("ayacucho", [Linea(Guid.NewGuid(), libro, 1, "ayacucho")]),
        };

        var crear = await Crear(Recojo(Guid.NewGuid(), comun, lineas));
        var previa = await Consultar(Previa(comun, lineas));

        foreach (var respuesta in new[] { crear, previa })
        {
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            var cuerpo = await Cuerpo(respuesta);
            Assert.Equal(CodigosDeErrorDelInventario.ValidacionInvalida, cuerpo.GetProperty("code").GetString());
            Assert.Contains(campo, cuerpo.GetProperty("errores").EnumerateArray().Select(e => e.GetProperty("campo").GetString()));
        }

        Assert.Equal((5, 0), await bd.StockDe(ayacucho, libro));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-44")]
    [InlineData("\"codigoDeSucursal\":7")]
    [InlineData("\"codigoDeSucursalElegida\":\"ayacucho\"")]
    public async Task ElEsquemaSigueSiendoEstrictoEnCadaLinea(string campo)
    {
        var (_, _, libro) = await Escenario();
        var linea = $$"""{"lineaId":"{{Guid.NewGuid()}}","edicionId":"{{libro}}","cantidad":1,{{campo}}}""";

        var crear = await Crear(Recojo(Guid.NewGuid(), null, linea));
        var previa = await Consultar(Previa(null, linea));

        Assert.Equal(HttpStatusCode.BadRequest, crear.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, previa.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-45")]
    public async Task LaConsultaPrevia_DiceQueLineasCubreCadaSucursal_ConSuVendible_SinTrasladoNiPlazoNiReserva()
    {
        var (ayacucho, cusco, libro) = await Escenario();
        var segunda = Guid.NewGuid();

        var respuesta = await Consultar(Previa(null, Linea(Guid.NewGuid(), libro, 3, "ayacucho"), Linea(segunda, libro, 3, "ayacucho"), Linea(Guid.NewGuid(), libro, 18, "cusco")));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var lineas = (await Cuerpo(respuesta)).GetProperty("lineas").EnumerateArray().ToList();
        Assert.Equal(["disponible", "sin-stock", "disponible"], lineas.Select(l => l.GetProperty("resultado").GetString()));
        Assert.Equal((segunda, 5), (lineas[1].GetProperty("lineaId").GetGuid(), lineas[1].GetProperty("disponibleEnLaSucursal").GetInt32()));
        Assert.False(lineas[0].TryGetProperty("disponibleEnLaSucursal", out _));
        Assert.All(lineas, l =>
        {
            Assert.False(l.GetProperty("requiereTraslado").GetBoolean());
            Assert.Equal(JsonValueKind.Null, l.GetProperty("plazoEstimadoDias").ValueKind);
            Assert.Equal(0, l.GetProperty("disponibleEnLaRed").GetInt32());
        });
        Assert.Equal((5, 0), await bd.StockDe(ayacucho, libro));
        Assert.Equal((30, 0), await bd.StockDe(cusco, libro));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));
    }
}
