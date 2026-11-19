using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Domain.Pagos;
using static BabelHub.Ecommerce.UnitTests.Pagos.InstantaneasDeCompraDePrueba;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>Crear, reabrir y cancelar la intención de pago que Carrito pide a Pagos (T09; PAG-14, PAG-16, PAG-17, D01, D21).</summary>
public sealed class IntencionesDePagoDeLaAplicacionTests
{
    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Crear_inserta_la_intencion_con_el_pedido_como_identificador_los_plazos_y_la_instantanea()
    {
        var e = new EntornoDePagos();

        var resultado = await e.CrearMixta();

        Assert.Equal(ResultadoDeCrearIntencion.Creada, resultado);
        var intencion = Assert.Single(e.Repositorio.Intenciones);
        Assert.Equal(Pedido, intencion.Id);
        Assert.Equal(Cuenta, intencion.CuentaId);
        Assert.Equal(EstadoDeIntencion.Abierta, intencion.Estado);
        Assert.Equal(Importe.De(140m), intencion.Total);
        Assert.Equal(Importe.De(15m), intencion.Envio);
        Assert.Equal(e.Reloj.Ahora.AddMinutes(15), intencion.VenceEn);
        Assert.Equal(e.Reloj.Ahora.AddMinutes(30), intencion.LimitePendienteEn);
        Assert.Equal(e.ReservaExpiraEn, intencion.ReservaExpiraEn);
        Assert.Equal("huella-a", intencion.HuellaDeInstantanea);
        Assert.Equal(2, intencion.Lineas.Count);
        Assert.Equal("Ana Pérez Quispe", intencion.Contacto!.Nombre);
        Assert.Equal("AYA", intencion.RegionDeEntrega);
        Assert.Equal(TipoDeEntrega.Domicilio, intencion.TipoDeEntrega);
        Assert.Equal("2026-11", intencion.Terminos!.Version);
        Assert.NotNull(intencion.ReembolsoDigital);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Crear_una_compra_solo_digital_vence_a_los_30_minutos_y_no_tiene_reserva_ni_contacto()
    {
        var e = new EntornoDePagos();

        var resultado = await e.Creador.Crear(Cuenta, SoloDigital(), "huella-d", e.ReservaExpiraEn);

        Assert.Equal(ResultadoDeCrearIntencion.Creada, resultado);
        var intencion = Assert.Single(e.Repositorio.Intenciones);
        Assert.False(intencion.TieneImpresos);
        Assert.Null(intencion.ReservaExpiraEn);
        Assert.Equal(e.Reloj.Ahora.AddMinutes(30), intencion.VenceEn);
        Assert.Null(intencion.Contacto);
        Assert.Equal(TipoDeEntrega.SinEntrega, intencion.TipoDeEntrega);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Con_impresos_la_vigencia_nunca_pasa_de_la_reserva()
    {
        var e = new EntornoDePagos();

        await e.Creador.Crear(Cuenta, Mixta(), "huella-a", e.Reloj.Ahora.AddMinutes(9));

        Assert.Equal(e.Reloj.Ahora.AddMinutes(9), Assert.Single(e.Repositorio.Intenciones).VenceEn);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Crear_de_nuevo_con_la_misma_huella_es_idempotente_y_no_cambia_nada()
    {
        var e = new EntornoDePagos();
        await e.CrearMixta();
        e.Reloj.Ahora = e.Reloj.Ahora.AddMinutes(3);

        var repetida = await e.CrearMixta();

        Assert.Equal(ResultadoDeCrearIntencion.Creada, repetida);
        var intencion = Assert.Single(e.Repositorio.Intenciones);
        Assert.Equal(IntencionesDePrueba.Inicio, intencion.IniciadaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Crear_con_otra_huella_sin_intento_activo_reemplaza_la_instantanea_y_renueva_los_plazos()
    {
        var e = new EntornoDePagos();
        await e.CrearMixta("huella-a");
        e.Reloj.Ahora = e.Reloj.Ahora.AddMinutes(4);

        var resultado = await e.Creador.Crear(Cuenta, Mixta("huella-b", envio: 20m), "huella-b", e.ReservaExpiraEn);

        Assert.Equal(ResultadoDeCrearIntencion.Creada, resultado);
        var intencion = Assert.Single(e.Repositorio.Intenciones);
        Assert.Equal("huella-b", intencion.HuellaDeInstantanea);
        Assert.Equal(Importe.De(145m), intencion.Total);
        Assert.Equal(e.Reloj.Ahora, intencion.IniciadaEn);
        Assert.Equal(e.Reloj.Ahora.AddMinutes(15), intencion.VenceEn);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Crear_con_otra_huella_y_un_intento_activo_es_un_conflicto_y_no_cambia_la_instantanea()
    {
        var e = new EntornoDePagos();
        await e.CrearMixta("huella-a");
        var intencion = e.Repositorio.Intenciones.Single();
        intencion.IniciarCobro(e.Reloj.Ahora.AddSeconds(30));
        e.Repositorio.Sembrar(intencion);

        var resultado = await e.Creador.Crear(Cuenta, Mixta("huella-b"), "huella-b", e.ReservaExpiraEn);

        Assert.Equal(ResultadoDeCrearIntencion.Conflicto, resultado);
        Assert.Equal("huella-a", e.Repositorio.Intenciones.Single().HuellaDeInstantanea);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Crear_sobre_una_cerrada_responde_cerrada_y_sobre_una_aprobada_responde_en_curso_con_cualquier_huella()
    {
        var cerrada = new EntornoDePagos();
        await cerrada.CrearMixta();
        await cerrada.Creador.Cancelar(Cuenta, Pedido);
        var aprobada = new EntornoDePagos();
        await aprobada.CrearMixta();
        var intencion = aprobada.Repositorio.Intenciones.Single();
        intencion.IniciarCobro(aprobada.Reloj.Ahora.AddSeconds(30));
        intencion.AplicarAprobacion(
            IntentoDeCobro.Iniciar(Guid.NewGuid(), Pedido, Domain.Pagos.MedioDePago.Yape, Importe.De(140m), aprobada.Reloj.Ahora.AddSeconds(30)),
            "ch_1",
            aprobada.Reloj.Ahora.AddMinutes(1));
        aprobada.Repositorio.Sembrar(intencion);

        Assert.Equal(ResultadoDeCrearIntencion.Cerrada, await cerrada.CrearMixta("huella-b"));
        Assert.Equal(ResultadoDeCrearIntencion.YaEnCurso, await aprobada.CrearMixta("huella-a"));
        Assert.Equal(ResultadoDeCrearIntencion.YaEnCurso, await aprobada.CrearMixta("huella-b"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Crear_bloquea_la_intencion_y_no_hace_llamadas_de_red()
    {
        var e = new EntornoDePagos();

        await e.CrearMixta();

        Assert.Equal(1, e.Repositorio.LecturasConBloqueo);
        Assert.Equal(1, e.Unidad.Ejecuciones);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Crear_con_la_instantanea_de_otra_cuenta_se_rechaza_y_no_guarda_nada()
    {
        var e = new EntornoDePagos();

        await Assert.ThrowsAsync<ArgumentException>(() => e.Creador.Crear(Guid.NewGuid(), Mixta(), "huella-a", e.ReservaExpiraEn));
        await Assert.ThrowsAsync<ArgumentException>(() => e.Creador.Crear(Cuenta, Mixta(), " ", e.ReservaExpiraEn));

        Assert.Empty(e.Repositorio.Intenciones);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Crear_sobre_el_pedido_de_otra_cuenta_deja_un_acceso_denegado_y_falla_sin_tocar_la_intencion()
    {
        var e = new EntornoDePagos();
        await e.CrearMixta();
        var ajena = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Creador.Crear(ajena, Mixta("huella-b", cuenta: ajena), "huella-b", e.ReservaExpiraEn));

        Assert.Equal("huella-a", e.Repositorio.Intenciones.Single().HuellaDeInstantanea);
        Assert.Equal(Cuenta, e.Repositorio.Intenciones.Single().CuentaId);
        var denegacion = Assert.Single(e.Denegaciones.Eventos);
        Assert.Equal("ECO-NO-ENCONTRADO", denegacion.CodigoDeError);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Una_instantanea_incoherente_se_rechaza_antes_de_guardar()
    {
        var e = new EntornoDePagos();
        var mala = Mixta() with { Total = 139m };

        await Assert.ThrowsAsync<ArgumentException>(() => e.Creador.Crear(Cuenta, mala, "huella-a", e.ReservaExpiraEn));
        await Assert.ThrowsAsync<ArgumentException>(() => e.Creador.Crear(Cuenta, Mixta() with { Aceptaciones = [] }, "huella-a", e.ReservaExpiraEn));

        Assert.Empty(e.Repositorio.Intenciones);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Reabrir_deja_la_intencion_abierta_sin_contacto_y_obsoleta()
    {
        var e = new EntornoDePagos();
        await e.CrearMixta();

        var resultado = await e.Creador.Reabrir(Cuenta, Pedido);

        Assert.Equal(ResultadoDeReabrirIntencion.Reabierta, resultado);
        var intencion = e.Repositorio.Intenciones.Single();
        Assert.Null(intencion.Contacto);
        Assert.True(intencion.InstantaneaObsoleta);
        Assert.Equal(ValidacionDelCobro.InstantaneaObsoleta, intencion.ValidarCobro(e.Reloj.Ahora.AddSeconds(1)));
        Assert.Equal(2, e.Repositorio.LecturasConBloqueo); // la creación y la reapertura
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Despues_de_reabrir_un_crear_con_la_misma_huella_reemplaza_la_instantanea_obsoleta()
    {
        var e = new EntornoDePagos();
        await e.CrearMixta();
        await e.Creador.Reabrir(Cuenta, Pedido);

        var resultado = await e.CrearMixta();

        Assert.Equal(ResultadoDeCrearIntencion.Creada, resultado);
        var intencion = e.Repositorio.Intenciones.Single();
        Assert.False(intencion.InstantaneaObsoleta);
        Assert.NotNull(intencion.Contacto);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Reabrir_con_un_intento_activo_no_se_puede_y_una_cerrada_o_desconocida_se_dice_asi()
    {
        var conCobro = new EntornoDePagos();
        await conCobro.CrearMixta();
        var intencion = conCobro.Repositorio.Intenciones.Single();
        intencion.IniciarCobro(conCobro.Reloj.Ahora.AddSeconds(30));
        conCobro.Repositorio.Sembrar(intencion);
        var cerrada = new EntornoDePagos();
        await cerrada.CrearMixta();
        await cerrada.Creador.Cancelar(Cuenta, Pedido);
        var vacia = new EntornoDePagos();

        Assert.Equal(ResultadoDeReabrirIntencion.NoCancelable, await conCobro.Creador.Reabrir(Cuenta, Pedido));
        Assert.Equal(ResultadoDeReabrirIntencion.Cerrada, await cerrada.Creador.Reabrir(Cuenta, Pedido));
        Assert.Equal(ResultadoDeReabrirIntencion.NoExiste, await vacia.Creador.Reabrir(Cuenta, Pedido));
        Assert.NotNull(conCobro.Repositorio.Intenciones.Single().Contacto);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Reabrir_y_cancelar_el_pedido_de_otra_cuenta_responde_como_inexistente_y_registra_un_acceso_denegado_por_cada_intento()
    {
        var e = new EntornoDePagos();
        await e.CrearMixta();
        var ajena = Guid.NewGuid();

        var reabrir = await e.Creador.Reabrir(ajena, Pedido);
        var cancelar = await e.Creador.Cancelar(ajena, Pedido);

        Assert.Equal(ResultadoDeReabrirIntencion.NoExiste, reabrir);
        Assert.Equal(ResultadoDeCancelarIntencion.NoExiste, cancelar);
        Assert.Equal(2, e.Denegaciones.Eventos.Count);
        Assert.Equal(EstadoDeIntencion.Abierta, e.Repositorio.Intenciones.Single().Estado);
        Assert.NotNull(e.Repositorio.Intenciones.Single().Contacto);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Una_intencion_inexistente_no_deja_un_acceso_denegado()
    {
        var e = new EntornoDePagos();

        Assert.Equal(ResultadoDeCancelarIntencion.NoExiste, await e.Creador.Cancelar(Cuenta, Guid.NewGuid()));

        Assert.Empty(e.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Cancelar_cierra_una_intencion_abierta_y_no_una_con_intento_activo()
    {
        var e = new EntornoDePagos();
        await e.CrearMixta();

        Assert.Equal(ResultadoDeCancelarIntencion.Cancelada, await e.Creador.Cancelar(Cuenta, Pedido));
        Assert.Equal(ResultadoDeCancelarIntencion.Cerrada, await e.Creador.Cancelar(Cuenta, Pedido));
        var intencion = e.Repositorio.Intenciones.Single();
        Assert.Equal(EstadoDeIntencion.CerradaSinCobro, intencion.Estado);
        Assert.Null(intencion.Contacto);

        var conCobro = new EntornoDePagos();
        await conCobro.CrearMixta();
        var activa = conCobro.Repositorio.Intenciones.Single();
        activa.IniciarCobro(conCobro.Reloj.Ahora.AddSeconds(30));
        conCobro.Repositorio.Sembrar(activa);
        Assert.Equal(ResultadoDeCancelarIntencion.NoCancelable, await conCobro.Creador.Cancelar(Cuenta, Pedido));
        Assert.Equal(EstadoDeIntencion.ConCobro, conCobro.Repositorio.Intenciones.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Un_fallo_al_bloquear_la_intencion_se_propaga_y_no_deja_nada_a_medias()
    {
        var e = new EntornoDePagos();
        await e.CrearMixta();
        e.Repositorio.BloqueoNoDisponible = true;

        await Assert.ThrowsAsync<TimeoutException>(() => e.Creador.Reabrir(Cuenta, Pedido));

        Assert.NotNull(e.Repositorio.Intenciones.Single().Contacto);
        Assert.False(e.Repositorio.Intenciones.Single().InstantaneaObsoleta);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-26")]
    public async Task La_conversion_no_deja_el_contacto_en_ningun_texto_de_la_intencion()
    {
        var e = new EntornoDePagos();
        await e.CrearMixta();

        var texto = e.Repositorio.Intenciones.Single() + " " + e.Repositorio.Intenciones.Single().Contacto;

        Assert.DoesNotContain("Ana", texto);
        Assert.DoesNotContain("987654321", texto);
        Assert.DoesNotContain("Olivos", texto);
    }
}
