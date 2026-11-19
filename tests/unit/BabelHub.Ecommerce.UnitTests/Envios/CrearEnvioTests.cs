using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;

namespace BabelHub.Ecommerce.UnitTests.Envios;

/// <summary>Crear el envío al marcar listas las líneas de un pedido a domicilio (T04; ENV-01, ENV-09).</summary>
public sealed class CrearEnvioTests
{
    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task Un_pedido_a_domicilio_con_lineas_listas_de_una_fuente_crea_un_envio_pendiente_de_guia()
    {
        var entorno = new EntornoDeEnvios();
        var a = EntornoDeEnvios.Impresa();
        var b = EntornoDeEnvios.Impresa();
        var pedido = entorno.SembrarPedido(true, a, b);

        var resultado = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [a.LineaId, b.LineaId]));

        Assert.True(resultado.EsExito);
        Assert.False(resultado.Valor.YaExistia);
        Assert.Empty(resultado.Valor.Rechazadas);
        Assert.Equal([a.LineaId, b.LineaId], resultado.Valor.LineaIds);
        var envio = Assert.Single(entorno.Envios.Todos);
        Assert.Equal(resultado.Valor.EnvioId, envio.Id);
        Assert.Equal(EstadoDeEnvio.PendienteDeGuia, envio.Estado);
        Assert.Equal(pedido.PedidoId, envio.PedidoId);
        Assert.Equal("lima-central", envio.CodigoDeFuente);
        Assert.Equal(EntornoDeEnvios.AhoraInicial, envio.CreadoEn);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task Un_pedido_de_dos_fuentes_crea_un_envio_por_llamada_y_las_que_quedan_listas_despues_forman_otro()
    {
        var entorno = new EntornoDeEnvios();
        var lima = EntornoDeEnvios.Impresa("lima-central");
        var cusco = EntornoDeEnvios.Impresa("cusco-norte");
        var rezagada = EntornoDeEnvios.Impresa("lima-central");
        var pedido = entorno.SembrarPedido(true, lima, cusco, rezagada);

        var primero = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [lima.LineaId]));
        var segundo = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "cusco-norte", [cusco.LineaId]));
        var tercero = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [rezagada.LineaId]));

        Assert.All([primero, segundo, tercero], r => Assert.True(r.EsExito));
        Assert.Equal(3, entorno.Envios.Todos.Count);
        Assert.Equal(3, new[] { primero.Valor.EnvioId, segundo.Valor.EnvioId, tercero.Valor.EnvioId }.Distinct().Count());
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task Un_pedido_de_recojo_no_crea_envio()
    {
        var entorno = new EntornoDeEnvios();
        var linea = EntornoDeEnvios.Impresa();
        var pedido = entorno.SembrarPedido(false, linea);

        var resultado = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [linea.LineaId]));

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.ReglaDeNegocio, resultado.Error!.Categoria);
        Assert.Equal("pedido-no-es-a-domicilio", resultado.Error.Clave);
        Assert.Empty(entorno.Envios.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task La_linea_digital_se_rechaza_y_el_envio_se_crea_con_las_impresas()
    {
        var entorno = new EntornoDeEnvios();
        var impresa = EntornoDeEnvios.Impresa();
        var digital = EntornoDeEnvios.Digital();
        var pedido = entorno.SembrarPedido(true, impresa, digital);

        var resultado = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [impresa.LineaId, digital.LineaId]));

        Assert.True(resultado.EsExito);
        Assert.Equal([impresa.LineaId], resultado.Valor.LineaIds);
        var rechazada = Assert.Single(resultado.Valor.Rechazadas);
        Assert.Equal(digital.LineaId, rechazada.LineaId);
        Assert.Equal(MotivoDeLineaRechazadaDelEnvio.Digital, rechazada.Motivo);
        Assert.Equal([impresa.LineaId], Assert.Single(entorno.Envios.Todos).Lineas.Select(l => l.LineaId));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task Sin_lineas_impresas_no_se_crea_nada()
    {
        var entorno = new EntornoDeEnvios();
        var digital = EntornoDeEnvios.Digital();
        var pedido = entorno.SembrarPedido(true, digital);

        var resultado = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [digital.LineaId]));

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.ReglaDeNegocio, resultado.Error!.Categoria);
        Assert.Equal("linea-digital", resultado.Error.Clave);
        Assert.Empty(entorno.Envios.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task Una_linea_que_no_es_del_pedido_no_esta_lista_o_es_de_otra_fuente_rechaza_todo_el_envio()
    {
        var entorno = new EntornoDeEnvios();
        var buena = EntornoDeEnvios.Impresa();
        var sinLista = EntornoDeEnvios.Impresa(lista: false);
        var deOtraFuente = EntornoDeEnvios.Impresa("cusco-norte");
        var pedido = entorno.SembrarPedido(true, buena, sinLista, deOtraFuente);

        var ajena = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [buena.LineaId, Guid.NewGuid()]));
        var noLista = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [buena.LineaId, sinLista.LineaId]));
        var otraFuente = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [buena.LineaId, deOtraFuente.LineaId]));

        Assert.Equal("linea-no-es-del-pedido", ajena.Error!.Clave);
        Assert.Equal("linea-no-esta-lista", noLista.Error!.Clave);
        Assert.Equal("linea-de-otra-fuente", otraFuente.Error!.Clave);
        Assert.All([ajena, noLista, otraFuente], r => Assert.Equal(CategoriaDeError.ReglaDeNegocio, r.Error!.Categoria));
        Assert.Empty(entorno.Envios.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task Repetir_la_llamada_con_las_mismas_lineas_devuelve_el_mismo_envio_sin_crear_otro()
    {
        var entorno = new EntornoDeEnvios();
        var a = EntornoDeEnvios.Impresa();
        var b = EntornoDeEnvios.Impresa();
        var pedido = entorno.SembrarPedido(true, a, b);
        var entrada = new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [a.LineaId, b.LineaId]);

        var primera = await entorno.Crear.Ejecutar(entrada);
        var repeticion = await entorno.Crear.Ejecutar(entrada with { LineaIds = [b.LineaId, a.LineaId] });

        Assert.True(repeticion.EsExito);
        Assert.True(repeticion.Valor.YaExistia);
        Assert.Equal(primera.Valor.EnvioId, repeticion.Valor.EnvioId);
        Assert.Single(entorno.Envios.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task Un_solape_parcial_con_otro_envio_se_rechaza_completo()
    {
        var entorno = new EntornoDeEnvios();
        var a = EntornoDeEnvios.Impresa();
        var b = EntornoDeEnvios.Impresa();
        var c = EntornoDeEnvios.Impresa();
        var pedido = entorno.SembrarPedido(true, a, b, c);
        await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [a.LineaId, b.LineaId]));

        var solape = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [b.LineaId, c.LineaId]));
        var subconjunto = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [a.LineaId]));

        Assert.False(solape.EsExito);
        Assert.Equal(CategoriaDeError.Conflicto, solape.Error!.Categoria);
        Assert.Equal("lineas-en-otro-envio", solape.Error.Clave);
        Assert.Equal("lineas-en-otro-envio", subconjunto.Error!.Clave);
        Assert.Single(entorno.Envios.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task Una_carrera_con_otro_envio_que_gana_el_unico_por_linea_es_un_conflicto_y_no_un_error_5xx()
    {
        var entorno = new EntornoDeEnvios();
        var a = EntornoDeEnvios.Impresa();
        var pedido = entorno.SembrarPedido(true, a);
        // Otra transacción ya creó el envío después de la búsqueda: se simula sembrándolo cuando el repositorio lo busca.
        entorno.Envios.DespuesDeBuscarPorLineas = () => entorno.Envios.Sembrar(Envio.Crear(Guid.NewGuid(), pedido.PedidoId, "lima-central", [a.LineaId, Guid.NewGuid()], EntornoDeEnvios.AhoraInicial));

        var resultado = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [a.LineaId]));

        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task La_lectura_del_pedido_ocurre_en_la_unidad_de_trabajo_del_llamador_y_sin_abrir_otra()
    {
        var entorno = new EntornoDeEnvios();
        var linea = EntornoDeEnvios.Impresa();
        var pedido = entorno.SembrarPedido(true, linea);

        // Pedidos abre su transacción (con el pedido ya bloqueado), marca listas las líneas y llama a Crear dentro de ella.
        var resultado = await entorno.Unidad.Ejecutar(token => entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [linea.LineaId]), token));

        Assert.True(resultado.EsExito);
        Assert.Equal(1, entorno.Pedidos.LecturasDentroDeLaTransaccion);
        Assert.Equal(1, entorno.Unidad.Ejecuciones); // Crear no abrió una transacción propia
        Assert.Equal(0, entorno.Envios.LecturasConBloqueo); // y no tomó ningún bloqueo: el pedido ya está bloqueado
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task Un_pedido_inexistente_o_con_pedidos_no_disponible_o_la_base_caida_falla_sin_crear_nada()
    {
        var entorno = new EntornoDeEnvios();
        var linea = EntornoDeEnvios.Impresa();
        var pedido = entorno.SembrarPedido(true, linea);

        var inexistente = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(Guid.NewGuid(), "lima-central", [linea.LineaId]));
        entorno.Pedidos.NoDisponible = true;
        var pedidosCaido = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [linea.LineaId]));
        entorno.Pedidos.NoDisponible = false;
        entorno.Envios.NoDisponible = true;
        var baseCaida = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [linea.LineaId]));

        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, pedidosCaido.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, baseCaida.Error!.Categoria);
        Assert.Empty(entorno.Envios.Todos);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-01")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Una_entrada_sin_fuente_o_sin_lineas_o_con_lineas_repetidas_es_una_validacion_fallida(string fuente)
    {
        var entorno = new EntornoDeEnvios();
        var linea = EntornoDeEnvios.Impresa();
        var pedido = entorno.SembrarPedido(true, linea);

        var sinFuente = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, fuente, [linea.LineaId]));
        var sinLineas = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", []));
        var repetidas = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [linea.LineaId, linea.LineaId]));

        Assert.All([sinFuente, sinLineas, repetidas], r => Assert.Equal(CategoriaDeError.Validacion, r.Error!.Categoria));
        Assert.Empty(entorno.Envios.Todos);
        Assert.Equal(0, entorno.Pedidos.Lecturas); // la validación rechaza antes de leer
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task Repetir_la_llamada_cuando_las_lineas_ya_avanzaron_de_estado_devuelve_el_envio_existente()
    {
        var entorno = new EntornoDeEnvios();
        var a = EntornoDeEnvios.Impresa();
        var pedido = entorno.SembrarPedido(true, a);
        var entrada = new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [a.LineaId]);
        var primera = await entorno.Crear.Ejecutar(entrada);

        // Pedidos ya despachó la línea: deja de figurar como «lista», pero la repetición sigue siendo idempotente.
        entorno.Pedidos.Sembrar(pedido with { Lineas = [a with { EstaLista = false }] });
        var repeticion = await entorno.Crear.Ejecutar(entrada);

        Assert.True(repeticion.EsExito);
        Assert.True(repeticion.Valor.YaExistia);
        Assert.Equal(primera.Valor.EnvioId, repeticion.Valor.EnvioId);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task Una_linea_marcada_lista_dentro_de_la_misma_transaccion_se_ve_al_crear_el_envio()
    {
        var entorno = new EntornoDeEnvios();
        var linea = EntornoDeEnvios.Impresa(lista: false);
        var pedido = entorno.SembrarPedido(true, linea);

        var resultado = await entorno.Unidad.Ejecutar(async token =>
        {
            // Pedidos marca lista la línea y llama a Crear dentro de la misma transacción.
            entorno.Pedidos.Sembrar(pedido with { Lineas = [linea with { EstaLista = true }] });
            return await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, "lima-central", [linea.LineaId]), token);
        });

        Assert.True(resultado.EsExito);
        Assert.Equal(1, entorno.Pedidos.LecturasDentroDeLaTransaccion);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task Una_fuente_de_mas_de_64_caracteres_es_una_validacion_fallida_y_no_una_excepcion()
    {
        var entorno = new EntornoDeEnvios();
        var linea = EntornoDeEnvios.Impresa();
        var pedido = entorno.SembrarPedido(true, linea);

        var resultado = await entorno.Crear.Ejecutar(new CrearEnvioEntrada(pedido.PedidoId, new string('f', 65), [linea.LineaId]));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
    }
}
