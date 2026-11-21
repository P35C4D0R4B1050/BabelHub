using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// La entrada no válida del índice infinito, sin eco (web-indice-infinito T11; AC-IND-06, IND-07, CTL-35, SEG-10): direcciones de 65 caracteres y de 2 KB, con mayúsculas, tildes, símbolos, un <c>&lt;script&gt;</c>, <c>%0d%0a</c>, <c>%2f</c>, <c>..</c>,
/// <c>?direccion=</c> repetido o de solo espacios, <c>%0a</c> en los extremos y <c>?desde=</c> repetido responden 200 amable o 302 <b>sin repetir el texto enviado</b> en el cuerpo, las cabeceras, los enlaces ni el campo del formulario, y ningún registro
/// de las categorías <c>BabelHub.*</c> lo contiene (el registro de solicitudes de <c>Microsoft.*</c> es el hallazgo del carril A). <c>%00</c>, UTF-8 inválido y una URL mayor que el límite del servidor los rechaza Kestrel antes del pipeline: un host en
/// memoria no los ejerce (se cubren con el host real en el turno de Docker).
/// </summary>
public sealed class IndiceInfinitoSinEcoTests
{
    private const string Centinela = "CentinelaX7q9";

    /// <summary>Un sumidero de registros que guarda cada mensaje y excepción de las categorías propias.</summary>
    private sealed class SumideroDeRegistros : ILoggerProvider
    {
        public ConcurrentQueue<string> Mensajes { get; } = new();

        public ILogger CreateLogger(string categoria) => categoria.StartsWith("BabelHub.", StringComparison.Ordinal) ? new Registro(this) : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class Registro(SumideroDeRegistros sumidero) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState estado)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel nivel) => true;

            public void Log<TState>(LogLevel nivel, EventId evento, TState estado, Exception? excepcion, Func<TState, Exception?, string> formateador) =>
                sumidero.Mensajes.Enqueue(formateador(estado, excepcion) + (excepcion?.ToString() ?? string.Empty));
        }
    }

    public static TheoryData<string> Rutas =>
    [
        "/indice-infinito/" + new string('a', 65),
        "/indice-infinito/" + new string('b', 2048),
        "/indice-infinito/" + Centinela,
        "/indice-infinito/sal%C3%B3n-" + Centinela,
        "/indice-infinito/" + Centinela + "%21%40%23",
        "/indice-infinito/%3Cscript%3E" + Centinela + "%3C%2Fscript%3E",
        "/indice-infinito/" + Centinela + "%0d%0aSet-Cookie:x=1",
        "/indice-infinito/" + Centinela + "%2fabc",
        "/indice-infinito/" + Centinela + "..%2f..",
        "/indice-infinito/.../" + Centinela,
        "/indice-infinito?direccion=" + Centinela,
        "/indice-infinito?direccion=A%26B" + Centinela,
        "/indice-infinito?direccion=" + Centinela + "&direccion=s1-p1-e1-v1-pg1",
        "/indice-infinito?direccion=s1-p1-e1-v1-pg1&direccion=" + Centinela,
        "/indice-infinito?direccion=%0as1-p1-e1-v1-pg1%0a",
        "/indice-infinito?direccion=%09" + Centinela,
        "/indice-infinito/acciones/al-azar?desde=" + Centinela,
        "/indice-infinito/acciones/al-azar?desde=a&desde=" + Centinela,
        "/indice-infinito/acciones/al-azar?desde=%3Cscript%3E" + Centinela + "&desde=b",
        "/indice-infinito/acciones/al-azar?desde=" + Centinela + "%0d%0aX-Inyectada:1",
    ];

    [Theory]
    [MemberData(nameof(Rutas))]
    [Trait("Criterio", "AC-IND-06")]
    public async Task Una_entrada_no_valida_responde_amable_o_redirige_sin_repetir_lo_enviado(string ruta)
    {
        var sumidero = new SumideroDeRegistros();
        await using var host = await AnfitrionDelIndice.Iniciar(s => s.AddLogging(b => b.AddProvider(sumidero)));

        var respuesta = await host.Cliente.GetAsync(ruta);
        var cuerpo = await respuesta.Content.ReadAsStringAsync();

        Assert.True(respuesta.StatusCode is HttpStatusCode.OK or HttpStatusCode.Found, $"Estado inesperado {(int)respuesta.StatusCode} para {ruta[..Math.Min(ruta.Length, 80)]}.");
        var cabeceras = string.Join('\n', respuesta.Headers.Concat(respuesta.Content.Headers).Select(h => h.Key + ": " + string.Join(',', h.Value)));
        foreach (var texto in new[] { cuerpo, cabeceras, string.Join('\n', sumidero.Mensajes) })
        {
            Assert.DoesNotContain(Centinela, texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(new string('a', 65), texto, StringComparison.Ordinal);
            Assert.DoesNotContain(new string('b', 100), texto, StringComparison.Ordinal);
            Assert.DoesNotContain("<script>", texto.Replace("<script src=", "<S", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("X-Inyectada", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("Set-Cookie:x", texto, StringComparison.OrdinalIgnoreCase);
        }

        if (respuesta.StatusCode == HttpStatusCode.Found)
        {
            Assert.Matches(AnfitrionDelIndice.FormaDeLaUbicacion(), respuesta.Headers.Location!.OriginalString);
        }
        else
        {
            // El campo del formulario no reaparece lleno y la dirección mostrada es la de la página al azar.
            Assert.Contains("name=\"direccion\"", cuerpo, StringComparison.Ordinal);
            Assert.Matches("^[a-z0-9-]{1,64}$", AnfitrionDelIndice.DireccionDeLaPagina(cuerpo));
            Assert.Contains("Esa dirección no existe en este laberinto", cuerpo, StringComparison.Ordinal);
        }
    }

    [Theory]
    [Trait("Criterio", "AC-IND-06")]
    [InlineData("/indice-infinito?direccion=s1-p1-e1-v1-pg1&direccion=s2-p1-e1-v1-pg1")]
    [InlineData("/indice-infinito?direccion=%0as1-p1-e1-v1-pg1")]
    public async Task Un_formulario_repetido_o_con_un_salto_de_linea_en_los_extremos_da_la_misma_respuesta_amable(string ruta)
    {
        await using var host = await AnfitrionDelIndice.Iniciar();

        var respuesta = await host.Cliente.GetAsync(ruta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("Esa dirección no existe en este laberinto", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IND-06")]
    public async Task Un_desde_repetido_o_no_valido_se_ignora_y_se_redirige_a_una_al_azar()
    {
        await using var host = await AnfitrionDelIndice.Iniciar();

        foreach (var ruta in new[] { "/indice-infinito/acciones/al-azar?desde=a&desde=b", "/indice-infinito/acciones/al-azar?desde=NoValido", "/indice-infinito/acciones/al-azar" })
        {
            var respuesta = await host.Cliente.GetAsync(ruta);

            Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
            Assert.Matches(AnfitrionDelIndice.FormaDeLaUbicacion(), respuesta.Headers.Location!.OriginalString);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-IND-06")]
    public async Task Una_direccion_de_64_caracteres_es_valida_y_la_de_65_da_el_mensaje_amable()
    {
        await using var host = await AnfitrionDelIndice.Iniciar();

        var valida = await host.Cliente.GetAsync("/indice-infinito/" + new string('a', 64));
        var larga = await host.Cliente.GetAsync("/indice-infinito/" + new string('a', 65));

        Assert.DoesNotContain("Esa dirección no existe", await valida.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("Esa dirección no existe", await larga.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
