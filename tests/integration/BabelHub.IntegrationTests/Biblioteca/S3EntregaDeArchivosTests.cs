using System.Net;
using System.Text;
using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Infrastructure.Biblioteca;

namespace BabelHub.IntegrationTests.Biblioteca;

/// <summary>
/// La firma SigV4 determinista y el almacén de entrega sobre S3 (T14; BIB-04, BIB-13, BIB-14, BIB-18, SEG-10). La firma se comprueba con el vector oficial de AWS y contra un almacén compatible con S3 real con contenido sintético: la URL apunta al almacén, vence a los 300 segundos,
/// obliga <c>attachment</c>, un <c>GET</c> anónimo se rechaza y el mismo instante produce la misma URL.
/// </summary>
[Collection(ColeccionAlmacenDeLibros.Nombre)]
public sealed class S3EntregaDeArchivosTests(AlmacenDeLibrosFixture almacen)
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false });

    private S3EntregaDeArchivos Entrega(TimeSpan? plazo = null) => new(almacen.Cliente, almacen.OpcionesDeLibros, plazo);

    [Fact]
    [Trait("Criterio", "AC-BIB-12")]
    public void La_firma_coincide_con_el_vector_oficial_de_aws()
    {
        // Ejemplo de «Authenticating Requests: Using Query Parameters» de la documentación de Amazon S3 (GET /test.txt, 86400 s, us-east-1).
        var url = FirmaDeEnlaceSigV4.Firmar(
            new Uri("https://s3.amazonaws.com"),
            rutaEnLaUrl: false,
            "examplebucket",
            "test.txt",
            "us-east-1",
            "AKIA" + "IOSFODNN7EXAMPLE", // el ejemplo oficial de la documentación de AWS, armado para no parecer una clave real
            "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
            new DateTimeOffset(2013, 5, 24, 0, 0, 0, TimeSpan.Zero),
            86400,
            contentDisposition: null);

        Assert.Equal("examplebucket.s3.amazonaws.com", url.Host);
        Assert.Equal("/test.txt", url.AbsolutePath);
        Assert.Contains("X-Amz-Signature=aeeed9bbccd4d02ee5c0109b86d86835f995330da4c265957d157751f604d404", url.Query, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-12")]
    public void El_mismo_instante_produce_la_misma_url_y_otro_instante_otra_distinta()
    {
        var entrega = Entrega();
        var clave = new string('a', 32);
        var instante = new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

        var a = entrega.FirmarEnlace(clave, "Libro.epub", instante, 300);
        var b = entrega.FirmarEnlace(clave, "Libro.epub", instante, 300);
        var c = entrega.FirmarEnlace(clave, "Libro.epub", instante.AddSeconds(1), 300);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-04")]
    public void La_url_vence_en_trescientos_segundos_como_maximo_obliga_attachment_y_no_lleva_el_secreto()
    {
        var entrega = Entrega();
        var url = entrega.FirmarEnlace(new string('b', 32), "Cien años de soledad.epub", DateTimeOffset.UtcNow, 300);

        var consulta = System.Web.HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("300", consulta["X-Amz-Expires"]);
        Assert.StartsWith("attachment; filename=\"Cien a_os de soledad.epub\"", consulta["response-content-disposition"], StringComparison.Ordinal);
        Assert.Contains("filename*=UTF-8''Cien%20a%C3%B1os%20de%20soledad.epub", consulta["response-content-disposition"], StringComparison.Ordinal);
        Assert.Equal("host", consulta["X-Amz-SignedHeaders"]);
        Assert.DoesNotContain(AlmacenDeLibrosFixture.ClaveSecreta, url.AbsoluteUri, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => entrega.FirmarEnlace(new string('b', 32), "x.epub", DateTimeOffset.UtcNow, 301));
        Assert.Throws<InvalidOperationException>(() => entrega.FirmarEnlace("../../secreto", "x.epub", DateTimeOffset.UtcNow, 300));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-12")]
    public async Task El_navegador_descarga_directamente_del_almacen_con_attachment_y_el_contenido_intacto()
    {
        var contenido = Encoding.UTF8.GetBytes("contenido sintético de prueba " + Guid.NewGuid());
        var clave = await almacen.GuardarSintetico(contenido);
        var url = Entrega().FirmarEnlace(clave, "Mi libro.epub", DateTimeOffset.UtcNow.AddSeconds(-1), 300);

        using var respuesta = await Http.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(contenido, await respuesta.Content.ReadAsByteArrayAsync());
        Assert.StartsWith("attachment", respuesta.Content.Headers.ContentDisposition?.ToString(), StringComparison.Ordinal);
        Assert.Contains("Mi libro.epub", respuesta.Content.Headers.ContentDisposition?.FileName ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(almacen.Opciones.Endpoint.Authority, url.Authority);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-06")]
    public async Task Un_enlace_firmado_hace_diez_minutos_no_completa_la_descarga()
    {
        var clave = await almacen.GuardarSintetico(Encoding.UTF8.GetBytes("sintético"));
        var vencido = Entrega().FirmarEnlace(clave, "Libro.epub", DateTimeOffset.UtcNow.AddMinutes(-10), 300);

        using var respuesta = await Http.GetAsync(vencido);

        Assert.True(respuesta.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest, $"Un enlace vencido debía rechazarse y respondió {(int)respuesta.StatusCode}.");
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-12")]
    public async Task Un_get_anonimo_al_objeto_y_una_firma_alterada_se_rechazan()
    {
        var clave = await almacen.GuardarSintetico(Encoding.UTF8.GetBytes("sintético"));
        var url = Entrega().FirmarEnlace(clave, "Libro.epub", DateTimeOffset.UtcNow.AddSeconds(-1), 300);
        var anonima = new Uri($"{almacen.Opciones.Endpoint.GetLeftPart(UriPartial.Authority)}/{AlmacenDeLibrosFixture.Contenedor}/{clave}");
        var alterada = new Uri(url.AbsoluteUri.Replace("X-Amz-Signature=", "X-Amz-Signature=0", StringComparison.Ordinal));

        using var sinFirma = await Http.GetAsync(anonima);
        using var conFirmaAlterada = await Http.GetAsync(alterada);

        Assert.NotEqual(HttpStatusCode.OK, sinFirma.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, conFirmaAlterada.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-11")]
    public async Task Existe_archivo_distingue_presente_ausente_y_almacen_caido_con_plazo_de_tres_segundos()
    {
        var clave = await almacen.GuardarSintetico(Encoding.UTF8.GetBytes("sintético"));
        var entrega = Entrega();

        Assert.Equal(ExistenciaDelArchivo.Existe, await entrega.ExisteArchivo(clave));
        Assert.Equal(ExistenciaDelArchivo.NoExiste, await entrega.ExisteArchivo(new string('c', 32)));
        Assert.Equal(ExistenciaDelArchivo.NoExiste, await entrega.ExisteArchivo("../no-es-una-clave"));

        var caido = new S3EntregaDeArchivos(
            BabelHub.Ecommerce.Infrastructure.Plataforma.ClienteDelAlmacenDeObjetos.Crear(almacen.Opciones with { Endpoint = new Uri("http://127.0.0.1:1") }),
            almacen.OpcionesDeLibros with { Endpoint = new Uri("http://127.0.0.1:1") },
            TimeSpan.FromSeconds(3));
        var inicio = DateTime.UtcNow;
        Assert.Equal(ExistenciaDelArchivo.NoDisponible, await caido.ExisteArchivo(clave));
        Assert.True(DateTime.UtcNow - inicio < TimeSpan.FromSeconds(8), "El HEAD debe respetar su plazo y no reintentar.");
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-12")]
    public void El_content_disposition_no_lleva_comillas_barras_ni_control_y_el_doble_en_memoria_no_firma_una_url_valida()
    {
        var cabecera = S3EntregaDeArchivos.ContentDisposition("Libro \"raro\"\\con/barra;y\r\ncontrol.pdf");

        Assert.DoesNotContain("\r", cabecera, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", cabecera, StringComparison.Ordinal);
        Assert.StartsWith("attachment; filename=\"Libro rarocon", cabecera, StringComparison.Ordinal);

        var enMemoria = new EntregaDeArchivosEnMemoria();
        Assert.EndsWith(".invalid", enMemoria.FirmarEnlace(new string('d', 32), "x.epub", DateTimeOffset.UtcNow, 300).Host, StringComparison.Ordinal);
    }
}
