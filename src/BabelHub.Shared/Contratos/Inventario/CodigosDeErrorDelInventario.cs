namespace BabelHub.Shared.Contratos.Inventario;

/// <summary>
/// Códigos estables de error del Inventario que el E-commerce interpreta (<c>application/problem+json</c>, campo <c>code</c>). Los genéricos
/// repiten <c>CodigosDeError</c> con el prefijo <c>INV</c>; la interfaz y las pruebas dependen del código, no del texto.
/// </summary>
public static class CodigosDeErrorDelInventario
{
    public const string NoAutenticado = "INV-NO-AUTENTICADO";

    public const string Prohibido = "INV-PROHIBIDO";

    public const string NoEncontrado = "INV-NO-ENCONTRADO";

    public const string ValidacionInvalida = "INV-VALIDACION-INVALIDA";

    public const string LimiteExcedido = "INV-LIMITE-EXCEDIDO";

    public const string ServicioNoDisponible = "INV-SERVICIO-NO-DISPONIBLE";

    /// <summary>409: una línea no tiene stock vendible suficiente en ninguna fuente (SEL-05); el cuerpo es <c>ProblemaDeStockDto</c>.</summary>
    public const string StockInsuficiente = "INV-STOCK-INSUFICIENTE";

    /// <summary>422: la edición no es impresa y no tiene stock (FUE-07, SEL-12, AJU-18, TRA-20).</summary>
    public const string EdicionNoImpresa = "INV-EDICION-NO-IMPRESA";

    /// <summary>409: la edición ya se registró con otro formato; un formato no cambia.</summary>
    public const string FormatoEnConflicto = "INV-FORMATO-EN-CONFLICTO";

    /// <summary>409: la reserva no está en un estado que admita la operación: vencida, liberada, cerrada o ya aplicada otra (SEL-27 a SEL-29).</summary>
    public const string ReservaEstadoInvalido = "INV-RESERVA-ESTADO-INVALIDO";

    /// <summary>409: la clave de idempotencia ya se usó con otra carga (SEL-31).</summary>
    public const string IdempotenciaEnConflicto = "INV-IDEMPOTENCIA-EN-CONFLICTO";

    /// <summary>409: la misma clave de idempotencia sigue procesándose; se reintenta sin contar como solicitud nueva.</summary>
    public const string IdempotenciaEnCurso = "INV-IDEMPOTENCIA-EN-CURSO";
}
