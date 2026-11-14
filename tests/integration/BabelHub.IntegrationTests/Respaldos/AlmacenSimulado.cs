using System.Globalization;
using System.Text.Json;
using BabelHub.IntegrationTests.Almacen;

namespace BabelHub.IntegrationTests.Respaldos;

internal sealed record ObjetoSimulado(string Contenedor, string Clave, DateTime Creado);

/// <summary>
/// El almacén de objetos simulado para la prueba de retención (PLA-18): recibe los respaldos tal como los sube <c>respaldar.sh</c> y el
/// archivado de cambios, y aplica el ciclo de vida <b>publicado</b> en <c>infra/almacen/ciclo-de-vida.json</c> (retira un objeto cuando
/// tiene tantos días como dice su contenedor). Nada de las reglas está escrito aquí: salen de la definición.
/// </summary>
internal sealed class AlmacenSimulado
{
    private readonly Dictionary<string, int> _dias;

    public AlmacenSimulado(string? carpeta = null)
    {
        using var documento = JsonDocument.Parse(File.ReadAllText(Path.Combine(carpeta ?? PoliticasDelAlmacen.Carpeta(), "ciclo-de-vida.json")));
        _dias = [];
        foreach (var contenedor in documento.RootElement.GetProperty("contenedores").EnumerateObject())
        {
            foreach (var regla in contenedor.Value.EnumerateArray().Where(r => r.GetProperty("action").GetString() == "DELETE" && r.GetProperty("isEnabled").GetBoolean()))
            {
                Assert.Equal("DAYS", regla.GetProperty("timeUnit").GetString());
                _dias[contenedor.Name] = regla.GetProperty("timeAmount").GetInt32();
            }
        }
    }

    public List<ObjetoSimulado> Objetos { get; } = [];

    public IEnumerable<ObjetoSimulado> De(string contenedor) => Objetos.Where(o => o.Contenedor == contenedor);

    /// <summary>Las clases de un día UTC: igual que <c>clases_del_dia</c> de <c>comun.sh</c> (domingo; el día siguiente es el 1).</summary>
    public static (bool Semanal, bool Mensual) Clases(DateTime dia) => (dia.DayOfWeek == DayOfWeek.Sunday, dia.AddDays(1).Day == 1);

    /// <summary>El respaldo programado: dos objetos (clúster y configuración) en el diario, y los mismos en semanales y mensuales según el día.</summary>
    public void Respaldar(DateTime instante)
    {
        var dia = instante.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var hora = instante.ToString("HHmmss", CultureInfo.InvariantCulture);
        var (semanal, mensual) = Clases(instante);
        foreach (var nombre in new[] { $"{dia}/{hora}-{Guid.NewGuid():N}.tar.age", $"{dia}/{hora}-config-{Guid.NewGuid():N}.tar.age" })
        {
            Objetos.Add(new ObjetoSimulado("respaldos-diarios", nombre, instante));
            if (semanal)
            {
                Objetos.Add(new ObjetoSimulado("respaldos-semanales", nombre, instante));
            }

            if (mensual)
            {
                Objetos.Add(new ObjetoSimulado("respaldos-mensuales", nombre, instante));
            }
        }
    }

    /// <summary>Un segmento de WAL por hora entre dos instantes (el archivado real es cada pocos minutos; la hora basta para la continuidad).</summary>
    public void ArchivarCambios(DateTime desde, DateTime hasta)
    {
        for (var hora = desde; hora < hasta; hora = hora.AddHours(1))
        {
            Objetos.Add(new ObjetoSimulado("respaldos-wal", $"{hora:yyyyMMddHH}-{Guid.NewGuid():N}.age", hora));
        }
    }

    /// <summary>Corre el ciclo de vida: retira lo que ya tiene tantos días como dice su contenedor.</summary>
    public void EjecutarCicloDeVida(DateTime ahora) =>
        Objetos.RemoveAll(o => _dias.TryGetValue(o.Contenedor, out var dias) && (ahora - o.Creado) >= TimeSpan.FromDays(dias));
}
