using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using Npgsql;

namespace BabelHub.IntegrationTests.Web;

/// <summary>Los borradores de pantalla en PostgreSQL real (T10): entrega única bajo concurrencia, vigencia desde el cierre de la sesión y un borrador por plantilla.</summary>
[Collection(ColeccionPostgresDeLaWeb.Nombre)]
public sealed class BorradoresPostgresTests(PostgresDeLaWebFixture bd) : IAsyncLifetime
{
    private const string Plantilla = "/reclamaciones/nueva";

    private static readonly IReadOnlyDictionary<string, string> Campos = new Dictionary<string, string> { ["detalle"] = "Mi reclamo con ñ y tildes: acción", ["monto"] = "12,50" };

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Una sesión que venció por inactividad, para que su borrador pueda conservarse.</summary>
    private static async Task<string> SesionVencida(EntornoDeSesiones e)
    {
        var cookie = await e.Abrir();
        e.Reloj.Ahora = EntornoDeSesiones.Inicio.AddMinutes(31);
        await e.Web.Cargar(cookie, HostWeb.Tienda, OrigenDeLaAccion.AccionDelUsuario);
        return cookie;
    }

    private static ConservarBorrador Entrada(string cookie, string plantilla = Plantilla) => new(cookie, plantilla, Campos, (_, _) => Task.FromResult(true));

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Al_vencer_la_sesion_lo_escrito_se_guarda_cifrado_y_vence_30_minutos_despues_del_cierre()
    {
        var e = new EntornoDeSesiones(bd);
        var cookie = await SesionVencida(e);
        e.Reloj.Ahora = EntornoDeSesiones.Inicio.AddMinutes(45);

        Assert.True(await e.BorradoresDePantalla.Conservar(Entrada(cookie)));

        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM web.borradores"));
        Assert.Equal(EntornoDeSesiones.Inicio.AddMinutes(61).UtcDateTime, await bd.Consultar<DateTime>("SELECT vence_en AT TIME ZONE 'UTC' FROM web.borradores"));
        Assert.True(await bd.Consultar<int>("SELECT octet_length(cuerpo_cifrado) FROM web.borradores") > 0);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Lo_escrito_se_recupera_con_sus_acentos_y_se_borra_al_entregarlo()
    {
        var e = new EntornoDeSesiones(bd);
        Assert.True(await e.BorradoresDePantalla.Conservar(Entrada(await SesionVencida(e))));

        var recuperado = await e.BorradoresDePantalla.Recuperar(VerificadorPorPrefijo.Usuario, Plantilla);

        Assert.Equal("Mi reclamo con ñ y tildes: acción", recuperado!["detalle"]);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM web.borradores"));
        Assert.Null(await e.BorradoresDePantalla.Recuperar(VerificadorPorPrefijo.Usuario, Plantilla));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Diez_entregas_simultaneas_del_mismo_borrador_entregan_una_sola_vez()
    {
        var e = new EntornoDeSesiones(bd);
        Assert.True(await e.BorradoresDePantalla.Conservar(Entrada(await SesionVencida(e))));

        var entregas = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => e.BorradoresDePantalla.Recuperar(VerificadorPorPrefijo.Usuario, Plantilla))));

        Assert.Single(entregas, entrega => entrega is not null);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM web.borradores"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Otra_persona_no_lo_recupera_ni_lo_gasta()
    {
        var e = new EntornoDeSesiones(bd);
        Assert.True(await e.BorradoresDePantalla.Conservar(Entrada(await SesionVencida(e))));

        Assert.Null(await e.BorradoresDePantalla.Recuperar(Guid.NewGuid(), Plantilla));

        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM web.borradores"));
        Assert.NotNull(await e.BorradoresDePantalla.Recuperar(VerificadorPorPrefijo.Usuario, Plantilla));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Un_borrador_vencido_no_se_entrega_y_se_borra()
    {
        var e = new EntornoDeSesiones(bd);
        Assert.True(await e.BorradoresDePantalla.Conservar(Entrada(await SesionVencida(e))));
        e.Reloj.Ahora = EntornoDeSesiones.Inicio.AddMinutes(62);

        Assert.Null(await e.BorradoresDePantalla.Recuperar(VerificadorPorPrefijo.Usuario, Plantilla));

        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM web.borradores"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Tras_un_cierre_voluntario_no_se_conserva_nada()
    {
        var e = new EntornoDeSesiones(bd);
        var cookie = await e.Abrir();
        await e.Web.Cerrar(cookie);

        Assert.False(await e.BorradoresDePantalla.Conservar(Entrada(cookie)));

        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM web.borradores"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Hay_un_borrador_por_usuario_y_plantilla_y_el_nuevo_reemplaza_al_anterior()
    {
        var e = new EntornoDeSesiones(bd);
        var cookie = await SesionVencida(e);

        Assert.True(await e.BorradoresDePantalla.Conservar(Entrada(cookie)));
        Assert.True(await e.BorradoresDePantalla.Conservar(new ConservarBorrador(cookie, Plantilla, new Dictionary<string, string> { ["detalle"] = "Corregido" }, (_, _) => Task.FromResult(true))));
        Assert.True(await e.BorradoresDePantalla.Conservar(new ConservarBorrador(cookie, "/cuenta/datos", new Dictionary<string, string> { ["nombre"] = "Ana" }, (_, _) => Task.FromResult(true))));

        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM web.borradores"));
        Assert.Equal("Corregido", (await e.BorradoresDePantalla.Recuperar(VerificadorPorPrefijo.Usuario, Plantilla))!["detalle"]);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task La_base_exige_la_forma_de_la_plantilla_y_la_vigencia()
    {
        var id = Guid.NewGuid();

        var plantilla = await Assert.ThrowsAsync<PostgresException>(() =>
            bd.Ejecutar($"INSERT INTO web.borradores VALUES ('{id}', '{VerificadorPorPrefijo.Usuario}', 'https://ajeno/?q=1', '\\x00', 1, now(), now() + interval '30 minutes')"));
        var vigencia = await Assert.ThrowsAsync<PostgresException>(() =>
            bd.Ejecutar($"INSERT INTO web.borradores VALUES ('{id}', '{VerificadorPorPrefijo.Usuario}', '/cuenta/datos', '\\x00', 1, now(), now())"));

        Assert.Equal("23514", plantilla.SqlState);
        Assert.Equal("23514", vigencia.SqlState);
    }
}
