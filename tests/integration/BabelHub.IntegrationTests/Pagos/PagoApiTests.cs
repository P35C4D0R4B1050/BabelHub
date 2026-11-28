using System.Net;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// T28: la Presentación del pago (PAG-01 a PAG-09, AC-PAG-07, AC-PAG-14, AC-PAG-21): el esquema estricto del cobro, que el token del medio no se repita en ningún error, las redirecciones al checkout cuando el carrito cambió o no hay un pago vigente, el estado
/// y el retorno de solo lectura, la traducción de los errores <c>ECO-PAGO-…</c> y que sin token antifalsificación no se llegue a Pagos.
/// </summary>
public sealed class PagoApiTests : IAsyncLifetime
{
    private static readonly Guid Pedido = PagosFalso.Pedido;

    private PagosApiHost _host = null!;

    public async Task InitializeAsync() => _host = await PagosApiHost.Crear();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static KeyValuePair<string, string> C(string nombre, string valor) => new(nombre, valor);

    private static KeyValuePair<string, string>[] Cobro(params KeyValuePair<string, string>[] cambios)
    {
        var campos = new Dictionary<string, string> { ["pedidoId"] = Pedido.ToString("D"), ["claveDeIntento"] = "clave.firmada", ["medio"] = "tarjeta", ["tokenDelMedio"] = "tkn_test_abc123" };
        foreach (var (nombre, valor) in cambios)
        {
            campos[nombre] = valor;
        }

        return [.. campos];
    }

    // ---------- Cobrar ----------

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Cobrar_llama_a_Pagos_con_el_medio_y_el_token_y_redirige_al_estado()
    {
        using var respuesta = await _host.Enviar("/checkout/pago", Cobro());

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal($"/checkout/pago/estado?pedidoId={Pedido:D}", respuesta.Headers.Location?.OriginalString);
        var entrada = Assert.IsType<CobrarEntrada>(Assert.Single(_host.Pagos.Entradas));
        Assert.Equal((Pedido, MedioDePago.Tarjeta, "clave.firmada"), (entrada.PedidoId, entrada.Medio, entrada.ClaveDeIntento));
        Assert.Equal("tkn_test_abc123", entrada.TokenDelMedio.Revelar());
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("medio", "bitcoin")]
    [InlineData("pedidoId", "no-es-guid")]
    [InlineData("claveDeIntento", "")]
    [InlineData("tokenDelMedio", "4111111111111111")]
    [InlineData("tokenDelMedio", "con espacios")]
    public async Task Un_campo_invalido_es_validacion_sin_llamar_a_Pagos_y_sin_repetir_el_token(string campo, string valor)
    {
        using var respuesta = await _host.Enviar("/checkout/pago", Cobro(C(campo, valor)));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(_host.Pagos.Llamadas);
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("4111111111111111", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("tkn_test_abc123", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_token_antifalsificacion_no_se_llega_a_Pagos()
    {
        using var sinToken = await _host.Enviar("/checkout/pago", Cobro(), conToken: false);

        Assert.NotEqual($"/checkout/pago/estado?pedidoId={Pedido:D}", sinToken.Headers.Location?.OriginalString);
        Assert.Empty(_host.Pagos.Llamadas);
    }

    [Theory]
    [InlineData("instantanea-obsoleta")]
    [InlineData("total-cambio")]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Un_carrito_o_un_total_que_cambio_vuelve_al_checkout_sin_cobrar(string clave)
    {
        _host.Pagos.Cobro = Resultado<ResultadoDelCobroDelCliente>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, clave));

        using var respuesta = await _host.Enviar("/checkout/pago", Cobro());

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal("/checkout", respuesta.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData(CategoriaDeError.Conflicto, "reserva-expirada", HttpStatusCode.Conflict, "ECO-PAGO-RESERVA-EXPIRADA")]
    [InlineData(CategoriaDeError.Conflicto, "intencion-cancelada", HttpStatusCode.Conflict, "ECO-PAGO-INTENCION-CANCELADA")]
    [InlineData(CategoriaDeError.Conflicto, "pago-en-curso", HttpStatusCode.Conflict, "ECO-PAGO-EN-CURSO")]
    [InlineData(CategoriaDeError.Conflicto, "clave-invalida", HttpStatusCode.Conflict, "ECO-PAGO-CLAVE-INVALIDA")]
    [InlineData(CategoriaDeError.Conflicto, "digital-ya-poseida", HttpStatusCode.Conflict, "ECO-DIGITAL-YA-POSEIDA")]
    [InlineData(CategoriaDeError.Prohibido, "cuenta-no-puede-comprar", HttpStatusCode.Forbidden, "ECO-CHECKOUT-CUENTA-NO-PUEDE-COMPRAR")]
    [InlineData(CategoriaDeError.NoDisponible, "servicio-no-disponible", HttpStatusCode.ServiceUnavailable, "ECO-SERVICIO-NO-DISPONIBLE")]
    [InlineData(CategoriaDeError.NoEncontrado, "no-encontrado", HttpStatusCode.NotFound, "ECO-NO-ENCONTRADO")]
    public async Task Los_errores_de_Pagos_se_traducen_a_su_codigo_estable(CategoriaDeError categoria, string clave, HttpStatusCode estado, string codigo)
    {
        _host.Pagos.Cobro = Resultado<ResultadoDelCobroDelCliente>.Fallo(new ErrorDeAplicacion(categoria, clave));

        using var respuesta = await _host.Enviar("/checkout/pago", Cobro());

        Assert.Equal(estado, respuesta.StatusCode);
        Assert.Equal(codigo, (await PagosApiHost.Leer(respuesta)).GetProperty("code").GetString());
    }

    // ---------- Mostrar ----------

    [Fact]
    [Trait("Criterio", "AC-PAG-29")]
    public async Task Con_la_pasarela_simulada_la_pantalla_del_pago_avisa_que_no_se_cobra_nada_y_con_la_real_no()
    {
        await using var simulado = await PagosApiHost.Crear(pagosSimulados: true);

        using var conAviso = await simulado.Cliente.SendAsync(simulado.Pedir($"/checkout/pago?pedidoId={Pedido:D}", html: true));
        using var sinAviso = await _host.Cliente.SendAsync(_host.Pedir($"/checkout/pago?pedidoId={Pedido:D}", html: true));

        var html = WebUtility.HtmlDecode(await conAviso.Content.ReadAsStringAsync());
        Assert.Contains("Pagos simulados: no se realiza ningún cobro", html, StringComparison.Ordinal);
        Assert.Contains("ningún medio de pago es real", html, StringComparison.Ordinal);
        // El aviso va dentro del contenido, antes del formulario de pago.
        Assert.True(html.IndexOf("data-bh-pagos-simulados", StringComparison.Ordinal) < html.IndexOf("formulario-de-pago", StringComparison.Ordinal));
        Assert.DoesNotContain("Pagos simulados", await sinAviso.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public async Task Mostrar_el_pago_con_el_pedidoId_emite_la_clave_y_pinta_el_formulario_sin_conservar_nada()
    {
        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir($"/checkout/pago?pedidoId={Pedido:D}", html: true));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var html = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("name=\"claveDeIntento\"", html, StringComparison.Ordinal);
        Assert.Contains("clave.firmada", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-bh-conservar", html, StringComparison.Ordinal);
        Assert.Equal(["estado", "clave"], _host.Pagos.Llamadas);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-01")]
    public async Task La_pantalla_del_pago_tiene_cuenta_regresiva_por_atributo_medios_accesibles_y_ningun_dato_de_tarjeta()
    {
        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir($"/checkout/pago?pedidoId={Pedido:D}", html: true));

        var html = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("role=\"timer\"", html, StringComparison.Ordinal);
        Assert.Contains("data-bh-cuenta-regresiva=\"", html, StringComparison.Ordinal);
        Assert.Contains("data-bh-vencido", html, StringComparison.Ordinal);
        Assert.Contains("name=\"medio\" value=\"yape\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"medio\" value=\"tarjeta\"", html, StringComparison.Ordinal);
        Assert.Contains("role=\"radiogroup\"", html, StringComparison.Ordinal);
        // Sin scripts en línea (la política de contenido no los admite) ni campos de tarjeta: solo el identificador que entrega la pasarela.
        Assert.DoesNotContain("<script", html.Replace("<script src=", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cc-number", html, StringComparison.Ordinal);
        Assert.Contains("name=\"tokenDelMedio\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mostrar_el_pago_sin_pedidoId_usa_el_checkout_vivo_en_pago()
    {
        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir("/checkout/pago", html: true));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(1, _host.Checkout.Consultas);
        Assert.Equal(Pedido, Assert.IsType<Guid>(_host.Pagos.Entradas[0]));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public async Task Sin_un_pedido_vigente_o_con_otro_estado_del_checkout_se_vuelve_al_checkout_o_al_estado_sin_emitir_nada()
    {
        _host.Checkout.Vivo = Resultado<CheckoutVivoParaPagar>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "checkout-no-encontrado"));
        using var sinVivo = await _host.Cliente.SendAsync(_host.Pedir("/checkout/pago"));
        _host.Checkout.Vivo = Resultado<CheckoutVivoParaPagar>.Exito(new CheckoutVivoParaPagar(Pedido, EstadoVisibleDelCheckout.EnEdicion));
        using var enEdicion = await _host.Cliente.SendAsync(_host.Pedir("/checkout/pago"));
        _host.Checkout.Vivo = Resultado<CheckoutVivoParaPagar>.Exito(new CheckoutVivoParaPagar(Pedido, EstadoVisibleDelCheckout.PagoPendiente));
        using var pendiente = await _host.Cliente.SendAsync(_host.Pedir("/checkout/pago"));

        Assert.Equal("/checkout", sinVivo.Headers.Location?.OriginalString);
        Assert.Equal("/checkout", enEdicion.Headers.Location?.OriginalString);
        Assert.Equal($"/checkout/pago/estado?pedidoId={Pedido:D}", pendiente.Headers.Location?.OriginalString);
        Assert.Empty(_host.Pagos.Llamadas);
    }

    [Fact]
    public async Task Si_la_clave_no_se_puede_emitir_porque_el_carrito_cambio_se_vuelve_al_checkout()
    {
        _host.Pagos.Clave = Resultado<ClaveDeIntentoEmitida>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, "instantanea-obsoleta"));

        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir($"/checkout/pago?pedidoId={Pedido:D}"));

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal("/checkout", respuesta.Headers.Location?.OriginalString);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-05")]
    public async Task Tras_un_rechazo_la_pantalla_dice_el_motivo_en_palabras_de_la_persona()
    {
        _host.Pagos.EstadoDelPago = Resultado<EstadoDelPagoVista>.Exito(new EstadoDelPagoVista(EstadoDelPagoDeLaCompra.ListoParaPagar, DateTimeOffset.UtcNow.AddMinutes(10), null, MotivoDeRechazoDePago.FondosInsuficientes, true));

        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir($"/checkout/pago?pedidoId={Pedido:D}", html: true));

        var html = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("no tiene fondos suficientes", html, StringComparison.Ordinal);
        Assert.Contains("No se te cobró nada", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Un_pedido_ajeno_responde_no_encontrado_y_uno_invalido_es_validacion()
    {
        _host.Pagos.EstadoDelPago = Resultado<EstadoDelPagoVista>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "no-encontrado"));

        using var ajeno = await _host.Cliente.SendAsync(_host.Pedir($"/checkout/pago?pedidoId={Pedido:D}"));
        using var invalido = await _host.Cliente.SendAsync(_host.Pedir("/checkout/pago?pedidoId=xyz"));

        Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.Equal(["estado"], _host.Pagos.Llamadas);
    }

    // ---------- Sin pasarela (DD-110) ----------

    private static EstadoDelPagoVista SinCobro(EstadoDelPagoDeLaCompra estado = EstadoDelPagoDeLaCompra.ListoParaPagar) =>
        new(estado, DateTimeOffset.UtcNow.AddMinutes(15), DateTimeOffset.UtcNow.AddMinutes(15), null, false, CobroDisponible: false);

    [Fact]
    [Trait("Criterio", "AC-PAG-42")]
    public async Task Sin_pasarela_la_pantalla_del_pago_no_tiene_formulario_ni_boton_de_pagar_y_ofrece_volver()
    {
        // Incluso con un rechazo anterior (de cuando había pasarela) no se invita a intentarlo de nuevo.
        _host.Pagos.EstadoDelPago = Resultado<EstadoDelPagoVista>.Exito(SinCobro() with { UltimoMotivo = MotivoDeRechazoDePago.FondosInsuficientes });

        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir($"/checkout/pago?pedidoId={Pedido:D}", html: true));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var html = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
        var principal = html[html.IndexOf("<main", StringComparison.Ordinal)..html.IndexOf("</main>", StringComparison.Ordinal)];
        Assert.Contains("El pago en línea estará disponible pronto", principal, StringComparison.Ordinal);
        Assert.Contains("No se te cobró nada", principal, StringComparison.Ordinal);
        foreach (var prohibido in new[] { "formulario-de-pago", "intentarlo de nuevo", "tokenDelMedio", "claveDeIntento", "Identificador del medio de pago", "name=\"medio\"", "action=\"/checkout/pago\"", "href=\"/checkout/pago", ">Pagar", "data-bh-cuenta-regresiva" })
        {
            Assert.DoesNotContain(prohibido, principal, StringComparison.Ordinal);
        }

        // Volver al carrito libera la compra por el contrato de Carrito; el catálogo está a un clic.
        Assert.Contains("action=\"/checkout/volver\"", principal, StringComparison.Ordinal);
        Assert.Contains($"name=\"pedidoId\" value=\"{Pedido:D}\"", principal, StringComparison.Ordinal);
        Assert.Contains("Volver al carrito", principal, StringComparison.Ordinal);
        Assert.Contains("href=\"/\"", principal, StringComparison.Ordinal);
        Assert.DoesNotContain("simulad", html, StringComparison.OrdinalIgnoreCase);
        // No se pide ninguna clave de intento: no hay nada que cobrar.
        Assert.Equal(["estado"], _host.Pagos.Llamadas);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-42")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sin_pasarela_el_estado_del_pago_no_invita_a_pagar(bool htmx)
    {
        _host.Pagos.EstadoDelPago = Resultado<EstadoDelPagoVista>.Exito(SinCobro());

        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir($"/checkout/pago/estado?pedidoId={Pedido:D}", htmx: htmx, html: true));

        var html = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
        Assert.Contains("El pago en línea estará disponible pronto", html, StringComparison.Ordinal);
        Assert.Contains("No se te cobró nada", html, StringComparison.Ordinal);
        Assert.Contains("action=\"/checkout/volver\"", html, StringComparison.Ordinal);
        Assert.Contains("Volver al carrito", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Listo para pagar", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Lo confirmaremos antes", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/checkout/pago?", html, StringComparison.Ordinal);
        Assert.DoesNotContain("hx-trigger", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-42")]
    public async Task Sin_pasarela_un_pago_que_ya_no_admite_cobro_se_sigue_contando_como_esta()
    {
        // Un pago que quedó de cuando había pasarela conserva su estado: no se le dice que «estará disponible pronto».
        _host.Pagos.EstadoDelPago = Resultado<EstadoDelPagoVista>.Exito(SinCobro(EstadoDelPagoDeLaCompra.ConfirmandoElPago));

        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir($"/checkout/pago/estado?pedidoId={Pedido:D}", html: true));

        var html = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
        Assert.Contains("Estamos confirmando tu pago", html, StringComparison.Ordinal);
        Assert.DoesNotContain("estará disponible pronto", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-43")]
    public async Task Sin_pasarela_un_POST_directo_al_cobro_se_rechaza_en_el_servidor_sin_tocar_nada()
    {
        // El módulo Pagos real, compuesto con «ninguna», y con la base, el Inventario, la auditoría y los límites doblados para fallar si alguien los llama.
        await using var cerrado = await PagosApiHost.Crear(sinPasarela: true);

        using var respuesta = await cerrado.Enviar("/checkout/pago", Cobro());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("\"ECO-PAGO-NO-DISPONIBLE\"", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("tkn_test_abc123", texto, StringComparison.Ordinal);
        Assert.Null(respuesta.Headers.Location);
        Assert.Equal(0, cerrado.Checkout.Consultas);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    // ---------- Estado y retorno ----------

    [Fact]
    [Trait("Criterio", "AC-PAG-12")]
    public async Task El_estado_pinta_la_pagina_y_con_htmx_solo_el_fragmento_que_sigue_sondeando_mientras_se_confirma()
    {
        _host.Pagos.EstadoDelPago = Resultado<EstadoDelPagoVista>.Exito(new EstadoDelPagoVista(EstadoDelPagoDeLaCompra.ConfirmandoElPago, DateTimeOffset.UtcNow.AddMinutes(20), null, null, false));

        using var pagina = await _host.Cliente.SendAsync(_host.Pedir($"/checkout/pago/estado?pedidoId={Pedido:D}", html: true));
        using var fragmento = await _host.Cliente.SendAsync(_host.Pedir($"/checkout/pago/estado?pedidoId={Pedido:D}", htmx: true));

        var html = await pagina.Content.ReadAsStringAsync();
        var parcial = await fragmento.Content.ReadAsStringAsync();
        Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Estamos confirmando tu pago", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<html", parcial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hx-trigger=\"every 5s\"", parcial, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Una_compra_completada_deja_de_sondear()
    {
        _host.Pagos.EstadoDelPago = Resultado<EstadoDelPagoVista>.Exito(new EstadoDelPagoVista(EstadoDelPagoDeLaCompra.Completado, null, null, null, false));

        using var fragmento = await _host.Cliente.SendAsync(_host.Pedir($"/checkout/pago/estado?pedidoId={Pedido:D}", htmx: true));

        var parcial = await fragmento.Content.ReadAsStringAsync();
        Assert.Contains("Compra confirmada", parcial, StringComparison.Ordinal);
        Assert.DoesNotContain("hx-trigger", parcial, StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_retorno_no_cambia_nada_y_manda_al_estado_con_el_pedidoId_valido_o_sin_el()
    {
        using var valido = await _host.Cliente.SendAsync(_host.Pedir($"/checkout/pago/retorno?pedidoId={Pedido:D}&estado=aprobado&cargo=ch_1"));
        using var invalido = await _host.Cliente.SendAsync(_host.Pedir("/checkout/pago/retorno?pedidoId=xyz"));

        Assert.Equal($"/checkout/pago/estado?pedidoId={Pedido:D}", valido.Headers.Location?.OriginalString);
        Assert.Equal("/checkout/pago/estado", invalido.Headers.Location?.OriginalString);
        Assert.Empty(_host.Pagos.Llamadas);
        Assert.Equal(0, _host.Checkout.Consultas);
    }
}
