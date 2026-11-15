using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Domain.Fuentes;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.UnitTests.Fuentes;
using BabelHub.Inventario.UnitTests.Soporte;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>T17: <c>CerrarReservas</c> (SEL-11, SEL-21, SEL-28, SEL-29, SEL-38, SEL-39): entrega y reparto con el alcance primero, la clave, <c>ya-cerrada</c> y el descuento del físico una sola vez.</summary>
public sealed class CerrarReservasTests
{
    private static string Clave() => EntornoDeReservas.ClaveNueva();

    private static CerrarReservasEntrada Entrega(Guid pedido, IReadOnlyList<Guid> lineas, string? clave = null) => new(pedido, MotivoDeCierreDeReserva.Entrega, lineas, null, clave ?? Clave());

    private static CerrarReservasEntrada Reparto(Guid pedido, IReadOnlyList<Guid> lineas, Guid? envio = null, string? clave = null) => new(pedido, MotivoDeCierreDeReserva.Reparto, lineas, envio ?? Guid.NewGuid(), clave ?? Clave());

    /// <summary>Una reserva confirmada de 1 unidad con físico 9 y reservado 1 en la fuente dada, de recojo en Lima o a domicilio.</summary>
    private static (EntornoDeReservas Entorno, ReservaDePedido Reserva, Guid Pedido, Guid Libro, Guid Linea) Confirmada(bool recojo = true, Func<EntornoDeReservas, Fuente>? fuente = null)
    {
        var e = new EntornoDeReservas(ActorDePrueba.DeAdministradorCentral());
        var libro = e.Impresa();
        var origen = fuente?.Invoke(e) ?? (recojo ? e.Base.Lima : e.Base.Cusco);
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(libro, origen, 1)], recojoEnLima: recojo);
        Assert.True(reserva.Confirmar(e.Base.Reloj.Ahora));
        e.Base.Existencias.Poner(origen.Id, libro, 9, 1);
        return (e, reserva, pedido, libro, reserva.Lineas.Single().LineaId);
    }

    private static void ComoEncargadoDe(EntornoDeReservas e, Guid sucursal) => e.Base.Actor.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, sucursal));

    private static void ComoLogistica(EntornoDeReservas e) => e.Base.Actor.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.PersonalDeLogistica));

    [Fact]
    [Trait("Criterio", "AC-SEL-11")]
    public async Task Entregar_DescuentaElFisicoYLoReservado_CierraLaLineaYLaAuditaConElFisicoAnteriorYPosterior()
    {
        var (e, reserva, pedido, libro, linea) = Confirmada();
        ComoEncargadoDe(e, e.Base.Lima.Id);

        var resultado = await e.Cerrar().Ejecutar(Entrega(pedido, [linea]));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal([new ResultadoDeCierreDeLinea(linea, false)], resultado.Valor.Lineas);
        Assert.Equal((8, 0), e.FilaDe(e.Base.Lima, libro));
        var cerrada = Assert.Single(reserva.Lineas);
        Assert.Equal((EstadoDeLinea.Cerrada, CausaDeCierre.Entrega), (cerrada.Estado, cerrada.CerradaPor));
        var (solicitud, _) = Assert.Single(e.Auditoria.Registros);
        Assert.Equal((AccionesAuditables.StockDescontadoPorEntrega, "pedido", "9", "8"), (solicitud.Accion, solicitud.ObjetoTipo, solicitud.Detalle["cantidadAnterior"], solicitud.Detalle["cantidadPosterior"]));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-21")]
    public async Task PasarAEnRepartoUnaReservaADomicilio_DescuentaElFisicoDeLaFuenteQueDespacha()
    {
        var (e, reserva, pedido, libro, linea) = Confirmada(recojo: false);
        ComoLogistica(e);

        var resultado = await e.Cerrar().Ejecutar(Reparto(pedido, [linea]));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((8, 0), e.FilaDe(e.Base.Cusco, libro));
        Assert.Equal(CausaDeCierre.Reparto, reserva.Lineas.Single().CerradaPor);
        Assert.Equal(AccionesAuditables.StockDescontadoPorReparto, Assert.Single(e.Auditoria.Registros).Solicitud.Accion);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-21")]
    public async Task RepetirConOtraClave_RespondeYaCerrada_YElFisicoNoBajaOtraVez()
    {
        var (e, _, pedido, libro, linea) = Confirmada(recojo: false);
        ComoLogistica(e);
        var envio = Guid.NewGuid();
        Assert.True((await e.Cerrar().Ejecutar(Reparto(pedido, [linea], envio))).EsExito);

        var repetida = await e.Cerrar().Ejecutar(Reparto(pedido, [linea], envio));

        Assert.True(repetida.EsExito, repetida.Error?.Clave);
        Assert.Equal([new ResultadoDeCierreDeLinea(linea, true)], repetida.Valor.Lineas);
        Assert.Equal((8, 0), e.FilaDe(e.Base.Cusco, libro));
        Assert.Single(e.Auditoria.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-39")]
    public async Task RepetirConLaMismaClaveYCarga_DevuelveElResultadoOriginal_SinDescontarOtraVez()
    {
        var (e, _, pedido, libro, linea) = Confirmada(recojo: false);
        ComoLogistica(e);
        var entrada = Reparto(pedido, [linea]);
        var primera = await e.Cerrar().Ejecutar(entrada);

        var repetida = await e.Cerrar().Ejecutar(entrada);

        Assert.Equal(primera.Valor.Lineas, repetida.Valor.Lineas);
        Assert.Equal([new ResultadoDeCierreDeLinea(linea, false)], repetida.Valor.Lineas);
        Assert.Equal((8, 0), e.FilaDe(e.Base.Cusco, libro));
        Assert.Single(e.Auditoria.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-39")]
    public async Task LaMismaClaveConOtraCarga_EsConflicto()
    {
        var (e, _, pedido, _, linea) = Confirmada(recojo: false);
        ComoLogistica(e);
        var clave = Clave();
        Assert.True((await e.Cerrar().Ejecutar(Reparto(pedido, [linea], clave: clave))).EsExito);

        var otro = await e.Cerrar().Ejecutar(Reparto(pedido, [linea], Guid.NewGuid(), clave));

        Assert.Equal((CategoriaDeError.Conflicto, "idempotencia-en-conflicto"), (otro.Error!.Categoria, otro.Error.Clave));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-40")]
    public async Task UnEncargadoDeOtraSucursal_NoEntrega_403ConElEventoAntesDeMirarLaClaveElEstadoOYaCerrada()
    {
        var (e, _, pedido, libro, linea) = Confirmada();
        var clave = Clave();
        e.Reservas.Operaciones[(pedido, "cerrar", clave)] = new OperacionGuardada(CerrarReservas.Huella(Entrega(pedido, [linea], clave)), "[]");
        ComoEncargadoDe(e, e.Base.Cusco.Id);

        var resultado = await e.Cerrar().Ejecutar(Entrega(pedido, [linea], clave));

        Assert.Equal((CategoriaDeError.Prohibido, "prohibido"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(["fuera-de-sucursal"], e.Base.MotivosDeDenegacion);
        Assert.DoesNotContain("reservas.ObtenerOperacion", e.Base.Bitacora.Llamadas);
        Assert.Equal((9, 1), e.FilaDe(e.Base.Lima, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-40")]
    public async Task UnEncargadoQueConsultaUnPedidoInexistente_RecibeElMismo403Que_UnoAjeno_SinPistas()
    {
        var (e, _, _, _, _) = Confirmada();
        ComoEncargadoDe(e, e.Base.Cusco.Id);

        var inexistente = await e.Cerrar().Ejecutar(Entrega(Guid.NewGuid(), [Guid.NewGuid()]));

        Assert.Equal((CategoriaDeError.Prohibido, "prohibido"), (inexistente.Error!.Categoria, inexistente.Error.Clave));
        Assert.Equal(["fuera-de-sucursal"], e.Base.MotivosDeDenegacion);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-40")]
    [InlineData("cliente-entrega")]
    [InlineData("logistica-entrega")]
    [InlineData("encargado-reparto")]
    [InlineData("cliente-reparto")]
    [InlineData("integracion-reparto")]
    public async Task UnRolSinAlcance_RecibeProhibidoAntesDeLeerLaReserva(string caso)
    {
        var (e, _, pedido, libro, linea) = Confirmada(recojo: caso.EndsWith("entrega", StringComparison.Ordinal));
        switch (caso)
        {
            case "cliente-entrega" or "cliente-reparto":
                e.Base.Actor.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.Cliente));
                break;
            case "logistica-entrega":
                ComoLogistica(e);
                break;
            case "encargado-reparto":
                ComoEncargadoDe(e, e.Base.Cusco.Id);
                break;
            default:
                e.Base.Actor.Establecer(ActorActual.DeIntegracion("expiracion-de-reservas"));
                break;
        }

        var entrada = caso.EndsWith("entrega", StringComparison.Ordinal) ? Entrega(pedido, [linea]) : Reparto(pedido, [linea]);
        var resultado = await e.Cerrar().Ejecutar(entrada);

        Assert.Equal((CategoriaDeError.Prohibido, "prohibido"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.DoesNotContain("reservas.UltimaDelPedido", e.Base.Bitacora.Llamadas);
        Assert.Single(e.Base.Denegaciones.Eventos);
        Assert.Equal(9, e.FilaDe(caso.EndsWith("entrega", StringComparison.Ordinal) ? e.Base.Lima : e.Base.Cusco, libro).Fisico);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-40")]
    public async Task SinActor_EsNoAutenticado()
    {
        var (e, _, pedido, _, linea) = Confirmada();
        e.Base.Actor.Establecer(null);

        var resultado = await e.Cerrar().Ejecutar(Entrega(pedido, [linea]));

        Assert.Equal(CategoriaDeError.NoAutenticado, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-28")]
    public async Task EntregarUnaReservaLiberada_EsEstadoInvalido_YElStockNoCambia()
    {
        var (e, reserva, pedido, libro, linea) = Confirmada();
        reserva.Lineas.Single().Liberar(e.Base.Reloj.Ahora);
        e.Base.Existencias.Poner(e.Base.Lima.Id, libro, 9, 0);
        ComoEncargadoDe(e, e.Base.Lima.Id);

        var resultado = await e.Cerrar().Ejecutar(Entrega(pedido, [linea]));

        Assert.Equal((CategoriaDeError.Conflicto, "reserva-estado-invalido"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal((9, 0), e.FilaDe(e.Base.Lima, libro));
        Assert.Empty(e.Auditoria.Registros);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-29")]
    [InlineData("pendiente")]
    [InlineData("expirada")]
    [InlineData("cerrada-por-reparto")]
    public async Task UnaLineaQueNoEstaConfirmadaNiCerradaPorEstaOperacion_EsEstadoInvalido(string estado)
    {
        var e = new EntornoDeReservas(ActorDePrueba.DeAdministradorCentral());
        var libro = e.Impresa();
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(libro, e.Base.Lima, 1)]);
        var linea = reserva.Lineas.Single();
        switch (estado)
        {
            case "expirada":
                linea.Expirar(e.Base.Reloj.Ahora);
                break;
            case "cerrada-por-reparto":
                Assert.True(reserva.Confirmar(e.Base.Reloj.Ahora));
                linea.Cerrar(CausaDeCierre.Reparto, e.Base.Reloj.Ahora);
                break;
        }

        var resultado = await e.Cerrar().Ejecutar(Entrega(pedido, [linea.LineaId]));

        Assert.Equal("reserva-estado-invalido", resultado.Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-28")]
    public async Task Entregar_CuandoLasUnidadesSiguenEnLaFuenteDeOrigenDeUnTraslado_EsEstadoInvalido_NoUnAccesoDenegado()
    {
        var (e, _, pedido, libro, linea) = Confirmada(recojo: true, fuente: x => x.Base.Cusco);
        ComoEncargadoDe(e, e.Base.Lima.Id);

        var resultado = await e.Cerrar().Ejecutar(Entrega(pedido, [linea]));

        Assert.Equal((CategoriaDeError.Conflicto, "reserva-estado-invalido"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Empty(e.Base.Denegaciones.Eventos);
        Assert.Equal((9, 1), e.FilaDe(e.Base.Cusco, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-29")]
    public async Task SiUnaLineaEsInvalida_SeRechazaTodoSinCambiosParciales()
    {
        var e = new EntornoDeReservas(ActorDePrueba.DeAdministradorCentral());
        var a = e.Impresa();
        var b = e.Impresa();
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(a, e.Base.Lima, 1), (b, e.Base.Lima, 1)]);
        e.Base.Existencias.Poner(e.Base.Lima.Id, a, 9, 1);
        e.Base.Existencias.Poner(e.Base.Lima.Id, b, 9, 1);
        reserva.Lineas.Single(l => l.EdicionId == a).Confirmar(e.Base.Reloj.Ahora);

        var resultado = await e.Cerrar().Ejecutar(Entrega(pedido, [.. reserva.Lineas.Select(l => l.LineaId)]));

        Assert.Equal("reserva-estado-invalido", resultado.Error!.Clave);
        Assert.Equal(EstadoDeLinea.Confirmada, reserva.Lineas.Single(l => l.EdicionId == a).Estado);
        Assert.Equal((9, 1), e.FilaDe(e.Base.Lima, a));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-11")]
    [InlineData("reparto-en-recojo")]
    [InlineData("entrega-en-domicilio")]
    public async Task LaOperacionDebeCorresponderAlTipoDeEntregaDeLaReserva(string caso)
    {
        var (e, _, pedido, _, linea) = Confirmada(recojo: caso == "reparto-en-recojo");
        e.Base.Actor.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.AdministradorCentral));

        var resultado = await e.Cerrar().Ejecutar(caso == "reparto-en-recojo" ? Reparto(pedido, [linea]) : Entrega(pedido, [linea]));

        Assert.Equal("reserva-estado-invalido", resultado.Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-11")]
    public async Task CerrarUnaLineaQueNoEsDeLaReserva_EsNoEncontrada()
    {
        var (e, _, pedido, _, _) = Confirmada();

        var resultado = await e.Cerrar().Ejecutar(Entrega(pedido, [Guid.NewGuid()]));

        Assert.Equal(CategoriaDeError.NoEncontrado, resultado.Error!.Categoria);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-11")]
    [InlineData("reparto-sin-envio")]
    [InlineData("entrega-con-envio")]
    [InlineData("sin-lineas")]
    [InlineData("lineas-repetidas")]
    [InlineData("clave-corta")]
    public async Task UnaEntradaInvalida_EsValidacion(string caso)
    {
        var (e, _, pedido, _, linea) = Confirmada();
        var entrada = caso switch
        {
            "reparto-sin-envio" => new CerrarReservasEntrada(pedido, MotivoDeCierreDeReserva.Reparto, [linea], null, Clave()),
            "entrega-con-envio" => new CerrarReservasEntrada(pedido, MotivoDeCierreDeReserva.Entrega, [linea], Guid.NewGuid(), Clave()),
            "sin-lineas" => Entrega(pedido, []),
            "lineas-repetidas" => Entrega(pedido, [linea, linea]),
            _ => Entrega(pedido, [linea], "corta"),
        };

        var resultado = await e.Cerrar().Ejecutar(entrada);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-32")]
    public async Task SiLaAuditoriaFalla_ElCierreNoSeAplica()
    {
        var (e, _, pedido, _, linea) = Confirmada();
        ComoEncargadoDe(e, e.Base.Lima.Id);
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => e.Cerrar().Ejecutar(Entrega(pedido, [linea])));

        // El doble no es transaccional: la excepción, en producción, revierte el descuento y la operación guardada.
        Assert.Empty(e.Reservas.Operaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-11")]
    public async Task Cerrar_TomaElBloqueoDelPedidoLuegoLasLineasPorIdYPorFinLasFilasDeStock_YNoElDelCliente()
    {
        var (e, _, pedido, _, linea) = Confirmada();

        await e.Cerrar().Ejecutar(Entrega(pedido, [linea]));

        var orden = e.Base.Bitacora.Llamadas;
        Assert.Equal([pedido], e.Bloqueos.Pedidos);
        Assert.Empty(e.Bloqueos.Clientes);
        Assert.True(orden.IndexOf("bloqueo.Pedido") < orden.IndexOf("existencias.BloquearFilas"));
        Assert.True(orden.IndexOf("reservas.BloquearLineas") < orden.IndexOf("existencias.BloquearFilas"));
        Assert.True(orden.IndexOf("existencias.BloquearFilas") < orden.IndexOf("reservas.CerrarLineas"));
        Assert.True(orden.IndexOf("reservas.CerrarLineas") < orden.IndexOf("existencias.Aplicar(-1,-1)"));
        Assert.Equal(EntornoDeFuentes.Ahora, e.Base.Reloj.Ahora);
    }
}
