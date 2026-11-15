using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Inventario.Infrastructure.Reservas;
using Microsoft.Extensions.Configuration;

namespace BabelHub.Inventario.Infrastructure.Composicion;

/// <summary>
/// Lee y comprueba la clave de las bajas de titular, <c>BABELHUB__Inventario__ClaveDeBajas</c> (D09): obligatoria, de al menos 32 bytes, con variedad de caracteres y distinta de cualquier otro secreto del servicio. Vive en la
/// composición porque es la única parte del módulo que puede mirar la lectura de secretos de la plataforma (regla 13). Los problemas nombran la variable y nunca su valor.
/// </summary>
public static class ClaveDeBajasDelEntorno
{
    /// <summary>
    /// Lee la clave con el lector de secretos del servicio: falta, débil (menos de 32 bytes o casi sin variedad) o repetida con otro secreto que el lector ya conoce son problemas. Devuelve <see langword="null"/> si hubo uno;
    /// el anfitrión junta todos y falla al arrancar (<see cref="LectorDeSecretos.ExigirValida"/>).
    /// </summary>
    public static HmacGeneradorDeIdentificadorAnonimo? Leer(LectorDeSecretos lector)
    {
        ArgumentNullException.ThrowIfNull(lector);
        var clave = lector.Obligatorio(HmacGeneradorDeIdentificadorAnonimo.Ruta);
        if (clave is null)
        {
            return null;
        }

        var antes = lector.Problemas.Count;
        lector.Comprobar(HmacGeneradorDeIdentificadorAnonimo.Ruta, clave);
        return lector.Problemas.Count == antes ? new HmacGeneradorDeIdentificadorAnonimo(clave) : null;
    }

    /// <summary>
    /// Carga la clave desde la configuración y <b>falla</b> si falta, es corta o se repite con otro secreto de <c>Seguridad</c>, <c>Integraciones</c> o <c>Auditoria</c> (las credenciales, la clave de huella y los secretos de proveedores).
    /// El anfitrión la llama al arrancar para no descubrir la falta de la clave en la primera baja.
    /// </summary>
    public static HmacGeneradorDeIdentificadorAnonimo Cargar(IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var lector = new LectorDeSecretos(configuracion);
        var generador = Leer(lector);

        if (generador is not null)
        {
            var clave = configuracion[HmacGeneradorDeIdentificadorAnonimo.Ruta]!;
            foreach (var seccion in new[] { "Seguridad", "Integraciones", "Auditoria" })
            {
                foreach (var (ruta, valor) in configuracion.GetSection(seccion).AsEnumerable().Where(p => !string.IsNullOrEmpty(p.Value)))
                {
                    // La clave de huella viaja como «versión:clave»: se compara también su parte secreta.
                    var texto = valor ?? string.Empty;
                    var parte = texto[(texto.IndexOf(':', StringComparison.Ordinal) + 1)..];
                    if (string.Equals(texto, clave, StringComparison.Ordinal) || string.Equals(parte, clave, StringComparison.Ordinal))
                    {
                        lector.Problema(HmacGeneradorDeIdentificadorAnonimo.Ruta, $"se repite en {LectorDeSecretos.Variable(ruta)}");
                    }
                }
            }
        }

        lector.ExigirValida();
        return generador!;
    }
}
