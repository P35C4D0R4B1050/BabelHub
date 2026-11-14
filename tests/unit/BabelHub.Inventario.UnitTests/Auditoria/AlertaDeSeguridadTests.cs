using BabelHub.Inventario.Domain.Auditoria;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>
/// <c>AlertaDeSeguridad</c> y <c>VentanaDeAgrupacion</c> (AUD-13, AUD-34, AUD-39): catálogo cerrado de motivos, ventana fija de 15 minutos desde la
/// primera ocurrencia, marca de revisada hasta el contador visto, vuelta a pendiente dentro de la ventana y apertura de una nueva después.
/// </summary>
public sealed class AlertaDeSeguridadTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly CatalogoDeMotivosDeAlerta Motivos = new(
    [
        new("ingresos-fallidos-en-exceso", ClaseDeAlerta.Seguridad),
        new("accesos-denegados-en-exceso", ClaseDeAlerta.Seguridad),
        new("correo-no-enviado", ClaseDeAlerta.Tecnica),
    ]);

    private static ResultadoDeAgrupacion Registrar(AlertaDeSeguridad? abierta, DateTimeOffset instante, string motivo = "ingresos-fallidos-en-exceso", ServicioDeOrigen servicio = ServicioDeOrigen.Ecommerce) =>
        VentanaDeAgrupacion.Registrar(abierta, Motivos, servicio, motivo, instante, Guid.NewGuid());

    private static AlertaDeSeguridad ConOcurrencias(int cuantas, string motivo = "ingresos-fallidos-en-exceso")
    {
        var resultado = Registrar(null, T0, motivo);
        var alerta = resultado.Alerta;
        for (var i = 1; i < cuantas; i++)
        {
            alerta = Registrar(alerta, T0.AddMinutes(i), motivo).Alerta;
        }

        return alerta;
    }

    // ---------- Motivos de catálogo cerrado ----------

    [Fact]
    public void ElCatalogoDeMotivosEsCerrado_BuscaPorCodigoYNoAceptaRepetidos()
    {
        Assert.Equal(ClaseDeAlerta.Tecnica, Motivos.Obtener("correo-no-enviado").Clase);
        Assert.True(Motivos.Contiene("accesos-denegados-en-exceso"));
        Assert.False(Motivos.Contiene("Accesos-Denegados"));
        Assert.False(Motivos.Contiene("texto libre de una excepcion"));
        Assert.Throws<ArgumentException>(() => new CatalogoDeMotivosDeAlerta([new("uno-dos", ClaseDeAlerta.Seguridad), new("uno-dos", ClaseDeAlerta.Tecnica)]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Mayuscula")]
    [InlineData("con espacio")]
    [InlineData("x")]
    [InlineData("a.b")]
    public void UnMotivoFueraDelFormatoDeCatalogo_SeRechaza(string codigo) =>
        Assert.Throws<ArgumentException>(() => new MotivoDeAlerta(codigo, ClaseDeAlerta.Seguridad));

    [Fact]
    public void UnMotivoQueNoEstaEnElCatalogo_NoGeneraAlerta_NuncaUnTextoLibre()
    {
        var ex = Assert.Throws<ArgumentException>(() => Registrar(null, T0, "NullReferenceException: object reference not set at C:\\src\\x.cs"));

        Assert.DoesNotContain("NullReference", ex.Message, StringComparison.Ordinal);
    }

    // ---------- Apertura y agrupación ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public void LaPrimeraOcurrenciaAbreUnaAlertaPendienteConSuCorreo()
    {
        var resultado = Registrar(null, T0);

        Assert.True(resultado.Abrio);
        Assert.True(resultado.EnviarCorreo);
        var alerta = resultado.Alerta;
        Assert.Equal(1, alerta.Ocurrencias);
        Assert.Equal(T0, alerta.PrimeraOcurrencia);
        Assert.Equal(T0, alerta.UltimaOcurrencia);
        Assert.Equal(EstadoDeAlerta.Pendiente, alerta.Estado);
        Assert.Equal(ClaseDeAlerta.Seguridad, alerta.Clase);
        Assert.Equal(ServicioDeOrigen.Ecommerce, alerta.Servicio);
        Assert.Equal(0, alerta.RevisadasHasta);
        Assert.False(alerta.EnviadaPorCorreo);
        Assert.Null(alerta.RevisadaPor);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public void LasOcurrenciasDentroDeLosQuinceMinutosDeLaPrimeraSeAgrupan_SinOtroCorreo()
    {
        var primera = Registrar(null, T0).Alerta;

        var segunda = Registrar(primera, T0.AddMinutes(1));
        var ultima = Registrar(segunda.Alerta, T0.AddMinutes(14).AddSeconds(59));

        Assert.False(segunda.Abrio);
        Assert.False(segunda.EnviarCorreo);
        Assert.Equal(primera.Id, ultima.Alerta.Id);
        Assert.Equal(3, ultima.Alerta.Ocurrencias);
        Assert.Equal(T0, ultima.Alerta.PrimeraOcurrencia);
        Assert.Equal(T0.AddMinutes(14).AddSeconds(59), ultima.Alerta.UltimaOcurrencia);
        Assert.False(ultima.EnviarCorreo);
    }

    [Theory]
    [InlineData(15, 0)]
    [InlineData(15, 1)]
    [InlineData(120, 0)]
    [Trait("Criterio", "AC-AUD-12")]
    public void UnaOcurrenciaDespuesDeLaVentanaDeQuinceMinutos_AbreUnaAlertaNuevaConSuCorreo(int minutos, int segundos)
    {
        var primera = ConOcurrencias(3);

        var nueva = Registrar(primera, T0.AddMinutes(minutos).AddSeconds(segundos));

        Assert.True(nueva.Abrio);
        Assert.True(nueva.EnviarCorreo);
        Assert.NotEqual(primera.Id, nueva.Alerta.Id);
        Assert.Equal(1, nueva.Alerta.Ocurrencias);
        Assert.Equal(T0.AddMinutes(minutos).AddSeconds(segundos), nueva.Alerta.PrimeraOcurrencia);
        Assert.Equal(EstadoDeAlerta.Pendiente, nueva.Alerta.Estado);
    }

    [Fact]
    public void LaVentanaEsFijaDesdeLaPrimera_NoSeDesplazaConLasSiguientes()
    {
        var alerta = Registrar(null, T0).Alerta;
        alerta = Registrar(alerta, T0.AddMinutes(10)).Alerta;

        // 16 minutos después de la primera, aunque solo 6 de la última: la ventana de la primera ya cerró.
        var nueva = Registrar(alerta, T0.AddMinutes(16));

        Assert.True(nueva.Abrio);
    }

    [Fact]
    public void LaVentanaDeAgrupacionEsDeQuinceMinutosYContieneSoloDesdeLaPrimeraHastaAntesDelCierre()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), VentanaDeAgrupacion.Duracion);
        Assert.True(VentanaDeAgrupacion.Contiene(T0, T0));
        Assert.True(VentanaDeAgrupacion.Contiene(T0, T0.AddMinutes(15).AddTicks(-1)));
        Assert.False(VentanaDeAgrupacion.Contiene(T0, T0.AddMinutes(15)));
        Assert.False(VentanaDeAgrupacion.Contiene(T0, T0.AddTicks(-1)));
    }

    [Fact]
    public void ElMismoMotivoDeOtroServicioNoSeAgrupa_CadaServicioLlevaLaSuya()
    {
        var eco = Registrar(null, T0).Alerta;

        var inv = Registrar(eco, T0.AddMinutes(1), servicio: ServicioDeOrigen.Inventario);

        Assert.True(inv.Abrio);
        Assert.StartsWith("inv:", inv.Alerta.Id, StringComparison.Ordinal);
        Assert.StartsWith("eco:", eco.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void ElMismoServicioConOtroMotivoNoSeAgrupa()
    {
        var uno = Registrar(null, T0).Alerta;

        var otro = Registrar(uno, T0.AddMinutes(1), "accesos-denegados-en-exceso");

        Assert.True(otro.Abrio);
        Assert.Equal("accesos-denegados-en-exceso", otro.Alerta.Motivo);
    }

    [Fact]
    public void UnaOcurrenciaMuyAnteriorALaPrimera_SeRechaza_PorUnRelojRoto() =>
        Assert.Throws<ArgumentException>(() => Registrar(ConOcurrencias(1), T0.AddMinutes(-10)));

    [Fact]
    public void LasOcurrenciasNoDebenIrHaciaAtras_DentroDeLaVentana()
    {
        var alerta = Registrar(Registrar(null, T0).Alerta, T0.AddMinutes(5)).Alerta;

        var antes = Registrar(alerta, T0.AddMinutes(2));

        Assert.Equal(3, antes.Alerta.Ocurrencias);
        Assert.Equal(T0.AddMinutes(5), antes.Alerta.UltimaOcurrencia);
    }

    [Fact]
    public void ElContadorEstaAcotado_SinDesbordarNiPerderLaAlerta()
    {
        var alerta = ConOcurrencias(1);
        for (var i = 1; i < AlertaDeSeguridad.TopeDeOcurrencias + 5; i++)
        {
            alerta = Registrar(alerta, T0.AddMinutes(1)).Alerta;
        }

        var ultima = Registrar(alerta, T0.AddMinutes(2)).Alerta;

        Assert.Equal(AlertaDeSeguridad.TopeDeOcurrencias, alerta.Ocurrencias);
        Assert.Equal(AlertaDeSeguridad.TopeDeOcurrencias, ultima.Ocurrencias);
        Assert.Equal(T0.AddMinutes(2), ultima.UltimaOcurrencia);
    }

    [Fact]
    public void ElIdentificadorLlevaElPrefijoDelServicio()
    {
        var id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        Assert.Equal("eco:aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", AlertaDeSeguridad.NuevoId(ServicioDeOrigen.Ecommerce, id));
        Assert.Equal("inv:aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", AlertaDeSeguridad.NuevoId(ServicioDeOrigen.Inventario, id));
        Assert.Throws<ArgumentException>(() => AlertaDeSeguridad.NuevoId(ServicioDeOrigen.Ecommerce, Guid.Empty));
    }

    [Fact]
    public void UnaOcurrenciaQueLlegaEnDesordenUnPocoAntesDeLaPrimera_SeTomaComoOcurridaEnLaPrimera_SinPerderla()
    {
        var alerta = Registrar(null, T0).Alerta;

        var temprana = Registrar(alerta, T0.AddMinutes(-4));

        Assert.False(temprana.Abrio);
        Assert.Equal(2, temprana.Alerta.Ocurrencias);
        Assert.Equal(T0, temprana.Alerta.PrimeraOcurrencia);
        Assert.Equal(T0, temprana.Alerta.UltimaOcurrencia);
        Assert.Throws<ArgumentException>(() => Registrar(alerta, T0.AddMinutes(-6)));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public void UnaMarcaQueNoAvanzaLoRevisado_NoCambiaQuienRevisoNiCuando()
    {
        var otro = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var porAdmin = ConOcurrencias(5).MarcarRevisada(3, Admin, T0.AddMinutes(6));

        var igual = porAdmin.MarcarRevisada(1, otro, T0.AddMinutes(7));
        var avanza = porAdmin.MarcarRevisada(4, otro, T0.AddMinutes(8));

        Assert.Same(porAdmin, igual);
        Assert.Equal(Admin, igual.RevisadaPor);
        Assert.Equal(otro, avanza.RevisadaPor);
        Assert.Equal(4, avanza.RevisadasHasta);
    }

    [Fact]
    public void EnElTopeDelContador_UnaOcurrenciaNuevaSigueSiendoPendiente_NoQuedaOcultaTrasRevisarTodo()
    {
        var alerta = ConOcurrencias(1);
        for (var i = 1; i < AlertaDeSeguridad.TopeDeOcurrencias + 1; i++)
        {
            alerta = Registrar(alerta, T0.AddMinutes(1)).Alerta;
        }

        var revisada = alerta.MarcarRevisada(AlertaDeSeguridad.TopeDeOcurrencias, Admin, T0.AddMinutes(2));
        Assert.Equal(EstadoDeAlerta.Revisada, revisada.Estado);

        var nueva = Registrar(revisada, T0.AddMinutes(3)).Alerta;

        Assert.Equal(EstadoDeAlerta.Pendiente, nueva.Estado);
    }

    [Fact]
    public void ElServicioDelIdentificadorDebeSerUnValorDefinido() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AlertaDeSeguridad.NuevoId((ServicioDeOrigen)9, Guid.NewGuid()));

    // ---------- Marcar como revisada (AUD-34) ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public void AlMarcarComoRevisada_SeMarcanLasOcurrenciasHastaElContadorQueVeia_YLaCuartaQuedaPendiente()
    {
        // Tres ocurrencias agrupadas y una cuarta que llega mientras el administrador la mira.
        var alerta = ConOcurrencias(3);
        var conCuarta = Registrar(alerta, T0.AddMinutes(4)).Alerta;

        var revisada = conCuarta.MarcarRevisada(contadorVisto: 3, por: Admin, T0.AddMinutes(5));

        Assert.Equal(3, revisada.RevisadasHasta);
        Assert.Equal(4, revisada.Ocurrencias);
        Assert.Equal(1, revisada.OcurrenciasSinRevisar);
        Assert.Equal(EstadoDeAlerta.Pendiente, revisada.Estado);
        Assert.Equal(Admin, revisada.RevisadaPor);
        Assert.Equal(T0.AddMinutes(5), revisada.RevisadaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public void SiElContadorVistoEsElActual_LaAlertaQuedaRevisada()
    {
        var revisada = ConOcurrencias(3).MarcarRevisada(3, Admin, T0.AddMinutes(5));

        Assert.Equal(EstadoDeAlerta.Revisada, revisada.Estado);
        Assert.Equal(0, revisada.OcurrenciasSinRevisar);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(1000)]
    [Trait("Criterio", "AC-AUD-12")]
    public void ElContadorVistoNuncaPuedeSerMayorQueElActual_NiMenorQueUno(int visto) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ConOcurrencias(3).MarcarRevisada(visto, Admin, T0.AddMinutes(5)));

    [Fact]
    public void MarcarRevisadaNoCambiaLaAlertaOriginal_SiNoSePuedeAuditarLaMarcaSeConservaLaPendiente()
    {
        var alerta = ConOcurrencias(3);

        var revisada = alerta.MarcarRevisada(3, Admin, T0.AddMinutes(5));

        // La aplicación solo guarda `revisada` si el registro de la marca se pudo escribir; si no, sigue `alerta` (AUD-34).
        Assert.Equal(EstadoDeAlerta.Pendiente, alerta.Estado);
        Assert.Equal(0, alerta.RevisadasHasta);
        Assert.Null(alerta.RevisadaPor);
        Assert.Equal(EstadoDeAlerta.Revisada, revisada.Estado);
    }

    [Fact]
    public void UnaMarcaMenorQueUnaAnteriorNoRetrocedeLoRevisado()
    {
        var alerta = ConOcurrencias(5).MarcarRevisada(4, Admin, T0.AddMinutes(6));

        var otra = alerta.MarcarRevisada(2, Admin, T0.AddMinutes(7));

        Assert.Equal(4, otra.RevisadasHasta);
    }

    // ---------- Vuelta a pendiente (AUD-39) ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public void UnaOcurrenciaALos3MinutosDeLaMarca_DentroDeLaVentana_LaVuelveAPendiente_SinOtroCorreo()
    {
        var revisada = ConOcurrencias(3).MarcarRevisada(3, Admin, T0.AddMinutes(5));

        var resultado = Registrar(revisada, T0.AddMinutes(8));

        Assert.False(resultado.Abrio);
        Assert.False(resultado.EnviarCorreo);
        Assert.Equal(revisada.Id, resultado.Alerta.Id);
        Assert.Equal(EstadoDeAlerta.Pendiente, resultado.Alerta.Estado);
        Assert.Equal(4, resultado.Alerta.Ocurrencias);
        Assert.Equal(3, resultado.Alerta.RevisadasHasta);
        Assert.Equal(1, resultado.Alerta.OcurrenciasSinRevisar);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public void UnaOcurrenciaPosteriorALaVentana_AbreUnaAlertaNuevaConSuCorreo_AunqueLaAnteriorEstuvieraRevisada()
    {
        var revisada = ConOcurrencias(3).MarcarRevisada(3, Admin, T0.AddMinutes(5));

        var resultado = Registrar(revisada, T0.AddMinutes(20));

        Assert.True(resultado.Abrio);
        Assert.True(resultado.EnviarCorreo);
        Assert.NotEqual(revisada.Id, resultado.Alerta.Id);
        Assert.Equal(EstadoDeAlerta.Pendiente, resultado.Alerta.Estado);
        Assert.Equal(EstadoDeAlerta.Revisada, revisada.Estado);
    }

    // ---------- Correo ----------

    [Fact]
    public void LaAlertaSeMarcaComoEnviadaPorCorreoUnaSolaVez()
    {
        var alerta = ConOcurrencias(1);

        var enviada = alerta.MarcarComoEnviada();

        Assert.True(enviada.EnviadaPorCorreo);
        Assert.False(alerta.EnviadaPorCorreo);
        Assert.Same(enviada, enviada.MarcarComoEnviada());
    }

    [Fact]
    public void LasAlertasTecnicasNoSonDeSeguridad_YNoTienenPantalla()
    {
        var tecnica = Registrar(null, T0, "correo-no-enviado").Alerta;

        Assert.Equal(ClaseDeAlerta.Tecnica, tecnica.Clase);
        Assert.False(tecnica.TienePantalla);
        Assert.True(ConOcurrencias(1).TienePantalla);
    }

    [Fact]
    public void ElCatalogoNoPuedeSerNuloNiLaAlertaAbiertaDeOtraClase()
    {
        Assert.Throws<ArgumentNullException>(() => VentanaDeAgrupacion.Registrar(null, null!, ServicioDeOrigen.Ecommerce, "correo-no-enviado", T0, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => VentanaDeAgrupacion.Registrar(null, Motivos, ServicioDeOrigen.Ecommerce, "correo-no-enviado", T0, Guid.Empty));
    }

    [Fact]
    public void LaFechaDeLaOcurrenciaDebeEstarEnUtc() =>
        Assert.Throws<ArgumentException>(() => Registrar(null, new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(-5))));
}

/// <summary>El motivo «no enviable por correo» y la restauración desde lo persistido (T10 y T18: una alerta originada por el propio correo no se envía por correo, AUD-29).</summary>
public sealed class MotivosDeAlertaEnviablesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Criterio", "AC-AUD-26")]
    public void UnMotivoEsEnviablePorCorreoSalvoQueSeDeclareLoContrario()
    {
        Assert.True(new MotivoDeAlerta("trabajo-de-fondo-detenido", ClaseDeAlerta.Tecnica).EnviablePorCorreo);
        Assert.False(new MotivoDeAlerta("correo-de-alertas-no-disponible", ClaseDeAlerta.Tecnica, enviablePorCorreo: false).EnviablePorCorreo);
    }

    [Fact]
    public void RestaurarReconstruyeLaAlertaPersistida_ConSusMarcas()
    {
        var id = AlertaDeSeguridad.NuevoId(ServicioDeOrigen.Inventario, Guid.NewGuid());

        var alerta = AlertaDeSeguridad.Restaurar(id, ServicioDeOrigen.Inventario, "accesos-denegados", ClaseDeAlerta.Seguridad, T0, T0.AddMinutes(3), 4, 2, Guid.NewGuid(), T0.AddMinutes(4), enviadaPorCorreo: true);

        Assert.Equal(id, alerta.Id);
        Assert.Equal(2, alerta.OcurrenciasSinRevisar);
        Assert.Equal(EstadoDeAlerta.Pendiente, alerta.Estado);
        Assert.True(alerta.EnviadaPorCorreo);
    }

    [Theory]
    [InlineData("eco:no-es-un-guid")]
    [InlineData("inv:11111111-1111-1111-1111-111111111111")]
    public void Restaurar_RechazaUnIdentificadorSinElPrefijoDeSuServicioOSinGuid(string id)
    {
        Assert.Throws<ArgumentException>(() =>
            AlertaDeSeguridad.Restaurar(id, ServicioDeOrigen.Ecommerce, "accesos-denegados", ClaseDeAlerta.Seguridad, T0, T0, 1, 0, null, null, false));
    }

    [Fact]
    public void Restaurar_RechazaContadoresIncoherentesOFechasQueNoEstanEnUtc()
    {
        var id = AlertaDeSeguridad.NuevoId(ServicioDeOrigen.Ecommerce, Guid.NewGuid());

        Assert.Throws<ArgumentOutOfRangeException>(() => AlertaDeSeguridad.Restaurar(id, ServicioDeOrigen.Ecommerce, "accesos-denegados", ClaseDeAlerta.Seguridad, T0, T0, 2, 3, null, null, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlertaDeSeguridad.Restaurar(id, ServicioDeOrigen.Ecommerce, "accesos-denegados", ClaseDeAlerta.Seguridad, T0, T0, 0, 0, null, null, false));
        Assert.Throws<ArgumentException>(() => AlertaDeSeguridad.Restaurar(id, ServicioDeOrigen.Ecommerce, "accesos-denegados", ClaseDeAlerta.Seguridad, T0.ToOffset(TimeSpan.FromHours(-5)), T0, 1, 0, null, null, false));
        Assert.Throws<ArgumentException>(() => AlertaDeSeguridad.Restaurar(id, ServicioDeOrigen.Ecommerce, "accesos-denegados", ClaseDeAlerta.Seguridad, T0, T0.AddMinutes(-1), 1, 0, null, null, false));
    }
}
