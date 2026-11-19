using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Envios;

/// <summary>Marcar un envío entregado y avisar el progreso a Pedidos en orden (T08; ENV-07, ENV-14).</summary>
public sealed class MarcarEntregadoTests
{
    private static Envio SembrarEnReparto(EntornoDeEnvios entorno, bool repartoInformado = true)
    {
        var envio = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "lima-central", [Guid.NewGuid()], EntornoDeEnvios.AhoraInicial);
        envio.TomarSolicitudDeGuia(EntornoDeEnvios.AhoraInicial, Guid.NewGuid());
        envio.RegistrarGuia(NumeroDeGuia.Crear("GUIA-000123"), "operador-a", EntornoDeEnvios.AhoraInicial);
        envio.IniciarDespacho(EntornoDeEnvios.AhoraInicial);
        envio.PasarAEnReparto(EntornoDeEnvios.AhoraInicial);
        if (repartoInformado)
        {
            envio.RegistrarProgresoEnRepartoInformado(EntornoDeEnvios.AhoraInicial);
        }

        entorno.Envios.Sembrar(envio);
        return envio;
    }

    private static Envio Guardado(EntornoDeEnvios entorno, Guid id) => entorno.Envios.Todos.Single(e => e.Id == id);

    [Fact]
    [Trait("Criterio", "AC-ENV-13")]
    public async Task Logistica_marca_entregado_con_la_fecha_y_una_entrada_de_auditoria_y_pedidos_marca_las_lineas_entregadas()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarEnReparto(entorno);

        var resultado = await entorno.MarcarEntregado.Ejecutar(envio.Id);

        Assert.True(resultado.EsExito);
        Assert.Equal(BabelHub.Ecommerce.Application.Envios.EstadoDelEnvio.Entregado, resultado.Valor.Estado);
        Assert.Equal(EntornoDeEnvios.AhoraInicial, resultado.Valor.EntregadoEn);
        var guardado = Guardado(entorno, envio.Id);
        Assert.Equal(EstadoDeEnvio.Entregado, guardado.Estado);
        Assert.NotNull(guardado.ProgresoEntregadoInformadoEn);

        var auditoria = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.EstadoDeEnvioCambiado, auditoria.Accion);
        Assert.Equal("en_reparto", auditoria.Detalle["estadoAnterior"]);
        Assert.Equal("entregado", auditoria.Detalle["estadoNuevo"]);
        Assert.Equal("2026-11-20T15:00:00Z", auditoria.Detalle["fecha"]);
        Assert.Equal("GUIA-000123", auditoria.Detalle["numeroDeGuia"]);
        Assert.Equal("operador-a", auditoria.Detalle["operador"]);
        Assert.Equal(envio.PedidoId.ToString(), auditoria.Detalle["pedidoId"]);

        var llamada = Assert.Single(entorno.Progreso.Llamadas);
        Assert.Equal(EntornoDeEnvios.AhoraInicial, llamada.Entregado!.Fecha);
        Assert.Equal(envio.PedidoId, llamada.Entregado.PedidoId);
        Assert.Equal(EstadoDeEnvio.Entregado, llamada.EstadoGuardado); // después de confirmar
        Assert.Equal(0, entorno.Progreso.LlamadasDentroDeLaTransaccion);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-13")]
    public async Task Solo_un_envio_en_reparto_se_marca_entregado()
    {
        var entorno = new EntornoDeEnvios();
        var pendiente = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "lima-central", [Guid.NewGuid()], EntornoDeEnvios.AhoraInicial);
        entorno.Envios.Sembrar(pendiente);
        var entregado = SembrarEnReparto(entorno);
        await entorno.MarcarEntregado.Ejecutar(entregado.Id);
        var auditorias = entorno.Auditoria.Solicitudes.Count;

        var sinReparto = await entorno.MarcarEntregado.Ejecutar(pendiente.Id);
        var repetido = await entorno.MarcarEntregado.Ejecutar(entregado.Id);

        Assert.Equal("estado-invalido", sinReparto.Error!.Clave);
        Assert.Equal("estado-invalido", repetido.Error!.Clave);
        Assert.Equal(auditorias, entorno.Auditoria.Solicitudes.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-13")]
    public async Task Con_la_auditoria_caida_no_se_marca_entregado()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarEnReparto(entorno);
        entorno.Auditoria.Caida = true;

        var resultado = await entorno.MarcarEntregado.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(EstadoDeEnvio.EnReparto, Guardado(entorno, envio.Id).Estado);
        Assert.Empty(entorno.Progreso.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-07")]
    public async Task La_entrega_no_se_informa_hasta_que_el_reparto_este_informado_y_el_trabajo_los_reintenta_en_orden()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarEnReparto(entorno, repartoInformado: false);
        entorno.Progreso.Falla = true;

        var resultado = await entorno.MarcarEntregado.Ejecutar(envio.Id);

        // La entrega se marcó, pero Pedidos está caído: solo se intentó el reparto y la entrega no se llamó.
        Assert.True(resultado.EsExito);
        Assert.Single(entorno.Progreso.Llamadas);
        Assert.NotNull(entorno.Progreso.Llamadas[0].EnReparto);
        Assert.Null(Guardado(entorno, envio.Id).ProgresoEnRepartoInformadoEn);

        entorno.Progreso.Falla = false;
        var corrida = await entorno.InformarProgreso.Ejecutar(10);

        Assert.True(corrida.EsExito);
        Assert.Equal(1, corrida.Valor);
        Assert.Equal(3, entorno.Progreso.Llamadas.Count);
        Assert.NotNull(entorno.Progreso.Llamadas[1].EnReparto); // primero el reparto
        Assert.NotNull(entorno.Progreso.Llamadas[2].Entregado); // después la entrega
        var guardado = Guardado(entorno, envio.Id);
        Assert.NotNull(guardado.ProgresoEnRepartoInformadoEn);
        Assert.NotNull(guardado.ProgresoEntregadoInformadoEn);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-07")]
    public async Task El_trabajo_es_idempotente_y_no_hace_nada_si_todo_esta_informado()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarEnReparto(entorno);
        await entorno.MarcarEntregado.Ejecutar(envio.Id);
        var llamadas = entorno.Progreso.Llamadas.Count;

        var corrida = await entorno.InformarProgreso.Ejecutar(10);

        Assert.True(corrida.EsExito);
        Assert.Equal(0, corrida.Valor);
        Assert.Equal(llamadas, entorno.Progreso.Llamadas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-07")]
    public async Task Lo_que_lleva_mas_de_una_hora_sin_informarse_publica_una_alerta_tecnica_sin_datos()
    {
        var entorno = new EntornoDeEnvios();
        SembrarEnReparto(entorno, repartoInformado: false);
        entorno.Progreso.Falla = true;

        await entorno.InformarProgreso.Ejecutar(10);
        Assert.Empty(entorno.Alertas.Alertas);

        entorno.Reloj.Ahora += TimeSpan.FromMinutes(61);
        await entorno.InformarProgreso.Ejecutar(10);

        var alerta = Assert.Single(entorno.Alertas.Alertas);
        Assert.Equal("envio-progreso-sin-informar", alerta.Motivo);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-07")]
    [InlineData("cliente")]
    [InlineData("logistica")]
    [InlineData("anonimo")]
    public async Task Solo_el_actor_sistema_corre_el_trabajo_de_informar_el_progreso(string quien)
    {
        var actor = quien switch
        {
            "cliente" => ActorDePrueba.DeCliente(),
            "logistica" => ActorDePrueba.DePersonalDeLogistica(),
            _ => ActorDePrueba.Anonimo(),
        };
        var entorno = new EntornoDeEnvios(actor);
        var informar = new BabelHub.Ecommerce.Application.Envios.Interno.InformarProgresoAPedidos(actor, entorno.Envios, entorno.Informador, entorno.Alertas, entorno.Reloj);

        var resultado = await informar.Ejecutar(10);

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-08")]
    [InlineData("cliente")]
    [InlineData("encargado")]
    [InlineData("anonimo")]
    public async Task Quien_no_es_de_logistica_ni_de_la_administracion_central_recibe_prohibido_con_un_acceso_denegado(string quien)
    {
        var actor = quien switch
        {
            "cliente" => ActorDePrueba.DeCliente(),
            "encargado" => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
            _ => ActorDePrueba.Anonimo(),
        };
        var entorno = new EntornoDeEnvios(actor);
        var envio = SembrarEnReparto(entorno);

        var resultado = await entorno.MarcarEntregado.Ejecutar(envio.Id);

        Assert.Equal(quien == "anonimo" ? CategoriaDeError.NoAutenticado : CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Single(entorno.Denegaciones.Eventos);
        Assert.Equal(EstadoDeEnvio.EnReparto, Guardado(entorno, envio.Id).Estado);
    }
}
