namespace BabelHub.Shared.Contratos;

/// <summary>
/// Alcances de la credencial de servicio entre el E-commerce y el Inventario (<c>docs/05-seguridad/03-autenticacion-y-autorizacion.md</c>,
/// <c>transversal-seguridad</c> T08): un secreto estático por alcance; la Aplicación de cada servicio los nombra con su enumeración
/// <c>AlcanceDeServicio</c> y la Api los mapea a estos nombres.
/// </summary>
public static class AlcancesDeCredencial
{
    public const string OperacionDeUsuario = "operacion-de-usuario";

    public const string LecturaDeDisponibilidad = "lectura-de-disponibilidad";

    public const string ExpiracionDeReservas = "expiracion-de-reservas";

    public const string ConsultaDePendientes = "consulta-de-pendientes";

    public const string BajaDeTitular = "baja-de-titular";

    public const string LecturaDeAlertas = "lectura-alertas";

    public const string ConsultaDeAuditoria = "consulta-de-auditoria";

    public const string MarcarAlerta = "marcar-alerta";

    public const string Historial = "historial";

    public const string SolicitarAviso = "solicitar-aviso";

    public const string LecturaDeContacto = "lectura-de-contacto";

    public const string OrigenReenviado = "origen-reenviado";

    public static readonly IReadOnlyList<string> Todos =
    [
        OperacionDeUsuario, LecturaDeDisponibilidad, ExpiracionDeReservas, ConsultaDePendientes, BajaDeTitular, LecturaDeAlertas,
        ConsultaDeAuditoria, MarcarAlerta, Historial, SolicitarAviso, LecturaDeContacto, OrigenReenviado,
    ];
}
