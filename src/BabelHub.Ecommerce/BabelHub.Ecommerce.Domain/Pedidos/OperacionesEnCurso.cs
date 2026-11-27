using System.Globalization;

namespace BabelHub.Ecommerce.Domain.Pedidos;

/// <summary>El estado de una operación sobre las líneas (anulación o entrega, D03): <see cref="EnCurso"/> bloquea sus líneas frente a otra operación («la primera que confirma gana», PED-21); las demás las liberan.</summary>
public enum EstadoDeOperacion
{
    EnCurso,
    Completada,

    /// <summary>Pasó de 15 minutos, recibió un error permanente o (en una entrega) no confirmó ningún cierre a tiempo: libera sus líneas, queda visible a la administración y se reintenta con una operación nueva.</summary>
    Fallida,

    /// <summary>Otra operación ya resolvió sus líneas o quedó sin ninguna.</summary>
    Descartada,
}

/// <summary>
/// Quién inició una operación: el identificador, el rol y la sucursal del actor de la solicitud, que se guardan en la fila porque una reanudación se audita como «sistema» y nombra al iniciador en el detalle (D03). Son datos personales del personal: se anonimizan a los dos años
/// (PED-34): el identificador pasa a <see cref="Guid.Empty"/> y el rol y la sucursal a nulos.
/// </summary>
public sealed record IniciadorDeOperacion(Guid UsuarioId, string? Rol, Guid? SucursalId)
{
    public bool EstaAnonimizado => UsuarioId == Guid.Empty && Rol is null && SucursalId is null;

    /// <summary>Sin el identificador, el rol ni la sucursal: son datos personales del personal y no deben salir en ningún texto.</summary>
    public override string ToString() => $"IniciadorDeOperacion {{ Anonimizado = {EstaAnonimizado} }}";

    public IniciadorDeOperacion Anonimizar() => new(Guid.Empty, null, null);
}

/// <summary>
/// La clave de idempotencia de <c>cerrar</c> por línea (D03): <c>cierre-&lt;pedido sin guiones&gt;-&lt;lineaId corto&gt;</c>. Una llamada por línea permite saber cuál ya salió y que dos cuerpos distintos no choquen con <c>INV-IDEMPOTENCIA-EN-CONFLICTO</c>; repetirla devuelve el
/// resultado original (no descuenta dos veces). Cumple el formato del Inventario y no pasa de 80 caracteres.
/// </summary>
public static class ClaveDeCierreDeLinea
{
    public static string Para(Guid pedidoId, Guid lineaId)
    {
        if (pedidoId == Guid.Empty || lineaId == Guid.Empty)
        {
            throw new ArgumentException("El pedido y la línea no pueden ser vacíos.");
        }

        return string.Create(CultureInfo.InvariantCulture, $"cierre-{pedidoId:N}-{lineaId.ToString("N")[..8]}");
    }
}

/// <summary>La espera creciente entre reintentos de una operación en curso (D03): 30 segundos, 1, 2, 4 y 8 minutos como tope.</summary>
public static class EsperaDeReintentos
{
    public static readonly TimeSpan Tope = TimeSpan.FromMinutes(8);

    public static readonly TimeSpan LimiteDeLaOperacion = TimeSpan.FromMinutes(15);

    public static TimeSpan Para(int intentos)
    {
        var potencia = Math.Clamp(intentos - 1, 0, 6);
        var espera = TimeSpan.FromSeconds(30 * Math.Pow(2, potencia));
        return espera > Tope ? Tope : espera;
    }
}

/// <summary>Los pasos de una anulación (D04): se marcan al terminar cada uno y no se repiten.</summary>
public enum PasoDeAnulacion
{
    EnviosCancelados,
    ReservasLiberadas,
    ReembolsoRegistrado,
    AccesosRevocados,
}

/// <summary>
/// Una anulación en curso (administración o cancelación del cliente; D03, D04, D21): la fila durable que deja el tiempo 1, con el plan (líneas, envío y monto), el iniciador, los pasos ya hechos, la espera y los reintentos. Mientras está <see cref="EstadoDeOperacion.EnCurso"/>
/// bloquea sus líneas; <see cref="AjustarAlPlan"/> la recalcula si un 409 revela que una línea ya salió y la descarta si no queda ninguna. Reintentar una fallida es repetir la anulación: una fila nueva que reutiliza los pasos por idempotencia.
/// </summary>
public sealed partial class Anulacion
{
    private readonly Dictionary<PasoDeAnulacion, DateTimeOffset> _pasos = [];
    private readonly List<Guid> _lineaIds;

    private Anulacion(Guid id, Guid pedidoId, PlanDeAnulacion plan, IniciadorDeOperacion iniciador, DateTimeOffset ahora)
    {
        Id = id;
        PedidoId = pedidoId;
        Origen = plan.Origen;
        Motivo = plan.Motivo;
        Iniciador = iniciador;
        CreadaEn = ahora;
        _lineaIds = [];
        Aplicar(plan);
        Envio = plan.Envio;
        Monto = plan.Monto;
        HayDigitales = plan.LineasDigitales.Count > 0;
        ProximoIntentoEn = ahora + EsperaDeReintentos.Para(1);
    }

    public Guid Id { get; }

    public Guid PedidoId { get; }

    public OrigenDeLaAnulacion Origen { get; }

    public MotivoDeAnulacion Motivo { get; }

    public IniciadorDeOperacion Iniciador { get; private set; }

    /// <summary>El envío que se devuelve (PED-45): se guarda con la anulación y es el que se pide a Pagos, también al reintentar.</summary>
    public decimal Envio { get; private set; }

    public bool IncluyeEnvio => Envio > 0m;

    public decimal Monto { get; private set; }

    public bool HayDigitales { get; private set; }

    public IReadOnlyList<Guid> LineaIds => _lineaIds.AsReadOnly();

    public EstadoDeOperacion Estado { get; private set; } = EstadoDeOperacion.EnCurso;

    public Guid? ReembolsoId { get; private set; }

    public DateTimeOffset CreadaEn { get; }

    public DateTimeOffset? CompletadaEn { get; private set; }

    public int Intentos { get; private set; }

    public DateTimeOffset? ProximoIntentoEn { get; private set; }

    public bool BloqueaLasLineas => Estado == EstadoDeOperacion.EnCurso;

    /// <summary>Los pasos que faltan, en el orden de la anulación: cancelar envíos, liberar reservas, registrar el reembolso y revocar accesos (este solo si hay digitales).</summary>
    public IReadOnlyList<PasoDeAnulacion> PasosPendientes => [.. PasosRequeridos().Where(p => !_pasos.ContainsKey(p))];

    public static Anulacion Iniciar(Guid id, Guid pedidoId, PlanDeAnulacion plan, IniciadorDeOperacion iniciador, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(iniciador);
        if (iniciador.UsuarioId == Guid.Empty)
        {
            throw new ArgumentException("El iniciador de una operación nueva tiene identificador.", nameof(iniciador));
        }

        if (id == Guid.Empty || pedidoId == Guid.Empty)
        {
            throw new ArgumentException("La anulación y el pedido no pueden ser vacíos.");
        }

        if (plan.LineasImpresas.Count + plan.LineasDigitales.Count == 0)
        {
            throw new ArgumentException("Una anulación tiene al menos una línea.", nameof(plan));
        }

        return new Anulacion(id, pedidoId, plan, iniciador, ahora);
    }

    public DateTimeOffset? PasoConfirmadoEn(PasoDeAnulacion paso) => _pasos.TryGetValue(paso, out var cuando) ? cuando : null;

    /// <summary>Marca un paso hecho una sola vez: devuelve <see langword="false"/> si ya estaba o la operación ya no está en curso. El reembolso se registra con <see cref="RegistrarReembolso"/>.</summary>
    public bool MarcarPaso(PasoDeAnulacion paso, DateTimeOffset ahora)
    {
        if (paso == PasoDeAnulacion.ReembolsoRegistrado)
        {
            throw new ArgumentException("El reembolso se registra con su identificador.", nameof(paso));
        }

        if (Estado != EstadoDeOperacion.EnCurso || _pasos.ContainsKey(paso))
        {
            return false;
        }

        _pasos[paso] = ahora;
        return true;
    }

    /// <summary>Registra el reembolso de la anulación (clave natural única por pedido, D04) y marca el paso: un reembolso ya registrado no se cambia.</summary>
    public bool RegistrarReembolso(Guid reembolsoId, DateTimeOffset ahora)
    {
        if (reembolsoId == Guid.Empty || Estado != EstadoDeOperacion.EnCurso || ReembolsoId is not null)
        {
            return false;
        }

        ReembolsoId = reembolsoId;
        _pasos[PasoDeAnulacion.ReembolsoRegistrado] = ahora;
        return true;
    }

    /// <summary>Completa la anulación solo con todos sus pasos hechos y una sola vez.</summary>
    public bool Completar(DateTimeOffset ahora)
    {
        if (Estado != EstadoDeOperacion.EnCurso || PasosPendientes.Count > 0)
        {
            return false;
        }

        Estado = EstadoDeOperacion.Completada;
        CompletadaEn = ahora;
        return true;
    }

    /// <summary>Anota un intento de la reanudación: sube la cuenta, fija la próxima espera creciente y, pasados 15 minutos desde que se creó, la deja fallida.</summary>
    public void RegistrarIntento(DateTimeOffset ahora)
    {
        if (Estado != EstadoDeOperacion.EnCurso)
        {
            return;
        }

        Intentos++;
        if (ahora - CreadaEn > EsperaDeReintentos.LimiteDeLaOperacion)
        {
            Fallar(ahora);
            return;
        }

        ProximoIntentoEn = ahora + EsperaDeReintentos.Para(Intentos);
    }

    public void Fallar(DateTimeOffset ahora)
    {
        if (Estado == EstadoDeOperacion.EnCurso)
        {
            Estado = EstadoDeOperacion.Fallida;
            ProximoIntentoEn = null;
        }
    }

    public void Descartar(DateTimeOffset ahora)
    {
        if (Estado == EstadoDeOperacion.EnCurso)
        {
            Estado = EstadoDeOperacion.Descartada;
            CompletadaEn = ahora;
            ProximoIntentoEn = null;
        }
    }

    /// <summary>
    /// Tras un 409 o un envío mantenido: recalcula las líneas, si incluye el envío, el monto y si siguen las digitales con el plan nuevo (D10); <see langword="null"/> o un plan sin líneas deja la operación <see cref="EstadoDeOperacion.Descartada"/>. Los pasos ya hechos se conservan.
    /// </summary>
    public void AjustarAlPlan(PlanDeAnulacion? plan, DateTimeOffset ahora)
    {
        if (Estado != EstadoDeOperacion.EnCurso)
        {
            return;
        }

        if (plan is null || plan.LineasImpresas.Count + plan.LineasDigitales.Count == 0)
        {
            Descartar(ahora);
            return;
        }

        if (plan.Origen != Origen || plan.Motivo != Motivo)
        {
            throw new ArgumentException("El plan nuevo debe tener el mismo origen y motivo que la anulación.", nameof(plan));
        }

        _lineaIds.Clear();
        Aplicar(plan);
        Envio = plan.Envio;
        Monto = plan.Monto;
        HayDigitales = plan.LineasDigitales.Count > 0;
    }

    /// <summary>Anonimiza al iniciador (PED-34): se llama a los dos años o al cancelar la cuenta.</summary>
    public void AnonimizarIniciador() => Iniciador = Iniciador.Anonimizar();

    private void Aplicar(PlanDeAnulacion plan)
    {
        _lineaIds.AddRange(plan.LineasImpresas);
        _lineaIds.AddRange(plan.LineasDigitales);
    }

    private IEnumerable<PasoDeAnulacion> PasosRequeridos()
    {
        yield return PasoDeAnulacion.EnviosCancelados;
        yield return PasoDeAnulacion.ReservasLiberadas;
        yield return PasoDeAnulacion.ReembolsoRegistrado;
        if (HayDigitales)
        {
            yield return PasoDeAnulacion.AccesosRevocados;
        }
    }
}

/// <summary>Cómo respondió el Inventario al cerrar una línea (D03): cerrada ahora, ya cerrada por una clave anterior (descuenta una sola vez) o ya salió por Envíos (<c>INV-RESERVA-ESTADO-INVALIDO</c>: queda fuera de la entrega).</summary>
public enum ResultadoDelCierre
{
    Cerrada,
    YaCerrada,
    YaSalio,
}

/// <summary>Una línea de una entrega con su clave de cierre y, tras la respuesta del Inventario, cuándo se confirmó y con qué resultado.</summary>
public sealed record LineaDeEntrega(Guid LineaId, string ClaveDeCierre, DateTimeOffset? CierreConfirmadoEn, ResultadoDelCierre? ResultadoDelCierre);

/// <summary>
/// Una entrega en curso (D03, PED-11, PED-25): una llamada de cierre por línea con su clave, el paso confirmado justo tras cada respuesta del Inventario y la reanudación que solo repite lo local. La línea pasa a entregada <b>solo si el cierre tuvo éxito</b>; una entrega sin ningún cierre confirmado
/// pasada la espera corta (2 minutos) queda fallida para que el encargado la reintente con la misma clave (descuenta una sola vez).
/// </summary>
public sealed partial class Entrega
{
    public static readonly TimeSpan EsperaCortaSinCierre = TimeSpan.FromMinutes(2);

    private readonly List<LineaDeEntrega> _lineas;

    private Entrega(Guid id, Guid pedidoId, IReadOnlyList<Guid> lineaIds, IniciadorDeOperacion iniciador, DateTimeOffset ahora)
    {
        Id = id;
        PedidoId = pedidoId;
        Iniciador = iniciador;
        CreadaEn = ahora;
        _lineas = [.. lineaIds.Select(l => new LineaDeEntrega(l, ClaveDeCierreDeLinea.Para(pedidoId, l), null, null))];
        ProximoIntentoEn = ahora + EsperaDeReintentos.Para(1);
    }

    public Guid Id { get; }

    public Guid PedidoId { get; }

    public IniciadorDeOperacion Iniciador { get; private set; }

    public EstadoDeOperacion Estado { get; private set; } = EstadoDeOperacion.EnCurso;

    public DateTimeOffset CreadaEn { get; }

    public int Intentos { get; private set; }

    public DateTimeOffset? ProximoIntentoEn { get; private set; }

    public IReadOnlyList<LineaDeEntrega> Lineas => _lineas.AsReadOnly();

    public bool BloqueaLasLineas => Estado == EstadoDeOperacion.EnCurso;

    public IReadOnlyList<Guid> LineasPendientesDeCierre => [.. _lineas.Where(l => l.CierreConfirmadoEn is null).Select(l => l.LineaId)];

    /// <summary>Las líneas cuyo cierre tuvo éxito (ahora o antes): las que pasan a entregadas.</summary>
    public IReadOnlyList<Guid> LineasCerradas => [.. _lineas.Where(l => l.ResultadoDelCierre is ResultadoDelCierre.Cerrada or ResultadoDelCierre.YaCerrada).Select(l => l.LineaId)];

    /// <summary>Las líneas que ya habían salido por Envíos: quedan fuera de la entrega.</summary>
    public IReadOnlyList<Guid> LineasQueSalieron => [.. _lineas.Where(l => l.ResultadoDelCierre == ResultadoDelCierre.YaSalio).Select(l => l.LineaId)];

    public static Entrega Iniciar(Guid id, Guid pedidoId, IReadOnlyList<Guid> lineaIds, IniciadorDeOperacion iniciador, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(lineaIds);
        ArgumentNullException.ThrowIfNull(iniciador);
        if (id == Guid.Empty || pedidoId == Guid.Empty || lineaIds.Count == 0 || lineaIds.Distinct().Count() != lineaIds.Count)
        {
            throw new ArgumentException("Una entrega lleva un pedido y al menos una línea, sin repetir.");
        }

        if (iniciador.UsuarioId == Guid.Empty)
        {
            throw new ArgumentException("El iniciador de una operación nueva tiene identificador.", nameof(iniciador));
        }

        var entrega = new Entrega(id, pedidoId, lineaIds, iniciador, ahora);
        if (entrega._lineas.Select(l => l.ClaveDeCierre).Distinct(StringComparer.Ordinal).Count() != entrega._lineas.Count)
        {
            throw new ArgumentException("Las claves de cierre de dos líneas coinciden.", nameof(lineaIds));
        }

        return entrega;
    }

    /// <summary>Marca el cierre de una línea justo tras la respuesta del Inventario: una sola vez y solo mientras la entrega está en curso.</summary>
    public bool ConfirmarCierre(Guid lineaId, ResultadoDelCierre resultado, DateTimeOffset ahora)
    {
        var indice = _lineas.FindIndex(l => l.LineaId == lineaId);
        if (Estado != EstadoDeOperacion.EnCurso || indice < 0 || _lineas[indice].CierreConfirmadoEn is not null)
        {
            return false;
        }

        _lineas[indice] = _lineas[indice] with { CierreConfirmadoEn = ahora, ResultadoDelCierre = resultado };
        return true;
    }

    /// <summary>Una entrega sin ningún cierre confirmado después de la espera corta debe quedar fallida: el encargado la reintenta con la misma clave.</summary>
    public bool DebeFallarPorFaltaDeCierre(DateTimeOffset ahora) =>
        Estado == EstadoDeOperacion.EnCurso && _lineas.All(l => l.CierreConfirmadoEn is null) && ahora - CreadaEn > EsperaCortaSinCierre;

    public bool Completar(DateTimeOffset ahora)
    {
        if (Estado != EstadoDeOperacion.EnCurso || LineasPendientesDeCierre.Count > 0)
        {
            return false;
        }

        Estado = EstadoDeOperacion.Completada;
        ProximoIntentoEn = null;
        return true;
    }

    public void RegistrarIntento(DateTimeOffset ahora)
    {
        if (Estado != EstadoDeOperacion.EnCurso)
        {
            return;
        }

        Intentos++;
        if (ahora - CreadaEn > EsperaDeReintentos.LimiteDeLaOperacion)
        {
            Fallar(ahora);
            return;
        }

        ProximoIntentoEn = ahora + EsperaDeReintentos.Para(Intentos);
    }

    public void Fallar(DateTimeOffset ahora)
    {
        if (Estado == EstadoDeOperacion.EnCurso)
        {
            Estado = EstadoDeOperacion.Fallida;
            ProximoIntentoEn = null;
        }
    }

    public void Descartar(DateTimeOffset ahora)
    {
        if (Estado == EstadoDeOperacion.EnCurso)
        {
            Estado = EstadoDeOperacion.Descartada;
            ProximoIntentoEn = null;
        }
    }

    public void AnonimizarIniciador() => Iniciador = Iniciador.Anonimizar();
}
