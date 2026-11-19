namespace BabelHub.Ecommerce.Domain.Reclamaciones;

/// <summary>Los campos de la hoja que se validan, en el orden en que se informan.</summary>
public enum CampoDeHoja
{
    Nombre,
    Documento,
    Domicilio,
    Correo,
    BienContratado,
    MontoReclamado,
    Tipo,
    Detalle,
    PedidoReferencia,
}

/// <summary>Lo que el consumidor escribió, tal cual y sin validar. Nunca sale en un texto de error ni de registro.</summary>
public sealed record EntradaDeHoja(
    string? Nombre,
    string? Documento,
    string? Domicilio,
    string? Correo,
    string? BienContratado,
    decimal? MontoReclamado,
    string? Tipo,
    string? Detalle,
    string? PedidoReferencia)
{
    /// <summary>Sin ningún dato personal.</summary>
    public override string ToString() => "EntradaDeHoja { (redactada) }";
}

/// <summary>Los campos que faltan y los que son inválidos (por largo, formato o rango), sin el valor escrito (REC-04).</summary>
public sealed class ResultadoDeValidacionDeHoja
{
    public ResultadoDeValidacionDeHoja(IReadOnlyList<CampoDeHoja> faltan, IReadOnlyList<CampoDeHoja> invalidos)
    {
        Faltan = faltan;
        Invalidos = invalidos;
    }

    public IReadOnlyList<CampoDeHoja> Faltan { get; }

    public IReadOnlyList<CampoDeHoja> Invalidos { get; }

    public bool EsValida => Faltan.Count == 0 && Invalidos.Count == 0;

    public override string ToString() => $"ResultadoDeValidacionDeHoja {{ Faltan = {Faltan.Count}, Invalidos = {Invalidos.Count} }}";
}

/// <summary>
/// La validación de una hoja (REC-03, REC-04): los datos obligatorios (nombre, documento, domicilio, correo, bien contratado, monto reclamado, tipo y detalle) y los largos máximos. Informa
/// cada campo que falta o es inválido sin repetir lo que se escribió.
/// </summary>
public static class ValidacionDeHoja
{
    public const int LongitudMaximaDelNombre = 120;
    public const int LongitudMaximaDelDocumento = 20;
    public const int LongitudMaximaDelDomicilio = 200;
    public const int LongitudMaximaDelBien = 200;
    public const int LongitudMaximaDelDetalle = 3000;
    public const int LongitudMaximaDeLaReferenciaDelPedido = 40;

    /// <summary>El tope de <c>numeric(12,2)</c>.</summary>
    public const decimal MontoMaximo = 9_999_999_999.99m;

    public static ResultadoDeValidacionDeHoja Validar(EntradaDeHoja entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var faltan = new List<CampoDeHoja>();
        var invalidos = new List<CampoDeHoja>();

        Texto(CampoDeHoja.Nombre, entrada.Nombre, LongitudMaximaDelNombre, obligatorio: true, faltan, invalidos);
        Texto(CampoDeHoja.Documento, entrada.Documento, LongitudMaximaDelDocumento, obligatorio: true, faltan, invalidos);
        Texto(CampoDeHoja.Domicilio, entrada.Domicilio, LongitudMaximaDelDomicilio, obligatorio: true, faltan, invalidos);

        if (string.IsNullOrWhiteSpace(entrada.Correo))
        {
            faltan.Add(CampoDeHoja.Correo);
        }
        else if (!CorreoNormalizado.TryCrear(entrada.Correo, out _))
        {
            invalidos.Add(CampoDeHoja.Correo);
        }

        Texto(CampoDeHoja.BienContratado, entrada.BienContratado, LongitudMaximaDelBien, obligatorio: true, faltan, invalidos);

        if (entrada.MontoReclamado is not { } monto)
        {
            faltan.Add(CampoDeHoja.MontoReclamado);
        }
        else if (monto < 0m || monto > MontoMaximo || decimal.Round(monto, 2, MidpointRounding.ToZero) != monto)
        {
            invalidos.Add(CampoDeHoja.MontoReclamado);
        }

        if (string.IsNullOrWhiteSpace(entrada.Tipo))
        {
            faltan.Add(CampoDeHoja.Tipo);
        }
        else if (!TipoDeHojaExtensiones.TryDesdeTexto(entrada.Tipo, out _))
        {
            invalidos.Add(CampoDeHoja.Tipo);
        }

        Texto(CampoDeHoja.Detalle, entrada.Detalle, LongitudMaximaDelDetalle, obligatorio: true, faltan, invalidos);
        Texto(CampoDeHoja.PedidoReferencia, entrada.PedidoReferencia, LongitudMaximaDeLaReferenciaDelPedido, obligatorio: false, faltan, invalidos);

        return new ResultadoDeValidacionDeHoja(faltan, invalidos);
    }

    private static void Texto(CampoDeHoja campo, string? valor, int maximo, bool obligatorio, List<CampoDeHoja> faltan, List<CampoDeHoja> invalidos)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            if (obligatorio)
            {
                faltan.Add(campo);
            }

            return;
        }

        if (valor.Trim().Length > maximo)
        {
            invalidos.Add(campo);
        }
    }
}
