using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.UnitTests.Fuentes;
using BabelHub.Inventario.UnitTests.Plataforma;
using static BabelHub.Inventario.UnitTests.Reservas.EntornoDeReservas;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>
/// T44: la alerta de concentración (SEL-47, D18): el contador por edición y sucursal en una ventana de 15 minutos y el decorador exterior a la transacción, que cuenta solo las reservas por sucursal nuevas y nunca
/// altera la respuesta.
/// </summary>
public sealed class ConcentracionDeReservasTests
{
    private const string Motivo = "concentracion-de-reservas";

    /// <summary>La reserva de dentro: responde lo que se le pida, sin tocar nada.</summary>
    private sealed class ReservaDeDentro : ICasoDeUso<ReservarEntrada, ReservaDelPedido>
    {
        public bool EsRepeticion { get; set; }

        public ErrorDeAplicacion? Error { get; set; }

        public List<CancellationToken> Cancelaciones { get; } = [];

        public Task<Resultado<ReservaDelPedido>> Ejecutar(ReservarEntrada entrada, CancellationToken cancelacion = default)
        {
            Cancelaciones.Add(cancelacion);
            return Task.FromResult(Error is { } error
                ? Resultado<ReservaDelPedido>.Fallo(error)
                : Resultado<ReservaDelPedido>.Exito(new ReservaDelPedido(entrada.PedidoId, EstadoDeReserva.Pendiente, DateTimeOffset.UnixEpoch, null, [], [], EsRepeticion)));
        }
    }

    private sealed class RegistroEspia : IRegistroDeConcentracion
    {
        public bool Falla { get; set; }

        public List<(Guid Edicion, string Sucursal, int Unidades)> Avisos { get; } = [];

        public void Registrar(Guid edicionId, string codigoDeSucursal, int unidades)
        {
            if (Falla)
            {
                throw new InvalidOperationException("El registro no responde.");
            }

            Avisos.Add((edicionId, codigoDeSucursal, unidades));
        }
    }

    private sealed class AlertasDeConcentracionEspia : IAlertas
    {
        public bool Falla { get; set; }

        public Action? AlPublicar { get; set; }

        public List<string> Tecnicas { get; } = [];

        public List<CancellationToken> Cancelaciones { get; } = [];

        public Task PublicarTecnica(CodigoDeAlerta motivo, CancellationToken cancelacion = default)
        {
            Cancelaciones.Add(cancelacion);
            AlPublicar?.Invoke();
            if (Falla)
            {
                throw new InvalidOperationException("Las alertas no responden.");
            }

            Tecnicas.Add(motivo.Valor);
            return Task.CompletedTask;
        }

        public Task PublicarDeSeguridad(CodigoDeAlerta motivo, CancellationToken cancelacion = default) => throw new NotSupportedException("La concentración es una alerta técnica.");
    }

    private sealed class Escenario
    {
        public Escenario(int maximo = 100)
        {
            Contador = new ContadorDeConcentracion(Reloj, maximo);
            Caso = new ConcentracionDeReservas(Dentro, Contador, Alertas, Registro);
        }

        public RelojFijo Reloj { get; } = new(EntornoDeFuentes.Ahora);

        public ReservaDeDentro Dentro { get; } = new();

        public AlertasDeConcentracionEspia Alertas { get; } = new();

        public RegistroEspia Registro { get; } = new();

        public ContadorDeConcentracion Contador { get; }

        public ConcentracionDeReservas Caso { get; }

        public Guid Libro { get; } = Guid.NewGuid();

        public Task<Resultado<ReservaDelPedido>> Reservar(string sucursal, int unidades, Guid? libro = null) =>
            Reservar(Linea(libro ?? Libro, unidades, sucursal: sucursal));

        public Task<Resultado<ReservaDelPedido>> Reservar(params LineaSolicitada[] lineas) =>
            Caso.Ejecutar(new ReservarEntrada(Guid.NewGuid(), TipoDeEntregaDeReserva.Recojo, null, null, lineas, ClaveNueva()));

        /// <summary>Aparta las unidades en reservas de hasta 20 por línea, como las admite SEL-40.</summary>
        public async Task Apartar(string sucursal, int unidades, Guid? libro = null)
        {
            for (var quedan = unidades; quedan > 0; quedan -= 20)
            {
                await Reservar(sucursal, Math.Min(20, quedan), libro);
            }
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-51")]
    public async Task CienUnidadesNoAlertan_YUnaMasDaExactamenteUnaAlertaConLaEdicionLaSucursalYLasUnidades()
    {
        var escenario = new Escenario();
        await escenario.Apartar("cusco", 100);
        Assert.Empty(escenario.Alertas.Tecnicas);
        Assert.Empty(escenario.Registro.Avisos);

        await escenario.Reservar("cusco", 1);

        Assert.Equal([Motivo], escenario.Alertas.Tecnicas);
        Assert.Equal([(escenario.Libro, "cusco", 101)], escenario.Registro.Avisos);

        await escenario.Apartar("cusco", 60);
        Assert.Single(escenario.Alertas.Tecnicas);
        Assert.Single(escenario.Registro.Avisos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-51")]
    public async Task OtraSucursalYOtraEdicionCuentanAparte()
    {
        var escenario = new Escenario();
        var otro = Guid.NewGuid();

        await escenario.Apartar("cusco", 60);
        await escenario.Apartar("ayacucho", 60);
        await escenario.Apartar("cusco", 60, otro);
        Assert.Empty(escenario.Alertas.Tecnicas);

        await escenario.Apartar("ayacucho", 41);
        await escenario.Apartar("cusco", 41, otro);

        Assert.Equal([(escenario.Libro, "ayacucho", 101), (otro, "cusco", 101)], escenario.Registro.Avisos);
        Assert.Equal(2, escenario.Alertas.Tecnicas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-51")]
    public async Task LasLineasDeUnaEdicionYSucursalEnUnaMismaReservaSeSuman()
    {
        var escenario = new Escenario(maximo: 30);

        await escenario.Reservar(Linea(escenario.Libro, 20, sucursal: "cusco"), Linea(escenario.Libro, 11, sucursal: "cusco"));

        Assert.Equal([(escenario.Libro, "cusco", 31)], escenario.Registro.Avisos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-51")]
    public async Task PasadaLaVentanaSeCuentaDesdeCero()
    {
        var escenario = new Escenario();
        await escenario.Apartar("cusco", 80);
        escenario.Reloj.Ahora = escenario.Reloj.Ahora.AddMinutes(15);

        await escenario.Apartar("cusco", 80);

        Assert.Empty(escenario.Alertas.Tecnicas);
        await escenario.Apartar("cusco", 21);
        Assert.Single(escenario.Alertas.Tecnicas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-51")]
    public async Task LaVentanaEsFijaDesdeLaPrimeraUnidadContada()
    {
        var escenario = new Escenario(maximo: 10);
        await escenario.Reservar("cusco", 5);
        escenario.Reloj.Ahora = escenario.Reloj.Ahora.AddMinutes(14).AddSeconds(59);

        await escenario.Reservar("cusco", 6);

        Assert.Single(escenario.Alertas.Tecnicas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-51")]
    public async Task UnaReservaRechazadaORepetidaNoCuenta()
    {
        var escenario = new Escenario(maximo: 10);
        escenario.Dentro.Error = new ErrorDeAplicacion(CategoriaDeError.Conflicto, "stock-insuficiente");
        await escenario.Reservar("cusco", 20);
        escenario.Dentro.Error = null;
        escenario.Dentro.EsRepeticion = true;
        await escenario.Reservar("cusco", 20);

        Assert.Empty(escenario.Alertas.Tecnicas);
        escenario.Dentro.EsRepeticion = false;
        await escenario.Reservar("cusco", 10);
        Assert.Empty(escenario.Alertas.Tecnicas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-51")]
    public async Task UnaReservaSinSucursalPorLineaNoCuenta()
    {
        var escenario = new Escenario(maximo: 1);

        await escenario.Caso.Ejecutar(ARecojo("lima", [Linea(escenario.Libro, 20)]));

        Assert.Empty(escenario.Alertas.Tecnicas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-51")]
    public async Task UnReemplazoCuentaTodoLoQueAparta()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Cusco.Id, libro, 30, 0);
        var alertas = new AlertasDeConcentracionEspia();
        var registro = new RegistroEspia();
        var caso = new ConcentracionDeReservas(entorno.Reservar(), new ContadorDeConcentracion(entorno.Base.Reloj, 10), alertas, registro);
        var pedido = Guid.NewGuid();
        ReservarEntrada Pedir(int unidades) => new(pedido, TipoDeEntregaDeReserva.Recojo, null, null, [Linea(libro, unidades, sucursal: "cusco")], ClaveNueva());

        Assert.True((await caso.Ejecutar(Pedir(6))).EsExito);
        Assert.Empty(alertas.Tecnicas);
        Assert.True((await caso.Ejecutar(Pedir(5))).EsExito);

        Assert.Equal([(libro, "cusco", 11)], registro.Avisos);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-52")]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SiPublicarORegistrarElAvisoFalla_LaReservaSeAceptaConLaMismaRespuesta(bool fallaLaAlerta, bool fallaElRegistro)
    {
        var escenario = new Escenario(maximo: 10);
        escenario.Alertas.Falla = fallaLaAlerta;
        escenario.Registro.Falla = fallaElRegistro;

        var resultado = await escenario.Reservar("cusco", 11);

        Assert.True(resultado.EsExito);
        Assert.Equal((EstadoDeReserva.Pendiente, false), (resultado.Valor.Estado, resultado.Valor.EsRepeticion));
        // Lo que no falló se hizo igual.
        Assert.Equal(fallaElRegistro ? 0 : 1, escenario.Registro.Avisos.Count);
        Assert.Equal(fallaLaAlerta ? 0 : 1, escenario.Alertas.Tecnicas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-52")]
    public async Task ElAvisoNoUsaLaCancelacionDeLaSolicitud()
    {
        var escenario = new Escenario(maximo: 10);
        using var solicitud = new CancellationTokenSource();
        escenario.Alertas.AlPublicar = solicitud.Cancel;

        var resultado = await escenario.Caso.Ejecutar(
            new ReservarEntrada(Guid.NewGuid(), TipoDeEntregaDeReserva.Recojo, null, null, [Linea(escenario.Libro, 11, sucursal: "cusco")], ClaveNueva()), solicitud.Token);

        Assert.True(resultado.EsExito);
        Assert.False(Assert.Single(escenario.Alertas.Cancelaciones).CanBeCanceled);
        Assert.Equal(solicitud.Token, Assert.Single(escenario.Dentro.Cancelaciones));
        Assert.Single(escenario.Registro.Avisos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-51")]
    public void ElContadorNoPierdeUnidadesNiAvisaDosVecesConSolicitudesSimultaneas()
    {
        var contador = new ContadorDeConcentracion(new RelojFijo(EntornoDeFuentes.Ahora), 999);
        var edicion = Guid.NewGuid();
        var avisos = 0;

        Parallel.For(0, 1000, _ =>
        {
            if (contador.Contar(edicion, "cusco", 1) is not null)
            {
                Interlocked.Increment(ref avisos);
            }
        });

        Assert.Equal(1, avisos);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ElMaximoEsMayorQueCero(int maximo) => Assert.Throws<ArgumentOutOfRangeException>(() => new ContadorDeConcentracion(new RelojFijo(EntornoDeFuentes.Ahora), maximo));

    [Fact]
    public void ElMaximoPorDefectoEsCien() => Assert.Equal(100, ContadorDeConcentracion.MaximoPorDefecto);
}
