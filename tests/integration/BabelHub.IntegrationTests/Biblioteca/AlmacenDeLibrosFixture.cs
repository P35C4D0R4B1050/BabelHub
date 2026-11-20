using Amazon.S3;
using Amazon.S3.Model;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace BabelHub.IntegrationTests.Biblioteca;

/// <summary>
/// Un almacén de objetos compatible con S3 real (en un contenedor) con el contenedor lógico <c>libros</c> y contenido <b>sintético</b> generado en la prueba (regla 11: nunca archivos de prueba en el repositorio). Sirve para comprobar la existencia del archivo, la URL firmada, su vencimiento y
/// que un <c>GET</c> anónimo se rechaza. No reproduce las políticas de Oracle (permisos por credencial: <c>PoliticasDelAlmacenTests</c> sobre <c>infra/almacen/</c>).
/// </summary>
public sealed class AlmacenDeLibrosFixture : IAsyncLifetime
{
    public const string Contenedor = "libros";
    public const string ClaveDeAcceso = "acceso-de-pruebas-del-almacen-0123456789";
    public const string ClaveSecreta = "secreta-de-pruebas-del-almacen-9876543210-abcdefgh";

    private readonly IContainer _contenedor = new ContainerBuilder("rustfs/rustfs:latest")
        .WithPortBinding(9000, true)
        .WithEnvironment("RUSTFS_ACCESS_KEY", ClaveDeAcceso)
        .WithEnvironment("RUSTFS_SECRET_KEY", ClaveSecreta)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(9000))
        .Build();

    public IAmazonS3 Cliente { get; private set; } = null!;

    public OpcionesDelAlmacenDeObjetos Opciones { get; private set; } = null!;

    /// <summary>Las mismas opciones, con la forma propia de Biblioteca (solo lectura de <c>libros</c>).</summary>
    internal BabelHub.Ecommerce.Infrastructure.Biblioteca.OpcionesDelAlmacenDeLibros OpcionesDeLibros => new(Opciones.Endpoint, Opciones.Region, Opciones.ClaveDeAcceso, Opciones.ClaveSecreta, Contenedor, Opciones.RutaEnLaUrl);

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Opciones = new OpcionesDelAlmacenDeObjetos(
            new Uri($"http://{_contenedor.Hostname}:{_contenedor.GetMappedPublicPort(9000)}"),
            "us-east-1",
            ClaveDeAcceso,
            new ValorSecreto(ClaveSecreta),
            Contenedor);
        Cliente = ClienteDelAlmacenDeObjetos.Crear(Opciones);
        await CrearElContenedorLogico();
    }

    private async Task CrearElContenedorLogico()
    {
        // El servidor puede tardar un poco en aceptar solicitudes tras abrir el puerto.
        for (var intento = 0; intento < 40; intento++)
        {
            try
            {
                await Cliente.PutBucketAsync(Contenedor);
                return;
            }
            catch (Exception) when (intento < 39)
            {
                await Task.Delay(500);
            }
        }
    }

    /// <summary>Guarda un archivo sintético bajo una clave aleatoria de 32 hexadecimales y devuelve la clave.</summary>
    public async Task<string> GuardarSintetico(byte[] contenido)
    {
        var clave = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        await Cliente.PutObjectAsync(new PutObjectRequest { BucketName = Contenedor, Key = clave, InputStream = new MemoryStream(contenido), ContentType = "application/octet-stream" });
        return clave;
    }

    public async Task DisposeAsync()
    {
        Cliente?.Dispose();
        await _contenedor.DisposeAsync();
    }
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionAlmacenDeLibros : ICollectionFixture<AlmacenDeLibrosFixture>
{
    public const string Nombre = "Almacen de libros";
}
