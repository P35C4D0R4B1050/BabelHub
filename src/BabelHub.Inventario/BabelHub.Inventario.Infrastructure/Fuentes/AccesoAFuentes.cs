using BabelHub.Inventario.Application.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Fuentes;

/// <summary>
/// Cómo entra la Infraestructura de <c>Fuentes</c> a su esquema (D01, D09, hallazgo del 2026-11-14): cada operación corre <b>dentro de la transacción de la unidad de trabajo</b> y con
/// <c>SET LOCAL ROLE rol_fuentes</c> (<c>EjecutarComo</c>), porque el rol de la aplicación por sí solo no lee ningún esquema. Hay dos modos:
/// <list type="bullet">
/// <item><see cref="ConAlcanceDelActor"/>: el alcance de datos que fijó la unidad de trabajo para el actor. Lo usan la escritura del stock (<c>Aplicar</c>) y las lecturas de una
/// fuente: la política de filas es la segunda capa del alcance por sucursal.</item>
/// <item><see cref="ConAlcanceDeRed"/>: la elevación <c>ConAlcanceDeRed</c> (D09), solo para garantizar y bloquear filas, aplicar las liberaciones de vencidas, comprobar si una fila existe y
/// leer la red completa, siempre después de que la Aplicación autorizó.</item>
/// </list>
/// <b>Sin actor no se toca el stock.</b> La política de filas deniega sin actor devolviendo cero filas <i>sin error</i>, que parece «sin stock»: por eso, si no hay actor o es un visitante, ambos modos
/// <b>fallan a la vista</b> con <see cref="ContextoDeActorAusenteException"/> y dejan una alerta técnica (<see cref="MotivoDeAlertaSinActor"/>) en lugar de leer o escribir como si nada. Quien llama
/// desde una ruta de servicio debe haber establecido el actor de integración; un trabajo de fondo, el actor del sistema.
/// </summary>
internal sealed class AccesoAFuentes(
    IUnidadDeTrabajo unidad,
    IConexionDeLaUnidadDeTrabajo conexion,
    IElevacionDeAlcance elevacion,
    IActorActual actores,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj,
    IAlcanceDeDatosFijado alcance)
{
    /// <summary>El motivo de la alerta técnica cuando se intenta tocar el stock sin actor (debe constar en el catálogo de motivos de alertas técnicas).</summary>
    public const string MotivoDeAlertaSinActor = "alcance-de-datos-sin-actor";

    private const string EsperaDeBloqueoAgotada = "55P03";

    private static readonly EsquemaDeModulo Esquema = EsquemaDeModulo.Crear(DefinicionDeFuentes.Esquema);

    /// <summary>Corre el bloque con el rol del módulo y el alcance de datos del actor.</summary>
    public Task<T> ConAlcanceDelActor<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion) =>
        EnElEsquema(bloque, elevar: false, cancelacion);

    /// <summary>Corre el bloque con el rol del módulo y el alcance elevado a toda la red mientras dure. Solo después de que la Aplicación autorizó.</summary>
    public Task<T> ConAlcanceDeRed<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion) =>
        EnElEsquema(bloque, elevar: true, cancelacion);

    /// <summary>Dentro del bloque de <see cref="ConAlcanceDelActor{T}"/>: corre otro bloque con la elevación a la red (la comprobación de existencia que distingue la denegación por alcance de la falta de stock).</summary>
    public Task<T> ElevarDentro<T>(Func<CancellationToken, Task<T>> bloque, CancellationToken cancelacion) => elevacion.ConAlcanceDeRed(bloque, cancelacion);

    private async Task<T> EnElEsquema<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, bool elevar, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(bloque);
        await ExigirActor(cancelacion).ConfigureAwait(false);

        var conexionActiva = (NpgsqlConnection)conexion.Conexion;
        var transaccion = (NpgsqlTransaction)conexion.Transaccion;
        T resultado = default!;

        try
        {
            await elevacion.TraducirRechazoDeLaPolitica(async () =>
            {
                if (elevar)
                {
                    resultado = await elevacion.EjecutarComoConAlcanceDeRed(Esquema, t => bloque(conexionActiva, transaccion, t), cancelacion).ConfigureAwait(false);
                    return 0;
                }

                await unidad.EjecutarComo(
                    Esquema,
                    async token => resultado = await bloque(conexionActiva, transaccion, token).ConfigureAwait(false),
                    cancelacion).ConfigureAwait(false);
                return 0;
            }).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == EsperaDeBloqueoAgotada)
        {
            // La lock_timeout de la transacción venció esperando una fila de stock: «bloqueo ocupado» y no un error del servidor.
            throw new EsperaDeBloqueoAgotadaException(ex);
        }

        return resultado;
    }

    private async Task ExigirActor(CancellationToken cancelacion)
    {
        // Un actor con contexto Y un alcance de datos que la unidad de trabajo pudo fijar para él: un encargado sin sucursal o un rol desconocido tienen actor pero no alcance, y las políticas
        // les devolverían cero filas sin error igual que a quien no tiene actor.
        if (actores.Actual is { Tipo: not TipoDeActor.Anonimo } && alcance.HayAlcanceFijado)
        {
            return;
        }

        try
        {
            await alertas.Publicar(new AlertaTecnica(MotivoDeAlertaSinActor, reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // La alerta no debe tapar el rechazo: si ni siquiera se puede alertar, la operación igual falla cerrada.
        }

        throw new ContextoDeActorAusenteException("Se intentó acceder al stock sin un actor con alcance de datos: una ruta de servicio debe establecer el actor de integración y un trabajo de fondo, el del sistema.");
    }
}

/// <summary>El registro del acceso al esquema: aquí vive el de la elevación porque solo la Infraestructura de <c>Fuentes</c> (y <c>Ajustes</c>) puede depender de ella (D09).</summary>
internal static class RegistroDeAccesoAFuentes
{
    public static IServiceCollection AgregarAccesoAFuentes(this IServiceCollection servicios)
    {
        servicios.TryAddSingleton<IElevacionDeAlcance>(sp => (IElevacionDeAlcance)sp.GetRequiredService<IUnidadDeTrabajo>());
        servicios.AddScoped<AccesoAFuentes>();
        return servicios;
    }
}
