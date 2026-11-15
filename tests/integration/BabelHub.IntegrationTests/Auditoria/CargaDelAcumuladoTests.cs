using System.Diagnostics;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Domain.Auditoria;
using BabelHub.Ecommerce.Infrastructure.Auditoria;

namespace BabelHub.IntegrationTests.Auditoria;

/// <summary>
/// La carga del acumulado de límites (T30; AC-AUD-25; AUD-04, AUD-41): un origen que rota miles de huellas en una sola ventana de 5 minutos, con solicitudes simultáneas, no satura la base: la ventana nunca pasa de 1000 filas, lo que
/// no cabe se agrupa en la fila «otros» sin perder ningún rechazo, y el conjunto termina en un tiempo acotado. La variante con k6 contra los servicios compuestos queda para cuando exista un servicio compuesto (ver
/// <c>progress/hallazgos.md</c>): hoy ninguno de los dos <c>Program.cs</c> compone los límites ni la auditoría.
/// </summary>
public sealed class CargaDelAcumuladoTests(PostgresDeAuditoriaFixture bd) : IClassFixture<PostgresDeAuditoriaFixture>
{
    private const int Origenes = 3000;

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public async Task UnOrigenQueRotaTresMilHuellasEnUnaVentana_NoSaturaLaBase_YElAcumuladorAgrupaEnOtros()
    {
        var acumulador = new PostgresAcumuladorDeLimites(bd.Acceso, new LimitadorDeEscrituras(concurrenciaMaxima: 32));
        var momento = DateTimeOffset.UtcNow.AddYears(5);
        var ventana = new DateTimeOffset(momento.Year, momento.Month, momento.Day, momento.Hour, momento.Minute - (momento.Minute % 5), 0, TimeSpan.Zero);
        var resultados = new ResultadoDeRegistroDeRechazo[Origenes];

        var reloj = Stopwatch.StartNew();
        await Parallel.ForEachAsync(
            Enumerable.Range(0, Origenes),
            new ParallelOptions { MaxDegreeOfParallelism = 16 },
            async (i, ct) => resultados[i] = await acumulador.Registrar("rota" + i.ToString("D5"), "registros-por-origen", ventana.AddSeconds(i % 240), "198.51.100.77", ct));
        reloj.Stop();

        var filas = await bd.Consultar<long>($"SELECT count(*) FROM auditoria.acumulado_de_limites WHERE ventana_inicio = '{ventana:O}'");
        var total = await bd.Consultar<long>($"SELECT sum(rechazos) FROM auditoria.acumulado_de_limites WHERE ventana_inicio = '{ventana:O}'");
        var otros = await bd.Consultar<int>($"SELECT rechazos FROM auditoria.acumulado_de_limites WHERE ventana_inicio = '{ventana:O}' AND huella = 'otros'");

        Assert.True(filas <= AcumuladoDeLimite.TopeDeFilas, $"La ventana tiene {filas} filas.");
        Assert.Equal(Origenes, total);
        Assert.True(otros >= Origenes - (AcumuladoDeLimite.TopeDeFilas - 1), $"«otros» agrupa {otros}.");
        Assert.Equal(otros, resultados.Count(r => r == ResultadoDeRegistroDeRechazo.AgrupadoEnOtros));
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(120), $"Tardó {reloj.Elapsed}.");
        Assert.Null(await bd.Consultar<string>($"SELECT direccion_completa FROM auditoria.acumulado_de_limites WHERE ventana_inicio = '{ventana:O}' AND huella = 'otros'"));
    }
}
