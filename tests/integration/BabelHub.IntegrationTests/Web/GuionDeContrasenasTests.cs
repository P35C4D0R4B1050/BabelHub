using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BabelHub.IntegrationTests.Notificaciones;

namespace BabelHub.IntegrationTests.Web;

/// <summary>Una prueba del guion de contraseñas: se ejecuta con Node.js; sin Node se omite, <b>salvo en la integración continua</b> (variable <c>CI</c>), donde su ausencia la hace fallar (web-acceso, plan §8).</summary>
public sealed class FactDelGuionAttribute : FactAttribute
{
    public FactDelGuionAttribute()
    {
        if (!NodeDisponible.Hay && Environment.GetEnvironmentVariable("CI") is null)
        {
            Skip = "Node.js no está instalado: se omite la ejecución del guion de contraseñas.";
        }
    }
}

/// <summary>
/// El guion real <c>wwwroot/js/contrasenas.js</c> (web-acceso ACC-02 a ACC-07 y ACC-09), ejecutado con Node.js sobre un doble mínimo del documento (<c>Web/js/ejecutar-contrasenas.cjs</c>) armado con los campos que de verdad
/// escribe el servidor. La tabla de ejemplos del nivel se lee de la propia spec.
/// </summary>
public sealed partial class GuionDeContrasenasTests : IAsyncLifetime
{
    private static readonly string[] Niveles = ["Débil", "Aceptable", "Buena", "Fuerte"];

    private AccesoApiHost _host = null!;

    public async Task InitializeAsync() => _host = await AccesoApiHost.Crear();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    // ---------- Piezas ----------

    private static string RutaDelGuion => RaizDelRepositorio.DeLaApi("wwwroot/js/contrasenas.js");

    private static JsonElement Ejecutar(object escenario)
    {
        var informacion = new ProcessStartInfo("node") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        informacion.ArgumentList.Add(Path.Combine(RaizDelRepositorio.Raiz, "tests", "integration", "BabelHub.IntegrationTests", "Web", "js", "ejecutar-contrasenas.cjs"));
        using var proceso = Process.Start(informacion)!;
        proceso.StandardInput.Write(JsonSerializer.Serialize(escenario));
        proceso.StandardInput.Close();
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var errores = proceso.StandardError.ReadToEndAsync();
        Assert.True(proceso.WaitForExit(30000), "El guion no terminó a tiempo.");
        Assert.True(proceso.ExitCode == 0, errores.Result);
        return JsonDocument.Parse(salida.Result).RootElement.Clone();
    }

    [GeneratedRegex("<input type=\"password\"(?<atributos>[^>]*?)/?>", RegexOptions.None, 2000)]
    private static partial Regex CampoDeContrasena();

    [GeneratedRegex("(?<nombre>[\\w:-]+)=\"(?<valor>[^\"]*)\"", RegexOptions.None, 2000)]
    private static partial Regex Atributo();

    /// <summary>Los campos de contraseña de una respuesta, con sus atributos tal como los escribe el servidor.</summary>
    internal static List<Dictionary<string, string>> CamposDe(string html) =>
        [.. CampoDeContrasena().Matches(html).Select(m =>
        {
            var atributos = Atributo().Matches(m.Groups["atributos"].Value).ToDictionary(a => a.Groups["nombre"].Value, a => WebUtility.HtmlDecode(a.Groups["valor"].Value), StringComparer.Ordinal);
            atributos["type"] = "password";
            return atributos;
        })];

    /// <summary>El formulario de confirmar la recuperación: el primer campo de la contraseña nueva y su repetición.</summary>
    private async Task<object[]> FormularioDeRecuperacion()
    {
        using var respuesta = await _host.Enviar("/recuperacion", [new("correo", "nadie@correo.example")]);
        var campos = CamposDe(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(["contrasenaNueva", "confirmacion"], campos.Select(c => c["name"]));
        return [.. campos.Select(c => new
        {
            atributos = c,
            plantillas = c.ContainsKey("data-bh-repite") ? new Dictionary<string, string> { ["exito"] = "<svg>exito</svg>", ["error"] = "<svg>error</svg>" } : [],
        })];
    }

    private static object Escribir(string campo, string valor, string evento = "input") => new { tipo = "escribir", campo, valor, evento };

    private static object Paso(string tipo, string campo = "contrasenaNueva") => new { tipo, campo };

    private static object Avanzar(int ms) => new { tipo = "avanzar", ms };

    private async Task<(JsonElement[] Estados, JsonElement Resultado)> Recorrer(params object[] pasos)
    {
        var resultado = Ejecutar(new { guion = RutaDelGuion, formularios = new[] { await FormularioDeRecuperacion() }, pasos });
        return ([.. resultado.GetProperty("estados").EnumerateArray()], resultado);
    }

    private static JsonElement Campo(JsonElement estado, string nombre = "contrasenaNueva") => estado.GetProperty("campos").GetProperty(nombre);

    private static string Texto(JsonElement elemento, params string[] ruta) => ruta.Aggregate(elemento, (e, p) => e.GetProperty(p)).GetString() ?? string.Empty;

    // ---------- El nivel ----------

    [FactDelGuion]
    [Trait("Criterio", "AC-ACC-04")]
    public void LaTablaDeEjemplosDeLaSpec_DaElNivelQueLaSpecDice()
    {
        var spec = File.ReadAllText(Path.Combine(RaizDelRepositorio.Raiz, "specs", "web-acceso", "spec.md"));
        var filas = Regex.Matches(spec, @"(?m)^\s*\| `(?<contrasena>[^`]+)` \| (?<nivel>Débil|Aceptable|Buena|Fuerte) \|", RegexOptions.None, TimeSpan.FromSeconds(2));
        Assert.True(filas.Count >= 20, "La tabla de ejemplos de la spec no se leyó entera.");
        var ejemplos = filas.Select(f => (Contrasena: f.Groups["contrasena"].Value, Nivel: f.Groups["nivel"].Value)).ToList();

        var resultado = Ejecutar(new { guion = RutaDelGuion, minimo = 10, evaluar = ejemplos.Select(e => e.Contrasena) });

        var obtenidos = resultado.GetProperty("niveles").EnumerateArray().Select(n => n.GetString()).ToList();
        Assert.Equal(ejemplos.Select(e => $"{e.Contrasena} => {e.Nivel}"), ejemplos.Zip(obtenidos, (e, n) => $"{e.Contrasena} => {n}"));
    }

    [FactDelGuion]
    [Trait("Criterio", "AC-ACC-04")]
    public void LaNormalizacionLasSecuenciasDeDigitosElTecladoAlRevesYElCampoVacio()
    {
        // Complementan la tabla de la spec (hallazgos de su revisión): la palabra en ancho completo solo es Débil si se normaliza; dígitos y teclado en ambos sentidos.
        string[] ejemplos = ["ｐａｓｓｗｏｒｄ１２", "123456789012", "987654321098", "poiuytrewq99", "mnbvcxzlkjhg", string.Empty];

        var resultado = Ejecutar(new { guion = RutaDelGuion, minimo = 10, evaluar = ejemplos });

        Assert.Equal(["Débil", "Débil", "Débil", "Débil", "Débil", null], resultado.GetProperty("niveles").EnumerateArray().Select(n => n.GetString()));
    }

    // ---------- La barra ----------

    [FactDelGuion]
    [Trait("Criterio", "AC-ACC-03")]
    public async Task LaBarra_SigueALoQueSeEscribeSeBorraSePegaORellenaElNavegador_YConElCampoVacioNoMuestraNivel()
    {
        var larga = string.Concat(Enumerable.Repeat("zqkv wxjp ", 15));
        var (estados, _) = await Recorrer(
            Escribir("contrasenaNueva", "abc"),
            Escribir("contrasenaNueva", "qzmvkrtpwj"),
            Escribir("contrasenaNueva", "mi gato come pescado fresco"),
            Escribir("contrasenaNueva", "Mi gato come pescado fresco", "change"),
            Escribir("contrasenaNueva", larga),
            Escribir("contrasenaNueva", string.Empty));

        var medidores = estados.Select(e => Campo(e).GetProperty("medidor")).ToList();
        Assert.True(medidores[0].GetProperty("oculto").GetBoolean());
        foreach (var (indice, nivel) in new[] { (1, 0), (2, 1), (3, 2), (4, 3) })
        {
            Assert.False(medidores[indice].GetProperty("oculto").GetBoolean());
            Assert.Equal((nivel + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Texto(medidores[indice], "nivel"));
            Assert.Contains("Nivel de seguridad: " + Niveles[nivel], Texto(medidores[indice], "texto"), StringComparison.Ordinal);
            Assert.Contains("solo una guía", Texto(medidores[indice], "texto"), StringComparison.Ordinal);
        }

        Assert.False(medidores[5].GetProperty("oculto").GetBoolean());
        Assert.True(medidores[6].GetProperty("oculto").GetBoolean());
        // El campo de repetición no tiene barra: solo la primera contraseña nueva.
        Assert.Equal(JsonValueKind.Null, Campo(estados[0], "confirmacion").GetProperty("medidor").ValueKind);
    }

    [FactDelGuion]
    [Trait("Criterio", "AC-ACC-02")]
    public async Task EnviarUnaContrasenaDebilCortaODistinta_NuncaSeDetiene()
    {
        var (estados, _) = await Recorrer(
            Escribir("contrasenaNueva", "aaaaaaaaaaaa"),
            Paso("enviar"),
            Escribir("contrasenaNueva", "corta"),
            Escribir("confirmacion", "otra cosa"),
            Paso("enviar"));

        Assert.Equal([false], estados[2].GetProperty("envios").EnumerateArray().Select(e => e.GetBoolean()));
        Assert.Equal([false], estados[5].GetProperty("envios").EnumerateArray().Select(e => e.GetBoolean()));
    }

    // ---------- La coincidencia ----------

    [FactDelGuion]
    [Trait("Criterio", "AC-ACC-06")]
    public async Task LaCoincidencia_SeAvisaConTextoEIcono_ComparaElTextoExacto_YSigueLosCambiosDeLosDosCampos()
    {
        var (estados, _) = await Recorrer(
            Escribir("contrasenaNueva", "mi gato come pescado"),
            Escribir("confirmacion", "mi gato"),
            Escribir("confirmacion", "mi gato come pescado"),
            Escribir("contrasenaNueva", "mi gato come pescado "),
            Escribir("confirmacion", string.Empty),
            Paso("enviar"));

        var avisos = estados.Select(e => Campo(e, "confirmacion").GetProperty("aviso")).ToList();
        Assert.True(avisos[0].GetProperty("oculto").GetBoolean());
        Assert.True(avisos[1].GetProperty("oculto").GetBoolean());
        Assert.Equal(("no", "Las contraseñas todavía no coinciden", "<svg>error</svg>"), (Texto(avisos[2], "coincide"), Texto(avisos[2], "texto"), Texto(avisos[2], "icono")));
        Assert.Equal(("si", "Las contraseñas coinciden", "<svg>exito</svg>"), (Texto(avisos[3], "coincide"), Texto(avisos[3], "texto"), Texto(avisos[3], "icono")));
        // Un espacio de más en el primer campo ya no es la misma contraseña: se compara como en el servidor.
        Assert.Equal("no", Texto(avisos[4], "coincide"));
        Assert.True(avisos[5].GetProperty("oculto").GetBoolean());
        Assert.Equal([false], estados[6].GetProperty("envios").EnumerateArray().Select(e => e.GetBoolean()));
    }

    // ---------- Ver u ocultar ----------

    [FactDelGuion]
    [Trait("Criterio", "AC-ACC-07")]
    public async Task ElBoton_MuestraYOcultaSinEnviarNiBorrar_NombraElCampoYSuEstado_YTodoVuelveOcultoAlEnviarOAlVolver()
    {
        var (estados, _) = await Recorrer(
            Escribir("contrasenaNueva", "mi gato come pescado"),
            Paso("pulsar"),
            Paso("pulsar"),
            Paso("pulsar"),
            Paso("pulsar", "confirmacion"),
            Paso("enviar"),
            Paso("pulsar"),
            Paso("volver"));

        var inicial = Campo(estados[0]);
        Assert.Equal((1, "password"), (inicial.GetProperty("botones").GetInt32(), Texto(inicial, "tipo")));
        Assert.Equal(("button", "Mostrar", "Mostrar la contraseña nueva"), (Texto(inicial, "boton", "tipo"), Texto(inicial, "boton", "visible"), Texto(inicial, "boton", "nombre")));
        Assert.False(inicial.GetProperty("boton").GetProperty("conName").GetBoolean());
        Assert.Equal("Mostrar la repetición de la contraseña nueva", Texto(Campo(estados[0], "confirmacion"), "boton", "nombre"));

        var visible = Campo(estados[2]);
        Assert.Equal(("text", "mi gato come pescado", "Ocultar", "Ocultar la contraseña nueva, ahora visible"), (Texto(visible, "tipo"), Texto(visible, "valor"), Texto(visible, "boton", "visible"), Texto(visible, "boton", "nombre")));
        Assert.Equal("password", Texto(Campo(estados[3]), "tipo"));
        Assert.Equal(("text", "text"), (Texto(Campo(estados[5]), "tipo"), Texto(Campo(estados[5], "confirmacion"), "tipo")));
        // Al enviar, los dos vuelven a quedar ocultos y el envío no se detiene.
        Assert.Equal(("password", "password"), (Texto(Campo(estados[6]), "tipo"), Texto(Campo(estados[6], "confirmacion"), "tipo")));
        Assert.Equal([false], estados[6].GetProperty("envios").EnumerateArray().Select(e => e.GetBoolean()));
        Assert.Equal("text", Texto(Campo(estados[7]), "tipo"));
        Assert.Equal(("password", "Mostrar la contraseña nueva"), (Texto(Campo(estados[8]), "tipo"), Texto(Campo(estados[8]), "boton", "nombre")));
    }

    [FactDelGuion]
    [Trait("Criterio", "AC-ACC-07")]
    public async Task UnaPantallaQueLlegaPorHtmx_SeMejoraUnaSolaVez()
    {
        var formulario = await FormularioDeRecuperacion();

        var resultado = Ejecutar(new { guion = RutaDelGuion, formularios = Array.Empty<object>(), tardios = formulario, pasos = new[] { new { tipo = "llegaPorHtmx" } } });

        var campos = resultado.GetProperty("estados")[1].GetProperty("campos");
        Assert.Equal((1, 1), (campos.GetProperty("contrasenaNueva").GetProperty("botones").GetInt32(), campos.GetProperty("confirmacion").GetProperty("botones").GetInt32()));
    }

    // ---------- Anuncios y privacidad ----------

    [FactDelGuion]
    [Trait("Criterio", "AC-ACC-08")]
    public async Task ElLectorDePantalla_RecibeSoloLosCambios_DespuesDeUnSegundoSinEscribir_SinInterrumpir()
    {
        var (estados, _) = await Recorrer(
            Escribir("contrasenaNueva", "a"),
            Avanzar(600),
            Escribir("contrasenaNueva", "ab"),
            Avanzar(600),
            Avanzar(500),
            Escribir("contrasenaNueva", "qzmvkrtpwj"),
            Avanzar(1100));

        JsonElement Anuncio(int indice) => Campo(estados[indice]).GetProperty("anuncios").EnumerateArray().Single();
        Assert.All(estados, e => Assert.Equal("polite", Texto(Campo(e).GetProperty("anuncios")[0], "cortesia")));
        Assert.Equal(string.Empty, Texto(Anuncio(2), "texto"));
        Assert.Equal(string.Empty, Texto(Anuncio(4), "texto"));
        Assert.Equal("Nivel de seguridad: Débil", Texto(Anuncio(5), "texto"));
        Assert.Equal("Nivel de seguridad: Débil", Texto(Anuncio(6), "texto"));
        Assert.Equal("Nivel de seguridad: Aceptable", Texto(Anuncio(7), "texto"));
    }

    [FactDelGuion]
    [Trait("Criterio", "AC-ACC-05")]
    public async Task EscribirLaContrasena_NoOriginaSolicitudes_NiLaGuardaNiLaCopiaFueraDeSuCampo()
    {
        var (_, resultado) = await Recorrer(
            Escribir("contrasenaNueva", "una frase que no debe salir"),
            Escribir("confirmacion", "una frase que no debe salir"),
            Paso("pulsar"),
            Paso("pulsar"),
            Avanzar(2000),
            Paso("volver"));

        Assert.Empty(resultado.GetProperty("efectos").EnumerateArray());
        Assert.Empty(resultado.GetProperty("fugas").EnumerateArray());
        // Y el archivo no usa nada que la sacaría del dispositivo, tampoco en una rama que el recorrido no pise.
        var fuente = File.ReadAllText(RutaDelGuion);
        foreach (var prohibido in new[] { "fetch(", "XMLHttpRequest", "sendBeacon", "localStorage", "sessionStorage", "document.cookie", ".cookie", "console.", "history.", "new Image", "WebSocket", "location" })
        {
            Assert.DoesNotContain(prohibido, fuente, StringComparison.Ordinal);
        }
    }
}
