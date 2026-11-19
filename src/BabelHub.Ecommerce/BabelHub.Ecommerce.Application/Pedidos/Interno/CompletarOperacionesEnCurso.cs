using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>Lo que dejó una pasada de la reanudación: operaciones completadas, descartadas (otra ya las resolvió), fallidas (visibles a la administración) y las que siguen en curso para la próxima pasada.</summary>
public sealed record ResultadoDeLaReanudacion(int Completadas, int Descartadas, int Fallidas, int Pendientes);

/// <summary>
/// La reanudación de las anulaciones y entregas que quedaron en curso por un corte o una auditoría caída (T22; PED-21, PED-24; AC-PED-09, 19, 22): la corre el trabajo de fondo como actor «sistema» (otro actor es <c>prohibido</c>) y repite <b>solo lo local</b> y <c>liberar</c> (alcance de sistema): <b>nunca llama a <c>cerrar</c></b>, que exige el token de una persona. Cada operación se vuelve a leer
/// antes de tocarla (si otra instancia la resolvió, se omite) y se procesa con su espera creciente (<c>proximo_intento_en</c>):
/// <list type="bullet">
/// <item><b>Anulación:</b> si otra operación ya anuló todas sus líneas se <b>descarta</b>; si no, se repiten los pasos que faltan (<see cref="OrquestadorDeAnulaciones.Continuar"/> con <c>reanudacion</c>), y la compleción se audita como «sistema» con el iniciador guardado en la fila en <c>iniciadaPor</c> (nunca se reconstruye un actor de persona). Un error permanente la deja <b>fallida</b>.</item>
/// <item><b>Entrega:</b> se <b>aplica <c>entregado</c> a las líneas con el cierre ya confirmado</b> (también de una entrega parcial); sin ningún cierre confirmado pasada la espera corta queda <b>fallida</b> (el encargado la reintenta con la misma clave, que descuenta una sola vez).</item>
/// <item><b>Ambas:</b> a los 15 minutos desde que se crearon, o ante un error permanente, la fila queda <b>fallida</b>, visible y con una alerta técnica sin datos personales.</item>
/// </list>
/// Un fallo en una operación nunca detiene la pasada.
/// </summary>
internal sealed class CompletarOperacionesEnCurso(
    AutorizadorDePedidos autorizador,
    IRepositorioDePedidos pedidos,
    IUnidadDeTrabajo unidad,
    OrquestadorDeAnulaciones orquestador,
    EntregarLineasDelPedido entregas,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj)
{
    public const string AlertaOperacionFallida = "pedido-operacion-fallida";
    public const int MaximoPorPasada = 100;

    public async Task<Resultado<ResultadoDeLaReanudacion>> Reanudar(CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<ResultadoDeLaReanudacion>.Fallo(rechazo);
        }

        OperacionesPorReanudar debidas;
        try
        {
            debidas = await pedidos.OperacionesEnCursoParaReanudar(reloj.Ahora, MaximoPorPasada, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return Resultado<ResultadoDeLaReanudacion>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }

        var totales = new int[4];
        foreach (var anulacion in debidas.Anulaciones)
        {
            Sumar(totales, await Proteger(() => ReanudarAnulacion(anulacion, cancelacion)).ConfigureAwait(false));
        }

        foreach (var entrega in debidas.Entregas)
        {
            Sumar(totales, await Proteger(() => ReanudarEntrega(entrega, cancelacion)).ConfigureAwait(false));
        }

        return Resultado<ResultadoDeLaReanudacion>.Exito(new ResultadoDeLaReanudacion(totales[0], totales[1], totales[2], totales[3]));
    }

    private enum Salida
    {
        Completada,
        Descartada,
        Fallida,
        Pendiente,
        Omitida,
    }

    private static void Sumar(int[] totales, Salida salida)
    {
        if (salida != Salida.Omitida)
        {
            totales[(int)salida]++;
        }
    }

    /// <summary>Una operación que falla por una excepción inesperada queda para la próxima pasada: nunca detiene la reanudación.</summary>
    private static async Task<Salida> Proteger(Func<Task<Salida>> pasada)
    {
        try
        {
            return await pasada().ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return Salida.Pendiente;
        }
    }

    private async Task<Salida> ReanudarAnulacion(Anulacion leida, CancellationToken cancelacion)
    {
        // Se vuelve a leer: si otra instancia o la solicitud original ya la resolvió, no se toca.
        var anulacion = (await pedidos.AnulacionesEnCurso(leida.PedidoId, cancelacion).ConfigureAwait(false)).FirstOrDefault(a => a.Id == leida.Id);
        if (anulacion is null)
        {
            return Salida.Omitida;
        }

        var ahora = reloj.Ahora;
        var pedido = await pedidos.Obtener(anulacion.PedidoId, conBloqueo: false, cancelacion).ConfigureAwait(false);
        if (pedido is null || ahora - anulacion.CreadaEn > EsperaDeReintentos.LimiteDeLaOperacion)
        {
            return await Fallar(() => anulacion.Fallar(ahora), anulacion, cancelacion).ConfigureAwait(false);
        }

        // Otra operación ya resolvió sus líneas: se descarta.
        if (pedido.Lineas.Where(l => anulacion.LineaIds.Contains(l.Id)).All(l => l.EstadoImpreso == EstadoDeLineaImpresa.Anulado || l.EstadoDigital == EstadoDeLineaDigital.Anulado))
        {
            await Guardar(() => anulacion.Descartar(ahora), a => pedidos.ActualizarAnulacion(a, cancelacion), anulacion).ConfigureAwait(false);
            return Salida.Descartada;
        }

        var aplicada = await orquestador.Continuar(anulacion, reanudacion: true, cancelacion).ConfigureAwait(false);
        if (aplicada.EsExito)
        {
            return Salida.Completada;
        }

        if (anulacion.Estado == EstadoDeOperacion.Descartada)
        {
            return Salida.Descartada;
        }

        if (EsPermanente(aplicada.Error!))
        {
            return await Fallar(() => anulacion.Fallar(ahora), anulacion, cancelacion).ConfigureAwait(false);
        }

        await Guardar(() => anulacion.RegistrarIntento(ahora), a => pedidos.ActualizarAnulacion(a, cancelacion), anulacion).ConfigureAwait(false);
        if (anulacion.Estado != EstadoDeOperacion.EnCurso)
        {
            // Pasados 15 minutos el intento la deja fallida.
            await PublicarAlerta(cancelacion).ConfigureAwait(false);
            return Salida.Fallida;
        }

        return Salida.Pendiente;
    }

    private async Task<Salida> ReanudarEntrega(Entrega leida, CancellationToken cancelacion)
    {
        var entrega = (await pedidos.EntregasEnCurso(leida.PedidoId, cancelacion).ConfigureAwait(false)).FirstOrDefault(e => e.Id == leida.Id);
        if (entrega is null)
        {
            return Salida.Omitida;
        }

        var ahora = reloj.Ahora;

        // Lo local primero: pasa a entregadas las líneas con el cierre ya confirmado (nunca se llama a cerrar).
        if (entrega.LineasCerradas.Count > 0 || entrega.LineasPendientesDeCierre.Count == 0)
        {
            if (!await entregas.Reanudar(entrega, cancelacion).ConfigureAwait(false))
            {
                await Guardar(() => entrega.RegistrarIntento(ahora), e => pedidos.ActualizarEntrega(e, cancelacion), entrega).ConfigureAwait(false);
                return await TrasElIntento(entrega, cancelacion).ConfigureAwait(false);
            }

            if (entrega.Estado == EstadoDeOperacion.Completada)
            {
                return Salida.Completada;
            }
        }

        // Quedan líneas sin cierre confirmado: sin ninguna confirmada y pasada la espera corta, la entrega queda fallida para que el encargado la reintente con la misma clave; a los 15 minutos, también.
        if (entrega.DebeFallarPorFaltaDeCierre(ahora))
        {
            await Guardar(() => entrega.Fallar(ahora), e => pedidos.ActualizarEntrega(e, cancelacion), entrega).ConfigureAwait(false);
            return Salida.Fallida;
        }

        if (ahora - entrega.CreadaEn > EsperaDeReintentos.LimiteDeLaOperacion)
        {
            return await Fallar(() => entrega.Fallar(ahora), entrega, cancelacion).ConfigureAwait(false);
        }

        await Guardar(() => entrega.RegistrarIntento(ahora), e => pedidos.ActualizarEntrega(e, cancelacion), entrega).ConfigureAwait(false);
        return await TrasElIntento(entrega, cancelacion).ConfigureAwait(false);
    }

    private async Task<Salida> TrasElIntento(Entrega entrega, CancellationToken cancelacion)
    {
        if (entrega.Estado == EstadoDeOperacion.EnCurso)
        {
            return Salida.Pendiente;
        }

        await PublicarAlerta(cancelacion).ConfigureAwait(false);
        return Salida.Fallida;
    }

    private async Task<Salida> Fallar(Action fallar, Anulacion anulacion, CancellationToken cancelacion)
    {
        await Guardar(fallar, a => pedidos.ActualizarAnulacion(a, cancelacion), anulacion).ConfigureAwait(false);
        await PublicarAlerta(cancelacion).ConfigureAwait(false);
        return Salida.Fallida;
    }

    private async Task<Salida> Fallar(Action fallar, Entrega entrega, CancellationToken cancelacion)
    {
        await Guardar(fallar, e => pedidos.ActualizarEntrega(e, cancelacion), entrega).ConfigureAwait(false);
        await PublicarAlerta(cancelacion).ConfigureAwait(false);
        return Salida.Fallida;
    }

    /// <summary>Un error que repetir no arregla: el pedido o su operación ya no existen o no se pueden aplicar. «Servicio no disponible» y el <c>conflicto</c> del bloqueo se reintentan.</summary>
    private static bool EsPermanente(ErrorDeAplicacion error) => error.Clave is not (ClavesDePedidos.ServicioNoDisponible or ClavesDePedidos.Conflicto);

    private async Task Guardar<T>(Action cambio, Func<T, Task> persistir, T operacion)
    {
        cambio();
        await unidad.Ejecutar(
            async _ =>
            {
                await persistir(operacion).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
    }

    private async Task PublicarAlerta(CancellationToken cancelacion)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(AlertaOperacionFallida, reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            // Una alerta que no se publica nunca detiene la reanudación.
        }
    }
}
