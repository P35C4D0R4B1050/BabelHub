using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>El valor de la cookie de sesión: 32 bytes aleatorios en base64url (43 caracteres), con longitud y alfabeto validados <b>antes</b> de tocar la base (D01).</summary>
public static class ValorDeCookieDeSesion
{
    public const int LongitudEnBytes = 32;
    public const int LongitudDelTexto = 43;

    public static string Nuevo(IAleatoriosSeguros aleatorios)
    {
        ArgumentNullException.ThrowIfNull(aleatorios);
        return Convert.ToBase64String(aleatorios.Bytes(LongitudEnBytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TieneFormaValida(string? valor) =>
        valor is { Length: LongitudDelTexto } && valor.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>El SHA-256 del valor de la cookie en hexadecimal minúsculo: lo único que se guarda (D01).</summary>
    public static HashDeSesion Hash(string valor)
    {
        ArgumentNullException.ThrowIfNull(valor);
        return HashDeSesion.De(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(valor))));
    }
}

/// <summary>Operaciones comunes de las sesiones: datos asociados del cifrado, cierre con borrado de secretos y revocación de la renovación (D05).</summary>
internal sealed class ServiciosDeSesion(
    IAlmacenDeSesionesWeb sesiones,
    ICifradorDeSecretosDeSesion cifrador,
    ISesionesDeIdentidad identidad,
    IRegistroDeSeguridadDeLaWeb seguridad,
    IReloj reloj)
{
    public IAlmacenDeSesionesWeb Sesiones => sesiones;

    public ICifradorDeSecretosDeSesion Cifrador => cifrador;

    public ISesionesDeIdentidad Identidad => identidad;

    public IRegistroDeSeguridadDeLaWeb Seguridad => seguridad;

    public IReloj Reloj => reloj;

    /// <summary>Los datos asociados ligan cada texto cifrado a su fila y a su propósito: moverlo a otra fila no lo descifra (D02).</summary>
    public static string DatosAsociados(HashDeSesion id, PropositoDeSecreto proposito, Guid usuarioId) =>
        $"{id.Valor}|{proposito}|{usuarioId:N}";

    public string? DescifrarRenovacion(SesionGuardada fila) =>
        fila.RenovacionCifrada is null
            ? null
            : cifrador.Descifrar(fila.RenovacionCifrada, fila.VersionDeClave, PropositoDeSecreto.Renovacion, DatosAsociados(fila.Sesion.Id, PropositoDeSecreto.Renovacion, fila.Sesion.UsuarioId));


    /// <summary>
    /// Cierra o vence la sesión: <b>borra siempre la credencial cifrada</b> y la renovación <b>solo cuando Identidad confirma su revocación</b>; si no, la
    /// fila queda con la revocación pendiente para que la purga la reintente (D05). Con <paramref name="intentarRevocar"/> en <see langword="false"/> (la purga, cuando
    /// Identidad ya no respondió en esta corrida) no llama a Identidad y deja la revocación pendiente. No guarda la fila: lo hace quien llama.
    /// </summary>
    public async Task CerrarYRevocar(SesionGuardada fila, MotivoDeCierre motivo, CancellationToken cancelacion, bool intentarRevocar = true)
    {
        fila.Sesion.Cerrar(motivo, reloj.Ahora);
        fila.CredencialCifrada = null;
        fila.CredencialVenceEn = null;
        fila.RetornoTrasAceptar = null;

        await Revocar(fila, cancelacion, intentarRevocar).ConfigureAwait(false);
    }

    /// <summary>
    /// Revoca en Identidad la renovación guardada. Devuelve <see langword="true"/> si ya no queda nada por revocar (no había renovación, se confirmó o no se podía leer) y
    /// <see langword="false"/> si la revocación queda pendiente (Identidad no confirmó, falló o no se intentó).
    /// </summary>
    public async Task<bool> Revocar(SesionGuardada fila, CancellationToken cancelacion, bool intentarRevocar = true)
    {
        if (fila.RenovacionCifrada is null)
        {
            fila.RevocacionPendiente = false;
            return true;
        }

        string renovacion;
        try
        {
            renovacion = DescifrarRenovacion(fila)!;
        }
        catch (SecretoDeSesionIlegibleException)
        {
            // No se puede revocar lo que no se puede leer: se descarta (la renovación vence sola) y queda el evento.
            fila.RenovacionCifrada = null;
            fila.RevocacionPendiente = false;
            fila.RenovacionIncierta = false;
            await seguridad.AccesoDenegado(MotivoDeDenegacionWeb.SecretoIlegible, fila.Sesion.UsuarioId, cancelacion).ConfigureAwait(false);
            return true;
        }

        var confirmada = false;
        if (intentarRevocar)
        {
            try
            {
                confirmada = await identidad.RevocarRenovacion(renovacion, cancelacion).ConfigureAwait(false);
            }
            catch (Exception) when (!cancelacion.IsCancellationRequested)
            {
                // Identidad no responde: se reintenta en la purga.
            }
        }

        if (confirmada)
        {
            fila.RenovacionCifrada = null;
            fila.RevocacionPendiente = false;
            fila.RenovacionIncierta = false;
        }
        else
        {
            fila.RevocacionPendiente = true;
        }

        return confirmada;
    }
}
