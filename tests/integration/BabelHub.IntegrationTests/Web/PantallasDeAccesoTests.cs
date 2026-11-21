using System.Net;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// T28: las pantallas de ingreso (con el segundo factor, su configuración y la contraseña temporal sobre la cookie del desafío), registro, verificación y recuperación sobre los contratos de Identidad doblados, con las vistas reales (AC-WEB-19, AC-WEB-20, AC-WEB-24, AC-WEB-28, AC-WEB-29): la credencial y la renovación
/// solo llegan a la sesión del servidor, un dato incorrecto responde siempre igual, la verificación de persona va antes de Identidad (SEG-31), el carrito de visitante se fusiona al ingresar y las respuestas no se cachean.
/// </summary>
public sealed class PantallasDeAccesoTests : IAsyncLifetime
{
    private static readonly Guid Usuario = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly string Desafio = new('d', 43);
    private static readonly DateTimeOffset Vence = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private AccesoApiHost _host = null!;

    public async Task InitializeAsync() => _host = await AccesoApiHost.Crear();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static CredencialDeSesion Credencial(RolDeCuenta rol = RolDeCuenta.Cliente) =>
        new(new ValorSecreto("jwt-secreto-de-prueba"), Vence, new ValorSecreto("renovacion-secreta-de-prueba"), Vence.AddDays(7), Usuario, rol, rol == RolDeCuenta.EncargadoDeSucursal ? Guid.NewGuid() : null, CorreoVerificado: true);

    private static Resultado<ResultadoDelIngreso> Completo(RolDeCuenta rol = RolDeCuenta.Cliente) => Resultado<ResultadoDelIngreso>.Exito(new ResultadoDelIngreso { Credencial = Credencial(rol) });

    private static Resultado<ResultadoDelIngreso> ConDesafio(PasoPendienteDelIngreso paso) =>
        Resultado<ResultadoDelIngreso>.Exito(new ResultadoDelIngreso { Desafio = new DesafioDelIngreso(new ValorSecreto(Desafio), paso, Vence) });

    private static readonly KeyValuePair<string, string>[] Datos = [new("correo", "ana@correo.example"), new("contrasena", "una frase larga y segura")];

    private static string CookieDelDesafio => $"{BabelHub.Ecommerce.Api.Web.CookieDelDesafio.Nombre}={Desafio}";

    private static string Cuerpo(HttpResponseMessage respuesta) => respuesta.Content.ReadAsStringAsync().GetAwaiter().GetResult();

    private static IEnumerable<string> Galletas(HttpResponseMessage respuesta) => respuesta.Headers.TryGetValues("Set-Cookie", out var valores) ? valores : [];

    // ---------- Ingreso ----------

    [Fact]
    [Trait("Criterio", "AC-WEB-19")]
    public async Task El_formulario_de_ingreso_deja_pegar_y_autocompletar_lleva_el_token_la_verificacion_y_no_se_cachea()
    {
        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir("/ingreso"));

        var html = await respuesta.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("autocomplete=\"current-password\"", html, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"username\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("onpaste", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=\"__RequestVerificationToken\"", html, StringComparison.Ordinal);
        Assert.Contains("cf-turnstile", html, StringComparison.Ordinal);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Empty(_host.Ingreso.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-19")]
    public async Task Un_ingreso_completo_abre_la_sesion_del_servidor_y_la_credencial_nunca_llega_al_navegador()
    {
        _host.Ingreso.Resultado = Completo();

        using var respuesta = await _host.Enviar("/ingreso", Datos);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal("/", respuesta.Headers.Location?.OriginalString);
        var sesion = Assert.Single(_host.Sesiones);
        Assert.Equal((HostWeb.Tienda, Usuario, RolWeb.Cliente), (sesion.Host, sesion.Ingreso.UsuarioId, sesion.Ingreso.Rol));
        Assert.Equal(("jwt-secreto-de-prueba", "renovacion-secreta-de-prueba"), (sesion.Ingreso.Credencial, sesion.Ingreso.Renovacion));
        var galletas = Galletas(respuesta).ToList();
        Assert.Contains(galletas, g => g.StartsWith("__Host-bh-tienda=valor-opaco-de-la-sesion", StringComparison.Ordinal) && g.Contains("httponly", StringComparison.OrdinalIgnoreCase) && g.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(galletas, g => g.StartsWith("__Host-bh-dispositivo=marca-nueva", StringComparison.Ordinal));
        Assert.DoesNotContain(galletas, g => g.Contains("jwt-secreto", StringComparison.Ordinal) || g.Contains("renovacion-secreta", StringComparison.Ordinal));
        Assert.DoesNotContain("jwt-secreto", Cuerpo(respuesta), StringComparison.Ordinal);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-47")]
    public async Task Un_ingreso_completo_informa_a_Identidad_el_dispositivo_como_el_titular_y_guarda_en_la_cookie_la_marca_que_Identidad_emite()
    {
        _host.Ingreso.Resultado = Completo();

        using var respuesta = await _host.Enviar("/ingreso", Datos);

        var registro = Assert.Single(_host.RegistrosDeDispositivo);
        Assert.Equal((Usuario, (string?)null, (Guid?)Usuario), (registro.Usuario, registro.Marca, registro.Actor));
        Assert.Contains(Galletas(respuesta), g => g.StartsWith("__Host-bh-dispositivo=marca-nueva-de-dispositivo-0123456789abcdefghijk", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-47")]
    public async Task Un_ingreso_con_la_marca_de_un_dispositivo_la_presenta_a_Identidad_y_no_emite_otra_cookie()
    {
        _host.Ingreso.Resultado = Completo();
        const string Marca = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ";

        using var respuesta = await _host.Enviar("/ingreso", Datos, cookies: $"__Host-bh-dispositivo={Marca}");

        Assert.Equal(Marca, Assert.Single(_host.RegistrosDeDispositivo).Marca);
        Assert.DoesNotContain(Galletas(respuesta), g => g.StartsWith("__Host-bh-dispositivo=", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-47")]
    public async Task Si_Identidad_no_puede_registrar_el_dispositivo_el_ingreso_igual_se_abre_sin_marca()
    {
        _host.Ingreso.Resultado = Completo();
        _host.DispositivosFallan = true;

        using var respuesta = await _host.Enviar("/ingreso", Datos);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Single(_host.Sesiones);
        Assert.DoesNotContain(Galletas(respuesta), g => g.StartsWith("__Host-bh-dispositivo=", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-19")]
    public async Task Un_ingreso_incorrecto_responde_lo_mismo_exista_o_no_la_cuenta_y_no_repite_lo_escrito()
    {
        _host.Ingreso.Resultado = Resultado<ResultadoDelIngreso>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, "credenciales-invalidas"));
        using var existe = await _host.Enviar("/ingreso", Datos);
        using var noExiste = await _host.Enviar("/ingreso", [new("correo", "otra@correo.example"), new("contrasena", "otra frase distinta")]);

        Assert.Equal(HttpStatusCode.Unauthorized, existe.StatusCode);
        Assert.Equal(existe.StatusCode, noExiste.StatusCode);
        var uno = Cuerpo(existe);
        Assert.Contains("El correo o la contraseña no son correctos", System.Net.WebUtility.HtmlDecode(uno), StringComparison.Ordinal);
        Assert.DoesNotContain("una frase larga", uno, StringComparison.Ordinal);
        Assert.DoesNotContain("ana@correo.example", uno, StringComparison.Ordinal);
        Assert.Empty(_host.Sesiones);
        Assert.DoesNotContain(Galletas(existe), g => g.StartsWith("__Host-bh-tienda=", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-19")]
    [InlineData(CategoriaDeError.Limite, HttpStatusCode.TooManyRequests, "demasiados intentos")]
    [InlineData(CategoriaDeError.NoDisponible, HttpStatusCode.ServiceUnavailable, "No podemos atenderte ahora")]
    public async Task El_limite_y_la_caida_de_Identidad_se_dicen_sin_pantalla_tecnica(CategoriaDeError categoria, HttpStatusCode estado, string texto)
    {
        _host.Ingreso.Resultado = Resultado<ResultadoDelIngreso>.Fallo(new ErrorDeAplicacion(categoria, "x"));

        using var respuesta = await _host.Enviar("/ingreso", Datos);

        Assert.Equal(estado, respuesta.StatusCode);
        Assert.Contains(texto, System.Net.WebUtility.HtmlDecode(Cuerpo(respuesta)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Una_excepcion_de_Identidad_es_servicio_no_disponible_y_nunca_una_pagina_de_error()
    {
        _host.Ingreso.Lanza = new InvalidOperationException("se cayó");

        using var respuesta = await _host.Enviar("/ingreso", Datos);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.DoesNotContain("se cayó", Cuerpo(respuesta), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-28")]
    [InlineData(ResultadoDeExigencia.Requerida, HttpStatusCode.Forbidden)]
    [InlineData(ResultadoDeExigencia.Fallida, HttpStatusCode.Forbidden)]
    [InlineData(ResultadoDeExigencia.NoDisponible, HttpStatusCode.ServiceUnavailable)]
    public async Task La_verificacion_de_persona_va_antes_de_Identidad_y_sin_ella_no_se_comprueba_la_contrasena(ResultadoDeExigencia exigencia, HttpStatusCode estado)
    {
        _host.Verificacion.Resultado = exigencia;
        _host.Ingreso.Resultado = Completo();

        using var respuesta = await _host.Enviar("/ingreso", [.. Datos, new("cf-turnstile-response", "token-de-turnstile")]);

        Assert.Equal(estado, respuesta.StatusCode);
        Assert.Equal("token-de-turnstile", _host.Verificacion.UltimoToken);
        Assert.Empty(_host.Ingreso.Llamadas);
        Assert.Empty(_host.Sesiones);
    }

    [Fact]
    public async Task Un_campo_de_mas_en_el_ingreso_es_un_error_de_forma_sin_llamar_a_Identidad()
    {
        using var respuesta = await _host.Enviar("/ingreso", [.. Datos, new("rol", "administrador")]);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(_host.Ingreso.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task Una_pareja_de_rol_y_host_que_no_corresponde_se_dice_igual_que_un_dato_incorrecto_y_no_deja_cookie()
    {
        _host.Ingreso.Resultado = Completo(RolDeCuenta.AdministradorCentral);
        _host.IniciarRechaza = true;

        using var respuesta = await _host.Enviar("/ingreso", Datos);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Contains("El correo o la contraseña no son correctos", System.Net.WebUtility.HtmlDecode(Cuerpo(respuesta)), StringComparison.Ordinal);
        Assert.DoesNotContain(Galletas(respuesta), g => g.StartsWith("__Host-bh-tienda=", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task La_pareja_invalida_y_la_contrasena_incorrecta_responden_con_las_mismas_cabeceras_y_la_sesion_descartada_se_revoca()
    {
        _host.Ingreso.Resultado = Resultado<ResultadoDelIngreso>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, "credenciales-invalidas"));
        using var incorrecta = await _host.Enviar("/ingreso", Datos);
        _host.Ingreso.Resultado = Completo(RolDeCuenta.AdministradorCentral);
        _host.IniciarRechaza = true;
        using var pareja = await _host.Enviar("/ingreso", Datos);

        Assert.Equal(incorrecta.StatusCode, pareja.StatusCode);
        Assert.Equal(Galletas(incorrecta).Order(), Galletas(pareja).Order());
        Assert.Equal(["cerrar"], _host.Revocaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-19")]
    public async Task Al_ingresar_un_cliente_se_fusiona_el_carrito_del_visitante_y_el_ingreso_no_depende_de_ello()
    {
        _host.Ingreso.Resultado = Completo();

        using var respuesta = await _host.Enviar("/ingreso", Datos, cookies: $"{BabelHub.Ecommerce.Api.Carrito.CookieDelCarrito.Nombre}={new string('c', 43)}");

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal(["fusionar"], _host.LlamadasDeCarrito);
    }

    [Fact]
    public async Task Un_encargado_llega_a_su_area_y_no_se_fusiona_ningun_carrito()
    {
        _host.Ingreso.Resultado = Completo(RolDeCuenta.EncargadoDeSucursal);

        using var respuesta = await _host.Enviar("/ingreso", Datos, cookies: $"{BabelHub.Ecommerce.Api.Carrito.CookieDelCarrito.Nombre}={new string('c', 43)}");

        Assert.Equal("/logistica/envios", respuesta.Headers.Location?.OriginalString);
        Assert.Empty(_host.LlamadasDeCarrito);
    }

    // ---------- El desafío en cookie ----------

    [Theory]
    [Trait("Criterio", "AC-WEB-28")]
    [InlineData(PasoPendienteDelIngreso.PresentarSegundoFactor, "/ingreso/segundo-factor")]
    [InlineData(PasoPendienteDelIngreso.ConfigurarSegundoFactor, "/ingreso/configuracion")]
    [InlineData(PasoPendienteDelIngreso.CambiarContrasenaTemporal, "/ingreso/contrasena")]
    public async Task Un_ingreso_con_un_paso_pendiente_guarda_el_desafio_en_su_cookie_y_nunca_en_la_URL_ni_en_un_campo(PasoPendienteDelIngreso paso, string accion)
    {
        _host.Ingreso.Resultado = ConDesafio(paso);
        using var enviado = await _host.Enviar("/ingreso", Datos);
        _host.Ingreso.Paso = paso;

        using var pantalla = await _host.Cliente.SendAsync(_host.Pedir("/ingreso", CookieDelDesafio));

        Assert.Equal(HttpStatusCode.SeeOther, enviado.StatusCode);
        Assert.Equal("/ingreso", enviado.Headers.Location?.OriginalString);
        Assert.DoesNotContain(Desafio, enviado.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Contains(Galletas(enviado), g => g.StartsWith($"__Host-bh-desafio={Desafio}", StringComparison.Ordinal) && g.Contains("httponly", StringComparison.OrdinalIgnoreCase) && g.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase) && !g.Contains("expires", StringComparison.OrdinalIgnoreCase));
        var html = Cuerpo(pantalla);
        Assert.Contains($"action=\"{accion}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(Desafio, html, StringComparison.Ordinal);
        Assert.Empty(_host.Sesiones);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-28")]
    public async Task El_segundo_factor_con_un_codigo_valido_abre_la_sesion_y_borra_la_cookie_del_desafio()
    {
        _host.Ingreso.Resultado = Completo();

        using var respuesta = await _host.Enviar("/ingreso/segundo-factor", [new("codigo", "123 456")], cookies: CookieDelDesafio);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal("123 456", _host.Ingreso.UltimoCodigo);
        Assert.Single(_host.Sesiones);
        Assert.Contains(Galletas(respuesta), g => g.StartsWith("__Host-bh-desafio=;", StringComparison.Ordinal) || g.StartsWith("__Host-bh-desafio=", StringComparison.Ordinal) && g.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-28")]
    public async Task Un_codigo_invalido_se_dice_sin_cerrar_el_desafio_y_un_desafio_vencido_empieza_de_nuevo()
    {
        _host.Ingreso.Resultado = Resultado<ResultadoDelIngreso>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, "codigo-invalido"));
        using var invalido = await _host.Enviar("/ingreso/segundo-factor", [new("codigo", "000000")], cookies: CookieDelDesafio);
        _host.Ingreso.Resultado = Resultado<ResultadoDelIngreso>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, "desafio-invalido"));
        using var vencido = await _host.Enviar("/ingreso/segundo-factor", [new("codigo", "000000")], cookies: CookieDelDesafio);

        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.Contains("El código no es válido", System.Net.WebUtility.HtmlDecode(Cuerpo(invalido)), StringComparison.Ordinal);
        Assert.DoesNotContain(Galletas(invalido), g => g.StartsWith("__Host-bh-desafio=", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Unauthorized, vencido.StatusCode);
        Assert.Contains(Galletas(vencido), g => g.StartsWith("__Host-bh-desafio=", StringComparison.Ordinal) && g.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("action=\"/ingreso\"", Cuerpo(vencido), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-28")]
    public async Task Sin_la_cookie_del_desafio_los_pasos_vuelven_al_ingreso_sin_llamar_a_Identidad()
    {
        foreach (var ruta in new[] { "/ingreso/segundo-factor", "/ingreso/configuracion", "/ingreso/contrasena" })
        {
            var campos = ruta.EndsWith("contrasena", StringComparison.Ordinal) ? new KeyValuePair<string, string>[] { new("contrasenaNueva", "x"), new("confirmacion", "x") } : [new("codigo", "123456")];
            using var respuesta = await _host.Enviar(ruta, campos);

            Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
            Assert.Equal("/ingreso", respuesta.Headers.Location?.OriginalString);
        }

        Assert.Empty(_host.Ingreso.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-28")]
    public async Task Configurar_el_segundo_factor_muestra_el_secreto_una_vez_y_al_confirmar_entrega_los_codigos_de_respaldo_en_la_respuesta_que_abre_la_sesion()
    {
        _host.Ingreso.Paso = PasoPendienteDelIngreso.ConfigurarSegundoFactor;
        using var configuracion = await _host.Cliente.SendAsync(_host.Pedir("/ingreso", CookieDelDesafio));
        _host.Ingreso.Resultado = Resultado<ResultadoDelIngreso>.Exito(new ResultadoDelIngreso { Credencial = Credencial(RolDeCuenta.EncargadoDeSucursal), CodigosDeRespaldo = [new ValorSecreto("AAAA-1111"), new ValorSecreto("BBBB-2222")] });

        using var confirmado = await _host.Enviar("/ingreso/configuracion", [new("codigo", "654321")], cookies: CookieDelDesafio);

        var pantalla = Cuerpo(configuracion);
        Assert.Contains("JBSWY3DPEHPK3PXP", pantalla, StringComparison.Ordinal);
        Assert.Contains("otpauth://totp/", pantalla, StringComparison.Ordinal);
        Assert.Contains("no-store", configuracion.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, confirmado.StatusCode);
        var codigos = Cuerpo(confirmado);
        Assert.Contains("AAAA-1111", codigos, StringComparison.Ordinal);
        Assert.Contains("BBBB-2222", codigos, StringComparison.Ordinal);
        Assert.Contains("solo esta vez", System.Net.WebUtility.HtmlDecode(codigos), StringComparison.Ordinal);
        Assert.Contains("no-store", confirmado.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Single(_host.Sesiones);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-28")]
    public async Task La_contrasena_temporal_se_cambia_con_dos_copias_iguales_y_sigue_con_el_paso_que_Identidad_pida()
    {
        _host.Ingreso.Resultado = ConDesafio(PasoPendienteDelIngreso.ConfigurarSegundoFactor);

        using var distintas = await _host.Enviar("/ingreso/contrasena", [new("contrasenaNueva", "una frase nueva y segura"), new("confirmacion", "otra distinta")], cookies: CookieDelDesafio);
        Assert.Empty(_host.Ingreso.Llamadas);
        using var iguales = await _host.Enviar("/ingreso/contrasena", [new("contrasenaNueva", "una frase nueva y segura"), new("confirmacion", "una frase nueva y segura")], cookies: CookieDelDesafio);

        Assert.Equal(HttpStatusCode.BadRequest, distintas.StatusCode);
        Assert.Contains("no coinciden", System.Net.WebUtility.HtmlDecode(Cuerpo(distintas)), StringComparison.Ordinal);
        Assert.DoesNotContain("una frase nueva", Cuerpo(distintas), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.SeeOther, iguales.StatusCode);
        Assert.Equal("/ingreso", iguales.Headers.Location?.OriginalString);
        Assert.Equal(["cambiar-contrasena"], _host.Ingreso.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-28")]
    public async Task Cancelar_descarta_el_desafio_en_Identidad_y_borra_la_cookie()
    {
        using var respuesta = await _host.Enviar("/ingreso/cancelar", [], cookies: CookieDelDesafio);

        Assert.Equal(["cancelar"], _host.Ingreso.Llamadas);
        Assert.Contains(Galletas(respuesta), g => g.StartsWith("__Host-bh-desafio=", StringComparison.Ordinal) && g.Contains("expires=", StringComparison.OrdinalIgnoreCase));
    }

    // ---------- Registro ----------

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task El_registro_ofrece_las_versiones_vigentes_la_mayoria_de_edad_y_la_verificacion_de_persona()
    {
        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir("/registro"));

        var html = Cuerpo(respuesta);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("name=\"versionDeTerminos\" value=\"T-2026-11\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"versionDePrivacidad\" value=\"P-2026-11\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"acepto\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"mayorDeEdad\"", html, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"new-password\"", html, StringComparison.Ordinal);
        Assert.Contains("cf-turnstile", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task El_registro_sin_aceptar_o_sin_declarar_la_mayoria_de_edad_no_llega_a_Identidad()
    {
        var campos = new KeyValuePair<string, string>[] { new("nombre", "Ana Pérez"), new("correo", "ana@correo.example"), new("contrasena", "una frase larga y segura"), new("versionDeTerminos", "T-2026-11"), new("versionDePrivacidad", "P-2026-11") };

        using var sinMarcas = await _host.Enviar("/registro", campos);
        using var soloAcepto = await _host.Enviar("/registro", [.. campos, new("acepto", "si")]);

        Assert.Equal(HttpStatusCode.BadRequest, sinMarcas.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, soloAcepto.StatusCode);
        Assert.Empty(_host.Registro.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Un_registro_valido_pasa_las_versiones_aceptadas_y_pide_el_codigo_sin_decir_si_la_cuenta_existia()
    {
        var campos = new KeyValuePair<string, string>[] { new("nombre", "Ana Pérez"), new("correo", "ana@correo.example"), new("contrasena", "una frase larga y segura"), new("versionDeTerminos", "T-2026-11"), new("versionDePrivacidad", "P-2026-11"), new("acepto", "si"), new("mayorDeEdad", "si") };

        using var respuesta = await _host.Enviar("/registro", campos);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var alta = _host.Registro.UltimaAlta!;
        Assert.Equal(("T-2026-11", "P-2026-11", true), (alta.VersionDeTerminos, alta.VersionDePrivacidad, alta.DeclaraSerMayorDeEdad));
        var html = Cuerpo(respuesta);
        Assert.Contains("action=\"/verificacion\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"ana@correo.example\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("una frase larga", html, StringComparison.Ordinal);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public async Task El_registro_tambien_exige_la_verificacion_de_persona_antes_de_Identidad()
    {
        _host.Verificacion.Resultado = ResultadoDeExigencia.Requerida;

        using var respuesta = await _host.Enviar("/registro", [new("nombre", "Ana"), new("correo", "ana@correo.example"), new("contrasena", "una frase larga y segura"), new("acepto", "si"), new("mayorDeEdad", "si")]);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Empty(_host.Registro.Llamadas);
    }

    // ---------- Verificación y recuperación ----------

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Verificar_el_correo_abre_la_sesion_y_un_codigo_malo_se_dice_sin_repetir_el_codigo()
    {
        using var malo = await _host.Enviar("/verificacion", [new("correo", "ana@correo.example"), new("codigo", "999999")]);
        _host.Registro.Verificacion = Completo();
        using var bueno = await _host.Enviar("/verificacion", [new("correo", "ana@correo.example"), new("codigo", "123456")]);

        Assert.Equal(HttpStatusCode.BadRequest, malo.StatusCode);
        Assert.DoesNotContain("999999", Cuerpo(malo), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.SeeOther, bueno.StatusCode);
        Assert.Single(_host.Sesiones);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Reenviar_el_codigo_responde_lo_mismo_exista_o_no_la_cuenta()
    {
        using var respuesta = await _host.Enviar("/verificacion/reenviar", [new("correo", "nadie@correo.example")]);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("Si el correo corresponde a una cuenta", System.Net.WebUtility.HtmlDecode(Cuerpo(respuesta)), StringComparison.Ordinal);
        Assert.Equal(["reenviar"], _host.Registro.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task La_recuperacion_responde_igual_exista_o_no_la_cuenta_y_confirma_con_la_contrasena_nueva_dos_veces()
    {
        using var solicitada = await _host.Enviar("/recuperacion", [new("correo", "nadie@correo.example")]);
        using var distintas = await _host.Enviar("/recuperacion/confirmar", [new("correo", "ana@correo.example"), new("codigo", "123456"), new("contrasenaNueva", "una frase nueva y segura"), new("confirmacion", "otra")]);
        using var confirmada = await _host.Enviar("/recuperacion/confirmar", [new("correo", "ana@correo.example"), new("codigo", "123456"), new("contrasenaNueva", "una frase nueva y segura"), new("confirmacion", "una frase nueva y segura")]);

        Assert.Equal(HttpStatusCode.OK, solicitada.StatusCode);
        Assert.Contains("Si el correo corresponde a una cuenta", System.Net.WebUtility.HtmlDecode(Cuerpo(solicitada)), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, distintas.StatusCode);
        Assert.Equal(HttpStatusCode.OK, confirmada.StatusCode);
        Assert.Contains("Tu contraseña se cambió", System.Net.WebUtility.HtmlDecode(Cuerpo(confirmada)), StringComparison.Ordinal);
        Assert.DoesNotContain("una frase nueva", Cuerpo(confirmada), StringComparison.Ordinal);
        Assert.Equal(["solicitar", "confirmar"], _host.Recuperacion.Llamadas);
    }

    [Theory]
    [InlineData("/ingreso/segundo-factor")]
    [InlineData("/registro")]
    [InlineData("/verificacion")]
    [InlineData("/recuperacion")]
    public async Task Todo_POST_sin_token_antifalsificacion_se_rechaza_sin_llamar_a_Identidad(string ruta)
    {
        using var respuesta = await _host.Enviar(ruta, [new("correo", "ana@correo.example")], conToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(_host.Ingreso.Llamadas);
        Assert.Empty(_host.Registro.Llamadas);
        Assert.Empty(_host.Recuperacion.Llamadas);
    }
}
