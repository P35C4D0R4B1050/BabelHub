using System.Text;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using StackExchange.Redis;

namespace BabelHub.IntegrationTests.Limites;

/// <summary>
/// Redis real (imagen <c>redis:7-alpine</c>) configurado como el de contadores de producción: sin expulsión de claves, AOF
/// cada segundo, y el usuario ACL restringido de <c>infra/redis-contadores/users.acl.plantilla</c> (el valor por defecto
/// desactivado). Las pruebas del motor usan solo ese usuario; <see cref="Administracion"/> existe para inspeccionar.
/// </summary>
public sealed class RedisDeContadoresFixture : IAsyncLifetime
{
    public const string ClaveDelUsuarioDeLimites = "clave-de-pruebas-de-limites-con-mas-de-32-caracteres";
    private const string ClaveDeAdministracion = "clave-de-administracion-de-pruebas-0123456789";

    private IContainer? _contenedor;

    public ConnectionMultiplexer Limites { get; private set; } = null!;

    public ConnectionMultiplexer Administracion { get; private set; } = null!;

    public IDatabase Bd => Administracion.GetDatabase();

    public MotorDeLimitesRedis Motor(OpcionesDeMotorRedis? opciones = null) => new(Limites, opciones ?? new OpcionesDeMotorRedis());

    public async Task InitializeAsync()
    {
        var raiz = BuscarRaiz();
        var plantilla = await File.ReadAllTextAsync(Path.Combine(raiz, "infra", "redis-contadores", "users.acl.plantilla"));
        var acl = plantilla.Replace("__CLAVE_LIMITES__", ClaveDelUsuarioDeLimites, StringComparison.Ordinal)
            + $"user admin on >{ClaveDeAdministracion} ~* &* +@all\n";

        _contenedor = new ContainerBuilder("redis:7-alpine")
            .WithPortBinding(6379, true)
            .WithResourceMapping(Encoding.UTF8.GetBytes(acl), "/etc/redis-pruebas/users.acl")
            .WithCommand(
                "redis-server",
                "--maxmemory", "100mb",
                "--maxmemory-policy", "noeviction",
                "--appendonly", "yes",
                "--appendfsync", "everysec",
                "--save", string.Empty,
                "--aclfile", "/etc/redis-pruebas/users.acl")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"))
            .Build();
        await _contenedor.StartAsync();

        var puerto = _contenedor.GetMappedPublicPort(6379);
        _puerto = puerto;
        Limites = await Conectar(puerto, "limites", ClaveDelUsuarioDeLimites);
        Administracion = await Conectar(puerto, "admin", ClaveDeAdministracion);
    }

    public async Task DisposeAsync()
    {
        if (Limites is not null)
        {
            await Limites.DisposeAsync();
        }

        if (Administracion is not null)
        {
            await Administracion.DisposeAsync();
        }

        if (_contenedor is not null)
        {
            await _contenedor.DisposeAsync();
        }
    }

    /// <summary>Un nombre único por prueba, para que los contadores de pruebas distintas no se mezclen.</summary>
    public static string Unico(string prefijo) => $"{prefijo}-{Guid.NewGuid():N}"[..(prefijo.Length + 9)];

    public async Task<string[]> TodasLasClaves()
    {
        var servidor = Administracion.GetServer(Administracion.GetEndPoints()[0]);
        var claves = new List<string>();
        await foreach (var clave in servidor.KeysAsync(pattern: "*"))
        {
            claves.Add(clave.ToString());
        }

        return [.. claves];
    }

    private int _puerto;

    /// <summary>Otra conexión del usuario restringido, con la protección del cliente para comandos de administración apagada: así la prueba mide la ACL del servidor.</summary>
    public Task<ConnectionMultiplexer> LimitesSinProteccionDelCliente() =>
        Conectar(_puerto, "limites", ClaveDelUsuarioDeLimites, permitirAdministracion: true);

    private static async Task<ConnectionMultiplexer> Conectar(int puerto, string usuario, string clave, bool permitirAdministracion = false)
    {
        var opciones = new ConfigurationOptions
        {
            EndPoints = { { "127.0.0.1", puerto } },
            User = usuario,
            Password = clave,
            AbortOnConnectFail = false,
            ConnectTimeout = 5000,
            SyncTimeout = 5000,
            AsyncTimeout = 5000,
            AllowAdmin = usuario == "admin" || permitirAdministracion,
        };
        return await ConnectionMultiplexer.ConnectAsync(opciones);
    }

    private static string BuscarRaiz()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null && !File.Exists(Path.Combine(directorio.FullName, "AGENTS.md")))
        {
            directorio = directorio.Parent;
        }

        return directorio?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}

[CollectionDefinition(Nombre)]
public sealed class RedisDeContadoresCollection : ICollectionFixture<RedisDeContadoresFixture>
{
    public const string Nombre = "Redis de contadores";
}
