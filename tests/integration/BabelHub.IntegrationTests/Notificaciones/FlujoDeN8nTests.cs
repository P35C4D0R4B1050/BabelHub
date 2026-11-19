using System.Text.Json;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Domain.Notificaciones;
using BabelHub.IntegrationTests.Imagenes;

namespace BabelHub.IntegrationTests.Notificaciones;

/// <summary>
/// La verificación de CI del flujo de n8n versionado en <c>infra/n8n/</c> (transversal-notificaciones T17, T18; AC-NOT-06, AC-NOT-09, AC-NOT-11; NOT-07, NOT-10, NOT-12): cada plantilla usa solo los campos de su lista y del catálogo del
/// Dominio, no lleva enlaces ni datos que no deben salir, el flujo no incrusta ningún secreto y verifica la firma con el mismo esquema que el servicio; el Compose deja n8n en la red interna, purga su historial y deja a Evolution sin
/// guardar mensajes ni chats.
/// </summary>
public sealed partial class FlujoDeN8nTests
{
    private static readonly string[] Derivados = ["saludo", "enSucursal"];

    private sealed record PlantillaDelFlujo(string Clave, int Version, IReadOnlyList<string> Campos, string Texto);

    private static string Flujo() => File.ReadAllText(Path.Combine(ComposeDeVms.Raiz(), "infra", "n8n", "flujo-de-avisos.json"));

    private static string CodigoDelNodo(string nombre)
    {
        using var documento = JsonDocument.Parse(Flujo());
        var nodo = documento.RootElement.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("name").GetString() == nombre);
        return nodo.GetProperty("parameters").GetProperty("jsCode").GetString()!;
    }

    private static List<PlantillaDelFlujo> Plantillas()
    {
        var codigo = CodigoDelNodo("Verificar");
        var bloque = codigo[codigo.IndexOf("const PLANTILLAS = {", StringComparison.Ordinal)..codigo.IndexOf("// FIN-PLANTILLAS", StringComparison.Ordinal)];
        return [.. FilaDePlantilla().Matches(bloque).Select(m => new PlantillaDelFlujo(
            m.Groups["clave"].Value,
            int.Parse(m.Groups["version"].Value, System.Globalization.CultureInfo.InvariantCulture),
            [.. m.Groups["campos"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(c => c.Trim('\''))],
            m.Groups["texto"].Value))];
    }

    [GeneratedRegex(@"'(?<clave>[a-z0-9-]+)': \{ version: (?<version>\d+), campos: \[(?<campos>[^\]]*)\], texto: '(?<texto>[^']*)' \}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex FilaDePlantilla();

    [GeneratedRegex(@"\{(?<nombre>[A-Za-z]+)\}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Marcador();

    [Fact]
    [Trait("Criterio", "AC-NOT-06")]
    public void ElFlujo_TieneUnaPlantillaPorCadaUnaDelCatalogo_ConLosMismosCamposYLaMismaVersion()
    {
        var delFlujo = Plantillas();

        Assert.Equal(CatalogoDePlantillas.Todas.Select(p => p.Clave).Order(StringComparer.Ordinal), delFlujo.Select(p => p.Clave).Order(StringComparer.Ordinal));
        foreach (var dominio in CatalogoDePlantillas.Todas)
        {
            var plantilla = delFlujo.Single(p => p.Clave == dominio.Clave);
            Assert.Equal(dominio.Version, plantilla.Version);
            Assert.Equal(dominio.Campos.Select(c => c.Nombre).Order(StringComparer.Ordinal), plantilla.Campos.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-06")]
    public void CadaTexto_UsaSoloMarcadoresDeSusCamposOLosDerivados_YNingunDatoQueNoDebeSalir()
    {
        foreach (var plantilla in Plantillas())
        {
            var marcadores = Marcador().Matches(plantilla.Texto).Select(m => m.Groups["nombre"].Value).ToList();

            Assert.All(marcadores, m => Assert.True(plantilla.Campos.Contains(m) || Derivados.Contains(m), $"«{plantilla.Clave}» usa {{{m}}}, que no está en su lista de campos."));
            if (marcadores.Contains("saludo"))
            {
                Assert.Contains("nombre", plantilla.Campos);
            }

            if (marcadores.Contains("enSucursal"))
            {
                Assert.Contains("sucursal", plantilla.Campos);
            }

            // Sin enlaces, códigos ni cifras sueltas: el mensaje de un libro digital nunca lleva el enlace de descarga y ningún texto repite un dato de pago.
            Assert.DoesNotMatch(@"(?i)https?:|www\.|://|wa\.me|\.com|\.pe\b", plantilla.Texto);
            Assert.DoesNotMatch(@"\d{3,}", plantilla.Texto);
            Assert.DoesNotContain("tarjeta", plantilla.Texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("contraseña", plantilla.Texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("código", plantilla.Texto, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-09")]
    public void ElTextoViveEnElFlujo_AlEditarloCambiaElMensaje_ElCuerpoDelServicioNoLlevaTexto()
    {
        var plantilla = Plantillas().Single(p => p.Clave == "pedido-entregado");
        var campos = new Dictionary<string, string> { ["numeroDePedido"] = "BH-000123", ["nombre"] = "María" };

        var antes = Armar(plantilla.Texto, campos);
        var despues = Armar(plantilla.Texto.Replace("fue entregado", "ya llegó", StringComparison.Ordinal), campos);

        Assert.Equal("Hola María, tu pedido BH-000123 fue entregado. Gracias por leer con nosotros. — BabelHub", antes);
        Assert.NotEqual(antes, despues);
        Assert.Contains("ya llegó", despues, StringComparison.Ordinal);

        // El servicio envía la clave de la plantilla y los campos (probado en AdaptadoresDeEnvioTests); el texto no está en el código del servicio.
        var fuentes = string.Concat(Directory.EnumerateFiles(Path.Combine(ComposeDeVms.Raiz(), "src", "BabelHub.Ecommerce", "BabelHub.Ecommerce.Infrastructure", "Notificaciones"), "*.cs").Select(File.ReadAllText));
        Assert.DoesNotContain("fue entregado", fuentes, StringComparison.Ordinal);
    }

    /// <summary>La misma sustitución que hace el nodo «Armar mensaje»: <c>{campo}</c> por su valor y los derivados <c>saludo</c> y <c>enSucursal</c>.</summary>
    private static string Armar(string texto, IReadOnlyDictionary<string, string> campos)
    {
        var derivados = new Dictionary<string, string>
        {
            ["saludo"] = campos.TryGetValue("nombre", out var nombre) && nombre.Length > 0 ? "Hola " + nombre : "Hola",
            ["enSucursal"] = campos.TryGetValue("sucursal", out var sucursal) && sucursal.Length > 0 ? " en la sucursal " + sucursal : string.Empty,
        };
        return Marcador().Replace(texto, m => derivados.TryGetValue(m.Groups["nombre"].Value, out var derivado) ? derivado : campos.GetValueOrDefault(m.Groups["nombre"].Value, string.Empty));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-11")]
    public void ElFlujo_VerificaLaFirmaConElMismoEsquemaQueElServicio_AntesDeActuar_YFirmaSusEstadosConLaOtraClave()
    {
        var verificar = CodigoDelNodo("Verificar");
        var firmar = CodigoDelNodo("Firmar estado");

        // El mismo texto firmado que N8nEnvioDeWhatsApp.Firmar: marca.método.ruta.cuerpo con HMAC-SHA256 y comparación en tiempo constante.
        Assert.Contains("marca + '.POST.' + RUTA + '.' + crudo", verificar, StringComparison.Ordinal);
        Assert.Contains("createHmac('sha256', clave)", verificar, StringComparison.Ordinal);
        Assert.Contains("timingSafeEqual", verificar, StringComparison.Ordinal);
        Assert.Contains("> 300", verificar, StringComparison.Ordinal);
        Assert.Contains("$env.BABELHUB_FIRMA_DE_AVISOS", verificar, StringComparison.Ordinal);

        // Los estados salen firmados con la clave del otro sentido, con el formato que verifica Plataforma (marca.cuerpo, sha256=hex).
        Assert.Contains("$env.BABELHUB_FIRMA_DE_ESTADOS", firmar, StringComparison.Ordinal);
        Assert.Contains("'sha256=' + crypto.createHmac('sha256'", firmar, StringComparison.Ordinal);
        Assert.Contains("update(marca + '.' + cuerpo)", firmar, StringComparison.Ordinal);
        Assert.DoesNotContain("BABELHUB_FIRMA_DE_AVISOS", firmar, StringComparison.Ordinal);
        Assert.DoesNotContain("BABELHUB_FIRMA_DE_ESTADOS", verificar, StringComparison.Ordinal);

        // La verificación va antes de armar o enviar nada: el nodo que sigue al webhook es el verificador.
        using var documento = JsonDocument.Parse(Flujo());
        var primero = documento.RootElement.GetProperty("connections").GetProperty("Webhook").GetProperty("main")[0][0].GetProperty("node").GetString();
        Assert.Equal("Verificar", primero);
        var ruta = documento.RootElement.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("name").GetString() == "Webhook").GetProperty("parameters").GetProperty("path").GetString();
        Assert.Contains($"const RUTA = '/webhook/{ruta}';", verificar, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-12")]
    public void ElFlujo_ElPrimerSegundoYElAvisoYaEntregado_SeResuelvenSinReenviar_YSoloSeRecuerdaElIdentificador()
    {
        var verificar = CodigoDelNodo("Verificar");
        var resultado = CodigoDelNodo("Resultado del envio");

        // Un avisoId ya entregado responde duplicado y no vuelve a salir; se recuerda solo si Evolution entregó, 48 horas, sin el número.
        Assert.Contains("48 * 3600 * 1000", verificar, StringComparison.Ordinal);
        Assert.Contains("datos.entregados[cuerpo.avisoId]", verificar, StringComparison.Ordinal);
        Assert.Contains("if (entregado)", resultado, StringComparison.Ordinal);
        Assert.DoesNotContain("numero", resultado.Replace("el número", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-06")]
    public void ElFlujo_NoIncrustaNingunSecreto_SoloLeeVariablesDeEntornoDeLaLista_YLaCredencialDeEvolutionVaPorNombre()
    {
        var texto = Flujo();
        var sinIdentificadores = Regex.Replace(texto, "\"(id|webhookId)\": \"[^\"]*\"", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));

        // Ninguna cadena larga que parezca una clave, ni credenciales, llaves o números de teléfono.
        Assert.DoesNotMatch(@"""[A-Za-z0-9+/=_-]{32,}""|'[A-Za-z0-9+/=_-]{32,}'", sinIdentificadores);
        Assert.DoesNotMatch(@"(?i)bearer\s+\S{8,}|password|contrase.a\s*[:=]|api[_-]?key""\s*:\s*""[^""]{8,}", sinIdentificadores);
        Assert.DoesNotMatch(@"\+?51\d{9}|\b9\d{8}\b", sinIdentificadores);
        using var documento = JsonDocument.Parse(texto);
        var credenciales = documento.RootElement.GetProperty("nodes").EnumerateArray().Where(n => n.TryGetProperty("credentials", out _)).Select(n => n.GetProperty("credentials")).ToList();
        Assert.NotEmpty(credenciales);
        Assert.All(credenciales, c => Assert.All(c.EnumerateObject(), tipo => Assert.Equal(["id", "name"], tipo.Value.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal))));

        var usadas = Regex.Matches(texto, @"\$env\.([A-Z_]+)", RegexOptions.None, TimeSpan.FromSeconds(2)).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var declaradas = File.ReadAllLines(Path.Combine(ComposeDeVms.Raiz(), "infra", "secretos-requeridos.txt"))
            .Where(l => l.StartsWith("n8n.env", StringComparison.Ordinal))
            .Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[1])
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(usadas);
        Assert.All(usadas, u => Assert.Contains(u, declaradas));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-06")]
    public void ElCompose_DejaN8nEnLaRedInterna_PurgaSuHistorial_YEvolutionNoGuardaMensajesNiChats()
    {
        var compose = File.ReadAllText(Path.Combine(ComposeDeVms.Raiz(), "infra", "docker-compose.ventas.yml"));
        var n8n = compose[compose.IndexOf("\n  n8n:", StringComparison.Ordinal)..compose.IndexOf("\n  evolution-api:", StringComparison.Ordinal)];
        var evolution = compose[compose.IndexOf("\n  evolution-api:", StringComparison.Ordinal)..compose.IndexOf("\n  cloudflared:", StringComparison.Ordinal)];

        Assert.Contains("\"127.0.0.1:5678:5678\"", n8n, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"-\s+""?(\$\{IP_PRIVADA[^}]*\}|0\.0\.0\.0):5678", n8n);
        Assert.Contains("EXECUTIONS_DATA_PRUNE: \"true\"", n8n, StringComparison.Ordinal);
        Assert.Contains("EXECUTIONS_DATA_MAX_AGE: \"168\"", n8n, StringComparison.Ordinal);
        Assert.Contains("logging: *registro", n8n, StringComparison.Ordinal);
        foreach (var variable in new[] { "DATABASE_SAVE_DATA_NEW_MESSAGE", "DATABASE_SAVE_MESSAGE_UPDATE", "DATABASE_SAVE_DATA_CONTACTS", "DATABASE_SAVE_DATA_CHATS", "DATABASE_SAVE_DATA_HISTORIC" })
        {
            Assert.Contains($"{variable}: \"false\"", evolution, StringComparison.Ordinal);
        }

        Assert.Contains("logging: *registro", evolution, StringComparison.Ordinal);
    }
}
