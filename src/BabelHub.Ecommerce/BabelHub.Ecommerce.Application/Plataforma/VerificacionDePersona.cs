namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>Acciones de la interfaz web que exigen verificar que es una persona cuando el origen se comporta como automatizado (SEG-31).</summary>
public enum AccionProtegida
{
    Ingreso,
    Registro,
    HojaDeReclamacion,
    Reserva,
    Asistente,
}

public static class AccionesProtegidas
{
    /// <summary>Nombre estable de la acción: es el <c>action</c> que el desafío de Turnstile declara en cada página.</summary>
    public static string NombreEstable(this AccionProtegida accion) => accion switch
    {
        AccionProtegida.Ingreso => "ingreso",
        AccionProtegida.Registro => "registro",
        AccionProtegida.HojaDeReclamacion => "hoja-de-reclamacion",
        AccionProtegida.Reserva => "reserva",
        AccionProtegida.Asistente => "asistente",
        _ => throw new ArgumentOutOfRangeException(nameof(accion)),
    };
}

public enum ResultadoDeExigencia
{
    /// <summary>El origen no se comporta como automatizado: la persona continúa sin fricción adicional.</summary>
    NoExigida,

    /// <summary>Se exigía y la persona superó la verificación; sigue sujeta a los límites por origen.</summary>
    Verificada,

    /// <summary>Se exige y no llegó el token: la Api responde 403 <c>&lt;SRV&gt;-VERIFICACION-REQUERIDA</c> y muestra el desafío.</summary>
    Requerida,

    /// <summary>El token es malformado o la verificación lo rechazó: 403 y un evento de seguridad.</summary>
    Fallida,

    /// <summary>La verificación no responde: falla cerrada con servicio no disponible para las páginas que la exigen.</summary>
    NoDisponible,
}

/// <summary>Decide si la solicitud debe demostrar que es de una persona y, si es así, la verifica antes de atenderla (SEG-31).</summary>
public interface IExigenciaDeVerificacionDePersona
{
    /// <summary>Se evalúa <b>antes</b> de reservar el cupo del límite, con el contador de rechazos del origen.</summary>
    Task<ResultadoDeExigencia> Exigir(AccionProtegida accion, string? tokenDeVerificacion, CancellationToken cancelacion = default);
}
