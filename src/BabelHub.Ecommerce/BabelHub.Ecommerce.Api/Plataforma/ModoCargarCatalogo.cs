using System.Globalization;
using System.Text;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Catalogo;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;

namespace BabelHub.Ecommerce.Api.Plataforma;

/// <summary>Los argumentos de <c>cargar-catalogo</c>: <c>--archivo</c>, <c>--portadas</c> y <c>--plan</c> (los tres obligatorios).</summary>
public sealed record ArgumentosDeCargarCatalogo(string Archivo, string Portadas, string Plan)
{
    /// <summary>Lee los argumentos tras el nombre de la orden; el error es una clave simbólica, nunca el valor recibido.</summary>
    public static (ArgumentosDeCargarCatalogo? Argumentos, string? Error) Leer(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? archivo = null;
        string? portadas = null;
        string? plan = null;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] is not ("--archivo" or "--portadas" or "--plan"))
            {
                return (null, "argumento-desconocido");
            }

            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return (null, "falta-el-valor-de-" + args[i][2..]);
            }

            var valor = args[i + 1];
            switch (args[i])
            {
                case "--archivo":
                    archivo = valor;
                    break;
                case "--portadas":
                    portadas = valor;
                    break;
                default:
                    plan = valor;
                    break;
            }

            i++;
        }

        if (archivo is null)
        {
            return (null, "falta-archivo");
        }

        if (portadas is null)
        {
            return (null, "falta-portadas");
        }

        return plan is null ? (null, "falta-plan") : (new ArgumentosDeCargarCatalogo(archivo, portadas, plan), null);
    }
}

/// <summary>
/// La orden de operación <c>cargar-catalogo</c> del E-commerce (transversal-carga-inicial, CIN-01 a CIN-10, CIN-17, CIN-18, CIN-20): <c>dotnet BabelHub.Ecommerce.Api.dll cargar-catalogo --archivo … --portadas … --plan …</c>. Compone el servicio con la
/// <b>misma configuración</b> en el modo <see cref="ModoDeArranque.Operacion"/> (adaptadores nulos, sin trabajos de fondo, sin escuchar en ningún puerto), reemplaza el Inventario por el diferido, fija la identidad de operación «carga inicial» (administración
/// central, sin cuenta ni credencial: solo se construye aquí) y llama a <see cref="ICargaInicialDelCatalogo"/>. Falta una variable: termina antes de escribir nombrándola, nunca su valor. Códigos de salida: <c>0</c> todo cargado o ya cargado, <c>2</c> parcial
/// (alguna fila rechazada o sin cargar, también por el almacén de objetos caído) y <c>1</c> error (configuración, archivo inválido, la corrida sin registrar, falla inesperada). Nunca imprime rutas, cadenas de conexión, claves ni mensajes de excepción: de una
/// falla inesperada, solo el tipo y el SQLSTATE.
/// </summary>
public static class ModoCargarCatalogo
{
    public const string Argumento = "cargar-catalogo";

    /// <summary>El tope de lectura del archivo de datos (CIN-04): 2 MB.</summary>
    public const int MaximoDeBytes = 2 * 1024 * 1024;

    public static async Task<int> Ejecutar(string[] args, TextWriter salida, TextWriter error, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(salida);
        ArgumentNullException.ThrowIfNull(error);

        var (argumentos, problema) = ArgumentosDeCargarCatalogo.Leer(args.Skip(1).ToList());
        if (argumentos is null)
        {
            await error.WriteLineAsync("cargar-catalogo: " + problema + ". Uso: cargar-catalogo --archivo <catalogo.csv> --portadas <carpeta> --plan <plan.json>").ConfigureAwait(false);
            return 1;
        }

        try
        {
            // El archivo se lee antes de componer: uno ilegible termina sin tocar la base ni el almacén.
            var texto = await LeerAcotado(argumentos.Archivo, error, cancelacion).ConfigureAwait(false);
            if (texto is null)
            {
                return 1;
            }

            // Sin argumentos de línea de órdenes: la configuración sale solo del entorno (BABELHUB__…), y ningún registro de la composición llega a la consola.
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
            builder.Configuration.AddEnvironmentVariables(prefix: "BABELHUB__");
            builder.Logging.ClearProviders();
            builder.AgregarEcommerce(ModoDeArranque.Operacion);
            builder.Services.AgregarCargaInicialDelCatalogo();
            await using var app = builder.Build();
            ArranqueSeguro.ExigirCoherencia(ModoDeArranque.Operacion, builder.Environment.IsProduction(), app.Services.GetServices<IAdaptadorSaliente>());

            await using var alcance = app.Services.CreateAsyncScope();
            alcance.ServiceProvider.GetRequiredService<IEstablecedorDeActor>().Establecer(ActorActual.DeUsuario(Guid.Parse(IdentidadesDeOperacion.CargaInicial), RolesDeUsuario.AdministradorCentral));
            var resultado = await alcance.ServiceProvider.GetRequiredService<ICargaInicialDelCatalogo>()
                .Cargar(new SolicitudDeCargaDelCatalogo(texto, new CarpetaDePortadasDeLaCarga(argumentos.Portadas)), cancelacion)
                .ConfigureAwait(false);
            return await Informar(resultado, argumentos.Plan, salida, error, cancelacion).ConfigureAwait(false);
        }
        catch (ConfiguracionDeSecretosInvalidaException e)
        {
            // Los problemas nombran la variable y el motivo, nunca su valor (SEG-11).
            await error.WriteLineAsync("cargar-catalogo: configuración inválida:").ConfigureAwait(false);
            foreach (var p in e.Problemas)
            {
                await error.WriteLineAsync("  " + p).ConfigureAwait(false);
            }

            return 1;
        }
        catch (ArranqueRechazadoException)
        {
            await error.WriteLineAsync("cargar-catalogo: el arranque de la orden fue rechazado (modo o adaptadores incoherentes).").ConfigureAwait(false);
            return 1;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("cargar-catalogo: la orden fue cancelada; repítela, lo ya cargado no se duplica.").ConfigureAwait(false);
            return 1;
        }
        catch (Exception e)
        {
            // Solo el tipo y el SQLSTATE: el mensaje de las bibliotecas puede traer cadenas de conexión, endpoints o nombres de objetos (CIN-20).
            var codigo = e is Npgsql.PostgresException pg ? " (SQLSTATE " + pg.SqlState + ")" : string.Empty;
            await error.WriteLineAsync("cargar-catalogo: falló la carga: " + e.GetType().Name + codigo).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> Informar(Application.Plataforma.Resultado<ResumenDeLaCargaDelCatalogo> resultado, string rutaDelPlan, TextWriter salida, TextWriter error, CancellationToken cancelacion)
    {
        if (!resultado.EsExito)
        {
            await error.WriteLineAsync("cargar-catalogo: rechazada: " + resultado.Error!.Clave).ConfigureAwait(false);
            foreach (var campo in resultado.Error.Errores)
            {
                await error.WriteLineAsync("  " + campo.Campo + ": " + campo.Clave).ConfigureAwait(false);
            }

            return 1;
        }

        var r = resultado.Valor;
        var c = CultureInfo.InvariantCulture;
        await salida.WriteLineAsync($"corrida {r.CorridaId:D} archivo {r.HuellaDelTexto[..16]}").ConfigureAwait(false);
        foreach (var fila in r.Filas.Where(f => f.Resultado == ResultadoDeFilaDeCarga.Rechazada))
        {
            var clave = fila.Etiqueta.Length == 0 ? string.Empty : " (" + fila.Etiqueta + ")";
            await salida.WriteLineAsync($"fila {fila.Fila.ToString(c)}{clave}: rechazada: {fila.Campo}: {fila.Motivo}").ConfigureAwait(false);
        }

        await salida.WriteLineAsync($"cargadas: {r.Cargadas.ToString(c)}; ya cargadas: {r.YaCargadas.ToString(c)}; rechazadas: {r.Rechazadas.ToString(c)}").ConfigureAwait(false);

        // El plan se escribe en un archivo temporal y se renombra al final: nunca queda un plan a medias.
        if (!await EscribirElPlan(rutaDelPlan, r.Plan.ComoJson(), cancelacion).ConfigureAwait(false))
        {
            await error.WriteLineAsync("cargar-catalogo: no se pudo escribir el plan de inventario").ConfigureAwait(false);
            return 1;
        }

        await salida.WriteLineAsync($"plan de inventario: {r.Plan.Ediciones.Count.ToString(c)} ediciones").ConfigureAwait(false);
        if (r.RegistroDeLaCorridaFallo)
        {
            await error.WriteLineAsync("cargar-catalogo: no se pudo registrar la corrida en la auditoría").ConfigureAwait(false);
            return 1;
        }

        return r.Completa ? 0 : 2;
    }

    private static async Task<bool> EscribirElPlan(string ruta, string json, CancellationToken cancelacion)
    {
        try
        {
            var temporal = ruta + ".tmp";
            await File.WriteAllTextAsync(temporal, json, new UTF8Encoding(false), cancelacion).ConfigureAwait(false);
            File.Move(temporal, ruta, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Lee el archivo de datos de hasta 2 MB (UTF-8 estricto); ante cualquier problema escribe solo una clave, nunca la ruta.</summary>
    private static async Task<string?> LeerAcotado(string ruta, TextWriter error, CancellationToken cancelacion)
    {
        try
        {
            await using var archivo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            if (archivo.Length > MaximoDeBytes)
            {
                await error.WriteLineAsync("cargar-catalogo: archivo: archivo-demasiado-grande").ConfigureAwait(false);
                return null;
            }

            var bytes = new byte[archivo.Length];
            await archivo.ReadExactlyAsync(bytes, cancelacion).ConfigureAwait(false);
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            await error.WriteLineAsync("cargar-catalogo: archivo: no-es-texto-valido").ConfigureAwait(false);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await error.WriteLineAsync("cargar-catalogo: archivo: archivo-ilegible").ConfigureAwait(false);
            return null;
        }
    }
}
