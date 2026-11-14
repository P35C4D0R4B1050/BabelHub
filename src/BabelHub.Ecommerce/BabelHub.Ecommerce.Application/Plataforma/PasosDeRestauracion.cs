namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>El desenlace de un paso del arranque en restauración: enumerado, nunca texto libre, para que no arrastre datos personales (PLA-19).</summary>
public enum ResultadoDePaso
{
    Correcto,

    /// <summary>El paso no pudo completarse: el servicio no se abre al público.</summary>
    Fallido,
}

/// <summary>
/// Un paso del arranque en modo <c>Restauracion</c> (plan §5.1): se ejecuta antes de abrir el servicio, en el orden de
/// <see cref="Orden"/>, y si falla el servicio no se abre. Plataforma, Identidad y Auditoría registran los suyos; el composition root
/// los recibe por inyección y los ordena por esta propiedad.
/// </summary>
public interface IPasoDeRestauracion
{
    /// <summary>Posición del paso: menor se ejecuta antes. Usa las constantes de <see cref="OrdenDePasoDeRestauracion"/>.</summary>
    int Orden { get; }

    /// <summary>Nombre estable del paso, para el informe de la restauración (sin datos personales).</summary>
    string Nombre { get; }

    Task<ResultadoDePaso> Ejecutar(CancellationToken cancelacion = default);
}

/// <summary>El orden de los pasos por módulo: Plataforma, luego Identidad, luego Auditoría (plan §5.1). Dentro de un módulo se suma a la base.</summary>
public static class OrdenDePasoDeRestauracion
{
    public const int Plataforma = 100;
    public const int Identidad = 200;
    public const int Auditoria = 300;
}
