using Microsoft.Extensions.DependencyInjection;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Infrastructure.Identidad;
using System.Security.Cryptography;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Plataforma;

/// <summary>PostgreSQL real con las tablas del correo de <c>Plataforma</c> y el rol de acceso <c>NOINHERIT</c> que opera como <c>rol_plataforma</c> (<c>EjecutarComo</c>).</summary>
public sealed class PostgresDelCorreoFixture : IAsyncLifetime
{
    private static readonly string ClaveDeAcceso = "acceso-" + Guid.NewGuid().ToString("N");

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pruebas")
        .WithUsername("postgres")
        .WithPassword("admin-" + Guid.NewGuid().ToString("N"))
        .Build();

    public NpgsqlDataSource Administracion { get; private set; } = null!;

    public UnidadDeTrabajoPostgres Unidad { get; private set; } = null!;

    private NpgsqlDataSource _acceso = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());
        await Ejecutar("DO $r$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rol_plataforma') THEN CREATE ROLE rol_plataforma NOLOGIN; END IF; END $r$;");
        await Ejecutar(DefinicionDelCorreo.Sql + DefinicionDelCorreo.Permisos);
        var clave = ClaveDeAcceso;
        await Ejecutar($"CREATE ROLE acceso LOGIN NOINHERIT PASSWORD '{clave}'; GRANT rol_plataforma TO acceso;");
        _acceso = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()) { Username = "acceso", Password = clave }.ConnectionString);
        Unidad = new UnidadDeTrabajoPostgres(_acceso);
    }

    public async Task DisposeAsync()
    {
        foreach (var origen in new[] { _acceso, Administracion })
        {
            if (origen is not null)
            {
                await origen.DisposeAsync();
            }
        }

        await _contenedor.DisposeAsync();
    }

    public async Task Ejecutar(string sql)
    {
        await using var comando = Administracion.CreateCommand(sql);
        await comando.ExecuteNonQueryAsync();
    }

    public async Task<T?> Consultar<T>(string sql)
    {
        await using var comando = Administracion.CreateCommand(sql);
        var valor = await comando.ExecuteScalarAsync();
        return valor is null or DBNull ? default : (T)valor;
    }
}

/// <summary>
/// El correo de producción de Plataforma (identidad-seguridad T34; AC-IDE-54, AC-IDE-55, AC-IDE-60, AC-IDE-62; IDE-65, IDE-66, IDE-73, IDE-78, IDE-80, IDE-82): la reserva atómica de la cuota y del tope de 24 al día, la cola
/// con el contenido cifrado y por prioridad, el cifrado AES-GCM de la cola, el transporte SMTP que no entrega a una dirección inválida y el transporte que falla cerrado sin cuenta configurada.
/// </summary>
public sealed class CorreoDeProduccionTests(PostgresDelCorreoFixture bd) : IClassFixture<PostgresDelCorreoFixture>
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    private sealed class RelojDePrueba : IReloj
    {
        private DateTimeOffset _actual = CorreoDeProduccionTests.Ahora;

        /// <summary>Cada lectura avanza un segundo: dos correos encolados seguidos no empatan en su fecha de creación.</summary>
        public DateTimeOffset Ahora
        {
            get => _actual = _actual.AddSeconds(1);
            set => _actual = value;
        }

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private static CifradorDeLaColaDeCorreo Cifrador() => new(RandomNumberGenerator.GetBytes(32));

    // ---------------------------------------------------------------- la cuota (IDE-66, IDE-73)

    [Fact]
    [Trait("Criterio", "AC-IDE-55")]
    public async Task LaReserva_LlenaElPrimerCupoYPasaAlSiguiente_SinSuperarNingunTamano()
    {
        var almacen = new PostgresAlmacenDeCuotaDeCorreo(bd.Unidad);
        var dia = new DateOnly(2030, 1, 1);
        IReadOnlyList<CupoCandidato> candidatos = [new(CupoDeCorreo.CriticasDeCuenta, 2), new(CupoDeCorreo.Resto, 3)];

        var reservas = new List<ReservaDeCupoDeCorreo?>();
        for (var i = 0; i < 6; i++)
        {
            reservas.Add(await almacen.Reservar(dia, candidatos));
        }

        Assert.Equal([CupoDeCorreo.CriticasDeCuenta, CupoDeCorreo.CriticasDeCuenta, CupoDeCorreo.Resto, CupoDeCorreo.Resto, CupoDeCorreo.Resto], reservas.Take(5).Select(r => r!.Cupo));
        Assert.Null(reservas[5]);
        Assert.Equal([1, 2, 1, 2, 3], reservas.Take(5).Select(r => r!.EnviadosEnElCupo));
        Assert.Equal([1, 2, 3, 4, 5], reservas.Take(5).Select(r => r!.EnviadosEnTotal));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-55")]
    public async Task CuarentaReservasSimultaneas_SobreUnCupoDe10_DejanExactamente10()
    {
        var almacen = new PostgresAlmacenDeCuotaDeCorreo(bd.Unidad);
        var dia = new DateOnly(2030, 1, 2);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => almacen.Reservar(dia, [new CupoCandidato(CupoDeCorreo.Codigos, 10)]))));

        Assert.Equal(10, resultados.Count(r => r is not null));
        Assert.Equal(10, await bd.Consultar<int>("SELECT enviados FROM plataforma.cuota_correo WHERE dia = '2030-01-02' AND cupo = 'codigos'"));
    }

    [Fact]
    public async Task DevolverUnLugar_LoDejaLibre_SinBajarDeCero_YUnCupoDeTamanoCeroSeSalta()
    {
        var almacen = new PostgresAlmacenDeCuotaDeCorreo(bd.Unidad);
        var dia = new DateOnly(2030, 1, 3);
        await almacen.Reservar(dia, [new CupoCandidato(CupoDeCorreo.Resto, 1)]);
        Assert.Null(await almacen.Reservar(dia, [new CupoCandidato(CupoDeCorreo.Resto, 1)]));

        await almacen.Devolver(dia, CupoDeCorreo.Resto);
        await almacen.Devolver(dia, CupoDeCorreo.Resto);
        await almacen.Devolver(dia, CupoDeCorreo.Codigos);

        Assert.NotNull(await almacen.Reservar(dia, [new CupoCandidato(CupoDeCorreo.Resto, 1)]));
        Assert.Equal(CupoDeCorreo.Resto, (await almacen.Reservar(dia, [new CupoCandidato(CupoDeCorreo.Administracion, 0), new CupoCandidato(CupoDeCorreo.Resto, 5)]))!.Cupo);
    }

    [Fact]
    public async Task CadaDiaTieneSuCuota()
    {
        var almacen = new PostgresAlmacenDeCuotaDeCorreo(bd.Unidad);

        Assert.NotNull(await almacen.Reservar(new DateOnly(2030, 2, 1), [new CupoCandidato(CupoDeCorreo.Resto, 1)]));
        Assert.NotNull(await almacen.Reservar(new DateOnly(2030, 2, 2), [new CupoCandidato(CupoDeCorreo.Resto, 1)]));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-62")]
    public async Task ElTopeDe24AlDiaPorMotivoYServicio_SeReservaAtomicamente_YSeDevuelve()
    {
        var almacen = new PostgresAlmacenDeCuotaDeCorreo(bd.Unidad);
        var dia = new DateOnly(2030, 1, 4);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => almacen.ReservarAlertaDeAdministracion(dia, "ingresos-fallidos", "ecommerce", 24))));

        Assert.Equal(24, resultados.Count(r => r));
        Assert.False(await almacen.ReservarAlertaDeAdministracion(dia, "ingresos-fallidos", "ecommerce", 24));
        Assert.True(await almacen.ReservarAlertaDeAdministracion(dia, "ingresos-fallidos", "inventario", 24));
        Assert.True(await almacen.ReservarAlertaDeAdministracion(dia, "otro-motivo", "ecommerce", 24));

        await almacen.DevolverAlertaDeAdministracion(dia, "ingresos-fallidos", "ecommerce");
        Assert.True(await almacen.ReservarAlertaDeAdministracion(dia, "ingresos-fallidos", "ecommerce", 24));
        Assert.False(await almacen.ReservarAlertaDeAdministracion(dia, "motivo-sin-tope", "ecommerce", 0));
        await Assert.ThrowsAsync<ArgumentException>(() => almacen.ReservarAlertaDeAdministracion(dia, "Motivo Con Espacios", "ecommerce", 24));
    }

    // ---------------------------------------------------------------- la cola cifrada (IDE-78, IDE-80)

    private static CorreoSaliente Correo(CategoriaDeCorreo categoria, string plantilla = "pago-confirmado", string destinatario = "ana.perez@example.com") =>
        categoria == CategoriaDeCorreo.AlertaALaAdministracion
            ? new CorreoSaliente(categoria, destinatario, "alerta-a-la-administracion", motivo: "ingresos-fallidos", servicio: "ecommerce")
            : new CorreoSaliente(categoria, destinatario, plantilla, new Dictionary<string, string> { ["numeroDePedido"] = "BH-000123" });

    [Fact]
    [Trait("Criterio", "AC-IDE-60")]
    public async Task LaCola_EntregaPorPrioridad_ConElContenidoCifradoEnLaBase_YDevuelveLoGuardado()
    {
        await bd.Ejecutar("DELETE FROM plataforma.correos_pendientes");
        var cola = new PostgresColaDeCorreosPendientes(bd.Unidad, Cifrador(), new RelojDePrueba());
        await cola.Agregar(Correo(CategoriaDeCorreo.Pago), 2, Ahora.AddMinutes(-3));
        await cola.Agregar(Correo(CategoriaDeCorreo.AlertaCriticaDeCuenta, "contrasena-cambiada", "critica@example.com"), 1, Ahora.AddMinutes(-3));
        await cola.Agregar(Correo(CategoriaDeCorreo.AlertaALaAdministracion), 0, Ahora.AddMinutes(-3));
        await cola.Agregar(Correo(CategoriaDeCorreo.RespuestaDeReclamo, "reclamo-respondido"), 2, Ahora.AddMinutes(-3));
        await cola.Agregar(Correo(CategoriaDeCorreo.Pago), 2, Ahora.AddMinutes(10));

        var turno = await cola.Vencidos(Ahora, 10);

        Assert.Equal(
            [CategoriaDeCorreo.AlertaALaAdministracion, CategoriaDeCorreo.AlertaCriticaDeCuenta, CategoriaDeCorreo.Pago, CategoriaDeCorreo.RespuestaDeReclamo],
            turno.Select(p => p.Correo.Categoria));
        Assert.Equal([0, 1, 2, 2], turno.Select(p => p.Prioridad));
        var critica = turno[1].Correo;
        Assert.Equal("critica@example.com", critica.Destinatario);
        Assert.Equal("contrasena-cambiada", critica.Plantilla);
        Assert.Equal("ingresos-fallidos", turno[0].Correo.Motivo);
        Assert.Equal("BH-000123", turno[2].Correo.Datos["numeroDePedido"]);

        // En la base no hay ninguna dirección ni dato del correo en claro.
        var crudo = Convert.FromHexString(await bd.Consultar<string>("SELECT string_agg(encode(contenido, 'hex'), '') FROM plataforma.correos_pendientes") ?? string.Empty);
        var texto = System.Text.Encoding.Latin1.GetString(crudo);
        Assert.DoesNotContain("example.com", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("BH-000123", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaCola_LimitaElTurno_EliminaLoEnviado_YReprogramaConSusIntentos()
    {
        await bd.Ejecutar("DELETE FROM plataforma.correos_pendientes");
        var cola = new PostgresColaDeCorreosPendientes(bd.Unidad, Cifrador(), new RelojDePrueba());
        for (var i = 0; i < 5; i++)
        {
            await cola.Agregar(Correo(CategoriaDeCorreo.Pago), 2, Ahora.AddMinutes(-1));
        }

        var primeros = await cola.Vencidos(Ahora, 3);
        await cola.Eliminar(primeros[0].Id);
        await cola.Reprogramar(primeros[1].Id, 4, Ahora.AddHours(1));

        Assert.Equal(3, primeros.Count);
        var restantes = await cola.Vencidos(Ahora, 10);
        Assert.Equal(3, restantes.Count);
        Assert.DoesNotContain(restantes, p => p.Id == primeros[0].Id || p.Id == primeros[1].Id);
        var reprogramado = (await cola.Vencidos(Ahora.AddHours(2), 10)).Single(p => p.Id == primeros[1].Id);
        Assert.Equal(4, reprogramado.Intentos);
    }

    [Fact]
    public async Task UnaFilaQueNoSeDescifra_SeOmite_SinDetenerLasDemas()
    {
        await bd.Ejecutar("DELETE FROM plataforma.correos_pendientes");
        var cola = new PostgresColaDeCorreosPendientes(bd.Unidad, Cifrador(), new RelojDePrueba());
        var otraClave = new PostgresColaDeCorreosPendientes(bd.Unidad, Cifrador(), new RelojDePrueba());
        await otraClave.Agregar(Correo(CategoriaDeCorreo.Pago), 2, Ahora.AddMinutes(-1));
        await cola.Agregar(Correo(CategoriaDeCorreo.AlertaCriticaDeCuenta, "contrasena-cambiada"), 1, Ahora.AddMinutes(-1));

        var turno = await cola.Vencidos(Ahora, 10);

        Assert.Equal(CategoriaDeCorreo.AlertaCriticaDeCuenta, Assert.Single(turno).Correo.Categoria);
        Assert.Equal(2, await bd.Consultar<long>("SELECT count(*) FROM plataforma.correos_pendientes"));
    }

    [Theory]
    [InlineData(CategoriaDeCorreo.AvisoDeSeguridad)]
    [InlineData(CategoriaDeCorreo.CodigoDeVerificacionORecuperacion)]
    public async Task SoloSeEncolanAlertasReclamosYPagos(CategoriaDeCorreo categoria)
    {
        var cola = new PostgresColaDeCorreosPendientes(bd.Unidad, Cifrador(), new RelojDePrueba());
        var correo = categoria == CategoriaDeCorreo.CodigoDeVerificacionORecuperacion ? null : new CorreoSaliente(categoria, "ana@example.com", "cancelacion-diferida");

        if (correo is null)
        {
            Assert.Throws<ArgumentException>(() => new CorreoSaliente(categoria, "ana@example.com", "codigo"));
            return;
        }

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => cola.Agregar(correo, 2, Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-60")]
    public async Task MasFilasIlegiblesQueElTurno_NoBloqueanLasDemas_YSeApartanUnDia()
    {
        await bd.Ejecutar("DELETE FROM plataforma.correos_pendientes");
        var cola = new PostgresColaDeCorreosPendientes(bd.Unidad, Cifrador(), new RelojDePrueba());
        var otraClave = new PostgresColaDeCorreosPendientes(bd.Unidad, Cifrador(), new RelojDePrueba());
        for (var i = 0; i < 5; i++)
        {
            await otraClave.Agregar(Correo(CategoriaDeCorreo.AlertaALaAdministracion), 0, Ahora.AddMinutes(-5));
        }

        await cola.Agregar(Correo(CategoriaDeCorreo.Pago), 2, Ahora.AddMinutes(-1));

        Assert.Empty(await cola.Vencidos(Ahora, 3));
        var segundaPasada = await cola.Vencidos(Ahora, 3);

        Assert.Equal(CategoriaDeCorreo.Pago, Assert.Single(segundaPasada).Correo.Categoria);
        // Se compara con el reloj de la prueba (`Ahora`, fijo), no con `now()` de la base: de lo contrario la aserción depende del día en que corre.
        var umbral = Ahora.AddHours(10).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(5, await bd.Consultar<long>($"SELECT count(*) FROM plataforma.correos_pendientes WHERE intentos = 1 AND proximo_intento > '{umbral}'::timestamptz"));
    }

    [Fact]
    public async Task LaPurga_QuitaLoQueSuperoLosTreintaDiasOLosIntentosMaximos_YConservaLoDemas()
    {
        await bd.Ejecutar("DELETE FROM plataforma.correos_pendientes");
        var cola = new PostgresColaDeCorreosPendientes(bd.Unidad, Cifrador(), new RelojDePrueba());
        await cola.Agregar(Correo(CategoriaDeCorreo.Pago), 2, Ahora);
        await cola.Agregar(Correo(CategoriaDeCorreo.Pago), 2, Ahora);
        await cola.Agregar(Correo(CategoriaDeCorreo.Pago), 2, Ahora);
        var todos = await cola.Vencidos(Ahora.AddMinutes(5), 10);
        await cola.Reprogramar(todos[0].Id, PostgresColaDeCorreosPendientes.MaximoDeIntentos, Ahora);
        await bd.Ejecutar($"UPDATE plataforma.correos_pendientes SET creada = now() - interval '40 days' WHERE id = '{todos[1].Id}'");

        var quitadas = await cola.PurgarCaducados(DateTimeOffset.UtcNow);

        Assert.Equal(2, quitadas);
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM plataforma.correos_pendientes"));
        Assert.DoesNotContain(await cola.Vencidos(Ahora.AddMinutes(5), 10), p => p.Id == todos[0].Id);
    }

    [Fact]
    public async Task CambiarLaPlantillaOLaCategoriaDeUnaFila_LaVuelveIlegible()
    {
        await bd.Ejecutar("DELETE FROM plataforma.correos_pendientes");
        var cola = new PostgresColaDeCorreosPendientes(bd.Unidad, Cifrador(), new RelojDePrueba());
        await cola.Agregar(Correo(CategoriaDeCorreo.Pago), 2, Ahora.AddMinutes(-1));
        await bd.Ejecutar("UPDATE plataforma.correos_pendientes SET plantilla = 'otra-plantilla'");

        Assert.Empty(await cola.Vencidos(Ahora, 10));
    }

    [Fact]
    public async Task ElRolDeLaAplicacion_SoloActualizaLosIntentosYElProximoIntento_NuncaElContenido()
    {
        await using var conexion = await bd.Administracion.OpenConnectionAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();
        await using (var cambio = new NpgsqlCommand("SET LOCAL ROLE rol_plataforma", conexion, transaccion))
        {
            await cambio.ExecuteNonQueryAsync();
        }

        await using var contenido = new NpgsqlCommand("UPDATE plataforma.correos_pendientes SET contenido = decode(repeat('aa', 40), 'hex')", conexion, transaccion);
        var error = await Assert.ThrowsAsync<PostgresException>(() => contenido.ExecuteNonQueryAsync());

        Assert.Equal("42501", error.SqlState);
    }

    [Fact]
    public async Task ElRolDeLaAplicacion_NoPuedeVaciarLaColaNiLaCuota()
    {
        await using var conexion = await bd.Administracion.OpenConnectionAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();
        await using (var cambio = new NpgsqlCommand("SET LOCAL ROLE rol_plataforma", conexion, transaccion))
        {
            await cambio.ExecuteNonQueryAsync();
        }

        await using var truncar = new NpgsqlCommand("TRUNCATE plataforma.correos_pendientes", conexion, transaccion);
        var error = await Assert.ThrowsAsync<PostgresException>(() => truncar.ExecuteNonQueryAsync());

        Assert.Equal("42501", error.SqlState);
    }

    // ---------------------------------------------------------------- el cifrado de la cola

    [Fact]
    public void ElCifrado_VuelveAlContenido_ConUnNonceDistinto_YLigadoAlIdentificador()
    {
        var cifrador = Cifrador();
        var id = Guid.NewGuid();
        var contenido = System.Text.Encoding.UTF8.GetBytes("{\"destinatario\":\"ana@example.com\"}");

        var uno = cifrador.Cifrar(contenido, id);
        var otro = cifrador.Cifrar(contenido, id);

        Assert.NotEqual(uno, otro);
        Assert.Equal(contenido, cifrador.Descifrar(uno, id));
        Assert.ThrowsAny<CryptographicException>(() => cifrador.Descifrar(uno, Guid.NewGuid()));
        var alterado = (byte[])uno.Clone();
        alterado[^1] ^= 0x01;
        Assert.ThrowsAny<CryptographicException>(() => cifrador.Descifrar(alterado, id));
        Assert.ThrowsAny<CryptographicException>(() => cifrador.Descifrar(uno.AsSpan(0, 10), id));
        Assert.ThrowsAny<CryptographicException>(() => Cifrador().Descifrar(uno, id));
        Assert.Throws<ArgumentException>(() => new CifradorDeLaColaDeCorreo(new byte[16]));
    }

    // ---------------------------------------------------------------- el transporte SMTP (sin red)

    private static OpcionesDelCorreoSmtp Opciones(string servidor = "smtp.example.com", int puerto = 587) =>
        new(servidor, puerto, "cuenta@example.com", Guid.NewGuid().ToString("N"), "no-responder@example.com");

    [Theory]
    [InlineData("")]
    [InlineData("con espacio.example.com")]
    public void ElServidorSmtpDebeSerUnHost(string servidor)
    {
        Assert.Throws<ArgumentException>(() => Opciones(servidor));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void ElPuertoDebeSerValido(int puerto)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Opciones(puerto: puerto));
    }

    [Fact]
    public void ElRemitenteYLaCredencial_SeValidan_YLaConfiguracionNoSeImprime()
    {
        Assert.Throws<ArgumentException>(() => new OpcionesDelCorreoSmtp("smtp.example.com", 587, "cuenta@example.com", Guid.NewGuid().ToString("N"), "no es una direccion"));
        Assert.Throws<ArgumentException>(() => new OpcionesDelCorreoSmtp("smtp.example.com", 587, "cuenta@example.com", " ", "a@example.com"));
        Assert.Equal("[configuración del correo]", Opciones().ToString());
    }

    [Theory]
    [InlineData("ana@example.com, otra@example.com")]
    [InlineData("ana@example.com\r\nBcc: espia@example.com")]
    [InlineData("no es una direccion")]
    [InlineData("")]
    public async Task UnDestinatarioInvalido_SeRechazaAntesDeTocarLaRed(string destinatario)
    {
        var transporte = new SmtpTransporteDeCorreo(Opciones(servidor: "invalido.invalid"));

        await Assert.ThrowsAsync<ArgumentException>(() => transporte.Enviar(new MensajeDeCorreo([destinatario], "Asunto", "Cuerpo")));
        await Assert.ThrowsAsync<ArgumentException>(() => transporte.Enviar(new MensajeDeCorreo([], "Asunto", "Cuerpo")));
    }

    [Fact]
    public async Task SinCuentaConfigurada_ElTransporteFallaCerrado_NuncaFingeEntregar()
    {
        var transporte = new TransporteDeCorreoNoConfigurado();

        await Assert.ThrowsAsync<InvalidOperationException>(() => transporte.Enviar(new MensajeDeCorreo(["ana@example.com"], "Asunto", "Cuerpo")));
    }

    [Fact]
    public async Task LaLimpiezaDeEnviados_ConLaBandejaDeEntradaComoCarpeta_SeRechazaAntesDeConectar()
    {
        var opciones = new OpcionesDelCorreoSmtp("smtp.example.com", 587, "cuenta@example.com", Guid.NewGuid().ToString("N"), "no-responder@example.com") { ServidorImap = "imap.invalid", CarpetaDeEnviados = "inbox" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => new LimpiezaDeEnviadosImap(opciones).Vaciar(Ahora));
    }

    [Fact]
    public async Task LaLimpiezaDeEnviadosSinImapConfigurado_NoHaceNada()
    {
        Assert.Equal(0, await new LimpiezaDeEnviadosImap(Opciones()).Vaciar(Ahora));
        Assert.Equal(TimeSpan.FromDays(7), LimpiezaDeEnviadosImap.Antiguedad);
    }
}

/// <summary>El canal en memoria de los códigos (T34; IDE-65, AC-IDE-54): acotado, no persistente y sin reintento; si se llena, descarta y lo dice.</summary>
public sealed class CanalDeCodigosEnMemoriaTests
{
    private static SolicitudDeCodigoEnSegundoPlano Solicitud(string correo) =>
        new(correo, BabelHub.Ecommerce.Domain.Identidad.PropositoDeCodigo.RecuperacionDeAcceso);

    [Fact]
    [Trait("Criterio", "AC-IDE-54")]
    public async Task ElCanalLleno_DescartaYDevuelveFalso_YLoEncoladoSaleEnOrden()
    {
        var canal = new CanalDeCodigosEnMemoria(capacidad: 3);

        var resultados = new[] { "a", "b", "c", "d" }.Select(c => canal.Encolar(Solicitud($"{c}@example.com"))).ToList();

        Assert.Equal([true, true, true, false], resultados);
        canal.Completar();
        var salieron = new List<string>();
        await foreach (var s in canal.Lector.ReadAllAsync())
        {
            salieron.Add(s.CorreoNormalizado);
        }

        Assert.Equal(["a@example.com", "b@example.com", "c@example.com"], salieron);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CanalDeCodigosEnMemoria(0));
    }
}

/// <summary>Los consumidores del canal de códigos (T34; IDE-65, AC-IDE-54): cada solicitud corre en su alcance y como «sistema», y un fallo no detiene a las demás.</summary>
public sealed class ServicioDeCodigosEnSegundoPlanoTests
{
    private sealed class ProveedorVacio : IServiceProvider, IServiceScopeFactory, IServiceScope
    {
        public IServiceProvider ServiceProvider => this;

        public object? GetService(Type serviceType) => serviceType == typeof(IServiceScopeFactory) ? this : null;

        public IServiceScope CreateScope() => this;

        public void Dispose()
        {
        }
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-54")]
    public async Task UnaSolicitudQueFalla_NoDetieneLasDemas_YCadaUnaCorreComoSistemaEnSuAlcance()
    {
        var canal = new CanalDeCodigosEnMemoria(10);
        var proveedor = new ProveedorVacio();
        var procesadas = new List<string>();
        var alcances = 0;
        var servicio = new ServicioDeCodigosEnSegundoPlano(
            canal,
            proveedor,
            async (corrida, ct) =>
            {
                alcances++;
                await corrida(ct);
            },
            1,
            (_, solicitud, _) =>
            {
                if (solicitud.CorreoNormalizado.StartsWith("falla", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Se cayó.");
                }

                procesadas.Add(solicitud.CorreoNormalizado);
                return Task.CompletedTask;
            });
        foreach (var correo in new[] { "uno@example.com", "falla@example.com", "dos@example.com" })
        {
            canal.Encolar(new SolicitudDeCodigoEnSegundoPlano(correo, BabelHub.Ecommerce.Domain.Identidad.PropositoDeCodigo.VerificacionDeCorreo));
        }

        canal.Completar();
        await servicio.Consumir(CancellationToken.None);

        Assert.Equal(["uno@example.com", "dos@example.com"], procesadas);
        Assert.Equal(3, alcances);
    }
}
