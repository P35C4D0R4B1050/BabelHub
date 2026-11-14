namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>El sello que VM-AUX escribe cada hora en <c>sellos/</c> (modelo del contrato, sin tipos del Dominio).</summary>
public sealed record SelloDelRegistroDto(DateTimeOffset Instante, int Cantidad, string SumaDelConjunto);

/// <summary>Los sellos del registro de supresiones (los escribe VM-AUX con su propia credencial; el servicio solo los lee).</summary>
public interface ISellosDelRegistro
{
    /// <summary>El sello más reciente anterior al instante del respaldo restaurado, o <c>null</c> si no hay ninguno.</summary>
    Task<SelloDelRegistroDto?> ObtenerAnteriorA(DateTimeOffset instante, CancellationToken cancelacion = default);
}

/// <summary>
/// La decisión explícita de la persona responsable de abrir pese a que el sello falta o no coincide, tomada tras verificar contra la
/// copia del registro y el ancla enviada por correo y registrada en <c>progress/ensayos/</c> con fecha, sello y decisión, sin
/// identificadores (vía de excepción de PLA-19: «no se abre» no es un bloqueo permanente).
/// </summary>
public sealed record ExcepcionDelRegistroDto(DateTimeOffset InstanteDelRespaldo, DateTimeOffset Fecha);

public interface IExcepcionesDelRegistro
{
    /// <summary>La decisión registrada para este respaldo, o <c>null</c> si no hay.</summary>
    Task<ExcepcionDelRegistroDto?> Obtener(DateTimeOffset instanteDelRespaldo, CancellationToken cancelacion = default);
}

/// <summary>El estado de la verificación del registro de supresiones antes de abrir el servicio restaurado (PLA-19).</summary>
public enum EstadoDelRegistro
{
    Valido,
    RegistroAusente,
    SelloAusente,
    EntradaAlterada,
    EntradasAusentes,
    SumaDelConjuntoNoCoincide,
}

/// <param name="Estado">Lo que encontró la verificación.</param>
/// <param name="ClavesAfectadas">Las claves de las entradas alteradas (llevan identificadores opacos, nunca datos personales).</param>
/// <param name="AbiertoPorExcepcion">La verificación no coincidía, pero la persona responsable decidió abrir y lo registró.</param>
public sealed record ResultadoDeVerificacionDelRegistroDto(EstadoDelRegistro Estado, IReadOnlyList<string> ClavesAfectadas, bool AbiertoPorExcepcion)
{
    /// <summary>Solo un registro válido, o uno cuyo sello falta o no coincide con una excepción registrada, permite abrir.</summary>
    public bool PermiteAbrir => Estado == EstadoDelRegistro.Valido || AbiertoPorExcepcion;
}
