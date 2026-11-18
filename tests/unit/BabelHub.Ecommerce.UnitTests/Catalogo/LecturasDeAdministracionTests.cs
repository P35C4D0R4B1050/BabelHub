using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>Las lecturas de la administración (T12; CAT-14): solo la administración central, con todas las ediciones y lo que el público no ve, sin la clave del almacén.</summary>
public sealed class LecturasDeAdministracionTests
{
    [Fact]
    public async Task La_administracion_central_ve_las_ediciones_con_su_licencia_estado_y_version_pero_sin_la_clave_del_almacen()
    {
        var e = new EntornoDelCatalogo();
        var obra = await e.ObraRegistrada("Completa", e.EdicionImpresa(), e.EdicionDigital());
        var digital = obra.Ediciones.Single(x => x.EsDigital);

        var ficha = await e.Administracion().ObtenerObra(obra.Id);
        var pagina = await e.Administracion().ListarObras(new ListarObrasDeAdministracionEntrada());

        Assert.Equal(obra.Version, ficha.Valor.Version);
        var edicion = ficha.Valor.Ediciones.Single(x => x.Formato == FormatoDeEdicion.Digital);
        Assert.Equal("Editorial Andina", edicion.Licencia!.Titular);
        Assert.Equal(digital.Archivo!.Huella.Corta, edicion.Archivo!.HuellaCorta);
        Assert.Equal(digital.Isbn.TextoIngresado, edicion.IsbnIngresado);
        Assert.DoesNotContain(digital.Archivo.ClaveDelAlmacen, ficha.Valor.ToString() + edicion + edicion.Archivo, StringComparison.Ordinal);
        Assert.Single(pagina.Valor.Obras);
        Assert.Equal(1, pagina.Valor.Total);
    }

    [Fact]
    public async Task La_lista_oculta_las_dadas_de_baja_salvo_que_se_pidan_y_busca_por_texto()
    {
        var e = new EntornoDelCatalogo();
        var obra = await e.ObraRegistrada("Visible", e.EdicionImpresa());
        var otra = await e.ObraRegistrada("Dada de baja", e.EdicionImpresa("0-306-40615-2"));
        await e.DarDeBajaObra().Ejecutar(new DarDeBajaObraEntrada(otra.Id, otra.Version));

        var visibles = await e.Administracion().ListarObras(new ListarObrasDeAdministracionEntrada());
        var todas = await e.Administracion().ListarObras(new ListarObrasDeAdministracionEntrada(IncluirDadasDeBaja: true));
        var texto = await e.Administracion().ListarObras(new ListarObrasDeAdministracionEntrada("dada", true));

        Assert.Equal([obra.Id], visibles.Valor.Obras.Select(o => o.ObraId).ToList());
        Assert.Equal(2, todas.Valor.Total);
        Assert.True(todas.Valor.Obras.Single(o => o.ObraId == otra.Id).DadaDeBaja);
        Assert.Equal([otra.Id], texto.Valor.Obras.Select(o => o.ObraId).ToList());
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-11")]
    [InlineData("encargado")]
    [InlineData("cliente")]
    [InlineData("anonimo")]
    public async Task Quien_no_es_la_administracion_central_no_lee_la_administracion_y_queda_el_acceso_denegado(string quien)
    {
        var admin = new EntornoDelCatalogo();
        var obra = await admin.ObraRegistrada("Privada", admin.EdicionImpresa());
        var e = new EntornoDelCatalogo(quien switch
        {
            "encargado" => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
            "cliente" => ActorDePrueba.DeCliente(),
            _ => ActorDePrueba.Anonimo(),
        });
        e.Obras.Sembrar(obra);

        var lista = await e.Administracion().ListarObras(new ListarObrasDeAdministracionEntrada());
        var ficha = await e.Administracion().ObtenerObra(obra.Id);

        Assert.NotNull(lista.Error);
        Assert.NotNull(ficha.Error);
        Assert.Equal(2, e.Denegaciones.Eventos.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-11")]
    public async Task Una_obra_inexistente_o_un_identificador_vacio_se_rechazan()
    {
        var e = new EntornoDelCatalogo();

        var inexistente = await e.Administracion().ObtenerObra(Guid.NewGuid());
        var vacio = await e.Administracion().ObtenerObra(Guid.Empty);
        var invalida = await e.Administracion().ListarObras(new ListarObrasDeAdministracionEntrada(Tamano: 99));

        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, vacio.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, invalida.Error!.Categoria);
    }
}
