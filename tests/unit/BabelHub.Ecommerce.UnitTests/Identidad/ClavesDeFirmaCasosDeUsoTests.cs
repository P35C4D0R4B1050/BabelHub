using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// La rotación y la declaración de clave comprometida (identidad-seguridad T25; AC-IDE-32, AC-IDE-33; IDE-35, IDE-36, IDE-61, IDE-85): rota a los 90 días y por orden del administrador, las credenciales de la anterior valen
/// hasta que venzan, una clave comprometida deja de aceptarse y de publicarse, las renovaciones se revocan de inmediato y todo queda auditado en la misma transacción.
/// </summary>
public sealed class ClavesDeFirmaCasosDeUsoTests
{
    private static (EntornoDeIdentidad E, CuentaDeIdentidad Admin, ClavesDeFirma Claves) Escenario(int diasDeLaClave = 10)
    {
        var e = new EntornoDeIdentidad();
        e.ConClaveActiva(diasDeLaClave);
        var admin = e.ActuarComoAdministrador();
        return (e, admin, e.Claves());
    }

    // ---------------------------------------------------------------- rotar (IDE-36)

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task UnaClaveDe91Dias_SeRota_LaAnteriorSigueValidaHastaQueVenzanSusCredenciales_YLasNuevasUsanLaNueva_YQuedaAuditada()
    {
        var (e, _, claves) = Escenario(diasDeLaClave: 91);
        e.Actor.Actual = ActorActual.DelSistema();

        var nueva = await claves.RotarSiHanPasado90Dias();

        Assert.NotNull(nueva);
        Assert.Equal("activa", nueva!.Estado);
        Assert.Equal(ClavesDeFirma.AlgoritmoDeFirma, nueva.Algoritmo);
        var juego = await e.AlmacenDeClaves.Cargar(false);
        Assert.Equal(nueva.Kid, juego.Activa.Kid);
        Assert.Equal(1, juego.Claves.Count(c => c.Estado == EstadoDeClaveDeFirma.Activa));

        // La anterior se acepta mientras dure la última credencial que firmó (15 minutos) y no después.
        Assert.True(juego.Acepta("kid-inicial-0001", e.Reloj.Ahora.AddMinutes(14)));
        Assert.False(juego.Acepta("kid-inicial-0001", e.Reloj.Ahora.AddMinutes(15)));
        Assert.True(juego.Acepta(nueva.Kid, e.Reloj.Ahora.AddMinutes(30)));

        var auditada = Assert.Single(e.Auditoria.Registradas);
        Assert.Equal(AccionesAuditables.ClaveDeFirmaRotada, auditada.Accion);
        Assert.Equal("kid-inicial-0001", auditada.Detalle["versionAnterior"]);
        Assert.Equal(nueva.Kid, auditada.Detalle["versionNueva"]);
    }

    [Fact]
    public async Task UnaClaveDe89Dias_NoSeRotaSola()
    {
        var (e, _, claves) = Escenario(diasDeLaClave: 89);
        e.Actor.Actual = ActorActual.DelSistema();

        var nueva = await claves.RotarSiHanPasado90Dias();

        Assert.Null(nueva);
        Assert.Empty(e.Auditoria.Registradas);
        Assert.Equal(0, e.GeneradorDeClaves.Generadas);
    }

    [Fact]
    public async Task UnaVezRotada_LaRotacionPeriodicaNoRotaOtraVez()
    {
        var (e, _, claves) = Escenario(diasDeLaClave: 120);
        e.Actor.Actual = ActorActual.DelSistema();
        await claves.RotarSiHanPasado90Dias();

        var otra = await claves.RotarSiHanPasado90Dias();

        Assert.Null(otra);
        Assert.Equal(1, e.GeneradorDeClaves.Generadas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task ElAdministrador_PuedeRotarLaClave_ConElBloqueoDelJuego_YQuedaAuditada()
    {
        var (e, admin, claves) = Escenario();

        var rotada = await claves.Rotar(admin.Usuario.Id);

        Assert.True(rotada.EsExito);
        Assert.Equal("activa", rotada.Valor.Estado);
        Assert.Equal($"jwk-publico-de-{rotada.Valor.Kid}", rotada.Valor.ParametrosPublicos);
        Assert.Equal(1, e.AlmacenDeClaves.Bloqueos);
        Assert.Equal(AccionesAuditables.ClaveDeFirmaRotada, Assert.Single(e.Auditoria.Registradas).Accion);
    }

    [Fact]
    public async Task SiLaAuditoriaFalla_NoSeRota()
    {
        var (e, admin, claves) = Escenario();
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => claves.Rotar(admin.Usuario.Id));

        var juego = await e.AlmacenDeClaves.Cargar(false);
        Assert.Equal("kid-inicial-0001", juego.Activa.Kid);
        Assert.Single(juego.Claves);
    }

    [Theory]
    [InlineData(RolDeUsuario.Cliente)]
    [InlineData(RolDeUsuario.EncargadoDeSucursal)]
    [InlineData(RolDeUsuario.Logistica)]
    public async Task QuienNoEsAdministrador_NoRotaNiDeclara_YQuedaRegistrado(RolDeUsuario rol)
    {
        var e = new EntornoDeIdentidad();
        e.ConClaveActiva();
        var persona = e.Cuenta("persona@example.com", rol);
        e.Actor.Actual = ActorActual.DeUsuario(persona.Usuario.Id, "otro");
        var claves = e.Claves();

        var rotar = await claves.Rotar(persona.Usuario.Id);
        var declarar = await claves.DeclararComprometida(persona.Usuario.Id, "kid-inicial-0001");

        Assert.Equal(AlcanceDeLaCuenta.Prohibido, rotar.Error);
        Assert.Equal(AlcanceDeLaCuenta.Prohibido, declarar.Error);
        Assert.Equal(2, e.Denegaciones.Registradas.Count);
        Assert.Equal(0, e.GeneradorDeClaves.Generadas);
        Assert.Empty(e.Auditoria.Registradas);
    }

    // ---------------------------------------------------------------- comprometida (IDE-61)

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task DeclararComprometidaLaActiva_RotaEnElActo_LaRechazaDeInmediato_NoLaPublica_RevocaTodasLasRenovaciones_YAudita()
    {
        var (e, admin, claves) = Escenario();
        var a = e.Cuenta("a@example.com");
        var b = e.Cuenta("b@example.com");
        var sesionA = await e.Sesiones.Nueva(a.Usuario, e.Reloj.Ahora, default);
        var sesionB = await e.Sesiones.Nueva(b.Usuario, e.Reloj.Ahora, default);

        var resultado = await claves.DeclararComprometida(admin.Usuario.Id, "kid-inicial-0001");

        Assert.True(resultado.EsExito);
        Assert.NotEqual("kid-inicial-0001", resultado.Valor.Kid);
        Assert.Equal("activa", resultado.Valor.Estado);
        var juego = await e.AlmacenDeClaves.Cargar(false);
        Assert.False(juego.Acepta("kid-inicial-0001", e.Reloj.Ahora));
        Assert.Equal(["kid-inicial-0001"], juego.Revocadas);
        Assert.DoesNotContain(await claves.Publicadas(), k => k.Kid == "kid-inicial-0001");
        Assert.Equal(["kid-inicial-0001"], await claves.Revocadas());

        // Las renovaciones de todos se revocan de inmediato.
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, (await e.Gestor().Renovar(sesionA.Renovacion)).Error);
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, (await e.Gestor().Renovar(sesionB.Renovacion)).Error);

        var auditada = Assert.Single(e.Auditoria.Registradas);
        Assert.Equal(AccionesAuditables.ClaveDeFirmaDeclaradaComprometida, auditada.Accion);
        Assert.Equal(ClavesDeFirma.MotivoDeLaDeclaracion, auditada.Motivo);
        Assert.Equal("kid-inicial-0001", auditada.Detalle["version"]);
    }

    [Fact]
    public async Task DeclararComprometidaLaAnterior_NoGeneraClaveNueva_PeroRevocaLasRenovaciones()
    {
        var (e, admin, claves) = Escenario();
        var rotada = (await claves.Rotar(admin.Usuario.Id)).Valor;
        var persona = e.Cuenta("persona@example.com");
        var sesion = await e.Sesiones.Nueva(persona.Usuario, e.Reloj.Ahora, default);
        e.Auditoria.Registradas.Clear();

        var resultado = await claves.DeclararComprometida(admin.Usuario.Id, "kid-inicial-0001");

        Assert.True(resultado.EsExito);
        Assert.Equal(rotada.Kid, resultado.Valor.Kid);
        Assert.Equal(1, e.GeneradorDeClaves.Generadas);
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, (await e.Gestor().Renovar(sesion.Renovacion)).Error);
        Assert.False((await e.AlmacenDeClaves.Cargar(false)).Acepta("kid-inicial-0001", e.Reloj.Ahora));
    }

    [Fact]
    public async Task DeclararDeNuevoLaMismaClave_EsIdempotente_YVuelveARevocarLasRenovaciones()
    {
        var (e, admin, claves) = Escenario();
        await claves.DeclararComprometida(admin.Usuario.Id, "kid-inicial-0001");
        var persona = e.Cuenta("persona@example.com");
        var sesion = await e.Sesiones.Nueva(persona.Usuario, e.Reloj.Ahora, default);

        var otra = await claves.DeclararComprometida(admin.Usuario.Id, "kid-inicial-0001");

        Assert.True(otra.EsExito);
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, (await e.Gestor().Renovar(sesion.Renovacion)).Error);
        Assert.Equal(1, e.GeneradorDeClaves.Generadas);
        Assert.Equal(1, (await e.AlmacenDeClaves.Cargar(false)).Claves.Count(c => c.Estado == EstadoDeClaveDeFirma.Activa));
    }

    [Fact]
    public async Task UnKidInexistenteOVacio_SeRechaza_SinTocarNada()
    {
        var (e, admin, claves) = Escenario();

        var inexistente = await claves.DeclararComprometida(admin.Usuario.Id, "kid-que-no-existe");
        var vacio = await claves.DeclararComprometida(admin.Usuario.Id, "   ");

        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, vacio.Error!.Categoria);
        Assert.Empty(e.Auditoria.Registradas);
        Assert.Equal(0, e.GeneradorDeClaves.Generadas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task SiLaAuditoriaFalla_LaDeclaracionNoSeAplicaNiSeRevocaNada()
    {
        var (e, admin, claves) = Escenario();
        var persona = e.Cuenta("persona@example.com");
        var sesion = await e.Sesiones.Nueva(persona.Usuario, e.Reloj.Ahora, default);
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => claves.DeclararComprometida(admin.Usuario.Id, "kid-inicial-0001"));

        var juego = await e.AlmacenDeClaves.Cargar(false);
        Assert.True(juego.Acepta("kid-inicial-0001", e.Reloj.Ahora));
        Assert.Empty(juego.Revocadas);
        Assert.True((await e.Gestor().Renovar(sesion.Renovacion)).EsExito);
    }

    // ---------------------------------------------------------------- publicar (IDE-35, IDE-85)

    [Fact]
    [Trait("Criterio", "AC-IDE-32")]
    public async Task SeLePublicaAlInventario_SoloLaClaveDeVerificacion_ConElAlgoritmoFijo_NuncaLaPrivadaNiUnaComprometida()
    {
        var (e, admin, claves) = Escenario();
        await claves.Rotar(admin.Usuario.Id);

        var publicadas = await claves.Publicadas();

        Assert.Equal(2, publicadas.Count);
        Assert.All(publicadas, k =>
        {
            Assert.Equal("ES256", k.Algoritmo);
            Assert.StartsWith("jwk-publico-de-", k.ParametrosPublicos, StringComparison.Ordinal);
            Assert.DoesNotContain("privada", k.ParametrosPublicos, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(["activa", "anterior"], publicadas.Select(k => k.Estado).OrderBy(s => s));

        // Pasado el vencimiento de las credenciales de la anterior, solo queda la activa.
        e.Reloj.Ahora = e.Reloj.Ahora.AddMinutes(16);
        Assert.Equal(["activa"], (await claves.Publicadas()).Select(k => k.Estado));
    }
}
