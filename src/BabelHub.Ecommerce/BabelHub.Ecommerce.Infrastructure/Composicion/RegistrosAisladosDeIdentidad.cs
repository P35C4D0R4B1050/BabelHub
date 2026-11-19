using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Auditoria;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>
/// Los puertos de Auditoría que implementa Identidad sobre <c>identidad.registros</c> (identidad-seguridad T27 y T36): reutilizan las utilidades de inserción y de paginación de <c>Auditoria</c> (parametrizadas por
/// esquema) bajo el rol <c>rol_identidad</c> y dentro de la unidad de trabajo del caso de uso. Viven en la composición porque unen dos módulos sin que uno referencie el interior del otro.
/// </summary>
public static class RegistrosAisladosDeIdentidad
{
    public const string Esquema = "identidad";
}

/// <summary>
/// <see cref="IAlmacenDeRegistrosAislados"/> sobre <c>identidad.registros</c>: solo inserción en la transacción de quien llama (si la auditoría falla, la operación se revierte, AUD-04); el evento de seguridad que no
/// concede nada se escribe con un <b>punto de guardado</b>: si el <c>INSERT</c> falla, la transacción de quien llama sigue válida (el bloqueo o el ingreso fallido se aplican igual) y el fallo se lanza sin efecto sobre ella (AUD-12).
/// </summary>
public sealed class PostgresAlmacenDeRegistrosAislados(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion) : IAlmacenDeRegistrosAislados
{
    public Task Insertar(RegistroAuditado registro, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(registro);
        return EscrituraDeAuditoria.EnLaUnidad(
            unidad,
            conexion,
            async (c, t, ct) => await InsercionDeRegistros.Insertar(c, t, registro, ignorarDuplicado: false, ct, RegistrosAisladosDeIdentidad.Esquema).ConfigureAwait(false),
            cancelacion,
            RegistrosAisladosDeIdentidad.Esquema);
    }

    public Task InsertarAislandoElFallo(RegistroAuditado registro, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(registro);
        return EscrituraDeAuditoria.EnLaUnidad(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using (var punto = new NpgsqlCommand("SAVEPOINT registro_aislado", c, t))
                {
                    await punto.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                try
                {
                    await InsercionDeRegistros.Insertar(c, t, registro, ignorarDuplicado: false, ct, RegistrosAisladosDeIdentidad.Esquema).ConfigureAwait(false);
                    await using var liberar = new NpgsqlCommand("RELEASE SAVEPOINT registro_aislado", c, t);
                    await liberar.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    return 0;
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // La transacción de quien llama sigue válida: se deshace solo este INSERT y el fallo se lanza.
                    await using var deshacer = new NpgsqlCommand("ROLLBACK TO SAVEPOINT registro_aislado", c, t);
                    await deshacer.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            },
            cancelacion,
            RegistrosAisladosDeIdentidad.Esquema);
    }
}

/// <summary>
/// <see cref="IFuenteDeRegistrosAislados"/> sobre <c>identidad.registros</c> (AUD-17, AUD-30): la misma paginación, filtros y orden que los demás registros, y el conteo de una acción en una ventana de 5 como
/// máximo (el umbral de SEG-09 sobre los ingresos fallidos), solo un número de vuelta.
/// </summary>
public sealed class PostgresFuenteDeRegistrosAislados(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion) : IFuenteDeRegistrosAislados
{
    public Task<PaginaDeRegistros> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        return ConsultaDePaginas.Consultar(unidad, conexion, consulta.Filtro ?? new FiltroDeAuditoria(), consulta.Cursor, restriccion: null, cancelacion, RegistrosAisladosDeIdentidad.Esquema);
    }

    public Task<int> Contar(SolicitudDeConteo solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return EscrituraDeAuditoria.EnLaUnidad(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = new NpgsqlCommand(
                    "SELECT count(*) FROM identidad.registros WHERE accion = @accion AND instante_utc >= @desde AND instante_utc < @hasta",
                    c,
                    t);
                comando.Parameters.AddWithValue("accion", solicitud.Accion.Identificador);
                comando.Parameters.Add(new NpgsqlParameter("desde", NpgsqlDbType.TimestampTz) { Value = solicitud.Desde.ToUniversalTime() });
                comando.Parameters.Add(new NpgsqlParameter("hasta", NpgsqlDbType.TimestampTz) { Value = solicitud.Hasta.ToUniversalTime() });
                return (int)Math.Min((long)(await comando.ExecuteScalarAsync(ct).ConfigureAwait(false))!, int.MaxValue);
            },
            cancelacion,
            RegistrosAisladosDeIdentidad.Esquema);
    }
}

/// <summary>
/// <see cref="IDepuradorDeRegistrosAislados"/>: ejecuta <c>identidad.anonimizar_registros()</c> con <b>otra fuente de datos</b>, la del rol de depuración (la aplicación no es miembro de ese rol), en su propia
/// conexión y transacción, un lote por llamada hasta que devuelva menos del tamaño del lote (AUD-19, AUD-36). La función no recibe parámetros: la aplicación no puede pedir anonimizar a quien quiera.
/// </summary>
public sealed class PostgresDepuradorDeRegistrosAislados(NpgsqlDataSource depuracion) : IDepuradorDeRegistrosAislados
{
    public async Task<int> Depurar(CancellationToken cancelacion = default)
    {
        var total = 0;
        for (var lotes = 0; lotes < PostgresAnonimizadorDeRegistros.MaximoDeLotesPorCorrida; lotes++)
        {
            await using var conexion = await depuracion.OpenConnectionAsync(cancelacion).ConfigureAwait(false);
            await using var comando = new NpgsqlCommand("SELECT identidad.anonimizar_registros()", conexion) { CommandTimeout = (int)PostgresAnonimizadorDeRegistros.EsperaPorLote.TotalSeconds };
            var lote = (int)(await comando.ExecuteScalarAsync(cancelacion).ConfigureAwait(false))!;
            total += lote;
            if (lote < PostgresAnonimizadorDeRegistros.TamanoDelLote)
            {
                break;
            }
        }

        return total;
    }
}
