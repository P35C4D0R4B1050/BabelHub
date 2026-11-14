namespace BabelHub.Ecommerce.Domain.Auditoria;

/// <summary>Qué hay que anonimizar de un registro (AUD-19): el resultado de <see cref="PoliticaDeAnonimizacion.Evaluar"/>.</summary>
public sealed record PlanDeAnonimizacion(bool ActorId, bool ActorRol, bool Objeto, bool Origen, IReadOnlyCollection<string> ClavesDelDetalle)
{
    public bool HayCambios => ActorId || ActorRol || Objeto || Origen || ClavesDelDetalle.Count > 0;
}

/// <summary>
/// La política de anonimización (AUD-11, AUD-19). Los registros conservan sus datos personales dos años, o hasta que se cancela la cuenta del
/// titular; después solo queda su <b>residuo anonimizado</b>, sin plazo: acción, fecha, tipo y resultado. Es una función pura de
/// (registro, instante, cuentas canceladas): la depuración de la base la aplica igual y una prueba de deriva compara las dos.
/// <list type="bullet">
/// <item>Un registro de <b>más de dos años</b> se anonimiza completo, sin necesidad de coincidencia con una cuenta: el identificador del actor, su
/// rol y el objeto <b>cuando el catálogo dice que identifican a una persona</b>, el origen y las claves personales del detalle.</item>
/// <item>Por <b>cuenta cancelada aún no anonimizada</b>: si el actor es esa cuenta, su identificador, su rol y el origen del mismo registro; si el
/// objeto afectado es esa cuenta y el catálogo dice que el objeto identifica, el objeto; y, en el detalle, solo las claves marcadas personales cuyo
/// valor es esa cuenta. El registro de un administrador sobre una cuenta cancelada conserva al administrador como actor.</item>
/// <item>Se reemplaza por una <b>constante</b> o por <c>NULL</c>, nunca por un hash estable (un hash enlazaría registros de la misma persona).</item>
/// <item>Es idempotente y por contenido: repetir no cambia nada, y una cuenta cancelada después de una anonimización parcial anonimiza lo que quede.</item>
/// </list>
/// </summary>
public static class PoliticaDeAnonimizacion
{
    /// <summary>Los años que se conservan los datos personales de un registro (AUD-11).</summary>
    public const int AniosDeRetencion = 2;

    /// <summary>El valor con que se reemplaza un campo de texto del detalle: una constante, igual para todas las personas.</summary>
    public const string Constante = "[anonimizado]";

    /// <summary>El residuo anonimizado no caduca (AUD-11): no hay plazo.</summary>
    public static readonly TimeSpan? PlazoDelResiduo = null;

    /// <summary>Si el registro tiene más de dos años de calendario. Exactamente dos años todavía no vence.</summary>
    public static bool Vencido(DateTimeOffset instante, DateTimeOffset ahora) => instante < ahora.AddYears(-AniosDeRetencion);

    public static PlanDeAnonimizacion Evaluar(RegistroDeAuditoria registro, DateTimeOffset ahora, IReadOnlyCollection<string> cuentasCanceladas)
    {
        ArgumentNullException.ThrowIfNull(registro);
        ArgumentNullException.ThrowIfNull(cuentasCanceladas);
        var canceladas = new HashSet<string>(cuentasCanceladas, StringComparer.OrdinalIgnoreCase);
        var vencido = Vencido(registro.InstanteUtc, ahora);
        var accion = registro.AccionDelCatalogo;

        var actorCancelado = registro.Actor.Id is { } id && canceladas.Contains(id.ToString());
        var objetoCancelado = registro.ObjetoId is { } objeto && canceladas.Contains(objeto);

        // Solo lo que todavía conserva un dato personal: así repetir la depuración no cambia nada.
        var actorId = registro.Actor.Id is not null && (vencido || actorCancelado);
        var actorRol = registro.Actor.Rol is not null && (actorCancelado || (vencido && accion.RolPersonal));
        var anonimizaObjeto = registro.ObjetoId is not null && accion.ObjetoPersonal && (vencido || objetoCancelado);
        var origen = registro.Origen is not null && (vencido || actorCancelado);

        var claves = registro.Detalle.Valores
            .Where(v => registro.Detalle.EsPersonal(v.Key) && v.Value != Constante && (vencido || canceladas.Contains(v.Value)))
            .Select(v => v.Key)
            .ToList();

        return new PlanDeAnonimizacion(actorId, actorRol, anonimizaObjeto, origen, claves);
    }

    /// <summary>Aplica el plan: el mismo registro con esos datos personales retirados, conservando acción, fecha, tipo y resultado.</summary>
    public static RegistroDeAuditoria Aplicar(RegistroDeAuditoria registro, PlanDeAnonimizacion plan, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(registro);
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.HayCambios)
        {
            return registro;
        }

        var actor = (plan.ActorId, plan.ActorRol) switch
        {
            (true, true) => registro.Actor.SinTodo(),
            (true, false) => registro.Actor.SinId(),
            (false, true) => registro.Actor.SinRol(),
            _ => registro.Actor,
        };

        return registro.ConAnonimizacion(
            actor,
            plan.Objeto ? null : registro.ObjetoId,
            plan.Origen ? null : registro.Origen,
            registro.Detalle.ConValoresAnonimizados(plan.ClavesDelDetalle, Constante),
            registro.AnonimizadoEn ?? ahora);
    }

    /// <summary>Evalúa y aplica: el registro anonimizado, o <c>null</c> si no había nada que anonimizar (idempotente).</summary>
    public static RegistroDeAuditoria? Depurar(RegistroDeAuditoria registro, DateTimeOffset ahora, IReadOnlyCollection<string> cuentasCanceladas)
    {
        var plan = Evaluar(registro, ahora, cuentasCanceladas);
        return plan.HayCambios ? Aplicar(registro, plan, ahora) : null;
    }
}
