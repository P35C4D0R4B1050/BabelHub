using BabelHub.Ecommerce.Infrastructure.Auditoria;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Identidad;
using BabelHub.IntegrationTests.Auditoria;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// PostgreSQL real con la migración de <c>Auditoria</c> y la del esquema <c>identidad</c> (<see cref="MigracionDelEsquemaDeIdentidad.Completa"/>): el rol de acceso de la aplicación es <c>NOINHERIT</c> y solo
/// opera el esquema con <c>SET LOCAL ROLE rol_identidad</c> dentro de una transacción. Las credenciales se generan distintas en cada ejecución.
/// </summary>
public sealed class PostgresDeIdentidadFixture : IAsyncLifetime
{
    private static readonly string ClaveDeAcceso = "acceso-" + Guid.NewGuid().ToString("N");

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pruebas")
        .WithUsername("postgres")
        .WithPassword("admin-" + Guid.NewGuid().ToString("N"))
        .Build();

    public NpgsqlDataSource Administracion { get; private set; } = null!;

    /// <summary>La aplicación: un rol <c>NOINHERIT</c> miembro solo de los roles de esquema.</summary>
    public NpgsqlDataSource Acceso { get; private set; } = null!;

    /// <summary>La unidad de trabajo real de la aplicación (rol de acceso <c>NOINHERIT</c> y <c>EjecutarComo</c>).</summary>
    public UnidadDeTrabajoPostgres Unidad { get; private set; } = null!;

    public AccesoAlEsquemaDeIdentidad AccesoAlEsquema { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());
        await Ejecutar(DefinicionDeAuditoria.MigracionCompleta(PostgresDeAuditoriaFixture.Catalogo));
        await Ejecutar(MigracionDelEsquemaDeIdentidad.Completa());
        await Ejecutar($"CREATE ROLE acceso LOGIN NOINHERIT PASSWORD '{ClaveDeAcceso}'; GRANT rol_auditoria, rol_identidad TO acceso;");
        var clave = ClaveDeAcceso;
        Acceso = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()) { Username = "acceso", Password = clave }.ConnectionString);
        Unidad = new UnidadDeTrabajoPostgres(Acceso);
        AccesoAlEsquema = new AccesoAlEsquemaDeIdentidad(Unidad, Unidad);
    }

    public async Task DisposeAsync()
    {
        foreach (var origen in new[] { Acceso, Administracion })
        {
            if (origen is not null)
            {
                await origen.DisposeAsync();
            }
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
}

/// <summary>
/// El esquema <c>identidad</c> (identidad-seguridad T30; AC-IDE-05, AC-IDE-37; IDE-06, IDE-40): sin <c>EjecutarComo</c> el acceso falla, el rol del esquema hace solo lo que necesita, las restricciones respaldan las reglas
/// de unicidad (correo, un encargado activo por sucursal, una sola clave activa, una cancelación abierta) y los registros aislados son inmutables.
/// </summary>
public sealed class EsquemaDeIdentidadTests(PostgresDeIdentidadFixture bd) : IClassFixture<PostgresDeIdentidadFixture>
{
    private static string Usuario(Guid? id = null, string correo = "ana@example.com", string rol = "cliente", string sucursal = "NULL", bool activo = true) =>
        $"INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, sucursal, activo, creada_en) VALUES ('{id ?? Guid.NewGuid()}', 'Ana', '{correo}', true, '{rol}', {sucursal}, {activo.ToString().ToLowerInvariant()}, now())";

    private Task<PostgresException?> IntentarAsync(string? rol, string sql) => PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, rol, sql);

    [Fact]
    public async Task SinElRolDelEsquema_LaAplicacionNoPuedeLeerNiEscribirIdentidad()
    {
        foreach (var tabla in new[] { "usuarios", "renovaciones", "claves_de_firma", "segundo_factor", "registros", "bajas_titulares" })
        {
            Assert.Equal("42501", (await IntentarAsync(null, $"SELECT * FROM identidad.{tabla}"))?.SqlState);
        }

        Assert.Equal("42501", (await IntentarAsync(null, Usuario()))?.SqlState);
    }

    [Fact]
    public async Task ElRolDelEsquema_EscribeYLeeSusTablas()
    {
        var id = Guid.NewGuid();

        Assert.Null(await IntentarAsync("rol_identidad", Usuario(id, "rol-ok@example.com")));
        Assert.Null(await IntentarAsync("rol_identidad", $"UPDATE identidad.usuarios SET nombre = 'Ana María' WHERE id = '{id}'"));
        Assert.Equal("Ana María", await bd.Consultar<string>($"SELECT nombre FROM identidad.usuarios WHERE id = '{id}'"));
    }

    [Fact]
    public async Task ElRolDelEsquema_NoPuedeVaciarNiAlterarLaEstructuraNiBorrarLoQueSeConserva()
    {
        Assert.Equal("42501", (await IntentarAsync("rol_identidad", "TRUNCATE identidad.usuarios CASCADE"))?.SqlState);
        Assert.Equal("42501", (await IntentarAsync("rol_identidad", "ALTER TABLE identidad.usuarios ADD COLUMN extra text"))?.SqlState);
        Assert.Equal("42501", (await IntentarAsync("rol_identidad", "DROP TABLE identidad.usuarios"))?.SqlState);
        Assert.Equal("42501", (await IntentarAsync("rol_identidad", "DELETE FROM identidad.claves_de_firma"))?.SqlState);
        Assert.Equal("42501", (await IntentarAsync("rol_identidad", "DELETE FROM identidad.bajas_titulares"))?.SqlState);
        Assert.Equal("42501", (await IntentarAsync("rol_identidad", "DELETE FROM identidad.solicitudes_de_datos"))?.SqlState);
        Assert.Equal("42501", (await IntentarAsync("rol_identidad", "SELECT * FROM auditoria.alertas"))?.SqlState);
    }

    [Fact]
    public async Task ElRolDelEsquema_NoPuedeEscribirLosTextosVigentes_NiAlterarLaClavePublicaOPrivadaDeUnaClaveExistente()
    {
        Assert.Equal("42501", (await IntentarAsync("rol_identidad", "INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso) VALUES (1, 'a', 'b', 'c')"))?.SqlState);
        Assert.Equal("42501", (await IntentarAsync("rol_identidad", "UPDATE identidad.claves_de_firma SET jwk_publico = repeat('x', 60)"))?.SqlState);
        Assert.Equal("42501", (await IntentarAsync("rol_identidad", "UPDATE identidad.claves_de_firma SET privada_cifrada = decode(repeat('aa', 40), 'hex')"))?.SqlState);
        Assert.Null(await IntentarAsync("rol_identidad", "UPDATE identidad.claves_de_firma SET estado = estado WHERE false"));
    }

    [Fact]
    public async Task LaMigracionRetiraLosPermisosDeMas_QueUnDespliegueAnteriorHayaConcedido()
    {
        await bd.Ejecutar("GRANT DELETE, TRUNCATE ON identidad.claves_de_firma TO rol_identidad");
        Assert.Null(await IntentarAsync("rol_identidad", "DELETE FROM identidad.claves_de_firma WHERE false"));

        await bd.Ejecutar(MigracionDelEsquemaDeIdentidad.Completa());

        Assert.Equal("42501", (await IntentarAsync("rol_identidad", "DELETE FROM identidad.claves_de_firma WHERE false"))?.SqlState);
        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM information_schema.role_table_grants WHERE grantee = 'rol_identidad' AND table_schema = 'identidad' AND privilege_type IN ('TRUNCATE', 'REFERENCES', 'TRIGGER')"));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-37")]
    public async Task ElResumenDeLaContrasena_DebeTenerElFormatoPHCCompleto()
    {
        var id = Guid.NewGuid();
        await bd.Ejecutar(Usuario(id, "phc@example.com"));

        Assert.Equal("23514", (await IntentarAsync("rol_identidad", $"UPDATE identidad.usuarios SET hash_contrasena = '$argon2id$hunter2' WHERE id = '{id}'"))?.SqlState);
        Assert.Null(await IntentarAsync("rol_identidad", $"UPDATE identidad.usuarios SET hash_contrasena = '$argon2id$v=19$m=19456,t=2,p=1$c2FsLXNhbC1zYWwtc2FsYQ$c2FsLXNhbC1zYWwtc2Fsc2FsLXNhbC1zYWwtc2Fsc2E' WHERE id = '{id}'"));
    }

    [Fact]
    public async Task LaMigracionEsIdempotente_YLasTablasTienenSuPropietario()
    {
        await bd.Ejecutar(MigracionDelEsquemaDeIdentidad.Completa());

        foreach (var tabla in new[] { "usuarios", "segundo_factor", "renovaciones", "claves_de_firma", "bajas_titulares", "registros", "por_anonimizar" })
        {
            Assert.Equal("auditoria_migraciones", await bd.Consultar<string>($"SELECT tableowner FROM pg_tables WHERE schemaname = 'identidad' AND tablename = '{tabla}'"));
        }
    }

    [Fact]
    public async Task ElCorreoEsUnico_YSoloSeGuardaNormalizado()
    {
        await bd.Ejecutar(Usuario(correo: "unico@example.com"));

        Assert.Equal("23505", (await IntentarAsync("rol_identidad", Usuario(correo: "unico@example.com")))?.SqlState);
        Assert.Equal("23514", (await IntentarAsync("rol_identidad", Usuario(correo: "Mayusculas@Example.com")))?.SqlState);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-05")]
    public async Task LaSucursalEsDelEncargadoYSoloSuya_YHayUnSoloEncargadoActivoPorSucursal()
    {
        var sucursal = Guid.NewGuid();
        await bd.Ejecutar(Usuario(correo: "encargado1@example.com", rol: "encargado-de-sucursal", sucursal: $"'{sucursal}'"));

        Assert.Equal("23514", (await IntentarAsync("rol_identidad", Usuario(correo: "sinsucursal@example.com", rol: "encargado-de-sucursal")))?.SqlState);
        Assert.Equal("23514", (await IntentarAsync("rol_identidad", Usuario(correo: "cliente-con-sucursal@example.com", sucursal: $"'{sucursal}'")))?.SqlState);
        Assert.Equal("23505", (await IntentarAsync("rol_identidad", Usuario(correo: "encargado2@example.com", rol: "encargado-de-sucursal", sucursal: $"'{sucursal}'")))?.SqlState);
        Assert.Null(await IntentarAsync("rol_identidad", Usuario(correo: "encargado3@example.com", rol: "encargado-de-sucursal", sucursal: $"'{sucursal}'", activo: false)));
    }

    [Fact]
    public async Task UnRolDesconocido_UnaContrasenaQueNoEsArgon2id_YUnNumeroQueNoEsE164_SeRechazan()
    {
        Assert.Equal("23514", (await IntentarAsync("rol_identidad", Usuario(correo: "rol@example.com", rol: "superusuario")))?.SqlState);
        var id = Guid.NewGuid();
        await bd.Ejecutar(Usuario(id, "chequeos@example.com"));
        Assert.Equal("23514", (await IntentarAsync("rol_identidad", $"UPDATE identidad.usuarios SET hash_contrasena = 'texto-en-claro' WHERE id = '{id}'"))?.SqlState);
        Assert.Equal("23514", (await IntentarAsync("rol_identidad", $"UPDATE identidad.usuarios SET numero_whatsapp = '999111222' WHERE id = '{id}'"))?.SqlState);
        Assert.Equal("23514", (await IntentarAsync("rol_identidad", $"UPDATE identidad.usuarios SET consentimiento_whatsapp = true WHERE id = '{id}'"))?.SqlState);
        Assert.Null(await IntentarAsync("rol_identidad", $"UPDATE identidad.usuarios SET numero_whatsapp = '+51999111222', consentimiento_whatsapp = true WHERE id = '{id}'"));
    }

    [Fact]
    public async Task LaClaveDeFirma_TieneUnaSolaActiva_YLaComprometidaLlevaSuFecha()
    {
        const string jwk = "{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"y\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}";
        string Clave(string kid, string estado, string comprometida = "NULL") =>
            $"INSERT INTO identidad.claves_de_firma (kid, creada_en, estado, comprometida_en, jwk_publico, privada_cifrada) VALUES ('{kid}', now(), '{estado}', {comprometida}, '{jwk}', decode('{new string('a', 80)}', 'hex'))";

        Assert.Null(await IntentarAsync("rol_identidad", Clave("kid-activa-0001", "activa")));
        Assert.Equal("23505", (await IntentarAsync("rol_identidad", Clave("kid-activa-0002", "activa")))?.SqlState);
        Assert.Null(await IntentarAsync("rol_identidad", Clave("kid-anterior-01", "anterior")));
        Assert.Equal("23514", (await IntentarAsync("rol_identidad", Clave("kid-comprom-001", "comprometida")))?.SqlState);
        Assert.Null(await IntentarAsync("rol_identidad", Clave("kid-comprom-002", "comprometida", "now()")));
    }

    [Fact]
    public async Task UnaCancelacionAbiertaPorUsuario_YUnCodigoVigentePorPropositoYUnCambioDeCorreoPendiente()
    {
        var usuario = Guid.NewGuid();
        await bd.Ejecutar(Usuario(usuario, "unicidades@example.com"));
        string Cancelacion(string estado) => $"INSERT INTO identidad.solicitudes_de_cancelacion (id, usuario_id, estado, solicitada_en) VALUES ('{Guid.NewGuid()}', '{usuario}', '{estado}', now())";
        string Codigo(string extra = "") =>
            $"INSERT INTO identidad.codigos_de_un_solo_uso (id, usuario_id, proposito, huella, creado_en, vence_en{(extra.Length > 0 ? ", " + extra.Split('=')[0] : string.Empty)}) VALUES ('{Guid.NewGuid()}', '{usuario}', 'recuperacion-de-acceso', '{new string('a', 64)}', now(), now() + interval '10 minutes'{(extra.Length > 0 ? ", " + extra.Split('=')[1] : string.Empty)})";
        string Cambio() => $"INSERT INTO identidad.cambios_de_correo_pendientes (id, usuario_id, correo_nuevo, creado_en, vence_en, estado) VALUES ('{Guid.NewGuid()}', '{usuario}', 'nuevo@example.com', now(), now() + interval '1 day', 'esperando-verificacion')";

        Assert.Null(await IntentarAsync("rol_identidad", Cancelacion("diferida")));
        Assert.Equal("23505", (await IntentarAsync("rol_identidad", Cancelacion("esperando-reconfirmacion")))?.SqlState);
        Assert.Null(await IntentarAsync("rol_identidad", Cancelacion("descartada")));

        Assert.Null(await IntentarAsync("rol_identidad", Codigo()));
        Assert.Equal("23505", (await IntentarAsync("rol_identidad", Codigo()))?.SqlState);
        Assert.Null(await IntentarAsync("rol_identidad", "UPDATE identidad.codigos_de_un_solo_uso SET invalidado = true WHERE invalidado = false AND usuario_id = '" + usuario + "'"));
        Assert.Null(await IntentarAsync("rol_identidad", Codigo()));

        Assert.Null(await IntentarAsync("rol_identidad", Cambio()));
        Assert.Equal("23505", (await IntentarAsync("rol_identidad", Cambio()))?.SqlState);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-37")]
    public async Task LosSecretosSoloSeGuardanCifradosOComoHuella()
    {
        var usuario = Guid.NewGuid();
        await bd.Ejecutar(Usuario(usuario, "huellas@example.com"));

        Assert.Equal("23514", (await IntentarAsync("rol_identidad", $"INSERT INTO identidad.renovaciones (huella, usuario_id, familia, emitida_en, expira_absoluta) VALUES ('valor-opaco-en-claro', '{usuario}', '{Guid.NewGuid()}', now(), now() + interval '1 day')"))?.SqlState);
        Assert.Equal("23514", (await IntentarAsync("rol_identidad", $"INSERT INTO identidad.segundo_factor (usuario_id, secreto_cifrado, estado) VALUES ('{usuario}', decode('0102', 'hex'), 'sin-confirmar')"))?.SqlState);
        Assert.Null(await IntentarAsync("rol_identidad", $"INSERT INTO identidad.renovaciones (huella, usuario_id, familia, emitida_en, expira_absoluta) VALUES ('{new string('b', 64)}', '{usuario}', '{Guid.NewGuid()}', now(), now() + interval '1 day')"));
        Assert.Null(await IntentarAsync("rol_identidad", $"INSERT INTO identidad.segundo_factor (usuario_id, secreto_cifrado, estado) VALUES ('{usuario}', decode('{new string('c', 80)}', 'hex'), 'sin-confirmar')"));
    }

    [Fact]
    public async Task LosRegistrosAislados_SonDeSoloInsercion_AunParaElRolDelEsquema()
    {
        var id = Guid.NewGuid();

        Assert.Null(await IntentarAsync("rol_identidad", RegistrosInmutablesTests.Insertar("identidad", id)));
        Assert.Equal("42501", (await IntentarAsync("rol_identidad", $"UPDATE identidad.registros SET motivo = 'cambiado' WHERE id = '{id}'"))?.SqlState);
        Assert.Equal("42501", (await IntentarAsync("rol_identidad", $"DELETE FROM identidad.registros WHERE id = '{id}'"))?.SqlState);
        Assert.Equal("42501", (await IntentarAsync("rol_identidad", "TRUNCATE identidad.registros"))?.SqlState);
        Assert.NotNull(await bd.Consultar<string>($"SELECT accion FROM identidad.registros WHERE id = '{id}'"));

        // Ni el superusuario puede alterar un registro por los disparadores (salvo la función de depuración).
        var error = await bd.Administracion.CreateCommand($"UPDATE identidad.registros SET accion = 'OtraAccion' WHERE id = '{id}'").ExecuteNonQueryAsync().ContinueWith(t => t.Exception?.InnerException as PostgresException);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task LaPorAnonimizarEsDeIdentidad_ElRolSoloInsertaYLaDepuracionPuedeLeerla()
    {
        var cuenta = Guid.NewGuid();

        Assert.Null(await IntentarAsync("rol_identidad", $"INSERT INTO identidad.por_anonimizar (identificador_opaco, tipo, registrado_en) VALUES ('{cuenta}', 'cuenta_cancelada', now())"));
        Assert.Equal("42501", (await IntentarAsync("rol_identidad", $"DELETE FROM identidad.por_anonimizar WHERE identificador_opaco = '{cuenta}'"))?.SqlState);
        Assert.Equal("42501", (await IntentarAsync("rol_identidad", "UPDATE identidad.por_anonimizar SET procesado_en = now()"))?.SqlState);
    }
}
