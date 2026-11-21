using System.Reflection;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Identidad;
using DominioProposito = BabelHub.Ecommerce.Domain.Identidad.PropositoDeCodigo;
using PropositoDelCorreo = BabelHub.Ecommerce.Application.Plataforma.PropositoDeCodigo;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// Topes y clasificación de los correos de seguridad (identidad-seguridad T28 y T29; AC-IDE-56, AC-IDE-59, AC-IDE-62, AC-IDE-65; IDE-67, IDE-74, IDE-76, IDE-79, IDE-83, IDE-86): el tope de la cuenta, la primera alerta crítica de
/// cada tipo, el tope del origen, el correo sin verificar, el evento y el aviso en la cuenta por cada supresión, y las plantillas sin enlaces ni peticiones de datos.
/// </summary>
public sealed class AvisosDeSeguridadPorCorreoTests
{
    private static Usuario Cliente(EntornoDeIdentidad e, bool verificado = true, string correo = "ana@example.com") => e.Cuenta(correo, verificada: verificado).Usuario;

    private static int Eventos(EntornoDeIdentidad e, string motivo) =>
        e.Seguridad.Eventos.Count(s => s.Accion == AccionesAuditables.AvisoDeSeguridadSuprimido && s.Motivo == motivo);

    // ---------------------------------------------------------------- clasificación (T29)

    [Fact]
    [Trait("Criterio", "AC-IDE-59")]
    public void CadaPlantillaDeAvisoEstaClasificada_YSoloCuatroSonAlertasCriticas()
    {
        var plantillas = typeof(AvisosDeCuenta).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name.StartsWith("Plantilla", StringComparison.Ordinal)).Select(f => (string)f.GetRawConstantValue()!).ToList();

        Assert.NotEmpty(plantillas);
        Assert.All(plantillas, p => Assert.Contains(p, ClasificacionDeAvisosDeCuenta.Todas.Keys));
        Assert.Equal(plantillas.Order(StringComparer.Ordinal), ClasificacionDeAvisosDeCuenta.Todas.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["cambio-de-correo-no-reconocido", "contrasena-cambiada", "correo-anterior-cambiado", "segundo-factor-restablecido"],
            ClasificacionDeAvisosDeCuenta.Todas.Where(p => p.Value == CategoriaDeCorreo.AlertaCriticaDeCuenta).Select(p => p.Key).Order(StringComparer.Ordinal));
        Assert.All(ClasificacionDeAvisosDeCuenta.Todas.Where(p => p.Value != CategoriaDeCorreo.AlertaCriticaDeCuenta), p => Assert.Equal(CategoriaDeCorreo.AvisoDeSeguridad, p.Value));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClasificacionDeAvisosDeCuenta.De("plantilla-desconocida"));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-59")]
    public void CadaPlantillaClasificadaTieneSuTextoEnElCatalogoDeCorreo()
    {
        Assert.Equal(ClasificacionDeAvisosDeCuenta.Todas.Keys.Order(StringComparer.Ordinal), PlantillasDeCorreoDeSeguridad.DeLaCuenta.Keys.Order(StringComparer.Ordinal));
    }

    // ---------------------------------------------------------------- correo sin verificar (IDE-86, IDE-79)

    [Fact]
    [Trait("Criterio", "AC-IDE-65")]
    public async Task UnCorreoSinVerificar_NoRecibeAvisos_SoloEventoYAvisoEnLaCuenta()
    {
        var e = new EntornoDeIdentidad();
        var usuario = Cliente(e, verificado: false);

        await e.Avisos.ContrasenaCambiada(usuario, CancellationToken.None);

        Assert.Empty(e.Correo.Correos);
        Assert.Empty(e.Correo.Correos);
        Assert.Equal(1, Eventos(e, AvisosDeCuenta.MotivoCorreoSinVerificar));
        Assert.Equal("contrasena-cambiada", Assert.Single(e.AvisosGuardados.Avisos).Aviso.Tipo);
        Assert.Empty(e.Limites.Reservas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-59")]
    public async Task UnAvisoCuyoCasoDeUsoYaLoDejaEnLaCuenta_NoSeDuplica_AlSuprimirse()
    {
        var e = new EntornoDeIdentidad();
        var usuario = Cliente(e, verificado: false);

        await e.Avisos.CancelacionDiferida(usuario, CancellationToken.None);

        Assert.Equal(1, Eventos(e, AvisosDeCuenta.MotivoCorreoSinVerificar));
        Assert.Empty(e.AvisosGuardados.Avisos);
    }

    // ---------------------------------------------------------------- tope por cuenta (IDE-74)

    [Fact]
    [Trait("Criterio", "AC-IDE-59")]
    public async Task ElCuartoAvisoNoCriticoEnLasMismas24Horas_NoSale_QuedaUnEventoYElAvisoSigueEnLaCuenta()
    {
        var e = new EntornoDeIdentidad();
        var usuario = Cliente(e);

        var salieron = new List<bool>();
        for (var i = 0; i < 4; i++)
        {
            salieron.Add(await e.Avisos.CancelacionDescartada(usuario, CancellationToken.None));
        }

        Assert.Equal([true, true, true, false], salieron);
        Assert.Equal(3, e.Correo.Correos.Count);
        Assert.Equal(1, Eventos(e, AvisosDeCuenta.MotivoTopePorCuenta));
        Assert.All(e.Correo.Correos, c => Assert.Equal(CategoriaDeCorreo.AvisoDeSeguridad, c.Categoria));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-59")]
    public async Task ElTopeDeLaCuentaEsPorCuenta_OtraCuentaSigueRecibiendo()
    {
        var e = new EntornoDeIdentidad();
        var una = Cliente(e);
        var otra = Cliente(e, correo: "luis@example.com");

        for (var i = 0; i < 4; i++)
        {
            await e.Avisos.CancelacionDescartada(una, CancellationToken.None);
        }

        var salio = await e.Avisos.CancelacionDescartada(otra, CancellationToken.None);

        Assert.True(salio);
        Assert.Equal(3, e.Correo.Correos.Count(c => c.Destinatario == "ana@example.com"));
        Assert.Equal(1, e.Correo.Correos.Count(c => c.Destinatario == "luis@example.com"));
    }

    // ---------------------------------------------------------------- la primera alerta crítica de cada tipo (IDE-74)

    [Fact]
    [Trait("Criterio", "AC-IDE-59")]
    public async Task LaPrimeraAlertaCriticaDeCadaTipoSaleAunqueElTopeDeLosNoCriticosEsteAgotado_YUnaDeOtroTipoEnLaMismaVentanaTambien()
    {
        var e = new EntornoDeIdentidad();
        var usuario = Cliente(e);
        for (var i = 0; i < 4; i++)
        {
            await e.Avisos.CancelacionDescartada(usuario, CancellationToken.None);
        }

        await e.Avisos.ContrasenaCambiada(usuario, CancellationToken.None);
        await e.Avisos.SegundoFactorRestablecido(usuario, CancellationToken.None);
        await e.Avisos.CambioDeCorreoNoReconocido(usuario, CancellationToken.None);

        Assert.Equal(6, e.Correo.Correos.Count);
        Assert.Contains(e.Correo.Correos, c => c.Plantilla == "contrasena-cambiada" && c.Categoria == CategoriaDeCorreo.AlertaCriticaDeCuenta);
        Assert.Contains(e.Correo.Correos, c => c.Plantilla == "segundo-factor-restablecido");
        Assert.Contains(e.Correo.Correos, c => c.Plantilla == "cambio-de-correo-no-reconocido");
        Assert.Equal(1, Eventos(e, AvisosDeCuenta.MotivoTopePorCuenta));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-59")]
    public async Task LaSegundaAlertaCriticaDelMismoTipoEnLas24Horas_NoSale_EventoYAvisoEnLaCuenta()
    {
        var e = new EntornoDeIdentidad();
        var usuario = Cliente(e);

        await e.Avisos.ContrasenaCambiada(usuario, CancellationToken.None);
        var tras = e.Correo.Correos.Count;
        await e.Avisos.ContrasenaCambiada(usuario, CancellationToken.None);

        Assert.Equal(1, tras);
        Assert.Single(e.Correo.Correos);
        Assert.Equal(1, Eventos(e, AvisosDeCuenta.MotivoTopePorCuenta));
        Assert.Equal("contrasena-cambiada", Assert.Single(e.AvisosGuardados.Avisos).Aviso.Tipo);
    }

    // ---------------------------------------------------------------- tope por origen (IDE-86)

    [Fact]
    [Trait("Criterio", "AC-IDE-65")]
    public async Task ElTopeDiarioDelOrigenDeLasAlertasCriticas_AcotaLasQueSalen_Y_DevuelveElCupoDeLaCuentaQueNoSalio()
    {
        var e = new EntornoDeIdentidad();
        e.Limites.Maximos["alertas-criticas-por-origen"] = 2;
        var cuentas = Enumerable.Range(0, 3).Select(i => Cliente(e, correo: $"cuenta{i}@example.com")).ToList();

        foreach (var cuenta in cuentas)
        {
            await e.Avisos.ContrasenaCambiada(cuenta, CancellationToken.None);
        }

        Assert.Equal(2, e.Correo.Correos.Count);
        Assert.Equal(1, Eventos(e, AvisosDeCuenta.MotivoTopePorOrigen));
        Assert.Equal(0, e.Limites.Cuenta("alertas-criticas-por-tipo-y-cuenta", $"{cuentas[2].Id:N}.contrasena-cambiada"));
        Assert.Equal("contrasena-cambiada", Assert.Single(e.AvisosGuardados.Avisos).Aviso.Tipo);
    }

    // ---------------------------------------------------------------- cuota y fallas

    [Fact]
    [Trait("Criterio", "AC-IDE-59")]
    public async Task UnAvisoSuprimidoPorLaCuota_DejaEventoYAvisoEnLaCuenta_YDevuelveSusTopes()
    {
        var e = new EntornoDeIdentidad();
        var usuario = Cliente(e);
        e.Correo.Resultado = ResultadoDeEnvio.Suprimido;

        await e.Avisos.ContrasenaCambiada(usuario, CancellationToken.None);
        var intentos = e.Correo.Correos.Count;
        e.Correo.Resultado = ResultadoDeEnvio.Enviado;
        await e.Avisos.ContrasenaCambiada(usuario, CancellationToken.None);

        Assert.Equal(1, intentos);
        Assert.Equal(1, Eventos(e, AvisosDeCuenta.MotivoCuotaDeCorreo));
        Assert.Equal(2, e.Correo.Correos.Count);
        Assert.Equal(0, Eventos(e, AvisosDeCuenta.MotivoTopePorCuenta));
    }

    [Fact]
    public async Task UnAvisoEncolado_NoCuentaComoSalido_NiDejaEvento_YConservaSuTope()
    {
        var e = new EntornoDeIdentidad();
        var usuario = Cliente(e);
        e.Correo.Resultado = ResultadoDeEnvio.Encolado;

        var salio = await e.Avisos.PendientesCerrados(usuario, CancellationToken.None);

        Assert.False(salio);
        Assert.Empty(e.Seguridad.Eventos);
        Assert.Equal(1, e.Limites.Cuenta("avisos-no-criticos-por-cuenta", usuario.Id.ToString("N")));
    }

    [Fact]
    public async Task SiElCorreoFalla_NadaLanza_YElTopeSeDevuelve()
    {
        var e = new EntornoDeIdentidad();
        var usuario = Cliente(e);
        e.Correo.Falla = true;

        await e.Avisos.ContrasenaCambiada(usuario, CancellationToken.None);
        e.Correo.Falla = false;
        await e.Avisos.ContrasenaCambiada(usuario, CancellationToken.None);

        Assert.Single(e.Correo.Correos);
    }

    [Fact]
    public async Task SinElAlmacenDeContadores_UnAvisoNoCriticoNoSale_YUnaAlertaCriticaSaleIgual()
    {
        var e = new EntornoDeIdentidad();
        var usuario = Cliente(e);
        e.Limites.Caido = true;

        var noCritico = await e.Avisos.CancelacionDescartada(usuario, CancellationToken.None);
        await e.Avisos.ContrasenaCambiada(usuario, CancellationToken.None);

        Assert.False(noCritico);
        Assert.Equal("contrasena-cambiada", Assert.Single(e.Correo.Correos).Plantilla);
        Assert.Equal(1, Eventos(e, AvisosDeCuenta.MotivoTopePorCuenta));
    }

    [Fact]
    public async Task SiElEventoDeSeguridadNoSePuedeEscribir_ElAvisoNoInterrumpeLaOperacion()
    {
        var e = new EntornoDeIdentidad();
        var usuario = Cliente(e, verificado: false);
        e.Seguridad.Falla = true;

        await e.Avisos.ContrasenaCambiada(usuario, CancellationToken.None);

        Assert.Empty(e.Correo.Correos);
        Assert.Single(e.AvisosGuardados.Avisos);
    }

    // ---------------------------------------------------------------- cupo de códigos por origen (IDE-83)

    [Fact]
    [Trait("Criterio", "AC-IDE-62")]
    public async Task ElOrigenQuePideCodigosSinParar_DejaDeConsumirElCupoAlLlegarA10AlDia()
    {
        var e = new EntornoDeIdentidad();
        var usuario = Cliente(e);

        for (var i = 0; i < 12; i++)
        {
            await e.EmisionDeCodigos.Emitir(usuario, DominioProposito.RecuperacionDeAcceso, CancellationToken.None);
        }

        Assert.Equal(10, e.Correo.Codigos.Count);
        Assert.Equal(10, e.Codigos.Codigos.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-62")]
    public async Task ElCupoDelOrigen_SeReservaEnElCaminoSincrono_YElTrabajoEnSegundoPlanoNoLoGastaOtraVez()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("ana@example.com");
        e.Limites.Maximos["codigos-por-correo"] = 100;
        e.Limites.Maximos["codigos-por-origen"] = 100;

        for (var i = 0; i < 10; i++)
        {
            Assert.Null(await SolicitudDeCodigos.Reservar(e.Limites, e.Huellas, "ana@example.com", "recuperacion", CancellationToken.None));
        }

        var rechazo = await SolicitudDeCodigos.Reservar(e.Limites, e.Huellas, "ana@example.com", "recuperacion", CancellationToken.None);
        e.Limites.Maximos["cupo-de-codigos-por-origen"] = 10;
        var reservasAntes = e.Limites.Cuenta("cupo-de-codigos-por-origen", "origen");
        await e.SegundoPlano.Ejecutar(new SolicitudDeCodigoEnSegundoPlano("ana@example.com", DominioProposito.RecuperacionDeAcceso));

        Assert.NotNull(rechazo);
        Assert.Equal(reservasAntes, e.Limites.Cuenta("cupo-de-codigos-por-origen", "origen"));
        Assert.Single(e.Correo.Codigos);
        _ = cuenta;
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-56")]
    public async Task SiElTopeImpideElEnvio_ElCodigoVigenteNoSeInvalida()
    {
        var e = new EntornoDeIdentidad();
        var usuario = Cliente(e);
        await e.EmisionDeCodigos.Emitir(usuario, DominioProposito.RecuperacionDeAcceso, CancellationToken.None);
        var vigentes = e.Codigos.Codigos.Count;
        e.Limites.Maximos["cupo-de-codigos-por-origen"] = 1;

        await e.EmisionDeCodigos.Emitir(usuario, DominioProposito.RecuperacionDeAcceso, CancellationToken.None);

        Assert.Equal(vigentes, e.Codigos.Codigos.Count);
        Assert.Single(e.Correo.Codigos);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-62")]
    public async Task SinElAlmacenDeContadores_NoSaleElCodigo_ConLaMismaRespuesta()
    {
        var e = new EntornoDeIdentidad();
        var usuario = Cliente(e);
        e.Limites.Caido = true;

        await e.EmisionDeCodigos.Emitir(usuario, DominioProposito.VerificacionDeCorreo, CancellationToken.None);

        Assert.Empty(e.Correo.Codigos);
    }

    // ---------------------------------------------------------------- plantillas sin enlaces ni peticiones de datos (T29, AC-IDE-56)

    [Fact]
    [Trait("Criterio", "AC-IDE-56")]
    public void LasPlantillasDeLaCuenta_NombranABabelHub_NoLlevanEnlacesNiPidenDatos_YDicenQueNuncaSePideElCodigo()
    {
        var renderizador = new RenderizadorDeCorreoDeProduccion();

        foreach (var (nombre, _) in PlantillasDeCorreoDeSeguridad.DeLaCuenta)
        {
            var mensaje = renderizador.De(new CorreoSaliente(CategoriaDeCorreo.AvisoDeSeguridad, "ana@example.com", nombre, new Dictionary<string, string> { ["dato"] = "dato-que-no-debe-salir" }));
            var texto = $"{mensaje.Asunto}\n{mensaje.Cuerpo}";

            Assert.Contains("BabelHub", texto, StringComparison.Ordinal);
            Assert.Contains("nunca te pedirá", texto, StringComparison.Ordinal);
            Assert.Contains("un código", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("dato-que-no-debe-salir", texto, StringComparison.Ordinal);
            Assert.Equal(["ana@example.com"], mensaje.Destinatarios);
            AssertSinEnlaces(texto);
            Assert.DoesNotContain("ingresa tu", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("envíanos", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("responde con", texto, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-56")]
    public void ElAvisoDeUnaRespuestaASolicitudDeDatos_NoLlevaElContenidoDeLaRespuesta_SoloDiceQueEstaEnLaCuenta()
    {
        var plantilla = PlantillasDeCorreoDeSeguridad.DeLaCuenta["solicitud-de-datos-respondida"];

        Assert.Contains("en tu cuenta", plantilla.Cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("{", plantilla.Cuerpo, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PropositoDelCorreo.VerificacionDeCuenta)]
    [InlineData(PropositoDelCorreo.RecuperacionDeAcceso)]
    [Trait("Criterio", "AC-IDE-56")]
    public void ElCorreoDeUnCodigo_LlevaSoloElCodigo_ConLaMismaFirma_SinEnlaces(PropositoDelCorreo proposito)
    {
        var renderizador = new RenderizadorDeCorreoDeProduccion();
        var correo = CorreoDeCodigo.ParaElTitular("ana@example.com", proposito, new ValorSecreto("482913"));

        var mensaje = renderizador.DeCodigo(correo);
        var texto = $"{mensaje.Asunto}\n{mensaje.Cuerpo}";

        Assert.Contains("482913", mensaje.Cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("482913", mensaje.Asunto, StringComparison.Ordinal);
        Assert.Contains("10 minutos", texto, StringComparison.Ordinal);
        Assert.Contains("nunca te pedirá", texto, StringComparison.Ordinal);
        AssertSinEnlaces(texto);
        Assert.Equal("[mensaje de correo]", mensaje.ToString());
    }

    [Fact]
    public void LaAlertaALaAdministracion_LlevaSoloServicioMotivoYConteo()
    {
        var mensaje = new RenderizadorDeCorreoDeProduccion().DeAlerta("Ecommerce", "ingresos-fallidos", new DateTimeOffset(2026, 11, 19, 15, 0, 0, TimeSpan.Zero), 7, ["admin@example.com"]);

        Assert.Equal(["admin@example.com"], mensaje.Destinatarios);
        Assert.Contains("ingresos-fallidos", mensaje.Cuerpo, StringComparison.Ordinal);
        Assert.Contains("7", mensaje.Cuerpo, StringComparison.Ordinal);
        AssertSinEnlaces(mensaje.Cuerpo);
    }

    [Fact]
    public void UnaPlantillaQueNoExiste_NoSale_YUnaDuplicadaSeRechazaAlConstruir()
    {
        var renderizador = new RenderizadorDeCorreoDeProduccion([new("pago-confirmado", new PlantillaDeCorreo("BabelHub: pago", "Cuerpo."))]);

        Assert.Throws<InvalidOperationException>(() => renderizador.De(new CorreoSaliente(CategoriaDeCorreo.AvisoDeSeguridad, "ana@example.com", "no-existe")));
        Assert.Equal("BabelHub: pago", renderizador.De(new CorreoSaliente(CategoriaDeCorreo.Pago, "ana@example.com", "pago-confirmado")).Asunto);
        Assert.Throws<ArgumentException>(() => new RenderizadorDeCorreoDeProduccion([new("contrasena-cambiada", new PlantillaDeCorreo("x", "y"))]));
    }

    private static void AssertSinEnlaces(string texto)
    {
        foreach (var prohibido in new[] { "http", "www.", "://", "@", ".com", ".pe", "<a ", "href" })
        {
            Assert.DoesNotContain(prohibido, texto, StringComparison.OrdinalIgnoreCase);
        }
    }
}
