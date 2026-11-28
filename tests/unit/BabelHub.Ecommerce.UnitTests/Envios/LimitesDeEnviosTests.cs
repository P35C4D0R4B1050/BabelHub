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
        Assert.Equal(["espera-en-segundos", "motivo", "disponibleDesde"], excedido.Error.Errores.Select(e => e.Campo));
        Assert.Equal("consultas-de-envios-por-usuario", excedido.Error.Errores.Single(e => e.Campo == "motivo").Clave);
        Assert.All(entorno.Limites.Reservas, r => Assert.Equal(((Guid)entorno.Actor.Actual!.UsuarioId!).ToString("N"), r.Sujeto));
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
        Assert.Equal(1, entorno.Contactos.Lecturas);
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
}
