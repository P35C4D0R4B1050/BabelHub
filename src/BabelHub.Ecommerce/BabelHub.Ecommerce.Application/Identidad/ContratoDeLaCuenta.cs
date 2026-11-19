using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Identidad;

// Las operaciones de las tareas T18 a T25 (IDE-4): sus modelos se declaran aquí para que la Api y la web dependan solo de esta raíz y se afinan al implementarlas.

/// <summary>La cuenta del cliente tal como se muestra y se corrige (IDE-24, IDE-25, IDE-31, IDE-37, IDE-52): nunca el resumen de la contraseña, el secreto del segundo factor ni los códigos.</summary>
public sealed record CuentaDelCliente(
    Guid UsuarioId,
    string Nombre,
    string Correo,
    bool CorreoVerificado,
    string? NumeroDeWhatsApp,
    bool ConsentimientoDeWhatsApp,
    bool SegundoFactorActivo,
    int CodigosDeRespaldoRestantes);

/// <summary>Un aviso de seguridad que se muestra en la cuenta (IDE-53, IDE-76, IDE-79): sin datos sensibles.</summary>
public sealed record AvisoDeCuenta(Guid Id, string Tipo, DateTimeOffset Fecha, bool Visto);

/// <summary>La oferta del consentimiento de avisos por WhatsApp para Carrito (CAR-18): si se ofrece y la huella enmascarada del número; <b>el número nunca sale de Identidad</b>.</summary>
public sealed record OfertaDeConsentimientoDeAvisos(bool SeOfrece, bool YaConsiente, string? HuellaEnmascaradaDelNumero);

/// <summary>
/// Consulta y corrección de la cuenta, cambio de contraseña, número y consentimiento y segundo factor de un cliente (IDE-24, IDE-25, IDE-31, IDE-37, IDE-52). <b>Solo la propia persona opera sobre su cuenta</b> (el actor se compara
/// con el identificador en la Aplicación, AGENTS.md §5): otro actor recibe <c>Prohibido</c> y la denegación queda registrada. El cambio de correo es de <see cref="ICambioDeCorreo"/>.
/// </summary>
public interface ICuentaDelCliente
{
    Task<Resultado<CuentaDelCliente>> Consultar(Guid usuarioId, CancellationToken cancelacion = default);

    Task<Resultado<CuentaDelCliente>> CorregirElNombre(Guid usuarioId, string nombre, CancellationToken cancelacion = default);

    /// <summary>
    /// Cambia la contraseña con la actual (IDE-25): revoca las demás renovaciones y conserva la de la sesión desde la que se hace (<paramref name="renovacionDeLaSesion"/>; sin ella, todas), audita el cambio y avisa al correo
    /// (IDE-44, IDE-45). La contraseña actual equivocada cuenta contra el límite de ingresos fallidos.
    /// </summary>
    Task<Resultado<RespuestaUniforme>> CambiarLaContrasena(Guid usuarioId, ValorSecreto actual, ValorSecreto nueva, ValorSecreto? renovacionDeLaSesion = null, CancellationToken cancelacion = default);

    /// <summary>Guarda el número de WhatsApp (E.164) o lo quita con <see langword="null"/>: deja sin efecto el consentimiento hasta confirmarlo para ese número y cancela los avisos pendientes (IDE-31).</summary>
    Task<Resultado<CuentaDelCliente>> GuardarElNumeroDeWhatsApp(Guid usuarioId, string? numero, CancellationToken cancelacion = default);

    /// <summary>Concede o revoca el consentimiento de avisos citando el número al que se refiere (IDE-11, IDE-31); revocarlo cancela los avisos pendientes.</summary>
    Task<Resultado<CuentaDelCliente>> CambiarElConsentimientoDeWhatsApp(Guid usuarioId, bool consiente, string? numeroConfirmado, CancellationToken cancelacion = default);

    /// <summary>Si se ofrece el consentimiento a Carrito y la huella enmascarada del número (CAR-18); sin número, no se ofrece.</summary>
    Task<Resultado<OfertaDeConsentimientoDeAvisos>> ConsultarLaOfertaDeConsentimiento(Guid usuarioId, CancellationToken cancelacion = default);

    /// <summary>Guarda el consentimiento si la huella enmascarada es la del número vigente; si el número cambió, falla y no guarda (CAR-18).</summary>
    Task<Resultado<RespuestaUniforme>> GuardarElConsentimientoPorHuella(Guid usuarioId, string huellaEnmascaradaDelNumero, CancellationToken cancelacion = default);

    /// <summary>Empieza a activar el segundo factor (IDE-37): pide la contraseña actual y entrega el secreto una sola vez; no se activa hasta confirmarlo con un código.</summary>
    Task<Resultado<SecretoDelSegundoFactor>> IniciarElSegundoFactor(Guid usuarioId, ValorSecreto contrasenaActual, CancellationToken cancelacion = default);

    /// <summary>Activa el segundo factor con un código válido, entrega los 8 códigos de respaldo una sola vez y revoca las demás renovaciones (IDE-37, IDE-44).</summary>
    Task<Resultado<IReadOnlyList<ValorSecreto>>> ConfirmarElSegundoFactor(Guid usuarioId, ValorSecreto codigo, ValorSecreto? renovacionDeLaSesion = null, CancellationToken cancelacion = default);

    /// <summary>Desactiva el segundo factor con la contraseña y un código vigente (IDE-37); revoca las demás renovaciones.</summary>
    Task<Resultado<RespuestaUniforme>> DesactivarElSegundoFactor(Guid usuarioId, ValorSecreto contrasenaActual, ValorSecreto codigo, ValorSecreto? renovacionDeLaSesion = null, CancellationToken cancelacion = default);

    /// <summary>Regenera los 8 códigos de respaldo con la contraseña y un código vigente; invalida los anteriores (IDE-52).</summary>
    Task<Resultado<IReadOnlyList<ValorSecreto>>> RegenerarLosCodigosDeRespaldo(Guid usuarioId, ValorSecreto contrasenaActual, ValorSecreto codigo, CancellationToken cancelacion = default);

    Task<Resultado<IReadOnlyList<AvisoDeCuenta>>> ListarLosAvisos(Guid usuarioId, CancellationToken cancelacion = default);

    /// <summary>Marca un aviso como visto; solo cuenta desde una sesión reconocida (WEB-36, WEB-39, IDE-76).</summary>
    Task<Resultado<RespuestaUniforme>> MarcarElAvisoComoVisto(Guid usuarioId, Guid avisoId, bool desdeUnaSesionReconocida, CancellationToken cancelacion = default);
}

/// <summary>El cambio de correo con verificación del nuevo y la confianza en el dispositivo y la región (IDE-25, IDE-53 a IDE-55): T20.</summary>
public interface ICambioDeCorreo
{
    /// <summary>
    /// Pide el cambio de correo con la contraseña actual. Desde un <b>dispositivo reconocido y una región habitual</b> espera el código del correo nuevo y se aplica; desde otro, no se aplica, queda 24 horas pendiente, se alerta
    /// por el correo anterior y en la cuenta y el dispositivo queda congelado (IDE-54).
    /// </summary>
    Task<Resultado<RespuestaUniforme>> Solicitar(Guid usuarioId, string correoNuevo, ValorSecreto contrasenaActual, ValorSecreto? marcaDeDispositivo, string? region, CancellationToken cancelacion = default);

    /// <summary>Confirma el correo nuevo con el código que llegó a él (IDE-53); si el cambio estaba pendiente de confirmación del titular, falla hasta que lo confirme.</summary>
    Task<Resultado<RespuestaUniforme>> VerificarElCorreoNuevo(Guid usuarioId, ValorSecreto codigo, CancellationToken cancelacion = default);

    /// <summary>El titular confirma un cambio pendiente desde un dispositivo reconocido, con su contraseña (IDE-54).</summary>
    Task<Resultado<RespuestaUniforme>> ConfirmarElCambioPendiente(Guid usuarioId, ValorSecreto contrasenaActual, ValorSecreto? marcaDeDispositivo, string? region, CancellationToken cancelacion = default);

    /// <summary>El titular rechaza un cambio pendiente desde un dispositivo reconocido (IDE-54).</summary>
    Task<Resultado<RespuestaUniforme>> RechazarElCambioPendiente(Guid usuarioId, ValorSecreto? marcaDeDispositivo, string? region, CancellationToken cancelacion = default);
}

public enum TipoDeSolicitudDeDatos
{
    Acceso,
    Rectificacion,
    Cancelacion,
    Oposicion,
}

/// <summary>Una solicitud sobre datos personales con su plazo en días hábiles (IDE-12, IDE-26, IDE-32): la lista no lleva datos personales.</summary>
public sealed record SolicitudSobreDatos(Guid Id, TipoDeSolicitudDeDatos Tipo, string Estado, DateOnly Recibida, DateOnly FechaLimite, bool Vencida);

/// <summary>Registro y atención de las solicitudes sobre datos personales (IDE-12, IDE-26, IDE-29, IDE-32, IDE-63).</summary>
public interface ISolicitudesDeDatos
{
    Task<Resultado<SolicitudSobreDatos>> Presentar(Guid usuarioId, TipoDeSolicitudDeDatos tipo, CancellationToken cancelacion = default);

    /// <summary>La lista del administrador central ordenada por fecha límite, sin datos personales.</summary>
    Task<Resultado<IReadOnlyList<SolicitudSobreDatos>>> Listar(CancellationToken cancelacion = default);

    /// <summary>El detalle de una solicitud: solo si se puede auditar la consulta (IDE-32, AUD-04).</summary>
    Task<Resultado<SolicitudSobreDatos>> Consultar(Guid solicitudId, CancellationToken cancelacion = default);

    Task<Resultado<SolicitudSobreDatos>> Responder(Guid solicitudId, string resolucion, string motivo, CancellationToken cancelacion = default);
}

public enum EstadoDeLaCancelacion
{
    SinSolicitud,
    Diferida,
    EsperandoReconfirmacion,
    Ejecutada,
    Descartada,
}

/// <summary>El estado y los plazos de la cancelación de una cuenta (IDE-27, IDE-68, IDE-71, IDE-76, IDE-81): pendientes como números, sin detalle de otros módulos.</summary>
public sealed record EstadoDeLaCancelacionDeCuenta(EstadoDeLaCancelacion Estado, int PendientesAbiertos, DateTimeOffset? ReconfirmarHasta, DateTimeOffset? SeDescartaEl, bool AvisoMostrado);

/// <summary>Cancelación de la cuenta (IDE-27, IDE-28, IDE-47, IDE-62, IDE-68 a IDE-71, IDE-75, IDE-81): se pide con contraseña y segundo factor y se ejecuta en una sola transacción.</summary>
public interface ICancelacionDeCuenta
{
    Task<Resultado<EstadoDeLaCancelacionDeCuenta>> Solicitar(Guid usuarioId, ValorSecreto contrasena, ValorSecreto? codigoDelSegundoFactor, CancellationToken cancelacion = default);

    Task<Resultado<EstadoDeLaCancelacionDeCuenta>> Consultar(Guid usuarioId, CancellationToken cancelacion = default);

    Task<Resultado<EstadoDeLaCancelacionDeCuenta>> Reconfirmar(Guid usuarioId, ValorSecreto contrasena, CancellationToken cancelacion = default);

    Task<Resultado<EstadoDeLaCancelacionDeCuenta>> Desistir(Guid usuarioId, CancellationToken cancelacion = default);
}

/// <summary>Un usuario del personal o un cliente como lo ve la administración (IDE-04 a IDE-07, IDE-49, IDE-87).</summary>
public sealed record UsuarioAdministrado(Guid UsuarioId, string Nombre, string Correo, RolDeCuenta Rol, Guid? Sucursal, bool Activo, bool CorreoVerificado);

/// <summary>El resultado de crear un usuario del personal o restablecer su contraseña: la contraseña temporal se muestra <b>solo al administrador</b> una vez y vence a las 24 horas (IDE-23).</summary>
public sealed record ContrasenaTemporalEntregada(UsuarioAdministrado Usuario, ValorSecreto ContrasenaTemporal, DateTimeOffset VenceEn);

/// <summary>Administración de usuarios por el administrador central (IDE-04 a IDE-07, IDE-23, IDE-38, IDE-44, IDE-49, IDE-66, IDE-87).</summary>
public interface IAdministracionDeUsuarios
{
    Task<Resultado<ContrasenaTemporalEntregada>> Crear(Guid administradorId, string nombre, string correo, RolDeCuenta rol, Guid? sucursal, CancellationToken cancelacion = default);

    Task<Resultado<UsuarioAdministrado>> Actualizar(Guid administradorId, Guid usuarioId, string nombre, CancellationToken cancelacion = default);

    Task<Resultado<UsuarioAdministrado>> CambiarElRol(Guid administradorId, Guid usuarioId, RolDeCuenta rol, Guid? sucursal, CancellationToken cancelacion = default);

    Task<Resultado<UsuarioAdministrado>> Desactivar(Guid administradorId, Guid usuarioId, CancellationToken cancelacion = default);

    Task<Resultado<UsuarioAdministrado>> Reactivar(Guid administradorId, Guid usuarioId, CancellationToken cancelacion = default);

    Task<Resultado<ContrasenaTemporalEntregada>> RestablecerLaContrasena(Guid administradorId, Guid usuarioId, CancellationToken cancelacion = default);

    Task<Resultado<RespuestaUniforme>> RestablecerElSegundoFactor(Guid administradorId, Guid usuarioId, CancellationToken cancelacion = default);
}

/// <summary>Una clave pública de firma para el JWKS (IDE-35, IDE-85): jamás la privada.</summary>
public sealed record ClavePublicaDeFirma(string Kid, string Algoritmo, string ParametrosPublicos, string Estado);

/// <summary>Rotación, declaración de clave comprometida y publicación de claves y revocaciones (IDE-35, IDE-36, IDE-61, IDE-85).</summary>
public interface IClavesDeFirma
{
    Task<Resultado<ClavePublicaDeFirma>> Rotar(Guid administradorId, CancellationToken cancelacion = default);

    Task<Resultado<ClavePublicaDeFirma>> DeclararComprometida(Guid administradorId, string kid, CancellationToken cancelacion = default);

    Task<IReadOnlyList<ClavePublicaDeFirma>> Publicadas(CancellationToken cancelacion = default);
}

/// <summary>
/// El contexto de lectura del asistente (IDE-33): un token firmado de 5 minutos con <c>aud=pedidos</c> y <c>typ=lectura-asistente</c>, sin renovación y con el alcance único de leer los pedidos de ese cliente.
/// Se pide en cada consulta y vale solo para Pedidos.
/// </summary>
public interface IContextoDeLectura
{
    Task<Resultado<ValorSecreto>> Emitir(Guid clienteId, CancellationToken cancelacion = default);
}
