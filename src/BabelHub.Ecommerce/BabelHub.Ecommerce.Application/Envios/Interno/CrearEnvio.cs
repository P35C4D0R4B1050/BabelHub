using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;

namespace BabelHub.Ecommerce.Application.Envios.Interno;

/// <summary>
/// <c>IDespachoDeEnvios.Crear</c> (T04; ENV-01, ENV-09): lo llama Pedidos al marcar listas las líneas de un pedido a domicilio, <b>dentro de su transacción y con el pedido ya bloqueado</b>. Por eso no abre una unidad de
/// trabajo ni toma bloqueos, y lee las líneas por <see cref="IPedidosParaEnvios"/> en esa misma transacción (ve las que Pedidos acaba de marcar listas). Crea <b>un envío pendiente de guía por llamada</b>, solo con las líneas
/// impresas de la fuente (las digitales se informan como rechazadas), es idempotente por <c>(pedido, fuente, líneas)</c> y rechaza completo un solape parcial. No guarda ningún dato de contacto (ENV-15).
/// </summary>
internal sealed class CrearEnvio(IPedidosParaEnvios pedidos, IRepositorioDeEnvios envios, IReloj reloj) : ICasoDeUso<CrearEnvioEntrada, ResultadoDeCrearEnvio>
{
    public async Task<Resultado<ResultadoDeCrearEnvio>> Ejecutar(CrearEnvioEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var fuente = entrada.CodigoDeFuente?.Trim();
        var lineaIds = entrada.LineaIds ?? [];
        if (entrada.PedidoId == Guid.Empty || string.IsNullOrEmpty(fuente) || fuente.Length > Envio.LongitudMaximaDeLaFuente || lineaIds.Count == 0 || lineaIds.Any(l => l == Guid.Empty) || lineaIds.Distinct().Count() != lineaIds.Count)
        {
            return FallosDeEnvios.De<ResultadoDeCrearEnvio>(CategoriaDeError.Validacion, ClavesDeEnvios.ValidacionFallida);
        }

        var lectura = await pedidos.Obtener(entrada.PedidoId, cancelacion).ConfigureAwait(false);
        if (!lectura.EsExito)
        {
            return FallosDeEnvios.Fallo<ResultadoDeCrearEnvio>(lectura.Error!);
        }

        var pedido = lectura.Valor;
        if (!pedido.EsADomicilio)
        {
            return FallosDeEnvios.De<ResultadoDeCrearEnvio>(CategoriaDeError.ReglaDeNegocio, ClavesDeEnvios.PedidoNoEsADomicilio);
        }

        var impresas = new List<Guid>();
        var algunaSinLista = false;
        var rechazadas = new List<LineaRechazadaDelEnvio>();
        foreach (var lineaId in lineaIds)
        {
            var linea = pedido.Lineas.FirstOrDefault(l => l.LineaId == lineaId);
            if (linea is null)
            {
                return FallosDeEnvios.De<ResultadoDeCrearEnvio>(CategoriaDeError.ReglaDeNegocio, ClavesDeEnvios.LineaNoEsDelPedido);
            }

            if (linea.EsDigital)
            {
                rechazadas.Add(new LineaRechazadaDelEnvio(lineaId, MotivoDeLineaRechazadaDelEnvio.Digital));
                continue;
            }

            if (!string.Equals(linea.CodigoDeFuente, fuente, StringComparison.Ordinal))
            {
                return FallosDeEnvios.De<ResultadoDeCrearEnvio>(CategoriaDeError.ReglaDeNegocio, ClavesDeEnvios.LineaDeOtraFuente);
            }

            algunaSinLista |= !linea.EstaLista;
            impresas.Add(lineaId);
        }

        if (impresas.Count == 0)
        {
            return FallosDeEnvios.De<ResultadoDeCrearEnvio>(CategoriaDeError.ReglaDeNegocio, ClavesDeEnvios.LineaDigital);
        }

        try
        {
            var existentes = await envios.BuscarPorLineas(impresas, cancelacion).ConfigureAwait(false);
            if (existentes.Count > 0)
            {
                return existentes is [var unico] && EsElMismoEnvio(unico, pedido.PedidoId, fuente, impresas)
                    ? Resultado<ResultadoDeCrearEnvio>.Exito(new ResultadoDeCrearEnvio(unico.Id, true, unico.Lineas.Select(l => l.LineaId).ToList(), rechazadas))
                    : FallosDeEnvios.De<ResultadoDeCrearEnvio>(CategoriaDeError.Conflicto, ClavesDeEnvios.LineasEnOtroEnvio);
            }

            // La idempotencia se resuelve antes: una repetición cuando las líneas ya avanzaron de estado devuelve el envío existente.
            if (algunaSinLista)
            {
                return FallosDeEnvios.De<ResultadoDeCrearEnvio>(CategoriaDeError.ReglaDeNegocio, ClavesDeEnvios.LineaNoEstaLista);
            }

            var envio = Envio.Crear(Guid.NewGuid(), pedido.PedidoId, fuente, impresas, reloj.Ahora);
            return await envios.Agregar(envio, cancelacion).ConfigureAwait(false) switch
            {
                ResultadoDeAgregadoDeEnvio.Agregado => Resultado<ResultadoDeCrearEnvio>.Exito(new ResultadoDeCrearEnvio(envio.Id, false, impresas, rechazadas)),

                // Otra transacción ganó el único (linea_id) después de la búsqueda: un conflicto, nunca un error de la base.
                _ => FallosDeEnvios.De<ResultadoDeCrearEnvio>(CategoriaDeError.Conflicto, ClavesDeEnvios.LineasEnOtroEnvio),
            };
        }
        catch (EnviosNoDisponiblesException)
        {
            return FallosDeEnvios.NoDisponible<ResultadoDeCrearEnvio>();
        }
    }

    private static bool EsElMismoEnvio(Envio existente, Guid pedidoId, string fuente, List<Guid> lineas) =>
        existente.PedidoId == pedidoId
        && string.Equals(existente.CodigoDeFuente, fuente, StringComparison.Ordinal)
        && existente.Lineas.Count == lineas.Count
        && existente.Lineas.Select(l => l.LineaId).ToHashSet().SetEquals(lineas);
}
