namespace BabelHub.Inventario.Domain.Ajustes;

/// <summary>Los largos y juegos de caracteres de los textos libres de un ajuste y de un ingreso (los mismos del <c>CHECK</c> de la tabla).</summary>
public static class LimitesDeTextoDeAjustes
{
    /// <summary>La nota es texto libre de un empleado y no debe llevar datos de clientes: se acota (plan §3).</summary>
    public const int LargoMaximoDeNota = 200;

    /// <summary>La referencia es el código del documento de llegada (guía o factura).</summary>
    public const int LargoMaximoDeReferencia = 60;

    /// <summary>Letras, dígitos, espacio, <c>-</c>, <c>/</c> y <c>.</c> (AJU-13).</summary>
    public static bool EsReferenciaValida(string referencia)
    {
        ArgumentNullException.ThrowIfNull(referencia);
        return referencia.Length is >= 1 and <= LargoMaximoDeReferencia
            && referencia.All(c => (c is >= 'a' and <= 'z') || (c is >= 'A' and <= 'Z') || (c is >= '0' and <= '9') || c is ' ' or '-' or '/' or '.');
    }

    /// <summary>La nota no pasa del largo máximo (el juego de caracteres lo acota la Api: aquí solo el largo que impone la tabla).</summary>
    public static bool EsNotaValida(string nota)
    {
        ArgumentNullException.ThrowIfNull(nota);
        return nota.Length <= LargoMaximoDeNota;
    }
}

/// <summary>
/// Un ajuste de stock, inmutable (spec §5): motivo, edición, fuente, usuario responsable, cantidades <b>anterior y posterior</b> del físico y una nota opcional (AJU-01). Los constructores nombrados
/// cumplen las mismas condiciones que los <c>CHECK</c> de la tabla: la merma y el daño bajan el físico, el ingreso y la devolución lo suben y la corrección deja el físico en el conteo real, distinto del anterior. El
/// <paramref name="UsuarioId"/> es nulo solo tras la depuración de los 2 años (AJU-21). Una violación es un error de programación (la regla la decidió antes <see cref="ReglasDeAjuste"/>).
/// </summary>
public sealed record Ajuste
{
    private Ajuste(
        Guid id,
        Guid fuenteId,
        Guid edicionId,
        MotivoDeAjuste motivo,
        int cantidadAnterior,
        int cantidadPosterior,
        int? conteoReal,
        string? nota,
        Guid? usuarioId,
        DateTimeOffset fecha,
        Guid? devolucionId,
        Guid? pedidoId)
    {
        if (id == Guid.Empty || fuenteId == Guid.Empty || edicionId == Guid.Empty)
        {
            throw new ArgumentException("Un ajuste necesita su identificador, la fuente y la edición.");
        }

        if (cantidadAnterior < 0 || cantidadPosterior < 0 || cantidadAnterior > ReglasDeAjuste.TopeTecnico || cantidadPosterior > ReglasDeAjuste.TopeTecnico)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidadPosterior), "Las cantidades de un ajuste van de cero al tope técnico.");
        }

        if (nota is not null && !LimitesDeTextoDeAjustes.EsNotaValida(nota))
        {
            throw new ArgumentException("La nota supera el largo máximo.", nameof(nota));
        }

        switch (motivo)
        {
            case MotivoDeAjuste.Merma or MotivoDeAjuste.Danio when cantidadPosterior >= cantidadAnterior || conteoReal is not null:
                throw new ArgumentException("La merma y el daño solo restan.", nameof(motivo));
            case MotivoDeAjuste.IngresoDeMercaderia or MotivoDeAjuste.Devolucion when cantidadPosterior <= cantidadAnterior || conteoReal is not null:
                throw new ArgumentException("El ingreso y la devolución solo suman.", nameof(motivo));
            case MotivoDeAjuste.CorreccionDeConteo when conteoReal is not { } conteo || conteo != cantidadPosterior || conteo == cantidadAnterior:
                throw new ArgumentException("La corrección deja el físico en el conteo real, distinto del anterior.", nameof(motivo));
        }

        if ((motivo == MotivoDeAjuste.Devolucion) != (devolucionId is not null) || (motivo == MotivoDeAjuste.Devolucion) != (pedidoId is not null))
        {
            throw new ArgumentException("La devolución y el pedido van solo con el motivo devolución.", nameof(devolucionId));
        }

        if (devolucionId == Guid.Empty || pedidoId == Guid.Empty)
        {
            throw new ArgumentException("Los identificadores de la devolución y del pedido no pueden estar vacíos.", nameof(devolucionId));
        }

        Id = id;
        FuenteId = fuenteId;
        EdicionId = edicionId;
        Motivo = motivo;
        CantidadAnterior = cantidadAnterior;
        CantidadPosterior = cantidadPosterior;
        ConteoReal = conteoReal;
        Nota = nota;
        UsuarioId = usuarioId;
        Fecha = fecha;
        DevolucionId = devolucionId;
        PedidoId = pedidoId;
    }

    /// <summary>Sin la nota ni la referencia: un registro de diagnóstico nunca debe volcar el texto libre de un empleado (AUD-06).</summary>
    public override string ToString() => $"{nameof(Ajuste)} {{ Id = {Id} }}";

    public Guid Id { get; }

    public Guid FuenteId { get; }

    public Guid EdicionId { get; }

    public MotivoDeAjuste Motivo { get; }

    public int CantidadAnterior { get; }

    public int CantidadPosterior { get; }

    /// <summary>Solo en la corrección de conteo: el conteo que dio el usuario.</summary>
    public int? ConteoReal { get; }

    /// <summary>Texto libre de un empleado (dato personal potencial): nunca va a la auditoría ni a los registros; se vacía a los 2 años.</summary>
    public string? Nota { get; }

    /// <summary>El responsable; nulo tras la depuración de los 2 años.</summary>
    public Guid? UsuarioId { get; }

    public DateTimeOffset Fecha { get; }

    /// <summary>Solo con el motivo devolución: la devolución que se reingresó (con la edición, identifica el reingreso: AJU-16).</summary>
    public Guid? DevolucionId { get; }

    /// <summary>Solo con el motivo devolución: el pedido devuelto, para que las unidades vendidas resten lo devuelto (<c>inventario-seleccion-fuente</c> D13).</summary>
    public Guid? PedidoId { get; }

    /// <summary>La diferencia del físico (negativa en una merma o un daño).</summary>
    public int Diferencia => CantidadPosterior - CantidadAnterior;

    /// <summary>Un ajuste manual de merma o daño con lo que decidió <see cref="ReglasDeAjuste.Restar"/>.</summary>
    public static Ajuste Restado(Guid id, Guid fuenteId, Guid edicionId, MotivoDeAjuste motivo, DecisionDeAjuste decision, string? nota, Guid usuarioId, DateTimeOffset fecha)
    {
        if (motivo is not (MotivoDeAjuste.Merma or MotivoDeAjuste.Danio))
        {
            throw new ArgumentException("Solo la merma y el daño restan.", nameof(motivo));
        }

        return Crear(id, fuenteId, edicionId, motivo, decision, null, nota, usuarioId, fecha, null, null);
    }

    /// <summary>Una corrección de conteo con lo que decidió <see cref="ReglasDeAjuste.Corregir"/>.</summary>
    public static Ajuste Corregido(Guid id, Guid fuenteId, Guid edicionId, DecisionDeAjuste decision, int conteoReal, string? nota, Guid usuarioId, DateTimeOffset fecha) =>
        Crear(id, fuenteId, edicionId, MotivoDeAjuste.CorreccionDeConteo, decision, conteoReal, nota, usuarioId, fecha, null, null);

    /// <summary>El ajuste de un ingreso de mercadería: suma al físico; su referencia vive solo en <c>ingresos</c>.</summary>
    public static Ajuste Ingresado(Guid id, Guid fuenteId, Guid edicionId, DecisionDeAjuste decision, string? nota, Guid usuarioId, DateTimeOffset fecha) =>
        Crear(id, fuenteId, edicionId, MotivoDeAjuste.IngresoDeMercaderia, decision, null, nota, usuarioId, fecha, null, null);

    /// <summary>El ajuste de un reingreso por devolución: suma al físico de la sucursal receptora, identificado por la devolución y la edición.</summary>
    public static Ajuste Devuelto(Guid id, Guid fuenteId, Guid edicionId, DecisionDeAjuste decision, Guid usuarioId, DateTimeOffset fecha, Guid devolucionId, Guid pedidoId) =>
        Crear(id, fuenteId, edicionId, MotivoDeAjuste.Devolucion, decision, null, null, usuarioId, fecha, devolucionId, pedidoId);

    /// <summary>Rehidrata un ajuste guardado (el usuario y la nota pueden ser nulos si ya se depuraron).</summary>
    public static Ajuste Rehidratar(
        Guid id,
        Guid fuenteId,
        Guid edicionId,
        MotivoDeAjuste motivo,
        int cantidadAnterior,
        int cantidadPosterior,
        int? conteoReal,
        string? nota,
        Guid? usuarioId,
        DateTimeOffset fecha,
        Guid? devolucionId,
        Guid? pedidoId) =>
        new(id, fuenteId, edicionId, motivo, cantidadAnterior, cantidadPosterior, conteoReal, nota, usuarioId, fecha, devolucionId, pedidoId);

    private static Ajuste Crear(Guid id, Guid fuenteId, Guid edicionId, MotivoDeAjuste motivo, DecisionDeAjuste decision, int? conteoReal, string? nota, Guid usuarioId, DateTimeOffset fecha, Guid? devolucionId, Guid? pedidoId)
    {
        if (!decision.Aceptado)
        {
            throw new InvalidOperationException("Un ajuste rechazado no se crea.");
        }

        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("Un ajuste nuevo tiene un usuario responsable.", nameof(usuarioId));
        }

        return new Ajuste(id, fuenteId, edicionId, motivo, decision.CantidadAnterior, decision.CantidadPosterior, conteoReal, nota, usuarioId, fecha, devolucionId, pedidoId);
    }
}
