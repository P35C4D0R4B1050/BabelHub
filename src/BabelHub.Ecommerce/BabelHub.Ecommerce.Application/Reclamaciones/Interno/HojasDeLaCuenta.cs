using BabelHub.Ecommerce.Application.Identidad;

namespace BabelHub.Ecommerce.Application.Reclamaciones.Interno;

/// <summary>
/// Lo que Reclamaciones aporta al conteo de pendientes de una cuenta (T10 y T27, IDE-27, REC-19, REC-22): <b>cuántas hojas <c>pendiente</c> tiene la cuenta como titular</b>, sin ningún dato de las hojas, sin las descartadas ni las de una
/// cuenta cancelada. Una cuenta sin correo verificado cuenta <b>cero</b> (para que quien crea una cuenta con el correo de otra persona y registra una hoja no bloquee el reemplazo de IDE-84, D18); con correo verificado, primero reclama las
/// hojas sin cuenta de ese correo (disparador <c>pendientes</c>) y luego cuenta las vinculadas. Corre dentro de la transacción de Identidad y, si no puede leer el correo, vincular o auditar, lanza: nunca se asume cero ni un conteo parcial.
/// Identidad solo lo llama para cuentas de cliente (IDE-27, IDE-84): es un invariante del llamador, el módulo no comprueba el rol.
/// </summary>
internal sealed class ContarHojasPendientesDeUnCorreo(ICorreoVerificadoDeLaCuenta correos, VinculadorDeHojas vinculador, IVinculosDeHojas vinculos) : IPendientesDeCuenta
{
    public async Task<int> Contar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La cuenta necesita su identificador.", nameof(usuarioId));
        }

        var correo = CorreosDeSesion.Normalizar(await correos.De(usuarioId, cancelacion).ConfigureAwait(false));
        if (correo is null)
        {
            return 0;
        }

        _ = await vinculador.Reclamar(usuarioId, correo, DisparadoresDeVinculo.Pendientes, cancelacion).ConfigureAwait(false);
        return await vinculos.ContarPendientesDeLaCuenta(usuarioId, cancelacion).ConfigureAwait(false);
    }
}

/// <summary>
/// La parte de Reclamaciones de la cancelación de una cuenta (T11 y T27, IDE-70, REC-20, REC-24): <b>marca y desvincula</b>, en una sola sentencia, las hojas de las que la cuenta es titular: las vinculadas a ella y, si la cuenta tiene un correo
/// verificado, las aún sin cuenta de ese correo. Una cuenta sin correo verificado marca solo sus hojas vinculadas: quien creó una cuenta con el correo de otra persona y la cancela no oculta las hojas de ese tercero. Es todo o nada porque corre
/// <b>dentro de la transacción de Identidad</b> (el repositorio pone el rol del esquema); <b>no poder leer el correo lanza</b> y la cancelación no se ejecuta (el correo se lee antes de que Identidad anonimice la cuenta, IDE-47). Es idempotente
/// y no falla si no hay ninguna hoja. No elimina ni altera ningún dato de las hojas.
/// </summary>
internal sealed class MarcarHojasDeUnaCuentaCancelada(ICorreoVerificadoDeLaCuenta correos, IVinculosDeHojas vinculos) : IAplicadorDeCancelacion
{
    public string Modulo => "reclamaciones";

    public async Task Aplicar(Guid usuarioId, Guid identificadorOpaco, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La cuenta necesita su identificador.", nameof(usuarioId));
        }

        var correo = CorreosDeSesion.Normalizar(await correos.De(usuarioId, cancelacion).ConfigureAwait(false));
        _ = await vinculos.MarcarYDesvincularDeLaCuenta(usuarioId, correo, cancelacion).ConfigureAwait(false);
    }
}
