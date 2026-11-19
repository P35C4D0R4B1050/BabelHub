using BabelHub.Ecommerce.Domain.Pagos;
using static BabelHub.Ecommerce.UnitTests.Pagos.IntencionesDePrueba;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>El reembolso: origen, clave natural, topes por cargo y por línea, envío una sola vez y estados (T05; PAG-10, PAG-20, D08).</summary>
public sealed class ReembolsoTests
{
    private static readonly Guid IntentoId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    // La compra de las pruebas: línea impresa de S/ 100 (2 × 50), línea digital de S/ 25, envío S/ 15 = cargo de S/ 140.
    private static readonly Importe Cargo = Importe.De(140m);
    private static readonly Importe Envio = Importe.De(15m);
    private static readonly IReadOnlyDictionary<Guid, Importe> Lineas = new Dictionary<Guid, Importe>
    {
        [LineaImpresa] = Importe.De(100m),
        [LineaDigital] = Importe.De(25m),
    };

    private static Reembolso DeLinea(Guid lineaId, decimal monto, OrigenDeReembolso origen = OrigenDeReembolso.Devolucion, string? clave = null, decimal envio = 0m) =>
        Reembolso.Crear(
            Guid.NewGuid(),
            origen,
            clave ?? ClaveDeReembolso.Devolucion(Guid.NewGuid(), lineaId),
            PedidoId,
            IntentoId,
            new[] { new LineaDeReembolso(lineaId, Importe.De(monto)) },
            Importe.De(envio),
            Inicio);

    private static Reembolso Total(OrigenDeReembolso origen = OrigenDeReembolso.Anulacion) =>
        Reembolso.Crear(
            Guid.NewGuid(),
            origen,
            ClaveDeReembolso.Anulacion(PedidoId),
            PedidoId,
            IntentoId,
            new[] { new LineaDeReembolso(LineaImpresa, Importe.De(100m)), new LineaDeReembolso(LineaDigital, Importe.De(25m)) },
            Envio,
            Inicio);

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public void Un_reembolso_de_una_linea_es_por_el_monto_de_esa_linea_y_nace_pendiente_con_su_trazabilidad()
    {
        var reembolso = DeLinea(LineaDigital, 25m);

        Assert.Equal(Importe.De(25m), reembolso.Monto);
        Assert.Equal(EstadoDeReembolso.Pendiente, reembolso.Estado);
        Assert.Equal(OrigenDeReembolso.Devolucion, reembolso.Origen);
        Assert.Equal(PedidoId, reembolso.IntencionId);
        Assert.Equal(IntentoId, reembolso.IntentoId);
        Assert.Equal(Inicio, reembolso.CreadoEn);
        Assert.Equal(0, reembolso.Intentos);
        Assert.True(reembolso.CuentaComoPendiente);
        Assert.Equal(LineaDigital, Assert.Single(reembolso.Lineas).LineaId);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public void El_monto_es_la_suma_de_las_lineas_mas_el_envio_y_es_mayor_que_cero()
    {
        Assert.Equal(Importe.De(140m), Total().Monto);
        Assert.Equal(Importe.De(15m), Total().Envio);
        Assert.Throws<ArgumentException>(() => Reembolso.Crear(Guid.NewGuid(), OrigenDeReembolso.Devolucion, "dev:x", PedidoId, IntentoId, Array.Empty<LineaDeReembolso>(), Importe.Cero, Inicio));
        Assert.Throws<ArgumentException>(() => DeLinea(LineaImpresa, 0m));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void La_compensacion_o_un_cobro_sobrante_reembolsa_el_cargo_completo_sin_lineas()
    {
        var reembolso = Reembolso.CrearDeCargo(Guid.NewGuid(), OrigenDeReembolso.Compensacion, $"comp:{IntentoId}", PedidoId, IntentoId, Cargo, Inicio);

        Assert.Equal(Cargo, reembolso.Monto);
        Assert.Empty(reembolso.Lineas);
        Assert.Equal(Importe.Cero, reembolso.Envio);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void Solo_la_compensacion_se_crea_sin_lineas()
    {
        Assert.Throws<ArgumentException>(() => Reembolso.CrearDeCargo(Guid.NewGuid(), OrigenDeReembolso.Devolucion, "dev:x", PedidoId, IntentoId, Cargo, Inicio));
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-17")]
    [InlineData("")]
    [InlineData("   ")]
    public void La_clave_natural_no_puede_estar_vacia(string clave)
    {
        Assert.Throws<ArgumentException>(() => DeLinea(LineaImpresa, 10m, clave: clave));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void Las_claves_naturales_con_guid_reales_caben_en_100_caracteres()
    {
        var claves = new[]
        {
            ClaveDeReembolso.Compensacion(Guid.NewGuid()),
            ClaveDeReembolso.Anulacion(Guid.NewGuid()),
            ClaveDeReembolso.Cancelacion(Guid.NewGuid()),
            ClaveDeReembolso.Devolucion(Guid.NewGuid(), Guid.NewGuid()),
        };

        Assert.All(claves, c => Assert.InRange(c.Length, 1, Reembolso.LongitudMaximaDeLaClave));
        Assert.StartsWith("comp:", claves[0]);
        Assert.StartsWith("anul:", claves[1]);
        Assert.StartsWith("canc:", claves[2]);
        Assert.StartsWith("dev:", claves[3]);
        Assert.Equal(77, claves[3].Length);
        Assert.Throws<ArgumentException>(() => DeLinea(LineaImpresa, 10m, clave: new string('x', 101)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public void Registrar_el_mismo_origen_y_clave_con_el_mismo_monto_es_idempotente_y_con_otro_monto_se_rechaza()
    {
        var existente = DeLinea(LineaImpresa, 50m, clave: "dev:1:1");

        Assert.Equal(ResultadoDeSolicitud.Repetida, ReglasDeReembolso.ResolverSolicitud(existente, OrigenDeReembolso.Devolucion, "dev:1:1", Importe.De(50m)));
        Assert.Equal(ResultadoDeSolicitud.MontoDistinto, ReglasDeReembolso.ResolverSolicitud(existente, OrigenDeReembolso.Devolucion, "dev:1:1", Importe.De(51m)));
        Assert.Equal(ResultadoDeSolicitud.Nueva, ReglasDeReembolso.ResolverSolicitud(null, OrigenDeReembolso.Devolucion, "dev:1:1", Importe.De(50m)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public void El_tope_por_linea_no_deja_reembolsar_mas_de_lo_cobrado_de_esa_linea()
    {
        var primero = DeLinea(LineaImpresa, 60m);
        var segundo = DeLinea(LineaImpresa, 40m);
        var tercero = DeLinea(LineaImpresa, 0.01m);

        Assert.Equal(ResultadoDeTope.Dentro, ReglasDeReembolso.ValidarTope(Cargo, Envio, Lineas, Array.Empty<Reembolso>(), primero));
        Assert.Equal(ResultadoDeTope.Dentro, ReglasDeReembolso.ValidarTope(Cargo, Envio, Lineas, new[] { primero }, segundo));
        Assert.Equal(ResultadoDeTope.SuperaLaLinea, ReglasDeReembolso.ValidarTope(Cargo, Envio, Lineas, new[] { primero, segundo }, tercero));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void El_tope_por_cargo_cuenta_los_pendientes_los_solicitados_y_los_reembolsados()
    {
        var pendiente = Reembolso.CrearDeCargo(Guid.NewGuid(), OrigenDeReembolso.Compensacion, "comp:a", PedidoId, IntentoId, Importe.De(100m), Inicio);
        var solicitado = Reembolso.CrearDeCargo(Guid.NewGuid(), OrigenDeReembolso.Compensacion, "comp:b", PedidoId, IntentoId, Importe.De(30m), Inicio);
        solicitado.Reclamar(Inicio.AddMinutes(1));
        var reembolsado = Reembolso.CrearDeCargo(Guid.NewGuid(), OrigenDeReembolso.Compensacion, "comp:c", PedidoId, IntentoId, Importe.De(10m), Inicio);
        reembolsado.Reclamar(Inicio.AddMinutes(1));
        reembolsado.MarcarReembolsado("rf_1", Inicio.AddMinutes(2));
        var nuevo = Reembolso.CrearDeCargo(Guid.NewGuid(), OrigenDeReembolso.Compensacion, "comp:d", PedidoId, IntentoId, Importe.De(0.01m), Inicio);

        Assert.Equal(ResultadoDeTope.SuperaElCargo, ReglasDeReembolso.ValidarTope(Cargo, Envio, Lineas, new[] { pendiente, solicitado, reembolsado }, nuevo));
        Assert.Equal(ResultadoDeTope.Dentro, ReglasDeReembolso.ValidarTope(Cargo, Envio, Lineas, new[] { pendiente, solicitado }, nuevo));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void El_tope_es_por_cargo_y_un_reembolso_de_otro_intento_no_lo_consume()
    {
        var otroCargo = Reembolso.CrearDeCargo(Guid.NewGuid(), OrigenDeReembolso.Compensacion, "comp:z", PedidoId, Guid.NewGuid(), Importe.De(140m), Inicio);
        var nuevo = Reembolso.CrearDeCargo(Guid.NewGuid(), OrigenDeReembolso.Compensacion, "comp:y", PedidoId, IntentoId, Cargo, Inicio);

        Assert.Equal(ResultadoDeTope.Dentro, ReglasDeReembolso.ValidarTope(Cargo, Envio, Lineas, new[] { otroCargo }, nuevo));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public void El_envio_se_reembolsa_a_lo_mas_una_vez()
    {
        var conEnvio = DeLinea(LineaImpresa, 10m, envio: 15m);
        var otroConEnvio = DeLinea(LineaDigital, 10m, envio: 15m);
        var envioMayor = DeLinea(LineaDigital, 10m, envio: 16m);

        Assert.Equal(ResultadoDeTope.Dentro, ReglasDeReembolso.ValidarTope(Cargo, Envio, Lineas, Array.Empty<Reembolso>(), conEnvio));
        Assert.Equal(ResultadoDeTope.EnvioRepetido, ReglasDeReembolso.ValidarTope(Cargo, Envio, Lineas, new[] { conEnvio }, otroConEnvio));
        Assert.Equal(ResultadoDeTope.EnvioRepetido, ReglasDeReembolso.ValidarTope(Cargo, Envio, Lineas, Array.Empty<Reembolso>(), envioMayor));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void Un_cobro_sobrante_junto_a_una_devolucion_parcial_se_topa_por_cargo_cada_uno()
    {
        var sobrante = Guid.NewGuid();
        var reembolsoDelSobrante = Reembolso.CrearDeCargo(Guid.NewGuid(), OrigenDeReembolso.Compensacion, $"comp:{sobrante}", PedidoId, sobrante, Cargo, Inicio);
        var devolucionParcial = DeLinea(LineaDigital, 25m);

        // Cada cargo tiene su propio tope: el reembolso del sobrante no resta de lo que se puede devolver del cargo del pedido.
        Assert.Equal(ResultadoDeTope.Dentro, ReglasDeReembolso.ValidarTope(Cargo, Envio, Lineas, new[] { reembolsoDelSobrante }, devolucionParcial));
        Assert.Equal(ResultadoDeTope.Dentro, ReglasDeReembolso.ValidarTope(Cargo, Envio, Lineas, new[] { devolucionParcial }, reembolsoDelSobrante));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public void Un_reembolso_no_puede_repetir_una_linea_para_eludir_el_tope_por_linea()
    {
        Assert.Throws<ArgumentException>(() => Reembolso.Crear(
            Guid.NewGuid(),
            OrigenDeReembolso.Devolucion,
            "dev:rep",
            PedidoId,
            IntentoId,
            new[] { new LineaDeReembolso(LineaDigital, Importe.De(20m)), new LineaDeReembolso(LineaDigital, Importe.De(20m)) },
            Importe.Cero,
            Inicio));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void Un_reintento_de_la_administracion_tras_sin_respuesta_tambien_espera_antes_de_pedir_otra_vez()
    {
        var reembolso = DeLinea(LineaImpresa, 10m);
        reembolso.Reclamar(Inicio.AddMinutes(1));
        reembolso.DejarPendiente(MotivoPendienteDeReembolso.SinRespuesta, Inicio.AddMinutes(1).AddSeconds(10));

        Assert.False(reembolso.Reclamar(Inicio.AddMinutes(1).AddSeconds(20)));
        Assert.Equal(1, reembolso.Intentos);
        Assert.True(reembolso.Reclamar(Inicio.AddMinutes(1).AddSeconds(31)));
        Assert.Equal(2, reembolso.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public void Una_linea_que_no_es_de_la_compra_se_rechaza()
    {
        var ajena = DeLinea(Guid.NewGuid(), 10m);

        Assert.Equal(ResultadoDeTope.LineaDesconocida, ReglasDeReembolso.ValidarTope(Cargo, Envio, Lineas, Array.Empty<Reembolso>(), ajena));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void Reclamar_es_condicional_pasa_a_solicitado_y_cuenta_el_intento_una_sola_vez()
    {
        var reembolso = DeLinea(LineaImpresa, 10m);

        Assert.True(reembolso.Reclamar(Inicio.AddMinutes(1)));
        Assert.False(reembolso.Reclamar(Inicio.AddMinutes(1).AddSeconds(1)));

        Assert.Equal(EstadoDeReembolso.Solicitado, reembolso.Estado);
        Assert.Equal(1, reembolso.Intentos);
        Assert.Equal(Inicio.AddMinutes(1), reembolso.UltimoIntentoEn);
        Assert.True(reembolso.CuentaComoPendiente);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void Un_reembolso_concluido_deja_de_ser_pendiente_y_guarda_la_referencia_de_la_pasarela()
    {
        var reembolso = DeLinea(LineaImpresa, 10m);
        reembolso.Reclamar(Inicio.AddMinutes(1));

        Assert.True(reembolso.MarcarReembolsado("rf_9", Inicio.AddMinutes(2)));

        Assert.Equal(EstadoDeReembolso.Reembolsado, reembolso.Estado);
        Assert.Equal("rf_9", reembolso.ReferenciaDePasarela);
        Assert.Equal(Inicio.AddMinutes(2), reembolso.ResueltoEn);
        Assert.False(reembolso.CuentaComoPendiente);
        Assert.False(reembolso.MarcarReembolsado("rf_otro", Inicio.AddMinutes(3)));
        Assert.Equal("rf_9", reembolso.ReferenciaDePasarela);
        Assert.False(reembolso.Reclamar(Inicio.AddMinutes(4)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void Un_reembolso_sin_respuesta_vuelve_a_pendiente_con_su_motivo_y_no_se_pide_otra_vez_a_ciegas()
    {
        var reembolso = DeLinea(LineaImpresa, 10m);
        reembolso.Reclamar(Inicio.AddMinutes(1));

        Assert.True(reembolso.DejarPendiente(MotivoPendienteDeReembolso.SinRespuesta, Inicio.AddMinutes(1).AddSeconds(10)));

        Assert.Equal(EstadoDeReembolso.Pendiente, reembolso.Estado);
        Assert.Equal(MotivoPendienteDeReembolso.SinRespuesta, reembolso.MotivoPendiente);
        Assert.True(reembolso.CuentaComoPendiente);
        // Hay que esperar más que el tiempo de espera de la pasarela más un margen antes de pedir otra vez.
        Assert.False(reembolso.PuedeReintentarse(Inicio.AddMinutes(1).AddSeconds(29)));
        Assert.True(reembolso.PuedeReintentarse(Inicio.AddMinutes(1).AddSeconds(31)));
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-17")]
    [InlineData(MotivoPendienteDeReembolso.RechazadoPorPasarela)]
    [InlineData(MotivoPendienteDeReembolso.NoSoportado)]
    public void Un_reembolso_rechazado_o_no_soportado_queda_pendiente_visible_y_lo_reintenta_la_administracion(MotivoPendienteDeReembolso motivo)
    {
        var reembolso = DeLinea(LineaImpresa, 10m);
        reembolso.Reclamar(Inicio.AddMinutes(1));
        reembolso.DejarPendiente(motivo, Inicio.AddMinutes(1).AddSeconds(2));

        Assert.True(reembolso.PuedeReintentarse(Inicio.AddMinutes(1).AddSeconds(3)));
        Assert.True(reembolso.Reclamar(Inicio.AddMinutes(2)));
        Assert.Equal(2, reembolso.Intentos);
        Assert.Null(reembolso.MotivoPendiente);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void Un_solicitado_sin_resolver_pasados_dos_minutos_es_huerfano_y_lo_reanuda_un_trabajo_consultando()
    {
        var reembolso = DeLinea(LineaImpresa, 10m);
        reembolso.Reclamar(Inicio.AddMinutes(1));

        Assert.False(reembolso.EsHuerfano(Inicio.AddMinutes(3).AddTicks(-1)));
        Assert.True(reembolso.EsHuerfano(Inicio.AddMinutes(3)));
        Assert.False(DeLinea(LineaImpresa, 10m).EsHuerfano(Inicio.AddDays(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void Dejar_pendiente_o_marcar_reembolsado_solo_aplica_a_un_reembolso_solicitado()
    {
        var reembolso = DeLinea(LineaImpresa, 10m);

        Assert.False(reembolso.DejarPendiente(MotivoPendienteDeReembolso.SinRespuesta, Inicio.AddMinutes(1)));
        Assert.False(reembolso.MarcarReembolsado("rf_1", Inicio.AddMinutes(1)));
        Assert.Equal(EstadoDeReembolso.Pendiente, reembolso.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public void El_texto_de_un_reembolso_no_lleva_datos_de_pago()
    {
        var reembolso = DeLinea(LineaImpresa, 10m);
        reembolso.Reclamar(Inicio.AddMinutes(1));
        reembolso.MarcarReembolsado("rf_secreta", Inicio.AddMinutes(2));

        Assert.DoesNotContain("rf_secreta", reembolso.ToString());
    }
}
