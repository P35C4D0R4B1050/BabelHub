using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Infrastructure.Ajustes;
using BabelHub.Inventario.Infrastructure.Auditoria;
using BabelHub.Inventario.Infrastructure.Composicion;
using BabelHub.Inventario.Infrastructure.Fuentes;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Inventario.Infrastructure.Reservas;
using BabelHub.Inventario.Infrastructure.Traslados;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Migracion;

/// <summary>
/// El plan de migración del Inventario (COMP-2): los esquemas de sus módulos en el orden de sus dependencias (Fuentes, Reservas, Ajustes, Traslados) y las tres cuentas de conexión del anfitrión (la de la aplicación y las de depuración de
/// ajustes y de auditoría), que se leen de las <b>mismas variables</b> que usa el servicio (<c>BABELHUB__Inventario__Base__…</c>): una sola fuente para el usuario y la contraseña de cada cuenta.
/// </summary>
public static class PlanDeMigracionDelInventario
{
    public const string Servicio = "inventario";
    public const string VariableDeLaCuentaPropietaria = "Migrador:Inventario:Propietaria";

    /// <summary>Los roles de esquema de los que la aplicación es miembro (<c>NOINHERIT</c>): lo único que puede hacer con <c>SET LOCAL ROLE</c> (AD18).</summary>
    public static IReadOnlyList<string> RolesDeLaAplicacion { get; } =
    [
        "rol_plataforma", "rol_auditoria", "rol_fuentes", "rol_reservas", "rol_ajustes", "rol_traslados",
    ];

    public static IReadOnlyList<string> Esquemas { get; } = ["plataforma", "fuentes", "reservas", "ajustes", "traslados"];

    public static IReadOnlyList<PasoDeMigracion> Pasos() =>
    [
        new("roles", """
            DO $r$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rol_plataforma') THEN CREATE ROLE rol_plataforma NOLOGIN; END IF;
            END
            $r$;
            """),
        new("plataforma", DefinicionDeEntradasProcesadas.Sql + """
            GRANT USAGE ON SCHEMA plataforma TO rol_plataforma;
            GRANT SELECT, INSERT, UPDATE, DELETE ON plataforma.entradas_procesadas TO rol_plataforma;
            """ + DefinicionDeTrabajosDeFondo.Sql + DefinicionDeTrabajosDeFondo.Permisos + DefinicionDeEventosDeLimite.Sql + DefinicionDeEventosDeLimite.Permisos + DefinicionDeLatido.Sql + DefinicionDeLatido.Rol),
        new("auditoria", DefinicionDeAuditoria.MigracionCompleta(CatalogoDeAccionesDeAuditoria.Crear())),
        new("fuentes", DefinicionDeFuentes.Rol + DefinicionDeFuentes.Sql + DefinicionDeFuentes.Permisos),
        new("reservas", DefinicionDeReservas.Rol + DefinicionDeReservas.Sql + DefinicionDeReservas.Permisos),
        new("ajustes", DefinicionDeAjustes.MigracionCompleta),
        new("traslados", DefinicionDeTraslados.MigracionCompleta),
    ];

    /// <summary>La cadena de la cuenta propietaria (administración de la base, solo para migrar): obligatoria y distinta de las cuentas del servicio. Nunca se imprime.</summary>
    public static string Propietaria(IConfiguration configuracion, PlanDeMigracion plan)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        ArgumentNullException.ThrowIfNull(plan);
        var cadena = configuracion[VariableDeLaCuentaPropietaria];
        if (string.IsNullOrWhiteSpace(cadena))
        {
            throw new MigracionFallidaException(LectorDeSecretos.Variable(VariableDeLaCuentaPropietaria) + ": falta la cuenta propietaria de la base.");
        }

        try
        {
            var usuario = new NpgsqlConnectionStringBuilder(cadena).Username;
            if (string.IsNullOrWhiteSpace(usuario) || plan.Cuentas.Any(c => string.Equals(c.Usuario, usuario, StringComparison.OrdinalIgnoreCase)))
            {
                throw new MigracionFallidaException(LectorDeSecretos.Variable(VariableDeLaCuentaPropietaria) + ": la cuenta propietaria necesita usuario y no puede ser una de las cuentas del servicio.");
            }
        }
        catch (ArgumentException)
        {
            throw new MigracionFallidaException(LectorDeSecretos.Variable(VariableDeLaCuentaPropietaria) + ": no es una cadena de conexión válida.");
        }

        return cadena;
    }

    /// <summary>
    /// Arma el plan/ con las cuentas del servicio tal como las configura el anfitrión. Si falta una variable o una cadena no es válida lanza <see cref="MigracionFallidaException"/> nombrando la variable (nunca su valor).
    /// </summary>
    public static PlanDeMigracion Leer(IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var problemas = new List<string>();
        var cuentas = new List<CuentaDeBase>();
        void Cuenta(string ruta, IReadOnlyList<string> roles, bool hereda)
        {
            var cadena = configuracion[ruta];
            if (string.IsNullOrWhiteSpace(cadena))
            {
                problemas.Add(LectorDeSecretos.Variable(ruta) + ": falta");
                return;
            }

            try
            {
                var c = new NpgsqlConnectionStringBuilder(cadena);
                if (string.IsNullOrWhiteSpace(c.Username) || string.IsNullOrEmpty(c.Password))
                {
                    problemas.Add(LectorDeSecretos.Variable(ruta) + ": necesita usuario y contraseña");
                    return;
                }

                cuentas.Add(new CuentaDeBase(c.Username, c.Password, roles, hereda));
            }
            catch (ArgumentException)
            {
                problemas.Add(LectorDeSecretos.Variable(ruta) + ": no es una cadena de conexión válida");
            }
        }

        Cuenta("Inventario:Base:Aplicacion", RolesDeLaAplicacion, hereda: false);
        Cuenta("Inventario:Base:DepuracionDeAjustes", ["ajustes_depuracion"], hereda: true);
        Cuenta("Inventario:Base:DepuracionDeAuditoria", ["auditoria_depuracion"], hereda: true);
        if (cuentas.Select(c => c.Usuario).Distinct(StringComparer.Ordinal).Count() != cuentas.Count)
        {
            problemas.Add("Las cuentas de la aplicación y de depuración deben ser distintas.");
        }

        if (problemas.Count > 0)
        {
            throw new MigracionFallidaException("La configuración de la migración no es válida: " + string.Join("; ", problemas));
        }

        return new PlanDeMigracion(Servicio, Pasos(), Esquemas, cuentas);
    }
}
