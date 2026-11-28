using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>EditarObra y EditarEdicion (T08; CAT-11, CAT-19, AC-CAT-08, AC-CAT-12): concurrencia optimista, cambio de precio y de licencia auditados, reemplazo de archivo o portada con descarte del objeto anterior.</summary>
public sealed class EditarObraYEdicionTests
{
    private static async Task<(EntornoDelCatalogo Entorno, Obra Obra)> ConImpresaYDigital()
    {
        var entorno = new EntornoDelCatalogo();
        var obra = await entorno.ObraRegistrada("Base", entorno.EdicionImpresa(precio: 129m), entorno.EdicionDigital());
        entorno.Auditoria.Solicitudes.Clear();
        return (entorno, obra);
    }

    // ---------- Precio y licencia ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-08")]
    public async Task Cambiar_el_precio_de_129_a_139_deja_una_auditoria_con_ambos_precios_y_sube_la_version()
    {
        var (entorno, obra) = await ConImpresaYDigital();
        var impresa = obra.Ediciones.Single(e => !e.EsDigital);

        var resultado = await entorno.EditarEdicion().Ejecutar(new EditarEdicionEntrada(obra.Id, impresa.Id, obra.Version, 139m, null, null));

        Assert.True(resultado.EsExito);
        Assert.Equal(obra.Version + 1, resultado.Valor.Version);
        var guardada = (await entorno.Obras.Obtener(obra.Id, false))!;
        Assert.Equal(139m, guardada.BuscarEdicion(impresa.Id)!.Precio.Valor);
        var precio = entorno.Auditoria.Solicitudes.Single(s => s.Accion.Identificador == "PrecioDeEdicionCambiado");
        Assert.Equal("129.00", precio.Detalle["precioAnterior"]);
        Assert.Equal("139.00", precio.Detalle["precioNuevo"]);
        Assert.Equal("PEN", precio.Detalle["moneda"]);
        Assert.Equal(impresa.Id.ToString("D"), precio.ObjetoId);
        Assert.Equal("edicion", precio.ObjetoTipo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-08")]
    public async Task Editar_con_el_mismo_precio_no_audita_un_cambio_de_precio_pero_deja_la_edicion_editada()
    {
        var (entorno, obra) = await ConImpresaYDigital();
        var impresa = obra.Ediciones.Single(e => !e.EsDigital);

        await entorno.EditarEdicion().Ejecutar(new EditarEdicionEntrada(obra.Id, impresa.Id, obra.Version, 129m, null, null));

        Assert.Equal(["EdicionEditada"], entorno.Auditoria.Solicitudes.Select(s => s.Accion.Identificador).ToList());
        Assert.Equal(string.Empty, entorno.Auditoria.Solicitudes[0].Detalle["camposCambiados"]);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-08")]
    public async Task Cambiar_la_licencia_audita_los_valores_anterior_y_nuevo()
    {
        var (entorno, obra) = await ConImpresaYDigital();
        var digital = obra.Ediciones.Single(e => e.EsDigital);
        var nueva = entorno.Licencia(hasta: entorno.Hoy.AddDays(400), territorios: ["PE", "CL"]);

        var resultado = await entorno.EditarEdicion().Ejecutar(new EditarEdicionEntrada(obra.Id, digital.Id, obra.Version, null, nueva, null));

        Assert.True(resultado.EsExito);
        var auditoria = entorno.Auditoria.Solicitudes.Single(s => s.Accion.Identificador == "LicenciaCambiada");
        Assert.Equal("comercial", auditoria.Detalle["tipoAnterior"]);
        Assert.Equal(entorno.Hoy.AddYears(1).ToString("yyyy-MM-dd"), auditoria.Detalle["vigenciaAnterior"]);
        Assert.Equal(entorno.Hoy.AddDays(400).ToString("yyyy-MM-dd"), auditoria.Detalle["vigenciaNueva"]);
        Assert.Equal("PE", auditoria.Detalle["territoriosAnterior"]);
        Assert.Equal("CL,PE", auditoria.Detalle["territoriosNuevo"]);
        Assert.Equal("licencia", entorno.Auditoria.Solicitudes.Single(s => s.Accion.Identificador == "EdicionEditada").Detalle["camposCambiados"]);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-03")]
    public async Task Una_licencia_nueva_con_vigencia_pasada_se_rechaza_y_no_cambia_nada()
    {
        var (entorno, obra) = await ConImpresaYDigital();
        var digital = obra.Ediciones.Single(e => e.EsDigital);

        var resultado = await entorno.EditarEdicion().Ejecutar(new EditarEdicionEntrada(obra.Id, digital.Id, obra.Version, 10m, entorno.Licencia(hasta: entorno.Hoy.AddDays(-1)), null));

        Assert.Equal(ClavesDelCatalogo.LicenciaInvalida, resultado.Error!.Clave);
        Assert.Equal(obra.Version, (await entorno.Obras.Obtener(obra.Id, false))!.Version);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-04")]
    public async Task Editar_el_precio_de_una_digital_con_licencia_vencida_no_falla()
    {
        var entorno = new EntornoDelCatalogo();
        var obra = await entorno.ObraRegistrada("Vence", entorno.EdicionDigital(licencia: entorno.Licencia(hasta: entorno.Hoy.AddDays(5))));
        entorno.Reloj.Ahora = entorno.Reloj.Ahora.AddDays(30);

        var resultado = await entorno.EditarEdicion().Ejecutar(new EditarEdicionEntrada(obra.Id, obra.Ediciones[0].Id, obra.Version, 59m, null, null));

        Assert.True(resultado.EsExito);
    }

    // ---------- Archivo y portada ----------

    [Fact]
    public async Task Reemplazar_el_libro_consume_la_referencia_encola_el_objeto_anterior_y_audita_tipo_tamano_y_huellas_cortas()
    {
        var (entorno, obra) = await ConImpresaYDigital();
        var digital = obra.Ediciones.Single(e => e.EsDigital);
        var anterior = digital.Archivo!;
        var nuevo = entorno.LibroListo();

        var resultado = await entorno.EditarEdicion().Ejecutar(new EditarEdicionEntrada(obra.Id, digital.Id, obra.Version, null, null, nuevo));

        Assert.True(resultado.EsExito);
        var guardada = (await entorno.Obras.Obtener(obra.Id, false))!.BuscarEdicion(digital.Id)!;
        Assert.NotEqual(anterior.ClaveDelAlmacen, guardada.Archivo!.ClaveDelAlmacen);
        Assert.Equal(EstadoDeReferencia.Consumida, entorno.Referencias.Todas.Single(r => r.Id == nuevo).Estado);
        var descarte = entorno.Referencias.Todas.Single(r => r.ClaveDelAlmacen == anterior.ClaveDelAlmacen);
        Assert.Equal(EstadoDeReferencia.Descarte, descarte.Estado);
        var auditoria = entorno.Auditoria.Solicitudes.Single(s => s.Accion.Identificador == "ArchivoDeEdicionReemplazado");
        Assert.Equal("epub", auditoria.Detalle["tipo"]);
        Assert.Equal(guardada.Archivo.Huella.Corta, auditoria.Detalle["huellaCorta"]);
        Assert.Equal(anterior.Huella.Corta, auditoria.Detalle["huellaCortaAnterior"]);
        Assert.DoesNotContain(auditoria.Detalle.Values, v => v is not null && v.Contains(anterior.ClaveDelAlmacen, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_libro_del_formato_equivocado_o_una_referencia_ajena_se_rechaza_y_el_archivo_anterior_sigue()
    {
        var (entorno, obra) = await ConImpresaYDigital();
        var digital = obra.Ediciones.Single(e => e.EsDigital);
        var pdf = entorno.LibroListo(TipoDeArchivo.Pdf);
        var ajeno = entorno.LibroListo(usuario: Guid.NewGuid());

        var formato = await entorno.EditarEdicion().Ejecutar(new EditarEdicionEntrada(obra.Id, digital.Id, obra.Version, null, null, pdf));
        var ajena = await entorno.EditarEdicion().Ejecutar(new EditarEdicionEntrada(obra.Id, digital.Id, obra.Version, null, null, ajeno));

        Assert.Equal(ClavesDelCatalogo.ReferenciaDeArchivoInvalida, formato.Error!.Clave);
        Assert.Equal(ClavesDelCatalogo.ReferenciaDeArchivoInvalida, ajena.Error!.Clave);
        Assert.Equal(digital.Archivo!.ClaveDelAlmacen, (await entorno.Obras.Obtener(obra.Id, false))!.BuscarEdicion(digital.Id)!.Archivo!.ClaveDelAlmacen);
        Assert.DoesNotContain(entorno.Referencias.Todas, r => r.Estado == EstadoDeReferencia.Descarte);
    }

    [Fact]
    public async Task Una_impresa_no_acepta_licencia_ni_libro()
    {
        var (entorno, obra) = await ConImpresaYDigital();
        var impresa = obra.Ediciones.Single(e => !e.EsDigital);

        var resultado = await entorno.EditarEdicion().Ejecutar(new EditarEdicionEntrada(obra.Id, impresa.Id, obra.Version, null, entorno.Licencia(), null));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Contains(new ErrorDeCampo("edicionId", ClavesDelCatalogo.SoloEdicionDigital), resultado.Error.Errores);
    }

    [Fact]
    public async Task Reemplazar_la_portada_consume_la_referencia_encola_la_anterior_y_audita_con_la_obra_como_objeto()
    {
        var (entorno, obra) = await ConImpresaYDigital();
        var anterior = obra.Portada;
        var nueva = entorno.PortadaLista();

        var resultado = await entorno.EditarObra().Ejecutar(new EditarObraEntrada(obra.Id, obra.Version, obra.Titulo, obra.Autor, obra.Editorial, obra.Categoria, nueva));

        Assert.True(resultado.EsExito);
        var guardada = (await entorno.Obras.Obtener(obra.Id, false))!;
        Assert.NotEqual(anterior.ClaveDelAlmacen, guardada.Portada.ClaveDelAlmacen);
        Assert.Equal(EstadoDeReferencia.Descarte, entorno.Referencias.Todas.Single(r => r.ClaveDelAlmacen == anterior.ClaveDelAlmacen).Estado);
        var auditoria = entorno.Auditoria.Solicitudes.Single(s => s.Accion.Identificador == "PortadaDeEdicionReemplazada");
        Assert.Equal("obra", auditoria.ObjetoTipo);
        Assert.Equal(anterior.Huella.Corta, auditoria.Detalle["huellaCortaAnterior"]);
        Assert.Equal(guardada.Portada.Huella.Corta, auditoria.Detalle["huellaCorta"]);
        Assert.DoesNotContain(entorno.Auditoria.Solicitudes, s => s.Accion.Identificador == "ObraEditada");
    }

    // ---------- EditarObra ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-08")]
    public async Task Editar_los_datos_de_la_obra_audita_los_campos_cambiados_sin_sus_valores_y_sube_la_version()
    {
        var (entorno, obra) = await ConImpresaYDigital();

        var resultado = await entorno.EditarObra().Ejecutar(new EditarObraEntrada(obra.Id, obra.Version, "Base II", obra.Autor, "Otra editorial", obra.Categoria, null));

        Assert.True(resultado.EsExito);
        Assert.Equal(obra.Version + 1, resultado.Valor.Version);
        var auditoria = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal("ObraEditada", auditoria.Accion.Identificador);
        Assert.Equal("titulo,editorial", auditoria.Detalle["camposCambiados"]);
        Assert.Equal("Base II", (await entorno.Obras.Obtener(obra.Id, false))!.Titulo);
    }

    // ---------- Concurrencia, no encontrado, rol, auditoría ----------

    [Fact]
    public async Task La_segunda_de_dos_ediciones_con_la_misma_version_recibe_conflicto()
    {
        var (entorno, obra) = await ConImpresaYDigital();
        var impresa = obra.Ediciones.Single(e => !e.EsDigital);

        var primera = await entorno.EditarEdicion().Ejecutar(new EditarEdicionEntrada(obra.Id, impresa.Id, obra.Version, 139m, null, null));
        var segunda = await entorno.EditarEdicion().Ejecutar(new EditarEdicionEntrada(obra.Id, impresa.Id, obra.Version, 149m, null, null));
        var obraEditada = await entorno.EditarObra().Ejecutar(new EditarObraEntrada(obra.Id, obra.Version, "Otro", obra.Autor, obra.Editorial, obra.Categoria, null));

        Assert.True(primera.EsExito);
        Assert.Equal(ClavesDelCatalogo.ConflictoDeVersion, segunda.Error!.Clave);
        Assert.Equal(CategoriaDeError.Conflicto, segunda.Error.Categoria);
        Assert.Equal(ClavesDelCatalogo.ConflictoDeVersion, obraEditada.Error!.Clave);
        Assert.Equal(139m, (await entorno.Obras.Obtener(obra.Id, false))!.BuscarEdicion(impresa.Id)!.Precio.Valor);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-11")]
    [InlineData("encargado")]
    [InlineData("cliente")]
    [InlineData("anonimo")]
    public async Task Quien_no_es_la_administracion_central_no_edita_y_queda_el_acceso_denegado(string quien)
    {
        var admin = new EntornoDelCatalogo();
        var obra = await admin.ObraRegistrada("Base", admin.EdicionImpresa());
        var actor = quien switch
        {
            "encargado" => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
            "cliente" => ActorDePrueba.DeCliente(),
            _ => ActorDePrueba.Anonimo(),
        };
        var otro = new EntornoDelCatalogo(actor);
        otro.Obras.Sembrar(obra);

        var precio = await otro.EditarEdicion().Ejecutar(new EditarEdicionEntrada(obra.Id, obra.Ediciones[0].Id, obra.Version, 1m, null, null));
        var datos = await otro.EditarObra().Ejecutar(new EditarObraEntrada(obra.Id, obra.Version, "Hack", "A", "E", "C", null));

        Assert.Equal(quien == "anonimo" ? CategoriaDeError.NoAutenticado : CategoriaDeError.Prohibido, precio.Error!.Categoria);
        Assert.Equal(precio.Error.Categoria, datos.Error!.Categoria);
        Assert.Equal(2, otro.Denegaciones.Eventos.Count);
        Assert.Equal(129m, (await otro.Obras.Obtener(obra.Id, false))!.Ediciones[0].Precio.Valor);
        Assert.Empty(otro.Auditoria.Solicitudes);
    }

    [Fact]
    public async Task Con_la_auditoria_caida_no_se_aplica_ni_el_precio_ni_el_reemplazo_del_libro()
    {
        var (entorno, obra) = await ConImpresaYDigital();
        var impresa = obra.Ediciones.Single(e => !e.EsDigital);
        var digital = obra.Ediciones.Single(e => e.EsDigital);
        var nuevo = entorno.LibroListo();
        entorno.Auditoria.Caida = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.EditarEdicion().Ejecutar(new EditarEdicionEntrada(obra.Id, impresa.Id, obra.Version, 139m, null, null)));
        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.EditarEdicion().Ejecutar(new EditarEdicionEntrada(obra.Id, digital.Id, obra.Version, null, null, nuevo)));
        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.EditarObra().Ejecutar(new EditarObraEntrada(obra.Id, obra.Version, "Otro", obra.Autor, obra.Editorial, obra.Categoria, entorno.PortadaLista())));

        var intacta = (await entorno.Obras.Obtener(obra.Id, false))!;
        Assert.Equal(obra.Version, intacta.Version);
        Assert.Equal(129m, intacta.BuscarEdicion(impresa.Id)!.Precio.Valor);
        Assert.Equal(digital.Archivo!.ClaveDelAlmacen, intacta.BuscarEdicion(digital.Id)!.Archivo!.ClaveDelAlmacen);
        Assert.Equal(EstadoDeReferencia.Lista, entorno.Referencias.Todas.Single(r => r.Id == nuevo).Estado);
        Assert.DoesNotContain(entorno.Referencias.Todas, r => r.Estado == EstadoDeReferencia.Descarte);
    }
}
