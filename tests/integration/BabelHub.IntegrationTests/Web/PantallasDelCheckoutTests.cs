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
        new(Guid.NewGuid(), Guid.NewGuid(), titulo, formato, cantidad, precio, precio * cantidad, disponibilidad, plazo, aConfirmar, retirada);

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

    private static async Task<AnfitrionWeb> Iniciar(CheckoutDelClienteFalso falso)
    {
        var actor = new ActorFijo(ActorActual.DeUsuario(Guid.NewGuid(), "cliente", null));
        return await AnfitrionWeb.Iniciar(servicios =>
        {
            servicios.AddSingleton<ICheckoutDelCliente>(falso);
            servicios.AddSingleton<IExigenciaDeVerificacionDePersona>(new ExigenciaDeVerificacionFalsa());
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

        Assert.Contains("En tu sucursal", html, StringComparison.Ordinal);
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
    public async Task La_entrega_ofrece_un_formulario_de_recojo_con_las_sucursales_y_otro_de_domicilio_con_sus_campos_etiquetados()
    {
        await using var anfitrion = await Iniciar(Con(Vista(Mixto)));

        var (html, _) = await Pedir(anfitrion);

        Assert.Equal(2, Regex.Matches(html, "action=\"/checkout/entrega\"").Count);
        Assert.Contains("name=\"tipoDeEntrega\" value=\"recojo\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"tipoDeEntrega\" value=\"domicilio\"", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"AYA\"", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"LIM\"", html, StringComparison.Ordinal);
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
}
