namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>
/// <see cref="IVersionesVigentesDeLosTextos"/> sobre <c>identidad.versiones_de_textos</c> (IDE-57): lee, sin caché, las mismas versiones con las que <c>AceptarLosTextos</c> compara lo que la web envía. No filtra datos personales y no
/// captura excepciones: si no hay versiones sembradas o la base no responde, lanza.
/// </summary>
internal sealed class VersionesVigentesDeLosTextosDeIdentidad(IVersionesDeTextosVigentes versiones) : IVersionesVigentesDeLosTextos
{
    public async Task<VersionesVigentesDeLosTextos> Consultar(CancellationToken cancelacion = default)
    {
        var (vigentes, desde) = await versiones.ObtenerConVigencia(cancelacion).ConfigureAwait(false);
        return new VersionesVigentesDeLosTextos(vigentes.Terminos, vigentes.Privacidad, vigentes.Reembolso, desde);
    }
}
