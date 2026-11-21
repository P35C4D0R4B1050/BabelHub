using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;

namespace BabelHub.Ecommerce.UnitTests.Envios;

/// <summary>Los avisos de guía generada y de en reparto (T10; ENV-06).</summary>
public sealed class AvisosDeEnviosTests
{
    private static Envio SembrarPendiente(EntornoDeEnvios entorno, Guid? cuenta = null)
    {
        var pedidoId = Guid.NewGuid();
        var envio = Envio.Crear(Guid.NewGuid(), pedidoId, "lima-central", [Guid.NewGuid()], EntornoDeEnvios.AhoraInicial);
        entorno.Envios.Sembrar(envio);
        entorno.Contactos.SembrarDisponible(pedidoId);
        entorno.Pedidos.Sembrar(new BabelHub.Ecommerce.Application.Pedidos.PedidoParaEnvios(pedidoId, "PED-2026-0042", cuenta ?? Guid.NewGuid(), true, []));
        return envio;
    }

    private static Envio SembrarConGuia(EntornoDeEnvios entorno)
    {
        var envio = SembrarPendiente(entorno);
        var guardado = entorno.Envios.Todos.Single(e => e.Id == envio.Id);
        guardado.TomarSolicitudDeGuia(EntornoDeEnvios.AhoraInicial, Guid.NewGuid());
        guardado.RegistrarGuia(NumeroDeGuia.Crear("GUIA-000123"), "operador-a", EntornoDeEnvios.AhoraInicial);
        entorno.Envios.Sembrar(guardado);
        return guardado;
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-06")]
    public async Task La_guia_generada_pide_un_aviso_en_la_misma_transaccion_con_el_evento_determinista_y_campos_cerrados()
    {
        var cuenta = Guid.NewGuid();
        var entorno = new EntornoDeEnvios();
        var envio = SembrarPendiente(entorno, cuenta);

        await entorno.SolicitarGuia.Ejecutar(envio.Id);

        var aviso = Assert.Single(entorno.Notificador.Solicitudes);
        Assert.Equal($"envio:{envio.Id}:guia-generada:cliente", aviso.EventoId);
        Assert.Equal(DestinatarioDelAviso.Cliente, aviso.Destinatario);
        Assert.Equal(cuenta.ToString("D"), aviso.DestinatarioRef);
        Assert.Equal("envio-guia", aviso.Plantilla);
        Assert.Equal(["estado", "numeroDeGuia", "numeroDePedido"], aviso.Campos.Keys.Order().ToArray());
        Assert.Equal("PED-2026-0042", aviso.Campos["numeroDePedido"]);
        Assert.Equal("GUIA-000001", aviso.Campos["numeroDeGuia"]);
        Assert.Equal("guia-generada", aviso.Campos["estado"]);
        Assert.Equal(1, entorno.Notificador.DentroDeLaTransaccion);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-01")]
    public async Task Los_avisos_de_guia_y_de_en_reparto_cumplen_el_validador_real_del_catalogo()
    {
        var entorno = new EntornoDeEnvios();
        var guia = SembrarPendiente(entorno);
        await entorno.SolicitarGuia.Ejecutar(guia.Id);
        var reparto = SembrarConGuia(entorno);
        await entorno.PasarAEnReparto.Ejecutar(reparto.Id);

        Assert.Equal(2, entorno.Notificador.Solicitudes.Count);
        BabelHub.Ecommerce.UnitTests.Notificaciones.AvisosContraElCatalogo.Comprobar(entorno.Notificador.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-06")]
    public async Task El_paso_a_en_reparto_pide_el_aviso_de_en_reparto_con_la_guia()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarConGuia(entorno);

        await entorno.PasarAEnReparto.Ejecutar(envio.Id);

        var aviso = Assert.Single(entorno.Notificador.Solicitudes);
        Assert.Equal($"envio:{envio.Id}:en-reparto:cliente", aviso.EventoId);
        Assert.Equal("en-reparto", aviso.Campos["estado"]);
        Assert.Equal("GUIA-000123", aviso.Campos["numeroDeGuia"]);
        Assert.Equal(1, entorno.Notificador.DentroDeLaTransaccion);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-06")]
    public async Task Entregado_y_cancelado_no_generan_aviso_propio()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarConGuia(entorno);
        await entorno.PasarAEnReparto.Ejecutar(envio.Id);
        entorno.Notificador.Solicitudes.Clear();

        await entorno.MarcarEntregado.Ejecutar(envio.Id);
        var pendiente = SembrarPendiente(entorno);
        await entorno.Cancelar.Ejecutar(pendiente.PedidoId);

        Assert.Empty(entorno.Notificador.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-06")]
    public async Task Ningun_aviso_lleva_datos_de_contacto()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarPendiente(entorno);
        await entorno.SolicitarGuia.Ejecutar(envio.Id);

        var texto = string.Join("|", entorno.Notificador.Solicitudes.Select(s => s.EventoId + s.DestinatarioRef + s.Plantilla + string.Join(",", s.Campos.Values)));

        foreach (var secreto in new[] { "Ana", "Pérez", "Olivos", "987654321", "Ayacucho" })
        {
            Assert.DoesNotContain(secreto, texto, StringComparison.Ordinal);
        }
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-06")]
    [InlineData("lanza")]
    [InlineData("no-se-guardo")]
    [InlineData("rechazado")]
    public async Task Con_el_notificador_caido_el_estado_cambia_igual(string modo)
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarPendiente(entorno);
        switch (modo)
        {
            case "lanza":
                entorno.Notificador.Lanza = true;
                break;
            case "no-se-guardo":
                entorno.Notificador.Resultado = ResultadoDeLaSolicitudDeAviso.NoSeGuardo;
                break;
            default:
                entorno.Notificador.Resultado = ResultadoDeLaSolicitudDeAviso.Rechazado;
                break;
        }

        var guia = await entorno.SolicitarGuia.Ejecutar(envio.Id);
        var reparto = await entorno.PasarAEnReparto.Ejecutar(envio.Id);

        Assert.True(guia.EsExito);
        Assert.True(reparto.EsExito);
        Assert.Equal(EstadoDeEnvio.EnReparto, entorno.Envios.Todos.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-06")]
    public async Task Sin_poder_leer_el_pedido_no_se_cambia_nada_y_se_reintenta()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarPendiente(entorno);
        entorno.Pedidos.NoDisponible = true;

        var guia = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.Equal(CategoriaDeError.NoDisponible, guia.Error!.Categoria);
        Assert.Empty(entorno.Operador.Recibidas);
        Assert.Null(entorno.Envios.Todos.Single().ClaveDeSolicitud);

        entorno.Pedidos.NoDisponible = false;
        Assert.True((await entorno.SolicitarGuia.Ejecutar(envio.Id)).EsExito);

        entorno.Pedidos.NoDisponible = true;
        var reparto = await entorno.PasarAEnReparto.Ejecutar(envio.Id);
        Assert.Equal(CategoriaDeError.NoDisponible, reparto.Error!.Categoria);
        Assert.Empty(entorno.Reservas.Llamadas);
        Assert.Null(entorno.Envios.Todos.Single().DespachoIniciadoEn);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-06")]
    public async Task Una_repeticion_con_la_guia_ya_registrada_no_pide_otro_aviso()
    {
        var entorno = new EntornoDeEnvios();
        var envio = SembrarPendiente(entorno);
        await entorno.SolicitarGuia.Ejecutar(envio.Id);

        await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.Single(entorno.Notificador.Solicitudes);
    }
}
