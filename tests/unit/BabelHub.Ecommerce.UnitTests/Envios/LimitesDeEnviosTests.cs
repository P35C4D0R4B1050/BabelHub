using BabelHub.Ecommerce.Application.Envios;
using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Envios;

/// <summary>Los límites de consulta y de operación de Envíos y el tope de guías al operador (T09 y T14; SEG-05, SEG-07, SEG-26, plan §6).</summary>
public sealed class LimitesDeEnviosTests
{
    private static Envio SembrarPendiente(EntornoDeEnvios entorno)
    {
        var pedidoId = Guid.NewGuid();
        var envio = Envio.Crear(Guid.NewGuid(), pedidoId, "lima-central", [Guid.NewGuid()], EntornoDeEnvios.AhoraInicial);
        entorno.Envios.Sembrar(envio);
        entorno.Contactos.SembrarDisponible(pedidoId);
        entorno.SembrarPedidoDelEnvio(pedidoId);
        return envio;
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Las_consultas_de_logistica_pasan_por_el_limite_de_consultas_por_usuario_y_el_excedido_es_el_429_uniforme()
    {
        var entorno = new EntornoDeEnvios();
        entorno.Limites.Maximos["consultas-de-envios-por-usuario"] = 2;
        var envio = SembrarPendiente(entorno);

        Assert.True((await entorno.Listar.Ejecutar(new ListarEnviosEntrada())).EsExito);
        Assert.True((await entorno.Obtener.Ejecutar(envio.Id)).EsExito);
        var excedido = await entorno.Listar.Ejecutar(new ListarEnviosEntrada());

        Assert.Equal(CategoriaDeError.Limite, excedido.Error!.Categoria);
        Assert.Equal("limite-excedido", excedido.Error.Clave);
        Assert.Equal("espera-en-segundos", Assert.Single(excedido.Error.Errores).Campo);
        Assert.All(entorno.Limites.Reservas, r => Assert.Equal(((Guid)entorno.Actor.Actual!.UsuarioId!).ToString("N"), r.Sujeto));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task La_consulta_del_titular_tambien_cuenta_contra_el_limite_de_consultas()
    {
        var cuenta = Guid.NewGuid();
        var entorno = new EntornoDeEnvios(ActorDePrueba.DeCliente(cuenta));
        var pedido = new PedidoParaEnvios(Guid.NewGuid(), "PED-2026-0001", cuenta, true, []);
        entorno.Pedidos.Sembrar(pedido);
        entorno.Limites.Maximos["consultas-de-envios-por-usuario"] = 1;

        var primera = await entorno.EnviosDelPedido.Ejecutar(pedido.PedidoId);
        var segunda = await entorno.EnviosDelPedido.Ejecutar(pedido.PedidoId);

        Assert.True(primera.EsExito);
        Assert.Equal(CategoriaDeError.Limite, segunda.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Las_operaciones_pasan_por_el_limite_de_operaciones_por_usuario_antes_de_tocar_nada()
    {
        var entorno = new EntornoDeEnvios();
        entorno.Limites.Maximos["operaciones-de-envios-por-usuario"] = 0;
        var envio = SembrarPendiente(entorno);

        var guia = await entorno.SolicitarGuia.Ejecutar(envio.Id);
        var reparto = await entorno.PasarAEnReparto.Ejecutar(envio.Id);
        var entrega = await entorno.MarcarEntregado.Ejecutar(envio.Id);

        Assert.All([guia, reparto, entrega], r => Assert.Equal(CategoriaDeError.Limite, r.Error!.Categoria));
        Assert.Empty(entorno.Operador.Recibidas);
        Assert.Empty(entorno.Reservas.Llamadas);
        Assert.Equal(0, entorno.Contactos.Lecturas);
        Assert.Null(entorno.Envios.Todos.Single().ClaveDeSolicitud);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public async Task El_tope_de_guias_al_operador_se_reserva_antes_de_fijar_la_clave_y_no_llama_al_operador()
    {
        var entorno = new EntornoDeEnvios();
        entorno.Limites.Maximos["guias-al-operador"] = 1;
        var primero = SembrarPendiente(entorno);
        var segundo = SembrarPendiente(entorno);

        var uno = await entorno.SolicitarGuia.Ejecutar(primero.Id);
        var dos = await entorno.SolicitarGuia.Ejecutar(segundo.Id);

        Assert.True(uno.EsExito);
        Assert.Equal(CategoriaDeError.Limite, dos.Error!.Categoria);
        Assert.Single(entorno.Operador.Recibidas);
        Assert.Null(entorno.Envios.Todos.Single(e => e.Id == segundo.Id).ClaveDeSolicitud);
        Assert.Contains(entorno.Limites.Reservas, r => r.Politica == "guias-al-operador" && r.Sujeto == LimitesDeEnvios.SujetoDelOperador);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Con_el_almacen_de_limites_caido_se_falla_cerrado_con_servicio_no_disponible()
    {
        var entorno = new EntornoDeEnvios();
        entorno.Limites.Caido = true;
        var envio = SembrarPendiente(entorno);

        var lista = await entorno.Listar.Ejecutar(new ListarEnviosEntrada());
        var guia = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.NoDisponible, lista.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, guia.Error!.Categoria);
        Assert.Empty(entorno.Operador.Recibidas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Un_rol_sin_permiso_se_rechaza_antes_de_consumir_el_limite()
    {
        var entorno = new EntornoDeEnvios(ActorDePrueba.DeCliente());
        var envio = SembrarPendiente(entorno);

        await entorno.Listar.Ejecutar(new ListarEnviosEntrada());
        await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.Empty(entorno.Limites.Reservas);
        Assert.Equal(2, entorno.Denegaciones.Eventos.Count);
    }
}
