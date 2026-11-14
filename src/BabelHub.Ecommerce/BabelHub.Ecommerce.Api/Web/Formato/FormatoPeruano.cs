using System.Globalization;

namespace BabelHub.Ecommerce.Api.Web.Formato;

/// <summary>
/// Importes en soles y fechas con formato peruano (WEB-14, docs/06 §08): <c>S/ 1 234,50</c>, día/mes/año y la hora de Lima (UTC-5, sin
/// horario de verano). No depende de la cultura del servidor: la interfaz muestra siempre lo mismo. El espacio entre «S/» y el
/// importe y el de los miles es un espacio de no separación, para que un importe no se parta entre dos líneas.
/// </summary>
public static class FormatoPeruano
{
    private const string EspacioDeNoSeparacion = " ";

    private static readonly NumberFormatInfo FormatoDeImporte = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = EspacioDeNoSeparacion,
        NumberGroupSizes = [3],
        NumberDecimalDigits = 2,
        NumberNegativePattern = 1,
    };

    private static readonly TimeSpan DesfaseDeLima = TimeSpan.FromHours(-5);

    /// <summary>Un importe en soles con dos decimales y separador de miles: <c>S/ 1 234,50</c>. Un negativo lleva el signo delante: <c>-S/ 5,50</c>.</summary>
    public static string Soles(decimal importe)
    {
        var redondeado = Math.Round(Math.Abs(importe), 2, MidpointRounding.AwayFromZero);
        var texto = redondeado.ToString("N2", FormatoDeImporte);
        return (importe < 0 && redondeado != 0 ? "-" : string.Empty) + "S/" + EspacioDeNoSeparacion + texto;
    }

    /// <summary>La fecha como día/mes/año.</summary>
    public static string Fecha(DateOnly fecha) => fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>El día y la hora de Lima de un instante: <c>14/11/2026 15:42</c>.</summary>
    public static string FechaYHora(DateTimeOffset instante) => EnLima(instante).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>La hora de Lima de un instante: <c>15:42</c>.</summary>
    public static string Hora(DateTimeOffset instante) => EnLima(instante).ToString("HH:mm", CultureInfo.InvariantCulture);

    private static DateTimeOffset EnLima(DateTimeOffset instante) => instante.ToOffset(DesfaseDeLima);
}
