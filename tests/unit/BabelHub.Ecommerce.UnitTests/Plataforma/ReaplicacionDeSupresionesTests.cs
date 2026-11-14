using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

public sealed class ReaplicacionDeSupresionesTests
{
    private const string CuentaA = "3f2a9c41b8d74e6a9d015c7e2b4f8a60";
    private const string CuentaB = "9b1e7d52c4a8436fa0e3d6b7c8f12a45";
    private const string CuentaC = "7c3d5e91a2b84f06b1d9e8a7c6f50d32";
    private static readonly DateTimeOffset Respaldo = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed class SellosEnMemoria : ISellosDelRegistro
    {
        public SelloDelRegistroDto? Sello { get; set; }

        public bool NoDisponible { get; set; }

        public Task<SelloDelRegistroDto?> ObtenerAnteriorA(DateTimeOffset instante, CancellationToken cancelacion = default) =>
            NoDisponible ? throw new RegistroDeSupresionesNoDisponibleException() : Task.FromResult(Sello);
    }

    private sealed class ExcepcionesEnMemoria : IExcepcionesDelRegistro
    {
        public ExcepcionDelRegistroDto? Decision { get; set; }

        public Task<ExcepcionDelRegistroDto?> Obtener(DateTimeOffset instanteDelRespaldo, CancellationToken cancelacion = default) => Task.FromResult(Decision);
    }

    /// <summary>Identidad: una cuenta restaurada del respaldo está activa; aplicar la supresión la cierra y repetirlo ya no hace nada.</summary>
    private sealed class AplicadorDeIdentidad : IAplicadorDeSupresion
    {
        private readonly HashSet<(string, ClaseDeSupresion)> _aplicadas = [];

        public HashSet<string> QueFallan { get; } = [];

        public List<string> Llamadas { get; } = [];

        public Task<ResultadoDeAplicacionDeSupresion> Aplicar(string identificadorOpaco, ClaseDeSupresion clase, CancellationToken cancelacion = default)
        {
            Llamadas.Add(identificadorOpaco);
            if (QueFallan.Contains(identificadorOpaco))
            {
                return Task.FromResult(ResultadoDeAplicacionDeSupresion.Fallida);
            }

            return Task.FromResult(_aplicadas.Add((identificadorOpaco, clase)) ? ResultadoDeAplicacionDeSupresion.Aplicada : ResultadoDeAplicacionDeSupresion.YaEstabaAplicada);
        }
    }

    private readonly RegistroDeSupresionesEnMemoria _registro = new();
    private readonly SellosEnMemoria _sellos = new();
    private readonly ExcepcionesEnMemoria _excepciones = new();
    private readonly AplicadorDeIdentidad _aplicador = new();

    /// <summary>Una entrada válida tal como estaba en el almacén: suma correcta, clave determinista y la hora en que se modificó.</summary>
    private static EntradaLeidaDto Valida(string cuenta, EtapaDeSupresion etapa, TimeSpan haceCuanto, ClaseDeSupresion clase = ClaseDeSupresion.Cancelacion)
    {
        var dominio = EntradaDeSupresion.Crear(cuenta, ConversionesParaPruebas.ATipo(clase), etapa == EtapaDeSupresion.Intencion ? FaseDeSupresion.Intencion : FaseDeSupresion.Cierre, Respaldo - haceCuanto);
        var dto = new EntradaDeSupresionDto(cuenta, clase, etapa, dominio.Fecha, dominio.Suma, false);
        return new EntradaLeidaDto(dominio.Clave, dto, Respaldo - haceCuanto);
    }

    /// <summary>Siembra las entradas y deja un sello que las cubre a todas (las que se modificaron mucho antes del respaldo).</summary>
    private void SembrarYSellar(params EntradaLeidaDto[] entradas)
    {
        foreach (var entrada in entradas)
        {
            _registro.Sembrar(entrada);
        }

        var dominio = entradas.Select(e => new EntradaLeida(e.ClaveEnElAlmacen, EntradaDeSupresion.Reconstruir(e.Entrada.IdentificadorOpaco, ConversionesParaPruebas.ATipo(e.Entrada.Clase), e.Entrada.Etapa == EtapaDeSupresion.Intencion ? FaseDeSupresion.Intencion : FaseDeSupresion.Cierre, e.Entrada.Fecha, e.Entrada.Suma), e.ModificadaEn));
        var sello = SelloDelRegistro.Sellar(Respaldo, dominio);
        _sellos.Sello = new SelloDelRegistroDto(sello.Instante, sello.Cantidad, sello.SumaDelConjunto);
    }

    private VerificarRegistroDeSupresiones Verificador() => new(_registro, _sellos, _excepciones);

    private ReaplicarSupresiones Reaplicador(int maximo = 50, bool confirma = false) => new(_registro, _aplicador, _sellos, new OpcionesDeReaplicacion(Respaldo, maximo, confirma));

    // ---------- VerificarRegistroDeSupresiones ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task UnRegistroQueCoincideConSuSello_PermiteAbrir()
    {
        SembrarYSellar(Valida(CuentaA, EtapaDeSupresion.Intencion, TimeSpan.FromHours(5)), Valida(CuentaA, EtapaDeSupresion.Cierre, TimeSpan.FromHours(4)));

        var resultado = await Verificador().Ejecutar(Respaldo);

        Assert.True(resultado.Valor.PermiteAbrir);
        Assert.Equal(EstadoDelRegistro.Valido, resultado.Valor.Estado);
        Assert.False(resultado.Valor.AbiertoPorExcepcion);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task ConElRegistroAusente_NoPermiteAbrirNiConUnaExcepcionRegistrada()
    {
        _registro.NoDisponible = true;
        _excepciones.Decision = new ExcepcionDelRegistroDto(Respaldo, Respaldo.AddHours(1));

        var resultado = await Verificador().Ejecutar(Respaldo);

        Assert.False(resultado.Valor.PermiteAbrir);
        Assert.Equal(EstadoDelRegistro.RegistroAusente, resultado.Valor.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task ConUnaEntradaAlterada_NoPermiteAbrirNiConUnaExcepcionRegistradaYSenalaLaClave()
    {
        var original = Valida(CuentaA, EtapaDeSupresion.Cierre, TimeSpan.FromHours(4));
        SembrarYSellar(Valida(CuentaA, EtapaDeSupresion.Intencion, TimeSpan.FromHours(5)), original);
        _registro.Sembrar(original with { Entrada = original.Entrada with { Fecha = original.Entrada.Fecha.AddDays(-20) } });
        _excepciones.Decision = new ExcepcionDelRegistroDto(Respaldo, Respaldo.AddHours(1));

        var resultado = await Verificador().Ejecutar(Respaldo);

        Assert.False(resultado.Valor.PermiteAbrir);
        Assert.Equal(EstadoDelRegistro.EntradaAlterada, resultado.Valor.Estado);
        Assert.Equal([original.ClaveEnElAlmacen], resultado.Valor.ClavesAfectadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task ConUnaEntradaMalformadaEnElAlmacen_SeTrataComoAlterada()
    {
        var mala = new EntradaLeidaDto(
            "entradas/lector@correo.pe/cancelacion-cierre.json",
            new EntradaDeSupresionDto("lector@correo.pe", ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Cierre, Respaldo, new string('a', 64), false),
            Respaldo - TimeSpan.FromHours(3));
        SembrarYSellar(Valida(CuentaA, EtapaDeSupresion.Intencion, TimeSpan.FromHours(5)));
        _registro.Sembrar(mala);

        var resultado = await Verificador().Ejecutar(Respaldo);

        Assert.Equal(EstadoDelRegistro.EntradaAlterada, resultado.Valor.Estado);
        Assert.Equal([mala.ClaveEnElAlmacen], resultado.Valor.ClavesAfectadas);
        Assert.False(resultado.Valor.PermiteAbrir);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task SinSello_NoPermiteAbrirSalvoConLaDecisionRegistradaParaEseRespaldo()
    {
        _registro.Sembrar(Valida(CuentaA, EtapaDeSupresion.Intencion, TimeSpan.FromHours(5)));
        _sellos.Sello = null;

        var sinDecision = await Verificador().Ejecutar(Respaldo);
        _excepciones.Decision = new ExcepcionDelRegistroDto(Respaldo.AddDays(-1), Respaldo.AddHours(1));
        var deOtroRespaldo = await Verificador().Ejecutar(Respaldo);
        _excepciones.Decision = new ExcepcionDelRegistroDto(Respaldo, Respaldo.AddHours(1));
        var conDecision = await Verificador().Ejecutar(Respaldo);

        Assert.False(sinDecision.Valor.PermiteAbrir);
        Assert.Equal(EstadoDelRegistro.SelloAusente, sinDecision.Valor.Estado);
        Assert.False(deOtroRespaldo.Valor.PermiteAbrir);
        Assert.True(conDecision.Valor.PermiteAbrir);
        Assert.True(conDecision.Valor.AbiertoPorExcepcion);
        Assert.Equal(EstadoDelRegistro.SelloAusente, conDecision.Valor.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task ConElAlmacenDeSellosSinRespuesta_SeTrataComoSelloAusente()
    {
        _registro.Sembrar(Valida(CuentaA, EtapaDeSupresion.Intencion, TimeSpan.FromHours(5)));
        _sellos.NoDisponible = true;

        var resultado = await Verificador().Ejecutar(Respaldo);

        Assert.Equal(EstadoDelRegistro.SelloAusente, resultado.Valor.Estado);
        Assert.False(resultado.Valor.PermiteAbrir);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task ConUnaSumaDelSelloQueNoCoincide_NoPermiteAbrirSalvoConLaDecisionRegistrada()
    {
        SembrarYSellar(Valida(CuentaA, EtapaDeSupresion.Intencion, TimeSpan.FromHours(5)));
        _sellos.Sello = _sellos.Sello! with { SumaDelConjunto = new string('0', 64) };

        var sin = await Verificador().Ejecutar(Respaldo);
        _excepciones.Decision = new ExcepcionDelRegistroDto(Respaldo, Respaldo.AddHours(1));
        var con = await Verificador().Ejecutar(Respaldo);

        Assert.Equal(EstadoDelRegistro.SumaDelConjuntoNoCoincide, sin.Valor.Estado);
        Assert.False(sin.Valor.PermiteAbrir);
        Assert.True(con.Valor.PermiteAbrir);
        Assert.True(con.Valor.AbiertoPorExcepcion);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task ConUnaEntradaBorradaDespuesDeSellar_NoPermiteAbrir()
    {
        var a = Valida(CuentaA, EtapaDeSupresion.Intencion, TimeSpan.FromHours(5));
        var b = Valida(CuentaB, EtapaDeSupresion.Intencion, TimeSpan.FromHours(4));
        SembrarYSellar(a, b);
        var sinB = new RegistroDeSupresionesEnMemoria();
        sinB.Sembrar(a);

        var resultado = await new VerificarRegistroDeSupresiones(sinB, _sellos, _excepciones).Ejecutar(Respaldo);

        Assert.Equal(EstadoDelRegistro.EntradasAusentes, resultado.Valor.Estado);
        Assert.False(resultado.Valor.PermiteAbrir);
    }

    [Fact]
    public async Task UnSelloConUnaCantidadImposible_NoCoincideConNada()
    {
        SembrarYSellar(Valida(CuentaA, EtapaDeSupresion.Intencion, TimeSpan.FromHours(5)));
        _sellos.Sello = _sellos.Sello! with { Cantidad = -1 };

        var resultado = await Verificador().Ejecutar(Respaldo);

        Assert.Equal(EstadoDelRegistro.SumaDelConjuntoNoCoincide, resultado.Valor.Estado);
        Assert.False(resultado.Valor.PermiteAbrir);
    }

    // ---------- ReaplicarSupresiones ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task LaCuentaConCierreSeReaplicaYLaQueSoloTieneIntencionSigueActivaYSeInforma()
    {
        SembrarYSellar(
            Valida(CuentaA, EtapaDeSupresion.Intencion, TimeSpan.FromHours(5)),
            Valida(CuentaA, EtapaDeSupresion.Cierre, TimeSpan.FromHours(4)),
            Valida(CuentaB, EtapaDeSupresion.Intencion, TimeSpan.FromHours(3)));

        var informe = await Reaplicador().Reaplicar();

        Assert.Equal(1, informe.Reaplicadas);
        Assert.Equal([CuentaA], _aplicador.Llamadas);
        Assert.Equal([CuentaB], informe.IntencionesSinCierre);
        Assert.Empty(informe.CierresSinIntencion);
        Assert.Empty(informe.Fallidas);
        Assert.True(informe.PermiteAbrir);
        Assert.False(informe.RequiereConfirmacion);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public async Task UnCierreSinIntencionPrevia_NoSeReaplicaYSeInforma()
    {
        SembrarYSellar(Valida(CuentaC, EtapaDeSupresion.Cierre, TimeSpan.FromHours(4)));

        var informe = await Reaplicador().Reaplicar();

        Assert.Equal(0, informe.Reaplicadas);
        Assert.Empty(_aplicador.Llamadas);
        Assert.Equal([CuentaC], informe.CierresSinIntencion);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task LaReaplicacionEsIdempotente_UnaCuentaYaSuprimidaEsExitoYNoSeCuentaComoReaplicada()
    {
        SembrarYSellar(Valida(CuentaA, EtapaDeSupresion.Intencion, TimeSpan.FromHours(5)), Valida(CuentaA, EtapaDeSupresion.Cierre, TimeSpan.FromHours(4)));
        var reaplicador = Reaplicador();

        var primera = await reaplicador.Reaplicar();
        var segunda = await reaplicador.Reaplicar();

        Assert.Equal(1, primera.Reaplicadas);
        Assert.Equal(0, primera.YaEstabanAplicadas);
        Assert.Equal(0, segunda.Reaplicadas);
        Assert.Equal(1, segunda.YaEstabanAplicadas);
        Assert.True(segunda.PermiteAbrir);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task UnaCuentaQueNoPuedeAplicarse_ImpideAbrirYLasDemasSeAplican()
    {
        SembrarYSellar(
            Valida(CuentaA, EtapaDeSupresion.Intencion, TimeSpan.FromHours(6)),
            Valida(CuentaA, EtapaDeSupresion.Cierre, TimeSpan.FromHours(5)),
            Valida(CuentaB, EtapaDeSupresion.Intencion, TimeSpan.FromHours(4)),
            Valida(CuentaB, EtapaDeSupresion.Cierre, TimeSpan.FromHours(3)));
        _aplicador.QueFallan.Add(CuentaA);

        var informe = await Reaplicador().Reaplicar();

        Assert.Equal([CuentaA], informe.Fallidas);
        Assert.Equal(1, informe.Reaplicadas);
        Assert.False(informe.PermiteAbrir);
    }

    [Fact]
    public async Task LaIntencionDeOtraClaseNoCuentaParaElCierre()
    {
        SembrarYSellar(
            Valida(CuentaA, EtapaDeSupresion.Intencion, TimeSpan.FromHours(5), ClaseDeSupresion.Cancelacion),
            Valida(CuentaA, EtapaDeSupresion.Cierre, TimeSpan.FromHours(4), ClaseDeSupresion.Anonimizacion));

        var informe = await Reaplicador().Reaplicar();

        Assert.Equal(0, informe.Reaplicadas);
        Assert.Empty(_aplicador.Llamadas);
        Assert.Equal([CuentaA], informe.IntencionesSinCierre);
        Assert.Equal([CuentaA], informe.CierresSinIntencion);
    }

    private static EntradaLeidaDto[] Nuevas(int cuantas) =>
        [.. Enumerable.Range(0, cuantas).SelectMany(i =>
        {
            var cuenta = $"cuenta-numero-{i:D4}-aleatoria";
            return new[]
            {
                Valida(cuenta, EtapaDeSupresion.Intencion, TimeSpan.FromMinutes(5)),
                Valida(cuenta, EtapaDeSupresion.Cierre, TimeSpan.FromMinutes(4)),
            };
        })];

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task ConMasEntradasNuevasQueElUmbralSinConfirmacion_NoSeReaplicaNadaYSePideConfirmar()
    {
        SembrarYSellar(Valida(CuentaA, EtapaDeSupresion.Intencion, TimeSpan.FromHours(5)), Valida(CuentaA, EtapaDeSupresion.Cierre, TimeSpan.FromHours(4)));
        foreach (var entrada in Nuevas(3))
        {
            _registro.Sembrar(entrada); // modificadas después del sello: seis entradas nuevas
        }

        var informe = await Reaplicador(maximo: 5).Reaplicar();

        Assert.True(informe.RequiereConfirmacion);
        Assert.False(informe.PermiteAbrir);
        Assert.Equal(0, informe.Reaplicadas);
        Assert.Empty(_aplicador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task ConLaConfirmacionDeLaPersonaResponsable_SeReaplicaPeseAlUmbral()
    {
        SembrarYSellar(Valida(CuentaA, EtapaDeSupresion.Intencion, TimeSpan.FromHours(5)), Valida(CuentaA, EtapaDeSupresion.Cierre, TimeSpan.FromHours(4)));
        foreach (var entrada in Nuevas(3))
        {
            _registro.Sembrar(entrada);
        }

        var informe = await Reaplicador(maximo: 5, confirma: true).Reaplicar();

        Assert.False(informe.RequiereConfirmacion);
        Assert.True(informe.ConfirmadaPorUmbral);
        Assert.True(informe.PermiteAbrir);
        Assert.Equal(4, informe.Reaplicadas);
    }

    [Fact]
    public async Task LasEntradasCubiertasPorElSelloNoCuentanParaElUmbral()
    {
        // Treinta cuentas con intención y cierre, todas selladas: mucho más que el umbral, pero ninguna es nueva.
        var antiguas = Enumerable.Range(0, 30).SelectMany(i => new[]
        {
            Valida($"cuenta-antigua-{i:D4}-aleatoria", EtapaDeSupresion.Intencion, TimeSpan.FromDays(3)),
            Valida($"cuenta-antigua-{i:D4}-aleatoria", EtapaDeSupresion.Cierre, TimeSpan.FromDays(3) - TimeSpan.FromMinutes(1)),
        }).ToArray();
        SembrarYSellar(antiguas);

        var informe = await Reaplicador(maximo: 5).Reaplicar();

        Assert.False(informe.RequiereConfirmacion);
        Assert.Equal(30, informe.Reaplicadas);
    }

    [Fact]
    public async Task SinSello_TodasLasEntradasCuentanComoNuevas()
    {
        foreach (var entrada in Nuevas(3))
        {
            _registro.Sembrar(entrada);
        }

        _sellos.Sello = null;

        var informe = await Reaplicador(maximo: 5).Reaplicar();

        Assert.True(informe.RequiereConfirmacion);
    }

    [Fact]
    public async Task ConElAlmacenDeSellosSinRespuesta_TodasLasEntradasCuentanComoNuevas()
    {
        foreach (var entrada in Nuevas(3))
        {
            _registro.Sembrar(entrada);
        }

        _sellos.NoDisponible = true;

        var informe = await Reaplicador(maximo: 5).Reaplicar();

        Assert.True(informe.RequiereConfirmacion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ElUmbralDebeSerPositivo(int maximo)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpcionesDeReaplicacion(Respaldo, maximo));
    }

    // ---------- El informe ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public void LosContadoresParaLaConstanciaNoLlevanNingunIdentificadorYElDetalleSi()
    {
        var informe = new InformeDeReaplicacion(2, 1, [CuentaB], [CuentaC], [CuentaA], RequiereConfirmacion: false, ConfirmadaPorUmbral: true);

        var contadores = informe.Contadores();
        var detalle = informe.Detalle();

        Assert.Equal(
            "reaplicadas=2\nya_aplicadas=1\nintenciones_sin_cierre=1\ncierres_sin_intencion=1\nfallidas=1\nconfirmaciones_por_umbral=1",
            contadores);
        foreach (var cuenta in new[] { CuentaA, CuentaB, CuentaC })
        {
            Assert.DoesNotContain(cuenta, contadores, StringComparison.Ordinal);
            Assert.Contains(cuenta, detalle, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UnInformeSinNadaPendiente_SoloTieneContadoresEnElDetalle()
    {
        var informe = new InformeDeReaplicacion(0, 0, [], [], [], RequiereConfirmacion: false, ConfirmadaPorUmbral: false);

        Assert.Equal(informe.Contadores(), informe.Detalle());
        Assert.True(informe.PermiteAbrir);
    }
}

/// <summary>Traducción de la clase del contrato al tipo del Dominio, para armar entradas válidas en las pruebas.</summary>
internal static class ConversionesParaPruebas
{
    public static TipoDeSupresion ATipo(ClaseDeSupresion clase) => clase == ClaseDeSupresion.Cancelacion ? TipoDeSupresion.Cancelacion : TipoDeSupresion.Anonimizacion;
}
