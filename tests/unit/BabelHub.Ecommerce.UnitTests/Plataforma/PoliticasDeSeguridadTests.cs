using BabelHub.Ecommerce.Domain.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

public sealed class PoliticasDeSeguridadTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Todas_TienenClavesUnicasYNingunaCaeAlMotorLocal()
    {
        Assert.Equal(PoliticasDeSeguridad.Todas.Count, PoliticasDeSeguridad.Todas.Select(p => p.Nombre).Distinct().Count());
        Assert.All(PoliticasDeSeguridad.Todas, p =>
        {
            Assert.False(p.RespaldoLocal, p.Nombre);
            Assert.True(p.PublicarEvento, p.Nombre);
        });
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-06")]
    public void IngresosFallidos_ConCincoFallosEnQuinceMinutos_RechazaHastaQuinceMinutosDespuesDelQuintoAunConCredencialesCorrectas()
    {
        var simulador = new SimuladorDeLimite(PoliticasDeSeguridad.IngresosFallidos);
        var quinto = simulador.Repetir(5, Inicio, TimeSpan.FromMinutes(3));

        // La credencial correcta también se evalúa antes de verificarse: el intento llega a Reservar y se rechaza.
        var durante = simulador.Intentar(quinto + TimeSpan.FromMinutes(1));

        Assert.False(durante.Permitida);
        Assert.Equal(quinto + TimeSpan.FromMinutes(15), durante.Respuesta!.DisponibleDesde);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-06")]
    public void IngresosFallidos_ConIntentosDuranteElBloqueo_NoLoExtiendeYAlTerminarElTitularTieneElCupoCompleto()
    {
        var simulador = new SimuladorDeLimite(PoliticasDeSeguridad.IngresosFallidos);
        var quinto = simulador.Repetir(5, Inicio, TimeSpan.FromMinutes(3));
        var finDelBloqueo = quinto + TimeSpan.FromMinutes(15);

        for (var minuto = 1; minuto < 15; minuto += 2)
        {
            var intento = simulador.Intentar(quinto + TimeSpan.FromMinutes(minuto));
            Assert.False(intento.Permitida);
            Assert.Equal(finDelBloqueo, intento.Respuesta!.DisponibleDesde);
        }

        // Las cinco entradas ya expiraron: vuelven a caber cinco intentos.
        simulador.Repetir(5, finDelBloqueo, TimeSpan.FromSeconds(10));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-06")]
    public void IngresosFallidos_ConCuatroFallosEnQuinceMinutos_SigueAceptandoIntentos()
    {
        var simulador = new SimuladorDeLimite(PoliticasDeSeguridad.IngresosFallidos);
        var cuarto = simulador.Repetir(4, Inicio, TimeSpan.FromMinutes(3));

        Assert.True(simulador.Intentar(cuarto + TimeSpan.FromMinutes(1)).Permitida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-06")]
    public void IngresosFallidos_DeUnaCombinacionDistinta_CuentaPorSeparado()
    {
        var combinacionBloqueada = new SimuladorDeLimite(PoliticasDeSeguridad.IngresosFallidos);
        var otroOrigen = new SimuladorDeLimite(PoliticasDeSeguridad.IngresosFallidos);
        var quinto = combinacionBloqueada.Repetir(5, Inicio, TimeSpan.FromSeconds(30));

        Assert.False(combinacionBloqueada.Intentar(quinto + TimeSpan.FromSeconds(5)).Permitida);
        Assert.True(otroOrigen.Intentar(quinto + TimeSpan.FromSeconds(5)).Permitida);
        Assert.Equal(TipoDeSujetoDeLimite.Combinacion, PoliticasDeSeguridad.IngresosFallidos.Tipo);
        Assert.True(PoliticasDeSeguridad.IngresosFallidos.Liberable);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-17")]
    [InlineData("correo")]
    [InlineData("origen")]
    public void Codigos_ConTresSolicitudesEnLaUltimaHora_RechazaLaCuartaYVuelveAlVencerLaPrimera(string sujeto)
    {
        var politica = sujeto == "correo" ? PoliticasDeSeguridad.CodigosPorCorreo : PoliticasDeSeguridad.CodigosPorOrigen;
        var simulador = new SimuladorDeLimite(politica);
        var tercera = simulador.Repetir(3, Inicio, TimeSpan.FromMinutes(10));

        var cuarta = simulador.Intentar(tercera + TimeSpan.FromMinutes(5));

        Assert.False(cuarta.Permitida);
        Assert.Equal(Inicio + TimeSpan.FromHours(1), cuarta.Respuesta!.DisponibleDesde);
        Assert.Null(cuarta.BloqueoHastaNuevo);
        Assert.True(simulador.Intentar(Inicio + TimeSpan.FromHours(1)).Permitida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-17")]
    public void Codigos_ConUnaPoliticaNoLiberable_NoDevuelveCupoPorCancelacion()
    {
        Assert.False(PoliticasDeSeguridad.CodigosPorCorreo.Liberable);
        Assert.False(PoliticasDeSeguridad.CodigosPorOrigen.Liberable);
        Assert.False(PoliticasDeSeguridad.RegistrosPorOrigen.Liberable);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-17")]
    public void Codigos_ParaUnCorreoQueExisteYOtroQueNo_DaLaMismaRespuestaDeLimite()
    {
        // El dominio no distingue cuentas: la misma política y las mismas entradas dan la misma respuesta.
        var existente = new SimuladorDeLimite(PoliticasDeSeguridad.CodigosPorCorreo);
        var inexistente = new SimuladorDeLimite(PoliticasDeSeguridad.CodigosPorCorreo);
        existente.Repetir(3, Inicio, TimeSpan.FromMinutes(1));
        inexistente.Repetir(3, Inicio, TimeSpan.FromMinutes(1));

        var a = existente.Intentar(Inicio + TimeSpan.FromMinutes(10));
        var b = inexistente.Intentar(Inicio + TimeSpan.FromMinutes(10));

        Assert.Equal(a.Respuesta, b.Respuesta);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-18")]
    public void SegundoFactorPorOrigen_ConCincoIncorrectos_RechazaQuinceMinutosAunConCodigoCorrecto()
    {
        var simulador = new SimuladorDeLimite(PoliticasDeSeguridad.SegundoFactorPorOrigen);
        var quinto = simulador.Repetir(5, Inicio, TimeSpan.FromMinutes(1));

        var correcto = simulador.Intentar(quinto + TimeSpan.FromMinutes(2));

        Assert.False(correcto.Permitida);
        Assert.Equal(quinto + TimeSpan.FromMinutes(15), correcto.Respuesta!.DisponibleDesde);
        Assert.True(simulador.Intentar(quinto + TimeSpan.FromMinutes(15)).Permitida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-18")]
    public void SegundoFactorPorOrigen_DeOtroOrigen_NoSeVeAfectado()
    {
        var bloqueado = new SimuladorDeLimite(PoliticasDeSeguridad.SegundoFactorPorOrigen);
        var otro = new SimuladorDeLimite(PoliticasDeSeguridad.SegundoFactorPorOrigen);
        var quinto = bloqueado.Repetir(5, Inicio, TimeSpan.FromMinutes(1));

        Assert.False(bloqueado.Intentar(quinto + TimeSpan.FromMinutes(1)).Permitida);
        Assert.True(otro.Intentar(quinto + TimeSpan.FromMinutes(1)).Permitida);
        Assert.Equal(TipoDeSujetoDeLimite.Combinacion, PoliticasDeSeguridad.SegundoFactorPorOrigen.Tipo);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-21")]
    public void RegistrosPorOrigen_ConTresCuentasEnLaUltimaHora_RechazaLaCuarta()
    {
        var simulador = new SimuladorDeLimite(PoliticasDeSeguridad.RegistrosPorOrigen);
        var tercera = simulador.Repetir(3, Inicio, TimeSpan.FromMinutes(5));

        var cuarta = simulador.Intentar(tercera + TimeSpan.FromMinutes(1));

        Assert.False(cuarta.Permitida);
        Assert.Equal(Inicio + TimeSpan.FromHours(1), cuarta.Respuesta!.DisponibleDesde);
        Assert.Equal(TipoDeSujetoDeLimite.Origen, PoliticasDeSeguridad.RegistrosPorOrigen.Tipo);
        Assert.True(new SimuladorDeLimite(PoliticasDeSeguridad.RegistrosPorOrigen).Intentar(tercera + TimeSpan.FromMinutes(1)).Permitida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-21")]
    public void RegistrosPorOrigen_ConVentanaDeslizante_NoPermiteUnaRafagaEnElCambioDeHora()
    {
        var simulador = new SimuladorDeLimite(PoliticasDeSeguridad.RegistrosPorOrigen);
        simulador.Repetir(3, Inicio + TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(3));

        Assert.False(simulador.Intentar(Inicio + TimeSpan.FromMinutes(70)).Permitida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-26")]
    public void SegundoFactorPorCuenta_ConDiezIncorrectosEnUnaHoraDeVariosOrigenes_RechazaQuinceMinutosYSigueContando()
    {
        var simulador = new SimuladorDeLimite(PoliticasDeSeguridad.SegundoFactorPorCuenta);
        var decimo = simulador.Repetir(10, Inicio, TimeSpan.FromMinutes(5.5));

        var correcto = simulador.Intentar(decimo + TimeSpan.FromMinutes(1));

        Assert.False(correcto.Permitida);
        Assert.Equal(decimo + TimeSpan.FromMinutes(15), correcto.Respuesta!.DisponibleDesde);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-26")]
    public void SegundoFactorPorCuenta_ConDiezFallosEnCincuentaMinutosYUnIntentoAlTerminarElBloqueo_SigueRechazandoHastaQueExpireLaMasAntigua()
    {
        // D26: la ventana de una hora no se vacía; nunca hay más de 10 fallos por hora aunque el titular espere el bloqueo.
        var simulador = new SimuladorDeLimite(PoliticasDeSeguridad.SegundoFactorPorCuenta);
        var primero = Inicio + TimeSpan.FromMinutes(20);
        var decimo = simulador.Repetir(10, primero, TimeSpan.FromMinutes(30.0 / 9));
        var alTerminarElBloqueo = decimo + TimeSpan.FromMinutes(15);

        var intento = simulador.Intentar(alTerminarElBloqueo);

        Assert.False(intento.Permitida);
        Assert.Equal(primero + TimeSpan.FromHours(1), intento.Respuesta!.DisponibleDesde);
        Assert.Null(intento.BloqueoHastaNuevo);
        Assert.True(simulador.Intentar(primero + TimeSpan.FromHours(1)).Permitida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-26")]
    public void SegundoFactorPorCuenta_ConDiezFallosEnCincuentaMinutosYElBloqueoYaTerminado_PermiteSoloCuandoLaMasAntiguaYaExpiro()
    {
        var simulador = new SimuladorDeLimite(PoliticasDeSeguridad.SegundoFactorPorCuenta);
        var decimo = simulador.Repetir(10, Inicio, TimeSpan.FromMinutes(50.0 / 9));

        Assert.True(simulador.Intentar(decimo + TimeSpan.FromMinutes(15)).Permitida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-26")]
    public void SegundoFactorPorCuenta_ConNueveFallosEnUnaHora_AunAceptaElDecimo()
    {
        var simulador = new SimuladorDeLimite(PoliticasDeSeguridad.SegundoFactorPorCuenta);
        var noveno = simulador.Repetir(9, Inicio, TimeSpan.FromMinutes(5));

        Assert.True(simulador.Intentar(noveno + TimeSpan.FromMinutes(1)).Permitida);
        Assert.True(PoliticasDeSeguridad.SegundoFactorPorCuenta.Liberable);
        Assert.Equal(TipoDeSujetoDeLimite.Usuario, PoliticasDeSeguridad.SegundoFactorPorCuenta.Tipo);
    }

    [Fact]
    public void Generales_TienenClavesUnicasConRespaldoLocalYPublicanElEvento()
    {
        Assert.Equal(PoliticasDeSeguridad.Generales.Count, PoliticasDeSeguridad.Generales.Select(p => p.Nombre).Distinct().Count());
        Assert.All(PoliticasDeSeguridad.Generales, p =>
        {
            Assert.True(p.RespaldoLocal, p.Nombre);
            Assert.True(p.PublicarEvento, p.Nombre);
            Assert.Null(p.DuracionDeBloqueo);
        });
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-05")]
    public void SolicitudesPorUsuario_ConCienEnElUltimoMinuto_RechazaLaSiguienteYVuelveAlTiempoSugerido()
    {
        var simulador = new SimuladorDeLimite(PoliticasDeSeguridad.SolicitudesPorUsuario);
        var ultima = simulador.Repetir(100, Inicio, TimeSpan.FromMilliseconds(500));

        var rechazo = simulador.Intentar(ultima + TimeSpan.FromSeconds(1));

        Assert.False(rechazo.Permitida);
        Assert.Equal(Inicio + TimeSpan.FromMinutes(1), rechazo.Respuesta!.DisponibleDesde);
        Assert.True(simulador.Intentar(rechazo.Respuesta.DisponibleDesde).Permitida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-16")]
    public void SolicitudesSinSesionPorOrigen_ConSesentaEnElUltimoMinuto_RechazaLaSiguiente()
    {
        var simulador = new SimuladorDeLimite(PoliticasDeSeguridad.SolicitudesSinSesionPorOrigen);
        var ultima = simulador.Repetir(60, Inicio, TimeSpan.FromSeconds(1));

        Assert.False(simulador.Intentar(ultima + TimeSpan.FromMilliseconds(500)).Permitida);
        Assert.Equal(TipoDeSujetoDeLimite.Origen, PoliticasDeSeguridad.SolicitudesSinSesionPorOrigen.Tipo);
    }

    [Fact]
    public void ElTopePrevio_EsMasAltoQueElLimiteSinSesionYElDelOrigenDesconocidoMasAltoAun()
    {
        Assert.Equal(600, PoliticasDeSeguridad.SolicitudesPreviasPorOrigen.Maximo);
        Assert.True(PoliticasDeSeguridad.SolicitudesDelOrigenDesconocido.Maximo > PoliticasDeSeguridad.SolicitudesPreviasPorOrigen.Maximo);
        Assert.True(PoliticasDeSeguridad.SolicitudesPreviasPorOrigen.Maximo > PoliticasDeSeguridad.SolicitudesSinSesionPorOrigen.Maximo);
    }

    [Fact]
    public void LasPoliticasDeEnvios_TienenLosValoresDelPlan_SonGeneralesYCaenAlLimiteLocal()
    {
        Assert.Equal(("consultas-de-envios-por-usuario", 60, TimeSpan.FromMinutes(1)), (PoliticasDeSeguridad.ConsultasDeEnviosPorUsuario.Nombre, PoliticasDeSeguridad.ConsultasDeEnviosPorUsuario.Maximo, PoliticasDeSeguridad.ConsultasDeEnviosPorUsuario.Ventana));
        Assert.Equal(("operaciones-de-envios-por-usuario", 30, TimeSpan.FromMinutes(1)), (PoliticasDeSeguridad.OperacionesDeEnviosPorUsuario.Nombre, PoliticasDeSeguridad.OperacionesDeEnviosPorUsuario.Maximo, PoliticasDeSeguridad.OperacionesDeEnviosPorUsuario.Ventana));
        Assert.Equal(("guias-al-operador", 30, TimeSpan.FromMinutes(1)), (PoliticasDeSeguridad.GuiasAlOperador.Nombre, PoliticasDeSeguridad.GuiasAlOperador.Maximo, PoliticasDeSeguridad.GuiasAlOperador.Ventana));
        foreach (var politica in new[] { PoliticasDeSeguridad.ConsultasDeEnviosPorUsuario, PoliticasDeSeguridad.OperacionesDeEnviosPorUsuario, PoliticasDeSeguridad.GuiasAlOperador })
        {
            Assert.Contains(politica, PoliticasDeSeguridad.Generales);
            Assert.True(politica.RespaldoLocal);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public void LaRafagaDeAvisosDeLaPasarela_EsPorOrigen_ConRespaldoLocal_YEstaEnElCatalogoDeLimites()
    {
        var politica = PoliticasDeSeguridad.AvisosDeLaPasarelaPorOrigen;

        Assert.Equal(("avisos-de-la-pasarela-por-origen", 120, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Origen), (politica.Nombre, politica.Maximo, politica.Ventana, politica.Tipo));
        Assert.True(politica.RespaldoLocal);
        Assert.Contains(politica, PoliticasDeSeguridad.Generales);
        Assert.Contains(politica.Nombre, BabelHub.Ecommerce.Application.Auditoria.Interno.CatalogoDeAccionesDeAuditoria.MotivosDeLimiteExcedido);
        Assert.Equal(politica.Nombre, BabelHub.Ecommerce.Application.Plataforma.PoliticasDeLimite.AvisosDeLaPasarelaPorOrigen.Valor);
    }

    [Fact]
    public void LasPoliticasDePedidos_TienenLosValoresDelPlan_LasDeUsoSonGeneralesYLaDelContactoEsCriticaYFallaCerrada()
    {
        var consultas = PoliticasDeSeguridad.ConsultasDePedidosPorUsuario;
        var gestion = PoliticasDeSeguridad.GestionDePedidosPorUsuario;
        var contacto = PoliticasDeSeguridad.LecturasDelContactoDePedidosPorPersona;

        Assert.Equal(("consultas-de-pedidos-por-usuario", 60, TimeSpan.FromMinutes(1)), (consultas.Nombre, consultas.Maximo, consultas.Ventana));
        Assert.Equal(("gestion-de-pedidos-por-usuario", 30, TimeSpan.FromMinutes(1)), (gestion.Nombre, gestion.Maximo, gestion.Ventana));
        Assert.Equal(("lecturas-del-contacto-de-pedidos-por-persona", 200, TimeSpan.FromHours(1)), (contacto.Nombre, contacto.Maximo, contacto.Ventana));
        Assert.All(new[] { consultas, gestion }, p =>
        {
            Assert.Equal(TipoDeSujetoDeLimite.Usuario, p.Tipo);
            Assert.True(p.RespaldoLocal);
            Assert.Contains(p, PoliticasDeSeguridad.Generales);
        });
        Assert.False(contacto.RespaldoLocal);
        Assert.Contains(contacto, PoliticasDeSeguridad.Todas);
        Assert.DoesNotContain(contacto, PoliticasDeSeguridad.Generales);
        foreach (var politica in new[] { consultas, gestion, contacto })
        {
            Assert.Contains(politica.Nombre, BabelHub.Ecommerce.Application.Auditoria.Interno.CatalogoDeAccionesDeAuditoria.MotivosDeLimiteExcedido);
        }

        Assert.Contains(BabelHub.Ecommerce.Application.Plataforma.PoliticasDeLimite.LecturasDelContactoDePedidosPorPersona, BabelHub.Ecommerce.Application.Plataforma.PoliticasDeLimite.Todas);
        Assert.Contains(BabelHub.Ecommerce.Application.Plataforma.PoliticasDeLimite.ConsultasDePedidosPorUsuario, BabelHub.Ecommerce.Application.Plataforma.PoliticasDeLimite.Generales);
        Assert.Contains(BabelHub.Ecommerce.Application.Plataforma.PoliticasDeLimite.GestionDePedidosPorUsuario, BabelHub.Ecommerce.Application.Plataforma.PoliticasDeLimite.Generales);
    }

    [Fact]
    public void LasPoliticasDeCarrito_TienenLosValoresDelPlan_YSoloPasarAlPagoFallaCerrada()
    {
        Assert.Equal(("carritos-de-visitante-por-origen", 10, TimeSpan.FromHours(1)), (PoliticasDeSeguridad.CarritosDeVisitantePorOrigen.Nombre, PoliticasDeSeguridad.CarritosDeVisitantePorOrigen.Maximo, PoliticasDeSeguridad.CarritosDeVisitantePorOrigen.Ventana));
        Assert.Equal((60, TimeSpan.FromMinutes(1)), (PoliticasDeSeguridad.EdicionDeLineasPorCuenta.Maximo, PoliticasDeSeguridad.EdicionDeLineasPorCuenta.Ventana));
        Assert.Equal((60, TimeSpan.FromMinutes(1)), (PoliticasDeSeguridad.EdicionDeLineasPorOrigen.Maximo, PoliticasDeSeguridad.EdicionDeLineasPorOrigen.Ventana));
        Assert.Equal((30, TimeSpan.FromMinutes(1)), (PoliticasDeSeguridad.RevisionDelCheckoutPorCuenta.Maximo, PoliticasDeSeguridad.RevisionDelCheckoutPorCuenta.Ventana));
        Assert.Equal((10, TimeSpan.FromHours(1)), (PoliticasDeSeguridad.PasosAlPagoPorCuenta.Maximo, PoliticasDeSeguridad.PasosAlPagoPorCuenta.Ventana));
        Assert.Equal((20, TimeSpan.FromHours(1)), (PoliticasDeSeguridad.VolverOReiniciarPorCuenta.Maximo, PoliticasDeSeguridad.VolverOReiniciarPorCuenta.Ventana));

        // Pasar al pago reserva stock: falla cerrada, entre las críticas y sin respaldo local; las demás de Carrito son generales.
        Assert.Contains(PoliticasDeSeguridad.PasosAlPagoPorCuenta, PoliticasDeSeguridad.Todas);
        Assert.False(PoliticasDeSeguridad.PasosAlPagoPorCuenta.RespaldoLocal);
        foreach (var general in new[] { PoliticasDeSeguridad.CarritosDeVisitantePorOrigen, PoliticasDeSeguridad.EdicionDeLineasPorCuenta, PoliticasDeSeguridad.EdicionDeLineasPorOrigen, PoliticasDeSeguridad.RevisionDelCheckoutPorCuenta, PoliticasDeSeguridad.VolverOReiniciarPorCuenta })
        {
            Assert.Contains(general, PoliticasDeSeguridad.Generales);
            Assert.True(general.RespaldoLocal, general.Nombre);
        }
    }
}
