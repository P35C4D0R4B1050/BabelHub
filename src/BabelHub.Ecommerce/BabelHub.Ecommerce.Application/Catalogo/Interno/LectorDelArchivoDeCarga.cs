using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

/// <summary>Una edición digital declarada por una fila: su ISBN, precio, formato y licencia (CIN-05; el territorio es Perú, decisión del 2026-10-16 de <c>ecommerce-catalogo</c>).</summary>
internal sealed record DigitalDeLaFila(Isbn Isbn, decimal Precio, FormatoDeArchivoDigital Formato, TipoDeLicenciaDeEdicion Licencia, string Titular, DateOnly? VigenteHasta);

/// <summary>Una fila válida del archivo de datos de la carga (todo ya validado: lo que el catálogo exige después lo decide su propio alta).</summary>
internal sealed record FilaDelArchivo(
    int Numero,
    string Clave,
    string Titulo,
    string Autor,
    string Editorial,
    string Categoria,
    string? Descripcion,
    string Portada,
    Isbn IsbnImpreso,
    decimal PrecioImpreso,
    DigitalDeLaFila? Digital,
    IReadOnlyList<KeyValuePair<string, int>> Stock);

/// <summary>El motivo por el que una fila no se carga: su número, su clave (si se leyó), el campo y una clave simbólica; <b>nunca el valor recibido</b> (SEG-01, CIN-20).</summary>
internal sealed record ErrorDeFilaDeCarga(int Fila, string Clave, string Campo, string Motivo);

/// <summary>Lo leído de un archivo: las filas válidas, los errores por fila o, si el archivo es ilegible en conjunto (CIN-04), el motivo único.</summary>
internal sealed record ArchivoDeCargaLeido(IReadOnlyList<FilaDelArchivo> Filas, IReadOnlyList<ErrorDeFilaDeCarga> Errores, string? MotivoDelArchivo)
{
    public static ArchivoDeCargaLeido Rechazado(string motivo) => new([], [], motivo);
}

/// <summary>
/// El lector y validador del archivo de datos de la carga inicial (CIN-03, CIN-04, CIN-15; plan §4): texto separado por <c>;</c> con comillas dobles, de hasta 2 MB y 500 filas, sin caracteres de control ni de dirección de texto ni
/// el carácter de sustitución de un UTF-8 mal leído, con cabecera de columnas conocidas (las de siempre y las <c>stock_&lt;código&gt;</c> que se quieran). Una celda que empieza con <c>=</c>, <c>+</c>, <c>-</c> o <c>@</c> es texto: nada
/// del archivo se interpreta. Un defecto del archivo entero lo rechaza antes de escribir; el de una fila rechaza solo esa fila. Los errores nunca repiten lo recibido.
/// </summary>
internal static partial class LectorDelArchivoDeCarga
{
    public const int MaximoDeBytes = 2 * 1024 * 1024;

    public const int MaximoDeFilas = 500;

    public const decimal PrecioImpresoMinimo = 25.00m;

    public const decimal PrecioImpresoMaximo = 120.00m;

    public const decimal PrecioDigitalMinimo = 5.00m;

    public const decimal PrecioDigitalMaximo = 120.00m;

    public const int StockMinimo = 1;

    public const int StockMaximo = 5_000;

    private const int MaximoDeCodigoDeSucursal = 40;

    private static readonly string[] ColumnasFijas =
    [
        "clave", "titulo", "autor", "editorial", "categoria", "descripcion", "portada", "isbn_impreso", "precio_impreso",
        "isbn_digital", "precio_digital", "formato_digital", "titular_digital", "licencia_digital", "vigente_hasta",
    ];

    public static ArchivoDeCargaLeido Leer(string? texto)
    {
        // Un UTF-8 con marca de orden de bytes (BOM) es el mismo texto: la marca no es parte de la cabecera.
        texto = texto?.TrimStart('\uFEFF');
        if (string.IsNullOrEmpty(texto))
        {
            return ArchivoDeCargaLeido.Rechazado("archivo-vacio");
        }

        if (texto.Length > MaximoDeBytes || Encoding.UTF8.GetByteCount(texto) > MaximoDeBytes)
        {
            return ArchivoDeCargaLeido.Rechazado("archivo-demasiado-grande");
        }

        if (TieneCaracterProhibido(texto))
        {
            return ArchivoDeCargaLeido.Rechazado("caracter-prohibido");
        }

        var lineas = texto.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lineas.Count > 0 && lineas[^1].Length == 0)
        {
            lineas.RemoveAt(lineas.Count - 1);
        }

        if (lineas.Count == 0)
        {
            return ArchivoDeCargaLeido.Rechazado("archivo-vacio");
        }

        if (lineas.Count - 1 > MaximoDeFilas)
        {
            return ArchivoDeCargaLeido.Rechazado("demasiadas-filas");
        }

        var cabecera = Separar(lineas[0]);
        if (cabecera is null)
        {
            return ArchivoDeCargaLeido.Rechazado("comillas-sin-cerrar");
        }

        var columnas = LeerCabecera(cabecera, out var motivoDeCabecera);
        if (columnas is null)
        {
            return ArchivoDeCargaLeido.Rechazado(motivoDeCabecera!);
        }

        var filas = new List<FilaDelArchivo>();
        var errores = new List<ErrorDeFilaDeCarga>();
        var isbnVistos = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < lineas.Count; i++)
        {
            var numero = i + 1;
            if (lineas[i].Trim().Length == 0)
            {
                continue;
            }

            var celdas = Separar(lineas[i]);
            if (celdas is null)
            {
                errores.Add(new ErrorDeFilaDeCarga(numero, string.Empty, "fila", "comillas-sin-cerrar"));
                continue;
            }

            if (celdas.Count != columnas.Count)
            {
                errores.Add(new ErrorDeFilaDeCarga(numero, string.Empty, "fila", "columnas-distintas-de-la-cabecera"));
                continue;
            }

            var fila = LeerFila(numero, columnas, celdas, isbnVistos, out var erroresDeLaFila);
            if (fila is not null)
            {
                filas.Add(fila);
            }
            else
            {
                errores.AddRange(erroresDeLaFila);
            }
        }

        return new ArchivoDeCargaLeido(filas, errores, null);
    }

    private static List<string>? LeerCabecera(List<string> cabecera, out string? motivo)
    {
        motivo = null;
        var vistas = new HashSet<string>(StringComparer.Ordinal);
        var normalizadas = new List<string>();
        foreach (var celda in cabecera)
        {
            var nombre = celda.Trim();
            var conocida = ColumnasFijas.Contains(nombre, StringComparer.Ordinal)
                || (nombre.StartsWith("stock_", StringComparison.Ordinal) && CodigoDeSucursal().IsMatch(nombre["stock_".Length..]));
            if (!conocida)
            {
                motivo = "columna-desconocida";
                return null;
            }

            if (!vistas.Add(nombre))
            {
                motivo = "columna-repetida";
                return null;
            }

            normalizadas.Add(nombre);
        }

        if (ColumnasFijas.Any(c => !vistas.Contains(c)))
        {
            motivo = "columna-faltante";
            return null;
        }

        return normalizadas;
    }

    private static FilaDelArchivo? LeerFila(int numero, List<string> columnas, List<string> celdas, HashSet<string> isbnVistos, out List<ErrorDeFilaDeCarga> errores)
    {
        var acumulados = new List<ErrorDeFilaDeCarga>();
        errores = acumulados;
        var valores = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < columnas.Count; i++)
        {
            valores[columnas[i]] = celdas[i].Trim();
        }

        var clave = valores["clave"];
        var claveParaErrores = ClaveDeFila().IsMatch(clave) ? clave : string.Empty;
        void Error(string campo, string motivo) => acumulados.Add(new ErrorDeFilaDeCarga(numero, claveParaErrores, campo, motivo));

        if (clave.Length == 0)
        {
            Error("clave", "obligatorio");
        }
        else if (!ClaveDeFila().IsMatch(clave))
        {
            Error("clave", "formato-invalido");
        }

        Texto(valores, "titulo", 200, Error);
        Texto(valores, "autor", 200, Error);
        Texto(valores, "editorial", 200, Error);
        Texto(valores, "categoria", 100, Error);

        var descripcion = DescripcionDeObra.Normalizar(valores["descripcion"], out var motivoDeDescripcion);
        if (motivoDeDescripcion is { } m)
        {
            Error("descripcion", m == MotivoDeDescripcionInvalida.DemasiadoLarga ? "demasiado-larga" : "caracter-prohibido");
        }

        var portada = valores["portada"];
        if (portada.Length == 0)
        {
            Error("portada", "obligatorio");
        }
        else if (!NombreDePortada().IsMatch(portada) || portada.Contains("..", StringComparison.Ordinal))
        {
            Error("portada", "portada-invalida");
        }

        var isbnImpreso = LeerIsbn(valores, "isbn_impreso", isbnVistos, Error);
        var precioImpreso = LeerPrecio(valores, "precio_impreso", PrecioImpresoMinimo, PrecioImpresoMaximo, Error);
        var digital = LeerDigital(valores, isbnVistos, Error);
        var stock = LeerStock(columnas, valores, Error);

        if (acumulados.Count > 0 || isbnImpreso is null || precioImpreso is null)
        {
            return null;
        }

        return new FilaDelArchivo(
            numero, clave, valores["titulo"], valores["autor"], valores["editorial"], valores["categoria"], descripcion, portada, isbnImpreso, precioImpreso.Value,
            digital?.Digital, stock);
    }

    private static DigitalDeLaFilaLeida? LeerDigital(Dictionary<string, string> valores, HashSet<string> isbnVistos, Action<string, string> error)
    {
        var formato = valores["formato_digital"];
        var camposDigitales = new[] { "isbn_digital", "precio_digital", "titular_digital", "licencia_digital", "vigente_hasta" };
        if (formato.Length == 0)
        {
            if (camposDigitales.Any(c => valores[c].Length > 0))
            {
                error("formato_digital", "digital-incompleta");
            }

            return null;
        }

        FormatoDeArchivoDigital? formatoLeido = formato switch
        {
            "epub" => FormatoDeArchivoDigital.Epub,
            _ => null,
        };
        if (formatoLeido is null)
        {
            error("formato_digital", "formato-invalido");
        }

        var isbn = LeerIsbn(valores, "isbn_digital", isbnVistos, error);
        var precio = LeerPrecio(valores, "precio_digital", PrecioDigitalMinimo, PrecioDigitalMaximo, error);
        var titular = valores["titular_digital"];
        if (titular.Length == 0)
        {
            error("titular_digital", "obligatorio");
        }
        else if (titular.Length > 200)
        {
            error("titular_digital", "demasiado-largo");
        }

        TipoDeLicenciaDeEdicion? licencia = valores["licencia_digital"] switch
        {
            "comercial" => TipoDeLicenciaDeEdicion.Comercial,
            "dominio-publico" => TipoDeLicenciaDeEdicion.DominioPublico,
            _ => null,
        };
        if (licencia is null)
        {
            error("licencia_digital", valores["licencia_digital"].Length == 0 ? "obligatorio" : "licencia-invalida");
        }

        DateOnly? vigencia = null;
        var textoDeVigencia = valores["vigente_hasta"];
        if (licencia == TipoDeLicenciaDeEdicion.Comercial)
        {
            if (!DateOnly.TryParseExact(textoDeVigencia, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
            {
                error("vigente_hasta", textoDeVigencia.Length == 0 ? "obligatorio" : "fecha-invalida");
            }
            else
            {
                vigencia = fecha;
            }
        }
        else if (licencia == TipoDeLicenciaDeEdicion.DominioPublico && textoDeVigencia.Length > 0)
        {
            error("vigente_hasta", "licencia-incoherente");
        }

        if (formatoLeido is null || isbn is null || precio is null || titular.Length is 0 or > 200 || licencia is null
            || (licencia == TipoDeLicenciaDeEdicion.Comercial && vigencia is null)
            || (licencia == TipoDeLicenciaDeEdicion.DominioPublico && textoDeVigencia.Length > 0))
        {
            return new DigitalDeLaFilaLeida(null);
        }

        return new DigitalDeLaFilaLeida(new DigitalDeLaFila(isbn, precio.Value, formatoLeido.Value, licencia.Value, titular, vigencia));
    }

    private static List<KeyValuePair<string, int>> LeerStock(List<string> columnas, Dictionary<string, string> valores, Action<string, string> error)
    {
        var stock = new List<KeyValuePair<string, int>>();
        foreach (var columna in columnas.Where(c => c.StartsWith("stock_", StringComparison.Ordinal)))
        {
            var texto = valores[columna];
            if (texto.Length == 0)
            {
                continue;
            }

            if (!int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var cantidad) || cantidad is < StockMinimo or > StockMaximo)
            {
                error(columna, "stock-fuera-de-rango");
                continue;
            }

            stock.Add(new KeyValuePair<string, int>(columna["stock_".Length..], cantidad));
        }

        return stock;
    }

    private static Isbn? LeerIsbn(Dictionary<string, string> valores, string campo, HashSet<string> isbnVistos, Action<string, string> error)
    {
        var texto = valores[campo];
        if (texto.Length == 0)
        {
            error(campo, "obligatorio");
            return null;
        }

        if (!Isbn.TryCrear(texto, out var isbn))
        {
            error(campo, "isbn-invalido");
            return null;
        }

        // La segunda aparición del mismo ISBN en el archivo es la que se rechaza (la primera se carga).
        if (!isbnVistos.Add(isbn.Isbn13))
        {
            error(campo, "isbn-repetido");
            return null;
        }

        return isbn;
    }

    private static decimal? LeerPrecio(Dictionary<string, string> valores, string campo, decimal minimo, decimal maximo, Action<string, string> error)
    {
        var texto = valores[campo];
        if (texto.Length == 0)
        {
            error(campo, "obligatorio");
            return null;
        }

        if (!PrecioConDosDecimales().IsMatch(texto) || !decimal.TryParse(texto, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var precio))
        {
            error(campo, "precio-invalido");
            return null;
        }

        if (precio < minimo || precio > maximo)
        {
            error(campo, "precio-fuera-de-rango");
            return null;
        }

        return precio;
    }

    private static void Texto(Dictionary<string, string> valores, string campo, int maximo, Action<string, string> error)
    {
        var texto = valores[campo];
        if (texto.Length == 0)
        {
            error(campo, "obligatorio");
        }
        else if (texto.Length > maximo)
        {
            error(campo, "demasiado-largo");
        }
    }

    /// <summary>Separa una línea por <c>;</c> con comillas dobles (<c>""</c> es una comilla); <see langword="null"/> si quedan comillas sin cerrar. Una celda se deja como texto: ningún valor se interpreta.</summary>
    private static List<string>? Separar(string linea)
    {
        var celdas = new List<string>();
        var actual = new StringBuilder();
        var entreComillas = false;
        for (var i = 0; i < linea.Length; i++)
        {
            var c = linea[i];
            if (entreComillas)
            {
                if (c == '"')
                {
                    if (i + 1 < linea.Length && linea[i + 1] == '"')
                    {
                        actual.Append('"');
                        i++;
                    }
                    else
                    {
                        entreComillas = false;
                    }
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

        if (entreComillas)
        {
            return null;
        }

        celdas.Add(actual.ToString());
        return celdas;
    }

    /// <summary>Un carácter de control (salvo el salto de línea y el retorno de carro de los finales de línea), de formato bidireccional, un separador de línea o de párrafo, el de sustitución de un UTF-8 mal leído o un sustituto suelto.</summary>
    private static bool TieneCaracterProhibido(string texto)
    {
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= texto.Length || !char.IsLowSurrogate(texto[i + 1]))
                {
                    return true;
                }

                i++;
                continue;
            }

            if (char.IsLowSurrogate(c))
            {
                return true;
            }

            if (c is '\n' or '\r')
            {
                continue;
            }

            if (char.IsControl(c) || c == '\uFFFD' || c is >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069' or '\u2028' or '\u2029' or '\u200E' or '\u200F' or '\u061C')
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex ClaveDeFila();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodigoDeSucursal();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex NombreDePortada();

    [GeneratedRegex("^[0-9]{1,6}(\\.[0-9]{1,2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex PrecioConDosDecimales();

    private sealed record DigitalDeLaFilaLeida(DigitalDeLaFila? Digital);
}
