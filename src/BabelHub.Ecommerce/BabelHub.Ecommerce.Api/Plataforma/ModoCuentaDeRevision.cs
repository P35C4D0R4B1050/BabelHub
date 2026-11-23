using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Plataforma;

namespace BabelHub.Ecommerce.Api.Plataforma;

/// <summary>
/// El modo <c>cuenta-de-prueba</c> del servicio (PAG-35): <c>dotnet BabelHub.Ecommerce.Api.dll cuenta-de-prueba [--correo <i>dirección</i>]</c> crea la cuenta de cliente con la que Culqi revisa la tienda y <b>muestra su contraseña una sola vez por la
/// consola</b> (la salida estándar de este proceso; nunca va a un archivo ni a un registro). Es una operación del despliegue, no un endpoint: se corre en la VM, dentro de la imagen y con el entorno del propio servicio (las mismas variables y el mismo rol de base).
/// <b>Solo existe en <c>Staging</c></b>: en <c>Production</c> y en cualquier otro entorno se niega <b>antes</b> de construir el servicio o tocar la base. No arranca el servidor ni los trabajos de fondo. Termina con 0 si dejó la cuenta y con 1 si no, diciendo qué falta
/// (nunca un secreto ni una traza).
/// </summary>
public static class ModoCuentaDeRevision
{
    public const string Argumento = "cuenta-de-prueba";

    public const string OpcionDelCorreo = "--correo";

    public const string OpcionDeRetirar = "--retirar";

    /// <param name="entorno">El entorno con el que corre; sin él, el de <c>ASPNETCORE_ENVIRONMENT</c> (y <c>Production</c> si no hay ninguno). Existe para las pruebas: ningún argumento de la línea de comandos lo cambia.</param>
    public static async Task<int> Ejecutar(string[] argumentos, TextWriter salida, TextWriter error, string? entorno = null, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(argumentos);
        ArgumentNullException.ThrowIfNull(salida);
        ArgumentNullException.ThrowIfNull(error);

        string? correo = null;
        var retirar = false;
        for (var i = 0; i < argumentos.Length; i++)
        {
            if (argumentos[i] == OpcionDelCorreo && i + 1 < argumentos.Length)
            {
                correo = argumentos[++i];
            }
            else if (argumentos[i] == OpcionDeRetirar)
            {
                retirar = true;
            }
            else
            {
                await error.WriteLineAsync($"cuenta-de-prueba: opción desconocida. Uso: cuenta-de-prueba [{OpcionDelCorreo} <dirección>] [{OpcionDeRetirar}]").ConfigureAwait(false);
                return 1;
            }
        }

        var builder = WebApplication.CreateBuilder(entorno is null ? new WebApplicationOptions() : new WebApplicationOptions { EnvironmentName = entorno });
        var nombreDelEntorno = builder.Environment.EnvironmentName;
        // Fuera de Staging no se crea nada (retirar la cuenta también se permite en Production, para quitarla antes de operar): ni la configuración del servicio ni la base se tocan antes de comprobarlo.
        var esStaging = string.Equals(nombreDelEntorno, CuentaDeRevisionDeLaPasarela.EntornoPermitido, StringComparison.OrdinalIgnoreCase);
        if (!esStaging && !(retirar && string.Equals(nombreDelEntorno, "Production", StringComparison.OrdinalIgnoreCase)))
        {
            await error.WriteLineAsync($"cuenta-de-prueba: solo existe en {CuentaDeRevisionDeLaPasarela.EntornoPermitido} (este entorno es «{nombreDelEntorno}»); {(retirar ? "retirarla también se permite en Production" : "no se hizo nada")}.").ConfigureAwait(false);
            return 1;
        }

        try
        {
            builder.Configuration.AddEnvironmentVariables(prefix: "BABELHUB__");
            builder.Services.AddControllersWithViews();
            builder.Services.AddSingleton(typeof(ModoDeArranque), ModoDeArranque.Normal);
            builder.AgregarEcommerce(ModoDeArranque.Normal);
            await using var app = builder.Build();
            await using var alcance = app.Services.CreateAsyncScope();
            // Las operaciones de fondo corren con el actor «sistema»: así queda en la auditoría (nunca una persona).
            alcance.ServiceProvider.GetRequiredService<IEstablecedorDeActor>().Establecer(ActorActual.DelSistema());
            var operacion = alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>();
            if (retirar)
            {
                var retirada = await operacion.Retirar(nombreDelEntorno, correo, cancelacion).ConfigureAwait(false);
                await salida.WriteLineAsync(retirada ? "cuenta-de-prueba: la cuenta se desactivó y sus sesiones se cerraron." : "cuenta-de-prueba: no había una cuenta activa que retirar.").ConfigureAwait(false);
                return 0;
            }

            var cuenta = await operacion.Crear(nombreDelEntorno, correo, cancelacion).ConfigureAwait(false);

            await salida.WriteLineAsync(cuenta.ContrasenaRotada ? "cuenta-de-prueba: la cuenta ya existía; se cambió su contraseña y se cerraron sus sesiones." : "cuenta-de-prueba: cuenta creada (correo verificado, textos vigentes aceptados).").ConfigureAwait(false);
            await salida.WriteLineAsync($"  correo:     {cuenta.Correo}").ConfigureAwait(false);
            await salida.WriteLineAsync($"  contraseña: {cuenta.Contrasena.Revelar()}").ConfigureAwait(false);
            await salida.WriteLineAsync("  Anótala ahora (en tu gestor de contraseñas): se muestra una sola vez y no queda en ningún archivo ni registro. Para obtener otra, repite el comando.").ConfigureAwait(false);
            return 0;
        }
        catch (CuentaDeRevisionNoPermitidaException e)
        {
            await error.WriteLineAsync("cuenta-de-prueba: " + e.Message).ConfigureAwait(false);
            return 1;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // La configuración incompleta nombra la variable (nunca su valor); otra falla (la red, la base) solo muestra el tipo: el mensaje puede traer nombres de objetos.
            var detalle = e is ConfiguracionDeSecretosInvalidaException cfg ? ": " + string.Join("; ", cfg.Problemas) : string.Empty;
            await error.WriteLineAsync("cuenta-de-prueba: no se pudo completar (" + e.GetType().Name + ")" + detalle).ConfigureAwait(false);
            return 1;
        }
    }
}
