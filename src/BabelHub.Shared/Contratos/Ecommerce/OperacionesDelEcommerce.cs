namespace BabelHub.Shared.Contratos.Ecommerce;

/// <summary>
/// Operaciones que publica el E-commerce (<c>/api/ecommerce/v1/integraciones</c>) y consume el Inventario. Las dos usan el alcance <c>solicitar-aviso</c>,
/// la única credencial del sentido Inventario → E-commerce (<c>transversal-seguridad</c> T08).
/// </summary>
public static class OperacionesDelEcommerce
{
    private const string Raiz = RutasDeServicio.Ecommerce;

    public static IReadOnlyList<OperacionDeContrato> Todas { get; } =
    [
        new(
            "ecommerce.avisos.solicitar-del-inventario", "POST", Raiz + "/integraciones/inventario/avisos", AlcancesDeCredencial.SolicitarAviso,
            false, false, false, 202, typeof(SolicitudDeAvisoDeInventarioDto), null,
            [CodigosDeErrorDelEcommerce.NoAutenticado, CodigosDeErrorDelEcommerce.ValidacionInvalida, CodigosDeErrorDelEcommerce.LimiteExcedido]),
        new(
            "ecommerce.traslados.informar-estado", "POST", Raiz + "/integraciones/inventario/traslados", AlcancesDeCredencial.SolicitarAviso,
            false, false, false, 204, typeof(EstadoDeTrasladoDePedidoDto), null,
            [
                CodigosDeErrorDelEcommerce.NoAutenticado, CodigosDeErrorDelEcommerce.ValidacionInvalida, CodigosDeErrorDelEcommerce.NoEncontrado,
                CodigosDeErrorDelEcommerce.ServicioNoDisponible,
            ]),
    ];
}
