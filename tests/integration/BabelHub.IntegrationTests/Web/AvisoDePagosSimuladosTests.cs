using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Pagos;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.IntegrationTests.Carrito;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// El aviso «Pagos simulados: no se realiza ningún cobro» (PAG-33; AC-PAG-29): con la pasarela simulada se ve, con texto e icono y sin región viva, <b>en el pie de toda página y en el checkout</b>; con la pasarela real, o sin el servicio compuesto, no aparece.
/// </summary>
public sealed partial class AvisoDePagosSimuladosTests
{
    private const string Texto = "Pagos simulados: no se realiza ningún cobro";

    [GeneratedRegex(@"<aside[^>]*data-bh-pagos-simulados[^>]*>.*?</aside>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Aviso();

    private sealed class ListaDePrueba : IListaDeSucursales
    {
        public Task<ListaDeSucursales?> Obtener(CancellationToken cancelacion = default) => Task.FromResult<ListaDeSucursales?>(new ListaDeSucursales([new("AYA", "Ayacucho"), new("LIM", "Lima")], true));
    }

    private sealed class ActorFijo(ActorActual actual) : IActorActual
    {
        public ActorActual? Actual { get; } = actual;
    }

    private static async Task<string> Pagina(AnfitrionWeb anfitrion, string ruta)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        using var respuesta = await anfitrion.Cliente.SendAsync(solicitud);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
    }

    private static Task<AnfitrionWeb> ConCheckout(ModoDePagosDeLaWeb? modo, ModoDeEnviosDeLaWeb? envios = null)
    {
        var linea = new LineaDeRevision(Guid.NewGuid(), Guid.NewGuid(), "Estructuras de datos", FormatoDeEdicion.Impresa, 1, 60m, 60m, DisponibilidadDeLineaDeCheckout.Disponible, null, false, false);
        var vista = new VistaDelCheckout(EstadoVisibleDelCheckout.EnEdicion, 1, 5, [linea], null, null, null, 60m, null, 60m, null, [], [], null, null, false);
        var falso = new CheckoutDelClienteFalso { RespuestaDeVista = Resultado<VistaDelCheckout>.Exito(vista) };
        var actor = new ActorFijo(ActorActual.DeUsuario(Guid.NewGuid(), "cliente", null));
        return AnfitrionWeb.Iniciar(servicios =>
        {
            servicios.AddSingleton<ICheckoutDelCliente>(falso);
            servicios.AddSingleton<IExigenciaDeVerificacionDePersona>(new ExigenciaDeVerificacionFalsa());
            servicios.AddSingleton<ITextosLegales>(new TextosDelCarrito());
            servicios.AddSingleton<IActorActual>(actor);
            servicios.AddSingleton<IListaDeSucursales>(new ListaDePrueba());
            servicios.AddSingleton(new OpcionesDeVerificacionWeb("clave-de-sitio-de-prueba"));
            servicios.AddSingleton(new OpcionesDelHostWeb(HostWeb.Tienda, []));
            servicios.AgregarProteccionContraFalsificacion();
            if (modo is not null)
            {
                servicios.AddSingleton(modo);
            }

            if (envios is not null)
            {
                servicios.AddSingleton(envios);
            }
        });
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-29")]
    public async Task Con_la_pasarela_simulada_el_pie_de_toda_pagina_dice_que_no_se_cobra_nada()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar(servicios => servicios.AddSingleton(new ModoDePagosDeLaWeb(Simulado: true)));

        foreach (var ruta in new[] { "/", "/legal/terminos" })
        {
            var html = await Pagina(anfitrion, ruta);

            var aviso = Aviso().Match(html);
            Assert.True(aviso.Success, ruta);
            Assert.Contains(Texto, aviso.Value, StringComparison.Ordinal);
            // Justo antes del pie legal, no dentro de él (el pie es de otra pantalla), y una sola vez.
            Assert.True(html.IndexOf("data-bh-pagos-simulados", StringComparison.Ordinal) < html.IndexOf("<footer", StringComparison.Ordinal), ruta);
            Assert.Single(Aviso().Matches(html));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-29")]
    public async Task El_aviso_es_accesible_con_icono_y_texto_sin_depender_del_color_ni_de_una_region_viva()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar(servicios => servicios.AddSingleton(new ModoDePagosDeLaWeb(Simulado: true)));

        var aviso = Aviso().Match(await Pagina(anfitrion, "/")).Value;

        Assert.Contains("aria-label=\"Aviso: pagos simulados\"", aviso, StringComparison.Ordinal);
        Assert.Contains("<svg", aviso, StringComparison.Ordinal);
        Assert.Contains("aria-hidden=\"true\"", aviso, StringComparison.Ordinal);
        Assert.DoesNotContain("role=", aviso, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-live", aviso, StringComparison.Ordinal);
        // Solo clases de los temas del sistema de diseño (tokens semánticos): ningún color suelto.
        Assert.DoesNotContain("style=", aviso, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(#[0-9a-fA-F]{3,8}|rgb\()", aviso);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-29")]
    public async Task Con_la_pasarela_real_o_sin_el_servicio_el_aviso_no_aparece()
    {
        await using var real = await AnfitrionWeb.Iniciar(servicios => servicios.AddSingleton(new ModoDePagosDeLaWeb(Simulado: false)));
        await using var sinServicio = await AnfitrionWeb.Iniciar();

        Assert.DoesNotContain("data-bh-pagos-simulados", await Pagina(real, "/"), StringComparison.Ordinal);
        Assert.DoesNotContain("data-bh-pagos-simulados", await Pagina(sinServicio, "/"), StringComparison.Ordinal);
        Assert.DoesNotContain(Texto, await Pagina(real, "/"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-29")]
    public async Task El_checkout_muestra_el_aviso_con_su_explicacion_ademas_del_pie()
    {
        await using var anfitrion = await ConCheckout(new ModoDePagosDeLaWeb(Simulado: true));

        var html = await Pagina(anfitrion, "/checkout");

        var avisos = Aviso().Matches(html);
        Assert.Equal(2, avisos.Count);
        Assert.Contains(ModoDePagosDeLaWeb.Explicacion, avisos[0].Value, StringComparison.Ordinal);
        Assert.DoesNotContain(ModoDePagosDeLaWeb.Explicacion, avisos[1].Value, StringComparison.Ordinal);
        Assert.All(avisos, a => Assert.Contains(Texto, a.Value, StringComparison.Ordinal));
        // El del checkout va debajo del título de la pantalla y dentro del contenido principal.
        Assert.True(html.IndexOf("Finalizar compra", StringComparison.Ordinal) < avisos[0].Index);
        Assert.True(avisos[0].Index < html.IndexOf("</main>", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-29")]
    public async Task El_checkout_con_la_pasarela_real_no_muestra_ningun_aviso()
    {
        await using var anfitrion = await ConCheckout(new ModoDePagosDeLaWeb(Simulado: false));

        Assert.DoesNotContain("data-bh-pagos-simulados", await Pagina(anfitrion, "/checkout"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-37")]
    public async Task Con_el_operador_logistico_simulado_la_tienda_dice_que_los_envios_son_simulados_y_con_otro_no()
    {
        await using var simulado = await AnfitrionWeb.Iniciar(servicios => servicios.AddSingleton(new ModoDeEnviosDeLaWeb(Simulado: true)));
        await using var checkout = await ConCheckout(new ModoDePagosDeLaWeb(Simulado: false), new ModoDeEnviosDeLaWeb(Simulado: true));
        await using var otro = await AnfitrionWeb.Iniciar(servicios => servicios.AddSingleton(new ModoDeEnviosDeLaWeb(Simulado: false)));

        var portada = await Pagina(simulado, "/");
        var enCheckout = await Pagina(checkout, "/checkout");

        Assert.Equal("Los envíos son simulados: proyecto académico", ModoDeEnviosDeLaWeb.Texto);
        Assert.Contains(ModoDeEnviosDeLaWeb.Texto, portada, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Aviso: envíos simulados\"", portada, StringComparison.Ordinal);
        Assert.True(portada.IndexOf("data-bh-envios-simulados", StringComparison.Ordinal) < portada.IndexOf("<footer", StringComparison.Ordinal));
        Assert.Contains(ModoDeEnviosDeLaWeb.Explicacion, enCheckout, StringComparison.Ordinal);
        Assert.DoesNotContain("data-bh-envios-simulados", await Pagina(otro, "/"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-29")]
    public void El_texto_del_aviso_es_exactamente_el_de_la_spec()
    {
        Assert.Equal(Texto, ModoDePagosDeLaWeb.Texto);
    }
}
