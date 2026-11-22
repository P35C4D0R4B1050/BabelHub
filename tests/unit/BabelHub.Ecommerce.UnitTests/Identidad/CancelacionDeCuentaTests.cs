using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using DominioEstado = BabelHub.Ecommerce.Domain.Identidad.EstadoDeCancelacion;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// La solicitud de cancelación de la cuenta (identidad-seguridad T22; AC-IDE-24, AC-IDE-25, AC-IDE-61; IDE-27, IDE-28, IDE-68, IDE-70, IDE-71, IDE-76, IDE-81): contraseña y segundo factor, ejecución sin pendientes en la misma
/// operación, diferida con pendientes (no compra, aviso, desistimiento), plazo de 7 días sin pasar del día 30, reconfirmación que vuelve a contar los pendientes y la lectura de la marca con bloqueo.
/// </summary>
public sealed class CancelacionDeCuentaTests
{
    private static ValorSecreto S(string texto) => new(texto);

    private static ValorSecreto Contrasena => S(EntornoDeIdentidad.ContrasenaSegura);

    private static (EntornoDeIdentidad E, CuentaDeIdentidad Cliente, CancelacionDeCuenta Cancelacion) Escenario()
    {
        var e = new EntornoDeIdentidad();
        var cliente = e.Cuenta("ana@example.com");
        e.ActuarComo(cliente);
        return (e, cliente, e.Cancelacion());
    }

    /// <summary>Deja una cancelación diferida (con pendientes) y la pasa a esperando la reconfirmación (cerrados los pendientes, aviso enviado).</summary>
    private static async Task<SolicitudDeCancelacion> EsperandoReconfirmacion(EntornoDeIdentidad e, CuentaDeIdentidad cliente, CancelacionDeCuenta cancelacion)
    {
        e.Pendientes.Cantidad = 2;
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        e.Pendientes.Cantidad = 0;
        e.Actor.Actual = ActorActual.DelSistema();
        await e.Revision().Revisar();
        e.ActuarComo(cliente);
        return e.Bd.Cancelaciones.Single();
    }

    // ---------------------------------------------------------------- solicitar sin pendientes (AC-IDE-25)

    [Fact]
    [Trait("Criterio", "AC-IDE-25")]
    public async Task SinPendientes_ConLaContrasena_SeEjecutaEnLaMismaOperacion()
    {
        var (e, cliente, cancelacion) = Escenario();

        var resultado = await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDeLaCancelacion.Ejecutada, resultado.Valor.Estado);
        Assert.Equal([cliente.Usuario.Id], e.Ejecutor.Ejecutadas);
        Assert.NotNull(cliente.CanceladaEn);
        Assert.Empty(e.Bd.Cancelaciones);
    }

    [Theory]
    [InlineData("incorrecta y larga de verdad")]
    [InlineData("   ")]
    [Trait("Criterio", "AC-IDE-25")]
    public async Task SinLaContrasenaActual_NoSeConfirmaNiSeEjecutaNada(string presentada)
    {
        var (e, cliente, cancelacion) = Escenario();

        var resultado = await cancelacion.Solicitar(cliente.Usuario.Id, S(presentada), null);

        Assert.Equal(Reautenticacion.ContrasenaActualIncorrecta, resultado.Error);
        Assert.Empty(e.Ejecutor.Ejecutadas);
        Assert.Null(cliente.CanceladaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-25")]
    public async Task ConElSegundoFactorActivo_SinElCodigo_NoSeConfirma_YConUnCodigoValido_SiSeEjecuta()
    {
        var (e, cliente, cancelacion) = Escenario();
        var (secreto, _) = e.ConSegundoFactor(cliente);

        var sinCodigo = await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        var incorrecto = await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, S("000000"));
        var conCodigo = await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, S(e.Totp(secreto)));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, sinCodigo.Error);
        Assert.Equal(FallosDeIdentidad.CodigoInvalido, incorrecto.Error);
        Assert.Contains(e.Seguridad.Eventos, ev => ev.Accion == AccionesAuditables.SegundoFactorFallido);
        Assert.True(conCodigo.EsExito);
        Assert.Equal(EstadoDeLaCancelacion.Ejecutada, conCodigo.Valor.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-25")]
    public async Task ElMismoCodigoDelSegundoFactor_NoSirveDosVeces()
    {
        var (e, cliente, cancelacion) = Escenario();
        var (secreto, _) = e.ConSegundoFactor(cliente);
        e.Pendientes.Cantidad = 1;
        var codigo = e.Totp(secreto);
        Assert.True((await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, S(codigo))).EsExito);
        await cancelacion.Desistir(cliente.Usuario.Id);

        var repetido = await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, S(codigo));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, repetido.Error);
    }

    [Fact]
    public async Task SoloElTitularCliente_PideCancelar()
    {
        var (e, cliente, cancelacion) = Escenario();
        var personal = e.Cuenta("logistica@example.com", RolDeUsuario.Logistica);
        e.Actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), "cliente");
        var ajena = await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        e.ActuarComo(personal);
        var delPersonal = await cancelacion.Solicitar(personal.Usuario.Id, Contrasena, null);

        Assert.Equal(AlcanceDeLaCuenta.Prohibido, ajena.Error);
        Assert.Equal("solo-clientes", delPersonal.Error!.Clave);
        Assert.Empty(e.Ejecutor.Ejecutadas);
    }

    // ---------------------------------------------------------------- con pendientes (AC-IDE-24)

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public async Task ConPendientes_QuedaDiferida_NoPuedeComprar_VeCuantosFaltan_YRecibeUnAviso()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Pendientes.Cantidad = 3;

        var resultado = await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDeLaCancelacion.Diferida, resultado.Valor.Estado);
        Assert.Equal(3, resultado.Valor.PendientesAbiertos);
        Assert.Equal(e.Reloj.Ahora.AddDays(30), resultado.Valor.SeDescartaEl);
        Assert.Null(resultado.Valor.ReconfirmarHasta);
        Assert.Empty(e.Ejecutor.Ejecutadas);
        Assert.Null(cliente.CanceladaEn);
        Assert.True(e.Bd.Cancelaciones.Single().ImpideComprar);
        Assert.Equal(EstadoDeLaMarca.Diferida, await e.Cancelacion().LeerConBloqueo(cliente.Usuario.Id));
        Assert.Equal("cancelacion-diferida", Assert.Single(e.Bd.Avisos).Aviso.Tipo);
        var correo = Assert.Single(e.Correo.Correos);
        Assert.Equal(AvisosDeCuenta.PlantillaDeCancelacionDiferida, correo.Plantilla);
        Assert.Equal(CategoriaDeCorreo.AvisoDeSeguridad, correo.Categoria);
        Assert.Equal("ana@example.com", correo.Destinatario);
    }

    [Fact]
    public async Task UnaSegundaSolicitud_MientrasHayUnaEnCurso_EsUnConflicto()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Pendientes.Cantidad = 1;
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);

        var otra = await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);

        Assert.Equal("cancelacion-en-curso", otra.Error!.Clave);
        Assert.Single(e.Bd.Cancelaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public async Task SiElCorreoFalla_LaSolicitudYaEstaGuardada_YElAvisoQuedaEnLaCuenta()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Pendientes.Cantidad = 1;
        e.Correo.Falla = true;

        var resultado = await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);

        Assert.True(resultado.EsExito);
        Assert.Single(e.Bd.Cancelaciones);
        Assert.Single(e.Bd.Avisos);
    }

    // ---------------------------------------------------------------- fallar cerrado (IDE-70)

    [Fact]
    [Trait("Criterio", "AC-IDE-57")]
    public async Task SiElPuertoDePendientesNoResponde_NoSeEjecutaNiSeDifiereNada()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Pendientes.Falla = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null));

        Assert.Empty(e.Ejecutor.Ejecutadas);
        Assert.Empty(e.Bd.Cancelaciones);
        Assert.Null(cliente.CanceladaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-57")]
    public async Task SiUnModuloNoPuedeAnonimizar_LaCuentaQuedaIntacta_YSePuedeReintentar()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Ejecutor.Falla = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null));

        Assert.Null(cliente.CanceladaEn);
        Assert.True(cliente.Usuario.Activo);
        e.Ejecutor.Falla = false;
        Assert.True((await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null)).EsExito);
        Assert.NotNull(cliente.CanceladaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public async Task ConPendientes_LaSolicitudYLaConsulta_IndicanCualesFaltanPorGrupo()
    {
        var (e, cliente, cancelacion) = Escenario();
        var faltan = new DesgloseDePendientes(PedidosSinCerrar: 1, Reembolsos: 2, Devoluciones: 0, Otros: 1);
        e.Pendientes.Desglose = faltan;

        var solicitada = await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        e.Pendientes.Desglose = faltan with { PedidosSinCerrar = 0 };
        var consultada = await cancelacion.Consultar(cliente.Usuario.Id);

        Assert.Equal(faltan, solicitada.Valor.Desglose);
        Assert.Equal(4, solicitada.Valor.PendientesAbiertos);
        Assert.Equal(new DesgloseDePendientes(0, 2, 0, 1), consultada.Valor.Desglose);
        Assert.Equal(3, consultada.Valor.PendientesAbiertos);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public async Task SinPendientes_NoHayDesglose_YLaReconfirmacionConUnPendienteNuevoLoInforma()
    {
        var (e, cliente, cancelacion) = Escenario();
        var sinPendientes = await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        Assert.Equal(EstadoDeLaCancelacion.Ejecutada, sinPendientes.Valor.Estado);
        Assert.Null(sinPendientes.Valor.Desglose);

        var (e2, cliente2, cancelacion2) = Escenario();
        await EsperandoReconfirmacion(e2, cliente2, cancelacion2);
        e2.Pendientes.Desglose = new DesgloseDePendientes(0, 0, 1, 0);

        var reconfirmada = await cancelacion2.Reconfirmar(cliente2.Usuario.Id, Contrasena, null);

        Assert.Equal(EstadoDeLaCancelacion.Diferida, reconfirmada.Valor.Estado);
        Assert.Equal(new DesgloseDePendientes(0, 0, 1, 0), reconfirmada.Valor.Desglose);
    }

    // ---------------------------------------------------------------- consultar

    [Fact]
    public async Task Consultar_SinSolicitud_Indica_SinSolicitud_YDiferida_Indica_LosPendientesYLosPlazos()
    {
        var (e, cliente, cancelacion) = Escenario();
        var sin = await cancelacion.Consultar(cliente.Usuario.Id);
        e.Pendientes.Cantidad = 2;
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        e.Pendientes.Cantidad = 1;

        var diferida = await cancelacion.Consultar(cliente.Usuario.Id);

        Assert.Equal(EstadoDeLaCancelacion.SinSolicitud, sin.Valor.Estado);
        Assert.Equal(EstadoDeLaCancelacion.Diferida, diferida.Valor.Estado);
        Assert.Equal(1, diferida.Valor.PendientesAbiertos);
        Assert.Equal(e.Reloj.Ahora.AddDays(30), diferida.Valor.SeDescartaEl);
    }

    [Fact]
    public async Task Consultar_ConElPuertoCaido_Falla_ENoInventaUnNumero()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Pendientes.Cantidad = 2;
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        e.Pendientes.Falla = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => cancelacion.Consultar(cliente.Usuario.Id));
    }

    [Fact]
    public async Task Consultar_DeOtraPersona_EsProhibido()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), "cliente");

        Assert.Equal(AlcanceDeLaCuenta.Prohibido, (await cancelacion.Consultar(cliente.Usuario.Id)).Error);
    }

    // ---------------------------------------------------------------- desistir

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public async Task Desistir_DescartaLaSolicitud_Audita_YDevuelveLaPosibilidadDeComprar()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Pendientes.Cantidad = 1;
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);

        var desistido = await cancelacion.Desistir(cliente.Usuario.Id);

        Assert.True(desistido.EsExito);
        Assert.Equal(EstadoDeLaCancelacion.Descartada, desistido.Valor.Estado);
        Assert.Equal(DominioEstado.Descartada, e.Bd.Cancelaciones.Single().Estado);
        Assert.Equal(AccionesAuditables.CancelacionDiferidaDescartada, Assert.Single(e.Auditoria.Registradas).Accion);
        Assert.Equal(EstadoDeLaMarca.Libre, await e.Cancelacion().LeerConBloqueo(cliente.Usuario.Id));
        // Ya puede pedir otra.
        Assert.True((await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null)).EsExito);
    }

    [Fact]
    public async Task Desistir_SinSolicitudOConUnaEjecucionEmpezada_EsUnConflicto()
    {
        var (e, cliente, cancelacion) = Escenario();
        var sin = await cancelacion.Desistir(cliente.Usuario.Id);
        e.Bd.Cancelaciones.Add(SolicitudDeCancelacion.Restaurar(Guid.NewGuid(), cliente.Usuario.Id, DominioEstado.EsperandoReconfirmacion, e.Reloj.Ahora, e.Reloj.Ahora.AddDays(5), null, null, ejecucionEnCurso: true));

        var empezada = await cancelacion.Desistir(cliente.Usuario.Id);

        Assert.Equal("sin-cancelacion-en-curso", sin.Error!.Clave);
        Assert.Equal("ejecucion-en-curso", empezada.Error!.Clave);
        Assert.Equal(DominioEstado.EsperandoReconfirmacion, e.Bd.Cancelaciones.Single().Estado);
    }

    // ---------------------------------------------------------------- la revisión y la reconfirmación (IDE-68, IDE-71)

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public async Task AlCerrarseLosPendientes_ElTitularRecibeOtroAviso_YTieneSieteDiasParaReconfirmar()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Pendientes.Cantidad = 2;
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        e.Correo.Correos.Clear();
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddDays(3);
        e.Pendientes.Cantidad = 0;
        e.Actor.Actual = ActorActual.DelSistema();

        var revision = await e.Revision().Revisar();

        Assert.Equal(1, revision.PendientesCerrados);
        var solicitud = e.Bd.Cancelaciones.Single();
        Assert.Equal(DominioEstado.EsperandoReconfirmacion, solicitud.Estado);
        Assert.Equal(e.Reloj.Ahora.AddDays(7), solicitud.ReconfirmarHasta);
        Assert.Equal(AvisosDeCuenta.PlantillaDePendientesCerrados, Assert.Single(e.Correo.Correos).Plantilla);
        Assert.Contains(e.Bd.Avisos, a => a.Aviso.Tipo == "cancelacion-pendientes-cerrados");
    }

    [Fact]
    public async Task MientrasHayaPendientes_LaRevisionNoCambiaNada()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Pendientes.Cantidad = 2;
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        e.Correo.Correos.Clear();
        e.Actor.Actual = ActorActual.DelSistema();

        var revision = await e.Revision().Revisar();

        Assert.Equal(0, revision.PendientesCerrados);
        Assert.Equal(DominioEstado.Diferida, e.Bd.Cancelaciones.Single().Estado);
        Assert.Empty(e.Correo.Correos);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public async Task Reconfirmar_SinPendientesNuevos_EjecutaLaCancelacion_ConContrasena_YElSegundoFactorSiLoTiene()
    {
        var (e, cliente, cancelacion) = Escenario();
        var (secreto, _) = e.ConSegundoFactor(cliente);
        var codigoDeLaSolicitud = e.Totp(secreto);
        e.Pendientes.Cantidad = 1;
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, S(codigoDeLaSolicitud));
        e.Pendientes.Cantidad = 0;
        e.Actor.Actual = ActorActual.DelSistema();
        await e.Revision().Revisar();
        e.ActuarComo(cliente);

        var sinCodigo = await cancelacion.Reconfirmar(cliente.Usuario.Id, Contrasena, null);
        var sinContrasena = await cancelacion.Reconfirmar(cliente.Usuario.Id, S("incorrecta y larga de verdad"), S(e.Totp(secreto, 1)));
        var reconfirmada = await cancelacion.Reconfirmar(cliente.Usuario.Id, Contrasena, S(e.Totp(secreto, 1)));

        Assert.Equal(FallosDeIdentidad.CodigoInvalido, sinCodigo.Error);
        Assert.Equal(Reautenticacion.ContrasenaActualIncorrecta, sinContrasena.Error);
        Assert.True(reconfirmada.EsExito);
        Assert.Equal(EstadoDeLaCancelacion.Ejecutada, reconfirmada.Valor.Estado);
        Assert.Equal([cliente.Usuario.Id], e.Ejecutor.Ejecutadas);
        Assert.Equal(DominioEstado.Ejecutada, e.Bd.Cancelaciones.Single().Estado);
        Assert.Equal(EstadoDeLaMarca.Ejecutada, await e.Cancelacion().LeerConBloqueo(cliente.Usuario.Id));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public async Task Reconfirmar_SiApareceUnPendienteNuevo_NoSeEjecuta_SeInformaCuantosSon_YSigueDiferida()
    {
        var (e, cliente, cancelacion) = Escenario();
        await EsperandoReconfirmacion(e, cliente, cancelacion);
        e.Pendientes.Cantidad = 1;

        var resultado = await cancelacion.Reconfirmar(cliente.Usuario.Id, Contrasena, null);

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDeLaCancelacion.Diferida, resultado.Valor.Estado);
        Assert.Equal(1, resultado.Valor.PendientesAbiertos);
        Assert.Empty(e.Ejecutor.Ejecutadas);
        Assert.Equal(DominioEstado.Diferida, e.Bd.Cancelaciones.Single().Estado);
        Assert.Null(e.Bd.Cancelaciones.Single().ReconfirmarHasta);

        // Al cerrarse, otro aviso y el plazo de 7 días se reinicia.
        e.Reloj.Ahora = e.Reloj.Ahora.AddDays(2);
        e.Pendientes.Cantidad = 0;
        e.Correo.Correos.Clear();
        e.Actor.Actual = ActorActual.DelSistema();
        await e.Revision().Revisar();
        Assert.Equal(e.Reloj.Ahora.AddDays(7), e.Bd.Cancelaciones.Single().ReconfirmarHasta);
        Assert.Equal(AvisosDeCuenta.PlantillaDePendientesCerrados, Assert.Single(e.Correo.Correos).Plantilla);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-61")]
    public async Task ElPlazoDeSieteDias_NuncaPasaDelDia30_YPasadoElDia30LaReconfirmacionNoSeAcepta()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Pendientes.Cantidad = 1;
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        // Los pendientes se cierran el día 29: el plazo de 7 días se recorta al tope.
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddDays(29);
        e.Pendientes.Cantidad = 0;
        e.Actor.Actual = ActorActual.DelSistema();
        await e.Revision().Revisar();
        var solicitud = e.Bd.Cancelaciones.Single();
        Assert.Equal(EntornoDeIdentidad.Inicio.AddDays(30), solicitud.ReconfirmarHasta);

        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddDays(30).AddMinutes(1);
        e.ActuarComo(cliente);
        var tarde = await cancelacion.Reconfirmar(cliente.Usuario.Id, Contrasena, null);

        Assert.Equal("cancelacion-descartada", tarde.Error!.Clave);
        Assert.Equal(DominioEstado.Descartada, e.Bd.Cancelaciones.Single().Estado);
        Assert.Empty(e.Ejecutor.Ejecutadas);
        Assert.Equal(AccionesAuditables.CancelacionDiferidaDescartada, Assert.Single(e.Auditoria.Registradas).Accion);
        Assert.Contains(e.Bd.Avisos, a => a.Aviso.Tipo == "cancelacion-descartada");
        Assert.Equal(AvisosDeCuenta.PlantillaDeCancelacionDescartada, e.Correo.Correos[^1].Plantilla);
    }

    [Fact]
    public async Task Reconfirmar_PasadosLosSieteDias_SeDescarta()
    {
        var (e, cliente, cancelacion) = Escenario();
        await EsperandoReconfirmacion(e, cliente, cancelacion);
        e.Reloj.Ahora = e.Reloj.Ahora.AddDays(8);

        var resultado = await cancelacion.Reconfirmar(cliente.Usuario.Id, Contrasena, null);

        Assert.Equal("cancelacion-descartada", resultado.Error!.Clave);
        Assert.Equal(DominioEstado.Descartada, e.Bd.Cancelaciones.Single().Estado);
        Assert.Empty(e.Ejecutor.Ejecutadas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-57")]
    public async Task Reconfirmar_SiElPuertoDePendientesNoResponde_NoSeEjecutaYSigueEsperando()
    {
        var (e, cliente, cancelacion) = Escenario();
        await EsperandoReconfirmacion(e, cliente, cancelacion);
        e.Pendientes.Falla = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => cancelacion.Reconfirmar(cliente.Usuario.Id, Contrasena, null));

        Assert.Empty(e.Ejecutor.Ejecutadas);
        var solicitud = e.Bd.Cancelaciones.Single();
        Assert.Equal(DominioEstado.EsperandoReconfirmacion, solicitud.Estado);
        Assert.False(solicitud.EjecucionEnCurso);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-57")]
    public async Task Reconfirmar_SiLaEjecucionFalla_NoQuedaEfectoParcial_YSePuedeReintentar()
    {
        var (e, cliente, cancelacion) = Escenario();
        await EsperandoReconfirmacion(e, cliente, cancelacion);
        e.Ejecutor.Falla = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => cancelacion.Reconfirmar(cliente.Usuario.Id, Contrasena, null));

        Assert.Null(cliente.CanceladaEn);
        var solicitud = e.Bd.Cancelaciones.Single();
        Assert.Equal(DominioEstado.EsperandoReconfirmacion, solicitud.Estado);
        Assert.False(solicitud.EjecucionEnCurso);
        e.Ejecutor.Falla = false;
        Assert.True((await cancelacion.Reconfirmar(cliente.Usuario.Id, Contrasena, null)).EsExito);
        Assert.NotNull(cliente.CanceladaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-61")]
    public async Task SiLaEjecucionFallaYElReintentoCruzaElDia30_LaSolicitudSeDescarta()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Pendientes.Cantidad = 1;
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddDays(26);
        e.Pendientes.Cantidad = 0;
        e.Actor.Actual = ActorActual.DelSistema();
        await e.Revision().Revisar();
        e.ActuarComo(cliente);
        e.Ejecutor.Falla = true;
        // El reloj corre mientras se ejecuta: el fallo ocurre pasado el tope.
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddDays(29).AddHours(23);
        var alReintentar = e.Reloj;

        await Assert.ThrowsAsync<InvalidOperationException>(() => cancelacion.Reconfirmar(cliente.Usuario.Id, Contrasena, null));

        Assert.Equal(DominioEstado.EsperandoReconfirmacion, e.Bd.Cancelaciones.Single().Estado);
        alReintentar.Ahora = EntornoDeIdentidad.Inicio.AddDays(30).AddMinutes(5);
        var descartes = await e.Revision().Revisar();
        Assert.Equal(1, descartes.Descartadas);
        Assert.Equal(DominioEstado.Descartada, e.Bd.Cancelaciones.Single().Estado);
    }

    [Fact]
    public async Task Reconfirmar_SinSolicitudOSinEstarEsperando_EsUnConflicto()
    {
        var (e, cliente, cancelacion) = Escenario();
        var sin = await cancelacion.Reconfirmar(cliente.Usuario.Id, Contrasena, null);
        e.Pendientes.Cantidad = 1;
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);

        var diferida = await cancelacion.Reconfirmar(cliente.Usuario.Id, Contrasena, null);

        Assert.Equal("sin-cancelacion-en-curso", sin.Error!.Clave);
        Assert.Equal("sin-reconfirmacion-pendiente", diferida.Error!.Clave);
    }

    // ---------------------------------------------------------------- aviso suprimido y descarte (IDE-76, IDE-81)

    [Fact]
    [Trait("Criterio", "AC-IDE-61")]
    public async Task SiElCorreoSeSuprime_ElPlazoDeSieteDiasCuentaDesdeQueElAvisoSeMuestraEnUnaSesionReconocida()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Pendientes.Cantidad = 1;
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        e.Pendientes.Cantidad = 0;
        e.Correo.Resultado = ResultadoDeEnvio.Suprimido;
        e.Actor.Actual = ActorActual.DelSistema();
        await e.Revision().Revisar();
        e.ActuarComo(cliente);

        var solicitud = e.Bd.Cancelaciones.Single();
        Assert.Equal(DominioEstado.EsperandoReconfirmacion, solicitud.Estado);
        Assert.Null(solicitud.ReconfirmarHasta);
        // Sin plazo abierto, todavía no se puede reconfirmar.
        Assert.Equal("sin-reconfirmacion-pendiente", (await cancelacion.Reconfirmar(cliente.Usuario.Id, Contrasena, null)).Error!.Clave);

        e.Reloj.Ahora = e.Reloj.Ahora.AddDays(2);
        var desdeOtra = await cancelacion.MostrarElAviso(cliente.Usuario.Id, desdeUnaSesionReconocida: false);
        Assert.Equal("sesion-no-reconocida", desdeOtra.Error!.Clave);
        Assert.Null(e.Bd.Cancelaciones.Single().ReconfirmarHasta);

        var mostrado = await cancelacion.MostrarElAviso(cliente.Usuario.Id, desdeUnaSesionReconocida: true);
        Assert.True(mostrado.EsExito);
        Assert.True(mostrado.Valor.AvisoMostrado);
        Assert.Equal(e.Reloj.Ahora.AddDays(7), mostrado.Valor.ReconfirmarHasta);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-61")]
    public async Task ElCorreoEncoladoTampocoAbreElPlazo_HastaQueElAvisoSeMuestre()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Pendientes.Cantidad = 1;
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        e.Pendientes.Cantidad = 0;
        e.Correo.Resultado = ResultadoDeEnvio.Encolado;
        e.Actor.Actual = ActorActual.DelSistema();

        await e.Revision().Revisar();

        Assert.Null(e.Bd.Cancelaciones.Single().ReconfirmarHasta);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-61")]
    public async Task A_Los30Dias_SeDescartaLaDiferidaSinCerrar_ConElAvisoEnLaCuentaYPorCorreo_YPuedeVolverAComprar()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Pendientes.Cantidad = 1;
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        e.Correo.Correos.Clear();
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddDays(30);
        e.Actor.Actual = ActorActual.DelSistema();

        var revision = await e.Revision().Revisar();

        Assert.Equal(1, revision.Descartadas);
        var solicitud = e.Bd.Cancelaciones.Single();
        Assert.Equal(DominioEstado.Descartada, solicitud.Estado);
        Assert.Equal(e.Reloj.Ahora, solicitud.CerradaEn);
        Assert.Equal(AccionesAuditables.CancelacionDiferidaDescartada, Assert.Single(e.Auditoria.Registradas).Accion);
        Assert.Contains(e.Bd.Avisos, a => a.Aviso.Tipo == "cancelacion-descartada");
        Assert.Equal(AvisosDeCuenta.PlantillaDeCancelacionDescartada, Assert.Single(e.Correo.Correos).Plantilla);
        Assert.Equal(EstadoDeLaMarca.Libre, await e.Cancelacion().LeerConBloqueo(cliente.Usuario.Id));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-61")]
    public async Task UnaEsperandoReconfirmacionSinReconfirmar_SeDescartaAlVencerElPlazo()
    {
        var (e, cliente, cancelacion) = Escenario();
        await EsperandoReconfirmacion(e, cliente, cancelacion);
        e.Reloj.Ahora = e.Reloj.Ahora.AddDays(8);
        e.Actor.Actual = ActorActual.DelSistema();

        var revision = await e.Revision().Revisar();

        Assert.Equal(1, revision.Descartadas);
        Assert.Equal(DominioEstado.Descartada, e.Bd.Cancelaciones.Single().Estado);
    }

    [Fact]
    public async Task LaRevision_SiUnaSolicitudFalla_SigueConLasDemas_YLaCuentaALaQueFalla_SeReintentaDespues()
    {
        var e = new EntornoDeIdentidad();
        var a = e.Cuenta("a@example.com");
        var b = e.Cuenta("b@example.com");
        e.Actor.Actual = ActorActual.DelSistema();
        e.Bd.Cancelaciones.Add(SolicitudDeCancelacion.Diferir(Guid.NewGuid(), a.Usuario.Id, e.Reloj.Ahora));
        e.Bd.Cancelaciones.Add(SolicitudDeCancelacion.Diferir(Guid.NewGuid(), b.Usuario.Id, e.Reloj.Ahora));
        e.Pendientes.Falla = true;

        var revision = await e.Revision().Revisar();

        Assert.Equal(2, revision.Revisadas);
        Assert.Equal(2, revision.Fallidas);
        e.Pendientes.Falla = false;
        e.Pendientes.Cantidad = 0;
        Assert.Equal(2, (await e.Revision().Revisar()).PendientesCerrados);
    }

    [Fact]
    public async Task LaRevision_NoTocaLasCuentasYaCanceladas()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("a@example.com");
        cuenta.CanceladaEn = e.Reloj.Ahora;
        e.Actor.Actual = ActorActual.DelSistema();
        e.Bd.Cancelaciones.Add(SolicitudDeCancelacion.Diferir(Guid.NewGuid(), cuenta.Usuario.Id, e.Reloj.Ahora));

        var revision = await e.Revision().Revisar();

        Assert.Equal(0, revision.PendientesCerrados + revision.Descartadas + revision.Fallidas);
    }

    // ---------------------------------------------------------------- la marca con bloqueo (IDE-71)

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public async Task LaMarca_SeLeeConBloqueo_YDiceLibreDiferidaEnEjecucionOEjecutada()
    {
        var (e, cliente, cancelacion) = Escenario();
        var marca = e.Cancelacion();
        var bloqueosAntes = e.Cuentas.Bloqueos;

        Assert.Equal(EstadoDeLaMarca.Libre, await marca.LeerConBloqueo(cliente.Usuario.Id));
        Assert.True(e.Cuentas.Bloqueos > bloqueosAntes);

        e.Pendientes.Cantidad = 1;
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        Assert.Equal(EstadoDeLaMarca.Diferida, await marca.LeerConBloqueo(cliente.Usuario.Id));

        var abierta = e.Bd.Cancelaciones.Single();
        e.Bd.Cancelaciones.Clear();
        e.Bd.Cancelaciones.Add(SolicitudDeCancelacion.Restaurar(abierta.Id, abierta.UsuarioId, DominioEstado.EsperandoReconfirmacion, abierta.SolicitadaEn, e.Reloj.Ahora.AddDays(3), null, null, ejecucionEnCurso: true));
        Assert.Equal(EstadoDeLaMarca.EnEjecucion, await marca.LeerConBloqueo(cliente.Usuario.Id));

        cliente.CanceladaEn = e.Reloj.Ahora;
        Assert.Equal(EstadoDeLaMarca.Ejecutada, await marca.LeerConBloqueo(cliente.Usuario.Id));
        Assert.Equal(EstadoDeLaMarca.Ejecutada, await marca.LeerConBloqueo(Guid.NewGuid()));
    }

    [Fact]
    public async Task LaMarca_DeUnaCuentaDesactivada_DiceEjecutada_PorqueNoPuedeComprar()
    {
        var (e, cliente, _) = Escenario();
        cliente.Usuario.Desactivar();

        Assert.Equal(EstadoDeLaMarca.Ejecutada, await e.Cancelacion().LeerConBloqueo(cliente.Usuario.Id));
    }

    [Fact]
    public async Task SiYaHayUnaCancelacionEnCurso_ElCodigoDelSegundoFactorNoSeConsume()
    {
        var (e, cliente, cancelacion) = Escenario();
        var (secreto, _) = e.ConSegundoFactor(cliente);
        e.Pendientes.Cantidad = 1;
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, S(e.Totp(secreto)));
        var codigo = e.Totp(secreto, 1);

        var otra = await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, S(codigo));
        var desiste = await cancelacion.Desistir(cliente.Usuario.Id);
        var denuevo = await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, S(codigo));

        Assert.Equal("cancelacion-en-curso", otra.Error!.Clave);
        Assert.True(desiste.EsExito);
        Assert.True(denuevo.EsExito);
    }

    [Fact]
    public async Task LaRevision_NoEnviaCorreoALasCuentasDesactivadas()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Pendientes.Cantidad = 1;
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        cliente.Usuario.Desactivar();
        e.Correo.Correos.Clear();
        e.Pendientes.Cantidad = 0;
        e.Actor.Actual = ActorActual.DelSistema();

        await e.Revision().Revisar();

        Assert.Empty(e.Correo.Correos);
    }
}
