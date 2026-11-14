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

/// <summary>Lo que dejó la ejecución de los pasos: si el servicio puede abrirse, qué pasos corrieron y cuál falló (solo su nombre, nunca un mensaje).</summary>
public sealed record ResultadoDeRestauracion(bool PermiteAbrir, IReadOnlyList<string> PasosEjecutados, string? PasoFallido);

/// <summary>
/// Ejecuta, antes de abrir el servicio restaurado, los pasos de <see cref="IPasoDeRestauracion"/> en el orden de su propiedad
/// <see cref="IPasoDeRestauracion.Orden"/> (Plataforma, Identidad, Auditoría; a igual orden, por nombre). Se detiene en el primero que
/// falla o que lanza una excepción: el servicio no se abre y el resultado solo nombra el paso (PLA-19, PLA-43, plan §5.1).
/// </summary>
public sealed class EjecutorDePasosDeRestauracion(IEnumerable<IPasoDeRestauracion> pasos)
{
    public async Task<ResultadoDeRestauracion> Ejecutar(CancellationToken cancelacion = default)
    {
        var ejecutados = new List<string>();
        foreach (var paso in pasos.OrderBy(p => p.Orden).ThenBy(p => p.Nombre, StringComparer.Ordinal))
        {
            ejecutados.Add(paso.Nombre);
            ResultadoDePaso resultado;
            try
            {
                resultado = await paso.Ejecutar(cancelacion).ConfigureAwait(false);
            }
            catch (Exception) when (!cancelacion.IsCancellationRequested)
            {
                resultado = ResultadoDePaso.Fallido;
            }

            if (resultado != ResultadoDePaso.Correcto)
            {
                return new ResultadoDeRestauracion(false, ejecutados, paso.Nombre);
            }
        }

        return new ResultadoDeRestauracion(true, ejecutados, null);
    }
}
