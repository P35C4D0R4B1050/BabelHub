using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>
/// Los destinatarios de las alertas (T27; AUD-29, TRA-22): los <b>administradores centrales activos</b> y, para los motivos que lo indican, también los usuarios de <b>logística</b> activos. Solo identificadores de cuenta,
/// nunca direcciones: el correo lo resuelve el puerto de correo con la dirección registrada. Una cuenta cancelada o desactivada no recibe nada.
/// </summary>
public sealed class DestinatariosDeAlertasDeIdentidad(IRepositorioDeCuentas cuentas) : IDestinatariosDeAlertas
{
    public async Task<IReadOnlyList<DestinatarioDeAlerta>> Activos(CancellationToken cancelacion = default) =>
        await Destinatarios([RolDeUsuario.AdministradorCentral], cancelacion).ConfigureAwait(false);

    public async Task<IReadOnlyList<DestinatarioDeAlerta>> DeLogistica(CancellationToken cancelacion = default) =>
        await Destinatarios([RolDeUsuario.Logistica], cancelacion).ConfigureAwait(false);

    private async Task<IReadOnlyList<DestinatarioDeAlerta>> Destinatarios(IReadOnlyCollection<RolDeUsuario> roles, CancellationToken cancelacion) =>
        [.. (await cuentas.IdsActivosConRol(roles, cancelacion).ConfigureAwait(false)).Select(id => new DestinatarioDeAlerta(id))];
}

/// <summary>Los identificadores opacos de las cuentas canceladas por anonimizar (AUD-19, AUD-31): lo que Auditoría vuelve a poblar en <c>auditoria.por_anonimizar</c> tras restaurar.</summary>
public sealed class CuentasCanceladasPendientesDeIdentidad(IPorAnonimizar porAnonimizar) : ICuentasCanceladasPendientes
{
    public Task<IReadOnlyList<Guid>> Listar(int maximo, CancellationToken cancelacion = default) => porAnonimizar.Listar(maximo, cancelacion);
}

/// <summary>
/// Reaplica la cancelación o la anonimización de una cuenta tras restaurar un respaldo (T27; PLA-19, PLA-41, IDE-75): <b>sin intención ni consulta de pendientes</b>, idempotente (una cuenta ya cancelada o anonimizada es
/// «ya aplicada») y en una sola transacción; un fallo es «fallida» y no se deshace nada, de modo que se reintenta. Una anonimización no entrega baja al Inventario; una cancelación sí.
/// </summary>
public sealed class AplicadorDeSupresionDeIdentidad(IRepositorioDeCuentas cuentas, EjecutorDeLaCancelacion ejecutor, IUnidadDeTrabajo unidad) : IAplicadorDeSupresion
{
    public async Task<ResultadoDeAplicacionDeSupresion> Aplicar(string identificadorOpaco, ClaseDeSupresion clase, CancellationToken cancelacion = default)
    {
        if (!Guid.TryParse(identificadorOpaco, out var usuarioId) || usuarioId == Guid.Empty)
        {
            return ResultadoDeAplicacionDeSupresion.Fallida;
        }

        try
        {
            return await unidad.Ejecutar(
                async ct =>
                {
                    var cuenta = await cuentas.PorId(usuarioId, paraActualizar: true, ct).ConfigureAwait(false);
                    // Solo se reaplica sobre la cuenta de un cliente: una entrada falsa en el registro no puede cancelar a un administrador ni al personal (IDE-49).
                    if (cuenta is { Usuario.Rol: not RolDeUsuario.Cliente })
                    {
                        return ResultadoDeAplicacionDeSupresion.Fallida;
                    }

                    if (cuenta is null || YaAplicada(cuenta, clase))
                    {
                        return ResultadoDeAplicacionDeSupresion.YaEstabaAplicada;
                    }

                    await ejecutor.Reaplicar(cuenta, conBaja: clase == ClaseDeSupresion.Cancelacion, ct).ConfigureAwait(false);
                    return ResultadoDeAplicacionDeSupresion.Aplicada;
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Nada queda a medias (la transacción se revirtió): el cierre queda pendiente y se reintenta sin deshacer nada (PLA-41).
            return ResultadoDeAplicacionDeSupresion.Fallida;
        }
    }

    private static bool YaAplicada(CuentaDeIdentidad cuenta, ClaseDeSupresion clase) =>
        clase == ClaseDeSupresion.Cancelacion
            ? cuenta.CanceladaEn is not null
            : cuenta.Usuario.Correo.EndsWith("@anonimo.invalid", StringComparison.Ordinal);
}

/// <summary>
/// Tras restaurar un respaldo (T27; PLA-43): rota la clave de firma y revoca todas las renovaciones, de modo que ninguna credencial ni sesión del respaldo sirva. Las sesiones de la capa servidor de la web las invalida su
/// propio paso de <c>web-base</c>, que corre después (<see cref="OrdenDePasoDeRestauracion.Web"/>). Un fallo es «fallido»: el servicio no se abre.
/// </summary>
public sealed class RestauracionDeIdentidad(ClavesDeFirma claves, IRepositorioDeRenovaciones renovaciones, IUnidadDeTrabajo unidad, IReloj reloj) : IRestauracionDeIdentidad
{
    public async Task<ResultadoDePaso> RotarClavesEInvalidarSesiones(CancellationToken cancelacion = default)
    {
        try
        {
            await claves.RotarParaLaRestauracion(cancelacion).ConfigureAwait(false);
            await unidad.Ejecutar(
                async ct =>
                {
                    await renovaciones.RevocarLasDeTodos(reloj.Ahora, ct).ConfigureAwait(false);
                    return true;
                },
                cancelacion).ConfigureAwait(false);
            return ResultadoDePaso.Correcto;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return ResultadoDePaso.Fallido;
        }
    }
}

/// <summary>El paso de restauración de Identidad que rota las claves e invalida las sesiones (PLA-43): corre antes del de la web, que vacía sus sesiones restauradas.</summary>
public sealed class PasoDeRestauracionRotarClaves(IRestauracionDeIdentidad restauracion) : IPasoDeRestauracion
{
    public int Orden => OrdenDePasoDeRestauracion.Identidad;

    public string Nombre => "identidad-rotar-claves-e-invalidar-sesiones";

    public Task<ResultadoDePaso> Ejecutar(CancellationToken cancelacion = default) => restauracion.RotarClavesEInvalidarSesiones(cancelacion);
}

/// <summary>
/// El paso de restauración de Identidad que reenvía las bajas pendientes al Inventario y cierra lo que falte en el registro de supresiones (T27; AUD-32, PLA-41): lo restaurado pudo perder una entrega. Es de mejor esfuerzo:
/// que el Inventario aún no responda no impide abrir el servicio (la baja se reintenta y a las 24 horas hay alerta); solo una falla inesperada lo marca como fallido.
/// </summary>
public sealed class PasoDeRestauracionReenviarBajas(ReintentoDeBajas reintento) : IPasoDeRestauracion
{
    public int Orden => OrdenDePasoDeRestauracion.Identidad + 10;

    public string Nombre => "identidad-reenviar-bajas-pendientes";

    public async Task<ResultadoDePaso> Ejecutar(CancellationToken cancelacion = default)
    {
        try
        {
            await reintento.Procesar(cancelacion).ConfigureAwait(false);
            return ResultadoDePaso.Correcto;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return ResultadoDePaso.Fallido;
        }
    }
}

/// <summary>La dirección registrada de una cuenta activa para el envío de alertas (T34; SEG-28): nunca una dirección que venga de una solicitud; una cuenta inexistente, desactivada o cancelada no tiene.</summary>
public sealed class DireccionesDeCuentasDeIdentidad(IRepositorioDeCuentas cuentas) : IDireccionesDeCuentas
{
    public async Task<string?> DeLaCuenta(Guid cuentaId, CancellationToken cancelacion = default) =>
        await cuentas.PorId(cuentaId, paraActualizar: false, cancelacion).ConfigureAwait(false) is { CanceladaEn: null, Usuario: { Activo: true, CorreoVerificado: true } usuario } ? usuario.Correo : null;
}
