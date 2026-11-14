using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>La purga de sesiones y borradores (T13, WEB-27, WEB-28, D05): revoca la renovación antes de borrar, reintenta, no se arrastra con una dependencia caída y alerta si persiste.</summary>
public sealed class PurgarSesionesWebTests
{
    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Una_sesion_vencida_por_inactividad_que_nadie_uso_se_cierra_se_borra_su_credencial_y_se_revoca_su_renovacion()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(renovacion: "renov-1");
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(31);

        var resultado = await d.Purgar.Ejecutar();

        var fila = d.Fila(cookie);
        Assert.Equal(1, resultado.Cerradas);
        Assert.Equal(EstadoDeSesion.Vencida, fila.Sesion.Estado);
        Assert.Equal(MotivoDeCierre.VencidaPorInactividad, fila.Sesion.MotivoDeCierre);
        Assert.Null(fila.CredencialCifrada);
        Assert.Null(fila.RenovacionCifrada);
        Assert.Equal(["renov-1"], d.Identidad.Revocadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task Una_sesion_que_paso_su_tope_absoluto_se_cierra_con_ese_motivo_aunque_la_actividad_sea_reciente()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(HostWeb.Gestion, RolWeb.Encargado);
        d.Fila(cookie).Sesion.RegistrarAccionDelUsuario(DoblesDeSesion.Inicio.AddHours(7).AddMinutes(50));
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddHours(8).AddMinutes(1);

        await d.PurgarDe(HostDeSesion.Gestion).Ejecutar();

        Assert.Equal(MotivoDeCierre.VencidaPorTopeAbsoluto, d.Fila(cookie).Sesion.MotivoDeCierre);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task La_purga_de_un_host_nunca_toca_las_filas_del_otro_cuya_clave_no_tiene()
    {
        var d = new DoblesDeSesion();
        var deLaTienda = await d.Abrir(HostWeb.Tienda);
        var deLaGestion = await d.Abrir(HostWeb.Gestion, RolWeb.Encargado);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(31);

        var resultado = await d.PurgarDe(HostDeSesion.Gestion).Ejecutar();

        Assert.Equal(1, resultado.Cerradas);
        Assert.Equal(EstadoDeSesion.Abierta, d.Fila(deLaTienda).Sesion.Estado);
        Assert.NotNull(d.Fila(deLaTienda).RenovacionCifrada);
        Assert.Equal(EstadoDeSesion.Vencida, d.Fila(deLaGestion).Sesion.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Veinticinco_revocaciones_imposibles_no_dejan_sin_turno_a_las_sesiones_vencidas_ni_a_los_borrados()
    {
        var d = new DoblesDeSesion();
        d.Identidad.ConfirmaRevocaciones = false;
        for (var i = 0; i < 25; i++)
        {
            await d.Cerrar.Cerrar(await d.Abrir(), default);
        }

        d.Identidad.ConfirmaRevocaciones = true;
        d.Identidad.LanzaAlRevocar = false;
        var vencida = await d.Abrir();
        d.Identidad.ConfirmaRevocaciones = false;
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(31);

        var resultado = await d.Purgar.Ejecutar();

        // Las cerradas con la revocación pendiente se reintentan al final y no impiden cerrar la vencida.
        Assert.Equal(1, resultado.Cerradas);
        Assert.Equal(EstadoDeSesion.Vencida, d.Fila(vencida).Sesion.Estado);
        Assert.Equal(26, d.Almacen.Filas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task Una_sesion_vigente_no_se_toca()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(29);

        var resultado = await d.Purgar.Ejecutar();

        Assert.Equal(0, resultado.Cambios);
        Assert.Equal(EstadoDeSesion.Abierta, d.Fila(cookie).Sesion.Estado);
        Assert.Equal(0, d.Identidad.LlamadasARevocar);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Una_fila_cerrada_se_conserva_30_minutos_para_el_borrador_y_despues_se_borra()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();
        await d.Cerrar.Cerrar(cookie, default);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(29);
        await d.Purgar.Ejecutar();
        Assert.True(d.Almacen.Filas.ContainsKey(ValorDeCookieDeSesion.Hash(cookie).Valor));

        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(30);
        var resultado = await d.Purgar.Ejecutar();

        Assert.Equal(1, resultado.SesionesBorradas);
        Assert.Empty(d.Almacen.Filas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Con_la_revocacion_pendiente_la_fila_no_se_borra_hasta_que_Identidad_confirme()
    {
        var d = new DoblesDeSesion();
        d.Identidad.ConfirmaRevocaciones = false;
        var cookie = await d.Abrir(renovacion: "renov-1");
        await d.Cerrar.Cerrar(cookie, default);
        Assert.True(d.Fila(cookie).RevocacionPendiente);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(40);

        var sinConfirmar = await d.Purgar.Ejecutar();

        Assert.Equal(1, sinConfirmar.RevocacionesPendientes);
        Assert.Equal(1, d.Fila(cookie).ReintentosDeRevocacion);
        Assert.NotNull(d.Fila(cookie).RenovacionCifrada);

        d.Identidad.ConfirmaRevocaciones = true;
        var confirmada = await d.Purgar.Ejecutar();

        Assert.Equal(1, confirmada.Revocadas);
        Assert.False(d.Fila(cookie).RevocacionPendiente);
        Assert.Null(d.Fila(cookie).RenovacionCifrada);

        // Solo cuando ya no queda nada por revocar, la fila (cerrada hace más de 30 minutos) se borra.
        var borrada = await d.Purgar.Ejecutar();
        Assert.Equal(1, borrada.SesionesBorradas);
        Assert.Empty(d.Almacen.Filas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Una_sesion_vencida_con_Identidad_caida_queda_con_la_revocacion_pendiente_y_solo_se_la_llama_una_vez_por_fase()
    {
        var d = new DoblesDeSesion();
        d.Identidad.LanzaAlRevocar = true;
        var cookies = new List<string> { await d.Abrir(), await d.Abrir(), await d.Abrir() };
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(31);

        var resultado = await d.Purgar.Ejecutar();

        Assert.Equal(3, resultado.Cerradas);
        // Una llamada que falla en cada fase (vencimientos y revocaciones pendientes) y ninguna más: la purga no se arrastra con una dependencia caída.
        Assert.Equal(2, d.Identidad.LlamadasARevocar);
        Assert.All(cookies, c =>
        {
            Assert.True(d.Fila(c).RevocacionPendiente);
            Assert.NotNull(d.Fila(c).RenovacionCifrada);
            Assert.Null(d.Fila(c).CredencialCifrada);
        });
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task La_revocacion_que_persiste_deja_una_alerta_tecnica_una_sola_vez()
    {
        var d = new DoblesDeSesion();
        d.Identidad.ConfirmaRevocaciones = false;
        var cookie = await d.Abrir();
        await d.Cerrar.Cerrar(cookie, default);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(10);

        for (var corrida = 0; corrida < PurgarSesionesWeb.ReintentosParaAlertar + 3; corrida++)
        {
            await d.Purgar.Ejecutar();
        }

        var alertas = d.Alertas.Alertas.Where(a => a.Motivo == PurgarSesionesWeb.MotivoDeAlertaPorRevocacionPersistente).ToList();
        Assert.Single(alertas);
        Assert.Equal(PurgarSesionesWeb.ReintentosParaAlertar + 3, d.Fila(cookie).ReintentosDeRevocacion);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Si_la_renovacion_ya_vencio_con_la_sesion_se_descarta_sin_llamar_a_Identidad()
    {
        var d = new DoblesDeSesion();
        d.Identidad.ConfirmaRevocaciones = false;
        var cookie = await d.Abrir();
        await d.Cerrar.Cerrar(cookie, default);
        Assert.True(d.Fila(cookie).RevocacionPendiente);
        var llamadasAntes = d.Identidad.LlamadasARevocar;
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddDays(7).AddMinutes(1);

        var resultado = await d.Purgar.Ejecutar();

        Assert.Equal(1, resultado.Descartadas);
        Assert.Equal(llamadasAntes, d.Identidad.LlamadasARevocar);
        Assert.Null(d.Fila(cookie).RenovacionCifrada);
        Assert.False(d.Fila(cookie).RevocacionPendiente);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Los_borradores_vencidos_se_borran_y_los_vigentes_no()
    {
        var d = new DoblesDeSesion();
        var vencido = BorradorDePantalla.Crear(Guid.NewGuid(), DoblesDeSesion.Usuario, "/cuenta/datos", new Dictionary<string, string> { ["nombre"] = "Ana" }, DoblesDeSesion.Inicio);
        var vigente = BorradorDePantalla.Crear(Guid.NewGuid(), DoblesDeSesion.Usuario, "/checkout/datos", new Dictionary<string, string> { ["nombre"] = "Ana" }, DoblesDeSesion.Inicio.AddMinutes(20));
        await d.Borradores.Guardar(new BorradorGuardado(vencido, [1], 1));
        await d.Borradores.Guardar(new BorradorGuardado(vigente, [1], 1));
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(31);

        var resultado = await d.Purgar.Ejecutar();

        Assert.Equal(1, resultado.BorradoresBorrados);
        Assert.Equal(vigente.Id, Assert.Single(d.Borradores.Filas).Borrador.Id);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Un_secreto_ilegible_se_descarta_con_un_evento_y_no_detiene_la_purga()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(31);
        d.Cifrador.Falla = true;

        var resultado = await d.Purgar.Ejecutar();

        Assert.Equal(1, resultado.Cerradas);
        Assert.Null(d.Fila(cookie).RenovacionCifrada);
        Assert.False(d.Fila(cookie).RevocacionPendiente);
        Assert.Contains(d.Seguridad.Eventos, e => e.Motivo == MotivoDeDenegacionWeb.SecretoIlegible);
    }
}
