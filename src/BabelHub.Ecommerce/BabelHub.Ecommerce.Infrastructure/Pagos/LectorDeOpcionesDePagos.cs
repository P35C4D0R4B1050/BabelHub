using Microsoft.Extensions.Configuration;

namespace BabelHub.Ecommerce.Infrastructure.Pagos;

/// <summary>
/// Lee de la configuración lo que decide el arranque de Pagos (PAG-12, T27): <c>Pagos:Pasarela</c> (<c>simulador</c> o <c>culqi</c>) y los secretos <c>BABELHUB__Pagos__LlavePrivada</c>, <c>BABELHUB__Pagos__ClaveDeIntentos</c> y <c>BABELHUB__Pagos__ClaveDeIntentosAnterior</c>
/// (el valor anterior sirve para rotar la clave: los tokens emitidos con él siguen valiendo hasta que vencen). Solo lee y entrega las opciones; <see cref="GuardaDeArranqueDePagos"/> decide si son válidas para el entorno y el modo. <b>En un modo que prohíbe llamadas
/// reales (<c>Restauracion</c> y <c>Ensayo</c>) el adaptador de la pasarela es siempre el nulo</b> y no se pide la llave privada: nunca se llama a un proveedor.
/// </summary>
public static class LectorDeOpcionesDePagos
{
    public static OpcionesDeArranqueDePagos Leer(IConfiguration configuracion, string entorno, bool sinLlamadasReales, bool pedidosDisponible)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var pasarela = Texto(configuracion, "Pagos:Pasarela");
        var llave = sinLlamadasReales ? null : Texto(configuracion, "Pagos:LlavePrivada");
        return new OpcionesDeArranqueDePagos(
            pasarela,
            entorno ?? string.Empty,
            sinLlamadasReales,
            PasarelaEsNula: sinLlamadasReales,
            pedidosDisponible,
            llave,
            Texto(configuracion, "Pagos:ClaveDeIntentos"),
            Texto(configuracion, "Pagos:ClaveDeIntentosAnterior"));
    }

    private static string? Texto(IConfiguration configuracion, string ruta) => string.IsNullOrWhiteSpace(configuracion[ruta]) ? null : configuracion[ruta];
}
