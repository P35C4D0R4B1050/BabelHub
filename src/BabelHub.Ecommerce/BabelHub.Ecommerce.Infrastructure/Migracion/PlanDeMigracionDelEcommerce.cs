using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Infrastructure.Auditoria;
using BabelHub.Ecommerce.Infrastructure.Carrito;
using BabelHub.Ecommerce.Infrastructure.Catalogo;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Devoluciones;
using BabelHub.Ecommerce.Infrastructure.Envios;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Reclamaciones;
using BabelHub.Ecommerce.Infrastructure.Web;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Migracion;

/// <summary>
/// El plan de migración del E-commerce (COMP-2): los esquemas de sus módulos en el orden de sus dependencias y las tres cuentas de conexión del anfitrión (la de la aplicación y las de depuración de auditoría y de registros), que se leen
/// de las <b>mismas variables</b> que usa el servicio (<c>BABELHUB__Ecommerce__Base__…</c>): una sola fuente para el usuario y la contraseña de cada cuenta. Pedidos aún no tiene esquema (su infraestructura no existe) y se agrega aquí cuando la tenga.
/// </summary>
public static class PlanDeMigracionDelEcommerce
{
    public const string Servicio = "ecommerce";
    public const string VariableDeLaCuentaPropietaria = "Migrador:Ecommerce:Propietaria";

    /// <summary>Los roles de esquema de los que la aplicación es miembro (<c>NOINHERIT</c>): lo único que puede hacer con <c>SET LOCAL ROLE</c> (AD18).</summary>
    public static IReadOnlyList<string> RolesDeLaAplicacion { get; } =
    [
        "rol_plataforma", "rol_auditoria", "rol_identidad", "rol_web", "rol_catalogo", "rol_carrito", "rol_pagos", "rol_envios", "rol_devoluciones", "rol_reclamaciones",
    ];

    public static IReadOnlyList<string> Esquemas { get; } = ["plataforma", "web", "catalogo", "carrito", "pagos", "envios", "devoluciones", "reclamaciones"];

    public static IReadOnlyList<PasoDeMigracion> Pasos() =>
    [
        new("roles", """
            DO $r$
            DECLARE rol text;
            BEGIN
                FOREACH rol IN ARRAY ARRAY['rol_plataforma', 'rol_web', 'rol_catalogo', 'rol_carrito', 'rol_pagos', 'rol_envios', 'rol_devoluciones', 'rol_reclamaciones', 'reclamaciones_dueno'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = rol) THEN EXECUTE format('CREATE ROLE %I NOLOGIN', rol); END IF;
                END LOOP;
            END
            $r$;
            """),
        new("plataforma", DefinicionDeEntradasProcesadas.Sql + """
            GRANT USAGE ON SCHEMA plataforma TO rol_plataforma;
            GRANT SELECT, INSERT, UPDATE, DELETE ON plataforma.entradas_procesadas TO rol_plataforma;
            """ + DefinicionDeTrabajosDeFondo.Sql + DefinicionDeTrabajosDeFondo.Permisos + DefinicionDelCorreo.Sql + DefinicionDelCorreo.Permisos + DefinicionDeLatido.Sql + DefinicionDeLatido.Rol),
        new("auditoria", DefinicionDeAuditoria.MigracionCompleta(CatalogoDeAccionesDeAuditoria.Crear())),
        new("identidad", MigracionDelEsquemaDeIdentidad.Completa()),
        new("web", DefinicionDeLaWeb.Sql + DefinicionDeLaWeb.Permisos),
        new("catalogo", DefinicionDelCatalogo.Sql + DefinicionDelCatalogo.Permisos),
        new("carrito", DefinicionDelCarrito.Sql + DefinicionDelCarrito.Permisos),
        new("pagos", DefinicionDePagos.Sql + DefinicionDePagos.Permisos),
        new("envios", DefinicionDeEnvios.Sql + DefinicionDeEnvios.Permisos),
        new("devoluciones", DefinicionDeDevoluciones.Sql + DefinicionDeDevoluciones.Permisos),
        new("reclamaciones", DefinicionDeReclamaciones.Sql + DefinicionDeReclamaciones.Propiedad + DefinicionDeReclamaciones.FuncionDeAnonimizacion + DefinicionDeReclamaciones.Permisos),
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

        Cuenta("Ecommerce:Base:Aplicacion", RolesDeLaAplicacion, hereda: false);
        Cuenta("Ecommerce:Base:DepuracionDeAuditoria", ["auditoria_depuracion"], hereda: true);
        Cuenta("Ecommerce:Base:DepuracionDeRegistros", ["auditoria_depuracion"], hereda: true);
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
