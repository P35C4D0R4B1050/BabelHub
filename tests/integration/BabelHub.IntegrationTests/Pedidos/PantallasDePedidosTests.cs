using System.Net;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.IntegrationTests.Web;
using BabelHub.Shared.Errores;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>
/// La Presentación de Pedidos con las vistas reales y la sesión web real (T31, T37 a T40; AC-PED-03, AC-PED-06, AC-PED-07, AC-PED-08, AC-PED-09, AC-PED-11, AC-PED-12, AC-PED-15, AC-PED-16, AC-PED-23, AC-PED-25, AC-PED-26):
/// rutas, <c>303</c>, el rechazo de entradas que no declara, la traducción de errores a <c>ECO-…</c>, el estado con texto e icono y <b>qué dato ve cada rol</b> (el contacto solo el titular y el personal; nunca la lista ni el
/// encargado de sucursal). Los contratos de la Aplicación son dobles que anotan cada llamada: la Aplicación y PostgreSQL se prueban aparte.
/// </summary>
public sealed class PantallasDePedidosTests
{
    private static readonly DateTimeOffset Creado = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

    private static readonly Guid Pedido = Guid.Parse("a1000000-0000-0000-0000-000000000001");

    private static readonly Guid LineaA = Guid.Parse("b1000000-0000-0000-0000-00000000000a");

    private static readonly Guid LineaB = Guid.Parse("b1000000-0000-0000-0000-00000000000b");

    private const string NombreDelContacto = "Rosa Quispe Huamán";

    private const string TelefonoDelContacto = "987654321";

    private const string DireccionDelContacto = "Jr. Los Pinos 123, Ayacucho";

    private static ResumenDePedido Resumen(EstadoVisibleDelPedido estado = EstadoVisibleDelPedido.Confirmado, TipoDeEntregaVisible tipo = TipoDeEntregaVisible.Recojo) =>
        new(Pedido, "BH-2026-001", Creado, estado, tipo, tipo == TipoDeEntregaVisible.Recojo ? "LIM-01" : null, 85.50m, 2);

    private static LineaVisibleDelPedido Linea(Guid id, string titulo, EstadoVisibleDeLaLinea estado = EstadoVisibleDeLaLinea.Confirmado, TrasladoVisible? traslado = null, bool digital = false) =>
        new(id, titulo, "9786123456789", digital, 1, 42.75m, 42.75m, estado, digital ? null : "LIM-01", traslado, null, null, null, Operable: !digital && estado is not (EstadoVisibleDeLaLinea.Entregado or EstadoVisibleDeLaLinea.Anulado or EstadoVisibleDeLaLinea.NoRecogido));

    private static ContactoDeEntrega Contacto() => new(NombreDelContacto, TelefonoDelContacto, DireccionDelContacto, "Ayacucho");

    private static ErrorDeAplicacion Error(CategoriaDeError categoria, string clave, params ErrorDeCampo[] campos) => new(categoria, clave, campos);

    // ---------- Dobles de los contratos ----------

    private sealed class PedidosDelClienteFalso : IPedidosDelCliente
    {
        public List<string> Llamadas { get; } = [];

        public FiltroDePedidos? UltimoFiltro { get; private set; }

        public Resultado<PaginaDePedidos> Lista { get; set; } = Resultado<PaginaDePedidos>.Exito(new PaginaDePedidos([], 1, 20, 0));

        public Resultado<DetalleDelPedidoDelCliente> Detalle { get; set; } = Resultado<DetalleDelPedidoDelCliente>.Exito(
            new DetalleDelPedidoDelCliente(Resumen(), 0m, null, [Linea(LineaA, "Cien años de soledad")], null, true, AlternativaDeCancelacion.Ninguna, null));

        public Resultado<ResultadoDeCancelacionDePedido> Cancelacion { get; set; } = Resultado<ResultadoDeCancelacionDePedido>.Exito(new ResultadoDeCancelacionDePedido(Pedido, EstadoVisibleDelPedido.Anulado, 85.50m));

        public Task<Resultado<PaginaDePedidos>> Listar(FiltroDePedidos filtro, CancellationToken cancelacion = default)
        {
            Llamadas.Add("listar");
            UltimoFiltro = filtro;
            return Task.FromResult(Lista);
        }

        public Task<Resultado<DetalleDelPedidoDelCliente>> Obtener(Guid pedidoId, CancellationToken cancelacion = default)
        {
            Llamadas.Add($"obtener:{pedidoId:D}");
            return Task.FromResult(Detalle);
        }

        public Task<Resultado<ResultadoDeCancelacionDePedido>> Cancelar(Guid pedidoId, CancellationToken cancelacion = default)
        {
            Llamadas.Add($"cancelar:{pedidoId:D}");
            return Task.FromResult(Cancelacion);
        }
    }

    private sealed class GestionDeRecojoFalsa : IGestionDeRecojo
    {
        public List<string> Llamadas { get; } = [];

        public Resultado<PaginaDePedidos> Lista { get; set; } = Resultado<PaginaDePedidos>.Exito(new PaginaDePedidos([], 1, 20, 0));

        public Resultado<DetalleDelPedidoDeSucursal> Detalle { get; set; } = Resultado<DetalleDelPedidoDeSucursal>.Exito(
            new DetalleDelPedidoDeSucursal(Resumen(), [Linea(LineaA, "Cien años de soledad"), Linea(LineaB, "La ciudad y los perros", EstadoVisibleDeLaLinea.EnTransito, new TrasladoVisible(new DateOnly(2026, 11, 25), true))]));

        public Resultado<LineasOperadas> Operacion { get; set; } = Resultado<LineasOperadas>.Exito(
            new LineasOperadas(Pedido, EstadoVisibleDelPedido.EnPreparacion, [new ResultadoDeLineaOperada(LineaA, ResultadoVisibleDeLinea.Aplicada), new ResultadoDeLineaOperada(LineaB, ResultadoVisibleDeLinea.EsperaElTraslado)]));

        public Task<Resultado<PaginaDePedidos>> Listar(string codigoDeSucursal, FiltroDePedidos filtro, CancellationToken cancelacion = default)
        {
            Llamadas.Add($"listar:{codigoDeSucursal}");
            return Task.FromResult(Lista);
        }

        public Task<Resultado<DetalleDelPedidoDeSucursal>> Obtener(string codigoDeSucursal, Guid pedidoId, CancellationToken cancelacion = default)
        {
            Llamadas.Add($"obtener:{codigoDeSucursal}:{pedidoId:D}");
            return Task.FromResult(Detalle);
        }

        public Task<Resultado<LineasOperadas>> Preparar(string codigoDeSucursal, OperarLineasEntrada entrada, CancellationToken cancelacion = default) => Operar("preparar", codigoDeSucursal, entrada);

        public Task<Resultado<LineasOperadas>> MarcarListas(string codigoDeSucursal, OperarLineasEntrada entrada, CancellationToken cancelacion = default) => Operar("listas", codigoDeSucursal, entrada);

        public Task<Resultado<LineasOperadas>> Entregar(string codigoDeSucursal, OperarLineasEntrada entrada, CancellationToken cancelacion = default) => Operar("entregar", codigoDeSucursal, entrada);

        private Task<Resultado<LineasOperadas>> Operar(string accion, string codigo, OperarLineasEntrada entrada)
        {
            Llamadas.Add($"{accion}:{codigo}:{entrada.PedidoId:D}:{string.Join(',', entrada.LineaIds.Select(l => l.ToString("D")))}");
            return Task.FromResult(Operacion);
        }
    }

    private sealed class GestionDeDespachoFalsa : IGestionDeDespacho
    {
        public List<string> Llamadas { get; } = [];

        public Resultado<DetalleDelPedidoDePersonal> Detalle { get; set; } = Resultado<DetalleDelPedidoDePersonal>.Exito(
            new DetalleDelPedidoDePersonal(Resumen(EstadoVisibleDelPedido.Confirmado, TipoDeEntregaVisible.Domicilio), 12m, "Ayacucho", [Linea(LineaA, "Cien años de soledad")], Contacto(), false, null));

        public Resultado<LineasOperadas> Operacion { get; set; } = Resultado<LineasOperadas>.Exito(
            new LineasOperadas(Pedido, EstadoVisibleDelPedido.EnPreparacion, [new ResultadoDeLineaOperada(LineaA, ResultadoVisibleDeLinea.Aplicada)]));

        public Task<Resultado<PaginaDePedidos>> Listar(FiltroDePedidos filtro, CancellationToken cancelacion = default)
        {
            Llamadas.Add("listar");
            return Task.FromResult(Resultado<PaginaDePedidos>.Exito(new PaginaDePedidos([Resumen(EstadoVisibleDelPedido.Confirmado, TipoDeEntregaVisible.Domicilio)], 1, 20, 1)));
        }

        public Task<Resultado<DetalleDelPedidoDePersonal>> Obtener(Guid pedidoId, CancellationToken cancelacion = default)
        {
            Llamadas.Add($"obtener:{pedidoId:D}");
            return Task.FromResult(Detalle);
        }

        public Task<Resultado<LineasOperadas>> Preparar(OperarLineasEntrada entrada, CancellationToken cancelacion = default)
        {
            Llamadas.Add($"preparar:{entrada.PedidoId:D}:{entrada.LineaIds.Count}");
            return Task.FromResult(Operacion);
        }

        public Task<Resultado<LineasOperadas>> MarcarListas(OperarLineasEntrada entrada, CancellationToken cancelacion = default)
        {
            Llamadas.Add($"listas:{entrada.PedidoId:D}:{entrada.LineaIds.Count}");
            return Task.FromResult(Operacion);
        }
    }

    private sealed class AdministracionFalsa : IAdministracionDePedidos
    {
        public List<string> Llamadas { get; } = [];

        public Resultado<DetalleDelPedidoDePersonal> Detalle { get; set; } = Resultado<DetalleDelPedidoDePersonal>.Exito(
            new DetalleDelPedidoDePersonal(Resumen(EstadoVisibleDelPedido.Confirmado, TipoDeEntregaVisible.Domicilio), 12m, "Ayacucho", [Linea(LineaA, "Cien años de soledad")], Contacto(), false, new ReembolsoPendienteDelPedido(85.50m), PuedeAnular: true));

        public Resultado<ResultadoDeAnulacion> Anulacion { get; set; } = Resultado<ResultadoDeAnulacion>.Exito(new ResultadoDeAnulacion(Pedido, EstadoVisibleDelPedido.Anulado, 85.50m, false));

        public Resultado<PaginaDeOperacionesFallidas> Fallidas { get; set; } = Resultado<PaginaDeOperacionesFallidas>.Exito(new PaginaDeOperacionesFallidas([], 1, 20, 0));

        public Task<Resultado<PaginaDePedidos>> Listar(FiltroDePedidos filtro, CancellationToken cancelacion = default)
        {
            Llamadas.Add("listar");
            return Task.FromResult(Resultado<PaginaDePedidos>.Exito(new PaginaDePedidos([Resumen()], 1, 20, 1)));
        }

        public Task<Resultado<DetalleDelPedidoDePersonal>> Obtener(Guid pedidoId, CancellationToken cancelacion = default)
        {
            Llamadas.Add($"obtener:{pedidoId:D}");
            return Task.FromResult(Detalle);
        }

        public Task<Resultado<ResultadoDeAnulacion>> Anular(AnularPedidoEntrada entrada, CancellationToken cancelacion = default)
        {
            Llamadas.Add($"anular:{entrada.PedidoId:D}:{entrada.Motivo}");
            return Task.FromResult(Anulacion);
        }

        public Task<Resultado<ResultadoDeAnulacion>> ReintentarElReembolso(Guid pedidoId, CancellationToken cancelacion = default)
        {
            Llamadas.Add($"reintentar:{pedidoId:D}");
            return Task.FromResult(Anulacion);
        }

        public Task<Resultado<PaginaDeOperacionesFallidas>> ListarOperacionesFallidas(int pagina, int tamano, CancellationToken cancelacion = default)
        {
            Llamadas.Add($"fallidas:{pagina}");
            return Task.FromResult(Fallidas);
        }
    }

    private sealed class ListaVacia : IListaDeSucursales
    {
        public Task<ListaDeSucursales?> Obtener(CancellationToken cancelacion = default) => Task.FromResult<ListaDeSucursales?>(new ListaDeSucursales([], true));
    }

    // ---------- Anfitriones y llamadas ----------

    private static async Task<AnfitrionWeb> Iniciar(RolWeb rol, HostWeb host, Action<IServiceCollection> servicios) =>
        await AnfitrionConSesion.Iniciar(
            new SesionesWebDePrueba().ConSesionValida(rol, rol == RolWeb.Cliente ? null : Guid.NewGuid()),
            host,
            servicios: s =>
            {
                s.AddSingleton<IListaDeSucursales>(new ListaVacia());
                servicios(s);
            });

    private static Task<AnfitrionWeb> ComoTitular(PedidosDelClienteFalso falso) => Iniciar(RolWeb.Cliente, HostWeb.Tienda, s => s.AddSingleton<IPedidosDelCliente>(falso));

    private static Task<AnfitrionWeb> ComoEncargado(GestionDeRecojoFalsa falso) => Iniciar(RolWeb.Encargado, HostWeb.Gestion, s => s.AddSingleton<IGestionDeRecojo>(falso));

    private static Task<AnfitrionWeb> ComoLogistica(GestionDeDespachoFalsa falso) => Iniciar(RolWeb.Logistica, HostWeb.Gestion, s => s.AddSingleton<IGestionDeDespacho>(falso));

    private static Task<AnfitrionWeb> ComoAdministracion(AdministracionFalsa falso) => Iniciar(RolWeb.AdministradorCentral, HostWeb.Gestion, s => s.AddSingleton<IAdministracionDePedidos>(falso));

    private static async Task<(HttpStatusCode Estado, string Html)> Pedir(AnfitrionWeb anfitrion, string ruta, HostWeb host, bool html = true)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        if (html)
        {
            solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        }

        solicitud.Headers.TryAddWithoutValidation("Cookie", AnfitrionConSesion.Cookie(host));
        using var respuesta = await anfitrion.Cliente.SendAsync(solicitud);
        return (respuesta.StatusCode, WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()));
    }

    private static async Task<HttpResponseMessage> Enviar(AnfitrionWeb anfitrion, string ruta, HostWeb host, IEnumerable<KeyValuePair<string, string>>? campos = null, bool html = false)
    {
        using var solicitud = await AnfitrionConSesion.Post(anfitrion, ruta, AnfitrionConSesion.Cookie(host));
        if (html)
        {
            solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        }

        solicitud.Content = new FormUrlEncodedContent(campos ?? []);
        return await anfitrion.Cliente.SendAsync(solicitud);
    }

    private static KeyValuePair<string, string> Id(Guid linea) => new("lineaIds", linea.ToString("D"));

    // ---------- El titular ----------

    [Fact]
    [Trait("Criterio", "AC-PED-03")]
    [Trait("Criterio", "AC-PED-26")]
    public async Task La_lista_del_titular_pinta_el_estado_con_texto_e_icono_y_nunca_el_contacto()
    {
        var falso = new PedidosDelClienteFalso
        {
            Lista = Resultado<PaginaDePedidos>.Exito(new PaginaDePedidos(
                [Resumen(EstadoVisibleDelPedido.EnTransito), Resumen(EstadoVisibleDelPedido.Anulado, TipoDeEntregaVisible.Domicilio)], 1, 20, 2)),
        };
        await using var anfitrion = await ComoTitular(falso);

        var (estado, html) = await Pedir(anfitrion, "/cuenta/pedidos?estado=en-transito&pagina=1", HostWeb.Tienda);

        Assert.Equal(HttpStatusCode.OK, estado);
        foreach (var texto in new[] { "Mis pedidos", "BH-2026-001", "En tránsito", "Anulado", "Recojo en sucursal", "Entrega a domicilio", FormatoPeruano.Soles(85.50m), "2 pedidos" })
        {
            Assert.Contains(texto, html, StringComparison.Ordinal);
        }

        Assert.Contains($"href=\"/cuenta/pedidos/{Pedido:D}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(NombreDelContacto, html, StringComparison.Ordinal);
        Assert.Equal(EstadoVisibleDelPedido.EnTransito, falso.UltimoFiltro!.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-03")]
    public async Task Sin_pedidos_se_muestra_el_estado_vacio_y_con_un_filtro_se_ofrece_ver_todos()
    {
        await using var anfitrion = await ComoTitular(new PedidosDelClienteFalso());

        var (_, vacio) = await Pedir(anfitrion, "/cuenta/pedidos", HostWeb.Tienda);
        var (_, filtrado) = await Pedir(anfitrion, "/cuenta/pedidos?estado=entregado", HostWeb.Tienda);

        Assert.Contains("Aún no hay pedidos", vacio, StringComparison.Ordinal);
        Assert.Contains("No hay pedidos en este estado", filtrado, StringComparison.Ordinal);
        Assert.Contains("Ver todos", filtrado, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-03")]
    [Trait("Criterio", "AC-PED-26")]
    public async Task El_detalle_del_titular_muestra_el_traslado_con_su_fecha_el_contacto_y_el_reembolso_pendiente()
    {
        var falso = new PedidosDelClienteFalso
        {
            Detalle = Resultado<DetalleDelPedidoDelCliente>.Exito(new DetalleDelPedidoDelCliente(
                Resumen(EstadoVisibleDelPedido.EnTransito, TipoDeEntregaVisible.Domicilio),
                12m,
                "Ayacucho",
                [Linea(LineaA, "Cien años de soledad", EstadoVisibleDeLaLinea.EnTransito, new TrasladoVisible(new DateOnly(2026, 11, 25), true)), Linea(LineaB, "El libro digital", EstadoVisibleDeLaLinea.DisponibleEnLaBiblioteca, digital: true)],
                Contacto(),
                false,
                AlternativaDeCancelacion.Devolucion,
                new ReembolsoPendienteDelPedido(30m))),
        };
        await using var anfitrion = await ComoTitular(falso);

        var (estado, html) = await Pedir(anfitrion, $"/cuenta/pedidos/{Pedido:D}?hecho=cancelar", HostWeb.Tienda);

        Assert.Equal(HttpStatusCode.OK, estado);
        foreach (var texto in new[] { "Traslado en camino, llega el 25/11/2026", "Disponible en tu biblioteca", NombreDelContacto, DireccionDelContacto, $"{FormatoPeruano.Soles(30m)} pendiente", "puedes pedir una devolución" })
        {
            Assert.Contains(texto, html, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("/cancelar\"", html, StringComparison.Ordinal);
        // El titular no ve la sucursal de origen de cada línea.
        Assert.DoesNotContain("Sucursal de origen", html, StringComparison.Ordinal);
        Assert.Contains($"/cuenta/pedidos/{Pedido:D}/envios", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-15")]
    public async Task El_detalle_ofrece_cancelar_solo_donde_procede_y_con_su_token()
    {
        await using var anfitrion = await ComoTitular(new PedidosDelClienteFalso());

        var (_, html) = await Pedir(anfitrion, $"/cuenta/pedidos/{Pedido:D}", HostWeb.Tienda);

        Assert.Contains($"action=\"/cuenta/pedidos/{Pedido:D}/cancelar\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(NombreDelContacto, html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-15")]
    public async Task Un_enlace_con_hecho_cancelar_no_afirma_una_cancelacion_que_no_ocurrio()
    {
        await using var anfitrion = await ComoTitular(new PedidosDelClienteFalso());

        var (_, html) = await Pedir(anfitrion, $"/cuenta/pedidos/{Pedido:D}?hecho=cancelar", HostWeb.Tienda);

        Assert.DoesNotContain("El pedido se canceló", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-15")]
    public async Task Cancelar_responde_303_al_detalle_y_llama_una_sola_vez()
    {
        var falso = new PedidosDelClienteFalso();
        await using var anfitrion = await ComoTitular(falso);

        using var respuesta = await Enviar(anfitrion, $"/cuenta/pedidos/{Pedido:D}/cancelar", HostWeb.Tienda);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal($"/cuenta/pedidos/{Pedido:D}?hecho=cancelar", respuesta.Headers.Location!.OriginalString);
        Assert.Equal([$"cancelar:{Pedido:D}"], falso.Llamadas);
    }

    [Theory]
    [Trait("Criterio", "AC-PED-16")]
    [InlineData("devolucion")]
    [InlineData("politica-de-reembolso-digital")]
    public async Task Un_pedido_no_cancelable_responde_409_con_la_alternativa_y_sin_datos_del_pedido(string alternativa)
    {
        var falso = new PedidosDelClienteFalso { Cancelacion = Resultado<ResultadoDeCancelacionDePedido>.Fallo(Error(CategoriaDeError.Conflicto, "pedido-no-cancelable", new ErrorDeCampo("alternativa", alternativa))) };
        await using var anfitrion = await ComoTitular(falso);

        using var respuesta = await Enviar(anfitrion, $"/cuenta/pedidos/{Pedido:D}/cancelar", HostWeb.Tienda);
        var json = JsonDePrueba.Leer(await respuesta.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("ECO-PEDIDO-NO-CANCELABLE", json.GetProperty("code").GetString());
        Assert.Equal(alternativa, json.GetProperty("errores")[0].GetProperty("clave").GetString());
        Assert.Equal("no-store", respuesta.Headers.CacheControl!.ToString());
    }

    [Theory]
    [Trait("Criterio", "AC-PED-03")]
    [InlineData(CategoriaDeError.NoEncontrado, "no-encontrado", 404, "ECO-NO-ENCONTRADO")]
    [InlineData(CategoriaDeError.Conflicto, "pedido-estado-invalido", 409, "ECO-PEDIDO-ESTADO-INVALIDO")]
    [InlineData(CategoriaDeError.Conflicto, "pedido-operacion-en-curso", 409, "ECO-PEDIDO-OPERACION-EN-CURSO")]
    [InlineData(CategoriaDeError.Conflicto, "conflicto", 409, "ECO-CONFLICTO")]
    [InlineData(CategoriaDeError.NoDisponible, "servicio-no-disponible", 503, "ECO-SERVICIO-NO-DISPONIBLE")]
    public async Task Los_fallos_de_cancelar_salen_como_problem_json_con_el_codigo_y_el_estado_del_plan(CategoriaDeError categoria, string clave, int estado, string codigo)
    {
        var falso = new PedidosDelClienteFalso { Cancelacion = Resultado<ResultadoDeCancelacionDePedido>.Fallo(Error(categoria, clave)) };
        await using var anfitrion = await ComoTitular(falso);

        using var respuesta = await Enviar(anfitrion, $"/cuenta/pedidos/{Pedido:D}/cancelar", HostWeb.Tienda);
        var json = JsonDePrueba.Leer(await respuesta.Content.ReadAsStringAsync());

        Assert.Equal(estado, (int)respuesta.StatusCode);
        Assert.Equal(codigo, json.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Criterio", "AC-PED-03")]
    public async Task Un_pedido_ajeno_o_inexistente_se_pinta_como_no_encontrado_sin_pistas()
    {
        var falso = new PedidosDelClienteFalso { Detalle = Resultado<DetalleDelPedidoDelCliente>.Fallo(Error(CategoriaDeError.NoEncontrado, "no-encontrado")) };
        await using var anfitrion = await ComoTitular(falso);

        var (estado, html) = await Pedir(anfitrion, $"/cuenta/pedidos/{Pedido:D}", HostWeb.Tienda);

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Contains("No encontramos ese pedido", html, StringComparison.Ordinal);
        Assert.Contains("ECO-NO-ENCONTRADO", html, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PED-03")]
    [InlineData("/cuenta/pedidos?estado=inventado")]
    [InlineData("/cuenta/pedidos?pagina=0")]
    [InlineData("/cuenta/pedidos?otra=1")]
    [InlineData("/cuenta/pedidos?estado=anulado&estado=entregado")]
    public async Task Una_consulta_con_valores_o_claves_que_no_declara_es_validacion_y_no_llega_a_la_Aplicacion(string ruta)
    {
        var falso = new PedidosDelClienteFalso();
        await using var anfitrion = await ComoTitular(falso);

        var (estado, cuerpo) = await Pedir(anfitrion, ruta, HostWeb.Tienda, html: false);

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Contains("ECO-VALIDACION-INVALIDA", cuerpo, StringComparison.Ordinal);
        Assert.Empty(falso.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-15")]
    public async Task Un_POST_de_cancelar_con_campos_que_no_declara_es_validacion()
    {
        var falso = new PedidosDelClienteFalso();
        await using var anfitrion = await ComoTitular(falso);

        using var respuesta = await Enviar(anfitrion, $"/cuenta/pedidos/{Pedido:D}/cancelar", HostWeb.Tienda, [new("cuentaId", Guid.NewGuid().ToString("D"))]);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(falso.Llamadas);
    }

    // ---------- El encargado de la sucursal ----------

    [Fact]
    [Trait("Criterio", "AC-PED-07")]
    public async Task La_lista_de_la_sucursal_lleva_el_codigo_de_la_ruta_a_la_Aplicacion_y_no_muestra_contacto()
    {
        var falso = new GestionDeRecojoFalsa { Lista = Resultado<PaginaDePedidos>.Exito(new PaginaDePedidos([Resumen(EstadoVisibleDelPedido.ListoParaRecojo)], 1, 20, 1)) };
        await using var anfitrion = await ComoEncargado(falso);

        var (estado, html) = await Pedir(anfitrion, "/sucursal/LIM-01/pedidos", HostWeb.Gestion);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal(["listar:LIM-01"], falso.Llamadas);
        Assert.Contains("Listo para recojo", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/sucursal/LIM-01/pedidos/{Pedido:D}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(NombreDelContacto, html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-08")]
    public async Task El_detalle_de_la_sucursal_ofrece_elegir_lineas_con_las_tres_acciones_y_el_aviso_del_traslado()
    {
        await using var anfitrion = await ComoEncargado(new GestionDeRecojoFalsa());

        var (_, html) = await Pedir(anfitrion, $"/sucursal/LIM-01/pedidos/{Pedido:D}?hecho=preparar&omitidas=1", HostWeb.Gestion);

        foreach (var ruta in new[] { "preparar", "listas", "entregar" })
        {
            Assert.Contains($"formaction=\"/sucursal/LIM-01/pedidos/{Pedido:D}/{ruta}\"", html, StringComparison.Ordinal);
        }

        Assert.Contains($"value=\"{LineaA:D}\"", html, StringComparison.Ordinal);
        Assert.Contains("Traslado en camino, llega el 25/11/2026", html, StringComparison.Ordinal);
        Assert.Contains("1 línea no cambió", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Entrega a domicilio", html, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PED-08")]
    [InlineData("preparar")]
    [InlineData("listas")]
    [InlineData("entregar")]
    public async Task Operar_lineas_responde_303_al_detalle_con_cuantas_no_cambiaron_y_lleva_solo_los_identificadores_pedidos(string accion)
    {
        var falso = new GestionDeRecojoFalsa();
        await using var anfitrion = await ComoEncargado(falso);

        using var respuesta = await Enviar(anfitrion, $"/sucursal/LIM-01/pedidos/{Pedido:D}/{accion}", HostWeb.Gestion, [Id(LineaA), Id(LineaB)]);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal($"/sucursal/LIM-01/pedidos/{Pedido:D}?hecho={accion}&omitidas=1", respuesta.Headers.Location!.OriginalString);
        Assert.Equal([$"{accion}:LIM-01:{Pedido:D}:{LineaA:D},{LineaB:D}"], falso.Llamadas);
    }

    [Theory]
    [Trait("Criterio", "AC-PED-25")]
    [InlineData("")]
    [InlineData("lineaIds=no-es-un-guid")]
    [InlineData("lineaIds=00000000-0000-0000-0000-000000000000")]
    [InlineData("lineaIds=b1000000-0000-0000-0000-00000000000a&lineaIds=b1000000-0000-0000-0000-00000000000a")]
    [InlineData("lineaIds=b1000000-0000-0000-0000-00000000000a&pedidoId=a1000000-0000-0000-0000-000000000001")]
    public async Task Un_cuerpo_invalido_es_validacion_y_no_llega_a_la_Aplicacion(string cuerpo)
    {
        var falso = new GestionDeRecojoFalsa();
        await using var anfitrion = await ComoEncargado(falso);
        var campos = cuerpo.Length == 0 ? [] : cuerpo.Split('&').Select(p => p.Split('=')).Select(p => new KeyValuePair<string, string>(p[0], p[1])).ToList();

        using var respuesta = await Enviar(anfitrion, $"/sucursal/LIM-01/pedidos/{Pedido:D}/preparar", HostWeb.Gestion, campos);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(falso.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-25")]
    public async Task Mas_lineas_que_el_tope_de_un_pedido_es_validacion()
    {
        var falso = new GestionDeRecojoFalsa();
        await using var anfitrion = await ComoEncargado(falso);
        var demasiadas = Enumerable.Range(0, 31).Select(_ => Id(Guid.NewGuid())).ToList();

        using var respuesta = await Enviar(anfitrion, $"/sucursal/LIM-01/pedidos/{Pedido:D}/entregar", HostWeb.Gestion, demasiadas);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(falso.Llamadas);
    }

    [Theory]
    [Trait("Criterio", "AC-PED-09")]
    [InlineData(CategoriaDeError.Conflicto, "linea-espera-traslado", 409, "ECO-LINEA-ESPERA-TRASLADO", "El libro aún espera su traslado")]
    [InlineData(CategoriaDeError.Conflicto, "pedido-operacion-en-curso", 409, "ECO-PEDIDO-OPERACION-EN-CURSO", "Ya hay una operación en curso")]
    [InlineData(CategoriaDeError.NoDisponible, "servicio-no-disponible", 503, "ECO-SERVICIO-NO-DISPONIBLE", "El servicio no respondió")]
    public async Task Los_fallos_de_una_operacion_se_pintan_con_su_codigo_y_el_enlace_para_volver_al_pedido(CategoriaDeError categoria, string clave, int estado, string codigo, string texto)
    {
        var falso = new GestionDeRecojoFalsa { Operacion = Resultado<LineasOperadas>.Fallo(Error(categoria, clave)) };
        await using var anfitrion = await ComoEncargado(falso);

        using var respuesta = await Enviar(anfitrion, $"/sucursal/LIM-01/pedidos/{Pedido:D}/entregar", HostWeb.Gestion, [Id(LineaA)], html: true);
        var html = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());

        Assert.Equal(estado, (int)respuesta.StatusCode);
        Assert.Contains(texto, html, StringComparison.Ordinal);
        Assert.Contains(codigo, html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-07")]
    public async Task Un_pedido_de_otra_sucursal_o_un_rol_fuera_de_alcance_se_pinta_igual_que_uno_inexistente()
    {
        var falso = new GestionDeRecojoFalsa { Detalle = Resultado<DetalleDelPedidoDeSucursal>.Fallo(Error(CategoriaDeError.NoEncontrado, "no-encontrado")) };
        await using var anfitrion = await ComoEncargado(falso);

        var (estado, html) = await Pedir(anfitrion, $"/sucursal/CUS-03/pedidos/{Pedido:D}", HostWeb.Gestion);

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Contains("ECO-NO-ENCONTRADO", html, StringComparison.Ordinal);
        Assert.DoesNotContain("LIM-01", html, StringComparison.Ordinal);
    }

    // ---------- Logística ----------

    [Fact]
    [Trait("Criterio", "AC-PED-06")]
    public async Task El_detalle_de_logistica_muestra_el_contacto_y_la_lista_nunca()
    {
        var falso = new GestionDeDespachoFalsa();
        await using var anfitrion = await ComoLogistica(falso);

        var (_, lista) = await Pedir(anfitrion, "/logistica/pedidos", HostWeb.Gestion);
        var (estado, detalle) = await Pedir(anfitrion, $"/logistica/pedidos/{Pedido:D}", HostWeb.Gestion);

        Assert.DoesNotContain(NombreDelContacto, lista, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, estado);
        foreach (var texto in new[] { NombreDelContacto, TelefonoDelContacto, DireccionDelContacto })
        {
            Assert.Contains(texto, detalle, StringComparison.Ordinal);
        }

        Assert.Contains($"formaction=\"/logistica/pedidos/{Pedido:D}/listas\"", detalle, StringComparison.Ordinal);
        Assert.DoesNotContain("/entregar\"", detalle, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-23")]
    public async Task Si_la_lectura_del_contacto_no_se_pudo_auditar_se_oculta_solo_el_contacto()
    {
        var falso = new GestionDeDespachoFalsa
        {
            Detalle = Resultado<DetalleDelPedidoDePersonal>.Exito(new DetalleDelPedidoDePersonal(Resumen(EstadoVisibleDelPedido.Confirmado, TipoDeEntregaVisible.Domicilio), 12m, "Ayacucho", [Linea(LineaA, "Cien años de soledad")], null, true, null, ContactoAnonimizado: false)),
        };
        await using var anfitrion = await ComoLogistica(falso);

        var (estado, html) = await Pedir(anfitrion, $"/logistica/pedidos/{Pedido:D}", HostWeb.Gestion);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("No pudimos mostrar los datos de contacto ahora", html, StringComparison.Ordinal);
        Assert.Contains("Cien años de soledad", html, StringComparison.Ordinal);
        Assert.DoesNotContain(NombreDelContacto, html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-25")]
    public async Task Marcar_listas_a_domicilio_responde_303_y_sin_permiso_el_rechazo_es_el_mismo_403()
    {
        var falso = new GestionDeDespachoFalsa();
        await using var anfitrion = await ComoLogistica(falso);

        using var respuesta = await Enviar(anfitrion, $"/logistica/pedidos/{Pedido:D}/listas", HostWeb.Gestion, [Id(LineaA)]);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal($"/logistica/pedidos/{Pedido:D}?hecho=listas", respuesta.Headers.Location!.OriginalString);

        falso.Operacion = Resultado<LineasOperadas>.Fallo(Error(CategoriaDeError.Prohibido, "prohibido"));
        using var rechazada = await Enviar(anfitrion, $"/logistica/pedidos/{Pedido:D}/preparar", HostWeb.Gestion, [Id(LineaA)]);
        var json = JsonDePrueba.Leer(await rechazada.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, rechazada.StatusCode);
        Assert.Equal("ECO-PROHIBIDO", json.GetProperty("code").GetString());
    }

    // ---------- Administración ----------

    [Fact]
    [Trait("Criterio", "AC-PED-11")]
    public async Task El_detalle_de_administracion_ofrece_anular_con_la_lista_cerrada_de_motivos_y_reintentar_el_reembolso()
    {
        await using var anfitrion = await ComoAdministracion(new AdministracionFalsa());

        var (estado, html) = await Pedir(anfitrion, $"/administracion/pedidos/{Pedido:D}", HostWeb.Gestion);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains($"action=\"/administracion/pedidos/{Pedido:D}/anular\"", html, StringComparison.Ordinal);
        Assert.Contains($"action=\"/administracion/pedidos/{Pedido:D}/reembolso/reintentar\"", html, StringComparison.Ordinal);
        foreach (var motivo in new[] { "value=\"pago-duplicado\"", "value=\"no-recogido\"", "value=\"otro\"" })
        {
            Assert.Contains(motivo, html, StringComparison.Ordinal);
        }

        Assert.Contains($"{FormatoPeruano.Soles(85.50m)} pendiente", html, StringComparison.Ordinal);
        Assert.Contains("/administracion/reembolsos", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"nota\"", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-11")]
    public async Task Anular_con_un_motivo_responde_303_y_llama_una_sola_vez()
    {
        var falso = new AdministracionFalsa();
        await using var anfitrion = await ComoAdministracion(falso);

        using var respuesta = await Enviar(anfitrion, $"/administracion/pedidos/{Pedido:D}/anular", HostWeb.Gestion, [new("motivo", "pago-duplicado")]);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal($"/administracion/pedidos/{Pedido:D}?hecho=anular", respuesta.Headers.Location!.OriginalString);
        Assert.Equal([$"anular:{Pedido:D}:pago-duplicado"], falso.Llamadas);
    }

    [Theory]
    [Trait("Criterio", "AC-PED-12")]
    [InlineData("")]
    [InlineData("motivo=")]
    [InlineData("motivo=otro&nota=texto-libre")]
    [InlineData("motivo=otro&motivo=no-recogido")]
    public async Task Anular_sin_un_motivo_unico_y_sin_campos_extra_es_validacion(string cuerpo)
    {
        var falso = new AdministracionFalsa();
        await using var anfitrion = await ComoAdministracion(falso);
        var campos = cuerpo.Length == 0 ? [] : cuerpo.Split('&').Select(p => p.Split('=')).Select(p => new KeyValuePair<string, string>(p[0], p.Length > 1 ? p[1] : string.Empty)).ToList();

        using var respuesta = await Enviar(anfitrion, $"/administracion/pedidos/{Pedido:D}/anular", HostWeb.Gestion, campos);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(falso.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-12")]
    public async Task Un_motivo_fuera_de_la_lista_lo_rechaza_la_Aplicacion_con_validacion_y_un_pedido_sin_lineas_anulables_es_422()
    {
        var falso = new AdministracionFalsa { Anulacion = Resultado<ResultadoDeAnulacion>.Fallo(Error(CategoriaDeError.Validacion, "validacion-fallida", new ErrorDeCampo("motivo", "no-permitido"))) };
        await using var anfitrion = await ComoAdministracion(falso);

        using var fueraDeLista = await Enviar(anfitrion, $"/administracion/pedidos/{Pedido:D}/anular", HostWeb.Gestion, [new("motivo", "porque-si")]);
        falso.Anulacion = Resultado<ResultadoDeAnulacion>.Fallo(Error(CategoriaDeError.ReglaDeNegocio, "pedido-sin-lineas-anulables"));
        using var sinLineas = await Enviar(anfitrion, $"/administracion/pedidos/{Pedido:D}/anular", HostWeb.Gestion, [new("motivo", "otro")]);

        Assert.Equal(HttpStatusCode.BadRequest, fueraDeLista.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sinLineas.StatusCode);
        Assert.Equal("ECO-PEDIDO-SIN-LINEAS-ANULABLES", JsonDePrueba.Leer(await sinLineas.Content.ReadAsStringAsync()).GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Criterio", "AC-PED-19")]
    public async Task Reintentar_el_reembolso_responde_303_y_un_rol_sin_permiso_recibe_el_mismo_403()
    {
        var falso = new AdministracionFalsa();
        await using var anfitrion = await ComoAdministracion(falso);

        using var respuesta = await Enviar(anfitrion, $"/administracion/pedidos/{Pedido:D}/reembolso/reintentar", HostWeb.Gestion);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal($"/administracion/pedidos/{Pedido:D}?hecho=reembolso", respuesta.Headers.Location!.OriginalString);
        Assert.Equal([$"reintentar:{Pedido:D}"], falso.Llamadas);

        falso.Anulacion = Resultado<ResultadoDeAnulacion>.Fallo(Error(CategoriaDeError.Prohibido, "prohibido"));
        using var rechazada = await Enviar(anfitrion, $"/administracion/pedidos/{Pedido:D}/reembolso/reintentar", HostWeb.Gestion);
        Assert.Equal(HttpStatusCode.Forbidden, rechazada.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-19")]
    public async Task Las_operaciones_fallidas_se_listan_con_sus_pasos_y_sin_datos_personales_y_vacias_dicen_que_no_hay()
    {
        var falso = new AdministracionFalsa();
        await using var anfitrion = await ComoAdministracion(falso);
        var (_, vacio) = await Pedir(anfitrion, "/administracion/pedidos/operaciones-fallidas", HostWeb.Gestion);

        falso.Fallidas = Resultado<PaginaDeOperacionesFallidas>.Exito(new PaginaDeOperacionesFallidas(
            [new OperacionFallidaVisible(Guid.NewGuid(), Pedido, "anulacion", "pago-duplicado", ["envios-cancelados", "reservas-liberadas"], Creado)], 1, 20, 1));
        var (estado, html) = await Pedir(anfitrion, "/administracion/pedidos/operaciones-fallidas", HostWeb.Gestion);

        Assert.Contains("No hay operaciones fallidas", vacio, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, estado);
        foreach (var texto in new[] { "Anulación", "pago-duplicado", "envios-cancelados", "reservas-liberadas", $"href=\"/administracion/pedidos/{Pedido:D}\"" })
        {
            Assert.Contains(texto, html, StringComparison.Ordinal);
        }
    }
}
