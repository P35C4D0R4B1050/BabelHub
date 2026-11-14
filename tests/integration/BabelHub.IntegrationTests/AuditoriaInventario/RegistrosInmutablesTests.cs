using BabelHub.Inventario.Infrastructure.Auditoria;
using Npgsql;

namespace BabelHub.IntegrationTests.AuditoriaInventario;

/// <summary>
/// Las migraciones y los roles del módulo (T22 y T23; AUD-01, AUD-03, AUD-07; AC-AUD-02): los registros son de solo inserción contra <c>auditoria.registros</c> <b>e</b> <c>identidad.registros</c>, con
/// PostgreSQL real, los roles reales y los disparadores reales: la aplicación no puede modificar, borrar, vaciar ni alterar la estructura, ni siquiera el propietario de las tablas, y no puede
/// hacerse pasar por los propietarios.
/// </summary>
public sealed class RegistrosInmutablesTests(PostgresDeAuditoriaFixture bd) : IClassFixture<PostgresDeAuditoriaFixture>
{
    public static TheoryData<string, string> Esquemas => new()
    {
        { "auditoria", "rol_auditoria" },
        { "identidad", "rol_identidad" },
    };

    internal static string Insertar(string esquema, Guid? id = null, string accion = "AjusteRegistrado", string? correlacion = null, string? motivo = null, string detalle = "{}", string extra = "") =>
        "INSERT INTO " + esquema + ".registros (id, servicio, tipo, actor_tipo, actor_id, rol, instante_utc, accion, objeto_tipo, objeto_id, motivo, origen, detalle, claves_personales, resultado, truncado, correlacion) "
        + $"VALUES ('{id ?? Guid.NewGuid()}', 'ecommerce', 'auditoria', 'usuario', '{Guid.NewGuid()}', 'administrador-central', now(), '{accion}', 'ajuste', '{Guid.NewGuid():D}', "
        + (motivo is null ? "NULL" : $"'{motivo}'")
        + ", 'v1:abc12345', '" + detalle + "'::jsonb, '{}', 'exitoso', false, "
        + (correlacion is null ? "NULL" : $"'{correlacion}'")
        + ") " + extra;

    // ---------- T22: la forma de las tablas ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-06")]
    public async Task LaMigracionCreaLasTablasDelModuloConSuPropietarioDeMigraciones()
    {
        foreach (var tabla in new[] { "registros", "alertas", "acumulado_de_limites", "por_anonimizar", "alertas_ya_enviadas", "marca_de_lectura_de_alertas", "estado_de_depuracion", "politica_de_anonimizacion" })
        {
            Assert.Equal("auditoria_migraciones", await bd.Consultar<string>($"SELECT tableowner FROM pg_tables WHERE schemaname = 'auditoria' AND tablename = '{tabla}'"));
        }

        Assert.Equal("auditoria_migraciones", await bd.Consultar<string>("SELECT tableowner FROM pg_tables WHERE schemaname = 'identidad' AND tablename = 'registros'"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-06")]
    public async Task LaMigracionEsIdempotente()
    {
        await bd.Ejecutar(DefinicionDeAuditoria.Roles + DefinicionDeAuditoria.Tablas + DefinicionDeAuditoria.RegistrosInmutables("auditoria") + DefinicionDeAuditoria.PoliticaDesdeElCatalogo(PostgresDeAuditoriaFixture.Catalogo));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-06")]
    public async Task LosIndicesDeConsultaYElDeTextoEstan()
    {
        var indices = new HashSet<string>();
        await using var comando = bd.Administracion.CreateCommand("SELECT indexname FROM pg_indexes WHERE schemaname = 'auditoria' AND tablename = 'registros'");
        await using var lector = await comando.ExecuteReaderAsync();
        while (await lector.ReadAsync())
        {
            indices.Add(lector.GetString(0));
        }

        Assert.Contains("registros_por_instante", indices);
        Assert.Contains("registros_por_texto", indices);
        Assert.Contains("registros_un_acceso_denegado_por_correlacion", indices);
        Assert.Contains("registros_de_umbral", indices);
        Assert.Equal("gin", await bd.Consultar<string>("SELECT am.amname FROM pg_class c JOIN pg_am am ON am.oid = c.relam WHERE c.relname = 'registros_por_texto' AND c.relnamespace = 'auditoria'::regnamespace"));
    }

    [Theory]
    [MemberData(nameof(Esquemas))]
    [Trait("Criterio", "AC-AUD-06")]
    public async Task LaSecuenciaLaAsignaLaBaseYSoloCrece(string esquema, string rol)
    {
        await bd.EjecutarComoLaAplicacion(rol, Insertar(esquema));
        await bd.EjecutarComoLaAplicacion(rol, Insertar(esquema));

        var secuencias = new List<long>();
        await using var comando = bd.Administracion.CreateCommand($"SELECT secuencia FROM {esquema}.registros ORDER BY secuencia");
        await using var lector = await comando.ExecuteReaderAsync();
        while (await lector.ReadAsync())
        {
            secuencias.Add(lector.GetInt64(0));
        }

        Assert.True(secuencias.Count >= 2);
        Assert.Equal(secuencias.Order(), secuencias);
        Assert.Equal(secuencias.Count, secuencias.Distinct().Count());

        // La aplicación no elige la secuencia.
        var error = await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, rol, Insertar(esquema).Replace("INSERT INTO " + esquema + ".registros (id,", "INSERT INTO " + esquema + ".registros (secuencia, id,", StringComparison.Ordinal).Replace("VALUES ('", "VALUES (1, '", StringComparison.Ordinal));
        Assert.NotNull(error);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-21")]
    public async Task ElIndiceUnicoParcialDejaUnSoloAccesoDenegadoPorServicioCorrelacionYMotivo_PeroNoLimitaLasDemasAcciones()
    {
        var correlacion = "corr" + Guid.NewGuid().ToString("N")[..20];
        await bd.EjecutarComoLaAplicacion("rol_auditoria", Insertar("auditoria", accion: "AccesoDenegado", correlacion: correlacion, motivo: "fuera-de-rol"));

        var repetido = await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, "rol_auditoria", Insertar("auditoria", accion: "AccesoDenegado", correlacion: correlacion, motivo: "fuera-de-rol"));
        Assert.Equal("23505", repetido?.SqlState);

        // El mismo intento con ON CONFLICT DO NOTHING no escribe otro evento ni falla (así lo escribe la Infraestructura).
        Assert.Null(await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, "rol_auditoria", Insertar("auditoria", accion: "AccesoDenegado", correlacion: correlacion, motivo: "fuera-de-rol", extra: "ON CONFLICT DO NOTHING")));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE correlacion = '{correlacion}'"));

        // Otro motivo o otra acción con la misma correlación sí se registran.
        await bd.EjecutarComoLaAplicacion("rol_auditoria", Insertar("auditoria", accion: "AccesoDenegado", correlacion: correlacion, motivo: "recurso-ajeno"));
        await bd.EjecutarComoLaAplicacion("rol_auditoria", Insertar("auditoria", accion: "AjusteRegistrado", correlacion: correlacion));
        await bd.EjecutarComoLaAplicacion("rol_auditoria", Insertar("auditoria", accion: "AjusteRegistrado", correlacion: correlacion));
        Assert.Equal(4L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE correlacion = '{correlacion}'"));
    }

    [Theory]
    [Trait("Criterio", "AC-AUD-05")]
    [InlineData("accion", "'minuscula'")]
    [InlineData("ruta", "'/pedidos/123?token=abc'")]
    [InlineData("ruta", "'https://externo/ruta'")]
    [InlineData("origen", "'con espacios'")]
    [InlineData("resultado", "'quizas'")]
    [InlineData("actor_tipo", "'otro'")]
    [InlineData("rol", "'Administrador Central'")]
    [InlineData("objeto_id", "'con;punto-y-coma'")]
    public async Task LasRestriccionesDeLaBaseRechazanFormasQueElDominioNuncaProduce(string columna, string valor)
    {
        var columnas = new Dictionary<string, string>
        {
            ["id"] = $"'{Guid.NewGuid()}'",
            ["servicio"] = "'ecommerce'",
            ["tipo"] = "'auditoria'",
            ["actor_tipo"] = "'sistema'",
            ["instante_utc"] = "now()",
            ["accion"] = "'AjusteRegistrado'",
            ["resultado"] = "'exitoso'",
        };
        columnas[columna] = valor;
        var insercion = $"INSERT INTO auditoria.registros ({string.Join(", ", columnas.Keys)}) VALUES ({string.Join(", ", columnas.Values)})";

        var error = await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, "rol_auditoria", insercion);

        Assert.Equal("23514", error?.SqlState);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-24")]
    public async Task ElDetalleDeMasDeCuatroMilCaracteresOQueNoEsUnObjetoSeRechaza()
    {
        var largo = "{\"a\":\"" + new string('x', 4100) + "\"}";

        Assert.Equal("23514", (await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, "rol_auditoria", Insertar("auditoria", detalle: largo)))?.SqlState);
        Assert.Equal("23514", (await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, "rol_auditoria", Insertar("auditoria", detalle: "[1,2]")))?.SqlState);
    }

    // ---------- T23: inmutabilidad y roles ----------

    [Theory]
    [MemberData(nameof(Esquemas))]
    [Trait("Criterio", "AC-AUD-02")]
    public async Task LaAplicacionPuedeInsertarYLeerPeroNoModificarNiBorrarNiVaciarNiAlterar(string esquema, string rol)
    {
        var id = Guid.NewGuid();
        await bd.EjecutarComoLaAplicacion(rol, Insertar(esquema, id));
        Assert.Null(await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, rol, $"SELECT count(*) FROM {esquema}.registros WHERE id = '{id}'"));

        foreach (var intento in new[]
        {
            $"UPDATE {esquema}.registros SET accion = 'Otra' WHERE id = '{id}'",
            $"UPDATE {esquema}.registros SET motivo = 'x-y' WHERE id = '{id}'",
            $"DELETE FROM {esquema}.registros WHERE id = '{id}'",
            $"TRUNCATE {esquema}.registros",
            $"ALTER TABLE {esquema}.registros ADD COLUMN extra text",
            $"ALTER TABLE {esquema}.registros DROP COLUMN detalle",
            $"DROP TABLE {esquema}.registros",
            $"ALTER TABLE {esquema}.registros DISABLE TRIGGER ALL",
            $"ALTER TABLE {esquema}.registros DISABLE TRIGGER registros_solo_insercion_por_fila",
            $"DROP TRIGGER registros_solo_insercion_por_fila ON {esquema}.registros",
            $"ALTER TABLE {esquema}.registros OWNER TO {rol}",
            $"CREATE OR REPLACE FUNCTION {esquema}.rechazar_modificacion_de_registros() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NEW; END $$",
        })
        {
            var error = await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, rol, intento);
            Assert.True(error is not null, intento);
            Assert.Contains(error!.SqlState, new[] { "42501", "42P01" });
        }

        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM {esquema}.registros WHERE id = '{id}' AND accion = 'AjusteRegistrado'"));
    }

    [Theory]
    [MemberData(nameof(Esquemas))]
    [Trait("Criterio", "AC-AUD-02")]
    public async Task NiElPropietarioDeLasTablasPuedeModificarNiBorrarNiVaciar_LosDisparadoresLoRechazan(string esquema, string rol)
    {
        var id = Guid.NewGuid();
        await bd.EjecutarComoLaAplicacion(rol, Insertar(esquema, id));

        foreach (var intento in new[]
        {
            $"UPDATE {esquema}.registros SET motivo = 'x-y' WHERE id = '{id}'",
            $"UPDATE {esquema}.registros SET actor_id = NULL WHERE id = '{id}'",
            $"DELETE FROM {esquema}.registros WHERE id = '{id}'",
            $"TRUNCATE {esquema}.registros",
        })
        {
            // El superusuario con SET ROLE al propietario: tiene todos los privilegios, solo lo detienen los disparadores.
            var error = await PostgresDeAuditoriaFixture.IntentarComo(bd.Administracion, "auditoria_migraciones", intento);
            Assert.Equal("42501", error?.SqlState);
            Assert.Contains("solo inserción", error!.MessageText, StringComparison.Ordinal);
        }

        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM {esquema}.registros WHERE id = '{id}' AND motivo IS NULL AND actor_id IS NOT NULL"));
    }

    [Theory]
    [MemberData(nameof(Esquemas))]
    [Trait("Criterio", "AC-AUD-02")]
    public async Task LosDisparadoresSeAplicanAunqueSeIntenteSaltarlosConLaReplicacion(string esquema, string rol)
    {
        var estado = await bd.Consultar<char>($"SELECT tgenabled FROM pg_trigger WHERE tgrelid = '{esquema}.registros'::regclass AND tgname = 'registros_solo_insercion_por_fila'");

        Assert.Equal('A', estado);
        Assert.Equal('A', await bd.Consultar<char>($"SELECT tgenabled FROM pg_trigger WHERE tgrelid = '{esquema}.registros'::regclass AND tgname = 'registros_solo_insercion_por_sentencia'"));
        _ = rol;
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-02")]
    public async Task LaAplicacionNoEsMiembroDeLosPropietariosNiDelRolDeDepuracion_YNoPuedeHacerseNiCrearFunciones()
    {
        foreach (var pertenencia in new[] { "auditoria_migraciones", "auditoria_depuracion_dueno", "auditoria_depuracion" })
        {
            Assert.False(await bd.Consultar<bool>($"SELECT pg_has_role('acceso', '{pertenencia}', 'MEMBER')"));
            var error = await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, null, $"SET ROLE \"{pertenencia}\"");
            Assert.Equal("42501", error?.SqlState);
        }

        Assert.Equal("42501", (await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, "rol_auditoria", "CREATE FUNCTION auditoria.imitar() RETURNS int LANGUAGE sql AS 'SELECT 1'"))?.SqlState);
        Assert.Equal("42501", (await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, null, "SELECT auditoria.anonimizar_registros()"))?.SqlState);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-02")]
    public async Task ElRolDeDepuracionSoloEjecuta_NoLeeNiEscribeNiPasaIdentificadores()
    {
        Assert.Null(await PostgresDeAuditoriaFixture.IntentarComo(bd.Depuracion, null, "SELECT auditoria.anonimizar_registros()"));

        foreach (var intento in new[]
        {
            "SELECT * FROM auditoria.registros",
            "INSERT INTO auditoria.por_anonimizar (identificador_opaco, tipo, registrado_en) VALUES (gen_random_uuid(), 'cuenta_cancelada', now())",
            "UPDATE auditoria.registros SET actor_id = NULL",
            "DELETE FROM auditoria.por_anonimizar",
            "SELECT auditoria.anonimizar_registros(1)",
        })
        {
            var error = await PostgresDeAuditoriaFixture.IntentarComo(bd.Depuracion, null, intento);
            Assert.True(error is not null, intento);
            Assert.Contains(error!.SqlState, new[] { "42501", "42883" });
        }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-02")]
    public async Task LaAplicacionNoPuedeEscribirLaPoliticaNiLosDemasEstadosFueraDeLoConcedido()
    {
        Assert.Equal("42501", (await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, "rol_auditoria", "INSERT INTO auditoria.politica_de_anonimizacion VALUES ('Nueva', false, false)"))?.SqlState);
        Assert.Equal("42501", (await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, "rol_auditoria", "UPDATE auditoria.por_anonimizar SET procesado_en = now()"))?.SqlState);
        Assert.Equal("42501", (await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, "rol_auditoria", "DELETE FROM auditoria.por_anonimizar"))?.SqlState);
        Assert.Null(await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, "rol_auditoria", "INSERT INTO auditoria.por_anonimizar (identificador_opaco, tipo, registrado_en) VALUES (gen_random_uuid(), 'baja_de_titular', now())"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-02")]
    public async Task SinEjecutarComoElRolDeAccesoNoLeeNingunEsquema()
    {
        Assert.Equal("42501", (await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, null, "SELECT count(*) FROM auditoria.registros"))?.SqlState);
        Assert.Equal("42501", (await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, null, "SELECT count(*) FROM identidad.registros"))?.SqlState);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-02")]
    public async Task ElRolDeUnEsquemaNoLeeElDelOtro()
    {
        Assert.Equal("42501", (await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, "rol_auditoria", "SELECT count(*) FROM identidad.registros"))?.SqlState);
        Assert.Equal("42501", (await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, "rol_identidad", "SELECT count(*) FROM auditoria.registros"))?.SqlState);
    }
}
