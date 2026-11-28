using System.Security.Cryptography;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>Lo que dejaron las revisiones de seguridad y de arquitectura de ECO-2a: texto de entrada sin caracteres de control, lotes sin repetidos, filas rehidratadas coherentes, <c>ToString</c> sin claves ni textos libres y el retiro de carritos.</summary>
public sealed class CorreccionesDeLaRevisionDeEco2aTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(-5));
    private static readonly Guid Ana = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static HuellaDeArchivo Huella() => HuellaDeArchivo.Desde(SHA256.HashData("x"u8));

    [Theory]
    [Trait("Criterio", "AC-CAT-06")]
    [InlineData("a\0b")]
    [InlineData("ab\u0007")]
    [InlineData("hola\nmundo")]
    public void La_busqueda_publica_rechaza_los_caracteres_de_control_en_q(string texto)
    {
        var resultado = new ValidadorDeBusquedaDeObras().Validar(new BusquedaDeObras(texto));

        Assert.Equal([new Application.Plataforma.ErrorDeCampo("q", "formato-invalido")], resultado.Errores);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-13")]
    public void El_titular_de_una_licencia_no_lleva_caracteres_de_control()
    {
        var licencia = new LicenciaDeEdicionEntrada(TipoDeLicenciaDeEdicion.Comercial, "Editorial\nAndina", ["PE"], new DateOnly(2027, 1, 1), FormatoDeArchivoDigital.Epub);
        var edicion = new NuevaEdicionEntrada(FormatoDeEdicion.Digital, "9790000000018", 10m, licencia, Guid.NewGuid(), null);

        var resultado = new ValidadorDeRegistrarObra().Validar(new RegistrarObraEntrada("T", "A", "E", "C", Guid.NewGuid(), [edicion]));

        Assert.Equal([new Application.Plataforma.ErrorDeCampo("ediciones[0].licencia.titular", "formato-invalido")], resultado.Errores);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Los_modelos_con_claves_del_almacen_o_texto_libre_no_los_imprimen()
    {
        var clave = ClaveEnAlmacen.Nueva();
        var edicion = Guid.NewGuid();
        var referencia = Guid.NewGuid();
        var archivo = new ArchivoDeEdicion(clave, TipoDeArchivo.Epub, 10, Huella(), Ahora, Ana);
        var portada = new PortadaDeObra(clave, TipoDeArchivo.Png, Huella());
        var cambios = new CambiosDeEdicion(Edicion.Impresa(edicion, Isbn.Crear("9780000000019"), Precio.Crear(1m)), null, null, archivo);
        var textos = new object[]
        {
            archivo,
            portada,
            cambios,
            new SolicitudDeIngresoAlInventario(edicion, referencia, 5, "GR-SECRETA-001", "nota privada"),
            new IngresoDelInventario(Guid.NewGuid(), edicion, "LIMA", 5, "GR-SECRETA-001", "nota privada", Ana, Ahora),
            new IngresoDeLaLista(referencia, null, 5, "GR-SECRETA-001", Ana, Ahora, null),
            new ArchivoSubido(referencia, FinalidadDeLaSubida.Libro, "epub", 10, new string('a', 64), Ahora),
        }.Select(o => o.ToString()!).ToList();

        Assert.All(textos, t =>
        {
            Assert.DoesNotContain(clave, t, StringComparison.Ordinal);
            Assert.DoesNotContain("SECRETA", t, StringComparison.Ordinal);
            Assert.DoesNotContain("privada", t, StringComparison.Ordinal);
            Assert.DoesNotContain(referencia.ToString(), t, StringComparison.Ordinal);
            Assert.DoesNotContain(Ana.ToString(), t, StringComparison.Ordinal);
        });
    }
}
