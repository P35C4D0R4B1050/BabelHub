using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Ajustes.Interno;

/// <summary>Un error de lectura de uno de los archivos de la carga: el archivo, la línea (0 si es del archivo entero) y una clave simbólica; nunca el valor leído (SEG-10).</summary>
internal sealed record ErrorDeLecturaDeLaCarga(string Archivo, int Linea, string Clave)
{
    public override string ToString() => $"{Archivo}:{Linea}:{Clave}";
}

internal sealed class ResultadoDeLectura<T>
{
    private ResultadoDeLectura(T? valor, IReadOnlyList<ErrorDeLecturaDeLaCarga> errores)
    {
        Valor = valor;
        Errores = errores;
    }

    public T? Valor { get; }

    public IReadOnlyList<ErrorDeLecturaDeLaCarga> Errores { get; }

    public bool EsValido => Errores.Count == 0;

    public static ResultadoDeLectura<T> Exito(T valor) => new(valor, []);

    public static ResultadoDeLectura<T> Fallo(IReadOnlyList<ErrorDeLecturaDeLaCarga> errores) => new(default, errores);
}

internal sealed record SucursalDeLaCarga(string Codigo, string Nombre, string Region);

internal sealed record RutaDeLaCarga(string Origen, string Destino, int Dias);

internal sealed record StockDeLaCarga(string Codigo, int Cantidad);

internal sealed record EdicionDelPlanDeInventario(Guid EdicionId, string Isbn, IReadOnlyList<StockDeLaCarga> Stock);

internal sealed record PlanDeInventario(IReadOnlyList<EdicionDelPlanDeInventario> Ediciones);

/// <summary>
/// Las reglas de entrada comunes de los archivos de la carga (CIN-04): hasta 2 MB de texto, sin caracteres de control (salvo el fin de línea), de dirección de texto ni separadores de línea de Unicode. El archivo
/// es una entrada no confiable: se valida entero antes de escribir nada.
/// </summary>
internal static class ReglasDeLosArchivosDeLaCarga
{
    public const int MaximoDeCaracteres = 2 * 1024 * 1024;

    public const int MaximoDeFilas = 500;

    /// <summary>Los de dirección de texto (U+202A a U+202E y U+2066 a U+2069), los separadores de línea y de párrafo de Unicode (U+2028 y U+2029) y la marca de orden de bytes (U+FEFF).</summary>
    private static bool EsDeDireccionOSeparador(char c) => c is (>= (char)0x202A and <= (char)0x202E) or (>= (char)0x2066 and <= (char)0x2069) or (char)0x2028 or (char)0x2029 or (char)0xFEFF;

    public static string? ProblemaDelTexto(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return "archivo-vacio";
        }

        if (texto.Length > MaximoDeCaracteres)
        {
            return "archivo-demasiado-grande";
        }

        foreach (var c in texto)
        {
            if (c is '\n' or '\r')
            {
                continue;
            }

            if (char.IsControl(c) || EsDeDireccionOSeparador(c))
            {
                return "caracter-no-permitido";
            }
        }

        return null;
    }
}

/// <summary>El lector de archivos de la carga con separador «;» y comillas dobles opcionales (un campo no ocupa varias líneas), con las columnas exactas del archivo.</summary>
internal static class LectorDeCsvDeLaCarga
{
    public static ResultadoDeLectura<IReadOnlyList<IReadOnlyDictionary<string, string>>> Leer(string archivo, string? texto, IReadOnlyList<string> columnas)
    {
        if (ReglasDeLosArchivosDeLaCarga.ProblemaDelTexto(texto) is { } problema)
        {
            return ResultadoDeLectura<IReadOnlyList<IReadOnlyDictionary<string, string>>>.Fallo([new ErrorDeLecturaDeLaCarga(archivo, 0, problema)]);
        }

        var lineas = texto!.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').ToList();
        while (lineas.Count > 0 && lineas[^1].Length == 0)
        {
            lineas.RemoveAt(lineas.Count - 1);
        }

        var errores = new List<ErrorDeLecturaDeLaCarga>();
        if (lineas.Count == 0)
        {
            return ResultadoDeLectura<IReadOnlyList<IReadOnlyDictionary<string, string>>>.Fallo([new ErrorDeLecturaDeLaCarga(archivo, 1, "columnas-invalidas")]);
        }

        var cabecera = Dividir(lineas[0], out _);
        if (!EncabezadoCoincide(cabecera, columnas))
        {
            return ResultadoDeLectura<IReadOnlyList<IReadOnlyDictionary<string, string>>>.Fallo([new ErrorDeLecturaDeLaCarga(archivo, 1, "columnas-invalidas")]);
        }
        if (lineas.Count - 1 > ReglasDeLosArchivosDeLaCarga.MaximoDeFilas)
        {
            return ResultadoDeLectura<IReadOnlyList<IReadOnlyDictionary<string, string>>>.Fallo([new ErrorDeLecturaDeLaCarga(archivo, 0, "demasiadas-filas")]);
        }

        var filas = new List<IReadOnlyDictionary<string, string>>();
        for (var i = 1; i < lineas.Count; i++)
        {
            if (lineas[i].Length == 0)
            {
                continue;
            }

            var celdas = Dividir(lineas[i], out var valida);
            if (!valida || celdas.Count != cabecera.Count)
            {
                errores.Add(new ErrorDeLecturaDeLaCarga(archivo, i + 1, "fila-mal-formada"));
                continue;
            }

            filas.Add(cabecera.Zip(celdas).ToDictionary(p => p.First, p => p.Second.Trim(), StringComparer.Ordinal));
        }

        return errores.Count > 0 ? ResultadoDeLectura<IReadOnlyList<IReadOnlyDictionary<string, string>>>.Fallo(errores) : ResultadoDeLectura<IReadOnlyList<IReadOnlyDictionary<string, string>>>.Exito(filas);
    }

    private static bool EncabezadoCoincide(List<string> cabecera, IReadOnlyList<string> columnas) =>
        cabecera.Count == columnas.Count && cabecera.Distinct(StringComparer.Ordinal).Count() == cabecera.Count && columnas.All(c => cabecera.Contains(c, StringComparer.Ordinal));

    private static List<string> Dividir(string linea, out bool valida)
    {
        valida = true;
        var celdas = new List<string>();
        var actual = new StringBuilder();
        var entreComillas = false;
        for (var i = 0; i < linea.Length; i++)
        {
            var c = linea[i];
            if (entreComillas)
            {
                if (c == '"' && i + 1 < linea.Length && linea[i + 1] == '"')
                {
                    actual.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    entreComillas = false;
                }
                else
                {
                    actual.Append(c);
                }
            }
            else if (c == '"' && actual.Length == 0)
            {
                entreComillas = true;
            }
            else if (c == ';')
            {
                celdas.Add(actual.ToString());
                actual.Clear();
            }
            else
            {
                actual.Append(c);
            }
        }

        valida = !entreComillas;
        celdas.Add(actual.ToString());
        return celdas;
    }
}

/// <summary>Los lectores de <c>sucursales.csv</c> y <c>rutas.csv</c> (CIN-04, CIN-12): el código de una sucursal tiene hasta 30 caracteres para que la clave de idempotencia de la carga quepa en los 80 del contrato.</summary>
internal static partial class LectorDeSucursalesYRutas
{
    public const int LargoMaximoDelCodigo = 30;

    public const int LargoMaximoDelNombre = 200;

    public static ResultadoDeLectura<IReadOnlyList<SucursalDeLaCarga>> LeerSucursales(string? texto)
    {
        const string Archivo = "sucursales";
        var lectura = LectorDeCsvDeLaCarga.Leer(Archivo, texto, ["codigo", "nombre", "region"]);
        if (!lectura.EsValido)
        {
            return ResultadoDeLectura<IReadOnlyList<SucursalDeLaCarga>>.Fallo(lectura.Errores);
        }

        var errores = new List<ErrorDeLecturaDeLaCarga>();
        var sucursales = new List<SucursalDeLaCarga>();
        var linea = 1;
        foreach (var fila in lectura.Valor!)
        {
            linea++;
            if (!EsCodigoValido(fila["codigo"]))
            {
                errores.Add(new ErrorDeLecturaDeLaCarga(Archivo, linea, "codigo"));
            }
            else if (sucursales.Any(s => s.Codigo == fila["codigo"]))
            {
                errores.Add(new ErrorDeLecturaDeLaCarga(Archivo, linea, "codigo-repetido"));
            }

            if (fila["nombre"].Length is 0 or > LargoMaximoDelNombre)
            {
                errores.Add(new ErrorDeLecturaDeLaCarga(Archivo, linea, "nombre"));
            }

            if (!RegionesDeEntrega.Es(fila["region"]))
            {
                errores.Add(new ErrorDeLecturaDeLaCarga(Archivo, linea, "region"));
            }

            sucursales.Add(new SucursalDeLaCarga(fila["codigo"], fila["nombre"], fila["region"]));
        }

        if (sucursales.Count == 0 && errores.Count == 0)
        {
            errores.Add(new ErrorDeLecturaDeLaCarga(Archivo, 0, "sin-sucursales"));
        }

        return errores.Count > 0 ? ResultadoDeLectura<IReadOnlyList<SucursalDeLaCarga>>.Fallo(errores) : ResultadoDeLectura<IReadOnlyList<SucursalDeLaCarga>>.Exito(sucursales);
    }

    public static ResultadoDeLectura<IReadOnlyList<RutaDeLaCarga>> LeerRutas(string? texto, IReadOnlyCollection<string> codigosConocidos)
    {
        const string Archivo = "rutas";
        var lectura = LectorDeCsvDeLaCarga.Leer(Archivo, texto, ["origen", "destino", "dias"]);
        if (!lectura.EsValido)
        {
            return ResultadoDeLectura<IReadOnlyList<RutaDeLaCarga>>.Fallo(lectura.Errores);
        }

        var errores = new List<ErrorDeLecturaDeLaCarga>();
        var rutas = new List<RutaDeLaCarga>();
        var linea = 1;
        foreach (var fila in lectura.Valor!)
        {
            linea++;
            if (!codigosConocidos.Contains(fila["origen"]) || !codigosConocidos.Contains(fila["destino"]) || fila["origen"] == fila["destino"])
            {
                errores.Add(new ErrorDeLecturaDeLaCarga(Archivo, linea, "sucursal-desconocida"));
            }

            if (!int.TryParse(fila["dias"], NumberStyles.None, CultureInfo.InvariantCulture, out var dias) || dias is < 1 or > 60)
            {
                errores.Add(new ErrorDeLecturaDeLaCarga(Archivo, linea, "dias"));
                continue;
            }

            if (rutas.Any(r => r.Origen == fila["origen"] && r.Destino == fila["destino"]))
            {
                errores.Add(new ErrorDeLecturaDeLaCarga(Archivo, linea, "ruta-repetida"));
            }

            rutas.Add(new RutaDeLaCarga(fila["origen"], fila["destino"], dias));
        }

        return errores.Count > 0 ? ResultadoDeLectura<IReadOnlyList<RutaDeLaCarga>>.Fallo(errores) : ResultadoDeLectura<IReadOnlyList<RutaDeLaCarga>>.Exito(rutas);
    }

    public static bool EsCodigoValido(string? codigo) => codigo is { Length: > 0 and <= LargoMaximoDelCodigo } && CodigoDeSucursal().IsMatch(codigo);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodigoDeSucursal();
}

/// <summary>
/// El lector del plan de inventario v1 (CIN-10, CIN-11): <c>{"version":1,"ediciones":[{"edicionId":"…","isbn":"…","stock":{"código":n}}]}</c>. Es la frontera de confianza entre las dos órdenes: esquema estricto (sin claves
/// desconocidas ni repetidas, profundidad acotada), hasta 500 ediciones, identificadores e ISBN distintos, stock de 1 a 5 000 y solo sucursales conocidas; un solo defecto rechaza el plan entero.
/// </summary>
internal static partial class LectorDelPlanDeInventario
{
    public const int StockMinimo = 1;

    public const int StockMaximo = 5_000;

    private const string Archivo = "plan";

    public static ResultadoDeLectura<PlanDeInventario> Leer(string? texto, IReadOnlyCollection<string> codigosConocidos)
    {
        if (ReglasDeLosArchivosDeLaCarga.ProblemaDelTexto(texto) is { } problema)
        {
            return Fallo(problema);
        }

        try
        {
            return Interpretar(texto!, codigosConocidos);
        }
        catch (JsonException)
        {
            return Fallo("json-invalido");
        }
        catch (InvalidOperationException)
        {
            return Fallo("json-invalido");
        }
    }

    private static ResultadoDeLectura<PlanDeInventario> Interpretar(string texto, IReadOnlyCollection<string> codigosConocidos)
    {
        var lector = new Utf8JsonReader(Encoding.UTF8.GetBytes(texto), new JsonReaderOptions { MaxDepth = 6, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
        var version = -1;
        List<EdicionDelPlanDeInventario>? ediciones = null;
        var vistas = new HashSet<string>(StringComparer.Ordinal);
        Leer(ref lector, JsonTokenType.StartObject);
        while (Siguiente(ref lector) != JsonTokenType.EndObject)
        {
            var clave = lector.GetString()!;
            if (!vistas.Add(clave))
            {
                return Fallo("clave-repetida");
            }

            switch (clave)
            {
                case "version":
                    Leer(ref lector, JsonTokenType.Number);
                    if (!lector.TryGetInt32(out version))
                    {
                        return Fallo("version");
                    }

                    break;
                case "ediciones":
                    var resultado = LeerEdiciones(ref lector, codigosConocidos);
                    if (!resultado.EsValido)
                    {
                        return ResultadoDeLectura<PlanDeInventario>.Fallo(resultado.Errores);
                    }

                    ediciones = resultado.Valor!.ToList();
                    break;
                default:
                    return Fallo("clave-desconocida");
            }
        }

        if (lector.Read())
        {
            return Fallo("json-invalido");
        }

        if (version != 1)
        {
            return Fallo("version");
        }

        if (ediciones is null)
        {
            return Fallo("sin-ediciones");
        }

        if (ediciones.Select(e => e.EdicionId).Distinct().Count() != ediciones.Count || ediciones.Select(e => e.Isbn).Distinct(StringComparer.Ordinal).Count() != ediciones.Count)
        {
            return Fallo("edicion-repetida");
        }

        return ResultadoDeLectura<PlanDeInventario>.Exito(new PlanDeInventario(ediciones));
    }

    private static ResultadoDeLectura<IReadOnlyList<EdicionDelPlanDeInventario>> LeerEdiciones(ref Utf8JsonReader lector, IReadOnlyCollection<string> codigosConocidos)
    {
        var resultado = new List<EdicionDelPlanDeInventario>();
        Leer(ref lector, JsonTokenType.StartArray);
        while (Siguiente(ref lector) != JsonTokenType.EndArray)
        {
            if (lector.TokenType != JsonTokenType.StartObject || resultado.Count >= ReglasDeLosArchivosDeLaCarga.MaximoDeFilas)
            {
                return ResultadoDeLectura<IReadOnlyList<EdicionDelPlanDeInventario>>.Fallo([new ErrorDeLecturaDeLaCarga(Archivo, 0, resultado.Count >= ReglasDeLosArchivosDeLaCarga.MaximoDeFilas ? "demasiadas-ediciones" : "edicion")]);
            }

            Guid? id = null;
            string? isbn = null;
            List<StockDeLaCarga>? stock = null;
            var vistas = new HashSet<string>(StringComparer.Ordinal);
            while (Siguiente(ref lector) != JsonTokenType.EndObject)
            {
                var clave = lector.GetString()!;
                if (!vistas.Add(clave))
                {
                    return ResultadoDeLectura<IReadOnlyList<EdicionDelPlanDeInventario>>.Fallo([new ErrorDeLecturaDeLaCarga(Archivo, 0, "clave-repetida")]);
                }

                switch (clave)
                {
                    case "edicionId":
                        Leer(ref lector, JsonTokenType.String);
                        id = Guid.TryParseExact(lector.GetString(), "D", out var g) && g != Guid.Empty ? g : null;
                        break;
                    case "isbn":
                        Leer(ref lector, JsonTokenType.String);
                        isbn = lector.GetString();
                        break;
                    case "stock":
                        var lectura = LeerStock(ref lector, codigosConocidos);
                        if (!lectura.EsValido)
                        {
                            return ResultadoDeLectura<IReadOnlyList<EdicionDelPlanDeInventario>>.Fallo(lectura.Errores);
                        }

                        stock = lectura.Valor!.ToList();
                        break;
                    default:
                        return ResultadoDeLectura<IReadOnlyList<EdicionDelPlanDeInventario>>.Fallo([new ErrorDeLecturaDeLaCarga(Archivo, 0, "clave-desconocida")]);
                }
            }

            if (id is null || isbn is null || !Isbn13().IsMatch(isbn) || stock is null || stock.Count == 0)
            {
                return ResultadoDeLectura<IReadOnlyList<EdicionDelPlanDeInventario>>.Fallo([new ErrorDeLecturaDeLaCarga(Archivo, 0, "edicion")]);
            }

            resultado.Add(new EdicionDelPlanDeInventario(id.Value, isbn, stock));
        }

        return ResultadoDeLectura<IReadOnlyList<EdicionDelPlanDeInventario>>.Exito(resultado);
    }

    private static ResultadoDeLectura<IReadOnlyList<StockDeLaCarga>> LeerStock(ref Utf8JsonReader lector, IReadOnlyCollection<string> codigosConocidos)
    {
        var stock = new List<StockDeLaCarga>();
        Leer(ref lector, JsonTokenType.StartObject);
        while (Siguiente(ref lector) != JsonTokenType.EndObject)
        {
            var codigo = lector.GetString()!;
            if (stock.Any(s => s.Codigo == codigo))
            {
                return ResultadoDeLectura<IReadOnlyList<StockDeLaCarga>>.Fallo([new ErrorDeLecturaDeLaCarga(Archivo, 0, "clave-repetida")]);
            }

            Leer(ref lector, JsonTokenType.Number);
            if (!LectorDeSucursalesYRutas.EsCodigoValido(codigo) || !codigosConocidos.Contains(codigo))
            {
                return ResultadoDeLectura<IReadOnlyList<StockDeLaCarga>>.Fallo([new ErrorDeLecturaDeLaCarga(Archivo, 0, "sucursal-desconocida")]);
            }

            if (!lector.TryGetInt32(out var cantidad) || cantidad is < StockMinimo or > StockMaximo)
            {
                return ResultadoDeLectura<IReadOnlyList<StockDeLaCarga>>.Fallo([new ErrorDeLecturaDeLaCarga(Archivo, 0, "stock-fuera-de-rango")]);
            }

            stock.Add(new StockDeLaCarga(codigo, cantidad));
        }

        return ResultadoDeLectura<IReadOnlyList<StockDeLaCarga>>.Exito(stock);
    }

    private static JsonTokenType Siguiente(ref Utf8JsonReader lector)
    {
        if (!lector.Read())
        {
            throw new JsonException();
        }

        return lector.TokenType;
    }

    private static void Leer(ref Utf8JsonReader lector, JsonTokenType esperado)
    {
        if (Siguiente(ref lector) != esperado)
        {
            throw new JsonException();
        }
    }

    private static ResultadoDeLectura<PlanDeInventario> Fallo(string clave) => ResultadoDeLectura<PlanDeInventario>.Fallo([new ErrorDeLecturaDeLaCarga(Archivo, 0, clave)]);

    [GeneratedRegex("^97[89][0-9]{10}$", RegexOptions.CultureInvariant)]
    private static partial Regex Isbn13();
}
