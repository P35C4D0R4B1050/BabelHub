namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>Qué se hizo con la cuenta: se cancela o se anonimiza (PLA-37). Modelo propio del contrato: no expone tipos del Dominio.</summary>
public enum ClaseDeSupresion
{
    Cancelacion,
    Anonimizacion,
}

/// <summary>La intención se agrega antes de completar la supresión y el cierre al completarla (PLA-37).</summary>
public enum EtapaDeSupresion
{
    Intencion,
    Cierre,
}

/// <summary>Una entrada del registro de supresiones: solo el identificador opaco de la cuenta, nunca datos personales.</summary>
/// <param name="YaExistia">Verdadero si la entrada ya estaba escrita: repetirla es éxito (clave determinista) y trae la fecha de la original.</param>
public sealed record EntradaDeSupresionDto(
    string IdentificadorOpaco,
    ClaseDeSupresion Clase,
    EtapaDeSupresion Etapa,
    DateTimeOffset Fecha,
    string Suma,
    bool YaExistia);

/// <summary>El registro de supresiones no responde: la supresión no se completa si no puede agregarse la intención (PLA-37).</summary>
public sealed class RegistroDeSupresionesNoDisponibleException : Exception
{
    public RegistroDeSupresionesNoDisponibleException()
    {
    }

    public RegistroDeSupresionesNoDisponibleException(string message)
        : base(message)
    {
    }

    public RegistroDeSupresionesNoDisponibleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// El registro de supresiones, que vive fuera de las bases respaldadas y solo admite agregar (PLA-38). La clave de cada entrada es
/// determinista, de modo que repetir una escritura es idempotente y dos instancias de Identidad no necesitan coordinarse.
/// </summary>
public interface IRegistroDeSupresiones
{
    /// <summary>
    /// Agrega la entrada con la fecha indicada. Si ya existía, no la sobrescribe y devuelve la original con <c>YaExistia</c> en verdadero.
    /// Lanza <see cref="RegistroDeSupresionesNoDisponibleException"/> si el almacén no responde.
    /// </summary>
    Task<EntradaDeSupresionDto> Agregar(string identificadorOpaco, ClaseDeSupresion clase, EtapaDeSupresion etapa, DateTimeOffset fecha, CancellationToken cancelacion = default);

    /// <summary>La entrada de una cuenta, clase y etapa, o <c>null</c> si no existe.</summary>
    Task<EntradaDeSupresionDto?> Obtener(string identificadorOpaco, ClaseDeSupresion clase, EtapaDeSupresion etapa, CancellationToken cancelacion = default);

    /// <summary>Todas las entradas del registro (para reaplicarlas al restaurar).</summary>
    Task<IReadOnlyList<EntradaDeSupresionDto>> Leer(CancellationToken cancelacion = default);
}

/// <summary>Lo que dejó una reaplicación de supresiones al restaurar (PLA-19): solo identificadores opacos y contadores.</summary>
/// <param name="Reaplicadas">Cuentas cuyo cierre se volvió a aplicar.</param>
/// <param name="IntencionesSinCierre">Cuentas con intención y sin cierre (supresión no completada): siguen activas y se informan a la persona responsable.</param>
/// <param name="CierresSinIntencion">Cierres que no tienen intención previa: no se reaplican y se informan.</param>
/// <param name="RequiereConfirmacion">El número de entradas supera el umbral configurable frente al conteo previo: no se reaplicó sin confirmación explícita.</param>
public sealed record InformeDeReaplicacion(
    int Reaplicadas,
    IReadOnlyList<string> IntencionesSinCierre,
    IReadOnlyList<string> CierresSinIntencion,
    bool RequiereConfirmacion);

/// <summary>Vuelve a aplicar, de forma idempotente, las supresiones con cierre antes de abrir el servicio restaurado (PLA-19).</summary>
public interface IReaplicadorDeSupresiones
{
    Task<InformeDeReaplicacion> Reaplicar(CancellationToken cancelacion = default);
}

public enum ResultadoDeAplicacionDeSupresion
{
    Aplicada,

    /// <summary>La cuenta ya estaba suprimida o anonimizada: es éxito (idempotente).</summary>
    YaEstabaAplicada,

    /// <summary>No se pudo aplicar: el cierre queda pendiente y se reintenta sin deshacer nada (PLA-41).</summary>
    Fallida,
}

/// <summary>Lo implementa Identidad: anonimiza o cancela la cuenta y reenvía la baja al Inventario (IDE-75). Idempotente.</summary>
public interface IAplicadorDeSupresion
{
    Task<ResultadoDeAplicacionDeSupresion> Aplicar(string identificadorOpaco, ClaseDeSupresion clase, CancellationToken cancelacion = default);
}

/// <summary>Lo implementa Identidad: tras restaurar, rota la clave de firma e invalida las sesiones y renovaciones, también las de la capa servidor de la Web (PLA-43).</summary>
public interface IRestauracionDeIdentidad
{
    Task<ResultadoDePaso> RotarClavesEInvalidarSesiones(CancellationToken cancelacion = default);
}
