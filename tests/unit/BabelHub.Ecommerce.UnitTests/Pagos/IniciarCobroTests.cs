using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.UnitTests.Soporte;
using static BabelHub.Ecommerce.UnitTests.Pagos.EntornoDePagosExtensiones;
using static BabelHub.Ecommerce.UnitTests.Pagos.InstantaneasDeCompraDePrueba;
using AppMedio = BabelHub.Ecommerce.Application.Pagos.MedioDePago;
using DomMedio = BabelHub.Ecommerce.Domain.Pagos.MedioDePago;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>El orquestador del cobro: paso 0, registrar, la pasarela fuera de la transacción y registrar el resultado (T13; PAG-01, PAG-02, PAG-05 a PAG-07, PAG-09, PAG-11, PAG-30).</summary>
public sealed class IniciarCobroTests
{
    private static async Task<CobrarEntrada> Pedir(EntornoDePagos e, AppMedio medio = AppMedio.Yape, string? medioDePago = null)
    {
        var clave = (await e.EmitirClave.Ejecutar(new EmitirClaveDeIntentoEntrada(Pedido))).Valor;
        return new CobrarEntrada(Pedido, clave.Token, medio, TokenDelMedioDePago.Crear(medioDePago ?? "tkn_test_abc123XYZ"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-01")]
    public async Task Un_cobro_aprobado_manda_a_la_pasarela_solo_el_importe_la_referencia_opaca_y_el_token_y_entrega_la_compra_a_la_confirmacion()
    {
        var e = await ConIntencionLista();
        var entrada = await Pedir(e, AppMedio.Tarjeta);

        var resultado = await e.Cobrar.Ejecutar(entrada);

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDelCobro.Aprobado, resultado.Valor.Estado);
        var solicitud = Assert.Single(e.Pasarela.Solicitudes);
        Assert.Equal(Importe.De(140m), solicitud.Importe);
        Assert.Equal(DomMedio.Tarjeta, solicitud.Medio);
        Assert.Equal(resultado.Valor.IntentoId, solicitud.ReferenciaOpaca);
        Assert.Equal("tkn_test_abc123XYZ", solicitud.Token.Revelar());
        Assert.Null(solicitud.CorreoDelTitular);
        var llamada = Assert.Single(e.Confirmador.Llamadas);
        Assert.Equal((Pedido, resultado.Valor.IntentoId, "ch_1"), llamada);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-01")]
    public async Task Ante_un_aprobado_el_registro_del_resultado_no_cambia_el_intento_ni_la_intencion()
    {
        var e = await ConIntencionLista();

        await e.Cobrar.Ejecutar(await Pedir(e));

        var intento = Assert.Single(e.Repositorio.Intentos);
        Assert.Equal(EstadoDeIntento.Iniciado, intento.Estado);
        Assert.Equal(EstadoDeIntencion.ConCobro, Assert.Single(e.Repositorio.Intenciones).Estado);
        Assert.Single(e.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-03")]
    public async Task La_pasarela_el_inventario_y_la_confirmacion_se_llaman_siempre_fuera_de_una_transaccion()
    {
        var e = await ConIntencionLista();

        await e.Cobrar.Ejecutar(await Pedir(e));

        Assert.Equal(0, e.Pasarela.LlamadasDentroDeUnaTransaccion);
        Assert.Equal(0, e.Confirmador.LlamadasDentroDeUnaTransaccion);
        Assert.Equal(0, e.Reservas.LlamadasDentroDeUnaTransaccion);
        Assert.Equal(0, e.Cuenta.ConsultasDentroDeUnaTransaccion);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-05")]
    public async Task Un_rechazo_informa_el_motivo_cierra_el_intento_deja_la_intencion_abierta_y_permite_reintentar_con_otro_medio()
    {
        var e = await ConIntencionLista();
        e.Pasarela.Respuesta = _ => ResultadoDeCobro.Rechazado(MotivoDeRechazo.FondosInsuficientes);

        var resultado = await e.Cobrar.Ejecutar(await Pedir(e, AppMedio.Tarjeta));

        Assert.Equal(EstadoDelCobro.Rechazado, resultado.Valor.Estado);
        Assert.Equal(MotivoDeRechazoDePago.FondosInsuficientes, resultado.Valor.Motivo);
        Assert.Empty(e.Confirmador.Llamadas);
        var intento = Assert.Single(e.Repositorio.Intentos);
        Assert.Equal(EstadoDeIntento.Rechazado, intento.Estado);
        Assert.Equal(MotivoDeRechazo.FondosInsuficientes, intento.MotivoDeRechazo);
        Assert.Equal(EstadoDeIntencion.Abierta, Assert.Single(e.Repositorio.Intenciones).Estado);
        Assert.Equal("fondos_insuficientes", e.Auditoria.Solicitudes.Last().Detalle["motivo"]);

        // Con otro medio: un intento nuevo con clave nueva de la misma intención.
        e.Pasarela.Respuesta = null;
        var segundo = await e.Cobrar.Ejecutar(await Pedir(e, AppMedio.Yape));
        Assert.Equal(EstadoDelCobro.Aprobado, segundo.Valor.Estado);
        Assert.Equal(2, e.Repositorio.Intentos.Count);
        Assert.NotEqual(resultado.Valor.IntentoId, segundo.Valor.IntentoId);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-09")]
    public async Task El_registro_de_un_rechazo_audita_el_resultado_sin_token_ni_datos_de_tarjeta()
    {
        var e = await ConIntencionLista();
        e.Pasarela.Respuesta = _ => ResultadoDeCobro.Rechazado(MotivoDeRechazo.TarjetaRechazada);

        await e.Cobrar.Ejecutar(await Pedir(e, AppMedio.Tarjeta));

        var resuelto = e.Auditoria.Solicitudes.Single(s => s.Accion.Identificador == "CobroResuelto");
        Assert.Equal("rechazado", resuelto.Detalle["resultado"]);
        Assert.DoesNotContain("abc123XYZ", string.Join(' ', resuelto.Detalle.Values));
        Assert.All(e.Auditoria.Solicitudes, s => Assert.DoesNotContain("abc123XYZ", string.Join(' ', s.Detalle.Values)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task Sin_respuesta_a_tiempo_el_pago_queda_pendiente_la_vigencia_sube_a_30_minutos_y_se_extiende_la_reserva_una_vez()
    {
        var e = await ConIntencionLista();
        e.Pasarela.NoResponde = true;

        var resultado = await e.Cobrar.Ejecutar(await Pedir(e));

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, resultado.Valor.Estado);
        var intento = Assert.Single(e.Repositorio.Intentos);
        Assert.Equal(EstadoDeIntento.PendienteDeConfirmacion, intento.Estado);
        var intencion = Assert.Single(e.Repositorio.Intenciones);
        Assert.Equal(EstadoDeIntencion.ConCobro, intencion.Estado);
        Assert.Equal(IntencionesDePrueba.Inicio.AddMinutes(30), intencion.VenceEn);
        Assert.Equal(IntencionesDePrueba.Inicio.AddMinutes(30), Assert.Single(e.Reservas.Extensiones));
        Assert.True(intencion.ReservaExtendida);
        Assert.Equal(IntencionesDePrueba.Inicio.AddMinutes(30), intencion.ReservaExpiraEn);
        Assert.Equal(0, e.Reservas.LlamadasDentroDeUnaTransaccion);
        Assert.Empty(e.Confirmador.Llamadas);
        Assert.Equal("pendiente", e.Auditoria.Solicitudes.Last().Detalle["resultado"]);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task Una_falla_de_la_pasarela_o_una_respuesta_dudosa_dejan_el_pago_pendiente_y_no_cobran_otra_vez()
    {
        var cae = await ConIntencionLista();
        cae.Pasarela.Lanza = true;
        var dudoso = await ConIntencionLista();
        dudoso.Pasarela.Respuesta = s => ResultadoDeCobro.Aprobado("ch_1", Importe.De(139.99m), "PEN", s.ReferenciaOpaca);

        var a = await cae.Cobrar.Ejecutar(await Pedir(cae));
        var b = await dudoso.Cobrar.Ejecutar(await Pedir(dudoso));

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, a.Valor.Estado);
        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, b.Valor.Estado);
        Assert.Empty(dudoso.Confirmador.Llamadas);
        Assert.Single(cae.Pasarela.Solicitudes);
        Assert.Single(dudoso.Pasarela.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Un_aprobado_dudoso_publica_una_alerta_tecnica_para_conciliar_y_nunca_un_reembolso()
    {
        var e = await ConIntencionLista();
        e.Pasarela.Respuesta = s => ResultadoDeCobro.Aprobado("ch_1", s.Importe, "USD", s.ReferenciaOpaca);

        await e.Cobrar.Ejecutar(await Pedir(e));

        Assert.Equal(IniciarCobro.MotivoDeAlertaCobroDudoso, Assert.Single(e.Alertas.Publicadas).Motivo);
        Assert.Equal(EstadoDeIntento.PendienteDeConfirmacion, Assert.Single(e.Repositorio.Intentos).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task Si_falla_la_auditoria_al_registrar_un_rechazo_el_cliente_ve_pendiente_y_el_intento_sigue_iniciado_para_la_reconciliacion()
    {
        var e = await ConIntencionLista();
        var entrada = await Pedir(e);
        e.Pasarela.Respuesta = _ =>
        {
            e.Auditoria.Caida = true;
            return ResultadoDeCobro.Rechazado(MotivoDeRechazo.TarjetaRechazada);
        };

        var resultado = await e.Cobrar.Ejecutar(entrada);

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, resultado.Valor.Estado);
        Assert.Equal(EstadoDeIntento.Iniciado, Assert.Single(e.Repositorio.Intentos).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task Si_falla_la_auditoria_al_cerrar_un_pendiente_el_intento_sigue_iniciado_y_se_publica_una_alerta()
    {
        var e = await ConIntencionLista();
        e.Pasarela.Respuesta = s =>
        {
            e.Auditoria.Caida = true;
            return ResultadoDeCobro.SinRespuesta();
        };

        var resultado = await e.Cobrar.Ejecutar(await Pedir(e));

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, resultado.Valor.Estado);
        Assert.Equal(EstadoDeIntento.Iniciado, Assert.Single(e.Repositorio.Intentos).Estado);
        Assert.Contains(e.Alertas.Publicadas, a => a.Motivo == IniciarCobro.MotivoDeAlertaAuditoriaNoDisponible);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-03")]
    public async Task Repetir_la_clave_de_un_cobro_que_ya_existia_devuelve_el_resultado_original_sin_consultas_cupo_ni_otra_llamada_a_la_pasarela()
    {
        var e = await ConIntencionLista();
        e.Pasarela.Respuesta = _ => ResultadoDeCobro.Rechazado(MotivoDeRechazo.TarjetaVencida);
        var entrada = await Pedir(e, AppMedio.Tarjeta);
        var primero = await e.Cobrar.Ejecutar(entrada);
        var reservasDeCupo = e.LimitesDeCobro.Reservas;
        var consultasDeCuenta = e.Cuenta.Consultas;
        e.Cuenta.Estado = new EstadoDeLaCuentaParaPagar(false, MotivoDeCuentaQueNoPuedePagar.CancelacionDiferida);

        var repetido = await e.Cobrar.Ejecutar(entrada);

        Assert.True(repetido.EsExito);
        Assert.Equal(primero.Valor, repetido.Valor);
        Assert.Single(e.Pasarela.Solicitudes);
        Assert.Equal(reservasDeCupo, e.LimitesDeCobro.Reservas);
        Assert.Equal(consultasDeCuenta, e.Cuenta.Consultas);
        Assert.Single(e.Repositorio.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Una_clave_cuyo_cobro_no_existia_se_rechaza_si_la_cuenta_no_puede_pagar_y_no_registra_nada()
    {
        var e = await ConIntencionLista();
        var entrada = await Pedir(e);
        e.Cuenta.Estado = new EstadoDeLaCuentaParaPagar(false, MotivoDeCuentaQueNoPuedePagar.TextosPendientes);

        var resultado = await e.Cobrar.Ejecutar(entrada);

        Assert.Equal("cuenta-no-puede-comprar", resultado.Error!.Clave);
        Assert.Empty(e.Pasarela.Solicitudes);
        Assert.Empty(e.Repositorio.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Un_intentoId_filtrado_no_devuelve_el_resultado_de_otra_cuenta()
    {
        var e = await ConIntencionLista();
        var entrada = await Pedir(e);
        await e.Cobrar.Ejecutar(entrada);
        var ajena = Guid.NewGuid();
        // Un token firmado de otra cuenta con el mismo intentoId.
        e.Firmador.Verificar(entrada.ClaveDeIntento, e.Reloj.Ahora, out var datos);
        var tokenAjeno = e.Firmador.Emitir(datos! with { CuentaId = ajena });
        e.Actor.Establecer(ActorDePrueba.DeCliente(ajena).Actual);

        var resultado = await e.Cobrar.Ejecutar(entrada with { ClaveDeIntento = tokenAjeno });

        Assert.Equal("clave-invalida", resultado.Error!.Clave);
        Assert.Single(e.Pasarela.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-03")]
    public async Task Un_token_alterado_vencido_de_otro_pedido_o_de_otra_cuenta_se_rechaza_sin_tocar_nada()
    {
        var e = await ConIntencionLista();
        var entrada = await Pedir(e);
        var operaciones = e.Repositorio.Operaciones;
        var deOtroPedido = e.Firmador.Emitir(new DatosDeLaClaveDeIntento(Guid.NewGuid(), Guid.NewGuid(), Cuenta, "huella-a", 14_000, e.Reloj.Ahora.AddMinutes(30)));
        var vencido = e.Firmador.Emitir(new DatosDeLaClaveDeIntento(Guid.NewGuid(), Pedido, Cuenta, "huella-a", 14_000, e.Reloj.Ahora.AddSeconds(-1)));

        foreach (var clave in new[] { "basura", entrada.ClaveDeIntento + "x", deOtroPedido, vencido })
        {
            var resultado = await e.Cobrar.Ejecutar(entrada with { ClaveDeIntento = clave });
            Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
            Assert.Equal("clave-invalida", resultado.Error.Clave);
        }

        Assert.Equal(operaciones, e.Repositorio.Operaciones);
        Assert.Empty(e.Pasarela.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Sin_sesion_se_responde_no_autenticado_sin_leer_nada()
    {
        var e = await ConIntencionLista();
        var entrada = await Pedir(e);
        e.Actor.Establecer(ActorDePrueba.Anonimo().Actual);
        var operaciones = e.Repositorio.Operaciones;

        var resultado = await e.Cobrar.Ejecutar(entrada);

        Assert.Equal(CategoriaDeError.NoAutenticado, resultado.Error!.Categoria);
        Assert.Equal(operaciones, e.Repositorio.Operaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Si_el_registro_del_intento_falla_la_pasarela_no_se_llama()
    {
        var limite = await ConIntencionLista();
        limite.LimitesDeCobro.Excedido = true;
        var inventario = await ConIntencionLista();
        inventario.Reservas.Consulta = new RespuestaDeReserva<EstadoDeLaReserva>(ResultadoDeLaLlamadaDeReserva.EstadoInvalido, null);

        var a = await limite.Cobrar.Ejecutar(await Pedir(limite));
        var b = await inventario.Cobrar.Ejecutar(await Pedir(inventario));

        Assert.Equal(CategoriaDeError.Limite, a.Error!.Categoria);
        Assert.Equal("reserva-expirada", b.Error!.Clave);
        Assert.Empty(limite.Pasarela.Solicitudes);
        Assert.Empty(inventario.Pasarela.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-30")]
    public async Task El_correo_del_titular_solo_viaja_a_la_pasarela_si_esta_lo_exige()
    {
        var sinCorreo = await ConIntencionLista();
        var conCorreo = await ConIntencionLista();
        var cobrar = conCorreo.CrearIniciarCobro(new OpcionesDelCobro(TimeSpan.FromMilliseconds(300), EnviarElCorreoDelTitular: true));

        await sinCorreo.Cobrar.Ejecutar(await Pedir(sinCorreo));
        await cobrar.Ejecutar(await Pedir(conCorreo));

        Assert.Null(Assert.Single(sinCorreo.Pasarela.Solicitudes).CorreoDelTitular);
        Assert.Equal("ana@correo.com", Assert.Single(conCorreo.Pasarela.Solicitudes).CorreoDelTitular);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-26")]
    public async Task Lo_que_llega_a_la_pasarela_la_auditoria_y_el_resultado_no_llevan_el_contacto_de_entrega()
    {
        var e = await ConIntencionLista();

        var resultado = await e.Cobrar.Ejecutar(await Pedir(e));

        var todo = e.Pasarela.Solicitudes.Single() + " " + resultado.Valor + string.Join(' ', e.Auditoria.Solicitudes.SelectMany(s => s.Detalle.Values));
        foreach (var secreto in new[] { "Ana Pérez", "987654321", "Olivos", "tkn_test" })
        {
            Assert.DoesNotContain(secreto, todo);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-02")]
    public async Task Los_motivos_de_rechazo_publicos_son_los_del_dominio_con_los_mismos_nombres()
    {
        foreach (var motivo in Enum.GetValues<MotivoDeRechazo>())
        {
            Assert.Equal(motivo.ToString(), MapeoDeMotivos.APublico(motivo).ToString());
        }

        Assert.Equal(MotivoDeRechazoDePago.Otro, MapeoDeMotivos.APublico(null));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-05")]
    public async Task Un_cobro_con_la_intencion_ya_con_un_intento_activo_no_llama_a_la_pasarela_de_nuevo()
    {
        var e = await ConIntencionLista();
        e.Pasarela.NoResponde = true;
        await e.Cobrar.Ejecutar(await Pedir(e));
        e.Pasarela.NoResponde = false;

        var otraClave = e.Firmador.Emitir(new DatosDeLaClaveDeIntento(Guid.NewGuid(), Pedido, Cuenta, "huella-a", 14_000, e.Reloj.Ahora.AddMinutes(30)));
        var segundo = await e.Cobrar.Ejecutar(new CobrarEntrada(Pedido, otraClave, AppMedio.Tarjeta, TokenDelMedioDePago.Crear("tkn_test_otro123")));

        Assert.Equal("pago-en-curso", segundo.Error!.Clave);
        Assert.Single(e.Pasarela.Solicitudes);
    }
}
