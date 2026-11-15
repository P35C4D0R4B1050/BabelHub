using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Catalogo;

/// <summary>
/// La disponibilidad de ediciones (CAT-10, CAT-12, CAT-21): una impresa con su stock vendible en la red y por sucursal y su frescura; una digital vigente como «disponible al instante» sin
/// consultar al Inventario (regla 12). Si el Inventario no responde usa el último dato de menos de 10 minutos («puede estar desactualizada») y, si es más antiguo o no hay, «no se puede
/// confirmar»; una impresa que el Inventario no conoce se muestra agotada. Hasta 50 ediciones por consulta.
/// </summary>
public interface IDisponibilidadDeEdiciones
{
    Task<Resultado<IReadOnlyList<DisponibilidadDeEdicion>>> Consultar(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default);
}

/// <summary>
/// Contrato aparte, <b>solo para <c>Biblioteca</c></b> (D13, D17; lo exige una prueba de arquitectura): el archivo de una edición digital (clave en el almacén, formato, huella y
/// tamaño), también si la edición está retirada o dada de baja, porque los accesos ya concedidos se conservan (BIB-09). No está en <see cref="IConsultaDeCatalogo"/> para que la web y el
/// Asistente nunca lo vean.
/// </summary>
public interface IReferenciaDeArchivosDelCatalogo
{
    /// <summary>La referencia del archivo; «no encontrada» si la edición no existe o no es digital.</summary>
    Task<Resultado<ReferenciaDeArchivoDigital>> ObtenerDeEdicion(Guid edicionId, CancellationToken cancelacion = default);
}

/// <summary>
/// Lo que <c>Catálogo</c> declara y <c>Carrito</c> implementa (D07, CAT-18): quitar una edición de los carritos cuando se da de baja. <c>Carrito</c> quita la línea de cada carrito y deja
/// un aviso persistente («X fue retirada del catálogo») hasta que el cliente lo marque como visto; no usa el Notificador (regla 10). Se llama dentro de la misma transacción de la baja,
/// y si falla la baja no se aplica. Mientras <c>Carrito</c> no exista, la composición usa una implementación nula con una prueba que falla cuando aparezca la real.
/// </summary>
public interface IRetiroDeEdicionDeCarritos
{
    Task Retirar(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default);
}

/// <summary>
/// La administración del catálogo (CAT-01 a CAT-05, CAT-11, CAT-13 a CAT-15, CAT-19, CAT-23): solo la administración central, comprobado en cada operación (el intento de otro rol se rechaza y
/// se registra por <c>IDenegacionDeAcceso</c>). Cada una valida su entrada al inicio, usa la versión de la obra para la concurrencia optimista y audita en la misma transacción.
/// </summary>
public interface IAdministracionDelCatalogo
{
    /// <summary>Alta de una obra con portada y al menos una edición. Con un <c>stockInicial</c> en la impresa pide el ingreso al Inventario tras confirmar; si no responde, el alta no se revierte (CAT-20).</summary>
    Task<Resultado<ResultadoDeAlta>> RegistrarObra(RegistrarObraEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<ResultadoDeEscritura>> EditarObra(EditarObraEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<ResultadoDeEscritura>> DarDeBajaObra(DarDeBajaObraEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<ResultadoDeAlta>> RegistrarEdicion(RegistrarEdicionEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<ResultadoDeEscritura>> EditarEdicion(EditarEdicionEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<ResultadoDeEscritura>> DarDeBajaEdicion(DarDeBajaEdicionEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<ResultadoDeEscritura>> RetirarEdicionDigital(RetirarEdicionDigitalEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<PaginaDeObrasDeAdministracion>> ListarObras(ListarObrasDeAdministracionEntrada entrada, CancellationToken cancelacion = default);

    /// <summary>Una obra con todas sus ediciones, también las dadas de baja; el identificador inexistente es «no encontrada».</summary>
    Task<Resultado<ObraDeAdministracion>> ObtenerObra(Guid obraId, CancellationToken cancelacion = default);
}

/// <summary>Los archivos del catálogo (CAT-16): subir un libro o una portada y obtener una portada para servirla en <c>/portadas/{huella}</c>.</summary>
public interface IArchivosDelCatalogo
{
    /// <summary>Solo la administración central, comprobado <b>antes</b> de leer el cuerpo.</summary>
    Task<Resultado<ArchivoSubido>> SubirArchivo(SubirArchivoEntrada entrada, CancellationToken cancelacion = default);

    /// <summary>La portada de una obra por su huella (exactamente 64 hexadecimales); solo se busca entre las portadas de obras, nunca entre las huellas de libros. También la de una obra dada de baja.</summary>
    Task<Resultado<PortadaServida>> ObtenerPortada(string huella, CancellationToken cancelacion = default);
}

/// <summary>Los ingresos de mercadería de las ediciones impresas desde el catálogo (CAT-20, CAT-22, CAT-23): solo la administración central.</summary>
public interface IIngresosDeMercaderia
{
    /// <summary>Emite la clave de idempotencia (un token firmado) al mostrar el formulario, sin escribir nada.</summary>
    Task<Resultado<ClaveDeIngresoEmitida>> EmitirClave(EmitirClaveDeIngresoEntrada entrada, CancellationToken cancelacion = default);

    /// <summary>Pide el ingreso al Inventario con su clave; un resultado incierto deja el ingreso <c>Pendiente</c> (éxito con aviso, no un fallo).</summary>
    Task<Resultado<ResultadoDeIngreso>> Registrar(RegistrarIngresoEntrada entrada, CancellationToken cancelacion = default);

    /// <summary>Reintenta un ingreso pendiente con su misma clave y carga; solo lo puede reintentar el usuario que lo pidió.</summary>
    Task<Resultado<ResultadoDeIngreso>> Reintentar(ReintentarIngresoEntrada entrada, CancellationToken cancelacion = default);

    /// <summary>Concilia un ingreso pendiente con los últimos ingresos del Inventario; solo administración central.</summary>
    Task<Resultado<ResultadoDeIngreso>> Conciliar(ConciliarIngresoEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<HistorialDeIngresos>> Listar(ListarIngresosEntrada entrada, CancellationToken cancelacion = default);
}
