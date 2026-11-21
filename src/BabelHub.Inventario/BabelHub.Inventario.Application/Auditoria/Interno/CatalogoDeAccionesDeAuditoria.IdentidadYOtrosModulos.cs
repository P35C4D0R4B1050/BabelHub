using BabelHub.Inventario.Domain.Auditoria;

namespace BabelHub.Inventario.Application.Auditoria.Interno;

public static partial class CatalogoDeAccionesDeAuditoria
{
    /// <summary>
    /// Usuarios, roles, cancelaciones, reemplazos, descartes, restituciones, revocaciones, reportes, hojas, consultas administrativas y claves
    /// (T13; AUD-16, AUD-21, AUD-22, AUD-33, AUD-35). Las de Identidad van al almacén aislado. La cuenta o el cliente como objeto es un dato personal
    /// (se anonimiza con la cuenta cancelada, AUD-19); una hoja, una solicitud o una clave no lo son. Ningún detalle lleva secretos, contraseñas,
    /// códigos, claves ni números: la lista blanca solo declara versiones, causas, cantidades e identificadores opacos.
    /// </summary>
    private static IEnumerable<AccionDelCatalogo> AccionesDeIdentidadYOtrosModulos()
    {
        // Identidad.
        yield return AuditadaAislada(nameof(AccionesAuditables.UsuarioDadoDeAlta), true, Clave("rolAsignado"));
        yield return AuditadaAislada(nameof(AccionesAuditables.UsuarioDesactivado), true);
        yield return AuditadaAislada(nameof(AccionesAuditables.UsuarioReactivado), true);
        yield return AuditadaAislada(nameof(AccionesAuditables.UsuarioActualizado), true);
        yield return AuditadaYSeguridadAislada(nameof(AccionesAuditables.CambioDeRol), true, ["asignacion-de-administracion"], Clave("rolAnterior"), Clave("rolNuevo"));
        yield return AuditadaAislada(nameof(AccionesAuditables.ContrasenaRestablecidaPorAdministracion), true);
        yield return AuditadaAislada(nameof(AccionesAuditables.SegundoFactorRestablecidoPorAdministracion), true);
        yield return AuditadaAislada(nameof(AccionesAuditables.CuentaCancelada), true, Clave("causa"));
        yield return AuditadaAislada(nameof(AccionesAuditables.CuentaReemplazada), true);
        yield return AuditadaAislada(nameof(AccionesAuditables.CancelacionDiferidaDescartada), true);
        yield return AuditadaAislada(nameof(AccionesAuditables.SolicitudDeDatosAtendida), false, Clave("tipoDeSolicitud"), Clave("resolucion"));
        yield return AuditadaAislada(nameof(AccionesAuditables.SolicitudDeDatosConsultada), false);
        yield return AuditadaAislada(nameof(AccionesAuditables.ClaveDeFirmaRotada), false, Clave("versionAnterior"), Clave("versionNueva"));
        yield return AuditadaYSeguridadAislada(nameof(AccionesAuditables.ClaveDeFirmaDeclaradaComprometida), false, ["declaracion-de-administracion"], Clave("version"));

        // Biblioteca digital.
        yield return Auditada(nameof(AccionesAuditables.DescargasRestituidas), false, false, Clave("edicionId"), Clave("cantidad"), Clave("motivo"));
        yield return Auditada(nameof(AccionesAuditables.DescargasDeBibliotecaDepuradas), false, false, Clave("cantidad", 12));
        yield return Auditada(nameof(AccionesAuditables.AyudasDeBibliotecaDepuradas), false, false, Clave("cantidad", 12));
        yield return Auditada(nameof(AccionesAuditables.AccesoDigitalRevocado), false, false, Clave("edicionId"), Clave("causa"));
        yield return Auditada(nameof(AccionesAuditables.ReporteDeArchivoResuelto), false, false, Clave("edicionId"), Clave("resolucion"));
        yield return Auditada(nameof(AccionesAuditables.BibliotecaDeClienteConsultada), true, false);

        // Libro de Reclamaciones.
        yield return Auditada(nameof(AccionesAuditables.HojaDeReclamacionRespondida), false, false, Clave("estadoNuevo"));
        yield return Auditada(nameof(AccionesAuditables.HojaDeReclamacionDescartada), false, false, Clave("causa"));
        yield return Auditada(nameof(AccionesAuditables.HojaDeReclamacionAnonimizada), false, false, Clave("causa"), Clave("cantidad", 12));
        yield return Auditada(nameof(AccionesAuditables.HojaDeReclamacionVinculada), true, false, new ClaveDeDetalle("disparador", personal: false) { Valores = ["registro", "consulta", "pendientes", "cambio-de-correo", "vinculacion-inicial"] }, Clave("conteo", 12));
        yield return Auditada(nameof(AccionesAuditables.HojaDeReclamacionConsultada), false, false);

        // Notificaciones: el número se identifica por su huella (objeto personal: una huella estable identifica a quien tiene el número).
        yield return Auditada(nameof(AccionesAuditables.AvisosDeNumeroDesactivados), true, false, Clave("causa"));
    }
}
