using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Reclamaciones;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>Los textos de la persona para los estados y los campos de la hoja: nunca un valor escrito por alguien (REC-04).</summary>
public static class TextosDeReclamaciones
{
    public static string Estado(EstadoDeLaHoja estado) => estado switch
    {
        EstadoDeLaHoja.Pendiente => "Pendiente de respuesta",
        EstadoDeLaHoja.Respondido => "Respondida",
        _ => "Descartada",
    };

    public static string Tipo(string tipo) => string.Equals(tipo, "queja", StringComparison.Ordinal) ? "Queja" : "Reclamo";

    public static string Campo(string campo) => campo switch
    {
        "nombre" => "Nombre completo",
        "documento" => "Documento de identidad",
        "domicilio" => "Domicilio",
        "correo" => "Correo electrónico",
        "bienContratado" => "Bien contratado",
        "montoReclamado" => "Monto reclamado",
        "tipo" => "Tipo (reclamo o queja)",
        "detalle" => "Detalle",
        "pedidoReferencia" => "Referencia del pedido",
        _ => "Un dato del formulario",
    };
}

/// <summary>El formulario público de la hoja (T15, T17): la clave de un solo uso que emite el servidor y los campos que faltan o no son válidos, <b>sin el valor escrito</b> (REC-04).</summary>
public sealed class ReclamacionNuevaVista : ModeloBaseDePagina
{
    public string ClaveDelFormulario { get; set; } = string.Empty;

    public IReadOnlyList<string> CamposConProblema { get; set; } = [];

    /// <summary>Lo que la persona tenía escrito cuando venció su sesión (WEB-25): solo los campos conservables, una sola vez; vacío en cualquier otro caso.</summary>
    public IReadOnlyDictionary<string, string> Valores { get; set; } = new Dictionary<string, string>();

    public string Valor(string campo) => Valores.TryGetValue(campo, out var v) ? v : string.Empty;
}

/// <summary>La constancia de una hoja registrada (REC-05), imprimible: número, fecha, tipo, lo que el consumidor acaba de escribir y el plazo de respuesta.</summary>
public sealed class ConstanciaVista : ModeloBaseDePagina
{
    public ConstanciaDeHoja Constancia { get; set; } = null!;
}

public sealed class MisHojasVista : ModeloBaseDePagina
{
    public PaginaDeMisHojas Pagina { get; set; } = null!;

    public bool HayAnterior => Pagina.Pagina > 1;

    public bool HaySiguiente => (long)Pagina.Pagina * Pagina.Tamano < Pagina.Total;
}

public sealed class MiHojaVista : ModeloBaseDePagina
{
    public MiHoja Hoja { get; set; } = null!;
}

/// <summary>
/// Lo que se conserva de la hoja si la sesión de quien la escribe vence a mitad de la acción (WEB-25, D06): solo lo que no identifica a la persona. <b>Nunca</b> el nombre, el documento, el domicilio ni el correo
/// (REC-04: la hoja no devuelve los datos personales escritos), ni la clave de un solo uso ni el token de verificación. El modelo solo declara qué campos se conservan; la acción sigue leyendo el cuerpo con su lector estricto.
/// </summary>
public sealed class FormularioDeLaHojaDeReclamacion
{
    [Conservar]
    public string? BienContratado { get; set; }

    [Conservar]
    public string? MontoReclamado { get; set; }

    [Conservar]
    public string? PedidoReferencia { get; set; }

    [Conservar]
    public string? Tipo { get; set; }

    [Conservar]
    public string? Detalle { get; set; }
}
