using System.Security.Cryptography;
using BabelHub.Ecommerce.Api.Plataforma;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;
using Eco = BabelHub.Ecommerce.Infrastructure.Migracion;
using Inv = BabelHub.Inventario.Infrastructure.Migracion;

namespace BabelHub.IntegrationTests.Despliegue;

/// <summary>Una instancia de PostgreSQL para las pruebas del migrador: cada prueba trabaja en su propia base, así que la corrida sobre un esquema limpio es siempre limpia.</summary>
public sealed class InstanciaParaMigrar : IAsyncLifetime
{
    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("postgres")
        .WithUsername("postgres")
        .WithPassword("administracion-" + Guid.NewGuid().ToString("N"))
        .Build();

    private NpgsqlDataSource _administracion = null!;
    private int _siguiente;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        _administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await _administracion.DisposeAsync();
        await _contenedor.DisposeAsync();
    }

    /// <summary>El registro del servidor de PostgreSQL (lo que un administrador vería en los logs del contenedor).</summary>
    public async Task<(string Salida, string Errores)> RegistroDelServidor() => await _contenedor.GetLogsAsync();

    /// <summary>Una base nueva y la cadena de la cuenta propietaria (administradora) para ella.</summary>
    public async Task<string> NuevaBase()
    {
        var nombre = "migrar_" + Interlocked.Increment(ref _siguiente) + "_" + Guid.NewGuid().ToString("N")[..8];
        await using var comando = _administracion.CreateCommand($"CREATE DATABASE {nombre}");
        await comando.ExecuteNonQueryAsync();
        return Cadena(nombre, "postgres", new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()).Password!);
    }

    public string Cadena(string baseDeDatos, string usuario, string pruebaClave) =>
        new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()) { Database = baseDeDatos, Username = usuario, Password = pruebaClave, Pooling = false }.ConnectionString;
}

/// <summary>
/// El migrador de esquemas y roles (COMP-2; PLA-04, AD18, SEG-11): sobre una base limpia crea los roles de esquema, los esquemas de los módulos y las cuentas del servicio con el permiso mínimo; repetirlo no daña ni cambia nada salvo rotar las contraseñas;
/// aplica los pasos en orden; y <b>falla cerrado</b> —sin la cuenta propietaria, con una variable que falta, con una cuenta que no es administradora o con un rol que falta— nombrando la variable o el rol y sin imprimir ningún secreto.
/// </summary>
public sealed class MigradorDeLaBaseTests(InstanciaParaMigrar instancia) : IClassFixture<InstanciaParaMigrar>
{
    private static string Clave() => "c" + Convert.ToHexString(RandomNumberGenerator.GetBytes(18)).ToLowerInvariant();

    private sealed record Cuentas(string Aplicacion, string ClaveDeLaAplicacion, string DepuracionUno, string ClaveUno, string DepuracionDos, string ClaveDos);

    private static Cuentas NuevasCuentas(string prefijo) => new($"{prefijo}_acceso", Clave(), $"{prefijo}_dep_uno", Clave(), $"{prefijo}_dep_dos", Clave());

    private string Conexion(string baseDeDatos, string usuario, string clave) => instancia.Cadena(baseDeDatos, usuario, clave);

    private static string Base(string cadena) => new NpgsqlConnectionStringBuilder(cadena).Database!;

    private IConfiguration ConfiguracionDelEcommerce(string propietaria, Cuentas c, string baseDeDatos) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Migrador:Ecommerce:Propietaria"] = propietaria,
        ["Ecommerce:Base:Aplicacion"] = Conexion(baseDeDatos, c.Aplicacion, c.ClaveDeLaAplicacion),
        ["Ecommerce:Base:DepuracionDeAuditoria"] = Conexion(baseDeDatos, c.DepuracionUno, c.ClaveUno),
        ["Ecommerce:Base:DepuracionDeRegistros"] = Conexion(baseDeDatos, c.DepuracionDos, c.ClaveDos),
    }).Build();

    private IConfiguration ConfiguracionDelInventario(string propietaria, Cuentas c, string baseDeDatos) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Migrador:Inventario:Propietaria"] = propietaria,
        ["Inventario:Base:Aplicacion"] = Conexion(baseDeDatos, c.Aplicacion, c.ClaveDeLaAplicacion),
        ["Inventario:Base:DepuracionDeAjustes"] = Conexion(baseDeDatos, c.DepuracionUno, c.ClaveUno),
        ["Inventario:Base:DepuracionDeAuditoria"] = Conexion(baseDeDatos, c.DepuracionDos, c.ClaveDos),
    }).Build();

    private static async Task<T?> Consultar<T>(string cadena, string sql)
    {
        await using var origen = NpgsqlDataSource.Create(cadena);
        await using var comando = origen.CreateCommand(sql);
        var valor = await comando.ExecuteScalarAsync();
        return valor is null or DBNull ? default : (T)valor;
    }

    private static async Task<(int Codigo, string Salida, string Error)> Migrar(IConfiguration configuracion, Func<IConfiguration, TextWriter, TextWriter, Task<int>> ejecutar)
    {
        var salida = new StringWriter();
        var error = new StringWriter();
        var codigo = await ejecutar(configuracion, salida, error);
        return (codigo, salida.ToString(), error.ToString());
    }

    private static Task<(int Codigo, string Salida, string Error)> MigrarElEcommerce(IConfiguration c) =>
        Migrar(c, (conf, o, e) => ModoMigrar.Ejecutar(conf, o, e));

    private static Task<(int Codigo, string Salida, string Error)> MigrarElInventario(IConfiguration c) =>
        Migrar(c, (conf, o, e) => BabelHub.Inventario.Api.Plataforma.ModoMigrar.Ejecutar(conf, o, e));

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public async Task EnUnaBaseLimpia_ElEcommerceQuedaConSusRolesEsquemasYCuentas_ConElPermisoMinimo()
    {
        var propietaria = await instancia.NuevaBase();
        var baseDeDatos = Base(propietaria);
        var cuentas = NuevasCuentas("eco1");

        var resultado = await MigrarElEcommerce(ConfiguracionDelEcommerce(propietaria, cuentas, baseDeDatos));

        Assert.True(resultado.Codigo == 0, resultado.Error);
        foreach (var rol in Eco.PlanDeMigracionDelEcommerce.RolesDeLaAplicacion)
        {
            Assert.Equal(1L, await Consultar<long>(propietaria, $"SELECT count(*) FROM pg_roles WHERE rolname = '{rol}' AND NOT rolcanlogin"));
            Assert.True(await Consultar<bool>(propietaria, $"SELECT pg_has_role('{cuentas.Aplicacion}', '{rol}', 'MEMBER')"), rol);
        }

        // La aplicación es NOINHERIT: por sí sola no lee ningún esquema; con el rol del módulo, sí.
        Assert.False(await Consultar<bool>(propietaria, $"SELECT rolinherit FROM pg_roles WHERE rolname = '{cuentas.Aplicacion}'"));
        Assert.False(await Consultar<bool>(propietaria, $"SELECT has_table_privilege('{cuentas.Aplicacion}', 'carrito.carritos', 'SELECT')"));
        var acceso = Conexion(baseDeDatos, cuentas.Aplicacion, cuentas.ClaveDeLaAplicacion);
        await using (var origen = NpgsqlDataSource.Create(acceso))
        {
            await using var conexion = await origen.OpenConnectionAsync();
            await using var transaccion = await conexion.BeginTransactionAsync();
            await using (var sin = new NpgsqlCommand("SELECT count(*) FROM carrito.carritos", conexion, transaccion))
            {
                await Assert.ThrowsAsync<PostgresException>(() => sin.ExecuteScalarAsync());
            }
        }

        await using (var origen = NpgsqlDataSource.Create(acceso))
        {
            await using var conexion = await origen.OpenConnectionAsync();
            await using var transaccion = await conexion.BeginTransactionAsync();
            await using (var rol = new NpgsqlCommand("SET LOCAL ROLE rol_carrito", conexion, transaccion))
            {
                await rol.ExecuteNonQueryAsync();
            }

            await using var con = new NpgsqlCommand("SELECT count(*) FROM carrito.carritos", conexion, transaccion);
            Assert.Equal(0L, await con.ExecuteScalarAsync());
        }

        // La propiedad: lo creado es de un rol propietario sin login ni privilegios; Auditoría e Identidad conservan la de sus roles de migraciones.
        Assert.Equal("propietario_ecommerce", await Consultar<string>(propietaria, "SELECT tableowner FROM pg_tables WHERE schemaname = 'carrito' AND tablename = 'carritos'"));
        Assert.Equal("propietario_ecommerce", await Consultar<string>(propietaria, "SELECT pg_get_userbyid(nspowner) FROM pg_namespace WHERE nspname = 'carrito'"));
        Assert.Equal("auditoria_migraciones", await Consultar<string>(propietaria, "SELECT pg_get_userbyid(nspowner) FROM pg_namespace WHERE nspname = 'auditoria'"));
        Assert.Equal("auditoria_migraciones", await Consultar<string>(propietaria, "SELECT pg_get_userbyid(nspowner) FROM pg_namespace WHERE nspname = 'identidad'"));
        Assert.False(await Consultar<bool>(propietaria, "SELECT rolsuper OR rolcanlogin OR rolcreaterole FROM pg_roles WHERE rolname = 'propietario_ecommerce'"));

        // Las dos cuentas de depuración heredan el rol de depuración y no son la de la aplicación.
        foreach (var depuracion in new[] { cuentas.DepuracionUno, cuentas.DepuracionDos })
        {
            Assert.True(await Consultar<bool>(propietaria, $"SELECT pg_has_role('{depuracion}', 'auditoria_depuracion', 'USAGE')"));
            Assert.False(await Consultar<bool>(propietaria, $"SELECT pg_has_role('{depuracion}', 'rol_carrito', 'MEMBER')"));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public async Task EnUnaBaseLimpia_ElInventarioQuedaConSusRolesEsquemasYCuentas()
    {
        var propietaria = await instancia.NuevaBase();
        var baseDeDatos = Base(propietaria);
        var cuentas = NuevasCuentas("inv1");

        var resultado = await MigrarElInventario(ConfiguracionDelInventario(propietaria, cuentas, baseDeDatos));

        Assert.True(resultado.Codigo == 0, resultado.Error);
        foreach (var rol in Inv.PlanDeMigracionDelInventario.RolesDeLaAplicacion)
        {
            Assert.True(await Consultar<bool>(propietaria, $"SELECT pg_has_role('{cuentas.Aplicacion}', '{rol}', 'MEMBER')"), rol);
        }

        foreach (var esquema in new[] { "fuentes", "reservas", "ajustes", "traslados", "auditoria", "plataforma" })
        {
            Assert.Equal(1L, await Consultar<long>(propietaria, $"SELECT count(*) FROM pg_namespace WHERE nspname = '{esquema}'"));
        }

        Assert.Equal("propietario_inventario", await Consultar<string>(propietaria, "SELECT tableowner FROM pg_tables WHERE schemaname = 'reservas' AND tablename = 'reservas'"));
        Assert.True(await Consultar<bool>(propietaria, $"SELECT pg_has_role('{cuentas.DepuracionUno}', 'ajustes_depuracion', 'USAGE')"));
        Assert.True(await Consultar<bool>(propietaria, $"SELECT pg_has_role('{cuentas.DepuracionDos}', 'auditoria_depuracion', 'USAGE')"));
        Assert.False(await Consultar<bool>(propietaria, $"SELECT rolinherit FROM pg_roles WHERE rolname = '{cuentas.Aplicacion}'"));
    }

    private static async Task<string?> EstadoDeLaSentencia(string cadena, string rol, string sql)
    {
        await using var origen = NpgsqlDataSource.Create(cadena);
        await using var conexion = await origen.OpenConnectionAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();
        await using (var cambio = new NpgsqlCommand($"SET LOCAL ROLE {rol}", conexion, transaccion))
        {
            await cambio.ExecuteNonQueryAsync();
        }

        try
        {
            await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
            await comando.ExecuteNonQueryAsync();
            return null;
        }
        catch (PostgresException e)
        {
            return e.SqlState;
        }
    }

    private static Task<long> ObjetosDeLaAdministracion(string propietaria) => Consultar<long>(propietaria, """
        SELECT (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'public') AND n.nspname NOT LIKE 'pg_%' AND c.relkind IN ('r', 'p', 'v', 'm') AND pg_get_userbyid(c.relowner) = 'postgres')
             + (SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'public') AND n.nspname NOT LIKE 'pg_%' AND pg_get_userbyid(p.proowner) = 'postgres')
        """)!;

    [Fact]
    [Trait("Criterio", "AC-SEG-04")]
    public async Task ElEsquemaMigradoDelEcommerce_ConservaLasGarantiasDeSeguridadDeSusModulos()
    {
        var propietaria = await instancia.NuevaBase();
        var baseDeDatos = Base(propietaria);
        var cuentas = NuevasCuentas("eco7");
        Assert.True((await MigrarElEcommerce(ConfiguracionDelEcommerce(propietaria, cuentas, baseDeDatos))).Codigo == 0);
        var acceso = Conexion(baseDeDatos, cuentas.Aplicacion, cuentas.ClaveDeLaAplicacion);

        // Los dueños que fijan los módulos se conservan: la depuración de reclamaciones y los registros de auditoría no son de la cuenta de administración.
        Assert.Equal("reclamaciones_dueno", await Consultar<string>(propietaria, "SELECT pg_get_userbyid(nspowner) FROM pg_namespace WHERE nspname = 'reclamaciones'"));
        Assert.Equal("reclamaciones_dueno", await Consultar<string>(propietaria, "SELECT tableowner FROM pg_tables WHERE schemaname = 'reclamaciones' AND tablename = 'hojas_reclamacion'"));
        Assert.Equal("reclamaciones_dueno", await Consultar<string>(propietaria, "SELECT pg_get_userbyid(proowner) FROM pg_proc WHERE oid = 'reclamaciones.anonimizar_hojas()'::regprocedure"));
        Assert.Equal("auditoria_migraciones", await Consultar<string>(propietaria, "SELECT tableowner FROM pg_tables WHERE schemaname = 'auditoria' AND tablename = 'registros'"));
        Assert.Equal("auditoria_migraciones", await Consultar<string>(propietaria, "SELECT tableowner FROM pg_tables WHERE schemaname = 'identidad' AND tablename = 'registros'"));

        // Lo que la aplicación no puede hacer, aun con el rol del módulo: reescribir o borrar los registros de auditoría y borrar hojas de reclamación (42501 = permiso denegado).
        Assert.Equal("42501", await EstadoDeLaSentencia(acceso, "rol_auditoria", "DELETE FROM auditoria.registros"));
        Assert.Equal("42501", await EstadoDeLaSentencia(acceso, "rol_auditoria", "UPDATE auditoria.registros SET accion = accion"));
        Assert.Equal("42501", await EstadoDeLaSentencia(acceso, "rol_reclamaciones", "DELETE FROM reclamaciones.hojas_reclamacion"));

        // Los roles de depuración solo ejecutan su función; la aplicación no.
        Assert.False(await Consultar<bool>(propietaria, $"SELECT has_function_privilege('{cuentas.Aplicacion}', 'reclamaciones.anonimizar_hojas()', 'EXECUTE')"));

        // Nada queda a nombre de la cuenta de administración.
        Assert.Equal(0L, await ObjetosDeLaAdministracion(propietaria));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-04")]
    public async Task ElEsquemaMigradoDelInventario_ConservaLasGarantiasDeSeguridadDeSusModulos()
    {
        var propietaria = await instancia.NuevaBase();
        var baseDeDatos = Base(propietaria);
        var cuentas = NuevasCuentas("inv7");
        Assert.True((await MigrarElInventario(ConfiguracionDelInventario(propietaria, cuentas, baseDeDatos))).Codigo == 0);
        var acceso = Conexion(baseDeDatos, cuentas.Aplicacion, cuentas.ClaveDeLaAplicacion);

        Assert.Equal("ajustes_depuracion_dueno", await Consultar<string>(propietaria, "SELECT pg_get_userbyid(proowner) FROM pg_proc WHERE oid = 'ajustes.anonimizar_registros()'::regprocedure"));
        Assert.Equal("auditoria_migraciones", await Consultar<string>(propietaria, "SELECT tableowner FROM pg_tables WHERE schemaname = 'auditoria' AND tablename = 'registros'"));
        Assert.Equal("42501", await EstadoDeLaSentencia(acceso, "rol_auditoria", "DELETE FROM auditoria.registros"));
        Assert.Equal("42501", await EstadoDeLaSentencia(acceso, "rol_ajustes", "DELETE FROM ajustes.ajustes"));
        Assert.True(await Consultar<bool>(propietaria, $"SELECT has_function_privilege('{cuentas.DepuracionUno}', 'ajustes.anonimizar_registros()', 'EXECUTE')"));
        Assert.False(await Consultar<bool>(propietaria, $"SELECT has_function_privilege('{cuentas.Aplicacion}', 'ajustes.anonimizar_registros()', 'EXECUTE')"));
        Assert.Equal(0L, await ObjetosDeLaAdministracion(propietaria));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public async Task RepetirLaMigracion_NoDañaNadaYSoloRotaLasContrasenas()
    {
        var propietaria = await instancia.NuevaBase();
        var baseDeDatos = Base(propietaria);
        var cuentas = NuevasCuentas("eco2");
        var primera = await MigrarElEcommerce(ConfiguracionDelEcommerce(propietaria, cuentas, baseDeDatos));
        await using (var origen = NpgsqlDataSource.Create(propietaria))
        {
            await using var dato = origen.CreateCommand("SELECT 1");
            await dato.ExecuteScalarAsync();
        }

        var rotada = cuentas with { ClaveDeLaAplicacion = Clave() };
        var segunda = await MigrarElEcommerce(ConfiguracionDelEcommerce(propietaria, rotada, baseDeDatos));
        var tercera = await MigrarElEcommerce(ConfiguracionDelEcommerce(propietaria, rotada, baseDeDatos));

        Assert.True(primera.Codigo == 0, primera.Error);
        Assert.True(segunda.Codigo == 0, segunda.Error);
        Assert.True(tercera.Codigo == 0, tercera.Error);
        Assert.Equal(primera.Salida.Split('\n').Where(l => l.StartsWith("paso: ", StringComparison.Ordinal)).Where(l => !l.StartsWith("paso: cuenta", StringComparison.Ordinal)),
            tercera.Salida.Split('\n').Where(l => l.StartsWith("paso: ", StringComparison.Ordinal)).Where(l => !l.StartsWith("paso: cuenta", StringComparison.Ordinal)));

        // La contraseña vieja ya no entra y la nueva sí; un solo rol de cada nombre.
        await Assert.ThrowsAsync<PostgresException>(() => Consultar<int>(Conexion(baseDeDatos, cuentas.Aplicacion, cuentas.ClaveDeLaAplicacion), "SELECT 1"));
        Assert.Equal(1, await Consultar<int>(Conexion(baseDeDatos, cuentas.Aplicacion, rotada.ClaveDeLaAplicacion), "SELECT 1"));
        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM pg_roles WHERE rolname = 'rol_carrito'"));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public async Task LosPasosSeAplicanEnElOrdenDelPlan_YLaSalidaNoLlevaNingunSecreto()
    {
        var propietaria = await instancia.NuevaBase();
        var baseDeDatos = Base(propietaria);
        var cuentas = NuevasCuentas("eco3");

        var resultado = await MigrarElEcommerce(ConfiguracionDelEcommerce(propietaria, cuentas, baseDeDatos));

        var pasos = resultado.Salida.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => l.StartsWith("paso: ", StringComparison.Ordinal)).Select(l => l[6..].Trim()).ToList();
        var esperado = Eco.PlanDeMigracionDelEcommerce.Pasos().Select(p => p.Nombre).Prepend("propietario").Prepend("sin-registro-de-sentencias").Append("propiedad").Concat(new[] { $"cuenta {cuentas.Aplicacion}", $"cuenta {cuentas.DepuracionUno}", $"cuenta {cuentas.DepuracionDos}" }).ToList();
        Assert.Equal(esperado, pasos);
        foreach (var secreto in new[] { cuentas.ClaveDeLaAplicacion, cuentas.ClaveUno, cuentas.ClaveDos, propietaria })
        {
            Assert.DoesNotContain(secreto, resultado.Salida + resultado.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public async Task SinLaCuentaPropietariaOConUnaVariableQueFalta_FallaCerradoNombrandoLaVariableSinTocarLaBase()
    {
        var propietaria = await instancia.NuevaBase();
        var baseDeDatos = Base(propietaria);
        var cuentas = NuevasCuentas("eco4");
        foreach (var variable in new[] { "Migrador:Ecommerce:Propietaria", "Ecommerce:Base:Aplicacion", "Ecommerce:Base:DepuracionDeAuditoria", "Ecommerce:Base:DepuracionDeRegistros" })
        {
            var valores = ConfiguracionDelEcommerce(propietaria, cuentas, baseDeDatos).AsEnumerable().Where(p => p.Key != variable && p.Value is not null).ToDictionary(p => p.Key, p => p.Value);

            var resultado = await MigrarElEcommerce(new ConfigurationBuilder().AddInMemoryCollection(valores).Build());

            Assert.Equal(1, resultado.Codigo);
            Assert.Contains("BABELHUB__" + variable.Replace(":", "__", StringComparison.Ordinal), resultado.Error, StringComparison.Ordinal);
            Assert.Empty(resultado.Salida);
        }

        Assert.Equal(0L, await Consultar<long>(propietaria, "SELECT count(*) FROM pg_namespace WHERE nspname IN ('plataforma', 'auditoria', 'carrito')"));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public async Task UnaCuentaQueNoEsAdministradora_ONoDifiereDeLasDelServicio_NoMigra()
    {
        var propietaria = await instancia.NuevaBase();
        var baseDeDatos = Base(propietaria);
        var cuentas = NuevasCuentas("eco5");
        Assert.True((await MigrarElEcommerce(ConfiguracionDelEcommerce(propietaria, cuentas, baseDeDatos))).Codigo == 0);

        // La cuenta de la aplicación como propietaria: se rechaza antes de abrirla por no distinguirse de las del servicio, y una cuenta común sin administración se rechaza al abrirla.
        var comoAplicacion = ConfiguracionDelEcommerce(Conexion(baseDeDatos, cuentas.Aplicacion, cuentas.ClaveDeLaAplicacion), cuentas, baseDeDatos);
        var comun = "comun_" + Guid.NewGuid().ToString("N")[..6];
        var claveComun = Clave();
        await using (var admin = NpgsqlDataSource.Create(propietaria))
        {
            await using var crear = admin.CreateCommand($"CREATE ROLE {comun} LOGIN NOSUPERUSER PASSWORD '{claveComun}'");
            await crear.ExecuteNonQueryAsync();
        }

        var sinAdministracion = ConfiguracionDelEcommerce(Conexion(baseDeDatos, comun, claveComun), cuentas, baseDeDatos);

        var mismaCuenta = await MigrarElEcommerce(comoAplicacion);
        var noAdministra = await MigrarElEcommerce(sinAdministracion);

        Assert.Equal(1, mismaCuenta.Codigo);
        Assert.Contains("no puede ser una de las cuentas del servicio", mismaCuenta.Error, StringComparison.Ordinal);
        Assert.Equal(1, noAdministra.Codigo);
        Assert.Contains("cuenta propietaria", noAdministra.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(cuentas.ClaveDeLaAplicacion, mismaCuenta.Error + noAdministra.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public async Task UnRolQueNingunPasoCreo_DetieneLaMigracionNombrandoloYSinDejarLaCuentaAMedias()
    {
        var propietaria = await instancia.NuevaBase();
        var baseDeDatos = Base(propietaria);
        var plan = new Eco.PlanDeMigracion(
            "ecommerce",
            [new Eco.PasoDeMigracion("vacio", "SELECT 1")],
            [],
            [new Eco.CuentaDeBase("cuenta_sin_rol", Clave(), ["rol_que_no_existe"])]);
        await using var origen = NpgsqlDataSource.Create(propietaria);
        var salida = new StringWriter();

        var falla = await Assert.ThrowsAsync<Eco.MigracionFallidaException>(() => new Eco.MigradorDeLaBase(origen, salida).Ejecutar(plan));

        Assert.Contains("rol_que_no_existe", falla.Message, StringComparison.Ordinal);
        Assert.Equal(0L, await Consultar<long>(propietaria, "SELECT count(*) FROM pg_roles WHERE rolname = 'cuenta_sin_rol'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public async Task UnaFallaAlPonerLaContrasena_NoDejaLaClaveEnElRegistroDelServidor()
    {
        var propietaria = await instancia.NuevaBase();
        var clave = Clave();
        // «postgres» es el superusuario de arranque: PostgreSQL rechaza quitarle el privilegio, así que el paso de la cuenta falla después de armar la sentencia con la contraseña.
        var plan = new Eco.PlanDeMigracion("ecommerce", [new Eco.PasoDeMigracion("vacio", "SELECT 1")], [], [new Eco.CuentaDeBase("postgres", clave, [])]);
        await using var origen = NpgsqlDataSource.Create(propietaria);

        await Assert.ThrowsAsync<Eco.MigracionFallidaException>(() => new Eco.MigradorDeLaBase(origen, new StringWriter()).Ejecutar(plan));

        var (salida, errores) = await instancia.RegistroDelServidor();
        Assert.DoesNotContain(clave, salida + errores, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public async Task SiLaCuentaPropietariaNoPuedeConectar_SaleSinTrazaNiTexto()
    {
        var cuentas = NuevasCuentas("eco6");
        var configuracion = ConfiguracionDelEcommerce(instancia.Cadena("base_que_no_existe", "postgres", Clave()), cuentas, "base_que_no_existe");

        var resultado = await MigrarElEcommerce(configuracion);

        Assert.Equal(1, resultado.Codigo);
        Assert.Contains("migracion: No se pudo abrir la cuenta propietaria", resultado.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", resultado.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public async Task UnPasoQueFalla_SeRevierteEntero_YElErrorNombraElPasoSinElSql()
    {
        var propietaria = await instancia.NuevaBase();
        var plan = new Eco.PlanDeMigracion(
            "ecommerce",
            [new Eco.PasoDeMigracion("bueno", "CREATE SCHEMA IF NOT EXISTS probado;"), new Eco.PasoDeMigracion("roto", "CREATE SCHEMA a_medias; CREATE TABLE a_medias.t (id int); SELECT 1/0;")],
            [],
            []);
        await using var origen = NpgsqlDataSource.Create(propietaria);

        var falla = await Assert.ThrowsAsync<Eco.MigracionFallidaException>(() => new Eco.MigradorDeLaBase(origen, new StringWriter()).Ejecutar(plan));

        Assert.Contains("«roto»", falla.Message, StringComparison.Ordinal);
        Assert.Contains("22012", falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("a_medias", falla.Message, StringComparison.Ordinal);
        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM pg_namespace WHERE nspname = 'probado'"));
        Assert.Equal(0L, await Consultar<long>(propietaria, "SELECT count(*) FROM pg_namespace WHERE nspname = 'a_medias'"));
    }
}
