using System.Net;
using System.Text.Json;
using BabelHub.Inventario.Api.Ajustes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.IntegrationTests.Reservas;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Inventario;
using BabelHub.Shared.Errores;

namespace BabelHub.IntegrationTests.Ajustes;

/// <summary>
/// T14, T16 y T17: la superficie HTTP de <c>Ajustes</c> con PostgreSQL real, el rol real de la aplicación y la cadena real de credenciales. Cada operación con el token de su rol: el alcance por sucursal y por rol en cada endpoint
/// (incluidos el historial y la lista de ingresos), el presupuesto de 60 por hora con la respuesta uniforme de límite, el esquema estricto, los valores centinela de la nota y la referencia ausentes de la auditoría y de los errores y
/// la entrada con <c>int.MaxValue</c>; y el contrato con un E-commerce simulado que solo conoce <c>OperacionesDelInventario</c> y los DTOs de ECO-0.
/// </summary>
public sealed class ApiDeAjustesTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private const string Centinela = "CENTINELA-NO-DEBE-SALIR-7731";

    private static readonly JsonSerializerOptions Estricto = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
    };

    private ApiDeReservasHost _api = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _api = await ApiDeReservasHost.Crear(bd);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private static string Clave() => Guid.NewGuid().ToString("N");

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<(Guid Lima, Guid Cusco, Guid Ayacucho, Guid Libro)> Escenario(int fisicoEnAyacucho = 9)
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        var ayacucho = await bd.AgregarFuente("ayacucho");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(ayacucho, libro, fisicoEnAyacucho, 0);
        await bd.PonerStock(cusco, libro, 9, 0);
        return (lima, cusco, ayacucho, libro);
    }

    private Task<HttpResponseMessage> Enviar(HttpMethod metodo, string ruta, string rol, Guid? sucursal = null, Guid? usuario = null, string? json = null, string? clave = null, string? credencial = null) =>
        _api.Enviar(metodo, ruta, credencial ?? ApiDeReservasHost.CredencialDeUsuario, rol, sucursal, usuario ?? Guid.NewGuid(), json, clave);

    private static string Ajuste(string fuente, Guid edicion, int cantidad, string? nota = null, string motivo = "merma", string? extra = null) =>
        $$"""{"codigoDeFuente":"{{fuente}}","edicionId":"{{edicion}}","motivo":"{{motivo}}","cantidad":{{cantidad}},"conteoReal":null,"nota":{{(nota is null ? "null" : $"\"{nota}\"")}}{{extra}}}""";

    private static string IngresoJson(Guid edicion, int cantidad, string? fuente = null, string? referencia = null, string? nota = null) =>
        $$"""{"edicionId":"{{edicion}}","codigoDeFuente":{{(fuente is null ? "null" : $"\"{fuente}\"")}},"cantidad":{{cantidad}},"referencia":{{(referencia is null ? "null" : $"\"{referencia}\"")}},"nota":{{(nota is null ? "null" : $"\"{nota}\"")}}}""";

    // ---------- el camino feliz y el contrato ----------

    [Fact]
    [Trait("Criterio", "AC-AJU-01")]
    public async Task UnaMermaDelEncargadoDeAyacucho_Responde201ConElDtoDeEco0_YBajaElFisico()
    {
        var (_, _, ayacucho, libro) = await Escenario();
        var usuario = Guid.NewGuid();

        var respuesta = await Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.EncargadoDeSucursal, ayacucho, usuario, Ajuste("ayacucho", libro, 2, "caja mojada"), Clave());

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var dto = JsonSerializer.Deserialize<AjusteRegistradoDto>(await respuesta.Content.ReadAsStringAsync(), Estricto)!;
        Assert.Equal(("ayacucho", libro, "merma", usuario, 9, 7, "caja mojada"), (dto.CodigoDeFuente, dto.EdicionId, dto.Motivo, dto.UsuarioId, dto.FisicoAnterior, dto.FisicoPosterior, dto.Nota));
        Assert.Equal((7, 0), await bd.StockDe(ayacucho, libro));
        Assert.Equal(1, _api.Auditoria.Registros.Count(r => r.Accion == "AjusteRegistrado"));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-10")]
    public async Task ElIngresoYSuLista_ConLosDtosDeEco0_SoloParaLaAdministracion()
    {
        var (lima, _, _, libro) = await Escenario();

        var ingreso = await Enviar(HttpMethod.Post, "/ingresos-de-mercaderia", RolesDeUsuario.AdministradorCentral, null, null, IngresoJson(libro, 10, null, "GR-001", "caja 1"), Clave());
        var lista = await Enviar(HttpMethod.Get, "/ingresos-de-mercaderia?limite=10", RolesDeUsuario.AdministradorCentral);
        var deEncargado = await Enviar(HttpMethod.Get, "/ingresos-de-mercaderia", RolesDeUsuario.EncargadoDeSucursal, lima);

        Assert.Equal(HttpStatusCode.Created, ingreso.StatusCode);
        var dto = JsonSerializer.Deserialize<IngresoDeMercaderiaDto>(await ingreso.Content.ReadAsStringAsync(), Estricto)!;
        Assert.Equal((libro, "lima", 10, "GR-001"), (dto.EdicionId, dto.CodigoDeFuente, dto.Cantidad, dto.Referencia));
        var listado = JsonSerializer.Deserialize<ListaDeIngresosDto>(await lista.Content.ReadAsStringAsync(), Estricto)!;
        Assert.Equal([dto.IngresoId], listado.Ingresos.Select(i => i.IngresoId));
        Assert.Equal(HttpStatusCode.Forbidden, deEncargado.StatusCode);
        Assert.Equal((10, 0), await bd.StockDe(lima, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-13")]
    public async Task ElHistorialDeAyacucho_ConElDtoDePaginaDeEco0_NoTrae_LosDeCusco_NiLaNota()
    {
        var (_, cusco, ayacucho, libro) = await Escenario();
        Assert.Equal(HttpStatusCode.Created, (await Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.EncargadoDeSucursal, ayacucho, null, Ajuste("ayacucho", libro, 1, "nota privada"), Clave())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.EncargadoDeSucursal, cusco, null, Ajuste("cusco", libro, 1), Clave())).StatusCode);

        var respuesta = await Enviar(HttpMethod.Get, "/ajustes?codigoDeFuente=ayacucho&pagina=1&tamano=10", RolesDeUsuario.EncargadoDeSucursal, ayacucho);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var pagina = JsonSerializer.Deserialize<PaginaDto<AjusteRegistradoDto>>(await respuesta.Content.ReadAsStringAsync(), Estricto)!;
        Assert.Equal((1, 1), (pagina.Total, pagina.Elementos.Count));
        Assert.Equal(("ayacucho", (string?)null), (pagina.Elementos[0].CodigoDeFuente, pagina.Elementos[0].Nota));
    }

    // ---------- alcance y roles en cada endpoint ----------

    [Fact]
    [Trait("Criterio", "AC-AJU-08")]
    public async Task ElEncargadoDeOtraSucursalLogisticaYClienteSeRechazanConAccesoDenegadoEnCadaEndpoint()
    {
        var (_, cusco, ayacucho, libro) = await Escenario();
        var denegacionesAntes = _api.Denegaciones.Registros.Count;

        var enCusco = await Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.EncargadoDeSucursal, ayacucho, null, Ajuste("cusco", libro, 1), Clave());
        var historialAjeno = await Enviar(HttpMethod.Get, "/ajustes?codigoDeFuente=cusco", RolesDeUsuario.EncargadoDeSucursal, ayacucho);
        var logistica = await Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.PersonalDeLogistica, null, null, Ajuste("cusco", libro, 1), Clave());
        var cliente = await Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.Cliente, null, null, Ajuste("cusco", libro, 1), Clave());
        var ingresoDeEncargado = await Enviar(HttpMethod.Post, "/ingresos-de-mercaderia", RolesDeUsuario.EncargadoDeSucursal, ayacucho, null, IngresoJson(libro, 5), Clave());
        var historialDeLogistica = await Enviar(HttpMethod.Get, "/ajustes?codigoDeFuente=cusco", RolesDeUsuario.PersonalDeLogistica);
        var inexistenteDeEncargado = await Enviar(HttpMethod.Get, "/ajustes?codigoDeFuente=noexiste", RolesDeUsuario.EncargadoDeSucursal, ayacucho);

        Assert.All(new[] { enCusco, historialAjeno, logistica, cliente, ingresoDeEncargado, historialDeLogistica, inexistenteDeEncargado }, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        Assert.Equal(denegacionesAntes + 7, _api.Denegaciones.Registros.Count);
        Assert.Equal((9, 0), await bd.StockDe(cusco, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-08")]
    public async Task UnaCredencialDeOtroAlcanceOSinCredencialEs401_EnCadaEndpoint()
    {
        var (_, _, _, libro) = await Escenario();

        var respuestas = new[]
        {
            await Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.AdministradorCentral, null, null, Ajuste("cusco", libro, 1), Clave(), ApiDeReservasHost.CredencialDeExpiracion),
            await Enviar(HttpMethod.Post, "/ingresos-de-mercaderia", RolesDeUsuario.AdministradorCentral, null, null, IngresoJson(libro, 5), Clave(), ApiDeReservasHost.CredencialDeDisponibilidad),
            await Enviar(HttpMethod.Get, "/ingresos-de-mercaderia", RolesDeUsuario.AdministradorCentral, credencial: ApiDeReservasHost.CredencialDePendientes),
            await Enviar(HttpMethod.Get, "/ajustes?codigoDeFuente=cusco", RolesDeUsuario.AdministradorCentral, credencial: ApiDeReservasHost.CredencialDeBaja),
        };

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM ajustes.ajustes"));
    }

    // ---------- reglas, formas y topes ----------

    [Fact]
    [Trait("Criterio", "AC-AJU-15")]
    public async Task IngresoEnUnaFuenteQueNoEsLaDeRecepcion_Y0Y6000_SeRechazanConSuCodigo_SinCambiarElStock()
    {
        var (lima, _, _, libro) = await Escenario();

        var enCusco = await Enviar(HttpMethod.Post, "/ingresos-de-mercaderia", RolesDeUsuario.AdministradorCentral, null, null, IngresoJson(libro, 5, "cusco"), Clave());
        var cero = await Enviar(HttpMethod.Post, "/ingresos-de-mercaderia", RolesDeUsuario.AdministradorCentral, null, null, IngresoJson(libro, 0, "lima"), Clave());
        var seisMil = await Enviar(HttpMethod.Post, "/ingresos-de-mercaderia", RolesDeUsuario.AdministradorCentral, null, null, IngresoJson(libro, 6000), Clave());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, enCusco.StatusCode);
        Assert.Equal(CodigosDeErrorDelInventario.FuenteNoEsDeRecepcion, (await Cuerpo(enCusco)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, cero.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, seisMil.StatusCode);
        Assert.Null(await bd.StockDe(lima, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-06")]
    public async Task LosRechazosDeNegocio_TraenSusCodigosYSusDatos()
    {
        var (_, _, ayacucho, libro) = await Escenario();
        var encargado = (Guid?)ayacucho;
        var pedido = PilaDeReservas.ARecojo("ayacucho", [PilaDeReservas.Linea(libro, 3)]);
        Assert.Equal(HttpStatusCode.Created, (await _api.Enviar(HttpMethod.Post, "/reservas", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: Guid.NewGuid(),
            json: $$"""{"pedidoId":"{{pedido.PedidoId}}","tipoDeEntrega":"recojo","codigoDeSucursalDeRecojo":"ayacucho","regionDeEntrega":null,"lineas":[{"lineaId":"{{pedido.Lineas[0].LineaId}}","edicionId":"{{libro}}","cantidad":3}]}""", clave: Clave())).StatusCode);

        var supera = await Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.EncargadoDeSucursal, encargado, null, Ajuste("ayacucho", libro, 7), Clave());
        var conteo = await Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.EncargadoDeSucursal, encargado, null, $$"""{"codigoDeFuente":"ayacucho","edicionId":"{{libro}}","motivo":"correccion-de-conteo","cantidad":null,"conteoReal":2,"nota":null}""", Clave());
        var igual = await Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.EncargadoDeSucursal, encargado, null, $$"""{"codigoDeFuente":"ayacucho","edicionId":"{{libro}}","motivo":"correccion-de-conteo","cantidad":null,"conteoReal":9,"nota":null}""", Clave());

        Assert.Equal(HttpStatusCode.Conflict, supera.StatusCode);
        var detalle = await Cuerpo(supera);
        Assert.Equal(CodigosDeErrorDelInventario.StockInsuficiente, detalle.GetProperty("code").GetString());
        Assert.Equal(["fisico:9", "reservado:3", "vendible:6"], detalle.GetProperty("errores").EnumerateArray().Select(e => $"{e.GetProperty("campo").GetString()}:{e.GetProperty("clave").GetString()}"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, conteo.StatusCode);
        var problema = JsonSerializer.Deserialize<ProblemaDeConteoDto>(await conteo.Content.ReadAsStringAsync(), Estricto)!;
        Assert.Equal(CodigosDeErrorDelInventario.ConteoMenorQueReservas, problema.Code);
        Assert.Equal([pedido.PedidoId], problema.PedidosQueReservan);
        Assert.Equal(CodigosDeErrorDelInventario.SinCambios, (await Cuerpo(igual)).GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-02")]
    public async Task MotivoInvalido_DevolucionEIngresoEnElEndpointManual_CampoDeMas_SinClave_YCantidadMaxima_SeRechazan()
    {
        var (_, _, ayacucho, libro) = await Escenario();
        Task<HttpResponseMessage> Ajustar(string json, string? clave = null) => Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.EncargadoDeSucursal, ayacucho, null, json, clave ?? Clave());

        var regalo = await Ajustar(Ajuste("ayacucho", libro, 1, null, "regalo"));
        var devolucion = await Ajustar(Ajuste("ayacucho", libro, 1, null, "devolucion"));
        var ingreso = await Ajustar(Ajuste("ayacucho", libro, 1, null, "ingreso-de-mercaderia"));
        var campoDeMas = await Ajustar(Ajuste("ayacucho", libro, 1, null, "merma", $$""","usuarioId":"{{Guid.NewGuid()}}" """));
        var sinClave = await Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.EncargadoDeSucursal, ayacucho, null, Ajuste("ayacucho", libro, 1), null);
        var maxima = await Ajustar(Ajuste("ayacucho", libro, int.MaxValue));

        Assert.All(new[] { regalo, devolucion, ingreso, campoDeMas, sinClave }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal(HttpStatusCode.Conflict, maxima.StatusCode);
        Assert.Equal((9, 0), await bd.StockDe(ayacucho, libro));
    }

    // ---------- presupuesto ----------

    [Fact]
    [Trait("Criterio", "AC-AJU-08")]
    public async Task ElAjuste61SeRechazaConLaRespuestaDeLimite_SinAfectarAOtroUsuario()
    {
        var (_, _, ayacucho, libro) = await Escenario(500);
        var usuario = Guid.NewGuid();

        for (var i = 0; i < 60; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.EncargadoDeSucursal, ayacucho, usuario, Ajuste("ayacucho", libro, 1), Clave())).StatusCode);
        }

        var rechazado = await Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.EncargadoDeSucursal, ayacucho, usuario, Ajuste("ayacucho", libro, 1), Clave());
        var otroUsuario = await Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.EncargadoDeSucursal, ayacucho, Guid.NewGuid(), Ajuste("ayacucho", libro, 1), Clave());

        Assert.Equal(HttpStatusCode.TooManyRequests, rechazado.StatusCode);
        var limite = JsonSerializer.Deserialize<RespuestaDeLimiteDto>(await rechazado.Content.ReadAsStringAsync(), Estricto)!;
        Assert.Equal((CodigosDeErrorDelInventario.LimiteExcedido, "ajustes-del-personal"), (limite.Code, limite.Motivo));
        Assert.NotEmpty(rechazado.Headers.GetValues("Retry-After").Single());
        Assert.Equal(HttpStatusCode.Created, otroUsuario.StatusCode);
        Assert.Equal((500 - 61, 0), await bd.StockDe(ayacucho, libro));
    }

    // ---------- datos personales ----------

    [Fact]
    [Trait("Criterio", "AC-AJU-01")]
    public async Task LaNotaYLaReferenciaCentinelaNoAparecenEnLaAuditoriaNiEnLasDenegacionesNiEnLosErrores()
    {
        var (_, _, ayacucho, libro) = await Escenario();
        var respuestas = new List<HttpResponseMessage>
        {
            await Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.EncargadoDeSucursal, ayacucho, null, Ajuste("ayacucho", libro, 1, Centinela), Clave()),
            await Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.EncargadoDeSucursal, ayacucho, null, Ajuste("ayacucho", libro, 99, Centinela), Clave()),
            await Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.EncargadoDeSucursal, ayacucho, null, Ajuste("cusco", libro, 1, Centinela), Clave()),
            await Enviar(HttpMethod.Post, "/ingresos-de-mercaderia", RolesDeUsuario.EncargadoDeSucursal, ayacucho, null, IngresoJson(libro, 5, null, "REF-" + Centinela, Centinela), Clave()),
            await Enviar(HttpMethod.Post, "/ingresos-de-mercaderia", RolesDeUsuario.AdministradorCentral, null, null, IngresoJson(libro, 6000, null, "GR-1", Centinela), Clave()),
            await Enviar(HttpMethod.Post, "/ingresos-de-mercaderia", RolesDeUsuario.AdministradorCentral, null, null, IngresoJson(libro, 5, null, "guía; " + Centinela, Centinela), Clave()),
            await Enviar(HttpMethod.Post, "/ajustes", RolesDeUsuario.EncargadoDeSucursal, ayacucho, null, Ajuste("ayacucho", libro, 1, new string('n', 300) + Centinela), Clave()),
        };

        // La auditoría, las denegaciones y los cuerpos de error (sin el de la mermaexitosa, que sí devuelve su propia nota al dueño) no llevan los centinelas.
        var auditoria = string.Join(" ", _api.Auditoria.Registros.Concat(_api.Denegaciones.Registros).Select(r => $"{r.Accion}|{r.ObjetoId}|{r.Motivo}|{r.Ruta}|{r.Detalle.Texto}"));
        Assert.DoesNotContain("CENTINELA", auditoria, StringComparison.Ordinal);
        foreach (var errorada in respuestas.Skip(1))
        {
            Assert.DoesNotContain("CENTINELA", await errorada.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    // ---------- la matriz ----------

    [Fact]
    [Trait("Criterio", "AC-AJU-08")]
    public void CadaOperacionDeAjustesEstaDeclaradaEnLaMatrizConUnSoloAlcance()
    {
        var endpoints = _api.Endpoints.Where(e => (e.RoutePattern.RawText ?? string.Empty).Contains("/ajustes", StringComparison.Ordinal) || (e.RoutePattern.RawText ?? string.Empty).Contains("/ingresos-de-mercaderia", StringComparison.Ordinal)).ToList();

        Assert.Equal(MatrizDeAjustes.Operaciones.Count, endpoints.Count);
        Assert.All(endpoints, e => Assert.Contains(e.Metadata.GetMetadata<BabelHub.Inventario.Api.Plataforma.OperacionDeServicioAttribute>()!.Operacion.Valor, MatrizDeAjustes.Operaciones));
        Assert.All(MatrizDeAjustes.Declaraciones(), d => Assert.Equal(AlcanceDeServicio.OperacionDeUsuario, d.Alcance));
    }
}
