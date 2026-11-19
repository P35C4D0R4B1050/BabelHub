using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Infrastructure.Identidad;

/// <summary>
/// La entrega de la baja de un titular al Inventario (T36; IDE-64, AC-IDE-53): llama a <c>DarDeBajaAlTitular</c> con solo el identificador opaco y la credencial de servicio del alcance <c>baja-de-titular</c>
/// (la pone <see cref="IClienteDelInventario"/>). <b>Verdadero solo si el Inventario aceptó la baja</b>; un «no disponible», un límite o un rechazo se reintentan sin deshacer la cancelación (el Inventario la trata de
/// forma idempotente: repetirla no cambia nada). Nunca lanza por una falla del Inventario.
/// </summary>
public sealed class InventarioEntregaDeBajas(IClienteDelInventario inventario) : IEntregaDeBajas
{
    public async Task<bool> Entregar(Guid identificadorOpaco, CancellationToken cancelacion = default)
    {
        if (identificadorOpaco == Guid.Empty)
        {
            throw new ArgumentException("La baja necesita el identificador opaco del titular.", nameof(identificadorOpaco));
        }

        var resultado = await inventario.DarDeBajaAlTitular(identificadorOpaco, cancelacion).ConfigureAwait(false);
        return resultado.Exitosa;
    }
}

/// <summary>
/// Los pendientes de una cuenta en el Inventario (T36; IDE-27): las reservas activas del titular. <b>Si no se puede contar lanza</b> (el Inventario no responde, supera su límite o rechaza): la cancelación falla
/// cerrada y nunca se asume cero.
/// </summary>
public sealed class InventarioPendientesDeCuenta(IClienteDelInventario inventario) : IPendientesDeCuenta
{
    public async Task<int> Contar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        var resultado = await inventario.ConsultarPendientes(usuarioId, cancelacion).ConfigureAwait(false);
        return resultado.Exitosa
            ? resultado.Valor.ReservasActivas
            : throw new PendientesNoDisponiblesException("El Inventario no pudo contar los pendientes del titular.");
    }
}

/// <summary>
/// Las sucursales que existen según el Inventario (<c>inventario.sucursales.listar</c>, D20): el código se compara sin distinguir mayúsculas. Un Inventario que no responde, supera su límite o rechaza es <see cref="ExistenciaDeSucursal.NoDisponible"/>:
/// nunca se asume que una sucursal existe.
/// </summary>
public sealed class InventarioCatalogoDeSucursales(IClienteDelInventario inventario) : ICatalogoDeSucursales
{
    public async Task<ExistenciaDeSucursal> Existe(string codigoDeSucursal, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigoDeSucursal);
        var lista = await inventario.ListarSucursales(cancelacion).ConfigureAwait(false);
        if (!lista.Exitosa)
        {
            return ExistenciaDeSucursal.NoDisponible;
        }

        return lista.Valor.Any(s => string.Equals(s.Codigo, codigoDeSucursal.Trim(), StringComparison.OrdinalIgnoreCase)) ? ExistenciaDeSucursal.Existe : ExistenciaDeSucursal.NoExiste;
    }
}
