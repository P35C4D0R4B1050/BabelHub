using System.Security.Cryptography;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using BabelHub.Ecommerce.Infrastructure.Identidad;
using Doble = BabelHub.IntegrationTests.Identidad.ComposicionDeIdentidadTests.Doble;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// Renovaciones y revocaciones simultáneas con el caso de uso real de sesiones sobre PostgreSQL (identidad-seguridad T49; AC-IDE-38, AC-IDE-40, AC-IDE-50; IDE-14, IDE-46, IDE-60): rotar y revocar corren en una transacción
/// que bloquea la fila del usuario, así que <b>dos renovaciones simultáneas del mismo valor tienen un solo éxito y la reutilización revoca la familia, de modo que no queda ninguna fila vigente</b> y <b>una renovación concurrente con «cerrar todas» no deja ninguna vigente</b>, sea cual sea el
/// orden en que lleguen. (Los códigos TOTP, de respaldo, de recuperación y los intentos simultáneos los prueban los repositorios en <c>RepositoriosDeIdentidadTests</c>.)
/// </summary>
public sealed class ConcurrenciaDeSesionesTests(PostgresDeIdentidadFixture bd) : IClassFixture<PostgresDeIdentidadFixture>
{
    private const string Resumen = "$argon2id$v=19$m=19456,t=2,p=1$c2FsLXNhbC1zYWwtc2FsYQ$c2FsLXNhbC1zYWwtc2Fsc2FsLXNhbC1zYWwtc2Fsc2E";

    private sealed class RelojDelSistema : IReloj
    {
        public DateTimeOffset Ahora => DateTimeOffset.UtcNow;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime.AddHours(-5));
    }

    private sealed class ActorFijo : IActorActual
    {
        public ActorActual? Actual { get; set; }
    }

    private sealed class ClaveEnMemoria(string kid, ECDsa clave) : IClavePrivadaDeFirma
    {
        public Task<ClavePrivadaActiva> Activa(CancellationToken cancelacion = default) => Task.FromResult(new ClavePrivadaActiva(kid, clave));
    }

    private sealed class Entorno
    {
        public Entorno(PostgresDeIdentidadFixture bd)
        {
            Bd = bd;
            var reloj = new RelojDelSistema();
            Cuentas = new PostgresRepositorioDeCuentas(bd.AccesoAlEsquema);
            Renovaciones = new PostgresRepositorioDeRenovaciones(bd.AccesoAlEsquema);
            var huellas = new HmacHuellasDeIdentidad(ClaveVersionadaDeIdentidad.Analizar($"v1:{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}", "pimienta"));
            var secretos = new CsprngGeneradorDeSecretos();
            Emision = new EmisionDeSesion(Renovaciones, new Es256EmisorDeCredenciales(new ClaveEnMemoria("kid-de-concurrencia-01", ECDsa.Create(ECCurve.NamedCurves.nistP256))), secretos, huellas);
            var revocacion = new RevocacionDeSesiones(Cuentas, Renovaciones, reloj);
            Actor = new ActorFijo();
            Sesiones = new Sesiones(
                Cuentas,
                Renovaciones,
                huellas,
                secretos,
                Emision,
                revocacion,
                bd.Unidad,
                new EventosDeSeguridadDeIdentidad(Doble.De<IRegistroDeSeguridad>()),
                Doble.De<IAlertas>(),
                Actor,
                Doble.De<IDenegacionDeAcceso>(),
                reloj);
        }

        public PostgresDeIdentidadFixture Bd { get; }

        public PostgresRepositorioDeCuentas Cuentas { get; }

        public PostgresRepositorioDeRenovaciones Renovaciones { get; }

        public EmisionDeSesion Emision { get; }

        public ActorFijo Actor { get; }

        public Sesiones Sesiones { get; }

        public async Task<(Guid Usuario, string ValorOpaco)> UnaSesion()
        {
            var usuario = Usuario.Restaurar(Guid.NewGuid(), $"c{Guid.NewGuid():N}@example.com", RolDeUsuario.Cliente, null, true, true);
            var cuenta = new CuentaDeIdentidad
            {
                Usuario = usuario,
                Nombre = "Ana Pérez",
                ResumenDeAcceso = Resumen,
                Aceptacion = AceptacionDeTextos.Restaurar(DateTimeOffset.UtcNow, [(TextoLegal.Terminos, "t1", DateTimeOffset.UtcNow)]),
                CreadaEn = DateTimeOffset.UtcNow,
            };
            Assert.True(await Cuentas.Insertar(cuenta));
            var credencial = await Bd.Unidad.Ejecutar(ct => Emision.Nueva(usuario, DateTimeOffset.UtcNow, ct));
            Actor.Actual = ActorActual.DeUsuario(usuario.Id, "cliente", null);
            return (usuario.Id, credencial.Renovacion.Revelar());
        }

        public Task<long> Vigentes(Guid usuario) =>
            Bd.Consultar<long>($"SELECT count(*) FROM identidad.renovaciones WHERE usuario_id = '{usuario}' AND usada_en IS NULL AND revocada_en IS NULL");
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-50")]
    public async Task DosRenovacionesSimultaneasDelMismoValor_NuncaDejanDosFilasVigentes_NiDosExitos()
    {
        var e = new Entorno(bd);

        for (var intento = 0; intento < 10; intento++)
        {
            var (usuario, valor) = await e.UnaSesion();

            var resultados = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => e.Sesiones.Renovar(new ValorSecreto(valor)))));

            // Exactamente una gana; las demás ven un valor ya usado, lo tratan como reutilización y revocan la familia: no queda ninguna fila vigente.
            Assert.Equal(1, resultados.Count(r => r.EsExito));
            Assert.Equal(0, await e.Vigentes(usuario));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-50")]
    public async Task UnaRenovacionConcurrenteConCerrarTodas_NoDejaNingunaVigente_SeaCualSeaElOrden()
    {
        var e = new Entorno(bd);

        for (var intento = 0; intento < 15; intento++)
        {
            var (usuario, valor) = await e.UnaSesion();

            var renovar = Task.Run(() => e.Sesiones.Renovar(new ValorSecreto(valor)));
            var cerrar = Task.Run(() => e.Sesiones.CerrarTodas(usuario));
            await Task.WhenAll(renovar, cerrar);

            Assert.True((await cerrar).EsExito);
            Assert.Equal(0, await e.Vigentes(usuario));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-50")]
    public async Task TrasCerrarTodas_NingunaRenovacionDeLaPersonaVuelveAFuncionar()
    {
        var e = new Entorno(bd);
        var (usuario, valor) = await e.UnaSesion();
        var segunda = await bd.Unidad.Ejecutar(ct => e.Emision.Nueva(Usuario.Restaurar(usuario, "x@example.com", RolDeUsuario.Cliente, null, true, true), DateTimeOffset.UtcNow, ct));
        var tercera = await bd.Unidad.Ejecutar(ct => e.Emision.Nueva(Usuario.Restaurar(usuario, "x@example.com", RolDeUsuario.Cliente, null, true, true), DateTimeOffset.UtcNow, ct));
        Assert.Equal(3, await e.Vigentes(usuario));

        Assert.True((await e.Sesiones.CerrarTodas(usuario)).EsExito);

        Assert.Equal(0, await e.Vigentes(usuario));
        foreach (var renovacion in new[] { valor, segunda.Renovacion.Revelar(), tercera.Renovacion.Revelar() })
        {
            Assert.False((await e.Sesiones.Renovar(new ValorSecreto(renovacion))).EsExito);
        }
    }
}
