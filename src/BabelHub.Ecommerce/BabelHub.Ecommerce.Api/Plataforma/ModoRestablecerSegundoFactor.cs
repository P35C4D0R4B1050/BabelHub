using BabelHub.Ecommerce.Application.Identidad;

namespace BabelHub.Ecommerce.Api.Plataforma;

/// <summary>
/// El modo <c>restablecer-segundo-factor --correo &lt;dirección&gt;</c> (identidad-recuperacion-del-segundo-factor; RSF-02, RSF-13, RSF-14): restablece el segundo factor de la <b>única</b> administración que puede ingresar. Se ejecuta en la VM, dentro de la imagen y con el
/// entorno del propio servicio; solo en <c>Staging</c> y <c>Production</c> y nunca en un ensayo o una restauración. No toca la contraseña, no tiene endpoint y <b>no escribe</b> en la consola ni en los registros el correo, secretos, códigos ni trazas.
/// Termina con 0 solo si restableció.
/// </summary>
public static class ModoRestablecerSegundoFactor
{
    public const string Argumento = "restablecer-segundo-factor";

    private const string Uso = $"{Argumento}: opción desconocida o sin valor. Uso: {Argumento} --correo <dirección>";

    private const string NoSeCompleto = $"{Argumento}: no se completó o no se pudo confirmar. Revisa que la base responda y repite la orden: si la anterior llegó a aplicarse, te dirá que no hay un segundo factor activado.";

    /// <param name="entorno">El entorno con el que corre; sin él, el de <c>ASPNETCORE_ENVIRONMENT</c> (y <c>Production</c> si no hay ninguno). Existe para las pruebas: ningún argumento lo cambia.</param>
    /// <param name="modo">El modo de arranque; sin él, el de <c>BABELHUB__Modo</c>. Existe para las pruebas.</param>
    public static async Task<int> Ejecutar(string[] argumentos, TextWriter salida, TextWriter error, string? entorno = null, string? modo = null, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(argumentos);
        ArgumentNullException.ThrowIfNull(salida);
        ArgumentNullException.ThrowIfNull(error);

        // Solo «--correo <dirección>», una vez: ningún argumento se repite en la salida.
        if (argumentos.Length is not (0 or 2) || (argumentos.Length == 2 && argumentos[0] != "--correo"))
        {
            await error.WriteLineAsync(Uso).ConfigureAwait(false);
            return 1;
        }

        var correo = argumentos.Length == 2 ? argumentos[1] : null;
        // El anfitrión de consola compone siempre en modo normal: el modo real se lee aquí, antes de construir nada, igual que al arrancar (sin distinguir mayúsculas en la clave).
        var modoDeArranque = modo ?? new ConfigurationBuilder().AddEnvironmentVariables(prefix: "BABELHUB__").Build()["Modo"];
        return await AnfitrionDeConsola.Ejecutar(
            Argumento,
            entorno,
            nombre => EntornoDelRestablecimiento.Permite(nombre, modoDeArranque),
            "solo existe en Staging y Production, en el modo normal",
            error,
            (servicios, nombreDelEntorno) => Informar(servicios, nombreDelEntorno, modoDeArranque, correo, salida, error, cancelacion),
            silenciarRegistros: true).ConfigureAwait(false);
    }

    /// <summary>Pide el restablecimiento sobre el servicio ya compuesto y dice qué pasó. Un fallo, sea cual sea, se informa con un mensaje fijo: ni el tipo ni el mensaje de la excepción salen.</summary>
    public static async Task<int> Informar(IServiceProvider servicios, string entorno, string? modo, string? correo, TextWriter salida, TextWriter error, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(salida);
        ArgumentNullException.ThrowIfNull(error);

        RestablecimientoDesdeLaInstalacion restablecimiento;
        try
        {
            restablecimiento = await servicios.GetRequiredService<IRestablecimientoDesdeLaInstalacion>().Restablecer(entorno, modo, correo, cancelacion).ConfigureAwait(false);
        }
        // Solo una cancelación pedida por quien llama sube; la de una dependencia que agota su plazo es una falla más (RSF-14).
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            await error.WriteLineAsync(NoSeCompleto).ConfigureAwait(false);
            return 1;
        }

        if (restablecimiento.Resultado != ResultadoDelRestablecimientoDesdeLaInstalacion.Restablecido)
        {
            await error.WriteLineAsync($"{Argumento}: {Motivo(restablecimiento.Resultado)}; no se cambió nada.").ConfigureAwait(false);
            return 1;
        }

        await salida.WriteLineAsync($"{Argumento}: se restableció el segundo factor y se cerraron las sesiones de la cuenta. La contraseña no cambió.").ConfigureAwait(false);
        await salida.WriteLineAsync(restablecimiento.AvisoEnviado
            ? "  El aviso por correo salió."
            : "  El aviso por correo no salió en este momento (puede haber quedado pendiente o como aviso en la cuenta).").ConfigureAwait(false);
        await salida.WriteLineAsync("  Ingresa ahora en la gestión con tu contraseña y configura el segundo factor de nuevo; guarda los códigos de respaldo. Si crees que alguien conoce la contraseña, cámbiala después.").ConfigureAwait(false);
        return 0;
    }

    private static string Motivo(ResultadoDelRestablecimientoDesdeLaInstalacion resultado) => resultado switch
    {
        ResultadoDelRestablecimientoDesdeLaInstalacion.CorreoInvalido => "falta el correo o no tiene un formato válido (--correo <dirección>)",
        ResultadoDelRestablecimientoDesdeLaInstalacion.NoEsUnaAdministracionActiva => "no hay una cuenta de administración central activa con ese correo",
        ResultadoDelRestablecimientoDesdeLaInstalacion.HayOtraAdministracion => "hay otra administración que puede ingresar: ella restablece el segundo factor desde la gestión (Usuarios y roles)",
        ResultadoDelRestablecimientoDesdeLaInstalacion.SinSegundoFactor => "esa cuenta no tiene un segundo factor activado",
        ResultadoDelRestablecimientoDesdeLaInstalacion.EntornoNoPermitido => "solo existe en Staging y Production, en el modo normal",
        _ => "la operación no está permitida",
    };
}
