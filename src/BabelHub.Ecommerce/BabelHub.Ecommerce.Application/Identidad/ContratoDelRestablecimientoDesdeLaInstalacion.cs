using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.Application.Identidad;

/// <summary>Cómo terminó un restablecimiento del segundo factor pedido desde el equipo del servicio.</summary>
public enum ResultadoDelRestablecimientoDesdeLaInstalacion
{
    Restablecido,

    /// <summary>Lo pidió alguien que no es el propio sistema (RSF-03).</summary>
    NoPermitido,

    /// <summary>El entorno o el modo de arranque no lo admiten (RSF-02).</summary>
    EntornoNoPermitido,

    CorreoInvalido,
    NoEsUnaAdministracionActiva,
    HayOtraAdministracion,
    SinSegundoFactor,
}

/// <summary>El resultado y, si se restableció, si el aviso por correo salió en ese momento (RSF-13).</summary>
public sealed record RestablecimientoDesdeLaInstalacion(ResultadoDelRestablecimientoDesdeLaInstalacion Resultado, bool AvisoEnviado = false);

/// <summary>
/// El restablecimiento del segundo factor de la única administración (identidad-recuperacion-del-segundo-factor). <b>Solo lo usa la orden de consola</b> <c>restablecer-segundo-factor</c>: no tiene endpoint ni pantalla, y exige el actor «sistema».
/// </summary>
public interface IRestablecimientoDesdeLaInstalacion
{
    Task<RestablecimientoDesdeLaInstalacion> Restablecer(string? entorno, string? modo, string? correo, CancellationToken cancelacion = default);
}

/// <summary>La regla de entorno (RSF-02) para quien debe negarse <b>antes</b> de construir el servicio, cuando todavía no puede resolver el contrato.</summary>
public static class EntornoDelRestablecimiento
{
    public static bool Permite(string? entorno, string? modo) => ReglasDeRestablecimientoDesdeLaInstalacion.EntornoPermitido(entorno, modo);
}
