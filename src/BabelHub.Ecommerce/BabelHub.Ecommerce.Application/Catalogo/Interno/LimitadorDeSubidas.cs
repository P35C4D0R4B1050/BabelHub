namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

/// <summary>
/// El permiso de una subida en curso (SEG-07, SEG-08): mientras vive cuenta contra el tope global y el del usuario, y su <see cref="Cancelacion"/> se dispara cuando la subida supera la duración máxima. Se libera una sola vez con
/// <see cref="Dispose"/>, también si la subida falla.
/// </summary>
internal sealed class PermisoDeSubida : IDisposable
{
    private readonly Action _liberar;
    private readonly CancellationTokenSource _plazo;
    private int _liberado;

    internal PermisoDeSubida(Action liberar, CancellationTokenSource plazo)
    {
        _liberar = liberar;
        _plazo = plazo;
    }

    /// <summary>Se cancela al cumplirse la duración máxima de la subida.</summary>
    public CancellationToken Cancelacion => _plazo.Token;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _liberado, 1) == 0)
        {
            _plazo.Dispose();
            _liberar();
        }
    }
}

/// <summary>
/// El limitador de subidas del Catálogo (plan §6, SEG-07, SEG-08): <b>4 subidas simultáneas</b> en el proceso, <b>2 por usuario</b> y una <b>duración máxima de 15 minutos</b> por subida (la depuración usa un margen mayor).
/// No espera: si no hay cupo devuelve <see langword="null"/> y la Api responde <c>429</c> con <c>Retry-After</c>. Es lógica en proceso y no depende de nada externo, por eso no es un puerto. Es seguro entre hilos.
/// </summary>
internal sealed class LimitadorDeSubidas(int maximoGlobal = LimitadorDeSubidas.MaximoGlobalPorDefecto, int maximoPorUsuario = LimitadorDeSubidas.MaximoPorUsuarioPorDefecto, TimeSpan? duracionMaxima = null, TimeProvider? reloj = null)
{
    public const int MaximoGlobalPorDefecto = 4;

    public const int MaximoPorUsuarioPorDefecto = 2;

    public static readonly TimeSpan DuracionMaximaPorDefecto = TimeSpan.FromMinutes(15);

    /// <summary>Lo que se sugiere esperar (<c>Retry-After</c>) cuando no hay cupo.</summary>
    public static readonly TimeSpan EsperaSugerida = TimeSpan.FromSeconds(30);

    private readonly object _candado = new();
    private readonly Dictionary<Guid, int> _porUsuario = [];
    private readonly TimeSpan _duracionMaxima = duracionMaxima ?? DuracionMaximaPorDefecto;
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private readonly int _maximoGlobal = maximoGlobal > 0 ? maximoGlobal : throw new ArgumentOutOfRangeException(nameof(maximoGlobal));
    private readonly int _maximoPorUsuario = maximoPorUsuario > 0 ? maximoPorUsuario : throw new ArgumentOutOfRangeException(nameof(maximoPorUsuario));
    private int _enCurso;

    /// <summary>Las subidas en curso (para las pruebas y el diagnóstico).</summary>
    public int EnCurso
    {
        get
        {
            lock (_candado)
            {
                return _enCurso;
            }
        }
    }

    /// <summary>Toma un cupo para el usuario, o <see langword="null"/> si el proceso o el usuario ya están en su tope.</summary>
    public PermisoDeSubida? Intentar(Guid usuarioId)
    {
        lock (_candado)
        {
            var delUsuario = _porUsuario.GetValueOrDefault(usuarioId);
            if (_enCurso >= _maximoGlobal || delUsuario >= _maximoPorUsuario)
            {
                return null;
            }

            _enCurso++;
            _porUsuario[usuarioId] = delUsuario + 1;
        }

        var plazo = new CancellationTokenSource(_duracionMaxima, _reloj);
        return new PermisoDeSubida(() => Liberar(usuarioId), plazo);
    }

    private void Liberar(Guid usuarioId)
    {
        lock (_candado)
        {
            _enCurso--;
            var restantes = _porUsuario.GetValueOrDefault(usuarioId) - 1;
            if (restantes <= 0)
            {
                _porUsuario.Remove(usuarioId);
            }
            else
            {
                _porUsuario[usuarioId] = restantes;
            }
        }
    }
}
