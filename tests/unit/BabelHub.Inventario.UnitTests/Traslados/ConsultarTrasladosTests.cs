using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Domain.Traslados;
using BabelHub.Inventario.UnitTests.Soporte;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>T14: el detalle, la lista con filtros y los traslados de un pedido, con el alcance comparado en la Aplicación.</summary>
public sealed class ConsultarTrasladosTests
{
    private static (EntornoDeTraslados Entorno, Guid Edicion) Preparar()
    {
        var entorno = new EntornoDeTraslados(ActorDePrueba.DeAdministradorCentral());
        return (entorno, entorno.Impresa());
    }

    private static ListarTrasladosEntrada Lista(string? estado = null, string? fuente = null, bool? vinculado = null, int? demora = null, int pagina = 1, int tamano = 20) => new(estado, fuente, vinculado, demora, pagina, tamano);

    // ---------- Detalle ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-10")]
    public async Task ElDetalleMuestraNumeroEstadoGuiaYFechas()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.EnTransito, guia: "CA-771204");
        entorno.ComoEncargadoDe(entorno.Ayacucho);

        var resultado = await entorno.Detalle().Ejecutar(new ConsultarTrasladoEntrada(traslado.Id));

        var d = resultado.Valor;
        Assert.Equal((traslado.Numero, "en-transito", "CA-771204", "cusco", "ayacucho", 4), (d.Numero, d.Estado, d.Guia, d.CodigoDeOrigen, d.CodigoDeDestino, d.Cantidad));
        Assert.NotNull(d.DespachadoEn);
        Assert.Empty(entorno.Base.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public async Task ElDetalleDeUnVinculadoMuestraLaFechaEstimadaOPorConfirmar()
    {
        var (entorno, edicion) = Preparar();
        var conRuta = entorno.Vinculado(edicion, entorno.Cusco, entorno.Ayacucho, 1, Guid.NewGuid());
        var sinRuta = Traslado.VincularAUnPedido(Guid.NewGuid(), edicion, entorno.Lima, entorno.Ayacucho, 1, Guid.NewGuid(), EstimacionDeLlegada.Calcular(new DateOnly(2026, 11, 14), null), null, EntornoDeTraslados.Ahora);
        entorno.Traslados.Sembrar(sinRuta);

        var a = (await entorno.Detalle().Ejecutar(new ConsultarTrasladoEntrada(conRuta.Id))).Valor;
        var b = (await entorno.Detalle().Ejecutar(new ConsultarTrasladoEntrada(sinRuta.Id))).Valor;

        Assert.Equal((new DateOnly(2026, 11, 16), false, true), (a.FechaEstimadaDeLlegada, a.EstimadaPorConfirmar, a.EsVinculado));
        Assert.Equal(((DateOnly?)null, true), (b.FechaEstimadaDeLlegada, b.EstimadaPorConfirmar));
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-03")]
    [InlineData("cusco", true)]
    [InlineData("ayacucho", true)]
    [InlineData("lima", false)]
    public async Task ElEncargadoSoloVeElDetalleSiSuSucursalEsElOrigenOElDestino_OtraEsUnAccesoDenegado(string sucursal, bool ve)
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);
        entorno.ComoEncargadoDe(sucursal == "cusco" ? entorno.Cusco : sucursal == "ayacucho" ? entorno.Ayacucho : entorno.Lima);

        var resultado = await entorno.Detalle().Ejecutar(new ConsultarTrasladoEntrada(traslado.Id));

        Assert.Equal(ve, resultado.EsExito);
        Assert.Equal(ve ? [] : ["fuera-de-sucursal"], entorno.Base.MotivosDeDenegacion.ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task UnIdInexistenteYUnoAjenoSonElMismoRechazoParaElEncargado_ParaLaAdministracionYLogisticaEsNoEncontrado()
    {
        var (entorno, edicion) = Preparar();
        var ajeno = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);
        entorno.ComoEncargadoDe(entorno.Lima);

        var inexistente = await entorno.Detalle().Ejecutar(new ConsultarTrasladoEntrada(Guid.NewGuid()));
        var deOtra = await entorno.Detalle().Ejecutar(new ConsultarTrasladoEntrada(ajeno.Id));
        entorno.Como(RolesDeUsuario.PersonalDeLogistica);
        var logistica = await entorno.Detalle().Ejecutar(new ConsultarTrasladoEntrada(Guid.NewGuid()));
        entorno.Como(RolesDeUsuario.AdministradorCentral);
        var administracion = await entorno.Detalle().Ejecutar(new ConsultarTrasladoEntrada(Guid.NewGuid()));

        Assert.Equal((inexistente.Error!.Categoria, inexistente.Error.Clave), (deOtra.Error!.Categoria, deOtra.Error.Clave));
        Assert.Equal(CategoriaDeError.NoEncontrado, logistica.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoEncontrado, administracion.Error!.Categoria);
        Assert.Equal(2, entorno.Base.Denegaciones.Eventos.Count);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-03")]
    [InlineData(RolesDeUsuario.PersonalDeLogistica, true)]
    [InlineData(RolesDeUsuario.AdministradorCentral, true)]
    [InlineData(RolesDeUsuario.Cliente, false)]
    public async Task LogisticaYAdministracionVenCualquierTraslado_ElClienteNoVeNingunoYElRechazoLlegaAntesDeLeer(string rol, bool ve)
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);
        entorno.Como(rol);

        var resultado = await entorno.Detalle().Ejecutar(new ConsultarTrasladoEntrada(traslado.Id));

        Assert.Equal(ve, resultado.EsExito);
        Assert.Equal(ve ? 1 : 0, entorno.Traslados.Lecturas);
        Assert.Equal(ve ? 0 : 1, entorno.Base.Denegaciones.Eventos.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task SinSesionElDetalleNoLeeNada()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);
        entorno.Actor.Establecer(null);

        var resultado = await entorno.Detalle().Ejecutar(new ConsultarTrasladoEntrada(traslado.Id));

        Assert.Equal(AutorizadorDeTraslados.ClaveNoAutenticado, resultado.Error!.Clave);
        Assert.Equal(0, entorno.Traslados.Lecturas);
    }

    // ---------- Lista ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task LaListaDelEncargadoSeFiltraASuSucursalComoOrigenODestino()
    {
        var (entorno, edicion) = Preparar();
        var ida = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 1);
        var vuelta = entorno.Reposicion(edicion, entorno.Ayacucho, entorno.Lima, 1);
        entorno.Reposicion(edicion, entorno.Cusco, entorno.Lima, 1);
        entorno.ComoEncargadoDe(entorno.Ayacucho);

        var pagina = (await entorno.Lista().Ejecutar(Lista())).Valor;
        var propia = (await entorno.Lista().Ejecutar(Lista(fuente: "ayacucho"))).Valor;

        Assert.Equal(2, pagina.Total);
        Assert.Equal(new[] { ida.Id, vuelta.Id }.Order().ToArray(), pagina.Elementos.Select(e => e.TrasladoId).Order().ToArray());
        Assert.Equal(pagina.Elementos.Select(e => e.TrasladoId).Order().ToArray(), propia.Elementos.Select(e => e.TrasladoId).Order().ToArray());
        Assert.Empty(entorno.Base.Denegaciones.Eventos);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-03")]
    [InlineData("lima")]
    [InlineData("cusco")]
    [InlineData("inexistente")]
    public async Task ElEncargadoQuePideOtraSucursalRecibeUnAccesoDenegadoConUnUnicoEvento(string codigo)
    {
        var (entorno, edicion) = Preparar();
        entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 1);
        entorno.ComoEncargadoDe(entorno.Ayacucho);

        var resultado = await entorno.Lista().Ejecutar(Lista(fuente: codigo));

        Assert.Equal((CategoriaDeError.Prohibido, AutorizadorDeTraslados.ClaveProhibido), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(["fuera-de-sucursal"], entorno.Base.MotivosDeDenegacion.ToArray());
        Assert.Equal(0, entorno.Traslados.Lecturas);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task LogisticaYAdministracionVenTodosYFiltranPorCualquierFuente_UnCodigoInexistenteDaUnaPaginaVacia()
    {
        var (entorno, edicion) = Preparar();
        entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 1);
        entorno.Reposicion(edicion, entorno.Lima, entorno.Ayacucho, 1);
        entorno.Reposicion(edicion, entorno.Lima, entorno.Cusco, 1);

        var todos = (await entorno.Lista().Ejecutar(Lista())).Valor;
        var deLima = (await entorno.Lista().Ejecutar(Lista(fuente: "lima"))).Valor;
        entorno.Como(RolesDeUsuario.PersonalDeLogistica);
        var logistica = (await entorno.Lista().Ejecutar(Lista())).Valor;
        var vacia = (await entorno.Lista().Ejecutar(Lista(fuente: "inexistente"))).Valor;

        Assert.Equal((3, 2, 3, 0), (todos.Total, deLima.Total, logistica.Total, vacia.Total));
        Assert.Empty(entorno.Base.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task ElClienteYOtrosRolesNoVenLaLista_ConUnEvento()
    {
        var (entorno, _) = Preparar();
        entorno.Como(RolesDeUsuario.Cliente);

        var resultado = await entorno.Lista().Ejecutar(Lista());

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(["fuera-de-rol"], entorno.Base.MotivosDeDenegacion.ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public async Task LosFiltrosPorEstadoVinculadoYDemoraMinima()
    {
        var (entorno, edicion) = Preparar();
        var hace49 = Traslado.VincularAUnPedido(Guid.NewGuid(), edicion, entorno.Cusco, entorno.Ayacucho, 1, Guid.NewGuid(), EstimacionDeLlegada.Calcular(new DateOnly(2026, 11, 14), 2), null, EntornoDeTraslados.Ahora.AddHours(-49));
        var hace47 = Traslado.VincularAUnPedido(Guid.NewGuid(), edicion, entorno.Cusco, entorno.Ayacucho, 1, Guid.NewGuid(), EstimacionDeLlegada.Calcular(new DateOnly(2026, 11, 14), 2), null, EntornoDeTraslados.Ahora.AddHours(-47));
        var reposicionVieja = Traslado.SolicitarReposicion(Guid.NewGuid(), edicion, entorno.Cusco, entorno.Ayacucho, 1, null, Guid.NewGuid(), EntornoDeTraslados.Ahora.AddHours(-60));
        var aprobadoViejo = Traslado.VincularAUnPedido(Guid.NewGuid(), edicion, entorno.Cusco, entorno.Ayacucho, 1, Guid.NewGuid(), EstimacionDeLlegada.Calcular(new DateOnly(2026, 11, 14), 2), null, EntornoDeTraslados.Ahora.AddHours(-70));
        aprobadoViejo.Aprobar(EntornoDeTraslados.Ahora.AddHours(-69));
        foreach (var t in new[] { hace49, hace47, reposicionVieja, aprobadoViejo })
        {
            entorno.Traslados.Sembrar(t);
        }

        var sinAprobar = (await entorno.Lista().Ejecutar(Lista(estado: "solicitado", vinculado: true, demora: 48))).Valor;
        var aprobados = (await entorno.Lista().Ejecutar(Lista(estado: "aprobado"))).Valor;
        var soloVinculados = (await entorno.Lista().Ejecutar(Lista(vinculado: true))).Valor;
        var reposiciones = (await entorno.Lista().Ejecutar(Lista(vinculado: false))).Valor;

        Assert.Equal([hace49.Id], sinAprobar.Elementos.Select(e => e.TrasladoId).ToArray());
        Assert.Equal([aprobadoViejo.Id], aprobados.Elementos.Select(e => e.TrasladoId).ToArray());
        Assert.Equal((3, 1), (soloVinculados.Total, reposiciones.Total));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public async Task LaListaVaDelMasRecienteAlMasAntiguo_ConPaginacion()
    {
        var (entorno, edicion) = Preparar();
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var t = Traslado.SolicitarReposicion(Guid.NewGuid(), edicion, entorno.Cusco, entorno.Ayacucho, 1, null, Guid.NewGuid(), EntornoDeTraslados.Ahora.AddHours(-i));
            entorno.Traslados.Sembrar(t);
            ids.Add(t.Id);
        }

        var primera = (await entorno.Lista().Ejecutar(Lista(pagina: 1, tamano: 2))).Valor;
        var tercera = (await entorno.Lista().Ejecutar(Lista(pagina: 3, tamano: 2))).Valor;

        Assert.Equal(5, primera.Total);
        Assert.Equal([ids[0], ids[1]], primera.Elementos.Select(e => e.TrasladoId).ToArray());
        Assert.Equal([ids[4]], tercera.Elementos.Select(e => e.TrasladoId).ToArray());
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-20")]
    [InlineData("perdido", null, 1, 20, "estado")]
    [InlineData(null, "CUSCO", 1, 20, "codigoDeFuente")]
    [InlineData(null, null, 0, 20, "pagina")]
    [InlineData(null, null, 1, 0, "tamano")]
    [InlineData(null, null, 1, 101, "tamano")]
    public async Task LaListaValidaSusFiltrosAntesDeLeer(string? estado, string? fuente, int pagina, int tamano, string campo)
    {
        var (entorno, _) = Preparar();

        var resultado = await entorno.Lista().Ejecutar(Lista(estado, fuente, pagina: pagina, tamano: tamano));

        Assert.Equal((CategoriaDeError.Validacion, campo), (resultado.Error!.Categoria, resultado.Error.Errores.Single().Campo));
        Assert.Equal(0, entorno.Traslados.Lecturas);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public async Task LaDemoraMinimaTieneRango()
    {
        var (entorno, _) = Preparar();

        var cero = await entorno.Lista().Ejecutar(Lista(demora: 0));
        var enorme = await entorno.Lista().Ejecutar(Lista(demora: 1_000_000));

        Assert.All([cero.Error!, enorme.Error!], e => Assert.Equal("demoraMinimaHoras", e.Errores.Single().Campo));
    }

    // ---------- Los de un pedido ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public async Task LosTrasladosDeUnPedidoSeDevuelvenConSusFechas_YUnPedidoSinTrasladosDaUnaListaVacia()
    {
        var (entorno, edicion) = Preparar();
        var pedido = Guid.NewGuid();
        var uno = entorno.Vinculado(edicion, entorno.Cusco, entorno.Ayacucho, 1, pedido);
        var dos = entorno.Vinculado(entorno.Impresa(), entorno.Lima, entorno.Ayacucho, 2, pedido, EstadoDeTraslado.Aprobado);
        entorno.Vinculado(edicion, entorno.Cusco, entorno.Ayacucho, 1, Guid.NewGuid());

        var delPedido = (await entorno.DelPedido().Ejecutar(new ConsultarTrasladosDeUnPedidoEntrada(pedido))).Valor;
        var sinTraslados = (await entorno.DelPedido().Ejecutar(new ConsultarTrasladosDeUnPedidoEntrada(Guid.NewGuid()))).Valor;

        Assert.Equal(new[] { uno.Id, dos.Id }.Order().ToArray(), delPedido.Traslados.Select(t => t.TrasladoId).Order().ToArray());
        Assert.All(delPedido.Traslados, t => Assert.Equal((pedido, new DateOnly(2026, 11, 16)), (t.PedidoId, t.FechaEstimadaDeLlegada)));
        Assert.Empty(sinTraslados.Traslados);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task ElEncargadoSoloRecibeLosTrasladosDelPedidoDeSuSucursal_ClienteYOtrosRolesNada()
    {
        var (entorno, edicion) = Preparar();
        var pedido = Guid.NewGuid();
        var propio = entorno.Vinculado(edicion, entorno.Cusco, entorno.Ayacucho, 1, pedido);
        entorno.Vinculado(entorno.Impresa(), entorno.Lima, entorno.Cusco, 1, pedido);

        entorno.ComoEncargadoDe(entorno.Ayacucho);
        var delEncargado = (await entorno.DelPedido().Ejecutar(new ConsultarTrasladosDeUnPedidoEntrada(pedido))).Valor;
        entorno.Como(RolesDeUsuario.Cliente);
        var delCliente = await entorno.DelPedido().Ejecutar(new ConsultarTrasladosDeUnPedidoEntrada(pedido));

        Assert.Equal([propio.Id], delEncargado.Traslados.Select(t => t.TrasladoId).ToArray());
        Assert.Equal(CategoriaDeError.Prohibido, delCliente.Error!.Categoria);
        Assert.Equal(["fuera-de-rol"], entorno.Base.MotivosDeDenegacion.ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public async Task ElPedidoVacioSeRechazaComoValidacion()
    {
        var (entorno, _) = Preparar();

        var resultado = await entorno.DelPedido().Ejecutar(new ConsultarTrasladosDeUnPedidoEntrada(Guid.Empty));

        Assert.Equal((CategoriaDeError.Validacion, "pedidoId"), (resultado.Error!.Categoria, resultado.Error.Errores.Single().Campo));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task LasConsultasNoAuditanNiConsumenPresupuestoNiTocanElBuzon()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);

        await entorno.Detalle().Ejecutar(new ConsultarTrasladoEntrada(traslado.Id));
        await entorno.Lista().Ejecutar(Lista());
        await entorno.DelPedido().Ejecutar(new ConsultarTrasladosDeUnPedidoEntrada(Guid.NewGuid()));

        Assert.Empty(entorno.Base.Auditoria.Solicitudes);
        Assert.Empty(entorno.Limites.Reservas);
        Assert.Empty(entorno.Buzon.Mensajes);
        Assert.Equal(0, entorno.Traslados.LecturasBloqueadas);
    }
}
