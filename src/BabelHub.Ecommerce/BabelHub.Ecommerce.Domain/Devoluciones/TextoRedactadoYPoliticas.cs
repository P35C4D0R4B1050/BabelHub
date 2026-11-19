using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BabelHub.Ecommerce.Domain.Devoluciones;

/// <summary>
/// El motivo y el texto libre de una solicitud o un reporte (DEV-29, DEV-31, D11): dato personal posible. Es una <b>clase</b> y no un <c>record</c> para que su <c>ToString</c>, su depurador y su serialización <b>no vuelquen el texto</b>;
/// el valor solo se lee con <see cref="Valor"/> donde la Presentación lo muestra codificado. Largo acotado y sin caracteres de control. La redacción (cancelación de la cuenta y dos años) lo reemplaza por un valor fijo y es idempotente.
/// </summary>
[DebuggerDisplay("[redactado]")]
[JsonConverter(typeof(ConvertidorDeTextoRedactado))]
public sealed class TextoRedactado
{
    public const string Redactado = "[redactado]";

    public const int LongitudMaximaDelMotivo = 300;

    public const int LongitudMaximaDelTexto = 2000;

    private TextoRedactado(string valor, bool redactado)
    {
        Valor = valor;
        EstaRedactado = redactado;
    }

    /// <summary>El texto tal como lo escribió la persona, o el valor fijo si ya se redactó.</summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public string Valor { get; }

    public bool EstaRedactado { get; }

    /// <summary>Crea el texto si no está vacío, no excede el máximo y no tiene caracteres de control; si no, <see langword="null"/> (validación del campo, sin repetir el valor).</summary>
    public static TextoRedactado? Crear(string? valor, int maximo)
    {
        var texto = valor?.Trim(' ');
        return string.IsNullOrEmpty(texto) || texto.Length > maximo || texto.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format) ? null : new TextoRedactado(texto, redactado: false);
    }

    /// <summary>Lo que lee el repositorio: un texto guardado o el valor fijo de uno ya redactado.</summary>
    public static TextoRedactado Rehidratar(string valor, bool redactado)
    {
        ArgumentException.ThrowIfNullOrEmpty(valor);
        return new TextoRedactado(redactado ? Redactado : valor, redactado);
    }

    /// <summary>El mismo campo ya redactado; repetirlo no cambia nada.</summary>
    public TextoRedactado Redactar() => EstaRedactado ? this : new TextoRedactado(Redactado, redactado: true);

    public override string ToString() => Redactado;
}

internal sealed class ConvertidorDeTextoRedactado : JsonConverter<TextoRedactado>
{
    public override TextoRedactado Read(ref Utf8JsonReader lector, Type tipo, JsonSerializerOptions opciones) =>
        throw new NotSupportedException("El texto de una devolución no se lee de un texto serializado.");

    public override void Write(Utf8JsonWriter escritor, TextoRedactado valor, JsonSerializerOptions opciones) => escritor.WriteStringValue(TextoRedactado.Redactado);
}

/// <summary>
/// Los plazos de una devolución (DEV-02, DEV-04, DEV-06, DEV-14, DEV-15, D05): <b>instantes</b>, porque Lima no tiene horario de verano (UTC−5): 15 días exactos desde la entrega aún se admiten y 15 días y un instante no; la recepción de
/// un impreso aprobado vence pasados 10 días exactos; el reembolso automático de un digital admite 14 días exactos desde el pago y el reporte, 30. También el monto de una línea: precio unitario por la cantidad, sin el envío.
/// </summary>
public static class PoliticaDeDevolucion
{
    public static readonly TimeSpan PlazoParaSolicitarUnImpreso = TimeSpan.FromDays(15);

    public static readonly TimeSpan PlazoParaRecibirUnImpreso = TimeSpan.FromDays(10);

    public static readonly TimeSpan PlazoDelReembolsoAutomaticoDeUnDigital = TimeSpan.FromDays(14);

    public static readonly TimeSpan PlazoParaReportarUnArchivo = TimeSpan.FromDays(30);

    /// <summary>El tope técnico del reingreso del Inventario: de 1 a 20 líneas de ediciones distintas (plan §1).</summary>
    public const int MaximoDeLineas = 20;

    /// <summary>Un impreso se puede solicitar hasta 15 días (inclusive) después de entregado (DEV-15).</summary>
    public static bool SolicitudDeImpresoEnPlazo(DateTimeOffset entregadaEn, DateTimeOffset ahora) => ahora - entregadaEn <= PlazoParaSolicitarUnImpreso;

    /// <summary>La recepción de un impreso aprobado venció si pasaron <b>más</b> de 10 días desde su aprobación (DEV-14).</summary>
    public static bool RecepcionVencida(DateTimeOffset aprobadaEn, DateTimeOffset ahora) => ahora - aprobadaEn > PlazoParaRecibirUnImpreso;

    /// <summary>El reembolso automático de un digital sin enlace entregado, hasta 14 días (inclusive) desde el pago (DEV-04, DEV-05).</summary>
    public static bool ReembolsoAutomaticoEnPlazo(DateTimeOffset pagadoEn, DateTimeOffset ahora) => ahora - pagadoEn <= PlazoDelReembolsoAutomaticoDeUnDigital;

    /// <summary>El reporte de un archivo defectuoso, hasta 30 días (inclusive) desde el pago (DEV-06).</summary>
    public static bool ReporteEnPlazo(DateTimeOffset pagadoEn, DateTimeOffset ahora) => ahora - pagadoEn <= PlazoParaReportarUnArchivo;

    /// <summary>El monto de una línea: precio unitario por la cantidad devuelta, redondeado al céntimo y <b>sin el envío</b> (DEV-08).</summary>
    public static decimal Monto(decimal precioUnitario, int cantidad)
    {
        if (precioUnitario < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(precioUnitario));
        }

        if (cantidad < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidad));
        }

        return decimal.Round(precioUnitario * cantidad, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>El último reembolso parcial de una línea se ajusta al saldo: nunca supera lo cobrado de la línea (plan §3).</summary>
    public static decimal AjustarAlSaldo(decimal monto, decimal importeDeLaLinea, decimal yaReembolsado)
    {
        var saldo = Math.Max(0m, importeDeLaLinea - yaReembolsado);
        return Math.Min(monto, saldo);
    }
}

/// <summary>
/// La retención del texto libre (DEV-27, DEV-30): se redacta a los <b>dos años calendario</b>, por <b>día de Lima</b>, desde el último cambio de la devolución completa cuando todas sus líneas están en reembolsada o rechazada; la fecha de Lima del
/// último cambio más dos años se compara con la de hoy en Lima (el 29 de febrero cumple el 28).
/// </summary>
public static class PoliticaDeRetencion
{
    public static readonly TimeSpan DesfaseDeLima = TimeSpan.FromHours(-5);

    public const int Anios = 2;

    public static DateOnly FechaEnLima(DateTimeOffset instante) => DateOnly.FromDateTime(instante.ToOffset(DesfaseDeLima).DateTime);

    public static bool EstaVencida(DateTimeOffset ultimoCambio, DateOnly hoyEnLima) => hoyEnLima >= FechaEnLima(ultimoCambio).AddYears(Anios);

    /// <summary>
    /// El instante antes del cual <b>puede</b> haber una devolución vencida: una cota holgada (el 29 de febrero cumple el 28) para pedir candidatas al almacén; la regla que decide es <see cref="EstaVencida"/>, que el llamador vuelve a aplicar bajo el bloqueo.
    /// </summary>
    public static DateTimeOffset CotaDeCandidatas(DateOnly hoyEnLima) =>
        new(hoyEnLima.AddYears(-Anios).AddDays(2).ToDateTime(TimeOnly.MinValue), DesfaseDeLima);
}

/// <summary>
/// El tope de unidades de una línea del pedido (DEV-03, DEV-18): <c>cantidad_comprometida</c> cuenta las líneas solicitada, aprobada, recibida y reembolsada y nunca pasa de lo comprado; una línea rechazada devuelve su cantidad. La atomicidad
/// frente a dos solicitudes simultáneas la garantiza el <c>UPDATE</c> condicional de la infraestructura: esta clase es la regla.
/// </summary>
public sealed class CupoDeLinea
{
    private CupoDeLinea(Guid lineaId, int comprada, int comprometida)
    {
        LineaId = lineaId;
        CantidadComprada = comprada;
        CantidadComprometida = comprometida;
    }

    public Guid LineaId { get; }

    public int CantidadComprada { get; }

    public int CantidadComprometida { get; private set; }

    public int Disponible => CantidadComprada - CantidadComprometida;

    public static CupoDeLinea Crear(Guid lineaId, int cantidadComprada)
    {
        if (lineaId == Guid.Empty)
        {
            throw new ArgumentException("La línea necesita un identificador.", nameof(lineaId));
        }

        return cantidadComprada < 1 ? throw new ArgumentOutOfRangeException(nameof(cantidadComprada)) : new CupoDeLinea(lineaId, cantidadComprada, 0);
    }

    public static CupoDeLinea Rehidratar(Guid lineaId, int cantidadComprada, int cantidadComprometida)
    {
        if (lineaId == Guid.Empty || cantidadComprada < 1 || cantidadComprometida < 0 || cantidadComprometida > cantidadComprada)
        {
            throw new ArgumentException("El cupo guardado no es coherente.");
        }

        return new CupoDeLinea(lineaId, cantidadComprada, cantidadComprometida);
    }

    /// <summary>Compromete unidades sin pasar de lo comprado; <see langword="false"/> y sin cambios si no alcanzan (<c>ECO-DEV-CANTIDAD-EXCEDIDA</c>).</summary>
    public bool Comprometer(int unidades)
    {
        if (unidades < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(unidades));
        }

        if (unidades > Disponible)
        {
            return false;
        }

        CantidadComprometida += unidades;
        return true;
    }

    /// <summary>Devuelve las unidades de una línea rechazada; no baja de cero (idempotencia defensiva: nunca se devuelve más de lo comprometido).</summary>
    public bool Devolver(int unidades)
    {
        if (unidades < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(unidades));
        }

        if (unidades > CantidadComprometida)
        {
            return false;
        }

        CantidadComprometida -= unidades;
        return true;
    }
}
