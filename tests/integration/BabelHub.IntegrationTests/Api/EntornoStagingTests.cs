using BabelHub.Ecommerce.Api.Pagos;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BabelHub.IntegrationTests.Api;

/// <summary>
/// El entorno <c>Staging</c> del E-commerce (PAG-33, PAG-34; AC-PAG-29, AC-PAG-30): el entorno de revisión de la tienda por la pasarela de pago, antes de tener sus llaves. <b>Es un entorno de tipo producción</b> (todo el endurecimiento de producción
/// sigue activo) y admite <b>solo</b> la pasarela simulada, que se anuncia; <c>Production</c> sigue fallando cerrado sin pasarela real, y ninguna otra combinación cobra a ciegas.
/// </summary>
public sealed class EntornoStagingTests
{
    private static Dictionary<string, string?> ConfiguracionDeUnEntornoDeTipoProduccion()
    {
        var configuracion = AnfitrionDelEcommerceTests.ConfiguracionCompleta();
        foreach (var (ruta, valor) in AnfitrionDelEcommerceTests.ConfiguracionDeProduccion())
        {
            configuracion[ruta] = valor;
        }

        return configuracion;
    }

    private static WebApplicationBuilder Constructor(Dictionary<string, string?> configuracion, string entorno)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = entorno });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(configuracion);
        builder.Services.AddControllersWithViews();
        builder.Host.UseDefaultServiceProvider(o =>
        {
            o.ValidateScopes = true;
            o.ValidateOnBuild = true;
        });
        return builder;
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-30")]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    [InlineData("staging", true)]
    [InlineData("Development", false)]
    [InlineData("Test", false)]
    [InlineData("Qa", false)]
    public void Production_y_Staging_son_los_unicos_entornos_de_tipo_produccion(string entorno, bool esperado)
    {
        Assert.Equal(esperado, new EntornoDePrueba(entorno).EsDeTipoProduccion());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-29")]
    [Trait("Criterio", "AC-PAG-34")]
    public void En_Staging_con_el_simulador_el_servicio_arranca_y_anuncia_los_pagos_simulados()
    {
        var builder = Constructor(ConfiguracionDeUnEntornoDeTipoProduccion(), "Staging");

        builder.AgregarEcommerce(ModoDeArranque.Normal);
        using var app = builder.Build();

        var modo = app.Services.GetRequiredService<ModoDePagosDeLaWeb>();
        Assert.True(modo.Simulado);
        Assert.Equal("Pagos simulados: no se realiza ningún cobro", ModoDePagosDeLaWeb.Texto);
        Assert.Contains(app.Services.GetServices<IAdaptadorSaliente>(), a => a.Nombre == "pasarela-de-pago" && !a.EsNulo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-34")]
    public void En_Staging_se_exige_todo_lo_de_produccion_y_cada_falta_se_nombra_sin_su_valor()
    {
        // Solo la configuración de desarrollo: sin Redis con TLS, proxies, correo, almacén de libros ni envío por n8n. Un entorno de revisión no afloja nada salvo el cobro.
        var builder = Constructor(AnfitrionDelEcommerceTests.ConfiguracionCompleta(), "Staging");

        var falla = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => builder.AgregarEcommerce(ModoDeArranque.Normal));

        Assert.Contains("BABELHUB__Ecommerce__ProxiesConocidos", falla.Message, StringComparison.Ordinal);
        Assert.Contains("BABELHUB__Correo__Smtp__Servidor", falla.Message, StringComparison.Ordinal);
        Assert.Contains("BABELHUB__Ecommerce__Redis__Tls", falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-34")]
    public void En_Staging_la_salida_al_Inventario_exige_https_como_en_produccion()
    {
        var configuracion = ConfiguracionDeUnEntornoDeTipoProduccion();
        configuracion[OpcionesDelAnfitrionDelEcommerce.RutaDelInventario] = "http://inventario.interno/";
        var builder = Constructor(configuracion, "Staging");

        var falla = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => builder.AgregarEcommerce(ModoDeArranque.Normal));

        Assert.Contains("HTTPS", falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-30")]
    public void En_Production_el_simulador_no_arranca_y_nunca_se_cobra_a_ciegas()
    {
        var builder = Constructor(ConfiguracionDeUnEntornoDeTipoProduccion(), "Production");

        var falla = Assert.Throws<ConfiguracionDePagosInvalidaException>(() => builder.AgregarEcommerce(ModoDeArranque.Normal));

        Assert.Contains("simulador", falla.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-30")]
    [InlineData("Staging", "sk_" + "test_llave-de-pruebas-0123456789")]
    [InlineData("Staging", "sk_" + "live_llave-de-produccion-0123456789")]
    [InlineData("Production", "sk_" + "test_llave-de-pruebas-0123456789")]
    public void Con_culqi_y_una_llave_que_no_es_de_su_entorno_el_arranque_falla_en_Staging_y_en_Production(string entorno, string llave)
    {
        var configuracion = ConfiguracionDeUnEntornoDeTipoProduccion();
        configuracion["Pagos:Pasarela"] = "culqi";
        configuracion["Pagos:LlavePrivada"] = llave;
        var builder = Constructor(configuracion, entorno);

        var falla = Assert.Throws<ConfiguracionDePagosInvalidaException>(() => builder.AgregarEcommerce(ModoDeArranque.Normal));

        Assert.DoesNotContain(llave, falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-30")]
    public void En_Production_con_culqi_y_la_llave_de_produccion_arranca_con_el_adaptador_real_y_sin_el_aviso_de_pagos_simulados()
    {
        var llave = "sk_" + "live_llave-de-produccion-0123456789";
        var configuracion = ConfiguracionDeUnEntornoDeTipoProduccion();
        configuracion["Pagos:Pasarela"] = "culqi";
        configuracion["Pagos:LlavePrivada"] = llave;
        var builder = Constructor(configuracion, "Production");

        builder.AgregarEcommerce(ModoDeArranque.Normal);
        using var app = builder.Build();

        Assert.IsType<CulqiPasarelaDePago>(app.Services.GetRequiredService<BabelHub.Ecommerce.Application.Pagos.Interno.IPasarelaDePago>());
        Assert.False(app.Services.GetRequiredService<ModoDePagosDeLaWeb>().Simulado);
        Assert.Contains(app.Services.GetServices<IAdaptadorSaliente>(), a => a.Nombre == "pasarela-de-pago" && !a.EsNulo);
        var rutas = ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>();
        Assert.DoesNotContain(rutas, e => (e.RoutePattern.RawText ?? string.Empty).Contains("integraciones/pasarela", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-30")]
    public void En_Production_con_culqi_sin_llave_el_arranque_falla_nombrando_la_variable()
    {
        var configuracion = ConfiguracionDeUnEntornoDeTipoProduccion();
        configuracion["Pagos:Pasarela"] = "culqi";
        configuracion.Remove("Pagos:LlavePrivada");
        var builder = Constructor(configuracion, "Production");

        var falla = Assert.Throws<ConfiguracionDePagosInvalidaException>(() => builder.AgregarEcommerce(ModoDeArranque.Normal));

        Assert.Contains("BABELHUB__Pagos__LlavePrivada", falla.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-30")]
    [Trait("Criterio", "AC-PAG-42")]
    [InlineData("Staging")]
    [InlineData("Development")]
    public void Sin_pasarela_el_servicio_arranca_sin_ningun_aviso_de_simulacion_y_con_el_cobro_cerrado(string entorno)
    {
        var configuracion = entorno == "Staging" ? ConfiguracionDeUnEntornoDeTipoProduccion() : AnfitrionDelEcommerceTests.ConfiguracionCompleta();
        configuracion["Pagos:Pasarela"] = "ninguna";
        // El operador logístico sigue siendo el simulado (no hay uno real): como no se puede comprar, tampoco se anuncia.
        configuracion["Envios:Operador"] = "simulador";
        var builder = Constructor(configuracion, entorno);

        builder.AgregarEcommerce(ModoDeArranque.Normal);
        using var app = builder.Build();

        Assert.False(app.Services.GetRequiredService<ModoDePagosDeLaWeb>().Simulado);
        Assert.False(app.Services.GetRequiredService<ModoDeEnviosDeLaWeb>().Simulado);
        Assert.Equal("NuloPasarelaDePago", app.Services.GetRequiredService<BabelHub.Ecommerce.Application.Pagos.Interno.IPasarelaDePago>().GetType().Name);
        Assert.True(app.Services.GetRequiredService<BabelHub.Ecommerce.Application.Pagos.Interno.OpcionesDelCobro>().Cerrado);
        // No hay proveedor de pago con el que hablar: la coherencia del arranque de un entorno de tipo producción no lo toma por un adaptador real que falta.
        ArranqueSeguro.ExigirCoherencia(ModoDeArranque.Normal, builder.Environment.EsDeTipoProduccion(), app.Services.GetServices<IAdaptadorSaliente>());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-30")]
    public void En_Production_sin_pasarela_el_arranque_se_niega()
    {
        var configuracion = ConfiguracionDeUnEntornoDeTipoProduccion();
        configuracion["Pagos:Pasarela"] = "ninguna";
        var builder = Constructor(configuracion, "Production");

        var falla = Assert.Throws<ConfiguracionDePagosInvalidaException>(() => builder.AgregarEcommerce(ModoDeArranque.Normal));

        Assert.Contains("ninguna", falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-30")]
    public void Sin_pasarela_al_restaurar_el_adaptador_se_declara_nulo()
    {
        var configuracion = ConfiguracionDeUnEntornoDeTipoProduccion();
        configuracion["Pagos:Pasarela"] = "ninguna";
        var builder = Constructor(configuracion, "Staging");
        builder.Services.AddSingleton(typeof(ModoDeArranque), ModoDeArranque.Ensayo);

        builder.AgregarEcommerce(ModoDeArranque.Ensayo);
        using var app = builder.Build();

        Assert.Contains(app.Services.GetServices<IAdaptadorSaliente>(), a => a.Nombre == "pasarela-de-pago" && a.EsNulo && a.EsHaciaProveedor);
        ArranqueSeguro.ExigirCoherencia(ModoDeArranque.Ensayo, esProduccion: true, app.Services.GetServices<IAdaptadorSaliente>());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-29")]
    public void En_Development_el_simulador_tambien_se_anuncia_y_con_un_modo_sin_llamadas_reales_no()
    {
        var desarrollo = Constructor(AnfitrionDelEcommerceTests.ConfiguracionCompleta(), "Development");
        desarrollo.AgregarEcommerce(ModoDeArranque.Normal);
        using var app = desarrollo.Build();
        Assert.True(app.Services.GetRequiredService<ModoDePagosDeLaWeb>().Simulado);

        var ensayo = Constructor(ConfiguracionDeUnEntornoDeTipoProduccion(), "Staging");
        ensayo.Services.AddSingleton(typeof(ModoDeArranque), ModoDeArranque.Ensayo);
        ensayo.AgregarEcommerce(ModoDeArranque.Ensayo);
        using var appEnsayo = ensayo.Build();
        Assert.False(appEnsayo.Services.GetRequiredService<ModoDePagosDeLaWeb>().Simulado);
    }


    [Theory]
    [Trait("Criterio", "AC-PAG-37")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void Con_el_operador_logistico_simulado_Staging_y_Production_arrancan_y_la_tienda_lo_anuncia(string entorno)
    {
        // No hay operador logístico real y no lo habrá (DD-22): se opera con el simulado y se dice (PAG-42). La pasarela real sigue sin existir: en Production, con el simulador de pagos, no arranca.
        var configuracion = ConfiguracionDeUnEntornoDeTipoProduccion();
        configuracion["Envios:Operador"] = "simulador";
        var builder = Constructor(configuracion, entorno);

        if (entorno == "Production")
        {
            Assert.Throws<ConfiguracionDePagosInvalidaException>(() => builder.AgregarEcommerce(ModoDeArranque.Normal));
            return;
        }

        builder.AgregarEcommerce(ModoDeArranque.Normal);
        using var app = builder.Build();
        Assert.True(app.Services.GetRequiredService<ModoDeEnviosDeLaWeb>().Simulado);
        ArranqueSeguro.ExigirCoherencia(ModoDeArranque.Normal, esProduccion: true, app.Services.GetServices<IAdaptadorSaliente>());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-37")]
    public void Sin_operador_logistico_en_un_entorno_de_tipo_produccion_el_arranque_se_niega_y_con_la_pasarela_real_nula_tambien()
    {
        IAdaptadorSaliente[] sinOperador = [new AdaptadorDePrueba("pasarela-de-pago", esNulo: false), new AdaptadorDePrueba("operador-logistico", esNulo: true)];

        Assert.Throws<ArranqueRechazadoException>(() => ArranqueSeguro.ExigirCoherencia(ModoDeArranque.Normal, esProduccion: true, sinOperador));
    }

    private sealed class AdaptadorDePrueba(string nombre, bool esNulo) : IAdaptadorSaliente
    {
        public string Nombre { get; } = nombre;

        public bool EsHaciaProveedor => true;

        public bool EsNulo { get; } = esNulo;
    }

    private sealed class EntornoDePrueba(string nombre) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nombre;

        public string ApplicationName { get; set; } = "prueba";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
