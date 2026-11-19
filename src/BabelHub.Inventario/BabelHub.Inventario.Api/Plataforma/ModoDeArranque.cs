using System.Security.Cryptography;
using System.Text;
using BabelHub.Inventario.Infrastructure.Plataforma;
using Microsoft.Extensions.Hosting;

namespace BabelHub.Inventario.Api.Plataforma;

/// <summary>
/// Cómo arranca el servicio (PLA-19, PLA-39, PLA-43; plan §5.1): <see cref="Normal"/>; <see cref="Restauracion"/>, que ejecuta solo los
/// pasos de restauración antes de abrir y sin trabajos de fondo ni adaptadores salientes hacia proveedores; y <see cref="Ensayo"/>, con
/// adaptadores nulos y los trabajos de fondo apagados. Se elige con <c>BABELHUB__Modo</c>.
/// </summary>
public enum ModoDeArranque
{
    Normal,
    Restauracion,
    Ensayo,
}

/// <summary>Se rechaza el arranque: el mensaje nombra la variable y el motivo (enumerado), nunca un valor secreto.</summary>
public sealed class ArranqueRechazadoException : Exception
{
    public ArranqueRechazadoException(string message)
        : base(message)
    {
    }

    public ArranqueRechazadoException()
        : base("Arranque rechazado.")
    {
    }

    public ArranqueRechazadoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public enum ResultadoDeMarca
{
    Valida,
    Ausente,
    Malformada,
    FirmaInvalida,
    Vencida,
    DeOtroModo,
    Reutilizada,
}

/// <summary>Un adaptador saliente registrado en el servicio: a qué sirve y si es el adaptador nulo (que no envía nada).</summary>
public interface IAdaptadorSaliente
{
    string Nombre { get; }

    /// <summary>Correo, notificador, pasarela de pago, logística o asistente. El cliente del otro servicio no es «hacia un proveedor».</summary>
    bool EsHaciaProveedor { get; }

    bool EsNulo { get; }
}

/// <summary>Guarda los nonce de las marcas ya usadas: una marca vale una sola vez, también tras reiniciar el proceso.</summary>
public interface IAlmacenDeNoncesDeMarcas
{
    /// <summary>Consume el nonce de forma atómica: verdadero si no se había usado, falso si ya se usó.</summary>
    bool Consumir(string nonce);
}

/// <summary>Los nonce usados como archivos vacíos creados con «crear solo si no existe»: atómico entre procesos y sobrevive a un reinicio.</summary>
public sealed class AlmacenDeNoncesEnArchivos(string carpeta) : IAlmacenDeNoncesDeMarcas
{
    public bool Consumir(string nonce)
    {
        ArgumentException.ThrowIfNullOrEmpty(nonce);
        Directory.CreateDirectory(carpeta);
        var ruta = Path.Combine(carpeta, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nonce))).ToLowerInvariant());
        try
        {
            using var archivo = new FileStream(ruta, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            return true;
        }
        catch (IOException) when (File.Exists(ruta))
        {
            return false;
        }
    }
}

/// <summary>
/// La marca de un solo uso que <c>restaurar.sh</c> emite para arrancar en producción un modo que no es el normal: <c>v1.&lt;modo&gt;.&lt;instante
/// Unix en segundos&gt;.&lt;nonce&gt;.&lt;HMAC-SHA256 en hexadecimal minúscula&gt;</c>, con el HMAC sobre <c>v1.&lt;modo&gt;.&lt;instante&gt;.&lt;nonce&gt;</c> y un secreto que
/// solo conocen el script y el servicio. Vale pocos minutos y una sola vez (el nonce se consume).
/// </summary>
public static class MarcaDeRestauracion
{
    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(5);

    /// <summary>Tolerancia al reloj de la otra máquina para una marca emitida «en el futuro».</summary>
    public static readonly TimeSpan Tolerancia = TimeSpan.FromSeconds(60);

    public static string Emitir(byte[] secreto, ModoDeArranque modo, DateTimeOffset ahora, string nonce)
    {
        ArgumentNullException.ThrowIfNull(secreto);
        var cuerpo = $"v1.{modo}.{ahora.ToUnixTimeSeconds()}.{nonce}";
        return $"{cuerpo}.{Firmar(secreto, cuerpo)}";
    }

    public static ResultadoDeMarca Verificar(string? marca, byte[] secreto, ModoDeArranque modo, DateTimeOffset ahora, IAlmacenDeNoncesDeMarcas almacen)
    {
        ArgumentNullException.ThrowIfNull(secreto);
        ArgumentNullException.ThrowIfNull(almacen);
        if (string.IsNullOrWhiteSpace(marca))
        {
            return ResultadoDeMarca.Ausente;
        }

        var partes = marca.Split('.');
        if (partes.Length != 5 || partes[0] != "v1" || !long.TryParse(partes[2], out var segundos) || partes[3].Length is < 16 or > 64 || partes[4].Length != 64)
        {
            return ResultadoDeMarca.Malformada;
        }

        var cuerpo = string.Join('.', partes[..4]);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Firmar(secreto, cuerpo)), Encoding.ASCII.GetBytes(partes[4])))
        {
            return ResultadoDeMarca.FirmaInvalida;
        }

        if (!string.Equals(partes[1], modo.ToString(), StringComparison.Ordinal))
        {
            return ResultadoDeMarca.DeOtroModo;
        }

        var emitida = DateTimeOffset.FromUnixTimeSeconds(segundos);
        if (ahora - emitida > Vigencia || emitida - ahora > Tolerancia)
        {
            return ResultadoDeMarca.Vencida;
        }

        // Lo último: solo una marca válida y vigente gasta su nonce.
        return almacen.Consumir(partes[3]) ? ResultadoDeMarca.Valida : ResultadoDeMarca.Reutilizada;
    }

    private static string Firmar(byte[] secreto, string cuerpo) => Convert.ToHexString(HMACSHA256.HashData(secreto, Encoding.UTF8.GetBytes(cuerpo))).ToLowerInvariant();
}

/// <summary>
/// Decide con qué modo arranca el servicio y lo rechaza si no corresponde: en producción un modo distinto del normal exige la marca de
/// un solo uso de <c>restaurar.sh</c>; en el ensayo no puede haber credenciales de producción; y los adaptadores salientes deben
/// corresponder al modo (nulos en el ensayo y sin proveedores al restaurar; reales en producción normal).
/// </summary>
public static class ArranqueSeguro
{
    public const string VariableDelModo = "BABELHUB__Modo";
    public const string VariableDeLaMarca = "BABELHUB__Restauracion__Marca";
    public const string VariableDelSecretoDeLaMarca = "BABELHUB__Restauracion__SecretoDeLaMarca";
    public const string VariableDeLosProhibidos = "BABELHUB__Ensayo__ProhibidosSha256";

    private static readonly string[] NombresDeSecretos = ["secreto", "clave", "credencial", "contrasena", "password", "token", "marca", "base", "pimienta"];

    /// <summary>Lee y valida el modo. Lanza <see cref="ArranqueRechazadoException"/> si el modo es inválido o si en producción falta la marca.</summary>
    public static ModoDeArranque Resolver(IConfiguration configuracion, bool esProduccion, DateTimeOffset ahora, IAlmacenDeNoncesDeMarcas almacen)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        ArgumentNullException.ThrowIfNull(almacen);

        var texto = configuracion["Modo"];
        var modo = string.IsNullOrWhiteSpace(texto)
            ? ModoDeArranque.Normal
            : Enum.GetNames<ModoDeArranque>().Contains(texto, StringComparer.Ordinal)
                ? Enum.Parse<ModoDeArranque>(texto)
                : throw new ArranqueRechazadoException($"{VariableDelModo}: el modo no es válido (Normal, Restauracion o Ensayo).");

        if (modo == ModoDeArranque.Normal)
        {
            return modo;
        }

        if (esProduccion)
        {
            ExigirMarca(configuracion, modo, ahora, almacen);
        }

        if (modo == ModoDeArranque.Ensayo)
        {
            ExigirSinCredencialesDeProduccion(configuracion);
        }

        return modo;
    }

    /// <summary>Los adaptadores salientes deben corresponder al modo (la negativa a arrancar si el modo no coincide con el notificador).</summary>
    public static void ExigirCoherencia(ModoDeArranque modo, bool esProduccion, IEnumerable<IAdaptadorSaliente> adaptadores)
    {
        ArgumentNullException.ThrowIfNull(adaptadores);
        foreach (var adaptador in adaptadores.Where(a => a.EsHaciaProveedor))
        {
            if (modo != ModoDeArranque.Normal && !adaptador.EsNulo)
            {
                throw new ArranqueRechazadoException($"El adaptador «{adaptador.Nombre}» no es nulo y el modo {modo} no admite comunicarse con proveedores.");
            }

            if (modo == ModoDeArranque.Normal && esProduccion && adaptador.EsNulo)
            {
                throw new ArranqueRechazadoException($"El adaptador «{adaptador.Nombre}» es nulo y el modo Normal de producción necesita el real.");
            }
        }
    }

    private static void ExigirMarca(IConfiguration configuracion, ModoDeArranque modo, DateTimeOffset ahora, IAlmacenDeNoncesDeMarcas almacen)
    {
        var lector = new LectorDeSecretos(configuracion);
        var secreto = lector.Obligatorio("Restauracion:SecretoDeLaMarca");
        if (secreto is not null)
        {
            lector.Comprobar("Restauracion:SecretoDeLaMarca", secreto);
        }

        if (lector.Problemas.Count > 0 || secreto is null)
        {
            throw new ArranqueRechazadoException($"El modo {modo} en producción exige la marca de restaurar.sh: {string.Join("; ", lector.Problemas)}");
        }

        var resultado = MarcaDeRestauracion.Verificar(configuracion["Restauracion:Marca"], Encoding.UTF8.GetBytes(secreto), modo, ahora, almacen);
        if (resultado != ResultadoDeMarca.Valida)
        {
            throw new ArranqueRechazadoException($"{VariableDeLaMarca}: la marca no sirve ({resultado}); el modo {modo} en producción solo arranca desde restaurar.sh.");
        }
    }

    /// <summary>El ensayo usa secretos propios: ninguna variable de secreto puede coincidir con la huella de uno de producción.</summary>
    private static void ExigirSinCredencialesDeProduccion(IConfiguration configuracion)
    {
        var prohibidos = (configuracion["Ensayo:ProhibidosSha256"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(h => h.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        if (prohibidos.Count == 0)
        {
            throw new ArranqueRechazadoException($"{VariableDeLosProhibidos}: el ensayo exige la lista de huellas de las credenciales de producción para negarse a arrancar con ellas.");
        }

        var coincidencias = configuracion.AsEnumerable()
            .Where(p => !string.IsNullOrEmpty(p.Value) && NombresDeSecretos.Any(n => p.Key.Contains(n, StringComparison.OrdinalIgnoreCase)))
            .Where(p => prohibidos.Contains(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p.Value!))).ToLowerInvariant()))
            .Select(p => LectorDeSecretos.Variable(p.Key))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (coincidencias.Count > 0)
        {
            throw new ArranqueRechazadoException($"El ensayo no arranca con credenciales de producción: {string.Join(", ", coincidencias)}.");
        }
    }
}

public static class ModoDeArranqueExtensiones
{
    /// <summary>Los trabajos de fondo (un <c>BackgroundService</c>) solo corren en el modo normal: ni al restaurar ni en el ensayo.</summary>
    public static IServiceCollection AgregarTrabajoDeFondo<T>(this IServiceCollection servicios, ModoDeArranque modo)
        where T : class, IHostedService
    {
        ArgumentNullException.ThrowIfNull(servicios);
        if (modo == ModoDeArranque.Normal)
        {
            servicios.AddHostedService<T>();
        }

        return servicios;
    }
}
