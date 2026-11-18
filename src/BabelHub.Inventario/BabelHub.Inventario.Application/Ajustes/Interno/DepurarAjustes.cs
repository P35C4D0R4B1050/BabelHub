using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Ajustes.Interno;

/// <summary>La depuración diaria de ajustes (AJU-21): sin datos de entrada (la función de la base no recibe parámetros: la aplicación no puede pedir anonimizar a nadie en particular).</summary>
internal sealed record DepurarAjustesEntrada;

/// <summary>Cuántas filas anonimizó la depuración de los 2 años y cuántas operaciones idempotentes purgó (las de más de 30 días).</summary>
internal sealed record ResultadoDeDepuracionDeAjustes(int Anonimizados, int OperacionesPurgadas);

/// <summary>
/// Puerto de la depuración de <c>Ajustes</c>: ejecuta <c>ajustes.anonimizar_registros()</c> y <c>ajustes.purgar_operaciones()</c> con <b>otra fuente de datos</b>, la del rol de depuración (la aplicación no es miembro de ese rol),
/// en su propia conexión y transacción, un lote por llamada hasta que devuelva menos del tamaño del lote.
/// </summary>
internal interface IDepuradorDeAjustes
{
    Task<int> Anonimizar(CancellationToken cancelacion = default);

    Task<int> PurgarOperaciones(CancellationToken cancelacion = default);
}

/// <summary>
/// La depuración diaria de ajustes e ingresos (AJU-21, D05): a los 2 años anonimiza el usuario responsable y vacía la nota, conservando el movimiento y sus cantidades, y purga las claves de idempotencia de más de 30 días (AJU-22).
/// <b>Exige el actor «sistema»</b> (<c>ExigirSistema</c>): un trabajo que corre sin actor responde «no autenticado» y no toca nada, y el trabajo de fondo lo convierte en un fallo a la vista. Repetirla no cambia nada.
/// </summary>
internal sealed class DepurarAjustes(AutorizadorDeAjustes autorizador, IDepuradorDeAjustes depurador) : ICasoDeUso<DepurarAjustesEntrada, ResultadoDeDepuracionDeAjustes>
{
    public async Task<Resultado<ResultadoDeDepuracionDeAjustes>> Ejecutar(DepurarAjustesEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeAjustes.Fallo<ResultadoDeDepuracionDeAjustes>(rechazo);
        }

        var anonimizados = await depurador.Anonimizar(cancelacion).ConfigureAwait(false);
        var purgadas = await depurador.PurgarOperaciones(cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeDepuracionDeAjustes>.Exito(new ResultadoDeDepuracionDeAjustes(anonimizados, purgadas));
    }
}
