using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Infrastructure.Composicion;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// La sesión web sobre el contrato de Identidad (identidad-seguridad T45; AC-IDE-30, AC-IDE-50; IDE-33, IDE-60; AGENTS.md §3.9): el contexto de lectura del asistente se pide <b>nuevo en cada consulta</b> y solo para el
/// cliente de la sesión; la renovación, el cierre y el cierre de todas las sesiones de la capa servidor llegan a Identidad y sus rechazos o fallas se traducen sin cerrar la sesión por una falla transitoria. La cookie
/// <c>__Host-</c> con el identificador opaco y la credencial cifrada en <c>web.sesiones_web</c> las prueban <c>CookieYCierreDeSesionTests</c> y las de sesión de <c>web-base</c>.
/// </summary>
public sealed class SesionWebSobreIdentidadTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    private sealed class ContextoFalso : IContextoDeLectura
    {
        public int Emisiones { get; private set; }

        public Resultado<ValorSecreto>? Respuesta { get; set; }

        public Task<Resultado<ValorSecreto>> Emitir(Guid clienteId, CancellationToken cancelacion = default)
        {
            Emisiones++;
            return Task.FromResult(Respuesta ?? Resultado<ValorSecreto>.Exito(new ValorSecreto($"contexto-{Emisiones}-{clienteId:N}")));
        }

        public Task<Resultado<ClienteDelContexto>> Verificar(ValorSecreto contexto, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class SesionesFalsas : ISesiones
    {
        public Func<Resultado<CredencialDeSesion>> Renovacion { get; set; } = () => throw new NotSupportedException();

        public Func<Resultado<RespuestaUniforme>> Cierre { get; set; } = () => Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme());

        public List<Guid> Cerradas { get; } = [];

        public List<string> Renovaciones { get; } = [];

        public Task<Resultado<CredencialDeSesion>> Renovar(ValorSecreto renovacion, CancellationToken cancelacion = default)
        {
            Renovaciones.Add(renovacion.Revelar());
            return Task.FromResult(Renovacion());
        }

        public Task<Resultado<RespuestaUniforme>> Cerrar(ValorSecreto renovacion, CancellationToken cancelacion = default) => Task.FromResult(Cierre());

        public Task<Resultado<RespuestaUniforme>> CerrarTodas(Guid usuarioId, CancellationToken cancelacion = default)
        {
            Cerradas.Add(usuarioId);
            return Task.FromResult(Cierre());
        }
    }

    private static CredencialDeSesion Credencial(Guid usuario) =>
        new(new ValorSecreto("jwt-de-prueba"), Ahora.AddMinutes(15), new ValorSecreto("renovacion-nueva"), Ahora.AddDays(7), usuario, RolDeCuenta.Cliente, null, true);

    // ---------------------------------------------------------------- el contexto del asistente

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public async Task ElContextoDelAsistente_SePideNuevoEnCadaConsulta_SinCache_YDistintoCadaVez()
    {
        var identidad = new ContextoFalso();
        var asistente = new ContextoDelAsistenteDeLaWeb(identidad);
        var actor = new ActorVerificado(Guid.NewGuid(), RolWeb.Cliente, null);

        var uno = await asistente.Pedir(actor);
        var dos = await asistente.Pedir(actor);

        Assert.True(uno.EsExito && dos.EsExito);
        Assert.Equal(2, identidad.Emisiones);
        Assert.NotEqual(uno.Valor.Revelar(), dos.Valor.Revelar());
        Assert.DoesNotContain("contexto-", uno.Valor.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RolWeb.Visitante)]
    [InlineData(RolWeb.Encargado)]
    [InlineData(RolWeb.Logistica)]
    [InlineData(RolWeb.AdministradorCentral)]
    [Trait("Criterio", "AC-IDE-30")]
    public async Task SoloUnClienteObtieneElContexto_OtroRolNoLlegaAIdentidad(RolWeb rol)
    {
        var identidad = new ContextoFalso();

        var resultado = await new ContextoDelAsistenteDeLaWeb(identidad).Pedir(new ActorVerificado(Guid.NewGuid(), rol, null));

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(0, identidad.Emisiones);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public async Task SiIdentidadNoLoEmite_LaWebDevuelveSuMismoError_SinInventarUnContexto()
    {
        var identidad = new ContextoFalso { Respuesta = Resultado<ValorSecreto>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "cuenta-no-encontrada")) };

        var resultado = await new ContextoDelAsistenteDeLaWeb(identidad).Pedir(new ActorVerificado(Guid.NewGuid(), RolWeb.Cliente, null));

        Assert.False(resultado.EsExito);
        Assert.Equal("cuenta-no-encontrada", resultado.Error!.Clave);
    }

    // ---------------------------------------------------------------- renovar, cerrar y cerrar todas

    [Fact]
    [Trait("Criterio", "AC-IDE-50")]
    public async Task CerrarTodasLasSesiones_LlegaAIdentidad_ConElUsuario_YDevuelveSiSeRevoco()
    {
        var sesiones = new SesionesFalsas();
        var puerto = new SesionesDeIdentidadParaLaWeb(sesiones);
        var usuario = Guid.NewGuid();

        Assert.True(await puerto.CerrarTodasLasSesiones(usuario));
        Assert.Equal([usuario], sesiones.Cerradas);

        sesiones.Cierre = () => Resultado<RespuestaUniforme>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "no-disponible"));
        Assert.False(await puerto.CerrarTodasLasSesiones(usuario));
        sesiones.Cierre = () => throw new InvalidOperationException("La base no responde.");
        Assert.False(await puerto.CerrarTodasLasSesiones(usuario));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-50")]
    public async Task RevocarUnaRenovacion_SoloEsVerdaderoSiIdentidadLaConfirma()
    {
        var sesiones = new SesionesFalsas();
        var puerto = new SesionesDeIdentidadParaLaWeb(sesiones);

        Assert.True(await puerto.RevocarRenovacion("renovacion-de-prueba"));
        sesiones.Cierre = () => throw new TimeoutException();
        Assert.False(await puerto.RevocarRenovacion("renovacion-de-prueba"));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-50")]
    public async Task UnaRenovacionCorrecta_EntregaLaCredencialYLaRenovacionNuevasSoloALaCapaServidor()
    {
        var usuario = Guid.NewGuid();
        var sesiones = new SesionesFalsas { Renovacion = () => Resultado<CredencialDeSesion>.Exito(Credencial(usuario)) };

        var resultado = await new SesionesDeIdentidadParaLaWeb(sesiones).Renovar("renovacion-vieja");

        var renovada = Assert.IsType<ResultadoDeRenovacion.Renovada>(resultado);
        Assert.Equal("jwt-de-prueba", renovada.Credencial);
        Assert.Equal("renovacion-nueva", renovada.Renovacion);
        Assert.Equal(Ahora.AddMinutes(15), renovada.CredencialVenceEn);
        Assert.Equal(["renovacion-vieja"], sesiones.Renovaciones);
        Assert.DoesNotContain("jwt-de-prueba", renovada.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CategoriaDeError.NoAutenticado, true)]
    [InlineData(CategoriaDeError.Prohibido, true)]
    [InlineData(CategoriaDeError.Validacion, true)]
    [InlineData(CategoriaDeError.NoDisponible, false)]
    [InlineData(CategoriaDeError.Limite, false)]
    [InlineData(CategoriaDeError.Inesperado, false)]
    [Trait("Criterio", "AC-IDE-50")]
    public async Task UnRechazoDeLaRenovacionCierraLaSesion_UnaFallaTransitoriaNo(CategoriaDeError categoria, bool cierra)
    {
        var sesiones = new SesionesFalsas { Renovacion = () => Resultado<CredencialDeSesion>.Fallo(new ErrorDeAplicacion(categoria, "motivo")) };

        var resultado = await new SesionesDeIdentidadParaLaWeb(sesiones).Renovar("renovacion-vieja");

        Assert.Equal(cierra, resultado is ResultadoDeRenovacion.Rechazada);
        Assert.Equal(!cierra, resultado is ResultadoDeRenovacion.Transitoria);
    }

    [Fact]
    public async Task UnaExcepcionDeIdentidadAlRenovar_EsTransitoria_YNoCierraLaSesion()
    {
        var sesiones = new SesionesFalsas { Renovacion = () => throw new InvalidOperationException("La base no responde.") };

        Assert.IsType<ResultadoDeRenovacion.Transitoria>(await new SesionesDeIdentidadParaLaWeb(sesiones).Renovar("renovacion-vieja"));
    }

    [Fact]
    public async Task UnaRenovacionVacia_SeRechazaAntesDeLlamarAIdentidad()
    {
        var sesiones = new SesionesFalsas();
        var puerto = new SesionesDeIdentidadParaLaWeb(sesiones);

        await Assert.ThrowsAsync<ArgumentException>(() => puerto.Renovar(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => puerto.RevocarRenovacion(string.Empty));
        Assert.Empty(sesiones.Renovaciones);
    }
}
