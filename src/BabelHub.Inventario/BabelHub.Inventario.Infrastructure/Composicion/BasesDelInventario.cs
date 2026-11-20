using BabelHub.Inventario.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Composicion;

/// <summary>
/// Las cadenas de conexión del Inventario y sus fuentes de datos (D10, SEG-11): la aplicación entra con su rol de acceso y cada depuración con su cuenta, <b>aparte</b>. Vive en la composición porque solo la Infraestructura conoce
/// PostgreSQL. Los problemas nombran la variable y nunca su valor.
/// </summary>
public static class BasesDelInventario
{
    /// <summary>Lee una cadena obligatoria: debe ser válida y, en producción, traer usuario y contraseña propios.</summary>
    public static string? Leer(LectorDeSecretos lector, string ruta, bool esProduccion)
    {
        ArgumentNullException.ThrowIfNull(lector);
        var valor = lector.Obligatorio(ruta);
        if (valor is null)
        {
            return null;
        }

        NpgsqlConnectionStringBuilder cadena;
        try
        {
            cadena = new NpgsqlConnectionStringBuilder(valor);
        }
        catch (ArgumentException)
        {
            lector.Problema(ruta, "no es una cadena de conexión válida");
            return null;
        }

        if (esProduccion && (string.IsNullOrWhiteSpace(cadena.Username) || string.IsNullOrEmpty(cadena.Password)))
        {
            lector.Problema(ruta, "en producción exige usuario y contraseña propios");
            return null;
        }

        return valor;
    }

    /// <summary>Las cuentas deben ser <b>cuentas</b> distintas (servidor, puerto, base y usuario): cambiar el nombre de la aplicación u otro parámetro no las separa.</summary>
    public static void ComprobarCuentasDistintas(LectorDeSecretos lector, params (string Ruta, string? Cadena)[] cuentas)
    {
        ArgumentNullException.ThrowIfNull(lector);
        var vistas = new List<(string Ruta, string Cuenta)>();
        foreach (var (ruta, cadena) in cuentas)
        {
            if (cadena is null)
            {
                continue;
            }

            var c = new NpgsqlConnectionStringBuilder(cadena);
            var cuenta = string.Join('|', c.Host?.ToLowerInvariant(), c.Port, c.Database?.ToLowerInvariant(), c.Username?.ToLowerInvariant());
            if (vistas.FirstOrDefault(v => v.Cuenta == cuenta) is { Ruta: not null } igual)
            {
                lector.Problema(ruta, $"usa la misma cuenta que {LectorDeSecretos.Variable(igual.Ruta)}: las cuentas de la aplicación y de depuración deben ser distintas");
            }
            else
            {
                vistas.Add((ruta, cuenta));
            }
        }
    }

    /// <summary>Máximo de conexiones del grupo de una cuenta cuando la cadena no fija uno: la cuenta tiene <c>CONNECTION LIMIT 20</c> (migrador y aprovisionamiento) y el grupo por omisión de Npgsql abre hasta 100, lo que con más de 20 consultas simultáneas daba «too many connections» y un 500 (hallazgo de K6-1). Por debajo del límite, el exceso espera un turno.</summary>
    public const int MaximoDeConexionesPorOmision = 16;

    public static NpgsqlDataSource Abrir(string cadena) => Abrir(cadena, null);

    /// <summary>Como <see cref="Abrir(string)"/>, con una fábrica de registros opcional (las pruebas cuentan las sentencias que Npgsql envía a la base).</summary>
    internal static NpgsqlDataSource Abrir(string cadena, Microsoft.Extensions.Logging.ILoggerFactory? registros)
    {
        // El constructor de Npgsql responde «sí» a toda palabra clave conocida: lo que el operador fijó se lee en la cadena tal cual.
        var escrito = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = cadena };
        var constructor = new NpgsqlConnectionStringBuilder(cadena);
        if (!escrito.ContainsKey("Maximum Pool Size") && !escrito.ContainsKey("MaxPoolSize"))
        {
            constructor.MaxPoolSize = MaximoDeConexionesPorOmision;
        }

        // Sin reinicio de sesión al devolver la conexión al grupo (Npgsql manda <c>DISCARD ALL</c> por omisión: era la tercera sentencia que más tiempo gastaba la base en la carga, DOC-1 §6b). Es
        // compatible con el aislamiento porque la aplicación no deja nada en la sesión: el rol (<c>SET LOCAL ROLE</c>), el alcance de datos (<c>set_config(…, true)</c>) y las esperas máximas valen solo
        // dentro de la transacción y la base los descarta al confirmar o revertir (la prueba de «no sobreviven a la transacción» lo comprueba). Lo que el operador fijó en la cadena se respeta.
        // Invariante: ningún código del Inventario deja estado de sesión (un `SET` sin LOCAL, `set_config(…, false)`, tablas temporales). Los bloqueos asesores de sesión cierran la conexión física si no se pueden soltar.
        // El migrador usa estado de sesión (`SET log_statement`, bloqueo asesor) pero su origen de datos se destruye al terminar, así que no se reutiliza.
        if (!escrito.ContainsKey("No Reset On Close") && !escrito.ContainsKey("NoResetOnClose"))
        {
            constructor.NoResetOnClose = true;
        }

        var origen = new NpgsqlDataSourceBuilder(constructor.ConnectionString);
        if (registros is not null)
        {
            origen.UseLoggerFactory(registros);
        }

        return origen.Build();
    }

    public static void RegistrarLaDeLaAplicacion(IServiceCollection servicios, string cadena)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AddSingleton(Abrir(cadena));
    }
}
