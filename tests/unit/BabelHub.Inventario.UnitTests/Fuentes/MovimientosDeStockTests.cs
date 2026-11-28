using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Domain.Fuentes;

namespace BabelHub.Inventario.UnitTests.Fuentes;

public sealed class MovimientosDeStockTests
{
    private readonly BitacoraDeLlamadas _bitacora = new();
    private readonly RepositorioDeFuentesEnMemoria _fuentes = new();
    private readonly RepositorioDeFichasEnMemoria _fichas;
    private readonly RepositorioDeExistenciasEnMemoria _existencias;
    private readonly ReservasDeStockDoble _reservas;
    private readonly Fuente _lima;
    private readonly Fuente _cusco;
    private readonly Fuente _ayacucho;

    public MovimientosDeStockTests()
    {
        _fichas = new RepositorioDeFichasEnMemoria(_bitacora);
        _existencias = new RepositorioDeExistenciasEnMemoria(_fuentes, _bitacora);
        _reservas = new ReservasDeStockDoble(_bitacora);
        _lima = _fuentes.Agregar("lima", recepcion: true);
        _cusco = _fuentes.Agregar("cusco");
        _ayacucho = _fuentes.Agregar("ayacucho");
    }

    private MovimientosDeStock Movimientos() => new(_fichas, _existencias, _reservas, new AplicadorDeMovimientos(_existencias));

    private Guid Impresa() => _fichas.Agregar().EdicionId;

    // --- Bloquear ---

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Bloquear_SigueElOrdenDeLaJerarquia_GarantizarLiberarBloquearYSoloAlFinalAplicarLosDeltas()
    {
        var libro = Impresa();
        _existencias.Poner(_lima.Id, libro, 5, 5);
        _reservas.Liberadas.Add(new UnidadesLiberadas(_lima.Id, libro, 2));

        var bloqueo = await Movimientos().Bloquear([libro]);

        Assert.True(bloqueo.EstaBloqueado);
        Assert.Equal(
            [
                "fichas.SonTodasImpresas",
                "existencias.GarantizarFilas",
                "reservas.LiberarVencidas",
                "existencias.BloquearFilas",
                "existencias.AplicarLiberacion(0,-2)",
            ],
            _bitacora.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Bloquear_ConUnaReservaVencidaSinProcesar_DevuelveLasCantidadesYaLiberadas()
    {
        var libro = Impresa();
        _existencias.Poner(_lima.Id, libro, 5, 5);
        _reservas.Liberadas.Add(new UnidadesLiberadas(_lima.Id, libro, 2));

        var bloqueo = await Movimientos().Bloquear([libro]);

        var fila = bloqueo.Fila(_lima.Id, libro);
        Assert.Equal(5, fila.Fisico);
        Assert.Equal(3, fila.Reservado);
        Assert.Equal(3, _existencias.Filas[(_lima.Id, libro)].Reservado);
    }

    [Fact]
    public async Task Bloquear_SinVencidas_NoAplicaNingunDelta()
    {
        var libro = Impresa();
        _existencias.Poner(_lima.Id, libro, 5, 2);

        await Movimientos().Bloquear([libro]);

        Assert.Empty(_existencias.Liberaciones);
        Assert.DoesNotContain(_bitacora.Llamadas, l => l.StartsWith("existencias.Aplicar", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Bloquear_AgregaLasLiberacionesDeLaMismaParejaYLasAplicaOrdenadasPorEdicionYFuente()
    {
        var uno = Impresa();
        var dos = Impresa();
        _existencias.Poner(_lima.Id, uno, 10, 6);
        _existencias.Poner(_cusco.Id, uno, 10, 4);
        _existencias.Poner(_lima.Id, dos, 10, 3);
        _reservas.Liberadas.AddRange(
        [
            new UnidadesLiberadas(_lima.Id, dos, 1),
            new UnidadesLiberadas(_cusco.Id, uno, 2),
            new UnidadesLiberadas(_lima.Id, uno, 1),
            new UnidadesLiberadas(_lima.Id, uno, 2),
            new UnidadesLiberadas(_lima.Id, dos, 0),
        ]);

        var bloqueo = await Movimientos().Bloquear([dos, uno]);

        Assert.Equal(3, _existencias.Liberaciones.Count); // una sola por pareja y ninguna vacía
        var esperado = new[] { (uno, _lima.Id, 3), (uno, _cusco.Id, 2), (dos, _lima.Id, 1) }
            .OrderBy(p => p.Item1, OrdenDeBloqueo.DeIdentificadores)
            .ThenBy(p => p.Item2, OrdenDeBloqueo.DeIdentificadores)
            .Select(p => new MovimientoDeStock(p.Item2, p.Item1, 0, -p.Item3))
            .ToList();
        Assert.Equal(esperado, _existencias.Liberaciones);
        Assert.Equal(3, bloqueo.Fila(_lima.Id, uno).Reservado);
        Assert.Equal(2, bloqueo.Fila(_cusco.Id, uno).Reservado);
        Assert.Equal(2, bloqueo.Fila(_lima.Id, dos).Reservado);
    }

    [Fact]
    public async Task Bloquear_DevuelveLasFilasDeTodasLasFuentesDeLaEdicion_ConCerosParaLasQueNoTenianFila()
    {
        var libro = Impresa();
        _existencias.Poner(_lima.Id, libro, 8, 0);

        var bloqueo = await Movimientos().Bloquear([libro]);

        Assert.Equal(3, bloqueo.Filas.Count);
        Assert.Equal(8, bloqueo.Fila(_lima.Id, libro).Fisico);
        Assert.Equal(0, bloqueo.Fila(_cusco.Id, libro).Fisico);
        Assert.Equal(0, bloqueo.Fila(_ayacucho.Id, libro).Reservado);
        Assert.Contains((_cusco.Id, libro), _existencias.Filas.Keys); // la fila se garantizó con ceros
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-04")]
    public async Task Bloquear_UnaFuenteNuevaApareceConStockCeroSinCambiarCodigoNiEstructura()
    {
        var libro = Impresa();
        _existencias.Poner(_lima.Id, libro, 8, 1);
        var sexta = _fuentes.Agregar("piura");

        var bloqueo = await Movimientos().Bloquear([libro]);

        Assert.Equal(new ExistenciaBloqueada(sexta.Id, libro, 0, 0), bloqueo.Fila(sexta.Id, libro));
    }

    [Fact]
    public async Task Bloquear_ConUnaEdicionDigitalOSinFicha_RechazaYNoTocaNada()
    {
        var digital = _fichas.Agregar(FormatoDeEdicion.Digital).EdicionId;
        var sinFicha = Guid.NewGuid();
        var impresa = Impresa();

        foreach (var ediciones in new[] { new[] { digital }, [sinFicha], [impresa, digital] })
        {
            _bitacora.Llamadas.Clear();

            var bloqueo = await Movimientos().Bloquear(ediciones);

            Assert.False(bloqueo.EstaBloqueado);
            Assert.Equal(ResultadoDeBloqueo.EdicionInexistenteONoImpresa, bloqueo.Resultado);
            Assert.Equal(["fichas.SonTodasImpresas"], _bitacora.Llamadas);
        }

        Assert.Empty(_existencias.Filas);
    }

    [Fact]
    public async Task Bloquear_SinEdiciones_NoHaceNada()
    {
        var bloqueo = await Movimientos().Bloquear([]);

        Assert.True(bloqueo.EstaBloqueado);
        Assert.Empty(bloqueo.Filas);
        Assert.Empty(_bitacora.Llamadas);
    }

    [Fact]
    public async Task Bloquear_ConEdicionesRepetidas_LasPideUnaSolaVezYEnElOrdenDeBloqueo()
    {
        var a = Impresa();
        var b = Impresa();
        var c = Impresa();
        IReadOnlyCollection<Guid>? recibidas = null;
        var fichas = new FichasQueAnotan(_fichas, ids => recibidas = ids);
        var movimientos = new MovimientosDeStock(fichas, _existencias, _reservas, new AplicadorDeMovimientos(_existencias));

        await movimientos.Bloquear([c, a, b, a, c]);

        Assert.NotNull(recibidas);
        Assert.Equal(3, recibidas.Count);
        Assert.Equal(recibidas.Order(OrdenDeBloqueo.DeIdentificadores), recibidas);
    }

    [Fact]
    public async Task Bloquear_SiLaLiberacionDevuelveMasDeLoReservado_FallaSinAplicarNada()
    {
        var libro = Impresa();
        _existencias.Poner(_lima.Id, libro, 5, 1);
        _reservas.Liberadas.Add(new UnidadesLiberadas(_lima.Id, libro, 2));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Movimientos().Bloquear([libro]));

        Assert.Empty(_existencias.Liberaciones);
        Assert.Equal((5, 1), _existencias.Filas[(_lima.Id, libro)]);
    }

    [Fact]
    public async Task Bloquear_SiLaLiberacionNombraUnaFuenteSinFila_Falla()
    {
        var libro = Impresa();
        _reservas.Liberadas.Add(new UnidadesLiberadas(Guid.NewGuid(), libro, 1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Movimientos().Bloquear([libro]));
    }

    [Fact]
    public async Task Bloquear_SiUnaLiberacionTraeUnidadesNegativas_Falla()
    {
        var libro = Impresa();
        _existencias.Poner(_lima.Id, libro, 5, 3);
        _reservas.Liberadas.Add(new UnidadesLiberadas(_lima.Id, libro, -1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Movimientos().Bloquear([libro]));
    }

    [Fact]
    public void ElOrdenDeBloqueo_CoincideConElTextoHexadecimalDelUuid_QueEsElOrdenDeBytesDePostgreSql()
    {
        var azar = new Random(20261114);
        var bytes = new byte[16];
        var guids = Enumerable.Range(0, 2000).Select(_ => { azar.NextBytes(bytes); return new Guid(bytes); }).ToList();

        var porOrden = guids.Order(OrdenDeBloqueo.DeIdentificadores).ToList();
        var porTexto = guids.OrderBy(g => g.ToString("N"), StringComparer.Ordinal).ToList();

        Assert.Equal(porTexto, porOrden);
        Assert.Equal(0, OrdenDeBloqueo.Comparar(guids[0], guids[0]));
    }

    [Fact]
    public void ElOrdenDeParejas_ComparaPrimeroLaEdicionYLuegoLaFuente()
    {
        var uno = Guid.Parse("10000000-0000-0000-0000-000000000000");
        var dos = Guid.Parse("20000000-0000-0000-0000-000000000000");

        Assert.True(OrdenDeBloqueo.Comparar(uno, dos, dos, uno) < 0);
        Assert.True(OrdenDeBloqueo.Comparar(uno, uno, uno, dos) < 0);
        Assert.Equal(0, OrdenDeBloqueo.Comparar(uno, dos, uno, dos));
    }

    // --- Aplicar ---

    [Fact]
    public async Task Aplicar_DevuelveLasCantidadesAnteriorYPosterior()
    {
        var libro = Impresa();
        _existencias.Poner(_lima.Id, libro, 9, 1);

        var resultado = await Movimientos().Aplicar(new MovimientoDeStock(_lima.Id, libro, 0, 1));

        Assert.True(resultado.Aplicado);
        Assert.Equal(new CantidadesDeStock(9, 1), resultado.Anterior);
        Assert.Equal(new CantidadesDeStock(9, 2), resultado.Posterior);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Aplicar_ConDosClientesPorLaUltimaUnidad_SoloLaPrimeraProcedeYElVendibleNoQuedaNegativo()
    {
        var libro = Impresa();
        _existencias.Poner(_lima.Id, libro, 5, 4);
        var aplicador = new AplicadorDeMovimientos(_existencias);

        var primero = await aplicador.Aplicar(new MovimientoDeStock(_lima.Id, libro, 0, 1));
        var segundo = await aplicador.Aplicar(new MovimientoDeStock(_lima.Id, libro, 0, 1));

        Assert.True(primero.Aplicado);
        Assert.Equal(ResultadoDeMovimiento.NoAplicable, segundo.Resultado);
        Assert.Null(segundo.Posterior);
        var (fisico, reservado) = _existencias.Filas[(_lima.Id, libro)];
        Assert.Equal(0, fisico - reservado);
    }

    [Fact]
    public async Task Aplicar_ConUnaFilaFueraDelAlcanceDelActor_RespondeDenegadoPorAlcanceYNoFaltaDeStock()
    {
        var libro = Impresa();
        _existencias.Poner(_cusco.Id, libro, 5, 0);
        _existencias.FuentesFueraDelAlcance.Add(_cusco.Id);

        var resultado = await Movimientos().Aplicar(new MovimientoDeStock(_cusco.Id, libro, -1, 0));

        Assert.Equal(ResultadoDeMovimiento.DeniegadoPorAlcance, resultado.Resultado);
        Assert.Equal((5, 0), _existencias.Filas[(_cusco.Id, libro)]);
    }

    [Fact]
    public async Task Aplicar_ConStockInsuficienteEnLaPropiaFuente_EsNoAplicableYNoCambiaNada()
    {
        var libro = Impresa();
        _existencias.Poner(_lima.Id, libro, 2, 0);

        var resultado = await Movimientos().Aplicar(new MovimientoDeStock(_lima.Id, libro, -3, 0));

        Assert.Equal(ResultadoDeMovimiento.NoAplicable, resultado.Resultado);
        Assert.Equal((2, 0), _existencias.Filas[(_lima.Id, libro)]);
    }

    [Fact]
    public async Task Aplicar_NoLiberaVencidasNiUsaElPuertoDeReservas()
    {
        var libro = Impresa();
        _existencias.Poner(_lima.Id, libro, 5, 5);
        _reservas.Liberadas.Add(new UnidadesLiberadas(_lima.Id, libro, 2));

        // El aplicador de Reservas (sin liberación previa): el stock sigue tal cual y el puerto no se llamó.
        var resultado = await new AplicadorDeMovimientos(_existencias).Aplicar(new MovimientoDeStock(_lima.Id, libro, 0, 1));

        Assert.False(resultado.Aplicado);
        Assert.Equal(0, _reservas.LlamadasALiberar);
        Assert.Equal(0, _reservas.LlamadasAVencidas);
    }

    [Fact]
    public async Task Aplicar_ConUnMovimientoInvalido_Lanza()
    {
        var aplicador = new AplicadorDeMovimientos(_existencias);

        await Assert.ThrowsAsync<ArgumentNullException>(() => aplicador.Aplicar(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => aplicador.Aplicar(new MovimientoDeStock(Guid.Empty, Guid.NewGuid(), 1, 0)));
        await Assert.ThrowsAsync<ArgumentException>(() => aplicador.Aplicar(new MovimientoDeStock(_lima.Id, Guid.Empty, 1, 0)));
        await Assert.ThrowsAsync<ArgumentException>(() => aplicador.Aplicar(new MovimientoDeStock(_lima.Id, Guid.NewGuid(), 0, 0)));
    }

    [Fact]
    public async Task ElAplicadorYElDominio_DanElMismoResultadoParaLosMismosCasos()
    {
        // Paridad de la regla: el doble implementa la regla del Dominio y la Aplicación solo delega; la paridad con el UPDATE real está en T18.
        var libro = Impresa();
        var azar = new Random(1114);
        var aplicador = new AplicadorDeMovimientos(_existencias);
        _existencias.Poner(_lima.Id, libro, 6, 2);

        for (var i = 0; i < 300; i++)
        {
            var (fisico, reservado) = _existencias.Filas[(_lima.Id, libro)];
            var df = azar.Next(-4, 5);
            var dr = azar.Next(-4, 5);
            if (df == 0 && dr == 0)
            {
                continue;
            }

            var esperado = new ExistenciaDeStock(fisico, reservado).Aplicar(df, dr);
            var resultado = await aplicador.Aplicar(new MovimientoDeStock(_lima.Id, libro, df, dr));

            Assert.Equal(esperado.Aplicado, resultado.Aplicado);
            if (resultado.Aplicado)
            {
                Assert.Equal(new CantidadesDeStock(esperado.Existencia.Fisico, esperado.Existencia.Reservado), resultado.Posterior);
            }
        }
    }

    private sealed class FichasQueAnotan(IRepositorioDeFichas interno, Action<IReadOnlyCollection<Guid>> alConsultar) : IRepositorioDeFichas
    {
        public Task<FichaDeEdicion?> PorId(Guid edicionId, CancellationToken cancelacion = default) => interno.PorId(edicionId, cancelacion);

        public Task<IReadOnlyList<FichaDeEdicion>> PorIds(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) => interno.PorIds(edicionIds, cancelacion);

        public Task<ResultadoDeGuardadoDeFicha> Guardar(FichaDeEdicion ficha, CancellationToken cancelacion = default) => interno.Guardar(ficha, cancelacion);

        public Task<bool> SonTodasImpresas(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
        {
            alConsultar(edicionIds);
            return interno.SonTodasImpresas(edicionIds, cancelacion);
        }
    }
}
