using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Devoluciones.Interno;

/// <summary>
/// La implementación pública de <see cref="IDevolucionesDelCliente"/> (T22): solo reparte cada operación a su caso de uso (cada uno comprueba el rol, el alcance, los textos legales y el límite por su cuenta, en el orden fijo del plan §4) y no añade reglas.
/// </summary>
internal sealed class DevolucionesDelClienteDeLaAplicacion(
    SolicitarDevolucionDeImpreso solicitarImpreso,
    SolicitarReembolsoDeDigital pedirReembolsoDeDigital,
    ReportarArchivoDefectuoso reportar,
    ConsultasDeDevoluciones consultas) : IDevolucionesDelCliente
{
    public Task<Resultado<DevolucionVista>> SolicitarImpreso(SolicitarDevolucionDeImpresoEntrada entrada, CancellationToken cancelacion = default) => solicitarImpreso.Ejecutar(entrada, cancelacion);

    public Task<Resultado<DevolucionVista>> PedirReembolsoDeDigital(PedirReembolsoDeDigitalEntrada entrada, CancellationToken cancelacion = default) => pedirReembolsoDeDigital.Ejecutar(entrada, cancelacion);

    public Task<Resultado<DevolucionVista>> ReportarArchivoDefectuoso(ReportarArchivoDefectuosoEntrada entrada, CancellationToken cancelacion = default) => reportar.Ejecutar(entrada, cancelacion);

    public Task<Resultado<PaginaDeDevoluciones>> ListarMias(ListarDevolucionesEntrada entrada, CancellationToken cancelacion = default) => consultas.ListarMias(entrada, cancelacion);

    public Task<Resultado<DevolucionVista>> ObtenerMia(Guid devolucionId, CancellationToken cancelacion = default) => consultas.ObtenerMia(devolucionId, cancelacion);
}

/// <summary>La implementación pública de <see cref="IGestionDeDevoluciones"/> (T22): reparte cada operación a su caso de uso, que comprueba el rol y el alcance por sucursal receptora en la Aplicación.</summary>
internal sealed class GestionDeDevolucionesDeLaAplicacion(
    ConsultasDeDevoluciones consultas,
    AprobarYRechazarDevolucion gestion,
    ConfirmarRecepcion recepcion,
    DecidirReporte reportes) : IGestionDeDevoluciones
{
    public Task<Resultado<PaginaDeDevoluciones>> ListarDeLaSucursal(string codigoDeSucursal, ListarDevolucionesEntrada entrada, CancellationToken cancelacion = default) =>
        consultas.ListarDeLaSucursal(codigoDeSucursal, entrada, cancelacion);

    public Task<Resultado<PaginaDeDevoluciones>> ListarDeLaAdministracion(ListarDevolucionesDeLaAdministracionEntrada entrada, CancellationToken cancelacion = default) =>
        consultas.ListarDeLaAdministracion(entrada, cancelacion);

    public Task<Resultado<DevolucionVista>> Obtener(Guid devolucionId, CancellationToken cancelacion = default) => consultas.Obtener(devolucionId, cancelacion);

    public Task<Resultado<DevolucionVista>> Aprobar(Guid devolucionId, CancellationToken cancelacion = default) => gestion.Aprobar(devolucionId, cancelacion);

    public Task<Resultado<DevolucionVista>> Rechazar(Guid devolucionId, CancellationToken cancelacion = default) => gestion.Rechazar(devolucionId, cancelacion);

    public Task<Resultado<DevolucionVista>> ConfirmarRecepcion(Guid devolucionId, CancellationToken cancelacion = default) => recepcion.Ejecutar(devolucionId, cancelacion);

    public Task<Resultado<DevolucionVista>> AprobarReporte(Guid devolucionId, CancellationToken cancelacion = default) => reportes.Aprobar(devolucionId, cancelacion);

    public Task<Resultado<DevolucionVista>> RechazarReporte(Guid devolucionId, CancellationToken cancelacion = default) => reportes.Rechazar(devolucionId, cancelacion);
}
