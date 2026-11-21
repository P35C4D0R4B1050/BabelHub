using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Domain.Notificaciones;

namespace BabelHub.Ecommerce.UnitTests.Notificaciones;

/// <summary>
/// Ata lo que pide cada emisor de avisos al <b>validador real del catálogo</b> (NOT-02, NOT-07; AC-NOT-01): una solicitud que el Notificador real rechazaría (plantilla desconocida, campo fuera del catálogo, valor con otra forma, evento o destinatario mal formado) no se notaría
/// con un Notificador de pruebas que todo lo acepta, así que cada emisor comprueba sus solicitudes aquí.
/// </summary>
internal static class AvisosContraElCatalogo
{
    public static void Comprobar(IEnumerable<SolicitudDeAviso> solicitudes)
    {
        ArgumentNullException.ThrowIfNull(solicitudes);
        foreach (var solicitud in solicitudes)
        {
            var plantilla = CatalogoDePlantillas.Obtener(solicitud.Plantilla);
            Assert.True(plantilla is not null, $"La plantilla «{solicitud.Plantilla}» no está en el catálogo.");
            var validacion = plantilla.Validar(solicitud.Campos);
            Assert.True(validacion.EsValido, $"{solicitud.Plantilla}: el campo «{validacion.Campo}» no cumple el catálogo ({validacion.Motivo}).");
            var destinatario = solicitud.Destinatario == DestinatarioDelAviso.Cliente ? TipoDeDestinatario.Cliente : TipoDeDestinatario.Sucursal;
            Assert.True(plantilla.Destinatario == destinatario, $"{solicitud.Plantilla}: el catálogo la dirige a {plantilla.Destinatario}, no a {destinatario}.");
            Assert.True(PlantillaDeAviso.EventoValido(solicitud.EventoId), $"{solicitud.Plantilla}: el evento «{solicitud.EventoId}» no tiene el formato del catálogo.");
            Assert.True(PlantillaDeAviso.ReferenciaValida(solicitud.DestinatarioRef), $"{solicitud.Plantilla}: la referencia del destinatario no es válida.");
        }
    }
}
