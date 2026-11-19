using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Envios;

/// <summary>Pedir la guía al operador (T06; ENV-02, ENV-04, ENV-10, ENV-17, ENV-18).</summary>
public sealed class SolicitarGuiaTests
{
    private static Envio SembrarEnvio(EntornoDeEnvios entorno, Action<Envio>? preparar = null)
    {
        var pedidoId = Guid.NewGuid();
        var envio = Envio.Crear(Guid.NewGuid(), pedidoId, "lima-central", [Guid.NewGuid(), Guid.NewGuid()], EntornoDeEnvios.AhoraInicial);
        preparar?.Invoke(envio);
        entorno.Envios.Sembrar(envio);
        entorno.Contactos.SembrarDisponible(pedidoId);
        entorno.SembrarPedidoDelEnvio(pedidoId);
        return envio;
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-02")]
    public async Task Logistica_pide_la_guia_y_el_envio_pasa_a_guia_generada_con_el_numero_el_operador_y_una_auditoria()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarEnvio(entorno);

        var resultado = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.True(resultado.EsExito);
        Assert.Equal(BabelHub.Ecommerce.Application.Envios.EstadoDelEnvio.GuiaGenerada, resultado.Valor.Estado);
        Assert.Equal("GUIA-000001", resultado.Valor.NumeroDeGuia);
        Assert.Equal("operador-a", resultado.Valor.Operador);
        var guardado = Assert.Single(entorno.Envios.Todos);
        Assert.Equal(EstadoDeEnvio.GuiaGenerada, guardado.Estado);
        Assert.Equal(entorno.Operador.Recibidas[0].Clave, guardado.ClaveDeSolicitud);

        var auditoria = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.EstadoDeEnvioCambiado, auditoria.Accion);
        Assert.Equal("pendiente_de_guia", auditoria.Detalle["estadoAnterior"]);
        Assert.Equal("guia_generada", auditoria.Detalle["estadoNuevo"]);
        Assert.Equal(envio.PedidoId.ToString(), auditoria.Detalle["pedidoId"]);
        Assert.Equal("GUIA-000001", auditoria.Detalle["numeroDeGuia"]);
        Assert.Equal("operador-a", auditoria.Detalle["operador"]);
        Assert.Equal("lima-central", auditoria.Detalle["fuente"]);
        Assert.Equal(["estadoAnterior", "estadoNuevo", "fuente", "numeroDeGuia", "operador", "pedidoId"], auditoria.Detalle.Keys.Order().ToArray());
        Assert.Empty(entorno.Alertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-02")]
    public async Task La_administracion_central_tambien_puede_pedir_la_guia()
    {
        var entorno = new EntornoDeEnvios(ActorDePrueba.DeAdministradorCentral());
        var envio = SembrarEnvio(entorno);

        Assert.True((await entorno.SolicitarGuia.Ejecutar(envio.Id)).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public async Task El_operador_recibe_solo_nombre_direccion_y_telefono_y_la_llamada_ocurre_fuera_de_toda_transaccion()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarEnvio(entorno);

        await entorno.SolicitarGuia.Ejecutar(envio.Id);

        var recibida = Assert.Single(entorno.Operador.Recibidas);
        Assert.Equal("Ana Pérez Quispe", recibida.Nombre);
        Assert.Equal("Av. Los Olivos 123, Ayacucho", recibida.Direccion); // la región va dentro de la dirección
        Assert.Equal("987654321", recibida.Telefono);
        Assert.Equal(0, entorno.Operador.LlamadasDentroDeLaTransaccion);
        Assert.Equal(0, entorno.Contactos.LecturasDentroDeLaTransaccion);
        Assert.Equal(1, entorno.Contactos.Lecturas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-09")]
    public async Task Un_contacto_con_un_campo_faltante_se_rechaza_sin_llamar_al_operador_ni_fijar_la_clave()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarEnvio(entorno);
        entorno.Contactos.Sembrar(envio.PedidoId, LecturaDelContactoDeEntrega.Faltante(CampoDelContactoDeEntrega.Telefono));

        var resultado = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.ReglaDeNegocio, resultado.Error!.Categoria);
        Assert.Equal("contacto-incompleto", resultado.Error.Clave);
        Assert.Equal("telefono", Assert.Single(resultado.Error.Errores).Campo);
        Assert.Empty(entorno.Operador.Recibidas);
        Assert.Null(entorno.Envios.Todos.Single().ClaveDeSolicitud);
        Assert.Null(entorno.Envios.Todos.Single().GuiaEnCursoHasta);
        Assert.Equal(0, entorno.Unidad.Ejecuciones);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-09")]
    public async Task Un_contacto_anonimizado_se_rechaza_indicando_la_anonimizacion_sin_llamar_al_operador()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarEnvio(entorno);
        entorno.Contactos.Sembrar(envio.PedidoId, LecturaDelContactoDeEntrega.Anonimizado());

        var resultado = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.Equal("contacto-incompleto", resultado.Error!.Clave);
        Assert.Equal("anonimizado", Assert.Single(resultado.Error.Errores).Campo);
        Assert.Empty(entorno.Operador.Recibidas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-09")]
    public async Task La_lectura_del_contacto_no_disponible_es_servicio_no_disponible_reintentable()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarEnvio(entorno);
        entorno.Contactos.Sembrar(envio.PedidoId, LecturaDelContactoDeEntrega.NoDisponible());

        var resultado = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Empty(entorno.Operador.Recibidas);
        Assert.Null(entorno.Envios.Todos.Single().ClaveDeSolicitud);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-04")]
    [InlineData("rechazo")]
    [InlineData("no-disponible")]
    [InlineData("invalida")]
    public async Task Un_operador_que_no_responde_rechaza_o_responde_algo_invalido_deja_el_envio_pendiente_sin_numero_ni_auditoria(string modo)
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarEnvio(entorno);
        entorno.Operador.Forzado = modo switch
        {
            "rechazo" => new ResultadoDelOperador.Rechazada(),
            "no-disponible" => new ResultadoDelOperador.NoDisponible(),
            _ => new ResultadoDelOperador.RespuestaInvalida(),
        };

        var resultado = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal("operador-no-disponible", resultado.Error.Clave);
        var guardado = Assert.Single(entorno.Envios.Todos);
        Assert.Equal(EstadoDeEnvio.PendienteDeGuia, guardado.Estado);
        Assert.Null(guardado.NumeroDeGuia);
        Assert.Null(guardado.GuiaEnCursoHasta); // el arrendamiento se liberó
        Assert.NotNull(guardado.ClaveDeSolicitud); // la clave se conservó
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-04")]
    public async Task Tras_un_fallo_del_operador_el_reintento_inmediato_usa_la_misma_clave_y_funciona()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarEnvio(entorno);
        entorno.Operador.Forzado = new ResultadoDelOperador.NoDisponible();
        await entorno.SolicitarGuia.Ejecutar(envio.Id);
        var clave = entorno.Envios.Todos.Single().ClaveDeSolicitud;

        entorno.Operador.Forzado = null;
        var reintento = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.True(reintento.EsExito);
        Assert.Equal(clave, entorno.Operador.Recibidas.Last().Clave);
        Assert.Equal(2, entorno.Operador.Recibidas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public async Task Una_segunda_solicitud_con_el_arrendamiento_vivo_recibe_guia_en_curso()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarEnvio(entorno, e => e.TomarSolicitudDeGuia(EntornoDeEnvios.AhoraInicial, Guid.NewGuid()));

        var resultado = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
        Assert.Equal("guia-en-curso", resultado.Error.Clave);
        Assert.Empty(entorno.Operador.Recibidas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public async Task Si_falla_la_auditoria_tras_crearse_la_guia_el_envio_sigue_pendiente_y_el_reintento_recupera_la_misma_guia_sin_duplicar()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarEnvio(entorno);
        entorno.Auditoria.Caida = true;

        var fallida = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.NoDisponible, fallida.Error!.Categoria);
        var pendiente = Assert.Single(entorno.Envios.Todos);
        Assert.Equal(EstadoDeEnvio.PendienteDeGuia, pendiente.Estado);
        Assert.Null(pendiente.NumeroDeGuia);
        Assert.Null(pendiente.GuiaEnCursoHasta);
        Assert.Equal(1, entorno.Operador.GuiasCreadas);

        entorno.Auditoria.Caida = false;
        var recuperada = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.True(recuperada.EsExito);
        Assert.Equal("GUIA-000001", recuperada.Valor.NumeroDeGuia); // la misma guía
        Assert.Equal(1, entorno.Operador.GuiasCreadas);
        Assert.Equal(entorno.Operador.Recibidas[0].Clave, entorno.Operador.Recibidas[1].Clave);
        Assert.Single(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public async Task Repetir_la_solicitud_con_la_guia_ya_generada_es_un_estado_invalido_sin_llamar_al_operador()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarEnvio(entorno);
        await entorno.SolicitarGuia.Ejecutar(envio.Id);
        var llamadas = entorno.Operador.Recibidas.Count;

        var repeticion = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.Conflicto, repeticion.Error!.Categoria);
        Assert.Equal("estado-invalido", repeticion.Error.Clave);
        Assert.Equal(llamadas, entorno.Operador.Recibidas.Count);
        Assert.Single(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public async Task Si_el_envio_se_cancela_mientras_el_operador_creaba_la_guia_queda_sin_uso_con_una_alerta_sin_datos()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarEnvio(entorno);
        entorno.Operador.AlLlamar = () =>
        {
            var cancelado = entorno.Envios.Todos.Single();
            cancelado.Cancelar(EntornoDeEnvios.AhoraInicial);
            entorno.Envios.Sembrar(cancelado);
        };

        var resultado = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.False(resultado.EsExito);
        Assert.Equal(EstadoDeEnvio.Cancelado, entorno.Envios.Todos.Single().Estado);
        Assert.Null(entorno.Envios.Todos.Single().NumeroDeGuia);
        var alerta = Assert.Single(entorno.Alertas.Alertas);
        Assert.Equal("envio-guia-sin-uso", alerta.Motivo);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-18")]
    public async Task Ni_la_auditoria_ni_los_errores_ni_las_alertas_llevan_el_contacto()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarEnvio(entorno);
        await entorno.SolicitarGuia.Ejecutar(envio.Id);
        var otro = SembrarEnvio(entorno);
        entorno.Contactos.Sembrar(otro.PedidoId, LecturaDelContactoDeEntrega.Faltante(CampoDelContactoDeEntrega.Nombre));
        var rechazo = await entorno.SolicitarGuia.Ejecutar(otro.Id);

        var texto = string.Join("|", entorno.Auditoria.Solicitudes.Select(s => s.ToString() + string.Join(",", s.Detalle.Values)))
            + rechazo.Error + string.Join(",", rechazo.Error!.Errores.Select(e => e.Campo + e.Clave))
            + string.Join("|", entorno.Alertas.Alertas.Select(a => a.Motivo));

        foreach (var secreto in new[] { "Ana", "Pérez", "Olivos", "987654321", "Ayacucho" })
        {
            Assert.DoesNotContain(secreto, texto, StringComparison.Ordinal);
        }
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
        var envio = SembrarEnvio(entorno);

        var resultado = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.Equal(quien == "anonimo" ? CategoriaDeError.NoAutenticado : CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Single(entorno.Denegaciones.Eventos);
        Assert.Empty(entorno.Operador.Recibidas);
        Assert.Equal(0, entorno.Contactos.Lecturas);
        Assert.Equal(0, entorno.Envios.Operaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Un_envio_inexistente_es_no_encontrado_para_logistica_sin_evento_de_denegacion()
    {
        var entorno = new EntornoDeEnvios();

        var resultado = await entorno.SolicitarGuia.Ejecutar(Guid.NewGuid());

        Assert.Equal(CategoriaDeError.NoEncontrado, resultado.Error!.Categoria);
        Assert.Empty(entorno.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-03")]
    public async Task Un_envio_cancelado_se_rechaza_por_estado_invalido_sin_llamar_al_operador()
    {
        var entorno = new EntornoDeEnvios();
        var cancelado = SembrarEnvio(entorno, e => e.Cancelar(EntornoDeEnvios.AhoraInicial));

        var resultado = await entorno.SolicitarGuia.Ejecutar(cancelado.Id);

        Assert.Equal("estado-invalido", resultado.Error!.Clave);
        Assert.Empty(entorno.Operador.Recibidas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-04")]
    public async Task Una_base_caida_es_servicio_no_disponible_sin_llamar_al_operador()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarEnvio(entorno);
        entorno.Envios.NoDisponible = true;

        var resultado = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Empty(entorno.Operador.Recibidas);
    }
}
