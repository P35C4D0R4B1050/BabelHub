using BabelHub.Ecommerce.Domain.Envios;

namespace BabelHub.Ecommerce.UnitTests.Envios;

/// <summary>El envío, su máquina de estados, la guía, el arrendamiento y el marcador de despacho (T01; ENV-03, ENV-19).</summary>
public sealed class EnvioTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Clave = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    internal static Envio Nuevo(params Guid[] lineas) =>
        Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "lima-central", lineas.Length == 0 ? [Guid.NewGuid(), Guid.NewGuid()] : lineas, Ahora);

    internal static Envio ConGuia()
    {
        var envio = Nuevo();
        Assert.Equal(ResultadoDeSolicitudDeGuia.Tomada, envio.TomarSolicitudDeGuia(Ahora, Clave));
        Assert.Equal(ResultadoDeRegistroDeGuia.Registrada, envio.RegistrarGuia(NumeroDeGuia.Crear("GUIA-000123"), "operador-a", Ahora));
        return envio;
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-03")]
    public void Un_envio_nuevo_esta_pendiente_de_guia_sin_numero_ni_marcador()
    {
        var envio = Nuevo();

        Assert.Equal(EstadoDeEnvio.PendienteDeGuia, envio.Estado);
        Assert.Null(envio.NumeroDeGuia);
        Assert.Null(envio.Operador);
        Assert.Null(envio.ClaveDeSolicitud);
        Assert.Null(envio.DespachoIniciadoEn);
        Assert.Equal(2, envio.Lineas.Count);
        Assert.Equal("lima-central", envio.CodigoDeFuente);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-03")]
    public void Un_envio_sin_lineas_o_con_lineas_repetidas_o_sin_fuente_se_rechaza()
    {
        var linea = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "lima-central", [], Ahora));
        Assert.Throws<ArgumentException>(() => Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "lima-central", [linea, linea], Ahora));
        Assert.Throws<ArgumentException>(() => Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), " ", [linea], Ahora));
        Assert.Throws<ArgumentException>(() => Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "lima-central", [Guid.Empty], Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-03")]
    public void El_flujo_completo_recorre_los_cuatro_estados_en_orden()
    {
        var envio = ConGuia();
        Assert.Equal(EstadoDeEnvio.GuiaGenerada, envio.Estado);
        Assert.Equal(Ahora, envio.GuiaGeneradaEn);

        Assert.Equal(ResultadoDeInicioDeDespacho.Iniciado, envio.IniciarDespacho(Ahora));
        Assert.Equal(ResultadoDeTransicion.Hecha, envio.PasarAEnReparto(Ahora.AddMinutes(1)));
        Assert.Equal(EstadoDeEnvio.EnReparto, envio.Estado);
        Assert.Equal(Ahora.AddMinutes(1), envio.EnRepartoEn);

        Assert.Equal(ResultadoDeTransicion.Hecha, envio.MarcarEntregado(Ahora.AddHours(5)));
        Assert.Equal(EstadoDeEnvio.Entregado, envio.Estado);
        Assert.Equal(Ahora.AddHours(5), envio.EntregadoEn);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-03")]
    public void Una_transicion_que_salta_un_estado_se_rechaza_por_estado_invalido()
    {
        var pendiente = Nuevo();
        Assert.Equal(ResultadoDeInicioDeDespacho.EstadoInvalido, pendiente.IniciarDespacho(Ahora));
        Assert.Equal(ResultadoDeTransicion.EstadoInvalido, pendiente.PasarAEnReparto(Ahora));
        Assert.Equal(ResultadoDeTransicion.EstadoInvalido, pendiente.MarcarEntregado(Ahora));

        var conGuia = ConGuia();
        Assert.Equal(ResultadoDeTransicion.EstadoInvalido, conGuia.MarcarEntregado(Ahora));
        Assert.Equal(ResultadoDeTransicion.EstadoInvalido, conGuia.PasarAEnReparto(Ahora)); // sin el marcador del despacho
        Assert.Equal(EstadoDeEnvio.GuiaGenerada, conGuia.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-03")]
    public void Un_entregado_no_vuelve_atras_ni_se_cancela()
    {
        var envio = ConGuia();
        envio.IniciarDespacho(Ahora);
        envio.PasarAEnReparto(Ahora);
        envio.MarcarEntregado(Ahora);

        Assert.Equal(ResultadoDeTransicion.EstadoInvalido, envio.MarcarEntregado(Ahora));
        Assert.Equal(ResultadoDeTransicion.EstadoInvalido, envio.PasarAEnReparto(Ahora));
        Assert.Equal(ResultadoDeInicioDeDespacho.EstadoInvalido, envio.IniciarDespacho(Ahora));
        Assert.Equal(ResultadoDeCancelacion.Mantenido, envio.Cancelar(Ahora));
        Assert.Equal(EstadoDeEnvio.Entregado, envio.Estado);
        Assert.Null(envio.CanceladoEn);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-12")]
    public void Se_cancela_el_pendiente_y_el_de_guia_sin_marcador_y_la_repeticion_es_idempotente()
    {
        var pendiente = Nuevo();
        Assert.Equal(ResultadoDeCancelacion.Cancelado, pendiente.Cancelar(Ahora));
        Assert.Equal(EstadoDeEnvio.Cancelado, pendiente.Estado);
        Assert.Equal(Ahora, pendiente.CanceladoEn);
        Assert.Equal(ResultadoDeCancelacion.YaEstabaCancelado, pendiente.Cancelar(Ahora.AddMinutes(5)));
        Assert.Equal(Ahora, pendiente.CanceladoEn);

        var conGuia = ConGuia();
        Assert.Equal(ResultadoDeCancelacion.Cancelado, conGuia.Cancelar(Ahora));
        Assert.Equal(EstadoDeEnvio.Cancelado, conGuia.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-19")]
    public void Con_el_marcador_de_despacho_o_en_reparto_se_mantiene_y_un_cancelado_no_pasa_a_reparto()
    {
        var conMarcador = ConGuia();
        conMarcador.IniciarDespacho(Ahora);
        Assert.Equal(ResultadoDeCancelacion.Mantenido, conMarcador.Cancelar(Ahora));
        Assert.Equal(EstadoDeEnvio.GuiaGenerada, conMarcador.Estado);

        conMarcador.PasarAEnReparto(Ahora);
        Assert.Equal(ResultadoDeCancelacion.Mantenido, conMarcador.Cancelar(Ahora));
        Assert.Equal(EstadoDeEnvio.EnReparto, conMarcador.Estado);

        var cancelado = ConGuia();
        cancelado.Cancelar(Ahora);
        Assert.Equal(ResultadoDeInicioDeDespacho.EstadoInvalido, cancelado.IniciarDespacho(Ahora));
        Assert.Equal(ResultadoDeTransicion.EstadoInvalido, cancelado.PasarAEnReparto(Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public void La_solicitud_de_guia_fija_la_clave_una_vez_y_un_arrendamiento_vivo_rechaza_a_la_simultanea()
    {
        var envio = Nuevo();

        Assert.Equal(ResultadoDeSolicitudDeGuia.Tomada, envio.TomarSolicitudDeGuia(Ahora, Clave));
        Assert.Equal(Clave, envio.ClaveDeSolicitud);
        Assert.Equal(Ahora.Add(Envio.ArrendamientoDeLaGuia), envio.GuiaEnCursoHasta);

        // Otra solicitud mientras el arrendamiento vive.
        Assert.Equal(ResultadoDeSolicitudDeGuia.EnCurso, envio.TomarSolicitudDeGuia(Ahora.AddSeconds(30), Guid.NewGuid()));
        Assert.Equal(Clave, envio.ClaveDeSolicitud);

        // Vencido el arrendamiento, el reintento continúa con la misma clave.
        Assert.Equal(ResultadoDeSolicitudDeGuia.Tomada, envio.TomarSolicitudDeGuia(Ahora.AddSeconds(91), Guid.NewGuid()));
        Assert.Equal(Clave, envio.ClaveDeSolicitud);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public void Liberar_el_arrendamiento_conserva_la_clave_y_permite_reintentar_de_inmediato()
    {
        var envio = Nuevo();
        envio.TomarSolicitudDeGuia(Ahora, Clave);

        envio.LiberarArrendamientoDeLaGuia();

        Assert.Null(envio.GuiaEnCursoHasta);
        Assert.Equal(Clave, envio.ClaveDeSolicitud);
        Assert.Equal(ResultadoDeSolicitudDeGuia.Tomada, envio.TomarSolicitudDeGuia(Ahora.AddSeconds(1), Guid.NewGuid()));
        Assert.Equal(Clave, envio.ClaveDeSolicitud);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public void Solo_el_pendiente_toma_una_solicitud_de_guia()
    {
        var conGuia = ConGuia();
        Assert.Equal(ResultadoDeSolicitudDeGuia.EstadoInvalido, conGuia.TomarSolicitudDeGuia(Ahora, Guid.NewGuid()));

        var cancelado = Nuevo();
        cancelado.Cancelar(Ahora);
        Assert.Equal(ResultadoDeSolicitudDeGuia.EstadoInvalido, cancelado.TomarSolicitudDeGuia(Ahora, Guid.NewGuid()));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public void El_numero_de_guia_es_inmutable_y_la_misma_guia_repetida_es_idempotente()
    {
        var envio = ConGuia();

        Assert.Equal(ResultadoDeRegistroDeGuia.YaRegistrada, envio.RegistrarGuia(NumeroDeGuia.Crear("GUIA-000123"), "operador-a", Ahora.AddMinutes(1)));
        Assert.Equal(ResultadoDeRegistroDeGuia.OtroNumero, envio.RegistrarGuia(NumeroDeGuia.Crear("GUIA-999999"), "operador-a", Ahora.AddMinutes(1)));
        Assert.Equal("GUIA-000123", envio.NumeroDeGuia!.ToString());
        Assert.Equal(Ahora, envio.GuiaGeneradaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public void Una_guia_que_llega_a_un_envio_cancelado_queda_sin_uso()
    {
        var envio = Nuevo();
        envio.TomarSolicitudDeGuia(Ahora, Clave);
        envio.Cancelar(Ahora);

        var resultado = envio.RegistrarGuia(NumeroDeGuia.Crear("GUIA-000123"), "operador-a", Ahora);

        Assert.Equal(ResultadoDeRegistroDeGuia.GuiaSinUso, resultado);
        Assert.Null(envio.NumeroDeGuia);
        Assert.Equal(EstadoDeEnvio.Cancelado, envio.Estado);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-15")]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("GUIA 000123")]
    [InlineData("GUIA/000123")]
    [InlineData("GUIA-0001230000000000000000000000000000000000000000")]
    [InlineData("GUÍA-000123")]
    public void Un_numero_de_guia_de_largo_o_caracteres_no_aceptados_se_rechaza(string texto)
    {
        Assert.False(NumeroDeGuia.TryCrear(texto, out _));
        Assert.Throws<ArgumentException>(() => NumeroDeGuia.Crear(texto));
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-15")]
    [InlineData("GUIA-000123")]
    [InlineData("A1B2C3")]
    [InlineData("1234567890123456789012345678901234567890")]
    public void Un_numero_de_guia_valido_se_acepta_y_se_compara_por_valor(string texto)
    {
        Assert.True(NumeroDeGuia.TryCrear(texto, out var numero));
        Assert.Equal(NumeroDeGuia.Crear(texto), numero);
        Assert.Equal(texto, numero.ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-16")]
    public void El_marcador_con_arrendamiento_vivo_rechaza_la_segunda_solicitud_de_reparto()
    {
        var envio = ConGuia();

        Assert.Equal(ResultadoDeInicioDeDespacho.Iniciado, envio.IniciarDespacho(Ahora));
        Assert.Equal(Ahora, envio.DespachoIniciadoEn);
        Assert.Equal(Ahora.Add(Envio.ArrendamientoDelDespacho), envio.DespachoEnCursoHasta);

        Assert.Equal(ResultadoDeInicioDeDespacho.EnCurso, envio.IniciarDespacho(Ahora.AddSeconds(10)));
        Assert.Equal(Ahora, envio.DespachoIniciadoEn);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-16")]
    public void Vencido_el_arrendamiento_se_retoma_el_mismo_marcador_y_liberarlo_lo_conserva()
    {
        var envio = ConGuia();
        envio.IniciarDespacho(Ahora);

        Assert.Equal(ResultadoDeInicioDeDespacho.Iniciado, envio.IniciarDespacho(Ahora.AddSeconds(91)));
        Assert.Equal(Ahora, envio.DespachoIniciadoEn);
        Assert.Equal(Ahora.AddSeconds(91).Add(Envio.ArrendamientoDelDespacho), envio.DespachoEnCursoHasta);

        envio.LiberarArrendamientoDelDespacho();
        Assert.Null(envio.DespachoEnCursoHasta);
        Assert.Equal(Ahora, envio.DespachoIniciadoEn);
        Assert.Equal(ResultadoDeInicioDeDespacho.Iniciado, envio.IniciarDespacho(Ahora.AddSeconds(100)));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-19")]
    public void El_marcador_solo_se_limpia_sin_un_intento_incierto_previo()
    {
        var limpio = ConGuia();
        limpio.IniciarDespacho(Ahora);
        Assert.True(limpio.LimpiarMarcadorDeDespacho());
        Assert.Null(limpio.DespachoIniciadoEn);
        Assert.Null(limpio.DespachoEnCursoHasta);
        Assert.Equal(ResultadoDeCancelacion.Cancelado, limpio.Cancelar(Ahora)); // vuelve a ser cancelable

        var incierto = ConGuia();
        incierto.IniciarDespacho(Ahora);
        incierto.MarcarDespachoIncierto(Ahora.AddSeconds(5));
        incierto.MarcarDespachoIncierto(Ahora.AddSeconds(50)); // no se borra ni se pisa
        Assert.Equal(Ahora.AddSeconds(5), incierto.DespachoIncertoEn);
        Assert.False(incierto.LimpiarMarcadorDeDespacho());
        Assert.NotNull(incierto.DespachoIniciadoEn);
        Assert.Equal(ResultadoDeCancelacion.Mantenido, incierto.Cancelar(Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-19")]
    public void Marcar_un_despacho_incierto_sin_marcador_es_un_estado_invalido()
    {
        Assert.Throws<InvalidOperationException>(() => ConGuia().MarcarDespachoIncierto(Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-10")]
    public void El_progreso_informado_a_pedidos_se_registra_una_vez_y_en_orden()
    {
        var envio = ConGuia();
        envio.IniciarDespacho(Ahora);
        envio.PasarAEnReparto(Ahora);

        Assert.False(envio.RegistrarProgresoEntregadoInformado(Ahora)); // antes de informar el reparto
        Assert.True(envio.RegistrarProgresoEnRepartoInformado(Ahora.AddMinutes(1)));
        Assert.False(envio.RegistrarProgresoEnRepartoInformado(Ahora.AddMinutes(2))); // ya informado
        Assert.Equal(Ahora.AddMinutes(1), envio.ProgresoEnRepartoInformadoEn);

        envio.MarcarEntregado(Ahora.AddHours(1));
        Assert.True(envio.RegistrarProgresoEntregadoInformado(Ahora.AddHours(2)));
        Assert.Equal(Ahora.AddHours(2), envio.ProgresoEntregadoInformadoEn);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-03")]
    public void Rehidratar_rechaza_un_estado_incoherente_con_sus_fechas_o_su_guia()
    {
        var id = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        var lineas = new[] { Guid.NewGuid() };

        // En reparto sin número de guía.
        Assert.Throws<ArgumentException>(() => Envio.Rehidratar(
            id, pedido, "lima-central", lineas, EstadoDeEnvio.EnReparto, null, null, null, null, null, null, null, Ahora, null, Ahora, null, null, null, null, 1));

        // Pendiente con número de guía.
        Assert.Throws<ArgumentException>(() => Envio.Rehidratar(
            id, pedido, "lima-central", lineas, EstadoDeEnvio.PendienteDeGuia, NumeroDeGuia.Crear("GUIA-000123"), "operador-a", null, null, null, null, null, Ahora, null, null, null, null, null, null, 1));

        // Pendiente con el marcador de despacho.
        Assert.Throws<ArgumentException>(() => Envio.Rehidratar(
            id, pedido, "lima-central", lineas, EstadoDeEnvio.PendienteDeGuia, null, null, null, null, Ahora, null, null, Ahora, null, null, null, null, null, null, 1));
    }
}
