using System.Net;
using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;

namespace BabelHub.IntegrationTests.Devoluciones;

/// <summary>
/// La Presentación de Devoluciones (T23, T24; DEV-13, DEV-29, DEV-31; AC-DEV-01, AC-DEV-02, AC-DEV-05, AC-DEV-12, AC-DEV-23, AC-DEV-30, AC-DEV-31): rutas, <c>303</c>, la lectura de los formularios, el rechazo de adjuntos y de campos que no declara,
/// la traducción de errores a <c>ECO-DEV-…</c> y que el motivo y el texto nunca vuelvan en una respuesta.
/// </summary>
public sealed class DevolucionesApiTests
{
    private static readonly Guid Pedido = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid Linea = Guid.Parse("b1000000-0000-0000-0000-000000000002");
    private static readonly Guid Devolucion = DevolucionesFalsas.DevolucionId;

    private const string MotivoSembrado = "SEMBRADO-motivo-<script>alert(1)</script>";
    private const string TextoSembrado = "SEMBRADO-texto-privado-9876";

    private static ErrorDeAplicacion Error(CategoriaDeError categoria, string clave, params ErrorDeCampo[] campos) => new(categoria, clave, campos);

    [Fact]
    [Trait("Criterio", "AC-DEV-01")]
    public async Task Solicitar_un_impreso_lee_las_cantidades_el_motivo_y_la_sucursal_y_responde_303_al_detalle()
    {
        await using var host = await DevolucionesApiHost.Crear();

        using var respuesta = await host.Enviar(
            $"/cuenta/pedidos/{Pedido:D}/devoluciones",
            [new($"cantidad.{Linea:D}", "2"), new($"cantidad.{Guid.NewGuid():D}", "0"), new("motivo", "Llegó dañado"), new("sucursalReceptora", "lima-central")]);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal($"/cuenta/devoluciones/{Devolucion:D}?hecho=solicitada", respuesta.Headers.Location!.OriginalString);
        Assert.Equal("no-store", respuesta.Headers.CacheControl!.ToString());
        var entrada = host.Devoluciones.UltimoImpreso!;
        Assert.Equal(Pedido, entrada.PedidoId);
        Assert.Equal([new LineaPedidaParaDevolver(Linea, 2)], entrada.Lineas);
        Assert.Equal("lima-central", entrada.CodigoDeSucursalReceptora);
        Assert.Equal(["impreso"], host.Devoluciones.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-02")]
    public async Task Pedir_el_reembolso_de_un_digital_y_reportar_un_archivo_llegan_a_la_aplicacion_con_la_linea_el_motivo_y_el_texto()
    {
        await using var host = await DevolucionesApiHost.Crear();

        using var digital = await host.Enviar($"/cuenta/pedidos/{Pedido:D}/devoluciones/digital", [new("lineaId", Linea.ToString("D")), new("motivo", "No lo quiero")]);
        using var reporte = await host.Enviar($"/cuenta/pedidos/{Pedido:D}/devoluciones/reporte", [new("lineaId", Linea.ToString("D")), new("motivo", "No abre"), new("texto", "Falla en el capítulo 3")]);

        Assert.Equal(HttpStatusCode.SeeOther, digital.StatusCode);
        Assert.Equal(HttpStatusCode.SeeOther, reporte.StatusCode);
        Assert.Equal(Linea, host.Devoluciones.UltimoDigital!.LineaId);
        Assert.Equal(Linea, host.Devoluciones.UltimoReporte!.LineaId);
        Assert.Equal("Falla en el capítulo 3", host.Devoluciones.UltimoReporte.Texto.Valor);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-31")]
    [InlineData("/cuenta/pedidos/a1000000-0000-0000-0000-000000000001/devoluciones")]
    [InlineData("/cuenta/pedidos/a1000000-0000-0000-0000-000000000001/devoluciones/digital")]
    [InlineData("/cuenta/pedidos/a1000000-0000-0000-0000-000000000001/devoluciones/reporte")]
    public async Task Un_adjunto_se_rechaza_como_validacion_sin_llamar_a_la_aplicacion_ni_repetir_el_texto(string ruta)
    {
        await using var host = await DevolucionesApiHost.Crear();
        using var cuerpo = new MultipartFormDataContent
        {
            { new StringContent(MotivoSembrado), "motivo" },
            { new StringContent(TextoSembrado), "texto" },
            { new ByteArrayContent([1, 2, 3]), "adjunto", "foto.png" },
        };

        using var respuesta = await host.Enviar(ruta, contenido: cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("ECO-VALIDACION-INVALIDA", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("SEMBRADO", texto, StringComparison.Ordinal);
        Assert.Empty(host.Devoluciones.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-31")]
    public async Task Un_campo_que_la_accion_no_declara_es_validacion_y_no_se_repite()
    {
        await using var host = await DevolucionesApiHost.Crear();

        using var respuesta = await host.Enviar($"/cuenta/pedidos/{Pedido:D}/devoluciones/digital", [new("lineaId", Linea.ToString("D")), new("motivo", MotivoSembrado), new("cuentaId", Guid.NewGuid().ToString("D"))]);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SEMBRADO", texto, StringComparison.Ordinal);
        Assert.Empty(host.Devoluciones.Llamadas);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-05")]
    [InlineData(CategoriaDeError.ReglaDeNegocio, "tipos-mezclados", 422, "ECO-DEV-TIPOS-MEZCLADOS")]
    [InlineData(CategoriaDeError.ReglaDeNegocio, "linea-no-entregada", 422, "ECO-DEV-LINEA-NO-ENTREGADA")]
    [InlineData(CategoriaDeError.ReglaDeNegocio, "plazo-vencido", 422, "ECO-DEV-PLAZO-VENCIDO")]
    [InlineData(CategoriaDeError.ReglaDeNegocio, "cantidad-excedida", 422, "ECO-DEV-CANTIDAD-EXCEDIDA")]
    [InlineData(CategoriaDeError.ReglaDeNegocio, "linea-no-devolvible", 422, "ECO-DEV-LINEA-NO-DEVOLVIBLE")]
    [InlineData(CategoriaDeError.ReglaDeNegocio, "politica-digital", 422, "ECO-DEV-POLITICA-DIGITAL")]
    [InlineData(CategoriaDeError.ReglaDeNegocio, "reporte-no-admitido", 422, "ECO-DEV-REPORTE-NO-ADMITIDO")]
    [InlineData(CategoriaDeError.Conflicto, "estado-invalido", 409, "ECO-DEV-ESTADO-INVALIDO")]
    [InlineData(CategoriaDeError.ReglaDeNegocio, "reingreso-rechazado", 422, "ECO-DEV-REINGRESO-RECHAZADO")]
    [InlineData(CategoriaDeError.Conflicto, "cuenta-en-cancelacion", 409, "ECO-CUENTA-EN-CANCELACION")]
    [InlineData(CategoriaDeError.Conflicto, "conflicto", 409, "ECO-CONFLICTO")]
    [InlineData(CategoriaDeError.NoDisponible, "servicio-no-disponible", 503, "ECO-SERVICIO-NO-DISPONIBLE")]
    [InlineData(CategoriaDeError.NoEncontrado, "no-encontrado", 404, "ECO-NO-ENCONTRADO")]
    [InlineData(CategoriaDeError.Prohibido, "prohibido", 403, "ECO-PROHIBIDO")]
    [InlineData(CategoriaDeError.NoAutenticado, "no-autenticado", 401, "ECO-NO-AUTENTICADO")]
    public async Task Los_fallos_salen_como_problem_json_con_el_codigo_y_el_estado_del_plan_sin_motivo_ni_texto(CategoriaDeError categoria, string clave, int estado, string codigo)
    {
        await using var host = await DevolucionesApiHost.Crear();
        host.Devoluciones.Respuesta = Resultado<DevolucionVista>.Fallo(Error(categoria, clave));

        using var respuesta = await host.Enviar($"/cuenta/pedidos/{Pedido:D}/devoluciones/reporte", [new("lineaId", Linea.ToString("D")), new("motivo", MotivoSembrado), new("texto", TextoSembrado)]);

        Assert.Equal(estado, (int)respuesta.StatusCode);
        Assert.StartsWith("application/problem+json", respuesta.Content.Headers.ContentType!.ToString(), StringComparison.Ordinal);
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains(codigo, cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("SEMBRADO", cuerpo, StringComparison.Ordinal);
        Assert.Equal("no-store", respuesta.Headers.CacheControl!.ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-05")]
    public async Task El_plazo_vencido_expone_el_plazo_en_dias_y_nada_mas()
    {
        await using var host = await DevolucionesApiHost.Crear();
        host.Devoluciones.Respuesta = Resultado<DevolucionVista>.Fallo(Error(CategoriaDeError.ReglaDeNegocio, "plazo-vencido", new ErrorDeCampo("plazo-en-dias", "15")));

        using var respuesta = await host.Enviar($"/cuenta/pedidos/{Pedido:D}/devoluciones/digital", [new("lineaId", Linea.ToString("D")), new("motivo", MotivoSembrado)]);

        var problema = await DevolucionesApiHost.Leer(respuesta);
        var campo = Assert.Single(problema.GetProperty("errores").EnumerateArray());
        Assert.Equal("plazo-en-dias", campo.GetProperty("campo").GetString());
        Assert.DoesNotContain("SEMBRADO", problema.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-12")]
    [InlineData("/sucursal/lima-central/devoluciones/d1000000-0000-0000-0000-000000000001/aprobar", "aprobar")]
    [InlineData("/sucursal/lima-central/devoluciones/d1000000-0000-0000-0000-000000000001/rechazar", "rechazar")]
    [InlineData("/sucursal/lima-central/devoluciones/d1000000-0000-0000-0000-000000000001/recibir", "recibir")]
    [InlineData("/administracion/devoluciones/d1000000-0000-0000-0000-000000000001/aprobar", "aprobar")]
    [InlineData("/administracion/devoluciones/d1000000-0000-0000-0000-000000000001/rechazar", "rechazar")]
    [InlineData("/administracion/devoluciones/d1000000-0000-0000-0000-000000000001/recibir", "recibir")]
    [InlineData("/administracion/devoluciones/d1000000-0000-0000-0000-000000000001/aprobar-reporte", "aprobar-reporte")]
    [InlineData("/administracion/devoluciones/d1000000-0000-0000-0000-000000000001/rechazar-reporte", "rechazar-reporte")]
    public async Task Las_operaciones_de_la_sucursal_y_de_la_administracion_llaman_una_vez_y_responden_303_al_detalle(string ruta, string llamada)
    {
        await using var host = await DevolucionesApiHost.Crear();

        using var respuesta = await host.Enviar(ruta);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.StartsWith(ruta[..ruta.LastIndexOf('/')], respuesta.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Equal([$"{llamada}:{Devolucion:D}"], host.Devoluciones.Llamadas);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-30")]
    [InlineData("/sucursal/lima-central/devoluciones/d1000000-0000-0000-0000-000000000001/recibir")]
    [InlineData("/administracion/devoluciones/d1000000-0000-0000-0000-000000000001/aprobar-reporte")]
    public async Task Una_operacion_con_un_cuerpo_con_campos_es_validacion_y_no_llama_a_la_aplicacion(string ruta)
    {
        await using var host = await DevolucionesApiHost.Crear();

        using var respuesta = await host.Enviar(ruta, [new("sucursal", "otra")]);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(host.Devoluciones.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-23")]
    public async Task Un_error_del_inventario_en_la_recepcion_sale_como_503_reintentable_y_un_rechazo_como_422()
    {
        await using var host = await DevolucionesApiHost.Crear();
        var ruta = $"/sucursal/lima-central/devoluciones/{Devolucion:D}/recibir";

        host.Devoluciones.Respuesta = Resultado<DevolucionVista>.Fallo(Error(CategoriaDeError.NoDisponible, "servicio-no-disponible"));
        using var caido = await host.Enviar(ruta);
        host.Devoluciones.Respuesta = Resultado<DevolucionVista>.Fallo(Error(CategoriaDeError.ReglaDeNegocio, "reingreso-rechazado"));
        using var rechazado = await host.Enviar(ruta);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, caido.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rechazado.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-30")]
    public async Task Un_codigo_de_sucursal_en_la_ruta_sin_la_forma_de_un_codigo_se_rechaza_antes_de_reflejarlo_ni_llamar_a_la_aplicacion()
    {
        await using var host = await DevolucionesApiHost.Crear();

        using var respuesta = await host.Enviar($"/sucursal/lima_central%22%3E/devoluciones/{Devolucion:D}/recibir");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(host.Devoluciones.Llamadas);
        Assert.DoesNotContain("lima_central", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
