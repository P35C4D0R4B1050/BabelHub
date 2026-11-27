namespace BabelHub.Ecommerce.Domain.Identidad;

/// <summary>La vigencia de una contraseña temporal (IDE-23): vence al llegar su hora. El mismo corte para el ingreso y para saber si una administración puede ingresar.</summary>
public static class VigenciaDeLaContrasenaTemporal
{
    public static bool Vencida(bool debeCambiar, DateTimeOffset? hasta, DateTimeOffset ahora) => debeCambiar && hasta is { } limite && ahora >= limite;
}

/// <summary>Lo que hace falta saber de una cuenta para decidir el restablecimiento desde la consola: nada de sus secretos.</summary>
public sealed record AccesoDeUnaAdministracion(Guid UsuarioId, RolDeUsuario Rol, bool Activa, bool Cancelada, bool DebeCambiarLaTemporal, DateTimeOffset? TemporalHasta)
{
    public bool EsUnaAdministracionActiva => Rol == RolDeUsuario.AdministradorCentral && Activa && !Cancelada;

    /// <summary>
    /// «Puede ingresar» (identidad-recuperacion-del-segundo-factor §5): una administración activa cuya contraseña es definitiva o temporal todavía vigente. Un bloqueo temporal, textos por aceptar o un segundo factor sin configurar no la excluyen.
    /// </summary>
    public bool PuedeIngresar(DateTimeOffset ahora) => EsUnaAdministracionActiva && !VigenciaDeLaContrasenaTemporal.Vencida(DebeCambiarLaTemporal, TemporalHasta, ahora);
}

public enum DecisionDelRestablecimientoDesdeLaInstalacion
{
    Permitido,
    NoEsUnaAdministracionActiva,
    HayOtraAdministracion,
    SinSegundoFactor,
}

/// <summary>
/// El restablecimiento del segundo factor de la única administración desde el equipo del servicio (RSF-02, RSF-04 a RSF-06). Solo se aplica cuando la vía normal (IDE-38, otra administración) no existe.
/// </summary>
public static class ReglasDeRestablecimientoDesdeLaInstalacion
{
    /// <summary>Solo el entorno de revisión y el de producción en su modo normal: nunca en desarrollo, en pruebas, en un ensayo ni al restaurar (RSF-02).</summary>
    public static bool EntornoPermitido(string? entorno, string? modo) =>
        (Es(entorno, "Staging") || Es(entorno, "Production")) && (string.IsNullOrWhiteSpace(modo) || Es(modo, "Normal"));

    /// <summary>En este orden: la cuenta indicada debe ser una administración activa, ninguna otra puede ingresar y tiene un segundo factor activado.</summary>
    public static DecisionDelRestablecimientoDesdeLaInstalacion Decidir(
        AccesoDeUnaAdministracion? objetivo, bool segundoFactorActivo, IEnumerable<AccesoDeUnaAdministracion> administraciones, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(administraciones);
        if (objetivo is not { EsUnaAdministracionActiva: true })
        {
            return DecisionDelRestablecimientoDesdeLaInstalacion.NoEsUnaAdministracionActiva;
        }

        if (administraciones.Any(a => a.UsuarioId != objetivo.UsuarioId && a.PuedeIngresar(ahora)))
        {
            return DecisionDelRestablecimientoDesdeLaInstalacion.HayOtraAdministracion;
        }

        return segundoFactorActivo ? DecisionDelRestablecimientoDesdeLaInstalacion.Permitido : DecisionDelRestablecimientoDesdeLaInstalacion.SinSegundoFactor;
    }

    private static bool Es(string? valor, string esperado) => string.Equals(valor, esperado, StringComparison.OrdinalIgnoreCase);
}
