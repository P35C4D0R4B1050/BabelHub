using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>
/// La purga de lo vencido de <c>identidad</c> (T37, T38): cada método elimina lo anterior al instante dado y devuelve cuántas filas quitó. Solo borra lo que ya no sirve para nada: un desafío o un código vencido, una
/// renovación expirada o revocada hace tiempo, la fila idéntica de una solicitud de código y un cambio de correo cuyo plazo de 24 horas pasó (con el correo nuevo, que es un dato personal).
/// </summary>
public interface IPurgaDeVencidos
{
    Task<int> DesafiosVencidos(DateTimeOffset limite, CancellationToken cancelacion = default);

    Task<int> CodigosDeUnSoloUsoVencidos(DateTimeOffset limite, CancellationToken cancelacion = default);

    /// <summary>Las renovaciones cuya expiración absoluta o revocación es anterior al límite (no las vigentes ni las recién usadas: sirven para detectar la reutilización).</summary>
    Task<int> RenovacionesVencidas(DateTimeOffset limite, CancellationToken cancelacion = default);

    Task<int> SolicitudesDeCodigo(DateTimeOffset limite, CancellationToken cancelacion = default);

    /// <summary>Los cambios de correo cuyo vencimiento es anterior al instante (IDE-54).</summary>
    Task<int> CambiosDeCorreoVencidos(DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>Las cuentas con un cambio de correo desde un dispositivo o región no reconocidos que sigue esperando la confirmación del titular y cuyo plazo venció (AUD-33): se leen <b>antes</b> de purgar.</summary>
    Task<IReadOnlyList<Guid>> CuentasConCambioNoReconocidoVencido(DateTimeOffset ahora, CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<Guid>>([]);
}

/// <summary>Lo que quitó una pasada de purga.</summary>
public sealed record ResultadoDeLaPurga(int Desafios, int Codigos, int Renovaciones, int SolicitudesDeCodigo, int CambiosDeCorreo)
{
    public int Total => Desafios + Codigos + Renovaciones + SolicitudesDeCodigo + CambiosDeCorreo;
}

/// <summary>
/// Los trabajos de vencimiento y de descarte de las filas temporales (T37, T38; IDE-19, IDE-46, IDE-54; AC-IDE-19): los desafíos y los códigos vencidos, las renovaciones y los códigos a los <b>30 días</b>, la fila de la
/// solicitud de código a los <b>10 minutos</b> y el cambio de correo pendiente a las <b>24 horas</b>. Cada paso va por separado: si uno falla, los demás igual corren y la pasada falla al final (el trabajo registra el
/// fallo y avisa tras varios seguidos). Corre con el actor del sistema.
/// </summary>
public sealed class PurgarVencidosDeIdentidad(IPurgaDeVencidos purga, IReloj reloj, EventosDeSeguridadDeIdentidad? eventos = null)
{
    /// <summary>Un desafío solo vale 5 minutos: una hora después de vencer ya no sirve ni para el registro.</summary>
    public static readonly TimeSpan RetencionDeLosDesafios = TimeSpan.FromHours(1);

    /// <summary>Códigos y renovaciones vencidos o revocados se conservan 30 días (reutilización, auditoría de intentos) y se purgan.</summary>
    public static readonly TimeSpan RetencionDeCodigosYRenovaciones = TimeSpan.FromDays(30);

    /// <summary>La fila de la solicitud de código se purga a los 10 minutos (IDE-65).</summary>
    public static readonly TimeSpan RetencionDeLasSolicitudesDeCodigo = TimeSpan.FromMinutes(10);

    public async Task<ResultadoDeLaPurga> Ejecutar(CancellationToken cancelacion = default)
    {
        var ahora = reloj.Ahora;
        var errores = new List<Exception>();
        var desafios = await Paso(() => purga.DesafiosVencidos(ahora - RetencionDeLosDesafios, cancelacion), errores, cancelacion).ConfigureAwait(false);
        var codigos = await Paso(() => purga.CodigosDeUnSoloUsoVencidos(ahora - RetencionDeCodigosYRenovaciones, cancelacion), errores, cancelacion).ConfigureAwait(false);
        var renovaciones = await Paso(() => purga.RenovacionesVencidas(ahora - RetencionDeCodigosYRenovaciones, cancelacion), errores, cancelacion).ConfigureAwait(false);
        var solicitudes = await Paso(() => purga.SolicitudesDeCodigo(ahora - RetencionDeLasSolicitudesDeCodigo, cancelacion), errores, cancelacion).ConfigureAwait(false);
        // El vencimiento de un cambio no reconocido es un evento de seguridad (AUD-33): se registra antes de borrar la fila. Si no se pudo <b>leer</b> la lista, la fila se conserva para la siguiente pasada; si el registro de seguridad no puede escribir, nunca interrumpe (deja su alerta técnica) y el evento puede perderse; si el borrado falla, la pasada siguiente repite el evento de las mismas cuentas.
        var erroresAntes = errores.Count;
        await Paso(() => RegistrarLosCambiosNoReconocidosVencidos(ahora, cancelacion), errores, cancelacion).ConfigureAwait(false);
        var cambios = errores.Count > erroresAntes ? 0 : await Paso(() => purga.CambiosDeCorreoVencidos(ahora, cancelacion), errores, cancelacion).ConfigureAwait(false);
        if (errores.Count > 0)
        {
            throw new AggregateException("La purga de lo vencido no se completó.", errores);
        }

        return new ResultadoDeLaPurga(desafios, codigos, renovaciones, solicitudes, cambios);
    }

    private async Task<int> RegistrarLosCambiosNoReconocidosVencidos(DateTimeOffset ahora, CancellationToken cancelacion)
    {
        if (eventos is null)
        {
            return 0;
        }

        var cuentas = await purga.CuentasConCambioNoReconocidoVencido(ahora, cancelacion).ConfigureAwait(false);
        foreach (var cuenta in cuentas)
        {
            await eventos.CambioDeCorreoVencido(cuenta, cancelacion).ConfigureAwait(false);
        }

        return cuentas.Count;
    }

    private static async Task<int> Paso(Func<Task<int>> paso, List<Exception> errores, CancellationToken cancelacion)
    {
        try
        {
            return await paso().ConfigureAwait(false);
        }
        catch (Exception e) when (!cancelacion.IsCancellationRequested)
        {
            errores.Add(e);
            return 0;
        }
    }
}
