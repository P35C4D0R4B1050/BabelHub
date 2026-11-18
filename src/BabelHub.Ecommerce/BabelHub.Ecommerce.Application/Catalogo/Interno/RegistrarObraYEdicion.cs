using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

/// <summary>
/// Crea las filas <c>pendiente</c> de ingreso del stock inicial de las ediciones impresas (CAT-20): la clave de idempotencia es el nonce que emite <see cref="IEmisorDeClavesDeIngreso"/> en el alta
/// (el reintento de un <c>pendiente</c> reutiliza su clave y su carga, D04). La fila nace en la misma transacción que la obra, antes de llamar al Inventario.
/// </summary>
internal sealed class SolicitudDeIngresosIniciales(IRepositorioDeIngresos ingresos, IEmisorDeClavesDeIngreso emisor, IReloj reloj)
{
    public async Task<IReadOnlyList<IngresoDeAlta>> Solicitar(Guid usuarioId, IEnumerable<EdicionConstruida> ediciones, CancellationToken cancelacion)
    {
        var resultado = new List<IngresoDeAlta>();
        foreach (var edicion in ediciones.Where(e => e.StockInicial is not null))
        {
            var emitido = emisor.Emitir(usuarioId, edicion.Edicion.Id, reloj.Ahora);
            var ingreso = IngresoSolicitado.Solicitar(Guid.NewGuid(), edicion.Edicion.Id, emitido.Nonce, usuarioId, edicion.StockInicial!.Value, null, null, reloj.Ahora);
            if (!await ingresos.Agregar(ingreso, cancelacion).ConfigureAwait(false))
            {
                // Una edición nueva no puede tener ya un pendiente: es un estado imposible y el alta se revierte antes que dejar un ingreso sin constancia.
                throw new InvalidOperationException("No se pudo registrar la constancia del ingreso inicial.");
            }

            resultado.Add(new IngresoDeAlta(ingreso.Id, ingreso.EdicionId, ingreso.Cantidad, EstadoDeIngresoSolicitado.Pendiente));
        }

        return resultado;
    }
}

/// <summary>
/// <c>RegistrarObra</c> (CAT-01 a CAT-06, CAT-19, CAT-20, CAT-23): una obra con su portada obligatoria y al menos una edición (a lo más una impresa y una digital), con la portada y el archivo de la
/// digital <b>consumidos</b> en la misma transacción, el ISBN repetido rechazado por el índice único (con el título que ya lo usa, leído después y solo tras comprobar el rol), la fila
/// <c>pendiente</c> del ingreso inicial y la auditoría, todo con <c>TransaccionPorCasoDeUso</c> alrededor: si no se puede auditar, nada se crea. El ingreso al Inventario lo hace después, tras
/// confirmar, el orquestador (T12): este caso de uso nunca llama al Inventario.
/// </summary>
internal sealed class RegistrarObra(
    AutorizadorDelCatalogo autorizador,
    IValidador<RegistrarObraEntrada> validador,
    ConsumirReferenciaDeArchivo referencias,
    ConstructorDeEdiciones constructor,
    SolicitudDeIngresosIniciales ingresosIniciales,
    IRepositorioDeObras obras,
    IAuditoria auditoria,
    IReloj reloj) : ICasoDeUso<RegistrarObraEntrada, ResultadoDeAlta>
{
    public async Task<Resultado<ResultadoDeAlta>> Ejecutar(RegistrarObraEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeAlta>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDelCatalogo.Validacion<ResultadoDeAlta>(validacion);
        }

        var usuarioId = autorizador.UsuarioId!.Value;
        var portada = await referencias.Consumir(entrada.PortadaRef, usuarioId, FinalidadDeArchivo.Portada, null, "portadaRef", cancelacion).ConfigureAwait(false);
        if (!portada.EsExito)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeAlta>(portada.Error!);
        }

        var construidas = new List<EdicionConstruida>();
        for (var i = 0; i < entrada.Ediciones!.Count; i++)
        {
            var edicion = await constructor.Construir(entrada.Ediciones[i], $"ediciones[{i}].", usuarioId, cancelacion).ConfigureAwait(false);
            if (!edicion.EsExito)
            {
                return FallosDelCatalogo.Fallo<ResultadoDeAlta>(edicion.Error!);
            }

            construidas.Add(edicion.Valor);
        }

        var registro = Obra.Registrar(Guid.NewGuid(), new DatosDeObra(entrada.Titulo, entrada.Autor, entrada.Editorial, entrada.Categoria), portada.Valor.ComoPortada(), construidas.Select(e => e.Edicion), reloj.Ahora);
        if (!registro.EsExito)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeAlta>(TraductorDeRechazos.De(registro.Rechazo!, entrada.Titulo?.Trim()));
        }

        var obra = registro.Valor;
        if (await obras.Agregar(obra, cancelacion).ConfigureAwait(false) == ResultadoDeEscrituraDeObra.IsbnDuplicado)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeAlta>(await TituloDelIsbnRepetido(obras, obra.Ediciones, cancelacion).ConfigureAwait(false));
        }

        var ingresos = await ingresosIniciales.Solicitar(usuarioId, construidas, cancelacion).ConfigureAwait(false);

        // La auditoría va en la misma transacción: si no se puede registrar (AuditoriaNoDisponibleException), la excepción sube y TransaccionPorCasoDeUso revierte la obra, las referencias y las filas.
        await auditoria.Registrar(AuditoriaDelCatalogo.ObraRegistrada(obra), cancelacion).ConfigureAwait(false);
        foreach (var edicion in obra.Ediciones)
        {
            await auditoria.Registrar(AuditoriaDelCatalogo.EdicionRegistrada(edicion), cancelacion).ConfigureAwait(false);
        }

        return Resultado<ResultadoDeAlta>.Exito(new ResultadoDeAlta(obra.Id, obra.Version, obra.Ediciones.Select(e => e.Id).ToList(), ingresos));
    }

    /// <summary>El error de ISBN repetido con el título que ya lo usa, leído en una lectura posterior (nunca de una transacción abortada).</summary>
    internal static async Task<ErrorDeAplicacion> TituloDelIsbnRepetido(IRepositorioDeObras obras, IEnumerable<Edicion> ediciones, CancellationToken cancelacion)
    {
        foreach (var edicion in ediciones)
        {
            if (await obras.TituloDeLaEdicionConIsbn(edicion.Isbn.Isbn13, cancelacion).ConfigureAwait(false) is { } titulo)
            {
                return TraductorDeRechazos.IsbnDuplicado(titulo);
            }
        }

        return TraductorDeRechazos.IsbnDuplicado(null);
    }
}

/// <summary>
/// <c>RegistrarEdicion</c> (CAT-02, CAT-19, CAT-20): agrega una edición a una obra existente con la concurrencia optimista de la versión de la obra, la misma regla de ISBN, tipo y licencia, su ingreso
/// inicial opcional (solo una impresa) y la auditoría en la misma transacción.
/// </summary>
internal sealed class RegistrarEdicion(
    AutorizadorDelCatalogo autorizador,
    IValidador<RegistrarEdicionEntrada> validador,
    ConstructorDeEdiciones constructor,
    SolicitudDeIngresosIniciales ingresosIniciales,
    IRepositorioDeObras obras,
    IAuditoria auditoria) : ICasoDeUso<RegistrarEdicionEntrada, ResultadoDeAlta>
{
    public async Task<Resultado<ResultadoDeAlta>> Ejecutar(RegistrarEdicionEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeAlta>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDelCatalogo.Validacion<ResultadoDeAlta>(validacion);
        }

        var obra = await obras.Obtener(entrada.ObraId, paraActualizar: true, cancelacion).ConfigureAwait(false);
        if (obra is null)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeAlta>(TraductorDeRechazos.ObraNoEncontrada());
        }

        if (!obra.CoincideLaVersion(entrada.Version))
        {
            return FallosDelCatalogo.Fallo<ResultadoDeAlta>(TraductorDeRechazos.ConflictoDeVersion());
        }

        var usuarioId = autorizador.UsuarioId!.Value;
        var construida = await constructor.Construir(entrada.Edicion, "edicion.", usuarioId, cancelacion).ConfigureAwait(false);
        if (!construida.EsExito)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeAlta>(construida.Error!);
        }

        var versionLeida = obra.Version;
        var agregada = obra.AgregarEdicion(construida.Valor.Edicion);
        if (!agregada.EsExito)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeAlta>(TraductorDeRechazos.De(agregada.Rechazo!, obra.Titulo));
        }

        switch (await obras.Guardar(obra, versionLeida, cancelacion).ConfigureAwait(false))
        {
            case ResultadoDeEscrituraDeObra.IsbnDuplicado:
                return FallosDelCatalogo.Fallo<ResultadoDeAlta>(await RegistrarObra.TituloDelIsbnRepetido(obras, [construida.Valor.Edicion], cancelacion).ConfigureAwait(false));
            case ResultadoDeEscrituraDeObra.ConflictoDeVersion:
                return FallosDelCatalogo.Fallo<ResultadoDeAlta>(TraductorDeRechazos.ConflictoDeVersion());
        }

        var ingresos = await ingresosIniciales.Solicitar(usuarioId, [construida.Valor], cancelacion).ConfigureAwait(false);
        await auditoria.Registrar(AuditoriaDelCatalogo.EdicionRegistrada(construida.Valor.Edicion), cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeAlta>.Exito(new ResultadoDeAlta(obra.Id, obra.Version, [construida.Valor.Edicion.Id], ingresos));
    }
}
