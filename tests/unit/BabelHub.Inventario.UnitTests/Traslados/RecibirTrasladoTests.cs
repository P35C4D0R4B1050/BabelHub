using System.Text.Json;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Domain.Traslados;
using BabelHub.Inventario.UnitTests.Soporte;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>T08: recibir un traslado en tránsito, con y sin pedido vinculado, sin base de datos y con el stock real en memoria.</summary>
public sealed class RecibirTrasladoTests
{
    private static (EntornoDeTraslados Entorno, Guid Edicion) Preparar()
    {
        var entorno = new EntornoDeTraslados(ActorDePrueba.DeAdministradorCentral());
        var edicion = entorno.Impresa();
        entorno.Poner(entorno.Cusco, edicion, 8, 0);
        entorno.Poner(entorno.Ayacucho, edicion, 2, 0);
        entorno.ComoEncargadoDe(entorno.Ayacucho);
        return (entorno, edicion);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-07")]
    public async Task ElDestinoRecibeUnTrasladoEnTransitoDe4_PasaARecibido_YElFisicoDelDestinoSubeEn4()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.EnTransito);

        var resultado = await entorno.Recibir().Ejecutar(new RecibirTrasladoEntrada(traslado.Id));

        Assert.Equal(("recibido", 3L), (resultado.Valor.Estado, resultado.Valor.Version));
        Assert.Equal((6, 0), entorno.Stock(entorno.Ayacucho, edicion));
        Assert.Equal((8, 0), entorno.Stock(entorno.Cusco, edicion));
        var guardado = entorno.Traslados.Almacenado(traslado.Id);
        Assert.Equal((EstadoDeTraslado.Recibido, EntornoDeTraslados.Ahora), (guardado.Estado, guardado.RecibidoEn));
        var auditoria = Assert.Single(entorno.Base.Auditoria.Solicitudes);
        Assert.Equal((AccionesAuditables.TrasladoRecibido, "en-transito", "recibido"), (auditoria.Accion, auditoria.Detalle["estadoAnterior"], auditoria.Detalle["estadoNuevo"]));
        Assert.Equal(["aviso", "aviso"], entorno.Buzon.Mensajes.Select(m => m.Tipo).ToArray());
        Assert.Empty(entorno.Reservas.Recreaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-13")]
    public async Task UnVinculadoAUnPedidoActivoRecreaLaReservaEnElDestino_ElVendibleNoCambia_YSeInformaAlPedido()
    {
        var (entorno, edicion) = Preparar();
        entorno.Poner(entorno.Ayacucho, edicion, 0, 0);
        var pedido = Guid.NewGuid();
        var traslado = entorno.Vinculado(edicion, entorno.Cusco, entorno.Ayacucho, 1, pedido, EstadoDeTraslado.EnTransito);

        var resultado = await entorno.Recibir().Ejecutar(new RecibirTrasladoEntrada(traslado.Id));

        Assert.Equal("recibido", resultado.Valor.Estado);
        Assert.Equal([(pedido, entorno.Ayacucho, edicion, 1, traslado.Id)], entorno.Reservas.Recreaciones);
        var (fisico, reservado) = entorno.Stock(entorno.Ayacucho, edicion);
        Assert.Equal((1, 1, 0), (fisico, reservado, fisico - reservado));
        Assert.Equal([pedido], entorno.Bloqueos.Pedidos);
        Assert.Equal(["aviso", "aviso", "estado-de-pedido"], entorno.Buzon.Mensajes.Select(m => m.Tipo).ToArray());
        var estado = JsonDocument.Parse(entorno.Buzon.Mensajes.Single(m => m.Tipo == "estado-de-pedido").Carga).RootElement;
        Assert.Equal(("recibido", pedido.ToString("D")), (estado.GetProperty("estado").GetString(), estado.GetProperty("pedidoId").GetString()));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-16")]
    public async Task UnVinculadoDeUnPedidoAnulado_SumaAlFisicoDelDestinoYLasUnidadesQuedanLibres_SinAvisoDeEstadoDelPedido()
    {
        var (entorno, edicion) = Preparar();
        entorno.Poner(entorno.Ayacucho, edicion, 0, 0);
        var traslado = entorno.Vinculado(edicion, entorno.Cusco, entorno.Ayacucho, 3, Guid.NewGuid(), EstadoDeTraslado.EnTransito);
        var marcado = entorno.Traslados.Almacenado(traslado.Id);
        marcado.AnularPorPedido(EntornoDeTraslados.Ahora);
        entorno.Traslados.Sembrar(marcado);

        var resultado = await entorno.Recibir().Ejecutar(new RecibirTrasladoEntrada(traslado.Id));

        Assert.Equal("recibido", resultado.Valor.Estado);
        Assert.Equal((3, 0), entorno.Stock(entorno.Ayacucho, edicion));
        Assert.Empty(entorno.Reservas.Recreaciones);
        Assert.Equal(["aviso", "aviso"], entorno.Buzon.Mensajes.Select(m => m.Tipo).ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-23")]
    public async Task UnaRecepcionRepetidaNoSumaDosVeces_LaSegundaRecibeEstadoInvalido()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.EnTransito);

        var primera = await entorno.Recibir().Ejecutar(new RecibirTrasladoEntrada(traslado.Id));
        var segunda = await entorno.Recibir().Ejecutar(new RecibirTrasladoEntrada(traslado.Id));

        Assert.True(primera.EsExito);
        Assert.Equal((CategoriaDeError.Conflicto, ClavesDeTraslados.TrasladoEstadoInvalido), (segunda.Error!.Categoria, segunda.Error.Clave));
        Assert.Equal((6, 0), entorno.Stock(entorno.Ayacucho, edicion));
        Assert.Single(entorno.Base.Auditoria.Solicitudes);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-02")]
    [InlineData(EstadoDeTraslado.Solicitado)]
    [InlineData(EstadoDeTraslado.Aprobado)]
    [InlineData(EstadoDeTraslado.Rechazado)]
    [InlineData(EstadoDeTraslado.Cancelado)]
    public async Task SoloSeRecibeUnTrasladoEnTransito(EstadoDeTraslado estado)
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, estado);

        var resultado = await entorno.Recibir().Ejecutar(new RecibirTrasladoEntrada(traslado.Id));

        Assert.Equal(ClavesDeTraslados.TrasladoEstadoInvalido, resultado.Error!.Clave);
        Assert.Equal((2, 0), entorno.Stock(entorno.Ayacucho, edicion));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-13")]
    public async Task SiLaReservaYaNoSePuedeRecrear_LaRecepcionFallaPorEstadoInvalido_NoPorFaltaDeStock_YNadaSeGuarda()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Vinculado(edicion, entorno.Cusco, entorno.Ayacucho, 1, Guid.NewGuid(), EstadoDeTraslado.EnTransito);
        entorno.Reservas.ResultadoAlRecrear = ResultadoDeMovimientoDeReserva.EstadoInvalido;

        var resultado = await entorno.Recibir().Ejecutar(new RecibirTrasladoEntrada(traslado.Id));

        Assert.Equal((CategoriaDeError.Conflicto, ClavesDeTraslados.TrasladoEstadoInvalido), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(EstadoDeTraslado.EnTransito, entorno.Traslados.Almacenado(traslado.Id).Estado);
        Assert.Empty(entorno.Base.Auditoria.Solicitudes);
        Assert.Empty(entorno.Buzon.Mensajes);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-07")]
    public async Task DenegadoPorAlcanceDeReservas_SeTraduceAProhibidoConUnUnicoEvento()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Vinculado(edicion, entorno.Cusco, entorno.Ayacucho, 1, Guid.NewGuid(), EstadoDeTraslado.EnTransito);
        entorno.Reservas.ResultadoAlRecrear = ResultadoDeMovimientoDeReserva.DeniegadoPorAlcance;

        var resultado = await entorno.Recibir().Ejecutar(new RecibirTrasladoEntrada(traslado.Id));

        Assert.Equal((CategoriaDeError.Prohibido, AutorizadorDeTraslados.ClaveProhibido), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(["fuera-de-sucursal"], entorno.Base.MotivosDeDenegacion.ToArray());
        Assert.Equal(EstadoDeTraslado.EnTransito, entorno.Traslados.Almacenado(traslado.Id).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-07")]
    public async Task DenegadoPorAlcanceEnAplicar_SeTraduceAProhibido_YNoSeConfundeConFaltaDeStock()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.EnTransito);
        entorno.Base.Existencias.FuentesFueraDelAlcance.Add(entorno.Ayacucho);

        var resultado = await entorno.Recibir().Ejecutar(new RecibirTrasladoEntrada(traslado.Id));

        Assert.Equal((CategoriaDeError.Prohibido, AutorizadorDeTraslados.ClaveProhibido), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(["fuera-de-sucursal"], entorno.Base.MotivosDeDenegacion.ToArray());
        Assert.Equal((2, 0), entorno.Stock(entorno.Ayacucho, edicion));
        Assert.Equal(EstadoDeTraslado.EnTransito, entorno.Traslados.Almacenado(traslado.Id).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task SoloElDestinoYLaAdministracionRecibenElOrigenLogisticaYClienteNo()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.EnTransito);

        entorno.ComoEncargadoDe(entorno.Cusco);
        var delOrigen = await entorno.Recibir().Ejecutar(new RecibirTrasladoEntrada(traslado.Id));
        entorno.Como(RolesDeUsuario.PersonalDeLogistica);
        var deLogistica = await entorno.Recibir().Ejecutar(new RecibirTrasladoEntrada(traslado.Id));
        entorno.Como(RolesDeUsuario.Cliente);
        var cliente = await entorno.Recibir().Ejecutar(new RecibirTrasladoEntrada(traslado.Id));

        Assert.All([delOrigen.Error!, deLogistica.Error!, cliente.Error!], e => Assert.Equal(CategoriaDeError.Prohibido, e.Categoria));
        Assert.Equal(["fuera-de-sucursal", "fuera-de-rol", "fuera-de-rol"], entorno.Base.MotivosDeDenegacion.ToArray());
        Assert.Equal((2, 0), entorno.Stock(entorno.Ayacucho, edicion));

        entorno.Como(RolesDeUsuario.AdministradorCentral);
        Assert.True((await entorno.Recibir().Ejecutar(new RecibirTrasladoEntrada(traslado.Id))).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task SiNoSePuedeAuditarLaRecepcion_LaOperacionFalla_ParaQueSeRevierta()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.EnTransito);
        entorno.Base.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.Recibir().Ejecutar(new RecibirTrasladoEntrada(traslado.Id)));

        Assert.Empty(entorno.Buzon.Mensajes);
    }
}
