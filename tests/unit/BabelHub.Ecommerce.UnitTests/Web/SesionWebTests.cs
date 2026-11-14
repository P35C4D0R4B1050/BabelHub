using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>La sesión web (T01, WEB-27, WEB-28, WEB-31): inactividad de 30 minutos que solo renuevan las acciones del usuario, tope absoluto y estados con su motivo de cierre.</summary>
public sealed class SesionWebTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 14, 15, 0, 0, TimeSpan.FromHours(-5));
    private static readonly Guid Usuario = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly HashDeSesion Id = HashDeSesion.De(new string('a', 64));

    private static SesionWeb Abrir(bool personal = false, HostDeSesion host = HostDeSesion.Tienda) =>
        SesionWeb.Abrir(Id, Usuario, host, personal, Inicio);

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public void Una_sesion_nueva_esta_abierta_con_su_tope_absoluto_de_7_dias_para_el_cliente_y_de_8_horas_para_el_personal()
    {
        var cliente = Abrir(personal: false);
        var personal = Abrir(personal: true, host: HostDeSesion.Gestion);

        Assert.Equal(EstadoDeSesion.Abierta, cliente.Estado);
        Assert.Equal(Inicio.AddDays(7), cliente.ExpiraAbsoluta);
        Assert.Equal(Inicio.AddHours(8), personal.ExpiraAbsoluta);
        Assert.Equal(Inicio, cliente.UltimaActividad);
    }

    [Theory]
    [InlineData(29, true)]
    [InlineData(30, false)]
    [InlineData(31, false)]
    [Trait("Criterio", "AC-WEB-22")]
    public void Pasados_30_minutos_sin_acciones_del_usuario_la_sesion_deja_de_ser_valida(int minutos, bool vigente)
    {
        var sesion = Abrir();

        var evaluacion = sesion.Evaluar(Inicio.AddMinutes(minutos));

        Assert.Equal(vigente, evaluacion == EvaluacionDeSesion.Vigente);
        if (!vigente)
        {
            Assert.Equal(EvaluacionDeSesion.VencidaPorInactividad, evaluacion);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public void Una_accion_del_usuario_renueva_la_inactividad_y_el_cliente_que_actua_cada_20_minutos_sigue_hasta_el_tope_de_7_dias()
    {
        var sesion = Abrir();
        var ahora = Inicio;

        while (ahora.AddMinutes(20) < Inicio.AddDays(7))
        {
            ahora = ahora.AddMinutes(20);
            Assert.Equal(EvaluacionDeSesion.Vigente, sesion.Evaluar(ahora));
            sesion.RegistrarAccionDelUsuario(ahora);
        }

        Assert.Equal(EvaluacionDeSesion.VencidaPorTopeAbsoluto, sesion.Evaluar(Inicio.AddDays(7)));
        Assert.Equal(EvaluacionDeSesion.VencidaPorTopeAbsoluto, sesion.Evaluar(Inicio.AddDays(7).AddMinutes(5)));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public void El_personal_es_llevado_al_ingreso_a_las_8_horas_aunque_haya_actividad()
    {
        var sesion = Abrir(personal: true, host: HostDeSesion.Gestion);
        for (var minuto = 10; minuto < 8 * 60; minuto += 10)
        {
            sesion.RegistrarAccionDelUsuario(Inicio.AddMinutes(minuto));
        }

        Assert.Equal(EvaluacionDeSesion.Vigente, sesion.Evaluar(Inicio.AddHours(7).AddMinutes(55)));
        Assert.Equal(EvaluacionDeSesion.VencidaPorTopeAbsoluto, sesion.Evaluar(Inicio.AddHours(8)));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public void El_tope_absoluto_gana_cuando_vencen_los_dos_plazos_a_la_vez()
    {
        var sesion = Abrir(personal: true, host: HostDeSesion.Gestion);

        Assert.Equal(EvaluacionDeSesion.VencidaPorTopeAbsoluto, sesion.Evaluar(Inicio.AddHours(9)));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public void La_actividad_solo_se_vuelve_a_escribir_pasado_un_minuto_y_la_primera_accion_es_siempre_valida()
    {
        var sesion = Abrir();

        Assert.False(sesion.RegistrarAccionDelUsuario(Inicio.AddSeconds(30)));
        Assert.Equal(Inicio, sesion.UltimaActividad);
        Assert.True(sesion.RegistrarAccionDelUsuario(Inicio.AddSeconds(61)));
        Assert.Equal(Inicio.AddSeconds(61), sesion.UltimaActividad);
        Assert.False(sesion.RegistrarAccionDelUsuario(Inicio.AddSeconds(100)));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public void Una_accion_sobre_una_sesion_vencida_no_la_revive()
    {
        var sesion = Abrir();

        var resultado = sesion.RegistrarAccionDelUsuario(Inicio.AddMinutes(45));

        Assert.False(resultado);
        Assert.Equal(Inicio, sesion.UltimaActividad);
        Assert.Equal(EvaluacionDeSesion.VencidaPorInactividad, sesion.Evaluar(Inicio.AddMinutes(45)));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public void Cerrar_o_vencer_deja_el_motivo_y_una_sesion_cerrada_nunca_vuelve_a_ser_valida()
    {
        var voluntaria = Abrir();
        voluntaria.Cerrar(MotivoDeCierre.Voluntario, Inicio.AddMinutes(5));
        var vencida = Abrir();
        vencida.Cerrar(MotivoDeCierre.VencidaPorInactividad, Inicio.AddMinutes(31));

        Assert.Equal(EstadoDeSesion.Cerrada, voluntaria.Estado);
        Assert.Equal(MotivoDeCierre.Voluntario, voluntaria.MotivoDeCierre);
        Assert.Equal(Inicio.AddMinutes(5), voluntaria.CerradaEn);
        Assert.Equal(EvaluacionDeSesion.Cerrada, voluntaria.Evaluar(Inicio.AddMinutes(6)));
        Assert.False(voluntaria.RegistrarAccionDelUsuario(Inicio.AddMinutes(7)));
        Assert.Equal(MotivoDeCierre.VencidaPorInactividad, vencida.MotivoDeCierre);
        Assert.Equal(EstadoDeSesion.Vencida, vencida.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public void Cerrar_dos_veces_conserva_el_primer_cierre()
    {
        var sesion = Abrir();
        sesion.Cerrar(MotivoDeCierre.RenovacionRechazada, Inicio.AddMinutes(1));

        sesion.Cerrar(MotivoDeCierre.Voluntario, Inicio.AddMinutes(2));

        Assert.Equal(MotivoDeCierre.RenovacionRechazada, sesion.MotivoDeCierre);
        Assert.Equal(Inicio.AddMinutes(1), sesion.CerradaEn);
    }

    [Theory]
    [InlineData(MotivoDeCierre.Voluntario, false)]
    [InlineData(MotivoDeCierre.CerradaPorRevocacion, false)]
    [InlineData(MotivoDeCierre.VencidaPorInactividad, true)]
    [InlineData(MotivoDeCierre.VencidaPorTopeAbsoluto, true)]
    [InlineData(MotivoDeCierre.RenovacionRechazada, true)]
    [Trait("Criterio", "AC-WEB-20")]
    public void Solo_un_vencimiento_o_un_rechazo_de_la_renovacion_conserva_lo_escrito_nunca_un_cierre_voluntario(MotivoDeCierre motivo, bool conservaElBorrador) =>
        Assert.Equal(conservaElBorrador, motivo.ConservaElBorrador());

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public void El_identificador_es_el_hash_de_64_caracteres_hexadecimales_y_nada_mas()
    {
        Assert.Equal(new string('a', 64), HashDeSesion.De(new string('a', 64)).Valor);
        Assert.Throws<ArgumentException>(() => HashDeSesion.De("corto"));
        Assert.Throws<ArgumentException>(() => HashDeSesion.De(new string('Z', 64)));
        Assert.Throws<ArgumentException>(() => HashDeSesion.De(new string('A', 64)));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public void Un_usuario_vacio_no_puede_tener_sesion_y_la_sesion_pertenece_a_su_host()
    {
        Assert.Throws<ArgumentException>(() => SesionWeb.Abrir(Id, Guid.Empty, HostDeSesion.Tienda, false, Inicio));
        Assert.Equal(HostDeSesion.Gestion, Abrir(personal: true, host: HostDeSesion.Gestion).Host);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public void Una_sesion_se_reconstruye_desde_lo_guardado_sin_perder_su_estado()
    {
        var original = Abrir();
        original.RegistrarAccionDelUsuario(Inicio.AddMinutes(10));
        original.Cerrar(MotivoDeCierre.VencidaPorInactividad, Inicio.AddMinutes(45));

        var copia = SesionWeb.Reconstruir(
            original.Id, original.UsuarioId, original.Host, original.Creada, original.UltimaActividad, original.ExpiraAbsoluta, original.Estado, original.MotivoDeCierre, original.CerradaEn);

        Assert.Equal(original.UltimaActividad, copia.UltimaActividad);
        Assert.Equal(EstadoDeSesion.Vencida, copia.Estado);
        Assert.Equal(MotivoDeCierre.VencidaPorInactividad, copia.MotivoDeCierre);
        Assert.Equal(original.ExpiraAbsoluta, copia.ExpiraAbsoluta);
    }
}
