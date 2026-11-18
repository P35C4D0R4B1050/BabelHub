using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

/// <summary>
/// <c>SubirArchivo</c> (CAT-16, D05, D14): <b>no es transaccional</b>. Solo la administración central, comprobado <b>antes de leer el cuerpo</b> (el token antifalsificación lo valida un filtro de la Api). Dos transacciones cortas con el
/// streaming entre ambas, sin una conexión abierta durante la escritura: (1) la fila <c>subiendo</c> nace <b>antes</b> de escribir el objeto bajo una clave aleatoria; (2) tras escribir, verificar y calcular la huella durante la
/// escritura, la fila pasa a <c>lista</c> con su tipo, su tamaño real y su huella, ligada al usuario y con vencimiento a las 24 horas. Si el almacén no responde la subida se rechaza (503) y la fila queda para el trabajo de fondo; si el
/// archivo se rechaza (tipo, tope, dimensiones o estructura) se borra el objeto y la fila. Nada pasa por el disco local ni por la memoria completa.
/// </summary>
internal sealed class SubirArchivo(
    AutorizadorDelCatalogo autorizador,
    IValidador<SubirArchivoEntrada> validador,
    IRepositorioDeReferenciasDeArchivo referencias,
    IAlmacenDeArchivos almacen,
    IUnidadDeTrabajo unidad,
    IReloj reloj,
    LimitadorDeSubidas limitador,
    IPublicadorDeEventosDeSeguridad eventos,
    IOrigenDeSolicitud origenes) : ICasoDeUso<SubirArchivoEntrada, ArchivoSubido>
{
    public async Task<Resultado<ArchivoSubido>> Ejecutar(SubirArchivoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDelCatalogo.Fallo<ArchivoSubido>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDelCatalogo.Validacion<ArchivoSubido>(validacion);
        }

        // El cupo se toma después de autorizar (quien no es de la administración central no consume cupos) y antes de leer el cuerpo.
        using var permiso = limitador.Intentar(autorizador.UsuarioId!.Value);
        if (permiso is null)
        {
            await PublicarElLimite(autorizador.UsuarioId!.Value).ConfigureAwait(false);
            return FallosDelCatalogo.De<ArchivoSubido>(CategoriaDeError.Limite, ClavesDelCatalogo.LimiteDeSubidas);
        }

        var finalidad = entrada.Finalidad == FinalidadDeLaSubida.Portada ? FinalidadDeArchivo.Portada : FinalidadDeArchivo.Libro;
        var referencia = ReferenciaDeArchivo.Iniciar(Guid.NewGuid(), autorizador.UsuarioId!.Value, finalidad, reloj.Ahora);

        // Transacción 1: la fila nace antes de escribir el objeto (si el proceso cae, el trabajo de fondo parte de ella).
        await unidad.Ejecutar(async token => { await referencias.Agregar(referencia, token).ConfigureAwait(false); return true; }, cancelacion).ConfigureAwait(false);

        // La escritura se corta al cumplirse la duración máxima de la subida o si el llamador cancela.
        using var corte = CancellationTokenSource.CreateLinkedTokenSource(cancelacion, permiso.Cancelacion);
        ResultadoDeEscrituraEnAlmacen escrito;
        try
        {
            escrito = await almacen.Escribir(referencia.ClaveDelAlmacen, finalidad, entrada.Contenido, corte.Token).ConfigureAwait(false);
        }
        catch (AlmacenNoDisponibleException)
        {
            // La subida se rechaza; la fila subiendo la depura el trabajo de fondo (borra el objeto, si llegó a escribirse, y luego la fila).
            return FallosDelCatalogo.De<ArchivoSubido>(CategoriaDeError.NoDisponible, ClavesDelCatalogo.AlmacenNoDisponible);
        }
        catch (OperationCanceledException) when (permiso.Cancelacion.IsCancellationRequested && !cancelacion.IsCancellationRequested)
        {
            // La subida duró más que lo permitido: se descarta lo escrito y se rechaza como un límite de uso.
            await DescartarLaSubida(referencia, cancelacion).ConfigureAwait(false);
            await PublicarElLimite(autorizador.UsuarioId!.Value).ConfigureAwait(false);
            return FallosDelCatalogo.De<ArchivoSubido>(CategoriaDeError.Limite, ClavesDelCatalogo.LimiteDeSubidas);
        }

        if (!escrito.Escrito)
        {
            await DescartarLaSubida(referencia, cancelacion).ConfigureAwait(false);
            return FallosDelCatalogo.Fallo<ArchivoSubido>(Traducir(escrito.Rechazo!.Value));
        }

        var archivo = escrito.Archivo!;

        // Transacción 2: pasa a lista con lo verificado.
        var lista = await unidad.Ejecutar(
            async token =>
            {
                var fila = await referencias.Obtener(referencia.Id, paraActualizar: true, token).ConfigureAwait(false);
                if (fila is null || fila.Estado != EstadoDeReferencia.Subiendo)
                {
                    return null;
                }

                fila.MarcarLista(archivo.Tipo, archivo.TamanoEnBytes, archivo.Huella);
                await referencias.Guardar(fila, token).ConfigureAwait(false);
                return fila;
            },
            cancelacion).ConfigureAwait(false);

        if (lista is null)
        {
            // La fila ya no estaba (la depuró el trabajo de fondo): el objeto no sirve.
            await DescartarLaSubida(referencia, cancelacion).ConfigureAwait(false);
            return FallosDelCatalogo.De<ArchivoSubido>(CategoriaDeError.NoDisponible, ClavesDelCatalogo.AlmacenNoDisponible);
        }

        return Resultado<ArchivoSubido>.Exito(
            new ArchivoSubido(lista.Id, entrada.Finalidad, TiposDeArchivo.Nombre(archivo.Tipo), archivo.TamanoEnBytes, archivo.Huella.Hex, lista.VenceEn));
    }

    /// <summary>
    /// Deja el evento de seguridad «límite excedido» (SEG-08, AC-SEG-08) con el usuario y el origen, para que Auditoría lo agrupe y las alertas lo vean (cuatro subidas lentas pueden bloquear la subida de toda la administración). Nunca interrumpe la respuesta del límite.
    /// </summary>
    private async Task PublicarElLimite(Guid usuarioId)
    {
        try
        {
            var origen = origenes.Actual;
            await eventos.Publicar(new EventoDeSeguridad(TipoDeEventoDeSeguridad.LimiteExcedido, reloj.Ahora, LimitadorDeSubidas.Politica, origen.Huella, origen.Direccion, usuarioId.ToString("D")), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // El evento es informativo: si Auditoría no responde, el límite se mantiene y la solicitud se rechaza igual.
        }
    }

    /// <summary>
    /// El archivo no sirve: se borra el objeto y, <b>solo si se borró</b>, la fila. Si el almacén no pudo borrarlo, la fila pasa a <c>descarte</c> y el trabajo de fondo —que parte de las filas— lo borra después: quitar la fila
    /// dejaría un objeto sin validar y sin seguimiento para siempre.
    /// </summary>
    private async Task DescartarLaSubida(ReferenciaDeArchivo referencia, CancellationToken cancelacion)
    {
        var borrado = await IntentarBorrar(referencia.ClaveDelAlmacen, cancelacion).ConfigureAwait(false);
        try
        {
            await unidad.Ejecutar(
                async token =>
                {
                    if (borrado)
                    {
                        await referencias.Eliminar(referencia.Id, token).ConfigureAwait(false);
                    }
                    else
                    {
                        referencia.PasarADescarte();
                        await referencias.Guardar(referencia, token).ConfigureAwait(false);
                    }

                    return true;
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Si no se pudo anotar, la fila subiendo queda y el trabajo de fondo la depura (parte de las filas).
        }
    }

    private async Task<bool> IntentarBorrar(string clave, CancellationToken cancelacion)
    {
        try
        {
            await almacen.Borrar(clave, cancelacion).ConfigureAwait(false);
            return true;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>Un archivo que supera el tope es <c>archivo-demasiado-grande</c> (413); cualquier otro rechazo, <c>archivo-invalido</c> (422) con su motivo.</summary>
    internal static ErrorDeAplicacion Traducir(MotivoDeRechazoDeArchivo motivo) =>
        motivo == MotivoDeRechazoDeArchivo.DemasiadoGrande
            ? new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDelCatalogo.ArchivoDemasiadoGrande, [new ErrorDeCampo("archivo", "demasiado-grande")])
            : new ErrorDeAplicacion(
                CategoriaDeError.ReglaDeNegocio,
                ClavesDelCatalogo.ArchivoInvalido,
                [new ErrorDeCampo("archivo", motivo switch
                {
                    MotivoDeRechazoDeArchivo.TipoNoValido => "tipo-no-valido",
                    MotivoDeRechazoDeArchivo.DimensionesInvalidas => "dimensiones-invalidas",
                    _ => "estructura-invalida",
                })]);
}

/// <summary>
/// <c>ObtenerPortada</c> (D06): la huella debe ser exactamente 64 hexadecimales en minúsculas y se busca <b>solo</b> entre las portadas de obras (la huella de un libro responde «no encontrada»); también la de una obra dada de baja
/// mientras exista su referencia. Devuelve el tipo de medio del tipo detectado y el flujo; la Api solo traduce a HTTP. Un almacén que no responde es «no disponible».
/// </summary>
internal sealed class ObtenerPortada(ILecturasDelCatalogo lecturas, IAlmacenDeArchivos almacen)
{
    public async Task<Resultado<PortadaServida>> Ejecutar(string? huella, CancellationToken cancelacion)
    {
        var noEncontrada = FallosDelCatalogo.Fallo<PortadaServida>(TraductorDeRechazos.ObraNoEncontrada());
        if (!HuellaDeArchivo.TryDesdeHexadecimal(huella, out var buscada) || buscada is null)
        {
            return noEncontrada;
        }

        var portada = await lecturas.PortadaPorHuella(buscada, cancelacion).ConfigureAwait(false);
        if (portada is null)
        {
            return noEncontrada;
        }

        try
        {
            var flujo = await almacen.AbrirPortada(portada.ClaveDelAlmacen, cancelacion).ConfigureAwait(false);
            return flujo is null ? noEncontrada : Resultado<PortadaServida>.Exito(new PortadaServida(TiposDeArchivo.TipoDeMedio(portada.TipoDetectado), flujo));
        }
        catch (AlmacenNoDisponibleException)
        {
            return FallosDelCatalogo.De<PortadaServida>(CategoriaDeError.NoDisponible, ClavesDelCatalogo.AlmacenNoDisponible);
        }
    }
}

/// <summary>Los archivos del catálogo para la Api (T05, T13): subir y servir portadas.</summary>
internal sealed class ArchivosDelCatalogoDeLaAplicacion(ICasoDeUso<SubirArchivoEntrada, ArchivoSubido> subir, ObtenerPortada portada) : IArchivosDelCatalogo
{
    public Task<Resultado<ArchivoSubido>> SubirArchivo(SubirArchivoEntrada entrada, CancellationToken cancelacion = default) => subir.Ejecutar(entrada, cancelacion);

    public Task<Resultado<PortadaServida>> ObtenerPortada(string huella, CancellationToken cancelacion = default) => portada.Ejecutar(huella, cancelacion);
}


/// <summary>
/// <c>DepurarArchivosSinUso</c> (D05, trabajo de fondo diario): <b>parte de las filas</b> de <c>archivos_pendientes</c>, no de los objetos del almacén. Para una <c>subiendo</c> más vieja que un margen mayor que la duración máxima de una
/// subida, una <c>lista</c> vencida y una <c>descarte</c> (el libro o la portada reemplazados) borra el <b>objeto y luego la fila</b>; una <c>consumida</c> de más de 7 días solo pierde la <b>fila</b>: el objeto ya vive en la edición o
/// en la obra y nunca se borra (el reemplazo a los 30 días deja el objeto borrado porque pasa a <c>descarte</c> en el reemplazo). Falla cerrada: un objeto que no se pudo borrar deja su fila para la corrida siguiente. Solo el sistema.
/// </summary>
internal sealed class DepurarArchivosSinUso(
    IActorActual actores,
    IRepositorioDeReferenciasDeArchivo referencias,
    IAlmacenDeArchivos almacen,
    IUnidadDeTrabajo unidad,
    IReloj reloj)
{
    /// <summary>El margen de una subida sin terminar: mayor que su duración máxima (el limitador la corta a los 15 minutos).</summary>
    public static readonly TimeSpan MargenDeSubida = TimeSpan.FromHours(1);

    public const int MaximoPorCorrida = 200;

    public async Task<Resultado<DepuracionDeArchivos>> Ejecutar(CancellationToken cancelacion)
    {
        if (actores.Actual is not { Tipo: TipoDeActor.Sistema })
        {
            return FallosDelCatalogo.De<DepuracionDeArchivos>(CategoriaDeError.Prohibido, AutorizadorDelCatalogo.ClaveProhibido);
        }

        int objetos = 0, filas = 0, fallidas = 0;
        var candidatas = await unidad.Ejecutar(token => referencias.Depurables(reloj.Ahora, MargenDeSubida, MaximoPorCorrida, token), cancelacion).ConfigureAwait(false);
        foreach (var candidata in candidatas)
        {
            try
            {
                // Cada fila en su propia transacción: se toma con bloqueo, se revalida que siga como se leyó (un reemplazo pudo pasarla a descarte o un consumo, a consumida) y se borra el objeto y luego la fila.
                var resultado = await unidad.Ejecutar(token => Depurar(candidata, token), cancelacion).ConfigureAwait(false);
                objetos += resultado.Objeto ? 1 : 0;
                filas += resultado.Fila ? 1 : 0;
            }
            catch (Exception) when (!cancelacion.IsCancellationRequested)
            {
                fallidas++;
            }
        }

        return Resultado<DepuracionDeArchivos>.Exito(new DepuracionDeArchivos(objetos, filas, fallidas));
    }

    private async Task<(bool Objeto, bool Fila)> Depurar(ReferenciaDeArchivo candidata, CancellationToken cancelacion)
    {
        var fila = await referencias.Obtener(candidata.Id, paraActualizar: true, cancelacion).ConfigureAwait(false);
        if (fila is null || fila.Estado != candidata.Estado)
        {
            // Otra operación la cambió o la quitó entre la lectura y el bloqueo: no se toca (se revisa en la corrida siguiente).
            return (false, false);
        }

        var borrarObjeto = fila.Estado != EstadoDeReferencia.Consumida;
        if (borrarObjeto)
        {
            await almacen.Borrar(fila.ClaveDelAlmacen, cancelacion).ConfigureAwait(false);
        }

        await referencias.Eliminar(fila.Id, cancelacion).ConfigureAwait(false);
        return (borrarObjeto, true);
    }
}
