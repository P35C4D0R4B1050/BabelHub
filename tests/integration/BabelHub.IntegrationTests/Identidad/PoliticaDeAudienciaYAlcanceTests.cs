using System.Security.Cryptography;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Identidad;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;
using EstadoWeb = BabelHub.Ecommerce.Application.Web.Interno.EstadoDeVerificacion;
using IReloj = BabelHub.Ecommerce.Application.Plataforma.IReloj;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// La política de autorización por audiencia y alcance (identidad-seguridad T44; AC-IDE-27, AC-IDE-30; IDE-30, IDE-33): con los verificadores <b>reales</b> de los dos servicios y el contexto de lectura real, una credencial
/// de acceso sirve en el E-commerce y en el Inventario pero nunca como contexto de lectura, y el contexto del asistente (<c>aud=pedidos</c>, <c>typ=lectura-asistente</c>) lo acepta solo Pedidos y para su propio cliente:
/// <b>todo endpoint de escritura, de otro cliente, de la biblioteca, de la cuenta o de la descarga se autentica con el verificador de credenciales y por tanto lo rechaza</b>. El mapeo de alcance a operación de las
/// tareas de Identidad con el Inventario (pendientes y bajas) sale del catálogo.
/// </summary>
public sealed class PoliticaDeAudienciaYAlcanceTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    private sealed class ClaveEnMemoria(string kid, ECDsa clave) : IClavePrivadaDeFirma
    {
        public Task<ClavePrivadaActiva> Activa(CancellationToken cancelacion = default) => Task.FromResult(new ClavePrivadaActiva(kid, clave));
    }

    private sealed class FuenteDelEcommerce(FotoDeClavesDeVerificacion foto) : BabelHub.Ecommerce.Infrastructure.Identidad.IFuenteDeClavesDeVerificacion
    {
        public FotoDeClavesDeVerificacion? Actual { get; } = foto;
    }

    private sealed class FuenteDelInventario(CopiaDeClavesDeVerificacion copia) : BabelHub.Inventario.Infrastructure.Plataforma.IFuenteDeClavesDeVerificacion
    {
        public CopiaDeClavesDeVerificacion? Actual { get; } = copia;
    }

    private sealed class RelojFijo : IReloj
    {
        public DateTimeOffset Ahora => PoliticaDeAudienciaYAlcanceTests.Ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(PoliticaDeAudienciaYAlcanceTests.Ahora.UtcDateTime.AddHours(-5));
    }

    private sealed class ActorFijo : IActorActual
    {
        public ActorActual? Actual { get; set; }
    }

    private sealed class EstadoVigente : IEstadoDeCuenta
    {
        public Task<EstadoDeCuenta?> Consultar(Guid usuarioId, CancellationToken cancelacion = default) =>
            Task.FromResult<EstadoDeCuenta?>(new EstadoDeCuenta(usuarioId, true, EstadoDeAceptacionDeLosTextos.AlDia, true, false, false));

        public Task<ResultadoDeLaAceptacion> AceptarLosTextos(Guid usuarioId, string versionMostrada, CancellationToken cancelacion = default) =>
            Task.FromResult(ResultadoDeLaAceptacion.NoDisponible);
    }

    private sealed class DenegacionesNulas : IDenegacionDeAcceso
    {
        public int Cantidad { get; private set; }

        public Task Registrar(DenegacionDeAcceso denegacion, CancellationToken cancelacion = default)
        {
            Cantidad++;
            return Task.CompletedTask;
        }
    }

    private sealed class Escenario
    {
        public Escenario()
        {
            const string Kid = "kid-de-politica-uno";
            var clave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var claves = new Dictionary<string, ECDsa> { [Kid] = clave };
            var foto = new FotoDeClavesDeVerificacion(claves, new HashSet<string>(), Ahora);
            var fuente = new FuenteDelEcommerce(foto);
            Emisor = new Es256EmisorDeCredenciales(new ClaveEnMemoria(Kid, clave));
            Web = new VerificadorDeCredencialDeLaWeb(new Es256VerificadorDeCredenciales(fuente));
            Inventario = new VerificadorDeCredencialesDelInventario(new FuenteDelInventario(new CopiaDeClavesDeVerificacion(claves, new HashSet<string>(), Ahora)));
            Firma = new Es256FirmaDeContextosDeLectura(new ClaveEnMemoria(Kid, clave), fuente, new RelojFijoDeTiempo());
            Actor = new ActorFijo();
            Denegaciones = new DenegacionesNulas();
            Contexto = new ContextoDeLecturaDelAsistente(new AlcanceDeLaCuenta(Actor, Denegaciones), new EstadoVigente(), Firma, new RelojFijo());
        }

        public Es256EmisorDeCredenciales Emisor { get; }

        public VerificadorDeCredencialDeLaWeb Web { get; }

        public VerificadorDeCredencialesDelInventario Inventario { get; }

        public Es256FirmaDeContextosDeLectura Firma { get; }

        public ActorFijo Actor { get; }

        public DenegacionesNulas Denegaciones { get; }

        public ContextoDeLecturaDelAsistente Contexto { get; }

        public async Task<string> Credencial(Guid? usuario = null) =>
            (await Emisor.Emitir(new DatosDeLaCredencial(usuario ?? Guid.NewGuid(), RolDeUsuario.Cliente, null, true), Ahora)).Jwt;
    }

    private sealed class RelojFijoDeTiempo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private static async Task<string> ContextoDe(Escenario e, Guid cliente)
    {
        e.Actor.Actual = ActorActual.DeUsuario(cliente, BabelHub.Shared.Contratos.RolesDeUsuario.Cliente, null);
        var resultado = await e.Contexto.Emitir(cliente);
        Assert.True(resultado.EsExito);
        return resultado.Valor.Revelar();
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public async Task ElContextoDeLectura_NoSirveEnElEcommerceNiEnElInventario_PorLoTantoNiParaEscribirNiParaLaBibliotecaLaCuentaOLaDescarga()
    {
        var e = new Escenario();
        var contexto = await ContextoDe(e, Guid.NewGuid());

        // Todo endpoint (escritura, otro cliente, biblioteca, cuenta, descarga, Inventario) autentica con el verificador de credenciales: el contexto no pasa.
        Assert.Equal(EstadoWeb.Invalida, e.Web.Verificar(contexto, Ahora).Estado);
        Assert.Equal(BabelHub.Inventario.Application.Plataforma.EstadoDeLaCredencial.Invalida, e.Inventario.Verificar(contexto, Ahora).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public async Task UnaCredencialDeAcceso_ServiriaEnLosDosServicios_PeroNoComoContextoDeLectura()
    {
        var e = new Escenario();
        var cliente = Guid.NewGuid();
        var credencial = await e.Credencial(cliente);
        e.Actor.Actual = ActorActual.DeUsuario(cliente, BabelHub.Shared.Contratos.RolesDeUsuario.Cliente, null);

        Assert.Equal(EstadoWeb.Valida, e.Web.Verificar(credencial, Ahora).Estado);
        Assert.Equal(BabelHub.Inventario.Application.Plataforma.EstadoDeLaCredencial.Valida, e.Inventario.Verificar(credencial, Ahora).Estado);
        var comoContexto = await e.Contexto.Verificar(new ValorSecreto(credencial));
        Assert.False(comoContexto.EsExito);
        Assert.Equal(CategoriaDeError.NoAutenticado, comoContexto.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public async Task ElContextoLoAceptaSoloPedidos_ParaSuPropioCliente_YNadaMas()
    {
        var e = new Escenario();
        var cliente = Guid.NewGuid();
        var contexto = await ContextoDe(e, cliente);

        var verificado = await e.Contexto.Verificar(new ValorSecreto(contexto));

        Assert.True(verificado.EsExito);
        Assert.Equal(cliente, verificado.Valor.ClienteId);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public async Task ElContextoDeOtroCliente_NoSePideNiSeEmite_YLaDenegacionSeRegistra()
    {
        var e = new Escenario();
        var otro = Guid.NewGuid();
        e.Actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), BabelHub.Shared.Contratos.RolesDeUsuario.Cliente, null);

        var resultado = await e.Contexto.Emitir(otro);

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(1, e.Denegaciones.Cantidad);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public async Task ElActorDeLaSesionQueNoEsElDeLaSolicitud_NoObtieneElContextoPorLaWeb_YLaDenegacionSeRegistra()
    {
        var e = new Escenario();
        var clienteDeLaSesion = Guid.NewGuid();
        var otroActorDeLaSolicitud = Guid.NewGuid();
        e.Actor.Actual = ActorActual.DeUsuario(otroActorDeLaSolicitud, BabelHub.Shared.Contratos.RolesDeUsuario.Cliente, null);
        var web = new BabelHub.Ecommerce.Application.Web.Interno.ContextoDelAsistenteDeLaWeb(e.Contexto);

        var resultado = await web.Pedir(new BabelHub.Ecommerce.Application.Web.ActorVerificado(clienteDeLaSesion, BabelHub.Ecommerce.Application.Web.RolWeb.Cliente, null));

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(1, e.Denegaciones.Cantidad);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-es-un-token")]
    [InlineData("a.b.c")]
    [Trait("Criterio", "AC-IDE-30")]
    public async Task UnContextoMalFormado_DaElMismoErrorQueUnoInvalido_SinDecirQueFallo(string basura)
    {
        var e = new Escenario();
        var invalido = await e.Contexto.Verificar(new ValorSecreto(basura.Length == 0 ? "x" : basura));

        Assert.False(invalido.EsExito);
        Assert.Equal(ContextoDeLecturaDelAsistente.ContextoInvalido, invalido.Error);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-27")]
    public void LasTareasDeIdentidadConElInventario_UsanElAlcanceMinimoDeSuOperacion()
    {
        var porNombre = CatalogoDeOperaciones.V1.ToDictionary(o => o.Nombre, StringComparer.Ordinal);

        // La consulta de pendientes y la baja del titular tienen cada una su alcance, sin token de usuario y distintos entre sí: una credencial no hace la tarea de la otra.
        Assert.Equal(AlcancesDeCredencial.ConsultaDePendientes, porNombre["inventario.titulares.consultar-pendientes"].Alcance);
        Assert.Equal(AlcancesDeCredencial.BajaDeTitular, porNombre["inventario.titulares.baja"].Alcance);
        Assert.NotEqual(porNombre["inventario.titulares.consultar-pendientes"].Alcance, porNombre["inventario.titulares.baja"].Alcance);
        Assert.False(porNombre["inventario.titulares.baja"].RequiereTokenDeUsuario);
        Assert.False(porNombre["inventario.titulares.consultar-pendientes"].RequiereTokenDeUsuario);

        // Ningún otro operación del Inventario comparte esos dos alcances: no hay una credencial que sirva para algo más.
        Assert.Single(CatalogoDeOperaciones.V1, o => o.Alcance == AlcancesDeCredencial.BajaDeTitular);
        Assert.Single(CatalogoDeOperaciones.V1, o => o.Alcance == AlcancesDeCredencial.ConsultaDePendientes);
    }
}
