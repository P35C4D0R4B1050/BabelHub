using System.Net;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.IntegrationTests.Pagos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// T29: la cuenta del cliente (datos, WhatsApp, contraseña, segundo factor, avisos, cambio de correo, cancelación diferida y solicitudes sobre datos personales) con las vistas reales y los contratos de Identidad doblados (AC-WEB-29, AC-WEB-30, AC-WEB-31, AC-WEB-32, AC-WEB-33, AC-WEB-36): cada acción es un
/// <c>POST</c> con antifalsificación y esquema estricto que vuelve con <c>303</c>, los secretos se muestran una vez, con los textos pendientes se ve sin acciones y al ejecutarse la cancelación se cierran todas las sesiones.
/// </summary>
public sealed class PantallasDeLaCuentaTests : IAsyncLifetime
{
    private readonly SesionesWebDePrueba _sesiones = new SesionesWebDePrueba().ConSesionValida();
    private readonly List<string> _llamadas = [];
    private readonly Dictionary<string, object?> _argumentos = [];
    private AnfitrionWeb _anfitrion = null!;
    private Resultado<EstadoDeLaCancelacionDeCuenta> _cancelacion = Resultado<EstadoDeLaCancelacionDeCuenta>.Exito(new EstadoDeLaCancelacionDeCuenta(EstadoDeLaCancelacion.SinSolicitud, 0, null, null, false));
    private bool _textosPendientes;
    private bool _reconocida;
    private ErrorDeAplicacion? _falla;

    private static readonly Guid Aviso = Guid.Parse("a1000000-0000-0000-0000-000000000001");

    public async Task InitializeAsync() => _anfitrion = await Armar();

    public async Task DisposeAsync() => await _anfitrion.DisposeAsync();

    private Resultado<T> De<T>(T valor) => _falla is null ? Resultado<T>.Exito(valor) : Resultado<T>.Fallo(_falla);

    private async Task<AnfitrionWeb> Armar(RolWeb rol = RolWeb.Cliente)
    {
        var usuario = _sesiones.UsuarioId;
        var datos = new CuentaDelCliente(usuario, "Ana <b>Pérez</b>", "ana@correo.example", true, "987654321", true, SegundoFactorActivo: true, CodigosDeRespaldoRestantes: 6);
        var cuenta = DobleDeMetodos.Crear<ICuentaDelCliente>(out var dCuenta);
        dCuenta.Con(nameof(ICuentaDelCliente.Consultar), _ => Task.FromResult(Resultado<CuentaDelCliente>.Exito(datos)));
        dCuenta.Con(nameof(ICuentaDelCliente.CorregirElNombre), a => { _llamadas.Add("nombre"); _argumentos["nombre"] = a[1]; return Task.FromResult(De(datos)); });
        dCuenta.Con(nameof(ICuentaDelCliente.GuardarElNumeroDeWhatsApp), a => { _llamadas.Add("numero"); return Task.FromResult(De(datos)); });
        dCuenta.Con(nameof(ICuentaDelCliente.CambiarElConsentimientoDeWhatsApp), a => { _llamadas.Add("consentimiento"); _argumentos["consiente"] = a[1]; return Task.FromResult(De(datos)); });
        dCuenta.Con(nameof(ICuentaDelCliente.CambiarLaContrasena), a => { _llamadas.Add("contrasena"); _argumentos["renovacion"] = ((ValorSecreto?)a[3])?.Revelar(); return Task.FromResult(De(new RespuestaUniforme())); });
        dCuenta.Con(nameof(ICuentaDelCliente.IniciarElSegundoFactor), a => { _llamadas.Add("iniciar-2fa"); return Task.FromResult(De(new SecretoDelSegundoFactor(new ValorSecreto("JBSWY3DPEHPK3PXP"), "BabelHub", "ana@correo.example"))); });
        dCuenta.Con(nameof(ICuentaDelCliente.ConfirmarElSegundoFactor), a => { _llamadas.Add("confirmar-2fa"); return Task.FromResult(De<IReadOnlyList<ValorSecreto>>([new ValorSecreto("AAAA-1111"), new ValorSecreto("BBBB-2222")])); });
        dCuenta.Con(nameof(ICuentaDelCliente.DesactivarElSegundoFactor), a => { _llamadas.Add("desactivar-2fa"); _argumentos["renovacion"] = ((ValorSecreto?)a[3])?.Revelar(); return Task.FromResult(De(new RespuestaUniforme())); });
        dCuenta.Con(nameof(ICuentaDelCliente.ListarLosAvisos), _ => Task.FromResult(Resultado<IReadOnlyList<AvisoDeCuenta>>.Exito([new AvisoDeCuenta(Aviso, "contrasena-cambiada", new DateTimeOffset(2026, 11, 19, 15, 0, 0, TimeSpan.Zero), false)])));
        dCuenta.Con(nameof(ICuentaDelCliente.MarcarElAvisoComoVisto), a => { _llamadas.Add("visto"); _argumentos["reconocida"] = a[2]; return Task.FromResult(De(new RespuestaUniforme())); });
        var dispositivos = DobleDeMetodos.Crear<IDispositivosDeLaCuenta>(out var dDispositivos);
        dDispositivos.Con(nameof(IDispositivosDeLaCuenta.Reconoce), a => { _llamadas.Add("reconoce"); _argumentos["marca"] = (a[1] as ValorSecreto)?.Revelar(); _argumentos["region"] = a[2]; return Task.FromResult(De(_reconocida)); });
        var correo = DobleDeMetodos.Crear<ICambioDeCorreo>(out var dCorreo);
        dCorreo.Con(nameof(ICambioDeCorreo.Solicitar), a => { _llamadas.Add("correo"); _argumentos["correoNuevo"] = a[1]; return Task.FromResult(De(new RespuestaUniforme())); });
        dCorreo.Con(nameof(ICambioDeCorreo.ConfirmarElCambioPendiente), a => { _llamadas.Add("confirmar-correo"); return Task.FromResult(De(new RespuestaUniforme())); });
        dCorreo.Con(nameof(ICambioDeCorreo.RechazarElCambioPendiente), a => { _llamadas.Add("rechazar-correo"); return Task.FromResult(De(new RespuestaUniforme())); });
        var cancelaciones = DobleDeMetodos.Crear<ICancelacionDeCuenta>(out var dCancelacion);
        dCancelacion.Con(nameof(ICancelacionDeCuenta.Consultar), _ => Task.FromResult(_cancelacion));
        dCancelacion.Con(nameof(ICancelacionDeCuenta.Solicitar), _ => { _llamadas.Add("solicitar-cancelacion"); return Task.FromResult(_cancelacion); });
        dCancelacion.Con(nameof(ICancelacionDeCuenta.Reconfirmar), _ => { _llamadas.Add("reconfirmar"); return Task.FromResult(_cancelacion); });
        dCancelacion.Con(nameof(ICancelacionDeCuenta.Desistir), _ => { _llamadas.Add("desistir"); return Task.FromResult(_cancelacion); });
        var solicitudes = DobleDeMetodos.Crear<ISolicitudesDeDatos>(out var dSolicitudes);
        dSolicitudes.Con(nameof(ISolicitudesDeDatos.Presentar), a => { _llamadas.Add("solicitud"); _argumentos["tipo"] = a[1]; return Task.FromResult(De(new SolicitudSobreDatos(Guid.NewGuid(), TipoDeSolicitudDeDatos.Acceso, "en atención", new DateOnly(2026, 11, 19), new DateOnly(2026, 12, 4), false))); });
        dSolicitudes.Con(nameof(ISolicitudesDeDatos.ListarLasDelCliente), _ => Task.FromResult(Resultado<IReadOnlyList<SolicitudDelCliente>>.Exito([new SolicitudDelCliente(new SolicitudSobreDatos(Guid.NewGuid(), TipoDeSolicitudDeDatos.Acceso, "en atención", new DateOnly(2026, 11, 19), new DateOnly(2026, 12, 4), false), null, null)])));
        var borradores = DobleDeMetodos.Crear<IBorradoresDePantalla>(out var dBorradores);
        dBorradores.Con(nameof(IBorradoresDePantalla.Recuperar), _ => Task.FromResult<IReadOnlyDictionary<string, string>?>(new Dictionary<string, string> { ["nombre"] = "Ana recuperada" }));
        if (rol != RolWeb.Cliente)
        {
            _sesiones.ConSesionValida(rol);
        }

        var flujo = new BabelHub.Ecommerce.Infrastructure.Plataforma.ActorDelFlujo();
        _sesiones.LeerActor = () => flujo.Actual;
        return await AnfitrionConSesion.IniciarCon(
            _sesiones,
            flujo,
            servicios: s =>
            {
                s.AddSingleton(cuenta);
                s.AddSingleton(correo);
                s.AddSingleton(dispositivos);
                s.AddSingleton(cancelaciones);
                s.AddSingleton(solicitudes);
                s.AddSingleton(borradores);
            },
            despuesDelHandler: app => app.Use(async (contexto, siguiente) =>
            {
                if (_textosPendientes)
                {
                    contexto.Items[BabelHub.Ecommerce.Api.Web.ContextoDePagina.ClaveTextosPendientes] = true;
                }

                await siguiente();
            }));
    }

    private Task<HttpResponseMessage> Pedir(string ruta)
    {
        var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        solicitud.Headers.TryAddWithoutValidation("Cookie", AnfitrionConSesion.Cookie());
        return _anfitrion.Cliente.SendAsync(solicitud);
    }

    private async Task<HttpResponseMessage> Enviar(string ruta, params (string Campo, string Valor)[] campos)
    {
        using var solicitud = await AnfitrionConSesion.Post(_anfitrion, ruta, AnfitrionConSesion.Cookie());
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        solicitud.Content = new FormUrlEncodedContent(campos.Select(c => new KeyValuePair<string, string>(c.Campo, c.Valor)));
        return await _anfitrion.Cliente.SendAsync(solicitud);
    }

    private static async Task<string> Html(HttpResponseMessage respuesta) => WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task Cada_seccion_de_la_cuenta_existe_codifica_lo_que_viene_de_Identidad_y_no_se_cachea()
    {
        foreach (var ruta in new[] { "/cuenta", "/cuenta/datos", "/cuenta/segundo-factor", "/cuenta/correo", "/cuenta/avisos", "/cuenta/cancelacion", "/cuenta/solicitudes" })
        {
            using var respuesta = await Pedir(ruta);

            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
            var crudo = await respuesta.Content.ReadAsStringAsync();
            Assert.DoesNotContain("<b>Pérez</b>", crudo, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task Los_datos_recuperan_lo_escrito_al_vencer_la_sesion_y_nunca_una_contrasena()
    {
        using var respuesta = await Pedir("/cuenta/datos");

        var html = await Html(respuesta);
        Assert.Contains("value=\"Ana recuperada\"", html, StringComparison.Ordinal);
        Assert.Contains("Recuperamos lo que estabas escribiendo", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"actual\" value", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task Guardar_el_nombre_y_el_consentimiento_llama_a_Identidad_y_vuelve_con_un_aviso_de_lista_cerrada()
    {
        using var nombre = await Enviar("/cuenta/datos", ("nombre", "Ana Pérez"));
        using var whatsapp = await Enviar("/cuenta/whatsapp", ("numero", "987654321"), ("consiente", "si"));

        Assert.Equal(HttpStatusCode.SeeOther, nombre.StatusCode);
        Assert.Equal("/cuenta/datos?aviso=nombre-guardado", nombre.Headers.Location?.OriginalString);
        Assert.Equal("/cuenta/datos?aviso=whatsapp-guardado", whatsapp.Headers.Location?.OriginalString);
        Assert.Equal(["nombre", "numero", "consentimiento"], _llamadas);
        Assert.Equal(true, _argumentos["consiente"]);
        using var pantalla = await Pedir("/cuenta/datos?aviso=nombre-guardado");
        Assert.Contains("Guardamos tu nombre.", await Html(pantalla), StringComparison.Ordinal);
        using var inventado = await Pedir("/cuenta/datos?aviso=%3Cscript%3E");
        Assert.DoesNotContain("<script>", await inventado.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
[Fact]    [Trait("Criterio", "AC-WEB-33")]    public async Task Cambiar_la_contrasena_o_desactivar_el_segundo_factor_entrega_a_Identidad_la_renovacion_de_la_sesion_actual_para_conservarla()    {        using var contrasena = await Enviar("/cuenta/contrasena", ("actual", "la contraseña actual"), ("nueva", "una frase nueva y segura"), ("confirmacion", "una frase nueva y segura"));        Assert.Equal("renovacion-de-la-sesion-actual", _argumentos["renovacion"]);        _argumentos.Remove("renovacion");        using var desactivado = await Enviar("/cuenta/segundo-factor/desactivar", ("actual", "la contraseña actual"), ("codigo", "123456"));        Assert.Equal(HttpStatusCode.SeeOther, contrasena.StatusCode);        Assert.Equal(HttpStatusCode.SeeOther, desactivado.StatusCode);        Assert.Equal("renovacion-de-la-sesion-actual", _argumentos["renovacion"]);        _sesiones.Renovacion = null;        _argumentos.Remove("renovacion");        using var sinSesionLegible = await Enviar("/cuenta/contrasena", ("actual", "x"), ("nueva", "una frase nueva y segura"), ("confirmacion", "una frase nueva y segura"));        Assert.Null(_argumentos["renovacion"]);    }

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task Un_campo_de_mas_o_una_confirmacion_distinta_se_rechazan_sin_llamar_a_Identidad_ni_repetir_la_contrasena()
    {
        using var deMas = await Enviar("/cuenta/datos", ("nombre", "Ana"), ("rol", "administrador"));
        using var distinta = await Enviar("/cuenta/contrasena", ("actual", "la actual"), ("nueva", "una frase nueva y segura"), ("confirmacion", "otra distinta"));

        Assert.Equal(HttpStatusCode.BadRequest, deMas.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, distinta.StatusCode);
        var html = await Html(distinta);
        Assert.Contains("no coinciden", html, StringComparison.Ordinal);
        Assert.DoesNotContain("una frase nueva", html, StringComparison.Ordinal);
        Assert.Empty(_llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-30")]
    public async Task El_segundo_factor_muestra_el_secreto_una_vez_y_los_codigos_de_respaldo_una_vez()
    {
        using var iniciado = await Enviar("/cuenta/segundo-factor/iniciar", ("actual", "la contraseña actual"));
        using var confirmado = await Enviar("/cuenta/segundo-factor/confirmar", ("codigo", "123456"));
        using var despues = await Pedir("/cuenta/segundo-factor");

        Assert.Equal(HttpStatusCode.OK, iniciado.StatusCode);
        Assert.Contains("JBSWY3DPEHPK3PXP", await Html(iniciado), StringComparison.Ordinal);
        var codigos = await Html(confirmado);
        Assert.Contains("AAAA-1111", codigos, StringComparison.Ordinal);
        Assert.Contains("solo esta vez", codigos, StringComparison.Ordinal);
        Assert.Contains("no-store", confirmado.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        var tras = await Html(despues);
        Assert.DoesNotContain("JBSWY3DPEHPK3PXP", tras, StringComparison.Ordinal);
        Assert.DoesNotContain("AAAA-1111", tras, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-33")]
    public async Task Marcar_un_aviso_como_visto_es_un_POST_explicito_y_entrega_a_Identidad_si_la_sesion_trae_la_marca_de_dispositivo()
    {
        using var sinMarca = await Enviar($"/cuenta/avisos/{Aviso:D}/visto");

        Assert.Equal(HttpStatusCode.SeeOther, sinMarca.StatusCode);
        Assert.Equal(false, _argumentos["reconocida"]);
        using var get = await _anfitrion.Cliente.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/cuenta/avisos/{Aviso:D}/visto") { Headers = { { "Cookie", AnfitrionConSesion.Cookie() } } });
        Assert.Contains(get.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Single(_llamadas, "visto");
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-33")]
    public async Task Marcar_un_aviso_como_visto_entrega_la_marca_y_la_region_a_Identidad_y_solo_esa_decide_si_la_sesion_es_reconocida()
    {
        var marca = new string('a', 43);
        async Task<HttpResponseMessage> Marcar()
        {
            using var solicitud = await AnfitrionConSesion.Post(_anfitrion, $"/cuenta/avisos/{Aviso:D}/visto", AnfitrionConSesion.Cookie() + "; " + BabelHub.Ecommerce.Api.Web.CookieDeDispositivo.Nombre + "=" + marca);
            solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
            solicitud.Headers.TryAddWithoutValidation(BabelHub.Ecommerce.Api.Web.DatosDelBorde.CabeceraDeRegion, "PE");
            solicitud.Content = new FormUrlEncodedContent([]);
            return await _anfitrion.Cliente.SendAsync(solicitud);
        }

        using var desconocida = await Marcar();
        Assert.Equal(false, _argumentos["reconocida"]);
        _reconocida = true;
        using var reconocida = await Marcar();

        Assert.Equal(marca, _argumentos["marca"]);
        Assert.Equal("PE", _argumentos["region"]);
        Assert.Equal(true, _argumentos["reconocida"]);
        Assert.Equal(HttpStatusCode.SeeOther, reconocida.StatusCode);
        Assert.Equal(HttpStatusCode.SeeOther, desconocida.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-31")]
    public async Task El_cambio_de_correo_se_pide_confirma_o_rechaza_con_la_contrasena_y_Identidad_decide_por_el_dispositivo()
    {
        using var pedido = await Enviar("/cuenta/correo/solicitar", ("correoNuevo", "nuevo@correo.example"), ("actual", "la contraseña actual"));
        _falla = new ErrorDeAplicacion(CategoriaDeError.Prohibido, "dispositivo-no-reconocido");
        using var denegado = await Enviar("/cuenta/correo/confirmar", ("actual", "la contraseña actual"));

        Assert.Equal("/cuenta/correo?aviso=correo-solicitado", pedido.Headers.Location?.OriginalString);
        Assert.Equal("nuevo@correo.example", _argumentos["correoNuevo"]);
        Assert.Equal(HttpStatusCode.Forbidden, denegado.StatusCode);
        Assert.Contains("No puedes hacer esto desde este dispositivo", await Html(denegado), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-25")]
    public async Task La_pantalla_de_cancelacion_advierte_que_se_pierde_la_biblioteca_digital()
    {
        _cancelacion = Resultado<EstadoDeLaCancelacionDeCuenta>.Exito(new EstadoDeLaCancelacionDeCuenta(EstadoDeLaCancelacion.Descartada, 0, null, null, true));

        using var respuesta = await Pedir("/cuenta/cancelacion");

        Assert.Contains("Perderás el acceso a tu biblioteca digital", await Html(respuesta), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-32")]
    public async Task Una_cancelacion_diferida_muestra_que_falta_cerrar_y_pide_contrasena_y_segundo_factor_para_reconfirmar()
    {
        _cancelacion = Resultado<EstadoDeLaCancelacionDeCuenta>.Exito(new EstadoDeLaCancelacionDeCuenta(EstadoDeLaCancelacion.Diferida, 2, null, new DateTimeOffset(2026, 12, 19, 0, 0, 0, TimeSpan.Zero), false));
        using var diferida = await Pedir("/cuenta/cancelacion");
        _cancelacion = Resultado<EstadoDeLaCancelacionDeCuenta>.Exito(new EstadoDeLaCancelacionDeCuenta(EstadoDeLaCancelacion.EsperandoReconfirmacion, 0, new DateTimeOffset(2026, 11, 26, 15, 0, 0, TimeSpan.Zero), null, true));
        using var reconfirmar = await Pedir("/cuenta/cancelacion");
        _cancelacion = Resultado<EstadoDeLaCancelacionDeCuenta>.Exito(new EstadoDeLaCancelacionDeCuenta(EstadoDeLaCancelacion.Descartada, 0, null, null, true));
        using var descartada = await Pedir("/cuenta/cancelacion");

        var uno = await Html(diferida);
        Assert.Contains("faltan cerrar <strong>2</strong> pendiente(s)", uno, StringComparison.Ordinal);
        Assert.Contains("7 días", uno, StringComparison.Ordinal);
        Assert.Contains("30 días", uno, StringComparison.Ordinal);
        var dos = await Html(reconfirmar);
        Assert.Contains("action=\"/cuenta/cancelacion/reconfirmar\"", dos, StringComparison.Ordinal);
        Assert.Contains("name=\"codigo\"", dos, StringComparison.Ordinal);
        Assert.Contains("se descartó porque pasaron 30 días", await Html(descartada), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-32")]
    public async Task Al_ejecutarse_la_cancelacion_se_cierran_todas_las_sesiones_y_el_navegador_queda_limpio()
    {
        _cancelacion = Resultado<EstadoDeLaCancelacionDeCuenta>.Exito(new EstadoDeLaCancelacionDeCuenta(EstadoDeLaCancelacion.Ejecutada, 0, null, null, true));

        using var respuesta = await Enviar("/cuenta/cancelacion/reconfirmar", ("contrasena", "la actual"), ("codigo", "123456"));

        Assert.Equal(["reconfirmar"], _llamadas);
        Assert.Equal([_sesiones.UsuarioId], _sesiones.CierresDeTodas);
        Assert.Equal("/ingreso", respuesta.Headers.Location?.OriginalString);
        Assert.True(respuesta.Headers.Contains("Clear-Site-Data"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-36")]
    public async Task Las_solicitudes_sobre_datos_se_presentan_con_un_tipo_de_lista_cerrada_y_se_ven_con_su_fecha_limite()
    {
        using var presentada = await Enviar("/cuenta/solicitudes", ("tipo", "Acceso"));
        using var invalida = await Enviar("/cuenta/solicitudes", ("tipo", "Borrar-todo"));
        using var lista = await Pedir("/cuenta/solicitudes");

        Assert.Equal("/cuenta/solicitudes?aviso=solicitud-presentada", presentada.Headers.Location?.OriginalString);
        Assert.Equal(TipoDeSolicitudDeDatos.Acceso, _argumentos["tipo"]);
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        var html = await Html(lista);
        Assert.Contains("Fecha límite", html, StringComparison.Ordinal);
        Assert.Contains("04/12/2026", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-36")]
    public async Task Con_textos_pendientes_la_cuenta_se_ve_sin_ofrecer_acciones()
    {
        _textosPendientes = true;

        using var vista = await Pedir("/cuenta/solicitudes");

        var html = await Html(vista);
        Assert.Equal(HttpStatusCode.OK, vista.StatusCode);
        Assert.Contains("textos legales por aceptar", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Presentar la solicitud", html, StringComparison.Ordinal);
        Assert.Empty(_llamadas);
    }

    [Fact]
    public async Task Un_error_de_Identidad_se_dice_sin_repetir_lo_que_respondio_y_una_caida_no_es_una_pagina_tecnica()
    {
        _falla = new ErrorDeAplicacion(CategoriaDeError.Limite, "limite-excedido");
        using var limite = await Enviar("/cuenta/datos", ("nombre", "Ana"));
        _falla = new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible");
        using var caida = await Enviar("/cuenta/datos", ("nombre", "Ana"));

        Assert.Equal(HttpStatusCode.TooManyRequests, limite.StatusCode);
        Assert.Contains("demasiados intentos", await Html(limite), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, caida.StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_va_al_ingreso_y_con_otro_rol_no_se_ofrecen_las_acciones_del_cliente()
    {
        using var anonimo = await _anfitrion.Cliente.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/cuenta") { Headers = { { "Accept", "text/html" } } });

        Assert.Equal(HttpStatusCode.Found, anonimo.StatusCode);
        Assert.Equal("/ingreso", anonimo.Headers.Location?.OriginalString);
        await using var personal = await Armar(RolWeb.Logistica);
        using var solicitud = await AnfitrionConSesion.Post(personal, "/cuenta/datos", AnfitrionConSesion.Cookie());
        solicitud.Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("nombre", "x")]);
        using var accion = await personal.Cliente.SendAsync(solicitud);
        Assert.Equal("/sin-permiso", accion.Headers.Location?.OriginalString);
        Assert.Empty(_llamadas);
    }
}
