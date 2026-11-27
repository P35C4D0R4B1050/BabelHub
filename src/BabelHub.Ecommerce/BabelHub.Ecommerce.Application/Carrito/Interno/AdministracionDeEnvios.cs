using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Domain.Carrito;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

/// <summary>
/// <see cref="IAdministracionDeEnvios"/> (T45): el rol se comprueba antes de leer o validar nada; la sucursal, contra la misma lista conocida que alimenta la ficha; y el cambio escribe la tarifa y su auditoría (con el importe que de verdad
/// reemplazó) en una sola transacción, así que sin auditoría no cambia nada.
/// </summary>
internal sealed class AdministracionDeEnvios(
    AutorizadorDelCarrito autorizador,
    IPreferenciaDeSucursal sucursales,
    IRepositorioDeTarifasDeEnvio tarifas,
    ICostoDeEnvioConfigurado predeterminado,
    IUnidadDeTrabajo unidad,
    IReloj reloj,
    IAuditoriaDelCarrito auditoria) : IAdministracionDeEnvios
{
    public async Task<Resultado<CostosDeEnvio>> Listar(CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<CostosDeEnvio>.Fallo(rechazo);
        }

        var conocidas = await sucursales.Resolver(null, cancelacion).ConfigureAwait(false);
        if (!conocidas.ListaDisponible)
        {
            return ServiciosDelCarrito.ServicioNoDisponible<CostosDeEnvio>();
        }

        var propias = await tarifas.Leer(cancelacion).ConfigureAwait(false);
        var porDefecto = predeterminado.Valor.Valor;
        return Resultado<CostosDeEnvio>.Exito(new CostosDeEnvio(
            [
                .. conocidas.Sucursales
                    .OrderBy(s => s.Nombre, StringComparer.Ordinal)
                    .ThenBy(s => s.Codigo, StringComparer.Ordinal)
                    .Select(s => new CostoDeEnvioDeSucursal(s.Codigo, s.Nombre, propias.TryGetValue(s.Codigo, out var propio) ? propio.Valor : porDefecto, propias.ContainsKey(s.Codigo))),
            ],
            porDefecto));
    }

    public async Task<Resultado<CostoDeEnvioDeSucursal>> Cambiar(CambiarCostoDeEnvioEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<CostoDeEnvioDeSucursal>.Fallo(rechazo);
        }

        if (string.IsNullOrWhiteSpace(entrada.CodigoDeSucursal))
        {
            return Invalido("sucursal", ConstructorDeValidacion.Obligatorio);
        }

        if (entrada.Importe < 0 || entrada.Importe > CostoDeEnvio.MaximoPorSucursal)
        {
            return Invalido("importe", ConstructorDeValidacion.FueraDeRango);
        }

        if (!CostoDeEnvio.TryCrear(entrada.Importe, out var costo))
        {
            return Invalido("importe", ConstructorDeValidacion.FormatoInvalido);
        }

        var conocidas = await sucursales.Resolver(null, cancelacion).ConfigureAwait(false);
        if (!conocidas.ListaDisponible)
        {
            return ServiciosDelCarrito.ServicioNoDisponible<CostoDeEnvioDeSucursal>();
        }

        var sucursal = conocidas.Sucursales.FirstOrDefault(s => string.Equals(s.Codigo, entrada.CodigoDeSucursal, StringComparison.Ordinal));
        if (sucursal is null)
        {
            return Invalido("sucursal", ConstructorDeValidacion.FormatoInvalido);
        }

        await unidad.Ejecutar(
            async token =>
            {
                var anterior = await tarifas.Fijar(sucursal.Codigo, costo, reloj.Ahora, token).ConfigureAwait(false);
                await auditoria.TarifaDeEnvioCambiada(sucursal.Codigo, (anterior ?? predeterminado.Valor).Valor, costo.Valor, token).ConfigureAwait(false);
                return true;
            },
            cancelacion).ConfigureAwait(false);

        return Resultado<CostoDeEnvioDeSucursal>.Exito(new CostoDeEnvioDeSucursal(sucursal.Codigo, sucursal.Nombre, costo.Valor, EsPropio: true));
    }

    private static Resultado<CostoDeEnvioDeSucursal> Invalido(string campo, string clave) =>
        ServiciosDelCarrito.Fallo<CostoDeEnvioDeSucursal>(CategoriaDeError.Validacion, ClavesDelCarrito.ValidacionFallida, [new ErrorDeCampo(campo, clave)]);
}
