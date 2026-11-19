using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;

namespace BabelHub.Ecommerce.Application.Envios.Interno;

/// <summary>
/// Informa a Pedidos el progreso de un envío (T08; ENV-07, ENV-14): «en reparto» y luego «entregado», <b>en ese orden</b> (Pedidos solo acepta la entrega tras el reparto) y siempre <b>después de confirmar</b> la
/// transacción propia. Ambas llamadas son idempotentes en Pedidos; lo que queda sin informar lo reintenta un trabajo. Nunca lanza: una falla de Pedidos no deshace nada de Envíos.
/// </summary>
internal sealed class InformadorDeProgreso(IRepositorioDeEnvios envios, IProgresoDeEnvios progreso, IUnidadDeTrabajo unidad, IReloj reloj)
{
    /// <summary>Intenta informar lo pendiente de un envío. Devuelve verdadero si no queda nada por informar.</summary>
    public async Task<bool> Informar(Guid envioId, CancellationToken cancelacion = default)
    {
        try
        {
            var envio = await envios.Obtener(envioId, paraActualizar: false, cancelacion).ConfigureAwait(false);
            if (envio is null)
            {
                return true;
            }

            var lineas = envio.Lineas.Select(l => l.LineaId).ToList();
            if (envio.EnRepartoEn is not null && envio.ProgresoEnRepartoInformadoEn is null)
            {
                var enReparto = await progreso.EnvioEnReparto(new EnvioEnRepartoEntrada(envio.PedidoId, envio.Id, lineas), cancelacion).ConfigureAwait(false);
                if (!enReparto.EsExito)
                {
                    return false;
                }

                await Anotar(envio.Id, e => e.RegistrarProgresoEnRepartoInformado(reloj.Ahora), cancelacion).ConfigureAwait(false);
            }

            if (envio.EntregadoEn is { } fecha && envio.ProgresoEntregadoInformadoEn is null)
            {
                var entregado = await progreso.EnvioEntregado(new EnvioEntregadoEntrada(envio.PedidoId, envio.Id, lineas, fecha), cancelacion).ConfigureAwait(false);
                if (!entregado.EsExito)
                {
                    return false;
                }

                await Anotar(envio.Id, e => e.RegistrarProgresoEntregadoInformado(reloj.Ahora), cancelacion).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Queda sin informar: el trabajo de fondo lo reintenta (idempotente).
            return false;
        }
    }

    private async Task Anotar(Guid envioId, Func<Envio, bool> anotar, CancellationToken cancelacion)
    {
        await unidad.Ejecutar(
            async token =>
            {
                var envio = await envios.Obtener(envioId, paraActualizar: true, token).ConfigureAwait(false);
                if (envio is not null && anotar(envio))
                {
                    await envios.Guardar(envio, token).ConfigureAwait(false);
                }

                return true;
            },
            cancelacion).ConfigureAwait(false);
    }
}

/// <summary>
/// Marcar un envío entregado (T08; ENV-07, ENV-14): una transacción corta que exige <b>en reparto</b>, guarda la fecha y audita en la misma transacción (sin poder auditar no se aplica); después de confirmar informa a
/// Pedidos (<c>EnvioEntregado</c> con la fecha, solo tras informar «en reparto»). Envíos <b>no avisa</b> por su cuenta en entregado (AC-ENV-06): el aviso es el de línea de Pedidos.
/// </summary>
internal sealed class MarcarEntregado(
    AutorizadorDeEnvios autorizador,
    IRepositorioDeEnvios envios,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    InformadorDeProgreso informador,
    IReloj reloj) : ICasoDeUso<Guid, DetalleDeEnvio>
{
    public async Task<Resultado<DetalleDeEnvio>> Ejecutar(Guid envioId, CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirLogisticaOAdministracion(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeEnvios.Fallo<DetalleDeEnvio>(rechazo);
        }

        Resultado<DetalleDeEnvio> resultado;
        try
        {
            resultado = await unidad.Ejecutar(
                async token =>
                {
                    var envio = await envios.Obtener(envioId, paraActualizar: true, token).ConfigureAwait(false);
                    if (envio is null)
                    {
                        return FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.NoEncontrado, ClavesDeEnvios.EnvioNoEncontrado);
                    }

                    var anterior = envio.Estado;
                    if (envio.MarcarEntregado(reloj.Ahora) != ResultadoDeTransicion.Hecha)
                    {
                        return FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.Conflicto, ClavesDeEnvios.EstadoInvalido);
                    }

                    await auditoria.Registrar(
                        AuditoriaDeEnvios.Cambio(envio, anterior, EstadoDeEnvio.Entregado, envio.EntregadoEn),
                        token).ConfigureAwait(false);
                    await envios.Guardar(envio, token).ConfigureAwait(false);
                    return Resultado<DetalleDeEnvio>.Exito(MapeoDeEnvios.Detalle(envio));
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AuditoriaNoDisponibleException or EnviosNoDisponiblesException)
        {
            return FallosDeEnvios.NoDisponible<DetalleDeEnvio>();
        }

        if (resultado.EsExito)
        {
            await informador.Informar(envioId, CancellationToken.None).ConfigureAwait(false);
        }

        return resultado;
    }
}

/// <summary>
/// El trabajo que reintenta lo que no se pudo informar a Pedidos (T08, ENV-07, ENV-14): corre como el actor «sistema», informa <b>en orden</b> (el reparto antes que la entrega) y, si algo lleva más de una hora sin
/// informarse, publica una alerta técnica sin datos del envío. Idempotente: repetirlo no cambia nada.
/// </summary>
internal sealed class InformarProgresoAPedidos(IActorActual actores, IRepositorioDeEnvios envios, InformadorDeProgreso informador, IPublicadorDeAlertasTecnicas alertas, IReloj reloj)
    : ICasoDeUso<int, int>
{
    public static readonly TimeSpan UmbralDeAlerta = TimeSpan.FromHours(1);

    /// <summary>Informa hasta <paramref name="maximo"/> envíos y devuelve cuántos quedaron al día.</summary>
    public async Task<Resultado<int>> Ejecutar(int maximo, CancellationToken cancelacion = default)
    {
        if (actores.Actual is not { Tipo: TipoDeActor.Sistema })
        {
            return FallosDeEnvios.De<int>(CategoriaDeError.Prohibido, ClavesDeEnvios.Prohibido);
        }

        IReadOnlyList<Envio> pendientes;
        try
        {
            pendientes = await envios.ObtenerConProgresoPendiente(Math.Clamp(maximo, 1, 500), cancelacion).ConfigureAwait(false);
        }
        catch (EnviosNoDisponiblesException)
        {
            return FallosDeEnvios.NoDisponible<int>();
        }

        var alDia = 0;
        foreach (var envio in pendientes)
        {
            if (await informador.Informar(envio.Id, cancelacion).ConfigureAwait(false))
            {
                alDia++;
            }
            else if ((envio.ProgresoEnRepartoInformadoEn is null ? envio.EnRepartoEn : envio.EntregadoEn) is { } desde && reloj.Ahora - desde > UmbralDeAlerta)
            {
                await Alertar().ConfigureAwait(false);
            }
        }

        return Resultado<int>.Exito(alDia);
    }

    private async Task Alertar()
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(ClavesDeEnvios.AlertaProgresoSinInformar, reloj.Ahora)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una alerta que no se publica nunca interrumpe el trabajo.
        }
    }
}
