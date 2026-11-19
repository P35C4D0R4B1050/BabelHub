namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>Una plantilla de correo del catálogo cerrado: el asunto y el cuerpo, fijos, sin enlaces ni peticiones de datos.</summary>
public sealed record PlantillaDeCorreo(string Asunto, string Cuerpo, IReadOnlyList<string>? Campos = null);

/// <summary>
/// Quien aporta plantillas de correo al catálogo cerrado de Plataforma (los módulos de pagos y de reclamaciones; PLA-X): el nombre es la clave de <see cref="CorreoSaliente.Plantilla"/>. Una plantilla con <see cref="PlantillaDeCorreo.Campos"/> admite solo
/// esos campos (<c>{campo}</c> en el asunto o el cuerpo); una sin campos es de texto fijo y sus datos nunca entran al correo.
/// </summary>
public interface IProveedorDePlantillasDeCorreo
{
    IReadOnlyDictionary<string, PlantillaDeCorreo> Plantillas { get; }
}

