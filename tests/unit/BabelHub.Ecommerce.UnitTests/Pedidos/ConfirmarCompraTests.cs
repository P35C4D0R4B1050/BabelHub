using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Domain.Notificaciones;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T11 (PED-01, PED-03, PED-12, PED-32, PED-35, PED-36, PED-38; AC-PED-01, 10, 27, 30, 32): crear el pedido desde la compra aprobada, dentro de la transacción de Pagos.</summary>
public sealed class ConfirmarCompraTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static LineaDeCompraAprobada Impresa(string titulo = "Estructuras de datos", bool traslado = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), false, titulo, "9780000000001", 1, 50m, 50m, "LIM", traslado, traslado ? Guid.NewGuid() : null, traslado ? new DateOnly(2026, 11, 25) : null);

    private static LineaDeCompraAprobada Digital(string titulo = "Cien años de soledad") =>
        new(Guid.NewGuid(), Guid.NewGuid(), true, titulo, "9780000000002", 1, 20m, 20m, null, false, null, null);

    private static IReadOnlyList<AceptacionDeCompra> Aceptaciones(bool reembolso) =>
        [new(TextoLegalDeCompra.Terminos, "t-1", Ahora), new(TextoLegalDeCompra.Privacidad, "p-1", Ahora), .. reembolso ? new[] { new AceptacionDeCompra(TextoLegalDeCompra.ReembolsoDigital, "r-1", Ahora) } : []];

    private static CompraAprobada Compra(TipoDeEntregaDeCompra tipo, params LineaDeCompraAprobada[] lineas)
    {
        var envio = tipo == TipoDeEntregaDeCompra.Domicilio ? 12m : 0m;
        var contacto = tipo == TipoDeEntregaDeCompra.Domicilio ? new ContactoDeLaCompra("Ana Pérez", "987654321", "Jr. Lima 123", "ayacucho") : null;
        return new CompraAprobada(
            Guid.NewGuid(), Cuenta, tipo, tipo == TipoDeEntregaDeCompra.Recojo ? "LIM" : null, tipo == TipoDeEntregaDeCompra.Domicilio ? "ayacucho" : null, envio, lineas.Sum(l => l.Importe) + envio, contacto, Aceptaciones(lineas.Any(l => l.EsDigital)), lineas);
    }

    private sealed class Entorno
    {
        public RepositorioDePedidosFalso Repositorio { get; } = new();

        public MarcaDeCancelacionFalsa Marca { get; } = new();

        public AccesosDeBibliotecaFalsos Accesos { get; } = new();

        public GeneradorDeNumerosFalso Numeros { get; } = new();

        public AuditoriaDelCatalogoEspia Auditoria { get; } = new();

        public NotificadorDePedidosEspia Notificador { get; } = new();

        public ConfirmarCompra Caso => new(Repositorio, Marca, Accesos, Numeros, Auditoria, Notificador, new RelojFijo(Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-01")]
    public async Task Una_compra_con_un_impreso_y_un_digital_crea_el_pedido_con_la_copia_y_concede_el_acceso()
    {
        var entorno = new Entorno();
        var compra = Compra(TipoDeEntregaDeCompra.Recojo, Impresa(), Digital());

        var resultado = await entorno.Caso.Confirmar(compra);

        Assert.Equal(ResultadoDeConfirmarCompra.Creado, resultado);
        var pedido = Assert.Single(entorno.Repositorio.Pedidos);
        Assert.Equal((compra.PedidoId, Cuenta, 70m), (pedido.Id, pedido.CuentaId, pedido.Total));
        Assert.Equal(2, pedido.Lineas.Count);
        Assert.Equal(EstadoDeLineaImpresa.Confirmado, pedido.Lineas.Single(l => !l.EsDigital).EstadoImpreso);
        Assert.Equal(EstadoDeLineaDigital.Disponible, pedido.Lineas.Single(l => l.EsDigital).EstadoDigital);
        Assert.Equal([(Cuenta, compra.PedidoId, compra.Lineas.Single(l => l.EsDigital).EdicionId)], entorno.Accesos.Concedidos);
        Assert.Empty(entorno.Repositorio.LiberacionesDeContacto);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-10")]
    public async Task Un_pedido_solo_digital_queda_entregado_y_no_se_pide_la_liberacion_del_contacto()
    {
        var entorno = new Entorno();

        var resultado = await entorno.Caso.Confirmar(Compra(TipoDeEntregaDeCompra.Digital, Digital()));

        Assert.Equal(ResultadoDeConfirmarCompra.Creado, resultado);
        Assert.Equal(EstadoDePedido.Entregado, Assert.Single(entorno.Repositorio.Pedidos).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-27")]
    public async Task Un_pedido_a_domicilio_guarda_la_copia_del_contacto_y_registra_la_liberacion_en_Pagos_y_uno_de_recojo_no()
    {
        var entorno = new Entorno();
        var domicilio = Compra(TipoDeEntregaDeCompra.Domicilio, Impresa());
        var recojo = Compra(TipoDeEntregaDeCompra.Recojo, Impresa("Otro"));

        await entorno.Caso.Confirmar(domicilio);
        await entorno.Caso.Confirmar(recojo);

        var guardado = entorno.Repositorio.Pedidos.Single(p => p.Id == domicilio.PedidoId);
        Assert.Equal(("Ana Pérez", "987654321", "Jr. Lima 123"), (guardado.Contacto!.Nombre, guardado.Contacto.Telefono, guardado.Contacto.Direccion));
        Assert.Equal("ayacucho", guardado.RegionDeEntrega);
        Assert.Null(entorno.Repositorio.Pedidos.Single(p => p.Id == recojo.PedidoId).Contacto);
        Assert.Equal([domicilio.PedidoId], entorno.Repositorio.LiberacionesDeContacto.Select(l => l.PedidoId));
        // La liberación se registra después de guardar la copia.
        Assert.True(entorno.Repositorio.Llamadas.IndexOf("agregar") < entorno.Repositorio.Llamadas.IndexOf("liberacion-de-contacto"));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-30")]
    public async Task Un_pedido_a_domicilio_sin_contacto_o_con_uno_incompleto_no_se_crea_ni_se_pide_la_liberacion()
    {
        var entorno = new Entorno();
        var sinContacto = Compra(TipoDeEntregaDeCompra.Domicilio, Impresa()) with { Contacto = null };
        var invalido = Compra(TipoDeEntregaDeCompra.Domicilio, Impresa()) with { Contacto = new ContactoDeLaCompra("Ana\n", "987654321", "Jr. Lima 123", "ayacucho") };

        var a = await entorno.Caso.Confirmar(sinContacto);
        var b = await entorno.Caso.Confirmar(invalido);

        Assert.Equal(ResultadoDeConfirmarCompra.Fallo, a);
        Assert.Equal(ResultadoDeConfirmarCompra.Fallo, b);
        Assert.Empty(entorno.Repositorio.Pedidos);
        Assert.Empty(entorno.Repositorio.LiberacionesDeContacto);
        Assert.Empty(entorno.Accesos.Concedidos);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-32")]
    public async Task Con_la_cuenta_en_ejecucion_de_cancelacion_no_se_crea_el_pedido_y_se_informa_para_que_Pagos_reembolse()
    {
        var entorno = new Entorno();
        entorno.Marca.Estado = EstadoDeLaMarca.EnEjecucion;

        var resultado = await entorno.Caso.Confirmar(Compra(TipoDeEntregaDeCompra.Domicilio, Impresa(), Digital()));

        Assert.Equal(ResultadoDeConfirmarCompra.CuentaEnCancelacion, resultado);
        Assert.Empty(entorno.Repositorio.Pedidos);
        Assert.Empty(entorno.Accesos.Concedidos);
        Assert.Empty(entorno.Repositorio.LiberacionesDeContacto);
        Assert.Equal(1, entorno.Marca.Lecturas);
    }

    [Theory]
    [InlineData(EstadoDeLaMarca.Libre, true)]
    [InlineData(EstadoDeLaMarca.Diferida, true)]
    [InlineData(EstadoDeLaMarca.EnEjecucion, false)]
    [InlineData(EstadoDeLaMarca.Ejecutada, false)]
    public async Task Solo_una_cancelacion_en_ejecucion_o_ejecutada_impide_crear_el_pedido(EstadoDeLaMarca marca, bool seCrea)
    {
        var entorno = new Entorno();
        entorno.Marca.Estado = marca;

        var resultado = await entorno.Caso.Confirmar(Compra(TipoDeEntregaDeCompra.Recojo, Impresa()));

        Assert.Equal(seCrea ? ResultadoDeConfirmarCompra.Creado : ResultadoDeConfirmarCompra.CuentaEnCancelacion, resultado);
    }

    [Fact]
    public async Task Una_repeticion_sobre_un_pedido_existente_con_la_cuenta_ya_en_cancelacion_devuelve_Creado_sin_duplicar()
    {
        var entorno = new Entorno();
        var compra = Compra(TipoDeEntregaDeCompra.Recojo, Impresa());
        Assert.Equal(ResultadoDeConfirmarCompra.Creado, await entorno.Caso.Confirmar(compra));
        entorno.Marca.Estado = EstadoDeLaMarca.EnEjecucion;

        var repetida = await entorno.Caso.Confirmar(compra);

        Assert.Equal(ResultadoDeConfirmarCompra.Creado, repetida);
        Assert.Single(entorno.Repositorio.Pedidos);
        Assert.Equal(1, entorno.Marca.Lecturas);
        Assert.Single(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    public async Task Una_colision_de_numero_se_reintenta_con_un_numero_nuevo_y_agotados_los_intentos_es_fallo()
    {
        var entorno = new Entorno();
        entorno.Repositorio.ColisionesDeNumero = 2;

        var resultado = await entorno.Caso.Confirmar(Compra(TipoDeEntregaDeCompra.Recojo, Impresa()));

        Assert.Equal(ResultadoDeConfirmarCompra.Creado, resultado);
        Assert.Equal(3, entorno.Numeros.Entregados.Count);
        Assert.Equal(entorno.Numeros.Entregados[2], Assert.Single(entorno.Repositorio.Pedidos).Numero.Valor);

        var agotado = new Entorno();
        agotado.Repositorio.ColisionesDeNumero = ConfirmarCompra.IntentosDeNumero;
        Assert.Equal(ResultadoDeConfirmarCompra.Fallo, await agotado.Caso.Confirmar(Compra(TipoDeEntregaDeCompra.Recojo, Impresa())));
        Assert.Empty(agotado.Repositorio.Pedidos);
    }

    [Fact]
    public async Task Una_digital_ya_poseida_por_otra_compra_responde_DigitalYaPoseida_para_que_Pagos_compense()
    {
        var entorno = new Entorno();
        var compra = Compra(TipoDeEntregaDeCompra.Recojo, Impresa(), Digital());
        entorno.Accesos.YaPoseidas.Add(compra.Lineas.Single(l => l.EsDigital).EdicionId);

        var resultado = await entorno.Caso.Confirmar(compra);

        Assert.Equal(ResultadoDeConfirmarCompra.DigitalYaPoseida, resultado);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Theory]
    [InlineData(ResultadoDeConcederAcceso.Concedido)]
    [InlineData(ResultadoDeConcederAcceso.Reactivado)]
    [InlineData(ResultadoDeConcederAcceso.YaConcedidoPorEstePedido)]
    public async Task Concedido_reactivado_o_ya_concedido_por_este_pedido_crean_el_pedido(ResultadoDeConcederAcceso respuesta)
    {
        var entorno = new Entorno();
        entorno.Accesos.Respuesta = respuesta;

        Assert.Equal(ResultadoDeConfirmarCompra.Creado, await entorno.Caso.Confirmar(Compra(TipoDeEntregaDeCompra.Digital, Digital())));
    }

    [Fact]
    public async Task Si_Biblioteca_falla_o_la_auditoria_no_esta_disponible_es_fallo_y_Pagos_revierte()
    {
        var biblioteca = new Entorno();
        biblioteca.Accesos.Falla = true;
        var auditoria = new Entorno();
        auditoria.Auditoria.Caida = true;

        var a = await biblioteca.Caso.Confirmar(Compra(TipoDeEntregaDeCompra.Digital, Digital()));
        var b = await auditoria.Caso.Confirmar(Compra(TipoDeEntregaDeCompra.Recojo, Impresa()));

        Assert.Equal(ResultadoDeConfirmarCompra.Fallo, a);
        Assert.Equal(ResultadoDeConfirmarCompra.Fallo, b);
    }

    [Fact]
    public async Task La_creacion_se_audita_sin_datos_personales_y_con_el_pedido_como_objeto()
    {
        var entorno = new Entorno();
        var compra = Compra(TipoDeEntregaDeCompra.Domicilio, Impresa());

        await entorno.Caso.Confirmar(compra);

        var solicitud = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal((AccionesAuditables.EstadoDePedidoCambiado, "pedido", compra.PedidoId.ToString("D")), (solicitud.Accion, solicitud.ObjetoTipo, solicitud.ObjetoId));
        Assert.Equal(("ninguno", "confirmado"), (solicitud.Detalle["estadoAnterior"], solicitud.Detalle["estadoNuevo"]));
        Assert.DoesNotContain("Ana", string.Join('|', solicitud.Detalle.Values), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Las_aceptaciones_y_las_lineas_se_copian_de_la_compra_aprobada()
    {
        var entorno = new Entorno();
        var compra = Compra(TipoDeEntregaDeCompra.Recojo, Impresa(traslado: true), Digital());

        await entorno.Caso.Confirmar(compra);

        var pedido = Assert.Single(entorno.Repositorio.Pedidos);
        Assert.Equal(("t-1", "p-1", "r-1"), (pedido.Terminos.Version, pedido.Privacidad.Version, pedido.ReembolsoDigital!.Version));
        var impresa = pedido.Lineas.Single(l => !l.EsDigital);
        Assert.True(impresa.RequiereTraslado);
        Assert.Equal(new DateOnly(2026, 11, 25), impresa.FechaEstimadaDeLlegada);
        Assert.Equal(compra.Lineas.Select(l => l.LineaId).Order(), pedido.Lineas.Select(l => l.Id).Order());
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-01")]
    public async Task Una_compra_con_un_impreso_y_un_digital_pide_el_aviso_de_pedido_confirmado_y_el_de_libro_digital_disponible_al_cliente()
    {
        var entorno = new Entorno();
        var compra = Compra(TipoDeEntregaDeCompra.Recojo, Impresa(), Digital());

        await entorno.Caso.Confirmar(compra);

        var pedido = Assert.Single(entorno.Repositorio.Pedidos);
        var solicitudes = entorno.Notificador.Solicitudes;
        Assert.Equal(["pedido-confirmado", "libro-digital-disponible"], solicitudes.Select(s => s.Plantilla));
        Assert.All(solicitudes, s => Assert.Equal((DestinatarioDelAviso.Cliente, Cuenta.ToString("D")), (s.Destinatario, s.DestinatarioRef)));
        Assert.Equal([$"pedido:{compra.PedidoId:N}:confirmado:compra", $"pedido:{compra.PedidoId:N}:libro-digital:compra"], solicitudes.Select(s => s.EventoId));
        Assert.All(solicitudes, s => Assert.Equal(pedido.Numero.Valor, s.Campos["numeroDePedido"]));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-01")]
    public async Task Una_compra_solo_de_impresos_no_pide_el_aviso_del_libro_digital_y_una_solo_digital_pide_los_dos()
    {
        var entorno = new Entorno();

        await entorno.Caso.Confirmar(Compra(TipoDeEntregaDeCompra.Recojo, Impresa()));
        var soloImpresos = entorno.Notificador.Solicitudes.Select(s => s.Plantilla).ToList();
        entorno.Notificador.Solicitudes.Clear();
        await entorno.Caso.Confirmar(Compra(TipoDeEntregaDeCompra.Digital, Digital()));

        Assert.Equal(["pedido-confirmado"], soloImpresos);
        Assert.Equal(["pedido-confirmado", "libro-digital-disponible"], entorno.Notificador.Solicitudes.Select(s => s.Plantilla));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-01")]
    public async Task Cada_aviso_que_pide_el_pedido_confirmado_cumple_el_validador_real_del_catalogo()
    {
        var entorno = new Entorno();

        await entorno.Caso.Confirmar(Compra(TipoDeEntregaDeCompra.Recojo, Impresa(), Digital()));

        Assert.NotEmpty(entorno.Notificador.Solicitudes);
        foreach (var solicitud in entorno.Notificador.Solicitudes)
        {
            var plantilla = CatalogoDePlantillas.Obtener(solicitud.Plantilla);
            Assert.NotNull(plantilla);
            var validacion = plantilla.Validar(solicitud.Campos);
            Assert.True(validacion.EsValido, $"{solicitud.Plantilla}: {validacion.Campo} {validacion.Motivo}");
            Assert.Equal(TipoDeDestinatario.Cliente, plantilla.Destinatario);
            Assert.True(PlantillaDeAviso.EventoValido(solicitud.EventoId), solicitud.EventoId);
            Assert.True(PlantillaDeAviso.ReferenciaValida(solicitud.DestinatarioRef));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-01")]
    public async Task Una_repeticion_de_la_confirmacion_no_vuelve_a_pedir_los_avisos()
    {
        var entorno = new Entorno();
        var compra = Compra(TipoDeEntregaDeCompra.Recojo, Impresa(), Digital());

        await entorno.Caso.Confirmar(compra);
        var primera = entorno.Notificador.Solicitudes.Count;
        var repetida = await entorno.Caso.Confirmar(compra);

        Assert.Equal(ResultadoDeConfirmarCompra.Creado, repetida);
        Assert.Equal(2, primera);
        Assert.Equal(primera, entorno.Notificador.Solicitudes.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-03")]
    public async Task Si_el_Notificador_falla_el_pedido_se_crea_igual_y_el_caso_de_uso_no_se_interrumpe()
    {
        var entorno = new Entorno();
        entorno.Notificador.Lanza = true;

        var resultado = await entorno.Caso.Confirmar(Compra(TipoDeEntregaDeCompra.Recojo, Impresa(), Digital()));

        Assert.Equal(ResultadoDeConfirmarCompra.Creado, resultado);
        Assert.Single(entorno.Repositorio.Pedidos);
        Assert.Single(entorno.Accesos.Concedidos);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-01")]
    public async Task Una_compra_que_no_se_crea_no_pide_ningun_aviso()
    {
        var entorno = new Entorno();
        entorno.Auditoria.Caida = true;
        var sinContacto = Compra(TipoDeEntregaDeCompra.Domicilio, Impresa()) with { Contacto = null };

        var sinAuditoria = await entorno.Caso.Confirmar(Compra(TipoDeEntregaDeCompra.Recojo, Impresa(), Digital()));
        entorno.Auditoria.Caida = false;
        var rechazada = await entorno.Caso.Confirmar(sinContacto);

        Assert.Equal(ResultadoDeConfirmarCompra.Fallo, sinAuditoria);
        Assert.Equal(ResultadoDeConfirmarCompra.Fallo, rechazada);
        Assert.Empty(entorno.Notificador.Solicitudes);
    }
}
