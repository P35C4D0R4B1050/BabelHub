using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Domain.Fuentes;
using BabelHub.Inventario.Domain.Traslados;
using BabelHub.Inventario.UnitTests.Soporte;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>T10 y T11: los dos casos de uso que <c>Traslados</c> implementa para <c>Reservas</c> (generar los vinculados al confirmar y cancelarlos al anular el pedido), sin base de datos.</summary>
public sealed class TrasladosDeUnPedidoTests
{
    private static (EntornoDeTraslados Entorno, Guid Edicion, Guid Pedido) Preparar(ActorDePrueba? actor = null)
    {
        var entorno = new EntornoDeTraslados(actor ?? ActorDePrueba.DelSistema());
        return (entorno, entorno.Impresa(), Guid.NewGuid());
    }

    // ---------- T10: generar ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public async Task SeCreaUnTrasladoSolicitadoPorFuenteYEdicion_VinculadoAlPedido_ConLaFechaDeLaRutaDeEseMomento()
    {
        var (entorno, edicion, pedido) = Preparar();
        var otra = entorno.Impresa();
        entorno.Base.Rutas.Rutas.Add(RutaEntreFuentes.Definir(entorno.Cusco, entorno.Ayacucho, 2));

        var generados = await entorno.Generador().Generar(
            pedido, entorno.Ayacucho, [new SolicitudDeTrasladoDePedido(entorno.Cusco, edicion, 1), new SolicitudDeTrasladoDePedido(entorno.Lima, otra, 3)]);

        Assert.Equal(2, generados.Count);
        var deCusco = generados.Single(g => g.CodigoDeOrigen == "cusco");
        Assert.Equal((edicion, 1, "ayacucho", "solicitado", new DateOnly(2026, 11, 16)), (deCusco.EdicionId, deCusco.Cantidad, deCusco.CodigoDeDestino, deCusco.Estado, deCusco.FechaEstimadaDeLlegada));
        Assert.Null(generados.Single(g => g.CodigoDeOrigen == "lima").FechaEstimadaDeLlegada);
        var guardado = entorno.Traslados.Almacenado(deCusco.TrasladoId);
        Assert.Equal((pedido, EstadoDeTraslado.Solicitado, false, null), (guardado.PedidoId, guardado.Estado, guardado.EstimadaPorConfirmar, guardado.SolicitadoPor));
        Assert.True(entorno.Traslados.Almacenado(generados.Single(g => g.CodigoDeOrigen == "lima").TrasladoId).EstimadaPorConfirmar);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public async Task LaEstimacionSeFijaAlCrear_SiLaRutaCambiaDespuesNoCambia_YRepetirLaConfirmacionDevuelveElMismo()
    {
        var (entorno, edicion, pedido) = Preparar();
        entorno.Base.Rutas.Rutas.Add(RutaEntreFuentes.Definir(entorno.Cusco, entorno.Ayacucho, 2));
        var solicitud = new[] { new SolicitudDeTrasladoDePedido(entorno.Cusco, edicion, 1) };
        var primera = await entorno.Generador().Generar(pedido, entorno.Ayacucho, solicitud);

        entorno.Base.Rutas.Rutas.Clear();
        entorno.Base.Rutas.Rutas.Add(RutaEntreFuentes.Definir(entorno.Cusco, entorno.Ayacucho, 5));
        var repetida = await entorno.Generador().Generar(pedido, entorno.Ayacucho, solicitud);

        Assert.Equal(primera.Single().TrasladoId, repetida.Single().TrasladoId);
        Assert.Equal(new DateOnly(2026, 11, 16), repetida.Single().FechaEstimadaDeLlegada);
        Assert.Single(entorno.Traslados.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task RepetirLaConfirmacionNoAuditaNiAvisaDeNuevo_YUnaConcurrenteDevuelveElQueGano()
    {
        var (entorno, edicion, pedido) = Preparar();
        var solicitud = new[] { new SolicitudDeTrasladoDePedido(entorno.Cusco, edicion, 1) };

        await entorno.Generador().Generar(pedido, entorno.Ayacucho, solicitud);
        await entorno.Generador().Generar(pedido, entorno.Ayacucho, solicitud);

        Assert.Single(entorno.Base.Auditoria.Solicitudes);
        Assert.Equal(2, entorno.Buzon.Mensajes.Count);

        var otro = Guid.NewGuid();
        entorno.Traslados.ChocaConUnoConcurrente = true;
        var concurrente = await entorno.Generador().Generar(otro, entorno.Ayacucho, solicitud);

        Assert.Equal(otro, entorno.Traslados.Almacenado(concurrente.Single().TrasladoId).PedidoId);
        Assert.Equal(2, entorno.Traslados.Todos.Count);
        Assert.Single(entorno.Base.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task CadaTrasladoCreadoSeAuditaConElPedidoOpaco_YAvisaALasDosSucursales_SinPasarPorPermisosNiPresupuesto()
    {
        var (entorno, edicion, pedido) = Preparar();

        await entorno.Generador().Generar(pedido, entorno.Ayacucho, [new SolicitudDeTrasladoDePedido(entorno.Cusco, edicion, 2)]);

        var auditoria = Assert.Single(entorno.Base.Auditoria.Solicitudes);
        Assert.Equal((AccionesAuditables.TrasladoSolicitado, entorno.Cusco, entorno.Ayacucho, "true", pedido.ToString("D")), (auditoria.Accion, auditoria.SucursalOrigen, auditoria.SucursalDestino, auditoria.Detalle["vinculado"], auditoria.Detalle["pedidoId"]));
        Assert.Equal(["ayacucho", "cusco"], entorno.Buzon.Mensajes.Select(m => m.SucursalCodigo).Order().ToArray());
        Assert.Empty(entorno.Base.Denegaciones.Eventos);
        Assert.Empty(entorno.Limites.Reservas);
    }

    [Fact]
    public async Task ElActorDeUnUsuarioQuedaComoSolicitante_YSinSolicitudesNoHayNada()
    {
        var (entorno, edicion, pedido) = Preparar(ActorDePrueba.DeAdministradorCentral());

        var vacio = await entorno.Generador().Generar(pedido, entorno.Ayacucho, []);
        var generados = await entorno.Generador().Generar(pedido, entorno.Ayacucho, [new SolicitudDeTrasladoDePedido(entorno.Cusco, edicion, 1)]);

        Assert.Empty(vacio);
        Assert.Equal(entorno.Actor.Actual!.UsuarioId, entorno.Traslados.Almacenado(generados.Single().TrasladoId).SolicitadoPor);
    }

    [Fact]
    public async Task UnaFuenteInexistenteOUnPedidoVacioSonUnError_NoUnTrasladoPerdido()
    {
        var (entorno, edicion, pedido) = Preparar();

        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.Generador().Generar(pedido, Guid.NewGuid(), [new SolicitudDeTrasladoDePedido(entorno.Cusco, edicion, 1)]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.Generador().Generar(pedido, entorno.Ayacucho, [new SolicitudDeTrasladoDePedido(Guid.NewGuid(), edicion, 1)]));
        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Generador().Generar(Guid.Empty, entorno.Ayacucho, [new SolicitudDeTrasladoDePedido(entorno.Cusco, edicion, 1)]));
        Assert.Empty(entorno.Traslados.Todos);
    }

    // ---------- T11: cancelar por la anulación del pedido ----------

    private static async Task<(EntornoDeTraslados Entorno, Guid Edicion, Guid Pedido, Traslado Traslado)> Vinculado(EstadoDeTraslado estado)
    {
        var (entorno, edicion, pedido) = Preparar();
        var traslado = entorno.Vinculado(edicion, entorno.Cusco, entorno.Ayacucho, 1, pedido, estado);
        await Task.CompletedTask;
        return (entorno, edicion, pedido, traslado);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-15")]
    [InlineData(EstadoDeTraslado.Solicitado)]
    [InlineData(EstadoDeTraslado.Aprobado)]
    public async Task LaAnulacionCancelaUnTrasladoSolicitadoOAprobado_ConElMotivoPedidoAnulado_AuditaYAvisa(EstadoDeTraslado estado)
    {
        var (entorno, _, pedido, traslado) = await Vinculado(estado);

        await entorno.CancelacionPorPedido().AlLiberar(pedido, MotivoDeLiberacionDeReserva.PedidoAnulado, [traslado.Id]);

        var guardado = entorno.Traslados.Almacenado(traslado.Id);
        Assert.Equal((EstadoDeTraslado.Cancelado, Traslado.MotivoPedidoAnulado, EntornoDeTraslados.Ahora), (guardado.Estado, guardado.MotivoDeCierre, guardado.CerradoEn));
        var auditoria = Assert.Single(entorno.Base.Auditoria.Solicitudes);
        Assert.Equal((AccionesAuditables.TrasladoCancelado, estado == EstadoDeTraslado.Solicitado ? "solicitado" : "aprobado", "cancelado"), (auditoria.Accion, auditoria.Detalle["estadoAnterior"], auditoria.Detalle["estadoNuevo"]));
        Assert.Equal(["aviso", "aviso", "estado-de-pedido"], entorno.Buzon.Mensajes.Select(m => m.Tipo).ToArray());
        Assert.All(entorno.Buzon.Mensajes, m => Assert.Equal($"traslado:{traslado.Id:D}:cancelado", m.ClaveDeEvento));
        Assert.Empty(entorno.Base.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-18")]
    public async Task LaAnulacionMarcaElTrasladoEnTransito_SinCancelarloNiFallar_YAlRecibirseLasUnidadesQuedanLibres()
    {
        var (entorno, edicion, pedido, traslado) = await Vinculado(EstadoDeTraslado.EnTransito);
        entorno.Poner(entorno.Ayacucho, edicion, 0, 0);

        await entorno.CancelacionPorPedido().AlLiberar(pedido, MotivoDeLiberacionDeReserva.PedidoAnulado, [traslado.Id]);

        var marcado = entorno.Traslados.Almacenado(traslado.Id);
        Assert.Equal((EstadoDeTraslado.EnTransito, true, 3L), (marcado.Estado, marcado.PedidoAnulado, marcado.Version));
        Assert.Empty(entorno.Base.Auditoria.Solicitudes);
        Assert.Empty(entorno.Buzon.Mensajes);

        entorno.Como(BabelHub.Shared.Contratos.RolesDeUsuario.AdministradorCentral);
        var recibido = await entorno.Recibir().Ejecutar(new BabelHub.Inventario.Application.Traslados.RecibirTrasladoEntrada(traslado.Id));
        Assert.True(recibido.EsExito);
        Assert.Equal((1, 0), entorno.Stock(entorno.Ayacucho, edicion));
        Assert.Empty(entorno.Reservas.Recreaciones);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-18")]
    [InlineData(EstadoDeTraslado.Recibido)]
    [InlineData(EstadoDeTraslado.Cancelado)]
    public async Task UnTrasladoYaCerradoNoCambia_YLaAnulacionNoFalla(EstadoDeTraslado estado)
    {
        var (entorno, _, pedido, traslado) = await Vinculado(estado);
        var versionAntes = entorno.Traslados.Almacenado(traslado.Id).Version;

        await entorno.CancelacionPorPedido().AlLiberar(pedido, MotivoDeLiberacionDeReserva.PedidoAnulado, [traslado.Id]);

        Assert.Equal((estado, versionAntes), (entorno.Traslados.Almacenado(traslado.Id).Estado, entorno.Traslados.Almacenado(traslado.Id).Version));
        Assert.Empty(entorno.Base.Auditoria.Solicitudes);
        Assert.Empty(entorno.Buzon.Mensajes);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-15")]
    public async Task RepetirLaAnulacionNoCambiaNadaMas()
    {
        var (entorno, _, pedido, traslado) = await Vinculado(EstadoDeTraslado.Aprobado);
        var puerto = entorno.CancelacionPorPedido();

        await puerto.AlLiberar(pedido, MotivoDeLiberacionDeReserva.PedidoAnulado, [traslado.Id]);
        var version = entorno.Traslados.Almacenado(traslado.Id).Version;
        await puerto.AlLiberar(pedido, MotivoDeLiberacionDeReserva.PedidoAnulado, [traslado.Id, traslado.Id]);

        Assert.Equal(version, entorno.Traslados.Almacenado(traslado.Id).Version);
        Assert.Single(entorno.Base.Auditoria.Solicitudes);
        Assert.Equal(3, entorno.Buzon.Mensajes.Count);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-15")]
    [InlineData(MotivoDeLiberacionDeReserva.CheckoutCancelado)]
    [InlineData(MotivoDeLiberacionDeReserva.PagoVencido)]
    public async Task UnCheckoutCanceladoOUnPagoVencidoNoTocanUnTraslado(MotivoDeLiberacionDeReserva motivo)
    {
        var (entorno, _, pedido, traslado) = await Vinculado(EstadoDeTraslado.Aprobado);

        await entorno.CancelacionPorPedido().AlLiberar(pedido, motivo, [traslado.Id]);

        Assert.Equal(EstadoDeTraslado.Aprobado, entorno.Traslados.Almacenado(traslado.Id).Estado);
        Assert.Equal(0, entorno.Traslados.LecturasBloqueadas);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-15")]
    [InlineData(MotivoDeLiberacionDeReserva.PedidoAnulado)]
    [InlineData(MotivoDeLiberacionDeReserva.CanceladoPorElCliente)]
    [InlineData(MotivoDeLiberacionDeReserva.FallaPosteriorAlPago)]
    [InlineData(MotivoDeLiberacionDeReserva.NoRecogido)]
    public async Task TodosLosMotivosQueAnulanElPedidoCancelanElTraslado(MotivoDeLiberacionDeReserva motivo)
    {
        var (entorno, _, pedido, traslado) = await Vinculado(EstadoDeTraslado.Aprobado);

        await entorno.CancelacionPorPedido().AlLiberar(pedido, motivo, [traslado.Id]);

        Assert.Equal(EstadoDeTraslado.Cancelado, entorno.Traslados.Almacenado(traslado.Id).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-15")]
    public async Task SoloActuaSobreLosTrasladosRecibidosYDeEsePedido()
    {
        var (entorno, edicion, pedido, propio) = await Vinculado(EstadoDeTraslado.Aprobado);
        var deOtroPedido = entorno.Vinculado(edicion, entorno.Cusco, entorno.Ayacucho, 1, Guid.NewGuid(), EstadoDeTraslado.Aprobado);
        var sinPedido = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 1, EstadoDeTraslado.Aprobado);
        var noRecibido = entorno.Vinculado(edicion, entorno.Lima, entorno.Ayacucho, 1, pedido, EstadoDeTraslado.Aprobado);

        await entorno.CancelacionPorPedido().AlLiberar(pedido, MotivoDeLiberacionDeReserva.PedidoAnulado, [propio.Id, deOtroPedido.Id, sinPedido.Id]);

        Assert.Equal(EstadoDeTraslado.Cancelado, entorno.Traslados.Almacenado(propio.Id).Estado);
        Assert.All([deOtroPedido, sinPedido, noRecibido], t => Assert.Equal(EstadoDeTraslado.Aprobado, entorno.Traslados.Almacenado(t.Id).Estado));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-15")]
    public async Task SinTrasladosNoLeeNada()
    {
        var (entorno, _, pedido) = Preparar();

        await entorno.CancelacionPorPedido().AlLiberar(pedido, MotivoDeLiberacionDeReserva.PedidoAnulado, []);

        Assert.Equal(0, entorno.Traslados.LecturasBloqueadas);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task SiNoSePuedeAuditarLaCancelacion_LaAnulacionFalla_ParaQueSeRevierta()
    {
        var (entorno, _, pedido, traslado) = await Vinculado(EstadoDeTraslado.Aprobado);
        entorno.Base.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.CancelacionPorPedido().AlLiberar(pedido, MotivoDeLiberacionDeReserva.PedidoAnulado, [traslado.Id]));

        Assert.Empty(entorno.Buzon.Mensajes);
    }


    [Fact]
    [Trait("Criterio", "AC-TRA-15")]
    public async Task SiElRepositorioNoDevuelveUnTrasladoPedido_PorqueLaPoliticaDeFilasLoOculto_LaAnulacionFallaCerrado()
    {
        var (entorno, _, pedido, traslado) = await Vinculado(EstadoDeTraslado.Aprobado);

        await Assert.ThrowsAsync<AlcanceDeDatosDenegadoException>(() => entorno.CancelacionPorPedido().AlLiberar(pedido, MotivoDeLiberacionDeReserva.PedidoAnulado, [traslado.Id, Guid.NewGuid()]));

        Assert.Equal(EstadoDeTraslado.Aprobado, entorno.Traslados.Almacenado(traslado.Id).Estado);
        Assert.Empty(entorno.Buzon.Mensajes);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-20")]
    [InlineData(RolesDeUsuario.AdministradorCentral, true)]
    [InlineData(RolesDeUsuario.EncargadoDeSucursal, true)]
    [InlineData(RolesDeUsuario.PersonalDeLogistica, true)]
    [InlineData(RolesDeUsuario.Cliente, false)]
    public async Task ElSolicitanteDeUnVinculadoEsSoloPersonal_ElIdDeUnClienteNuncaSeGuarda(string rol, bool quedaGuardado)
    {
        var (entorno, edicion, pedido) = Preparar(ActorDePrueba.DeAdministradorCentral());
        entorno.Como(rol, entorno.Ayacucho);

        var generados = await entorno.Generador().Generar(pedido, entorno.Ayacucho, [new SolicitudDeTrasladoDePedido(entorno.Cusco, edicion, 1)]);

        var solicitante = entorno.Traslados.Almacenado(generados.Single().TrasladoId).SolicitadoPor;
        Assert.Equal(quedaGuardado, solicitante is not null);
    }

    // ---------- Ningún endpoint los alcanza ----------

    [Fact]
    public void LosDosCasosDeUsoSonPuertosDeReservas_NoCasosDeUsoExpuestosNiDependenDelStockNiDeLasReservas()
    {
        Assert.Contains(typeof(IGeneradorDeTrasladosDePedido), typeof(GenerarTrasladosDePedido).GetInterfaces());
        Assert.Contains(typeof(IAlLiberarPedido), typeof(CancelarTrasladosDePedido).GetInterfaces());
        var casosDeUso = new[] { typeof(GenerarTrasladosDePedido), typeof(CancelarTrasladosDePedido) };
        Assert.All(casosDeUso, t => Assert.DoesNotContain(t.GetInterfaces(), i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICasoDeUso<,>)));
        var dependencias = casosDeUso.SelectMany(t => t.GetConstructors().SelectMany(c => c.GetParameters().Select(p => p.ParameterType))).ToList();
        Assert.DoesNotContain(dependencias, d => d == typeof(BabelHub.Inventario.Application.Fuentes.IMovimientosDeStock) || d == typeof(IReservasParaTraslados) || d == typeof(AutorizadorDeTraslados));
    }
}
