namespace BabelHub.Inventario.Domain.Fuentes;

/// <summary>El estado de stock de una edición en una fuente, derivado de su vendible (FUE-18).</summary>
public enum EstadoDeStock
{
    /// <summary>Vendible 0.</summary>
    SinStock,

    /// <summary>Vendible de 1 a 2.</summary>
    Bajo,

    /// <summary>Vendible de 3 o más.</summary>
    Normal,
}

/// <summary>Por qué se rechaza un movimiento de stock: lo que dejaría un estado imposible o una cantidad inválida.</summary>
public enum MotivoDeRechazoDeStock
{
    /// <summary>La cantidad de una transición nombrada no es positiva.</summary>
    CantidadNoPositiva,

    /// <summary>El stock físico quedaría negativo (o no cabría en el tipo).</summary>
    FisicoNegativo,

    /// <summary>Las unidades reservadas quedarían negativas.</summary>
    ReservadoNegativo,

    /// <summary>Las reservas superarían al físico: el vendible quedaría negativo (FUE-04).</summary>
    ReservadoSuperaAlFisico,
}

/// <summary>El resultado de aplicar un movimiento: la existencia nueva, o el motivo del rechazo (la existencia no cambia).</summary>
public readonly record struct ResultadoDeMovimientoDeStock
{
    private ResultadoDeMovimientoDeStock(bool aplicado, ExistenciaDeStock existencia, MotivoDeRechazoDeStock? motivo)
    {
        Aplicado = aplicado;
        Existencia = existencia;
        Motivo = motivo;
    }

    public bool Aplicado { get; }

    /// <summary>La existencia posterior si se aplicó; la misma de antes si se rechazó.</summary>
    public ExistenciaDeStock Existencia { get; }

    public MotivoDeRechazoDeStock? Motivo { get; }

    internal static ResultadoDeMovimientoDeStock Con(ExistenciaDeStock nueva) => new(true, nueva, null);

    internal static ResultadoDeMovimientoDeStock Rechazado(ExistenciaDeStock sinCambios, MotivoDeRechazoDeStock motivo) => new(false, sinCambios, motivo);
}

/// <summary>
/// El stock de una edición impresa en una fuente: unidades físicas y unidades reservadas (FUE-02). El invariante es
/// <c>0 ≤ reservado ≤ físico</c>, y de él sale que el vendible nunca es negativo (FUE-04, RNF01). La regla general
/// <see cref="Aplicar"/> es la que espeja, en la Infraestructura, la única primitiva de escritura de <c>stock</c> (un <c>UPDATE</c> condicional
/// con la misma condición) y el <c>CHECK</c> de la tabla; una prueba de paridad compara las tres (inventario-fuentes D01).
/// </summary>
public readonly record struct ExistenciaDeStock
{
    public ExistenciaDeStock(int fisico, int reservado)
    {
        if (fisico < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fisico), "El stock físico no puede ser negativo.");
        }

        if (reservado < 0 || reservado > fisico)
        {
            throw new ArgumentOutOfRangeException(nameof(reservado), "Las unidades reservadas van de cero al stock físico.");
        }

        Fisico = fisico;
        Reservado = reservado;
    }

    /// <summary>Una fuente sin movimientos: físico 0 y reservado 0 (la fila ausente equivale a esto, FUE-06).</summary>
    public static ExistenciaDeStock Vacia => new(0, 0);

    public int Fisico { get; }

    public int Reservado { get; }

    /// <summary>
    /// Stock vendible: <c>max(0, físico − reservado + vencidas)</c> (FUE-03, D02). Las <paramref name="vencidas"/> son las unidades de reservas
    /// pendientes ya vencidas que nadie procesó y que <b>siguen sumadas</b> en <see cref="Reservado"/>, así que se devuelven al vendible y no se
    /// descuentan: físico 5, reservado 5 y vencidas 2 da 2; físico 5, reservado 2 y todo vencido da 5.
    /// </summary>
    public int Vendible(int vencidas = 0)
    {
        if (vencidas < 0 || vencidas > Reservado)
        {
            throw new ArgumentOutOfRangeException(nameof(vencidas), "Las vencidas van de cero a las unidades reservadas.");
        }

        return (int)Math.Max(0L, (long)Fisico - Reservado + vencidas);
    }

    /// <summary>El estado de stock según el vendible (FUE-18).</summary>
    public EstadoDeStock Estado(int vencidas = 0) => EstadoDeStockExtensiones.DeVendible(Vendible(vencidas));

    /// <summary>
    /// La regla general: aplica un cambio al físico y al reservado y exige que el resultado cumpla <c>0 ≤ reservado ≤ físico</c>. Cualquier
    /// otro estado se rechaza sin cambiar nada. Es exactamente la condición del <c>UPDATE</c> de la primitiva de escritura.
    /// </summary>
    public ResultadoDeMovimientoDeStock Aplicar(int deltaFisico, int deltaReservado)
    {
        var fisico = (long)Fisico + deltaFisico;
        var reservado = (long)Reservado + deltaReservado;

        if (fisico < 0 || fisico > int.MaxValue)
        {
            // Un físico que no cabe en el tipo no es un estado representable.
            return ResultadoDeMovimientoDeStock.Rechazado(this, MotivoDeRechazoDeStock.FisicoNegativo);
        }

        if (reservado < 0)
        {
            return ResultadoDeMovimientoDeStock.Rechazado(this, MotivoDeRechazoDeStock.ReservadoNegativo);
        }

        if (reservado > fisico)
        {
            return ResultadoDeMovimientoDeStock.Rechazado(this, MotivoDeRechazoDeStock.ReservadoSuperaAlFisico);
        }

        return ResultadoDeMovimientoDeStock.Con(new ExistenciaDeStock((int)fisico, (int)reservado));
    }

    /// <summary>Aparta unidades para una reserva: suben las reservadas; se rechaza si no hay vendible suficiente (ya liberadas las vencidas).</summary>
    public ResultadoDeMovimientoDeStock Apartar(int unidades) => Transicion(unidades, 0, unidades);

    /// <summary>Devuelve unidades apartadas (reserva liberada, expirada o reemplazada): bajan las reservadas.</summary>
    public ResultadoDeMovimientoDeStock DevolverApartado(int unidades) => Transicion(unidades, 0, -unidades);

    /// <summary>Descuenta unidades vendidas y despachadas de una reserva confirmada: bajan el físico y las reservadas por igual.</summary>
    public ResultadoDeMovimientoDeStock Descontar(int unidades) => Transicion(unidades, -unidades, -unidades);

    /// <summary>Suma unidades al físico (ingreso de mercadería o reingreso por devolución).</summary>
    public ResultadoDeMovimientoDeStock Sumar(int unidades) => Transicion(unidades, unidades, 0);

    private ResultadoDeMovimientoDeStock Transicion(int unidades, int deltaFisico, int deltaReservado) =>
        unidades <= 0
            ? ResultadoDeMovimientoDeStock.Rechazado(this, MotivoDeRechazoDeStock.CantidadNoPositiva)
            : Aplicar(deltaFisico, deltaReservado);
}

/// <summary>Cálculo del estado de stock a partir del vendible.</summary>
public static class EstadoDeStockExtensiones
{
    /// <summary>Sin stock con vendible 0, bajo de 1 a 2 y normal desde 3 (FUE-18).</summary>
    public static EstadoDeStock DeVendible(int vendible) => vendible switch
    {
        < 0 => throw new ArgumentOutOfRangeException(nameof(vendible), "El vendible nunca es negativo."),
        0 => EstadoDeStock.SinStock,
        <= 2 => EstadoDeStock.Bajo,
        _ => EstadoDeStock.Normal,
    };
}
