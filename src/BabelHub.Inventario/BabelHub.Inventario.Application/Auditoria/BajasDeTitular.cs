using System.Globalization;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Auditoria;

/// <summary>
/// El aviso de la baja de un titular que el E-commerce entrega al Inventario (AUD-32, IDE-64), ya validado con un esquema <b>estricto</b>: un único campo,
/// <c>identificadorOpaco</c>, con el formato de un GUID canónico (36 caracteres en minúsculas con guiones, no vacío); cualquier campo de más, un campo ausente o un valor con otra
/// forma se rechaza como validación. Nunca lleva un correo, un nombre ni un número: solo el identificador opaco de la cuenta.
/// </summary>
public sealed record BajaDeTitularRecibida
{
    public const string CampoDelIdentificador = "identificadorOpaco";

    private BajaDeTitularRecibida(Guid identificador) => Identificador = identificador;

    public Guid Identificador { get; }

    /// <summary>
    /// Valida el aviso: <paramref name="camposRecibidos"/> son los nombres de todas las propiedades que llegaron (la Api los toma del cuerpo tal cual; los extra se
    /// rechazan, no se ignoran) y <paramref name="identificadorOpaco"/> es el valor del campo, como texto.
    /// </summary>
    public static Resultado<BajaDeTitularRecibida> Validar(IReadOnlyCollection<string> camposRecibidos, string? identificadorOpaco)
    {
        ArgumentNullException.ThrowIfNull(camposRecibidos);
        if (camposRecibidos.Count != 1 || !camposRecibidos.Contains(CampoDelIdentificador, StringComparer.Ordinal))
        {
            return Resultado<BajaDeTitularRecibida>.Fallo(Invalido("esquema-invalido"));
        }

        if (identificadorOpaco is not { Length: 36 }
            || !Guid.TryParseExact(identificadorOpaco, "D", out var guid)
            || guid == Guid.Empty
            || !string.Equals(identificadorOpaco, guid.ToString("D", CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            return Resultado<BajaDeTitularRecibida>.Fallo(Invalido("identificador-invalido"));
        }

        return Resultado<BajaDeTitularRecibida>.Exito(new BajaDeTitularRecibida(guid));
    }

    private static ErrorDeAplicacion Invalido(string clave) =>
        new(CategoriaDeError.Validacion, clave, [new ErrorDeCampo(CampoDelIdentificador, clave)]);
}

/// <summary>
/// Audita cada baja de titular recibida (AUD-32, AUD-26): un registro <c>BajaDeTitularProcesada</c> con el actor «integración» (el de la credencial de servicio de alcance
/// <c>baja-de-titular</c>, nunca el de la solicitud, AUD-23), el identificador opaco como objeto y sin más datos. <b>La llama Reservas</b> (que es dueño de <c>bajas_recibidas</c>) en
/// la <b>misma transacción</b> que guarda la baja: si no se puede auditar, la baja no se guarda y el E-commerce la reintenta (AUD-04). Una repetición de la misma baja
/// (<paramref name="esNueva"/> falso, porque <c>bajas_recibidas</c> ya la tenía) responde 204 y <b>no escribe una segunda auditoría</b>.
/// </summary>
public interface IAuditoriaDeBajasRecibidas
{
    /// <exception cref="AuditoriaNoDisponibleException">No se pudo registrar: la baja no debe guardarse (AUD-04).</exception>
    /// <exception cref="InvalidOperationException">El actor del contexto no es una integración: esta operación solo llega con la credencial de servicio.</exception>
    Task Registrar(BajaDeTitularRecibida baja, bool esNueva, CancellationToken cancelacion = default);
}
