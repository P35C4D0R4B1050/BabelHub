using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>La autorización de la administración central y la validación de entrada de cada caso de uso (T06; AC-CAT-11, AC-CAT-19, CAT-14, CAT-23).</summary>
public sealed class AutorizacionYValidacionTests
{
    private static (AutorizadorDelCatalogo Autorizador, DenegacionesDelCatalogoEspia Denegaciones) Crear(ActorDePrueba actor)
    {
        var denegaciones = new DenegacionesDelCatalogoEspia();
        return (new AutorizadorDelCatalogo(actor, denegaciones), denegaciones);
    }

    // ---------- Autorización ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-11")]
    public async Task La_administracion_central_pasa_y_no_deja_evento()
    {
        var admin = Guid.NewGuid();
        var (autorizador, denegaciones) = Crear(ActorDePrueba.DeAdministradorCentral(admin));

        var error = await autorizador.ExigirAdministracionCentral(default);

        Assert.Null(error);
        Assert.Equal(admin, autorizador.UsuarioId);
        Assert.Empty(denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-11")]
    public async Task Un_encargado_de_sucursal_se_rechaza_como_prohibido_y_queda_el_acceso_denegado()
    {
        var (autorizador, denegaciones) = Crear(ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));

        var error = await autorizador.ExigirAdministracionCentral(default);

        Assert.Equal(new ErrorDeAplicacion(CategoriaDeError.Prohibido, AutorizadorDelCatalogo.ClaveProhibido), error);
        var evento = Assert.Single(denegaciones.Eventos);
        Assert.Equal("fuera-de-rol", evento.Motivo);
        Assert.Equal("ECO-PROHIBIDO", evento.CodigoDeError);
        Assert.Null(autorizador.UsuarioId);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-19")]
    [InlineData("cliente")]
    [InlineData("personal-de-logistica")]
    [InlineData("sistema")]
    [InlineData("integracion")]
    public async Task Ningun_otro_actor_con_contexto_es_la_administracion_central(string actor)
    {
        var (autorizador, denegaciones) = Crear(actor switch
        {
            "cliente" => ActorDePrueba.DeCliente(),
            "personal-de-logistica" => ActorDePrueba.DePersonalDeLogistica(),
            "sistema" => ActorDePrueba.DelSistema(),
            _ => ActorDePrueba.DeIntegracion("lectura-de-disponibilidad"),
        });

        var error = await autorizador.ExigirAdministracionCentral(default);

        Assert.Equal(CategoriaDeError.Prohibido, error!.Categoria);
        Assert.Single(denegaciones.Eventos);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-11")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sin_sesion_o_con_un_visitante_es_no_autenticado_y_nunca_se_concede_por_omision(bool visitante)
    {
        var (autorizador, denegaciones) = Crear(visitante ? ActorDePrueba.Anonimo() : ActorDePrueba.SinContexto());

        var error = await autorizador.ExigirAdministracionCentral(default);

        Assert.Equal(new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, AutorizadorDelCatalogo.ClaveNoAutenticado), error);
        var evento = Assert.Single(denegaciones.Eventos);
        Assert.Equal("sin-sesion", evento.Motivo);
        Assert.Equal("ECO-NO-AUTENTICADO", evento.CodigoDeError);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-19")]
    public async Task Un_recurso_ajeno_se_rechaza_como_prohibido_con_el_motivo_recurso_ajeno()
    {
        var (autorizador, denegaciones) = Crear(ActorDePrueba.DeAdministradorCentral());

        var error = await autorizador.RechazarRecursoAjeno(default);

        Assert.Equal(CategoriaDeError.Prohibido, error.Categoria);
        Assert.Equal("recurso-ajeno", Assert.Single(denegaciones.Eventos).Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-11")]
    public void El_rol_de_la_aplicacion_es_el_de_la_administracion_central()
    {
        Assert.Equal("administrador-central", RolesDelCatalogo.AdministradorCentral);
        Assert.True(RolesDelCatalogo.EsAdministradorCentral(ActorDePrueba.DeAdministradorCentral().Actual));
        Assert.False(RolesDelCatalogo.EsAdministradorCentral(null));
    }

    // ---------- Validación ----------

    private static LicenciaDeEdicionEntrada Licencia(string? titular = "Editorial Andina", IReadOnlyList<string>? territorios = null, DateOnly? hasta = null, TipoDeLicenciaDeEdicion tipo = TipoDeLicenciaDeEdicion.Comercial) =>
        new(tipo, titular, territorios ?? ["PE"], hasta ?? new DateOnly(2027, 1, 1), FormatoDeArchivoDigital.Epub);

    private static NuevaEdicionEntrada Impresa(string? isbn = "978-0-00-000001-9", decimal precio = 129m, int? stock = null) =>
        new(FormatoDeEdicion.Impresa, isbn, precio, null, null, stock);

    private static NuevaEdicionEntrada Digital(string? isbn = "9790000000018", decimal precio = 49m, LicenciaDeEdicionEntrada? licencia = null, Guid? archivo = null) =>
        new(FormatoDeEdicion.Digital, isbn, precio, licencia ?? Licencia(), archivo ?? Guid.NewGuid(), null);

    private static RegistrarObraEntrada Obra(params NuevaEdicionEntrada[] ediciones) =>
        new("Estructuras de datos", "Ana Quispe", "Editorial Andina", "Técnico", Guid.NewGuid(), ediciones);

    private static IReadOnlyList<string> Campos(ResultadoDeValidacion resultado) => resultado.Errores.Select(e => e.Campo).ToList();

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public void Una_obra_con_una_edicion_impresa_valida_pasa()
    {
        Assert.True(new ValidadorDeRegistrarObra().Validar(Obra(Impresa())).EsValido);
        Assert.True(new ValidadorDeRegistrarObra().Validar(Obra(Impresa(stock: 12), Digital())).EsValido);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public void Una_obra_sin_ediciones_o_con_mas_de_dos_se_rechaza()
    {
        var sin = new ValidadorDeRegistrarObra().Validar(Obra());
        var nulo = new ValidadorDeRegistrarObra().Validar(Obra() with { Ediciones = null });
        var tres = new ValidadorDeRegistrarObra().Validar(Obra(Impresa(), Digital(), Impresa("0-306-40615-2")));

        Assert.Equal(["ediciones"], Campos(sin));
        Assert.Equal(["ediciones"], Campos(nulo));
        Assert.Equal(["ediciones"], Campos(tres));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public void Los_textos_de_la_obra_son_obligatorios_tienen_tope_y_no_llevan_caracteres_de_control_y_la_portada_es_obligatoria()
    {
        var entrada = new RegistrarObraEntrada("", new string('a', 201), "Ed\u0007", new string('c', 101), null, [Impresa()]);

        var resultado = new ValidadorDeRegistrarObra().Validar(entrada);

        Assert.Equal(["titulo", "autor", "editorial", "categoria", "portadaRef"], Campos(resultado));
        Assert.Equal(["obligatorio", "demasiado-largo", "formato-invalido", "demasiado-largo", "obligatorio"], resultado.Errores.Select(e => e.Clave).ToList());
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-14")]
    [InlineData("978-0-00-000001-1")]
    [InlineData("1234")]
    [InlineData("978-0-00-00000A-9")]
    public void Un_isbn_con_digito_de_control_invalido_se_rechaza_por_formato(string isbn)
    {
        var resultado = new ValidadorDeRegistrarObra().Validar(Obra(Impresa(isbn)));

        Assert.Equal([new ErrorDeCampo("ediciones[0].isbn", "formato-invalido")], resultado.Errores);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-14")]
    public void Un_isbn_ausente_es_obligatorio()
    {
        var resultado = new ValidadorDeRegistrarObra().Validar(Obra(Impresa(isbn: null)));

        Assert.Equal([new ErrorDeCampo("ediciones[0].isbn", "obligatorio")], resultado.Errores);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-19")]
    [InlineData(6000)]
    [InlineData(5001)]
    [InlineData(-3)]
    [InlineData(0)]
    public void Un_stock_inicial_fuera_de_1_a_5000_incluido_cero_se_rechaza_sin_crear_nada(int stock)
    {
        var resultado = new ValidadorDeRegistrarObra().Validar(Obra(Impresa(stock: stock)));

        Assert.Equal([new ErrorDeCampo("ediciones[0].stockInicial", "fuera-de-rango")], resultado.Errores);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-16")]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(5000)]
    public void Un_stock_inicial_de_1_a_5000_o_ausente_se_acepta(int stock)
    {
        Assert.True(new ValidadorDeRegistrarObra().Validar(Obra(Impresa(stock: stock))).EsValido);
        Assert.True(new ValidadorDeRegistrarObra().Validar(Obra(Impresa(stock: null))).EsValido);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-01")]
    [InlineData("-0.01", "fuera-de-rango")]
    [InlineData("129.005", "formato-invalido")]
    [InlineData("10000000000", "fuera-de-rango")]
    public void El_precio_no_negativo_con_dos_decimales_y_dentro_del_maximo(string precio, string clave)
    {
        var resultado = new ValidadorDeRegistrarObra().Validar(Obra(Impresa(precio: decimal.Parse(precio, System.Globalization.CultureInfo.InvariantCulture))));

        Assert.Equal([new ErrorDeCampo("ediciones[0].precio", clave)], resultado.Errores);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-03")]
    public void Una_digital_necesita_licencia_y_archivo_y_nunca_stock_y_una_impresa_no_lleva_licencia_ni_archivo()
    {
        var digitalIncompleta = new NuevaEdicionEntrada(FormatoDeEdicion.Digital, "9790000000018", 10m, null, null, 5);
        var impresaConLicencia = new NuevaEdicionEntrada(FormatoDeEdicion.Impresa, "978-0-00-000001-9", 10m, Licencia(), Guid.NewGuid(), null);

        var primera = new ValidadorDeRegistrarObra().Validar(Obra(digitalIncompleta));
        var segunda = new ValidadorDeRegistrarObra().Validar(Obra(impresaConLicencia));

        Assert.Equal(["ediciones[0].licencia", "ediciones[0].archivoRef", "ediciones[0].stockInicial"], Campos(primera));
        Assert.Equal(["ediciones[0].licencia", "ediciones[0].archivoRef"], Campos(segunda));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-13")]
    public void La_forma_de_la_licencia_tiene_topes_pero_su_contenido_lo_decide_el_dominio()
    {
        var excesos = Licencia(titular: new string('t', 201), territorios: Enumerable.Repeat("PE", 251).ToList());
        var largo = Licencia(territorios: ["PE", "PERUANO-LARGO"]);
        var sinTitular = Licencia(titular: null);

        Assert.Equal(["ediciones[0].licencia.titular", "ediciones[0].licencia.territorios"], Campos(new ValidadorDeRegistrarObra().Validar(Obra(Digital(licencia: excesos)))));
        Assert.Equal(["ediciones[0].licencia.territorios"], Campos(new ValidadorDeRegistrarObra().Validar(Obra(Digital(licencia: largo)))));
        // El titular faltante no es un error de forma: lo rechaza LicenciaDigital (ECO-LICENCIA-INVALIDA, con el campo faltante).
        Assert.True(new ValidadorDeRegistrarObra().Validar(Obra(Digital(licencia: sinTitular))).EsValido);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-08")]
    public void Editar_una_obra_o_una_edicion_exige_identificadores_y_la_version_leida()
    {
        var obra = new ValidadorDeEditarObra().Validar(new EditarObraEntrada(Guid.Empty, 0, "T", "A", "E", "C", Guid.Empty));
        var edicion = new ValidadorDeEditarEdicion().Validar(new EditarEdicionEntrada(Guid.NewGuid(), Guid.Empty, 0, -1m, Licencia(titular: new string('x', 300)), Guid.Empty));
        var valida = new ValidadorDeEditarEdicion().Validar(new EditarEdicionEntrada(Guid.NewGuid(), Guid.NewGuid(), 3, 139m, null, null));

        Assert.Equal(["obraId", "version", "portadaRef"], Campos(obra));
        Assert.Equal(["edicionId", "version", "precio", "licencia.titular", "archivoRef"], Campos(edicion));
        Assert.True(valida.EsValido);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-15")]
    public void Las_bajas_exigen_identificadores_y_version()
    {
        Assert.Equal(["obraId", "version"], Campos(new ValidadorDeDarDeBajaObra().Validar(new DarDeBajaObraEntrada(Guid.Empty, 0))));
        Assert.Equal(["obraId", "edicionId", "version"], Campos(new ValidadorDeDarDeBajaEdicion().Validar(new DarDeBajaEdicionEntrada(Guid.Empty, Guid.Empty, -2))));
        Assert.True(new ValidadorDeDarDeBajaEdicion().Validar(new DarDeBajaEdicionEntrada(Guid.NewGuid(), Guid.NewGuid(), 1)).EsValido);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-05")]
    public void El_motivo_de_retiro_es_obligatorio_tiene_tope_de_300_y_no_lleva_datos_prohibidos()
    {
        var obra = Guid.NewGuid();
        var edicion = Guid.NewGuid();

        Assert.Equal(["motivo"], Campos(new ValidadorDeRetirarEdicionDigital().Validar(new RetirarEdicionDigitalEntrada(obra, edicion, "  "))));
        Assert.Equal(["motivo"], Campos(new ValidadorDeRetirarEdicionDigital().Validar(new RetirarEdicionDigitalEntrada(obra, edicion, new string('m', 301)))));
        Assert.Equal(["motivo"], Campos(new ValidadorDeRetirarEdicionDigital().Validar(new RetirarEdicionDigitalEntrada(obra, edicion, "Pagado con la tarjeta 4111 1111 1111 1111"))));
        Assert.True(new ValidadorDeRetirarEdicionDigital().Validar(new RetirarEdicionDigitalEntrada(obra, edicion, new string('m', 300))).EsValido);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-19")]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(5001)]
    [InlineData(6000)]
    public void La_cantidad_de_un_ingreso_fuera_de_1_a_5000_se_rechaza(int cantidad)
    {
        var resultado = new ValidadorDeRegistrarIngreso().Validar(new RegistrarIngresoEntrada(Guid.NewGuid(), cantidad, "GR-001", null, "token"));

        Assert.Equal([new ErrorDeCampo("cantidad", "fuera-de-rango")], resultado.Errores);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public void Un_ingreso_de_30_con_la_referencia_GR_001_se_acepta_y_sus_textos_tienen_tope()
    {
        var edicion = Guid.NewGuid();

        Assert.True(new ValidadorDeRegistrarIngreso().Validar(new RegistrarIngresoEntrada(edicion, 30, "GR-001", "Lote de la editorial\ncon dos cajas", "token")).EsValido);
        var excesos = new ValidadorDeRegistrarIngreso().Validar(new RegistrarIngresoEntrada(edicion, 30, new string('r', 81), new string('n', 501), null));
        Assert.Equal(["referencia", "nota", "token"], Campos(excesos));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public void La_nota_de_un_ingreso_no_lleva_una_tarjeta_y_la_referencia_no_admite_saltos_de_linea()
    {
        var edicion = Guid.NewGuid();

        var tarjeta = new ValidadorDeRegistrarIngreso().Validar(new RegistrarIngresoEntrada(edicion, 5, null, "pago 4111 1111 1111 1111", "token"));
        var salto = new ValidadorDeRegistrarIngreso().Validar(new RegistrarIngresoEntrada(edicion, 5, "GR\n001", null, "token"));

        Assert.Equal(["nota"], Campos(tarjeta));
        Assert.Equal(["referencia"], Campos(salto));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public void Reintentar_conciliar_emitir_y_listar_exigen_sus_identificadores_y_una_resolucion_conocida()
    {
        Assert.Equal(["ingresoId"], Campos(new ValidadorDeReintentarIngreso().Validar(new ReintentarIngresoEntrada(Guid.Empty))));
        Assert.Equal(["ingresoId", "resolucion"], Campos(new ValidadorDeConciliarIngreso().Validar(new ConciliarIngresoEntrada(Guid.Empty, (ResolucionDeIngreso)9, false))));
        Assert.Equal(["edicionId"], Campos(new ValidadorDeEmitirClaveDeIngreso().Validar(new EmitirClaveDeIngresoEntrada(Guid.Empty))));
        Assert.Equal(["edicionId"], Campos(new ValidadorDeListarIngresos().Validar(new ListarIngresosEntrada(Guid.Empty))));
        Assert.True(new ValidadorDeConciliarIngreso().Validar(new ConciliarIngresoEntrada(Guid.NewGuid(), ResolucionDeIngreso.Fallido, true)).EsValido);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Subir_un_archivo_exige_una_finalidad_conocida_y_un_cuerpo()
    {
        Assert.Equal(["finalidad", "archivo"], Campos(new ValidadorDeSubirArchivo().Validar(new SubirArchivoEntrada((FinalidadDeLaSubida)7, null!))));
        Assert.True(new ValidadorDeSubirArchivo().Validar(new SubirArchivoEntrada(FinalidadDeLaSubida.Libro, new MemoryStream())).EsValido);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public void La_busqueda_valida_el_texto_de_2_a_100_el_rango_de_precio_y_la_pagina()
    {
        var invalida = new BusquedaDeObras("a", new string('x', 201), null, null, (FormatoDeEdicion)5, -1m, 0m, false, 0, 51);
        var rangoInvertido = new BusquedaDeObras(null, null, null, null, null, 100m, 50m);

        Assert.Equal(["q", "autor", "formato", "precioMin", "pagina", "tamano"], Campos(new ValidadorDeBusquedaDeObras().Validar(invalida)));
        Assert.Equal(["precioMax"], Campos(new ValidadorDeBusquedaDeObras().Validar(rangoInvertido)));
        Assert.Equal(["q"], Campos(new ValidadorDeBusquedaDeObras().Validar(new BusquedaDeObras(new string('q', 101)))));
        Assert.True(new ValidadorDeBusquedaDeObras().Validar(new BusquedaDeObras("Estructuras", null, null, "Técnico", FormatoDeEdicion.Digital, 0m, 100m, true, 1, 50)).EsValido);
        Assert.True(new ValidadorDeBusquedaDeObras().Validar(new BusquedaDeObras()).EsValido);
        Assert.Equal(["pagina"], Campos(new ValidadorDeBusquedaDeObras().Validar(new BusquedaDeObras(Pagina: 1_000_001))));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public void La_lista_de_administracion_valida_el_texto_y_la_pagina()
    {
        Assert.Equal(["q", "tamano"], Campos(new ValidadorDeListarObrasDeAdministracion().Validar(new ListarObrasDeAdministracionEntrada(new string('q', 101), false, 1, 0))));
        Assert.True(new ValidadorDeListarObrasDeAdministracion().Validar(new ListarObrasDeAdministracionEntrada()).EsValido);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-17")]
    public void Las_ediciones_por_identificador_son_hasta_50_sin_vacios()
    {
        var cincuenta = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();

        Assert.True(ValidadorDeEdicionesPorIdentificador.Validar(cincuenta).EsValido);
        Assert.Equal(["edicionIds"], Campos(ValidadorDeEdicionesPorIdentificador.Validar([.. cincuenta, Guid.NewGuid()])));
        Assert.Equal(["edicionIds"], Campos(ValidadorDeEdicionesPorIdentificador.Validar([])));
        Assert.Equal(["edicionIds"], Campos(ValidadorDeEdicionesPorIdentificador.Validar(null)));
        Assert.Equal(["edicionIds"], Campos(ValidadorDeEdicionesPorIdentificador.Validar([Guid.Empty])));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-19")]
    public void Los_errores_de_validacion_llevan_el_campo_y_una_clave_pero_nunca_el_valor_recibido()
    {
        var resultado = new ValidadorDeRegistrarIngreso().Validar(new RegistrarIngresoEntrada(Guid.NewGuid(), 6000, "pago con 4111 1111 1111 1111", null, "token-secreto-12345"));

        Assert.All(resultado.Errores, e =>
        {
            Assert.DoesNotContain("4111", e.Clave, StringComparison.Ordinal);
            Assert.DoesNotContain("6000", e.Clave, StringComparison.Ordinal);
            Assert.DoesNotContain("secreto", e.Campo + e.Clave, StringComparison.Ordinal);
        });
    }
}
