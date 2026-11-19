using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Domain.Identidad;
using Konscious.Security.Cryptography;
using DominioProposito = BabelHub.Ecommerce.Domain.Identidad.PropositoDeCodigo;

namespace BabelHub.Ecommerce.Infrastructure.Identidad;

/// <summary>
/// Una clave secreta del entorno tal como llega (<c>versión:clave en base64</c>, de al menos 32 bytes): la versión son 1 a 16 letras o dígitos. Nunca se imprime ni se serializa.
/// </summary>
public sealed partial class ClaveVersionadaDeIdentidad
{
    public const int LongitudMinima = 32;

    private ClaveVersionadaDeIdentidad(string version, byte[] clave)
    {
        Version = version;
        Clave = clave;
    }

    public string Version { get; }

    internal byte[] Clave { get; }

    public static ClaveVersionadaDeIdentidad Analizar(string? valor, string nombre)
    {
        var separador = valor?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        if (valor is null || separador <= 0)
        {
            throw new ArgumentException("La clave tiene el formato «versión:clave en base64».", nombre);
        }

        var version = valor[..separador];
        if (!FormaDeLaVersion().IsMatch(version))
        {
            throw new ArgumentException("La versión de la clave son 1 a 16 letras o dígitos.", nombre);
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(valor[(separador + 1)..]);
        }
        catch (FormatException)
        {
            throw new ArgumentException("La clave no es base64 válido.", nombre);
        }

        return bytes.Length < LongitudMinima
            ? throw new ArgumentException($"La clave debe tener al menos {LongitudMinima} bytes.", nombre)
            : new ClaveVersionadaDeIdentidad(version, bytes);
    }

    public override string ToString() => "[clave]";

    [GeneratedRegex("^[A-Za-z0-9]{1,16}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex FormaDeLaVersion();
}

/// <summary>
/// Argon2id (T31, D03): <b>19 MiB, 2 iteraciones, 1 hilo</b>, sal de 16 bytes y resumen de 32, en formato PHC. Un <b>semáforo global</b> acota los cálculos simultáneos (la VM de 6 GB se comparte con el resto del sistema): si no
/// se consigue turno en el tiempo de espera, lanza <see cref="CalculoDeContrasenasSaturadoException"/> (la Api responde 503) y <b>nunca</b> cae a un cálculo más barato. Verificar un resumen ausente o ilegible hace el mismo
/// cálculo con una sal fija y devuelve falso, de modo que el tiempo no revela si la cuenta existe (IDE-03, IDE-65). La comparación es en tiempo constante.
/// </summary>
public sealed class Argon2idHasherDeContrasenas : IHasherDeContrasenas, IDisposable
{
    public const int ConcurrenciaPorDefecto = 4;

    public static readonly TimeSpan EsperaPorDefecto = TimeSpan.FromSeconds(3);

    private static readonly byte[] SalDelSenuelo = Encoding.ASCII.GetBytes("senuelo-babelhub");

    private readonly SemaphoreSlim _turnos;
    private readonly TimeSpan _espera;

    public Argon2idHasherDeContrasenas(int concurrenciaMaxima = ConcurrenciaPorDefecto, TimeSpan? espera = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrenciaMaxima, 1);
        _turnos = new SemaphoreSlim(concurrenciaMaxima, concurrenciaMaxima);
        _espera = espera ?? EsperaPorDefecto;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_espera, TimeSpan.Zero);
    }

    /// <summary>Los turnos libres ahora: para las pruebas y las métricas.</summary>
    public int TurnosLibres => _turnos.CurrentCount;

    public async Task<string> Resumir(Contrasena contrasena, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(contrasena);
        var sal = RandomNumberGenerator.GetBytes(ParametrosDelResumen.BytesDeSal);
        var resumen = await Calcular(contrasena.Revelar(), sal, ParametrosDelResumen.MemoriaEnKiB, ParametrosDelResumen.Iteraciones, ParametrosDelResumen.Paralelismo, cancelacion).ConfigureAwait(false);
        return $"$argon2id$v=19$m={ParametrosDelResumen.MemoriaEnKiB},t={ParametrosDelResumen.Iteraciones},p={ParametrosDelResumen.Paralelismo}${Base64SinRelleno(sal)}${Base64SinRelleno(resumen)}";
    }

    public async Task<bool> Verificar(string? resumen, Contrasena presentada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(presentada);
        var leido = ResumenDeContrasena.Leer(resumen);
        if (leido is null || !TryDecodificar(leido.Sal, out var sal) || !TryDecodificar(LeerElResumen(leido.Valor), out var esperado) || esperado.Length != ParametrosDelResumen.BytesDelResumen)
        {
            // Cuenta inexistente, sin contraseña o resumen ilegible: mismo costo, sin acertar nunca.
            await Calcular(presentada.Revelar(), SalDelSenuelo, ParametrosDelResumen.MemoriaEnKiB, ParametrosDelResumen.Iteraciones, ParametrosDelResumen.Paralelismo, cancelacion).ConfigureAwait(false);
            return false;
        }

        var calculado = await Calcular(presentada.Revelar(), sal, leido.Memoria, leido.Iteraciones, leido.Paralelismo, cancelacion).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }

    public void Dispose() => _turnos.Dispose();

    private async Task<byte[]> Calcular(string contrasena, byte[] sal, int memoriaEnKiB, int iteraciones, int paralelismo, CancellationToken cancelacion)
    {
        if (!await _turnos.WaitAsync(_espera, cancelacion).ConfigureAwait(false))
        {
            throw new CalculoDeContrasenasSaturadoException();
        }

        try
        {
            return await Task.Run(
                () =>
                {
                    var bytes = Encoding.UTF8.GetBytes(contrasena);
                    try
                    {
                        using var argon = new Argon2id(bytes) { Salt = sal, MemorySize = memoriaEnKiB, Iterations = iteraciones, DegreeOfParallelism = paralelismo };
                        return argon.GetBytes(ParametrosDelResumen.BytesDelResumen);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(bytes);
                    }
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _turnos.Release();
        }
    }

    private static string LeerElResumen(string phc) => phc[(phc.LastIndexOf('$') + 1)..];

    private static string Base64SinRelleno(byte[] datos) => Convert.ToBase64String(datos).TrimEnd('=');

    private static bool TryDecodificar(string texto, out byte[] bytes)
    {
        bytes = [];
        var relleno = texto.PadRight(texto.Length + ((4 - (texto.Length % 4)) % 4), '=');
        try
        {
            bytes = Convert.FromBase64String(relleno);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// La lista local de contraseñas comunes o filtradas (IDE-59): un recurso incrustado, una por línea, en minúsculas y de 10 o más caracteres (la longitud mínima de la política); más de 10 000. Se carga una vez.
/// </summary>
public sealed class RecursoListaDeContrasenasComunes : IListaDeContrasenasComunes
{
    public const string NombreDelRecurso = "BabelHub.Ecommerce.Identidad.contrasenas-comunes.txt";

    private readonly HashSet<string> _comunes;

    public RecursoListaDeContrasenasComunes()
        : this(Cargar())
    {
    }

    public RecursoListaDeContrasenasComunes(IEnumerable<string> comunes)
    {
        ArgumentNullException.ThrowIfNull(comunes);
        _comunes = new HashSet<string>(comunes.Select(c => c.Trim().ToLowerInvariant()).Where(c => c.Length > 0), StringComparer.Ordinal);
    }

    public int Cantidad => _comunes.Count;

    public bool EsComun(string normalizada) => normalizada is not null && _comunes.Contains(normalizada);

    private static List<string> Cargar()
    {
        using var flujo = typeof(RecursoListaDeContrasenasComunes).Assembly.GetManifestResourceStream(NombreDelRecurso)
            ?? throw new InvalidOperationException("Falta el recurso con la lista de contraseñas comunes.");
        using var lector = new StreamReader(flujo, Encoding.UTF8);
        var lineas = new List<string>();
        while (lector.ReadLine() is { } linea)
        {
            lineas.Add(linea);
        }

        return lineas;
    }
}

/// <summary>
/// El cifrado de los secretos del segundo factor (T31, D04): AES-256-GCM con un nonce aleatorio de 12 bytes por cifrado, una clave <b>versionada y distinta por propósito</b> (HKDF-SHA256 sobre la clave maestra del entorno,
/// con el propósito como información) y el identificador del usuario como datos asociados, de modo que un secreto copiado a otra cuenta no descifra. El resultado es <c>[largo de la versión][versión][nonce][etiqueta][cifrado]</c>.
/// Al rotar la clave maestra, las anteriores siguen sirviendo <b>solo para descifrar</b>; todo cifrado nuevo usa la actual. Una alteración lanza.
/// </summary>
public sealed class AesGcmCifradorDeSecretos : ICifradorDeSecretos
{
    public const string PropositoDelSegundoFactor = "segundo-factor";

    public const string PropositoDeLaClaveDeFirma = "clave-de-firma";

    private const int BytesDelNonce = 12;

    private const int BytesDeLaEtiqueta = 16;

    private readonly ClaveVersionadaDeIdentidad _actual;
    private readonly Dictionary<string, ClaveVersionadaDeIdentidad> _porVersion;

    public AesGcmCifradorDeSecretos(ClaveVersionadaDeIdentidad actual, IEnumerable<ClaveVersionadaDeIdentidad>? anteriores = null)
    {
        ArgumentNullException.ThrowIfNull(actual);
        _actual = actual;
        _porVersion = new Dictionary<string, ClaveVersionadaDeIdentidad>(StringComparer.Ordinal) { [actual.Version] = actual };
        foreach (var anterior in anteriores ?? [])
        {
            if (!_porVersion.TryAdd(anterior.Version, anterior))
            {
                throw new ArgumentException("Cada clave del cifrado tiene una versión distinta.", nameof(anteriores));
            }

            if (CryptographicOperations.FixedTimeEquals(anterior.Clave, actual.Clave))
            {
                throw new ArgumentException("Una clave anterior no puede ser igual a la actual.", nameof(anteriores));
            }
        }
    }

    public byte[] Cifrar(ReadOnlySpan<byte> secreto, Guid usuarioId) => Cifrar(PropositoDelSegundoFactor, usuarioId.ToString("N"), secreto);

    /// <summary>Cifra para un propósito (con su propia clave derivada) y un contexto que viaja como dato asociado: una clave de firma privada se liga a su <c>kid</c>.</summary>
    public byte[] Cifrar(string proposito, string contexto, ReadOnlySpan<byte> secreto)
    {
        var version = Encoding.ASCII.GetBytes(_actual.Version);
        var nonce = RandomNumberGenerator.GetBytes(BytesDelNonce);
        var cifrado = new byte[secreto.Length];
        var etiqueta = new byte[BytesDeLaEtiqueta];
        var clave = DerivarLaClave(_actual, proposito);
        try
        {
            using var aes = new AesGcm(clave, BytesDeLaEtiqueta);
            aes.Encrypt(nonce, secreto, cifrado, etiqueta, DatosAsociados(proposito, contexto, version));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clave);
        }

        var resultado = new byte[1 + version.Length + BytesDelNonce + BytesDeLaEtiqueta + cifrado.Length];
        resultado[0] = (byte)version.Length;
        version.CopyTo(resultado, 1);
        nonce.CopyTo(resultado, 1 + version.Length);
        etiqueta.CopyTo(resultado, 1 + version.Length + BytesDelNonce);
        cifrado.CopyTo(resultado, 1 + version.Length + BytesDelNonce + BytesDeLaEtiqueta);
        return resultado;
    }

    public byte[] Descifrar(ReadOnlySpan<byte> cifrado, Guid usuarioId) => Descifrar(PropositoDelSegundoFactor, usuarioId.ToString("N"), cifrado);

    public byte[] Descifrar(string proposito, string contexto, ReadOnlySpan<byte> cifrado)
    {
        if (cifrado.Length < 2 || cifrado[0] is 0 or > 16 || cifrado.Length < 1 + cifrado[0] + BytesDelNonce + BytesDeLaEtiqueta)
        {
            throw new CryptographicException("El secreto cifrado no tiene un formato válido.");
        }

        int largoDeLaVersion = cifrado[0];
        var version = cifrado.Slice(1, largoDeLaVersion);
        if (!_porVersion.TryGetValue(Encoding.ASCII.GetString(version), out var maestra))
        {
            throw new CryptographicException("El secreto cifrado usa una versión de clave desconocida.");
        }

        var desplazamiento = 1 + largoDeLaVersion;
        var nonce = cifrado.Slice(desplazamiento, BytesDelNonce);
        var etiqueta = cifrado.Slice(desplazamiento + BytesDelNonce, BytesDeLaEtiqueta);
        var texto = cifrado[(desplazamiento + BytesDelNonce + BytesDeLaEtiqueta)..];
        var plano = new byte[texto.Length];
        var clave = DerivarLaClave(maestra, proposito);
        try
        {
            using var aes = new AesGcm(clave, BytesDeLaEtiqueta);
            aes.Decrypt(nonce, texto, etiqueta, plano, DatosAsociados(proposito, contexto, version.ToArray()));
            return plano;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plano);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clave);
        }
    }

    private static byte[] DerivarLaClave(ClaveVersionadaDeIdentidad maestra, string proposito) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, maestra.Clave, 32, salt: null, info: Encoding.ASCII.GetBytes($"babelhub/identidad/{proposito}"));

    private static byte[] DatosAsociados(string proposito, string contexto, byte[] version) =>
        Encoding.UTF8.GetBytes($"babelhub/identidad/{proposito}/{contexto}/{Encoding.ASCII.GetString(version)}");
}

/// <summary>
/// Las huellas con clave de lo que no se guarda en claro (T31, IDE-40): HMAC-SHA256 con la pimienta del entorno y el propósito dentro del mensaje (un código de verificación no sirve para recuperar el acceso ni uno de respaldo
/// para nada más); los valores opacos de renovaciones y desafíos (256 bits aleatorios) solo necesitan SHA-256. Salida en 64 caracteres hexadecimales. Rotar la pimienta invalida los códigos vigentes (10 minutos) y obliga a
/// regenerar los códigos de respaldo.
/// </summary>
public sealed class HmacHuellasDeIdentidad : IHuellasDeIdentidad
{
    private readonly byte[] _pimienta;

    public HmacHuellasDeIdentidad(ClaveVersionadaDeIdentidad pimienta)
    {
        ArgumentNullException.ThrowIfNull(pimienta);
        _pimienta = pimienta.Clave;
    }

    public string DeCodigo(Guid usuarioId, DominioProposito proposito, string codigo) =>
        Hmac(proposito switch
        {
            DominioProposito.VerificacionDeCorreo => "codigo-de-verificacion",
            DominioProposito.RecuperacionDeAcceso => "codigo-de-recuperacion",
            _ => throw new ArgumentOutOfRangeException(nameof(proposito), proposito, "Propósito desconocido."),
        }, usuarioId, codigo);

    public string DeCodigoDeRespaldo(Guid usuarioId, string codigoCanonico) => Hmac("codigo-de-respaldo", usuarioId, codigoCanonico);

    public string DeValorOpaco(string valor)
    {
        ArgumentException.ThrowIfNullOrEmpty(valor);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));
    }

    private string Hmac(string dominio, Guid usuarioId, string valor)
    {
        ArgumentNullException.ThrowIfNull(valor);
        var mensaje = Encoding.UTF8.GetBytes($"{dominio}\0{usuarioId:N}\0{valor}");
        return Convert.ToHexStringLower(HMACSHA256.HashData(_pimienta, mensaje));
    }
}

/// <summary>Los aleatorios criptográficos del módulo (T31): todos de un CSPRNG.</summary>
public sealed class CsprngGeneradorDeSecretos : IGeneradorDeSecretos
{
    /// <summary>Sin caracteres ambiguos (0, O, I, l, 1): la contraseña temporal se dicta o se copia a mano.</summary>
    private const string AlfabetoDeLaContrasenaTemporal = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    public const int LongitudDeLaContrasenaTemporal = 24;

    public Guid NuevoIdentificador() => Guid.NewGuid();

    public string ValorOpaco() => Base64Url.Codificar(RandomNumberGenerator.GetBytes(32));

    public byte[] SecretoDelSegundoFactor() => RandomNumberGenerator.GetBytes(20);

    public string ContrasenaTemporal()
    {
        var caracteres = new char[LongitudDeLaContrasenaTemporal];
        for (var i = 0; i < caracteres.Length; i++)
        {
            caracteres[i] = AlfabetoDeLaContrasenaTemporal[RandomNumberGenerator.GetInt32(AlfabetoDeLaContrasenaTemporal.Length)];
        }

        return new string(caracteres);
    }
}
