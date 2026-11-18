using System.Globalization;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Shared.Contratos.Auditoria;

namespace BabelHub.Ecommerce.Infrastructure.Web;

/// <summary>
/// Registra una acción directa bloqueada por textos legales pendientes (WEB-47, AC-WEB-40) como un <b>evento de seguridad propio</b>
/// (<see cref="AccionesAuditables.AccesoBloqueadoPorTextosPendientes"/>, no <c>AccesoDenegado</c>, de modo que el umbral de SEG-09 no lo cuenta) con el motivo del bloqueo, la plantilla de la ruta sin
/// consulta, el método y la cuenta por su identificador opaco, y lo cuenta en una <b>ventana fija de 15 minutos</b> alineada al reloj: la primera vez que la ventana supera
/// <see cref="UmbralPorVentana"/> publica <b>una</b> alerta técnica (<c>bloqueos-por-textos-pendientes</c>), que Auditoría agrupa y envía por correo según AUD-29. El contador es en memoria y por
/// proceso (cada host de la web es un proceso: un volumen anómalo avisa una vez por ventana y por host). Nunca interrumpe: el bloqueo de la solicitud ya está decidido.
/// </summary>
public sealed class RegistroDeBloqueosPorTextosPendientes(IRegistroDeSeguridad registro, IAlertas alertas, IReloj reloj)
{
    /// <summary>Cuántos bloqueos en una ventana de 15 minutos se consideran anómalos. Valor confirmado por la persona responsable el 2026-11-18 (<c>progress/hallazgos.md</c>).</summary>
    public const int UmbralPorVentana = 50;

    public static readonly TimeSpan Ventana = TimeSpan.FromMinutes(15);

    private readonly object _candado = new();
    private long _ventanaActual = long.MinValue;
    private int _cuenta;
    private bool _avisada;

    /// <summary>El motivo de catálogo de la acción para cada motivo de bloqueo; <see langword="null"/> si el motivo no es de este evento (la falla de Identidad es la alerta de WEB-48).</summary>
    public static string? MotivoDelCatalogo(MotivoDeBloqueoPorTextos motivo) => motivo switch
    {
        MotivoDeBloqueoPorTextos.AccionNoPermitida => "accion-no-permitida",
        MotivoDeBloqueoPorTextos.RutaNoDeclarada => "ruta-no-declarada",
        MotivoDeBloqueoPorTextos.MetodoNoDeclarado => "metodo-no-declarado",
        MotivoDeBloqueoPorTextos.MetodoNoEstandar => "metodo-no-estandar",
        _ => null,
    };

    public async Task Registrar(ClaveDeBloqueoPorTextos clave, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(clave);
        if (MotivoDelCatalogo(clave.Motivo) is not { } motivo)
        {
            return;
        }

        var solicitud = new SolicitudDeRegistro(AccionesAuditables.AccesoBloqueadoPorTextosPendientes)
        {
            ObjetoTipo = "usuario",
            ObjetoId = Guid.TryParseExact(clave.Cuenta, "N", out var cuenta) ? cuenta.ToString("D", CultureInfo.InvariantCulture) : null,
            Motivo = motivo,
            Resultado = ResultadoAuditado.Rechazado,
            Detalle = new Dictionary<string, string?> { ["metodo"] = clave.Metodo, ["plantilla"] = clave.PlantillaDeRuta },
        };
        await registro.Registrar(solicitud, cancelacion).ConfigureAwait(false);

        if (ContarYDebeAvisar(reloj.Ahora))
        {
            await alertas.PublicarTecnica(new CodigoDeAlerta(MotivosDeAlertaDeAuditoria.BloqueosPorTextosPendientes), cancelacion).ConfigureAwait(false);
        }
    }

    private bool ContarYDebeAvisar(DateTimeOffset ahora)
    {
        var ventana = ahora.ToUnixTimeSeconds() / (long)Ventana.TotalSeconds;
        lock (_candado)
        {
            if (ventana != _ventanaActual)
            {
                _ventanaActual = ventana;
                _cuenta = 0;
                _avisada = false;
            }

            _cuenta++;
            if (_avisada || _cuenta <= UmbralPorVentana)
            {
                return false;
            }

            _avisada = true;
            return true;
        }
    }
}
