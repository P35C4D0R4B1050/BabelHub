namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>
/// Incumplimiento esperado que un caso de uso expresa con una clave simbólica (por ejemplo «stock-insuficiente»); el
/// decorador de transacción revierte los efectos y lo convierte en un <see cref="ErrorDeAplicacion"/>, y la Api lo traduce al
/// código estable. Nunca lleva trazas ni datos internos. Los errores inesperados no usan esta excepción (SEG-03, SEG-04).
/// </summary>
public sealed class ExcepcionDeNegocio : Exception
{
    public ExcepcionDeNegocio(string clave, CategoriaDeError categoria = CategoriaDeError.ReglaDeNegocio, IReadOnlyList<ErrorDeCampo>? errores = null)
        : base(clave)
    {
        if (string.IsNullOrWhiteSpace(clave))
        {
            throw new ArgumentException("El error necesita una clave simbólica.", nameof(clave));
        }

        if (categoria == CategoriaDeError.Inesperado)
        {
            throw new ArgumentException("Un error inesperado no se expresa como excepción de negocio.", nameof(categoria));
        }

        Clave = clave;
        Categoria = categoria;
        Errores = errores ?? [];
    }

    public ExcepcionDeNegocio()
        : this("regla-de-negocio-incumplida")
    {
    }

    public ExcepcionDeNegocio(string message, Exception innerException)
        : base(message, innerException)
    {
        Clave = message;
        Categoria = CategoriaDeError.ReglaDeNegocio;
        Errores = [];
    }

    public string Clave { get; }

    public CategoriaDeError Categoria { get; }

    public IReadOnlyList<ErrorDeCampo> Errores { get; }

    public ErrorDeAplicacion ComoError() => new(Categoria, Clave, Errores);
}

/// <summary>
/// Qué mensaje se muestra según la categoría (SEG-16): la validación, la regla de negocio y el límite excedido indican el
/// motivo específico; los errores inesperados y de seguridad llevan un mensaje genérico, sin datos internos.
/// </summary>
public static class MensajesDeError
{
    public static bool ExponeElMotivo(this CategoriaDeError categoria) =>
        categoria is CategoriaDeError.Validacion or CategoriaDeError.ReglaDeNegocio or CategoriaDeError.Limite;

    public static string MensajeGenerico(this CategoriaDeError categoria) => categoria switch
    {
        CategoriaDeError.Validacion => "Los datos enviados no son válidos.",
        CategoriaDeError.ReglaDeNegocio => "La operación no se puede completar.",
        CategoriaDeError.NoAutenticado => "Debes iniciar sesión para continuar.",
        CategoriaDeError.Prohibido => "No tienes permiso para realizar esta acción.",
        CategoriaDeError.NoEncontrado => "No se encontró lo que buscas.",
        CategoriaDeError.Conflicto => "La operación entra en conflicto con el estado actual.",
        CategoriaDeError.Limite => "Superaste el límite de uso. Inténtalo más tarde.",
        CategoriaDeError.NoDisponible => "El servicio no está disponible. Inténtalo de nuevo en unos minutos.",
        _ => "Ocurrió un error inesperado. Inténtalo de nuevo más tarde.",
    };
}

/// <summary>
/// Ejecuta un efecto cuya falla no debe interrumpir la operación (aviso por WhatsApp, correo de una alerta, caché): captura
/// el fallo, deja una alerta técnica y continúa (SEG-04). La cancelación del llamador sí se propaga.
/// </summary>
public interface IEjecutorDeDependenciasNoNecesarias
{
    /// <summary>Devuelve <see langword="true"/> si el efecto se aplicó y <see langword="false"/> si falló y se omitió.</summary>
    Task<bool> Ejecutar(Func<CancellationToken, Task> efecto, CancellationToken cancelacion = default);
}
