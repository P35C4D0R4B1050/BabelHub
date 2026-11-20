using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Controllers;

public enum PantallaDeLaCuenta
{
    Resumen,
    Datos,
    SegundoFactor,
    Correo,
    Cancelacion,
    Solicitudes,
    Avisos,
    CodigosDeRespaldo,
}

/// <summary>Lo que dibuja la cuenta del cliente: la pantalla, lo que Identidad dijo (solo texto de lista cerrada) y los datos de cada sección. Ningún secreto se guarda: el de un segundo factor nuevo y los códigos de respaldo se muestran una vez y no vuelven.</summary>
public sealed class CuentaVista : ModeloBaseDePagina
{
    public PantallaDeLaCuenta Pantalla { get; set; }

    public string? Mensaje { get; set; }

    public bool EsError { get; set; }

    public CuentaDelCliente? Cuenta { get; set; }

    public IReadOnlyList<AvisoDeCuenta> Avisos { get; set; } = [];

    public EstadoDeLaCancelacionDeCuenta? Cancelacion { get; set; }

    public IReadOnlyList<SolicitudDelCliente> Solicitudes { get; set; } = [];

    public string? SecretoBase32 { get; set; }

    public string? UriDelSecreto { get; set; }

    public IReadOnlyList<string> Codigos { get; set; } = [];

    public IReadOnlyDictionary<string, string> Borrador { get; set; } = new Dictionary<string, string>();

    public bool ConAcciones => !TextosPendientes;
}

/// <summary>
/// La cuenta del cliente (T29; WEB-35, WEB-37, WEB-38, WEB-39): datos, consentimiento de WhatsApp, contraseña, segundo factor, avisos de seguridad, cambio de correo, cancelación diferida y solicitudes sobre datos personales, <b>sobre los contratos de Identidad en proceso</b>. <b>Presentación pura</b>: toda regla es de
/// Identidad. Con los textos pendientes se ven las pantallas de lectura (<c>[PermitidaConTextosPendientes(Cuenta)]</c>) sin acciones ni marcar avisos; cada acción es un <c>POST</c> con antifalsificación y esquema estricto, que vuelve con <c>303</c> a la pantalla con un aviso de lista cerrada (nunca lo escrito ni lo que respondió
/// Identidad). Al ejecutarse la cancelación se cierran todas las sesiones. Solo los clientes: el personal solo ve su sesión y salir.
/// </summary>
[RequiereSesion]
[SinRetorno]
public sealed class CuentaController(
    ICuentaDelCliente cuenta,
    ICambioDeCorreo correo,
    IDispositivosDeLaCuenta dispositivos,
    ICancelacionDeCuenta cancelacion,
    ISolicitudesDeDatos solicitudes,
    ISesionesWeb sesiones,
    IBorradoresDePantalla borradores,
    ContextoDePagina pagina,
    OpcionesDelHostWeb host) : Controller
{
    public const string Datos = "/cuenta/datos";

    private static readonly Dictionary<string, string> Avisos = new(StringComparer.Ordinal)
    {
        ["nombre-guardado"] = "Guardamos tu nombre.",
        ["whatsapp-guardado"] = "Guardamos tu preferencia de avisos por WhatsApp.",
        ["contrasena-cambiada"] = "Cambiamos tu contraseña. Te avisamos por correo.",
        ["segundo-factor-desactivado"] = "Desactivamos tu segundo factor.",
        ["aviso-visto"] = "Marcamos el aviso como visto.",
        ["correo-solicitado"] = "Te enviamos un código al correo nuevo. Escríbelo para verificarlo.",
        ["correo-verificado"] = "Verificamos tu correo nuevo.",
        ["correo-confirmado"] = "Confirmamos el cambio de correo.",
        ["correo-rechazado"] = "Rechazamos el cambio de correo.",
        ["cancelacion-solicitada"] = "Registramos tu solicitud de cancelación. Te mostramos qué falta cerrar.",
        ["cancelacion-reconfirmada"] = "Reconfirmaste la cancelación.",
        ["cancelacion-desistida"] = "Desististe de cancelar tu cuenta.",
        ["solicitud-presentada"] = "Presentamos tu solicitud. Responderemos antes de la fecha límite.",
    };

    private static readonly string[] SoloAviso = ["aviso"];

    // ---------- Lectura ----------

    [HttpGet("/cuenta")]
    [PermitidaConTextosPendientes(CapacidadWeb.Cuenta)]
    public Task<IActionResult> Resumen([FromQuery] string? aviso, CancellationToken cancelacion) => Mostrar(PantallaDeLaCuenta.Resumen, aviso, cancelacion);

    [HttpGet(Datos)]
    [PermitidaConTextosPendientes(CapacidadWeb.Cuenta)]
    public Task<IActionResult> DatosDeLaCuenta([FromQuery] string? aviso, CancellationToken cancelacion) => Mostrar(PantallaDeLaCuenta.Datos, aviso, cancelacion);

    [HttpGet("/cuenta/segundo-factor")]
    [PermitidaConTextosPendientes(CapacidadWeb.Cuenta)]
    public Task<IActionResult> SegundoFactor([FromQuery] string? aviso, CancellationToken cancelacion) => Mostrar(PantallaDeLaCuenta.SegundoFactor, aviso, cancelacion);

    [HttpGet("/cuenta/correo")]
    [PermitidaConTextosPendientes(CapacidadWeb.Cuenta)]
    public Task<IActionResult> Correo([FromQuery] string? aviso, CancellationToken cancelacion) => Mostrar(PantallaDeLaCuenta.Correo, aviso, cancelacion);

    [HttpGet("/cuenta/avisos")]
    [PermitidaConTextosPendientes(CapacidadWeb.Cuenta)]
    public Task<IActionResult> ListaDeAvisos([FromQuery] string? aviso, CancellationToken cancelacion) => Mostrar(PantallaDeLaCuenta.Avisos, aviso, cancelacion);

    [HttpGet("/cuenta/cancelacion")]
    [PermitidaConTextosPendientes(CapacidadWeb.Cuenta)]
    public Task<IActionResult> Cancelacion([FromQuery] string? aviso, CancellationToken cancelacion) => Mostrar(PantallaDeLaCuenta.Cancelacion, aviso, cancelacion);

    [HttpGet("/cuenta/solicitudes")]
    [PermitidaConTextosPendientes(CapacidadWeb.Cuenta)]
    public Task<IActionResult> Solicitudes([FromQuery] string? aviso, CancellationToken cancelacion) => Mostrar(PantallaDeLaCuenta.Solicitudes, aviso, cancelacion);

    // ---------- Acciones ----------

    [HttpPost(Datos)]
    public Task<IActionResult> GuardarElNombre([FromForm] FormularioDeDatosDeLaCuenta? formulario, CancellationToken cancelacion) =>
        Accion(["nombre"], PantallaDeLaCuenta.Datos, "nombre-guardado", async (actor, campos, token) =>
        {
            var resultado = await cuenta.CorregirElNombre(actor.UsuarioId, campos.GetValueOrDefault("nombre") ?? string.Empty, token);
            return resultado.EsExito ? null : resultado.Error;
        }, cancelacion);

    [HttpPost("/cuenta/whatsapp")]
    public Task<IActionResult> Whatsapp(CancellationToken cancelacion) =>
        Accion(["numero", "consiente"], PantallaDeLaCuenta.Datos, "whatsapp-guardado", async (actor, campos, token) =>
        {
            var numero = campos.GetValueOrDefault("numero");
            var guardado = await cuenta.GuardarElNumeroDeWhatsApp(actor.UsuarioId, string.IsNullOrWhiteSpace(numero) ? null : numero, token);
            if (!guardado.EsExito)
            {
                return guardado.Error;
            }

            var consentimiento = await cuenta.CambiarElConsentimientoDeWhatsApp(actor.UsuarioId, campos.ContainsKey("consiente"), string.IsNullOrWhiteSpace(numero) ? null : numero, token);
            return consentimiento.EsExito ? null : consentimiento.Error;
        }, cancelacion);

    [HttpPost("/cuenta/contrasena")]
    public Task<IActionResult> Contrasena(CancellationToken cancelacion) =>
        Accion(["actual", "nueva", "confirmacion"], PantallaDeLaCuenta.Datos, "contrasena-cambiada", async (actor, campos, token) =>
        {
            if (!campos.TryGetValue("actual", out var actual) || !campos.TryGetValue("nueva", out var nueva) || !campos.TryGetValue("confirmacion", out var confirmacion) || !string.Equals(nueva, confirmacion, StringComparison.Ordinal))
            {
                return new ErrorDeAplicacion(CategoriaDeError.Validacion, "confirmacion-distinta");
            }

            var dispositivo = DatosDelBorde.Leer(Request);
            var resultado = await cuenta.CambiarLaContrasena(actor.UsuarioId, new ValorSecreto(actual), new ValorSecreto(nueva), await RenovacionActual(token), dispositivo.Marca is null ? null : new ValorSecreto(dispositivo.Marca), token);
            return resultado.EsExito ? null : resultado.Error;
        }, cancelacion);

    [HttpPost("/cuenta/segundo-factor/iniciar")]
    public async Task<IActionResult> IniciarElSegundoFactor(CancellationToken cancelacion)
    {
        var (actor, campos, fallo) = await Preparar(["actual"], cancelacion);
        if (actor is null)
        {
            return fallo!;
        }

        var dispositivo = DatosDelBorde.Leer(Request);
        var resultado = await Llamar(() => cuenta.IniciarElSegundoFactor(actor.UsuarioId, new ValorSecreto(campos!.GetValueOrDefault("actual") ?? string.Empty), dispositivo.Marca is null ? null : new ValorSecreto(dispositivo.Marca), cancelacion), cancelacion);
        if (!resultado.EsExito)
        {
            return await Volver(PantallaDeLaCuenta.SegundoFactor, resultado.Error!, cancelacion);
        }

        // El secreto se muestra esta única vez y no se guarda.
        var vista = await Armar(PantallaDeLaCuenta.SegundoFactor, actor, null, cancelacion);
        vista.SecretoBase32 = resultado.Valor.SecretoBase32.Revelar();
        vista.UriDelSecreto = $"otpauth://totp/{Uri.EscapeDataString(resultado.Valor.Emisor)}:{Uri.EscapeDataString(resultado.Valor.Cuenta)}?secret={resultado.Valor.SecretoBase32.Revelar()}&issuer={Uri.EscapeDataString(resultado.Valor.Emisor)}";
        return Pintar(vista, StatusCodes.Status200OK);
    }

    [HttpPost("/cuenta/segundo-factor/confirmar")]
    public async Task<IActionResult> ConfirmarElSegundoFactor(CancellationToken cancelacion)
    {
        var (actor, campos, fallo) = await Preparar(["codigo"], cancelacion);
        if (actor is null)
        {
            return fallo!;
        }

        var dispositivo = DatosDelBorde.Leer(Request);
        var renovacion = await RenovacionActual(cancelacion);
        var resultado = await Llamar(() => cuenta.ConfirmarElSegundoFactor(actor.UsuarioId, new ValorSecreto(campos!.GetValueOrDefault("codigo") ?? string.Empty), renovacion, dispositivo.Marca is null ? null : new ValorSecreto(dispositivo.Marca), cancelacion), cancelacion);
        return resultado.EsExito ? await MostrarCodigos(actor, resultado.Valor, cancelacion) : await Volver(PantallaDeLaCuenta.SegundoFactor, resultado.Error!, cancelacion);
    }

    [HttpPost("/cuenta/segundo-factor/desactivar")]
    public Task<IActionResult> DesactivarElSegundoFactor(CancellationToken cancelacion) =>
        Accion(["actual", "codigo"], PantallaDeLaCuenta.SegundoFactor, "segundo-factor-desactivado", async (actor, campos, token) =>
        {
            var dispositivo = DatosDelBorde.Leer(Request);
            var resultado = await cuenta.DesactivarElSegundoFactor(actor.UsuarioId, new ValorSecreto(campos.GetValueOrDefault("actual") ?? string.Empty), new ValorSecreto(campos.GetValueOrDefault("codigo") ?? string.Empty), await RenovacionActual(token), dispositivo.Marca is null ? null : new ValorSecreto(dispositivo.Marca), token);
            return resultado.EsExito ? null : resultado.Error;
        }, cancelacion);

    [HttpPost("/cuenta/segundo-factor/regenerar")]
    public async Task<IActionResult> RegenerarLosCodigos(CancellationToken cancelacion)
    {
        var (actor, campos, fallo) = await Preparar(["actual", "codigo"], cancelacion);
        if (actor is null)
        {
            return fallo!;
        }

        var dispositivo = DatosDelBorde.Leer(Request);
        var resultado = await Llamar(() => cuenta.RegenerarLosCodigosDeRespaldo(actor.UsuarioId, new ValorSecreto(campos!.GetValueOrDefault("actual") ?? string.Empty), new ValorSecreto(campos!.GetValueOrDefault("codigo") ?? string.Empty), dispositivo.Marca is null ? null : new ValorSecreto(dispositivo.Marca), cancelacion), cancelacion);
        return resultado.EsExito ? await MostrarCodigos(actor, resultado.Valor, cancelacion) : await Volver(PantallaDeLaCuenta.SegundoFactor, resultado.Error!, cancelacion);
    }

    [HttpPost("/cuenta/avisos/{id:guid}/visto")]
    public Task<IActionResult> MarcarElAvisoComoVisto(Guid id, CancellationToken cancelacion) =>
        Accion([], PantallaDeLaCuenta.Avisos, "aviso-visto", async (actor, _, token) =>
        {
            // Que un aviso se mostró lo informa solo esta acción explícita de la persona (nunca una consulta automática ni un prefetch). La web no afirma que la sesión es reconocida (la cookie del dispositivo la controla quien tiene la sesión): lo decide Identidad con la marca y la región del borde (IDE-55, IDE-76).
            var borde = DatosDelBorde.Leer(Request);
            var reconocida = await dispositivos.Reconoce(actor.UsuarioId, borde.Marca is null ? null : new ValorSecreto(borde.Marca), borde.Region, token);
            if (!reconocida.EsExito)
            {
                return reconocida.Error;
            }

            var resultado = await cuenta.MarcarElAvisoComoVisto(actor.UsuarioId, id, desdeUnaSesionReconocida: reconocida.Valor, token);
            return resultado.EsExito ? null : resultado.Error;
        }, cancelacion);

    [HttpPost("/cuenta/correo/solicitar")]
    public Task<IActionResult> SolicitarElCorreo(CancellationToken cancelacion) =>
        Accion(["correoNuevo", "actual"], PantallaDeLaCuenta.Correo, "correo-solicitado", async (actor, campos, token) =>
        {
            var dispositivo = DatosDelBorde.Leer(Request);
            var resultado = await correo.Solicitar(actor.UsuarioId, campos.GetValueOrDefault("correoNuevo") ?? string.Empty, new ValorSecreto(campos.GetValueOrDefault("actual") ?? string.Empty), dispositivo.Marca is null ? null : new ValorSecreto(dispositivo.Marca), dispositivo.Region, token);
            return resultado.EsExito ? null : resultado.Error;
        }, cancelacion);

    [HttpPost("/cuenta/correo/verificar")]
    public Task<IActionResult> VerificarElCorreoNuevo(CancellationToken cancelacion) =>
        Accion(["codigo"], PantallaDeLaCuenta.Correo, "correo-verificado", async (actor, campos, token) =>
        {
            var resultado = await correo.VerificarElCorreoNuevo(actor.UsuarioId, new ValorSecreto(campos.GetValueOrDefault("codigo") ?? string.Empty), token);
            return resultado.EsExito ? null : resultado.Error;
        }, cancelacion);

    [HttpPost("/cuenta/correo/confirmar")]
    public Task<IActionResult> ConfirmarElCambioPendiente(CancellationToken cancelacion) =>
        Accion(["actual"], PantallaDeLaCuenta.Correo, "correo-confirmado", async (actor, campos, token) =>
        {
            var dispositivo = DatosDelBorde.Leer(Request);
            var resultado = await correo.ConfirmarElCambioPendiente(actor.UsuarioId, new ValorSecreto(campos.GetValueOrDefault("actual") ?? string.Empty), dispositivo.Marca is null ? null : new ValorSecreto(dispositivo.Marca), dispositivo.Region, token);
            return resultado.EsExito ? null : resultado.Error;
        }, cancelacion);

    [HttpPost("/cuenta/correo/rechazar")]
    public Task<IActionResult> RechazarElCambioPendiente(CancellationToken cancelacion) =>
        Accion(["actual"], PantallaDeLaCuenta.Correo, "correo-rechazado", async (actor, campos, token) =>
        {
            var dispositivo = DatosDelBorde.Leer(Request);
            var resultado = await correo.RechazarElCambioPendiente(actor.UsuarioId, new ValorSecreto(campos.GetValueOrDefault("actual") ?? string.Empty), dispositivo.Marca is null ? null : new ValorSecreto(dispositivo.Marca), dispositivo.Region, token);
            return resultado.EsExito ? null : resultado.Error;
        }, cancelacion);

    [HttpPost("/cuenta/cancelacion/solicitar")]
    public Task<IActionResult> SolicitarLaCancelacion(CancellationToken cancelacionDeLaSolicitud) => AccionDeCancelacion("cancelacion-solicitada", (actor, contrasena, codigo, token) => cancelacion.Solicitar(actor.UsuarioId, contrasena, codigo, token), cancelacionDeLaSolicitud);

    [HttpPost("/cuenta/cancelacion/reconfirmar")]
    public Task<IActionResult> ReconfirmarLaCancelacion(CancellationToken cancelacionDeLaSolicitud) => AccionDeCancelacion("cancelacion-reconfirmada", (actor, contrasena, codigo, token) => cancelacion.Reconfirmar(actor.UsuarioId, contrasena, codigo, token), cancelacionDeLaSolicitud);

    [HttpPost("/cuenta/cancelacion/desistir")]
    public Task<IActionResult> DesistirDeLaCancelacion(CancellationToken cancelacionDeLaSolicitud) =>
        Accion([], PantallaDeLaCuenta.Cancelacion, "cancelacion-desistida", async (actor, _, token) =>
        {
            var resultado = await cancelacion.Desistir(actor.UsuarioId, token);
            return resultado.EsExito ? null : resultado.Error;
        }, cancelacionDeLaSolicitud);

    [HttpPost("/cuenta/solicitudes")]
    public Task<IActionResult> PresentarUnaSolicitud(CancellationToken cancelacion) =>
        Accion(["tipo"], PantallaDeLaCuenta.Solicitudes, "solicitud-presentada", async (actor, campos, token) =>
        {
            if (Array.FindIndex(Enum.GetNames<TipoDeSolicitudDeDatos>(), n => string.Equals(n, campos.GetValueOrDefault("tipo"), StringComparison.OrdinalIgnoreCase)) < 0 || !Enum.TryParse<TipoDeSolicitudDeDatos>(campos.GetValueOrDefault("tipo"), ignoreCase: true, out var tipo))
            {
                return new ErrorDeAplicacion(CategoriaDeError.Validacion, "tipo-invalido");
            }

            var resultado = await solicitudes.Presentar(actor.UsuarioId, tipo, token);
            return resultado.EsExito ? null : resultado.Error;
        }, cancelacion);

    // ---------- Piezas ----------

    private async Task<IActionResult> AccionDeCancelacion(string aviso, Func<ActorVerificado, ValorSecreto, ValorSecreto?, CancellationToken, Task<Resultado<EstadoDeLaCancelacionDeCuenta>>> operacion, CancellationToken cancelacionDeLaSolicitud)
    {
        var (actor, campos, fallo) = await Preparar(["contrasena", "codigo"], cancelacionDeLaSolicitud);
        if (actor is null)
        {
            return fallo!;
        }

        var codigo = campos!.GetValueOrDefault("codigo");
        var resultado = await Llamar(() => operacion(actor, new ValorSecreto(campos!.GetValueOrDefault("contrasena") ?? string.Empty), string.IsNullOrWhiteSpace(codigo) ? null : new ValorSecreto(codigo), cancelacionDeLaSolicitud), cancelacionDeLaSolicitud);
        if (!resultado.EsExito)
        {
            return await Volver(PantallaDeLaCuenta.Cancelacion, resultado.Error!, cancelacionDeLaSolicitud);
        }

        if (resultado.Valor.Estado == EstadoDeLaCancelacion.Ejecutada)
        {
            // La cancelación se ejecutó: se cierran todas las sesiones de la cuenta (la de este navegador incluida) y el navegador queda limpio.
            try
            {
                await sesiones.CerrarTodasDeUnUsuario(actor.UsuarioId, cancelacionDeLaSolicitud);
            }
            catch (Exception) when (!cancelacionDeLaSolicitud.IsCancellationRequested)
            {
                // La cuenta ya se canceló: igual se borra la cookie y se limpia el navegador; las sesiones vencen solas al renovarse.
            }

            CookieDeSesionWeb.Borrar(Response, host.Host);
            Response.LimpiarElNavegador();
            return Request.EsHtmx() ? new HxRedirectResult(RespuestasDeSesion.RutaDelIngreso) : new LocalRedirectResult(RespuestasDeSesion.RutaDelIngreso);
        }

        return Redirigir($"/cuenta/cancelacion?aviso={aviso}");
    }

    /// <summary>Una acción de la cuenta: sesión de cliente, esquema estricto del cuerpo, la llamada a Identidad y, si sale bien, <c>303</c> a la pantalla con un aviso de lista cerrada; si no, la misma pantalla con el motivo.</summary>
    private async Task<IActionResult> Accion(string[] permitidos, PantallaDeLaCuenta pantalla, string aviso, Func<ActorVerificado, IReadOnlyDictionary<string, string>, CancellationToken, Task<ErrorDeAplicacion?>> operacion, CancellationToken cancelacionDeLaSolicitud)
    {
        var (actor, campos, fallo) = await Preparar(permitidos, cancelacionDeLaSolicitud);
        if (actor is null)
        {
            return fallo!;
        }

        ErrorDeAplicacion? error;
        try
        {
            error = await operacion(actor, campos!, cancelacionDeLaSolicitud);
        }
        catch (Exception) when (!cancelacionDeLaSolicitud.IsCancellationRequested)
        {
            error = new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible");
        }

        return error is null ? Redirigir($"{RutaDe(pantalla)}?aviso={aviso}") : await Volver(pantalla, error, cancelacionDeLaSolicitud);
    }

    /// <summary>La sesión de un cliente y el cuerpo con su esquema estricto; sin sesión, al ingreso; con otro rol, sin permiso.</summary>
    private async Task<(ActorVerificado? Actor, IReadOnlyDictionary<string, string>? Campos, IActionResult? Fallo)> Preparar(string[] permitidos, CancellationToken cancelacionDeLaSolicitud)
    {
        if (HttpContext.ActorDeLaSolicitud() is not { } actor)
        {
            return (null, null, HttpContext.RedirigirAlIngreso());
        }

        if (actor.Rol != RolWeb.Cliente)
        {
            return (null, null, Redirigir(RespuestasDeSesion.RutaSinPermiso));
        }

        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, permitidos, cancelacionDeLaSolicitud);
        if (campos is null)
        {
            return (null, null, await Volver(PantallaDeLaCuenta.Resumen, error!, cancelacionDeLaSolicitud));
        }

        return (actor, campos, null);
    }
/// <summary>La renovación de la sesión de este navegador, solo en proceso, para que Identidad la conserve al revocar las demás; sin sesión legible es `null` (se revocan todas, que es lo seguro).</summary>    private async Task<ValorSecreto?> RenovacionActual(CancellationToken cancelacionDeLaSolicitud) =>        CookieDeSesionWeb.Leer(Request, host.Host) is { } cookie && await sesiones.RenovacionDeLaSesion(cookie, host.Host, cancelacionDeLaSolicitud) is { } renovacion ? new ValorSecreto(renovacion.Revelar()) : null;
    /// <summary>La renovación de la sesión de este navegador, solo en proceso, para que Identidad la conserve al revocar las demás; sin una sesión legible es <see langword="null"/> (se revocan todas, que es lo seguro).</summary>
    private async Task<ValorSecreto?> RenovacionActual(CancellationToken cancelacionDeLaSolicitud) =>
        CookieDeSesionWeb.Leer(Request, host.Host) is { } cookie && await sesiones.RenovacionDeLaSesion(cookie, host.Host, cancelacionDeLaSolicitud) is { } renovacion ? new ValorSecreto(renovacion.Revelar()) : null;

    private static string RutaDe(PantallaDeLaCuenta pantalla) => pantalla switch
    {
        PantallaDeLaCuenta.Datos => Datos,
        PantallaDeLaCuenta.SegundoFactor => "/cuenta/segundo-factor",
        PantallaDeLaCuenta.Correo => "/cuenta/correo",
        PantallaDeLaCuenta.Cancelacion => "/cuenta/cancelacion",
        PantallaDeLaCuenta.Solicitudes => "/cuenta/solicitudes",
        PantallaDeLaCuenta.Avisos => "/cuenta/avisos",
        _ => "/cuenta",
    };

    private async Task<IActionResult> Mostrar(PantallaDeLaCuenta pantalla, string? aviso, CancellationToken cancelacionDeLaSolicitud)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext, SoloAviso) is { } consulta)
        {
            return await Volver(pantalla, consulta, cancelacionDeLaSolicitud);
        }

        if (HttpContext.ActorDeLaSolicitud() is not { } actor)
        {
            return HttpContext.RedirigirAlIngreso();
        }

        var vista = await Armar(pantalla, actor, aviso, cancelacionDeLaSolicitud);
        return Pintar(vista, vista.EsError ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK);
    }

    private async Task<CuentaVista> Armar(PantallaDeLaCuenta pantalla, ActorVerificado actor, string? aviso, CancellationToken cancelacionDeLaSolicitud)
    {
        var vista = await pagina.Completar(new CuentaVista { Pantalla = pantalla }, "Mi cuenta", cancelacion: cancelacionDeLaSolicitud);
        if (aviso is not null && Avisos.TryGetValue(aviso, out var texto))
        {
            vista.Mensaje = texto;
        }

        if (actor.Rol != RolWeb.Cliente)
        {
            return vista;
        }

        try
        {
            var datos = await cuenta.Consultar(actor.UsuarioId, cancelacionDeLaSolicitud);
            if (!datos.EsExito)
            {
                return Caida(vista);
            }

            vista.Cuenta = datos.Valor;
            switch (pantalla)
            {
                case PantallaDeLaCuenta.Avisos or PantallaDeLaCuenta.Resumen:
                    var avisos = await cuenta.ListarLosAvisos(actor.UsuarioId, cancelacionDeLaSolicitud);
                    vista.Avisos = avisos.EsExito ? avisos.Valor : [];
                    if (pantalla == PantallaDeLaCuenta.Resumen)
                    {
                        var estado = await cancelacion.Consultar(actor.UsuarioId, cancelacionDeLaSolicitud);
                        vista.Cancelacion = estado.EsExito ? estado.Valor : null;
                    }

                    break;
                case PantallaDeLaCuenta.Cancelacion:
                    var detalle = await cancelacion.Consultar(actor.UsuarioId, cancelacionDeLaSolicitud);
                    if (!detalle.EsExito)
                    {
                        return Caida(vista);
                    }

                    vista.Cancelacion = detalle.Valor;
                    break;
                case PantallaDeLaCuenta.Solicitudes:
                    var lista = await solicitudes.ListarLasDelCliente(actor.UsuarioId, cancelacionDeLaSolicitud);
                    if (!lista.EsExito)
                    {
                        return Caida(vista);
                    }

                    vista.Solicitudes = lista.Valor;
                    break;
                case PantallaDeLaCuenta.Datos:
                    // Lo que la persona tenía escrito cuando venció su sesión (WEB-25), una sola vez y solo para ella.
                    vista.Borrador = await borradores.Recuperar(actor.UsuarioId, Datos, cancelacionDeLaSolicitud) ?? vista.Borrador;
                    break;
            }
        }
        catch (Exception) when (!cancelacionDeLaSolicitud.IsCancellationRequested)
        {
            return Caida(vista);
        }

        return vista;
    }

    private static CuentaVista Caida(CuentaVista vista)
    {
        vista.Mensaje = "No podemos mostrar tu cuenta ahora. Inténtalo en unos minutos.";
        vista.EsError = true;
        return vista;
    }

    private async Task<IActionResult> MostrarCodigos(ActorVerificado actor, IReadOnlyList<ValorSecreto> codigos, CancellationToken cancelacionDeLaSolicitud)
    {
        var vista = await Armar(PantallaDeLaCuenta.CodigosDeRespaldo, actor, null, cancelacionDeLaSolicitud);
        vista.Codigos = [.. codigos.Select(c => c.Revelar())];
        return Pintar(vista, StatusCodes.Status200OK);
    }

    private async Task<Resultado<T>> Llamar<T>(Func<Task<Resultado<T>>> llamada, CancellationToken cancelacionDeLaSolicitud)
    {
        try
        {
            return await llamada();
        }
        catch (Exception) when (!cancelacionDeLaSolicitud.IsCancellationRequested)
        {
            return Resultado<T>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"));
        }
    }

    private async Task<IActionResult> Volver(PantallaDeLaCuenta pantalla, ErrorDeAplicacion error, CancellationToken cancelacionDeLaSolicitud)
    {
        var actor = HttpContext.ActorDeLaSolicitud();
        var vista = actor is null ? await pagina.Completar(new CuentaVista { Pantalla = pantalla }, "Mi cuenta", cancelacion: cancelacionDeLaSolicitud) : await Armar(pantalla, actor, null, cancelacionDeLaSolicitud);
        vista.EsError = true;
        vista.Mensaje = TextoDelError(error);
        return Pintar(vista, CodigoHttp(error));
    }

    private static string TextoDelError(ErrorDeAplicacion error) => error.Categoria switch
    {
        CategoriaDeError.Limite => "Hiciste demasiados intentos. Espera unos minutos y vuelve a probar.",
        CategoriaDeError.NoDisponible => "No podemos atenderte ahora. Inténtalo en unos minutos.",
        CategoriaDeError.Prohibido => "No puedes hacer esto desde este dispositivo o en este momento.",
        CategoriaDeError.Conflicto => "No pudimos completar la acción porque algo cambió. Revisa la pantalla e inténtalo de nuevo.",
        _ => (error.Errores.FirstOrDefault()?.Clave ?? error.Clave) switch
        {
            "contrasena-actual-incorrecta" => "La contraseña actual no es correcta.",
            "confirmacion-distinta" => "La contraseña nueva y su repetición no coinciden.",
            "codigo-invalido" => "El código no es válido o venció.",
            "demasiado-corta" => "La contraseña es demasiado corta: usa al menos 12 caracteres.",
            "demasiado-comun" => "Esa contraseña es muy común. Elige otra.",
            _ => "Revisa los datos: alguno falta o no es válido.",
        },
    };

    private static int CodigoHttp(ErrorDeAplicacion error) => error.Categoria switch
    {
        CategoriaDeError.Limite => StatusCodes.Status429TooManyRequests,
        CategoriaDeError.NoDisponible => StatusCodes.Status503ServiceUnavailable,
        CategoriaDeError.Prohibido => StatusCodes.Status403Forbidden,
        CategoriaDeError.Conflicto => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    private IActionResult Pintar(CuentaVista vista, int estado)
    {
        Response.Headers.CacheControl = "no-store";
        Response.StatusCode = Request.EsHtmx() ? StatusCodes.Status200OK : estado;
        return View("Cuenta", vista);
    }

    private IActionResult Redirigir(string destino)
    {
        Response.Headers.CacheControl = "no-store";
        if (Request.EsHtmx())
        {
            return new HxRedirectResult(destino);
        }

        Response.StatusCode = StatusCodes.Status303SeeOther;
        Response.Headers.Location = destino;
        return new EmptyResult();
    }
}

/// <summary>El formulario de los datos de la cuenta, para que la sesión que vence a mitad de la acción conserve lo escrito (WEB-25): solo el nombre es conservable; nunca contraseñas ni códigos. La acción lee el cuerpo con su esquema estricto; este tipo solo declara qué campo se conserva.</summary>
public sealed class FormularioDeDatosDeLaCuenta
{
    [Conservar]
    public string? Nombre { get; set; }
}
