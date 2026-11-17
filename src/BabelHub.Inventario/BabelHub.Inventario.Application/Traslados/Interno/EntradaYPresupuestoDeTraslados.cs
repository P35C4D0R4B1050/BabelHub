using System.Text.RegularExpressions;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Traslados;

namespace BabelHub.Inventario.Application.Traslados.Interno;

/// <summary>Las reglas de forma comunes (cada error lleva el campo y una clave, nunca el valor recibido: SEG-01). Los rangos de negocio los decide el Dominio.</summary>
internal static partial class ReglasDeEntradaDeTraslados
{
    [GeneratedRegex(@"^[a-z0-9-]{2,20}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormaDelCodigo();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/-]{2,39}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormaDeLaGuia();

    public static void Identificador(List<ErrorDeCampo> errores, string campo, Guid valor)
    {
        if (valor == Guid.Empty)
        {
            errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.Obligatorio));
        }
    }

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

    /// <summary>
    /// El motivo (de la solicitud, del rechazo o de la cancelación): hasta 200 caracteres y sin caracteres de control; es texto de un empleado y nunca entra a la auditoría, a los avisos ni a los registros técnicos (D07).
    /// <paramref name="obligatorio"/> exige que traiga texto.
    /// </summary>
    public static void Motivo(List<ErrorDeCampo> errores, string campo, string? motivo, bool obligatorio)
    {
        if (motivo is null)
        {
            if (obligatorio)
            {
                errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.Obligatorio));
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(motivo))
        {
            errores.Add(new ErrorDeCampo(campo, obligatorio ? ConstructorDeValidacion.Obligatorio : ConstructorDeValidacion.FormatoInvalido));
        }
        else if (motivo.Length > Traslado.LargoMaximoDeTexto)
        {
            errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.DemasiadoLargo));
        }
        else if (motivo.Any(char.IsControl))
        {
            errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.FormatoInvalido));
        }
    }

    /// <summary>La guía: de 3 a 40 caracteres, letras, dígitos y <c>. _ / -</c> (como «CA-771204»).</summary>
    public static void Guia(List<ErrorDeCampo> errores, string? guia)
    {
        if (string.IsNullOrWhiteSpace(guia))
        {
            errores.Add(new ErrorDeCampo("guia", ConstructorDeValidacion.Obligatorio));
        }
        else if (guia.Length > Traslado.LargoMaximoDeLaGuia)
        {
            errores.Add(new ErrorDeCampo("guia", ConstructorDeValidacion.DemasiadoLargo));
        }
        else if (!FormaDeLaGuia().IsMatch(guia))
        {
            errores.Add(new ErrorDeCampo("guia", ConstructorDeValidacion.FormatoInvalido));
        }
    }

    public static ResultadoDeValidacion Resultado(List<ErrorDeCampo> errores) => errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
}

internal sealed class ValidadorDeSolicitarTraslado : IValidador<SolicitarTrasladoEntrada>
{
    public ResultadoDeValidacion Validar(SolicitarTrasladoEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDeTraslados.Identificador(errores, "edicionId", entrada.EdicionId);
        ReglasDeEntradaDeTraslados.Codigo(errores, "codigoDeOrigen", entrada.CodigoDeOrigen);
        ReglasDeEntradaDeTraslados.Codigo(errores, "codigoDeDestino", entrada.CodigoDeDestino);
        ReglasDeEntradaDeTraslados.Motivo(errores, "motivo", entrada.Motivo, obligatorio: false);
        return ReglasDeEntradaDeTraslados.Resultado(errores);
    }
}

internal sealed class ValidadorDeConsultarOrigenesPosibles : IValidador<ConsultarOrigenesPosiblesEntrada>
{
    public ResultadoDeValidacion Validar(ConsultarOrigenesPosiblesEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDeTraslados.Identificador(errores, "edicionId", entrada.EdicionId);
        ReglasDeEntradaDeTraslados.Codigo(errores, "codigoDeDestino", entrada.CodigoDeDestino);
        return ReglasDeEntradaDeTraslados.Resultado(errores);
    }
}

internal sealed class ValidadorDeTrasladoPorId<TEntrada>(Func<TEntrada, Guid> trasladoId) : IValidador<TEntrada>
{
    public ResultadoDeValidacion Validar(TEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDeTraslados.Identificador(errores, "trasladoId", trasladoId(entrada));
        return ReglasDeEntradaDeTraslados.Resultado(errores);
    }
}

internal sealed class ValidadorDeRechazarTraslado : IValidador<RechazarTrasladoEntrada>
{
    public ResultadoDeValidacion Validar(RechazarTrasladoEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDeTraslados.Identificador(errores, "trasladoId", entrada.TrasladoId);
        ReglasDeEntradaDeTraslados.Motivo(errores, "motivo", entrada.Motivo, obligatorio: true);
        return ReglasDeEntradaDeTraslados.Resultado(errores);
    }
}

internal sealed class ValidadorDeCancelarTraslado : IValidador<CancelarTrasladoEntrada>
{
    public ResultadoDeValidacion Validar(CancelarTrasladoEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDeTraslados.Identificador(errores, "trasladoId", entrada.TrasladoId);
        ReglasDeEntradaDeTraslados.Motivo(errores, "motivo", entrada.Motivo, obligatorio: true);
        return ReglasDeEntradaDeTraslados.Resultado(errores);
    }
}

internal sealed class ValidadorDeRegistrarGuiaDeTraslado : IValidador<RegistrarGuiaDeTrasladoEntrada>
{
    public ResultadoDeValidacion Validar(RegistrarGuiaDeTrasladoEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDeTraslados.Identificador(errores, "trasladoId", entrada.TrasladoId);
        ReglasDeEntradaDeTraslados.Guia(errores, entrada.Guia);
        return ReglasDeEntradaDeTraslados.Resultado(errores);
    }
}

/// <summary>
/// El presupuesto por hora del personal para solicitar traslados (SEG-30, AD24): 30 por usuario, <b>solo</b> en <c>SolicitarTraslado</c> (la generación desde un pedido, el trabajo de fondo y las transiciones no lo consumen). Cuenta
/// el intento aunque se rechace, en una transacción independiente (la del motor de límites). Un almacén de contadores caído falla cerrado (<c>limites-no-disponibles</c>, 503). La respuesta es la uniforme de límite excedido.
/// </summary>
internal sealed class PresupuestoDeTraslados(IControlDeLimites control, IReloj reloj)
{
    public async Task<ErrorDeAplicacion?> Consumir(Guid usuarioId, CancellationToken cancelacion)
    {
        var resultado = await control.Reservar(PoliticasDeLimite.TrasladosDelPersonal, usuarioId.ToString("D"), cancelacion: cancelacion).ConfigureAwait(false);
        return resultado.Estado switch
        {
            EstadoDeLimite.Permitido => null,
            EstadoDeLimite.Excedido => new ErrorDeAplicacion(
                CategoriaDeError.Limite,
                DetalleDeLimiteDeTraslados.ClaveDeLimiteExcedido,
                DetalleDeLimiteDeTraslados.Codificar(new RechazoDePresupuestoDeTraslados(
                    resultado.Motivo ?? PoliticasDeLimite.TrasladosDelPersonal.Valor,
                    Math.Max(1, resultado.EsperaEnSegundos ?? 1),
                    resultado.DisponibleDesde ?? reloj.Ahora.AddSeconds(1)))),
            _ => new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeTraslados.LimitesNoDisponibles),
        };
    }
}
