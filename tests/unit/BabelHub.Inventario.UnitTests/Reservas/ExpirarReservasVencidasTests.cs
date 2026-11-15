using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Domain.Fuentes;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.UnitTests.Fuentes;
using BabelHub.Inventario.UnitTests.Soporte;
using AplicacionTipoDeActor = BabelHub.Inventario.Application.Plataforma.TipoDeActor;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>
/// T19: la expiración compartida (SEL-08, SEL-27): lo que libera <c>Bloquear</c> por <c>IReservasDeStock</c> y el caso de uso por pedido del trabajo de fondo. Candidatos sin bloqueo, <c>IntentarPedido</c>, líneas por id,
/// vencimiento repetido con la hora del reloj, deltas agregados y ordenados y <c>ReservaExpirada</c> con actor «sistema».
/// </summary>
public sealed class ExpirarReservasVencidasTests
{
    private static DateTimeOffset Ahora => EntornoDeFuentes.Ahora;

    /// <summary>Una reserva pendiente cuya expiración ya pasó (venció hace cinco minutos), con una línea por edición.</summary>
    private static ReservaDePedido Vencida(EntornoDeReservas e, Guid pedido, params (Guid Edicion, Fuente Fuente, int Cantidad)[] lineas) =>
        Reserva(e, pedido, Ahora.AddMinutes(-20), lineas);

    private static ReservaDePedido Vigente(EntornoDeReservas e, Guid pedido, params (Guid Edicion, Fuente Fuente, int Cantidad)[] lineas) =>
        Reserva(e, pedido, Ahora.AddMinutes(-5), lineas);

    private static ReservaDePedido Reserva(EntornoDeReservas e, Guid pedido, DateTimeOffset iniciadaEn, (Guid Edicion, Fuente Fuente, int Cantidad)[] lineas)
    {
        var reserva = ReservaDePedido.Iniciar(
            Guid.NewGuid(), pedido, Guid.NewGuid(), TipoDeEntrega.Recojo, e.Base.Lima.Id, null, iniciadaEn,
            lineas.Select(l => LineaDeReserva.Pendiente(Guid.NewGuid(), Guid.NewGuid(), l.Edicion, l.Fuente.Id, l.Cantidad, iniciadaEn)));
        e.Reservas.Guardadas.Add(reserva);
        return reserva;
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task Bloquear_ExpiraLasVencidasDeLasEdicionesYDevuelveLasUnidades()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 3);
        var reserva = Vencida(entorno, Guid.NewGuid(), (libro, entorno.Base.Lima, 3));

        var bloqueo = await entorno.Stock.Bloquear([libro]);

        Assert.True(bloqueo.EstaBloqueado);
        Assert.Equal(EstadoDeLinea.Expirada, Assert.Single(reserva.Lineas).Estado);
        Assert.Equal((5, 0), entorno.FilaDe(entorno.Base.Lima, libro));
        Assert.Equal(new ExistenciaBloqueada(entorno.Base.Lima.Id, libro, 5, 0), bloqueo.Fila(entorno.Base.Lima.Id, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task LaExpiracionSeAuditaConActorSistemaAunqueLaTransaccionSeaDeUnCliente()
    {
        var entorno = new EntornoDeReservas();
        var clienteId = entorno.ClienteId;
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Cusco.Id, libro, 4, 2);
        var pedido = Guid.NewGuid();
        Vencida(entorno, pedido, (libro, entorno.Base.Cusco, 2));

        await entorno.Stock.Bloquear([libro]);

        var (solicitud, actor) = Assert.Single(entorno.Auditoria.Registros);
        Assert.Equal(AccionesAuditables.ReservaExpirada, solicitud.Accion);
        Assert.Equal(AplicacionTipoDeActor.Sistema, actor);
        Assert.Equal(("pedido", pedido.ToString("D"), entorno.Base.Cusco.Id), (solicitud.ObjetoTipo, solicitud.ObjetoId, solicitud.SucursalOrigen));
        Assert.Equal(libro.ToString("D"), solicitud.Detalle["edicionId"]);
        Assert.Equal("2", solicitud.Detalle["cantidad"]);
        Assert.Equal(1, entorno.Sistema.Llamadas);

        // El actor del cliente vuelve a su lugar: lo que sigue en la transacción del cliente no es del sistema.
        Assert.Equal(clienteId, entorno.Base.Actor.Actual!.UsuarioId);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task LaAuditoriaDeLaExpiracionTieneUnaEntradaPorLinea()
    {
        var entorno = new EntornoDeReservas();
        var a = entorno.Impresa();
        var b = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, a, 5, 1);
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, b, 5, 2);
        Vencida(entorno, Guid.NewGuid(), (a, entorno.Base.Lima, 1), (b, entorno.Base.Lima, 2));

        await entorno.Stock.Bloquear([a, b]);

        Assert.Equal(2, entorno.Auditoria.Registros.Count);
        Assert.All(entorno.Auditoria.Registros, r => Assert.Equal(AplicacionTipoDeActor.Sistema, r.Actor));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task UnaReservaVigenteNoSeExpira_NiUnaConfirmada()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 10, 5);
        var vigente = Vigente(entorno, Guid.NewGuid(), (libro, entorno.Base.Lima, 2));
        var confirmada = Vencida(entorno, Guid.NewGuid(), (libro, entorno.Base.Lima, 3));
        Assert.True(confirmada.Confirmar(Ahora.AddMinutes(-18)));

        await entorno.Stock.Bloquear([libro]);

        Assert.Equal(EstadoDeLinea.Pendiente, Assert.Single(vigente.Lineas).Estado);
        Assert.Equal(EstadoDeLinea.Confirmada, Assert.Single(confirmada.Lineas).Estado);
        Assert.Equal((10, 5), entorno.FilaDe(entorno.Base.Lima, libro));
        Assert.Empty(entorno.Auditoria.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task LaReservaQueVenceJustoEnElInstanteActualYaEstaVencida()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 1);
        Reserva(entorno, Guid.NewGuid(), Ahora - ExpiracionDeReserva.Inicial, [(libro, entorno.Base.Lima, 1)]);

        await entorno.Stock.Bloquear([libro]);

        Assert.Equal((5, 0), entorno.FilaDe(entorno.Base.Lima, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task SoloSeExpiranLasVencidasDeLasEdicionesPedidas()
    {
        var entorno = new EntornoDeReservas();
        var pedida = entorno.Impresa();
        var ajena = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, pedida, 5, 1);
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, ajena, 5, 1);
        var reserva = Vencida(entorno, Guid.NewGuid(), (pedida, entorno.Base.Lima, 1), (ajena, entorno.Base.Lima, 1));

        await entorno.Stock.Bloquear([pedida]);

        Assert.Equal([EstadoDeLinea.Expirada, EstadoDeLinea.Pendiente], reserva.Lineas.OrderBy(l => l.EdicionId == pedida ? 0 : 1).Select(l => l.Estado));
        Assert.Equal((5, 1), entorno.FilaDe(entorno.Base.Lima, ajena));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task UnPedidoQueOtraTransaccionTiene_SeOmiteYSusUnidadesSiguenReservadas()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 3);
        var ocupado = Guid.NewGuid();
        var libre = Guid.NewGuid();
        var delOcupado = Vencida(entorno, ocupado, (libro, entorno.Base.Lima, 1));
        var delLibre = Vencida(entorno, libre, (libro, entorno.Base.Lima, 2));
        entorno.Bloqueos.PedidosOcupados.Add(ocupado);

        var bloqueo = await entorno.Stock.Bloquear([libro]);

        // Error conservador: lo del pedido ocupado sigue reservado y no cuenta como disponible; no se audita ni se toca.
        Assert.Equal(EstadoDeLinea.Pendiente, Assert.Single(delOcupado.Lineas).Estado);
        Assert.Equal(EstadoDeLinea.Expirada, Assert.Single(delLibre.Lineas).Estado);
        Assert.Equal((5, 1), entorno.FilaDe(entorno.Base.Lima, libro));
        Assert.Equal(1, bloqueo.Fila(entorno.Base.Lima.Id, libro).Reservado);
        Assert.Equal(libre.ToString("D"), Assert.Single(entorno.Auditoria.Registros).Solicitud.ObjetoId);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task SiSeBloqueaDosVecesNoSeLiberaDosVeces()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 2);
        Vencida(entorno, Guid.NewGuid(), (libro, entorno.Base.Lima, 2));

        await entorno.Stock.Bloquear([libro]);
        await entorno.Stock.Bloquear([libro]);

        Assert.Equal((5, 0), entorno.FilaDe(entorno.Base.Lima, libro));
        Assert.Single(entorno.Auditoria.Registros);
        Assert.Single(entorno.Base.Existencias.Liberaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task LosDeltasDeLasVencidasSeAgreganYSeAplicanEnElOrdenEdicionFuente()
    {
        var entorno = new EntornoDeReservas();
        var a = entorno.Impresa();
        var b = entorno.Impresa();
        foreach (var fuente in new[] { entorno.Base.Lima, entorno.Base.Cusco })
        {
            entorno.Base.Existencias.Poner(fuente.Id, a, 9, 4);
            entorno.Base.Existencias.Poner(fuente.Id, b, 9, 4);
        }

        // Dos pedidos con ediciones cruzadas y fuentes mezcladas: los deltas de una misma pareja se suman.
        Vencida(entorno, Guid.NewGuid(), (b, entorno.Base.Cusco, 1), (a, entorno.Base.Lima, 1));
        Vencida(entorno, Guid.NewGuid(), (a, entorno.Base.Lima, 2), (b, entorno.Base.Cusco, 2));
        Vencida(entorno, Guid.NewGuid(), (a, entorno.Base.Cusco, 1));

        await entorno.Stock.Bloquear([b, a]);

        var liberaciones = entorno.Base.Existencias.Liberaciones;
        Assert.Equal(
            liberaciones.OrderBy(m => m.EdicionId, OrdenDeBloqueo.DeIdentificadores).ThenBy(m => m.FuenteId, OrdenDeBloqueo.DeIdentificadores).Select(m => (m.EdicionId, m.FuenteId)),
            liberaciones.Select(m => (m.EdicionId, m.FuenteId)));
        Assert.Equal(liberaciones.Count, liberaciones.Select(m => (m.EdicionId, m.FuenteId)).Distinct().Count());
        Assert.Equal(3, liberaciones.Count);
        Assert.Equal(-7, liberaciones.Sum(m => m.DeltaReservado));
        Assert.All(liberaciones, m => Assert.True(m.DeltaReservado < 0 && m.DeltaFisico == 0));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task LosPedidosCandidatosSeTomanSinEsperarYEnUnOrdenFijo()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 9, 3);
        var pedidos = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        foreach (var pedido in pedidos)
        {
            Vencida(entorno, pedido, (libro, entorno.Base.Lima, 1));
        }

        await entorno.Stock.Bloquear([libro]);

        Assert.Equal(pedidos.Order(), entorno.Bloqueos.Intentados);
        Assert.Empty(entorno.Bloqueos.Pedidos);
        var orden = entorno.Base.Bitacora.Llamadas;
        Assert.True(orden.IndexOf("reservas.PedidosConVencidas") < orden.IndexOf("bloqueo.IntentarPedido"));
        Assert.True(orden.LastIndexOf("reservas.ExpirarLineasVencidas") < orden.IndexOf("existencias.BloquearFilas"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task SiLaAuditoriaFalla_LaLiberacionNoLlegaAAplicarseAlStock()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 2);
        Vencida(entorno, Guid.NewGuid(), (libro, entorno.Base.Lima, 2));
        entorno.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.Stock.Bloquear([libro]));

        // La excepción deshace la transacción real (AUD-04): aquí al menos el stock no se tocó y el actor volvió a su lugar.
        Assert.Empty(entorno.Base.Existencias.Liberaciones);
        Assert.Equal((5, 2), entorno.FilaDe(entorno.Base.Lima, libro));
        Assert.Equal(entorno.ClienteId, entorno.Base.Actor.Actual!.UsuarioId);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task VencidasPorEdicion_UsaLaHoraDelRelojYSoloLeeSinBloquear()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        Vencida(entorno, Guid.NewGuid(), (libro, entorno.Base.Lima, 2));
        Vencida(entorno, Guid.NewGuid(), (libro, entorno.Base.Lima, 3));
        Vigente(entorno, Guid.NewGuid(), (libro, entorno.Base.Lima, 4));

        var vencidas = await entorno.ReservasDeStock.VencidasPorEdicion([libro]);

        Assert.Equal(new UnidadesVencidas(entorno.Base.Lima.Id, libro, 5), Assert.Single(vencidas));
        Assert.Empty(entorno.Bloqueos.Intentados);
        Assert.Equal(0, entorno.Sistema.Llamadas);

        // Si el reloj avanza, la vigente también vence: la lectura no depende de la hora de la base.
        entorno.Base.Reloj.Ahora = Ahora.AddMinutes(30);
        Assert.Equal(9, Assert.Single(await entorno.ReservasDeStock.VencidasPorEdicion([libro])).Unidades);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task PedidosQueApartan_CuentaLasPendientesVigentesYLasConfirmadas_PeroNoLasVencidas()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        var vigente = Guid.NewGuid();
        var confirmada = Guid.NewGuid();
        Vigente(entorno, vigente, (libro, entorno.Base.Lima, 1));
        Assert.True(Vencida(entorno, confirmada, (libro, entorno.Base.Lima, 1)).Confirmar(Ahora.AddMinutes(-18)));
        Vencida(entorno, Guid.NewGuid(), (libro, entorno.Base.Lima, 1));
        Vigente(entorno, Guid.NewGuid(), (libro, entorno.Base.Cusco, 1));

        var apartan = await entorno.ReservasDeStock.PedidosQueApartan(entorno.Base.Lima.Id, libro, 10);

        Assert.Equal(2, apartan.TotalDePedidos);
        Assert.Equal(new[] { vigente, confirmada }.Order(), apartan.PedidoIds);
        Assert.Single((await entorno.ReservasDeStock.PedidosQueApartan(entorno.Base.Lima.Id, libro, 1)).PedidoIds);
    }

    // --- el caso de uso por pedido del trabajo de fondo ---

    private static EntornoDeReservas ConSistema() => new(ActorDePrueba.DelSistema());

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task ExpirarUnPedido_ExpiraTodasSusVencidasYDevuelveLasUnidades()
    {
        var entorno = ConSistema();
        var a = entorno.Impresa();
        var b = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, a, 5, 2);
        entorno.Base.Existencias.Poner(entorno.Base.Cusco.Id, b, 5, 3);
        var pedido = Guid.NewGuid();
        var reserva = Vencida(entorno, pedido, (a, entorno.Base.Lima, 2), (b, entorno.Base.Cusco, 3));

        var resultado = await entorno.ExpirarPedido().Ejecutar(new ExpirarReservasEntrada(pedido));

        Assert.True(resultado.EsExito);
        Assert.Equal(new ExpiracionDelPedido(pedido, true, 2), resultado.Valor);
        Assert.All(reserva.Lineas, l => Assert.Equal(EstadoDeLinea.Expirada, l.Estado));
        Assert.Equal((5, 0), entorno.FilaDe(entorno.Base.Lima, a));
        Assert.Equal((5, 0), entorno.FilaDe(entorno.Base.Cusco, b));
        Assert.Equal(2, entorno.Auditoria.Registros.Count);
        Assert.All(entorno.Auditoria.Registros, r => Assert.Equal(AplicacionTipoDeActor.Sistema, r.Actor));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task ExpirarUnPedido_TomaElPedidoSinEsperarAntesDeLeerYDeBloquearElStock()
    {
        var entorno = ConSistema();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 1);
        var pedido = Guid.NewGuid();
        Vencida(entorno, pedido, (libro, entorno.Base.Lima, 1));

        await entorno.ExpirarPedido().Ejecutar(new ExpirarReservasEntrada(pedido));

        var orden = entorno.Base.Bitacora.Llamadas;
        Assert.Empty(entorno.Bloqueos.Pedidos);
        Assert.True(orden.IndexOf("bloqueo.IntentarPedido") < orden.IndexOf("reservas.VencidasDelPedido"));
        Assert.True(orden.IndexOf("reservas.VencidasDelPedido") < orden.IndexOf("existencias.GarantizarFilas"));
        Assert.True(orden.IndexOf("existencias.GarantizarFilas") < orden.IndexOf("existencias.BloquearFilas"));
        Assert.True(orden.IndexOf("reservas.ExpirarLineasVencidas") < orden.IndexOf("existencias.BloquearFilas"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task ExpirarUnPedido_ConOtraTransaccionQueLoTiene_NoLoProcesaYNoTocaNada()
    {
        var entorno = ConSistema();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 1);
        var pedido = Guid.NewGuid();
        var reserva = Vencida(entorno, pedido, (libro, entorno.Base.Lima, 1));
        entorno.Bloqueos.PedidosOcupados.Add(pedido);

        var resultado = await entorno.ExpirarPedido().Ejecutar(new ExpirarReservasEntrada(pedido));

        Assert.Equal(new ExpiracionDelPedido(pedido, false, 0), resultado.Valor);
        Assert.Equal(EstadoDeLinea.Pendiente, Assert.Single(reserva.Lineas).Estado);
        Assert.Equal((5, 1), entorno.FilaDe(entorno.Base.Lima, libro));
        Assert.Empty(entorno.Auditoria.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task ExpirarUnPedido_SinVencidas_NoHaceNadaNiBloqueaFilasDeStock()
    {
        var entorno = ConSistema();
        var libro = entorno.Impresa();
        var pedido = Guid.NewGuid();
        Vigente(entorno, pedido, (libro, entorno.Base.Lima, 1));

        var resultado = await entorno.ExpirarPedido().Ejecutar(new ExpirarReservasEntrada(pedido));

        Assert.Equal(new ExpiracionDelPedido(pedido, true, 0), resultado.Valor);
        Assert.DoesNotContain("existencias.BloquearFilas", entorno.Base.Bitacora.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task ExpirarUnPedido_ConUnPedidoVacio_EsUnaValidacion()
    {
        var resultado = await ConSistema().ExpirarPedido().Ejecutar(new ExpirarReservasEntrada(Guid.Empty));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
    }

    public static TheoryData<string> ActoresQueNoSonElSistema() => new() { "sin-contexto", "visitante", "cliente", "administrador", "integracion" };

    [Theory]
    [MemberData(nameof(ActoresQueNoSonElSistema))]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task ExpirarUnPedido_SoloLoHaceElSistema_SinActorEsNoAutenticadoYNoTocaNada(string quien)
    {
        var actor = quien switch
        {
            "sin-contexto" => ActorDePrueba.SinContexto(),
            "visitante" => ActorDePrueba.Anonimo(),
            "cliente" => ActorDePrueba.DeCliente(),
            "administrador" => ActorDePrueba.DeAdministradorCentral(),
            _ => ActorDePrueba.DeIntegracion("expiracion-de-reservas"),
        };
        var entorno = new EntornoDeReservas(actor);
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 1);
        var pedido = Guid.NewGuid();
        var reserva = Vencida(entorno, pedido, (libro, entorno.Base.Lima, 1));

        var resultado = await entorno.ExpirarPedido().Ejecutar(new ExpirarReservasEntrada(pedido));

        var esperado = quien is "sin-contexto" or "visitante" ? CategoriaDeError.NoAutenticado : CategoriaDeError.Prohibido;
        Assert.Equal(esperado, resultado.Error!.Categoria);
        Assert.Equal(EstadoDeLinea.Pendiente, Assert.Single(reserva.Lineas).Estado);
        Assert.Empty(entorno.Bloqueos.Intentados);
        Assert.Single(entorno.Base.Denegaciones.Eventos);
    }
}
