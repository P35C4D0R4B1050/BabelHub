using System.Text.Json;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Inventario;

namespace BabelHub.ArchitectureTests.Contratos;

/// <summary>
/// Lo que se congela sobre fuentes, rutas, existencias, unidades vendidas, contacto de sucursal y los pendientes y bajas de un titular
/// (FUE-01 a FUE-22, SEL-33 a SEL-35, NOT): cada operación con su ruta, alcance, DTOs y errores.
/// </summary>
public sealed class ContratosDeFuentesYTitularesTests
{
    private const string Usuario = AlcancesDeCredencial.OperacionDeUsuario;
    private const string Lectura = AlcancesDeCredencial.LecturaDeDisponibilidad;

    /// <summary>Nombre, método, ruta, alcance, token de usuario, código de éxito, solicitud, respuesta, es de solo lectura.</summary>
    public static TheoryData<string, string, string, string, bool, int, Type?, Type?, bool> Operaciones() => new()
    {
        { "inventario.sucursales.listar", "GET", "/api/inventario/v1/sucursales", Lectura, false, 200, null, typeof(SucursalesDto), true },
        { "inventario.fuentes.listar", "GET", "/api/inventario/v1/fuentes", Usuario, true, 200, null, typeof(ListaDeFuentesDto), true },
        { "inventario.fuentes.consultar", "GET", "/api/inventario/v1/fuentes/{codigo}", Usuario, true, 200, null, typeof(FuenteDetalleDto), true },
        { "inventario.fuentes.registrar", "POST", "/api/inventario/v1/fuentes", Usuario, true, 201, typeof(RegistrarFuenteDto), typeof(FuenteDetalleDto), false },
        { "inventario.fuentes.editar", "PUT", "/api/inventario/v1/fuentes/{codigo}", Usuario, true, 200, typeof(EditarFuenteDto), typeof(FuenteDetalleDto), false },
        { "inventario.fuentes.designar-recepcion", "PUT", "/api/inventario/v1/fuente-de-recepcion", Usuario, true, 204, typeof(DesignarRecepcionDto), null, false },
        { "inventario.fuentes.existencias", "GET", "/api/inventario/v1/fuentes/{codigo}/existencias", Usuario, true, 200, null, typeof(PaginaDto<ExistenciaDto>), true },
        { "inventario.rutas.listar", "GET", "/api/inventario/v1/rutas", Usuario, true, 200, null, typeof(ListaDeRutasDto), true },
        { "inventario.rutas.consultar", "GET", "/api/inventario/v1/rutas/{origen}/{destino}", Usuario, true, 200, null, typeof(RutaDto), true },
        { "inventario.rutas.definir", "PUT", "/api/inventario/v1/rutas/{origen}/{destino}", Usuario, true, 200, typeof(DefinirRutaDto), typeof(RutaDto), false },
        { "inventario.unidades-vendidas.consultar", "GET", "/api/inventario/v1/unidades-vendidas", Usuario, true, 200, null, typeof(UnidadesVendidasDto), true },
        { "inventario.sucursales.contacto", "GET", "/api/inventario/v1/sucursales/{codigo}/contacto-de-notificacion", AlcancesDeCredencial.LecturaDeContacto, false, 200, null, typeof(ContactoDeSucursalDto), true },
        { "inventario.titulares.consultar-pendientes", "POST", "/api/inventario/v1/titulares/consultas-de-pendientes", AlcancesDeCredencial.ConsultaDePendientes, false, 200, typeof(ConsultaDePendientesDto), typeof(PendientesDeTitularDto), true },
        { "inventario.titulares.baja", "POST", "/api/inventario/v1/bajas-de-titular", AlcancesDeCredencial.BajaDeTitular, false, 204, typeof(BajaDeTitularDto), null, false },
    };

    [Theory]
    [MemberData(nameof(Operaciones))]
    [Trait("Criterio", "AC-SEL-35")]
    public void Cada_operacion_esta_congelada_con_su_ruta_alcance_y_dtos(
        string nombre, string metodo, string ruta, string alcance, bool token, int exito, Type? solicitud, Type? respuesta, bool lectura)
    {
        var operacion = CatalogoDeOperaciones.V1.SingleOrDefault(o => o.Nombre == nombre);

        Assert.NotNull(operacion);
        Assert.Equal(metodo, operacion.Metodo);
        Assert.Equal(ruta, operacion.Ruta);
        Assert.Equal(alcance, operacion.Alcance);
        Assert.Equal(token, operacion.RequiereTokenDeUsuario);
        Assert.False(operacion.RequiereClaveDeIdempotencia);
        Assert.Equal(exito, operacion.CodigoDeExito);
        Assert.Equal(solicitud, operacion.Solicitud);
        Assert.Equal(respuesta, operacion.Respuesta);
        Assert.Equal(lectura, operacion.EsLectura);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-35")]
    public void El_contacto_de_sucursal_y_los_pendientes_son_de_alcance_propio_y_sin_token_de_usuario()
    {
        var contacto = CatalogoDeOperaciones.V1.Single(o => o.Nombre == "inventario.sucursales.contacto");
        var pendientes = CatalogoDeOperaciones.V1.Single(o => o.Nombre == "inventario.titulares.consultar-pendientes");
        var baja = CatalogoDeOperaciones.V1.Single(o => o.Nombre == "inventario.titulares.baja");

        Assert.Equal(["INV-NO-AUTENTICADO", "INV-NO-ENCONTRADO", "INV-SERVICIO-NO-DISPONIBLE"], contacto.Errores.Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(CodigosDeErrorDelInventario.Prohibido, pendientes.Errores);
        Assert.All(new[] { contacto, pendientes, baja }, o => Assert.False(o.RequiereTokenDeUsuario));
    }

    [Fact]
    public void Un_titular_viaja_solo_en_el_cuerpo_y_nunca_en_la_ruta_de_los_pendientes_ni_de_la_baja()
    {
        var rutas = CatalogoDeOperaciones.V1
            .Where(o => o.Nombre is "inventario.titulares.consultar-pendientes" or "inventario.titulares.baja")
            .Select(o => o.Ruta);

        Assert.All(rutas, r => Assert.DoesNotContain('{', r));
        Assert.Equal(["titularId"], NombresJson(typeof(ConsultaDePendientesDto)));
        Assert.Equal(["titularId"], NombresJson(typeof(BajaDeTitularDto)));
        Assert.Equal(["reservasActivas"], NombresJson(typeof(PendientesDeTitularDto)));
    }

    [Fact]
    public void Una_pagina_se_serializa_con_los_elementos_la_pagina_el_tamano_y_el_total()
    {
        var pagina = new PaginaDto<ExistenciaDto>([new ExistenciaDto(Guid.Empty, 9, 1, 8, EstadosDeStock.Normal)], 1, 20, 1);

        var json = JsonSerializer.Serialize(pagina, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(
            "{\"elementos\":[{\"edicionId\":\"00000000-0000-0000-0000-000000000000\",\"fisico\":9,\"reservado\":1,\"vendible\":8,\"estadoDeStock\":\"normal\"}],\"pagina\":1,\"tamano\":20,\"total\":1}",
            json);
    }

    [Fact]
    public void Las_existencias_exponen_fisico_y_reservado_y_la_disponibilidad_publica_solo_el_vendible()
    {
        Assert.Contains("fisico", NombresJson(typeof(ExistenciaDto)));
        Assert.Contains("reservado", NombresJson(typeof(ExistenciaDto)));
        Assert.DoesNotContain("fisico", NombresJson(typeof(FuenteConVendibleDto)));
        Assert.DoesNotContain("reservado", NombresJson(typeof(FuenteConVendibleDto)));
        Assert.DoesNotContain("contactoDeNotificacion", NombresJson(typeof(SucursalPublicaDto)));
    }

    [Fact]
    public void Las_reglas_de_tamano_de_pagina_y_de_ingreso_son_las_de_las_specs()
    {
        Assert.Equal(20, LimitesDeContrato.TamanoDePaginaPorDefecto);
        Assert.Equal(100, LimitesDeContrato.TamanoMaximoDePagina);
        Assert.Equal(1, LimitesDeContrato.IngresoMinimo);
        Assert.Equal(5000, LimitesDeContrato.IngresoMaximo);
        Assert.Equal(30, LimitesDeContrato.DiasDeUnidadesVendidas);
    }

    private static string[] NombresJson(Type dto) =>
        dto.GetProperties()
            .Where(p => p.Name != "EqualityContract")
            .Select(p => ((System.Text.Json.Serialization.JsonPropertyNameAttribute)Attribute.GetCustomAttribute(p, typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute))!).Name)
            .ToArray();
}
