namespace BabelHub.Ecommerce.Application.Auditoria.Interno;

/// <summary>Las cuentas canceladas que Identidad aún no anonimizó, como fuente de <c>auditoria.por_anonimizar</c> (AUD-19, AUD-31). No hay nada que marcar: Identidad deja de listarlas cuando las anonimiza.</summary>
public sealed class CuentasCanceladasComoFuente(ICuentasCanceladasPendientes cuentas) : IFuenteDeIdentificadoresPorAnonimizar
{
    public TipoDePorAnonimizar Tipo => TipoDePorAnonimizar.CuentaCancelada;

    public Task<IReadOnlyList<Guid>> Pendientes(int maximo, CancellationToken cancelacion = default) => cuentas.Listar(maximo, cancelacion);

    public Task MarcarEntregados(IReadOnlyCollection<Guid> identificadores, CancellationToken cancelacion = default) => Task.CompletedTask;
}

/// <summary>La depuración de <c>identidad.registros</c> con su propia función y su propia <c>por_anonimizar</c>, en la misma corrida (D17, AUD-17).</summary>
public sealed class DepuradorDeRegistrosDeIdentidad(IDepuradorDeRegistrosAislados identidad) : IDepuradorAdicional
{
    public Task<int> Depurar(CancellationToken cancelacion = default) => identidad.Depurar(cancelacion);
}
