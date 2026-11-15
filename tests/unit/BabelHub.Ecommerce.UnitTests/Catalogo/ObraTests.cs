using System.Security.Cryptography;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>La obra y sus ediciones (T02; CAT-01, CAT-04, CAT-13): a lo más una impresa y una digital, al menos una edición no dada de baja, bajas que conservan los datos y versión que crece con cada operación.</summary>
public sealed class ObraTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(-5));
    private static readonly DateOnly Hoy = new(2026, 11, 14);
    private static readonly Guid Ana = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static HuellaDeArchivo Huella(string semilla) => HuellaDeArchivo.Desde(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(semilla)));

    private static PortadaDeObra UnaPortada(string semilla = "portada") => new(ClaveEnAlmacen.Nueva(), TipoDeArchivo.Jpeg, Huella(semilla));

    private static ArchivoDeEdicion UnLibro(TipoDeArchivo tipo = TipoDeArchivo.Epub, string semilla = "libro") => new(ClaveEnAlmacen.Nueva(), tipo, 1_000, Huella(semilla), Ahora, Ana);

    private static LicenciaDigital UnaLicencia(FormatoDigital formato = FormatoDigital.Epub, DateOnly? hasta = null, string titular = "Editorial Andina", IEnumerable<string>? territorios = null) =>
        LicenciaDigital.Crear(TipoDeLicencia.Comercial, titular, territorios ?? ["PE"], hasta ?? Hoy.AddYears(1), formato, Hoy).Licencia!;

    private static Edicion Impresa(string isbn = "978-0-00-000001-9", decimal precio = 129m) => Edicion.Impresa(Guid.NewGuid(), Isbn.Crear(isbn), Precio.Crear(precio));

    private static Edicion Digital(string isbn = "9790000000018", decimal precio = 49m) =>
        Edicion.Digital(Guid.NewGuid(), Isbn.Crear(isbn), Precio.Crear(precio), UnaLicencia(), UnLibro());

    private static DatosDeObra Datos(string titulo = "Estructuras de datos") => new(titulo, "Ana Quispe", "Editorial Andina", "Técnico");

    private static Obra RegistrarConAmbas() => Obra.Registrar(Guid.NewGuid(), Datos(), UnaPortada(), [Impresa(), Digital()], Ahora).Valor;

    private static Obra RegistrarSoloImpresa() => Obra.Registrar(Guid.NewGuid(), Datos(), UnaPortada(), [Impresa()], Ahora).Valor;

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public void Una_obra_con_una_edicion_impresa_y_su_isbn_se_registra()
    {
        var resultado = Obra.Registrar(Guid.NewGuid(), Datos(), UnaPortada(), [Impresa()], Ahora);

        Assert.True(resultado.EsExito);
        var obra = resultado.Valor;
        Assert.Equal("Estructuras de datos", obra.Titulo);
        Assert.Equal(EstadoDeObra.Activa, obra.Estado);
        Assert.Equal(1, obra.Version);
        Assert.Single(obra.Ediciones);
        Assert.Equal("9780000000019", obra.Ediciones[0].Isbn.Isbn13);
        Assert.Equal(Ahora, obra.CreadaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public void Una_obra_sin_ninguna_edicion_se_rechaza()
    {
        var resultado = Obra.Registrar(Guid.NewGuid(), Datos(), UnaPortada(), [], Ahora);

        Assert.False(resultado.EsExito);
        Assert.Equal(RechazoDeCatalogo.ObraSinEdiciones, resultado.Rechazo!.Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public void Una_obra_acepta_una_impresa_y_una_digital_pero_no_dos_del_mismo_tipo()
    {
        var ambas = Obra.Registrar(Guid.NewGuid(), Datos(), UnaPortada(), [Impresa(), Digital()], Ahora);
        var dosImpresas = Obra.Registrar(Guid.NewGuid(), Datos(), UnaPortada(), [Impresa(), Impresa("9790000000018")], Ahora);

        Assert.True(ambas.EsExito);
        Assert.Equal(RechazoDeCatalogo.EdicionDuplicadaPorTipo, dosImpresas.Rechazo!.Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-14")]
    public void Dos_ediciones_de_la_misma_obra_no_pueden_tener_el_mismo_isbn_aunque_uno_sea_isbn_10()
    {
        var digital = Edicion.Digital(Guid.NewGuid(), Isbn.Crear("0-306-40615-2"), Precio.Crear(10m), UnaLicencia(), UnLibro());
        var impresa = Edicion.Impresa(Guid.NewGuid(), Isbn.Crear("978-0-306-40615-7"), Precio.Crear(10m));

        var resultado = Obra.Registrar(Guid.NewGuid(), Datos(), UnaPortada(), [impresa, digital], Ahora);

        Assert.Equal(RechazoDeCatalogo.IsbnRepetidoEnLaObra, resultado.Rechazo!.Motivo);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-01")]
    [InlineData("titulo")]
    [InlineData("autor")]
    [InlineData("editorial")]
    [InlineData("categoria")]
    public void Un_dato_de_la_obra_vacio_o_demasiado_largo_se_rechaza_indicando_el_campo(string campo)
    {
        DatosDeObra Con(string? valor) => campo switch
        {
            "titulo" => new DatosDeObra(valor, "A", "E", "C"),
            "autor" => new DatosDeObra("T", valor, "E", "C"),
            "editorial" => new DatosDeObra("T", "A", valor, "C"),
            _ => new DatosDeObra("T", "A", "E", valor),
        };

        var vacio = Obra.Registrar(Guid.NewGuid(), Con("  "), UnaPortada(), [Impresa()], Ahora);
        var largo = Obra.Registrar(Guid.NewGuid(), Con(new string('x', 201)), UnaPortada(), [Impresa()], Ahora);

        Assert.Equal(new RechazoDeObra(RechazoDeCatalogo.DatosInvalidos, campo), vacio.Rechazo);
        Assert.Equal(new RechazoDeObra(RechazoDeCatalogo.DatosInvalidos, campo), largo.Rechazo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public void Los_datos_de_la_obra_se_guardan_sin_espacios_en_los_extremos()
    {
        var obra = Obra.Registrar(Guid.NewGuid(), new DatosDeObra("  Libro  ", " Autor", "Editorial ", " Cat "), UnaPortada(), [Impresa()], Ahora).Valor;

        Assert.Equal(("Libro", "Autor", "Editorial", "Cat"), (obra.Titulo, obra.Autor, obra.Editorial, obra.Categoria));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-03")]
    public void Una_edicion_digital_tiene_licencia_y_archivo_y_una_impresa_no()
    {
        var digital = Digital();
        var impresa = Impresa();

        Assert.NotNull(digital.Licencia);
        Assert.NotNull(digital.Archivo);
        Assert.Null(impresa.Licencia);
        Assert.Null(impresa.Archivo);
        Assert.Throws<ArgumentNullException>(() => Edicion.Digital(Guid.NewGuid(), Isbn.Crear("9790000000018"), Precio.Crear(1m), UnaLicencia(), null!));
        Assert.Throws<ArgumentNullException>(() => Edicion.Digital(Guid.NewGuid(), Isbn.Crear("9790000000018"), Precio.Crear(1m), null!, UnLibro()));
        Assert.Throws<ArgumentException>(() => Edicion.Digital(Guid.NewGuid(), Isbn.Crear("9790000000018"), Precio.Crear(1m), UnaLicencia(FormatoDigital.Pdf), UnLibro(TipoDeArchivo.Epub)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-03")]
    public void Rehidratar_exige_licencia_y_archivo_en_una_digital_y_ninguno_en_una_impresa()
    {
        var isbn = Isbn.Crear("9790000000018");
        var precio = Precio.Crear(1m);

        Assert.Throws<ArgumentException>(() => Edicion.Rehidratar(Guid.NewGuid(), TipoDeEdicion.Digital, isbn, precio, EstadoDeEdicion.Activa, null, null, null, null, null));
        Assert.Throws<ArgumentException>(() => Edicion.Rehidratar(Guid.NewGuid(), TipoDeEdicion.Impresa, isbn, precio, EstadoDeEdicion.Activa, null, null, null, UnaLicencia(), UnLibro()));
        Assert.Throws<ArgumentException>(() => Edicion.Rehidratar(Guid.NewGuid(), TipoDeEdicion.Impresa, isbn, precio, EstadoDeEdicion.RetiradaDeVenta, null, null, null, null, null));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public void Agregar_una_edicion_incrementa_la_version_y_respeta_el_tipo_y_el_isbn()
    {
        var obra = RegistrarSoloImpresa();

        var agregada = obra.AgregarEdicion(Digital());
        var otraImpresa = obra.AgregarEdicion(Impresa("9790000000018"));
        var isbnRepetido = RegistrarSoloImpresa().AgregarEdicion(Edicion.Digital(Guid.NewGuid(), Isbn.Crear("978-0-00-000001-9"), Precio.Crear(5m), UnaLicencia(), UnLibro()));

        Assert.True(agregada.EsExito);
        Assert.Equal(2, obra.Version);
        Assert.Equal(2, obra.Ediciones.Count);
        Assert.Equal(RechazoDeCatalogo.EdicionDuplicadaPorTipo, otraImpresa.Rechazo!.Motivo);
        Assert.Equal(2, obra.Version);
        Assert.Equal(RechazoDeCatalogo.IsbnRepetidoEnLaObra, isbnRepetido.Rechazo!.Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public void Una_edicion_dada_de_baja_libera_su_tipo_pero_su_isbn_sigue_reservado()
    {
        var obra = RegistrarConAmbas();
        var impresa = obra.Ediciones.Single(e => e.Tipo == TipoDeEdicion.Impresa);
        obra.DarDeBajaEdicion(impresa.Id, Ahora);

        var mismoIsbn = obra.AgregarEdicion(Impresa("978-0-00-000001-9"));
        var otroIsbn = obra.AgregarEdicion(Impresa("0-306-40615-2"));

        Assert.Equal(RechazoDeCatalogo.IsbnRepetidoEnLaObra, mismoIsbn.Rechazo!.Motivo);
        Assert.True(otroIsbn.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-08")]
    public void Editar_el_precio_devuelve_el_anterior_y_el_nuevo_e_incrementa_la_version()
    {
        var obra = RegistrarSoloImpresa();
        var impresa = obra.Ediciones[0];

        var resultado = obra.EditarEdicion(impresa.Id, new CambioDeEdicion(Precio: Precio.Crear(139m)));

        Assert.True(resultado.EsExito);
        Assert.Equal(129m, resultado.Valor.PrecioAnterior!.Valor);
        Assert.Equal(139m, impresa.Precio.Valor);
        Assert.True(resultado.Valor.CambioElPrecio);
        Assert.False(resultado.Valor.CambioLaLicencia);
        Assert.False(resultado.Valor.ReemplazoElArchivo);
        Assert.Equal(2, obra.Version);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-08")]
    public void Editar_con_el_mismo_precio_no_lo_reporta_como_cambio_pero_si_cuenta_como_operacion()
    {
        var obra = RegistrarSoloImpresa();

        var resultado = obra.EditarEdicion(obra.Ediciones[0].Id, new CambioDeEdicion(Precio: Precio.Crear(129.00m)));

        Assert.False(resultado.Valor.CambioElPrecio);
        Assert.Equal(2, obra.Version);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-08")]
    public void Cambiar_la_licencia_o_reemplazar_el_archivo_de_una_digital_devuelve_lo_anterior()
    {
        var obra = RegistrarConAmbas();
        var digital = obra.Ediciones.Single(e => e.EsDigital);
        var archivoAnterior = digital.Archivo;
        var licenciaAnterior = digital.Licencia;
        var nuevaLicencia = UnaLicencia(territorios: ["PE", "CL"]);
        var nuevoLibro = UnLibro(semilla: "otro");

        var resultado = obra.EditarEdicion(digital.Id, new CambioDeEdicion(Licencia: nuevaLicencia, Archivo: nuevoLibro));

        Assert.True(resultado.EsExito);
        Assert.Equal(licenciaAnterior, resultado.Valor.LicenciaAnterior);
        Assert.Same(archivoAnterior, resultado.Valor.ArchivoAnterior);
        Assert.Equal(nuevaLicencia, digital.Licencia);
        Assert.Same(nuevoLibro, digital.Archivo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-03")]
    public void Una_impresa_no_acepta_licencia_ni_archivo_y_el_archivo_debe_coincidir_con_el_formato_de_la_licencia()
    {
        var obra = RegistrarConAmbas();
        var impresa = obra.Ediciones.Single(e => !e.EsDigital);
        var digital = obra.Ediciones.Single(e => e.EsDigital);

        var conLicencia = obra.EditarEdicion(impresa.Id, new CambioDeEdicion(Licencia: UnaLicencia()));
        var conArchivo = obra.EditarEdicion(impresa.Id, new CambioDeEdicion(Archivo: UnLibro()));
        var formatoDistinto = obra.EditarEdicion(digital.Id, new CambioDeEdicion(Licencia: UnaLicencia(FormatoDigital.Pdf)));
        var archivoDistinto = obra.EditarEdicion(digital.Id, new CambioDeEdicion(Archivo: UnLibro(TipoDeArchivo.Pdf)));
        var ambosPdf = obra.EditarEdicion(digital.Id, new CambioDeEdicion(Licencia: UnaLicencia(FormatoDigital.Pdf), Archivo: UnLibro(TipoDeArchivo.Pdf)));

        Assert.Equal(RechazoDeCatalogo.SoloEdicionDigital, conLicencia.Rechazo!.Motivo);
        Assert.Equal(RechazoDeCatalogo.SoloEdicionDigital, conArchivo.Rechazo!.Motivo);
        Assert.Equal(RechazoDeCatalogo.ArchivoNoCoincideConLaLicencia, formatoDistinto.Rechazo!.Motivo);
        Assert.Equal(RechazoDeCatalogo.ArchivoNoCoincideConLaLicencia, archivoDistinto.Rechazo!.Motivo);
        Assert.True(ambosPdf.EsExito);
        Assert.Equal(2, obra.Version);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-04")]
    public void Editar_el_precio_de_una_digital_con_licencia_vencida_no_falla()
    {
        var vencida = LicenciaDigital.Rehidratar(TipoDeLicencia.Comercial, "Editorial", ["PE"], Hoy.AddDays(-30), FormatoDigital.Epub);
        var digital = Edicion.Rehidratar(Guid.NewGuid(), TipoDeEdicion.Digital, Isbn.Crear("9790000000018"), Precio.Crear(49m), EstadoDeEdicion.Activa, null, null, null, vencida, UnLibro());
        var obra = Obra.Rehidratar(Guid.NewGuid(), Datos(), UnaPortada(), 7, EstadoDeObra.Activa, null, Ahora, [Impresa(), digital]);

        var resultado = obra.EditarEdicion(digital.Id, new CambioDeEdicion(Precio: Precio.Crear(59m)));

        Assert.True(resultado.EsExito);
        Assert.Equal(MotivoDeNoDisponible.LicenciaVencida, digital.MotivoDeNoDisponiblePara(Hoy));
        Assert.Equal(8, obra.Version);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-05")]
    public void Retirar_de_la_venta_la_unica_edicion_digital_se_permite_y_la_obra_conserva_su_edicion()
    {
        var obra = Obra.Registrar(Guid.NewGuid(), Datos(), UnaPortada(), [Digital()], Ahora).Valor;
        var digital = obra.Ediciones[0];
        var archivo = digital.Archivo;

        var resultado = obra.RetirarEdicionDigital(digital.Id, "  Fin del contrato  ", Ahora.AddDays(1));

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDeEdicion.RetiradaDeVenta, digital.Estado);
        Assert.Equal("Fin del contrato", digital.MotivoDeRetiro);
        Assert.Equal(Ahora.AddDays(1), digital.RetiradaEn);
        Assert.Same(archivo, digital.Archivo);
        Assert.Equal(MotivoDeNoDisponible.RetiradaDeLaVenta, digital.MotivoDeNoDisponiblePara(Hoy));
        Assert.Equal(2, obra.Version);
        Assert.Single(obra.Ediciones, e => !e.EstaDadaDeBaja);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-05")]
    public void Retirar_exige_una_edicion_digital_un_motivo_y_no_se_repite()
    {
        var obra = RegistrarConAmbas();
        var impresa = obra.Ediciones.Single(e => !e.EsDigital);
        var digital = obra.Ediciones.Single(e => e.EsDigital);

        Assert.Equal(RechazoDeCatalogo.SoloEdicionDigital, obra.RetirarEdicionDigital(impresa.Id, "x", Ahora).Rechazo!.Motivo);
        Assert.Equal(new RechazoDeObra(RechazoDeCatalogo.DatosInvalidos, "motivo"), obra.RetirarEdicionDigital(digital.Id, "  ", Ahora).Rechazo);
        Assert.Equal(new RechazoDeObra(RechazoDeCatalogo.DatosInvalidos, "motivo"), obra.RetirarEdicionDigital(digital.Id, new string('m', 301), Ahora).Rechazo);
        Assert.Equal(RechazoDeCatalogo.EdicionNoEncontrada, obra.RetirarEdicionDigital(Guid.NewGuid(), "x", Ahora).Rechazo!.Motivo);
        Assert.Equal(1, obra.Version);

        Assert.True(obra.RetirarEdicionDigital(digital.Id, new string('m', 300), Ahora).EsExito);
        Assert.Equal(RechazoDeCatalogo.EdicionYaRetirada, obra.RetirarEdicionDigital(digital.Id, "otra vez", Ahora).Rechazo!.Motivo);
        Assert.Equal(2, obra.Version);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public void La_baja_de_la_ultima_edicion_no_dada_de_baja_se_rechaza_y_una_retirada_cuenta_como_existente()
    {
        var obra = RegistrarConAmbas();
        var impresa = obra.Ediciones.Single(e => !e.EsDigital);
        var digital = obra.Ediciones.Single(e => e.EsDigital);

        obra.RetirarEdicionDigital(digital.Id, "Fin del contrato", Ahora);
        var bajaDeLaImpresa = obra.DarDeBajaEdicion(impresa.Id, Ahora);

        Assert.True(bajaDeLaImpresa.EsExito);
        Assert.Equal(RechazoDeCatalogo.UltimaEdicionNoDadaDeBaja, obra.DarDeBajaEdicion(digital.Id, Ahora).Rechazo!.Motivo);
        Assert.Equal(EstadoDeEdicion.RetiradaDeVenta, digital.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-10")]
    public void La_baja_de_una_edicion_digital_conserva_su_archivo_su_licencia_y_los_accesos()
    {
        var obra = RegistrarConAmbas();
        var digital = obra.Ediciones.Single(e => e.EsDigital);
        var archivo = digital.Archivo;
        var licencia = digital.Licencia;

        var resultado = obra.DarDeBajaEdicion(digital.Id, Ahora.AddDays(2));

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDeEdicion.Baja, digital.Estado);
        Assert.Equal(Ahora.AddDays(2), digital.DadaDeBajaEn);
        Assert.Same(archivo, digital.Archivo);
        Assert.Same(licencia, digital.Licencia);
        Assert.Equal(MotivoDeNoDisponible.DadaDeBaja, digital.MotivoDeNoDisponiblePara(Hoy));
        Assert.Equal(2, obra.Version);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-10")]
    public void Dar_de_baja_la_obra_da_de_baja_todas_sus_ediciones_y_conserva_los_datos()
    {
        var obra = RegistrarConAmbas();
        var digital = obra.Ediciones.Single(e => e.EsDigital);
        var yaBaja = Obra.Registrar(Guid.NewGuid(), Datos(), UnaPortada(), [Impresa("0-306-40615-2"), Digital("9790000000018")], Ahora).Valor;
        yaBaja.DarDeBajaEdicion(yaBaja.Ediciones[0].Id, Ahora);

        var resultado = obra.DarDeBaja(Ahora.AddDays(3));
        var conUnaPrevia = yaBaja.DarDeBaja(Ahora.AddDays(3));

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDeObra.Baja, obra.Estado);
        Assert.Equal(Ahora.AddDays(3), obra.DadaDeBajaEn);
        Assert.All(obra.Ediciones, e => Assert.Equal(EstadoDeEdicion.Baja, e.Estado));
        Assert.Equal(2, resultado.Valor.EdicionesDadasDeBaja.Count);
        Assert.Equal("Estructuras de datos", obra.Titulo);
        Assert.NotNull(digital.Archivo);
        Assert.Equal(2, obra.Version);
        Assert.Single(conUnaPrevia.Valor.EdicionesDadasDeBaja);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-10")]
    public void Una_obra_dada_de_baja_no_acepta_mas_operaciones_pero_sigue_legible()
    {
        var obra = RegistrarConAmbas();
        var impresa = obra.Ediciones.Single(e => !e.EsDigital);
        obra.DarDeBaja(Ahora);
        var version = obra.Version;

        Assert.Equal(RechazoDeCatalogo.ObraDadaDeBaja, obra.DarDeBaja(Ahora).Rechazo!.Motivo);
        Assert.Equal(RechazoDeCatalogo.ObraDadaDeBaja, obra.EditarDatos(Datos("Otro")).Rechazo!.Motivo);
        Assert.Equal(RechazoDeCatalogo.ObraDadaDeBaja, obra.AgregarEdicion(Digital("0-306-40615-2")).Rechazo!.Motivo);
        Assert.Equal(RechazoDeCatalogo.ObraDadaDeBaja, obra.EditarEdicion(impresa.Id, new CambioDeEdicion(Precio: Precio.Crear(1m))).Rechazo!.Motivo);
        Assert.Equal(RechazoDeCatalogo.ObraDadaDeBaja, obra.DarDeBajaEdicion(impresa.Id, Ahora).Rechazo!.Motivo);
        Assert.Equal(RechazoDeCatalogo.ObraDadaDeBaja, obra.ReemplazarPortada(UnaPortada("x")).Rechazo!.Motivo);
        Assert.Equal(version, obra.Version);
        Assert.Same(impresa, obra.BuscarEdicion(impresa.Id));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-10")]
    public void Una_edicion_dada_de_baja_no_se_edita_ni_se_retira_ni_se_da_de_baja_otra_vez()
    {
        var obra = RegistrarConAmbas();
        var impresa = obra.Ediciones.Single(e => !e.EsDigital);
        var digital = obra.Ediciones.Single(e => e.EsDigital);
        obra.DarDeBajaEdicion(digital.Id, Ahora);

        Assert.Equal(RechazoDeCatalogo.EdicionDadaDeBaja, obra.EditarEdicion(digital.Id, new CambioDeEdicion(Precio: Precio.Crear(1m))).Rechazo!.Motivo);
        Assert.Equal(RechazoDeCatalogo.EdicionDadaDeBaja, obra.RetirarEdicionDigital(digital.Id, "x", Ahora).Rechazo!.Motivo);
        Assert.Equal(RechazoDeCatalogo.EdicionDadaDeBaja, obra.DarDeBajaEdicion(digital.Id, Ahora).Rechazo!.Motivo);
        Assert.Equal(RechazoDeCatalogo.UltimaEdicionNoDadaDeBaja, obra.DarDeBajaEdicion(impresa.Id, Ahora).Rechazo!.Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-08")]
    public void Editar_los_datos_de_la_obra_lista_los_campos_cambiados_y_reemplazar_la_portada_devuelve_la_anterior()
    {
        var obra = RegistrarSoloImpresa();
        var anterior = obra.Portada;
        var nueva = UnaPortada("nueva");

        var datos = obra.EditarDatos(new DatosDeObra("Estructuras de datos II", "Ana Quispe", "Otra editorial", "Técnico"));
        var portada = obra.ReemplazarPortada(nueva);

        Assert.Equal(["titulo", "editorial"], datos.Valor.CamposCambiados);
        Assert.Same(anterior, portada.Valor);
        Assert.Same(nueva, obra.Portada);
        Assert.Equal(3, obra.Version);
        Assert.Equal(RechazoDeCatalogo.DatosInvalidos, obra.EditarDatos(new DatosDeObra("", "a", "e", "c")).Rechazo!.Motivo);
        Assert.Equal(3, obra.Version);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public void La_version_esperada_se_compara_con_la_actual()
    {
        var obra = RegistrarSoloImpresa();

        Assert.True(obra.CoincideLaVersion(1));
        obra.EditarDatos(Datos("Otro título"));
        Assert.False(obra.CoincideLaVersion(1));
        Assert.True(obra.CoincideLaVersion(2));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-04")]
    public void Una_impresa_agotada_no_cambia_su_oferta_y_una_digital_vigente_se_ofrece()
    {
        var obra = RegistrarConAmbas();

        Assert.All(obra.Ediciones, e => Assert.True(e.SeOfrece(Hoy)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-04")]
    public void Una_digital_sin_peru_no_se_ofrece_y_la_baja_manda_sobre_el_retiro_y_este_sobre_la_licencia()
    {
        var sinPeru = Edicion.Digital(Guid.NewGuid(), Isbn.Crear("9790000000018"), Precio.Crear(10m), UnaLicencia(territorios: ["CL"]), UnLibro());
        var obra = Obra.Registrar(Guid.NewGuid(), Datos(), UnaPortada(), [Impresa(), sinPeru], Ahora).Valor;

        Assert.Equal(MotivoDeNoDisponible.SinPeru, sinPeru.MotivoDeNoDisponiblePara(Hoy));
        obra.RetirarEdicionDigital(sinPeru.Id, "Retirada", Ahora);
        Assert.Equal(MotivoDeNoDisponible.RetiradaDeLaVenta, sinPeru.MotivoDeNoDisponiblePara(Hoy));
        obra.DarDeBajaEdicion(sinPeru.Id, Ahora);
        Assert.Equal(MotivoDeNoDisponible.DadaDeBaja, sinPeru.MotivoDeNoDisponiblePara(Hoy));
    }
}
