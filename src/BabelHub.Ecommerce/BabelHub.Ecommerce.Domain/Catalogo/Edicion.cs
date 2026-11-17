namespace BabelHub.Ecommerce.Domain.Catalogo;

/// <summary>El tipo de una edición (CAT-02, D16). No cambia nunca: una obra tiene a lo más una impresa y una digital.</summary>
public enum TipoDeEdicion
{
    Impresa,
    Digital,
}

/// <summary>
/// El estado persistente de una edición (D12). <c>RetiradaDeVenta</c> es solo de las digitales y definitivo en este plan; una licencia vencida o sin Perú <b>no</b> es un estado: se deriva de la
/// licencia (<see cref="Edicion.MotivoDeNoDisponiblePara"/>).
/// </summary>
public enum EstadoDeEdicion
{
    Activa,
    RetiradaDeVenta,
    Baja,
}

/// <summary>
/// Una edición de una obra (impresa o digital) con su ISBN y su precio. Una <b>digital</b> tiene licencia y archivo y nunca stock, reserva ni fuente (regla 12); una <b>impresa</b> no tiene licencia ni archivo.
/// Solo la raíz <see cref="Obra"/> la modifica: toda operación sobre una edición incrementa la versión de su obra.
/// </summary>
public sealed class Edicion
{
    public const int LongitudMaximaDelMotivoDeRetiro = 300;

    private Edicion(
        Guid id,
        TipoDeEdicion tipo,
        Isbn isbn,
        Precio precio,
        EstadoDeEdicion estado,
        string? motivoDeRetiro,
        DateTimeOffset? retiradaEn,
        DateTimeOffset? dadaDeBajaEn,
        LicenciaDigital? licencia,
        ArchivoDeEdicion? archivo)
    {
        Id = id;
        Tipo = tipo;
        Isbn = isbn;
        Precio = precio;
        Estado = estado;
        MotivoDeRetiro = motivoDeRetiro;
        RetiradaEn = retiradaEn;
        DadaDeBajaEn = dadaDeBajaEn;
        Licencia = licencia;
        Archivo = archivo;
    }

    public Guid Id { get; }

    public TipoDeEdicion Tipo { get; }

    public Isbn Isbn { get; }

    public Precio Precio { get; private set; }

    public EstadoDeEdicion Estado { get; private set; }

    /// <summary>Por qué la administración retiró la edición digital de la venta (hasta 300 caracteres); nulo si no está retirada.</summary>
    public string? MotivoDeRetiro { get; private set; }

    public DateTimeOffset? RetiradaEn { get; private set; }

    public DateTimeOffset? DadaDeBajaEn { get; private set; }

    /// <summary>La licencia: de una digital siempre; de una impresa, nunca.</summary>
    public LicenciaDigital? Licencia { get; private set; }

    /// <summary>El libro: de una digital siempre; de una impresa, nunca. Se conserva al dar de baja o retirar la edición (BIB-09).</summary>
    public ArchivoDeEdicion? Archivo { get; private set; }

    public bool EsDigital => Tipo == TipoDeEdicion.Digital;

    public bool EstaDadaDeBaja => Estado == EstadoDeEdicion.Baja;

    public static Edicion Impresa(Guid id, Isbn isbn, Precio precio)
    {
        ArgumentNullException.ThrowIfNull(isbn);
        ArgumentNullException.ThrowIfNull(precio);
        ExigirIdentificador(id);
        return new Edicion(id, TipoDeEdicion.Impresa, isbn, precio, EstadoDeEdicion.Activa, null, null, null, null, null);
    }

    public static Edicion Digital(Guid id, Isbn isbn, Precio precio, LicenciaDigital licencia, ArchivoDeEdicion archivo)
    {
        ArgumentNullException.ThrowIfNull(isbn);
        ArgumentNullException.ThrowIfNull(precio);
        ArgumentNullException.ThrowIfNull(licencia);
        ArgumentNullException.ThrowIfNull(archivo);
        ExigirIdentificador(id);
        ExigirFormatoAcorde(licencia, archivo);
        return new Edicion(id, TipoDeEdicion.Digital, isbn, precio, EstadoDeEdicion.Activa, null, null, null, licencia, archivo);
    }

    /// <summary>Reconstruye una edición guardada (con su licencia y archivo si es digital).</summary>
    public static Edicion Rehidratar(
        Guid id,
        TipoDeEdicion tipo,
        Isbn isbn,
        Precio precio,
        EstadoDeEdicion estado,
        string? motivoDeRetiro,
        DateTimeOffset? retiradaEn,
        DateTimeOffset? dadaDeBajaEn,
        LicenciaDigital? licencia,
        ArchivoDeEdicion? archivo)
    {
        ArgumentNullException.ThrowIfNull(isbn);
        ArgumentNullException.ThrowIfNull(precio);
        ExigirIdentificador(id);
        if (tipo == TipoDeEdicion.Digital && (licencia is null || archivo is null))
        {
            throw new ArgumentException("Una edición digital guardada tiene licencia y archivo.", nameof(licencia));
        }

        if (tipo == TipoDeEdicion.Impresa && (licencia is not null || archivo is not null || estado == EstadoDeEdicion.RetiradaDeVenta))
        {
            throw new ArgumentException("Una edición impresa no tiene licencia, archivo ni se retira de la venta.", nameof(tipo));
        }

        return new Edicion(id, tipo, isbn, precio, estado, motivoDeRetiro, retiradaEn, dadaDeBajaEn, licencia, archivo);
    }

    /// <summary>
    /// Por qué no se ofrece hoy, o <see langword="null"/> si se ofrece (CAT-07). La baja manda sobre el retiro y este sobre la licencia; una impresa activa siempre se ofrece
    /// (que esté agotada no la quita de la venta, CAT-21).
    /// </summary>
    public MotivoDeNoDisponible? MotivoDeNoDisponiblePara(DateOnly hoyEnLima)
    {
        if (Estado == EstadoDeEdicion.Baja)
        {
            return MotivoDeNoDisponible.DadaDeBaja;
        }

        if (Estado == EstadoDeEdicion.RetiradaDeVenta)
        {
            return MotivoDeNoDisponible.RetiradaDeLaVenta;
        }

        return Licencia?.MotivoDeNoDisponiblePara(hoyEnLima);
    }

    public bool SeOfrece(DateOnly hoyEnLima) => MotivoDeNoDisponiblePara(hoyEnLima) is null;

    internal void CambiarPrecio(Precio precio) => Precio = precio;

    internal void CambiarLicencia(LicenciaDigital licencia) => Licencia = licencia;

    internal void ReemplazarArchivo(ArchivoDeEdicion archivo) => Archivo = archivo;

    internal void Retirar(string motivo, DateTimeOffset ahora)
    {
        Estado = EstadoDeEdicion.RetiradaDeVenta;
        MotivoDeRetiro = motivo;
        RetiradaEn = ahora;
    }

    /// <summary>Marca la baja: la licencia y el archivo se conservan para los accesos ya concedidos y los pedidos históricos (CAT-08, CAT-13).</summary>
    internal void DarDeBaja(DateTimeOffset ahora)
    {
        Estado = EstadoDeEdicion.Baja;
        DadaDeBajaEn = ahora;
    }

    internal static void ExigirFormatoAcorde(LicenciaDigital licencia, ArchivoDeEdicion archivo)
    {
        if (licencia.Formato != archivo.Formato)
        {
            throw new ArgumentException("El archivo de la edición digital no corresponde al formato de su licencia.", nameof(archivo));
        }
    }

    private static void ExigirIdentificador(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("La edición necesita un identificador.", nameof(id));
        }
    }
}
