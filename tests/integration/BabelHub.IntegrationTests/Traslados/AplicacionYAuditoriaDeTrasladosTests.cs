using BabelHub.IntegrationTests.Reservas;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;
using static BabelHub.IntegrationTests.Traslados.EscenarioDeTraslados;

namespace BabelHub.IntegrationTests.Traslados;

/// <summary>
/// T17: la Aplicación de <c>Traslados</c> con PostgreSQL real y el rol real de la aplicación: la tabla rol × operación con negativos (un 403 y un único evento, también en lectura y en orígenes posibles), el rechazo de un
/// vinculado como 422 sin evento, la cancelación por el origen y no por el destino, el despacho sin stock o sin reserva, la guía sin cambiar el estado, una entrada de auditoría por cada cambio, los textos libres ausentes del
/// buzón y de los avisos, el intento 31 de <c>traslados-del-personal</c> y el cambio no aplicado con la auditoría caída. La parte HTTP corre en T24.
/// </summary>
public sealed class AplicacionYAuditoriaDeTrasladosTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private const string MotivoCentinela = "MOTIVO-CENTINELA-9X";

    private const string GuiaCentinela = "GUIA-CENTINELA-9X";

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Operacion(string Nombre, Func<EscenarioDeTraslados, Guid, Guid, ActorActual, Task<ErrorDeAplicacion?>> Ejecutar, bool LogisticaPuede = false, bool EncargadoAjenoPuede = false);

    private static Task<ErrorDeAplicacion?> Fallo<T>(Task<Resultado<T>> tarea) => tarea.ContinueWith(t => t.Result.Error);

    private static IReadOnlyList<Operacion> Operaciones(string guia) =>
    [
        new("solicitar", (e, solicitado, _, actor) => Fallo(e.Ejecutar(actor, new SolicitarTrasladoEntrada(e.Libro, 1, "cusco", "ayacucho", "x")))),
        new("origenes-posibles", (e, _, _, actor) => Fallo(e.Consultar<ConsultarOrigenesPosiblesEntrada, OrigenesPosibles>(actor, new ConsultarOrigenesPosiblesEntrada(e.Libro, "ayacucho")))),
        new("aprobar", (e, solicitado, _, actor) => Fallo(e.Ejecutar(actor, new AprobarTrasladoEntrada(solicitado)))),
        new("rechazar", (e, solicitado, _, actor) => Fallo(e.Ejecutar(actor, new RechazarTrasladoEntrada(solicitado, "sin stock")))),
        new("cancelar", (e, solicitado, _, actor) => Fallo(e.Ejecutar(actor, new CancelarTrasladoEntrada(solicitado, "error")))),
        new("despachar", (e, _, aprobado, actor) => Fallo(e.Ejecutar(actor, new DespacharTrasladoEntrada(aprobado)))),
        new("recibir", (e, _, aprobado, actor) => Fallo(e.Ejecutar(actor, new RecibirTrasladoEntrada(aprobado)))),
        new("guia", (e, _, aprobado, actor) => Fallo(e.Ejecutar(actor, new RegistrarGuiaDeTrasladoEntrada(aprobado, guia))), LogisticaPuede: true),
        new("detalle", (e, solicitado, _, actor) => Fallo(e.Consultar<ConsultarTrasladoEntrada, TrasladoRegistrado>(actor, new ConsultarTrasladoEntrada(solicitado))), LogisticaPuede: true),
        new("lista", (e, _, _, actor) => Fallo(e.Consultar<ListarTrasladosEntrada, PaginaDeTraslados>(actor, new ListarTrasladosEntrada(null, "cusco", null, null, 1, 20))), LogisticaPuede: true),
        new("de-un-pedido", (e, _, _, actor) => Fallo(e.Consultar<ConsultarTrasladosDeUnPedidoEntrada, TrasladosDeUnPedido>(actor, new ConsultarTrasladosDeUnPedidoEntrada(Guid.NewGuid()))), LogisticaPuede: true, EncargadoAjenoPuede: true),
    ];

    private async Task<(EscenarioDeTraslados Escenario, Guid Solicitado, Guid Aprobado)> Preparar(int stockDeCusco = 10)
    {
        var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeCusco: stockDeCusco);
        var solicitado = await escenario.SolicitarComoAdministracion(2);
        var aprobado = await escenario.ReposicionAprobada(2);
        return (escenario, solicitado.TrasladoId, aprobado.TrasladoId);
    }

    // ---------- La tabla rol × operación con negativos ----------

    [Theory]
    [Trait("Criterio", "AC-TRA-03")]
    [InlineData("logistica")]
    [InlineData("cliente")]
    [InlineData("encargado-ajeno")]
    public async Task CadaOperacionNegadaDevuelveUn403ConUnUnicoEvento_ElEstadoNoCambia(string quien)
    {
        var (escenario, solicitado, aprobado) = await Preparar();
        using var cierre = escenario;
        ActorActual Actor() => quien switch
        {
            "logistica" => Logistica(),
            "cliente" => PilaDeReservas.Cliente(),
            _ => Encargado(escenario.Lima),
        };
        var versiones = (await escenario.VersionDe(solicitado), await escenario.VersionDe(aprobado));
        var auditoriaAntes = escenario.Pila.Auditoria.Registros.Count;

        foreach (var operacion in Operaciones("CA-771204"))
        {
            if ((quien == "logistica" && operacion.LogisticaPuede) || (quien == "encargado-ajeno" && operacion.EncargadoAjenoPuede))
            {
                continue;
            }

            var eventosAntes = escenario.Pila.Denegaciones.Motivos.Count;
            var error = await operacion.Ejecutar(escenario, solicitado, aprobado, Actor());

            Assert.True(error is { Categoria: CategoriaDeError.Prohibido }, $"{operacion.Nombre} con {quien}: {error?.Clave ?? "se permitió"}");
            Assert.Equal(eventosAntes + 1, escenario.Pila.Denegaciones.Motivos.Count);
        }

        Assert.Equal(versiones, (await escenario.VersionDe(solicitado), await escenario.VersionDe(aprobado)));
        Assert.Equal(auditoriaAntes, escenario.Pila.Auditoria.Registros.Count);
        Assert.Null(await bd.Consultar<string>($"SELECT guia FROM traslados.traslados WHERE id = '{aprobado}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task ElEncargadoDelOrigenYDelDestinoHacenLoSuyo_LaLogisticaRegistraLaGuiaYConsulta()
    {
        var (escenario, solicitado, aprobado) = await Preparar();
        using var cierre = escenario;

        var aprueba = await escenario.Ejecutar(Encargado(escenario.Cusco), new AprobarTrasladoEntrada(solicitado));
        var despacha = await escenario.Ejecutar(Encargado(escenario.Cusco), new DespacharTrasladoEntrada(aprobado));
        var guia = await escenario.Ejecutar(Logistica(), new RegistrarGuiaDeTrasladoEntrada(aprobado, "CA-771204"));
        var recibe = await escenario.Ejecutar(Encargado(escenario.Ayacucho), new RecibirTrasladoEntrada(aprobado));
        var detalle = await escenario.Consultar<ConsultarTrasladoEntrada, TrasladoRegistrado>(Logistica(), new ConsultarTrasladoEntrada(aprobado));
        var deAyacucho = await escenario.Consultar<ListarTrasladosEntrada, PaginaDeTraslados>(Encargado(escenario.Ayacucho), new ListarTrasladosEntrada(null, null, null, null, 1, 20));

        Assert.All(new[] { aprueba, despacha, guia, recibe }, r => Assert.True(r.EsExito, r.Error?.Clave));
        Assert.Equal(("recibido", "CA-771204"), (detalle.Valor.Estado, detalle.Valor.Guia));
        Assert.Equal(2, deAyacucho.Valor.Total);
        Assert.Empty(escenario.Pila.Denegaciones.Motivos);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task UnIdAjenoEInexistenteSonElMismoRechazoParaElEncargado_ElEncargadoNoVeLosTrasladosDeOtraSucursalEnLaLista()
    {
        var (escenario, solicitado, _) = await Preparar();
        using var cierre = escenario;

        var ajeno = await escenario.Consultar<ConsultarTrasladoEntrada, TrasladoRegistrado>(Encargado(escenario.Lima), new ConsultarTrasladoEntrada(solicitado));
        var inexistente = await escenario.Consultar<ConsultarTrasladoEntrada, TrasladoRegistrado>(Encargado(escenario.Lima), new ConsultarTrasladoEntrada(Guid.NewGuid()));
        var lista = await escenario.Consultar<ListarTrasladosEntrada, PaginaDeTraslados>(Encargado(escenario.Lima), new ListarTrasladosEntrada(null, null, null, null, 1, 20));
        var otraSucursal = await escenario.Consultar<ListarTrasladosEntrada, PaginaDeTraslados>(Encargado(escenario.Lima), new ListarTrasladosEntrada(null, "cusco", null, null, 1, 20));

        Assert.Equal((ajeno.Error!.Categoria, ajeno.Error.Clave), (inexistente.Error!.Categoria, inexistente.Error.Clave));
        Assert.Equal(0, lista.Valor.Total);
        Assert.Equal(CategoriaDeError.Prohibido, otraSucursal.Error!.Categoria);
        Assert.Equal(3, escenario.Pila.Denegaciones.Motivos.Count);
    }

    // ---------- Reglas de negocio sin evento de seguridad ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-14")]
    public async Task RechazarOCancelarUnVinculadoEsUn422SinEvento_ConLaReposicionSiSePuede()
    {
        using var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeCusco: 1);
        var (_, _, vinculado) = await escenario.PedidoConTraslado();
        await bd.PonerStock(escenario.Cusco, escenario.Libro, 5, 1);
        var reposicion = await escenario.SolicitarComoAdministracion(1);

        var rechazarComoOrigen = await escenario.Ejecutar(Encargado(escenario.Cusco), new RechazarTrasladoEntrada(vinculado, "sin stock"));
        var rechazarComoAdministracion = await escenario.Ejecutar(Administracion(), new RechazarTrasladoEntrada(vinculado, "sin stock"));
        var cancelarComoAdministracion = await escenario.Ejecutar(Administracion(), new CancelarTrasladoEntrada(vinculado, "error"));
        var rechazarReposicion = await escenario.Ejecutar(Encargado(escenario.Cusco), new RechazarTrasladoEntrada(reposicion.TrasladoId, "sin stock"));

        Assert.All(new[] { rechazarComoOrigen, rechazarComoAdministracion, cancelarComoAdministracion }, r => Assert.Equal((CategoriaDeError.ReglaDeNegocio, ClavesDeTraslados.TrasladoVinculadoNoSeRechaza), (r.Error!.Categoria, r.Error.Clave)));
        Assert.Equal("solicitado", await escenario.EstadoDe(vinculado));
        Assert.True(rechazarReposicion.EsExito);
        Assert.Empty(escenario.Pila.Denegaciones.Motivos);
        Assert.DoesNotContain(escenario.Pila.Auditoria.Registros, r => r.ObjetoId == vinculado.ToString("D") && r.Accion != "TrasladoSolicitado");
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-17")]
    public async Task LaCancelacionLaHaceElOrigenYNoElDestino()
    {
        var (escenario, _, aprobado) = await Preparar();
        using var cierre = escenario;

        var destino = await escenario.Ejecutar(Encargado(escenario.Ayacucho), new CancelarTrasladoEntrada(aprobado, "error de pedido"));
        var origen = await escenario.Ejecutar(Encargado(escenario.Cusco), new CancelarTrasladoEntrada(aprobado, "error de pedido"));
        var despacho = await escenario.Ejecutar(Administracion(), new DespacharTrasladoEntrada(aprobado));

        Assert.Equal(CategoriaDeError.Prohibido, destino.Error!.Categoria);
        Assert.Equal(("cancelado", "error de pedido"), (origen.Valor.Estado, origen.Valor.MotivoDeCierre));
        Assert.Equal(ClavesDeTraslados.TrasladoEstadoInvalido, despacho.Error!.Clave);
        Assert.Equal(["fuera-de-sucursal"], escenario.Pila.Denegaciones.Motivos);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-05")]
    public async Task ElDespachoSinStockVendibleSeRechazaYElTrasladoSigueAprobado()
    {
        var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeCusco: 3);
        using var cierre = escenario;
        var traslado = await escenario.ReposicionAprobada(3);
        await bd.PonerStock(escenario.Cusco, escenario.Libro, 3, 3);

        var despacho = await escenario.Ejecutar(Encargado(escenario.Cusco), new DespacharTrasladoEntrada(traslado.TrasladoId));

        Assert.Equal((CategoriaDeError.Conflicto, ClavesDeTraslados.StockInsuficiente), (despacho.Error!.Categoria, despacho.Error.Clave));
        Assert.Equal((3, 3), await bd.StockDe(escenario.Cusco, escenario.Libro));
        Assert.Equal(("aprobado", 1L), (await escenario.EstadoDe(traslado.TrasladoId), await escenario.VersionDe(traslado.TrasladoId)));
        Assert.Empty(escenario.Pila.Denegaciones.Motivos);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-05")]
    public async Task ElDespachoDeUnVinculadoCuyaReservaYaNoEstaConfirmadaSeRechaza_ElTrasladoSigueAprobado()
    {
        var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeCusco: 1);
        using var cierre = escenario;
        var (pedido, traslado) = await escenario.VinculadoAprobado();
        await bd.Ejecutar($"UPDATE reservas.reserva_lineas SET estado = 'liberada' WHERE pedido_id = '{pedido}'");

        var despacho = await escenario.Ejecutar(Encargado(escenario.Cusco), new DespacharTrasladoEntrada(traslado));

        Assert.Equal((CategoriaDeError.Conflicto, ClavesDeTraslados.StockInsuficiente), (despacho.Error!.Categoria, despacho.Error.Clave));
        Assert.Equal("aprobado", await escenario.EstadoDe(traslado));
        Assert.Equal((1, 1), await bd.StockDe(escenario.Cusco, escenario.Libro));
    }

    // ---------- Guía, auditoría y textos libres ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-10")]
    public async Task LaGuiaSeRegistraSinCambiarElEstado_YLaAuditoriaGuardaElValorNuevoYSiReemplazo()
    {
        var (escenario, _, aprobado) = await Preparar();
        using var cierre = escenario;

        var primera = await escenario.Ejecutar(Logistica(), new RegistrarGuiaDeTrasladoEntrada(aprobado, "CA-771204"));
        var segunda = await escenario.Ejecutar(Administracion(), new RegistrarGuiaDeTrasladoEntrada(aprobado, "CA-999999"));

        Assert.Equal(("aprobado", "CA-771204"), (primera.Valor.Estado, primera.Valor.Guia));
        Assert.Equal("CA-999999", segunda.Valor.Guia);
        var registros = escenario.Pila.Auditoria.Registros.Where(r => r.Accion == "GuiaDeTrasladoRegistrada" && r.ObjetoId == aprobado.ToString("D")).ToList();
        Assert.Equal(("CA-771204", "false"), (registros[0].Detalle["guiaNueva"], registros[0].Detalle["reemplazoOtraGuia"]));
        Assert.Equal(("CA-999999", "true"), (registros[1].Detalle["guiaNueva"], registros[1].Detalle["reemplazoOtraGuia"]));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM traslados.mensajes_salientes WHERE clave_de_evento LIKE 'traslado:{aprobado:D}:%' AND carga::text LIKE '%CA-%'"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task CadaCambioDeEstadoDejaUnaEntradaDeAuditoria_ConElObjetoLasSucursalesYElEstado()
    {
        var escenario = await new EscenarioDeTraslados(bd).Preparar();
        using var cierre = escenario;

        var solicitado = await escenario.SolicitarComoAdministracion(2);
        await escenario.Ejecutar(Encargado(escenario.Cusco), new AprobarTrasladoEntrada(solicitado.TrasladoId));
        await escenario.Ejecutar(Encargado(escenario.Cusco), new DespacharTrasladoEntrada(solicitado.TrasladoId));
        await escenario.Ejecutar(Encargado(escenario.Ayacucho), new RecibirTrasladoEntrada(solicitado.TrasladoId));

        var deEste = escenario.Pila.Auditoria.Registros.Where(r => r.ObjetoId == solicitado.TrasladoId.ToString("D")).ToList();
        Assert.Equal(["TrasladoSolicitado", "TrasladoAprobado", "TrasladoDespachado", "TrasladoRecibido"], deEste.Select(r => r.Accion).ToArray());
        Assert.All(deEste, r => Assert.Equal((escenario.Cusco, TipoDeActor.Usuario), (r.Fuente, r.Actor)));
        Assert.Equal(["solicitado", "aprobado", "en-transito"], deEste.Skip(1).Select(r => r.Detalle["estadoAnterior"]!).ToArray());
        Assert.Equal(["aprobado", "en-transito", "recibido"], deEste.Skip(1).Select(r => r.Detalle["estadoNuevo"]!).ToArray());
        Assert.Equal(8L, await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task LosTextosLibresNoLlegaranAlBuzonNiALosAvisosNiALaAuditoria_LaGuiaSoloFiguraEnLaAuditoriaComoValorNuevo()
    {
        var escenario = await new EscenarioDeTraslados(bd).Preparar();
        using var cierre = escenario;

        var solicitado = await escenario.SolicitarComoAdministracion(2, motivo: MotivoCentinela);
        await escenario.Ejecutar(Administracion(), new AprobarTrasladoEntrada(solicitado.TrasladoId));
        await escenario.Ejecutar(Logistica(), new RegistrarGuiaDeTrasladoEntrada(solicitado.TrasladoId, GuiaCentinela));
        var otro = await escenario.SolicitarComoAdministracion(1, motivo: MotivoCentinela);
        await escenario.Ejecutar(Encargado(escenario.Cusco), new RechazarTrasladoEntrada(otro.TrasladoId, MotivoCentinela));

        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes WHERE carga::text LIKE '%CENTINELA%' OR clave_de_evento LIKE '%CENTINELA%'"));
        Assert.DoesNotContain(escenario.Pila.Auditoria.Registros, r => r.Detalle.Values.Any(v => v?.Contains("MOTIVO-CENTINELA", StringComparison.Ordinal) == true));
        var conGuia = escenario.Pila.Auditoria.Registros.Where(r => r.Detalle.Values.Any(v => v?.Contains("GUIA-CENTINELA", StringComparison.Ordinal) == true)).ToList();
        Assert.Equal(["GuiaDeTrasladoRegistrada"], conGuia.Select(r => r.Accion).ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task ConLaAuditoriaCaida_ElCambioNoSeAplicaNiDejaMensajes()
    {
        var (escenario, solicitado, _) = await Preparar();
        using var cierre = escenario;
        var mensajes = await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes");
        escenario.Pila.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => escenario.Ejecutar(Administracion(), new AprobarTrasladoEntrada(solicitado)));
        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => escenario.Ejecutar(Administracion(), new SolicitarTrasladoEntrada(escenario.Libro, 1, "cusco", "ayacucho", null)));

        Assert.Equal(("solicitado", 0L), (await escenario.EstadoDe(solicitado), await escenario.VersionDe(solicitado)));
        Assert.Equal(mensajes, await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes"));
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados"));
    }

    // ---------- Presupuesto ----------

    [Fact]
    public async Task ElIntento31DeTrasladosDelPersonalSeRechazaConLaRespuestaUniforme_OtroEncargadoNoSeAfecta()
    {
        var escenario = await new EscenarioDeTraslados(bd).Preparar();
        using var cierre = escenario;
        var usuario = ActorActual.DeUsuario(Guid.NewGuid(), BabelHub.Shared.Contratos.RolesDeUsuario.EncargadoDeSucursal, escenario.Ayacucho);
        var solicitud = new SolicitarTrasladoEntrada(escenario.Libro, 1, "cusco", "ayacucho", null);

        for (var i = 0; i < 30; i++)
        {
            var permitida = await escenario.Ejecutar(usuario, solicitud);
            Assert.True(permitida.EsExito, $"intento {i + 1}: {permitida.Error?.Clave}");
        }

        var excedida = await escenario.Ejecutar(usuario, solicitud);
        var otro = await escenario.Ejecutar(ActorActual.DeUsuario(Guid.NewGuid(), BabelHub.Shared.Contratos.RolesDeUsuario.EncargadoDeSucursal, escenario.Ayacucho), solicitud);

        Assert.Equal((CategoriaDeError.Limite, DetalleDeLimiteDeTraslados.ClaveDeLimiteExcedido), (excedida.Error!.Categoria, excedida.Error.Clave));
        Assert.True(DetalleDeLimiteDeTraslados.TryLeer(excedida.Error.Errores, out var rechazo));
        Assert.Equal("traslados-del-personal", rechazo!.Motivo);
        Assert.True(otro.EsExito);
        Assert.Equal(31L, await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados"));
        Assert.NotEmpty(escenario.Pila.Eventos.Eventos);
    }

    [Fact]
    public async Task LasTransicionesYLasConsultasNoConsumenElPresupuesto()
    {
        var escenario = await new EscenarioDeTraslados(bd).Preparar();
        using var cierre = escenario;
        var usuario = ActorActual.DeUsuario(Guid.NewGuid(), BabelHub.Shared.Contratos.RolesDeUsuario.EncargadoDeSucursal, escenario.Cusco);
        var traslado = await escenario.SolicitarComoAdministracion(1);

        for (var i = 0; i < 35; i++)
        {
            await escenario.Consultar<ListarTrasladosEntrada, PaginaDeTraslados>(usuario, new ListarTrasladosEntrada(null, null, null, null, 1, 10));
        }

        var aprueba = await escenario.Ejecutar(usuario, new AprobarTrasladoEntrada(traslado.TrasladoId));

        Assert.True(aprueba.EsExito, aprueba.Error?.Clave);
        Assert.Empty(escenario.Pila.Eventos.Eventos);
    }

    // ---------- Consultas con PostgreSQL ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-01")]
    public async Task LosOrigenesPosiblesSonLasFuentesConVendible_SinCantidades_ParaElEncargadoDelDestino()
    {
        var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeLima: 0, stockDeCusco: 5);
        using var cierre = escenario;
        await bd.PonerStock(escenario.Lima, escenario.Libro, 2, 2);

        var origenes = await escenario.Consultar<ConsultarOrigenesPosiblesEntrada, OrigenesPosibles>(Encargado(escenario.Ayacucho), new ConsultarOrigenesPosiblesEntrada(escenario.Libro, "ayacucho"));

        Assert.Equal(["cusco"], origenes.Valor.Origenes.Select(o => o.Codigo).ToArray());
        Assert.Equal(2, typeof(OrigenPosible).GetProperties().Length);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public async Task LaListaYLosTrasladosDeUnPedidoSeConsultanConLosFiltros_YLaFechaEstimadaFigura()
    {
        var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeCusco: 1);
        using var cierre = escenario;
        var (pedido, _, vinculado) = await escenario.PedidoConTraslado();
        await bd.PonerStock(escenario.Cusco, escenario.Libro, 5, 1);
        var reposicion = await escenario.SolicitarComoAdministracion(1);

        var vinculados = await escenario.Consultar<ListarTrasladosEntrada, PaginaDeTraslados>(Administracion(), new ListarTrasladosEntrada("solicitado", null, true, null, 1, 20));
        var reposiciones = await escenario.Consultar<ListarTrasladosEntrada, PaginaDeTraslados>(Logistica(), new ListarTrasladosEntrada(null, "ayacucho", false, null, 1, 20));
        var demorados = await escenario.Consultar<ListarTrasladosEntrada, PaginaDeTraslados>(Administracion(), new ListarTrasladosEntrada("solicitado", null, true, 48, 1, 20));
        var delPedido = await escenario.Consultar<ConsultarTrasladosDeUnPedidoEntrada, TrasladosDeUnPedido>(Encargado(escenario.Lima), new ConsultarTrasladosDeUnPedidoEntrada(pedido));
        var ajeno = await escenario.Consultar<ConsultarTrasladosDeUnPedidoEntrada, TrasladosDeUnPedido>(Encargado(escenario.Ayacucho), new ConsultarTrasladosDeUnPedidoEntrada(pedido));

        Assert.Equal([vinculado], vinculados.Valor.Elementos.Select(t => t.TrasladoId).ToArray());
        Assert.Equal([reposicion.TrasladoId], reposiciones.Valor.Elementos.Select(t => t.TrasladoId).ToArray());
        Assert.Equal(0, demorados.Valor.Total);
        Assert.Equal([vinculado], delPedido.Valor.Traslados.Select(t => t.TrasladoId).ToArray());
        Assert.NotNull(delPedido.Valor.Traslados.Single().FechaEstimadaDeLlegada);
        Assert.Empty(ajeno.Valor.Traslados);
    }
}
