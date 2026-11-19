namespace BabelHub.Ecommerce.Domain.Pagos;

public enum TipoDeResultadoDeCobro
{
    Aprobado,
    Rechazado,
    SinRespuesta,
}

/// <summary>Por qué un aprobado de la pasarela no se acepta (D13, AM11): no coincide con lo que se cobró.</summary>
public enum MotivoDeDuda
{
    /// <summary>La referencia opaca no corresponde al intento: un cargo sin intento coincidente (solo alerta, nunca un reembolso automático).</summary>
    ReferenciaDistinta,

    MonedaDistinta,

    ImporteDistinto,
}

/// <summary>
/// Lo que la pasarela respondió, ya traducido a los tipos propios (capa anticorrupción, D13): <c>Cobrar</c> y <c>ConsultarEstado</c> devuelven el mismo tipo. Un aprobado solo se acepta tras
/// <see cref="Validar"/>. No guarda la respuesta cruda ni su texto.
/// </summary>
public sealed class ResultadoDeCobro
{
    private ResultadoDeCobro(TipoDeResultadoDeCobro tipo, string? referenciaDePasarela, Importe? importe, string? moneda, Guid? referenciaOpaca, MotivoDeRechazo? motivo)
    {
        Tipo = tipo;
        ReferenciaDePasarela = referenciaDePasarela;
        Importe = importe;
        Moneda = moneda;
        ReferenciaOpaca = referenciaOpaca;
        Motivo = motivo;
    }

    public TipoDeResultadoDeCobro Tipo { get; }

    public string? ReferenciaDePasarela { get; }

    public Importe? Importe { get; }

    public string? Moneda { get; }

    public Guid? ReferenciaOpaca { get; }

    public MotivoDeRechazo? Motivo { get; }

    public static ResultadoDeCobro Aprobado(string referenciaDePasarela, Importe importe, string moneda, Guid referenciaOpaca)
    {
        if (string.IsNullOrWhiteSpace(referenciaDePasarela) || string.IsNullOrWhiteSpace(moneda) || referenciaOpaca == Guid.Empty)
        {
            throw new ArgumentException("Un aprobado trae la referencia del cargo, la moneda y la referencia opaca del intento.");
        }

        return new ResultadoDeCobro(TipoDeResultadoDeCobro.Aprobado, referenciaDePasarela.Trim(), importe, moneda.Trim(), referenciaOpaca, null);
    }

    public static ResultadoDeCobro Rechazado(MotivoDeRechazo motivo) => new(TipoDeResultadoDeCobro.Rechazado, null, null, null, null, motivo);

    public static ResultadoDeCobro SinRespuesta() => new(TipoDeResultadoDeCobro.SinRespuesta, null, null, null, null, null);

    /// <summary>
    /// Compara la respuesta con el intento. Un aprobado se acepta solo si la referencia opaca, la moneda y el importe coinciden; si no, es dudoso: se trata como <c>SinRespuesta</c> y
    /// la Aplicación registra un evento de seguridad. Los demás resultados pasan tal cual.
    /// </summary>
    public ResultadoValidado Validar(IntentoDeCobro intento)
    {
        ArgumentNullException.ThrowIfNull(intento);
        if (Tipo != TipoDeResultadoDeCobro.Aprobado)
        {
            return new ResultadoValidado(Tipo, null, Motivo, null);
        }

        if (ReferenciaOpaca != intento.ReferenciaOpaca)
        {
            return Dudoso(MotivoDeDuda.ReferenciaDistinta);
        }

        if (!string.Equals(Moneda, Pagos.Importe.Moneda, StringComparison.Ordinal))
        {
            return Dudoso(MotivoDeDuda.MonedaDistinta);
        }

        if (Importe != intento.Importe)
        {
            return Dudoso(MotivoDeDuda.ImporteDistinto);
        }

        return new ResultadoValidado(TipoDeResultadoDeCobro.Aprobado, ReferenciaDePasarela, null, null);
    }

    /// <summary>Sin la referencia del cargo.</summary>
    public override string ToString() => $"ResultadoDeCobro {{ Tipo = {Tipo} }}";

    private static ResultadoValidado Dudoso(MotivoDeDuda duda) => new(TipoDeResultadoDeCobro.SinRespuesta, null, null, duda);
}

/// <summary>El resultado ya comparado con el intento: <c>Tipo</c> nunca es <c>Aprobado</c> si hay una duda.</summary>
public sealed record ResultadoValidado(TipoDeResultadoDeCobro Tipo, string? ReferenciaDePasarela, MotivoDeRechazo? Motivo, MotivoDeDuda? Duda)
{
    /// <summary>Sin la referencia del cargo en la pasarela (PAG-30, SEG-10): solo el tipo, la duda y el motivo de la lista cerrada.</summary>
    public override string ToString() => $"ResultadoValidado {{ Tipo = {Tipo}, Duda = {Duda}, Motivo = {Motivo} }}";

    public bool EsDudoso => Duda is not null;

    /// <summary>Hay un cargo con la referencia del intento pero otro importe o moneda: se alerta para conciliar a mano (R02). Un cargo sin intento coincidente solo alerta.</summary>
    public bool RequiereConciliacionManual => Duda is MotivoDeDuda.ImporteDistinto or MotivoDeDuda.MonedaDistinta;
}
