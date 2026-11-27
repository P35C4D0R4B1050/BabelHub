using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Plataforma;

namespace BabelHub.Ecommerce.Api.Plataforma;

/// <summary>
/// El anfitrión de los modos de consola de operación (<c>cuenta-de-prueba</c>, <c>crear-administrador</c> y <c>restablecer-segundo-factor</c>): comprueba el entorno <b>antes</b> de construir nada, construye el servicio con la configuración de su propio entorno (sin arrancar el servidor ni los
/// trabajos de fondo), fija el actor «sistema» y ejecuta la operación. Nunca imprime un secreto ni una traza: una configuración incompleta nombra la variable; otra falla solo muestra el tipo. Con <c>silenciarRegistros</c>, ningún registro de la composición ni de la operación llega a la consola.
/// </summary>
internal static class AnfitrionDeConsola
{
    public static async Task<int> Ejecutar(string nombreDelModo, string? entorno, Func<string, bool> entornoPermitido, string dondeExiste, TextWriter error, Func<IServiceProvider, string, Task<int>> operacion, bool silenciarRegistros = false)
    {
        var builder = WebApplication.CreateBuilder(entorno is null ? new WebApplicationOptions() : new WebApplicationOptions { EnvironmentName = entorno });
        var nombreDelEntorno = builder.Environment.EnvironmentName;
        if (!entornoPermitido(nombreDelEntorno))
        {
            await error.WriteLineAsync($"{nombreDelModo}: {dondeExiste} (este entorno es «{nombreDelEntorno}»); no se hizo nada.").ConfigureAwait(false);
            return 1;
        }

        try
        {
            builder.Configuration.AddEnvironmentVariables(prefix: "BABELHUB__");
            if (silenciarRegistros)
            {
                builder.Logging.ClearProviders();
            }

            builder.Services.AddControllersWithViews();
            builder.Services.AddSingleton(typeof(ModoDeArranque), ModoDeArranque.Normal);
            builder.AgregarEcommerce(ModoDeArranque.Normal);
            await using var app = builder.Build();
            await using var alcance = app.Services.CreateAsyncScope();
            // Las operaciones de consola corren con el actor «sistema»: así queda en la auditoría (nunca una persona).
            alcance.ServiceProvider.GetRequiredService<IEstablecedorDeActor>().Establecer(ActorActual.DelSistema());
            return await operacion(alcance.ServiceProvider, nombreDelEntorno).ConfigureAwait(false);
        }
        catch (CuentaDeRevisionNoPermitidaException e)
        {
            await error.WriteLineAsync($"{nombreDelModo}: " + e.Message).ConfigureAwait(false);
            return 1;
        }
        catch (Exception e)
        {
            // También una cancelación por el plazo de una dependencia: nadie cancela una orden de consola con un testigo, y su traza no debe salir. La configuración incompleta nombra la variable (nunca su valor); otra falla (la red, la base) solo muestra el tipo: el mensaje puede traer nombres de objetos.
            var detalle = e is ConfiguracionDeSecretosInvalidaException cfg ? ": " + string.Join("; ", cfg.Problemas) : string.Empty;
            await error.WriteLineAsync($"{nombreDelModo}: no se pudo completar (" + e.GetType().Name + ")" + detalle).ConfigureAwait(false);
            return 1;
        }
    }
}

/// <summary>
/// El modo <c>cuenta-de-prueba</c> del servicio (PAG-35, PAG-40, PAG-43, PAG-45): crea las cuentas de cliente de prueba y <b>muestra cada contraseña una sola vez por la consola</b> (nunca a un archivo ni a un registro). Es una operación del despliegue, no un
/// endpoint: se corre en la VM, dentro de la imagen y con el entorno del propio servicio.
/// <list type="bullet">
/// <item><c>cuenta-de-prueba [--correo dirección]</c>: la cuenta de la revisión de la pasarela (<c>revision@example.test</c>), solo en <c>Staging</c>.</item>
/// <item><c>cuenta-de-prueba --cantidad N</c> (1 a 20): N cuentas para las pruebas con personas (<c>persona-01@example.test</c>…), solo en <c>Staging</c>.</item>
/// <item><c>cuenta-de-prueba --retirar [--correo dirección | --todas]</c>: desactiva esa cuenta (o todas las de prueba) y cierra sus sesiones; en <c>Staging</c> y <c>Production</c>.</item>
/// <item><c>cuenta-de-prueba --purgar</c>: cancela todas las cuentas de prueba por la cancelación de cuenta (cada módulo desvincula o anonimiza lo suyo por su contrato); en <c>Staging</c> y <c>Production</c>.</item>
/// </list>
/// Fuera de esos entornos se niega <b>antes</b> de construir el servicio o tocar la base. Termina con 0 si hizo lo pedido y con 1 si no, diciendo qué falta.
/// </summary>
public static class ModoCuentaDeRevision
{
    public const string Argumento = "cuenta-de-prueba";

    public const string OpcionDelCorreo = "--correo";

    public const string OpcionDeRetirar = "--retirar";

    public const string OpcionDeCantidad = "--cantidad";

    public const string OpcionDeTodas = "--todas";

    public const string OpcionDePurgar = "--purgar";

    /// <param name="entorno">El entorno con el que corre; sin él, el de <c>ASPNETCORE_ENVIRONMENT</c> (y <c>Production</c> si no hay ninguno). Existe para las pruebas: ningún argumento de la línea de comandos lo cambia.</param>
    public static async Task<int> Ejecutar(string[] argumentos, TextWriter salida, TextWriter error, string? entorno = null, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(argumentos);
        ArgumentNullException.ThrowIfNull(salida);
        ArgumentNullException.ThrowIfNull(error);

        string? correo = null;
        int? cantidad = null;
        bool retirar = false, todas = false, purgar = false;
        for (var i = 0; i < argumentos.Length; i++)
        {
            if (argumentos[i] == OpcionDelCorreo && i + 1 < argumentos.Length)
            {
                correo = argumentos[++i];
            }
            else if (argumentos[i] == OpcionDeCantidad && i + 1 < argumentos.Length && int.TryParse(argumentos[i + 1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n))
            {
                cantidad = n;
                i++;
            }
            else if (argumentos[i] == OpcionDeRetirar)
            {
                retirar = true;
            }
            else if (argumentos[i] == OpcionDeTodas)
            {
                todas = true;
            }
            else if (argumentos[i] == OpcionDePurgar)
            {
                purgar = true;
            }
            else
            {
                await error.WriteLineAsync($"cuenta-de-prueba: opción desconocida o sin valor. Uso: cuenta-de-prueba [{OpcionDelCorreo} <dirección> | {OpcionDeCantidad} <1 a 20>] | {OpcionDeRetirar} [{OpcionDelCorreo} <dirección> | {OpcionDeTodas}] | {OpcionDePurgar}").ConfigureAwait(false);
                return 1;
            }
        }

        if ((retirar ? 1 : 0) + (purgar ? 1 : 0) + (cantidad is not null ? 1 : 0) > 1 || (todas && !retirar) || (cantidad is not null && correo is not null) || (purgar && correo is not null))
        {
            await error.WriteLineAsync("cuenta-de-prueba: combinación de opciones no válida; no se hizo nada.").ConfigureAwait(false);
            return 1;
        }

        var soloStaging = !retirar && !purgar;
        return await AnfitrionDeConsola.Ejecutar(
            Argumento,
            entorno,
            e => string.Equals(e, CuentaDeRevisionDeLaPasarela.EntornoPermitido, StringComparison.OrdinalIgnoreCase) || (!soloStaging && string.Equals(e, "Production", StringComparison.OrdinalIgnoreCase)),
            soloStaging ? $"crear cuentas de prueba solo existe en {CuentaDeRevisionDeLaPasarela.EntornoPermitido}" : $"solo existe en {CuentaDeRevisionDeLaPasarela.EntornoPermitido} y Production",
            error,
            async (servicios, nombreDelEntorno) =>
            {
                var operacion = servicios.GetRequiredService<CuentaDeRevisionDeLaPasarela>();
                if (purgar)
                {
                    var n = await operacion.Purgar(nombreDelEntorno, cancelacion).ConfigureAwait(false);
                    await salida.WriteLineAsync($"cuenta-de-prueba: {n} cuenta(s) de prueba canceladas; cada módulo desvinculó o anonimizó lo suyo por su contrato y la baja de las reservas la entrega el trabajo de bajas. No se tocó el catálogo ni ningún usuario real.").ConfigureAwait(false);
                    return 0;
                }

                if (retirar)
                {
                    if (todas)
                    {
                        var n = await operacion.RetirarTodas(nombreDelEntorno, cancelacion).ConfigureAwait(false);
                        await salida.WriteLineAsync($"cuenta-de-prueba: {n} cuenta(s) de prueba desactivadas y sus sesiones cerradas.").ConfigureAwait(false);
                        return 0;
                    }

                    var retirada = await operacion.Retirar(nombreDelEntorno, correo, cancelacion).ConfigureAwait(false);
                    await salida.WriteLineAsync(retirada ? "cuenta-de-prueba: la cuenta se desactivó y sus sesiones se cerraron." : "cuenta-de-prueba: no había una cuenta activa que retirar.").ConfigureAwait(false);
                    return 0;
                }

                async Task Mostrar(CuentaDeRevisionCreada cuenta)
                {
                    await salida.WriteLineAsync(cuenta.ContrasenaRotada ? "cuenta-de-prueba: la cuenta ya existía; se cambió su contraseña y se cerraron sus sesiones." : "cuenta-de-prueba: cuenta creada (correo verificado, textos vigentes aceptados).").ConfigureAwait(false);
                    await salida.WriteLineAsync($"  correo:     {cuenta.Correo}").ConfigureAwait(false);
                    await salida.WriteLineAsync($"  contraseña: {cuenta.Contrasena.Revelar()}").ConfigureAwait(false);
                }

                if (cantidad is { } c)
                {
                    await operacion.CrearVarias(nombreDelEntorno, c, Mostrar, cancelacion).ConfigureAwait(false);
                }
                else
                {
                    await Mostrar(await operacion.Crear(nombreDelEntorno, correo, cancelacion).ConfigureAwait(false)).ConfigureAwait(false);
                }

                await salida.WriteLineAsync("  Anótalas ahora (en tu gestor de contraseñas) y repártelas por un canal seguro: se muestran una sola vez y no quedan en ningún archivo ni registro. Para obtener otra, repite el comando.").ConfigureAwait(false);
                return 0;
            }).ConfigureAwait(false);
    }
}

/// <summary>
/// El modo <c>crear-administrador</c> (PAG-44): siembra el <b>primer administrador central</b> con una contraseña temporal aleatoria mostrada una sola vez (se cambia, y se configura el segundo factor TOTP, en el primer ingreso). <c>crear-administrador --correo
/// dirección [--nombre texto]</c>, en <c>Staging</c> y <c>Production</c>. Si ya existe un administrador central activo no crea otro; repetirlo no duplica nada.
/// </summary>
public static class ModoCrearAdministrador
{
    public const string Argumento = "crear-administrador";

    public static async Task<int> Ejecutar(string[] argumentos, TextWriter salida, TextWriter error, string? entorno = null, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(argumentos);
        ArgumentNullException.ThrowIfNull(salida);
        ArgumentNullException.ThrowIfNull(error);

        string? correo = null, nombre = null;
        for (var i = 0; i < argumentos.Length; i++)
        {
            if (argumentos[i] == "--correo" && i + 1 < argumentos.Length)
            {
                correo = argumentos[++i];
            }
            else if (argumentos[i] == "--nombre" && i + 1 < argumentos.Length)
            {
                nombre = argumentos[++i];
            }
            else
            {
                await error.WriteLineAsync("crear-administrador: opción desconocida o sin valor. Uso: crear-administrador --correo <dirección> [--nombre <texto>]").ConfigureAwait(false);
                return 1;
            }
        }

        return await AnfitrionDeConsola.Ejecutar(
            Argumento,
            entorno,
            e => string.Equals(e, "Staging", StringComparison.OrdinalIgnoreCase) || string.Equals(e, "Production", StringComparison.OrdinalIgnoreCase),
            "solo existe en Staging y Production",
            error,
            async (servicios, nombreDelEntorno) =>
            {
                var r = await servicios.GetRequiredService<PrimerAdministradorDeLaInstalacion>().Crear(nombreDelEntorno, correo, nombre, cancelacion).ConfigureAwait(false);
                if (r.Resultado == ResultadoDelPrimerAdministrador.YaExiste)
                {
                    await salida.WriteLineAsync("crear-administrador: ese administrador ya existe y ya completó su primer ingreso; no se cambió nada.").ConfigureAwait(false);
                    return 0;
                }

                await salida.WriteLineAsync(r.Resultado == ResultadoDelPrimerAdministrador.Creado ? "crear-administrador: administrador central creado (correo verificado)." : "crear-administrador: el administrador aún no completó su primer ingreso; se generó otra contraseña temporal.").ConfigureAwait(false);
                await salida.WriteLineAsync($"  correo:                {r.Correo}").ConfigureAwait(false);
                await salida.WriteLineAsync($"  contraseña temporal:   {r.ContrasenaTemporal!.Revelar()}").ConfigureAwait(false);
                await salida.WriteLineAsync("  Anótala ahora: se muestra una sola vez y no queda en ningún archivo ni registro. Vale 24 horas: en el primer ingreso (gestion.<dominio>) se cambia la contraseña y se configura el segundo factor (TOTP).").ConfigureAwait(false);
                return 0;
            }).ConfigureAwait(false);
    }
}
