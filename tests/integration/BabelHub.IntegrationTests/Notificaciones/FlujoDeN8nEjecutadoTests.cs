using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BabelHub.Ecommerce.Infrastructure.Notificaciones;
using BabelHub.IntegrationTests.Imagenes;

namespace BabelHub.IntegrationTests.Notificaciones;

/// <summary>
/// Ejecuta de verdad los nodos de código del flujo de n8n (transversal-notificaciones T17; AC-NOT-09, AC-NOT-11, AC-NOT-12; NOT-12) con Node.js y un doble mínimo del entorno de n8n (<c>n8n/ejecutar-flujo.cjs</c>, sin red ni
/// dependencias): la llamada que firma <see cref="N8nEnvioDeWhatsApp"/> la acepta el nodo <c>Verificar</c>; una firma falsa, vieja o de otra clave, un cuerpo mal formado, un campo de más o un valor sospechoso se rechazan; un aviso ya
/// entregado se reconoce como duplicado y uno que falló no; el mensaje se arma con la plantilla; y el estado sale firmado con el formato que verifica el servicio. Se omite si Node.js no está instalado.
/// </summary>
public sealed class FlujoDeN8nEjecutadoTests
{
    private const string ClaveDeAvisos = "clave-del-sentido-saliente-0123456789-abcdef";

    private const string ClaveDeEstados = "clave-del-sentido-entrante-0123456789-abcdef";

    private const string Ruta = "/webhook/babelhub-avisos";

    private static string? Node()
    {
        var candidatos = new[] { "node", @"C:\Program Files\nodejs\node.exe", "/usr/bin/node", "/usr/local/bin/node" };
        foreach (var candidato in candidatos)
        {
            try
            {
                using var proceso = Process.Start(new ProcessStartInfo(candidato, "--version") { RedirectStandardOutput = true, RedirectStandardError = true });
                if (proceso is not null && proceso.WaitForExit(5000) && proceso.ExitCode == 0)
                {
                    return candidato;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Ese candidato no existe: se prueba el siguiente.
            }
        }

        return null;
    }

    private static JsonElement Ejecutar(object escenario)
    {
        var node = Node() ?? throw new InvalidOperationException("Node.js no está instalado.");
        var guion = Path.Combine(ComposeDeVms.Raiz(), "tests", "integration", "BabelHub.IntegrationTests", "Notificaciones", "n8n", "ejecutar-flujo.cjs");
        var informacion = new ProcessStartInfo(node) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        informacion.ArgumentList.Add(Path.GetFullPath(guion));
        informacion.ArgumentList.Add(ComposeDeVms.Raiz());
        using var proceso = Process.Start(informacion)!;
        proceso.StandardInput.Write(JsonSerializer.Serialize(escenario));
        proceso.StandardInput.Close();
        var salida = proceso.StandardOutput.ReadToEnd();
        var error = proceso.StandardError.ReadToEnd();
        Assert.True(proceso.WaitForExit(30000) && proceso.ExitCode == 0, error);
        return JsonDocument.Parse(salida).RootElement.Clone();
    }

    private static string Firma(string clave, long marca, string cuerpo, string ruta = Ruta) =>
        N8nEnvioDeWhatsApp.Firmar(Encoding.UTF8.GetBytes(clave), marca.ToString(System.Globalization.CultureInfo.InvariantCulture), "POST", ruta, cuerpo);

    private static string Cuerpo(Guid? aviso = null, string plantilla = "pedido-listo-para-recojo", object? campos = null, string numero = "+51987654321") =>
        JsonSerializer.Serialize(new { avisoId = (aviso ?? Guid.NewGuid()).ToString("D"), plantilla, version = 1, campos = campos ?? new { numeroDePedido = "BH-000123", nombre = "María" }, numero });

    private static object Verificar(string cuerpo, string? firma = null, long? marca = null, string clave = ClaveDeAvisos, long? ahoraMs = null)
    {
        var ahora = ahoraMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var m = marca ?? ahora / 1000;
        return new
        {
            nodo = "Verificar",
            ahoraMs = ahora,
            cuerpoCrudo = cuerpo,
            cabeceras = new Dictionary<string, string> { ["x-babelhub-marca"] = m.ToString(System.Globalization.CultureInfo.InvariantCulture), ["x-babelhub-firma"] = firma ?? Firma(ClaveDeAvisos, m, cuerpo) },
            env = new Dictionary<string, string> { ["BABELHUB_FIRMA_DE_AVISOS"] = clave },
        };
    }

    private static JsonElement Primero(JsonElement resultado, int paso = 0) => resultado.GetProperty("salida")[paso][0].GetProperty("json");

    [FactSiHayNode]
    [Trait("Criterio", "AC-NOT-11")]
    public void LaLlamadaFirmadaPorElServicio_LaAceptaElNodoVerificar_ConLaPlantillaYElNumero()
    {
        var aviso = Guid.NewGuid();
        var cuerpo = Cuerpo(aviso);

        var json = Primero(Ejecutar(new { pasos = new[] { Verificar(cuerpo) } }));

        Assert.True(json.GetProperty("valido").GetBoolean());
        Assert.False(json.GetProperty("duplicado").GetBoolean());
        Assert.Equal(aviso.ToString("D"), json.GetProperty("avisoId").GetString());
        Assert.Equal("+51987654321", json.GetProperty("numero").GetString());
        Assert.Contains("{numeroDePedido}", json.GetProperty("texto").GetString(), StringComparison.Ordinal);
    }

    [TheorySiHayNode]
    [InlineData("firma-falsa", 401)]
    [InlineData("firma-con-otra-clave", 401)]
    [InlineData("firma-de-otra-ruta", 401)]
    [InlineData("marca-vieja", 401)]
    [InlineData("marca-del-futuro", 401)]
    [InlineData("sin-firma", 401)]
    [InlineData("cuerpo-alterado", 401)]
    [Trait("Criterio", "AC-NOT-11")]
    public void LoNoVerificable_ElNodoVerificarLoRechazaAntesDeActuar(string caso, int estado)
    {
        var ahora = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var cuerpo = Cuerpo();
        var marca = ahora / 1000;
        var paso = caso switch
        {
            "firma-falsa" => Verificar(cuerpo, new string('a', 64)),
            "firma-con-otra-clave" => Verificar(cuerpo, Firma(ClaveDeEstados, marca, cuerpo)),
            "firma-de-otra-ruta" => Verificar(cuerpo, Firma(ClaveDeAvisos, marca, cuerpo, "/webhook/otro")),
            "marca-vieja" => Verificar(cuerpo, Firma(ClaveDeAvisos, marca - 301, cuerpo), marca - 301),
            "marca-del-futuro" => Verificar(cuerpo, Firma(ClaveDeAvisos, marca + 301, cuerpo), marca + 301),
            "sin-firma" => Verificar(cuerpo, string.Empty),
            _ => Verificar(cuerpo + " "),
        };

        // En «cuerpo-alterado» la firma es la del cuerpo original: el verificador recalcula sobre lo que llegó.
        if (caso == "cuerpo-alterado")
        {
            paso = Verificar(cuerpo + " ", Firma(ClaveDeAvisos, marca, cuerpo), marca);
        }

        var json = Primero(Ejecutar(new { pasos = new[] { paso } }));

        Assert.False(json.GetProperty("valido").GetBoolean());
        Assert.Equal(estado, json.GetProperty("status").GetInt32());
    }

    [TheorySiHayNode]
    [InlineData("plantilla-desconocida")]
    [InlineData("campo-de-mas")]
    [InlineData("valor-con-enlace")]
    [InlineData("valor-con-arroba")]
    [InlineData("valor-con-13-digitos")]
    [InlineData("valor-largo")]
    [InlineData("numero-invalido")]
    [InlineData("avisoid-invalido")]
    [InlineData("cuerpo-no-json")]
    [Trait("Criterio", "AC-NOT-06")]
    public void ConFirmaValida_PeroCuerpoNoValido_ElNodoVerificarResponde400_SinEnviar(string caso)
    {
        var cuerpo = caso switch
        {
            "plantilla-desconocida" => Cuerpo(plantilla: "publicidad"),
            "campo-de-mas" => Cuerpo(campos: new { numeroDePedido = "BH-000123", telefono = "987654321" }),
            "valor-con-enlace" => Cuerpo(campos: new { numeroDePedido = "https://malo.example" }),
            "valor-con-arroba" => Cuerpo(campos: new { numeroDePedido = "a@b" }),
            "valor-con-13-digitos" => Cuerpo(campos: new { numeroDePedido = "4111111111111111" }),
            "valor-largo" => Cuerpo(campos: new { numeroDePedido = new string('a', 65) }),
            "numero-invalido" => Cuerpo(numero: "987654321"),
            "avisoid-invalido" => Cuerpo().Replace(Guid.NewGuid().ToString("D"), "x", StringComparison.Ordinal).Replace("\"avisoId\":\"", "\"avisoId\":\"ZZ-", StringComparison.Ordinal),
            _ => "no es json",
        };

        var json = Primero(Ejecutar(new { pasos = new[] { Verificar(cuerpo) } }));

        Assert.False(json.GetProperty("valido").GetBoolean());
        Assert.Equal(400, json.GetProperty("status").GetInt32());
    }

    [FactSiHayNode]
    [Trait("Criterio", "AC-NOT-12")]
    public void UnAvisoYaEntregado_SeRecuerdaSinElNumero_YSuRepeticionEsDuplicada_PeroUnoQueFallo_NoSeRecuerda()
    {
        var entregado = Guid.NewGuid();
        var fallido = Guid.NewGuid();
        var cuerpoEntregado = Cuerpo(entregado);
        var cuerpoFallido = Cuerpo(fallido);
        var verificarEntregado = Verificar(cuerpoEntregado);

        var resultado = Ejecutar(new
        {
            pasos = new object[]
            {
                verificarEntregado,
                new { nodo = "Resultado del envio", anteriores = new Dictionary<string, object> { ["Armar mensaje"] = new { avisoId = entregado.ToString("D"), numero = "+51987654321", texto = "x" } }, json = new { } },
                Verificar(cuerpoEntregado),
                Verificar(cuerpoFallido),
                new { nodo = "Resultado del envio", anteriores = new Dictionary<string, object> { ["Armar mensaje"] = new { avisoId = fallido.ToString("D"), numero = "+51987654321", texto = "x" } }, json = new { error = new { message = "Evolution no respondió" } } },
                Verificar(cuerpoFallido),
            },
        });

        Assert.Equal("entregado", Primero(resultado, 1).GetProperty("estado").GetString());
        Assert.True(Primero(resultado, 2).GetProperty("duplicado").GetBoolean());
        Assert.False(Primero(resultado, 3).GetProperty("duplicado").GetBoolean());
        Assert.Equal("fallido", Primero(resultado, 4).GetProperty("estado").GetString());
        Assert.False(Primero(resultado, 5).GetProperty("duplicado").GetBoolean());

        // Solo se guarda el identificador y la hora: nunca el número ni el texto.
        var guardado = resultado.GetProperty("datos").GetProperty("entregados").GetRawText();
        Assert.Contains(entregado.ToString("D"), guardado, StringComparison.Ordinal);
        Assert.DoesNotContain(fallido.ToString("D"), guardado, StringComparison.Ordinal);
        Assert.DoesNotContain("987654321", resultado.GetProperty("datos").GetRawText(), StringComparison.Ordinal);
    }

    [FactSiHayNode]
    [Trait("Criterio", "AC-NOT-12")]
    public void LosAvisosEntregadosHaceMasDe48Horas_SeOlvidan()
    {
        var viejo = Guid.NewGuid();
        var ahora = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var hace49Horas = ahora - (49L * 3600 * 1000);

        var resultado = Ejecutar(new
        {
            datosEstaticos = new { entregados = new Dictionary<string, long> { [viejo.ToString("D")] = hace49Horas } },
            pasos = new[] { Verificar(Cuerpo(viejo), ahoraMs: ahora) },
        });

        Assert.False(Primero(resultado).GetProperty("duplicado").GetBoolean());
        Assert.DoesNotContain(viejo.ToString("D"), resultado.GetProperty("datos").GetRawText(), StringComparison.Ordinal);
    }

    [TheorySiHayNode]
    [InlineData("pedido-listo-para-recojo", "{\"numeroDePedido\":\"BH-000123\",\"nombre\":\"María\",\"sucursal\":\"AYACUCHO\"}", "Hola María, tu pedido BH-000123 está listo para recoger en la sucursal AYACUCHO. — BabelHub")]
    [InlineData("pedido-listo-para-recojo", "{\"numeroDePedido\":\"BH-000123\"}", "Hola, tu pedido BH-000123 está listo para recoger. — BabelHub")]
    [InlineData("traslado-cambio-de-estado", "{\"numeroDeTraslado\":\"7\",\"estado\":\"en-transito\",\"rolDeLaSucursal\":\"destino\",\"cantidad\":\"3\"}", "Traslado 7: estado en-transito. Cantidad: 3. — BabelHub")]
    [Trait("Criterio", "AC-NOT-09")]
    public void ElMensaje_SeArmaConLaPlantillaDelFlujo_ConLosCamposYLosDerivados(string plantilla, string camposJson, string esperado)
    {
        var campos = JsonSerializer.Deserialize<Dictionary<string, string>>(camposJson)!;
        var cuerpo = JsonSerializer.Serialize(new { avisoId = Guid.NewGuid().ToString("D"), plantilla, version = 1, campos, numero = "+51987654321" });

        var verificado = Primero(Ejecutar(new { pasos = new[] { Verificar(cuerpo) } }));
        var segundo = Ejecutar(new
        {
            pasos = new object[]
            {
                new
                {
                    nodo = "Armar mensaje",
                    anteriores = new Dictionary<string, object> { ["Verificar"] = new { avisoId = verificado.GetProperty("avisoId").GetString(), numero = "+51987654321", campos, texto = verificado.GetProperty("texto").GetString() } },
                },
            },
        });

        Assert.Equal(esperado, Primero(segundo).GetProperty("texto").GetString());
        Assert.Equal("+51987654321", Primero(segundo).GetProperty("numero").GetString());
    }

    [TheorySiHayNode]
    [InlineData("entregado")]
    [InlineData("fallido")]
    [Trait("Criterio", "AC-NOT-11")]
    public void ElEstadoSale_FirmadoConLaClaveEntrante_ConElFormatoQueVerificaElServicio(string estado)
    {
        var aviso = Guid.NewGuid();
        var resultado = Ejecutar(new
        {
            pasos = new object[] { new { nodo = "Firmar estado", json = new { avisoId = aviso.ToString("D"), estado }, env = new Dictionary<string, string> { ["BABELHUB_FIRMA_DE_ESTADOS"] = ClaveDeEstados } } },
        });
        var json = Primero(resultado);
        var cuerpo = json.GetProperty("cuerpo").GetString()!;
        var marca = json.GetProperty("marca").GetString()!;
        var firma = json.GetProperty("firma").GetString()!;

        var esperada = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ClaveDeEstados), Encoding.UTF8.GetBytes(marca + "." + cuerpo))).ToLowerInvariant();

        Assert.Equal(esperada, firma);
        Assert.Equal($"{{\"avisoId\":\"{aviso:D}\",\"estado\":\"{estado}\"}}", cuerpo);
        Assert.InRange(long.Parse(marca, System.Globalization.CultureInfo.InvariantCulture), DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 5, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 5);
    }

    [FactSiHayNode]
    [Trait("Criterio", "AC-NOT-11")]
    public void SinLaClaveConfigurada_ElNodoVerificarRechazaTodo()
    {
        var cuerpo = Cuerpo();
        var ahora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sinClave = new { nodo = "Verificar", cuerpoCrudo = cuerpo, cabeceras = new Dictionary<string, string> { ["x-babelhub-marca"] = ahora.ToString(System.Globalization.CultureInfo.InvariantCulture), ["x-babelhub-firma"] = Firma(string.Empty, ahora, cuerpo) }, env = new Dictionary<string, string>() };

        var json = Primero(Ejecutar(new { pasos = new[] { sinClave } }));

        Assert.False(json.GetProperty("valido").GetBoolean());
        Assert.Equal(401, json.GetProperty("status").GetInt32());
    }
}

/// <summary>Un <c>Fact</c> que se omite, con su motivo a la vista, si Node.js no está instalado (la ejecución del flujo de n8n lo necesita).</summary>
public sealed class FactSiHayNodeAttribute : FactAttribute
{
    public FactSiHayNodeAttribute()
    {
        if (!NodeDisponible.Hay)
        {
            Skip = "Node.js no está instalado: se omite la ejecución del flujo de n8n.";
        }
    }
}

/// <summary>Un <c>Theory</c> que se omite si Node.js no está instalado.</summary>
public sealed class TheorySiHayNodeAttribute : TheoryAttribute
{
    public TheorySiHayNodeAttribute()
    {
        if (!NodeDisponible.Hay)
        {
            Skip = "Node.js no está instalado: se omite la ejecución del flujo de n8n.";
        }
    }
}

internal static class NodeDisponible
{
    public static bool Hay { get; } = Buscar();

    private static bool Buscar()
    {
        foreach (var candidato in new[] { "node", @"C:\Program Files\nodejs\node.exe", "/usr/bin/node", "/usr/local/bin/node" })
        {
            try
            {
                using var proceso = Process.Start(new ProcessStartInfo(candidato, "--version") { RedirectStandardOutput = true, RedirectStandardError = true });
                if (proceso is not null && proceso.WaitForExit(5000) && proceso.ExitCode == 0)
                {
                    return true;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Ese candidato no existe: se prueba el siguiente.
            }
        }

        return false;
    }
}
