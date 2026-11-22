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

/// <summary>
/// Los dos números que informa Pedidos para una cuenta (PED-31): pedidos con alguna línea impresa sin entregar ni anular (incluidas las no recogidas) y reembolsos pendientes de anulación y de cancelación del cliente.
/// Solo números de esa cuenta; nunca datos de otra ni de los pedidos.
/// </summary>
public readonly record struct PendientesDeCuentaPorTipo(int PedidosSinCerrar, int ReembolsosPendientes)
{
    /// <summary>La suma que cuenta <see cref="IPendientesDeCuenta.Contar"/>.</summary>
    public int Total => checked(PedidosSinCerrar + ReembolsosPendientes);
}

/// <summary>
/// Los pendientes de una cuenta <b>separados por tipo</b> (PED-31, AC-PED-26): extiende <see cref="IPendientesDeCuenta"/> sin cambiarlo (<see cref="IPendientesDeCuenta.Contar"/> sigue devolviendo la suma, y la suma es
/// <see cref="PendientesDeCuentaPorTipo.Total"/>). Lo implementa Pedidos; mismas reglas: falla cerrado con <see cref="PendientesNoDisponiblesException"/> y nunca asume cero.
/// </summary>
public interface IPendientesDeCuentaPorTipo : IPendientesDeCuenta
{
    Task<PendientesDeCuentaPorTipo> ContarPorTipo(Guid usuarioId, CancellationToken cancelacion = default);
}

/// <summary>
/// Los tres números que informa Devoluciones para una cuenta (DEV-23), sin contar un mismo caso en dos: devoluciones abiertas de impresos (alguna línea solicitada o aprobada), reportes de archivo defectuoso sin decidir y
/// reembolsos pendientes (línea recibida de un impreso o aprobada de un digital, hasta que la pasarela confirma). Solo números de esa cuenta; nunca motivos, textos ni importes.
/// </summary>
public readonly record struct PendientesDeDevolucionesDeCuenta(int DevolucionesAbiertas, int ReportesSinDecidir, int ReembolsosPendientes)
{
    /// <summary>La suma que cuenta <see cref="IPendientesDeCuenta.Contar"/>.</summary>
    public int Total => checked(DevolucionesAbiertas + ReportesSinDecidir + ReembolsosPendientes);
}

/// <summary>
/// Los pendientes de Devoluciones de una cuenta <b>en sus tres números</b> (DEV-23, AC-DEV-25): extiende <see cref="IPendientesDeCuenta"/> sin cambiarlo (<see cref="IPendientesDeCuenta.Contar"/> sigue devolviendo la suma, que es
/// <see cref="PendientesDeDevolucionesDeCuenta.Total"/>). Lo implementa Devoluciones; mismas reglas: falla cerrado con <see cref="PendientesNoDisponiblesException"/> y nunca asume cero.
/// </summary>
public interface IPendientesDeDevolucionesDeCuenta : IPendientesDeCuenta
{
    Task<PendientesDeDevolucionesDeCuenta> ContarEnTres(Guid usuarioId, CancellationToken cancelacion = default);
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
public sealed class PendientesDeCuentaCompuestos(IReadOnlyList<IPendientesDeCuenta> modulos) : IDesgloseDePendientesDeCuenta
{
    public async Task<int> Contar(Guid usuarioId, CancellationToken cancelacion = default)
        => (await Desglosar(usuarioId, cancelacion).ConfigureAwait(false)).Total;

    /// <summary>
    /// Los mismos pendientes en cuatro grupos para decirle al cliente cuáles faltan (IDE-27, D5): usa los contratos por tipo de Pedidos (PED-31) y de Devoluciones (DEV-23) cuando el módulo los ofrece y deja en «otros»
    /// lo que cuenta cada uno de los demás (reservas del Inventario, pagos, reclamos). Una sola llamada por módulo; mismas reglas de falla cerrada que <see cref="Contar"/>.
    /// </summary>
    public async Task<DesgloseDePendientes> Desglosar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (modulos.Count == 0)
        {
            throw new PendientesNoDisponiblesException("No hay ningún módulo que cuente los pendientes.");
        }

        int pedidos = 0, reembolsos = 0, devoluciones = 0, otros = 0;
        foreach (var modulo in modulos)
        {
            switch (modulo)
            {
                case IPendientesDeCuentaPorTipo porTipo:
                    var deLosPedidos = await porTipo.ContarPorTipo(usuarioId, cancelacion).ConfigureAwait(false);
                    Validar(deLosPedidos.PedidosSinCerrar, deLosPedidos.ReembolsosPendientes);
                    pedidos = checked(pedidos + deLosPedidos.PedidosSinCerrar);
                    reembolsos = checked(reembolsos + deLosPedidos.ReembolsosPendientes);
                    break;
                case IPendientesDeDevolucionesDeCuenta enTres:
                    var deLasDevoluciones = await enTres.ContarEnTres(usuarioId, cancelacion).ConfigureAwait(false);
                    Validar(deLasDevoluciones.DevolucionesAbiertas, deLasDevoluciones.ReportesSinDecidir, deLasDevoluciones.ReembolsosPendientes);
                    devoluciones = checked(devoluciones + deLasDevoluciones.DevolucionesAbiertas + deLasDevoluciones.ReportesSinDecidir);
                    reembolsos = checked(reembolsos + deLasDevoluciones.ReembolsosPendientes);
                    break;
                default:
                    var cuenta = await modulo.Contar(usuarioId, cancelacion).ConfigureAwait(false);
                    Validar(cuenta);
                    otros = checked(otros + cuenta);
                    break;
            }
        }

        return new DesgloseDePendientes(pedidos, reembolsos, devoluciones, otros);
    }

    private static void Validar(params int[] cuentas)
    {
        if (cuentas.Any(c => c < 0))
        {
            throw new PendientesNoDisponiblesException("Un módulo devolvió un conteo inválido de pendientes.");
        }
    }
}

/// <summary>
/// Lo que le falta cerrar a una cuenta para poder cancelarse, en grupos (IDE-27, D5): pedidos con una impresa sin cerrar, reembolsos pendientes (de Pedidos y de Devoluciones), devoluciones y reportes abiertos, y «otros»
/// (reservas del Inventario, pagos y reclamos). Solo números de esa cuenta; nunca datos de otro módulo.
/// </summary>
public readonly record struct DesgloseDePendientes(int PedidosSinCerrar, int Reembolsos, int Devoluciones, int Otros)
{
    public int Total => checked(PedidosSinCerrar + Reembolsos + Devoluciones + Otros);
}

/// <summary>Un conjunto de pendientes que además sabe separarse en grupos (D5): lo implementa el compuesto de Identidad; <see cref="IPendientesDeCuenta.Contar"/> sigue siendo la suma.</summary>
public interface IDesgloseDePendientesDeCuenta : IPendientesDeCuenta
{
    Task<DesgloseDePendientes> Desglosar(Guid usuarioId, CancellationToken cancelacion = default);
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

/// <summary>
/// Lo que cada módulo receptor implementa para los cambios de cuenta que le importan (IDE-99, DD-26): Reclamaciones vincula a la cuenta las hojas del correo anterior de un cliente antes de que ese correo quede libre.
/// Se llama <b>dentro de la transacción</b> del cambio de correo de un cliente (<see cref="AlCambiarElCorreo"/>) o de la de reemplazo de una cuenta sin verificar (<see cref="AlReemplazarLaCuenta"/>); si lanza, no se
/// aplica el cambio ni el reemplazo. El correo anterior viaja <b>solo en memoria</b>: el receptor no lo guarda, no lo audita ni lo registra, y cada receptor escribe bajo su propio <c>EjecutarComo</c> desde su Infraestructura (D23).
/// </summary>
public interface IReceptorDeCambiosDeCuenta
{
    /// <summary>El nombre estable del módulo (define el orden de llamada, el mismo de la cancelación).</summary>
    string Modulo { get; }

    /// <summary>Se llama antes de liberar el correo anterior de un cliente (ya normalizado): el receptor lo usa solo para vincular lo suyo a la cuenta.</summary>
    Task AlCambiarElCorreo(Guid usuarioId, string correoAnteriorNormalizado, CancellationToken cancelacion = default);

    /// <summary>Se llama antes de eliminar una cuenta de cliente sin verificar que se reemplaza (IDE-84): el receptor desvincula lo suyo.</summary>
    Task AlReemplazarLaCuenta(Guid usuarioId, CancellationToken cancelacion = default);
}

/// <summary>Un módulo receptor no pudo aplicar un cambio de cuenta, o no hay ninguno registrado (IDE-99): el cambio o el reemplazo no se aplican. El mensaje es fijo y nunca lleva el correo ni la causa del receptor.</summary>
public sealed class CambiosDeCuentaNoAplicablesException : InvalidOperationException
{
    public CambiosDeCuentaNoAplicablesException()
        : base("Un módulo receptor no pudo aplicar el cambio de cuenta.")
    {
    }

    public CambiosDeCuentaNoAplicablesException(string message)
        : base(message)
    {
    }

    public CambiosDeCuentaNoAplicablesException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>El nombre del módulo que falló (sin datos del cambio), o <see langword="null"/> si no había ninguno.</summary>
    public string? Modulo { get; init; }
}

/// <summary>
/// Los receptores de los cambios de cuenta como uno solo (IDE-99): los llama por <see cref="IReceptorDeCambiosDeCuenta.Modulo"/> en el orden de la cancelación y <b>falla cerrado</b> si no hay ninguno (como
/// <see cref="PendientesDeCuentaCompuestos"/>) o si uno lanza. Descarta el mensaje y la causa del receptor: el correo anterior viaja como parámetro y no debe llegar a un log ni a un mensaje. Cada receptor escribe
/// bajo su propio rol desde su Infraestructura; este compuesto no conoce esquemas.
/// </summary>
public sealed class ReceptoresDeCambiosDeCuentaCompuestos(IReadOnlyList<IReceptorDeCambiosDeCuenta> receptores)
{
    public Task AlCambiarElCorreo(Guid usuarioId, string correoAnteriorNormalizado, CancellationToken cancelacion = default) =>
        Llamar(receptor => receptor.AlCambiarElCorreo(usuarioId, correoAnteriorNormalizado, cancelacion), cancelacion);

    public Task AlReemplazarLaCuenta(Guid usuarioId, CancellationToken cancelacion = default) =>
        Llamar(receptor => receptor.AlReemplazarLaCuenta(usuarioId, cancelacion), cancelacion);

    private async Task Llamar(Func<IReceptorDeCambiosDeCuenta, Task> llamada, CancellationToken cancelacion)
    {
        if (receptores.Count == 0)
        {
            throw new CambiosDeCuentaNoAplicablesException("No hay ningún módulo receptor de los cambios de cuenta.");
        }

        foreach (var receptor in Interno.EjecutorDeLaCancelacion.EnElOrden(receptores, r => r.Modulo))
        {
            try
            {
                await llamada(receptor).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Sin el mensaje ni la causa del receptor (podrían llevar el correo anterior): solo el módulo que falló.
                throw new CambiosDeCuentaNoAplicablesException { Modulo = receptor.Modulo };
            }
        }
    }
}
