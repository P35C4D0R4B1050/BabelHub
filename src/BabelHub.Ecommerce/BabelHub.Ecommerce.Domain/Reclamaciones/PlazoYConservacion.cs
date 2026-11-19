namespace BabelHub.Ecommerce.Domain.Reclamaciones;

/// <summary>El plazo de respuesta que se informa en la constancia (REC-05): 15 días calendario desde el día de Lima del registro.</summary>
public static class PlazoDeRespuesta
{
    public const int Dias = 15;

    public static DateOnly FechaLimite(DateTimeOffset fechaDeRegistro) => DiaDeLima.De(fechaDeRegistro).AddDays(Dias);
}

/// <summary>
/// La conservación de una hoja (REC-06, REC-17): al menos dos años, contados por día de Lima. Desde el día en que cumple dos años se anonimiza (un 29 de febrero cumple el 28). La función de
/// base de datos aplica el mismo predicado y una prueba de deriva comprueba que coinciden.
/// </summary>
public static class PoliticaDeConservacion
{
    public const int Anios = 2;

    public static DateOnly PrimerDiaAnonimizable(DateTimeOffset fechaDeRegistro) => DiaDeLima.De(fechaDeRegistro).AddYears(Anios);

    public static bool DebeAnonimizarse(DateTimeOffset fechaDeRegistro, DateTimeOffset ahora) => DiaDeLima.De(ahora) >= PrimerDiaAnonimizable(fechaDeRegistro);
}
