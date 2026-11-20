using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Domain.Biblioteca;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Ecommerce.Infrastructure.Biblioteca;

/// <summary>
/// El repositorio de los accesos sobre el esquema <c>biblioteca</c> (T12, T13). <b>Cada operación que decide es una sola sentencia condicional</b> (plan §5): el <c>UPDATE … WHERE</c> resuelve el tope (BIB-17), la carrera con un reembolso automático (BIB-23) y la cancelación de la cuenta (BIB-28) con el
/// estado que tiene la fila al desbloquearse, y quien pierde relee para clasificar. Participan en la transacción del llamador y no abren otra; el acceso es lo último en el orden global de bloqueo. Todos los valores van por parámetros; nada guarda ni lee una URL ni la clave de objeto.
/// </summary>
internal sealed class RepositorioDeAccesosPostgres(AccesoAlEsquemaDeBiblioteca acceso) : IRepositorioDeAccesos
{
    internal const string Columnas = "id, cuenta_id, edicion_id, pedido_id, estado, descargas_usadas, enlaces_entregados, concedido_en, reactivado_en, revocado_en, motivo_de_revocacion, suspendido_en";

    internal static Acceso Leer(NpgsqlDataReader filas)
    {
        var estado = CodigosDeBiblioteca.EstadoDesdeCodigo(filas.GetString(4)) ?? throw new InvalidOperationException("Estado de acceso desconocido.");
        var causa = filas.IsDBNull(10) ? null : CodigosDeBiblioteca.CausaDesdeCodigo(filas.GetString(10));
        return Acceso.Reconstruir(
            filas.GetGuid(0),
            filas.GetGuid(1),
            filas.GetGuid(2),
            filas.GetGuid(3),
            estado,
            filas.GetInt32(5),
            filas.GetInt32(6),
            filas.GetFieldValue<DateTimeOffset>(7),
            AccesoAlEsquemaDeBiblioteca.LeerInstante(filas, 8),
            AccesoAlEsquemaDeBiblioteca.LeerInstante(filas, 9),
            causa,
            AccesoAlEsquemaDeBiblioteca.LeerInstante(filas, 11));
    }

    private static async Task<Acceso?> LeerUno(NpgsqlCommand comando, CancellationToken token)
    {
        await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await filas.ReadAsync(token).ConfigureAwait(false) ? Leer(filas) : null;
    }

    public Task<Acceso?> Obtener(Guid accesoId, Guid? cuentaId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand($"SELECT {Columnas} FROM biblioteca.accesos WHERE id = @id AND (@cuenta::uuid IS NULL OR cuenta_id = @cuenta::uuid)", conexion, transaccion);
                comando.Parameters.AddWithValue("id", accesoId);
                comando.Parameters.Add(new NpgsqlParameter("cuenta", NpgsqlDbType.Uuid) { Value = AccesoAlEsquemaDeBiblioteca.ONulo(cuentaId) });
                return await LeerUno(comando, token).ConfigureAwait(false);
            },
            cancelacion);

    public Task<IReadOnlyList<Acceso>> ListarDeLaCuenta(Guid cuentaId, int salto, int tomar, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    $"SELECT {Columnas} FROM biblioteca.accesos WHERE cuenta_id = @cuenta ORDER BY COALESCE(reactivado_en, concedido_en) DESC, id LIMIT @tomar OFFSET @salto",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("cuenta", cuentaId);
                comando.Parameters.AddWithValue("tomar", Math.Clamp(tomar, 1, 200));
                comando.Parameters.AddWithValue("salto", Math.Max(salto, 0));
                var lista = new List<Acceso>();
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    lista.Add(Leer(filas));
                }

                return (IReadOnlyList<Acceso>)lista;
            },
            cancelacion);

    public Task<int> ContarDeLaCuenta(Guid cuentaId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT count(*) FROM biblioteca.accesos WHERE cuenta_id = @cuenta", conexion, transaccion);
                comando.Parameters.AddWithValue("cuenta", cuentaId);
                return (int)(long)(await comando.ExecuteScalarAsync(token).ConfigureAwait(false))!;
            },
            cancelacion);

    public Task<IReadOnlyList<Guid>> EdicionesPoseidas(Guid cuentaId, IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    "SELECT edicion_id FROM biblioteca.accesos WHERE cuenta_id = @cuenta AND edicion_id = ANY(@ediciones) AND estado IN ('vigente', 'suspendido')",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("cuenta", cuentaId);
                comando.Parameters.Add(new NpgsqlParameter("ediciones", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = edicionIds.ToArray() });
                var lista = new List<Guid>();
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    lista.Add(filas.GetGuid(0));
                }

                return (IReadOnlyList<Guid>)lista;
            },
            cancelacion);

    public Task<Acceso?> ObtenerDeLaCompra(Guid cuentaId, Guid pedidoId, Guid edicionId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand($"SELECT {Columnas} FROM biblioteca.accesos WHERE cuenta_id = @cuenta AND pedido_id = @pedido AND edicion_id = @edicion", conexion, transaccion);
                comando.Parameters.AddWithValue("cuenta", cuentaId);
                comando.Parameters.AddWithValue("pedido", pedidoId);
                comando.Parameters.AddWithValue("edicion", edicionId);
                return await LeerUno(comando, token).ConfigureAwait(false);
            },
            cancelacion);

    public Task<ResultadoDeConcesion> Conceder(Guid cuentaId, Guid edicionId, Guid pedidoId, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // «ON CONFLICT DO NOTHING» sin destino: dos concesiones simultáneas del mismo pedido chocan con el único (cuenta, edición) o con el único (pedido, edición) según quién se compruebe primero, y ambos casos son «ya existe» (BIB-19), nunca un error.
                // Dos vueltas como máximo: si el acceso existente desaparece entre las sentencias (la cancelación de la cuenta), se inserta de nuevo.
                for (var vuelta = 0; vuelta < 2; vuelta++)
                {
                    await using (var insertar = new NpgsqlCommand(
                        $"INSERT INTO biblioteca.accesos ({Columnas}) VALUES (@id, @cuenta, @edicion, @pedido, 'vigente', 0, 0, @ahora, NULL, NULL, NULL, NULL) ON CONFLICT DO NOTHING",
                        conexion,
                        transaccion))
                    {
                        insertar.Parameters.AddWithValue("id", Guid.NewGuid());
                        insertar.Parameters.AddWithValue("cuenta", cuentaId);
                        insertar.Parameters.AddWithValue("edicion", edicionId);
                        insertar.Parameters.AddWithValue("pedido", pedidoId);
                        insertar.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
                        if (await insertar.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1)
                        {
                            return ResultadoDeConcesion.Concedido;
                        }
                    }

                    // Ya existía: solo un revocado de OTRA compra se reactiva (BIB-20); la sentencia reinicia el cupo y los enlaces.
                    await using (var reactivar = new NpgsqlCommand(
                        "UPDATE biblioteca.accesos SET estado = 'vigente', descargas_usadas = 0, enlaces_entregados = 0, pedido_id = @pedido, reactivado_en = @ahora, revocado_en = NULL, motivo_de_revocacion = NULL, suspendido_en = NULL "
                        + "WHERE cuenta_id = @cuenta AND edicion_id = @edicion AND estado = 'revocado' AND pedido_id <> @pedido",
                        conexion,
                        transaccion))
                    {
                        reactivar.Parameters.AddWithValue("cuenta", cuentaId);
                        reactivar.Parameters.AddWithValue("edicion", edicionId);
                        reactivar.Parameters.AddWithValue("pedido", pedidoId);
                        reactivar.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
                        if (await reactivar.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1)
                        {
                            return ResultadoDeConcesion.Reactivado;
                        }
                    }

                    await using var leer = new NpgsqlCommand("SELECT pedido_id FROM biblioteca.accesos WHERE cuenta_id = @cuenta AND edicion_id = @edicion", conexion, transaccion);
                    leer.Parameters.AddWithValue("cuenta", cuentaId);
                    leer.Parameters.AddWithValue("edicion", edicionId);
                    if (await leer.ExecuteScalarAsync(token).ConfigureAwait(false) is Guid existente)
                    {
                        // El del mismo pedido es la repetición (incluso revocado: la revocación manda); otro pedido vigente o suspendido, ya poseído por otra compra.
                        return existente == pedidoId ? ResultadoDeConcesion.YaConcedidoPorEstePedido : ResultadoDeConcesion.YaPoseidoPorOtraCompra;
                    }
                }

                throw new BibliotecaNoDisponibleException("concesion-no-resuelta");
            },
            cancelacion);

    public Task<CambioDeAcceso<ResultadoDeRevocacion>> Revocar(Guid pedidoId, Guid edicionId, CausaDeRevocacion causa, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // Solo la primera afecta una fila; una orden de un pedido que ya no es el del acceso (reactivado por una recompra) no encuentra ninguna.
                await using var comando = new NpgsqlCommand(
                    $"UPDATE biblioteca.accesos SET estado = 'revocado', revocado_en = @ahora, motivo_de_revocacion = @causa, suspendido_en = NULL WHERE pedido_id = @pedido AND edicion_id = @edicion AND estado <> 'revocado' RETURNING {Columnas}",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("pedido", pedidoId);
                comando.Parameters.AddWithValue("edicion", edicionId);
                comando.Parameters.AddWithValue("causa", causa.Codigo());
                comando.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
                var revocado = await LeerUno(comando, token).ConfigureAwait(false);
                return new CambioDeAcceso<ResultadoDeRevocacion>(revocado is null ? ResultadoDeRevocacion.YaRevocado : ResultadoDeRevocacion.Revocado, revocado);
            },
            cancelacion);

    public Task<CambioDeAcceso<ResultadoDeRevocacionSinEnlace>> RevocarSiNoHayEnlace(Guid cuentaId, Guid pedidoId, Guid edicionId, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                for (var vuelta = 0; vuelta < 2; vuelta++)
                {
                    // Gana la primera sentencia que confirma frente a una entrega: exige vigente y ningún enlace entregado (BIB-22, BIB-23).
                    await using (var revocar = new NpgsqlCommand(
                        $"UPDATE biblioteca.accesos SET estado = 'revocado', revocado_en = @ahora, motivo_de_revocacion = 'reembolso' WHERE cuenta_id = @cuenta AND pedido_id = @pedido AND edicion_id = @edicion AND estado = 'vigente' AND enlaces_entregados = 0 RETURNING {Columnas}",
                        conexion,
                        transaccion))
                    {
                        revocar.Parameters.AddWithValue("cuenta", cuentaId);
                        revocar.Parameters.AddWithValue("pedido", pedidoId);
                        revocar.Parameters.AddWithValue("edicion", edicionId);
                        revocar.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
                        if (await LeerUno(revocar, token).ConfigureAwait(false) is { } revocado)
                        {
                            return new CambioDeAcceso<ResultadoDeRevocacionSinEnlace>(ResultadoDeRevocacionSinEnlace.Revocado, revocado);
                        }
                    }

                    // No afectó filas: se relee y se clasifica con las mismas reglas del Dominio sobre una copia.
                    await using var leer = new NpgsqlCommand($"SELECT {Columnas} FROM biblioteca.accesos WHERE cuenta_id = @cuenta AND edicion_id = @edicion", conexion, transaccion);
                    leer.Parameters.AddWithValue("cuenta", cuentaId);
                    leer.Parameters.AddWithValue("edicion", edicionId);
                    if (await LeerUno(leer, token).ConfigureAwait(false) is not { } actual)
                    {
                        return new CambioDeAcceso<ResultadoDeRevocacionSinEnlace>(ResultadoDeRevocacionSinEnlace.SinAcceso, null);
                    }

                    var clasificacion = actual.RevocarSiNoHayEnlace(pedidoId, CausaDeRevocacion.Reembolso, ahora);
                    if (clasificacion != ResultadoDeRevocacionSinEnlace.Revocado)
                    {
                        return new CambioDeAcceso<ResultadoDeRevocacionSinEnlace>(clasificacion, clasificacion == ResultadoDeRevocacionSinEnlace.SinAcceso ? null : actual);
                    }

                    // El estado cambió entre las dos sentencias y ahora sí se puede revocar: se intenta de nuevo.
                }

                throw new BibliotecaNoDisponibleException("revocacion-no-resuelta");
            },
            cancelacion);

    public Task<CambioDeAcceso<ResultadoDeSuspension>> Suspender(Guid cuentaId, Guid accesoId, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using (var suspender = new NpgsqlCommand(
                    $"UPDATE biblioteca.accesos SET estado = 'suspendido', suspendido_en = @ahora WHERE id = @id AND cuenta_id = @cuenta AND estado = 'vigente' RETURNING {Columnas}",
                    conexion,
                    transaccion))
                {
                    suspender.Parameters.AddWithValue("id", accesoId);
                    suspender.Parameters.AddWithValue("cuenta", cuentaId);
                    suspender.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
                    if (await LeerUno(suspender, token).ConfigureAwait(false) is { } suspendido)
                    {
                        return new CambioDeAcceso<ResultadoDeSuspension>(ResultadoDeSuspension.Suspendido, suspendido);
                    }
                }

                await using var leer = new NpgsqlCommand($"SELECT {Columnas} FROM biblioteca.accesos WHERE id = @id AND cuenta_id = @cuenta", conexion, transaccion);
                leer.Parameters.AddWithValue("id", accesoId);
                leer.Parameters.AddWithValue("cuenta", cuentaId);
                var actual = await LeerUno(leer, token).ConfigureAwait(false);
                return new CambioDeAcceso<ResultadoDeSuspension>(actual?.Estado == EstadoDeAcceso.Suspendido ? ResultadoDeSuspension.YaSuspendido : ResultadoDeSuspension.NoDisponible, actual);
            },
            cancelacion);

    public Task<CambioDeAcceso<ResultadoDeReanudacion>> Reanudar(Guid cuentaId, Guid accesoId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    $"UPDATE biblioteca.accesos SET estado = 'vigente', suspendido_en = NULL WHERE id = @id AND cuenta_id = @cuenta AND estado = 'suspendido' RETURNING {Columnas}",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("id", accesoId);
                comando.Parameters.AddWithValue("cuenta", cuentaId);
                var reanudado = await LeerUno(comando, token).ConfigureAwait(false);
                return new CambioDeAcceso<ResultadoDeReanudacion>(reanudado is null ? ResultadoDeReanudacion.NoDisponible : ResultadoDeReanudacion.Reanudado, reanudado);
            },
            cancelacion);

    public Task<ResultadoDelConteoAtomico> ConsumirCupo(Guid accesoId, Guid cuentaId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // La sentencia condicional que resuelve a la vez el tope, la titularidad, el estado y la carrera con la cancelación de la cuenta (BIB-17, BIB-23, BIB-28, D14).
                await using var comando = new NpgsqlCommand(
                    "UPDATE biblioteca.accesos SET descargas_usadas = descargas_usadas + 1, enlaces_entregados = enlaces_entregados + 1 WHERE id = @id AND cuenta_id = @cuenta AND estado = 'vigente' AND descargas_usadas < 5",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("id", accesoId);
                comando.Parameters.AddWithValue("cuenta", cuentaId);
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1 ? ResultadoDelConteoAtomico.Contada : ResultadoDelConteoAtomico.NoAfecto;
            },
            cancelacion);

    public Task<Acceso?> Restituir(Guid accesoId, int cantidad, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    $"UPDATE biblioteca.accesos SET descargas_usadas = descargas_usadas - @cantidad WHERE id = @id AND @cantidad >= 1 AND descargas_usadas >= @cantidad AND estado <> 'revocado' RETURNING {Columnas}",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("id", accesoId);
                comando.Parameters.AddWithValue("cantidad", cantidad);
                return await LeerUno(comando, token).ConfigureAwait(false);
            },
            cancelacion);

    public Task<int> EliminarDeLaCuenta(Guid cuentaId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // La cascada elimina el registro de descargas, las claves y las ayudas (BIB-25).
                await using var comando = new NpgsqlCommand("DELETE FROM biblioteca.accesos WHERE cuenta_id = @cuenta", conexion, transaccion);
                comando.Parameters.AddWithValue("cuenta", cuentaId);
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            },
            cancelacion);
}
