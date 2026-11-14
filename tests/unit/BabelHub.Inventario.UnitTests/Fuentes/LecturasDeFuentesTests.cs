using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Domain.Fuentes;

namespace BabelHub.Inventario.UnitTests.Fuentes;

public sealed class ConsultaDeExistenciasTests
{
    private readonly BitacoraDeLlamadas _bitacora = new();
    private readonly LecturaDirecta _lectura = new();
    private readonly RepositorioDeFuentesEnMemoria _fuentes = new();
    private readonly RepositorioDeExistenciasEnMemoria _existencias;
    private readonly ReservasDeStockDoble _reservas;
    private readonly Fuente _lima;
    private readonly Fuente _cusco;

    public ConsultaDeExistenciasTests()
    {
        _existencias = new RepositorioDeExistenciasEnMemoria(_fuentes, _bitacora);
        _reservas = new ReservasDeStockDoble(_bitacora);
        _lima = _fuentes.Agregar("lima", recepcion: true);
        _cusco = _fuentes.Agregar("cusco");
    }

    private ConsultaDeExistencias Consulta() => new(_lectura, _fuentes, _existencias, _reservas);

    [Fact]
    [Trait("Criterio", "AC-FUE-03")]
    public async Task Disponibilidad_ConLima8YCusco3_Responde11EnLaRedYElDesglosePorFuente()
    {
        var libro = Guid.NewGuid();
        _existencias.Poner(_lima.Id, libro, 8, 0);
        _existencias.Poner(_cusco.Id, libro, 3, 0);

        var disponibilidad = Assert.Single(await Consulta().Disponibilidad([libro]));

        Assert.Equal(libro, disponibilidad.EdicionId);
        Assert.Equal(11, disponibilidad.VendibleDeLaRed);
        Assert.Collection(
            disponibilidad.PorFuente,
            cusco => { Assert.Equal("cusco", cusco.CodigoDeFuente); Assert.Equal(3, cusco.Vendible); },
            lima => { Assert.Equal("lima", lima.CodigoDeFuente); Assert.Equal(8, lima.Vendible); });
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-01")]
    public async Task Disponibilidad_ConFisico9YUnaReservaConfirmadaDe1_MuestraFisico9Reservado1YVendible8()
    {
        var libro = Guid.NewGuid();
        var ayacucho = _fuentes.Agregar("ayacucho");
        _existencias.Poner(ayacucho.Id, libro, 9, 1);

        var existencia = (await Consulta().Disponibilidad([libro])).Single().PorFuente.Single(e => e.FuenteId == ayacucho.Id);

        Assert.Equal((9, 1, 8), (existencia.Fisico, existencia.Reservado, existencia.Vendible));
        Assert.Equal(EstadoDeExistencia.Normal, existencia.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-04")]
    public async Task Disponibilidad_DeUnaFuenteSinFilas_FiguraEnCeroSinSinStockInventado()
    {
        var libro = Guid.NewGuid();
        _existencias.Poner(_lima.Id, libro, 4, 0);
        var sexta = _fuentes.Agregar("piura");

        var desglose = (await Consulta().Disponibilidad([libro])).Single().PorFuente;

        var nueva = desglose.Single(e => e.FuenteId == sexta.Id);
        Assert.Equal((0, 0, 0), (nueva.Fisico, nueva.Reservado, nueva.Vendible));
        Assert.Equal(EstadoDeExistencia.SinStock, nueva.Estado);
        Assert.Equal(4, (await Consulta().Disponibilidad([libro])).Single().VendibleDeLaRed);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Disponibilidad_ConFisico5Reservado5YVencidas2_DaVendible2YConTodoVencido_DaElFisico()
    {
        var uno = Guid.NewGuid();
        var dos = Guid.NewGuid();
        _existencias.Poner(_lima.Id, uno, 5, 5);
        _existencias.Poner(_lima.Id, dos, 5, 2);
        _reservas.Vencidas.AddRange([new UnidadesVencidas(_lima.Id, uno, 2), new UnidadesVencidas(_lima.Id, dos, 2)]);

        var resultado = await Consulta().Disponibilidad([uno, dos]);

        Assert.Equal(2, resultado[0].PorFuente.Single(e => e.FuenteId == _lima.Id).Vendible);
        Assert.Equal(5, resultado[1].PorFuente.Single(e => e.FuenteId == _lima.Id).Vendible);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Disponibilidad_SinVencidas_UnaReservaPendienteVencidaNoSeDescuentaDosVeces()
    {
        var libro = Guid.NewGuid();
        _existencias.Poner(_lima.Id, libro, 5, 5);

        Assert.Equal(0, (await Consulta().Disponibilidad([libro])).Single().VendibleDeLaRed);
    }

    [Fact]
    public async Task Disponibilidad_ConVencidasQueSuperanLoReservado_LasTopaYNuncaPasaDelFisico()
    {
        var libro = Guid.NewGuid();
        _existencias.Poner(_lima.Id, libro, 5, 1);
        _reservas.Vencidas.Add(new UnidadesVencidas(_lima.Id, libro, 4));

        var vendible = (await Consulta().Disponibilidad([libro])).Single().PorFuente.Single(e => e.FuenteId == _lima.Id).Vendible;

        Assert.Equal(5, vendible);
    }

    [Fact]
    public async Task Disponibilidad_LeeTodoEnUnaSolaInstantanea()
    {
        var uno = Guid.NewGuid();
        var dos = Guid.NewGuid();

        await Consulta().Disponibilidad([uno, dos]);

        Assert.Equal(1, _lectura.Instantaneas);
        Assert.Equal(["existencias.Leer", "reservas.VencidasPorEdicion"], _bitacora.Llamadas);
    }

    [Fact]
    public async Task Disponibilidad_ConVariasEdiciones_ConservaElOrdenPedidoYNoRepite()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        _existencias.Poner(_lima.Id, a, 1, 0);
        _existencias.Poner(_lima.Id, b, 2, 0);

        var resultado = await Consulta().Disponibilidad([b, a, b]);

        Assert.Equal([b, a], resultado.Select(r => r.EdicionId));
    }

    [Fact]
    public async Task Disponibilidad_SinEdiciones_NoConsultaNada()
    {
        Assert.Empty(await Consulta().Disponibilidad([]));
        Assert.Equal(0, _lectura.Instantaneas);
        await Assert.ThrowsAsync<ArgumentNullException>(() => Consulta().Disponibilidad(null!));
    }

    [Fact]
    public async Task Disponibilidad_DeUnaEdicionSinFilas_DaCeroEnTodasLasFuentes()
    {
        var resultado = (await Consulta().Disponibilidad([Guid.NewGuid()])).Single();

        Assert.Equal(0, resultado.VendibleDeLaRed);
        Assert.Equal(2, resultado.PorFuente.Count);
        Assert.All(resultado.PorFuente, e => Assert.Equal(EstadoDeExistencia.SinStock, e.Estado));
    }

    // --- las existencias de una fuente ---

    private Guid Poner(Fuente fuente, int fisico, int reservado)
    {
        var edicion = Guid.NewGuid();
        _existencias.Poner(fuente.Id, edicion, fisico, reservado);
        return edicion;
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-15")]
    public async Task DeLaFuente_ConVendiblesDe0_2Y3_MuestraSinStockBajoYNormal()
    {
        var sin = Poner(_lima, 4, 4);
        var bajo = Poner(_lima, 4, 2);
        var normal = Poner(_lima, 3, 0);

        var pagina = await Consulta().DeLaFuente(new ConsultaDeExistenciasDeFuente(_lima.Id, null, null, 1, 50));

        Assert.Equal(3, pagina.Total);
        Assert.Equal(EstadoDeExistencia.SinStock, pagina.Elementos.Single(e => e.EdicionId == sin).Estado);
        Assert.Equal(EstadoDeExistencia.Bajo, pagina.Elementos.Single(e => e.EdicionId == bajo).Estado);
        Assert.Equal(EstadoDeExistencia.Normal, pagina.Elementos.Single(e => e.EdicionId == normal).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-07")]
    public async Task DeLaFuente_ConElFiltroDeEdiciones_MuestraSoloLasCoincidentesConFisicoReservadoYVendible()
    {
        var catedral = Poner(_lima, 6, 2);
        Poner(_lima, 9, 0);
        Poner(_cusco, 5, 0);

        var pagina = await Consulta().DeLaFuente(new ConsultaDeExistenciasDeFuente(_lima.Id, [catedral], null, 1, 50));

        var existencia = Assert.Single(pagina.Elementos);
        Assert.Equal((catedral, 6, 2, 4), (existencia.EdicionId, existencia.Fisico, existencia.Reservado, existencia.Vendible));
        Assert.Equal(1, pagina.Total);
    }

    [Fact]
    public async Task DeLaFuente_ConElFiltroDeEstado_DevuelveSoloEseEstadoYSuTotal()
    {
        Poner(_lima, 4, 4);
        var bajo1 = Poner(_lima, 4, 2);
        var bajo2 = Poner(_lima, 1, 0);
        Poner(_lima, 10, 0);

        var pagina = await Consulta().DeLaFuente(new ConsultaDeExistenciasDeFuente(_lima.Id, null, EstadoDeExistencia.Bajo, 1, 50));

        Assert.Equal(2, pagina.Total);
        Assert.Equivalent(new[] { bajo1, bajo2 }, pagina.Elementos.Select(e => e.EdicionId));
    }

    [Fact]
    public async Task DeLaFuente_CuentaLasVencidasSinProcesarParaElEstado()
    {
        var libro = Poner(_lima, 5, 5);
        _reservas.Vencidas.Add(new UnidadesVencidas(_lima.Id, libro, 3));

        var existencia = (await Consulta().DeLaFuente(new ConsultaDeExistenciasDeFuente(_lima.Id, null, null, 1, 10))).Elementos.Single();

        Assert.Equal(3, existencia.Vendible);
        Assert.Equal(EstadoDeExistencia.Normal, existencia.Estado);
    }

    [Fact]
    public async Task DeLaFuente_PaginaEnOrdenEstableYDevuelveElTotal()
    {
        var ediciones = Enumerable.Range(0, 7).Select(_ => Poner(_lima, 5, 0)).Order(OrdenDeBloqueo.DeIdentificadores).ToList();

        var primera = await Consulta().DeLaFuente(new ConsultaDeExistenciasDeFuente(_lima.Id, null, null, 1, 3));
        var tercera = await Consulta().DeLaFuente(new ConsultaDeExistenciasDeFuente(_lima.Id, null, null, 3, 3));
        var cuarta = await Consulta().DeLaFuente(new ConsultaDeExistenciasDeFuente(_lima.Id, null, null, 4, 3));

        Assert.Equal(7, primera.Total);
        Assert.Equal(ediciones.Take(3), primera.Elementos.Select(e => e.EdicionId));
        Assert.Equal(ediciones.Skip(6), tercera.Elementos.Select(e => e.EdicionId));
        Assert.Empty(cuarta.Elementos);
        Assert.Equal((3, 3), (tercera.Pagina, tercera.Tamano));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-12")]
    public async Task DeLaFuente_DeUnaFuenteInexistente_DevuelveUnaPaginaVacia()
    {
        var pagina = await Consulta().DeLaFuente(new ConsultaDeExistenciasDeFuente(Guid.NewGuid(), null, null, 1, 10));

        Assert.Empty(pagina.Elementos);
        Assert.Equal(0, pagina.Total);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(-1, 10)]
    public async Task DeLaFuente_ConUnaPaginaOUnTamanoFueraDeRango_Lanza(int pagina, int tamano)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Consulta().DeLaFuente(new ConsultaDeExistenciasDeFuente(_lima.Id, null, null, pagina, tamano)));
    }

    [Fact]
    public async Task DeLaFuente_SoloPideVencidasDeLasEdicionesConUnidadesReservadas()
    {
        Poner(_lima, 5, 0);
        var conReserva = Poner(_lima, 5, 1);

        await Consulta().DeLaFuente(new ConsultaDeExistenciasDeFuente(_lima.Id, null, null, 1, 10));

        Assert.Equal(1, _reservas.LlamadasAVencidas);
        Assert.Equal(1, _lectura.Instantaneas);
        Assert.Equal([conReserva], Assert.Single(_reservas.EdicionesPedidasDeVencidas)); // la única consulta de vencidas es por la edición con reserva
    }
}

public sealed class DirectorioDeFuentesTests
{
    private readonly LecturaDirecta _lectura = new();
    private readonly RepositorioDeFuentesEnMemoria _fuentes = new();
    private readonly RepositorioDeRutasEnMemoria _rutas = new();
    private readonly RepositorioDeFichasEnMemoria _fichas = new();

    private DirectorioDeFuentes Directorio() => new(_lectura, _fuentes, _rutas, _fichas);

    [Fact]
    public async Task Listar_DevuelveLasFuentesOrdenadasPorCodigoSinElContacto()
    {
        _fuentes.Agregar("lima", recepcion: true);
        _fuentes.Agregar("ayacucho");
        _fuentes.Agregar("cusco");

        var lista = await Directorio().Listar();

        Assert.Equal(["ayacucho", "cusco", "lima"], lista.Select(f => f.Codigo));
        Assert.Single(lista, f => f.EsRecepcion);
    }

    [Fact]
    public async Task PorIdYPorCodigo_DevuelvenLaFuenteOSuAusencia()
    {
        var cusco = _fuentes.Agregar("cusco");

        Assert.Equal(cusco.Id, (await Directorio().PorId(cusco.Id))!.Id);
        Assert.Equal("cusco", (await Directorio().PorCodigo("cusco"))!.Codigo);
        Assert.Null(await Directorio().PorId(Guid.NewGuid()));
        Assert.Null(await Directorio().PorCodigo("arequipa"));
    }

    [Fact]
    public async Task PorCodigo_ConUnaFormaInvalida_ResponderNoExisteSinConsultar()
    {
        Assert.Null(await Directorio().PorCodigo("Cusco; DROP TABLE"));
        Assert.Null(await Directorio().PorCodigo(string.Empty));
        Assert.Equal(0, _lectura.Instantaneas);
        await Assert.ThrowsAsync<ArgumentNullException>(() => Directorio().PorCodigo(null!));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public async Task FuenteDeRecepcion_DevuelveLaUnicaMarcada()
    {
        _fuentes.Agregar("lima");
        var arequipa = _fuentes.Agregar("arequipa", recepcion: true);

        Assert.Equal(arequipa.Id, (await Directorio().FuenteDeRecepcion())!.Id);
    }

    [Fact]
    public async Task FuenteDeRecepcion_SinFuentes_DevuelveNulo()
    {
        Assert.Null(await Directorio().FuenteDeRecepcion());
    }

    [Fact]
    public async Task DeLaRegion_DevuelveTodasLasFuentesDeLaRegionOrdenadasPorCodigo()
    {
        _fuentes.Agregar("lima-sur", region: "lima");
        _fuentes.Agregar("lima-norte", region: "lima");
        _fuentes.Agregar("cusco");

        var lima = await Directorio().DeLaRegion("lima");

        Assert.Equal(["lima-norte", "lima-sur"], lima.Select(f => f.Codigo));
        Assert.Empty(await Directorio().DeLaRegion("piura"));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-11")]
    public async Task PlazoDeRuta_ConUnaRutaDefinida_DaLosDiasYSinRuta_DaNulo()
    {
        var cusco = _fuentes.Agregar("cusco");
        var ayacucho = _fuentes.Agregar("ayacucho");
        var lima = _fuentes.Agregar("lima");
        _rutas.Rutas.Add(RutaEntreFuentes.Definir(cusco.Id, ayacucho.Id, 2));

        Assert.Equal(2, await Directorio().PlazoDeRuta(cusco.Id, ayacucho.Id));
        Assert.Null(await Directorio().PlazoDeRuta(lima.Id, ayacucho.Id));
        Assert.Null(await Directorio().PlazoDeRuta(ayacucho.Id, cusco.Id)); // es direccional
    }

    [Fact]
    public async Task RutasHaciaDestino_DevuelveLasDefinidasOrdenadasPorPlazo()
    {
        var destino = _fuentes.Agregar("ayacucho");
        var cusco = _fuentes.Agregar("cusco");
        var lima = _fuentes.Agregar("lima");
        var otra = _fuentes.Agregar("piura");
        _rutas.Rutas.AddRange([RutaEntreFuentes.Definir(lima.Id, destino.Id, 3), RutaEntreFuentes.Definir(cusco.Id, destino.Id, 1), RutaEntreFuentes.Definir(destino.Id, otra.Id, 1)]);

        var rutas = await Directorio().RutasHaciaDestino(destino.Id);

        Assert.Equal([(cusco.Id, 1), (lima.Id, 3)], rutas.Select(r => (r.OrigenId, r.PlazoEnDias)));
        Assert.All(rutas, r => Assert.Equal(destino.Id, r.DestinoId));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-12")]
    public async Task Fichas_PorId_DevuelveElFormatoOSuAusencia()
    {
        var impresa = _fichas.Agregar().EdicionId;
        var digital = _fichas.Agregar(FormatoDeEdicion.Digital).EdicionId;
        IFichasDeEdiciones fichas = Directorio();

        Assert.True((await fichas.PorId(impresa))!.EsImpresa);
        Assert.False((await fichas.PorId(digital))!.EsImpresa);
        Assert.Null(await fichas.PorId(Guid.NewGuid()));
    }
}
