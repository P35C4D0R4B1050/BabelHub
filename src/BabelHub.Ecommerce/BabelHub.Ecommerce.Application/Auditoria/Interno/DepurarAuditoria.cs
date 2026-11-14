using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Auditoria.Interno;

/// <summary>Qué identifica un identificador opaco de <c>auditoria.por_anonimizar</c> (plan §3).</summary>
public enum TipoDePorAnonimizar
{
    CuentaCancelada,
    BajaDeTitular,
}

/// <summary>
/// De dónde salen los identificadores opacos que llenan <c>auditoria.por_anonimizar</c>: en el E-commerce, las cuentas canceladas aún no anonimizadas
/// (<c>ICuentasCanceladasPendientes</c>, que implementa Identidad); en el Inventario, las bajas de titular recibidas (<c>IBajasRecibidas</c>, que implementa Reservas).
/// Tras restaurar un respaldo se vuelve a poblar desde aquí (AUD-31).
/// </summary>
public interface IFuenteDeIdentificadoresPorAnonimizar
{
    TipoDePorAnonimizar Tipo { get; }

    /// <summary>Hasta <paramref name="maximo"/> identificadores pendientes, para consumirlos por lotes sin bloqueos largos.</summary>
    Task<IReadOnlyList<Guid>> Pendientes(int maximo, CancellationToken cancelacion = default);

    /// <summary>Los marca como entregados a Auditoría (en el Inventario arranca la retención de 30 días de la baja); en el E-commerce no hace nada.</summary>
    Task MarcarEntregados(IReadOnlyCollection<Guid> identificadores, CancellationToken cancelacion = default);
}

/// <summary>
/// <c>auditoria.por_anonimizar</c> (T24 y T25): la aplicación solo <b>inserta</b> identificadores opacos (con validación de tipo y formato en la base y cada inserción auditada
/// por el trabajo); la función de depuración los lee. Repetir un identificador no hace nada (<c>ON CONFLICT DO NOTHING</c>). Devuelve cuántos eran nuevos.
/// </summary>
public interface IAlmacenDePorAnonimizar
{
    Task<int> Agregar(TipoDePorAnonimizar tipo, IReadOnlyCollection<Guid> identificadores, DateTimeOffset registradoEn, CancellationToken cancelacion = default);
}

/// <summary>
/// Ejecuta la función <c>auditoria.anonimizar_registros()</c> (<c>SECURITY DEFINER</c>, sin parámetros, con su propietario propio; T24) con el rol de depuración, que solo
/// tiene <c>EXECUTE</c>, y devuelve cuántos registros anonimizó. La aplicación no puede escribir la función ni imitar a su propietario (AUD-03).
/// </summary>
public interface IAnonimizadorDeRegistros
{
    Task<int> Anonimizar(CancellationToken cancelacion = default);
}

/// <summary>Una depuración adicional del servicio que corre en la misma corrida (en el E-commerce, la de <c>identidad.registros</c> por <c>IDepuradorDeRegistrosAislados</c>).</summary>
public interface IDepuradorAdicional
{
    /// <summary>Cuántos registros anonimizó.</summary>
    Task<int> Depurar(CancellationToken cancelacion = default);
}

/// <summary>Cómo va la depuración: la última ejecución exitosa, el primer fallo desde entonces (la alerta cuenta 24 horas desde ahí), los intentos fallidos y el próximo intento.</summary>
public sealed record EstadoDeLaDepuracion(DateTimeOffset? UltimaExitosa, DateTimeOffset? PrimerFalloSinExito, int IntentosFallidos, DateTimeOffset? ProximoIntento, int AnonimizadosSinRegistrar = 0)
{
    public static readonly EstadoDeLaDepuracion Inicial = new(null, null, 0, null);
}

/// <summary>El estado persistente de la depuración (<c>plataforma.trabajos_de_fondo</c> u otra tabla del módulo, T27): sobrevive a un reinicio.</summary>
public interface IEstadoDeLaDepuracion
{
    Task<EstadoDeLaDepuracion> Obtener(CancellationToken cancelacion = default);

    Task Guardar(EstadoDeLaDepuracion estado, CancellationToken cancelacion = default);
}

/// <summary>El bloqueo asesor de la depuración (<c>pg_try_advisory_lock</c>): dos corridas simultáneas no se pisan. Devuelve <see langword="null"/> si otra lo tiene; se libera al disponerlo.</summary>
public interface IBloqueoDeLaDepuracion
{
    Task<IAsyncDisposable?> Intentar(CancellationToken cancelacion = default);
}

public enum EstadoDeLaCorridaDeDepuracion
{
    Ejecutada,

    /// <summary>Un intento anterior falló y todavía no le toca reintentar (esperas crecientes, AUD-31).</summary>
    EnEspera,

    /// <summary>Otra corrida tiene el bloqueo: esta no hace nada.</summary>
    OtraCorridaEnCurso,

    Fallida,
}

/// <summary>Lo que hizo una corrida (cantidades y estado; nunca un mensaje de excepción).</summary>
public sealed record ResultadoDeDepuracion(EstadoDeLaCorridaDeDepuracion Estado, int RegistrosAnonimizados, int IdentificadoresNuevos, bool AlertaPublicada);

/// <summary>
/// La depuración diaria de la auditoría (AUD-11, AUD-19, AUD-31, AUD-36): llena <c>por_anonimizar</c> desde los puertos del servicio, anonimiza los registros de más de dos
/// años y los de cuentas canceladas o bajas de titular con la función de la base de datos (y con la de Identidad, en el E-commerce) y <b>registra la ejecución con el actor
/// «sistema» aunque sea cero</b>. La función corre con el rol de depuración, su propia conexión y su propia transacción (la aplicación no es miembro de ese rol): si no se puede registrar la ejecución, el número anonimizado queda pendiente en el estado y la próxima corrida lo registra (no se pierde ni se registra un cero que no es el suyo); la corrida vuelve a
/// intentarlo. Con un bloqueo asesor; un fallo reintenta con esperas crecientes (1, 2, 4… minutos hasta una hora)
/// y, si pasan 24 horas desde el primer fallo sin una ejecución exitosa, publica la alerta técnica (AUD-31). Repetir el mismo día no cambia nada y registra cero. Corre
/// como «sistema»: sin ese actor explícito se niega (un trabajo de fondo sin actor es un defecto, no un permiso).
/// </summary>
public sealed class DepurarAuditoria(
    IReadOnlyList<IFuenteDeIdentificadoresPorAnonimizar> fuentes,
    IAlmacenDePorAnonimizar porAnonimizar,
    IAnonimizadorDeRegistros anonimizador,
    IReadOnlyList<IDepuradorAdicional> adicionales,
    IAuditoria auditoria,
    IEstadoDeLaDepuracion estados,
    IBloqueoDeLaDepuracion bloqueo,
    IAlertas alertas,
    IActorActual actores,
    IUnidadDeTrabajo unidad,
    IReloj reloj)
{
    /// <summary>El máximo de identificadores que se copian por corrida y fuente (por lotes, sin bloqueos largos ni picos de WAL); la corrida siguiente continúa.</summary>
    public const int MaximoDeIdentificadoresPorCorrida = 1000;

    public static readonly TimeSpan EsperaMaxima = TimeSpan.FromHours(1);

    public static readonly TimeSpan SinExitoHastaLaAlerta = TimeSpan.FromHours(24);

    /// <summary>La corrida diaria: respeta la espera de un fallo anterior.</summary>
    public Task<ResultadoDeDepuracion> Ejecutar(CancellationToken cancelacion = default) => Correr(ignorarEspera: false, cancelacion);

    /// <summary>La de la restauración (antes de abrir al público): no espera, porque el estado restaurado puede ser de un instante anterior.</summary>
    public Task<ResultadoDeDepuracion> EjecutarAntesDeAbrir(CancellationToken cancelacion = default) => Correr(ignorarEspera: true, cancelacion);

    public static TimeSpan EsperaTrasFallar(int intentosFallidos)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(intentosFallidos, 1);
        return TimeSpan.FromMinutes(Math.Min(Math.Pow(2, Math.Min(intentosFallidos - 1, 12)), EsperaMaxima.TotalMinutes));
    }

    private async Task<ResultadoDeDepuracion> Correr(bool ignorarEspera, CancellationToken cancelacion)
    {
        if (actores.Actual is not { Tipo: TipoDeActor.Sistema })
        {
            throw new InvalidOperationException("La depuración corre como «sistema»: el trabajo debe fijar su actor explícito antes de llamarla.");
        }

        var tomado = await bloqueo.Intentar(cancelacion).ConfigureAwait(false);
        if (tomado is null)
        {
            return new ResultadoDeDepuracion(EstadoDeLaCorridaDeDepuracion.OtraCorridaEnCurso, 0, 0, false);
        }

        await using (tomado.ConfigureAwait(false))
        {
            var ahora = reloj.Ahora;
            var estado = await estados.Obtener(cancelacion).ConfigureAwait(false);
            if (!ignorarEspera && estado.ProximoIntento is { } proximo && ahora < proximo)
            {
                return new ResultadoDeDepuracion(EstadoDeLaCorridaDeDepuracion.EnEspera, 0, 0, false);
            }

            // Lo anonimizado en esta corrida que todavía no consta en un registro de ejecución: se acumula a lo que quedó pendiente de una corrida anterior (si no, un registro fallido
            // dejaría una ejecución registrada con un cero que no es el suyo, AUD-36).
            var anonimizadosEnEstaCorrida = 0;
            var nuevos = 0;
            try
            {
                // 1. Llenar `por_anonimizar` y marcar entregadas las bajas: una unidad de trabajo que confirma antes de anonimizar (la función lo lee con otra conexión).
                nuevos = await unidad.Ejecutar(async ct => await LlenarPorAnonimizar(ahora, ct).ConfigureAwait(false), cancelacion).ConfigureAwait(false);

                // 2. Anonimizar con la función de la base (rol de depuración, conexión y transacción propias) y con las depuraciones adicionales del servicio.
                anonimizadosEnEstaCorrida = await anonimizador.Anonimizar(cancelacion).ConfigureAwait(false);
                foreach (var adicional in adicionales)
                {
                    anonimizadosEnEstaCorrida += await adicional.Depurar(cancelacion).ConfigureAwait(false);
                }

                // 3. Registrar la ejecución aunque sea cero (AUD-36), con el actor «sistema» del contexto y solo el número.
                var total = estado.AnonimizadosSinRegistrar + anonimizadosEnEstaCorrida;
                await unidad.Ejecutar(async ct => await RegistrarEjecucion(total, ct).ConfigureAwait(false), cancelacion).ConfigureAwait(false);

                // La unidad ya confirmó: que no se pueda anotar el estado no la convierte en fallo ni la repite (otro registro, otra alerta falsa a las 24 horas).
                try
                {
                    await estados.Guardar(new EstadoDeLaDepuracion(ahora, null, 0, null), cancelacion).ConfigureAwait(false);
                }
                catch (Exception falla) when (falla is not OperationCanceledException)
                {
                    // El próximo estado exitoso lo corrige.
                }

                return new ResultadoDeDepuracion(EstadoDeLaCorridaDeDepuracion.Ejecutada, total, nuevos, false);
            }
            catch (Exception falla) when (falla is not OperationCanceledException)
            {
                return await RegistrarFallo(estado with { AnonimizadosSinRegistrar = estado.AnonimizadosSinRegistrar + anonimizadosEnEstaCorrida }, ahora, cancelacion).ConfigureAwait(false);
            }
        }
    }

    private async Task<int> LlenarPorAnonimizar(DateTimeOffset ahora, CancellationToken cancelacion)
    {
        var nuevos = 0;
        foreach (var fuente in fuentes)
        {
            var pendientes = await fuente.Pendientes(MaximoDeIdentificadoresPorCorrida, cancelacion).ConfigureAwait(false);
            if (pendientes.Count == 0)
            {
                continue;
            }

            nuevos += await porAnonimizar.Agregar(fuente.Tipo, pendientes, ahora, cancelacion).ConfigureAwait(false);
            await fuente.MarcarEntregados(pendientes, cancelacion).ConfigureAwait(false);
        }

        return nuevos;
    }

    private async Task<bool> RegistrarEjecucion(int anonimizados, CancellationToken cancelacion)
    {
        await auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.DepuracionDiariaEjecutada)
            {
                Detalle = new Dictionary<string, string?> { ["registrosAnonimizados"] = anonimizados.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            },
            cancelacion).ConfigureAwait(false);
        return true;
    }

    private async Task<ResultadoDeDepuracion> RegistrarFallo(EstadoDeLaDepuracion previo, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        var primerFallo = previo.PrimerFalloSinExito ?? ahora;
        var intentos = previo.IntentosFallidos + 1;
        var alertar = ahora - primerFallo >= SinExitoHastaLaAlerta;
        try
        {
            await estados.Guardar(new EstadoDeLaDepuracion(previo.UltimaExitosa, primerFallo, intentos, ahora + EsperaTrasFallar(intentos), previo.AnonimizadosSinRegistrar), cancelacion).ConfigureAwait(false);
        }
        catch (Exception falla) when (falla is not OperationCanceledException)
        {
            // Sin dónde anotar el fallo: la próxima corrida lo reintenta de todos modos.
        }

        if (alertar)
        {
            try
            {
                await alertas.PublicarTecnica(new CodigoDeAlerta(MotivosDeAlerta.DepuracionDiariaSinExito), cancelacion).ConfigureAwait(false);
            }
            catch (Exception falla) when (falla is not OperationCanceledException)
            {
                // La alerta nunca interrumpe.
            }
        }

        return new ResultadoDeDepuracion(EstadoDeLaCorridaDeDepuracion.Fallida, 0, 0, alertar);
    }
}

/// <summary>
/// El paso de restauración de Auditoría (plan §5.1, D08, AUD-31): corre <b>después</b> de reaplicar las supresiones (Plataforma) y de reenviar las bajas (Identidad) y antes de
/// abrir al público; vuelve a llenar <c>por_anonimizar</c> desde los puertos y ejecuta la depuración de inmediato. Si falla, el servicio no se abre. No invoca ni duplica la
/// reaplicación, que es de <c>Plataforma</c>. Corre como «sistema» (el arranque en restauración fija ese actor).
/// </summary>
public sealed class PasoDeRestauracionDeDepuracion(DepurarAuditoria depurar) : IPasoDeRestauracion
{
    public const string NombreDelPaso = "auditoria-depurar";

    public int Orden => OrdenDePasoDeRestauracion.Auditoria;

    public string Nombre => NombreDelPaso;

    public async Task<ResultadoDePaso> Ejecutar(CancellationToken cancelacion = default)
    {
        var resultado = await depurar.EjecutarAntesDeAbrir(cancelacion).ConfigureAwait(false);
        return resultado.Estado == EstadoDeLaCorridaDeDepuracion.Ejecutada ? ResultadoDePaso.Correcto : ResultadoDePaso.Fallido;
    }
}
