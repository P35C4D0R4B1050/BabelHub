using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>RegistrarObra y RegistrarEdicion (T07; CAT-01 a CAT-06, CAT-19, CAT-20, CAT-23): obra con portada y edición, ISBN repetido, licencia, referencias consumidas, ingreso inicial pendiente y auditoría en la misma transacción.</summary>
public sealed class RegistrarObraYEdicionTests
{
    private static string Isbn13(string isbn) => Domain.Catalogo.Isbn.Crear(isbn).Isbn13;

    private static async Task SinCambios(EntornoDelCatalogo entorno, int obras, int referenciasConsumidas = 0)
    {
        await Task.CompletedTask;
        Assert.Equal(obras, entorno.Obras.Todas.Count);
        Assert.Equal(referenciasConsumidas, entorno.Referencias.Todas.Count(r => r.Estado == EstadoDeReferencia.Consumida));
        Assert.Empty(entorno.Ingresos.Todos);
    }

    // ---------- RegistrarObra ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public async Task Una_obra_con_una_edicion_impresa_y_su_isbn_se_registra_audita_y_consume_la_portada()
    {
        var entorno = new EntornoDelCatalogo();
        var entrada = entorno.Obra("Estructuras de datos", entorno.EdicionImpresa());

        var resultado = await entorno.RegistrarObra().Ejecutar(entrada);

        Assert.True(resultado.EsExito);
        var obra = (await entorno.Obras.Obtener(resultado.Valor.ObraId, false))!;
        Assert.Equal("Estructuras de datos", obra.Titulo);
        Assert.Equal(1, resultado.Valor.Version);
        Assert.Equal([obra.Ediciones[0].Id], resultado.Valor.EdicionIds);
        Assert.Equal("9780000000019", obra.Ediciones[0].Isbn.Isbn13);
        Assert.Equal(EstadoDeReferencia.Consumida, entorno.Referencias.Todas.Single().Estado);
        Assert.Equal(["ObraRegistrada", "EdicionRegistrada"], entorno.Auditoria.Solicitudes.Select(s => s.Accion.Identificador).ToList());
        Assert.Equal(obra.Id.ToString("D"), entorno.Auditoria.Solicitudes[0].ObjetoId);
        Assert.Equal("Estructuras de datos", entorno.Auditoria.Solicitudes[0].Detalle["titulo"]);
        Assert.Equal("impresa", entorno.Auditoria.Solicitudes[1].Detalle["formato"]);
        Assert.Empty(entorno.Inventario.SolicitudesDeIngreso);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public async Task Una_obra_sin_ninguna_edicion_se_rechaza_sin_crear_nada()
    {
        var entorno = new EntornoDelCatalogo();

        var resultado = await entorno.RegistrarObra().Ejecutar(entorno.Obra("Sin ediciones"));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Contains(new ErrorDeCampo("ediciones", "obligatorio"), resultado.Error.Errores);
        await SinCambios(entorno, obras: 0);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public async Task La_portada_es_obligatoria_y_dos_ediciones_del_mismo_tipo_se_rechazan()
    {
        var entorno = new EntornoDelCatalogo();

        var sinPortada = await entorno.RegistrarObra().Ejecutar(entorno.Obra(ediciones: entorno.EdicionImpresa()) with { PortadaRef = null });
        var dosImpresas = await entorno.RegistrarObra().Ejecutar(entorno.Obra(ediciones: [entorno.EdicionImpresa(), entorno.EdicionImpresa("9790000000018")]));

        Assert.Contains(new ErrorDeCampo("portadaRef", "obligatorio"), sinPortada.Error!.Errores);
        Assert.Equal(ClavesDelCatalogo.EdicionDuplicadaPorTipo, dosImpresas.Error!.Clave);
        Assert.Equal(CategoriaDeError.Conflicto, dosImpresas.Error.Categoria);
        await SinCambios(entorno, obras: 0);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-02")]
    public async Task Un_isbn_repetido_se_rechaza_indicando_el_titulo_que_ya_lo_usa_y_no_crea_ni_consume_nada()
    {
        var entorno = new EntornoDelCatalogo();
        await entorno.ObraRegistrada("Estructuras de datos", entorno.EdicionImpresa("978-0-00-000001-9"));
        var auditadas = entorno.Auditoria.Solicitudes.Count;
        var consumidas = entorno.Referencias.Todas.Count(r => r.Estado == EstadoDeReferencia.Consumida);

        var repetido = await entorno.RegistrarObra().Ejecutar(entorno.Obra("Otra obra", entorno.EdicionImpresa("978-0-00-000001-9")));

        Assert.Equal(CategoriaDeError.Conflicto, repetido.Error!.Categoria);
        Assert.Equal(ClavesDelCatalogo.IsbnDuplicado, repetido.Error.Clave);
        Assert.Contains(new ErrorDeCampo("tituloExistente", "Estructuras de datos"), repetido.Error.Errores);
        Assert.Single(entorno.Obras.Todas);
        Assert.Equal(auditadas, entorno.Auditoria.Solicitudes.Count);
        Assert.Equal(consumidas, entorno.Referencias.Todas.Count(r => r.Estado == EstadoDeReferencia.Consumida));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-14")]
    public async Task Un_isbn_con_digito_de_control_invalido_se_rechaza_por_formato_antes_de_crear_nada()
    {
        var entorno = new EntornoDelCatalogo();

        var resultado = await entorno.RegistrarObra().Ejecutar(entorno.Obra(ediciones: entorno.EdicionImpresa("978-0-00-000001-1")));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Contains(new ErrorDeCampo("ediciones[0].isbn", "formato-invalido"), resultado.Error.Errores);
        await SinCambios(entorno, obras: 0);
        Assert.Equal(EstadoDeReferencia.Lista, entorno.Referencias.Todas.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-03")]
    public async Task Una_digital_con_licencia_comercial_vencida_se_rechaza_y_con_fecha_futura_titular_territorios_y_archivo_se_acepta()
    {
        var entorno = new EntornoDelCatalogo();
        var pasada = entorno.Licencia(hasta: entorno.Hoy.AddDays(-1));
        var futura = entorno.Licencia(hasta: entorno.Hoy.AddDays(30));

        var rechazada = await entorno.RegistrarObra().Ejecutar(entorno.Obra("Digital", entorno.EdicionDigital(licencia: pasada)));
        await SinCambios(entorno, obras: 0);
        var aceptada = await entorno.RegistrarObra().Ejecutar(entorno.Obra("Digital", entorno.EdicionDigital(licencia: futura)));

        Assert.Equal(ClavesDelCatalogo.LicenciaInvalida, rechazada.Error!.Clave);
        Assert.Equal(CategoriaDeError.ReglaDeNegocio, rechazada.Error.Categoria);
        Assert.Contains(new ErrorDeCampo("ediciones[0].licencia.vigenteHasta", "vigencia-no-posterior-a-hoy"), rechazada.Error.Errores);
        Assert.True(aceptada.EsExito);
        var digital = (await entorno.Obras.Obtener(aceptada.Valor.ObraId, false))!.Ediciones[0];
        Assert.NotNull(digital.Archivo);
        Assert.Equal(entorno.Hoy.AddDays(30), digital.Licencia!.VigenteHasta);
        Assert.Equal(entorno.UsuarioId, digital.Archivo!.SubidoPor);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-13")]
    public async Task Una_digital_sin_titular_se_rechaza_indicando_el_campo_y_una_de_dominio_publico_sin_vigencia_se_acepta()
    {
        var entorno = new EntornoDelCatalogo();
        var sinTitular = entorno.Licencia(titular: null);
        var publica = new LicenciaDeEdicionEntrada(TipoDeLicenciaDeEdicion.DominioPublico, "Dominio público", ["PE"], null, FormatoDeArchivoDigital.Epub);

        var rechazada = await entorno.RegistrarObra().Ejecutar(entorno.Obra("A", entorno.EdicionDigital(licencia: sinTitular)));
        var aceptada = await entorno.RegistrarObra().Ejecutar(entorno.Obra("B", entorno.EdicionDigital(licencia: publica)));

        Assert.Equal(ClavesDelCatalogo.LicenciaInvalida, rechazada.Error!.Clave);
        Assert.Contains(new ErrorDeCampo("ediciones[0].licencia.titular", "obligatorio"), rechazada.Error.Errores);
        Assert.True(aceptada.EsExito);
    }

    [Fact]
    public async Task El_archivo_de_la_digital_debe_ser_del_formato_de_la_licencia_y_el_rechazo_no_deja_nada_consumido()
    {
        var entorno = new EntornoDelCatalogo();
        var pdf = entorno.LibroListo(TipoDeArchivo.Pdf);

        var resultado = await entorno.RegistrarObra().Ejecutar(entorno.Obra("X", entorno.EdicionDigital(archivoRef: pdf)));

        Assert.Equal(ClavesDelCatalogo.ReferenciaDeArchivoInvalida, resultado.Error!.Clave);
        Assert.Equal(CategoriaDeError.ReglaDeNegocio, resultado.Error.Categoria);
        await SinCambios(entorno, obras: 0);
    }

    [Fact]
    public async Task Una_referencia_ajena_vencida_usada_de_otra_finalidad_o_inexistente_responde_lo_mismo()
    {
        var entorno = new EntornoDelCatalogo();
        var ajena = entorno.PortadaLista(usuario: Guid.NewGuid());
        var vencida = entorno.Referencias.SembrarLista(entorno.UsuarioId, FinalidadDeArchivo.Portada, TipoDeArchivo.Png, entorno.Reloj.Ahora.AddHours(-25));
        var comoLibro = entorno.Referencias.SembrarLista(entorno.UsuarioId, FinalidadDeArchivo.Libro, TipoDeArchivo.Epub, entorno.Reloj.Ahora);
        var usada = entorno.PortadaLista();
        var primera = await entorno.RegistrarObra().Ejecutar(entorno.Obra("Primera", entorno.EdicionImpresa()) with { PortadaRef = usada });
        Assert.True(primera.EsExito);

        var casos = new[] { ajena, vencida, comoLibro, usada, Guid.NewGuid() };
        var errores = new List<ErrorDeAplicacion>();
        foreach (var referencia in casos)
        {
            var entrada = entorno.Obra("Otra", entorno.EdicionImpresa("0-306-40615-2")) with { PortadaRef = referencia };
            errores.Add((await entorno.RegistrarObra().Ejecutar(entrada)).Error!);
        }

        Assert.All(errores, e =>
        {
            Assert.Equal(ClavesDelCatalogo.ReferenciaDeArchivoInvalida, e.Clave);
            Assert.Equal([new ErrorDeCampo("portadaRef", "referencia-invalida")], e.Errores);
        });
        Assert.Single(entorno.Obras.Todas);
        Assert.Equal(EstadoDeReferencia.Lista, entorno.Referencias.Todas.Single(r => r.Id == ajena).Estado);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-19")]
    [InlineData(6000)]
    [InlineData(-3)]
    [InlineData(0)]
    public async Task Un_stock_inicial_fuera_de_1_a_5000_se_rechaza_sin_crear_ni_cambiar_nada(int stock)
    {
        var entorno = new EntornoDelCatalogo();

        var resultado = await entorno.RegistrarObra().Ejecutar(entorno.Obra(ediciones: entorno.EdicionImpresa(stock: stock)));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Contains(new ErrorDeCampo("ediciones[0].stockInicial", "fuera-de-rango"), resultado.Error.Errores);
        await SinCambios(entorno, obras: 0);
        Assert.Empty(entorno.Auditoria.Solicitudes);
        Assert.Empty(entorno.Inventario.SolicitudesDeIngreso);
    }

    [Fact]
    public async Task Un_stock_inicial_de_12_deja_una_fila_pendiente_con_la_clave_del_emisor_y_no_llama_al_inventario()
    {
        var entorno = new EntornoDelCatalogo();

        var resultado = await entorno.RegistrarObra().Ejecutar(entorno.Obra(ediciones: entorno.EdicionImpresa(stock: 12)));

        Assert.True(resultado.EsExito);
        var ingreso = entorno.Ingresos.Todos.Single();
        var edicionId = resultado.Valor.EdicionIds.Single();
        Assert.Equal(EstadoDeIngreso.Pendiente, ingreso.Estado);
        Assert.Equal(12, ingreso.Cantidad);
        Assert.Equal(edicionId, ingreso.EdicionId);
        Assert.Equal(entorno.UsuarioId, ingreso.UsuarioId);
        Assert.Equal(1, ingreso.Intentos);
        Assert.Equal(1, entorno.Emisor.Emisiones);
        Assert.Equal(new IngresoDeAlta(ingreso.Id, edicionId, 12, EstadoDeIngresoSolicitado.Pendiente), Assert.Single(resultado.Valor.Ingresos));
        Assert.Empty(entorno.Inventario.SolicitudesDeIngreso);
        Assert.Empty(entorno.Inventario.FichasRegistradas);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-11")]
    [InlineData("encargado")]
    [InlineData("cliente")]
    [InlineData("anonimo")]
    public async Task Quien_no_es_la_administracion_central_no_puede_registrar_una_obra_y_queda_el_acceso_denegado(string quien)
    {
        var actor = quien switch
        {
            "encargado" => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
            "cliente" => ActorDePrueba.DeCliente(),
            _ => ActorDePrueba.Anonimo(),
        };
        var entorno = new EntornoDelCatalogo(actor);

        var resultado = await entorno.RegistrarObra().Ejecutar(entorno.Obra(ediciones: entorno.EdicionImpresa(stock: 6000)) with { PortadaRef = Guid.NewGuid() });

        Assert.Equal(quien == "anonimo" ? CategoriaDeError.NoAutenticado : CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Empty(resultado.Error.Errores);
        Assert.Single(entorno.Denegaciones.Eventos);
        await SinCambios(entorno, obras: 0);
    }

    [Fact]
    public async Task Con_la_auditoria_caida_no_se_crea_nada_y_la_portada_sigue_sin_consumir()
    {
        var entorno = new EntornoDelCatalogo();
        entorno.Auditoria.Caida = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.RegistrarObra().Ejecutar(entorno.Obra(ediciones: entorno.EdicionImpresa(stock: 12))));

        Assert.Empty(entorno.Obras.Todas);
        Assert.Empty(entorno.Ingresos.Todos);
        Assert.Equal(EstadoDeReferencia.Lista, entorno.Referencias.Todas.Single().Estado);
    }

    // ---------- RegistrarEdicion ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public async Task Agregar_una_edicion_digital_a_una_obra_impresa_incrementa_la_version_y_audita()
    {
        var entorno = new EntornoDelCatalogo();
        var obra = await entorno.ObraRegistrada("Base", entorno.EdicionImpresa());
        var auditadas = entorno.Auditoria.Solicitudes.Count;

        var resultado = await entorno.RegistrarEdicion().Ejecutar(new RegistrarEdicionEntrada(obra.Id, obra.Version, entorno.EdicionDigital()));

        Assert.True(resultado.EsExito);
        Assert.Equal(2, resultado.Valor.Version);
        var guardada = (await entorno.Obras.Obtener(obra.Id, false))!;
        Assert.Equal(2, guardada.Ediciones.Count);
        Assert.Equal(2, guardada.Version);
        Assert.Equal("EdicionRegistrada", entorno.Auditoria.Solicitudes[auditadas].Accion.Identificador);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-02")]
    public async Task Una_edicion_con_el_isbn_de_otra_obra_se_rechaza_con_su_titulo_y_no_cambia_la_obra()
    {
        var entorno = new EntornoDelCatalogo();
        await entorno.ObraRegistrada("Dueña del ISBN", entorno.EdicionImpresa("978-0-00-000001-9"));
        var otra = await entorno.ObraRegistrada("Otra", entorno.EdicionImpresa("0-306-40615-2"));

        var resultado = await entorno.RegistrarEdicion().Ejecutar(new RegistrarEdicionEntrada(otra.Id, otra.Version, entorno.EdicionDigital("978-0-00-000001-9")));

        Assert.Equal(ClavesDelCatalogo.IsbnDuplicado, resultado.Error!.Clave);
        Assert.Contains(new ErrorDeCampo("tituloExistente", "Dueña del ISBN"), resultado.Error.Errores);
        var intacta = (await entorno.Obras.Obtener(otra.Id, false))!;
        Assert.Single(intacta.Ediciones);
        Assert.Equal(otra.Version, intacta.Version);
        Assert.Equal(1, entorno.Referencias.Todas.Count(r => r.Estado == EstadoDeReferencia.Lista && r.Finalidad == FinalidadDeArchivo.Libro));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public async Task Una_segunda_edicion_del_mismo_tipo_se_rechaza()
    {
        var entorno = new EntornoDelCatalogo();
        var obra = await entorno.ObraRegistrada("Base", entorno.EdicionImpresa());

        var resultado = await entorno.RegistrarEdicion().Ejecutar(new RegistrarEdicionEntrada(obra.Id, obra.Version, entorno.EdicionImpresa("9790000000018")));

        Assert.Equal(ClavesDelCatalogo.EdicionDuplicadaPorTipo, resultado.Error!.Clave);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-11")]
    [InlineData("encargado")]
    [InlineData("cliente")]
    public async Task Quien_no_es_la_administracion_central_no_puede_agregar_una_edicion(string quien)
    {
        var entorno = new EntornoDelCatalogo(quien == "cliente" ? ActorDePrueba.DeCliente() : ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));

        var resultado = await entorno.RegistrarEdicion().Ejecutar(new RegistrarEdicionEntrada(Guid.NewGuid(), 1, entorno.EdicionImpresa()));

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Single(entorno.Denegaciones.Eventos);
    }

    [Fact]
    public async Task Con_la_auditoria_caida_no_se_agrega_la_edicion_ni_su_ingreso_ni_se_consume_el_archivo()
    {
        var entorno = new EntornoDelCatalogo();
        var obra = await entorno.ObraRegistrada("Base", entorno.EdicionImpresa());
        entorno.Auditoria.Caida = true;
        var nueva = entorno.EdicionDigital();

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.RegistrarEdicion().Ejecutar(new RegistrarEdicionEntrada(obra.Id, obra.Version, nueva)));

        var guardada = (await entorno.Obras.Obtener(obra.Id, false))!;
        Assert.Single(guardada.Ediciones);
        Assert.Equal(obra.Version, guardada.Version);
        Assert.Equal(EstadoDeReferencia.Lista, entorno.Referencias.Todas.Single(r => r.Id == nueva.ArchivoRef).Estado);
    }
}
