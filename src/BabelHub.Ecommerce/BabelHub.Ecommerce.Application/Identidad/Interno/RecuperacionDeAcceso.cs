using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using PropositoDeCodigo = BabelHub.Ecommerce.Domain.Identidad.PropositoDeCodigo;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>
/// Recuperación del acceso (T17; IDE-17, IDE-18, IDE-19, IDE-29, IDE-45, IDE-65): <b>el camino síncrono de la solicitud es idéntico</b> exista o no la cuenta, sea del personal o no esté verificado su correo: reserva
/// los límites de SEG-19, inserta la fila idéntica de solicitud y encola el trabajo; <b>nada de lo que depende de la cuenta ocurre aquí</b> (la búsqueda, el evento, la generación del código, su HMAC y el envío van en
/// segundo plano, <see cref="ProcesarSolicitudDeCodigo"/>), y solo un cliente activo con el correo verificado recibe un código. Confirmar valida la política de contraseñas <b>antes</b> de gastar un intento,
/// verifica el código de forma atómica, define la contraseña nueva, revoca todas las renovaciones y audita la recuperación en la misma transacción (sin auditoría no se completa y el código no se consume); el segundo
/// factor se mantiene. Todo fallo del código es el mismo mensaje genérico (IDE-19) y deja un evento con el motivo exacto.
/// </summary>
public sealed class RecuperacionDeAcceso(
    IRepositorioDeCuentas cuentas,
    IHasherDeContrasenas hasher,
    IListaDeContrasenasComunes comunes,
    IControlDeLimites limites,
    ICalculadorDeHuella huellas,
    IRegistroDeSolicitudesDeCodigo solicitudesDeCodigo,
    IColaDeCodigos cola,
    ICodigosDeUnSoloUso codigos,
    IHuellasDeIdentidad huellasDeIdentidad,
    RevocacionDeSesiones revocacion,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    EventosDeSeguridadDeIdentidad eventos,
    AvisosDeCuenta avisos,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj) : IRecuperacionDeAcceso
{
    private const string Proposito = "codigos-recuperacion";

    public async Task<Resultado<RespuestaUniforme>> Solicitar(string correo, CancellationToken cancelacion = default)
    {
        if (!EntradasDeIdentidad.TryNormalizarElCorreo(correo, out var normalizado))
        {
            return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.Validacion("correo", "formato-invalido"));
        }

        var rechazo = await SolicitudDeCodigos.Reservar(limites, huellas, normalizado, Proposito, cancelacion).ConfigureAwait(false);
        if (rechazo is not null)
        {
            return Resultado<RespuestaUniforme>.Fallo(rechazo);
        }

        await solicitudesDeCodigo.Registrar(huellas.DeSujeto(normalizado, Proposito).Valor, PropositoDeCodigo.RecuperacionDeAcceso, reloj.Ahora, cancelacion).ConfigureAwait(false);
        if (!cola.Encolar(new SolicitudDeCodigoEnSegundoPlano(normalizado, PropositoDeCodigo.RecuperacionDeAcceso)))
        {
            // El canal está lleno: se descarta en silencio, la respuesta es la misma y queda una alerta técnica (IDE-65).
            await AlertarSinInterrumpir().ConfigureAwait(false);
        }

        return Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme());
    }

    public async Task<Resultado<RespuestaUniforme>> Confirmar(string correo, ValorSecreto codigo, ValorSecreto contrasenaNueva, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(codigo);
        ArgumentNullException.ThrowIfNull(contrasenaNueva);
        if (!EntradasDeIdentidad.TryNormalizarElCorreo(correo, out var normalizado) || !CodigoDeUnSoloUso.TieneLaForma(codigo.Revelar()))
        {
            return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.CodigoInvalido);
        }

        // La política de contraseñas va antes de gastar un intento del código: una contraseña débil no consume uno.
        var (nueva, evaluacion) = Contrasena.Crear(contrasenaNueva.Revelar(), comunes.EsComun);
        if (nueva is null)
        {
            return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.Validacion("contrasena", evaluacion switch
            {
                ResultadoDeContrasena.Corta => "demasiado-corta",
                ResultadoDeContrasena.Larga => "demasiado-larga",
                ResultadoDeContrasena.Comun => "demasiado-comun",
                _ => "obligatoria",
            }));
        }

        var cuenta = await cuentas.PorCorreo(normalizado, cancelacion).ConfigureAwait(false);
        if (cuenta is null || !PuedeRecuperar(cuenta))
        {
            // El mismo costo que la rama elegible (un resumen de contraseña descartado): el tiempo no delata qué cuentas existen ni cuáles pueden recuperar (IDE-17, IDE-65).
            _ = await hasher.Resumir(nueva, cancelacion).ConfigureAwait(false);
            await eventos.CodigoRechazado(cuenta?.Usuario.Id, ResultadoDelCodigo.NoVigente, cancelacion).ConfigureAwait(false);
            return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.CodigoInvalido);
        }

        var usuarioId = cuenta.Usuario.Id;
        var huella = huellasDeIdentidad.DeCodigo(usuarioId, PropositoDeCodigo.RecuperacionDeAcceso, codigo.Revelar());
        var resumen = await hasher.Resumir(nueva, cancelacion).ConfigureAwait(false);

        ResultadoDelCodigo? rechazado = null;
        var completada = await unidad.Ejecutar(
            async ct =>
            {
                // El intento y el consumo son atómicos; si la auditoría falla más abajo, la transacción revierte también el consumo del código.
                var resultado = await codigos.Verificar(usuarioId, PropositoDeCodigo.RecuperacionDeAcceso, huella, reloj.Ahora, ct).ConfigureAwait(false);
                if (resultado != ResultadoDelCodigo.Correcto)
                {
                    rechazado = resultado;
                    return false;
                }

                var actual = await cuentas.PorId(usuarioId, paraActualizar: true, ct).ConfigureAwait(false);
                if (actual is null || !PuedeRecuperar(actual))
                {
                    // Se desactivó, se canceló o cambió de rol entre la lectura y el bloqueo: no se le define una contraseña nueva.
                    rechazado = ResultadoDelCodigo.NoVigente;
                    return false;
                }

                actual.ResumenDeContrasena = resumen;
                actual.ContrasenaTemporalHasta = null;
                actual.DebeCambiarLaContrasena = false;

                // Todas las renovaciones dejan de servir (IDE-18, IDE-44); el segundo factor no se toca.
                await revocacion.Todas(actual, ct).ConfigureAwait(false);
                await auditoria.Registrar(
                    new SolicitudDeRegistro(AccionesAuditables.RecuperacionDeAccesoCompletada) { ObjetoTipo = "cuenta", ObjetoId = usuarioId.ToString("D"), Motivo = "recuperacion-completada" },
                    ct).ConfigureAwait(false);
                return true;
            },
            cancelacion).ConfigureAwait(false);

        if (!completada)
        {
            await eventos.CodigoRechazado(usuarioId, rechazado!.Value, cancelacion).ConfigureAwait(false);
            return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.CodigoInvalido);
        }

        await avisos.ContrasenaCambiada(cuenta.Usuario, cancelacion).ConfigureAwait(false);
        return Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme());
    }

    /// <summary>Solo un cliente activo, sin cancelar y con el correo verificado puede recuperar el acceso (IDE-17, IDE-29).</summary>
    private static bool PuedeRecuperar(CuentaDeIdentidad cuenta) =>
        cuenta is { CanceladaEn: null, Usuario: { Activo: true, CorreoVerificado: true, Rol: RolDeUsuario.Cliente } };

    private async Task AlertarSinInterrumpir()
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(EmisionDeCodigos.MotivoDeFallaDelCorreo, reloj.Ahora)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Sin a dónde mandar la falla: la respuesta es la misma.
        }
    }
}
