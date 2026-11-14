using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>Conservar y recuperar lo escrito cuando la sesión venció (T09, WEB-25, D06).</summary>
public sealed class BorradoresDePantallaTests
{
    private const string Plantilla = "/reclamaciones/nueva";

    private static readonly IReadOnlyDictionary<string, string> Campos = new Dictionary<string, string> { ["detalle"] = "Mi reclamo", ["monto"] = "12,50" };

    private static (BorradoresDePantalla Borradores, AlmacenDeBorradoresEnMemoria Almacen) Crear(DoblesDeSesion d)
    {
        var almacen = new AlmacenDeBorradoresEnMemoria();
        return (new BorradoresDePantalla(d.Almacen, almacen, d.Cifrador, d.Seguridad, d.Unidad, d.Reloj), almacen);
    }

    private static async Task<string> SesionVencida(DoblesDeSesion d)
    {
        var cookie = await d.Abrir();
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(31);
        await d.Cargar.Ejecutar(cookie, HostWeb.Tienda, OrigenDeLaAccion.AccionDelUsuario, default);
        return cookie;
    }

    private static ConservarBorrador Entrada(string cookie, bool token = true, string plantilla = Plantilla, IReadOnlyDictionary<string, string>? campos = null) =>
        new(cookie, plantilla, campos ?? Campos, token);

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Al_vencer_la_sesion_lo_escrito_se_conserva_cifrado_y_se_recupera_al_volver_a_ingresar()
    {
        var d = new DoblesDeSesion();
        var (borradores, almacen) = Crear(d);
        var cookie = await SesionVencida(d);

        var guardado = await borradores.Conservar(Entrada(cookie));

        Assert.True(guardado);
        var fila = Assert.Single(almacen.Filas);
        Assert.Equal(DoblesDeSesion.Usuario, fila.Borrador.UsuarioId);
        Assert.Equal(Plantilla, fila.Borrador.Plantilla);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(40);
        var recuperado = await borradores.Recuperar(DoblesDeSesion.Usuario, Plantilla);
        Assert.Equal("Mi reclamo", recuperado!["detalle"]);
        Assert.Empty(almacen.Filas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Los_30_minutos_corren_desde_que_termino_la_sesion_y_no_desde_que_se_guarda()
    {
        var d = new DoblesDeSesion();
        var (borradores, almacen) = Crear(d);
        var cookie = await SesionVencida(d);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(50);

        Assert.True(await borradores.Conservar(Entrada(cookie)));

        Assert.Equal(DoblesDeSesion.Inicio.AddMinutes(61), Assert.Single(almacen.Filas).Borrador.VenceEn);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(62);
        Assert.Null(await borradores.Recuperar(DoblesDeSesion.Usuario, Plantilla));
        Assert.Empty(almacen.Filas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task La_entrega_es_unica_la_segunda_recuperacion_no_devuelve_nada()
    {
        var d = new DoblesDeSesion();
        var (borradores, _) = Crear(d);
        await borradores.Conservar(Entrada(await SesionVencida(d)));

        Assert.NotNull(await borradores.Recuperar(DoblesDeSesion.Usuario, Plantilla));
        Assert.Null(await borradores.Recuperar(DoblesDeSesion.Usuario, Plantilla));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Otra_persona_que_ingresa_en_el_mismo_equipo_no_ve_lo_escrito_y_no_lo_gasta()
    {
        var d = new DoblesDeSesion();
        var (borradores, almacen) = Crear(d);
        await borradores.Conservar(Entrada(await SesionVencida(d)));

        var ajeno = await borradores.Recuperar(Guid.NewGuid(), Plantilla);

        Assert.Null(ajeno);
        Assert.Single(almacen.Filas);
        Assert.NotNull(await borradores.Recuperar(DoblesDeSesion.Usuario, Plantilla));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Pasados_30_minutos_el_borrador_ya_no_se_entrega_y_se_borra()
    {
        var d = new DoblesDeSesion();
        var (borradores, almacen) = Crear(d);
        await borradores.Conservar(Entrada(await SesionVencida(d)));
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(31 + 31);

        Assert.Null(await borradores.Recuperar(DoblesDeSesion.Usuario, Plantilla));
        Assert.Empty(almacen.Filas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Tras_un_cierre_voluntario_nunca_se_conserva()
    {
        var d = new DoblesDeSesion();
        var (borradores, almacen) = Crear(d);
        var cookie = await d.Abrir();
        await d.Cerrar.Cerrar(cookie, default);

        Assert.False(await borradores.Conservar(Entrada(cookie)));
        Assert.Empty(almacen.Filas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Tras_un_rechazo_de_la_renovacion_si_se_conserva()
    {
        var d = new DoblesDeSesion();
        var (borradores, almacen) = Crear(d);
        var cookie = await d.Abrir(vence: 1);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(5);
        d.Identidad.Renovaciones.Enqueue(new ResultadoDeRenovacion.Rechazada());
        await d.Asegurar.Ejecutar(cookie, HostWeb.Tienda, default);

        Assert.True(await borradores.Conservar(Entrada(cookie)));
        Assert.Single(almacen.Filas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Sin_un_token_antifalsificacion_valido_para_la_sesion_no_se_conserva_nada()
    {
        var d = new DoblesDeSesion();
        var (borradores, almacen) = Crear(d);
        var cookie = await SesionVencida(d);

        Assert.False(await borradores.Conservar(Entrada(cookie, token: false)));
        Assert.Empty(almacen.Filas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Una_sesion_abierta_desconocida_o_con_cookie_mala_no_conserva_nada()
    {
        var d = new DoblesDeSesion();
        var (borradores, almacen) = Crear(d);
        var abierta = await d.Abrir();

        Assert.False(await borradores.Conservar(Entrada(abierta)));
        Assert.False(await borradores.Conservar(Entrada(new string('Q', 43))));
        Assert.False(await borradores.Conservar(Entrada("mala")));
        Assert.Empty(almacen.Filas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Una_sesion_vencida_hace_mas_de_30_minutos_ya_no_puede_plantar_un_borrador()
    {
        var d = new DoblesDeSesion();
        var (borradores, almacen) = Crear(d);
        var cookie = await SesionVencida(d);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(31 + 31);

        Assert.False(await borradores.Conservar(Entrada(cookie)));
        Assert.Empty(almacen.Filas);
    }

    [Theory]
    [InlineData("/ingreso")]
    [InlineData("/admin/usuarios")]
    [InlineData("/reclamaciones/nueva?x=1")]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Una_plantilla_fuera_de_la_lista_cerrada_no_se_conserva(string plantilla)
    {
        var d = new DoblesDeSesion();
        var (borradores, almacen) = Crear(d);

        Assert.False(await borradores.Conservar(Entrada(await SesionVencida(d), plantilla: plantilla)));
        Assert.Empty(almacen.Filas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Un_campo_de_contrasena_o_codigo_nunca_se_conserva_aunque_llegue_marcado()
    {
        var d = new DoblesDeSesion();
        var (borradores, almacen) = Crear(d);
        var campos = new Dictionary<string, string> { ["detalle"] = "x", ["contrasena"] = "secreta" };

        Assert.False(await borradores.Conservar(Entrada(await SesionVencida(d), campos: campos)));
        Assert.Empty(almacen.Filas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Guardar_de_nuevo_la_misma_plantilla_reemplaza_el_borrador_anterior()
    {
        var d = new DoblesDeSesion();
        var (borradores, almacen) = Crear(d);
        var cookie = await SesionVencida(d);

        await borradores.Conservar(Entrada(cookie, campos: new Dictionary<string, string> { ["detalle"] = "uno" }));
        await borradores.Conservar(Entrada(cookie, campos: new Dictionary<string, string> { ["detalle"] = "dos" }));

        Assert.Single(almacen.Filas);
        Assert.Equal("dos", (await borradores.Recuperar(DoblesDeSesion.Usuario, Plantilla))!["detalle"]);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task Un_cuerpo_que_no_se_puede_descifrar_se_borra_con_un_evento_y_no_falla_con_una_excepcion()
    {
        var d = new DoblesDeSesion();
        var (borradores, almacen) = Crear(d);
        await borradores.Conservar(Entrada(await SesionVencida(d)));
        d.Cifrador.Falla = true;

        var recuperado = await borradores.Recuperar(DoblesDeSesion.Usuario, Plantilla);

        Assert.Null(recuperado);
        Assert.Empty(almacen.Filas);
        Assert.Contains(d.Seguridad.Eventos, e => e.Motivo == MotivoDeDenegacionWeb.SecretoIlegible);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public void El_borrador_se_guarda_en_una_sola_unidad_de_trabajo_y_no_imprime_los_campos()
    {
        Assert.DoesNotContain("Mi reclamo", Entrada("x").ToString(), StringComparison.Ordinal);
    }
}
