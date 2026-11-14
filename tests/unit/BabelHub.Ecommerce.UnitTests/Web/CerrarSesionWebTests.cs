using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>Cerrar una sesión o todas las de un usuario (T08, WEB-28).</summary>
public sealed class CerrarSesionWebTests
{
    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task El_cierre_voluntario_invalida_la_sesion_borra_la_credencial_y_revoca_la_renovacion()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(renovacion: "renov-1");

        await d.Cerrar.Cerrar(cookie, default);

        var fila = d.Fila(cookie);
        Assert.Equal(EstadoDeSesion.Cerrada, fila.Sesion.Estado);
        Assert.Equal(MotivoDeCierre.Voluntario, fila.Sesion.MotivoDeCierre);
        Assert.Null(fila.CredencialCifrada);
        Assert.Null(fila.RenovacionCifrada);
        Assert.Equal(["renov-1"], d.Identidad.Revocadas);
        Assert.Equal(EstadoDeCarga.Cerrada, (await d.Cargar.Ejecutar(cookie, HostWeb.Tienda, OrigenDeLaAccion.AccionDelUsuario, default)).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Con_Identidad_caida_la_sesion_web_igual_se_invalida_y_la_revocacion_queda_pendiente()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();
        d.Identidad.ConfirmaRevocaciones = false;

        await d.Cerrar.Cerrar(cookie, default);

        var fila = d.Fila(cookie);
        Assert.Equal(EstadoDeSesion.Cerrada, fila.Sesion.Estado);
        Assert.Null(fila.CredencialCifrada);
        Assert.NotNull(fila.RenovacionCifrada);
        Assert.True(fila.RevocacionPendiente);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Cerrar_es_idempotente_y_una_cookie_mala_o_desconocida_no_hace_nada()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();

        await d.Cerrar.Cerrar(cookie, default);
        d.Identidad.Revocadas.Clear();
        await d.Cerrar.Cerrar(cookie, default);
        await d.Cerrar.Cerrar("mala", default);
        await d.Cerrar.Cerrar(new string('Q', 43), default);

        Assert.Empty(d.Identidad.Revocadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Un_cierre_voluntario_nunca_conserva_el_borrador()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();

        await d.Cerrar.Cerrar(cookie, default);

        Assert.False(d.Fila(cookie).Sesion.MotivoDeCierre!.Value.ConservaElBorrador());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Cerrar_todas_cierra_las_filas_del_usuario_y_llama_a_Identidad_en_una_sola_unidad_de_trabajo_sin_tocar_a_otros_usuarios()
    {
        var d = new DoblesDeSesion();
        var a = await d.Abrir(renovacion: "renov-a");
        var b = await d.Abrir(renovacion: "renov-b");
        var ajena = d.Almacen.Filas.Values.First().Sesion;
        var otroUsuario = SesionWeb.Abrir(ValorDeCookieDeSesion.Hash(new string('Y', 43)), Guid.NewGuid(), HostDeSesion.Tienda, false, DoblesDeSesion.Inicio);
        await d.Almacen.Insertar(new SesionGuardada { Sesion = otroUsuario }, default);
        var ejecucionesAntes = d.Unidad.Ejecuciones;

        await d.Cerrar.CerrarTodasDeUnUsuario(DoblesDeSesion.Usuario, default);

        Assert.Equal(EstadoDeSesion.Cerrada, d.Fila(a).Sesion.Estado);
        Assert.Equal(EstadoDeSesion.Cerrada, d.Fila(b).Sesion.Estado);
        Assert.All([d.Fila(a), d.Fila(b)], f =>
        {
            Assert.Equal(MotivoDeCierre.Voluntario, f.Sesion.MotivoDeCierre);
            Assert.Null(f.CredencialCifrada);
            Assert.Null(f.RenovacionCifrada);
            Assert.False(f.RevocacionPendiente);
        });
        Assert.Equal(EstadoDeSesion.Abierta, otroUsuario.Estado);
        Assert.Equal([DoblesDeSesion.Usuario], d.Identidad.CerradasTodas);
        Assert.Equal(ejecucionesAntes + 1, d.Unidad.Ejecuciones);
        Assert.NotNull(ajena);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Si_Identidad_no_cierra_todas_las_filas_web_quedan_cerradas_con_la_revocacion_pendiente_y_hay_una_alerta_tecnica()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();
        d.Identidad.CierraTodas = false;

        await d.Cerrar.CerrarTodasDeUnUsuario(DoblesDeSesion.Usuario, default);

        var fila = d.Fila(cookie);
        Assert.Equal(EstadoDeSesion.Cerrada, fila.Sesion.Estado);
        Assert.Null(fila.CredencialCifrada);
        Assert.True(fila.RevocacionPendiente);
        Assert.NotNull(fila.RenovacionCifrada);
        Assert.Equal("revocacion-de-sesiones-pendiente", Assert.Single(d.Alertas.Alertas).Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Cerrar_todas_de_un_usuario_sin_sesiones_no_alerta()
    {
        var d = new DoblesDeSesion();
        d.Identidad.CierraTodas = false;

        await d.Cerrar.CerrarTodasDeUnUsuario(Guid.NewGuid(), default);

        Assert.Empty(d.Alertas.Alertas);
    }
}
