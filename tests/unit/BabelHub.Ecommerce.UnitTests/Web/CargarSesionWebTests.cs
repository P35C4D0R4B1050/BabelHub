using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>Cargar la sesión de una cookie (T06, WEB-27, WEB-28, WEB-31).</summary>
public sealed class CargarSesionWebTests
{
    private static Task<ResultadoDeCarga> Cargar(DoblesDeSesion d, string? cookie, HostWeb host = HostWeb.Tienda, OrigenDeLaAccion origen = OrigenDeLaAccion.AccionDelUsuario) =>
        d.Cargar.Ejecutar(cookie, host, origen, default);

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Una_cookie_mal_formada_se_rechaza_sin_tocar_la_base()
    {
        var d = new DoblesDeSesion();
        await d.Abrir();

        foreach (var mala in new string?[] { null, "", "corta", new string('A', 44), new string('=', 43), "A!" + new string('A', 41) })
        {
            Assert.Equal(EstadoDeCarga.Inexistente, (await Cargar(d, mala)).Estado);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Una_cookie_desconocida_no_existe_y_una_valida_carga_al_usuario()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();

        Assert.Equal(EstadoDeCarga.Inexistente, (await Cargar(d, new string('Z', 43))).Estado);
        var valida = await Cargar(d, cookie);
        Assert.Equal(EstadoDeCarga.Valida, valida.Estado);
        Assert.Equal(DoblesDeSesion.Usuario, valida.UsuarioId);
    }

    [Theory]
    [InlineData(29, EstadoDeCarga.Valida)]
    [InlineData(31, EstadoDeCarga.VencidaPorInactividad)]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task Pasados_30_minutos_sin_acciones_la_sesion_vence(int minutos, EstadoDeCarga esperado)
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(minutos);

        Assert.Equal(esperado, (await Cargar(d, cookie)).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task Al_vencer_se_cierra_se_borra_la_credencial_se_revoca_la_renovacion_y_el_identificador_ya_no_vuelve_a_usarse()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(renovacion: "renov-1");
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(31);

        await Cargar(d, cookie);

        var fila = d.Fila(cookie);
        Assert.Equal(EstadoDeSesion.Vencida, fila.Sesion.Estado);
        Assert.Equal(MotivoDeCierre.VencidaPorInactividad, fila.Sesion.MotivoDeCierre);
        Assert.Null(fila.CredencialCifrada);
        Assert.Null(fila.RenovacionCifrada);
        Assert.Equal(["renov-1"], d.Identidad.Revocadas);

        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(32);
        Assert.Equal(EstadoDeCarga.Cerrada, (await Cargar(d, cookie)).Estado);
        Assert.Contains(d.Seguridad.Eventos, e => e.Motivo == MotivoDeDenegacionWeb.IdentificadorDeSesionCerrado);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Con_Identidad_caida_al_vencer_la_revocacion_queda_pendiente_y_la_renovacion_cifrada_se_conserva()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();
        d.Identidad.ConfirmaRevocaciones = false;
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(31);

        await Cargar(d, cookie);

        var fila = d.Fila(cookie);
        Assert.True(fila.RevocacionPendiente);
        Assert.NotNull(fila.RenovacionCifrada);
        Assert.Null(fila.CredencialCifrada);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task Una_consulta_automatica_no_renueva_la_inactividad_ni_revive_una_sesion_vencida()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();

        // Consultas automáticas cada 10 minutos: no cuentan como actividad de la persona.
        for (var minuto = 10; minuto <= 25; minuto += 5)
        {
            d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(minuto);
            Assert.Equal(EstadoDeCarga.Valida, (await Cargar(d, cookie, origen: OrigenDeLaAccion.ConsultaAutomatica)).Estado);
        }

        Assert.Equal(DoblesDeSesion.Inicio, d.Fila(cookie).Sesion.UltimaActividad);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(31);
        Assert.Equal(EstadoDeCarga.VencidaPorInactividad, (await Cargar(d, cookie, origen: OrigenDeLaAccion.ConsultaAutomatica)).Estado);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(32);
        Assert.Equal(EstadoDeCarga.Cerrada, (await Cargar(d, cookie, origen: OrigenDeLaAccion.AccionDelUsuario)).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task Una_accion_del_usuario_renueva_la_inactividad_y_se_escribe_como_maximo_una_vez_por_minuto()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();

        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddSeconds(30);
        await Cargar(d, cookie);
        Assert.Equal(DoblesDeSesion.Inicio, d.Fila(cookie).Sesion.UltimaActividad);

        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(20);
        await Cargar(d, cookie);
        Assert.Equal(DoblesDeSesion.Inicio.AddMinutes(20), d.Fila(cookie).Sesion.UltimaActividad);

        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(40);
        Assert.Equal(EstadoDeCarga.Valida, (await Cargar(d, cookie)).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task El_cliente_que_actua_cada_20_minutos_sigue_hasta_los_7_dias_y_ahi_se_cierra()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();
        var ahora = DoblesDeSesion.Inicio;

        while (ahora.AddMinutes(20) < DoblesDeSesion.Inicio.AddDays(7))
        {
            ahora = ahora.AddMinutes(20);
            d.Reloj.Ahora = ahora;
            Assert.Equal(EstadoDeCarga.Valida, (await Cargar(d, cookie)).Estado);
        }

        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddDays(7);
        Assert.Equal(EstadoDeCarga.VencidaPorTopeAbsoluto, (await Cargar(d, cookie)).Estado);
        Assert.Equal(MotivoDeCierre.VencidaPorTopeAbsoluto, d.Fila(cookie).Sesion.MotivoDeCierre);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task El_encargado_es_llevado_al_ingreso_a_las_8_horas_aunque_haya_actividad()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(HostWeb.Gestion, RolWeb.Encargado);

        for (var minuto = 20; minuto < 8 * 60; minuto += 20)
        {
            d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(minuto);
            Assert.Equal(EstadoDeCarga.Valida, (await Cargar(d, cookie, HostWeb.Gestion)).Estado);
        }

        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddHours(8);
        Assert.Equal(EstadoDeCarga.VencidaPorTopeAbsoluto, (await Cargar(d, cookie, HostWeb.Gestion)).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Una_sesion_de_otro_host_se_rechaza_con_un_evento_y_sigue_abierta_en_su_host()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(HostWeb.Tienda);

        var resultado = await Cargar(d, cookie, HostWeb.Gestion);

        Assert.Equal(EstadoDeCarga.OtroHost, resultado.Estado);
        Assert.Null(resultado.UsuarioId);
        Assert.Contains(d.Seguridad.Eventos, e => e.Motivo == MotivoDeDenegacionWeb.SesionDeOtroHost);
        Assert.Equal(EstadoDeSesion.Abierta, d.Fila(cookie).Sesion.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Cerrar_una_sesion_no_cierra_la_otra_del_mismo_usuario_y_el_identificador_cerrado_se_rechaza()
    {
        var d = new DoblesDeSesion();
        var a = await d.Abrir(renovacion: "renov-a");
        var b = await d.Abrir(renovacion: "renov-b");
        d.Fila(a).Sesion.Cerrar(MotivoDeCierre.Voluntario, DoblesDeSesion.Inicio);

        Assert.Equal(EstadoDeCarga.Cerrada, (await Cargar(d, a)).Estado);
        Assert.Equal(EstadoDeCarga.Valida, (await Cargar(d, b)).Estado);
    }
}
