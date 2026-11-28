using BabelHub.Ecommerce.Application.Envios;
using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Envios;

/// <summary>Las consultas de logística y del titular, y el orden rol → alcance → estado (T09; ENV-05, ENV-08).</summary>
public sealed class ConsultasDeEnviosTests
{
    private static Envio Sembrar(EntornoDeEnvios entorno, Guid pedidoId, DateTimeOffset? creado = null, bool conGuia = false)
    {
        var envio = Envio.Crear(Guid.NewGuid(), pedidoId, "lima-central", [Guid.NewGuid()], creado ?? EntornoDeEnvios.AhoraInicial);
        if (conGuia)
        {
            envio.TomarSolicitudDeGuia(EntornoDeEnvios.AhoraInicial, Guid.NewGuid());
            envio.RegistrarGuia(NumeroDeGuia.Crear("GUIA-000123"), "operador-a", EntornoDeEnvios.AhoraInicial);
        }

        entorno.Envios.Sembrar(envio);
        return envio;
    }

    private static PedidoParaEnvios SembrarPedidoDe(EntornoDeEnvios entorno, Guid cuentaId)
    {
        var pedido = new PedidoParaEnvios(Guid.NewGuid(), "PED-2026-0001", cuentaId, true, [new LineaDePedidoParaEnvios(Guid.NewGuid(), false, "lima-central", true)]);
        entorno.Pedidos.Sembrar(pedido);
        return pedido;
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Logistica_lista_los_envios_por_estado_con_pagina_topada_y_sin_datos_de_contacto()
    {
        var entorno = new EntornoDeEnvios();
        for (var i = 0; i < 60; i++)
        {
            Sembrar(entorno, Guid.NewGuid(), EntornoDeEnvios.AhoraInicial.AddMinutes(i));
        }

        Sembrar(entorno, Guid.NewGuid(), conGuia: true);

        var primera = await entorno.Listar.Ejecutar(new ListarEnviosEntrada(null, 1, 500));
        var conGuia = await entorno.Listar.Ejecutar(new ListarEnviosEntrada(EstadoDelEnvio.GuiaGenerada));
        var pendientes = await entorno.Listar.Ejecutar(new ListarEnviosEntrada(EstadoDelEnvio.PendienteDeGuia, 2, 20));

        Assert.Equal(50, primera.Valor.Tamano);
        Assert.Equal(50, primera.Valor.Envios.Count);
        Assert.Equal(61, primera.Valor.Total);
        var fila = Assert.Single(conGuia.Valor.Envios);
        Assert.Equal("GUIA-000123", fila.NumeroDeGuia);
        Assert.Equal("operador-a", fila.Operador);
        Assert.Equal(60, pendientes.Valor.Total);
        Assert.Equal(20, pendientes.Valor.Envios.Count);
        Assert.Equal(2, pendientes.Valor.Pagina);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Logistica_y_la_administracion_central_obtienen_un_envio_y_uno_inexistente_es_no_encontrado_sin_evento()
    {
        var entorno = new EntornoDeEnvios(ActorDePrueba.DeAdministradorCentral());
        var envio = Sembrar(entorno, Guid.NewGuid(), conGuia: true);

        var existente = await entorno.Obtener.Ejecutar(envio.Id);
        var inexistente = await entorno.Obtener.Ejecutar(Guid.NewGuid());

        Assert.Equal("GUIA-000123", existente.Valor.NumeroDeGuia);
        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
        Assert.Empty(entorno.Denegaciones.Eventos);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-08")]
    [InlineData("cliente")]
    [InlineData("encargado")]
    [InlineData("anonimo")]
    public async Task Quien_no_es_de_logistica_ni_de_la_administracion_central_no_lista_ni_obtiene_envios(string quien)
    {
        var actor = quien switch
        {
            "cliente" => ActorDePrueba.DeCliente(),
            "encargado" => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
            _ => ActorDePrueba.Anonimo(),
        };
        var entorno = new EntornoDeEnvios(actor);
        var envio = Sembrar(entorno, Guid.NewGuid());

        var lista = await entorno.Listar.Ejecutar(new ListarEnviosEntrada());
        var detalle = await entorno.Obtener.Ejecutar(envio.Id);

        var esperada = quien == "anonimo" ? CategoriaDeError.NoAutenticado : CategoriaDeError.Prohibido;
        Assert.Equal(esperada, lista.Error!.Categoria);
        Assert.Equal(esperada, detalle.Error!.Categoria);
        Assert.Equal(2, entorno.Denegaciones.Eventos.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-05")]
    public async Task El_titular_ve_el_estado_y_la_guia_de_los_envios_de_su_pedido()
    {
        var cuenta = Guid.NewGuid();
        var entorno = new EntornoDeEnvios(ActorDePrueba.DeCliente(cuenta));
        var pedido = SembrarPedidoDe(entorno, cuenta);
        Sembrar(entorno, pedido.PedidoId, conGuia: true);
        Sembrar(entorno, pedido.PedidoId);

        var resultado = await entorno.EnviosDelPedido.Ejecutar(pedido.PedidoId);

        Assert.True(resultado.EsExito);
        Assert.Equal(2, resultado.Valor.Envios.Count);
        Assert.Contains(resultado.Valor.Envios, e => e.Estado == EstadoDelEnvio.GuiaGenerada && e.NumeroDeGuia == "GUIA-000123");
        Assert.Contains(resultado.Valor.Envios, e => e.Estado == EstadoDelEnvio.PendienteDeGuia && e.NumeroDeGuia is null);
        Assert.Empty(entorno.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-05")]
    public async Task Un_pedido_ajeno_y_uno_inexistente_responden_exactamente_igual_con_un_acceso_denegado_por_cada_intento()
    {
        var entorno = new EntornoDeEnvios(ActorDePrueba.DeCliente());
        var ajeno = SembrarPedidoDe(entorno, Guid.NewGuid());
        Sembrar(entorno, ajeno.PedidoId, conGuia: true);

        var deOtro = await entorno.EnviosDelPedido.Ejecutar(ajeno.PedidoId);
        var inexistente = await entorno.EnviosDelPedido.Ejecutar(Guid.NewGuid());

        Assert.Equal(CategoriaDeError.NoEncontrado, deOtro.Error!.Categoria);
        Assert.Equal(inexistente.Error!.Categoria, deOtro.Error.Categoria);
        Assert.Equal(inexistente.Error.Clave, deOtro.Error.Clave);
        Assert.Equal(2, entorno.Denegaciones.Eventos.Count);
        Assert.All(entorno.Denegaciones.Eventos, e => Assert.Equal("ECO-NO-ENCONTRADO", e.CodigoDeError));
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-05")]
    [InlineData("encargado")]
    [InlineData("anonimo")]
    public async Task Un_encargado_o_un_visitante_no_consultan_los_envios_de_un_pedido(string quien)
    {
        var actor = quien == "encargado" ? ActorDePrueba.DeEncargadoDe(Guid.NewGuid()) : ActorDePrueba.Anonimo();
        var entorno = new EntornoDeEnvios(actor);
        var pedido = SembrarPedidoDe(entorno, Guid.NewGuid());

        var resultado = await entorno.EnviosDelPedido.Ejecutar(pedido.PedidoId);

        Assert.Equal(quien == "anonimo" ? CategoriaDeError.NoAutenticado : CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Single(entorno.Denegaciones.Eventos);
        Assert.Equal(0, entorno.Pedidos.Lecturas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-05")]
    public async Task Con_pedidos_o_la_base_caidos_es_servicio_no_disponible_y_no_un_no_encontrado()
    {
        var cuenta = Guid.NewGuid();
        var entorno = new EntornoDeEnvios(ActorDePrueba.DeCliente(cuenta));
        var pedido = SembrarPedidoDe(entorno, cuenta);

        entorno.Pedidos.NoDisponible = true;
        var pedidosCaido = await entorno.EnviosDelPedido.Ejecutar(pedido.PedidoId);
        entorno.Pedidos.NoDisponible = false;
        entorno.Envios.NoDisponible = true;
        var baseCaida = await entorno.EnviosDelPedido.Ejecutar(pedido.PedidoId);

        Assert.Equal(CategoriaDeError.NoDisponible, pedidosCaido.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, baseCaida.Error!.Categoria);
        Assert.Empty(entorno.Denegaciones.Eventos);
    }
}
