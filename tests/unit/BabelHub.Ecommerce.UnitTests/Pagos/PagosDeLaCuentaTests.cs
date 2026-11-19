using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

public sealed class PagosDeLaCuentaTests
{
    private static readonly Guid Cuenta = InstantaneasDeCompraDePrueba.Cuenta;
    private static readonly Guid Pedido = InstantaneasDeCompraDePrueba.Pedido;
    private static readonly Guid OtroPedido = Guid.Parse("22222222-2222-2222-2222-222222222299");

    private static async Task<EntornoDeReembolsos> ConCompraCompletada() => await EntornoDeReembolsos.ConCompraCompletada();

    private static Task CrearOtraIntencion(EntornoDePagos e, Guid? cuenta = null) =>
        e.Creador.Crear(cuenta ?? Cuenta, InstantaneasDeCompraDePrueba.Mixta("huella-b", pedido: OtroPedido, cuenta: cuenta), "huella-b", e.ReservaExpiraEn);

    // ---------- Contar pendientes ----------

    [Fact]
    [Trait("Criterio", "AC-PAG-22")]
    public async Task Una_cuenta_sin_pagos_en_curso_ni_reembolsos_de_compensacion_cuenta_cero()
    {
        var e = await ConCompraCompletada();
        var contar = new ContarPendientesDeCuenta(e.Pagos.Repositorio, e.Pagos.Reembolsos);

        Assert.Equal(0, await contar.Contar(Cuenta));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-22")]
    public async Task Un_cobro_sin_resolver_y_una_compra_aprobada_sin_pedido_cuentan_como_pendientes()
    {
        var b = await EntornoDeConfirmacion.ConIntentoIniciado();
        var p = b.Entorno;
        var contar = new ContarPendientesDeCuenta(p.Repositorio, p.Reembolsos);
        Assert.Equal(1, await contar.Contar(Cuenta)); // con_cobro

        var intencion = b.Intencion;
        intencion.AplicarAprobacion(b.Intento, "ch_1", p.Reloj.Ahora);
        await p.Repositorio.GuardarIntencion(intencion);
        Assert.Equal(1, await contar.Contar(Cuenta)); // aprobada sin pedido

        intencion.Completar(p.Reloj.Ahora);
        await p.Repositorio.GuardarIntencion(intencion);
        Assert.Equal(0, await contar.Contar(Cuenta)); // completada
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-22")]
    public async Task Una_intencion_abierta_no_es_un_pendiente()
    {
        var e = await EntornoDePagosExtensiones.ConIntencionLista();
        var contar = new ContarPendientesDeCuenta(e.Repositorio, e.Reembolsos);

        Assert.Equal(0, await contar.Contar(Cuenta));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-22")]
    public async Task Los_reembolsos_de_compensacion_cuentan_pero_los_de_otro_origen_los_cuentan_Pedidos_y_Devoluciones()
    {
        var e = await ConCompraCompletada();
        var contar = new ContarPendientesDeCuenta(e.Pagos.Repositorio, e.Pagos.Reembolsos);
        await e.Pedir(OrigenDelReembolso.Anulacion, EntornoDeReembolsos.ClaveDeAnulacion, 10m);
        Assert.Equal(0, await contar.Contar(Cuenta));

        await e.Pagos.Unidad.Ejecutar(token => e.Registrar.RegistrarDeCompensacion(e.Base.Intento, token));

        Assert.Equal(1, await contar.Contar(Cuenta));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-22")]
    public async Task Los_pendientes_de_otra_cuenta_no_cuentan_y_una_cuenta_vacia_se_rechaza()
    {
        var b = await EntornoDeConfirmacion.ConIntentoIniciado();
        var contar = new ContarPendientesDeCuenta(b.Entorno.Repositorio, b.Entorno.Reembolsos);

        Assert.Equal(0, await contar.Contar(Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => contar.Contar(Guid.Empty));
    }

    // ---------- Desvincular ----------

    [Fact]
    [Trait("Criterio", "AC-PAG-27")]
    public async Task Cancelar_la_cuenta_desvincula_todas_sus_intenciones_y_conserva_importes_fechas_y_estados()
    {
        var e = await ConCompraCompletada();
        await CrearOtraIntencion(e.Pagos);
        var antes = e.Pagos.Repositorio.Intenciones.ToDictionary(i => i.Id, i => (i.Estado, i.Total, i.Envio, i.CreadaEn));
        var aplicador = new DesvincularPagosDeUnaCuenta(e.Pagos.Repositorio, e.Pagos.Auditoria, e.Pagos.Reloj);

        await aplicador.Aplicar(Cuenta, Guid.NewGuid());

        Assert.All(e.Pagos.Repositorio.Intenciones, i =>
        {
            Assert.Null(i.CuentaId);
            Assert.Null(i.Terminos);
            Assert.Null(i.Privacidad);
            Assert.Null(i.ReembolsoDigital);
            Assert.Null(i.TipoDeEntrega);
            Assert.Null(i.RegionDeEntrega);
            Assert.Null(i.Contacto);
            Assert.Null(i.HuellaDeInstantanea);
            Assert.All(i.Lineas, l =>
            {
                Assert.Null(l.EdicionId);
                Assert.Null(l.Titulo);
                Assert.Null(l.Isbn);
            });
            Assert.Equal(antes[i.Id], (i.Estado, i.Total, i.Envio, i.CreadaEn));
            Assert.Equal(e.Pagos.Reloj.Ahora, i.DesvinculadaEn);
        });
        Assert.Equal(2, e.Pagos.Repositorio.Intenciones.Count);
        Assert.Equal("pagos", aplicador.Modulo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-27")]
    public async Task Las_intenciones_de_otra_cuenta_no_se_tocan()
    {
        var e = await ConCompraCompletada();
        await CrearOtraIntencion(e.Pagos, cuenta: Guid.Parse("11111111-1111-1111-1111-111111111199"));
        var aplicador = new DesvincularPagosDeUnaCuenta(e.Pagos.Repositorio, e.Pagos.Auditoria, e.Pagos.Reloj);

        await aplicador.Aplicar(Cuenta, Guid.NewGuid());

        var ajena = e.Pagos.Repositorio.Intenciones.Single(i => i.Id == OtroPedido);
        Assert.NotNull(ajena.CuentaId);
        Assert.NotNull(ajena.Contacto);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-27")]
    public async Task Desvincular_es_idempotente_lee_con_bloqueo_y_se_reaplica_tras_restaurar_un_respaldo()
    {
        var e = await ConCompraCompletada();
        var respaldo = e.Pagos.Repositorio.Intenciones.Single();
        e.Pagos.Repositorio.Sembrar(respaldo);
        var aplicador = new DesvincularPagosDeUnaCuenta(e.Pagos.Repositorio, e.Pagos.Auditoria, e.Pagos.Reloj);
        var bloqueosAntes = e.Pagos.Repositorio.LecturasConBloqueo;

        await aplicador.Aplicar(Cuenta, Guid.NewGuid());
        var guardadosTrasLaPrimera = e.Pagos.Repositorio.GuardadosDeIntenciones;
        await aplicador.Aplicar(Cuenta, Guid.NewGuid());

        Assert.Equal(guardadosTrasLaPrimera, e.Pagos.Repositorio.GuardadosDeIntenciones);
        Assert.True(e.Pagos.Repositorio.LecturasConBloqueo > bloqueosAntes);

        // Se restaura un respaldo anterior: la intención reapareció con su cuenta y se vuelve a desvincular.
        var restaurada = IntencionDePago.Crear(Pedido, InstantaneaRestaurada(), e.Pagos.ReservaExpiraEn, e.Pagos.Reloj.Ahora);
        e.Pagos.Repositorio.Sembrar(restaurada);
        await aplicador.Aplicar(Cuenta, Guid.NewGuid());

        Assert.Null(e.Pagos.Repositorio.Intenciones.Single().CuentaId);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-27")]
    public async Task Si_falla_a_mitad_de_la_cancelacion_se_revierte_todo_dentro_de_la_transaccion_de_Identidad()
    {
        var e = await ConCompraCompletada();
        await CrearOtraIntencion(e.Pagos);
        var aplicador = new DesvincularPagosDeUnaCuenta(e.Pagos.Repositorio, e.Pagos.Auditoria, e.Pagos.Reloj);
        e.Pagos.Repositorio.GuardarIntencionLanzaEnLaLlamada = e.Pagos.Repositorio.GuardadosDeIntenciones + 2;

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Pagos.Unidad.Ejecutar(token => Aplicar(aplicador, token)));

        Assert.All(e.Pagos.Repositorio.Intenciones, i => Assert.NotNull(i.CuentaId));
    }

    private static async Task<bool> Aplicar(DesvincularPagosDeUnaCuenta aplicador, CancellationToken token)
    {
        await aplicador.Aplicar(Cuenta, Guid.NewGuid(), token);
        return true;
    }

    private static InstantaneaDeIntencion InstantaneaRestaurada() =>
        InstantaneaDeIntencion.Crear(
            Cuenta,
            TipoDeEntrega.Domicilio,
            null,
            "AYA",
            [IntencionesDePrueba.Impresa(), IntencionesDePrueba.Digital()],
            Importe.De(15m),
            Importe.De(140m),
            "huella-r",
            IntencionesDePrueba.Texto(),
            IntencionesDePrueba.Texto(),
            IntencionesDePrueba.Texto(),
            IntencionesDePrueba.Contacto());

    // ---------- Liberar el contacto ----------

    [Fact]
    [Trait("Criterio", "AC-PAG-23")]
    public async Task Pedidos_pide_liberar_el_contacto_de_una_compra_completada_y_se_elimina()
    {
        var e = await ConCompraCompletada();
        Assert.NotNull(e.Pagos.Repositorio.Intenciones.Single().Contacto);
        var liberar = new LiberarContactoDeLaIntencion(e.Pagos.Repositorio, e.Pagos.Unidad, new BloqueoDeIntenciones(e.Pagos.Repositorio, e.Pagos.Cuenta), e.Pagos.Auditoria, e.Pagos.Reloj);

        await liberar.Liberar(Pedido);

        var intencion = e.Pagos.Repositorio.Intenciones.Single();
        Assert.Null(intencion.Contacto);
        Assert.Equal(e.Pagos.Reloj.Ahora, intencion.ContactoLiberadoEn);
        Assert.Null(intencion.HuellaDeInstantanea);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-23")]
    public async Task Liberar_el_contacto_dos_veces_es_idempotente_y_lee_la_marca_antes_de_bloquear()
    {
        var e = await ConCompraCompletada();
        var liberar = new LiberarContactoDeLaIntencion(e.Pagos.Repositorio, e.Pagos.Unidad, new BloqueoDeIntenciones(e.Pagos.Repositorio, e.Pagos.Cuenta), e.Pagos.Auditoria, e.Pagos.Reloj);
        e.Pagos.Orden.Clear();

        await liberar.Liberar(Pedido);
        var guardados = e.Pagos.Repositorio.GuardadosDeIntenciones;
        e.Pagos.Reloj.Ahora = e.Pagos.Reloj.Ahora.AddMinutes(5);
        await liberar.Liberar(Pedido);

        Assert.Equal(guardados, e.Pagos.Repositorio.GuardadosDeIntenciones);
        Assert.Equal(["marca-con-bloqueo", "intencion-con-bloqueo"], e.Pagos.Orden.Take(2).ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-23")]
    public async Task El_contacto_de_una_compra_que_aun_no_tiene_pedido_no_se_libera_porque_lo_necesita_Pedidos()
    {
        var b = await EntornoDeConfirmacion.ConIntentoIniciado();
        var p = b.Entorno;
        var liberar = new LiberarContactoDeLaIntencion(p.Repositorio, p.Unidad, new BloqueoDeIntenciones(p.Repositorio, p.Cuenta), p.Auditoria, p.Reloj);

        await liberar.Liberar(Pedido);

        Assert.NotNull(b.Intencion.Contacto);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-23")]
    public async Task Liberar_el_contacto_de_una_intencion_desconocida_o_vacia_no_falla()
    {
        var e = await ConCompraCompletada();
        var liberar = new LiberarContactoDeLaIntencion(e.Pagos.Repositorio, e.Pagos.Unidad, new BloqueoDeIntenciones(e.Pagos.Repositorio, e.Pagos.Cuenta), e.Pagos.Auditoria, e.Pagos.Reloj);

        await liberar.Liberar(Guid.NewGuid());
        await liberar.Liberar(Guid.Empty);

        Assert.NotNull(e.Pagos.Repositorio.Intenciones.Single().Contacto);
    }
}
