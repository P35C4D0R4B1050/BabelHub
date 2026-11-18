using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Domain.Traslados;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>La auditoría <b>real</b> del Inventario (el catálogo completo): lo que los casos de uso piden registrar lo acepta el catálogo y no guarda texto libre de un empleado (TRA-13, D07).</summary>
public sealed class AuditoriaRealDeTrasladosTests
{
    private const string Centinela = "CENTINELA-SECRETO-777";

    private static (EntornoDeTraslados Entorno, Guid Edicion) Preparar()
    {
        var entorno = new EntornoDeTraslados(BabelHub.Inventario.UnitTests.Soporte.ActorDePrueba.DeAdministradorCentral());
        var edicion = entorno.Impresa();
        entorno.Poner(entorno.Cusco, edicion, 12, 0);
        entorno.Poner(entorno.Ayacucho, edicion, 0, 0);
        return (entorno, edicion);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task ElCatalogoAceptaLaAccionYLaListaBlanca_DeCadaCambioDeEstado_ConLasSucursalesEnLasColumnas()
    {
        var (entorno, edicion) = Preparar();
        var (auditoria, almacen) = entorno.Base.AuditoriaReal();
        entorno.AuditoriaDeLaPrueba = auditoria;

        var solicitado = (await entorno.Solicitar().Ejecutar(new SolicitarTrasladoEntrada(edicion, 4, "cusco", "ayacucho", Centinela))).Valor;
        await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(solicitado.TrasladoId));
        await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(solicitado.TrasladoId));
        await entorno.Guia().Ejecutar(new RegistrarGuiaDeTrasladoEntrada(solicitado.TrasladoId, "CA-771204"));
        await entorno.Recibir().Ejecutar(new RecibirTrasladoEntrada(solicitado.TrasladoId));

        Assert.Equal(
            ["TrasladoSolicitado", "TrasladoAprobado", "TrasladoDespachado", "GuiaDeTrasladoRegistrada", "TrasladoRecibido"],
            almacen.Registros.Select(r => r.Accion).ToArray());
        Assert.All(almacen.Registros, r =>
        {
            Assert.Equal(solicitado.TrasladoId.ToString("D"), r.ObjetoId);
            Assert.Equal(entorno.Cusco.ToString("D"), r.SucursalOrigen);
            Assert.Equal(entorno.Ayacucho.ToString("D"), r.SucursalDestino);
            Assert.DoesNotContain(Centinela, r.Detalle.Texto, StringComparison.Ordinal);
            Assert.DoesNotContain(Centinela, r.ToString(), StringComparison.Ordinal);
        });

        var aprobado = almacen.Registros[1].Detalle.Valores;
        Assert.Equal(("solicitado", "aprobado", "false", "4"), (aprobado["estadoAnterior"], aprobado["estadoNuevo"], aprobado["vinculado"], aprobado["cantidad"]));
        Assert.Equal(edicion.ToString("D"), aprobado["edicionId"]);

        var guia = almacen.Registros[3].Detalle.Valores;
        Assert.Equal(("CA-771204", "false"), (guia["guiaNueva"], guia["reemplazoOtraGuia"]));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task LaAuditoriaDeUnTrasladoVinculadoLlevaElPedidoOpaco_YLaDeUnRechazoNoLlevaElMotivo()
    {
        var (entorno, edicion) = Preparar();
        var (auditoria, almacen) = entorno.Base.AuditoriaReal();
        entorno.AuditoriaDeLaPrueba = auditoria;
        var pedido = Guid.NewGuid();
        var vinculado = entorno.Vinculado(edicion, entorno.Cusco, entorno.Ayacucho, 1, pedido);
        var reposicion = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 2);

        await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(vinculado.Id));
        await entorno.Rechazar().Ejecutar(new RechazarTrasladoEntrada(reposicion.Id, Centinela));

        Assert.Equal(pedido.ToString("D"), almacen.Registros[0].Detalle.Valores["pedidoId"]);
        Assert.Equal("true", almacen.Registros[0].Detalle.Valores["vinculado"]);
        Assert.Equal("TrasladoRechazado", almacen.Registros[1].Accion);
        Assert.DoesNotContain(Centinela, almacen.Registros[1].Detalle.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain(Centinela, almacen.Registros[1].ToString(), StringComparison.Ordinal);
    }
}
