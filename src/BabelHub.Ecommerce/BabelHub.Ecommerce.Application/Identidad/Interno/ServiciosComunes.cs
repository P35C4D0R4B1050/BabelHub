using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using DominioProposito = BabelHub.Ecommerce.Domain.Identidad.PropositoDeCodigo;
using PropositoDelCorreo = BabelHub.Ecommerce.Application.Plataforma.PropositoDeCodigo;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>Los fallos esperados de Identidad: categorías y claves simbólicas estables (SEG-03). Ninguno lleva datos de la persona ni dice qué dato falló (IDE-03, IDE-19).</summary>
public static class FallosDeIdentidad
{
    /// <summary>Datos incorrectos, cuenta inexistente, desactivada o bloqueada: el mismo mensaje (IDE-03, IDE-08).</summary>
    public static ErrorDeAplicacion CredencialesInvalidas { get; } = new(CategoriaDeError.NoAutenticado, "credenciales-invalidas");

    /// <summary>El mensaje genérico de un código incorrecto, vencido, usado o agotado (IDE-19).</summary>
    public static ErrorDeAplicacion CodigoInvalido { get; } = new(CategoriaDeError.Validacion, "codigo-invalido");

    /// <summary>El correo ya está en uso: sin datos de la cuenta existente (IDE-13).</summary>
    public static ErrorDeAplicacion CorreoEnUso { get; } = new(CategoriaDeError.Conflicto, "correo-en-uso");

    public static ErrorDeAplicacion LimiteExcedido { get; } = new(CategoriaDeError.Limite, "limite-excedido");

    public static ErrorDeAplicacion NoDisponible { get; } = new(CategoriaDeError.NoDisponible, "servicio-no-disponible");

    /// <summary>La sesión no se puede renovar (vencida, revocada, reutilizada, invalidada o de un usuario desactivado): el mismo rechazo para todas (IDE-08, IDE-14).</summary>
    public static ErrorDeAplicacion SesionNoRenovable { get; } = new(CategoriaDeError.NoAutenticado, "sesion-no-renovable");

    public static ErrorDeAplicacion DesafioInvalido { get; } = new(CategoriaDeError.NoAutenticado, "desafio-invalido");

    public static ErrorDeAplicacion Validacion(string campo, string clave) => new(CategoriaDeError.Validacion, "validacion-invalida", [new ErrorDeCampo(campo, clave)]);
}

/// <summary>Traduce el resultado de reservar un cupo de límite al fallo que corresponde: excedido, o no disponible si el almacén de contadores no responde y la política falla cerrada (SEG-26).</summary>
public static class LimitesDeIdentidad
{
    public static ErrorDeAplicacion? Rechazo(ResultadoDeLimite resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        return resultado.Estado switch
        {
            EstadoDeLimite.Permitido => null,
            EstadoDeLimite.NoDisponible => FallosDeIdentidad.NoDisponible,
            _ => FallosDeIdentidad.LimiteExcedido,
        };
    }
}

/// <summary>
/// Emite una sesión: la renovación opaca de 256 bits (se guarda su SHA-256, con vencimiento absoluto según el rol, IDE-46) y la credencial de 15 minutos con el rol, la sucursal y el correo verificado
/// <b>actuales</b> (IDE-02). Lo usan la verificación del correo, el ingreso, la configuración del segundo factor y la renovación. Los dos valores solo llegan a la capa servidor.
/// </summary>
public sealed class EmisionDeSesion(IRepositorioDeRenovaciones renovaciones, IEmisorDeCredenciales emisor, IGeneradorDeSecretos secretos, IHuellasDeIdentidad huellas)
{
    /// <summary>Una sesión nueva (un ingreso): una familia nueva.</summary>
    public async Task<CredencialDeSesion> Nueva(Usuario usuario, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        var valor = secretos.ValorOpaco();
        var renovacion = Renovacion.Emitir(huellas.DeValorOpaco(valor), usuario.Id, secretos.NuevoIdentificador(), usuario.Rol, ahora);
        await renovaciones.Guardar(renovacion, cancelacion).ConfigureAwait(false);
        return await ConCredencial(usuario, renovacion, valor, ahora, cancelacion).ConfigureAwait(false);
    }

    /// <summary>La credencial para la renovación siguiente de una rotación ya hecha (el valor opaco es el que se le entrega al cliente).</summary>
    public async Task<CredencialDeSesion> ParaLaSiguiente(Usuario usuario, Renovacion siguiente, string valorOpaco, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        await renovaciones.Guardar(siguiente, cancelacion).ConfigureAwait(false);
        return await ConCredencial(usuario, siguiente, valorOpaco, ahora, cancelacion).ConfigureAwait(false);
    }

    private async Task<CredencialDeSesion> ConCredencial(Usuario usuario, Renovacion renovacion, string valorOpaco, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        var credencial = await emisor.Emitir(new DatosDeLaCredencial(usuario.Id, usuario.Rol, usuario.Sucursal, usuario.CorreoVerificado), ahora, cancelacion).ConfigureAwait(false);
        return new CredencialDeSesion(
            new ValorSecreto(credencial.Jwt),
            credencial.ExpiraEn,
            new ValorSecreto(valorOpaco),
            renovacion.ExpiraAbsoluta,
            usuario.Id,
            RolDeCuentaDe(usuario.Rol),
            usuario.Sucursal,
            usuario.CorreoVerificado);
    }

    public static RolDeCuenta RolDeCuentaDe(RolDeUsuario rol) => rol switch
    {
        RolDeUsuario.Cliente => RolDeCuenta.Cliente,
        RolDeUsuario.EncargadoDeSucursal => RolDeCuenta.EncargadoDeSucursal,
        RolDeUsuario.Logistica => RolDeCuenta.Logistica,
        RolDeUsuario.AdministradorCentral => RolDeCuenta.AdministradorCentral,
        _ => throw new ArgumentOutOfRangeException(nameof(rol), rol, "Rol desconocido."),
    };
}

/// <summary>
/// Emite y envía un código de un solo uso a la dirección <b>registrada</b> de la cuenta (nunca a la de la solicitud, SEG-28): genera el código con un CSPRNG, guarda solo su huella con clave (el nuevo invalida
/// el anterior, IDE-56) y lo entrega por el canal de códigos. <b>Nunca interrumpe</b> a quien lo llama (AGENTS.md §3.10): si el envío falla, deja una alerta técnica y el resultado es el mismo para la persona
/// (IDE-65). El código no se imprime ni se registra (IDE-40).
/// </summary>
public sealed class EmisionDeCodigos(ICodigosDeUnSoloUso codigos, IHuellasDeIdentidad huellas, IEnviadorDeCorreo correo, IControlDeLimites limites, IPublicadorDeAlertasTecnicas alertas, IReloj reloj)
{
    /// <summary>El motivo del catálogo cerrado para una dependencia no necesaria que falló (el correo de un código): la alerta técnica de IDE-65.</summary>
    public const string MotivoDeFallaDelCorreo = "dependencia-no-necesaria-fallida";

    public Task Emitir(Usuario usuario, DominioProposito proposito, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        return EmitirA(usuario, usuario.Correo, proposito, cancelacion);
    }

    /// <summary>
    /// Emite el código y lo envía a <paramref name="correoDestino"/>: la dirección registrada del titular, o la <b>del correo nuevo guardado en el cambio de correo en curso</b> (nunca la de la solicitud en vuelo, SEG-28).
    /// </summary>
    public async Task EmitirA(Usuario usuario, string correoDestino, DominioProposito proposito, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        var ahora = reloj.Ahora;
        var codigo = CodigoDeUnSoloUso.Generar();
        var emitido = CodigoDeUnSoloUso.Emitir(usuario.Id, proposito, huellas.DeCodigo(usuario.Id, proposito, codigo), ahora);
        await codigos.Emitir(emitido, cancelacion).ConfigureAwait(false);

        try
        {
            // El tope de 10 al día por origen del consumo del cupo de códigos (IDE-83): superado, o sin el almacén de contadores, el código no sale (falla cerrada) y la respuesta es la misma. El código ya emitido vence solo.
            var cupo = await limites.Reservar(PoliticasDeLimite.CupoDeCodigosPorOrigen, cancelacion: cancelacion).ConfigureAwait(false);
            if (!cupo.EsPermitido)
            {
                return;
            }

            var mensaje = CorreoDeCodigo.ParaElTitular(correoDestino, PropositoDelCorreoDe(proposito), new ValorSecreto(codigo));
            await correo.EnviarCodigo(mensaje, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            await AlertarSinInterrumpir().ConfigureAwait(false);
        }
    }

    private static PropositoDelCorreo PropositoDelCorreoDe(DominioProposito proposito) => proposito switch
    {
        DominioProposito.VerificacionDeCorreo => PropositoDelCorreo.VerificacionDeCuenta,
        DominioProposito.RecuperacionDeAcceso => PropositoDelCorreo.RecuperacionDeAcceso,
        _ => throw new ArgumentOutOfRangeException(nameof(proposito), proposito, "Propósito desconocido."),
    };

    private async Task AlertarSinInterrumpir()
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(MotivoDeFallaDelCorreo, reloj.Ahora)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Sin a dónde mandar la falla: el flujo sigue con la misma respuesta.
        }
    }
}

/// <summary>Registra los eventos de seguridad de Identidad que no conceden nada (IDE-19, IDE-22, D02): nunca interrumpen y el identificador de la cuenta solo se guarda si existe.</summary>
public sealed class EventosDeSeguridadDeIdentidad(IRegistroDeSeguridad registro)
{
    public Task IngresoFallido(Guid? usuarioId, CancellationToken cancelacion) =>
        Registrar(AccionesAuditables.IngresoFallido, "credenciales-invalidas", usuarioId, cancelacion);

    public Task BloqueoTemporal(Guid? usuarioId, CancellationToken cancelacion) =>
        Registrar(AccionesAuditables.BloqueoTemporalAplicado, "intentos-excedidos", usuarioId, cancelacion);

    public Task SegundoFactorFallido(Guid usuarioId, CancellationToken cancelacion) =>
        Registrar(AccionesAuditables.SegundoFactorFallido, "codigo-invalido", usuarioId, cancelacion);

    public Task CodigoRechazado(Guid? usuarioId, ResultadoDelCodigo resultado, CancellationToken cancelacion) =>
        Registrar(AccionesAuditables.CodigoDeUnSoloUsoRechazado, MotivoDelCodigo(resultado), usuarioId, cancelacion);

    public Task RenovacionReutilizada(Guid usuarioId, CancellationToken cancelacion) =>
        Registrar(AccionesAuditables.RenovacionReutilizada, "reutilizacion-de-renovacion", usuarioId, cancelacion);

    public Task CambioDeCorreoRechazado(Guid usuarioId, CancellationToken cancelacion) =>
        Registrar(AccionesAuditables.CambioDeCorreoNoReconocidoRechazado, "rechazado-por-el-titular", usuarioId, cancelacion);

    /// <summary>Un aviso de seguridad no salió por correo (IDE-79, AUD-43): por la cuota, un tope por cuenta o por origen o un correo sin verificar. Guarda la cuenta y el motivo, nunca el correo ni el contenido.</summary>
    public Task AvisoSuprimido(Guid usuarioId, string motivo, CancellationToken cancelacion) =>
        Registrar(AccionesAuditables.AvisoDeSeguridadSuprimido, motivo, usuarioId, cancelacion);

    public Task RecuperacionSolicitada(Guid? usuarioId, CancellationToken cancelacion) =>
        Registrar(AccionesAuditables.RecuperacionDeAccesoSolicitada, "solicitud-de-recuperacion", usuarioId, cancelacion);

    private static string MotivoDelCodigo(ResultadoDelCodigo resultado) => resultado switch
    {
        ResultadoDelCodigo.Incorrecto => "incorrecto",
        ResultadoDelCodigo.Vencido => "vencido",
        ResultadoDelCodigo.NoVigente => "no-vigente",
        ResultadoDelCodigo.IntentosAgotados => "intentos-agotados",
        _ => throw new ArgumentOutOfRangeException(nameof(resultado), resultado, "Solo un rechazo se registra como evento."),
    };

    private async Task Registrar(AccionAuditable accion, string motivo, Guid? usuarioId, CancellationToken cancelacion)
    {
        var solicitud = new SolicitudDeRegistro(accion)
        {
            Motivo = motivo,
            Resultado = ResultadoAuditado.Rechazado,
            ObjetoTipo = usuarioId is null ? null : "cuenta",
            ObjetoId = usuarioId?.ToString("D"),
        };
        try
        {
            await registro.Registrar(solicitud, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // IRegistroDeSeguridad ya deja su alerta técnica si no puede escribir; un evento que no se escribe nunca abre una puerta.
        }
    }
}
