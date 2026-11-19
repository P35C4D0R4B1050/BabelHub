namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>La categoría con la que un módulo clasifica un correo de seguridad, de reclamo o de pago para contarlo contra la cuota (IDE-66, IDE-67, IDE-78).</summary>
public enum CategoriaDeCorreo
{
    /// <summary>Una alerta técnica o de seguridad a los administradores centrales (AUD-29): cupo exclusivo y tope de 24 al día por motivo y servicio (IDE-82).</summary>
    AlertaALaAdministracion,

    /// <summary>Un aviso crítico de cuenta: dispositivo no reconocido, restablecimiento del segundo factor o cambio de contraseña (IDE-74).</summary>
    AlertaCriticaDeCuenta,

    /// <summary>Un código de verificación o de recuperación: sale por el canal en memoria y sin reintento (IDE-19).</summary>
    CodigoDeVerificacionORecuperacion,

    /// <summary>Un aviso de seguridad que no es crítico: sin cupo no sale y queda visible en la cuenta (IDE-79).</summary>
    AvisoDeSeguridad,

    /// <summary>La respuesta a un reclamo: no se suprime ni se limita por cuenta (IDE-78).</summary>
    RespuestaDeReclamo,

    /// <summary>Un correo de pago: no se suprime ni se limita por cuenta (IDE-78).</summary>
    Pago,
}

/// <summary>Qué pasó con un correo (IDE-78, IDE-80): salió, quedó pendiente en la cola o se suprimió.</summary>
public enum ResultadoDeEnvio
{
    Enviado,
    Encolado,
    Suprimido,
}

/// <summary>
/// Un correo que sale de la cuenta de envío: su categoría, la dirección registrada del destinatario, la plantilla del catálogo y los datos que esa plantilla admite (nunca datos sensibles, enlaces ni
/// peticiones de datos o contraseñas, IDE-67). Las alertas a la administración llevan además su <see cref="Motivo"/> y su <see cref="Servicio"/> para el tope de IDE-82. Un código de un solo uso
/// no usa este tipo: viaja como <see cref="CorreoDeCodigo"/>.
/// </summary>
public sealed record CorreoSaliente
{
    public CorreoSaliente(CategoriaDeCorreo categoria, string destinatario, string plantilla, IReadOnlyDictionary<string, string>? datos = null, string? motivo = null, string? servicio = null)
    {
        if (categoria == CategoriaDeCorreo.CodigoDeVerificacionORecuperacion)
        {
            throw new ArgumentException("Un código sale como CorreoDeCodigo.", nameof(categoria));
        }

        if (string.IsNullOrWhiteSpace(destinatario) || destinatario.Length > 254 || destinatario.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is ',' or ';' or '<' or '>') || !destinatario.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("El destinatario es una sola dirección válida.", nameof(destinatario));
        }

        if (string.IsNullOrWhiteSpace(plantilla) || plantilla.Length > 64)
        {
            throw new ArgumentException("La plantilla es un nombre del catálogo.", nameof(plantilla));
        }

        if (categoria == CategoriaDeCorreo.AlertaALaAdministracion && (string.IsNullOrWhiteSpace(motivo) || string.IsNullOrWhiteSpace(servicio)))
        {
            throw new ArgumentException("Una alerta a la administración lleva su motivo y su servicio (tope de IDE-82).", nameof(motivo));
        }

        Categoria = categoria;
        Destinatario = destinatario.Trim();
        Plantilla = plantilla;
        Datos = datos ?? new Dictionary<string, string>();
        Motivo = motivo;
        Servicio = servicio;
    }

    public CategoriaDeCorreo Categoria { get; }

    public string Destinatario { get; }

    public string Plantilla { get; }

    public IReadOnlyDictionary<string, string> Datos { get; }

    public string? Motivo { get; }

    public string? Servicio { get; }

    /// <summary>No imprime el destinatario ni los datos.</summary>
    public override string ToString() => $"Correo {Categoria} ({Plantilla})";
}

/// <summary>
/// Envía correo desde la cuenta de envío del E-commerce <b>contando contra la cuota diaria</b> (IDE-66): lo implementa <c>Plataforma</c> (<c>EnviadorDeCorreoSmtp</c>, T34) con el reparto de cupos y el
/// tope por motivo y servicio. Una falla del correo de un módulo <b>nunca interrumpe su caso de uso</b> (AGENTS.md §3.10): el resultado es un valor, no una excepción, y quien llama decide qué mostrar.
/// </summary>
public interface IEnviadorDeCorreo
{
    /// <summary>Envía un correo que no es un código. <see cref="ResultadoDeEnvio.Encolado"/> si no hay cupo y su categoría se reintenta; <see cref="ResultadoDeEnvio.Suprimido"/> si no se reintenta.</summary>
    Task<ResultadoDeEnvio> Enviar(CorreoSaliente correo, CancellationToken cancelacion = default);

    /// <summary>Envía un código de un solo uso por el canal en memoria, sin cola ni reintento (vence a los 10 minutos): <see cref="ResultadoDeEnvio.Suprimido"/> si su cupo se agotó o el canal está lleno.</summary>
    Task<ResultadoDeEnvio> EnviarCodigo(CorreoDeCodigo correo, CancellationToken cancelacion = default);
}

/// <summary>
/// La dirección registrada de una cuenta (T34): la resuelve Identidad con la dirección de la cuenta activa, <b>nunca</b> una que venga de una solicitud (SEG-28). Una cuenta inexistente, desactivada o cancelada no tiene
/// dirección: <see langword="null"/>.
/// </summary>
public interface IDireccionesDeCuentas
{
    Task<string?> DeLaCuenta(Guid cuentaId, CancellationToken cancelacion = default);
}
