using System.Text.Json;
using System.Text.Json.Serialization;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Domain.Carrito;
using Npgsql;
using NpgsqlTypes;
using Tipo = BabelHub.Ecommerce.Domain.Carrito.TipoDeEntrega;

namespace BabelHub.Ecommerce.Infrastructure.Carrito;

/// <summary>
/// El repositorio de checkouts sobre el esquema <c>carrito</c> (T24): a lo más uno vivo por cuenta (índice único parcial, traducido a <see cref="CheckoutYaExisteException"/>); cada lectura <c>conBloqueo</c> es <c>SELECT … FOR UPDATE</c> y entrega una
/// entidad nueva leída bajo el bloqueo; <see cref="ObtenerVivosDeCuentasConBloqueo"/> bloquea por <c>id</c> ascendente (D10). El contacto viaja como texto y nunca en un mensaje de error (<see cref="FallaDeLaBaseDelCarritoException"/>). Las líneas se
/// reemplazan enteras al guardar conservando el <c>linea_id</c> que traen del agregado, y la versión solo avanza.
/// </summary>
public sealed class RepositorioDeCheckoutsPostgres(AccesoAlEsquemaDelCarrito acceso) : IRepositorioDeCheckouts
{
    private const string Vivos = "('en_edicion', 'reservando', 'en_pago', 'pago_pendiente')";

    private const string Terminados = "('pagado', 'cancelado', 'reiniciado')";

    private const string Columnas =
        "id, cuenta_id, carrito_id, estado, paso, version, tipo_de_entrega, codigo_de_sucursal_de_recojo, region_de_entrega, direccion, contacto_nombre, contacto_telefono, envio_congelado, total_congelado, "
        + "pedido_id, reservando_desde, huella_de_reserva, generacion_de_reserva, huella_de_instantanea, reserva_expira_en, pedido_descartado, reserva_a_liberar_desde, pedido_a_liberar, limite_pendiente_en, "
        + "pago_pendiente_desde, terminos_version, terminos_aceptado_en, privacidad_version, privacidad_aceptado_en, reembolso_digital_version, reembolso_digital_aceptado_en, creado_en, modificado_en, envios_congelados";

    private static readonly string[] RestriccionesDelVivo = ["checkouts_uno_vivo_por_cuenta", "checkouts_pkey", "checkouts_pedido_unico"];

    // ---------- Lecturas ----------

    public Task<Checkout?> ObtenerVivoDeCuenta(Guid cuentaId, bool conBloqueo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var lista = await Leer(conexion, transaccion, $"cuenta_id = @valor AND estado IN {Vivos}", [new NpgsqlParameter("valor", cuentaId)], conBloqueo ? " FOR UPDATE" : string.Empty, token).ConfigureAwait(false);
                return lista.FirstOrDefault();
            },
            cancelacion);

    public Task<Checkout?> ObtenerPorPedido(Guid pedidoId, bool conBloqueo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var lista = await Leer(
                    conexion,
                    transaccion,
                    "(pedido_id = @valor OR pedido_descartado = @valor OR pedido_a_liberar = @valor)",
                    [new NpgsqlParameter("valor", pedidoId)],
                    $" ORDER BY (estado IN {Vivos}) DESC, creado_en DESC LIMIT 1{(conBloqueo ? " FOR UPDATE" : string.Empty)}",
                    token).ConfigureAwait(false);
                return lista.FirstOrDefault();
            },
            cancelacion);

    public Task<IReadOnlyList<Checkout>> ObtenerVivosDeCuentasConBloqueo(IReadOnlyCollection<Guid> cuentaIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(cuentaIds);
        if (cuentaIds.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<Checkout>>([]);
        }

        return acceso.Ejecutar<IReadOnlyList<Checkout>>(
            async (conexion, transaccion, token) => await Leer(conexion, transaccion, $"cuenta_id = ANY(@valor) AND estado IN {Vivos}", [new NpgsqlParameter("valor", cuentaIds.ToArray())], " ORDER BY id FOR UPDATE", token).ConfigureAwait(false),
            cancelacion);
    }

    public Task<EstadoDelCheckout?> LeerEstadoSinBloqueo(Guid cuentaId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand($"SELECT estado FROM carrito.checkouts WHERE cuenta_id = @cuenta AND estado IN {Vivos}", conexion, transaccion);
                comando.Parameters.AddWithValue("cuenta", cuentaId);
                return await comando.ExecuteScalarAsync(token).ConfigureAwait(false) is string estado ? (EstadoDelCheckout?)DeEstado(estado) : null;
            },
            cancelacion);

    // ---------- Escrituras ----------

    public async Task Agregar(Checkout checkout, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        try
        {
            await acceso.Ejecutar(
                async (conexion, transaccion, token) =>
                {
                    await using (var comando = new NpgsqlCommand(
                        $"INSERT INTO carrito.checkouts ({Columnas}) VALUES (@id, @cuenta, @carrito, @estado, @paso, @version, @tipo, @sucursal, @region, @direccion, @nombre, @telefono, @envio, @total, @pedido, @reservando, "
                        + "@huellaReserva, @generacion, @huellaInstantanea, @expira, @descartado, @liberarDesde, @liberar, @limite, @pendienteDesde, @terminosVersion, @terminosEn, @privacidadVersion, @privacidadEn, @reembolsoVersion, @reembolsoEn, @creado, @modificado, @envios)",
                        conexion,
                        transaccion))
                    {
                        Parametros(comando, checkout, esNuevo: true);
                        await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    await InsertarLineas(conexion, transaccion, checkout, token).ConfigureAwait(false);
                    return 0;
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (FallaDeLaBaseDelCarritoException falla) when (AccesoAlEsquemaDelCarrito.EsUnicidad(falla, RestriccionesDelVivo))
        {
            throw new CheckoutYaExisteException();
        }
    }

    public Task Guardar(Checkout checkout, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using (var comando = new NpgsqlCommand(
                    """
                    UPDATE carrito.checkouts SET
                        estado = @estado, paso = @paso, version = @version, tipo_de_entrega = @tipo, codigo_de_sucursal_de_recojo = @sucursal, region_de_entrega = @region, direccion = @direccion,
                        contacto_nombre = @nombre, contacto_telefono = @telefono, envio_congelado = @envio, total_congelado = @total, envios_congelados = @envios, pedido_id = @pedido, reservando_desde = @reservando,
                        huella_de_reserva = @huellaReserva, generacion_de_reserva = @generacion, huella_de_instantanea = @huellaInstantanea, reserva_expira_en = @expira, pedido_descartado = @descartado,
                        reserva_a_liberar_desde = @liberarDesde, pedido_a_liberar = @liberar, limite_pendiente_en = @limite, pago_pendiente_desde = @pendienteDesde,
                        terminos_version = @terminosVersion, terminos_aceptado_en = @terminosEn, privacidad_version = @privacidadVersion, privacidad_aceptado_en = @privacidadEn,
                        reembolso_digital_version = @reembolsoVersion, reembolso_digital_aceptado_en = @reembolsoEn, modificado_en = @modificado
                    WHERE id = @id AND version <= @version
                    """,
                    conexion,
                    transaccion))
                {
                    Parametros(comando, checkout, esNuevo: false);
                    if (await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
                    {
                        throw new InvalidOperationException("El checkout no existe o ya lo guardó una versión más nueva.");
                    }
                }

                await using (var borrar = new NpgsqlCommand("DELETE FROM carrito.lineas_del_checkout WHERE checkout_id = @checkout", conexion, transaccion))
                {
                    borrar.Parameters.AddWithValue("checkout", checkout.Id);
                    await borrar.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await InsertarLineas(conexion, transaccion, checkout, token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    // ---------- Trabajos de fondo y eliminación ----------

    public Task<IReadOnlyList<Guid>> CuentasConCheckoutVivoSinMovimientoDesde(DateTimeOffset antesDe, int maximo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<Guid>>(
            async (conexion, transaccion, token) =>
            {
                var cuentas = new List<Guid>();
                await using var comando = new NpgsqlCommand(
                    $"SELECT cuenta_id FROM carrito.checkouts WHERE estado IN {Vivos} AND modificado_en <= @limite ORDER BY modificado_en, id LIMIT @maximo",
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

    public Task<int> EliminarTerminadosAnterioresA(DateTimeOffset antesDe, int maximo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // Sin una reserva por liberar: si no, se perdería la anotación que reintenta LiberarReservasPendientes.
                await using var comando = new NpgsqlCommand(
                    $"DELETE FROM carrito.checkouts WHERE id IN (SELECT id FROM carrito.checkouts WHERE estado IN {Terminados} AND reserva_a_liberar_desde IS NULL AND modificado_en <= @limite ORDER BY modificado_en, id LIMIT @maximo)",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("limite", antesDe.ToUniversalTime());
                comando.Parameters.AddWithValue("maximo", maximo);
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            },
            cancelacion);

    public Task<int> ContarConElPagoPendienteDesdeAntesDe(DateTimeOffset antesDe, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT count(*) FROM carrito.checkouts WHERE estado = 'pago_pendiente' AND pago_pendiente_desde < @limite", conexion, transaccion);
                comando.Parameters.AddWithValue("limite", antesDe.ToUniversalTime());
                return Convert.ToInt32(await comando.ExecuteScalarAsync(token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            },
            cancelacion);

    public Task EliminarDeCuenta(Guid cuentaId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("DELETE FROM carrito.checkouts WHERE cuenta_id = @cuenta", conexion, transaccion);
                comando.Parameters.AddWithValue("cuenta", cuentaId);
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);

    public Task<IReadOnlyList<PedidoPorLiberar>> ConLiberacionPendiente(int maximo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<PedidoPorLiberar>>(
            async (conexion, transaccion, token) =>
            {
                var pendientes = new List<PedidoPorLiberar>();
                await using var comando = new NpgsqlCommand(
                    "SELECT id, pedido_a_liberar FROM carrito.checkouts WHERE reserva_a_liberar_desde IS NOT NULL AND pedido_a_liberar IS NOT NULL ORDER BY reserva_a_liberar_desde, id LIMIT @maximo",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("maximo", maximo);
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    pendientes.Add(new PedidoPorLiberar(filas.GetGuid(0), filas.GetGuid(1)));
                }

                return pendientes;
            },
            cancelacion);

    public Task<IReadOnlyList<CheckoutPorRecuperar>> ReservandoDesdeAntesDe(DateTimeOffset antesDe, int maximo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<CheckoutPorRecuperar>>(
            async (conexion, transaccion, token) =>
            {
                var atascados = new List<CheckoutPorRecuperar>();
                await using var comando = new NpgsqlCommand(
                    "SELECT cuenta_id, pedido_id, generacion_de_reserva FROM carrito.checkouts WHERE estado = 'reservando' AND pedido_id IS NOT NULL AND reservando_desde <= @limite ORDER BY reservando_desde, id LIMIT @maximo",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("limite", antesDe.ToUniversalTime());
                comando.Parameters.AddWithValue("maximo", maximo);
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    // Un corte pudo dejar la intención creada: se intenta cancelarla siempre (idempotente).
                    atascados.Add(new CheckoutPorRecuperar(filas.GetGuid(0), filas.GetGuid(1), filas.GetInt32(2), IntencionPudoCrearse: true));
                }

                return atascados;
            },
            cancelacion);

    // ---------- Piezas ----------

    private static async Task<List<Checkout>> Leer(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string filtro, NpgsqlParameter[] parametros, string sufijo, CancellationToken token)
    {
        var datos = new List<DatosGuardadosDelCheckout>();
        await using (var comando = new NpgsqlCommand($"SELECT {Columnas} FROM carrito.checkouts WHERE {filtro}{sufijo}", conexion, transaccion))
        {
            comando.Parameters.AddRange(parametros);
            var cabeceras = new List<Cabecera>();
            await using (var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    cabeceras.Add(LeerCabecera(filas));
                }
            }

            foreach (var cabecera in cabeceras)
            {
                var lineas = await LeerLineas(conexion, transaccion, cabecera.Id, token).ConfigureAwait(false);
                datos.Add(cabecera.ConLineas(lineas));
            }
        }

        return [.. datos.Select(Checkout.Restaurar)];
    }

    private static Cabecera LeerCabecera(NpgsqlDataReader f)
    {
        var aceptaciones = new List<AceptacionDeTexto>();
        Agregar(aceptaciones, TextoLegal.Terminos, f, 25, 26);
        Agregar(aceptaciones, TextoLegal.Privacidad, f, 27, 28);
        Agregar(aceptaciones, TextoLegal.ReembolsoDigital, f, 29, 30);
        return new Cabecera(
            f.GetGuid(0),
            f.GetGuid(1),
            f.GetGuid(2),
            DeEstado(f.GetString(3)),
            (PasoDelCheckout)f.GetInt16(4),
            f.GetInt32(5),
            AccesoAlEsquemaDelCarrito.LeerTexto(f, 6) is { } tipo ? DeTipo(tipo) : null,
            AccesoAlEsquemaDelCarrito.LeerTexto(f, 7),
            AccesoAlEsquemaDelCarrito.LeerTexto(f, 8),
            AccesoAlEsquemaDelCarrito.LeerTexto(f, 9),
            AccesoAlEsquemaDelCarrito.LeerTexto(f, 10),
            AccesoAlEsquemaDelCarrito.LeerTexto(f, 11),
            AccesoAlEsquemaDelCarrito.LeerNulable<decimal>(f, 12),
            AccesoAlEsquemaDelCarrito.LeerNulable<decimal>(f, 13),
            AccesoAlEsquemaDelCarrito.LeerNulable<Guid>(f, 14),
            AccesoAlEsquemaDelCarrito.LeerInstante(f, 15),
            AccesoAlEsquemaDelCarrito.LeerTexto(f, 16),
            f.GetInt32(17),
            AccesoAlEsquemaDelCarrito.LeerTexto(f, 18),
            AccesoAlEsquemaDelCarrito.LeerInstante(f, 19),
            AccesoAlEsquemaDelCarrito.LeerNulable<Guid>(f, 20),
            AccesoAlEsquemaDelCarrito.LeerInstante(f, 21),
            AccesoAlEsquemaDelCarrito.LeerNulable<Guid>(f, 22),
            AccesoAlEsquemaDelCarrito.LeerInstante(f, 23),
            AccesoAlEsquemaDelCarrito.LeerInstante(f, 24),
            aceptaciones,
            AccesoAlEsquemaDelCarrito.LeerInstante(f, 31)!.Value,
            AccesoAlEsquemaDelCarrito.LeerInstante(f, 32)!.Value,
            DeTextoDeEnvios(AccesoAlEsquemaDelCarrito.LeerTexto(f, 33)));
    }

    private static void Agregar(List<AceptacionDeTexto> aceptaciones, TextoLegal texto, NpgsqlDataReader f, int version, int fecha)
    {
        if (AccesoAlEsquemaDelCarrito.LeerTexto(f, version) is { } v && AccesoAlEsquemaDelCarrito.LeerInstante(f, fecha) is { } en)
        {
            aceptaciones.Add(new AceptacionDeTexto(texto, v, en));
        }
    }

    private static async Task<List<LineaDelCheckout>> LeerLineas(NpgsqlConnection conexion, NpgsqlTransaction transaccion, Guid checkoutId, CancellationToken token)
    {
        var lineas = new List<LineaDelCheckout>();
        await using var comando = new NpgsqlCommand(
            "SELECT linea_id, edicion_id, es_digital, titulo, isbn, cantidad, precio_unitario, codigo_de_fuente_informado, requiere_traslado_informado, plazo_informado_dias, consultado_en, linea_de_carrito_id, codigo_de_sucursal FROM carrito.lineas_del_checkout WHERE checkout_id = @checkout ORDER BY posicion",
            conexion,
            transaccion);
        comando.Parameters.AddWithValue("checkout", checkoutId);
        await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await filas.ReadAsync(token).ConfigureAwait(false))
        {
            lineas.Add(LineaDelCheckout.Restaurar(
                filas.GetGuid(0),
                filas.GetGuid(1),
                filas.GetBoolean(2),
                filas.GetString(3),
                filas.GetString(4),
                filas.GetInt32(5),
                filas.GetDecimal(6),
                AccesoAlEsquemaDelCarrito.LeerTexto(filas, 7),
                AccesoAlEsquemaDelCarrito.LeerNulable<bool>(filas, 8),
                AccesoAlEsquemaDelCarrito.LeerNulable<int>(filas, 9),
                AccesoAlEsquemaDelCarrito.LeerInstante(filas, 10),
                AccesoAlEsquemaDelCarrito.LeerNulable<Guid>(filas, 11),
                AccesoAlEsquemaDelCarrito.LeerTexto(filas, 12)));
        }

        return lineas;
    }

    private static async Task InsertarLineas(NpgsqlConnection conexion, NpgsqlTransaction transaccion, Checkout checkout, CancellationToken token)
    {
        var posicion = 0;
        foreach (var linea in checkout.Lineas)
        {
            await using var comando = new NpgsqlCommand(
                "INSERT INTO carrito.lineas_del_checkout (checkout_id, edicion_id, posicion, linea_id, linea_de_carrito_id, codigo_de_sucursal, es_digital, titulo, isbn, cantidad, precio_unitario, codigo_de_fuente_informado, requiere_traslado_informado, plazo_informado_dias, consultado_en) "
                + "VALUES (@checkout, @edicion, @posicion, @linea, @lineaDeCarrito, @sucursal, @digital, @titulo, @isbn, @cantidad, @precio, @fuente, @traslado, @plazo, @consultado)",
                conexion,
                transaccion);
            comando.Parameters.AddWithValue("checkout", checkout.Id);
            comando.Parameters.AddWithValue("edicion", linea.EdicionId);
            comando.Parameters.AddWithValue("posicion", posicion++);
            comando.Parameters.AddWithValue("linea", linea.LineaId);
            comando.Parameters.AddWithValue("lineaDeCarrito", NpgsqlDbType.Uuid, AccesoAlEsquemaDelCarrito.ONulo(linea.LineaDeCarritoId));
            comando.Parameters.AddWithValue("sucursal", NpgsqlDbType.Text, AccesoAlEsquemaDelCarrito.ONulo(linea.CodigoDeSucursal));
            comando.Parameters.AddWithValue("digital", linea.EsDigital);
            comando.Parameters.AddWithValue("titulo", linea.Titulo);
            comando.Parameters.AddWithValue("isbn", linea.Isbn);
            comando.Parameters.AddWithValue("cantidad", linea.Cantidad.Valor);
            comando.Parameters.AddWithValue("precio", linea.PrecioUnitario);
            comando.Parameters.AddWithValue("fuente", NpgsqlDbType.Text, AccesoAlEsquemaDelCarrito.ONulo(linea.CodigoDeFuenteInformado));
            comando.Parameters.AddWithValue("traslado", NpgsqlDbType.Boolean, AccesoAlEsquemaDelCarrito.ONulo(linea.RequiereTrasladoInformado));
            comando.Parameters.AddWithValue("plazo", NpgsqlDbType.Integer, AccesoAlEsquemaDelCarrito.ONulo(linea.PlazoInformadoDias));
            comando.Parameters.AddWithValue("consultado", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDelCarrito.UtcONulo(linea.ConsultadoEn));
            await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }

    private static void Parametros(NpgsqlCommand comando, Checkout c, bool esNuevo)
    {
        var p = comando.Parameters;
        p.AddWithValue("id", c.Id);
        if (esNuevo)
        {
            p.AddWithValue("cuenta", c.CuentaId);
            p.AddWithValue("carrito", c.CarritoId);
            p.AddWithValue("creado", c.CreadoEn.ToUniversalTime());
        }

        p.AddWithValue("estado", AEstado(c.Estado));
        p.AddWithValue("paso", (short)c.PasoDeEdicion);
        p.AddWithValue("version", c.Version);
        p.AddWithValue("tipo", NpgsqlDbType.Text, AccesoAlEsquemaDelCarrito.ONulo(c.TipoDeEntrega is { } t ? ATipo(t) : null));
        p.AddWithValue("sucursal", NpgsqlDbType.Text, AccesoAlEsquemaDelCarrito.ONulo(c.CodigoDeSucursalDeRecojo));
        p.AddWithValue("region", NpgsqlDbType.Text, AccesoAlEsquemaDelCarrito.ONulo(c.RegionDeEntrega));
        p.AddWithValue("direccion", NpgsqlDbType.Text, AccesoAlEsquemaDelCarrito.ONulo(c.Contacto?.Direccion.Texto));
        p.AddWithValue("nombre", NpgsqlDbType.Text, AccesoAlEsquemaDelCarrito.ONulo(c.Contacto?.Nombre.Valor));
        p.AddWithValue("telefono", NpgsqlDbType.Text, AccesoAlEsquemaDelCarrito.ONulo(c.Contacto?.Telefono.Valor));
        p.AddWithValue("envio", NpgsqlDbType.Numeric, AccesoAlEsquemaDelCarrito.ONulo(c.EnvioCongelado));
        p.AddWithValue("total", NpgsqlDbType.Numeric, AccesoAlEsquemaDelCarrito.ONulo(c.TotalCongelado));
        p.AddWithValue("envios", NpgsqlDbType.Jsonb, AccesoAlEsquemaDelCarrito.ONulo(EnviosATexto(c.EnviosCongelados)));
        p.AddWithValue("pedido", NpgsqlDbType.Uuid, AccesoAlEsquemaDelCarrito.ONulo(c.PedidoId));
        p.AddWithValue("reservando", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDelCarrito.UtcONulo(c.ReservandoDesde));
        p.AddWithValue("huellaReserva", NpgsqlDbType.Text, AccesoAlEsquemaDelCarrito.ONulo(c.HuellaDeReserva));
        p.AddWithValue("generacion", c.GeneracionDeReserva);
        p.AddWithValue("huellaInstantanea", NpgsqlDbType.Text, AccesoAlEsquemaDelCarrito.ONulo(c.HuellaDeInstantanea));
        p.AddWithValue("expira", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDelCarrito.UtcONulo(c.ReservaExpiraEn));
        p.AddWithValue("descartado", NpgsqlDbType.Uuid, AccesoAlEsquemaDelCarrito.ONulo(c.PedidoDescartado));
        p.AddWithValue("liberarDesde", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDelCarrito.UtcONulo(c.ReservaALiberarDesde));
        p.AddWithValue("liberar", NpgsqlDbType.Uuid, AccesoAlEsquemaDelCarrito.ONulo(c.PedidoALiberar));
        p.AddWithValue("limite", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDelCarrito.UtcONulo(c.LimitePendienteEn));
        p.AddWithValue("pendienteDesde", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDelCarrito.UtcONulo(c.PagoPendienteDesde));
        Aceptacion(p, c, TextoLegal.Terminos, "terminosVersion", "terminosEn");
        Aceptacion(p, c, TextoLegal.Privacidad, "privacidadVersion", "privacidadEn");
        Aceptacion(p, c, TextoLegal.ReembolsoDigital, "reembolsoVersion", "reembolsoEn");
        p.AddWithValue("modificado", c.ModificadoPorLaPersonaEn.ToUniversalTime());
    }

    private static void Aceptacion(NpgsqlParameterCollection p, Checkout c, TextoLegal texto, string version, string fecha)
    {
        var aceptada = c.Aceptaciones.Aceptadas.FirstOrDefault(a => a.Texto == texto);
        p.AddWithValue(version, NpgsqlDbType.Text, AccesoAlEsquemaDelCarrito.ONulo(aceptada?.Version));
        p.AddWithValue(fecha, NpgsqlDbType.TimestampTz, aceptada is null ? DBNull.Value : aceptada.AceptadoEn.ToUniversalTime());
    }

    private static string AEstado(EstadoDelCheckout estado) => estado switch
    {
        EstadoDelCheckout.EnEdicion => "en_edicion",
        EstadoDelCheckout.Reservando => "reservando",
        EstadoDelCheckout.EnPago => "en_pago",
        EstadoDelCheckout.PagoPendiente => "pago_pendiente",
        EstadoDelCheckout.Pagado => "pagado",
        EstadoDelCheckout.Cancelado => "cancelado",
        EstadoDelCheckout.Reiniciado => "reiniciado",
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };

    private static EstadoDelCheckout DeEstado(string estado) => estado switch
    {
        "en_edicion" => EstadoDelCheckout.EnEdicion,
        "reservando" => EstadoDelCheckout.Reservando,
        "en_pago" => EstadoDelCheckout.EnPago,
        "pago_pendiente" => EstadoDelCheckout.PagoPendiente,
        "pagado" => EstadoDelCheckout.Pagado,
        "cancelado" => EstadoDelCheckout.Cancelado,
        "reiniciado" => EstadoDelCheckout.Reiniciado,
        _ => throw new InvalidOperationException("El checkout guardado tiene un estado desconocido."),
    };

    /// <summary>Lo congelado de cada sucursal tal como se guarda en <c>envios_congelados</c>: solo el código y el importe, nunca el nombre.</summary>
    private sealed record EnvioGuardado([property: JsonPropertyName("sucursal")] string Sucursal, [property: JsonPropertyName("importe")] decimal Importe);

    private static string? EnviosATexto(IReadOnlyList<EnvioDeSucursal>? envios) =>
        envios is null ? null : JsonSerializer.Serialize(envios.Select(e => new EnvioGuardado(e.CodigoDeSucursal, e.Costo.Valor)));

    private static List<EnvioDeSucursal>? DeTextoDeEnvios(string? texto) =>
        texto is null
            ? null
            : [.. (JsonSerializer.Deserialize<List<EnvioGuardado>>(texto) ?? throw new InvalidOperationException("El checkout guardado tiene un envío por sucursal vacío.")).Select(e => new EnvioDeSucursal(e.Sucursal, CostoDeEnvio.Crear(e.Importe)))];

    private static string ATipo(Tipo tipo) => tipo switch
    {
        Tipo.Recojo => "recojo",
        Tipo.Domicilio => "domicilio",
        Tipo.Digital => "digital",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
    };

    private static Tipo DeTipo(string tipo) => tipo switch
    {
        "recojo" => Tipo.Recojo,
        "domicilio" => Tipo.Domicilio,
        "digital" => Tipo.Digital,
        _ => throw new InvalidOperationException("El checkout guardado tiene un tipo de entrega desconocido."),
    };

    private sealed record Cabecera(
        Guid Id,
        Guid CuentaId,
        Guid CarritoId,
        EstadoDelCheckout Estado,
        PasoDelCheckout Paso,
        int Version,
        Tipo? Entrega,
        string? Sucursal,
        string? Region,
        string? Direccion,
        string? Nombre,
        string? Telefono,
        decimal? Envio,
        decimal? Total,
        Guid? PedidoId,
        DateTimeOffset? ReservandoDesde,
        string? HuellaDeReserva,
        int Generacion,
        string? HuellaDeInstantanea,
        DateTimeOffset? ReservaExpiraEn,
        Guid? PedidoDescartado,
        DateTimeOffset? ReservaALiberarDesde,
        Guid? PedidoALiberar,
        DateTimeOffset? LimitePendienteEn,
        DateTimeOffset? PagoPendienteDesde,
        IReadOnlyList<AceptacionDeTexto> Aceptaciones,
        DateTimeOffset CreadoEn,
        DateTimeOffset Modificado,
        IReadOnlyList<EnvioDeSucursal>? Envios)
    {
        public DatosGuardadosDelCheckout ConLineas(IReadOnlyList<LineaDelCheckout> lineas) => new()
        {
            Id = Id,
            CuentaId = CuentaId,
            CarritoId = CarritoId,
            Estado = Estado,
            Version = Version,
            CreadoEn = CreadoEn,
            ModificadoPorLaPersonaEn = Modificado,
            PasoDeEdicion = Paso,
            Lineas = lineas,
            Aceptaciones = Aceptaciones,
            TipoDeEntrega = Entrega,
            CodigoDeSucursalDeRecojo = Sucursal,
            RegionDeEntrega = Region,
            ContactoNombre = Nombre,
            ContactoTelefono = Telefono,
            ContactoDireccion = Direccion,
            EnvioCongelado = Envio,
            TotalCongelado = Total,
            EnviosCongelados = Envios,
            PedidoId = PedidoId,
            PedidoDescartado = PedidoDescartado,
            ReservandoDesde = ReservandoDesde,
            HuellaDeReserva = HuellaDeReserva,
            GeneracionDeReserva = Generacion,
            HuellaDeInstantanea = HuellaDeInstantanea,
            ReservaExpiraEn = ReservaExpiraEn,
            ReservaALiberarDesde = ReservaALiberarDesde,
            PedidoALiberar = PedidoALiberar,
            LimitePendienteEn = LimitePendienteEn,
            PagoPendienteDesde = PagoPendienteDesde,
        };
    }
}
