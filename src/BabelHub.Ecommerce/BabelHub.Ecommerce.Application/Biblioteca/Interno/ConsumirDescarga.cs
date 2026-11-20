using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;

namespace BabelHub.Ecommerce.Application.Biblioteca.Interno;

/// <summary>Lo que necesita la transacción corta de la descarga: el acceso leído, la clave de un solo uso, el archivo y el instante de firma (único para la clave y el enlace, D02).</summary>
internal sealed record EntradaDeConsumo(Acceso Acceso, ClaveDeDescarga Clave, string ClaveEnAlmacen, string NombreDeArchivo, DateTimeOffset FirmadoEn, DateOnly DiaDeLima);

internal abstract record SalidaDeConsumo
{
    private SalidaDeConsumo()
    {
    }

    /// <summary>El enlace se entregó y la descarga quedó contada y registrada.</summary>
    public sealed record Entregado(EnlaceDeDescarga Enlace) : SalidaDeConsumo;

    /// <summary>El conteo condicional no afectó filas (inexistente, ajeno, no vigente o sin cupo): no se escribió nada; hay que releer para clasificar.</summary>
    public sealed record SinFilas : SalidaDeConsumo;

    /// <summary>La clave ya tenía un enlace vigente (dos solicitudes simultáneas con la misma clave): se revirtió toda la transacción, también el conteo.</summary>
    public sealed record RepeticionVigente : SalidaDeConsumo;

    /// <summary>El acceso desapareció entre el conteo y la clave (clave foránea, 23503): se revirtió todo y no queda ningún registro (BIB-28).</summary>
    public sealed record AccesoInexistente : SalidaDeConsumo;
}

/// <summary>
/// La transacción corta de <c>SolicitarEnlace</c> (T07; BIB-04, BIB-05, BIB-11, BIB-17, BIB-23, BIB-28; plan §1 paso 4): <b>acceso → clave → registro</b> (el mismo orden en que la cancelación bloquea acceso y luego sus hijos, sin interbloqueo). La sentencia condicional del acceso
/// resuelve a la vez el tope, la carrera con un reembolso automático y la cancelación de la cuenta. Si el conteo no afecta filas o la clave es una repetición vigente, se <b>revierte toda la transacción</b> (también el conteo) y se relee fuera de ella para clasificar. La firma del enlace
/// es local (sin red) y ocurre <b>antes de confirmar</b>: si falla, se revierte el conteo. La URL no se guarda ni se registra (SEG-10).
/// </summary>
internal sealed class ConsumirDescarga(IRepositorioDeAccesos accesos, IRepositorioDeDescargas descargas, IEntregaDeArchivos entrega, IUnidadDeTrabajo unidad)
{
    private sealed class ReversionDelConsumoException(SalidaDeConsumo salida) : Exception("Se revierte la descarga.")
    {
        public SalidaDeConsumo Salida { get; } = salida;
    }

    public async Task<SalidaDeConsumo> Ejecutar(EntradaDeConsumo entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var vencimiento = entrada.FirmadoEn.AddSeconds(PoliticaDeDescargas.VigenciaDelEnlaceEnSegundos);
        try
        {
            return await unidad.Ejecutar<SalidaDeConsumo>(
                async token =>
                {
                    // (1) El acceso: titular, vigente y con cupo en una sola sentencia (D14).
                    if (await accesos.ConsumirCupo(entrada.Acceso.Id, entrada.Acceso.CuentaId, token).ConfigureAwait(false) == ResultadoDelConteoAtomico.NoAfecto)
                    {
                        return new SalidaDeConsumo.SinFilas();
                    }

                    // (2) La clave de un solo uso: una repetición vigente no cuenta y revierte el conteo.
                    var descargaId = Guid.NewGuid();
                    var clave = new ClaveRegistrada(entrada.Acceso.Id, entrada.Clave, entrada.FirmadoEn, vencimiento, descargaId);
                    switch (await descargas.RegistrarClave(clave, entrada.FirmadoEn, token).ConfigureAwait(false))
                    {
                        case ResultadoDeRegistrarClave.RepeticionVigente:
                            throw new ReversionDelConsumoException(new SalidaDeConsumo.RepeticionVigente());
                        case ResultadoDeRegistrarClave.AccesoInexistente:
                            throw new ReversionDelConsumoException(new SalidaDeConsumo.AccesoInexistente());
                    }

                    // (3) El registro de la descarga (BIB-11): acceso, usuario, edición y fecha; sin la URL ni la clave.
                    await descargas.Registrar(
                        new RegistroDeDescarga(descargaId, entrada.Acceso.Id, entrada.Acceso.CuentaId, entrada.Acceso.EdicionId, entrada.FirmadoEn, entrada.DiaDeLima),
                        token).ConfigureAwait(false);

                    // (4) La firma, local y antes de confirmar: si falla, la excepción revierte el conteo.
                    var url = entrega.FirmarEnlace(entrada.ClaveEnAlmacen, entrada.NombreDeArchivo, entrada.FirmadoEn, PoliticaDeDescargas.VigenciaDelEnlaceEnSegundos);
                    return new SalidaDeConsumo.Entregado(new EnlaceDeDescarga(url, vencimiento));
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (ReversionDelConsumoException reversion)
        {
            return reversion.Salida;
        }
    }
}
