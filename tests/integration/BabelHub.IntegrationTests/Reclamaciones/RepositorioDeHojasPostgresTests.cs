using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using BabelHub.Ecommerce.Domain.Reclamaciones;
using BabelHub.Ecommerce.Infrastructure.Reclamaciones;
using Npgsql;

namespace BabelHub.IntegrationTests.Reclamaciones;

/// <summary>
/// T13: el esquema y los repositorios de Reclamaciones sobre PostgreSQL real con el rol <c>rol_reclamaciones</c> (AC-REC-04, AC-REC-05, AC-REC-15, REC-06): ida y vuelta de la hoja, la secuencia por año consecutiva y sin repetirse bajo simultaneidad, la clave repetida que no aborta la
/// transacción, la inmutabilidad y el no borrado también para el dueño, los privilegios por columna, el bloqueo <c>FOR UPDATE</c>, la anonimización de dos años por día de Lima contra <c>PoliticaDeConservacion</c> y que un <c>CHECK</c> fallido no deje datos personales en el error.
/// </summary>
[Collection(ColeccionPostgresDeReclamaciones.Nombre)]
public sealed class RepositorioDeHojasPostgresTests(PostgresDeReclamacionesFixture bd) : IAsyncLifetime
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

    private static readonly Guid Cuenta = Guid.Parse("c0000000-0000-0000-0000-00000000b001");

    private static readonly Guid OtraCuenta = Guid.Parse("c0000000-0000-0000-0000-00000000b002");

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static HojaDeReclamacion Hoja(int secuencia, string clave, string correo = "ana@example.com", DateTimeOffset? fecha = null, string documento = "12345678") =>
        HojaDeReclamacion.Registrar(
            Guid.NewGuid(),
            NumeroDeHoja.Crear((fecha ?? Inicio).ToOffset(TimeSpan.FromHours(-5)).Year, secuencia),
            TipoDeHoja.Reclamo,
            new DatosDelConsumidor("Ana Pérez Quispe", documento, "Jr. Lima 123, Ayacucho", correo),
            "Cien años de soledad",
            59.90m,
            "Llegó con las páginas dañadas",
            "PED-123",
            "contenido-" + clave,
            clave,
            fecha ?? Inicio);

    private async Task<ResultadoDeAgregado> Agregar(HojaDeReclamacion hoja) => await bd.Unidad.Ejecutar(token => bd.Hojas.Agregar(hoja, token));

    private Task<HojaDeReclamacion?> Leer(Guid id, bool paraActualizar = false) => bd.Unidad.Ejecutar(token => bd.Hojas.Obtener(id, paraActualizar, token));

    private async Task Guardar(HojaDeReclamacion hoja) => await bd.Unidad.Ejecutar(async token => { await bd.Hojas.Guardar(hoja, token); return 0; });

    // ---------- Ida y vuelta ----------

    [Fact]
    public async Task Una_hoja_se_guarda_y_se_lee_igual_con_sus_datos_y_su_correo_normalizado()
    {
        var hoja = Hoja(1, "clave-1", correo: "Ana@Example.com");
        Assert.Equal(ResultadoDeAgregado.Agregada, await Agregar(hoja));

        var leida = await Leer(hoja.Id);

        Assert.Equivalent(hoja, leida, strict: true);
        Assert.Equal("ana@example.com", await bd.Consultar<string>($"SELECT correo_normalizado FROM reclamaciones.hojas_reclamacion WHERE id = '{hoja.Id}'"));
        Assert.Equal("Ana@Example.com", leida!.Datos.Correo);
        Assert.Equal(hoja.Detalle, leida.Detalle);
    }

    [Fact]
    public async Task Responder_y_marcar_el_aviso_se_guardan_sin_tocar_lo_original_y_se_leen_igual()
    {
        var hoja = Hoja(1, "clave-1");
        await Agregar(hoja);
        var quien = Guid.NewGuid();
        Assert.Equal(ResultadoDeTransicion.Hecha, hoja.Responder("Le ofrecemos reponer el libro.", quien, Inicio.AddHours(1)));
        await Guardar(hoja);
        hoja.MarcarAvisoEnviado(Inicio.AddHours(2));
        await Guardar(hoja);

        var leida = await Leer(hoja.Id);

        Assert.Equivalent(hoja, leida, strict: true);
        Assert.Equal((EstadoDeHoja.Respondido, AvisoDeRespuesta.Enviado, quien), (leida!.Estado, leida.AvisoDeRespuesta!.Value, leida.RespondidaPor!.Value));
    }

    // ---------- Secuencia y clave ----------

    [Fact]
    public async Task La_secuencia_avanza_por_anio_y_cada_anio_empieza_en_uno()
    {
        var esperados = new[] { 1, 2, 3 };
        var obtenidos = new List<int>();
        foreach (var _ in esperados)
        {
            obtenidos.Add(await bd.Unidad.Ejecutar(token => bd.Hojas.SiguienteSecuencia(2026, token)));
        }

        Assert.Equal(esperados, obtenidos);
        Assert.Equal(1, await bd.Unidad.Ejecutar(token => bd.Hojas.SiguienteSecuencia(2027, token)));
        Assert.Equal(4, await bd.Unidad.Ejecutar(token => bd.Hojas.SiguienteSecuencia(2026, token)));
    }

    [Fact]
    public async Task Un_registro_revertido_devuelve_su_numero()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => bd.Unidad.Ejecutar<int>(
            async token =>
            {
                Assert.Equal(1, await bd.Hojas.SiguienteSecuencia(2026, token));
                throw new InvalidOperationException("se revierte");
            }));

        Assert.Equal(1, await bd.Unidad.Ejecutar(token => bd.Hojas.SiguienteSecuencia(2026, token)));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-04")]
    public async Task Una_clave_repetida_no_agrega_otra_hoja_ni_aborta_la_transaccion_y_se_encuentra_por_clave()
    {
        var primera = Hoja(1, "clave-igual");
        var repetida = Hoja(2, "clave-igual");

        var (primero, segundo, encontrada) = await bd.Unidad.Ejecutar(
            async token =>
            {
                var a = await bd.Hojas.Agregar(primera, token);
                var b = await bd.Hojas.Agregar(repetida, token);
                // Si la violación hubiera abortado la transacción, esta lectura fallaría con 25P02.
                return (a, b, await bd.Hojas.BuscarPorClave(["otra", "clave-igual"], token));
            });

        Assert.Equal((ResultadoDeAgregado.Agregada, ResultadoDeAgregado.ClaveRepetida), (primero, segundo));
        Assert.Equal(primera.Id, encontrada!.Id);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reclamaciones.hojas_reclamacion"));
        Assert.Null(await bd.Unidad.Ejecutar(token => bd.Hojas.BuscarPorClave(["no-existe"], token)));
        Assert.Null(await bd.Unidad.Ejecutar(token => bd.Hojas.BuscarPorClave([], token)));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-13")]
    public async Task Un_numero_repetido_falla_sin_repetir_datos_personales()
    {
        await Agregar(Hoja(1, "clave-a"));

        var falla = await Assert.ThrowsAsync<FallaDeLaBaseDeReclamacionesException>(() => Agregar(Hoja(1, "clave-b")));

        Assert.Equal(("23505", "hojas_numero_unico"), (falla.CodigoDeEstado, falla.Restriccion));
        Assert.DoesNotContain("Ana", falla.ToString(), StringComparison.Ordinal);
    }

    // ---------- Consultas ----------

    [Fact]
    public async Task Las_hojas_se_consultan_por_numero_por_correo_y_se_listan_por_estado_y_por_fecha_descendente()
    {
        var a = Hoja(1, "clave-a", fecha: Inicio);
        var b = Hoja(2, "clave-b", fecha: Inicio.AddHours(1));
        var c = Hoja(3, "clave-c", correo: "otra@example.com", fecha: Inicio.AddHours(2));
        foreach (var hoja in new[] { a, b, c })
        {
            await Agregar(hoja);
        }

        Assert.Equal(ResultadoDeTransicion.Hecha, b.Descartar(Inicio.AddHours(3)));
        await Guardar(b);

        Assert.Equal(a.Id, (await bd.Unidad.Ejecutar(token => bd.Hojas.ObtenerPorNumero(a.Numero, token)))!.Id);
        Assert.Null(await bd.Unidad.Ejecutar(token => bd.Hojas.ObtenerPorNumero(NumeroDeHoja.Crear(2026, 999), token)));
        Assert.Equal(2, await bd.Unidad.Ejecutar(token => bd.Vinculos.ReclamarDelCorreo(Cuenta, "ana@example.com", token)));
        var delCorreo = await bd.Unidad.Ejecutar(token => bd.Vinculos.ListarDeLaCuenta(Cuenta, 1, 10, token));
        Assert.Equal(new[] { b.Id, a.Id }, delCorreo.Hojas.Select(h => h.Id));
        Assert.Equal(2, delCorreo.Total);
        Assert.Equal(1, await bd.Unidad.Ejecutar(token => bd.Vinculos.ContarPendientesDeLaCuenta(Cuenta, token)));
        var sinEstado = await bd.Unidad.Ejecutar(token => bd.Hojas.Listar(null, 1, 10, token));
        Assert.Equal(new[] { c.Id, a.Id }, sinEstado.Hojas.Select(h => h.Id));
        var descartadas = await bd.Unidad.Ejecutar(token => bd.Hojas.Listar(EstadoDeHoja.Descartado, 1, 10, token));
        Assert.Equal(b.Id, Assert.Single(descartadas.Hojas).Id);
        var segunda = await bd.Unidad.Ejecutar(token => bd.Hojas.Listar(null, 2, 1, token));
        Assert.Equal((a.Id, 2), (Assert.Single(segunda.Hojas).Id, segunda.Total));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-05")]
    public async Task Marcar_la_cuenta_cancelada_oculta_las_hojas_del_correo_y_es_idempotente()
    {
        await Agregar(Hoja(1, "clave-a"));
        await Agregar(Hoja(2, "clave-b"));
        await Agregar(Hoja(3, "clave-c", correo: "otra@example.com"));

        Assert.Equal(2, await bd.Unidad.Ejecutar(token => bd.Vinculos.ReclamarDelCorreo(Cuenta, "ana@example.com", token)));

        // Marca y desvincula, en una sola sentencia, las de la cuenta (y las sin cuenta de su correo verificado); repetirla no cambia nada.
        Assert.Equal(2, await bd.Unidad.Ejecutar(token => bd.Vinculos.MarcarYDesvincularDeLaCuenta(Cuenta, "ana@example.com", token)));
        Assert.Equal(0, await bd.Unidad.Ejecutar(token => bd.Vinculos.MarcarYDesvincularDeLaCuenta(Cuenta, "ana@example.com", token)));

        Assert.Equal(0, (await bd.Unidad.Ejecutar(token => bd.Vinculos.ListarDeLaCuenta(Cuenta, 1, 10, token))).Total);
        Assert.Equal(0, await bd.Unidad.Ejecutar(token => bd.Vinculos.ContarPendientesDeLaCuenta(Cuenta, token)));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE cuenta_id IS NOT NULL AND de_cuenta_cancelada"));
        Assert.Equal(1, await bd.Unidad.Ejecutar(token => bd.Vinculos.ReclamarDelCorreo(OtraCuenta, "otra@example.com", token)));
        Assert.Equal(1, (await bd.Unidad.Ejecutar(token => bd.Vinculos.ListarDeLaCuenta(OtraCuenta, 1, 10, token))).Total);
    }

    [Fact]
    public async Task Marcar_la_cuenta_cancelada_se_revierte_con_la_transaccion()
    {
        await Agregar(Hoja(1, "clave-a"));

        Assert.Equal(1, await bd.Unidad.Ejecutar(token => bd.Vinculos.ReclamarDelCorreo(Cuenta, "ana@example.com", token)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => bd.Unidad.Ejecutar<int>(
            async token =>
            {
                Assert.Equal(1, await bd.Vinculos.MarcarYDesvincularDeLaCuenta(Cuenta, "ana@example.com", token));
                throw new InvalidOperationException("Identidad falla");
            }));

        Assert.Equal(1, await bd.Unidad.Ejecutar(token => bd.Vinculos.ContarPendientesDeLaCuenta(Cuenta, token)));
    }

    // ---------- Bloqueo ----------

    [Fact]
    public async Task La_lectura_para_actualizar_espera_a_la_fila_bloqueada_y_vence_como_no_disponible()
    {
        var hoja = Hoja(1, "clave-a");
        await Agregar(hoja);
        var sostiene = new TaskCompletionSource();
        var libera = new TaskCompletionSource();
        var primera = bd.Unidad.Ejecutar(
            async token =>
            {
                Assert.NotNull(await bd.Hojas.Obtener(hoja.Id, paraActualizar: true, token));
                sostiene.SetResult();
                await libera.Task;
                return 0;
            });
        await sostiene.Task;

        await Assert.ThrowsAsync<HojasNoDisponiblesException>(() => Leer(hoja.Id, paraActualizar: true));

        libera.SetResult();
        await primera;
        Assert.NotNull(await Leer(hoja.Id, paraActualizar: true));
    }

    // ---------- Inmutabilidad y privilegios ----------

    private async Task<PostgresException> FallaComo(string rol, string sql, bool deAcceso = false)
    {
        var fuente = deAcceso ? bd.Acceso : bd.Administracion;
        await using var conexion = await fuente.OpenConnectionAsync();
        await using var comando = new NpgsqlCommand($"SET ROLE {rol}; {sql}", conexion);
        return await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
    }

    [Theory]
    [InlineData("UPDATE reclamaciones.hojas_reclamacion SET nombre = 'otro'")]
    [InlineData("UPDATE reclamaciones.hojas_reclamacion SET detalle = 'otro'")]
    [InlineData("UPDATE reclamaciones.hojas_reclamacion SET monto_reclamado = 1")]
    [InlineData("UPDATE reclamaciones.hojas_reclamacion SET anonimizada_en = now()")]
    [InlineData("UPDATE reclamaciones.hojas_reclamacion SET fecha_registro = now()")]
    [InlineData("DELETE FROM reclamaciones.hojas_reclamacion")]
    [InlineData("TRUNCATE reclamaciones.hojas_reclamacion")]
    [InlineData("UPDATE reclamaciones.secuencias_de_hoja SET anio = 2000")]
    [InlineData("DELETE FROM reclamaciones.secuencias_de_hoja")]
    [Trait("Criterio", "AC-REC-05")]
    public async Task El_rol_de_la_aplicacion_no_puede_alterar_lo_original_ni_borrar_ni_vaciar(string sql)
    {
        await Agregar(Hoja(1, "clave-a"));
        await bd.Unidad.Ejecutar(token => bd.Hojas.SiguienteSecuencia(2026, token));

        var falla = await FallaComo("rol_reclamaciones", sql, deAcceso: true);

        Assert.Equal("42501", falla.SqlState);
    }

    [Theory]
    [InlineData("DELETE FROM reclamaciones.hojas_reclamacion")]
    [InlineData("TRUNCATE reclamaciones.hojas_reclamacion")]
    [InlineData("UPDATE reclamaciones.hojas_reclamacion SET monto_reclamado = 1")]
    [InlineData("UPDATE reclamaciones.hojas_reclamacion SET nombre = 'otro'")]
    [InlineData("UPDATE reclamaciones.hojas_reclamacion SET numero = '2026-000999'")]
    [InlineData("UPDATE reclamaciones.hojas_reclamacion SET anonimizada_en = now()")]
    [InlineData("UPDATE reclamaciones.secuencias_de_hoja SET ultimo = 0")]
    [InlineData("DELETE FROM reclamaciones.secuencias_de_hoja")]
    [Trait("Criterio", "AC-REC-05")]
    public async Task Tampoco_el_dueno_puede_alterar_lo_original_borrar_ni_vaciar_ni_hacer_retroceder_la_secuencia(string sql)
    {
        var hoja = Hoja(1, "clave-a");
        await Agregar(hoja);
        await bd.Unidad.Ejecutar(token => bd.Hojas.SiguienteSecuencia(2026, token));
        await bd.Unidad.Ejecutar(token => bd.Hojas.SiguienteSecuencia(2026, token));

        var falla = await FallaComo("reclamaciones_dueno", sql);

        Assert.Contains(falla.SqlState, new[] { "23000", "23514" });
    }

    [Fact]
    public async Task La_respuesta_se_escribe_una_vez_el_estado_solo_avanza_y_un_aviso_enviado_no_se_deshace()
    {
        var hoja = Hoja(1, "clave-a");
        await Agregar(hoja);
        hoja.Responder("Primera respuesta.", Guid.NewGuid(), Inicio.AddHours(1));
        await Guardar(hoja);

        var otraRespuesta = await FallaComo("reclamaciones_dueno", "UPDATE reclamaciones.hojas_reclamacion SET respuesta = 'Otra respuesta.'");
        var haciaAtras = await FallaComo("reclamaciones_dueno", "UPDATE reclamaciones.hojas_reclamacion SET estado = 'pendiente', respuesta = NULL, respondida_en = NULL, respondida_por = NULL, aviso_estado = NULL");
        hoja.MarcarAvisoEnviado(Inicio.AddHours(2));
        await Guardar(hoja);
        var avisoAtras = await FallaComo("reclamaciones_dueno", "UPDATE reclamaciones.hojas_reclamacion SET aviso_estado = 'pendiente', aviso_enviado_en = NULL");
        Assert.True(hoja.MarcarDeCuentaCancelada());
        await Guardar(hoja);
        var cancelada = await FallaComo("reclamaciones_dueno", "UPDATE reclamaciones.hojas_reclamacion SET de_cuenta_cancelada = false");

        Assert.All(new[] { otraRespuesta, haciaAtras, avisoAtras }, f => Assert.Equal("23000", f.SqlState));
        Assert.Equal("23000", cancelada.SqlState);
    }

    [Fact]
    public async Task Una_hoja_descartada_o_respondida_no_cambia_de_estado_por_el_dueno()
    {
        var hoja = Hoja(1, "clave-a");
        await Agregar(hoja);
        hoja.Descartar(Inicio.AddHours(1));
        await Guardar(hoja);

        var falla = await FallaComo("reclamaciones_dueno", "UPDATE reclamaciones.hojas_reclamacion SET estado = 'pendiente', descartada_en = NULL");

        Assert.Equal("23000", falla.SqlState);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-13")]
    public async Task Un_check_fallido_no_deja_los_datos_personales_en_el_error()
    {
        var invalida = Hoja(1, "clave-a", documento: new string('9', 25));

        var falla = await Assert.ThrowsAsync<FallaDeLaBaseDeReclamacionesException>(() => Agregar(invalida));

        Assert.Equal("23514", falla.CodigoDeEstado);
        var texto = falla.ToString();
        Assert.DoesNotContain("Ana", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Lima 123", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", texto, StringComparison.Ordinal);
        Assert.Null(falla.InnerException);
    }

    // ---------- Anonimización ----------

    private async Task<Guid> InsertarComoAdministrador(DateTimeOffset fecha, string clave, string estado = "pendiente")
    {
        var id = Guid.NewGuid();
        await using var comando = bd.Administracion.CreateCommand(
            """
            INSERT INTO reclamaciones.hojas_reclamacion (id, numero, anio, tipo, nombre, documento, domicilio, correo, correo_normalizado, bien_contratado, monto_reclamado, detalle, pedido_referencia, contenido_hash, fecha_registro, clave_hash, estado,
                                                         respuesta, respondida_en, respondida_por, aviso_estado)
            VALUES (@id, @numero, 2024, 'queja', 'Ana Pérez', '12345678', 'Jr. Lima 123', 'ana@example.com', 'ana@example.com', 'Libro', 10.50, 'Detalle de la queja', 'PED-9', 'hash', @fecha, @clave, @estado,
                    CASE WHEN @estado = 'respondido' THEN 'Respuesta' END, CASE WHEN @estado = 'respondido' THEN now() END, CASE WHEN @estado = 'respondido' THEN gen_random_uuid() END, CASE WHEN @estado = 'respondido' THEN 'pendiente' END)
            """);
        comando.Parameters.AddWithValue("id", id);
        comando.Parameters.AddWithValue("numero", $"2024-{Math.Abs(clave.GetHashCode()) % 999_000 + 1:000000}");
        comando.Parameters.AddWithValue("fecha", fecha.ToUniversalTime());
        comando.Parameters.AddWithValue("clave", clave);
        comando.Parameters.AddWithValue("estado", estado);
        await comando.ExecuteNonQueryAsync();
        return id;
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public async Task La_funcion_anonimiza_exactamente_lo_que_dice_la_politica_de_dos_anios_por_dia_de_Lima()
    {
        var ahora = new DateTimeOffset(await bd.Consultar<DateTime>("SELECT now()"), TimeSpan.Zero);
        var casos = new List<(Guid Id, DateTimeOffset Fecha)>();
        var contador = 0;
        foreach (var dias in new[] { -3, -2, -1, 0, 1, 2 })
        {
            foreach (var horas in new[] { -7, -2, 0, 3, 8 })
            {
                var fecha = ahora.AddYears(-2).AddDays(dias).AddHours(horas);
                casos.Add((await InsertarComoAdministrador(fecha, $"clave-{contador++}"), fecha));
            }
        }

        var cantidad = await bd.Unidad.Ejecutar(token => bd.Anonimizador.Anonimizar(token));
        var ahoraDeLaBase = new DateTimeOffset(await bd.Consultar<DateTime>("SELECT now()"), TimeSpan.Zero);

        var esperadas = casos.Where(c => PoliticaDeConservacion.DebeAnonimizarse(c.Fecha, ahoraDeLaBase)).Select(c => c.Id).ToHashSet();
        var anonimizadas = new HashSet<Guid>();
        foreach (var (id, _) in casos)
        {
            if (await bd.Consultar<bool>($"SELECT anonimizada_en IS NOT NULL FROM reclamaciones.hojas_reclamacion WHERE id = '{id}'"))
            {
                anonimizadas.Add(id);
            }
        }

        Assert.NotEmpty(esperadas);
        Assert.True(esperadas.Count < casos.Count);
        Assert.Equal(esperadas.Count, cantidad);
        Assert.True(esperadas.SetEquals(anonimizadas));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public async Task Anonimizar_borra_lo_personal_redacta_el_detalle_y_la_respuesta_y_conserva_lo_demas_y_es_idempotente()
    {
        var vieja = DateTimeOffset.UtcNow.AddYears(-3);
        var pendiente = await InsertarComoAdministrador(vieja, "clave-pendiente");
        var respondida = await InsertarComoAdministrador(vieja, "clave-respondida", "respondido");
        var reciente = await InsertarComoAdministrador(DateTimeOffset.UtcNow.AddMonths(-3), "clave-reciente");

        Assert.Null(await bd.Unidad.Ejecutar(token => bd.Anonimizador.UltimaCorridaEn(token)));
        Assert.Equal(2, await bd.Unidad.Ejecutar(token => bd.Anonimizador.Anonimizar(token)));
        Assert.Equal(0, await bd.Unidad.Ejecutar(token => bd.Anonimizador.Anonimizar(token)));

        var hoja = await Leer(pendiente);
        Assert.Equal((DatosDelConsumidor.ValorAnonimo, DatosDelConsumidor.ValorAnonimo, DatosDelConsumidor.ValorAnonimo, DatosDelConsumidor.ValorAnonimo), (hoja!.Datos.Nombre, hoja.Datos.Documento, hoja.Datos.Domicilio, hoja.Datos.Correo));
        Assert.Equal((HojaDeReclamacion.TextoRedactado, null, true), (hoja.Detalle, hoja.ContenidoHash, hoja.EstaAnonimizada));
        Assert.Equal(("queja", 10.50m, "PED-9", "pendiente"), (hoja.Tipo.ATexto(), hoja.MontoReclamado, hoja.PedidoReferencia, EstadoATexto(hoja.Estado)));
        Assert.Null(await bd.Consultar<string>($"SELECT correo_normalizado FROM reclamaciones.hojas_reclamacion WHERE id = '{pendiente}'"));
        var conRespuesta = await Leer(respondida);
        Assert.Equal((HojaDeReclamacion.TextoRedactado, EstadoDeHoja.Respondido), (conRespuesta!.Respuesta, conRespuesta.Estado));
        Assert.False((await Leer(reciente))!.EstaAnonimizada);
        Assert.NotNull(await bd.Unidad.Ejecutar(token => bd.Anonimizador.UltimaCorridaEn(token)));
    }

    [Fact]
    public async Task Una_hoja_anonimizada_ya_no_admite_cambios_ni_respuesta()
    {
        var id = await InsertarComoAdministrador(DateTimeOffset.UtcNow.AddYears(-3), "clave-vieja");
        await bd.Unidad.Ejecutar(token => bd.Anonimizador.Anonimizar(token));

        var falla = await FallaComo("reclamaciones_dueno", $"UPDATE reclamaciones.hojas_reclamacion SET estado = 'descartado', descartada_en = now() WHERE id = '{id}'");

        Assert.Equal("23000", falla.SqlState);
    }

    [Fact]
    public async Task Solo_el_rol_de_la_aplicacion_puede_ejecutar_la_funcion_y_los_textos_coinciden_con_el_dominio()
    {
        await using var conexion = await bd.Administracion.OpenConnectionAsync();
        await using var comando = new NpgsqlCommand("CREATE ROLE intruso NOLOGIN; SET ROLE intruso; SELECT reclamaciones.anonimizar_hojas()", conexion);
        var falla = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());

        Assert.Equal("42501", falla.SqlState);
        Assert.Equal(HojaDeReclamacion.TextoRedactado, DefinicionDeReclamaciones.TextoRedactado);
        Assert.Equal(DatosDelConsumidor.ValorAnonimo, DefinicionDeReclamaciones.ValorAnonimo);
    }

    private static string EstadoATexto(EstadoDeHoja estado) => RepositorioDeHojasPostgres.EstadoATexto(estado);
}
