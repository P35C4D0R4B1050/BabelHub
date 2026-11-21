using System.Globalization;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;

namespace BabelHub.Ecommerce.Application.Devoluciones.Interno;

/// <summary>
/// Localiza lo de una cuenta <b>por el pedido</b> (D01: Devoluciones no guarda la cuenta): los pedidos de la cuenta salen del contrato de lectura de Pedidos, de a <see cref="IPedidosParaDevoluciones.TamanoMaximo"/>, y con ellos las devoluciones. Falla cerrado: si Pedidos o el almacén
/// no responden, o la cuenta tiene más pedidos de los que se pueden recorrer, <b>lanza</b> (nunca devuelve una lista incompleta que se tome por la verdad, IDE-27, IDE-70).
/// </summary>
internal sealed class LocalizadorDeDevolucionesDeUnaCuenta(IPedidosParaDevoluciones pedidos, IRepositorioDeDevoluciones repositorio)
{
    /// <summary>Las páginas de pedidos que se recorren a lo sumo (cada una de <see cref="IPedidosParaDevoluciones.TamanoMaximo"/>): 5000 pedidos por cuenta.</summary>
    public const int PaginasDePedidosMaximas = 50;

    public async Task<IReadOnlyList<Guid>> PedidosDeLaCuenta(Guid cuentaId, CancellationToken cancelacion)
    {
        var ids = new List<Guid>();
        for (var pagina = 1; pagina <= PaginasDePedidosMaximas; pagina++)
        {
            Resultado<PaginaDePedidosDeUnaCuenta> lectura;
            try
            {
                lectura = await pedidos.ListarDeUnaCuenta(cuentaId, pagina, IPedidosParaDevoluciones.TamanoMaximo, cancelacion).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new PendientesNoDisponiblesException("Pedidos no respondió al localizar las devoluciones de la cuenta.", ex);
            }

            if (!lectura.EsExito)
            {
                throw new PendientesNoDisponiblesException("Pedidos no respondió al localizar las devoluciones de la cuenta.");
            }

            ids.AddRange(lectura.Valor.PedidoIds);
            if (lectura.Valor.PedidoIds.Count == 0 || ids.Count >= lectura.Valor.Total)
            {
                return ids;
            }
        }

        throw new PendientesNoDisponiblesException("La cuenta tiene más pedidos de los que se pueden recorrer: no se asume que no tiene devoluciones.");
    }

    /// <summary>Todas las devoluciones de los pedidos de la cuenta (abiertas o cerradas, redactadas o no), sin bloqueo.</summary>
    public async Task<IReadOnlyList<Devolucion>> DevolucionesDeLaCuenta(Guid cuentaId, CancellationToken cancelacion)
    {
        var pedidoIds = await PedidosDeLaCuenta(cuentaId, cancelacion).ConfigureAwait(false);
        var devoluciones = new List<Devolucion>();
        foreach (var grupo in pedidoIds.Chunk(IPedidosParaDevoluciones.TamanoMaximo))
        {
            var pagina = 1;
            var leidas = 0;
            while (true)
            {
                PaginaDeDevolucionesDelDominio lectura;
                try
                {
                    lectura = await repositorio.ListarDePedidos(grupo, pagina, 100, cancelacion).ConfigureAwait(false);
                }
                catch (DevolucionesNoDisponiblesException ex)
                {
                    throw new PendientesNoDisponiblesException("El almacén de Devoluciones no respondió.", ex);
                }

                devoluciones.AddRange(lectura.Devoluciones);
                leidas += lectura.Devoluciones.Count;
                if (lectura.Devoluciones.Count == 0 || leidas >= lectura.Total)
                {
                    break;
                }

                pagina++;
            }
        }

        return devoluciones;
    }
}

/// <summary>
/// <c>ContarPendientesDeCuenta</c> de Devoluciones (T15; DEV-23, IDE-27; AC-DEV-25): lo que la cuenta tiene pendiente en el módulo, en <b>tres números que no cuentan un caso en dos</b> y solo de los pedidos de esa cuenta: (1) las devoluciones <b>abiertas de impresos</b> (con alguna línea solicitada o aprobada), (2) los
/// <b>reportes sin decidir</b> (con una línea solicitada) y (3) los <b>reembolsos pendientes</b>: cada línea recibida de un impreso o aprobada de un digital hasta que la pasarela confirma. Corre dentro de la transacción de Identidad; no devuelve motivo, texto ni importes. Si no puede contar, lanza: nunca se asume cero.
/// </summary>
internal sealed class ContarPendientesDeCuenta(LocalizadorDeDevolucionesDeUnaCuenta localizador) : IPendientesDeDevolucionesDeCuenta
{
    /// <summary>La suma de los tres números (lo que usa Identidad para la cancelación y el reemplazo).</summary>
    public async Task<int> Contar(Guid usuarioId, CancellationToken cancelacion = default)
        => (await ContarEnTres(usuarioId, cancelacion).ConfigureAwait(false)).Total;

    /// <summary>Los tres números de DEV-23 por separado.</summary>
    public async Task<PendientesDeDevolucionesDeCuenta> ContarEnTres(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La cuenta necesita su identificador.", nameof(usuarioId));
        }

        var devoluciones = await localizador.DevolucionesDeLaCuenta(usuarioId, cancelacion).ConfigureAwait(false);
        return Pendientes(devoluciones);
    }

    /// <summary>Las tres clases son disjuntas: un impreso abierto tiene líneas solicitada o aprobada (nunca recibida a la vez), un reporte sin decidir está solicitado y un reembolso pendiente es una línea recibida o aprobada de un digital.</summary>
    public static PendientesDeDevolucionesDeCuenta Pendientes(IEnumerable<Devolucion> devoluciones)
    {
        int abiertas = 0, reportes = 0, reembolsos = 0;
        foreach (var devolucion in devoluciones)
        {
            if (devolucion.Tipo == TipoDeDevolucion.Impreso)
            {
                abiertas += devolucion.Lineas.Any(l => l.Estado is EstadoDeLineaDeDevolucion.Solicitada or EstadoDeLineaDeDevolucion.Aprobada) ? 1 : 0;
                reembolsos += devolucion.Lineas.Count(l => l.Estado == EstadoDeLineaDeDevolucion.Recibida);
                continue;
            }

            reportes += devolucion.Origen == OrigenDeDevolucion.Reporte && devolucion.Lineas.Any(l => l.Estado == EstadoDeLineaDeDevolucion.Solicitada) ? 1 : 0;
            reembolsos += devolucion.Lineas.Count(l => l.Estado == EstadoDeLineaDeDevolucion.Aprobada);
        }

        return new PendientesDeDevolucionesDeCuenta(abiertas, reportes, reembolsos);
    }
}

/// <summary>
/// <c>RedactarDevolucionesDeUnaCuenta</c> (T16; DEV-24, DEV-25; AC-DEV-26, AC-DEV-27): la parte de Devoluciones de la cancelación de la cuenta (<see cref="IAplicadorDeCancelacion"/>). Redacta el motivo y el texto de <b>todas</b> las devoluciones de los pedidos de la cuenta (abiertas o cerradas), quita el identificador del cliente
/// de cada cambio y la referencia al acceso de los reportes, y conserva pedido, líneas, cantidades, importes, estados y fechas. <b>Todo o nada</b>: corre dentro de la transacción única de Identidad (bajo el rol del esquema, que abre la Infraestructura), bloquea cada devolución después de leerlas, <b>en orden de identificador</b> (cuenta → pedido → devolución, y entre devoluciones por identificador, igual que la depuración, para que no se interbloqueen), no captura
/// ninguna excepción —si algo falla, <b>lanza</b> para que Identidad no ejecute la cancelación— y es <b>idempotente y reaplicable</b> (una devolución ya redactada no cambia y no falla; tras restaurar un respaldo, la que reapareció con su texto se vuelve a redactar). Sin devoluciones no hace nada ni falla. Audita el conteo, sin datos personales.
/// </summary>
internal sealed class RedactarDevolucionesDeUnaCuenta(LocalizadorDeDevolucionesDeUnaCuenta localizador, IRepositorioDeDevoluciones repositorio, IAuditoria auditoria, IReloj reloj) : IAplicadorDeCancelacion
{
    public string Modulo => "devoluciones";

    public async Task Aplicar(Guid usuarioId, Guid identificadorOpaco, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La cuenta necesita su identificador.", nameof(usuarioId));
        }

        // Los pedidos y las devoluciones de la cuenta se leen sin bloqueo; después cada una se vuelve a leer bloqueada para redactarla.
        var ahora = reloj.Ahora;
        var redactadas = 0;
        foreach (var leida in (await localizador.DevolucionesDeLaCuenta(usuarioId, cancelacion).ConfigureAwait(false)).OrderBy(d => d.Id))
        {
            var devolucion = await repositorio.Obtener(leida.Id, paraActualizar: true, cancelacion).ConfigureAwait(false);
            if (devolucion is not null && devolucion.Redactar(ahora, quitarElAcceso: true))
            {
                await repositorio.Guardar(devolucion, cancelacion).ConfigureAwait(false);
                redactadas++;
            }
        }

        if (redactadas > 0)
        {
            await auditoria.Registrar(
                new SolicitudDeRegistro(AccionesAuditables.DevolucionesDeCuentaRedactadas)
                {
                    Detalle = new Dictionary<string, string?> { ["cantidad"] = redactadas.ToString(CultureInfo.InvariantCulture) },
                },
                cancelacion).ConfigureAwait(false);
        }
    }
}
