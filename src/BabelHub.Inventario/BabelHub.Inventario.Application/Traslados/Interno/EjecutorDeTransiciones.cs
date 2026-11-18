using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Traslados;

namespace BabelHub.Inventario.Application.Traslados.Interno;

/// <summary>
/// El orden común de las operaciones sobre un traslado existente (plan §5): (1) el rol puede intentarla, (2) la forma de la entrada, (3) <b>leer el traslado sin bloqueo</b> con el alcance del actor (si el encargado no lo ve, el mismo 403
/// con un único evento que para uno ajeno), (4) <b>autorizar</b> por rol y sucursal (403 y un único evento), (5) el bloqueo del pedido si está vinculado, (6) la fila del traslado <c>FOR UPDATE</c> y el efecto de la operación
/// sobre el Dominio y el stock con el estado ya revalidado (409 y 422 sin evento de seguridad), (7) la fila con su versión, (8) la <b>auditoría</b> en la misma transacción (si falla, nada se aplica) y (9) los <b>mensajes</b> del buzón de
/// salida en la misma transacción. Un rechazo de negocio devuelve un fallo y el decorador de transacción lo revierte. Un <c>lock_timeout</c> es «servicio no disponible»; el reintento por interbloqueo es de <c>Plataforma</c> y no
/// vuelve a consumir presupuesto ni a registrar la denegación (<c>TomarTurno</c>).
/// </summary>
internal sealed class EjecutorDeTransiciones(
    AutorizadorDeTraslados autorizador,
    IRepositorioDeTraslados traslados,
    IBloqueosAsesores bloqueos,
    IDirectorioDeFuentes fuentes,
    IBuzonDeSalida buzon,
    IAuditoria auditoria,
    IReloj reloj)
{
    public AutorizadorDeTraslados Autorizador => autorizador;

    public DateTimeOffset Ahora => reloj.Ahora;

    /// <summary>
    /// Ejecuta <paramref name="aplicar"/> (el efecto sobre el Dominio y el stock: devuelve un error de negocio o <see langword="null"/>) sobre el traslado ya autorizado y bloqueado, y lo guarda con su auditoría y sus mensajes.
    /// </summary>
    public async Task<Resultado<TrasladoRegistrado>> Ejecutar(
        OperacionDeTraslado operacion,
        Guid trasladoId,
        Func<ResultadoDeValidacion> validar,
        Func<Traslado, DateTimeOffset, Task<ErrorDeAplicacion?>> aplicar,
        CancellationToken cancelacion)
    {
        try
        {
            var (leido, error) = await LeerYAutorizar(operacion, trasladoId, validar, cancelacion).ConfigureAwait(false);
            if (leido is null)
            {
                return FallosDeTraslados.Fallo<TrasladoRegistrado>(error!);
            }

            var traslado = await Bloquear(leido, cancelacion).ConfigureAwait(false);
            if (traslado is null)
            {
                return FallosDeTraslados.De<TrasladoRegistrado>(CategoriaDeError.NoEncontrado, ClavesDeTraslados.TrasladoNoEncontrado);
            }

            var anterior = traslado.Estado;
            var version = traslado.Version;
            var ahora = reloj.Ahora;
            ErrorDeAplicacion? fallo;
            try
            {
                fallo = await aplicar(traslado, ahora).ConfigureAwait(false);
            }
            catch (TransicionDeTrasladoInvalidaException)
            {
                return FallosDeTraslados.De<TrasladoRegistrado>(CategoriaDeError.Conflicto, ClavesDeTraslados.TrasladoEstadoInvalido);
            }
            catch (TrasladoVinculadoNoSeRechazaException)
            {
                return FallosDeTraslados.De<TrasladoRegistrado>(CategoriaDeError.ReglaDeNegocio, ClavesDeTraslados.TrasladoVinculadoNoSeRechaza);
            }

            if (fallo is not null)
            {
                return FallosDeTraslados.Fallo<TrasladoRegistrado>(fallo);
            }

            await GuardarAuditarYAvisar(traslado, version, ConversionDeTraslados.Auditar(traslado, anterior), conMensajes: true, ahora, cancelacion).ConfigureAwait(false);
            var (origen, destino) = await Fuentes(traslado, cancelacion).ConfigureAwait(false);
            return Resultado<TrasladoRegistrado>.Exito(ConversionDeTraslados.ARespuesta(traslado, origen, destino));
        }
        catch (EsperaDeBloqueoAgotadaException)
        {
            return FallosDeTraslados.BloqueoOcupado<TrasladoRegistrado>();
        }
    }

    /// <summary>Los pasos 1 a 4: el rol, la forma de la entrada, la lectura sin bloqueo con el alcance del actor y la autorización por rol y sucursal.</summary>
    public async Task<(Traslado? Traslado, ErrorDeAplicacion? Error)> LeerYAutorizar(OperacionDeTraslado operacion, Guid trasladoId, Func<ResultadoDeValidacion> validar, CancellationToken cancelacion)
    {
        if (await autorizador.ExigirRol(operacion, cancelacion).ConfigureAwait(false) is { } sinRol)
        {
            return (null, sinRol);
        }

        var validacion = validar();
        if (!validacion.EsValido)
        {
            return (null, new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDeTraslados.ValidacionFallida, validacion.Errores));
        }

        var leido = await traslados.PorId(trasladoId, cancelacion).ConfigureAwait(false);
        if (leido is null)
        {
            return (null, await autorizador.TrasladoNoVisible(cancelacion).ConfigureAwait(false));
        }

        if (await autorizador.Exigir(operacion, leido.OrigenId, leido.DestinoId, cancelacion).ConfigureAwait(false) is { } fueraDeAlcance)
        {
            return (null, fueraDeAlcance);
        }

        return (leido, null);
    }

    /// <summary>Los pasos 5 y 6: el bloqueo del pedido (si es vinculado) y la fila del traslado <c>FOR UPDATE</c>; el estado que se revalida es el de esta lectura.</summary>
    public async Task<Traslado?> Bloquear(Traslado leido, CancellationToken cancelacion)
    {
        if (leido.PedidoId is { } pedido)
        {
            await bloqueos.Pedido(pedido, cancelacion).ConfigureAwait(false);
        }

        return await traslados.PorIdBloqueado(leido.Id, cancelacion).ConfigureAwait(false);
    }

    /// <summary>Los pasos 7 a 9: la fila con su versión, la auditoría y los mensajes del buzón, todo en la transacción de la operación.</summary>
    public async Task GuardarAuditarYAvisar(Traslado traslado, long versionAnterior, SolicitudDeRegistro auditar, bool conMensajes, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        if (!await traslados.Guardar(traslado, versionAnterior, cancelacion).ConfigureAwait(false))
        {
            // Con la fila bloqueada nadie pudo cambiarla: un estado imposible aborta y la transacción se revierte.
            throw new InvalidOperationException("No se pudo guardar un traslado ya bloqueado.");
        }

        await auditoria.Registrar(auditar, cancelacion).ConfigureAwait(false);
        if (conMensajes)
        {
            var (origen, destino) = await Fuentes(traslado, cancelacion).ConfigureAwait(false);
            await buzon.Insertar(ConversionDeTraslados.Mensajes(traslado, origen.Codigo, destino.Codigo, ahora), cancelacion).ConfigureAwait(false);
        }
    }

    /// <summary>Las fuentes de origen y de destino de un traslado: nunca faltan (un traslado no existe sin ellas); si faltaran, es un estado imposible que revierte.</summary>
    public async Task<(FuenteDeStock Origen, FuenteDeStock Destino)> Fuentes(Traslado traslado, CancellationToken cancelacion)
    {
        var origen = await fuentes.PorId(traslado.OrigenId, cancelacion).ConfigureAwait(false);
        var destino = await fuentes.PorId(traslado.DestinoId, cancelacion).ConfigureAwait(false);
        return origen is null || destino is null
            ? throw new InvalidOperationException("Un traslado apunta a una fuente que no existe.")
            : (origen, destino);
    }
}
