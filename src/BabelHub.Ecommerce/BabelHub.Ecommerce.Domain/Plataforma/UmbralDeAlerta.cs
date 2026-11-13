namespace BabelHub.Ecommerce.Domain.Plataforma;

/// <summary>
/// Umbral de una alerta de seguridad (SEG-09): se supera cuando el conteo de eventos de una ventana es estrictamente mayor
/// que el máximo. La cuenta de los eventos por ventana es de Auditoría; la regla del umbral es de esta plataforma.
/// </summary>
public sealed record UmbralDeAlerta
{
    public UmbralDeAlerta(string motivo, int maximo, TimeSpan ventana)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new ArgumentException("El umbral necesita un motivo estable.", nameof(motivo));
        }

        if (maximo <= 0)
        {
            throw new ArgumentException("El máximo debe ser positivo.", nameof(maximo));
        }

        if (ventana <= TimeSpan.Zero)
        {
            throw new ArgumentException("La ventana debe ser positiva.", nameof(ventana));
        }

        Motivo = motivo;
        Maximo = maximo;
        Ventana = ventana;
    }

    public string Motivo { get; }

    public int Maximo { get; }

    public TimeSpan Ventana { get; }

    /// <summary>Con exactamente el máximo no hay alerta; con uno más, sí.</summary>
    public bool Supera(int eventosEnLaVentana) => eventosEnLaVentana > Maximo;
}

/// <summary>Umbrales iniciales de SEG-09 que evalúa el E-commerce.</summary>
public static class UmbralesDeAlerta
{
    public static UmbralDeAlerta AccesosDenegados { get; } = new("accesos-denegados", 10, TimeSpan.FromMinutes(5));

    public static UmbralDeAlerta IngresosFallidos { get; } = new("ingresos-fallidos", 20, TimeSpan.FromMinutes(5));

    public static IReadOnlyList<UmbralDeAlerta> Todos { get; } = [AccesosDenegados, IngresosFallidos];
}
