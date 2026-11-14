namespace BabelHub.Inventario.Application.Auditoria.Interno;

/// <summary>
/// Las bajas de titular recibidas del E-commerce, como fuente de <c>auditoria.por_anonimizar</c> (AUD-19, AUD-32): Reservas es dueño de <c>bajas_recibidas</c> y las
/// entrega por lotes; al copiarlas se marcan entregadas (arranca su retención de 30 días) en la misma unidad de trabajo de la depuración. Tras restaurar un respaldo se vuelve a poblar desde aquí.
/// </summary>
public sealed class BajasRecibidasComoFuente(IBajasRecibidas bajas) : IFuenteDeIdentificadoresPorAnonimizar
{
    public TipoDePorAnonimizar Tipo => TipoDePorAnonimizar.BajaDeTitular;

    public Task<IReadOnlyList<Guid>> Pendientes(int maximo, CancellationToken cancelacion = default) => bajas.Pendientes(maximo, cancelacion);

    public Task MarcarEntregados(IReadOnlyCollection<Guid> identificadores, CancellationToken cancelacion = default) => bajas.MarcarEntregadas(identificadores, cancelacion);
}
