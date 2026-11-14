using System.Xml.Linq;
using BabelHub.Ecommerce.Domain.Web;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BabelHub.IntegrationTests.Web;

/// <summary>El anillo de claves de Data Protection en la base (T12): un anillo por host, cifrado con la clave de su host, compartido por las réplicas y re-cifrado al rotar.</summary>
[Collection(ColeccionPostgresDeLaWeb.Nombre)]
public sealed class ClavesDeProteccionPostgresTests(PostgresDeLaWebFixture bd) : IAsyncLifetime
{
    private const string ClaveA = "clave-A-de-sesiones-0123456789-abcdefghij";
    private const string ClaveB = "clave-B-de-sesiones-9876543210-jihgfedcba";

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private RepositorioDeClavesDeProteccionPostgres Repositorio(string vigente, string? anterior = null, HostDeSesion host = HostDeSesion.Tienda)
    {
        var unidad = new UnidadDeTrabajoPostgres(bd.Acceso);
        return new RepositorioDeClavesDeProteccionPostgres(unidad, unidad, new AesGcmCifradorDeSecretosDeSesion(ConfiguracionDeSesionesWeb.Crear(host, vigente, anterior)), host);
    }

    private static XElement Clave(string contenido) => new("key", new XAttribute("id", Guid.NewGuid()), new XElement("descriptor", contenido));

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task Las_claves_se_guardan_cifradas_con_el_host_y_se_leen_de_vuelta()
    {
        var repositorio = Repositorio(ClaveA);
        var clave = Clave("material-de-la-clave-de-firma");

        repositorio.StoreElement(clave, "key-1");

        var leidas = repositorio.GetAllElements();
        Assert.Equal(clave.ToString(), Assert.Single(leidas).ToString());
        var fila = (await bd.Consultar<string>("SELECT row(t.*)::text FROM web.claves_de_proteccion t"))!;
        Assert.DoesNotContain("material-de-la-clave", fila, StringComparison.Ordinal);
        Assert.DoesNotContain("descriptor", fila, StringComparison.Ordinal);
        Assert.Equal("tienda", await bd.Consultar<string>("SELECT host FROM web.claves_de_proteccion"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task Hay_un_anillo_por_host_y_una_replica_no_lee_ni_descifra_el_del_otro()
    {
        var tienda = Repositorio(ClaveA, host: HostDeSesion.Tienda);
        var gestion = Repositorio(ClaveB, host: HostDeSesion.Gestion);
        tienda.StoreElement(Clave("de-la-tienda"), "k");
        gestion.StoreElement(Clave("de-la-gestion"), "k");

        Assert.Contains("de-la-tienda", Assert.Single(tienda.GetAllElements()).ToString(), StringComparison.Ordinal);
        Assert.Contains("de-la-gestion", Assert.Single(gestion.GetAllElements()).ToString(), StringComparison.Ordinal);

        // Aunque la fila de la tienda se rotulara como de la gestión, su clave no la abre: se omite y se cuenta.
        await bd.Ejecutar("UPDATE web.claves_de_proteccion SET host = 'gestion' WHERE host = 'tienda'");
        var vistasPorLaGestion = gestion.GetAllElements();
        Assert.Single(vistasPorLaGestion);
        Assert.Equal(1, gestion.FilasIlegibles);
        Assert.Empty(tienda.GetAllElements());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task Con_la_rotacion_en_curso_las_claves_se_recifran_con_la_vigente_y_al_retirar_la_anterior_siguen_abriendose()
    {
        Repositorio(ClaveA).StoreElement(Clave("persistente"), "k");
        var antes = await bd.Consultar<short>("SELECT version_de_clave FROM web.claves_de_proteccion");

        var intermedio = Repositorio(ClaveB, ClaveA);
        Assert.Single(intermedio.GetAllElements());
        var despues = await bd.Consultar<short>("SELECT version_de_clave FROM web.claves_de_proteccion");
        Assert.NotEqual(antes, despues);

        var retirada = Repositorio(ClaveB);
        Assert.Contains("persistente", Assert.Single(retirada.GetAllElements()).ToString(), StringComparison.Ordinal);
        Assert.Equal(0, retirada.FilasIlegibles);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void Si_se_retira_la_anterior_sin_recifrar_las_filas_se_omiten_sin_lanzar_y_se_cuentan()
    {
        Repositorio(ClaveA).StoreElement(Clave("sin-migrar"), "k");
        var retirada = Repositorio(ClaveB);

        var leidas = retirada.GetAllElements();

        Assert.Empty(leidas);
        Assert.Equal(1, retirada.FilasIlegibles);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task Dos_replicas_del_mismo_host_comparten_las_claves_y_la_del_otro_host_no_valida_sus_tokens()
    {
        static IDataProtector Protector(IXmlRepository repositorio)
        {
            var servicios = new ServiceCollection();
            servicios.AddDataProtection().SetApplicationName("babelhub").AddKeyManagementOptions(o => o.XmlRepository = repositorio);
            return servicios.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>().CreateProtector("antiforgery");
        }

        var primeraReplica = Protector(Repositorio(ClaveA, host: HostDeSesion.Tienda));
        var token = primeraReplica.Protect("token-antifalsificacion");

        var segundaReplica = Protector(Repositorio(ClaveA, host: HostDeSesion.Tienda));
        Assert.Equal("token-antifalsificacion", segundaReplica.Unprotect(token));

        var otroHost = Protector(Repositorio(ClaveB, host: HostDeSesion.Gestion));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => otroHost.Unprotect(token));
        Assert.True(await bd.Consultar<long>("SELECT count(*) FROM web.claves_de_proteccion WHERE host = 'tienda'") >= 1);
        var enClaro = await bd.Consultar<string>("SELECT string_agg(encode(xml_cifrado, 'escape'), '') FROM web.claves_de_proteccion");
        Assert.DoesNotContain("masterKey", enClaro!, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task El_rol_del_modulo_no_puede_borrar_claves_del_anillo()
    {
        Repositorio(ClaveA).StoreElement(Clave("k"), "k");

        await using var conexion = await bd.Administracion.OpenConnectionAsync();
        await using var rol = new NpgsqlCommand("SET ROLE rol_web", conexion);
        await rol.ExecuteNonQueryAsync();
        await using var borrar = new NpgsqlCommand("DELETE FROM web.claves_de_proteccion", conexion);
        var error = await Assert.ThrowsAsync<PostgresException>(() => borrar.ExecuteNonQueryAsync());

        Assert.Equal("42501", error.SqlState);
    }
}
