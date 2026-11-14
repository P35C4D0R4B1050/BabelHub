using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Auditoria;
using AlertaDeSeguridadDePlataforma = BabelHub.Inventario.Application.Plataforma.AlertaDeSeguridad;

namespace BabelHub.Inventario.Application.Auditoria.Interno;

/// <summary>
/// Guarda y agrupa las alertas del servicio (AUD-13, AUD-29, AUD-39, AUD-40) y es la única implementación de los tres puertos de publicación: el público
/// <see cref="IAlertas"/> (cualquier módulo, con un motivo del catálogo cerrado) y los dos que define <c>Plataforma</c> (<see cref="IPublicadorDeAlertasTecnicas"/>
/// e <see cref="IPublicadorDeAlertasDeSeguridad"/>, D18). <b>Nunca interrumpe a quien la publica</b>: se escribe con un plazo propio (no con el token de la
/// solicitud) y cualquier fallo del almacén se traga; solo <see cref="IAlertas"/> lanza <see cref="ArgumentException"/> por un motivo que el catálogo no tiene
/// o de la clase equivocada (un defecto de quien llama). Los puertos de <c>Plataforma</c> con un motivo desconocido conservan la alerta con
/// <see cref="MotivosDeAlerta.MotivoNoCatalogado"/> (el texto original nunca se guarda). El correo no se envía aquí: lo hace <c>AgruparYEnviarAlertas</c>.
/// </summary>
public sealed class PublicadorDeAlertas(
    ServicioDeOrigen servicio,
    CatalogoDeMotivosDeAlerta catalogo,
    IAlmacenDeAlertas almacen,
    IUnidadDeTrabajo unidad,
    IReloj reloj) : IAlertas, IPublicadorDeAlertasTecnicas, IPublicadorDeAlertasDeSeguridad
{
    public Task PublicarTecnica(CodigoDeAlerta motivo, CancellationToken cancelacion = default) => PublicarDeCatalogo(motivo, ClaseDeAlerta.Tecnica);

    public Task PublicarDeSeguridad(CodigoDeAlerta motivo, CancellationToken cancelacion = default) => PublicarDeCatalogo(motivo, ClaseDeAlerta.Seguridad);

    Task IPublicadorDeAlertasTecnicas.Publicar(AlertaTecnica alerta, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(alerta);
        return Registrar(alerta.Motivo, alerta.Instante);
    }

    Task IPublicadorDeAlertasDeSeguridad.Publicar(AlertaDeSeguridadDePlataforma alerta, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(alerta);
        return Registrar(alerta.Motivo, alerta.Instante);
    }

    private Task PublicarDeCatalogo(CodigoDeAlerta motivo, ClaseDeAlerta clase)
    {
        if (string.IsNullOrEmpty(motivo.Valor) || !catalogo.Contiene(motivo.Valor))
        {
            throw new ArgumentException("El motivo no está en el catálogo cerrado de alertas: una alerta sin catalogar no se publica.", nameof(motivo));
        }

        if (catalogo.Obtener(motivo.Valor).Clase != clase)
        {
            throw new ArgumentException("El motivo es de otra clase: una alerta técnica y una de seguridad se publican por su método.", nameof(motivo));
        }

        return Registrar(motivo.Valor, reloj.Ahora);
    }

    /// <summary>Suma la ocurrencia a la alerta abierta de la ventana, o abre una nueva; todo fallo del almacén se traga (AUD-40).</summary>
    private async Task Registrar(string motivo, DateTimeOffset instante)
    {
        var codigo = catalogo.Contiene(motivo) ? motivo : MotivosDeAlerta.MotivoNoCatalogado;
        try
        {
            var enUtc = instante.ToUniversalTime();
            await PlazoDeEscrituraDeSeguridad.Ejecutar(token => unidad.Ejecutar(async ct =>
            {
                var abierta = await almacen.UltimaDelMotivoParaActualizar(servicio, codigo, ct).ConfigureAwait(false);
                var resultado = VentanaDeAgrupacion.Registrar(abierta, catalogo, servicio, codigo, enUtc, Guid.NewGuid());
                var alerta = resultado.Alerta;

                // Una alerta originada por el propio correo queda en el registro técnico sin pendiente de correo: se atiende reintentando el correo (AUD-29).
                if (resultado.Abrio && !catalogo.Obtener(codigo).EnviablePorCorreo)
                {
                    alerta = alerta.MarcarComoEnviada();
                }

                await almacen.Guardar(alerta, ct).ConfigureAwait(false);
                return true;
            }, token)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // El registro técnico tampoco responde: no hay a dónde mandar esta falla y nunca se interrumpe a quien publica (AUD-12, AUD-40).
        }
    }
}
