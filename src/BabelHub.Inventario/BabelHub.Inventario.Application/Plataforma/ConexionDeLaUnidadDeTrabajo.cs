using System.Data.Common;

namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>
/// La conexión y la transacción de la unidad de trabajo en curso, para que los adaptadores de Infraestructura de cada módulo
/// (por ejemplo, el contexto de EF Core) escriban dentro de la misma transacción del caso de uso. Solo la usa la
/// Infraestructura: los casos de uso no tocan la conexión.
/// </summary>
public interface IConexionDeLaUnidadDeTrabajo
{
    bool HayTransaccion { get; }

    /// <summary>La conexión de la transacción activa; lanza si no hay una.</summary>
    DbConnection Conexion { get; }

    DbTransaction Transaccion { get; }
}
