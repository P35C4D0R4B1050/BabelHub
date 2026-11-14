using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>La sucursal preferida del cliente entre sesiones (WEB-16, D16, T26): se valida contra la lista de sucursales antes de usarla.</summary>
public sealed class PreferenciaDeSucursalTests
{
    private static readonly IReadOnlyList<SucursalDeLaLista> Lista =
    [
        new("AYA", "Ayacucho"),
        new("LIM", "Lima"),
    ];

    private sealed class ListaFalsa(ListaDeSucursales? lista = null, bool falla = false) : IListaDeSucursales
    {
        public Task<ListaDeSucursales?> Obtener(CancellationToken cancelacion = default) =>
            falla ? throw new HttpRequestException("Inventario caído") : Task.FromResult(lista);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-12")]
    public async Task Un_cliente_que_eligio_Ayacucho_la_conserva_cuando_vuelve()
    {
        var preferencia = new PreferenciaDeSucursal(new ListaFalsa(new ListaDeSucursales(Lista, Actualizada: true)));

        var elegida = await preferencia.Resolver("AYA");

        Assert.Equal("AYA", elegida.CodigoElegido);
        Assert.Equal(2, elegida.Sucursales.Count);
        Assert.True(elegida.ListaDisponible);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("XXX")]
    [InlineData("aya")]
    [InlineData("AYA; DROP TABLE")]
    [InlineData("../../etc")]
    [Trait("Criterio", "AC-WEB-12")]
    public async Task Un_valor_guardado_que_no_esta_en_la_lista_se_ignora(string? guardado)
    {
        var preferencia = new PreferenciaDeSucursal(new ListaFalsa(new ListaDeSucursales(Lista, Actualizada: true)));

        var elegida = await preferencia.Resolver(guardado);

        Assert.Null(elegida.CodigoElegido);
        Assert.Equal(2, elegida.Sucursales.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-12")]
    public async Task Con_la_lista_de_la_ultima_consulta_conocida_se_sigue_validando()
    {
        var preferencia = new PreferenciaDeSucursal(new ListaFalsa(new ListaDeSucursales(Lista, Actualizada: false)));

        var elegida = await preferencia.Resolver("LIM");

        Assert.Equal("LIM", elegida.CodigoElegido);
        Assert.True(elegida.ListaDisponible);
        Assert.False(elegida.ListaActualizada);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Criterio", "AC-WEB-12")]
    public async Task Sin_ninguna_lista_conocida_no_se_ofrece_el_selector_y_no_se_afirma_ninguna_sucursal(bool falla)
    {
        var preferencia = new PreferenciaDeSucursal(new ListaFalsa(lista: null, falla: falla));

        var elegida = await preferencia.Resolver("AYA");

        Assert.Null(elegida.CodigoElegido);
        Assert.Empty(elegida.Sucursales);
        Assert.False(elegida.ListaDisponible);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-12")]
    public async Task Elegir_una_sucursal_de_la_lista_es_valido_y_una_ajena_no()
    {
        var preferencia = new PreferenciaDeSucursal(new ListaFalsa(new ListaDeSucursales(Lista, Actualizada: true)));

        Assert.Equal("LIM", await preferencia.Validar("LIM"));
        Assert.Null(await preferencia.Validar("OTRA"));
        Assert.Null(await preferencia.Validar(null));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-12")]
    public async Task Elegir_con_la_lista_caida_no_guarda_nada()
    {
        var preferencia = new PreferenciaDeSucursal(new ListaFalsa(falla: true));

        Assert.Null(await preferencia.Validar("AYA"));
    }
}
