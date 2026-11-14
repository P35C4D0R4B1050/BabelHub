namespace BabelHub.Inventario.Application.Auditoria;

/// <summary>
/// Puerto que <b>Auditoría define y Reservas implementa</b> (solo en el Inventario, D07): las bajas de titular recibidas del E-commerce, con su solo
/// identificador opaco, para llenar <c>auditoria.por_anonimizar</c> y anonimizar lo que se registró de ese titular (AUD-19, AUD-32). Reservas es dueño
/// de <c>bajas_recibidas</c>: Auditoría no lee su esquema.
/// </summary>
public interface IBajasRecibidas
{
    /// <summary>Hasta <paramref name="maximo"/> identificadores de bajas aún no entregadas a Auditoría, para consumirlos por lotes. Tras restaurar un respaldo se vuelve a poblar desde aquí.</summary>
    Task<IReadOnlyList<Guid>> Pendientes(int maximo, CancellationToken cancelacion = default);

    /// <summary>Marca como entregadas a Auditoría las bajas ya copiadas a <c>por_anonimizar</c> (arranca su retención de 30 días).</summary>
    Task MarcarEntregadas(IReadOnlyCollection<Guid> identificadores, CancellationToken cancelacion = default);
}
