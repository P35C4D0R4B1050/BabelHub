using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

/// <summary>
/// El rol de cliente. La Aplicación no referencia <c>Shared</c> (regla 6), así que repite el valor de <c>RolesDeUsuario.Cliente</c>; una prueba de arquitectura compara ambos y falla si divergen.
/// </summary>
internal static class RolesDelCarrito
{
    public const string Cliente = "cliente";
}

/// <summary>Las claves simbólicas de los fallos del módulo; la Api las traduce a los códigos <c>ECO-…</c> (plan §4).</summary>
internal static class ClavesDelCarrito
{
    public const string NoAutenticado = "no-autenticado";

    public const string Prohibido = "prohibido";

    /// <summary>Uniforme para un carrito, checkout o pedido ajeno, inexistente o mal formado: no revela si existe (CAR-35).</summary>
    public const string NoEncontrado = "no-encontrado";

    /// <summary>El <c>pedidoId</c> oculto es el propio pero ya terminado o descartado (una pestaña obsoleta): error amable y sin evento de seguridad.</summary>
    public const string PedidoObsoleto = "pedido-obsoleto";

    public const string ValidacionFallida = "validacion-fallida";

    /// <summary>Más unidades de un impreso que el stock vendible de la red (409): el campo <c>maximo</c> lleva el máximo disponible (CAR-04).</summary>
    public const string SinStock = "carrito-sin-stock";

    public const string LimiteDeLineas = "carrito-limite-de-lineas";

    public const string LimiteDeUnidades = "carrito-limite-de-unidades";

    /// <summary>La cuenta ya posee la edición digital (409, CAR-20): solo con sesión.</summary>
    public const string DigitalYaPoseida = "digital-ya-poseida";

    public const string EdicionNoDisponible = "edicion-no-disponible";

    public const string LineaNoEncontrada = "linea-no-encontrada";

    public const string PagoPendiente = "checkout-pago-pendiente";

    public const string EnCurso = "checkout-en-curso";

    /// <summary>La cuenta está cancelada, desactivada o no existe: no opera sobre el carrito (el token vive hasta 15 minutos).</summary>
    public const string CuentaNoPuedeOperar = "cuenta-no-puede-comprar";

    public const string ServicioNoDisponible = "servicio-no-disponible";

    public const string LimiteExcedido = "limite-excedido";

    /// <summary>El Inventario no respondió (o respondió algo inválido) al validar el stock: no se avanza ni se cobra (CAR-06).</summary>
    public const string StockNoValidable = "checkout-stock-no-validable";

    /// <summary>El carrito no tiene ninguna línea que se pueda pagar (CAR-05).</summary>
    public const string CheckoutVacio = "checkout-vacio";

    /// <summary>No se pudo validar la entrega (la lista de sucursales y regiones no está disponible): no se continúa (R05).</summary>
    public const string EntregaNoValidable = "entrega-no-validable";

    public const string CheckoutNoEncontrado = "checkout-no-encontrado";

    /// <summary>Falta aceptar un texto requerido (422): los términos y la privacidad siempre y el reembolso digital si hay digitales (CAR-12, CAR-13).</summary>
    public const string AceptacionRequerida = "checkout-aceptacion-requerida";

    /// <summary>La versión vigente de un texto cambió respecto de la que se mostró (409): se vuelve a pedir su aceptación (CAR-28). El campo lleva el texto y la clave, la versión vigente.</summary>
    public const string TextoCambio = "checkout-texto-cambio";

    public const string CuentaNoPuedeComprar = "checkout-cuenta-no-puede-comprar";

    public const string CorreoSinVerificar = "checkout-correo-sin-verificar";

    public const string TextosPendientes = "checkout-textos-pendientes";

    /// <summary>Una edición digital dejó de estar disponible por su licencia antes de iniciar el pago (409, CAR-14): se invita a quitarla.</summary>
    public const string LicenciaNoVigente = "checkout-licencia-no-vigente";

    public const string DatosDeEntregaIncompletos = "checkout-datos-de-entrega-incompletos";

    /// <summary>El total que la persona vio ya no es el calculado (409, D17): no se crea nada.</summary>
    public const string TotalCambio = "checkout-total-cambio";

    /// <summary>El Inventario rechazó la reserva (409): falta de stock con las líneas y el máximo, un plazo o una fuente distinta de lo informado, o una reserva expirada. Se vuelve a la revisión con el carrito conservado (CAR-27).</summary>
    public const string ReservaRechazada = "checkout-reserva-rechazada";
}

/// <summary>Los motivos de <c>AccesoDenegado</c> que usa el módulo (AUD-05).</summary>
internal static class MotivosDeDenegacionDelCarrito
{
    public const string FueraDeRol = "fuera-de-rol";

    public const string SinSesion = "sin-sesion";

    /// <summary>Prefijo del motivo de un identificador ajeno: se le agrega el nombre del campo (<see cref="CampoDeIdentificadorAjeno"/>), nunca su valor.</summary>
    public const string RecursoAjeno = "recurso-ajeno";
}

/// <summary>La lista cerrada de identificadores que una solicitud no debe traer o que no son de quien los envía: su nombre es lo único que queda en el evento (CAR-35, AC-CAR-31).</summary>
public enum CampoDeIdentificadorAjeno
{
    CarritoId,
    CheckoutId,
    CuentaId,
    PedidoId,
}

public enum TipoDeSolicitante
{
    /// <summary>Un cliente con sesión: la cuenta sale del actor.</summary>
    Cuenta,

    /// <summary>Sin sesión: lo identifica solo la cookie opaca.</summary>
    Visitante,

    /// <summary>Personal, sistema, integración o nadie estableció el contexto: Carrito no es suyo.</summary>
    Rechazado,
}

public sealed record SolicitanteDelCarrito(TipoDeSolicitante Tipo, Guid? CuentaId = null);

/// <summary>El checkout que corresponde a un <c>pedidoId</c> oculto, según el repositorio: de qué cuenta es y si es el vivo (si no, está terminado o descartado).</summary>
public sealed record PedidoVisto(Guid CuentaId, bool EsElVivo);

/// <summary>
/// El alcance de Carrito, comprobado en la Aplicación y no solo en la Api (AGENTS.md §5, CAR-35): <b>la cuenta sale siempre del actor</b>, nunca de la solicitud; un cliente con sesión es la cuenta, un visitante lo es por su
/// cookie, y el personal, el sistema, una integración o la ausencia de contexto se rechazan (SEG-15). Un identificador ajeno deja <b>un evento</b> por <see cref="IDenegacionDeAcceso"/> con el nombre del campo y no su
/// valor, y la respuesta es la misma «no encontrado» que la de un inexistente. El evento se registra fuera de la transacción del caso de uso, así que persiste aunque esta se revierta.
/// </summary>
internal sealed class AutorizadorDelCarrito(IActorActual actores, IDenegacionDeAcceso denegaciones)
{
    public SolicitanteDelCarrito QuienPide() => actores.Actual switch
    {
        { Tipo: TipoDeActor.Usuario, UsuarioId: { } cuenta, Rol: RolesDelCarrito.Cliente } => new SolicitanteDelCarrito(TipoDeSolicitante.Cuenta, cuenta),
        { Tipo: TipoDeActor.Anonimo } => new SolicitanteDelCarrito(TipoDeSolicitante.Visitante),
        _ => new SolicitanteDelCarrito(TipoDeSolicitante.Rechazado),
    };

    /// <summary>Solo un cliente con sesión (el checkout y todo lo de la cuenta): devuelve su cuenta o el error con su evento.</summary>
    public async Task<Resultado<Guid>> ExigirCuenta(CancellationToken cancelacion)
    {
        var actor = actores.Actual;
        if (actor is null or { Tipo: TipoDeActor.Anonimo })
        {
            await Denegar(MotivosDeDenegacionDelCarrito.SinSesion, "ECO-NO-AUTENTICADO", cancelacion).ConfigureAwait(false);
            return Resultado<Guid>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, ClavesDelCarrito.NoAutenticado));
        }

        var quien = QuienPide();
        if (quien.Tipo == TipoDeSolicitante.Cuenta)
        {
            return Resultado<Guid>.Exito(quien.CuentaId!.Value);
        }

        await Denegar(MotivosDeDenegacionDelCarrito.FueraDeRol, "ECO-PROHIBIDO", cancelacion).ConfigureAwait(false);
        return Resultado<Guid>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDelCarrito.Prohibido));
    }

    /// <summary>Quien puede tener carrito (un cliente o un visitante); el personal y los demás se rechazan con su evento.</summary>
    public async Task<Resultado<SolicitanteDelCarrito>> ExigirClienteOVisitante(CancellationToken cancelacion)
    {
        var quien = QuienPide();
        if (quien.Tipo != TipoDeSolicitante.Rechazado)
        {
            return Resultado<SolicitanteDelCarrito>.Exito(quien);
        }

        if (actores.Actual is null)
        {
            await Denegar(MotivosDeDenegacionDelCarrito.SinSesion, "ECO-NO-AUTENTICADO", cancelacion).ConfigureAwait(false);
            return Resultado<SolicitanteDelCarrito>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, ClavesDelCarrito.NoAutenticado));
        }

        await Denegar(MotivosDeDenegacionDelCarrito.FueraDeRol, "ECO-PROHIBIDO", cancelacion).ConfigureAwait(false);
        return Resultado<SolicitanteDelCarrito>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDelCarrito.Prohibido));
    }

    /// <summary>El «no encontrado» uniforme: el mismo para un recurso ajeno, inexistente o mal formado.</summary>
    public ErrorDeAplicacion NoEncontrado() => new(CategoriaDeError.NoEncontrado, ClavesDelCarrito.NoEncontrado);

    /// <summary>Un identificador recibido que no es de quien pide (o que no debía venir): un evento con el nombre del campo y «no encontrado».</summary>
    public async Task<ErrorDeAplicacion> RechazarIdentificadorAjeno(CampoDeIdentificadorAjeno campo, CancellationToken cancelacion)
    {
        await Denegar($"{MotivosDeDenegacionDelCarrito.RecursoAjeno}-{NombreDelCampo(campo)}", "ECO-NO-ENCONTRADO", cancelacion).ConfigureAwait(false);
        return NoEncontrado();
    }

    /// <summary>
    /// Comprueba el <c>pedidoId</c> oculto de un formulario contra el checkout que lo tiene (<paramref name="visto"/>, <see langword="null"/> si ninguno): sin <c>pedidoId</c> se usa el checkout vivo del actor; uno de otra cuenta
    /// o inexistente es «no encontrado» con el evento; el propio ya terminado o descartado recibe un error amable y <b>sin</b> evento, para no inflar SEG-09 con pestañas viejas.
    /// </summary>
    public async Task<ErrorDeAplicacion?> ComprobarPedidoOculto(Guid? pedidoId, Guid cuentaDelActor, PedidoVisto? visto, CancellationToken cancelacion)
    {
        if (pedidoId is null)
        {
            return null;
        }

        if (visto is null || visto.CuentaId != cuentaDelActor)
        {
            return await RechazarIdentificadorAjeno(CampoDeIdentificadorAjeno.PedidoId, cancelacion).ConfigureAwait(false);
        }

        return visto.EsElVivo ? null : new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.PedidoObsoleto);
    }

    private static string NombreDelCampo(CampoDeIdentificadorAjeno campo) => campo switch
    {
        CampoDeIdentificadorAjeno.CarritoId => "carrito-id",
        CampoDeIdentificadorAjeno.CheckoutId => "checkout-id",
        CampoDeIdentificadorAjeno.CuentaId => "cuenta-id",
        CampoDeIdentificadorAjeno.PedidoId => "pedido-id",
        _ => throw new ArgumentOutOfRangeException(nameof(campo)),
    };

    private Task Denegar(string motivo, string codigoDeError, CancellationToken cancelacion) =>
        denegaciones.Registrar(new DenegacionDeAcceso(motivo, ruta: null) { CodigoDeError = codigoDeError }, cancelacion);
}

/// <summary>
/// La huella de la cookie del visitante (D01): la cookie <c>__Host-bh-carrito</c> son 256 bits aleatorios en base64url sin relleno (43 caracteres) y el servidor guarda solo su SHA-256 de 32 bytes. Una cookie
/// de otra longitud o con otro alfabeto equivale a «sin carrito» y no llega a la base de datos.
/// </summary>
public static class HuellaDeLaCookie
{
    public const int LongitudDeLaCookie = 43;

    public static bool TryDe(string? cookie, out byte[]? huella)
    {
        huella = null;
        if (cookie is null || cookie.Length != LongitudDeLaCookie || !cookie.All(EsBase64Url))
        {
            return false;
        }

        huella = SHA256.HashData(Encoding.ASCII.GetBytes(cookie));
        return true;
    }

    private static bool EsBase64Url(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
}
