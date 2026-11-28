using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>La descripción opcional en los casos de uso (T30; CAT-01, CAT-14, CAT-19, AC-CAT-20): registrar y editar con su validación, la auditoría de la edición sin el texto y el rechazo de quien no es la administración central.</summary>
public sealed class DescripcionDeObraDeLaAplicacionTests
{
    private const string Secreto = "texto-de-la-descripcion-que-no-debe-aparecer-en-ningun-registro";

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public async Task Registrar_con_600_se_acepta_con_601_se_rechaza_con_el_campo_y_sin_el_texto_y_sin_descripcion_se_acepta()
    {
        var entorno = new EntornoDelCatalogo();
        var con600 = await entorno.RegistrarObra().Ejecutar(entorno.Obra("Seiscientos", entorno.EdicionImpresa()) with { Descripcion = new string('a', 600) });
        var soloEspacios = await entorno.RegistrarObra().Ejecutar(entorno.Obra("Espacios", entorno.EdicionImpresa("9790000000018")) with { Descripcion = "   " });
        var sin = await entorno.RegistrarObra().Ejecutar(entorno.Obra("Sin", entorno.EdicionImpresa("9790000000025")));
        var con601 = await entorno.RegistrarObra().Ejecutar(entorno.Obra("Seiscientos uno", entorno.EdicionImpresa("9790000000032")) with { Descripcion = Secreto + new string('b', 600) });

        Assert.True(con600.EsExito);
        Assert.Equal(600, (await entorno.Obras.Obtener(con600.Valor.ObraId, false))!.Descripcion!.Length);
        Assert.Null((await entorno.Obras.Obtener(soloEspacios.Valor.ObraId, false))!.Descripcion);
        Assert.Null((await entorno.Obras.Obtener(sin.Valor.ObraId, false))!.Descripcion);
        Assert.Equal(CategoriaDeError.Validacion, con601.Error!.Categoria);
        Assert.Contains(new ErrorDeCampo("descripcion", "demasiado-largo"), con601.Error.Errores!);
        Assert.DoesNotContain(Secreto, string.Join('|', con601.Error.Errores!.Select(e => e.Campo + e.Clave)), StringComparison.Ordinal);
        Assert.Equal(3, entorno.Obras.Todas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public async Task Registrar_con_un_caracter_de_control_se_rechaza_como_formato_invalido_y_los_saltos_de_linea_se_conservan()
    {
        var entorno = new EntornoDelCatalogo();
        var conTab = await entorno.RegistrarObra().Ejecutar(entorno.Obra("Con tab", entorno.EdicionImpresa()) with { Descripcion = "uno\tdos" });
        var conSaltos = await entorno.RegistrarObra().Ejecutar(entorno.Obra("Con saltos", entorno.EdicionImpresa("9790000000018")) with { Descripcion = "uno\r\ndos\n\ntres" });

        Assert.Contains(new ErrorDeCampo("descripcion", "formato-invalido"), conTab.Error!.Errores!);
        Assert.Equal("uno\ndos\n\ntres", (await entorno.Obras.Obtener(conSaltos.Valor.ObraId, false))!.Descripcion);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public async Task Editar_reemplaza_o_quita_la_descripcion_y_la_auditoria_solo_nombra_el_campo()
    {
        var entorno = new EntornoDelCatalogo();
        var obra = await entorno.ObraRegistrada("Base", entorno.EdicionImpresa());
        entorno.Auditoria.Solicitudes.Clear();

        var reemplazo = await entorno.EditarObra().Ejecutar(new EditarObraEntrada(obra.Id, obra.Version, obra.Titulo, obra.Autor, obra.Editorial, obra.Categoria, null, Secreto));
        Assert.True(reemplazo.EsExito);
        Assert.Equal(Secreto, (await entorno.Obras.Obtener(obra.Id, false))!.Descripcion);
        var auditoria = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal("ObraEditada", auditoria.Accion.Identificador);
        Assert.Equal("descripcion", auditoria.Detalle["camposCambiados"]);
        Assert.DoesNotContain(Secreto, string.Join('|', auditoria.Detalle.Values), StringComparison.Ordinal);

        var quitada = await entorno.EditarObra().Ejecutar(new EditarObraEntrada(obra.Id, reemplazo.Valor.Version, obra.Titulo, obra.Autor, obra.Editorial, obra.Categoria, null, ""));
        Assert.True(quitada.EsExito);
        Assert.Null((await entorno.Obras.Obtener(obra.Id, false))!.Descripcion);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public async Task Un_encargado_de_sucursal_que_intenta_editar_la_descripcion_se_rechaza_y_queda_el_acceso_denegado()
    {
        var entorno = new EntornoDelCatalogo(ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));
        var obra = Domain.Catalogo.Obra.Rehidratar(
            Guid.NewGuid(),
            new Domain.Catalogo.DatosDeObra("Base", "A", "E", "C", "Original"),
            new Domain.Catalogo.PortadaDeObra(Domain.Catalogo.ClaveEnAlmacen.Nueva(), Domain.Catalogo.TipoDeArchivo.Jpeg, Domain.Catalogo.HuellaDeArchivo.Desde(new byte[32])),
            1,
            Domain.Catalogo.EstadoDeObra.Activa,
            null,
            EntornoDelCatalogo.InicioDelDia,
            [Domain.Catalogo.Edicion.Impresa(Guid.NewGuid(), Domain.Catalogo.Isbn.Crear("978-0-00-000001-9"), Domain.Catalogo.Precio.Crear(40m))]);
        entorno.Obras.Sembrar(obra);

        var resultado = await entorno.EditarObra().Ejecutar(new EditarObraEntrada(obra.Id, obra.Version, "Base", "A", "E", "C", null, "Cambiada"));

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal("Original", (await entorno.Obras.Obtener(obra.Id, false))!.Descripcion);
        var denegacion = Assert.Single(entorno.Denegaciones.Eventos);
        Assert.Equal("fuera-de-rol", denegacion.Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public async Task La_consulta_publica_y_la_de_administracion_devuelven_la_descripcion_y_nulo_si_falta()
    {
        var entorno = new EntornoDelCatalogo();
        var con = await entorno.RegistrarObra().Ejecutar(entorno.Obra("Con descripción", entorno.EdicionImpresa()) with { Descripcion = "Una sinopsis." });
        var sin = await entorno.RegistrarObra().Ejecutar(entorno.Obra("Sin descripción", entorno.EdicionImpresa("9790000000018")));

        var administracion = entorno.Administracion();
        Assert.Equal("Una sinopsis.", (await administracion.ObtenerObra(con.Valor.ObraId)).Valor.Descripcion);
        Assert.Null((await administracion.ObtenerObra(sin.Valor.ObraId)).Valor.Descripcion);
    }
}
