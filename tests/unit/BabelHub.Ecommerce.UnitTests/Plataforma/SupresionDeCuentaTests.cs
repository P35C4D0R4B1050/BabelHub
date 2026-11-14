using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

public sealed class SupresionDeCuentaTests
{
    private const string Cuenta = "3f2a9c41b8d74e6a9d015c7e2b4f8a60";
    private static readonly DateTimeOffset Inicio = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly RegistroDeSupresionesEnMemoria _registro = new();
    private readonly RelojFijo _reloj = new(Inicio);
    private readonly AgregarIntencionDeSupresion _intencion;
    private readonly CerrarSupresion _cierre;

    public SupresionDeCuentaTests()
    {
        _intencion = new AgregarIntencionDeSupresion(_registro, _reloj);
        _cierre = new CerrarSupresion(_registro, _reloj);
    }

    private static SupresionDeCuenta Cancelar(string id = Cuenta) => new(id, ClaseDeSupresion.Cancelacion);

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public async Task LaIntencion_SeAgregaConLaFechaDeAhoraYSinDatosPersonales()
    {
        var resultado = await _intencion.Ejecutar(Cancelar());

        Assert.True(resultado.EsExito);
        var entrada = resultado.Valor;
        Assert.Equal(new EntradaDeSupresionDto(Cuenta, ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Intencion, Inicio, entrada.Suma, false), entrada);
        Assert.Equal([$"{Cuenta}/Cancelacion-Intencion"], _registro.Escrituras);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public async Task LaIntencionRepetida_EsExitoYConservaLaOriginalSinSobrescribirla()
    {
        await _intencion.Ejecutar(Cancelar());
        _reloj.Ahora = Inicio.AddHours(2);

        var repetida = await _intencion.Ejecutar(Cancelar());

        Assert.True(repetida.EsExito);
        Assert.True(repetida.Valor.YaExistia);
        Assert.Equal(Inicio, repetida.Valor.Fecha);
        Assert.Single(_registro.Escrituras);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public async Task SiElRegistroNoResponde_LaIntencionFallaComoNoDisponibleYNoSeEscribeNada()
    {
        _registro.NoDisponible = true;

        var resultado = await _intencion.Ejecutar(Cancelar());

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal("registro-de-supresiones-no-disponible", resultado.Error.Clave);
        Assert.Empty(_registro.Escrituras);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public async Task ElCierre_SeAgregaConSuPropiaFechaDespuesDeLaIntencion()
    {
        await _intencion.Ejecutar(Cancelar());
        _reloj.Ahora = Inicio.AddMinutes(3);

        var resultado = await _cierre.Ejecutar(Cancelar());

        Assert.True(resultado.EsExito);
        Assert.Equal(EtapaDeSupresion.Cierre, resultado.Valor.Etapa);
        Assert.Equal(Inicio.AddMinutes(3), resultado.Valor.Fecha);
        Assert.Equal([$"{Cuenta}/Cancelacion-Intencion", $"{Cuenta}/Cancelacion-Cierre"], _registro.Escrituras);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public async Task SinIntencionPrevia_ElCierreSeRechazaYNoSeEscribeNada()
    {
        var resultado = await _cierre.Ejecutar(Cancelar());

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.ReglaDeNegocio, resultado.Error!.Categoria);
        Assert.Equal("supresion-sin-intencion", resultado.Error.Clave);
        Assert.Empty(_registro.Escrituras);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public async Task LaIntencionDeOtraClaseONoCuentaParaElCierre()
    {
        await _intencion.Ejecutar(new SupresionDeCuenta(Cuenta, ClaseDeSupresion.Cancelacion));

        var anonimizar = await _cierre.Ejecutar(new SupresionDeCuenta(Cuenta, ClaseDeSupresion.Anonimizacion));
        var otraCuenta = await _cierre.Ejecutar(Cancelar("9b1e7d52c4a8436fa0e3d6b7c8f12a45"));

        Assert.Equal("supresion-sin-intencion", anonimizar.Error!.Clave);
        Assert.Equal("supresion-sin-intencion", otraCuenta.Error!.Clave);
        Assert.Single(_registro.Escrituras);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public async Task ElCierreRepetido_EsExito()
    {
        await _intencion.Ejecutar(Cancelar());
        var primero = await _cierre.Ejecutar(Cancelar());
        _reloj.Ahora = Inicio.AddDays(1);

        var repetido = await _cierre.Ejecutar(Cancelar());

        Assert.True(repetido.EsExito);
        Assert.True(repetido.Valor.YaExistia);
        Assert.Equal(primero.Valor.Fecha, repetido.Valor.Fecha);
        Assert.Equal(2, _registro.Escrituras.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public async Task SiElCierreNoPuedeEscribirse_FallaComoNoDisponibleYLaIntencionQuedaIntacta()
    {
        await _intencion.Ejecutar(Cancelar());
        _registro.NoDisponible = true;

        var resultado = await _cierre.Ejecutar(Cancelar());

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal([$"{Cuenta}/Cancelacion-Intencion"], _registro.Escrituras);

        // Al volver el registro, el reintento del cierre (de Identidad, PLA-41) lo completa sin rehacer la intención.
        _registro.NoDisponible = false;
        var reintento = await _cierre.Ejecutar(Cancelar());
        Assert.True(reintento.EsExito);
        Assert.Equal([$"{Cuenta}/Cancelacion-Intencion", $"{Cuenta}/Cancelacion-Cierre"], _registro.Escrituras);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public async Task SiLaSupresionFallaTrasLaIntencion_NoQuedaCierre()
    {
        // El flujo de Identidad: intención, supresión (que falla) y solo entonces el cierre, que no se llama.
        await _intencion.Ejecutar(Cancelar());
        var supresionCompletada = false;
        if (supresionCompletada)
        {
            await _cierre.Ejecutar(Cancelar());
        }

        var entradas = (await _registro.LeerConMetadatos()).Select(e => e.Entrada).ToList();

        Assert.Single(entradas);
        Assert.Equal(EtapaDeSupresion.Intencion, entradas[0].Etapa);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-37")]
    [InlineData("lector@correo.pe")]
    [InlineData("")]
    [InlineData("corto")]
    [InlineData("Ana Quispe Huamán 12345678")]
    public async Task UnIdentificadorQueNoEsElInternoSeRechazaSinTocarElRegistro(string identificador)
    {
        var deIntencion = await _intencion.Ejecutar(Cancelar(identificador));
        var deCierre = await _cierre.Ejecutar(Cancelar(identificador));

        foreach (var resultado in new[] { deIntencion, deCierre })
        {
            Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
            Assert.Equal("identificador-opaco-invalido", resultado.Error.Clave);
            var campo = Assert.Single(resultado.Error.Errores);
            Assert.Equal("IdentificadorOpaco", campo.Campo);
            Assert.DoesNotContain(identificador.Length > 0 ? identificador : "\0", campo.Clave, StringComparison.Ordinal);
        }

        Assert.Empty(_registro.Escrituras);
        Assert.Equal(0, _registro.Consultas);
    }

    [Fact]
    public async Task LaAnonimizacionTieneSuPropiaIntencionYCierre()
    {
        var anonimizar = new SupresionDeCuenta(Cuenta, ClaseDeSupresion.Anonimizacion);

        await _intencion.Ejecutar(anonimizar);
        var cierre = await _cierre.Ejecutar(anonimizar);

        Assert.True(cierre.EsExito);
        Assert.Equal([$"{Cuenta}/Anonimizacion-Intencion", $"{Cuenta}/Anonimizacion-Cierre"], _registro.Escrituras);
    }

    // ---------- Pasos de restauración ----------

    private sealed class PasoDePrueba(int orden, string nombre) : IPasoDeRestauracion
    {
        public int Orden { get; } = orden;

        public string Nombre { get; } = nombre;

        public Task<ResultadoDePaso> Ejecutar(CancellationToken cancelacion = default) => Task.FromResult(ResultadoDePaso.Correcto);
    }

    [Fact]
    public void LosPasosDeRestauracion_SeOrdenanPorModuloPlataformaIdentidadYAuditoria()
    {
        var pasos = new IPasoDeRestauracion[]
        {
            new PasoDePrueba(OrdenDePasoDeRestauracion.Auditoria, "depurar"),
            new PasoDePrueba(OrdenDePasoDeRestauracion.Identidad + 1, "rotar-claves"),
            new PasoDePrueba(OrdenDePasoDeRestauracion.Plataforma, "verificar-registro"),
            new PasoDePrueba(OrdenDePasoDeRestauracion.Identidad, "reaplicar"),
        };

        var orden = pasos.OrderBy(p => p.Orden).Select(p => p.Nombre).ToList();

        Assert.Equal(["verificar-registro", "reaplicar", "rotar-claves", "depurar"], orden);
        Assert.True(OrdenDePasoDeRestauracion.Plataforma < OrdenDePasoDeRestauracion.Identidad);
        Assert.True(OrdenDePasoDeRestauracion.Identidad < OrdenDePasoDeRestauracion.Auditoria);
    }
}
