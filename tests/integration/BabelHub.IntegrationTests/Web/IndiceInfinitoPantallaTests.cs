using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// El recorrido funcional del índice infinito sobre el MVC real de la Api (web-indice-infinito T10; AC-IND-01 a 04): la misma dirección da el mismo texto, «al azar» redirige a otra dirección con otro texto, la dirección <c>al-azar</c> abre su página y otra
/// sesión abre la misma con el enlace copiado; toda página trae el aviso y el enlace al catálogo real. En memoria y sin Docker. <b>Falta, para el turno de Docker:</b> la sesión con textos pendientes que redirige a aceptar sin guardar la ruta
/// (necesita el host compuesto con Identidad real; la política sin retorno está cubierta por <see cref="IndiceInfinitoCabecerasTests"/>).
/// </summary>
public sealed class IndiceInfinitoPantallaTests
{
    private const string Aviso = "no son libros reales ni están a la venta";

    private static async Task<(HttpStatusCode Estado, string Cuerpo, HttpResponseMessage Respuesta)> Pedir(AnfitrionWeb host, string ruta)
    {
        var respuesta = await host.Cliente.GetAsync(ruta);
        return (respuesta.StatusCode, await respuesta.Content.ReadAsStringAsync(), respuesta);
    }

    [Fact]
    [Trait("Criterio", "AC-IND-01")]
    public async Task Abrir_dos_veces_la_misma_direccion_muestra_exactamente_el_mismo_texto()
    {
        await using var host = await AnfitrionDelIndice.Iniciar();

        var (estadoUno, uno, _) = await Pedir(host, "/indice-infinito/s12-p3-e4-v5-pg100");
        var (estadoDos, dos, _) = await Pedir(host, "/indice-infinito/s12-p3-e4-v5-pg100");

        Assert.Equal(HttpStatusCode.OK, estadoUno);
        Assert.Equal(HttpStatusCode.OK, estadoDos);
        var texto = AnfitrionDelIndice.TextoDeLaPagina(uno);
        Assert.Equal(texto, AnfitrionDelIndice.TextoDeLaPagina(dos));
        Assert.Equal(20, texto.Split('\n').Length);
        Assert.Equal("s12-p3-e4-v5-pg100", AnfitrionDelIndice.DireccionDeLaPagina(uno));
    }

    [Fact]
    [Trait("Criterio", "AC-IND-01")]
    public async Task El_texto_de_una_direccion_fija_es_el_del_patron_de_oro()
    {
        await using var host = await AnfitrionDelIndice.Iniciar();

        var (_, cuerpo, _) = await Pedir(host, "/indice-infinito/s1-p1-e1-v1-pg1");

        var lineas = AnfitrionDelIndice.TextoDeLaPagina(cuerpo).Split('\n');
        Assert.Equal("implcrjrmolsrñflijp ,,zhqoyjddjzpiwdn.fkktblxtnu qdxkttjrklgkv,pgftbqk", lineas[0]);
    }

    [Fact]
    [Trait("Criterio", "AC-IND-02")]
    public async Task Otra_pagina_al_azar_redirige_a_otra_direccion_con_otro_texto()
    {
        await using var host = await AnfitrionDelIndice.Iniciar();
        var (_, actual, _) = await Pedir(host, "/indice-infinito/s1-p1-e1-v1-pg1");

        var (estado, _, respuesta) = await Pedir(host, "/indice-infinito/acciones/al-azar?desde=s1-p1-e1-v1-pg1");

        Assert.Equal(HttpStatusCode.Found, estado);
        var ubicacion = respuesta.Headers.Location!.OriginalString;
        Assert.Matches(AnfitrionDelIndice.FormaDeLaUbicacion(), ubicacion);
        Assert.NotEqual("/indice-infinito/s1-p1-e1-v1-pg1", ubicacion);
        var (estadoNueva, nueva, _) = await Pedir(host, ubicacion);
        Assert.Equal(HttpStatusCode.OK, estadoNueva);
        Assert.NotEqual(AnfitrionDelIndice.TextoDeLaPagina(actual), AnfitrionDelIndice.TextoDeLaPagina(nueva));
        Assert.Equal(ubicacion["/indice-infinito/".Length..], AnfitrionDelIndice.DireccionDeLaPagina(nueva));
    }

    [Fact]
    [Trait("Criterio", "AC-IND-02")]
    public async Task El_enlace_de_otra_al_azar_de_la_pagina_lleva_la_direccion_actual_como_desde()
    {
        await using var host = await AnfitrionDelIndice.Iniciar();

        var (_, cuerpo, _) = await Pedir(host, "/indice-infinito/s7-p2-e3-v9-pg77");

        Assert.Contains("href=\"/indice-infinito/acciones/al-azar?desde=s7-p2-e3-v9-pg77\"", cuerpo, StringComparison.Ordinal);
        Assert.Contains("rel=\"nofollow\"", cuerpo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IND-02")]
    public async Task La_direccion_al_azar_literal_abre_su_pagina_y_la_accion_vive_en_otra_ruta()
    {
        await using var host = await AnfitrionDelIndice.Iniciar();

        var (estado, cuerpo, _) = await Pedir(host, "/indice-infinito/al-azar");

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal("al-azar", AnfitrionDelIndice.DireccionDeLaPagina(cuerpo));
        Assert.DoesNotContain("Esa dirección no existe", cuerpo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IND-03")]
    public async Task Otra_sesion_que_abre_el_enlace_copiado_ve_la_misma_pagina()
    {
        await using var host = await AnfitrionDelIndice.Iniciar();
        var (_, mia, _) = await Pedir(host, "/indice-infinito/s3-p1-e2-v4-pg250");
        var enlace = "/indice-infinito/" + AnfitrionDelIndice.DireccionDeLaPagina(mia);

        // Otra persona es otro cliente sin cookies del primero: el texto depende solo de la dirección.
        var (_, ajena, _) = await PedirSinCookies(host, enlace);

        Assert.Equal(AnfitrionDelIndice.TextoDeLaPagina(mia), AnfitrionDelIndice.TextoDeLaPagina(ajena));
    }

    private static async Task<(HttpStatusCode Estado, string Cuerpo, HttpResponseMessage Respuesta)> PedirSinCookies(AnfitrionWeb host, string ruta)
    {
        // Otro cliente del mismo servidor, sin las cookies del primero: es «otra persona».
        using var cliente = ((TestServer)host.Servicios.GetRequiredService<IServer>()).CreateClient();
        cliente.BaseAddress = new Uri("https://localhost/");
        var respuesta = await cliente.GetAsync(ruta);
        return (respuesta.StatusCode, await respuesta.Content.ReadAsStringAsync(), respuesta);
    }

    [Theory]
    [Trait("Criterio", "AC-IND-04")]
    [InlineData("/indice-infinito/s12-p3-e4-v5-pg100")]
    [InlineData("/indice-infinito/al-azar")]
    [InlineData("/indice-infinito/Mayuscula")]
    [InlineData("/indice-infinito/con%20espacio")]
    public async Task Toda_pagina_trae_el_aviso_de_texto_generado_y_el_enlace_al_catalogo_real(string ruta)
    {
        await using var host = await AnfitrionDelIndice.Iniciar();

        var (estado, cuerpo, _) = await Pedir(host, ruta);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains(Aviso, cuerpo, StringComparison.Ordinal);
        Assert.Contains("Texto generado", cuerpo, StringComparison.Ordinal);
        Assert.Contains("href=\"/catalogo\"", cuerpo, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Página generada\"", cuerpo, StringComparison.Ordinal);
        Assert.Contains("<pre lang=\"es\" tabindex=\"0\"", cuerpo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IND-04")]
    public async Task El_contenido_no_usa_datos_del_catalogo_el_carrito_ni_la_busqueda()
    {
        await using var host = await AnfitrionDelIndice.Iniciar();

        var (_, cuerpo, _) = await Pedir(host, "/indice-infinito/s12-p3-e4-v5-pg100");

        var principal = System.Text.RegularExpressions.Regex.Match(cuerpo, "<main.*?</main>", System.Text.RegularExpressions.RegexOptions.Singleline).Value;
        Assert.DoesNotContain("/catalogo/obras/", principal, StringComparison.Ordinal);
        Assert.DoesNotContain("/carrito", principal, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"q\"", principal, StringComparison.Ordinal);
        Assert.DoesNotContain("hx-", principal, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IND-06")]
    public async Task Una_direccion_no_valida_da_el_mensaje_amable_con_una_pagina_al_azar_y_no_un_error()
    {
        await using var host = await AnfitrionDelIndice.Iniciar();

        var (estado, cuerpo, _) = await Pedir(host, "/Indice-Infinito/ABC");

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("Esa dirección no existe en este laberinto", cuerpo, StringComparison.Ordinal);
        Assert.Matches("^[a-z0-9-]{1,64}$", AnfitrionDelIndice.DireccionDeLaPagina(cuerpo));
        Assert.Equal(20, AnfitrionDelIndice.TextoDeLaPagina(cuerpo).Split('\n').Length);
    }

    [Theory]
    [Trait("Criterio", "AC-IND-03")]
    [InlineData("/indice-infinito")]
    [InlineData("/indice-infinito/")]
    [InlineData("/indice-infinito?direccion=")]
    [InlineData("/indice-infinito?direccion=%20%20%20")]
    public async Task La_entrada_vacia_o_de_solo_espacios_redirige_a_una_al_azar(string ruta)
    {
        await using var host = await AnfitrionDelIndice.Iniciar();

        var (estado, _, respuesta) = await Pedir(host, ruta);

        Assert.Equal(HttpStatusCode.Found, estado);
        Assert.Matches(AnfitrionDelIndice.FormaDeLaUbicacion(), respuesta.Headers.Location!.OriginalString);
    }

    [Theory]
    [Trait("Criterio", "AC-IND-03")]
    [InlineData("/indice-infinito?direccion=s12-p3-e4-v5-pg100")]
    [InlineData("/indice-infinito?direccion=%20s12-p3-e4-v5-pg100%20")]
    [InlineData("/indice-infinito/?direccion=s12-p3-e4-v5-pg100")]
    public async Task El_formulario_con_una_direccion_valida_redirige_a_la_canonica(string ruta)
    {
        await using var host = await AnfitrionDelIndice.Iniciar();

        var (estado, _, respuesta) = await Pedir(host, ruta);

        Assert.Equal(HttpStatusCode.Found, estado);
        Assert.Equal("/indice-infinito/s12-p3-e4-v5-pg100", respuesta.Headers.Location!.OriginalString);
    }

    [Fact]
    [Trait("Criterio", "AC-IND-04")]
    public async Task El_formulario_ir_a_una_direccion_es_un_get_con_ejemplo_valido_y_sin_valor_previo()
    {
        await using var host = await AnfitrionDelIndice.Iniciar();

        var (_, cuerpo, _) = await Pedir(host, "/indice-infinito/s12-p3-e4-v5-pg100");

        Assert.Contains("<form method=\"get\" action=\"/indice-infinito\"", cuerpo, StringComparison.Ordinal);
        Assert.Contains("name=\"direccion\"", cuerpo, StringComparison.Ordinal);
        Assert.Contains("maxlength=\"64\"", cuerpo, StringComparison.Ordinal);
        Assert.Contains("placeholder=\"s12-p3-e4-v5-pg100\"", cuerpo, StringComparison.Ordinal);
    }
}

/// <summary>
/// El índice infinito con la política de textos legales pendientes de <c>web-base</c> (web-indice-infinito T10; D10): el índice no declara excepciones, así que un cliente con sesión y textos pendientes va a aceptarlos, y las acciones llevan
/// <c>[SinRetorno]</c>: la ruta recibida (que puede traer la dirección) <b>no se guarda</b> como destino de retorno.
/// </summary>
public sealed class IndiceInfinitoConTextosPendientesTests
{
    [Theory]
    [Trait("Criterio", "AC-IND-04")]
    [InlineData("/indice-infinito/s12-p3-e4-v5-pg100")]
    [InlineData("/indice-infinito")]
    [InlineData("/indice-infinito/acciones/al-azar?desde=s1-p1-e1-v1-pg1")]
    [InlineData("/indice-infinito/Mayuscula<script>")]
    public async Task Un_cliente_con_textos_pendientes_es_llevado_a_aceptarlos_sin_guardar_la_ruta_recibida_como_retorno(string ruta)
    {
        var (anfitrion, _, _, _, _, almacen, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = anfitrion;

        using var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        solicitud.Headers.Add("Cookie", AnfitrionConSesion.Cookie());
        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.True(respuesta.StatusCode is HttpStatusCode.Found or HttpStatusCode.SeeOther, $"Estado {(int)respuesta.StatusCode}.");
        Assert.Equal("/textos/aceptar", respuesta.Headers.Location!.OriginalString);
        Assert.Empty(almacen.Retornos);
    }
}
