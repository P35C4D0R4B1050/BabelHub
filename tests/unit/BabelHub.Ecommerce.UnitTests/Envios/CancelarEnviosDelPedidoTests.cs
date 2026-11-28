using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;

namespace BabelHub.Ecommerce.UnitTests.Envios;

/// <summary>Cancelar los envíos de un pedido anulado, y la implementación de <c>IDespachoDeEnvios</c> de Pedidos (T05; ENV-03, ENV-13, ENV-19).</summary>
public sealed class CancelarEnviosDelPedidoTests
{
    private static readonly DateTimeOffset Ahora = EntornoDeEnvios.AhoraInicial;

    private static Envio Sembrar(EntornoDeEnvios entorno, Guid pedidoId, Action<Envio>? preparar = null, params Guid[] lineas)
    {
        var envio = Envio.Crear(Guid.NewGuid(), pedidoId, "lima-central", lineas.Length == 0 ? [Guid.NewGuid()] : lineas, Ahora);
        preparar?.Invoke(envio);
        entorno.Envios.Sembrar(envio);
        return envio;
    }

    private static void ConGuia(Envio envio)
    {
        envio.TomarSolicitudDeGuia(Ahora, Guid.NewGuid());
        envio.RegistrarGuia(NumeroDeGuia.Crear("GUIA-000123"), "operador-a", Ahora);
    }

    private static void EnReparto(Envio envio)
    {
        ConGuia(envio);
        envio.IniciarDespacho(Ahora);
        envio.PasarAEnReparto(Ahora);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-12")]
    public async Task El_pendiente_se_cancela_el_en_reparto_se_mantiene_y_queda_una_entrada_de_auditoria()
    {
        var entorno = new EntornoDeEnvios();
        var pedido = Guid.NewGuid();
        var lineaPendiente = Guid.NewGuid();
        var lineaEnReparto = Guid.NewGuid();
        var pendiente = Sembrar(entorno, pedido, null, lineaPendiente);
        var enReparto = Sembrar(entorno, pedido, EnReparto, lineaEnReparto);

        var resultado = await entorno.Cancelar.Ejecutar(pedido);

        Assert.True(resultado.EsExito);
        var destinos = resultado.Valor.ToDictionary(r => r.EnvioId);
        Assert.Equal(DestinoDelEnvioAlCancelar.Cancelado, destinos[pendiente.Id].Destino);
        Assert.Equal([lineaPendiente], destinos[pendiente.Id].LineaIds);
        Assert.Equal(DestinoDelEnvioAlCancelar.Mantenido, destinos[enReparto.Id].Destino);
        Assert.Equal([lineaEnReparto], destinos[enReparto.Id].LineaIds);
        Assert.Equal(EstadoDeEnvio.Cancelado, entorno.Envios.Todos.Single(e => e.Id == pendiente.Id).Estado);
        Assert.Equal(EstadoDeEnvio.EnReparto, entorno.Envios.Todos.Single(e => e.Id == enReparto.Id).Estado);

        var entrada = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.EstadoDeEnvioCambiado, entrada.Accion);
        Assert.Equal("envio", entrada.ObjetoTipo);
        Assert.Equal(pendiente.Id.ToString(), entrada.ObjetoId);
        Assert.Equal("pendiente_de_guia", entrada.Detalle["estadoAnterior"]);
        Assert.Equal("cancelado", entrada.Detalle["estadoNuevo"]);
        Assert.Equal(pedido.ToString(), entrada.Detalle["pedidoId"]);
        Assert.Equal("lima-central", entrada.Detalle["fuente"]);
        Assert.Null(entrada.Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-12")]
    public async Task Un_entregado_nunca_se_cancela()
    {
        var entorno = new EntornoDeEnvios();
        var pedido = Guid.NewGuid();
        var entregado = Sembrar(entorno, pedido, e =>
        {
            EnReparto(e);
            e.MarcarEntregado(Ahora);
        });

        var resultado = await entorno.Cancelar.Ejecutar(pedido);

        Assert.Equal(DestinoDelEnvioAlCancelar.Mantenido, Assert.Single(resultado.Valor).Destino);
        Assert.Equal(EstadoDeEnvio.Entregado, entorno.Envios.Todos.Single(e => e.Id == entregado.Id).Estado);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-19")]
    public async Task El_de_guia_generada_sin_marcador_se_cancela_y_con_el_marcador_se_mantiene()
    {
        var entorno = new EntornoDeEnvios();
        var pedido = Guid.NewGuid();
        var sinMarcador = Sembrar(entorno, pedido, ConGuia);
        var conMarcador = Sembrar(entorno, pedido, e =>
        {
            ConGuia(e);
            e.IniciarDespacho(Ahora);
        });

        var resultado = await entorno.Cancelar.Ejecutar(pedido);

        var destinos = resultado.Valor.ToDictionary(r => r.EnvioId, r => r.Destino);
        Assert.Equal(DestinoDelEnvioAlCancelar.Cancelado, destinos[sinMarcador.Id]);
        Assert.Equal(DestinoDelEnvioAlCancelar.Mantenido, destinos[conMarcador.Id]);
        Assert.Equal(EstadoDeEnvio.GuiaGenerada, entorno.Envios.Todos.Single(e => e.Id == conMarcador.Id).Estado);
        var cancelado = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal("guia_generada", cancelado.Detalle["estadoAnterior"]);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-19")]
    public async Task Cancelar_corre_en_la_unidad_de_trabajo_del_llamador_y_no_abre_una_propia()
    {
        var entorno = new EntornoDeEnvios();
        var pedido = Guid.NewGuid();
        Sembrar(entorno, pedido);

        var resultado = await entorno.Unidad.Ejecutar(token => entorno.Cancelar.Ejecutar(pedido, token));

        Assert.True(resultado.EsExito);
        Assert.Equal(1, entorno.Unidad.Ejecuciones);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-12")]
    public async Task Un_envio_ya_cancelado_se_mantiene_sin_auditar_de_nuevo_y_sus_lineas_no_se_procesan_otra_vez()
    {
        var entorno = new EntornoDeEnvios();
        var pedido = Guid.NewGuid();
        var envio = Sembrar(entorno, pedido);

        var primera = await entorno.Cancelar.Ejecutar(pedido);
        var repeticion = await entorno.Cancelar.Ejecutar(pedido);

        Assert.Equal(DestinoDelEnvioAlCancelar.Cancelado, Assert.Single(primera.Valor).Destino);
        var mantenido = Assert.Single(repeticion.Valor);
        Assert.Equal(envio.Id, mantenido.EnvioId);
        Assert.Equal(DestinoDelEnvioAlCancelar.Mantenido, mantenido.Destino);
        Assert.Single(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-12")]
    public async Task Si_la_segunda_auditoria_falla_la_excepcion_se_propaga_y_la_unidad_del_llamador_revierte_todo()
    {
        var entorno = new EntornoDeEnvios();
        var pedido = Guid.NewGuid();
        Sembrar(entorno, pedido);
        Sembrar(entorno, pedido);
        entorno.Auditoria.FallaEnLaLlamada = 2;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.Unidad.Ejecutar(token => entorno.Cancelar.Ejecutar(pedido, token)));

        Assert.Empty(entorno.Auditoria.Solicitudes);
        Assert.All(entorno.Envios.Todos, e => Assert.Equal(EstadoDeEnvio.PendienteDeGuia, e.Estado));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-12")]
    public async Task Si_el_segundo_guardado_falla_se_propaga_y_la_unidad_del_llamador_revierte_todo()
    {
        var entorno = new EntornoDeEnvios();
        var pedido = Guid.NewGuid();
        Sembrar(entorno, pedido);
        Sembrar(entorno, pedido);
        entorno.Envios.FallaEnElGuardadoNumero = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.Unidad.Ejecutar(token => entorno.Cancelar.Ejecutar(pedido, token)));

        Assert.Empty(entorno.Auditoria.Solicitudes);
        Assert.All(entorno.Envios.Todos, e => Assert.Equal(EstadoDeEnvio.PendienteDeGuia, e.Estado));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-12")]
    public async Task Con_la_auditoria_caida_no_se_cancela_ninguno_y_se_responde_servicio_no_disponible()
    {
        var entorno = new EntornoDeEnvios();
        var pedido = Guid.NewGuid();
        var a = Sembrar(entorno, pedido);
        var b = Sembrar(entorno, pedido);
        entorno.Auditoria.Caida = true;

        var resultado = await entorno.Cancelar.Ejecutar(pedido);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.All(entorno.Envios.Todos, e => Assert.Equal(EstadoDeEnvio.PendienteDeGuia, e.Estado));
        Assert.Equal(2, entorno.Envios.Todos.Count(e => e.Id == a.Id || e.Id == b.Id));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-19")]
    public async Task Un_bloqueo_que_no_responde_o_la_base_caida_es_servicio_no_disponible_sin_efectos()
    {
        var entorno = new EntornoDeEnvios();
        var pedido = Guid.NewGuid();
        Sembrar(entorno, pedido);

        entorno.Envios.BloqueoNoDisponible = true;
        var bloqueo = await entorno.Cancelar.Ejecutar(pedido);
        entorno.Envios.BloqueoNoDisponible = false;
        entorno.Envios.NoDisponible = true;
        var caida = await entorno.Cancelar.Ejecutar(pedido);

        Assert.Equal(CategoriaDeError.NoDisponible, bloqueo.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, caida.Error!.Categoria);
        Assert.All(entorno.Envios.Todos, e => Assert.Equal(EstadoDeEnvio.PendienteDeGuia, e.Estado));
    }
}
