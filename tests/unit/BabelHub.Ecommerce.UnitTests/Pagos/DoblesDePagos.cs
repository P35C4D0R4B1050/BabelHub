using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Pagos;
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

/// <summary>La pasarela: responde lo que la prueba fije, puede tardar más que el plazo o fallar, y anota lo que recibió y si la llamaron dentro de una transacción.</summary>
internal sealed class PasarelaDePagoDoble(UnidadDeTrabajoReversible unidad) : IPasarelaDePago
{
    public Func<SolicitudDeCobroEnPasarela, ResultadoDeCobro>? Respuesta { get; set; }

    public bool NoResponde { get; set; }

    public bool Lanza { get; set; }

    public List<SolicitudDeCobroEnPasarela> Solicitudes { get; } = [];

    public int LlamadasDentroDeUnaTransaccion { get; private set; }

    public async Task<ResultadoDeCobro> Cobrar(SolicitudDeCobroEnPasarela solicitud, CancellationToken cancelacion = default)
    {
        Solicitudes.Add(solicitud);
        if (unidad.EnCurso)
        {
            LlamadasDentroDeUnaTransaccion++;
        }

        if (Lanza)
        {
            throw new HttpRequestException("La pasarela falló.");
        }

        if (NoResponde)
        {
            await Task.Delay(Timeout.Infinite, cancelacion);
        }

        return Respuesta is { } f ? f(solicitud) : ResultadoDeCobro.Aprobado("ch_1", solicitud.Importe, "PEN", solicitud.ReferenciaOpaca);
    }

    public Task<ResultadoDeCobro> ConsultarEstado(Guid referenciaOpaca, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<ResultadoDeReembolsoEnPasarela> Reembolsar(Guid reembolsoId, string referenciaDelCargo, Importe monto, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<ReembolsoEnPasarela>?> ConsultarReembolsos(string referenciaDelCargo, CancellationToken cancelacion = default) => throw new NotSupportedException();
}

/// <summary>La confirmación de la compra aprobada (T14): anota la llamada y si ocurrió dentro de una transacción.</summary>
internal sealed class ConfirmadorDeCompraDoble(UnidadDeTrabajoReversible unidad) : IConfirmadorDeCompraAprobada
{
    public List<(Guid PedidoId, Guid IntentoId, string Referencia)> Llamadas { get; } = [];

    public int LlamadasDentroDeUnaTransaccion { get; private set; }

    public Task<ResultadoDelCobroDelCliente> Confirmar(Guid pedidoId, Guid intentoId, string referenciaDePasarela, CancellationToken cancelacion = default)
    {
        Llamadas.Add((pedidoId, intentoId, referenciaDePasarela));
        if (unidad.EnCurso)
        {
            LlamadasDentroDeUnaTransaccion++;
        }

        return Task.FromResult(new ResultadoDelCobroDelCliente(Application.Pagos.EstadoDelCobro.Aprobado, null, intentoId));
    }
}

internal sealed class AlertasTecnicasEspia : IPublicadorDeAlertasTecnicas
{
    public List<AlertaTecnica> Publicadas { get; } = [];

    public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
    {
        Publicadas.Add(alerta);
        return Task.CompletedTask;
    }
}
