using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Reservas;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>T05: el contrato público de <c>Reservas</c> usa las acciones de auditoría del catálogo (sin crear propias) y no imprime identificadores de titulares.</summary>
public sealed class ContratoDeReservasTests
{
    private static readonly AccionAuditable[] AccionesQueUsaElModulo =
    [
        AccionesAuditables.ReservaConfirmada,
        AccionesAuditables.ReservaLiberada,
        AccionesAuditables.ReservaExpirada,
        AccionesAuditables.StockDescontadoPorEntrega,
        AccionesAuditables.StockDescontadoPorReparto,
        AccionesAuditables.BajaDeTitularProcesada,
    ];

    [Fact]
    [Trait("Criterio", "AC-SEL-32")]
    public void Las_acciones_de_auditoria_del_modulo_estan_todas_en_el_catalogo_con_su_marca_y_sus_claves()
    {
        var catalogo = CatalogoDeAccionesDeAuditoria.Crear();

        foreach (var accion in AccionesQueUsaElModulo)
        {
            Assert.True(catalogo.TryObtener(accion.Identificador, out var declarada), accion.Identificador);
            Assert.Equal(accion.Aislada, declarada!.Aislada);
        }

        // La baja es una acción de integración con el titular como objeto; los movimientos de stock llevan el pedido.
        Assert.Equal(["titular"], catalogo.TryObtener("BajaDeTitularProcesada", out var baja) ? baja!.TiposDeObjeto : []);
        Assert.Equal(["pedido"], catalogo.TryObtener("ReservaExpirada", out var expirada) ? expirada!.TiposDeObjeto : []);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public void Los_modelos_que_llevan_el_identificador_de_un_titular_no_lo_imprimen()
    {
        var titular = Guid.NewGuid();

        Assert.DoesNotContain(titular.ToString(), new ConsultarPendientesEntrada(titular).ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(titular.ToString(), new ProcesarBajaDeTitularEntrada(titular).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-09")]
    public void Una_reserva_devuelta_por_una_repeticion_se_distingue_de_una_nueva()
    {
        var reserva = new ReservaDelPedido(Guid.NewGuid(), EstadoDeReserva.Pendiente, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(15), [], []);

        Assert.False(reserva.EsRepeticion);
        Assert.True((reserva with { EsRepeticion = true }).EsRepeticion);
    }
}
