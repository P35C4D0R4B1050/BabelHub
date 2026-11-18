using System.Globalization;
using System.Text.Json;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Ajustes;

namespace BabelHub.Inventario.Application.Ajustes.Interno;

internal sealed class ValidadorDeRegistrarIngreso : IValidador<RegistrarIngresoDeMercaderiaEntrada>
{
    public ResultadoDeValidacion Validar(RegistrarIngresoDeMercaderiaEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        if (entrada.EdicionId == Guid.Empty)
        {
            errores.Add(new ErrorDeCampo("edicionId", ConstructorDeValidacion.Obligatorio));
        }

        if (entrada.CodigoDeFuente is not null)
        {
            ReglasDeEntradaDeAjustes.Codigo(errores, "codigoDeFuente", entrada.CodigoDeFuente);
        }

        // El rango de la cantidad (1 a 5 000) es una regla de negocio que decide el Dominio (D08): aquí solo la forma de la entrada.
        ReglasDeEntradaDeAjustes.Referencia(errores, entrada.Referencia);
        ReglasDeEntradaDeAjustes.Nota(errores, entrada.Nota);
        ReglasDeEntradaDeAjustes.Clave(errores, entrada.ClaveDeIdempotencia);
        return ReglasDeEntradaDeAjustes.Resultado(errores);
    }
}

/// <summary>
/// Registra un ingreso de mercadería (AJU-13, AJU-14, AJU-19, AJU-22): solo la administración central (cualquier otro, rechazado con acceso denegado) y solo en la fuente de recepción (si la solicitud no trae fuente, va a ella).
/// Orden: rol, forma, presupuesto, bloqueo asesor de recepción (para no cruzarse con un cambio de recepción), fuente y que sea la de recepción, clave de idempotencia (misma carga: el original; otra: conflicto), reglas del Dominio
/// (cantidad de 1 a 5 000, referencia), ficha de la edición, <c>Bloquear</c> y <c>Aplicar(+c, 0)</c>, la fila de <c>ingresos</c> y la del ajuste con motivo <c>ingreso-de-mercaderia</c> en la misma transacción, con
/// <b>una sola</b> entrada de auditoría (<c>IngresoDeMercaderiaRegistrado</c>, con la cadena anterior/posterior). Dos ingresos simultáneos sobre una pareja nueva dejan 0 → 10 y 10 → 15 porque <c>Bloquear</c> serializa.
/// La referencia y la nota nunca van a la auditoría ni a los errores (D04).
/// </summary>
internal sealed class RegistrarIngresoDeMercaderia(
    AutorizadorDeAjustes autorizador,
    IValidador<RegistrarIngresoDeMercaderiaEntrada> validador,
    PresupuestoDelPersonal presupuesto,
    IBloqueosAsesores bloqueos,
    IDirectorioDeFuentes fuentes,
    IFichasDeEdiciones fichas,
    IMovimientosDeStock stock,
    IRepositorioDeAjustes ajustes,
    IRepositorioDeIngresos ingresos,
    IRepositorioDeOperacionesIdempotentes operaciones,
    IAuditoria auditoria,
    IReloj reloj) : ICasoDeUso<RegistrarIngresoDeMercaderiaEntrada, IngresoRegistrado>
{
    private const string TipoDeOperacion = "ingreso";

    public async Task<Resultado<IngresoRegistrado>> Ejecutar(RegistrarIngresoDeMercaderiaEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeAjustes.Fallo<IngresoRegistrado>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeAjustes.Validacion<IngresoRegistrado>(validacion);
        }

        var usuarioId = autorizador.UsuarioId!.Value;
        if (await presupuesto.Consumir(usuarioId, cancelacion).ConfigureAwait(false) is { } sinPresupuesto)
        {
            return FallosDeAjustes.Fallo<IngresoRegistrado>(sinPresupuesto);
        }

        try
        {
            return await Registrar(entrada, usuarioId, cancelacion).ConfigureAwait(false);
        }
        catch (EsperaDeBloqueoAgotadaException)
        {
            return FallosDeAjustes.BloqueoOcupado<IngresoRegistrado>();
        }
    }

    private async Task<Resultado<IngresoRegistrado>> Registrar(RegistrarIngresoDeMercaderiaEntrada entrada, Guid usuarioId, CancellationToken cancelacion)
    {
        // Para no cruzarse con quien cambia la fuente de recepción (FUE-19, FUE-20).
        await bloqueos.Recepcion(cancelacion).ConfigureAwait(false);
        var recepcion = await fuentes.FuenteDeRecepcion(cancelacion).ConfigureAwait(false);
        if (recepcion is null)
        {
            return FallosDeAjustes.De<IngresoRegistrado>(CategoriaDeError.NoEncontrado, ClavesDeAjustes.FuenteNoEncontrada);
        }

        if (entrada.CodigoDeFuente is not null)
        {
            var pedida = await fuentes.PorCodigo(entrada.CodigoDeFuente, cancelacion).ConfigureAwait(false);
            if (pedida is null)
            {
                return FallosDeAjustes.De<IngresoRegistrado>(CategoriaDeError.NoEncontrado, ClavesDeAjustes.FuenteNoEncontrada);
            }

            if (pedida.Id != recepcion.Id)
            {
                return FallosDeAjustes.De<IngresoRegistrado>(CategoriaDeError.ReglaDeNegocio, ClavesDeAjustes.FuenteNoEsDeRecepcion);
            }
        }

        var ingresoId = Guid.NewGuid();
        var ajusteId = Guid.NewGuid();
        var huella = ReglasDeEntradaDeAjustes.Huella(
            $"ingreso|{recepcion.Id:N}|{entrada.EdicionId:N}|{entrada.Cantidad.ToString(CultureInfo.InvariantCulture)}|{entrada.Referencia}|{entrada.Nota}");
        var previa = await operaciones.Registrar(usuarioId, TipoDeOperacion, entrada.ClaveDeIdempotencia, huella, JsonSerializer.Serialize(new { ingresoId, ajusteId }), cancelacion).ConfigureAwait(false);
        if (previa is not null)
        {
            return await Repetir(previa, huella, recepcion, cancelacion).ConfigureAwait(false);
        }

        if (ReglasDeIngreso.Evaluar(entrada.Cantidad, entrada.Referencia) is { } regla)
        {
            return FallosDeAjustes.De<IngresoRegistrado>(CategoriaDeError.ReglaDeNegocio, regla == MotivoDeRechazoDeIngreso.CantidadFueraDeRango ? ClavesDeAjustes.CantidadFueraDeRango : ClavesDeAjustes.ReferenciaInvalida);
        }

        var ficha = await fichas.PorId(entrada.EdicionId, cancelacion).ConfigureAwait(false);
        if (ficha is null)
        {
            return FallosDeAjustes.De<IngresoRegistrado>(CategoriaDeError.NoEncontrado, ClavesDeAjustes.EdicionNoEncontrada);
        }

        if (!ficha.EsImpresa)
        {
            return FallosDeAjustes.De<IngresoRegistrado>(CategoriaDeError.ReglaDeNegocio, ClavesDeAjustes.EdicionNoImpresa);
        }

        var bloqueo = await stock.Bloquear([entrada.EdicionId], cancelacion).ConfigureAwait(false);
        if (!bloqueo.EstaBloqueado)
        {
            return FallosDeAjustes.De<IngresoRegistrado>(CategoriaDeError.ReglaDeNegocio, ClavesDeAjustes.EdicionNoImpresa);
        }

        var fila = bloqueo.Fila(recepcion.Id, entrada.EdicionId);
        var decision = ReglasDeAjuste.Sumar(fila.Fisico, entrada.Cantidad);
        if (!decision.Aceptado)
        {
            return FallosDeAjustes.De<IngresoRegistrado>(CategoriaDeError.ReglaDeNegocio, ClavesDeAjustes.CantidadFueraDeRango);
        }

        var resultado = await stock.Aplicar(new MovimientoDeStock(recepcion.Id, entrada.EdicionId, decision.DeltaFisico, 0), cancelacion).ConfigureAwait(false);
        if (resultado.Resultado == ResultadoDeMovimiento.DeniegadoPorAlcance)
        {
            return FallosDeAjustes.Fallo<IngresoRegistrado>(await autorizador.DenegadoPorAlcance(cancelacion).ConfigureAwait(false));
        }

        if (!resultado.Aplicado)
        {
            throw new InvalidOperationException("No se pudo aplicar un ingreso ya decidido sobre una fila bloqueada.");
        }

        var ahora = reloj.Ahora;
        var ajuste = Ajuste.Ingresado(ajusteId, recepcion.Id, entrada.EdicionId, decision, entrada.Nota, usuarioId, ahora);
        var ingreso = IngresoDeMercaderia.Nuevo(ingresoId, recepcion.Id, entrada.EdicionId, entrada.Cantidad, entrada.Referencia, entrada.Nota, usuarioId, ahora, ajusteId);

        // La fila de ajuste y la de ingreso van juntas y con una sola entrada de auditoría: si cualquiera falla, la transacción se revierte entera.
        await ajustes.Insertar(ajuste, cancelacion).ConfigureAwait(false);
        await ingresos.Insertar(ingreso, cancelacion).ConfigureAwait(false);
        await auditoria.Registrar(
            ConversionDeAjustes.Auditar(AccionesAuditables.IngresoDeMercaderiaRegistrado, "ingreso", ingresoId, recepcion.Id, ajuste, new Dictionary<string, string?> { ["ingresoId"] = ingresoId.ToString("D") }),
            cancelacion).ConfigureAwait(false);

        return Resultado<IngresoRegistrado>.Exito(ARespuesta(ingreso, ajuste, recepcion.Codigo));
    }

    private async Task<Resultado<IngresoRegistrado>> Repetir(OperacionIdempotenteGuardada previa, string huella, FuenteDeStock recepcion, CancellationToken cancelacion)
    {
        if (previa.HuellaDeLaCarga != huella)
        {
            return FallosDeAjustes.De<IngresoRegistrado>(CategoriaDeError.Conflicto, ClavesDeAjustes.IdempotenciaEnConflicto);
        }

        using var documento = JsonDocument.Parse(previa.Resultado);
        var ingreso = await ingresos.PorId(documento.RootElement.GetProperty("ingresoId").GetGuid(), cancelacion).ConfigureAwait(false);
        var ajuste = await ajustes.PorId(documento.RootElement.GetProperty("ajusteId").GetGuid(), cancelacion).ConfigureAwait(false);
        return ingreso is null || ajuste is null || ingreso.FuenteId != recepcion.Id
            ? FallosDeAjustes.De<IngresoRegistrado>(CategoriaDeError.Conflicto, ClavesDeAjustes.IdempotenciaEnConflicto)
            : Resultado<IngresoRegistrado>.Exito(ARespuesta(ingreso, ajuste, recepcion.Codigo));
    }

    private static IngresoRegistrado ARespuesta(IngresoDeMercaderia ingreso, Ajuste ajuste, string codigoDeFuente) =>
        new(ingreso.Id, ajuste.Id, ingreso.EdicionId, codigoDeFuente, ingreso.Cantidad, ingreso.Referencia, ingreso.Nota, ingreso.UsuarioId, ingreso.Fecha, ajuste.CantidadAnterior, ajuste.CantidadPosterior);
}

internal sealed class ValidadorDeConsultarIngresos : IValidador<ConsultarIngresosEntrada>
{
    public ResultadoDeValidacion Validar(ConsultarIngresosEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        if (entrada.Limite is < 1 or > LimitesDeEntradaDeAjustes.MaximoDeLimiteDeIngresos)
        {
            errores.Add(new ErrorDeCampo("limite", ConstructorDeValidacion.FueraDeRango));
        }

        if (entrada.EdicionId == Guid.Empty)
        {
            errores.Add(new ErrorDeCampo("edicionId", ConstructorDeValidacion.FormatoInvalido));
        }

        return ReglasDeEntradaDeAjustes.Resultado(errores);
    }
}

/// <summary>Los últimos ingresos de mercadería (AJU-20): solo la administración central; cualquier otro usuario, rechazado con acceso denegado y sin leer nada.</summary>
internal sealed class ConsultarIngresos(
    AutorizadorDeAjustes autorizador,
    IValidador<ConsultarIngresosEntrada> validador,
    IDirectorioDeFuentes fuentes,
    IRepositorioDeIngresos ingresos) : ICasoDeUso<ConsultarIngresosEntrada, IngresosConsultados>
{
    public async Task<Resultado<IngresosConsultados>> Ejecutar(ConsultarIngresosEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeAjustes.Fallo<IngresosConsultados>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeAjustes.Validacion<IngresosConsultados>(validacion);
        }

        var ultimos = await ingresos.Ultimos(entrada.EdicionId, entrada.Limite, cancelacion).ConfigureAwait(false);
        var codigos = (await fuentes.Listar(cancelacion).ConfigureAwait(false)).ToDictionary(f => f.Id, f => f.Codigo);
        return Resultado<IngresosConsultados>.Exito(new IngresosConsultados(
            [.. ultimos.Select(i => new IngresoListado(i.Id, i.EdicionId, codigos.GetValueOrDefault(i.FuenteId, string.Empty), i.Cantidad, i.Referencia, i.Nota, i.UsuarioId, i.Fecha))]));
    }
}
