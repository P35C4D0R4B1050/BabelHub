using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.UnitTests.Soporte;
using static BabelHub.Ecommerce.UnitTests.Pagos.EntornoDePagosExtensiones;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>Registrar el intento: consultas fuera de la transacción, marca de la cuenta antes de la intención, token y plazos, y falla cerrada (T11; PAG-04, PAG-08, PAG-17, PAG-21, PAG-25, PAG-31; D10, D24).</summary>
public sealed class RegistrarIntentoTests
{
    [Fact]
    [Trait("Criterio", "AC-PAG-03")]
    public async Task Un_cobro_valido_inserta_el_intento_iniciado_pasa_la_intencion_a_con_cobro_y_audita_sin_datos_de_pago()
    {
        var e = await ConIntencionLista();
        var entrada = e.Entrada(medio: Domain.Pagos.MedioDePago.Tarjeta);

        var resultado = await e.RegistrarIntento.Registrar(entrada);

        Assert.True(resultado.EsExito);
        var intento = Assert.Single(e.Repositorio.Intentos);
        Assert.Equal(entrada.Clave.IntentoId, intento.Id);
        Assert.Equal(EstadoDeIntento.Iniciado, intento.Estado);
        Assert.Equal(Domain.Pagos.MedioDePago.Tarjeta, intento.Medio);
        Assert.Equal(Importe.De(140m), intento.Importe);
        Assert.Equal(EstadoDeIntencion.ConCobro, e.Repositorio.Intenciones.Single().Estado);
        var auditoria = Assert.Single(e.Auditoria.Solicitudes);
        Assert.Equal("CobroIniciado", auditoria.Accion.Identificador);
        Assert.Equal("intento-de-cobro", auditoria.ObjetoTipo);
        Assert.Equal(intento.Id.ToString("D"), auditoria.ObjetoId);
        Assert.Equal("tarjeta", auditoria.Detalle["medio"]);
        Assert.Equal("140.00", auditoria.Detalle["importe"]);
        Assert.Equal(2, auditoria.Detalle.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Las_consultas_a_identidad_a_la_posesion_a_las_reservas_y_al_limite_van_fuera_de_la_transaccion()
    {
        var e = await ConIntencionLista();

        await e.RegistrarIntento.Registrar(e.Entrada());

        Assert.Equal(0, e.Cuenta.ConsultasDentroDeUnaTransaccion);
        Assert.Equal(0, e.Reservas.LlamadasDentroDeUnaTransaccion);
        Assert.Equal(1, e.Reservas.Consultas);
        Assert.Equal(3, e.LimitesDeCobro.Reservas);
        Assert.Single(e.Posesion.Consultadas);
    }

    [Fact]
    public async Task La_marca_de_cancelacion_se_lee_con_bloqueo_antes_de_bloquear_la_intencion()
    {
        var e = await ConIntencionLista();
        e.Orden.Clear();

        await e.RegistrarIntento.Registrar(e.Entrada());

        var posicionDeLaMarca = e.Orden.IndexOf("marca-con-bloqueo");
        var posicionDeLaIntencion = e.Orden.IndexOf("intencion-con-bloqueo");
        Assert.True(posicionDeLaMarca >= 0 && posicionDeLaIntencion > posicionDeLaMarca, string.Join(" > ", e.Orden));
        Assert.Equal(1, e.Cuenta.MarcasLeidasConBloqueo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-28")]
    public async Task Con_la_cancelacion_de_la_cuenta_en_ejecucion_no_se_cobra()
    {
        var e = await ConIntencionLista();
        e.Cuenta.CancelacionEnCurso = true;

        var resultado = await e.RegistrarIntento.Registrar(e.Entrada());

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal("cuenta-no-puede-comprar", resultado.Error.Clave);
        Assert.Equal("cancelacion-en-ejecucion", resultado.Error.Errores.Single().Clave);
        Assert.Empty(e.Repositorio.Intentos);
        Assert.Equal(EstadoDeIntencion.Abierta, e.Repositorio.Intenciones.Single().Estado);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-21")]
    [InlineData("CorreoSinVerificar")]
    [InlineData("CancelacionDiferida")]
    [InlineData("TextosPendientes")]
    [InlineData("CuentaInactiva")]
    public async Task Una_cuenta_que_no_puede_pagar_se_rechaza_con_su_motivo_sin_gastar_cupo_ni_registrar_el_intento(string nombreDelMotivo)
    {
        var e = await ConIntencionLista();
        var motivo = Enum.Parse<MotivoDeCuentaQueNoPuedePagar>(nombreDelMotivo);
        e.Cuenta.Estado = new EstadoDeLaCuentaParaPagar(false, motivo);

        var resultado = await e.RegistrarIntento.Registrar(e.Entrada());

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal("cuenta-no-puede-comprar", resultado.Error.Clave);
        Assert.Equal(motivo.ToString(), resultado.Error.Errores.Single().Clave);
        Assert.Empty(e.Repositorio.Intentos);
        Assert.Equal(0, e.LimitesDeCobro.Reservas);
        Assert.Equal(0, e.Reservas.Consultas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Si_identidad_no_responde_o_no_sabe_no_se_cobra()
    {
        var cae = await ConIntencionLista();
        cae.Cuenta.Falla = true;
        var nula = await ConIntencionLista();
        nula.Cuenta.Estado = null;

        Assert.Equal(CategoriaDeError.NoDisponible, (await cae.RegistrarIntento.Registrar(cae.Entrada())).Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, (await nula.RegistrarIntento.Registrar(nula.Entrada())).Error!.Categoria);
        Assert.Empty(cae.Repositorio.Intentos);
        Assert.Empty(nula.Repositorio.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-18")]
    public async Task Una_digital_que_la_cuenta_ya_posee_se_rechaza_y_si_la_posesion_no_se_puede_confirmar_no_se_cobra()
    {
        var posee = await ConIntencionLista();
        posee.Posesion.Resultado = ResultadoDePosesion.Posee([Guid.Parse("44444444-4444-4444-4444-444444444442")]);
        var incierta = await ConIntencionLista();
        incierta.Posesion.Resultado = ResultadoDePosesion.NoSePuedeConfirmar;
        var cae = await ConIntencionLista();
        cae.Posesion.Falla = true;

        Assert.Equal("digital-ya-poseida", (await posee.RegistrarIntento.Registrar(posee.Entrada())).Error!.Clave);
        Assert.Equal(CategoriaDeError.NoDisponible, (await incierta.RegistrarIntento.Registrar(incierta.Entrada())).Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, (await cae.RegistrarIntento.Registrar(cae.Entrada())).Error!.Categoria);
        Assert.Empty(posee.Repositorio.Intentos);
        Assert.Equal([Guid.Parse("44444444-4444-4444-4444-444444444442")], posee.Posesion.Consultadas.Single());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public async Task Una_compra_solo_digital_no_consulta_reservas()
    {
        var e = new EntornoDePagos();
        await e.Creador.Crear(InstantaneasDeCompraDePrueba.Cuenta, InstantaneasDeCompraDePrueba.SoloDigital(), "huella-d", e.ReservaExpiraEn);
        e.Reloj.Ahora = IntencionesDePrueba.Inicio.AddSeconds(30);
        var entrada = e.Entrada(huella: "huella-d", total: 2_500);

        var resultado = await e.RegistrarIntento.Registrar(entrada);

        Assert.True(resultado.EsExito);
        Assert.Equal(0, e.Reservas.Consultas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public async Task Con_la_reserva_expirada_en_el_inventario_se_niega_el_cobro_aunque_el_reloj_local_diga_que_hay_tiempo()
    {
        var expirada = await ConIntencionLista();
        expirada.Reservas.Consulta = new RespuestaDeReserva<EstadoDeLaReserva>(ResultadoDeLaLlamadaDeReserva.Correcto, new EstadoDeLaReserva(expirada.Reloj.Ahora.AddMinutes(-1), false));
        var invalida = await ConIntencionLista();
        invalida.Reservas.Consulta = new RespuestaDeReserva<EstadoDeLaReserva>(ResultadoDeLaLlamadaDeReserva.EstadoInvalido, null);

        Assert.Equal("reserva-expirada", (await expirada.RegistrarIntento.Registrar(expirada.Entrada())).Error!.Clave);
        Assert.Equal("reserva-expirada", (await invalida.RegistrarIntento.Registrar(invalida.Entrada())).Error!.Clave);
        Assert.Empty(expirada.Repositorio.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public async Task Si_el_inventario_no_responde_no_se_cobra()
    {
        var sinRespuesta = await ConIntencionLista();
        sinRespuesta.Reservas.Consulta = new RespuestaDeReserva<EstadoDeLaReserva>(ResultadoDeLaLlamadaDeReserva.SinRespuesta, null);
        var cae = await ConIntencionLista();
        cae.Reservas.Falla = true;

        Assert.Equal(CategoriaDeError.NoDisponible, (await sinRespuesta.RegistrarIntento.Registrar(sinRespuesta.Entrada())).Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, (await cae.RegistrarIntento.Registrar(cae.Entrada())).Error!.Categoria);
        Assert.Empty(sinRespuesta.Repositorio.Intentos);
        Assert.Empty(cae.Repositorio.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Pasado_el_limite_el_cobro_se_rechaza_con_la_espera_y_no_registra_nada()
    {
        var e = await ConIntencionLista();
        e.LimitesDeCobro.Excedido = true;

        var resultado = await e.RegistrarIntento.Registrar(e.Entrada());

        Assert.Equal(CategoriaDeError.Limite, resultado.Error!.Categoria);
        Assert.Equal("900", resultado.Error.Errores.Single().Clave);
        Assert.Empty(e.Repositorio.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Con_el_almacen_de_contadores_caido_no_se_cobra()
    {
        var e = await ConIntencionLista();
        e.LimitesDeCobro.Caido = true;

        var resultado = await e.RegistrarIntento.Registrar(e.Entrada());

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Empty(e.Repositorio.Intentos);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-09")]
    [InlineData("huella-otra", 14_000L)]
    [InlineData("huella-a", 14_500L)]
    public async Task Si_el_carrito_cambio_en_otra_pestana_el_token_no_coincide_y_se_pide_volver_a_mostrar_el_formulario(string huella, long total)
    {
        var e = await ConIntencionLista();

        var resultado = await e.RegistrarIntento.Registrar(e.Entrada(huella: huella, total: total));

        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
        Assert.Equal("total-cambio", resultado.Error.Clave);
        Assert.Empty(e.Repositorio.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public async Task Una_intencion_cancelada_obsoleta_o_vencida_no_registra_el_intento_y_dice_por_que()
    {
        var cancelada = await ConIntencionLista();
        await cancelada.Creador.Cancelar(InstantaneasDeCompraDePrueba.Cuenta, InstantaneasDeCompraDePrueba.Pedido);
        var obsoleta = await ConIntencionLista();
        await obsoleta.Creador.Reabrir(InstantaneasDeCompraDePrueba.Cuenta, InstantaneasDeCompraDePrueba.Pedido);
        var vencida = await ConIntencionLista();
        vencida.Reloj.Ahora = IntencionesDePrueba.Inicio.AddMinutes(15);
        vencida.Reservas.Consulta = new RespuestaDeReserva<EstadoDeLaReserva>(ResultadoDeLaLlamadaDeReserva.Correcto, new EstadoDeLaReserva(vencida.Reloj.Ahora.AddMinutes(5), true));

        Assert.Equal("intencion-cancelada", (await cancelada.RegistrarIntento.Registrar(cancelada.Entrada(huella: "huella-a"))).Error!.Clave);
        Assert.Equal("instantanea-obsoleta", (await obsoleta.RegistrarIntento.Registrar(obsoleta.Entrada())).Error!.Clave);
        Assert.Equal("reserva-expirada", (await vencida.RegistrarIntento.Registrar(vencida.Entrada())).Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Carrito_debe_devolver_el_pedido_de_esta_intencion_en_en_pago_y_se_lee_dentro_de_la_transaccion_sin_bloqueo()
    {
        var otro = await ConIntencionLista();
        otro.Checkout.Respuesta = Resultado<CheckoutVivoParaPagar>.Exito(new CheckoutVivoParaPagar(Guid.NewGuid(), EstadoVisibleDelCheckout.EnPago));
        var pendiente = await ConIntencionLista();
        pendiente.Checkout.Respuesta = Resultado<CheckoutVivoParaPagar>.Exito(new CheckoutVivoParaPagar(InstantaneasDeCompraDePrueba.Pedido, EstadoVisibleDelCheckout.PagoPendiente));
        var sinVivo = await ConIntencionLista();
        sinVivo.Checkout.Respuesta = Resultado<CheckoutVivoParaPagar>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "no-encontrado"));

        Assert.Equal("instantanea-obsoleta", (await otro.RegistrarIntento.Registrar(otro.Entrada())).Error!.Clave);
        Assert.Equal("pago-en-curso", (await pendiente.RegistrarIntento.Registrar(pendiente.Entrada())).Error!.Clave);
        Assert.Equal("instantanea-obsoleta", (await sinVivo.RegistrarIntento.Registrar(sinVivo.Entrada())).Error!.Clave);
        Assert.Empty(otro.Repositorio.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Otro_intento_activo_de_la_misma_intencion_rechaza_el_segundo_cobro_aunque_llegue_desde_otra_pestana()
    {
        var e = await ConIntencionLista();
        await e.RegistrarIntento.Registrar(e.Entrada());

        var segundo = await e.RegistrarIntento.Registrar(e.Entrada(medio: Domain.Pagos.MedioDePago.Tarjeta));

        Assert.Equal(CategoriaDeError.Conflicto, segundo.Error!.Categoria);
        Assert.Equal("pago-en-curso", segundo.Error.Clave);
        Assert.Single(e.Repositorio.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public async Task Una_clave_ya_usada_devuelve_el_intento_original_sin_insertar_otro()
    {
        var e = await ConIntencionLista();
        var clave = Guid.NewGuid();
        await e.RegistrarIntento.Registrar(e.Entrada(clave));
        var intencion = e.Repositorio.Intenciones.Single();
        intencion.ResolverIntentoSinCobro(e.Reloj.Ahora);
        e.Repositorio.Sembrar(intencion);
        var intento = e.Repositorio.Intentos.Single();
        intento.MarcarRechazado(MotivoDeRechazo.Otro, e.Reloj.Ahora);
        await e.Repositorio.GuardarIntento(intento);

        var repetida = await e.RegistrarIntento.Registrar(e.Entrada(clave));

        // La misma clave que ya se registró (por ejemplo, una solicitud simultánea que ganó el bloqueo) es el resultado original: no inserta otro intento.
        Assert.True(repetida.EsExito);
        Assert.True(repetida.Valor.EraRepetido);
        Assert.Equal(clave, repetida.Valor.Intento.Id);
        Assert.Single(e.Repositorio.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-03")]
    public async Task La_misma_clave_con_la_intencion_ya_con_cobro_es_el_resultado_original_y_no_un_cobro_en_curso_ajeno()
    {
        var e = await ConIntencionLista();
        var clave = Guid.NewGuid();
        var primera = await e.RegistrarIntento.Registrar(e.Entrada(clave));
        Assert.Equal(EstadoDeIntencion.ConCobro, e.Repositorio.Intenciones.Single().Estado);

        // La doble pulsación llega después de que el primer cobro registró su intento: la lectura previa ya ve la intención con cobro.
        var segunda = await e.RegistrarIntento.Registrar(e.Entrada(clave));

        Assert.True(segunda.EsExito);
        Assert.True(segunda.Valor.EraRepetido);
        Assert.Equal(primera.Valor.Intento.Id, segunda.Valor.Intento.Id);
        Assert.Single(e.Repositorio.Intentos);

        // Otra clave sí es un cobro en curso ajeno.
        var otra = await e.RegistrarIntento.Registrar(e.Entrada(Guid.NewGuid()));
        Assert.Equal("pago-en-curso", otra.Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Una_clave_que_es_de_un_intento_de_otra_intencion_no_se_devuelve_y_sigue_siendo_un_conflicto()
    {
        var e = await ConIntencionLista();
        await e.RegistrarIntento.Registrar(e.Entrada(Guid.NewGuid()));
        var ajena = Guid.NewGuid();
        await e.Repositorio.AgregarIntento(IntentoDeCobro.Iniciar(ajena, Guid.NewGuid(), MedioDePago.Yape, Importe.De(140m), e.Reloj.Ahora));

        var resultado = await e.RegistrarIntento.Registrar(e.Entrada(ajena));

        Assert.False(resultado.EsExito);
        Assert.Equal("pago-en-curso", resultado.Error!.Clave);
        Assert.Equal(2, e.Repositorio.Intentos.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task El_pedido_de_otra_cuenta_o_inexistente_responde_no_encontrado_con_un_acceso_denegado_y_sin_tocar_nada()
    {
        var e = await ConIntencionLista();
        var ajena = e.Entrada() with { CuentaId = Guid.NewGuid() };
        var inexistente = e.Entrada() with { PedidoId = Guid.NewGuid() };

        var a = await e.RegistrarIntento.Registrar(ajena);
        var b = await e.RegistrarIntento.Registrar(inexistente);

        Assert.Equal(CategoriaDeError.NoEncontrado, a.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoEncontrado, b.Error!.Categoria);
        Assert.Equal(2, e.Denegaciones.Eventos.Count);
        Assert.Equal(0, e.Cuenta.Consultas);
        Assert.Empty(e.Repositorio.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Sin_poder_auditar_no_se_registra_el_intento_ni_se_cobra_y_la_intencion_sigue_abierta()
    {
        var e = await ConIntencionLista();
        e.Auditoria.Caida = true;

        var resultado = await e.RegistrarIntento.Registrar(e.Entrada());

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Empty(e.Repositorio.Intentos);
        Assert.Equal(EstadoDeIntencion.Abierta, e.Repositorio.Intenciones.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-02")]
    public async Task El_resultado_no_muestra_datos_de_la_cuenta_ni_de_pago_en_su_texto()
    {
        var e = await ConIntencionLista();
        var resultado = await e.RegistrarIntento.Registrar(e.Entrada());

        var texto = resultado.Valor.Intento + " " + resultado.Valor;

        Assert.DoesNotContain("ana@correo.com", texto);
        Assert.DoesNotContain("Ana", texto);
    }
}
