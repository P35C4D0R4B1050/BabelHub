using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.Configuration;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>
/// Que un secreto propio de un módulo (la clave del cursor de auditoría, la de bajas) no sea igual a ningún otro secreto del servicio (SEG-11, D10): se compara con las secciones de secretos comunes
/// (huella, credenciales de servicio, secretos de proveedores) y con las claves propias de los módulos, también por la parte secreta de un valor «versión:clave». Los problemas nombran la variable y nunca su valor.
/// </summary>
public static class RepeticionDeSecretos
{
    private static readonly string[] Secciones = ["Seguridad", "Integraciones", "Restauracion"];
    private static readonly string[] RutasSueltas = ["Auditoria:ClaveDeCursor", "Catalogo:ClaveDeIngresos"];

    public static void Comprobar(LectorDeSecretos lector, IConfiguration configuracion, string rutaPropia, string valor)
    {
        ArgumentNullException.ThrowIfNull(lector);
        ArgumentNullException.ThrowIfNull(configuracion);
        ArgumentException.ThrowIfNullOrEmpty(rutaPropia);
        ArgumentException.ThrowIfNullOrEmpty(valor);

        var otros = Secciones
            .SelectMany(s => configuracion.GetSection(s).AsEnumerable())
            .Select(p => (Ruta: p.Key, p.Value))
            .Concat(RutasSueltas.Select(r => (Ruta: r, Value: configuracion[r])))
            .Where(p => !string.IsNullOrEmpty(p.Value) && !string.Equals(p.Ruta, rutaPropia, StringComparison.OrdinalIgnoreCase));
        foreach (var (ruta, otro) in otros)
        {
            var texto = otro!;
            var parte = texto[(texto.IndexOf(':', StringComparison.Ordinal) + 1)..];
            if (string.Equals(texto, valor, StringComparison.Ordinal) || string.Equals(parte, valor, StringComparison.Ordinal))
            {
                lector.Problema(rutaPropia, $"se repite en {LectorDeSecretos.Variable(ruta)}");
            }
        }
    }
}
