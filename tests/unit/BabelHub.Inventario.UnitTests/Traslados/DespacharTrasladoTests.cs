using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Domain.Traslados;
using BabelHub.Inventario.UnitTests.Soporte;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>T07: despachar un traslado aprobado, con y sin pedido vinculado, sin base de datos y con el stock real en memoria.</summary>
public sealed class DespacharTrasladoTests
{
    private static (EntornoDeTraslados Entorno, Guid Edicion) Preparar(int fisicoDelOrigen = 12, int reservadoDelOrigen = 0)
    {
        var entorno = new EntornoDeTraslados(ActorDePrueba.DeAdministradorCentral());
        var edicion = entorno.Impresa();
        entorno.Poner(entorno.Cusco, edicion, fisicoDelOrigen, reservadoDelOrigen);
        entorno.Poner(entorno.Ayacucho, edicion, 0, 0);
        entorno.ComoEncargadoDe(entorno.Cusco);
        return (entorno, edicion);
    }

    // ---------- Sin pedido ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-06")]
    public async Task ElOrigenDespachaUnTrasladoDe4ConVendible12_PasaAEnTransito_YElFisicoDelOrigenBajaEn4()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.Aprobado);

        var resultado = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));

        Assert.Equal(("en-transito", 2L), (resultado.Valor.Estado, resultado.Valor.Version));
        Assert.Equal((8, 0), entorno.Stock(entorno.Cusco, edicion));
        Assert.Equal((0, 0), entorno.Stock(entorno.Ayacucho, edicion));
        var guardado = entorno.Traslados.Almacenado(traslado.Id);
        Assert.Equal((EstadoDeTraslado.EnTransito, EntornoDeTraslados.Ahora), (guardado.Estado, guardado.DespachadoEn));
        var auditoria = Assert.Single(entorno.Base.Auditoria.Solicitudes);
        Assert.Equal((AccionesAuditables.TrasladoDespachado, "aprobado", "en-transito"), (auditoria.Accion, auditoria.Detalle["estadoAnterior"], auditoria.Detalle["estadoNuevo"]));
        Assert.Equal(["aviso", "aviso"], entorno.Buzon.Mensajes.Select(m => m.Tipo).ToArray());
        Assert.Empty(entorno.Reservas.Consumos);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-05")]
    public async Task SinVendibleSuficiente_ElDespachoSeRechazaYElTrasladoSigueAprobado_SinCambiarElStock()
    {
        var (entorno, edicion) = Preparar(fisicoDelOrigen: 3, reservadoDelOrigen: 3);
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 3, EstadoDeTraslado.Aprobado);

        var resultado = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));

        Assert.Equal((CategoriaDeError.Conflicto, ClavesDeTraslados.StockInsuficiente), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Empty(resultado.Error.Errores);
        Assert.Equal((3, 3), entorno.Stock(entorno.Cusco, edicion));
        Assert.Equal((EstadoDeTraslado.Aprobado, 1L), (entorno.Traslados.Almacenado(traslado.Id).Estado, entorno.Traslados.Almacenado(traslado.Id).Version));
        Assert.Empty(entorno.Base.Auditoria.Solicitudes);
        Assert.Empty(entorno.Buzon.Mensajes);
        Assert.Empty(entorno.Base.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-23")]
    public async Task DosTrasladosDe2UnidadesSobreUnVendibleDe3_SoloUnoSeDespacha_ElOtroSeRechazaYSigueAprobado()
    {
        var (entorno, edicion) = Preparar(fisicoDelOrigen: 3);
        var uno = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 2, EstadoDeTraslado.Aprobado);
        var dos = entorno.Reposicion(edicion, entorno.Cusco, entorno.Lima, 2, EstadoDeTraslado.Aprobado);

        var primero = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(uno.Id));
        var segundo = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(dos.Id));

        Assert.True(primero.EsExito);
        Assert.Equal(ClavesDeTraslados.StockInsuficiente, segundo.Error!.Clave);
        Assert.Equal((1, 0), entorno.Stock(entorno.Cusco, edicion));
        Assert.Equal(EstadoDeTraslado.Aprobado, entorno.Traslados.Almacenado(dos.Id).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-23")]
    public async Task UnDespachoRepetidoSeRechazaPorEstadoInvalido_SinDescontarDosVeces()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.Aprobado);

        var primero = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));
        var repetido = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));

        Assert.True(primero.EsExito);
        Assert.Equal((CategoriaDeError.Conflicto, ClavesDeTraslados.TrasladoEstadoInvalido), (repetido.Error!.Categoria, repetido.Error.Clave));
        Assert.Equal((8, 0), entorno.Stock(entorno.Cusco, edicion));
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-02")]
    [InlineData(EstadoDeTraslado.Solicitado)]
    [InlineData(EstadoDeTraslado.EnTransito)]
    [InlineData(EstadoDeTraslado.Recibido)]
    [InlineData(EstadoDeTraslado.Rechazado)]
    [InlineData(EstadoDeTraslado.Cancelado)]
    public async Task SoloSeDespachaUnTrasladoAprobado_YUnoCanceladoYaNoSePuedeDespachar(EstadoDeTraslado estado)
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, estado);

        var resultado = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));

        Assert.Equal(ClavesDeTraslados.TrasladoEstadoInvalido, resultado.Error!.Clave);
        Assert.Equal((12, 0), entorno.Stock(entorno.Cusco, edicion));
        Assert.Equal(estado, entorno.Traslados.Almacenado(traslado.Id).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task ElDestinoLogisticaYClienteNoDespachan_YElStockNoCambia()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.Aprobado);

        entorno.ComoEncargadoDe(entorno.Ayacucho);
        var delDestino = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));
        entorno.Como(RolesDeUsuario.PersonalDeLogistica);
        var deLogistica = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));
        entorno.Como(RolesDeUsuario.Cliente);
        var cliente = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));

        Assert.All([delDestino.Error!, deLogistica.Error!, cliente.Error!], e => Assert.Equal(CategoriaDeError.Prohibido, e.Categoria));
        Assert.Equal(["fuera-de-sucursal", "fuera-de-rol", "fuera-de-rol"], entorno.Base.MotivosDeDenegacion.ToArray());
        Assert.Equal((12, 0), entorno.Stock(entorno.Cusco, edicion));
        Assert.Equal(EstadoDeTraslado.Aprobado, entorno.Traslados.Almacenado(traslado.Id).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task LaAdministracionCentralDespachaCualquierTraslado()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.Aprobado);
        entorno.Como(RolesDeUsuario.AdministradorCentral);

        var resultado = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));

        Assert.Equal("en-transito", resultado.Valor.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-05")]
    public async Task DenegadoPorAlcanceEnAplicarSeTraduceAProhibido_ConUnUnicoEvento_YNoSeConfundeConFaltaDeStock()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.Aprobado);
        entorno.Base.Existencias.FuentesFueraDelAlcance.Add(entorno.Cusco);

        var resultado = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));

        Assert.Equal((CategoriaDeError.Prohibido, AutorizadorDeTraslados.ClaveProhibido), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(["fuera-de-sucursal"], entorno.Base.MotivosDeDenegacion.ToArray());
        Assert.Equal((12, 0), entorno.Stock(entorno.Cusco, edicion));
        Assert.Equal(EstadoDeTraslado.Aprobado, entorno.Traslados.Almacenado(traslado.Id).Estado);
        Assert.Empty(entorno.Base.Auditoria.Solicitudes);
        Assert.Empty(entorno.Buzon.Mensajes);
    }

    // ---------- Vinculado a un pedido ----------

    private static (EntornoDeTraslados Entorno, Guid Edicion, Guid Pedido, Traslado Traslado) PrepararVinculado()
    {
        var (entorno, edicion) = Preparar(fisicoDelOrigen: 1, reservadoDelOrigen: 1);
        var pedido = Guid.NewGuid();
        var traslado = entorno.Vinculado(edicion, entorno.Cusco, entorno.Ayacucho, 1, pedido, EstadoDeTraslado.Aprobado);
        entorno.Reservas.Confirmada = new ReservaConfirmadaDeLaFuente(pedido, entorno.Cusco, edicion, 1, null);
        return (entorno, edicion, pedido, traslado);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-12")]
    public async Task UnVinculadoConsumeLaReservaConfirmadaDeSuPedido_ElOrigenQuedaEnFisico0YReservado0_YSeInformaAlPedido()
    {
        var (entorno, edicion, pedido, traslado) = PrepararVinculado();

        var resultado = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));

        Assert.Equal("en-transito", resultado.Valor.Estado);
        Assert.Equal((0, 0), entorno.Stock(entorno.Cusco, edicion));
        Assert.Equal([(pedido, entorno.Cusco, edicion)], entorno.Reservas.Consumos);
        Assert.Equal([pedido], entorno.Bloqueos.Pedidos);
        Assert.Equal(["aviso", "aviso", "estado-de-pedido"], entorno.Buzon.Mensajes.Select(m => m.Tipo).ToArray());
        Assert.All(entorno.Buzon.Mensajes, m => Assert.Equal($"traslado:{traslado.Id:D}:en-transito", m.ClaveDeEvento));
        Assert.Equal("ayacucho", entorno.Buzon.Mensajes.Single(m => m.Tipo == "estado-de-pedido").SucursalCodigo);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-12")]
    public async Task UnVinculadoNoTocaElStockDirectamente_ElVendibleDelOrigenNoCambiaPorElDespacho()
    {
        var (entorno, edicion, _, traslado) = PrepararVinculado();
        entorno.Poner(entorno.Cusco, edicion, 5, 1);
        var vendibleAntes = entorno.Stock(entorno.Cusco, edicion).Fisico - entorno.Stock(entorno.Cusco, edicion).Reservado;

        await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));

        var (fisico, reservado) = entorno.Stock(entorno.Cusco, edicion);
        Assert.Equal(vendibleAntes, fisico - reservado);
        Assert.Empty(entorno.Base.Existencias.Movimientos);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-05")]
    public async Task UnVinculadoCuyaReservaConfirmadaYaNoExiste_SeRechazaYSigueAprobado()
    {
        var (entorno, edicion, _, traslado) = PrepararVinculado();
        entorno.Reservas.Confirmada = null;

        var resultado = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));

        Assert.Equal((CategoriaDeError.Conflicto, ClavesDeTraslados.StockInsuficiente), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Empty(entorno.Reservas.Consumos);
        Assert.Equal((1, 1), entorno.Stock(entorno.Cusco, edicion));
        Assert.Equal(EstadoDeTraslado.Aprobado, entorno.Traslados.Almacenado(traslado.Id).Estado);
        Assert.Empty(entorno.Base.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-05")]
    public async Task UnaReservaDeOtraCantidadNoSirveParaElTraslado()
    {
        var (entorno, edicion, pedido, traslado) = PrepararVinculado();
        entorno.Reservas.Confirmada = new ReservaConfirmadaDeLaFuente(pedido, entorno.Cusco, edicion, 2, null);

        var resultado = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));

        Assert.Equal(ClavesDeTraslados.StockInsuficiente, resultado.Error!.Clave);
        Assert.Empty(entorno.Reservas.Consumos);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-22")]
    [InlineData(ResultadoDeMovimientoDeReserva.EstadoInvalido)]
    [InlineData(ResultadoDeMovimientoDeReserva.NoEncontrada)]
    public async Task SiLaReservaSeLiberaAntesDeConsumirla_ElDespachoSeRechazaYElTrasladoSigueAprobado(ResultadoDeMovimientoDeReserva resultadoDeReservas)
    {
        var (entorno, edicion, _, traslado) = PrepararVinculado();
        entorno.Reservas.ResultadoAlConsumir = resultadoDeReservas;

        var resultado = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));

        Assert.Equal(ClavesDeTraslados.StockInsuficiente, resultado.Error!.Clave);
        Assert.Equal((1, 1), entorno.Stock(entorno.Cusco, edicion));
        Assert.Equal(EstadoDeTraslado.Aprobado, entorno.Traslados.Almacenado(traslado.Id).Estado);
        Assert.Empty(entorno.Buzon.Mensajes);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-05")]
    public async Task DenegadoPorAlcanceDeReservas_SeTraduceAProhibidoConUnUnicoEvento_ElTrasladoSigueAprobadoYNadaSeAudita()
    {
        var (entorno, edicion, _, traslado) = PrepararVinculado();
        entorno.Reservas.ResultadoAlConsumir = ResultadoDeMovimientoDeReserva.DeniegadoPorAlcance;

        var resultado = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));

        Assert.Equal((CategoriaDeError.Prohibido, AutorizadorDeTraslados.ClaveProhibido), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(["fuera-de-sucursal"], entorno.Base.MotivosDeDenegacion.ToArray());
        Assert.Equal(EstadoDeTraslado.Aprobado, entorno.Traslados.Almacenado(traslado.Id).Estado);
        Assert.Empty(entorno.Base.Auditoria.Solicitudes);
        Assert.Equal((1, 1), entorno.Stock(entorno.Cusco, edicion));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-22")]
    public async Task ElPedidoSeBloqueaAntesQueLaFilaDelTrasladoYAntesDeMoverElStock()
    {
        var (entorno, _, _, traslado) = PrepararVinculado();

        await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));

        var orden = entorno.Base.Bitacora.Llamadas.Where(l => l.StartsWith("bloqueo", StringComparison.Ordinal) || l.StartsWith("reservas", StringComparison.Ordinal) || l.StartsWith("auditoria", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["bloqueo.Pedido", "reservas.ConsultarConfirmada", "reservas.ConsumirEnElOrigen", "auditoria.TrasladoDespachado"], orden);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-22")]
    public async Task UnDespachoQuePierdeFrenteALaAnulacionDelPedidoRecibeEstadoInvalido_SinTocarLaReserva()
    {
        var (entorno, _, _, traslado) = PrepararVinculado();
        var cancelado = entorno.Traslados.Almacenado(traslado.Id);
        cancelado.AnularPorPedido(EntornoDeTraslados.Ahora);
        entorno.Traslados.Sembrar(cancelado);

        var resultado = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));

        Assert.Equal((CategoriaDeError.Conflicto, ClavesDeTraslados.TrasladoEstadoInvalido), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Empty(entorno.Reservas.Consumos);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task SiNoSePuedeAuditarElDespacho_LaOperacionFalla_ParaQueSeRevierta()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.Aprobado);
        entorno.Base.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id)));

        Assert.Empty(entorno.Buzon.Mensajes);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-17")]
    public async Task UnaReposicionCanceladaYaNoSePuedeDespachar()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.Aprobado);
        await entorno.Cancelar().Ejecutar(new CancelarTrasladoEntrada(traslado.Id, "error de pedido"));

        var despacho = await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));

        Assert.Equal(ClavesDeTraslados.TrasladoEstadoInvalido, despacho.Error!.Clave);
        Assert.Equal((12, 0), entorno.Stock(entorno.Cusco, edicion));
    }
}
