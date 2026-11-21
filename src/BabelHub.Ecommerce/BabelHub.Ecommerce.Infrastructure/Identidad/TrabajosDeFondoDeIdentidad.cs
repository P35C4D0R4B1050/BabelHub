using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.Ecommerce.Infrastructure.Identidad;

/// <summary>La purga de lo vencido (T37) en PostgreSQL: cada sentencia es un <c>DELETE</c> acotado por el límite, bajo <c>rol_identidad</c>.</summary>
public sealed class PostgresPurgaDeVencidos(AccesoAlEsquemaDeIdentidad acceso) : IPurgaDeVencidos
{
    public Task<int> DesafiosVencidos(DateTimeOffset limite, CancellationToken cancelacion = default) =>
        Borrar("DELETE FROM identidad.desafios_de_ingreso WHERE vence_en < @limite", limite, cancelacion);

    public Task<int> CodigosDeUnSoloUsoVencidos(DateTimeOffset limite, CancellationToken cancelacion = default) =>
        Borrar("DELETE FROM identidad.codigos_de_un_solo_uso WHERE vence_en < @limite", limite, cancelacion);

    public Task<int> RenovacionesVencidas(DateTimeOffset limite, CancellationToken cancelacion = default) =>
        Borrar("DELETE FROM identidad.renovaciones WHERE expira_absoluta < @limite OR revocada_en < @limite", limite, cancelacion);

    public Task<int> SolicitudesDeCodigo(DateTimeOffset limite, CancellationToken cancelacion = default) =>
        Borrar("DELETE FROM identidad.solicitudes_de_codigo WHERE instante < @limite", limite, cancelacion);

    public Task<int> CambiosDeCorreoVencidos(DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        Borrar("DELETE FROM identidad.cambios_de_correo_pendientes WHERE vence_en < @limite", ahora, cancelacion);

    public Task<IReadOnlyList<Guid>> CuentasConCambioNoReconocidoVencido(DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<Guid>>(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, "SELECT usuario_id FROM identidad.cambios_de_correo_pendientes WHERE vence_en < @limite AND estado = 'pendiente-de-confirmacion'", ("limite", ahora));
                await using var filas = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                var cuentas = new List<Guid>();
                while (await filas.ReadAsync(ct).ConfigureAwait(false))
                {
                    cuentas.Add(filas.GetGuid(0));
                }

                return cuentas;
            },
            cancelacion);

    private Task<int> Borrar(string sql, DateTimeOffset limite, CancellationToken cancelacion) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, sql, ("limite", limite));
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            cancelacion);
}

/// <summary>
/// Corre una corrida de un trabajo como el actor «sistema»: lo aporta el anfitrión (la Api), que es de los pocos lugares que fijan el actor (regla de arquitectura): fija «sistema», ejecuta la corrida con su cancelación
/// y limpia el actor al terminar. Un trabajo de fondo sin actor es un defecto, no un permiso.
/// </summary>
public delegate Task AlcanceDeSistemaDeIdentidad(Func<CancellationToken, Task> corrida, CancellationToken cancelacion);

/// <summary>Cuándo corre un trabajo de fondo de Identidad y cuántos fallos seguidos hacen falta para avisar.</summary>
public sealed record OpcionesDelTrabajoDeIdentidad(TimeSpan Intervalo, int FallosSeguidosParaAlertar = 3, TimeSpan? PlazoMaximoDeLaCorrida = null)
{
    /// <summary>Una corrida colgada (red o base de datos) se cancela y cuenta como fallo: sin un plazo nunca llegaría a alertar.</summary>
    public TimeSpan PlazoDeLaCorrida => PlazoMaximoDeLaCorrida ?? TimeSpan.FromMinutes(10);
}

/// <summary>
/// Un trabajo de fondo de Identidad (T37 a T40): corre su corrida cada intervalo <b>como «sistema» fijado explícitamente</b> (un trabajo de fondo sin actor es un defecto, no un permiso), registra el resultado enumerado de
/// cada corrida en <c>plataforma.trabajos_de_fondo</c> y, tras varios fallos seguidos, publica una alerta técnica (<c>trabajo-de-fondo-detenido</c>). Nunca deja caer el servicio: solo se propaga la cancelación del apagado.
/// </summary>
public sealed class TrabajoDeFondoDeIdentidad(
    string nombre,
    Func<CancellationToken, Task> corrida,
    IRegistroDeTrabajosDeFondo registro,
    AlcanceDeSistemaDeIdentidad alcanceDeSistema,
    IAlertas alertas,
    IReloj reloj,
    OpcionesDelTrabajoDeIdentidad opciones,
    TimeProvider? tiempo = null)
{
    /// <summary>El motivo del catálogo cerrado de alertas técnicas para un trabajo de fondo que no corre.</summary>
    public const string MotivoDelTrabajoDetenido = "trabajo-de-fondo-detenido";

    private int _fallosSeguidos;

    public string Nombre { get; } = nombre;

    public async Task EjecutarUnaVez(CancellationToken cancelacion = default)
    {
        try
        {
            using var plazo = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
            plazo.CancelAfter(opciones.PlazoDeLaCorrida);
            await alcanceDeSistema(corrida, plazo.Token).ConfigureAwait(false);
            _fallosSeguidos = 0;
            await Registrar(ResultadoDeTrabajo.Exito, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            _fallosSeguidos++;
            await Registrar(ResultadoDeTrabajo.Fallo, cancelacion).ConfigureAwait(false);
            if (_fallosSeguidos >= opciones.FallosSeguidosParaAlertar && (_fallosSeguidos - opciones.FallosSeguidosParaAlertar) % opciones.FallosSeguidosParaAlertar == 0)
            {
                await Alertar(cancelacion).ConfigureAwait(false);
            }
        }
    }

    public async Task Correr(CancellationToken cancelacion)
    {
        using var temporizador = new PeriodicTimer(opciones.Intervalo, tiempo ?? TimeProvider.System);
        try
        {
            do
            {
                await EjecutarUnaVez(cancelacion).ConfigureAwait(false);
            }
            while (await temporizador.WaitForNextTickAsync(cancelacion).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Apagado ordenado del servicio.
        }
    }

    private async Task Registrar(ResultadoDeTrabajo resultado, CancellationToken cancelacion)
    {
        try
        {
            await registro.Registrar(Nombre, resultado, reloj.Ahora, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // El registro de la corrida es informativo: si falla, el trabajo sigue.
        }
    }

    private async Task Alertar(CancellationToken cancelacion)
    {
        try
        {
            await alertas.PublicarTecnica(new CodigoDeAlerta(MotivoDelTrabajoDetenido), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Si ni siquiera se puede alertar, el trabajo sigue intentando en la próxima corrida.
        }
    }
}

/// <summary>
/// Los trabajos de fondo de Identidad con sus nombres estables, intervalos y corridas (T37 a T40): cada corrida abre su propio alcance de servicios (los casos de uso dependen de la unidad de trabajo y del actor,
/// que son por alcance).
/// </summary>
public static class TrabajosDeIdentidad
{
    public const string PurgaDeVencidos = "identidad-purga-de-vencidos";

    public const string DescarteDeCancelaciones = "identidad-descarte-de-cancelaciones";

    public const string RotacionDeLaClaveDeFirma = "identidad-rotacion-de-la-clave";

    public const string RenovacionDeLasClavesDeVerificacion = "identidad-renovacion-de-claves-de-verificacion";

    public const string EntregaDeBajas = "identidad-entrega-de-bajas";

    /// <summary>Cada 10 minutos: la fila de la solicitud de código se purga a los 10 minutos y el resto no urge.</summary>
    public static readonly OpcionesDelTrabajoDeIdentidad IntervaloDeLaPurga = new(TimeSpan.FromMinutes(10));

    /// <summary>Cada hora: los plazos son de 7 y 30 días (IDE-68, IDE-81).</summary>
    public static readonly OpcionesDelTrabajoDeIdentidad IntervaloDelDescarte = new(TimeSpan.FromHours(1));

    /// <summary>Cada 6 horas se decide si toca: la rotación es a los 90 días (IDE-36).</summary>
    public static readonly OpcionesDelTrabajoDeIdentidad IntervaloDeLaRotacion = new(TimeSpan.FromHours(6), FallosSeguidosParaAlertar: 1);

    /// <summary>Cada minuto: la caché de claves y revocaciones debe renovarse al menos cada 5 minutos (IDE-61) y su foto vale 5.</summary>
    public static readonly OpcionesDelTrabajoDeIdentidad IntervaloDeLaRenovacionDeClaves = new(TimeSpan.FromMinutes(1), FallosSeguidosParaAlertar: 3, PlazoMaximoDeLaCorrida: TimeSpan.FromMinutes(2));

    /// <summary>Cada 15 minutos (T40).</summary>
    public static readonly OpcionesDelTrabajoDeIdentidad IntervaloDeLasBajas = new(TimeSpan.FromMinutes(15));

    public static TrabajoDeFondoDeIdentidad Purga(IServiceScopeFactory fabrica, IRegistroDeTrabajosDeFondo registro, AlcanceDeSistemaDeIdentidad alcance, IAlertas alertas, IReloj reloj, TimeProvider? tiempo = null) =>
        new(
            PurgaDeVencidos,
            async ct =>
            {
                using var alcance = fabrica.CreateScope();
                await alcance.ServiceProvider.GetRequiredService<PurgarVencidosDeIdentidad>().Ejecutar(ct).ConfigureAwait(false);
            },
            registro,
            alcance,
            alertas,
            reloj,
            IntervaloDeLaPurga,
            tiempo);

    /// <summary>El descarte de cancelaciones: la reconfirmación a los 7 días y la solicitud a los 30 (la revisión periódica de T23 y T24).</summary>
    public static TrabajoDeFondoDeIdentidad Descarte(IServiceScopeFactory fabrica, IRegistroDeTrabajosDeFondo registro, AlcanceDeSistemaDeIdentidad alcance, IAlertas alertas, IReloj reloj, TimeProvider? tiempo = null) =>
        new(
            DescarteDeCancelaciones,
            async ct =>
            {
                using var alcance = fabrica.CreateScope();
                await alcance.ServiceProvider.GetRequiredService<RevisionDeLasCancelaciones>().Revisar(ct).ConfigureAwait(false);
            },
            registro,
            alcance,
            alertas,
            reloj,
            IntervaloDelDescarte,
            tiempo);

    /// <summary>La rotación a los 90 días: una sola clave activa, el juego se carga con bloqueo y la rotación audita en su transacción.</summary>
    public static TrabajoDeFondoDeIdentidad Rotacion(IServiceScopeFactory fabrica, IRegistroDeTrabajosDeFondo registro, AlcanceDeSistemaDeIdentidad alcance, IAlertas alertas, IReloj reloj, TimeProvider? tiempo = null) =>
        new(
            RotacionDeLaClaveDeFirma,
            async ct =>
            {
                using var alcance = fabrica.CreateScope();
                await alcance.ServiceProvider.GetRequiredService<ClavesDeFirma>().RotarSiHanPasado90Dias(ct).ConfigureAwait(false);
            },
            registro,
            alcance,
            alertas,
            reloj,
            IntervaloDeLaRotacion,
            tiempo);

    /// <summary>
    /// La renovación de la caché de claves de verificación y revocaciones (IDE-61): si la renovación falla, el trabajo falla (se registra y, tras varios fallos, alerta) y, pasados 5 minutos, el verificador rechaza
    /// toda credencial.
    /// </summary>
    public static TrabajoDeFondoDeIdentidad RenovacionDeClaves(CacheDeClavesDeVerificacion cache, IRegistroDeTrabajosDeFondo registro, AlcanceDeSistemaDeIdentidad alcance, IAlertas alertas, IReloj reloj, TimeProvider? tiempo = null) =>
        new(
            RenovacionDeLasClavesDeVerificacion,
            async ct =>
            {
                if (!await cache.Renovar(ct).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("No se pudo renovar la caché de claves de verificación.");
                }
            },
            registro,
            alcance,
            alertas,
            reloj,
            IntervaloDeLaRenovacionDeClaves,
            tiempo);

    /// <summary>
    /// La entrega de las bajas y el reintento de los cierres pendientes cada 15 minutos (T40; IDE-64, PLA-41): <c>ReintentoDeBajas</c> (con la alerta a las 24 horas) corre con cada operación en su propia transacción corta
    /// y <b>sin retener bloqueos durante las llamadas al Inventario</b>. Dos instancias de Identidad pueden repetir un paso sobre la misma baja, sin efecto: la entrega y el cierre son idempotentes (IDE-64) y guardar no
    /// deshace lo ya hecho. Una transacción única con <c>FOR UPDATE SKIP LOCKED</c> se descartó: mezclaría pasos que se tragan sus errores y un fallo de SQL abortaría toda la pasada sin dejar rastro.
    /// </summary>
    public static TrabajoDeFondoDeIdentidad Bajas(IServiceScopeFactory fabrica, IRegistroDeTrabajosDeFondo registro, AlcanceDeSistemaDeIdentidad alcance, IAlertas alertas, IReloj reloj, TimeProvider? tiempo = null) =>
        new(
            EntregaDeBajas,
            async ct =>
            {
                using var alcanceDeServicios = fabrica.CreateScope();
                await alcanceDeServicios.ServiceProvider.GetRequiredService<ReintentoDeBajas>().Procesar(ct).ConfigureAwait(false);
            },
            registro,
            alcance,
            alertas,
            reloj,
            IntervaloDeLasBajas,
            tiempo);
}

/// <summary>El servicio en segundo plano que corre los trabajos de Identidad en paralelo hasta el apagado.</summary>
public sealed class ServicioDeTrabajosDeIdentidad(IEnumerable<TrabajoDeFondoDeIdentidad> trabajos) : BackgroundService
{
    private readonly IReadOnlyList<TrabajoDeFondoDeIdentidad> _trabajos = [.. trabajos];

    /// <summary>Los nombres de los trabajos que corre este servicio (para comprobar la composición).</summary>
    public IReadOnlyList<string> Nombres => [.. _trabajos.Select(t => t.Nombre)];

    protected override Task ExecuteAsync(CancellationToken cancelacion) =>
        Task.WhenAll(_trabajos.Select(trabajo => trabajo.Correr(cancelacion)));
}
