namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>Llamada entrante de una integración (por ejemplo, el aviso de la pasarela de pago) tal como llegó: el cuerpo crudo y su firma.</summary>
public sealed record EntradaDeIntegracion
{
    public EntradaDeIntegracion(string proveedor, string idDeEntrada, string marcaDeTiempo, ReadOnlyMemory<byte> cuerpo, string firma)
    {
        Proveedor = proveedor ?? string.Empty;
        IdDeEntrada = idDeEntrada ?? string.Empty;
        MarcaDeTiempo = marcaDeTiempo ?? string.Empty;
        Cuerpo = cuerpo;
        Firma = firma ?? string.Empty;
    }

    public string Proveedor { get; }

    /// <summary>Identificador del evento que da el proveedor; con el proveedor forma la clave única de la entrada.</summary>
    public string IdDeEntrada { get; }

    /// <summary>Segundos Unix en que el proveedor firmó la entrada; la firma cubre «{marca}.{cuerpo crudo}».</summary>
    public string MarcaDeTiempo { get; }

    public ReadOnlyMemory<byte> Cuerpo { get; }

    public string Firma { get; }
}

public enum EstadoDeEntradaDeIntegracion
{
    /// <summary>Verificada y reclamada: el llamador aplica el efecto y llama a <c>Completar</c> en la misma transacción.</summary>
    Aceptada,

    /// <summary>Ya se procesó: se responde el mismo éxito con el resultado guardado, sin repetir el efecto.</summary>
    Repetida,

    /// <summary>Otra llamada con el mismo identificador sigue en curso.</summary>
    EnCurso,

    /// <summary>No verificable: se responde un 401 uniforme, sin decir por qué; el motivo queda en el evento de seguridad.</summary>
    Rechazada,
}

public sealed record ResultadoDeEntradaDeIntegracion(EstadoDeEntradaDeIntegracion Estado, ResultadoMinimo? ResultadoGuardado = null);

/// <summary>Autentica y deduplica las llamadas entrantes de integraciones (SEG-12).</summary>
public interface IProcesadorDeEntradasDeIntegracion
{
    /// <summary>Comprueba proveedor, marca de tiempo, tope del cuerpo y firma, y reclama el identificador de la entrada.</summary>
    Task<ResultadoDeEntradaDeIntegracion> Recibir(EntradaDeIntegracion entrada, CancellationToken cancelacion = default);

    /// <summary>Guarda el resultado de una entrada aceptada para responder lo mismo a sus repeticiones.</summary>
    Task Completar(string proveedor, string idDeEntrada, ResultadoMinimo resultado, CancellationToken cancelacion = default);
}
