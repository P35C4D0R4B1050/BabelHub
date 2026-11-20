using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>
/// La parte de Pedidos de la cancelación de una cuenta (T24; PED-33, PED-37; AC-PED-28, AC-PED-31): anonimiza el nombre, el teléfono y la dirección de entrega de <b>todos los pedidos de esa cuenta</b> y conserva el pedido, sus líneas, importes, fechas y el identificador opaco de la cuenta. Corre <b>dentro de la transacción de Identidad</b> (la Infraestructura lo ejecuta bajo el rol del esquema) con el orden de bloqueo de D19:
/// la marca de la cuenta es de Identidad y va antes; los pedidos se leen con bloqueo y en orden de identificador. Es <b>idempotente y reaplicable</b> (un pedido de recojo, digital o ya anonimizado por la depuración termina sin error y sin cambios; tras restaurar un respaldo, un contacto que reapareció se vuelve a anonimizar). Si algo no se puede anonimizar o auditar <b>lanza</b>: Identidad lo informa, no ejecuta
/// la cancelación y revierte la transacción, así que ningún pedido queda anonimizado a medias y la cancelación puede reintentarse.
/// </summary>
internal sealed class AnonimizarContactoDeUnaCuenta(IRepositorioDePedidos pedidos, IAuditoria auditoria, IReloj reloj) : IAplicadorDeCancelacion
{
    public const string CausaDeLaCancelacion = "cancelacion-de-la-cuenta";

    public string Modulo => "pedidos";

    public async Task Aplicar(Guid usuarioId, Guid identificadorOpaco, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La cuenta necesita su identificador.", nameof(usuarioId));
        }

        var ahora = reloj.Ahora;
        var anonimizados = 0;
        foreach (var pedido in await pedidos.ListarDeLaCuentaConBloqueo(usuarioId, cancelacion).ConfigureAwait(false))
        {
            if (pedido.AnonimizarContacto(ahora))
            {
                await pedidos.Actualizar(pedido, cancelacion).ConfigureAwait(false);
                anonimizados++;
            }

            // Junto con el contacto (plan §3): la identidad de quien inició sus anulaciones y entregas.
            await pedidos.AnonimizarIniciadoresDeOperaciones(pedido.Id, cancelacion).ConfigureAwait(false);
        }

        if (anonimizados > 0)
        {
            await auditoria.Registrar(
                new SolicitudDeRegistro(AccionesAuditables.ContactoDePedidoAnonimizado)
                {
                    Detalle = new Dictionary<string, string?> { ["causa"] = CausaDeLaCancelacion, ["cantidad"] = anonimizados.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                },
                cancelacion).ConfigureAwait(false);
        }
    }
}

/// <summary>Lo que dejó una depuración: cuántos pedidos quedaron con el contacto anonimizado.</summary>
public sealed record ResultadoDeLaDepuracion(int Anonimizados);

/// <summary>
/// La depuración diaria por retención (T25; PED-34, PED-39; AC-PED-29, AC-PED-33): la corre el trabajo de fondo como actor «sistema» (03:00 de Lima). Anonimiza el contacto de todo pedido a los <b>dos años por fecha de Lima</b> desde su último cambio a entregado o anulado y, en los mismos pedidos, la identidad (identificador, rol y sucursal) de quien inició sus anulaciones y entregas;
/// lo demás se conserva. Todo ocurre en <b>una sola transacción</b> con la auditoría de la corrida (<c>PedidosDepurados</c> con actor «sistema» y solo el conteo, también cuando es cero): <b>si no puede auditar o anonimizar no hace nada</b> y responde «servicio no disponible» para reintentar al día siguiente, y publica la alerta técnica si pasaron 24 horas desde la última corrida con éxito (o nunca hubo una).
/// Repetirla no cambia nada.
/// </summary>
internal sealed class AnonimizarContactosVencidos(
    AutorizadorDePedidos autorizador,
    IRepositorioDePedidos pedidos,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj)
{
    public const int MaximoPorCorrida = 1000;

    /// <summary>El motivo catalogado de un trabajo de fondo que dejó de correr (el catálogo no tiene uno propio de la depuración de pedidos).</summary>
    public const string AlertaSinCorrida = "depuracion-de-pedidos-sin-exito";

    public static readonly TimeSpan SinCorridaDurante = TimeSpan.FromHours(24);

    public async Task<Resultado<ResultadoDeLaDepuracion>> Depurar(CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<ResultadoDeLaDepuracion>.Fallo(rechazo);
        }

        try
        {
            var anonimizados = await unidad.Ejecutar(token => Anonimizar(token), cancelacion).ConfigureAwait(false);
            return Resultado<ResultadoDeLaDepuracion>.Exito(new ResultadoDeLaDepuracion(anonimizados));
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            await AlertarSiPasaronLas24Horas(cancelacion).ConfigureAwait(false);
            return Resultado<ResultadoDeLaDepuracion>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }
    }

    private async Task<int> Anonimizar(CancellationToken cancelacion)
    {
        var ahora = reloj.Ahora;
        var hoy = reloj.HoyEnLima;
        // Se vuelve a comprobar el vencimiento con la regla del Dominio: un filtro más amplio del adaptador nunca anonimiza antes de tiempo.
        var vencidos = (await pedidos.ListarVencidosParaDepurar(hoy, MaximoPorCorrida, cancelacion).ConfigureAwait(false)).Where(p => p.EstaVencidoParaDepurar(hoy)).ToList();
        var anonimizados = vencidos.Count(p => p.Contacto is not null);

        // Se audita primero: sin poder auditar no se anonimiza nada (y la transacción se revierte entera si algo falla después).
        await auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.PedidosDepurados)
            {
                Detalle = new Dictionary<string, string?> { ["cantidad"] = anonimizados.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            },
            cancelacion).ConfigureAwait(false);

        foreach (var pedido in vencidos)
        {
            if (pedido.AnonimizarPorRetencion(hoy, ahora))
            {
                await pedidos.Actualizar(pedido, cancelacion).ConfigureAwait(false);
            }

            await pedidos.AnonimizarIniciadoresDeOperaciones(pedido.Id, cancelacion).ConfigureAwait(false);
        }

        await pedidos.RegistrarDepuracionExitosa(ahora, cancelacion).ConfigureAwait(false);
        return anonimizados;
    }

    private async Task AlertarSiPasaronLas24Horas(CancellationToken cancelacion)
    {
        try
        {
            var ultima = await pedidos.UltimaDepuracionExitosaEn(cancelacion).ConfigureAwait(false);
            if (ultima is null || reloj.Ahora - ultima >= SinCorridaDurante)
            {
                await alertas.Publicar(new AlertaTecnica(AlertaSinCorrida, reloj.Ahora), cancelacion).ConfigureAwait(false);
            }
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            // Una alerta que no se publica nunca interrumpe el trabajo.
        }
    }
}

/// <summary>Lo que dejó una pasada de la liberación del contacto: pedidos liberados en Pagos y los que siguen pendientes.</summary>
public sealed record ResultadoDeLaLiberacionDeContactos(int Liberados, int Pendientes);

/// <summary>
/// La liberación del contacto de las intenciones de pago (T26; PED-35; AC-PED-27): tras confirmarse la creación del pedido (la fila de <c>liberaciones_de_contacto</c> se anotó en la misma transacción), pide a Pagos eliminar esos datos de la intención por <see cref="ILiberacionDeContactoDePago"/> (idempotente), <b>fuera de toda transacción</b> y como actor «sistema». Una fila de un pedido <b>sin contacto</b> (recojo o ya
/// anonimizado) se borra sin pedir nada. Si Pagos no responde, la fila queda con un intento más y se repite en la pasada siguiente; pasadas 24 horas desde que se anotó, publica una alerta técnica (rige el respaldo de 30 días de PAG-27).
/// </summary>
internal sealed class LiberarContactoDeIntenciones(
    AutorizadorDePedidos autorizador,
    IRepositorioDePedidos pedidos,
    ILiberacionDeContactoDePago liberacion,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj)
{
    public const int MaximoPorPasada = 200;

    public const string AlertaSinLiberar = "contacto-de-pedido-sin-liberar";

    public static readonly TimeSpan SinLiberarDurante = TimeSpan.FromHours(24);

    public async Task<Resultado<ResultadoDeLaLiberacionDeContactos>> Liberar(CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<ResultadoDeLaLiberacionDeContactos>.Fallo(rechazo);
        }

        IReadOnlyList<LiberacionDeContactoPendiente> anotadas;
        try
        {
            anotadas = await pedidos.LiberacionesDeContactoPendientes(MaximoPorPasada, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return Resultado<ResultadoDeLaLiberacionDeContactos>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }

        int liberados = 0, pendientes = 0;
        var vencidaSinLiberar = false;
        foreach (var anotada in anotadas)
        {
            try
            {
                var pedido = await pedidos.Obtener(anotada.PedidoId, conBloqueo: false, cancelacion).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("El pedido de una liberación de contacto no existe.");
                if (pedido.Contacto is not null)
                {
                    await liberacion.Liberar(anotada.PedidoId, cancelacion).ConfigureAwait(false);
                }

                await pedidos.QuitarLiberacionDeContacto(anotada.PedidoId, cancelacion).ConfigureAwait(false);
                liberados++;
            }
            catch (Exception excepcion) when (excepcion is not OperationCanceledException)
            {
                pendientes++;
                vencidaSinLiberar |= await AnotarElIntento(anotada, cancelacion).ConfigureAwait(false);
            }
        }

        // Una sola alerta por pasada, aunque sean muchas las filas vencidas.
        if (vencidaSinLiberar)
        {
            await Alertar(cancelacion).ConfigureAwait(false);
        }

        return Resultado<ResultadoDeLaLiberacionDeContactos>.Exito(new ResultadoDeLaLiberacionDeContactos(liberados, pendientes));
    }

    /// <summary>Anota el intento y dice si la fila ya lleva 24 horas o más sin liberarse.</summary>
    private async Task<bool> AnotarElIntento(LiberacionDeContactoPendiente anotada, CancellationToken cancelacion)
    {
        try
        {
            await pedidos.AnotarIntentoDeLiberacionDeContacto(anotada.PedidoId, reloj.Ahora, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            // El intento se repite en la pasada siguiente; nunca se detiene el resto.
        }

        return reloj.Ahora - anotada.Desde >= SinLiberarDurante;
    }

    private async Task Alertar(CancellationToken cancelacion)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(AlertaSinLiberar, reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            // Una alerta que no se publica nunca interrumpe la pasada.
        }
    }
}
