using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>
/// Lo que Auditoría usa para enviar el correo de una alerta a la administración (T34; AUD-29, IDE-82; AC-SEG-23): <b>un solo correo</b> con todos los destinatarios cuenta <b>una vez</b> contra el cupo de la administración
/// y contra el tope de <b>24 al día por motivo y servicio</b>; lo que supera el tope queda en la lista de alertas sin salir por correo (suprimido); sin cupo queda pendiente y Auditoría lo reintenta con sus esperas crecientes
/// (encolado); si el transporte falla se devuelve el cupo y el tope y se lanza, para que Auditoría reintente (AUD-40). Las direcciones salen de las cuentas registradas (nunca de una solicitud, SEG-28). Es el pegamento entre
/// los dos módulos: vive en la composición (fuera de los módulos) porque Auditoría no conoce el interior de Plataforma ni al revés (regla 13).
/// </summary>
public sealed class EnvioDeCorreoDeAlertasDeProduccion(
    CuotaDeCorreo cuota,
    ITransporteDeCorreo transporte,
    IRenderizadorDeCorreo plantillas,
    IDireccionesDeCuentas direcciones) : IEnvioDeCorreoDeAlertas
{
    /// <summary>La plantilla del correo de una alerta a la administración (sin enlaces ni datos sensibles, T29).</summary>
    public const string PlantillaDeAlerta = "alerta-a-la-administracion";

    public async Task<ResultadoDeEnvioDeAlerta> Enviar(CorreoDeAlerta correo, IReadOnlyList<DestinatarioDeAlerta> destinatarios, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(correo);
        ArgumentNullException.ThrowIfNull(destinatarios);

        // La decisión usa el motivo y el servicio, que son lo que cuenta el tope (IDE-82); el destinatario es solo de forma: las direcciones reales se resuelven después.
        var decision = await cuota.Decidir(
            new CorreoSaliente(CategoriaDeCorreo.AlertaALaAdministracion, "administracion@babelhub.invalid", PlantillaDeAlerta, motivo: correo.Motivo.Valor, servicio: correo.Servicio.ToString()),
            cancelacion).ConfigureAwait(false);
        switch (decision.Resultado)
        {
            case ResultadoDeEnvio.Suprimido:
                return ResultadoDeEnvioDeAlerta.Suprimido;
            case ResultadoDeEnvio.Encolado:
                return ResultadoDeEnvioDeAlerta.Encolado;
        }

        try
        {
            var lista = new List<string>();
            foreach (var destinatario in destinatarios)
            {
                if (await direcciones.DeLaCuenta(destinatario.CuentaId, cancelacion).ConfigureAwait(false) is { } direccion)
                {
                    lista.Add(direccion);
                }
            }

            if (lista.Count == 0)
            {
                throw new InvalidOperationException("Ningún destinatario de la alerta tiene una dirección registrada.");
            }

            await transporte.Enviar(plantillas.DeAlerta(correo.Servicio.ToString(), correo.Motivo.Valor, correo.PrimeraOcurrencia, correo.Ocurrencias, lista), cancelacion).ConfigureAwait(false);
            return ResultadoDeEnvioDeAlerta.Enviado;
        }
        catch (Exception)
        {
            // La cuota solo cuenta lo que salió; Auditoría reintenta con esperas crecientes (AUD-40).
            try
            {
                await cuota.Devolver(decision.Reserva!, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception) when (!cancelacion.IsCancellationRequested)
            {
                // El cupo se renueva solo a la medianoche de Lima.
            }

            throw;
        }
    }
}
