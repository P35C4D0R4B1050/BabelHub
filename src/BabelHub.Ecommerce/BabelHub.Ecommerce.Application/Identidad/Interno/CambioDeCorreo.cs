using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using DominioProposito = BabelHub.Ecommerce.Domain.Identidad.PropositoDeCodigo;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>
/// La confianza en el dispositivo y la región de quien pide algo sensible (IDE-54, IDE-55): se reconoce por la huella de su marca (solo se guarda el SHA-256) y por la región que entregó el borde, y solo cuenta un
/// dispositivo con 24 horas de antigüedad y que no esté congelado. Un dispositivo <b>congelado</b> no puede hacer cambios sensibles (correo, contraseña, segundo factor) hasta que venza el congelamiento, y
/// <b>mientras hay un cambio de correo no reconocido pendiente</b> los cambios sensibles exigen un dispositivo reconocido: el congelamiento es de la cuenta y no depende de que quien lo pidió traiga una marca.
/// </summary>
public sealed class ConfianzaDelDispositivo(IRepositorioDeDispositivos dispositivos, IRepositorioDeCambiosDeCorreo cambios, IHuellasDeIdentidad huellas, IReloj reloj)
{
    public static ErrorDeAplicacion DispositivoCongelado { get; } = new(CategoriaDeError.Prohibido, "dispositivo-congelado");

    /// <summary>
    /// El dispositivo congelado no hace cambios sensibles (IDE-54). Mientras hay un cambio de correo pendiente de confirmación, quien no presenta la marca de un dispositivo reconocido (sin marca, desconocido o con menos de
    /// 24 horas) tampoco: así una sesión robada que pidió el cambio sin cookie no puede cambiar la contraseña ni el segundo factor para atrincherarse. Sin cambio pendiente, solo se bloquea al dispositivo congelado.
    /// </summary>
    public async Task<ErrorDeAplicacion?> ComprobarQueNoEsteCongelado(Guid usuarioId, ValorSecreto? marca, CancellationToken cancelacion)
    {
        var ahora = reloj.Ahora;
        var pendiente = await cambios.Obtener(usuarioId, cancelacion).ConfigureAwait(false) is { Estado: EstadoDelCambioDeCorreo.PendienteDeConfirmacion } cambio && ahora < cambio.Vence;
        if (marca is null)
        {
            return pendiente ? DispositivoCongelado : null;
        }

        var lista = await dispositivos.Listar(usuarioId, cancelacion).ConfigureAwait(false);
        var huella = huellas.DeValorOpaco(marca.Revelar());
        var presentado = lista.FirstOrDefault(d => d.HuellaDeLaMarca == huella);
        if (presentado is not null && presentado.EstaCongelado(ahora))
        {
            return DispositivoCongelado;
        }

        return pendiente && (presentado is null || !presentado.EsMaduro(ahora)) ? DispositivoCongelado : null;
    }

    /// <summary>La confianza de la solicitud y el dispositivo que presentó, si lo guardado lo reconoce por su marca.</summary>
    public async Task<(ConfianzaDeLaSolicitud Confianza, Dispositivo? Presentado)> Evaluar(Guid usuarioId, ValorSecreto? marca, string? region, CancellationToken cancelacion)
    {
        var lista = await dispositivos.Listar(usuarioId, cancelacion).ConfigureAwait(false);
        var huella = marca is null ? null : huellas.DeValorOpaco(marca.Revelar());
        var confianza = ConfianzaDeDispositivos.Evaluar(huella, region, [.. lista], reloj.Ahora);
        return (confianza, huella is null ? null : lista.FirstOrDefault(d => d.HuellaDeLaMarca == huella));
    }
}

/// <summary>
/// El registro de dispositivos tras un ingreso completo (IDE-55): si la marca que trae ya es de un dispositivo del usuario no hace nada; si no (o no trae), emite una marca nueva de 256 bits y guarda solo su huella y
/// la región. Solo el usuario autenticado lo registra.
/// </summary>
public sealed class DispositivosDeLaCuenta(
    AlcanceDeLaCuenta alcance,
    IRepositorioDeDispositivos dispositivos,
    IHuellasDeIdentidad huellas,
    IGeneradorDeSecretos secretos,
    IUnidadDeTrabajo unidad,
    IReloj reloj) : IDispositivosDeLaCuenta
{
    public async Task<Resultado<bool>> Reconoce(Guid usuarioId, ValorSecreto? marcaPresentada, string? region, CancellationToken cancelacion = default)
    {
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<bool>.Fallo(prohibido);
        }

        var lista = await dispositivos.Listar(usuarioId, cancelacion).ConfigureAwait(false);
        var huella = marcaPresentada is null ? null : huellas.DeValorOpaco(marcaPresentada.Revelar());
        return Resultado<bool>.Exito(ConfianzaDeDispositivos.Evaluar(huella, region, [.. lista], reloj.Ahora).PermiteAplicarElCambio);
    }

    public async Task<Resultado<MarcaDeDispositivoEmitida>> RegistrarElIngresoCompleto(Guid usuarioId, ValorSecreto? marcaPresentada, string? region, CancellationToken cancelacion = default)
    {
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<MarcaDeDispositivoEmitida>.Fallo(prohibido);
        }

        return await unidad.Ejecutar(
            async ct =>
            {
                var lista = await dispositivos.Listar(usuarioId, ct).ConfigureAwait(false);
                if (marcaPresentada is not null)
                {
                    var huellaPresentada = huellas.DeValorOpaco(marcaPresentada.Revelar());
                    if (lista.Any(d => d.HuellaDeLaMarca == huellaPresentada))
                    {
                        return Resultado<MarcaDeDispositivoEmitida>.Exito(new MarcaDeDispositivoEmitida(null));
                    }
                }

                var marca = secretos.ValorOpaco();
                await dispositivos.Guardar(Dispositivo.Emitir(usuarioId, huellas.DeValorOpaco(marca), region, reloj.Ahora), ct).ConfigureAwait(false);
                return Resultado<MarcaDeDispositivoEmitida>.Exito(new MarcaDeDispositivoEmitida(new ValorSecreto(marca)));
            },
            cancelacion).ConfigureAwait(false);
    }
}

/// <summary>
/// El cambio de correo de un cliente (T20; IDE-25, IDE-53, IDE-54, IDE-55, AC-IDE-47, AC-IDE-48). Pide la contraseña actual (con el límite de ingresos fallidos) y evalúa la confianza de la solicitud. <b>Dispositivo
/// reconocido y región habitual:</b> se envía un código al correo <b>nuevo</b> y el cambio se aplica al verificarlo, con aviso en la cuenta y al correo nuevo y nada al anterior. <b>Otro caso:</b> no se aplica, queda
/// pendiente 24 horas, se congelan los cambios sensibles del dispositivo, se alerta al correo anterior y en la cuenta, y el titular lo confirma o rechaza desde un dispositivo reconocido y con su contraseña; si
/// confirma, se envía el código al correo nuevo; vencido el plazo, se descarta. Un correo ya usado por otra cuenta no se distingue: la respuesta es la misma y no sale ningún código (sin enumeración de cuentas).
/// El código sale solo al correo nuevo guardado en el cambio en curso (SEG-28) y con los límites por correo y por origen de SEG-19.
/// </summary>
public sealed class CambioDeCorreo(
    IRepositorioDeCuentas cuentas,
    AlcanceDeLaCuenta alcance,
    Reautenticacion reautenticacion,
    ConfianzaDelDispositivo confianza,
    IRepositorioDeDispositivos dispositivos,
    IRepositorioDeCambiosDeCorreo cambios,
    EmisionDeCodigos emisionDeCodigos,
    ICodigosDeUnSoloUso codigos,
    IHuellasDeIdentidad huellasDeIdentidad,
    IGeneradorDeSecretos secretos,
    IControlDeLimites limites,
    ICalculadorDeHuella huellas,
    IAvisosDeCuentaAlmacen avisosDeCuenta,
    AvisosDeCuenta avisos,
    EventosDeSeguridadDeIdentidad eventos,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IReloj reloj) : ICambioDeCorreo
{
    public const string AvisoDeCambioNoReconocido = "cambio-de-correo-no-reconocido";

    public const string AvisoDeCorreoCambiado = "correo-cambiado";

    private const string PropositoDeLosLimites = "cambio-de-correo";

    private static ErrorDeAplicacion CambioPendiente { get; } = new(CategoriaDeError.Conflicto, "cambio-pendiente");

    private static ErrorDeAplicacion SinCambioPendiente { get; } = new(CategoriaDeError.Conflicto, "sin-cambio-pendiente");

    private static ErrorDeAplicacion CambioVencido { get; } = new(CategoriaDeError.Conflicto, "cambio-vencido");

    private static ErrorDeAplicacion DispositivoNoReconocido { get; } = new(CategoriaDeError.Prohibido, "dispositivo-no-reconocido");

    public async Task<Resultado<RespuestaUniforme>> Solicitar(Guid usuarioId, string correoNuevo, ValorSecreto contrasenaActual, ValorSecreto? marcaDeDispositivo, string? region, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(contrasenaActual);
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<RespuestaUniforme>.Fallo(prohibido);
        }

        var cuenta = await Cargar(usuarioId, false, cancelacion).ConfigureAwait(false);
        if (cuenta is null)
        {
            return Resultado<RespuestaUniforme>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
        }

        // El correo del personal lo asigna la administración; y cambiar el correo de una cuenta sin verificar es registrarse de nuevo.
        if (cuenta.Usuario.Rol != RolDeUsuario.Cliente)
        {
            return Resultado<RespuestaUniforme>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "solo-clientes"));
        }

        if (!cuenta.Usuario.CorreoVerificado)
        {
            return Resultado<RespuestaUniforme>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, "correo-sin-verificar"));
        }

        if (await confianza.ComprobarQueNoEsteCongelado(usuarioId, marcaDeDispositivo, cancelacion).ConfigureAwait(false) is { } congelado)
        {
            return Resultado<RespuestaUniforme>.Fallo(congelado);
        }

        if (!EntradasDeIdentidad.TryNormalizarElCorreo(correoNuevo, out var nuevo))
        {
            return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.Validacion("correoNuevo", "invalido"));
        }

        if (nuevo == cuenta.Usuario.Correo)
        {
            return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.Validacion("correoNuevo", "igual-al-actual"));
        }

        if (await reautenticacion.ContrasenaActual(cuenta, contrasenaActual, cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<RespuestaUniforme>.Fallo(rechazo);
        }

        var ahora = reloj.Ahora;

        // Los límites de SEG-19 se reservan ANTES de saber si el correo ya es de otra cuenta y en todas las ramas: exista o no, cuesta lo mismo y responde lo mismo (sin oráculo de enumeración).
        if (await SolicitudDeCodigos.Reservar(limites, huellas, nuevo, PropositoDeLosLimites, cancelacion).ConfigureAwait(false) is { } limite)
        {
            return Resultado<RespuestaUniforme>.Fallo(limite);
        }

        var yaEsDeOtraCuenta = await cuentas.PorCorreo(nuevo, cancelacion).ConfigureAwait(false) is not null;
        var (evaluada, presentado) = await confianza.Evaluar(usuarioId, marcaDeDispositivo, region, cancelacion).ConfigureAwait(false);
        var cambio = CambioDeCorreoPendiente.Pedir(secretos.NuevoIdentificador(), usuarioId, nuevo, evaluada, ahora);
        var seEnviaElCodigo = cambio.Estado == EstadoDelCambioDeCorreo.EsperandoVerificacion;
        var guardado = false;

        var resultado = await unidad.Ejecutar(
            async ct =>
            {
                // Bajo el bloqueo de la fila del usuario se relee el cambio en curso: dos solicitudes simultáneas no sustituyen el pendiente que IDE-54 protege.
                if (await Cargar(usuarioId, true, ct).ConfigureAwait(false) is null)
                {
                    return Resultado<RespuestaUniforme>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
                }

                var existente = await cambios.Obtener(usuarioId, ct).ConfigureAwait(false);
                if (existente is { Estado: EstadoDelCambioDeCorreo.PendienteDeConfirmacion } && ahora < existente.Vence)
                {
                    // Mientras el titular no confirme ni rechace el cambio no reconocido, no se pide otro (IDE-54).
                    return Resultado<RespuestaUniforme>.Fallo(CambioPendiente);
                }

                // Un correo que ya es de otra cuenta responde igual y no manda nada: quien lo prueba no aprende qué correos existen.
                if (yaEsDeOtraCuenta)
                {
                    return Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme());
                }

                // Un código anterior (de otro correo nuevo) no puede aplicar este cambio: el código no va ligado a la dirección, así que se invalida con el cambio que reemplaza (SEG-28).
                await codigos.Invalidar(usuarioId, DominioProposito.VerificacionDeCorreo, ct).ConfigureAwait(false);
                await cambios.Guardar(cambio, ct).ConfigureAwait(false);
                if (cambio.CongelaAlDispositivo)
                {
                    presentado?.Congelar(cambio.Vence);
                    if (presentado is not null)
                    {
                        await dispositivos.Guardar(presentado, ct).ConfigureAwait(false);
                    }

                    await avisosDeCuenta.Agregar(usuarioId, AvisoDeCambioNoReconocido, ahora, ct).ConfigureAwait(false);
                }

                guardado = true;
                return Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme());
            },
            cancelacion).ConfigureAwait(false);

        if (!resultado.EsExito || !guardado)
        {
            return resultado;
        }

        if (seEnviaElCodigo)
        {
            await emisionDeCodigos.EmitirA(cuenta.Usuario, cambio.CorreoNuevo, DominioProposito.VerificacionDeCorreo, cancelacion).ConfigureAwait(false);
        }
        else
        {
            await avisos.CambioDeCorreoNoReconocido(cuenta.Usuario, cancelacion).ConfigureAwait(false);
        }

        return Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme());
    }

    public async Task<Resultado<RespuestaUniforme>> VerificarElCorreoNuevo(Guid usuarioId, ValorSecreto codigo, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(codigo);
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<RespuestaUniforme>.Fallo(prohibido);
        }

        if (await Cargar(usuarioId, false, cancelacion).ConfigureAwait(false) is null)
        {
            return Resultado<RespuestaUniforme>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
        }

        var enCurso = await cambios.Obtener(usuarioId, cancelacion).ConfigureAwait(false);
        if (enCurso is not { Estado: EstadoDelCambioDeCorreo.EsperandoVerificacion })
        {
            return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.CodigoInvalido);
        }

        var huella = huellasDeIdentidad.DeCodigo(usuarioId, DominioProposito.VerificacionDeCorreo, codigo.Revelar());
        ResultadoDelCodigo? rechazado = null;
        Usuario? conElCorreoNuevo = null;

        // El intento, el consumo del código, el cambio del correo, el aviso en la cuenta y la auditoría son UNA transacción (AUD-04): si no se puede auditar, no se cambia.
        var resultado = await unidad.Ejecutar(
            async ct =>
            {
                var verificacion = await codigos.Verificar(usuarioId, DominioProposito.VerificacionDeCorreo, huella, reloj.Ahora, ct).ConfigureAwait(false);
                if (verificacion != ResultadoDelCodigo.Correcto)
                {
                    rechazado = verificacion;
                    return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.CodigoInvalido);
                }

                var cuenta = await Cargar(usuarioId, true, ct).ConfigureAwait(false);
                var cambio = await cambios.Obtener(usuarioId, ct).ConfigureAwait(false);
                if (cuenta is null || cambio is null)
                {
                    return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.CodigoInvalido);
                }

                var aplicacion = cambio.CorreoNuevoVerificado(reloj.Ahora);
                if (aplicacion != ResultadoDelCambioDeCorreo.Hecho)
                {
                    // Fuera de plazo el cambio quedó descartado: se guarda ese estado.
                    await cambios.Guardar(cambio, ct).ConfigureAwait(false);
                    return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.CodigoInvalido);
                }

                // Otra cuenta pudo registrar ese correo mientras tanto: no se aplica y la respuesta es la misma.
                if (await cuentas.PorCorreo(cambio.CorreoNuevo, ct).ConfigureAwait(false) is { } otra && otra.Usuario.Id != usuarioId)
                {
                    return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.CodigoInvalido);
                }

                cuenta.Usuario.CambiarElCorreo(cambio.CorreoNuevo);
                await cuentas.Guardar(cuenta, ct).ConfigureAwait(false);
                await cambios.Guardar(cambio, ct).ConfigureAwait(false);
                await avisosDeCuenta.Agregar(usuarioId, AvisoDeCorreoCambiado, reloj.Ahora, ct).ConfigureAwait(false);
                await auditoria.Registrar(
                    new SolicitudDeRegistro(AccionesAuditables.CorreoCambiado) { ObjetoTipo = "cuenta", ObjetoId = usuarioId.ToString("D"), Motivo = "cambio-por-el-titular" },
                    ct).ConfigureAwait(false);
                conElCorreoNuevo = cuenta.Usuario;
                return Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme());
            },
            cancelacion).ConfigureAwait(false);

        if (rechazado is { } motivo)
        {
            await eventos.CodigoRechazado(usuarioId, motivo, cancelacion).ConfigureAwait(false);
        }

        if (conElCorreoNuevo is not null)
        {
            // Solo al correo nuevo: al anterior no sale nada (IDE-53).
            await avisos.CorreoCambiado(conElCorreoNuevo, cancelacion).ConfigureAwait(false);
        }

        return resultado;
    }

    public async Task<Resultado<RespuestaUniforme>> ConfirmarElCambioPendiente(Guid usuarioId, ValorSecreto contrasenaActual, ValorSecreto? marcaDeDispositivo, string? region, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(contrasenaActual);
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<RespuestaUniforme>.Fallo(prohibido);
        }

        var cuenta = await Cargar(usuarioId, false, cancelacion).ConfigureAwait(false);
        if (cuenta is null)
        {
            return Resultado<RespuestaUniforme>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
        }

        var cambio = await cambios.Obtener(usuarioId, cancelacion).ConfigureAwait(false);
        if (cambio is not { Estado: EstadoDelCambioDeCorreo.PendienteDeConfirmacion })
        {
            return Resultado<RespuestaUniforme>.Fallo(SinCambioPendiente);
        }

        var (evaluada, _) = await confianza.Evaluar(usuarioId, marcaDeDispositivo, region, cancelacion).ConfigureAwait(false);
        if (!evaluada.DispositivoReconocido)
        {
            await alcance.Denegar(cancelacion).ConfigureAwait(false);
            return Resultado<RespuestaUniforme>.Fallo(DispositivoNoReconocido);
        }

        if (await reautenticacion.ContrasenaActual(cuenta, contrasenaActual, cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<RespuestaUniforme>.Fallo(rechazo);
        }

        // Los límites de SEG-19 se reservan ANTES de confirmar: si se exceden, el cambio sigue pendiente y no queda esperando un código que nunca saldrá.
        if (await SolicitudDeCodigos.Reservar(limites, huellas, cambio.CorreoNuevo, PropositoDeLosLimites, cancelacion).ConfigureAwait(false) is { } limite)
        {
            return Resultado<RespuestaUniforme>.Fallo(limite);
        }

        var ahora = reloj.Ahora;
        var vencido = false;
        var confirmado = await unidad.Ejecutar(
            async ct =>
            {
                // Bajo el bloqueo de la fila del usuario: si otra solicitud sustituyó el cambio mientras tanto, no se confirma ni se envía nada al correo anterior (SEG-28).
                if (await Cargar(usuarioId, true, ct).ConfigureAwait(false) is null)
                {
                    return false;
                }

                var actual = await cambios.Obtener(usuarioId, ct).ConfigureAwait(false);
                if (actual is null || actual.CorreoNuevo != cambio.CorreoNuevo)
                {
                    return false;
                }

                var resultadoDelCambio = actual.ConfirmarElTitular(ahora, desdeDispositivoReconocido: true);
                if (resultadoDelCambio == ResultadoDelCambioDeCorreo.FueraDePlazo)
                {
                    vencido = true;
                    await cambios.Guardar(actual, ct).ConfigureAwait(false);
                    return false;
                }

                if (resultadoDelCambio != ResultadoDelCambioDeCorreo.Hecho)
                {
                    return false;
                }

                await cambios.Guardar(actual, ct).ConfigureAwait(false);
                await auditoria.Registrar(
                    new SolicitudDeRegistro(AccionesAuditables.CambioDeCorreoNoReconocidoConfirmado) { ObjetoTipo = "cuenta", ObjetoId = usuarioId.ToString("D"), Motivo = "confirmado-por-el-titular" },
                    ct).ConfigureAwait(false);
                return true;
            },
            cancelacion).ConfigureAwait(false);

        if (!confirmado)
        {
            return Resultado<RespuestaUniforme>.Fallo(vencido ? CambioVencido : SinCambioPendiente);
        }

        // Confirmado, falta verificar el correo nuevo: el código sale al correo guardado en el cambio (el mismo que se comprobó bajo el bloqueo).
        await emisionDeCodigos.EmitirA(cuenta.Usuario, cambio.CorreoNuevo, DominioProposito.VerificacionDeCorreo, cancelacion).ConfigureAwait(false);
        return Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme());
    }

    public async Task<Resultado<RespuestaUniforme>> RechazarElCambioPendiente(Guid usuarioId, ValorSecreto contrasenaActual, ValorSecreto? marcaDeDispositivo, string? region, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(contrasenaActual);
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<RespuestaUniforme>.Fallo(prohibido);
        }

        var cuenta = await Cargar(usuarioId, false, cancelacion).ConfigureAwait(false);
        if (cuenta is null)
        {
            return Resultado<RespuestaUniforme>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
        }

        if (await cambios.Obtener(usuarioId, cancelacion).ConfigureAwait(false) is not { Estado: EstadoDelCambioDeCorreo.PendienteDeConfirmacion })
        {
            return Resultado<RespuestaUniforme>.Fallo(SinCambioPendiente);
        }

        var (evaluada, _) = await confianza.Evaluar(usuarioId, marcaDeDispositivo, region, cancelacion).ConfigureAwait(false);
        if (!evaluada.DispositivoReconocido)
        {
            await alcance.Denegar(cancelacion).ConfigureAwait(false);
            return Resultado<RespuestaUniforme>.Fallo(DispositivoNoReconocido);
        }

        if (await reautenticacion.ContrasenaActual(cuenta, contrasenaActual, cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<RespuestaUniforme>.Fallo(rechazo);
        }

        var vencido = false;
        var hecho = await unidad.Ejecutar(
            async ct =>
            {
                var actual = await cambios.Obtener(usuarioId, ct).ConfigureAwait(false);
                if (actual is null)
                {
                    return false;
                }

                var resultadoDelCambio = actual.RechazarElTitular(reloj.Ahora, desdeDispositivoReconocido: true);
                vencido = resultadoDelCambio == ResultadoDelCambioDeCorreo.FueraDePlazo;
                if (resultadoDelCambio is not (ResultadoDelCambioDeCorreo.Hecho or ResultadoDelCambioDeCorreo.FueraDePlazo))
                {
                    return false;
                }

                await cambios.Guardar(actual, ct).ConfigureAwait(false);
                return resultadoDelCambio == ResultadoDelCambioDeCorreo.Hecho;
            },
            cancelacion).ConfigureAwait(false);

        if (!hecho)
        {
            return Resultado<RespuestaUniforme>.Fallo(vencido ? CambioVencido : SinCambioPendiente);
        }

        // El congelamiento del dispositivo que lo pidió sigue hasta que venza (IDE-54): rechazar no lo libera, porque podría ser el del atacante.
        await eventos.CambioDeCorreoRechazado(usuarioId, cancelacion).ConfigureAwait(false);
        return Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme());
    }

    private async Task<CuentaDeIdentidad?> Cargar(Guid usuarioId, bool paraActualizar, CancellationToken cancelacion)
    {
        var cuenta = await cuentas.PorId(usuarioId, paraActualizar, cancelacion).ConfigureAwait(false);
        return cuenta is { CanceladaEn: null, Usuario.Activo: true } ? cuenta : null;
    }
}
