using System.Globalization;
using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Domain.Notificaciones;

/// <summary>El tipo de un campo de una plantilla (NOT-07, NOT-10): cada uno tiene una forma cerrada, de modo que un campo no puede arrastrar texto libre, enlaces ni datos que no deben salir.</summary>
public enum TipoDeCampo
{
    /// <summary>Un identificador opaco (de un traslado o de una devolución): letras, dígitos, guiones y guiones bajos.</summary>
    Identificador,

    /// <summary>Un valor de una lista cerrada (el estado de un traslado, el resultado de un pago).</summary>
    Enumeracion,

    /// <summary>El número de un pedido: letras, dígitos y guiones.</summary>
    NumeroDePedido,

    /// <summary>Una fecha ISO (<c>2026-11-19</c>).</summary>
    Fecha,

    /// <summary>El primer nombre y solo letras: el registrante controla lo que recibe un tercero, así que no se admite texto libre.</summary>
    PrimerNombre,

    /// <summary>El código de una sucursal.</summary>
    CodigoDeSucursal,

    /// <summary>Una cantidad pequeña (hasta cinco dígitos).</summary>
    Cantidad,
}

/// <summary>Un campo permitido de una plantilla: su nombre, su tipo, su largo máximo y, si es una enumeración, los valores admitidos.</summary>
public sealed record CampoDePlantilla(string Nombre, TipoDeCampo Tipo, int LargoMaximo, IReadOnlySet<string>? Valores = null, bool Obligatorio = true);

/// <summary>El resultado de validar los campos de un aviso contra su plantilla: válido, o el campo y el motivo del primer rechazo (sin el valor).</summary>
public sealed record ResultadoDeValidacionDeCampos(bool EsValido, string? Campo, string? Motivo)
{
    public static ResultadoDeValidacionDeCampos Valido { get; } = new(true, null, null);

    public static ResultadoDeValidacionDeCampos Invalido(string campo, string motivo) => new(false, campo, motivo);
}

/// <summary>
/// Una plantilla de aviso (NOT-02, NOT-07, NOT-10): su clave, a quién va, la versión del conjunto de campos y los <b>campos permitidos con su tipo y su largo máximo</b>. <b>No lleva texto</b>: el texto vive en el servicio de
/// envío (n8n) y se cambia sin tocar el código (NOT-10). Solo existen las plantillas de los eventos de NOT-02: el Dominio no admite publicidad.
/// </summary>
public sealed partial class PlantillaDeAviso
{
    public const int LargoMaximoDelEvento = 160;

    private PlantillaDeAviso(string clave, TipoDeDestinatario destinatario, int version, IReadOnlyList<CampoDePlantilla> campos)
    {
        Clave = clave;
        Destinatario = destinatario;
        Version = version;
        Campos = campos;
    }

    public string Clave { get; }

    public TipoDeDestinatario Destinatario { get; }

    /// <summary>La versión del conjunto de campos: se guarda con el aviso y cambia si cambian los campos.</summary>
    public int Version { get; }

    public IReadOnlyList<CampoDePlantilla> Campos { get; }

    public static PlantillaDeAviso Crear(string clave, TipoDeDestinatario destinatario, int version, params CampoDePlantilla[] campos)
    {
        if (!FormatoDeLaClave().IsMatch(clave ?? string.Empty))
        {
            throw new ArgumentException("La clave de la plantilla son letras minúsculas, dígitos y guiones.", nameof(clave));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        if (campos.Select(c => c.Nombre).Distinct(StringComparer.Ordinal).Count() != campos.Length)
        {
            throw new ArgumentException("Los campos de una plantilla no se repiten.", nameof(campos));
        }

        return new PlantillaDeAviso(clave!, destinatario, version, campos);
    }

    /// <summary>
    /// Valida los campos de un aviso: <b>ninguno fuera del catálogo</b> de la plantilla, todos los obligatorios, cada uno con la forma de su tipo y su largo máximo, y ninguno con un enlace, una dirección, un
    /// número que parezca de tarjeta ni caracteres de control, aunque su forma lo permitiera (NOT-07). El valor nunca vuelve en el resultado.
    /// </summary>
    public ResultadoDeValidacionDeCampos Validar(IReadOnlyDictionary<string, string> campos)
    {
        ArgumentNullException.ThrowIfNull(campos);
        foreach (var nombre in campos.Keys)
        {
            if (Campos.All(c => c.Nombre != nombre))
            {
                return ResultadoDeValidacionDeCampos.Invalido(nombre, "campo-fuera-del-catalogo");
            }
        }

        foreach (var campo in Campos)
        {
            if (!campos.TryGetValue(campo.Nombre, out var valor))
            {
                if (campo.Obligatorio)
                {
                    return ResultadoDeValidacionDeCampos.Invalido(campo.Nombre, "obligatorio");
                }

                continue;
            }

            var motivo = ValidarElValor(campo, valor);
            if (motivo is not null)
            {
                return ResultadoDeValidacionDeCampos.Invalido(campo.Nombre, motivo);
            }
        }

        return ResultadoDeValidacionDeCampos.Valido;
    }

    /// <summary>El identificador de un evento (<c>pedido:123:listo-para-recojo</c>): determinista por hecho de negocio, con el tipo, el identificador y el estado destino.</summary>
    public static bool EventoValido(string? eventoId) => eventoId is { Length: > 0 and <= LargoMaximoDelEvento } && FormatoDelEvento().IsMatch(eventoId);

    /// <summary>La referencia opaca de un destinatario: el id de una cuenta o el código de una sucursal; nunca un número de teléfono ni una dirección.</summary>
    public static bool ReferenciaValida(string? referencia) => referencia is { Length: >= 2 and <= 64 } && FormatoDeLaReferencia().IsMatch(referencia) && !referencia.All(c => char.IsAsciiDigit(c) || c == '+');

    private static string? ValidarElValor(CampoDePlantilla campo, string? valor)
    {
        if (string.IsNullOrEmpty(valor) || valor.Length > campo.LargoMaximo)
        {
            return "largo-invalido";
        }

        if (valor.Any(char.IsControl))
        {
            return "caracteres-de-control";
        }

        // Un identificador o un número de pedido puede llevar muchos dígitos (un GUID, una guía): solo se rechaza si es todo dígitos y separadores (parece una tarjeta), no por las rachas de un valor mixto.
        var esIdentificador = campo.Tipo is TipoDeCampo.Identificador or TipoDeCampo.NumeroDePedido;
        var esGuia = campo.Nombre == "numeroDeGuia";
        // El número de guía de un operador puede ser solo de dígitos y largo (13 o más): la heurística de tarjeta no le aplica; su forma cerrada (ASCII, hasta 40) y la ausencia de enlaces bastan.
        var parecenDigitos = esGuia ? PareceUnNumeroDeTarjeta(valor) : (esIdentificador ? SoloDigitosYSeparadores().IsMatch(valor) && DigitosSeguidos().IsMatch(valor) : DigitosSeguidos().IsMatch(valor));
        if (ParecenEnlaceODireccion().IsMatch(valor) || parecenDigitos)
        {
            return "valor-prohibido";
        }

        return campo.Tipo switch
        {
            TipoDeCampo.Identificador => FormatoDelIdentificador().IsMatch(valor) ? null : "formato-invalido",
            TipoDeCampo.NumeroDePedido => FormatoDelNumeroDePedido().IsMatch(valor) ? null : "formato-invalido",
            TipoDeCampo.CodigoDeSucursal => FormatoDelCodigoDeSucursal().IsMatch(valor) ? null : "formato-invalido",
            TipoDeCampo.Cantidad => FormatoDeLaCantidad().IsMatch(valor) ? null : "formato-invalido",
            TipoDeCampo.Fecha => DateOnly.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? null : "formato-invalido",
            TipoDeCampo.PrimerNombre => valor.All(char.IsLetter) ? null : "formato-invalido",
            TipoDeCampo.Enumeracion => campo.Valores is { } admitidos && admitidos.Contains(valor) ? null : "valor-fuera-de-la-lista",
            _ => "tipo-desconocido",
        };
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{1,63}\\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex FormatoDeLaClave();

    [GeneratedRegex("^[a-z][a-z0-9-]{1,30}(:[A-Za-z0-9][A-Za-z0-9_.-]{0,63}){1,4}\\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex FormatoDelEvento();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{1,63}\\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex FormatoDeLaReferencia();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}\\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex FormatoDelIdentificador();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{2,31}\\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex FormatoDelNumeroDePedido();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{1,15}\\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex FormatoDelCodigoDeSucursal();

    [GeneratedRegex("^[0-9]{1,5}\\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex FormatoDeLaCantidad();

    /// <summary>Una guía solo de dígitos de 13 a 19 cifras que pasa la comprobación de Luhn es un número de tarjeta, no una guía: no sale en un aviso (NOT-07, SEG-10).</summary>
    private static bool PareceUnNumeroDeTarjeta(string conSeparadores)
    {
        var valor = conSeparadores.Replace("-", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (valor.Length is < 13 or > 19 || !valor.All(char.IsAsciiDigit))
        {
            return false;
        }

        var suma = 0;
        for (var i = 0; i < valor.Length; i++)
        {
            var cifra = valor[^(i + 1)] - '0';
            if (i % 2 == 1)
            {
                cifra *= 2;
                if (cifra > 9)
                {
                    cifra -= 9;
                }
            }

            suma += cifra;
        }

        return suma % 10 == 0;
    }

    [GeneratedRegex("(://|www\\.|@|\\.(com|pe|net|org|io)\\b)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 250)]
    private static partial Regex ParecenEnlaceODireccion();

    [GeneratedRegex("^[0-9 -]+\\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex SoloDigitosYSeparadores();

    /// <summary>Trece o más dígitos seguidos (con o sin espacios o guiones): parece el número de una tarjeta.</summary>
    [GeneratedRegex("([0-9][ -]?){13,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex DigitosSeguidos();
}

/// <summary>
/// El catálogo cerrado de plantillas (NOT-02, NOT-07): <b>solo los eventos notificables</b> de la spec —el pedido confirmado, en preparación, en tránsito, listo para recojo, entregado y no recogido, el pago pendiente y su
/// resultado, la anulación o cancelación de un pedido, el reembolso automático, la guía de envío (`envio-guia`, con el número de guía), el libro digital disponible y los cambios de estado de un traslado y de una devolución—; no hay forma de crear una
/// plantilla de publicidad. Ningún campo lleva texto libre, un enlace, un dato de pago ni un código de uso único.
/// </summary>
public static class CatalogoDePlantillas
{
    private static readonly CampoDePlantilla Pedido = new("numeroDePedido", TipoDeCampo.NumeroDePedido, 32);

    private static readonly CampoDePlantilla Nombre = new("nombre", TipoDeCampo.PrimerNombre, 30, Obligatorio: false);

    private static IReadOnlySet<string> Lista(params string[] valores) => new HashSet<string>(valores, StringComparer.Ordinal);

    /// <summary>Los estados de un pedido que Pedidos avisa (PED-05, PED-06): la lista cerrada de su máquina de estados.</summary>
    private static readonly CampoDePlantilla EstadoDelPedido = new(
        "estado",
        TipoDeCampo.Enumeracion,
        20,
        new HashSet<string>(["confirmado", "en-transito", "recibido", "en-preparacion", "listo-para-recojo", "despachado", "entregado", "no-recogido", "anulado"], StringComparer.Ordinal));

    public static IReadOnlyList<PlantillaDeAviso> Todas { get; } =
    [
        PlantillaDeAviso.Crear("pedido-confirmado", TipoDeDestinatario.Cliente, 1, Pedido, Nombre),
        PlantillaDeAviso.Crear("pedido-estado", TipoDeDestinatario.Cliente, 1, Pedido, EstadoDelPedido),
        PlantillaDeAviso.Crear("pedido-estado-sucursal", TipoDeDestinatario.Sucursal, 1, Pedido, EstadoDelPedido),
        PlantillaDeAviso.Crear("pedido-en-preparacion", TipoDeDestinatario.Cliente, 1, Pedido, Nombre),
        PlantillaDeAviso.Crear("pedido-en-transito", TipoDeDestinatario.Cliente, 1, Pedido, Nombre),
        PlantillaDeAviso.Crear("pedido-listo-para-recojo", TipoDeDestinatario.Cliente, 1, Pedido, Nombre, new CampoDePlantilla("sucursal", TipoDeCampo.CodigoDeSucursal, 16, Obligatorio: false)),
        PlantillaDeAviso.Crear("pedido-entregado", TipoDeDestinatario.Cliente, 1, Pedido, Nombre),
        PlantillaDeAviso.Crear("pedido-no-recogido", TipoDeDestinatario.Cliente, 1, Pedido, Nombre),
        PlantillaDeAviso.Crear("pago-pendiente", TipoDeDestinatario.Cliente, 1, Pedido, Nombre),
        PlantillaDeAviso.Crear("pago-resultado", TipoDeDestinatario.Cliente, 1, Pedido, new CampoDePlantilla("resultado", TipoDeCampo.Enumeracion, 12, Lista("aprobado", "rechazado")), Nombre),
        PlantillaDeAviso.Crear("pedido-anulado", TipoDeDestinatario.Cliente, 1, Pedido, new CampoDePlantilla("motivo", TipoDeCampo.Enumeracion, 12, Lista("anulado", "cancelado")), Nombre),
        PlantillaDeAviso.Crear("reembolso-automatico", TipoDeDestinatario.Cliente, 1, Pedido, Nombre),
        PlantillaDeAviso.Crear("envio-guia", TipoDeDestinatario.Cliente, 1, Pedido, new CampoDePlantilla("numeroDeGuia", TipoDeCampo.Identificador, 40), new CampoDePlantilla("estado", TipoDeCampo.Enumeracion, 16, Lista("guia-generada", "en-reparto")), Nombre),
        PlantillaDeAviso.Crear("libro-digital-disponible", TipoDeDestinatario.Cliente, 1, Pedido, Nombre),
        PlantillaDeAviso.Crear(
            "devolucion-estado",
            TipoDeDestinatario.Cliente,
            1,
            new CampoDePlantilla("devolucionId", TipoDeCampo.Identificador, 64),
            new CampoDePlantilla("estado", TipoDeCampo.Enumeracion, 16, Lista("aprobada", "rechazada", "recibida", "reembolsada")),
            Nombre),
        PlantillaDeAviso.Crear(
            "traslado-cambio-de-estado",
            TipoDeDestinatario.Sucursal,
            1,
            new CampoDePlantilla("numeroDeTraslado", TipoDeCampo.Identificador, 20),
            new CampoDePlantilla("estado", TipoDeCampo.Enumeracion, 16, Lista("solicitado", "aprobado", "en-transito", "recibido", "rechazado", "cancelado")),
            new CampoDePlantilla("rolDeLaSucursal", TipoDeCampo.Enumeracion, 8, Lista("origen", "destino")),
            new CampoDePlantilla("cantidad", TipoDeCampo.Cantidad, 5)),
    ];

    /// <summary>Las plantillas que solo puede pedir el Inventario (solo las de traslado, NOT-09).</summary>
    public static bool EsDeTraslado(string clave) => clave == "traslado-cambio-de-estado";

    public static PlantillaDeAviso? Obtener(string? clave) => Todas.FirstOrDefault(p => p.Clave == clave);
}
