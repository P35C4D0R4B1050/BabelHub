using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>
/// El lector de formularios de las pantallas de administración: el mismo esquema estricto que el de Carrito (sin consulta, sin campos repetidos ni desconocidos, sin el campo del token) pero <b>sin la lista de identificadores prohibidos de Carrito</b>, porque la administración central actúa sobre un usuario
/// por su identificador (que valida Identidad contra el alcance del actor, nunca la web).
/// </summary>
public static class EntradasHttpDeAdministracion
{
    public static async Task<(IReadOnlyDictionary<string, string>? Campos, ErrorDeAplicacion? Error)> Leer(HttpContext contexto, IReadOnlyCollection<string> permitidos, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(permitidos);
        var solicitud = contexto.Request;
        var invalido = new ErrorDeAplicacion(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo("cuerpo", "formato-invalido")]);
        if (solicitud.Query.Count > 0)
        {
            return (null, invalido);
        }

        var campos = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!solicitud.HasFormContentType)
        {
            return (solicitud.ContentLength ?? 0) > 0 ? (null, invalido) : (campos, null);
        }

        var formulario = await solicitud.ReadFormAsync(cancelacion).ConfigureAwait(false);
        foreach (var campo in formulario)
        {
            if (string.Equals(campo.Key, ProteccionContraFalsificacion.NombreDelCampo, StringComparison.Ordinal))
            {
                continue;
            }

            if (campo.Value.Count != 1 || !permitidos.Contains(campo.Key, StringComparer.Ordinal) || !campos.TryAdd(campo.Key, campo.Value[0] ?? string.Empty))
            {
                return (null, invalido);
            }
        }

        return (campos, null);
    }
}
