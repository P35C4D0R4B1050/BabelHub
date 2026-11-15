using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>T22: <c>ConsultarPendientes</c> (SEL-33, AC-SEL-35): cuántos pedidos vivos tiene un titular, solo con la credencial de su alcance y sin datos de las ediciones.</summary>
public sealed class ConsultarPendientesTests
{
    private static EntornoDeReservas Entorno() => new(ActorDePrueba.DeIntegracion("consulta-de-pendientes"));

    [Fact]
    [Trait("Criterio", "AC-SEL-35")]
    public async Task UnaConfirmadaCuenta_UnaPendienteVencidaNo_YUnClienteSinReservasTampoco()
    {
        var e = Entorno();
        var libro = e.Impresa();
        var conConfirmada = Guid.NewGuid();
        var soloVencida = Guid.NewGuid();
        Assert.True(e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Lima, 1)], clienteId: conConfirmada).Confirmar(e.Base.Reloj.Ahora));
        e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Lima, 1)], haceMinutos: 30, clienteId: soloVencida);

        var primero = await e.ConsultarPendientes().Ejecutar(new ConsultarPendientesEntrada(conConfirmada));
        var segundo = await e.ConsultarPendientes().Ejecutar(new ConsultarPendientesEntrada(soloVencida));
        var tercero = await e.ConsultarPendientes().Ejecutar(new ConsultarPendientesEntrada(Guid.NewGuid()));

        Assert.Equal(1, primero.Valor.ReservasActivas);
        Assert.Equal(0, segundo.Valor.ReservasActivas);
        Assert.Equal(0, tercero.Valor.ReservasActivas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-35")]
    public async Task UnaPendienteVigenteCuenta()
    {
        var e = Entorno();
        var cliente = Guid.NewGuid();
        e.Pendiente(Guid.NewGuid(), [(e.Impresa(), e.Base.Lima, 1)], clienteId: cliente);

        Assert.Equal(1, (await e.ConsultarPendientes().Ejecutar(new ConsultarPendientesEntrada(cliente))).Valor.ReservasActivas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-35")]
    public async Task CadaPedidoCuentaUnaVez_AunqueTengaVariasLineas()
    {
        var e = Entorno();
        var cliente = Guid.NewGuid();
        var reserva = e.Pendiente(Guid.NewGuid(), [(e.Impresa(), e.Base.Lima, 1), (e.Impresa(), e.Base.Cusco, 2)], clienteId: cliente);
        Assert.True(reserva.Confirmar(e.Base.Reloj.Ahora));
        e.Pendiente(Guid.NewGuid(), [(e.Impresa(), e.Base.Lima, 1)], clienteId: cliente);

        Assert.Equal(2, (await e.ConsultarPendientes().Ejecutar(new ConsultarPendientesEntrada(cliente))).Valor.ReservasActivas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-35")]
    public async Task UnPedidoEnTransitoEntreSucursales_SigueContando_PeroNoUnoAnuladoNiLoYaEntregado()
    {
        var e = Entorno();
        var libro = e.Impresa();
        var cliente = Guid.NewGuid();
        var ahora = e.Base.Reloj.Ahora;

        var enTransito = e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Cusco, 1)], clienteId: cliente);
        Assert.True(enTransito.Confirmar(ahora));
        enTransito.Lineas.Single().Cerrar(CausaDeCierre.Traslado, ahora);

        var anulado = e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Cusco, 1)], clienteId: cliente);
        Assert.True(anulado.Confirmar(ahora));
        anulado.Lineas.Single().Cerrar(CausaDeCierre.Traslado, ahora);
        anulado.Lineas.Single().Anular(ahora);

        var entregado = e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Lima, 1)], clienteId: cliente);
        Assert.True(entregado.Confirmar(ahora));
        entregado.Lineas.Single().Cerrar(CausaDeCierre.Entrega, ahora);

        var recreado = e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Cusco, 1)], clienteId: cliente);
        Assert.True(recreado.Confirmar(ahora));
        recreado.Lineas.Single().Cerrar(CausaDeCierre.Traslado, ahora);
        recreado.Lineas.Single().MarcarRecreadaEnDestino();

        Assert.Equal(1, (await e.ConsultarPendientes().Ejecutar(new ConsultarPendientesEntrada(cliente))).Valor.ReservasActivas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-35")]
    public async Task LaRespuestaNoLlevaDatosDeLasEdiciones()
    {
        var propiedades = typeof(PendientesDeTitular).GetProperties().Select(p => p.Name);

        Assert.Equal(["ReservasActivas"], propiedades);
        await Task.CompletedTask;
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-35")]
    [InlineData("cliente")]
    [InlineData("otra-integracion")]
    [InlineData("administracion")]
    [InlineData("sin-actor")]
    public async Task SinLaCredencialDeSuAlcance_SeRechaza_SinLeerNada(string quien)
    {
        var actor = quien switch
        {
            "cliente" => ActorDePrueba.DeCliente(),
            "otra-integracion" => ActorDePrueba.DeIntegracion("expiracion-de-reservas"),
            "administracion" => ActorDePrueba.DeAdministradorCentral(),
            _ => ActorDePrueba.SinContexto(),
        };
        var e = new EntornoDeReservas(actor);
        var cliente = Guid.NewGuid();
        Assert.True(e.Pendiente(Guid.NewGuid(), [(e.Impresa(), e.Base.Lima, 1)], clienteId: cliente).Confirmar(e.Base.Reloj.Ahora));
        e.Base.Bitacora.Llamadas.Clear();

        var resultado = await e.ConsultarPendientes().Ejecutar(new ConsultarPendientesEntrada(cliente));

        Assert.Equal(quien == "sin-actor" ? CategoriaDeError.NoAutenticado : CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.DoesNotContain(e.Base.Bitacora.Llamadas, l => l.StartsWith("reservas.", StringComparison.Ordinal));
        Assert.Single(e.Base.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-35")]
    public async Task UnTitularVacio_EsValidacion()
    {
        var resultado = await Entorno().ConsultarPendientes().Ejecutar(new ConsultarPendientesEntrada(Guid.Empty));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-35")]
    public void ElIdentificadorDelTitularNuncaApareceEnLaCadena()
    {
        var titular = Guid.NewGuid();

        Assert.DoesNotContain(titular.ToString(), new ConsultarPendientesEntrada(titular).ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
