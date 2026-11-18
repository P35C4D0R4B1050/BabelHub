using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>
/// La revocación de sesiones que otros casos de uso piden (IDE-44, IDE-60): avanza el corte de sesiones del usuario (toda renovación emitida antes deja de servir, D17) y revoca las renovaciones vigentes.
/// Se llama dentro de la transacción del caso de uso, con la fila del usuario ya bloqueada.
/// </summary>
public sealed class RevocacionDeSesiones(IRepositorioDeCuentas cuentas, IRepositorioDeRenovaciones renovaciones, IReloj reloj)
{
    /// <summary>Todas las renovaciones: «cerrar todas», la recuperación del acceso y que otra persona restablezca la contraseña (IDE-18, IDE-44, IDE-60).</summary>
    public async Task Todas(CuentaDeIdentidad cuenta, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(cuenta);
        var ahora = reloj.Ahora;
        cuenta.Usuario.AvanzarElCorteDeSesiones(ahora);
        await cuentas.Guardar(cuenta, cancelacion).ConfigureAwait(false);
        await renovaciones.RevocarTodas(cuenta.Usuario.Id, ahora, cancelacion).ConfigureAwait(false);
    }

    /// <summary>Las demás: cuando el titular cambia su contraseña o su segundo factor conserva la sesión desde la que lo hizo y revoca el resto (IDE-44).</summary>
    public async Task LasDemas(CuentaDeIdentidad cuenta, Guid familiaQueSeConserva, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(cuenta);
        await renovaciones.RevocarLasDemas(cuenta.Usuario.Id, familiaQueSeConserva, reloj.Ahora, cancelacion).ConfigureAwait(false);
    }
}

/// <summary>
/// Renovar, cerrar y cerrar todas las sesiones (T16; IDE-08, IDE-14, IDE-43, IDE-44, IDE-46, IDE-60). Renovar rota la renovación <b>bajo el bloqueo de la fila del usuario</b>, de modo que una rotación concurrente no
/// deja viva una fila nueva tras cambiar la contraseña o cerrar todas; emite una credencial con el rol, la sucursal y el correo verificado <b>actuales</b> (no los del ingreso) y hereda el vencimiento absoluto, que
/// nunca se extiende. Vencida, revocada, anterior al corte de sesiones, de un usuario desactivado o cancelado, o inexistente: el mismo <see cref="FallosDeIdentidad.SesionNoRenovable"/>. <b>Reutilizar una renovación ya
/// rotada</b> es señal de robo: se revoca toda la familia, se registra un evento de seguridad y se publica una alerta de seguridad (D02). Si el texto legal vigente cambió, la renovación sigue y la decisión de pedirlo
/// la toma la política de la web con <see cref="IEstadoDeCuenta"/> (IDE-43).
/// </summary>
public sealed class Sesiones(
    IRepositorioDeCuentas cuentas,
    IRepositorioDeRenovaciones renovaciones,
    IHuellasDeIdentidad huellas,
    IGeneradorDeSecretos secretos,
    EmisionDeSesion emision,
    RevocacionDeSesiones revocacion,
    IUnidadDeTrabajo unidad,
    EventosDeSeguridadDeIdentidad eventos,
    IAlertas alertas,
    IReloj reloj) : ISesiones
{
    /// <summary>El motivo de la alerta de seguridad por reutilizar una renovación (catálogo cerrado de alertas).</summary>
    public const string MotivoDeRenovacionReutilizada = "renovacion-reutilizada";

    private sealed record Desenlace(Resultado<CredencialDeSesion> Resultado, Guid? ReutilizadaPor);

    public async Task<Resultado<CredencialDeSesion>> Renovar(ValorSecreto renovacion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(renovacion);
        var huella = huellas.DeValorOpaco(renovacion.Revelar());

        var desenlace = await unidad.Ejecutar(ct => Rotar(huella, ct), cancelacion).ConfigureAwait(false);

        if (desenlace.ReutilizadaPor is { } usuarioId)
        {
            await eventos.RenovacionReutilizada(usuarioId, cancelacion).ConfigureAwait(false);
            await AlertarSinInterrumpir().ConfigureAwait(false);
        }

        return desenlace.Resultado;
    }

    public async Task<Resultado<RespuestaUniforme>> Cerrar(ValorSecreto renovacion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(renovacion);
        var huella = huellas.DeValorOpaco(renovacion.Revelar());
        await unidad.Ejecutar(
            async ct =>
            {
                if (await renovaciones.PorHuella(huella, ct).ConfigureAwait(false) is { } actual)
                {
                    // Cierra la sesión entera (su familia): la renovación y las que nacieron de sus rotaciones dejan de servir (IDE-14). Repetirlo no cambia nada y no revela si existía.
                    await renovaciones.RevocarFamilia(actual.UsuarioId, actual.Familia, reloj.Ahora, ct).ConfigureAwait(false);
                }

                return true;
            },
            cancelacion).ConfigureAwait(false);
        return Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme());
    }

    public async Task<Resultado<RespuestaUniforme>> CerrarTodas(Guid usuarioId, CancellationToken cancelacion = default)
    {
        var existe = await unidad.Ejecutar(
            async ct =>
            {
                var cuenta = await cuentas.PorId(usuarioId, paraActualizar: true, ct).ConfigureAwait(false);
                if (cuenta is null)
                {
                    return false;
                }

                await revocacion.Todas(cuenta, ct).ConfigureAwait(false);
                return true;
            },
            cancelacion).ConfigureAwait(false);
        return existe
            ? Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme())
            : Resultado<RespuestaUniforme>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "cuenta-no-encontrada"));
    }

    private async Task<Desenlace> Rotar(string huella, CancellationToken cancelacion)
    {
        var rechazo = new Desenlace(Resultado<CredencialDeSesion>.Fallo(FallosDeIdentidad.SesionNoRenovable), null);
        var actual = await renovaciones.PorHuella(huella, cancelacion).ConfigureAwait(false);
        if (actual is null)
        {
            return rechazo;
        }

        // El bloqueo de la fila del usuario serializa rotar y revocar (D17).
        var cuenta = await cuentas.PorId(actual.UsuarioId, paraActualizar: true, cancelacion).ConfigureAwait(false);
        if (cuenta is not { CanceladaEn: null, Usuario.Activo: true })
        {
            return rechazo;
        }

        var ahora = reloj.Ahora;
        var valor = secretos.ValorOpaco();
        var (resultado, siguiente) = actual.Rotar(ahora, cuenta.Usuario.SesionesValidasDesde, huellas.DeValorOpaco(valor));
        if (resultado == ResultadoDeRotacion.Rotada && !await renovaciones.MarcarUsada(huella, ahora, cancelacion).ConfigureAwait(false))
        {
            // Otra solicitud la rotó entre la lectura y el UPDATE condicional: es una reutilización.
            resultado = ResultadoDeRotacion.Reutilizada;
        }

        switch (resultado)
        {
            case ResultadoDeRotacion.Rotada:
                var credencial = await emision.ParaLaSiguiente(cuenta.Usuario, siguiente!, valor, ahora, cancelacion).ConfigureAwait(false);
                return new Desenlace(Resultado<CredencialDeSesion>.Exito(credencial), null);

            case ResultadoDeRotacion.Reutilizada:
                await renovaciones.RevocarFamilia(actual.UsuarioId, actual.Familia, ahora, cancelacion).ConfigureAwait(false);
                return rechazo with { ReutilizadaPor = actual.UsuarioId };

            default:
                return rechazo;
        }
    }

    private async Task AlertarSinInterrumpir()
    {
        try
        {
            await alertas.PublicarDeSeguridad(new CodigoDeAlerta(MotivoDeRenovacionReutilizada)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Una alerta que no se puede publicar no cambia el rechazo ni deshace la revocación (AUD-40).
        }
    }
}
