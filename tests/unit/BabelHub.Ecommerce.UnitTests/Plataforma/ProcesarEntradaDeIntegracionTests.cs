using System.Text;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

public sealed class ProcesarEntradaDeIntegracionTests
{
    private const string Resumen = "k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE";
    private const string Proveedor = "pasarela-de-pago";
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed class VerificadorDeFirmaFalso : IVerificadorDeFirma
    {
        public int Llamadas { get; private set; }

        public static string Firmar(string proveedor, string marca, byte[] cuerpo) => $"v1={proveedor}.{marca}.{Encoding.UTF8.GetString(cuerpo)}";

        public bool Verificar(string proveedor, string marcaDeTiempo, ReadOnlyMemory<byte> cuerpo, string firma)
        {
            Llamadas++;
            return firma == Firmar(proveedor, marcaDeTiempo, cuerpo.ToArray());
        }
    }

    private sealed class AlmacenDeMemoria : IAlmacenDeEntradasProcesadas
    {
        private readonly Dictionary<string, ResultadoMinimo?> _entradas = [];

        public int Reclamos { get; private set; }

        public Task<ReclamoDeEntrada> Reclamar(string proveedor, string idDeEntrada, CancellationToken cancelacion = default)
        {
            Reclamos++;
            var clave = $"{proveedor}|{idDeEntrada}";
            if (!_entradas.TryGetValue(clave, out var resultado))
            {
                _entradas[clave] = null;
                return Task.FromResult(new ReclamoDeEntrada(EstadoDeEntrada.Nueva));
            }

            return Task.FromResult(resultado is null
                ? new ReclamoDeEntrada(EstadoDeEntrada.EnCurso)
                : new ReclamoDeEntrada(EstadoDeEntrada.Procesada, resultado));
        }

        public Task Completar(string proveedor, string idDeEntrada, ResultadoMinimo resultado, CancellationToken cancelacion = default)
        {
            _entradas[$"{proveedor}|{idDeEntrada}"] = resultado;
            return Task.CompletedTask;
        }
    }

    private readonly RelojFijo _reloj = new(Ahora);
    private readonly PublicadorDeEventosEspia _eventos = new();
    private readonly VerificadorDeFirmaFalso _firmas = new();
    private readonly AlmacenDeMemoria _almacen = new();
    private readonly ProcesarEntradaDeIntegracion _procesador;

    public ProcesarEntradaDeIntegracionTests()
    {
        var origen = new OrigenFijo(new OrigenDeSolicitud(HuellaDeOrigen.Crear(Resumen, "v1"), "203.0.113.7"));
        _procesador = new ProcesarEntradaDeIntegracion(
            new OpcionesDeEntradasDeIntegracion([Proveedor], topeDelCuerpoEnBytes: 1024),
            _firmas,
            _almacen,
            origen,
            _eventos,
            _reloj);
    }

    private static EntradaDeIntegracion Entrada(
        string proveedor = Proveedor,
        string id = "evt-1",
        string cuerpo = "{\"pago\":\"ok\"}",
        string? firma = null,
        DateTimeOffset? marca = null)
    {
        var bytes = Encoding.UTF8.GetBytes(cuerpo);
        var marcaTexto = (marca ?? Ahora).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new EntradaDeIntegracion(proveedor, id, marcaTexto, bytes, firma ?? VerificadorDeFirmaFalso.Firmar(proveedor, marcaTexto, bytes));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public async Task Recibir_ConUnaFirmaValida_AceptaYReclamaLaEntrada()
    {
        var resultado = await _procesador.Recibir(Entrada());

        Assert.Equal(EstadoDeEntradaDeIntegracion.Aceptada, resultado.Estado);
        Assert.Equal(1, _almacen.Reclamos);
        Assert.Empty(_eventos.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public async Task Recibir_ConUnaEntradaRepetida_NoLaProcesaDeNuevoYResponderElMismoResultado()
    {
        await _procesador.Recibir(Entrada());
        await _procesador.Completar(Proveedor, "evt-1", new ResultadoMinimo("pago-confirmado", "pedido-9"));

        var repeticion = await _procesador.Recibir(Entrada());

        Assert.Equal(EstadoDeEntradaDeIntegracion.Repetida, repeticion.Estado);
        Assert.Equal("pedido-9", repeticion.ResultadoGuardado!.IdDelRecurso);
        Assert.Empty(_eventos.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public async Task Recibir_ConUnDuplicadoMientrasLaPrimeraSigueEnCurso_ResponderEnCurso()
    {
        await _procesador.Recibir(Entrada());

        var duplicado = await _procesador.Recibir(Entrada());

        Assert.Equal(EstadoDeEntradaDeIntegracion.EnCurso, duplicado.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public async Task Recibir_ConIdentificadoresDistintos_ProcesaCadaUno()
    {
        Assert.Equal(EstadoDeEntradaDeIntegracion.Aceptada, (await _procesador.Recibir(Entrada(id: "evt-1"))).Estado);
        Assert.Equal(EstadoDeEntradaDeIntegracion.Aceptada, (await _procesador.Recibir(Entrada(id: "evt-2"))).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public async Task Recibir_SinFirma_RechazaYRegistraElEventoConElOrigen()
    {
        var resultado = await _procesador.Recibir(Entrada(firma: string.Empty));

        Assert.Equal(EstadoDeEntradaDeIntegracion.Rechazada, resultado.Estado);
        var evento = Assert.Single(_eventos.Eventos);
        Assert.Equal(TipoDeEventoDeSeguridad.EntradaDeIntegracionRechazada, evento.Tipo);
        Assert.Equal(ProcesarEntradaDeIntegracion.MotivoSinFirma, evento.Motivo);
        Assert.Equal(Proveedor, evento.Proveedor);
        Assert.Equal("203.0.113.7", evento.DireccionCompleta);
        Assert.Equal(0, _almacen.Reclamos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public async Task Recibir_ConUnaFirmaInvalida_RechazaSinReclamarLaEntrada()
    {
        var resultado = await _procesador.Recibir(Entrada(firma: "v1=falsa"));

        Assert.Equal(EstadoDeEntradaDeIntegracion.Rechazada, resultado.Estado);
        Assert.Equal(ProcesarEntradaDeIntegracion.MotivoFirmaInvalida, Assert.Single(_eventos.Eventos).Motivo);
        Assert.Equal(0, _almacen.Reclamos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public async Task Recibir_ConElCuerpoAlteradoDespuesDeFirmar_RechazaPorFirmaInvalida()
    {
        var original = Entrada(cuerpo: "{\"monto\":10}");
        var alterada = new EntradaDeIntegracion(Proveedor, "evt-1", original.MarcaDeTiempo, Encoding.UTF8.GetBytes("{\"monto\":1000}"), original.Firma);

        var resultado = await _procesador.Recibir(alterada);

        Assert.Equal(EstadoDeEntradaDeIntegracion.Rechazada, resultado.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public async Task Recibir_DeUnProveedorFueraDeLaListaBlanca_RechazaYNoRegistraSuNombre()
    {
        var resultado = await _procesador.Recibir(Entrada(proveedor: "<script>alert(1)</script>"));

        Assert.Equal(EstadoDeEntradaDeIntegracion.Rechazada, resultado.Estado);
        var evento = Assert.Single(_eventos.Eventos);
        Assert.Equal(ProcesarEntradaDeIntegracion.MotivoProveedorDesconocido, evento.Motivo);
        Assert.Null(evento.Proveedor);
        Assert.Equal(0, _firmas.Llamadas);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-11")]
    [InlineData(-6, ProcesarEntradaDeIntegracion.MotivoMarcaFueraDeTolerancia)]
    [InlineData(6, ProcesarEntradaDeIntegracion.MotivoMarcaFueraDeTolerancia)]
    public async Task Recibir_ConUnaMarcaFueraDeLaToleranciaDeCincoMinutos_Rechaza(int minutos, string motivo)
    {
        var resultado = await _procesador.Recibir(Entrada(marca: Ahora.AddMinutes(minutos)));

        Assert.Equal(EstadoDeEntradaDeIntegracion.Rechazada, resultado.Estado);
        Assert.Equal(motivo, Assert.Single(_eventos.Eventos).Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public async Task Recibir_ConUnaMarcaEnElBordeDeLaTolerancia_Acepta()
    {
        Assert.Equal(EstadoDeEntradaDeIntegracion.Aceptada, (await _procesador.Recibir(Entrada(id: "a", marca: Ahora.AddMinutes(-5)))).Estado);
        Assert.Equal(EstadoDeEntradaDeIntegracion.Aceptada, (await _procesador.Recibir(Entrada(id: "b", marca: Ahora.AddMinutes(5)))).Estado);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-11")]
    [InlineData("")]
    [InlineData("ayer")]
    [InlineData("-5")]
    [InlineData("99999999999999999999")]
    public async Task Recibir_ConUnaMarcaQueNoEsUnaFecha_RechazaSinLanzar(string marca)
    {
        var entrada = new EntradaDeIntegracion(Proveedor, "evt-1", marca, Encoding.UTF8.GetBytes("{}"), "firma");

        var resultado = await _procesador.Recibir(entrada);

        Assert.Equal(EstadoDeEntradaDeIntegracion.Rechazada, resultado.Estado);
        Assert.Equal(ProcesarEntradaDeIntegracion.MotivoMarcaInvalida, Assert.Single(_eventos.Eventos).Motivo);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-11")]
    [InlineData("")]
    [InlineData("tiene espacio")]
    [InlineData("a/b")]
    public async Task Recibir_ConUnIdentificadorInvalido_Rechaza(string id)
    {
        var resultado = await _procesador.Recibir(Entrada(id: id));

        Assert.Equal(EstadoDeEntradaDeIntegracion.Rechazada, resultado.Estado);
        Assert.Equal(ProcesarEntradaDeIntegracion.MotivoIdInvalido, Assert.Single(_eventos.Eventos).Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public async Task Recibir_ConUnCuerpoMayorAlTope_RechazaSinVerificarLaFirma()
    {
        var resultado = await _procesador.Recibir(Entrada(cuerpo: new string('x', 1025)));

        Assert.Equal(EstadoDeEntradaDeIntegracion.Rechazada, resultado.Estado);
        Assert.Equal(ProcesarEntradaDeIntegracion.MotivoCuerpoDemasiadoGrande, Assert.Single(_eventos.Eventos).Motivo);
        Assert.Equal(0, _firmas.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public async Task Recibir_TodoRechazo_ResponderIgualSinImportarElMotivo()
    {
        var resultados = new[]
        {
            await _procesador.Recibir(Entrada(proveedor: "otro")),
            await _procesador.Recibir(Entrada(firma: "mala")),
            await _procesador.Recibir(Entrada(marca: Ahora.AddHours(-1))),
            await _procesador.Recibir(Entrada(id: "")),
        };

        Assert.All(resultados, r => Assert.Equal(new ResultadoDeEntradaDeIntegracion(EstadoDeEntradaDeIntegracion.Rechazada), r));
        Assert.Equal(4, _eventos.Eventos.Count);
    }

    [Fact]
    public void EventoDeSeguridad_DeIntegracionRechazada_ExigeElMotivo()
    {
        Assert.Throws<ArgumentException>(() => new EventoDeSeguridad(TipoDeEventoDeSeguridad.EntradaDeIntegracionRechazada, Ahora));
    }
}
