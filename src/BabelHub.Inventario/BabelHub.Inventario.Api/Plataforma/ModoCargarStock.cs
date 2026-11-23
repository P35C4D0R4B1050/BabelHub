using System.Globalization;
using System.Text;
using BabelHub.Inventario.Application.Ajustes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.Api.Plataforma;

/// <summary>Los argumentos de <c>cargar-stock</c>: <c>--plan</c> y <c>--sucursales</c> (obligatorios), <c>--rutas</c> y <c>--esperar</c> (opcionales).</summary>
public sealed record ArgumentosDeCargarStock(string Plan, string Sucursales, string? Rutas, bool Esperar)
{
    /// <summary>Lee los argumentos tras el nombre de la orden; el error es una clave simbólica, nunca el valor recibido.</summary>
    public static (ArgumentosDeCargarStock? Argumentos, string? Error) Leer(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? plan = null;
        string? sucursales = null;
        string? rutas = null;
        var esperar = false;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--esperar":
                    esperar = true;
                    break;
                case "--plan" or "--sucursales" or "--rutas":
                    if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        return (null, "falta-el-valor-de-" + args[i][2..]);
                    }

                    var valor = args[++i];
                    if (args[i - 1] == "--plan")
                    {
                        plan = valor;
                    }
                    else if (args[i - 1] == "--sucursales")
                    {
                        sucursales = valor;
                    }
                    else
                    {
                        rutas = valor;
                    }

                    break;
                default:
                    return (null, "argumento-desconocido");
            }
        }

        if (plan is null)
        {
            return (null, "falta-plan");
        }

        return sucursales is null ? (null, "falta-sucursales") : (new ArgumentosDeCargarStock(plan, sucursales, rutas, esperar), null);
    }
}

/// <summary>
/// La orden de operación <c>cargar-stock</c> del Inventario (transversal-carga-inicial, CIN-08 a CIN-14, CIN-17, CIN-18, CIN-20): <c>dotnet BabelHub.Inventario.Api.dll cargar-stock --plan … --sucursales … [--rutas …] [--esperar]</c>.
/// Compone el servicio con la <b>misma configuración</b> en el modo <see cref="ModoDeArranque.Operacion"/> (adaptadores nulos, sin trabajos de fondo, sin escuchar en ningún puerto), fija la identidad de operación «carga inicial»
/// (administración central, sin cuenta ni credencial: solo se construye aquí) y llama al contrato <see cref="ICargaInicialDelInventario"/>. Falta una variable: termina antes de escribir nombrándola, nunca su valor.
/// Códigos de salida: <c>0</c> todo cargado o ya cargado, <c>2</c> parcial (rechazos o presupuesto agotado, con la hora de reanudación) y <c>1</c> error (configuración, archivo inválido, falla inesperada). Nunca imprime rutas, cadenas de
/// conexión, claves ni mensajes de excepción: de una falla inesperada, solo el tipo y el SQLSTATE.
/// </summary>
public static class ModoCargarStock
{
    public const string Argumento = "cargar-stock";

    /// <summary>El tope de lectura de cada archivo (CIN-04): 2 MB.</summary>
    public const int MaximoDeBytes = 2 * 1024 * 1024;

    public static async Task<int> Ejecutar(string[] args, TextWriter salida, TextWriter error, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(salida);
        ArgumentNullException.ThrowIfNull(error);

        var (argumentos, problema) = ArgumentosDeCargarStock.Leer(args.Skip(1).ToList());
        if (argumentos is null)
        {
            await error.WriteLineAsync("cargar-stock: " + problema + ". Uso: cargar-stock --plan <plan.json> --sucursales <sucursales.csv> [--rutas <rutas.csv>] [--esperar]").ConfigureAwait(false);
            return 1;
        }

        try
        {
            // Los archivos se leen antes de componer: un archivo ilegible termina sin tocar la base.
            var plan = await LeerAcotado("plan", argumentos.Plan, error, cancelacion).ConfigureAwait(false);
            var sucursales = await LeerAcotado("sucursales", argumentos.Sucursales, error, cancelacion).ConfigureAwait(false);
            var rutas = argumentos.Rutas is null ? string.Empty : await LeerAcotado("rutas", argumentos.Rutas, error, cancelacion).ConfigureAwait(false);
            if (plan is null || sucursales is null || rutas is null)
            {
                return 1;
            }

            // Sin argumentos de línea de órdenes: la configuración sale solo del entorno (BABELHUB__…), y ningún registro de la composición llega a la consola.
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
            builder.Configuration.AddEnvironmentVariables(prefix: "BABELHUB__");
            builder.Logging.ClearProviders();
            builder.AgregarInventario(ModoDeArranque.Operacion);
            await using var app = builder.Build();
            ArranqueSeguro.ExigirCoherencia(ModoDeArranque.Operacion, builder.Environment.IsProduction(), app.Services.GetServices<IAdaptadorSaliente>());

            await using var alcance = app.Services.CreateAsyncScope();
            alcance.ServiceProvider.GetRequiredService<IEstablecedorDeActor>().Establecer(ActorActual.DeUsuario(Guid.Parse(IdentidadesDeOperacion.CargaInicial), RolesDeUsuario.AdministradorCentral));
            var resultado = await alcance.ServiceProvider.GetRequiredService<ICargaInicialDelInventario>()
                .Cargar(new SolicitudDeCargaDeInventario(plan, sucursales, rutas.Length == 0 ? null : rutas, argumentos.Esperar), cancelacion)
                .ConfigureAwait(false);
            return await Informar(resultado, salida, error).ConfigureAwait(false);
        }
        catch (ConfiguracionDeSecretosInvalidaException e)
        {
            // Los problemas nombran la variable y el motivo, nunca su valor (SEG-11).
            await error.WriteLineAsync("cargar-stock: configuración inválida:").ConfigureAwait(false);
            foreach (var p in e.Problemas)
            {
                await error.WriteLineAsync("  " + p).ConfigureAwait(false);
            }

            return 1;
        }
        catch (ArranqueRechazadoException)
        {
            await error.WriteLineAsync("cargar-stock: el arranque de la orden fue rechazado (modo o adaptadores incoherentes).").ConfigureAwait(false);
            return 1;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Solo el tipo y el SQLSTATE: el mensaje de las bibliotecas puede traer cadenas de conexión, endpoints o nombres de objetos (CIN-20).
            var codigo = e is Npgsql.PostgresException pg ? " (SQLSTATE " + pg.SqlState + ")" : string.Empty;
            await error.WriteLineAsync("cargar-stock: falló la carga: " + e.GetType().Name + codigo).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> Informar(Resultado<ResumenDeCargaDeInventario> resultado, TextWriter salida, TextWriter error)
    {
        if (!resultado.EsExito)
        {
            var clave = resultado.Error!.Clave;
            await error.WriteLineAsync("cargar-stock: rechazada: " + clave).ConfigureAwait(false);
            foreach (var campo in resultado.Error.Errores)
            {
                await error.WriteLineAsync("  " + campo.Campo + ": " + campo.Clave).ConfigureAwait(false);
            }

            return 1;
        }

        var r = resultado.Valor;
        var c = CultureInfo.InvariantCulture;
        await salida.WriteLineAsync($"corrida {r.CorridaId:D} plan {r.HuellaDelPlan[..16]}").ConfigureAwait(false);
        await salida.WriteLineAsync($"sucursales: {r.SucursalesNuevas.ToString(c)} nuevas, {r.SucursalesExistentes.ToString(c)} ya existían; rutas: {r.RutasNuevas.ToString(c)} nuevas, {r.RutasExistentes.ToString(c)} ya existían").ConfigureAwait(false);
        await salida.WriteLineAsync($"ediciones registradas: {r.EdicionesRegistradas.ToString(c)}; stock cargado: {r.StockCargado.ToString(c)}; ya cargado: {r.YaCargado.ToString(c)}; rechazadas: {r.Rechazadas.ToString(c)}").ConfigureAwait(false);
        foreach (var p in r.Problemas)
        {
            await salida.WriteLineAsync("  rechazada: " + p).ConfigureAwait(false);
        }

        if (r.PendientesPorLimite > 0)
        {
            await salida.WriteLineAsync($"parcial por límite: {r.PendientesPorLimite.ToString(c)} operaciones pendientes del presupuesto por hora; repetir la orden a partir de {r.ReanudaDesde?.ToString("O", c)} (o usar --esperar)").ConfigureAwait(false);
        }

        if (r.RegistroDeLaCorridaFallo)
        {
            await error.WriteLineAsync("cargar-stock: no se pudo registrar la corrida en la auditoría").ConfigureAwait(false);
            return 1;
        }

        return r.Completa ? 0 : 2;
    }

    /// <summary>Lee un archivo de texto de hasta 2 MB (UTF-8 estricto); ante cualquier problema escribe solo la etiqueta del archivo y una clave, nunca la ruta.</summary>
    private static async Task<string?> LeerAcotado(string etiqueta, string ruta, TextWriter error, CancellationToken cancelacion)
    {
        try
        {
            await using var archivo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            if (archivo.Length > MaximoDeBytes)
            {
                await error.WriteLineAsync($"cargar-stock: {etiqueta}: archivo-demasiado-grande").ConfigureAwait(false);
                return null;
            }

            var bytes = new byte[archivo.Length];
            await archivo.ReadExactlyAsync(bytes, cancelacion).ConfigureAwait(false);
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            await error.WriteLineAsync($"cargar-stock: {etiqueta}: no-es-texto-valido").ConfigureAwait(false);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await error.WriteLineAsync($"cargar-stock: {etiqueta}: archivo-ilegible").ConfigureAwait(false);
            return null;
        }
    }
}
