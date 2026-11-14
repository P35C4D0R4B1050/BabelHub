using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;
using ActorDeDominio = BabelHub.Ecommerce.Domain.Auditoria.Actor;
using TipoDeActorDePlataforma = BabelHub.Ecommerce.Application.Plataforma.TipoDeActor;

namespace BabelHub.Ecommerce.Application.Auditoria.Interno;

/// <summary>Motivos de las alertas técnicas que publica el propio módulo de Auditoría.</summary>
public static class MotivosDeAlertaDeAuditoria
{
    /// <summary>No se pudo registrar un evento de seguridad (AUD-12): el rechazo se mantiene, queda esta alerta.</summary>
    public const string AuditoriaNoDisponible = "auditoria-no-disponible";
}

/// <summary>
/// Puerto interno de solo inserción de los registros del módulo (la base de datos lo implementa en T25): un registro, una vez guardado, nunca se
/// modifica (AUD-03). Un fallo se expresa con una excepción.
/// </summary>
public interface IAlmacenDeRegistros
{
    Task Insertar(RegistroDeAuditoria registro, CancellationToken cancelacion = default);
}

/// <summary>
/// Puerto interno de escritura de un evento de seguridad que <b>no concede nada</b> (AUD-12): su implementación no debe dejar abortada la transacción
/// de quien llama si el <c>INSERT</c> falla (un punto de guardado con retroceso parcial, o una conexión y una transacción cortas propias), porque
/// el efecto protector que acompaña al evento (un bloqueo temporal, el rechazo de una solicitud) debe mantenerse con la auditoría caída (AC-AUD-03).
/// Un fallo se expresa con una excepción, ya sin efecto sobre la transacción del llamante.
/// </summary>
public interface IAlmacenDeEventosDeSeguridad
{
    Task Insertar(RegistroDeAuditoria registro, CancellationToken cancelacion = default);
}

/// <summary>El plazo propio con el que se escribe un evento de seguridad (separado del token de la solicitud, que un cliente puede cancelar a voluntad).</summary>
public static class PlazoDeEscrituraDeSeguridad
{
    public static readonly TimeSpan Plazo = TimeSpan.FromSeconds(5);

    public static async Task Ejecutar(Func<CancellationToken, Task> escribir)
    {
        ArgumentNullException.ThrowIfNull(escribir);
        using var fuente = new CancellationTokenSource(Plazo);
        await escribir(fuente.Token).ConfigureAwait(false);
    }
}

/// <summary>
/// Construye el <see cref="RegistroDeAuditoria"/> de una solicitud: resuelve la acción en el catálogo cerrado, impone la clase del puerto, toma el
/// actor del contexto verificado (AUD-23), el instante del reloj y el origen de la conexión, y deja el detalle en <see cref="DetalleSeguro"/>.
/// </summary>
public sealed class ConstructorDeRegistros(
    ServicioDeOrigen servicio,
    CatalogoDeAcciones catalogo,
    IReloj reloj,
    IActorActual actores,
    IOrigenDeSolicitud origenes)
{
    /// <summary>
    /// La acción del catálogo de la solicitud, si es de la clase que pide el puerto. Un error aquí es un defecto de quien llama (una acción sin
    /// declarar, de la clase equivocada o con la marca «aislada» distinta de la del catálogo) y siempre se propaga: ninguna ruta lo traga.
    /// </summary>
    public AccionDelCatalogo Resolver(SolicitudDeRegistro solicitud, ClaseDeAccion clase)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        if (!solicitud.Accion.EsValida)
        {
            throw new ArgumentException("La solicitud no indica la acción.", nameof(solicitud));
        }

        if (!catalogo.TryObtener(solicitud.Accion.Identificador, out var accion) || accion is null)
        {
            throw new ArgumentException("La acción no está declarada en el catálogo de auditoría: una acción sin declarar no se registra.", nameof(solicitud));
        }

        if (accion.Clase != clase)
        {
            throw new ArgumentException(
                clase == ClaseDeAccion.ConcedeOCambia
                    ? "Una acción que no concede nada se registra con IRegistroDeSeguridad, no con IAuditoria."
                    : "Una acción que concede o cambia algo se registra con IAuditoria, no con IRegistroDeSeguridad.",
                nameof(solicitud));
        }

        if (accion.Aislada != solicitud.Accion.Aislada)
        {
            throw new ArgumentException("La marca «aislada» de la acción no coincide con la del catálogo.", nameof(solicitud));
        }

        // Los dos servicios llevan el mismo catálogo, pero el almacén aislado es de Identidad (E-commerce): el Inventario no tiene dónde escribir una
        // acción de Identidad, y guardarla en su propio almacén dejaría datos de una cuenta fuera de su depuración (D17, AUD-19).
        if (accion.Aislada && servicio == ServicioDeOrigen.Inventario)
        {
            throw new ArgumentException("El Inventario no tiene almacén aislado: una acción de Identidad no se registra aquí.", nameof(solicitud));
        }

        return accion;
    }

    /// <summary>El registro de la solicitud ya resuelta. Falla si no hay contexto de actor o si un campo no cumple su formato.</summary>
    public RegistroDeAuditoria Construir(
        SolicitudDeRegistro solicitud,
        AccionDelCatalogo accion,
        RutaResuelta? ruta = null,
        string? correlacion = null,
        bool anonimoSinContexto = false)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        ArgumentNullException.ThrowIfNull(accion);
        var actual = actores.Actual
            ?? (anonimoSinContexto
                ? ActorActual.Anonimo()
                : throw new InvalidOperationException("Nadie estableció el contexto del actor: no se registra sin él (SEG-15)."));
        var origen = solicitud.Origen ?? (accion.Tipo == TipoDeRegistro.Auditoria ? null : origenes.Actual.Huella.ToString());

        return RegistroDeAuditoria.Crear(
            Guid.NewGuid(),
            servicio,
            accion,
            ADominio(actual),
            reloj.Ahora.ToUniversalTime(),
            solicitud.ObjetoTipo,
            solicitud.ObjetoId,
            solicitud.SucursalOrigen?.ToString("D"),
            solicitud.SucursalDestino?.ToString("D"),
            solicitud.Motivo,
            origen,
            ruta,
            DetalleSeguro.Construir(accion.Detalle, solicitud.Detalle),
            solicitud.Resultado switch
            {
                ResultadoAuditado.Exitoso => ResultadoDeOperacion.Exitoso,
                ResultadoAuditado.Rechazado => ResultadoDeOperacion.Rechazado,
                ResultadoAuditado.Fallido => ResultadoDeOperacion.Fallido,
                _ => throw new ArgumentException("El resultado no es válido.", nameof(solicitud)),
            },
            correlacion);
    }

    private static ActorDeDominio ADominio(ActorActual actual) => actual.Tipo switch
    {
        TipoDeActorDePlataforma.Usuario => ActorDeDominio.Usuario(actual.UsuarioId!.Value, actual.Rol!),
        TipoDeActorDePlataforma.Sistema => ActorDeDominio.Sistema(),
        TipoDeActorDePlataforma.Integracion => ActorDeDominio.Integracion(),
        TipoDeActorDePlataforma.Anonimo => ActorDeDominio.Anonimo(),
        _ => throw new InvalidOperationException("El tipo de actor no es válido."),
    };
}

/// <summary>Implementa <see cref="IAuditoria"/>: si no se puede registrar, la operación no se aplica (AUD-04).</summary>
public sealed class AuditoriaDeAplicacion(ConstructorDeRegistros constructor, IAlmacenDeRegistros almacen) : IAuditoria
{
    public async Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
    {
        var accion = constructor.Resolver(solicitud, ClaseDeAccion.ConcedeOCambia);
        var registro = constructor.Construir(solicitud, accion);
        try
        {
            await almacen.Insertar(registro, cancelacion).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception falla)
        {
            // El mensaje no repite el de la excepción: podría llevar la cadena de conexión o datos de la solicitud.
            throw new AuditoriaNoDisponibleException("No se pudo registrar la auditoría de la operación.", falla);
        }
    }
}

/// <summary>Implementa <see cref="IRegistroDeSeguridad"/>: nunca interrumpe; ante un fallo mantiene la protección y deja una alerta técnica (AUD-12).</summary>
public sealed class RegistroDeSeguridadDeAplicacion(
    ConstructorDeRegistros constructor,
    IAlmacenDeEventosDeSeguridad almacen,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj) : IRegistroDeSeguridad
{
    public async Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
    {
        // Un defecto de quien llama (acción sin declarar o de la clase equivocada) se propaga: no es un fallo del registro.
        var accion = constructor.Resolver(solicitud, ClaseDeAccion.NoConcede);
        try
        {
            var registro = constructor.Construir(solicitud, accion);

            // El evento de un rechazo no depende de que el cliente siga conectado: se escribe con un plazo propio y no con el token de la solicitud
            // (si no, cerrar el socket evitaría el registro y dispararía una alerta falsa).
            await PlazoDeEscrituraDeSeguridad.Ejecutar(token => almacen.Insertar(registro, token)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await AlertarSinInterrumpir().ConfigureAwait(false);
        }
    }

    private async Task AlertarSinInterrumpir()
    {
        try
        {
            // Sin token de cancelación: la alerta debe salir aunque la solicitud que la originó ya se haya cancelado.
            await alertas.Publicar(new AlertaTecnica(MotivosDeAlertaDeAuditoria.AuditoriaNoDisponible, reloj.Ahora)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // El registro técnico tampoco responde: la protección sigue y la falla ya no tiene a dónde ir.
        }
    }
}
