using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Envios;

/// <summary>La revisión de envíos estancados y de guías sin uso (T11; ENV-12, R03, R05).</summary>
public sealed class RevisarEnviosEstancadosTests
{
    private static Envio ConDespachoIniciado(EntornoDeEnvios entorno, DateTimeOffset cuando)
    {
        var envio = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "lima-central", [Guid.NewGuid()], cuando);
        envio.TomarSolicitudDeGuia(cuando, Guid.NewGuid());
        envio.RegistrarGuia(NumeroDeGuia.Crear("GUIA-000123"), "operador-a", cuando);
        envio.IniciarDespacho(cuando);
        envio.LiberarArrendamientoDelDespacho();
        entorno.Envios.Sembrar(envio);
        return envio;
    }

    private static Envio CanceladoConGuia(EntornoDeEnvios entorno, DateTimeOffset cancelado)
    {
        var envio = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "lima-central", [Guid.NewGuid()], cancelado.AddDays(-1));
        envio.TomarSolicitudDeGuia(cancelado.AddDays(-1), Guid.NewGuid());
        envio.RegistrarGuia(NumeroDeGuia.Crear("GUIA-000999"), "operador-a", cancelado.AddDays(-1));
        envio.Cancelar(cancelado);
        entorno.Envios.Sembrar(envio);
        return envio;
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-11")]
    public async Task Un_envio_con_el_despacho_iniciado_hace_mas_de_24_horas_publica_una_alerta_sin_datos()
    {
        var entorno = new EntornoDeEnvios();
        ConDespachoIniciado(entorno, EntornoDeEnvios.AhoraInicial.AddHours(-25));
        ConDespachoIniciado(entorno, EntornoDeEnvios.AhoraInicial.AddHours(-23));

        var resultado = await entorno.RevisarEstancados.Ejecutar(100);

        Assert.Equal(1, resultado.Valor);
        var alerta = Assert.Single(entorno.Alertas.Alertas);
        Assert.Equal("dependencia-no-necesaria-fallida", alerta.Motivo);
        Assert.DoesNotContain("GUIA", alerta.Motivo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-11")]
    public async Task Una_guia_sin_uso_de_un_envio_cancelado_alerta_una_sola_vez_en_la_ventana_de_24_a_25_horas()
    {
        var entorno = new EntornoDeEnvios();
        CanceladoConGuia(entorno, EntornoDeEnvios.AhoraInicial.AddHours(-12));
        CanceladoConGuia(entorno, EntornoDeEnvios.AhoraInicial.AddHours(-24).AddMinutes(-30));
        CanceladoConGuia(entorno, EntornoDeEnvios.AhoraInicial.AddHours(-40));

        var primera = await entorno.RevisarEstancados.Ejecutar(100);
        entorno.Reloj.Ahora += TimeSpan.FromHours(1);
        var siguiente = await entorno.RevisarEstancados.Ejecutar(100);

        Assert.Equal(1, primera.Valor);
        Assert.Equal(0, siguiente.Valor);
        Assert.Single(entorno.Alertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-11")]
    public async Task Una_alerta_que_no_se_puede_publicar_no_interrumpe_la_revision()
    {
        var entorno = new EntornoDeEnvios();
        ConDespachoIniciado(entorno, EntornoDeEnvios.AhoraInicial.AddHours(-25));
        entorno.Alertas.Falla = new InvalidOperationException("sin alertas");

        var resultado = await entorno.RevisarEstancados.Ejecutar(100);

        Assert.True(resultado.EsExito);
        Assert.Equal(0, resultado.Valor);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-11")]
    [InlineData("cliente")]
    [InlineData("logistica")]
    [InlineData("anonimo")]
    public async Task Solo_el_actor_sistema_corre_la_revision(string quien)
    {
        var actor = quien switch
        {
            "cliente" => ActorDePrueba.DeCliente(),
            "logistica" => ActorDePrueba.DePersonalDeLogistica(),
            _ => ActorDePrueba.Anonimo(),
        };
        var entorno = new EntornoDeEnvios(actor);
        var revisar = new RevisarEnviosEstancados(actor, entorno.Envios, entorno.Alertas, entorno.Reloj);

        var resultado = await revisar.Ejecutar(100);

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
    }
}
