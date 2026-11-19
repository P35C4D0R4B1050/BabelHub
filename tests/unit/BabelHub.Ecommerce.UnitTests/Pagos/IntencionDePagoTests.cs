using BabelHub.Ecommerce.Domain.Pagos;
using static BabelHub.Ecommerce.UnitTests.Pagos.IntencionesDePrueba;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>
/// La intención de pago: la instantánea inmutable, su creación y reemplazo, la reapertura, la cancelación y las condiciones para cobrar (T02; PAG-08, PAG-16, PAG-17, PAG-26, D21, D23).
/// El ciclo de aprobación, la desvinculación y el contacto están en <see cref="IntencionDePagoCicloDeVidaTests"/>.
/// </summary>
public sealed class IntencionDePagoTests
{
    [Fact]
    [Trait("Criterio", "AC-PAG-01")]
    public void La_instantanea_exige_que_el_total_sea_la_suma_de_las_lineas_mas_el_envio()
    {
        var instantanea = Mixta();

        Assert.Equal(Importe.De(140m), instantanea.Total);
        Assert.Equal(Importe.De(15m), instantanea.Envio);
        Assert.True(instantanea.TieneImpresos);
        Assert.Throws<ArgumentException>(() => InstantaneaDeIntencion.Crear(
            Cuenta, TipoDeEntrega.Domicilio, null, "AYA", new[] { Impresa(), Digital() }, Importe.De(15m), Importe.De(139.99m), "h", Texto(), Texto(), Texto(), Contacto()));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-01")]
    public void Una_linea_cuyo_importe_no_es_el_precio_por_la_cantidad_se_rechaza()
    {
        var mal = Impresa() with { Importe = Importe.De(99m) };

        Assert.Throws<ArgumentException>(() => InstantaneaDeIntencion.Crear(
            Cuenta, TipoDeEntrega.Recojo, "AYA-01", null, new[] { mal }, Importe.Cero, Importe.De(99m), "h", Texto(), Texto(), null, null));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-26")]
    public void La_entrega_a_domicilio_exige_contacto_y_region_y_las_demas_no_llevan_contacto()
    {
        Assert.Throws<ArgumentException>(() => InstantaneaDeIntencion.Crear(
            Cuenta, TipoDeEntrega.Domicilio, null, "AYA", new[] { Impresa() }, Importe.Cero, Importe.De(100m), "h", Texto(), Texto(), null, contacto: null));
        Assert.Throws<ArgumentException>(() => InstantaneaDeIntencion.Crear(
            Cuenta, TipoDeEntrega.Domicilio, null, regionDeEntrega: null, new[] { Impresa() }, Importe.Cero, Importe.De(100m), "h", Texto(), Texto(), null, Contacto()));
        Assert.Throws<ArgumentException>(() => InstantaneaDeIntencion.Crear(
            Cuenta, TipoDeEntrega.Recojo, "AYA-01", null, new[] { Impresa() }, Importe.Cero, Importe.De(100m), "h", Texto(), Texto(), null, Contacto()));
        Assert.Throws<ArgumentException>(() => InstantaneaDeIntencion.Crear(
            Cuenta, TipoDeEntrega.Recojo, codigoDeSucursalDeRecojo: null, null, new[] { Impresa() }, Importe.Cero, Importe.De(100m), "h", Texto(), Texto(), null, null));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-01")]
    public void Una_compra_con_impresos_necesita_entrega_y_una_solo_digital_no_la_tiene()
    {
        Assert.Throws<ArgumentException>(() => InstantaneaDeIntencion.Crear(
            Cuenta, TipoDeEntrega.SinEntrega, null, null, new[] { Impresa() }, Importe.Cero, Importe.De(100m), "h", Texto(), Texto(), null, null));
        Assert.Throws<ArgumentException>(() => InstantaneaDeIntencion.Crear(
            Cuenta, TipoDeEntrega.Recojo, "AYA-01", null, new[] { Digital() }, Importe.Cero, Importe.De(25m), "h", Texto(), Texto(), Texto(), null));
        Assert.False(SoloDigital().TieneImpresos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-01")]
    public void Una_instantanea_sin_lineas_sin_cuenta_o_sin_huella_se_rechaza()
    {
        Assert.Throws<ArgumentException>(() => InstantaneaDeIntencion.Crear(
            Cuenta, TipoDeEntrega.SinEntrega, null, null, Array.Empty<LineaDeIntencion>(), Importe.Cero, Importe.Cero, "h", Texto(), Texto(), null, null));
        Assert.Throws<ArgumentException>(() => InstantaneaDeIntencion.Crear(
            Guid.Empty, TipoDeEntrega.SinEntrega, null, null, new[] { Digital() }, Importe.Cero, Importe.De(25m), "h", Texto(), Texto(), Texto(), null));
        Assert.Throws<ArgumentException>(() => InstantaneaDeIntencion.Crear(
            Cuenta, TipoDeEntrega.SinEntrega, null, null, new[] { Digital() }, Importe.Cero, Importe.De(25m), " ", Texto(), Texto(), Texto(), null));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-26")]
    public void El_contacto_de_entrega_no_se_muestra_en_ningun_texto()
    {
        var contacto = Contacto();

        var texto = contacto.ToString() + " " + Mixta().ToString() + " " + Abierta().ToString();

        Assert.DoesNotContain("Ana", texto);
        Assert.DoesNotContain("987654321", texto);
        Assert.DoesNotContain("Olivos", texto);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-26")]
    public void Las_lineas_no_muestran_titulo_isbn_ni_edicion_en_su_texto()
    {
        var texto = Impresa().ToString() + " " + string.Join(' ', Abierta().Lineas) + " " + Mixta();

        Assert.DoesNotContain("ciudad", texto);
        Assert.DoesNotContain("9780306406157", texto);
        Assert.DoesNotContain("44444444", texto);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-01")]
    public void Una_linea_repetida_en_la_instantanea_se_rechaza()
    {
        Assert.Throws<ArgumentException>(() => InstantaneaDeIntencion.Crear(
            Cuenta, TipoDeEntrega.Recojo, "AYA-01", null, new[] { Impresa(), Impresa() }, Importe.Cero, Importe.De(200m), "h", Texto(), Texto(), null, null));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-22")]
    public void Una_intencion_desvinculada_no_se_reemplaza_ni_se_reabre_y_no_reconstruye_datos_personales()
    {
        var intencion = Abierta();
        intencion.Desvincular(Inicio.AddMinutes(1));

        Assert.Equal(ResultadoDeCrear.Cerrada, intencion.Reemplazar(Mixta("huella-b"), Inicio.AddMinutes(20), Inicio.AddMinutes(2)));
        Assert.Equal(ResultadoDeReapertura.Cerrada, intencion.Reabrir());

        Assert.Null(intencion.CuentaId);
        Assert.Null(intencion.Contacto);
        Assert.Null(intencion.Terminos);
        Assert.All(intencion.Lineas, l => Assert.Null(l.Titulo));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public void Reemplazar_con_la_instantanea_de_otra_cuenta_se_rechaza()
    {
        var intencion = Abierta();
        var deOtra = InstantaneaDeIntencion.Crear(
            Guid.NewGuid(), TipoDeEntrega.SinEntrega, null, null, new[] { Digital() }, Importe.Cero, Importe.De(25m), "otra", Texto(), Texto(), Texto(), null);

        Assert.Throws<InvalidOperationException>(() => intencion.Reemplazar(deOtra, null, Inicio.AddMinutes(1)));
        Assert.Equal(Cuenta, intencion.CuentaId);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public void Reabrir_tambien_anula_la_huella_junto_con_el_contacto()
    {
        var intencion = Abierta();

        intencion.Reabrir();

        Assert.Null(intencion.HuellaDeInstantanea);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public void Una_intencion_nueva_con_impresos_vence_a_los_15_minutos_y_su_pendiente_a_los_30()
    {
        var intencion = Abierta();

        Assert.Equal(PedidoId, intencion.Id);
        Assert.Equal(EstadoDeIntencion.Abierta, intencion.Estado);
        Assert.Equal(Inicio.AddMinutes(15), intencion.VenceEn);
        Assert.Equal(Inicio.AddMinutes(30), intencion.LimitePendienteEn);
        Assert.Equal(Inicio.AddMinutes(15), intencion.ReservaExpiraEn);
        Assert.Equal(Inicio, intencion.IniciadaEn);
        Assert.False(intencion.InstantaneaObsoleta);
        Assert.Equal(Importe.De(140m), intencion.Total);
        Assert.Equal(2, intencion.Lineas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public void Una_intencion_solo_digital_vence_a_los_30_minutos_y_no_tiene_reserva()
    {
        var intencion = AbiertaSoloDigital();

        Assert.Equal(Inicio.AddMinutes(30), intencion.VenceEn);
        Assert.Null(intencion.ReservaExpiraEn);
        Assert.False(intencion.TieneImpresos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public void Crear_con_la_misma_huella_es_idempotente_en_una_intencion_abierta_o_con_cobro()
    {
        var abierta = Abierta();
        var conCobro = ConCobro();

        Assert.Equal(ResultadoDeCrear.Creada, abierta.Reemplazar(Mixta(), Inicio.AddMinutes(10), Inicio.AddMinutes(2)));
        Assert.Equal(ResultadoDeCrear.Creada, conCobro.Reemplazar(Mixta(), Inicio.AddMinutes(10), Inicio.AddMinutes(2)));

        // Nada cambia: ni plazos ni estado.
        Assert.Equal(Inicio, abierta.IniciadaEn);
        Assert.Equal(Inicio.AddMinutes(15), abierta.VenceEn);
        Assert.Equal(EstadoDeIntencion.ConCobro, conCobro.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public void Crear_con_otra_huella_y_un_intento_activo_es_un_conflicto_y_no_cambia_nada()
    {
        var conCobro = ConCobro();

        var resultado = conCobro.Reemplazar(Mixta("huella-b"), Inicio.AddMinutes(20), Inicio.AddMinutes(2));

        Assert.Equal(ResultadoDeCrear.Conflicto, resultado);
        Assert.Equal("huella-a", conCobro.HuellaDeInstantanea);
        Assert.Equal(Inicio, conCobro.IniciadaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public void Crear_con_otra_huella_sin_intento_activo_reemplaza_la_instantanea_y_renueva_los_plazos()
    {
        var intencion = Abierta();
        var nueva = Mixta("huella-b", envio: 20m);
        var ahora = Inicio.AddMinutes(4);

        var resultado = intencion.Reemplazar(nueva, ahora.AddMinutes(15), ahora);

        Assert.Equal(ResultadoDeCrear.Reemplazada, resultado);
        Assert.Equal("huella-b", intencion.HuellaDeInstantanea);
        Assert.Equal(Importe.De(145m), intencion.Total);
        Assert.Equal(Importe.De(20m), intencion.Envio);
        Assert.Equal(ahora, intencion.IniciadaEn);
        Assert.Equal(ahora.AddMinutes(15), intencion.VenceEn);
        Assert.Equal(ahora.AddMinutes(30), intencion.LimitePendienteEn);
        Assert.Equal(ahora.AddMinutes(15), intencion.ReservaExpiraEn);
        Assert.False(intencion.InstantaneaObsoleta);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public void Crear_nunca_deja_la_vigencia_mas_alla_de_la_reserva_nueva()
    {
        var intencion = Abierta();
        var ahora = Inicio.AddMinutes(4);

        intencion.Reemplazar(Mixta("huella-b"), reservaExpiraEn: ahora.AddMinutes(9), ahora);

        Assert.Equal(ahora.AddMinutes(9), intencion.VenceEn);
        Assert.Equal(ahora.AddMinutes(9), intencion.ReservaExpiraEn);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public void Crear_sobre_una_intencion_cerrada_sin_cobro_o_compensada_responde_cerrada()
    {
        var cerrada = Abierta();
        Assert.Equal(ResultadoDeCancelacion.Cancelada, cerrada.Cancelar(Inicio.AddMinutes(1)));
        var compensada = Aprobada();
        compensada.Compensar(Inicio.AddMinutes(2));

        Assert.Equal(ResultadoDeCrear.Cerrada, cerrada.Reemplazar(Mixta("huella-b"), Inicio.AddMinutes(20), Inicio.AddMinutes(3)));
        Assert.Equal(ResultadoDeCrear.Cerrada, compensada.Reemplazar(Mixta(), Inicio.AddMinutes(20), Inicio.AddMinutes(3)));
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-14")]
    [InlineData("huella-a")]
    [InlineData("huella-otra")]
    public void Crear_sobre_una_intencion_aprobada_o_completada_responde_en_curso_con_cualquier_huella_y_nunca_cerrada(string huella)
    {
        var aprobada = Aprobada();
        var completada = Aprobada();
        completada.Completar(Inicio.AddMinutes(2));

        Assert.Equal(ResultadoDeCrear.YaEnCurso, aprobada.Reemplazar(Mixta(huella), Inicio.AddMinutes(20), Inicio.AddMinutes(3)));
        Assert.Equal(ResultadoDeCrear.YaEnCurso, completada.Reemplazar(Mixta(huella), Inicio.AddMinutes(20), Inicio.AddMinutes(3)));
        Assert.Equal(EstadoDeIntencion.Aprobada, aprobada.Estado);
        Assert.Equal(EstadoDeIntencion.Completada, completada.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public void Reabrir_deja_la_intencion_abierta_sin_contacto_y_obsoleta_y_un_crear_la_reemplaza()
    {
        var intencion = Abierta();

        Assert.Equal(ResultadoDeReapertura.Reabierta, intencion.Reabrir());

        Assert.Equal(EstadoDeIntencion.Abierta, intencion.Estado);
        Assert.Null(intencion.Contacto);
        Assert.True(intencion.InstantaneaObsoleta);
        Assert.Equal(ValidacionDelCobro.InstantaneaObsoleta, intencion.ValidarCobro(Inicio.AddMinutes(1)));

        // Aunque la huella sea la misma, una intención obsoleta se reemplaza y vuelve a tener contacto.
        Assert.Equal(ResultadoDeCrear.Reemplazada, intencion.Reemplazar(Mixta(), Inicio.AddMinutes(20), Inicio.AddMinutes(5)));
        Assert.False(intencion.InstantaneaObsoleta);
        Assert.NotNull(intencion.Contacto);
        Assert.Equal(Inicio.AddMinutes(5), intencion.IniciadaEn);
        Assert.Equal(ValidacionDelCobro.Permitido, intencion.ValidarCobro(Inicio.AddMinutes(6)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public void Reabrir_es_idempotente_y_no_se_permite_con_un_intento_activo_ni_con_la_compra_en_curso_o_cerrada()
    {
        var intencion = Abierta();
        Assert.Equal(ResultadoDeReapertura.Reabierta, intencion.Reabrir());
        Assert.Equal(ResultadoDeReapertura.Reabierta, intencion.Reabrir());

        Assert.Equal(ResultadoDeReapertura.ConIntentoActivo, ConCobro().Reabrir());
        Assert.Equal(ResultadoDeReapertura.YaEnCurso, Aprobada().Reabrir());

        var cerrada = Abierta();
        cerrada.Cancelar(Inicio.AddMinutes(1));
        Assert.Equal(ResultadoDeReapertura.Cerrada, cerrada.Reabrir());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public void Cancelar_cierra_una_intencion_abierta_y_no_una_con_intento_activo()
    {
        var abierta = Abierta();

        Assert.Equal(ResultadoDeCancelacion.Cancelada, abierta.Cancelar(Inicio.AddMinutes(1)));
        Assert.Equal(EstadoDeIntencion.CerradaSinCobro, abierta.Estado);
        Assert.Equal(MotivoDeCierre.Cancelada, abierta.MotivoDeCierre);
        Assert.Equal(Inicio.AddMinutes(1), abierta.CerradaEn);
        Assert.Equal(ResultadoDeCancelacion.Cerrada, abierta.Cancelar(Inicio.AddMinutes(2)));

        Assert.Equal(ResultadoDeCancelacion.ConIntentoActivo, ConCobro().Cancelar(Inicio.AddMinutes(1)));
        Assert.Equal(ResultadoDeCancelacion.ConIntentoActivo, Aprobada().Cancelar(Inicio.AddMinutes(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public void Se_niega_el_cobro_con_la_reserva_expirada()
    {
        var intencion = Abierta();

        Assert.Equal(ValidacionDelCobro.Permitido, intencion.ValidarCobro(Inicio.AddMinutes(15).AddTicks(-1)));
        Assert.Equal(ValidacionDelCobro.ReservaExpirada, intencion.ValidarCobro(Inicio.AddMinutes(15)));
        Assert.Equal(ValidacionDelCobro.ReservaExpirada, intencion.ValidarCobro(Inicio.AddMinutes(16)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public void Una_compra_solo_digital_se_cobra_hasta_los_30_minutos()
    {
        var intencion = AbiertaSoloDigital();

        Assert.Equal(ValidacionDelCobro.Permitido, intencion.ValidarCobro(Inicio.AddMinutes(29)));
        Assert.Equal(ValidacionDelCobro.ReservaExpirada, intencion.ValidarCobro(Inicio.AddMinutes(30)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public void Se_niega_el_cobro_de_una_intencion_cancelada_vencida_o_compensada()
    {
        var cancelada = Abierta();
        cancelada.Cancelar(Inicio.AddMinutes(1));
        var vencida = Abierta();
        Assert.True(vencida.CerrarPorVencimiento(Inicio.AddMinutes(16)));
        var compensada = Aprobada();
        compensada.Compensar(Inicio.AddMinutes(2));

        Assert.Equal(ValidacionDelCobro.IntencionCerrada, cancelada.ValidarCobro(Inicio.AddMinutes(2)));
        Assert.Equal(ValidacionDelCobro.IntencionCerrada, vencida.ValidarCobro(Inicio.AddMinutes(17)));
        Assert.Equal(ValidacionDelCobro.IntencionCerrada, compensada.ValidarCobro(Inicio.AddMinutes(3)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public void Se_niega_otro_cobro_con_un_intento_activo_o_la_compra_ya_aprobada_desde_cualquier_pestana()
    {
        Assert.Equal(ValidacionDelCobro.CobroEnCurso, ConCobro().ValidarCobro(Inicio.AddMinutes(1)));
        Assert.Equal(ValidacionDelCobro.CobroEnCurso, Aprobada().ValidarCobro(Inicio.AddMinutes(2)));

        var completada = Aprobada();
        completada.Completar(Inicio.AddMinutes(2));
        Assert.Equal(ValidacionDelCobro.CobroEnCurso, completada.ValidarCobro(Inicio.AddMinutes(3)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public void Iniciar_el_cobro_pasa_a_con_cobro_y_un_segundo_inicio_se_rechaza()
    {
        var intencion = Abierta();

        Assert.Equal(ValidacionDelCobro.Permitido, intencion.IniciarCobro(Inicio.AddSeconds(5)));
        Assert.Equal(EstadoDeIntencion.ConCobro, intencion.Estado);
        Assert.Equal(ValidacionDelCobro.CobroEnCurso, intencion.IniciarCobro(Inicio.AddSeconds(6)));
        Assert.Equal(EstadoDeIntencion.ConCobro, intencion.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public void Una_intencion_abierta_vence_solo_cuando_pasa_su_vigencia_y_no_tiene_intento_activo()
    {
        var abierta = Abierta();
        Assert.False(abierta.CerrarPorVencimiento(Inicio.AddMinutes(15).AddTicks(-1)));
        Assert.True(abierta.CerrarPorVencimiento(Inicio.AddMinutes(15)));
        Assert.Equal(EstadoDeIntencion.CerradaSinCobro, abierta.Estado);
        Assert.Equal(MotivoDeCierre.Vencida, abierta.MotivoDeCierre);

        // Una con intento activo no la cierra el vencimiento: la resuelve su intento.
        var conCobro = ConCobro();
        Assert.False(conCobro.CerrarPorVencimiento(Inicio.AddMinutes(40)));
        Assert.Equal(EstadoDeIntencion.ConCobro, conCobro.Estado);
        var aprobada = Aprobada();
        Assert.False(aprobada.CerrarPorVencimiento(Inicio.AddMinutes(40)));
        Assert.Equal(EstadoDeIntencion.Aprobada, aprobada.Estado);
    }
}
