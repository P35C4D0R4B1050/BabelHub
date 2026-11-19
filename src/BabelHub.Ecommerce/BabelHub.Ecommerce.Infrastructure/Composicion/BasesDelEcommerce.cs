using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>
/// Las cadenas de conexión del E-commerce y sus fuentes de datos (D10, SEG-11): la aplicación entra con su rol de acceso y cada depuración con su cuenta, <b>aparte</b>. Vive en la composición porque solo la Infraestructura conoce
/// PostgreSQL. Los problemas nombran la variable y nunca su valor.
/// </summary>
public static class BasesDelEcommerce
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

    public static NpgsqlDataSource Abrir(string cadena) => NpgsqlDataSource.Create(cadena);

    public static void RegistrarLaDeLaAplicacion(IServiceCollection servicios, string cadena)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AddSingleton(Abrir(cadena));
    }
}
