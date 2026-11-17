namespace BabelHub.Inventario.Domain.Traslados;

/// <summary>Los estados de un traslado (TRA-03): solicitado → aprobado | rechazado | cancelado; aprobado → en tránsito | cancelado; en tránsito → recibido. No hay otras transiciones.</summary>
public enum EstadoDeTraslado
{
    Solicitado,
    Aprobado,
    EnTransito,
    Recibido,
    Rechazado,
    Cancelado,
}

/// <summary>Los identificadores estables de los estados (los de <c>EstadosDeTraslado</c> de <c>Shared</c> y los del <c>CHECK</c> de la tabla).</summary>
public static class EstadosDeTraslado
{
    public const string Solicitado = "solicitado";

    public const string Aprobado = "aprobado";

    public const string EnTransito = "en-transito";

    public const string Recibido = "recibido";

    public const string Rechazado = "rechazado";

    public const string Cancelado = "cancelado";

    public static string Identificador(this EstadoDeTraslado estado) => estado switch
    {
        EstadoDeTraslado.Solicitado => Solicitado,
        EstadoDeTraslado.Aprobado => Aprobado,
        EstadoDeTraslado.EnTransito => EnTransito,
        EstadoDeTraslado.Recibido => Recibido,
        EstadoDeTraslado.Rechazado => Rechazado,
        EstadoDeTraslado.Cancelado => Cancelado,
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };

    public static EstadoDeTraslado? DeIdentificador(string? identificador) => identificador switch
    {
        Solicitado => EstadoDeTraslado.Solicitado,
        Aprobado => EstadoDeTraslado.Aprobado,
        EnTransito => EstadoDeTraslado.EnTransito,
        Recibido => EstadoDeTraslado.Recibido,
        Rechazado => EstadoDeTraslado.Rechazado,
        Cancelado => EstadoDeTraslado.Cancelado,
        _ => null,
    };
}

/// <summary>Se intentó una transición que el estado del traslado no admite (despachar sin aprobar, aprobar dos veces, recibir lo que no está en tránsito…). La Aplicación la traduce a <c>INV-TRASLADO-ESTADO-INVALIDO</c> (TRA-03, D02) sin cambiar nada.</summary>
public sealed class TransicionDeTrasladoInvalidaException : Exception
{
    public TransicionDeTrasladoInvalidaException()
        : base("La transición no es válida para el estado actual del traslado.")
    {
    }

    public TransicionDeTrasladoInvalidaException(string message)
        : base(message)
    {
    }

    public TransicionDeTrasladoInvalidaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Se intentó rechazar o cancelar a mano un traslado vinculado a un pedido (TRA-16, TRA-18): solo se aprueba o se anula el pedido. La Aplicación la traduce a <c>INV-TRASLADO-VINCULADO-NO-SE-RECHAZA</c> (422), sin evento de seguridad.</summary>
public sealed class TrasladoVinculadoNoSeRechazaException : Exception
{
    public TrasladoVinculadoNoSeRechazaException()
        : base("Un traslado vinculado a un pedido solo puede aprobarse; para detenerlo se debe anular el pedido.")
    {
    }

    public TrasladoVinculadoNoSeRechazaException(string message)
        : base(message)
    {
    }

    public TrasladoVinculadoNoSeRechazaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Qué hizo la anulación de un pedido sobre su traslado (TRA-15, TRA-19): lo canceló, lo marcó de pedido anulado (estaba en tránsito) o no tenía nada que cambiar (ya cerrado o ya marcado). La anulación nunca falla por un traslado (D02).</summary>
public enum EfectoDeLaAnulacionDelPedido
{
    SinCambios,
    Cancelado,
    MarcadoDePedidoAnulado,
}

/// <summary>
/// Un traslado entre dos fuentes (TRA-01 a TRA-23): la edición, la cantidad, el origen, el destino, el motivo, el estado, el pedido al que está vinculado (si es uno de recojo que sale de otra sucursal), la marca de pedido anulado, la estimación de
/// llegada, la guía y las fechas por estado, con una <b>versión</b> que sube con cada cambio (la base la usa para que una transición repetida o simultánea cambie una sola vez). La máquina de estados vive aquí: cualquier otra transición lanza
/// <see cref="TransicionDeTrasladoInvalidaException"/> y rechazar o cancelar a mano uno vinculado, <see cref="TrasladoVinculadoNoSeRechazaException"/> (también lo impide el Dominio, D04). Los textos libres (motivo, motivo de cierre y guía) son de
/// empleados: tienen tope, no entran a la auditoría ni a los avisos (D07) y no se imprimen.
/// </summary>
public sealed class Traslado
{
    /// <summary>El tope del motivo y del motivo de cierre.</summary>
    public const int LargoMaximoDeTexto = 200;

    /// <summary>El tope de la guía (un código de transportista).</summary>
    public const int LargoMaximoDeLaGuia = 40;

    private Traslado(
        Guid id,
        long numero,
        Guid edicionId,
        Guid origenId,
        Guid destinoId,
        int cantidad,
        EstadoDeTraslado estado,
        string? motivo,
        string? motivoDeCierre,
        Guid? pedidoId,
        bool pedidoAnulado,
        DateOnly? fechaEstimada,
        bool estimadaPorConfirmar,
        string? guia,
        Guid? solicitadoPor,
        DateTimeOffset solicitadoEn,
        DateTimeOffset? aprobadoEn,
        DateTimeOffset? despachadoEn,
        DateTimeOffset? recibidoEn,
        DateTimeOffset? cerradoEn,
        DateTimeOffset? alertaDeDemoraEn,
        long version)
    {
        if (id == Guid.Empty || edicionId == Guid.Empty || origenId == Guid.Empty || destinoId == Guid.Empty)
        {
            throw new ArgumentException("Un traslado necesita su identificador, la edición, el origen y el destino.");
        }

        if (origenId == destinoId)
        {
            throw new ArgumentException("El origen y el destino de un traslado son distintos.", nameof(destinoId));
        }

        if (cantidad <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad de un traslado es positiva.");
        }

        if (motivo is { Length: > LargoMaximoDeTexto } || motivoDeCierre is { Length: > LargoMaximoDeTexto } || guia is { Length: > LargoMaximoDeLaGuia })
        {
            throw new ArgumentException("Un texto del traslado supera su tope.");
        }

        if (pedidoId == Guid.Empty || (pedidoAnulado && pedidoId is null))
        {
            throw new ArgumentException("La marca de pedido anulado solo existe en un traslado vinculado.", nameof(pedidoAnulado));
        }

        if (pedidoId is not null && estado == EstadoDeTraslado.Rechazado)
        {
            throw new ArgumentException("Un traslado vinculado a un pedido no se rechaza.", nameof(estado));
        }

        Id = id;
        Numero = numero;
        EdicionId = edicionId;
        OrigenId = origenId;
        DestinoId = destinoId;
        Cantidad = cantidad;
        Estado = estado;
        Motivo = motivo;
        MotivoDeCierre = motivoDeCierre;
        PedidoId = pedidoId;
        PedidoAnulado = pedidoAnulado;
        FechaEstimada = fechaEstimada;
        EstimadaPorConfirmar = estimadaPorConfirmar;
        Guia = guia;
        SolicitadoPor = solicitadoPor;
        SolicitadoEn = solicitadoEn;
        AprobadoEn = aprobadoEn;
        DespachadoEn = despachadoEn;
        RecibidoEn = recibidoEn;
        CerradoEn = cerradoEn;
        AlertaDeDemoraEn = alertaDeDemoraEn;
        Version = version;
    }

    public Guid Id { get; }

    /// <summary>El número que ven las pantallas («TR-0042»): lo genera la base al guardar (D09); cero mientras no se guardó.</summary>
    public long Numero { get; private set; }

    public Guid EdicionId { get; }

    public Guid OrigenId { get; }

    public Guid DestinoId { get; }

    public int Cantidad { get; }

    public EstadoDeTraslado Estado { get; private set; }

    /// <summary>Texto libre de un empleado (por qué se solicita o se rechaza): nunca a la auditoría ni a los avisos.</summary>
    public string? Motivo { get; private set; }

    /// <summary>Texto libre de un empleado (por qué se cancela).</summary>
    public string? MotivoDeCierre { get; private set; }

    public Guid? PedidoId { get; }

    public bool EsVinculado => PedidoId is not null;

    /// <summary>El pedido se anuló con el traslado en tránsito: al recibirse, sus unidades quedan libres (TRA-17, TRA-19).</summary>
    public bool PedidoAnulado { get; private set; }

    public DateOnly? FechaEstimada { get; }

    public bool EstimadaPorConfirmar { get; }

    public string? Guia { get; private set; }

    /// <summary>El responsable; nulo cuando lo genera el sistema o tras la depuración de los 2 años.</summary>
    public Guid? SolicitadoPor { get; }

    public DateTimeOffset SolicitadoEn { get; }

    public DateTimeOffset? AprobadoEn { get; private set; }

    public DateTimeOffset? DespachadoEn { get; private set; }

    public DateTimeOffset? RecibidoEn { get; private set; }

    public DateTimeOffset? CerradoEn { get; private set; }

    public DateTimeOffset? AlertaDeDemoraEn { get; }

    public long Version { get; private set; }

    /// <summary>Sin los textos libres de un empleado: un registro de diagnóstico nunca debe volcarlos.</summary>
    public override string ToString() => $"{nameof(Traslado)} {{ Id = {Id}, Estado = {Estado} }}";

    /// <summary>Una solicitud de reposición (sin pedido) hecha por una persona del personal (TRA-01); nace solicitada.</summary>
    public static Traslado SolicitarReposicion(Guid id, Guid edicionId, Guid origenId, Guid destinoId, int cantidad, string? motivo, Guid solicitadoPor, DateTimeOffset ahora) =>
        solicitadoPor == Guid.Empty
            ? throw new ArgumentException("Una solicitud tiene un usuario responsable.", nameof(solicitadoPor))
            : new Traslado(id, 0, edicionId, origenId, destinoId, cantidad, EstadoDeTraslado.Solicitado, motivo, null, null, false, null, false, null, solicitadoPor, ahora, null, null, null, null, null, 0);

    /// <summary>Un traslado vinculado a un pedido de recojo, que genera el sistema al confirmar el pago (SEL-03, TRA-21): la estimación de llegada queda fijada con la ruta de ese momento.</summary>
    public static Traslado VincularAUnPedido(Guid id, Guid edicionId, Guid origenId, Guid destinoId, int cantidad, Guid pedidoId, EstimacionDeLlegada estimacion, Guid? solicitadoPor, DateTimeOffset ahora) =>
        pedidoId == Guid.Empty
            ? throw new ArgumentException("Un traslado vinculado necesita su pedido.", nameof(pedidoId))
            : new Traslado(id, 0, edicionId, origenId, destinoId, cantidad, EstadoDeTraslado.Solicitado, null, null, pedidoId, false, estimacion.Fecha, estimacion.PorConfirmar, null, solicitadoPor, ahora, null, null, null, null, null, 0);

    /// <summary>Rehidrata un traslado guardado.</summary>
    public static Traslado Rehidratar(
        Guid id,
        long numero,
        Guid edicionId,
        Guid origenId,
        Guid destinoId,
        int cantidad,
        EstadoDeTraslado estado,
        string? motivo,
        string? motivoDeCierre,
        Guid? pedidoId,
        bool pedidoAnulado,
        DateOnly? fechaEstimada,
        bool estimadaPorConfirmar,
        string? guia,
        Guid? solicitadoPor,
        DateTimeOffset solicitadoEn,
        DateTimeOffset? aprobadoEn,
        DateTimeOffset? despachadoEn,
        DateTimeOffset? recibidoEn,
        DateTimeOffset? cerradoEn,
        DateTimeOffset? alertaDeDemoraEn,
        long version) =>
        new(id, numero, edicionId, origenId, destinoId, cantidad, estado, motivo, motivoDeCierre, pedidoId, pedidoAnulado, fechaEstimada, estimadaPorConfirmar, guia, solicitadoPor, solicitadoEn, aprobadoEn, despachadoEn, recibidoEn, cerradoEn, alertaDeDemoraEn, version);

    /// <summary>La base asigna el número al guardar un traslado nuevo.</summary>
    public void AsignarNumero(long numero)
    {
        if (numero <= 0 || Numero != 0)
        {
            throw new InvalidOperationException("El número se asigna una sola vez, al guardar.");
        }

        Numero = numero;
    }

    /// <summary>solicitado → aprobado (TRA-04).</summary>
    public void Aprobar(DateTimeOffset ahora)
    {
        Exigir(EstadoDeTraslado.Solicitado);
        Estado = EstadoDeTraslado.Aprobado;
        AprobadoEn = ahora;
        Version++;
    }

    /// <summary>solicitado → rechazado con motivo (TRA-05); uno vinculado no se rechaza (TRA-16).</summary>
    public void Rechazar(string? motivoDeCierre, DateTimeOffset ahora)
    {
        ExigirTexto(motivoDeCierre);
        if (EsVinculado)
        {
            throw new TrasladoVinculadoNoSeRechazaException();
        }

        Exigir(EstadoDeTraslado.Solicitado);
        Estado = EstadoDeTraslado.Rechazado;
        MotivoDeCierre = motivoDeCierre;
        CerradoEn = ahora;
        Version++;
    }

    /// <summary>solicitado o aprobado → cancelado con motivo, por el origen o la administración (TRA-18); uno vinculado no se cancela a mano: solo al anularse su pedido (<see cref="AnularPorPedido"/>).</summary>
    public void Cancelar(string? motivoDeCierre, DateTimeOffset ahora)
    {
        ExigirTexto(motivoDeCierre);
        if (EsVinculado)
        {
            throw new TrasladoVinculadoNoSeRechazaException();
        }

        if (Estado is not (EstadoDeTraslado.Solicitado or EstadoDeTraslado.Aprobado))
        {
            throw new TransicionDeTrasladoInvalidaException();
        }

        Estado = EstadoDeTraslado.Cancelado;
        MotivoDeCierre = motivoDeCierre;
        CerradoEn = ahora;
        Version++;
    }

    /// <summary>aprobado → en tránsito (TRA-07): el stock ya salió del origen.</summary>
    public void Despachar(DateTimeOffset ahora)
    {
        Exigir(EstadoDeTraslado.Aprobado);
        Estado = EstadoDeTraslado.EnTransito;
        DespachadoEn = ahora;
        Version++;
    }

    /// <summary>en tránsito → recibido (TRA-08).</summary>
    public void Recibir(DateTimeOffset ahora)
    {
        Exigir(EstadoDeTraslado.EnTransito);
        Estado = EstadoDeTraslado.Recibido;
        RecibidoEn = ahora;
        CerradoEn = ahora;
        Version++;
    }

    /// <summary>
    /// La anulación del pedido sobre su traslado (TRA-15, TRA-19): solicitado o aprobado pasa a cancelado con el motivo «pedido anulado»; el que está en tránsito queda marcado de pedido anulado (al recibirse, sus unidades quedan libres); uno ya
    /// cerrado o ya marcado no cambia. <b>Nunca falla</b>: anular un pedido es una operación con reembolso (D02).
    /// </summary>
    public EfectoDeLaAnulacionDelPedido AnularPorPedido(DateTimeOffset ahora)
    {
        if (!EsVinculado)
        {
            throw new InvalidOperationException("Solo un traslado vinculado a un pedido se cancela por la anulación de su pedido.");
        }

        switch (Estado)
        {
            case EstadoDeTraslado.Solicitado or EstadoDeTraslado.Aprobado:
                Estado = EstadoDeTraslado.Cancelado;
                MotivoDeCierre = MotivoPedidoAnulado;
                CerradoEn = ahora;
                Version++;
                return EfectoDeLaAnulacionDelPedido.Cancelado;

            case EstadoDeTraslado.EnTransito when !PedidoAnulado:
                PedidoAnulado = true;
                Version++;
                return EfectoDeLaAnulacionDelPedido.MarcadoDePedidoAnulado;

            default:
                return EfectoDeLaAnulacionDelPedido.SinCambios;
        }
    }

    /// <summary>El motivo con el que la anulación de un pedido cancela su traslado (TRA-15).</summary>
    public const string MotivoPedidoAnulado = "pedido-anulado";

    /// <summary>Registra o cambia la guía de un traslado aprobado o en tránsito sin cambiar su estado (TRA-12); devuelve si reemplazó a otra distinta.</summary>
    public bool RegistrarGuia(string guia)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guia);
        if (guia.Length > LargoMaximoDeLaGuia)
        {
            throw new ArgumentException("La guía supera su tope.", nameof(guia));
        }

        if (Estado is not (EstadoDeTraslado.Aprobado or EstadoDeTraslado.EnTransito))
        {
            throw new TransicionDeTrasladoInvalidaException();
        }

        var reemplaza = Guia is not null && Guia != guia;
        Guia = guia;
        Version++;
        return reemplaza;
    }

    private void Exigir(EstadoDeTraslado esperado)
    {
        if (Estado != esperado)
        {
            throw new TransicionDeTrasladoInvalidaException();
        }
    }

    private static void ExigirTexto(string? texto)
    {
        if (texto is { Length: > LargoMaximoDeTexto })
        {
            throw new ArgumentException("El motivo supera su tope.");
        }
    }
}

/// <summary>La fecha de llegada que se guarda al crear un traslado vinculado (TRA-21): calculada con el plazo de su ruta en ese momento y sin cambiar si la ruta cambia después, o «por confirmar» si la ruta no está definida.</summary>
public readonly record struct EstimacionDeLlegada(DateOnly? Fecha, bool PorConfirmar)
{
    /// <summary>La estimación con el plazo (en días) de la ruta de hoy, o «por confirmar» si no está definida.</summary>
    public static EstimacionDeLlegada Calcular(DateOnly hoy, int? plazoEnDias) =>
        plazoEnDias is > 0 ? new EstimacionDeLlegada(hoy.AddDays(plazoEnDias.Value), false) : new EstimacionDeLlegada(null, true);
}
