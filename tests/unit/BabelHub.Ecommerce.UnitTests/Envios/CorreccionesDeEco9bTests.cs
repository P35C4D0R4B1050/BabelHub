using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;

namespace BabelHub.Ecommerce.UnitTests.Envios;

/// <summary>Los hallazgos de las revisiones de ECO-9b: direcciones largas, excepciones de los adaptadores, el pedido en el cierre del Inventario y el marcador retomado.</summary>
public sealed class CorreccionesDeEco9bTests
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

    private static Envio SembrarConGuia(EntornoDeEnvios entorno)
    {
        var pedidoId = Guid.NewGuid();
        var envio = Envio.Crear(Guid.NewGuid(), pedidoId, "lima-central", [Guid.NewGuid()], EntornoDeEnvios.AhoraInicial);
        envio.TomarSolicitudDeGuia(EntornoDeEnvios.AhoraInicial, Guid.NewGuid());
        envio.RegistrarGuia(NumeroDeGuia.Crear("GUIA-000123"), "operador-a", EntornoDeEnvios.AhoraInicial);
        entorno.Envios.Sembrar(envio);
        entorno.SembrarPedidoDelEnvio(pedidoId);
        return envio;
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-09")]
    public async Task Una_direccion_de_200_caracteres_con_una_region_de_60_se_entrega_al_operador_sin_un_falso_campo_faltante()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarPendiente(entorno);
        var direccion = new string('d', 200);
        var region = new string('r', 60);
        entorno.Contactos.Sembrar(envio.PedidoId, LecturaDelContactoDeEntrega.Disponible(new ContactoDeEntrega("Ana Pérez", "987654321", direccion, region)));

        var resultado = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.True(resultado.EsExito);
        Assert.Equal($"{direccion}, {region}", Assert.Single(entorno.Operador.Recibidas).Direccion);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-17")]
    [InlineData("Ana\u2028Pérez", "Av. Los Olivos 123", "987654321", CampoDelDestinatario.Nombre)]
    [InlineData("Ana\u200BPérez", "Av. Los Olivos 123", "987654321", CampoDelDestinatario.Nombre)]
    [InlineData("Ana Pérez", "Av.\u202ELos Olivos", "987654321", CampoDelDestinatario.Direccion)]
    [InlineData("Ana Pérez", "Av.\u00A0Los Olivos", "987654321", CampoDelDestinatario.Direccion)]
    public void Los_caracteres_de_formato_bidireccionales_y_otros_separadores_se_rechazan_como_los_de_carrito(string nombre, string direccion, string telefono, CampoDelDestinatario esperado)
    {
        var resultado = DestinatarioParaGuia.Validar(nombre, direccion, telefono);

        Assert.False(resultado.EsValido);
        Assert.Equal(esperado, resultado.CampoFaltante);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-04")]
    public async Task Una_excepcion_inesperada_del_operador_es_operador_no_disponible_libera_el_arrendamiento_y_no_propaga_su_texto()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarPendiente(entorno);
        entorno.Operador.Lanza = true;

        var resultado = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal("operador-no-disponible", resultado.Error.Clave);
        Assert.DoesNotContain("TEXTO-INTERNO", resultado.Error + string.Join(",", resultado.Error.Errores.Select(e => e.Campo + e.Clave)), StringComparison.Ordinal);
        var guardado = Assert.Single(entorno.Envios.Todos);
        Assert.Null(guardado.GuiaEnCursoHasta);
        Assert.NotNull(guardado.ClaveDeSolicitud);

        var reintento = await entorno.SolicitarGuia.Ejecutar(envio.Id);
        Assert.True(reintento.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public async Task Una_excepcion_inesperada_al_registrar_la_guia_libera_el_arrendamiento_y_el_reintento_recupera_la_misma_guia()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarPendiente(entorno);
        entorno.Auditoria.FallaEnLaLlamada = 0;
        var fallida = false;
        entorno.Operador.AlLlamar = () =>
        {
            if (!fallida)
            {
                fallida = true;
                entorno.Envios.FallaAlGuardarElSiguiente = true;
            }
        };

        var primera = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.NoDisponible, primera.Error!.Categoria);
        Assert.Null(entorno.Envios.Todos.Single().GuiaEnCursoHasta);

        var recuperada = await entorno.SolicitarGuia.Ejecutar(envio.Id);
        Assert.True(recuperada.EsExito);
        Assert.Equal(1, entorno.Operador.GuiasCreadas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-11")]
    public async Task Una_excepcion_inesperada_del_inventario_es_un_resultado_incierto_y_no_propaga_su_texto()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarConGuia(entorno);
        entorno.Reservas.Lanza = true;

        var resultado = await entorno.PasarAEnReparto.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.DoesNotContain("TEXTO-INTERNO", resultado.Error.ToString(), StringComparison.Ordinal);
        var guardado = Assert.Single(entorno.Envios.Todos);
        Assert.Equal(EstadoDeEnvio.GuiaGenerada, guardado.Estado);
        Assert.NotNull(guardado.DespachoIniciadoEn);
        Assert.NotNull(guardado.DespachoIncertoEn);
        Assert.Null(guardado.DespachoEnCursoHasta);
        Assert.Equal(DestinoDelEnvioAlCancelar.Mantenido, Assert.Single((await entorno.Cancelar.Ejecutar(envio.PedidoId)).Valor).Destino);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-11")]
    public async Task El_cierre_del_inventario_lleva_el_pedido_el_envio_y_todas_las_lineas()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarConGuia(entorno);

        await entorno.PasarAEnReparto.Ejecutar(envio.Id);

        var llamada = Assert.Single(entorno.Reservas.Llamadas);
        Assert.Equal(envio.PedidoId, llamada.PedidoId);
        Assert.Equal(envio.Id, llamada.EnvioId);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-19")]
    public void Retomar_un_marcador_existente_cuenta_como_intento_incierto_y_ya_no_se_limpia()
    {
        var envio = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "lima-central", [Guid.NewGuid()], EntornoDeEnvios.AhoraInicial);
        envio.TomarSolicitudDeGuia(EntornoDeEnvios.AhoraInicial, Guid.NewGuid());
        envio.RegistrarGuia(NumeroDeGuia.Crear("GUIA-000123"), "operador-a", EntornoDeEnvios.AhoraInicial);
        envio.IniciarDespacho(EntornoDeEnvios.AhoraInicial);
        envio.LiberarArrendamientoDelDespacho();

        envio.IniciarDespacho(EntornoDeEnvios.AhoraInicial.AddMinutes(5));

        Assert.Equal(EntornoDeEnvios.AhoraInicial.AddMinutes(5), envio.DespachoIncertoEn);
        Assert.False(envio.LimpiarMarcadorDeDespacho());
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-07")]
    public async Task La_alerta_de_progreso_mide_desde_la_entrega_cuando_lo_pendiente_es_la_entrega()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarConGuia(entorno);
        var guardado = entorno.Envios.Todos.Single();
        guardado.IniciarDespacho(EntornoDeEnvios.AhoraInicial.AddDays(-3));
        guardado.PasarAEnReparto(EntornoDeEnvios.AhoraInicial.AddDays(-3));
        guardado.RegistrarProgresoEnRepartoInformado(EntornoDeEnvios.AhoraInicial.AddDays(-3));
        guardado.MarcarEntregado(EntornoDeEnvios.AhoraInicial.AddMinutes(-5));
        entorno.Envios.Sembrar(guardado);
        entorno.Progreso.Falla = true;

        await entorno.InformarProgreso.Ejecutar(10);

        Assert.Empty(entorno.Alertas.Alertas); // la entrega lleva 5 minutos, no 3 días
        _ = envio;
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public async Task Un_operador_que_repite_el_numero_de_otro_envio_deja_el_envio_pendiente_con_una_alerta_y_no_un_error_500()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarPendiente(entorno);
        entorno.Envios.GuiaRepetidaAlGuardar = true;

        var resultado = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal("operador-no-disponible", resultado.Error.Clave);
        var guardado = Assert.Single(entorno.Envios.Todos);
        Assert.Equal(EstadoDeEnvio.PendienteDeGuia, guardado.Estado);
        Assert.Null(guardado.NumeroDeGuia);
        Assert.Null(guardado.GuiaEnCursoHasta);
        Assert.Equal("envio-guia-repetida", Assert.Single(entorno.Alertas.Alertas).Motivo);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }
}
