using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;
using AlertaAgrupada = BabelHub.Ecommerce.Domain.Auditoria.AlertaDeSeguridad;

namespace BabelHub.Ecommerce.Application.Auditoria.Interno;

/// <summary>
/// Los motivos del catálogo cerrado de alertas (D06): un motivo que no está aquí no se guarda con su código (un texto libre de una excepción filtraría
/// datos). Cada motivo declara su clase y si se puede enviar por correo: la alerta originada por el propio correo no se envía por correo (AUD-29).
/// </summary>
public static class MotivosDeAlerta
{
    /// <summary>Alerta de seguridad: más de 10 accesos denegados en 5 minutos (SEG-09).</summary>
    public const string AccesosDenegados = "accesos-denegados";

    /// <summary>Alerta de seguridad: más de 20 ingresos fallidos en 5 minutos (SEG-09).</summary>
    public const string IngresosFallidos = "ingresos-fallidos";

    public const string AuditoriaNoDisponible = MotivosDeAlertaDeAuditoria.AuditoriaNoDisponible;
    public const string CredencialDeServicioAnteriorEnUso = "credencial-de-servicio-anterior-en-uso";
    public const string DependenciaNoNecesariaFallida = "dependencia-no-necesaria-fallida";
    public const string TrabajoDeFondoDetenido = "trabajo-de-fondo-detenido";
    public const string MemoriaDeContadoresAlta = "memoria-de-contadores-alta";
    public const string VerificadorDeCredencialesSinClaves = "verificador-de-credenciales-sin-claves";
    public const string RevocacionDeSesionesPendiente = "revocacion-de-sesiones-pendiente";

    /// <summary>No se pudo guardar un rechazo en el acumulado de límites (la protección sigue, AUD-28).</summary>
    public const string AcumuladoDeLimitesNoDisponible = "acumulado-de-limites-no-disponible";

    /// <summary>Identidad no respondió el conteo de ingresos fallidos: el límite sigue protegiendo con el conteo local (T10).</summary>
    public const string ConteoDeIngresosNoDisponible = "conteo-de-ingresos-no-disponible";

    /// <summary>Un motivo que ningún catálogo conoce: se conserva la alerta con este código en vez de perderla (el código original no se guarda).</summary>
    public const string MotivoNoCatalogado = "motivo-de-alerta-no-catalogado";

    /// <summary>El correo de las alertas falló: <b>no se envía por correo</b> (bucle) y se reintenta (AUD-40).</summary>
    public const string CorreoDeAlertasNoDisponible = "correo-de-alertas-no-disponible";

    /// <summary>Pasaron 24 horas sin una depuración diaria exitosa (AUD-31).</summary>
    public const string DepuracionDiariaSinExito = "depuracion-diaria-sin-exito";

    /// <summary>Pasaron 24 horas sin entregar la baja de un titular al Inventario (AUD-32, IDE-64).</summary>
    public const string BajaDeTitularSinEntregar = "baja-de-titular-sin-entregar";

    /// <summary>Un traslado vinculado sigue sin aprobar a las 48 horas (TRA-22).</summary>
    public const string TrasladoVinculadoSinAprobar = "traslado-vinculado-sin-aprobar";

    public const string RetomaDePagosFallida = "retoma-de-pagos-fallida";

    public const string ReservasConfirmadasInconsistentes = "reservas-confirmadas-inconsistentes";

    public const string ReservaConfirmadaInexistente = "reserva-confirmada-inexistente";

    public const string ReservaConfirmadaSinLiberar = "reserva-confirmada-sin-liberar";

    public const string ReintentoDeReembolsosFallido = "reintento-de-reembolsos-fallido";

    public const string RegistroDelCobroFallido = "registro-del-cobro-fallido";

    public const string ReembolsoPedidoSinRegistrar = "reembolso-pedido-sin-registrar";

    public const string PurgaDeContactoFallida = "purga-de-contacto-fallida";

    public const string ConsultaDePagoFallida = "consulta-de-pago-fallida";

    public const string ConfirmacionDeCompraFallida = "confirmacion-de-compra-fallida";

    public const string CompraAprobadaSinReferencia = "compra-aprobada-sin-referencia";

    public const string CompraAprobadaSinCompletar = "compra-aprobada-sin-completar";

    public const string CompensacionNoAplicable = "compensacion-no-aplicable";

    public const string CompensacionDeCompraFallida = "compensacion-fallida";

    public const string CobroDudosoParaConciliar = "cobro-dudoso-para-conciliar";

    public const string CierreDeIntencionesFallido = "cierre-de-intenciones-fallido";

    public const string CargoDuplicadoParaConciliar = "cargo-duplicado-para-conciliar";

    public const string AvisoDePagoFallido = "aviso-de-pago-fallido";

    public const string AvisoACarritoFallido = "aviso-a-carrito-fallido";

    public const string CompraAprobadaSinPedidoALos30Dias = "compra-aprobada-sin-pedido-a-los-30-dias";

    public const string AnonimizacionDeHojasSinCorrer = "anonimizacion-de-hojas-sin-correr";

    /// <summary>Las alertas del Inventario no se pudieron leer (AUD-38): el E-commerce sigue y lo reintenta.</summary>
    public const string LecturaDeAlertasDelInventarioNoDisponible = "lectura-de-alertas-del-inventario-no-disponible";

    /// <summary>Una lectura de datos del Inventario llegó sin actor (un defecto de composición: el alcance por sucursal devolvería cero filas sin error, INV-1a).</summary>
    public const string AlcanceDeDatosSinActor = "alcance-de-datos-sin-actor";

    /// <summary>Más de 200 confirmaciones de reservas en 5 minutos (INV-2b): solo avisa.</summary>
    public const string ConfirmacionesDeReservasAnomalas = "confirmaciones-de-reservas-anomalas";

    /// <summary>Más de 200 liberaciones de reservas en 5 minutos (INV-2b): solo avisa.</summary>
    public const string LiberacionesDeReservasAnomalas = "liberaciones-de-reservas-anomalas";

    /// <summary>Más de 20 bajas de titular en un minuto (SEL-34, AM31): solo avisa, la baja nunca se rechaza.</summary>
    public const string BajasDeTitularAnomalas = "bajas-de-titular-anomalas";

    /// <summary>Muchas acciones bloqueadas por textos legales pendientes en una ventana de 15 minutos (WEB-47): no cuenta para SEG-09.</summary>
    public const string BloqueosPorTextosPendientes = "bloqueos-por-textos-pendientes";

    /// <summary>La cuota diaria de correo llegó al 80 % (IDE-73).</summary>
    public const string CuotaDeCorreoAlta = "cuota-de-correo-alta";

    /// <summary>El cupo de códigos de verificación y de recuperación llegó al 80 % (IDE-73).</summary>
    public const string CupoDeCodigosAlto = "cupo-de-codigos-alto";

    /// <summary>Alerta de seguridad: se reutilizó una renovación ya rotada y se revocó su familia (D02, IDE-14).</summary>
    public const string RenovacionReutilizada = "renovacion-reutilizada";

    /// <summary>Un trabajo de fondo de Carrito no corrió en el plazo esperado.</summary>
    public const string CarritoTrabajoSinCorrer = "carrito-trabajo-sin-correr";

    /// <summary>Hay checkouts de Carrito con el pago pendiente más tiempo del permitido.</summary>
    public const string CarritoCheckoutsConElPagoPendienteDemasiado = "carrito-checkouts-con-el-pago-pendiente-demasiado";

    /// <summary>La clave de idempotencia de una reserva de Carrito chocó con otra reserva distinta.</summary>
    public const string CarritoClaveDeReservaEnConflicto = "carrito-clave-de-reserva-en-conflicto";

    /// <summary>Carrito intentó liberar la reserva de un pedido que ya estaba confirmado (T26c).</summary>
    public const string CarritoLiberacionDeUnPedidoConfirmado = "carrito-liberacion-de-un-pedido-confirmado";
}

/// <summary>El catálogo cerrado de motivos de alerta de los dos servicios (D06). Otros módulos publican por <see cref="IAlertas"/> con estos códigos.</summary>
public static class CatalogoDeAlertasDeAuditoria
{
    public static CatalogoDeMotivosDeAlerta Crear() => new(
    [
        new(MotivosDeAlerta.AccesosDenegados, ClaseDeAlerta.Seguridad),
        new(MotivosDeAlerta.IngresosFallidos, ClaseDeAlerta.Seguridad),
        new(MotivosDeAlerta.AuditoriaNoDisponible, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.CredencialDeServicioAnteriorEnUso, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.DependenciaNoNecesariaFallida, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.TrabajoDeFondoDetenido, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.MemoriaDeContadoresAlta, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.VerificadorDeCredencialesSinClaves, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.RevocacionDeSesionesPendiente, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.AcumuladoDeLimitesNoDisponible, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.ConteoDeIngresosNoDisponible, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.MotivoNoCatalogado, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.CorreoDeAlertasNoDisponible, ClaseDeAlerta.Tecnica, enviablePorCorreo: false),
        new(MotivosDeAlerta.DepuracionDiariaSinExito, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.BajaDeTitularSinEntregar, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.TrasladoVinculadoSinAprobar, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.RetomaDePagosFallida, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.ReservasConfirmadasInconsistentes, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.ReservaConfirmadaInexistente, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.ReservaConfirmadaSinLiberar, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.ReintentoDeReembolsosFallido, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.RegistroDelCobroFallido, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.ReembolsoPedidoSinRegistrar, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.PurgaDeContactoFallida, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.ConsultaDePagoFallida, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.ConfirmacionDeCompraFallida, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.CompraAprobadaSinReferencia, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.CompraAprobadaSinCompletar, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.CompensacionNoAplicable, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.CompensacionDeCompraFallida, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.CobroDudosoParaConciliar, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.CierreDeIntencionesFallido, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.CargoDuplicadoParaConciliar, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.AvisoDePagoFallido, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.AvisoACarritoFallido, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.CompraAprobadaSinPedidoALos30Dias, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.AnonimizacionDeHojasSinCorrer, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.LecturaDeAlertasDelInventarioNoDisponible, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.AlcanceDeDatosSinActor, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.ConfirmacionesDeReservasAnomalas, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.LiberacionesDeReservasAnomalas, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.BajasDeTitularAnomalas, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.BloqueosPorTextosPendientes, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.CuotaDeCorreoAlta, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.CupoDeCodigosAlto, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.RenovacionReutilizada, ClaseDeAlerta.Seguridad),
        new(MotivosDeAlerta.CarritoTrabajoSinCorrer, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.CarritoCheckoutsConElPagoPendienteDemasiado, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.CarritoClaveDeReservaEnConflicto, ClaseDeAlerta.Tecnica),
        new(MotivosDeAlerta.CarritoLiberacionDeUnPedidoConfirmado, ClaseDeAlerta.Tecnica),
    ]);
}

/// <summary>
/// Almacén de las alertas del servicio (el registro técnico, <c>auditoria.alertas</c>, T22 y T25): la Infraestructura lo implementa. Las operaciones
/// de escritura corren dentro de la transacción de <see cref="IUnidadDeTrabajo"/>; las alertas no son registros de auditoría y pueden cambiar.
/// </summary>
public interface IAlmacenDeAlertas
{
    /// <summary>La alerta más reciente del servicio y motivo, con bloqueo asesor por servicio y motivo en la transacción en curso: dos ocurrencias simultáneas no abren dos alertas.</summary>
    Task<AlertaAgrupada?> UltimaDelMotivoParaActualizar(ServicioDeOrigen servicio, string motivo, CancellationToken cancelacion = default);

    /// <summary>
    /// La alerta por su identificador. <b>Dentro de una unidad de trabajo toma el mismo bloqueo asesor por servicio y motivo</b> que <see cref="UltimaDelMotivoParaActualizar"/>, o <see cref="Guardar"/> actualiza
    /// por columnas (<c>ocurrencias = ocurrencias + 1</c>, <c>revisadas_hasta = GREATEST(…)</c>): marcar como revisada y sumar una ocurrencia al mismo tiempo no pueden pisarse (AUD-34, AUD-39); la
    /// prueba de concurrencia con PostgreSQL real es de AUD-4.
    /// </summary>
    Task<AlertaAgrupada?> Obtener(string id, CancellationToken cancelacion = default);

    /// <summary>Inserta la alerta o actualiza la existente (mismo identificador).</summary>
    Task Guardar(AlertaAgrupada alerta, CancellationToken cancelacion = default);

    /// <summary>
    /// Las alertas de seguridad del servicio de la más reciente a la más antigua por <c>(última ocurrencia, identificador)</c>, a partir de la posición
    /// indicada (excluida) y hasta <paramref name="maximo"/>; las técnicas no tienen pantalla.
    /// </summary>
    Task<IReadOnlyList<AlertaAgrupada>> ListarDeSeguridad(PosicionDeAlerta? despuesDe, int maximo, CancellationToken cancelacion = default);

    /// <summary>
    /// Las alertas (de seguridad y técnicas) con ocurrencia posterior a <paramref name="desde"/> (lectura del Inventario por el E-commerce, AUD-38), <b>en orden ascendente por <c>(última ocurrencia, id)</c></b> y hasta
    /// <paramref name="maximo"/>: si hay más, el lector sigue desde la última que vio (la marca avanza solo hasta ahí).
    /// </summary>
    Task<IReadOnlyList<AlertaAgrupada>> Desde(DateTimeOffset desde, int maximo, CancellationToken cancelacion = default);
}

/// <summary>La posición de una alerta en el orden de la lista: su última ocurrencia (UTC) y su identificador.</summary>
public sealed record PosicionDeAlerta(DateTimeOffset UltimaOcurrencia, string Id);
