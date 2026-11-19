using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>La cuenta del pagador: puede pagar, tiene la cancelación en ejecución o cae. Anota el orden de las lecturas y si alguna ocurrió dentro de una transacción.</summary>
internal sealed class CuentaDelPagadorDoble(UnidadDeTrabajoReversible unidad, List<string> orden) : ICuentaDelPagador
{
    public EstadoDeLaCuentaParaPagar? Estado { get; set; } = new(true, null);

    public bool Falla { get; set; }

    public bool CancelacionEnCurso { get; set; }

    public string? Correo { get; set; } = "ana@correo.com";

    public int ConsultasDentroDeUnaTransaccion { get; private set; }

    public int Consultas { get; private set; }

    public int MarcasLeidasConBloqueo { get; private set; }

    public Task<EstadoDeLaCuentaParaPagar?> Consultar(Guid cuentaId, CancellationToken cancelacion = default)
    {
        Consultas++;
        orden.Add("cuenta");
        if (unidad.EnCurso)
        {
            ConsultasDentroDeUnaTransaccion++;
        }

        return Falla ? throw new InvalidOperationException("Identidad no responde.") : Task.FromResult(Estado);
    }

    public Task<bool> CancelacionEnEjecucion(Guid cuentaId, bool bloquear, CancellationToken cancelacion = default)
    {
        orden.Add(bloquear ? "marca-con-bloqueo" : "marca");
        if (bloquear)
        {
            MarcasLeidasConBloqueo++;
        }

        return Falla ? throw new InvalidOperationException("Identidad no responde.") : Task.FromResult(CancelacionEnCurso);
    }

    public Task<string?> CorreoDelTitular(Guid cuentaId, CancellationToken cancelacion = default) => Task.FromResult(Correo);
}

internal sealed class PosesionDeEdicionesDoble : IPosesionDeEdiciones
{
    public ResultadoDePosesion Resultado { get; set; } = ResultadoDePosesion.NoPosee;

    public bool Falla { get; set; }

    public List<IReadOnlyCollection<Guid>> Consultadas { get; } = [];

    public Task<ResultadoDePosesion> Posee(Guid cuentaId, IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        Consultadas.Add(edicionIds);
        return Falla ? throw new InvalidOperationException("Biblioteca no responde.") : Task.FromResult(Resultado);
    }
}

/// <summary>Las reservas del Inventario: respuestas configurables y el registro de cuántas llamadas ocurrieron dentro de una transacción (no debe haber ninguna, D03).</summary>
internal sealed class ReservasDelPagoDoble(UnidadDeTrabajoReversible unidad, RelojFijo reloj) : IReservasDelPago
{
    public RespuestaDeReserva<EstadoDeLaReserva>? Consulta { get; set; }

    public RespuestaDeReserva<EstadoDeLaReserva>? Extension { get; set; }

    public RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>? Confirmacion { get; set; }

    public RespuestaDeReserva<bool>? Liberacion { get; set; }

    public bool Falla { get; set; }

    public int LlamadasDentroDeUnaTransaccion { get; private set; }

    public List<DateTimeOffset> Extensiones { get; } = [];

    public List<string> Confirmaciones { get; } = [];

    public List<MotivoDeLiberacion> Liberaciones { get; } = [];

    public int Consultas { get; private set; }

    private void Anotar()
    {
        if (unidad.EnCurso)
        {
            LlamadasDentroDeUnaTransaccion++;
        }

        if (Falla)
        {
            throw new InvalidOperationException("El Inventario no responde.");
        }
    }

    public Task<RespuestaDeReserva<EstadoDeLaReserva>> Consultar(Guid pedidoId, Guid cuentaId, CancellationToken cancelacion = default)
    {
        Consultas++;
        Anotar();
        return Task.FromResult(Consulta ?? new RespuestaDeReserva<EstadoDeLaReserva>(ResultadoDeLaLlamadaDeReserva.Correcto, new EstadoDeLaReserva(reloj.Ahora.AddMinutes(10), true)));
    }

    public Task<RespuestaDeReserva<EstadoDeLaReserva>> Extender(Guid pedidoId, DateTimeOffset hasta, CancellationToken cancelacion = default)
    {
        Anotar();
        Extensiones.Add(hasta);
        return Task.FromResult(Extension ?? new RespuestaDeReserva<EstadoDeLaReserva>(ResultadoDeLaLlamadaDeReserva.Correcto, new EstadoDeLaReserva(hasta, true)));
    }

    public Task<RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>> Confirmar(Guid pedidoId, string claveDeIdempotencia, CancellationToken cancelacion = default)
    {
        Anotar();
        Confirmaciones.Add(claveDeIdempotencia);
        return Task.FromResult(Confirmacion ?? new RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>(ResultadoDeLaLlamadaDeReserva.Correcto, []));
    }

    public Task<RespuestaDeReserva<bool>> Liberar(Guid pedidoId, MotivoDeLiberacion motivo, CancellationToken cancelacion = default)
    {
        Anotar();
        Liberaciones.Add(motivo);
        return Task.FromResult(Liberacion ?? new RespuestaDeReserva<bool>(ResultadoDeLaLlamadaDeReserva.Correcto, true));
    }
}

/// <summary>El motor de límites del cobro: permite todo, excede o cae, y anota cuántas reservas se pidieron.</summary>
internal sealed class LimitesDeCobroDoble : IControlDeLimites
{
    public bool Excedido { get; set; }

    public bool Caido { get; set; }

    public int Reservas { get; private set; }

    public Task<ResultadoDeLimite> Reservar(ClaveDePolitica politica, string? sujeto = null, string? claveDelLlamador = null, HuellaDeSujeto? huellaDelLlamador = null, ClaveDeIdempotencia? idempotencia = null, CancellationToken cancelacion = default)
    {
        Reservas++;
        if (Caido)
        {
            return Task.FromResult(ResultadoDeLimite.NoDisponible());
        }

        return Task.FromResult(Excedido
            ? ResultadoDeLimite.Excedido(politica.Valor, 900, DateTimeOffset.UtcNow.AddSeconds(900))
            : ResultadoDeLimite.Permitido(new ReservaDeLimite(politica.Valor, "sujeto", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow)));
    }

    public Task Liberar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;
}
