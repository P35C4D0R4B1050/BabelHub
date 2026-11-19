using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>
/// La entrada de los estados de traslado desde el Inventario (T14; PED-19, AC-PED-03): <c>en-transito</c>, <c>recibido</c> y <c>cancelado</c>. Orden: (1) el <c>eventoId</c> debe ser <c>traslado:&lt;trasladoId&gt;:&lt;estado&gt;</c> y coincidir con el cuerpo; (2) es <b>idempotente por <c>eventoId</c></b>: el mismo evento con el mismo cuerpo
/// responde sin cambios y con otro cuerpo se rechaza; (3) el pedido se lee <b>con bloqueo</b> y se <b>cruzan</b> el traslado, el pedido, la edición y la cantidad con lo guardado al crearlo (uno que no coincide es validación; un pedido o traslado inexistente es <c>no encontrado</c>, un error <b>permanente</b> con alerta
/// técnica porque el buzón del Inventario no reintenta un 404); (4) una línea con una operación <c>en curso</c> responde «servicio no disponible» (reintentable); (5) una línea ya anulada responde sin efecto y con alerta, salvo el <c>cancelado</c> que provocó la propia liberación; (6) solo transiciones monótonas (un
/// <c>recibido</c> sin <c>en-transito</c> previo recorre ambos) con la fecha estimada actualizada. Si el estado del pedido cambia se audita y se avisa al cliente y a la sucursal responsable (evento = el <c>eventoId</c> del traslado). Todo ocurre en una transacción corta.
/// </summary>
public sealed class RecibirEstadoDeTraslado(
    IRepositorioDePedidos pedidos,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IPublicadorDeAlertasTecnicas alertas,
    Notificaciones.INotificador notificador,
    IReloj reloj) : IReceptorDeEstadosDeTraslado
{
    public const string AlertaPedidoInexistente = "pedido-estado-de-traslado-de-un-pedido-o-linea-inexistente";
    public const string AlertaLineaAnulada = "pedido-estado-de-traslado-de-una-linea-anulada";

    public async Task<Resultado<bool>> Recibir(EstadoDeTrasladoRecibido estado, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(estado);
        if (Validar(estado) is { } invalido)
        {
            return Resultado<bool>.Fallo(invalido);
        }

        var huella = Huella(estado);
        (Resultado<bool> Resultado, string? Alerta) salida;
        try
        {
            salida = await unidad.Ejecutar(token => Aplicar(estado, huella, token), cancelacion).ConfigureAwait(false);
        }
        catch (EsperaDeBloqueoDeUnPedidoAgotadaException)
        {
            return Resultado<bool>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.Conflicto));
        }

        if (salida.Alerta is { } motivo)
        {
            await PublicarAlerta(motivo, cancelacion).ConfigureAwait(false);
        }

        return salida.Resultado;
    }

    private async Task<(Resultado<bool>, string?)> Aplicar(EstadoDeTrasladoRecibido estado, string huella, CancellationToken cancelacion)
    {
        var previo = await pedidos.ObtenerEstadoDeTraslado(estado.EventoId, cancelacion).ConfigureAwait(false);
        if (previo is not null)
        {
            // La misma entrega repetida: 204. Otro cuerpo con el mismo evento: validación.
            return previo.HuellaDelCuerpo == huella
                ? (Resultado<bool>.Exito(true), null)
                : (Resultado<bool>.Fallo(Validacion("eventoId")), null);
        }

        var pedido = await pedidos.Obtener(estado.PedidoId, conBloqueo: true, cancelacion).ConfigureAwait(false);
        var linea = pedido?.Lineas.FirstOrDefault(l => l.TrasladoId == estado.TrasladoId);
        if (pedido is null || linea is null)
        {
            return (Resultado<bool>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, ClavesDePedidos.NoEncontrado)), AlertaPedidoInexistente);
        }

        // El cruce: la edición y la cantidad deben ser las guardadas al crear el pedido.
        if (linea.EdicionId != estado.EdicionId || linea.Cantidad != estado.Cantidad || linea.EsDigital)
        {
            return (Resultado<bool>.Fallo(Validacion("lineaId")), null);
        }

        if ((await pedidos.AnulacionesEnCurso(pedido.Id, cancelacion).ConfigureAwait(false)).Any(a => a.BloqueaLasLineas && a.LineaIds.Contains(linea.Id))
            || (await pedidos.EntregasEnCurso(pedido.Id, cancelacion).ConfigureAwait(false)).Any(e => e.BloqueaLasLineas && e.Lineas.Any(l => l.LineaId == linea.Id)))
        {
            return (Resultado<bool>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible)), null);
        }

        var ahora = reloj.Ahora;
        string? alerta = null;
        var anterior = pedido.Estado;
        if (linea.EstadoImpreso == EstadoDeLineaImpresa.Anulado)
        {
            // Sin efecto; el «cancelado» de la propia liberación de una anulación no es una anomalía.
            alerta = estado.Estado == EstadoDelTrasladoInformado.Cancelado ? null : AlertaLineaAnulada;
        }
        else if (estado.Estado == EstadoDelTrasladoInformado.Cancelado)
        {
            alerta = AlertaLineaAnulada;
        }
        else
        {
            var destino = estado.Estado == EstadoDelTrasladoInformado.EnTransito ? EstadoDeLineaImpresa.EnTransito : EstadoDeLineaImpresa.Recibido;
            pedido.RegistrarTraslado(linea.Id, destino, estado.FechaEstimadaDeLlegada, ahora);
            await pedidos.Actualizar(pedido, cancelacion).ConfigureAwait(false);
        }

        await pedidos.GuardarEstadoDeTraslado(new EstadoDeTrasladoRegistrado(estado.EventoId, estado.TrasladoId, estado.PedidoId, NombreDelEstado(estado.Estado), huella, ahora), cancelacion).ConfigureAwait(false);
        if (pedido.Estado != anterior)
        {
            await Auditar(pedido, anterior, cancelacion).ConfigureAwait(false);
            await new AvisosDePedidos(notificador).Solicitar(pedido, AvisosDePedidos.NombreDelEstado(pedido.Estado), estado.EventoId.Replace(':', '-'), AvisosDePedidos.SucursalesResponsables(pedido, [linea.Id]), cancelacion).ConfigureAwait(false);
        }

        return (Resultado<bool>.Exito(true), alerta);
    }

    private Task Auditar(Pedido pedido, EstadoDePedido anterior, CancellationToken cancelacion) =>
        auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.EstadoDePedidoCambiado)
            {
                ObjetoTipo = "pedido",
                ObjetoId = pedido.Id.ToString("D"),
                Detalle = new Dictionary<string, string?> { ["estadoAnterior"] = AvisosDePedidos.NombreDelEstado(anterior), ["estadoNuevo"] = AvisosDePedidos.NombreDelEstado(pedido.Estado) },
            },
            cancelacion);

    private async Task PublicarAlerta(string motivo, CancellationToken cancelacion)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(motivo, reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            // Una alerta que no se publica nunca cambia la respuesta de la entrada.
        }
    }

    private static ErrorDeAplicacion? Validar(EstadoDeTrasladoRecibido estado)
    {
        if (estado.TrasladoId == Guid.Empty || estado.PedidoId == Guid.Empty || estado.EdicionId == Guid.Empty)
        {
            return Validacion("identificadores");
        }

        if (!Enum.IsDefined(estado.Estado) || estado.Cantidad < 1)
        {
            return Validacion("cantidad");
        }

        return string.Equals(estado.EventoId, $"traslado:{estado.TrasladoId:D}:{NombreDelEstado(estado.Estado)}", StringComparison.Ordinal) ? null : Validacion("eventoId");
    }

    private static ErrorDeAplicacion Validacion(string campo) =>
        new(CategoriaDeError.Validacion, ClavesDePedidos.ValidacionFallida, [new ErrorDeCampo(campo, "invalido")]);

    private static string NombreDelEstado(EstadoDelTrasladoInformado estado) => estado switch
    {
        EstadoDelTrasladoInformado.EnTransito => "en-transito",
        EstadoDelTrasladoInformado.Recibido => "recibido",
        _ => "cancelado",
    };

    private static string Huella(EstadoDeTrasladoRecibido estado) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(
            CultureInfo.InvariantCulture, $"{estado.TrasladoId:N}|{estado.PedidoId:N}|{estado.EdicionId:N}|{estado.Cantidad}|{NombreDelEstado(estado.Estado)}|{estado.FechaEstimadaDeLlegada:O}")))).ToLowerInvariant();
}
