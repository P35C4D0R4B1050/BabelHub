using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;

namespace BabelHub.Inventario.UnitTests.Plataforma;

public sealed class FallaSeguraTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Transacción simulada: los efectos solo quedan confirmados si el trabajo termina sin lanzar.</summary>
    private sealed class UnidadDeTrabajoFalsa : IUnidadDeTrabajo
    {
        private List<string> _pendientes = [];

        public List<string> Confirmados { get; } = [];

        public int Transacciones { get; private set; }

        public void Registrar(string efecto) => _pendientes.Add(efecto);

        public async Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default)
        {
            Transacciones++;
            _pendientes = [];
            try
            {
                var resultado = await trabajo(cancelacion);
                Confirmados.AddRange(_pendientes);
                return resultado;
            }
            finally
            {
                _pendientes = [];
            }
        }

        public Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default) =>
            bloque(cancelacion);
    }

    private sealed class CasoDeUsoDePrueba(UnidadDeTrabajoFalsa unidad, Func<Task<Resultado<string>>> aMitad)
        : ICasoDeUso<string, string>
    {
        public async Task<Resultado<string>> Ejecutar(string entrada, CancellationToken cancelacion = default)
        {
            unidad.Registrar("descontar-stock");
            var resultado = await aMitad();
            unidad.Registrar("crear-pedido");
            return resultado;
        }
    }

    private sealed class AlertasEspia : IPublicadorDeAlertasTecnicas
    {
        public List<AlertaTecnica> Alertas { get; } = [];

        public bool Falla { get; set; }

        public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
        {
            if (Falla)
            {
                throw new InvalidOperationException("auditoría caída");
            }

            Alertas.Add(alerta);
            return Task.CompletedTask;
        }
    }

    private readonly UnidadDeTrabajoFalsa _unidad = new();
    private readonly AlertasEspia _alertas = new();

    private TransaccionPorCasoDeUso<string, string> Decorar(Func<Task<Resultado<string>>> aMitad) =>
        new(new CasoDeUsoDePrueba(_unidad, aMitad), _unidad);

    [Fact]
    [Trait("Criterio", "AC-SEG-04")]
    public async Task Ejecutar_ConUnCasoDeUsoQueTermina_ConfirmaTodosSusEfectosEnUnaSolaTransaccion()
    {
        var resultado = await Decorar(() => Task.FromResult(Resultado<string>.Exito("listo"))).Ejecutar("x");

        Assert.True(resultado.EsExito);
        Assert.Equal(["descontar-stock", "crear-pedido"], _unidad.Confirmados);
        Assert.Equal(1, _unidad.Transacciones);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-04")]
    public async Task Ejecutar_ConUnErrorInesperadoAMitad_NoDejaEfectosParcialesYElErrorSube()
    {
        var decorado = Decorar(() => throw new InvalidOperationException("se cayó la base"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => decorado.Ejecutar("x"));

        Assert.Empty(_unidad.Confirmados);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-04")]
    public async Task Ejecutar_ConUnFalloEsperado_RevierteLosEfectosYDevuelveElMismoError()
    {
        var error = new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, "stock-insuficiente");

        var resultado = await Decorar(() => Task.FromResult(Resultado<string>.Fallo(error))).Ejecutar("x");

        Assert.False(resultado.EsExito);
        Assert.Same(error, resultado.Error);
        Assert.Empty(_unidad.Confirmados);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-03")]
    public async Task Ejecutar_ConUnaExcepcionDeNegocio_RevierteYDevuelveLaClaveSimbolica()
    {
        var decorado = Decorar(() => throw new ExcepcionDeNegocio("stock-insuficiente"));

        var resultado = await decorado.Ejecutar("x");

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.ReglaDeNegocio, resultado.Error!.Categoria);
        Assert.Equal("stock-insuficiente", resultado.Error.Clave);
        Assert.Empty(_unidad.Confirmados);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-03")]
    public async Task Ejecutar_ConUnaExcepcionDeNegocioDeValidacion_ConservaLosCampos()
    {
        var campos = new[] { new ErrorDeCampo("cantidad", "fuera-de-rango") };
        var decorado = Decorar(() => throw new ExcepcionDeNegocio("datos-invalidos", CategoriaDeError.Validacion, campos));

        var resultado = await decorado.Ejecutar("x");

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal(campos, resultado.Error.Errores);
    }

    [Fact]
    public async Task Ejecutar_ConLaCancelacionDelLlamador_SePropagaYNoDejaEfectos()
    {
        var decorado = Decorar(() => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => decorado.Ejecutar("x"));

        Assert.Empty(_unidad.Confirmados);
    }

    [Fact]
    public void ExcepcionDeNegocio_ConDatosInvalidos_Lanza()
    {
        Assert.Throws<ArgumentException>(() => new ExcepcionDeNegocio(" "));
        Assert.Throws<ArgumentException>(() => new ExcepcionDeNegocio("x", CategoriaDeError.Inesperado));
        Assert.Equal("stock-insuficiente", new ExcepcionDeNegocio("stock-insuficiente").ComoError().Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-04")]
    public async Task Ejecutar_ConUnaDependenciaNoNecesariaQueFalla_LaOperacionSeCompletaYQuedaUnaAlertaTecnica()
    {
        var ejecutor = new EjecutorDeDependenciasNoNecesarias(_alertas, new RelojFijo(Ahora));
        var avisoEnviado = false;
        var casoDeUso = new TransaccionPorCasoDeUso<string, string>(
            new CasoDeUsoConAviso(_unidad, ejecutor, () => avisoEnviado = true, falla: true), _unidad);

        var resultado = await casoDeUso.Ejecutar("x");

        Assert.True(resultado.EsExito);
        Assert.Equal(["crear-pedido"], _unidad.Confirmados);
        Assert.False(avisoEnviado);
        var alerta = Assert.Single(_alertas.Alertas);
        Assert.Equal(EjecutorDeDependenciasNoNecesarias.MotivoDeAlerta, alerta.Motivo);
        Assert.Equal(Ahora, alerta.Instante);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-04")]
    public async Task Ejecutar_ConUnaDependenciaNoNecesariaQueFunciona_AplicaElEfectoSinAlerta()
    {
        var ejecutor = new EjecutorDeDependenciasNoNecesarias(_alertas, new RelojFijo(Ahora));
        var avisoEnviado = false;

        var aplicado = await ejecutor.Ejecutar(_ =>
        {
            avisoEnviado = true;
            return Task.CompletedTask;
        });

        Assert.True(aplicado);
        Assert.True(avisoEnviado);
        Assert.Empty(_alertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-04")]
    public async Task Ejecutar_ConUnaDependenciaNoNecesariaQueFallaYSinPoderAlertar_AunAsiContinua()
    {
        _alertas.Falla = true;
        var ejecutor = new EjecutorDeDependenciasNoNecesarias(_alertas, new RelojFijo(Ahora));

        var aplicado = await ejecutor.Ejecutar(_ => throw new HttpRequestException("whatsapp 503"));

        Assert.False(aplicado);
    }

    [Fact]
    public async Task Ejecutar_ConLaCancelacionDelLlamadorEnUnaDependenciaNoNecesaria_SePropaga()
    {
        var ejecutor = new EjecutorDeDependenciasNoNecesarias(_alertas, new RelojFijo(Ahora));
        using var fuente = new CancellationTokenSource();
        await fuente.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ejecutor.Ejecutar(token => Task.FromCanceled(token), fuente.Token));

        Assert.Empty(_alertas.Alertas);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-03")]
    [InlineData(CategoriaDeError.Validacion, true)]
    [InlineData(CategoriaDeError.ReglaDeNegocio, true)]
    [InlineData(CategoriaDeError.Limite, true)]
    [InlineData(CategoriaDeError.Inesperado, false)]
    [InlineData(CategoriaDeError.NoAutenticado, false)]
    [InlineData(CategoriaDeError.Prohibido, false)]
    [InlineData(CategoriaDeError.NoEncontrado, false)]
    [InlineData(CategoriaDeError.Conflicto, false)]
    [InlineData(CategoriaDeError.NoDisponible, false)]
    public void MensajesDeError_ExponeElMotivoSoloEnValidacionReglaDeNegocioYLimite(CategoriaDeError categoria, bool especifico)
    {
        Assert.Equal(especifico, categoria.ExponeElMotivo());
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-03")]
    public void MensajesDeError_ElInesperadoEsGenericoYNingunMensajeTrazaNiDatosInternos()
    {
        foreach (var categoria in Enum.GetValues<CategoriaDeError>())
        {
            var mensaje = categoria.MensajeGenerico();

            Assert.False(string.IsNullOrWhiteSpace(mensaje));
            Assert.DoesNotContain("Exception", mensaje, StringComparison.Ordinal);
            Assert.DoesNotContain(" at ", mensaje, StringComparison.Ordinal);
            Assert.DoesNotContain("SELECT", mensaje, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal("Ocurrió un error inesperado. Inténtalo de nuevo más tarde.", CategoriaDeError.Inesperado.MensajeGenerico());
        Assert.Equal(
            Enum.GetValues<CategoriaDeError>().Length,
            Enum.GetValues<CategoriaDeError>().Select(c => c.MensajeGenerico()).Distinct().Count());
    }

    private sealed class CasoDeUsoConAviso(UnidadDeTrabajoFalsa unidad, IEjecutorDeDependenciasNoNecesarias ejecutor, Action alEnviar, bool falla)
        : ICasoDeUso<string, string>
    {
        public async Task<Resultado<string>> Ejecutar(string entrada, CancellationToken cancelacion = default)
        {
            unidad.Registrar("crear-pedido");
            await ejecutor.Ejecutar(
                _ =>
                {
                    if (falla)
                    {
                        throw new HttpRequestException("whatsapp 503");
                    }

                    alEnviar();
                    return Task.CompletedTask;
                },
                cancelacion);
            return Resultado<string>.Exito("listo");
        }
    }
}
