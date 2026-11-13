using System.Text.RegularExpressions;

namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>Reloj del sistema como puerto, para poder probar vencimientos y ventanas.</summary>
public interface IReloj
{
    DateTimeOffset Ahora { get; }

    /// <summary>El día de calendario de la hora de Lima (UTC-5): topes diarios, vencimientos «al final del día».</summary>
    DateOnly HoyEnLima { get; }
}

public enum TipoDeActor
{
    Usuario,
    Sistema,
    Integracion,
}

/// <summary>Quién ejecuta el caso de uso: una persona con rol (y sucursal, si la tiene), el sistema o una integración.</summary>
public sealed record ActorActual
{
    private ActorActual(TipoDeActor tipo, Guid? usuarioId, string? rol, Guid? sucursalId, string? integracion)
    {
        Tipo = tipo;
        UsuarioId = usuarioId;
        Rol = rol;
        SucursalId = sucursalId;
        Integracion = integracion;
    }

    public TipoDeActor Tipo { get; }

    public Guid? UsuarioId { get; }

    public string? Rol { get; }

    public Guid? SucursalId { get; }

    /// <summary>Nombre de la integración cuando el actor lo es (por ejemplo, el proveedor de pagos).</summary>
    public string? Integracion { get; }

    public static ActorActual DeUsuario(Guid usuarioId, string rol, Guid? sucursalId = null)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del usuario no puede ser vacío.", nameof(usuarioId));
        }

        if (string.IsNullOrWhiteSpace(rol))
        {
            throw new ArgumentException("El usuario necesita un rol.", nameof(rol));
        }

        return new ActorActual(TipoDeActor.Usuario, usuarioId, rol, sucursalId, null);
    }

    public static ActorActual DelSistema() => new(TipoDeActor.Sistema, null, null, null, null);

    public static ActorActual DeIntegracion(string nombre) =>
        string.IsNullOrWhiteSpace(nombre)
            ? throw new ArgumentException("La integración necesita un nombre.", nameof(nombre))
            : new ActorActual(TipoDeActor.Integracion, null, null, null, nombre);
}

/// <summary>Actor de la solicitud en curso; <see langword="null"/> si no hay sesión (visitante).</summary>
public interface IActorActual
{
    ActorActual? Actual { get; }
}

/// <summary>
/// Esquema de base de datos de un módulo. Lo construye solo la Infraestructura de cada módulo (constructor interno), y con
/// él <see cref="IUnidadDeTrabajo.EjecutarComo"/> corre un bloque bajo el rol de ese esquema (AD18).
/// </summary>
public sealed partial record EsquemaDeModulo
{
    private EsquemaDeModulo(string nombre) => Nombre = nombre;

    public string Nombre { get; }

    internal static EsquemaDeModulo Crear(string nombre)
    {
        if (string.IsNullOrEmpty(nombre) || !Forma().IsMatch(nombre))
        {
            throw new ArgumentException("El esquema es un identificador en minúsculas, con guiones bajos.", nameof(nombre));
        }

        return new EsquemaDeModulo(nombre);
    }

    public override string ToString() => Nombre;

    [GeneratedRegex("^[a-z][a-z0-9_]{1,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex Forma();
}

/// <summary>Unidad de trabajo: una transacción por caso de uso, y el acceso a esquemas ajenos solo con el rol del esquema.</summary>
public interface IUnidadDeTrabajo
{
    /// <summary>Ejecuta el trabajo en una transacción: confirma si termina, revierte si falla (SEG-04).</summary>
    Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default);

    /// <summary>
    /// Dentro de la transacción activa, corre el bloque con <c>SET LOCAL ROLE</c> del esquema del módulo y restaura el rol
    /// previo al terminar. Falla si no hay una transacción activa.
    /// </summary>
    Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default);
}

/// <summary>Credencial de servicio entre el E-commerce y el Inventario (rechazo por defecto; matriz alcance → operación en T08).</summary>
public enum AlcanceDeServicio
{
    OperacionDeUsuario,
    LecturaDeDisponibilidad,
    ExpiracionDeReservas,
    ConsultaDePendientes,
    BajaDeTitular,
    LecturaDeAlertas,
    ConsultaDeAuditoria,
    MarcarAlerta,
    Historial,
    SolicitarAviso,
    LecturaDeContacto,
    OrigenReenviado,
}
