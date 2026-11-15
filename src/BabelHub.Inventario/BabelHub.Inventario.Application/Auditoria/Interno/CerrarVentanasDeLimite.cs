using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Auditoria;
using TipoDeActor = BabelHub.Inventario.Application.Plataforma.TipoDeActor;

namespace BabelHub.Inventario.Application.Auditoria.Interno;

/// <summary>
/// El acumulado persistente de los rechazos por límite visto desde el cierre de las ventanas (AUD-28, AUD-41, D11; T25 y T27): la Infraestructura lo implementa sobre
/// <c>auditoria.acumulado_de_limites</c>. <see cref="Cargar"/> y <see cref="GuardarCierre"/> corren en la unidad de trabajo del cierre: el bloqueo de la ventana la serializa con los rechazos que siguen llegando.
/// </summary>
public interface IAcumuladoParaCerrar
{
    /// <summary>Los inicios de las ventanas aún abiertas cuyo cierre más el margen de 10 segundos ya pasó, de la más antigua a la más reciente, hasta <paramref name="maximo"/>.</summary>
    Task<IReadOnlyList<DateTimeOffset>> VentanasCerrables(DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default);

    /// <summary>Bloquea la ventana y devuelve sus filas con el dominio (<see cref="AcumuladoDeLimite.Restaurar"/>); <see langword="null"/> si ya no existe.</summary>
    Task<AcumuladoDeLimite?> Cargar(DateTimeOffset inicioDeLaVentana, CancellationToken cancelacion = default);

    /// <summary>Marca la ventana cerrada y borra todas sus direcciones completas, en la misma transacción que escribe los eventos (AUD-41).</summary>
    Task GuardarCierre(DateTimeOffset inicioDeLaVentana, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>Anota un intento de cierre fallido y devuelve cuántos van (en su propia transacción: la del cierre se revirtió).</summary>
    Task<int> RegistrarIntentoFallido(DateTimeOffset inicioDeLaVentana, CancellationToken cancelacion = default);

    /// <summary>Borra las direcciones completas de la ventana conservando la huella y los totales (la auditoría sigue caída tras los reintentos, AUD-41).</summary>
    Task BorrarDirecciones(DateTimeOffset inicioDeLaVentana, CancellationToken cancelacion = default);

    /// <summary>Purga las ventanas cerradas hace más de 24 horas (D11); devuelve cuántas filas borró.</summary>
    Task<int> Purgar(DateTimeOffset ahora, CancellationToken cancelacion = default);
}

/// <summary>Lo que hizo una corrida del cierre (cantidades, nunca datos).</summary>
public sealed record ResultadoDelCierreDeVentanas(int VentanasCerradas, int EventosEscritos, int Fallidas, int DireccionesBorradas, int FilasPurgadas);

/// <summary>
/// Cierra las ventanas de 5 minutos del acumulado de límites (AUD-28, AUD-41): <b>un solo evento</b> <c>LimiteExcedido</c> por huella, límite y ventana, fechado en el cierre, con el total
/// de rechazos y la dirección completa del origen, escrito con la ventana marcada como cerrada y las direcciones borradas <b>en una sola transacción</b> y tras un margen de 10 segundos.
/// Es idempotente (una ventana cerrada no vuelve a emitir) y retoma tras un reinicio desde lo persistido. Corre como «sistema». Si la auditoría sigue caída tras
/// <see cref="IntentosAntesDeBorrarLaDireccion"/> reintentos, publica una alerta técnica y borra la dirección conservando solo la huella; las ventanas cerradas se purgan a las 24 horas.
/// </summary>
public sealed class CerrarVentanasDeLimite(
    IAcumuladoParaCerrar acumulado,
    IAlmacenDeRegistros registros,
    CatalogoDeAcciones catalogo,
    ServicioDeOrigen servicio,
    IUnidadDeTrabajo unidad,
    IAlertas alertas,
    IActorActual actores,
    IReloj reloj)
{
    public const int IntentosAntesDeBorrarLaDireccion = 10;

    public const int MaximoDeVentanasPorCorrida = 50;

    /// <summary>El motivo del evento de la fila «otros» (los orígenes que no cupieron en las 1000 filas): su límite original ya no se conoce; el catálogo cerrado no tiene un motivo propio.</summary>
    public const string MotivoDeLaFilaOtros = "solicitudes-del-origen-desconocido";

    public async Task<ResultadoDelCierreDeVentanas> Ejecutar(CancellationToken cancelacion = default)
    {
        if (actores.Actual is not { Tipo: TipoDeActor.Sistema })
        {
            throw new InvalidOperationException("El cierre de ventanas corre como «sistema»: el trabajo debe fijar su actor explícito antes de llamarlo.");
        }

        var ahora = reloj.Ahora;
        var purgadas = await acumulado.Purgar(ahora, cancelacion).ConfigureAwait(false);
        var cerradas = 0;
        var eventos = 0;
        var fallidas = 0;
        var borradas = 0;

        foreach (var inicio in await acumulado.VentanasCerrables(ahora, MaximoDeVentanasPorCorrida, cancelacion).ConfigureAwait(false))
        {
            try
            {
                var escritos = await unidad.Ejecutar(async ct => await Cerrar(inicio, ahora, ct).ConfigureAwait(false), cancelacion).ConfigureAwait(false);
                if (escritos >= 0)
                {
                    cerradas++;
                    eventos += escritos;
                }
            }
            catch (Exception falla) when (falla is not OperationCanceledException)
            {
                fallidas++;
                borradas += await TrasUnFallo(inicio, cancelacion).ConfigureAwait(false);
            }
        }

        return new ResultadoDelCierreDeVentanas(cerradas, eventos, fallidas, borradas, purgadas);
    }

    /// <summary>Escribe los eventos de la ventana y la cierra; devuelve -1 si ya no existía o ya estaba cerrada.</summary>
    private async Task<int> Cerrar(DateTimeOffset inicio, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        var ventana = await acumulado.Cargar(inicio, cancelacion).ConfigureAwait(false);
        if (ventana is null || ventana.Cerrada || !ventana.PuedeCerrarse(ahora))
        {
            return -1;
        }

        var accion = catalogo.Obtener(nameof(AccionesAuditables.LimiteExcedido));
        var escritos = 0;
        foreach (var evento in ventana.Cerrar(ahora))
        {
            await registros.Insertar(ARegistro(accion, evento), cancelacion).ConfigureAwait(false);
            escritos++;
        }

        await acumulado.GuardarCierre(inicio, ahora, cancelacion).ConfigureAwait(false);
        return escritos;
    }

    private RegistroDeAuditoria ARegistro(AccionDelCatalogo accion, EventoDeLimite evento)
    {
        var esOtros = evento.Limite == AcumuladoDeLimite.LimiteDeOtros && evento.Huella == AcumuladoDeLimite.HuellaDeOtros;
        var detalle = DetalleSeguro.Construir(accion.Detalle, new Dictionary<string, string?> { ["totalDeRechazos"] = evento.Rechazos.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        return RegistroDeAuditoria.Crear(
            Guid.NewGuid(),
            servicio,
            accion,
            Actor.Sistema(),
            evento.Fecha.ToUniversalTime(),
            objetoTipo: null,
            objetoId: null,
            sucursalOrigen: null,
            sucursalDestino: null,
            motivo: esOtros ? MotivoDeLaFilaOtros : evento.Limite,
            origen: evento.DireccionCompleta ?? evento.Huella,
            ruta: null,
            detalle,
            ResultadoDeOperacion.Rechazado,
            correlacion: null);
    }

    private async Task<int> TrasUnFallo(DateTimeOffset inicio, CancellationToken cancelacion)
    {
        try
        {
            var intentos = await acumulado.RegistrarIntentoFallido(inicio, cancelacion).ConfigureAwait(false);
            if (intentos < IntentosAntesDeBorrarLaDireccion)
            {
                return 0;
            }

            // La auditoría sigue caída: la dirección completa no se conserva más (solo la huella y los totales) y se avisa por el registro técnico.
            await acumulado.BorrarDirecciones(inicio, cancelacion).ConfigureAwait(false);
            await alertas.PublicarTecnica(new CodigoDeAlerta(MotivosDeAlerta.AuditoriaNoDisponible), cancelacion).ConfigureAwait(false);
            return 1;
        }
        catch (Exception falla) when (falla is not OperationCanceledException)
        {
            // El estado tampoco responde: la ventana sigue abierta y la próxima corrida la reintenta.
            return 0;
        }
    }
}
