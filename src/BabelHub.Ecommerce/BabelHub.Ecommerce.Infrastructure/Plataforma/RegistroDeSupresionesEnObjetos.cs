using System.Globalization;
using System.Net;
using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Plataforma;
using Microsoft.Extensions.Configuration;

namespace BabelHub.Ecommerce.Infrastructure.Plataforma;

/// <summary>
/// Dónde y con qué credencial se escribe el registro de supresiones: un almacén de objetos compatible con S3 (Oracle Object Storage en
/// producción), con la credencial <c>supresiones-registro</c>, que solo puede crear y leer en <c>entradas/</c> (D08, PLA-38).
/// </summary>
public sealed record OpcionesDelAlmacenDeObjetos(
    Uri Endpoint,
    string Region,
    string ClaveDeAcceso,
    ValorSecreto ClaveSecreta,
    string Contenedor = OpcionesDelAlmacenDeObjetos.ContenedorDeSupresiones,
    bool RutaEnLaUrl = true)
{
    /// <summary>
    /// El contenedor de las entradas del registro de supresiones, el único donde la credencial <c>supresiones-registro</c> tiene permiso
    /// (<c>infra/almacen/contenedores.txt</c> y <c>credenciales.txt</c>, D23) y el que leen los sellos y la copia del registro.
    /// </summary>
    public const string ContenedorDeSupresiones = "supresiones-entradas";

    /// <summary>
    /// Lee <c>BABELHUB__Almacen__Supresiones__Endpoint</c> (una URL https; http solo para un almacén local de desarrollo),
    /// <c>…__Region</c>, <c>…__ClaveDeAcceso</c> y <c>…__ClaveSecreta</c>, todas obligatorias. Las claves siguen las reglas de los demás
    /// secretos (largo, variedad); si algo falta o es débil falla al arrancar nombrando la variable, nunca el valor. Otro uso del mismo almacén (el catálogo, <c>Almacen:Catalogo:</c>) lo pide con su <paramref name="raiz"/>.
    /// </summary>
    public static OpcionesDelAlmacenDeObjetos Cargar(IConfiguration configuracion, bool permitirHttp = false, string raiz = "Almacen:Supresiones:")
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var lector = new LectorDeSecretos(configuracion);

        var endpoint = lector.Obligatorio(raiz + "Endpoint");
        var region = lector.Obligatorio(raiz + "Region");
        var acceso = lector.Obligatorio(raiz + "ClaveDeAcceso");
        var secreta = lector.Obligatorio(raiz + "ClaveSecreta");
        if (acceso is not null)
        {
            lector.Comprobar(raiz + "ClaveDeAcceso", acceso);
        }

        if (secreta is not null)
        {
            lector.Comprobar(raiz + "ClaveSecreta", secreta);
        }

        Uri? uri = null;
        if (endpoint is not null && !(Uri.TryCreate(endpoint, UriKind.Absolute, out uri)
                                      && (uri.Scheme == Uri.UriSchemeHttps || (permitirHttp && uri.Scheme == Uri.UriSchemeHttp))))
        {
            lector.Problema(raiz + "Endpoint", "no es una URL https válida");
        }

        lector.ExigirValida();
        return new OpcionesDelAlmacenDeObjetos(uri!, region!, acceso!, new ValorSecreto(secreta!));
    }
}

/// <summary>Crea el cliente S3 con los ajustes que necesitan los almacenes compatibles (suma de verificación solo cuando se exige, ruta en la URL).</summary>
public static class ClienteDelAlmacenDeObjetos
{
    public static IAmazonS3 Crear(OpcionesDelAlmacenDeObjetos opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        var configuracion = new AmazonS3Config
        {
            ServiceURL = opciones.Endpoint.ToString(),
            ForcePathStyle = opciones.RutaEnLaUrl,
            AuthenticationRegion = opciones.Region,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            MaxErrorRetry = 2,
            Timeout = TimeSpan.FromSeconds(10),
        };
        return new AmazonS3Client(new BasicAWSCredentials(opciones.ClaveDeAcceso, opciones.ClaveSecreta.Revelar()), configuracion);
    }
}

/// <summary>
/// El registro de supresiones sobre un almacén de objetos (PLA-37, PLA-38, D15): un objeto por entrada con la clave determinista
/// <c>entradas/&lt;identificador&gt;/&lt;tipo&gt;-&lt;fase&gt;.json</c> y el contenido <c>{identificadorOpaco, tipo, fase, fecha, suma}</c>,
/// sin datos personales. Solo <b>agrega</b>: lee antes de escribir, escribe con «crear solo si no existe» (<c>If-None-Match: *</c>) y la
/// política de la credencial impide sobrescribir y borrar; nunca emite <c>DELETE</c>, <c>COPY</c> ni una escritura sobre una clave que
/// ya existe. Un duplicado (también el simultáneo) devuelve la entrada original como éxito, sin cambiarla. Si el almacén no responde
/// lanza <see cref="RegistroDeSupresionesNoDisponibleException"/> y la supresión no se completa.
/// </summary>
public sealed class RegistroDeSupresionesEnObjetos(IAmazonS3 almacen, string contenedor = OpcionesDelAlmacenDeObjetos.ContenedorDeSupresiones) : IRegistroDeSupresiones
{
    private const string Prefijo = "entradas/";
    private const int ReintentosDeLectura = 3;
    private const int LecturasSimultaneas = 8;

    public async Task<EntradaDeSupresionDto> Agregar(string identificadorOpaco, ClaseDeSupresion clase, EtapaDeSupresion etapa, DateTimeOffset fecha, CancellationToken cancelacion = default)
    {
        var entrada = EntradaDeSupresion.Crear(identificadorOpaco, ATipo(clase), AFase(etapa), fecha);

        var existente = await Leer(entrada.Clave, cancelacion).ConfigureAwait(false);
        if (existente is not null)
        {
            return existente.Entrada with { YaExistia = true };
        }

        try
        {
            await almacen.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = contenedor,
                    Key = entrada.Clave,
                    ContentBody = Contenido(entrada),
                    ContentType = "application/json",
                    IfNoneMatch = "*",
                },
                cancelacion).ConfigureAwait(false);
            return new EntradaDeSupresionDto(entrada.IdentificadorOpaco, clase, etapa, entrada.Fecha, entrada.Suma, false);
        }
        catch (AmazonS3Exception error) when (error.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict or HttpStatusCode.Forbidden)
        {
            // Otra instancia la creó entre la lectura y la escritura (o la política impide pisar lo que existe): se lee la que quedó.
            for (var intento = 0; intento < ReintentosDeLectura; intento++)
            {
                var creada = await Leer(entrada.Clave, cancelacion).ConfigureAwait(false);
                if (creada is not null)
                {
                    return creada.Entrada with { YaExistia = true };
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50 * (intento + 1)), cancelacion).ConfigureAwait(false);
            }

            throw new RegistroDeSupresionesNoDisponibleException("El almacén rechazó la escritura de la entrada y no hay una entrada existente.", error);
        }
        catch (Exception error) when (EsFalloDelAlmacen(error, cancelacion))
        {
            throw new RegistroDeSupresionesNoDisponibleException("El almacén del registro de supresiones no respondió.", error);
        }
    }

    public async Task<EntradaDeSupresionDto?> Obtener(string identificadorOpaco, ClaseDeSupresion clase, EtapaDeSupresion etapa, CancellationToken cancelacion = default)
    {
        var entrada = EntradaDeSupresion.Crear(identificadorOpaco, ATipo(clase), AFase(etapa), DateTimeOffset.UnixEpoch);
        var leida = await Leer(entrada.Clave, cancelacion).ConfigureAwait(false);
        return leida?.Entrada;
    }

    public async Task<IReadOnlyList<EntradaLeidaDto>> LeerConMetadatos(CancellationToken cancelacion = default)
    {
        var objetos = new List<S3Object>();
        try
        {
            var solicitud = new ListObjectsV2Request { BucketName = contenedor, Prefix = Prefijo };
            ListObjectsV2Response respuesta;
            do
            {
                respuesta = await almacen.ListObjectsV2Async(solicitud, cancelacion).ConfigureAwait(false);
                objetos.AddRange(respuesta.S3Objects ?? []);
                solicitud.ContinuationToken = respuesta.NextContinuationToken;
            }
            while (respuesta.IsTruncated == true);
        }
        catch (Exception error) when (EsFalloDelAlmacen(error, cancelacion))
        {
            throw new RegistroDeSupresionesNoDisponibleException("No se pudo listar el registro de supresiones.", error);
        }

        using var cupo = new SemaphoreSlim(LecturasSimultaneas);
        var lecturas = objetos.Select(async objeto =>
        {
            await cupo.WaitAsync(cancelacion).ConfigureAwait(false);
            try
            {
                var leida = await Leer(objeto.Key, cancelacion, tolerarMalformada: true).ConfigureAwait(false);
                return leida is null
                    ? null // se borró entre el listado y la lectura: el sello lo detectará
                    : leida with { ModificadaEn = objeto.LastModified is { } m ? new DateTimeOffset(DateTime.SpecifyKind(m, DateTimeKind.Utc)) : leida.ModificadaEn };
            }
            finally
            {
                cupo.Release();
            }
        });

        return [.. (await Task.WhenAll(lecturas).ConfigureAwait(false)).Where(e => e is not null).Select(e => e!).OrderBy(e => e.ClaveEnElAlmacen, StringComparer.Ordinal)];
    }

    private async Task<EntradaLeidaDto?> Leer(string clave, CancellationToken cancelacion, bool tolerarMalformada = false)
    {
        try
        {
            using var respuesta = await almacen.GetObjectAsync(new GetObjectRequest { BucketName = contenedor, Key = clave }, cancelacion).ConfigureAwait(false);
            using var lector = new StreamReader(respuesta.ResponseStream);
            var texto = await lector.ReadToEndAsync(cancelacion).ConfigureAwait(false);
            var modificada = respuesta.LastModified is { } m ? new DateTimeOffset(DateTime.SpecifyKind(m, DateTimeKind.Utc)) : DateTimeOffset.UnixEpoch;
            return Interpretar(clave, texto, modificada, tolerarMalformada);
        }
        catch (AmazonS3Exception error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception error) when (EsFalloDelAlmacen(error, cancelacion))
        {
            throw new RegistroDeSupresionesNoDisponibleException("No se pudo leer una entrada del registro de supresiones.", error);
        }
    }

    /// <summary>
    /// El contenido de un objeto como entrada. Una entrada que no se puede interpretar es una entrada alterada: al verificar se
    /// devuelve con una suma vacía (que el verificador rechaza), y al leer una entrada concreta se informa como «no disponible».
    /// </summary>
    private static EntradaLeidaDto Interpretar(string clave, string texto, DateTimeOffset modificada, bool tolerarMalformada)
    {
        try
        {
            using var documento = JsonDocument.Parse(texto);
            var raiz = documento.RootElement;
            var tipo = ATipoDeNombre(raiz.GetProperty("tipo").GetString());
            var fase = ADeFaseNombre(raiz.GetProperty("fase").GetString());
            var fecha = DateTimeOffset.Parse(raiz.GetProperty("fecha").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
            var dto = new EntradaDeSupresionDto(
                raiz.GetProperty("identificadorOpaco").GetString()!,
                tipo == TipoDeSupresion.Cancelacion ? ClaseDeSupresion.Cancelacion : ClaseDeSupresion.Anonimizacion,
                fase == FaseDeSupresion.Intencion ? EtapaDeSupresion.Intencion : EtapaDeSupresion.Cierre,
                fecha,
                raiz.GetProperty("suma").GetString()!,
                false);
            return new EntradaLeidaDto(clave, dto, modificada);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        {
            if (!tolerarMalformada)
            {
                throw new RegistroDeSupresionesNoDisponibleException("La entrada del registro no se puede interpretar.", error);
            }

            var dto = new EntradaDeSupresionDto(string.Empty, ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Intencion, modificada, string.Empty, false);
            return new EntradaLeidaDto(clave, dto, modificada);
        }
    }

    private static string Contenido(EntradaDeSupresion entrada) =>
        JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["identificadorOpaco"] = entrada.IdentificadorOpaco,
            ["tipo"] = EntradaDeSupresion.NombreDe(entrada.Tipo),
            ["fase"] = EntradaDeSupresion.NombreDe(entrada.Fase),
            ["fecha"] = entrada.Fecha.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture),
            ["suma"] = entrada.Suma,
        });

    private static bool EsFalloDelAlmacen(Exception error, CancellationToken cancelacion) =>
        !cancelacion.IsCancellationRequested
        && error is AmazonServiceException or AmazonClientException or HttpRequestException or IOException or TaskCanceledException or TimeoutException;

    private static TipoDeSupresion ATipo(ClaseDeSupresion clase) => clase == ClaseDeSupresion.Cancelacion ? TipoDeSupresion.Cancelacion : TipoDeSupresion.Anonimizacion;

    private static FaseDeSupresion AFase(EtapaDeSupresion etapa) => etapa == EtapaDeSupresion.Intencion ? FaseDeSupresion.Intencion : FaseDeSupresion.Cierre;

    private static TipoDeSupresion ATipoDeNombre(string? nombre) => nombre switch
    {
        "cancelacion" => TipoDeSupresion.Cancelacion,
        "anonimizacion" => TipoDeSupresion.Anonimizacion,
        _ => throw new FormatException("tipo desconocido"),
    };

    private static FaseDeSupresion ADeFaseNombre(string? nombre) => nombre switch
    {
        "intencion" => FaseDeSupresion.Intencion,
        "cierre" => FaseDeSupresion.Cierre,
        _ => throw new FormatException("fase desconocida"),
    };
}
