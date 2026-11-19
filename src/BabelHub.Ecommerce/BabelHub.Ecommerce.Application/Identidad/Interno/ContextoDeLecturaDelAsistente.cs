using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>Lo que firma el contexto de lectura (IDE-33): el cliente, para quién vale, qué tipo es, su único alcance y su vida de 5 minutos.</summary>
public sealed record ReclamosDelContexto(Guid ClienteId, string Audiencia, string Tipo, string Alcance, DateTimeOffset EmitidoEn, DateTimeOffset VenceEn, string Identificador);

/// <summary>
/// La firma del contexto de lectura (T32 la implementa con ES256 y la clave activa): <see cref="Firmar"/> devuelve el token y <see cref="Verificar"/> devuelve los reclamos <b>solo si la firma es válida</b> con el
/// algoritmo fijado y una clave no revocada; si no, <see langword="null"/>. La vigencia y los reclamos los decide la Aplicación.
/// </summary>
public interface IFirmaDeContextosDeLectura
{
    Task<string> Firmar(ReclamosDelContexto reclamos, CancellationToken cancelacion = default);

    Task<ReclamosDelContexto?> Verificar(string token, CancellationToken cancelacion = default);
}

/// <summary>
/// El contexto de lectura del asistente (T26; IDE-33, AC-IDE-30): se emite <b>solo para el propio cliente autenticado</b> con la cuenta vigente; vence a los <b>5 minutos exactos</b>, <b>no se renueva</b> (no hay forma de
/// pedirle otro con él), vale solo para <c>pedidos</c> (<c>aud</c>), es de tipo <c>lectura-asistente</c> y su único alcance es leer los pedidos de ese cliente: escribir, leer a otro, la biblioteca, la cuenta o la descarga
/// quedan fuera. Al verificarlo, <b>cualquier fallo devuelve el mismo error</b> (no es un oráculo de cuál comprobación falló) y la cuenta debe seguir vigente.
/// </summary>
public sealed class ContextoDeLecturaDelAsistente(
    AlcanceDeLaCuenta alcance,
    IEstadoDeCuenta estadoDeCuenta,
    IFirmaDeContextosDeLectura firma,
    IReloj reloj) : IContextoDeLectura
{
    public const string AudienciaDePedidos = "pedidos";

    public const string TipoDelContexto = "lectura-asistente";

    public const string AlcanceDeLeerPedidos = "leer-pedidos";

    public static readonly TimeSpan Vida = TimeSpan.FromMinutes(5);

    public static ErrorDeAplicacion ContextoInvalido { get; } = new(CategoriaDeError.NoAutenticado, "contexto-invalido");

    public async Task<Resultado<ValorSecreto>> Emitir(Guid clienteId, CancellationToken cancelacion = default)
    {
        if (clienteId == Guid.Empty)
        {
            return Resultado<ValorSecreto>.Fallo(AlcanceDeLaCuenta.Prohibido);
        }

        if (await alcance.Comprobar(clienteId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<ValorSecreto>.Fallo(prohibido);
        }

        if (!await CuentaVigente(clienteId, cancelacion).ConfigureAwait(false))
        {
            return Resultado<ValorSecreto>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
        }

        var ahora = reloj.Ahora;
        var reclamos = new ReclamosDelContexto(clienteId, AudienciaDePedidos, TipoDelContexto, AlcanceDeLeerPedidos, ahora, ahora + Vida, Guid.NewGuid().ToString("N"));
        var token = await firma.Firmar(reclamos, cancelacion).ConfigureAwait(false);
        return Resultado<ValorSecreto>.Exito(new ValorSecreto(token));
    }

    public async Task<Resultado<ClienteDelContexto>> Verificar(ValorSecreto contexto, CancellationToken cancelacion = default)
    {
        var invalido = Resultado<ClienteDelContexto>.Fallo(ContextoInvalido);
        if (contexto is null)
        {
            return invalido;
        }

        ReclamosDelContexto? reclamos;
        try
        {
            reclamos = await firma.Verificar(contexto.Revelar(), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return invalido;
        }

        if (reclamos is null || !ReclamosValidos(reclamos, reloj.Ahora) || !await CuentaVigente(reclamos.ClienteId, cancelacion).ConfigureAwait(false))
        {
            return invalido;
        }

        return Resultado<ClienteDelContexto>.Exito(new ClienteDelContexto(reclamos.ClienteId));
    }

    /// <summary>Audiencia, tipo y alcance exactos; vida de a lo sumo 5 minutos (un contexto más largo es una renovación o una falsificación); emitido no en el futuro y aún no vencido.</summary>
    private static bool ReclamosValidos(ReclamosDelContexto r, DateTimeOffset ahora) =>
        r.ClienteId != Guid.Empty
        && string.Equals(r.Audiencia, AudienciaDePedidos, StringComparison.Ordinal)
        && string.Equals(r.Tipo, TipoDelContexto, StringComparison.Ordinal)
        && string.Equals(r.Alcance, AlcanceDeLeerPedidos, StringComparison.Ordinal)
        && r.VenceEn > r.EmitidoEn
        && r.VenceEn - r.EmitidoEn <= Vida
        && r.EmitidoEn <= ahora
        && ahora < r.VenceEn;

    private async Task<bool> CuentaVigente(Guid clienteId, CancellationToken cancelacion)
    {
        try
        {
            var estado = await estadoDeCuenta.Consultar(clienteId, cancelacion).ConfigureAwait(false);
            return estado is { CuentaCanceladaODesactivada: false };
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Falla cerrada: sin conocer el estado de la cuenta no se emite ni se acepta un contexto.
            return false;
        }
    }
}
