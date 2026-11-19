namespace BabelHub.Ecommerce.Domain.Identidad;

/// <summary>Los cuatro roles (IDE-04): el cliente y tres de personal.</summary>
public enum RolDeUsuario
{
    Cliente,
    EncargadoDeSucursal,
    Logistica,
    AdministradorCentral,
}

public static class RolesDeUsuario
{
    /// <summary>Todo rol que no es de cliente es de personal: exige correo verificado (IDE-87) y segundo factor (IDE-20).</summary>
    public static bool EsDePersonal(this RolDeUsuario rol) => rol != RolDeUsuario.Cliente;

    /// <summary>Solo el encargado tiene sucursal, y exactamente una (IDE-05).</summary>
    public static bool LlevaSucursal(this RolDeUsuario rol) => rol == RolDeUsuario.EncargadoDeSucursal;

    /// <summary>La vigencia de la renovación depende del rol (IDE-46): 7 días el cliente, 8 horas el personal.</summary>
    public static TimeSpan VigenciaDeLaRenovacion(this RolDeUsuario rol) => rol.EsDePersonal() ? TimeSpan.FromHours(8) : TimeSpan.FromDays(7);
}

/// <summary>Por qué se rechazó un cambio de usuario o de rol: son códigos de regla, nunca texto que dependa del dato.</summary>
public enum MotivoDeRechazoDeUsuario
{
    /// <summary>Un encargado sin sucursal, o un rol que no es de encargado con una sucursal (IDE-05).</summary>
    SucursalInvalida,

    /// <summary>La sucursal ya tiene un encargado activo (IDE-06); el resultado indica quién.</summary>
    SucursalYaTieneEncargado,

    /// <summary>Un administrador no puede restablecer, desactivar ni cambiar el rol de su propio usuario (IDE-49).</summary>
    SobreSiMismo,

    /// <summary>No se puede desactivar ni degradar al último administrador activo (IDE-49).</summary>
    UltimoAdministrador,

    /// <summary>No se asigna un rol de personal a una cuenta cuyo correo no está verificado (IDE-87).</summary>
    CorreoSinVerificar,

    /// <summary>Quien hace el cambio no es administrador central (IDE-07).</summary>
    NoEsAdministrador,

    /// <summary>La cuenta ya está desactivada o cancelada.</summary>
    CuentaNoActiva,
}

/// <summary>El resultado de una regla de administración: permitida, o rechazada con su motivo y, si es la sucursal ocupada, quién la tiene (IDE-06).</summary>
public sealed record DecisionDeAdministracion
{
    private DecisionDeAdministracion(MotivoDeRechazoDeUsuario? motivo, Guid? encargadoActual)
    {
        Motivo = motivo;
        EncargadoActual = encargadoActual;
    }

    public static DecisionDeAdministracion Permitida { get; } = new(null, null);

    public bool EsPermitida => Motivo is null;

    public MotivoDeRechazoDeUsuario? Motivo { get; }

    /// <summary>El usuario que ya es encargado de la sucursal, solo con <see cref="MotivoDeRechazoDeUsuario.SucursalYaTieneEncargado"/>.</summary>
    public Guid? EncargadoActual { get; }

    public static DecisionDeAdministracion Rechazada(MotivoDeRechazoDeUsuario motivo, Guid? encargadoActual = null) => new(motivo, encargadoActual);
}

/// <summary>
/// El usuario del módulo Identidad (IDE-04 a IDE-06, IDE-49, IDE-87): su rol, la sucursal del encargado, si está activo y si su correo está verificado. Las reglas que miran a otros usuarios (el último administrador, el
/// encargado de la sucursal) las evalúa <see cref="ReglasDeAdministracion"/> con lo que le entrega la Aplicación; el estado concurrente lo garantizan el índice único parcial y el bloqueo de la base (D17).
/// </summary>
public sealed class Usuario
{
    private Usuario(Guid id, string correo, RolDeUsuario rol, Guid? sucursal, bool correoVerificado, bool activo)
    {
        Id = id;
        Correo = correo;
        Rol = rol;
        Sucursal = sucursal;
        CorreoVerificado = correoVerificado;
        Activo = activo;
    }

    public Guid Id { get; }

    /// <summary>El correo normalizado (minúsculas, sin espacios en los extremos).</summary>
    public string Correo { get; private set; }

    public RolDeUsuario Rol { get; private set; }

    /// <summary>La sucursal del encargado; nula en los demás roles.</summary>
    public Guid? Sucursal { get; private set; }

    public bool CorreoVerificado { get; private set; }

    public bool Activo { get; private set; }

    /// <summary>Toda renovación emitida antes de este instante deja de servir (D17): cambiar el rol, la contraseña o el segundo factor, «cerrar todas» y declarar comprometida la clave lo avanzan.</summary>
    public DateTimeOffset? SesionesValidasDesde { get; private set; }

    /// <summary>Avanza el corte de sesiones (solo hacia adelante).</summary>
    public void AvanzarElCorteDeSesiones(DateTimeOffset ahora) => SesionesValidasDesde = CorteDeSesiones.Avanzar(SesionesValidasDesde, ahora);

    public static string NormalizarCorreo(string correo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correo);
        return correo.Trim().ToLowerInvariant();
    }

    /// <summary>Una cuenta nueva: siempre de cliente, activa y con el correo sin verificar (IDE-01).</summary>
    public static Usuario NuevoCliente(Guid id, string correo)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("El usuario necesita su identificador.", nameof(id));
        }

        return new Usuario(id, NormalizarCorreo(correo), RolDeUsuario.Cliente, null, correoVerificado: false, activo: true);
    }

    /// <summary>Reconstruye un usuario guardado; la combinación de rol y sucursal debe ser válida (IDE-05).</summary>
    public static Usuario Restaurar(Guid id, string correo, RolDeUsuario rol, Guid? sucursal, bool correoVerificado, bool activo)
    {
        if (!SucursalCoincideConElRol(rol, sucursal))
        {
            throw new ArgumentException("Un encargado tiene exactamente una sucursal y los demás roles ninguna.", nameof(sucursal));
        }

        return new Usuario(id, NormalizarCorreo(correo), rol, sucursal, correoVerificado, activo);
    }

    public static bool SucursalCoincideConElRol(RolDeUsuario rol, Guid? sucursal) =>
        rol.LlevaSucursal() ? sucursal is { } s && s != Guid.Empty : sucursal is null;

    public void VerificarCorreo() => CorreoVerificado = true;

    /// <summary>Aplica un cambio de correo ya verificado por su código (IDE-53): el correo nuevo queda normalizado y verificado.</summary>
    public void CambiarElCorreo(string correoNuevo)
    {
        Correo = NormalizarCorreo(correoNuevo);
        CorreoVerificado = true;
    }

    public void Desactivar() => Activo = false;

    public void Reactivar() => Activo = true;

    /// <summary>
    /// Cambia el rol si las reglas de este usuario lo permiten: el encargado lleva exactamente una sucursal y los demás ninguna (IDE-05) y un rol de personal exige el correo verificado (IDE-87). Las reglas que dependen de
    /// otros usuarios se evalúan antes con <see cref="ReglasDeAdministracion"/>.
    /// </summary>
    public DecisionDeAdministracion CambiarRol(RolDeUsuario nuevo, Guid? sucursal, DateTimeOffset ahora)
    {
        if (!Activo)
        {
            return DecisionDeAdministracion.Rechazada(MotivoDeRechazoDeUsuario.CuentaNoActiva);
        }

        if (!SucursalCoincideConElRol(nuevo, sucursal))
        {
            return DecisionDeAdministracion.Rechazada(MotivoDeRechazoDeUsuario.SucursalInvalida);
        }

        if (nuevo.EsDePersonal() && !CorreoVerificado)
        {
            return DecisionDeAdministracion.Rechazada(MotivoDeRechazoDeUsuario.CorreoSinVerificar);
        }

        // Cambiar de rol invalida las renovaciones anteriores: la vigencia, el segundo factor y los permisos del rol nuevo se exigen en un ingreso completo (IDE-21).
        AvanzarElCorteDeSesiones(ahora);
        Rol = nuevo;
        Sucursal = sucursal;
        return DecisionDeAdministracion.Permitida;
    }
}

/// <summary>Un encargado activo de una sucursal, tal como lo entrega la Aplicación a las reglas.</summary>
public readonly record struct EncargadoDeSucursal(Guid UsuarioId, Guid Sucursal);

/// <summary>Las reglas de la administración de usuarios (IDE-06, IDE-07, IDE-49) que miran a más de un usuario. El instante y los datos llegan como parámetros: el Dominio no consulta nada.</summary>
public static class ReglasDeAdministracion
{
    /// <summary>
    /// ¿Puede <paramref name="actor"/> cambiar a <paramref name="objetivo"/> a <paramref name="nuevoRol"/> (o desactivarlo, si <paramref name="nuevoRol"/> es nulo)? Rechaza si el actor no es administrador central, si es sobre sí mismo
    /// (IDE-49), si degrada o desactiva al último administrador activo (IDE-49), si la sucursal ya tiene otro encargado activo (IDE-06, indicando quién) y las de <see cref="Usuario.CambiarRol"/>.
    /// </summary>
    public static DecisionDeAdministracion CambiarRolODesactivar(
        Usuario actor,
        Usuario objetivo,
        RolDeUsuario? nuevoRol,
        Guid? sucursal,
        int administradoresActivos,
        DateTimeOffset ahora,
        EncargadoDeSucursal? encargadoActualDeLaSucursal)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(objetivo);
        if (actor.Rol != RolDeUsuario.AdministradorCentral || !actor.Activo)
        {
            return DecisionDeAdministracion.Rechazada(MotivoDeRechazoDeUsuario.NoEsAdministrador);
        }

        if (actor.Id == objetivo.Id)
        {
            return DecisionDeAdministracion.Rechazada(MotivoDeRechazoDeUsuario.SobreSiMismo);
        }

        var dejaDeSerAdministrador = objetivo.Rol == RolDeUsuario.AdministradorCentral && objetivo.Activo && (nuevoRol is null || nuevoRol != RolDeUsuario.AdministradorCentral);
        if (dejaDeSerAdministrador && administradoresActivos <= 1)
        {
            return DecisionDeAdministracion.Rechazada(MotivoDeRechazoDeUsuario.UltimoAdministrador);
        }

        if (nuevoRol is { } rol)
        {
            if (rol == RolDeUsuario.EncargadoDeSucursal
                && sucursal is { } destino
                && encargadoActualDeLaSucursal is { } actual
                && actual.Sucursal == destino
                && actual.UsuarioId != objetivo.Id)
            {
                return DecisionDeAdministracion.Rechazada(MotivoDeRechazoDeUsuario.SucursalYaTieneEncargado, actual.UsuarioId);
            }

            // Se evalúa sobre una copia para no modificar al usuario si la decisión es solo una consulta.
            var copia = Usuario.Restaurar(objetivo.Id, objetivo.Correo, objetivo.Rol, objetivo.Sucursal, objetivo.CorreoVerificado, objetivo.Activo);
            return copia.CambiarRol(rol, sucursal, ahora);
        }

        return objetivo.Activo ? DecisionDeAdministracion.Permitida : DecisionDeAdministracion.Rechazada(MotivoDeRechazoDeUsuario.CuentaNoActiva);
    }

    /// <summary>Restablecer la contraseña o el segundo factor de otro usuario: nunca el propio (IDE-49) y solo el administrador central.</summary>
    public static DecisionDeAdministracion Restablecer(Usuario actor, Usuario objetivo)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(objetivo);
        if (actor.Rol != RolDeUsuario.AdministradorCentral || !actor.Activo)
        {
            return DecisionDeAdministracion.Rechazada(MotivoDeRechazoDeUsuario.NoEsAdministrador);
        }

        return actor.Id == objetivo.Id ? DecisionDeAdministracion.Rechazada(MotivoDeRechazoDeUsuario.SobreSiMismo) : DecisionDeAdministracion.Permitida;
    }
}
