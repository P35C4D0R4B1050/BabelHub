using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>
/// La ejecución de la cancelación de una cuenta en <b>una sola transacción</b> (T23; IDE-47, IDE-48, IDE-62, IDE-64, IDE-69, IDE-70, IDE-75, IDE-89; AC-IDE-43, AC-IDE-51, AC-IDE-57). Se llama dentro de la transacción de quien
/// solicita o reconfirma. En orden: (1) la <b>intención</b> en el registro de supresiones, que vive fuera de las bases: si no se puede agregar, no se ejecuta nada (IDE-75); (2) cada módulo del E-commerce anonimiza, desvincula,
/// elimina o marca lo suyo por su puerto, y el esquema <c>identidad</c> aplica su parte de la matriz; (3) se cancelan los avisos pendientes y se revocan las renovaciones; (4) la cuenta se desactiva y se <b>anonimiza</b>
/// (el nombre, el correo —aleatorio, no derivado del original, de modo que el original pueda registrarse otra vez como una cuenta nueva, IDE-48—, el número y la contraseña); (5) se guarda la baja del titular y el cierre pendiente en la
/// misma transacción (IDE-64); (6) se audita con el titular como actor y sin datos personales (IDE-62). <b>Cualquier fallo lanza y todo se revierte</b> (IDE-70): la intención sin cierre no cuenta (PLA-19) y la cancelación
/// se puede reintentar. El cierre y la entrega de la baja los reintenta <see cref="ReintentoDeBajas"/> sin deshacer la cancelación.
/// </summary>
public sealed class EjecutorDeLaCancelacion(
    IRegistroDeSupresiones registro,
    IEnumerable<IAplicadorDeCancelacion> aplicadores,
    IPurgaDeIdentidad purga,
    ICancelacionDeAvisos cancelacionDeAvisos,
    IRepositorioDeCuentas cuentas,
    RevocacionDeSesiones revocacion,
    IRepositorioDeCancelaciones cancelaciones,
    IRepositorioDeBajas bajas,
    IPorAnonimizar porAnonimizar,
    IGeneradorDeSecretos secretos,
    IAuditoria auditoria,
    IReloj reloj) : IEjecutorDeLaCancelacion
{
    public const string CausaPorSolicitudDelTitular = "solicitud-del-titular";

    public const string CausaPorReconfirmacion = "reconfirmacion-tras-pendientes";

    public const string NombreAnonimo = "Cuenta cancelada";

    /// <summary>El orden en que se llama a los receptores (decisión D04 de <c>ecommerce-devoluciones</c> y orden global de bloqueo AD18): Pedidos, Devoluciones, Pagos, Carrito, Biblioteca y Reclamaciones; un módulo que no figura va al final, en el orden en que se registró. No depende del orden de la composición.</summary>
    internal static readonly string[] OrdenDeLosReceptores = ["pedidos", "devoluciones", "pagos", "carrito", "biblioteca", "reclamaciones"];

    public async Task Ejecutar(CuentaDeIdentidad cuenta, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(cuenta);
        var usuarioId = cuenta.Usuario.Id;

        // (1) La intención va ANTES de ejecutar y fuera de las bases: si el registro no responde, la excepción sube y no se ejecuta nada (IDE-75, IDE-70).
        await registro.Agregar(usuarioId.ToString("D"), ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Intencion, reloj.Ahora, cancelacion).ConfigureAwait(false);

        var causa = await cancelaciones.AbiertaDe(usuarioId, paraActualizar: true, cancelacion).ConfigureAwait(false) is null ? CausaPorSolicitudDelTitular : CausaPorReconfirmacion;
        await Aplicar(cuenta, conBaja: true, cancelacion).ConfigureAwait(false);

        // (6) La auditoría del titular, sin datos personales en el detalle; si no se puede auditar, la cancelación no se ejecuta.
        await auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.CuentaCancelada)
            {
                ObjetoTipo = "cuenta",
                ObjetoId = usuarioId.ToString("D"),
                Detalle = new Dictionary<string, string?> { ["causa"] = causa },
            },
            cancelacion).ConfigureAwait(false);
    }

    /// <summary>
    /// Vuelve a aplicar la cancelación o la anonimización de una cuenta tras restaurar un respaldo (PLA-19, IDE-75): <b>sin intención</b> (el registro ya la tiene) ni consulta de pendientes, de forma idempotente y sin
    /// auditar de nuevo. Una anonimización no entrega baja al Inventario.
    /// </summary>
    public Task Reaplicar(CuentaDeIdentidad cuenta, bool conBaja, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(cuenta);
        return Aplicar(cuenta, conBaja, cancelacion);
    }

    internal static IEnumerable<IAplicadorDeCancelacion> EnElOrdenDeLosReceptores(IEnumerable<IAplicadorDeCancelacion> receptores) => EnElOrden(receptores, receptor => receptor.Modulo);

    /// <summary>El orden de llamada de los módulos receptores (cancelación y cambios de cuenta): el de <see cref="OrdenDeLosReceptores"/>, los desconocidos al final y, entre iguales, el de registro.</summary>
    internal static IEnumerable<T> EnElOrden<T>(IEnumerable<T> receptores, Func<T, string> modulo) =>
        receptores
            .Select((receptor, posicion) => (receptor, posicion, rango: Array.IndexOf(OrdenDeLosReceptores, modulo(receptor))))
            .OrderBy(r => r.rango < 0 ? int.MaxValue : r.rango)
            .ThenBy(r => r.posicion)
            .Select(r => r.receptor);

    private async Task Aplicar(CuentaDeIdentidad cuenta, bool conBaja, CancellationToken cancelacion)
    {
        var usuarioId = cuenta.Usuario.Id;
        var ahora = reloj.Ahora;

        // (2) Cada módulo receptor, con el mismo identificador opaco; si uno falla, todo se revierte.
        foreach (var aplicador in EnElOrdenDeLosReceptores(aplicadores))
        {
            await aplicador.Aplicar(usuarioId, usuarioId, cancelacion).ConfigureAwait(false);
        }

        await purga.EliminarLosDatosDe(usuarioId, cancelacion).ConfigureAwait(false);

        // (3) Avisos pendientes y sesiones.
        await cancelacionDeAvisos.CancelarLosAvisosDe(usuarioId, cancelacion).ConfigureAwait(false);
        await revocacion.Todas(cuenta, cancelacion).ConfigureAwait(false);

        // (4) La cuenta queda anonimizada; lo que se conserva es el identificador opaco y lo que exige una obligación legal en los demás módulos (IDE-89).
        cuenta.Usuario.Anonimizar($"cancelada-{secretos.NuevoIdentificador():N}@anonimo.invalid");
        cuenta.Nombre = NombreAnonimo;
        cuenta.ResumenDeAcceso = null;
        cuenta.ContrasenaTemporalHasta = null;
        cuenta.DebeCambiarLaTemporal = false;
        cuenta.Whatsapp.Eliminar();
        cuenta.Aceptacion = AceptacionDeTextos.Vacia();
        if (conBaja)
        {
            cuenta.CanceladaEn ??= ahora;
        }

        await cuentas.Guardar(cuenta, cancelacion).ConfigureAwait(false);

        // (5) La baja del titular y el cierre pendiente, en la misma transacción.
        if (conBaja)
        {
            await bajas.Agregar(new BajaDeTitular(usuarioId, ahora), cancelacion).ConfigureAwait(false);
        }

        // Para la depuración de los registros aislados y la restauración (AUD-19, AUD-31): el identificador opaco de la cuenta cancelada queda por anonimizar en sus registros.
        await porAnonimizar.Agregar(usuarioId, cancelacion).ConfigureAwait(false);
    }
}

/// <summary>
/// El cierre de las cancelaciones y la entrega de las bajas (T24; IDE-64, IDE-75, AUD-32, PLA-41; AC-IDE-53, AC-PLA-37): <b>después</b> de que la cancelación se confirmó, y repetible, agrega el cierre al registro de
/// supresiones y entrega al Inventario la baja con solo el identificador opaco. Cada paso es idempotente y un fallo de uno no deshace nada ni detiene el otro ni a las demás bajas: se reintenta en la siguiente pasada. Si una
/// baja lleva más de 24 horas sin entregarse se publica la alerta técnica. Corre con el actor del sistema.
/// </summary>
public sealed class ReintentoDeBajas(
    IRepositorioDeBajas bajas,
    IRegistroDeSupresiones registro,
    IEntregaDeBajas entrega,
    IAlertas alertas,
    IReloj reloj)
{
    public const int LimitePorPasada = 100;

    public static readonly TimeSpan PlazoParaLaAlerta = TimeSpan.FromHours(24);

    /// <summary>El motivo del catálogo cerrado de alertas técnicas: una baja lleva más de 24 horas sin entregarse o sin cerrarse en el registro de supresiones (AUD-32, PLA-41).</summary>
    public const string MotivoDeBajaSinEntregar = "baja-de-titular-sin-entregar";

    public async Task<ResultadoDelReintentoDeBajas> Procesar(CancellationToken cancelacion = default)
    {
        var pendientes = await bajas.Pendientes(LimitePorPasada, cancelacion).ConfigureAwait(false);
        var entregadas = 0;
        var cerradas = 0;
        var sinEntregarMuchoTiempo = 0;
        foreach (var baja in pendientes)
        {
            var ahora = reloj.Ahora;
            if (baja.CerradaEn is null && await Cerrar(baja, ahora, cancelacion).ConfigureAwait(false))
            {
                cerradas++;
            }

            if (baja.EntregadaEn is null && await Entregar(baja, ahora, cancelacion).ConfigureAwait(false))
            {
                entregadas++;
            }

            // Sin entregar al Inventario o sin cerrar en el registro de supresiones pasadas 24 horas: la misma alerta técnica (AUD-32, PLA-41).
            if (baja.Pendiente && ahora - baja.Creada >= PlazoParaLaAlerta)
            {
                sinEntregarMuchoTiempo++;
            }
        }

        if (sinEntregarMuchoTiempo > 0)
        {
            await AlertarSinInterrumpir(cancelacion).ConfigureAwait(false);
        }

        return new ResultadoDelReintentoDeBajas(pendientes.Count, entregadas, cerradas, sinEntregarMuchoTiempo);
    }

    private async Task<bool> Cerrar(BajaDeTitular baja, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        try
        {
            // Solo se cierra una supresión con intención previa (PLA-37): la ejecución la dejó antes de confirmar; si no está, no se escribe el cierre.
            var id = baja.IdentificadorOpaco.ToString("D");
            if (await registro.Obtener(id, ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Intencion, cancelacion).ConfigureAwait(false) is null)
            {
                return false;
            }

            await registro.Agregar(id, ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Cierre, ahora, cancelacion).ConfigureAwait(false);
            baja.CerradaEn = ahora;
            await bajas.Guardar(baja, cancelacion).ConfigureAwait(false);
            return true;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // El registro no responde: el cierre queda pendiente y se reintenta sin deshacer la cancelación (PLA-41).
            return false;
        }
    }

    private async Task<bool> Entregar(BajaDeTitular baja, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        try
        {
            if (!await entrega.Entregar(baja.IdentificadorOpaco, cancelacion).ConfigureAwait(false))
            {
                return false;
            }

            baja.EntregadaEn = ahora;
            await bajas.Guardar(baja, cancelacion).ConfigureAwait(false);
            return true;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // El Inventario no está disponible: la baja se reintenta (es idempotente en el Inventario, IDE-64).
            return false;
        }
    }

    private async Task AlertarSinInterrumpir(CancellationToken cancelacion)
    {
        try
        {
            await alertas.PublicarTecnica(new CodigoDeAlerta(MotivoDeBajaSinEntregar), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Una alerta que no se puede publicar no cambia nada (AUD-40).
        }
    }
}

/// <summary>Lo que hizo una pasada de reintentos: cuántas bajas había pendientes, cuántas se entregaron, cuántos cierres se escribieron y cuántas llevan más de 24 horas sin entregarse.</summary>
public sealed record ResultadoDelReintentoDeBajas(int Pendientes, int Entregadas, int Cerradas, int SinEntregarPasadoElPlazo);
