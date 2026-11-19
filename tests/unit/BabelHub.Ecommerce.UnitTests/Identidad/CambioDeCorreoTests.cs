using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// El cambio de correo y los dispositivos reconocidos (identidad-seguridad T20; AC-IDE-47, AC-IDE-48; IDE-25, IDE-53, IDE-54, IDE-55, SEG-19, SEG-28): desde un dispositivo reconocido y una región habitual se aplica tras
/// verificar el correo nuevo; desde otro no se aplica, congela el dispositivo, alerta al correo anterior y en la cuenta, y el titular lo confirma o rechaza en 24 horas desde un dispositivo reconocido.
/// </summary>
public sealed class CambioDeCorreoTests
{
    private const string Antiguo = "ana@example.com";
    private const string Nuevo = "ana.nueva@example.com";

    private static ValorSecreto S(string texto) => new(texto);

    private static (EntornoDeIdentidad E, CuentaDeIdentidad Cuenta, CambioDeCorreo Cambio, ValorSecreto Marca) Escenario()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta(Antiguo);
        e.ActuarComo(cuenta);
        var marca = e.DispositivoReconocido(cuenta, "PE");
        return (e, cuenta, e.CambioDeCorreo(), marca);
    }

    private static Task<Resultado<RespuestaUniforme>> Pedir(CambioDeCorreo cambio, CuentaDeIdentidad cuenta, ValorSecreto? marca = null, string? region = "PE", string correo = Nuevo, string frase = EntornoDeIdentidad.ContrasenaSegura, bool marcaNula = false) =>
        cambio.Solicitar(cuenta.Usuario.Id, correo, S(frase), marcaNula ? null : marca, region);

    // ---------------------------------------------------------------- reconocido y habitual (AC-IDE-47)

    [Fact]
    [Trait("Criterio", "AC-IDE-47")]
    public async Task DesdeUnDispositivoReconocidoYUnaRegionHabitual_ElCodigoSaleAlCorreoNuevo_YElCambioAunNoSeAplica()
    {
        var (e, cuenta, cambio, marca) = Escenario();

        var resultado = await Pedir(cambio, cuenta, marca);

        Assert.True(resultado.EsExito);
        Assert.Equal(Antiguo, cuenta.Usuario.Correo);
        var codigo = Assert.Single(e.Correo.Codigos);
        Assert.Equal(Nuevo, codigo.Destinatario);
        Assert.Equal(EstadoDelCambioDeCorreo.EsperandoVerificacion, e.Bd.Cambios.Single().Estado);
        Assert.Empty(e.Correo.Correos);
        Assert.Empty(e.Bd.Avisos);
        Assert.False(e.Bd.Dispositivos.Single().EstaCongelado(e.Reloj.Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-47")]
    public async Task AlVerificarElCorreoNuevo_ElCambioSeAplica_AvisaEnLaCuentaYAlNuevo_Audita_YNoEnviaNadaAlAnterior()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        await Pedir(cambio, cuenta, marca);
        var codigo = e.UltimoCodigoEnviado();

        var verificado = await cambio.VerificarElCorreoNuevo(cuenta.Usuario.Id, S(codigo));

        Assert.True(verificado.EsExito);
        Assert.Equal(Nuevo, cuenta.Usuario.Correo);
        Assert.True(cuenta.Usuario.CorreoVerificado);
        Assert.Equal(EstadoDelCambioDeCorreo.Aplicado, e.Bd.Cambios.Single().Estado);
        Assert.Equal("correo-cambiado", Assert.Single(e.Bd.Avisos).Aviso.Tipo);
        var aviso = Assert.Single(e.Correo.Correos);
        Assert.Equal(Nuevo, aviso.Destinatario);
        Assert.Equal(AvisosDeCuenta.PlantillaDeCorreoCambiado, aviso.Plantilla);
        Assert.DoesNotContain(e.Correo.Correos, c => c.Destinatario == Antiguo);
        Assert.DoesNotContain(e.Correo.Codigos, c => c.Destinatario == Antiguo);
        Assert.Equal(AccionesAuditables.CorreoCambiado, Assert.Single(e.Auditoria.Registradas).Accion);

        // El correo anterior ya no ingresa; el nuevo sí.
        Assert.Equal(FallosDeIdentidad.CredencialesInvalidas, (await e.Ingreso().Ingresar(new SolicitudDeIngreso(Antiguo, S(EntornoDeIdentidad.ContrasenaSegura)))).Error);
        Assert.True((await e.Ingreso().Ingresar(new SolicitudDeIngreso(Nuevo, S(EntornoDeIdentidad.ContrasenaSegura)))).EsExito);
    }

    [Fact]
    public async Task ElCodigoDeCambioDeCorreo_SoloSaleAlCorreoNuevoGuardado_YEnNingunMomentoAlAnterior()
    {
        var (e, cuenta, cambio, marca) = Escenario();

        await Pedir(cambio, cuenta, marca);

        Assert.All(e.Correo.Codigos, c => Assert.Equal(Nuevo, c.Destinatario));
    }

    [Fact]
    public async Task VerificarConUnCodigoIncorrecto_NoCambiaNada_YCuentaIntentos()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        await Pedir(cambio, cuenta, marca);
        var correcto = e.UltimoCodigoEnviado();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(FallosDeIdentidad.CodigoInvalido, (await cambio.VerificarElCorreoNuevo(cuenta.Usuario.Id, S("000000"))).Error);
        }

        // Agotados los 5 intentos, ni el código correcto sirve.
        Assert.Equal(FallosDeIdentidad.CodigoInvalido, (await cambio.VerificarElCorreoNuevo(cuenta.Usuario.Id, S(correcto))).Error);
        Assert.Equal(Antiguo, cuenta.Usuario.Correo);
        Assert.Contains(e.Seguridad.Eventos, ev => ev.Accion == AccionesAuditables.CodigoDeUnSoloUsoRechazado);
    }

    [Fact]
    public async Task VerificarElCodigoVencido_NoCambiaNada()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        await Pedir(cambio, cuenta, marca);
        var codigo = e.UltimoCodigoEnviado();
        e.Reloj.Ahora = e.Reloj.Ahora.AddMinutes(11);

        var resultado = await cambio.VerificarElCorreoNuevo(cuenta.Usuario.Id, S(codigo));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, resultado.Error);
        Assert.Equal(Antiguo, cuenta.Usuario.Correo);
    }

    [Fact]
    public async Task VerificarSinUnCambioEnCurso_EsUnCodigoInvalido()
    {
        var (_, cuenta, cambio, _) = Escenario();

        var resultado = await cambio.VerificarElCorreoNuevo(cuenta.Usuario.Id, S("123456"));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, resultado.Error);
    }

    [Fact]
    public async Task VerificarElCorreoNuevo_SiLaAuditoriaFalla_NoSeCambiaElCorreo()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        await Pedir(cambio, cuenta, marca);
        var codigo = e.UltimoCodigoEnviado();
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => cambio.VerificarElCorreoNuevo(cuenta.Usuario.Id, S(codigo)));

        Assert.Equal(Antiguo, cuenta.Usuario.Correo);
        Assert.Equal(EstadoDelCambioDeCorreo.EsperandoVerificacion, e.Bd.Cambios.Single().Estado);
        Assert.Empty(e.Bd.Avisos);
    }

    [Fact]
    public async Task VerificarElCorreoNuevo_SiOtraCuentaYaLoRegistro_NoSeAplica()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        await Pedir(cambio, cuenta, marca);
        var codigo = e.UltimoCodigoEnviado();
        e.Cuenta(Nuevo);

        var resultado = await cambio.VerificarElCorreoNuevo(cuenta.Usuario.Id, S(codigo));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, resultado.Error);
        Assert.Equal(Antiguo, cuenta.Usuario.Correo);
    }

    // ---------------------------------------------------------------- validaciones

    [Fact]
    [Trait("Criterio", "AC-IDE-47")]
    public async Task SinLaContrasenaActual_NoSeGuardaNiSeEnviaNada()
    {
        var (e, cuenta, cambio, marca) = Escenario();

        var resultado = await Pedir(cambio, cuenta, marca, frase: "equivocada y larga de verdad");

        Assert.Equal(Reautenticacion.ContrasenaActualIncorrecta, resultado.Error);
        Assert.Empty(e.Bd.Cambios);
        Assert.Empty(e.Correo.Codigos);
        Assert.Empty(e.Correo.Correos);
    }

    [Theory]
    [InlineData("no-es-un-correo", "invalido")]
    [InlineData(Antiguo, "igual-al-actual")]
    [InlineData("ANA@example.com", "igual-al-actual")]
    public async Task ElCorreoNuevo_InvalidoOIgualAlActual_SeRechaza(string correo, string clave)
    {
        var (e, cuenta, cambio, marca) = Escenario();

        var resultado = await Pedir(cambio, cuenta, marca, correo: correo);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal(clave, Assert.Single(resultado.Error.Errores).Clave);
        Assert.Empty(e.Bd.Cambios);
    }

    [Fact]
    public async Task UnCorreoYaUsadoPorOtraCuenta_RespondeIgual_NoEnviaCodigoNiGuardaNada()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        e.Cuenta(Nuevo);

        var resultado = await Pedir(cambio, cuenta, marca);

        Assert.True(resultado.EsExito);
        Assert.Empty(e.Correo.Codigos);
        Assert.Empty(e.Correo.Correos);
        Assert.Empty(e.Bd.Cambios);
    }

    [Fact]
    public async Task UnaCuentaConElCorreoSinVerificar_OUnaDelPersonal_NoCambiaElCorreo()
    {
        var e = new EntornoDeIdentidad();
        var sinVerificar = e.Cuenta("sin@example.com", verificada: false);
        var personal = e.Cuenta("personal@example.com", RolDeUsuario.Logistica);

        e.ActuarComo(sinVerificar);
        var a = await e.CambioDeCorreo().Solicitar(sinVerificar.Usuario.Id, Nuevo, S(EntornoDeIdentidad.ContrasenaSegura), null, "PE");
        e.Actor.Actual = ActorActual.DeUsuario(personal.Usuario.Id, "logistica");
        var b = await e.CambioDeCorreo().Solicitar(personal.Usuario.Id, Nuevo, S(EntornoDeIdentidad.ContrasenaSegura), null, "PE");

        Assert.Equal("correo-sin-verificar", a.Error!.Clave);
        Assert.Equal("solo-clientes", b.Error!.Clave);
        Assert.Empty(e.Bd.Cambios);
    }

    [Fact]
    public async Task CualquierOperacion_DeOtraPersona_EsProhibida()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        e.Actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), "cliente");
        var id = cuenta.Usuario.Id;

        var errores = new[]
        {
            (await cambio.Solicitar(id, Nuevo, S(EntornoDeIdentidad.ContrasenaSegura), marca, "PE")).Error,
            (await cambio.VerificarElCorreoNuevo(id, S("123456"))).Error,
            (await cambio.ConfirmarElCambioPendiente(id, S(EntornoDeIdentidad.ContrasenaSegura), marca, "PE")).Error,
            (await cambio.RechazarElCambioPendiente(id, S(EntornoDeIdentidad.ContrasenaSegura), marca, "PE")).Error,
        };

        Assert.All(errores, error => Assert.Equal(AlcanceDeLaCuenta.Prohibido, error));
        Assert.Equal(errores.Length, e.Denegaciones.Registradas.Count);
    }

    [Fact]
    public async Task LasSolicitudesDeCodigoAUnMismoCorreoNuevo_TienenElLimiteDeSEG19()
    {
        var (_, cuenta, cambio, marca) = Escenario();

        ErrorDeAplicacion? ultimo = null;
        for (var i = 0; i < 4; i++)
        {
            ultimo = (await Pedir(cambio, cuenta, marca)).Error;
        }

        Assert.Equal(FallosDeIdentidad.LimiteExcedido, ultimo);
    }

    // ---------------------------------------------------------------- no reconocido (AC-IDE-48)

    [Fact]
    [Trait("Criterio", "AC-IDE-48")]
    public async Task DesdeUnaRegionNoHabitual_NoSeAplica_CongelaElDispositivo_AlertaAlCorreoAnterior_YAvisaEnLaCuenta()
    {
        var (e, cuenta, cambio, marca) = Escenario();

        var resultado = await Pedir(cambio, cuenta, marca, region: "BR");

        Assert.True(resultado.EsExito);
        Assert.Equal(Antiguo, cuenta.Usuario.Correo);
        var pendiente = e.Bd.Cambios.Single();
        Assert.Equal(EstadoDelCambioDeCorreo.PendienteDeConfirmacion, pendiente.Estado);
        Assert.Equal(e.Reloj.Ahora.AddHours(24), pendiente.Vence);
        Assert.True(e.Bd.Dispositivos.Single().EstaCongelado(e.Reloj.Ahora));
        Assert.Equal(pendiente.Vence, e.Bd.Dispositivos.Single().CongeladoHasta);
        var alerta = Assert.Single(e.Correo.Correos);
        Assert.Equal(Antiguo, alerta.Destinatario);
        Assert.Equal(CategoriaDeCorreo.AlertaCriticaDeCuenta, alerta.Categoria);
        Assert.Equal(AvisosDeCuenta.PlantillaDeCambioDeCorreoNoReconocido, alerta.Plantilla);
        Assert.Equal("cambio-de-correo-no-reconocido", Assert.Single(e.Bd.Avisos).Aviso.Tipo);
        // Sin código al correo nuevo mientras el titular no lo confirme.
        Assert.Empty(e.Correo.Codigos);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-48")]
    public async Task DesdeUnDispositivoSinMarca_NoSeAplica_YAlertaAlCorreoAnterior()
    {
        var (e, cuenta, cambio, _) = Escenario();

        var resultado = await Pedir(cambio, cuenta, marca: null);

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDelCambioDeCorreo.PendienteDeConfirmacion, e.Bd.Cambios.Single().Estado);
        Assert.Equal(Antiguo, Assert.Single(e.Correo.Correos).Destinatario);
        Assert.Empty(e.Correo.Codigos);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-48")]
    public async Task UnDispositivoRecienEmitido_NoCuentaComoReconocido_NiSuRegionComoHabitual()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta(Antiguo);
        e.ActuarComo(cuenta);
        var emitida = (await e.DispositivosDeLaCuenta().RegistrarElIngresoCompleto(cuenta.Usuario.Id, null, "PE")).Valor.MarcaNueva!;

        var resultado = await Pedir(e.CambioDeCorreo(), cuenta, emitida);

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDelCambioDeCorreo.PendienteDeConfirmacion, e.Bd.Cambios.Single().Estado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ZZ")]
    [InlineData("pais-largo")]
    public async Task UnaRegionDesconocida_NuncaEsHabitual(string? region)
    {
        var (e, cuenta, cambio, marca) = Escenario();

        await Pedir(cambio, cuenta, marca, region);

        Assert.Equal(EstadoDelCambioDeCorreo.PendienteDeConfirmacion, e.Bd.Cambios.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-48")]
    public async Task ElDispositivoCongelado_NoHaceCambiosSensibles_PeroUnoSinMarcaOOtroReconocidoSi()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        await Pedir(cambio, cuenta, marca, region: "BR");
        var gestion = e.Gestion();
        var id = cuenta.Usuario.Id;

        // Congelados: otro cambio de correo, la contraseña y el segundo factor desde ese dispositivo.
        Assert.Equal(ConfianzaDelDispositivo.DispositivoCongelado, (await Pedir(cambio, cuenta, marca, correo: "otro@example.com")).Error);
        Assert.Equal(ConfianzaDelDispositivo.DispositivoCongelado, (await gestion.CambiarLaContrasena(id, S(EntornoDeIdentidad.ContrasenaSegura), S("otra frase larga y distinta"), marcaDeDispositivo: marca)).Error);
        Assert.Equal(ConfianzaDelDispositivo.DispositivoCongelado, (await gestion.IniciarElSegundoFactor(id, S(EntornoDeIdentidad.ContrasenaSegura), marca)).Error);

        // Otro dispositivo reconocido no está congelado.
        var otraMarca = e.DispositivoReconocido(cuenta, "PE");
        Assert.True((await gestion.CambiarLaContrasena(id, S(EntornoDeIdentidad.ContrasenaSegura), S("otra frase larga y distinta"), marcaDeDispositivo: otraMarca)).EsExito);
    }

    [Fact]
    public async Task MientrasHayUnCambioNoReconocidoPendiente_NoSePideOtro_AunqueSeaDesdeUnDispositivoReconocido()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        await Pedir(cambio, cuenta, marca, region: "BR");
        var otraMarca = e.DispositivoReconocido(cuenta, "PE");

        var resultado = await Pedir(cambio, cuenta, otraMarca, correo: "otro@example.com");

        Assert.Equal("cambio-pendiente", resultado.Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-48")]
    public async Task PasadasLas24Horas_ElCambioPendienteSeDescarta_YNoSePuedeConfirmar_YSePuedePedirOtro()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        await Pedir(cambio, cuenta, marca, region: "BR");
        var otraMarca = e.DispositivoReconocido(cuenta, "PE");
        e.Reloj.Ahora = e.Reloj.Ahora.AddHours(25);

        var tarde = await cambio.ConfirmarElCambioPendiente(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), otraMarca, "PE");

        Assert.Equal("cambio-vencido", tarde.Error!.Clave);
        Assert.Equal(EstadoDelCambioDeCorreo.Descartado, e.Bd.Cambios.Single().Estado);
        Assert.Equal(Antiguo, cuenta.Usuario.Correo);
        Assert.True((await Pedir(cambio, cuenta, otraMarca, correo: "otro@example.com")).EsExito);
    }

    // ---------------------------------------------------------------- confirmar y rechazar

    [Fact]
    [Trait("Criterio", "AC-IDE-48")]
    public async Task ElTitularConfirma_DesdeUnDispositivoReconocido_ConSuContrasena_ElCodigoSaleAlCorreoNuevo_Audita_YAlVerificarSeAplica()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        await Pedir(cambio, cuenta, marca, region: "BR");
        var otraMarca = e.DispositivoReconocido(cuenta, "PE");
        e.Correo.Correos.Clear();

        var confirmado = await cambio.ConfirmarElCambioPendiente(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), otraMarca, "PE");

        Assert.True(confirmado.EsExito);
        Assert.Equal(EstadoDelCambioDeCorreo.EsperandoVerificacion, e.Bd.Cambios.Single().Estado);
        Assert.Equal(Nuevo, Assert.Single(e.Correo.Codigos).Destinatario);
        Assert.Equal(AccionesAuditables.CambioDeCorreoNoReconocidoConfirmado, Assert.Single(e.Auditoria.Registradas).Accion);
        Assert.Equal(Antiguo, cuenta.Usuario.Correo);

        var verificado = await cambio.VerificarElCorreoNuevo(cuenta.Usuario.Id, S(e.UltimoCodigoEnviado()));
        Assert.True(verificado.EsExito);
        Assert.Equal(Nuevo, cuenta.Usuario.Correo);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-48")]
    public async Task UnCambioNoReconocido_NoSeAplicaSoloPorVerificarElCorreoNuevo()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        await Pedir(cambio, cuenta, marca, region: "BR");

        // Aunque alguien tuviera un código del correo nuevo, un cambio pendiente de confirmación no se aplica.
        var resultado = await cambio.VerificarElCorreoNuevo(cuenta.Usuario.Id, S("123456"));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, resultado.Error);
        Assert.Equal(Antiguo, cuenta.Usuario.Correo);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-48")]
    public async Task ConfirmarOrechazar_DesdeElDispositivoCongelado_OSinMarca_EsProhibido_YQuedaRegistrado()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        await Pedir(cambio, cuenta, marca, region: "BR");
        var id = cuenta.Usuario.Id;

        var desdeElCongelado = await cambio.ConfirmarElCambioPendiente(id, S(EntornoDeIdentidad.ContrasenaSegura), marca, "PE");
        var sinMarca = await cambio.RechazarElCambioPendiente(id, S(EntornoDeIdentidad.ContrasenaSegura), null, "PE");

        Assert.Equal("dispositivo-no-reconocido", desdeElCongelado.Error!.Clave);
        Assert.Equal("dispositivo-no-reconocido", sinMarca.Error!.Clave);
        Assert.Equal(2, e.Denegaciones.Registradas.Count);
        Assert.Equal(EstadoDelCambioDeCorreo.PendienteDeConfirmacion, e.Bd.Cambios.Single().Estado);
    }

    [Fact]
    public async Task Confirmar_ConUnaContrasenaIncorrecta_SeRechaza()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        await Pedir(cambio, cuenta, marca, region: "BR");
        var otraMarca = e.DispositivoReconocido(cuenta, "PE");

        var resultado = await cambio.ConfirmarElCambioPendiente(cuenta.Usuario.Id, S("equivocada y larga de verdad"), otraMarca, "PE");

        Assert.Equal(Reautenticacion.ContrasenaActualIncorrecta, resultado.Error);
        Assert.Equal(EstadoDelCambioDeCorreo.PendienteDeConfirmacion, e.Bd.Cambios.Single().Estado);
    }

    [Fact]
    public async Task ConfirmarORechazar_SinCambioPendiente_EsUnConflicto()
    {
        var (_, cuenta, cambio, marca) = Escenario();

        var confirmar = await cambio.ConfirmarElCambioPendiente(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), marca, "PE");
        var rechazar = await cambio.RechazarElCambioPendiente(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), marca, "PE");

        Assert.Equal("sin-cambio-pendiente", confirmar.Error!.Clave);
        Assert.Equal("sin-cambio-pendiente", rechazar.Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-48")]
    public async Task ElTitularRechaza_ElCambioNoSeAplica_QuedaElEventoDeSeguridad_YElDispositivoSigueCongelado()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        await Pedir(cambio, cuenta, marca, region: "BR");
        var otraMarca = e.DispositivoReconocido(cuenta, "PE");

        var rechazado = await cambio.RechazarElCambioPendiente(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), otraMarca, "PE");

        Assert.True(rechazado.EsExito);
        Assert.Equal(EstadoDelCambioDeCorreo.Rechazado, e.Bd.Cambios.Single().Estado);
        Assert.Equal(Antiguo, cuenta.Usuario.Correo);
        Assert.Contains(e.Seguridad.Eventos, ev => ev.Accion == AccionesAuditables.CambioDeCorreoNoReconocidoRechazado);
        Assert.Empty(e.Correo.Codigos);
        // Podría ser el dispositivo del atacante: rechazar no lo libera hasta que venza.
        Assert.True(e.Bd.Dispositivos.Single(d => d.HuellaDeLaMarca == e.HuellasDeIdentidad.DeValorOpaco(marca.Revelar())).EstaCongelado(e.Reloj.Ahora));
        Assert.Equal("sin-cambio-pendiente", (await cambio.ConfirmarElCambioPendiente(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), otraMarca, "PE")).Error!.Clave);
    }

    [Fact]
    public async Task Confirmar_SiLaAuditoriaFalla_ElCambioSigueSinConfirmar()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        await Pedir(cambio, cuenta, marca, region: "BR");
        var otraMarca = e.DispositivoReconocido(cuenta, "PE");
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => cambio.ConfirmarElCambioPendiente(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), otraMarca, "PE"));

        Assert.Equal(EstadoDelCambioDeCorreo.PendienteDeConfirmacion, e.Bd.Cambios.Single().Estado);
        Assert.Empty(e.Correo.Codigos);
    }

    // ---------------------------------------------------------------- registro de dispositivos

    [Fact]
    [Trait("Criterio", "AC-IDE-47")]
    public async Task RegistrarElIngresoCompleto_SinMarca_EmiteUnaNueva_YGuardaSoloSuHuellaYLaRegion()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta(Antiguo);
        e.ActuarComo(cuenta);

        var resultado = await e.DispositivosDeLaCuenta().RegistrarElIngresoCompleto(cuenta.Usuario.Id, null, "pe");

        var marca = resultado.Valor.MarcaNueva!.Revelar();
        var guardado = Assert.Single(e.Bd.Dispositivos);
        Assert.Equal(e.HuellasDeIdentidad.DeValorOpaco(marca), guardado.HuellaDeLaMarca);
        Assert.NotEqual(marca, guardado.HuellaDeLaMarca);
        Assert.Equal("PE", guardado.Region);
        Assert.Equal(e.Reloj.Ahora, guardado.Emitido);
    }

    [Fact]
    public async Task RegistrarElIngresoCompleto_ConUnaMarcaConocida_NoEmiteOtra_YConUnaDesconocida_Si()
    {
        var (e, cuenta, _, marca) = Escenario();
        var registro = e.DispositivosDeLaCuenta();

        var conocida = await registro.RegistrarElIngresoCompleto(cuenta.Usuario.Id, marca, "PE");
        var desconocida = await registro.RegistrarElIngresoCompleto(cuenta.Usuario.Id, S("marca-que-no-esta-guardada"), "PE");

        Assert.Null(conocida.Valor.MarcaNueva);
        Assert.NotNull(desconocida.Valor.MarcaNueva);
        Assert.Equal(2, e.Bd.Dispositivos.Count);
    }

    [Fact]
    public async Task RegistrarElIngresoCompleto_ConservaComoMaximoDiezDispositivos()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta(Antiguo);
        e.ActuarComo(cuenta);
        var registro = e.DispositivosDeLaCuenta();

        for (var i = 0; i < 13; i++)
        {
            e.Reloj.Ahora = e.Reloj.Ahora.AddMinutes(1);
            await registro.RegistrarElIngresoCompleto(cuenta.Usuario.Id, null, "PE");
        }

        Assert.Equal(IRepositorioDeDispositivos.Maximo, e.Bd.Dispositivos.Count);
    }

    [Fact]
    public async Task RegistrarElIngresoCompleto_DeOtraPersona_EsProhibido()
    {
        var (e, cuenta, _, _) = Escenario();
        e.Actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), "cliente");

        var resultado = await e.DispositivosDeLaCuenta().RegistrarElIngresoCompleto(cuenta.Usuario.Id, null, "PE");

        Assert.Equal(AlcanceDeLaCuenta.Prohibido, resultado.Error);
        Assert.Single(e.Bd.Dispositivos);
    }

    // ---------------------------------------------------------------- correcciones de la revisión de IDE-4

    [Fact]
    [Trait("Criterio", "AC-IDE-47")]
    public async Task ElLimiteDeCodigos_SeConsumeIgualParaUnCorreoLibreQueParaUnoOcupado_SinOraculoDeEnumeracion()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        e.Cuenta("ocupado@example.com");

        ErrorDeAplicacion? libre = null;
        ErrorDeAplicacion? ocupado = null;
        for (var i = 0; i < 4; i++)
        {
            libre = (await Pedir(cambio, cuenta, marca, correo: "libre@example.com")).Error;
            ocupado = (await Pedir(cambio, cuenta, marca, correo: "ocupado@example.com")).Error;
        }

        Assert.Equal(FallosDeIdentidad.LimiteExcedido, libre);
        Assert.Equal(FallosDeIdentidad.LimiteExcedido, ocupado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-48")]
    public async Task MientrasHayUnCambioNoReconocidoPendiente_SinMarcaOConUnaInmadura_NoSeHacenCambiosSensibles()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        // Una sesión robada, sin la cookie, pide el cambio: nada se congela en el dispositivo, pero la cuenta sí.
        await Pedir(cambio, cuenta, marcaNula: true);
        var gestion = e.Gestion();
        var id = cuenta.Usuario.Id;
        var nueva = S("otra frase larga y distinta");
        var inmadura = (await e.DispositivosDeLaCuenta().RegistrarElIngresoCompleto(id, null, "PE")).Valor.MarcaNueva!;

        Assert.Equal(ConfianzaDelDispositivo.DispositivoCongelado, (await gestion.CambiarLaContrasena(id, S(EntornoDeIdentidad.ContrasenaSegura), nueva)).Error);
        Assert.Equal(ConfianzaDelDispositivo.DispositivoCongelado, (await gestion.IniciarElSegundoFactor(id, S(EntornoDeIdentidad.ContrasenaSegura))).Error);
        Assert.Equal(ConfianzaDelDispositivo.DispositivoCongelado, (await gestion.CambiarLaContrasena(id, S(EntornoDeIdentidad.ContrasenaSegura), nueva, marcaDeDispositivo: inmadura)).Error);
        Assert.Equal(ConfianzaDelDispositivo.DispositivoCongelado, (await gestion.CambiarLaContrasena(id, S(EntornoDeIdentidad.ContrasenaSegura), nueva, marcaDeDispositivo: S("marca-desconocida"))).Error);

        // Quien sí trae un dispositivo reconocido sigue pudiendo, y sin cambio pendiente nadie queda bloqueado por no traer marca.
        Assert.True((await gestion.CambiarLaContrasena(id, S(EntornoDeIdentidad.ContrasenaSegura), nueva, marcaDeDispositivo: marca)).EsExito);
    }

    [Fact]
    public async Task SinCambioPendiente_UnaSolicitudSinMarca_SigueHaciendoCambiosSensibles()
    {
        var (e, cuenta, _, _) = Escenario();

        var resultado = await e.Gestion().CambiarLaContrasena(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S("otra frase larga y distinta"));

        Assert.True(resultado.EsExito);
    }

    [Fact]
    public async Task UnNuevoCambio_InvalidaElCodigoDelAnterior_YElCodigoViejoNoAplicaElCorreoNuevo()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        await Pedir(cambio, cuenta, marca, correo: "primero@example.com");
        var codigoDelPrimero = e.UltimoCodigoEnviado();
        // Si el segundo cambio se guarda pero el envío del código falla, el código viejo no puede aplicar el segundo correo.
        e.Correo.Falla = true;
        await Pedir(cambio, cuenta, marca, correo: "segundo@example.com");
        e.Correo.Falla = false;

        var resultado = await cambio.VerificarElCorreoNuevo(cuenta.Usuario.Id, S(codigoDelPrimero));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, resultado.Error);
        Assert.Equal(Antiguo, cuenta.Usuario.Correo);
    }

    [Fact]
    public async Task ElLimiteDeCodigos_SeReservaAntesDeConfirmar_YSiSeExcedeElCambioSigueSinConfirmar()
    {
        var (e, cuenta, cambio, marca) = Escenario();
        await Pedir(cambio, cuenta, marca, region: "BR");
        var otraMarca = e.DispositivoReconocido(cuenta, "PE");
        e.Limites.Maximos["codigos-por-correo"] = 0;

        var resultado = await cambio.ConfirmarElCambioPendiente(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), otraMarca, "PE");

        Assert.Equal(FallosDeIdentidad.LimiteExcedido, resultado.Error);
        Assert.Equal(EstadoDelCambioDeCorreo.PendienteDeConfirmacion, e.Bd.Cambios.Single().Estado);
    }
}
