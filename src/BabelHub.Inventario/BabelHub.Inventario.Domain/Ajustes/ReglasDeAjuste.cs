using BabelHub.Inventario.Domain.Fuentes;

namespace BabelHub.Inventario.Domain.Ajustes;

/// <summary>Los motivos de un ajuste (AJU-02): tres entran por el ajuste manual (merma, daño y corrección de conteo); el ingreso de mercadería y la devolución solo por sus propias operaciones.</summary>
public enum MotivoDeAjuste
{
    Merma,
    Danio,
    CorreccionDeConteo,
    IngresoDeMercaderia,
    Devolucion,
}

/// <summary>Los identificadores estables de los motivos (los de <c>MotivosDeAjuste</c> de <c>Shared</c> y los del <c>CHECK</c> de la tabla; una prueba verifica que coinciden).</summary>
public static class MotivosDeAjuste
{
    public const string Merma = "merma";

    public const string Danio = "danio";

    public const string CorreccionDeConteo = "correccion-de-conteo";

    public const string IngresoDeMercaderia = "ingreso-de-mercaderia";

    public const string Devolucion = "devolucion";

    public static string Identificador(this MotivoDeAjuste motivo) => motivo switch
    {
        MotivoDeAjuste.Merma => Merma,
        MotivoDeAjuste.Danio => Danio,
        MotivoDeAjuste.CorreccionDeConteo => CorreccionDeConteo,
        MotivoDeAjuste.IngresoDeMercaderia => IngresoDeMercaderia,
        MotivoDeAjuste.Devolucion => Devolucion,
        _ => throw new ArgumentOutOfRangeException(nameof(motivo)),
    };

    public static MotivoDeAjuste? DeIdentificador(string? identificador) => identificador switch
    {
        Merma => MotivoDeAjuste.Merma,
        Danio => MotivoDeAjuste.Danio,
        CorreccionDeConteo => MotivoDeAjuste.CorreccionDeConteo,
        IngresoDeMercaderia => MotivoDeAjuste.IngresoDeMercaderia,
        Devolucion => MotivoDeAjuste.Devolucion,
        _ => null,
    };

    /// <summary>Los motivos del ajuste manual (AJU-02): el ingreso y la devolución se crean solo por sus operaciones (AJU-14, AJU-19).</summary>
    public static bool EsManual(this MotivoDeAjuste motivo) => motivo is MotivoDeAjuste.Merma or MotivoDeAjuste.Danio or MotivoDeAjuste.CorreccionDeConteo;
}

/// <summary>Por qué la regla de un ajuste lo rechaza (AJU-03 a AJU-07, AJU-12): la Aplicación lo traduce a su código estable.</summary>
public enum MotivoDeRechazoDeAjuste
{
    /// <summary>Merma o daño con cero, una cantidad negativa o una que aumentaría el stock (AJU-03, AJU-12).</summary>
    CantidadNoPositiva,

    /// <summary>Merma o daño por encima del stock vendible: lo reservado de pedidos en curso no se pierde (AJU-04).</summary>
    CantidadSuperaAlVendible,

    /// <summary>El conteo real es negativo o supera el tope técnico de 1 000 000 de unidades (D08).</summary>
    ConteoFueraDeRango,

    /// <summary>El conteo real es igual al físico registrado: no hay nada que ajustar (AJU-07).</summary>
    SinCambios,

    /// <summary>El conteo real es menor que las unidades reservadas (AJU-06).</summary>
    ConteoMenorQueLoReservado,
}

/// <summary>Lo que decide una regla de ajuste: el movimiento de físico que se aplicaría (con las cantidades anterior y posterior) o el motivo del rechazo. El stock no cambia por decidir.</summary>
public readonly record struct DecisionDeAjuste
{
    private DecisionDeAjuste(bool aceptado, int anterior, int posterior, MotivoDeRechazoDeAjuste? rechazo)
    {
        Aceptado = aceptado;
        CantidadAnterior = anterior;
        CantidadPosterior = posterior;
        Rechazo = rechazo;
    }

    public bool Aceptado { get; }

    /// <summary>El físico antes del ajuste.</summary>
    public int CantidadAnterior { get; }

    /// <summary>El físico que quedaría: el que tendrá la fila tras <c>Aplicar</c>.</summary>
    public int CantidadPosterior { get; }

    /// <summary>El cambio del físico (negativo en una merma o un daño; con signo en una corrección).</summary>
    public int DeltaFisico => CantidadPosterior - CantidadAnterior;

    public MotivoDeRechazoDeAjuste? Rechazo { get; }

    internal static DecisionDeAjuste Con(int anterior, int posterior) => new(true, anterior, posterior, null);

    internal static DecisionDeAjuste Rechazado(int anterior, MotivoDeRechazoDeAjuste motivo) => new(false, anterior, anterior, motivo);
}

/// <summary>
/// Las reglas de cada motivo de ajuste, decididas con el stock <b>ya bloqueado y actualizado</b> (D01, AJU-08): se prueban sin base de datos. La merma y el daño solo restan una cantidad entera positiva y nunca
/// más que el vendible (AJU-03, AJU-04, AJU-12); la corrección recibe el conteo real, calcula la diferencia y se rechaza si es igual al físico o menor que lo reservado (AJU-05 a AJU-07). El Dominio es
/// quien decide los rangos (regla de negocio); el validador de entrada solo comprueba tipo y forma.
/// </summary>
public static class ReglasDeAjuste
{
    /// <summary>Tope técnico de unidades de una fila tras un conteo o un reingreso: evita desbordes del entero y un stock inflado por una entrada errónea (D08, <c>CHECK</c> de la tabla).</summary>
    public const int TopeTecnico = 1_000_000;

    /// <summary>Merma o daño: resta <paramref name="cantidad"/> del físico si es positiva y no supera el vendible (AJU-03, AJU-04, AJU-12).</summary>
    public static DecisionDeAjuste Restar(ExistenciaDeStock existencia, int cantidad)
    {
        if (cantidad <= 0)
        {
            return DecisionDeAjuste.Rechazado(existencia.Fisico, MotivoDeRechazoDeAjuste.CantidadNoPositiva);
        }

        // El vendible ya devolvió las vencidas (Bloquear las liberó): físico menos reservado.
        if (cantidad > existencia.Vendible())
        {
            return DecisionDeAjuste.Rechazado(existencia.Fisico, MotivoDeRechazoDeAjuste.CantidadSuperaAlVendible);
        }

        return DecisionDeAjuste.Con(existencia.Fisico, existencia.Fisico - cantidad);
    }

    /// <summary>Corrección de conteo: el físico pasa a ser el conteo real, que no puede ser negativo ni pasar el tope, igual al físico ni menor que lo reservado (AJU-05, AJU-06, AJU-07).</summary>
    public static DecisionDeAjuste Corregir(ExistenciaDeStock existencia, int conteoReal)
    {
        if (conteoReal < 0 || conteoReal > TopeTecnico)
        {
            return DecisionDeAjuste.Rechazado(existencia.Fisico, MotivoDeRechazoDeAjuste.ConteoFueraDeRango);
        }

        if (conteoReal == existencia.Fisico)
        {
            return DecisionDeAjuste.Rechazado(existencia.Fisico, MotivoDeRechazoDeAjuste.SinCambios);
        }

        // Con el conteo igual a lo reservado se acepta y el vendible queda en 0 (AC-AJU-06).
        if (conteoReal < existencia.Reservado)
        {
            return DecisionDeAjuste.Rechazado(existencia.Fisico, MotivoDeRechazoDeAjuste.ConteoMenorQueLoReservado);
        }

        return DecisionDeAjuste.Con(existencia.Fisico, conteoReal);
    }

    /// <summary>Un ingreso o un reingreso suma <paramref name="cantidad"/> al físico: positiva y sin pasar el tope técnico tras sumar. El rango propio de cada operación lo decide su regla.</summary>
    public static DecisionDeAjuste Sumar(ExistenciaDeStock existencia, int cantidad)
    {
        if (cantidad <= 0)
        {
            return DecisionDeAjuste.Rechazado(existencia.Fisico, MotivoDeRechazoDeAjuste.CantidadNoPositiva);
        }

        if ((long)existencia.Fisico + cantidad > TopeTecnico)
        {
            return DecisionDeAjuste.Rechazado(existencia.Fisico, MotivoDeRechazoDeAjuste.ConteoFueraDeRango);
        }

        return DecisionDeAjuste.Con(existencia.Fisico, existencia.Fisico + cantidad);
    }
}
