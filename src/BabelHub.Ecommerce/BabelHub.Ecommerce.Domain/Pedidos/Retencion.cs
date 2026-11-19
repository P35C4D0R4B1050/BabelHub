namespace BabelHub.Ecommerce.Domain.Pedidos;

/// <summary>
/// La retención de los datos personales de un pedido (PED-34, D08): el contacto de entrega y la identidad del personal que inició una anulación o una entrega se anonimizan a los <b>dos años calendario, en hora de Lima</b>, desde el último cambio del pedido a entregado o anulado.
/// Lima no tiene horario de verano (UTC−5): se compara la <b>fecha de Lima</b> de ese último cambio, más dos años, con la fecha de hoy en Lima; el 29 de febrero cumple el 28 de febrero.
/// </summary>
public static class PoliticaDeRetencion
{
    public static readonly TimeSpan DesfaseDeLima = TimeSpan.FromHours(-5);

    public const int Anios = 2;

    public static DateOnly FechaEnLima(DateTimeOffset instante) => DateOnly.FromDateTime(instante.ToOffset(DesfaseDeLima).DateTime);

    /// <summary>Vence cuando la fecha de hoy en Lima alcanza el segundo aniversario de la fecha de Lima del último cambio a entregado o anulado.</summary>
    public static bool EstaVencida(DateTimeOffset fechaEstadoFinal, DateOnly hoyEnLima) => hoyEnLima >= FechaEnLima(fechaEstadoFinal).AddYears(Anios);
}

public sealed partial class Pedido
{
    /// <summary>Cuándo se anonimizó el contacto (al cancelar la cuenta o por la retención); <see langword="null"/> si no.</summary>
    public DateTimeOffset? ContactoAnonimizadoEn { get; private set; }

    public bool ContactoEstaAnonimizado => ContactoAnonimizadoEn is not null;

    /// <summary>
    /// Anonimiza el nombre, el teléfono y la dirección de entrega y conserva todo lo demás (PED-33, PED-34): las líneas, los importes, las fechas y el identificador opaco de la cuenta. <b>Idempotente</b>: un pedido ya anonimizado o sin contacto (recojo o digital) termina sin error y sin cambios.
    /// Devuelve <see langword="true"/> solo si cambió algo.
    /// </summary>
    public bool AnonimizarContacto(DateTimeOffset ahora)
    {
        if (Contacto is null)
        {
            return false;
        }

        Contacto = null;
        ContactoAnonimizadoEn = ahora;
        return true;
    }

    /// <summary>El pedido ya cumplió los dos años desde su último cambio a entregado o anulado (<see cref="PoliticaDeRetencion"/>): también uno de recojo o digital, por la identidad del iniciador de sus operaciones.</summary>
    public bool EstaVencidoParaDepurar(DateOnly hoyEnLima) => FechaEstadoFinal is { } final && PoliticaDeRetencion.EstaVencida(final, hoyEnLima);

    /// <summary>Anonimiza el contacto si el pedido está vencido para depurar (PED-34); repetirlo no cambia nada.</summary>
    public bool AnonimizarPorRetencion(DateOnly hoyEnLima, DateTimeOffset ahora) => EstaVencidoParaDepurar(hoyEnLima) && AnonimizarContacto(ahora);
}
