using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Envios;

/// <summary>Pasar un envío a en reparto y descontar el físico del Inventario (T07; ENV-11, ENV-12, ENV-19).</summary>
public sealed class PasarAEnRepartoTests
{
    private static Envio SembrarConGuia(EntornoDeEnvios entorno, Action<Envio>? preparar = null)
    {
        var envio = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "lima-central", [Guid.NewGuid(), Guid.NewGuid()], EntornoDeEnvios.AhoraInicial);
        envio.TomarSolicitudDeGuia(EntornoDeEnvios.AhoraInicial, Guid.NewGuid());
        envio.RegistrarGuia(NumeroDeGuia.Crear("GUIA-000123"), "operador-a", EntornoDeEnvios.AhoraInicial);
        preparar?.Invoke(envio);
        entorno.Envios.Sembrar(envio);
        entorno.SembrarPedidoDelEnvio(envio.PedidoId);
        return envio;
    }

    private static Envio Guardado(EntornoDeEnvios entorno, Guid id) => entorno.Envios.Todos.Single(e => e.Id == id);

    [Fact]
    [Trait("Criterio", "AC-ENV-10")]
    public async Task Logistica_pasa_a_en_reparto_el_inventario_descuenta_todas_las_lineas_en_una_llamada_y_pedidos_las_marca_despachadas()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarConGuia(entorno);

        var resultado = await entorno.PasarAEnReparto.Ejecutar(envio.Id);

        Assert.True(resultado.EsExito);
        Assert.Equal(BabelHub.Ecommerce.Application.Envios.EstadoDelEnvio.EnReparto, resultado.Valor.Estado);
        var guardado = Guardado(entorno, envio.Id);
        Assert.Equal(EstadoDeEnvio.EnReparto, guardado.Estado);
        Assert.NotNull(guardado.ProgresoEnRepartoInformadoEn);

        var llamada = Assert.Single(entorno.Reservas.Llamadas);
        Assert.Equal(envio.Id, llamada.EnvioId);
        Assert.Equal(envio.Lineas.Select(l => l.LineaId).Order(), llamada.Lineas.Order());
        Assert.Equal(1, entorno.Reservas.EnviosDescontados);

        var progreso = Assert.Single(entorno.Progreso.Llamadas);
        Assert.Equal(envio.PedidoId, progreso.EnReparto!.PedidoId);
        Assert.Equal(envio.Id, progreso.EnReparto.EnvioId);

        var auditoria = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal("guia_generada", auditoria.Detalle["estadoAnterior"]);
        Assert.Equal("en_reparto", auditoria.Detalle["estadoNuevo"]);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-16")]
    public async Task Ninguna_llamada_de_red_ocurre_dentro_de_una_transaccion_y_pedidos_se_informa_solo_despues_de_confirmar()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarConGuia(entorno);

        await entorno.PasarAEnReparto.Ejecutar(envio.Id);

        Assert.Equal(0, entorno.Reservas.LlamadasDentroDeLaTransaccion);
        Assert.Equal(0, entorno.Progreso.LlamadasDentroDeLaTransaccion);
        Assert.Equal(EstadoDeEnvio.EnReparto, Assert.Single(entorno.Progreso.Llamadas).EstadoGuardado);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-16")]
    public async Task Una_segunda_solicitud_con_el_marcador_vivo_se_rechaza_por_estado_invalido_sin_llamar_al_inventario()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarConGuia(entorno, e => e.IniciarDespacho(EntornoDeEnvios.AhoraInicial));

        var resultado = await entorno.PasarAEnReparto.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
        Assert.Equal("estado-invalido", resultado.Error.Clave);
        Assert.Empty(entorno.Reservas.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-03")]
    public async Task Un_envio_que_no_tiene_guia_o_ya_esta_en_reparto_se_rechaza_por_estado_invalido()
    {
        var entorno = new EntornoDeEnvios();
        var pendiente = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "lima-central", [Guid.NewGuid()], EntornoDeEnvios.AhoraInicial);
        entorno.Envios.Sembrar(pendiente);
        var enReparto = SembrarConGuia(entorno, e =>
        {
            e.IniciarDespacho(EntornoDeEnvios.AhoraInicial);
            e.PasarAEnReparto(EntornoDeEnvios.AhoraInicial);
        });

        var sinGuia = await entorno.PasarAEnReparto.Ejecutar(pendiente.Id);
        var repetido = await entorno.PasarAEnReparto.Ejecutar(enReparto.Id);

        Assert.Equal("estado-invalido", sinGuia.Error!.Clave);
        Assert.Equal("estado-invalido", repetido.Error!.Clave);
        Assert.Empty(entorno.Reservas.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-11")]
    public async Task Si_el_inventario_no_responde_el_envio_sigue_en_guia_generada_sin_descontar_y_el_reintento_descuenta_una_sola_vez()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarConGuia(entorno);
        entorno.Reservas.Encolar(new ResultadoDeCierreDeReservas.Incierto());

        var fallida = await entorno.PasarAEnReparto.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.NoDisponible, fallida.Error!.Categoria);
        var tras = Guardado(entorno, envio.Id);
        Assert.Equal(EstadoDeEnvio.GuiaGenerada, tras.Estado);
        Assert.NotNull(tras.DespachoIniciadoEn); // el marcador se conserva ante un resultado incierto
        Assert.NotNull(tras.DespachoIncertoEn);
        Assert.Null(tras.DespachoEnCursoHasta); // el arrendamiento se liberó
        Assert.Equal(0, entorno.Reservas.EnviosDescontados);
        Assert.Empty(entorno.Progreso.Llamadas);
        Assert.Empty(entorno.Auditoria.Solicitudes);

        var reintento = await entorno.PasarAEnReparto.Ejecutar(envio.Id);

        Assert.True(reintento.EsExito);
        Assert.Equal(1, entorno.Reservas.EnviosDescontados);
        Assert.Equal(2, entorno.Reservas.Llamadas.Count);
        Assert.Equal(envio.Id, entorno.Reservas.Llamadas[1].EnvioId); // la misma clave natural
        Assert.Single(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-11")]
    public async Task Un_rechazo_de_negocio_sin_intento_incierto_limpia_el_marcador_y_el_envio_vuelve_a_ser_cancelable()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarConGuia(entorno);
        entorno.Reservas.Encolar(new ResultadoDeCierreDeReservas.RechazoDeNegocio());

        var resultado = await entorno.PasarAEnReparto.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
        Assert.Equal("inventario-rechazo", resultado.Error.Clave);
        var tras = Guardado(entorno, envio.Id);
        Assert.Null(tras.DespachoIniciadoEn);
        Assert.Null(tras.DespachoEnCursoHasta);
        Assert.Equal(0, entorno.Reservas.EnviosDescontados);

        var cancelado = await entorno.Cancelar.Ejecutar(envio.PedidoId);
        Assert.Equal(DestinoDelEnvioAlCancelar.Cancelado, Assert.Single(cancelado.Valor).Destino);
        Assert.Empty(entorno.Alertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-19")]
    public async Task Un_rechazo_de_negocio_tras_un_intento_incierto_conserva_el_marcador_y_publica_una_alerta_y_cancelar_mantiene()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarConGuia(entorno);
        entorno.Reservas.Encolar(new ResultadoDeCierreDeReservas.Incierto());
        entorno.Reservas.Encolar(new ResultadoDeCierreDeReservas.RechazoDeNegocio());

        await entorno.PasarAEnReparto.Ejecutar(envio.Id);
        var segundo = await entorno.PasarAEnReparto.Ejecutar(envio.Id);

        Assert.Equal("inventario-rechazo", segundo.Error!.Clave);
        Assert.NotNull(Guardado(entorno, envio.Id).DespachoIniciadoEn);
        Assert.Single(entorno.Alertas.Alertas);

        var cancelado = await entorno.Cancelar.Ejecutar(envio.PedidoId);
        Assert.Equal(DestinoDelEnvioAlCancelar.Mantenido, Assert.Single(cancelado.Valor).Destino);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-19")]
    public async Task Un_cierre_lento_que_el_reintento_ve_en_curso_deja_el_marcador_y_cancelar_mantiene()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarConGuia(entorno);
        // El primer cierre pudo seguir ejecutándose en el Inventario: el reintento recibe «en curso» (incierto).
        entorno.Reservas.Encolar(new ResultadoDeCierreDeReservas.Incierto());
        entorno.Reservas.Encolar(new ResultadoDeCierreDeReservas.Incierto());

        await entorno.PasarAEnReparto.Ejecutar(envio.Id);
        await entorno.PasarAEnReparto.Ejecutar(envio.Id);
        var cancelado = await entorno.Cancelar.Ejecutar(envio.PedidoId);

        Assert.Equal(DestinoDelEnvioAlCancelar.Mantenido, Assert.Single(cancelado.Valor).Destino);
        Assert.Equal(EstadoDeEnvio.GuiaGenerada, Guardado(entorno, envio.Id).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-19")]
    public async Task Si_la_cancelacion_confirma_primero_el_paso_a_reparto_se_rechaza_sin_tocar_el_inventario()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarConGuia(entorno);
        await entorno.Cancelar.Ejecutar(envio.PedidoId);

        var resultado = await entorno.PasarAEnReparto.Ejecutar(envio.Id);

        Assert.Equal("estado-invalido", resultado.Error!.Clave);
        Assert.Empty(entorno.Reservas.Llamadas);
        Assert.Equal(EstadoDeEnvio.Cancelado, Guardado(entorno, envio.Id).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-19")]
    public async Task Si_el_paso_a_reparto_confirma_el_marcador_primero_cancelar_mantiene_el_envio()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarConGuia(entorno);
        Resultado<IReadOnlyList<ResultadoDeCancelarEnvio>>? cancelacion = null;
        entorno.Reservas.AlCerrar = () => cancelacion = entorno.Cancelar.Ejecutar(envio.PedidoId).GetAwaiter().GetResult();

        var resultado = await entorno.PasarAEnReparto.Ejecutar(envio.Id);

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDeEnvio.EnReparto, Guardado(entorno, envio.Id).Estado);
        Assert.Equal(1, entorno.Reservas.EnviosDescontados);
        Assert.Equal(DestinoDelEnvioAlCancelar.Mantenido, Assert.Single(cancelacion!.Valor).Destino);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-16")]
    public async Task Si_falla_la_auditoria_de_la_tercera_transaccion_el_marcador_sobrevive_y_el_reintento_repite_el_cierre_idempotente()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarConGuia(entorno);
        entorno.Auditoria.Caida = true;

        var fallida = await entorno.PasarAEnReparto.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.NoDisponible, fallida.Error!.Categoria);
        var tras = Guardado(entorno, envio.Id);
        Assert.Equal(EstadoDeEnvio.GuiaGenerada, tras.Estado);
        Assert.NotNull(tras.DespachoIniciadoEn);
        Assert.Null(tras.DespachoEnCursoHasta);
        Assert.Equal(1, entorno.Reservas.EnviosDescontados);
        Assert.Empty(entorno.Progreso.Llamadas);

        entorno.Auditoria.Caida = false;
        var reintento = await entorno.PasarAEnReparto.Ejecutar(envio.Id);

        Assert.True(reintento.EsExito);
        Assert.Equal(1, entorno.Reservas.EnviosDescontados); // el físico se descuenta una sola vez
        Assert.Equal(2, entorno.Reservas.Llamadas.Count);
        Assert.Single(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-10")]
    public async Task Con_pedidos_caido_el_envio_igual_pasa_a_reparto_y_queda_pendiente_informar()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarConGuia(entorno);
        entorno.Progreso.Falla = true;

        var resultado = await entorno.PasarAEnReparto.Ejecutar(envio.Id);

        Assert.True(resultado.EsExito);
        var guardado = Guardado(entorno, envio.Id);
        Assert.Equal(EstadoDeEnvio.EnReparto, guardado.Estado);
        Assert.Null(guardado.ProgresoEnRepartoInformadoEn); // un trabajo lo reintenta
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
        var envio = SembrarConGuia(entorno);

        var resultado = await entorno.PasarAEnReparto.Ejecutar(envio.Id);

        Assert.Equal(quien == "anonimo" ? CategoriaDeError.NoAutenticado : CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Single(entorno.Denegaciones.Eventos);
        Assert.Empty(entorno.Reservas.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Un_envio_inexistente_es_no_encontrado_y_una_base_caida_es_servicio_no_disponible()
    {
        var entorno = new EntornoDeEnvios();
        var inexistente = await entorno.PasarAEnReparto.Ejecutar(Guid.NewGuid());
        var envio = SembrarConGuia(entorno);
        entorno.Envios.NoDisponible = true;
        var caida = await entorno.PasarAEnReparto.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, caida.Error!.Categoria);
        Assert.Empty(entorno.Reservas.Llamadas);
    }
}
