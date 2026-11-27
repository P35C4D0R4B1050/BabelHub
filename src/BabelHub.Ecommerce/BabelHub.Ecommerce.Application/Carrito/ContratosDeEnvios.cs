using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Carrito;

/// <summary>
/// Los costos de envío a domicilio por sucursal (CAR-46, CAR-47, CAR-48). Solo la administración central los ve y los cambia, comprobado en la Aplicación; cada cambio se audita en la misma transacción y no altera los totales ya congelados.
/// </summary>
public interface IAdministracionDeEnvios
{
    /// <summary>El costo vigente de cada sucursal registrada, ordenadas por nombre; sin ninguna lista de sucursales, «servicio no disponible».</summary>
    Task<Resultado<CostosDeEnvio>> Listar(CancellationToken cancelacion = default);

    /// <summary>Fija el costo propio de una sucursal registrada: de 0,00 a 500,00 soles con a lo más dos decimales.</summary>
    Task<Resultado<CostoDeEnvioDeSucursal>> Cambiar(CambiarCostoDeEnvioEntrada entrada, CancellationToken cancelacion = default);
}

/// <summary>El costo vigente de una sucursal: propio, o el predeterminado si <see cref="EsPropio"/> es falso.</summary>
public sealed record CostoDeEnvioDeSucursal(string CodigoDeSucursal, string Nombre, decimal Importe, bool EsPropio);

public sealed record CostosDeEnvio(IReadOnlyList<CostoDeEnvioDeSucursal> Sucursales, decimal Predeterminado);

public sealed record CambiarCostoDeEnvioEntrada(string CodigoDeSucursal, decimal Importe);
