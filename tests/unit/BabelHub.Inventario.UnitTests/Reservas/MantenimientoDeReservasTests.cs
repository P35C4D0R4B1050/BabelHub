using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>T25: el mantenimiento de las reservas (SEL-08, SEL-35, plan §5): solo el sistema lo pide y cada tarea aplica su plazo con el reloj controlado.</summary>
public sealed class MantenimientoDeReservasTests
{
    private static EntornoDeReservas Entorno() => new(ActorDePrueba.DelSistema());

    private static AutorizadorDeReservas Autorizador(EntornoDeReservas e) => e.Autorizador;

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task LaBusquedaDeVencidas_DevuelveLosPedidosConLineasVencidas_HastaElMaximo()
    {
        var e = Entorno();
        var libro = e.Impresa();
        var vencidos = Enumerable.Range(0, 3).Select(_ => e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Lima, 1)], haceMinutos: 30).PedidoId).ToList();
        e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Lima, 1)], haceMinutos: 1);

        var todos = await new BuscarPedidosConVencidas(Autorizador(e), e.Reservas, e.Base.Reloj).Ejecutar(new BuscarPedidosConVencidasEntrada(10));
        var dos = await new BuscarPedidosConVencidas(Autorizador(e), e.Reservas, e.Base.Reloj).Ejecutar(new BuscarPedidosConVencidasEntrada(2));

        Assert.Equal(vencidos.Order(), todos.Valor.PedidoIds.Order());
        Assert.Equal(2, dos.Valor.PedidoIds.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task LaBusquedaConUnMaximoInvalido_EsValidacion()
    {
        var e = Entorno();

        var resultado = await new BuscarPedidosConVencidas(Autorizador(e), e.Reservas, e.Base.Reloj).Ejecutar(new BuscarPedidosConVencidasEntrada(0));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task CadaPurga_AplicaSuPlazoContraElRelojDeLaAplicacion()
    {
        var e = Entorno();
        var ahora = e.Base.Reloj.Ahora;
        e.Reservas.FilasDeMantenimiento = 4;

        var huellas = await new PurgarHuellasDeOrigen(Autorizador(e), e.Reservas, e.Base.Reloj).Ejecutar(new PurgarHuellasDeOrigenEntrada());
        var registros = await new PurgarRegistrosCaducados(Autorizador(e), e.Reservas, e.Base.Reloj).Ejecutar(new PurgarRegistrosCaducadosEntrada());

        Assert.Equal(4, huellas.Valor.Filas);
        Assert.Equal(new ResultadoDeMantenimiento(4, 4), registros.Valor);
        Assert.Equal(
            [("huellas", ahora.AddHours(-24)), ("operaciones", ahora.AddDays(-30)), ("bajas", ahora.AddDays(-30))],
            e.Reservas.Mantenimiento);
    }

    private static ReservaDePedido Antigua(EntornoDeReservas e, Guid cliente, int años = 3)
    {
        var reserva = e.Pendiente(Guid.NewGuid(), [(e.Impresa(), e.Base.Lima, 1)], clienteId: cliente, haceMinutos: 60 * 24 * 365 * años);
        reserva.Lineas.Single().Expirar(e.Base.Reloj.Ahora.AddYears(-años));
        return reserva;
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task LaDepuracion_AnonimizaConElMismoIdentificadorDeLaBaja_SoloLoQueLleva2AñosQuieto()
    {
        var e = Entorno();
        var generador = new GeneradorAnonimoDoble();
        var antiguo = Guid.NewGuid();
        var reciente = Guid.NewGuid();
        var viva = Guid.NewGuid();
        var vieja = Antigua(e, antiguo);
        var casiDosAños = Antigua(e, reciente, años: 1);
        e.Pendiente(Guid.NewGuid(), [(e.Impresa(), e.Base.Lima, 1)], clienteId: viva, haceMinutos: 60 * 24 * 365 * 3);
        var caso = new DepurarReservasAntiguas(Autorizador(e), e.Reservas, generador, e.Base.Reloj);

        var resultado = await caso.Ejecutar(new DepurarReservasAntiguasEntrada(100));

        Assert.Equal(1, resultado.Valor.Filas);
        Assert.Equal(generador.Generar(antiguo), e.Reservas.Guardadas.Single(r => r.Id == vieja.Id).ClienteId);
        Assert.Equal(reciente, e.Reservas.Guardadas.Single(r => r.Id == casiDosAños.Id).ClienteId);
        Assert.Equal(viva, e.Reservas.Guardadas.Single(r => r.ClienteId == viva).ClienteId);
        Assert.Equal(e.Base.Reloj.Ahora.AddYears(-2), e.Reservas.Mantenimiento.Single().Limite);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task LaDepuracion_EsRepetible_EsAcotada_YNoVuelveACalcularLoYaAnonimo()
    {
        var e = Entorno();
        var generador = new GeneradorAnonimoDoble();
        var clientes = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        foreach (var cliente in clientes)
        {
            Antigua(e, cliente);
        }

        var caso = new DepurarReservasAntiguas(Autorizador(e), e.Reservas, generador, e.Base.Reloj);

        var primera = await caso.Ejecutar(new DepurarReservasAntiguasEntrada(2));
        var segunda = await caso.Ejecutar(new DepurarReservasAntiguasEntrada(2));
        var tercera = await caso.Ejecutar(new DepurarReservasAntiguasEntrada(2));

        Assert.Equal([2, 1, 0], new[] { primera.Valor.Filas, segunda.Valor.Filas, tercera.Valor.Filas });
        Assert.Equal(clientes.Select(generador.Generar).Order(), e.Reservas.Guardadas.Select(r => r.ClienteId).Order());
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task LaDepuracionConUnMaximoInvalido_EsValidacion()
    {
        var e = Entorno();

        var resultado = await new DepurarReservasAntiguas(Autorizador(e), e.Reservas, new GeneradorAnonimoDoble(), e.Base.Reloj).Ejecutar(new DepurarReservasAntiguasEntrada(0));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-08")]
    [InlineData("cliente")]
    [InlineData("integracion")]
    [InlineData("administracion")]
    [InlineData("sin-actor")]
    public async Task SinElActorSistema_TodoElMantenimientoSeRechaza_SinTocarNada(string quien)
    {
        var actor = quien switch
        {
            "cliente" => ActorDePrueba.DeCliente(),
            "integracion" => ActorDePrueba.DeIntegracion("expiracion-de-reservas"),
            "administracion" => ActorDePrueba.DeAdministradorCentral(),
            _ => ActorDePrueba.SinContexto(),
        };
        var e = new EntornoDeReservas(actor);
        var esperado = quien == "sin-actor" ? CategoriaDeError.NoAutenticado : CategoriaDeError.Prohibido;

        var resultados = new[]
        {
            (await new BuscarPedidosConVencidas(Autorizador(e), e.Reservas, e.Base.Reloj).Ejecutar(new BuscarPedidosConVencidasEntrada(10))).Error,
            (await new PurgarHuellasDeOrigen(Autorizador(e), e.Reservas, e.Base.Reloj).Ejecutar(new PurgarHuellasDeOrigenEntrada())).Error,
            (await new PurgarRegistrosCaducados(Autorizador(e), e.Reservas, e.Base.Reloj).Ejecutar(new PurgarRegistrosCaducadosEntrada())).Error,
            (await new DepurarReservasAntiguas(Autorizador(e), e.Reservas, new GeneradorAnonimoDoble(), e.Base.Reloj).Ejecutar(new DepurarReservasAntiguasEntrada(10))).Error,
        };

        Assert.All(resultados, error => Assert.Equal(esperado, error!.Categoria));
        Assert.Empty(e.Reservas.Mantenimiento);
        Assert.DoesNotContain(e.Base.Bitacora.Llamadas, l => l.StartsWith("reservas.", StringComparison.Ordinal));
        Assert.Equal(4, e.Base.Denegaciones.Eventos.Count);
    }
}
