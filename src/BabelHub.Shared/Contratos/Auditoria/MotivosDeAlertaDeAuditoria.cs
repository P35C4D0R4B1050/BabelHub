namespace BabelHub.Shared.Contratos.Auditoria;

/// <summary>
/// El catálogo cerrado de motivos de alerta (D06) tal como viaja en <c>motivo</c> de <see cref="AlertaDto"/>: nunca el texto de una excepción. Una prueba de contrato de cada servicio compara esta lista con el
/// catálogo de su módulo <c>Auditoria</c> (T21).
/// </summary>
public static class MotivosDeAlertaDeAuditoria
{
    public const string AuditoriaNoDisponible = "auditoria-no-disponible";

    public const string AccesosDenegados = "accesos-denegados";

    public const string IngresosFallidos = "ingresos-fallidos";

    public const string CredencialDeServicioAnteriorEnUso = "credencial-de-servicio-anterior-en-uso";

    public const string DependenciaNoNecesariaFallida = "dependencia-no-necesaria-fallida";

    public const string TrabajoDeFondoDetenido = "trabajo-de-fondo-detenido";

    public const string MemoriaDeContadoresAlta = "memoria-de-contadores-alta";

    public const string VerificadorDeCredencialesSinClaves = "verificador-de-credenciales-sin-claves";

    public const string RevocacionDeSesionesPendiente = "revocacion-de-sesiones-pendiente";

    public const string AcumuladoDeLimitesNoDisponible = "acumulado-de-limites-no-disponible";

    public const string ConteoDeIngresosNoDisponible = "conteo-de-ingresos-no-disponible";

    public const string MotivoNoCatalogado = "motivo-de-alerta-no-catalogado";

    public const string CorreoDeAlertasNoDisponible = "correo-de-alertas-no-disponible";

    public const string DepuracionDiariaSinExito = "depuracion-diaria-sin-exito";

    public const string BajaDeTitularSinEntregar = "baja-de-titular-sin-entregar";

    public const string TrasladoVinculadoSinAprobar = "traslado-vinculado-sin-aprobar";

    public const string LecturaDeAlertasDelInventarioNoDisponible = "lectura-de-alertas-del-inventario-no-disponible";

    public const string AlcanceDeDatosSinActor = "alcance-de-datos-sin-actor";

    public static readonly IReadOnlyList<string> Todos =
    [
        AccesosDenegados,
        AuditoriaNoDisponible,
        IngresosFallidos,
        CredencialDeServicioAnteriorEnUso,
        DependenciaNoNecesariaFallida,
        TrabajoDeFondoDetenido,
        MemoriaDeContadoresAlta,
        VerificadorDeCredencialesSinClaves,
        RevocacionDeSesionesPendiente,
        AcumuladoDeLimitesNoDisponible,
        ConteoDeIngresosNoDisponible,
        MotivoNoCatalogado,
        CorreoDeAlertasNoDisponible,
        DepuracionDiariaSinExito,
        BajaDeTitularSinEntregar,
        TrasladoVinculadoSinAprobar,
        LecturaDeAlertasDelInventarioNoDisponible,
        AlcanceDeDatosSinActor,
    ];
}
