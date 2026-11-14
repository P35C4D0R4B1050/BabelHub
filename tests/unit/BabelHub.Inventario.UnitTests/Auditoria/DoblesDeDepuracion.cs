using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;

namespace BabelHub.Inventario.UnitTests.Auditoria;

internal sealed class FuenteDeIdentificadoresFalsa(TipoDePorAnonimizar tipo) : IFuenteDeIdentificadoresPorAnonimizar
{
    public List<Guid> Disponibles { get; } = [];

    public List<Guid> Entregados { get; } = [];

    public Exception? Falla { get; set; }

    public TipoDePorAnonimizar Tipo => tipo;

    public Task<IReadOnlyList<Guid>> Pendientes(int maximo, CancellationToken cancelacion = default) =>
        Falla is not null
            ? Task.FromException<IReadOnlyList<Guid>>(Falla)
            : Task.FromResult<IReadOnlyList<Guid>>([.. Disponibles.Except(Entregados).Take(maximo)]);

    public Task MarcarEntregados(IReadOnlyCollection<Guid> identificadores, CancellationToken cancelacion = default)
    {
        Entregados.AddRange(identificadores);
        return Task.CompletedTask;
    }
}

internal sealed class PorAnonimizarEnMemoria : IAlmacenDePorAnonimizar
{
    public HashSet<(TipoDePorAnonimizar Tipo, Guid Id)> Filas { get; } = [];

    public List<IReadOnlyCollection<Guid>> Lotes { get; } = [];

    public Task<int> Agregar(TipoDePorAnonimizar tipo, IReadOnlyCollection<Guid> identificadores, DateTimeOffset registradoEn, CancellationToken cancelacion = default)
    {
        Lotes.Add(identificadores);
        return Task.FromResult(identificadores.Count(id => Filas.Add((tipo, id))));
    }
}

internal sealed class AnonimizadorFalso : IAnonimizadorDeRegistros
{
    public Queue<int> Respuestas { get; } = new();

    public Exception? Falla { get; set; }

    public int Llamadas { get; private set; }

    public Task<int> Anonimizar(CancellationToken cancelacion = default)
    {
        Llamadas++;
        return Falla is not null ? Task.FromException<int>(Falla) : Task.FromResult(Respuestas.Count > 0 ? Respuestas.Dequeue() : 0);
    }
}

internal sealed class DepuradorAdicionalFalso(int cantidad) : IDepuradorAdicional
{
    public Exception? Falla { get; set; }

    public int Llamadas { get; private set; }

    public Task<int> Depurar(CancellationToken cancelacion = default)
    {
        Llamadas++;
        return Falla is not null ? Task.FromException<int>(Falla) : Task.FromResult(cantidad);
    }
}

internal sealed class EstadoDeDepuracionEnMemoria : IEstadoDeLaDepuracion
{
    public EstadoDeLaDepuracion Estado { get; set; } = EstadoDeLaDepuracion.Inicial;

    public Task<EstadoDeLaDepuracion> Obtener(CancellationToken cancelacion = default) => Task.FromResult(Estado);

    public Task Guardar(EstadoDeLaDepuracion estado, CancellationToken cancelacion = default)
    {
        Estado = estado;
        return Task.CompletedTask;
    }
}

internal sealed class BloqueoDeDepuracionFalso : IBloqueoDeLaDepuracion
{
    public bool OtroLoTiene { get; set; }

    public int Tomados { get; private set; }

    public int Liberados { get; private set; }

    public Task<IAsyncDisposable?> Intentar(CancellationToken cancelacion = default)
    {
        if (OtroLoTiene)
        {
            return Task.FromResult<IAsyncDisposable?>(null);
        }

        Tomados++;
        return Task.FromResult<IAsyncDisposable?>(new Liberador(this));
    }

    private sealed class Liberador(BloqueoDeDepuracionFalso dueno) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            dueno.Liberados++;
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class AlertasEspia : IAlertas
{
    public List<string> Tecnicas { get; } = [];

    public Task PublicarTecnica(CodigoDeAlerta motivo, CancellationToken cancelacion = default)
    {
        Tecnicas.Add(motivo.Valor);
        return Task.CompletedTask;
    }

    public Task PublicarDeSeguridad(CodigoDeAlerta motivo, CancellationToken cancelacion = default) => Task.CompletedTask;
}
