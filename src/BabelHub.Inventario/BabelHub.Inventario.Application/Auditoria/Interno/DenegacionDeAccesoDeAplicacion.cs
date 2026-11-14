using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Auditoria;

namespace BabelHub.Inventario.Application.Auditoria.Interno;

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

/// <summary>
/// Las plantillas de ruta de los endpoints del servicio, que la Api obtiene de sus rutas registradas y <b>normaliza</b> a segmentos <c>{nombre}</c>
/// (sin restricciones como <c>{id:guid}</c>, opcionales ni comodines): con ellas se reduce la ruta cruda a su plantilla (AUD-05, AUD-06). Una plantilla
/// que no cumpla la forma se descarta (<see cref="ResolutorDeRutas"/>) y no impide registrar el evento.
/// </summary>
public interface IPlantillasDeRuta
{
    IReadOnlyCollection<string> Todas { get; }
}

/// <summary>
/// Reduce la ruta cruda de una solicitud a su plantilla. Valida y ordena la lista de plantillas <b>una sola vez</b> (descarta las que no cumplen la
/// forma) y, ante cualquier problema, devuelve «sin-coincidencia»: la ruta nunca puede hacer perder el evento ni se guarda cruda. Una ruta vacía o
/// demasiado larga se descarta sin tocar la lista.
/// </summary>
public sealed class ResolutorDeRutas(IPlantillasDeRuta plantillas)
{
    private readonly Lazy<IReadOnlyCollection<string>> _validas = new(() =>
        Array.AsReadOnly(plantillas.Todas.Where(PlantillaDeRuta.EsPlantillaValida).Distinct(StringComparer.Ordinal).ToArray()));

    public RutaResuelta Resolver(string? rutaCruda)
    {
        if (string.IsNullOrWhiteSpace(rutaCruda) || rutaCruda.Length > PlantillaDeRuta.MaximoDeCaracteres)
        {
            return RutaResuelta.SinCoincidencia;
        }

        try
        {
            return PlantillaDeRuta.ResolverComoRuta(rutaCruda, _validas.Value);
        }
        catch (Exception)
        {
            return RutaResuelta.SinCoincidencia;
        }
    }
}

/// <summary>
/// El estado de la solicitud en curso para sus denegaciones: la correlación que <b>genera el servidor</b> (nunca la que envía el cliente) y la
/// bandera que garantiza <b>un único evento por solicitud</b>, aunque el rechazo pase por el manejador de errores, el de autorización y los eventos de
/// autenticación. Se registra con vida por solicitud (<c>Scoped</c>): con otra vida se registraría un solo evento por proceso.
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
/// «anónimo»: el rechazo no se pierde), su rol, la ruta reducida a su plantilla, el motivo, el código del error y el origen de la conexión. Nunca
/// interrumpe el rechazo y <b>no depende de que el cliente siga conectado</b>: se escribe con un plazo propio y no con el token de la solicitud. Si no
/// se puede escribir (o el motivo no está en el catálogo), deja una alerta técnica y la protección se mantiene (AUD-12).
/// </summary>
public sealed class DenegacionDeAccesoDeAplicacion(
    ConstructorDeRegistros constructor,
    IContextoDeDenegacion contexto,
    ResolutorDeRutas rutas,
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
            var solicitud = new SolicitudDeRegistro(AccionesAuditables.AccesoDenegado)
            {
                Motivo = denegacion.Motivo,
                Resultado = ResultadoAuditado.Rechazado,
                Detalle = denegacion.CodigoDeError is null
                    ? new Dictionary<string, string?>()
                    : new Dictionary<string, string?> { ["codigoDeError"] = denegacion.CodigoDeError },
            };
            var accion = constructor.Resolver(solicitud, ClaseDeAccion.NoConcede);
            var registro = constructor.Construir(solicitud, accion, rutas.Resolver(denegacion.Ruta), contexto.Correlacion, anonimoSinContexto: true);

            await PlazoDeEscrituraDeSeguridad.Ejecutar(token => escritor.Escribir(registro, token)).ConfigureAwait(false);
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
