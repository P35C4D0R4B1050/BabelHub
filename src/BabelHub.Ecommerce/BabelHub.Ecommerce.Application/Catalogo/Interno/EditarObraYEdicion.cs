using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

/// <summary>
/// <c>EditarObra</c> (CAT-01, CAT-19, AC-CAT-12): cambia los datos de la obra y, si trae una portada nueva, reemplaza la anterior. Concurrencia optimista por la versión de la obra (la segunda de dos
/// ediciones simultáneas recibe <c>conflicto-de-version</c>); la referencia de la portada se consume en la misma transacción; el objeto reemplazado pasa a <c>descarte</c> para que el trabajo de fondo
/// lo borre; y se audita el cambio de la obra y el reemplazo de la portada con su tipo, su tamaño y su huella corta (nunca la clave del almacén).
/// </summary>
internal sealed class EditarObra(
    AutorizadorDelCatalogo autorizador,
    IValidador<EditarObraEntrada> validador,
    ConsumirReferenciaDeArchivo referencias,
    IRepositorioDeObras obras,
    IRepositorioDeReferenciasDeArchivo archivos,
    IAuditoria auditoria,
    IReloj reloj) : ICasoDeUso<EditarObraEntrada, ResultadoDeEscritura>
{
    public async Task<Resultado<ResultadoDeEscritura>> Ejecutar(EditarObraEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDelCatalogo.Validacion<ResultadoDeEscritura>(validacion);
        }

        var obra = await obras.Obtener(entrada.ObraId, paraActualizar: true, cancelacion).ConfigureAwait(false);
        if (obra is null)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.ObraNoEncontrada());
        }

        if (!obra.CoincideLaVersion(entrada.Version))
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.ConflictoDeVersion());
        }

        var versionLeida = obra.Version;
        var usuarioId = autorizador.UsuarioId!.Value;

        ReferenciaDeArchivo? portadaNueva = null;
        if (entrada.PortadaRef is not null)
        {
            var consumida = await referencias.Consumir(entrada.PortadaRef, usuarioId, FinalidadDeArchivo.Portada, null, "portadaRef", cancelacion).ConfigureAwait(false);
            if (!consumida.EsExito)
            {
                return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(consumida.Error!);
            }

            portadaNueva = consumida.Valor;
        }

        var datos = obra.EditarDatos(new DatosDeObra(entrada.Titulo, entrada.Autor, entrada.Editorial, entrada.Categoria));
        if (!datos.EsExito)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.De(datos.Rechazo!));
        }

        PortadaDeObra? anterior = null;
        if (portadaNueva is not null)
        {
            anterior = obra.ReemplazarPortada(portadaNueva.ComoPortada()).Valor;
        }

        if (await obras.Guardar(obra, versionLeida, cancelacion).ConfigureAwait(false) != ResultadoDeEscrituraDeObra.Escrita)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.ConflictoDeVersion());
        }

        if (anterior is not null)
        {
            await archivos.EncolarDescarte(anterior.ClaveDelAlmacen, FinalidadDeArchivo.Portada, usuarioId, reloj.Ahora, cancelacion).ConfigureAwait(false);
            await auditoria.Registrar(AuditoriaDelCatalogo.PortadaReemplazada(obra, anterior, portadaNueva!.TamanoEnBytes!.Value), cancelacion).ConfigureAwait(false);
        }

        // Cada edición de la obra deja su entrada: también cuando solo cambia la portada se audita el reemplazo, y si no cambió ningún dato ni la portada, la operación igual consta.
        if (datos.Valor.CamposCambiados.Count > 0 || anterior is null)
        {
            await auditoria.Registrar(AuditoriaDelCatalogo.ObraEditada(obra, datos.Valor.CamposCambiados), cancelacion).ConfigureAwait(false);
        }

        return Resultado<ResultadoDeEscritura>.Exito(new ResultadoDeEscritura(obra.Id, obra.Version));
    }
}

/// <summary>
/// <c>EditarEdicion</c> (CAT-11, CAT-19, AC-CAT-08, AC-CAT-12): cambia el precio, la licencia o el libro de una edición con la concurrencia optimista de la versión de la obra. El cambio de precio se
/// audita con ambos valores, el de licencia con sus valores anterior y nuevo, y el reemplazo del libro con su tipo, su tamaño y las huellas cortas; el libro reemplazado pasa a <c>descarte</c>. La
/// licencia nueva se valida con la regla de vigencia (solo al cambiarla: editar el precio de una digital vencida no falla) y el archivo nuevo se consume con el formato de la licencia resultante.
/// </summary>
internal sealed class EditarEdicion(
    AutorizadorDelCatalogo autorizador,
    IValidador<EditarEdicionEntrada> validador,
    ConsumirReferenciaDeArchivo referencias,
    ConstructorDeEdiciones constructor,
    IRepositorioDeObras obras,
    IRepositorioDeReferenciasDeArchivo archivos,
    IAuditoria auditoria,
    IReloj reloj) : ICasoDeUso<EditarEdicionEntrada, ResultadoDeEscritura>
{
    public async Task<Resultado<ResultadoDeEscritura>> Ejecutar(EditarEdicionEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDelCatalogo.Validacion<ResultadoDeEscritura>(validacion);
        }

        var obra = await obras.Obtener(entrada.ObraId, paraActualizar: true, cancelacion).ConfigureAwait(false);
        if (obra is null)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.ObraNoEncontrada());
        }

        if (!obra.CoincideLaVersion(entrada.Version))
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.ConflictoDeVersion());
        }

        var edicion = obra.BuscarEdicion(entrada.EdicionId);
        if (edicion is null || edicion.EstaDadaDeBaja || obra.EstaDadaDeBaja)
        {
            var motivo = obra.EstaDadaDeBaja ? RechazoDeCatalogo.ObraDadaDeBaja : edicion is null ? RechazoDeCatalogo.EdicionNoEncontrada : RechazoDeCatalogo.EdicionDadaDeBaja;
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.De(new RechazoDeObra(motivo)));
        }

        if (!edicion.EsDigital && (entrada.Licencia is not null || entrada.ArchivoRef is not null))
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.De(new RechazoDeObra(RechazoDeCatalogo.SoloEdicionDigital)));
        }

        var usuarioId = autorizador.UsuarioId!.Value;
        LicenciaDigital? licenciaNueva = null;
        if (entrada.Licencia is not null)
        {
            var licencia = constructor.Licencia(entrada.Licencia);
            if (!licencia.EsExito)
            {
                return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.LicenciaInvalida(licencia.Errores, "licencia."));
            }

            licenciaNueva = licencia.Licencia;
        }

        ArchivoDeEdicion? archivoNuevo = null;
        ReferenciaDeArchivo? referenciaDelArchivo = null;
        if (entrada.ArchivoRef is not null)
        {
            var formato = (licenciaNueva ?? edicion.Licencia)!.Formato;
            var consumida = await referencias.Consumir(entrada.ArchivoRef, usuarioId, FinalidadDeArchivo.Libro, formato, "archivoRef", cancelacion).ConfigureAwait(false);
            if (!consumida.EsExito)
            {
                return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(consumida.Error!);
            }

            referenciaDelArchivo = consumida.Valor;
            archivoNuevo = referenciaDelArchivo.ComoArchivoDeEdicion();
        }

        var versionLeida = obra.Version;
        var cambio = new CambioDeEdicion(entrada.Precio is { } precio ? Precio.Crear(precio) : null, licenciaNueva, archivoNuevo);
        var edicionEditada = obra.EditarEdicion(entrada.EdicionId, cambio);
        if (!edicionEditada.EsExito)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.De(edicionEditada.Rechazo!));
        }

        if (await obras.Guardar(obra, versionLeida, cancelacion).ConfigureAwait(false) != ResultadoDeEscrituraDeObra.Escrita)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.ConflictoDeVersion());
        }

        var cambios = edicionEditada.Valor;
        var campos = new List<string>();
        if (cambios.CambioElPrecio)
        {
            campos.Add("precio");
            await auditoria.Registrar(AuditoriaDelCatalogo.PrecioCambiado(cambios.Edicion, cambios.PrecioAnterior!), cancelacion).ConfigureAwait(false);
        }

        if (cambios.CambioLaLicencia)
        {
            campos.Add("licencia");
            await auditoria.Registrar(AuditoriaDelCatalogo.LicenciaCambiada(cambios.Edicion, cambios.LicenciaAnterior!), cancelacion).ConfigureAwait(false);
        }

        if (cambios.ReemplazoElArchivo)
        {
            campos.Add("archivo");
            await archivos.EncolarDescarte(cambios.ArchivoAnterior!.ClaveDelAlmacen, FinalidadDeArchivo.Libro, usuarioId, reloj.Ahora, cancelacion).ConfigureAwait(false);
            await auditoria.Registrar(AuditoriaDelCatalogo.ArchivoReemplazado(cambios.Edicion, cambios.ArchivoAnterior), cancelacion).ConfigureAwait(false);
        }

        await auditoria.Registrar(AuditoriaDelCatalogo.EdicionEditada(cambios.Edicion, campos), cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeEscritura>.Exito(new ResultadoDeEscritura(obra.Id, obra.Version));
    }
}
