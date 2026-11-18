using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

/// <summary>
/// <c>ConciliarIngresoPendiente</c> (CAT-22, D04): <b>solo la administración central</b> resuelve a mano un <c>Pendiente</c> (el del autor que no está disponible o el de más de 24 horas) con los últimos 50 ingresos del Inventario
/// (<c>ingresos.listar</c> de la edición, que solo filtra por edición y límite). El criterio de coincidencia es explícito: misma cantidad, mismo usuario (una fila anonimizada no coincide con ninguno), la referencia si la hay y un
/// instante entre <b>5 minutos antes</b> de <c>solicitado_en</c> y <b>60 minutos después</b> de <c>ultimo_intento_en</c> (los relojes del Catálogo y del Inventario difieren, y un ingreso aplicado en un intento anterior no se debe
/// perder), sin contar los ingresos que otra fila ya constató. Una coincidencia <b>única</b> marca <c>Registrado</c> con su <c>ingreso_id</c>; sin coincidencia o con varias solo se marca <c>Fallido</c> con una confirmación
/// explícita (habilita una clave nueva y puede duplicar el stock), y con una coincidencia única se rechaza (consta como aplicado). Si <c>ingresos.listar</c> falla, el <c>Pendiente</c> queda intacto (503). La llamada al
/// Inventario va <b>fuera</b> de la transacción; después una corta (bloqueo de la fila, transición condicional y auditoría) revalida que siga <c>Pendiente</c> y, con 0 filas afectadas, audita una discrepancia y no reescribe la fila.
/// La auditoría no lleva la nota ni la referencia.
/// </summary>
internal sealed class ConciliarIngresoPendiente(
    AutorizadorDelCatalogo autorizador,
    IValidador<ConciliarIngresoEntrada> validador,
    IRepositorioDeIngresos ingresos,
    IInventarioDelCatalogo inventario,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IMemoriaDeDisponibilidad memoria,
    IReloj reloj) : ICasoDeUso<ConciliarIngresoEntrada, ResultadoDeIngreso>
{
    public static readonly TimeSpan MargenAnterior = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan MargenPosterior = TimeSpan.FromMinutes(60);

    public const int IngresosRevisados = 50;

    /// <summary>Un reintento en vuelo (hasta 15 s de presupuesto más un margen) puede aplicarse aún: marcar <c>Fallido</c> habilitaría una clave nueva y duplicaría el lote.</summary>
    public static readonly TimeSpan IntentoReciente = TimeSpan.FromMinutes(2);

    public async Task<Resultado<ResultadoDeIngreso>> Ejecutar(ConciliarIngresoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeIngreso>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDelCatalogo.Validacion<ResultadoDeIngreso>(validacion);
        }

        var fila = await unidad.Ejecutar(token => ingresos.Obtener(entrada.IngresoSolicitadoId, paraActualizar: false, token), cancelacion).ConfigureAwait(false);
        if (fila is null || (entrada.EdicionId is { } edicion && fila.EdicionId != edicion))
        {
            return FallosDelCatalogo.De<ResultadoDeIngreso>(CategoriaDeError.NoEncontrado, ClavesDelCatalogo.IngresoNoEncontrado);
        }

        if (!fila.EstaPendiente)
        {
            return FallosDelCatalogo.De<ResultadoDeIngreso>(CategoriaDeError.Conflicto, ClavesDelCatalogo.IngresoYaResuelto);
        }

if (entrada.Resolucion == ResolucionDeIngreso.Fallido && reloj.Ahora - fila.UltimoIntentoEn < IntentoReciente)        {            return FallosDelCatalogo.De<ResultadoDeIngreso>(CategoriaDeError.ReglaDeNegocio, ClavesDelCatalogo.ReglaDeNegocio, [new ErrorDeCampo("resolucion", "intento-reciente")]);        }
        // Fuera de la transacción: la llamada al Inventario nunca queda dentro de una.
        var remotos = await inventario.ListarIngresos(fila.EdicionId, IngresosRevisados, cancelacion).ConfigureAwait(false);
        if (remotos is null || remotos.Any(i => i.EdicionId != fila.EdicionId))
        {
            return FallosDelCatalogo.De<ResultadoDeIngreso>(CategoriaDeError.NoDisponible, ClavesDelCatalogo.InventarioNoDisponible);
        }

        var constatados = await unidad.Ejecutar(token => ingresos.IngresosYaConstatados(remotos.Select(i => i.IngresoId).ToList(), token), cancelacion).ConfigureAwait(false);
        var coincidencias = remotos.Where(i => !constatados.Contains(i.IngresoId) && Coincide(fila, i)).ToList();

        if (entrada.Resolucion == ResolucionDeIngreso.Registrado && coincidencias.Count != 1)
        {
            return FallosDelCatalogo.De<ResultadoDeIngreso>(CategoriaDeError.ReglaDeNegocio, ClavesDelCatalogo.ReglaDeNegocio, [new ErrorDeCampo("resolucion", "sin-coincidencia-unica")]);
        }

        if (entrada.Resolucion == ResolucionDeIngreso.Fallido)
        {
            if (coincidencias.Count == 1)
            {
                // El ingreso consta como aplicado: marcarlo fallido habilitaría una clave nueva y duplicaría el lote.
                return FallosDelCatalogo.De<ResultadoDeIngreso>(CategoriaDeError.ReglaDeNegocio, ClavesDelCatalogo.ReglaDeNegocio, [new ErrorDeCampo("resolucion", "consta-como-aplicado")]);
            }

            if (!entrada.ConfirmaFallido)
            {
                return FallosDelCatalogo.De<ResultadoDeIngreso>(CategoriaDeError.ReglaDeNegocio, ClavesDelCatalogo.ReglaDeNegocio, [new ErrorDeCampo("confirmaFallido", "obligatorio")]);
            }
        }

        var coincidente = entrada.Resolucion == ResolucionDeIngreso.Registrado ? coincidencias[0] : null;
        var resultado = await unidad.Ejecutar(token => Aplicar(fila.Id, entrada.Resolucion, coincidente, token), cancelacion).ConfigureAwait(false);
        if (resultado.EsExito && entrada.Resolucion == ResolucionDeIngreso.Registrado)
        {
            memoria.Invalidar(fila.EdicionId);
        }

        return resultado;
    }

    /// <summary>La ventana de la coincidencia y los demás criterios (<see cref="MargenAnterior"/>, <see cref="MargenPosterior"/>).</summary>
    internal static bool Coincide(IngresoSolicitado fila, IngresoDelInventario ingreso) =>
        ingreso.Cantidad == fila.Cantidad
        && fila.UsuarioId != Guid.Empty
        && ingreso.UsuarioId == fila.UsuarioId
        && (fila.Referencia is null || string.Equals(fila.Referencia, ingreso.Referencia, StringComparison.Ordinal))
        && ingreso.Instante >= fila.SolicitadoEn - MargenAnterior
        && ingreso.Instante <= fila.UltimoIntentoEn + MargenPosterior;

    private async Task<Resultado<ResultadoDeIngreso>> Aplicar(Guid ingresoSolicitadoId, ResolucionDeIngreso resolucion, IngresoDelInventario? coincidente, CancellationToken cancelacion)
    {
        var fila = await ingresos.Obtener(ingresoSolicitadoId, paraActualizar: true, cancelacion).ConfigureAwait(false);
        if (fila is null)
        {
            return FallosDelCatalogo.De<ResultadoDeIngreso>(CategoriaDeError.NoEncontrado, ClavesDelCatalogo.IngresoNoEncontrado);
        }

        if (!fila.EstaPendiente || !await Transicionar(fila, resolucion, coincidente, cancelacion).ConfigureAwait(false))
        {
            // 0 filas afectadas: otro la resolvió entre la lectura y la escritura. Se audita la discrepancia y se informa, sin reescribir la fila.
            await auditoria.Registrar(Solicitud(fila, "discrepancia"), cancelacion).ConfigureAwait(false);
            return FallosDelCatalogo.De<ResultadoDeIngreso>(CategoriaDeError.Conflicto, ClavesDelCatalogo.IngresoYaResuelto);
        }

        await auditoria.Registrar(Solicitud(fila, resolucion == ResolucionDeIngreso.Registrado ? "registrado" : "fallido"), cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeIngreso>.Exito(new ResultadoDeIngreso(fila.Id, fila.EdicionId, MarcarResultadoDeIngreso.ADelDominio(fila.Estado)));
    }

    private async Task<bool> Transicionar(IngresoSolicitado fila, ResolucionDeIngreso resolucion, IngresoDelInventario? coincidente, CancellationToken cancelacion)
    {
        if (resolucion == ResolucionDeIngreso.Registrado)
        {
            fila.MarcarRegistrado(coincidente!.IngresoId, reloj.Ahora);
        }
        else
        {
            fila.MarcarFallido(reloj.Ahora);
        }

        return await ingresos.GuardarTransicion(fila, cancelacion).ConfigureAwait(false);
    }

    private static SolicitudDeRegistro Solicitud(IngresoSolicitado fila, string resolucion) =>
        new(AccionesAuditables.IngresoPendienteConciliado)
        {
            ObjetoTipo = "ingreso",
            ObjetoId = fila.Id.ToString("D"),
            Detalle = new Dictionary<string, string?>
            {
                ["edicionId"] = fila.EdicionId.ToString("D"),
                ["cantidad"] = fila.Cantidad.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["intentos"] = fila.Intentos.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["resolucion"] = resolucion,
            },
        };
}
