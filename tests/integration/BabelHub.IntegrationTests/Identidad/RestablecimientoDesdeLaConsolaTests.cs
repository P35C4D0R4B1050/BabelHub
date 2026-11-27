using System.Collections.Concurrent;
using System.Security.Cryptography;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Identidad;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Migracion;
using BabelHub.IntegrationTests.Api;
using BabelHub.IntegrationTests.Despliegue;
using BabelHub.IntegrationTests.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// El restablecimiento del segundo factor de la única administración desde la consola (identidad-recuperacion-del-segundo-factor T02, T06 y T07; AC-RSF-01, AC-RSF-02, AC-RSF-05, AC-RSF-07, AC-RSF-09, AC-RSF-10, AC-RSF-11) contra una base <b>migrada de verdad</b> y
/// el servicio compuesto: sus efectos en las tablas, la auditoría en la misma transacción, las solicitudes simultáneas y lo que la consola dice.
/// </summary>
public sealed class RestablecimientoDesdeLaConsolaTests(InstanciaParaMigrar instancia) : IClassFixture<InstanciaParaMigrar>
{
    private const string Correo = "admin.unica@ejemplo.pe";
    private const string Accion = "SegundoFactorRestablecidoPorAdministracion";

    private static string Secreto() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(36));

    private static ValorSecreto Frase() => new("Una-contrasena-nueva-9#xZ" + Guid.NewGuid().ToString("N")[..8]);

    // ---------- Efectos ----------

    [Fact]
    [Trait("Criterio", "AC-RSF-01")]
    [Trait("Criterio", "AC-RSF-07")]
    public async Task LaUnicaAdministracion_PierdeSuSegundoFactor_SesionesEIngresos_YQuedaAuditadoSinSecretos()
    {
        var m = await Montar();
        await using var _ = m.Servicio;
        var admin = await m.CrearLaAdministracion();
        var aMedias = (await m.Ingresar(Correo, admin.Contrasena)).Valor.Desafio!;
        var resumen = await m.Texto("SELECT hash_contrasena || '|' || rol || '|' || activo::text FROM identidad.usuarios");

        var (codigo, salida, error) = await m.Pedir("Production");

        Assert.Equal((0, string.Empty), (codigo, error));
        Assert.Contains("se restableció el segundo factor", salida, StringComparison.Ordinal);
        Assert.Contains("El aviso por correo salió", salida, StringComparison.Ordinal);
        Assert.Contains("configura el segundo factor", salida, StringComparison.Ordinal);
        Assert.Equal(0L, await m.Numero("SELECT count(*) FROM identidad.segundo_factor"));
        Assert.Equal(0L, await m.Numero("SELECT count(*) FROM identidad.codigos_de_respaldo"));
        Assert.Equal(0L, await m.Numero("SELECT count(*) FROM identidad.desafios_de_ingreso WHERE NOT usado"));
        Assert.Equal(0L, await m.Numero("SELECT count(*) FROM identidad.renovaciones WHERE revocada_en IS NULL"));
        Assert.Equal(resumen, await m.Texto("SELECT hash_contrasena || '|' || rol || '|' || activo::text FROM identidad.usuarios"));
        Assert.False((await m.Renovar(admin.Renovacion)).EsExito);

        // Auditoría: una sola fila, actor «sistema», sin correo ni secretos.
        Assert.Equal(1L, await m.Numero($"SELECT count(*) FROM identidad.registros WHERE accion = '{Accion}'"));
        var fila = await m.Texto($"SELECT row_to_json(r)::text FROM identidad.registros r WHERE accion = '{Accion}'");
        Assert.Contains("sistema", fila, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(admin.UsuarioId.ToString("D"), fila, StringComparison.Ordinal);
        foreach (var prohibido in new[] { Correo, admin.Contrasena.Revelar(), admin.Secreto, admin.Respaldo[0] })
        {
            Assert.DoesNotContain(prohibido, fila + salida, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(prohibido, string.Join('\n', m.Registros), StringComparison.OrdinalIgnoreCase);
        }

        // El aviso por correo, sin datos sensibles.
        var aviso = Assert.Single(m.Correos, c => c.Asunto.Contains("verificación en dos pasos", StringComparison.Ordinal));
        Assert.Equal([Correo], aviso.Destinatarios);
        Assert.DoesNotContain(admin.Secreto, aviso.Cuerpo, StringComparison.Ordinal);

        // El siguiente ingreso obliga a configurar; recibe 8 códigos; el ingreso empezado antes no se completa ni con el factor nuevo.
        var siguiente = (await m.Ingresar(Correo, admin.Contrasena)).Valor;
        Assert.Null(siguiente.Credencial);
        Assert.Equal(PasoPendienteDelIngreso.ConfigurarSegundoFactor, siguiente.Desafio!.Paso);
        var (secretoNuevo, codigos) = await m.Configurar(siguiente.Desafio.Identificador);
        Assert.Equal(8, codigos.Count);
        var viejo = await m.Con(s => s.GetRequiredService<IIngreso>().PresentarSegundoFactor(aMedias.Identificador, new ValorSecreto(CodigoTotp(secretoNuevo, pasos: 1))));
        Assert.False(viejo.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-01")]
    public async Task LosDesafiosDeLosTresModos_QuedanAnulados_YLosDeOtraCuentaNo()
    {
        var m = await Montar();
        await using var _ = m.Servicio;
        var admin = await m.CrearLaAdministracion();
        foreach (var (modo, indice) in new[] { "presentar-segundo-factor", "configurar-segundo-factor", "cambiar-contrasena-temporal" }.Select((v, i) => (v, i)))
        {
            await m.Ejecutar($"""
                INSERT INTO identidad.desafios_de_ingreso (id, huella, usuario_id, modo, creado_en, vence_en, usado)
                VALUES (gen_random_uuid(), '{new string((char)('a' + indice), 64)}', '{admin.UsuarioId:D}', '{modo}', now(), now() + interval '5 minutes', false)
                """);
        }

        Assert.Equal(0, (await m.Pedir("Staging")).Codigo);

        Assert.Equal(0L, await m.Numero("SELECT count(*) FROM identidad.desafios_de_ingreso WHERE NOT usado"));
    }

    // ---------- Otra administración ----------

    [Fact]
    [Trait("Criterio", "AC-RSF-05")]
    public async Task ConOtraAdministracionQuePuedeIngresar_AunBloqueada_SeNiega_YConSuTemporalVencidaSeAplica()
    {
        var m = await Montar();
        await using var _ = m.Servicio;
        var admin = await m.CrearLaAdministracion();
        var otra = await m.CrearOtraAdministracion(admin.UsuarioId, "admin.segunda@ejemplo.pe");
        for (var i = 0; i < 6; i++)
        {
            await m.Ingresar("admin.segunda@ejemplo.pe", new ValorSecreto("una contraseña que no es"));
        }

        var negada = await m.Pedir("Production");

        Assert.Equal(1, negada.Codigo);
        Assert.Contains("hay otra administración que puede ingresar", negada.Error, StringComparison.Ordinal);
        Assert.Equal(1L, await m.Numero($"SELECT count(*) FROM identidad.segundo_factor WHERE usuario_id = '{admin.UsuarioId:D}'"));
        Assert.Equal(0L, await m.Numero($"SELECT count(*) FROM identidad.registros WHERE accion = '{Accion}'"));

        await m.Ejecutar($"UPDATE identidad.usuarios SET contrasena_temporal_hasta = now() - interval '1 minute' WHERE id = '{otra:D}'");

        Assert.Equal(0, (await m.Pedir("Production")).Codigo);
        Assert.Equal(0L, await m.Numero($"SELECT count(*) FROM identidad.segundo_factor WHERE usuario_id = '{admin.UsuarioId:D}'"));
    }

    // ---------- Fallas ----------

    [Fact]
    [Trait("Criterio", "AC-RSF-07")]
    [Trait("Criterio", "AC-RSF-09")]
    public async Task SiLaAuditoriaNoPuedeRegistrar_NoSeAplicaNada_NiSeAvisa()
    {
        var interruptor = new Interruptor();
        var m = await Montar(auditoria: interruptor);
        await using var _ = m.Servicio;
        var admin = await m.CrearLaAdministracion();
        var aMedias = (await m.Ingresar(Correo, admin.Contrasena)).Valor.Desafio!;
        var correosAntes = m.Correos.Count;
        interruptor.Falla = true;

        var (codigo, salida, error) = await m.Pedir("Production");

        Assert.Equal((1, string.Empty), (codigo, salida));
        Assert.Contains("no se completó o no se pudo confirmar", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", error, StringComparison.Ordinal);
        Assert.DoesNotContain("falla de prueba", error + string.Join('\n', m.Registros), StringComparison.Ordinal);
        Assert.Equal(1L, await m.Numero("SELECT count(*) FROM identidad.segundo_factor"));
        Assert.Equal(8L, await m.Numero("SELECT count(*) FROM identidad.codigos_de_respaldo"));
        Assert.Equal(1L, await m.Numero("SELECT count(*) FROM identidad.renovaciones WHERE revocada_en IS NULL"));
        Assert.Equal(1L, await m.Numero("SELECT count(*) FROM identidad.desafios_de_ingreso WHERE NOT usado"));
        Assert.Equal(correosAntes, m.Correos.Count);
        interruptor.Falla = false;
        var presentado = await m.Con(s => s.GetRequiredService<IIngreso>().PresentarSegundoFactor(aMedias.Identificador, new ValorSecreto(CodigoTotp(admin.Secreto, pasos: 1))));
        Assert.True(presentado.EsExito, presentado.Error?.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-11")]
    public async Task SiLosDatosNoEstanDisponibles_NoCambiaNada_YAlRepetirSeAplica()
    {
        var m = await Montar();
        await using var _ = m.Servicio;
        await m.CrearLaAdministracion();
        var sinBase = new Dictionary<string, string?>(m.Configuracion);
        sinBase[OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeLaAplicacion] =
            new NpgsqlConnectionStringBuilder(sinBase[OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeLaAplicacion]) { Port = 1, Timeout = 3 }.ConnectionString;
        await using var caido = Levantar(sinBase, m.Registros, m.Correos, null, null);

        var fallida = await Pedir(caido, "Production", Correo);

        Assert.Equal(1, fallida.Codigo);
        Assert.Contains("repite la orden", fallida.Error, StringComparison.Ordinal);
        Assert.Equal(1L, await m.Numero("SELECT count(*) FROM identidad.segundo_factor"));
        Assert.Equal(0, (await m.Pedir("Production")).Codigo);
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-09")]
    public async Task ElAvisoQueNoSale_SeDice_YLaOrdenTerminaConExito()
    {
        var m = await Montar();
        await using var _ = m.Servicio;
        await m.CrearLaAdministracion();
        m.TransporteCaido.Falla = true;

        var (codigo, salida, _) = await m.Pedir("Production");

        Assert.Equal(0, codigo);
        Assert.Contains("El aviso por correo no salió en este momento", salida, StringComparison.Ordinal);
        Assert.Equal(0L, await m.Numero("SELECT count(*) FROM identidad.segundo_factor"));
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-09")]
    public async Task CadaRechazo_DiceElMotivo_SinRepetirElCorreo_YTerminaSinExito()
    {
        var m = await Montar();
        await using var _ = m.Servicio;
        await m.CrearLaAdministracion();

        var sinCorreo = await Pedir(m.Servicio, "Production", null);
        var desconocido = await Pedir(m.Servicio, "Production", "nadie.de.nadie@ejemplo.pe");
        await m.Pedir("Production");
        var repetida = await m.Pedir("Production");

        Assert.Equal((1, 1, 1), (sinCorreo.Codigo, desconocido.Codigo, repetida.Codigo));
        Assert.Contains("falta el correo", sinCorreo.Error, StringComparison.Ordinal);
        Assert.Contains("no hay una cuenta de administración central activa", desconocido.Error, StringComparison.Ordinal);
        Assert.Contains("no tiene un segundo factor activado", repetida.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("nadie.de.nadie", desconocido.Error + string.Join('\n', m.Registros), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1L, await m.Numero($"SELECT count(*) FROM identidad.registros WHERE accion = '{Accion}'"));
    }

    // ---------- Solicitudes simultáneas ----------

    [Fact]
    [Trait("Criterio", "AC-RSF-10")]
    public async Task DosOrdenesALaVez_SoloUnaRestablece_YHayUnaSolaFilaDeAuditoria()
    {
        var m = await Montar();
        await using var _ = m.Servicio;
        await m.CrearLaAdministracion();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => m.Pedir("Production"))));

        Assert.Equal([0, 1], resultados.Select(r => r.Codigo).Order());
        Assert.Contains("no tiene un segundo factor activado", resultados.Single(r => r.Codigo == 1).Error, StringComparison.Ordinal);
        Assert.Equal(1L, await m.Numero($"SELECT count(*) FROM identidad.registros WHERE accion = '{Accion}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-10")]
    public async Task UnaReactivacionDeOtraAdministracionSinConfirmar_HaceEsperarALaOrden_YLaNiega()
    {
        var m = await Montar();
        await using var _ = m.Servicio;
        var admin = await m.CrearLaAdministracion();
        var otra = await m.CrearOtraAdministracion(admin.UsuarioId, "admin.segunda@ejemplo.pe");
        await m.Ejecutar($"UPDATE identidad.usuarios SET activo = false WHERE id = '{otra:D}'");
        await using var conexion = new NpgsqlConnection(m.Propietaria);
        await conexion.OpenAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();
        await using (var reactivar = new NpgsqlCommand($"UPDATE identidad.usuarios SET activo = true WHERE id = '{otra:D}'", conexion, transaccion))
        {
            await reactivar.ExecuteNonQueryAsync();
        }

        var orden = Task.Run(() => m.Pedir("Production"));
        await m.EsperarUnBloqueo();
        await transaccion.CommitAsync();
        var resultado = await orden;

        Assert.Equal(1, resultado.Codigo);
        Assert.Contains("hay otra administración que puede ingresar", resultado.Error, StringComparison.Ordinal);
        Assert.Equal(1L, await m.Numero($"SELECT count(*) FROM identidad.segundo_factor WHERE usuario_id = '{admin.UsuarioId:D}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-10")]
    public async Task UnIngresoQueCompruebaLaContrasenaMientrasCorreLaOrden_NoDejaDesafioNiSesion()
    {
        var pausa = new Interruptor();
        var m = await Montar(auditoria: pausa);
        await using var _ = m.Servicio;
        var admin = await m.CrearLaAdministracion();
        pausa.Retener();
        var orden = Task.Run(() => m.Pedir("Production"));
        await pausa.Alcanzado;

        // La orden ya borró el factor pero no confirmó: el ingreso lee los datos anteriores y espera el bloqueo de la cuenta.
        var ingreso = Task.Run(() => m.Ingresar(Correo, admin.Contrasena));
        await m.EsperarUnBloqueo();
        pausa.Soltar();

        Assert.Equal(0, (await orden).Codigo);
        Assert.False((await ingreso).EsExito);
        Assert.Equal(0L, await m.Numero("SELECT count(*) FROM identidad.desafios_de_ingreso WHERE NOT usado"));
        Assert.Equal(0L, await m.Numero("SELECT count(*) FROM identidad.renovaciones WHERE revocada_en IS NULL"));
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-10")]
    public async Task UnIngresoQuePresentaSuCodigoMientrasCorreLaOrden_NoDejaNingunaSesionUtilizable()
    {
        var pausa = new Interruptor();
        var m = await Montar(auditoria: pausa);
        await using var _ = m.Servicio;
        var admin = await m.CrearLaAdministracion();
        var desafio = (await m.Ingresar(Correo, admin.Contrasena)).Valor.Desafio!;
        pausa.Retener();
        var orden = Task.Run(() => m.Pedir("Production"));
        await pausa.Alcanzado;

        var codigo = Task.Run(() => m.Con(s => s.GetRequiredService<IIngreso>().PresentarSegundoFactor(desafio.Identificador, new ValorSecreto(CodigoTotp(admin.Secreto, pasos: 1)))));
        await m.EsperarUnBloqueo();
        pausa.Soltar();
        var primero = await orden;

        // La base puede abortar una de las dos por interbloqueo: en ese caso la orden no cambia nada y se repite (RSF-15).
        if (primero.Codigo != 0)
        {
            await Esperar(codigo);
            Assert.Equal(0, (await m.Pedir("Production")).Codigo);
        }
        else
        {
            await Esperar(codigo);
        }

        Assert.Equal(0L, await m.Numero("SELECT count(*) FROM identidad.renovaciones WHERE revocada_en IS NULL"));
        Assert.Equal(0L, await m.Numero("SELECT count(*) FROM identidad.segundo_factor"));

        static async Task Esperar(Task tarea)
        {
            try
            {
                await tarea;
            }
            catch (NpgsqlException)
            {
                // El ingreso puede ser la víctima del interbloqueo: su transacción no dejó efectos.
            }
        }
    }

    // ---------- El modo de consola ----------

    [Theory]
    [Trait("Criterio", "AC-RSF-02")]
    [InlineData("Development", null)]
    [InlineData("Test", null)]
    [InlineData("Production", "Ensayo")]
    [InlineData("Production", "Restauracion")]
    [InlineData("Staging", "Operacion")]
    [InlineData("Production", "otro")]
    public async Task LaOrden_SeNiegaFueraDeLosEntornosYModosAdmitidos_AntesDeConstruirElServicio(string entorno, string? modo)
    {
        var (salida, error) = (new StringWriter(), new StringWriter());

        // Sin configuración alguna: si llegara a construir el servicio, el mensaje sería otro.
        var codigo = await ModoRestablecerSegundoFactor.Ejecutar(["--correo", Correo], salida, error, entorno, modo ?? "Normal");

        Assert.Equal(1, codigo);
        Assert.Contains("solo existe en Staging y Production, en el modo normal", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("no se hizo nada", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Correo, error.ToString() + salida, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [Trait("Criterio", "AC-RSF-09")]
    [InlineData("--contrasena", "valor-que-no-debe-repetirse")]
    [InlineData("valor-que-no-debe-repetirse", null)]
    [InlineData("--correo", null)]
    public async Task UnaOpcionDesconocidaOSinValor_NoRepiteLoEscrito(string primero, string? segundo)
    {
        var (salida, error) = (new StringWriter(), new StringWriter());

        var codigo = await ModoRestablecerSegundoFactor.Ejecutar(segundo is null ? [primero] : [primero, segundo], salida, error, "Production", "Normal");

        Assert.Equal(1, codigo);
        Assert.Contains("Uso: restablecer-segundo-factor --correo", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("valor-que-no-debe-repetirse", error.ToString() + salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-09")]
    public async Task SinLaConfiguracionDelServicio_FallaDiciendoloSinImprimirElCorreo()
    {
        var (salida, error) = (new StringWriter(), new StringWriter());

        var codigo = await ModoRestablecerSegundoFactor.Ejecutar(["--correo", Correo], salida, error, "Production", "Normal");

        Assert.Equal(1, codigo);
        Assert.Equal(string.Empty, salida.ToString());
        Assert.DoesNotContain(Correo, error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-02")]
    public void SoloLaOrdenDeConsola_UsaElRestablecimiento()
    {
        var origen = Path.Combine(RaizDelRepositorio.Raiz, "src", "BabelHub.Ecommerce");

        var queLoNombran = Directory.EnumerateFiles(origen, "*.cs", SearchOption.AllDirectories)
            .Where(ruta => !ruta.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !ruta.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(ruta => File.ReadAllText(ruta).Contains("IRestablecimientoDesdeLaInstalacion", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            ["ComposicionDeIdentidad.cs", "ContratoDelRestablecimientoDesdeLaInstalacion.cs", "ModoRestablecerSegundoFactor.cs", "RestablecimientoDelSegundoFactorDesdeLaInstalacion.cs"],
            queLoNombran);
    }

    // ---------- Piezas ----------

    private static string CodigoTotp(string secretoBase32, int pasos = 0)
    {
        const string alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in secretoBase32)
        {
            buffer = (buffer << 5) | alfabeto.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        return Totp.Codigo([.. bytes], Totp.PasoDe(DateTimeOffset.UtcNow) + pasos);
    }

    private sealed record Admin(Guid UsuarioId, ValorSecreto Contrasena, string Secreto, IReadOnlyList<string> Respaldo, ValorSecreto Renovacion);

    private sealed class Montaje(WebApplication servicio, string propietaria, Dictionary<string, string?> configuracion, ConcurrentQueue<string> registros, List<MensajeDeCorreo> correos, Interruptor transporte)
    {
        public WebApplication Servicio => servicio;

        public string Propietaria => propietaria;

        public Dictionary<string, string?> Configuracion => configuracion;

        public ConcurrentQueue<string> Registros => registros;

        public List<MensajeDeCorreo> Correos => correos;

        public Interruptor TransporteCaido => transporte;

        public async Task<T> Con<T>(Func<IServiceProvider, Task<T>> operacion)
        {
            await using var alcance = servicio.Services.CreateAsyncScope();
            // Como en la web: quien ingresa es un visitante anónimo; quien necesite otro actor lo fija dentro.
            alcance.ServiceProvider.GetRequiredService<IEstablecedorDeActor>().Establecer(ActorActual.Anonimo());
            return await operacion(alcance.ServiceProvider);
        }

        public Task<(int Codigo, string Salida, string Error)> Pedir(string entorno) => RestablecimientoDesdeLaConsolaTests.Pedir(servicio, entorno, Correo);

        public Task<Resultado<ResultadoDelIngreso>> Ingresar(string correo, ValorSecreto contrasena) => Con(s => s.GetRequiredService<IIngreso>().Ingresar(new SolicitudDeIngreso(correo, contrasena)));

        public Task<Resultado<CredencialDeSesion>> Renovar(ValorSecreto renovacion) => Con(s => s.GetRequiredService<ISesiones>().Renovar(renovacion));

        public async Task<(string Secreto, IReadOnlyList<string> Codigos)> Configurar(ValorSecreto desafio)
        {
            var secreto = (await Con(s => s.GetRequiredService<IIngreso>().IniciarConfiguracionDelSegundoFactor(desafio))).Valor.SecretoBase32.Revelar();
            var confirmado = await Con(s => s.GetRequiredService<IIngreso>().ConfirmarElSegundoFactor(desafio, new ValorSecreto(CodigoTotp(secreto))));
            Assert.True(confirmado.EsExito, confirmado.Error?.Clave);
            return (secreto, [.. confirmado.Valor.CodigosDeRespaldo.Select(c => c.Revelar())]);
        }

        /// <summary>El primer administrador con su primer ingreso completo: contraseña propia, segundo factor y una sesión abierta.</summary>
        public async Task<Admin> CrearLaAdministracion()
        {
            var creado = await Con(s =>
            {
                s.GetRequiredService<IEstablecedorDeActor>().Establecer(ActorActual.DelSistema());
                return s.GetRequiredService<PrimerAdministradorDeLaInstalacion>().Crear("Production", Correo, nombre: null);
            });
            var temporal = (await Ingresar(Correo, creado.ContrasenaTemporal!)).Valor.Desafio!;
            var propia = Frase();
            var cambiada = await Con(s => s.GetRequiredService<IIngreso>().CambiarLaContrasenaTemporal(temporal.Identificador, propia));
            Assert.True(cambiada.EsExito, cambiada.Error?.Clave);
            var desafio = cambiada.Valor.Desafio!.Identificador;
            var secreto = (await Con(s => s.GetRequiredService<IIngreso>().IniciarConfiguracionDelSegundoFactor(desafio))).Valor.SecretoBase32.Revelar();
            // Se confirma con el código del paso anterior para que los pasos siguientes sigan disponibles en la prueba.
            var confirmada = await Con(s => s.GetRequiredService<IIngreso>().ConfirmarElSegundoFactor(desafio, new ValorSecreto(CodigoTotp(secreto, pasos: -1))));
            Assert.True(confirmada.EsExito, confirmada.Error?.Clave);
            return new Admin(creado.UsuarioId, propia, secreto, [.. confirmada.Valor.CodigosDeRespaldo.Select(c => c.Revelar())], confirmada.Valor.Credencial!.Renovacion);
        }

        public async Task<Guid> CrearOtraAdministracion(Guid quien, string correo)
        {
            var creada = await Con(s =>
            {
                s.GetRequiredService<IEstablecedorDeActor>().Establecer(ActorActual.DeUsuario(quien, "administrador-central"));
                return s.GetRequiredService<IAdministracionDeUsuarios>().Crear(quien, "Segunda administración", correo, RolDeCuenta.AdministradorCentral, null);
            });
            Assert.True(creada.EsExito, creada.Error?.Clave);
            return creada.Valor.Usuario.UsuarioId;
        }

        public async Task<long> Numero(string sql) => (long)(await Escalar(sql))!;

        public async Task<string> Texto(string sql) => (string)(await Escalar(sql))!;

        public async Task Ejecutar(string sql)
        {
            await using var origen = NpgsqlDataSource.Create(propietaria);
            await using var comando = origen.CreateCommand(sql);
            await comando.ExecuteNonQueryAsync();
        }

        /// <summary>Espera a que alguna conexión de esta base quede esperando un bloqueo (la otra operación ya llegó a la fila).</summary>
        public async Task EsperarUnBloqueo()
        {
            using var plazo = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (await Numero("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'") == 0)
            {
                await Task.Delay(50, plazo.Token);
            }
        }

        private async Task<object?> Escalar(string sql)
        {
            await using var origen = NpgsqlDataSource.Create(propietaria);
            await using var comando = origen.CreateCommand(sql);
            return await comando.ExecuteScalarAsync();
        }
    }

    private static async Task<(int Codigo, string Salida, string Error)> Pedir(WebApplication servicio, string entorno, string? correo)
    {
        await using var alcance = servicio.Services.CreateAsyncScope();
        alcance.ServiceProvider.GetRequiredService<IEstablecedorDeActor>().Establecer(ActorActual.DelSistema());
        var (salida, error) = (new StringWriter(), new StringWriter());
        var codigo = await ModoRestablecerSegundoFactor.Informar(alcance.ServiceProvider, entorno, "Normal", correo, salida, error);
        return (codigo, salida.ToString(), error.ToString());
    }

    private async Task<Montaje> Montar(Interruptor? auditoria = null)
    {
        var propietaria = await instancia.NuevaBase();
        var baseDeDatos = new NpgsqlConnectionStringBuilder(propietaria).Database!;
        var configuracion = AnfitrionDelEcommerceTests.ConfiguracionCompleta();
        var claves = new Dictionary<string, (string Usuario, string Clave)>
        {
            [OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeLaAplicacion] = ("ecommerce_rsf", Secreto()),
            [OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeDepuracionDeAuditoria] = ("ecommerce_rsf_aud", Secreto()),
            [OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeDepuracionDeRegistros] = ("ecommerce_rsf_reg", Secreto()),
        };
        foreach (var (ruta, (usuario, clave)) in claves)
        {
            configuracion[ruta] = instancia.Cadena(baseDeDatos, usuario, clave);
        }

        var migracion = new Dictionary<string, string?> { ["Migrador:Ecommerce:Propietaria"] = propietaria, [PlanDeMigracionDelEcommerce.VariableDeLaVersionDeTextos] = "0.1-provisional" };
        foreach (var (ruta, valor) in configuracion.Where(p => p.Key.StartsWith("Ecommerce:Base:", StringComparison.Ordinal)))
        {
            migracion[ruta] = valor;
        }

        var error = new StringWriter();
        var codigo = await ModoMigrar.Ejecutar(new ConfigurationBuilder().AddInMemoryCollection(migracion).Build(), new StringWriter(), error);
        Assert.True(codigo == 0, error.ToString());

        var registros = new ConcurrentQueue<string>();
        var correos = new List<MensajeDeCorreo>();
        var transporte = new Interruptor();
        return new Montaje(Levantar(configuracion, registros, correos, auditoria, transporte), propietaria, configuracion, registros, correos, transporte);
    }

    private static WebApplication Levantar(Dictionary<string, string?> configuracion, ConcurrentQueue<string> registros, List<MensajeDeCorreo> correos, Interruptor? auditoria, Interruptor? transporte)
    {
        // Como en Development (límites en memoria, sin un Redis real): la operación recibe el entorno por parámetro.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new RegistrosCapturados(registros));
        builder.Configuration.AddInMemoryCollection(configuracion);
        builder.Services.AddControllersWithViews();
        builder.Services.AddSingleton(typeof(ModoDeArranque), ModoDeArranque.Normal);
        builder.AgregarEcommerce(ModoDeArranque.Normal);
        builder.Services.AddSingleton<ITransporteDeCorreo>(new TransporteDePrueba(correos, transporte ?? new Interruptor()));
        if (auditoria is not null)
        {
            var original = builder.Services.Last(d => d.ServiceType == typeof(IAuditoria));
            builder.Services.Remove(original);
            builder.Services.Add(new ServiceDescriptor(
                typeof(IAuditoria),
                sp => new AuditoriaConInterruptor((IAuditoria)(original.ImplementationFactory?.Invoke(sp) ?? original.ImplementationInstance ?? ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)), auditoria),
                original.Lifetime));
        }

        return builder.Build();
    }

    /// <summary>Una falla o una pausa que la prueba enciende.</summary>
    private sealed class Interruptor
    {
        private TaskCompletionSource? _soltar;
        private TaskCompletionSource _alcanzado = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Falla { get; set; }

        public Task Alcanzado => _alcanzado.Task.WaitAsync(TimeSpan.FromSeconds(30));

        public void Retener()
        {
            _alcanzado = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _soltar = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Soltar() => _soltar?.TrySetResult();

        public async Task Pasar()
        {
            if (_soltar is { } espera)
            {
                _alcanzado.TrySetResult();
                await espera.Task.WaitAsync(TimeSpan.FromSeconds(60));
            }
        }
    }

    /// <summary>La auditoría real, que la prueba puede romper o detener <b>solo</b> al registrar el restablecimiento (dentro de su transacción).</summary>
    private sealed class AuditoriaConInterruptor(IAuditoria real, Interruptor interruptor) : IAuditoria
    {
        public async Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
        {
            if (solicitud.Accion == AccionesAuditables.SegundoFactorRestablecidoPorAdministracion)
            {
                if (interruptor.Falla)
                {
                    throw new InvalidOperationException("falla de prueba");
                }

                await interruptor.Pasar();
            }

            await real.Registrar(solicitud, cancelacion);
        }
    }

    private sealed class TransporteDePrueba(List<MensajeDeCorreo> correos, Interruptor interruptor) : ITransporteDeCorreo
    {
        public Task Enviar(MensajeDeCorreo mensaje, CancellationToken cancelacion = default)
        {
            if (interruptor.Falla)
            {
                throw new InvalidOperationException("El transporte no responde.");
            }

            lock (correos)
            {
                correos.Add(mensaje);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RegistrosCapturados(ConcurrentQueue<string> registros) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Registrador(registros);

        public void Dispose()
        {
        }

        private sealed class Registrador(ConcurrentQueue<string> registros) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                registros.Enqueue(formatter(state, exception) + " " + exception);
        }
    }
}
