using Amazon.S3;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace BabelHub.IntegrationTests.Plataforma;

/// <summary>
/// Un almacén de objetos compatible con S3 real (en un contenedor) con el contenedor lógico del registro de supresiones. No reproduce las
/// políticas de Oracle (permisos por prefijo, retención con bloqueo: son de T21 y se verifican contra el almacén real con el spike T02),
/// pero sí la semántica que el registro necesita: «crear solo si no existe» atómico, lectura y listado.
/// </summary>
public sealed class AlmacenDeObjetosFixture : IAsyncLifetime
{
    public const string Contenedor = OpcionesDelAlmacenDeObjetos.ContenedorDeSupresiones;
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

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Opciones = new OpcionesDelAlmacenDeObjetos(
            new Uri($"http://{_contenedor.Hostname}:{_contenedor.GetMappedPublicPort(9000)}"),
            "us-east-1",
            ClaveDeAcceso,
            new BabelHub.Ecommerce.Application.Plataforma.ValorSecreto(ClaveSecreta),
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

    public async Task DisposeAsync()
    {
        Cliente?.Dispose();
        await _contenedor.DisposeAsync();
    }
}

[CollectionDefinition(Nombre)]
public sealed class AlmacenDeObjetosCollection : ICollectionFixture<AlmacenDeObjetosFixture>
{
    public const string Nombre = "Almacen de objetos";
}
