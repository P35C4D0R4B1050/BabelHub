using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using BabelHub.Ecommerce.Infrastructure.Identidad;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// El estado de la cuenta y la aceptación de los textos sobre PostgreSQL (identidad-seguridad T13, IDE-27, IDE-29, IDE-43, IDE-57, IDE-58; AC-IDE-14, AC-IDE-47): «puede comprar» exige cuenta activa, correo verificado
/// y los tres textos vigentes aceptados; aceptar cita el <b>conjunto</b> vigente (<c>terminos:V|privacidad:V|reembolso:V</c>) y una versión publicada mientras la persona leía se rechaza sin cambiar nada.
/// </summary>
public sealed class EstadoDeCuentaDeIdentidadTests : IClassFixture<PostgresDeIdentidadFixture>
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private const string Resumen = "$argon2id$v=19$m=19456,t=2,p=1$c2FsLXNhbC1zYWwtc2FsYQ$c2FsLXNhbC1zYWwtc2Fsc2FsLXNhbC1zYWwtc2Fsc2E";

    private sealed class RelojFijo : IReloj
    {
        public DateTimeOffset Ahora => EstadoDeCuentaDeIdentidadTests.Ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(EstadoDeCuentaDeIdentidadTests.Ahora.UtcDateTime.AddHours(-5));
    }

    private readonly PostgresDeIdentidadFixture _bd;
    private readonly PostgresRepositorioDeCuentas _cuentas;
    private readonly EstadoDeCuentaDeIdentidad _estado;

    public EstadoDeCuentaDeIdentidadTests(PostgresDeIdentidadFixture bd)
    {
        _bd = bd;
        _cuentas = new PostgresRepositorioDeCuentas(bd.AccesoAlEsquema);
        _estado = new EstadoDeCuentaDeIdentidad(_cuentas, new PostgresRepositorioDeCancelaciones(bd.AccesoAlEsquema), new PostgresVersionesDeTextosVigentes(bd.AccesoAlEsquema), bd.Unidad, new RelojFijo());
        bd.Ejecutar("INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso) VALUES (1, 'T1', 'P1', 'R1') ON CONFLICT (id) DO UPDATE SET terminos = 'T1', privacidad = 'P1', reembolso = 'R1'").GetAwaiter().GetResult();
    }

    private async Task<Guid> Cuenta(bool alDia = true, bool correoVerificado = true, bool activo = true, bool conWhatsApp = false)
    {
        var aceptadas = alDia
            ? new[] { (TextoLegal.Terminos, "T1", Ahora), (TextoLegal.Privacidad, "P1", Ahora), (TextoLegal.Reembolso, "R1", Ahora) }
            : new[] { (TextoLegal.Terminos, "T1", Ahora), (TextoLegal.Privacidad, "P1", Ahora) };
        var cuenta = new CuentaDeIdentidad
        {
            Usuario = Usuario.Restaurar(Guid.NewGuid(), $"e{Guid.NewGuid():N}@example.com", RolDeUsuario.Cliente, null, correoVerificado, activo),
            Nombre = "Ana Pérez",
            ResumenDeAcceso = Resumen,
            Aceptacion = AceptacionDeTextos.Restaurar(Ahora, aceptadas),
            CreadaEn = Ahora,
        };
        if (conWhatsApp)
        {
            cuenta.Whatsapp.GuardarNumero("+51999111222", Ahora);
            cuenta.Whatsapp.Consentir("+51999111222", Ahora);
        }

        Assert.True(await _cuentas.Insertar(cuenta));
        return cuenta.Usuario.Id;
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-47")]
    public async Task UnaCuentaActivaVerificadaYAlDia_PuedeComprar()
    {
        var id = await Cuenta(conWhatsApp: true);

        var estado = await _estado.Consultar(id);

        Assert.NotNull(estado);
        Assert.True(estado.PuedeComprar);
        Assert.Equal(EstadoDeAceptacionDeLosTextos.AlDia, estado.Textos);
        Assert.True(estado.CorreoVerificado);
        Assert.False(estado.CuentaCanceladaODesactivada);
        Assert.True(estado.TieneWhatsAppConConsentimiento);
        Assert.Equal(id, estado.UsuarioId);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-47")]
    public async Task ConUnTextoPendiente_NoPuedeComprar_PeroLaCuentaExiste()
    {
        var estado = await _estado.Consultar(await Cuenta(alDia: false));

        Assert.NotNull(estado);
        Assert.False(estado.PuedeComprar);
        Assert.Equal(EstadoDeAceptacionDeLosTextos.Pendiente, estado.Textos);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-47")]
    public async Task ConElCorreoSinVerificar_NoPuedeComprar()
    {
        var estado = await _estado.Consultar(await Cuenta(correoVerificado: false));

        Assert.NotNull(estado);
        Assert.False(estado.PuedeComprar);
        Assert.False(estado.CorreoVerificado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-47")]
    public async Task UnaCuentaDesactivada_NoPuedeComprar_YSeInformaComoCanceladaODesactivada()
    {
        var estado = await _estado.Consultar(await Cuenta(activo: false));

        Assert.NotNull(estado);
        Assert.False(estado.PuedeComprar);
        Assert.True(estado.CuentaCanceladaODesactivada);
    }

    [Fact]
    public async Task UnaCuentaInexistente_OSinIdentificador_DevuelveNulo()
    {
        Assert.Null(await _estado.Consultar(Guid.NewGuid()));
        Assert.Null(await _estado.Consultar(Guid.Empty));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public async Task AceptarElConjuntoVigente_RegistraLosTresTextos_YLaCuentaQuedaAlDia()
    {
        var id = await Cuenta(alDia: false);

        var resultado = await _estado.AceptarLosTextos(id, "terminos:T1|privacidad:P1|reembolso:R1");

        Assert.Equal(ResultadoDeLaAceptacion.Aceptada, resultado);
        var estado = await _estado.Consultar(id);
        Assert.Equal(EstadoDeAceptacionDeLosTextos.AlDia, estado!.Textos);
        Assert.True(estado.PuedeComprar);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public async Task UnaVersionPublicadaMientrasLeia_SeRechaza_YNoCambiaNada()
    {
        var id = await Cuenta(alDia: false);
        await _bd.Ejecutar("UPDATE identidad.versiones_de_textos SET terminos = 'T2' WHERE id = 1");

        try
        {
            var resultado = await _estado.AceptarLosTextos(id, "terminos:T1|privacidad:P1|reembolso:R1");

            Assert.Equal(ResultadoDeLaAceptacion.VersionCambiada, resultado);
            Assert.Equal(EstadoDeAceptacionDeLosTextos.Pendiente, (await _estado.Consultar(id))!.Textos);
        }
        finally
        {
            await _bd.Ejecutar("UPDATE identidad.versiones_de_textos SET terminos = 'T1' WHERE id = 1");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("terminos:T1")]
    [InlineData("terminos:T1|privacidad:P1|reembolso:R1|otro:X")]
    [InlineData("reembolso:R1|privacidad:P1|terminos:T1")]
    [InlineData("TERMINOS:T1|PRIVACIDAD:P1|REEMBOLSO:R1")]
    [Trait("Criterio", "AC-IDE-14")]
    public async Task UnaVersionQueNoEsElConjuntoExactoVigente_SeRechaza(string version)
    {
        var id = await Cuenta(alDia: false);

        Assert.Equal(ResultadoDeLaAceptacion.VersionCambiada, await _estado.AceptarLosTextos(id, version));
        Assert.Equal(EstadoDeAceptacionDeLosTextos.Pendiente, (await _estado.Consultar(id))!.Textos);
    }

    [Fact]
    public async Task AceptarSobreUnaCuentaInexistenteODesactivada_NoEstaDisponible()
    {
        const string Conjunto = "terminos:T1|privacidad:P1|reembolso:R1";

        Assert.Equal(ResultadoDeLaAceptacion.NoDisponible, await _estado.AceptarLosTextos(Guid.NewGuid(), Conjunto));
        Assert.Equal(ResultadoDeLaAceptacion.NoDisponible, await _estado.AceptarLosTextos(await Cuenta(alDia: false, activo: false), Conjunto));
        Assert.Equal(ResultadoDeLaAceptacion.VersionCambiada, await _estado.AceptarLosTextos(Guid.Empty, Conjunto));
        Assert.Equal(ResultadoDeLaAceptacion.VersionCambiada, await _estado.AceptarLosTextos(Guid.NewGuid(), new string('x', 201)));
    }
}
