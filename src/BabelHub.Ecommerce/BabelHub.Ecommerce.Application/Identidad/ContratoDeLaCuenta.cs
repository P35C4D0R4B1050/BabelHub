using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Identidad;

// Las operaciones de las tareas T18 a T25 (IDE-4): sus modelos se declaran aquí para que la Api y la web dependan solo de esta raíz y se afinan al implementarlas.

/// <summary>La cuenta tal como se muestra y se corrige (IDE-24, IDE-31, IDE-37, IDE-52, IDE-90; la consulta y el nombre sirven también al personal, DD-25, aunque el nombre del tipo siga siendo el del cliente): nunca el resumen de la contraseña, el secreto del segundo factor ni los códigos.</summary>
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
    Task<Resultado<RespuestaUniforme>> CambiarLaContrasena(Guid usuarioId, ValorSecreto actual, ValorSecreto nueva, ValorSecreto? renovacionDeLaSesion = null, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default);

    /// <summary>Guarda el número de WhatsApp (E.164) o lo quita con <see langword="null"/>: deja sin efecto el consentimiento hasta confirmarlo para ese número y cancela los avisos pendientes (IDE-31).</summary>
    Task<Resultado<CuentaDelCliente>> GuardarElNumeroDeWhatsApp(Guid usuarioId, string? numero, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default);

    /// <summary>Concede o revoca el consentimiento de avisos citando el número al que se refiere (IDE-11, IDE-31); revocarlo cancela los avisos pendientes.</summary>
    Task<Resultado<CuentaDelCliente>> CambiarElConsentimientoDeWhatsApp(Guid usuarioId, bool consiente, string? numeroConfirmado, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default);

    /// <summary>Si se ofrece el consentimiento a Carrito y la huella enmascarada del número (CAR-18); sin número, no se ofrece.</summary>
    Task<Resultado<OfertaDeConsentimientoDeAvisos>> ConsultarLaOfertaDeConsentimiento(Guid usuarioId, CancellationToken cancelacion = default);

    /// <summary>Guarda el consentimiento si la huella enmascarada es la del número vigente; si el número cambió, falla y no guarda (CAR-18).</summary>
    Task<Resultado<RespuestaUniforme>> GuardarElConsentimientoPorHuella(Guid usuarioId, string huellaEnmascaradaDelNumero, CancellationToken cancelacion = default);

    /// <summary>Empieza a activar el segundo factor (IDE-37): pide la contraseña actual y entrega el secreto una sola vez; no se activa hasta confirmarlo con un código.</summary>
    Task<Resultado<SecretoDelSegundoFactor>> IniciarElSegundoFactor(Guid usuarioId, ValorSecreto contrasenaActual, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default);

    /// <summary>Activa el segundo factor con un código válido, entrega los 8 códigos de respaldo una sola vez y revoca las demás renovaciones (IDE-37, IDE-44).</summary>
    Task<Resultado<IReadOnlyList<ValorSecreto>>> ConfirmarElSegundoFactor(Guid usuarioId, ValorSecreto codigo, ValorSecreto? renovacionDeLaSesion = null, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default);

    /// <summary>Desactiva el segundo factor con la contraseña y un código vigente (IDE-37); revoca las demás renovaciones.</summary>
    Task<Resultado<RespuestaUniforme>> DesactivarElSegundoFactor(Guid usuarioId, ValorSecreto contrasenaActual, ValorSecreto codigo, ValorSecreto? renovacionDeLaSesion = null, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default);

    /// <summary>Regenera los 8 códigos de respaldo con la contraseña y un código vigente; invalida los anteriores (IDE-52).</summary>
    Task<Resultado<IReadOnlyList<ValorSecreto>>> RegenerarLosCodigosDeRespaldo(Guid usuarioId, ValorSecreto contrasenaActual, ValorSecreto codigo, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default);

    Task<Resultado<IReadOnlyList<AvisoDeCuenta>>> ListarLosAvisos(Guid usuarioId, CancellationToken cancelacion = default);

    /// <summary>Marca un aviso como visto; solo cuenta desde una sesión reconocida (WEB-36, WEB-39, IDE-76).</summary>
    Task<Resultado<RespuestaUniforme>> MarcarElAvisoComoVisto(Guid usuarioId, Guid avisoId, bool desdeUnaSesionReconocida, CancellationToken cancelacion = default);
}

/// <summary>El cambio de correo de un usuario de cualquier rol con verificación del nuevo y la confianza en el dispositivo y la región (IDE-97, IDE-53 a IDE-55, IDE-94, IDE-96): T20, T53 y T54.</summary>
public interface ICambioDeCorreo
{
    /// <summary>
    /// Pide el cambio de correo con la contraseña actual y, si el usuario tiene el segundo factor activo, un código vigente o de respaldo (IDE-97). Desde un <b>dispositivo reconocido y una región habitual</b> espera el código del correo nuevo y se aplica; desde otro, no se aplica, queda 24 horas pendiente, se alerta
    /// por el correo anterior y en la cuenta y el dispositivo queda congelado (IDE-54).
    /// </summary>
    Task<Resultado<RespuestaUniforme>> Solicitar(Guid usuarioId, string correoNuevo, ValorSecreto contrasenaActual, ValorSecreto? marcaDeDispositivo, string? region, ValorSecreto? codigoDelSegundoFactor = null, CancellationToken cancelacion = default);

    /// <summary>
    /// Confirma el correo nuevo con el código que llegó a él (IDE-53); si el cambio estaba pendiente de confirmación del titular, falla hasta que lo confirme. Al aplicarlo revoca las demás renovaciones conservando la de
    /// la sesión desde la que se verifica (<paramref name="renovacionDeLaSesion"/>; sin ella, todas), invalida los códigos de verificación y de recuperación pendientes y avisa al correo nuevo y al anterior (IDE-44, IDE-91, IDE-94).
    /// </summary>
    Task<Resultado<RespuestaUniforme>> VerificarElCorreoNuevo(Guid usuarioId, ValorSecreto codigo, ValorSecreto? renovacionDeLaSesion = null, CancellationToken cancelacion = default);

    /// <summary>El titular confirma un cambio pendiente desde un dispositivo reconocido, con su contraseña (IDE-54).</summary>
    Task<Resultado<RespuestaUniforme>> ConfirmarElCambioPendiente(Guid usuarioId, ValorSecreto contrasenaActual, ValorSecreto? marcaDeDispositivo, string? region, CancellationToken cancelacion = default);

    /// <summary>El titular rechaza un cambio pendiente desde un dispositivo reconocido y con su contraseña (IDE-54, WEB-37).</summary>
    Task<Resultado<RespuestaUniforme>> RechazarElCambioPendiente(Guid usuarioId, ValorSecreto contrasenaActual, ValorSecreto? marcaDeDispositivo, string? region, CancellationToken cancelacion = default);
}

/// <summary>El resultado de registrar el dispositivo tras un ingreso completo: la marca nueva si hubo que emitir una (la capa servidor la guarda en una cookie), o nada si el dispositivo ya era conocido (IDE-55).</summary>
public sealed record MarcaDeDispositivoEmitida(ValorSecreto? MarcaNueva);

/// <summary>
/// Los dispositivos reconocidos (IDE-55, D11): tras <b>cada ingreso completo</b> la capa servidor registra el dispositivo con la marca que traía (si traía una) y la región que entregó el borde. Solo el propio usuario
/// autenticado lo registra (el actor se compara con el identificador): una credencial solo existe tras un ingreso completo. Un dispositivo solo cuenta como reconocido pasadas 24 horas de su emisión.
/// </summary>
public interface IDispositivosDeLaCuenta
{
    Task<Resultado<MarcaDeDispositivoEmitida>> RegistrarElIngresoCompleto(Guid usuarioId, ValorSecreto? marcaPresentada, string? region, CancellationToken cancelacion = default);

    /// <summary>
    /// Si la sesión que pide algo es <b>reconocida</b> (IDE-55, IDE-76): trae la marca de un dispositivo guardado, con 24 horas de antigüedad y no congelado, y su región coincide con la habitual. Solo lo consulta el propio usuario
    /// autenticado. La web no puede afirmarlo por su cuenta (la cookie la controla quien tiene la sesión): lo decide Identidad. Sin marca o con una desconocida, <see langword="false"/>.
    /// </summary>
    Task<Resultado<bool>> Reconoce(Guid usuarioId, ValorSecreto? marcaPresentada, string? region, CancellationToken cancelacion = default);
}

public enum TipoDeSolicitudDeDatos
{
    Acceso,
    Rectificacion,
    Cancelacion,
    Oposicion,
}

/// <summary>Lo que decide la administración sobre una solicitud (IDE-26): ponerla en atención, atenderla con una respuesta o rechazarla con un motivo.</summary>
public enum ResolucionDeSolicitud
{
    EnAtencion,
    Atendida,
    Rechazada,
}

/// <summary>Una solicitud sobre datos personales con su plazo en días hábiles (IDE-12, IDE-26, IDE-32, IDE-63): la lista de la administración solo lleva identificador, tipo, estado, fecha y fecha límite, sin datos personales.</summary>
public sealed record SolicitudSobreDatos(Guid Id, TipoDeSolicitudDeDatos Tipo, string Estado, DateOnly Recibida, DateOnly FechaLimite, bool Vencida);

/// <summary>Una solicitud como la ve su titular en su cuenta (IDE-26): con la respuesta o el motivo del rechazo.</summary>
public sealed record SolicitudDelCliente(SolicitudSobreDatos Solicitud, string? Respuesta, string? Motivo);

/// <summary>El detalle que ve la administración al abrir una solicitud (IDE-63): solo se entrega si la consulta quedó auditada; el titular va por su identificador opaco.</summary>
public sealed record DetalleDeSolicitud(SolicitudSobreDatos Solicitud, Guid UsuarioId, string? Respuesta, string? Motivo);

/// <summary>
/// Registro y atención de las solicitudes sobre datos personales (IDE-12, IDE-26, IDE-29, IDE-32, IDE-63). <b>Solo la propia persona presenta y lista las suyas</b> (con el correo verificado, IDE-29) y <b>solo el
/// administrador central</b> activo lista, abre y responde las de todos; cualquier otro recibe <c>Prohibido</c> con la denegación registrada.
/// </summary>
public interface ISolicitudesDeDatos
{
    Task<Resultado<SolicitudSobreDatos>> Presentar(Guid usuarioId, TipoDeSolicitudDeDatos tipo, CancellationToken cancelacion = default);

    /// <summary>Las solicitudes del propio titular con la respuesta o el motivo (IDE-26).</summary>
    Task<Resultado<IReadOnlyList<SolicitudDelCliente>>> ListarLasDelCliente(Guid usuarioId, CancellationToken cancelacion = default);

    /// <summary>La lista de la administración central ordenada por fecha límite, con las vencidas sin atender marcadas (IDE-32), sin datos personales (IDE-63).</summary>
    Task<Resultado<IReadOnlyList<SolicitudSobreDatos>>> Listar(Guid administradorId, CancellationToken cancelacion = default);

    /// <summary>El detalle de una solicitud: la consulta se audita en la misma operación y, si no se puede auditar, no se muestra (IDE-63, AUD-04).</summary>
    Task<Resultado<DetalleDeSolicitud>> Consultar(Guid administradorId, Guid solicitudId, CancellationToken cancelacion = default);

    /// <summary>
    /// Pone la solicitud en atención, la atiende con una <paramref name="respuesta"/> o la rechaza con un <paramref name="motivo"/> (IDE-26): audita, deja la respuesta en la cuenta del titular y le avisa por correo
    /// sin el contenido.
    /// </summary>
    Task<Resultado<SolicitudSobreDatos>> Responder(Guid administradorId, Guid solicitudId, ResolucionDeSolicitud resolucion, string? respuesta, string? motivo, CancellationToken cancelacion = default);
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
public sealed record EstadoDeLaCancelacionDeCuenta(EstadoDeLaCancelacion Estado, int PendientesAbiertos, DateTimeOffset? ReconfirmarHasta, DateTimeOffset? SeDescartaEl, bool AvisoMostrado, DesgloseDePendientes? Desglose = null);

/// <summary>
/// Cancelación de la cuenta de un cliente (IDE-27, IDE-28, IDE-47, IDE-62, IDE-68 a IDE-71, IDE-75, IDE-76, IDE-81). Solo el titular la pide, la reconfirma, desiste y la consulta; la <b>advertencia</b> de que perderá su
/// biblioteca digital la muestra la interfaz antes de pedir la contraseña y el segundo factor (IDE-28). Sin pendientes se ejecuta en esa misma operación; con pendientes queda <b>diferida</b> (no puede comprar, ve cuántos
/// faltan, recibe un aviso y puede desistir); al cerrarse todos recibe otro aviso y tiene 7 días, sin pasar de 30 desde la solicitud, para <b>reconfirmar</b> con su contraseña y su segundo factor; la reconfirmación consulta
/// los pendientes de nuevo en la misma operación que ejecuta.
/// </summary>
public interface ICancelacionDeCuenta
{
    Task<Resultado<EstadoDeLaCancelacionDeCuenta>> Solicitar(Guid usuarioId, ValorSecreto contrasena, ValorSecreto? codigoDelSegundoFactor, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default);

    /// <summary>El estado y los plazos de la solicitud más reciente del titular; <see cref="EstadoDeLaCancelacion.SinSolicitud"/> si no hay.</summary>
    Task<Resultado<EstadoDeLaCancelacionDeCuenta>> Consultar(Guid usuarioId, CancellationToken cancelacion = default);

    Task<Resultado<EstadoDeLaCancelacionDeCuenta>> Reconfirmar(Guid usuarioId, ValorSecreto contrasena, ValorSecreto? codigoDelSegundoFactor, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default);

    Task<Resultado<EstadoDeLaCancelacionDeCuenta>> Desistir(Guid usuarioId, CancellationToken cancelacion = default);

    /// <summary>El aviso de «ya puede reconfirmar» se mostró en una <b>sesión reconocida</b> del titular: si el correo se suprimió, el plazo de 7 días cuenta desde ahora (IDE-76). Desde otra sesión no cuenta.</summary>
    Task<Resultado<EstadoDeLaCancelacionDeCuenta>> MostrarElAviso(Guid usuarioId, bool desdeUnaSesionReconocida, CancellationToken cancelacion = default);
}

/// <summary>La marca de cancelación de una cuenta tal como la leen los módulos que deciden si se puede comprar (Carrito, Pagos; IDE-71, CAR-31).</summary>
public enum EstadoDeLaMarca
{
    /// <summary>La cuenta está activa y sin cancelación en marcha.</summary>
    Libre,

    /// <summary>Tiene una cancelación diferida o esperando la reconfirmación (IDE-27): no compra.</summary>
    Diferida,

    /// <summary>Una ejecución empezó y no terminó (IDE-70, IDE-71): no se inicia ningún pago.</summary>
    EnEjecucion,

    /// <summary>La cuenta ya se canceló, está desactivada o no existe.</summary>
    Ejecutada,
}

/// <summary>
/// La marca de cancelación leída <b>con bloqueo</b> (IDE-71): toma el bloqueo de la fila del usuario, de modo que la cancelación de una cuenta y el paso al pago de ese mismo usuario se serializan. Se llama <b>dentro de la
/// transacción</b> del módulo que decide; si no se puede leer, lanza (nunca se asume libre).
/// </summary>
public interface IMarcaDeCancelacionDeCuenta
{
    Task<EstadoDeLaMarca> LeerConBloqueo(Guid usuarioId, CancellationToken cancelacion = default);
}

/// <summary>Un usuario del personal o un cliente como lo ve la administración (IDE-04 a IDE-07, IDE-49, IDE-87).</summary>
public sealed record UsuarioAdministrado(Guid UsuarioId, string Nombre, string Correo, RolDeCuenta Rol, Guid? Sucursal, bool Activo, bool CorreoVerificado);

/// <summary>El resultado de crear un usuario del personal o restablecer su contraseña: la contraseña temporal se muestra <b>solo al administrador</b> una vez y vence a las 24 horas (IDE-23).</summary>
public sealed record ContrasenaTemporalEntregada(UsuarioAdministrado Usuario, ValorSecreto ContrasenaTemporal, DateTimeOffset VenceEn);

/// <summary>Lo que se pide al listar usuarios: filtros opcionales por rol y estado, el correo exacto (única forma de ver una cuenta de cliente), el cursor (el último correo de la página anterior) y el tamaño (1 a 50; 20 por omisión).</summary>
public sealed record FiltroDeUsuarios(RolDeCuenta? Rol = null, bool? Activo = null, string? Correo = null, string? Despues = null, int Tamano = 20)
{
    /// <summary>El tamaño de página que pide la pantalla y el tope que aplica Identidad.</summary>
    public const int TamanoMaximo = 50;
}

/// <summary>Una página de usuarios ordenada por correo; <paramref name="Siguiente"/> es el cursor de la página que sigue, o <see langword="null"/> si no hay más.</summary>
public sealed record PaginaDeUsuarios(IReadOnlyList<UsuarioAdministrado> Usuarios, string? Siguiente);

/// <summary>Administración de usuarios por el administrador central (IDE-04 a IDE-07, IDE-23, IDE-38, IDE-44, IDE-49, IDE-66, IDE-87).</summary>
/// <remarks>La sucursal de un encargado se pasa <b>por su código</b> (D20): Identidad la valida contra <c>inventario.sucursales.listar</c> y guarda el identificador derivado del código (<see cref="IdentificadorDeSucursal"/>).</remarks>
public interface IAdministracionDeUsuarios
{
    Task<Resultado<ContrasenaTemporalEntregada>> Crear(Guid administradorId, string nombre, string correo, RolDeCuenta rol, string? codigoDeSucursal, CancellationToken cancelacion = default);

    Task<Resultado<UsuarioAdministrado>> CambiarElRol(Guid administradorId, Guid usuarioId, RolDeCuenta rol, string? codigoDeSucursal, CancellationToken cancelacion = default);

    /// <summary>La lista de usuarios para la administración central (WEB-35): sin correo exacto solo el personal (la lista de clientes no se entrega); con el correo, esa cuenta. Quien llama debe ser el administrador central activo.</summary>
    Task<Resultado<PaginaDeUsuarios>> Listar(Guid administradorId, FiltroDeUsuarios filtro, CancellationToken cancelacion = default);

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

    /// <summary>Los <c>kid</c> de las claves declaradas comprometidas: la lista de revocaciones que cada servicio renueva al menos cada 5 minutos (IDE-61, IDE-85).</summary>
    Task<IReadOnlyList<string>> Revocadas(CancellationToken cancelacion = default);
}

/// <summary>
/// El contexto de lectura del asistente (IDE-33): un token firmado de 5 minutos con <c>aud=pedidos</c> y <c>typ=lectura-asistente</c>, sin renovación y con el alcance único de leer los pedidos de ese cliente.
/// Se pide en cada consulta y vale solo para Pedidos.
/// </summary>
public interface IContextoDeLectura
{
    /// <summary>Lo emite solo para el propio cliente autenticado (otro actor: <c>Prohibido</c>) y con la cuenta vigente.</summary>
    Task<Resultado<ValorSecreto>> Emitir(Guid clienteId, CancellationToken cancelacion = default);

    /// <summary>
    /// Verifica un contexto en proceso (lo usa Pedidos): firma, vigencia de 5 minutos sin renovación, <c>aud=pedidos</c>, <c>typ=lectura-asistente</c> y el alcance único de leer pedidos, con la cuenta aún vigente. Todo fallo
    /// devuelve el mismo error <c>NoAutenticado</c>, sin decir cuál comprobación falló. El único cliente a quien da acceso es el del contexto.
    /// </summary>
    Task<Resultado<ClienteDelContexto>> Verificar(ValorSecreto contexto, CancellationToken cancelacion = default);
}

/// <summary>El cliente cuyos pedidos puede leer un contexto verificado: el único dato que de él se desprende.</summary>
public sealed record ClienteDelContexto(Guid ClienteId);
