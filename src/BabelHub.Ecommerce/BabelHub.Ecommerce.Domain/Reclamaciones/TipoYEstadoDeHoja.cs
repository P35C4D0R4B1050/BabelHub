namespace BabelHub.Ecommerce.Domain.Reclamaciones;

/// <summary>El tipo de una hoja (REC-03): un reclamo (por el producto o servicio) o una queja (por la atención).</summary>
public enum TipoDeHoja
{
    Reclamo,
    Queja,
}

public static class TipoDeHojaExtensiones
{
    public static string ATexto(this TipoDeHoja tipo) => tipo switch
    {
        TipoDeHoja.Reclamo => "reclamo",
        TipoDeHoja.Queja => "queja",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
    };

    public static bool TryDesdeTexto(string? texto, out TipoDeHoja tipo)
    {
        switch (texto?.Trim())
        {
            case "reclamo":
                tipo = TipoDeHoja.Reclamo;
                return true;
            case "queja":
                tipo = TipoDeHoja.Queja;
                return true;
            default:
                tipo = default;
                return false;
        }
    }
}

/// <summary>El estado de una hoja (REC-16): solo se avanza de pendiente a respondido o a descartado.</summary>
public enum EstadoDeHoja
{
    Pendiente,
    Respondido,
    Descartado,
}

/// <summary>El estado del aviso por correo de una respuesta (REC-13): pendiente hasta que se envía o se encola.</summary>
public enum AvisoDeRespuesta
{
    Pendiente,
    Enviado,
}
