using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.UnitTests.Plataforma;

public sealed class PasosDeRestauracionTests
{
    private sealed class PasoDePrueba(int orden, string nombre) : IPasoDeRestauracion
    {
        public int Orden { get; } = orden;

        public string Nombre { get; } = nombre;

        public Task<ResultadoDePaso> Ejecutar(CancellationToken cancelacion = default) => Task.FromResult(ResultadoDePaso.Correcto);
    }

    [Fact]
    public void LosPasosDeRestauracion_SeOrdenanPorModuloPlataformaIdentidadYAuditoria()
    {
        var pasos = new IPasoDeRestauracion[]
        {
            new PasoDePrueba(OrdenDePasoDeRestauracion.Auditoria, "depurar"),
            new PasoDePrueba(OrdenDePasoDeRestauracion.Identidad + 1, "aplicar-bajas"),
            new PasoDePrueba(OrdenDePasoDeRestauracion.Plataforma, "migraciones"),
        };

        var orden = pasos.OrderBy(p => p.Orden).Select(p => p.Nombre).ToList();

        Assert.Equal(["migraciones", "aplicar-bajas", "depurar"], orden);
    }

    [Fact]
    public async Task UnPasoDevuelveUnResultadoEnumeradoSinTextoLibre()
    {
        IPasoDeRestauracion paso = new PasoDePrueba(OrdenDePasoDeRestauracion.Plataforma, "migraciones");

        Assert.Equal(ResultadoDePaso.Correcto, await paso.Ejecutar());
        Assert.Equal(["Correcto", "Fallido"], Enum.GetNames<ResultadoDePaso>());
    }
}
