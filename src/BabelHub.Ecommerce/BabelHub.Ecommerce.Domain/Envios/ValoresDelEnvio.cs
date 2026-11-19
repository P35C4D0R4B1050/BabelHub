using System.Diagnostics.CodeAnalysis;

namespace BabelHub.Ecommerce.Domain.Envios;

/// <summary>El estado de un envío (ENV-03). Los códigos son los que guarda la base de datos.</summary>
public enum EstadoDeEnvio
{
    PendienteDeGuia,
    GuiaGenerada,
    EnReparto,
    Entregado,
    Cancelado,
}

public static class CodigosDeEstadoDeEnvio
{
    public static string Codigo(this EstadoDeEnvio estado) => estado switch
    {
        EstadoDeEnvio.PendienteDeGuia => "pendiente_de_guia",
        EstadoDeEnvio.GuiaGenerada => "guia_generada",
        EstadoDeEnvio.EnReparto => "en_reparto",
        EstadoDeEnvio.Entregado => "entregado",
        EstadoDeEnvio.Cancelado => "cancelado",
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };

    public static bool TryDesdeCodigo(string? codigo, out EstadoDeEnvio estado)
    {
        foreach (var candidato in Enum.GetValues<EstadoDeEnvio>())
        {
            if (string.Equals(candidato.Codigo(), codigo, StringComparison.Ordinal))
            {
                estado = candidato;
                return true;
            }
        }

        estado = default;
        return false;
    }
}

/// <summary>
/// El número de guía que entrega el operador (ENV-02): inmutable una vez guardado y de largo y caracteres acotados (SEG-13), porque viene de un tercero y se valida antes de usarlo.
/// Acepta de 6 a 40 letras sin tilde, dígitos y guiones.
/// </summary>
public readonly record struct NumeroDeGuia
{
    public const int LongitudMinima = 6;

    public const int LongitudMaxima = 40;

    private NumeroDeGuia(string valor)
    {
        Valor = valor;
    }

    public string Valor { get; }

    public static bool TryCrear(string? texto, [NotNullWhen(true)] out NumeroDeGuia numero)
    {
        numero = default;
        if (texto is null || texto.Length is < LongitudMinima or > LongitudMaxima)
        {
            return false;
        }

        foreach (var c in texto)
        {
            var valido = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-';
            if (!valido)
            {
                return false;
            }
        }

        // Al menos un carácter alfanumérico, sin guion al principio ni al final: «------» no es un número de guía.
        if (texto[0] == '-' || texto[^1] == '-' || !texto.Any(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        numero = new NumeroDeGuia(texto);
        return true;
    }

    public static NumeroDeGuia Crear(string? texto) =>
        TryCrear(texto, out var numero) ? numero : throw new ArgumentException("El número de guía no tiene un largo o caracteres aceptados.", nameof(texto));

    public override string ToString() => Valor;
}

/// <summary>Una línea impresa del pedido que viaja en el envío (ENV-01). Solo la identidad: las cantidades y los precios los lee Pedidos.</summary>
public readonly record struct LineaDeEnvio(Guid LineaId);

public enum ResultadoDeSolicitudDeGuia
{
    /// <summary>La clave y el arrendamiento quedaron fijados; quien pide puede llamar al operador.</summary>
    Tomada,

    /// <summary>Otra solicitud tiene el arrendamiento vivo (ENV-18).</summary>
    EnCurso,

    EstadoInvalido,
}

public enum ResultadoDeRegistroDeGuia
{
    Registrada,

    /// <summary>Otra solicitud ganó con el mismo número: éxito sin auditar de nuevo.</summary>
    YaRegistrada,

    /// <summary>El envío ya tiene otro número: se rechaza y se alerta.</summary>
    OtroNumero,

    /// <summary>El envío se canceló mientras tanto: la guía queda sin uso en el operador (spec §6).</summary>
    GuiaSinUso,

    EstadoInvalido,
}

public enum ResultadoDeInicioDeDespacho
{
    /// <summary>El marcador de despacho y su arrendamiento quedaron fijados.</summary>
    Iniciado,

    /// <summary>Otra solicitud tiene el arrendamiento vivo: la primera que confirma gana (AC-ENV-16).</summary>
    EnCurso,

    EstadoInvalido,
}

public enum ResultadoDeTransicion
{
    Hecha,

    /// <summary>El envío ya estaba en reparto: éxito sin auditar ni avisar de nuevo.</summary>
    YaEnReparto,

    EstadoInvalido,
}

public enum ResultadoDeCancelacion
{
    Cancelado,

    /// <summary>Ya estaba cancelado: éxito idempotente.</summary>
    YaEstabaCancelado,

    /// <summary>Con el marcador de despacho, en reparto o entregado: no se cancela (ENV-13, ENV-19).</summary>
    Mantenido,
}
