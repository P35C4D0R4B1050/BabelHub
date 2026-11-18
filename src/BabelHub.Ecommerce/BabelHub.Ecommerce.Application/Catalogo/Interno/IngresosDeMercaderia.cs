using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

/// <summary>Qué significa la respuesta del Inventario a un ingreso para la fila local.</summary>
internal enum ClasificacionDeIngreso
{
    /// <summary>201 bien formado: el ingreso se aplicó.</summary>
    Registrado,

    /// <summary>Una respuesta que prueba que el ingreso no se aplicó (lista cerrada, solo en el primer envío).</summary>
    Fallido,

    /// <summary>Incierto (tiempo agotado, corte, 5xx, 429, un rechazo de forma inesperada…): se queda <c>Pendiente</c> y se reintenta con su misma clave o se concilia.</summary>
    Pendiente,
}

/// <summary>
/// Decide si una respuesta del Inventario prueba que un ingreso <b>no</b> se aplicó (D04, <c>plan.md</c> §1). <c>Fallido</c> es una <b>lista cerrada</b>: el 401 (<c>INV-NO-AUTENTICADO</c>) y los códigos
/// <c>INV-VALIDACION-INVALIDA</c>, <c>INV-PROHIBIDO</c>, <c>INV-NO-ENCONTRADO</c>, <c>INV-EDICION-NO-IMPRESA</c> e <c>INV-FUENTE-NO-ES-DE-RECEPCION</c>, con un <c>problem+json</c> bien formado (el código <b>y</b> un estado HTTP
/// coherente con él: un 401, 403 o 404 de forma inesperada deja <c>Pendiente</c>, SEG-13). Los dos 409 de idempotencia, un tiempo agotado, un 5xx, un corte y un 429 dejan <c>Pendiente</c> <b>en el primer envío</b>; en
/// un reintento todo resultado distinto de 201 deja <c>Pendiente</c> (un token vencido o un rol revocado entre intentos no prueba que el primero no se aplicó).
/// </summary>
internal static class ClasificadorDeIngreso
{
    private static readonly IReadOnlyDictionary<string, int[]> ListaCerrada = new Dictionary<string, int[]>(StringComparer.Ordinal)
    {
        ["INV-NO-AUTENTICADO"] = [401],
        ["INV-PROHIBIDO"] = [403],
        ["INV-NO-ENCONTRADO"] = [404],
        ["INV-VALIDACION-INVALIDA"] = [400, 422],
        ["INV-EDICION-NO-IMPRESA"] = [409, 422],
        ["INV-FUENTE-NO-ES-DE-RECEPCION"] = [409, 422],
    };

    public static ClasificacionDeIngreso Clasificar(RespuestaDeIngreso respuesta, bool esReintento)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        if (respuesta.Tipo == TipoDeRespuestaDeIngreso.Creado)
        {
            return respuesta.IngresoId is { } id && id != Guid.Empty ? ClasificacionDeIngreso.Registrado : ClasificacionDeIngreso.Pendiente;
        }

        if (esReintento || respuesta.Tipo != TipoDeRespuestaDeIngreso.Rechazado)
        {
            return ClasificacionDeIngreso.Pendiente;
        }

        return respuesta.CodigoDeError is { } codigo && respuesta.EstadoHttp is { } estado && ListaCerrada.TryGetValue(codigo, out var estados) && estados.Contains(estado)
            ? ClasificacionDeIngreso.Fallido
            : ClasificacionDeIngreso.Pendiente;
    }
}

/// <summary>Lo que se anota de la llamada al Inventario: la clasificación y, si se aplicó, el identificador del ingreso.</summary>
internal sealed record MarcarResultadoDeIngresoEntrada(Guid IngresoSolicitadoId, ClasificacionDeIngreso Clasificacion, Guid? IngresoId);

/// <summary>
/// <c>MarcarResultadoDeIngreso</c> (transaccional, en su propia transacción corta tras la llamada al Inventario, D15): pasa la fila a <c>Registrado</c> (con el identificador opaco del ingreso) o a <c>Fallido</c>; con
/// <c>Pendiente</c> no cambia nada. La transición es <b>condicional</b> (<c>WHERE estado = pendiente</c>): si otro la resolvió antes (una conciliación), no se pisa y se devuelve el estado actual. Al registrarse
/// invalida la disponibilidad guardada de la edición (CAT-22). El Catálogo no audita el ingreso: lo hace el Inventario con el actor correcto.
/// </summary>
internal sealed class MarcarResultadoDeIngreso(IRepositorioDeIngresos ingresos, IMemoriaDeDisponibilidad memoria, IReloj reloj) : ICasoDeUso<MarcarResultadoDeIngresoEntrada, EstadoDeIngresoSolicitado>
{
    public async Task<Resultado<EstadoDeIngresoSolicitado>> Ejecutar(MarcarResultadoDeIngresoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var ingreso = await ingresos.Obtener(entrada.IngresoSolicitadoId, paraActualizar: true, cancelacion).ConfigureAwait(false);
        if (ingreso is null)
        {
            return FallosDelCatalogo.De<EstadoDeIngresoSolicitado>(CategoriaDeError.NoEncontrado, ClavesDelCatalogo.IngresoNoEncontrado);
        }

        if (!ingreso.EstaPendiente || entrada.Clasificacion == ClasificacionDeIngreso.Pendiente)
        {
            return Resultado<EstadoDeIngresoSolicitado>.Exito(ADelDominio(ingreso.Estado));
        }

        if (entrada.Clasificacion == ClasificacionDeIngreso.Registrado)
        {
            ingreso.MarcarRegistrado(entrada.IngresoId ?? throw new ArgumentException("Un ingreso registrado trae su identificador.", nameof(entrada)), reloj.Ahora);
        }
        else
        {
            ingreso.MarcarFallido(reloj.Ahora);
        }

        if (!await ingresos.GuardarTransicion(ingreso, cancelacion).ConfigureAwait(false))
        {
            // Otro la resolvió entre la lectura y la escritura: se devuelve lo que quedó.
            var actual = await ingresos.Obtener(ingreso.Id, paraActualizar: false, cancelacion).ConfigureAwait(false);
            return Resultado<EstadoDeIngresoSolicitado>.Exito(ADelDominio(actual?.Estado ?? EstadoDeIngreso.Pendiente));
        }

        if (ingreso.Estado == EstadoDeIngreso.Registrado)
        {
            memoria.Invalidar(ingreso.EdicionId);
        }

        return Resultado<EstadoDeIngresoSolicitado>.Exito(ADelDominio(ingreso.Estado));
    }

    internal static EstadoDeIngresoSolicitado ADelDominio(EstadoDeIngreso estado) =>
        estado switch
        {
            EstadoDeIngreso.Registrado => EstadoDeIngresoSolicitado.Registrado,
            EstadoDeIngreso.Fallido => EstadoDeIngresoSolicitado.Fallido,
            _ => EstadoDeIngresoSolicitado.Pendiente,
        };
}

/// <summary>La fila que dejó el primer paso de un ingreso y si se creó ahora (hay que enviarla al Inventario) o ya existía (repetición de la misma clave: no se envía de nuevo).</summary>
internal sealed record IngresoPedido(IngresoSolicitado Fila, bool EsNueva);

/// <summary>
/// <c>RegistrarIngresoDeMercaderia</c> (CAT-22, CAT-23, AC-CAT-18, AC-CAT-19): solo la administración central, comprobado antes de leer nada. Primer paso del ingreso, en <b>su propia transacción</b>: valida el token
/// firmado (de este usuario y de esta edición, vigente), exige que la edición sea una impresa no dada de baja, que no haya otro <c>Pendiente</c> de la edición (<c>ingreso-pendiente</c>, 409) y deja la fila
/// <c>Pendiente</c> <b>antes</b> de llamar al Inventario. La misma clave con la misma carga repite el resultado sin crear otra fila (también dos envíos simultáneos); con otra carga, de un <c>Fallido</c>, de otro usuario o
/// de otra edición responde <c>clave-de-ingreso-invalida</c> (409). La llamada al Inventario y el marcado del resultado son del orquestador (nunca dentro de esta transacción).
/// </summary>
internal sealed class RegistrarIngresoDeMercaderia(
    AutorizadorDelCatalogo autorizador,
    IValidador<RegistrarIngresoEntrada> validador,
    IRepositorioDeObras obras,
    IRepositorioDeIngresos ingresos,
    IEmisorDeClavesDeIngreso emisor,
    IReloj reloj) : ICasoDeUso<RegistrarIngresoEntrada, IngresoPedido>
{
    public async Task<Resultado<IngresoPedido>> Ejecutar(RegistrarIngresoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDelCatalogo.Fallo<IngresoPedido>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDelCatalogo.Validacion<IngresoPedido>(validacion);
        }

        var usuarioId = autorizador.UsuarioId!.Value;
        var obra = await obras.ObtenerPorEdicion(entrada.EdicionId, paraActualizar: false, cancelacion).ConfigureAwait(false);
        var edicion = obra?.BuscarEdicion(entrada.EdicionId);
        if (edicion is null)
        {
            return FallosDelCatalogo.Fallo<IngresoPedido>(TraductorDeRechazos.De(new RechazoDeObra(RechazoDeCatalogo.EdicionNoEncontrada)));
        }

        // Un ingreso es de una edición impresa activa (AJU-18): una digital nunca tiene stock (regla 12) y una dada de baja no se repone.
        if (edicion.EsDigital || edicion.EstaDadaDeBaja)
        {
            return FallosDelCatalogo.Validacion<IngresoPedido>(ResultadoDeValidacion.Invalido([new ErrorDeCampo("edicionId", "solo-edicion-impresa-activa")]));
        }

        if (emisor.Verificar(entrada.Token, usuarioId, entrada.EdicionId, reloj.Ahora) is not { } nonce)
        {
            return ClaveInvalida();
        }

        if (await ingresos.PorClave(nonce, cancelacion).ConfigureAwait(false) is { } existente)
        {
            return Repeticion(existente, entrada, usuarioId);
        }

        if (await ingresos.PendienteDeLaEdicion(entrada.EdicionId, cancelacion).ConfigureAwait(false) is not null)
        {
            return FallosDelCatalogo.De<IngresoPedido>(CategoriaDeError.Conflicto, ClavesDelCatalogo.IngresoPendiente);
        }

        var fila = IngresoSolicitado.Solicitar(Guid.NewGuid(), entrada.EdicionId, nonce, usuarioId, entrada.Cantidad, entrada.Referencia, entrada.Nota, reloj.Ahora);
        if (!await ingresos.Agregar(fila, cancelacion).ConfigureAwait(false))
        {
            // Una carrera: otro envío con la misma clave se adelantó (se reduce a uno) o apareció otro pendiente de la edición.
            if (await ingresos.PorClave(nonce, cancelacion).ConfigureAwait(false) is { } adelantado)
            {
                return Repeticion(adelantado, entrada, usuarioId);
            }

            return FallosDelCatalogo.De<IngresoPedido>(CategoriaDeError.Conflicto, ClavesDelCatalogo.IngresoPendiente);
        }

        return Resultado<IngresoPedido>.Exito(new IngresoPedido(fila, EsNueva: true));
    }

    private static Resultado<IngresoPedido> ClaveInvalida() => FallosDelCatalogo.De<IngresoPedido>(CategoriaDeError.Conflicto, ClavesDelCatalogo.ClaveDeIngresoInvalida);

    /// <summary>La misma clave: con la misma carga y no fallida es la misma solicitud (se devuelve su estado, sin crear otra ni volver a enviar); con otra carga o de un <c>Fallido</c> la clave ya no sirve.</summary>
    private static Resultado<IngresoPedido> Repeticion(IngresoSolicitado existente, RegistrarIngresoEntrada entrada, Guid usuarioId)
    {
        var mismaCarga = existente.EdicionId == entrada.EdicionId
            && existente.EsDelUsuario(usuarioId)
            && existente.Cantidad == entrada.Cantidad
            && string.Equals(existente.Referencia, Normalizar(entrada.Referencia), StringComparison.Ordinal)
            && string.Equals(existente.Nota, Normalizar(entrada.Nota), StringComparison.Ordinal);

        return mismaCarga && existente.Estado != EstadoDeIngreso.Fallido
            ? Resultado<IngresoPedido>.Exito(new IngresoPedido(existente, EsNueva: false))
            : ClaveInvalida();
    }

    private static string? Normalizar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
