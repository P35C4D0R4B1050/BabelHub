using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>
/// Muestra y registra la aceptación de la versión vigente de los textos (T38, WEB-34, WEB-41, R12). La versión que se muestra es un identificador del <b>conjunto</b> de los tres textos
/// (la de cada uno que informa Identidad): el formulario la envía y Identidad la compara con la vigente, de modo que una versión publicada mientras la persona leía se rechaza y se le
/// pide revisarla de nuevo. La web no decide si la aceptación vale: lo decide Identidad.
/// </summary>
public sealed class AceptarTextosLegales(ITextosLegales textos, IAceptacionDeTextosEnIdentidad identidad) : IAceptacionDeTextos
{
    private static readonly TipoDeTextoLegal[] Tipos = [TipoDeTextoLegal.Terminos, TipoDeTextoLegal.Privacidad, TipoDeTextoLegal.Reembolso];

    public async Task<Resultado<VersionParaAceptar>> VersionVigente(CancellationToken cancelacion = default)
    {
        var vigentes = new List<TextoLegal>();
        foreach (var tipo in Tipos)
        {
            var resultado = await textos.Obtener(tipo, cancelacion).ConfigureAwait(false);
            if (!resultado.EsExito)
            {
                // Sin la versión de alguno no se pide aceptar «lo vigente»: no se inventa (WEB-29).
                return Resultado<VersionParaAceptar>.Fallo(resultado.Error!);
            }

            vigentes.Add(resultado.Valor);
        }

        var identificador = string.Join('|', vigentes.Select(t => $"{TiposDeTextoLegal.Clave(t.Tipo)}:{t.Version}"));
        return Resultado<VersionParaAceptar>.Exito(new VersionParaAceptar(identificador, vigentes));
    }

    public async Task<ResultadoDeAceptacion> Aceptar(ActorVerificado actor, string versionMostrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(versionMostrada) || versionMostrada.Length > 200)
        {
            return ResultadoDeAceptacion.VersionCambiada;
        }

        try
        {
            return await identidad.Aceptar(actor.UsuarioId, versionMostrada, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return ResultadoDeAceptacion.NoDisponible;
        }
    }
}
