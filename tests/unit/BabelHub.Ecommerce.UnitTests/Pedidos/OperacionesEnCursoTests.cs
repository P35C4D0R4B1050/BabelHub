using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T05 (D03, D04, D21; AC-PED-09, AC-PED-17, AC-PED-19): las operaciones en curso (anulación y entrega) con sus pasos, su iniciador, su espera creciente y sus reintentos.</summary>
public sealed class OperacionesEnCursoTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Pedido = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Linea = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly IniciadorDeOperacion Admin = new(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "administracion-central", null);

    private static PlanDeAnulacion Plan(bool conDigital = false, decimal monto = 50m) =>
        new(OrigenDeLaAnulacion.Administracion, MotivoDeAnulacion.PagoDuplicado, [Linea], conDigital ? [Guid.NewGuid()] : [], false, monto, []);

    [Fact]
    public void La_clave_de_cierre_por_linea_es_determinista_corta_y_distinta_por_linea()
    {
        var otra = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        var uno = ClaveDeCierreDeLinea.Para(Pedido, Linea);
        var dos = ClaveDeCierreDeLinea.Para(Pedido, otra);

        Assert.Equal("cierre-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-bbbbbbbb", uno);
        Assert.NotEqual(uno, dos);
        Assert.Equal(uno, ClaveDeCierreDeLinea.Para(Pedido, Linea));
        Assert.Matches("^[A-Za-z0-9_-]{16,80}$", uno);
    }

    [Fact]
    public void Una_anulacion_nace_en_curso_con_su_iniciador_y_sus_lineas_y_marca_cada_paso_una_sola_vez()
    {
        var anulacion = Anulacion.Iniciar(Guid.NewGuid(), Pedido, Plan(conDigital: true), Admin, Inicio);

        Assert.Equal(EstadoDeOperacion.EnCurso, anulacion.Estado);
        Assert.Equal(Admin, anulacion.Iniciador);
        Assert.Equal(2, anulacion.LineaIds.Count);
        Assert.Equal(
            [PasoDeAnulacion.EnviosCancelados, PasoDeAnulacion.ReservasLiberadas, PasoDeAnulacion.ReembolsoRegistrado, PasoDeAnulacion.AccesosRevocados],
            anulacion.PasosPendientes);
        Assert.True(anulacion.MarcarPaso(PasoDeAnulacion.EnviosCancelados, Inicio.AddSeconds(1)));
        Assert.False(anulacion.MarcarPaso(PasoDeAnulacion.EnviosCancelados, Inicio.AddSeconds(9)));
        Assert.Equal(Inicio.AddSeconds(1), anulacion.PasoConfirmadoEn(PasoDeAnulacion.EnviosCancelados));
    }

    [Fact]
    public void Una_anulacion_sin_digitales_no_pide_revocar_accesos_y_solo_se_completa_con_todos_sus_pasos()
    {
        var anulacion = Anulacion.Iniciar(Guid.NewGuid(), Pedido, Plan(), Admin, Inicio);

        Assert.DoesNotContain(PasoDeAnulacion.AccesosRevocados, anulacion.PasosPendientes);
        Assert.False(anulacion.Completar(Inicio.AddSeconds(5)));
        anulacion.MarcarPaso(PasoDeAnulacion.EnviosCancelados, Inicio);
        anulacion.MarcarPaso(PasoDeAnulacion.ReservasLiberadas, Inicio);
        Assert.False(anulacion.Completar(Inicio.AddSeconds(5)));
        anulacion.RegistrarReembolso(Guid.NewGuid(), Inicio);
        Assert.True(anulacion.Completar(Inicio.AddSeconds(6)));
        Assert.Equal(EstadoDeOperacion.Completada, anulacion.Estado);
        Assert.Equal(Inicio.AddSeconds(6), anulacion.CompletadaEn);
        Assert.False(anulacion.Completar(Inicio.AddSeconds(7)));
    }

    [Fact]
    public void Un_reembolso_ya_registrado_no_se_cambia()
    {
        var anulacion = Anulacion.Iniciar(Guid.NewGuid(), Pedido, Plan(), Admin, Inicio);
        var primero = Guid.NewGuid();

        Assert.True(anulacion.RegistrarReembolso(primero, Inicio));
        Assert.False(anulacion.RegistrarReembolso(Guid.NewGuid(), Inicio.AddSeconds(1)));

        Assert.Equal(primero, anulacion.ReembolsoId);
    }

    [Fact]
    public void La_espera_entre_reintentos_crece_y_pasados_15_minutos_la_operacion_queda_fallida()
    {
        var anulacion = Anulacion.Iniciar(Guid.NewGuid(), Pedido, Plan(), Admin, Inicio);

        anulacion.RegistrarIntento(Inicio.AddMinutes(1));
        var primera = anulacion.ProximoIntentoEn!.Value - Inicio.AddMinutes(1);
        anulacion.RegistrarIntento(Inicio.AddMinutes(2));
        var segunda = anulacion.ProximoIntentoEn!.Value - Inicio.AddMinutes(2);

        Assert.Equal(2, anulacion.Intentos);
        Assert.True(segunda > primera);
        Assert.Equal(EstadoDeOperacion.EnCurso, anulacion.Estado);
        anulacion.RegistrarIntento(Inicio.AddMinutes(16));
        Assert.Equal(EstadoDeOperacion.Fallida, anulacion.Estado);
        Assert.False(anulacion.BloqueaLasLineas);
    }

    [Fact]
    public void La_espera_nunca_pasa_del_tope()
    {
        Assert.True(EsperaDeReintentos.Para(1) < EsperaDeReintentos.Para(2));
        Assert.Equal(EsperaDeReintentos.Para(10), EsperaDeReintentos.Para(50));
        Assert.True(EsperaDeReintentos.Para(50) <= TimeSpan.FromMinutes(8));
    }

    [Fact]
    public void Una_anulacion_en_curso_bloquea_sus_lineas_y_una_descartada_o_fallida_no()
    {
        var anulacion = Anulacion.Iniciar(Guid.NewGuid(), Pedido, Plan(), Admin, Inicio);
        Assert.True(anulacion.BloqueaLasLineas);

        anulacion.Descartar(Inicio.AddSeconds(30));

        Assert.Equal(EstadoDeOperacion.Descartada, anulacion.Estado);
        Assert.False(anulacion.BloqueaLasLineas);
    }

    [Fact]
    public void Tras_un_conflicto_la_anulacion_ajusta_sus_lineas_el_envio_y_el_monto_y_sin_lineas_se_descarta()
    {
        var anulacion = Anulacion.Iniciar(Guid.NewGuid(), Pedido, Plan(conDigital: true, monto: 82m) with { IncluyeEnvio = true }, Admin, Inicio);

        anulacion.AjustarAlPlan(Plan(monto: 50m), Inicio.AddSeconds(10));

        Assert.False(anulacion.IncluyeEnvio);
        Assert.Equal(50m, anulacion.Monto);
        Assert.Single(anulacion.LineaIds);
        Assert.DoesNotContain(PasoDeAnulacion.AccesosRevocados, anulacion.PasosPendientes);

        anulacion.AjustarAlPlan(null, Inicio.AddSeconds(20));
        Assert.Equal(EstadoDeOperacion.Descartada, anulacion.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-09")]
    public void Una_entrega_lleva_una_clave_de_cierre_por_linea_y_marca_cada_cierre_confirmado()
    {
        var otra = Guid.NewGuid();
        var entrega = Entrega.Iniciar(Guid.NewGuid(), Pedido, [Linea, otra], Admin, Inicio);

        Assert.Equal(EstadoDeOperacion.EnCurso, entrega.Estado);
        Assert.Equal([ClaveDeCierreDeLinea.Para(Pedido, Linea), ClaveDeCierreDeLinea.Para(Pedido, otra)], entrega.Lineas.Select(l => l.ClaveDeCierre));
        Assert.Equal([Linea, otra], entrega.LineasPendientesDeCierre);
        Assert.True(entrega.ConfirmarCierre(Linea, ResultadoDelCierre.Cerrada, Inicio.AddSeconds(2)));
        Assert.False(entrega.ConfirmarCierre(Linea, ResultadoDelCierre.YaCerrada, Inicio.AddSeconds(3)));
        Assert.Equal([otra], entrega.LineasPendientesDeCierre);
        Assert.Equal([Linea], entrega.LineasCerradas);
        Assert.Equal(ResultadoDelCierre.Cerrada, entrega.Lineas[0].ResultadoDelCierre);
        Assert.False(entrega.ConfirmarCierre(Guid.NewGuid(), ResultadoDelCierre.Cerrada, Inicio));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-17")]
    public void Un_cierre_que_ya_salio_por_Envios_deja_la_linea_fuera_de_la_entrega()
    {
        var entrega = Entrega.Iniciar(Guid.NewGuid(), Pedido, [Linea], Admin, Inicio);

        Assert.True(entrega.ConfirmarCierre(Linea, ResultadoDelCierre.YaSalio, Inicio.AddSeconds(1)));

        Assert.Empty(entrega.LineasCerradas);
        Assert.Empty(entrega.LineasPendientesDeCierre);
        Assert.Equal([Linea], entrega.LineasQueSalieron);
    }

    [Fact]
    public void Una_entrega_sin_cierre_confirmado_pasada_la_espera_corta_queda_fallida_y_con_un_cierre_no()
    {
        var sinCierre = Entrega.Iniciar(Guid.NewGuid(), Pedido, [Linea], Admin, Inicio);
        var conCierre = Entrega.Iniciar(Guid.NewGuid(), Pedido, [Linea], Admin, Inicio);
        conCierre.ConfirmarCierre(Linea, ResultadoDelCierre.Cerrada, Inicio.AddSeconds(5));

        Assert.False(sinCierre.DebeFallarPorFaltaDeCierre(Inicio.AddMinutes(1)));
        Assert.True(sinCierre.DebeFallarPorFaltaDeCierre(Inicio.AddMinutes(2).AddSeconds(1)));
        Assert.False(conCierre.DebeFallarPorFaltaDeCierre(Inicio.AddMinutes(10)));

        sinCierre.Fallar(Inicio.AddMinutes(3));
        Assert.Equal(EstadoDeOperacion.Fallida, sinCierre.Estado);
        Assert.False(sinCierre.BloqueaLasLineas);
    }

    [Fact]
    public void Una_entrega_se_completa_solo_con_todos_los_cierres_resueltos_y_una_vez()
    {
        var otra = Guid.NewGuid();
        var entrega = Entrega.Iniciar(Guid.NewGuid(), Pedido, [Linea, otra], Admin, Inicio);
        entrega.ConfirmarCierre(Linea, ResultadoDelCierre.Cerrada, Inicio);

        Assert.False(entrega.Completar(Inicio.AddSeconds(5)));
        entrega.ConfirmarCierre(otra, ResultadoDelCierre.YaCerrada, Inicio);
        Assert.True(entrega.Completar(Inicio.AddSeconds(6)));
        Assert.False(entrega.Completar(Inicio.AddSeconds(7)));
        Assert.Equal(EstadoDeOperacion.Completada, entrega.Estado);
    }

    [Fact]
    public void El_iniciador_lleva_identificador_rol_y_sucursal_y_se_anonimiza_una_vez()
    {
        var sucursal = Guid.NewGuid();
        var encargado = new IniciadorDeOperacion(Guid.NewGuid(), "encargado-de-sucursal", sucursal);

        var anonimo = encargado.Anonimizar();

        Assert.Equal((Guid.Empty, null, null), (anonimo.UsuarioId, anonimo.Rol, anonimo.SucursalId));
        Assert.True(anonimo.EstaAnonimizado);
        Assert.False(encargado.EstaAnonimizado);
        Assert.Equal(anonimo, anonimo.Anonimizar());
    }
}
