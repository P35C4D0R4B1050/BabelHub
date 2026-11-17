using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Ajustes;

namespace BabelHub.Inventario.Application.Ajustes.Interno;

internal sealed class ValidadorDeReingresarPorDevolucion : IValidador<ReingresarPorDevolucionEntrada>
{
    public ResultadoDeValidacion Validar(ReingresarPorDevolucionEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        if (entrada.DevolucionId == Guid.Empty)
        {
            errores.Add(new ErrorDeCampo("devolucionId", ConstructorDeValidacion.Obligatorio));
        }

        // El pedido es obligatorio: las unidades vendidas restan lo devuelto por pedido y edición (inventario-seleccion-fuente D13).
        if (entrada.PedidoId == Guid.Empty)
        {
            errores.Add(new ErrorDeCampo("pedidoId", ConstructorDeValidacion.Obligatorio));
        }

        ReglasDeEntradaDeAjustes.Codigo(errores, "codigoDeFuente", entrada.CodigoDeFuente);
        var lineas = entrada.Lineas;
        if (lineas is null || lineas.Count == 0)
        {
            errores.Add(new ErrorDeCampo("lineas", ConstructorDeValidacion.Obligatorio));
        }
        else if (lineas.Count > LimitesDeEntradaDeAjustes.MaximoDeLineasDeReingreso)
        {
            errores.Add(new ErrorDeCampo("lineas", ConstructorDeValidacion.DemasiadoLargo));
        }
        else
        {
            if (lineas.Any(l => l is null || l.EdicionId == Guid.Empty))
            {
                errores.Add(new ErrorDeCampo("lineas", ConstructorDeValidacion.FormatoInvalido));
            }
            else if (lineas.Select(l => l.EdicionId).Distinct().Count() != lineas.Count)
            {
                errores.Add(new ErrorDeCampo("lineas", "duplicadas"));
            }

            if (lineas.Any(l => l is not null && l.Cantidad is < 1 or > LimitesDeEntradaDeAjustes.MaximaCantidadPorLineaDeReingreso))
            {
                errores.Add(new ErrorDeCampo("lineas.cantidad", ConstructorDeValidacion.FueraDeRango));
            }
        }

        return ReglasDeEntradaDeAjustes.Resultado(errores);
    }
}

/// <summary>
/// El reingreso de los impresos de una devolución aceptada a la sucursal receptora (AJU-11, AJU-16, AJU-18). Idempotente por <c>(devolución, edición)</c>: orden (1) rol, (2) forma, (3) la fuente y el <b>alcance</b> (el encargado de la
/// sucursal receptora o la administración; antes de buscar la clave), (4) el bloqueo asesor de la devolución (dos reingresos de la misma devolución se aplican una sola vez), (5) por cada línea, el ajuste ya existente: con la misma
/// fuente, cantidad y pedido es repetición y con otra es <c>INV-IDEMPOTENCIA-EN-CONFLICTO</c>, (6) si todas ya estaban, <c>YaAplicado</c> sin sumar ni auditar; si solo parte, se aplican las nuevas, (7) la ficha de las nuevas
/// (digital o inexistente, rechazada), (8) <c>Bloquear</c> una sola vez con todas las ediciones en orden y (9) <c>Aplicar(+c, 0)</c>, la fila de ajuste con la devolución y el pedido y su auditoría, línea a línea y en orden.
/// No consume el presupuesto del personal (D06). Una violación de la unicidad al insertar (la última defensa) responde «en curso» y revierte todo, incluido <c>Aplicar</c>.
/// </summary>
internal sealed class ReingresarPorDevolucion(
    AutorizadorDeAjustes autorizador,
    IValidador<ReingresarPorDevolucionEntrada> validador,
    IBloqueosAsesores bloqueos,
    IDirectorioDeFuentes fuentes,
    IFichasDeEdiciones fichas,
    IMovimientosDeStock stock,
    IRepositorioDeAjustes ajustes,
    IAuditoria auditoria,
    IReloj reloj) : ICasoDeUso<ReingresarPorDevolucionEntrada, ReingresoRegistrado>
{
    public async Task<Resultado<ReingresoRegistrado>> Ejecutar(ReingresarPorDevolucionEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirAdministracionOEncargado(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeAjustes.Fallo<ReingresoRegistrado>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeAjustes.Validacion<ReingresoRegistrado>(validacion);
        }

        try
        {
            return await Reingresar(entrada, cancelacion).ConfigureAwait(false);
        }
        catch (EsperaDeBloqueoAgotadaException)
        {
            return FallosDeAjustes.BloqueoOcupado<ReingresoRegistrado>();
        }
        catch (AjusteDuplicadoException)
        {
            return FallosDeAjustes.De<ReingresoRegistrado>(CategoriaDeError.Conflicto, ClavesDeAjustes.IdempotenciaEnCurso);
        }
    }

    private async Task<Resultado<ReingresoRegistrado>> Reingresar(ReingresarPorDevolucionEntrada entrada, CancellationToken cancelacion)
    {
        var fuente = await fuentes.PorCodigo(entrada.CodigoDeFuente, cancelacion).ConfigureAwait(false);
        if (await autorizador.ExigirFuente(fuente?.Id, cancelacion).ConfigureAwait(false) is { } fueraDeAlcance)
        {
            return FallosDeAjustes.Fallo<ReingresoRegistrado>(fueraDeAlcance);
        }

        if (fuente is null)
        {
            return FallosDeAjustes.De<ReingresoRegistrado>(CategoriaDeError.NoEncontrado, ClavesDeAjustes.FuenteNoEncontrada);
        }

        await bloqueos.Devolucion(entrada.DevolucionId, cancelacion).ConfigureAwait(false);

        // Las líneas en orden de edición: el bloqueo de filas y las respuestas son deterministas.
        var lineas = entrada.Lineas.OrderBy(l => l.EdicionId, OrdenDeBloqueo.DeIdentificadores).ToList();
        var existentes = new Dictionary<Guid, Ajuste>();
        var nuevas = new List<LineaDeReingreso>();
        foreach (var linea in lineas)
        {
            var existente = await ajustes.PorDevolucionYEdicion(entrada.DevolucionId, linea.EdicionId, cancelacion).ConfigureAwait(false);
            if (existente is null)
            {
                nuevas.Add(linea);
                continue;
            }

            // La misma carga es repetición; cambiar la fuente, la cantidad o el pedido es conflicto (AJU-16).
            if (existente.FuenteId != fuente.Id || existente.Diferencia != linea.Cantidad || existente.PedidoId != entrada.PedidoId)
            {
                return FallosDeAjustes.De<ReingresoRegistrado>(CategoriaDeError.Conflicto, ClavesDeAjustes.IdempotenciaEnConflicto);
            }

            existentes[linea.EdicionId] = existente;
        }

        var aplicados = new Dictionary<Guid, Ajuste>();
        if (nuevas.Count > 0)
        {
            foreach (var linea in nuevas)
            {
                var ficha = await fichas.PorId(linea.EdicionId, cancelacion).ConfigureAwait(false);
                if (ficha is null)
                {
                    return FallosDeAjustes.De<ReingresoRegistrado>(CategoriaDeError.NoEncontrado, ClavesDeAjustes.EdicionNoEncontrada);
                }

                if (!ficha.EsImpresa)
                {
                    return FallosDeAjustes.De<ReingresoRegistrado>(CategoriaDeError.ReglaDeNegocio, ClavesDeAjustes.EdicionNoImpresa);
                }
            }

            var bloqueo = await stock.Bloquear([.. nuevas.Select(l => l.EdicionId)], cancelacion).ConfigureAwait(false);
            if (!bloqueo.EstaBloqueado)
            {
                return FallosDeAjustes.De<ReingresoRegistrado>(CategoriaDeError.ReglaDeNegocio, ClavesDeAjustes.EdicionNoImpresa);
            }

            var usuarioId = autorizador.UsuarioId!.Value;
            foreach (var linea in nuevas)
            {
                var fila = bloqueo.Fila(fuente.Id, linea.EdicionId);
                var decision = ReglasDeAjuste.Sumar(fila.Fisico, linea.Cantidad);
                if (!decision.Aceptado)
                {
                    return FallosDeAjustes.De<ReingresoRegistrado>(CategoriaDeError.ReglaDeNegocio, ClavesDeAjustes.CantidadFueraDeRango);
                }

                var resultado = await stock.Aplicar(new MovimientoDeStock(fuente.Id, linea.EdicionId, decision.DeltaFisico, 0), cancelacion).ConfigureAwait(false);
                if (resultado.Resultado == ResultadoDeMovimiento.DeniegadoPorAlcance)
                {
                    return FallosDeAjustes.Fallo<ReingresoRegistrado>(await autorizador.DenegadoPorAlcance(cancelacion).ConfigureAwait(false));
                }

                if (!resultado.Aplicado)
                {
                    throw new InvalidOperationException("No se pudo aplicar un reingreso ya decidido sobre una fila bloqueada.");
                }

                var ajuste = Ajuste.Devuelto(Guid.NewGuid(), fuente.Id, linea.EdicionId, decision, usuarioId, reloj.Ahora, entrada.DevolucionId, entrada.PedidoId);
                await ajustes.Insertar(ajuste, cancelacion).ConfigureAwait(false);
                await auditoria.Registrar(
                    ConversionDeAjustes.Auditar(AccionesAuditables.ReingresoPorDevolucion, "ajuste", ajuste.Id, fuente.Id, ajuste, new Dictionary<string, string?> { ["devolucionId"] = entrada.DevolucionId.ToString("D") }),
                    cancelacion).ConfigureAwait(false);
                aplicados[linea.EdicionId] = ajuste;
            }
        }

        var respuesta = lineas.Select(l => ConversionDeAjustes.ARespuesta(aplicados.TryGetValue(l.EdicionId, out var nuevo) ? nuevo : existentes[l.EdicionId], fuente.Codigo)).ToList();
        return Resultado<ReingresoRegistrado>.Exito(new ReingresoRegistrado(entrada.DevolucionId, YaAplicado: nuevas.Count == 0, respuesta));
    }
}

internal sealed class ValidadorDeConsultarHistorialDeAjustes : IValidador<ConsultarHistorialDeAjustesEntrada>
{
    public ResultadoDeValidacion Validar(ConsultarHistorialDeAjustesEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDeAjustes.Codigo(errores, "codigoDeFuente", entrada.CodigoDeFuente);
        if (entrada.Pagina < 1)
        {
            errores.Add(new ErrorDeCampo("pagina", ConstructorDeValidacion.FueraDeRango));
        }

        if (entrada.Tamano is < 1 or > LimitesDeEntradaDeAjustes.MaximoDeTamanoDePagina)
        {
            errores.Add(new ErrorDeCampo("tamano", ConstructorDeValidacion.FueraDeRango));
        }

        return ReglasDeEntradaDeAjustes.Resultado(errores);
    }
}

/// <summary>
/// El historial de ajustes de una fuente (AJU-17): edición, motivo, usuario, fecha y cantidades anterior y posterior, del más reciente al más antiguo. El alcance se compara en la Aplicación: el encargado de esa fuente o la
/// administración central; otra fuente, logística y cliente, rechazados con acceso denegado (AJU-09). Sin nombres de usuarios: los compone la web (D07).
/// </summary>
internal sealed class ConsultarHistorialDeAjustes(
    AutorizadorDeAjustes autorizador,
    IValidador<ConsultarHistorialDeAjustesEntrada> validador,
    IDirectorioDeFuentes fuentes,
    IRepositorioDeAjustes ajustes) : ICasoDeUso<ConsultarHistorialDeAjustesEntrada, PaginaDeAjustes>
{
    public async Task<Resultado<PaginaDeAjustes>> Ejecutar(ConsultarHistorialDeAjustesEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirAdministracionOEncargado(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeAjustes.Fallo<PaginaDeAjustes>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeAjustes.Validacion<PaginaDeAjustes>(validacion);
        }

        var fuente = await fuentes.PorCodigo(entrada.CodigoDeFuente, cancelacion).ConfigureAwait(false);
        if (await autorizador.ExigirFuente(fuente?.Id, cancelacion).ConfigureAwait(false) is { } fueraDeAlcance)
        {
            return FallosDeAjustes.Fallo<PaginaDeAjustes>(fueraDeAlcance);
        }

        if (fuente is null)
        {
            return FallosDeAjustes.De<PaginaDeAjustes>(CategoriaDeError.NoEncontrado, ClavesDeAjustes.FuenteNoEncontrada);
        }

        var pagina = await ajustes.Historial(fuente.Id, entrada.Pagina, entrada.Tamano, cancelacion).ConfigureAwait(false);
        return Resultado<PaginaDeAjustes>.Exito(new PaginaDeAjustes([.. pagina.Elementos.Select(a => ConversionDeAjustes.ARespuesta(a, fuente.Codigo))], pagina.Total, entrada.Pagina, entrada.Tamano));
    }
}

/// <summary>
/// <see cref="IReingresosPorDevolucion"/>, la clase de lectura aparte de <c>Ajustes</c> (D09): las unidades reingresadas por pedido y edición, sumadas en todas las sucursales (las ventas de una sucursal restan lo devuelto en cualquiera,
/// FUE-21), sin presupuesto ni actor propio (la autorización es de quien consulta las unidades vendidas, <c>Fuentes</c>). El repositorio lee con el alcance de la red.
/// </summary>
internal sealed class ConsultarReingresosPorDevolucion(IRepositorioDeAjustes ajustes) : IReingresosPorDevolucion
{
    public async Task<IReadOnlyList<UnidadesReingresadas>> PorPedidos(IReadOnlyCollection<Guid> pedidoIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(pedidoIds);
        return pedidoIds.Count == 0 ? [] : await ajustes.ReingresadasPorPedidos(pedidoIds, cancelacion).ConfigureAwait(false);
    }
}
