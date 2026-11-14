using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Infrastructure.Web;

namespace BabelHub.IntegrationTests.Web;

/// <summary>La lista de sucursales con su última lista conocida (D16, T26): se reutiliza un rato, resiste una caída de la fuente y no demora cada página.</summary>
public sealed class ListaDeSucursalesTests
{
    private sealed class RelojManual(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _ahora = inicio;

        public override DateTimeOffset GetUtcNow() => _ahora;

        public void Avanzar(TimeSpan tiempo) => _ahora += tiempo;
    }

    private sealed class FuenteDePrueba : IFuenteDeSucursales
    {
        public int Llamadas { get; private set; }

        public bool Cae { get; set; }

        public IReadOnlyList<SucursalDeLaLista> Lista { get; set; } = [new("AYA", "Ayacucho")];

        public Task<IReadOnlyList<SucursalDeLaLista>> Listar(CancellationToken cancelacion = default)
        {
            Llamadas++;
            return Cae ? throw new HttpRequestException("caído") : Task.FromResult(Lista);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-12")]
    public async Task La_lista_se_reutiliza_dentro_de_su_vigencia_y_se_renueva_despues()
    {
        var reloj = new RelojManual(DateTimeOffset.UnixEpoch);
        var fuente = new FuenteDePrueba();
        var lista = new ListaDeSucursalesConUltimaConocida(fuente, reloj);

        var primera = await lista.Obtener();
        reloj.Avanzar(TimeSpan.FromMinutes(4));
        await lista.Obtener();
        reloj.Avanzar(TimeSpan.FromMinutes(2));
        await lista.Obtener();

        Assert.True(primera!.Actualizada);
        Assert.Equal(2, fuente.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-12")]
    public async Task Si_la_fuente_cae_se_usa_la_ultima_lista_conocida_marcada_como_no_actualizada()
    {
        var reloj = new RelojManual(DateTimeOffset.UnixEpoch);
        var fuente = new FuenteDePrueba();
        var lista = new ListaDeSucursalesConUltimaConocida(fuente, reloj);
        await lista.Obtener();
        reloj.Avanzar(TimeSpan.FromMinutes(10));
        fuente.Cae = true;

        var resultado = await lista.Obtener();

        Assert.NotNull(resultado);
        Assert.False(resultado.Actualizada);
        Assert.Equal("AYA", Assert.Single(resultado.Sucursales).Codigo);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-12")]
    public async Task Sin_ninguna_lista_conocida_y_con_la_fuente_caida_no_hay_lista()
    {
        var fuente = new FuenteDePrueba { Cae = true };
        var lista = new ListaDeSucursalesConUltimaConocida(fuente, new RelojManual(DateTimeOffset.UnixEpoch));

        Assert.Null(await lista.Obtener());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-21")]
    public async Task Tras_un_fallo_no_se_vuelve_a_llamar_a_la_fuente_antes_de_la_espera()
    {
        var reloj = new RelojManual(DateTimeOffset.UnixEpoch);
        var fuente = new FuenteDePrueba { Cae = true };
        var lista = new ListaDeSucursalesConUltimaConocida(fuente, reloj);

        await lista.Obtener();
        reloj.Avanzar(TimeSpan.FromSeconds(10));
        await lista.Obtener();
        await lista.Obtener();
        reloj.Avanzar(ListaDeSucursalesConUltimaConocida.EsperaTrasUnFallo);
        await lista.Obtener();

        Assert.Equal(2, fuente.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-12")]
    public async Task La_fuente_provisional_siempre_falla_y_no_se_inventa_ninguna_sucursal()
    {
        var lista = new ListaDeSucursalesConUltimaConocida(new FuenteDeSucursalesNoDisponible());

        Assert.Null(await lista.Obtener());
    }
}
