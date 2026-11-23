using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Ajustes;

// El contrato público de la carga inicial del inventario (transversal-carga-inicial, CIN-07 a CIN-14): la orden de operación `cargar-stock` lo llama por la raíz del módulo, sin tocar su Interno
// (regla 13). Son modelos propios: ni DTOs de Shared ni tipos del Dominio. Ningún tipo lleva datos personales ni el contenido de los archivos en su ToString.

/// <summary>
/// Lo que la orden entrega a la carga: el <b>texto</b> ya leído (con su tope de 2 MB) del plan de inventario (JSON v1), de las sucursales (<c>codigo;nombre;region</c>, la primera es la recepción) y, si hay, de las
/// rutas (<c>origen;destino;dias</c>). <see cref="Esperar"/> pide esperar, hasta 2 horas, a que se libere el presupuesto por hora del personal (SEG-30); <see cref="Espera"/> reemplaza la espera real en las pruebas.
/// </summary>
public sealed record SolicitudDeCargaDeInventario(string? TextoDelPlan, string? TextoDeLasSucursales, string? TextoDeLasRutas, bool Esperar, Func<TimeSpan, CancellationToken, Task>? Espera = null)
{
    public override string ToString() => nameof(SolicitudDeCargaDeInventario);
}

/// <summary>
/// Lo cargado en una corrida, sin datos personales: <see cref="HuellaDelPlan"/> es el SHA-256 del plan; <see cref="ReanudaDesde"/> tiene valor si el presupuesto se agotó y la carga se detuvo (repetir la orden la
/// reanuda); <see cref="RegistroDeLaCorridaFallo"/> indica que no se pudo dejar el registro de la corrida en la auditoría. <see cref="Problemas"/> son claves simbólicas (sucursal o edición y motivo), nunca valores secretos.
/// </summary>
public sealed record ResumenDeCargaDeInventario(
    Guid CorridaId,
    string HuellaDelPlan,
    int SucursalesNuevas,
    int SucursalesExistentes,
    int RutasNuevas,
    int RutasExistentes,
    int EdicionesRegistradas,
    int StockCargado,
    int YaCargado,
    int Rechazadas,
    int PendientesPorLimite,
    DateTimeOffset? ReanudaDesde,
    bool RegistroDeLaCorridaFallo,
    IReadOnlyList<string> Problemas)
{
    /// <summary>Todo cargado o ya cargado: salida 0.</summary>
    public bool Completa => Rechazadas == 0 && PendientesPorLimite == 0 && !RegistroDeLaCorridaFallo;
}

/// <summary>
/// La carga inicial del inventario (CIN-12 a CIN-14): solo la administración central. Valida <b>todo</b> antes de escribir (un archivo inválido es un fallo de validación con el archivo y la línea), asegura las sucursales
/// y las rutas sin modificar las que existen, hace que el Inventario conozca cada edición impresa del plan y deja su stock físico en cada sucursal con las operaciones y reglas del propio Inventario: ingreso de mercadería en
/// la recepción y corrección de conteo en las demás. Es idempotente y reanudable.
/// </summary>
public interface ICargaInicialDelInventario
{
    Task<Resultado<ResumenDeCargaDeInventario>> Cargar(SolicitudDeCargaDeInventario solicitud, CancellationToken cancelacion = default);
}
