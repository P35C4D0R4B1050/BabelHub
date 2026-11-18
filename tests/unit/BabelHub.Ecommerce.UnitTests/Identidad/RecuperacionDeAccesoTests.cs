using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// Recuperación del acceso (identidad-seguridad T17; AC-IDE-15, AC-IDE-16, AC-IDE-17, AC-IDE-40, AC-IDE-45; IDE-17, IDE-18, IDE-19, IDE-29, IDE-45, IDE-65): respuesta y camino síncrono idénticos exista o no la cuenta,
/// el código solo a un cliente con el correo verificado, la contraseña nueva con revocación de todas las renovaciones, el aviso sin la contraseña y el segundo factor que se mantiene.
/// </summary>
public sealed class RecuperacionDeAccesoTests
{
    private const string Nueva = "una frase nueva y segura";

    private static async Task<string> PedirElCodigo(EntornoDeIdentidad e, RecuperacionDeAcceso recuperacion, string correo = "ana@example.com")
    {
        Assert.True((await recuperacion.Solicitar(correo)).EsExito);
        await e.ProcesarLaCola();
        return e.UltimoCodigoEnviado();
    }

    // ---------- Solicitar ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-16")]
    public async Task LaRespuestaYElCaminoSincronoSonIdenticos_ExistaONoLaCuenta_SeaDelPersonalONoEsteVerificada()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("verificada@example.com");
        e.Cuenta("sin-verificar@example.com", verificada: false);
        e.Cuenta("jefe@example.com", RolDeUsuario.AdministradorCentral);
        var recuperacion = e.Recuperacion();
        e.Limites.Maximos["codigos-por-origen"] = 100;
        var correos = new[] { "verificada@example.com", "sin-verificar@example.com", "jefe@example.com", "nadie@example.com" };

        var respuestas = new List<Resultado<RespuestaUniforme>>();
        foreach (var correo in correos)
        {
            respuestas.Add(await recuperacion.Solicitar(correo));
        }

        Assert.All(respuestas, r => Assert.True(r.EsExito));
        Assert.Equal(4, e.Solicitudes.Registradas.Count);
        Assert.Equal(4, e.Cola.Encoladas.Count);
        Assert.Empty(e.Correo.Codigos);
        Assert.Empty(e.Seguridad.Eventos);
        Assert.Equal(0, e.Hasher.Senuelos + e.Hasher.Resumenes);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-16")]
    public async Task SoloUnClienteActivoConElCorreoVerificado_RecibeElCodigo_EnSegundoPlano()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("verificada@example.com");
        e.Cuenta("sin-verificar@example.com", verificada: false);
        e.Cuenta("jefe@example.com", RolDeUsuario.AdministradorCentral);
        e.Cuenta("desactivada@example.com", activa: false);
        var cancelada = e.Cuenta("cancelada@example.com");
        cancelada.CanceladaEn = EntornoDeIdentidad.Inicio;
        var recuperacion = e.Recuperacion();
        e.Limites.Maximos["codigos-por-origen"] = 100;
        foreach (var correo in new[] { "verificada@example.com", "sin-verificar@example.com", "jefe@example.com", "nadie@example.com", "desactivada@example.com", "cancelada@example.com" })
        {
            await recuperacion.Solicitar(correo);
        }

        await e.ProcesarLaCola();

        Assert.Equal("verificada@example.com", Assert.Single(e.Correo.Codigos).Destinatario);
        Assert.Equal(6, e.Seguridad.Eventos.Count(ev => ev.Accion == AccionesAuditables.RecuperacionDeAccesoSolicitada));
        Assert.Equal(5, e.Seguridad.Eventos.Count(ev => ev.Accion == AccionesAuditables.RecuperacionDeAccesoSolicitada && ev.ObjetoId is not null));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-16")]
    public async Task ElEventoDeSolicitudGuardaLaCuentaSoloSiExiste_YNuncaElCodigo()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("verificada@example.com");
        var recuperacion = e.Recuperacion();
        await recuperacion.Solicitar("verificada@example.com");
        await recuperacion.Solicitar("nadie@example.com");

        await e.ProcesarLaCola();

        Assert.Equal(cuenta.Usuario.Id.ToString("D"), e.Seguridad.Eventos[0].ObjetoId);
        Assert.Null(e.Seguridad.Eventos[1].ObjetoId);
        Assert.DoesNotContain(e.UltimoCodigoEnviado(), string.Join(' ', e.Seguridad.Eventos.Select(ev => ev.ToString() + string.Join(' ', ev.Detalle.Values))), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-16")]
    public async Task ElTercerPedidoPorHoraSePermite_ElCuartoSeRechazaIgualExistaONoLaCuenta()
    {
        var existe = new EntornoDeIdentidad();
        existe.Cuenta("verificada@example.com");
        var noExiste = new EntornoDeIdentidad();
        var a = existe.Recuperacion();
        var b = noExiste.Recuperacion();
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await a.Solicitar("verificada@example.com")).EsExito);
            Assert.True((await b.Solicitar("nadie@example.com")).EsExito);
        }

        var cuartoA = await a.Solicitar("verificada@example.com");
        var cuartoB = await b.Solicitar("nadie@example.com");

        Assert.Equal(CategoriaDeError.Limite, cuartoA.Error!.Categoria);
        Assert.Equal((cuartoA.Error.Categoria, cuartoA.Error.Clave), (cuartoB.Error!.Categoria, cuartoB.Error.Clave));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-16")]
    public async Task ElCanalLleno_DescartaEnSilencio_ConLaMismaRespuestaYUnaAlertaTecnica_YUnaFallaDelCorreoNoCambiaNada()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("verificada@example.com");
        var recuperacion = e.Recuperacion();
        e.Cola.Llena = true;

        var lleno = await recuperacion.Solicitar("verificada@example.com");
        e.Cola.Llena = false;
        e.Correo.Falla = true;
        var conCorreoCaido = await recuperacion.Solicitar("verificada@example.com");
        await e.ProcesarLaCola();

        Assert.True(lleno.EsExito);
        Assert.True(conCorreoCaido.EsExito);
        Assert.Contains(e.Alertas.Alertas, a => a.Motivo == EmisionDeCodigos.MotivoDeFallaDelCorreo);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-16")]
    public async Task ConElAlmacenDeLimitesCaido_FallaCerrado_ConLaMismaRespuestaExistaONoLaCuenta()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("verificada@example.com");
        e.Limites.Caido = true;
        var recuperacion = e.Recuperacion();

        var existe = await recuperacion.Solicitar("verificada@example.com");
        var noExiste = await recuperacion.Solicitar("nadie@example.com");

        Assert.Equal(CategoriaDeError.NoDisponible, existe.Error!.Categoria);
        Assert.Equal(existe.Error, noExiste.Error);
        Assert.Empty(e.Cola.Encoladas);
    }

    // ---------- Confirmar ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-15")]
    public async Task ConElCodigoCorrecto_DefineLaContrasenaNueva_InvalidaElCodigo_YRevocaLasRenovaciones()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("ana@example.com");
        var sesion = await e.Sesiones.Nueva(cuenta.Usuario, EntornoDeIdentidad.Inicio, default);
        var otra = await e.Sesiones.Nueva(cuenta.Usuario, EntornoDeIdentidad.Inicio, default);
        var recuperacion = e.Recuperacion();
        var codigo = await PedirElCodigo(e, recuperacion);
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddMinutes(3);

        var resultado = await recuperacion.Confirmar("ana@example.com", new ValorSecreto(codigo), new ValorSecreto(Nueva));
        var repetido = await recuperacion.Confirmar("ana@example.com", new ValorSecreto(codigo), new ValorSecreto("otra frase nueva y segura"));

        Assert.True(resultado.EsExito);
        Assert.Equal(HasherDePrueba.ResumenDe(Nueva), cuenta.ResumenDeContrasena);
        Assert.Equal(FallosDeIdentidad.CodigoInvalido, repetido.Error);
        Assert.Equal(HasherDePrueba.ResumenDe(Nueva), cuenta.ResumenDeContrasena);
        Assert.All(e.Bd.Renovaciones, r => Assert.NotNull(r.RevocadaEn));
        Assert.NotNull(cuenta.Usuario.SesionesValidasDesde);
        Assert.Equal(AccionesAuditables.RecuperacionDeAccesoCompletada, Assert.Single(e.Auditoria.Registradas).Accion);
        Assert.NotNull(sesion);
        Assert.NotNull(otra);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-15")]
    public async Task LaContrasenaAnteriorDejaDeServir_YLaNuevaPermiteIngresar()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("ana@example.com");
        var recuperacion = e.Recuperacion();
        var codigo = await PedirElCodigo(e, recuperacion);

        await recuperacion.Confirmar("ana@example.com", new ValorSecreto(codigo), new ValorSecreto(Nueva));
        var ingreso = e.Ingreso();
        var vieja = await ingreso.Ingresar(new SolicitudDeIngreso("ana@example.com", new ValorSecreto(EntornoDeIdentidad.ContrasenaSegura)));
        var nueva = await ingreso.Ingresar(new SolicitudDeIngreso("ana@example.com", new ValorSecreto(Nueva)));

        Assert.Equal(FallosDeIdentidad.CredencialesInvalidas, vieja.Error);
        Assert.True(nueva.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-17")]
    public async Task UnCodigoIncorrectoVencidoOUsado_FallaConElMismoMensajeGenerico_YDejaElEventoConSuMotivo()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("ana@example.com");
        var recuperacion = e.Recuperacion();
        var codigo = await PedirElCodigo(e, recuperacion);
        var incorrecto = codigo == "000000" ? "111111" : "000000";

        var mal = await recuperacion.Confirmar("ana@example.com", new ValorSecreto(incorrecto), new ValorSecreto(Nueva));
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddMinutes(11);
        var vencido = await recuperacion.Confirmar("ana@example.com", new ValorSecreto(codigo), new ValorSecreto(Nueva));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, mal.Error);
        Assert.Equal(FallosDeIdentidad.CodigoInvalido, vencido.Error);
        Assert.Equal(["incorrecto", "vencido"], e.Seguridad.Eventos.Where(ev => ev.Accion == AccionesAuditables.CodigoDeUnSoloUsoRechazado).Select(ev => ev.Motivo));
        Assert.Equal(HasherDePrueba.ResumenDe(EntornoDeIdentidad.ContrasenaSegura), cuenta.ResumenDeContrasena);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-17")]
    public async Task ConCincoIntentosIncorrectos_NiElCodigoCorrectoSirve()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("ana@example.com");
        var recuperacion = e.Recuperacion();
        var codigo = await PedirElCodigo(e, recuperacion);
        var incorrecto = codigo == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
        {
            await recuperacion.Confirmar("ana@example.com", new ValorSecreto(incorrecto), new ValorSecreto(Nueva));
        }

        var resultado = await recuperacion.Confirmar("ana@example.com", new ValorSecreto(codigo), new ValorSecreto(Nueva));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, resultado.Error);
        Assert.Equal("intentos-agotados", e.Seguridad.Eventos[^1].Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-17")]
    public async Task UnCodigoNuevoInvalidaElAnterior()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("ana@example.com");
        var recuperacion = e.Recuperacion();
        var primero = await PedirElCodigo(e, recuperacion);
        var segundo = await PedirElCodigo(e, recuperacion);

        var viejo = await recuperacion.Confirmar("ana@example.com", new ValorSecreto(primero), new ValorSecreto(Nueva));
        var nuevo = await recuperacion.Confirmar("ana@example.com", new ValorSecreto(segundo), new ValorSecreto(Nueva));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, viejo.Error);
        Assert.True(nuevo.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-16")]
    public async Task ConfirmarParaUnaCuentaQueNoPuedeRecuperar_FallaConElMismoMensaje_SinRevelarNada()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("sin-verificar@example.com", verificada: false);
        e.Cuenta("jefe@example.com", RolDeUsuario.Logistica);
        var recuperacion = e.Recuperacion();

        var resultados = new[]
        {
            await recuperacion.Confirmar("sin-verificar@example.com", new ValorSecreto("123456"), new ValorSecreto(Nueva)),
            await recuperacion.Confirmar("jefe@example.com", new ValorSecreto("123456"), new ValorSecreto(Nueva)),
            await recuperacion.Confirmar("nadie@example.com", new ValorSecreto("123456"), new ValorSecreto(Nueva)),
            await recuperacion.Confirmar("no-es-correo", new ValorSecreto("abc"), new ValorSecreto(Nueva)),
        };

        Assert.All(resultados, r => Assert.Equal(FallosDeIdentidad.CodigoInvalido, r.Error));
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-15")]
    [InlineData("corta", "demasiado-corta")]
    [InlineData("contrasena123", "demasiado-comun")]
    public async Task UnaContrasenaNuevaQueLaPoliticaRechaza_NoGastaUnIntentoDelCodigo(string contrasena, string clave)
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("ana@example.com");
        var recuperacion = e.Recuperacion();
        var codigo = await PedirElCodigo(e, recuperacion);

        var rechazada = await recuperacion.Confirmar("ana@example.com", new ValorSecreto(codigo), new ValorSecreto(contrasena));
        var luego = await recuperacion.Confirmar("ana@example.com", new ValorSecreto(codigo), new ValorSecreto(Nueva));

        Assert.Equal(clave, rechazada.Error!.Errores[0].Clave);
        Assert.True(luego.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-15")]
    public async Task SiNoSePuedeAuditarLaRecuperacion_NoSeCompleta_YElCodigoNoSeConsume()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("ana@example.com");
        await e.Sesiones.Nueva(cuenta.Usuario, EntornoDeIdentidad.Inicio, default);
        var recuperacion = e.Recuperacion();
        var codigo = await PedirElCodigo(e, recuperacion);
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => recuperacion.Confirmar("ana@example.com", new ValorSecreto(codigo), new ValorSecreto(Nueva)));

        Assert.Equal(HasherDePrueba.ResumenDe(EntornoDeIdentidad.ContrasenaSegura), cuenta.ResumenDeContrasena);
        Assert.All(e.Bd.Renovaciones, r => Assert.Null(r.RevocadaEn));
        Assert.Empty(e.Correo.Correos);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-40")]
    public async Task ElAvisoDeCambioSaleAlCorreoDeLaCuenta_SinLaContrasena_YUnaFallaDelCorreoNoLoImpide()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("ana@example.com");
        var recuperacion = e.Recuperacion();
        var codigo = await PedirElCodigo(e, recuperacion);
        e.Correo.Falla = true;

        var resultado = await recuperacion.Confirmar("ana@example.com", new ValorSecreto(codigo), new ValorSecreto(Nueva));
        e.Correo.Falla = false;
        var segundoCodigo = await PedirElCodigo(e, recuperacion);
        var segunda = await recuperacion.Confirmar("ana@example.com", new ValorSecreto(segundoCodigo), new ValorSecreto("otra frase nueva y segura"));

        Assert.True(resultado.EsExito);
        Assert.True(segunda.EsExito);
        var aviso = Assert.Single(e.Correo.Correos);
        Assert.Equal(("ana@example.com", "contrasena-cambiada"), (aviso.Destinatario, aviso.Plantilla));
        Assert.DoesNotContain("frase", string.Join(' ', aviso.Datos.Values), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-45")]
    public async Task ElSegundoFactorSeMantieneTrasLaRecuperacion()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("ana@example.com");
        e.ConSegundoFactor(cuenta);
        var recuperacion = e.Recuperacion();
        var codigo = await PedirElCodigo(e, recuperacion);

        await recuperacion.Confirmar("ana@example.com", new ValorSecreto(codigo), new ValorSecreto(Nueva));

        var guardado = await e.SegundoFactor.Obtener(cuenta.Usuario.Id);
        Assert.True(guardado!.Factor.EstaActivo);
        Assert.Equal(8, guardado.Factor.CodigosDeRespaldoRestantes);
        var ingreso = await e.Ingreso().Ingresar(new SolicitudDeIngreso("ana@example.com", new ValorSecreto(Nueva)));
        Assert.Equal(PasoPendienteDelIngreso.PresentarSegundoFactor, ingreso.Valor.Desafio!.Paso);
    }

    // ---------- Correcciones de la revisión de seguridad ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-16")]
    public async Task ConfirmarCuestaLoMismoSeaLaCuentaElegibleONo_ElTiempoNoDelataQueCuentasExisten()
    {
        var elegible = new EntornoDeIdentidad();
        elegible.Cuenta("ana@example.com");
        var recuperacion = elegible.Recuperacion();
        var codigo = await PedirElCodigo(elegible, recuperacion);
        var antes = elegible.Hasher.Resumenes;
        await recuperacion.Confirmar("ana@example.com", new ValorSecreto(codigo), new ValorSecreto(Nueva));
        var costoElegible = elegible.Hasher.Resumenes - antes;

        var otro = new EntornoDeIdentidad();
        otro.Cuenta("sin-verificar@example.com", verificada: false);
        otro.Cuenta("jefe@example.com", RolDeUsuario.Logistica);
        var r = otro.Recuperacion();
        foreach (var correo in new[] { "sin-verificar@example.com", "jefe@example.com", "nadie@example.com" })
        {
            var inicio = otro.Hasher.Resumenes;
            await r.Confirmar(correo, new ValorSecreto("123456"), new ValorSecreto(Nueva));
            Assert.Equal(costoElegible, otro.Hasher.Resumenes - inicio);
        }
    }
}
