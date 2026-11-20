using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Domain.Biblioteca;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Ecommerce.Infrastructure.Biblioteca;

/// <summary>
/// El registro de descargas, las claves de un solo uso y las ayudas sobre el esquema <c>biblioteca</c> (T12, T13). La clave se registra con <c>INSERT … ON CONFLICT (acceso_id, clave_hash) DO UPDATE … WHERE vence_en &lt;= ahora</c>: una clave vigente no afecta filas (la repetición, BIB-05) y una clave
/// cuyo acceso desapareció (violación 23503) se traduce sin abortar la transacción del llamador (BIB-28). <b>Nada guarda la URL, la clave en claro ni el origen.</b> Todos los valores van por parámetros.
/// </summary>
internal sealed class RepositorioDeDescargasPostgres(AccesoAlEsquemaDeBiblioteca acceso) : IRepositorioDeDescargas
{
    private const string ColumnasDeAyuda = "id, acceso_id, motivo, estado, creada_en, cerrada_en";

    private static SolicitudDeAyuda LeerAyuda(NpgsqlDataReader filas) =>
        SolicitudDeAyuda.Reconstruir(
            filas.GetGuid(0),
            filas.GetGuid(1),
            CodigosDeMotivos.AyudaDesdeCodigo(filas.GetString(2)) ?? throw new InvalidOperationException("Motivo de ayuda desconocido."),
            CodigosDeMotivos.EstadoDeAyudaDesdeCodigo(filas.GetString(3)) ?? throw new InvalidOperationException("Estado de ayuda desconocido."),
            filas.GetFieldValue<DateTimeOffset>(4),
            AccesoAlEsquemaDeBiblioteca.LeerInstante(filas, 5));

    public Task Registrar(RegistroDeDescarga registro, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    "INSERT INTO biblioteca.descargas (id, acceso_id, usuario_id, edicion_id, entregada_en, dia) VALUES (@id, @acceso, @usuario, @edicion, @entregada, @dia)",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("id", registro.Id);
                comando.Parameters.Add(new NpgsqlParameter("acceso", NpgsqlDbType.Uuid) { Value = AccesoAlEsquemaDeBiblioteca.ONulo(registro.AccesoId) });
                comando.Parameters.Add(new NpgsqlParameter("usuario", NpgsqlDbType.Uuid) { Value = AccesoAlEsquemaDeBiblioteca.ONulo(registro.UsuarioId) });
                comando.Parameters.AddWithValue("edicion", registro.EdicionId);
                comando.Parameters.Add(new NpgsqlParameter("entregada", NpgsqlDbType.TimestampTz) { Value = AccesoAlEsquemaDeBiblioteca.UtcONulo(registro.EntregadaEn) });
                comando.Parameters.Add(new NpgsqlParameter("dia", NpgsqlDbType.Date) { Value = registro.Dia });
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);

    public Task<ClaveRegistrada?> ObtenerClave(Guid accesoId, ClaveDeDescarga clave, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    "SELECT firmado_en, vence_en, descarga_id FROM biblioteca.claves_de_descarga WHERE acceso_id = @acceso AND clave_hash = @hash",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("acceso", accesoId);
                comando.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Bytea) { Value = clave.Resumen });
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await filas.ReadAsync(token).ConfigureAwait(false)
                    ? new ClaveRegistrada(accesoId, clave, filas.GetFieldValue<DateTimeOffset>(0), filas.GetFieldValue<DateTimeOffset>(1), filas.GetGuid(2))
                    : null;
            },
            cancelacion);

    public Task<ResultadoDeRegistrarClave> RegistrarClave(ClaveRegistrada clave, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await transaccion.SaveAsync("registrar_clave", token).ConfigureAwait(false);
                try
                {
                    await using var comando = new NpgsqlCommand(
                        "INSERT INTO biblioteca.claves_de_descarga (acceso_id, clave_hash, firmado_en, vence_en, descarga_id) VALUES (@acceso, @hash, @firmado, @vence, @descarga) "
                        + "ON CONFLICT (acceso_id, clave_hash) DO UPDATE SET firmado_en = EXCLUDED.firmado_en, vence_en = EXCLUDED.vence_en, descarga_id = EXCLUDED.descarga_id WHERE biblioteca.claves_de_descarga.vence_en <= @ahora",
                        conexion,
                        transaccion);
                    comando.Parameters.AddWithValue("acceso", clave.AccesoId);
                    comando.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Bytea) { Value = clave.Clave.Resumen });
                    comando.Parameters.AddWithValue("firmado", clave.FirmadoEn.ToUniversalTime());
                    comando.Parameters.AddWithValue("vence", clave.VenceEn.ToUniversalTime());
                    comando.Parameters.AddWithValue("descarga", clave.DescargaId);
                    comando.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
                    var afectadas = await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await transaccion.ReleaseAsync("registrar_clave", token).ConfigureAwait(false);
                    return afectadas == 1 ? ResultadoDeRegistrarClave.Registrada : ResultadoDeRegistrarClave.RepeticionVigente;
                }
                catch (PostgresException excepcion) when (excepcion.SqlState == AccesoAlEsquemaDeBiblioteca.ViolacionDeClaveForanea)
                {
                    // El acceso ya no existe (la cuenta se canceló entre el conteo y la clave): se deshace solo este intento y no se escribe nada con el usuario (BIB-28).
                    await transaccion.RollbackAsync("registrar_clave", token).ConfigureAwait(false);
                    return ResultadoDeRegistrarClave.AccesoInexistente;
                }
            },
            cancelacion);

    public Task<int> PurgarClaves(DateTimeOffset antes, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("DELETE FROM biblioteca.claves_de_descarga WHERE vence_en <= @antes", conexion, transaccion);
                comando.Parameters.AddWithValue("antes", antes.ToUniversalTime());
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            },
            cancelacion);

    public Task<int> DepurarRegistros(DateOnly diaLimite, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // Solo toca lo no depurado: repetir la depuración no cambia nada (BIB-26); el acceso del cliente no se toca.
                await using var comando = new NpgsqlCommand(
                    "UPDATE biblioteca.descargas SET acceso_id = NULL, usuario_id = NULL, entregada_en = NULL WHERE acceso_id IS NOT NULL AND dia <= @limite",
                    conexion,
                    transaccion);
                comando.Parameters.Add(new NpgsqlParameter("limite", NpgsqlDbType.Date) { Value = diaLimite });
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            },
            cancelacion);

    public Task<SolicitudDeAyuda?> ObtenerAyudaAbierta(Guid accesoId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand($"SELECT {ColumnasDeAyuda} FROM biblioteca.solicitudes_de_ayuda WHERE acceso_id = @acceso AND estado = 'abierta'", conexion, transaccion);
                comando.Parameters.AddWithValue("acceso", accesoId);
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await filas.ReadAsync(token).ConfigureAwait(false) ? LeerAyuda(filas) : null;
            },
            cancelacion);

    public Task<SolicitudDeAyuda?> ObtenerAyuda(Guid ayudaId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand($"SELECT {ColumnasDeAyuda} FROM biblioteca.solicitudes_de_ayuda WHERE id = @id", conexion, transaccion);
                comando.Parameters.AddWithValue("id", ayudaId);
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await filas.ReadAsync(token).ConfigureAwait(false) ? LeerAyuda(filas) : null;
            },
            cancelacion);

    public Task<ResultadoDeAgregarAyuda> AgregarAyuda(SolicitudDeAyuda ayuda, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await transaccion.SaveAsync("agregar_ayuda", token).ConfigureAwait(false);
                try
                {
                    await using var comando = new NpgsqlCommand(
                        $"INSERT INTO biblioteca.solicitudes_de_ayuda ({ColumnasDeAyuda}) VALUES (@id, @acceso, @motivo, 'abierta', @creada, NULL)",
                        conexion,
                        transaccion);
                    comando.Parameters.AddWithValue("id", ayuda.Id);
                    comando.Parameters.AddWithValue("acceso", ayuda.AccesoId);
                    comando.Parameters.AddWithValue("motivo", ayuda.Motivo.Codigo());
                    comando.Parameters.AddWithValue("creada", ayuda.CreadaEn.ToUniversalTime());
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await transaccion.ReleaseAsync("agregar_ayuda", token).ConfigureAwait(false);
                    return ResultadoDeAgregarAyuda.Agregada;
                }
                catch (PostgresException excepcion) when (excepcion.SqlState == AccesoAlEsquemaDeBiblioteca.ViolacionDeUnicidad)
                {
                    // Ya hay una abierta para el acceso (el único parcial): se deshace solo este intento.
                    await transaccion.RollbackAsync("agregar_ayuda", token).ConfigureAwait(false);
                    return ResultadoDeAgregarAyuda.YaHayAbierta;
                }
                catch (PostgresException excepcion) when (excepcion.SqlState == AccesoAlEsquemaDeBiblioteca.ViolacionDeClaveForanea)
                {
                    // El acceso desapareció (la cuenta se canceló entre la lectura y la inserción): se deshace solo este intento y no queda ninguna ayuda huérfana.
                    await transaccion.RollbackAsync("agregar_ayuda", token).ConfigureAwait(false);
                    return ResultadoDeAgregarAyuda.AccesoInexistente;
                }
            },
            cancelacion);

    public Task<bool> CerrarAyuda(Guid ayudaId, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("UPDATE biblioteca.solicitudes_de_ayuda SET estado = 'cerrada', cerrada_en = @ahora WHERE id = @id AND estado = 'abierta'", conexion, transaccion);
                comando.Parameters.AddWithValue("id", ayudaId);
                comando.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1;
            },
            cancelacion);

    public Task<(IReadOnlyList<SolicitudDeAyuda> Ayudas, int Total)> ListarAyudas(EstadoDeAyuda? estado, int salto, int tomar, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var codigo = estado?.Codigo();
                await using var contar = new NpgsqlCommand("SELECT count(*) FROM biblioteca.solicitudes_de_ayuda WHERE (@estado::text IS NULL OR estado = @estado::text)", conexion, transaccion);
                contar.Parameters.Add(new NpgsqlParameter("estado", NpgsqlDbType.Text) { Value = AccesoAlEsquemaDeBiblioteca.ONulo(codigo) });
                var total = (int)(long)(await contar.ExecuteScalarAsync(token).ConfigureAwait(false))!;

                await using var listar = new NpgsqlCommand(
                    $"SELECT {ColumnasDeAyuda} FROM biblioteca.solicitudes_de_ayuda WHERE (@estado::text IS NULL OR estado = @estado::text) ORDER BY creada_en DESC, id LIMIT @tomar OFFSET @salto",
                    conexion,
                    transaccion);
                listar.Parameters.Add(new NpgsqlParameter("estado", NpgsqlDbType.Text) { Value = AccesoAlEsquemaDeBiblioteca.ONulo(codigo) });
                listar.Parameters.AddWithValue("tomar", Math.Clamp(tomar, 1, 200));
                listar.Parameters.AddWithValue("salto", Math.Max(salto, 0));
                var ayudas = new List<SolicitudDeAyuda>();
                await using var filas = await listar.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    ayudas.Add(LeerAyuda(filas));
                }

                return ((IReadOnlyList<SolicitudDeAyuda>)ayudas, total);
            },
            cancelacion);

    public Task<IReadOnlyList<Guid>> AccesosConAyudaAbierta(IReadOnlyCollection<Guid> accesoIds, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT DISTINCT acceso_id FROM biblioteca.solicitudes_de_ayuda WHERE estado = 'abierta' AND acceso_id = ANY(@accesos)", conexion, transaccion);
                comando.Parameters.Add(new NpgsqlParameter("accesos", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = accesoIds.ToArray() });
                var lista = new List<Guid>();
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    lista.Add(filas.GetGuid(0));
                }

                return (IReadOnlyList<Guid>)lista;
            },
            cancelacion);

    public Task<int> DepurarAyudas(DateOnly diaLimite, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // De cualquier estado, por el día de Lima de su creación (BIB-29).
                await using var comando = new NpgsqlCommand("DELETE FROM biblioteca.solicitudes_de_ayuda WHERE (creada_en AT TIME ZONE 'America/Lima')::date <= @limite", conexion, transaccion);
                comando.Parameters.Add(new NpgsqlParameter("limite", NpgsqlDbType.Date) { Value = diaLimite });
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            },
            cancelacion);
}
