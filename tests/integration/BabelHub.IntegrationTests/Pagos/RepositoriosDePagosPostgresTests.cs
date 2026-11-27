using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using Npgsql;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// T24: los repositorios de Pagos sobre PostgreSQL real con el rol <c>rol_pagos</c> (AC-PAG-14, AC-PAG-22, AC-PAG-23): ida y vuelta de la intención con sus líneas, el contacto y lo que devolvió <c>reservas.confirmar</c>; el intento activo único
/// y sus resultados sin abortar la transacción; el bloqueo <c>FOR UPDATE</c> con su espera; las compensaciones y los avisos que no se repiten; los reembolsos con su clave natural y sus reintentos; los límites inclusivos y exclusivos de las listas
/// de los trabajos; la desvinculación y la liberación del contacto escritas de verdad; y que un <c>CHECK</c> fallido no deje el contacto en el error.
/// </summary>
[Collection(ColeccionPostgresDePagos.Nombre)]
public sealed class RepositoriosDePagosPostgresTests(PostgresDePagosFixture bd) : IAsyncLifetime
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid LineaImpresa = Guid.Parse("33333333-3333-3333-3333-333333333331");

    private static readonly Guid LineaDigital = Guid.Parse("33333333-3333-3333-3333-333333333332");

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------- Construcción ----------

    private static LineaDeIntencion Impresa() =>
        new(LineaImpresa, Guid.Parse("44444444-4444-4444-4444-444444444441"), EsDigital: false, "La ciudad y los perros", "9780306406157", 2, Importe.De(50m), Importe.De(100m));

    private static LineaDeIntencion Digital() =>
        new(LineaDigital, Guid.Parse("44444444-4444-4444-4444-444444444442"), EsDigital: true, "Rayuela", "9780306406164", 1, Importe.De(25m), Importe.De(25m));

    private static TextoAceptado Texto() => new("2026-11", Inicio.AddMinutes(-5));

    private static ContactoDeEntrega Contacto() => new("Ana Pérez Quispe", "987654321", "Av. Los Olivos 123, Ayacucho");

    /// <summary>Una compra mixta a domicilio: 2 impresos de S/ 50 + 1 digital de S/ 25 + envío S/ 15 = S/ 140.</summary>
    private static InstantaneaDeIntencion Mixta(Guid cuenta) =>
        InstantaneaDeIntencion.Crear(cuenta, TipoDeEntrega.Domicilio, null, "AYA", [Impresa(), Digital()], Importe.De(15m), Importe.De(140m), "huella-a", Texto(), Texto(), Texto(), Contacto(), [new EnvioDeSucursalDeIntencion("ayacucho", Importe.De(15m))]);

    private static InstantaneaDeIntencion SoloDigital(Guid cuenta) =>
        InstantaneaDeIntencion.Crear(cuenta, TipoDeEntrega.SinEntrega, null, null, [Digital()], Importe.Cero, Importe.De(25m), "huella-d", Texto(), Texto(), Texto(), null);

    private static IntencionDePago Abierta(Guid? pedido = null, Guid? cuenta = null, DateTimeOffset? ahora = null) =>
        IntencionDePago.Crear(pedido ?? Guid.NewGuid(), Mixta(cuenta ?? Cuenta), (ahora ?? Inicio) + PlazosDelPago.VigenciaConImpresos, ahora ?? Inicio);

    private async Task Sembrar(IntencionDePago intencion) =>
        await bd.Unidad.Ejecutar(async token => { await bd.Pagos.AgregarIntencion(intencion, token); return 0; });

    private async Task Guardar(IntencionDePago intencion) =>
        await bd.Unidad.Ejecutar(async token => { await bd.Pagos.GuardarIntencion(intencion, token); return 0; });

    private Task<IntencionDePago?> Leer(Guid id, bool paraActualizar = false) => bd.Unidad.Ejecutar(token => bd.Pagos.ObtenerIntencion(id, paraActualizar, token));

    private async Task<ResultadoDeAgregarIntento> Agregar(IntentoDeCobro intento) => await bd.Unidad.Ejecutar(token => bd.Pagos.AgregarIntento(intento, token));

    private async Task<(IntencionDePago Intencion, IntentoDeCobro Intento)> ConIntentoAprobado()
    {
        var intencion = Abierta();
        Assert.Equal(ValidacionDelCobro.Permitido, intencion.IniciarCobro(Inicio.AddSeconds(30)));
        var intento = IntentoDeCobro.Iniciar(Guid.NewGuid(), intencion.Id, MedioDePago.Tarjeta, intencion.Total, Inicio.AddSeconds(30));
        Assert.Equal(ResultadoDeAprobacion.Aprobado, intencion.AplicarAprobacion(intento, "chr_1", Inicio.AddMinutes(1)));
        await Sembrar(intencion);
        Assert.Equal(ResultadoDeAgregarIntento.Agregado, await Agregar(intento));
        return (intencion, intento);
    }

    private async Task<(IntencionDePago Intencion, IntentoDeCobro Intento)> ConIntento()
    {
        var intencion = Abierta();
        var intento = IntentoDeCobro.Iniciar(Guid.NewGuid(), intencion.Id, MedioDePago.Yape, intencion.Total, Inicio.AddSeconds(30));
        Assert.Equal(ValidacionDelCobro.Permitido, intencion.IniciarCobro(Inicio.AddSeconds(30)));
        await Sembrar(intencion);
        Assert.Equal(ResultadoDeAgregarIntento.Agregado, await Agregar(intento));
        return (intencion, intento);
    }

    // ---------- Intenciones ----------

    [Fact]
    public async Task Una_intencion_mixta_a_domicilio_se_guarda_y_se_lee_igual_con_sus_lineas_y_su_contacto()
    {
        var intencion = Abierta();
        await Sembrar(intencion);

        var leida = await Leer(intencion.Id);

        Assert.NotNull(leida);
        Assert.Equivalent(intencion, leida, strict: true);
        Assert.Equal(intencion.Lineas, leida!.Lineas);
        Assert.Equal(intencion.Contacto, leida.Contacto);
        Assert.Equal(Importe.De(140m), leida.Total);
    }

    [Fact]
    public async Task Una_intencion_solo_digital_no_guarda_entrega_ni_contacto()
    {
        var intencion = IntencionDePago.Crear(Guid.NewGuid(), SoloDigital(Cuenta), null, Inicio);
        await Sembrar(intencion);

        var leida = await Leer(intencion.Id);

        Assert.Equivalent(intencion, leida, strict: true);
        Assert.Null(leida!.Contacto);
        Assert.Equal(TipoDeEntrega.SinEntrega, leida.TipoDeEntrega);
    }

    [Fact]
    public async Task Una_intencion_aprobada_conserva_lo_que_devolvio_confirmar_las_reservas()
    {
        var (intencion, _) = await ConIntentoAprobado();
        Assert.True(intencion.MarcarConfirmacionIniciada(Inicio.AddMinutes(2)));
        var traslado = Guid.NewGuid();
        intencion.MarcarReservasConfirmadas(Inicio.AddMinutes(2), [new ReservaConfirmadaDeLinea(LineaImpresa, "AYA-02", true, traslado, new DateOnly(2026, 11, 25))]);
        await Guardar(intencion);

        var leida = await Leer(intencion.Id);

        Assert.Equivalent(intencion, leida, strict: true);
        var reserva = Assert.Single(leida!.LineasConfirmadas);
        Assert.Equal("AYA-02", reserva.CodigoDeFuente);
        Assert.True(reserva.RequiereTraslado);
        Assert.Equal(traslado, reserva.TrasladoId);
        Assert.Equal(new DateOnly(2026, 11, 25), reserva.FechaEstimadaDeLlegada);
    }

    [Fact]
    public async Task Guardar_reemplaza_las_lineas_y_las_reservas_enteras()
    {
        var (intencion, _) = await ConIntentoAprobado();
        intencion.MarcarConfirmacionIniciada(Inicio.AddMinutes(2));
        intencion.MarcarReservasConfirmadas(Inicio.AddMinutes(2), [new ReservaConfirmadaDeLinea(LineaImpresa, "AYA-02", false, null, null)]);
        await Guardar(intencion);
        await Guardar(intencion);

        Assert.Equal(2L, await bd.Consultar<long>($"SELECT count(*) FROM pagos.intencion_lineas WHERE intencion_id = '{intencion.Id}'"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM pagos.intencion_reserva_lineas WHERE intencion_id = '{intencion.Id}'"));
    }

    [Fact]
    public async Task Guardar_una_intencion_que_no_existe_falla()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Guardar(Abierta()));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-22")]
    public async Task Desvincular_la_cuenta_borra_el_identificador_el_contacto_las_ediciones_y_las_reservas_y_conserva_los_importes()
    {
        var (intencion, _) = await ConIntentoAprobado();
        intencion.MarcarConfirmacionIniciada(Inicio.AddMinutes(2));
        intencion.MarcarReservasConfirmadas(Inicio.AddMinutes(2), [new ReservaConfirmadaDeLinea(LineaImpresa, "AYA-02", false, null, null)]);
        await Guardar(intencion);
        Assert.True(intencion.Desvincular(Inicio.AddDays(40)));

        await Guardar(intencion);

        var leida = await Leer(intencion.Id);
        Assert.Null(leida!.CuentaId);
        Assert.Null(leida.Contacto);
        Assert.Null(leida.Terminos);
        Assert.All(leida.Lineas, l => Assert.Null(l.EdicionId));
        Assert.All(leida.Lineas, l => Assert.Null(l.Titulo));
        Assert.Equal(Importe.De(140m), leida.Total);
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM pagos.intencion_reserva_lineas WHERE intencion_id = '{intencion.Id}'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM pagos.intenciones WHERE id = '{intencion.Id}' AND (contacto_nombre IS NOT NULL OR contacto_telefono IS NOT NULL OR contacto_direccion IS NOT NULL OR cuenta_id IS NOT NULL)"));
        Assert.Equal("chr_1", await bd.Consultar<string>($"SELECT referencia_pasarela FROM pagos.intentos WHERE intencion_id = '{intencion.Id}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-23")]
    public async Task El_contacto_vence_a_los_treinta_dias_y_liberarlo_lo_borra_de_la_tabla()
    {
        var intencion = Abierta();
        await Sembrar(intencion);

        Assert.DoesNotContain(intencion.Id, await bd.Unidad.Ejecutar(token => bd.Pagos.ListarIntencionesConContactoVencido(Inicio.AddDays(30).AddTicks(-1), 10, token)));
        Assert.Contains(intencion.Id, await bd.Unidad.Ejecutar(token => bd.Pagos.ListarIntencionesConContactoVencido(Inicio.AddDays(30), 10, token)));

        Assert.True(intencion.LiberarContacto(Inicio.AddDays(30)));
        await Guardar(intencion);

        Assert.DoesNotContain(intencion.Id, await bd.Unidad.Ejecutar(token => bd.Pagos.ListarIntencionesConContactoVencido(Inicio.AddDays(31), 10, token)));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM pagos.intenciones WHERE id = '{intencion.Id}' AND contacto_nombre IS NOT NULL"));
        Assert.NotNull(await bd.Consultar<DateTime?>($"SELECT contacto_liberado_en FROM pagos.intenciones WHERE id = '{intencion.Id}'"));
    }

    [Fact]
    public async Task Un_check_fallido_no_deja_el_contacto_en_el_error()
    {
        var abierta = Abierta();
        var invalida = IntencionDePago.Rehidratar(
            abierta.Id, abierta.CreadaEn, abierta.CuentaId, abierta.Estado, null, true, TipoDeEntrega.Recojo, "AYA-01", null, abierta.Total, Importe.Cero, abierta.HuellaDeInstantanea, false, abierta.Lineas, abierta.Terminos, abierta.Privacidad,
            abierta.ReembolsoDigital, abierta.Contacto, null, null, abierta.IniciadaEn, abierta.VenceEn, abierta.LimitePendienteEn, abierta.ReservaExpiraEn, false, null, null, null, [], null, null);

        var falla = await Assert.ThrowsAsync<FallaDeLaBaseDePagosException>(() => Sembrar(invalida));

        Assert.Equal("23514", falla.CodigoDeEstado);
        Assert.Equal("intenciones_contacto_solo_a_domicilio_y_sin_liberar", falla.Restriccion);
        Assert.DoesNotContain("Ana", falla.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Olivos", falla.ToString(), StringComparison.Ordinal);
        Assert.Null(falla.InnerException);
    }

    [Fact]
    public async Task La_lectura_para_actualizar_espera_a_la_fila_bloqueada_y_vence_con_un_tiempo_de_espera()
    {
        var intencion = Abierta();
        await Sembrar(intencion);
        var sostiene = new TaskCompletionSource();
        var libera = new TaskCompletionSource();
        var primera = bd.Unidad.Ejecutar(
            async token =>
            {
                Assert.NotNull(await bd.Pagos.ObtenerIntencion(intencion.Id, paraActualizar: true, token));
                sostiene.SetResult();
                await libera.Task;
                return 0;
            });
        await sostiene.Task;

        await Assert.ThrowsAsync<TimeoutException>(() => Leer(intencion.Id, paraActualizar: true));

        libera.SetResult();
        await primera;
        Assert.NotNull(await Leer(intencion.Id, paraActualizar: true));
    }

    [Fact]
    public async Task Las_intenciones_de_una_cuenta_se_listan_por_identificador_y_la_cuenta_ajena_no_aparece()
    {
        var propias = new[] { Abierta(), Abierta(), Abierta() };
        foreach (var intencion in propias)
        {
            await Sembrar(intencion);
        }

        await Sembrar(Abierta(cuenta: Guid.NewGuid()));

        var lista = await bd.Unidad.Ejecutar(token => bd.Pagos.ListarIntencionesDeLaCuenta(Cuenta, paraActualizar: true, token));

        Assert.Equivalent(propias.Select(i => i.Id).ToArray(), lista.Select(i => i.Id).ToArray());
        Assert.Empty(await bd.Unidad.Ejecutar(token => bd.Pagos.ListarIntencionesDeLaCuenta(Guid.NewGuid(), paraActualizar: false, token)));
    }

    [Fact]
    public async Task Las_intenciones_abiertas_vencen_en_el_instante_exacto_y_solo_las_abiertas()
    {
        var abierta = Abierta();
        await Sembrar(abierta);
        var (conCobro, _) = await ConIntento();

        Assert.DoesNotContain(abierta.Id, await bd.Unidad.Ejecutar(token => bd.Pagos.ListarIntencionesAbiertasVencidas(abierta.VenceEn.AddTicks(-1), 10, token)));
        var vencidas = await bd.Unidad.Ejecutar(token => bd.Pagos.ListarIntencionesAbiertasVencidas(abierta.VenceEn.AddDays(1), 10, token));
        Assert.Contains(abierta.Id, vencidas);
        Assert.DoesNotContain(conCobro.Id, vencidas);
    }

    [Fact]
    public async Task Las_intenciones_aprobadas_y_en_curso_se_cuentan_por_cuenta()
    {
        var (aprobada, _) = await ConIntentoAprobado();
        var (conCobro, _) = await ConIntento();
        await Sembrar(Abierta());

        var aprobadas = await bd.Unidad.Ejecutar(token => bd.Pagos.ListarIntencionesAprobadas(10, token));
        var enCurso = await bd.Unidad.Ejecutar(token => bd.Pagos.ContarIntencionesEnCursoDeLaCuenta(Cuenta, token));

        Assert.Equal(new[] { aprobada.Id }, aprobadas);
        Assert.Equal(2, enCurso);
        Assert.Equal(0, await bd.Unidad.Ejecutar(token => bd.Pagos.ContarIntencionesEnCursoDeLaCuenta(Guid.NewGuid(), token)));
        Assert.NotEqual(aprobada.Id, conCobro.Id);
    }

    [Fact]
    public async Task Las_intenciones_cerradas_se_listan_para_depurar_antes_del_corte_y_solo_si_siguen_vinculadas()
    {
        var cerrada = Abierta();
        Assert.Equal(ResultadoDeCancelacion.Cancelada, cerrada.Cancelar(Inicio.AddMinutes(1)));
        await Sembrar(cerrada);
        var abierta = Abierta();
        await Sembrar(abierta);

        Assert.Empty(await bd.Unidad.Ejecutar(token => bd.Pagos.ListarIntencionesCerradasParaDepurar(Inicio.AddMinutes(1), 10, token)));
        var lista = await bd.Unidad.Ejecutar(token => bd.Pagos.ListarIntencionesCerradasParaDepurar(Inicio.AddMinutes(2), 10, token));
        Assert.Equal(cerrada.Id, Assert.Single(lista).Id);

        Assert.True(lista[0].Desvincular(Inicio.AddDays(100)));
        await Guardar(lista[0]);
        Assert.Empty(await bd.Unidad.Ejecutar(token => bd.Pagos.ListarIntencionesCerradasParaDepurar(Inicio.AddMinutes(2), 10, token)));
    }

    // ---------- Intentos ----------

    [Fact]
    public async Task Un_intento_se_guarda_se_actualiza_y_se_lee_igual()
    {
        var (_, intento) = await ConIntento();
        Assert.True(intento.MarcarPendiente(Inicio.AddSeconds(40)));
        intento.RegistrarConsulta(Inicio.AddSeconds(50));
        Assert.True(intento.MarcarRechazado(MotivoDeRechazo.FondosInsuficientes, Inicio.AddSeconds(55)));

        await bd.Unidad.Ejecutar(async token => { await bd.Pagos.GuardarIntento(intento, token); return 0; });

        var leido = await bd.Unidad.Ejecutar(token => bd.Pagos.ObtenerIntento(intento.Id, token));
        Assert.Equivalent(intento, leido, strict: true);
        Assert.Equal(MotivoDeRechazo.FondosInsuficientes, leido!.MotivoDeRechazo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Un_segundo_intento_activo_de_la_misma_intencion_no_se_agrega_y_la_transaccion_sigue_viva()
    {
        var (intencion, primero) = await ConIntento();
        var repetido = IntentoDeCobro.Iniciar(primero.Id, intencion.Id, MedioDePago.Yape, intencion.Total, Inicio.AddSeconds(31));
        var otro = IntentoDeCobro.Iniciar(Guid.NewGuid(), intencion.Id, MedioDePago.Tarjeta, intencion.Total, Inicio.AddSeconds(32));

        var (clave, activo, existente) = await bd.Unidad.Ejecutar(
            async token =>
            {
                var porClave = await bd.Pagos.AgregarIntento(repetido, token);
                var porActivo = await bd.Pagos.AgregarIntento(otro, token);
                // Si la violación hubiera abortado la transacción, esta lectura fallaría con 25P02.
                return (porClave, porActivo, await bd.Pagos.ObtenerIntentoActivo(intencion.Id, token));
            });

        Assert.Equal(ResultadoDeAgregarIntento.ClaveRepetida, clave);
        Assert.Equal(ResultadoDeAgregarIntento.IntentoActivoExistente, activo);
        Assert.Equal(primero.Id, existente!.Id);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Tras_un_rechazo_la_intencion_acepta_otro_intento_con_otro_medio()
    {
        var (intencion, primero) = await ConIntento();
        Assert.True(primero.MarcarRechazado(MotivoDeRechazo.TarjetaRechazada, Inicio.AddMinutes(1)));
        await bd.Unidad.Ejecutar(async token => { await bd.Pagos.GuardarIntento(primero, token); return 0; });

        var otro = IntentoDeCobro.Iniciar(Guid.NewGuid(), intencion.Id, MedioDePago.Tarjeta, intencion.Total, Inicio.AddMinutes(2));

        Assert.Equal(ResultadoDeAgregarIntento.Agregado, await Agregar(otro));
        Assert.Equal(otro.Id, (await bd.Unidad.Ejecutar(token => bd.Pagos.ObtenerIntentoActivo(intencion.Id, token)))!.Id);
    }

    [Fact]
    public async Task El_ultimo_intento_es_el_de_inicio_mas_reciente_activo_o_no()
    {
        var (intencion, primero) = await ConIntento();
        Assert.True(primero.MarcarRechazado(MotivoDeRechazo.TarjetaVencida, Inicio.AddMinutes(1)));
        await bd.Unidad.Ejecutar(async token => { await bd.Pagos.GuardarIntento(primero, token); return 0; });
        var segundo = IntentoDeCobro.Iniciar(Guid.NewGuid(), intencion.Id, MedioDePago.Tarjeta, intencion.Total, Inicio.AddMinutes(2));
        await Agregar(segundo);

        Assert.Equal(segundo.Id, (await bd.Unidad.Ejecutar(token => bd.Pagos.ObtenerUltimoIntento(intencion.Id, token)))!.Id);
        Assert.Null(await bd.Unidad.Ejecutar(token => bd.Pagos.ObtenerUltimoIntento(Guid.NewGuid(), token)));
    }

    [Fact]
    public async Task Los_intentos_por_consultar_son_los_sin_resolver_y_los_resueltos_hace_menos_de_veinticuatro_horas()
    {
        var (intencion, sinResolver) = await ConIntento();
        var rechazadoReciente = IntentoDeCobro.Iniciar(Guid.NewGuid(), intencion.Id, MedioDePago.Tarjeta, intencion.Total, Inicio);
        rechazadoReciente.MarcarRechazado(MotivoDeRechazo.Otro, Inicio.AddHours(1));
        var vencidoAntiguo = IntentoDeCobro.Iniciar(Guid.NewGuid(), intencion.Id, MedioDePago.Tarjeta, intencion.Total, Inicio);
        vencidoAntiguo.Vencer(Inicio.AddHours(1).AddTicks(-1));
        await Agregar(rechazadoReciente);
        await Agregar(vencidoAntiguo);
        var ahora = Inicio.AddHours(1) + IntentoDeCobro.VentanaDeConciliacion;

        var ids = await bd.Unidad.Ejecutar(token => bd.Pagos.ListarIntentosParaConsultar(ahora, 10, token));

        Assert.Contains(sinResolver.Id, ids);
        Assert.Contains(rechazadoReciente.Id, ids);
        Assert.DoesNotContain(vencidoAntiguo.Id, ids);
    }

    // ---------- Compensaciones ----------

    [Fact]
    public async Task Una_compensacion_no_se_repite_se_guarda_paso_a_paso_y_deja_de_listarse_al_terminar()
    {
        var (intencion, intento) = await ConIntentoAprobado();
        var compensacion = Compensacion.Iniciar(intencion.Id, intento.Id, MotivoDeCompensacion.FallaPosteriorAlPago, VarianteDeCompensacion.Completa, tieneReservas: true, Inicio.AddMinutes(3));

        Assert.True(await bd.Unidad.Ejecutar(token => bd.Pagos.AgregarCompensacion(compensacion, token)));
        Assert.False(await bd.Unidad.Ejecutar(token => bd.Pagos.AgregarCompensacion(Compensacion.Iniciar(intencion.Id, intento.Id, MotivoDeCompensacion.PagoTardio, VarianteDeCompensacion.SoloReembolso, false, Inicio), token)));
        Assert.Equal(intento.Id, Assert.Single(await bd.Unidad.Ejecutar(token => bd.Pagos.ListarCompensacionesSinTerminar(10, token))).IntentoId);

        Assert.True(compensacion.MarcarReservasLiberadas(MotivoDeLiberacion.FallaPosteriorAlPago, Inicio.AddMinutes(4)));
        var reembolsoId = Guid.NewGuid();
        Assert.True(compensacion.MarcarReembolsoRegistrado(reembolsoId, Inicio.AddMinutes(5)));
        await bd.Unidad.Ejecutar(async token => { await bd.Pagos.GuardarCompensacion(compensacion, token); return 0; });

        var leida = await bd.Unidad.Ejecutar(token => bd.Pagos.ObtenerCompensacion(intento.Id, token));
        Assert.Equivalent(compensacion, leida, strict: true);
        Assert.Equal(PasoDeCompensacion.CerrarCheckout, leida!.SiguientePaso);
        Assert.True(leida.Avisar);

        Assert.True(compensacion.MarcarCheckoutCerrado(Inicio.AddMinutes(6)));
        await bd.Unidad.Ejecutar(async token => { await bd.Pagos.GuardarCompensacion(compensacion, token); return 0; });
        Assert.Empty(await bd.Unidad.Ejecutar(token => bd.Pagos.ListarCompensacionesSinTerminar(10, token)));
        Assert.True((await bd.Unidad.Ejecutar(token => bd.Pagos.ObtenerCompensacion(intento.Id, token)))!.EstaTerminada);
    }

    [Fact]
    public async Task Una_compensacion_por_cancelacion_de_cuenta_no_avisa_tras_guardarla_y_leerla()
    {
        var (intencion, intento) = await ConIntentoAprobado();
        var compensacion = Compensacion.Iniciar(intencion.Id, intento.Id, MotivoDeCompensacion.CancelacionDeCuentaEnCurso, VarianteDeCompensacion.SoloReembolso, false, Inicio.AddMinutes(3));
        Assert.True(await bd.Unidad.Ejecutar(token => bd.Pagos.AgregarCompensacion(compensacion, token)));

        var leida = await bd.Unidad.Ejecutar(token => bd.Pagos.ObtenerCompensacion(intento.Id, token));

        Assert.False(leida!.Avisar);
        Assert.Equivalent(compensacion, leida, strict: true);
    }

    // ---------- Avisos ----------

    [Fact]
    public async Task Un_aviso_a_carrito_no_se_repite_por_clave_se_libera_y_se_reintenta_con_espera()
    {
        var (intencion, intento) = await ConIntento();
        var retenido = AvisoACarrito.Crear(intencion.Id, intento.Id, TransicionDeAviso.Pendiente, retenido: true, Inicio);

        Assert.True(await bd.Unidad.Ejecutar(token => bd.Pagos.AgregarAvisoACarrito(retenido, token)));
        Assert.False(await bd.Unidad.Ejecutar(token => bd.Pagos.AgregarAvisoACarrito(AvisoACarrito.Crear(intencion.Id, intento.Id, TransicionDeAviso.Pendiente, false, Inicio), token)));
        Assert.Empty(await bd.Unidad.Ejecutar(token => bd.Pagos.ListarIntencionesConAvisosACarritoListos(Inicio.AddHours(1), 10, token)));

        Assert.True(retenido.Liberar());
        await bd.Unidad.Ejecutar(async token => { await bd.Pagos.GuardarAvisoACarrito(retenido, token); return 0; });
        Assert.Equal(new[] { intencion.Id }, await bd.Unidad.Ejecutar(token => bd.Pagos.ListarIntencionesConAvisosACarritoListos(Inicio, 10, token)));

        retenido.RegistrarFalla(Inicio);
        await bd.Unidad.Ejecutar(async token => { await bd.Pagos.GuardarAvisoACarrito(retenido, token); return 0; });
        Assert.Empty(await bd.Unidad.Ejecutar(token => bd.Pagos.ListarIntencionesConAvisosACarritoListos(Inicio.AddSeconds(14), 10, token)));
        Assert.Equal(new[] { intencion.Id }, await bd.Unidad.Ejecutar(token => bd.Pagos.ListarIntencionesConAvisosACarritoListos(Inicio.AddSeconds(15), 10, token)));

        var leido = Assert.Single(await bd.Unidad.Ejecutar(token => bd.Pagos.ListarAvisosACarritoDeLaIntencion(intencion.Id, token)));
        Assert.Equivalent(retenido, leido, strict: true);

        Assert.True(retenido.MarcarEnviado(Inicio.AddMinutes(1)));
        await bd.Unidad.Ejecutar(async token => { await bd.Pagos.GuardarAvisoACarrito(retenido, token); return 0; });
        Assert.Empty(await bd.Unidad.Ejecutar(token => bd.Pagos.ListarIntencionesConAvisosACarritoListos(Inicio.AddDays(1), 10, token)));
    }

    [Fact]
    public async Task Un_aviso_de_pago_no_se_repite_y_tras_diez_fallas_se_omite_y_deja_de_listarse()
    {
        var (intencion, intento) = await ConIntento();
        var aviso = AvisoDePago.Crear(intencion.Id, intento.Id, EventoDeAvisoDePago.Aprobado, Inicio);

        Assert.True(await bd.Unidad.Ejecutar(token => bd.Pagos.AgregarAvisoDePago(aviso, token)));
        Assert.False(await bd.Unidad.Ejecutar(token => bd.Pagos.AgregarAvisoDePago(AvisoDePago.Crear(intencion.Id, intento.Id, EventoDeAvisoDePago.Aprobado, Inicio), token)));
        Assert.True(await bd.Unidad.Ejecutar(token => bd.Pagos.AgregarAvisoDePago(AvisoDePago.Crear(intencion.Id, intento.Id, EventoDeAvisoDePago.Vencido, Inicio), token)));
        Assert.Equal(2, (await bd.Unidad.Ejecutar(token => bd.Pagos.ListarAvisosDePagoListos(Inicio, 10, token))).Count);

        for (var i = 0; i < AvisoDePago.MaximoDeIntentos; i++)
        {
            aviso.RegistrarFalla(Inicio);
        }

        await bd.Unidad.Ejecutar(async token => { await bd.Pagos.GuardarAvisoDePago(aviso, token); return 0; });

        var leido = (await bd.Unidad.Ejecutar(token => bd.Pagos.ListarAvisosDePagoDeLaIntencion(intencion.Id, token))).Single(a => a.Evento == EventoDeAvisoDePago.Aprobado);
        Assert.Equivalent(aviso, leido, strict: true);
        Assert.Equal(EstadoDeAvisoDePago.Omitido, leido.Estado);
        Assert.Equal(EventoDeAvisoDePago.Vencido, Assert.Single(await bd.Unidad.Ejecutar(token => bd.Pagos.ListarAvisosDePagoListos(Inicio.AddDays(1), 10, token))).Evento);
    }

    [Fact]
    public async Task La_purga_borra_los_avisos_creados_antes_del_corte_en_las_dos_tablas_y_respeta_la_tanda()
    {
        var (intencion, intento) = await ConIntento();
        var corte = Inicio.AddDays(90);
        foreach (var evento in new[] { EventoDeAvisoDePago.Pendiente, EventoDeAvisoDePago.Aprobado, EventoDeAvisoDePago.Vencido })
        {
            await bd.Unidad.Ejecutar(token => bd.Pagos.AgregarAvisoDePago(AvisoDePago.Crear(intencion.Id, intento.Id, evento, corte.AddDays(-1)), token));
        }

        await bd.Unidad.Ejecutar(token => bd.Pagos.AgregarAvisoDePago(AvisoDePago.Crear(intencion.Id, intento.Id, EventoDeAvisoDePago.Rechazado, corte), token));
        await bd.Unidad.Ejecutar(token => bd.Pagos.AgregarAvisoACarrito(AvisoACarrito.Crear(intencion.Id, intento.Id, TransicionDeAviso.Aprobado, false, corte.AddDays(-1)), token));

        var primera = await bd.Unidad.Ejecutar(token => bd.Pagos.PurgarAvisosAnterioresA(corte, 2, token));
        var segunda = await bd.Unidad.Ejecutar(token => bd.Pagos.PurgarAvisosAnterioresA(corte, 2, token));
        var tercera = await bd.Unidad.Ejecutar(token => bd.Pagos.PurgarAvisosAnterioresA(corte, 2, token));

        Assert.Equal(3, primera); // dos de pago por la tanda y uno a Carrito
        Assert.Equal(1, segunda);
        Assert.Equal(0, tercera);
        Assert.Equal(EventoDeAvisoDePago.Rechazado, Assert.Single(await bd.Unidad.Ejecutar(token => bd.Pagos.ListarAvisosDePagoDeLaIntencion(intencion.Id, token))).Evento);
    }

    // ---------- Reembolsos ----------

    private static Reembolso DeLineas(IntencionDePago intencion, IntentoDeCobro intento, OrigenDeReembolso origen, string clave, DateTimeOffset? ahora = null) =>
        Reembolso.Crear(Guid.NewGuid(), origen, clave, intencion.Id, intento.Id, [new LineaDeReembolso(LineaImpresa, Importe.De(50m)), new LineaDeReembolso(LineaDigital, Importe.De(25m))], Importe.De(15m), ahora ?? Inicio.AddMinutes(10));

    private Task Agregar(Reembolso reembolso) => bd.Unidad.Ejecutar(async token => { await bd.Reembolsos.Agregar(reembolso, token); return 0; });

    private Task Guardar(Reembolso reembolso) => bd.Unidad.Ejecutar(async token => { await bd.Reembolsos.Guardar(reembolso, token); return 0; });

    [Fact]
    public async Task Un_reembolso_se_guarda_con_sus_lineas_se_busca_por_origen_y_se_lee_igual()
    {
        var (intencion, intento) = await ConIntentoAprobado();
        var reembolso = DeLineas(intencion, intento, OrigenDeReembolso.Devolucion, ClaveDeReembolso.Devolucion(Guid.NewGuid(), LineaImpresa));
        await Agregar(reembolso);

        var porId = await bd.Unidad.Ejecutar(token => bd.Reembolsos.Obtener(reembolso.Id, paraActualizar: true, token));
        var porOrigen = await bd.Unidad.Ejecutar(token => bd.Reembolsos.BuscarPorOrigen(OrigenDeReembolso.Devolucion, reembolso.ClaveNatural, token));

        Assert.Equal(Importe.De(90m), reembolso.Monto);
        Assert.Equivalent(reembolso, porId, strict: true);
        Assert.Equal(reembolso.Id, porOrigen!.Id);
        Assert.Equal(reembolso.Lineas.OrderBy(l => l.LineaId), porId!.Lineas);
        Assert.Null(await bd.Unidad.Ejecutar(token => bd.Reembolsos.BuscarPorOrigen(OrigenDeReembolso.Anulacion, reembolso.ClaveNatural, token)));
    }

    [Fact]
    public async Task La_clave_natural_de_un_origen_no_se_repite_y_la_falla_no_trae_datos_de_la_fila()
    {
        var (intencion, intento) = await ConIntentoAprobado();
        var clave = ClaveDeReembolso.Devolucion(Guid.NewGuid(), LineaImpresa);
        await Agregar(DeLineas(intencion, intento, OrigenDeReembolso.Devolucion, clave));

        var falla = await Assert.ThrowsAsync<FallaDeLaBaseDePagosException>(() => Agregar(DeLineas(intencion, intento, OrigenDeReembolso.Devolucion, clave)));

        Assert.True(AccesoAlEsquemaDePagos.EsUnicidad(falla, "reembolsos_clave_natural"));
        Assert.DoesNotContain(clave, falla.ToString(), StringComparison.Ordinal);
        await Agregar(DeLineas(intencion, intento, OrigenDeReembolso.Anulacion, ClaveDeReembolso.Anulacion(intencion.Id)));
    }

    [Fact]
    public async Task Un_reembolso_solicitado_y_resuelto_se_guarda_con_su_referencia_y_sus_intentos()
    {
        var (intencion, intento) = await ConIntentoAprobado();
        var reembolso = DeLineas(intencion, intento, OrigenDeReembolso.CancelacionDelCliente, ClaveDeReembolso.Cancelacion(intencion.Id));
        await Agregar(reembolso);
        Assert.True(reembolso.Reclamar(Inicio.AddMinutes(11)));
        await Guardar(reembolso);
        Assert.True(reembolso.MarcarReembolsado("rfd_1", Inicio.AddMinutes(12)));
        await Guardar(reembolso);

        var leido = await bd.Unidad.Ejecutar(token => bd.Reembolsos.Obtener(reembolso.Id, false, token));

        Assert.Equivalent(reembolso, leido, strict: true);
        Assert.Equal((EstadoDeReembolso.Reembolsado, "rfd_1", 1), (leido!.Estado, leido.ReferenciaDePasarela, leido.Intentos));
        Assert.Equal(0, await bd.Unidad.Ejecutar(token => bd.Reembolsos.ContarPendientesDeLaCuenta(Cuenta, OrigenDeReembolso.CancelacionDelCliente, false, token)));
    }

    [Fact]
    public async Task Los_reembolsos_pendientes_de_una_cuenta_se_cuentan_por_origen_y_se_excluyen_los_de_la_cancelacion_de_la_cuenta()
    {
        var (intencion, intento) = await ConIntentoAprobado();
        var compensacion = Compensacion.Iniciar(intencion.Id, intento.Id, MotivoDeCompensacion.CancelacionDeCuentaEnCurso, VarianteDeCompensacion.SoloReembolso, false, Inicio.AddMinutes(3));
        Assert.True(await bd.Unidad.Ejecutar(token => bd.Pagos.AgregarCompensacion(compensacion, token)));
        await Agregar(Reembolso.CrearDeCargo(Guid.NewGuid(), OrigenDeReembolso.Compensacion, ClaveDeReembolso.Compensacion(intento.Id), intencion.Id, intento.Id, intencion.Total, Inicio.AddMinutes(4)));

        Assert.Equal(1, await bd.Unidad.Ejecutar(token => bd.Reembolsos.ContarPendientesDeLaCuenta(Cuenta, OrigenDeReembolso.Compensacion, false, token)));
        Assert.Equal(0, await bd.Unidad.Ejecutar(token => bd.Reembolsos.ContarPendientesDeLaCuenta(Cuenta, OrigenDeReembolso.Compensacion, true, token)));
        Assert.Equal(0, await bd.Unidad.Ejecutar(token => bd.Reembolsos.ContarPendientesDeLaCuenta(Cuenta, OrigenDeReembolso.Devolucion, false, token)));
        Assert.Equal(0, await bd.Unidad.Ejecutar(token => bd.Reembolsos.ContarPendientesDeLaCuenta(Guid.NewGuid(), OrigenDeReembolso.Compensacion, false, token)));
    }

    [Fact]
    public async Task Los_reembolsos_pendientes_se_listan_por_paginas_con_su_total()
    {
        var (intencion, intento) = await ConIntentoAprobado();
        for (var i = 0; i < 3; i++)
        {
            await Agregar(DeLineas(intencion, intento, OrigenDeReembolso.Devolucion, ClaveDeReembolso.Devolucion(Guid.NewGuid(), LineaImpresa), Inicio.AddMinutes(10 + i)));
        }

        var (primera, total) = await bd.Unidad.Ejecutar(token => bd.Reembolsos.ListarPendientes(1, 2, token));
        var (segunda, _) = await bd.Unidad.Ejecutar(token => bd.Reembolsos.ListarPendientes(2, 2, token));

        Assert.Equal(3, total);
        Assert.Equal((2, 1), (primera.Count, segunda.Count));
        Assert.Empty(primera.Select(r => r.Id).Intersect(segunda.Select(r => r.Id)));
    }

    [Fact]
    public async Task Un_reembolso_solicitado_es_huerfano_a_los_dos_minutos_exactos()
    {
        var (intencion, intento) = await ConIntentoAprobado();
        var reembolso = DeLineas(intencion, intento, OrigenDeReembolso.Anulacion, ClaveDeReembolso.Anulacion(intencion.Id));
        await Agregar(reembolso);
        Assert.True(reembolso.Reclamar(Inicio.AddMinutes(11)));
        await Guardar(reembolso);

        Assert.Empty(await bd.Unidad.Ejecutar(token => bd.Reembolsos.ListarHuerfanos(Inicio.AddMinutes(11).Add(Reembolso.PlazoDeHuerfano).AddTicks(-1), 10, token)));
        Assert.Equal(new[] { reembolso.Id }, await bd.Unidad.Ejecutar(token => bd.Reembolsos.ListarHuerfanos(Inicio.AddMinutes(11).Add(Reembolso.PlazoDeHuerfano), 10, token)));
    }

    [Fact]
    public async Task Los_reintentos_automaticos_respetan_la_espera_creciente_el_motivo_y_el_tope()
    {
        var (intencion, intento) = await ConIntentoAprobado();
        var sinRespuesta = DeLineas(intencion, intento, OrigenDeReembolso.Devolucion, ClaveDeReembolso.Devolucion(Guid.NewGuid(), LineaImpresa));
        var rechazado = DeLineas(intencion, intento, OrigenDeReembolso.Devolucion, ClaveDeReembolso.Devolucion(Guid.NewGuid(), LineaImpresa));
        var nuevo = DeLineas(intencion, intento, OrigenDeReembolso.Devolucion, ClaveDeReembolso.Devolucion(Guid.NewGuid(), LineaImpresa));
        var agotado = DeLineas(intencion, intento, OrigenDeReembolso.Devolucion, ClaveDeReembolso.Devolucion(Guid.NewGuid(), LineaImpresa));
        foreach (var reembolso in new[] { sinRespuesta, rechazado, nuevo, agotado })
        {
            await Agregar(reembolso);
        }

        var t0 = Inicio.AddMinutes(20);
        Assert.True(sinRespuesta.Reclamar(t0));
        Assert.True(sinRespuesta.DejarPendiente(MotivoPendienteDeReembolso.SinRespuesta, t0));
        Assert.True(rechazado.Reclamar(t0));
        Assert.True(rechazado.DejarPendiente(MotivoPendienteDeReembolso.RechazadoPorPasarela, t0));
        for (var i = 0; i < Reembolso.MaximoDeReintentosAutomaticos; i++)
        {
            Assert.True(agotado.Reclamar(t0.AddDays(i)));
            Assert.True(agotado.DejarPendiente(MotivoPendienteDeReembolso.SinRespuesta, t0.AddDays(i)));
        }

        foreach (var reembolso in new[] { sinRespuesta, rechazado, agotado })
        {
            await Guardar(reembolso);
        }

        var pronto = await bd.Unidad.Ejecutar(token => bd.Reembolsos.ListarReintentables(t0.AddSeconds(20), 10, token));
        var despues = await bd.Unidad.Ejecutar(token => bd.Reembolsos.ListarReintentables(t0.AddMinutes(2), 10, token));
        var muyDespues = await bd.Unidad.Ejecutar(token => bd.Reembolsos.ListarReintentables(t0.AddDays(30), 10, token));

        Assert.Equal(new[] { nuevo.Id }, pronto);
        Assert.Equivalent(new[] { nuevo.Id, sinRespuesta.Id }, despues);
        Assert.DoesNotContain(rechazado.Id, muyDespues);
        Assert.DoesNotContain(agotado.Id, muyDespues);
        Assert.Contains(sinRespuesta.Id, muyDespues);
    }

    // ---------- Permisos ----------

    [Fact]
    public async Task El_rol_de_pagos_no_puede_cambiar_el_importe_de_un_intento_ni_borrar_intentos_ni_vaciar_tablas()
    {
        var (_, intento) = await ConIntento();

        await using var conexion = await bd.Acceso.OpenConnectionAsync();
        foreach (var sql in new[]
        {
            $"UPDATE pagos.intentos SET importe = 1 WHERE id = '{intento.Id}'",
            $"DELETE FROM pagos.intentos WHERE id = '{intento.Id}'",
            "TRUNCATE pagos.intentos CASCADE",
            $"UPDATE pagos.intenciones SET creada_en = now() WHERE id = '{intento.IntencionId}'",
            "UPDATE pagos.reembolsos SET monto = 1",
        })
        {
            await using var comando = new NpgsqlCommand($"SET ROLE rol_pagos; {sql}", conexion);
            var falla = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
            Assert.Equal("42501", falla.SqlState);
            await using var restablecer = new NpgsqlCommand("RESET ROLE", conexion);
            await restablecer.ExecuteNonQueryAsync();
        }
    }
}
