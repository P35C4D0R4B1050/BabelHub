using BabelHub.Ecommerce.Application.Identidad;

namespace BabelHub.Ecommerce.Application.Reclamaciones.Interno;

/// <summary>
/// Lo que Reclamaciones aporta al conteo de pendientes de una cuenta (T10, IDE-27, REC-19): <b>cuántas hojas <c>pendiente</c> tiene el correo verificado de la cuenta</b>, sin ningún dato de las hojas, sin las descartadas
/// ni las de una cuenta cancelada. Una cuenta sin correo verificado no tiene hojas que contar (un correo sin verificar podría ser de otra persona). Corre dentro de la transacción de Identidad y, si no puede contar (el
/// correo o el almacén no responden), lanza: nunca se asume cero.
/// </summary>
internal sealed class ContarHojasPendientesDeUnCorreo(ICorreoVerificadoDeLaCuenta correos, IRepositorioDeHojas hojas) : IPendientesDeCuenta
{
    public async Task<int> Contar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La cuenta necesita su identificador.", nameof(usuarioId));
        }

        var correo = CorreosDeSesion.Normalizar(await correos.De(usuarioId, cancelacion).ConfigureAwait(false));
        return correo is null ? 0 : await hojas.ContarPendientesDelCorreo(correo, cancelacion).ConfigureAwait(false);
    }
}

/// <summary>
/// La parte de Reclamaciones de la cancelación de una cuenta (T11, IDE-70, REC-20): <b>marca</b> todas las hojas del correo verificado de la cuenta como de una cuenta cancelada, sin eliminar ni alterar ningún dato
/// (la administración las sigue viendo; otra persona que se registre después con ese correo no las ve). Es todo o nada porque corre <b>dentro de la transacción de Identidad</b> (bajo el rol del esquema, que pone el
/// repositorio): si algo falla, lanza y la cancelación no se ejecuta ni deja nada a medias. Es idempotente y no falla si no hay ninguna hoja.
/// </summary>
internal sealed class MarcarHojasDeUnaCuentaCancelada(ICorreoVerificadoDeLaCuenta correos, IRepositorioDeHojas hojas) : IAplicadorDeCancelacion
{
    public string Modulo => "reclamaciones";

    public async Task Aplicar(Guid usuarioId, Guid identificadorOpaco, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La cuenta necesita su identificador.", nameof(usuarioId));
        }

        // Solo un correo verificado marca hojas: uno sin verificar podría ser de otra persona y esconderle sus hojas.
        if (CorreosDeSesion.Normalizar(await correos.De(usuarioId, cancelacion).ConfigureAwait(false)) is { } correo)
        {
            await hojas.MarcarDeCuentaCancelada(correo, cancelacion).ConfigureAwait(false);
        }
    }
}
