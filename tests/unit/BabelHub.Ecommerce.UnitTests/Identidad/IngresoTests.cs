using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// El ingreso en dos pasos (identidad-seguridad T15; AC-IDE-02, AC-IDE-03, AC-IDE-07, AC-IDE-17, AC-IDE-18, AC-IDE-20, AC-IDE-21, AC-IDE-38, AC-IDE-41; IDE-02, IDE-03, IDE-08, IDE-19 a IDE-23, IDE-42, IDE-50): mismo
/// mensaje ante datos incorrectos, el segundo factor según el rol, la contraseña temporal, el desafío de 5 minutos de un solo uso y los eventos de seguridad.
/// </summary>
public sealed class IngresoTests
{
    private static SolicitudDeIngreso Solicitud(string correo = "ana@example.com", string frase = EntornoDeIdentidad.ContrasenaSegura) => new(correo, new ValorSecreto(frase));

    // ---------- Credenciales ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-02")]
    public async Task UnClienteConCredencialesCorrectasYSinSegundoFactor_RecibeLaCredencialConSuRolYSuCorreoVerificado()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta();

        var resultado = await e.Ingreso().Ingresar(Solicitud());

        Assert.True(resultado.EsExito);
        var credencial = resultado.Valor.Credencial!;
        Assert.Equal((cuenta.Usuario.Id, RolDeCuenta.Cliente, null, true), (credencial.UsuarioId, credencial.Rol, credencial.Sucursal, credencial.CorreoVerificado));
        Assert.Equal(EntornoDeIdentidad.Inicio.AddDays(7), credencial.RenovacionExpiraEn);
        Assert.Equal(EntornoDeIdentidad.Inicio.AddMinutes(15), credencial.CredencialExpiraEn);
        Assert.Null(resultado.Valor.Desafio);
        Assert.Single(e.Bd.Renovaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-02")]
    public async Task LaCredencialDeUnEncargadoLlevaSuSucursal_YLaRenovacionDelPersonalDura8Horas()
    {
        var e = new EntornoDeIdentidad();
        var sucursal = Guid.NewGuid();
        var cuenta = e.Cuenta("jefe@example.com", RolDeUsuario.EncargadoDeSucursal, sucursal: sucursal);
        var (secreto, _) = e.ConSegundoFactor(cuenta);
        var ingreso = e.Ingreso();
        var desafio = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;

        var resultado = await ingreso.PresentarSegundoFactor(desafio.Identificador, new ValorSecreto(e.Totp(secreto)));

        var credencial = resultado.Valor.Credencial!;
        Assert.Equal((RolDeCuenta.EncargadoDeSucursal, (Guid?)sucursal), (credencial.Rol, credencial.Sucursal));
        Assert.Equal(EntornoDeIdentidad.Inicio.AddHours(8), credencial.RenovacionExpiraEn);
        Assert.Equal(sucursal, Assert.Single(e.Emisor.Emitidas).Sucursal);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-03")]
    public async Task DatosIncorrectosCuentaInexistenteDesactivadaOCancelada_RespondenLoMismo_YCuestanLoMismo()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("existe@example.com");
        e.Cuenta("desactivada@example.com", activa: false);
        var cancelada = e.Cuenta("cancelada@example.com");
        cancelada.CanceladaEn = EntornoDeIdentidad.Inicio;
        var ingreso = e.Ingreso();

        var casos = new[]
        {
            await ingreso.Ingresar(Solicitud("existe@example.com", "otra contraseña distinta")),
            await ingreso.Ingresar(Solicitud("nadie@example.com")),
            await ingreso.Ingresar(Solicitud("desactivada@example.com")),
            await ingreso.Ingresar(Solicitud("cancelada@example.com")),
            await ingreso.Ingresar(new SolicitudDeIngreso("no-es-correo", new ValorSecreto("x"))),
        };

        Assert.All(casos, r => Assert.Equal(FallosDeIdentidad.CredencialesInvalidas, r.Error));
        Assert.True(e.Hasher.Senuelos >= 2, "La cuenta inexistente consume un cálculo señuelo.");
        Assert.Empty(e.Emisor.Emitidas);
        Assert.Equal(5, e.Seguridad.Eventos.Count(ev => ev.Accion == AccionesAuditables.IngresoFallido));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-03")]
    public async Task ElEventoDeIngresoFallido_GuardaLaCuentaSoloSiExiste_YNuncaLaContrasena()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta();
        var ingreso = e.Ingreso();

        await ingreso.Ingresar(Solicitud(frase: "contraseña-equivocada-123"));
        await ingreso.Ingresar(Solicitud("nadie@example.com", "contraseña-equivocada-123"));

        Assert.Equal(cuenta.Usuario.Id.ToString("D"), e.Seguridad.Eventos[0].ObjetoId);
        Assert.Null(e.Seguridad.Eventos[1].ObjetoId);
        Assert.All(e.Seguridad.Eventos, ev => Assert.DoesNotContain("equivocada", ev.ToString() + string.Join(' ', ev.Detalle.Values), StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-03")]
    public async Task CincoIngresosFallidos_BloqueanElSexto_AunConLaContrasenaCorrecta_YDejanElEventoDeBloqueo()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta();
        var ingreso = e.Ingreso();
        for (var i = 0; i < 5; i++)
        {
            await ingreso.Ingresar(Solicitud(frase: "contraseña-equivocada-123"));
        }

        var sexto = await ingreso.Ingresar(Solicitud());

        Assert.Equal(CategoriaDeError.Limite, sexto.Error!.Categoria);
        Assert.Contains(e.Seguridad.Eventos, ev => ev.Accion == AccionesAuditables.BloqueoTemporalAplicado);
        Assert.Empty(e.Emisor.Emitidas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-03")]
    public async Task ConLaAuditoriaCaida_ElBloqueoSeAplicaIgual_ElIngresoNuncaFallaAbierto()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta();
        e.Seguridad.Falla = true;
        e.Auditoria.Falla = true;
        var ingreso = e.Ingreso();
        for (var i = 0; i < 5; i++)
        {
            var fallo = await ingreso.Ingresar(Solicitud(frase: "contraseña-equivocada-123"));
            Assert.Equal(FallosDeIdentidad.CredencialesInvalidas, fallo.Error);
        }

        var sexto = await ingreso.Ingresar(Solicitud());

        Assert.Equal(CategoriaDeError.Limite, sexto.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-03")]
    public async Task ElCupoDeIngresosFallidosSeDevuelveAlAcertarLaContrasena_YSiElAlmacenDeLimitesCaeFallaCerrado()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta();
        var ingreso = e.Ingreso();
        for (var i = 0; i < 4; i++)
        {
            await ingreso.Ingresar(Solicitud(frase: "contraseña-equivocada-123"));
        }

        Assert.True((await ingreso.Ingresar(Solicitud())).EsExito);
        Assert.True((await ingreso.Ingresar(Solicitud())).EsExito);
        e.Limites.Caido = true;
        var sinAlmacen = await ingreso.Ingresar(Solicitud());

        Assert.Equal(CategoriaDeError.NoDisponible, sinAlmacen.Error!.Categoria);
    }

    // ---------- Segundo factor ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-20")]
    public async Task ElPersonalConSegundoFactor_RecibeUnDesafioDe5Minutos_SinCredencial()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("jefe@example.com", RolDeUsuario.Logistica);
        e.ConSegundoFactor(cuenta);

        var resultado = await e.Ingreso().Ingresar(Solicitud("jefe@example.com"));

        Assert.False(resultado.Valor.Completo);
        Assert.Null(resultado.Valor.Credencial);
        Assert.Equal(PasoPendienteDelIngreso.PresentarSegundoFactor, resultado.Valor.Desafio!.Paso);
        Assert.Equal(EntornoDeIdentidad.Inicio.AddMinutes(5), resultado.Valor.Desafio.VenceEn);
        Assert.Empty(e.Bd.Renovaciones);
        Assert.Empty(e.Emisor.Emitidas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-20")]
    [Trait("Criterio", "AC-IDE-38")]
    public async Task UnTotpCorrecto_EmiteLaCredencial_YUnDesafioNoSirveDosVeces()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("jefe@example.com", RolDeUsuario.AdministradorCentral);
        var (secreto, _) = e.ConSegundoFactor(cuenta);
        var ingreso = e.Ingreso();
        var desafio = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;
        var codigo = new ValorSecreto(e.Totp(secreto));

        var primero = await ingreso.PresentarSegundoFactor(desafio.Identificador, codigo);
        var segundo = await ingreso.PresentarSegundoFactor(desafio.Identificador, codigo);

        Assert.True(primero.Valor.Completo);
        Assert.Equal(FallosDeIdentidad.DesafioInvalido, segundo.Error);
        Assert.Single(e.Bd.Renovaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-18")]
    public async Task UnCodigoTotpYaUsado_SeRechaza_AunConOtroDesafio()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("jefe@example.com", RolDeUsuario.Logistica);
        var (secreto, _) = e.ConSegundoFactor(cuenta);
        var ingreso = e.Ingreso();
        var uno = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;
        var dos = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;
        var codigo = new ValorSecreto(e.Totp(secreto));

        var aceptado = await ingreso.PresentarSegundoFactor(uno.Identificador, codigo);
        var repetido = await ingreso.PresentarSegundoFactor(dos.Identificador, codigo);

        Assert.True(aceptado.EsExito);
        Assert.Equal(FallosDeIdentidad.CodigoInvalido, repetido.Error);
        Assert.Contains(e.Seguridad.Eventos, ev => ev.Accion == AccionesAuditables.SegundoFactorFallido);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-38")]
    public async Task DosSolicitudesSimultaneasConElMismoCodigo_AceptanSoloUna()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("jefe@example.com", RolDeUsuario.Logistica);
        var (secreto, _) = e.ConSegundoFactor(cuenta);
        var ingreso = e.Ingreso();
        var uno = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;
        var dos = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;
        var codigo = new ValorSecreto(e.Totp(secreto));

        var resultados = await Task.WhenAll(ingreso.PresentarSegundoFactor(uno.Identificador, codigo), ingreso.PresentarSegundoFactor(dos.Identificador, codigo));

        Assert.Equal(1, resultados.Count(r => r.EsExito));
        Assert.Single(e.Emisor.Emitidas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-17")]
    public async Task UnCodigoIncorrecto_FallaConElMensajeGenerico_SinConsumirElDesafio_YDejaElEvento()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("jefe@example.com", RolDeUsuario.Logistica);
        var (secreto, _) = e.ConSegundoFactor(cuenta);
        var ingreso = e.Ingreso();
        var desafio = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;
        var correcto = e.Totp(secreto);
        var incorrecto = correcto == "000000" ? "111111" : "000000";

        var fallido = await ingreso.PresentarSegundoFactor(desafio.Identificador, new ValorSecreto(incorrecto));
        var luego = await ingreso.PresentarSegundoFactor(desafio.Identificador, new ValorSecreto(correcto));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, fallido.Error);
        Assert.True(luego.EsExito);
        Assert.Equal(AccionesAuditables.SegundoFactorFallido, Assert.Single(e.Seguridad.Eventos).Accion);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-17")]
    public async Task CincoSegundosFactoresIncorrectos_BloqueanElSexto_AunCorrecto()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("jefe@example.com", RolDeUsuario.Logistica);
        var (secreto, _) = e.ConSegundoFactor(cuenta);
        var ingreso = e.Ingreso();
        var desafio = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;
        var correcto = e.Totp(secreto);
        var incorrecto = correcto == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
        {
            await ingreso.PresentarSegundoFactor(desafio.Identificador, new ValorSecreto(incorrecto));
        }

        var sexto = await ingreso.PresentarSegundoFactor(desafio.Identificador, new ValorSecreto(correcto));

        Assert.Equal(CategoriaDeError.Limite, sexto.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-42")]
    [Trait("Criterio", "AC-IDE-41")]
    public async Task UnDesafioVencidoOInexistente_NoSirve_YElIngresoSeReinicia()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("jefe@example.com", RolDeUsuario.Logistica);
        var (secreto, _) = e.ConSegundoFactor(cuenta);
        var ingreso = e.Ingreso();
        var desafio = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddMinutes(6);

        var vencido = await ingreso.PresentarSegundoFactor(desafio.Identificador, new ValorSecreto(e.Totp(secreto)));
        var inexistente = await ingreso.PresentarSegundoFactor(new ValorSecreto("desafio-que-no-existe-" + new string('z', 30)), new ValorSecreto("123456"));

        Assert.Equal(FallosDeIdentidad.DesafioInvalido, vencido.Error);
        Assert.Equal(FallosDeIdentidad.DesafioInvalido, inexistente.Error);
        Assert.Empty(e.Emisor.Emitidas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-42")]
    public async Task UnDesafioSoloPermiteLoDeSuModo()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("jefe@example.com", RolDeUsuario.Logistica);
        var ingreso = e.Ingreso();
        var configurar = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;

        var presentar = await ingreso.PresentarSegundoFactor(configurar.Identificador, new ValorSecreto("123456"));
        var cambiar = await ingreso.CambiarLaContrasenaTemporal(configurar.Identificador, new ValorSecreto("otra frase larga y segura"));

        Assert.Equal(PasoPendienteDelIngreso.ConfigurarSegundoFactor, configurar.Paso);
        Assert.Equal(FallosDeIdentidad.DesafioInvalido, presentar.Error);
        Assert.Equal(FallosDeIdentidad.DesafioInvalido, cambiar.Error);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-21")]
    public async Task ElPersonalSinSegundoFactor_LoConfiguraAntesDeRecibirCredencial_ConOchoCodigosDeRespaldo()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("jefe@example.com", RolDeUsuario.AdministradorCentral);
        var ingreso = e.Ingreso();
        var desafio = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;

        var secreto = await ingreso.IniciarConfiguracionDelSegundoFactor(desafio.Identificador);
        var enClaro = e.Secretos.SecretoDelSegundoFactor();
        var sinConfirmar = await ingreso.ConfirmarElSegundoFactor(desafio.Identificador, new ValorSecreto("000000"));
        var confirmado = await ingreso.ConfirmarElSegundoFactor(desafio.Identificador, new ValorSecreto(e.Totp(enClaro)));

        Assert.Equal(Base32.Codificar(enClaro), secreto.Valor.SecretoBase32.Revelar());
        Assert.Equal("BabelHub", secreto.Valor.Emisor);
        Assert.Equal(FallosDeIdentidad.CodigoInvalido, sinConfirmar.Error);
        Assert.True(confirmado.Valor.Completo);
        Assert.Equal(8, confirmado.Valor.CodigosDeRespaldo.Count);
        Assert.All(confirmado.Valor.CodigosDeRespaldo, c => Assert.True(CodigosDeRespaldo.TieneLaForma(c.Revelar())));
        Assert.Contains(e.Auditoria.Registradas, r => r.Accion == AccionesAuditables.SegundoFactorActivado);
        var guardado = await e.SegundoFactor.Obtener(cuenta.Usuario.Id);
        Assert.True(guardado!.Factor.EstaActivo);
        Assert.Equal(8, guardado.Factor.CodigosDeRespaldoRestantes);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-21")]
    public async Task ElSecretoDelSegundoFactorNoSeGuardaEnClaro_YElDesafioDeConfiguracionNoSirveParaPresentar()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("jefe@example.com", RolDeUsuario.Logistica);
        var ingreso = e.Ingreso();
        var desafio = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;

        await ingreso.IniciarConfiguracionDelSegundoFactor(desafio.Identificador);

        var guardado = await e.SegundoFactor.Obtener(cuenta.Usuario.Id);
        Assert.False(guardado!.Factor.EstaActivo);
        Assert.NotEqual(e.Secretos.SecretoDelSegundoFactor(), guardado.SecretoCifrado);
        Assert.Empty(e.Emisor.Emitidas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-50")]
    public async Task UnClienteQueActivoElSegundoFactor_TambienLoPresenta()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta();
        var (secreto, _) = e.ConSegundoFactor(cuenta);
        var ingreso = e.Ingreso();

        var paso = await ingreso.Ingresar(Solicitud());
        var final = await ingreso.PresentarSegundoFactor(paso.Valor.Desafio!.Identificador, new ValorSecreto(e.Totp(secreto)));

        Assert.Equal(PasoPendienteDelIngreso.PresentarSegundoFactor, paso.Valor.Desafio.Paso);
        Assert.Equal(RolDeCuenta.Cliente, final.Valor.Credencial!.Rol);
    }

    // ---------- Códigos de respaldo ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-20")]
    public async Task UnCodigoDeRespaldo_SeAceptaUnaSolaVez_SeAudita_YDiceCuantosQuedan()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("jefe@example.com", RolDeUsuario.Logistica);
        var (_, respaldo) = e.ConSegundoFactor(cuenta);
        var ingreso = e.Ingreso();
        var uno = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;
        var dos = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;

        var primero = await ingreso.PresentarSegundoFactor(uno.Identificador, new ValorSecreto(respaldo[0]));
        var repetido = await ingreso.PresentarSegundoFactor(dos.Identificador, new ValorSecreto(respaldo[0].ToLowerInvariant()));

        Assert.True(primero.Valor.Completo);
        Assert.Equal(7, primero.Valor.CodigosDeRespaldoRestantes);
        Assert.Equal(FallosDeIdentidad.CodigoInvalido, repetido.Error);
        var auditoria = Assert.Single(e.Auditoria.Registradas);
        Assert.Equal(AccionesAuditables.CodigoDeRespaldoUsado, auditoria.Accion);
        Assert.DoesNotContain(respaldo[0], string.Join(' ', auditoria.Detalle.Values) + auditoria.ObjetoId + auditoria.Motivo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-20")]
    public async Task SiNoSePuedeAuditarElCodigoDeRespaldo_NoSeIngresa_YElCodigoNoSeConsume()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("jefe@example.com", RolDeUsuario.Logistica);
        var (_, respaldo) = e.ConSegundoFactor(cuenta);
        var ingreso = e.Ingreso();
        var desafio = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => ingreso.PresentarSegundoFactor(desafio.Identificador, new ValorSecreto(respaldo[0])));

        Assert.Empty(e.Emisor.Emitidas);
        Assert.Empty(e.Bd.Renovaciones);
    }

    // ---------- Contraseña temporal ----------

    private static CuentaDeIdentidad ConTemporal(EntornoDeIdentidad e, RolDeUsuario rol, string correo = "nuevo@example.com")
    {
        var cuenta = e.Cuenta(correo, rol, frase: "Temporal-de-prueba-0001");
        cuenta.DebeCambiarLaTemporal = true;
        cuenta.ContrasenaTemporalHasta = EntornoDeIdentidad.Inicio.AddHours(24);
        return cuenta;
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-21")]
    public async Task ConUnaContrasenaTemporal_SoloSePermiteElDesafioDeCambio_SinCredencial()
    {
        var e = new EntornoDeIdentidad();
        ConTemporal(e, RolDeUsuario.Logistica);

        var resultado = await e.Ingreso().Ingresar(Solicitud("nuevo@example.com", "Temporal-de-prueba-0001"));

        Assert.Equal(PasoPendienteDelIngreso.CambiarContrasenaTemporal, resultado.Valor.Desafio!.Paso);
        Assert.Null(resultado.Valor.Credencial);
        Assert.Empty(e.Emisor.Emitidas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-21")]
    public async Task UnaContrasenaTemporalVencida_SeRechazaComoUnaContrasenaIncorrecta()
    {
        var e = new EntornoDeIdentidad();
        ConTemporal(e, RolDeUsuario.Logistica);
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddHours(25);

        var resultado = await e.Ingreso().Ingresar(Solicitud("nuevo@example.com", "Temporal-de-prueba-0001"));

        Assert.Equal(FallosDeIdentidad.CredencialesInvalidas, resultado.Error);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-28")]
    public async Task ElPasoDelDesafio_DiceQueFalta_YCancelarloLoDejaSinPaso_AunSiVenceOEsAjeno()
    {
        var e = new EntornoDeIdentidad();
        ConTemporal(e, RolDeUsuario.Logistica);
        var ingreso = e.Ingreso();
        var desafio = (await ingreso.Ingresar(Solicitud("nuevo@example.com", "Temporal-de-prueba-0001"))).Valor.Desafio!;

        var paso = await ingreso.PasoDelDesafio(desafio.Identificador);
        var ajeno = await ingreso.PasoDelDesafio(new ValorSecreto("ajeno-que-no-existe-0123456789-0123456789ab"));
        await ingreso.CancelarElDesafio(desafio.Identificador);
        await ingreso.CancelarElDesafio(desafio.Identificador);
        await ingreso.CancelarElDesafio(new ValorSecreto("ajeno-que-no-existe-0123456789-0123456789ab"));
        var trasCancelar = await ingreso.PasoDelDesafio(desafio.Identificador);
        var cambiarTrasCancelar = await ingreso.CambiarLaContrasenaTemporal(desafio.Identificador, new ValorSecreto("una frase nueva y segura"));

        Assert.Equal(PasoPendienteDelIngreso.CambiarContrasenaTemporal, paso);
        Assert.Null(ajeno);
        Assert.Null(trasCancelar);
        Assert.False(cambiarTrasCancelar.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-21")]
    public async Task IniciarLaConfiguracionVariasVeces_EntregaElMismoSecretoSinConfirmar_YUnSegundoFactorActivoNoSeReemplaza()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("jefe@example.com", RolDeUsuario.AdministradorCentral);
        var ingreso = e.Ingreso();
        var desafio = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;

        var primero = await ingreso.IniciarConfiguracionDelSegundoFactor(desafio.Identificador);
        var errado = await ingreso.ConfirmarElSegundoFactor(desafio.Identificador, new ValorSecreto("000000"));
        var segundo = await ingreso.IniciarConfiguracionDelSegundoFactor(desafio.Identificador);

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, errado.Error);
        Assert.Equal(primero.Valor.SecretoBase32.Revelar(), segundo.Valor.SecretoBase32.Revelar());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-28")]
    public async Task UnDesafioVencido_NoTienePaso()
    {
        var e = new EntornoDeIdentidad();
        ConTemporal(e, RolDeUsuario.Logistica);
        var ingreso = e.Ingreso();
        var desafio = (await ingreso.Ingresar(Solicitud("nuevo@example.com", "Temporal-de-prueba-0001"))).Valor.Desafio!;
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddMinutes(6);

        Assert.Null(await ingreso.PasoDelDesafio(desafio.Identificador));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-21")]
    public async Task ElCambioDeLaContrasenaTemporal_ValidaLaPolitica_RevocaSesiones_Audita_Avisa_YSigueConElSegundoFactor()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = ConTemporal(e, RolDeUsuario.Logistica);
        await e.Sesiones.Nueva(cuenta.Usuario, EntornoDeIdentidad.Inicio, default);
        var ingreso = e.Ingreso();
        var desafio = (await ingreso.Ingresar(Solicitud("nuevo@example.com", "Temporal-de-prueba-0001"))).Valor.Desafio!;

        var corta = await ingreso.CambiarLaContrasenaTemporal(desafio.Identificador, new ValorSecreto("corta"));
        var comun = await ingreso.CambiarLaContrasenaTemporal(desafio.Identificador, new ValorSecreto("contrasena123"));
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddMinutes(2);
        var cambiado = await ingreso.CambiarLaContrasenaTemporal(desafio.Identificador, new ValorSecreto("una frase nueva y segura"));

        Assert.Equal("demasiado-corta", corta.Error!.Errores[0].Clave);
        Assert.Equal("demasiado-comun", comun.Error!.Errores[0].Clave);
        Assert.Equal(PasoPendienteDelIngreso.ConfigurarSegundoFactor, cambiado.Valor.Desafio!.Paso);
        Assert.Equal(EntornoDeIdentidad.Inicio.AddMinutes(5), cambiado.Valor.Desafio.VenceEn);
        Assert.False(cuenta.DebeCambiarLaTemporal);
        Assert.Null(cuenta.ContrasenaTemporalHasta);
        Assert.Equal(HasherDePrueba.ResumenDe("una frase nueva y segura"), cuenta.ResumenDeAcceso);
        Assert.NotNull(cuenta.Usuario.SesionesValidasDesde);
        Assert.All(e.Bd.Renovaciones, r => Assert.NotNull(r.RevocadaEn));
        Assert.Contains(e.Auditoria.Registradas, r => r.Accion == AccionesAuditables.ContrasenaCambiada);
        Assert.Equal("contrasena-cambiada", Assert.Single(e.Correo.Correos).Plantilla);
        Assert.DoesNotContain("frase nueva", string.Join(' ', e.Correo.Correos.SelectMany(c => c.Datos.Values)), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-21")]
    public async Task UnClienteConContrasenaTemporalYSinSegundoFactor_RecibeLaCredencialAlCambiarla()
    {
        var e = new EntornoDeIdentidad();
        ConTemporal(e, RolDeUsuario.Cliente);
        var ingreso = e.Ingreso();
        var desafio = (await ingreso.Ingresar(Solicitud("nuevo@example.com", "Temporal-de-prueba-0001"))).Valor.Desafio!;

        var cambiado = await ingreso.CambiarLaContrasenaTemporal(desafio.Identificador, new ValorSecreto("una frase nueva y segura"));

        Assert.True(cambiado.Valor.Completo);
    }

    // ---------- Correcciones de la revisión de seguridad ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-18")]
    public async Task UnSegundoFactorAcertado_DevuelveLosCuposDelSegundoFactor_PorqueSoloCuentanLosIncorrectos()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("jefe@example.com", RolDeUsuario.Logistica);
        var (secreto, respaldo) = e.ConSegundoFactor(cuenta);
        var ingreso = e.Ingreso();
        var sujeto = cuenta.Usuario.Id.ToString("N");
        for (var i = 0; i < 6; i++)
        {
            var desafio = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;
            var codigo = i % 2 == 0 ? e.Totp(secreto, i) : respaldo[i];
            e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddSeconds(31 * i);
            var resultado = await ingreso.PresentarSegundoFactor(desafio.Identificador, new ValorSecreto(i % 2 == 0 ? e.Totp(secreto) : codigo));
            Assert.True(resultado.EsExito, $"el ingreso legítimo {i} debe pasar");
        }

        Assert.Equal(0, e.Limites.Cuenta("segundo-factor-por-origen", sujeto));
        Assert.Equal(0, e.Limites.Cuenta("segundo-factor-por-cuenta", sujeto));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-21")]
    public async Task UnDesafioDeConfiguracionRezagado_NoPuedeReemplazarElSecretoDeUnSegundoFactorYaActivo()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("jefe@example.com", RolDeUsuario.AdministradorCentral);
        var ingreso = e.Ingreso();
        var uno = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;
        var dos = (await ingreso.Ingresar(Solicitud("jefe@example.com"))).Valor.Desafio!;
        await ingreso.IniciarConfiguracionDelSegundoFactor(uno.Identificador);
        var secreto = e.Secretos.SecretoDelSegundoFactor();
        Assert.True((await ingreso.ConfirmarElSegundoFactor(uno.Identificador, new ValorSecreto(e.Totp(secreto)))).EsExito);
        var antes = (await e.SegundoFactor.Obtener(cuenta.Usuario.Id))!.SecretoCifrado;

        var rezagado = await ingreso.IniciarConfiguracionDelSegundoFactor(dos.Identificador);

        Assert.Equal(FallosDeIdentidad.DesafioInvalido, rezagado.Error);
        Assert.Equal(antes, (await e.SegundoFactor.Obtener(cuenta.Usuario.Id))!.SecretoCifrado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-21")]
    public async Task CambiarLaContrasenaTemporal_ReleeLaCuentaBajoElBloqueoDeSuFila()
    {
        var e = new EntornoDeIdentidad();
        ConTemporal(e, RolDeUsuario.Cliente);
        var ingreso = e.Ingreso();
        var desafio = (await ingreso.Ingresar(Solicitud("nuevo@example.com", "Temporal-de-prueba-0001"))).Valor.Desafio!;
        var antes = e.Cuentas.Bloqueos;

        await ingreso.CambiarLaContrasenaTemporal(desafio.Identificador, new ValorSecreto("una frase nueva y segura"));

        Assert.True(e.Cuentas.Bloqueos > antes);
    }
}
