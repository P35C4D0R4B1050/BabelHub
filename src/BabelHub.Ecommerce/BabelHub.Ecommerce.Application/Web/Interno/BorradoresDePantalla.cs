using System.Text.Json;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>
/// Conserva y recupera lo que la persona tenía en pantalla cuando su sesión venció (WEB-25, T09, D06): solo si la sesión <b>venció</b> o se rechazó la renovación
/// (nunca tras un cierre voluntario), dentro de los 30 minutos que se conserva la fila, con un token antifalsificación válido para esa sesión, solo de una lista cerrada de
/// plantillas y sin contraseñas ni códigos. Cuerpo cifrado con datos asociados ligados al borrador y al usuario; entrega única y solo al mismo usuario.
/// </summary>
public sealed class BorradoresDePantalla(
    IAlmacenDeSesionesWeb sesiones,
    IAlmacenDeBorradores borradores,
    ICifradorDeSecretosDeSesion cifrador,
    IRegistroDeSeguridadDeLaWeb seguridad,
    IUnidadDeTrabajo unidad,
    IReloj reloj) : IBorradoresDePantalla
{
    public async Task<bool> Conservar(ConservarBorrador entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (!entrada.TokenAntifalsificacionValido || !ValorDeCookieDeSesion.TieneFormaValida(entrada.ValorDeLaCookie))
        {
            return false;
        }

        var fila = await sesiones.Cargar(ValorDeCookieDeSesion.Hash(entrada.ValorDeLaCookie), cancelacion).ConfigureAwait(false);
        var sesion = fila?.Sesion;
        var ahora = reloj.Ahora;
        if (sesion is null
            || sesion.Estado == EstadoDeSesion.Abierta
            || sesion.MotivoDeCierre is not { } motivo
            || !motivo.ConservaElBorrador()
            || sesion.CerradaEn is not { } cerradaEn
            || ahora - cerradaEn >= PoliticaDeBorradores.Vigencia)
        {
            return false;
        }

        BorradorDePantalla borrador;
        try
        {
            borrador = BorradorDePantalla.Crear(Guid.NewGuid(), sesion.UsuarioId, entrada.PlantillaDeRuta, entrada.Campos, ahora);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return await unidad.Ejecutar(
            async ct =>
            {
                var existente = await borradores.Cargar(sesion.UsuarioId, entrada.PlantillaDeRuta, ct).ConfigureAwait(false);
                var cantidad = await borradores.Contar(sesion.UsuarioId, ahora, ct).ConfigureAwait(false);
                if (!PoliticaDeBorradores.PuedeGuardar(cantidad, existente is not null))
                {
                    return false;
                }

                var cuerpo = cifrador.Cifrar(
                    JsonSerializer.Serialize(borrador.CamposGuardados),
                    PropositoDeSecreto.Borrador,
                    DatosAsociados(borrador.Id, borrador.UsuarioId, borrador.Plantilla));
                await borradores.Guardar(new BorradorGuardado(borrador, cuerpo.Bytes, cuerpo.VersionDeClave), ct).ConfigureAwait(false);
                return true;
            },
            cancelacion).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, string>?> Recuperar(Guid usuarioId, string plantillaDeRuta, CancellationToken cancelacion = default) =>
        await unidad.Ejecutar<IReadOnlyDictionary<string, string>?>(
            async ct =>
            {
                var guardado = await borradores.Cargar(usuarioId, plantillaDeRuta, ct).ConfigureAwait(false);
                if (guardado is null)
                {
                    return null;
                }

                var borrador = guardado.Borrador;
                IReadOnlyDictionary<string, string>? campos;
                try
                {
                    var texto = cifrador.Descifrar(guardado.CuerpoCifrado, guardado.VersionDeClave, PropositoDeSecreto.Borrador, DatosAsociados(borrador.Id, borrador.UsuarioId, borrador.Plantilla));
                    campos = JsonSerializer.Deserialize<Dictionary<string, string>>(texto);
                }
                catch (Exception excepcion) when (excepcion is SecretoDeSesionIlegibleException or JsonException)
                {
                    // Un cuerpo ilegible se descarta con un evento; nunca un error 500 (D02).
                    await borradores.Borrar(borrador.Id, ct).ConfigureAwait(false);
                    await seguridad.AccesoDenegado(MotivoDeDenegacionWeb.SecretoIlegible, usuarioId, ct).ConfigureAwait(false);
                    return null;
                }

                if (campos is null)
                {
                    await borradores.Borrar(borrador.Id, ct).ConfigureAwait(false);
                    return null;
                }

                var entrega = BorradorDePantalla.Reconstruir(borrador.Id, borrador.UsuarioId, borrador.Plantilla, campos, borrador.CreadoEn, borrador.Entregado).Entregar(usuarioId, reloj.Ahora);

                // Entregado, vencido o ya entregado: en los tres casos la fila se borra (el borrador es de un solo uso y de 30 minutos).
                if (entrega.Resultado != ResultadoDeEntrega.NoEsDelUsuario)
                {
                    await borradores.Borrar(borrador.Id, ct).ConfigureAwait(false);
                }

                return entrega.Campos;
            },
            cancelacion).ConfigureAwait(false);

    private static string DatosAsociados(Guid id, Guid usuarioId, string plantilla) => $"{id:N}|borrador|{usuarioId:N}|{plantilla}";
}
