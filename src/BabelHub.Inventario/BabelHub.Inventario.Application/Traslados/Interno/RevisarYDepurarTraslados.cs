using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Traslados.Interno;

/// <summary>Cuántos traslados vinculados sin aprobar se revisan por corrida.</summary>
internal sealed record RevisarTrasladosSinAprobarEntrada(int Maximo = 200);

/// <summary>Cuántos traslados vencieron y se avisaron en una corrida.</summary>
internal sealed record ResultadoDeLaRevision(int Avisados);

/// <summary>
/// La revisión de plazos (TRA-22, AC-TRA-21, solo la mitad del Inventario): cada hora toma con <c>FOR UPDATE SKIP LOCKED</c> y <c>ORDER BY id</c> los traslados vinculados <c>solicitado</c> hace más de 48 horas y sin alerta previa, marca
/// <c>alerta_de_demora_en</c> con <c>UPDATE … WHERE estado = 'solicitado' AND alerta_de_demora_en IS NULL</c> (no avisa de uno aprobado en paralelo) y publica la alerta <c>traslado-vinculado-sin-aprobar</c> (clase técnica, sin identificador
/// del traslado: el listado <c>?vinculado=true&amp;demoraMinimaHoras=48</c> dice cuáles) <b>en la misma transacción</b>. Uno de 47 horas no genera nada y cada traslado avisa una sola vez. El reparto del aviso a la administración
/// central y a logística es del carril A. <b>Exige el actor «sistema»</b>.
/// </summary>
internal sealed class RevisarTrasladosSinAprobar(
    AutorizadorDeTraslados autorizador,
    IRepositorioDeTraslados traslados,
    IAlertas alertas,
    IReloj reloj) : ICasoDeUso<RevisarTrasladosSinAprobarEntrada, ResultadoDeLaRevision>
{
    /// <summary>El plazo de aprobación de un traslado vinculado (TRA-22).</summary>
    public static readonly TimeSpan Plazo = TimeSpan.FromHours(48);

    /// <summary>El motivo del catálogo cerrado de alertas del Inventario.</summary>
    public const string MotivoDeAlerta = "traslado-vinculado-sin-aprobar";

    public async Task<Resultado<ResultadoDeLaRevision>> Ejecutar(RevisarTrasladosSinAprobarEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeTraslados.Fallo<ResultadoDeLaRevision>(rechazo);
        }

        var ahora = reloj.Ahora;
        var vencidos = await traslados.VinculadosSinAprobarVencidos(ahora - Plazo, Math.Clamp(entrada.Maximo, 1, 1000), cancelacion).ConfigureAwait(false);

        var avisados = 0;
        foreach (var id in vencidos)
        {
            if (!await traslados.MarcarAlertaDeDemora(id, ahora, cancelacion).ConfigureAwait(false))
            {
                continue;
            }

            await alertas.PublicarTecnica(new CodigoDeAlerta(MotivoDeAlerta), cancelacion).ConfigureAwait(false);
            avisados++;
        }

        return Resultado<ResultadoDeLaRevision>.Exito(new ResultadoDeLaRevision(avisados));
    }
}

/// <summary>Sin datos de entrada: la depuración no recibe a quién anonimizar.</summary>
internal sealed record DepurarTrasladosEntrada;

/// <summary>Cuántos traslados se anonimizaron y cuántos mensajes del buzón se purgaron.</summary>
internal sealed record ResultadoDeLaDepuracionDeTraslados(int Anonimizados, int MensajesPurgados);

/// <summary>
/// La depuración diaria (plan §3, D07): a los 2 años de la fecha final del traslado quita <c>solicitado_por</c> y vacía el motivo, el motivo de cierre y la guía, conservando el movimiento y sus cantidades (mismo plazo que la
/// auditoría, AUD-19), y purga del buzón los mensajes enviados o fallidos de más de 30 días. Va en lotes de 5 000 hasta que uno devuelva menos; repetirla no cambia nada. <b>Exige el actor «sistema»</b>.
/// </summary>
internal sealed class DepurarTraslados(
    AutorizadorDeTraslados autorizador,
    IRepositorioDeTraslados traslados,
    IColaDeMensajesSalientes cola,
    IReloj reloj) : ICasoDeUso<DepurarTrasladosEntrada, ResultadoDeLaDepuracionDeTraslados>
{
    public const int TamanoDelLote = 5000;

    public static readonly TimeSpan RetencionDeMensajes = TimeSpan.FromDays(30);

    public async Task<Resultado<ResultadoDeLaDepuracionDeTraslados>> Ejecutar(DepurarTrasladosEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeTraslados.Fallo<ResultadoDeLaDepuracionDeTraslados>(rechazo);
        }

        var ahora = reloj.Ahora;
        var anonimizados = 0;
        int lote;
        do
        {
            lote = await traslados.AnonimizarCerrados(ahora.AddYears(-2), TamanoDelLote, cancelacion).ConfigureAwait(false);
            anonimizados += lote;
        }
        while (lote >= TamanoDelLote);

        var purgados = await cola.PurgarTerminados(ahora - RetencionDeMensajes, cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeLaDepuracionDeTraslados>.Exito(new ResultadoDeLaDepuracionDeTraslados(anonimizados, purgados));
    }
}
