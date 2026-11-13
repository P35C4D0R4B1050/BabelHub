using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

public sealed class AlcancesDeServicioTests
{
    private const string Resumen = "k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE";
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed class ActorFijo(ActorActual? actor) : IActorActual
    {
        public ActorActual? Actual { get; set; } = actor;
    }

    private sealed class CalculadorDeHuellaFalso : ICalculadorDeHuella
    {
        public HuellaDeOrigen DeOrigen(string direccion) =>
            HuellaDeOrigen.Crear(Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(direccion))).TrimEnd('=').Replace('+', '-').Replace('/', '_'), "v1");

        public HuellaDeSujeto DeSujeto(string valor, string proposito) =>
            HuellaDeSujeto.Crear(Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(proposito + valor))).TrimEnd('=').Replace('+', '-').Replace('/', '_'), "v1");
    }

    private static readonly AlcanceDeServicio[] Alcances = Enum.GetValues<AlcanceDeServicio>();

    private readonly RelojFijo _reloj = new(Ahora);
    private readonly PublicadorDeEventosEspia _eventos = new();
    private readonly ActorFijo _actor = new(ActorActual.DeUsuario(Guid.NewGuid(), "cliente"));
    private readonly OrigenFijo _origen = new(new OrigenDeSolicitud(HuellaDeOrigen.Crear(Resumen, "v1"), "10.0.0.5"));

    private static OperacionDeServicio Operacion(AlcanceDeServicio alcance) => new($"prueba.operacion-{alcance.NombreEstable()}");

    private AutorizarLlamadaDeServicio Autorizador()
    {
        var matriz = new MatrizDeAlcances(Alcances.Select(a => (Operacion(a), a)));
        return new AutorizarLlamadaDeServicio(matriz, _actor, _origen, _eventos, _reloj);
    }

    public static TheoryData<AlcanceDeServicio, AlcanceDeServicio> ParesDeAlcances()
    {
        var datos = new TheoryData<AlcanceDeServicio, AlcanceDeServicio>();
        foreach (var credencial in Enum.GetValues<AlcanceDeServicio>())
        {
            foreach (var exigido in Enum.GetValues<AlcanceDeServicio>())
            {
                datos.Add(credencial, exigido);
            }
        }

        return datos;
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-14")]
    public async Task Autorizar_SinCredencial_RechazaYRegistraUnEventoConElOrigen()
    {
        var resultado = await Autorizador().Autorizar(new LlamadaDeServicio(Operacion(AlcanceDeServicio.LecturaDeDisponibilidad), null));

        Assert.False(resultado.Autorizada);
        Assert.Equal(MotivoDeRechazoDeLlamada.SinCredencial, resultado.Motivo);
        var evento = Assert.Single(_eventos.Eventos);
        Assert.Equal(TipoDeEventoDeSeguridad.LlamadaEntreServiciosSinCredencial, evento.Tipo);
        Assert.Equal("sin-credencial", evento.Motivo);
        Assert.Equal("10.0.0.5", evento.DireccionCompleta);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-14")]
    [MemberData(nameof(ParesDeAlcances))]
    public async Task Autorizar_ConLaMatrizCompleta_SoloAutorizaElAlcanceQueLaOperacionDeclara(AlcanceDeServicio credencial, AlcanceDeServicio exigido)
    {
        var resultado = await Autorizador().Autorizar(new LlamadaDeServicio(Operacion(exigido), credencial));

        Assert.Equal(credencial == exigido, resultado.Autorizada);
        if (credencial != exigido)
        {
            Assert.Equal(MotivoDeRechazoDeLlamada.OtroAlcance, resultado.Motivo);
            Assert.Equal("otro-alcance", Assert.Single(_eventos.Eventos).Motivo);
        }
        else
        {
            Assert.Empty(_eventos.Eventos);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-14")]
    public async Task Autorizar_ConUnaOperacionQueNoDeclaroSuAlcance_RechazaPorDefectoConCualquierCredencial()
    {
        var autorizador = Autorizador();

        foreach (var alcance in Alcances)
        {
            var resultado = await autorizador.Autorizar(new LlamadaDeServicio(new OperacionDeServicio("prueba.sin-declarar"), alcance));

            Assert.Equal(MotivoDeRechazoDeLlamada.OperacionNoDeclarada, resultado.Motivo);
        }

        Assert.Equal(Alcances.Length, _eventos.Eventos.Count);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-14")]
    [InlineData("anonimo")]
    [InlineData("sistema")]
    [InlineData("integracion")]
    [InlineData("ninguno")]
    public async Task Autorizar_UnaOperacionDeUsuarioSinContextoDeUsuario_Rechaza(string actor)
    {
        _actor.Actual = actor switch
        {
            "anonimo" => ActorActual.Anonimo(),
            "sistema" => ActorActual.DelSistema(),
            "integracion" => ActorActual.DeIntegracion("pasarela"),
            _ => null,
        };

        var resultado = await Autorizador().Autorizar(new LlamadaDeServicio(Operacion(AlcanceDeServicio.OperacionDeUsuario), AlcanceDeServicio.OperacionDeUsuario));

        Assert.Equal(MotivoDeRechazoDeLlamada.SinContextoDeUsuario, resultado.Motivo);
        Assert.Equal("sin-contexto-de-usuario", Assert.Single(_eventos.Eventos).Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-14")]
    public async Task Autorizar_UnaOperacionDeServicioConElActorDelSistema_Autoriza()
    {
        _actor.Actual = ActorActual.DelSistema();

        var resultado = await Autorizador().Autorizar(new LlamadaDeServicio(Operacion(AlcanceDeServicio.ExpiracionDeReservas), AlcanceDeServicio.ExpiracionDeReservas));

        Assert.True(resultado.Autorizada);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-14")]
    public void ContextoDelActor_SinContexto_RechazaLaLlamadaEntreModulos()
    {
        var sinContexto = new ActorFijo(null);

        Assert.Throws<ContextoDeActorAusenteException>(() => sinContexto.Exigir());
        Assert.Equal(TipoDeActor.Anonimo, new ActorFijo(ActorActual.Anonimo()).Exigir().Tipo);
        Assert.Equal(TipoDeActor.Usuario, _actor.Exigir().Tipo);
    }

    [Fact]
    public void MatrizDeAlcances_ConUnaOperacionDeclaradaDosVeces_Lanza()
    {
        var operacion = new OperacionDeServicio("a.b");

        Assert.Throws<ArgumentException>(() => new MatrizDeAlcances([(operacion, AlcanceDeServicio.Historial), (operacion, AlcanceDeServicio.MarcarAlerta)]));
        Assert.Null(new MatrizDeAlcances([]).Exigido(operacion));
    }

    [Fact]
    public void AlcancesDeServicio_TienenLosDoceNombresEstablesDeLaSpecYSeConviertenDeIda()
    {
        var nombres = Alcances.Select(a => a.NombreEstable()).ToList();

        Assert.Equal(12, nombres.Distinct().Count());
        Assert.Contains("operacion-de-usuario", nombres);
        Assert.Contains("origen-reenviado", nombres);
        Assert.Contains("lectura-alertas", nombres);
        foreach (var alcance in Alcances)
        {
            Assert.True(AlcancesDeServicio.TryParse(alcance.NombreEstable(), out var vuelta));
            Assert.Equal(alcance, vuelta);
        }

        Assert.False(AlcancesDeServicio.TryParse("administrador", out _));
        Assert.False(AlcancesDeServicio.TryParse(null, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("con espacio")]
    [InlineData("a/b")]
    public void OperacionDeServicio_ConUnNombreInvalido_Lanza(string valor)
    {
        Assert.Throws<ArgumentException>(() => new OperacionDeServicio(valor));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task Resolver_ConLaDireccionReenviadaYLaCredencialDeOrigen_UsaLaDireccionDelCliente()
    {
        var resolutor = new ResolverOrigenDeLlamadaDeServicio(new CalculadorDeHuellaFalso(), _eventos, _reloj);

        var resuelto = await resolutor.Resolver("10.0.0.5", "203.0.113.7", credencialDeOrigenValida: true);

        Assert.Equal("203.0.113.7", resuelto.Origen.Direccion);
        Assert.NotEqual(new CalculadorDeHuellaFalso().DeOrigen("10.0.0.5"), resuelto.Origen.Huella);
        Assert.True(resuelto.AplicaLimitesPorOrigen);
        Assert.False(resuelto.ReenvioIgnorado);
        Assert.Empty(_eventos.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-29")]
    public async Task Resolver_ConLaDireccionReenviadaSinLaCredencial_LaIgnoraUsaLaDirectaSinLimitesPorOrigenYRegistraUnEventoSinLaDireccion()
    {
        var resolutor = new ResolverOrigenDeLlamadaDeServicio(new CalculadorDeHuellaFalso(), _eventos, _reloj);

        var resuelto = await resolutor.Resolver("10.0.0.5", "203.0.113.7", credencialDeOrigenValida: false);

        Assert.Equal("10.0.0.5", resuelto.Origen.Direccion);
        Assert.Equal(new CalculadorDeHuellaFalso().DeOrigen("10.0.0.5"), resuelto.Origen.Huella);
        Assert.False(resuelto.AplicaLimitesPorOrigen);
        Assert.True(resuelto.ReenvioIgnorado);
        var evento = Assert.Single(_eventos.Eventos);
        Assert.Equal(TipoDeEventoDeSeguridad.OrigenReenviadoSinCredencial, evento.Tipo);
        Assert.Null(evento.DireccionCompleta);
        Assert.Equal(resuelto.Origen.Huella, evento.Origen);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task Resolver_SinDireccionReenviada_UsaLaDirecta()
    {
        var resolutor = new ResolverOrigenDeLlamadaDeServicio(new CalculadorDeHuellaFalso(), _eventos, _reloj);

        var resuelto = await resolutor.Resolver("10.0.0.5", null, credencialDeOrigenValida: false);

        Assert.Equal("10.0.0.5", resuelto.Origen.Direccion);
        Assert.True(resuelto.AplicaLimitesPorOrigen);
        Assert.Empty(_eventos.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task Resolver_SinDireccionDirecta_EsElOrigenDesconocido()
    {
        var resolutor = new ResolverOrigenDeLlamadaDeServicio(new CalculadorDeHuellaFalso(), _eventos, _reloj);

        var resuelto = await resolutor.Resolver(null, null, credencialDeOrigenValida: false);

        Assert.True(resuelto.Origen.Huella.EsDesconocido);
    }

    [Fact]
    public void ActorActual_Anonimo_NoTraeUsuarioNiRol()
    {
        var anonimo = ActorActual.Anonimo();

        Assert.Equal(TipoDeActor.Anonimo, anonimo.Tipo);
        Assert.Null(anonimo.UsuarioId);
        Assert.Null(anonimo.Rol);
    }
}
