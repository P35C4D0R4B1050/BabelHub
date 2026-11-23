namespace BabelHub.Ecommerce.Domain.Catalogo;

public enum EstadoDeObra
{
    Activa,

    /// <summary>Deja de mostrarse en el catálogo (la ficha pública responde 404) pero sus datos siguen legibles por identificador para pedidos y traslados en curso (CAT-13, CAT-18).</summary>
    Baja,
}

/// <summary>Por qué el Dominio rechazó una operación sobre una obra. La Aplicación lo traduce al error <c>ECO-…</c> que corresponde.</summary>
public enum RechazoDeCatalogo
{
    /// <summary>Un dato de la obra (título, autor, editorial, categoría o motivo) falta o es demasiado largo; el campo viaja en el rechazo.</summary>
    DatosInvalidos,

    /// <summary>Una obra necesita al menos una edición (CAT-04).</summary>
    ObraSinEdiciones,

    /// <summary>Una obra tiene a lo más una edición impresa y una digital no dadas de baja (D16).</summary>
    EdicionDuplicadaPorTipo,

    /// <summary>Dos ediciones de la misma obra con el mismo ISBN (también contra el de una edición dada de baja, que sigue reservado).</summary>
    IsbnRepetidoEnLaObra,

    EdicionNoEncontrada,

    /// <summary>La baja de la última edición no dada de baja dejaría la obra sin ediciones (<c>ECO-OBRA-SIN-EDICIONES</c>). Una retirada de la venta cuenta como existente.</summary>
    UltimaEdicionNoDadaDeBaja,

    ObraDadaDeBaja,

    EdicionDadaDeBaja,

    /// <summary>Solo una edición digital tiene licencia, archivo y se retira de la venta.</summary>
    SoloEdicionDigital,

    EdicionYaRetirada,

    /// <summary>El formato del archivo no corresponde al de la licencia de la edición digital.</summary>
    ArchivoNoCoincideConLaLicencia,
}

/// <summary>El rechazo de una operación: el motivo y, si aplica, el campo.</summary>
public sealed record RechazoDeObra(RechazoDeCatalogo Motivo, string? Campo = null);

/// <summary>Resultado de una operación sobre la obra: un valor o el rechazo del Dominio (sin excepciones para flujos esperados).</summary>
public sealed class ResultadoDeObra<T>
{
    private readonly T? _valor;

    private ResultadoDeObra(T? valor, RechazoDeObra? rechazo)
    {
        _valor = valor;
        Rechazo = rechazo;
    }

    public bool EsExito => Rechazo is null;

    public RechazoDeObra? Rechazo { get; }

    public T Valor => EsExito ? _valor! : throw new InvalidOperationException("El resultado es un rechazo y no tiene valor.");

    internal static ResultadoDeObra<T> Exito(T valor) => new(valor, null);

    internal static ResultadoDeObra<T> Rechazar(RechazoDeCatalogo motivo, string? campo = null) => new(default, new RechazoDeObra(motivo, campo));
}

/// <summary>Los datos de texto de una obra (CAT-01). Se recortan los espacios de los extremos y se validan largo y obligatoriedad al crear o editar la obra.</summary>
public sealed record DatosDeObra(string? Titulo, string? Autor, string? Editorial, string? Categoria, string? Descripcion = null);

/// <summary>Lo que cambió al editar los datos de una obra, para la auditoría (<c>camposCambiados</c>, sin los valores).</summary>
public sealed record CambiosDeObra(IReadOnlyList<string> CamposCambiados);

/// <summary>Lo que se quiere cambiar de una edición; lo que viene nulo no se toca. El ISBN y el tipo no cambian nunca.</summary>
public sealed record CambioDeEdicion(Precio? Precio = null, LicenciaDigital? Licencia = null, ArchivoDeEdicion? Archivo = null);

/// <summary>Lo que cambió de verdad en una edición, con los valores anteriores: el precio y la licencia para la auditoría, y el archivo reemplazado para encolar su descarte.</summary>
public sealed record CambiosDeEdicion(
    Edicion Edicion,
    Precio? PrecioAnterior,
    LicenciaDigital? LicenciaAnterior,
    ArchivoDeEdicion? ArchivoAnterior)
{
    public bool CambioElPrecio => PrecioAnterior is not null;

    public bool CambioLaLicencia => LicenciaAnterior is not null;

    public bool ReemplazoElArchivo => ArchivoAnterior is not null;

    /// <summary>La licencia anterior y el archivo (con su clave del almacén) no se imprimen: un registro de diagnóstico no debe volcarlos (SEG-10).</summary>
    private bool PrintMembers(System.Text.StringBuilder constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);
        constructor.Append("EdicionId = ").Append(Edicion.Id).Append(", CambioElPrecio = ").Append(CambioElPrecio)
            .Append(", CambioLaLicencia = ").Append(CambioLaLicencia).Append(", ReemplazoElArchivo = ").Append(ReemplazoElArchivo);
        return true;
    }
}

/// <summary>Las ediciones que quedaron dadas de baja con la obra (para quitarlas de los carritos y auditarlas).</summary>
public sealed record BajaDeObra(IReadOnlyList<Edicion> EdicionesDadasDeBaja);

/// <summary>
/// La obra, raíz del agregado del Catálogo (CAT-01 a CAT-04, CAT-13, D16): título, autor, editorial, categoría, portada y sus ediciones —a lo más una impresa y una digital de tipo inmutable—. Una sola
/// <see cref="Version"/> cubre la edición concurrente de la obra y de cualquiera de sus ediciones; <b>toda operación incrementa la versión</b>. Una obra tiene siempre al menos una edición no dada de baja
/// (una retirada de la venta cuenta): la última no se da de baja sola. La baja de la obra da de baja todas sus ediciones y conserva los datos y, en las digitales, el archivo y los accesos ya concedidos.
/// </summary>
public sealed class Obra
{
    public const int LongitudMaximaDelTexto = 200;

    public const int LongitudMaximaDeLaCategoria = 100;

    private readonly List<Edicion> _ediciones;

    private Obra(
        Guid id,
        DatosDeObra datos,
        PortadaDeObra portada,
        long version,
        EstadoDeObra estado,
        DateTimeOffset? dadaDeBajaEn,
        DateTimeOffset creadaEn,
        List<Edicion> ediciones)
    {
        Id = id;
        Titulo = datos.Titulo!;
        Autor = datos.Autor!;
        Editorial = datos.Editorial!;
        Categoria = datos.Categoria!;
        Descripcion = datos.Descripcion;
        Portada = portada;
        Version = version;
        Estado = estado;
        DadaDeBajaEn = dadaDeBajaEn;
        CreadaEn = creadaEn;
        _ediciones = ediciones;
    }

    public Guid Id { get; }

    public string Titulo { get; private set; }

    public string Autor { get; private set; }

    public string Editorial { get; private set; }

    public string Categoria { get; private set; }

    /// <summary>La descripción opcional (CAT-01): texto plano de hasta 600 puntos de código, o <see langword="null"/> si no tiene (<see cref="DescripcionDeObra"/>).</summary>
    public string? Descripcion { get; private set; }

    public PortadaDeObra Portada { get; private set; }

    /// <summary>La versión de concurrencia optimista de todo el agregado: empieza en 1 y crece con cada operación sobre la obra o sus ediciones.</summary>
    public long Version { get; private set; }

    public EstadoDeObra Estado { get; private set; }

    public DateTimeOffset? DadaDeBajaEn { get; private set; }

    public DateTimeOffset CreadaEn { get; }

    public IReadOnlyList<Edicion> Ediciones => _ediciones;

    public bool EstaDadaDeBaja => Estado == EstadoDeObra.Baja;

    /// <summary>Registra una obra con su portada y al menos una edición (a lo más una impresa y una digital, con ISBN distintos).</summary>
    public static ResultadoDeObra<Obra> Registrar(
        Guid id,
        DatosDeObra datos,
        PortadaDeObra portada,
        IEnumerable<Edicion> ediciones,
        DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(datos);
        ArgumentNullException.ThrowIfNull(portada);
        ArgumentNullException.ThrowIfNull(ediciones);
        if (id == Guid.Empty)
        {
            throw new ArgumentException("La obra necesita un identificador.", nameof(id));
        }

        var datosLimpios = Normalizar(datos, out var campoInvalido);
        if (campoInvalido is not null)
        {
            return ResultadoDeObra<Obra>.Rechazar(RechazoDeCatalogo.DatosInvalidos, campoInvalido);
        }

        var lista = ediciones.ToList();
        if (lista.Count == 0)
        {
            return ResultadoDeObra<Obra>.Rechazar(RechazoDeCatalogo.ObraSinEdiciones, "ediciones");
        }

        if (lista.Any(e => e.EstaDadaDeBaja))
        {
            return ResultadoDeObra<Obra>.Rechazar(RechazoDeCatalogo.EdicionDadaDeBaja, "ediciones");
        }

        if (lista.GroupBy(e => e.Tipo).Any(g => g.Count() > 1))
        {
            return ResultadoDeObra<Obra>.Rechazar(RechazoDeCatalogo.EdicionDuplicadaPorTipo, "ediciones");
        }

        if (lista.GroupBy(e => e.Isbn).Any(g => g.Count() > 1))
        {
            return ResultadoDeObra<Obra>.Rechazar(RechazoDeCatalogo.IsbnRepetidoEnLaObra, "isbn");
        }

        return ResultadoDeObra<Obra>.Exito(new Obra(id, datosLimpios, portada, 1, EstadoDeObra.Activa, null, ahora, lista));
    }

    /// <summary>Reconstruye una obra guardada con su versión y todas sus ediciones (también las dadas de baja).</summary>
    public static Obra Rehidratar(
        Guid id,
        DatosDeObra datos,
        PortadaDeObra portada,
        long version,
        EstadoDeObra estado,
        DateTimeOffset? dadaDeBajaEn,
        DateTimeOffset creadaEn,
        IEnumerable<Edicion> ediciones)
    {
        ArgumentNullException.ThrowIfNull(datos);
        ArgumentNullException.ThrowIfNull(portada);
        ArgumentNullException.ThrowIfNull(ediciones);
        var limpios = Normalizar(datos, out var campoInvalido);
        if (campoInvalido is not null)
        {
            throw new ArgumentException($"La obra guardada tiene un dato inválido: {campoInvalido}.", nameof(datos));
        }

        return new Obra(id, limpios, portada, version, estado, dadaDeBajaEn, creadaEn, ediciones.ToList());
    }

    public Edicion? BuscarEdicion(Guid edicionId) => _ediciones.Find(e => e.Id == edicionId);

    /// <summary>Si la versión que quien edita leyó es la actual: la segunda de dos ediciones concurrentes recibe <c>ECO-CONFLICTO</c>.</summary>
    public bool CoincideLaVersion(long versionEsperada) => Version == versionEsperada;

    public ResultadoDeObra<CambiosDeObra> EditarDatos(DatosDeObra datos)
    {
        ArgumentNullException.ThrowIfNull(datos);
        if (EstaDadaDeBaja)
        {
            return ResultadoDeObra<CambiosDeObra>.Rechazar(RechazoDeCatalogo.ObraDadaDeBaja);
        }

        var limpios = Normalizar(datos, out var campoInvalido);
        if (campoInvalido is not null)
        {
            return ResultadoDeObra<CambiosDeObra>.Rechazar(RechazoDeCatalogo.DatosInvalidos, campoInvalido);
        }

        var cambiados = new List<string>();
        if (!string.Equals(Titulo, limpios.Titulo, StringComparison.Ordinal))
        {
            cambiados.Add("titulo");
        }

        if (!string.Equals(Autor, limpios.Autor, StringComparison.Ordinal))
        {
            cambiados.Add("autor");
        }

        if (!string.Equals(Editorial, limpios.Editorial, StringComparison.Ordinal))
        {
            cambiados.Add("editorial");
        }

        if (!string.Equals(Categoria, limpios.Categoria, StringComparison.Ordinal))
        {
            cambiados.Add("categoria");
        }

        if (!string.Equals(Descripcion, limpios.Descripcion, StringComparison.Ordinal))
        {
            cambiados.Add("descripcion");
        }

        Titulo = limpios.Titulo!;
        Autor = limpios.Autor!;
        Editorial = limpios.Editorial!;
        Categoria = limpios.Categoria!;
        Descripcion = limpios.Descripcion;
        Version++;
        return ResultadoDeObra<CambiosDeObra>.Exito(new CambiosDeObra(cambiados));
    }

    /// <summary>Cambia la portada y devuelve la anterior para encolar el descarte de su objeto.</summary>
    public ResultadoDeObra<PortadaDeObra> ReemplazarPortada(PortadaDeObra nueva)
    {
        ArgumentNullException.ThrowIfNull(nueva);
        if (EstaDadaDeBaja)
        {
            return ResultadoDeObra<PortadaDeObra>.Rechazar(RechazoDeCatalogo.ObraDadaDeBaja);
        }

        var anterior = Portada;
        Portada = nueva;
        Version++;
        return ResultadoDeObra<PortadaDeObra>.Exito(anterior);
    }

    /// <summary>Agrega una edición a una obra existente: su tipo no puede repetir el de otra no dada de baja y su ISBN no puede repetir ninguno de la obra (el de una dada de baja sigue reservado).</summary>
    public ResultadoDeObra<Edicion> AgregarEdicion(Edicion edicion)
    {
        ArgumentNullException.ThrowIfNull(edicion);
        if (EstaDadaDeBaja)
        {
            return ResultadoDeObra<Edicion>.Rechazar(RechazoDeCatalogo.ObraDadaDeBaja);
        }

        if (edicion.EstaDadaDeBaja)
        {
            return ResultadoDeObra<Edicion>.Rechazar(RechazoDeCatalogo.EdicionDadaDeBaja);
        }

        if (_ediciones.Any(e => !e.EstaDadaDeBaja && e.Tipo == edicion.Tipo))
        {
            return ResultadoDeObra<Edicion>.Rechazar(RechazoDeCatalogo.EdicionDuplicadaPorTipo, "tipo");
        }

        if (_ediciones.Any(e => e.Isbn.Equals(edicion.Isbn)))
        {
            return ResultadoDeObra<Edicion>.Rechazar(RechazoDeCatalogo.IsbnRepetidoEnLaObra, "isbn");
        }

        _ediciones.Add(edicion);
        Version++;
        return ResultadoDeObra<Edicion>.Exito(edicion);
    }

    /// <summary>
    /// Cambia el precio, la licencia o el archivo de una edición no dada de baja (también de una digital retirada o vencida). La licencia y el archivo son solo de una digital y el formato del archivo
    /// debe corresponder al de la licencia resultante. Devuelve los valores anteriores de lo que cambió.
    /// </summary>
    public ResultadoDeObra<CambiosDeEdicion> EditarEdicion(Guid edicionId, CambioDeEdicion cambio)
    {
        ArgumentNullException.ThrowIfNull(cambio);
        var comprobacion = ExigirEdicionEditable<CambiosDeEdicion>(edicionId, out var edicion);
        if (comprobacion is not null)
        {
            return comprobacion;
        }

        if (!edicion!.EsDigital && (cambio.Licencia is not null || cambio.Archivo is not null))
        {
            return ResultadoDeObra<CambiosDeEdicion>.Rechazar(RechazoDeCatalogo.SoloEdicionDigital);
        }

        var licenciaResultante = cambio.Licencia ?? edicion.Licencia;
        var archivoResultante = cambio.Archivo ?? edicion.Archivo;
        if (edicion.EsDigital && licenciaResultante!.Formato != archivoResultante!.Formato)
        {
            return ResultadoDeObra<CambiosDeEdicion>.Rechazar(RechazoDeCatalogo.ArchivoNoCoincideConLaLicencia, "archivoRef");
        }

        Precio? precioAnterior = null;
        LicenciaDigital? licenciaAnterior = null;
        ArchivoDeEdicion? archivoAnterior = null;

        if (cambio.Precio is not null && !cambio.Precio.Equals(edicion.Precio))
        {
            precioAnterior = edicion.Precio;
            edicion.CambiarPrecio(cambio.Precio);
        }

        if (cambio.Licencia is not null && !cambio.Licencia.Equals(edicion.Licencia))
        {
            licenciaAnterior = edicion.Licencia;
            edicion.CambiarLicencia(cambio.Licencia);
        }

        if (cambio.Archivo is not null)
        {
            archivoAnterior = edicion.Archivo;
            edicion.ReemplazarArchivo(cambio.Archivo);
        }

        Version++;
        return ResultadoDeObra<CambiosDeEdicion>.Exito(new CambiosDeEdicion(edicion, precioAnterior, licenciaAnterior, archivoAnterior));
    }

    /// <summary>
    /// Retira una edición digital de la venta (CAT-08, CAT-15): se permite aunque sea la única de la obra (la obra conserva su edición) y es definitivo en este plan. No cambia los accesos ya concedidos
    /// ni el archivo.
    /// </summary>
    public ResultadoDeObra<Edicion> RetirarEdicionDigital(Guid edicionId, string? motivo, DateTimeOffset ahora)
    {
        var comprobacion = ExigirEdicionEditable<Edicion>(edicionId, out var edicion);
        if (comprobacion is not null)
        {
            return comprobacion;
        }

        if (!edicion!.EsDigital)
        {
            return ResultadoDeObra<Edicion>.Rechazar(RechazoDeCatalogo.SoloEdicionDigital);
        }

        if (edicion.Estado == EstadoDeEdicion.RetiradaDeVenta)
        {
            return ResultadoDeObra<Edicion>.Rechazar(RechazoDeCatalogo.EdicionYaRetirada);
        }

        var limpio = motivo?.Trim();
        if (string.IsNullOrEmpty(limpio) || limpio.Length > Edicion.LongitudMaximaDelMotivoDeRetiro)
        {
            return ResultadoDeObra<Edicion>.Rechazar(RechazoDeCatalogo.DatosInvalidos, "motivo");
        }

        edicion.Retirar(limpio, ahora);
        Version++;
        return ResultadoDeObra<Edicion>.Exito(edicion);
    }

    /// <summary>Da de baja una edición. La última edición no dada de baja de la obra no se da de baja sola (se da de baja la obra); la licencia y el archivo de una digital se conservan.</summary>
    public ResultadoDeObra<Edicion> DarDeBajaEdicion(Guid edicionId, DateTimeOffset ahora)
    {
        var comprobacion = ExigirEdicionEditable<Edicion>(edicionId, out var edicion);
        if (comprobacion is not null)
        {
            return comprobacion;
        }

        if (_ediciones.Count(e => !e.EstaDadaDeBaja) <= 1)
        {
            return ResultadoDeObra<Edicion>.Rechazar(RechazoDeCatalogo.UltimaEdicionNoDadaDeBaja);
        }

        edicion!.DarDeBaja(ahora);
        Version++;
        return ResultadoDeObra<Edicion>.Exito(edicion);
    }

    /// <summary>Da de baja la obra y todas sus ediciones aún no dadas de baja; los datos y los archivos de las digitales se conservan (CAT-13).</summary>
    public ResultadoDeObra<BajaDeObra> DarDeBaja(DateTimeOffset ahora)
    {
        if (EstaDadaDeBaja)
        {
            return ResultadoDeObra<BajaDeObra>.Rechazar(RechazoDeCatalogo.ObraDadaDeBaja);
        }

        var afectadas = _ediciones.Where(e => !e.EstaDadaDeBaja).ToList();
        foreach (var edicion in afectadas)
        {
            edicion.DarDeBaja(ahora);
        }

        Estado = EstadoDeObra.Baja;
        DadaDeBajaEn = ahora;
        Version++;
        return ResultadoDeObra<BajaDeObra>.Exito(new BajaDeObra(afectadas));
    }

    private ResultadoDeObra<T>? ExigirEdicionEditable<T>(Guid edicionId, out Edicion? edicion)
    {
        edicion = null;
        if (EstaDadaDeBaja)
        {
            return ResultadoDeObra<T>.Rechazar(RechazoDeCatalogo.ObraDadaDeBaja);
        }

        edicion = BuscarEdicion(edicionId);
        if (edicion is null)
        {
            return ResultadoDeObra<T>.Rechazar(RechazoDeCatalogo.EdicionNoEncontrada);
        }

        return edicion.EstaDadaDeBaja ? ResultadoDeObra<T>.Rechazar(RechazoDeCatalogo.EdicionDadaDeBaja) : null;
    }

    private static DatosDeObra Normalizar(DatosDeObra datos, out string? campoInvalido)
    {
        var titulo = datos.Titulo?.Trim();
        var autor = datos.Autor?.Trim();
        var editorial = datos.Editorial?.Trim();
        var categoria = datos.Categoria?.Trim();
        var descripcion = DescripcionDeObra.Normalizar(datos.Descripcion, out var motivoDeLaDescripcion);

        campoInvalido = EsInvalido(titulo, LongitudMaximaDelTexto) ? "titulo"
            : EsInvalido(autor, LongitudMaximaDelTexto) ? "autor"
            : EsInvalido(editorial, LongitudMaximaDelTexto) ? "editorial"
            : EsInvalido(categoria, LongitudMaximaDeLaCategoria) ? "categoria"
            : motivoDeLaDescripcion is not null ? "descripcion"
            : null;
        return new DatosDeObra(titulo, autor, editorial, categoria, descripcion);
    }

    private static bool EsInvalido(string? texto, int maximo) => string.IsNullOrEmpty(texto) || texto.Length > maximo;
}
