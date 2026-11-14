using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Auditoria;
using AlertaAgrupada = BabelHub.Inventario.Domain.Auditoria.AlertaDeSeguridad;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>Unidad de trabajo que ejecuta el trabajo tal cual y puede fallar a pedido (una base de datos que no responde).</summary>
internal sealed class UnidadDeTrabajoDeAuditoria : IUnidadDeTrabajo
{
    public int Ejecuciones { get; private set; }

    public Exception? Falla { get; set; }

    public async Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default)
    {
        Ejecuciones++;
        return Falla is not null ? throw Falla : await trabajo(cancelacion);
    }

    public Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default) => bloque(cancelacion);
}

/// <summary>Almacén de alertas en memoria con la semántica del puerto (la última por servicio y motivo, el orden de la lista y la lectura desde un instante).</summary>
internal sealed class AlmacenDeAlertasEnMemoria : IAlmacenDeAlertas
{
    public List<AlertaAgrupada> Alertas { get; } = [];

    public Exception? Falla { get; set; }

    public int Guardados { get; private set; }

    public AlertaAgrupada? Buscar(string motivo, ServicioDeOrigen servicio = ServicioDeOrigen.Ecommerce) =>
        Alertas.Where(a => a.Motivo == motivo && a.Servicio == servicio).OrderByDescending(a => a.PrimeraOcurrencia).FirstOrDefault();

    public Task<AlertaAgrupada?> UltimaDelMotivoParaActualizar(ServicioDeOrigen servicio, string motivo, CancellationToken cancelacion = default) =>
        Falla is not null ? Task.FromException<AlertaAgrupada?>(Falla) : Task.FromResult(Buscar(motivo, servicio));

    public Task<AlertaAgrupada?> Obtener(string id, CancellationToken cancelacion = default) =>
        Falla is not null ? Task.FromException<AlertaAgrupada?>(Falla) : Task.FromResult(Alertas.FirstOrDefault(a => a.Id == id));

    public Task Guardar(AlertaAgrupada alerta, CancellationToken cancelacion = default)
    {
        if (Falla is not null)
        {
            return Task.FromException(Falla);
        }

        Guardados++;
        var indice = Alertas.FindIndex(a => a.Id == alerta.Id);
        if (indice >= 0)
        {
            Alertas[indice] = alerta;
        }
        else
        {
            Alertas.Add(alerta);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AlertaAgrupada>> ListarDeSeguridad(PosicionDeAlerta? despuesDe, int maximo, CancellationToken cancelacion = default)
    {
        IEnumerable<AlertaAgrupada> consulta = Alertas.Where(a => a.Clase == ClaseDeAlerta.Seguridad)
            .OrderByDescending(a => a.UltimaOcurrencia).ThenBy(a => a.Id, StringComparer.Ordinal);
        if (despuesDe is not null)
        {
            consulta = consulta.Where(a => a.UltimaOcurrencia < despuesDe.UltimaOcurrencia
                || (a.UltimaOcurrencia == despuesDe.UltimaOcurrencia && string.CompareOrdinal(a.Id, despuesDe.Id) > 0));
        }

        return Task.FromResult<IReadOnlyList<AlertaAgrupada>>([.. consulta.Take(maximo)]);
    }

    public Task<IReadOnlyList<AlertaAgrupada>> Desde(DateTimeOffset desde, int maximo, CancellationToken cancelacion = default) =>
        Falla is not null
            ? Task.FromException<IReadOnlyList<AlertaAgrupada>>(Falla)
            : Task.FromResult<IReadOnlyList<AlertaAgrupada>>([.. Alertas.Where(a => a.UltimaOcurrencia > desde).OrderBy(a => a.UltimaOcurrencia).Take(maximo)]);
}

/// <summary>Acumulador de límites en memoria: una ventana por inicio, con el dominio real (tope de filas, «otros» y cierre).</summary>
internal sealed class AcumuladorDeLimitesEnMemoria : IAcumuladorDeLimites
{
    public Dictionary<DateTimeOffset, AcumuladoDeLimite> Ventanas { get; } = [];

    public Exception? Falla { get; set; }

    public Task<ResultadoDeRegistroDeRechazo> Registrar(string huella, string limite, DateTimeOffset instante, string? direccionCompleta, CancellationToken cancelacion = default)
    {
        if (Falla is not null)
        {
            return Task.FromException<ResultadoDeRegistroDeRechazo>(Falla);
        }

        var ventana = VentanaDeLimite.Que(instante);
        if (!Ventanas.TryGetValue(ventana.Inicio, out var acumulado))
        {
            Ventanas[ventana.Inicio] = acumulado = new AcumuladoDeLimite(ventana);
        }

        return Task.FromResult(acumulado.Registrar(huella, limite, instante, direccionCompleta));
    }
}

internal sealed class ConteoLocalFalso : IConteoLocalDeEventos
{
    public int Accesos { get; set; }

    public int RechazosDeIngresos { get; set; }

    public List<(string Accion, DateTimeOffset Desde, DateTimeOffset Hasta)> Consultas { get; } = [];

    public Task<int> ContarRegistros(AccionAuditable accion, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion = default)
    {
        Consultas.Add((accion.Identificador, desde, hasta));
        return Task.FromResult(Accesos);
    }

    public Task<int> ContarRechazosDeLimite(string? limite, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion = default)
    {
        Consultas.Add(("limite:" + limite, desde, hasta));
        return Task.FromResult(RechazosDeIngresos);
    }
}
