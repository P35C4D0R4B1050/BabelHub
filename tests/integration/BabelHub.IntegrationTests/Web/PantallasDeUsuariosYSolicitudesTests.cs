using System.Net;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.IntegrationTests.Pagos;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// T30: la administración de usuarios y roles y de las solicitudes sobre datos personales (AC-WEB-29), con las vistas reales y los contratos de Identidad doblados: la contraseña temporal se muestra una sola vez, los datos del cliente salen codificados contra XSS almacenado, el esquema de cada cuerpo es estricto
/// y un rol sin permiso ve el motivo sin llegar a ejecutar nada.
/// </summary>
public sealed class PantallasDeUsuariosYSolicitudesTests : IAsyncLifetime
{
    private static readonly Guid Usuario = Guid.Parse("b0000000-0000-4000-8000-000000000001");
    private static readonly Guid Solicitud = Guid.Parse("c0000000-0000-4000-8000-000000000002");

    private readonly SesionesWebDePrueba _sesiones = new SesionesWebDePrueba().ConSesionValida(RolWeb.AdministradorCentral);
    private readonly List<string> _llamadas = [];
    private readonly Dictionary<string, object?> _argumentos = [];
    private ErrorDeAplicacion? _falla;
    private AnfitrionWeb _anfitrion = null!;

    private static UsuarioAdministrado Administrado(RolDeCuenta rol = RolDeCuenta.Logistica) => new(Usuario, "Luis <i>Soto</i>", "luis@babelhub.example", rol, null, true, true);

    private Resultado<T> De<T>(T valor) => _falla is null ? Resultado<T>.Exito(valor) : Resultado<T>.Fallo(_falla);

    public async Task InitializeAsync()
    {
        var usuarios = DobleDeMetodos.Crear<IAdministracionDeUsuarios>(out var dU);
        dU.Con(nameof(IAdministracionDeUsuarios.Crear), a => { _llamadas.Add("crear"); _argumentos["rol"] = a[3]; _argumentos["sucursal"] = a[4]; return Task.FromResult(De(new ContrasenaTemporalEntregada(Administrado(), new ValorSecreto("Temporal-Una-Sola-Vez-9"), new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero)))); });
        dU.Con(nameof(IAdministracionDeUsuarios.CambiarElRol), a => { _llamadas.Add("rol"); _argumentos["rol"] = a[2]; _argumentos["sucursalDelCambio"] = a[3]; return Task.FromResult(De(Administrado(RolDeCuenta.EncargadoDeSucursal))); });
        dU.Con(nameof(IAdministracionDeUsuarios.Listar), _ => Task.FromResult(De(new PaginaDeUsuarios([Administrado()], null))));
        dU.Con(nameof(IAdministracionDeUsuarios.Desactivar), _ => { _llamadas.Add("desactivar"); return Task.FromResult(De(Administrado() with { Activo = false })); });
        dU.Con(nameof(IAdministracionDeUsuarios.Reactivar), _ => { _llamadas.Add("reactivar"); return Task.FromResult(De(Administrado())); });
        dU.Con(nameof(IAdministracionDeUsuarios.RestablecerLaContrasena), _ => { _llamadas.Add("contrasena"); return Task.FromResult(De(new ContrasenaTemporalEntregada(Administrado(), new ValorSecreto("Otra-Temporal-9"), new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero)))); });
        dU.Con(nameof(IAdministracionDeUsuarios.RestablecerElSegundoFactor), _ => { _llamadas.Add("segundo-factor"); return Task.FromResult(De(new RespuestaUniforme())); });
        var solicitudes = DobleDeMetodos.Crear<ISolicitudesDeDatos>(out var dS);
        var s = new SolicitudSobreDatos(Solicitud, TipoDeSolicitudDeDatos.Acceso, "en atención", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 16), true);
        dS.Con(nameof(ISolicitudesDeDatos.Listar), _ => Task.FromResult(_falla is null ? Resultado<IReadOnlyList<SolicitudSobreDatos>>.Exito([s]) : Resultado<IReadOnlyList<SolicitudSobreDatos>>.Fallo(_falla)));
        dS.Con(nameof(ISolicitudesDeDatos.Consultar), _ => Task.FromResult(De(new DetalleDeSolicitud(s, Usuario, "<script>alert('x')</script> Te enviamos tus datos.", null))));
        dS.Con(nameof(ISolicitudesDeDatos.Responder), a => { _llamadas.Add("responder"); _argumentos["resolucion"] = a[2]; _argumentos["motivo"] = a[4]; return Task.FromResult(De(s)); });
        _anfitrion = await AnfitrionConSesion.Iniciar(_sesiones, HostWeb.Gestion, servicios: sv => { sv.AddSingleton(usuarios); sv.AddSingleton(solicitudes); });
    }

    public async Task DisposeAsync() => await _anfitrion.DisposeAsync();

    private Task<HttpResponseMessage> Pedir(string ruta)
    {
        var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        solicitud.Headers.TryAddWithoutValidation("Cookie", AnfitrionConSesion.Cookie(HostWeb.Gestion));
        return _anfitrion.Cliente.SendAsync(solicitud);
    }

    private async Task<HttpResponseMessage> Enviar(string ruta, params (string Campo, string Valor)[] campos)
    {
        using var solicitud = await AnfitrionConSesion.Post(_anfitrion, ruta, AnfitrionConSesion.Cookie(HostWeb.Gestion));
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        solicitud.Content = new FormUrlEncodedContent(campos.Select(c => new KeyValuePair<string, string>(c.Campo, c.Valor)));
        return await _anfitrion.Cliente.SendAsync(solicitud);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task Crear_un_usuario_muestra_la_contrasena_temporal_una_sola_vez_y_codifica_sus_datos()
    {
        using var creado = await Enviar("/administracion/usuarios/crear", ("nombre", "Luis <i>Soto</i>"), ("correo", "luis@babelhub.example"), ("rol", "Logistica"), ("sucursal", ""));
        using var despues = await Pedir("/administracion/usuarios");

        var crudo = await creado.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, creado.StatusCode);
        Assert.Contains("Temporal-Una-Sola-Vez-9", crudo, StringComparison.Ordinal);
        Assert.DoesNotContain("<i>Soto</i>", crudo, StringComparison.Ordinal);
        Assert.Contains("&lt;i&gt;Soto&lt;/i&gt;", crudo, StringComparison.Ordinal);
        Assert.Contains("no-store", creado.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal(RolDeCuenta.Logistica, _argumentos["rol"]);
        Assert.DoesNotContain("Temporal-Una-Sola-Vez-9", await despues.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task Cambiar_el_rol_desactivar_reactivar_y_restablecer_llaman_a_Identidad_con_el_identificador_del_usuario()
    {
        using var rol = await Enviar("/administracion/usuarios/rol", ("usuarioId", Usuario.ToString("D")), ("rol", "EncargadoDeSucursal"), ("sucursal", " Ayacucho "));
        using var desactivado = await Enviar("/administracion/usuarios/desactivar", ("usuarioId", Usuario.ToString("D")));
        using var reactivado = await Enviar("/administracion/usuarios/reactivar", ("usuarioId", Usuario.ToString("D")));
        using var contrasena = await Enviar("/administracion/usuarios/contrasena", ("usuarioId", Usuario.ToString("D")));
        using var factor = await Enviar("/administracion/usuarios/segundo-factor", ("usuarioId", Usuario.ToString("D")));

        Assert.All(new[] { rol, desactivado, reactivado, contrasena, factor }, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(["rol", "desactivar", "reactivar", "contrasena", "segundo-factor"], _llamadas);
        Assert.Contains("Otra-Temporal-9", await contrasena.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(RolDeCuenta.EncargadoDeSucursal, _argumentos["rol"]);
        Assert.Equal("Ayacucho", _argumentos["sucursalDelCambio"]); // D20: la sucursal viaja por código, sin espacios en los extremos
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-29")]
    [InlineData("rol", "Dueño")]
    [InlineData("rol", "0")]
    [InlineData("sucursal", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Un_rol_o_una_sucursal_invalidos_no_llegan_a_Identidad(string campo, string valor)
    {
        using var respuesta = await Enviar("/administracion/usuarios/crear", ("nombre", "Luis"), ("correo", "luis@babelhub.example"), (campo == "rol" ? "rol" : "rol", campo == "rol" ? valor : "Logistica"), ("sucursal", campo == "sucursal" ? valor : string.Empty));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(_llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task Un_campo_de_mas_o_un_identificador_invalido_se_rechazan_sin_llamar_a_Identidad()
    {
        using var deMas = await Enviar("/administracion/usuarios/desactivar", ("usuarioId", Usuario.ToString("D")), ("rol", "AdministradorCentral"));
        using var invalido = await Enviar("/administracion/usuarios/desactivar", ("usuarioId", "1; DROP TABLE"));

        Assert.Equal(HttpStatusCode.BadRequest, deMas.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.Empty(_llamadas);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-29")]
    [InlineData(CategoriaDeError.Prohibido, HttpStatusCode.Forbidden, "No tienes permiso")]
    [InlineData(CategoriaDeError.NoEncontrado, HttpStatusCode.NotFound, "No encontramos ese usuario")]
    [InlineData(CategoriaDeError.Conflicto, HttpStatusCode.Conflict, "algo cambió")]
    [InlineData(CategoriaDeError.NoDisponible, HttpStatusCode.ServiceUnavailable, "No podemos atenderte ahora")]
    public async Task Los_errores_de_Identidad_se_dicen_sin_repetir_lo_que_respondio(CategoriaDeError categoria, HttpStatusCode estado, string texto)
    {
        _falla = new ErrorDeAplicacion(categoria, "detalle-interno-que-no-se-muestra");

        using var respuesta = await Enviar("/administracion/usuarios/desactivar", ("usuarioId", Usuario.ToString("D")));

        Assert.Equal(estado, respuesta.StatusCode);
        var html = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
        Assert.Contains(texto, html, StringComparison.Ordinal);
        Assert.DoesNotContain("detalle-interno", html, StringComparison.Ordinal);
    }


    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task La_pantalla_lista_al_personal_codificado_y_si_la_lista_falla_lo_dice_sin_romper_las_acciones()
    {
        using var conLista = await Pedir("/administracion/usuarios");
        _falla = new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "detalle-interno-que-no-se-muestra");
        using var sinLista = await Pedir("/administracion/usuarios");

        var crudo = await conLista.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, conLista.StatusCode);
        Assert.Contains("Personal registrado", crudo, StringComparison.Ordinal);
        Assert.DoesNotContain("<i>Soto</i>", crudo, StringComparison.Ordinal);
        Assert.Contains("&lt;i&gt;Soto&lt;/i&gt;", crudo, StringComparison.Ordinal);
        Assert.Contains("luis@babelhub.example", crudo, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, sinLista.StatusCode);
        var html = WebUtility.HtmlDecode(await sinLista.Content.ReadAsStringAsync());
        Assert.Contains("No pudimos leer la lista", html, StringComparison.Ordinal);
        Assert.DoesNotContain("detalle-interno", html, StringComparison.Ordinal);
        Assert.Contains("Otras acciones sobre un usuario", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task Si_Identidad_niega_la_lista_la_pantalla_dice_sin_permiso_y_no_que_no_esta_disponible()
    {
        _falla = new ErrorDeAplicacion(CategoriaDeError.Prohibido, "detalle-interno-que-no-se-muestra");

        using var respuesta = await Pedir("/administracion/usuarios");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        var html = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
        Assert.Contains("No tienes permiso", html, StringComparison.Ordinal);
        Assert.DoesNotContain("No pudimos leer la lista", html, StringComparison.Ordinal);
        Assert.DoesNotContain("detalle-interno", html, StringComparison.Ordinal);
    }
    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task Las_solicitudes_se_listan_con_su_fecha_limite_y_las_vencidas_sin_atender_se_senalan()
    {
        using var respuesta = await Pedir("/administracion/solicitudes");

        var html = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("16/11/2026", html, StringComparison.Ordinal);
        Assert.Contains("VENCIDA sin atender", html, StringComparison.Ordinal);
        Assert.Contains($"/administracion/solicitudes/{Solicitud:D}", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task El_detalle_codifica_lo_que_escribio_el_cliente_y_la_respuesta_envia_la_resolucion_y_el_motivo()
    {
        using var detalle = await Pedir($"/administracion/solicitudes/{Solicitud:D}");
        using var respondida = await Enviar($"/administracion/solicitudes/{Solicitud:D}/responder", ("resolucion", "Rechazada"), ("respuesta", "No procede."), ("motivo", "Sin titularidad"));

        var crudo = await detalle.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script>alert('x')</script>", crudo, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", crudo, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.SeeOther, respondida.StatusCode);
        Assert.Equal("/administracion/solicitudes?aviso=respondida", respondida.Headers.Location?.OriginalString);
        Assert.Equal((ResolucionDeSolicitud.Rechazada, "Sin titularidad"), ((ResolucionDeSolicitud)_argumentos["resolucion"]!, (string?)_argumentos["motivo"]));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task Una_resolucion_inventada_o_un_id_que_no_es_guid_no_llegan_a_Identidad()
    {
        using var invalida = await Enviar($"/administracion/solicitudes/{Solicitud:D}/responder", ("resolucion", "Borrar"));
        using var sinRuta = await Pedir("/administracion/solicitudes/no-es-guid");

        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sinRuta.StatusCode);
        Assert.Empty(_llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task Un_rol_sin_permiso_ve_el_motivo_en_la_lista_de_solicitudes()
    {
        _falla = new ErrorDeAplicacion(CategoriaDeError.Prohibido, "prohibido");

        using var respuesta = await Pedir("/administracion/solicitudes");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Contains("No tienes permiso", WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()), StringComparison.Ordinal);
    }
}
