using System.Security.Cryptography;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Domain.Identidad;
using BabelHub.Ecommerce.Infrastructure.Identidad;
using DominioProposito = BabelHub.Ecommerce.Domain.Identidad.PropositoDeCodigo;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// Los repositorios de PostgreSQL de Identidad (identidad-seguridad T33; AC-IDE-17, AC-IDE-20, AC-IDE-38; IDE-19, IDE-22, IDE-41): PostgreSQL real, el rol de acceso <c>NOINHERIT</c> y <c>EjecutarComo</c>; las actualizaciones
/// condicionales atómicas con solicitudes simultáneas (paso TOTP, códigos de respaldo, intentos y consumo de códigos de un solo uso, rotación de renovaciones, revocación) y las reglas que respalda la base.
/// </summary>
public sealed class RepositoriosDeIdentidadTests : IClassFixture<PostgresDeIdentidadFixture>
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private const string Resumen = "$argon2id$v=19$m=19456,t=2,p=1$c2FsLXNhbC1zYWwtc2Fs$c2FsLXNhbC1zYWwtc2Fsc2FsLXNhbC1zYWwtc2Fsc2E";

    private readonly PostgresDeIdentidadFixture _bd;
    private readonly PostgresRepositorioDeCuentas _cuentas;

    public RepositoriosDeIdentidadTests(PostgresDeIdentidadFixture bd)
    {
        _bd = bd;
        _cuentas = new PostgresRepositorioDeCuentas(bd.AccesoAlEsquema);
    }

    private static string Correo() => $"p{Guid.NewGuid():N}@example.com";

    private static CuentaDeIdentidad Cuenta(string? correo = null, RolDeUsuario rol = RolDeUsuario.Cliente, Guid? sucursal = null, bool activo = true) => new()
    {
        Usuario = Usuario.Restaurar(Guid.NewGuid(), correo ?? Correo(), rol, rol == RolDeUsuario.EncargadoDeSucursal ? sucursal ?? Guid.NewGuid() : null, true, activo),
        Nombre = "Ana Pérez",
        ResumenDeAcceso = Resumen,
        Aceptacion = AceptacionDeTextos.Restaurar(Ahora, [(TextoLegal.Terminos, "t1", Ahora), (TextoLegal.Privacidad, "p1", Ahora)]),
        CreadaEn = Ahora,
    };

    private async Task<CuentaDeIdentidad> Insertada(string? correo = null, RolDeUsuario rol = RolDeUsuario.Cliente, Guid? sucursal = null, bool activo = true)
    {
        var cuenta = Cuenta(correo, rol, sucursal, activo);
        Assert.True(await _cuentas.Insertar(cuenta));
        return cuenta;
    }

    // ---------------------------------------------------------------- cuentas

    [Fact]
    [Trait("Criterio", "AC-IDE-17")]
    public async Task UnaCuenta_SeGuardaYSeLeeCompleta_PorCorreoYPorIdentificador()
    {
        var cuenta = Cuenta();
        cuenta.ContrasenaTemporalHasta = Ahora.AddDays(1);
        cuenta.DebeCambiarLaTemporal = true;
        cuenta.Whatsapp.GuardarNumero("+51999111222", Ahora);
        cuenta.Usuario.AvanzarElCorteDeSesiones(Ahora.AddMinutes(-5));
        Assert.True(await _cuentas.Insertar(cuenta));

        var porId = await _cuentas.PorId(cuenta.Usuario.Id);
        var porCorreo = await _cuentas.PorCorreo(cuenta.Usuario.Correo);

        Assert.NotNull(porId);
        Assert.Equal(cuenta.Usuario.Id, porCorreo!.Usuario.Id);
        Assert.Equal("Ana Pérez", porId.Nombre);
        Assert.Equal(Resumen, porId.ResumenDeAcceso);
        Assert.Equal(RolDeUsuario.Cliente, porId.Usuario.Rol);
        Assert.True(porId.Usuario.CorreoVerificado);
        Assert.Equal(Ahora.AddMinutes(-5), porId.Usuario.SesionesValidasDesde);
        Assert.Equal(Ahora.AddDays(1), porId.ContrasenaTemporalHasta);
        Assert.True(porId.DebeCambiarLaTemporal);
        Assert.Equal("t1", porId.Aceptacion.VersionAceptada(TextoLegal.Terminos));
        Assert.Null(porId.Aceptacion.VersionAceptada(TextoLegal.Reembolso));
        Assert.Equal(Ahora, porId.Aceptacion.MayorDeEdadDeclaradaEn);
        Assert.Equal("+51999111222", porId.Whatsapp.Numero!.Value.Valor);
        Assert.False(porId.Whatsapp.Consentido);
        Assert.Null(await _cuentas.PorId(Guid.NewGuid()));
        Assert.Null(await _cuentas.PorCorreo("nadie@example.com"));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-17")]
    public async Task DosRegistrosSimultaneosDelMismoCorreo_DejanUnaSolaCuenta()
    {
        var correo = Correo();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => _cuentas.Insertar(Cuenta(correo)))));

        Assert.Equal(1, resultados.Count(r => r));
        Assert.Equal(1, await _bd.Consultar<long>($"SELECT count(*) FROM identidad.usuarios WHERE correo = '{correo}'"));
    }

    [Fact]
    public async Task GuardarActualiza_LoMutable_YElCorreoSeGuardaNormalizado()
    {
        var cuenta = await Insertada();
        cuenta.Nombre = "Otro Nombre";
        cuenta.ResumenDeAcceso = null;
        cuenta.CanceladaEn = Ahora;
        cuenta.Whatsapp.GuardarNumero("+51988777666", Ahora);
        cuenta.Whatsapp.Consentir("+51988777666", Ahora);
        cuenta.Usuario.AvanzarElCorteDeSesiones(Ahora);

        await _cuentas.Guardar(cuenta);
        var leida = (await _cuentas.PorId(cuenta.Usuario.Id))!;

        Assert.Equal("Otro Nombre", leida.Nombre);
        Assert.Null(leida.ResumenDeAcceso);
        Assert.Equal(Ahora, leida.CanceladaEn);
        Assert.True(leida.Whatsapp.Consentido);
        Assert.Equal(Ahora, leida.Usuario.SesionesValidasDesde);
        Assert.Equal((await _cuentas.PorCorreo(cuenta.Usuario.Correo.ToUpperInvariant()))!.Usuario.Id, cuenta.Usuario.Id);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-20")]
    public async Task ElRecuentoDeAdministradores_TomaElBloqueo_YDosDesactivacionesNoDejanElSistemaSinAdministrador()
    {
        // Esquema limpio de administradores para esta prueba: se cuentan y se compara con lo que la base dice.
        await _bd.Ejecutar("DELETE FROM identidad.usuarios WHERE rol = 'administrador-central'");
        var uno = await Insertada(rol: RolDeUsuario.AdministradorCentral);
        var dos = await Insertada(rol: RolDeUsuario.AdministradorCentral);

        async Task<bool> Desactivar(CuentaDeIdentidad cuenta) =>
            await _bd.Unidad.Ejecutar(
                async ct =>
                {
                    var activos = await _cuentas.ContarAdministradoresActivos(ct);
                    if (activos <= 1)
                    {
                        return false;
                    }

                    await _bd.Ejecutar("SELECT pg_sleep(0.2)");
                    await _bd.AccesoAlEsquema.Ejecutar(
                        async (c, t, tk) =>
                        {
                            await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, "UPDATE identidad.usuarios SET activo = false WHERE id = @id", ("id", cuenta.Usuario.Id));
                            await comando.ExecuteNonQueryAsync(tk);
                            return 0;
                        },
                        ct);
                    return true;
                },
                CancellationToken.None);

        var resultados = await Task.WhenAll(Task.Run(() => Desactivar(uno)), Task.Run(() => Desactivar(dos)));

        Assert.Equal(1, resultados.Count(r => r));
        Assert.Equal(1, await _bd.Consultar<long>("SELECT count(*) FROM identidad.usuarios WHERE rol = 'administrador-central' AND activo"));
    }

    [Fact]
    public async Task LosDestinatariosYElEncargadoActivo_SeConsultanPorRolYSucursal_YUnaCancelada_NoCuenta()
    {
        var sucursal = Guid.NewGuid();
        var encargado = await Insertada(rol: RolDeUsuario.EncargadoDeSucursal, sucursal: sucursal);
        var logistica = await Insertada(rol: RolDeUsuario.Logistica);
        var cancelada = Cuenta(rol: RolDeUsuario.Logistica);
        cancelada.CanceladaEn = Ahora;
        await _cuentas.Insertar(cancelada);

        var ids = await _cuentas.IdsActivosConRol([RolDeUsuario.Logistica]);
        var activo = await _cuentas.EncargadoActivoDe(sucursal);

        Assert.Contains(logistica.Usuario.Id, ids);
        Assert.DoesNotContain(cancelada.Usuario.Id, ids);
        Assert.DoesNotContain(encargado.Usuario.Id, ids);
        Assert.Equal(encargado.Usuario.Id, activo!.Value.UsuarioId);
        Assert.Null(await _cuentas.EncargadoActivoDe(Guid.NewGuid()));
    }

    [Fact]
    public async Task EliminarUnaCuenta_SeLlevaSusFilasDependientes_SinDejarHuerfanas()
    {
        var cuenta = await Insertada();
        var id = cuenta.Usuario.Id;
        await new PostgresAvisosDeCuenta(_bd.AccesoAlEsquema).Agregar(id, "contrasena-cambiada", Ahora);
        await new PostgresRepositorioDeDispositivos(_bd.AccesoAlEsquema).Guardar(Dispositivo.Emitir(id, new string('a', 64), "pe", Ahora));

        await _cuentas.Eliminar(id);

        Assert.Null(await _cuentas.PorId(id));
        Assert.Equal(0, await _bd.Consultar<long>($"SELECT count(*) FROM identidad.avisos_de_cuenta WHERE usuario_id = '{id}'"));
        Assert.Equal(0, await _bd.Consultar<long>($"SELECT count(*) FROM identidad.dispositivos WHERE usuario_id = '{id}'"));
    }

    [Fact]
    public async Task LasVersionesDeLosTextos_SeLeen_YSinSembrarFallaClaro()
    {
        var versiones = new PostgresVersionesDeTextosVigentes(_bd.AccesoAlEsquema);
        await _bd.Ejecutar("DELETE FROM identidad.versiones_de_textos");
        await Assert.ThrowsAsync<InvalidOperationException>(() => versiones.Obtener());

        await _bd.Ejecutar("INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso) VALUES (1, 't2', 'p2', 'r2')");

        Assert.Equal(new VersionesDeTextos("t2", "p2", "r2"), await versiones.Obtener());
    }

    // ---------------------------------------------------------------- códigos de un solo uso

    private static readonly string Huella = new('a', 64);
    private static readonly string OtraHuella = new('b', 64);

    [Fact]
    [Trait("Criterio", "AC-IDE-17")]
    public async Task UnCodigoSeAceptaUnaSolaVez_YUnCodigoNuevoInvalidaElAnterior()
    {
        var codigos = new PostgresCodigosDeUnSoloUso(_bd.AccesoAlEsquema);
        var cuenta = await Insertada();
        var id = cuenta.Usuario.Id;
        await codigos.Emitir(CodigoDeUnSoloUso.Emitir(id, DominioProposito.RecuperacionDeAcceso, Huella, Ahora));
        await codigos.Emitir(CodigoDeUnSoloUso.Emitir(id, DominioProposito.RecuperacionDeAcceso, OtraHuella, Ahora));

        Assert.Equal(ResultadoDelCodigo.Incorrecto, await codigos.Verificar(id, DominioProposito.RecuperacionDeAcceso, Huella, Ahora.AddMinutes(1)));
        Assert.Equal(ResultadoDelCodigo.Correcto, await codigos.Verificar(id, DominioProposito.RecuperacionDeAcceso, OtraHuella, Ahora.AddMinutes(1)));
        Assert.Equal(ResultadoDelCodigo.NoVigente, await codigos.Verificar(id, DominioProposito.RecuperacionDeAcceso, OtraHuella, Ahora.AddMinutes(1)));
        Assert.Equal(ResultadoDelCodigo.NoVigente, await codigos.Verificar(id, DominioProposito.VerificacionDeCorreo, OtraHuella, Ahora.AddMinutes(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-17")]
    public async Task CuarentaSolicitudesSimultaneasConElMismoCodigoCorrecto_AceptanSoloUna()
    {
        var codigos = new PostgresCodigosDeUnSoloUso(_bd.AccesoAlEsquema);
        var id = (await Insertada()).Usuario.Id;
        await codigos.Emitir(CodigoDeUnSoloUso.Emitir(id, DominioProposito.VerificacionDeCorreo, Huella, Ahora));

        var resultados = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => codigos.Verificar(id, DominioProposito.VerificacionDeCorreo, Huella, Ahora.AddMinutes(1)))));

        Assert.Equal(1, resultados.Count(r => r == ResultadoDelCodigo.Correcto));
        Assert.Equal(39, resultados.Count(r => r == ResultadoDelCodigo.NoVigente));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-17")]
    public async Task CuarentaIntentosIncorrectosSimultaneos_NoSuperanLos5Intentos_YElCodigoCorrectoYaNoSirve()
    {
        var codigos = new PostgresCodigosDeUnSoloUso(_bd.AccesoAlEsquema);
        var id = (await Insertada()).Usuario.Id;
        await codigos.Emitir(CodigoDeUnSoloUso.Emitir(id, DominioProposito.RecuperacionDeAcceso, Huella, Ahora));

        var resultados = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => codigos.Verificar(id, DominioProposito.RecuperacionDeAcceso, OtraHuella, Ahora.AddMinutes(1)))));

        Assert.Equal(5, resultados.Count(r => r == ResultadoDelCodigo.Incorrecto));
        Assert.Equal(35, resultados.Count(r => r == ResultadoDelCodigo.IntentosAgotados));
        Assert.Equal(5, await _bd.Consultar<int>($"SELECT intentos FROM identidad.codigos_de_un_solo_uso WHERE usuario_id = '{id}' AND NOT invalidado"));
        Assert.Equal(ResultadoDelCodigo.IntentosAgotados, await codigos.Verificar(id, DominioProposito.RecuperacionDeAcceso, Huella, Ahora.AddMinutes(1)));
    }

    [Fact]
    public async Task UnCodigoVencido_SeRechazaComoVencido_YInvalidarLoDejaSinEfecto()
    {
        var codigos = new PostgresCodigosDeUnSoloUso(_bd.AccesoAlEsquema);
        var id = (await Insertada()).Usuario.Id;
        await codigos.Emitir(CodigoDeUnSoloUso.Emitir(id, DominioProposito.RecuperacionDeAcceso, Huella, Ahora));

        Assert.Equal(ResultadoDelCodigo.Vencido, await codigos.Verificar(id, DominioProposito.RecuperacionDeAcceso, Huella, Ahora.AddMinutes(10)));

        await codigos.Emitir(CodigoDeUnSoloUso.Emitir(id, DominioProposito.RecuperacionDeAcceso, Huella, Ahora.AddMinutes(20)));
        await codigos.Invalidar(id, DominioProposito.RecuperacionDeAcceso);
        Assert.Equal(ResultadoDelCodigo.NoVigente, await codigos.Verificar(id, DominioProposito.RecuperacionDeAcceso, Huella, Ahora.AddMinutes(21)));
    }

    [Fact]
    public async Task LaSolicitudDeCodigo_SeRegistraConLaHuellaDelCorreo_YSePurgaA_Los10Minutos()
    {
        var registro = new PostgresRegistroDeSolicitudesDeCodigo(_bd.AccesoAlEsquema);
        await _bd.Ejecutar("DELETE FROM identidad.solicitudes_de_codigo");
        await registro.Registrar(new string('c', 64), DominioProposito.RecuperacionDeAcceso, Ahora);
        await registro.Registrar(new string('d', 64), DominioProposito.VerificacionDeCorreo, Ahora.AddMinutes(8));

        var purgadas = await registro.Purgar(Ahora.AddMinutes(11));

        Assert.Equal(1, purgadas);
        Assert.Equal(1, await _bd.Consultar<long>("SELECT count(*) FROM identidad.solicitudes_de_codigo"));
    }

    // ---------------------------------------------------------------- renovaciones y desafíos

    private static Renovacion Renovacion(string huella, Guid usuario, Guid familia, DateTimeOffset? usada = null, DateTimeOffset? revocada = null) =>
        BabelHub.Ecommerce.Domain.Identidad.Renovacion.Restaurar(huella, usuario, familia, Ahora, Ahora.AddDays(30), usada, revocada);

    [Fact]
    [Trait("Criterio", "AC-IDE-20")]
    public async Task UnaRenovacion_SeRotaUnaSolaVez_ConSolicitudesSimultaneas()
    {
        var renovaciones = new PostgresRepositorioDeRenovaciones(_bd.AccesoAlEsquema);
        var usuario = (await Insertada()).Usuario.Id;
        var huella = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        await renovaciones.Guardar(Renovacion(huella, usuario, Guid.NewGuid()));

        var resultados = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => Task.Run(() => renovaciones.MarcarUsada(huella, Ahora.AddMinutes(1)))));

        Assert.Equal(1, resultados.Count(r => r));
        var leida = (await renovaciones.PorHuella(huella))!;
        Assert.Equal(Ahora.AddMinutes(1), leida.UsadaEn);
        Assert.Equal(usuario, leida.UsuarioId);
        Assert.Null(await renovaciones.PorHuella(new string('f', 64)));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-20")]
    public async Task RevocarLasRenovaciones_PorFamilia_PorUsuario_LasDemas_YLasDeTodos()
    {
        var renovaciones = new PostgresRepositorioDeRenovaciones(_bd.AccesoAlEsquema);
        var usuario = (await Insertada()).Usuario.Id;
        var otro = (await Insertada()).Usuario.Id;
        var familiaA = Guid.NewGuid();
        var familiaB = Guid.NewGuid();
        string H() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var (a1, a2, b1, o1) = (H(), H(), H(), H());
        await renovaciones.Guardar(Renovacion(a1, usuario, familiaA));
        await renovaciones.Guardar(Renovacion(a2, usuario, familiaA));
        await renovaciones.Guardar(Renovacion(b1, usuario, familiaB));
        await renovaciones.Guardar(Renovacion(o1, otro, Guid.NewGuid()));

        await renovaciones.RevocarFamilia(usuario, familiaA, Ahora);
        Assert.NotNull((await renovaciones.PorHuella(a1))!.RevocadaEn);
        Assert.NotNull((await renovaciones.PorHuella(a2))!.RevocadaEn);
        Assert.Null((await renovaciones.PorHuella(b1))!.RevocadaEn);

        await renovaciones.RevocarLasDemas(usuario, familiaB, Ahora.AddMinutes(1));
        Assert.Null((await renovaciones.PorHuella(b1))!.RevocadaEn);
        Assert.Equal(Ahora, (await renovaciones.PorHuella(a1))!.RevocadaEn);

        await renovaciones.RevocarTodas(usuario, Ahora.AddMinutes(2));
        Assert.Equal(Ahora.AddMinutes(2), (await renovaciones.PorHuella(b1))!.RevocadaEn);
        Assert.Null((await renovaciones.PorHuella(o1))!.RevocadaEn);

        await renovaciones.RevocarLasDeTodos(Ahora.AddMinutes(3));
        Assert.Equal(Ahora.AddMinutes(3), (await renovaciones.PorHuella(o1))!.RevocadaEn);
    }

    [Fact]
    public async Task UnDesafio_SeGuardaPorSuHuella_YSeConsumeUnaSolaVez()
    {
        var desafios = new PostgresRepositorioDeDesafios(_bd.AccesoAlEsquema);
        var usuario = (await Insertada()).Usuario.Id;
        var desafio = DesafioDeIngreso.Crear(Guid.NewGuid(), usuario, ModoDelDesafio.ConfigurarSegundoFactor, Ahora);
        var huella = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        await desafios.Guardar(desafio, huella);

        var leido = (await desafios.PorHuella(huella))!;
        var resultados = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => desafios.MarcarUsado(desafio.Id))));

        Assert.Equal(desafio.Id, leido.Id);
        Assert.Equal(ModoDelDesafio.ConfigurarSegundoFactor, leido.Modo);
        Assert.Equal(Ahora.AddMinutes(5), leido.Vence);
        Assert.Equal(1, resultados.Count(r => r));
        Assert.True((await desafios.PorHuella(huella))!.Usado);
        Assert.Null(await desafios.PorHuella(new string('9', 64)));
    }

    // ---------------------------------------------------------------- segundo factor

    [Fact]
    [Trait("Criterio", "AC-IDE-20")]
    public async Task ElSegundoFactor_SeConfirmaConCodigosDeRespaldo_YUnPasoSeAceptaUnaSolaVez()
    {
        var almacen = new PostgresSegundoFactorAlmacen(_bd.AccesoAlEsquema);
        var usuario = (await Insertada()).Usuario.Id;
        var secreto = RandomNumberGenerator.GetBytes(60);
        await almacen.GuardarSinConfirmar(usuario, secreto);

        var sinConfirmar = (await almacen.Obtener(usuario))!;
        Assert.Equal(EstadoDelSegundoFactor.SinConfirmar, sinConfirmar.Factor.Estado);
        Assert.Equal(secreto, sinConfirmar.SecretoCifrado);

        var respaldo = Enumerable.Range(0, 8).Select(_ => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))).ToList();
        await almacen.Activar(usuario, 1000, respaldo);
        var activo = (await almacen.Obtener(usuario))!;

        Assert.Equal(EstadoDelSegundoFactor.Activo, activo.Factor.Estado);
        Assert.Equal(1000, activo.Factor.UltimoPasoUsado);
        Assert.Equal(8, activo.Factor.CodigosDeRespaldoRestantes);

        var pasos = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => Task.Run(() => almacen.AceptarPaso(usuario, 1001))));
        Assert.Equal(1, pasos.Count(r => r));
        Assert.False(await almacen.AceptarPaso(usuario, 1001));
        Assert.False(await almacen.AceptarPaso(usuario, 1000));
        Assert.True(await almacen.AceptarPaso(usuario, 1002));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-38")]
    public async Task UnCodigoDeRespaldo_SeConsumeUnaSolaVez_ConSolicitudesSimultaneas()
    {
        var almacen = new PostgresSegundoFactorAlmacen(_bd.AccesoAlEsquema);
        var usuario = (await Insertada()).Usuario.Id;
        await almacen.GuardarSinConfirmar(usuario, RandomNumberGenerator.GetBytes(60));
        var respaldo = Enumerable.Range(0, 8).Select(_ => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))).ToList();
        await almacen.Activar(usuario, 1, respaldo);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => Task.Run(() => almacen.ConsumirCodigoDeRespaldo(usuario, respaldo[0], Ahora))));

        Assert.Equal(1, resultados.Count(r => r));
        Assert.Equal(7, (await almacen.Obtener(usuario))!.Factor.CodigosDeRespaldoRestantes);
        Assert.False(await almacen.ConsumirCodigoDeRespaldo(usuario, new string('0', 64), Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-38")]
    public async Task RegenerarLosCodigosDeRespaldo_DejaSinEfectoLosAnteriores_YDesactivarEliminaTodo()
    {
        var almacen = new PostgresSegundoFactorAlmacen(_bd.AccesoAlEsquema);
        var usuario = (await Insertada()).Usuario.Id;
        await almacen.GuardarSinConfirmar(usuario, RandomNumberGenerator.GetBytes(60));
        var viejos = Enumerable.Range(0, 8).Select(_ => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))).ToList();
        await almacen.Activar(usuario, 1, viejos);
        var nuevos = Enumerable.Range(0, 8).Select(_ => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))).ToList();

        await almacen.ReemplazarCodigosDeRespaldo(usuario, nuevos);

        Assert.False(await almacen.ConsumirCodigoDeRespaldo(usuario, viejos[0], Ahora));
        Assert.True(await almacen.ConsumirCodigoDeRespaldo(usuario, nuevos[0], Ahora));

        await almacen.Desactivar(usuario);
        Assert.Null(await almacen.Obtener(usuario));
        Assert.Equal(0, await _bd.Consultar<long>($"SELECT count(*) FROM identidad.codigos_de_respaldo WHERE usuario_id = '{usuario}'"));
    }

    [Fact]
    public async Task GuardarSinConfirmarReemplazaElQueEstabaSinConfirmar_PeroNuncaUnoActivo()
    {
        var almacen = new PostgresSegundoFactorAlmacen(_bd.AccesoAlEsquema);
        var usuario = (await Insertada()).Usuario.Id;
        var primero = RandomNumberGenerator.GetBytes(60);
        var segundo = RandomNumberGenerator.GetBytes(60);
        await almacen.GuardarSinConfirmar(usuario, primero);
        await almacen.GuardarSinConfirmar(usuario, segundo);
        Assert.Equal(segundo, (await almacen.Obtener(usuario))!.SecretoCifrado);

        await almacen.Activar(usuario, 5, [new string('1', 64)]);
        await almacen.GuardarSinConfirmar(usuario, primero);

        Assert.Equal(segundo, (await almacen.Obtener(usuario))!.SecretoCifrado);
        await Assert.ThrowsAsync<InvalidOperationException>(() => almacen.Activar(Guid.NewGuid(), 1, []));
    }

    // ---------------------------------------------------------------- avisos, dispositivos, cambios de correo

    [Fact]
    public async Task LosAvisosDeLaCuenta_SeListanMarcanVistosSoloPorSuTitular()
    {
        var avisos = new PostgresAvisosDeCuenta(_bd.AccesoAlEsquema);
        var titular = (await Insertada()).Usuario.Id;
        var otro = (await Insertada()).Usuario.Id;
        await avisos.Agregar(titular, "contrasena-cambiada", Ahora);
        await avisos.Agregar(titular, "correo-cambiado", Ahora.AddMinutes(1));

        var lista = await avisos.Listar(titular);

        Assert.Equal(["correo-cambiado", "contrasena-cambiada"], lista.Select(a => a.Tipo));
        Assert.All(lista, a => Assert.False(a.Visto));
        Assert.False(await avisos.MarcarVisto(otro, lista[0].Id, Ahora));
        Assert.True(await avisos.MarcarVisto(titular, lista[0].Id, Ahora.AddMinutes(2)));
        Assert.True((await avisos.Listar(titular))[0].Visto);
        Assert.False(await avisos.MarcarVisto(titular, Guid.NewGuid(), Ahora));
    }

    [Fact]
    public async Task LosDispositivos_SeActualizanPorHuella_YSoloSeConservanLosDiezMasRecientes()
    {
        var dispositivos = new PostgresRepositorioDeDispositivos(_bd.AccesoAlEsquema);
        var usuario = (await Insertada()).Usuario.Id;
        for (var i = 0; i < 12; i++)
        {
            await dispositivos.Guardar(Dispositivo.Emitir(usuario, i.ToString("x2").PadLeft(64, '0'), "pe", Ahora.AddMinutes(i)));
        }

        var lista = await dispositivos.Listar(usuario);

        Assert.Equal(IRepositorioDeDispositivos.Maximo, lista.Count);
        Assert.DoesNotContain(lista, d => d.HuellaDeLaMarca == "00".PadLeft(64, '0'));
        Assert.Equal("0b".PadLeft(64, '0'), lista[0].HuellaDeLaMarca);

        var reciente = lista[0];
        reciente.Congelar(Ahora.AddDays(1));
        await dispositivos.Guardar(reciente);
        Assert.Equal(Ahora.AddDays(1), (await dispositivos.Listar(usuario))[0].CongeladoHasta);
        Assert.Equal(IRepositorioDeDispositivos.Maximo, (await dispositivos.Listar(usuario)).Count);
    }

    [Fact]
    public async Task ElCambioDeCorreo_SoloHayUnoPorUsuario_YGuardarReemplazaElAnterior()
    {
        var cambios = new PostgresRepositorioDeCambiosDeCorreo(_bd.AccesoAlEsquema);
        var usuario = (await Insertada()).Usuario.Id;
        var primero = CambioDeCorreoPendiente.Restaurar(Guid.NewGuid(), usuario, "primero@example.com", Ahora, EstadoDelCambioDeCorreo.EsperandoVerificacion);
        var segundo = CambioDeCorreoPendiente.Restaurar(Guid.NewGuid(), usuario, "SEGUNDO@example.com", Ahora.AddHours(1), EstadoDelCambioDeCorreo.PendienteDeConfirmacion);

        await cambios.Guardar(primero);
        await cambios.Guardar(segundo);
        var leido = (await cambios.Obtener(usuario))!;

        Assert.Equal(segundo.Id, leido.Id);
        Assert.Equal("segundo@example.com", leido.CorreoNuevo);
        Assert.Equal(EstadoDelCambioDeCorreo.PendienteDeConfirmacion, leido.Estado);
        Assert.Equal(Ahora.AddHours(1).AddHours(24), leido.Vence);
        Assert.Equal(1, await _bd.Consultar<long>($"SELECT count(*) FROM identidad.cambios_de_correo_pendientes WHERE usuario_id = '{usuario}'"));
        Assert.Null(await cambios.Obtener(Guid.NewGuid()));
    }

    // ---------------------------------------------------------------- solicitudes, cancelaciones, bajas

    [Fact]
    public async Task LasSolicitudesDeDatos_SeGuardanSeActualizanYSeConsultan()
    {
        var repositorio = new PostgresRepositorioDeSolicitudesDeDatos(_bd.AccesoAlEsquema);
        var usuario = (await Insertada()).Usuario.Id;
        var solicitud = SolicitudDeDatos.Recibir(Guid.NewGuid(), usuario, TipoDeSolicitudDeDatos.Acceso, Ahora, DateOnly.FromDateTime(Ahora.Date));
        await repositorio.Guardar(solicitud);

        var leida = (await repositorio.Obtener(solicitud.Id))!;
        Assert.Equal(EstadoDeSolicitudDeDatos.Recibida, leida.Estado);
        Assert.Equal(solicitud.FechaLimite, leida.FechaLimite);

        leida.Atender(Ahora.AddDays(2), "Le enviamos su información.");
        await repositorio.Guardar(leida);

        var tras = (await repositorio.Obtener(solicitud.Id, paraActualizar: false))!;
        Assert.Equal(EstadoDeSolicitudDeDatos.Atendida, tras.Estado);
        Assert.Equal("Le enviamos su información.", tras.Respuesta);
        Assert.Single(await repositorio.DelUsuario(usuario));
        Assert.Contains(await repositorio.Todas(), s => s.Id == solicitud.Id);
        Assert.Null(await repositorio.Obtener(Guid.NewGuid()));
    }

    [Fact]
    public async Task UnaCancelacionAbiertaPorUsuario_SeGuardaSeLeeYSeCierra()
    {
        var repositorio = new PostgresRepositorioDeCancelaciones(_bd.AccesoAlEsquema);
        var usuario = (await Insertada()).Usuario.Id;
        var solicitud = SolicitudDeCancelacion.Diferir(Guid.NewGuid(), usuario, Ahora);
        await repositorio.Guardar(solicitud);

        Assert.Equal(solicitud.Id, (await repositorio.AbiertaDe(usuario))!.Id);
        await Assert.ThrowsAnyAsync<Exception>(() => repositorio.Guardar(SolicitudDeCancelacion.Diferir(Guid.NewGuid(), usuario, Ahora.AddMinutes(1))));

        Assert.True(solicitud.Desistir(Ahora.AddDays(30)));
        await repositorio.Guardar(solicitud);

        Assert.Null(await repositorio.AbiertaDe(usuario));
        Assert.Equal(EstadoDeCancelacion.Descartada, (await repositorio.UltimaDe(usuario))!.Estado);
        Assert.Equal(Ahora.AddDays(30), (await repositorio.UltimaDe(usuario))!.CerradaEn);
        Assert.Null(await repositorio.UltimaDe(Guid.NewGuid()));
    }

    [Fact]
    public async Task LaRevisionDeCancelaciones_TomaCadaAbiertaConSkipLocked_YDosRevisionesNoComparten()
    {
        var repositorio = new PostgresRepositorioDeCancelaciones(_bd.AccesoAlEsquema);
        await _bd.Ejecutar("DELETE FROM identidad.solicitudes_de_cancelacion");
        for (var i = 0; i < 6; i++)
        {
            var usuario = (await Insertada()).Usuario.Id;
            await repositorio.Guardar(SolicitudDeCancelacion.Diferir(Guid.NewGuid(), usuario, Ahora.AddMinutes(i)));
        }

        var primera = new TaskCompletionSource();
        var liberar = new TaskCompletionSource();
        var revisionA = _bd.Unidad.Ejecutar(
            async ct =>
            {
                var tomadas = await repositorio.AbiertasParaRevisar(3, ct);
                primera.SetResult();
                await liberar.Task;
                return tomadas;
            },
            CancellationToken.None);
        await primera.Task;

        var revisionB = await _bd.Unidad.Ejecutar(ct => repositorio.AbiertasParaRevisar(10, ct), CancellationToken.None);
        liberar.SetResult();
        var tomadasA = await revisionA;

        Assert.Equal(3, tomadasA.Count);
        Assert.Equal(3, revisionB.Count);
        Assert.Empty(tomadasA.Select(s => s.Id).Intersect(revisionB.Select(s => s.Id)));
    }

    [Fact]
    public async Task LasBajas_SeAgreganUnaVez_QuedanPendientesHastaEntregarYCerrar_SinClaveForanea()
    {
        var bajas = new PostgresRepositorioDeBajas(_bd.AccesoAlEsquema);
        await _bd.Ejecutar("DELETE FROM identidad.bajas_titulares");
        var opaco = Guid.NewGuid();
        await bajas.Agregar(new BajaDeTitular(opaco, Ahora));
        await bajas.Agregar(new BajaDeTitular(opaco, Ahora.AddMinutes(5)));

        var pendientes = await bajas.Pendientes(10);
        Assert.Single(pendientes);
        Assert.Equal(Ahora, pendientes[0].Creada);

        pendientes[0].EntregadaEn = Ahora.AddMinutes(1);
        await bajas.Guardar(pendientes[0]);
        Assert.Single(await bajas.Pendientes(10));

        pendientes[0].CerradaEn = Ahora.AddMinutes(2);
        await bajas.Guardar(pendientes[0]);
        Assert.Empty(await bajas.Pendientes(10));
        Assert.Equal(1, await _bd.Consultar<long>($"SELECT count(*) FROM identidad.bajas_titulares WHERE identificador_opaco = '{opaco}' AND entregada_en IS NOT NULL AND cerrada_en IS NOT NULL"));
    }

    [Fact]
    public async Task PorAnonimizar_SeAgregaIdempotente_YSeListaSoloLoPendiente()
    {
        var porAnonimizar = new PostgresPorAnonimizar(_bd.AccesoAlEsquema);
        await _bd.Ejecutar("DELETE FROM identidad.por_anonimizar");
        var uno = Guid.NewGuid();
        var otro = Guid.NewGuid();

        await porAnonimizar.Agregar(uno);
        await porAnonimizar.Agregar(uno);
        await porAnonimizar.Agregar(otro);
        await _bd.Ejecutar($"UPDATE identidad.por_anonimizar SET procesado_en = now() WHERE identificador_opaco = '{otro}'");

        Assert.Equal([uno], await porAnonimizar.Listar(10));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-20")]
    public async Task LaPurgaDeCancelacion_EliminaLosDatosDelModulo_ConservaLaSolicitudSinSuTexto_YNoTocaLaCuenta()
    {
        var purga = new PostgresPurgaDeIdentidad(_bd.AccesoAlEsquema);
        var cuenta = await Insertada();
        var id = cuenta.Usuario.Id;
        await new PostgresAvisosDeCuenta(_bd.AccesoAlEsquema).Agregar(id, "contrasena-cambiada", Ahora);
        await new PostgresRepositorioDeDispositivos(_bd.AccesoAlEsquema).Guardar(Dispositivo.Emitir(id, new string('e', 64), "pe", Ahora));
        var almacen = new PostgresSegundoFactorAlmacen(_bd.AccesoAlEsquema);
        await almacen.GuardarSinConfirmar(id, RandomNumberGenerator.GetBytes(60));
        await almacen.Activar(id, 1, [new string('2', 64)]);
        var renovaciones = new PostgresRepositorioDeRenovaciones(_bd.AccesoAlEsquema);
        await renovaciones.Guardar(Renovacion(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), id, Guid.NewGuid()));
        var datos = new PostgresRepositorioDeSolicitudesDeDatos(_bd.AccesoAlEsquema);
        var solicitud = SolicitudDeDatos.Recibir(Guid.NewGuid(), id, TipoDeSolicitudDeDatos.Acceso, Ahora, DateOnly.FromDateTime(Ahora.Date));
        solicitud.Atender(Ahora.AddDays(1), "Contenido de la respuesta.");
        await datos.Guardar(solicitud);

        await purga.EliminarLosDatosDe(id);

        foreach (var tabla in new[] { "avisos_de_cuenta", "dispositivos", "segundo_factor", "codigos_de_respaldo", "renovaciones" })
        {
            Assert.Equal(0, await _bd.Consultar<long>($"SELECT count(*) FROM identidad.{tabla} WHERE usuario_id = '{id}'"));
        }

        var conservada = (await datos.Obtener(solicitud.Id))!;
        Assert.Null(conservada.Respuesta);
        Assert.Null(conservada.Motivo);
        Assert.Equal(EstadoDeSolicitudDeDatos.Atendida, conservada.Estado);
        Assert.NotNull(await _cuentas.PorId(id));
    }

    // ---------------------------------------------------------------- claves de firma

    [Fact]
    [Trait("Criterio", "AC-IDE-20")]
    public async Task LaRotacionDeLaClaveDeFirma_ActivaLaNueva_DejaLaAnterior_YLaPrivadaSoloSaleDescifrada()
    {
        await _bd.Ejecutar("DELETE FROM identidad.claves_de_firma");
        var almacen = new PostgresAlmacenDeClavesDeFirma(_bd.AccesoAlEsquema);
        var cifrador = new AesGcmCifradorDeSecretos(ClaveVersionadaDeIdentidad.Analizar($"v1:{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}", "c"));
        var generador = new Es256GeneradorDeClavesDeFirma(cifrador, almacen);
        var ahora = Ahora;

        // La primera clave: el despliegue la crea y la deja activa.
        await _bd.Unidad.Ejecutar(
            async ct =>
            {
                var (primera, _) = await generador.Generar(ahora, ct);
                await almacen.Guardar(JuegoDeClavesDeFirma.Iniciar(primera), ct);
                return 0;
            },
            CancellationToken.None);
        var proveedor = new PostgresClavePrivadaDeFirma(almacen, cifrador);
        var activaAntes = await proveedor.Activa();

        // La rotación: carga con bloqueo, genera la nueva en la misma transacción y guarda el juego.
        await _bd.Unidad.Ejecutar(
            async ct =>
            {
                var juego = await almacen.Cargar(paraActualizar: true, ct);
                var (nueva, _) = await generador.Generar(ahora.AddDays(91), ct);
                juego.Rotar(nueva, ahora.AddDays(91));
                await almacen.Guardar(juego, ct);
                return 0;
            },
            CancellationToken.None);

        var juegoFinal = await almacen.Cargar(paraActualizar: false);
        var publicas = await almacen.ParametrosPublicos([.. juegoFinal.Claves.Select(c => c.Kid)]);

        Assert.Equal(2, juegoFinal.Claves.Count);
        Assert.NotEqual(activaAntes.Kid, juegoFinal.Activa.Kid);
        Assert.Equal(EstadoDeClaveDeFirma.Anterior, juegoFinal.Claves.Single(c => c.Kid == activaAntes.Kid).Estado);
        Assert.Equal(2, publicas.Count);
        Assert.All(publicas.Values, jwk => Assert.True(JwkEs256.TryLeerLaPublica(jwk, out _)));
        Assert.DoesNotContain(publicas.Values, jwk => jwk.Contains("\"d\"", StringComparison.Ordinal));
        Assert.Equal(1, await _bd.Consultar<long>("SELECT count(*) FROM identidad.claves_de_firma WHERE estado = 'activa'"));

        // La caché de un minuto: tras vencer, el proveedor entrega la nueva activa y su firma se verifica con el JWK publicado.
        var reloj = new RelojDeCachePrueba(Ahora);
        var sinCache = new PostgresClavePrivadaDeFirma(almacen, cifrador, reloj);
        var activaDespues = await sinCache.Activa();
        Assert.Equal(juegoFinal.Activa.Kid, activaDespues.Kid);
        Assert.True(JwkEs256.TryLeerLaPublica(publicas[activaDespues.Kid], out var publica));
        var firma = activaDespues.Clave.SignData(new byte[] { 1, 2, 3 }, HashAlgorithmName.SHA256);
        Assert.True(publica.VerifyData(new byte[] { 1, 2, 3 }, firma, HashAlgorithmName.SHA256));
    }

    [Fact]
    public async Task UnaClaveGeneradaCuyaTransaccionSeRevierte_NoDejaNada()
    {
        var almacen = new PostgresAlmacenDeClavesDeFirma(_bd.AccesoAlEsquema);
        var cifrador = new AesGcmCifradorDeSecretos(ClaveVersionadaDeIdentidad.Analizar($"v1:{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}", "c"));
        var generador = new Es256GeneradorDeClavesDeFirma(cifrador, almacen);
        var antes = await _bd.Consultar<long>("SELECT count(*) FROM identidad.claves_de_firma");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _bd.Unidad.Ejecutar<int>(
            async ct =>
            {
                await generador.Generar(Ahora, ct);
                throw new InvalidOperationException("La rotación falló.");
            },
            CancellationToken.None));

        Assert.Equal(antes, await _bd.Consultar<long>("SELECT count(*) FROM identidad.claves_de_firma"));
    }

    private sealed class RelojDeCachePrueba(DateTimeOffset inicio) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => inicio;
    }
}
