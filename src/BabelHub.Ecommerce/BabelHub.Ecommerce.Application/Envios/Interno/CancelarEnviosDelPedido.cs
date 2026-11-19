using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;

namespace BabelHub.Ecommerce.Application.Envios.Interno;

/// <summary>
/// <c>IDespachoDeEnvios.Cancelar</c> (T05; ENV-03, ENV-13, ENV-19): lo llama Pedidos <b>dentro de la transacción de la anulación y con el pedido ya bloqueado</b>. Bloquea los envíos del pedido en orden de identificador
/// (pedido → envío), cancela los que no salieron (el pendiente y el de guía generada sin el marcador de despacho) y mantiene el resto, <b>incluido uno ya cancelado</b> (plan D08: sus líneas ya salieron de la anulación
/// y no se procesan otra vez). <b>No avisa</b> (el aviso es el de línea de Pedidos). Audita cada cancelación en la misma transacción y falla cerrado. <b>Un resultado fallido no deja ningún efecto</b> (falla antes de auditar
/// o de guardar); si falla después del primer efecto (la segunda auditoría, un segundo guardado), la excepción <b>se propaga</b> para que la unidad de trabajo del llamador revierta todo, en vez de devolver un fallo que
/// confirmaría una cancelación a medias.
/// </summary>
internal sealed class CancelarEnviosDelPedido(IRepositorioDeEnvios envios, IAuditoria auditoria, IReloj reloj) : ICasoDeUso<Guid, IReadOnlyList<ResultadoDeCancelarEnvio>>
{
    public async Task<Resultado<IReadOnlyList<ResultadoDeCancelarEnvio>>> Ejecutar(Guid pedidoId, CancellationToken cancelacion = default)
    {
        if (pedidoId == Guid.Empty)
        {
            return FallosDeEnvios.De<IReadOnlyList<ResultadoDeCancelarEnvio>>(CategoriaDeError.Validacion, ClavesDeEnvios.ValidacionFallida);
        }

        IReadOnlyList<Envio> delPedido;
        try
        {
            delPedido = await envios.ObtenerDelPedido(pedidoId, paraActualizar: true, cancelacion).ConfigureAwait(false);
        }
        catch (EnviosNoDisponiblesException)
        {
            // Todavía no hay ningún efecto: el fallo es seguro.
            return FallosDeEnvios.NoDisponible<IReadOnlyList<ResultadoDeCancelarEnvio>>();
        }

        var ahora = reloj.Ahora;
        var resultados = new List<ResultadoDeCancelarEnvio>(delPedido.Count);
        var aGuardar = new List<Envio>();
        var auditorias = new List<SolicitudDeRegistro>();
        foreach (var envio in delPedido)
        {
            var anterior = envio.Estado;
            var lineas = envio.Lineas.Select(l => l.LineaId).ToList();
            switch (envio.Cancelar(ahora))
            {
                case ResultadoDeCancelacion.Cancelado:
                    aGuardar.Add(envio);
                    auditorias.Add(
                        new SolicitudDeRegistro(AccionesAuditables.EstadoDeEnvioCambiado)
                        {
                            ObjetoTipo = "envio",
                            ObjetoId = envio.Id.ToString(),
                            Detalle = new Dictionary<string, string?>
                            {
                                ["estadoAnterior"] = anterior.Codigo(),
                                ["estadoNuevo"] = EstadoDeEnvio.Cancelado.Codigo(),
                            },
                        });
                    resultados.Add(new ResultadoDeCancelarEnvio(envio.Id, DestinoDelEnvioAlCancelar.Cancelado, lineas));
                    break;
                default:
                    // Con el marcador de despacho, en reparto, entregado o ya cancelado: se mantiene.
                    resultados.Add(new ResultadoDeCancelarEnvio(envio.Id, DestinoDelEnvioAlCancelar.Mantenido, lineas));
                    break;
            }
        }

        var efectos = 0;
        try
        {
            // Sin poder auditar, la operación no se aplica.
            foreach (var solicitud in auditorias)
            {
                await auditoria.Registrar(solicitud, cancelacion).ConfigureAwait(false);
                efectos++;
            }

            foreach (var envio in aGuardar)
            {
                await envios.Guardar(envio, cancelacion).ConfigureAwait(false);
                efectos++;
            }

            return Resultado<IReadOnlyList<ResultadoDeCancelarEnvio>>.Exito(resultados);
        }
        catch (Exception ex) when (efectos == 0 && ex is EnviosNoDisponiblesException or AuditoriaNoDisponibleException)
        {
            return FallosDeEnvios.NoDisponible<IReadOnlyList<ResultadoDeCancelarEnvio>>();
        }
    }
}

/// <summary>La implementación de <see cref="IDespachoDeEnvios"/> (de Pedidos) por los casos de uso de Envíos. Ambas operaciones corren en la unidad de trabajo del llamador.</summary>
internal sealed class DespachoDeEnviosDeLaAplicacion(CrearEnvio crear, CancelarEnviosDelPedido cancelar) : IDespachoDeEnvios
{
    public Task<Resultado<ResultadoDeCrearEnvio>> Crear(CrearEnvioEntrada entrada, CancellationToken cancelacion = default) => crear.Ejecutar(entrada, cancelacion);

    public Task<Resultado<IReadOnlyList<ResultadoDeCancelarEnvio>>> Cancelar(Guid pedidoId, CancellationToken cancelacion = default) => cancelar.Ejecutar(pedidoId, cancelacion);
}
