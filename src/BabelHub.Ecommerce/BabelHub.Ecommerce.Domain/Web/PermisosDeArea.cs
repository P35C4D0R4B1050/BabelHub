namespace BabelHub.Ecommerce.Domain.Web;

/// <summary>Las tres áreas de la interfaz (WEB-02).</summary>
public enum AreaDeInterfaz
{
    Tienda,
    Sucursal,
    Administracion,
}

/// <summary>El rol de quien mira la interfaz (tipo propio del módulo <c>Web</c>: la credencial de Identidad se traduce a él en la Aplicación).</summary>
public enum RolDeInterfaz
{
    Visitante,
    Cliente,
    Encargado,
    Logistica,
    AdministradorCentral,
}

public static class RolesDeInterfaz
{
    /// <summary>El personal (encargado, logística y administración central): tope de sesión de 8 horas y segundo factor (WEB-31, IDE-20).</summary>
    public static bool EsPersonal(this RolDeInterfaz rol) => rol is RolDeInterfaz.Encargado or RolDeInterfaz.Logistica or RolDeInterfaz.AdministradorCentral;
}

/// <summary>
/// Qué área ve cada rol y qué host la sirve (WEB-02, D03): cliente y visitante, Tienda; encargado y logística, Sucursal; administración central,
/// Sucursal y Administración. La tienda se sirve en el host <c>tienda</c> y Sucursal y Administración en la <c>gestion</c>.
/// </summary>
public static class PermisosDeArea
{
    public static IReadOnlyList<AreaDeInterfaz> Areas(RolDeInterfaz rol) => rol switch
    {
        RolDeInterfaz.Visitante or RolDeInterfaz.Cliente => [AreaDeInterfaz.Tienda],
        RolDeInterfaz.Encargado or RolDeInterfaz.Logistica => [AreaDeInterfaz.Sucursal],
        RolDeInterfaz.AdministradorCentral => [AreaDeInterfaz.Sucursal, AreaDeInterfaz.Administracion],
        _ => [],
    };

    public static bool VeLaArea(RolDeInterfaz rol, AreaDeInterfaz area) => Areas(rol).Contains(area);

    public static HostDeSesion HostDeLaArea(AreaDeInterfaz area) => area == AreaDeInterfaz.Tienda ? HostDeSesion.Tienda : HostDeSesion.Gestion;

    /// <summary>
    /// Un cliente solo ingresa por la tienda y el personal solo por la gestión (D03); el visitante no tiene sesión. El ingreso rechaza la pareja que no
    /// corresponde con el mismo mensaje que una credencial inválida.
    /// </summary>
    public static bool ParejaDeRolYHostValida(RolDeInterfaz rol, HostDeSesion host) => rol switch
    {
        RolDeInterfaz.Cliente => host == HostDeSesion.Tienda,
        RolDeInterfaz.Encargado or RolDeInterfaz.Logistica or RolDeInterfaz.AdministradorCentral => host == HostDeSesion.Gestion,
        _ => false,
    };
}

/// <summary>Los pasos del ingreso que Identidad exige antes de emitir una credencial (WEB-34, IDE-23, IDE-42): no son sesión, son un desafío previo.</summary>
public enum PasoPendienteDeIngreso
{
    Ninguno,
    ContrasenaTemporal,
    ConfigurarSegundoFactor,
    PresentarSegundoFactor,
}

public static class PasosDelIngreso
{
    /// <summary>El orden en que se piden: primero cambiar la contraseña temporal y después configurar o presentar el segundo factor.</summary>
    public static int Orden(this PasoPendienteDeIngreso paso) => paso switch
    {
        PasoPendienteDeIngreso.ContrasenaTemporal => 1,
        PasoPendienteDeIngreso.ConfigurarSegundoFactor => 2,
        PasoPendienteDeIngreso.PresentarSegundoFactor => 3,
        _ => 0,
    };

    /// <summary>El primero de los pasos pendientes según su orden; <see cref="PasoPendienteDeIngreso.Ninguno"/> si no hay ninguno.</summary>
    public static PasoPendienteDeIngreso Primero(IEnumerable<PasoPendienteDeIngreso> pendientes)
    {
        ArgumentNullException.ThrowIfNull(pendientes);
        return pendientes.Where(p => p != PasoPendienteDeIngreso.Ninguno).OrderBy(p => p.Orden()).FirstOrDefault();
    }

    /// <summary>Un paso del ingreso pesa más que los textos legales: se completa primero y aceptar los textos nunca lo resuelve (WEB-34).</summary>
    public static bool PrevaleceSobreLosTextos(PasoPendienteDeIngreso paso) => paso != PasoPendienteDeIngreso.Ninguno;
}

/// <summary>Lo que una persona con sesión puede hacer mientras tiene pendiente aceptar los textos legales (WEB-41, WEB-42, WEB-43, WEB-46).</summary>
public enum CapacidadDeAcceso
{
    Cuenta,
    Pedidos,
    Aceptar,
    CerrarSesion,
    TextosLegales,
    LibroDeReclamaciones,
    EnviarHoja,
}

public enum ClaseDeCapacidad
{
    /// <summary>Solo <c>GET</c>.</summary>
    Lectura,

    /// <summary>Solo <c>POST</c>, de una lista cerrada: aceptar, cerrar sesión y enviar una hoja de reclamación.</summary>
    Accion,
}

public static class CapacidadesDeAcceso
{
    public static ClaseDeCapacidad Clase(this CapacidadDeAcceso capacidad) =>
        capacidad is CapacidadDeAcceso.Aceptar or CapacidadDeAcceso.CerrarSesion or CapacidadDeAcceso.EnviarHoja ? ClaseDeCapacidad.Accion : ClaseDeCapacidad.Lectura;

    /// <summary>
    /// Si el método HTTP corresponde a la clase de la capacidad. La comparación es ordinal y distingue mayúsculas: <c>get</c> o un método no estándar no
    /// son <c>GET</c>. <c>HEAD</c>, <c>OPTIONS</c>, <c>TRACE</c>, <c>PUT</c> y <c>DELETE</c> nunca se admiten (negación por defecto, WEB-46).
    /// </summary>
    public static bool AdmiteElMetodo(this CapacidadDeAcceso capacidad, string? metodo) =>
        capacidad.Clase() == ClaseDeCapacidad.Lectura ? string.Equals(metodo, "GET", StringComparison.Ordinal) : string.Equals(metodo, "POST", StringComparison.Ordinal);
}

/// <summary>
/// Qué capacidades conserva cada rol con los textos legales pendientes (decisión B literal del 2026-11-14): el cliente, su cuenta y sus pedidos; el personal,
/// solo su cuenta; ambos pueden aceptar los textos, cerrar sesión, leer los textos legales y usar el Libro de Reclamaciones (con el envío de la hoja). Con
/// Identidad caída (WEB-45) solo queda cerrar sesión, los textos legales, el Libro y enviar una hoja. Sin rutas ni métodos: eso lo declara la Api.
/// </summary>
public static class PoliticaDeAccesoConTextosPendientes
{
    public static IReadOnlySet<CapacidadDeAcceso> Capacidades(RolDeInterfaz rol, bool identidadCaida)
    {
        if (rol == RolDeInterfaz.Visitante)
        {
            return new HashSet<CapacidadDeAcceso>();
        }

        if (identidadCaida)
        {
            return new HashSet<CapacidadDeAcceso>
            {
                CapacidadDeAcceso.CerrarSesion,
                CapacidadDeAcceso.TextosLegales,
                CapacidadDeAcceso.LibroDeReclamaciones,
                CapacidadDeAcceso.EnviarHoja,
            };
        }

        var permitidas = new HashSet<CapacidadDeAcceso>
        {
            CapacidadDeAcceso.Cuenta,
            CapacidadDeAcceso.Aceptar,
            CapacidadDeAcceso.CerrarSesion,
            CapacidadDeAcceso.TextosLegales,
            CapacidadDeAcceso.LibroDeReclamaciones,
            CapacidadDeAcceso.EnviarHoja,
        };
        if (rol == RolDeInterfaz.Cliente)
        {
            permitidas.Add(CapacidadDeAcceso.Pedidos);
        }

        return permitidas;
    }
}
