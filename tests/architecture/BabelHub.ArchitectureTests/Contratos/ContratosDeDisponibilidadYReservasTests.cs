using System.Text.Json;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Inventario;
using BabelHub.Shared.Errores;

namespace BabelHub.ArchitectureTests.Contratos;

/// <summary>
/// Lo que el E-commerce y el Inventario congelan sobre disponibilidad y reservas (FUE-05, FUE-07, FUE-13, SEL-01 a SEL-35):
/// cada operación con su ruta, alcance, token, clave de idempotencia, DTOs y errores. Cambiar una fila es un cambio de contrato.
/// </summary>
public sealed class ContratosDeDisponibilidadYReservasTests
{
    private const string Usuario = AlcancesDeCredencial.OperacionDeUsuario;
    private const string Lectura = AlcancesDeCredencial.LecturaDeDisponibilidad;
    private const string Reservas = AlcancesDeCredencial.ExpiracionDeReservas;

    /// <summary>Nombre, método, ruta, alcance, token de usuario, clave de idempotencia, código de éxito, solicitud, respuesta, es de solo lectura.</summary>
    public static TheoryData<string, string, string, string, bool, bool, int, Type?, Type?, bool> Operaciones() => new()
    {
        { "inventario.ediciones.registrar", "PUT", "/api/inventario/v1/ediciones/{edicionId}", Usuario, true, false, 204, typeof(RegistrarEdicionDto), null, false },
        { "inventario.disponibilidad.consultar", "GET", "/api/inventario/v1/disponibilidad/{edicionId}", Lectura, false, false, 200, null, typeof(DisponibilidadDeEdicionDto), true },
        { "inventario.disponibilidad.consultar-varias", "POST", "/api/inventario/v1/disponibilidad/consultas", Lectura, false, false, 200, typeof(ConsultaDeDisponibilidadDto), typeof(DisponibilidadDeEdicionesDto), true },
        { "inventario.reservas.consulta-previa", "POST", "/api/inventario/v1/reservas/consulta-previa", Lectura, false, false, 200, typeof(ConsultaPreviaDto), typeof(ConsultaPreviaRespuestaDto), true },
        { "inventario.reservas.crear", "POST", "/api/inventario/v1/reservas", Usuario, true, true, 201, typeof(CrearReservaDto), typeof(ReservaDto), false },
        { "inventario.reservas.consultar", "GET", "/api/inventario/v1/reservas/{pedidoId}", Usuario, true, false, 200, null, typeof(ReservaDto), true },
        { "inventario.reservas.confirmar", "POST", "/api/inventario/v1/reservas/{pedidoId}/confirmar", Reservas, false, true, 200, null, typeof(ReservaDto), false },
        { "inventario.reservas.extender", "POST", "/api/inventario/v1/reservas/{pedidoId}/extender", Reservas, false, false, 200, null, typeof(ExtensionDeReservaDto), false },
        { "inventario.reservas.liberar", "POST", "/api/inventario/v1/reservas/{pedidoId}/liberar", Reservas, false, false, 200, typeof(LiberarReservaDto), typeof(ReservaDto), false },
        { "inventario.reservas.cerrar", "POST", "/api/inventario/v1/reservas/{pedidoId}/cerrar", Usuario, true, true, 200, typeof(CerrarReservaDto), typeof(CierreDeReservaDto), false },
    };

    [Theory]
    [MemberData(nameof(Operaciones))]
    [Trait("Criterio", "AC-SEL-09")]
    public void Cada_operacion_esta_congelada_con_su_ruta_alcance_y_dtos(
        string nombre, string metodo, string ruta, string alcance, bool token, bool clave, int exito, Type? solicitud, Type? respuesta, bool lectura)
    {
        var operacion = CatalogoDeOperaciones.V1.SingleOrDefault(o => o.Nombre == nombre);

        Assert.NotNull(operacion);
        Assert.Equal(metodo, operacion.Metodo);
        Assert.Equal(ruta, operacion.Ruta);
        Assert.Equal(alcance, operacion.Alcance);
        Assert.Equal(token, operacion.RequiereTokenDeUsuario);
        Assert.Equal(clave, operacion.RequiereClaveDeIdempotencia);
        Assert.Equal(exito, operacion.CodigoDeExito);
        Assert.Equal(solicitud, operacion.Solicitud);
        Assert.Equal(respuesta, operacion.Respuesta);
        Assert.Equal(lectura, operacion.EsLectura);
    }

    [Fact]
    public void Crear_una_reserva_declara_los_errores_de_stock_limites_e_idempotencia()
    {
        var crear = CatalogoDeOperaciones.V1.Single(o => o.Nombre == "inventario.reservas.crear");

        Assert.Equal(
            new[]
            {
                CodigosDeErrorDelInventario.EdicionNoImpresa,
                CodigosDeErrorDelInventario.IdempotenciaEnConflicto,
                CodigosDeErrorDelInventario.IdempotenciaEnCurso,
                CodigosDeErrorDelInventario.LimiteExcedido,
                CodigosDeErrorDelInventario.NoAutenticado,
                CodigosDeErrorDelInventario.NoEncontrado,
                CodigosDeErrorDelInventario.ReservaEstadoInvalido,
                CodigosDeErrorDelInventario.ServicioNoDisponible,
                CodigosDeErrorDelInventario.StockInsuficiente,
                CodigosDeErrorDelInventario.ValidacionInvalida,
            },
            crear.Errores.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Los_codigos_de_error_son_estables_y_llevan_el_prefijo_del_servicio()
    {
        Assert.Equal("INV-STOCK-INSUFICIENTE", CodigosDeErrorDelInventario.StockInsuficiente);
        Assert.Equal("INV-RESERVA-ESTADO-INVALIDO", CodigosDeErrorDelInventario.ReservaEstadoInvalido);
        Assert.Equal("INV-IDEMPOTENCIA-EN-CONFLICTO", CodigosDeErrorDelInventario.IdempotenciaEnConflicto);
        Assert.Equal("INV-EDICION-NO-IMPRESA", CodigosDeErrorDelInventario.EdicionNoImpresa);
        Assert.Equal(CodigosDeErrorDelInventario.LimiteExcedido, CodigosDeError.De(CodigosDeError.Inventario, CodigosDeError.LimiteExcedido));
        Assert.Equal(CodigosDeErrorDelInventario.NoEncontrado, CodigosDeError.De(CodigosDeError.Inventario, CodigosDeError.NoEncontrado));
        Assert.Equal(CodigosDeErrorDelInventario.NoAutenticado, CodigosDeError.De(CodigosDeError.Inventario, CodigosDeError.NoAutenticado));
        Assert.Equal(CodigosDeErrorDelInventario.Prohibido, CodigosDeError.De(CodigosDeError.Inventario, CodigosDeError.Prohibido));
        Assert.Equal(CodigosDeErrorDelInventario.ServicioNoDisponible, CodigosDeError.De(CodigosDeError.Inventario, CodigosDeError.ServicioNoDisponible));
        Assert.Equal(CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeError.De(CodigosDeError.Inventario, CodigosDeError.ValidacionInvalida));
    }

    [Fact]
    public void Los_valores_cerrados_de_reservas_son_los_de_las_specs()
    {
        Assert.Equal(["recojo", "domicilio"], TiposDeEntrega.Todos);
        Assert.Equal(["pendiente", "confirmada", "liberada", "expirada", "cerrada"], EstadosDeReserva.Todos);
        Assert.Equal(["entrega", "reparto"], MotivosDeCierre.Todos);
        Assert.Equal(
            ["checkout-cancelado", "pedido-anulado", "cancelado-por-el-cliente", "no-recogido", "pago-vencido", "falla-posterior-al-pago"],
            MotivosDeLiberacion.Todos);
        Assert.Equal(["reservas-pendiente-por-cliente", "reservas-por-hora-por-cliente", "reservas-por-hora-por-origen", "reservas-reemplazos-por-pedido"], MotivosDeLimiteDeReservas.Todos);
        Assert.Equal(["impreso", "digital"], FormatosDeEdicion.Todos);
        Assert.Equal(["sin-stock", "bajo", "normal"], EstadosDeStock.Todos);
    }

    [Fact]
    public void Crear_una_reserva_viaja_con_estos_nombres_json()
    {
        var pedido = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var linea = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        var edicion = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
        var solicitud = new CrearReservaDto(pedido, TiposDeEntrega.Recojo, "AYA", null, [new LineaDeSolicitudDto(linea, edicion, 2)]);

        var json = JsonSerializer.Serialize(solicitud, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(
            "{\"pedidoId\":\"aaaaaaaa-0000-0000-0000-000000000001\",\"tipoDeEntrega\":\"recojo\",\"codigoDeSucursalDeRecojo\":\"AYA\","
            + "\"regionDeEntrega\":null,\"lineas\":[{\"lineaId\":\"bbbbbbbb-0000-0000-0000-000000000002\",\"edicionId\":\"cccccccc-0000-0000-0000-000000000003\",\"cantidad\":2}]}",
            json);
    }

    [Fact]
    public void Una_reserva_rechazada_por_stock_lleva_las_lineas_sin_stock_y_el_codigo_estable()
    {
        var problema = new ProblemaDeStockDto
        {
            Status = 409,
            Code = CodigosDeErrorDelInventario.StockInsuficiente,
            Lineas = [new LineaSinStockDto(Guid.Empty, Guid.Empty, 3, 1)],
        };

        var json = JsonSerializer.Serialize(problema, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"code\":\"INV-STOCK-INSUFICIENTE\"", json, StringComparison.Ordinal);
        Assert.Contains("\"lineas\":[{", json, StringComparison.Ordinal);
        Assert.Contains("\"disponibleEnLaRed\":1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_limite_de_reservas_responde_con_el_formato_unico_de_limites_y_uno_de_los_tres_motivos()
    {
        Assert.IsAssignableFrom<ProblemaDto>(new RespuestaDeLimiteDto { Motivo = MotivosDeLimiteDeReservas.PendientePorCliente });
        Assert.Contains(MotivosDeLimiteDeReservas.PorHoraPorOrigen, MotivosDeLimiteDeReservas.Todos);
    }
}
