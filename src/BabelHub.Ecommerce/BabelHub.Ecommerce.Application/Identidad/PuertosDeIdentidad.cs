namespace BabelHub.Ecommerce.Application.Identidad;

/// <summary>
/// Los puertos que <b>Identidad define y otros módulos implementan</b> (D10, IDE-27, IDE-47, IDE-70, IDE-84): Identidad no depende de ellos, ellos dependen de este contrato. Cada puerto se llama dentro de la
/// transacción de la operación de Identidad y, si falla, Identidad falla cerrada (nada se aplica a medias). Los identificadores viajan opacos; ningún puerto devuelve datos personales de otro módulo.
/// </summary>
public interface IPendientesDeCuenta
{
    /// <summary>
    /// Cuántos pendientes tiene la cuenta en los módulos y el Inventario (pedidos, envíos, devoluciones, reembolsos, reservas…): la cancelación solo se ejecuta con cero (IDE-27) y un reemplazo (IDE-84) también.
    /// Si no se puede contar, lanza: nunca se asume cero.
    /// </summary>
    Task<int> Contar(Guid usuarioId, CancellationToken cancelacion = default);
}

/// <summary>No se pudo contar los pendientes de una cuenta: la cancelación o el reemplazo fallan cerrados (IDE-27, IDE-84).</summary>
public sealed class PendientesNoDisponiblesException : InvalidOperationException
{
    public PendientesNoDisponiblesException()
        : base("No se pudieron contar los pendientes de la cuenta.")
    {
    }

    public PendientesNoDisponiblesException(string message)
        : base(message)
    {
    }

    public PendientesNoDisponiblesException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Los pendientes de una cuenta en todos los módulos y el Inventario (IDE-27): suma lo que cuenta cada <see cref="IPendientesDeCuenta"/> concreto. La composición (T44) lo construye <b>por fábrica</b> con la lista
/// explícita de las implementaciones (Pagos, Reclamaciones, Inventario…), sin registrarlo como <see cref="IPendientesDeCuenta"/> de la enumeración, para no recursar. Falla cerrado: <b>sin ningún módulo, con un
/// conteo negativo o si uno solo no puede contar, lanza</b>; nunca se asume cero.
/// </summary>
public sealed class PendientesDeCuentaCompuestos(IReadOnlyList<IPendientesDeCuenta> modulos) : IPendientesDeCuenta
{
    public async Task<int> Contar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (modulos.Count == 0)
        {
            throw new PendientesNoDisponiblesException("No hay ningún módulo que cuente los pendientes.");
        }

        var total = 0;
        foreach (var modulo in modulos)
        {
            var cuenta = await modulo.Contar(usuarioId, cancelacion).ConfigureAwait(false);
            total = cuenta < 0 ? throw new PendientesNoDisponiblesException("Un módulo devolvió un conteo inválido de pendientes.") : checked(total + cuenta);
        }

        return total;
    }
}

/// <summary>Lo que Carrito implementa para que Identidad elimine el carrito de una cuenta de cliente sin verificar que se reemplaza (IDE-84, IDE-88): si no puede, lanza y el reemplazo no se hace.</summary>
public interface ICarritoDeCuentaReemplazada
{
    Task Eliminar(Guid usuarioId, CancellationToken cancelacion = default);
}

/// <summary>Lo que Notificaciones implementa para cancelar los avisos pendientes de una cuenta o un número (IDE-31, IDE-69): al cambiar el consentimiento o el número, desactivar o cancelar la cuenta.</summary>
public interface ICancelacionDeAvisos
{
    Task CancelarLosAvisosDe(Guid usuarioId, CancellationToken cancelacion = default);
}

/// <summary>Lo que cada módulo receptor implementa para la cancelación (IDE-47, IDE-70, IDE-89): anonimiza, desvincula, elimina o marca lo suyo en la misma transacción; si falla, todo se revierte.</summary>
public interface IAplicadorDeCancelacion
{
    /// <summary>El nombre estable del módulo (para la prueba de reversión por módulo y verbo).</summary>
    string Modulo { get; }

    Task Aplicar(Guid usuarioId, Guid identificadorOpaco, CancellationToken cancelacion = default);
}

/// <summary>Entrega la baja de un titular al Inventario con solo el identificador opaco (IDE-64): idempotente y reintentable.</summary>
public interface IEntregaDeBajas
{
    Task<bool> Entregar(Guid identificadorOpaco, CancellationToken cancelacion = default);
}
