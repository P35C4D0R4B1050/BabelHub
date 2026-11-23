using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Catalogo;

namespace BabelHub.Ecommerce.Infrastructure.Catalogo;

/// <summary>
/// Las portadas de la carga inicial leídas de una carpeta (CIN-05, CIN-09, plan §6): solo un nombre de archivo simple (letras, dígitos, punto, guion y guion bajo; sin separadores, <c>..</c>, ruta absoluta, unidad, flujo alterno de NTFS ni nombre
/// reservado de Windows), resuelto de forma canónica y contenido en la carpeta, que sea un archivo regular (ni carpeta, ni dispositivo, ni enlace simbólico ni punto de reanálisis) y abierto de solo lectura. El flujo devuelto se acota a 2 MB más un byte:
/// el tope y el tipo (por contenido, nunca por el nombre) los decide <c>SubirArchivo</c> (CAT-16). Un vínculo duro no se detecta aquí (la carpeta la controla la persona operadora).
/// </summary>
public sealed partial class CarpetaDePortadasDeLaCarga : IOrigenDeArchivosDeLaCarga
{
    /// <summary>2 MB más un byte: lo justo para que el caso de uso de subida vea que se pasó del tope sin leer un archivo grande entero.</summary>
    public const long LecturaMaxima = (2L * 1024 * 1024) + 1;

    private static readonly HashSet<string> NombresReservados = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private readonly string _raiz;

    public CarpetaDePortadasDeLaCarga(string carpeta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carpeta);
        _raiz = Path.TrimEndingDirectorySeparator(Path.GetFullPath(carpeta));
    }

    public Task<Stream?> AbrirPortada(string nombre, CancellationToken cancelacion = default)
    {
        cancelacion.ThrowIfCancellationRequested();
        return Task.FromResult(Abrir(nombre));
    }

    private Stream? Abrir(string? nombre)
    {
        if (!NombreSimple(nombre))
        {
            return null;
        }

        try
        {
            var directorio = new DirectoryInfo(_raiz);
            if (!directorio.Exists)
            {
                return null;
            }

            var ruta = Path.GetFullPath(Path.Combine(_raiz, nombre!));
            var comparacion = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(Path.GetDirectoryName(ruta), _raiz, comparacion))
            {
                return null;
            }

            var archivo = new FileInfo(ruta);
            if (!archivo.Exists || archivo.LinkTarget is not null || (archivo.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)) != 0)
            {
                return null;
            }

            // Abierto de solo lectura y comprobado otra vez sobre lo abierto: si el archivo cambió entre la comprobación y la apertura, no se entrega.
            var flujo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var abierto = new FileInfo(ruta);
            if (!flujo.CanRead || abierto.LinkTarget is not null || (abierto.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                flujo.Dispose();
                return null;
            }

            return new FlujoAcotado(flujo, LecturaMaxima);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static bool NombreSimple(string? nombre)
    {
        if (string.IsNullOrEmpty(nombre) || nombre.Length > 100 || !NombrePermitido().IsMatch(nombre) || nombre.Contains("..", StringComparison.Ordinal) || nombre.EndsWith('.'))
        {
            return false;
        }

        var sinExtension = nombre.Contains('.', StringComparison.Ordinal) ? nombre[..nombre.IndexOf('.', StringComparison.Ordinal)] : nombre;
        return !NombresReservados.Contains(sinExtension);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NombrePermitido();

    /// <summary>Un flujo de solo lectura que entrega a lo más <c>limite</c> bytes del interior (nunca lee más del tope).</summary>
    private sealed class FlujoAcotado(Stream interior, long limiteDeLectura) : Stream
    {
        private readonly long _limite = limiteDeLectura;
        private long _restante = limiteDeLectura;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => Math.Min(interior.Length, _limite);

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_restante <= 0)
            {
                return 0;
            }

            var leido = interior.Read(buffer[..(int)Math.Min(buffer.Length, _restante)]);
            _restante -= leido;
            return leido;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_restante <= 0)
            {
                return 0;
            }

            var leido = await interior.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _restante)], cancellationToken).ConfigureAwait(false);
            _restante -= leido;
            return leido;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

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
                interior.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await interior.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
