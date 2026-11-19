using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Domain.Carrito;
using Npgsql;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.Infrastructure.Carrito;

/// <summary>
/// El repositorio de carritos sobre el esquema <c>carrito</c> (T24): cada lectura <c>conBloqueo</c> es <c>SELECT … FOR UPDATE</c> parametrizado dentro de la transacción activa y entrega una entidad <b>nueva</b> leída bajo ese bloqueo (nunca una instancia
/// leída antes sin él); la violación del índice único de un carrito de cuenta o de visitante se traduce a <see cref="CarritoYaExisteException"/> para que la Aplicación rehaga la transacción (con PostgreSQL una unicidad aborta la transacción y no se puede
/// seguir dentro de ella). Las líneas se reemplazan enteras al guardar (a lo más 50) con su <c>posicion</c>, y la versión solo avanza: guardar sobre una versión más nueva falla.
/// </summary>
public sealed class RepositorioDeCarritosPostgres(AccesoAlEsquemaDelCarrito acceso) : IRepositorioDeCarritos
{
    private const string Columnas = "id, cuenta_id, visitante_hash, estado, version, creado_en, modificado_en, vence_en";

    private static readonly string[] RestriccionesDeUnicidad = ["carritos_pkey", "carritos_cuenta_unica", "carritos_visitante_unico"];

    public Task<CarritoDeDominio?> ObtenerDeCuenta(Guid cuentaId, bool conBloqueo, CancellationToken cancelacion = default) =>
        Leer("cuenta_id = @valor", new NpgsqlParameter("valor", cuentaId), conBloqueo, cancelacion);

    public Task<CarritoDeDominio?> ObtenerDeVisitante(byte[] huellaDeLaCookie, bool conBloqueo, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(huellaDeLaCookie);
        return Leer("visitante_hash = @valor", new NpgsqlParameter("valor", huellaDeLaCookie), conBloqueo, cancelacion);
    }

    public Task<CarritoDeDominio?> Obtener(Guid carritoId, bool conBloqueo, CancellationToken cancelacion = default) =>
        Leer("id = @valor", new NpgsqlParameter("valor", carritoId), conBloqueo, cancelacion);

    private Task<CarritoDeDominio?> Leer(string filtro, NpgsqlParameter parametro, bool conBloqueo, CancellationToken cancelacion) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand($"SELECT {Columnas} FROM carrito.carritos WHERE {filtro}{(conBloqueo ? " FOR UPDATE" : string.Empty)}", conexion, transaccion);
                comando.Parameters.Add(parametro);
                CarritoDeDominio? carrito = null;
                CabeceraDelCarrito? cabecera = null;
                await using (var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (await filas.ReadAsync(token).ConfigureAwait(false))
                    {
                        cabecera = new CabeceraDelCarrito(
                            filas.GetGuid(0),
                            filas.IsDBNull(1) ? PropietarioDelCarrito.DeVisitante(filas.GetFieldValue<byte[]>(2)) : PropietarioDelCarrito.DeCuenta(filas.GetGuid(1)),
                            filas.GetString(3) == "fusionado" ? EstadoDeCarrito.Fusionado : EstadoDeCarrito.Abierto,
                            filas.GetInt32(4),
                            AccesoAlEsquemaDelCarrito.LeerInstante(filas, 5)!.Value,
                            AccesoAlEsquemaDelCarrito.LeerInstante(filas, 6)!.Value,
                            AccesoAlEsquemaDelCarrito.LeerInstante(filas, 7));
                    }
                }

                if (cabecera is not null)
                {
                    carrito = CarritoDeDominio.Restaurar(
                        cabecera.Id,
                        cabecera.Propietario,
                        cabecera.Estado,
                        cabecera.Version,
                        cabecera.CreadoEn,
                        cabecera.ModificadoEn,
                        cabecera.VenceEn,
                        await LeerLineas(conexion, transaccion, cabecera.Id, token).ConfigureAwait(false));
                }

                return carrito;
            },
            cancelacion);

    private static async Task<List<LineaDeCarrito>> LeerLineas(NpgsqlConnection conexion, NpgsqlTransaction transaccion, Guid carritoId, CancellationToken token)
    {
        var lineas = new List<LineaDeCarrito>();
        await using var comando = new NpgsqlCommand(
            "SELECT edicion_id, es_digital, cantidad, agregada_en, retiro_pendiente FROM carrito.lineas_de_carrito WHERE carrito_id = @carrito ORDER BY posicion",
            conexion,
            transaccion);
        comando.Parameters.AddWithValue("carrito", carritoId);
        await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await filas.ReadAsync(token).ConfigureAwait(false))
        {
            lineas.Add(LineaDeCarrito.Crear(filas.GetGuid(0), filas.GetBoolean(1), Cantidad.Crear(filas.GetInt32(2)), AccesoAlEsquemaDelCarrito.LeerInstante(filas, 3)!.Value, filas.GetBoolean(4)));
        }

        return lineas;
    }

    public async Task Agregar(CarritoDeDominio carrito, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(carrito);
        try
        {
            await acceso.Ejecutar(
                async (conexion, transaccion, token) =>
                {
                    await using (var comando = new NpgsqlCommand(
                        $"INSERT INTO carrito.carritos ({Columnas}) VALUES (@id, @cuenta, @visitante, @estado, @version, @creado, @modificado, @vence)",
                        conexion,
                        transaccion))
                    {
                        Parametros(comando, carrito);
                        comando.Parameters.AddWithValue("creado", carrito.CreadoEn.ToUniversalTime());
                        await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    await InsertarLineas(conexion, transaccion, carrito, token).ConfigureAwait(false);
                    return 0;
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (FallaDeLaBaseDelCarritoException falla) when (AccesoAlEsquemaDelCarrito.EsUnicidad(falla, RestriccionesDeUnicidad))
        {
            throw new CarritoYaExisteException();
        }
    }

    public Task Guardar(CarritoDeDominio carrito, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(carrito);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using (var comando = new NpgsqlCommand(
                    "UPDATE carrito.carritos SET estado = @estado, version = @version, modificado_en = @modificado, vence_en = @vence WHERE id = @id AND version <= @version",
                    conexion,
                    transaccion))
                {
                    Parametros(comando, carrito, incluirPropietario: false);
                    if (await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
                    {
                        throw new InvalidOperationException("El carrito no existe o ya lo guardó una versión más nueva.");
                    }
                }

                await using (var borrar = new NpgsqlCommand("DELETE FROM carrito.lineas_de_carrito WHERE carrito_id = @carrito", conexion, transaccion))
                {
                    borrar.Parameters.AddWithValue("carrito", carrito.Id);
                    await borrar.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await InsertarLineas(conexion, transaccion, carrito, token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    private static void Parametros(NpgsqlCommand comando, CarritoDeDominio carrito, bool incluirPropietario = true)
    {
        comando.Parameters.AddWithValue("id", carrito.Id);
        if (incluirPropietario)
        {
            comando.Parameters.AddWithValue("cuenta", NpgsqlTypes.NpgsqlDbType.Uuid, AccesoAlEsquemaDelCarrito.ONulo(carrito.Propietario.CuentaId));
            comando.Parameters.AddWithValue("visitante", NpgsqlTypes.NpgsqlDbType.Bytea, carrito.Propietario.EsDeVisitante ? carrito.Propietario.HuellaDelVisitante.ToArray() : DBNull.Value);
        }

        comando.Parameters.AddWithValue("estado", carrito.Estado == EstadoDeCarrito.Fusionado ? "fusionado" : "abierto");
        comando.Parameters.AddWithValue("version", carrito.Version);
        comando.Parameters.AddWithValue("modificado", carrito.ModificadoEn.ToUniversalTime());
        comando.Parameters.AddWithValue("vence", NpgsqlTypes.NpgsqlDbType.TimestampTz, AccesoAlEsquemaDelCarrito.UtcONulo(carrito.VenceEn));
    }

    private static async Task InsertarLineas(NpgsqlConnection conexion, NpgsqlTransaction transaccion, CarritoDeDominio carrito, CancellationToken token)
    {
        var posicion = 0;
        foreach (var linea in carrito.Lineas)
        {
            await using var comando = new NpgsqlCommand(
                "INSERT INTO carrito.lineas_de_carrito (carrito_id, edicion_id, posicion, cantidad, es_digital, agregada_en, retiro_pendiente) VALUES (@carrito, @edicion, @posicion, @cantidad, @digital, @agregada, @retiro)",
                conexion,
                transaccion);
            comando.Parameters.AddWithValue("carrito", carrito.Id);
            comando.Parameters.AddWithValue("edicion", linea.EdicionId);
            comando.Parameters.AddWithValue("posicion", posicion++);
            comando.Parameters.AddWithValue("cantidad", linea.Cantidad.Valor);
            comando.Parameters.AddWithValue("digital", linea.EsDigital);
            comando.Parameters.AddWithValue("agregada", linea.AgregadaEn.ToUniversalTime());
            comando.Parameters.AddWithValue("retiro", linea.RetiroPendiente);
            await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }

    // ---------- Avisos ----------

    public Task<IReadOnlyList<AvisoDelCarrito>> AvisosSinVer(Guid cuentaId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<AvisoDelCarrito>>(
            async (conexion, transaccion, token) =>
            {
                var avisos = new List<AvisoDelCarrito>();
                await using var comando = new NpgsqlCommand(
                    "SELECT id, tipo, edicion_id, titulo, detalle, cantidad_final, creado_en FROM carrito.avisos_de_carrito WHERE cuenta_id = @cuenta AND visto_en IS NULL ORDER BY creado_en DESC, id",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("cuenta", cuentaId);
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    avisos.Add(new AvisoDelCarrito(
                        filas.GetGuid(0),
                        filas.GetString(1) == "edicion_retirada" ? TipoDeAvisoDelCarrito.EdicionRetirada : TipoDeAvisoDelCarrito.FusionAjustada,
                        filas.GetGuid(2),
                        filas.GetString(3),
                        DeDetalle(AccesoAlEsquemaDelCarrito.LeerTexto(filas, 4)),
                        AccesoAlEsquemaDelCarrito.LeerNulable<int>(filas, 5),
                        AccesoAlEsquemaDelCarrito.LeerInstante(filas, 6)!.Value));
                }

                return avisos;
            },
            cancelacion);

    public Task AgregarAvisos(Guid cuentaId, IReadOnlyList<AvisoDelCarrito> avisos, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(avisos);
        if (avisos.Count == 0)
        {
            return Task.CompletedTask;
        }

        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                foreach (var aviso in avisos)
                {
                    await using var comando = new NpgsqlCommand(
                        "INSERT INTO carrito.avisos_de_carrito (id, cuenta_id, tipo, edicion_id, titulo, detalle, cantidad_final, creado_en) VALUES (@id, @cuenta, @tipo, @edicion, @titulo, @detalle, @cantidad, @creado)",
                        conexion,
                        transaccion);
                    comando.Parameters.AddWithValue("id", aviso.Id);
                    comando.Parameters.AddWithValue("cuenta", cuentaId);
                    comando.Parameters.AddWithValue("tipo", aviso.Tipo == TipoDeAvisoDelCarrito.EdicionRetirada ? "edicion_retirada" : "fusion_ajustada");
                    comando.Parameters.AddWithValue("edicion", aviso.EdicionId);
                    comando.Parameters.AddWithValue("titulo", aviso.Titulo);
                    comando.Parameters.AddWithValue("detalle", NpgsqlTypes.NpgsqlDbType.Text, AccesoAlEsquemaDelCarrito.ONulo(aviso.Motivo is { } motivo ? ADetalle(motivo) : null));
                    comando.Parameters.AddWithValue("cantidad", NpgsqlTypes.NpgsqlDbType.Integer, AccesoAlEsquemaDelCarrito.ONulo(aviso.CantidadFinal));
                    comando.Parameters.AddWithValue("creado", aviso.CreadoEn.ToUniversalTime());
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                return 0;
            },
            cancelacion);
    }

    public Task MarcarAvisosComoVistos(Guid cuentaId, IReadOnlyCollection<Guid> avisoIds, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(avisoIds);
        if (avisoIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    "UPDATE carrito.avisos_de_carrito SET visto_en = @ahora WHERE cuenta_id = @cuenta AND id = ANY(@ids) AND visto_en IS NULL",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
                comando.Parameters.AddWithValue("cuenta", cuentaId);
                comando.Parameters.AddWithValue("ids", avisoIds.ToArray());
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    private static string ADetalle(MotivoDeAjusteDeAviso motivo) => motivo switch
    {
        MotivoDeAjusteDeAviso.SinStock => "sin_stock",
        MotivoDeAjusteDeAviso.YaPoseida => "ya_poseida",
        MotivoDeAjusteDeAviso.NoVendible => "no_vendible",
        MotivoDeAjusteDeAviso.RecortadaAlMaximo => "recortada_al_maximo",
        MotivoDeAjusteDeAviso.DigitalAUnaUnidad => "digital_a_una_unidad",
        MotivoDeAjusteDeAviso.TopeDeLineas => "tope_de_lineas",
        MotivoDeAjusteDeAviso.TopeDeUnidades => "tope_de_unidades",
        _ => throw new ArgumentOutOfRangeException(nameof(motivo)),
    };

    private static MotivoDeAjusteDeAviso? DeDetalle(string? detalle) => detalle switch
    {
        null => null,
        "sin_stock" => MotivoDeAjusteDeAviso.SinStock,
        "ya_poseida" => MotivoDeAjusteDeAviso.YaPoseida,
        "no_vendible" => MotivoDeAjusteDeAviso.NoVendible,
        "recortada_al_maximo" => MotivoDeAjusteDeAviso.RecortadaAlMaximo,
        "digital_a_una_unidad" => MotivoDeAjusteDeAviso.DigitalAUnaUnidad,
        "tope_de_lineas" => MotivoDeAjusteDeAviso.TopeDeLineas,
        "tope_de_unidades" => MotivoDeAjusteDeAviso.TopeDeUnidades,
        _ => throw new InvalidOperationException("El aviso guardado tiene un detalle desconocido."),
    };

    // ---------- Fusión, purgas y eliminación ----------

    public Task RegistrarFusion(byte[] huellaDeLaCookie, Guid carritoDeCuentaId, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(huellaDeLaCookie);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    "INSERT INTO carrito.fusiones_aplicadas (visitante_hash, carrito_de_cuenta_id, aplicada_en) VALUES (@huella, @carrito, @ahora) ON CONFLICT (visitante_hash) DO NOTHING",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("huella", huellaDeLaCookie);
                comando.Parameters.AddWithValue("carrito", carritoDeCuentaId);
                comando.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task<int> EliminarDeVisitantesVencidos(DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default) =>
        Borrar(
            "DELETE FROM carrito.carritos WHERE id IN (SELECT id FROM carrito.carritos WHERE visitante_hash IS NOT NULL AND vence_en <= @limite ORDER BY vence_en, id LIMIT @maximo FOR UPDATE SKIP LOCKED)",
            [new NpgsqlParameter("limite", ahora.ToUniversalTime()), new NpgsqlParameter("maximo", maximo)],
            cancelacion);

    public Task<int> EliminarFusionesAnterioresA(DateTimeOffset antesDe, int maximo, CancellationToken cancelacion = default) =>
        Borrar(
            "DELETE FROM carrito.fusiones_aplicadas WHERE visitante_hash IN (SELECT visitante_hash FROM carrito.fusiones_aplicadas WHERE aplicada_en <= @limite ORDER BY aplicada_en LIMIT @maximo)",
            [new NpgsqlParameter("limite", antesDe.ToUniversalTime()), new NpgsqlParameter("maximo", maximo)],
            cancelacion);

    public Task<int> EliminarAvisosVencidos(DateTimeOffset vistosAntesDe, DateTimeOffset creadosAntesDe, CancellationToken cancelacion = default) =>
        Borrar(
            "DELETE FROM carrito.avisos_de_carrito WHERE (visto_en IS NOT NULL AND visto_en <= @vistos) OR creado_en <= @creados",
            [new NpgsqlParameter("vistos", vistosAntesDe.ToUniversalTime()), new NpgsqlParameter("creados", creadosAntesDe.ToUniversalTime())],
            cancelacion);

    private Task<int> Borrar(string sql, NpgsqlParameter[] parametros, CancellationToken cancelacion) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
                comando.Parameters.AddRange(parametros);
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            },
            cancelacion);

    public Task<IReadOnlyList<Guid>> CuentasSinMovimientoDesde(DateTimeOffset antesDe, int maximo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<Guid>>(
            async (conexion, transaccion, token) =>
            {
                var cuentas = new List<Guid>();
                await using var comando = new NpgsqlCommand(
                    """
                    SELECT cuenta_id FROM (
                        SELECT cuenta_id, modificado_en FROM carrito.carritos WHERE cuenta_id IS NOT NULL AND modificado_en <= @limite
                        UNION
                        SELECT cuenta_id, modificado_en FROM carrito.checkouts WHERE modificado_en <= @limite
                    ) AS sin_movimiento
                    GROUP BY cuenta_id
                    ORDER BY min(modificado_en), cuenta_id
                    LIMIT @maximo
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("limite", antesDe.ToUniversalTime());
                comando.Parameters.AddWithValue("maximo", maximo);
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    cuentas.Add(filas.GetGuid(0));
                }

                return cuentas;
            },
            cancelacion);

    public Task EliminarDeCuenta(Guid cuentaId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // Las líneas y las constancias de fusión caen en cascada; los avisos se borran aparte. El checkout ya se eliminó antes (D10).
                await using (var carritos = new NpgsqlCommand("DELETE FROM carrito.carritos WHERE cuenta_id = @cuenta", conexion, transaccion))
                {
                    carritos.Parameters.AddWithValue("cuenta", cuentaId);
                    await carritos.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await using var avisos = new NpgsqlCommand("DELETE FROM carrito.avisos_de_carrito WHERE cuenta_id = @cuenta", conexion, transaccion);
                avisos.Parameters.AddWithValue("cuenta", cuentaId);
                await avisos.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);

    public Task<IReadOnlyList<CarritoConEdicion>> BuscarConEdiciones(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);
        if (edicionIds.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<CarritoConEdicion>>([]);
        }

        return acceso.Ejecutar<IReadOnlyList<CarritoConEdicion>>(
            async (conexion, transaccion, token) =>
            {
                var carritos = new List<CarritoConEdicion>();
                await using var comando = new NpgsqlCommand(
                    "SELECT c.id, c.cuenta_id FROM carrito.carritos c WHERE EXISTS (SELECT 1 FROM carrito.lineas_de_carrito l WHERE l.carrito_id = c.id AND l.edicion_id = ANY(@ediciones)) ORDER BY c.id",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("ediciones", edicionIds.ToArray());
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    carritos.Add(new CarritoConEdicion(filas.GetGuid(0), AccesoAlEsquemaDelCarrito.LeerNulable<Guid>(filas, 1)));
                }

                return carritos;
            },
            cancelacion);
    }

    private sealed record CabeceraDelCarrito(Guid Id, PropietarioDelCarrito Propietario, EstadoDeCarrito Estado, int Version, DateTimeOffset CreadoEn, DateTimeOffset ModificadoEn, DateTimeOffset? VenceEn);
}
