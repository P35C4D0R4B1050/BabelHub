using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Biblioteca;

// El contrato de Biblioteca para Devoluciones (`IAccesosParaDevoluciones`, T09 de ecommerce-biblioteca-digital). Lo declara Devoluciones **como cambio aditivo mínimo** para poder consumirlo (el carril de Biblioteca lo adopta o lo ajusta
// con su implementación). Todas las operaciones participan en la transacción del llamador (una sola sentencia condicional sobre la fila del acceso, que es **lo último** en el orden de bloqueo) y casan por cuenta, pedido y edición:
// la cuenta sale del contrato de lectura de Pedidos, nunca del actor.

/// <summary>El estado de un acceso digital para Devoluciones.</summary>
public enum EstadoDelAccesoParaDevoluciones
{
    Vigente,
    Suspendido,
    Revocado,
}

/// <summary>El acceso de la cuenta a una edición de un pedido: su identificador, su estado y si se entregó algún enlace desde la concesión o la última reactivación (DEV-04, DEV-05).</summary>
public sealed record AccesoParaDevoluciones(Guid AccesoId, EstadoDelAccesoParaDevoluciones Estado, bool EnlaceEntregado);

/// <summary>El resultado de revocar un acceso solo si no se entregó un enlace (DEV-20, BIB-23): gana la primera operación que confirma.</summary>
public enum ResultadoDeRevocarSiNoHayEnlace
{
    Revocado,
    EnlaceYaEntregado,
    YaRevocado,
    SinAcceso,

    /// <summary>El acceso está suspendido (hay un reporte abierto): no se revoca por esta vía.</summary>
    NoDisponible,
}

public enum ResultadoDeSuspenderAcceso
{
    Suspendido,
    YaSuspendido,
    NoDisponible,
}

public enum ResultadoDeReanudarAcceso
{
    Reanudado,
    NoDisponible,
}

public interface IAccesosParaDevoluciones
{
    /// <summary>El acceso de la cuenta a esa edición de ese pedido, o <see langword="null"/> si no existe o es de otra compra («sin acceso»; el de un ajeno tampoco aparece).</summary>
    Task<Resultado<AccesoParaDevoluciones?>> Consultar(Guid cuentaId, Guid pedidoId, Guid edicionId, CancellationToken cancelacion = default);

    Task<Resultado<ResultadoDeRevocarSiNoHayEnlace>> RevocarSiNoHayEnlace(Guid cuentaId, Guid pedidoId, Guid edicionId, CancellationToken cancelacion = default);

    /// <summary>Suspende las descargas solo desde vigente (BIB-10).</summary>
    Task<Resultado<ResultadoDeSuspenderAcceso>> Suspender(Guid cuentaId, Guid accesoId, CancellationToken cancelacion = default);

    /// <summary>Reanuda las descargas solo desde suspendido (BIB-22).</summary>
    Task<Resultado<ResultadoDeReanudarAcceso>> Reanudar(Guid cuentaId, Guid accesoId, CancellationToken cancelacion = default);
}
