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
    public void Un_lote_de_ediciones_con_el_mismo_identificador_dos_veces_se_rechaza()
    {
        var id = Guid.NewGuid();

        Assert.False(ValidadorDeEdicionesPorIdentificador.Validar([id, id]).EsValido);
        Assert.True(ValidadorDeEdicionesPorIdentificador.Validar([id, Guid.NewGuid()]).EsValido);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Rehidratar_una_referencia_exige_coherencia_entre_su_estado_y_sus_campos()
    {
        var clave = ClaveEnAlmacen.Nueva();

        ReferenciaDeArchivo Rehidratar(EstadoDeReferencia estado, TipoDeArchivo? tipo, long? tamano, HuellaDeArchivo? huella, DateTimeOffset? consumidoEn) =>
            ReferenciaDeArchivo.Rehidratar(Guid.NewGuid(), Ana, FinalidadDeArchivo.Portada, clave, estado, tipo, tamano, huella, Ahora, Ahora.AddHours(24), consumidoEn);

        Assert.Throws<ArgumentException>(() => Rehidratar(EstadoDeReferencia.Lista, null, null, null, null));
        Assert.Throws<ArgumentException>(() => Rehidratar(EstadoDeReferencia.Lista, TipoDeArchivo.Pdf, 10, Huella(), null));
        Assert.Throws<ArgumentException>(() => Rehidratar(EstadoDeReferencia.Consumida, TipoDeArchivo.Png, 10, Huella(), null));
        Assert.Throws<ArgumentException>(() => Rehidratar(EstadoDeReferencia.Lista, TipoDeArchivo.Png, 10, Huella(), Ahora));
        Assert.Equal(EstadoDeReferencia.Subiendo, Rehidratar(EstadoDeReferencia.Subiendo, null, null, null, null).Estado);
        Assert.Equal(EstadoDeReferencia.Consumida, Rehidratar(EstadoDeReferencia.Consumida, TipoDeArchivo.Png, 10, Huella(), Ahora).Estado);
        Assert.Equal(EstadoDeReferencia.Descarte, Rehidratar(EstadoDeReferencia.Descarte, null, null, null, Ahora).Estado);
        // Una lista rehidratada y válida no revienta al consumirla.
        Assert.Equal(ResultadoDeConsumo.Consumida, Rehidratar(EstadoDeReferencia.Lista, TipoDeArchivo.Png, 10, Huella(), null).Consumir(Ana, FinalidadDeArchivo.Portada, null, Ahora.AddHours(1)));
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

    [Fact]
    public async Task El_retiro_de_carritos_recibe_las_ediciones_de_baja_y_si_falla_lo_propaga()
    {
        var retiro = new RetiroDeCarritosEspia();
        var ediciones = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await retiro.Retirar(ediciones);
        retiro.Falla = true;

        Assert.Equal(ediciones, Assert.Single(retiro.Retiros));
        await Assert.ThrowsAsync<InvalidOperationException>(() => retiro.Retirar(ediciones));
    }
}
