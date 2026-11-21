using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.IntegrationTests.Pedidos;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using Texto = BabelHub.Ecommerce.Application.Web.TipoDeTextoLegal;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// Aceptar los textos legales avanza en el host compuesto (IDE-43, IDE-57, WEB-34; AC-IDE-39): la web muestra la versión vigente que informa Identidad desde <c>identidad.versiones_de_textos</c>, la persona la acepta y Identidad la
/// reconoce; antes, el proveedor provisional enviaba una versión que Identidad nunca reconocía («los textos cambiaron» sin fin). Anfitrión real del E-commerce sobre PostgreSQL con todos los esquemas migrados.
/// </summary>
[Collection(ColeccionDelEcommerceCompleto.Nombre)]
public sealed class AceptacionDeTextosConElHostRealTests(PostgresDelEcommerceCompletoFixture bd)
{
    private async Task<Guid> SembrarClienteConTextosPendientes(string terminos, string privacidad, string reembolso, DateOnly desde)
    {
        var id = Guid.NewGuid();
        await bd.Ejecutar(
            $"""
            INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso, vigente_desde) VALUES (1, '{terminos}', '{privacidad}', '{reembolso}', DATE '{desde:yyyy-MM-dd}')
              ON CONFLICT (id) DO UPDATE SET terminos = EXCLUDED.terminos, privacidad = EXCLUDED.privacidad, reembolso = EXCLUDED.reembolso, vigente_desde = EXCLUDED.vigente_desde;
            INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, creada_en, acepto_terminos_version, acepto_terminos_en, acepto_privacidad_version, acepto_privacidad_en)
              VALUES ('{id}', 'Ana Pérez', 'p{id:N}@example.test', true, 'cliente', true, now(), 'anterior', now(), 'anterior', now());
            """);
        return id;
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-39")]
    public async Task La_web_muestra_la_version_de_Identidad_la_persona_acepta_y_puede_continuar()
    {
        await using var anfitrion = bd.Anfitrion(_ => { });
        var cuenta = await SembrarClienteConTextosPendientes("t-v7", "p-v3", "r-v2", new DateOnly(2026, 11, 20));
        var titular = ActorActual.DeUsuario(cuenta, RolesDeUsuario.Cliente, null);
        var actor = new ActorVerificado(cuenta, RolWeb.Cliente, null);

        var antes = await anfitrion.Como(titular, sp => sp.GetRequiredService<IEstadoDeCuenta>().Consultar(cuenta));
        var vigente = await anfitrion.Como(titular, sp => sp.GetRequiredService<IAceptacionDeTextos>().VersionVigente());

        Assert.Equal(EstadoDeAceptacionDeLosTextos.Pendiente, antes!.Textos);
        Assert.False(antes.PuedeComprar);
        Assert.True(vigente.EsExito, vigente.Error?.Clave);
        Assert.Equal("terminos:t-v7|privacidad:p-v3|reembolso:r-v2", vigente.Valor.Identificador);
        Assert.DoesNotContain("provisional", vigente.Valor.Identificador, StringComparison.Ordinal);
        Assert.All(vigente.Valor.Textos, t => Assert.Equal(new DateOnly(2026, 11, 20), t.VigenteDesde));

        var resultado = await anfitrion.Como(titular, sp => sp.GetRequiredService<IAceptacionDeTextos>().Aceptar(actor, vigente.Valor.Identificador));
        var despues = await anfitrion.Como(titular, sp => sp.GetRequiredService<IEstadoDeCuenta>().Consultar(cuenta));

        Assert.Equal(ResultadoDeAceptacion.Aceptada, resultado);
        Assert.Equal(EstadoDeAceptacionDeLosTextos.AlDia, despues!.Textos);
        Assert.True(despues.PuedeComprar);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-39")]
    public async Task Una_version_publicada_mientras_la_persona_leia_se_rechaza_y_al_recargar_la_nueva_se_acepta()
    {
        await using var anfitrion = bd.Anfitrion(_ => { });
        var cuenta = await SembrarClienteConTextosPendientes("t-v7", "p-v3", "r-v2", new DateOnly(2026, 11, 20));
        var titular = ActorActual.DeUsuario(cuenta, RolesDeUsuario.Cliente, null);
        var actor = new ActorVerificado(cuenta, RolWeb.Cliente, null);
        var mostrada = (await anfitrion.Como(titular, sp => sp.GetRequiredService<IAceptacionDeTextos>().VersionVigente())).Valor.Identificador;

        await bd.Ejecutar("UPDATE identidad.versiones_de_textos SET terminos = 't-v8', vigente_desde = DATE '2026-11-21' WHERE id = 1");
        var vieja = await anfitrion.Como(titular, sp => sp.GetRequiredService<IAceptacionDeTextos>().Aceptar(actor, mostrada));
        var nueva = (await anfitrion.Como(titular, sp => sp.GetRequiredService<IAceptacionDeTextos>().VersionVigente())).Valor;
        var aceptada = await anfitrion.Como(titular, sp => sp.GetRequiredService<IAceptacionDeTextos>().Aceptar(actor, nueva.Identificador));

        Assert.Equal(ResultadoDeAceptacion.VersionCambiada, vieja);
        Assert.Equal("terminos:t-v8|privacidad:p-v3|reembolso:r-v2", nueva.Identificador);
        Assert.Equal(ResultadoDeAceptacion.Aceptada, aceptada);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task La_version_de_cada_texto_que_sirve_la_web_es_la_de_la_base_y_la_provisional_no_se_usa()
    {
        await using var anfitrion = bd.Anfitrion(_ => { });
        await SembrarClienteConTextosPendientes("t-v7", "p-v3", "r-v2", new DateOnly(2026, 11, 20));

        var versiones = await anfitrion.Como(ActorActual.DelSistema(), async sp =>
        {
            var textos = sp.GetRequiredService<ITextosLegales>();
            var resultados = new List<TextoLegal>();
            foreach (var tipo in new[] { Texto.Terminos, Texto.Privacidad, Texto.Reembolso })
            {
                resultados.Add((await textos.Obtener(tipo)).Valor);
            }

            return resultados;
        });

        Assert.Equal(["t-v7", "p-v3", "r-v2"], versiones.Select(t => t.Version));
    }
}
