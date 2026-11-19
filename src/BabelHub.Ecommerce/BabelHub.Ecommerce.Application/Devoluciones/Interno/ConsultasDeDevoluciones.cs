using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;

namespace BabelHub.Ecommerce.Application.Devoluciones.Interno;

/// <summary>
/// Las consultas de Devoluciones (T13; DEV-13, DEV-21, DEV-28; AC-DEV-12, AC-DEV-20, AC-DEV-30): <c>ListarMisDevoluciones</c>, <c>ObtenerDevolucion</c> (del cliente y de la gestión), <c>ListarDevolucionesDeSucursal</c> (solo impresos de su sucursal receptora) y <c>ListarDevolucionesDeLaAdministracion</c> (con los reportes).
/// Todas con el orden fijo rol → textos legales → límite de consultas → alcance, paginadas con tope de 50, <b>sin el responsable de ningún cambio</b> (la vista no lo trae) y sin la cuenta. Una devolución ajena o inexistente responde lo mismo (<c>ECO-NO-ENCONTRADO</c>) y registra un acceso denegado por cada intento.
/// Cada línea lleva si su reembolso está pendiente (DEV-21). Son lecturas: no abren transacción ni bloquean.
/// </summary>
internal sealed class ConsultasDeDevoluciones(AutorizadorDeDevoluciones autorizador, LimitesDeDevoluciones limites, IPedidosParaDevoluciones pedidos, LocalizadorDeDevolucionesDeUnaCuenta localizador, IRepositorioDeDevoluciones repositorio)
{
    public const int TamanoMaximo = 50;

    /// <summary>Un tope de página evita el desborde de <c>(página - 1) × tamaño</c> en el repositorio.</summary>
    public const int PaginaMaxima = 100_000;

    public async Task<Resultado<PaginaDeDevoluciones>> ListarMias(ListarDevolucionesEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirCliente(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeDevoluciones.Fallo<PaginaDeDevoluciones>(rechazo);
        }

        if (await limites.Consulta(cancelacion).ConfigureAwait(false) is { } excedido)
        {
            return FallosDeDevoluciones.Fallo<PaginaDeDevoluciones>(excedido);
        }

        try
        {
            var (pagina, tamano) = Paginar(entrada.Pagina, entrada.Tamano);
            var pedidoIds = await localizador.PedidosDeLaCuenta(autorizador.Actor!.UsuarioId!.Value, cancelacion).ConfigureAwait(false);

            var devoluciones = pedidoIds.Count == 0
                ? new PaginaDeDevolucionesDelDominio([], 0)
                : await repositorio.ListarDePedidos(pedidoIds, pagina, tamano, cancelacion).ConfigureAwait(false);
            return Resultado<PaginaDeDevoluciones>.Exito(Pagina(devoluciones, pagina, tamano));
        }
        catch (Exception ex) when (ex is DevolucionesNoDisponiblesException or PendientesNoDisponiblesException)
        {
            return FallosDeDevoluciones.NoDisponible<PaginaDeDevoluciones>();
        }
    }

    public async Task<Resultado<DevolucionVista>> ObtenerMia(Guid devolucionId, CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirCliente(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(rechazo);
        }

        if (await limites.Consulta(cancelacion).ConfigureAwait(false) is { } excedido)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(excedido);
        }

        try
        {
            var devolucion = await repositorio.Obtener(devolucionId, paraActualizar: false, cancelacion).ConfigureAwait(false);
            if (devolucion is null)
            {
                return FallosDeDevoluciones.Fallo<DevolucionVista>(await autorizador.NoEncontrado(MotivosDeDenegacionDeDevoluciones.RecursoAjeno, cancelacion).ConfigureAwait(false));
            }

            // La cuenta sale siempre del contrato de lectura de Pedidos (D01): la devolución no la guarda.
            var lectura = await pedidos.Obtener(devolucion.PedidoId, cancelacion).ConfigureAwait(false);
            if (!lectura.EsExito)
            {
                return lectura.Error!.Categoria == CategoriaDeError.NoEncontrado
                    ? FallosDeDevoluciones.Fallo<DevolucionVista>(await autorizador.NoEncontrado(MotivosDeDenegacionDeDevoluciones.RecursoAjeno, cancelacion).ConfigureAwait(false))
                    : FallosDeDevoluciones.NoDisponible<DevolucionVista>();
            }

            if (await autorizador.ExigirQueElPedidoEsDelCliente(lectura.Valor.CuentaId, cancelacion).ConfigureAwait(false) is { } ajena)
            {
                return FallosDeDevoluciones.Fallo<DevolucionVista>(ajena);
            }

            return Resultado<DevolucionVista>.Exito(MapeoDeDevoluciones.Vista(devolucion));
        }
        catch (DevolucionesNoDisponiblesException)
        {
            return FallosDeDevoluciones.NoDisponible<DevolucionVista>();
        }
    }

    /// <summary>Una devolución para la gestión: el encargado solo ve las de impresos de <b>su</b> sucursal receptora (nunca un reporte ni un digital) y la administración central ve todas.</summary>
    public async Task<Resultado<DevolucionVista>> Obtener(Guid devolucionId, CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirPersonal(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(rechazo);
        }

        if (await limites.Consulta(cancelacion).ConfigureAwait(false) is { } excedido)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(excedido);
        }

        try
        {
            var devolucion = await repositorio.Obtener(devolucionId, paraActualizar: false, cancelacion).ConfigureAwait(false);
            if (devolucion is null)
            {
                return FallosDeDevoluciones.Fallo<DevolucionVista>(await autorizador.NoEncontrado(MotivosDeDenegacionDeDevoluciones.RecursoAjeno, cancelacion).ConfigureAwait(false));
            }

            if (await autorizador.ExigirAlcanceDeGestion(devolucion, cancelacion).ConfigureAwait(false) is { } fueraDeAlcance)
            {
                return FallosDeDevoluciones.Fallo<DevolucionVista>(fueraDeAlcance);
            }

            return Resultado<DevolucionVista>.Exito(MapeoDeDevoluciones.Vista(devolucion));
        }
        catch (DevolucionesNoDisponiblesException)
        {
            return FallosDeDevoluciones.NoDisponible<DevolucionVista>();
        }
    }

    /// <summary>Las devoluciones de <b>impresos</b> de una sucursal receptora: el encargado de esa sucursal o la administración central. El código de la ruta que no es el del encargado es <c>ECO-PROHIBIDO</c> con un acceso denegado.</summary>
    public async Task<Resultado<PaginaDeDevoluciones>> ListarDeLaSucursal(string codigoDeSucursal, ListarDevolucionesEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirPersonal(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeDevoluciones.Fallo<PaginaDeDevoluciones>(rechazo);
        }

        if (string.IsNullOrWhiteSpace(codigoDeSucursal) || codigoDeSucursal.Length > Devolucion.LongitudMaximaDeLaSucursal)
        {
            return FallosDeDevoluciones.Fallo<PaginaDeDevoluciones>(MapeoDeDevoluciones.ValidacionDeCampo("codigoDeSucursal", ClavesDeDevoluciones.ValidacionFallida));
        }

        if (await limites.Consulta(cancelacion).ConfigureAwait(false) is { } excedido)
        {
            return FallosDeDevoluciones.Fallo<PaginaDeDevoluciones>(excedido);
        }

        if (await autorizador.ExigirSucursalDeLaRuta(codigoDeSucursal, cancelacion).ConfigureAwait(false) is { } fueraDeLaRuta)
        {
            return FallosDeDevoluciones.Fallo<PaginaDeDevoluciones>(fueraDeLaRuta);
        }

        try
        {
            var (pagina, tamano) = Paginar(entrada.Pagina, entrada.Tamano);
            var devoluciones = await repositorio.ListarDeLaSucursal(codigoDeSucursal, pagina, tamano, cancelacion).ConfigureAwait(false);
            return Resultado<PaginaDeDevoluciones>.Exito(Pagina(devoluciones, pagina, tamano));
        }
        catch (DevolucionesNoDisponiblesException)
        {
            return FallosDeDevoluciones.NoDisponible<PaginaDeDevoluciones>();
        }
    }

    /// <summary>Todas las devoluciones, con los reportes y el reembolso pendiente de cada línea: solo la administración central (DEV-28).</summary>
    public async Task<Resultado<PaginaDeDevoluciones>> ListarDeLaAdministracion(ListarDevolucionesDeLaAdministracionEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracion(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeDevoluciones.Fallo<PaginaDeDevoluciones>(rechazo);
        }

        if (entrada.Tipo is { } tipo && !Enum.IsDefined(tipo))
        {
            return FallosDeDevoluciones.Fallo<PaginaDeDevoluciones>(MapeoDeDevoluciones.ValidacionDeCampo("tipo", ClavesDeDevoluciones.ValidacionFallida));
        }

        if (await limites.Consulta(cancelacion).ConfigureAwait(false) is { } excedido)
        {
            return FallosDeDevoluciones.Fallo<PaginaDeDevoluciones>(excedido);
        }

        try
        {
            var (pagina, tamano) = Paginar(entrada.Pagina, entrada.Tamano);
            var filtro = new FiltroDeLaAdministracion(entrada.Tipo is { } t ? (t == TipoDeLaDevolucion.Impreso ? TipoDeDevolucion.Impreso : TipoDeDevolucion.Digital) : null, entrada.SoloReportes);
            var devoluciones = await repositorio.ListarDeLaAdministracion(filtro, pagina, tamano, cancelacion).ConfigureAwait(false);
            return Resultado<PaginaDeDevoluciones>.Exito(Pagina(devoluciones, pagina, tamano));
        }
        catch (DevolucionesNoDisponiblesException)
        {
            return FallosDeDevoluciones.NoDisponible<PaginaDeDevoluciones>();
        }
    }

    private static (int Pagina, int Tamano) Paginar(int pagina, int tamano) => (Math.Clamp(pagina, 1, PaginaMaxima), Math.Clamp(tamano, 1, TamanoMaximo));

    private static PaginaDeDevoluciones Pagina(PaginaDeDevolucionesDelDominio devoluciones, int pagina, int tamano) =>
        new([.. devoluciones.Devoluciones.Select(MapeoDeDevoluciones.Vista)], pagina, tamano, devoluciones.Total);
}
