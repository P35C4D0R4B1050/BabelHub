using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

/// <summary>Las claves simbólicas de los fallos del módulo; la Api las traduce a los códigos <c>ECO-PAGO-…</c> y <c>ECO-…</c> (plan §4).</summary>
internal static class ClavesDePagos
{
    public const string ValidacionFallida = "validacion-fallida";
    public const string NoEncontrado = "no-encontrado";
    public const string NoAutenticado = "no-autenticado";

    /// <summary><c>ECO-PAGO-RESERVA-EXPIRADA</c> (409).</summary>
    public const string ReservaExpirada = "reserva-expirada";

    /// <summary><c>ECO-PAGO-INTENCION-CANCELADA</c> (409).</summary>
    public const string IntencionCancelada = "intencion-cancelada";

    /// <summary><c>ECO-PAGO-INSTANTANEA-OBSOLETA</c> (409): el carrito cambió; la pantalla dice «tu carrito cambió».</summary>
    public const string InstantaneaObsoleta = "instantanea-obsoleta";

    /// <summary><c>ECO-PAGO-TOTAL-CAMBIO</c> (409).</summary>
    public const string TotalCambio = "total-cambio";

    /// <summary><c>ECO-PAGO-EN-CURSO</c> (409).</summary>
    public const string PagoEnCurso = "pago-en-curso";

    /// <summary><c>ECO-PAGO-CUENTA-NO-PUEDE-COMPRAR</c> (403): el campo <c>motivo</c> lleva la razón.</summary>
    public const string CuentaNoPuedeComprar = "cuenta-no-puede-comprar";

    /// <summary><c>ECO-DIGITAL-YA-POSEIDA</c> (409).</summary>
    public const string DigitalYaPoseida = "digital-ya-poseida";

    /// <summary><c>ECO-PAGO-CLAVE-INVALIDA</c> (409).</summary>
    public const string ClaveInvalida = "clave-invalida";

    public const string LimiteExcedido = "limite-excedido";
    public const string EsperaEnSegundos = "espera-en-segundos";
    public const string ServicioNoDisponible = "servicio-no-disponible";
}

internal static class FallosDePagos
{
    public static Resultado<T> De<T>(CategoriaDeError categoria, string clave, IReadOnlyList<ErrorDeCampo>? errores = null) =>
        Resultado<T>.Fallo(new ErrorDeAplicacion(categoria, clave, errores));
}

/// <summary>Las claves de firma de los tokens de intento (<c>BABELHUB__Pagos__ClaveDeIntentos</c> y su valor anterior, para rotarla). No se imprimen.</summary>
internal sealed class OpcionesDeClavesDeIntento
{
    public OpcionesDeClavesDeIntento(byte[] actual, byte[]? anterior = null)
    {
        ArgumentNullException.ThrowIfNull(actual);
        if (actual.Length < 32)
        {
            throw new ArgumentException("La clave de intentos tiene al menos 32 bytes.", nameof(actual));
        }

        Actual = actual;
        Anterior = anterior;
    }

    public byte[] Actual { get; }

    public byte[]? Anterior { get; }

    public override string ToString() => "OpcionesDeClavesDeIntento { (secreto) }";
}

/// <summary>Lo que lleva la clave de un intento (D04): el intento (clave de idempotencia y referencia opaca), la intención, la cuenta, la huella de la instantánea, el total en céntimos y su vigencia.</summary>
internal sealed record DatosDeLaClaveDeIntento(Guid IntentoId, Guid PedidoId, Guid CuentaId, string HuellaDeInstantanea, long TotalEnCentimos, DateTimeOffset ExpiraEn);

internal enum ResultadoDeVerificarClave
{
    Valida,
    Invalida,
    Vencida,
}

/// <summary>
/// Emite y verifica la clave de un intento (D04, PAG-04, PAG-16): un token firmado con HMAC-SHA256 que se emite <b>sin escribir nada</b>, con comparación en tiempo constante y con la clave actual y su valor anterior
/// (rotación). Ligado a la cuenta, a la intención, a la huella de la instantánea y al total, y vigente 30 minutos. Un token alterado, de otra cuenta o vencido se rechaza.
/// </summary>
internal sealed class FirmadorDeClavesDeIntento(OpcionesDeClavesDeIntento opciones)
{
    private const string Version = "v1";

    private sealed record Carga(string I, string P, string C, string H, long T, long E);

    public string Emitir(DatosDeLaClaveDeIntento datos)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var carga = JsonSerializer.SerializeToUtf8Bytes(new Carga(datos.IntentoId.ToString("N"), datos.PedidoId.ToString("N"), datos.CuentaId.ToString("N"), datos.HuellaDeInstantanea, datos.TotalEnCentimos, datos.ExpiraEn.ToUnixTimeSeconds()));
        return $"{Version}.{Base64Url(carga)}.{Base64Url(Firmar(opciones.Actual, carga))}";
    }

    public ResultadoDeVerificarClave Verificar(string? token, DateTimeOffset ahora, out DatosDeLaClaveDeIntento? datos)
    {
        datos = null;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 1024)
        {
            return ResultadoDeVerificarClave.Invalida;
        }

        var partes = token.Split('.');
        if (partes.Length != 3 || partes[0] != Version)
        {
            return ResultadoDeVerificarClave.Invalida;
        }

        byte[] carga;
        byte[] firma;
        try
        {
            carga = DeBase64Url(partes[1]);
            firma = DeBase64Url(partes[2]);
        }
        catch (FormatException)
        {
            return ResultadoDeVerificarClave.Invalida;
        }

        var coincide = CryptographicOperations.FixedTimeEquals(firma, Firmar(opciones.Actual, carga));
        if (!coincide && opciones.Anterior is { } anterior)
        {
            coincide = CryptographicOperations.FixedTimeEquals(firma, Firmar(anterior, carga));
        }

        if (!coincide)
        {
            return ResultadoDeVerificarClave.Invalida;
        }

        Carga? leida;
        try
        {
            leida = JsonSerializer.Deserialize<Carga>(carga);
        }
        catch (JsonException)
        {
            return ResultadoDeVerificarClave.Invalida;
        }

        if (leida is null || !Guid.TryParseExact(leida.I, "N", out var intento) || !Guid.TryParseExact(leida.P, "N", out var pedido) || !Guid.TryParseExact(leida.C, "N", out var cuenta))
        {
            return ResultadoDeVerificarClave.Invalida;
        }

        datos = new DatosDeLaClaveDeIntento(intento, pedido, cuenta, leida.H, leida.T, DateTimeOffset.FromUnixTimeSeconds(leida.E));
        return datos.ExpiraEn <= ahora ? ResultadoDeVerificarClave.Vencida : ResultadoDeVerificarClave.Valida;
    }

    private static byte[] Firmar(byte[] clave, byte[] carga) => HMACSHA256.HashData(clave, carga);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DeBase64Url(string texto)
    {
        var relleno = texto.Replace('-', '+').Replace('_', '/');
        relleno = relleno.PadRight(relleno.Length + ((4 - (relleno.Length % 4)) % 4), '=');
        return Convert.FromBase64String(relleno);
    }
}

/// <summary>
/// El actor del pago: solo un cliente con sesión (la cuenta sale del actor, nunca de la solicitud). Un pedido ajeno responde igual que uno inexistente y deja un acceso denegado en cada intento (SEG-09).
/// </summary>
internal sealed class AutorizadorDePagos(IActorActual actores, IDenegacionDeAcceso denegaciones)
{
    public Guid? CuentaId => actores.Actual is { Tipo: TipoDeActor.Usuario, UsuarioId: { } id } ? id : null;

    public async Task<ErrorDeAplicacion?> ExigirSesion(CancellationToken cancelacion)
    {
        if (CuentaId is not null)
        {
            return null;
        }

        await Denegar("sin-sesion", "ECO-NO-AUTENTICADO", cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, ClavesDePagos.NoAutenticado);
    }

    /// <summary>Un token firmado por el servidor para otra cuenta u otro pedido, o un intento ajeno: la respuesta es uniforme pero queda el acceso denegado (SEG-09).</summary>
    public Task RegistrarClaveAjena(CancellationToken cancelacion) => Denegar("clave-ajena", "ECO-PAGO-CLAVE-INVALIDA", cancelacion);

    public Task<ErrorDeAplicacion> RechazarAjena(CancellationToken cancelacion) => RechazarAjenaAsync(cancelacion);

    private async Task<ErrorDeAplicacion> RechazarAjenaAsync(CancellationToken cancelacion)
    {
        await Denegar("recurso-ajeno", "ECO-NO-ENCONTRADO", cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, ClavesDePagos.NoEncontrado);
    }

    private Task Denegar(string motivo, string codigo, CancellationToken cancelacion) =>
        denegaciones.Registrar(new DenegacionDeAcceso(motivo, ruta: null) { CodigoDeError = codigo }, cancelacion);
}

/// <summary>
/// Emitir la clave de un intento al mostrar el formulario (PAG-04, PAG-16, D04): comprueba la propiedad y que se pueda pagar (la intención abierta y vigente y Carrito en <c>en_pago</c> con el <c>pedidoId</c> de esta
/// intención, sin retiro pendiente; con <c>pago_pendiente</c> no se emite; si Carrito no responde, falla cerrada) y devuelve un token firmado. <b>No escribe nada.</b>
/// </summary>
internal sealed class EmitirClaveDeIntento(
    AutorizadorDePagos autorizador,
    IRepositorioDePagos repositorio,
    IUnidadDeTrabajo unidad,
    ICheckoutDelCliente checkout,
    FirmadorDeClavesDeIntento firmador,
    IReloj reloj) : ICasoDeUso<EmitirClaveDeIntentoEntrada, ClaveDeIntentoEmitida>
{
    public async Task<Resultado<ClaveDeIntentoEmitida>> Ejecutar(EmitirClaveDeIntentoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirSesion(cancelacion).ConfigureAwait(false) is { } sinSesion)
        {
            return Resultado<ClaveDeIntentoEmitida>.Fallo(sinSesion);
        }

        var cuentaId = autorizador.CuentaId!.Value;
        var intencion = await unidad.Ejecutar(token => repositorio.ObtenerIntencion(entrada.PedidoId, paraActualizar: false, token), cancelacion).ConfigureAwait(false);
        if (intencion is null || !intencion.EsDeLaCuenta(cuentaId))
        {
            return Resultado<ClaveDeIntentoEmitida>.Fallo(await autorizador.RechazarAjena(cancelacion).ConfigureAwait(false));
        }

        var ahora = reloj.Ahora;
        var validacion = intencion.ValidarCobro(ahora);
        if (validacion != ValidacionDelCobro.Permitido)
        {
            return FallosDePagos.De<ClaveDeIntentoEmitida>(CategoriaDeError.Conflicto, Clave(validacion));
        }

        // Carrito dice si este es el checkout vivo y si se puede pagar: falla cerrada si no responde.
        Resultado<CheckoutVivoParaPagar> vivo;
        try
        {
            vivo = await checkout.ObtenerParaPagar(cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FallosDePagos.De<ClaveDeIntentoEmitida>(CategoriaDeError.NoDisponible, ClavesDePagos.ServicioNoDisponible);
        }

        if (!vivo.EsExito)
        {
            return vivo.Error!.Categoria == CategoriaDeError.NoEncontrado
                ? FallosDePagos.De<ClaveDeIntentoEmitida>(CategoriaDeError.Conflicto, ClavesDePagos.InstantaneaObsoleta)
                : FallosDePagos.De<ClaveDeIntentoEmitida>(CategoriaDeError.NoDisponible, ClavesDePagos.ServicioNoDisponible);
        }

        if (vivo.Valor.PedidoId != intencion.Id)
        {
            return FallosDePagos.De<ClaveDeIntentoEmitida>(CategoriaDeError.Conflicto, ClavesDePagos.InstantaneaObsoleta);
        }

        if (vivo.Valor.Estado != EstadoVisibleDelCheckout.EnPago)
        {
            return FallosDePagos.De<ClaveDeIntentoEmitida>(CategoriaDeError.Conflicto, ClavesDePagos.PagoEnCurso);
        }

        var expira = ahora + PlazosDelPago.VigenciaDelTokenDeIntento;
        var token = firmador.Emitir(new DatosDeLaClaveDeIntento(Guid.NewGuid(), intencion.Id, cuentaId, intencion.HuellaDeInstantanea ?? string.Empty, intencion.Total.EnCentimos, expira));
        return Resultado<ClaveDeIntentoEmitida>.Exito(new ClaveDeIntentoEmitida(token, expira, intencion.Total.Valor, intencion.VenceEn, intencion.ReservaExpiraEn));
    }

    internal static string Clave(ValidacionDelCobro validacion) => validacion switch
    {
        ValidacionDelCobro.ReservaExpirada => ClavesDePagos.ReservaExpirada,
        ValidacionDelCobro.IntencionCerrada => ClavesDePagos.IntencionCancelada,
        ValidacionDelCobro.InstantaneaObsoleta => ClavesDePagos.InstantaneaObsoleta,
        ValidacionDelCobro.CobroEnCurso => ClavesDePagos.PagoEnCurso,
        _ => throw new ArgumentOutOfRangeException(nameof(validacion)),
    };
}
