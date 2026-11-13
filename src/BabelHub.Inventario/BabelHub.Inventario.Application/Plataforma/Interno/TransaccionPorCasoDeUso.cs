namespace BabelHub.Inventario.Application.Plataforma.Interno;

/// <summary>
/// Decorador de un caso de uso: lo ejecuta en una sola transacción (SEG-04). Si termina con un fallo esperado o lanza una
/// excepción de negocio, revierte todos sus efectos y devuelve el error; si lanza cualquier otra excepción, revierte y la
/// deja subir para que la Api responda el error inesperado genérico. Es <c>public</c> solo para que la Infraestructura lo
/// registre (D25).
/// </summary>
public sealed class TransaccionPorCasoDeUso<TEntrada, TSalida>(ICasoDeUso<TEntrada, TSalida> interno, IUnidadDeTrabajo unidad)
    : ICasoDeUso<TEntrada, TSalida>
{
    public async Task<Resultado<TSalida>> Ejecutar(TEntrada entrada, CancellationToken cancelacion = default)
    {
        try
        {
            return await unidad.Ejecutar(
                async token =>
                {
                    var resultado = await interno.Ejecutar(entrada, token).ConfigureAwait(false);

                    // Un fallo esperado también deshace lo que el caso de uso alcanzó a hacer.
                    return resultado.EsExito ? resultado : throw new RevertirConFallo(resultado);
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (RevertirConFallo revertido)
        {
            return revertido.Resultado;
        }
        catch (ExcepcionDeNegocio negocio)
        {
            return Resultado<TSalida>.Fallo(negocio.ComoError());
        }
    }

    private sealed class RevertirConFallo(Resultado<TSalida> resultado) : Exception("El caso de uso terminó con un fallo esperado.")
    {
        public Resultado<TSalida> Resultado { get; } = resultado;
    }
}

/// <summary>Implementa <see cref="IEjecutorDeDependenciasNoNecesarias"/>: un fallo del efecto deja una alerta técnica y la operación continúa.</summary>
public sealed class EjecutorDeDependenciasNoNecesarias(IPublicadorDeAlertasTecnicas alertas, IReloj reloj) : IEjecutorDeDependenciasNoNecesarias
{
    /// <summary>Motivo del catálogo cerrado de alertas técnicas para esta falla.</summary>
    public const string MotivoDeAlerta = "dependencia-no-necesaria-fallida";

    public async Task<bool> Ejecutar(Func<CancellationToken, Task> efecto, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(efecto);

        try
        {
            await efecto(cancelacion).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await Alertar(cancelacion).ConfigureAwait(false);
            return false;
        }
    }

    private async Task Alertar(CancellationToken cancelacion)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(MotivoDeAlerta, reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Si ni siquiera se puede alertar, la operación sigue: la alerta no es parte de su efecto.
        }
    }
}
