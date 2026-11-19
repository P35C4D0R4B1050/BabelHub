using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Infrastructure.Ajustes;
using BabelHub.Inventario.Infrastructure.Fuentes;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Inventario.Infrastructure.Reservas;
using BabelHub.Inventario.Infrastructure.Traslados;
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

    /// <summary>La cuenta de depuración de Ajustes (miembro de <c>ajustes_depuracion</c>, que solo ejecuta las dos funciones): distinta de la de la aplicación.</summary>
    public NpgsqlDataSource Depuracion { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());

        await Ejecutar(DefinicionDeFuentes.Rol);
        await Ejecutar(DefinicionDeReservas.Rol);
        await Ejecutar($"""
            CREATE ROLE propietario NOLOGIN;
            CREATE ROLE acceso LOGIN NOINHERIT PASSWORD '{ClaveDeAcceso}';
            GRANT rol_reservas TO acceso;
            GRANT rol_fuentes TO acceso;
            """);
        await Ejecutar(DefinicionDeFuentes.Sql);
        await Ejecutar(DefinicionDeReservas.Sql);
        await Ejecutar("""
            ALTER SCHEMA reservas OWNER TO propietario;
            ALTER TABLE reservas.reservas OWNER TO propietario;
            ALTER TABLE reservas.reserva_lineas OWNER TO propietario;
            ALTER TABLE reservas.operaciones_idempotentes OWNER TO propietario;
            ALTER TABLE reservas.bajas_recibidas OWNER TO propietario;
            """);
        await Ejecutar("""
            ALTER SCHEMA fuentes OWNER TO propietario;
            ALTER TABLE fuentes.fuentes OWNER TO propietario;
            ALTER TABLE fuentes.ediciones OWNER TO propietario;
            ALTER TABLE fuentes.stock OWNER TO propietario;
            ALTER TABLE fuentes.rutas OWNER TO propietario;
            """);
        await Ejecutar(DefinicionDeFuentes.Permisos);
        await Ejecutar(DefinicionDeReservas.Permisos);
        // Los eventos del motor de límites de plataforma (plataforma.eventos_limite), con el permiso mínimo de su rol.
        await Ejecutar("CREATE ROLE rol_plataforma NOLOGIN; GRANT rol_plataforma TO acceso;");
        await Ejecutar(DefinicionDeEventosDeLimite.Sql + """
            ALTER SCHEMA plataforma OWNER TO propietario;
            ALTER TABLE plataforma.eventos_limite OWNER TO propietario;
            """ + DefinicionDeEventosDeLimite.Permisos);

        // El esquema de Ajustes: roles, tablas con sus disparadores, tablas del propietario, funciones de depuración y permisos; y la cuenta de depuración, aparte de la de la aplicación.
        await Ejecutar(DefinicionDeAjustes.Roles);
        await Ejecutar($"CREATE ROLE depurador LOGIN PASSWORD '{ClaveDeAcceso}'; GRANT ajustes_depuracion TO depurador; GRANT rol_ajustes TO acceso;");
        await Ejecutar(DefinicionDeAjustes.Sql);
        await Ejecutar("ALTER SCHEMA ajustes OWNER TO propietario; ALTER TABLE ajustes.ajustes OWNER TO propietario; ALTER TABLE ajustes.ingresos OWNER TO propietario; ALTER TABLE ajustes.operaciones_idempotentes OWNER TO propietario;");
        await Ejecutar(DefinicionDeAjustes.FuncionesDeDepuracion);
        await Ejecutar(DefinicionDeAjustes.Permisos);

        // El esquema de Traslados: rol, tablas con su política de filas, tablas del propietario y permisos mínimos.
        await Ejecutar(DefinicionDeTraslados.Rol);
        await Ejecutar("GRANT rol_traslados TO acceso;");
        await Ejecutar(DefinicionDeTraslados.Sql);
        await Ejecutar("ALTER SCHEMA traslados OWNER TO propietario; ALTER TABLE traslados.traslados OWNER TO propietario; ALTER TABLE traslados.mensajes_salientes OWNER TO propietario;");
        await Ejecutar(DefinicionDeTraslados.Permisos);

        Depuracion = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()) { Username = "depurador", ["Password"] = ClaveDeAcceso }.ConnectionString);

        Acceso = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString())
        {
            Username = "acceso",
            ["Password"] = ClaveDeAcceso,
            MaxPoolSize = 60,
        }.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (Depuracion is not null)
        {
            await Depuracion.DisposeAsync();
        }

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

    public Task Limpiar() => Ejecutar(
        """
        ALTER TABLE ajustes.ajustes DISABLE TRIGGER ajustes_solo_insercion_por_sentencia;
        ALTER TABLE ajustes.ingresos DISABLE TRIGGER ingresos_solo_insercion_por_sentencia;
        TRUNCATE traslados.traslados, traslados.mensajes_salientes, ajustes.ajustes, ajustes.ingresos, ajustes.operaciones_idempotentes, reservas.reserva_lineas, reservas.reservas, reservas.operaciones_idempotentes, reservas.bajas_recibidas, plataforma.eventos_limite, fuentes.stock, fuentes.rutas, fuentes.ediciones, fuentes.fuentes CASCADE;
        ALTER TABLE ajustes.ajustes ENABLE ALWAYS TRIGGER ajustes_solo_insercion_por_sentencia;
        ALTER TABLE ajustes.ingresos ENABLE ALWAYS TRIGGER ingresos_solo_insercion_por_sentencia;
        """);

    // --- los datos de Fuentes (con la cuenta de despliegue) ---

    public async Task<Guid> AgregarFuente(string codigo, string? region = null, bool recepcion = false)
    {
        var id = PostgresDeFuentesFixture.IdDeFuente(codigo);
        await using var comando = Administracion.CreateCommand(
            "INSERT INTO fuentes.fuentes (id, codigo, nombre, region, es_recepcion) VALUES (@id, @codigo, @nombre, @region, @recepcion)");
        comando.Parameters.AddWithValue("id", id);
        comando.Parameters.AddWithValue("codigo", codigo);
        comando.Parameters.AddWithValue("nombre", char.ToUpperInvariant(codigo[0]) + codigo[1..]);
        comando.Parameters.AddWithValue("region", region ?? codigo);
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

    public async Task DefinirRuta(Guid origenId, Guid destinoId, int dias) =>
        await Ejecutar($"INSERT INTO fuentes.rutas (origen_id, destino_id, plazo_dias) VALUES ('{origenId}', '{destinoId}', {dias}) ON CONFLICT (origen_id, destino_id) DO UPDATE SET plazo_dias = {dias}");

    /// <summary>El stock actual de una pareja leído con la cuenta de despliegue (se salta la política), o <see langword="null"/> si no hay fila.</summary>
    public async Task<(int Fisico, int Reservado)?> StockDe(Guid fuenteId, Guid edicionId)
    {
        await using var comando = Administracion.CreateCommand("SELECT fisico, reservado FROM fuentes.stock WHERE fuente_id = @f AND edicion_id = @e");
        comando.Parameters.AddWithValue("f", fuenteId);
        comando.Parameters.AddWithValue("e", edicionId);
        await using var lector = await comando.ExecuteReaderAsync();
        return await lector.ReadAsync() ? (lector.GetInt32(0), lector.GetInt32(1)) : null;
    }

    /// <summary>Las unidades reservadas de la red para una edición.</summary>
    public async Task<long> ReservadoDeLaRed(Guid edicionId) =>
        await Consultar<long>($"SELECT coalesce(sum(reservado), 0) FROM fuentes.stock WHERE edicion_id = '{edicionId}'");

    /// <summary>La suma de las cantidades de las líneas activas (pendientes o confirmadas) de la red para una edición: debe coincidir con lo reservado.</summary>
    public async Task<long> UnidadesDeLineasActivas(Guid edicionId) =>
        await Consultar<long>($"SELECT coalesce(sum(cantidad), 0) FROM reservas.reserva_lineas WHERE edicion_id = '{edicionId}' AND estado IN ('pendiente', 'confirmada')");

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
