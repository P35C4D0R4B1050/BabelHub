namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>
/// Tope de renovaciones simultáneas por proceso (plan §5: 8): cada una retiene una conexión de la base mientras espera el bloqueo de su fila y la llamada a Identidad,
/// y sin un tope una ráfaga de credenciales por vencer agotaría el grupo de conexiones. Las demás esperan un turno un tiempo acotado y, si no llega, responden servicio
/// no disponible (se reintenta en la solicitud siguiente). Es un singleton del proceso.
/// </summary>
public sealed class LimitadorDeRenovaciones : IDisposable
{
    public const int MaximoPorProceso = 8;

    private readonly SemaphoreSlim _turnos;
    private readonly TimeSpan _espera;

    public LimitadorDeRenovaciones(int maximo = MaximoPorProceso, TimeSpan? espera = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximo, 1);
        _turnos = new SemaphoreSlim(maximo, maximo);
        _espera = espera ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>Un turno para renovar, o <see langword="null"/> si no llegó dentro del plazo. Quien lo recibe lo libera con <see cref="IDisposable.Dispose"/>.</summary>
    public async Task<IDisposable?> Entrar(CancellationToken cancelacion)
    {
        if (!await _turnos.WaitAsync(_espera, cancelacion).ConfigureAwait(false))
        {
            return null;
        }

        return new Turno(_turnos);
    }

    public void Dispose() => _turnos.Dispose();

    private sealed class Turno(SemaphoreSlim turnos) : IDisposable
    {
        private int _liberado;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _liberado, 1) == 0)
            {
                turnos.Release();
            }
        }
    }
}
