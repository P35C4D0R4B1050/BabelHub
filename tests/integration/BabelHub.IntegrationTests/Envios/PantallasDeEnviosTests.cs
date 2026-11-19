using System.Net;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Envios;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.IntegrationTests.Web;
using BabelHub.Shared.Errores;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Envios;

/// <summary>Las pantallas de Envíos con las vistas reales (T18, T19; AC-ENV-05, AC-ENV-08, AC-ENV-09, AC-ENV-11): la lista de logística y su acción por estado, el bloque del titular y los estados estándar, sin datos de contacto.</summary>
public sealed class PantallasDeEnviosTests
{
    private static readonly DateTimeOffset Creado = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

    private sealed class EnviosDelClienteFalso : IEnviosDelCliente
    {
        public Resultado<EnviosDeMiPedido> Respuesta { get; set; } = Resultado<EnviosDeMiPedido>.Exito(new EnviosDeMiPedido(Guid.NewGuid(), []));

        public Task<Resultado<EnviosDeMiPedido>> ObtenerDeUnPedido(Guid pedidoId, CancellationToken cancelacion = default) => Task.FromResult(Respuesta);
    }

    private static ResumenDeEnvio Resumen(EstadoDelEnvio estado, string? guia = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "lima-central", estado, guia, guia is null ? null : "operador-a", 2, Creado, guia is null ? null : Creado.AddHours(1), estado is EstadoDelEnvio.EnReparto or EstadoDelEnvio.Entregado ? Creado.AddHours(2) : null, estado == EstadoDelEnvio.Entregado ? Creado.AddHours(5) : null);

    private static async Task<AnfitrionWeb> IniciarLogistica(EnviosDeLogisticaFalso falso, RolWeb rol = RolWeb.Logistica) =>
        await AnfitrionConSesion.Iniciar(
            new SesionesWebDePrueba().ConSesionValida(rol, Guid.NewGuid()),
            HostWeb.Gestion,
            servicios: s => s.AddSingleton<IEnviosDeLogistica>(falso));

    private static async Task<(HttpStatusCode Estado, string Html)> Pedir(AnfitrionWeb anfitrion, string ruta, HostWeb host, bool htmx = false)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        solicitud.Headers.TryAddWithoutValidation("Cookie", AnfitrionConSesion.Cookie(host));
        if (htmx)
        {
            solicitud.Headers.TryAddWithoutValidation("HX-Request", "true");
        }

        using var respuesta = await anfitrion.Cliente.SendAsync(solicitud);
        var cuerpo = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
        return (respuesta.StatusCode, cuerpo);
    }

    // ---------- Logística: la lista ----------

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task La_lista_pinta_cada_estado_con_texto_e_icono_la_guia_y_solo_la_accion_que_sigue_con_su_token()
    {
        var pendiente = Resumen(EstadoDelEnvio.PendienteDeGuia);
        var conGuia = Resumen(EstadoDelEnvio.GuiaGenerada, "GUIA-000123");
        var enReparto = Resumen(EstadoDelEnvio.EnReparto, "GUIA-000124");
        var entregado = Resumen(EstadoDelEnvio.Entregado, "GUIA-000125");
        var cancelado = Resumen(EstadoDelEnvio.Cancelado);
        var falso = new EnviosDeLogisticaFalso { RespuestaDeLista = Resultado<PaginaDeEnvios>.Exito(new PaginaDeEnvios([pendiente, conGuia, enReparto, entregado, cancelado], 1, 20, 5)) };
        await using var anfitrion = await IniciarLogistica(falso);

        var (estado, html) = await Pedir(anfitrion, "/logistica/envios", HostWeb.Gestion);

        Assert.Equal(HttpStatusCode.OK, estado);
        foreach (var texto in new[] { "Sin guía", "Con guía", "En reparto", "Entregado", "Cancelado", "GUIA-000123", "operador-a", "lima-central", "5 envíos" })
        {
            Assert.Contains(texto, html, StringComparison.Ordinal);
        }

        Assert.Contains($"action=\"/logistica/envios/{pendiente.Id:D}/guia\"", html, StringComparison.Ordinal);
        Assert.Contains($"action=\"/logistica/envios/{conGuia.Id:D}/reparto\"", html, StringComparison.Ordinal);
        Assert.Contains($"action=\"/logistica/envios/{enReparto.Id:D}/entregado\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"/logistica/envios/{entregado.Id:D}/", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"/logistica/envios/{cancelado.Id:D}/", html, StringComparison.Ordinal);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(html, "name=\"__RequestVerificationToken\"").Count >= 3);
        Assert.Contains("Generar guía", html, StringComparison.Ordinal);
        Assert.Contains("Pasar a en reparto", html, StringComparison.Ordinal);
        Assert.Contains("Marcar entregado", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task El_filtro_por_estado_y_la_pagina_llegan_a_la_Aplicacion_y_el_aviso_confirma_la_ultima_accion()
    {
        var falso = new EnviosDeLogisticaFalso { RespuestaDeLista = Resultado<PaginaDeEnvios>.Exito(new PaginaDeEnvios([Resumen(EstadoDelEnvio.EnReparto, "GUIA-000124")], 2, 20, 45)) };
        await using var anfitrion = await IniciarLogistica(falso);

        var (_, html) = await Pedir(anfitrion, "/logistica/envios?estado=en-reparto&pagina=2&hecho=reparto", HostWeb.Gestion);

        Assert.Equal(EstadoDelEnvio.EnReparto, falso.UltimaConsulta!.Estado);
        Assert.Equal(2, falso.UltimaConsulta.Pagina);
        Assert.Contains("Envío pasado a reparto.", html, StringComparison.Ordinal);
        Assert.Contains("Página 2 de 3", html, StringComparison.Ordinal);
        Assert.Contains("estado=en-reparto", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Sin_envios_se_muestra_el_estado_vacio_y_con_un_filtro_se_ofrece_ver_todos()
    {
        var falso = new EnviosDeLogisticaFalso();
        await using var anfitrion = await IniciarLogistica(falso);

        var (_, vacio) = await Pedir(anfitrion, "/logistica/envios", HostWeb.Gestion);
        var (_, filtrado) = await Pedir(anfitrion, "/logistica/envios?estado=entregado", HostWeb.Gestion);

        Assert.Contains("Aún no hay envíos", vacio, StringComparison.Ordinal);
        Assert.Contains("No hay envíos en este estado", filtrado, StringComparison.Ordinal);
        Assert.Contains("Ver todos", filtrado, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-08")]
    [InlineData(CategoriaDeError.NoDisponible, "servicio-no-disponible", HttpStatusCode.ServiceUnavailable, "No podemos")]
    [InlineData(CategoriaDeError.Prohibido, "prohibido", HttpStatusCode.Forbidden, "No tienes acceso")]
    [InlineData(CategoriaDeError.Limite, "limite-excedido", HttpStatusCode.TooManyRequests, "Espera un momento")]
    public async Task Los_fallos_de_la_lista_se_pintan_como_estado_estandar_con_el_codigo_del_modulo(CategoriaDeError categoria, string clave, HttpStatusCode codigo, string texto)
    {
        var falso = new EnviosDeLogisticaFalso { RespuestaDeLista = Resultado<PaginaDeEnvios>.Fallo(new ErrorDeAplicacion(categoria, clave, categoria == CategoriaDeError.Limite ? [new ErrorDeCampo("espera-en-segundos", "30")] : [])) };
        await using var anfitrion = await IniciarLogistica(falso);

        var (estado, html) = await Pedir(anfitrion, "/logistica/envios", HostWeb.Gestion);

        Assert.Equal(codigo, estado);
        Assert.Contains(texto, html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Un_encargado_entra_al_area_pero_la_Aplicacion_lo_rechaza_y_se_pinta_sin_permiso()
    {
        var falso = new EnviosDeLogisticaFalso { RespuestaDeLista = Resultado<PaginaDeEnvios>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "prohibido")) };
        await using var anfitrion = await IniciarLogistica(falso, RolWeb.Encargado);

        var (estado, html) = await Pedir(anfitrion, "/logistica/envios", HostWeb.Gestion);

        Assert.Equal(HttpStatusCode.Forbidden, estado);
        Assert.Contains("No tienes acceso", html, StringComparison.Ordinal);
        Assert.Equal(["listar"], falso.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Un_cliente_o_un_encargado_no_abren_la_lista_de_logistica()
    {
        var falso = new EnviosDeLogisticaFalso();
        await using var anfitrion = await IniciarLogistica(falso, RolWeb.Cliente);

        var (estado, _) = await Pedir(anfitrion, "/logistica/envios", HostWeb.Gestion);

        Assert.NotEqual(HttpStatusCode.OK, estado);
        Assert.Empty(falso.Llamadas);
    }

    // ---------- Logística: los fallos de una acción ----------

    private static async Task<(HttpStatusCode Estado, string Html)> Operar(AnfitrionWeb anfitrion, string accion)
    {
        using var solicitud = await AnfitrionConSesion.Post(anfitrion, $"/logistica/envios/{Guid.NewGuid():D}/{accion}", AnfitrionConSesion.Cookie(HostWeb.Gestion));
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        solicitud.Content = new FormUrlEncodedContent([]);
        using var respuesta = await anfitrion.Cliente.SendAsync(solicitud);
        var cuerpo = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
        return (respuesta.StatusCode, cuerpo);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-09")]
    [InlineData("nombre", "Falta el nombre del destinatario")]
    [InlineData("direccion", "Falta la dirección de entrega")]
    [InlineData("telefono", "Falta el teléfono del destinatario")]
    [InlineData("anonimizado", "El contacto del pedido se anonimizó")]
    public async Task El_contacto_incompleto_se_explica_por_campo_sin_ofrecer_reintentar_ni_mostrar_valores(string campo, string texto)
    {
        var falso = new EnviosDeLogisticaFalso { RespuestaDeOperacion = Resultado<DetalleDeEnvio>.Fallo(new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, "contacto-incompleto", [new ErrorDeCampo(campo, "faltante")])) };
        await using var anfitrion = await IniciarLogistica(falso);

        var (estado, html) = await Operar(anfitrion, "guia");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, estado);
        Assert.Contains(texto, html, StringComparison.Ordinal);
        Assert.Contains("ECO-ENV-CONTACTO-INCOMPLETO", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Reintentar", html, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-11")]
    [InlineData("guia", CategoriaDeError.NoDisponible, "operador-no-disponible", HttpStatusCode.ServiceUnavailable, "El operador logístico no respondió")]
    [InlineData("reparto", CategoriaDeError.Conflicto, "inventario-rechazo", HttpStatusCode.Conflict, "El Inventario rechazó el descuento")]
    [InlineData("guia", CategoriaDeError.Conflicto, "guia-en-curso", HttpStatusCode.Conflict, "La guía ya se está pidiendo")]
    [InlineData("reparto", CategoriaDeError.Conflicto, "estado-invalido", HttpStatusCode.Conflict, "El envío ya cambió de estado")]
    public async Task Los_fallos_reintentables_se_pintan_con_su_texto_y_el_enlace_para_volver_a_la_lista(string accion, CategoriaDeError categoria, string clave, HttpStatusCode codigo, string texto)
    {
        var falso = new EnviosDeLogisticaFalso { RespuestaDeOperacion = Resultado<DetalleDeEnvio>.Fallo(new ErrorDeAplicacion(categoria, clave)) };
        await using var anfitrion = await IniciarLogistica(falso);

        var (estado, html) = await Operar(anfitrion, accion);

        Assert.Equal(codigo, estado);
        Assert.Contains(texto, html, StringComparison.Ordinal);
        Assert.Contains("href=\"/logistica/envios\"", html, StringComparison.Ordinal);
    }

    // ---------- El titular: bloque de envío del pedido ----------

    private static async Task<AnfitrionWeb> IniciarTitular(EnviosDelClienteFalso falso) =>
        await AnfitrionConSesion.Iniciar(
            new SesionesWebDePrueba().ConSesionValida(RolWeb.Cliente, null),
            HostWeb.Tienda,
            servicios: s =>
            {
                s.AddSingleton<IEnviosDelCliente>(falso);
                s.AddSingleton<IListaDeSucursales>(new ListaVacia());
            });

    private sealed class ListaVacia : IListaDeSucursales
    {
        public Task<ListaDeSucursales?> Obtener(CancellationToken cancelacion = default) => Task.FromResult<ListaDeSucursales?>(new ListaDeSucursales([], true));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-05")]
    public async Task El_bloque_del_titular_dice_el_estado_con_texto_e_icono_la_guia_y_la_fecha_de_entrega()
    {
        var pedido = Guid.NewGuid();
        var falso = new EnviosDelClienteFalso
        {
            Respuesta = Resultado<EnviosDeMiPedido>.Exito(new EnviosDeMiPedido(pedido,
            [
                new EnvioDeMiPedido(Guid.NewGuid(), "lima-central", EstadoDelEnvio.PendienteDeGuia, null, null, null),
                new EnvioDeMiPedido(Guid.NewGuid(), "arequipa", EstadoDelEnvio.GuiaGenerada, "CA-882013", "operador-a", null),
                new EnvioDeMiPedido(Guid.NewGuid(), "cusco", EstadoDelEnvio.EnReparto, "CA-882014", "operador-a", null),
                new EnvioDeMiPedido(Guid.NewGuid(), "piura", EstadoDelEnvio.Entregado, "CA-882015", "operador-a", Creado),
                new EnvioDeMiPedido(Guid.NewGuid(), "tacna", EstadoDelEnvio.Cancelado, null, null, null),
            ])),
        };
        await using var anfitrion = await IniciarTitular(falso);

        var (estado, html) = await Pedir(anfitrion, $"/cuenta/pedidos/{pedido:D}/envios", HostWeb.Tienda);

        Assert.Equal(HttpStatusCode.OK, estado);
        foreach (var texto in new[] { "Pendiente de guía", "Guía generada", "En reparto", "Entregado el 20/11/2026 10:00", "Cancelado", "CA-882013", "operador-a", "Seguimiento de tu pedido" })
        {
            Assert.Contains(texto, html, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-05")]
    public async Task Sin_envios_creados_se_dice_pendiente_de_guia_y_con_htmx_solo_viaja_el_bloque()
    {
        var falso = new EnviosDelClienteFalso();
        await using var anfitrion = await IniciarTitular(falso);
        var ruta = $"/cuenta/pedidos/{Guid.NewGuid():D}/envios";

        var (_, pagina) = await Pedir(anfitrion, ruta, HostWeb.Tienda);
        var (estado, fragmento) = await Pedir(anfitrion, ruta, HostWeb.Tienda, htmx: true);

        Assert.Contains("Pendiente de guía", pagina, StringComparison.Ordinal);
        Assert.Contains("<html", pagina, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("Pendiente de guía", fragmento, StringComparison.Ordinal);
        Assert.DoesNotContain("<html", fragmento, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-05")]
    public async Task Un_pedido_ajeno_o_inexistente_responde_igual_404_y_con_htmx_se_pinta_dentro_del_bloque()
    {
        var falso = new EnviosDelClienteFalso { Respuesta = Resultado<EnviosDeMiPedido>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "pedido-no-encontrado")) };
        await using var anfitrion = await IniciarTitular(falso);
        var ruta = $"/cuenta/pedidos/{Guid.NewGuid():D}/envios";

        var (estado, pagina) = await Pedir(anfitrion, ruta, HostWeb.Tienda);
        var (estadoHtmx, fragmento) = await Pedir(anfitrion, ruta, HostWeb.Tienda, htmx: true);

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Contains("ECO-NO-ENCONTRADO", pagina, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, estadoHtmx);
        Assert.Contains("ECO-NO-ENCONTRADO", fragmento, StringComparison.Ordinal);
        Assert.DoesNotContain("<html", fragmento, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-05")]
    [InlineData(CategoriaDeError.NoDisponible, "servicio-no-disponible", HttpStatusCode.ServiceUnavailable)]
    [InlineData(CategoriaDeError.Limite, "limite-excedido", HttpStatusCode.TooManyRequests)]
    public async Task Un_servicio_caido_o_un_limite_se_pintan_como_estado_estandar_del_bloque(CategoriaDeError categoria, string clave, HttpStatusCode codigo)
    {
        var falso = new EnviosDelClienteFalso { Respuesta = Resultado<EnviosDeMiPedido>.Fallo(new ErrorDeAplicacion(categoria, clave)) };
        await using var anfitrion = await IniciarTitular(falso);

        var (estado, html) = await Pedir(anfitrion, $"/cuenta/pedidos/{Guid.NewGuid():D}/envios", HostWeb.Tienda);

        Assert.Equal(codigo, estado);
        Assert.Contains("Envío", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-05")]
    public async Task Un_visitante_sin_sesion_va_al_ingreso_sin_llamar_a_la_Aplicacion()
    {
        var falso = new EnviosDelClienteFalso();
        await using var anfitrion = await AnfitrionConSesion.Iniciar(new SesionesWebDePrueba(), HostWeb.Tienda, servicios: s =>
        {
            s.AddSingleton<IEnviosDelCliente>(falso);
            s.AddSingleton<IListaDeSucursales>(new ListaVacia());
        });
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, $"/cuenta/pedidos/{Guid.NewGuid():D}/envios");
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");

        using var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.StartsWith("/ingreso", respuesta.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }
}
