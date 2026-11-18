using System.Net;
using System.Security.Cryptography;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Infrastructure.Catalogo;

/// <summary>
/// Dónde y con qué credencial el Catálogo guarda sus archivos: el almacén de objetos compatible con S3 (Oracle Object Storage en producción) con la credencial <c>catalogo-archivos</c> (crear, leer y borrar, sin sobrescribir ni listar) sobre los
/// contenedores <c>libros</c> y <c>portadas</c> (<c>infra/almacen/</c>, D08, D24).
/// </summary>
public sealed record OpcionesDelAlmacenDelCatalogo(
    Uri Endpoint,
    string Region,
    string ClaveDeAcceso,
    ValorSecreto ClaveSecreta,
    string ContenedorDeLibros = "libros",
    string ContenedorDePortadas = "portadas",
    bool RutaEnLaUrl = true)
{
    /// <summary>El cliente S3: plazo por solicitud de un minuto (una parte de 5 MiB), dos reintentos y la suma de verificación solo cuando el almacén la exige.</summary>
    public IAmazonS3 CrearCliente() =>
        new AmazonS3Client(
            new BasicAWSCredentials(ClaveDeAcceso, ClaveSecreta.Revelar()),
            new AmazonS3Config
            {
                ServiceURL = Endpoint.ToString(),
                ForcePathStyle = RutaEnLaUrl,
                AuthenticationRegion = Region,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
                MaxErrorRetry = 2,
                Timeout = TimeSpan.FromMinutes(1),
            });
}

/// <summary>
/// El almacén de archivos del Catálogo sobre S3 (T17, D14, regla 11): <b>nada pasa por el disco local ni se carga completo en memoria</b>. Verifica los primeros bytes <b>antes</b> de escribir (y las dimensiones de una imagen), cuenta el tope con los
/// bytes <b>realmente leídos</b>, calcula la huella SHA-256 durante la escritura (con un búfer de una parte de 5 MiB: <c>PutObject</c> si todo cabe, subida por partes si no) y, después, verifica la estructura del libro con
/// <see cref="VerificadorDeContenido"/> por lecturas por rango. Si rechaza, borra lo escrito. Cada archivo vive bajo una clave aleatoria que nunca se sobrescribe. Un fallo del almacén (red, 5xx, plazo) es
/// <see cref="AlmacenNoDisponibleException"/>; la cancelación del llamador se propaga.
/// </summary>
public sealed class S3AlmacenDeArchivos(IAmazonS3 almacen, string contenedorDeLibros = "libros", string contenedorDePortadas = "portadas") : IAlmacenDeArchivos
{
    /// <summary>El tamaño de una parte (el mínimo de S3 salvo la última) y del búfer de la subida.</summary>
    public const int TamanoDeParte = 5 * 1024 * 1024;

    private const int TamanoDeLectura = 81920;

    public async Task<ResultadoDeEscrituraEnAlmacen> Escribir(string claveEnAlmacen, FinalidadDeArchivo finalidad, Stream contenido, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        if (!ClaveEnAlmacen.EsValida(claveEnAlmacen))
        {
            throw new ArgumentException("La clave del almacén es de 32 hexadecimales aleatorios.", nameof(claveEnAlmacen));
        }

        var contenedor = ContenedorDe(finalidad);

        // 1. Antes de escribir: el tipo por los primeros bytes y, en una imagen, sus dimensiones por la cabecera.
        var prefijo = await LeerPrefijo(contenido, finalidad == FinalidadDeArchivo.Portada ? VerificadorDeContenido.PrefijoDeImagen : TiposDeArchivo.BytesDeFirma, cancelacion).ConfigureAwait(false);
        if (TiposDeArchivo.PorPrimerosBytes(prefijo) is not { } tipo || !TiposDeArchivo.EsAcordeALaFinalidad(tipo, finalidad))
        {
            return ResultadoDeEscrituraEnAlmacen.Rechazado(MotivoDeRechazoDeArchivo.TipoNoValido);
        }

        if (finalidad == FinalidadDeArchivo.Portada)
        {
            if (VerificadorDeContenido.DimensionesDeImagen(tipo, prefijo) is not { } dimensiones)
            {
                return ResultadoDeEscrituraEnAlmacen.Rechazado(MotivoDeRechazoDeArchivo.EstructuraInvalida);
            }

            if (!PoliticaDeArchivos.DimensionesValidas(dimensiones.Ancho, dimensiones.Alto))
            {
                return ResultadoDeEscrituraEnAlmacen.Rechazado(MotivoDeRechazoDeArchivo.DimensionesInvalidas);
            }
        }

        // 2. Durante: el tope por bytes leídos, la huella incremental y la escritura en streaming.
        var escrito = await Subir(contenedor, claveEnAlmacen, prefijo, contenido, finalidad, cancelacion).ConfigureAwait(false);
        if (escrito.Rechazo is { } rechazo)
        {
            return ResultadoDeEscrituraEnAlmacen.Rechazado(rechazo);
        }

        // 3. Después: la estructura del libro por lecturas por rango sobre el objeto ya escrito.
        var motivo = await Llamar(
            () => VerificadorDeContenido.VerificarEstructura(tipo, new LectorDeRangosS3(almacen, contenedor, claveEnAlmacen, escrito.Total), cancelacion),
            cancelacion).ConfigureAwait(false);
        if (motivo is { } invalido)
        {
            await Borrar(claveEnAlmacen, cancelacion).ConfigureAwait(false);
            return ResultadoDeEscrituraEnAlmacen.Rechazado(invalido);
        }

        return ResultadoDeEscrituraEnAlmacen.Correcto(new ArchivoEscrito(tipo, escrito.Total, HuellaDeArchivo.Desde(escrito.Huella)));
    }

    public async Task<Stream?> AbrirPortada(string claveEnAlmacen, CancellationToken cancelacion = default)
    {
        if (!ClaveEnAlmacen.EsValida(claveEnAlmacen))
        {
            return null;
        }

        try
        {
            var respuesta = await almacen.GetObjectAsync(new GetObjectRequest { BucketName = contenedorDePortadas, Key = claveEnAlmacen }, cancelacion).ConfigureAwait(false);
            return new FlujoDeRespuesta(respuesta);
        }
        catch (AmazonS3Exception error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception error) when (EsFalloDelAlmacen(error, cancelacion))
        {
            throw new AlmacenNoDisponibleException("El almacén de portadas no respondió.", error);
        }
    }

    /// <summary>Borra el objeto de la clave; como la clave es aleatoria y única, se borra en los dos contenedores (borrar uno que no existe no es un error).</summary>
    public async Task Borrar(string claveEnAlmacen, CancellationToken cancelacion = default)
    {
        if (!ClaveEnAlmacen.EsValida(claveEnAlmacen))
        {
            return;
        }

        foreach (var contenedor in new[] { contenedorDeLibros, contenedorDePortadas })
        {
            await Llamar(async () => await almacen.DeleteObjectAsync(new DeleteObjectRequest { BucketName = contenedor, Key = claveEnAlmacen }, cancelacion).ConfigureAwait(false), cancelacion).ConfigureAwait(false);
        }
    }

    private string ContenedorDe(FinalidadDeArchivo finalidad) => finalidad == FinalidadDeArchivo.Portada ? contenedorDePortadas : contenedorDeLibros;

    private static async Task<byte[]> LeerPrefijo(Stream contenido, int maximo, CancellationToken cancelacion)
    {
        var bufer = new byte[maximo];
        var leidos = 0;
        while (leidos < maximo)
        {
            var n = await contenido.ReadAsync(bufer.AsMemory(leidos, maximo - leidos), cancelacion).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }

            leidos += n;
        }

        return bufer.AsSpan(0, leidos).ToArray();
    }

    private async Task<(byte[] Huella, long Total, MotivoDeRechazoDeArchivo? Rechazo)> Subir(string contenedor, string clave, byte[] prefijo, Stream contenido, FinalidadDeArchivo finalidad, CancellationToken cancelacion)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var bufer = new byte[TamanoDeParte];
        var enElBufer = 0;
        long total = 0;
        string? subida = null;
        var partes = new List<PartETag>();

        try
        {
            // El prefijo ya leído va primero; después sigue el flujo. Exactamente el tope se acepta; un byte más se rechaza (se cuenta lo leído, no lo declarado).
            var pendiente = prefijo.AsMemory();
            while (true)
            {
                int leidos;
                if (!pendiente.IsEmpty)
                {
                    leidos = Math.Min(pendiente.Length, bufer.Length - enElBufer);
                    pendiente.Span[..leidos].CopyTo(bufer.AsSpan(enElBufer));
                    pendiente = pendiente[leidos..];
                }
                else
                {
                    leidos = await contenido.ReadAsync(bufer.AsMemory(enElBufer, Math.Min(TamanoDeLectura, bufer.Length - enElBufer)), cancelacion).ConfigureAwait(false);
                    if (leidos == 0)
                    {
                        break;
                    }
                }

                hash.AppendData(bufer.AsSpan(enElBufer, leidos));
                enElBufer += leidos;
                total += leidos;
                if (PoliticaDeArchivos.SuperaElTope(finalidad, total))
                {
                    await AbortarSiHaceFalta(contenedor, clave, subida).ConfigureAwait(false);
                    return ([], total, MotivoDeRechazoDeArchivo.DemasiadoGrande);
                }

                if (enElBufer == bufer.Length)
                {
                    subida ??= await Llamar(async () => (await almacen.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest { BucketName = contenedor, Key = clave }, cancelacion).ConfigureAwait(false)).UploadId, cancelacion).ConfigureAwait(false);
                    await SubirParte(contenedor, clave, subida, partes, bufer, enElBufer, cancelacion).ConfigureAwait(false);
                    enElBufer = 0;
                }
            }

            if (subida is null)
            {
                // Todo cupo en una parte: una sola escritura.
                await Llamar(
                    async () =>
                    {
                        using var flujo = new MemoryStream(bufer, 0, enElBufer, writable: false);
                        await almacen.PutObjectAsync(new PutObjectRequest { BucketName = contenedor, Key = clave, InputStream = flujo, AutoCloseStream = false, AutoResetStreamPosition = true }, cancelacion).ConfigureAwait(false);
                    },
                    cancelacion).ConfigureAwait(false);
            }
            else
            {
                if (enElBufer > 0)
                {
                    await SubirParte(contenedor, clave, subida, partes, bufer, enElBufer, cancelacion).ConfigureAwait(false);
                }

                await Llamar(
                    async () => await almacen.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest { BucketName = contenedor, Key = clave, UploadId = subida, PartETags = partes }, cancelacion).ConfigureAwait(false),
                    cancelacion).ConfigureAwait(false);
            }

            return (hash.GetHashAndReset(), total, null);
        }
        catch (Exception)
        {
            await AbortarSiHaceFalta(contenedor, clave, subida).ConfigureAwait(false);
            throw;
        }
    }

    private async Task SubirParte(string contenedor, string clave, string subida, List<PartETag> partes, byte[] bufer, int largo, CancellationToken cancelacion)
    {
        var numero = partes.Count + 1;
        var respuesta = await Llamar(
            async () =>
            {
                using var flujo = new MemoryStream(bufer, 0, largo, writable: false);
                return await almacen.UploadPartAsync(
                    new UploadPartRequest { BucketName = contenedor, Key = clave, UploadId = subida, PartNumber = numero, PartSize = largo, InputStream = flujo },
                    cancelacion).ConfigureAwait(false);
            },
            cancelacion).ConfigureAwait(false);
        partes.Add(new PartETag(numero, respuesta.ETag));
    }

    /// <summary>Aborta una subida por partes sin terminar (mejor esfuerzo: el ciclo de vida del contenedor aborta las que queden al día siguiente).</summary>
    private async Task AbortarSiHaceFalta(string contenedor, string clave, string? subida)
    {
        if (subida is null)
        {
            return;
        }

        try
        {
            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await almacen.AbortMultipartUploadAsync(new AbortMultipartUploadRequest { BucketName = contenedor, Key = clave, UploadId = subida }, limite.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Mejor esfuerzo.
        }
    }

    private static async Task<T> Llamar<T>(Func<Task<T>> llamada, CancellationToken cancelacion)
    {
        try
        {
            return await llamada().ConfigureAwait(false);
        }
        catch (Exception error) when (EsFalloDelAlmacen(error, cancelacion))
        {
            throw new AlmacenNoDisponibleException("El almacén de archivos no respondió.", error);
        }
    }

    private static async Task Llamar(Func<Task> llamada, CancellationToken cancelacion)
    {
        try
        {
            await llamada().ConfigureAwait(false);
        }
        catch (Exception error) when (EsFalloDelAlmacen(error, cancelacion))
        {
            throw new AlmacenNoDisponibleException("El almacén de archivos no respondió.", error);
        }
    }

    /// <summary>Un fallo del almacén: una respuesta de error de S3, un fallo de red o un plazo del propio cliente; la cancelación pedida por el llamador no lo es.</summary>
    private static bool EsFalloDelAlmacen(Exception error, CancellationToken cancelacion) =>
        error switch
        {
            AlmacenNoDisponibleException => false,
            OperationCanceledException => !cancelacion.IsCancellationRequested,
            AmazonServiceException or AmazonClientException or HttpRequestException or IOException => true,
            _ => false,
        };

    /// <summary>Lee rangos de un objeto del almacén; devuelve solo lo pedido y nunca más.</summary>
    private sealed class LectorDeRangosS3(IAmazonS3 almacen, string contenedor, string clave, long tamano) : ILectorDeRangos
    {
        public long Tamano => tamano;

        public async Task<byte[]> Leer(long desde, int cantidad, CancellationToken cancelacion = default)
        {
            if (desde < 0 || cantidad <= 0 || desde >= tamano)
            {
                return [];
            }

            var hasta = Math.Min(tamano, desde + cantidad) - 1;
            using var respuesta = await almacen.GetObjectAsync(new GetObjectRequest { BucketName = contenedor, Key = clave, ByteRange = new ByteRange(desde, hasta) }, cancelacion).ConfigureAwait(false);
            var esperado = (int)(hasta - desde + 1);
            var datos = new byte[esperado];
            var leidos = 0;
            while (leidos < esperado)
            {
                var n = await respuesta.ResponseStream.ReadAsync(datos.AsMemory(leidos), cancelacion).ConfigureAwait(false);
                if (n == 0)
                {
                    break;
                }

                leidos += n;
            }

            return leidos == esperado ? datos : datos.AsSpan(0, leidos).ToArray();
        }
    }

    /// <summary>La portada que se sirve: lee de la respuesta del almacén y la libera al cerrarse.</summary>
    private sealed class FlujoDeRespuesta(GetObjectResponse respuesta) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => respuesta.ResponseStream.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => respuesta.ResponseStream.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => respuesta.ResponseStream.ReadAsync(buffer, offset, count, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                respuesta.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
