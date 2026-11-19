using BabelHub.Inventario.Domain.Auditoria;

namespace BabelHub.Inventario.Application.Auditoria.Interno;

public static partial class CatalogoDeAccionesDeAuditoria
{
    /// <summary>
    /// Los eventos de seguridad (T15; AUD-05, AUD-10, AUD-28, AUD-33, AUD-37): cada uno con su motivo de catálogo cerrado (nunca el texto de una
    /// solicitud). Los de un efecto que no concede nada (un rechazo, un bloqueo, un intento) son de seguridad y van por <c>IRegistroDeSeguridad</c>; los
    /// que concedan o cambien algo (un código de respaldo, una contraseña, un correo, el segundo factor) son de ambos tipos y van por
    /// <c>IAuditoria</c>, de modo que sin auditoría no se aplican (AUD-04, AUD-10, AUD-33). El identificador de la cuenta solo se guarda si existe.
    /// </summary>
    private static IEnumerable<AccionDelCatalogo> AccionesDeEventosDeSeguridad()
    {
        // Plataforma (no aislados).
        yield return Seguridad(nameof(AccionesAuditables.AccesoDenegado), false, ["fuera-de-rol", "fuera-de-sucursal", "recurso-ajeno", "sin-sesion", "sin-credencial-de-servicio", "alcance-insuficiente"], Clave("codigoDeError"));
        yield return Seguridad(nameof(AccionesAuditables.LimiteExcedido), false, MotivosDeLimiteExcedido, Clave("totalDeRechazos"));
        yield return Seguridad(nameof(AccionesAuditables.PresupuestoDelPersonalExcedido), false, ["presupuesto-excedido"]);
        yield return Seguridad(nameof(AccionesAuditables.LlamadaEntreServiciosSinCredencial), false, ["sin-credencial-de-servicio"]);
        yield return Seguridad(nameof(AccionesAuditables.OrigenIndeterminable), false, ["origen-no-determinable"]);
        yield return Seguridad(nameof(AccionesAuditables.VerificacionDePersonaFallida), false, ["verificacion-fallida"]);
        yield return Seguridad(nameof(AccionesAuditables.EntradaDeIntegracionRechazada), false, MotivosDeIntegracionRechazada, Clave("proveedor", 32));

        // Identidad (aislados). El objeto es la cuenta, un dato personal; en un ingreso fallido solo si la cuenta existe.
        yield return Seguridad(nameof(AccionesAuditables.IngresoFallido), true, ["credenciales-invalidas"], aislada: true);
        yield return Seguridad(nameof(AccionesAuditables.BloqueoTemporalAplicado), true, ["intentos-excedidos"], aislada: true);
        yield return Seguridad(nameof(AccionesAuditables.RecuperacionDeAccesoSolicitada), true, ["solicitud-de-recuperacion"], aislada: true);
        yield return Seguridad(nameof(AccionesAuditables.SegundoFactorFallido), true, ["codigo-invalido"], aislada: true);
        yield return Seguridad(nameof(AccionesAuditables.CodigoDeUnSoloUsoRechazado), true, ["incorrecto", "vencido", "no-vigente", "intentos-agotados"], aislada: true);
        yield return Seguridad(nameof(AccionesAuditables.RenovacionReutilizada), true, ["reutilizacion-de-renovacion"], aislada: true);
        yield return Seguridad(nameof(AccionesAuditables.CambioDeCorreoNoReconocidoRechazado), true, ["rechazado-por-el-titular"], aislada: true);
        yield return Seguridad(nameof(AccionesAuditables.CambioDeCorreoNoReconocidoVencido), true, ["vencido-sin-confirmar"], aislada: true);

        yield return AuditadaYSeguridadAislada(nameof(AccionesAuditables.RecuperacionDeAccesoCompletada), true, ["recuperacion-completada"]);
        yield return AuditadaYSeguridadAislada(nameof(AccionesAuditables.CodigoDeRespaldoUsado), true, ["uso-de-codigo-de-respaldo"]);
        yield return AuditadaYSeguridadAislada(nameof(AccionesAuditables.ContrasenaCambiada), true, ["cambio-por-el-titular"]);
        yield return AuditadaYSeguridadAislada(nameof(AccionesAuditables.CorreoCambiado), true, ["cambio-por-el-titular"]);
        yield return AuditadaYSeguridadAislada(nameof(AccionesAuditables.SegundoFactorActivado), true, ["activacion"]);
        yield return AuditadaYSeguridadAislada(nameof(AccionesAuditables.SegundoFactorDesactivado), true, ["desactivacion"]);
        yield return AuditadaYSeguridadAislada(nameof(AccionesAuditables.SegundoFactorRegenerado), true, ["regeneracion"]);
        yield return AuditadaYSeguridadAislada(nameof(AccionesAuditables.CambioDeCorreoNoReconocidoConfirmado), true, ["confirmado-por-el-titular"]);
    }

    /// <summary>Las políticas de límite de los dos servicios cuyo exceso es un evento de seguridad (AUD-28): una prueba comprueba que las claves de la plataforma de cada servicio estén aquí.</summary>
    public static readonly IReadOnlyList<string> MotivosDeLimiteExcedido =
    [
        "ingresos-fallidos",
        "codigos-por-correo",
        "codigos-por-origen",
        "segundo-factor-por-origen",
        "segundo-factor-por-cuenta",
        "registros-por-origen",
        "hojas-por-origen",
        "cobros-por-intencion",
        "cobros-por-cuenta",
        "cobros-por-origen",
        "solicitudes-previas-por-origen",
        "solicitudes-del-origen-desconocido",
        "solicitudes-sin-sesion-por-origen",
        "solicitudes-por-usuario",
        "solicitudes-por-huella-directa",
        "ajustes-del-personal",
        "traslados-del-personal",
        "reservas-pendiente-por-cliente",
        "reservas-por-hora-por-cliente",
        "reservas-por-hora-por-origen",
        "reservas-reemplazos-por-pedido",
        "subidas-del-catalogo",
    ];

    /// <summary>Los motivos por los que se rechaza una llamada de integración (SEG-12): una prueba compara esta lista con los de la plataforma.</summary>
    public static readonly IReadOnlyList<string> MotivosDeIntegracionRechazada =
    [
        "proveedor-desconocido",
        "id-invalido",
        "cuerpo-demasiado-grande",
        "marca-invalida",
        "marca-fuera-de-tolerancia",
        "sin-firma",
        "firma-invalida",
    ];

    /// <summary>Un evento de seguridad que no concede nada: se registra por <c>IRegistroDeSeguridad</c>, nunca interrumpe y siempre lleva motivo de catálogo.</summary>
    private static AccionDelCatalogo Seguridad(string identificador, bool objetoPersonal, IReadOnlyCollection<string> motivos, params ClaveDeDetalle[] detalle) =>
        Seguridad(identificador, objetoPersonal, motivos, aislada: false, detalle);

    private static AccionDelCatalogo Seguridad(string identificador, bool objetoPersonal, IReadOnlyCollection<string> motivos, bool aislada, params ClaveDeDetalle[] detalle) =>
        new(identificador, ClaseDeAccion.NoConcede, TipoDeRegistro.Seguridad, aislada, detalle, objetoPersonal, rolPersonal: false, motivos, exigeMotivo: true);
}
