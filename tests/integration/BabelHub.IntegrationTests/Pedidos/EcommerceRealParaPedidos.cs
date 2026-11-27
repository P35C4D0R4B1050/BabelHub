using System.Security.Cryptography;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.IntegrationTests.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>
/// PostgreSQL real con <b>todos los esquemas del E-commerce</b> aplicados por el migrador de verdad (COMP-2: roles, esquemas y permisos mínimos de cada módulo) y las tres cuentas del servicio con contraseñas aleatorias de esta
/// ejecución (nunca un secreto fijo en el repositorio). Una sola instancia para las clases de la colección; las pruebas usan identificadores propios y no vacían nada. Lo usan las pruebas de Pedidos con los módulos reales
/// (ECO-6g): Auditoría, Biblioteca, límites de uso y la Aplicación de Pedidos compuestas por el anfitrión real.
/// </summary>
public sealed class PostgresDelEcommerceCompletoFixture : IAsyncLifetime
{
    private static string Clave() => "c" + Convert.ToHexString(RandomNumberGenerator.GetBytes(18)).ToLowerInvariant();

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("postgres")
        .WithUsername("postgres")
        .WithPassword("admin-" + Guid.NewGuid().ToString("N"))
        .Build();

    private readonly string _aplicacion = "ecommerce_acceso";
    private readonly string _claveDeLaAplicacion = Clave();
    private readonly string _depuradorDeAuditoria = "ecommerce_depurador_auditoria";
    private readonly string _claveDelDepuradorDeAuditoria = Clave();
    private readonly string _depuradorDeRegistros = "ecommerce_depurador_registros";
    private readonly string _claveDelDepuradorDeRegistros = Clave();
    private string _baseDeDatos = string.Empty;

    public NpgsqlDataSource Administracion { get; private set; } = null!;

    public string CadenaDeLaAplicacion => Cadena(_baseDeDatos, _aplicacion, _claveDeLaAplicacion);

    public string CadenaDeDepuracionDeAuditoria => Cadena(_baseDeDatos, _depuradorDeAuditoria, _claveDelDepuradorDeAuditoria);

    public string CadenaDeDepuracionDeRegistros => Cadena(_baseDeDatos, _depuradorDeRegistros, _claveDelDepuradorDeRegistros);

    private string Cadena(string baseDeDatos, string usuario, string clave) =>
        new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()) { Database = baseDeDatos, Username = usuario, Password = clave, Pooling = true, MaxPoolSize = 6, ConnectionIdleLifetime = 1, ConnectionPruningInterval = 1 }.ConnectionString;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var administrador = new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString());
        await using (var origen = NpgsqlDataSource.Create(administrador.ConnectionString))
        {
            _baseDeDatos = "ecommerce_" + Guid.NewGuid().ToString("N")[..10];
            await using var crear = origen.CreateCommand($"CREATE DATABASE {_baseDeDatos}");
            await crear.ExecuteNonQueryAsync();
        }

        var propietaria = new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()) { Database = _baseDeDatos, Pooling = false }.ConnectionString;
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Migrador:Ecommerce:Propietaria"] = propietaria,
            ["Ecommerce:Base:Aplicacion"] = CadenaDeLaAplicacion,
            ["Ecommerce:Base:DepuracionDeAuditoria"] = CadenaDeDepuracionDeAuditoria,
            ["Ecommerce:Base:DepuracionDeRegistros"] = CadenaDeDepuracionDeRegistros,
        }).Build();
        var salida = new StringWriter();
        var error = new StringWriter();
        var codigo = await ModoMigrar.Ejecutar(configuracion, salida, error);
        Assert.True(codigo == 0, "El migrador del E-commerce falló: " + error);

        Administracion = NpgsqlDataSource.Create(propietaria);
    }

    public async Task DisposeAsync()
    {
        if (Administracion is not null)
        {
            await Administracion.DisposeAsync();
        }

        await _contenedor.DisposeAsync();
    }

    public async Task Ejecutar(string sql)
    {
        await using var comando = Administracion.CreateCommand(sql);
        await comando.ExecuteNonQueryAsync();
    }

    public async Task<T?> Consultar<T>(string sql, params (string Nombre, object Valor)[] parametros)
    {
        await using var comando = Administracion.CreateCommand(sql);
        foreach (var (nombre, valor) in parametros)
        {
            comando.Parameters.AddWithValue(nombre, valor);
        }

        var resultado = await comando.ExecuteScalarAsync();
        return resultado is null or DBNull ? default : (T)Convert.ChangeType(resultado, typeof(T));
    }

    /// <summary>Un anfitrión real del E-commerce (tienda, <c>Development</c>) sobre esta base, sin arrancarlo: se usan sus servicios por alcance. <paramref name="ajustes"/> sustituye lo externo (el Inventario); <paramref name="pasarela"/>, si se da, es el valor de <c>Pagos:Pasarela</c>.</summary>
    internal AnfitrionRealDelEcommerce Anfitrion(Action<IServiceCollection> ajustes, string? pasarela = null) => AnfitrionRealDelEcommerce.Crear(this, ajustes, pasarela);

    internal IReadOnlyDictionary<string, string?> Configuracion()
    {
        var valores = new Dictionary<string, string?>(AnfitrionDelEcommerceTests.ConfiguracionCompleta())
        {
            [OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeLaAplicacion] = CadenaDeLaAplicacion,
            [OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeDepuracionDeAuditoria] = CadenaDeDepuracionDeAuditoria,
            [OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeDepuracionDeRegistros] = CadenaDeDepuracionDeRegistros,
        };
        return valores;
    }
}

/// <summary>El anfitrión real del E-commerce compuesto sin escuchar: la Aplicación de Pedidos con Auditoría, Biblioteca y los límites reales sobre PostgreSQL real, y el Inventario sustituido por un doble.</summary>
internal sealed class AnfitrionRealDelEcommerce : IAsyncDisposable
{
    private AnfitrionRealDelEcommerce(WebApplication app) => Aplicacion = app;

    public WebApplication Aplicacion { get; }

    public IServiceProvider Servicios => Aplicacion.Services;

    public static AnfitrionRealDelEcommerce Crear(PostgresDelEcommerceCompletoFixture bd, Action<IServiceCollection> ajustes, string? pasarela = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(bd.Configuracion());
        if (pasarela is not null)
        {
            builder.Configuration.AddInMemoryCollection([new KeyValuePair<string, string?>("Pagos:Pasarela", pasarela)]);
        }

        builder.Services.AddControllersWithViews();
        builder.Host.UseDefaultServiceProvider(o =>
        {
            o.ValidateScopes = true;
            o.ValidateOnBuild = false;
        });
        builder.Services.AddSingleton(typeof(ModoDeArranque), ModoDeArranque.Normal);
        builder.Services.AddScoped<EjecutorDePasosDeRestauracion>();
        builder.AgregarEcommerce(ModoDeArranque.Normal);
        ajustes(builder.Services);
        return new AnfitrionRealDelEcommerce(builder.Build());
    }

    /// <summary>Corre un bloque en un alcance propio con el actor dado (se limpia al terminar); varios bloques pueden correr a la vez.</summary>
    public async Task<T> Como<T>(ActorActual actor, Func<IServiceProvider, Task<T>> bloque)
    {
        await using var alcance = Servicios.CreateAsyncScope();
        var establecedor = Servicios.GetRequiredService<IEstablecedorDeActor>();
        establecedor.Establecer(actor);
        try
        {
            return await bloque(alcance.ServiceProvider);
        }
        finally
        {
            establecedor.Limpiar();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Aplicacion.DisposeAsync();
    }
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionDelEcommerceCompleto : ICollectionFixture<PostgresDelEcommerceCompletoFixture>
{
    public const string Nombre = "E-commerce completo sobre PostgreSQL";
}
