using BabelHub.Ecommerce.Domain.Auditoria;

namespace BabelHub.Ecommerce.UnitTests.Auditoria;

/// <summary>
/// <c>AcumuladoDeLimite</c> (AUD-28, AUD-41): ventana de 5 minutos alineada al reloj, clave por huella, un solo evento por usuario u origen, límite y
/// ventana, agrupación en «otros» sobre 1000 filas, sin perder ni duplicar rechazos (tampoco tras un reinicio).
/// </summary>
public sealed class AcumuladoDeLimiteTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private const string Huella = "h-origen-0001";
    private const string Limite = "ingreso-por-origen";
    private const string Direccion = "200.37.12.4";

    private static AcumuladoDeLimite Nuevo() => new(VentanaDeLimite.Que(Inicio.AddSeconds(30)));

    private static void Rechazar(AcumuladoDeLimite acumulado, int veces, string huella = Huella, string limite = Limite, string? direccion = Direccion)
    {
        for (var i = 0; i < veces; i++)
        {
            acumulado.Registrar(huella, limite, Inicio.AddSeconds(30 + (i % 200)), direccion);
        }
    }

    // ---------- La ventana ----------

    [Theory]
    [InlineData("2026-11-14T12:00:00Z", "2026-11-14T12:00:00Z")]
    [InlineData("2026-11-14T12:03:27Z", "2026-11-14T12:00:00Z")]
    [InlineData("2026-11-14T12:04:59.9999999Z", "2026-11-14T12:00:00Z")]
    [InlineData("2026-11-14T12:05:00Z", "2026-11-14T12:05:00Z")]
    [InlineData("2026-11-14T23:59:59Z", "2026-11-14T23:55:00Z")]
    [Trait("Criterio", "AC-AUD-25")]
    public void LaVentanaEsDeCincoMinutosAlineadaAlRelojUtc(string instante, string inicio)
    {
        var ventana = VentanaDeLimite.Que(DateTimeOffset.Parse(instante, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(DateTimeOffset.Parse(inicio, System.Globalization.CultureInfo.InvariantCulture), ventana.Inicio);
        Assert.Equal(ventana.Inicio.AddMinutes(5), ventana.Cierre);
        Assert.Equal(TimeSpan.FromMinutes(5), VentanaDeLimite.Duracion);
    }

    [Fact]
    public void LaVentanaExigeUtc() =>
        Assert.Throws<ArgumentException>(() => VentanaDeLimite.Que(new DateTimeOffset(2026, 11, 14, 7, 3, 0, TimeSpan.FromHours(-5))));

    [Fact]
    public void LaVentanaSeAlineaIgualEnHoraDeLima_PorqueElDesfaseEsDeHorasEnteras()
    {
        var utc = VentanaDeLimite.Que(new DateTimeOffset(2026, 11, 14, 17, 3, 0, TimeSpan.Zero));

        Assert.Equal(0, utc.Inicio.ToOffset(HoraDeLima.Desfase).Minute % 5);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 17, 0, 0, TimeSpan.Zero), utc.Inicio);
    }

    [Fact]
    public void UnRechazoSoloEntraEnLaVentanaQueLeCorresponde()
    {
        var acumulado = Nuevo();

        Assert.Throws<ArgumentException>(() => acumulado.Registrar(Huella, Limite, Inicio.AddMinutes(5), Direccion));
        Assert.Throws<ArgumentException>(() => acumulado.Registrar(Huella, Limite, Inicio.AddTicks(-1), Direccion));
        Assert.Equal(ResultadoDeRegistroDeRechazo.Acumulado, acumulado.Registrar(Huella, Limite, Inicio.AddMinutes(5).AddTicks(-1), Direccion));
    }

    // ---------- Un solo evento por huella, límite y ventana ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public void CincuentaRechazosDelMismoOrigenPorElMismoLimite_SonUnaSolaFila()
    {
        var acumulado = Nuevo();

        Rechazar(acumulado, 50);

        var fila = Assert.Single(acumulado.Filas);
        Assert.Equal(50, fila.Rechazos);
        Assert.Equal(Direccion, fila.DireccionCompleta);
        Assert.Equal(50, acumulado.TotalDeRechazos);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public void ConOtroOrigenOOtroLimite_SonFilasDistintas_YCadaUnaDaSuEvento()
    {
        var acumulado = Nuevo();
        Rechazar(acumulado, 3, "h-uno-0001", "ingreso-por-origen", "10.0.0.1");
        Rechazar(acumulado, 2, "h-dos-0002", "ingreso-por-origen", "10.0.0.2");
        Rechazar(acumulado, 4, "h-uno-0001", "recuperacion-por-origen", "10.0.0.1");

        var eventos = acumulado.Cerrar(Inicio.AddMinutes(6));

        Assert.Equal(3, eventos.Count);
        Assert.Equal(3, eventos.Single(e => e.Huella == "h-uno-0001" && e.Limite == "ingreso-por-origen").Rechazos);
        Assert.Equal(2, eventos.Single(e => e.Huella == "h-dos-0002").Rechazos);
        Assert.Equal(4, eventos.Single(e => e.Limite == "recuperacion-por-origen").Rechazos);
        Assert.Equal(eventos.Count, eventos.Select(e => (e.Huella, e.Limite)).Distinct().Count());
    }

    // ---------- Sin perder rechazos tras un reinicio ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public void CincuentaRechazosUnReinicioYVeinteMas_DanUnSoloEventoConElTotalDeSetenta()
    {
        var antes = Nuevo();
        Rechazar(antes, 50);
        var persistido = antes.Filas.ToList();

        // El servicio se reinicia: se recupera lo persistido y siguen llegando rechazos de la misma ventana.
        var despues = AcumuladoDeLimite.Restaurar(antes.Ventana, persistido, cerrada: false);
        Rechazar(despues, 20);
        var eventos = despues.Cerrar(Inicio.AddMinutes(5).AddSeconds(10));

        var evento = Assert.Single(eventos);
        Assert.Equal(70, evento.Rechazos);
        Assert.Equal(Direccion, evento.DireccionCompleta);
        Assert.Equal(Inicio.AddMinutes(5), evento.Fecha);
    }

    [Fact]
    public void LoRestauradoSeValida_UnaFilaSinRechazosOCerradaConDireccionNoSeAcepta()
    {
        var ventana = VentanaDeLimite.Que(Inicio);

        Assert.Throws<ArgumentException>(() => AcumuladoDeLimite.Restaurar(ventana, [new(Huella, Limite, 0, null)], cerrada: false));
        Assert.Throws<ArgumentException>(() => AcumuladoDeLimite.Restaurar(ventana, [new(Huella, Limite, 1, Direccion)], cerrada: true));
        Assert.Throws<ArgumentException>(() => AcumuladoDeLimite.Restaurar(ventana, [new(Huella, Limite, 1, null), new(Huella, Limite, 2, null)], cerrada: false));
    }

    // ---------- El cierre ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public void ElEventoSeFechaEnElCierreDeLaVentana_ConElTotalYLaDireccionCompleta()
    {
        var acumulado = Nuevo();
        Rechazar(acumulado, 50);

        var evento = Assert.Single(acumulado.Cerrar(Inicio.AddMinutes(5).AddSeconds(11)));

        Assert.Equal(Inicio.AddMinutes(5), evento.Fecha);
        Assert.Equal(50, evento.Rechazos);
        Assert.Equal(Direccion, evento.DireccionCompleta);
        Assert.Equal(Huella, evento.Huella);
        Assert.Equal(Limite, evento.Limite);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(299, false)]
    [InlineData(309, false)]
    [InlineData(310, true)]
    [InlineData(400, true)]
    [Trait("Criterio", "AC-AUD-25")]
    public void LaVentanaSoloSeCierraDespuesDeUnMargenDeDiezSegundos(int segundosDesdeElInicio, bool puede)
    {
        var acumulado = Nuevo();

        Assert.Equal(puede, acumulado.PuedeCerrarse(Inicio.AddSeconds(segundosDesdeElInicio)));
        Assert.Equal(TimeSpan.FromSeconds(10), VentanaDeLimite.MargenDeCierre);
        if (!puede)
        {
            Assert.Throws<InvalidOperationException>(() => acumulado.Cerrar(Inicio.AddSeconds(segundosDesdeElInicio)));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public void AlCerrar_LaDireccionCompletaSeBorra_ConservandoLaHuellaYElTotal()
    {
        var acumulado = Nuevo();
        Rechazar(acumulado, 5);

        _ = acumulado.Cerrar(Inicio.AddMinutes(6));

        Assert.True(acumulado.Cerrada);
        var fila = Assert.Single(acumulado.Filas);
        Assert.Null(fila.DireccionCompleta);
        Assert.Equal(Huella, fila.Huella);
        Assert.Equal(5, fila.Rechazos);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public void CerrarDosVeces_NoDuplicaElEvento()
    {
        var acumulado = Nuevo();
        Rechazar(acumulado, 5);

        var primera = acumulado.Cerrar(Inicio.AddMinutes(6));
        var segunda = acumulado.Cerrar(Inicio.AddMinutes(7));

        Assert.Single(primera);
        Assert.Empty(segunda);
    }

    [Fact]
    public void UnRechazoDeUnaVentanaYaCerrada_NoSeAcumula_LoAvisaParaQueSeEscribaAparte()
    {
        var acumulado = Nuevo();
        Rechazar(acumulado, 2);
        _ = acumulado.Cerrar(Inicio.AddMinutes(6));

        var resultado = acumulado.Registrar(Huella, Limite, Inicio.AddSeconds(40), Direccion);

        Assert.Equal(ResultadoDeRegistroDeRechazo.VentanaCerrada, resultado);
        Assert.Equal(2, acumulado.TotalDeRechazos);
    }

    [Fact]
    public void SiLaAuditoriaSigueCaidaTrasLosReintentos_SePuedeBorrarLaDireccionConservandoSoloLaHuella()
    {
        var acumulado = Nuevo();
        Rechazar(acumulado, 4);

        acumulado.BorrarDirecciones();

        var fila = Assert.Single(acumulado.Filas);
        Assert.Null(fila.DireccionCompleta);
        Assert.Equal(4, fila.Rechazos);
        Assert.False(acumulado.Cerrada);
    }

    // ---------- Tope de filas y «otros» ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public void SobreMilFilas_LosOrigenesNuevosSeAgrupanEnOtros_SinPerderRechazos()
    {
        var acumulado = Nuevo();
        for (var i = 0; i < 1500; i++)
        {
            acumulado.Registrar($"h-origen-{i:D5}", Limite, Inicio.AddSeconds(10), $"10.1.{i / 250}.{i % 250}");
        }

        Assert.Equal(AcumuladoDeLimite.TopeDeFilas, acumulado.Filas.Count);
        Assert.Equal(1500, acumulado.TotalDeRechazos);
        var otros = acumulado.Filas.Single(f => f.Huella == AcumuladoDeLimite.HuellaDeOtros);
        Assert.Equal(1500 - (AcumuladoDeLimite.TopeDeFilas - 1), otros.Rechazos);
        Assert.Null(otros.DireccionCompleta);
        Assert.Equal(AcumuladoDeLimite.LimiteDeOtros, otros.Limite);
    }

    [Fact]
    public void UnOrigenYaRegistradoSigueSumandoEnSuFila_AunqueLaVentanaYaEsteLlena()
    {
        var acumulado = Nuevo();
        for (var i = 0; i < 1200; i++)
        {
            acumulado.Registrar($"h-origen-{i:D5}", Limite, Inicio.AddSeconds(10), null);
        }

        var resultado = acumulado.Registrar("h-origen-00007", Limite, Inicio.AddSeconds(20), null);

        Assert.Equal(ResultadoDeRegistroDeRechazo.Acumulado, resultado);
        Assert.Equal(2, acumulado.Filas.Single(f => f.Huella == "h-origen-00007").Rechazos);
        Assert.Equal(AcumuladoDeLimite.TopeDeFilas, acumulado.Filas.Count);
    }

    [Fact]
    public void ElAgrupadoEnOtros_AvisaQueSeAgrupo_YDaUnEventoConElTotalSinDireccion()
    {
        var acumulado = Nuevo();
        for (var i = 0; i < 999; i++)
        {
            acumulado.Registrar($"h-origen-{i:D5}", Limite, Inicio.AddSeconds(10), "10.0.0.1");
        }

        var resultado = acumulado.Registrar("h-nuevo-99999", Limite, Inicio.AddSeconds(11), "10.0.0.2");
        _ = acumulado.Registrar("h-nuevo-88888", "recuperacion-por-origen", Inicio.AddSeconds(12), "10.0.0.3");
        var eventos = acumulado.Cerrar(Inicio.AddMinutes(6));

        Assert.Equal(ResultadoDeRegistroDeRechazo.AgrupadoEnOtros, resultado);
        Assert.Equal(1000, eventos.Count);
        var otros = eventos.Single(e => e.Huella == AcumuladoDeLimite.HuellaDeOtros);
        Assert.Equal(2, otros.Rechazos);
        Assert.Null(otros.DireccionCompleta);
        Assert.Equal(1001, eventos.Sum(e => e.Rechazos));
    }

    // ---------- Validación y purga ----------

    [Theory]
    [InlineData("", "ingreso-por-origen")]
    [InlineData("corta", "ingreso-por-origen")]
    [InlineData("con espacio aqui", "ingreso-por-origen")]
    [InlineData("h-origen-0001", "")]
    [InlineData("h-origen-0001", "Con Mayuscula")]
    [InlineData("otros", "ingreso-por-origen")]
    public void LaHuellaYElLimiteDebenSerValidos_YLaHuellaDeOtrosEstaReservada(string huella, string limite)
    {
        var acumulado = Nuevo();

        Assert.Throws<ArgumentException>(() => acumulado.Registrar(huella, limite, Inicio.AddSeconds(5), null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("con espacio")]
    public void UnaDireccionInvalidaSeRechaza(string direccion)
    {
        var acumulado = Nuevo();

        Assert.Throws<ArgumentException>(() => acumulado.Registrar(Huella, Limite, Inicio.AddSeconds(5), direccion));
        Assert.Throws<ArgumentException>(() => acumulado.Registrar(Huella, Limite, Inicio.AddSeconds(5), new string('1', 65)));
    }

    [Fact]
    public void SiLlegaConDireccionDespuesDeUnaSinDireccion_SeCompletaLaFila_PeroNuncaSeCambiaLaQueYaEstaba()
    {
        var acumulado = Nuevo();

        acumulado.Registrar(Huella, Limite, Inicio.AddSeconds(5), null);
        acumulado.Registrar(Huella, Limite, Inicio.AddSeconds(6), "10.0.0.1");
        acumulado.Registrar(Huella, Limite, Inicio.AddSeconds(7), "10.0.0.2");

        var fila = Assert.Single(acumulado.Filas);
        Assert.Equal(3, fila.Rechazos);
        Assert.Equal("10.0.0.1", fila.DireccionCompleta);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(24 * 3600 - 1, false)]
    [InlineData(24 * 3600, true)]
    [InlineData(48 * 3600, true)]
    [Trait("Criterio", "AC-AUD-25")]
    public void LaFilaCerradaSePurgaALas24HorasDelCierre_NuncaAntes_NiLaAbierta(int segundosDespuesDelCierre, bool purgable)
    {
        var acumulado = Nuevo();
        Rechazar(acumulado, 1);
        var cierre = acumulado.Ventana.Cierre;
        Assert.False(acumulado.EsPurgable(cierre.AddHours(30)));

        _ = acumulado.Cerrar(cierre.AddSeconds(10));

        Assert.Equal(purgable, acumulado.EsPurgable(cierre.AddSeconds(segundosDespuesDelCierre)));
        Assert.Equal(TimeSpan.FromHours(24), AcumuladoDeLimite.RetencionTrasElCierre);
    }

    [Fact]
    public void LasFilasQueSeLeenNoSePuedenAlterarDesdeAfuera()
    {
        var acumulado = Nuevo();
        Rechazar(acumulado, 2);
        var copia = acumulado.Filas;

        Rechazar(acumulado, 3);

        Assert.Equal(2, copia.Single().Rechazos);
        Assert.Equal(5, acumulado.Filas.Single().Rechazos);
    }
}
