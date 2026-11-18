using System.Globalization;
using System.Text.Json;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Ajustes;

namespace BabelHub.Inventario.Application.Ajustes.Interno;

internal sealed class ValidadorDeRegistrarAjusteManual : IValidador<RegistrarAjusteManualEntrada>
{
    public ResultadoDeValidacion Validar(RegistrarAjusteManualEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDeAjustes.Codigo(errores, "codigoDeFuente", entrada.CodigoDeFuente);
        if (entrada.EdicionId == Guid.Empty)
        {
            errores.Add(new ErrorDeCampo("edicionId", ConstructorDeValidacion.Obligatorio));
        }

        // Solo tres motivos entran por el ajuste manual: el ingreso y la devolución tienen su operación (AJU-02, AJU-14, AJU-19).
        var motivo = MotivosDeAjuste.DeIdentificador(entrada.Motivo);
        if (motivo is not { } m || !m.EsManual())
        {
            errores.Add(new ErrorDeCampo("motivo", string.IsNullOrEmpty(entrada.Motivo) ? ConstructorDeValidacion.Obligatorio : ConstructorDeValidacion.FormatoInvalido));
        }
        else if (m == MotivoDeAjuste.CorreccionDeConteo)
        {
            if (entrada.ConteoReal is null)
            {
                errores.Add(new ErrorDeCampo("conteoReal", ConstructorDeValidacion.Obligatorio));
            }

            if (entrada.Cantidad is not null)
            {
                errores.Add(new ErrorDeCampo("cantidad", ConstructorDeValidacion.FormatoInvalido));
            }
        }
        else
        {
            if (entrada.Cantidad is null)
            {
                errores.Add(new ErrorDeCampo("cantidad", ConstructorDeValidacion.Obligatorio));
            }

            if (entrada.ConteoReal is not null)
            {
                errores.Add(new ErrorDeCampo("conteoReal", ConstructorDeValidacion.FormatoInvalido));
            }
        }

        ReglasDeEntradaDeAjustes.Nota(errores, entrada.Nota);
        ReglasDeEntradaDeAjustes.Clave(errores, entrada.ClaveDeIdempotencia);
        return ReglasDeEntradaDeAjustes.Resultado(errores);
    }
}

/// <summary>Convierte un ajuste guardado en la respuesta del contrato (la referencia solo la tiene el ajuste de un ingreso: la pone quien la conoce).</summary>
internal static class ConversionDeAjustes
{
    public static AjusteRegistrado ARespuesta(Ajuste ajuste, string codigoDeFuente, string? referencia = null) =>
        new(ajuste.Id, codigoDeFuente, ajuste.EdicionId, ajuste.Motivo.Identificador(), ajuste.UsuarioId, ajuste.Fecha, ajuste.CantidadAnterior, ajuste.CantidadPosterior, ajuste.Nota, referencia);

    /// <summary>La entrada de auditoría de un ajuste: lista blanca (motivo, edición, cantidades y el identificador opaco del ajuste); la nota y la referencia nunca entran (D04). Con la sucursal en <c>SucursalOrigen</c> (AJU-15).</summary>
    public static SolicitudDeRegistro Auditar(AccionAuditable accion, string objetoTipo, Guid objetoId, Guid fuenteId, Ajuste ajuste, IReadOnlyDictionary<string, string?> extras)
    {
        var detalle = new Dictionary<string, string?>(extras)
        {
            ["ajusteId"] = ajuste.Id.ToString("D"),
            ["edicionId"] = ajuste.EdicionId.ToString("D"),
            ["cantidadAnterior"] = ajuste.CantidadAnterior.ToString(CultureInfo.InvariantCulture),
            ["cantidadPosterior"] = ajuste.CantidadPosterior.ToString(CultureInfo.InvariantCulture),
        };

        return new SolicitudDeRegistro(accion) { ObjetoTipo = objetoTipo, ObjetoId = objetoId.ToString("D"), SucursalOrigen = fuenteId, Detalle = detalle };
    }
}

/// <summary>
/// Registra un ajuste manual: merma, daño o corrección de conteo (AJU-01 a AJU-10, AJU-12, AJU-15, AJU-18, AJU-22). Orden fijo (plan §5): (1) el rol (administración o encargado; logística y cliente, rechazados con acceso denegado),
/// (2) la forma de la entrada, (3) el presupuesto del personal (transacción independiente: cuenta aunque se rechace), (4) la fuente y el <b>alcance</b> sobre ella (AJU-09: el encargado solo la suya), (5) la clave de
/// idempotencia, <b>después</b> del alcance para no devolver el resultado de otra sucursal (misma carga: el original sin reaplicar; otra: conflicto), (6) la ficha de la edición (impresa y existente, AJU-18), (7) <c>Bloquear</c>
/// la fila, (8) las reglas del Dominio con el stock ya bloqueado y actualizado (AJU-08, D01), (9) <c>Aplicar</c> con anterior y posterior, (10) la fila de <c>ajustes</c> y (11) la auditoría en la misma transacción: si no se
/// puede auditar, la transacción se revierte y el stock no cambia (AJU-10). Un rechazo de negocio deshace también la clave guardada: no consume la clave.
/// </summary>
internal sealed class RegistrarAjusteManual(
    AutorizadorDeAjustes autorizador,
    IValidador<RegistrarAjusteManualEntrada> validador,
    PresupuestoDelPersonal presupuesto,
    IDirectorioDeFuentes fuentes,
    IFichasDeEdiciones fichas,
    IMovimientosDeStock stock,
    IReservasDeStock reservas,
    IRepositorioDeAjustes ajustes,
    IRepositorioDeOperacionesIdempotentes operaciones,
    IAuditoria auditoria,
    IReloj reloj) : ICasoDeUso<RegistrarAjusteManualEntrada, AjusteRegistrado>
{
    private const string TipoDeOperacion = "ajuste";

    public async Task<Resultado<AjusteRegistrado>> Ejecutar(RegistrarAjusteManualEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirAdministracionOEncargado(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeAjustes.Fallo<AjusteRegistrado>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeAjustes.Validacion<AjusteRegistrado>(validacion);
        }

        var usuarioId = autorizador.UsuarioId!.Value;
        if (await presupuesto.Consumir(usuarioId, cancelacion).ConfigureAwait(false) is { } sinPresupuesto)
        {
            return FallosDeAjustes.Fallo<AjusteRegistrado>(sinPresupuesto);
        }

        try
        {
            return await Registrar(entrada, usuarioId, cancelacion).ConfigureAwait(false);
        }
        catch (EsperaDeBloqueoAgotadaException)
        {
            return FallosDeAjustes.BloqueoOcupado<AjusteRegistrado>();
        }
    }

    private async Task<Resultado<AjusteRegistrado>> Registrar(RegistrarAjusteManualEntrada entrada, Guid usuarioId, CancellationToken cancelacion)
    {
        var fuente = await fuentes.PorCodigo(entrada.CodigoDeFuente, cancelacion).ConfigureAwait(false);
        if (await autorizador.ExigirFuente(fuente?.Id, cancelacion).ConfigureAwait(false) is { } fueraDeAlcance)
        {
            return FallosDeAjustes.Fallo<AjusteRegistrado>(fueraDeAlcance);
        }

        if (fuente is null)
        {
            return FallosDeAjustes.De<AjusteRegistrado>(CategoriaDeError.NoEncontrado, ClavesDeAjustes.FuenteNoEncontrada);
        }

        var motivo = MotivosDeAjuste.DeIdentificador(entrada.Motivo)!.Value;
        var ajusteId = Guid.NewGuid();
        var huella = ReglasDeEntradaDeAjustes.Huella(
            $"ajuste|{fuente.Id:N}|{entrada.EdicionId:N}|{(int)motivo}|{entrada.Cantidad?.ToString(CultureInfo.InvariantCulture)}|{entrada.ConteoReal?.ToString(CultureInfo.InvariantCulture)}|{entrada.Nota}");
        var previa = await operaciones.Registrar(usuarioId, TipoDeOperacion, entrada.ClaveDeIdempotencia, huella, JsonSerializer.Serialize(new { ajusteId }), cancelacion).ConfigureAwait(false);
        if (previa is not null)
        {
            return await Repetir(previa, huella, fuente, cancelacion).ConfigureAwait(false);
        }

        var ficha = await fichas.PorId(entrada.EdicionId, cancelacion).ConfigureAwait(false);
        if (ficha is null)
        {
            return FallosDeAjustes.De<AjusteRegistrado>(CategoriaDeError.NoEncontrado, ClavesDeAjustes.EdicionNoEncontrada);
        }

        if (!ficha.EsImpresa)
        {
            return FallosDeAjustes.De<AjusteRegistrado>(CategoriaDeError.ReglaDeNegocio, ClavesDeAjustes.EdicionNoImpresa);
        }

        // Bloquear garantiza la fila, libera las vencidas y bloquea: la decisión se toma con el stock ya bloqueado y actualizado (D01, AJU-08).
        var bloqueo = await stock.Bloquear([entrada.EdicionId], cancelacion).ConfigureAwait(false);
        if (!bloqueo.EstaBloqueado)
        {
            return FallosDeAjustes.De<AjusteRegistrado>(CategoriaDeError.ReglaDeNegocio, ClavesDeAjustes.EdicionNoImpresa);
        }

        var fila = bloqueo.Fila(fuente.Id, entrada.EdicionId);
        var decision = motivo == MotivoDeAjuste.CorreccionDeConteo
            ? ReglasDeAjuste.Corregir(fila.Fisico, fila.Reservado, entrada.ConteoReal!.Value)
            : ReglasDeAjuste.Restar(fila.Fisico, fila.Reservado, entrada.Cantidad!.Value);
        if (!decision.Aceptado)
        {
            return await Rechazar(decision.Rechazo!.Value, fila.Fisico, fila.Reservado, fuente.Id, entrada.EdicionId, cancelacion).ConfigureAwait(false);
        }

        var resultado = await stock.Aplicar(new MovimientoDeStock(fuente.Id, entrada.EdicionId, decision.DeltaFisico, 0), cancelacion).ConfigureAwait(false);
        if (resultado.Resultado == ResultadoDeMovimiento.DeniegadoPorAlcance)
        {
            return FallosDeAjustes.Fallo<AjusteRegistrado>(await autorizador.DenegadoPorAlcance(cancelacion).ConfigureAwait(false));
        }

        if (!resultado.Aplicado)
        {
            // La decisión se tomó con la fila bloqueada: aplicarla no puede fallar; un estado imposible aborta y la transacción se revierte.
            throw new InvalidOperationException("No se pudo aplicar un ajuste ya decidido sobre una fila bloqueada.");
        }

        var ahora = reloj.Ahora;
        var ajuste = motivo == MotivoDeAjuste.CorreccionDeConteo
            ? Ajuste.Corregido(ajusteId, fuente.Id, entrada.EdicionId, decision, entrada.ConteoReal!.Value, entrada.Nota, usuarioId, ahora)
            : Ajuste.Restado(ajusteId, fuente.Id, entrada.EdicionId, motivo, decision, entrada.Nota, usuarioId, ahora);
        await ajustes.Insertar(ajuste, cancelacion).ConfigureAwait(false);

        await auditoria.Registrar(
            ConversionDeAjustes.Auditar(AccionesAuditables.AjusteRegistrado, "ajuste", ajuste.Id, fuente.Id, ajuste, new Dictionary<string, string?> { ["motivo"] = motivo.Identificador() }),
            cancelacion).ConfigureAwait(false);

        return Resultado<AjusteRegistrado>.Exito(ConversionDeAjustes.ARespuesta(ajuste, fuente.Codigo));
    }

    /// <summary>La misma clave: con la misma carga, el resultado original sin reaplicar; con otra, conflicto (AJU-22). El alcance ya se comprobó antes: quien repite sin alcance no llega aquí.</summary>
    private async Task<Resultado<AjusteRegistrado>> Repetir(OperacionIdempotenteGuardada previa, string huella, FuenteDeStock fuente, CancellationToken cancelacion)
    {
        if (previa.HuellaDeLaCarga != huella)
        {
            return FallosDeAjustes.De<AjusteRegistrado>(CategoriaDeError.Conflicto, ClavesDeAjustes.IdempotenciaEnConflicto);
        }

        using var documento = JsonDocument.Parse(previa.Resultado);
        var original = await ajustes.PorId(documento.RootElement.GetProperty("ajusteId").GetGuid(), cancelacion).ConfigureAwait(false);
        return original is null || original.FuenteId != fuente.Id
            ? FallosDeAjustes.De<AjusteRegistrado>(CategoriaDeError.Conflicto, ClavesDeAjustes.IdempotenciaEnConflicto)
            : Resultado<AjusteRegistrado>.Exito(ConversionDeAjustes.ARespuesta(original, fuente.Codigo));
    }

    private async Task<Resultado<AjusteRegistrado>> Rechazar(MotivoDeRechazoDeAjuste motivo, int fisico, int reservado, Guid fuenteId, Guid edicionId, CancellationToken cancelacion)
    {
        switch (motivo)
        {
            case MotivoDeRechazoDeAjuste.CantidadSuperaAlVendible:
                return Resultado<AjusteRegistrado>.Fallo(new ErrorDeAplicacion(
                    CategoriaDeError.Conflicto, ClavesDeAjustes.StockInsuficiente, DetalleDeStockInsuficienteDeAjuste.Codificar(fisico, reservado, Math.Max(0, fisico - reservado))));

            case MotivoDeRechazoDeAjuste.ConteoMenorQueLoReservado:
                var pedidos = await reservas.PedidosQueApartan(fuenteId, edicionId, LimitesDeEntradaDeAjustes.MaximoDePedidosEnElRechazo, cancelacion).ConfigureAwait(false);
                return Resultado<AjusteRegistrado>.Fallo(new ErrorDeAplicacion(
                    CategoriaDeError.ReglaDeNegocio, ClavesDeAjustes.ConteoMenorQueReservas, DetalleDeConteoMenorQueReservas.Codificar(pedidos.PedidoIds, pedidos.TotalDePedidos)));

            case MotivoDeRechazoDeAjuste.SinCambios:
                return FallosDeAjustes.De<AjusteRegistrado>(CategoriaDeError.ReglaDeNegocio, ClavesDeAjustes.SinCambios);

            case MotivoDeRechazoDeAjuste.ConteoFueraDeRango:
                return FallosDeAjustes.De<AjusteRegistrado>(CategoriaDeError.ReglaDeNegocio, ClavesDeAjustes.ConteoFueraDeRango);

            default:
                return FallosDeAjustes.De<AjusteRegistrado>(CategoriaDeError.ReglaDeNegocio, ClavesDeAjustes.CantidadNoPositiva);
        }
    }
}
