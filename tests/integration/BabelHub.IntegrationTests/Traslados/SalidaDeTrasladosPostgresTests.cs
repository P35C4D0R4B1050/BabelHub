using BabelHub.IntegrationTests.Reservas;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Infrastructure.Composicion;
using Microsoft.Extensions.DependencyInjection;
using static BabelHub.IntegrationTests.Traslados.EscenarioDeTraslados;

namespace BabelHub.IntegrationTests.Traslados;

/// <summary>Un E-commerce simulado: recuerda cada aviso y cada estado de pedido que recibe y responde lo que se le indica. Seguro entre hilos.</summary>
internal sealed class EcommerceSimulado : IEnvioAlEcommerce
{
    private readonly object _candado = new();

    public List<AvisoDeTrasladoParaEnviar> Avisos { get; } = [];

    public List<EstadoDeTrasladoDePedidoParaEnviar> Estados { get; } = [];

    public ResultadoDeEnvio Respuesta { get; set; } = ResultadoDeEnvio.Entregado;

    public int Llamadas
    {
        get
        {
            lock (_candado)
            {
                return Avisos.Count + Estados.Count;
            }
        }
    }

    public Task<ResultadoDeEnvio> EnviarAviso(AvisoDeTrasladoParaEnviar aviso, CancellationToken cancelacion = default)
    {
        lock (_candado)
        {
            Avisos.Add(aviso);
        }

        return Task.FromResult(Respuesta);
    }

    public Task<ResultadoDeEnvio> InformarEstadoDelPedido(EstadoDeTrasladoDePedidoParaEnviar estado, CancellationToken cancelacion = default)
    {
        lock (_candado)
        {
            Estados.Add(estado);
        }

        return Task.FromResult(Respuesta);
    }
}

/// <summary>
/// T22: la salida de <c>Traslados</c> con PostgreSQL real y un E-commerce simulado: la entrega del buzón (avisos a origen y destino, estado del pedido, E-commerce caído con el cambio aplicado y el mensaje que se entrega después,
/// errores permanentes, orden por traslado y dos trabajadores a la vez), la alerta de 48 horas con reloj controlado (a las 49 horas sí, a las 47 no, una sola vez) y la depuración de los 2 años. La parte HTTP del adaptador es de T24.
/// </summary>
public sealed class SalidaDeTrasladosPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly ActorActual Sistema = ActorActual.DelSistema();

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<EscenarioDeTraslados> Preparar(PostgresDeReservasFixture bd, EcommerceSimulado ecommerce, int stockDeCusco = 10)
    {
        var escenario = new EscenarioDeTraslados(bd);
        await escenario.Preparar(stockDeCusco: stockDeCusco, pila: s => s.AddSingleton<IEnvioAlEcommerce>(ecommerce));
        return escenario;
    }

    private static Task<Resultado<ResultadoDeLaEntrega>> Entregar(EscenarioDeTraslados e, int maximo = 50) =>
        e.Pila.Ejecutar<EntregarMensajesSalientesEntrada, ResultadoDeLaEntrega>(Sistema, new EntregarMensajesSalientesEntrada(maximo));

    private static Task<Resultado<ResultadoDeLaRevision>> Revisar(EscenarioDeTraslados e) =>
        e.Pila.Ejecutar<RevisarTrasladosSinAprobarEntrada, ResultadoDeLaRevision>(Sistema, new RevisarTrasladosSinAprobarEntrada());

    private static void Avanzar(EscenarioDeTraslados e, TimeSpan tiempo) => e.Pila.Reloj.Ahora += tiempo;

    private Task<string> Estados(Guid trasladoId) =>
        bd.Consultar<string>($"SELECT string_agg(tipo || ':' || sucursal_codigo || ':' || estado, ',' ORDER BY tipo, sucursal_codigo) FROM traslados.mensajes_salientes WHERE clave_de_evento LIKE 'traslado:{trasladoId:D}:%'")!;

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task UnCambioDeEstadoAvisaALaSucursalDeOrigenYALaDeDestino_ConLosCamposTipados()
    {
        var ecommerce = new EcommerceSimulado();
        using var escenario = await Preparar(bd, ecommerce);
        var aprobado = await escenario.ReposicionAprobada(3);

        var entrega = await Entregar(escenario);

        Assert.True(entrega.EsExito, entrega.Error?.Clave);
        Assert.Equal(4, entrega.Valor.Entregados);
        var delAprobado = ecommerce.Avisos.Where(a => a.EventoId == $"traslado:{aprobado.TrasladoId:D}:aprobado").ToList();
        Assert.Equal(["ayacucho", "cusco"], delAprobado.Select(a => a.CodigoDeSucursal).Order().ToArray());
        Assert.Equal(("aprobado", 3), (delAprobado[0].Estado, delAprobado[0].Cantidad));
        Assert.Equal(["destino", "origen"], delAprobado.Select(a => a.RolDeLaSucursal).Order().ToArray());
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes WHERE estado <> 'enviado'"));
        Assert.Empty(ecommerce.Estados);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-09")]
    public async Task LaRecepcionDeUnVinculadoInformaAlEcommerceQueElPedidoPuedePrepararse_YAvisaAlDestino()
    {
        var ecommerce = new EcommerceSimulado();
        using var escenario = await Preparar(bd, ecommerce, stockDeCusco: 1);
        var (pedido, traslado) = await escenario.VinculadoAprobado();
        await escenario.Ejecutar(Administracion(), new DespacharTrasladoEntrada(traslado));
        await escenario.Ejecutar(Encargado(escenario.Lima), new RecibirTrasladoEntrada(traslado));

        await Entregar(escenario);
        Avanzar(escenario, TimeSpan.FromSeconds(1));
        await Entregar(escenario);

        Assert.Equal(["en-transito", "recibido"], ecommerce.Estados.Select(e => e.Estado).ToArray());
        var recibido = ecommerce.Estados.Single(e => e.Estado == "recibido");
        Assert.Equal(($"traslado:{traslado:D}:recibido", pedido, traslado, escenario.Libro, 1), (recibido.EventoId, recibido.PedidoId, recibido.TrasladoId, recibido.EdicionId, recibido.Cantidad));
        Assert.NotNull(recibido.FechaEstimadaDeLlegada);
        Assert.Contains(ecommerce.Avisos, a => a.EventoId == $"traslado:{traslado:D}:recibido" && a.CodigoDeSucursal == "lima" && a.RolDeLaSucursal == "destino");
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-09")]
    public async Task UnPedidoAnuladoEnTransitoNoGeneraElMensajeDeRecibido_ElDeCanceladoSi()
    {
        var ecommerce = new EcommerceSimulado();
        using var escenario = await Preparar(bd, ecommerce, stockDeCusco: 1);
        var (pedido, traslado) = await escenario.VinculadoAprobado();
        await escenario.Ejecutar(Administracion(), new DespacharTrasladoEntrada(traslado));
        await escenario.AnularElPedido(pedido);
        await escenario.Ejecutar(Encargado(escenario.Lima), new RecibirTrasladoEntrada(traslado));

        await Entregar(escenario);

        Assert.DoesNotContain(ecommerce.Estados, e => e.Estado == "recibido");
        Assert.Equal(["en-transito"], ecommerce.Estados.Select(e => e.Estado).ToArray());
        Assert.Contains(ecommerce.Avisos, a => a.Estado == "recibido");
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task ConElEcommerceCaido_ElCambioQuedaAplicadoYElMensajeSeEntregaDespues()
    {
        var ecommerce = new EcommerceSimulado { Respuesta = ResultadoDeEnvio.Reintentable };
        using var escenario = await Preparar(bd, ecommerce);
        var aprobado = await escenario.ReposicionAprobada(3);

        var caido = await Entregar(escenario);

        Assert.Equal("aprobado", await escenario.EstadoDe(aprobado.TrasladoId));
        Assert.Equal((0, 4), (caido.Valor.Entregados, caido.Valor.Reprogramados));
        Assert.Equal(4L, await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes WHERE estado = 'pendiente' AND intentos = 1"));

        var antesDeTiempo = await Entregar(escenario);
        ecommerce.Respuesta = ResultadoDeEnvio.Entregado;
        Avanzar(escenario, TimeSpan.FromMinutes(2));
        var recuperado = await Entregar(escenario);

        Assert.Equal(0, antesDeTiempo.Valor.Reclamados);
        Assert.Equal((4, 4), (recuperado.Valor.Reclamados, recuperado.Valor.Entregados));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes WHERE estado <> 'enviado'"));
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-08")]
    [InlineData("permanente")]
    [InlineData("credencial")]
    public async Task UnErrorPermanenteOUnaCredencialRechazadaDejanFallidoConAlerta_SinReintentarLuego(string motivo)
    {
        var ecommerce = new EcommerceSimulado { Respuesta = motivo == "permanente" ? ResultadoDeEnvio.Permanente : ResultadoDeEnvio.CredencialRechazada };
        using var escenario = await Preparar(bd, ecommerce);
        await escenario.ReposicionAprobada(3);

        await Entregar(escenario);
        Avanzar(escenario, TimeSpan.FromHours(3));
        var despues = await Entregar(escenario);

        Assert.Equal(4L, await bd.Consultar<long>($"SELECT count(*) FROM traslados.mensajes_salientes WHERE estado = 'fallido' AND motivo_de_fallo = '{motivo}'"));
        Assert.Equal(0, despues.Valor.Reclamados);
        Assert.Equal(4, escenario.Pila.AlertasPublicadas.Tecnicas.Count(t => t == "dependencia-no-necesaria-fallida"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task UnAvisoDeMasDe24HorasQuedaFallidoConAlerta_ElEstadoDelPedidoAvisaUnaVezYSigue()
    {
        var ecommerce = new EcommerceSimulado { Respuesta = ResultadoDeEnvio.Reintentable };
        using var escenario = await Preparar(bd, ecommerce, stockDeCusco: 1);
        var (_, traslado) = await escenario.VinculadoAprobado();
        await escenario.Ejecutar(Administracion(), new DespacharTrasladoEntrada(traslado));

        Avanzar(escenario, TimeSpan.FromHours(25));
        await Entregar(escenario);
        var tras = escenario.Pila.AlertasPublicadas.Tecnicas.Count;
        Avanzar(escenario, TimeSpan.FromHours(2));
        await Entregar(escenario);

        Assert.Equal("vencido", await bd.Consultar<string>("SELECT DISTINCT motivo_de_fallo FROM traslados.mensajes_salientes WHERE tipo = 'aviso' AND estado = 'fallido'"));
        Assert.Equal(("pendiente", true), (await bd.Consultar<string>("SELECT estado FROM traslados.mensajes_salientes WHERE tipo = 'estado-de-pedido' AND clave_de_evento LIKE '%en-transito'") ?? string.Empty, await bd.Consultar<bool>("SELECT alertado_en IS NOT NULL FROM traslados.mensajes_salientes WHERE tipo = 'estado-de-pedido' AND clave_de_evento LIKE '%en-transito'")));
        Assert.Equal(tras, escenario.Pila.AlertasPublicadas.Tecnicas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-09")]
    public async Task LosEstadosDeUnTrasladoSeEntreganEnOrden_ElRecibidoEsperaAlEnTransito()
    {
        var ecommerce = new EcommerceSimulado { Respuesta = ResultadoDeEnvio.Reintentable };
        using var escenario = await Preparar(bd, ecommerce, stockDeCusco: 1);
        var (_, traslado) = await escenario.VinculadoAprobado();
        await escenario.Ejecutar(Administracion(), new DespacharTrasladoEntrada(traslado));
        await escenario.Ejecutar(Encargado(escenario.Lima), new RecibirTrasladoEntrada(traslado));

        await Entregar(escenario);
        Assert.Equal(["en-transito"], ecommerce.Estados.Select(e => e.Estado).ToArray());

        ecommerce.Respuesta = ResultadoDeEnvio.Entregado;
        Avanzar(escenario, TimeSpan.FromMinutes(2));
        await Entregar(escenario);
        Assert.Equal(["en-transito", "en-transito"], ecommerce.Estados.Select(e => e.Estado).ToArray());

        await Entregar(escenario);
        Assert.Equal(["en-transito", "en-transito", "recibido"], ecommerce.Estados.Select(e => e.Estado).ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task DosTrabajadoresALaVezNuncaEntreganElMismoMensajeDosVeces_SkipLocked()
    {
        var ecommerce = new EcommerceSimulado();
        using var escenario = await Preparar(bd, ecommerce);
        for (var i = 0; i < 6; i++)
        {
            await escenario.SolicitarComoAdministracion(1);
        }

        var resultados = await Simultaneas(() => Entregar(escenario, 100), () => Entregar(escenario, 100), () => Entregar(escenario, 100));

        Assert.All(resultados, r => Assert.True(r.EsExito, r.Error?.Clave));
        Assert.Equal(12, resultados.Sum(r => r.Valor.Entregados));
        Assert.Equal(12, ecommerce.Avisos.Count);
        Assert.Equal(12, ecommerce.Avisos.Select(a => (a.EventoId, a.CodigoDeSucursal)).Distinct().Count());
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes WHERE estado <> 'enviado'"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task SoloElActorSistemaEntrega_UnEncargadoNoToca()
    {
        var ecommerce = new EcommerceSimulado();
        using var escenario = await Preparar(bd, ecommerce);
        await escenario.ReposicionAprobada(3);

        var comoEncargado = await escenario.Pila.Ejecutar<EntregarMensajesSalientesEntrada, ResultadoDeLaEntrega>(Encargado(escenario.Cusco), new EntregarMensajesSalientesEntrada());

        Assert.Equal(CategoriaDeError.Prohibido, comoEncargado.Error!.Categoria);
        Assert.Equal(0, ecommerce.Llamadas);
    }

    // ---------- La alerta de 48 horas ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-21")]
    public async Task LaAlertaDeLas48HorasSalePorUnVinculadoDe49HorasYNoPorUnoDe47_YUnaSolaVezPorTraslado()
    {
        var ecommerce = new EcommerceSimulado();
        using var escenario = await Preparar(bd, ecommerce, stockDeCusco: 2);
        var (_, _, traslado) = await escenario.PedidoConTraslado();

        Avanzar(escenario, TimeSpan.FromHours(47));
        var a47Horas = await Revisar(escenario);
        Avanzar(escenario, TimeSpan.FromHours(2));
        var a49Horas = await Revisar(escenario);
        var repetida = await Revisar(escenario);

        Assert.Equal(0, a47Horas.Valor.Avisados);
        Assert.Equal(1, a49Horas.Valor.Avisados);
        Assert.Equal(0, repetida.Valor.Avisados);
        Assert.Equal(1, escenario.Pila.AlertasPublicadas.Tecnicas.Count(t => t == "traslado-vinculado-sin-aprobar"));
        Assert.True(await bd.Consultar<bool>($"SELECT alerta_de_demora_en IS NOT NULL FROM traslados.traslados WHERE id = '{traslado}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-21")]
    public async Task UnVinculadoAprobadoAntesDeLaRevisionNoSeAvisa_NiUnaReposicion()
    {
        var ecommerce = new EcommerceSimulado();
        using var escenario = await Preparar(bd, ecommerce, stockDeCusco: 2);
        var (_, traslado) = await escenario.VinculadoAprobado();
        await escenario.SolicitarComoAdministracion(1);
        Avanzar(escenario, TimeSpan.FromHours(60));

        var resultado = await Revisar(escenario);

        Assert.Equal(0, resultado.Valor.Avisados);
        Assert.Empty(escenario.Pila.AlertasPublicadas.Tecnicas);
        Assert.Equal("aprobado", await escenario.EstadoDe(traslado));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-21")]
    public async Task DosInstanciasDeLaRevisionNoAvisanElMismoTrasladoDosVeces()
    {
        var ecommerce = new EcommerceSimulado();
        using var escenario = await Preparar(bd, ecommerce, stockDeCusco: 4);
        for (var i = 0; i < 3; i++)
        {
            await escenario.PedidoConTraslado();
        }

        Avanzar(escenario, TimeSpan.FromHours(60));
        var resultados = await Simultaneas(() => Revisar(escenario), () => Revisar(escenario), () => Revisar(escenario));

        Assert.Equal(3, resultados.Sum(r => r.Valor.Avisados));
        Assert.Equal(3, escenario.Pila.AlertasPublicadas.Tecnicas.Count(t => t == "traslado-vinculado-sin-aprobar"));
    }

    // ---------- Depuración y trabajos de fondo ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-21")]
    public async Task ALos2AnosSeAnonimizaElSolicitanteYLosTextos_ConElRelojControlado_ConservandoElMovimiento()
    {
        var ecommerce = new EcommerceSimulado();
        using var escenario = await Preparar(bd, ecommerce);
        var traslado = await escenario.SolicitarComoAdministracion(2, motivo: "MOTIVO-VIEJO");
        await escenario.Ejecutar(Administracion(), new AprobarTrasladoEntrada(traslado.TrasladoId));
        await escenario.Ejecutar(Logistica(), new RegistrarGuiaDeTrasladoEntrada(traslado.TrasladoId, "CA-771204"));
        await escenario.Ejecutar(Administracion(), new DespacharTrasladoEntrada(traslado.TrasladoId));
        await escenario.Ejecutar(Administracion(), new RecibirTrasladoEntrada(traslado.TrasladoId));
        var cantidad = await bd.Consultar<int>($"SELECT cantidad FROM traslados.traslados WHERE id = '{traslado.TrasladoId}'");

        Avanzar(escenario, TimeSpan.FromDays(365));
        var antes = await escenario.Pila.Ejecutar<DepurarTrasladosEntrada, ResultadoDeLaDepuracionDeTraslados>(Sistema, new DepurarTrasladosEntrada());
        Avanzar(escenario, TimeSpan.FromDays(368));
        var despues = await escenario.Pila.Ejecutar<DepurarTrasladosEntrada, ResultadoDeLaDepuracionDeTraslados>(Sistema, new DepurarTrasladosEntrada());
        var repetida = await escenario.Pila.Ejecutar<DepurarTrasladosEntrada, ResultadoDeLaDepuracionDeTraslados>(Sistema, new DepurarTrasladosEntrada());

        Assert.Equal(0, antes.Valor.Anonimizados);
        Assert.Equal((1, 0), (despues.Valor.Anonimizados, repetida.Valor.Anonimizados));
        Assert.Equal("NULL|NULL|NULL|NULL|recibido", await bd.Consultar<string>($"SELECT coalesce(solicitado_por::text, 'NULL') || '|' || coalesce(motivo, 'NULL') || '|' || coalesce(motivo_de_cierre, 'NULL') || '|' || coalesce(guia, 'NULL') || '|' || estado FROM traslados.traslados WHERE id = '{traslado.TrasladoId}'"));
        Assert.Equal(cantidad, await bd.Consultar<int>($"SELECT cantidad FROM traslados.traslados WHERE id = '{traslado.TrasladoId}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-21")]
    public async Task ALos30DiasSePurganLosMensajesEnviadosOFallidos_NuncaLosPendientes()
    {
        var ecommerce = new EcommerceSimulado();
        using var escenario = await Preparar(bd, ecommerce);
        await escenario.ReposicionAprobada(3);
        await Entregar(escenario);
        ecommerce.Respuesta = ResultadoDeEnvio.Reintentable;
        var pendiente = await escenario.SolicitarComoAdministracion(1);
        await Entregar(escenario);
        Avanzar(escenario, TimeSpan.FromDays(31));

        var depuracion = await escenario.Pila.Ejecutar<DepurarTrasladosEntrada, ResultadoDeLaDepuracionDeTraslados>(Sistema, new DepurarTrasladosEntrada());

        Assert.Equal(4, depuracion.Valor.MensajesPurgados);
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes WHERE estado = 'pendiente'"));
        Assert.Equal(2L, await bd.Consultar<long>($"SELECT count(*) FROM traslados.mensajes_salientes WHERE clave_de_evento LIKE 'traslado:{pendiente.TrasladoId:D}:%'"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task LosTrabajosDeFondoCorrenComoSistemaConSuRegistro_YUnaSegundaInstanciaOmiteLaCorrida()
    {
        var ecommerce = new EcommerceSimulado();
        using var escenario = await Preparar(bd, ecommerce);
        await escenario.ReposicionAprobada(3);

        var entrega = await TrabajosDeTraslados.Entregar(escenario.Pila.Proveedor).EjecutarUnaVez();
        var revision = await TrabajosDeTraslados.Revisar(escenario.Pila.Proveedor).EjecutarUnaVez();
        var depuracion = await TrabajosDeTraslados.Depurar(escenario.Pila.Proveedor).EjecutarUnaVez();

        Assert.All(new[] { entrega, revision, depuracion }, r => Assert.Equal(ResultadoDeCorrida.Exitosa, r));
        Assert.Equal(4, ecommerce.Avisos.Count);
        Assert.Equal(
            [TrabajosDeTraslados.EntregaDeMensajes, TrabajosDeTraslados.RevisionDePlazos, TrabajosDeTraslados.DepuracionDiaria],
            escenario.Pila.Registro.Corridas.Select(c => c.Trabajo).ToArray());

        var trabajo = TrabajosDeTraslados.Entregar(escenario.Pila.Proveedor);
        await using var bloqueo = await escenario.Pila.Proveedor.GetRequiredService<BloqueoDeInstanciaUnica>().Intentar(TrabajosDeTraslados.EntregaDeMensajes);
        Assert.Equal(ResultadoDeCorrida.OmitidaPorOtraInstancia, await trabajo.EjecutarUnaVez());
    }
}
