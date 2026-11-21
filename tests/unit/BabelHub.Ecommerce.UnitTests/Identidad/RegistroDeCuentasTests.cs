using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using PropositoDeCodigo = BabelHub.Ecommerce.Domain.Identidad.PropositoDeCodigo;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// Registro y verificación del correo (identidad-seguridad T14; AC-IDE-01, AC-IDE-14, AC-IDE-26, AC-IDE-31, AC-IDE-36, AC-IDE-63; IDE-01, IDE-13, IDE-16, IDE-29, IDE-34, IDE-39, IDE-84, IDE-88): textos y mayoría de
/// edad, correo ya usado sin revelar datos, reemplazo de una cuenta de cliente sin verificar con reversión total si un puerto falla, y el código de un solo uso con su mensaje genérico.
/// </summary>
public sealed class RegistroDeCuentasTests
{
    // ---------- Registro ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-01")]
    public async Task Registrar_CreaUnaCuentaDeClienteConElCorreoSinVerificar_ConElResumenYNoLaContrasena()
    {
        var e = new EntornoDeIdentidad();

        var resultado = await e.Registro().Registrar(e.Alta("  Ana@Example.com "));

        Assert.True(resultado.EsExito);
        var cuenta = Assert.Single(e.Bd.Cuentas.Values);
        Assert.Equal("ana@example.com", cuenta.Usuario.Correo);
        Assert.Equal(RolDeUsuario.Cliente, cuenta.Usuario.Rol);
        Assert.False(cuenta.Usuario.CorreoVerificado);
        Assert.True(cuenta.Usuario.Activo);
        Assert.Equal("Ana", cuenta.Nombre);
        Assert.Equal(HasherDePrueba.ResumenDe(EntornoDeIdentidad.ContrasenaSegura), cuenta.ResumenDeAcceso);
        Assert.DoesNotContain(EntornoDeIdentidad.ContrasenaSegura, cuenta.ResumenDeAcceso!, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-31")]
    public async Task Registrar_GuardaLaVersionYLaFechaDeLosTextosAceptados_YLaMayoriaDeEdad()
    {
        var e = new EntornoDeIdentidad();

        await e.Registro().Registrar(e.Alta());

        var aceptacion = Assert.Single(e.Bd.Cuentas.Values).Aceptacion;
        Assert.Equal("t1", aceptacion.VersionAceptada(TextoLegal.Terminos));
        Assert.Equal("p1", aceptacion.VersionAceptada(TextoLegal.Privacidad));
        Assert.Equal(EntornoDeIdentidad.Inicio, aceptacion.Aceptadas[TextoLegal.Terminos].Fecha);
        Assert.Equal(EntornoDeIdentidad.Inicio, aceptacion.MayorDeEdadDeclaradaEn);
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-36")]
    [InlineData(null, "p1", true, "incompleta")]
    [InlineData("t1", null, true, "incompleta")]
    [InlineData("t1", "p1", false, "incompleta")]
    [InlineData("t0", "p1", true, "version-no-vigente")]
    [InlineData("t1", "p0", true, "version-no-vigente")]
    public async Task Registrar_SinAceptarLosTextosVigentesODeclararLaMayoriaDeEdad_NoCreaLaCuenta(string? terminos, string? privacidad, bool mayor, string clave)
    {
        var e = new EntornoDeIdentidad();

        var resultado = await e.Registro().Registrar(e.Alta(terminos: terminos, privacidad: privacidad, mayor: mayor));

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal(("aceptacion", clave), (resultado.Error.Errores[0].Campo, resultado.Error.Errores[0].Clave));
        Assert.Empty(e.Bd.Cuentas);
        Assert.Empty(e.Cola.Encoladas);
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-01")]
    [InlineData("corta", "demasiado-corta")]
    [InlineData("contrasena123", "demasiado-comun")]
    [InlineData("", "demasiado-corta")]
    public async Task Registrar_ConUnaContrasenaQueLaPoliticaRechaza_NoCreaLaCuenta(string entrada, string clave)
    {
        var e = new EntornoDeIdentidad();

        var resultado = await e.Registro().Registrar(e.Alta() with { Contrasena = new ValorSecreto(entrada.Length == 0 ? " " : entrada) });

        Assert.False(resultado.EsExito);
        Assert.Equal("contrasena", resultado.Error!.Errores[0].Campo);
        Assert.Equal(clave, resultado.Error.Errores[0].Clave);
        Assert.Empty(e.Bd.Cuentas);
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-01")]
    [InlineData("sin-arroba", "correo")]
    [InlineData("a@b@c.com", "correo")]
    [InlineData("a b@c.com", "correo")]
    [InlineData("a@c.com, b@c.com", "correo")]
    public async Task Registrar_ConUnCorreoQueNoEsUnaSolaDireccion_SeRechazaAntesDeTocarNada(string correo, string campo)
    {
        var e = new EntornoDeIdentidad();

        var resultado = await e.Registro().Registrar(e.Alta(correo));

        Assert.Equal(campo, resultado.Error!.Errores[0].Campo);
        Assert.Empty(e.Limites.Reservas);
        Assert.Equal(0, e.Hasher.Resumenes);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-62")]
    public async Task Registrar_ReservaElCupoDeCodigosDelOrigen_YAlAgotarloLaCuentaSeCreaSinEncolarElCodigo()
    {
        var e = new EntornoDeIdentidad();
        e.Limites.Maximos["cupo-de-codigos-por-origen"] = 1;

        var primera = await e.Registro().Registrar(e.Alta("Ana@Example.com"));
        var segunda = await e.Registro().Registrar(e.Alta("Luis@Example.com"));

        Assert.True(primera.EsExito);
        Assert.True(segunda.EsExito);
        Assert.Equal(new SolicitudDeCodigoEnSegundoPlano("ana@example.com", PropositoDeCodigo.VerificacionDeCorreo), Assert.Single(e.Cola.Encoladas));
        Assert.NotNull(await e.Cuentas.PorCorreo("luis@example.com"));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public async Task Registrar_EncolaElCodigoDeVerificacion_ElCorreoNoSaleEnElCaminoSincrono()
    {
        var e = new EntornoDeIdentidad();

        await e.Registro().Registrar(e.Alta());

        Assert.Equal(new SolicitudDeCodigoEnSegundoPlano("ana@example.com", PropositoDeCodigo.VerificacionDeCorreo), Assert.Single(e.Cola.Encoladas));
        Assert.Empty(e.Correo.Codigos);

        await e.ProcesarLaCola();

        Assert.Equal("ana@example.com", Assert.Single(e.Correo.Codigos).Destinatario);
        Assert.Matches("^[0-9]{6}$", e.UltimoCodigoEnviado());
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-01")]
    public async Task Registrar_UnCorreoYaUsadoPorUnaCuentaVerificadaODelPersonal_SeRechazaSinDatosYSinEnviarCodigo()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("verificada@example.com");
        e.Cuenta("personal@example.com", RolDeUsuario.AdministradorCentral, verificada: false);
        var antes = e.Bd.Cuentas.Count;

        var a = await e.Registro().Registrar(e.Alta("verificada@example.com"));
        var b = await e.Registro().Registrar(e.Alta("personal@example.com"));

        foreach (var r in new[] { a, b })
        {
            Assert.Equal((CategoriaDeError.Conflicto, "correo-en-uso"), (r.Error!.Categoria, r.Error.Clave));
            Assert.Empty(r.Error.Errores);
        }

        Assert.Equal(antes, e.Bd.Cuentas.Count);
        Assert.Empty(e.Cola.Encoladas);
        Assert.Equal(2, e.Hasher.Resumenes);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-01")]
    public async Task DosRegistrosConElMismoCorreo_DejanUnaSolaCuenta_PorqueLaPrimeraSinVerificarSeReemplaza()
    {
        var e = new EntornoDeIdentidad();
        var registro = e.Registro();

        var resultados = await Task.WhenAll(registro.Registrar(e.Alta()), registro.Registrar(e.Alta(nombre: "Otra")));

        Assert.All(resultados, r => Assert.True(r.EsExito));
        Assert.Equal("Otra", Assert.Single(e.Bd.Cuentas.Values).Nombre);
        Assert.Single(e.Auditoria.Registradas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-01")]
    public async Task Registrar_ReservaElLimiteDeRegistrosPorOrigen_YElCuartoSeRechazaOFallaCerrado()
    {
        var e = new EntornoDeIdentidad();
        var registro = e.Registro();
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await registro.Registrar(e.Alta($"p{i}@example.com"))).EsExito);
        }

        var cuarto = await registro.Registrar(e.Alta("p3@example.com"));
        e.Limites.Caido = true;
        var sinAlmacen = await registro.Registrar(e.Alta("p4@example.com"));

        Assert.Equal(CategoriaDeError.Limite, cuarto.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, sinAlmacen.Error!.Categoria);
        Assert.Equal(3, e.Bd.Cuentas.Count);
    }

    // ---------- Reemplazo de una cuenta sin verificar (IDE-84, IDE-88) ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-26")]
    public async Task UnaCuentaDeClienteSinVerificarYSinPendientes_SeReemplaza_ConCarritoRenovacionesYAuditoria()
    {
        var e = new EntornoDeIdentidad();
        var vieja = e.Cuenta("ana@example.com", verificada: false);
        var sesion = await e.Sesiones.Nueva(vieja.Usuario, EntornoDeIdentidad.Inicio, default);

        var resultado = await e.Registro().Registrar(e.Alta("ana@example.com", nombre: "Ana Nueva"));

        Assert.True(resultado.EsExito);
        var nueva = Assert.Single(e.Bd.Cuentas.Values);
        Assert.NotEqual(vieja.Usuario.Id, nueva.Usuario.Id);
        Assert.Equal("Ana Nueva", nueva.Nombre);
        Assert.Equal([vieja.Usuario.Id], e.Carrito.Eliminados);
        Assert.All(e.Bd.Renovaciones.Where(r => r.UsuarioId == vieja.Usuario.Id), r => Assert.NotNull(r.RevocadaEn));
        var evento = Assert.Single(e.Auditoria.Registradas);
        Assert.Equal(AccionesAuditables.CuentaReemplazada, evento.Accion);
        Assert.Equal(vieja.Usuario.Id.ToString("D"), evento.ObjetoId);
        Assert.NotNull(sesion);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-63")]
    public async Task UnaCuentaSinVerificarConPendientes_NoSeReemplaza_ComoUnCorreoEnUso()
    {
        var e = new EntornoDeIdentidad();
        var vieja = e.Cuenta("ana@example.com", verificada: false);
        e.Pendientes.Cantidad = 1;

        var resultado = await e.Registro().Registrar(e.Alta("ana@example.com"));

        Assert.Equal("correo-en-uso", resultado.Error!.Clave);
        Assert.Same(vieja, Assert.Single(e.Bd.Cuentas.Values));
        Assert.Empty(e.Carrito.Eliminados);
        Assert.Empty(e.Auditoria.Registradas);
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-63")]
    [InlineData("pendientes")]
    [InlineData("carrito")]
    [InlineData("auditoria")]
    public async Task SiUnPuertoDelReemplazoFalla_NoSeReemplazaNada_YElRechazoEsIndistinguibleDeIde13(string quienFalla)
    {
        var e = new EntornoDeIdentidad();
        var vieja = e.Cuenta("ana@example.com", verificada: false);
        e.Cuenta("otra@example.com", verificada: true);
        await e.Sesiones.Nueva(vieja.Usuario, EntornoDeIdentidad.Inicio, default);
        e.Pendientes.Falla = quienFalla == "pendientes";
        e.Carrito.Falla = quienFalla == "carrito";
        e.Auditoria.Falla = quienFalla == "auditoria";

        var fallido = await e.Registro().Registrar(e.Alta("ana@example.com"));
        var enUso = await e.Registro().Registrar(e.Alta("otra@example.com"));

        Assert.Equal((enUso.Error!.Categoria, enUso.Error.Clave, enUso.Error.Errores.Count), (fallido.Error!.Categoria, fallido.Error.Clave, fallido.Error.Errores.Count));
        Assert.Same(vieja, e.Bd.Cuentas[vieja.Usuario.Id]);
        Assert.Equal(2, e.Bd.Cuentas.Count);
        Assert.Empty(e.Carrito.Eliminados);
        Assert.All(e.Bd.Renovaciones, r => Assert.Null(r.RevocadaEn));
        Assert.Empty(e.Auditoria.Registradas);
        Assert.Empty(e.Cola.Encoladas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-26")]
    public async Task NuncaSeReemplazaUnaCuentaDelPersonalAunqueNoTengaElCorreoVerificado()
    {
        var e = new EntornoDeIdentidad();
        var personal = e.Cuenta("jefe@example.com", RolDeUsuario.AdministradorCentral, verificada: false);

        var resultado = await e.Registro().Registrar(e.Alta("jefe@example.com"));

        Assert.Equal("correo-en-uso", resultado.Error!.Clave);
        Assert.Same(personal, Assert.Single(e.Bd.Cuentas.Values));
        Assert.Empty(e.Carrito.Eliminados);
    }

    // ---------- Verificación del correo ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public async Task ElCodigoCorrecto_VerificaElCorreo_YEmiteLaCredencialConEseDato()
    {
        var e = new EntornoDeIdentidad();
        var registro = e.Registro();
        await registro.Registrar(e.Alta());
        await e.ProcesarLaCola();

        var resultado = await registro.VerificarCorreo("ana@example.com", new ValorSecreto(e.UltimoCodigoEnviado()));

        Assert.True(resultado.EsExito);
        Assert.True(resultado.Valor.Credencial!.CorreoVerificado);
        Assert.Equal(RolDeCuenta.Cliente, resultado.Valor.Credencial.Rol);
        Assert.True(Assert.Single(e.Bd.Cuentas.Values).Usuario.CorreoVerificado);
        var datos = Assert.Single(e.Emisor.Emitidas);
        Assert.True(datos.CorreoVerificado);
        Assert.NotEmpty(e.Bd.Renovaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public async Task UnCodigoIncorrecto_FallaConElMensajeGenerico_YDejaUnEventoDeSeguridad()
    {
        var e = new EntornoDeIdentidad();
        var registro = e.Registro();
        await registro.Registrar(e.Alta());
        await e.ProcesarLaCola();
        var incorrecto = e.UltimoCodigoEnviado() == "000000" ? "111111" : "000000";

        var resultado = await registro.VerificarCorreo("ana@example.com", new ValorSecreto(incorrecto));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, resultado.Error);
        Assert.False(Assert.Single(e.Bd.Cuentas.Values).Usuario.CorreoVerificado);
        var evento = Assert.Single(e.Seguridad.Eventos);
        Assert.Equal(AccionesAuditables.CodigoDeUnSoloUsoRechazado, evento.Accion);
        Assert.Equal("incorrecto", evento.Motivo);
        Assert.DoesNotContain(incorrecto, string.Join(' ', evento.Detalle.Values), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public async Task ConCincoIntentosIncorrectos_NiElCodigoCorrectoSirve()
    {
        var e = new EntornoDeIdentidad();
        var registro = e.Registro();
        await registro.Registrar(e.Alta());
        await e.ProcesarLaCola();
        var correcto = e.UltimoCodigoEnviado();
        var incorrecto = correcto == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
        {
            await registro.VerificarCorreo("ana@example.com", new ValorSecreto(incorrecto));
        }

        var resultado = await registro.VerificarCorreo("ana@example.com", new ValorSecreto(correcto));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, resultado.Error);
        Assert.Equal("intentos-agotados", e.Seguridad.Eventos[^1].Motivo);
        Assert.False(Assert.Single(e.Bd.Cuentas.Values).Usuario.CorreoVerificado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public async Task UnCodigoVencidoOYaUsado_FallaConElMismoMensaje()
    {
        var e = new EntornoDeIdentidad();
        var registro = e.Registro();
        await registro.Registrar(e.Alta());
        await e.ProcesarLaCola();
        var codigo = e.UltimoCodigoEnviado();
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddMinutes(11);

        var vencido = await registro.VerificarCorreo("ana@example.com", new ValorSecreto(codigo));
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio;
        await registro.ReenviarCodigo("ana@example.com");
        await e.ProcesarLaCola();
        var nuevo = e.UltimoCodigoEnviado();
        var primeraVez = await registro.VerificarCorreo("ana@example.com", new ValorSecreto(nuevo));
        var segundaVez = await registro.VerificarCorreo("ana@example.com", new ValorSecreto(nuevo));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, vencido.Error);
        Assert.True(primeraVez.EsExito);
        Assert.Equal(FallosDeIdentidad.CodigoInvalido, segundaVez.Error);
        Assert.Contains(e.Seguridad.Eventos, ev => ev.Motivo == "vencido");
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public async Task UnCodigoDeRecuperacion_NoServiaParaVerificarElCorreo_ElPropositoVaEnLaHuella()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("ana@example.com", verificada: false);
        var codigo = "123456";
        await e.Codigos.Emitir(CodigoDeUnSoloUso.Emitir(cuenta.Usuario.Id, PropositoDeCodigo.RecuperacionDeAcceso, e.HuellasDeIdentidad.DeCodigo(cuenta.Usuario.Id, PropositoDeCodigo.RecuperacionDeAcceso, codigo), EntornoDeIdentidad.Inicio), default);

        var resultado = await e.Registro().VerificarCorreo("ana@example.com", new ValorSecreto(codigo));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, resultado.Error);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public async Task VerificarUnCorreoQueNoExisteOYaEstaVerificado_FallaConElMismoMensaje_SinRevelarNada()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("verificada@example.com", verificada: true);
        var registro = e.Registro();

        var inexistente = await registro.VerificarCorreo("nadie@example.com", new ValorSecreto("123456"));
        var verificada = await registro.VerificarCorreo("verificada@example.com", new ValorSecreto("123456"));
        var malFormado = await registro.VerificarCorreo("no-es-un-correo", new ValorSecreto("abc"));

        Assert.All(new[] { inexistente, verificada, malFormado }, r => Assert.Equal(FallosDeIdentidad.CodigoInvalido, r.Error));
        Assert.Empty(e.Emisor.Emitidas);
    }

    // ---------- Reenvío ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public async Task ReenviarElCodigo_RespondeLoMismoExistaONoLaCuenta_YRegistraLaFilaIdentica()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("existe@example.com", verificada: false);
        var registro = e.Registro();

        var existente = await registro.ReenviarCodigo("existe@example.com");
        var inexistente = await registro.ReenviarCodigo("nadie@example.com");

        Assert.True(existente.EsExito);
        Assert.True(inexistente.EsExito);
        Assert.Equal(2, e.Solicitudes.Registradas.Count);
        Assert.Equal(2, e.Cola.Encoladas.Count);
        Assert.Empty(e.Correo.Codigos);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public async Task ReenviarElCodigo_ElTercerPedidoPorHoraSePermiteYElCuartoSeRechazaIgualExistaONoLaCuenta()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("existe@example.com", verificada: false);
        var registro = e.Registro();
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await registro.ReenviarCodigo("existe@example.com")).EsExito);
        }

        var existente = await registro.ReenviarCodigo("existe@example.com");

        for (var i = 0; i < 3; i++)
        {
            Assert.True((await new EntornoDeIdentidad().Registro().ReenviarCodigo("nadie@example.com")).EsExito);
        }

        var otro = new EntornoDeIdentidad();
        for (var i = 0; i < 3; i++)
        {
            await otro.Registro().ReenviarCodigo("nadie@example.com");
        }

        var inexistente = await otro.Registro().ReenviarCodigo("nadie@example.com");
        Assert.Equal(CategoriaDeError.Limite, existente.Error!.Categoria);
        Assert.Equal((existente.Error.Categoria, existente.Error.Clave), (inexistente.Error!.Categoria, inexistente.Error.Clave));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public async Task ElCodigoDeUnaCuentaInexistenteVerificadaODelPersonal_NoSeEnviaNunca()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("verificada@example.com", verificada: true);
        e.Cuenta("jefe@example.com", RolDeUsuario.Logistica, verificada: false);
        var registro = e.Registro();
        foreach (var correo in new[] { "nadie@example.com", "verificada@example.com", "jefe@example.com" })
        {
            await registro.ReenviarCodigo(correo);
        }

        await e.ProcesarLaCola();

        Assert.Empty(e.Correo.Codigos);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public async Task ElCanalLleno_DescartaEnSilencio_ConLaMismaRespuestaYUnaAlertaTecnica()
    {
        var e = new EntornoDeIdentidad();
        e.Cola.Llena = true;

        var resultado = await e.Registro().ReenviarCodigo("alguien@example.com");
        await Task.Delay(50);

        Assert.True(resultado.EsExito);
        Assert.Contains(e.Alertas.Alertas, a => a.Motivo == EmisionDeCodigos.MotivoDeFallaDelCorreo);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public async Task UnaFallaDelCorreo_NoInterrumpeElFlujo_YDejaUnaAlertaTecnica()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("ana@example.com", verificada: false);
        e.Correo.Falla = true;
        var registro = e.Registro();
        await registro.ReenviarCodigo("ana@example.com");

        await e.ProcesarLaCola();

        Assert.Contains(e.Alertas.Alertas, a => a.Motivo == EmisionDeCodigos.MotivoDeFallaDelCorreo);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public async Task UnCodigoNuevoInvalidaElAnterior_ElCodigoSeEnviaSoloALaDireccionRegistrada()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("ana@example.com", verificada: false);
        var registro = e.Registro();
        await registro.ReenviarCodigo("ana@example.com");
        await e.ProcesarLaCola();
        var primero = e.UltimoCodigoEnviado();
        await registro.ReenviarCodigo("ANA@example.com");
        await e.ProcesarLaCola();

        var viejo = await registro.VerificarCorreo("ana@example.com", new ValorSecreto(primero));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, viejo.Error);
        Assert.All(e.Correo.Codigos, c => Assert.Equal("ana@example.com", c.Destinatario));
    }

    // ---------- Correcciones de la revisión de seguridad ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public async Task ConElSegundoFactorActivo_ElCorreoSeVerificaPeroNoSeEmiteSesion()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("ana@example.com", verificada: false);
        e.ConSegundoFactor(cuenta);
        var registro = e.Registro();
        await registro.ReenviarCodigo("ana@example.com");
        await e.ProcesarLaCola();

        var resultado = await registro.VerificarCorreo("ana@example.com", new ValorSecreto(e.UltimoCodigoEnviado()));

        Assert.True(resultado.EsExito);
        Assert.Null(resultado.Valor.Credencial);
        Assert.Null(resultado.Valor.Desafio);
        Assert.True(cuenta.Usuario.CorreoVerificado);
        Assert.Empty(e.Emisor.Emitidas);
        Assert.Empty(e.Bd.Renovaciones);
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-01")]
    [InlineData("a@b")]
    [InlineData("a@b.")]
    public async Task UnCorreoQueNoAdmiteUnCodigo_SeRechazaAlRegistrar_PorqueNuncaSePodriaVerificar(string correo)
    {
        var e = new EntornoDeIdentidad();

        var resultado = await e.Registro().Registrar(e.Alta(correo));

        Assert.Equal("correo", resultado.Error!.Errores[0].Campo);
        Assert.True(EntradasDeIdentidad.TryNormalizarElCorreo("ana@example.com", out _));
        Assert.Empty(e.Bd.Cuentas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-63")]
    public async Task UnaFallaDeUnPuertoDelReemplazo_DejaUnaAlertaTecnica_ElRechazoSigueSiendoElMismo()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("ana@example.com", verificada: false);
        e.Pendientes.Falla = true;

        var resultado = await e.Registro().Registrar(e.Alta("ana@example.com"));

        Assert.Equal("correo-en-uso", resultado.Error!.Clave);
        Assert.Contains(e.Alertas.Alertas, a => a.Motivo == EmisionDeCodigos.MotivoDeFallaDelCorreo);
    }
}
