using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;


/// <summary>
/// Anonimiza las filas del catálogo con datos de personal anteriores al límite (plan §3, retención de 2 años): en <c>ingresos_solicitados</c>, el usuario (<see cref="Guid.Empty"/>, que <c>IActorActual</c> nunca produce) y los textos libres <c>nota</c> y <c>referencia</c>;
/// en <c>archivos_digitales</c>, <c>subido_por</c>; y en <c>archivos_pendientes</c>, <c>usuario_id</c>. No toca el estado ni los identificadores opacos del ingreso. Una sola instrucción por tabla, en la transacción de quien llama (la auditoría va en ella).
/// </summary>
public interface IAnonimizadorDelCatalogo
{
    Task<AnonimizacionDelCatalogoRealizada> Anonimizar(DateTimeOffset limite, CancellationToken cancelacion = default);
}

/// <summary>
/// <c>AnonimizarIngresosSolicitados</c> (trabajo de fondo diario, plan §3, T23): anonimiza lo anterior a <b>2 años</b> y audita la depuración en la misma transacción con solo la cantidad de filas (<b>sin datos personales</b>). Solo el sistema (un trabajo de fondo sin ese actor
/// es un defecto, no un permiso). <b>Falla cerrada</b>: si la auditoría no se puede escribir, la transacción se revierte y no se anonimiza nada; la corrida siguiente lo reintenta. Es idempotente: una fila ya anonimizada no se vuelve a contar y una corrida sin cambios no deja registro.
/// </summary>
internal sealed class AnonimizarIngresosSolicitados(IActorActual actores, IAnonimizadorDelCatalogo anonimizador, IUnidadDeTrabajo unidad, IAuditoria auditoria, IReloj reloj)
{
    /// <summary>La retención de los datos de personal del catálogo: 2 años.</summary>
    public const int AniosDeRetencion = 2;

    public async Task<Resultado<AnonimizacionDelCatalogoRealizada>> Ejecutar(CancellationToken cancelacion = default)
    {
        if (actores.Actual is not { Tipo: TipoDeActor.Sistema })
        {
            return FallosDelCatalogo.De<AnonimizacionDelCatalogoRealizada>(CategoriaDeError.Prohibido, AutorizadorDelCatalogo.ClaveProhibido);
        }

        var limite = reloj.Ahora.AddYears(-AniosDeRetencion);
        var realizada = await unidad.Ejecutar(
            async token =>
            {
                var resultado = await anonimizador.Anonimizar(limite, token).ConfigureAwait(false);
                if (resultado.Total > 0)
                {
                    await auditoria.Registrar(
                        new SolicitudDeRegistro(AccionesAuditables.IngresosSolicitadosAnonimizados)
                        {
                            Detalle = new Dictionary<string, string?> { ["cantidad"] = resultado.Total.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                        },
                        token).ConfigureAwait(false);
                }

                return resultado;
            },
            cancelacion).ConfigureAwait(false);
        return Resultado<AnonimizacionDelCatalogoRealizada>.Exito(realizada);
    }
}

/// <summary>La fachada del mantenimiento del catálogo (<see cref="IMantenimientoDelCatalogo"/>): cada operación comprueba que el actor sea el sistema.</summary>
internal sealed class MantenimientoDelCatalogoDeLaAplicacion(DepurarArchivosSinUso depurar, AnonimizarIngresosSolicitados anonimizar) : IMantenimientoDelCatalogo
{
    public Task<Resultado<DepuracionDeArchivos>> DepurarArchivosSinUso(CancellationToken cancelacion = default) => depurar.Ejecutar(cancelacion);

    public Task<Resultado<AnonimizacionDelCatalogoRealizada>> AnonimizarIngresosSolicitados(CancellationToken cancelacion = default) => anonimizar.Ejecutar(cancelacion);
}
