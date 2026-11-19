using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Reclamaciones;

namespace BabelHub.Ecommerce.Application.Reclamaciones.Interno;

internal static class RolesDeReclamaciones
{
    public const string AdministradorCentral = "administrador-central";

    public const string Cliente = "cliente";

    public static bool EsAdministradorCentral(ActorActual? actor) =>
        actor is { Tipo: TipoDeActor.Usuario, UsuarioId: not null } && string.Equals(actor.Rol, AdministradorCentral, StringComparison.Ordinal);
}

internal static class MotivosDeDenegacionDeReclamaciones
{
    public const string FueraDeRol = "fuera-de-rol";

    public const string SinSesion = "sin-sesion";

    public const string RecursoAjeno = "recurso-ajeno";

    public const string CorreoNoVerificado = "correo-no-verificado";
}

/// <summary>Las claves simbólicas de los fallos del módulo; la Api las traduce a los códigos <c>ECO-…</c> y <c>ECO-REC-…</c> (plan §4).</summary>
internal static class ClavesDeReclamaciones
{
    public const string ValidacionFallida = "validacion-fallida";

    public const string VerificacionRequerida = "verificacion-requerida";

    /// <summary>El 429 uniforme: el campo <c>espera-en-segundos</c> lleva el tiempo sugerido para el <c>Retry-After</c>.</summary>
    public const string LimiteExcedido = "limite-excedido";

    public const string EsperaEnSegundos = "espera-en-segundos";

    /// <summary>La misma clave del formulario con otro contenido, o con el primer envío todavía en curso (409).</summary>
    public const string Conflicto = "conflicto";

    public const string ServicioNoDisponible = "servicio-no-disponible";

    public const string HojaNoEncontrada = "hoja-no-encontrada";

    /// <summary><c>ECO-REC-ESTADO-INVALIDO</c> (409): la hoja no está pendiente o ya se anonimizó (REC-16).</summary>
    public const string EstadoInvalido = "estado-invalido";

    public const string Prohibido = "prohibido";

    public const string NoAutenticado = "no-autenticado";

    /// <summary><c>ECO-REC-CORREO-NO-VERIFICADO</c> (403): la sesión no tiene un correo verificado.</summary>
    public const string CorreoNoVerificado = "correo-no-verificado";
}

internal static class FallosDeReclamaciones
{
    public static Resultado<T> De<T>(CategoriaDeError categoria, string clave, IReadOnlyList<ErrorDeCampo>? errores = null) =>
        Resultado<T>.Fallo(new ErrorDeAplicacion(categoria, clave, errores));

    public static Resultado<T> Fallo<T>(ErrorDeAplicacion error) => Resultado<T>.Fallo(error);

    public static string NombreDelCampo(CampoDeHoja campo) => campo switch
    {
        CampoDeHoja.Nombre => "nombre",
        CampoDeHoja.Documento => "documento",
        CampoDeHoja.Domicilio => "domicilio",
        CampoDeHoja.Correo => "correo",
        CampoDeHoja.BienContratado => "bienContratado",
        CampoDeHoja.MontoReclamado => "montoReclamado",
        CampoDeHoja.Tipo => "tipo",
        CampoDeHoja.Detalle => "detalle",
        CampoDeHoja.PedidoReferencia => "pedidoReferencia",
        _ => throw new ArgumentOutOfRangeException(nameof(campo)),
    };
}

/// <summary>La comprobación del rol, siempre con el mismo orden: sesión, rol, hoja, estado. Cada intento rechazado deja un acceso denegado (SEG-09).</summary>
internal sealed class AutorizadorDeReclamaciones(IActorActual actores, IDenegacionDeAcceso denegaciones)
{
    public ActorActual? Actor => actores.Actual is { Tipo: not TipoDeActor.Anonimo } actor ? actor : null;

    /// <summary>El identificador del administrador central actual; solo tiene valor tras <see cref="ExigirAdministracionCentral"/> sin rechazo.</summary>
    public Guid? UsuarioId => RolesDeReclamaciones.EsAdministradorCentral(Actor) ? Actor!.UsuarioId : null;

    public async Task<ErrorDeAplicacion?> ExigirAdministracionCentral(CancellationToken cancelacion)
    {
        var actor = Actor;
        if (actor is null)
        {
            await Denegar(MotivosDeDenegacionDeReclamaciones.SinSesion, "ECO-NO-AUTENTICADO", cancelacion).ConfigureAwait(false);
            return new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, ClavesDeReclamaciones.NoAutenticado);
        }

        if (RolesDeReclamaciones.EsAdministradorCentral(actor))
        {
            return null;
        }

        await Denegar(MotivosDeDenegacionDeReclamaciones.FueraDeRol, "ECO-PROHIBIDO", cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDeReclamaciones.Prohibido);
    }

    /// <summary>El identificador del consumidor actual; solo tiene valor tras <see cref="ExigirConsumidor"/> sin rechazo.</summary>
    public Guid? ConsumidorId => Actor is { Tipo: TipoDeActor.Usuario, UsuarioId: { } id, Rol: RolesDeReclamaciones.Cliente } ? id : null;

    /// <summary>
    /// Las rutas del consumidor (mis hojas), en el orden fijo sesión → rol: un visitante recibe «no autenticado» <b>sin tocar nada y sin generar evento</b> (no hay número que registrar: lo acota el límite general por
    /// origen); un rol que no es el de cliente (el encargado de sucursal y la logística nunca ven datos de hojas, y la administración usa sus propias rutas) recibe «prohibido» con un acceso denegado por cada intento.
    /// </summary>
    public async Task<ErrorDeAplicacion?> ExigirConsumidor(CancellationToken cancelacion)
    {
        var actor = Actor;
        if (actor is null)
        {
            return new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, ClavesDeReclamaciones.NoAutenticado);
        }

        if (ConsumidorId is not null)
        {
            return null;
        }

        await Denegar(MotivosDeDenegacionDeReclamaciones.FueraDeRol, "ECO-PROHIBIDO", cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDeReclamaciones.Prohibido);
    }

    /// <summary>Una hoja ajena, inexistente, de una cuenta cancelada o un número mal escrito: <b>la misma respuesta</b> (no hay oráculo), con un acceso denegado por cada intento de una sesión.</summary>
    public async Task<ErrorDeAplicacion> RechazarHojaAjena(CancellationToken cancelacion)
    {
        await Denegar(MotivosDeDenegacionDeReclamaciones.RecursoAjeno, "ECO-NO-ENCONTRADO", cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, ClavesDeReclamaciones.HojaNoEncontrada);
    }

    /// <summary>La lista de una sesión sin correo verificado: <c>ECO-REC-CORREO-NO-VERIFICADO</c> (403) sin datos y con un acceso denegado.</summary>
    public async Task<ErrorDeAplicacion> RechazarCorreoNoVerificado(CancellationToken cancelacion)
    {
        await Denegar(MotivosDeDenegacionDeReclamaciones.CorreoNoVerificado, "ECO-REC-CORREO-NO-VERIFICADO", cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDeReclamaciones.CorreoNoVerificado);
    }

    private Task Denegar(string motivo, string codigoDeError, CancellationToken cancelacion) =>
        denegaciones.Registrar(new DenegacionDeAcceso(motivo, ruta: null) { CodigoDeError = codigoDeError }, cancelacion);
}
