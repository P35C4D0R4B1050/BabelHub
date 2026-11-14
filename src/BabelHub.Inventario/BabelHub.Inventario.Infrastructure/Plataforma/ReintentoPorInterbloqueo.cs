using BabelHub.Inventario.Application.Plataforma;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Plataforma;

/// <summary>Opciones del reintento por interbloqueo.</summary>
public sealed record OpcionesDeReintentoPorInterbloqueo
{
    /// <summary>Repeticiones tras el primer intento: una sola (D04).</summary>
    public int Repeticiones { get; init; } = 1;

    /// <summary>Pausa antes de repetir, para que la transacción ganadora termine.</summary>
    public TimeSpan EsperaAntesDeRepetir { get; init; } = TimeSpan.FromMilliseconds(25);
}

/// <summary>
/// Decorador de un caso de uso que lo repite cuando PostgreSQL aborta su transacción por un interbloqueo (<c>40P01</c>) o un fallo de
/// serialización (<c>40001</c>). Va <b>por fuera</b> de la transacción (de <c>TransaccionPorCasoDeUso</c>): cada intento abre una transacción
/// nueva, porque una transacción abortada no se puede continuar. Vive en la Infraestructura porque esos códigos son de Npgsql.
/// No repite si ya hay una transacción activa (la repetición debe abrir otra: el abortado es el caso de uso de afuera), ni un caso de uso que
/// no esté marcado <see cref="ICasoDeUsoIdempotente"/>, ni ningún otro error; tras agotar las repeticiones, el error original sube.
/// </summary>
public sealed class ReintentoPorInterbloqueo<TEntrada, TSalida>(
    ICasoDeUso<TEntrada, TSalida> interno,
    IConexionDeLaUnidadDeTrabajo conexion,
    OpcionesDeReintentoPorInterbloqueo? opciones = null,
    bool? esIdempotente = null) : ICasoDeUso<TEntrada, TSalida>
{
    private const string Interbloqueo = "40P01";
    private const string FalloDeSerializacion = "40001";

    private readonly OpcionesDeReintentoPorInterbloqueo _opciones = Validar(opciones ?? new OpcionesDeReintentoPorInterbloqueo());
    private readonly bool _idempotente = esIdempotente ?? interno is ICasoDeUsoIdempotente;

    public async Task<Resultado<TSalida>> Ejecutar(TEntrada entrada, CancellationToken cancelacion = default)
    {
        if (!_idempotente || conexion.HayTransaccion)
        {
            return await interno.Ejecutar(entrada, cancelacion).ConfigureAwait(false);
        }

        for (var repeticion = 0; ; repeticion++)
        {
            try
            {
                return await interno.Ejecutar(entrada, cancelacion).ConfigureAwait(false);
            }
            catch (Exception ex) when (repeticion < _opciones.Repeticiones && EsReintentable(ex) && !cancelacion.IsCancellationRequested)
            {
                if (_opciones.EsperaAntesDeRepetir > TimeSpan.Zero)
                {
                    await Task.Delay(_opciones.EsperaAntesDeRepetir, cancelacion).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>Si el error es de interbloqueo o de serialización de PostgreSQL (en la excepción o en la que la envuelve).</summary>
    public static bool EsReintentable(Exception error)
    {
        for (var actual = error; actual is not null; actual = actual.InnerException)
        {
            if (actual is PostgresException { SqlState: Interbloqueo or FalloDeSerializacion })
            {
                return true;
            }
        }

        return false;
    }

    private static OpcionesDeReintentoPorInterbloqueo Validar(OpcionesDeReintentoPorInterbloqueo opciones) =>
        opciones.Repeticiones >= 0 && opciones.EsperaAntesDeRepetir >= TimeSpan.Zero
            ? opciones
            : throw new ArgumentOutOfRangeException(nameof(opciones), "Las repeticiones y la espera no pueden ser negativas.");
}
