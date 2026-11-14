namespace BabelHub.Inventario.Application.Plataforma.Interno;

/// <summary>
/// Decorador de un caso de uso de <b>solo lectura</b>: lo ejecuta entero dentro de una lectura coherente (<c>REPEATABLE READ</c> de solo lectura), de modo que todo lo que lee el caso de uso (el stock y las
/// reservas vencidas que informa <c>IReservasDeStock</c>, las fuentes y las fichas) ve la misma instantánea (<c>inventario-fuentes</c> D06, spec §6). No se usa <c>TransaccionPorCasoDeUso</c> para las
/// lecturas: esa abre una transacción <c>READ COMMITTED</c> y <see cref="ILecturaCoherente"/> dentro de una transacción activa hereda su aislamiento, con lo que el vendible volvería a leerse en dos
/// instantes distintos. Es <c>public</c> solo para que la composición de dependencias de la Infraestructura lo registre (D25).
/// </summary>
public sealed class LecturaCoherentePorCasoDeUso<TEntrada, TSalida>(ICasoDeUso<TEntrada, TSalida> interno, ILecturaCoherente lectura)
    : ICasoDeUso<TEntrada, TSalida>
{
    public Task<Resultado<TSalida>> Ejecutar(TEntrada entrada, CancellationToken cancelacion = default) =>
        lectura.EjecutarLectura(token => interno.Ejecutar(entrada, token), cancelacion);
}
