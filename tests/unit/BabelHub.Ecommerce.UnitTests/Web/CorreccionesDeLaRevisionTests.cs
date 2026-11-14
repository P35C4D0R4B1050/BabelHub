using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>Lo que dejó la revisión de WEB-1a: la política de la sesión rige también al asegurar la credencial y cargar no pisa la credencial.</summary>
public sealed class CorreccionesDeLaRevisionTests
{
    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task Una_sesion_vencida_por_tope_no_renueva_credencial_aunque_nadie_haya_llamado_a_cargar()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddDays(8);

        var resultado = await d.Asegurar.Ejecutar(cookie, HostWeb.Tienda, default);

        Assert.Equal(EstadoDeCredencial.SinSesion, resultado.Estado);
        Assert.Equal(0, d.Identidad.LlamadasARenovar);
        Assert.Equal(MotivoDeCierre.VencidaPorTopeAbsoluto, d.Fila(cookie).Sesion.MotivoDeCierre);
        Assert.Equal(["renov-1"], d.Identidad.Revocadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task Una_sesion_inactiva_mas_de_30_minutos_tampoco_renueva_al_asegurar_la_credencial()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(vence: 15);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(31);

        var resultado = await d.Asegurar.Ejecutar(cookie, HostWeb.Tienda, default);

        Assert.Equal(EstadoDeCredencial.SinSesion, resultado.Estado);
        Assert.Equal(0, d.Identidad.LlamadasARenovar);
        Assert.Equal(MotivoDeCierre.VencidaPorInactividad, d.Fila(cookie).Sesion.MotivoDeCierre);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Registrar_actividad_solo_actualiza_la_actividad_y_no_la_fila_completa()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();
        var credencialAntes = d.Fila(cookie).CredencialCifrada;
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(5);

        await d.Cargar.Ejecutar(cookie, HostWeb.Tienda, OrigenDeLaAccion.AccionDelUsuario, default);

        Assert.Equal(1, d.Almacen.ActualizacionesDeActividad);
        Assert.Same(credencialAntes, d.Fila(cookie).CredencialCifrada);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void La_solicitud_de_inicio_no_imprime_la_cookie_anterior_ni_las_credenciales()
    {
        var solicitud = new SolicitudDeInicioDeSesion(HostWeb.Tienda, DoblesDeSesion.Ingreso(credencial: "jwt-secreto", renovacion: "renov-secreta"), "COOKIE-VIVA-ANTERIOR", true);

        var texto = solicitud.ToString();

        Assert.DoesNotContain("COOKIE-VIVA", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", texto, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task Un_secreto_ilegible_de_la_credencial_igual_revoca_la_renovacion_que_se_puede_leer()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(renovacion: "renov-1");
        var fila = d.Fila(cookie);
        fila.CredencialCifrada = [1, 2, 3];

        var resultado = await d.Asegurar.Ejecutar(cookie, HostWeb.Tienda, default);

        Assert.Equal(EstadoDeCredencial.SesionCerrada, resultado.Estado);
        Assert.Equal(["renov-1"], d.Identidad.Revocadas);
        Assert.Null(d.Fila(cookie).RenovacionCifrada);
    }
}
