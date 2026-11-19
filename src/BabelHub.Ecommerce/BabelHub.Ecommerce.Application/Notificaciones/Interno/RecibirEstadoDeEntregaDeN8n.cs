using System.Security.Cryptography;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Notificaciones.Interno;

/// <summary>
/// La entrada firmada de estados de entrega de n8n (T19; NOT-06, NOT-12, AC-NOT-11): <b>solo adelanta lo verificable</b>. <c>Plataforma</c> verifica la firma con la clave del sentido entrante (HMAC sobre «{marca}.{cuerpo}»), la marca de
/// tiempo (5 minutos) y el tope del cuerpo, y deduplica por <c>avisoId</c> y estado; lo no verificable es un 401 uniforme con su evento de seguridad (AUD-37). El estado debe ser de un conjunto cerrado (<c>entregado</c> o <c>fallido</c>):
/// uno desconocido o un cuerpo mal formado se descarta sin efecto y de forma idempotente (SEG-13). Todo ocurre <b>en una sola transacción</b>: si algo falla se revierte también el reclamo y n8n reintenta. Un aviso que no existe se acepta
/// sin efecto y sin revelar nada.
/// </summary>
public sealed class RecibirEstadoDeEntregaDeN8n(IProcesadorDeEntradasDeIntegracion procesador, IRegistroDeEstadoDeEntrega registro, IUnidadDeTrabajo unidad, IReloj reloj) : IRecepcionDeEstadosDeEntrega
{
    /// <summary>El proveedor de la lista blanca de entradas de integración y de los secretos de <c>Plataforma</c> (<c>Integraciones:n8n:Secreto</c>).</summary>
    public const string Proveedor = "n8n";

    public const string Entregado = "entregado";

    public const string Fallido = "fallido";

    private const string DesenlaceRegistrado = "registrado";

    private const string DesenlaceDescartado = "descartado";

    public async Task<Resultado<ConfirmacionDeNotificaciones>> Recibir(EstadoDeEntregaFirmadoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var estado = Interpretar(entrada.Cuerpo.Span);

        // El identificador de la entrada es el aviso y el estado; un cuerpo que no se entiende usa su resumen, para que igual se verifique la firma y se deje el evento.
        // Un «entregado» es terminal y se deduplica por aviso; cada «fallido» es un intento distinto, así que su identificador lleva la marca firmada: repetir el mismo mensaje no cuenta dos veces, pero un segundo fallo del siguiente reintento sí llega (AC-NOT-12).
        var marca = new string(entrada.MarcaDeTiempo.Where(char.IsAsciiDigit).Take(20).ToArray());
        var idDeEntrada = estado is { } valido
            ? valido.Estado == Fallido ? $"{valido.AvisoId:N}.{valido.Estado}.{marca}" : $"{valido.AvisoId:N}.{valido.Estado}"
            : "ilegible." + Convert.ToHexString(SHA256.HashData(entrada.Cuerpo.Span))[..32];
        try
        {
            return await unidad.Ejecutar(token => RecibirEnLaTransaccion(estado, idDeEntrada, entrada, token), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // La transacción se revirtió (también el reclamo de la entrada): n8n reintenta.
            return Resultado<ConfirmacionDeNotificaciones>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"));
        }
    }

    private async Task<Resultado<ConfirmacionDeNotificaciones>> RecibirEnLaTransaccion(EstadoLeido? estado, string idDeEntrada, EstadoDeEntregaFirmadoEntrada entrada, CancellationToken token)
    {
        var recibida = await procesador.Recibir(new EntradaDeIntegracion(Proveedor, idDeEntrada, entrada.MarcaDeTiempo, entrada.Cuerpo, entrada.Firma), token).ConfigureAwait(false);
        switch (recibida.Estado)
        {
            case EstadoDeEntradaDeIntegracion.Rechazada:
                return Resultado<ConfirmacionDeNotificaciones>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, "no-autenticado"));
            case EstadoDeEntradaDeIntegracion.EnCurso:
                return Resultado<ConfirmacionDeNotificaciones>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"));
            case EstadoDeEntradaDeIntegracion.Repetida:
                return recibida.ResultadoGuardado?.Desenlace == DesenlaceDescartado ? Descartado() : Exito();
        }

        if (estado is not { } leido)
        {
            // Firmado pero ilegible o de un estado fuera del conjunto cerrado: se descarta sin efecto (SEG-13) y repetirlo responde lo mismo.
            await procesador.Completar(Proveedor, idDeEntrada, new ResultadoMinimo(DesenlaceDescartado), token).ConfigureAwait(false);
            return Descartado();
        }

        // Un aviso que no existe (o ya terminado) se acepta sin efecto: no se revela cuáles existen.
        _ = await registro.Registrar(leido.AvisoId, leido.Estado == Entregado ? EstadoDeEntrega.Entregado : EstadoDeEntrega.Fallido, reloj.Ahora, token).ConfigureAwait(false);
        await procesador.Completar(Proveedor, idDeEntrada, new ResultadoMinimo(DesenlaceRegistrado), token).ConfigureAwait(false);
        return Exito();
    }

    private static Resultado<ConfirmacionDeNotificaciones> Exito() => Resultado<ConfirmacionDeNotificaciones>.Exito(new ConfirmacionDeNotificaciones());

    private static Resultado<ConfirmacionDeNotificaciones> Descartado() =>
        Resultado<ConfirmacionDeNotificaciones>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, "validacion-invalida", [new ErrorDeCampo("estado", "invalido")]));

    private readonly record struct EstadoLeido(Guid AvisoId, string Estado);

    /// <summary>Lee <c>{"avisoId": "...", "estado": "entregado|fallido"}</c> sin fiarse de nada: otra forma, otro estado o un identificador vacío es «ilegible».</summary>
    private static EstadoLeido? Interpretar(ReadOnlySpan<byte> cuerpo)
    {
        try
        {
            var lector = new Utf8JsonReader(cuerpo);
            using var documento = JsonDocument.ParseValue(ref lector);
            var raiz = documento.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object
                || !raiz.TryGetProperty("avisoId", out var id) || id.ValueKind != JsonValueKind.String || !Guid.TryParse(id.GetString(), out var avisoId) || avisoId == Guid.Empty
                || !raiz.TryGetProperty("estado", out var estado) || estado.ValueKind != JsonValueKind.String || estado.GetString() is not (Entregado or Fallido) and not null)
            {
                return null;
            }

            return new EstadoLeido(avisoId, estado.GetString()!);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
