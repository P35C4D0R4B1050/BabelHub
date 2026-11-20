using BabelHub.Ecommerce.Application.Identidad;

namespace BabelHub.Ecommerce.Application.Biblioteca.Interno;

/// <summary>
/// La parte de la cancelación de la cuenta que le toca a Biblioteca (<c>IAplicadorDeCancelacion</c>, T10; IDE-47, IDE-70; BIB-25, BIB-27, AC-BIB-22, AC-BIB-25): <b>elimina</b> los accesos de la cuenta (con los revocados) y, en cascada, su registro de descargas, sus claves y
/// sus solicitudes de ayuda. Corre <b>dentro de la transacción única de Identidad</b> y bajo <c>SET LOCAL ROLE rol_biblioteca</c> (lo fija la Infraestructura). Es <b>todo o nada</b> e <b>idempotente</b>: una cuenta sin accesos termina sin error. Si no puede eliminar algo,
/// la excepción sube, Identidad revierte todo (también lo de los demás módulos) y lo informa para que la cancelación no se ejecute y pueda reintentarse. No usa el identificador opaco (Biblioteca no guarda copia anonimizada: elimina), no audita (la cancelación la audita
/// Identidad) y no tiene parte en <c>IPendientesDeCuenta</c> (no hay pendientes de Biblioteca que difieran la cancelación).
/// </summary>
internal sealed class EliminarBibliotecaDeUnaCuenta(IRepositorioDeAccesos accesos) : IAplicadorDeCancelacion
{
    public const string NombreDelModulo = "biblioteca";

    public string Modulo => NombreDelModulo;

    public async Task Aplicar(Guid usuarioId, Guid identificadorOpaco, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La cuenta necesita su identificador.", nameof(usuarioId));
        }

        await accesos.EliminarDeLaCuenta(usuarioId, cancelacion).ConfigureAwait(false);
    }
}
