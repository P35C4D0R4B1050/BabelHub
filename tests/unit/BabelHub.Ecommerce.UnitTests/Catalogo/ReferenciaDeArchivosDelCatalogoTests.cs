using System.Security.Cryptography;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>La referencia del archivo de una edición digital que Catálogo entrega solo a Biblioteca (D13, D17; BIB-09, AC-BIB-08): también de una edición con la licencia vencida; «no encontrada» si no existe o no es digital.</summary>
public sealed class ReferenciaDeArchivosDelCatalogoTests
{
    private static readonly Guid Ana = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static string Isbn13(int n)
    {
        var doce = "978" + n.ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        var suma = doce.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return doce + ((10 - (suma % 10)) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static HuellaDeArchivo Huella() => HuellaDeArchivo.Desde(SHA256.HashData(Guid.NewGuid().ToByteArray()));

    private static (EntornoDelCatalogo Entorno, Obra Obra, string Clave) Sembrar(TipoDeArchivo tipo, DateOnly? vigenteHasta = null)
    {
        var e = new EntornoDelCatalogo();
        var clave = ClaveEnAlmacen.Nueva();
        var licencia = LicenciaDigital.Rehidratar(TipoDeLicencia.Comercial, "Editorial", ["PE"], vigenteHasta ?? e.Hoy.AddYears(1), tipo == TipoDeArchivo.Pdf ? FormatoDigital.Pdf : FormatoDigital.Epub);
        var digital = Edicion.Digital(Guid.NewGuid(), Isbn.Crear(Isbn13(2001)), Precio.Crear(30m), licencia, new ArchivoDeEdicion(clave, tipo, 2048, Huella(), e.Reloj.Ahora, Ana));
        var impresa = Edicion.Impresa(Guid.NewGuid(), Isbn.Crear(Isbn13(2002)), Precio.Crear(40m));
        var obra = Obra.Registrar(Guid.NewGuid(), new DatosDeObra("Libro", "Autora", "Editorial", "Novela"), new PortadaDeObra(ClaveEnAlmacen.Nueva(), TipoDeArchivo.Jpeg, Huella()), [impresa, digital], e.Reloj.Ahora).Valor;
        e.Obras.Sembrar(obra);
        return (e, obra, clave);
    }

    [Theory]
    [InlineData(TipoDeArchivo.Epub, FormatoDeArchivoDigital.Epub)]
    [InlineData(TipoDeArchivo.Pdf, FormatoDeArchivoDigital.Pdf)]
    [Trait("Criterio", "AC-BIB-12")]
    public async Task Entrega_la_clave_el_formato_la_huella_y_el_tamano_de_una_digital(TipoDeArchivo tipo, FormatoDeArchivoDigital formato)
    {
        var (entorno, obra, clave) = Sembrar(tipo);
        var digital = obra.Ediciones.Single(x => x.EsDigital);

        var resultado = await new ReferenciaDeArchivosDelCatalogoDeLaAplicacion(entorno.Lecturas).ObtenerDeEdicion(digital.Id);

        Assert.True(resultado.EsExito);
        Assert.Equal(clave, resultado.Valor.ClaveEnAlmacen);
        Assert.Equal(formato, resultado.Valor.Formato);
        Assert.Equal(digital.Archivo!.Huella.Hex, resultado.Valor.HuellaSha256);
        Assert.Equal(2048, resultado.Valor.TamanoEnBytes);
        Assert.DoesNotContain(clave, resultado.Valor.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-08")]
    public async Task Una_edicion_con_la_licencia_vencida_conserva_su_referencia_para_quienes_ya_la_compraron()
    {
        var (entorno, obra, _) = Sembrar(TipoDeArchivo.Epub, vigenteHasta: new DateOnly(2020, 1, 1));
        var digital = obra.Ediciones.Single(x => x.EsDigital);

        var resultado = await new ReferenciaDeArchivosDelCatalogoDeLaAplicacion(entorno.Lecturas).ObtenerDeEdicion(digital.Id);

        Assert.True(resultado.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-11")]
    public async Task Una_impresa_una_inexistente_o_vacia_son_no_encontradas()
    {
        var (entorno, obra, _) = Sembrar(TipoDeArchivo.Epub);
        var adaptador = new ReferenciaDeArchivosDelCatalogoDeLaAplicacion(entorno.Lecturas);

        foreach (var id in new[] { obra.Ediciones.Single(x => !x.EsDigital).Id, Guid.NewGuid(), Guid.Empty })
        {
            var resultado = await adaptador.ObtenerDeEdicion(id);

            Assert.Equal(CategoriaDeError.NoEncontrado, resultado.Error!.Categoria);
        }
    }
}
