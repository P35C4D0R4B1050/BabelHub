using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;

namespace BabelHub.Ecommerce.Infrastructure.Plataforma;

public sealed record OpcionesDeMotorLocal
{
    /// <summary>Tope de contadores en memoria: con más, se purgan los vencidos y, si aún no cabe, se descarta el de actividad más antigua.</summary>
    public int TopeDeContadores { get; init; } = 50_000;

    public TimeSpan MargenDeExpiracion { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan VigenciaEnCurso { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan VigenciaCompletada { get; init; } = TimeSpan.FromHours(24);
}

/// <summary>
/// Motor de límites en memoria del proceso: el respaldo de las políticas que lo permiten cuando el almacén de contadores no
/// responde (SEG-26) y, en el Inventario, el límite general. Aplica la misma regla de ventana deslizante, bloqueo e idempotencia
/// que el motor de Redis (usa <see cref="PoliticaDeLimite.Evaluar"/>), pero por instancia: con varias instancias el límite
/// real es el de cada una. Es seguro entre hilos con un único candado: las operaciones son cortas.
/// </summary>
public sealed class MotorDeLimitesLocal(OpcionesDeMotorLocal? opciones = null, TimeProvider? tiempo = null) : IMotorDeLimites
{
    private const int OperacionesEntrePurgas = 1000;

    private readonly OpcionesDeMotorLocal _opciones = opciones ?? new OpcionesDeMotorLocal();
    private readonly TimeProvider _tiempo = tiempo ?? TimeProvider.System;
    private readonly object _candado = new();
    private readonly Dictionary<string, Contador> _contadores = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Reservada> _reservas = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Idempotente> _idempotentes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<long>> _rechazos = new(StringComparer.Ordinal);
    private int _operaciones;

    public int ContadoresEnMemoria
    {
        get
        {
            lock (_candado)
            {
                return _contadores.Count;
            }
        }
    }

    public Task<ResultadoDeLimite> Reservar(
        PoliticaDeLimite politica,
        string contador,
        string? claveDelLlamador,
        ClaveDeIdempotencia? idempotencia,
        CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(politica);
        ArgumentException.ThrowIfNullOrWhiteSpace(contador);

        lock (_candado)
        {
            var ahora = Ahora();
            PurgarSiToca(ahora);

            var claveIdem = idempotencia is null ? null : $"idem:{politica.Nombre}:{contador}:{idempotencia.Ruta}:{idempotencia.ClaveDelCliente}";
            if (claveIdem is not null && _idempotentes.TryGetValue(claveIdem, out var previo) && previo.VenceMs > ahora)
            {
                if (previo.HuellaDelCuerpo != idempotencia!.HuellaDelCuerpo)
                {
                    return Task.FromResult(ResultadoDeLimite.ClaveEnConflicto());
                }

                return Task.FromResult(previo.Resultado is { } guardado ? ResultadoDeLimite.RepeticionCompletada(guardado) : ResultadoDeLimite.RepeticionEnCurso());
            }

            var claveRsv = claveDelLlamador is null ? null : $"rsv:{politica.Nombre}:{claveDelLlamador}";
            if (claveRsv is not null && _reservas.TryGetValue(claveRsv, out var reservada) && reservada.VenceMs > ahora)
            {
                return Task.FromResult(ResultadoDeLimite.Permitido(
                    new ReservaDeLimite(politica.Nombre, contador, reservada.Id, Instante(reservada.Ms), claveDelLlamador, claveIdem)));
            }

            var clave = $"{politica.Nombre}|{contador}";
            var existente = _contadores.TryGetValue(clave, out var estado);
            estado ??= new Contador();
            var bloqueoHasta = estado.BloqueoHastaMs is { } b && b > ahora ? Instante(b) : (DateTimeOffset?)null;

            var vivas = estado.Entradas.Where(e => e.Ms > ahora - (long)politica.Ventana.TotalMilliseconds).Select(e => Instante(e.Ms)).ToList();
            var decision = politica.Evaluar(vivas, bloqueoHasta, Instante(ahora));
            if (!decision.Permitida)
            {
                var respuesta = decision.Respuesta!;
                return Task.FromResult(ResultadoDeLimite.Excedido(respuesta.Motivo, respuesta.EsperaEnSegundos, respuesta.DisponibleDesde));
            }

            if (!existente)
            {
                HacerLugar(ahora);
                _contadores[clave] = estado;
            }

            estado.Entradas.RemoveAll(e => e.Ms <= ahora - (long)politica.Ventana.TotalMilliseconds);
            var id = Guid.NewGuid().ToString("N");
            estado.Entradas.Add((id, ahora));
            estado.UltimaActividadMs = ahora;
            estado.VenceMs = ahora + (long)(politica.Ventana + _opciones.MargenDeExpiracion).TotalMilliseconds;
            if (decision.BloqueoHastaNuevo is { } nuevo)
            {
                estado.BloqueoHastaMs = nuevo.ToUnixTimeMilliseconds();
                estado.BloqueoCreadoPor = id;
            }

            if (claveRsv is not null)
            {
                _reservas[claveRsv] = new Reservada(id, ahora, ahora + (long)politica.Ventana.TotalMilliseconds);
            }

            if (claveIdem is not null)
            {
                _idempotentes[claveIdem] = new Idempotente(idempotencia!.HuellaDelCuerpo, null, ahora + (long)_opciones.VigenciaEnCurso.TotalMilliseconds);
            }

            return Task.FromResult(ResultadoDeLimite.Permitido(
                new ReservaDeLimite(politica.Nombre, contador, id, Instante(ahora), claveDelLlamador, claveIdem)));
        }
    }

    public Task Liberar(PoliticaDeLimite politica, ReservaDeLimite reserva, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(reserva);

        lock (_candado)
        {
            if (_contadores.TryGetValue($"{reserva.Politica}|{reserva.Sujeto}", out var estado))
            {
                estado.Entradas.RemoveAll(e => e.Id == reserva.Identificador);
                if (estado.BloqueoCreadoPor == reserva.Identificador)
                {
                    estado.BloqueoHastaMs = null;
                    estado.BloqueoCreadoPor = null;
                }
            }

            if (reserva.ClaveDelLlamador is not null)
            {
                _reservas.Remove($"rsv:{reserva.Politica}:{reserva.ClaveDelLlamador}");
            }
        }

        return Task.CompletedTask;
    }

    public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(reserva);
        ArgumentNullException.ThrowIfNull(resultado);

        lock (_candado)
        {
            if (reserva.ClaveDeIdempotencia is { } clave && _idempotentes.TryGetValue(clave, out var previo))
            {
                _idempotentes[clave] = previo with { Resultado = resultado, VenceMs = Ahora() + (long)_opciones.VigenciaCompletada.TotalMilliseconds };
            }
        }

        return Task.CompletedTask;
    }

    public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(reserva);

        lock (_candado)
        {
            if (reserva.ClaveDeIdempotencia is { } clave)
            {
                _idempotentes.Remove(clave);
            }
        }

        return Task.CompletedTask;
    }

    public Task<int> RegistrarRechazoDeOrigen(HuellaDeOrigen origen, TimeSpan ventana, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(origen);

        lock (_candado)
        {
            var ahora = Ahora();
            var clave = origen.ToString();
            if (!_rechazos.TryGetValue(clave, out var lista))
            {
                HacerLugar(ahora);
                lista = _rechazos[clave] = [];
            }

            lista.RemoveAll(i => i <= ahora - (long)ventana.TotalMilliseconds);
            lista.Add(ahora);
            return Task.FromResult(lista.Count);
        }
    }

    public Task<int> ContarRechazosDeOrigen(HuellaDeOrigen origen, TimeSpan ventana, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(origen);

        lock (_candado)
        {
            var ahora = Ahora();
            return Task.FromResult(_rechazos.TryGetValue(origen.ToString(), out var lista) ? lista.Count(i => i > ahora - (long)ventana.TotalMilliseconds) : 0);
        }
    }

    private long Ahora() => _tiempo.GetUtcNow().ToUnixTimeMilliseconds();

    private static DateTimeOffset Instante(long milisegundos) => DateTimeOffset.FromUnixTimeMilliseconds(milisegundos);

    private void PurgarSiToca(long ahora)
    {
        if (++_operaciones % OperacionesEntrePurgas == 0)
        {
            Purgar(ahora);
        }
    }

    private void Purgar(long ahora)
    {
        foreach (var clave in _contadores.Where(p => p.Value.VenceMs <= ahora).Select(p => p.Key).ToList())
        {
            _contadores.Remove(clave);
        }

        foreach (var clave in _reservas.Where(p => p.Value.VenceMs <= ahora).Select(p => p.Key).ToList())
        {
            _reservas.Remove(clave);
        }

        foreach (var clave in _idempotentes.Where(p => p.Value.VenceMs <= ahora).Select(p => p.Key).ToList())
        {
            _idempotentes.Remove(clave);
        }

        foreach (var clave in _rechazos.Where(p => p.Value.Count == 0 || p.Value[^1] <= ahora - (long)TimeSpan.FromHours(1).TotalMilliseconds).Select(p => p.Key).ToList())
        {
            _rechazos.Remove(clave);
        }
    }

    /// <summary>Antes de crear un contador nuevo: si se llegó al tope, purga los vencidos y, si aún no cabe, descarta el de actividad más antigua.</summary>
    private void HacerLugar(long ahora)
    {
        if (_contadores.Count + _rechazos.Count < _opciones.TopeDeContadores)
        {
            return;
        }

        Purgar(ahora);
        while (_contadores.Count + _rechazos.Count >= _opciones.TopeDeContadores && _contadores.Count > 0)
        {
            var masAntiguo = _contadores.MinBy(p => p.Value.UltimaActividadMs).Key;
            _contadores.Remove(masAntiguo);
        }

        if (_contadores.Count + _rechazos.Count >= _opciones.TopeDeContadores && _rechazos.Count > 0)
        {
            _rechazos.Remove(_rechazos.MinBy(p => p.Value.Count == 0 ? 0 : p.Value[^1]).Key);
        }
    }

    private sealed class Contador
    {
        public List<(string Id, long Ms)> Entradas { get; } = [];

        public long? BloqueoHastaMs { get; set; }

        public string? BloqueoCreadoPor { get; set; }

        public long UltimaActividadMs { get; set; }

        public long VenceMs { get; set; }
    }

    private sealed record Reservada(string Id, long Ms, long VenceMs);

    private sealed record Idempotente(string HuellaDelCuerpo, ResultadoMinimo? Resultado, long VenceMs);
}
