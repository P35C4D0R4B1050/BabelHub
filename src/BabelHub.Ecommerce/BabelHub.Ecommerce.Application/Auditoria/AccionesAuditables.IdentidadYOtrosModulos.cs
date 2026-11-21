namespace BabelHub.Ecommerce.Application.Auditoria;

public static partial class AccionesAuditables
{
    // ---------- Identidad (AUD-16, AUD-22): todas van al almacén aislado de Identidad (D17) ----------

    public static readonly AccionAuditable UsuarioDadoDeAlta = new(nameof(UsuarioDadoDeAlta), aislada: true);

    public static readonly AccionAuditable UsuarioDesactivado = new(nameof(UsuarioDesactivado), aislada: true);

    public static readonly AccionAuditable UsuarioReactivado = new(nameof(UsuarioReactivado), aislada: true);

    /// <summary>Un usuario corrigió su nombre (IDE-90, AUD-45): auditoría de Identidad sin ningún dato personal en el detalle.</summary>
    public static readonly AccionAuditable UsuarioActualizado = new(nameof(UsuarioActualizado), aislada: true);

    /// <summary>Un cambio de rol: un único registro con las marcas de auditoría y de seguridad (AUD-20).</summary>
    public static readonly AccionAuditable CambioDeRol = new(nameof(CambioDeRol), aislada: true);

    /// <summary>La administración restableció la contraseña de un usuario (nunca la temporal en el detalle, AC-AUD-05).</summary>
    public static readonly AccionAuditable ContrasenaRestablecidaPorAdministracion = new(nameof(ContrasenaRestablecidaPorAdministracion), aislada: true);

    public static readonly AccionAuditable SegundoFactorRestablecidoPorAdministracion = new(nameof(SegundoFactorRestablecidoPorAdministracion), aislada: true);

    public static readonly AccionAuditable CuentaCancelada = new(nameof(CuentaCancelada), aislada: true);

    /// <summary>Una cuenta sin verificar se reemplazó por otra (IDE-84).</summary>
    public static readonly AccionAuditable CuentaReemplazada = new(nameof(CuentaReemplazada), aislada: true);

    /// <summary>Se descartó una cancelación diferida a los 30 días (IDE-81, actor «sistema»).</summary>
    public static readonly AccionAuditable CancelacionDiferidaDescartada = new(nameof(CancelacionDiferidaDescartada), aislada: true);

    /// <summary>La administración atendió una solicitud sobre datos personales.</summary>
    public static readonly AccionAuditable SolicitudDeDatosAtendida = new(nameof(SolicitudDeDatosAtendida), aislada: true);

    /// <summary>La administración consultó una solicitud sobre datos personales (AUD-21).</summary>
    public static readonly AccionAuditable SolicitudDeDatosConsultada = new(nameof(SolicitudDeDatosConsultada), aislada: true);

    /// <summary>Se rotó la clave de firma de las credenciales (solo las versiones, nunca la clave).</summary>
    public static readonly AccionAuditable ClaveDeFirmaRotada = new(nameof(ClaveDeFirmaRotada), aislada: true);

    /// <summary>Se declaró comprometida una clave de firma: también es un evento de seguridad (AUD-33).</summary>
    public static readonly AccionAuditable ClaveDeFirmaDeclaradaComprometida = new(nameof(ClaveDeFirmaDeclaradaComprometida), aislada: true);

    // ---------- Biblioteca digital (AUD-35, AUD-21) ----------

    public static readonly AccionAuditable DescargasRestituidas = new(nameof(DescargasRestituidas));

    /// <summary>El trabajo diario quitó del registro de descargas de dos años la referencia al acceso y al usuario (solo la cantidad, BIB-26).</summary>
    public static readonly AccionAuditable DescargasDeBibliotecaDepuradas = new(nameof(DescargasDeBibliotecaDepuradas));

    /// <summary>El trabajo diario eliminó las solicitudes de ayuda de dos años (solo la cantidad, BIB-29).</summary>
    public static readonly AccionAuditable AyudasDeBibliotecaDepuradas = new(nameof(AyudasDeBibliotecaDepuradas));

    public static readonly AccionAuditable AccesoDigitalRevocado = new(nameof(AccesoDigitalRevocado));

    /// <summary>La administración resolvió el reporte de un archivo defectuoso.</summary>
    public static readonly AccionAuditable ReporteDeArchivoResuelto = new(nameof(ReporteDeArchivoResuelto));

    /// <summary>La administración consultó la biblioteca de un cliente (AUD-21).</summary>
    public static readonly AccionAuditable BibliotecaDeClienteConsultada = new(nameof(BibliotecaDeClienteConsultada));

    // ---------- Libro de Reclamaciones (AUD-35, AUD-21) ----------

    public static readonly AccionAuditable HojaDeReclamacionRespondida = new(nameof(HojaDeReclamacionRespondida));

    public static readonly AccionAuditable HojaDeReclamacionDescartada = new(nameof(HojaDeReclamacionDescartada));

    public static readonly AccionAuditable HojaDeReclamacionAnonimizada = new(nameof(HojaDeReclamacionAnonimizada));

    /// <summary>Una operación vinculó una o más hojas de reclamación a la cuenta de un cliente (AUD-46, DD-26): objeto personal, detalle solo con el disparador y el conteo, sin el correo ni el número de ninguna hoja.</summary>
    public static readonly AccionAuditable HojaDeReclamacionVinculada = new(nameof(HojaDeReclamacionVinculada));

    /// <summary>La administración consultó una hoja de reclamación (AUD-21).</summary>
    public static readonly AccionAuditable HojaDeReclamacionConsultada = new(nameof(HojaDeReclamacionConsultada));

    // ---------- Notificaciones (AUD-22) ----------

    /// <summary>La administración desactivó los avisos de un número (se identifica por su huella, nunca por el número).</summary>
    public static readonly AccionAuditable AvisosDeNumeroDesactivados = new(nameof(AvisosDeNumeroDesactivados));
}
