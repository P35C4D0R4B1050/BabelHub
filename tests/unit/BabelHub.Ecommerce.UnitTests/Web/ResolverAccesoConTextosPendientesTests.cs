using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>T36: la política de acceso con los textos legales pendientes (WEB-41 a WEB-49): orden, falla cerrada, método y ruta, retorno y evento propio.</summary>
public sealed class ResolverAccesoConTextosPendientesTests
{
    private static readonly Guid Sucursal = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly ActorVerificado Cliente = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), RolWeb.Cliente, null);
    private static readonly ActorVerificado Encargado = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), RolWeb.Encargado, Sucursal);
    private static readonly ActorVerificado Logistica = new(Guid.Parse("33333333-3333-3333-3333-333333333333"), RolWeb.Logistica, Sucursal);
    private static readonly ActorVerificado Central = new(Guid.Parse("44444444-4444-4444-4444-444444444444"), RolWeb.AdministradorCentral, null);

    private sealed class EstadoDePrueba : IEstadoDeCuentaDeLaWeb
    {
        public EstadoDeAceptacionDeTextos Estado { get; set; } = EstadoDeAceptacionDeTextos.Pendientes;

        public Func<CancellationToken, Task<EstadoDeAceptacionDeTextos>>? Comportamiento { get; set; }

        public int Consultas { get; private set; }

        public Task<EstadoDeAceptacionDeTextos> Aceptacion(Guid usuarioId, CancellationToken cancelacion = default)
        {
            Consultas++;
            return Comportamiento is { } c ? c(cancelacion) : Task.FromResult(Estado);
        }
    }

    private sealed class Entorno
    {
        public EstadoDePrueba Estado { get; } = new();

        public AlmacenDeSesionesEnMemoria Almacen { get; } = new();

        public RegistroDeSeguridadEspia Seguridad { get; } = new();

        public AlertasTecnicasEspia Alertas { get; } = new();

        public LimitadorDeConsultasDeEstado Limitador { get; } = new(2);

        public ResolverAccesoConTextosPendientes Resolver(TimeSpan? espera = null) =>
            new(new AreasDeInterfaz(), Estado, Almacen, Seguridad, Alertas, new RelojFijo(), Limitador, espera ?? TimeSpan.FromMilliseconds(200));
    }

    private sealed class RelojFijo : IReloj
    {
        public DateTimeOffset Ahora => new(2026, 11, 14, 15, 0, 0, TimeSpan.FromHours(-5));

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.DateTime);
    }

    private static SolicitudDeAcceso Pedir(
        ActorVerificado? actor,
        CapacidadWeb? capacidad = null,
        string metodo = "GET",
        AreaWeb? area = null,
        Guid? sucursal = null,
        bool sinConsulta = false,
        string? ruta = "/catalogo",
        bool pantallaCompleta = true,
        bool sinRetorno = false,
        string? cookie = null) =>
        new(actor, cookie, area, capacidad, metodo, sucursal, sinConsulta, "/catalogo/{id}", ruta, pantallaCompleta, sinRetorno);

    // ---- Orden: sesión, rol y alcance antes de consultar ----

    [Fact]
    [Trait("Criterio", "AC-WEB-34")]
    public async Task UnVisitante_NoConsultaAIdentidad_YUnaAreaDelPersonalPideSesion()
    {
        var e = new Entorno();

        var publica = await e.Resolver().Resolver(Pedir(null));
        var delPersonal = await e.Resolver().Resolver(Pedir(null, area: AreaWeb.Sucursal));

        Assert.Equal(TipoDeDecisionDeAcceso.Permitida, publica.Tipo);
        Assert.Equal(TipoDeDecisionDeAcceso.SinSesion, delPersonal.Tipo);
        Assert.Equal(0, e.Estado.Consultas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-34")]
    public async Task ElClienteQueAbreAdministracion_VeLaPantallaDeSinPermiso_YNoLaInvitacionAAceptar_SinConsultarAIdentidad()
    {
        var e = new Entorno();

        var decision = await e.Resolver().Resolver(Pedir(Cliente, area: AreaWeb.Administracion));

        Assert.Equal(TipoDeDecisionDeAcceso.SinPermiso, decision.Tipo);
        Assert.Equal(0, e.Estado.Consultas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-37")]
    public async Task ElEncargadoDeOtraSucursal_VeSinPermiso_AntesDeConsultar_YElCentralVeTodas()
    {
        var e = new Entorno();
        var otra = Guid.NewGuid();

        var ajena = await e.Resolver().Resolver(Pedir(Encargado, CapacidadWeb.Cuenta, area: AreaWeb.Sucursal, sucursal: otra));
        var propia = await e.Resolver().Resolver(Pedir(Encargado, CapacidadWeb.Cuenta, area: AreaWeb.Sucursal, sucursal: Sucursal));
        var central = await e.Resolver().Resolver(Pedir(Central, CapacidadWeb.Cuenta, area: AreaWeb.Sucursal, sucursal: otra));

        Assert.Equal(TipoDeDecisionDeAcceso.SinPermiso, ajena.Tipo);
        Assert.True(ajena.AlcanceFueraDeLaSucursal);
        Assert.Equal(TipoDeDecisionDeAcceso.Permitida, propia.Tipo);
        Assert.Equal(TipoDeDecisionDeAcceso.Permitida, central.Tipo);
        Assert.Equal(2, e.Estado.Consultas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-38")]
    public async Task UnEndpointSinConsultaDeEstado_NoPreguntaANadie()
    {
        var e = new Entorno();

        var decision = await e.Resolver().Resolver(Pedir(Cliente, sinConsulta: true));

        Assert.Equal(TipoDeDecisionDeAcceso.Permitida, decision.Tipo);
        Assert.Equal(0, e.Estado.Consultas);
    }

    // ---- Cada solicitud consulta, sin caché ni estado anterior ----

    [Fact]
    [Trait("Criterio", "AC-WEB-34")]
    public async Task CadaSolicitudConsultaDeNuevo_YUnaAceptacionSeVeAlSiguienteIntento()
    {
        var e = new Entorno();
        var politica = e.Resolver();

        var antes = await politica.Resolver(Pedir(Cliente, CapacidadWeb.Pedidos));
        e.Estado.Estado = EstadoDeAceptacionDeTextos.Aceptados;
        var despues = await politica.Resolver(Pedir(Cliente));

        Assert.True(antes.TextosPendientes);
        Assert.False(despues.TextosPendientes);
        Assert.Equal(2, e.Estado.Consultas);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-34")]
    [InlineData(RolWeb.Cliente, CapacidadWeb.Pedidos, "GET", true)]
    [InlineData(RolWeb.Cliente, CapacidadWeb.Cuenta, "GET", true)]
    [InlineData(RolWeb.Cliente, CapacidadWeb.TextosLegales, "GET", true)]
    [InlineData(RolWeb.Cliente, CapacidadWeb.LibroDeReclamaciones, "GET", true)]
    [InlineData(RolWeb.Cliente, CapacidadWeb.Aceptar, "POST", true)]
    [InlineData(RolWeb.Cliente, CapacidadWeb.CerrarSesion, "POST", true)]
    [InlineData(RolWeb.Cliente, CapacidadWeb.EnviarHoja, "POST", true)]
    [InlineData(RolWeb.Encargado, CapacidadWeb.Cuenta, "GET", true)]
    [InlineData(RolWeb.Encargado, CapacidadWeb.Pedidos, "GET", false)]
    [InlineData(RolWeb.Logistica, CapacidadWeb.Pedidos, "GET", false)]
    [InlineData(RolWeb.AdministradorCentral, CapacidadWeb.Pedidos, "GET", false)]
    public async Task ConTextosPendientes_SoloSeAtiendeLoQueLaCapacidadDeLaRutaYSuRolPermiten(RolWeb rol, CapacidadWeb capacidad, string metodo, bool permitida)
    {
        var e = new Entorno();
        var actor = rol switch { RolWeb.Cliente => Cliente, RolWeb.Encargado => Encargado, RolWeb.Logistica => Logistica, _ => Central };

        var decision = await e.Resolver().Resolver(Pedir(actor, capacidad, metodo));

        Assert.Equal(permitida, decision.Tipo == TipoDeDecisionDeAcceso.Permitida);
        if (permitida)
        {
            Assert.True(decision.TextosPendientes);
        }
    }

    // ---- Pantalla frente a acción: redirige o rechaza ----

    [Fact]
    [Trait("Criterio", "AC-WEB-34")]
    public async Task UnaPantallaQueElRolPermitePeroLosTextosBloquean_RedirigeAAceptar_YGuardaElRetorno()
    {
        var e = new Entorno();
        var cookie = await Abrir(e);

        var decision = await e.Resolver().Resolver(Pedir(Cliente, capacidad: null, cookie: cookie, ruta: "/catalogo/libro-1"));

        Assert.Equal(TipoDeDecisionDeAcceso.Redirige, decision.Tipo);
        Assert.Empty(e.Seguridad.Bloqueos);
        Assert.Equal("/catalogo/libro-1", await e.Resolver().TomarRetorno(cookie, RolWeb.Cliente));
        Assert.Null(await e.Resolver().TomarRetorno(cookie, RolWeb.Cliente));
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-38")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    [InlineData("get")]
    [InlineData("PROPFIND")]
    public async Task UnaAccionOUnMetodoNoDeclarado_SeRechaza_ConElEventoPropio_YSinGuardarRetorno(string metodo)
    {
        var e = new Entorno();
        var cookie = await Abrir(e);

        var decision = await e.Resolver().Resolver(Pedir(Cliente, capacidad: null, metodo: metodo, cookie: cookie));

        Assert.Equal(TipoDeDecisionDeAcceso.Bloqueada, decision.Tipo);
        var clave = Assert.Single(e.Seguridad.Bloqueos);
        Assert.Equal("/catalogo/{id}", clave.PlantillaDeRuta);
        Assert.Equal(metodo is "get" or "PROPFIND" ? ClaveDeBloqueoPorTextos.MetodoNoEstandar : metodo, clave.Metodo);
        Assert.Null(await e.Resolver().TomarRetorno(cookie, RolWeb.Cliente));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-38")]
    public async Task UnPostAUnaRutaQueSoloAdmiteGet_EsMetodoNoDeclarado_YUnaCapacidadAjenaUnaAccionNoPermitida()
    {
        var e = new Entorno();

        var metodo = await e.Resolver().Resolver(Pedir(Cliente, CapacidadWeb.Pedidos, "POST"));
        var ajena = await e.Resolver().Resolver(Pedir(Encargado, CapacidadWeb.Pedidos, "GET"));

        Assert.Equal(MotivoDeBloqueoPorTextos.MetodoNoDeclarado, metodo.Motivo);
        Assert.Equal(TipoDeDecisionDeAcceso.Redirige, ajena.Tipo);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-40")]
    public async Task ElEventoDelBloqueo_NoLlevaDatosDeLaPersona_NiLaCuentaEnClaro_YUnaRutaLargaSeNormaliza()
    {
        var e = new Entorno();

        await e.Resolver().Resolver(Pedir(Cliente, null, "POST") with { PlantillaDeRuta = new string('x', 200) });

        var clave = Assert.Single(e.Seguridad.Bloqueos);
        Assert.Equal(ClaveDeBloqueoPorTextos.RutaNoDeclarada, clave.PlantillaDeRuta);
        Assert.DoesNotContain(Cliente.UsuarioId.ToString(), clave.ToString(), StringComparison.Ordinal);
        Assert.Equal(Cliente.UsuarioId.ToString("N"), clave.Cuenta);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-40")]
    public async Task SiElRegistroFalla_ElRechazoSigueVigente_YSePublicaLaAlertaTecnica()
    {
        var e = new Entorno();
        e.Seguridad.FallaAlRegistrar = true;

        var decision = await e.Resolver().Resolver(Pedir(Cliente, null, "POST"));

        Assert.Equal(TipoDeDecisionDeAcceso.Bloqueada, decision.Tipo);
        Assert.Single(e.Alertas.Alertas);
    }

    // ---- Falla cerrada ----

    public static TheoryData<string> Fallos => new() { "error", "tiempo", "mal-formada", "desconocido" };

    [Theory]
    [Trait("Criterio", "AC-WEB-39")]
    [MemberData(nameof(Fallos))]
    public async Task SiIdentidadFalla_ErraTardaOResponde_Mal_LaWebFallaCerrada_ConUnaAlertaTecnica_SinTocarLaSesion(string fallo)
    {
        var e = new Entorno();
        e.Estado.Comportamiento = fallo switch
        {
            "error" => _ => throw new InvalidOperationException("Identidad cayó"),
            "tiempo" => async ct =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                return EstadoDeAceptacionDeTextos.Aceptados;
            },
            "mal-formada" => _ => Task.FromResult((EstadoDeAceptacionDeTextos)99),
            _ => _ => Task.FromResult(EstadoDeAceptacionDeTextos.Desconocido),
        };

        var pantalla = await e.Resolver().Resolver(Pedir(Cliente, CapacidadWeb.Pedidos));

        Assert.Equal(TipoDeDecisionDeAcceso.NoDisponible, pantalla.Tipo);
        Assert.Equal(MotivoDeBloqueoPorTextos.IdentidadNoDisponible, pantalla.Motivo);
        Assert.Single(e.Alertas.Alertas);
        Assert.Empty(e.Almacen.Filas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-39")]
    public async Task ConIdentidadCaida_LaAlertaTecnicaSePublicaComoMaximoUnaVezPorMinuto_AunqueCadaSolicitudFalleCerrada()
    {
        var e = new Entorno();
        e.Estado.Estado = EstadoDeAceptacionDeTextos.Desconocido;
        var politica = e.Resolver();

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(TipoDeDecisionDeAcceso.NoDisponible, (await politica.Resolver(Pedir(Cliente, CapacidadWeb.Pedidos))).Tipo);
        }

        Assert.Single(e.Alertas.Alertas);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-39")]
    [InlineData(CapacidadWeb.CerrarSesion, "POST", true)]
    [InlineData(CapacidadWeb.EnviarHoja, "POST", true)]
    [InlineData(CapacidadWeb.TextosLegales, "GET", true)]
    [InlineData(CapacidadWeb.LibroDeReclamaciones, "GET", true)]
    [InlineData(CapacidadWeb.Cuenta, "GET", false)]
    [InlineData(CapacidadWeb.Pedidos, "GET", false)]
    [InlineData(CapacidadWeb.Aceptar, "POST", false)]
    public async Task ConIdentidadCaida_SoloQuedanCerrarSesion_LosTextos_ElLibroYEnviarUnaHoja(CapacidadWeb capacidad, string metodo, bool permitida)
    {
        var e = new Entorno();
        e.Estado.Estado = EstadoDeAceptacionDeTextos.Desconocido;

        var decision = await e.Resolver().Resolver(Pedir(Cliente, capacidad, metodo));

        Assert.Equal(permitida ? TipoDeDecisionDeAcceso.Permitida : TipoDeDecisionDeAcceso.NoDisponible, decision.Tipo);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-39")]
    public async Task ConElTopeDeConsultasLleno_FallaCerradoDeInmediato_SinEsperar()
    {
        var e = new Entorno();
        var liberar = new TaskCompletionSource<EstadoDeAceptacionDeTextos>();
        e.Estado.Comportamiento = _ => liberar.Task;
        var politica = e.Resolver(TimeSpan.FromSeconds(30));
        var dos = new[] { politica.Resolver(Pedir(Cliente, CapacidadWeb.Pedidos)), politica.Resolver(Pedir(Cliente, CapacidadWeb.Pedidos)) };
        await Task.Delay(100);

        var tercera = await politica.Resolver(Pedir(Cliente, CapacidadWeb.Pedidos));

        Assert.Equal(TipoDeDecisionDeAcceso.NoDisponible, tercera.Tipo);
        Assert.Equal(2, e.Estado.Consultas);
        liberar.SetResult(EstadoDeAceptacionDeTextos.Aceptados);
        Assert.All(await Task.WhenAll(dos), d => Assert.Equal(TipoDeDecisionDeAcceso.Permitida, d.Tipo));
        // Los turnos se liberan: una consulta nueva vuelve a pasar.
        e.Estado.Comportamiento = null;
        e.Estado.Estado = EstadoDeAceptacionDeTextos.Aceptados;
        Assert.Equal(TipoDeDecisionDeAcceso.Permitida, (await politica.Resolver(Pedir(Cliente))).Tipo);
    }

    // ---- Retorno tras aceptar ----

    [Theory]
    [Trait("Criterio", "AC-WEB-42")]
    [InlineData("/pedidos", true)]
    [InlineData("/catalogo/libro-1", true)]
    [InlineData("//atacante.example", false)]
    [InlineData("/\\atacante.example", false)]
    [InlineData("https://atacante.example/", false)]
    [InlineData("/%2f%2fatacante.example", false)]
    [InlineData("/ruta?x=1", false)]
    [InlineData("/ruta#f", false)]
    [InlineData("/ruta\r\nSet-Cookie: x=1", false)]
    [InlineData("/ruta con espacio", false)]
    [InlineData("/../etc/passwd", false)]
    [InlineData("/ingreso", false)]
    [InlineData("/recuperacion/abc", false)]
    [InlineData("/verificacion/abc", false)]
    [InlineData("/checkout/retorno", false)]
    [InlineData("/textos/aceptar", false)]
    [InlineData("pedidos", false)]
    [InlineData("", false)]
    public void ElRetorno_SoloEsUnaRutaLocalSimpleSinTokens(string ruta, bool valido)
    {
        Assert.Equal(valido, ResolverAccesoConTextosPendientes.RetornoValido(ruta, RolWeb.Cliente));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-42")]
    public void LaRutaDelPersonal_SoloEsRetornoValidoParaElPersonal()
    {
        Assert.False(ResolverAccesoConTextosPendientes.RetornoValido("/sucursal/inventario", RolWeb.Cliente));
        Assert.False(ResolverAccesoConTextosPendientes.RetornoValido("/admin/usuarios", RolWeb.Cliente));
        Assert.True(ResolverAccesoConTextosPendientes.RetornoValido("/sucursal/inventario", RolWeb.Encargado));
        Assert.True(ResolverAccesoConTextosPendientes.RetornoValido("/admin/usuarios", RolWeb.AdministradorCentral));
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-42")]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task UnFragmentoOUnEndpointSinRetorno_NuncaSeGuardaComoDestino(bool sinRetorno, bool fragmento)
    {
        var e = new Entorno();
        var cookie = await Abrir(e);

        await e.Resolver().Resolver(Pedir(Cliente, capacidad: null, cookie: cookie, sinRetorno: sinRetorno, pantallaCompleta: !fragmento));

        Assert.Null(await e.Resolver().TomarRetorno(cookie, RolWeb.Cliente));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-42")]
    public async Task UnaRutaGuardadaQueYaNoEsValidaParaElRol_NoSeEntrega()
    {
        var e = new Entorno();
        var cookie = await Abrir(e);
        await e.Almacen.GuardarRetorno(ValorDeCookieHash(cookie), "/admin/usuarios");

        Assert.Null(await e.Resolver().TomarRetorno(cookie, RolWeb.Cliente));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-42")]
    public async Task UnaCookieConFormaInvalida_NoGuardaNiEntregaNada()
    {
        var e = new Entorno();

        var decision = await e.Resolver().Resolver(Pedir(Cliente, capacidad: null, cookie: "mala"));

        Assert.Equal(TipoDeDecisionDeAcceso.Redirige, decision.Tipo);
        Assert.Null(await e.Resolver().TomarRetorno("mala", RolWeb.Cliente));
    }

    private static async Task<string> Abrir(Entorno e)
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();
        foreach (var fila in d.Almacen.Filas)
        {
            await e.Almacen.Insertar(fila.Value);
        }

        return cookie;
    }

    private static BabelHub.Ecommerce.Domain.Web.HashDeSesion ValorDeCookieHash(string cookie) =>
        ValorDeCookieDeSesion.Hash(cookie);
}
