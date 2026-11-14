using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;

namespace BabelHub.Ecommerce.Application.Auditoria.Interno;

/// <summary>
/// Escribe un acceso denegado <b>fuera de la transacción del caso de uso</b> (que ya se deshizo cuando corre el manejador de errores): la
/// Infraestructura lo implementa con su propia conexión y transacción corta, con <c>ON CONFLICT DO NOTHING</c> sobre el índice único parcial
/// <c>(servicio, correlacion, motivo)</c> y un tope de concurrencia con cortacircuitos para que un ataque con muchos orígenes no agote el pool (T25).
/// Un fallo se expresa con una excepción.
/// </summary>
public interface IEscritorDeDenegaciones
{
    Task Escribir(RegistroDeAuditoria registro, CancellationToken cancelacion = default);
}

/// <summary>Las plantillas de ruta de los endpoints del servicio, que la Api obtiene de sus rutas registradas: con ellas se reduce la ruta cruda a su plantilla (AUD-05, AUD-06).</summary>
public interface IPlantillasDeRuta
{
    IReadOnlyCollection<string> Todas { get; }
}

/// <summary>
/// El estado de la solicitud en curso para sus denegaciones: la correlación que <b>genera el servidor</b> (nunca la que envía el cliente) y la
/// bandera que garantiza <b>un único evento por solicitud</b>, aunque el rechazo pase por el manejador de errores, el de autorización y los eventos de
/// autenticación. Se registra con vida por solicitud (<c>Scoped</c>).
/// </summary>
public interface IContextoDeDenegacion
{
    string Correlacion { get; }

    /// <summary>Verdadero solo la primera vez que se pide en la solicitud; las siguientes son el mismo rechazo y no se registran otra vez.</summary>
    bool TomarTurno();
}

public sealed class ContextoDeDenegacion : IContextoDeDenegacion
{
    private int _tomado;

    public string Correlacion { get; } = Guid.NewGuid().ToString("N");

    public bool TomarTurno() => Interlocked.Exchange(ref _tomado, 1) == 0;
}

/// <summary>
/// Implementa <see cref="IDenegacionDeAcceso"/> (AUD-05): un único evento por solicitud, con el usuario o «anónimo» (si nadie fijó el contexto, también
/// «anónimo»: el rechazo no se pierde), su rol, la ruta reducida a su plantilla, el motivo y el origen de la conexión. Nunca interrumpe el rechazo: si no
/// se puede escribir (o el motivo no está en el catálogo), deja una alerta técnica y la protección se mantiene (AUD-12).
/// </summary>
public sealed class DenegacionDeAccesoDeAplicacion(
    ConstructorDeRegistros constructor,
    IContextoDeDenegacion contexto,
    IPlantillasDeRuta plantillas,
    IEscritorDeDenegaciones escritor,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj) : IDenegacionDeAcceso
{
    public async Task Registrar(DenegacionDeAcceso denegacion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(denegacion);
        if (!contexto.TomarTurno())
        {
            return;
        }

        try
        {
            var solicitud = new SolicitudDeRegistro(AccionesAuditables.AccesoDenegado) { Motivo = denegacion.Motivo, Resultado = ResultadoAuditado.Rechazado };
            var accion = constructor.Resolver(solicitud, ClaseDeAccion.NoConcede);
            var ruta = PlantillaDeRuta.ResolverComoRuta(denegacion.Ruta, plantillas.Todas);
            var registro = constructor.Construir(solicitud, accion, ruta, contexto.Correlacion, anonimoSinContexto: true);

            await escritor.Escribir(registro, cancelacion).ConfigureAwait(false);
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
            await alertas.Publicar(new AlertaTecnica(MotivosDeAlertaDeAuditoria.AuditoriaNoDisponible, reloj.Ahora)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // El registro técnico tampoco responde: el rechazo se mantiene.
        }
    }
}
