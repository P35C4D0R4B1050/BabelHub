namespace BabelHub.Ecommerce.Domain.Reclamaciones;

/// <summary>
/// El día y el año de Lima (UTC−5, sin horario de verano) de un instante: el número de hoja, el plazo de respuesta y la conservación cuentan por día de Lima. El Dominio no lee la hora:
/// recibe el instante.
/// </summary>
public static class DiaDeLima
{
    private static readonly TimeSpan Desfase = TimeSpan.FromHours(-5);

    public static DateOnly De(DateTimeOffset instante) => DateOnly.FromDateTime(instante.ToOffset(Desfase).DateTime);

    public static int Anio(DateTimeOffset instante) => De(instante).Year;
}
