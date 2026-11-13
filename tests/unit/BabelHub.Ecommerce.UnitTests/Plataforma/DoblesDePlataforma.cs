using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

internal sealed class RelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset Ahora { get; set; } = ahora;

    public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
}

internal sealed class OrigenFijo(OrigenDeSolicitud origen) : IOrigenDeSolicitud
{
    public OrigenDeSolicitud Actual { get; set; } = origen;
}

internal sealed class PublicadorDeEventosEspia : IPublicadorDeEventosDeSeguridad
{
    public List<EventoDeSeguridad> Eventos { get; } = [];

    public Task Publicar(EventoDeSeguridad evento, CancellationToken cancelacion = default)
    {
        Eventos.Add(evento);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Motor de contadores en memoria con la semántica del contrato (ventana deslizante con la política del Dominio, bloqueo,
/// reserva idempotente por clave del llamador, idempotencia del cliente y rechazos por origen). Sirve de doble en las
/// pruebas de la Aplicación; el motor real de producción se prueba en la Infraestructura.
/// </summary>
internal sealed class MotorDeMemoria(IReloj reloj) : IMotorDeLimites
{
    private sealed class Contador
    {
        public List<(string Id, DateTimeOffset Instante)> Entradas { get; } = [];

        public DateTimeOffset? BloqueoHasta { get; set; }

        public string? BloqueoCreadoPor { get; set; }
    }

    private sealed record Idempotente(ClaveDeIdempotencia Clave, bool Completada, ResultadoMinimo? Resultado);

    private readonly Dictionary<string, Contador> _contadores = [];
    private readonly Dictionary<string, ReservaDeLimite> _porClaveDelLlamador = [];
    private readonly Dictionary<string, Idempotente> _idempotentes = [];
    private readonly Dictionary<string, string> _claveDeIdempotenciaPorReserva = [];
    private readonly Dictionary<string, List<DateTimeOffset>> _rechazos = [];

    public bool NoDisponible { get; set; }

    /// <summary>Claves de contador con las que se llamó, tal como las compone la Aplicación.</summary>
    public List<string> ContadoresUsados { get; } = [];

    public int Cupos(string contador) =>
        _contadores.Where(p => p.Key.EndsWith("|" + contador, StringComparison.Ordinal)).Sum(p => p.Value.Entradas.Count);

    public Task<ResultadoDeLimite> Reservar(
        PoliticaDeLimite politica,
        string contador,
        string? claveDelLlamador,
        ClaveDeIdempotencia? idempotencia,
        CancellationToken cancelacion)
    {
        ComprobarDisponible();
        ContadoresUsados.Add(contador);

        var claveIdem = idempotencia is null ? null : $"{politica.Nombre}|{idempotencia.ClaveDelCliente}";
        if (claveIdem is not null && _idempotentes.TryGetValue(claveIdem, out var previa))
        {
            if (previa.Clave != idempotencia)
            {
                return Task.FromResult(ResultadoDeLimite.ClaveEnConflicto());
            }

            return Task.FromResult(previa.Completada ? ResultadoDeLimite.RepeticionCompletada(previa.Resultado!) : ResultadoDeLimite.RepeticionEnCurso());
        }

        var claveRsv = claveDelLlamador is null ? null : $"{politica.Nombre}|{claveDelLlamador}";
        if (claveRsv is not null && _porClaveDelLlamador.TryGetValue(claveRsv, out var existente))
        {
            return Task.FromResult(ResultadoDeLimite.Permitido(existente));
        }

        var estado = ObtenerContador(politica.Nombre, contador);
        var ahora = reloj.Ahora;
        var decision = politica.Evaluar(estado.Entradas.Select(e => e.Instante).ToList(), estado.BloqueoHasta, ahora);
        if (!decision.Permitida)
        {
            var respuesta = decision.Respuesta!;
            return Task.FromResult(ResultadoDeLimite.Excedido(respuesta.Motivo, respuesta.EsperaEnSegundos, respuesta.DisponibleDesde));
        }

        var id = Guid.NewGuid().ToString("N");
        estado.Entradas.Add((id, ahora));
        if (decision.BloqueoHastaNuevo is { } bloqueo)
        {
            estado.BloqueoHasta = bloqueo;
            estado.BloqueoCreadoPor = id;
        }

        var reserva = new ReservaDeLimite(politica.Nombre, contador, id, ahora, claveDelLlamador);
        if (claveRsv is not null)
        {
            _porClaveDelLlamador[claveRsv] = reserva;
        }

        if (claveIdem is not null)
        {
            _idempotentes[claveIdem] = new Idempotente(idempotencia!, false, null);
            _claveDeIdempotenciaPorReserva[id] = claveIdem;
        }

        return Task.FromResult(ResultadoDeLimite.Permitido(reserva));
    }

    public Task Liberar(PoliticaDeLimite politica, ReservaDeLimite reserva, CancellationToken cancelacion)
    {
        ComprobarDisponible();
        var estado = ObtenerContador(reserva.Politica, reserva.Sujeto);
        estado.Entradas.RemoveAll(e => e.Id == reserva.Identificador);
        if (estado.BloqueoCreadoPor == reserva.Identificador)
        {
            estado.BloqueoHasta = null;
            estado.BloqueoCreadoPor = null;
        }

        if (reserva.ClaveDelLlamador is not null)
        {
            _porClaveDelLlamador.Remove($"{politica.Nombre}|{reserva.ClaveDelLlamador}");
        }

        return Task.CompletedTask;
    }

    public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion)
    {
        ComprobarDisponible();
        if (_claveDeIdempotenciaPorReserva.TryGetValue(reserva.Identificador, out var clave))
        {
            _idempotentes[clave] = _idempotentes[clave] with { Completada = true, Resultado = resultado };
        }

        return Task.CompletedTask;
    }

    public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion)
    {
        ComprobarDisponible();
        if (_claveDeIdempotenciaPorReserva.TryGetValue(reserva.Identificador, out var clave))
        {
            _idempotentes.Remove(clave);
        }

        return Task.CompletedTask;
    }

    public Task<int> RegistrarRechazoDeOrigen(HuellaDeOrigen origen, TimeSpan ventana, CancellationToken cancelacion)
    {
        ComprobarDisponible();
        var lista = _rechazos.TryGetValue(origen.ToString(), out var l) ? l : _rechazos[origen.ToString()] = [];
        lista.Add(reloj.Ahora);
        return Task.FromResult(lista.Count(i => i > reloj.Ahora - ventana));
    }

    public Task<int> ContarRechazosDeOrigen(HuellaDeOrigen origen, TimeSpan ventana, CancellationToken cancelacion)
    {
        ComprobarDisponible();
        return Task.FromResult(_rechazos.TryGetValue(origen.ToString(), out var l) ? l.Count(i => i > reloj.Ahora - ventana) : 0);
    }

    private Contador ObtenerContador(string politica, string contador)
    {
        var clave = $"{politica}|{contador}";
        return _contadores.TryGetValue(clave, out var c) ? c : _contadores[clave] = new Contador();
    }

    private void ComprobarDisponible()
    {
        if (NoDisponible)
        {
            throw new MotorDeLimitesNoDisponibleException("El almacén de contadores no responde.");
        }
    }
}
