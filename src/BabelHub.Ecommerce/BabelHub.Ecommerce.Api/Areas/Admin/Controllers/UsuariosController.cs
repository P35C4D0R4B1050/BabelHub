using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Admin.Controllers;

/// <summary>Lo que pinta la administración de usuarios: el resultado de la última acción (un usuario y, solo al crear o restablecer, la contraseña temporal que se muestra <b>una vez</b>) o el motivo de su rechazo. Todo texto sale como dato, nunca como HTML.</summary>
public sealed class UsuariosVista : ModeloBaseDePagina
{
    public string? Mensaje { get; set; }

    public bool EsError { get; set; }

    public UsuarioAdministrado? Usuario { get; set; }

    public string? ContrasenaTemporal { get; set; }

    public DateTimeOffset? ContrasenaVenceEn { get; set; }
}

/// <summary>
/// Los usuarios y roles (T30; WEB-35, AC-WEB-29): crear un usuario del personal con su contraseña temporal, cambiar su rol y sucursal, desactivarlo o reactivarlo y restablecer su contraseña o su segundo factor, <b>solo para la administración central</b> (la decide Identidad, <see cref="IAdministracionDeUsuarios"/>; otro rol recibe «sin permiso»).
/// <b>Presentación pura</b>: cada acción es un <c>POST</c> con esquema estricto; la contraseña temporal se muestra una sola vez en la respuesta (<c>no-store</c>) y no se guarda ni se registra. Identidad aún no ofrece una lista ni una búsqueda de usuarios: las acciones sobre un usuario existente piden su identificador (hallazgo registrado).
/// </summary>
[Area("Admin")]
public sealed class UsuariosController(IAdministracionDeUsuarios usuarios, ContextoDePagina pagina) : Controller
{
    public const string Ruta = "/administracion/usuarios";

    [HttpGet(Ruta)]
    public async Task<IActionResult> Index(CancellationToken cancelacion)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext) is not null)
        {
            return await Pintar(new UsuariosVista { Mensaje = "La consulta no es válida.", EsError = true }, StatusCodes.Status400BadRequest, cancelacion);
        }

        return await Pintar(new UsuariosVista(), StatusCodes.Status200OK, cancelacion);
    }

    [HttpPost(Ruta + "/crear")]
    public Task<IActionResult> Crear(CancellationToken cancelacion) =>
        Operar(["nombre", "correo", "rol", "sucursal"], async (admin, campos, token) =>
        {
            if (!TryRol(campos.GetValueOrDefault("rol"), out var rol) || !TrySucursal(campos.GetValueOrDefault("sucursal"), out var sucursal) || campos.GetValueOrDefault("nombre") is not { Length: > 0 and <= 120 } nombre || campos.GetValueOrDefault("correo") is not { Length: > 0 and <= 254 } correo)
            {
                return Invalido();
            }

            var creado = await usuarios.Crear(admin, nombre, correo, rol, sucursal, token);
            return creado.EsExito ? Entregada(creado.Valor, "Creamos el usuario. Entrégale la contraseña temporal: solo se muestra ahora.") : creado.Error;
        }, cancelacion);

    [HttpPost(Ruta + "/rol")]
    public Task<IActionResult> CambiarElRol(CancellationToken cancelacion) =>
        Operar(["usuarioId", "rol", "sucursal"], async (admin, campos, token) =>
        {
            if (!EntradasHttpDelCarrito.TryGuid(campos, "usuarioId", out var id) || !TryRol(campos.GetValueOrDefault("rol"), out var rol) || !TrySucursal(campos.GetValueOrDefault("sucursal"), out var sucursal))
            {
                return Invalido();
            }

            var cambiado = await usuarios.CambiarElRol(admin, id, rol, sucursal, token);
            return cambiado.EsExito ? Listo(cambiado.Valor, "Cambiamos el rol. Sus sesiones se cierran al renovarse.") : cambiado.Error;
        }, cancelacion);

    [HttpPost(Ruta + "/desactivar")]
    public Task<IActionResult> Desactivar(CancellationToken cancelacion) => SobreUnUsuario((admin, id, token) => usuarios.Desactivar(admin, id, token), "Desactivamos el usuario y cerramos sus sesiones.", cancelacion);

    [HttpPost(Ruta + "/reactivar")]
    public Task<IActionResult> Reactivar(CancellationToken cancelacion) => SobreUnUsuario((admin, id, token) => usuarios.Reactivar(admin, id, token), "Reactivamos el usuario.", cancelacion);

    [HttpPost(Ruta + "/contrasena")]
    public Task<IActionResult> RestablecerLaContrasena(CancellationToken cancelacion) =>
        Operar(["usuarioId"], async (admin, campos, token) =>
        {
            if (!EntradasHttpDelCarrito.TryGuid(campos, "usuarioId", out var id))
            {
                return Invalido();
            }

            var entregada = await usuarios.RestablecerLaContrasena(admin, id, token);
            return entregada.EsExito ? Entregada(entregada.Valor, "Restablecimos la contraseña. Entrégale la temporal: solo se muestra ahora.") : entregada.Error;
        }, cancelacion);

    [HttpPost(Ruta + "/segundo-factor")]
    public Task<IActionResult> RestablecerElSegundoFactor(CancellationToken cancelacion) =>
        Operar(["usuarioId"], async (admin, campos, token) =>
        {
            if (!EntradasHttpDelCarrito.TryGuid(campos, "usuarioId", out var id))
            {
                return Invalido();
            }

            var resultado = await usuarios.RestablecerElSegundoFactor(admin, id, token);
            return resultado.EsExito ? new UsuariosVista { Mensaje = "Restablecimos su segundo factor: deberá configurarlo al ingresar." } : resultado.Error;
        }, cancelacion);

    // ---------- Piezas ----------

    private Task<IActionResult> SobreUnUsuario(Func<Guid, Guid, CancellationToken, Task<Resultado<UsuarioAdministrado>>> operacion, string mensaje, CancellationToken cancelacion) =>
        Operar(["usuarioId"], async (admin, campos, token) =>
        {
            if (!EntradasHttpDelCarrito.TryGuid(campos, "usuarioId", out var id))
            {
                return Invalido();
            }

            var resultado = await operacion(admin, id, token);
            return resultado.EsExito ? Listo(resultado.Valor, mensaje) : resultado.Error;
        }, cancelacion);

    private static OneOf<UsuariosVista, ErrorDeAplicacion?> Invalido() => new ErrorDeAplicacion(CategoriaDeError.Validacion, "validacion-fallida");

    private static OneOf<UsuariosVista, ErrorDeAplicacion?> Listo(UsuarioAdministrado usuario, string mensaje) => new UsuariosVista { Usuario = usuario, Mensaje = mensaje };

    private static OneOf<UsuariosVista, ErrorDeAplicacion?> Entregada(ContrasenaTemporalEntregada entregada, string mensaje) =>
        new UsuariosVista { Usuario = entregada.Usuario, Mensaje = mensaje, ContrasenaTemporal = entregada.ContrasenaTemporal.Revelar(), ContrasenaVenceEn = entregada.VenceEn };

    private async Task<IActionResult> Operar(string[] permitidos, Func<Guid, IReadOnlyDictionary<string, string>, CancellationToken, Task<OneOf<UsuariosVista, ErrorDeAplicacion?>>> operacion, CancellationToken cancelacion)
    {
        if (HttpContext.ActorDeLaSolicitud() is not { } actor)
        {
            return HttpContext.RedirigirAlIngreso();
        }

        var (campos, error) = await EntradasHttpDeAdministracion.Leer(HttpContext, permitidos, cancelacion);
        if (campos is null)
        {
            _ = error;
            return await Pintar(new UsuariosVista { Mensaje = "No pudimos leer lo que enviaste. Revisa los datos.", EsError = true }, StatusCodes.Status400BadRequest, cancelacion);
        }

        OneOf<UsuariosVista, ErrorDeAplicacion?> resultado;
        try
        {
            resultado = await operacion(actor.UsuarioId, campos, cancelacion);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            resultado = new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible");
        }

        if (resultado.Vista is { } vista)
        {
            return await Pintar(vista, StatusCodes.Status200OK, cancelacion);
        }

        var falla = resultado.Error!;
        var (texto, estado) = falla.Categoria switch
        {
            CategoriaDeError.Prohibido or CategoriaDeError.NoAutenticado => ("No tienes permiso para administrar usuarios.", StatusCodes.Status403Forbidden),
            CategoriaDeError.NoEncontrado => ("No encontramos ese usuario.", StatusCodes.Status404NotFound),
            CategoriaDeError.Conflicto => ("No pudimos hacerlo porque algo cambió o el correo ya está en uso. Revisa e inténtalo de nuevo.", StatusCodes.Status409Conflict),
            CategoriaDeError.Limite => ("Hiciste demasiados intentos. Espera unos minutos.", StatusCodes.Status429TooManyRequests),
            CategoriaDeError.NoDisponible => ("No podemos atenderte ahora. Inténtalo en unos minutos.", StatusCodes.Status503ServiceUnavailable),
            _ => ("Revisa los datos: alguno falta o no es válido.", StatusCodes.Status400BadRequest),
        };
        return await Pintar(new UsuariosVista { Mensaje = texto, EsError = true }, estado, cancelacion);
    }

    private async Task<IActionResult> Pintar(UsuariosVista vista, int estado, CancellationToken cancelacion)
    {
        await pagina.Completar(vista, "Usuarios y roles", AreaDeLaPagina.Admin, cancelacion);
        Response.Headers.CacheControl = "no-store";
        Response.StatusCode = Request.EsHtmx() ? StatusCodes.Status200OK : estado;
        return View("Index", vista);
    }

    private static bool TryRol(string? texto, out RolDeCuenta rol)
    {
        rol = default;
        return texto is not null && Array.IndexOf(Enum.GetNames<RolDeCuenta>(), texto) >= 0 && Enum.TryParse(texto, ignoreCase: false, out rol);
    }

    /// <summary>La sucursal es opcional (el personal de logística y la administración central no la llevan); si viene, es un identificador.</summary>
    private static bool TrySucursal(string? texto, out Guid? sucursal)
    {
        sucursal = null;
        if (string.IsNullOrEmpty(texto))
        {
            return true;
        }

        if (!Guid.TryParseExact(texto, "D", out var id) || id == Guid.Empty)
        {
            return false;
        }

        sucursal = id;
        return true;
    }

    /// <summary>Lo que devuelve una operación: la pantalla con su resultado o el error de Identidad.</summary>
    public readonly record struct OneOf<TVista, TError>(TVista? Vista, TError? Error)
        where TVista : class
    {
        public static implicit operator OneOf<TVista, TError>(TVista vista) => new(vista, default);

        public static implicit operator OneOf<TVista, TError>(TError error) => new(null, error);
    }
}
