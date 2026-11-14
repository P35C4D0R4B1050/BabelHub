using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Infrastructure.Fuentes;
using BabelHub.Inventario.Infrastructure.Plataforma;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Fuentes;

/// <summary>
/// PostgreSQL real con la forma de permisos de AD18 para el módulo <c>Fuentes</c> del Inventario: la migración de <see cref="DefinicionDeFuentes"/> aplicada por la
/// cuenta de despliegue (el superusuario del contenedor), un propietario de las tablas, el rol del módulo <c>rol_fuentes</c> sin inicio de sesión y el rol de acceso de
/// la aplicación (<c>acceso</c>, <c>NOINHERIT</c>, miembro solo de <c>rol_fuentes</c>). Las pruebas usan <see cref="Acceso"/>: <b>nunca un superusuario</b>, que se salta la
/// seguridad por filas y haría pasar pruebas que fallarían en producción. <see cref="Administracion"/> es solo para preparar y comprobar.
/// </summary>
public sealed class PostgresDeFuentesFixture : IAsyncLifetime
{
    private const string ClaveDeAcceso = "acceso-fuentes-0123456789";

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("inventario")
        .WithUsername("postgres")
        .WithPassword("postgres-pruebas")
        .Build();

    /// <summary>El rol de la aplicación: sin permisos por sí mismo, solo miembro de <c>rol_fuentes</c> (con <c>SET LOCAL ROLE</c> dentro de <c>EjecutarComo</c>).</summary>
    public NpgsqlDataSource Acceso { get; private set; } = null!;

    /// <summary>La cuenta de despliegue (superusuario): prepara los datos y comprueba el resultado. Se salta la seguridad por filas.</summary>
    public NpgsqlDataSource Administracion { get; private set; } = null!;

    public string CadenaDeAcceso { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());

        await Ejecutar(DefinicionDeFuentes.Rol);
        await Ejecutar("""
            CREATE ROLE propietario NOLOGIN;
            CREATE ROLE acceso LOGIN NOINHERIT PASSWORD 'acceso-fuentes-0123456789';
            GRANT rol_fuentes TO acceso;
            """);
        await Ejecutar(DefinicionDeFuentes.Sql);
        await Ejecutar("""
            ALTER SCHEMA fuentes OWNER TO propietario;
            ALTER TABLE fuentes.fuentes OWNER TO propietario;
            ALTER TABLE fuentes.ediciones OWNER TO propietario;
            ALTER TABLE fuentes.stock OWNER TO propietario;
            ALTER TABLE fuentes.rutas OWNER TO propietario;
            """);
        await Ejecutar(DefinicionDeFuentes.Permisos);

        CadenaDeAcceso = new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString())
        {
            Username = "acceso",
            Password = ClaveDeAcceso,
            MaxPoolSize = 60,
        }.ConnectionString;
        Acceso = NpgsqlDataSource.Create(CadenaDeAcceso);
    }

    public async Task DisposeAsync()
    {
        if (Acceso is not null)
        {
            await Acceso.DisposeAsync();
        }

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

    public async Task<T?> Consultar<T>(string sql)
    {
        await using var comando = Administracion.CreateCommand(sql);
        var valor = await comando.ExecuteScalarAsync();
        return valor is null or DBNull ? default : (T)valor;
    }

    /// <summary>Vacía las tablas del módulo (con la cuenta de despliegue) para que cada prueba parta de cero.</summary>
    public Task Limpiar() => Ejecutar("TRUNCATE fuentes.stock, fuentes.rutas, fuentes.ediciones, fuentes.fuentes");

    /// <summary>Una unidad de trabajo con el rol real de la aplicación y el actor dado (<see langword="null"/> = nadie estableció el contexto).</summary>
    public UnidadDeTrabajoPostgres Unidad(ActorActual? actor) => new(Acceso, actor: new ActorFijo(actor));

    /// <summary>Inserta una fuente con la cuenta de despliegue y devuelve su identificador.</summary>
    public async Task<Guid> AgregarFuente(string codigo, string? nombre = null, string region = "lima", bool recepcion = false)
    {
        var id = Guid.NewGuid();
        await using var comando = Administracion.CreateCommand(
            "INSERT INTO fuentes.fuentes (id, codigo, nombre, region, es_recepcion) VALUES (@id, @codigo, @nombre, @region, @recepcion)");
        comando.Parameters.AddWithValue("id", id);
        comando.Parameters.AddWithValue("codigo", codigo);
        comando.Parameters.AddWithValue("nombre", nombre ?? char.ToUpperInvariant(codigo[0]) + codigo[1..]);
        comando.Parameters.AddWithValue("region", region);
        comando.Parameters.AddWithValue("recepcion", recepcion);
        await comando.ExecuteNonQueryAsync();
        return id;
    }

    public async Task<Guid> AgregarEdicion(string formato = "impreso")
    {
        var id = Guid.NewGuid();
        await using var comando = Administracion.CreateCommand("INSERT INTO fuentes.ediciones (edicion_id, formato) VALUES (@id, @formato)");
        comando.Parameters.AddWithValue("id", id);
        comando.Parameters.AddWithValue("formato", formato);
        await comando.ExecuteNonQueryAsync();
        return id;
    }

    public async Task PonerStock(Guid fuenteId, Guid edicionId, int fisico, int reservado)
    {
        await using var comando = Administracion.CreateCommand(
            """
            INSERT INTO fuentes.stock (fuente_id, edicion_id, formato, fisico, reservado) VALUES (@f, @e, 'impreso', @fi, @re)
            ON CONFLICT (fuente_id, edicion_id) DO UPDATE SET fisico = @fi, reservado = @re
            """);
        comando.Parameters.AddWithValue("f", fuenteId);
        comando.Parameters.AddWithValue("e", edicionId);
        comando.Parameters.AddWithValue("fi", fisico);
        comando.Parameters.AddWithValue("re", reservado);
        await comando.ExecuteNonQueryAsync();
    }

    /// <summary>El stock actual de una pareja leído con la cuenta de despliegue (se salta la política), o <see langword="null"/> si no hay fila.</summary>
    public async Task<(int Fisico, int Reservado)?> StockDe(Guid fuenteId, Guid edicionId)
    {
        await using var comando = Administracion.CreateCommand("SELECT fisico, reservado FROM fuentes.stock WHERE fuente_id = @f AND edicion_id = @e");
        comando.Parameters.AddWithValue("f", fuenteId);
        comando.Parameters.AddWithValue("e", edicionId);
        await using var lector = await comando.ExecuteReaderAsync();
        return await lector.ReadAsync() ? (lector.GetInt32(0), lector.GetInt32(1)) : null;
    }
}

/// <summary>El actor de una prueba: un valor fijo que se puede cambiar, o ninguno.</summary>
public sealed class ActorFijo(ActorActual? actor) : IActorActual
{
    public ActorActual? Actual { get; set; } = actor;
}
