using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// El restablecimiento del segundo factor de la única administración desde la consola (identidad-recuperacion-del-segundo-factor T03 a T05; AC-RSF-01 a AC-RSF-08, AC-RSF-10): solo el actor «sistema» y en los entornos desplegados, solo cuando ninguna otra administración puede
/// ingresar; borra el segundo factor, anula los ingresos pendientes, cierra las sesiones y audita en la misma transacción; el aviso va después y nunca deshace nada; y el ingreso no crea su paso siguiente si las sesiones se cerraron mientras comprobaba la contraseña.
/// </summary>
public sealed class RestablecimientoDesdeLaInstalacionTests
{
    private const string Correo = "admin@example.com";

    private static ValorSecreto S(string texto) => new(texto);

    private static (EntornoDeIdentidad E, CuentaDeIdentidad Admin) Escenario(bool conSegundoFactor = true)
    {
        var e = new EntornoDeIdentidad();
        var admin = e.Cuenta(Correo, RolDeUsuario.AdministradorCentral);
        if (conSegundoFactor)
        {
            e.ConSegundoFactor(admin);
        }

        e.Actor.Actual = ActorActual.DelSistema();
        return (e, admin);
    }

    private static Task<RestablecimientoDesdeLaInstalacion> Restablecer(EntornoDeIdentidad e, string? correo = Correo, string entorno = "Production", string? modo = null, IUnidadDeTrabajo? unidad = null) =>
        e.Restablecimiento(unidad).Restablecer(entorno, modo, correo);

    private static async Task SinCambios(EntornoDeIdentidad e, CuentaDeIdentidad admin)
    {
        Assert.NotNull(await e.SegundoFactor.Obtener(admin.Usuario.Id));
        Assert.Empty(e.Auditoria.Registradas);
        Assert.Empty(e.Correo.Correos);
        Assert.All(e.Bd.Renovaciones, r => Assert.Null(r.RevocadaEn));
        Assert.All(e.Bd.Desafios, d => Assert.False(d.Desafio.Usado));
    }

    // ---------- Aplicar ----------

    [Fact]
    [Trait("Criterio", "AC-RSF-01")]
    public async Task LaUnicaAdministracion_PierdeSoloSuSegundoFactor_SusSesionesSeCierran_YAlIngresarLoConfigura()
    {
        var (e, admin) = Escenario(conSegundoFactor: false);
        var (_, respaldoViejo) = e.ConSegundoFactor(admin);
        var cliente = e.Cuenta("cliente@example.com");
        e.ConSegundoFactor(cliente);
        var sesion = await e.Sesiones.Nueva(admin.Usuario, e.Reloj.Ahora, default);
        var delCliente = await e.Sesiones.Nueva(cliente.Usuario, e.Reloj.Ahora, default);
        var aMedias = (await e.Ingreso().Ingresar(new SolicitudDeIngreso(Correo, S(EntornoDeIdentidad.ContrasenaSegura)))).Valor.Desafio!;
        var (resumen, correo, rol, activa) = (admin.ResumenDeAcceso, admin.Usuario.Correo, admin.Usuario.Rol, admin.Usuario.Activo);
        e.Reloj.Ahora = e.Reloj.Ahora.AddSeconds(30);

        var resultado = await Restablecer(e, "  Admin@Example.com ");

        Assert.Equal(ResultadoDelRestablecimientoDesdeLaInstalacion.Restablecido, resultado.Resultado);
        Assert.True(resultado.AvisoEnviado);
        Assert.Null(await e.SegundoFactor.Obtener(admin.Usuario.Id));
        Assert.Equal((resumen, correo, rol, activa), (admin.ResumenDeAcceso, admin.Usuario.Correo, admin.Usuario.Rol, admin.Usuario.Activo));
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, (await e.Gestor().Renovar(sesion.Renovacion)).Error);
        Assert.NotNull(await e.SegundoFactor.Obtener(cliente.Usuario.Id));
        Assert.True((await e.Gestor().Renovar(delCliente.Renovacion)).EsExito);

        // Al ingresar debe configurar el segundo factor antes de recibir una credencial, y recibe 8 códigos nuevos (IDE-21).
        var siguiente = (await e.Ingreso().Ingresar(new SolicitudDeIngreso(Correo, S(EntornoDeIdentidad.ContrasenaSegura)))).Valor;
        Assert.Null(siguiente.Credencial);
        Assert.Equal(PasoPendienteDelIngreso.ConfigurarSegundoFactor, siguiente.Desafio!.Paso);
        var secreto = (await e.Ingreso().IniciarConfiguracionDelSegundoFactor(siguiente.Desafio.Identificador)).Valor;
        var confirmado = (await e.Ingreso().ConfirmarElSegundoFactor(siguiente.Desafio.Identificador, S(e.Totp(Base32DePrueba(secreto.SecretoBase32.Revelar()))))).Valor;
        Assert.NotNull(confirmado.Credencial);
        Assert.Equal(8, confirmado.CodigosDeRespaldo.Count);
        Assert.DoesNotContain(confirmado.CodigosDeRespaldo, c => respaldoViejo.Contains(c.Revelar()));

        // El ingreso empezado antes no se completa, tampoco con el segundo factor nuevo.
        var conElNuevo = await e.Ingreso().PresentarSegundoFactor(aMedias.Identificador, S(e.Totp(Base32DePrueba(secreto.SecretoBase32.Revelar()), pasos: 1)));
        Assert.Equal(FallosDeIdentidad.DesafioInvalido, conElNuevo.Error);
    }

    [Theory]
    [Trait("Criterio", "AC-RSF-01")]
    [InlineData(ModoDelDesafio.PresentarSegundoFactor)]
    [InlineData(ModoDelDesafio.ConfigurarSegundoFactor)]
    [InlineData(ModoDelDesafio.CambiarContrasenaTemporal)]
    public async Task UnIngresoPendiente_EnCualquierPaso_QuedaSinEfecto(ModoDelDesafio modo)
    {
        var (e, admin) = Escenario();
        var pendiente = DesafioDeIngreso.Crear(Guid.NewGuid(), admin.Usuario.Id, modo, e.Reloj.Ahora);
        var ajeno = DesafioDeIngreso.Crear(Guid.NewGuid(), e.Cuenta("cliente@example.com").Usuario.Id, modo, e.Reloj.Ahora);
        await e.Desafios.Guardar(pendiente, "huella-del-pendiente");
        await e.Desafios.Guardar(ajeno, "huella-del-ajeno");

        await Restablecer(e);

        Assert.True((await e.Desafios.PorHuella("huella-del-pendiente"))!.Usado);
        Assert.False((await e.Desafios.PorHuella("huella-del-ajeno"))!.Usado);
    }

    // ---------- Quién y dónde ----------

    [Theory]
    [Trait("Criterio", "AC-RSF-03")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnNombreDeUnaPersona_ConOSinSesion_SeRechaza_YQuedaLaDenegacion(bool conSesion)
    {
        var (e, admin) = Escenario();
        e.Actor.Actual = conSesion ? ActorActual.DeUsuario(admin.Usuario.Id, "administrador-central") : ActorActual.Anonimo();

        var resultado = await Restablecer(e);

        Assert.Equal(ResultadoDelRestablecimientoDesdeLaInstalacion.NoPermitido, resultado.Resultado);
        Assert.Equal("fuera-de-rol", Assert.Single(e.Denegaciones.Registradas).Motivo);
        await SinCambios(e, admin);
    }

    [Theory]
    [Trait("Criterio", "AC-RSF-02")]
    [InlineData("Development", null)]
    [InlineData("Test", null)]
    [InlineData("Production", "Ensayo")]
    [InlineData("Production", "Restauracion")]
    public async Task FueraDeLosEntornosDesplegados_NoSeTocaNingunDato_NiSiquieraLaDenegacionDeUnaPersona(string entorno, string? modo)
    {
        var (e, admin) = Escenario();
        e.Actor.Actual = ActorActual.Anonimo();

        var resultado = await Restablecer(e, entorno: entorno, modo: modo);

        Assert.Equal(ResultadoDelRestablecimientoDesdeLaInstalacion.EntornoNoPermitido, resultado.Resultado);
        Assert.Empty(e.Denegaciones.Registradas);
        Assert.Equal(0, e.Unidad.Transacciones);
        await SinCambios(e, admin);
    }

    // ---------- Rechazos ----------

    [Theory]
    [Trait("Criterio", "AC-RSF-04")]
    [InlineData(null, ResultadoDelRestablecimientoDesdeLaInstalacion.CorreoInvalido)]
    [InlineData("", ResultadoDelRestablecimientoDesdeLaInstalacion.CorreoInvalido)]
    [InlineData("sin-arroba", ResultadoDelRestablecimientoDesdeLaInstalacion.CorreoInvalido)]
    [InlineData("nadie@example.com", ResultadoDelRestablecimientoDesdeLaInstalacion.NoEsUnaAdministracionActiva)]
    [InlineData("cliente@example.com", ResultadoDelRestablecimientoDesdeLaInstalacion.NoEsUnaAdministracionActiva)]
    [InlineData("encargado@example.com", ResultadoDelRestablecimientoDesdeLaInstalacion.NoEsUnaAdministracionActiva)]
    [InlineData("logistica@example.com", ResultadoDelRestablecimientoDesdeLaInstalacion.NoEsUnaAdministracionActiva)]
    [InlineData("desactivada@example.com", ResultadoDelRestablecimientoDesdeLaInstalacion.NoEsUnaAdministracionActiva)]
    [InlineData("cancelada@example.com", ResultadoDelRestablecimientoDesdeLaInstalacion.NoEsUnaAdministracionActiva)]
    public async Task UnCorreoQueNoEsDeUnaAdministracionActiva_SeRechazaSinCambios(string? correo, ResultadoDelRestablecimientoDesdeLaInstalacion esperado)
    {
        var (e, admin) = Escenario();
        e.ConSegundoFactor(e.Cuenta("cliente@example.com"));
        e.ConSegundoFactor(e.Cuenta("encargado@example.com", RolDeUsuario.EncargadoDeSucursal));
        e.ConSegundoFactor(e.Cuenta("logistica@example.com", RolDeUsuario.Logistica));
        e.ConSegundoFactor(e.Cuenta("desactivada@example.com", RolDeUsuario.AdministradorCentral, activa: false));
        e.Cuenta("cancelada@example.com", RolDeUsuario.AdministradorCentral).CanceladaEn = e.Reloj.Ahora.AddDays(-1);

        var resultado = await Restablecer(e, correo);

        Assert.Equal(esperado, resultado.Resultado);
        await SinCambios(e, admin);
    }

    [Theory]
    [Trait("Criterio", "AC-RSF-05")]
    [InlineData("definitiva")]
    [InlineData("temporal-vigente")]
    [InlineData("sin-segundo-factor")]
    [InlineData("textos-por-aceptar")]
    public async Task ConOtraAdministracionQuePuedeIngresar_SeRechazaSinCambios(string caso)
    {
        var (e, admin) = Escenario();
        var otra = e.Cuenta("otra@example.com", RolDeUsuario.AdministradorCentral);
        if (caso == "temporal-vigente")
        {
            otra.DebeCambiarLaTemporal = true;
            otra.ContrasenaTemporalHasta = e.Reloj.Ahora.AddHours(1);
        }
        else if (caso == "textos-por-aceptar")
        {
            otra.Aceptacion = AceptacionDeTextos.Vacia();
        }
        else if (caso == "definitiva")
        {
            e.ConSegundoFactor(otra);
        }

        var resultado = await Restablecer(e);

        Assert.Equal(ResultadoDelRestablecimientoDesdeLaInstalacion.HayOtraAdministracion, resultado.Resultado);
        await SinCambios(e, admin);
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-05")]
    public async Task ConLasDemasDesactivadasCanceladasOConLaTemporalVencida_SeRestablece()
    {
        var (e, admin) = Escenario();
        e.Cuenta("desactivada@example.com", RolDeUsuario.AdministradorCentral, activa: false);
        e.Cuenta("cancelada@example.com", RolDeUsuario.AdministradorCentral).CanceladaEn = e.Reloj.Ahora.AddDays(-1);
        var vencida = e.Cuenta("vencida@example.com", RolDeUsuario.AdministradorCentral);
        vencida.DebeCambiarLaTemporal = true;
        vencida.ContrasenaTemporalHasta = e.Reloj.Ahora;

        var resultado = await Restablecer(e);

        Assert.Equal(ResultadoDelRestablecimientoDesdeLaInstalacion.Restablecido, resultado.Resultado);
        Assert.Null(await e.SegundoFactor.Obtener(admin.Usuario.Id));
    }

    [Theory]
    [Trait("Criterio", "AC-RSF-06")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SinUnSegundoFactorActivado_NadaCambia_NiSeAvisaNiSeAudita(bool aMedioConfigurar)
    {
        var (e, admin) = Escenario(conSegundoFactor: false);
        if (aMedioConfigurar)
        {
            await e.SegundoFactor.GuardarSinConfirmar(admin.Usuario.Id, [1, 2, 3]);
        }

        await e.Sesiones.Nueva(admin.Usuario, e.Reloj.Ahora, default);

        var resultado = await Restablecer(e);

        Assert.Equal(ResultadoDelRestablecimientoDesdeLaInstalacion.SinSegundoFactor, resultado.Resultado);
        Assert.Equal(aMedioConfigurar, await e.SegundoFactor.Obtener(admin.Usuario.Id) is not null);
        Assert.Empty(e.Auditoria.Registradas);
        Assert.Empty(e.Correo.Correos);
        Assert.All(e.Bd.Renovaciones, r => Assert.Null(r.RevocadaEn));
    }

    // ---------- Auditoría y fallas ----------

    [Fact]
    [Trait("Criterio", "AC-RSF-07")]
    public async Task SeAudita_UnaSolaVez_SinSecretosNiCorreo()
    {
        var (e, admin) = Escenario();

        await Restablecer(e);

        var registro = Assert.Single(e.Auditoria.Registradas);
        Assert.Equal(AccionesAuditables.SegundoFactorRestablecidoPorAdministracion, registro.Accion);
        Assert.Equal(("usuario", admin.Usuario.Id.ToString("D")), (registro.ObjetoTipo, registro.ObjetoId));
        Assert.Null(registro.Motivo);
        Assert.Empty(registro.Detalle);
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-07")]
    public async Task SiNoSePuedeAuditar_LaOperacionFalla_NoRevocaNiAvisa()
    {
        var (e, _) = Escenario();
        var admin = e.Bd.Cuentas.Values.Single();
        await e.Sesiones.Nueva(admin.Usuario, e.Reloj.Ahora, default);
        e.Auditoria.Falla = true;

        await Assert.ThrowsAnyAsync<Exception>(() => Restablecer(e));

        Assert.All(e.Bd.Renovaciones, r => Assert.Null(r.RevocadaEn));
        Assert.Empty(e.Correo.Correos);
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-11")]
    public async Task UnChoqueAlConfirmar_SubeSinAvisar()
    {
        var (e, _) = Escenario();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Restablecer(e, unidad: new UnidadQueNoConfirma(e.Unidad)));

        Assert.Empty(e.Correo.Correos);
    }

    // ---------- El aviso ----------

    [Fact]
    [Trait("Criterio", "AC-RSF-08")]
    public async Task ElAviso_SaleAlCorreoDeLaCuenta_ConLaPlantillaDelRestablecimiento()
    {
        var (e, _) = Escenario();

        var resultado = await Restablecer(e);

        Assert.True(resultado.AvisoEnviado);
        var aviso = Assert.Single(e.Correo.Correos);
        Assert.Equal((AvisosDeCuenta.PlantillaDeSegundoFactorRestablecido, Correo, CategoriaDeCorreo.AlertaCriticaDeCuenta), (aviso.Plantilla, aviso.Destinatario, aviso.Categoria));
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-08")]
    public void ElTextoDelAviso_DiceQueHacer_SinEnlacesNiAtribuirloAOtraPersona()
    {
        var mensaje = new RenderizadorDeCorreoDeProduccion().De(new CorreoSaliente(CategoriaDeCorreo.AlertaCriticaDeCuenta, Correo, AvisosDeCuenta.PlantillaDeSegundoFactorRestablecido));

        Assert.Contains("verificación en dos pasos", mensaje.Cuerpo, StringComparison.Ordinal);
        Assert.Contains("configurarla de nuevo la próxima vez que ingreses", mensaje.Cuerpo, StringComparison.Ordinal);
        Assert.Contains("Si no pediste este cambio, ingresa cuanto antes, cambia tu contraseña y avisa a la administración de BabelHub o a quien opera la instalación", mensaje.Cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("La administración restableció", mensaje.Cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("http", mensaje.Cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Correo, mensaje.Cuerpo, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-RSF-08")]
    [InlineData(ResultadoDeEnvio.Encolado)]
    [InlineData(ResultadoDeEnvio.Suprimido)]
    public async Task SiElAvisoNoSaleAhora_ElRestablecimientoSeMantiene(ResultadoDeEnvio envio)
    {
        var (e, admin) = Escenario();
        e.Correo.Resultado = envio;

        var resultado = await Restablecer(e);

        Assert.Equal((ResultadoDelRestablecimientoDesdeLaInstalacion.Restablecido, false), (resultado.Resultado, resultado.AvisoEnviado));
        Assert.Null(await e.SegundoFactor.Obtener(admin.Usuario.Id));
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-08")]
    public async Task SiElEnvioFalla_ElRestablecimientoSeMantiene()
    {
        var (e, admin) = Escenario();
        e.Correo.Falla = true;

        var resultado = await Restablecer(e);

        Assert.Equal((ResultadoDelRestablecimientoDesdeLaInstalacion.Restablecido, false), (resultado.Resultado, resultado.AvisoEnviado));
        Assert.Null(await e.SegundoFactor.Obtener(admin.Usuario.Id));
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-08")]
    public async Task UnaSegundaAlertaEn24Horas_NoSalePorCorreo_YQuedaElEventoYElAvisoEnLaCuenta()
    {
        var (e, admin) = Escenario();
        await Restablecer(e);
        e.ConSegundoFactor(admin);

        var segunda = await Restablecer(e);

        Assert.Equal((ResultadoDelRestablecimientoDesdeLaInstalacion.Restablecido, false), (segunda.Resultado, segunda.AvisoEnviado));
        Assert.Single(e.Correo.Correos);
        Assert.NotEmpty(e.Seguridad.Eventos);
        Assert.Contains(e.Bd.Avisos, a => a.UsuarioId == admin.Usuario.Id && a.Aviso.Tipo == AvisosDeCuenta.PlantillaDeSegundoFactorRestablecido);
    }

    // ---------- El ingreso no sobrevive a un restablecimiento simultáneo (D07) ----------

    [Theory]
    [Trait("Criterio", "AC-RSF-10")]
    [InlineData(RolDeUsuario.AdministradorCentral)]
    [InlineData(RolDeUsuario.Cliente)]
    public async Task SiLasSesionesSeCierranMientrasSeCompruebaLaContrasena_ElIngresoNoCreaSuPasoNiSuSesion(RolDeUsuario rol)
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("persona@example.com", rol);
        if (rol == RolDeUsuario.AdministradorCentral)
        {
            e.ConSegundoFactor(cuenta);
        }

        var simultanea = new UnidadConCambioPrevio(e.Unidad, () =>
        {
            e.Reloj.Ahora = e.Reloj.Ahora.AddSeconds(1);
            cuenta.Usuario.AvanzarElCorteDeSesiones(e.Reloj.Ahora);
        });

        var resultado = await e.Ingreso(simultanea).Ingresar(new SolicitudDeIngreso("persona@example.com", S(EntornoDeIdentidad.ContrasenaSegura)));

        Assert.Equal(FallosDeIdentidad.CredencialesInvalidas, resultado.Error);
        Assert.Empty(e.Bd.Desafios);
        Assert.Empty(e.Bd.Renovaciones);
        Assert.NotEmpty(e.Seguridad.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-40")]
    public async Task SiLaContrasenaCambiaMientrasSeCompruebaLaAnterior_ElIngresoNoContinua()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("persona@example.com", RolDeUsuario.Cliente);
        // El titular cambia su contraseña conservando su sesión: el corte de sesiones no avanza (IDE-44).
        var simultanea = new UnidadConCambioPrevio(e.Unidad, () => cuenta.ResumenDeAcceso = "resumen-de-otra-contrasena");

        var resultado = await e.Ingreso(simultanea).Ingresar(new SolicitudDeIngreso("persona@example.com", S(EntornoDeIdentidad.ContrasenaSegura)));

        Assert.Equal(FallosDeIdentidad.CredencialesInvalidas, resultado.Error);
        Assert.Empty(e.Bd.Desafios);
        Assert.Empty(e.Bd.Renovaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-10")]
    public async Task UnDesafioAnuladoMientrasSeConfigura_NoGuardaUnSecretoSinConfirmar()
    {
        var (e, admin) = Escenario(conSegundoFactor: false);
        var desafio = (await e.Ingreso().Ingresar(new SolicitudDeIngreso(Correo, S(EntornoDeIdentidad.ContrasenaSegura)))).Valor.Desafio!;
        var simultanea = new UnidadConCambioPrevio(e.Unidad, () => e.Desafios.AnularLosDelUsuario(admin.Usuario.Id).GetAwaiter().GetResult());

        var resultado = await e.Ingreso(simultanea).IniciarConfiguracionDelSegundoFactor(desafio.Identificador);

        Assert.Equal(FallosDeIdentidad.DesafioInvalido, resultado.Error);
        Assert.Null(await e.SegundoFactor.Obtener(admin.Usuario.Id));
    }

    private static byte[] Base32DePrueba(string base32)
    {
        const string alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in base32)
        {
            buffer = (buffer << 5) | alfabeto.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        return [.. bytes];
    }

    /// <summary>Antes de cada transacción ocurre algo en otra conexión (una orden de consola que ya confirmó).</summary>
    private sealed class UnidadConCambioPrevio(IUnidadDeTrabajo interior, Action antes) : IUnidadDeTrabajo
    {
        public Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default)
        {
            antes();
            return interior.Ejecutar(trabajo, cancelacion);
        }

        public Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default) => interior.EjecutarComo(esquema, bloque, cancelacion);
    }

    /// <summary>La base aborta la transacción al confirmarla (un interbloqueo o una conexión perdida).</summary>
    private sealed class UnidadQueNoConfirma(IUnidadDeTrabajo interior) : IUnidadDeTrabajo
    {
        public Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default) =>
            interior.Ejecutar<T>(
                async ct =>
                {
                    await trabajo(ct);
                    throw new InvalidOperationException("La base abortó la transacción.");
                },
                cancelacion);

        public Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default) => interior.EjecutarComo(esquema, bloque, cancelacion);
    }
}
