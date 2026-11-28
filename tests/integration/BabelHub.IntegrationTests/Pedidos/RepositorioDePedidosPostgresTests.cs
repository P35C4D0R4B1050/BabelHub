using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Infrastructure.Pedidos;
using BabelHub.Ecommerce.Domain.Pedidos;
using Npgsql;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>
/// El esquema y el repositorio de Pedidos sobre PostgreSQL real (T28; PED-21, PED-33, PED-34; AC-PED-01, 17, 28, 29): la ida y vuelta de todos los campos, el contacto que solo se escribe al crear y se anonimiza, el listado que no lo carga, los únicos de las operaciones en curso, el bloqueo <c>FOR UPDATE</c>
/// con su <c>lock_timeout</c>, la concesión de la reanudación, la fecha de Lima de la retención y el permiso mínimo del rol.
/// </summary>
[Collection(ColeccionPostgresDePedidos.Nombre)]
public sealed class RepositorioDePedidosPostgresTests(PostgresDePedidosFixture bd) : IAsyncLifetime
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static int _contador;

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Numero() => "ABCD23456" + "ABCDEFGHJKLMNPQRSTUVWXYZ"[Interlocked.Increment(ref _contador) % 24];

    private static Pedido Domicilio(Guid? cuenta = null, string? numero = null)
    {
        var lineas = new[]
        {
            new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Cien años de soledad", "9780307474728", 2, 45.50m, 91m, "LIM", true, Guid.NewGuid(), new DateOnly(2026, 11, 25)),
            new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), true, "El Aleph", null, 1, 30m, 30m, null, false, null, null),
        };
        var contacto = ContactoDeEntrega.Crear("Ana Pérez", "987654321", "Jr. Lima 123");
        return Pedido.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), numero ?? Numero(), cuenta ?? Cuenta, TipoDeEntregaDelPedido.Domicilio, null, "ayacucho", 12m, 133m, contacto, new("t-1", Ahora), new("p-1", Ahora), new("rd-1", Ahora), lineas, Ahora)).Pedido!;
    }

    private static Pedido Recojo(string sucursal = "LIM")
    {
        var linea = new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Ficciones", null, 1, 50m, 50m, sucursal, false, null, null);
        return Pedido.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), Numero(), Cuenta, TipoDeEntregaDelPedido.Recojo, sucursal, null, 0m, 50m, null, new("t-1", Ahora), new("p-1", Ahora), null, [linea], Ahora)).Pedido!;
    }

    private Task<T> En<T>(Func<CancellationToken, Task<T>> trabajo) => bd.Unidad.Ejecutar(trabajo);

    [Fact]
    [Trait("Criterio", "AC-PED-01")]
    public async Task Un_pedido_a_domicilio_se_guarda_y_se_lee_con_todos_sus_campos_y_su_contacto()
    {
        var pedido = Domicilio();

        Assert.True(await bd.Pedidos.Agregar(pedido));
        var leido = await bd.Pedidos.Obtener(pedido.Id, conBloqueo: false);

        Assert.NotNull(leido);
        Assert.Equal((pedido.Numero.Valor, pedido.CuentaId, pedido.Tipo, pedido.RegionDeEntrega, pedido.CostoDeEnvio, pedido.Total, pedido.Estado), (leido.Numero.Valor, leido.CuentaId, leido.Tipo, leido.RegionDeEntrega, leido.CostoDeEnvio, leido.Total, leido.Estado));
        Assert.Equal(("Ana Pérez", "987654321", "Jr. Lima 123"), (leido.Contacto!.Nombre, leido.Contacto.Telefono, leido.Contacto.Direccion));
        Assert.Equal((pedido.Terminos, pedido.Privacidad, pedido.ReembolsoDigital), (leido.Terminos, leido.Privacidad, leido.ReembolsoDigital));
        Assert.Equal(pedido.Lineas.Select(l => (l.Id, l.EdicionId, l.EsDigital, l.Titulo, l.Isbn, l.Cantidad, l.PrecioUnitario, l.Importe, l.CodigoDeFuente, l.RequiereTraslado, l.TrasladoId, l.FechaEstimadaDeLlegada, l.EstadoImpreso, l.EstadoDigital)),
            leido.Lineas.Select(l => (l.Id, l.EdicionId, l.EsDigital, l.Titulo, l.Isbn, l.Cantidad, l.PrecioUnitario, l.Importe, l.CodigoDeFuente, l.RequiereTraslado, l.TrasladoId, l.FechaEstimadaDeLlegada, l.EstadoImpreso, l.EstadoDigital)));
        Assert.True(await bd.Pedidos.Existe(pedido.Id));
        Assert.False(await bd.Pedidos.Existe(Guid.NewGuid()));
    }

    [Fact]
    public async Task Una_colision_de_numero_o_de_id_dice_ya_existia_sin_abortar_la_transaccion_del_llamador()
    {
        var primero = Recojo();
        Assert.True(await bd.Pedidos.Agregar(primero));
        var mismoNumero = Domicilio(numero: primero.Numero.Valor);

        var resultados = await En(async token => (await bd.Pedidos.Agregar(mismoNumero, token), await bd.Pedidos.Agregar(primero, token), await bd.Pedidos.Existe(primero.Id, token)));

        Assert.Equal((false, false, true), resultados);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-28")]
    public async Task Anonimizar_el_contacto_lo_borra_conserva_lo_demas_y_un_listado_nunca_lo_carga()
    {
        var pedido = Domicilio();
        await bd.Pedidos.Agregar(pedido);

        var enLista = (await bd.Pedidos.Listar(new CriterioDeListado(Cuenta, null, false, null, 1, 10))).Pedidos.Single();
        Assert.Null(enLista.Contacto);

        // Guardar un pedido leído en un listado jamás borra el contacto guardado.
        await bd.Pedidos.Actualizar(enLista);
        Assert.NotNull((await bd.Pedidos.Obtener(pedido.Id, false))!.Contacto);

        var aAnonimizar = (await bd.Pedidos.Obtener(pedido.Id, conBloqueo: true))!;
        Assert.True(aAnonimizar.AnonimizarContacto(Ahora));
        await bd.Pedidos.Actualizar(aAnonimizar);
        var leido = (await bd.Pedidos.Obtener(pedido.Id, false))!;

        Assert.Null(leido.Contacto);
        Assert.NotNull(leido.ContactoAnonimizadoEn);
        Assert.Equal((pedido.Total, pedido.Lineas.Count, pedido.CuentaId), (leido.Total, leido.Lineas.Count, leido.CuentaId));
        Assert.Null(await bd.Consultar<string>("SELECT contacto_nombre FROM pedidos.pedidos"));
    }

    [Fact]
    public async Task Los_cambios_de_estado_de_las_lineas_se_guardan_y_el_pedido_se_recalcula()
    {
        var pedido = Recojo();
        await bd.Pedidos.Agregar(pedido);
        var cargado = (await bd.Pedidos.Obtener(pedido.Id, true))!;
        var ids = cargado.Lineas.Select(l => l.Id).ToList();
        cargado.PrepararLineas(ids, Ahora);
        cargado.MarcarListas(ids, Ahora);
        cargado.EntregarLineas(ids, Ahora.AddHours(1));
        await bd.Pedidos.Actualizar(cargado);

        var leido = (await bd.Pedidos.Obtener(pedido.Id, false))!;

        Assert.Equal(EstadoDePedido.Entregado, leido.Estado);
        Assert.Equal(Ahora.AddHours(1), leido.FechaEstadoFinal);
        Assert.Equal((EstadoDeLineaImpresa.Entregado, Ahora.AddHours(1)), (leido.Lineas[0].EstadoImpreso!.Value, leido.Lineas[0].EntregadaEn!.Value));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-17")]
    public async Task Una_segunda_anulacion_en_curso_sobre_el_mismo_pedido_o_linea_se_rechaza_por_el_unico()
    {
        var pedido = Recojo();
        await bd.Pedidos.Agregar(pedido);
        var plan = pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro).Plan!;
        var iniciador = new IniciadorDeOperacion(Guid.NewGuid(), "administrador-central", null);
        await bd.Pedidos.AgregarAnulacion(Anulacion.Iniciar(Guid.NewGuid(), pedido.Id, plan, iniciador, Ahora));

        var segunda = await Assert.ThrowsAsync<OperacionRechazadaException>(() => bd.Pedidos.AgregarAnulacion(Anulacion.Iniciar(Guid.NewGuid(), pedido.Id, plan, iniciador, Ahora)));
        var entrega = Entrega.Iniciar(Guid.NewGuid(), pedido.Id, [pedido.Lineas[0].Id], new IniciadorDeOperacion(Guid.NewGuid(), "encargado-de-sucursal", null), Ahora);
        await bd.Pedidos.AgregarEntrega(entrega);
        var otraEntrega = Entrega.Iniciar(Guid.NewGuid(), pedido.Id, [pedido.Lineas[0].Id], new IniciadorDeOperacion(Guid.NewGuid(), "encargado-de-sucursal", null), Ahora);
        var repetida = await Assert.ThrowsAsync<OperacionRechazadaException>(() => bd.Pedidos.AgregarEntrega(otraEntrega));

        Assert.Equal("pedido-operacion-en-curso", segunda.Error.Clave);
        Assert.Equal("pedido-operacion-en-curso", repetida.Error.Clave);
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM pedidos.anulaciones"));
    }

    [Fact]
    public async Task Una_anulacion_se_guarda_con_sus_pasos_y_se_actualiza_al_ajustarse_su_plan_y_al_completarse()
    {
        var pedido = Domicilio();
        await bd.Pedidos.Agregar(pedido);
        var plan = pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.PagoDuplicado).Plan!;
        var anulacion = Anulacion.Iniciar(Guid.NewGuid(), pedido.Id, plan, new IniciadorDeOperacion(Guid.NewGuid(), "administrador-central", Guid.NewGuid()), Ahora);
        anulacion.MarcarPaso(PasoDeAnulacion.EnviosCancelados, Ahora);
        await bd.Pedidos.AgregarAnulacion(anulacion);

        var leida = Assert.Single(await bd.Pedidos.AnulacionesEnCurso(pedido.Id));
        Assert.Equal((anulacion.Motivo, anulacion.Monto, anulacion.IncluyeEnvio, anulacion.HayDigitales, anulacion.Iniciador), (leida.Motivo, leida.Monto, leida.IncluyeEnvio, leida.HayDigitales, leida.Iniciador));
        Assert.Equal(anulacion.LineaIds, leida.LineaIds);
        Assert.NotNull(leida.PasoConfirmadoEn(PasoDeAnulacion.EnviosCancelados));
        Assert.Equal([PasoDeAnulacion.ReservasLiberadas, PasoDeAnulacion.ReembolsoRegistrado, PasoDeAnulacion.AccesosRevocados], leida.PasosPendientes);

        leida.MarcarPaso(PasoDeAnulacion.ReservasLiberadas, Ahora);
        leida.RegistrarReembolso(Guid.NewGuid(), Ahora);
        leida.MarcarPaso(PasoDeAnulacion.AccesosRevocados, Ahora);
        Assert.True(leida.Completar(Ahora));
        await bd.Pedidos.ActualizarAnulacion(leida);

        Assert.Empty(await bd.Pedidos.AnulacionesEnCurso(pedido.Id));
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM pedidos.anulaciones WHERE estado = 'completada' AND reembolso_id IS NOT NULL"));
        // Con la anulación completada una nueva puede entrar: el único solo vale mientras está en curso.
        await bd.Pedidos.AgregarAnulacion(Anulacion.Iniciar(Guid.NewGuid(), pedido.Id, plan, new IniciadorDeOperacion(Guid.NewGuid(), "administrador-central", null), Ahora));
    }

    [Fact]
    public async Task Una_entrega_guarda_el_cierre_confirmado_por_linea_y_la_reanudacion_toma_cada_fila_una_sola_vez_con_una_concesion()
    {
        var pedido = Recojo();
        await bd.Pedidos.Agregar(pedido);
        var entrega = Entrega.Iniciar(Guid.NewGuid(), pedido.Id, [pedido.Lineas[0].Id], new IniciadorDeOperacion(Guid.NewGuid(), "encargado-de-sucursal", Guid.NewGuid()), Ahora);
        await bd.Pedidos.AgregarEntrega(entrega);
        entrega.ConfirmarCierre(pedido.Lineas[0].Id, ResultadoDelCierre.YaCerrada, Ahora);
        await bd.Pedidos.ActualizarEntrega(entrega);

        var leida = Assert.Single(await bd.Pedidos.EntregasEnCurso(pedido.Id));
        Assert.Equal((ResultadoDelCierre.YaCerrada, entrega.Lineas[0].ClaveDeCierre), (leida.Lineas[0].ResultadoDelCierre!.Value, leida.Lineas[0].ClaveDeCierre));

        var primera = await bd.Pedidos.OperacionesEnCursoParaReanudar(Ahora.AddMinutes(5), 10);
        var segunda = await bd.Pedidos.OperacionesEnCursoParaReanudar(Ahora.AddMinutes(5), 10);

        Assert.Single(primera.Entregas);
        Assert.Empty(segunda.Entregas);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-17")]
    public async Task Un_pedido_bloqueado_por_otra_transaccion_agota_el_lock_timeout_y_responde_conflicto()
    {
        var pedido = Recojo();
        await bd.Pedidos.Agregar(pedido);
        var bloqueado = new TaskCompletionSource();
        var soltar = new TaskCompletionSource();
        var primera = En(async token =>
        {
            await bd.Pedidos.Obtener(pedido.Id, conBloqueo: true, token);
            bloqueado.SetResult();
            await soltar.Task;
            return 0;
        });
        await bloqueado.Task;

        await Assert.ThrowsAsync<EsperaDeBloqueoDeUnPedidoAgotadaException>(() => bd.Pedidos.Obtener(pedido.Id, conBloqueo: true));

        soltar.SetResult();
        await primera;
    }

    [Fact]
    [Trait("Criterio", "AC-PED-29")]
    public async Task La_retencion_cuenta_por_fecha_de_Lima_y_anonimiza_tambien_al_iniciador_de_las_operaciones()
    {
        // Anulado el 2024-11-21 a las 02:00 UTC = 2024-11-20 21:00 en Lima: cumple los dos años el 2026-11-20 en Lima.
        var anuladoEn = new DateTimeOffset(2024, 11, 21, 2, 0, 0, TimeSpan.Zero);
        var vencible = Domicilio();
        await bd.Pedidos.Agregar(vencible);
        var cargado = (await bd.Pedidos.Obtener(vencible.Id, true))!;
        cargado.AplicarAnulacion(cargado.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro).Plan!, anuladoEn);
        await bd.Pedidos.Actualizar(cargado);
        var fila = Anulacion.Iniciar(Guid.NewGuid(), vencible.Id, Domicilio().PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro).Plan!, new IniciadorDeOperacion(Guid.NewGuid(), "administrador-central", null), anuladoEn);
        await bd.Ejecutar($"UPDATE pedidos.pedidos SET creado_en = creado_en WHERE id = '{vencible.Id}'");
        var enCurso = Domicilio();
        await bd.Pedidos.Agregar(enCurso);

        Assert.Empty(await bd.Pedidos.ListarVencidosParaDepurar(new DateOnly(2026, 11, 19), 10));
        var vencidos = await bd.Pedidos.ListarVencidosParaDepurar(new DateOnly(2026, 11, 20), 10);

        Assert.Equal([vencible.Id], vencidos.Select(p => p.Id));
        Assert.NotNull(fila);

        var depurado = vencidos.Single();
        Assert.True(depurado.AnonimizarPorRetencion(new DateOnly(2026, 11, 20), Ahora));
        await bd.Pedidos.Actualizar(depurado);
        Assert.Empty(await bd.Pedidos.ListarVencidosParaDepurar(new DateOnly(2026, 11, 20), 10));
    }

    [Fact]
    public async Task Los_iniciadores_se_anonimizan_una_sola_vez_y_la_ultima_depuracion_se_registra()
    {
        var pedido = Recojo();
        await bd.Pedidos.Agregar(pedido);
        await bd.Pedidos.AgregarAnulacion(Anulacion.Iniciar(Guid.NewGuid(), pedido.Id, pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro).Plan!, new IniciadorDeOperacion(Guid.NewGuid(), "administrador-central", Guid.NewGuid()), Ahora));

        Assert.Equal(1, await bd.Pedidos.AnonimizarIniciadoresDeOperaciones(pedido.Id));
        Assert.Equal(0, await bd.Pedidos.AnonimizarIniciadoresDeOperaciones(pedido.Id));
        Assert.True(Assert.Single(await bd.Pedidos.AnulacionesEnCurso(pedido.Id)).Iniciador.EstaAnonimizado);

        Assert.Null(await bd.Pedidos.UltimaDepuracionExitosaEn());
        await bd.Pedidos.RegistrarDepuracionExitosa(Ahora);
        await bd.Pedidos.RegistrarDepuracionExitosa(Ahora.AddDays(1));
        Assert.Equal(Ahora.AddDays(1), await bd.Pedidos.UltimaDepuracionExitosaEn());
    }

    [Fact]
    public async Task Las_colas_de_liberacion_y_los_estados_de_traslado_se_guardan_cuentan_y_se_purgan()
    {
        var pedido = Domicilio();
        await bd.Pedidos.Agregar(pedido);
        await bd.Pedidos.RegistrarLiberacionDeContacto(pedido.Id, Ahora);
        await bd.Pedidos.RegistrarLiberacionDeContacto(pedido.Id, Ahora.AddHours(1));
        await bd.Pedidos.AnotarIntentoDeLiberacionDeContacto(pedido.Id, Ahora);
        var pendiente = Assert.Single(await bd.Pedidos.LiberacionesDeContactoPendientes(10));
        Assert.Equal((pedido.Id, Ahora, 1), (pendiente.PedidoId, pendiente.Desde, pendiente.Intentos));
        await bd.Pedidos.QuitarLiberacionDeContacto(pedido.Id);
        Assert.Empty(await bd.Pedidos.LiberacionesDeContactoPendientes(10));

        var linea = pedido.Lineas[0].Id;
        await bd.Pedidos.RegistrarLiberacionesDeReserva(pedido.Id, [linea], "no-recogido", Ahora);
        await bd.Pedidos.RegistrarLiberacionesDeReserva(pedido.Id, [linea], "no-recogido", Ahora);
        await bd.Pedidos.AnotarIntentoDeLiberacion([linea]);
        Assert.Equal(1, Assert.Single(await bd.Pedidos.LiberacionesDeReservaPendientes(10)).Intentos);
        await bd.Pedidos.QuitarLiberacionesDeReserva([linea]);
        Assert.Empty(await bd.Pedidos.LiberacionesDeReservaPendientes(10));

        var estado = new EstadoDeTrasladoRegistrado("traslado:a:en-transito", Guid.NewGuid(), pedido.Id, "en-transito", "huella", Ahora);
        await bd.Pedidos.GuardarEstadoDeTraslado(estado);
        await bd.Pedidos.GuardarEstadoDeTraslado(estado);
        Assert.Equal(estado, await bd.Pedidos.ObtenerEstadoDeTraslado(estado.EventoId));
        Assert.Equal(0, await bd.Pedidos.PurgarEstadosDeTraslado(Ahora.AddDays(-1)));
        Assert.Equal(1, await bd.Pedidos.PurgarEstadosDeTraslado(Ahora.AddDays(90)));
        Assert.Null(await bd.Pedidos.ObtenerEstadoDeTraslado(estado.EventoId));
    }

    [Fact]
    public async Task El_rol_no_puede_borrar_pedidos_ni_cambiar_importes_y_el_esquema_rechaza_un_contacto_en_un_pedido_de_recojo()
    {
        var pedido = Recojo();
        await bd.Pedidos.Agregar(pedido);

        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var conexion = await bd.Acceso.OpenConnectionAsync();
            await using var rol = new NpgsqlCommand("SET ROLE rol_pedidos", conexion);
            await rol.ExecuteNonQueryAsync();
            await using var borrar = new NpgsqlCommand("DELETE FROM pedidos.pedidos", conexion);
            await borrar.ExecuteNonQueryAsync();
        });
        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var conexion = await bd.Acceso.OpenConnectionAsync();
            await using var rol = new NpgsqlCommand("SET ROLE rol_pedidos", conexion);
            await rol.ExecuteNonQueryAsync();
            await using var cambiar = new NpgsqlCommand("UPDATE pedidos.pedidos SET total = 1", conexion);
            await cambiar.ExecuteNonQueryAsync();
        });
        var ex = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE pedidos.pedidos SET contacto_nombre = 'x', contacto_telefono = '1', contacto_direccion = 'y' WHERE id = '{pedido.Id}'"));
        // El disparador del contacto (solo se anonimiza) o el CHECK de «solo a domicilio»: ambos rechazan con 23514.
        Assert.Equal("23514", ex.SqlState);
    }

    [Fact]
    public async Task El_contacto_guardado_no_puede_cambiar_a_otro_valor_solo_anonimizarse_y_una_anulacion_en_curso_exige_su_proximo_intento()
    {
        var pedido = Domicilio();
        await bd.Pedidos.Agregar(pedido);

        var cambio = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE pedidos.pedidos SET contacto_nombre = 'Otra persona' WHERE id = '{pedido.Id}'"));
        await bd.Ejecutar($"UPDATE pedidos.pedidos SET contacto_nombre = NULL, contacto_telefono = NULL, contacto_direccion = NULL, contacto_anonimizado_en = now() WHERE id = '{pedido.Id}'");

        Assert.Equal("23514", cambio.SqlState);
        Assert.Null(await bd.Consultar<string>("SELECT contacto_nombre FROM pedidos.pedidos"));

        var sinProximo = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar(
            $"INSERT INTO pedidos.anulaciones (id, pedido_id, origen, motivo, incluye_envio, hay_digitales, monto, estado, iniciada_por, intentos, creada_en) VALUES (gen_random_uuid(), '{pedido.Id}', 'administracion', 'otro', false, false, 1, 'en_curso', gen_random_uuid(), 0, now())"));
        Assert.Equal("anulaciones_en_curso_con_proximo_intento", sinProximo.ConstraintName);
    }

    [Fact]
    public async Task Los_conteos_y_el_listado_por_filtro_responden_solo_de_lo_pedido()
    {
        var domicilio = Domicilio();
        var recojoLima = Recojo("LIM");
        var recojoAyacucho = Recojo("AYA");
        var ajeno = Domicilio(Guid.NewGuid());
        foreach (var p in new[] { domicilio, recojoLima, recojoAyacucho, ajeno })
        {
            await bd.Pedidos.Agregar(p);
        }

        Assert.Equal(3, (await bd.Pedidos.Listar(new CriterioDeListado(Cuenta, null, false, null, 1, 10))).Total);
        Assert.Equal([recojoLima.Id], (await bd.Pedidos.Listar(new CriterioDeListado(null, "LIM", false, null, 1, 10))).Pedidos.Select(p => p.Id));
        Assert.Equal(2, (await bd.Pedidos.Listar(new CriterioDeListado(null, null, true, null, 1, 10))).Total);
        Assert.Equal(2, (await bd.Pedidos.Listar(new CriterioDeListado(null, null, false, EstadoDePedido.Confirmado, 1, 2))).Pedidos.Count);
        Assert.Equal(3, await bd.Pedidos.ContarPedidosConImpresaSinCerrar(Cuenta));
        Assert.Equal(0, await bd.Pedidos.ContarNoRecogidasSinAnularDesde(Ahora.AddDays(1)));
        Assert.Equal(4, await bd.Pedidos.ContarImpresasSinEstadoFinalDesde(Ahora.AddDays(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-13")]
    public async Task La_consulta_de_recojo_vencido_trae_solo_las_listas_de_recojo_con_mas_de_15_dias_y_no_las_de_14_ni_las_de_domicilio()
    {
        var plazo = TimeSpan.FromHours(PoliticaDeNoRecogido.PlazoEnHoras);
        var vencido = Recojo();
        var justoAntes = Recojo();
        var reciente = Recojo();
        var sinListo = Recojo();
        var aDomicilio = Domicilio();
        foreach (var pedido in new[] { vencido, justoAntes, reciente, sinListo, aDomicilio })
        {
            Assert.True(await bd.Pedidos.Agregar(pedido));
        }

        string Listo(Pedido pedido, DateTimeOffset listoEn) =>
            $"UPDATE pedidos.lineas_de_pedido SET estado = 'listo_para_recojo', listo_en = '{listoEn.UtcDateTime:O}' WHERE pedido_id = '{pedido.Id}' AND NOT es_digital";
        await bd.Ejecutar(Listo(vencido, Ahora - plazo - TimeSpan.FromHours(1)));
        await bd.Ejecutar(Listo(justoAntes, Ahora - plazo + TimeSpan.FromMinutes(1)));
        await bd.Ejecutar(Listo(reciente, Ahora - TimeSpan.FromDays(14)));
        await bd.Ejecutar(Listo(aDomicilio, Ahora - plazo - TimeSpan.FromDays(2)));

        var candidatos = await bd.Pedidos.PedidosConRecojoVencido(Ahora, 100);

        Assert.Equal([vencido.Id], candidatos);
    }
}
