using System.Globalization;

namespace BabelHub.IntegrationTests.Respaldos;

/// <summary>
/// PLA-18 y AC-PLA-18: sobre respaldos simulados día tras día, el ciclo de vida publicado deja los 7 diarios más recientes, el último de cada
/// una de las 4 semanas más recientes (la copia dominical), el último de cada uno de los 3 meses más recientes (la copia del último día del
/// mes) y el archivo de cambios necesario para restaurar el más antiguo, y retira lo demás. Cada día se comprueba lo que queda justo después
/// de correr el ciclo de vida, durante más de dos años que incluyen un febrero bisiesto.
/// </summary>
public sealed class RetencionTests
{
    private static readonly DateTime Inicio = new(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Simula <paramref name="dias"/> días: respaldo a las 02:30, archivado de cambios cada hora y ciclo de vida a las 03:00.</summary>
    private static IEnumerable<(DateTime Dia, AlmacenSimulado Almacen)> Simular(int dias)
    {
        var almacen = new AlmacenSimulado();
        for (var d = 0; d < dias; d++)
        {
            var dia = Inicio.AddDays(d);
            almacen.ArchivarCambios(dia.AddDays(-1).AddHours(3), dia.AddHours(3));
            almacen.Respaldar(dia.AddHours(2).AddMinutes(30));
            almacen.EjecutarCicloDeVida(dia.AddHours(3));
            yield return (dia, almacen);
        }
    }

    private static IEnumerable<string> DiasConRespaldo(AlmacenSimulado almacen, string contenedor) =>
        almacen.De(contenedor).Select(o => o.Clave[..10]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    private static string Fecha(DateTime dia) => dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    [Trait("Criterio", "AC-PLA-18")]
    public void ConCienDiasDeRespaldos_QuedanLosSieteDiariosLosCuatroSemanalesYLosTresMensualesMasRecientes()
    {
        var (ultimo, almacen) = Simular(100).Last();

        var diarios = DiasConRespaldo(almacen, "respaldos-diarios").ToList();
        Assert.Equal(Enumerable.Range(0, 7).Select(i => Fecha(ultimo.AddDays(-i))).Order(StringComparer.Ordinal), diarios);

        var domingos = Enumerable.Range(0, 28).Select(i => ultimo.AddDays(-i)).Where(d => d.DayOfWeek == DayOfWeek.Sunday).Select(Fecha).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(4, domingos.Count);
        Assert.Equal(domingos, DiasConRespaldo(almacen, "respaldos-semanales"));

        var finesDeMes = Enumerable.Range(0, 100).Select(i => ultimo.AddDays(-i)).Where(d => d.AddDays(1).Day == 1).Select(Fecha).ToList();
        var mensuales = DiasConRespaldo(almacen, "respaldos-mensuales").ToList();
        Assert.Equal(finesDeMes.Take(3).Order(StringComparer.Ordinal), mensuales.TakeLast(3));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-18")]
    public void CadaDia_QuedanSiempreLosSieteDiariosYLosCuatroSemanalesQueCorresponden()
    {
        foreach (var (dia, almacen) in Simular(800).Skip(35))
        {
            var diarios = DiasConRespaldo(almacen, "respaldos-diarios").ToList();
            Assert.Equal(7, diarios.Count);
            Assert.Equal(Fecha(dia), diarios[^1]);
            Assert.Equal(Fecha(dia.AddDays(-6)), diarios[0]);

            var semanales = DiasConRespaldo(almacen, "respaldos-semanales").ToList();
            Assert.Equal(4, semanales.Count);
            Assert.All(semanales, s => Assert.Equal(DayOfWeek.Sunday, DateTime.Parse(s, CultureInfo.InvariantCulture).DayOfWeek));
            Assert.True((dia - DateTime.Parse(semanales[0], CultureInfo.InvariantCulture)).TotalDays < 28);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-18")]
    public void CadaDia_QuedanComoMinimoLosTresUltimosFinesDeMes_YNuncaMasDeCuatro()
    {
        foreach (var (dia, almacen) in Simular(800).Skip(100))
        {
            var mensuales = DiasConRespaldo(almacen, "respaldos-mensuales").ToList();
            var tresMasRecientes = Enumerable.Range(0, 130).Select(i => dia.AddDays(-i)).Where(d => d.AddDays(1).Day == 1).Take(3).Select(Fecha).Order(StringComparer.Ordinal).ToList();

            Assert.True(mensuales.Count is 3 or 4, $"{Fecha(dia)}: quedan {mensuales.Count} mensuales");
            Assert.Subset(mensuales.ToHashSet(StringComparer.Ordinal), tresMasRecientes.ToHashSet(StringComparer.Ordinal));
            Assert.All(mensuales, m => Assert.Equal(1, DateTime.Parse(m, CultureInfo.InvariantCulture).AddDays(1).Day));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-18")]
    public void CadaDia_ElArchivoDeCambiosCubreDesdeElRespaldoMasAntiguoQueQuedaHastaHoy_YNoGuardaMas()
    {
        foreach (var (dia, almacen) in Simular(500).Skip(120))
        {
            var masAntiguo = new[] { "respaldos-diarios", "respaldos-semanales", "respaldos-mensuales" }.SelectMany(almacen.De).Min(o => o.Creado);
            var cambios = almacen.De("respaldos-wal").Select(o => o.Creado).Order().ToList();

            // Continuidad: ni un solo hueco de una hora entre el respaldo más antiguo y el último cambio archivado.
            var necesarios = cambios.Where(c => c >= masAntiguo.AddHours(-1)).ToList();
            Assert.True(necesarios.Count > 0, "no hay archivo de cambios desde el respaldo más antiguo");
            Assert.True(cambios[0] <= masAntiguo, $"{Fecha(dia)}: el archivo de cambios empieza el {cambios[0]:u} y el respaldo más antiguo es del {masAntiguo:u}");
            for (var i = 1; i < cambios.Count; i++)
            {
                Assert.Equal(TimeSpan.FromHours(1), cambios[i] - cambios[i - 1]);
            }

            // Y no se acumula más de lo que dura la retención del archivo de cambios.
            Assert.True((dia.AddHours(3) - cambios[0]).TotalDays < 100);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-18")]
    public void UnRespaldoBajoDemanda_NoAlteraLoQueQuedaDeLosSemanalesNiDeLosMensuales()
    {
        var almacen = new AlmacenSimulado();
        var ahora = new DateTime(2027, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        almacen.Objetos.Add(new ObjetoSimulado("respaldos-diarios", "2027-09-15/120000-x.tar.age", ahora));

        almacen.EjecutarCicloDeVida(ahora.AddDays(7).AddMinutes(1));

        Assert.Empty(almacen.De("respaldos-diarios"));
        Assert.Empty(almacen.De("respaldos-semanales"));
        Assert.Empty(almacen.De("respaldos-mensuales"));
    }
}
