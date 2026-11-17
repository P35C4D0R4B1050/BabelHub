using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

/// <summary>Traduce los rechazos del Dominio y los de los repositorios a los errores de la Aplicación (la Api los traduce a los <c>ECO-…</c> del plan §4).</summary>
internal static class TraductorDeRechazos
{
    public static ErrorDeAplicacion De(RechazoDeObra rechazo, string? tituloDeLaObra = null)
    {
        ArgumentNullException.ThrowIfNull(rechazo);
        return rechazo.Motivo switch
        {
            RechazoDeCatalogo.DatosInvalidos => new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDelCatalogo.ValidacionFallida, [new ErrorDeCampo(rechazo.Campo ?? "obra", ConstructorDeValidacion.FormatoInvalido)]),
            RechazoDeCatalogo.ObraSinEdiciones => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCatalogo.ObraSinEdiciones),
            RechazoDeCatalogo.UltimaEdicionNoDadaDeBaja => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCatalogo.ObraSinEdiciones),
            RechazoDeCatalogo.EdicionDuplicadaPorTipo => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCatalogo.EdicionDuplicadaPorTipo),
            RechazoDeCatalogo.IsbnRepetidoEnLaObra => IsbnDuplicado(tituloDeLaObra),
            RechazoDeCatalogo.EdicionNoEncontrada => new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, ClavesDelCatalogo.EdicionNoEncontrada),
            RechazoDeCatalogo.ObraDadaDeBaja => new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDelCatalogo.ObraDadaDeBaja),
            RechazoDeCatalogo.EdicionDadaDeBaja => new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDelCatalogo.EdicionDadaDeBaja),
            RechazoDeCatalogo.SoloEdicionDigital => new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDelCatalogo.ValidacionFallida, [new ErrorDeCampo("edicionId", ClavesDelCatalogo.SoloEdicionDigital)]),
            RechazoDeCatalogo.EdicionYaRetirada => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCatalogo.EdicionYaRetirada),
            RechazoDeCatalogo.ArchivoNoCoincideConLaLicencia => new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDelCatalogo.LicenciaInvalida, [new ErrorDeCampo("archivoRef", "formato-no-coincide")]),
            _ => new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDelCatalogo.ReglaDeNegocio),
        };
    }

    /// <summary>El ISBN ya lo usa otra edición (409). El título que lo usa viaja en <c>tituloExistente</c> (el título de un catálogo público no es sensible) y solo se lee tras comprobar el rol.</summary>
    public static ErrorDeAplicacion IsbnDuplicado(string? tituloExistente)
    {
        var errores = new List<ErrorDeCampo> { new("isbn", "duplicado") };
        if (!string.IsNullOrEmpty(tituloExistente))
        {
            errores.Add(new ErrorDeCampo("tituloExistente", tituloExistente));
        }

        return new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCatalogo.IsbnDuplicado, errores);
    }

    public static ErrorDeAplicacion ConflictoDeVersion() => new(CategoriaDeError.Conflicto, ClavesDelCatalogo.ConflictoDeVersion);

    public static ErrorDeAplicacion ObraNoEncontrada() => new(CategoriaDeError.NoEncontrado, ClavesDelCatalogo.ObraNoEncontrada);

    public static ErrorDeAplicacion LicenciaInvalida(IEnumerable<ErrorDeLicencia> errores, string prefijo) =>
        new(CategoriaDeError.ReglaDeNegocio, ClavesDelCatalogo.LicenciaInvalida, errores.Select(e => new ErrorDeCampo(prefijo + e.Campo, e.Clave)).ToList());
}

/// <summary>
/// Consume una referencia de archivo (D05, <c>ConsumirReferenciaDeArchivo</c>) <b>en la misma transacción</b> que la edición y la auditoría: la toma con bloqueo, comprueba que sea del mismo usuario,
/// de la finalidad que corresponde, de un tipo acorde al formato de la licencia, no vencida según <see cref="IReloj"/> y de un solo uso, y la marca consumida. La ajena, vencida, usada, de otra
/// finalidad, inexistente o que aún se sube responde <b>lo mismo</b> (<c>ECO-REFERENCIA-DE-ARCHIVO-INVALIDA</c>): quien llama no distingue la causa.
/// </summary>
internal sealed class ConsumirReferenciaDeArchivo(IRepositorioDeReferenciasDeArchivo referencias, IReloj reloj)
{
    public async Task<Resultado<ReferenciaDeArchivo>> Consumir(Guid? referenciaId, Guid usuarioId, FinalidadDeArchivo finalidad, FormatoDigital? formato, string campo, CancellationToken cancelacion)
    {
        var invalida = Resultado<ReferenciaDeArchivo>.Fallo(
            new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDelCatalogo.ReferenciaDeArchivoInvalida, [new ErrorDeCampo(campo, "referencia-invalida")]));

        if (referenciaId is not { } id || id == Guid.Empty)
        {
            return invalida;
        }

        var referencia = await referencias.Obtener(id, paraActualizar: true, cancelacion).ConfigureAwait(false);
        if (referencia is null || referencia.Consumir(usuarioId, finalidad, formato, reloj.Ahora) != ResultadoDeConsumo.Consumida)
        {
            return invalida;
        }

        await referencias.Guardar(referencia, cancelacion).ConfigureAwait(false);
        return Resultado<ReferenciaDeArchivo>.Exito(referencia);
    }
}

/// <summary>Una edición ya construida y, si es impresa con stock inicial, la cantidad que se pedirá al Inventario después de confirmar.</summary>
internal sealed record EdicionConstruida(Edicion Edicion, int? StockInicial);

/// <summary>
/// Construye una <see cref="Edicion"/> a partir de la entrada (CAT-02, CAT-05, CAT-06): el ISBN y el precio ya validados, la licencia con la regla de vigencia posterior a hoy en la hora de Lima
/// y, en una digital, el archivo de su referencia consumida <b>con el formato de la licencia</b> (así <c>Edicion.Digital</c> nunca recibe un archivo que no corresponde).
/// </summary>
internal sealed class ConstructorDeEdiciones(ConsumirReferenciaDeArchivo referencias, IReloj reloj)
{
    public async Task<Resultado<EdicionConstruida>> Construir(NuevaEdicionEntrada entrada, string prefijo, Guid usuarioId, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var isbn = Isbn.Crear(entrada.Isbn);
        var precio = Precio.Crear(entrada.Precio);

        if (entrada.Formato == FormatoDeEdicion.Impresa)
        {
            return Resultado<EdicionConstruida>.Exito(new EdicionConstruida(Edicion.Impresa(Guid.NewGuid(), isbn, precio), entrada.StockInicial));
        }

        var licencia = Licencia(entrada.Licencia!, prefijo);
        if (!licencia.EsExito)
        {
            return Resultado<EdicionConstruida>.Fallo(TraductorDeRechazos.LicenciaInvalida(licencia.Errores, prefijo + "licencia."));
        }

        var archivo = await referencias.Consumir(entrada.ArchivoRef, usuarioId, FinalidadDeArchivo.Libro, licencia.Licencia!.Formato, prefijo + "archivoRef", cancelacion).ConfigureAwait(false);
        if (!archivo.EsExito)
        {
            return Resultado<EdicionConstruida>.Fallo(archivo.Error!);
        }

        return Resultado<EdicionConstruida>.Exito(
            new EdicionConstruida(Edicion.Digital(Guid.NewGuid(), isbn, precio, licencia.Licencia, archivo.Valor.ComoArchivoDeEdicion()), null));
    }

    /// <summary>La licencia de la entrada, validada por el Dominio con el día de Lima (solo al registrar o cambiar).</summary>
    public ResultadoDeLicencia Licencia(LicenciaDeEdicionEntrada entrada, string prefijo)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        _ = prefijo;
        return LicenciaDigital.Crear(
            entrada.Tipo == TipoDeLicenciaDeEdicion.Comercial ? TipoDeLicencia.Comercial : TipoDeLicencia.DominioPublico,
            entrada.Titular,
            entrada.Territorios,
            entrada.VigenteHasta,
            entrada.Formato == FormatoDeArchivoDigital.Epub ? FormatoDigital.Epub : FormatoDigital.Pdf,
            reloj.HoyEnLima);
    }
}

/// <summary>Las solicitudes de auditoría del módulo (CAT-11, CAT-15, CAT-19): el objeto es la obra o la edición (identificadores opacos) y el detalle solo lleva las claves que declara la acción, sin textos libres ni claves del almacén.</summary>
internal static class AuditoriaDelCatalogo
{
    public static SolicitudDeRegistro ObraRegistrada(Obra obra) =>
        new(AccionesAuditables.ObraRegistrada) { ObjetoTipo = "obra", ObjetoId = Id(obra.Id), Detalle = new Dictionary<string, string?> { ["titulo"] = obra.Titulo } };

    public static SolicitudDeRegistro ObraEditada(Obra obra, IReadOnlyList<string> campos) =>
        new(AccionesAuditables.ObraEditada)
        {
            ObjetoTipo = "obra",
            ObjetoId = Id(obra.Id),
            Detalle = new Dictionary<string, string?> { ["titulo"] = obra.Titulo, ["camposCambiados"] = string.Join(',', campos) },
        };

    public static SolicitudDeRegistro ObraDadaDeBaja(Obra obra) =>
        new(AccionesAuditables.ObraDadaDeBaja) { ObjetoTipo = "obra", ObjetoId = Id(obra.Id), Detalle = new Dictionary<string, string?> { ["titulo"] = obra.Titulo } };

    public static SolicitudDeRegistro EdicionRegistrada(Edicion edicion) =>
        new(AccionesAuditables.EdicionRegistrada)
        {
            ObjetoTipo = "edicion",
            ObjetoId = Id(edicion.Id),
            Detalle = new Dictionary<string, string?> { ["isbn"] = edicion.Isbn.Isbn13, ["formato"] = edicion.EsDigital ? "digital" : "impresa" },
        };

    public static SolicitudDeRegistro EdicionEditada(Edicion edicion, IReadOnlyList<string> campos) =>
        new(AccionesAuditables.EdicionEditada)
        {
            ObjetoTipo = "edicion",
            ObjetoId = Id(edicion.Id),
            Detalle = new Dictionary<string, string?> { ["isbn"] = edicion.Isbn.Isbn13, ["camposCambiados"] = string.Join(',', campos) },
        };

    public static SolicitudDeRegistro EdicionDadaDeBaja(Edicion edicion) =>
        new(AccionesAuditables.EdicionDadaDeBaja) { ObjetoTipo = "edicion", ObjetoId = Id(edicion.Id), Detalle = new Dictionary<string, string?> { ["isbn"] = edicion.Isbn.Isbn13 } };

    public static SolicitudDeRegistro EdicionDigitalRetirada(Edicion edicion) =>
        new(AccionesAuditables.EdicionDigitalRetirada) { ObjetoTipo = "edicion", ObjetoId = Id(edicion.Id), Detalle = new Dictionary<string, string?> { ["isbn"] = edicion.Isbn.Isbn13 } };

    /// <summary>El cambio de precio con ambos valores (CAT-11, AC-CAT-08).</summary>
    public static SolicitudDeRegistro PrecioCambiado(Edicion edicion, Precio anterior) =>
        new(AccionesAuditables.PrecioDeEdicionCambiado)
        {
            ObjetoTipo = "edicion",
            ObjetoId = Id(edicion.Id),
            Detalle = new Dictionary<string, string?> { ["precioAnterior"] = anterior.ATexto(), ["precioNuevo"] = edicion.Precio.ATexto(), ["moneda"] = "PEN" },
        };

    public static SolicitudDeRegistro LicenciaCambiada(Edicion edicion, LicenciaDigital anterior)
    {
        var nueva = edicion.Licencia!;
        return new SolicitudDeRegistro(AccionesAuditables.LicenciaCambiada)
        {
            ObjetoTipo = "edicion",
            ObjetoId = Id(edicion.Id),
            Detalle = new Dictionary<string, string?>
            {
                ["tipoAnterior"] = Tipo(anterior.Tipo),
                ["tipoNuevo"] = Tipo(nueva.Tipo),
                ["vigenciaAnterior"] = anterior.VigenteHasta?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ["vigenciaNueva"] = nueva.VigenteHasta?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ["territoriosAnterior"] = Recortar(string.Join(',', anterior.Territorios)),
                ["territoriosNuevo"] = Recortar(string.Join(',', nueva.Territorios)),
            },
        };
    }

    /// <summary>El reemplazo del libro con su tipo, su tamaño y las huellas cortas (nunca la clave del almacén ni un enlace).</summary>
    public static SolicitudDeRegistro ArchivoReemplazado(Edicion edicion, ArchivoDeEdicion anterior) =>
        new(AccionesAuditables.ArchivoDeEdicionReemplazado)
        {
            ObjetoTipo = "edicion",
            ObjetoId = Id(edicion.Id),
            Detalle = new Dictionary<string, string?>
            {
                ["tipo"] = TiposDeArchivo.Nombre(edicion.Archivo!.TipoDetectado),
                ["tamanoEnBytes"] = edicion.Archivo.TamanoEnBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["huellaCorta"] = edicion.Archivo.Huella.Corta,
                ["huellaCortaAnterior"] = anterior.Huella.Corta,
            },
        };

    /// <summary>El reemplazo de la portada de la obra (el objeto de la auditoría es la obra).</summary>
    public static SolicitudDeRegistro PortadaReemplazada(Obra obra, PortadaDeObra anterior, long tamanoEnBytes) =>
        new(AccionesAuditables.PortadaDeEdicionReemplazada)
        {
            ObjetoTipo = "obra",
            ObjetoId = Id(obra.Id),
            Detalle = new Dictionary<string, string?>
            {
                ["tipo"] = TiposDeArchivo.Nombre(obra.Portada.TipoDetectado),
                ["tamanoEnBytes"] = tamanoEnBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["huellaCorta"] = obra.Portada.Huella.Corta,
                ["huellaCortaAnterior"] = anterior.Huella.Corta,
            },
        };

    private static string Id(Guid id) => id.ToString("D");

    /// <summary>Las claves del detalle admiten hasta 64 caracteres: una lista larga de territorios se recorta (el valor completo está en la licencia).</summary>
    private static string Recortar(string valor) => valor.Length <= 64 ? valor : valor[..64];

    private static string Tipo(TipoDeLicencia tipo) => tipo == TipoDeLicencia.Comercial ? "comercial" : "dominio-publico";
}
