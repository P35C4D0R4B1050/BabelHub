using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.UnitTests.Plataforma;

namespace BabelHub.Inventario.UnitTests.Auditoria;

internal sealed class ClavesDeCursorFalsas : IClavesDeCursor
{
    private static readonly ClaveDeCursor Actual = new("v2", new ValorSecreto("clave-de-prueba-" + Guid.NewGuid().ToString("N")));
    private static readonly ClaveDeCursor Anterior = new("v1", new ValorSecreto("clave-anterior-" + Guid.NewGuid().ToString("N")));

    public ClaveDeCursor Vigente => Actual;

    public ClaveDeCursor? Buscar(string version) => version == Actual.Version ? Actual : version == Anterior.Version ? Anterior : null;

    /// <summary>Un cursor firmado con la clave anterior (todavía válida durante la rotación).</summary>
    public static IClavesDeCursor SoloLaAnterior => new SoloAnterior();

    private sealed class SoloAnterior : IClavesDeCursor
    {
        public ClaveDeCursor Vigente => Anterior;

        public ClaveDeCursor? Buscar(string version) => version == Anterior.Version ? Anterior : null;
    }
}

/// <summary>
/// Una fuente paginada en memoria con la semántica de la real: orden fijo, cursor de instantánea (la secuencia máxima al abrir) y de posición (el último
/// entregado), de modo que un registro posterior al abrir no aparece ni desplaza a los demás (AUD-30). Cuenta las llamadas y puede fallar.
/// </summary>
internal sealed class FuentePaginadaFalsa
{
    private readonly List<(long Secuencia, RegistroAuditado Registro)> _filas = [];
    private long _secuencia;

    public int Llamadas { get; private set; }

    public Exception? Falla { get; set; }

    public List<ConsultaDeAuditoria> Consultas { get; } = [];

    public int TamanoDePagina { get; set; } = PaginaDeRegistros.TamanoDePagina;

    public void Agregar(RegistroAuditado registro) => _filas.Add((++_secuencia, registro));

    public void Agregar(IEnumerable<RegistroAuditado> registros)
    {
        foreach (var registro in registros)
        {
            Agregar(registro);
        }
    }

    public Task<PaginaDeRegistros> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default)
    {
        Llamadas++;
        Consultas.Add(consulta);
        if (Falla is not null)
        {
            return Task.FromException<PaginaDeRegistros>(Falla);
        }

        var instantanea = _secuencia;
        string? despues = null;
        if (consulta.Cursor is not null)
        {
            var partes = consulta.Cursor.Split('|');
            instantanea = long.Parse(partes[0], System.Globalization.CultureInfo.InvariantCulture);
            despues = partes.Length > 1 && partes[1].Length > 0 ? partes[1] : null;
        }

        var ordenadas = _filas.Where(f => f.Secuencia <= instantanea).Select(f => f.Registro).OrderBy(r => r, OrdenDeLaLista.Instancia).ToList();
        var inicio = despues is null ? 0 : ordenadas.FindIndex(r => r.Id.ToString("D") == despues) + 1;
        var pagina = ordenadas.Skip(inicio).Take(TamanoDePagina + 1).ToList();
        var hayMas = pagina.Count > TamanoDePagina;
        pagina = [.. pagina.Take(TamanoDePagina)];
        return Task.FromResult(new PaginaDeRegistros(pagina, hayMas ? $"{instantanea}|{pagina[^1].Id:D}" : null)
        {
            CursorDeLaPagina = $"{instantanea}|{(despues ?? string.Empty)}",
        });
    }
}

/// <summary>Un <see cref="IAuditoria"/> que guarda lo registrado y puede fallar como una auditoría caída (AUD-04).</summary>
internal sealed class AuditoriaEspia : IAuditoria
{
    public List<SolicitudDeRegistro> Solicitudes { get; } = [];

    public bool Caida { get; set; }

    public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
    {
        if (Caida)
        {
            return Task.FromException(new AuditoriaNoDisponibleException("sin auditoría"));
        }

        Solicitudes.Add(solicitud);
        return Task.CompletedTask;
    }
}

internal static class RegistrosDePrueba
{
    public static RegistroAuditado De(
        ServicioAuditado servicio,
        DateTimeOffset instante,
        string? sucursal = null,
        Guid? id = null,
        string accion = "AccesoDenegado") =>
        new(id ?? Guid.NewGuid(), servicio, TipoDeRegistroAuditado.Seguridad, TipoDeActor.Sistema, null, null, instante.ToUniversalTime(), accion, ResultadoAuditado.Rechazado)
        {
            SucursalOrigen = sucursal,
        };
}
