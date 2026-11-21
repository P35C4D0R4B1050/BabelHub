using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;

namespace BabelHub.Ecommerce.Application.Biblioteca.Interno;

internal static class AuditoriaDeBiblioteca
{
    public const string TipoDeObjeto = "acceso-digital";

    /// <summary>La revocación de un acceso (BIB-24): quién (el actor del contexto) y por qué (la causa de lista cerrada); nunca la URL ni la clave del archivo.</summary>
    public static SolicitudDeRegistro Revocado(Acceso acceso, CausaDeRevocacion causa) =>
        new(AccionesAuditables.AccesoDigitalRevocado)
        {
            ObjetoTipo = TipoDeObjeto,
            ObjetoId = acceso.Id.ToString(),
            Detalle = new Dictionary<string, string?> { ["edicionId"] = acceso.EdicionId.ToString(), ["causa"] = causa.Codigo() },
        };
}

/// <summary>
/// <c>IAccesosDeBiblioteca.Conceder</c> (T04; BIB-01, BIB-19, BIB-20): corre <b>dentro de la transacción del llamador</b> (la de Pagos al confirmar el cobro) con una sola sentencia condicional (plan §1). No reserva stock, no consulta al Inventario
/// (regla 12) ni la licencia de la edición (CAR-14), y no audita (la concesión no es una operación sensible, D12). Un fallo de la base propaga la excepción para que el llamador revierta.
/// </summary>
internal sealed class ConcederAcceso(IRepositorioDeAccesos accesos, IReloj reloj) : ICasoDeUso<ConcederAccesoEntrada, ResultadoDeConcederAcceso>
{
    public async Task<Resultado<ResultadoDeConcederAcceso>> Ejecutar(ConcederAccesoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (entrada.CuentaId == Guid.Empty || entrada.PedidoId == Guid.Empty || entrada.EdicionId == Guid.Empty)
        {
            return FallosDeBiblioteca.Validacion<ResultadoDeConcederAcceso>();
        }

        try
        {
            var resultado = await accesos.Conceder(entrada.CuentaId, entrada.EdicionId, entrada.PedidoId, reloj.Ahora, cancelacion).ConfigureAwait(false);
            return Resultado<ResultadoDeConcederAcceso>.Exito(resultado switch
            {
                ResultadoDeConcesion.Concedido => ResultadoDeConcederAcceso.Concedido,
                ResultadoDeConcesion.Reactivado => ResultadoDeConcederAcceso.Reactivado,
                ResultadoDeConcesion.YaConcedidoPorEstePedido => ResultadoDeConcederAcceso.YaConcedidoPorEstePedido,
                ResultadoDeConcesion.YaPoseidoPorOtraCompra => ResultadoDeConcederAcceso.YaPoseidoPorOtraCompra,
                _ => throw new ArgumentOutOfRangeException(nameof(entrada)),
            });
        }
        catch (BibliotecaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<ResultadoDeConcederAcceso>();
        }
    }
}

/// <summary>
/// <c>IAccesosDeBiblioteca.Revocar</c> (T04; BIB-24, D16): idempotente y casado por pedido y edición (una orden tardía de un pedido viejo tras una recompra no toca el acceso nuevo). Audita <b>solo la primera revocación</b> en la misma transacción del llamador:
/// si ya se revocó pero no se puede auditar, <b>la excepción se propaga</b> para que el llamador revierta todo (una revocación sin auditoría no se aplica).
/// </summary>
internal sealed class RevocarAcceso(IRepositorioDeAccesos accesos, IAuditoria auditoria, IReloj reloj) : ICasoDeUso<RevocarAccesoEntrada, ResultadoDeRevocarAcceso>
{
    public async Task<Resultado<ResultadoDeRevocarAcceso>> Ejecutar(RevocarAccesoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (entrada.PedidoId == Guid.Empty || entrada.EdicionId == Guid.Empty || !Enum.IsDefined(entrada.Motivo))
        {
            return FallosDeBiblioteca.Validacion<ResultadoDeRevocarAcceso>();
        }

        var causa = MapeoDeBiblioteca.Causa(entrada.Motivo);
        CambioDeAcceso<ResultadoDeRevocacion> cambio;
        try
        {
            cambio = await accesos.Revocar(entrada.PedidoId, entrada.EdicionId, causa, reloj.Ahora, cancelacion).ConfigureAwait(false);
        }
        catch (BibliotecaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<ResultadoDeRevocarAcceso>();
        }

        if (cambio.Resultado != ResultadoDeRevocacion.Revocado || cambio.Acceso is null)
        {
            return Resultado<ResultadoDeRevocarAcceso>.Exito(ResultadoDeRevocarAcceso.YaRevocado);
        }

        // Ya hay un efecto: si la auditoría cae, la excepción sube y el llamador revierte la transacción.
        await auditoria.Registrar(AuditoriaDeBiblioteca.Revocado(cambio.Acceso, causa), cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeRevocarAcceso>.Exito(ResultadoDeRevocarAcceso.Revocado);
    }
}

/// <summary>La implementación de <see cref="IAccesosDeBiblioteca"/> (de Pedidos) por los casos de uso de Biblioteca: ambas operaciones participan en la unidad de trabajo del llamador.</summary>
internal sealed class AccesosDeBibliotecaDeLaAplicacion(ConcederAcceso conceder, RevocarAcceso revocar, IRepositorioDeAccesos accesos) : IAccesosDeBiblioteca
{
    public async Task<Resultado<bool>> EstaRevocadoPorReembolso(Guid cuentaId, Guid pedidoId, Guid edicionId, CancellationToken cancelacion = default)
    {
        if (cuentaId == Guid.Empty || pedidoId == Guid.Empty || edicionId == Guid.Empty)
        {
            return FallosDeBiblioteca.Validacion<bool>();
        }

        try
        {
            var acceso = await accesos.ObtenerDeLaCompra(cuentaId, pedidoId, edicionId, cancelacion).ConfigureAwait(false);
            return Resultado<bool>.Exito(acceso is { Estado: EstadoDeAcceso.Revocado, CausaDeRevocacion: CausaDeRevocacion.Reembolso or CausaDeRevocacion.ReporteAprobado });
        }
        catch (BibliotecaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<bool>();
        }
    }


    public Task<Resultado<ResultadoDeConcederAcceso>> Conceder(ConcederAccesoEntrada entrada, CancellationToken cancelacion = default) => conceder.Ejecutar(entrada, cancelacion);

    public Task<Resultado<ResultadoDeRevocarAcceso>> Revocar(RevocarAccesoEntrada entrada, CancellationToken cancelacion = default) => revocar.Ejecutar(entrada, cancelacion);
}

/// <summary>
/// <c>IPosesionDeEdiciones.Posee</c> (T04; CAR-20, D10, DD-06): lee <b>sin bloqueo</b> los accesos vigentes o suspendidos de la cuenta (un revocado no cuenta, BIB-20). Ante cualquier fallo de lectura responde «no se puede confirmar»
/// (Carrito falla cerrado al pasar al pago). No consulta la licencia ni el Inventario.
/// </summary>
internal sealed class ComprobarPosesion(IRepositorioDeAccesos accesos) : IPosesionDeEdiciones
{
    public async Task<ResultadoDePosesion> Posee(Guid cuentaId, IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);
        if (cuentaId == Guid.Empty)
        {
            return ResultadoDePosesion.NoSePuedeConfirmar;
        }

        if (edicionIds.Count == 0)
        {
            return ResultadoDePosesion.NoPosee;
        }

        try
        {
            var poseidas = await accesos.EdicionesPoseidas(cuentaId, edicionIds, cancelacion).ConfigureAwait(false);
            return ResultadoDePosesion.Posee([.. poseidas]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ResultadoDePosesion.NoSePuedeConfirmar;
        }
    }
}
