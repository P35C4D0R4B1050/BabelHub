using System.Net;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.IntegrationTests.Web;
using BabelHub.Shared.Errores;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Devoluciones;

/// <summary>
/// Las pantallas de Devoluciones con las vistas reales y la sesión web real (T26, T27, T28; AC-DEV-01, AC-DEV-04, AC-DEV-06, AC-DEV-07, AC-DEV-12, AC-DEV-20, AC-DEV-23, AC-DEV-24, AC-DEV-30, AC-DEV-31): el estado de cada línea con texto e icono, las acciones que
/// permite el estado, el reembolso pendiente, los estados estándar y que el motivo y el texto de la persona <b>se muestren siempre codificados</b> (un <c>&lt;script&gt;</c> sembrado no llega al HTML). Los contratos de la Aplicación son dobles.
/// </summary>
public sealed class PantallasDeDevolucionesTests
{
    private const string Sembrado = "<script>alert(1)</script>";

    private static readonly Guid Devolucion = DevolucionesFalsas.DevolucionId;
    private static readonly Guid Pedido = Guid.Parse("a1000000-0000-0000-0000-000000000001");

    private static DevolucionVista Vista(
        TipoDeLaDevolucion tipo = TipoDeLaDevolucion.Impreso,
        OrigenDeLaDevolucion origen = OrigenDeLaDevolucion.Solicitud,
        EstadoDeLaLineaDeDevolucion estado = EstadoDeLaLineaDeDevolucion.Solicitada,
        bool pendiente = false,
        bool redactada = false) =>
        new(
            Devolucion,
            Pedido,
            tipo,
            origen,
            tipo == TipoDeLaDevolucion.Impreso ? "lima-central" : null,
            new TextoLibre(redactada ? TextoLibre.Redactado : "Motivo " + Sembrado),
            origen == OrigenDeLaDevolucion.Reporte ? new TextoLibre("Texto " + Sembrado) : null,
            redactada,
            new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero),
            [new LineaDeDevolucionVista(Guid.NewGuid(), Guid.NewGuid(), 2, 85.50m, estado, new DateTimeOffset(2026, 11, 20, 16, 0, 0, TimeSpan.Zero), pendiente)]);

    private static Resultado<PaginaDeDevoluciones> Pagina(params DevolucionVista[] devoluciones) => Resultado<PaginaDeDevoluciones>.Exito(new PaginaDeDevoluciones(devoluciones, 1, 20, devoluciones.Length));

    private static Task<AnfitrionWeb> Iniciar(RolWeb rol, HostWeb host, DevolucionesFalsas falso) =>
        AnfitrionConSesion.Iniciar(
            new SesionesWebDePrueba().ConSesionValida(rol, rol == RolWeb.Cliente ? null : Guid.NewGuid()),
            host,
            servicios: s =>
            {
                s.AddSingleton<IDevolucionesDelCliente>(falso);
                s.AddSingleton<IGestionDeDevoluciones>(falso);
            });

    private static async Task<(HttpStatusCode Estado, string Html)> Pedir(AnfitrionWeb anfitrion, string ruta, HostWeb host)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        solicitud.Headers.TryAddWithoutValidation("Cookie", AnfitrionConSesion.Cookie(host));
        using var respuesta = await anfitrion.Cliente.SendAsync(solicitud);
        return (respuesta.StatusCode, await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-20")]
    public async Task Mis_devoluciones_pinta_cada_linea_con_su_estado_en_texto_e_icono_y_el_reembolso_pendiente()
    {
        var falso = new DevolucionesFalsas { Lista = Pagina(Vista(estado: EstadoDeLaLineaDeDevolucion.Recibida, pendiente: true), Vista(TipoDeLaDevolucion.Digital, estado: EstadoDeLaLineaDeDevolucion.Reembolsada)) };
        await using var anfitrion = await Iniciar(RolWeb.Cliente, HostWeb.Tienda, falso);

        var (estado, html) = await Pedir(anfitrion, "/cuenta/devoluciones", HostWeb.Tienda);

        Assert.Equal(HttpStatusCode.OK, estado);
        foreach (var texto in new[] { "Mis devoluciones", "Recibida", "Reembolsada", "reembolso pendiente", "Libro impreso", "Libro digital", "2 devoluciones" })
        {
            Assert.Contains(texto, html, StringComparison.Ordinal);
        }

        Assert.Equal(["listar-mias"], falso.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-31")]
    public async Task El_detalle_muestra_el_motivo_y_el_texto_codificados_nunca_como_marcado()
    {
        var falso = new DevolucionesFalsas { Respuesta = Resultado<DevolucionVista>.Exito(Vista(TipoDeLaDevolucion.Digital, OrigenDeLaDevolucion.Reporte)) };
        await using var anfitrion = await Iniciar(RolWeb.Cliente, HostWeb.Tienda, falso);

        var (estado, html) = await Pedir(anfitrion, $"/cuenta/devoluciones/{Devolucion:D}", HostWeb.Tienda);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.DoesNotContain(Sembrado, html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("reporte de archivo defectuoso", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-27")]
    public async Task Una_devolucion_redactada_no_muestra_el_texto_y_explica_por_que()
    {
        var falso = new DevolucionesFalsas { Respuesta = Resultado<DevolucionVista>.Exito(Vista(redactada: true)) };
        await using var anfitrion = await Iniciar(RolWeb.Cliente, HostWeb.Tienda, falso);

        var (_, html) = await Pedir(anfitrion, $"/cuenta/devoluciones/{Devolucion:D}", HostWeb.Tienda);

        Assert.Contains("se eliminó por la política de privacidad", html, StringComparison.Ordinal);
        Assert.DoesNotContain(TextoLibre.Redactado, html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-06")]
    public async Task Sin_devoluciones_se_muestra_el_estado_vacio()
    {
        await using var anfitrion = await Iniciar(RolWeb.Cliente, HostWeb.Tienda, new DevolucionesFalsas());

        var (_, html) = await Pedir(anfitrion, "/cuenta/devoluciones", HostWeb.Tienda);

        Assert.Contains("Aún no hay devoluciones", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-20")]
    [InlineData(CategoriaDeError.NoDisponible, "servicio-no-disponible", HttpStatusCode.ServiceUnavailable)]
    [InlineData(CategoriaDeError.NoEncontrado, "no-encontrado", HttpStatusCode.NotFound)]
    [InlineData(CategoriaDeError.Limite, "limite-excedido", HttpStatusCode.TooManyRequests)]
    public async Task Los_fallos_de_la_lista_se_pintan_como_estado_estandar_con_el_codigo_del_modulo(CategoriaDeError categoria, string clave, HttpStatusCode codigo)
    {
        var falso = new DevolucionesFalsas { Lista = Resultado<PaginaDeDevoluciones>.Fallo(new ErrorDeAplicacion(categoria, clave, categoria == CategoriaDeError.Limite ? [new ErrorDeCampo("espera-en-segundos", "30")] : [])) };
        await using var anfitrion = await Iniciar(RolWeb.Cliente, HostWeb.Tienda, falso);

        var (estado, html) = await Pedir(anfitrion, "/cuenta/devoluciones", HostWeb.Tienda);

        Assert.Equal(codigo, estado);
        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-07")]
    public async Task La_sucursal_ofrece_aprobar_y_rechazar_una_solicitada_y_confirmar_la_recepcion_una_aprobada_con_su_token()
    {
        var solicitada = new DevolucionesFalsas { Respuesta = Resultado<DevolucionVista>.Exito(Vista()) };
        await using var primero = await Iniciar(RolWeb.Encargado, HostWeb.Gestion, solicitada);
        var (_, htmlSolicitada) = await Pedir(primero, $"/sucursal/lima-central/devoluciones/{Devolucion:D}", HostWeb.Gestion);

        var aprobada = new DevolucionesFalsas { Respuesta = Resultado<DevolucionVista>.Exito(Vista(estado: EstadoDeLaLineaDeDevolucion.Aprobada)) };
        await using var segundo = await Iniciar(RolWeb.Encargado, HostWeb.Gestion, aprobada);
        var (_, htmlAprobada) = await Pedir(segundo, $"/sucursal/lima-central/devoluciones/{Devolucion:D}", HostWeb.Gestion);

        Assert.Contains($"action=\"/sucursal/lima-central/devoluciones/{Devolucion:D}/aprobar\"", htmlSolicitada, StringComparison.Ordinal);
        Assert.Contains($"action=\"/sucursal/lima-central/devoluciones/{Devolucion:D}/rechazar\"", htmlSolicitada, StringComparison.Ordinal);
        Assert.DoesNotContain("/recibir", htmlSolicitada, StringComparison.Ordinal);
        Assert.Contains($"action=\"/sucursal/lima-central/devoluciones/{Devolucion:D}/recibir\"", htmlAprobada, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", htmlSolicitada, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-30")]
    public async Task Un_encargado_de_otra_sucursal_ve_el_rechazo_sin_permiso_y_no_se_pinta_nada_de_la_devolucion()
    {
        var falso = new DevolucionesFalsas { Respuesta = Resultado<DevolucionVista>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "no-encontrado")) };
        await using var anfitrion = await Iniciar(RolWeb.Encargado, HostWeb.Gestion, falso);

        var (estado, html) = await Pedir(anfitrion, $"/sucursal/arequipa/devoluciones/{Devolucion:D}", HostWeb.Gestion);

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.DoesNotContain(Sembrado, html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-23")]
    public async Task La_administracion_decide_un_reporte_y_no_ve_las_acciones_de_un_impreso_en_un_reporte()
    {
        var falso = new DevolucionesFalsas { Respuesta = Resultado<DevolucionVista>.Exito(Vista(TipoDeLaDevolucion.Digital, OrigenDeLaDevolucion.Reporte)) };
        await using var anfitrion = await Iniciar(RolWeb.AdministradorCentral, HostWeb.Gestion, falso);

        var (_, html) = await Pedir(anfitrion, $"/administracion/devoluciones/{Devolucion:D}", HostWeb.Gestion);

        Assert.Contains($"action=\"/administracion/devoluciones/{Devolucion:D}/aprobar-reporte\"", html, StringComparison.Ordinal);
        Assert.Contains($"action=\"/administracion/devoluciones/{Devolucion:D}/rechazar-reporte\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"/devoluciones/{Devolucion:D}/aprobar\"", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-24")]
    public async Task La_administracion_lista_todas_y_los_reportes_con_su_filtro_y_el_reembolso_pendiente()
    {
        var falso = new DevolucionesFalsas { Lista = Pagina(Vista(estado: EstadoDeLaLineaDeDevolucion.Aprobada, pendiente: true)) };
        await using var anfitrion = await Iniciar(RolWeb.AdministradorCentral, HostWeb.Gestion, falso);

        var (_, todas) = await Pedir(anfitrion, "/administracion/devoluciones?tipo=impreso", HostWeb.Gestion);
        var consultaDeTodas = falso.UltimaConsultaDeLaAdministracion!;
        var (_, reportes) = await Pedir(anfitrion, "/administracion/devoluciones/reportes", HostWeb.Gestion);

        Assert.Equal(TipoDeLaDevolucion.Impreso, consultaDeTodas.Tipo);
        Assert.False(consultaDeTodas.SoloReportes);
        Assert.True(falso.UltimaConsultaDeLaAdministracion!.SoloReportes);
        Assert.Contains("reembolso pendiente", todas, StringComparison.Ordinal);
        Assert.Contains("Reportes de archivos defectuosos", reportes, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-04")]
    public async Task La_pantalla_de_devolver_ofrece_los_impresos_entregados_con_su_plazo_los_digitales_con_su_politica_y_el_aviso_de_privacidad()
    {
        var entregada = new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
        var impresa = new LineaVisibleDelPedido(Guid.NewGuid(), "Cien años de soledad", "9786123456789", false, 2, 42.75m, 85.50m, EstadoVisibleDeLaLinea.Entregado, "lima-central", null, null, entregada, null);
        var digital = new LineaVisibleDelPedido(Guid.NewGuid(), "Pedro Páramo", "9786123456790", true, 1, 20m, 20m, EstadoVisibleDeLaLinea.DisponibleEnLaBiblioteca, null, null, null, null, null);
        var resumen = new ResumenDePedido(Pedido, "BH-2026-001", entregada, EstadoVisibleDelPedido.Confirmado, TipoDeEntregaVisible.Domicilio, null, 105.50m, 2);
        var pedidos = new PedidosDelClienteDeDevolver(Resultado<DetalleDelPedidoDelCliente>.Exito(new DetalleDelPedidoDelCliente(resumen, 0m, null, [impresa, digital], null, false, AlternativaDeCancelacion.Ninguna, null)));
        await using var anfitrion = await AnfitrionConSesion.Iniciar(
            new SesionesWebDePrueba().ConSesionValida(RolWeb.Cliente, null),
            HostWeb.Tienda,
            servicios: s => s.AddSingleton<IPedidosDelCliente>(pedidos));

        var (estado, html) = await Pedir(anfitrion, $"/cuenta/pedidos/{Pedido:D}/devolver", HostWeb.Tienda);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains($"action=\"/cuenta/pedidos/{Pedido:D}/devoluciones\"", html, StringComparison.Ordinal);
        Assert.Contains($"name=\"cantidad.{impresa.LineaId:D}\"", html, StringComparison.Ordinal);
        Assert.Contains("No escribas datos innecesarios", html, StringComparison.Ordinal);
        Assert.Contains($"action=\"/cuenta/pedidos/{Pedido:D}/devoluciones/digital\"", html, StringComparison.Ordinal);
        Assert.Contains($"action=\"/cuenta/pedidos/{Pedido:D}/devoluciones/reporte\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("type=\"file\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("multipart", html, StringComparison.Ordinal);
    }

    private sealed class PedidosDelClienteDeDevolver(Resultado<DetalleDelPedidoDelCliente> detalle) : IPedidosDelCliente
    {
        public Task<Resultado<PaginaDePedidos>> Listar(FiltroDePedidos filtro, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<DetalleDelPedidoDelCliente>> Obtener(Guid pedidoId, CancellationToken cancelacion = default) => Task.FromResult(detalle);

        public Task<Resultado<ResultadoDeCancelacionDePedido>> Cancelar(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    [Fact]
    [Trait("Criterio", "AC-PED-41")]
    public async Task La_pantalla_de_devolver_rotula_cada_linea_con_su_sucursal_y_no_preselecciona_la_receptora_ni_con_la_preferida_guardada()
    {
        var entregada = new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
        LineaVisibleDelPedido Impresa(string sucursal) =>
            new(Guid.NewGuid(), "Cien años de soledad", "9786123456789", false, 2, 42.75m, 85.50m, EstadoVisibleDeLaLinea.Entregado, sucursal, null, null, entregada, null, CodigoDeSucursal: sucursal);
        var resumen = new ResumenDePedido(Pedido, "BH-2026-001", entregada, EstadoVisibleDelPedido.Entregado, TipoDeEntregaVisible.Recojo, null, 171m, 2);
        var pedidos = new PedidosDelClienteDeDevolver(Resultado<DetalleDelPedidoDelCliente>.Exito(new DetalleDelPedidoDelCliente(resumen, 0m, null, [Impresa("cusco"), Impresa("ayacucho")], null, false, AlternativaDeCancelacion.Ninguna, null)));
        await using var anfitrion = await AnfitrionConSesion.Iniciar(
            new SesionesWebDePrueba().ConSesionValida(RolWeb.Cliente, null),
            HostWeb.Tienda,
            servicios: s =>
            {
                s.AddSingleton<IPedidosDelCliente>(pedidos);
                s.AddSingleton<IListaDeSucursales>(new SucursalesParaDevolver());
            });
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, $"/cuenta/pedidos/{Pedido:D}/devolver");
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        solicitud.Headers.TryAddWithoutValidation("Cookie", AnfitrionConSesion.Cookie(HostWeb.Tienda) + "; bh-sucursal=cusco");

        using var respuesta = await anfitrion.Cliente.SendAsync(solicitud);
        var html = System.Net.WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Matches(@"(?s)Cien años de soledad · Atiende: Sucursal Cusco.*?Cien años de soledad · Atiende: Sucursal Ayacucho", html);
        var receptora = System.Text.RegularExpressions.Regex.Match(html, @"(?s)<select name=""sucursalReceptora"".*?</select>").Value;
        Assert.Matches(@"<option value="""">Elige una sucursal</option>", receptora);
        Assert.Contains("value=\"cusco\"", receptora, StringComparison.Ordinal);
        Assert.DoesNotContain("selected", receptora, StringComparison.Ordinal);
    }

    private sealed class SucursalesParaDevolver : IListaDeSucursales
    {
        public Task<ListaDeSucursales?> Obtener(CancellationToken cancelacion = default) =>
            Task.FromResult<ListaDeSucursales?>(new ListaDeSucursales([new("ayacucho", "Sucursal Ayacucho"), new("cusco", "Sucursal Cusco")], true));
    }
}
