using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Infrastructure.Reservas;
using BabelHub.Shared.Contratos;
using Npgsql;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>
/// T08: la migración del esquema <c>reservas</c> con PostgreSQL real y <b>el rol real de la aplicación</b> (no un superusuario): las restricciones son la última defensa de las reglas del Dominio, la
/// unicidad de la clave de idempotencia es por cliente y el índice de línea activa impide dos líneas activas de una misma línea de pedido aunque se salte la Aplicación.
/// </summary>
public sealed class MigracionDeReservasTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly EsquemaDeModulo Esquema = EsquemaDeModulo.Crear(DefinicionDeReservas.Esquema);

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<string> SqlStateDe(Func<Task> operacion)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(operacion);
        return error.SqlState;
    }

    private Task<string> FallaAlEjecutar(string sql) => SqlStateDe(() => bd.Ejecutar(sql));

    // --- la migración ---

    [Fact]
    [Trait("Criterio", "AC-SEL-04")]
    public async Task LaMigracionEsRepetible_YNoPierdeLosDatos()
    {
        var (pedido, reserva) = await bd.PedidoConReserva();
        await bd.AgregarLinea(reserva, pedido);

        await bd.Ejecutar(DefinicionDeReservas.Rol);
        await bd.Ejecutar(DefinicionDeReservas.Sql);
        await bd.Ejecutar(DefinicionDeReservas.Permisos);

        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reserva_lineas"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-48")]
    public async Task UnaBaseCreadaConLaRestriccionAnterior_SeActualizaDosVeces_SinPerderDatos_YAdmiteElRecojoPorSucursal()
    {
        // La restricción tal como la creó la versión anterior: el recojo exigía la sucursal de cabecera.
        await bd.Ejecutar(
            """
            ALTER TABLE reservas.reservas DROP CONSTRAINT ck_reservas_destino;
            ALTER TABLE reservas.reservas ADD CONSTRAINT ck_reservas_destino CHECK (
                (tipo_entrega = 'recojo' AND sucursal_recojo_id IS NOT NULL AND region_entrega IS NULL)
                OR (tipo_entrega = 'domicilio' AND region_entrega IS NOT NULL AND sucursal_recojo_id IS NULL));
            """);
        var (pedido, reserva) = await bd.PedidoConReserva();
        await bd.AgregarLinea(reserva, pedido);
        Assert.Equal("23514", await FallaAlEjecutar(InsertarRecojoPorSucursal()));

        for (var vez = 0; vez < 2; vez++)
        {
            await bd.Ejecutar(DefinicionDeReservas.Sql);
            await bd.Ejecutar(DefinicionDeReservas.Permisos);
        }

        await bd.Ejecutar(InsertarRecojoPorSucursal());
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE pedido_id = '{pedido}' AND sucursal_recojo_id IS NOT NULL"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reserva_lineas"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM pg_constraint WHERE conname = 'ck_reservas_destino' AND convalidated"));
    }

    private static string InsertarRecojoPorSucursal() =>
        $"""
        INSERT INTO reservas.reservas (id, pedido_id, cliente_id, clave_idempotencia, huella_de_carga, tipo_entrega, iniciada_en)
        VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', '{Guid.NewGuid():N}', '{new string('b', 64)}', 'recojo', now())
        """;

    // --- reservas (la cabecera) ---

    [Fact]
    [Trait("Criterio", "AC-SEL-09")]
    public async Task LaClaveDeIdempotencia_EsUnicaPorCliente_YOtroClientePuedeUsarLaMisma()
    {
        var cliente = Guid.NewGuid();
        await bd.AgregarReserva(clienteId: cliente, clave: "clave-repetida-0123456789");

        var repetida = await SqlStateDe(() => bd.AgregarReserva(clienteId: cliente, clave: "clave-repetida-0123456789"));
        await bd.AgregarReserva(clienteId: Guid.NewGuid(), clave: "clave-repetida-0123456789");

        Assert.Equal("23505", repetida);
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-09")]
    public async Task UnPedidoPuedeTenerVariasCabecerasEnElTiempo_ElReemplazoCreaOtra()
    {
        var pedido = Guid.NewGuid();
        await bd.AgregarReserva(pedido);
        await bd.AgregarReserva(pedido, reemplazos: 1);

        Assert.Equal(2L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE pedido_id = '{pedido}'"));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-37")]
    [InlineData(-1)]
    [InlineData(4)]
    public async Task LosReemplazosQuedanEntreCeroYElTope(int reemplazos)
    {
        var estado = await SqlStateDe(() => bd.AgregarReserva(reemplazos: reemplazos));

        Assert.Equal("23514", estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-37")]
    public async Task ElTopeDeReemplazosEsAdmitido()
    {
        await bd.AgregarReserva(reemplazos: 3);

        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas WHERE reemplazos = 3"));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-24")]
    [InlineData("recojo", false, true)]
    [InlineData("recojo", true, true)]
    [InlineData("domicilio", true, true)]
    [InlineData("domicilio", false, false)]
    [InlineData("correo", true, false)]
    public async Task ElTipoDeEntregaYSuDestinoDebenSerCoherentes(string tipo, bool conSucursal, bool conRegion)
    {
        var sucursal = conSucursal ? $"'{Guid.NewGuid()}'" : "NULL";
        var region = conRegion ? "'cusco'" : "NULL";

        var estado = await FallaAlEjecutar(
            $"""
            INSERT INTO reservas.reservas (id, pedido_id, cliente_id, clave_idempotencia, huella_de_carga, tipo_entrega, sucursal_recojo_id, region_entrega, iniciada_en)
            VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', '{Guid.NewGuid():N}', '{new string('b', 64)}', '{tipo}', {sucursal}, {region}, now())
            """);

        Assert.Equal("23514", estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-24")]
    public async Task UnaReservaADomicilioConRegionYSinSucursal_EsValida()
    {
        await bd.Ejecutar(
            $"""
            INSERT INTO reservas.reservas (id, pedido_id, cliente_id, clave_idempotencia, huella_de_carga, tipo_entrega, region_entrega, iniciada_en)
            VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', '{Guid.NewGuid():N}', '{new string('b', 64)}', 'domicilio', 'cusco', now())
            """);

        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas WHERE tipo_entrega = 'domicilio'"));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-31")]
    [InlineData("corta")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public async Task LaHuellaDeLaCargaEsUnSha256EnHexadecimalMinuscula(string huella)
    {
        var estado = await FallaAlEjecutar(
            $"""
            INSERT INTO reservas.reservas (id, pedido_id, cliente_id, clave_idempotencia, huella_de_carga, tipo_entrega, sucursal_recojo_id, iniciada_en)
            VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', '{Guid.NewGuid():N}', '{huella}', 'recojo', '{Guid.NewGuid()}', now())
            """);

        Assert.Equal("23514", estado);
    }

    // --- líneas ---

    [Theory]
    [Trait("Criterio", "AC-SEL-04")]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task LaCantidadDeUnaLineaEsPositiva(int cantidad)
    {
        var (pedido, reserva) = await bd.PedidoConReserva();

        var estado = await SqlStateDe(() => bd.AgregarLinea(reserva, pedido, cantidad: cantidad));

        Assert.Equal("23514", estado);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reserva_lineas"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-04")]
    public async Task ElEstadoDeUnaLineaEsDeLosCincoConocidos()
    {
        var (pedido, reserva) = await bd.PedidoConReserva();

        var estado = await SqlStateDe(() => bd.AgregarLinea(reserva, pedido, estado: "perdida"));

        Assert.Equal("23514", estado);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-04")]
    [InlineData("cerrada", null)]
    [InlineData("pendiente", "entrega")]
    [InlineData("confirmada", "traslado")]
    [InlineData("liberada", "reparto")]
    [InlineData("cerrada", "devolucion")]
    public async Task LaCausaDeCierreSoloVaConLaLineaCerrada_YEsDeLasTresConocidas(string estado, string? causa)
    {
        var (pedido, reserva) = await bd.PedidoConReserva();

        var sqlState = await SqlStateDe(() => bd.AgregarLinea(reserva, pedido, estado: estado, cerradaPor: causa));

        Assert.Equal("23514", sqlState);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-04")]
    [InlineData("entrega")]
    [InlineData("reparto")]
    [InlineData("traslado")]
    public async Task UnaLineaCerradaConCadaCausaEsValida(string causa)
    {
        var (pedido, reserva) = await bd.PedidoConReserva();

        await bd.AgregarLinea(reserva, pedido, estado: "cerrada", cerradaPor: causa);

        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reserva_lineas WHERE cerrada_por = '{causa}'"));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-29")]
    [InlineData("entrega", true, false)]
    [InlineData("reparto", false, true)]
    public async Task ElMarcadoDeRecreadaYDeAnuladaSoloEsDeUnaLineaCerradaPorTraslado(string causa, bool recreada, bool anulada)
    {
        var (pedido, reserva) = await bd.PedidoConReserva();

        var estado = await SqlStateDe(() => bd.AgregarLinea(reserva, pedido, estado: "cerrada", cerradaPor: causa, recreada: recreada, anulada: anulada));

        Assert.Equal("23514", estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-29")]
    public async Task UnaLineaCerradaPorTrasladoPuedeEstarRecreadaYAnulada()
    {
        var (pedido, reserva) = await bd.PedidoConReserva();

        await bd.AgregarLinea(reserva, pedido, estado: "cerrada", cerradaPor: "traslado", recreada: true, anulada: true);

        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reserva_lineas WHERE recreada_en_destino AND anulada_en IS NOT NULL"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-04")]
    public async Task UnaLineaDebeTenerSuCabecera_YElPedidoDeLaLineaEsElDeLaCabecera()
    {
        var (pedido, reserva) = await bd.PedidoConReserva();

        var sinCabecera = await SqlStateDe(() => bd.AgregarLinea(Guid.NewGuid(), pedido));
        var otroPedido = await SqlStateDe(() => bd.AgregarLinea(reserva, Guid.NewGuid()));

        Assert.Equal("23503", sinCabecera);
        Assert.Equal("23503", otroPedido);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-04")]
    public async Task UnaLineaDePedidoTieneUnaSolaLineaActiva_PendienteOConfirmada()
    {
        var (pedido, reserva) = await bd.PedidoConReserva();
        var lineaDelPedido = Guid.NewGuid();
        await bd.AgregarLinea(reserva, pedido, lineaDelPedido);

        var otraPendiente = await SqlStateDe(() => bd.AgregarLinea(reserva, pedido, lineaDelPedido));
        var otraConfirmada = await SqlStateDe(() => bd.AgregarLinea(reserva, pedido, lineaDelPedido, estado: "confirmada"));

        Assert.Equal("23505", otraPendiente);
        Assert.Equal("23505", otraConfirmada);
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reserva_lineas WHERE linea_id = '{lineaDelPedido}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-04")]
    public async Task UnaLineaInactivaNoImpideCrearLaActivaDeUnReemplazo()
    {
        var pedido = Guid.NewGuid();
        var primera = await bd.AgregarReserva(pedido);
        var segunda = await bd.AgregarReserva(pedido, reemplazos: 1);
        var lineaDelPedido = Guid.NewGuid();

        await bd.AgregarLinea(primera, pedido, lineaDelPedido, estado: "liberada");
        await bd.AgregarLinea(segunda, pedido, lineaDelPedido);

        // Hay una liberada y una pendiente de la misma línea: lo permitido. Una confirmada más no cabe.
        var tercera = await SqlStateDe(() => bd.AgregarLinea(segunda, pedido, lineaDelPedido, estado: "confirmada"));
        Assert.Equal("23505", tercera);
        Assert.Equal(2L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reserva_lineas WHERE linea_id = '{lineaDelPedido}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-04")]
    public async Task PasarUnaLineaPendienteAConfirmada_NoViolaElIndice_PeroReactivarUnaLiberadaSi()
    {
        var (pedido, reserva) = await bd.PedidoConReserva();
        var lineaDelPedido = Guid.NewGuid();
        var pendiente = await bd.AgregarLinea(reserva, pedido, lineaDelPedido);
        await bd.AgregarLinea(reserva, pedido, lineaDelPedido, estado: "liberada");

        await bd.Ejecutar($"UPDATE reservas.reserva_lineas SET estado = 'confirmada' WHERE id = '{pendiente}'");
        var reactivar = await FallaAlEjecutar($"UPDATE reservas.reserva_lineas SET estado = 'pendiente' WHERE linea_id = '{lineaDelPedido}' AND estado = 'liberada'");

        Assert.Equal("23505", reactivar);
    }

    // --- idempotencia de confirmar y cerrar, y bajas ---

    [Fact]
    [Trait("Criterio", "AC-SEL-31")]
    public async Task LaClaveDeConfirmarOCerrarSeAcotaPorPedidoYOperacion()
    {
        var pedido = Guid.NewGuid();
        await bd.Ejecutar($"INSERT INTO reservas.operaciones_idempotentes (pedido_id, tipo, clave, huella_de_carga) VALUES ('{pedido}', 'confirmar', 'k-0123456789abcdef', '{new string('c', 64)}')");

        var repetida = await FallaAlEjecutar($"INSERT INTO reservas.operaciones_idempotentes (pedido_id, tipo, clave, huella_de_carga) VALUES ('{pedido}', 'confirmar', 'k-0123456789abcdef', '{new string('d', 64)}')");
        await bd.Ejecutar($"INSERT INTO reservas.operaciones_idempotentes (pedido_id, tipo, clave, huella_de_carga) VALUES ('{pedido}', 'cerrar', 'k-0123456789abcdef', '{new string('c', 64)}')");
        await bd.Ejecutar($"INSERT INTO reservas.operaciones_idempotentes (pedido_id, tipo, clave, huella_de_carga) VALUES ('{Guid.NewGuid()}', 'confirmar', 'k-0123456789abcdef', '{new string('c', 64)}')");
        var tipoDesconocido = await FallaAlEjecutar($"INSERT INTO reservas.operaciones_idempotentes (pedido_id, tipo, clave, huella_de_carga) VALUES ('{pedido}', 'reservar', 'otra-clave-0123456789', '{new string('c', 64)}')");

        Assert.Equal("23505", repetida);
        Assert.Equal("23514", tipoDesconocido);
        Assert.Equal(3L, await bd.Consultar<long>("SELECT count(*) FROM reservas.operaciones_idempotentes"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-34")]
    public async Task UnaBajaRecibidaSeIdentificaPorSuIdentificadorOpaco_YNoSeRepite()
    {
        var titular = Guid.NewGuid();
        await bd.Ejecutar($"INSERT INTO reservas.bajas_recibidas (identificador_opaco, recibida_en) VALUES ('{titular}', now())");

        var repetida = await FallaAlEjecutar($"INSERT INTO reservas.bajas_recibidas (identificador_opaco, recibida_en) VALUES ('{titular}', now())");

        Assert.Equal("23505", repetida);
    }

    // --- el permiso mínimo de rol_reservas ---

    [Fact]
    [Trait("Criterio", "AC-SEL-04")]
    public async Task ElRolDeAcceso_PorSiSoloNoLeeElEsquema_YElRolDelModuloNoBorraReservasNiCreaTablas()
    {
        var (pedido, reserva) = await bd.PedidoConReserva();
        await bd.AgregarLinea(reserva, pedido);
        var actor = ActorActual.DelSistema();

        var sinRol = bd.Unidad(actor);
        var lecturaSinRol = await Assert.ThrowsAsync<PostgresException>(() => sinRol.Ejecutar(async token =>
        {
            await using var comando = new NpgsqlCommand("SELECT count(*) FROM reservas.reservas", (NpgsqlConnection)sinRol.Conexion, (NpgsqlTransaction)sinRol.Transaccion);
            return await comando.ExecuteScalarAsync(token);
        }));

        var borrado = await EjecutarComoElRolDelModulo(actor, "DELETE FROM reservas.reserva_lineas");
        var borradoDeCabecera = await EjecutarComoElRolDelModulo(actor, "DELETE FROM reservas.reservas");
        var creacion = await EjecutarComoElRolDelModulo(actor, "CREATE TABLE reservas.intrusa (id int)");

        Assert.Equal("42501", lecturaSinRol.SqlState);
        Assert.Equal("42501", borrado);
        Assert.Equal("42501", borradoDeCabecera);
        Assert.Equal("42501", creacion);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reserva_lineas"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-04")]
    public async Task ElRolDelModulo_PuedeLeerInsertarYActualizarReservas_YPurgarLasOperacionesIdempotentes()
    {
        var actor = ActorActual.DelSistema();
        var (pedido, reserva) = await bd.PedidoConReserva();

        var lectura = await EjecutarComoElRolDelModulo(actor, "SELECT count(*) FROM reservas.reservas");
        var actualizacion = await EjecutarComoElRolDelModulo(actor, $"UPDATE reservas.reservas SET extendida = true WHERE id = '{reserva}'");
        var insercion = await EjecutarComoElRolDelModulo(actor, $"INSERT INTO reservas.operaciones_idempotentes (pedido_id, tipo, clave, huella_de_carga) VALUES ('{pedido}', 'cerrar', 'k-0123456789abcdef', '{new string('e', 64)}')");
        var purga = await EjecutarComoElRolDelModulo(actor, "DELETE FROM reservas.operaciones_idempotentes WHERE creada_en < now() + interval '1 day'");

        Assert.Null(lectura);
        Assert.Null(actualizacion);
        Assert.Null(insercion);
        Assert.Null(purga);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.operaciones_idempotentes"));
        Assert.True(await bd.Consultar<bool>($"SELECT extendida FROM reservas.reservas WHERE id = '{reserva}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-04")]
    public async Task ElRolDelModulo_SoloCambiaLasColumnasDeEstado_NoLaCantidadNiLaFuenteNiElPedido()
    {
        var actor = ActorActual.DelSistema();
        var (pedido, reserva) = await bd.PedidoConReserva();
        var linea = await bd.AgregarLinea(reserva, pedido, cantidad: 2);

        var estado = await EjecutarComoElRolDelModulo(actor, $"UPDATE reservas.reserva_lineas SET estado = 'confirmada', confirmada_en = now(), estado_cambiado_en = now() WHERE id = '{linea}'");
        var cantidad = await EjecutarComoElRolDelModulo(actor, $"UPDATE reservas.reserva_lineas SET cantidad = 99 WHERE id = '{linea}'");
        var fuente = await EjecutarComoElRolDelModulo(actor, $"UPDATE reservas.reserva_lineas SET fuente_id = '{Guid.NewGuid()}' WHERE id = '{linea}'");
        var pedidoDeLaCabecera = await EjecutarComoElRolDelModulo(actor, $"UPDATE reservas.reservas SET pedido_id = '{Guid.NewGuid()}' WHERE id = '{reserva}'");
        var clave = await EjecutarComoElRolDelModulo(actor, $"UPDATE reservas.reservas SET clave_idempotencia = 'otra-clave-0123456789abcdef' WHERE id = '{reserva}'");
        var expiracion = await EjecutarComoElRolDelModulo(actor, $"UPDATE reservas.reservas SET fecha_expiracion = NULL, extendida = true WHERE id = '{reserva}'");
        var anonimizar = await EjecutarComoElRolDelModulo(actor, $"UPDATE reservas.reservas SET cliente_id = '{Guid.NewGuid()}', huella_origen = NULL WHERE id = '{reserva}'");

        Assert.Null(estado);
        Assert.Equal("42501", cantidad);
        Assert.Equal("42501", fuente);
        Assert.Equal("42501", pedidoDeLaCabecera);
        Assert.Equal("42501", clave);
        Assert.Null(expiracion);
        Assert.Null(anonimizar);
        Assert.Equal(2, await bd.Consultar<int>($"SELECT cantidad FROM reservas.reserva_lineas WHERE id = '{linea}'"));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-31")]
    [InlineData("corta")]
    [InlineData("una-clave-demasiado-larga-0123456789-0123456789-0123456789-0123456789-0123456789-0123456789")]
    public async Task LaClaveDeIdempotenciaTieneUnLargoAcotado(string clave)
    {
        var estado = await SqlStateDe(() => bd.AgregarReserva(clave: clave));

        Assert.Equal("23514", estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-35")]
    public async Task LaHuellaDeOrigenEsUnValorOpacoDeLargoAcotado_NoUnaDireccion()
    {
        var corta = await FallaAlEjecutar($"INSERT INTO reservas.reservas (id, pedido_id, cliente_id, huella_origen, clave_idempotencia, huella_de_carga, tipo_entrega, sucursal_recojo_id, iniciada_en) VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', '1.2.3.4', '{Guid.NewGuid():N}', '{new string('b', 64)}', 'recojo', '{Guid.NewGuid()}', now())");
        await bd.Ejecutar($"INSERT INTO reservas.reservas (id, pedido_id, cliente_id, huella_origen, clave_idempotencia, huella_de_carga, tipo_entrega, sucursal_recojo_id, iniciada_en) VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', '{new string('c', 64)}', '{Guid.NewGuid():N}', '{new string('b', 64)}', 'recojo', '{Guid.NewGuid()}', now())");

        Assert.Equal("23514", corta);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-39")]
    public async Task ElEnvioSoloLoLlevaUnaLineaCerradaPorReparto()
    {
        var (pedido, reserva) = await bd.PedidoConReserva();
        var envio = Guid.NewGuid();

        var pendiente = await FallaAlEjecutar($"INSERT INTO reservas.reserva_lineas (id, linea_id, reserva_id, pedido_id, edicion_id, fuente_id, cantidad, estado, envio_id, estado_cambiado_en) VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', '{reserva}', '{pedido}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', 1, 'pendiente', '{envio}', now())");
        var porEntrega = await FallaAlEjecutar($"INSERT INTO reservas.reserva_lineas (id, linea_id, reserva_id, pedido_id, edicion_id, fuente_id, cantidad, estado, cerrada_por, envio_id, estado_cambiado_en) VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', '{reserva}', '{pedido}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', 1, 'cerrada', 'entrega', '{envio}', now())");
        await bd.Ejecutar($"INSERT INTO reservas.reserva_lineas (id, linea_id, reserva_id, pedido_id, edicion_id, fuente_id, cantidad, estado, cerrada_por, envio_id, estado_cambiado_en) VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', '{reserva}', '{pedido}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', 1, 'cerrada', 'reparto', '{envio}', now())");

        Assert.Equal("23514", pendiente);
        Assert.Equal("23514", porEntrega);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-31")]
    [InlineData("corta", "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc")]
    [InlineData("una-clave-valida-0123456789", "no-es-un-sha256")]
    public async Task LaClaveYLaHuellaDeUnaOperacionIdempotenteTienenFormaYLargoAcotados(string clave, string huella)
    {
        var estado = await FallaAlEjecutar($"INSERT INTO reservas.operaciones_idempotentes (pedido_id, tipo, clave, huella_de_carga) VALUES ('{Guid.NewGuid()}', 'cerrar', '{clave}', '{huella}')");

        Assert.Equal("23514", estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-29")]
    public async Task UnaLineaQueNoEstaCerradaNoPuedeLlevarRecreadaNiAnulada()
    {
        var (pedido, reserva) = await bd.PedidoConReserva();

        var recreada = await SqlStateDe(() => bd.AgregarLinea(reserva, pedido, estado: "pendiente", recreada: true));
        var anulada = await SqlStateDe(() => bd.AgregarLinea(reserva, pedido, estado: "confirmada", anulada: true));

        Assert.Equal("23514", recreada);
        Assert.Equal("23514", anulada);
    }

    /// <summary>Corre una sentencia con el rol del módulo y devuelve el <c>SqlState</c> del fallo, o <see langword="null"/> si se ejecutó.</summary>
    private async Task<string?> EjecutarComoElRolDelModulo(ActorActual actor, string sql)
    {
        var unidad = bd.Unidad(actor);
        try
        {
            await unidad.Ejecutar(async token =>
            {
                await unidad.EjecutarComo(Esquema, async _ =>
                {
                    await using var comando = new NpgsqlCommand(sql, (NpgsqlConnection)unidad.Conexion, (NpgsqlTransaction)unidad.Transaccion);
                    await comando.ExecuteNonQueryAsync(token);
                }, token);
                return 0;
            });
            return null;
        }
        catch (PostgresException error)
        {
            return error.SqlState;
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-04")]
    public async Task ElRolDeAcceso_NoEsSuperusuarioNiSaltaLaSeguridadPorFilas_NiEsMiembroDelPropietario()
    {
        Assert.False(await bd.Consultar<bool>("SELECT rolsuper FROM pg_roles WHERE rolname = 'acceso'"));
        Assert.False(await bd.Consultar<bool>("SELECT rolbypassrls FROM pg_roles WHERE rolname = 'acceso'"));
        Assert.False(await bd.Consultar<bool>("SELECT pg_has_role('acceso', 'propietario', 'MEMBER')"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-04")]
    public async Task NingunaTablaDeReservasTieneClaveForaneaHaciaOtroEsquema()
    {
        var foraneasAjenas = await bd.Consultar<long>(
            """
            SELECT count(*) FROM pg_constraint c
            JOIN pg_class origen ON origen.oid = c.conrelid
            JOIN pg_namespace no ON no.oid = origen.relnamespace
            JOIN pg_class destino ON destino.oid = c.confrelid
            JOIN pg_namespace nd ON nd.oid = destino.relnamespace
            WHERE c.contype = 'f' AND no.nspname = 'reservas' AND nd.nspname <> 'reservas'
            """);

        Assert.Equal(0L, foraneasAjenas);
    }
}
