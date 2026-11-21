using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using Npgsql;
using RolesDeUsuario = BabelHub.Shared.Contratos.RolesDeUsuario;

namespace BabelHub.IntegrationTests.Reclamaciones;

/// <summary>Identidad en la vinculación inicial: la cuenta de cliente de cada correo, con una falla a pedido después de un número de consultas.</summary>
internal sealed class ClientesPorCorreoEnMemoria : IClientePorCorreoVerificadoDeSistema
{
    public Dictionary<string, Guid> Cuentas { get; } = [];

    public int FallaDespuesDe { get; set; } = int.MaxValue;

    public int Consultas { get; private set; }

    public Task<Guid?> Consultar(string correoNormalizado, CancellationToken cancelacion = default)
    {
        if (Consultas++ >= FallaDespuesDe)
        {
            throw new InvalidOperationException("Identidad no responde.");
        }

        return Task.FromResult<Guid?>(Cuentas.TryGetValue(correoNormalizado, out var cuenta) ? cuenta : null);
    }
}

/// <summary>
/// T35 de <c>ecommerce-libro-reclamaciones</c> (DD-26): el vínculo de las hojas con la cuenta sobre PostgreSQL real con los casos de uso, el vinculador y el receptor reales (AC-REC-15, AC-REC-17 a AC-REC-31). El reclamo condicional y atómico bajo
/// simultaneidad, el guardián de la base (de una cuenta a otra, la cuenta cancelada), el registro con sesión con la auditoría caída (no se guarda la hoja, la secuencia se revierte y el cupo vuelve), el cambio de correo y el reemplazo,
/// la vinculación inicial por lotes (reanudable, idempotente y que no se reabre), la anonimización que desvincula y la cancelación de una cuenta sin correo verificado. Solo Identidad, el actor, los límites, el correo y la auditoría son dobles.
/// </summary>
[Collection(ColeccionPostgresDeReclamaciones.Nombre)]
public sealed class VinculoDeHojasConPostgresTests(PostgresDeReclamacionesFixture bd) : IAsyncLifetime
{
    private static readonly Guid Ana = Guid.Parse("c0000000-0000-0000-0000-00000000c001");
    private static readonly Guid Beto = Guid.Parse("c0000000-0000-0000-0000-00000000c002");
    private static readonly Guid Carla = Guid.Parse("c0000000-0000-0000-0000-00000000c003");
    private const string CorreoDeAna = "ana@example.com";

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private EntornoDeReclamacionesConPostgres Entorno()
    {
        var e = new EntornoDeReclamacionesConPostgres(bd);
        e.Limites.Maximo = 10_000;
        return e;
    }

    /// <summary>Registra una hoja como la registraría un visitante (sin sesión) para un correo de contacto y devuelve su número.</summary>
    private static async Task<string> RegistrarComoVisitante(EntornoDeReclamacionesConPostgres e, string correo)
    {
        e.Actor.Actual = ActorActual.Anonimo();
        var resultado = await e.Reclamaciones.Registrar(e.Entrada() with { Correo = correo });
        Assert.True(resultado.EsExito);
        return resultado.Valor.Numero;
    }

    private Task<T?> Uno<T>(string sql) => bd.Consultar<T>(sql);

    private Task<Guid?> CuentaDe(string numero) => bd.Consultar<Guid?>($"SELECT cuenta_id FROM reclamaciones.hojas_reclamacion WHERE numero = '{numero}'");

    private static ReceptorDeCambiosDeCuentaDeReclamaciones Receptor(EntornoDeReclamacionesConPostgres e) => new(e.Vinculador, e.Vinculos, e.Unidad);

    // ---------- Reclamo atómico (AC-REC-21, AC-REC-22, AC-REC-25) ----------

    [Fact]
    [Trait("Criterio", "AC-REC-21")]
    [Trait("Criterio", "AC-REC-26")]
    public async Task Reclamar_vincula_las_hojas_sin_cuenta_del_correo_y_las_audita_una_vez_sin_el_correo_ni_el_numero()
    {
        var e = Entorno();
        var a = await RegistrarComoVisitante(e, CorreoDeAna);
        var b = await RegistrarComoVisitante(e, "otra@example.com");
        e.Actor.Actual = ActorActual.DeUsuario(Ana, RolesDeUsuario.Cliente);

        var vinculadas = await e.Vinculador.Reclamar(Ana, CorreoDeAna, DisparadoresDeVinculo.Consulta);

        Assert.Equal(1, vinculadas);
        Assert.Equal(Ana, await CuentaDe(a));
        Assert.Null(await CuentaDe(b));
        var auditoria = Assert.Single(e.Auditoria.Solicitudes, s => s.Accion == AccionesAuditables.HojaDeReclamacionVinculada);
        Assert.Equal(Ana.ToString("D"), auditoria.ObjetoId);
        Assert.Equal("1", auditoria.Detalle["conteo"]);
        Assert.DoesNotContain(auditoria.Detalle.Values, v => v is not null && (v.Contains('@', StringComparison.Ordinal) || v.Contains(a, StringComparison.Ordinal)));
        Assert.Equal(0, await e.Vinculador.Reclamar(Ana, CorreoDeAna, DisparadoresDeVinculo.Consulta));
        _ = Assert.Single(e.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-25")]
    public async Task Dos_cuentas_que_reclaman_a_la_vez_el_mismo_correo_no_fallan_y_gana_una_sola_que_conserva_todas_las_hojas()
    {
        var e = Entorno();
        for (var i = 0; i < 5; i++)
        {
            await RegistrarComoVisitante(e, CorreoDeAna);
        }

        var resultados = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(i => e.Vinculador.Reclamar(i % 2 == 0 ? Ana : Beto, CorreoDeAna, DisparadoresDeVinculo.Consulta)));

        Assert.Equal(5, resultados.Sum());
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(DISTINCT cuenta_id) FROM reclamaciones.hojas_reclamacion WHERE cuenta_id IS NOT NULL"));
        Assert.Equal(5L, await bd.Consultar<long>("SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE cuenta_id IS NOT NULL"));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-25")]
    public async Task La_base_no_deja_pasar_una_hoja_de_una_cuenta_a_otra_ni_vincular_una_hoja_de_una_cuenta_cancelada()
    {
        var e = Entorno();
        var vinculada = await RegistrarComoVisitante(e, CorreoDeAna);
        await e.Vinculador.Reclamar(Ana, CorreoDeAna, DisparadoresDeVinculo.Consulta);
        var cancelada = await RegistrarComoVisitante(e, "otra@example.com");
        await bd.Ejecutar($"UPDATE reclamaciones.hojas_reclamacion SET de_cuenta_cancelada = true WHERE numero = '{cancelada}'");

        var cambio = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE reclamaciones.hojas_reclamacion SET cuenta_id = '{Beto}' WHERE numero = '{vinculada}'"));
        var deCancelada = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE reclamaciones.hojas_reclamacion SET cuenta_id = '{Beto}' WHERE numero = '{cancelada}'"));

        Assert.Equal("23000", cambio.SqlState);
        Assert.Equal("23514", deCancelada.SqlState);
        Assert.Equal(Ana, await CuentaDe(vinculada));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-25")]
    public async Task Reclamo_cancelacion_y_desvinculacion_simultaneos_sobre_las_mismas_hojas_no_forman_interbloqueos()
    {
        var e = Entorno();
        for (var i = 0; i < 20; i++)
        {
            await RegistrarComoVisitante(e, CorreoDeAna);
        }

        var tareas = Enumerable.Range(0, 30).Select(i => (i % 3) switch
        {
            0 => e.Vinculador.Reclamar(Ana, CorreoDeAna, DisparadoresDeVinculo.Consulta),
            1 => bd.Unidad.Ejecutar(token => e.Vinculos.MarcarYDesvincularDeLaCuenta(Ana, CorreoDeAna, token)),
            _ => bd.Unidad.Ejecutar(token => e.Vinculos.DesvincularDeLaCuenta(Ana, token)),
        });

        // Ninguna falla por interbloqueo (40P01) ni por tiempo de espera: todas terminan.
        await Task.WhenAll(tareas);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE cuenta_id IS NOT NULL AND de_cuenta_cancelada"));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-25")]
    public async Task El_rol_de_la_aplicacion_no_puede_pasar_el_vinculo_de_una_cuenta_a_otra_ni_borrar_el_recorrido_de_la_vinculacion_inicial()
    {
        var e = Entorno();
        var numero = await RegistrarComoVisitante(e, CorreoDeAna);
        await e.Vinculador.Reclamar(Ana, CorreoDeAna, DisparadoresDeVinculo.Consulta);
        var esquema = new BabelHub.Ecommerce.Infrastructure.Reclamaciones.AccesoAlEsquemaDeReclamaciones(bd.Unidad, bd.Unidad);

        async Task Ejecutar(string sql) => await esquema.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
                return await comando.ExecuteNonQueryAsync(token);
            },
            CancellationToken.None);

        var cambio = await Assert.ThrowsAsync<BabelHub.Ecommerce.Infrastructure.Reclamaciones.FallaDeLaBaseDeReclamacionesException>(
            () => Ejecutar($"UPDATE reclamaciones.hojas_reclamacion SET cuenta_id = '{Beto}' WHERE numero = '{numero}'"));
        var borrado = await Assert.ThrowsAsync<BabelHub.Ecommerce.Infrastructure.Reclamaciones.FallaDeLaBaseDeReclamacionesException>(
            () => Ejecutar("DELETE FROM reclamaciones.vinculacion_inicial"));

        Assert.Equal("23000", cambio.CodigoDeEstado);
        Assert.NotNull(borrado.CodigoDeEstado);
        Assert.Equal(Ana, await CuentaDe(numero));
    }

    [Theory]
    [Trait("Criterio", "AC-REC-22")]
    [InlineData("auditoria")]
    [InlineData("correo")]
    public async Task Si_no_se_puede_auditar_o_leer_el_correo_no_se_vincula_nada_y_la_consulta_falla_cerrada(string falla)
    {
        var e = Entorno();
        var numero = await RegistrarComoVisitante(e, CorreoDeAna);
        var cuentas = new CorreosDeLasCuentas { [Ana] = CorreoDeAna };
        e.Actor.Actual = ActorActual.DeUsuario(Ana, RolesDeUsuario.Cliente);
        if (falla == "auditoria")
        {
            e.Auditoria.Caida = true;
        }
        else
        {
            cuentas.Falla = true;
        }

        var listar = new ListarMisHojas(new AutorizadorDeReclamaciones(e.Actor, e.Denegaciones), cuentas, e.Vinculador, e.Vinculos, e.Unidad, new LimiteDeConsultaDeHojas(e.Limites));
        var resultado = await listar.Ejecutar(new ListarMisHojasEntrada());

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Null(await CuentaDe(numero));
    }

    // ---------- Registro con sesión (AC-REC-20, AC-REC-29) ----------

    [Fact]
    [Trait("Criterio", "AC-REC-20")]
    public async Task Un_cliente_con_sesion_registra_la_hoja_vinculada_a_su_cuenta_aunque_el_correo_de_contacto_sea_otro_y_la_ve_sin_reclamar()
    {
        var e = Entorno();
        e.Actor.Actual = ActorActual.DeUsuario(Ana, RolesDeUsuario.Cliente);

        var resultado = await e.Reclamaciones.Registrar(e.Entrada() with { Correo = "contacto.distinto@example.com" });

        Assert.True(resultado.EsExito);
        Assert.Equal(Ana, await CuentaDe(resultado.Valor.Numero));
        var lista = await bd.Unidad.Ejecutar(token => e.Vinculos.ListarDeLaCuenta(Ana, 1, 10, token));
        Assert.Equal(resultado.Valor.Numero, Assert.Single(lista.Hojas).Numero.ToString());
        Assert.Equal("registro", Assert.Single(e.Auditoria.Solicitudes).Detalle["disparador"]);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-29")]
    [InlineData("visitante")]
    [InlineData("personal")]
    public async Task Un_visitante_o_el_personal_registran_sin_cuenta_y_sin_auditoria(string quien)
    {
        var e = Entorno();
        e.Actor.Actual = quien == "visitante" ? ActorActual.Anonimo() : ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.AdministradorCentral);

        var resultado = await e.Reclamaciones.Registrar(e.Entrada());

        Assert.True(resultado.EsExito);
        Assert.Null(await CuentaDe(resultado.Valor.Numero));
        Assert.Empty(e.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-26")]
    [Trait("Criterio", "AC-REC-30")]
    public async Task Con_la_auditoria_caida_el_registro_con_sesion_no_guarda_la_hoja_revierte_la_secuencia_y_el_mismo_envio_sin_sesion_se_registra()
    {
        var e = Entorno();
        e.Actor.Actual = ActorActual.DeUsuario(Ana, RolesDeUsuario.Cliente);
        e.Auditoria.Caida = true;
        var entrada = e.Entrada("clave-del-envio-con-sesion");

        var rechazo = await e.Reclamaciones.Registrar(entrada);

        Assert.Equal(CategoriaDeError.NoDisponible, rechazo.Error!.Categoria);
        Assert.Contains(rechazo.Error.Errores, c => c.Campo == ClavesDeReclamaciones.RegistrarSinSesion);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reclamaciones.hojas_reclamacion"));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT coalesce(sum(ultimo), 0) FROM reclamaciones.secuencias_de_hoja"));

        e.Actor.Actual = ActorActual.Anonimo();
        var sinSesion = await e.Reclamaciones.Registrar(entrada);

        Assert.True(sinSesion.EsExito);
        Assert.Equal(DateTimeOffset.UtcNow.AddHours(-5).Year + "-000001", sinSesion.Valor.Numero);
        Assert.Null(await CuentaDe(sinSesion.Valor.Numero));
    }

    // ---------- Cambio de correo y reemplazo (AC-REC-23, AC-REC-27, AC-REC-30) ----------

    [Fact]
    [Trait("Criterio", "AC-REC-23")]
    [Trait("Criterio", "AC-REC-26")]
    public async Task El_cambio_de_correo_vincula_las_hojas_del_correo_anterior_sin_las_de_una_cuenta_cancelada()
    {
        var e = Entorno();
        var viva = await RegistrarComoVisitante(e, CorreoDeAna);
        var cancelada = await RegistrarComoVisitante(e, CorreoDeAna);
        await bd.Ejecutar($"UPDATE reclamaciones.hojas_reclamacion SET de_cuenta_cancelada = true WHERE numero = '{cancelada}'");
        e.Actor.Actual = ActorActual.DeUsuario(Ana, RolesDeUsuario.Cliente);

        await bd.Unidad.Ejecutar<int>(async token =>
        {
            await Receptor(e).AlCambiarElCorreo(Ana, CorreoDeAna, token);
            return 0;
        });

        Assert.Equal(Ana, await CuentaDe(viva));
        Assert.Null(await CuentaDe(cancelada));
        Assert.Equal("cambio-de-correo", Assert.Single(e.Auditoria.Solicitudes).Detalle["disparador"]);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-23")]
    public async Task Si_el_receptor_no_puede_auditar_el_cambio_de_correo_no_se_aplica_y_no_queda_nada_vinculado()
    {
        var e = Entorno();
        var numero = await RegistrarComoVisitante(e, CorreoDeAna);
        e.Actor.Actual = ActorActual.DeUsuario(Ana, RolesDeUsuario.Cliente);
        e.Auditoria.Caida = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => bd.Unidad.Ejecutar<int>(async token =>
        {
            await Receptor(e).AlCambiarElCorreo(Ana, CorreoDeAna, token);
            return 0;
        }));

        Assert.Null(await CuentaDe(numero));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-27")]
    public async Task Al_reemplazar_una_cuenta_sin_verificar_sus_hojas_quedan_sin_cuenta_y_otra_cuenta_las_reclama()
    {
        var e = Entorno();
        e.Actor.Actual = ActorActual.DeUsuario(Ana, RolesDeUsuario.Cliente);
        var resultado = await e.Reclamaciones.Registrar(e.Entrada() with { Correo = CorreoDeAna });
        Assert.Equal(Ana, await CuentaDe(resultado.Valor.Numero));

        await bd.Unidad.Ejecutar<int>(async token =>
        {
            await Receptor(e).AlReemplazarLaCuenta(Ana, token);
            return 0;
        });

        Assert.Null(await CuentaDe(resultado.Valor.Numero));
        Assert.Equal(1, await e.Vinculador.Reclamar(Beto, CorreoDeAna, DisparadoresDeVinculo.Consulta));
        Assert.Equal(Beto, await CuentaDe(resultado.Valor.Numero));
    }

    // ---------- Vinculación inicial (AC-REC-24, AC-REC-26) ----------

    private (VincularHojasExistentes Paso, ClientesPorCorreoEnMemoria Clientes) VinculacionInicial(EntornoDeReclamacionesConPostgres e)
    {
        e.Actor.ComoSistema();
        var clientes = new ClientesPorCorreoEnMemoria();
        return (new VincularHojasExistentes(new AutorizadorDeReclamaciones(e.Actor, e.Denegaciones), e.Vinculos, clientes, e.Vinculador, e.Unidad, new RelojReal()), clientes);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-24")]
    public async Task La_vinculacion_inicial_es_reanudable_idempotente_y_no_se_reabre()
    {
        var e = Entorno();
        var total = VincularHojasExistentes.TamanoDelLote + 10;
        for (var i = 0; i < total; i++)
        {
            await RegistrarComoVisitante(e, $"cliente{i:D3}@example.com");
        }

        var (paso, clientes) = VinculacionInicial(e);
        for (var i = 0; i < total; i += 2)
        {
            clientes.Cuentas[$"cliente{i:D3}@example.com"] = Guid.NewGuid();
        }

        // Identidad cae a mitad del segundo lote: el cursor queda en el último correo del primer lote.
        clientes.FallaDespuesDe = VincularHojasExistentes.TamanoDelLote + 3;
        var fallida = await paso.Ejecutar();
        Assert.Equal(CategoriaDeError.NoDisponible, fallida.Error!.Categoria);
        Assert.Equal($"cliente{VincularHojasExistentes.TamanoDelLote - 1:D3}@example.com", await Uno<string>("SELECT ultimo_correo FROM reclamaciones.vinculacion_inicial WHERE id = 1"));
        Assert.Null(await Uno<DateTime?>("SELECT completada_en FROM reclamaciones.vinculacion_inicial WHERE id = 1"));

        // La siguiente ejecución continúa y termina: solo las cuentas de cliente existentes quedan vinculadas y el cursor se pone en nulo.
        clientes.FallaDespuesDe = int.MaxValue;
        var reanudada = await paso.Ejecutar();
        Assert.True(reanudada.EsExito);
        Assert.Equal((long)(total + 1) / 2, await bd.Consultar<long>("SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE cuenta_id IS NOT NULL"));
        Assert.Null(await Uno<string>("SELECT ultimo_correo FROM reclamaciones.vinculacion_inicial WHERE id = 1"));
        Assert.NotNull(await Uno<DateTime?>("SELECT completada_en FROM reclamaciones.vinculacion_inicial WHERE id = 1"));
        Assert.All(e.Auditoria.Solicitudes, s => Assert.Equal("vinculacion-inicial", s.Detalle["disparador"]));

        // Una nueva corrida no consulta ni vincula; la base no deja reabrir el recorrido.
        var consultas = clientes.Consultas;
        Assert.Equal(0, (await paso.Ejecutar()).Valor);
        Assert.Equal(consultas, clientes.Consultas);
        var reabrir = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar("UPDATE reclamaciones.vinculacion_inicial SET completada_en = NULL WHERE id = 1"));
        Assert.Equal("23000", reabrir.SqlState);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-24")]
    public async Task La_vinculacion_inicial_con_la_auditoria_caida_no_avanza_el_cursor_y_no_vincula_nada()
    {
        var e = Entorno();
        await RegistrarComoVisitante(e, CorreoDeAna);
        var (paso, clientes) = VinculacionInicial(e);
        clientes.Cuentas[CorreoDeAna] = Ana;
        e.Auditoria.Caida = true;

        var resultado = await paso.Ejecutar();

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE cuenta_id IS NOT NULL"));
        Assert.Null(await Uno<string>("SELECT ultimo_correo FROM reclamaciones.vinculacion_inicial WHERE id = 1"));
    }

    // ---------- Anonimización, cancelación y conteo (AC-REC-15, AC-REC-17, AC-REC-31) ----------

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public async Task La_anonimizacion_desvincula_la_hoja_y_deja_de_verse_en_las_hojas_de_su_titular_y_en_el_conteo()
    {
        var e = Entorno();
        await bd.Ejecutar(
            $"""
            INSERT INTO reclamaciones.hojas_reclamacion (id, numero, anio, tipo, nombre, documento, domicilio, correo, correo_normalizado, bien_contratado, monto_reclamado, detalle, contenido_hash, fecha_registro, clave_hash, estado, cuenta_id)
            VALUES (gen_random_uuid(), '2023-000001', 2023, 'queja', 'Ana Pérez', '12345678', 'Jr. Lima 123', 'ana@example.com', 'ana@example.com', 'Libro', 10, 'Detalle', 'h', now() - interval '2 years 1 day', 'clave-de-la-hoja-vieja', 'pendiente', '{Ana}')
            """);
        Assert.Equal(1, await bd.Unidad.Ejecutar(token => e.Vinculos.ContarPendientesDeLaCuenta(Ana, token)));
        e.Actor.ComoSistema();

        var anonimizadas = await e.Anonimizacion.Ejecutar();

        Assert.Equal(1, anonimizadas.Valor);
        Assert.Null(await CuentaDe("2023-000001"));
        Assert.Equal(0, (await bd.Unidad.Ejecutar(token => e.Vinculos.ListarDeLaCuenta(Ana, 1, 10, token))).Total);
        Assert.Equal(0, await bd.Unidad.Ejecutar(token => e.Vinculos.ContarPendientesDeLaCuenta(Ana, token)));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-31")]
    public async Task Cancelar_una_cuenta_sin_correo_verificado_marca_solo_sus_hojas_vinculadas_y_las_de_un_tercero_siguen_visibles_para_quien_verifique_el_correo()
    {
        var e = Entorno();
        var deTercero = await RegistrarComoVisitante(e, CorreoDeAna);
        e.Actor.Actual = ActorActual.DeUsuario(Carla, RolesDeUsuario.Cliente);
        var suya = (await e.Reclamaciones.Registrar(e.Entrada() with { Correo = CorreoDeAna })).Valor.Numero;
        Assert.Equal(Carla, await CuentaDe(suya));
        var sinVerificar = new CorreosDeLasCuentas { [Carla] = null };

        await bd.Unidad.Ejecutar<int>(async token =>
        {
            await new MarcarHojasDeUnaCuentaCancelada(sinVerificar, e.Vinculos).Aplicar(Carla, Guid.NewGuid(), token);
            return 0;
        });

        Assert.True(await bd.Consultar<bool>($"SELECT de_cuenta_cancelada FROM reclamaciones.hojas_reclamacion WHERE numero = '{suya}'"));
        Assert.Null(await CuentaDe(suya));
        Assert.False(await bd.Consultar<bool>($"SELECT de_cuenta_cancelada FROM reclamaciones.hojas_reclamacion WHERE numero = '{deTercero}'"));
        Assert.Equal(1, await e.Vinculador.Reclamar(Ana, CorreoDeAna, DisparadoresDeVinculo.Consulta));
        Assert.Equal(Ana, await CuentaDe(deTercero));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-31")]
    public async Task Si_no_se_puede_leer_el_correo_verificado_la_cancelacion_falla_y_no_marca_ninguna_hoja()
    {
        var e = Entorno();
        e.Actor.Actual = ActorActual.DeUsuario(Ana, RolesDeUsuario.Cliente);
        var numero = (await e.Reclamaciones.Registrar(e.Entrada())).Valor.Numero;
        var caido = new CorreosDeLasCuentas { Falla = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => bd.Unidad.Ejecutar<int>(async token =>
        {
            await new MarcarHojasDeUnaCuentaCancelada(caido, e.Vinculos).Aplicar(Ana, Guid.NewGuid(), token);
            return 0;
        }));

        Assert.False(await bd.Consultar<bool>($"SELECT de_cuenta_cancelada FROM reclamaciones.hojas_reclamacion WHERE numero = '{numero}'"));
        Assert.Equal(Ana, await CuentaDe(numero));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-17")]
    public async Task El_conteo_de_una_cuenta_sin_correo_verificado_es_cero_y_el_de_una_verificada_incluye_las_hojas_del_correo_tras_reclamarlas()
    {
        var e = Entorno();
        await RegistrarComoVisitante(e, CorreoDeAna);
        var cuentas = new CorreosDeLasCuentas { [Ana] = CorreoDeAna, [Beto] = null };
        var contar = new ContarHojasPendientesDeUnCorreo(cuentas, e.Vinculador, e.Vinculos);

        var (ana, beto) = await bd.Unidad.Ejecutar(async token => (await contar.Contar(Ana, token), await contar.Contar(Beto, token)));

        Assert.Equal(1, ana);
        Assert.Equal(0, beto);
        Assert.Equal("pendientes", Assert.Single(e.Auditoria.Solicitudes).Detalle["disparador"]);
    }
}

internal sealed class RelojReal : IReloj
{
    public DateTimeOffset Ahora => DateTimeOffset.UtcNow;

    public DateOnly HoyEnLima => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));
}
