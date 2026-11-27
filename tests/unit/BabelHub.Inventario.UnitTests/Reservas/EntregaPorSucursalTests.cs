using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.UnitTests.Soporte;
using static BabelHub.Inventario.UnitTests.Reservas.EntornoDeReservas;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>
/// T38: la entrega de un recojo por sucursal (SEL-45, SEL-39): el encargado solo entrega un conjunto de líneas que sean todas de su sucursal, en cualquier estado; si alguna es ajena o no existe recibe el mismo 403 con su
/// evento antes de mirar la clave o el estado; la administración central entrega cualquiera.
/// </summary>
public sealed class EntregaPorSucursalTests
{
    private sealed record Escenario(EntornoDeReservas Entorno, Guid Libro, Guid Pedido, Guid DeAyacucho, Guid DeCusco)
    {
        public void ComoEncargadoDeAyacucho() => Entorno.Base.Actor.Establecer(ActorDePrueba.DeEncargadoDe(Entorno.Base.Ayacucho.Id).Actual);

        public Task<Resultado<CierreDeReserva>> Entregar(IReadOnlyList<Guid> lineas, string? clave = null, Guid? pedido = null) =>
            Entorno.Cerrar().Ejecutar(new CerrarReservasEntrada(pedido ?? Pedido, MotivoDeCierreDeReserva.Entrega, lineas, null, clave ?? ClaveNueva()));

        public void NadaCambio()
        {
            Assert.Equal((10, 2), Entorno.FilaDe(Entorno.Base.Ayacucho, Libro));
            Assert.Equal((10, 3), Entorno.FilaDe(Entorno.Base.Cusco, Libro));
            Assert.Empty(Entorno.Reservas.Operaciones);
        }
    }

    /// <summary>Una reserva por sucursal de un cliente con 2 unidades en Ayacucho y 3 en Cusco, confirmada salvo que se pida pendiente.</summary>
    private static async Task<Escenario> Preparar(bool confirmada = true, TipoDeEntregaDeReserva tipo = TipoDeEntregaDeReserva.Recojo)
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Ayacucho.Id, libro, 10, 0);
        entorno.Base.Existencias.Poner(entorno.Base.Cusco.Id, libro, 10, 0);
        var deAyacucho = Linea(libro, 2, sucursal: "ayacucho");
        var deCusco = Linea(libro, 3, sucursal: "cusco");
        var pedido = Guid.NewGuid();
        var region = tipo == TipoDeEntregaDeReserva.Domicilio ? "lima" : null;
        var reservada = await entorno.Reservar().Ejecutar(new ReservarEntrada(pedido, tipo, null, region, [deAyacucho, deCusco], ClaveNueva()));
        Assert.True(reservada.EsExito, reservada.Error?.Clave);

        if (confirmada)
        {
            entorno.Base.Actor.Establecer(ActorDePrueba.DeIntegracion("expiracion-de-reservas").Actual);
            Assert.True((await entorno.Confirmar().Ejecutar(new ConfirmarReservasEntrada(pedido, ClaveNueva()))).EsExito);
        }

        var escenario = new Escenario(entorno, libro, pedido, deAyacucho.LineaId, deCusco.LineaId);
        escenario.ComoEncargadoDeAyacucho();
        return escenario;
    }

    private static void EsProhibidoConSuEvento(Escenario escenario, Resultado<CierreDeReserva> resultado, int eventos = 1)
    {
        Assert.False(resultado.EsExito, "Se esperaba un rechazo.");
        Assert.Equal((CategoriaDeError.Prohibido, "prohibido"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(eventos, escenario.Entorno.Base.Denegaciones.Eventos.Count(d => d.Motivo == "fuera-de-sucursal"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-46")]
    public async Task ElEncargadoEntregaSuLinea_ElFisicoBajaSoloEnSuSucursal_YLaOtraSigueConfirmada()
    {
        var escenario = await Preparar();

        var resultado = await escenario.Entregar([escenario.DeAyacucho]);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.False(Assert.Single(resultado.Valor.Lineas).YaCerrada);
        Assert.Equal((8, 0), escenario.Entorno.FilaDe(escenario.Entorno.Base.Ayacucho, escenario.Libro));
        Assert.Equal((10, 3), escenario.Entorno.FilaDe(escenario.Entorno.Base.Cusco, escenario.Libro));
        var lineas = escenario.Entorno.Reservas.Guardadas.Single().Lineas.ToDictionary(l => l.LineaId, l => l.Estado);
        Assert.Equal((EstadoDeLinea.Cerrada, EstadoDeLinea.Confirmada), (lineas[escenario.DeAyacucho], lineas[escenario.DeCusco]));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-46")]
    [InlineData("ajena")]
    [InlineData("ambas")]
    [InlineData("inexistente")]
    [InlineData("propia-e-inexistente")]
    [InlineData("vacia")]
    [InlineData("repetida")]
    [InlineData("identificador-vacio")]
    [InlineData("demasiadas")]
    public async Task SiAlgunaLineaNoEsDeSuSucursalOLaListaEsInvalida_RecibeElMismo403SinCambios(string caso)
    {
        var escenario = await Preparar();
        IReadOnlyList<Guid> lineas = caso switch
        {
            "ajena" => [escenario.DeCusco],
            "ambas" => [escenario.DeAyacucho, escenario.DeCusco],
            "inexistente" => [Guid.NewGuid()],
            "propia-e-inexistente" => [escenario.DeAyacucho, Guid.NewGuid()],
            "vacia" => [],
            "repetida" => [escenario.DeAyacucho, escenario.DeAyacucho],
            "identificador-vacio" => [Guid.Empty],
            _ => [.. Enumerable.Repeat(0, 31).Select(_ => Guid.NewGuid())],
        };

        var resultado = await escenario.Entregar(lineas);

        EsProhibidoConSuEvento(escenario, resultado);
        escenario.NadaCambio();
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-40")]
    public async Task ElRechazoLlegaAntesDeLaClave_AunqueEsaClaveTengaUnResultadoGuardado()
    {
        var escenario = await Preparar();
        var clave = ClaveNueva();
        escenario.Entorno.Base.Actor.Establecer(ActorDePrueba.DeEncargadoDe(escenario.Entorno.Base.Cusco.Id).Actual);
        Assert.True((await escenario.Entregar([escenario.DeCusco], clave)).EsExito);
        escenario.ComoEncargadoDeAyacucho();

        var repetida = await escenario.Entregar([escenario.DeCusco], clave);
        var conOtraCarga = await escenario.Entregar([escenario.DeAyacucho, escenario.DeCusco], clave);

        EsProhibidoConSuEvento(escenario, repetida, eventos: 2);
        EsProhibidoConSuEvento(escenario, conOtraCarga, eventos: 2);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-46")]
    public async Task ElRechazoLlegaAntesDelEstado_UnaLineaAjenaYaCerradaNoRespondeYaCerrada()
    {
        var escenario = await Preparar();
        escenario.Entorno.Base.Actor.Establecer(ActorDePrueba.DeEncargadoDe(escenario.Entorno.Base.Cusco.Id).Actual);
        Assert.True((await escenario.Entregar([escenario.DeCusco])).EsExito);
        escenario.ComoEncargadoDeAyacucho();

        EsProhibidoConSuEvento(escenario, await escenario.Entregar([escenario.DeCusco]));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-46")]
    public async Task UnDomicilioPorSucursalNoLoEntregaUnEncargado_AunqueLaLineaSalgaDeSuSucursal()
    {
        var escenario = await Preparar(tipo: TipoDeEntregaDeReserva.Domicilio);

        EsProhibidoConSuEvento(escenario, await escenario.Entregar([escenario.DeAyacucho]));
        escenario.NadaCambio();
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-46")]
    public async Task UnPedidoSinReservaRespondeElMismo403()
    {
        var escenario = await Preparar();

        EsProhibidoConSuEvento(escenario, await escenario.Entregar([escenario.DeAyacucho], pedido: Guid.NewGuid()));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-46")]
    public async Task SuLineaPendienteEsEstadoInvalido()
    {
        var escenario = await Preparar(confirmada: false);

        var resultado = await escenario.Entregar([escenario.DeAyacucho]);

        Assert.Equal("reserva-estado-invalido", resultado.Error!.Clave);
        escenario.NadaCambio();
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-46")]
    public async Task SuLineaLiberadaEsEstadoInvalido()
    {
        var escenario = await Preparar();
        escenario.Entorno.Base.Actor.Establecer(ActorDePrueba.DeIntegracion("expiracion-de-reservas").Actual);
        Assert.True((await escenario.Entorno.Liberar().Ejecutar(new LiberarReservasEntrada(escenario.Pedido, MotivoDeLiberacionDeReserva.PedidoAnulado, [escenario.DeAyacucho]))).EsExito);
        escenario.ComoEncargadoDeAyacucho();

        var resultado = await escenario.Entregar([escenario.DeAyacucho]);

        Assert.Equal("reserva-estado-invalido", resultado.Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-40")]
    public async Task ConLaClaveMalFormada_ElQueTieneAlcanceRecibeLaValidacionYElQueNo_El403()
    {
        var escenario = await Preparar();

        var propia = await escenario.Entregar([escenario.DeAyacucho], clave: "corta");
        var ajena = await escenario.Entregar([escenario.DeCusco], clave: "corta");

        Assert.Equal(CategoriaDeError.Validacion, propia.Error!.Categoria);
        EsProhibidoConSuEvento(escenario, ajena);
        escenario.NadaCambio();
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-46")]
    public async Task LaAdministracionCentralEntregaCualquierLinea_TambienDeVariasSucursalesALaVez()
    {
        var escenario = await Preparar();
        escenario.Entorno.Base.Actor.Establecer(ActorDePrueba.DeAdministradorCentral().Actual);

        var resultado = await escenario.Entregar([escenario.DeAyacucho, escenario.DeCusco]);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((8, 0), escenario.Entorno.FilaDe(escenario.Entorno.Base.Ayacucho, escenario.Libro));
        Assert.Equal((7, 0), escenario.Entorno.FilaDe(escenario.Entorno.Base.Cusco, escenario.Libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-46")]
    public async Task RepetirSuEntregaDevuelveElResultadoOriginal_YConOtraClaveRespondeYaCerrada()
    {
        var escenario = await Preparar();
        var clave = ClaveNueva();
        Assert.True((await escenario.Entregar([escenario.DeAyacucho], clave)).EsExito);

        var repetida = await escenario.Entregar([escenario.DeAyacucho], clave);
        var conOtraClave = await escenario.Entregar([escenario.DeAyacucho]);

        Assert.False(Assert.Single(repetida.Valor.Lineas).YaCerrada);
        Assert.True(Assert.Single(conOtraClave.Valor.Lineas).YaCerrada);
        Assert.Equal((8, 0), escenario.Entorno.FilaDe(escenario.Entorno.Base.Ayacucho, escenario.Libro));
    }
}
