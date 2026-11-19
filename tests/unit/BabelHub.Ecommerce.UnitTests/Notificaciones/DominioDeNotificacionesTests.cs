using BabelHub.Ecommerce.Domain.Notificaciones;

namespace BabelHub.Ecommerce.UnitTests.Notificaciones;

/// <summary>
/// El Dominio de los avisos (transversal-notificaciones T01 y T02; AC-NOT-05, AC-NOT-06, AC-NOT-09, AC-NOT-12, AC-NOT-15; NOT-04, NOT-06, NOT-07, NOT-10): la máquina de estados, la política de reintentos, las plantillas
/// con campos cerrados y tipados y el número de destino normalizado a E.164.
/// </summary>
public sealed class DominioDeNotificacionesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    private static Aviso Nuevo(TipoDeDestinatario tipo = TipoDeDestinatario.Cliente, string plantilla = "pedido-listo-para-recojo") =>
        Aviso.Crear(Guid.NewGuid(), "pedido:123:listo-para-recojo", tipo == TipoDeDestinatario.Cliente ? Guid.NewGuid().ToString("D") : "AYACUCHO", tipo, plantilla, 1, new Dictionary<string, string> { ["numeroDePedido"] = "BH-000123" }, Ahora);

    // ---------------------------------------------------------------- el aviso y su máquina de estados (T01)

    [Fact]
    [Trait("Criterio", "AC-NOT-05")]
    public void UnAvisoNuevo_EstaPendiente_SinIntentos_ConSuFechaYSusCampos_YVenceA24Horas()
    {
        var aviso = Nuevo();

        Assert.Equal(EstadoDelAviso.Pendiente, aviso.Estado);
        Assert.Equal(0, aviso.Intentos);
        Assert.Equal(Ahora, aviso.ProximaFecha);
        Assert.Equal(Ahora, aviso.Creado);
        Assert.Null(aviso.FechaFinal);
        Assert.Equal("BH-000123", aviso.Campos["numeroDePedido"]);
        Assert.Equal(Ahora.AddHours(24), aviso.VenceEn);
        Assert.False(aviso.EsFinal);
    }

    [Fact]
    public void UnAvisoSinConsentimiento_NaceOmitido_ConSuEstadoYSuFecha_SinCampos()
    {
        var aviso = Aviso.OmitidoSinConsentimiento(Guid.NewGuid(), "pedido:1:entregado", Guid.NewGuid().ToString("D"), TipoDeDestinatario.Cliente, "pedido-entregado", 1, "sin-consentimiento", Ahora);

        Assert.Equal(EstadoDelAviso.OmitidoSinConsentimiento, aviso.Estado);
        Assert.Equal(Ahora, aviso.FechaFinal);
        Assert.Equal("sin-consentimiento", aviso.MotivoDeCierre);
        Assert.Empty(aviso.Campos);
        Assert.True(aviso.EsFinal);
    }

    [Theory]
    [InlineData("no-es-un-evento")]
    [InlineData("pedido:")]
    [InlineData("Pedido:1:listo")]
    [InlineData("pedido:1:listo para recojo")]
    [InlineData("pedido:1:2:3:4:5")]
    public void UnEventoSinElFormatoCerrado_SeRechaza(string evento)
    {
        Assert.False(PlantillaDeAviso.EventoValido(evento));
        Assert.Throws<ArgumentException>(() => Aviso.Crear(Guid.NewGuid(), evento, "AYACUCHO", TipoDeDestinatario.Sucursal, "traslado-cambio-de-estado", 1, new Dictionary<string, string>(), Ahora));
    }

    [Theory]
    [InlineData("+51999111222")]
    [InlineData("999111222")]
    [InlineData("a")]
    [InlineData("persona@example.com")]
    [InlineData("con espacios")]
    public void UnDestinatarioQueNoEsUnaReferenciaOpaca_SeRechaza(string referencia)
    {
        Assert.False(PlantillaDeAviso.ReferenciaValida(referencia));
        Assert.Throws<ArgumentException>(() => Aviso.Crear(Guid.NewGuid(), "pedido:1:entregado", referencia, TipoDeDestinatario.Cliente, "pedido-entregado", 1, new Dictionary<string, string>(), Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-05")]
    public void MarcarEnviado_EsFinal_ConLaFecha_VaciaLosCampos_YNoCambiaDespues()
    {
        var aviso = Nuevo();
        Assert.True(aviso.IniciarEnvio(Ahora, Ahora.AddMinutes(2)));

        Assert.True(aviso.MarcarEnviado(Ahora.AddSeconds(5)));

        Assert.Equal(EstadoDelAviso.Enviado, aviso.Estado);
        Assert.Equal(Ahora.AddSeconds(5), aviso.FechaFinal);
        Assert.Empty(aviso.Campos);
        Assert.False(aviso.Cancelar("motivo", Ahora.AddMinutes(1)));
        Assert.False(aviso.FalloElIntento(Ahora.AddMinutes(1)));
        Assert.False(aviso.OmitirPorTope(Ahora.AddMinutes(1)));
        Assert.Equal(EstadoDelAviso.Enviado, aviso.Estado);
    }

    [Fact]
    public void SoloSeIniciaElEnvioDeUnPendienteCuyaFechaLlego()
    {
        var aviso = Nuevo();
        aviso.FalloElIntento(Ahora);

        Assert.False(aviso.IniciarEnvio(Ahora, Ahora.AddMinutes(2)));
        Assert.True(aviso.IniciarEnvio(aviso.ProximaFecha, aviso.ProximaFecha.AddMinutes(2)));
        Assert.Equal(EstadoDelAviso.Enviando, aviso.Estado);
        Assert.False(aviso.IniciarEnvio(aviso.ProximaFecha, aviso.ProximaFecha.AddMinutes(2)));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-12")]
    public void ElIntentoInicialYCincoReintentos_ConEsperasDe1_5_15_30_Y60Minutos_ElSextoFalloQuedaFallido()
    {
        var aviso = Nuevo();
        var ahora = Ahora;
        var esperas = new List<TimeSpan>();

        for (var i = 0; i < 5; i++)
        {
            Assert.True(aviso.FalloElIntento(ahora));
            Assert.Equal(EstadoDelAviso.Pendiente, aviso.Estado);
            esperas.Add(aviso.ProximaFecha - ahora);
            ahora = aviso.ProximaFecha;
        }

        Assert.Equal([TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(60)], esperas);
        Assert.Equal(5, aviso.Intentos);

        aviso.FalloElIntento(ahora);

        Assert.Equal(EstadoDelAviso.Fallido, aviso.Estado);
        Assert.Equal("reintentos-agotados", aviso.MotivoDeCierre);
        Assert.Equal(6, aviso.Intentos);
        Assert.Equal(ahora, aviso.FechaFinal);
        Assert.Empty(aviso.Campos);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void LaPoliticaSoloTieneCincoReintentos(int reintento) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PoliticaDeReintento.EsperaDelReintento(reintento));

    [Fact]
    [Trait("Criterio", "AC-NOT-15")]
    public void UnAplazamientoNoConsumeUnIntento_EsperaCincoMinutos_YElAvisoVenceA24HorasConSusCamposVacios()
    {
        var aviso = Nuevo();
        aviso.IniciarEnvio(Ahora, Ahora.AddMinutes(2));

        Assert.True(aviso.AplazarSinConsumir(Ahora));

        Assert.Equal(0, aviso.Intentos);
        Assert.Equal(EstadoDelAviso.Pendiente, aviso.Estado);
        Assert.Equal(Ahora.AddMinutes(5), aviso.ProximaFecha);
        Assert.Null(aviso.ArrendadoHasta);

        Assert.False(aviso.VencerSiCorresponde(Ahora.AddHours(23).AddMinutes(59)));
        Assert.True(aviso.VencerSiCorresponde(Ahora.AddHours(24)));
        Assert.Equal(EstadoDelAviso.Fallido, aviso.Estado);
        Assert.Equal("vencido", aviso.MotivoDeCierre);
        Assert.Empty(aviso.Campos);
    }

    [Fact]
    public void UnAvisoVencidoNoSeEnvia_AunqueSuFechaHayaLlegado()
    {
        var aviso = Nuevo();

        Assert.False(aviso.IniciarEnvio(Ahora.AddHours(25), Ahora.AddHours(26)));

        Assert.Equal(EstadoDelAviso.Fallido, aviso.Estado);
        Assert.Equal("vencido", aviso.MotivoDeCierre);
    }

    [Fact]
    public void EsperarElEstadoDeEntrega_NoConsumeIntento_TresVecesSeguidas_YLaCuartaCuentaComoIntento()
    {
        var aviso = Nuevo();
        var ahora = Ahora;

        for (var i = 1; i <= PoliticaDeReintento.MaximoDeEsperasSeguidasPorElEstado; i++)
        {
            aviso.IniciarEnvio(aviso.ProximaFecha, aviso.ProximaFecha.AddMinutes(2));
            Assert.True(aviso.EsperarElEstadoDeEntrega(ahora));
            Assert.Equal(0, aviso.Intentos);
            Assert.Equal(ahora + PoliticaDeReintento.EsperaDelEstadoDeEntrega, aviso.ProximaFecha);
            ahora = aviso.ProximaFecha;
        }

        aviso.IniciarEnvio(ahora, ahora.AddMinutes(2));
        aviso.EsperarElEstadoDeEntrega(ahora);

        Assert.Equal(1, aviso.Intentos);
        Assert.Equal(0, aviso.RepeticionesDeDuplicado);
        Assert.Equal(ahora + PoliticaDeReintento.EsperaDelReintento(1), aviso.ProximaFecha);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-07")]
    public void Cancelar_DejaElAvisoCanceladoConSuMotivo_YUnAvisoCanceladoNoSeEnvia()
    {
        var aviso = Nuevo();

        Assert.True(aviso.Cancelar("consentimiento-revocado", Ahora));

        Assert.Equal(EstadoDelAviso.Cancelado, aviso.Estado);
        Assert.Equal("consentimiento-revocado", aviso.MotivoDeCierre);
        Assert.False(aviso.IniciarEnvio(Ahora, Ahora.AddMinutes(2)));
        Assert.False(aviso.Cancelar("otro", Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-14")]
    public void OmitirPorTope_EsFinal_ConSuFecha_YNoSeReenvia()
    {
        var aviso = Nuevo();

        Assert.True(aviso.OmitirPorTope(Ahora));

        Assert.Equal(EstadoDelAviso.OmitidoPorTope, aviso.Estado);
        Assert.Equal(Ahora, aviso.FechaFinal);
        Assert.False(aviso.IniciarEnvio(Ahora, Ahora.AddMinutes(2)));
    }

    private static readonly CupoDelTope[] DosCupos =
    [
        new("avisos-por-numero-por-hora", "n:v1:aaaa", "e1", Ahora),
        new("avisos-por-numero-en-24-horas", "n:v1:aaaa", "e2", Ahora),
    ];

    [Fact]
    [Trait("Criterio", "AC-NOT-15")]
    public void LosCuposSeReservanUnaVez_YSeVacianAlTerminar()
    {
        var aviso = Nuevo();

        aviso.ReservarLosCupos(DosCupos);
        aviso.ReservarLosCupos([new("otra", "x", "y", Ahora)]);

        Assert.True(aviso.CupoReservado);
        Assert.Equal(2, aviso.Cupos.Count);
        Assert.True(aviso.Cancelar("destinatario-cambiado", Ahora));
        Assert.False(aviso.CupoReservado);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-15")]
    public void UnAvisoFinal_NoReservaCupos()
    {
        var aviso = Nuevo();
        aviso.Cancelar("x", Ahora);

        aviso.ReservarLosCupos(DosCupos);

        Assert.False(aviso.CupoReservado);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-12")]
    public void UnEnvioAceptado_EsperaLaEntrega10Minutos_YSuArrendamientoVencidoPermiteRecogerlo()
    {
        var aviso = Nuevo();
        Assert.True(aviso.IniciarEnvio(Ahora, Ahora.AddMinutes(2)));

        Assert.True(aviso.EsperarLaEntrega(Ahora));

        Assert.Equal(EstadoDelAviso.Enviando, aviso.Estado);
        Assert.Equal(Ahora.AddMinutes(10), aviso.ArrendadoHasta);
        Assert.False(aviso.IniciarEnvio(Ahora.AddMinutes(9), Ahora.AddMinutes(11)));
        Assert.True(aviso.IniciarEnvio(Ahora.AddMinutes(10), Ahora.AddMinutes(12)));
    }

    [Fact]
    public void EsperarLaEntrega_SoloSirveEnEnvio()
    {
        Assert.False(Nuevo().EsperarLaEntrega(Ahora));
    }

    // ---------------------------------------------------------------- las plantillas (T02)

    [Fact]
    [Trait("Criterio", "AC-NOT-09")]
    public void ElCatalogoTieneSoloLosEventosDeNOT02_ConClavesUnicas_YSinTextoEnLaPlantilla()
    {
        var claves = CatalogoDePlantillas.Todas.Select(p => p.Clave).ToList();

        Assert.Equal(claves.Count, claves.Distinct().Count());
        Assert.Equivalent(
            new[]
            {
                "pedido-confirmado", "pedido-estado", "pedido-estado-sucursal", "pedido-en-preparacion", "pedido-en-transito", "pedido-listo-para-recojo", "pedido-entregado", "pedido-no-recogido", "pago-pendiente", "pago-resultado", "pedido-anulado",
                "reembolso-automatico", "envio-guia", "libro-digital-disponible", "devolucion-estado", "traslado-cambio-de-estado",
            },
            claves);
        Assert.Null(CatalogoDePlantillas.Obtener("promocion-de-navidad"));
        Assert.True(CatalogoDePlantillas.EsDeTraslado("traslado-cambio-de-estado"));
        Assert.False(CatalogoDePlantillas.EsDeTraslado("pedido-confirmado"));
        Assert.Equal(TipoDeDestinatario.Sucursal, CatalogoDePlantillas.Obtener("traslado-cambio-de-estado")!.Destinatario);
    }

    [Theory]
    [InlineData("pedido-estado", TipoDeDestinatario.Cliente)]
    [InlineData("pedido-estado-sucursal", TipoDeDestinatario.Sucursal)]
    [Trait("Criterio", "AC-NOT-01")]
    public void LasPlantillasDeEstadoDePedido_TienenUnSoloTipoDeDestinatario_YAceptanSoloElNumeroYUnEstadoDeLaListaCerrada(string clave, TipoDeDestinatario destinatario)
    {
        var plantilla = CatalogoDePlantillas.Obtener(clave)!;
        var valido = new Dictionary<string, string> { ["numeroDePedido"] = "BH-000123", ["estado"] = "listo-para-recojo" };

        Assert.Equal(destinatario, plantilla.Destinatario);
        Assert.Equal(["numeroDePedido", "estado"], plantilla.Campos.Select(c => c.Nombre));
        Assert.All(new[] { "confirmado", "en-transito", "recibido", "en-preparacion", "listo-para-recojo", "despachado", "entregado", "no-recogido", "anulado" }, estado =>
            Assert.True(plantilla.Validar(new Dictionary<string, string>(valido) { ["estado"] = estado }).EsValido, estado));
        Assert.Equal("valor-fuera-de-la-lista", plantilla.Validar(new Dictionary<string, string>(valido) { ["estado"] = "devuelto" }).Motivo);
        Assert.Equal("campo-fuera-del-catalogo", plantilla.Validar(new Dictionary<string, string>(valido) { ["nombre"] = "Ana" }).Motivo);
        Assert.Equal("obligatorio", plantilla.Validar(new Dictionary<string, string> { ["numeroDePedido"] = "BH-000123" }).Motivo);
        Assert.Equal("valor-prohibido", plantilla.Validar(new Dictionary<string, string>(valido) { ["numeroDePedido"] = "https://malo.example" }).Motivo);
    }

    [Theory]
    [InlineData("guia-generada")]
    [InlineData("en-reparto")]
    [Trait("Criterio", "AC-NOT-06")]
    public void LaPlantillaEnvioGuia_AceptaLosCamposQueArmaEnvios_YRechazaUnaGuiaConEnlaceOUnEstadoAjeno(string estado)
    {
        var plantilla = CatalogoDePlantillas.Obtener("envio-guia")!;
        var envios = new Dictionary<string, string> { ["numeroDePedido"] = "BH-000123", ["numeroDeGuia"] = "OLVA-20261119-0042", ["estado"] = estado };
        Assert.True(plantilla.Validar(new Dictionary<string, string>(envios) { ["numeroDeGuia"] = "20261119004212345678" }).EsValido);

        Assert.True(plantilla.Validar(envios).EsValido);
        Assert.Equal(TipoDeDestinatario.Cliente, plantilla.Destinatario);
        Assert.False(plantilla.Validar(new Dictionary<string, string>(envios) { ["numeroDeGuia"] = "https://operador.example/guia" }).EsValido);
        Assert.False(plantilla.Validar(new Dictionary<string, string>(envios) { ["estado"] = "entregado" }).EsValido);
        Assert.False(plantilla.Validar(new Dictionary<string, string> { ["numeroDePedido"] = "BH-000123", ["estado"] = estado }).EsValido);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-06")]
    public void LosCamposPermitidos_Pasan_YUnCampoFueraDelCatalogoSeRechaza()
    {
        var plantilla = CatalogoDePlantillas.Obtener("pedido-listo-para-recojo")!;

        var bien = plantilla.Validar(new Dictionary<string, string> { ["numeroDePedido"] = "BH-000123", ["nombre"] = "María", ["sucursal"] = "AYACUCHO" });
        var extra = plantilla.Validar(new Dictionary<string, string> { ["numeroDePedido"] = "BH-000123", ["enlaceDeDescarga"] = "mi-archivo" });
        var sinObligatorio = plantilla.Validar(new Dictionary<string, string> { ["nombre"] = "María" });

        Assert.True(bien.EsValido);
        Assert.Equal("campo-fuera-del-catalogo", extra.Motivo);
        Assert.Equal("enlaceDeDescarga", extra.Campo);
        Assert.Equal("obligatorio", sinObligatorio.Motivo);
    }

    [Theory]
    [InlineData("numeroDePedido", "https://tienda.example/descarga?x=1")]
    [InlineData("numeroDePedido", "www.tienda.pe")]
    [InlineData("nombre", "juan@example.com")]
    [InlineData("nombre", "Maria Jose")]
    [InlineData("nombre", "Ana123")]
    [InlineData("nombre", "")]
    [InlineData("numeroDePedido", "4111-1111-1111-1111")]
    [InlineData("numeroDePedido", "4111111111111111")]
    [InlineData("numeroDePedido", "AB")]
    [InlineData("numeroDePedido", "BH 123")]
    [InlineData("numeroDePedido", "BH-123\n456")]
    [Trait("Criterio", "AC-NOT-06")]
    public void UnValorConEnlaceTarjetaTextoLibreOFormatoInvalido_SeRechaza_SinDevolverElValor(string campo, string valor)
    {
        var plantilla = CatalogoDePlantillas.Obtener("pedido-confirmado")!;
        var campos = new Dictionary<string, string> { ["numeroDePedido"] = "BH-000123" };
        campos[campo] = valor;

        var resultado = plantilla.Validar(campos);

        Assert.False(resultado.EsValido);
        Assert.Equal(campo, resultado.Campo);
        if (valor.Length > 0)
        {
            Assert.DoesNotContain(valor, resultado.Motivo ?? string.Empty, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UnaEnumeracion_SoloAdmiteSusValores_YElLargoMaximoSeAplica()
    {
        var plantilla = CatalogoDePlantillas.Obtener("traslado-cambio-de-estado")!;

        Assert.True(plantilla.Validar(new Dictionary<string, string> { ["numeroDeTraslado"] = "7", ["rolDeLaSucursal"] = "destino", ["cantidad"] = "3", ["estado"] = "aprobado" }).EsValido);
        Assert.Equal("valor-fuera-de-la-lista", plantilla.Validar(new Dictionary<string, string> { ["numeroDeTraslado"] = "7", ["rolDeLaSucursal"] = "destino", ["cantidad"] = "3", ["estado"] = "robado" }).Motivo);
        Assert.Equal("largo-invalido", plantilla.Validar(new Dictionary<string, string> { ["numeroDeTraslado"] = new string('a', 21), ["rolDeLaSucursal"] = "destino", ["cantidad"] = "3", ["estado"] = "aprobado" }).Motivo);
    }

    [Fact]
    public void UnaPlantillaNoAdmiteCamposRepetidosNiUnaClaveInvalida()
    {
        var campo = new CampoDePlantilla("a", TipoDeCampo.Cantidad, 3);

        Assert.Throws<ArgumentException>(() => PlantillaDeAviso.Crear("clave-valida", TipoDeDestinatario.Cliente, 1, campo, campo));
        Assert.Throws<ArgumentException>(() => PlantillaDeAviso.Crear("Clave Invalida", TipoDeDestinatario.Cliente, 1, campo));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlantillaDeAviso.Crear("clave-valida", TipoDeDestinatario.Cliente, 0, campo));
    }

    // ---------------------------------------------------------------- el número de destino (T02)

    [Theory]
    [InlineData("+51999111222", "+51999111222")]
    [InlineData("999111222", "+51999111222")]
    [InlineData("999 111 222", "+51999111222")]
    [InlineData("999-111-222", "+51999111222")]
    [InlineData("0999111222", "+51999111222")]
    [InlineData("0051999111222", "+51999111222")]
    [InlineData("+51 999 111 222", "+51999111222")]
    [InlineData("(+51) 999.111.222", "+51999111222")]
    [InlineData("  +51999111222  ", "+51999111222")]
    [InlineData("+14155552671", "+14155552671")]
    [InlineData("0014155552671", "+14155552671")]
    [InlineData("٩٩٩١١١٢٢٢", "+51999111222")]
    [InlineData("＋５１９９９１１１２２２", "+51999111222")]
    [Trait("Criterio", "AC-NOT-13")]
    public void ElMismoNumero_EscritoDeCualquierForma_TieneLaMismaFormaE164(string escrito, string esperado)
    {
        // El signo + de ancho completo no es válido: solo se acepta el + ASCII; con él, el resto se normaliza igual.
        var normalizable = NumeroDeDestino.TryNormalizar(escrito, out var numero);

        if (escrito.Contains('＋'))
        {
            Assert.False(normalizable);
            return;
        }

        Assert.True(normalizable);
        Assert.Equal(esperado, numero.E164);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("+51 99 abc")]
    [InlineData("12345")]
    [InlineData("+0999111222")]
    [InlineData("+519991112223333333")]
    [InlineData("999111222; DROP TABLE")]
    [InlineData("+51999111222+")]
    public void UnNumeroInvalido_SeRechaza(string? texto) => Assert.False(NumeroDeDestino.TryNormalizar(texto, out _));

    [Fact]
    public void ElNumero_NoSeImprime()
    {
        NumeroDeDestino.TryNormalizar("999111222", out var numero);

        Assert.DoesNotContain("999", numero.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("999", $"{numero}", StringComparison.Ordinal);
    }
}

public sealed class GuiaDeEnvioConAspectoDeTarjetaTests
{
    [Theory]
    [InlineData("4111111111111111", false)]
    [InlineData("4111-1111-1111-1111", false)]
    [InlineData("1234567890123", true)]
    [InlineData("ABC1234567890123XYZ", true)]
    [InlineData("12345678901234567890123", true)]
    [Trait("Criterio", "AC-NOT-06")]
    public void UnaGuiaDeDigitosQuePasaLuhnEsUnaTarjeta_UnaGuiaCualquieraDeOperadorSiSale(string guia, bool valida)
    {
        var plantilla = CatalogoDePlantillas.Obtener("envio-guia")!;

        var resultado = plantilla.Validar(new Dictionary<string, string> { ["numeroDePedido"] = "BH-000123", ["numeroDeGuia"] = guia, ["estado"] = "guia-generada" });

        Assert.Equal(valida, resultado.EsValido);
    }
}
