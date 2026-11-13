namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>
/// Origen de la solicitud en curso: su huella y, solo para el evento de seguridad, la dirección completa (SEG-25). Un
/// origen que no se pudo determinar no trae dirección y usa la huella fija <see cref="HuellaDeOrigen.Desconocido"/>.
/// </summary>
public sealed record OrigenDeSolicitud
{
    public OrigenDeSolicitud(HuellaDeOrigen huella, string? direccion)
    {
        ArgumentNullException.ThrowIfNull(huella);

        if (huella.EsDesconocido != string.IsNullOrEmpty(direccion))
        {
            throw new ArgumentException("El origen desconocido no trae dirección y uno conocido sí la trae.", nameof(direccion));
        }

        Huella = huella;
        Direccion = direccion;
    }

    public HuellaDeOrigen Huella { get; }

    public string? Direccion { get; }

    public static OrigenDeSolicitud Desconocido { get; } = new(HuellaDeOrigen.Desconocido, null);
}

/// <summary>Origen de la solicitud que se está atendiendo; lo fija la Api con el proxy conocido (SEG-17, SEG-23).</summary>
public interface IOrigenDeSolicitud
{
    OrigenDeSolicitud Actual { get; }
}

/// <summary>Calcula huellas con la clave secreta versionada del entorno (SEG-25); es la única forma de construirlas.</summary>
public interface ICalculadorDeHuella
{
    /// <summary>Huella de una dirección de origen, normalizada (IPv6 por /64).</summary>
    HuellaDeOrigen DeOrigen(string direccion);

    /// <summary>Huella de un dato personal que sujeta un límite, con un propósito distinto por uso.</summary>
    HuellaDeSujeto DeSujeto(string valor, string proposito);
}
