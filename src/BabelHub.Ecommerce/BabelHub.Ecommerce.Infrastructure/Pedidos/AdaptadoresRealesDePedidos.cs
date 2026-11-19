using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Infrastructure.Pedidos;

/// <summary>
/// Las reservas del pedido sobre el cliente del Inventario de <c>Plataforma</c> (<c>reservas.cerrar</c> y <c>reservas.liberar</c>; plan §4): <b>una llamada por línea</b> con la clave <c>cierre-&lt;pedido&gt;-&lt;línea&gt;</c> (el contrato lleva una sola clave por llamada, así que una clave repetida con otras líneas sería un
/// conflicto) y <c>motivo = entrega</c>. Este adaptador no toca el token ni abre un <c>HttpClient</c> (regla 9): el cliente pone el token del usuario en <c>cerrar</c> y la credencial de sistema en <c>liberar</c>. Una falla del Inventario nunca es una excepción: sin respuesta, 429, una clave en curso o en conflicto
/// y una respuesta que no nombra la línea pedida son <b>inciertas</b> (se reintenta con la misma clave y descuenta una sola vez); <c>INV-RESERVA-ESTADO-INVALIDO</c> significa que la línea ya salió por otro camino.
/// </summary>
public sealed class RestReservasDelPedido(IClienteDelInventario cliente) : IReservasDelPedido
{
    public const string MotivoDeEntrega = "entrega";

    private const string ReservaEstadoInvalido = "INV-RESERVA-ESTADO-INVALIDO";
    private const string NoEncontrado = "INV-NO-ENCONTRADO";

    public async Task<ResultadoDeCerrarReserva> Cerrar(Guid pedidoId, Guid lineaId, string claveDeCierre, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claveDeCierre);
        ResultadoDelInventario<CierreDeLaReserva> resultado;
        try
        {
            resultado = await cliente.CerrarReserva(pedidoId, MotivoDeEntrega, [lineaId], null, claveDeCierre, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return ResultadoDeCerrarReserva.Incierto;
        }

        switch (resultado.Estado)
        {
            case EstadoDeLlamadaAlInventario.Exitosa:
                var propia = resultado.Valor.Lineas?.Where(l => l is not null && l.LineaId == lineaId).ToList();
                if (resultado.Valor.PedidoId != pedidoId || propia is not { Count: 1 })
                {
                    // Una respuesta que no nombra exactamente la línea pedida se trata como incierta: nunca como cierre (SEG-13).
                    return ResultadoDeCerrarReserva.Incierto;
                }

                return propia[0].Resultado switch
                {
                    "cerrada" => ResultadoDeCerrarReserva.Cerrada,
                    "ya-cerrada" => ResultadoDeCerrarReserva.YaCerrada,
                    _ => ResultadoDeCerrarReserva.Incierto,
                };
            case EstadoDeLlamadaAlInventario.Rechazada when resultado.Codigo == ReservaEstadoInvalido:
                return ResultadoDeCerrarReserva.YaSalio;
            case EstadoDeLlamadaAlInventario.Rechazada when resultado.Codigo is "INV-IDEMPOTENCIA-EN-CURSO" or "INV-IDEMPOTENCIA-EN-CONFLICTO" or "INV-PROHIBIDO" or "INV-NO-AUTENTICADO"
                || resultado.EstadoHttp is 401 or 403 or 408:
                return ResultadoDeCerrarReserva.Incierto;
            case EstadoDeLlamadaAlInventario.Rechazada:
                return ResultadoDeCerrarReserva.Rechazada;
            default:
                return ResultadoDeCerrarReserva.Incierto;
        }
    }

    public async Task<ResultadoDeLiberarReserva> Liberar(Guid pedidoId, string motivo, IReadOnlyList<Guid> lineaIds, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        ArgumentNullException.ThrowIfNull(lineaIds);
        ResultadoDelInventario<ReservaDelInventario> resultado;
        try
        {
            resultado = await cliente.Liberar(pedidoId, motivo, lineaIds, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return ResultadoDeLiberarReserva.NoSePudo;
        }

        return resultado.Estado switch
        {
            EstadoDeLlamadaAlInventario.Exitosa => ResultadoDeLiberarReserva.Liberada,
            EstadoDeLlamadaAlInventario.Rechazada when resultado.Codigo == NoEncontrado => ResultadoDeLiberarReserva.NoExistia,
            EstadoDeLlamadaAlInventario.Rechazada when resultado.Codigo == ReservaEstadoInvalido => ResultadoDeLiberarReserva.YaCerrada,
            _ => ResultadoDeLiberarReserva.NoSePudo,
        };
    }
}

/// <summary>
/// Los límites de Pedidos sobre <see cref="IControlDeLimites"/> y sus políticas de <c>Plataforma</c> (T10; SEG-05, SEG-07, SEG-26): consultas, 60 por usuario y minuto; gestión, 30 por usuario y minuto; y lecturas del detalle con contacto, 200 por persona y hora, <b>crítica y que falla cerrada</b>. Ante una excepción o sin
/// respuesta del almacén (<c>NoDisponible</c>) ninguna de las tres se sirve: la lectura del contacto se oculta y el resto responde «servicio no disponible».
/// </summary>
public sealed class LimitadorDePedidosSobreLimites(IControlDeLimites limites) : ILimitadorDePedidos
{
    public Task<ResultadoDeLimiteDePedidos> Consultar(Guid usuarioId, CancellationToken cancelacion = default) => Reservar(PoliticasDeLimite.ConsultasDePedidosPorUsuario, usuarioId, cancelacion);

    public Task<ResultadoDeLimiteDePedidos> Gestionar(Guid usuarioId, CancellationToken cancelacion = default) => Reservar(PoliticasDeLimite.GestionDePedidosPorUsuario, usuarioId, cancelacion);

    public Task<ResultadoDeLimiteDePedidos> LeerContacto(Guid usuarioId, CancellationToken cancelacion = default) => Reservar(PoliticasDeLimite.LecturasDelContactoDePedidosPorPersona, usuarioId, cancelacion);

    private async Task<ResultadoDeLimiteDePedidos> Reservar(ClaveDePolitica politica, Guid usuarioId, CancellationToken cancelacion)
    {
        ResultadoDeLimite resultado;
        try
        {
            resultado = await limites.Reservar(politica, usuarioId.ToString("N"), cancelacion: cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return ResultadoDeLimiteDePedidos.NoDisponible;
        }

        return resultado.Estado switch
        {
            EstadoDeLimite.Permitido => ResultadoDeLimiteDePedidos.Permitido,
            EstadoDeLimite.Excedido => new ResultadoDeLimiteDePedidos(DecisionDeLimiteDePedidos.Excedido, resultado.DisponibleDesde),
            _ => ResultadoDeLimiteDePedidos.NoDisponible,
        };
    }
}

/// <summary>
/// La auditoría de la lectura del contacto por el personal sobre <see cref="IAuditoria"/> (PED-40, D09): registra <c>ContactoDePedidoConsultado</c> con el pedido como objeto y el rol en el detalle, <b>nunca el contacto</b>. Si no se puede auditar, la excepción sube y el detalle se muestra sin contacto.
/// </summary>
public sealed class AuditoriaDeContactoSobreAuditoria(IAuditoria auditoria) : IAuditoriaDeContacto
{
    public Task ContactoConsultado(Guid pedidoId, string rol, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rol);
        return auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.ContactoDePedidoConsultado)
            {
                ObjetoTipo = "pedido",
                ObjetoId = pedidoId.ToString("D"),
                Detalle = new Dictionary<string, string?> { ["rol"] = rol },
            },
            cancelacion);
    }
}
