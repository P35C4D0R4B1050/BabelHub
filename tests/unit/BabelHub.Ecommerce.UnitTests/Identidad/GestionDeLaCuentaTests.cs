using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// La cuenta del cliente (identidad-seguridad T18; AC-IDE-10, AC-IDE-22, AC-IDE-28, AC-IDE-34, AC-IDE-46; IDE-11, IDE-24, IDE-25, IDE-31, IDE-37, IDE-44, IDE-45, IDE-52): solo el titular opera su cuenta, las operaciones
/// sensibles piden la contraseña actual (y un código del segundo factor), el número y el consentimiento cancelan los avisos pendientes y cada cambio se audita en la misma transacción.
/// </summary>
public sealed class GestionDeLaCuentaTests
{
    private const string Nueva = "otra frase larga y distinta";

    private static (EntornoDeIdentidad E, CuentaDeIdentidad Cuenta, GestionDeLaCuenta Gestion) Titular()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta();
        e.ActuarComo(cuenta);
        return (e, cuenta, e.Gestion());
    }

    private static ValorSecreto S(string texto) => new(texto);

    // ---------------------------------------------------------------- alcance y lectura

    [Fact]
    [Trait("Criterio", "AC-IDE-22")]
    public async Task Consultar_ComoTitular_MuestraSusDatos_SinSecretos()
    {
        var (_, cuenta, gestion) = Titular();

        var resultado = await gestion.Consultar(cuenta.Usuario.Id);

        Assert.True(resultado.EsExito);
        Assert.Equal(cuenta.Usuario.Correo, resultado.Valor.Correo);
        Assert.Equal("Persona", resultado.Valor.Nombre);
        Assert.False(resultado.Valor.SegundoFactorActivo);
        Assert.Equal(0, resultado.Valor.CodigosDeRespaldoRestantes);
        Assert.Null(resultado.Valor.NumeroDeWhatsApp);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-22")]
    public async Task CualquierOperacion_DeOtraPersona_EsProhibida_YQuedaRegistradaLaDenegacion()
    {
        var (e, cuenta, gestion) = Titular();
        e.Actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), "cliente");
        var id = cuenta.Usuario.Id;

        var resultados = new[]
        {
            (await gestion.Consultar(id)).Error,
            (await gestion.CorregirElNombre(id, "Otra")).Error,
            (await gestion.CambiarLaContrasena(id, S(EntornoDeIdentidad.ContrasenaSegura), S(Nueva))).Error,
            (await gestion.GuardarElNumeroDeWhatsApp(id, "+51999111222")).Error,
            (await gestion.CambiarElConsentimientoDeWhatsApp(id, false, null)).Error,
            (await gestion.ConsultarLaOfertaDeConsentimiento(id)).Error,
            (await gestion.GuardarElConsentimientoPorHuella(id, "+51*****222")).Error,
            (await gestion.IniciarElSegundoFactor(id, S(EntornoDeIdentidad.ContrasenaSegura))).Error,
            (await gestion.ConfirmarElSegundoFactor(id, S("123456"))).Error,
            (await gestion.DesactivarElSegundoFactor(id, S(EntornoDeIdentidad.ContrasenaSegura), S("123456"))).Error,
            (await gestion.RegenerarLosCodigosDeRespaldo(id, S(EntornoDeIdentidad.ContrasenaSegura), S("123456"))).Error,
            (await gestion.ListarLosAvisos(id)).Error,
            (await gestion.MarcarElAvisoComoVisto(id, Guid.NewGuid(), true)).Error,
        };

        Assert.All(resultados, error => Assert.Equal(AlcanceDeLaCuenta.Prohibido, error));
        Assert.Equal(resultados.Length, e.Denegaciones.Registradas.Count);
        Assert.Equal("Persona", cuenta.Nombre);
        Assert.Empty(e.Auditoria.Registradas);
    }

    [Theory]
    [InlineData("anonimo")]
    [InlineData("sistema")]
    public async Task Consultar_SinSerUnUsuario_EsProhibido(string actor)
    {
        var (e, cuenta, gestion) = Titular();
        e.Actor.Actual = actor == "anonimo" ? ActorActual.Anonimo() : ActorActual.DelSistema();

        var resultado = await gestion.Consultar(cuenta.Usuario.Id);

        Assert.Equal(AlcanceDeLaCuenta.Prohibido, resultado.Error);
    }

    [Fact]
    public async Task Consultar_DeUnaCuentaCancelada_NoLaEncuentra()
    {
        var (_, cuenta, gestion) = Titular();
        cuenta.CanceladaEn = EntornoDeIdentidad.Inicio;

        var resultado = await gestion.Consultar(cuenta.Usuario.Id);

        Assert.Equal(AlcanceDeLaCuenta.CuentaNoEncontrada, resultado.Error);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-22")]
    public async Task CorregirElNombre_Valido_LoGuarda_YElInvalido_SeRechaza()
    {
        var (_, cuenta, gestion) = Titular();

        var bien = await gestion.CorregirElNombre(cuenta.Usuario.Id, "  Ana María  ");
        var vacio = await gestion.CorregirElNombre(cuenta.Usuario.Id, "   ");
        var largo = await gestion.CorregirElNombre(cuenta.Usuario.Id, new string('a', 101));

        Assert.Equal("Ana María", bien.Valor.Nombre);
        Assert.Equal("Ana María", cuenta.Nombre);
        Assert.Equal(CategoriaDeError.Validacion, vacio.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, largo.Error!.Categoria);
    }

    // ---------------------------------------------------------------- contraseña

    [Fact]
    [Trait("Criterio", "AC-IDE-22")]
    [Trait("Criterio", "AC-IDE-40")]
    public async Task CambiarLaContrasena_ConLaActual_Cambia_Audita_Avisa_YRevocaLasDemasSesiones()
    {
        var (e, cuenta, gestion) = Titular();
        var propia = await e.Sesiones.Nueva(cuenta.Usuario, e.Reloj.Ahora, default);
        var otra = await e.Sesiones.Nueva(cuenta.Usuario, e.Reloj.Ahora, default);

        var resultado = await gestion.CambiarLaContrasena(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S(Nueva), propia.Renovacion);

        Assert.True(resultado.EsExito);
        Assert.Equal(HasherDePrueba.ResumenDe(PoliticaDeContrasenas.Normalizar(Nueva)), cuenta.ResumenDeAcceso);
        var auditada = Assert.Single(e.Auditoria.Registradas);
        Assert.Equal(AccionesAuditables.ContrasenaCambiada, auditada.Accion);
        var aviso = Assert.Single(e.Correo.Correos);
        Assert.Equal(AvisosDeCuenta.PlantillaDeContrasenaCambiada, aviso.Plantilla);
        Assert.DoesNotContain(Nueva, aviso.Plantilla, StringComparison.Ordinal);
        var gestor = e.Gestor();
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, (await gestor.Renovar(otra.Renovacion)).Error);
        Assert.True((await gestor.Renovar(propia.Renovacion)).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-22")]
    public async Task CambiarLaContrasena_SinLaRenovacionDeLaSesion_RevocaTodas()
    {
        var (e, cuenta, gestion) = Titular();
        var sesion = await e.Sesiones.Nueva(cuenta.Usuario, e.Reloj.Ahora, default);

        var resultado = await gestion.CambiarLaContrasena(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S(Nueva));

        Assert.True(resultado.EsExito);
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, (await e.Gestor().Renovar(sesion.Renovacion)).Error);
    }

    [Theory]
    [InlineData("incorrecta y larga de verdad")]
    [InlineData("   ")]
    [Trait("Criterio", "AC-IDE-22")]
    public async Task CambiarLaContrasena_SinLaActual_SeRechaza_YNoCambiaNada(string presentada)
    {
        var (e, cuenta, gestion) = Titular();
        var antes = cuenta.ResumenDeAcceso;

        var resultado = await gestion.CambiarLaContrasena(cuenta.Usuario.Id, S(presentada), S(Nueva));

        Assert.Equal(Reautenticacion.ContrasenaActualIncorrecta, resultado.Error);
        Assert.Equal(antes, cuenta.ResumenDeAcceso);
        Assert.Empty(e.Auditoria.Registradas);
        Assert.Empty(e.Correo.Correos);
        Assert.Contains(e.Seguridad.Eventos, ev => ev.Accion == AccionesAuditables.IngresoFallido);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-22")]
    public async Task CambiarLaContrasena_ContraseñaActualEquivocadaRepetida_CuentaContraElLimite()
    {
        var (_, cuenta, gestion) = Titular();

        ErrorDeAplicacion? ultimo = null;
        for (var i = 0; i < 6; i++)
        {
            ultimo = (await gestion.CambiarLaContrasena(cuenta.Usuario.Id, S("equivocada y larga de verdad"), S(Nueva))).Error;
        }

        Assert.Equal(FallosDeIdentidad.LimiteExcedido, ultimo);
        // Con el límite agotado, ni la correcta pasa.
        Assert.Equal(FallosDeIdentidad.LimiteExcedido, (await gestion.CambiarLaContrasena(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S(Nueva))).Error);
    }

    [Fact]
    public async Task CambiarLaContrasena_AcertarLaActual_DevuelveElCupo()
    {
        var (e, cuenta, gestion) = Titular();

        await gestion.CambiarLaContrasena(cuenta.Usuario.Id, S("equivocada y larga de verdad"), S(Nueva));
        await gestion.CambiarLaContrasena(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S(Nueva));

        var huella = e.Limites.Reservas.Where(r => r.Politica == "ingresos-fallidos").Select(r => r.Sujeto).Distinct().Single();
        Assert.Equal(1, e.Limites.Cuenta("ingresos-fallidos", huella));
    }

    [Fact]
    public async Task CambiarLaContrasena_SiElAlmacenDeLimitesCae_FallaCerrado()
    {
        var (e, cuenta, gestion) = Titular();
        e.Limites.Caido = true;
        var antes = cuenta.ResumenDeAcceso;

        var resultado = await gestion.CambiarLaContrasena(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S(Nueva));

        Assert.Equal(FallosDeIdentidad.NoDisponible, resultado.Error);
        Assert.Equal(antes, cuenta.ResumenDeAcceso);
    }

    [Theory]
    [InlineData("corta")]
    [InlineData("contrasena123")]
    public async Task CambiarLaContrasena_NuevaQueNoCumpleLaPolitica_SeRechaza(string nueva)
    {
        var (_, cuenta, gestion) = Titular();
        var antes = cuenta.ResumenDeAcceso;

        var resultado = await gestion.CambiarLaContrasena(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S(nueva));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal(antes, cuenta.ResumenDeAcceso);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-22")]
    public async Task CambiarLaContrasena_SiLaAuditoriaFalla_NoSeCambiaNiSeRevocaNada()
    {
        var (e, cuenta, gestion) = Titular();
        var sesion = await e.Sesiones.Nueva(cuenta.Usuario, e.Reloj.Ahora, default);
        var antes = cuenta.ResumenDeAcceso;
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => gestion.CambiarLaContrasena(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S(Nueva)));

        Assert.Equal(antes, cuenta.ResumenDeAcceso);
        Assert.Empty(e.Correo.Correos);
        Assert.Null(e.Bd.Renovaciones.Single(r => r.Familia != Guid.Empty).RevocadaEn);
        Assert.True((await e.Gestor().Renovar(sesion.Renovacion)).EsExito);
    }

    [Fact]
    public async Task CambiarLaContrasena_SiElCorreoFalla_ElCambioYaEstaHecho()
    {
        var (e, cuenta, gestion) = Titular();
        e.Correo.Falla = true;

        var resultado = await gestion.CambiarLaContrasena(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S(Nueva));

        Assert.True(resultado.EsExito);
        Assert.Equal(HasherDePrueba.ResumenDe(PoliticaDeContrasenas.Normalizar(Nueva)), cuenta.ResumenDeAcceso);
    }

    // ---------------------------------------------------------------- WhatsApp

    [Fact]
    [Trait("Criterio", "AC-IDE-28")]
    public async Task GuardarElNumero_ConFormatoInvalido_SeRechaza_YConFormatoValido_SeGuardaSinConsentimiento()
    {
        var (e, cuenta, gestion) = Titular();

        var invalido = await gestion.GuardarElNumeroDeWhatsApp(cuenta.Usuario.Id, "999111222");
        var valido = await gestion.GuardarElNumeroDeWhatsApp(cuenta.Usuario.Id, "+51999111222");

        Assert.Equal(CategoriaDeError.Validacion, invalido.Error!.Categoria);
        Assert.True(valido.EsExito);
        Assert.Equal("+51999111222", valido.Valor.NumeroDeWhatsApp);
        Assert.False(valido.Valor.ConsentimientoDeWhatsApp);
        // Es un número nuevo: lo anterior (nada) se cancela.
        Assert.Contains(cuenta.Usuario.Id, e.CancelacionDeAvisos.Cancelados);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-28")]
    public async Task GuardarElNumero_Distinto_DejaSinEfectoElConsentimiento_YCancelaLosAvisosPendientes()
    {
        var (e, cuenta, gestion) = Titular();
        await gestion.GuardarElNumeroDeWhatsApp(cuenta.Usuario.Id, "+51999111222");
        await gestion.CambiarElConsentimientoDeWhatsApp(cuenta.Usuario.Id, true, "+51999111222");
        e.CancelacionDeAvisos.Cancelados.Clear();

        var cambio = await gestion.GuardarElNumeroDeWhatsApp(cuenta.Usuario.Id, "+51988777666");

        Assert.False(cambio.Valor.ConsentimientoDeWhatsApp);
        Assert.False(cuenta.Whatsapp.PuedeRecibirAvisos);
        Assert.Single(e.CancelacionDeAvisos.Cancelados);

        // Hasta confirmar para el número nuevo no recibe avisos; citar el anterior no sirve.
        Assert.Equal(CategoriaDeError.Validacion, (await gestion.CambiarElConsentimientoDeWhatsApp(cuenta.Usuario.Id, true, "+51999111222")).Error!.Categoria);
        Assert.True((await gestion.CambiarElConsentimientoDeWhatsApp(cuenta.Usuario.Id, true, "+51988777666")).Valor.ConsentimientoDeWhatsApp);
    }

    [Fact]
    public async Task GuardarElNumero_Igual_NoCancelaNada()
    {
        var (e, cuenta, gestion) = Titular();
        await gestion.GuardarElNumeroDeWhatsApp(cuenta.Usuario.Id, "+51999111222");
        await gestion.CambiarElConsentimientoDeWhatsApp(cuenta.Usuario.Id, true, "+51999111222");
        e.CancelacionDeAvisos.Cancelados.Clear();

        var igual = await gestion.GuardarElNumeroDeWhatsApp(cuenta.Usuario.Id, "+51999111222");

        Assert.True(igual.Valor.ConsentimientoDeWhatsApp);
        Assert.Empty(e.CancelacionDeAvisos.Cancelados);
    }

    [Fact]
    public async Task GuardarElNumero_Nulo_LoQuita_YCancelaLosAvisos()
    {
        var (e, cuenta, gestion) = Titular();
        await gestion.GuardarElNumeroDeWhatsApp(cuenta.Usuario.Id, "+51999111222");
        e.CancelacionDeAvisos.Cancelados.Clear();

        var quitado = await gestion.GuardarElNumeroDeWhatsApp(cuenta.Usuario.Id, null);

        Assert.Null(quitado.Valor.NumeroDeWhatsApp);
        Assert.False(quitado.Valor.ConsentimientoDeWhatsApp);
        Assert.Single(e.CancelacionDeAvisos.Cancelados);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-10")]
    public async Task Revocar_ElConsentimiento_GuardaLaFecha_YCancelaLosAvisosSiguientes()
    {
        var (e, cuenta, gestion) = Titular();
        await gestion.GuardarElNumeroDeWhatsApp(cuenta.Usuario.Id, "+51999111222");
        await gestion.CambiarElConsentimientoDeWhatsApp(cuenta.Usuario.Id, true, "+51999111222");
        e.CancelacionDeAvisos.Cancelados.Clear();
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddDays(2);

        var revocado = await gestion.CambiarElConsentimientoDeWhatsApp(cuenta.Usuario.Id, false, null);

        Assert.False(revocado.Valor.ConsentimientoDeWhatsApp);
        Assert.False(cuenta.Whatsapp.PuedeRecibirAvisos);
        Assert.Equal(e.Reloj.Ahora, cuenta.Whatsapp.Fecha);
        Assert.Single(e.CancelacionDeAvisos.Cancelados);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-10")]
    public async Task Consentir_SinNumeroOCitandoOtro_SeRechaza()
    {
        var (_, cuenta, gestion) = Titular();

        var sinNumero = await gestion.CambiarElConsentimientoDeWhatsApp(cuenta.Usuario.Id, true, "+51999111222");
        await gestion.GuardarElNumeroDeWhatsApp(cuenta.Usuario.Id, "+51999111222");
        var otro = await gestion.CambiarElConsentimientoDeWhatsApp(cuenta.Usuario.Id, true, "+51988777666");

        Assert.Equal(CategoriaDeError.Validacion, sinNumero.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, otro.Error!.Categoria);
        Assert.False(cuenta.Whatsapp.Consentido);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-10")]
    public async Task Revocar_SiElModuloDeAvisosFalla_NoCambiaNada()
    {
        var (e, cuenta, gestion) = Titular();
        await gestion.GuardarElNumeroDeWhatsApp(cuenta.Usuario.Id, "+51999111222");
        await gestion.CambiarElConsentimientoDeWhatsApp(cuenta.Usuario.Id, true, "+51999111222");
        e.CancelacionDeAvisos.Falla = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => gestion.CambiarElConsentimientoDeWhatsApp(cuenta.Usuario.Id, false, null));

        Assert.True(cuenta.Whatsapp.PuedeRecibirAvisos);
    }

    [Fact]
    public async Task LaOferta_SinNumero_NoSeOfrece_ConNumero_SeOfreceConLaHuellaEnmascarada_YElNumeroNoSale()
    {
        var (_, cuenta, gestion) = Titular();

        var sin = await gestion.ConsultarLaOfertaDeConsentimiento(cuenta.Usuario.Id);
        await gestion.GuardarElNumeroDeWhatsApp(cuenta.Usuario.Id, "+51999111222");
        var con = await gestion.ConsultarLaOfertaDeConsentimiento(cuenta.Usuario.Id);

        Assert.False(sin.Valor.SeOfrece);
        Assert.Null(sin.Valor.HuellaEnmascaradaDelNumero);
        Assert.True(con.Valor.SeOfrece);
        Assert.False(con.Valor.YaConsiente);
        Assert.Equal("+51******222", con.Valor.HuellaEnmascaradaDelNumero);
        Assert.NotEqual("+51999111222", con.Valor.HuellaEnmascaradaDelNumero);
    }

    [Fact]
    public async Task LaOferta_YaConsentida_NoSeOfrece()
    {
        var (_, cuenta, gestion) = Titular();
        await gestion.GuardarElNumeroDeWhatsApp(cuenta.Usuario.Id, "+51999111222");
        await gestion.CambiarElConsentimientoDeWhatsApp(cuenta.Usuario.Id, true, "+51999111222");

        var oferta = await gestion.ConsultarLaOfertaDeConsentimiento(cuenta.Usuario.Id);

        Assert.False(oferta.Valor.SeOfrece);
        Assert.True(oferta.Valor.YaConsiente);
        Assert.Null(oferta.Valor.HuellaEnmascaradaDelNumero);
    }

    [Fact]
    public async Task GuardarElConsentimientoPorHuella_ConLaHuellaVigente_Consiente()
    {
        var (_, cuenta, gestion) = Titular();
        await gestion.GuardarElNumeroDeWhatsApp(cuenta.Usuario.Id, "+51999111222");
        var oferta = (await gestion.ConsultarLaOfertaDeConsentimiento(cuenta.Usuario.Id)).Valor;

        var guardado = await gestion.GuardarElConsentimientoPorHuella(cuenta.Usuario.Id, oferta.HuellaEnmascaradaDelNumero!);

        Assert.True(guardado.EsExito);
        Assert.True(cuenta.Whatsapp.PuedeRecibirAvisos);
    }

    [Fact]
    public async Task GuardarElConsentimientoPorHuella_SiElNumeroCambioEntreTanto_NoGuardaNada()
    {
        var (_, cuenta, gestion) = Titular();
        await gestion.GuardarElNumeroDeWhatsApp(cuenta.Usuario.Id, "+51999111222");
        var oferta = (await gestion.ConsultarLaOfertaDeConsentimiento(cuenta.Usuario.Id)).Valor;
        await gestion.GuardarElNumeroDeWhatsApp(cuenta.Usuario.Id, "+51988777666");

        var guardado = await gestion.GuardarElConsentimientoPorHuella(cuenta.Usuario.Id, oferta.HuellaEnmascaradaDelNumero!);

        Assert.Equal(CategoriaDeError.Conflicto, guardado.Error!.Categoria);
        Assert.False(cuenta.Whatsapp.Consentido);
    }

    [Fact]
    public async Task GuardarElConsentimientoPorHuella_SinNumero_NoGuardaNada()
    {
        var (_, cuenta, gestion) = Titular();

        var guardado = await gestion.GuardarElConsentimientoPorHuella(cuenta.Usuario.Id, "+51******222");

        Assert.Equal(CategoriaDeError.Conflicto, guardado.Error!.Categoria);
    }

    // ---------------------------------------------------------------- segundo factor

    [Fact]
    [Trait("Criterio", "AC-IDE-34")]
    public async Task ActivarElSegundoFactor_PideLaContrasena_EntregaElSecreto_YNoQuedaActivoHastaConfirmar()
    {
        var (e, cuenta, gestion) = Titular();

        var sinContrasena = await gestion.IniciarElSegundoFactor(cuenta.Usuario.Id, S("equivocada y larga de verdad"));
        var inicio = await gestion.IniciarElSegundoFactor(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura));

        Assert.Equal(Reautenticacion.ContrasenaActualIncorrecta, sinContrasena.Error);
        Assert.True(inicio.EsExito);
        Assert.False(string.IsNullOrEmpty(inicio.Valor.SecretoBase32.Revelar()));
        Assert.Equal(cuenta.Usuario.Correo, inicio.Valor.Cuenta);
        Assert.False((await gestion.Consultar(cuenta.Usuario.Id)).Valor.SegundoFactorActivo);
        Assert.Empty(e.Auditoria.Registradas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-34")]
    public async Task ActivarElSegundoFactor_ConUnCodigoValido_EntregaOchoCodigos_Audita_RevocaLasDemas_YElSiguienteIngresoLoPide()
    {
        var (e, cuenta, gestion) = Titular();
        var propia = await e.Sesiones.Nueva(cuenta.Usuario, e.Reloj.Ahora, default);
        var otra = await e.Sesiones.Nueva(cuenta.Usuario, e.Reloj.Ahora, default);
        await gestion.IniciarElSegundoFactor(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura));
        var secreto = e.Secretos.SecretoDelSegundoFactor();

        var confirmado = await gestion.ConfirmarElSegundoFactor(cuenta.Usuario.Id, S(e.Totp(secreto)), propia.Renovacion);

        Assert.True(confirmado.EsExito);
        Assert.Equal(CodigosDeRespaldo.Cantidad, confirmado.Valor.Count);
        Assert.All(confirmado.Valor, c => Assert.True(CodigosDeRespaldo.TieneLaForma(c.Revelar())));
        Assert.Equal(AccionesAuditables.SegundoFactorActivado, Assert.Single(e.Auditoria.Registradas).Accion);
        var estado = (await gestion.Consultar(cuenta.Usuario.Id)).Valor;
        Assert.True(estado.SegundoFactorActivo);
        Assert.Equal(CodigosDeRespaldo.Cantidad, estado.CodigosDeRespaldoRestantes);
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, (await e.Gestor().Renovar(otra.Renovacion)).Error);
        Assert.True((await e.Gestor().Renovar(propia.Renovacion)).EsExito);

        var ingreso = await e.Ingreso().Ingresar(new SolicitudDeIngreso(cuenta.Usuario.Correo, S(EntornoDeIdentidad.ContrasenaSegura)));
        Assert.Equal(PasoPendienteDelIngreso.PresentarSegundoFactor, ingreso.Valor.Desafio!.Paso);
        Assert.Null(ingreso.Valor.Credencial);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-34")]
    public async Task ActivarElSegundoFactor_ConUnCodigoIncorrecto_NoSeActiva_YDejaElEvento()
    {
        var (e, cuenta, gestion) = Titular();
        await gestion.IniciarElSegundoFactor(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura));

        var rechazado = await gestion.ConfirmarElSegundoFactor(cuenta.Usuario.Id, S("000000"));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, rechazado.Error);
        Assert.False((await gestion.Consultar(cuenta.Usuario.Id)).Valor.SegundoFactorActivo);
        Assert.Contains(e.Seguridad.Eventos, ev => ev.Accion == AccionesAuditables.SegundoFactorFallido);
        Assert.Empty(e.Auditoria.Registradas);
    }

    [Fact]
    public async Task ConfirmarElSegundoFactor_SinHaberlo_Iniciado_EsUnConflicto()
    {
        var (_, cuenta, gestion) = Titular();

        var resultado = await gestion.ConfirmarElSegundoFactor(cuenta.Usuario.Id, S("123456"));

        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
    }

    [Fact]
    public async Task IniciarElSegundoFactor_YaActivo_EsUnConflicto_YNoCambiaElSecreto()
    {
        var (e, cuenta, gestion) = Titular();
        var (secreto, _) = e.ConSegundoFactor(cuenta);

        var resultado = await gestion.IniciarElSegundoFactor(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura));

        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
        Assert.True((await gestion.DesactivarElSegundoFactor(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S(e.Totp(secreto)))).EsExito);
    }

    [Fact]
    public async Task ConfirmarElSegundoFactor_SiLaAuditoriaFalla_LanzaYNoEntregaCodigos()
    {
        var (e, cuenta, gestion) = Titular();
        await gestion.IniciarElSegundoFactor(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura));
        var secreto = e.Secretos.SecretoDelSegundoFactor();
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => gestion.ConfirmarElSegundoFactor(cuenta.Usuario.Id, S(e.Totp(secreto))));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-03")]
    public async Task DesactivarElSegundoFactor_SiLaAuditoriaFalla_LaOperacionFallaYNoQuedaNingunRegistro()
    {
        var (e, cuenta, gestion) = Titular();
        var (secreto, _) = e.ConSegundoFactor(cuenta);
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => gestion.DesactivarElSegundoFactor(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S(e.Totp(secreto))));

        Assert.Empty(e.Auditoria.Registradas);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-03")]
    public async Task RegenerarLosCodigosDeRespaldo_SiLaAuditoriaFalla_LaOperacionFallaYNoQuedaNingunRegistro()
    {
        var (e, cuenta, gestion) = Titular();
        var (secreto, _) = e.ConSegundoFactor(cuenta);
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => gestion.RegenerarLosCodigosDeRespaldo(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S(e.Totp(secreto))));

        Assert.Empty(e.Auditoria.Registradas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-34")]
    public async Task DesactivarElSegundoFactor_PideLaContrasenaYUnCodigoVigente()
    {
        var (e, cuenta, gestion) = Titular();
        var (secreto, _) = e.ConSegundoFactor(cuenta);
        var id = cuenta.Usuario.Id;

        var sinContrasena = await gestion.DesactivarElSegundoFactor(id, S("equivocada y larga de verdad"), S(e.Totp(secreto)));
        var sinCodigo = await gestion.DesactivarElSegundoFactor(id, S(EntornoDeIdentidad.ContrasenaSegura), S("000000"));
        var estadoIntermedio = (await gestion.Consultar(id)).Valor;
        var propia = await e.Sesiones.Nueva(cuenta.Usuario, e.Reloj.Ahora, default);
        var otra = await e.Sesiones.Nueva(cuenta.Usuario, e.Reloj.Ahora, default);
        var desactivado = await gestion.DesactivarElSegundoFactor(id, S(EntornoDeIdentidad.ContrasenaSegura), S(e.Totp(secreto)), propia.Renovacion);

        Assert.Equal(Reautenticacion.ContrasenaActualIncorrecta, sinContrasena.Error);
        Assert.Equal(FallosDeIdentidad.CodigoInvalido, sinCodigo.Error);
        Assert.True(estadoIntermedio.SegundoFactorActivo);
        Assert.True(desactivado.EsExito);
        Assert.False((await gestion.Consultar(id)).Valor.SegundoFactorActivo);
        Assert.Equal(AccionesAuditables.SegundoFactorDesactivado, Assert.Single(e.Auditoria.Registradas).Accion);
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, (await e.Gestor().Renovar(otra.Renovacion)).Error);
        Assert.True((await e.Gestor().Renovar(propia.Renovacion)).EsExito);
    }

    [Fact]
    public async Task DesactivarElSegundoFactor_ConUnCodigoDeRespaldo_Funciona_YElCodigoQuedaConsumido()
    {
        var (e, cuenta, gestion) = Titular();
        var (_, respaldo) = e.ConSegundoFactor(cuenta);

        var desactivado = await gestion.DesactivarElSegundoFactor(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S(respaldo[0]));

        Assert.True(desactivado.EsExito);
        Assert.False((await gestion.Consultar(cuenta.Usuario.Id)).Valor.SegundoFactorActivo);
    }

    [Fact]
    public async Task DesactivarElSegundoFactor_ConElMismoPasoYaUsado_SeRechaza()
    {
        var (e, cuenta, gestion) = Titular();
        var (secreto, _) = e.ConSegundoFactor(cuenta);
        var codigo = e.Totp(secreto);
        // Regenerar acepta el paso: presentarlo otra vez no sirve (IDE-41).
        Assert.True((await gestion.RegenerarLosCodigosDeRespaldo(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S(codigo))).EsExito);

        var repetido = await gestion.DesactivarElSegundoFactor(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S(codigo));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, repetido.Error);
        Assert.True((await gestion.Consultar(cuenta.Usuario.Id)).Valor.SegundoFactorActivo);
    }

    [Fact]
    public async Task DesactivarElSegundoFactor_SinEstarActivo_EsUnConflicto()
    {
        var (_, cuenta, gestion) = Titular();

        var resultado = await gestion.DesactivarElSegundoFactor(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S("123456"));

        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
    }

    [Fact]
    public async Task DesactivarElSegundoFactor_DelPersonal_EsProhibido()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("logistica@example.com", RolDeUsuario.Logistica);
        e.ActuarComo(cuenta);
        var (secreto, _) = e.ConSegundoFactor(cuenta);

        var resultado = await e.Gestion().DesactivarElSegundoFactor(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S(e.Totp(secreto)));

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal("segundo-factor-obligatorio", resultado.Error.Clave);
    }

    [Fact]
    public async Task DesactivarElSegundoFactor_CodigosIncorrectosRepetidos_CuentanContraElLimiteDelSegundoFactor()
    {
        var (e, cuenta, gestion) = Titular();
        e.ConSegundoFactor(cuenta);

        ErrorDeAplicacion? ultimo = null;
        for (var i = 0; i < 6; i++)
        {
            ultimo = (await gestion.DesactivarElSegundoFactor(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S("000000"))).Error;
        }

        Assert.Equal(FallosDeIdentidad.LimiteExcedido, ultimo);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-46")]
    public async Task Regenerar_ConLaContrasenaYUnCodigoVigente_EntregaOchoNuevos_YLosAnterioresDejanDeServir()
    {
        var (e, cuenta, gestion) = Titular();
        var (secreto, anteriores) = e.ConSegundoFactor(cuenta);
        var id = cuenta.Usuario.Id;

        var regenerados = await gestion.RegenerarLosCodigosDeRespaldo(id, S(EntornoDeIdentidad.ContrasenaSegura), S(e.Totp(secreto)));

        Assert.True(regenerados.EsExito);
        Assert.Equal(CodigosDeRespaldo.Cantidad, regenerados.Valor.Count);
        Assert.Empty(regenerados.Valor.Select(c => c.Revelar()).Intersect(anteriores));
        Assert.Equal(AccionesAuditables.SegundoFactorRegenerado, Assert.Single(e.Auditoria.Registradas).Accion);
        // Un código anterior ya no vale para desactivar; uno nuevo sí.
        Assert.Equal(FallosDeIdentidad.CodigoInvalido, (await gestion.DesactivarElSegundoFactor(id, S(EntornoDeIdentidad.ContrasenaSegura), S(anteriores[0]))).Error);
        Assert.True((await gestion.DesactivarElSegundoFactor(id, S(EntornoDeIdentidad.ContrasenaSegura), S(regenerados.Valor[0].Revelar()))).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-46")]
    public async Task Regenerar_SinContrasena_ConUnCodigoDeRespaldoOSinSegundoFactor_SeRechaza_YNoCambiaNada()
    {
        var (e, cuenta, gestion) = Titular();
        var sinFactor = await gestion.RegenerarLosCodigosDeRespaldo(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S("123456"));
        var (secreto, anteriores) = e.ConSegundoFactor(cuenta);
        var id = cuenta.Usuario.Id;

        var sinContrasena = await gestion.RegenerarLosCodigosDeRespaldo(id, S("equivocada y larga de verdad"), S(e.Totp(secreto)));
        var conRespaldo = await gestion.RegenerarLosCodigosDeRespaldo(id, S(EntornoDeIdentidad.ContrasenaSegura), S(anteriores[0]));

        Assert.Equal(CategoriaDeError.Conflicto, sinFactor.Error!.Categoria);
        Assert.Equal(Reautenticacion.ContrasenaActualIncorrecta, sinContrasena.Error);
        Assert.Equal(FallosDeIdentidad.CodigoInvalido, conRespaldo.Error);
        Assert.Empty(e.Auditoria.Registradas);
        Assert.Equal(CodigosDeRespaldo.Cantidad, (await gestion.Consultar(id)).Valor.CodigosDeRespaldoRestantes);
    }

    // ---------------------------------------------------------------- avisos

    [Fact]
    public async Task ListarLosAvisos_SoloMuestraLosPropios()
    {
        var (e, cuenta, gestion) = Titular();
        e.AvisosGuardados.Sembrar(cuenta.Usuario.Id, "contrasena-cambiada", EntornoDeIdentidad.Inicio);
        e.AvisosGuardados.Sembrar(Guid.NewGuid(), "ajeno", EntornoDeIdentidad.Inicio);

        var avisos = await gestion.ListarLosAvisos(cuenta.Usuario.Id);

        Assert.Equal("contrasena-cambiada", Assert.Single(avisos.Valor).Tipo);
    }

    [Fact]
    public async Task MarcarElAvisoComoVisto_DesdeUnaSesionReconocida_LoMarca_YDesdeOtra_No()
    {
        var (e, cuenta, gestion) = Titular();
        var id = e.AvisosGuardados.Sembrar(cuenta.Usuario.Id, "contrasena-cambiada", EntornoDeIdentidad.Inicio);

        var desconocida = await gestion.MarcarElAvisoComoVisto(cuenta.Usuario.Id, id, desdeUnaSesionReconocida: false);
        Assert.Equal("sesion-no-reconocida", desconocida.Error!.Clave);
        Assert.False((await gestion.ListarLosAvisos(cuenta.Usuario.Id)).Valor.Single().Visto);

        var reconocida = await gestion.MarcarElAvisoComoVisto(cuenta.Usuario.Id, id, desdeUnaSesionReconocida: true);
        Assert.True(reconocida.EsExito);
        Assert.True((await gestion.ListarLosAvisos(cuenta.Usuario.Id)).Valor.Single().Visto);
    }

    [Fact]
    public async Task MarcarElAvisoComoVisto_DeOtraCuenta_NoLoEncuentra()
    {
        var (e, cuenta, gestion) = Titular();
        var ajeno = e.AvisosGuardados.Sembrar(Guid.NewGuid(), "ajeno", EntornoDeIdentidad.Inicio);

        var resultado = await gestion.MarcarElAvisoComoVisto(cuenta.Usuario.Id, ajeno, desdeUnaSesionReconocida: true);

        Assert.Equal(CategoriaDeError.NoEncontrado, resultado.Error!.Categoria);
    }

    [Fact]
    public async Task UnaCuentaDesactivada_NoLeeSusAvisosAunqueSuCredencialSigaVigente()
    {
        var (e, cuenta, gestion) = Titular();
        e.AvisosGuardados.Sembrar(cuenta.Usuario.Id, "contrasena-cambiada", EntornoDeIdentidad.Inicio);
        cuenta.Usuario.Desactivar();

        var resultado = await gestion.ListarLosAvisos(cuenta.Usuario.Id);

        Assert.Equal(AlcanceDeLaCuenta.CuentaNoEncontrada, resultado.Error);
    }

    [Fact]
    public async Task ActivarElSegundoFactor_SinHaberloIniciado_NoConsumeLosCuposDeIntentos()
    {
        var (e, cuenta, gestion) = Titular();

        for (var i = 0; i < 8; i++)
        {
            Assert.Equal("segundo-factor-no-iniciado", (await gestion.ConfirmarElSegundoFactor(cuenta.Usuario.Id, S("123456"))).Error!.Clave);
        }
    }

    [Fact]
    public async Task CambiarLaContrasena_SiOtroCambioLaPisoMientrasSeVerificaba_NoSeSobrescribe()
    {
        var (e, cuenta, gestion) = Titular();
        e.Hasher.AlResumir = () => cuenta.ResumenDeAcceso = "resumen-de-una-recuperacion-concurrente";

        var resultado = await gestion.CambiarLaContrasena(cuenta.Usuario.Id, S(EntornoDeIdentidad.ContrasenaSegura), S(Nueva));

        Assert.Equal(Reautenticacion.ContrasenaActualIncorrecta, resultado.Error);
        Assert.Equal("resumen-de-una-recuperacion-concurrente", cuenta.ResumenDeAcceso);
        Assert.Empty(e.Auditoria.Registradas);
    }
}
