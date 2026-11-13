using BabelHub.Inventario.Domain.Plataforma;

namespace BabelHub.Inventario.Application.Plataforma.Interno;

/// <summary>Estado de un umbral entre evaluaciones: si ya estaba superado y el instante de la última evaluación (marca de agua).</summary>
public sealed record EstadoDeUmbral(bool Superado, DateTimeOffset UltimaEvaluacion);

/// <summary>Guarda el estado de cada umbral para que el trabajo de fondo dispare la alerta solo al cruzarlo, aun tras un reinicio.</summary>
public interface IEstadoDeUmbrales
{
    Task<EstadoDeUmbral?> Obtener(string motivo, CancellationToken cancelacion);

    Task Guardar(string motivo, EstadoDeUmbral estado, CancellationToken cancelacion);
}

/// <summary>Qué pasó con un umbral en una evaluación.</summary>
public sealed record ResultadoDeUmbral(string Motivo, int Total, bool Superado, bool AlertaPublicada);

/// <summary>
/// Evalúa los umbrales de alerta de seguridad de SEG-09 (más de 10 accesos denegados o más de 20 ingresos fallidos en 5
/// minutos). Consulta el conteo de los últimos 5 minutos a <see cref="IContadorDeEventosDeSeguridad"/> y publica la alerta
/// solo al cruzar el umbral; mientras siga superado no vuelve a publicar, para no inflar las ocurrencias. La cuenta por
/// ventana, la agrupación y el correo con su tope son de <c>Auditoria</c>.
/// </summary>
public sealed class EvaluarUmbralesDeAlerta(
    IReadOnlyList<UmbralDeAlerta> umbrales,
    IContadorDeEventosDeSeguridad contador,
    IPublicadorDeAlertasDeSeguridad alertas,
    IEstadoDeUmbrales estados,
    IReloj reloj)
{
    public async Task<IReadOnlyList<ResultadoDeUmbral>> Ejecutar(CancellationToken cancelacion = default)
    {
        var ahora = reloj.Ahora;
        var resultados = new List<ResultadoDeUmbral>(umbrales.Count);

        foreach (var umbral in umbrales)
        {
            resultados.Add(await Evaluar(umbral, ahora, cancelacion).ConfigureAwait(false));
        }

        return resultados;
    }

    private async Task<ResultadoDeUmbral> Evaluar(UmbralDeAlerta umbral, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        var previo = await estados.Obtener(umbral.Motivo, cancelacion).ConfigureAwait(false);

        // Una evaluación que no es más nueva que la última ya hecha (dos corridas del mismo minuto) no cambia nada.
        if (previo is not null && ahora <= previo.UltimaEvaluacion)
        {
            return new ResultadoDeUmbral(umbral.Motivo, 0, previo.Superado, AlertaPublicada: false);
        }

        var total = await contador.Contar(umbral.Motivo, ahora - umbral.Ventana, ahora, cancelacion).ConfigureAwait(false);
        var superado = umbral.Supera(total);
        var alertar = superado && previo?.Superado != true;

        if (alertar)
        {
            // Primero se publica: si falla, el estado no avanza y la próxima corrida lo reintenta.
            await alertas.Publicar(new AlertaDeSeguridad(umbral.Motivo, ahora, total), cancelacion).ConfigureAwait(false);
        }

        await estados.Guardar(umbral.Motivo, new EstadoDeUmbral(superado, ahora), cancelacion).ConfigureAwait(false);
        return new ResultadoDeUmbral(umbral.Motivo, total, superado, alertar);
    }
}
