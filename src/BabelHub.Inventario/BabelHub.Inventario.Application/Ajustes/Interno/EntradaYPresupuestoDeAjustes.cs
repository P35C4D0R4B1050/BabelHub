using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Ajustes;

namespace BabelHub.Inventario.Application.Ajustes.Interno;

/// <summary>Los topes de forma de las entradas del módulo (los rangos de negocio los decide el Dominio).</summary>
internal static class LimitesDeEntradaDeAjustes
{
    public const int MaximoDeLineasDeReingreso = 20;

    public const int MaximoDeTamanoDePagina = 100;

    public const int MaximoDeLimiteDeIngresos = 50;

    /// <summary>El tamaño de la lista de pedidos que reservan las unidades en el rechazo de un conteo (AJU-06).</summary>
    public const int MaximoDePedidosEnElRechazo = 20;

    /// <summary>La cantidad por línea de un reingreso (de forma: AJU-11 deja el rango como validación).</summary>
    public const int MaximaCantidadPorLineaDeReingreso = ReglasDeIngreso.CantidadMaxima;
}

/// <summary>Las reglas de forma comunes (cada error lleva el campo y una clave, nunca el valor recibido: SEG-01).</summary>
internal static partial class ReglasDeEntradaDeAjustes
{
    [GeneratedRegex(@"^[a-z0-9-]{2,20}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormaDelCodigo();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{16,80}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormaDeLaClave();

    public static void Codigo(List<ErrorDeCampo> errores, string campo, string? codigo)
    {
        if (string.IsNullOrEmpty(codigo))
        {
            errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.Obligatorio));
        }
        else if (!FormaDelCodigo().IsMatch(codigo))
        {
            errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.FormatoInvalido));
        }
    }

    public static void Clave(List<ErrorDeCampo> errores, string? clave)
    {
        if (string.IsNullOrEmpty(clave))
        {
            errores.Add(new ErrorDeCampo("idempotencyKey", ConstructorDeValidacion.Obligatorio));
        }
        else if (!FormaDeLaClave().IsMatch(clave))
        {
            errores.Add(new ErrorDeCampo("idempotencyKey", ConstructorDeValidacion.FormatoInvalido));
        }
    }

    /// <summary>La nota es opcional, de hasta 200 caracteres y sin caracteres de control (los saltos de línea tampoco: es una nota corta).</summary>
    public static void Nota(List<ErrorDeCampo> errores, string? nota)
    {
        if (nota is null)
        {
            return;
        }

        if (nota.Length > LimitesDeTextoDeAjustes.LargoMaximoDeNota)
        {
            errores.Add(new ErrorDeCampo("nota", ConstructorDeValidacion.DemasiadoLargo));
        }
        else if (nota.Any(char.IsControl))
        {
            errores.Add(new ErrorDeCampo("nota", ConstructorDeValidacion.FormatoInvalido));
        }
    }

    /// <summary>La referencia es opcional y de hasta 60 caracteres; el juego de caracteres lo decide el Dominio (<see cref="ReglasDeIngreso"/>).</summary>
    public static void Referencia(List<ErrorDeCampo> errores, string? referencia)
    {
        if (referencia is null)
        {
            return;
        }

        if (referencia.Length > LimitesDeTextoDeAjustes.LargoMaximoDeReferencia)
        {
            errores.Add(new ErrorDeCampo("referencia", ConstructorDeValidacion.DemasiadoLargo));
        }
        else if (string.IsNullOrWhiteSpace(referencia))
        {
            errores.Add(new ErrorDeCampo("referencia", ConstructorDeValidacion.FormatoInvalido));
        }
    }

    public static ResultadoDeValidacion Resultado(List<ErrorDeCampo> errores) => errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);

    /// <summary>La huella SHA-256 de una carga canónica (incluye la operación, así una clave no se confunde entre un ajuste y un ingreso).</summary>
    public static string Huella(string canonica) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonica)));
}

/// <summary>
/// El presupuesto por hora del personal para ajustes e ingresos (SEG-30, AD24, D06): 60 por usuario. Cuenta el intento aunque se rechace, en una transacción independiente (la del motor de límites), y una repetición
/// idempotente también cuenta. Un almacén de contadores caído falla cerrado (<c>limites-no-disponibles</c>, 503). La respuesta es la uniforme de límite excedido y el evento de seguridad lo publica el motor.
/// </summary>
internal sealed class PresupuestoDelPersonal(IControlDeLimites control, IReloj reloj)
{
    public async Task<ErrorDeAplicacion?> Consumir(Guid usuarioId, CancellationToken cancelacion)
    {
        var resultado = await control.Reservar(PoliticasDeLimite.AjustesDelPersonal, usuarioId.ToString("D"), cancelacion: cancelacion).ConfigureAwait(false);
        return resultado.Estado switch
        {
            EstadoDeLimite.Permitido => null,
            EstadoDeLimite.Excedido => new ErrorDeAplicacion(
                CategoriaDeError.Limite,
                DetalleDeLimiteDeAjustes.ClaveDeLimiteExcedido,
                DetalleDeLimiteDeAjustes.Codificar(new RechazoDePresupuestoDelPersonal(
                    resultado.Motivo ?? PoliticasDeLimite.AjustesDelPersonal.Valor,
                    Math.Max(1, resultado.EsperaEnSegundos ?? 1),
                    resultado.DisponibleDesde ?? reloj.Ahora.AddSeconds(1)))),
            _ => new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeAjustes.LimitesNoDisponibles),
        };
    }
}
