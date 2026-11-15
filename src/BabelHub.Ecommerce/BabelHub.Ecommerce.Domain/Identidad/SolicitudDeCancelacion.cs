namespace BabelHub.Ecommerce.Domain.Identidad;

public enum EstadoDeCancelacion
{
    /// <summary>Hay pendientes (pedidos sin entregar, reservas, pagos, devoluciones, reclamos): la cancelación espera y la persona no puede comprar (IDE-27).</summary>
    Diferida,

    /// <summary>Se cerraron todos los pendientes y se avisó: la persona tiene 7 días (sin pasar de los 30 desde la solicitud) para reconfirmar (IDE-68).</summary>
    EsperandoReconfirmacion,

    Ejecutada,

    /// <summary>No se reconfirmó, la persona desistió o pasaron 30 días (IDE-68, IDE-81): puede volver a comprar.</summary>
    Descartada,
}

public enum ResultadoDeReconfirmacion
{
    /// <summary>Se reconfirmó sin pendientes: se ejecuta la cancelación en la misma operación (IDE-71).</summary>
    Ejecutar,

    /// <summary>Apareció un pendiente: no se ejecuta, se informa cuáles son y la solicitud sigue diferida (IDE-71).</summary>
    MantenerDiferida,

    /// <summary>Pasó el plazo de 7 días o el día 30: la solicitud se descarta (IDE-81).</summary>
    FueraDePlazo,

    /// <summary>La solicitud no está esperando una reconfirmación.</summary>
    NoEstaEsperando,
}

/// <summary>
/// La solicitud de cancelación de una cuenta con pendientes (IDE-27, IDE-68, IDE-71, IDE-76, IDE-81): <b>diferida</b> mientras haya pendientes; al cerrarse todos se avisa y pasa a <b>esperando reconfirmación</b> con un plazo de
/// 7 días <b>sin pasar nunca del día 30</b> desde la solicitud; se ejecuta solo si la persona reconfirma sin pendientes nuevos; si aparece uno, vuelve a diferida y el plazo de 7 días se reinicia al cerrarse (con el mismo tope);
/// si el aviso por correo se suprimió, el plazo cuenta desde que el aviso se muestra en una sesión reconocida (IDE-76); pasados 30 días se descarta. Los instantes llegan como parámetros.
/// </summary>
public sealed class SolicitudDeCancelacion
{
    public static readonly TimeSpan PlazoDeReconfirmacion = TimeSpan.FromDays(7);

    public static readonly TimeSpan Tope = TimeSpan.FromDays(30);

    private SolicitudDeCancelacion(
        Guid id,
        Guid usuarioId,
        EstadoDeCancelacion estado,
        DateTimeOffset solicitadaEn,
        DateTimeOffset? reconfirmarHasta,
        DateTimeOffset? avisoVistoEn,
        DateTimeOffset? cerradaEn,
        bool ejecucionEnCurso)
    {
        Id = id;
        UsuarioId = usuarioId;
        Estado = estado;
        SolicitadaEn = solicitadaEn;
        ReconfirmarHasta = reconfirmarHasta;
        AvisoVistoEn = avisoVistoEn;
        CerradaEn = cerradaEn;
        EjecucionEnCurso = ejecucionEnCurso;
    }

    public Guid Id { get; }

    public Guid UsuarioId { get; }

    public EstadoDeCancelacion Estado { get; private set; }

    public DateTimeOffset SolicitadaEn { get; }

    /// <summary>Hasta cuándo puede reconfirmarse; nulo mientras el plazo no empezó (el aviso aún no se mostró, IDE-76).</summary>
    public DateTimeOffset? ReconfirmarHasta { get; private set; }

    /// <summary>Cuándo se mostró el aviso en una sesión reconocida (solo si el correo se suprimió).</summary>
    public DateTimeOffset? AvisoVistoEn { get; private set; }

    /// <summary>Cuándo se ejecutó o se descartó.</summary>
    public DateTimeOffset? CerradaEn { get; private set; }

    /// <summary>Una ejecución empezó pero no terminó (falló cerrada, IDE-70): se puede reintentar sin duplicarla.</summary>
    public bool EjecucionEnCurso { get; private set; }

    public DateTimeOffset FechaTope => SolicitadaEn + Tope;

    /// <summary>Mientras la solicitud esté diferida o esperando reconfirmación la persona no puede comprar (IDE-27); al descartarse lo recupera (IDE-81).</summary>
    public bool ImpideComprar => Estado is EstadoDeCancelacion.Diferida or EstadoDeCancelacion.EsperandoReconfirmacion;

    public static SolicitudDeCancelacion Diferir(Guid id, Guid usuarioId, DateTimeOffset ahora)
    {
        if (id == Guid.Empty || usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La solicitud necesita su identificador y su titular.");
        }

        return new SolicitudDeCancelacion(id, usuarioId, EstadoDeCancelacion.Diferida, ahora, null, null, null, false);
    }

    public static SolicitudDeCancelacion Restaurar(
        Guid id,
        Guid usuarioId,
        EstadoDeCancelacion estado,
        DateTimeOffset solicitadaEn,
        DateTimeOffset? reconfirmarHasta,
        DateTimeOffset? avisoVistoEn,
        DateTimeOffset? cerradaEn,
        bool ejecucionEnCurso) =>
        new(id, usuarioId, estado, solicitadaEn, reconfirmarHasta, avisoVistoEn, cerradaEn, ejecucionEnCurso);

    /// <summary>
    /// Se cerraron todos los pendientes (IDE-68): pasa a esperar la reconfirmación con 7 días de plazo, sin pasar del día 30; si el aviso por correo no pudo enviarse (<paramref name="avisoPorCorreo"/> falso) el plazo no empieza hasta
    /// que el aviso se muestre en la cuenta (IDE-76). Si ya pasó el día 30, se descarta. Devuelve falso si la solicitud no estaba diferida.
    /// </summary>
    public bool PendientesCerrados(DateTimeOffset ahora, bool avisoPorCorreo)
    {
        if (Estado != EstadoDeCancelacion.Diferida)
        {
            return false;
        }

        if (ahora >= FechaTope)
        {
            Descartar(ahora);
            return true;
        }

        Estado = EstadoDeCancelacion.EsperandoReconfirmacion;
        AvisoVistoEn = null;
        ReconfirmarHasta = avisoPorCorreo ? PlazoDesde(ahora) : null;
        return true;
    }

    /// <summary>El aviso de «ya puede reconfirmar» se muestra en una sesión reconocida (IDE-76): el plazo de 7 días cuenta desde ese instante, con el tope de 30 días.</summary>
    public void AvisoMostrado(DateTimeOffset ahora)
    {
        if (Estado == EstadoDeCancelacion.EsperandoReconfirmacion && ReconfirmarHasta is null)
        {
            AvisoVistoEn = ahora;
            ReconfirmarHasta = PlazoDesde(ahora);
        }
    }

    /// <summary>
    /// La persona reconfirma con su contraseña (y su segundo factor): se ejecuta solo en plazo y sin pendientes en <i>esa misma operación</i> (IDE-71); si hay un pendiente nuevo, sigue diferida (y al cerrarse el plazo de 7 días
    /// se reinicia, sin pasar del día 30); si el plazo venció, se descarta.
    /// </summary>
    public ResultadoDeReconfirmacion Reconfirmar(DateTimeOffset ahora, bool hayPendientes)
    {
        if (Estado != EstadoDeCancelacion.EsperandoReconfirmacion)
        {
            return ResultadoDeReconfirmacion.NoEstaEsperando;
        }

        // El plazo no empezó: el aviso aún no se mostró en una sesión reconocida (IDE-76), no se puede reconfirmar.
        if (ReconfirmarHasta is null && ahora < FechaTope)
        {
            return ResultadoDeReconfirmacion.NoEstaEsperando;
        }

        if (ahora >= FechaTope || ReconfirmarHasta is { } hasta && ahora > hasta)
        {
            Descartar(ahora);
            return ResultadoDeReconfirmacion.FueraDePlazo;
        }

        if (hayPendientes)
        {
            Estado = EstadoDeCancelacion.Diferida;
            ReconfirmarHasta = null;
            AvisoVistoEn = null;
            return ResultadoDeReconfirmacion.MantenerDiferida;
        }

        EjecucionEnCurso = true;
        return ResultadoDeReconfirmacion.Ejecutar;
    }

    /// <summary>La ejecución terminó en una sola transacción (IDE-70): la solicitud queda ejecutada.</summary>
    public bool Ejecutada(DateTimeOffset ahora)
    {
        // Solo termina una ejecución que empezó (devuelve falso si no: no se «ejecuta» en silencio una solicitud descartada).
        if (Estado != EstadoDeCancelacion.EsperandoReconfirmacion || !EjecucionEnCurso)
        {
            return false;
        }

        Estado = EstadoDeCancelacion.Ejecutada;
        CerradaEn = ahora;
        EjecucionEnCurso = false;
        return true;
    }

    /// <summary>La ejecución falló cerrada (IDE-70): no queda efecto parcial y se puede reintentar; si el reintento cruza el día 30, la solicitud se descarta (IDE-81).</summary>
    public void EjecucionFallo(DateTimeOffset ahora)
    {
        EjecucionEnCurso = false;
        if (Estado == EstadoDeCancelacion.EsperandoReconfirmacion && ahora >= FechaTope)
        {
            Descartar(ahora);
        }
    }

    /// <summary>La persona desiste antes de la ejecución (IDE-68): se descarta y puede volver a comprar.</summary>
    public bool Desistir(DateTimeOffset ahora)
    {
        if (EjecucionEnCurso)
        {
            // Una ejecución empezada se termina o falla cerrada: no se descarta a mitad (los datos quedarían borrados con la solicitud «descartada»).
            return false;
        }

        if (Estado is not (EstadoDeCancelacion.Diferida or EstadoDeCancelacion.EsperandoReconfirmacion))
        {
            return false;
        }

        Descartar(ahora);
        return true;
    }

    /// <summary>
    /// La revisión periódica: descarta lo vencido (el plazo de reconfirmación sin reconfirmar, IDE-68, o los 30 días, IDE-81, incluso si el aviso nunca se mostró o la ejecución falló cerrada). Devuelve verdadero si descartó.
    /// </summary>
    public bool DescartarSiVencio(DateTimeOffset ahora)
    {
        if (EjecucionEnCurso || Estado is not (EstadoDeCancelacion.Diferida or EstadoDeCancelacion.EsperandoReconfirmacion))
        {
            return false;
        }

        if (ahora >= FechaTope || Estado == EstadoDeCancelacion.EsperandoReconfirmacion && ReconfirmarHasta is { } hasta && ahora > hasta)
        {
            Descartar(ahora);
            return true;
        }

        return false;
    }

    private DateTimeOffset PlazoDesde(DateTimeOffset desde)
    {
        var propuesto = desde + PlazoDeReconfirmacion;
        return propuesto > FechaTope ? FechaTope : propuesto;
    }

    private void Descartar(DateTimeOffset ahora)
    {
        Estado = EstadoDeCancelacion.Descartada;
        CerradaEn = ahora;
        EjecucionEnCurso = false;
    }
}
