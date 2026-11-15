namespace BabelHub.Ecommerce.Domain.Identidad;

/// <summary>
/// El calendario de días hábiles de los plazos de los derechos sobre los datos (IDE-12, D07): <b>lunes a viernes, sin feriados</b> (confirmado por la persona responsable el 2026-10-16), contados <b>desde el día siguiente</b>.
/// Trabaja con fechas de Lima (<see cref="DateOnly"/>): el llamador convierte el instante a la fecha de Lima.
/// </summary>
public static class CalendarioHabil
{
    public static bool EsHabil(DateOnly fecha) => fecha.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>La fecha del <paramref name="dias"/>-ésimo día hábil después de <paramref name="desde"/> (el día de <paramref name="desde"/> no cuenta). Con 0 días devuelve <paramref name="desde"/>.</summary>
    public static DateOnly SumarDiasHabiles(DateOnly desde, int dias)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dias);
        var fecha = desde;
        var restantes = dias;
        while (restantes > 0)
        {
            fecha = fecha.AddDays(1);
            if (EsHabil(fecha))
            {
                restantes--;
            }
        }

        return fecha;
    }

    /// <summary>Cuántos días hábiles hay entre <paramref name="desde"/> (excluido) y <paramref name="hasta"/> (incluido); 0 si <paramref name="hasta"/> no es posterior.</summary>
    public static int DiasHabilesEntre(DateOnly desde, DateOnly hasta)
    {
        var cuenta = 0;
        for (var fecha = desde.AddDays(1); fecha <= hasta; fecha = fecha.AddDays(1))
        {
            if (EsHabil(fecha))
            {
                cuenta++;
            }
        }

        return cuenta;
    }
}

public enum TipoDeSolicitudDeDatos
{
    Acceso,
    Rectificacion,
    Cancelacion,
    Oposicion,
}

public enum EstadoDeSolicitudDeDatos
{
    Recibida,
    EnAtencion,
    Atendida,
}

/// <summary>
/// Una solicitud del titular sobre sus datos personales (IDE-12, IDE-32): queda <b>recibida</b> con su fecha límite, contada en días hábiles desde el día siguiente (<b>20 para el acceso y 10 para los demás tipos</b>). Una
/// solicitud <b>en atención cuenta como sin atender</b> a efectos del vencimiento. Solo guarda identificador, tipo, estado y fechas (IDE-69): el detalle de lo pedido no es un dato de este tipo.
/// </summary>
public sealed class SolicitudDeDatos
{
    public const int DiasHabilesParaElAcceso = 20;

    public const int DiasHabilesParaLosDemas = 10;

    private SolicitudDeDatos(Guid id, Guid usuarioId, TipoDeSolicitudDeDatos tipo, EstadoDeSolicitudDeDatos estado, DateTimeOffset recibidaEn, DateOnly fechaLimite, DateTimeOffset? atendidaEn)
    {
        Id = id;
        UsuarioId = usuarioId;
        Tipo = tipo;
        Estado = estado;
        RecibidaEn = recibidaEn;
        FechaLimite = fechaLimite;
        AtendidaEn = atendidaEn;
    }

    public Guid Id { get; }

    public Guid UsuarioId { get; }

    public TipoDeSolicitudDeDatos Tipo { get; }

    public EstadoDeSolicitudDeDatos Estado { get; private set; }

    public DateTimeOffset RecibidaEn { get; }

    /// <summary>La fecha (de Lima) en que vence el plazo: se cumple si se atiende hasta ese día inclusive.</summary>
    public DateOnly FechaLimite { get; }

    public DateTimeOffset? AtendidaEn { get; private set; }

    public static int DiasHabilesDe(TipoDeSolicitudDeDatos tipo) => tipo == TipoDeSolicitudDeDatos.Acceso ? DiasHabilesParaElAcceso : DiasHabilesParaLosDemas;

    /// <summary>Registra la solicitud como recibida; <paramref name="hoyEnLima"/> es la fecha de Lima de <paramref name="ahora"/>.</summary>
    public static SolicitudDeDatos Recibir(Guid id, Guid usuarioId, TipoDeSolicitudDeDatos tipo, DateTimeOffset ahora, DateOnly hoyEnLima)
    {
        if (id == Guid.Empty || usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La solicitud necesita su identificador y su titular.");
        }

        return new SolicitudDeDatos(id, usuarioId, tipo, EstadoDeSolicitudDeDatos.Recibida, ahora, CalendarioHabil.SumarDiasHabiles(hoyEnLima, DiasHabilesDe(tipo)), null);
    }

    public static SolicitudDeDatos Restaurar(Guid id, Guid usuarioId, TipoDeSolicitudDeDatos tipo, EstadoDeSolicitudDeDatos estado, DateTimeOffset recibidaEn, DateOnly fechaLimite, DateTimeOffset? atendidaEn) =>
        new(id, usuarioId, tipo, estado, recibidaEn, fechaLimite, atendidaEn);

    public void PonerEnAtencion()
    {
        if (Estado == EstadoDeSolicitudDeDatos.Recibida)
        {
            Estado = EstadoDeSolicitudDeDatos.EnAtencion;
        }
    }

    public void Atender(DateTimeOffset ahora)
    {
        if (Estado != EstadoDeSolicitudDeDatos.Atendida)
        {
            Estado = EstadoDeSolicitudDeDatos.Atendida;
            AtendidaEn = ahora;
        }
    }

    /// <summary>Vencida: pasó la fecha límite y no está atendida (una solicitud en atención cuenta como sin atender, IDE-32).</summary>
    public bool EstaVencida(DateOnly hoyEnLima) => Estado != EstadoDeSolicitudDeDatos.Atendida && hoyEnLima > FechaLimite;

    public static IReadOnlyList<SolicitudConVencimiento> Ordenar(IEnumerable<SolicitudDeDatos> solicitudes, DateOnly hoyEnLima)
    {
        ArgumentNullException.ThrowIfNull(solicitudes);
        return
        [
            .. solicitudes
                .OrderBy(s => s.FechaLimite)
                .ThenBy(s => s.RecibidaEn)
                .ThenBy(s => s.Id)
                .Select(s => new SolicitudConVencimiento(s, s.EstaVencida(hoyEnLima))),
        ];
    }
}

/// <summary>Una solicitud de la lista de la administración con la marca de vencida (IDE-32).</summary>
public readonly record struct SolicitudConVencimiento(SolicitudDeDatos Solicitud, bool Vencida);
