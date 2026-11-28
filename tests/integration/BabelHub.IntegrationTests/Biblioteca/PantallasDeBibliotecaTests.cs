using System.Net;
using System.Text.Json;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.IntegrationTests.Web;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Biblioteca;

/// <summary>
/// Las pantallas de Biblioteca con las vistas reales (T16 a T20; BIB-02, BIB-06, BIB-08, BIB-12, BIB-13, BIB-16, AC-BIB-02, AC-BIB-07, AC-BIB-10, AC-BIB-14): mi biblioteca con «usadas de 5», el botón de descarga con su clave, el aviso de los cinco minutos, la ayuda de lista cerrada, los estados
/// estándar, y la administración (consulta auditada, ayudas, restitución y cierre) con el rechazo a quien no tiene permiso. La Aplicación se prueba aparte: aquí los contratos son dobles.
/// </summary>
public sealed class PantallasDeBibliotecaTests
{
    private static readonly DateTimeOffset Compra = new(2026, 11, 3, 15, 0, 0, TimeSpan.Zero);

    private sealed class BibliotecaDelClienteFalsa : IBibliotecaDelCliente
    {
        public Resultado<PaginaDeAccesos> Respuesta { get; set; } = Resultado<PaginaDeAccesos>.Exito(new PaginaDeAccesos([], 1, 12, 0));

        public (int Pagina, int Tamano)? UltimaConsulta { get; private set; }

        public Task<Resultado<PaginaDeAccesos>> Listar(int pagina, int tamano, CancellationToken cancelacion = default)
        {
            UltimaConsulta = (pagina, tamano);
            return Task.FromResult(Respuesta);
        }

        public Task<Resultado<AccesoDeBiblioteca>> Obtener(Guid accesoId, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class AyudaFalsa : IAyudaDeLaBiblioteca
    {
        public List<(Guid Acceso, string Motivo)> Llamadas { get; } = [];

        public Resultado<AyudaDeBiblioteca>? Respuesta { get; set; }

        public Task<Resultado<AyudaDeBiblioteca>> Pedir(Guid accesoId, string motivo, CancellationToken cancelacion = default)
        {
            Llamadas.Add((accesoId, motivo));
            return Task.FromResult(Respuesta ?? Resultado<AyudaDeBiblioteca>.Exito(new AyudaDeBiblioteca(Guid.NewGuid(), accesoId, motivo, EstadoDeLaAyudaDeBiblioteca.Abierta, Compra, null)));
        }
    }

    private sealed class AdministracionFalsa : IAdministracionDeLaBiblioteca
    {
        public List<string> Llamadas { get; } = [];

        public RestituirDescargasEntrada? UltimaRestitucion { get; private set; }

        public Resultado<IReadOnlyList<AccesoDeBiblioteca>> RespuestaDelCliente { get; set; } = Resultado<IReadOnlyList<AccesoDeBiblioteca>>.Exito([]);

        public Resultado<PaginaDeAyudas> RespuestaDeAyudas { get; set; } = Resultado<PaginaDeAyudas>.Exito(new PaginaDeAyudas([], 1, 20, 0));

        public Resultado<AccesoDeBiblioteca>? RespuestaDeRestitucion { get; set; }

        public EstadoDeLaAyudaDeBiblioteca? UltimoFiltro { get; private set; }

        public Task<Resultado<IReadOnlyList<AccesoDeBiblioteca>>> ConsultarBibliotecaDeUnCliente(Guid cuentaId, CancellationToken cancelacion = default)
        {
            Llamadas.Add($"cliente:{cuentaId:D}");
            return Task.FromResult(RespuestaDelCliente);
        }

        public Task<Resultado<PaginaDeAyudas>> ListarAyudas(EstadoDeLaAyudaDeBiblioteca? estado, int pagina, int tamano, CancellationToken cancelacion = default)
        {
            Llamadas.Add("ayudas");
            UltimoFiltro = estado;
            return Task.FromResult(RespuestaDeAyudas);
        }

        public Task<Resultado<AccesoDeBiblioteca>> RestituirDescargas(RestituirDescargasEntrada entrada, CancellationToken cancelacion = default)
        {
            Llamadas.Add("restituir");
            UltimaRestitucion = entrada;
            return Task.FromResult(RespuestaDeRestitucion ?? Resultado<AccesoDeBiblioteca>.Exito(Libro(entrada.AccesoId, "Libro", 1)));
        }

        public Task<Resultado<AyudaDeBiblioteca>> CerrarAyuda(Guid ayudaId, CancellationToken cancelacion = default)
        {
            Llamadas.Add($"cerrar:{ayudaId:D}");
            return Task.FromResult(Resultado<AyudaDeBiblioteca>.Exito(new AyudaDeBiblioteca(ayudaId, Guid.NewGuid(), "otro", EstadoDeLaAyudaDeBiblioteca.Cerrada, Compra, Compra.AddHours(1))));
        }
    }

    private static AccesoDeBiblioteca Libro(Guid? id, string titulo, int usadas, EstadoDelAccesoDigital estado = EstadoDelAccesoDigital.Vigente, bool ayuda = false, FormatoDeArchivoDigital? formato = FormatoDeArchivoDigital.Epub) =>
        new(id ?? Guid.NewGuid(), Guid.NewGuid(), titulo, formato, Compra, estado, usadas, 5, 5 - usadas, ayuda);

    private static Task<AnfitrionWeb> IniciarTienda(BibliotecaDelClienteFalsa biblioteca, AyudaFalsa ayuda, RolWeb rol = RolWeb.Cliente) =>
        AnfitrionConSesion.Iniciar(
            new SesionesWebDePrueba().ConSesionValida(rol, Guid.NewGuid()),
            HostWeb.Tienda,
            servicios: s =>
            {
                s.AddSingleton<IBibliotecaDelCliente>(biblioteca);
                s.AddSingleton<IAyudaDeLaBiblioteca>(ayuda);
            });

    private static Task<AnfitrionWeb> IniciarAdministracion(AdministracionFalsa administracion) =>
        AnfitrionConSesion.Iniciar(
            new SesionesWebDePrueba().ConSesionValida(RolWeb.AdministradorCentral, Guid.NewGuid()),
            HostWeb.Gestion,
            servicios: s => s.AddSingleton<IAdministracionDeLaBiblioteca>(administracion));

    private static async Task<(HttpStatusCode Estado, string Html, HttpResponseMessage Respuesta)> Pedir(AnfitrionWeb anfitrion, string ruta, HostWeb host, bool html = true)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        if (html)
        {
            solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        }

        solicitud.Headers.TryAddWithoutValidation("Cookie", AnfitrionConSesion.Cookie(host));
        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);
        return (respuesta.StatusCode, WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()), respuesta);
    }

    private static async Task<HttpResponseMessage> Enviar(AnfitrionWeb anfitrion, string ruta, HostWeb host, params (string Campo, string Valor)[] campos)
    {
        using var solicitud = await AnfitrionConSesion.Post(anfitrion, ruta, AnfitrionConSesion.Cookie(host));
        solicitud.Content = new FormUrlEncodedContent(campos.ToDictionary(c => c.Campo, c => c.Valor));
        return await anfitrion.Cliente.SendAsync(solicitud);
    }

    // ---------- Mi biblioteca ----------

    [Fact]
    [Trait("Criterio", "AC-BIB-02")]
    public async Task Mi_biblioteca_pinta_cada_libro_con_formato_fecha_estado_y_dos_de_cinco_descargas_y_el_aviso_de_cinco_minutos()
    {
        var disponible = Libro(null, "Cien años de soledad", 2, formato: FormatoDeArchivoDigital.Pdf);
        var agotado = Libro(null, "El Aleph", 5, ayuda: true);
        var suspendido = Libro(null, "Ficciones", 1, EstadoDelAccesoDigital.Suspendido);
        var revocado = Libro(null, "Rayuela", 0, EstadoDelAccesoDigital.Revocado);
        var biblioteca = new BibliotecaDelClienteFalsa { Respuesta = Resultado<PaginaDeAccesos>.Exito(new PaginaDeAccesos([disponible, agotado, suspendido, revocado], 1, 12, 4)) };
        await using var anfitrion = await IniciarTienda(biblioteca, new AyudaFalsa());

        var (estado, html, respuesta) = await Pedir(anfitrion, "/biblioteca", HostWeb.Tienda);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        foreach (var texto in new[] { "Cien años de soledad", "PDF", "EPUB", "2 de 5 descargas usadas", "5 de 5 descargas usadas", "Disponible", "En revisión", "Revocado", "03/11/2026", "5 minutos", "Alcanzaste el límite de descargas", "Te quedan 3 descargas" })
        {
            Assert.Contains(texto, html, StringComparison.Ordinal);
        }

        // Solo el libro disponible con cupo ofrece descargar (con la clave de un solo uso y el token); el agotado, el suspendido y el revocado no.
        Assert.Contains($"action=\"/biblioteca/{disponible.AccesoId:D}/descargar\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"/biblioteca/{agotado.AccesoId:D}/descargar", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"/biblioteca/{suspendido.AccesoId:D}/descargar", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"/biblioteca/{revocado.AccesoId:D}/descargar", html, StringComparison.Ordinal);
        Assert.Matches("name=\"clave\" value=\"[0-9a-f]{32}\"", html);
        Assert.Contains("Ya pediste ayuda sobre este libro", html, StringComparison.Ordinal);
        Assert.Contains($"action=\"/biblioteca/{disponible.AccesoId:D}/ayuda\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"/biblioteca/{revocado.AccesoId:D}/ayuda", html, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Amz", html, StringComparison.Ordinal);
        foreach (var motivo in new[] { "El archivo no abre", "El archivo no está disponible", "Se agotaron mis descargas", "Otro problema" })
        {
            Assert.Contains(motivo, html, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-02")]
    public async Task Sin_libros_se_muestra_el_estado_vacio_y_la_pagina_pedida_llega_a_la_aplicacion()
    {
        var biblioteca = new BibliotecaDelClienteFalsa();
        await using var anfitrion = await IniciarTienda(biblioteca, new AyudaFalsa());

        var (_, vacio, _) = await Pedir(anfitrion, "/biblioteca?pagina=2&tamano=6", HostWeb.Tienda);

        Assert.Equal((2, 6), biblioteca.UltimaConsulta);
        Assert.Contains("Aún no tienes libros digitales", vacio, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-BIB-02")]
    [InlineData("?pagina=0x")]
    [InlineData("?tamano=-1")]
    [InlineData("?otro=1")]
    [InlineData("?pagina=1&pagina=2")]
    public async Task Una_consulta_mal_formada_o_desconocida_se_rechaza_sin_llamar_a_la_aplicacion(string consulta)
    {
        var biblioteca = new BibliotecaDelClienteFalsa();
        await using var anfitrion = await IniciarTienda(biblioteca, new AyudaFalsa());

        var (estado, _, _) = await Pedir(anfitrion, "/biblioteca" + consulta, HostWeb.Tienda);

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Null(biblioteca.UltimaConsulta);
    }

    [Theory]
    [Trait("Criterio", "AC-BIB-03")]
    [InlineData(CategoriaDeError.NoDisponible, "servicio-no-disponible", HttpStatusCode.ServiceUnavailable, "No podemos mostrar tu biblioteca")]
    [InlineData(CategoriaDeError.Prohibido, "prohibido", HttpStatusCode.Forbidden, "No tienes acceso")]
    [InlineData(CategoriaDeError.Limite, "limite-excedido", HttpStatusCode.TooManyRequests, "Espera un momento")]
    public async Task Los_fallos_de_la_biblioteca_se_pintan_como_estado_estandar(CategoriaDeError categoria, string clave, HttpStatusCode codigo, string texto)
    {
        var biblioteca = new BibliotecaDelClienteFalsa
        {
            Respuesta = Resultado<PaginaDeAccesos>.Fallo(new ErrorDeAplicacion(categoria, clave, categoria == CategoriaDeError.Limite ? [new ErrorDeCampo("espera-en-segundos", "30")] : [])),
        };
        await using var anfitrion = await IniciarTienda(biblioteca, new AyudaFalsa());

        var (estado, html, _) = await Pedir(anfitrion, "/biblioteca", HostWeb.Tienda);

        Assert.Equal(codigo, estado);
        Assert.Contains(texto, html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-03")]
    public async Task Con_textos_legales_pendientes_la_biblioteca_lleva_a_aceptarlos()
    {
        var biblioteca = new BibliotecaDelClienteFalsa { Respuesta = Resultado<PaginaDeAccesos>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "textos-pendientes")) };
        await using var anfitrion = await IniciarTienda(biblioteca, new AyudaFalsa());

        var (estado, _, respuesta) = await Pedir(anfitrion, "/biblioteca", HostWeb.Tienda);

        Assert.Equal(HttpStatusCode.Redirect, estado);
        Assert.Equal("/textos/aceptar", respuesta.Headers.Location?.OriginalString);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-03")]
    public async Task Sin_navegador_el_fallo_sale_como_problem_json_con_el_codigo_de_biblioteca_y_sin_cache()
    {
        var biblioteca = new BibliotecaDelClienteFalsa { Respuesta = Resultado<PaginaDeAccesos>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "no-encontrado")) };
        await using var anfitrion = await IniciarTienda(biblioteca, new AyudaFalsa());

        var (estado, cuerpo, respuesta) = await Pedir(anfitrion, "/biblioteca", HostWeb.Tienda, html: false);

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal("ECO-NO-ENCONTRADO", JsonDocument.Parse(cuerpo).RootElement.GetProperty("code").GetString());
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    // ---------- Pedir ayuda ----------

    [Fact]
    [Trait("Criterio", "AC-BIB-14")]
    public async Task Pedir_ayuda_llama_a_la_aplicacion_con_el_motivo_y_vuelve_a_la_biblioteca()
    {
        var ayuda = new AyudaFalsa();
        await using var anfitrion = await IniciarTienda(new BibliotecaDelClienteFalsa(), ayuda);
        var acceso = Guid.NewGuid();

        using var respuesta = await Enviar(anfitrion, $"/biblioteca/{acceso:D}/ayuda", HostWeb.Tienda, ("motivo", "descargas-agotadas"));

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal("/biblioteca", respuesta.Headers.Location?.OriginalString);
        Assert.Equal((acceso, "descargas-agotadas"), Assert.Single(ayuda.Llamadas));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-14")]
    public async Task Pedir_ayuda_sin_motivo_o_con_campos_ajenos_se_rechaza_sin_llamar_a_la_aplicacion()
    {
        var ayuda = new AyudaFalsa();
        await using var anfitrion = await IniciarTienda(new BibliotecaDelClienteFalsa(), ayuda);

        using var sinMotivo = await Enviar(anfitrion, $"/biblioteca/{Guid.NewGuid():D}/ayuda", HostWeb.Tienda);
        using var conExtra = await Enviar(anfitrion, $"/biblioteca/{Guid.NewGuid():D}/ayuda", HostWeb.Tienda, ("motivo", "otro"), ("cuentaId", Guid.NewGuid().ToString("D")));

        Assert.Equal(HttpStatusCode.BadRequest, sinMotivo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, conExtra.StatusCode);
        Assert.Empty(ayuda.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-14")]
    public async Task Un_rechazo_de_la_aplicacion_al_pedir_ayuda_se_traduce_al_codigo_de_biblioteca()
    {
        var ayuda = new AyudaFalsa { Respuesta = Resultado<AyudaDeBiblioteca>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, "acceso-no-disponible")) };
        await using var anfitrion = await IniciarTienda(new BibliotecaDelClienteFalsa(), ayuda);

        using var respuesta = await Enviar(anfitrion, $"/biblioteca/{Guid.NewGuid():D}/ayuda", HostWeb.Tienda, ("motivo", "otro"));

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Contains("ECO-BIB-ACCESO-NO-DISPONIBLE", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-BIB-05")]
    [InlineData("limite-de-descargas", CategoriaDeError.Conflicto, HttpStatusCode.Conflict, "Alcanzaste el límite de descargas", "ECO-BIB-LIMITE-DE-DESCARGAS")]
    [InlineData("archivo-no-disponible", CategoriaDeError.NoDisponible, HttpStatusCode.ServiceUnavailable, "No pudimos preparar tu archivo", "ECO-BIB-ARCHIVO-NO-DISPONIBLE")]
    [InlineData("acceso-no-disponible", CategoriaDeError.Conflicto, HttpStatusCode.Conflict, "Este libro no se puede descargar ahora", "ECO-BIB-ACCESO-NO-DISPONIBLE")]
    public async Task La_descarga_rechazada_explica_el_limite_o_el_archivo_y_lleva_a_pedir_ayuda(string clave, CategoriaDeError categoria, HttpStatusCode codigo, string texto, string codigoDeSoporte)
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar(
            s => s.AddSingleton<IDescargasDeLaBiblioteca>(new DescargasFalsa(Resultado<EnlaceDeDescarga>.Fallo(new ErrorDeAplicacion(categoria, clave)))),
            partes: [typeof(PantallasDeBibliotecaTests).Assembly]);
        using var formulario = await anfitrion.Cliente.GetAsync($"/prueba/boton-de-descarga/{Guid.NewGuid():D}");
        var html = await formulario.Content.ReadAsStringAsync();
        var token = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<v>[^\"]+)\"").Groups["v"].Value;
        var claveDeUnSoloUso = System.Text.RegularExpressions.Regex.Match(html, "name=\"clave\" value=\"(?<v>[0-9a-f]{32})\"").Groups["v"].Value;
        var cookies = string.Join("; ", formulario.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, $"/biblioteca/{Guid.NewGuid():D}/descargar")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["clave"] = claveDeUnSoloUso }),
        };
        solicitud.Headers.Add("Cookie", cookies);

        using var respuesta = await anfitrion.Cliente.SendAsync(solicitud);
        var cuerpo = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());

        Assert.Equal(codigo, respuesta.StatusCode);
        Assert.Contains(texto, cuerpo, StringComparison.Ordinal);
        Assert.Contains(codigoDeSoporte, cuerpo, StringComparison.Ordinal);
        Assert.Null(respuesta.Headers.Location);
        Assert.Contains("href=\"/biblioteca\"", cuerpo, StringComparison.Ordinal);
    }

    private sealed class DescargasFalsa(Resultado<EnlaceDeDescarga> respuesta) : IDescargasDeLaBiblioteca
    {
        public Task<Resultado<EnlaceDeDescarga>> SolicitarEnlace(Guid accesoId, string claveDeIdempotencia, CancellationToken cancelacion = default) => Task.FromResult(respuesta);
    }

    // ---------- Administración ----------

    [Fact]
    [Trait("Criterio", "AC-BIB-10")]
    public async Task La_biblioteca_de_un_cliente_avisa_que_la_consulta_queda_auditada_y_ofrece_restituir_solo_donde_hay_descargas_usadas()
    {
        var cuenta = Guid.NewGuid();
        var conUso = Libro(null, "Libro con uso", 3);
        var sinUso = Libro(null, "Libro sin uso", 0);
        var revocado = Libro(null, "Libro revocado", 2, EstadoDelAccesoDigital.Revocado);
        var administracion = new AdministracionFalsa { RespuestaDelCliente = Resultado<IReadOnlyList<AccesoDeBiblioteca>>.Exito([conUso, sinUso, revocado]) };
        await using var anfitrion = await IniciarAdministracion(administracion);

        var (estado, html, respuesta) = await Pedir(anfitrion, $"/administracion/clientes/{cuenta:D}/biblioteca", HostWeb.Gestion);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal($"cliente:{cuenta:D}", Assert.Single(administracion.Llamadas));
        Assert.Contains("queda registrada en la auditoría", html, StringComparison.Ordinal);
        Assert.Contains("3 de 5 descargas usadas", html, StringComparison.Ordinal);
        Assert.Contains($"action=\"/administracion/biblioteca/accesos/{conUso.AccesoId:D}/restituir\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"/accesos/{sinUso.AccesoId:D}/restituir", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"/accesos/{revocado.AccesoId:D}/restituir", html, StringComparison.Ordinal);
        Assert.Contains("La descarga falló o se interrumpió", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-10")]
    public async Task Quien_no_es_administracion_central_recibe_el_rechazo_de_la_aplicacion_como_403_y_sin_cache()
    {
        var administracion = new AdministracionFalsa { RespuestaDelCliente = Resultado<IReadOnlyList<AccesoDeBiblioteca>>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "prohibido")) };
        await using var anfitrion = await IniciarAdministracion(administracion);

        var (estado, cuerpo, respuesta) = await Pedir(anfitrion, $"/administracion/clientes/{Guid.NewGuid():D}/biblioteca", HostWeb.Gestion, html: false);

        Assert.Equal(HttpStatusCode.Forbidden, estado);
        Assert.Equal("ECO-PROHIBIDO", JsonDocument.Parse(cuerpo).RootElement.GetProperty("code").GetString());
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-14")]
    public async Task Las_ayudas_se_listan_sin_datos_de_la_cuenta_con_filtro_y_con_las_acciones_de_restituir_y_cerrar()
    {
        var abierta = new AyudaDeBiblioteca(Guid.NewGuid(), Guid.NewGuid(), "descargas-agotadas", EstadoDeLaAyudaDeBiblioteca.Abierta, Compra, null);
        var cerrada = new AyudaDeBiblioteca(Guid.NewGuid(), Guid.NewGuid(), "archivo-no-abre", EstadoDeLaAyudaDeBiblioteca.Cerrada, Compra, Compra.AddHours(2));
        var administracion = new AdministracionFalsa { RespuestaDeAyudas = Resultado<PaginaDeAyudas>.Exito(new PaginaDeAyudas([abierta, cerrada], 1, 20, 2)) };
        await using var anfitrion = await IniciarAdministracion(administracion);

        var (_, html, _) = await Pedir(anfitrion, "/administracion/biblioteca/ayudas?estado=todas", HostWeb.Gestion);

        Assert.Null(administracion.UltimoFiltro);
        Assert.Contains("Se agotaron mis descargas", html, StringComparison.Ordinal);
        Assert.Contains("El archivo no abre", html, StringComparison.Ordinal);
        Assert.Contains($"action=\"/administracion/biblioteca/accesos/{abierta.AccesoId:D}/restituir\"", html, StringComparison.Ordinal);
        Assert.Contains($"name=\"ayudaId\" value=\"{abierta.AyudaId:D}\"", html, StringComparison.Ordinal);
        Assert.Contains($"action=\"/administracion/biblioteca/ayudas/{abierta.AyudaId:D}/cerrar\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"/ayudas/{cerrada.AyudaId:D}/cerrar", html, StringComparison.Ordinal);
        // El pie de toda página lleva ahora el correo de contacto del comercio (COM-02); lo que no debe aparecer es el correo de una cuenta en el contenido.
        var contenido = html[..html.IndexOf("<footer", StringComparison.OrdinalIgnoreCase)];
        Assert.DoesNotContain("correo", contenido, StringComparison.OrdinalIgnoreCase);

        await Pedir(anfitrion, "/administracion/biblioteca/ayudas", HostWeb.Gestion);
        Assert.Equal(EstadoDeLaAyudaDeBiblioteca.Abierta, administracion.UltimoFiltro);
        var (estado, _, _) = await Pedir(anfitrion, "/administracion/biblioteca/ayudas?estado=otro", HostWeb.Gestion);
        Assert.Equal(HttpStatusCode.BadRequest, estado);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-07")]
    public async Task Restituir_llama_a_la_aplicacion_con_la_cantidad_el_motivo_y_la_ayuda_y_vuelve_solo_a_una_pantalla_de_la_administracion()
    {
        var administracion = new AdministracionFalsa();
        await using var anfitrion = await IniciarAdministracion(administracion);
        var acceso = Guid.NewGuid();
        var ayuda = Guid.NewGuid();
        var cuenta = Guid.NewGuid();

        using var desdeAyudas = await Enviar(anfitrion, $"/administracion/biblioteca/accesos/{acceso:D}/restituir", HostWeb.Gestion, ("cantidad", "1"), ("motivo", "descarga-fallida"), ("ayudaId", ayuda.ToString("D")));
        Assert.Equal(HttpStatusCode.SeeOther, desdeAyudas.StatusCode);
        Assert.Equal("/administracion/biblioteca/ayudas", desdeAyudas.Headers.Location?.OriginalString);
        Assert.Equal(new RestituirDescargasEntrada(acceso, 1, "descarga-fallida", ayuda), administracion.UltimaRestitucion);

        using var desdeCliente = await Enviar(anfitrion, $"/administracion/biblioteca/accesos/{acceso:D}/restituir", HostWeb.Gestion, ("cantidad", "2"), ("motivo", "otro"), ("cuentaId", cuenta.ToString("D")));
        Assert.Equal($"/administracion/clientes/{cuenta:D}/biblioteca", desdeCliente.Headers.Location?.OriginalString);
        Assert.Equal(new RestituirDescargasEntrada(acceso, 2, "otro", null), administracion.UltimaRestitucion);
    }

    [Theory]
    [Trait("Criterio", "AC-BIB-07")]
    [InlineData("cantidad", "uno")]
    [InlineData("cantidad", "-1")]
    [InlineData("cantidad", "")]
    [InlineData("cantidad", "1000")]
    [InlineData("ayudaId", "no-es-un-guid")]
    [InlineData("cuentaId", "no-es-un-guid")]
    public async Task Restituir_con_un_campo_mal_formado_se_rechaza_sin_llamar_a_la_aplicacion(string campo, string valor)
    {
        var administracion = new AdministracionFalsa();
        await using var anfitrion = await IniciarAdministracion(administracion);
        var campos = new Dictionary<string, string> { ["cantidad"] = "1", ["motivo"] = "otro" };
        campos[campo] = valor;

        using var respuesta = await Enviar(anfitrion, $"/administracion/biblioteca/accesos/{Guid.NewGuid():D}/restituir", HostWeb.Gestion, [.. campos.Select(c => (c.Key, c.Value))]);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(administracion.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-07")]
    public async Task Restituir_sin_motivo_o_sin_cantidad_se_rechaza_y_un_rechazo_de_la_aplicacion_se_traduce()
    {
        var administracion = new AdministracionFalsa();
        await using var anfitrion = await IniciarAdministracion(administracion);
        var ruta = $"/administracion/biblioteca/accesos/{Guid.NewGuid():D}/restituir";

        using var sinMotivo = await Enviar(anfitrion, ruta, HostWeb.Gestion, ("cantidad", "1"));
        using var sinCantidad = await Enviar(anfitrion, ruta, HostWeb.Gestion, ("motivo", "otro"));
        Assert.Equal(HttpStatusCode.BadRequest, sinMotivo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinCantidad.StatusCode);
        Assert.Empty(administracion.Llamadas);

        administracion.RespuestaDeRestitucion = Resultado<AccesoDeBiblioteca>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "prohibido"));
        using var prohibido = await Enviar(anfitrion, ruta, HostWeb.Gestion, ("cantidad", "1"), ("motivo", "otro"));
        Assert.Equal(HttpStatusCode.Forbidden, prohibido.StatusCode);
        Assert.Contains("ECO-PROHIBIDO", await prohibido.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-14")]
    public async Task Cerrar_una_ayuda_llama_a_la_aplicacion_y_vuelve_a_la_lista_y_un_post_sin_token_se_rechaza()
    {
        var administracion = new AdministracionFalsa();
        await using var anfitrion = await IniciarAdministracion(administracion);
        var ayuda = Guid.NewGuid();

        using var cerrada = await Enviar(anfitrion, $"/administracion/biblioteca/ayudas/{ayuda:D}/cerrar", HostWeb.Gestion);
        using var sinToken = new HttpRequestMessage(HttpMethod.Post, $"/administracion/biblioteca/ayudas/{ayuda:D}/cerrar");
        sinToken.Headers.Add("Cookie", AnfitrionConSesion.Cookie(HostWeb.Gestion));
        using var rechazada = await anfitrion.Cliente.SendAsync(sinToken);

        Assert.Equal(HttpStatusCode.SeeOther, cerrada.StatusCode);
        Assert.Equal("/administracion/biblioteca/ayudas", cerrada.Headers.Location?.OriginalString);
        Assert.Equal($"cerrar:{ayuda:D}", Assert.Single(administracion.Llamadas));
        Assert.NotEqual(HttpStatusCode.SeeOther, rechazada.StatusCode);
    }
}
