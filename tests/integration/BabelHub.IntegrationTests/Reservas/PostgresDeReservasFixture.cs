using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Inventario.Infrastructure.Reservas;
using BabelHub.IntegrationTests.Fuentes;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>
/// PostgreSQL real con la forma de permisos de AD18 para el módulo <c>Reservas</c> del Inventario: la migración de <see cref="DefinicionDeReservas"/> aplicada por la cuenta de despliegue, un propietario de las
/// tablas, el rol del módulo <c>rol_reservas</c> sin inicio de sesión y el rol de acceso de la aplicación (<c>acceso</c>, <c>NOINHERIT</c>, miembro solo de <c>rol_reservas</c>). Las pruebas usan <see cref="Acceso"/>:
/// <b>nunca un superusuario</b>. <see cref="Administracion"/> es solo para preparar y comprobar.
/// </summary>
public sealed class PostgresDeReservasFixture : IAsyncLifetime
{
    private const string ClaveDeAcceso = "acceso-reservas-0123456789";

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("inventario")
        .WithUsername("postgres")
        .WithPassword("postgres-pruebas")
        .Build();

    public NpgsqlDataSource Acceso { get; private set; } = null!;

    public NpgsqlDataSource Administracion { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());

        await Ejecutar(DefinicionDeReservas.Rol);
        await Ejecutar($"""
            CREATE ROLE propietario NOLOGIN;
            CREATE ROLE acceso LOGIN NOINHERIT PASSWORD '{ClaveDeAcceso}';
            GRANT rol_reservas TO acceso;
            """);
        await Ejecutar(DefinicionDeReservas.Sql);
        await Ejecutar("""
            ALTER SCHEMA reservas OWNER TO propietario;
            ALTER TABLE reservas.reservas OWNER TO propietario;
            ALTER TABLE reservas.reserva_lineas OWNER TO propietario;
            ALTER TABLE reservas.operaciones_idempotentes OWNER TO propietario;
            ALTER TABLE reservas.bajas_recibidas OWNER TO propietario;
            """);
        await Ejecutar(DefinicionDeReservas.Permisos);

        Acceso = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString())
        {
            Username = "acceso",
            ["Password"] = ClaveDeAcceso,
            MaxPoolSize = 60,
        }.ConnectionString);
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

    public Task Limpiar() => Ejecutar("TRUNCATE reservas.reserva_lineas, reservas.reservas, reservas.operaciones_idempotentes, reservas.bajas_recibidas");

    /// <summary>Una unidad de trabajo con el rol real de la aplicación y el actor dado (<see langword="null"/> = nadie estableció el contexto).</summary>
    public UnidadDeTrabajoPostgres Unidad(ActorActual? actor) => new(Acceso, actor: new ActorFijo(actor));

    /// <summary>Inserta una cabecera de recojo con la cuenta de despliegue y devuelve su identificador.</summary>
    public async Task<Guid> AgregarReserva(Guid? pedidoId = null, Guid? clienteId = null, string? clave = null, int reemplazos = 0)
    {
        var id = Guid.NewGuid();
        await using var comando = Administracion.CreateCommand(
            """
            INSERT INTO reservas.reservas (id, pedido_id, cliente_id, clave_idempotencia, huella_de_carga, tipo_entrega, sucursal_recojo_id, iniciada_en, fecha_expiracion, reemplazos)
            VALUES (@id, @pedido, @cliente, @clave, @huella, 'recojo', @sucursal, now(), now() + interval '15 minutes', @reemplazos)
            """);
        comando.Parameters.AddWithValue("id", id);
        comando.Parameters.AddWithValue("pedido", pedidoId ?? Guid.NewGuid());
        comando.Parameters.AddWithValue("cliente", clienteId ?? Guid.NewGuid());
        comando.Parameters.AddWithValue("clave", clave ?? Guid.NewGuid().ToString("N"));
        comando.Parameters.AddWithValue("huella", new string('a', 64));
        comando.Parameters.AddWithValue("sucursal", Guid.NewGuid());
        comando.Parameters.AddWithValue("reemplazos", reemplazos);
        await comando.ExecuteNonQueryAsync();
        return id;
    }

    public async Task<(Guid PedidoId, Guid ReservaId)> PedidoConReserva()
    {
        var pedido = Guid.NewGuid();
        return (pedido, await AgregarReserva(pedido));
    }

    /// <summary>Inserta una línea con la cuenta de despliegue.</summary>
    public async Task<Guid> AgregarLinea(Guid reservaId, Guid pedidoId, Guid? lineaId = null, string estado = "pendiente", string? cerradaPor = null, int cantidad = 1, bool recreada = false, bool anulada = false)
    {
        var id = Guid.NewGuid();
        await using var comando = Administracion.CreateCommand(
            """
            INSERT INTO reservas.reserva_lineas (id, linea_id, reserva_id, pedido_id, edicion_id, fuente_id, cantidad, estado, cerrada_por, recreada_en_destino, anulada_en, estado_cambiado_en)
            VALUES (@id, @linea, @reserva, @pedido, @edicion, @fuente, @cantidad, @estado, @cerradaPor, @recreada, CASE WHEN @anulada THEN now() ELSE NULL END, now())
            """);
        comando.Parameters.AddWithValue("id", id);
        comando.Parameters.AddWithValue("linea", lineaId ?? Guid.NewGuid());
        comando.Parameters.AddWithValue("reserva", reservaId);
        comando.Parameters.AddWithValue("pedido", pedidoId);
        comando.Parameters.AddWithValue("edicion", Guid.NewGuid());
        comando.Parameters.AddWithValue("fuente", Guid.NewGuid());
        comando.Parameters.AddWithValue("cantidad", cantidad);
        comando.Parameters.AddWithValue("estado", estado);
        comando.Parameters.AddWithValue("cerradaPor", (object?)cerradaPor ?? DBNull.Value);
        comando.Parameters.AddWithValue("recreada", recreada);
        comando.Parameters.AddWithValue("anulada", anulada);
        await comando.ExecuteNonQueryAsync();
        return id;
    }
}
