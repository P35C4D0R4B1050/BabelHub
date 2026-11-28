using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.IntegrationTests.Carrito;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// T34: la revisión del checkout y la entrega sobre el anfitrión web con las vistas reales (AC-CAR-04, AC-CAR-05, AC-CAR-07, AC-CAR-08, AC-CAR-21, AC-CAR-26): cada línea con su disponibilidad (en su sucursal, por traslado con plazo o «a confirmar», agotada, sin poder validar), los pasos (cinco o cuatro
/// si es solo digital), recojo o domicilio con un formulario cada uno, los errores junto a cada campo con lo escrito solo en esa respuesta, el envío y el total, y el aviso de que no se puede validar el stock.
/// </summary>
public sealed class PantallasDelCheckoutTests
{
    private static readonly Guid Pedido = Guid.Parse("f0000000-0000-0000-0000-000000000001");

    private sealed class ListaDePrueba : IListaDeSucursales
    {
        public Task<ListaDeSucursales?> Obtener(CancellationToken cancelacion = default) => Task.FromResult<ListaDeSucursales?>(new ListaDeSucursales([new("AYA", "Ayacucho"), new("LIM", "Lima")], true));
    }

    private sealed class ActorFijo(ActorActual actual) : IActorActual
    {
        public ActorActual? Actual { get; } = actual;
    }

    private static LineaDeRevision Linea(string titulo, FormatoDeEdicion formato, int cantidad, decimal precio, DisponibilidadDeLineaDeCheckout disponibilidad, int? plazo = null, bool aConfirmar = false, bool retirada = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), titulo, formato, cantidad, precio, precio * cantidad, disponibilidad, plazo, aConfirmar, retirada, formato == FormatoDeEdicion.Digital ? null : "AYA", formato == FormatoDeEdicion.Digital ? null : "Ayacucho");

    private static VistaDelCheckout Vista(
        IReadOnlyList<LineaDeRevision> lineas,
        int paso = 1,
        int total = 5,
        TipoDeEntregaDeCompra? tipo = null,
        decimal? envio = null,
        EstadoVisibleDelCheckout estado = EstadoVisibleDelCheckout.EnEdicion,
        Guid? pedido = null,
        ContactoDeCompra? contacto = null,
        bool carritoCambio = false)
    {
        var subtotal = lineas.Sum(l => l.Importe);
        return new VistaDelCheckout(estado, paso, total, lineas, tipo, tipo == TipoDeEntregaDeCompra.Recojo ? "LIM" : null, tipo == TipoDeEntregaDeCompra.Domicilio ? "ayacucho" : null, subtotal, envio, subtotal + (envio ?? 0m), contacto, [], [], null, pedido, carritoCambio);
    }

    private static readonly LineaDeRevision[] Mixto =
    [
        Linea("Estructuras de datos", FormatoDeEdicion.Impresa, 2, 60m, DisponibilidadDeLineaDeCheckout.Disponible),
        Linea("Cien años de soledad", FormatoDeEdicion.Digital, 1, 20m, DisponibilidadDeLineaDeCheckout.DisponibleAlInstante),
    ];

    private static async Task<AnfitrionWeb> Iniciar(CheckoutDelClienteFalso falso, ExigenciaDeVerificacionFalsa? exigencia = null, ITextosLegales? textos = null)
    {
        var actor = new ActorFijo(ActorActual.DeUsuario(Guid.NewGuid(), "cliente", null));
        return await AnfitrionWeb.Iniciar(servicios =>
        {
            servicios.AddSingleton<ICheckoutDelCliente>(falso);
            servicios.AddSingleton<IExigenciaDeVerificacionDePersona>(exigencia ?? new ExigenciaDeVerificacionFalsa());
            servicios.AddSingleton<ITextosLegales>(textos ?? new TextosDelCarrito());
            servicios.AddSingleton<IActorActual>(actor);
            servicios.AddSingleton<IListaDeSucursales>(new ListaDePrueba());
            servicios.AddSingleton(new OpcionesDeVerificacionWeb("clave-de-sitio-de-prueba"));
            servicios.AddSingleton(new OpcionesDelHostWeb(HostWeb.Tienda, []));
            servicios.AgregarProteccionContraFalsificacion();
        });
    }

    private static CheckoutDelClienteFalso Con(VistaDelCheckout vista) => new() { RespuestaDeVista = Resultado<VistaDelCheckout>.Exito(vista) };

    private static async Task<(string Html, HttpResponseMessage Respuesta)> Pedir(AnfitrionWeb anfitrion, string ruta = "/checkout")
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);
        return (WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()), respuesta);
    }

    /// <summary>Un <c>POST</c> de formulario de un navegador: lee el token y la cookie antifalsificación de la pantalla y los manda con el origen de la propia página.</summary>
    private static async Task<(string Html, HttpResponseMessage Respuesta, string Crudo)> Enviar(AnfitrionWeb anfitrion, string ruta, params KeyValuePair<string, string>[] campos)
    {
        using var pantalla = await anfitrion.Cliente.SendAsync(Conaccept(HttpMethod.Get, "/checkout"));
        var html = await pantalla.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        var cookie = pantalla.Headers.GetValues("Set-Cookie").First(c => c.StartsWith(ProteccionContraFalsificacion.NombreDeLaCookie + "=", StringComparison.Ordinal)).Split(';')[0];
        using var solicitud = Conaccept(HttpMethod.Post, ruta);
        solicitud.Headers.TryAddWithoutValidation("Cookie", cookie);
        solicitud.Headers.TryAddWithoutValidation("Origin", "https://localhost");
        solicitud.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
        solicitud.Content = new FormUrlEncodedContent([.. campos, new("__RequestVerificationToken", token)]);
        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);
        var crudo = await respuesta.Content.ReadAsStringAsync();
        return (WebUtility.HtmlDecode(crudo), respuesta, crudo);
    }

    private static HttpRequestMessage Conaccept(HttpMethod metodo, string ruta)
    {
        var solicitud = new HttpRequestMessage(metodo, ruta);
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        return solicitud;
    }

    // ---------- Revisión ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-04")]
    public async Task Cada_linea_dice_su_disponibilidad_con_texto_e_icono_y_el_plazo_del_traslado_o_a_confirmar()
    {
        var lineas = new[]
        {
            Linea("Libro en sucursal", FormatoDeEdicion.Impresa, 1, 10m, DisponibilidadDeLineaDeCheckout.EnSuSucursal),
            Linea("Libro con traslado", FormatoDeEdicion.Impresa, 1, 10m, DisponibilidadDeLineaDeCheckout.PorTraslado, plazo: 3),
            Linea("Libro de un dia", FormatoDeEdicion.Impresa, 1, 10m, DisponibilidadDeLineaDeCheckout.PorTraslado, plazo: 1),
            Linea("Libro sin plazo", FormatoDeEdicion.Impresa, 1, 10m, DisponibilidadDeLineaDeCheckout.PorTraslado, aConfirmar: true),
            Linea("Libro agotado", FormatoDeEdicion.Impresa, 1, 10m, DisponibilidadDeLineaDeCheckout.Agotada),
            Linea("Libro digital", FormatoDeEdicion.Digital, 1, 10m, DisponibilidadDeLineaDeCheckout.DisponibleAlInstante),
        };
        await using var anfitrion = await Iniciar(Con(Vista(lineas, tipo: TipoDeEntregaDeCompra.Recojo, envio: 0m)));

        var (html, _) = await Pedir(anfitrion);

        Assert.Contains("Disponible en su sucursal", html, StringComparison.Ordinal);
        Assert.Contains("Se trae de otra sucursal", html, StringComparison.Ordinal);
        Assert.Contains("Plazo estimado: 3 días", html, StringComparison.Ordinal);
        Assert.Contains("Plazo estimado: 1 día", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Plazo estimado: 1 días", html, StringComparison.Ordinal);
        Assert.Contains("Plazo a confirmar", html, StringComparison.Ordinal);
        Assert.Contains("Agotado", html, StringComparison.Ordinal);
        Assert.Contains("Disponible al instante", html, StringComparison.Ordinal);
        Assert.Contains("Alguna línea ya no se puede comprar", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-05")]
    public async Task Sin_poder_validar_el_stock_se_avisa_que_no_se_cobra_nada_y_se_ofrece_reintentar()
    {
        var lineas = new[] { Linea("Libro", FormatoDeEdicion.Impresa, 1, 10m, DisponibilidadDeLineaDeCheckout.NoSePuedeValidar) };
        await using var anfitrion = await Iniciar(Con(Vista(lineas)));

        var (html, _) = await Pedir(anfitrion);

        Assert.Contains("No podemos validar el stock de los impresos ahora", html, StringComparison.Ordinal);
        Assert.Contains("no cobraremos nada", html, StringComparison.Ordinal);
        Assert.Contains("Reintentar", html, StringComparison.Ordinal);
        Assert.Contains("No se puede validar el stock ahora", html, StringComparison.Ordinal);
    }

    // ---------- Pasos y resumen ----------

    [Fact]
    public async Task Los_pasos_son_cinco_con_impresos_y_cuatro_si_es_solo_digital_y_el_actual_lleva_aria_current()
    {
        await using var mixto = await Iniciar(Con(Vista(Mixto, paso: 2)));
        await using var digital = await Iniciar(Con(Vista([Linea("Digital", FormatoDeEdicion.Digital, 1, 20m, DisponibilidadDeLineaDeCheckout.DisponibleAlInstante)], paso: 2, total: 4)));

        var (conImpresos, _) = await Pedir(mixto);
        var (soloDigital, _) = await Pedir(digital);

        Assert.Equal(5, Regex.Matches(conImpresos, "class=\"step ").Count);
        Assert.Matches("aria-current=\"step\">Entrega", conImpresos);
        Assert.Contains("Revisar (listo)", conImpresos, StringComparison.Ordinal);
        Assert.Equal(4, Regex.Matches(soloDigital, "class=\"step ").Count);
        Assert.DoesNotContain(">Entrega", soloDigital, StringComparison.Ordinal);
        Assert.Matches("aria-current=\"step\">Tus datos", soloDigital);
    }

    [Theory]
    [InlineData(null, null, "Se calcula al elegir la entrega")]
    [InlineData(TipoDeEntregaDeCompra.Recojo, 0, "Gratis (recojo)")]
    // La Aplicación no informa envío en un recojo (null): la pantalla igual dice que el recojo es gratis.
    [InlineData(TipoDeEntregaDeCompra.Recojo, null, "Gratis (recojo)")]
    [InlineData(TipoDeEntregaDeCompra.Domicilio, 12, "domicilio")]
    [Trait("Criterio", "AC-CAR-21")]
    public async Task El_envio_y_el_total_salen_del_servidor_con_formato_peruano(TipoDeEntregaDeCompra? tipo, int? envio, string textoDelEnvio)
    {
        await using var anfitrion = await Iniciar(Con(Vista(Mixto, tipo: tipo, envio: envio)));

        var (html, _) = await Pedir(anfitrion);

        Assert.Contains(textoDelEnvio == "domicilio" ? FormatoPeruano.Soles(12m) : textoDelEnvio, html, StringComparison.Ordinal);
        Assert.Contains(FormatoPeruano.Soles(140m + (envio ?? 0)), html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Una_compra_solo_digital_no_ofrece_entrega_y_dice_que_no_aplica_el_envio()
    {
        var digital = new[] { Linea("Digital", FormatoDeEdicion.Digital, 1, 20m, DisponibilidadDeLineaDeCheckout.DisponibleAlInstante) };
        await using var anfitrion = await Iniciar(Con(Vista(digital, total: 4)));

        var (html, _) = await Pedir(anfitrion);

        Assert.DoesNotContain("action=\"/checkout/entrega\"", html, StringComparison.Ordinal);
        Assert.Contains("Compra 100 % digital: sin entrega.", html, StringComparison.Ordinal);
        Assert.Contains("No aplica (solo digitales)", html, StringComparison.Ordinal);
    }

    // ---------- Entrega ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-07")]
    public async Task La_entrega_ofrece_el_recojo_en_la_sucursal_de_cada_libro_sin_elegir_otra_y_el_domicilio_con_sus_campos_etiquetados()
    {
        await using var anfitrion = await Iniciar(Con(Vista(Mixto)));

        var (html, _) = await Pedir(anfitrion);

        Assert.Equal(2, Regex.Matches(html, "action=\"/checkout/entrega\"").Count);
        Assert.Contains("name=\"tipoDeEntrega\" value=\"recojo\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"tipoDeEntrega\" value=\"domicilio\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"codigoDeSucursal\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<option value=\"AYA\"", html, StringComparison.Ordinal);
        Assert.Contains("cada libro se recoge en la sucursal que lo atiende", html, StringComparison.Ordinal);
        Assert.Contains("2 × Estructuras de datos — <strong>Ayacucho</strong>", html, StringComparison.Ordinal);
        foreach (var campo in new[] { "nombre", "telefono", "direccion", "region" })
        {
            Assert.Contains($"id=\"entrega-{campo}\"", html, StringComparison.Ordinal);
            Assert.Contains($"for=\"entrega-{campo}\"", html, StringComparison.Ordinal);
        }

        Assert.Contains("autocomplete=\"name\"", html, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"tel-national\"", html, StringComparison.Ordinal);
        Assert.Contains("inputmode=\"tel\"", html, StringComparison.Ordinal);
        Assert.True(Regex.Matches(html, "name=\"__RequestVerificationToken\"").Count >= 3);
        Assert.Contains("Tus libros digitales no necesitan entrega", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_entrega_elegida_se_marca_con_texto_y_el_contacto_guardado_rellena_el_formulario_de_domicilio()
    {
        var contacto = new ContactoDeCompra("Ana Pérez Quispe", "987654321", "Jr. Lima 123", "ayacucho");
        await using var anfitrion = await Iniciar(Con(Vista(Mixto, paso: 3, tipo: TipoDeEntregaDeCompra.Domicilio, envio: 12m, contacto: contacto)));

        var (html, _) = await Pedir(anfitrion);

        Assert.Contains("Elegido", html, StringComparison.Ordinal);
        Assert.Contains("value=\"Ana Pérez Quispe\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"987654321\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"Jr. Lima 123\"", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-26")]
    public async Task Un_error_de_validacion_se_dice_junto_al_campo_con_su_aria_y_lo_escrito_solo_vive_en_esa_respuesta()
    {
        var falso = Con(Vista(Mixto));
        falso.RespuestaDeVista = Resultado<VistaDelCheckout>.Exito(Vista(Mixto));
        await using var anfitrion = await Iniciar(falso);
        falso.RespuestaDeVistaPorLlamada = llamada => llamada == "entrega"
            ? Resultado<VistaDelCheckout>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo("telefono", "formato-invalido"), new ErrorDeCampo("region", "obligatorio")]))
            : null;

        var (html, respuesta, crudo) = await Enviar(
            anfitrion,
            "/checkout/entrega",
            new("tipoDeEntrega", "domicilio"),
            new("nombre", "<b>Ana</b> Pérez"),
            new("telefono", "123"),
            new("direccion", "Jr. Lima 123"),
            new("region", string.Empty));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("Revisa los datos", html, StringComparison.Ordinal);
        Assert.Contains("id=\"error-telefono\"", html, StringComparison.Ordinal);
        Assert.Contains("Escribe un celular de 9 dígitos que empiece con 9.", html, StringComparison.Ordinal);
        Assert.Contains("Escribe la región de entrega.", html, StringComparison.Ordinal);
        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"error-telefono\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"Jr. Lima 123\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>Ana</b>", crudo, StringComparison.Ordinal);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    // ---------- Otros estados ----------

    [Fact]
    public async Task Con_el_pago_en_curso_la_pantalla_lleva_al_pago_y_no_ofrece_cambiar_la_entrega()
    {
        await using var anfitrion = await Iniciar(Con(Vista(Mixto, paso: 4, tipo: TipoDeEntregaDeCompra.Recojo, envio: 0m, estado: EstadoVisibleDelCheckout.EnPago, pedido: Pedido)));

        var (html, _) = await Pedir(anfitrion);

        Assert.Contains("Tu compra está lista para pagar", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/checkout/pago\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("action=\"/checkout/entrega\"", html, StringComparison.Ordinal);
        Assert.Contains($"name=\"pedidoId\" value=\"{Pedido:D}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_carrito_que_cambio_avisa_y_un_titulo_con_marcado_se_codifica()
    {
        var lineas = new[] { Linea("<script>alert(1)</script>", FormatoDeEdicion.Impresa, 1, 10m, DisponibilidadDeLineaDeCheckout.Disponible) };
        await using var anfitrion = await Iniciar(Con(Vista(lineas, carritoCambio: true)));

        using var respuesta = await anfitrion.Cliente.SendAsync(Conaccept(HttpMethod.Get, "/checkout"));
        var crudo = await respuesta.Content.ReadAsStringAsync();

        Assert.Contains("Tu carrito cambió", crudo, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert(1)</script>", crudo, StringComparison.Ordinal);
    }

    // ---------- Datos y aceptaciones (T35) ----------

    private sealed class TextosCaidos : ITextosLegales
    {
        public Task<Resultado<TextoLegal>> Obtener(TipoDeTextoLegal tipo, CancellationToken cancelacion = default) =>
            Task.FromResult(Resultado<TextoLegal>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "texto-no-disponible")));
    }

    private static VistaDelCheckout ConEntrega(IReadOnlyList<LineaDeRevision> lineas, IReadOnlyList<AceptacionDeCompra>? aceptaciones = null, ConsentimientoDeWhatsAppDeCompra? consentimiento = null)
    {
        var subtotal = lineas.Sum(l => l.Importe);
        return new VistaDelCheckout(EstadoVisibleDelCheckout.EnEdicion, 3, 5, lineas, TipoDeEntregaDeCompra.Recojo, "LIM", null, subtotal, 0m, subtotal, null, aceptaciones ?? [], [], null, null, false, consentimiento);
    }

    private static KeyValuePair<string, string>[] CamposDePago(params KeyValuePair<string, string>[] extra) =>
        [new("terminosVersion", "t-1"), new("privacidadVersion", "p-1"), new("reembolsoDigitalVersion", "r-1"), new("totalVisto", "140.00"), .. extra];

    [Fact]
    [Trait("Criterio", "AC-CAR-11")]
    public async Task Despues_de_elegir_la_entrega_se_ofrecen_los_textos_con_su_version_vigente_el_total_visto_y_el_desafio_de_persona()
    {
        await using var anfitrion = await Iniciar(Con(ConEntrega(Mixto)));

        var (html, _) = await Pedir(anfitrion);

        Assert.Contains("action=\"/checkout\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"terminosVersion\" value=\"t-1\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"privacidadVersion\" value=\"p-1\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"reembolsoDigitalVersion\" value=\"r-1\"", html, StringComparison.Ordinal);
        Assert.Contains("Acepto los términos y condiciones.", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/legal/terminos\" target=\"_blank\" rel=\"noopener\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"totalVisto\" value=\"140.00\"", html, StringComparison.Ordinal);
        Assert.Contains("cf-turnstile", html, StringComparison.Ordinal);
        Assert.Contains("clave-de-sitio-de-prueba", html, StringComparison.Ordinal);
        Assert.Contains("Ir al pago · " + FormatoPeruano.Soles(140m), html, StringComparison.Ordinal);
        Assert.Contains("15 minutos", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_entrega_elegida_no_se_ofrece_pagar_y_una_compra_solo_digital_si_con_su_politica_de_reembolso()
    {
        await using var sinEntrega = await Iniciar(Con(Vista(Mixto)));
        var digital = new[] { Linea("Digital", FormatoDeEdicion.Digital, 1, 20m, DisponibilidadDeLineaDeCheckout.DisponibleAlInstante) };
        await using var soloDigital = await Iniciar(Con(Vista(digital, paso: 2, total: 4)));

        var (antes, _) = await Pedir(sinEntrega);
        var (conDigital, _) = await Pedir(soloDigital);

        Assert.DoesNotContain("Tus datos y aceptaciones", antes, StringComparison.Ordinal);
        Assert.Contains("Tus datos y aceptaciones", conDigital, StringComparison.Ordinal);
        Assert.Contains("la política de cambios y devoluciones (reembolso de libros digitales)", conDigital, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_texto_ya_aceptado_en_su_version_vigente_sale_marcado_y_uno_de_otra_version_no()
    {
        var aceptadas = new[]
        {
            new AceptacionDeCompra(TextoLegalDeCompra.Terminos, "t-1", DateTimeOffset.UtcNow),
            new AceptacionDeCompra(TextoLegalDeCompra.Privacidad, "p-0", DateTimeOffset.UtcNow),
        };
        await using var anfitrion = await Iniciar(Con(ConEntrega(Mixto, aceptadas)));

        var (html, _) = await Pedir(anfitrion);

        Assert.Matches("name=\"terminosVersion\" value=\"t-1\" checked", html);
        Assert.DoesNotMatch("name=\"privacidadVersion\" value=\"p-1\" checked", html);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-16")]
    public async Task El_consentimiento_de_WhatsApp_es_opcional_sin_marcar_con_la_huella_y_solo_si_se_ofrece()
    {
        await using var seOfrece = await Iniciar(Con(ConEntrega(Mixto, consentimiento: new ConsentimientoDeWhatsAppDeCompra(EstadoDelConsentimientoDeCompra.SeOfrece, "***321"))));
        await using var noSeOfrece = await Iniciar(Con(ConEntrega(Mixto, consentimiento: new ConsentimientoDeWhatsAppDeCompra(EstadoDelConsentimientoDeCompra.NoSeOfrece))));
        await using var yaConsiente = await Iniciar(Con(ConEntrega(Mixto, consentimiento: new ConsentimientoDeWhatsAppDeCompra(EstadoDelConsentimientoDeCompra.YaConsiente))));

        var (ofrecido, _) = await Pedir(seOfrece);
        var (ausente, _) = await Pedir(noSeOfrece);
        var (existente, _) = await Pedir(yaConsiente);

        Assert.Contains("name=\"consentimientoDeWhatsApp\" value=\"true\"", ofrecido, StringComparison.Ordinal);
        Assert.DoesNotMatch("name=\"consentimientoDeWhatsApp\" value=\"true\" checked", ofrecido);
        Assert.Contains("name=\"huellaDelNumero\" value=\"***321\"", ofrecido, StringComparison.Ordinal);
        Assert.Contains("al número ***321 (opcional", ofrecido, StringComparison.Ordinal);
        Assert.DoesNotContain("consentimientoDeWhatsApp", ausente, StringComparison.Ordinal);
        Assert.Contains("Ya recibes avisos de tus pedidos por WhatsApp.", existente, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Con_un_problema_en_las_lineas_o_sin_los_textos_el_boton_de_pagar_queda_inactivo_y_se_explica()
    {
        var agotada = new[] { Linea("Libro", FormatoDeEdicion.Impresa, 1, 10m, DisponibilidadDeLineaDeCheckout.Agotada) };
        await using var conProblema = await Iniciar(Con(ConEntrega(agotada)));
        await using var sinTextos = await Iniciar(Con(ConEntrega(Mixto)), textos: new TextosCaidos());

        var (problema, _) = await Pedir(conProblema);
        var (caidos, _) = await Pedir(sinTextos);

        Assert.Matches("<button type=\"submit\" class=\"btn btn-primary\" disabled", problema);
        Assert.Contains("Resuelve los avisos de arriba para pasar al pago.", problema, StringComparison.Ordinal);
        Assert.Matches("<button type=\"submit\" class=\"btn btn-primary\" disabled", caidos);
        Assert.Contains("No podemos mostrar los textos legales ahora", caidos, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-27")]
    public async Task Pasar_al_pago_con_exito_redirige_a_la_pantalla_de_Pagos_con_un_303()
    {
        await using var anfitrion = await Iniciar(Con(ConEntrega(Mixto)));

        var (_, respuesta, _) = await Enviar(anfitrion, "/checkout", CamposDePago());

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal("/checkout/pago", respuesta.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("checkout-total-cambio", CategoriaDeError.Conflicto, 409, "El total cambió")]
    [InlineData("checkout-stock-no-validable", CategoriaDeError.NoDisponible, 503, "No podemos validar el stock ahora")]
    [InlineData("checkout-cuenta-no-puede-comprar", CategoriaDeError.Prohibido, 403, "Tu cuenta no puede comprar ahora")]
    public async Task Cada_bloqueo_del_paso_de_reserva_se_dice_en_la_misma_pantalla_con_su_texto_y_el_carrito_conservado(string clave, CategoriaDeError categoria, int estado, string titulo)
    {
        var falso = Con(ConEntrega(Mixto));
        falso.RespuestaDePago = Resultado<ResultadoDePasarAlPago>.Fallo(new ErrorDeAplicacion(categoria, clave));
        await using var anfitrion = await Iniciar(falso);

        var (html, respuesta, _) = await Enviar(anfitrion, "/checkout", CamposDePago());

        Assert.Equal(estado, (int)respuesta.StatusCode);
        Assert.Contains(titulo, html, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
        Assert.Contains("Estructuras de datos", html, StringComparison.Ordinal);
        Assert.Contains("Ir al pago · ", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-14")]
    public async Task El_limite_de_reservas_dice_a_que_hora_de_Lima_se_puede_reintentar()
    {
        var hasta = new DateTimeOffset(2030, 5, 1, 20, 42, 0, TimeSpan.Zero);
        var falso = Con(ConEntrega(Mixto));
        falso.RespuestaDePago = Resultado<ResultadoDePasarAlPago>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Limite, "limite-excedido", [new ErrorDeCampo("disponibleDesde", hasta.ToString("O", System.Globalization.CultureInfo.InvariantCulture))]));
        await using var anfitrion = await Iniciar(falso);

        var (html, respuesta, _) = await Enviar(anfitrion, "/checkout", CamposDePago());

        Assert.Equal(HttpStatusCode.TooManyRequests, respuesta.StatusCode);
        Assert.Contains("Podrás intentarlo de nuevo a las 15:42 (hora de Lima)", html, StringComparison.Ordinal);
        Assert.True(respuesta.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Falta_aceptar_un_texto_se_dice_junto_a_su_casilla_y_lo_marcado_se_conserva()
    {
        var falso = Con(ConEntrega(Mixto));
        falso.RespuestaDeVistaPorLlamada = llamada => llamada == "aceptaciones"
            ? Resultado<VistaDelCheckout>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, "checkout-aceptacion-requerida", [new ErrorDeCampo("privacidad", "obligatorio")]))
            : null;
        await using var anfitrion = await Iniciar(falso);

        var (html, respuesta, _) = await Enviar(anfitrion, "/checkout", new KeyValuePair<string, string>("terminosVersion", "t-1"), new("totalVisto", "140.00"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        Assert.Contains("Falta aceptar los textos", html, StringComparison.Ordinal);
        Assert.Contains("id=\"error-privacidadVersion\"", html, StringComparison.Ordinal);
        Assert.Matches("name=\"terminosVersion\" value=\"t-1\" checked", html);
        Assert.DoesNotContain("pagar", falso.Llamadas);
    }

    [Theory]
    [InlineData(ResultadoDeExigencia.Requerida, "Confirma que eres una persona")]
    [InlineData(ResultadoDeExigencia.Fallida, "No pudimos verificarte")]
    public async Task Sin_la_verificacion_de_persona_se_dice_en_la_pantalla_y_no_se_llama_a_la_Aplicacion(ResultadoDeExigencia resultado, string titulo)
    {
        var falso = Con(ConEntrega(Mixto));
        await using var anfitrion = await Iniciar(falso, new ExigenciaDeVerificacionFalsa { Resultado = resultado });

        var (html, respuesta, _) = await Enviar(anfitrion, "/checkout", CamposDePago());

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Contains(titulo, html, StringComparison.Ordinal);
        Assert.DoesNotContain("pagar", falso.Llamadas);
        Assert.DoesNotContain("aceptaciones", falso.Llamadas);
    }
}
