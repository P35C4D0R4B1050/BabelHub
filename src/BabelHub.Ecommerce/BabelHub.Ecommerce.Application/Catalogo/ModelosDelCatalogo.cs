namespace BabelHub.Ecommerce.Application.Catalogo;

// Modelos propios del contrato público del módulo Catálogo (regla 13): la web, Carrito, Pedidos, Biblioteca y el Asistente los usan sin tocar el Dominio ni el Interno del
// módulo, y por eso ningún tipo de aquí expone un tipo del Dominio, de la Infraestructura ni de Shared (lo exige una prueba de arquitectura). Los DTO públicos excluyen todo
// campo interno (titular, territorios, vigencia, claves y huellas del almacén, versión, motivo de retiro, estados internos): los de la administración son otros tipos y
// llevan «DeAdministracion» en el nombre.

/// <summary>El formato de una edición: impresa o digital (CAT-02). No cambia nunca.</summary>
public enum FormatoDeEdicion
{
    Impresa,
    Digital,
}

/// <summary>El formato del archivo de una edición digital (CAT-05).</summary>
public enum FormatoDeArchivoDigital
{
    Epub,
    Pdf,
}

public enum TipoDeLicenciaDeEdicion
{
    Comercial,
    DominioPublico,
}

/// <summary>El estado persistente de una edición (D12). Una licencia vencida o sin Perú no es un estado: se deriva y aparece como <see cref="MotivoDeNoDisponibleDeEdicion"/>.</summary>
public enum EstadoDeEdicionConsultado
{
    Activa,
    RetiradaDeVenta,
    DadaDeBaja,
}

/// <summary>Por qué una edición no se ofrece a la venta (CAT-07, D12): Carrito y Biblioteca lo distinguen.</summary>
public enum MotivoDeNoDisponibleDeEdicion
{
    LicenciaVencida,
    SinPeru,
    RetiradaDeLaVenta,
    DadaDeBaja,
}

/// <summary>Cómo se muestra la disponibilidad de una edición (CAT-10, CAT-12, CAT-21).</summary>
public enum EstadoDeDisponibilidad
{
    /// <summary>Una impresa con stock vendible en la red.</summary>
    Disponible,

    /// <summary>Una impresa sin stock vendible en la red, o que el Inventario aún no conoce (CAT-21): sigue a la venta.</summary>
    Agotada,

    /// <summary>El Inventario no responde y no hay un dato de menos de 10 minutos: «no se puede confirmar» (CAT-12).</summary>
    NoSePuedeConfirmar,

    /// <summary>Una digital vigente: «disponible al instante», sin consultar al Inventario (regla 12).</summary>
    DisponibleAlInstante,

    /// <summary>Una digital que no se ofrece (licencia vencida, sin Perú, retirada o dada de baja); el motivo viaja aparte.</summary>
    NoSeOfrece,
}

/// <summary>De cuándo es el dato de disponibilidad (CAT-12): reciente (de esta solicitud o de menos de 30 segundos), puede estar desactualizado (último dato conocido de menos de 10 minutos) o no se puede confirmar.</summary>
public enum FrescuraDeLaDisponibilidad
{
    Reciente,
    PuedeEstarDesactualizada,
    NoSePuedeConfirmar,
}

/// <summary>El stock vendible de una edición en una fuente; el físico y el reservado no salen del Inventario.</summary>
public sealed record DisponibilidadPorFuente(string CodigoDeFuente, string NombreDeFuente, int Vendible);

/// <summary>
/// La disponibilidad de una edición. <see cref="VendibleEnLaRed"/> y <see cref="PorFuente"/> solo existen para una impresa con dato. El aviso «registro o ingreso pendiente» (el
/// Inventario aún no conoce la edición) <b>no está aquí</b>: lo ve solo la administración central y por eso vive en los modelos de administración; el público ve «agotada».
/// </summary>
public sealed record DisponibilidadDeEdicion(
    Guid EdicionId,
    EstadoDeDisponibilidad Estado,
    int? VendibleEnLaRed,
    IReadOnlyList<DisponibilidadPorFuente> PorFuente,
    FrescuraDeLaDisponibilidad Frescura,
    DateTimeOffset? ConsultadaEn,
    MotivoDeNoDisponibleDeEdicion? Motivo);

// ---------- Consulta pública ----------

/// <summary>
/// Los filtros de la búsqueda (CAT-09): título (<see cref="Texto"/>, de 2 a 100 caracteres), autor, editorial, categoría, formato, rango de precio y disponibilidad. Una obra
/// cumple si <b>alguna edición</b> cumple a la vez formato, precio y disponibilidad. El orden es fijo (título); el tamaño de página tiene tope 50.
/// </summary>
public sealed record BusquedaDeObras(
    string? Texto = null,
    string? Autor = null,
    string? Editorial = null,
    string? Categoria = null,
    FormatoDeEdicion? Formato = null,
    decimal? PrecioMinimo = null,
    decimal? PrecioMaximo = null,
    bool SoloDisponibles = false,
    int Pagina = 1,
    int Tamano = 20);

/// <summary>Una edición como la ve un cliente: su ISBN-13, su precio (el de una digital se muestra sin IGV, referencial) y su disponibilidad.</summary>
public sealed record EdicionDeObra(Guid EdicionId, FormatoDeEdicion Formato, string Isbn, decimal Precio, DisponibilidadDeEdicion Disponibilidad);

/// <summary>Una obra con sus ediciones no dadas de baja. <see cref="PortadaHuella"/> son los 64 hexadecimales de la ruta <c>/portadas/{huella}</c>.</summary>
public sealed record ResumenDeObra(
    Guid ObraId,
    string Titulo,
    string Autor,
    string Editorial,
    string Categoria,
    string PortadaHuella,
    IReadOnlyList<EdicionDeObra> Ediciones);

/// <summary>Una página de la búsqueda. <see cref="Total"/> es el de las obras que cumplen los filtros.</summary>
public sealed record PaginaDeObras(IReadOnlyList<ResumenDeObra> Obras, int Pagina, int Tamano, int Total);

/// <summary>
/// Una edición por su identificador, para pedidos, carritos y traslados en curso (CAT-13, CAT-18): incluye las de baja y retiradas, con su título, su ISBN y el motivo por el
/// que no se ofrece. Es lo único que otros módulos leen de una edición.
/// </summary>
public sealed record EdicionConsultada(
    Guid EdicionId,
    Guid ObraId,
    string Titulo,
    FormatoDeEdicion Formato,
    string Isbn,
    decimal Precio,
    EstadoDeEdicionConsultado Estado,
    MotivoDeNoDisponibleDeEdicion? MotivoDeNoDisponible);

// ---------- Referencia del archivo de una digital (solo Biblioteca) ----------

/// <summary>El archivo de una edición digital: clave en el almacén, formato, huella SHA-256 (64 hexadecimales) y tamaño. Solo lo lee <c>Biblioteca</c> (D13, D17); nunca llega a la web.</summary>
public sealed record ReferenciaDeArchivoDigital(Guid EdicionId, string ClaveEnAlmacen, FormatoDeArchivoDigital Formato, string HuellaSha256, long TamanoEnBytes)
{
    /// <summary>La clave del almacén nunca se imprime (un registro de diagnóstico no debe volcarla).</summary>
    public override string ToString() => $"ReferenciaDeArchivoDigital {{ EdicionId = {EdicionId}, Formato = {Formato} }}";
}

// ---------- Administración ----------

/// <summary>La licencia de una edición digital tal como la escribe la administración (CAT-05). Los territorios son códigos de país de dos letras.</summary>
public sealed record LicenciaDeEdicionEntrada(
    TipoDeLicenciaDeEdicion Tipo,
    string? Titular,
    IReadOnlyList<string>? Territorios,
    DateOnly? VigenteHasta,
    FormatoDeArchivoDigital Formato);

/// <summary>
/// Una edición nueva: su formato, ISBN (como lo escribió la persona), precio y, si es digital, la licencia y la referencia del archivo ya subido (<see cref="ArchivoRef"/>);
/// si es impresa, el stock inicial opcional (un entero de 1 a 5 000; ausente es «sin stock inicial» y cualquier otro valor, incluido 0, se rechaza, CAT-23).
/// </summary>
public sealed record NuevaEdicionEntrada(
    FormatoDeEdicion Formato,
    string? Isbn,
    decimal Precio,
    LicenciaDeEdicionEntrada? Licencia,
    Guid? ArchivoRef,
    int? StockInicial);

/// <summary>Registra una obra con su portada (ya subida, <see cref="PortadaRef"/>) y al menos una edición (a lo más una impresa y una digital).</summary>
public sealed record RegistrarObraEntrada(string? Titulo, string? Autor, string? Editorial, string? Categoria, Guid? PortadaRef, IReadOnlyList<NuevaEdicionEntrada>? Ediciones);

/// <summary>Edita los datos de la obra y, si trae <see cref="PortadaRef"/>, reemplaza su portada; <see cref="Version"/> es la que leyó quien edita (concurrencia optimista).</summary>
public sealed record EditarObraEntrada(Guid ObraId, long Version, string? Titulo, string? Autor, string? Editorial, string? Categoria, Guid? PortadaRef);

public sealed record DarDeBajaObraEntrada(Guid ObraId, long Version);

/// <summary>Agrega una edición a una obra existente (también con su stock inicial si es impresa).</summary>
public sealed record RegistrarEdicionEntrada(Guid ObraId, long Version, NuevaEdicionEntrada Edicion);

/// <summary>Edita una edición: el precio, la licencia (solo digital) o el libro (<see cref="ArchivoRef"/>, solo digital); lo que viene nulo no cambia. El ISBN y el formato no cambian.</summary>
public sealed record EditarEdicionEntrada(Guid ObraId, Guid EdicionId, long Version, decimal? Precio, LicenciaDeEdicionEntrada? Licencia, Guid? ArchivoRef);

public sealed record DarDeBajaEdicionEntrada(Guid ObraId, Guid EdicionId, long Version);

/// <summary>Retira de la venta una edición digital (CAT-08, CAT-15) con un motivo de hasta 300 caracteres; es definitivo y no cambia los accesos ya concedidos.</summary>
public sealed record RetirarEdicionDigitalEntrada(Guid ObraId, Guid EdicionId, string? Motivo);

public enum EstadoDeIngresoSolicitado
{
    /// <summary>Se pidió y no se sabe si el Inventario lo aplicó (tiempo agotado u otra respuesta incierta): se reintenta con su misma clave o se concilia.</summary>
    Pendiente,

    Registrado,

    /// <summary>Se sabe que no se aplicó: se puede emitir una clave nueva.</summary>
    Fallido,
}

/// <summary>El ingreso de mercadería que pidió el alta o la edición (de la edición impresa) y cómo quedó; si quedó <c>Pendiente</c>, la ficha lo avisa (CAT-20).</summary>
public sealed record IngresoDeAlta(Guid IngresoSolicitadoId, Guid EdicionId, int Cantidad, EstadoDeIngresoSolicitado Estado);

/// <summary>El resultado de un alta: la obra, su versión y las ediciones creadas, con el ingreso inicial si se pidió y las ediciones impresas cuya ficha el Inventario no confirmó (<see cref="FichasSinConfirmar"/>, para el aviso «registro pendiente» de la administración). Un Inventario que no responde no revierte el alta (CAT-20): el aviso va en <see cref="Ingresos"/>.</summary>
public sealed record ResultadoDeAlta(Guid ObraId, long Version, IReadOnlyList<Guid> EdicionIds, IReadOnlyList<IngresoDeAlta> Ingresos, IReadOnlyList<Guid>? FichasSinConfirmar = null);

/// <summary>La versión nueva de la obra tras una operación que la cambia.</summary>
public sealed record ResultadoDeEscritura(Guid ObraId, long Version);

/// <summary>Una edición como la ve la administración, con lo que el público no ve (licencia, estado, motivo de retiro, ISBN como se ingresó y datos del archivo).</summary>
public sealed record EdicionDeAdministracion(
    Guid EdicionId,
    FormatoDeEdicion Formato,
    string Isbn,
    string IsbnIngresado,
    decimal Precio,
    EstadoDeEdicionConsultado Estado,
    string? MotivoDeRetiro,
    MotivoDeNoDisponibleDeEdicion? MotivoDeNoDisponible,
    LicenciaDeEdicionDeAdministracion? Licencia,
    ArchivoDeEdicionDeAdministracion? Archivo,
    bool? RegistroPendienteEnElInventario = null);

public sealed record LicenciaDeEdicionDeAdministracion(
    TipoDeLicenciaDeEdicion Tipo,
    string Titular,
    IReadOnlyList<string> Territorios,
    DateOnly? VigenteHasta,
    FormatoDeArchivoDigital Formato);

/// <summary>Los datos visibles del archivo de una digital: tipo, tamaño y huella corta (nunca la clave del almacén ni un enlace).</summary>
public sealed record ArchivoDeEdicionDeAdministracion(string Tipo, long TamanoEnBytes, string HuellaCorta, DateTimeOffset SubidoEn);

/// <summary>Una obra como la ve la administración: todas sus ediciones (también las de baja), su versión y su estado.</summary>
public sealed record ObraDeAdministracion(
    Guid ObraId,
    string Titulo,
    string Autor,
    string Editorial,
    string Categoria,
    string PortadaHuella,
    long Version,
    bool DadaDeBaja,
    IReadOnlyList<EdicionDeAdministracion> Ediciones);

public sealed record ListarObrasDeAdministracionEntrada(string? Texto = null, bool IncluirDadasDeBaja = false, int Pagina = 1, int Tamano = 20);

public sealed record PaginaDeObrasDeAdministracion(IReadOnlyList<ObraDeAdministracion> Obras, int Pagina, int Tamano, int Total);

// ---------- Archivos ----------

public enum FinalidadDeLaSubida
{
    Portada,
    Libro,
}

/// <summary>
/// Sube un archivo (CAT-16): el cuerpo se lee en streaming, nunca completo en memoria ni en disco local. El tipo se decide por los primeros bytes, el tope se cuenta con los bytes
/// realmente leídos y la huella SHA-256 se calcula durante la escritura.
/// </summary>
public sealed record SubirArchivoEntrada(FinalidadDeLaSubida Finalidad, Stream Contenido);

/// <summary>La referencia que se usa después al registrar la obra o la edición: la huella SHA-256 (64 hexadecimales), el tipo y el tamaño; nunca la clave del almacén.</summary>
public sealed record ArchivoSubido(Guid ReferenciaId, FinalidadDeLaSubida Finalidad, string Tipo, long TamanoEnBytes, string HuellaSha256, DateTimeOffset VenceEn)
{
    /// <summary>La referencia es una capacidad de un solo uso ligada al usuario: no se imprime (SEG-10).</summary>
    public override string ToString() => $"ArchivoSubido {{ Finalidad = {Finalidad}, Tipo = {Tipo}, TamanoEnBytes = {TamanoEnBytes} }}";
}

/// <summary>Una portada lista para servir: su tipo de medio y su flujo (que quien llama cierra).</summary>
public sealed record PortadaServida(string TipoDeMedio, Stream Contenido);

// ---------- Ingresos de mercadería ----------

/// <summary>La clave de idempotencia de un ingreso: un token firmado que el servidor emite al <b>mostrar</b> el formulario (sin escribir nada) y que vence a los 30 minutos.</summary>
public sealed record ClaveDeIngresoEmitida(string Token, DateTimeOffset VenceEn)
{
    /// <summary>El token firmado nunca se imprime (SEG-10): solo se guarda su nonce.</summary>
    public override string ToString() => $"ClaveDeIngresoEmitida {{ VenceEn = {VenceEn:O} }}";
}

public sealed record EmitirClaveDeIngresoEntrada(Guid EdicionId);

/// <summary>Un ingreso de mercadería de una edición impresa existente (CAT-22): cantidad entera de 1 a 5 000, referencia (hasta 80), nota (hasta 500) y el token del formulario.</summary>
public sealed record RegistrarIngresoEntrada(Guid EdicionId, int Cantidad, string? Referencia, string? Nota, string? Token)
{
    public override string ToString() => $"RegistrarIngresoEntrada {{ EdicionId = {EdicionId}, Cantidad = {Cantidad} }}";
}

/// <summary>El resultado de pedir, reintentar o conciliar un ingreso: su identificador, el estado y, si <c>Pendiente</c>, el aviso «reintentar» con su misma clave.</summary>
public sealed record ResultadoDeIngreso(Guid IngresoSolicitadoId, Guid EdicionId, EstadoDeIngresoSolicitado Estado);

/// <summary>Reintenta el ingreso pendiente. <paramref name="EdicionId"/> (la de la ruta) es opcional: si viene y la fila es de otra edición, responde «no encontrado».</summary>
public sealed record ReintentarIngresoEntrada(Guid IngresoSolicitadoId, Guid? EdicionId = null);

public enum ResolucionDeIngreso
{
    Registrado,
    Fallido,
}

/// <summary>
/// Concilia un ingreso pendiente (solo administración central): <c>Registrado</c> exige una coincidencia única en los últimos ingresos del Inventario; <c>Fallido</c> exige la
/// confirmación explícita de que habilita una clave nueva y puede duplicar el stock.
/// </summary>
public sealed record ConciliarIngresoEntrada(Guid IngresoSolicitadoId, ResolucionDeIngreso Resolucion, bool ConfirmaFallido, Guid? EdicionId = null);

public sealed record ListarIngresosEntrada(Guid EdicionId);

/// <summary>Un ingreso de la lista de una edición: el del Inventario (con su fecha y su usuario) y los solicitados aquí que aún no constan, con el aviso «ingreso pendiente de …».</summary>
public sealed record IngresoDeLaLista(
    Guid? IngresoId,
    Guid? IngresoSolicitadoId,
    int Cantidad,
    string? Referencia,
    Guid? UsuarioId,
    DateTimeOffset Instante,
    EstadoDeIngresoSolicitado? Estado)
{
    /// <summary>La referencia (texto libre) y el usuario no se imprimen (SEG-10).</summary>
    public override string ToString() => $"IngresoDeLaLista {{ Cantidad = {Cantidad}, Instante = {Instante:O}, Estado = {Estado} }}";
}

/// <summary>Los últimos ingresos de una edición con su fecha y su usuario. <see cref="InventarioNoRespondio"/> indica que la parte del Inventario falta.</summary>
public sealed record HistorialDeIngresos(Guid EdicionId, IReadOnlyList<IngresoDeLaLista> Ingresos, bool InventarioNoRespondio);

// ---------- Mantenimiento (trabajos de fondo) ----------

/// <summary>Lo que depuró una corrida: los objetos borrados, las filas quitadas y las que no se pudieron depurar (se reintentan en la corrida siguiente).</summary>
public sealed record DepuracionDeArchivos(int ObjetosBorrados, int FilasQuitadas, int Fallidas);

/// <summary>Cuántas filas anonimizó una corrida, por tabla: los ingresos solicitados, los libros de las ediciones y las subidas pendientes. Solo cantidades: nunca datos de personas.</summary>
public sealed record AnonimizacionDelCatalogoRealizada(int IngresosSolicitados, int LibrosDeEdiciones, int SubidasPendientes)
{
    public int Total => IngresosSolicitados + LibrosDeEdiciones + SubidasPendientes;
}
