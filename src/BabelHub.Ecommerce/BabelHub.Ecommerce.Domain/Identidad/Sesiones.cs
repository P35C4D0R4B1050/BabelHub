namespace BabelHub.Ecommerce.Domain.Identidad;

/// <summary>Para qué sirve el desafío de un ingreso a medias (IDE-42): presentar el segundo factor, configurarlo (IDE-21) o cambiar la contraseña temporal (IDE-26 de la administración).</summary>
public enum ModoDelDesafio
{
    PresentarSegundoFactor,
    ConfigurarSegundoFactor,
    CambiarContrasenaTemporal,
}

public enum EstadoDelDesafio
{
    Vigente,
    Vencido,
    Usado,
}

/// <summary>Lo que la persona intenta hacer con el desafío: solo lo que el modo permite sirve (IDE-42).</summary>
public enum AccionConDesafio
{
    PresentarSegundoFactor,
    ConfigurarSegundoFactor,
    CambiarContrasenaTemporal,
}

/// <summary>
/// El desafío de un ingreso en dos pasos (IDE-42): se crea cuando la contraseña es correcta pero falta el segundo factor, dura <b>5 minutos como máximo</b>, solo permite lo de su modo (configurar el segundo factor o presentarlo, o
/// cambiar la contraseña temporal) y no emite ninguna credencial; vencido, o usado, el ingreso se reinicia. El identificador opaco viaja en la cookie del desafío y aquí se guarda su huella.
/// </summary>
public sealed class DesafioDeIngreso
{
    public static readonly TimeSpan Duracion = TimeSpan.FromMinutes(5);

    private DesafioDeIngreso(Guid id, Guid usuarioId, ModoDelDesafio modo, DateTimeOffset creado, bool usado)
    {
        Id = id;
        UsuarioId = usuarioId;
        Modo = modo;
        Creado = creado;
        Vence = creado + Duracion;
        Usado = usado;
    }

    public Guid Id { get; }

    public Guid UsuarioId { get; }

    public ModoDelDesafio Modo { get; }

    public DateTimeOffset Creado { get; }

    public DateTimeOffset Vence { get; }

    public bool Usado { get; private set; }

    public static DesafioDeIngreso Crear(Guid id, Guid usuarioId, ModoDelDesafio modo, DateTimeOffset ahora)
    {
        if (id == Guid.Empty || usuarioId == Guid.Empty)
        {
            throw new ArgumentException("El desafío necesita su identificador y su usuario.");
        }

        return new DesafioDeIngreso(id, usuarioId, modo, ahora, usado: false);
    }

    public static DesafioDeIngreso Restaurar(Guid id, Guid usuarioId, ModoDelDesafio modo, DateTimeOffset creado, bool usado) => new(id, usuarioId, modo, creado, usado);

    public EstadoDelDesafio Evaluar(DateTimeOffset ahora) => Usado ? EstadoDelDesafio.Usado : ahora >= Vence ? EstadoDelDesafio.Vencido : EstadoDelDesafio.Vigente;

    /// <summary>¿Permite el desafío esta acción? Un desafío de «presentar» no permite configurar, y uno de «cambiar contraseña» solo eso (IDE-42).</summary>
    public bool Permite(AccionConDesafio accion) => accion switch
    {
        AccionConDesafio.PresentarSegundoFactor => Modo == ModoDelDesafio.PresentarSegundoFactor,
        AccionConDesafio.ConfigurarSegundoFactor => Modo == ModoDelDesafio.ConfigurarSegundoFactor,
        AccionConDesafio.CambiarContrasenaTemporal => Modo == ModoDelDesafio.CambiarContrasenaTemporal,
        _ => false,
    };

    /// <summary>Gasta el desafío al completar el paso: solo si sigue vigente y es de esa acción; cada desafío sirve una vez (IDE-42).</summary>
    public bool Consumir(DateTimeOffset ahora, AccionConDesafio accion)
    {
        if (Evaluar(ahora) != EstadoDelDesafio.Vigente || !Permite(accion))
        {
            return false;
        }

        Usado = true;
        return true;
    }
}

/// <summary>Los plazos de la renovación (IDE-46): 7 días para el cliente y 8 horas para el personal, absolutos.</summary>
public static class PoliticaDeRenovacion
{
    public static DateTimeOffset VencimientoAbsoluto(RolDeUsuario rol, DateTimeOffset emitida) => emitida + rol.VigenciaDeLaRenovacion();
}

public enum ResultadoDeRotacion
{
    /// <summary>Se rotó: la renovación anterior queda usada y nace la siguiente, con el mismo vencimiento absoluto.</summary>
    Rotada,

    /// <summary>Pasó el vencimiento absoluto (IDE-46): no se extiende por usarla.</summary>
    Vencida,

    /// <summary>Se revocó (cierre de sesión, «cerrar todas», cambio de contraseña o clave comprometida, IDE-14, IDE-60, IDE-61).</summary>
    Revocada,

    /// <summary>Ya se había rotado: se reutilizó una renovación vieja, lo que indica un robo; se revoca la familia entera (D02).</summary>
    Reutilizada,

    /// <summary>Se emitió antes de <c>sesiones_validas_desde</c> del usuario (por ejemplo, antes de un cambio de contraseña, D17).</summary>
    Invalidada,
}

/// <summary>
/// La renovación opaca de una sesión (IDE-14, IDE-46, IDE-60; D02, D17): de 256 bits (se guarda su SHA-256), con una familia por ingreso, <b>vencimiento absoluto heredado</b> (cada rotación hereda el del ingreso y nunca lo
/// extiende), rotación única (usarla una vez la deja usada; usarla otra vez revoca toda la familia) y revocación. Nunca se imprime.
/// </summary>
public sealed class Renovacion
{
    private Renovacion(string huella, Guid usuarioId, Guid familia, DateTimeOffset emitida, DateTimeOffset expiraAbsoluta, DateTimeOffset? usadaEn, DateTimeOffset? revocadaEn)
    {
        Huella = huella;
        UsuarioId = usuarioId;
        Familia = familia;
        Emitida = emitida;
        ExpiraAbsoluta = expiraAbsoluta;
        UsadaEn = usadaEn;
        RevocadaEn = revocadaEn;
    }

    /// <summary>El SHA-256 en hexadecimal minúsculo del valor opaco de 256 bits (64 caracteres).</summary>
    public string Huella { get; }

    public Guid UsuarioId { get; }

    public Guid Familia { get; }

    public DateTimeOffset Emitida { get; }

    public DateTimeOffset ExpiraAbsoluta { get; }

    public DateTimeOffset? UsadaEn { get; private set; }

    public DateTimeOffset? RevocadaEn { get; private set; }

    /// <summary>La renovación de un ingreso: familia nueva y vencimiento absoluto según el rol.</summary>
    public static Renovacion Emitir(string huella, Guid usuarioId, RolDeUsuario rol, DateTimeOffset ahora) =>
        Crear(huella, usuarioId, Guid.NewGuid(), ahora, PoliticaDeRenovacion.VencimientoAbsoluto(rol, ahora));

    public static Renovacion Restaurar(string huella, Guid usuarioId, Guid familia, DateTimeOffset emitida, DateTimeOffset expiraAbsoluta, DateTimeOffset? usadaEn, DateTimeOffset? revocadaEn) =>
        new(Validar(huella), usuarioId, familia, emitida, expiraAbsoluta, usadaEn, revocadaEn);

    /// <summary>
    /// Rota la renovación (la usa): <paramref name="huellaDeLaSiguiente"/> es la de la nueva, que hereda la familia y el vencimiento absoluto. Rechaza una vencida, revocada, ya rotada (la familia se revoca: el llamador
    /// persiste <see cref="RevocacionDeFamilia"/>) o emitida antes de <paramref name="sesionesValidasDesde"/>.
    /// </summary>
    public (ResultadoDeRotacion Resultado, Renovacion? Siguiente) Rotar(DateTimeOffset ahora, DateTimeOffset? sesionesValidasDesde, string huellaDeLaSiguiente)
    {
        if (RevocadaEn is not null)
        {
            return (ResultadoDeRotacion.Revocada, null);
        }

        if (UsadaEn is not null)
        {
            return (ResultadoDeRotacion.Reutilizada, null);
        }

        if (ahora >= ExpiraAbsoluta)
        {
            return (ResultadoDeRotacion.Vencida, null);
        }

        if (sesionesValidasDesde is { } desde && Emitida < desde)
        {
            return (ResultadoDeRotacion.Invalidada, null);
        }

        UsadaEn = ahora;
        return (ResultadoDeRotacion.Rotada, Crear(huellaDeLaSiguiente, UsuarioId, Familia, ahora, ExpiraAbsoluta));
    }

    /// <summary>Cierra la sesión: la renovación deja de funcionar (IDE-14, AC-IDE-12).</summary>
    public void Revocar(DateTimeOffset ahora) => RevocadaEn ??= ahora;

    /// <summary>¿Funciona esta renovación ahora? No vencida, no revocada, no usada y no anterior a <paramref name="sesionesValidasDesde"/>.</summary>
    public bool EstaVigente(DateTimeOffset ahora, DateTimeOffset? sesionesValidasDesde = null) =>
        RevocadaEn is null && UsadaEn is null && ahora < ExpiraAbsoluta && (sesionesValidasDesde is not { } desde || Emitida >= desde);

    /// <summary>Qué renovaciones se revocan cuando se reutiliza una ya rotada: todas las de la familia (D02).</summary>
    public RevocacionDeFamilia RevocacionDeFamilia(DateTimeOffset ahora) => new(UsuarioId, Familia, ahora);

    public override string ToString() => "[renovación]";

    private static Renovacion Crear(string huella, Guid usuarioId, Guid familia, DateTimeOffset emitida, DateTimeOffset expiraAbsoluta) =>
        new(Validar(huella), usuarioId, familia, emitida, expiraAbsoluta, null, null);

    private static string Validar(string huella)
    {
        if (huella is null || huella.Length != 64 || !huella.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new ArgumentException("La huella de la renovación es un SHA-256 de 64 caracteres hexadecimales en minúscula.", nameof(huella));
        }

        return huella;
    }
}

/// <summary>La orden de revocar toda una familia de renovaciones (reutilización de una ya rotada).</summary>
public readonly record struct RevocacionDeFamilia(Guid UsuarioId, Guid Familia, DateTimeOffset Instante);

/// <summary>
/// El corte de sesiones de un usuario (IDE-60, D17): «cerrar todas», cambiar la contraseña o declarar comprometida la clave fijan <c>sesiones_validas_desde</c> y toda renovación emitida antes deja de servir, aunque su fila no se
/// haya tocado. El corte solo avanza.
/// </summary>
public static class CorteDeSesiones
{
    public static DateTimeOffset Avanzar(DateTimeOffset? actual, DateTimeOffset ahora) => actual is { } previo && previo > ahora ? previo : ahora;
}
