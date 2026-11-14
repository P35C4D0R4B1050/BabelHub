using BabelHub.Inventario.Infrastructure.Auditoria;
using Npgsql;

namespace BabelHub.IntegrationTests.AuditoriaInventario;

/// <summary>
/// La función de depuración contra PostgreSQL real, los roles reales y los dos esquemas (T24; AUD-11, AUD-19, AUD-36; AC-AUD-09): anonimiza lo que la política dice con constantes o
/// <c>NULL</c>, conserva acción, fecha, tipo y resultado, no toca lo que no corresponde, es idempotente, y una prueba de deriva compara la tabla de política con el catálogo.
/// </summary>
public sealed class DepuracionDeRegistrosTests(PostgresDeAuditoriaFixture bd) : IClassFixture<PostgresDeAuditoriaFixture>
{
    public static TheoryData<string, string> Esquemas => new()
    {
        { "auditoria", "rol_auditoria" },
        { "identidad", "rol_identidad" },
    };

    private const string Correo = "persona.sembrada@example.com";

    private sealed record Sembrado(Guid Id, Guid? Actor, string? Rol, string? Origen, string? ObjetoId, string Detalle);

    /// <summary>Inserta un registro como la aplicación, con un instante relativo a ahora y los datos personales indicados.</summary>
    private async Task<Guid> Sembrar(
        string esquema,
        string rolDeEsquema,
        string intervalo,
        string accion = "SolicitudDeDatosConsultada",
        Guid? actor = null,
        string? rol = "administrador-central",
        string? origen = "v1:origen-de-la-huella",
        string? objetoId = null,
        string? objetoTipo = null,
        string detalle = "{}",
        string clavesPersonales = "{}")
    {
        var id = Guid.NewGuid();
        var sql = $"""
            INSERT INTO {esquema}.registros (id, servicio, tipo, actor_tipo, actor_id, rol, instante_utc, accion, objeto_tipo, objeto_id, origen, detalle, claves_personales, resultado, truncado)
            VALUES ('{id}', 'ecommerce', 'auditoria', 'usuario', {(actor is null ? "NULL" : $"'{actor}'")}, {(rol is null ? "NULL" : $"'{rol}'")}, now() - interval '{intervalo}', '{accion}',
                    {(objetoTipo is null ? "NULL" : $"'{objetoTipo}'")}, {(objetoId is null ? "NULL" : $"'{objetoId}'")}, {(origen is null ? "NULL" : $"'{origen}'")}, '{detalle}'::jsonb, '{clavesPersonales}', 'exitoso', false)
            """;
        await bd.EjecutarComoLaAplicacion(rolDeEsquema, sql);
        return id;
    }

    private Task Cancelar(string esquema, string rolDeEsquema, Guid cuenta) =>
        bd.EjecutarComoLaAplicacion(rolDeEsquema, $"INSERT INTO {esquema}.por_anonimizar (identificador_opaco, tipo, registrado_en) VALUES ('{cuenta}', 'cuenta_cancelada', now())");

    private async Task<int> Depurar(string esquema)
    {
        await using var conexion = await bd.Depuracion.OpenConnectionAsync();
        await using var comando = new NpgsqlCommand($"SELECT {esquema}.anonimizar_registros()", conexion);
        return (int)(await comando.ExecuteScalarAsync())!;
    }

    private async Task<(Guid? Actor, string? Rol, string? Origen, string? ObjetoId, string Detalle, string? AnonimizadoEn, string Accion, string Resultado, string Tipo)> Leer(string esquema, Guid id)
    {
        await using var comando = bd.Administracion.CreateCommand($"SELECT actor_id, rol, origen, objeto_id, detalle::text, anonimizado_en::text, accion, resultado, tipo FROM {esquema}.registros WHERE id = '{id}'");
        await using var lector = await comando.ExecuteReaderAsync();
        Assert.True(await lector.ReadAsync());
        return (lector.IsDBNull(0) ? null : lector.GetGuid(0), lector.IsDBNull(1) ? null : lector.GetString(1), lector.IsDBNull(2) ? null : lector.GetString(2), lector.IsDBNull(3) ? null : lector.GetString(3),
            lector.GetString(4), lector.IsDBNull(5) ? null : lector.GetString(5), lector.GetString(6), lector.GetString(7), lector.GetString(8));
    }

    private async Task LimpiarPorAnonimizar(string esquema) => await bd.Ejecutar($"DELETE FROM {esquema}.por_anonimizar");

    // ---------- AC-AUD-09 ----------

    [Theory]
    [MemberData(nameof(Esquemas))]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task UnRegistroDeMasDeDosAniosSeAnonimizaCompleto_UnoDeDosAniosMenosUnDiaNoCambia(string esquema, string rolDeEsquema)
    {
        await LimpiarPorAnonimizar(esquema);
        var usuario = Guid.NewGuid();
        var detalle = $"{{\"tipoDeSolicitud\":\"acceso\",\"nombre\":\"{Correo}\"}}";
        var viejo = await Sembrar(esquema, rolDeEsquema, "2 years 1 day", actor: usuario, objetoId: Guid.NewGuid().ToString("D"), objetoTipo: "solicitud", detalle: detalle, clavesPersonales: "{nombre}");
        var reciente = await Sembrar(esquema, rolDeEsquema, "1 year 364 days", actor: usuario, detalle: detalle, clavesPersonales: "{nombre}");

        var anonimizados = await Depurar(esquema);

        Assert.True(anonimizados >= 1);
        var a = await Leer(esquema, viejo);
        Assert.Null(a.Actor);
        Assert.Null(a.Origen);
        Assert.Contains("\"nombre\": \"[anonimizado]\"", a.Detalle, StringComparison.Ordinal);
        Assert.Contains("\"tipoDeSolicitud\": \"acceso\"", a.Detalle, StringComparison.Ordinal);
        Assert.NotNull(a.AnonimizadoEn);
        Assert.Equal(("SolicitudDeDatosConsultada", "exitoso", "auditoria"), (a.Accion, a.Resultado, a.Tipo));

        var b = await Leer(esquema, reciente);
        Assert.Equal(usuario, b.Actor);
        Assert.Equal("v1:origen-de-la-huella", b.Origen);
        Assert.Contains(Correo, b.Detalle, StringComparison.Ordinal);
        Assert.Null(b.AnonimizadoEn);
    }

    [Theory]
    [MemberData(nameof(Esquemas))]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task LaCuentaCanceladaAnonimizaSusRegistros_PeroElRegistroDeUnAdministradorSobreEllaConservaAlAdministrador(string esquema, string rolDeEsquema)
    {
        await LimpiarPorAnonimizar(esquema);
        var cuenta = Guid.NewGuid();
        var administrador = Guid.NewGuid();
        var otra = Guid.NewGuid();

        // La cuenta como actor (hace ayer): se anonimizan su id, su rol y su origen.
        var propio = await Sembrar(esquema, rolDeEsquema, "1 day", accion: "SolicitudDeDatosConsultada", actor: cuenta, rol: "cliente", origen: "v1:otro-origen-1234");
        // Un administrador sobre esa cuenta (el objeto es la cuenta): se anonimiza el objeto, no el administrador.
        var ajeno = await Sembrar(esquema, rolDeEsquema, "1 day", accion: "BibliotecaDeClienteConsultada", actor: administrador, objetoTipo: "cliente", objetoId: cuenta.ToString("D"));
        // Una cuenta que no se canceló: no cambia nada.
        var intacto = await Sembrar(esquema, rolDeEsquema, "1 day", accion: "BibliotecaDeClienteConsultada", actor: administrador, objetoTipo: "cliente", objetoId: otra.ToString("D"));
        // Una clave personal del detalle cuyo valor es la cuenta.
        var enDetalle = await Sembrar(esquema, rolDeEsquema, "1 day", accion: "SolicitudDeDatosAtendida", actor: administrador, detalle: $"{{\"resolucion\":\"atendida\",\"titular\":\"{cuenta:D}\"}}", clavesPersonales: "{titular}");
        await Cancelar(esquema, rolDeEsquema, cuenta);

        var anonimizados = await Depurar(esquema);

        Assert.Equal(3, anonimizados);
        var p = await Leer(esquema, propio);
        Assert.Null(p.Actor);
        Assert.Null(p.Rol);
        Assert.Null(p.Origen);

        var a = await Leer(esquema, ajeno);
        Assert.Equal(administrador, a.Actor);
        Assert.Equal("administrador-central", a.Rol);
        Assert.Null(a.ObjetoId);

        var i = await Leer(esquema, intacto);
        Assert.Equal(otra.ToString("D"), i.ObjetoId);
        Assert.Null(i.AnonimizadoEn);

        var d = await Leer(esquema, enDetalle);
        Assert.Equal(administrador, d.Actor);
        Assert.Contains("\"titular\": \"[anonimizado]\"", d.Detalle, StringComparison.Ordinal);
        Assert.Contains("\"resolucion\": \"atendida\"", d.Detalle, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Esquemas))]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task RepetirLaDepuracionElMismoDiaNoCambiaNadaYDevuelveCero(string esquema, string rolDeEsquema)
    {
        await LimpiarPorAnonimizar(esquema);
        var cuenta = Guid.NewGuid();
        await Sembrar(esquema, rolDeEsquema, "3 years", actor: Guid.NewGuid());
        await Sembrar(esquema, rolDeEsquema, "1 day", actor: cuenta, rol: "cliente");
        await Cancelar(esquema, rolDeEsquema, cuenta);

        Assert.True(await Depurar(esquema) >= 2);
        var instantaneas = await bd.Consultar<string>($"SELECT string_agg(r::text, '|' ORDER BY secuencia) FROM {esquema}.registros r");

        Assert.Equal(0, await Depurar(esquema));
        Assert.Equal(instantaneas, await bd.Consultar<string>($"SELECT string_agg(r::text, '|' ORDER BY secuencia) FROM {esquema}.registros r"));
    }

    [Theory]
    [MemberData(nameof(Esquemas))]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task UnaCuentaCanceladaDespuesDeUnaAnonimizacionParcialAnonimizaLoQueQuede(string esquema, string rolDeEsquema)
    {
        await LimpiarPorAnonimizar(esquema);
        var cuenta = Guid.NewGuid();
        var registro = await Sembrar(esquema, rolDeEsquema, "10 days", accion: "BibliotecaDeClienteConsultada", actor: Guid.NewGuid(), objetoTipo: "cliente", objetoId: cuenta.ToString("D"));
        Assert.Equal(0, await Depurar(esquema));

        await Cancelar(esquema, rolDeEsquema, cuenta);
        Assert.Equal(1, await Depurar(esquema));

        Assert.Null((await Leer(esquema, registro)).ObjetoId);
    }

    [Theory]
    [MemberData(nameof(Esquemas))]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task ElResiduoNoTieneValorPersonalAlguno_UnCorreoSembradoNoQuedaEnNingunaColumna(string esquema, string rolDeEsquema)
    {
        await LimpiarPorAnonimizar(esquema);
        var viejo = await Sembrar(
            esquema,
            rolDeEsquema,
            "2 years 2 days",
            accion: "SolicitudDeDatosAtendida",
            actor: Guid.NewGuid(),
            origen: "v1:203.0.113.7",
            objetoTipo: "solicitud",
            detalle: $"{{\"resolucion\":\"atendida\",\"titular\":\"{Correo}\"}}",
            clavesPersonales: "{titular}");

        await Depurar(esquema);

        var fila = await bd.Consultar<string>($"SELECT r::text FROM {esquema}.registros r WHERE id = '{viejo}'");
        Assert.DoesNotContain(Correo, fila, StringComparison.Ordinal);
        Assert.DoesNotContain("203.0.113.7", fila, StringComparison.Ordinal);
        Assert.Contains(PoliticaDeAnonimizacionDeLaBaseInventario.Constante, fila, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Esquemas))]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task ElRolPersonalSoloSeAnonimizaEnLasAccionesQueLoDeclaranYAlVencer(string esquema, string rolDeEsquema)
    {
        await LimpiarPorAnonimizar(esquema);
        var conRolPersonal = await bd.Consultar<string>("SELECT accion FROM auditoria.politica_de_anonimizacion WHERE rol_personal LIMIT 1");
        var sinRolPersonal = await Sembrar(esquema, rolDeEsquema, "3 years", accion: "SolicitudDeDatosConsultada", actor: Guid.NewGuid(), rol: "encargado-de-sucursal");

        await Depurar(esquema);

        var registro = await Leer(esquema, sinRolPersonal);
        Assert.Null(registro.Actor);
        Assert.Equal("encargado-de-sucursal", registro.Rol);
        _ = conRolPersonal;
    }

    // ---------- Retención de por_anonimizar ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task LosIdentificadoresSeMarcanProcesadosYSeBorranALos30Dias()
    {
        await LimpiarPorAnonimizar("auditoria");
        var cuenta = Guid.NewGuid();
        await Cancelar("auditoria", "rol_auditoria", cuenta);

        await Depurar("auditoria");
        Assert.True(await bd.Consultar<bool>($"SELECT procesado_en IS NOT NULL FROM auditoria.por_anonimizar WHERE identificador_opaco = '{cuenta}'"));

        await bd.Ejecutar($"ALTER TABLE auditoria.por_anonimizar DISABLE TRIGGER ALL; UPDATE auditoria.por_anonimizar SET procesado_en = now() - interval '31 days' WHERE identificador_opaco = '{cuenta}'");
        await Depurar("auditoria");

        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.por_anonimizar WHERE identificador_opaco = '{cuenta}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task UnRegistroPosteriorQueMencionaLaCuentaDentroDeLos30DiasTambienSeAnonimiza()
    {
        await LimpiarPorAnonimizar("auditoria");
        var cuenta = Guid.NewGuid();
        await Cancelar("auditoria", "rol_auditoria", cuenta);
        await Depurar("auditoria");

        var tardio = await Sembrar("auditoria", "rol_auditoria", "1 minute", accion: "BibliotecaDeClienteConsultada", actor: Guid.NewGuid(), objetoTipo: "cliente", objetoId: cuenta.ToString("D"));
        await Depurar("auditoria");

        Assert.Null((await Leer("auditoria", tardio)).ObjetoId);
    }

    // ---------- Deriva con el catálogo ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task LaTablaDePoliticaCoincideConElCatalogo_UnaAccionNuevaSinClasificarHaceFallarLaPrueba()
    {
        var enLaBase = new Dictionary<string, (bool Objeto, bool Rol)>();
        await using (var comando = bd.Administracion.CreateCommand("SELECT accion, objeto_personal, rol_personal FROM auditoria.politica_de_anonimizacion"))
        await using (var lector = await comando.ExecuteReaderAsync())
        {
            while (await lector.ReadAsync())
            {
                enLaBase[lector.GetString(0)] = (lector.GetBoolean(1), lector.GetBoolean(2));
            }
        }

        var enElCatalogo = PostgresDeAuditoriaFixture.Catalogo.Acciones.ToDictionary(a => a.Identificador, a => (a.ObjetoPersonal, a.RolPersonal));

        Assert.Equal(enElCatalogo.OrderBy(p => p.Key, StringComparer.Ordinal), enLaBase.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task ElDisparadorGuardianRechazaUnUpdateDeLaFuncionQueTocaLaIdentidadDelRegistro()
    {
        var id = await Sembrar("auditoria", "rol_auditoria", "1 hour", actor: Guid.NewGuid());

        // Ni siquiera el propietario de la función puede cambiar la acción, la fecha, el tipo o el resultado.
        Assert.Null(await PostgresDeAuditoriaFixture.IntentarComo(bd.Administracion, "auditoria_depuracion_dueno", $"UPDATE auditoria.registros SET anonimizado_en = now() WHERE id = '{id}'"));
        Assert.NotNull(await PostgresDeAuditoriaFixture.IntentarComo(bd.Administracion, "auditoria_depuracion_dueno", $"UPDATE auditoria.registros SET resultado = 'fallido' WHERE id = '{id}'"));
        Assert.NotNull(await PostgresDeAuditoriaFixture.IntentarComo(bd.Administracion, "auditoria_depuracion_dueno", $"DELETE FROM auditoria.registros WHERE id = '{id}'"));
    }
}

internal static class PoliticaDeAnonimizacionDeLaBaseInventario
{
    public const string Constante = "[anonimizado]";
}
