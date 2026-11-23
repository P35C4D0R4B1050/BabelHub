using System.Security.Cryptography;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Migracion;
using BabelHub.IntegrationTests.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BabelHub.IntegrationTests.Despliegue;

/// <summary>
/// La cuenta con la que la pasarela de pago revisa la tienda en <c>Staging</c> (PAG-35; AC-PAG-32) y la siembra de los textos legales vigentes que la hace posible (<c>migrar</c> con <c>BABELHUB__Migrador__Ecommerce__VersionDeTextos</c>), contra una base de
/// datos migrada de verdad: crea <b>una</b> cuenta de cliente con el correo verificado y los textos aceptados, con una contraseña aleatoria que ingresa por el camino real; repetirla solo rota esa contraseña; <b>se niega fuera de Staging</b> sin tocar nada;
/// nunca toca una cuenta ajena; y la contraseña no queda en ninguna tabla ni en el registro del servidor de base de datos.
/// </summary>
public sealed class CuentaDeRevisionDeLaPasarelaTests(InstanciaParaMigrar instancia) : IClassFixture<InstanciaParaMigrar>
{
    private static string Secreto() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(36));

    private sealed record Servicio(WebApplication App, string Propietaria) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await App.DisposeAsync();

        public AsyncServiceScope Alcance()
        {
            var alcance = App.Services.CreateAsyncScope();
            alcance.ServiceProvider.GetRequiredService<IEstablecedorDeActor>().Establecer(ActorActual.DelSistema());
            return alcance;
        }
    }

    private async Task<(string Propietaria, Dictionary<string, string?> Configuracion)> MigrarUnaBase(string? versionDeTextos)
    {
        var propietaria = await instancia.NuevaBase();
        var baseDeDatos = new NpgsqlConnectionStringBuilder(propietaria).Database!;
        var configuracion = AnfitrionDelEcommerceTests.ConfiguracionCompleta();
        var claves = new Dictionary<string, (string Usuario, string Clave)>
        {
            [OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeLaAplicacion] = ("ecommerce_revision", Secreto()),
            [OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeDepuracionDeAuditoria] = ("ecommerce_revision_aud", Secreto()),
            [OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeDepuracionDeRegistros] = ("ecommerce_revision_reg", Secreto()),
        };
        foreach (var (ruta, (usuario, clave)) in claves)
        {
            configuracion[ruta] = instancia.Cadena(baseDeDatos, usuario, clave);
        }

        var migracion = new Dictionary<string, string?> { ["Migrador:Ecommerce:Propietaria"] = propietaria };
        if (versionDeTextos is not null)
        {
            migracion[PlanDeMigracionDelEcommerce.VariableDeLaVersionDeTextos] = versionDeTextos;
        }

        foreach (var (ruta, valor) in configuracion.Where(p => p.Key.StartsWith("Ecommerce:Base:", StringComparison.Ordinal)))
        {
            migracion[ruta] = valor;
        }

        var error = new StringWriter();
        var codigo = await ModoMigrar.Ejecutar(new ConfigurationBuilder().AddInMemoryCollection(migracion).Build(), new StringWriter(), error);
        Assert.True(codigo == 0, error.ToString());
        return (propietaria, configuracion);
    }

    private static Servicio Levantar(string propietaria, Dictionary<string, string?> configuracion, Action<IServiceCollection>? ajustar = null)
    {
        // El anfitrión se compone como en Development (los límites en memoria, sin un Redis real): la operación recibe el entorno por parámetro y los dos primeros casos prueban que se niega fuera de Staging.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(configuracion);
        builder.Services.AddControllersWithViews();
        builder.Services.AddSingleton(typeof(ModoDeArranque), ModoDeArranque.Normal);
        builder.AgregarEcommerce(ModoDeArranque.Normal);
        ajustar?.Invoke(builder.Services);
        return new Servicio(builder.Build(), propietaria);
    }

    private async Task<T> Consultar<T>(string propietaria, string sql)
    {
        await using var origen = NpgsqlDataSource.Create(propietaria);
        await using var comando = origen.CreateCommand(sql);
        return (T)(await comando.ExecuteScalarAsync())!;
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-32")]
    public async Task En_Staging_crea_una_cuenta_de_cliente_verificada_con_los_textos_aceptados_y_su_contrasena_ingresa()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();

        var cuenta = await alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>().Crear("Staging", correo: null);

        Assert.Equal("revision@example.test", cuenta.Correo);
        Assert.False(cuenta.ContrasenaRotada);
        Assert.True(cuenta.Contrasena.Revelar().Length >= 28);
        Assert.DoesNotContain(cuenta.Contrasena.Revelar(), cuenta.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(cuenta.Contrasena.Revelar(), cuenta.Contrasena.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios WHERE correo = 'revision@example.test' AND correo_verificado AND activo AND rol = 'cliente'"));
        // Ingresa por el camino real: sin paso pendiente (un cliente no tiene segundo factor), con credencial. Eso prueba también que se creó la primera clave de firma.
        var ingreso = await alcance.ServiceProvider.GetRequiredService<IIngreso>().Ingresar(new SolicitudDeIngreso(cuenta.Correo, cuenta.Contrasena));
        Assert.True(ingreso.EsExito, ingreso.Error?.Clave);
        Assert.NotNull(ingreso.Valor.Credencial);
        // Y los textos vigentes ya están aceptados: no queda pendiente nada para pagar.
        var estado = await alcance.ServiceProvider.GetRequiredService<IEstadoDeCuenta>().Consultar(cuenta.UsuarioId);
        Assert.NotNull(estado);
        Assert.Equal(EstadoDeAceptacionDeLosTextos.AlDia, estado.Textos);
        Assert.True(estado.PuedeComprar);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-32")]
    public async Task Repetirla_solo_rota_la_contrasena_y_la_anterior_deja_de_servir()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();
        var operacion = alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>();

        var primera = await operacion.Crear("Staging", correo: null);
        var segunda = await operacion.Crear("Staging", correo: null);

        Assert.True(segunda.ContrasenaRotada);
        Assert.Equal(primera.UsuarioId, segunda.UsuarioId);
        Assert.NotEqual(primera.Contrasena.Revelar(), segunda.Contrasena.Revelar());
        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios WHERE correo = 'revision@example.test'"));
        var ingreso = alcance.ServiceProvider.GetRequiredService<IIngreso>();
        Assert.True((await ingreso.Ingresar(new SolicitudDeIngreso(segunda.Correo, segunda.Contrasena))).EsExito);
        Assert.False((await ingreso.Ingresar(new SolicitudDeIngreso(primera.Correo, primera.Contrasena))).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-32")]
    public async Task Rotar_la_contrasena_cierra_tambien_las_sesiones_ya_abiertas()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();
        var operacion = alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>();
        var primera = await operacion.Crear("Staging", correo: null);
        var abierta = await alcance.ServiceProvider.GetRequiredService<IIngreso>().Ingresar(new SolicitudDeIngreso(primera.Correo, primera.Contrasena));
        var renovacion = abierta.Valor.Credencial!.Renovacion;
        var sesiones = alcance.ServiceProvider.GetRequiredService<ISesiones>();

        await operacion.Crear("Staging", correo: null);

        Assert.False((await sesiones.Renovar(renovacion)).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-32")]
    public async Task Una_cuenta_con_ese_correo_sin_verificar_no_se_toca_aunque_lleve_el_nombre()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();
        var operacion = alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>();
        var creada = await operacion.Crear("Staging", correo: null);
        await using (var origen = NpgsqlDataSource.Create(propietaria))
        {
            await using var cambio = origen.CreateCommand("UPDATE identidad.usuarios SET correo_verificado = false WHERE correo = 'revision@example.test'");
            await cambio.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => operacion.Crear("Staging", correo: null));

        Assert.True((await alcance.ServiceProvider.GetRequiredService<IIngreso>().Ingresar(new SolicitudDeIngreso(creada.Correo, creada.Contrasena))).EsExito);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-32")]
    [InlineData("persona.real@gmail.com")]
    [InlineData("cliente@babel-hub.shop")]
    public async Task Un_correo_de_un_dominio_que_no_es_de_pruebas_se_rechaza_sin_crear_nada(string correo)
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();

        var falla = await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>().Crear("Staging", correo));

        Assert.Contains("dominio reservado", falla.Message, StringComparison.Ordinal);
        Assert.Equal(0L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios"));
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-36")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public async Task Retirar_desactiva_la_cuenta_cierra_sus_sesiones_y_lo_audita_y_repetirlo_no_cambia_nada(string entorno)
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();
        var operacion = alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>();
        var cuenta = await operacion.Crear("Staging", correo: null);
        var abierta = await alcance.ServiceProvider.GetRequiredService<IIngreso>().Ingresar(new SolicitudDeIngreso(cuenta.Correo, cuenta.Contrasena));

        Assert.True(await operacion.Retirar(entorno, correo: null));
        Assert.False(await operacion.Retirar(entorno, correo: null));

        Assert.False((await alcance.ServiceProvider.GetRequiredService<IIngreso>().Ingresar(new SolicitudDeIngreso(cuenta.Correo, cuenta.Contrasena))).EsExito);
        Assert.False((await alcance.ServiceProvider.GetRequiredService<ISesiones>().Renovar(abierta.Valor.Credencial!.Renovacion)).EsExito);
        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.registros WHERE accion = 'UsuarioDesactivado'"));
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-36")]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Qa")]
    public async Task Retirar_se_niega_fuera_de_Staging_y_Production_y_nunca_toca_una_cuenta_ajena(string entorno)
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();
        var operacion = alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>();
        await operacion.Crear("Staging", correo: "persona-07@example.test");
        await using (var origen = NpgsqlDataSource.Create(propietaria))
        {
            await using var cambio = origen.CreateCommand("UPDATE identidad.usuarios SET nombre = 'Una persona real'");
            await cambio.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => operacion.Retirar(entorno, correo: null));
        await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => operacion.Retirar("Staging", correo: "persona-07@example.test"));

        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios WHERE activo"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-36")]
    public async Task El_modo_de_consola_retirar_se_permite_en_Production_y_se_niega_en_otro_entorno_sin_construir_el_servicio()
    {
        var salida = new StringWriter();
        var error = new StringWriter();

        var enDesarrollo = await ModoCuentaDeRevision.Ejecutar(["--retirar"], salida, error, "Development");
        var enProduccionSinConfiguracion = await ModoCuentaDeRevision.Ejecutar(["--retirar"], new StringWriter(), new StringWriter(), "Production");

        Assert.Equal(1, enDesarrollo);
        Assert.Contains("solo existe en Staging y Production", error.ToString(), StringComparison.Ordinal);
        // En Production sí llega a construir el servicio (y falla aquí porque no hay configuración), no se niega por el entorno.
        Assert.Equal(1, enProduccionSinConfiguracion);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-32")]
    [InlineData("Production")]
    [InlineData("production")]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Qa")]
    [InlineData("")]
    public async Task Fuera_de_Staging_se_niega_antes_de_tocar_la_base(string entorno)
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();

        var falla = await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>().Crear(entorno, correo: null));

        Assert.Contains("solo se crea en Staging", falla.Message, StringComparison.Ordinal);
        Assert.Equal(0L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios"));
        Assert.Equal(0L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.claves_de_firma"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-32")]
    public async Task Sin_los_textos_vigentes_sembrados_se_niega_y_dice_como_sembrarlos_sin_crear_nada()
    {
        var (propietaria, configuracion) = await MigrarUnaBase(versionDeTextos: null);
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();

        var falla = await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>().Crear("Staging", correo: null));

        Assert.Contains("BABELHUB__Migrador__Ecommerce__VersionDeTextos", falla.Message, StringComparison.Ordinal);
        Assert.Equal(0L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-32")]
    public async Task Nunca_toca_una_cuenta_ajena_que_tenga_ese_correo()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();
        var operacion = alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>();
        var creada = await operacion.Crear("Staging", correo: "persona-07@example.test");
        // La cuenta pasa a ser «de una persona»: ya no es la que esta operación creó.
        await using (var origen = NpgsqlDataSource.Create(propietaria))
        {
            await using var cambio = origen.CreateCommand("UPDATE identidad.usuarios SET nombre = 'Una persona real' WHERE correo = 'persona-07@example.test'");
            await cambio.ExecuteNonQueryAsync();
        }

        var falla = await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => operacion.Crear("Staging", correo: "persona-07@example.test"));

        Assert.Contains("otra cuenta", falla.Message, StringComparison.Ordinal);
        // Su contraseña sigue siendo la que tenía (la operación no la rotó).
        var ingreso = alcance.ServiceProvider.GetRequiredService<IIngreso>();
        Assert.True((await ingreso.Ingresar(new SolicitudDeIngreso(creada.Correo, creada.Contrasena))).EsExito);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-32")]
    [InlineData("sin-arroba")]
    [InlineData("dos@@example.test")]
    [InlineData("con espacio@example.test")]
    public async Task Un_correo_invalido_se_rechaza_sin_crear_nada(string correo)
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();

        await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>().Crear("Staging", correo));

        Assert.Equal(0L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-32")]
    public async Task La_contrasena_no_queda_en_ninguna_tabla_ni_en_el_registro_del_servidor_de_base_de_datos()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();

        var cuenta = await alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>().Crear("Staging", correo: null);

        var contrasena = cuenta.Contrasena.Revelar();
        // Solo el resumen Argon2id (nunca la contraseña) está en la cuenta; la auditoría lleva el rol, no secretos.
        var resumen = await Consultar<string>(propietaria, "SELECT hash_contrasena FROM identidad.usuarios WHERE correo = 'revision@example.test'");
        Assert.StartsWith("$argon2id$", resumen, StringComparison.Ordinal);
        Assert.DoesNotContain(contrasena, resumen, StringComparison.Ordinal);
        var (salida, errores) = await instancia.RegistroDelServidor();
        Assert.DoesNotContain(contrasena, salida + errores, StringComparison.Ordinal);
    }


    private sealed class AuditoriaQueFalla : BabelHub.Ecommerce.Application.Auditoria.IAuditoria
    {
        public Task Registrar(BabelHub.Ecommerce.Application.Auditoria.SolicitudDeRegistro solicitud, CancellationToken cancelacion = default) =>
            throw new BabelHub.Ecommerce.Application.Auditoria.AuditoriaNoDisponibleException("auditoría caída");
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-35")]
    public async Task Cada_cambio_queda_en_la_auditoria_la_creacion_la_primera_clave_y_la_rotacion()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();
        var operacion = alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>();

        await operacion.Crear("Staging", correo: null);
        await operacion.Crear("Staging", correo: null);

        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.registros WHERE accion = 'UsuarioDadoDeAlta'"));
        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.registros WHERE accion = 'ClaveDeFirmaRotada'"));
        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.registros WHERE accion = 'ContrasenaRestablecidaPorAdministracion'"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-35")]
    public async Task Si_no_se_puede_auditar_no_se_crea_ni_la_cuenta_ni_la_clave_de_firma()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion, servicios => servicios.AddScoped<BabelHub.Ecommerce.Application.Auditoria.IAuditoria, AuditoriaQueFalla>());
        await using var alcance = servicio.Alcance();

        await Assert.ThrowsAsync<BabelHub.Ecommerce.Application.Auditoria.AuditoriaNoDisponibleException>(() => alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>().Crear("Staging", correo: null));

        Assert.Equal(0L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios"));
        Assert.Equal(0L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.claves_de_firma"));
    }

    // ---------- Varias cuentas, purga y el primer administrador (PAG-43, PAG-44, PAG-45) ----------

    private sealed class RegistroDeSupresionesEnMemoria : IRegistroDeSupresiones
    {
        public Task<EntradaDeSupresionDto> Agregar(string identificadorOpaco, ClaseDeSupresion clase, EtapaDeSupresion etapa, DateTimeOffset fecha, CancellationToken cancelacion = default) =>
            Task.FromResult(new EntradaDeSupresionDto(identificadorOpaco, clase, etapa, fecha, "suma", false));

        public Task<EntradaDeSupresionDto?> Obtener(string identificadorOpaco, ClaseDeSupresion clase, EtapaDeSupresion etapa, CancellationToken cancelacion = default) => Task.FromResult<EntradaDeSupresionDto?>(null);

        public Task<IReadOnlyList<EntradaLeidaDto>> LeerConMetadatos(CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<EntradaLeidaDto>>([]);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-38")]
    public async Task Crear_varias_cuentas_da_a_cada_una_su_contrasena_y_todas_ingresan_y_el_limite_es_de_20()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();
        var operacion = alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>();

        var cuentas = await operacion.CrearVarias("Staging", 3);

        Assert.Equal(["persona-01@example.test", "persona-02@example.test", "persona-03@example.test"], cuentas.Select(c => c.Correo));
        Assert.Equal(3, cuentas.Select(c => c.Contrasena.Revelar()).Distinct().Count());
        var ingreso = alcance.ServiceProvider.GetRequiredService<IIngreso>();
        foreach (var c in cuentas)
        {
            Assert.True((await ingreso.Ingresar(new SolicitudDeIngreso(c.Correo, c.Contrasena))).EsExito);
        }

        await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => operacion.CrearVarias("Staging", 21));
        await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => operacion.CrearVarias("Staging", 0));
        await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => operacion.CrearVarias("Production", 2));
        Assert.Equal(3L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios"));
        // «Retirar todas» las desactiva (incluida, si existe, la de la revisión) y repetirlo no cambia nada.
        Assert.Equal(3, await operacion.RetirarTodas("Production"));
        Assert.Equal(0, await operacion.RetirarTodas("Production"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-40")]
    public async Task Purgar_cancela_las_cuentas_de_prueba_por_la_cancelacion_de_cuenta_y_no_toca_a_las_reales_y_es_idempotente()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion, servicios => servicios.AddSingleton<IRegistroDeSupresiones>(new RegistroDeSupresionesEnMemoria()));
        await using var alcance = servicio.Alcance();
        var operacion = alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>();
        var revision = await operacion.Crear("Staging", correo: null);
        await operacion.CrearVarias("Staging", 2);
        // Una cuenta real (con el nombre de una persona) con un correo de la misma forma: no se toca.
        await operacion.Crear("Staging", "persona-05@example.test");
        await using (var origen = NpgsqlDataSource.Create(propietaria))
        {
            await using var cambio = origen.CreateCommand("UPDATE identidad.usuarios SET nombre = 'Una persona real' WHERE correo = 'persona-05@example.test'");
            await cambio.ExecuteNonQueryAsync();
        }

        Assert.Equal(3, await operacion.Purgar("Staging"));
        Assert.Equal(0, await operacion.Purgar("Production"));

        Assert.Equal(3L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios WHERE cancelada_en IS NOT NULL"));
        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios WHERE cancelada_en IS NULL AND nombre = 'Una persona real'"));
        Assert.Equal(3L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.registros WHERE accion = 'CuentaCancelada'"));
        Assert.False((await alcance.ServiceProvider.GetRequiredService<IIngreso>().Ingresar(new SolicitudDeIngreso(revision.Correo, revision.Contrasena))).EsExito);
        // El correo quedó libre: la cuenta de la revisión puede volver a crearse.
        Assert.False((await operacion.Crear("Staging", correo: null)).ContrasenaRotada);
        await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => operacion.Purgar("Development"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-38")]
    public async Task El_modo_de_consola_valida_la_cantidad_y_las_combinaciones_sin_construir_el_servicio()
    {
        foreach (var argumentos in new[] { new[] { "--cantidad", "x" }, ["--cantidad", "3", "--correo", "a@example.test"], ["--purgar", "--retirar"], ["--todas"], ["--purgar", "--correo", "a@example.test"] })
        {
            var error = new StringWriter();

            Assert.Equal(1, await ModoCuentaDeRevision.Ejecutar(argumentos, new StringWriter(), error, "Staging"));
            Assert.Contains("cuenta-de-prueba:", error.ToString(), StringComparison.Ordinal);
        }

        var enProduccion = new StringWriter();
        Assert.Equal(1, await ModoCuentaDeRevision.Ejecutar(["--cantidad", "2"], new StringWriter(), enProduccion, "Production"));
        Assert.Contains("solo existe en Staging", enProduccion.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-39")]
    public async Task Crear_el_primer_administrador_da_una_contrasena_temporal_y_exige_cambiarla_y_configurar_el_segundo_factor()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();
        var operacion = alcance.ServiceProvider.GetRequiredService<PrimerAdministradorDeLaInstalacion>();

        var r = await operacion.Crear("Production", "admin.inicial@ejemplo.pe", nombre: null);

        Assert.Equal(ResultadoDelPrimerAdministrador.Creado, r.Resultado);
        Assert.DoesNotContain(r.ContrasenaTemporal!.Revelar(), r.ToString(), StringComparison.Ordinal);
        // PAG-46: en una instalación sin la primera clave de firma, la operación la crea.
        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.claves_de_firma"));
        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios WHERE correo = 'admin.inicial@ejemplo.pe' AND rol = 'administrador-central' AND correo_verificado AND activo AND debe_cambiar"));
        // La temporal vence a las 24 horas (como la de todo el personal, IDE-23).
        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios WHERE correo = 'admin.inicial@ejemplo.pe' AND contrasena_temporal_hasta BETWEEN now() + interval '23 hours' AND now() + interval '25 hours'"));
        var ingreso = alcance.ServiceProvider.GetRequiredService<IIngreso>();
        var primero = await ingreso.Ingresar(new SolicitudDeIngreso(r.Correo, r.ContrasenaTemporal));
        // Sin credencial: primero cambia la contraseña temporal; después, el segundo factor es obligatorio antes de recibir una credencial.
        Assert.True(primero.EsExito);
        Assert.Null(primero.Valor.Credencial);
        Assert.Equal(PasoPendienteDelIngreso.CambiarContrasenaTemporal, primero.Valor.Desafio!.Paso);
        var nueva = new ValorSecreto("Una-contrasena-nueva-9#xZ" + Guid.NewGuid().ToString("N")[..6]);
        var segundo = await ingreso.CambiarLaContrasenaTemporal(primero.Valor.Desafio.Identificador, nueva);
        Assert.True(segundo.EsExito, segundo.Error?.Clave);
        Assert.Null(segundo.Valor.Credencial);
        Assert.Equal(PasoPendienteDelIngreso.ConfigurarSegundoFactor, segundo.Valor.Desafio!.Paso);
        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.registros WHERE accion = 'UsuarioDadoDeAlta'"));
        var hash = await Consultar<string>(propietaria, "SELECT hash_contrasena FROM identidad.usuarios WHERE correo = 'admin.inicial@ejemplo.pe'");
        Assert.DoesNotContain(r.ContrasenaTemporal.Revelar(), hash, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-39")]
    public async Task No_crea_un_segundo_administrador_y_repetirla_no_duplica_y_solo_regenera_la_temporal_si_no_hubo_primer_ingreso()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();
        var operacion = alcance.ServiceProvider.GetRequiredService<PrimerAdministradorDeLaInstalacion>();
        var primera = await operacion.Crear("Staging", "admin.inicial@ejemplo.pe", "Ana Pérez");

        var otro = await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => operacion.Crear("Staging", "otro.admin@ejemplo.pe", nombre: null));
        var repetida = await operacion.Crear("Staging", "admin.inicial@ejemplo.pe", nombre: null);

        Assert.Contains("Ya existe un administrador central", otro.Message, StringComparison.Ordinal);
        Assert.Equal(ResultadoDelPrimerAdministrador.ContrasenaTemporalRegenerada, repetida.Resultado);
        Assert.Equal(primera.UsuarioId, repetida.UsuarioId);
        Assert.NotEqual(primera.ContrasenaTemporal!.Revelar(), repetida.ContrasenaTemporal!.Revelar());
        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios"));
        var ingreso = alcance.ServiceProvider.GetRequiredService<IIngreso>();
        Assert.False((await ingreso.Ingresar(new SolicitudDeIngreso(primera.Correo, primera.ContrasenaTemporal))).EsExito);
        // Tras completar el primer ingreso (cambiar la contraseña), repetir no cambia nada.
        var desafio = (await ingreso.Ingresar(new SolicitudDeIngreso(repetida.Correo, repetida.ContrasenaTemporal!))).Valor.Desafio!;
        await ingreso.CambiarLaContrasenaTemporal(desafio.Identificador, new ValorSecreto("Otra-contrasena-nueva-7#kQ" + Guid.NewGuid().ToString("N")[..6]));
        var tercera = await operacion.Crear("Staging", "admin.inicial@ejemplo.pe", nombre: null);
        Assert.Equal(ResultadoDelPrimerAdministrador.YaExiste, tercera.Resultado);
        Assert.Null(tercera.ContrasenaTemporal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-39")]
    public async Task Con_otro_administrador_activo_no_se_regenera_la_temporal_de_uno_que_aun_no_ingreso_ni_se_audita()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();
        var operacion = alcance.ServiceProvider.GetRequiredService<PrimerAdministradorDeLaInstalacion>();
        var primero = await operacion.Crear("Staging", "admin.inicial@ejemplo.pe", nombre: null);
        // Otro administrador central dado de alta después por la administración, que aún no ingresó.
        await using (var origen = NpgsqlDataSource.Create(propietaria))
        {
            await using var alta = origen.CreateCommand("INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, creada_en, debe_cambiar) VALUES (gen_random_uuid(), 'Otro administrador', 'otro.admin@ejemplo.pe', true, 'administrador-central', true, now(), true)");
            await alta.ExecuteNonQueryAsync();
        }

        var hashAntes = await Consultar<string>(propietaria, "SELECT hash_contrasena FROM identidad.usuarios WHERE correo = 'admin.inicial@ejemplo.pe'");
        var falla = await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => operacion.Crear("Staging", "admin.inicial@ejemplo.pe", nombre: null));
        await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => operacion.Crear("Staging", "otro.admin@ejemplo.pe", nombre: null));

        Assert.Contains("Ya existe otro administrador central", falla.Message, StringComparison.Ordinal);
        Assert.Equal(hashAntes, await Consultar<string>(propietaria, "SELECT hash_contrasena FROM identidad.usuarios WHERE correo = 'admin.inicial@ejemplo.pe'"));
        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.registros WHERE accion = 'UsuarioDadoDeAlta'"));
        Assert.NotNull(primero.ContrasenaTemporal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-36")]
    public async Task Retirar_y_despues_purgar_cancela_la_cuenta_ya_retirada_y_libera_su_correo()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion, servicios => servicios.AddSingleton<IRegistroDeSupresiones>(new RegistroDeSupresionesEnMemoria()));
        await using var alcance = servicio.Alcance();
        var operacion = alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>();
        await operacion.Crear("Staging", correo: null);

        Assert.Equal(1, await operacion.RetirarTodas("Production"));
        Assert.Equal(1, await operacion.Purgar("Production"));

        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios WHERE cancelada_en IS NOT NULL"));
        Assert.False((await operacion.Crear("Staging", correo: null)).ContrasenaRotada);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-38")]
    public async Task Crear_varias_entrega_cada_contrasena_al_crearla_aunque_una_posterior_falle()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();
        var operacion = alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>();
        var entregadas = new List<string>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => operacion.CrearVarias("Staging", 5, cuenta =>
        {
            entregadas.Add(cuenta.Correo);
            return entregadas.Count == 3 ? throw new InvalidOperationException("falla de prueba") : Task.CompletedTask;
        }));

        Assert.Equal(["persona-01@example.test", "persona-02@example.test", "persona-03@example.test"], entregadas);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-39")]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Qa")]
    public async Task El_primer_administrador_solo_se_crea_en_Staging_y_Production_y_pide_un_correo_valido(string entorno)
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();
        var operacion = alcance.ServiceProvider.GetRequiredService<PrimerAdministradorDeLaInstalacion>();

        await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => operacion.Crear(entorno, "admin@ejemplo.pe", nombre: null));
        await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => operacion.Crear("Production", null, nombre: null));
        await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => operacion.Crear("Production", "sin-arroba", nombre: null));

        Assert.Equal(0L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-41")]
    public async Task El_primer_administrador_se_niega_con_el_correo_de_un_cliente_o_de_un_administrador_desactivado_sin_tocar_nada()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();
        var cuentaDePrueba = alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>();
        var operacion = alcance.ServiceProvider.GetRequiredService<PrimerAdministradorDeLaInstalacion>();
        await cuentaDePrueba.Crear("Staging", "persona-05@example.test");
        await operacion.Crear("Staging", "admin.inicial@ejemplo.pe", nombre: null);
        await using (var origen = NpgsqlDataSource.Create(propietaria))
        {
            await using var desactivar = origen.CreateCommand("UPDATE identidad.usuarios SET activo = false WHERE correo = 'admin.inicial@ejemplo.pe'");
            await desactivar.ExecuteNonQueryAsync();
        }

        var cliente = await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => operacion.Crear("Production", "persona-05@example.test", nombre: null));
        var desactivado = await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => operacion.Crear("Production", "admin.inicial@ejemplo.pe", nombre: null));

        Assert.Contains("otra cuenta", cliente.Message, StringComparison.Ordinal);
        Assert.Contains("otra cuenta", desactivado.Message, StringComparison.Ordinal);
        Assert.Equal(0L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios WHERE rol = 'administrador-central' AND activo"));
        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios WHERE correo = 'persona-05@example.test' AND rol <> 'administrador-central'"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-32")]
    public async Task Crear_una_cuenta_de_prueba_solo_admite_el_correo_de_la_revision_o_los_de_las_personas()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion);
        await using var alcance = servicio.Alcance();
        var operacion = alcance.ServiceProvider.GetRequiredService<CuentaDeRevisionDeLaPasarela>();

        await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => operacion.Crear("Staging", "otra@example.test"));
        await Assert.ThrowsAsync<CuentaDeRevisionNoPermitidaException>(() => operacion.Crear("Staging", "persona-21@example.test"));
        Assert.Equal(0L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios"));
        Assert.False((await operacion.Crear("Staging", "persona-20@example.test")).ContrasenaRotada);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-35")]
    public async Task Si_no_se_puede_auditar_no_se_crea_el_primer_administrador()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        await using var servicio = Levantar(propietaria, configuracion, servicios => servicios.AddScoped<BabelHub.Ecommerce.Application.Auditoria.IAuditoria, AuditoriaQueFalla>());
        await using var alcance = servicio.Alcance();

        await Assert.ThrowsAsync<BabelHub.Ecommerce.Application.Auditoria.AuditoriaNoDisponibleException>(() => alcance.ServiceProvider.GetRequiredService<PrimerAdministradorDeLaInstalacion>().Crear("Production", "admin@ejemplo.pe", nombre: null));

        Assert.Equal(0L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.usuarios"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-39")]
    public async Task El_modo_de_consola_crear_administrador_se_niega_fuera_de_Staging_y_Production_y_valida_sus_opciones()
    {
        var error = new StringWriter();
        var salida = new StringWriter();

        Assert.Equal(1, await ModoCrearAdministrador.Ejecutar(["--correo", "a@ejemplo.pe"], salida, error, "Development"));
        Assert.Contains("solo existe en Staging y Production", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, await ModoCrearAdministrador.Ejecutar(["--contrasena", "valor-que-no-debe-repetirse"], salida, error, "Production"));
        Assert.DoesNotContain("valor-que-no-debe-repetirse", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, salida.ToString());
    }

    // ---------- La siembra de los textos legales vigentes (migrar) ----------

    [Fact]
    [Trait("Criterio", "AC-PAG-33")]
    public async Task Migrar_con_la_version_siembra_los_textos_una_vez_y_nunca_pisa_los_que_ya_estan()
    {
        var (propietaria, configuracion) = await MigrarUnaBase("0.1-provisional");
        Assert.Equal("0.1-provisional", await Consultar<string>(propietaria, "SELECT terminos FROM identidad.versiones_de_textos WHERE id = 1 AND privacidad = terminos AND reembolso = terminos"));
        await using (var origen = NpgsqlDataSource.Create(propietaria))
        {
            await using var cambio = origen.CreateCommand("UPDATE identidad.versiones_de_textos SET terminos = '2026-12', privacidad = '2026-12', reembolso = '2026-12' WHERE id = 1");
            await cambio.ExecuteNonQueryAsync();
        }

        // Una segunda migración con otra versión no cambia lo que la persona responsable dejó vigente.
        var migracion = new Dictionary<string, string?>
        {
            ["Migrador:Ecommerce:Propietaria"] = propietaria,
            [PlanDeMigracionDelEcommerce.VariableDeLaVersionDeTextos] = "otra-version",
        };
        foreach (var (ruta, valor) in configuracion.Where(p => p.Key.StartsWith("Ecommerce:Base:", StringComparison.Ordinal)))
        {
            migracion[ruta] = valor;
        }

        var error = new StringWriter();
        Assert.True(await ModoMigrar.Ejecutar(new ConfigurationBuilder().AddInMemoryCollection(migracion).Build(), new StringWriter(), error) == 0, error.ToString());

        Assert.Equal("2026-12", await Consultar<string>(propietaria, "SELECT terminos FROM identidad.versiones_de_textos WHERE id = 1"));
        Assert.Equal(1L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.versiones_de_textos"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-33")]
    public async Task Migrar_sin_la_version_no_siembra_nada()
    {
        var (propietaria, _) = await MigrarUnaBase(versionDeTextos: null);

        Assert.Equal(0L, await Consultar<long>(propietaria, "SELECT count(*) FROM identidad.versiones_de_textos"));
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-33")]
    [InlineData("con espacio")]
    [InlineData("comilla'doble")]
    [InlineData("'; DROP TABLE identidad.usuarios; --")]
    [InlineData("-empieza-con-guion")]
    [InlineData("una-version-demasiado-larga-para-la-columna-de-cuarenta-caracteres")]
    public void Una_version_invalida_falla_la_migracion_nombrando_la_variable_y_no_la_repite(string version)
    {
        var configuracion = AnfitrionDelEcommerceTests.ConfiguracionCompleta();
        var migracion = new Dictionary<string, string?> { [PlanDeMigracionDelEcommerce.VariableDeLaVersionDeTextos] = version };
        foreach (var (ruta, valor) in configuracion.Where(p => p.Key.StartsWith("Ecommerce:Base:", StringComparison.Ordinal)))
        {
            migracion[ruta] = valor;
        }

        var falla = Assert.Throws<MigracionFallidaException>(() => PlanDeMigracionDelEcommerce.Leer(new ConfigurationBuilder().AddInMemoryCollection(migracion).Build()));

        Assert.Contains("BABELHUB__Migrador__Ecommerce__VersionDeTextos", falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(version, falla.Message, StringComparison.Ordinal);
    }

    // ---------- El modo de consola ----------

    [Theory]
    [Trait("Criterio", "AC-PAG-32")]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Qa")]
    public async Task El_modo_de_consola_se_niega_fuera_de_Staging_sin_construir_el_servicio_ni_imprimir_nada(string entorno)
    {
        var salida = new StringWriter();
        var error = new StringWriter();

        var codigo = await ModoCuentaDeRevision.Ejecutar([], salida, error, entorno);

        Assert.Equal(1, codigo);
        Assert.Contains("solo existe en Staging", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, salida.ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-32")]
    public async Task El_modo_de_consola_rechaza_una_opcion_desconocida_y_no_repite_su_valor()
    {
        var error = new StringWriter();

        var codigo = await ModoCuentaDeRevision.Ejecutar(["--contrasena", "valor-que-no-debe-repetirse"], new StringWriter(), error, "Staging");

        Assert.Equal(1, codigo);
        Assert.Contains("opción desconocida", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("valor-que-no-debe-repetirse", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-32")]
    public async Task El_modo_de_consola_en_Staging_sin_la_configuracion_del_servicio_falla_diciendolo_y_sin_imprimir_nada_en_la_salida()
    {
        var salida = new StringWriter();
        var error = new StringWriter();

        var codigo = await ModoCuentaDeRevision.Ejecutar([], salida, error, "Staging");

        Assert.Equal(1, codigo);
        Assert.Contains("cuenta-de-prueba: no se pudo completar", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, salida.ToString());
    }
}
