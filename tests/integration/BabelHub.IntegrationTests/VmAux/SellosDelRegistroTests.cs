using System.Globalization;
using System.Text.Json;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.IntegrationTests.Imagenes;

namespace BabelHub.IntegrationTests.VmAux;

/// <summary>
/// PLA-38, AC-PLA-37 y la prueba de conformidad del plan: el sello que escribe el script de VM-AUX es el que verifica
/// <c>VerificadorDelRegistro</c> del E-commerce (mismas formas canónicas, mismo conjunto cubierto, mismo orden de claves), y cualquier
/// alteración posterior —una suma cambiada, una entrada borrada, una agregada— la detecta. También la copia diaria del registro. Cada script
/// usa solo lo que su credencial permite (el almacén de prueba aplica <c>credenciales.txt</c>).
/// </summary>
[Collection(ProcesosPesadosCollection.Nombre)]
[Trait("Categoria", Categorias.Guiones)]
public sealed class SellosDelRegistroTests : IDisposable
{
    private static readonly DateTime Hoy = DateTime.UtcNow;
    private readonly EntornoDeVmAux _entorno = new();

    public void Dispose() => _entorno.Dispose();

    private static string Contenido(EntradaDeSupresion entrada) =>
        JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["identificadorOpaco"] = entrada.IdentificadorOpaco,
            ["tipo"] = EntradaDeSupresion.NombreDe(entrada.Tipo),
            ["fase"] = EntradaDeSupresion.NombreDe(entrada.Fase),
            ["fecha"] = entrada.Fecha.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture),
            ["suma"] = entrada.Suma,
        });

    /// <summary>Deja una entrada en el registro de prueba tal como la escribe el adaptador del E-commerce.</summary>
    private EntradaLeida Agregar(string identificador, TipoDeSupresion tipo, FaseDeSupresion fase, DateTime modificada)
    {
        var entrada = EntradaDeSupresion.Crear(identificador, tipo, fase, new DateTimeOffset(modificada, TimeSpan.Zero));
        _entorno.Publicar("supresiones-entradas", entrada.Clave, Contenido(entrada), modificada);
        var segundos = DateTime.ParseExact(EntornoDeVmAux.Hora(modificada), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return new EntradaLeida(entrada.Clave, entrada, new DateTimeOffset(DateTime.SpecifyKind(segundos, DateTimeKind.Utc)));
    }

    /// <summary>Un registro con claves de mayúsculas, minúsculas, guiones y guiones bajos (para el orden ordinal) y dos entradas recientes.</summary>
    private List<EntradaLeida> RegistroDeEjemplo()
    {
        var entradas = new List<EntradaLeida>
        {
            Agregar("Cuenta-AAAA-0000000001", TipoDeSupresion.Cancelacion, FaseDeSupresion.Intencion, Hoy.AddDays(-9)),
            Agregar("Cuenta-AAAA-0000000001", TipoDeSupresion.Cancelacion, FaseDeSupresion.Cierre, Hoy.AddDays(-9).AddMinutes(1)),
            Agregar("cuenta_bbbb_0000000002", TipoDeSupresion.Anonimizacion, FaseDeSupresion.Intencion, Hoy.AddDays(-3)),
            Agregar("9uenta-cccc-000000000003", TipoDeSupresion.Cancelacion, FaseDeSupresion.Intencion, Hoy.AddHours(-5)),
            Agregar("cuenta-dddd-0000000004-x", TipoDeSupresion.Cancelacion, FaseDeSupresion.Cierre, Hoy.AddHours(-2)),
        };

        // Dos entradas escritas hace 5 y 10 minutos: dentro del margen de 15 minutos, no entran al sello.
        entradas.Add(Agregar("cuenta-eeee-0000000005", TipoDeSupresion.Cancelacion, FaseDeSupresion.Intencion, Hoy.AddMinutes(-5)));
        entradas.Add(Agregar("cuenta-ffff-0000000006", TipoDeSupresion.Cancelacion, FaseDeSupresion.Intencion, Hoy.AddMinutes(-10)));
        return entradas;
    }

    private (SelloDelRegistro Sello, string Clave) LeerElSello()
    {
        var clave = Assert.Single(_entorno.Claves("supresiones-sellos"));
        Assert.Matches(@"^sellos/\d{4}-\d{2}-\d{2}-\d{4}\.json$", clave);
        using var documento = JsonDocument.Parse(_entorno.Leer("supresiones-sellos", clave)!);
        var raiz = documento.RootElement;
        var sello = new SelloDelRegistro(
            DateTimeOffset.Parse(raiz.GetProperty("instante").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            raiz.GetProperty("cantidad").GetInt32(),
            raiz.GetProperty("sumaDelConjunto").GetString()!);
        return (sello, clave);
    }

    // ---------- Conformidad con el verificador del E-commerce ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void ElSelloDelScript_ElVerificadorDelEcommerceLoDaPorValido_ConLaCantidadYLaSumaDelConjuntoCubierto()
    {
        var entradas = RegistroDeEjemplo();

        var (codigo, salida, errores) = _entorno.Ejecutar("sellar-registro.sh");

        Assert.True(codigo == 0, salida + errores);
        var (sello, _) = LeerElSello();
        Assert.Equal(5, sello.Cantidad);
        Assert.Equal(sello.SumaDelConjunto, SelloDelRegistro.SumaDelConjuntoDe(sello.ConjuntoSellado(entradas)));
        Assert.Equal(EstadoDeVerificacionDelRegistro.Valido, VerificadorDelRegistro.Verificar(entradas, sello).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void ElSelloDeUnRegistroVacio_EsElDeLaCadenaVacia_ComoElDelVerificador()
    {
        var (codigo, salida, errores) = _entorno.Ejecutar("sellar-registro.sh");

        Assert.True(codigo == 0, salida + errores);
        var (sello, _) = LeerElSello();
        Assert.Equal(0, sello.Cantidad);
        Assert.Equal(SelloDelRegistro.SumaDelConjuntoDe([]), sello.SumaDelConjunto);
        Assert.Equal(EstadoDeVerificacionDelRegistro.Valido, VerificadorDelRegistro.Verificar([], sello).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void LoEscritoDentroDelMargenDeQuinceMinutos_NoCambiaLaVerificacion()
    {
        var entradas = RegistroDeEjemplo();
        _entorno.Ejecutar("sellar-registro.sh");
        var (sello, _) = LeerElSello();

        // Una entrada nueva escrita después del sello: no está cubierta, el verificador la ignora.
        var nueva = EntradaDeSupresion.Crear("cuenta-gggg-0000000007", TipoDeSupresion.Cancelacion, FaseDeSupresion.Intencion, DateTimeOffset.UtcNow);
        entradas.Add(new EntradaLeida(nueva.Clave, nueva, DateTimeOffset.UtcNow));

        Assert.Equal(EstadoDeVerificacionDelRegistro.Valido, VerificadorDelRegistro.Verificar(entradas, sello).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void SiDespuesDelSelloSeCambiaUnaSuma_SeBorraUnaEntradaOSeAgregaUnaFalsa_ElVerificadorLoDetecta()
    {
        var entradas = RegistroDeEjemplo();
        _entorno.Ejecutar("sellar-registro.sh");
        var (sello, _) = LeerElSello();

        var cambiada = entradas.ToList();
        var original = cambiada[2];
        cambiada[2] = original with { Entrada = EntradaDeSupresion.Reconstruir(original.Entrada.IdentificadorOpaco, original.Entrada.Tipo, original.Entrada.Fase, original.Entrada.Fecha, new string('a', 64)) };
        var borrada = entradas.Where(e => e.ClaveEnElAlmacen != entradas[3].ClaveEnElAlmacen).ToList();
        var agregada = entradas.ToList();
        var falsa = EntradaDeSupresion.Crear("cuenta-falsa-0000000099", TipoDeSupresion.Anonimizacion, FaseDeSupresion.Cierre, DateTimeOffset.UtcNow.AddDays(-30));
        agregada.Add(new EntradaLeida(falsa.Clave, falsa, DateTimeOffset.UtcNow.AddDays(-30)));

        Assert.Equal(EstadoDeVerificacionDelRegistro.EntradaAlterada, VerificadorDelRegistro.Verificar(cambiada, sello).Estado);
        Assert.Equal(EstadoDeVerificacionDelRegistro.EntradasAusentes, VerificadorDelRegistro.Verificar(borrada, sello).Estado);
        Assert.Equal(EstadoDeVerificacionDelRegistro.SumaDelConjuntoNoCoincide, VerificadorDelRegistro.Verificar(agregada, sello).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void ElSelloSiguiente_CubreLasEntradasQueEntraronDespues()
    {
        RegistroDeEjemplo();
        _entorno.Ejecutar("sellar-registro.sh");
        var primero = _entorno.Leer("supresiones-sellos", _entorno.Claves("supresiones-sellos")[0])!;

        // Pasa el tiempo: lo que estaba dentro del margen entra al conjunto sellado (se simula envejeciendo esas entradas en el almacén).
        var tiempos = Path.Combine(_entorno.Almacen, "supresiones-entradas.tiempos");
        File.WriteAllLines(tiempos, File.ReadAllLines(tiempos).Select(l => l.Split(';')[0] + ";" + EntornoDeVmAux.Hora(Hoy.AddHours(-1))));
        File.Delete(Path.Combine(_entorno.Almacen, "supresiones-sellos", _entorno.Claves("supresiones-sellos")[0].Replace('/', Path.DirectorySeparatorChar)));
        File.WriteAllText(Path.Combine(_entorno.Almacen, "supresiones-sellos.tiempos"), string.Empty);
        var (codigo, _, errores) = _entorno.Ejecutar("sellar-registro.sh");

        Assert.True(codigo == 0, errores);
        var (segundo, _) = LeerElSello();
        Assert.Equal(7, segundo.Cantidad);
        Assert.NotEqual(primero, _entorno.Leer("supresiones-sellos", _entorno.Claves("supresiones-sellos")[0]));
    }

    // ---------- Credenciales mínimas y fallos ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void ElSellador_SoloListaYLeeLasEntradas_YCreaSuSelloEnSuContenedor()
    {
        RegistroDeEjemplo();

        var (codigo, salida, errores) = _entorno.Ejecutar("sellar-registro.sh");

        Assert.True(codigo == 0, salida + errores);
        Assert.Contains(_entorno.Llamadas, l => l.StartsWith("lsf ", StringComparison.Ordinal) && l.Contains("sello:supresiones-entradas", StringComparison.Ordinal));
        Assert.Contains(_entorno.Llamadas, l => l.StartsWith("cat ", StringComparison.Ordinal) && l.Contains("sello:supresiones-entradas/entradas/", StringComparison.Ordinal));
        Assert.Single(_entorno.Llamadas, l => l.StartsWith("copyto ", StringComparison.Ordinal));
        Assert.Contains(_entorno.Llamadas, l => l.StartsWith("copyto ", StringComparison.Ordinal) && l.Contains("sello:supresiones-sellos/sellos/", StringComparison.Ordinal));
        Assert.All(_entorno.Llamadas, l => Assert.Matches(@"^(lsf|cat|copyto) ", l));
        Assert.DoesNotContain("AccessDenied", errores, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void ElServicioDeIdentidad_NoPuedeEscribirSellos_PorqueSuCredencialNoLlegaAEseContenedor()
    {
        // La matriz que aplica el almacén de prueba es la de credenciales.txt: supresiones-registro no crea en supresiones-sellos.
        var lineas = File.ReadAllLines(Path.Combine(EntornoDeVmAux.RaizDelRepositorio(), "infra", "almacen", "credenciales.txt"));

        Assert.DoesNotContain(lineas, l => l.StartsWith("supresiones-registro", StringComparison.Ordinal) && l.Contains("supresiones-sellos", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void UnaEntradaQueNoSePuedeInterpretar_NoSeSella_YSeAvisa()
    {
        RegistroDeEjemplo();
        _entorno.Publicar("supresiones-entradas", "entradas/cuenta-hhhh-0000000008/cancelacion-intencion.json", "{ esto no es una entrada }", Hoy.AddHours(-3));

        var (codigo, salida, _) = _entorno.Ejecutar("sellar-registro.sh");

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA sello del registro: la entrada entradas/cuenta-hhhh-0000000008/cancelacion-intencion.json no se puede interpretar", salida, StringComparison.Ordinal);
        Assert.Empty(_entorno.Claves("supresiones-sellos"));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void SiElRegistroNoSePuedeConsultar_ElSellador_AvisaSinSellar()
    {
        var (codigo, salida, _) = _entorno.Ejecutar("sellar-registro.sh", null, new Dictionary<string, string> { ["DOBLE_ROTO"] = "supresiones-entradas" });

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA sello del registro: no se pudo consultar supresiones-entradas", salida, StringComparison.Ordinal);
        Assert.Empty(_entorno.Claves("supresiones-sellos"));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void UnSelloNoSePisa_SiYaExisteElDeEsteMinuto_SeAvisaEnVezDeSobrescribir()
    {
        _entorno.Ejecutar("sellar-registro.sh");
        var primero = _entorno.Leer("supresiones-sellos", _entorno.Claves("supresiones-sellos")[0]);

        var (codigo, salida, _) = _entorno.Ejecutar("sellar-registro.sh");

        // Dentro del mismo minuto el nombre coincide y el almacén rechaza la sobrescritura; en otro minuto crea uno nuevo.
        if (codigo == 1)
        {
            Assert.Contains("ALERTA sello del registro: no se pudo escribir", salida, StringComparison.Ordinal);
            Assert.Equal(primero, _entorno.Leer("supresiones-sellos", _entorno.Claves("supresiones-sellos")[0]));
        }
        else
        {
            Assert.Equal(2, _entorno.Claves("supresiones-sellos").Count);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void ElUltimoSelloQuedaGuardadoParaElAncla()
    {
        RegistroDeEjemplo();

        _entorno.Ejecutar("sellar-registro.sh");

        var guardado = File.ReadAllText(Path.Combine(_entorno.Estado, "ultimo-sello.json"));
        Assert.Equal(_entorno.Leer("supresiones-sellos", _entorno.Claves("supresiones-sellos")[0]), guardado);
    }

    // ---------- Copia diaria del registro ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void LaCopiaDiaria_LlevaTodasLasEntradas_ConElNombreConLaSumaDelTar_YSoloUsaLoPermitido()
    {
        var entradas = RegistroDeEjemplo();

        var (codigo, salida, errores) = _entorno.Ejecutar("copiar-registro.sh");

        Assert.True(codigo == 0, salida + errores);
        var clave = Assert.Single(_entorno.Claves("supresiones-copia"));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}/\d{6}-[0-9a-f]{64}\.tar$", clave);
        Assert.Contains($"ok copia {clave} ({entradas.Count} entradas)", salida, StringComparison.Ordinal);
        var tar = File.ReadAllBytes(Path.Combine(_entorno.Almacen, "supresiones-copia", clave.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Equal(System.Text.RegularExpressions.Regex.Match(clave, "[0-9a-f]{64}").Value, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(tar)));
        var texto = System.Text.Encoding.Latin1.GetString(tar);
        Assert.All(entradas, e => Assert.Contains(e.Entrada.Suma, texto, StringComparison.Ordinal));

        // Lee con la credencial del sello y crea con la de la copia; nada más.
        Assert.Contains(_entorno.Llamadas, l => l.StartsWith("cat ", StringComparison.Ordinal) && l.Contains("sello:supresiones-entradas/", StringComparison.Ordinal));
        Assert.Single(_entorno.Llamadas, l => l.StartsWith("copyto ", StringComparison.Ordinal));
        Assert.Contains(_entorno.Llamadas, l => l.StartsWith("copyto ", StringComparison.Ordinal) && l.Contains("copia:supresiones-copia/", StringComparison.Ordinal));
        Assert.DoesNotContain("AccessDenied", errores, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void LaCopiaDeUnRegistroVacio_SeHaceIgual_ParaProbarQueLaCadenaFunciona()
    {
        var (codigo, salida, _) = _entorno.Ejecutar("copiar-registro.sh");

        Assert.Equal(0, codigo);
        Assert.Contains("(0 entradas)", salida, StringComparison.Ordinal);
        Assert.Single(_entorno.Claves("supresiones-copia"));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void SiLaCopiaNoSePuedeSubir_SeAlerta()
    {
        RegistroDeEjemplo();

        var (codigo, salida, _) = _entorno.Ejecutar("copiar-registro.sh", null, new Dictionary<string, string> { ["DOBLE_ROTO"] = "supresiones-copia" });

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA copia del registro: no se pudo subir la copia", salida, StringComparison.Ordinal);
        Assert.Empty(_entorno.Claves("supresiones-copia"));
    }
}
