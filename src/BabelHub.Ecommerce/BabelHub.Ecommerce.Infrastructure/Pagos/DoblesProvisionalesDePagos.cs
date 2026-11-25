using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Ecommerce.Infrastructure.Pagos;

/// <summary>Un módulo del que depende Pagos aún no existe y su doble falla cerrado: no se cobra ni se confirma nada con él.</summary>
public sealed class DependenciaProvisionalDePagosNoDisponibleException : InvalidOperationException
{
    public DependenciaProvisionalDePagosNoDisponibleException(string modulo)
        : base($"El módulo {modulo} aún no existe y su doble provisional falla cerrado.")
    {
        Modulo = modulo;
    }

    public DependenciaProvisionalDePagosNoDisponibleException()
    {
        Modulo = string.Empty;
    }

    public DependenciaProvisionalDePagosNoDisponibleException(string mensaje, Exception interna)
        : base(mensaje, interna)
    {
        Modulo = string.Empty;
    }

    public string Modulo { get; }
}

/// <summary>La cuenta del pagador sin el módulo Identidad real: no se sabe nada de la cuenta, así que no se cobra (falla cerrada, PAG-21, PAG-25).</summary>
internal sealed class CuentaDelPagadorSinIdentidad : ICuentaDelPagador
{
    public Task<EstadoDeLaCuentaParaPagar?> Consultar(Guid cuentaId, CancellationToken cancelacion = default) => throw new DependenciaProvisionalDePagosNoDisponibleException("Identidad");

    public Task<bool> CancelacionEnEjecucion(Guid cuentaId, bool bloquear, CancellationToken cancelacion = default) => throw new DependenciaProvisionalDePagosNoDisponibleException("Identidad");

    public Task<string?> CorreoDelTitular(Guid cuentaId, CancellationToken cancelacion = default) => throw new DependenciaProvisionalDePagosNoDisponibleException("Identidad");
}

/// <summary>La confirmación de la compra sin el módulo Pedidos real: no crea ningún pedido (falla cerrado; la compra aprobada se reintenta o se compensa).</summary>
internal sealed class ConfirmacionDeCompraSinPedidos : IConfirmacionDeCompra
{
    public Task<ResultadoDeConfirmarCompra> Confirmar(CompraAprobada compra, CancellationToken cancelacion = default) => throw new DependenciaProvisionalDePagosNoDisponibleException("Pedidos");
}

/// <summary>La pasarela sin ningún proveedor: «sin respuesta» a todo y nunca aprobada. Es un doble hasta el simulador y el adaptador nulo (T25).</summary>
internal sealed class PasarelaDePagoSinProveedor : IPasarelaDePago
{
    public Task<ResultadoDeCobro> Cobrar(SolicitudDeCobroEnPasarela solicitud, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeCobro.SinRespuesta());

    public Task<ResultadoDeCobro> ConsultarEstado(Guid referenciaOpaca, CancellationToken cancelacion = default, DateTimeOffset? iniciadoEn = null) => Task.FromResult(ResultadoDeCobro.SinRespuesta());

    public Task<ResultadoDeReembolsoEnPasarela> Reembolsar(Guid reembolsoId, string referenciaDelCargo, Importe monto, CancellationToken cancelacion = default) =>
        Task.FromResult(ResultadoDeReembolsoEnPasarela.SinRespuesta);

    public Task<IReadOnlyList<ReembolsoEnPasarela>?> ConsultarReembolsos(string referenciaDelCargo, CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<ReembolsoEnPasarela>?>(null);

    public AvisoDeLaPasarela? InterpretarAviso(ReadOnlyMemory<byte> cuerpo) => null;
}

/// <summary>
/// Un Inventario simulado fiel a los contratos v1 para las reservas del pago: una reserva pendiente se consulta, se extiende una sola vez, se confirma con una clave idempotente y se libera (liberar una ya expirada o
/// liberada es un éxito sin cambios; liberar con <c>pago-vencido</c> una ya confirmada es un estado inválido). Solo para <c>Development</c> y <c>Test</c>.
/// </summary>
internal sealed class ReservasSimuladasDelPago(IReloj reloj) : IReservasDelPago
{
    private readonly Lock _candado = new();
    private readonly Dictionary<Guid, Reserva> _reservas = [];

    public bool Caido { get; set; }

    /// <summary>Crea una reserva pendiente con las líneas que devolverá <c>confirmar</c>.</summary>
    public void Sembrar(Guid pedidoId, DateTimeOffset expiraEn, IReadOnlyList<LineaConfirmadaDeReserva> lineas)
    {
        lock (_candado)
        {
            _reservas[pedidoId] = new Reserva(expiraEn, lineas);
        }
    }

    public Task<RespuestaDeReserva<EstadoDeLaReserva>> Consultar(Guid pedidoId, Guid cuentaId, CancellationToken cancelacion = default)
    {
        lock (_candado)
        {
            if (Caido)
            {
                return Task.FromResult(new RespuestaDeReserva<EstadoDeLaReserva>(ResultadoDeLaLlamadaDeReserva.SinRespuesta, null));
            }

            return Task.FromResult(_reservas.TryGetValue(pedidoId, out var r)
                ? new RespuestaDeReserva<EstadoDeLaReserva>(ResultadoDeLaLlamadaDeReserva.Correcto, new EstadoDeLaReserva(r.ExpiraEn, r.Estado == EstadoDeReserva.Pendiente && r.ExpiraEn > reloj.Ahora))
                : new RespuestaDeReserva<EstadoDeLaReserva>(ResultadoDeLaLlamadaDeReserva.NoExiste, null));
        }
    }

    /// <summary>Como el Inventario real (<c>ExtenderReservas</c>, contrato v1): sin cuerpo, el instante lo calcula el servicio (inicio de la reserva más 30 minutos) y lo que no se puede extender (ya extendida, vencida o confirmada) es un éxito sin cambios con <c>Extendida = false</c>, nunca un error.</summary>
    public Task<RespuestaDeReserva<ResultadoDeExtension>> Extender(Guid pedidoId, CancellationToken cancelacion = default)
    {
        lock (_candado)
        {
            if (Caido)
            {
                return Task.FromResult(new RespuestaDeReserva<ResultadoDeExtension>(ResultadoDeLaLlamadaDeReserva.SinRespuesta, null));
            }

            if (!_reservas.TryGetValue(pedidoId, out var r))
            {
                return Task.FromResult(new RespuestaDeReserva<ResultadoDeExtension>(ResultadoDeLaLlamadaDeReserva.NoExiste, null));
            }

            var extendible = r.Estado == EstadoDeReserva.Pendiente && !r.Extendida && r.ExpiraEn > reloj.Ahora;
            if (extendible)
            {
                r.ExpiraEn = r.IniciadaEn + PlazosDelPago.PlazoDePendiente;
                r.Extendida = true;
            }

            return Task.FromResult(new RespuestaDeReserva<ResultadoDeExtension>(ResultadoDeLaLlamadaDeReserva.Correcto, new ResultadoDeExtension(r.ExpiraEn, extendible)));
        }
    }

    public Task<RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>> Confirmar(Guid pedidoId, string claveDeIdempotencia, CancellationToken cancelacion = default)
    {
        lock (_candado)
        {
            if (Caido)
            {
                return Task.FromResult(new RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>(ResultadoDeLaLlamadaDeReserva.SinRespuesta, null));
            }

            if (!_reservas.TryGetValue(pedidoId, out var r))
            {
                return Task.FromResult(new RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>(ResultadoDeLaLlamadaDeReserva.NoExiste, null));
            }

            if (r.Estado == EstadoDeReserva.Confirmada && r.ClaveDeConfirmacion == claveDeIdempotencia)
            {
                return Task.FromResult(new RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>(ResultadoDeLaLlamadaDeReserva.Correcto, r.Lineas));
            }

            if (r.Estado != EstadoDeReserva.Pendiente || r.ExpiraEn <= reloj.Ahora)
            {
                return Task.FromResult(new RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>(ResultadoDeLaLlamadaDeReserva.EstadoInvalido, null));
            }

            r.Estado = EstadoDeReserva.Confirmada;
            r.ClaveDeConfirmacion = claveDeIdempotencia;
            return Task.FromResult(new RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>(ResultadoDeLaLlamadaDeReserva.Correcto, r.Lineas));
        }
    }

    public Task<RespuestaDeReserva<bool>> Liberar(Guid pedidoId, MotivoDeLiberacion motivo, CancellationToken cancelacion = default)
    {
        lock (_candado)
        {
            if (Caido)
            {
                return Task.FromResult(new RespuestaDeReserva<bool>(ResultadoDeLaLlamadaDeReserva.SinRespuesta, false));
            }

            if (!_reservas.TryGetValue(pedidoId, out var r))
            {
                return Task.FromResult(new RespuestaDeReserva<bool>(ResultadoDeLaLlamadaDeReserva.NoExiste, false));
            }

            if (r.Estado == EstadoDeReserva.Confirmada && motivo == MotivoDeLiberacion.PagoVencido)
            {
                return Task.FromResult(new RespuestaDeReserva<bool>(ResultadoDeLaLlamadaDeReserva.EstadoInvalido, false));
            }

            r.Estado = EstadoDeReserva.Liberada;
            return Task.FromResult(new RespuestaDeReserva<bool>(ResultadoDeLaLlamadaDeReserva.Correcto, true));
        }
    }

    private enum EstadoDeReserva
    {
        Pendiente,
        Confirmada,
        Liberada,
    }

    private sealed class Reserva(DateTimeOffset expiraEn, IReadOnlyList<LineaConfirmadaDeReserva> lineas)
    {
        public DateTimeOffset ExpiraEn { get; set; } = expiraEn;

        /// <summary>El inicio de la reserva: 15 minutos antes de su expiración original.</summary>
        public DateTimeOffset IniciadaEn { get; } = expiraEn - PlazosDelPago.VigenciaConImpresos;

        public IReadOnlyList<LineaConfirmadaDeReserva> Lineas { get; } = lineas;

        public EstadoDeReserva Estado { get; set; } = EstadoDeReserva.Pendiente;

        public bool Extendida { get; set; }

        public string? ClaveDeConfirmacion { get; set; }
    }
}

public static class ComposicionProvisionalDePagos
{
    /// <summary>
    /// Registra los dobles provisionales de lo que Pagos necesita y aún no existe (Identidad, Pedidos, el cliente real del Inventario y la pasarela) con <c>TryAdd</c>: <b>un registro real anterior se respeta</b>. Con
    /// <paramref name="esProduccion"/>, si cualquiera de esos puertos termina resuelto por un doble, el arranque falla nombrando cuál: nunca se publica con una pasarela, un Inventario o una confirmación simulados (D05).
    /// Las posesiones de ediciones y los avisos al checkout los registran Carrito y Biblioteca. Se llama <b>después</b> de registrar las implementaciones reales.
    /// </summary>
    public static IServiceCollection AgregarDoblesProvisionalesDePagos(this IServiceCollection servicios, bool esProduccion)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.TryAddSingleton<ICuentaDelPagador, CuentaDelPagadorSinIdentidad>();
        servicios.TryAddSingleton<IConfirmacionDeCompra, ConfirmacionDeCompraSinPedidos>();
        servicios.TryAddSingleton<IPasarelaDePago, PasarelaDePagoSinProveedor>();
        servicios.TryAddSingleton<IReservasDelPago, ReservasSimuladasDelPago>();

        if (esProduccion)
        {
            ExigirQueNingunoSeaProvisional(servicios);
        }

        return servicios;
    }

    private static readonly (Type Puerto, Type Doble)[] Dobles =
    [
        (typeof(ICuentaDelPagador), typeof(CuentaDelPagadorSinIdentidad)),
        (typeof(IConfirmacionDeCompra), typeof(ConfirmacionDeCompraSinPedidos)),
        (typeof(IPasarelaDePago), typeof(PasarelaDePagoSinProveedor)),
        (typeof(IReservasDelPago), typeof(ReservasSimuladasDelPago)),
    ];

    private static void ExigirQueNingunoSeaProvisional(IServiceCollection servicios)
    {
        var provisionales = Dobles
            .Where(d => servicios.Any(s => s.ServiceType == d.Puerto && (s.ImplementationType == d.Doble || s.ImplementationInstance?.GetType() == d.Doble)))
            .Select(d => d.Puerto.Name)
            .ToList();
        if (provisionales.Count > 0)
        {
            throw new InvalidOperationException($"En producción no se permiten dobles provisionales de Pagos. Falta la implementación real de: {string.Join(", ", provisionales)}.");
        }
    }
}
