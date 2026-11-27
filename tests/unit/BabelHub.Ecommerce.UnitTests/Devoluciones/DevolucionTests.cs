using BabelHub.Ecommerce.Domain.Devoluciones;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>La devolución y sus líneas: un solo tipo, la máquina de estados, los cambios con responsable, el reingreso en tres pasos y el rechazo por plazo (T01; DEV-01, DEV-07, DEV-10, DEV-14, DEV-16, DEV-17, DEV-18).</summary>
public sealed class DevolucionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

    private static readonly Guid Cliente = Guid.Parse("c1000000-0000-0000-0000-000000000001");

    private static readonly Guid Personal = Guid.Parse("d1000000-0000-0000-0000-000000000001");

    private static TextoRedactado Motivo() => TextoRedactado.Crear("Llegó con las hojas dobladas", TextoRedactado.LongitudMaximaDelMotivo)!;

    private static NuevaLineaDeDevolucion Linea(decimal precio = 50m, int cantidad = 1, Guid? acceso = null) => new(Guid.NewGuid(), Guid.NewGuid(), cantidad, precio, acceso);

    private static Devolucion Impreso(int lineas = 2, DateTimeOffset? ahora = null) =>
        Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Impreso, OrigenDeDevolucion.Solicitud, "lima-central", Motivo(), null, [.. Enumerable.Range(0, lineas).Select(_ => Linea())], ahora ?? Ahora, ResponsableDelCambio.Cliente(Cliente));

    private static Devolucion Digital() =>
        Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Digital, OrigenDeDevolucion.Solicitud, null, Motivo(), null, [Linea(30m)], Ahora, ResponsableDelCambio.Cliente(Cliente));

    private static Devolucion Reporte() =>
        Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Digital, OrigenDeDevolucion.Reporte, null, Motivo(), TextoRedactado.Crear("El archivo no abre", TextoRedactado.LongitudMaximaDelTexto), [Linea(30m, 1, Guid.NewGuid())], Ahora, ResponsableDelCambio.Cliente(Cliente));

    private static Devolucion Aprobada(DateTimeOffset? aprobadaEn = null)
    {
        var devolucion = Impreso();
        Assert.Equal(ResultadoDeTransicionDeDevolucion.Aplicada, devolucion.Aprobar(aprobadaEn ?? Ahora, ResponsableDelCambio.Personal(Personal)));
        return devolucion;
    }

    // ---------- Solicitar ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-01")]
    public void Una_solicitud_de_impreso_registra_lineas_cantidades_motivo_y_sucursal_con_cada_linea_solicitada_y_su_responsable()
    {
        var devolucion = Impreso(2);

        Assert.Equal(TipoDeDevolucion.Impreso, devolucion.Tipo);
        Assert.Equal("lima-central", devolucion.CodigoDeSucursalReceptora);
        Assert.Equal(2, devolucion.Lineas.Count);
        Assert.All(devolucion.Lineas, l =>
        {
            Assert.Equal(EstadoDeLineaDeDevolucion.Solicitada, l.Estado);
            var cambio = Assert.Single(l.Cambios);
            Assert.Equal(Ahora, cambio.Instante);
            Assert.Equal(ResponsableDelCambio.Cliente(Cliente), cambio.Responsable);
        });
        Assert.False(devolucion.Motivo.EstaRedactado);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-01")]
    public void El_monto_de_una_linea_es_el_precio_unitario_por_la_cantidad_sin_el_envio()
    {
        var devolucion = Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Impreso, OrigenDeDevolucion.Solicitud, "lima-central", Motivo(), null, [Linea(129.90m, 3)], Ahora, ResponsableDelCambio.Cliente(Cliente));

        Assert.Equal(389.70m, devolucion.Lineas[0].Monto);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-01")]
    public void Un_impreso_exige_sucursal_un_digital_no_la_admite_y_el_texto_es_solo_de_los_reportes()
    {
        var lineas = new[] { Linea() };
        var yo = ResponsableDelCambio.Cliente(Cliente);

        Assert.Throws<ArgumentException>(() => Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Impreso, OrigenDeDevolucion.Solicitud, null, Motivo(), null, lineas, Ahora, yo));
        Assert.Throws<ArgumentException>(() => Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Impreso, OrigenDeDevolucion.Solicitud, new string('x', 41), Motivo(), null, lineas, Ahora, yo));
        Assert.Throws<ArgumentException>(() => Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Digital, OrigenDeDevolucion.Solicitud, "lima-central", Motivo(), null, lineas, Ahora, yo));
        Assert.Throws<ArgumentException>(() => Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Impreso, OrigenDeDevolucion.Solicitud, "lima-central", Motivo(), Motivo(), lineas, Ahora, yo));
        Assert.Throws<ArgumentException>(() => Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Digital, OrigenDeDevolucion.Reporte, null, Motivo(), null, [Linea(30m, 1, Guid.NewGuid())], Ahora, yo));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-01")]
    public void Una_devolucion_lleva_de_1_a_20_lineas_de_pedido_distintas_y_un_impreso_admite_dos_de_una_edicion()
    {
        var yo = ResponsableDelCambio.Cliente(Cliente);
        var repetida = Linea();

        Assert.Throws<ArgumentException>(() => Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Impreso, OrigenDeDevolucion.Solicitud, "lima-central", Motivo(), null, [], Ahora, yo));
        Assert.Throws<ArgumentException>(() => Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Impreso, OrigenDeDevolucion.Solicitud, "lima-central", Motivo(), null, [.. Enumerable.Range(0, 21).Select(_ => Linea())], Ahora, yo));
        Assert.Throws<ArgumentException>(() => Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Impreso, OrigenDeDevolucion.Solicitud, "lima-central", Motivo(), null, [repetida, repetida], Ahora, yo));
        Assert.Equal(2, Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Impreso, OrigenDeDevolucion.Solicitud, "lima-central", Motivo(), null, [repetida, repetida with { LineaDePedidoId = Guid.NewGuid() }], Ahora, yo).Lineas.Count);
        Assert.Throws<ArgumentException>(() => Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Digital, OrigenDeDevolucion.Solicitud, null, Motivo(), null, [repetida, repetida with { LineaDePedidoId = Guid.NewGuid() }], Ahora, yo));
        Assert.Equal(20, Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Impreso, OrigenDeDevolucion.Solicitud, "lima-central", Motivo(), null, [.. Enumerable.Range(0, 20).Select(_ => Linea())], Ahora, yo).Lineas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-16")]
    public void Un_reporte_es_de_una_sola_linea_digital_con_su_acceso_y_una_solicitud_no_lleva_acceso()
    {
        var yo = ResponsableDelCambio.Cliente(Cliente);

        Assert.NotNull(Reporte().Lineas[0].AccesoId);
        Assert.Throws<ArgumentException>(() => Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Impreso, OrigenDeDevolucion.Reporte, "lima-central", Motivo(), Motivo(), [Linea(30m, 1, Guid.NewGuid())], Ahora, yo));
        Assert.Throws<ArgumentException>(() => Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Digital, OrigenDeDevolucion.Reporte, null, Motivo(), Motivo(), [Linea(30m, 1, Guid.NewGuid()), Linea(30m, 1, Guid.NewGuid())], Ahora, yo));
        Assert.Throws<ArgumentException>(() => Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Digital, OrigenDeDevolucion.Solicitud, null, Motivo(), null, [Linea(30m, 1, Guid.NewGuid())], Ahora, yo));
    }

    // ---------- Aprobar y rechazar la devolución completa ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-10")]
    public void Aprobar_pasa_todas_las_lineas_con_la_fecha_y_el_responsable_y_repetirlo_es_estado_invalido_sin_efectos()
    {
        var devolucion = Impreso();

        var primera = devolucion.Aprobar(Ahora.AddHours(1), ResponsableDelCambio.Personal(Personal));
        var repetida = devolucion.Aprobar(Ahora.AddHours(2), ResponsableDelCambio.Personal(Personal));

        Assert.Equal(ResultadoDeTransicionDeDevolucion.Aplicada, primera);
        Assert.Equal(ResultadoDeTransicionDeDevolucion.EstadoInvalido, repetida);
        Assert.All(devolucion.Lineas, l =>
        {
            Assert.Equal(EstadoDeLineaDeDevolucion.Aprobada, l.Estado);
            Assert.Equal(2, l.Cambios.Count);
            Assert.Equal(Ahora.AddHours(1), l.AprobadaEn);
            Assert.Equal(ResponsableDelCambio.Personal(Personal), l.Cambios[1].Responsable);
        });
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-17")]
    public void Rechazar_por_decision_opera_solo_sobre_lineas_solicitadas_y_una_aprobada_ya_no_se_rechaza_por_decision()
    {
        var solicitada = Impreso();
        var aprobada = Aprobada();

        Assert.Equal(ResultadoDeTransicionDeDevolucion.Aplicada, solicitada.Rechazar(Ahora, ResponsableDelCambio.Personal(Personal)));
        Assert.All(solicitada.Lineas, l => Assert.Equal(EstadoDeLineaDeDevolucion.Rechazada, l.Estado));
        Assert.Equal(ResultadoDeTransicionDeDevolucion.EstadoInvalido, aprobada.Rechazar(Ahora, ResponsableDelCambio.Personal(Personal)));
        Assert.Equal(ResultadoDeTransicionDeDevolucion.EstadoInvalido, solicitada.Aprobar(Ahora, ResponsableDelCambio.Personal(Personal)));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-17")]
    public void Una_linea_rechazada_y_una_reembolsada_son_finales_y_no_comprometen_el_cupo_solo_la_rechazada()
    {
        Assert.True(EstadoDeLineaDeDevolucion.Rechazada.EsFinal());
        Assert.True(EstadoDeLineaDeDevolucion.Reembolsada.EsFinal());
        Assert.False(EstadoDeLineaDeDevolucion.Recibida.EsFinal());
        Assert.False(EstadoDeLineaDeDevolucion.Rechazada.ComprometeCupo());
        Assert.All(new[] { EstadoDeLineaDeDevolucion.Solicitada, EstadoDeLineaDeDevolucion.Aprobada, EstadoDeLineaDeDevolucion.Recibida, EstadoDeLineaDeDevolucion.Reembolsada }, e => Assert.True(e.ComprometeCupo()));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-17")]
    public void Los_codigos_de_estado_van_y_vuelven()
    {
        foreach (var estado in Enum.GetValues<EstadoDeLineaDeDevolucion>())
        {
            Assert.Equal(estado, EstadosDeLineaDeDevolucion.Desde(estado.Codigo()));
        }

        Assert.Throws<ArgumentException>(() => EstadosDeLineaDeDevolucion.Desde("devuelta"));
    }

    // ---------- Plazo de recepción (DEV-14, DEV-16) ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-15")]
    public void La_recepcion_vence_a_mas_de_10_dias_y_a_10_dias_exactos_no()
    {
        var aprobada = Aprobada(Ahora);
        var exacto = Aprobada(Ahora);

        Assert.Equal(ResultadoDeTransicionDeDevolucion.NoVencida, exacto.RechazarPorPlazoDeRecepcion(Ahora.AddDays(10)));
        Assert.Equal(ResultadoDeTransicionDeDevolucion.Aplicada, aprobada.RechazarPorPlazoDeRecepcion(Ahora.AddDays(10).AddHours(1)));
        Assert.All(aprobada.Lineas, l =>
        {
            Assert.Equal(EstadoDeLineaDeDevolucion.Rechazada, l.Estado);
            Assert.Equal(ResponsableDelCambio.Sistema, l.Cambios[^1].Responsable);
        });
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-15")]
    public void Con_el_reingreso_iniciado_el_plazo_no_rechaza_la_devolucion()
    {
        var devolucion = Aprobada(Ahora);
        Assert.Equal(ResultadoDeInicioDeReingreso.Iniciado, devolucion.IniciarReingreso(Ahora.AddDays(9)));

        var resultado = devolucion.RechazarPorPlazoDeRecepcion(Ahora.AddDays(30));

        Assert.Equal(ResultadoDeTransicionDeDevolucion.ReingresoIniciado, resultado);
        Assert.All(devolucion.Lineas, l => Assert.Equal(EstadoDeLineaDeDevolucion.Aprobada, l.Estado));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-15")]
    public void El_plazo_de_recepcion_no_aplica_a_un_digital_ni_a_una_devolucion_que_no_esta_aprobada()
    {
        Assert.Equal(ResultadoDeTransicionDeDevolucion.TipoInvalido, Digital().RechazarPorPlazoDeRecepcion(Ahora.AddDays(99)));
        Assert.Equal(ResultadoDeTransicionDeDevolucion.EstadoInvalido, Impreso().RechazarPorPlazoDeRecepcion(Ahora.AddDays(99)));
    }

    // ---------- Reingreso y recepción (DEV-07) ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-07")]
    public void Recibir_exige_el_reingreso_confirmado_y_entonces_pasa_todas_las_lineas_a_recibida()
    {
        var devolucion = Aprobada();

        Assert.Equal(ResultadoDeTransicionDeDevolucion.ReingresoSinConfirmar, devolucion.Recibir(Ahora, ResponsableDelCambio.Personal(Personal)));
        devolucion.IniciarReingreso(Ahora);
        Assert.Equal(ResultadoDeTransicionDeDevolucion.ReingresoSinConfirmar, devolucion.Recibir(Ahora, ResponsableDelCambio.Personal(Personal)));
        devolucion.ConfirmarReingreso(Ahora.AddMinutes(1));

        Assert.Equal(ResultadoDeTransicionDeDevolucion.Aplicada, devolucion.Recibir(Ahora.AddMinutes(2), ResponsableDelCambio.Personal(Personal)));
        Assert.All(devolucion.Lineas, l => Assert.Equal(EstadoDeLineaDeDevolucion.Recibida, l.Estado));
        Assert.Equal(ResultadoDeTransicionDeDevolucion.EstadoInvalido, devolucion.Recibir(Ahora.AddMinutes(3), ResponsableDelCambio.Personal(Personal)));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-07")]
    public void El_marcador_de_reingreso_se_fija_una_vez_y_un_reintento_lo_encuentra_ya_iniciado()
    {
        var devolucion = Aprobada();

        Assert.Equal(ResultadoDeInicioDeReingreso.Iniciado, devolucion.IniciarReingreso(Ahora));
        Assert.Equal(ResultadoDeInicioDeReingreso.YaIniciado, devolucion.IniciarReingreso(Ahora.AddMinutes(5)));
        Assert.Equal(Ahora, devolucion.ReingresoIniciadoEn);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-07")]
    public void No_se_inicia_el_reingreso_de_una_devolucion_no_aprobada_ni_de_un_digital()
    {
        Assert.Equal(ResultadoDeInicioDeReingreso.EstadoInvalido, Impreso().IniciarReingreso(Ahora));
        Assert.Equal(ResultadoDeInicioDeReingreso.TipoInvalido, Digital().IniciarReingreso(Ahora));
        Assert.Equal(ResultadoDeTransicionDeDevolucion.TipoInvalido, Digital().Recibir(Ahora, ResponsableDelCambio.Sistema));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-07")]
    public void Un_rechazo_de_negocio_limpia_el_marcador_solo_si_ningun_intento_anterior_quedo_incierto()
    {
        var limpio = Aprobada();
        limpio.IniciarReingreso(Ahora);
        var incierto = Aprobada();
        incierto.IniciarReingreso(Ahora);
        incierto.MarcarReingresoIncierto(Ahora.AddMinutes(1));
        var confirmado = Aprobada();
        confirmado.IniciarReingreso(Ahora);
        confirmado.ConfirmarReingreso(Ahora);

        Assert.True(limpio.LimpiarMarcadorDeReingreso());
        Assert.Null(limpio.ReingresoIniciadoEn);
        Assert.False(incierto.LimpiarMarcadorDeReingreso());
        Assert.NotNull(incierto.ReingresoIniciadoEn);
        Assert.False(confirmado.LimpiarMarcadorDeReingreso());
        Assert.False(Aprobada().LimpiarMarcadorDeReingreso());
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-07")]
    public void El_primer_resultado_incierto_se_conserva_y_no_se_borra()
    {
        var devolucion = Aprobada();
        devolucion.IniciarReingreso(Ahora);

        devolucion.MarcarReingresoIncierto(Ahora.AddMinutes(1));
        devolucion.MarcarReingresoIncierto(Ahora.AddMinutes(9));

        Assert.Equal(Ahora.AddMinutes(1), devolucion.ReingresoIncertoEn);
        Assert.Throws<InvalidOperationException>(() => Aprobada().MarcarReingresoIncierto(Ahora));
        Assert.Throws<InvalidOperationException>(() => Aprobada().ConfirmarReingreso(Ahora));
    }

    // ---------- Reembolso por línea ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-16")]
    public void Un_impreso_se_reembolsa_linea_a_linea_solo_desde_recibida_y_repetirlo_es_estado_invalido()
    {
        var devolucion = Aprobada();
        var primera = devolucion.Lineas[0].LineaDePedidoId;
        var segunda = devolucion.Lineas[1].LineaDePedidoId;

        Assert.Equal(ResultadoDeTransicionDeDevolucion.EstadoInvalido, devolucion.Reembolsar(primera, Ahora, ResponsableDelCambio.Sistema));
        devolucion.IniciarReingreso(Ahora);
        devolucion.ConfirmarReingreso(Ahora);
        devolucion.Recibir(Ahora, ResponsableDelCambio.Personal(Personal));
        devolucion.RegistrarReembolsoDeLinea(primera, Ahora);
        var fecha = devolucion.Lineas[0].ReembolsoRegistradoEn;
        devolucion.RegistrarReembolsoDeLinea(primera, Ahora.AddHours(1));

        Assert.Equal(ResultadoDeTransicionDeDevolucion.Aplicada, devolucion.Reembolsar(primera, Ahora.AddMinutes(5), ResponsableDelCambio.Sistema));
        Assert.Equal(ResultadoDeTransicionDeDevolucion.EstadoInvalido, devolucion.Reembolsar(primera, Ahora.AddMinutes(6), ResponsableDelCambio.Sistema));
        Assert.Equal(EstadoDeLineaDeDevolucion.Recibida, devolucion.Lineas[1].Estado);
        Assert.Equal(fecha, devolucion.Lineas[0].ReembolsoRegistradoEn);
        Assert.Equal(Ahora.AddMinutes(5), devolucion.Lineas[0].ReembolsadaEn);
        Assert.Null(devolucion.Lineas[1].ReembolsadaEn);
        Assert.False(devolucion.TodasFinales);
        Assert.Equal(ResultadoDeTransicionDeDevolucion.EstadoInvalido, devolucion.Reembolsar(segunda, Ahora.AddMinutes(7), ResponsableDelCambio.Sistema));
        devolucion.RegistrarReembolsoDeLinea(segunda, Ahora);
        Assert.Equal(ResultadoDeTransicionDeDevolucion.Aplicada, devolucion.Reembolsar(segunda, Ahora.AddMinutes(8), ResponsableDelCambio.Sistema));
        Assert.True(devolucion.TodasFinales);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-08")]
    public void Un_digital_aprobado_por_el_sistema_se_reembolsa_sin_pasar_por_recibida()
    {
        var devolucion = Digital();
        var linea = devolucion.Lineas[0].LineaDePedidoId;

        Assert.Equal(ResultadoDeTransicionDeDevolucion.Aplicada, devolucion.Aprobar(Ahora, ResponsableDelCambio.Sistema));
        devolucion.RegistrarReembolsoDeLinea(linea, Ahora);
        Assert.Equal(ResultadoDeTransicionDeDevolucion.Aplicada, devolucion.Reembolsar(linea, Ahora.AddMinutes(1), ResponsableDelCambio.Sistema));

        Assert.DoesNotContain(devolucion.Lineas[0].Cambios, c => c.Estado == EstadoDeLineaDeDevolucion.Recibida);
        Assert.Throws<ArgumentException>(() => devolucion.Reembolsar(Guid.NewGuid(), Ahora, ResponsableDelCambio.Sistema));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-07")]
    public void El_responsable_sistema_no_tiene_identificador_y_cliente_y_personal_si()
    {
        Assert.Null(ResponsableDelCambio.Sistema.Id);
        Assert.Throws<ArgumentException>(() => ResponsableDelCambio.Cliente(Guid.Empty));
        Assert.Throws<ArgumentException>(() => ResponsableDelCambio.Personal(Guid.Empty));
        Assert.Throws<ArgumentException>(() => ResponsableDelCambio.Rehidratar(TipoDeResponsable.Sistema, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => ResponsableDelCambio.Rehidratar(TipoDeResponsable.Personal, null));
        Assert.Null(ResponsableDelCambio.Rehidratar(TipoDeResponsable.Cliente, null).Id);
    }

    // ---------- Rehidratar ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-16")]
    public void Rehidratar_devuelve_lo_guardado_y_rechaza_lo_incoherente()
    {
        var original = Aprobada();
        var linea = original.Lineas[0];
        LineaRehidratada Guardada(IReadOnlyList<CambioDeEstado> cambios) => new(linea.Id, linea.LineaDePedidoId, linea.EdicionId, linea.Cantidad, linea.PrecioUnitario, linea.Monto, null, cambios, null);

        var rehidratada = Devolucion.Rehidratar(original.Id, original.PedidoId, original.Tipo, original.Origen, original.CodigoDeSucursalReceptora, original.Motivo, null, original.CreadaEn, null, null, null, null, [Guardada(linea.Cambios)]);

        Assert.Equal(EstadoDeLineaDeDevolucion.Aprobada, rehidratada.Lineas[0].Estado);
        Assert.Throws<ArgumentException>(() => Devolucion.Rehidratar(original.Id, original.PedidoId, original.Tipo, original.Origen, original.CodigoDeSucursalReceptora, original.Motivo, null, original.CreadaEn, null, null, Ahora, null, [Guardada(linea.Cambios)]));
        Assert.Throws<ArgumentException>(() => Devolucion.Rehidratar(original.Id, original.PedidoId, TipoDeDevolucion.Digital, original.Origen, null, original.Motivo, null, original.CreadaEn, null, Ahora, null, null, [Guardada(linea.Cambios)]));
        Assert.Throws<ArgumentException>(() => Devolucion.Rehidratar(original.Id, original.PedidoId, original.Tipo, original.Origen, null, original.Motivo, null, original.CreadaEn, null, null, null, null, [Guardada(linea.Cambios)]));
        Assert.Throws<ArgumentException>(() => Devolucion.Rehidratar(original.Id, original.PedidoId, original.Tipo, original.Origen, original.CodigoDeSucursalReceptora, original.Motivo, null, original.CreadaEn, null, null, null, null, [Guardada([new CambioDeEstado(EstadoDeLineaDeDevolucion.Aprobada, Ahora, ResponsableDelCambio.Sistema)])]));
        Assert.Throws<ArgumentException>(() => Devolucion.Rehidratar(original.Id, original.PedidoId, original.Tipo, original.Origen, original.CodigoDeSucursalReceptora, original.Motivo, null, original.CreadaEn, null, null, null, null, [Guardada([linea.Cambios[0], new CambioDeEstado(EstadoDeLineaDeDevolucion.Reembolsada, Ahora, ResponsableDelCambio.Sistema)])]));
    }
}
