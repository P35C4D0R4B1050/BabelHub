namespace BabelHub.Ecommerce.Application.Plataforma.Interno;

/// <summary>Los datos de la restauración en curso: de cuándo es el respaldo restaurado (el sello se busca anterior a ese instante).</summary>
public sealed record OpcionesDeRestauracion(DateTimeOffset InstanteDelRespaldo);

/// <summary>
/// Paso 1 de la restauración (plan §5.1): verifica el registro de supresiones y su sello. Si no puede abrirse (registro ausente, entrada
/// alterada, sello que falta o no coincide sin la excepción registrada), el paso falla y el servicio no se abre (PLA-19).
/// </summary>
public sealed class PasoVerificarRegistroDeSupresiones(VerificarRegistroDeSupresiones verificar, OpcionesDeRestauracion opciones) : IPasoDeRestauracion
{
    public int Orden => OrdenDePasoDeRestauracion.Plataforma;

    public string Nombre => "verificar-registro-de-supresiones";

    /// <summary>Lo último que encontró la verificación, para el informe de la persona responsable.</summary>
    public ResultadoDeVerificacionDelRegistroDto? Ultimo { get; private set; }

    public async Task<ResultadoDePaso> Ejecutar(CancellationToken cancelacion = default)
    {
        var resultado = await verificar.Ejecutar(opciones.InstanteDelRespaldo, cancelacion).ConfigureAwait(false);
        Ultimo = resultado.EsExito ? resultado.Valor : null;
        return resultado.EsExito && resultado.Valor.PermiteAbrir ? ResultadoDePaso.Correcto : ResultadoDePaso.Fallido;
    }
}

/// <summary>
/// Paso 2 de la restauración (plan §5.1): vuelve a aplicar, de forma idempotente, las supresiones con cierre. Falla, y el servicio no se
/// abre, si una cuenta no pudo aplicarse o si el número de entradas nuevas supera el umbral sin la confirmación de la persona
/// responsable (PLA-19).
/// </summary>
public sealed class PasoReaplicarSupresiones(IReaplicadorDeSupresiones reaplicador) : IPasoDeRestauracion
{
    /// <summary>Un poco después de la verificación, dentro del mismo módulo.</summary>
    public int Orden => OrdenDePasoDeRestauracion.Plataforma + 10;

    public string Nombre => "reaplicar-supresiones";

    /// <summary>El informe de la última ejecución: sus contadores se versionan, su detalle con identificadores va solo a pantalla y a un archivo local.</summary>
    public InformeDeReaplicacion? Informe { get; private set; }

    public async Task<ResultadoDePaso> Ejecutar(CancellationToken cancelacion = default)
    {
        Informe = await reaplicador.Reaplicar(cancelacion).ConfigureAwait(false);
        return Informe.PermiteAbrir ? ResultadoDePaso.Correcto : ResultadoDePaso.Fallido;
    }
}
