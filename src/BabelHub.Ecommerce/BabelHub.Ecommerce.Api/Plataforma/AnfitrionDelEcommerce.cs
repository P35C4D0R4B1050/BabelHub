using BabelHub.Ecommerce.Api.Auditoria;
using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Catalogo;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Infrastructure.Biblioteca;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.Domain.Web;
using BabelHub.Ecommerce.Infrastructure.Auditoria;
using BabelHub.Ecommerce.Infrastructure.Carrito;
using BabelHub.Ecommerce.Infrastructure.Catalogo;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Notificaciones;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Web;
using BabelHub.Shared.Contratos;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace BabelHub.Ecommerce.Api.Plataforma;

/// <summary>
/// Lo que la composición del E-commerce necesita del entorno (COMP-1; SEG-11, D10): las tres cadenas de conexión (el rol de acceso y las dos cuentas de depuración, distintas entre sí), la clave del cursor de auditoría, la dirección interna
/// del Inventario, los secretos comunes (huella, credenciales de servicio, Turnstile y el Redis de contadores), las claves de Identidad, del catálogo, de las sesiones del host y de la cola de correo, y los almacenes de objetos.
/// <b>Todo es obligatorio en producción</b> y el arranque falla, nombrando la variable y nunca su valor, si falta algo o es débil; fuera de producción son opcionales solo el Redis de contadores y el servidor SMTP (con el contador local y
/// sin salida de correo).
/// </summary>
public sealed class OpcionesDelAnfitrionDelEcommerce
{
    public const string RutaDeLaBaseDeLaAplicacion = "Ecommerce:Base:Aplicacion";
    public const string RutaDeLaBaseDeDepuracionDeAuditoria = "Ecommerce:Base:DepuracionDeAuditoria";
    public const string RutaDeLaBaseDeDepuracionDeRegistros = "Ecommerce:Base:DepuracionDeRegistros";
    public const string RutaDeLaClaveDelCursor = "Auditoria:ClaveDeCursor";
    public const string RutaDeLaVersionDelCursor = "Auditoria:VersionDelCursor";
    public const string RutaDelInventario = "Ecommerce:Inventario:Direccion";
    public const string RutaDelRedis = "Ecommerce:Redis:Direccion";
    public const string RutaDeLosProxies = "Ecommerce:ProxiesConocidos";
    public const string RutaDeLaClaveDeIngresos = "Catalogo:ClaveDeIngresos";
    public const string RutaDeLaClaveDeLaCola = "Correo:ClaveDeLaCola";

    /// <summary>Los alcances de servicio que el E-commerce acepta o presenta: cada uno es una variable <c>BABELHUB__Seguridad__Credenciales__&lt;Alcance&gt;</c> (el mismo valor que en el Inventario).</summary>
    public static IReadOnlyList<AlcanceDeServicio> AlcancesRequeridos { get; } =
    [
        AlcanceDeServicio.LecturaDeDisponibilidad,
        AlcanceDeServicio.OperacionDeUsuario,
        AlcanceDeServicio.LecturaDeContacto,
        AlcanceDeServicio.ExpiracionDeReservas,
        AlcanceDeServicio.ConsultaDePendientes,
        AlcanceDeServicio.BajaDeTitular,
        AlcanceDeServicio.OrigenReenviado,
        AlcanceDeServicio.ConsultaDeAuditoria,
        AlcanceDeServicio.LecturaDeAlertas,
        AlcanceDeServicio.MarcarAlerta,
        AlcanceDeServicio.Historial,
        AlcanceDeServicio.SolicitarAviso,
    ];

    private OpcionesDelAnfitrionDelEcommerce()
    {
    }

    internal string BaseDeLaAplicacion { get; private init; } = string.Empty;

    internal string BaseDeDepuracionDeAuditoria { get; private init; } = string.Empty;

    internal string BaseDeDepuracionDeRegistros { get; private init; } = string.Empty;

    internal string ClaveDelCursor { get; private init; } = string.Empty;

    public string VersionDelCursor { get; private init; } = "v1";

    public Uri Inventario { get; private init; } = null!;

    internal ConfiguracionDelEcommerce Configuracion { get; private init; } = null!;

    internal OpcionesDelRedisDeLimites? Redis { get; private init; }

    internal OpcionesDeIdentidad Identidad { get; private init; } = null!;

    internal string ClaveDeIngresos { get; private init; } = string.Empty;

    internal OpcionesDelAlmacenDeObjetos AlmacenDelCatalogo { get; private init; } = null!;

    internal OpcionesDelAlmacenDeObjetos AlmacenDeSupresiones { get; private init; } = null!;

    internal OpcionesDelHostWeb Host { get; private init; } = null!;

    internal ConfiguracionDeSesionesWeb Sesiones { get; private init; } = null!;

    internal OpcionesDeVerificacionWeb VerificacionWeb { get; private init; } = null!;

    internal byte[] ClaveDeLaCola { get; private init; } = [];

    internal OpcionesDelCorreoSmtp? Smtp { get; private init; }

    internal AppSettingsOpcionesDeEnvio CostoDeEnvio { get; private init; } = null!;

    internal IReadOnlyList<string> Proxies { get; private init; } = [];

    public override string ToString() => "[configuración del anfitrión del E-commerce]";

    /// <summary>Lee y valida todo junto: junta todos los problemas y falla una sola vez con el nombre de cada variable (nunca su valor).</summary>
    public static OpcionesDelAnfitrionDelEcommerce Leer(IConfiguration configuracion, bool esProduccion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var lector = new LectorDeSecretos(configuracion);
        if (configuracion.GetSection("Migrador").GetChildren().Any())
        {
            lector.Problema("Migrador", "la cuenta propietaria de las bases es solo para el modo migrar y no debe estar en el entorno del servicio");
        }

        var extra = new List<string>();

        var baseDeLaAplicacion = BasesDelEcommerce.Leer(lector, RutaDeLaBaseDeLaAplicacion, esProduccion);
        var depuracionDeAuditoria = BasesDelEcommerce.Leer(lector, RutaDeLaBaseDeDepuracionDeAuditoria, esProduccion);
        var depuracionDeRegistros = BasesDelEcommerce.Leer(lector, RutaDeLaBaseDeDepuracionDeRegistros, esProduccion);
        BasesDelEcommerce.ComprobarCuentasDistintas(lector, (RutaDeLaBaseDeLaAplicacion, baseDeLaAplicacion), (RutaDeLaBaseDeDepuracionDeAuditoria, depuracionDeAuditoria), (RutaDeLaBaseDeDepuracionDeRegistros, depuracionDeRegistros));

        var claveDelCursor = lector.Obligatorio(RutaDeLaClaveDelCursor);
        if (claveDelCursor is not null)
        {
            lector.Comprobar(RutaDeLaClaveDelCursor, claveDelCursor);
        }

        var claveDeIngresos = lector.Obligatorio(RutaDeLaClaveDeIngresos);
        if (claveDeIngresos is not null)
        {
            lector.Comprobar(RutaDeLaClaveDeIngresos, claveDeIngresos);
        }

        var claveDeLaCola = lector.Obligatorio(RutaDeLaClaveDeLaCola);
        if (claveDeLaCola is not null)
        {
            lector.Comprobar(RutaDeLaClaveDeLaCola, claveDeLaCola);
        }

        // Ningún secreto del servicio repite otro (§3.2: una réplica comprometida no abre otras claves): todos los que se leen aquí, de los módulos y de los almacenes se comparan con todos los demás.
        var rutasDeSecretos = new List<string>
        {
            RutaDeLaClaveDelCursor,
            RutaDeLaClaveDeIngresos,
            RutaDeLaClaveDeLaCola,
            "Catalogo:ClaveDeIngresosAnterior",
            "Web:ClaveDeSesionesTienda",
            "Web:ClaveDeSesionesTiendaAnterior",
            "Web:ClaveDeSesionesGestion",
            "Web:ClaveDeSesionesGestionAnterior",
            OpcionesDeIdentidad.VariableDeLaClaveDeCifrado,
            OpcionesDeIdentidad.VariableDeLaPimienta,
            "Correo:Smtp:Credencial",
            "Almacen:Catalogo:ClaveDeAcceso",
            "Almacen:Catalogo:ClaveSecreta",
            "Almacen:Supresiones:ClaveDeAcceso",
            "Almacen:Supresiones:ClaveSecreta",
        };
        foreach (var ruta in rutasDeSecretos)
        {
            if (configuracion[ruta] is { Length: > 0 } valor)
            {
                RepeticionDeSecretos.Comprobar(lector, configuracion, ruta, valor);
            }
        }

        foreach (var anterior in (configuracion[OpcionesDeIdentidad.VariableDeLasClavesAnteriores] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            RepeticionDeSecretos.Comprobar(lector, configuracion, OpcionesDeIdentidad.VariableDeLasClavesAnteriores, anterior);
        }

        var version = lector.Opcional(RutaDeLaVersionDelCursor) ?? "v1";
        var inventario = Direccion(lector, RutaDelInventario, esProduccion);
        var redis = LeerRedis(lector, configuracion, esProduccion);
        var proxies = (configuracion[RutaDeLosProxies] ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (esProduccion && proxies.Length == 0)
        {
            lector.Problema(RutaDeLosProxies, "falta: en producción el origen del cliente solo se acepta de un proxy conocido");
        }

        ConfiguracionDelEcommerce? comun = Capturar(extra, () => ConfiguracionDelEcommerce.Cargar(configuracion, AlcancesRequeridos, []));
        var identidad = Capturar(extra, () => OpcionesDeIdentidad.Leer(configuracion, esProduccion));
        var almacenDelCatalogo = Capturar(extra, () => OpcionesDelAlmacenDeObjetos.Cargar(configuracion, permitirHttp: !esProduccion, raiz: "Almacen:Catalogo:"));
        var almacenDeSupresiones = Capturar(extra, () => OpcionesDelAlmacenDeObjetos.Cargar(configuracion, permitirHttp: !esProduccion, raiz: "Almacen:Supresiones:"));
        var host = Capturar(extra, () => OpcionesDelHostWeb.Leer(configuracion, esProduccion));
        var sesiones = host is null ? null : Capturar(extra, () => ConfiguracionDeSesionesWeb.Leer(configuracion, host.Host == HostWeb.Tienda ? HostDeSesion.Tienda : HostDeSesion.Gestion));
        var verificacion = Capturar(extra, () => new OpcionesDeVerificacionWeb(configuracion["Seguridad:Turnstile:ClaveDeSitio"] ?? string.Empty));
        var costo = Capturar(extra, () => AppSettingsOpcionesDeEnvio.Cargar(configuracion));
        var smtp = LeerSmtp(configuracion, esProduccion, extra);

        if (lector.Problemas.Count > 0 || extra.Count > 0)
        {
            throw new ConfiguracionDeSecretosInvalidaException([.. lector.Problemas, .. extra]);
        }

        return new OpcionesDelAnfitrionDelEcommerce
        {
            BaseDeLaAplicacion = baseDeLaAplicacion!,
            BaseDeDepuracionDeAuditoria = depuracionDeAuditoria!,
            BaseDeDepuracionDeRegistros = depuracionDeRegistros!,
            ClaveDelCursor = claveDelCursor!,
            VersionDelCursor = version,
            Inventario = inventario!,
            Configuracion = comun!,
            Redis = redis,
            Identidad = identidad!,
            ClaveDeIngresos = claveDeIngresos!,
            AlmacenDelCatalogo = almacenDelCatalogo!,
            AlmacenDeSupresiones = almacenDeSupresiones!,
            Host = host!,
            Sesiones = sesiones!,
            VerificacionWeb = verificacion!,
            ClaveDeLaCola = System.Text.Encoding.UTF8.GetBytes(claveDeLaCola!),
            Smtp = smtp,
            CostoDeEnvio = costo!,
            Proxies = proxies,
        };
    }

    private static T? Capturar<T>(List<string> problemas, Func<T> leer)
        where T : class
    {
        try
        {
            return leer();
        }
        catch (ConfiguracionDeSecretosInvalidaException e)
        {
            problemas.AddRange(e.Problemas);
        }
        catch (ClaveDeSesionesInvalidaException e)
        {
            problemas.AddRange(e.Problemas);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArranqueRechazadoException or FormatException)
        {
            // Estos lectores describen la regla incumplida con el nombre de la variable, nunca con el valor.
            problemas.Add(e.Message);
        }

        return null;
    }

    private static Uri? Direccion(LectorDeSecretos lector, string ruta, bool esProduccion)
    {
        var texto = lector.Obligatorio(ruta);
        if (texto is null)
        {
            return null;
        }

        if (!Uri.TryCreate(texto, UriKind.Absolute, out var direccion) || (direccion.Scheme != Uri.UriSchemeHttp && direccion.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(direccion.UserInfo))
        {
            lector.Problema(ruta, "no es una dirección HTTP o HTTPS válida y sin usuario");
            return null;
        }

        if (esProduccion && direccion.Scheme != Uri.UriSchemeHttps)
        {
            lector.Problema(ruta, "en producción la salida al Inventario va por HTTPS");
            return null;
        }

        return direccion;
    }

    private static OpcionesDelRedisDeLimites? LeerRedis(LectorDeSecretos lector, IConfiguration configuracion, bool esProduccion)
    {
        var direccion = configuracion[RutaDelRedis];
        var clave = configuracion["Seguridad:ClaveDeRedisDeLimites"];
        if (esProduccion && configuracion["Ecommerce:Redis:Tls"] is not ("1" or "true"))
        {
            lector.Problema("Ecommerce:Redis:Tls", "en producción el Redis de contadores va por TLS (su contraseña no viaja en claro)");
        }

        if (string.IsNullOrWhiteSpace(direccion))
        {
            if (esProduccion)
            {
                lector.Problema(RutaDelRedis, "falta: en producción los contadores de límites viven en su Redis dedicado");
            }

            return null;
        }

        return string.IsNullOrWhiteSpace(clave) ? null : new OpcionesDelRedisDeLimites(direccion.Trim(), new ValorSecreto(clave), ConTls: configuracion["Ecommerce:Redis:Tls"] is "1" or "true");
    }

    private static OpcionesDelCorreoSmtp? LeerSmtp(IConfiguration configuracion, bool esProduccion, List<string> problemas)
    {
        var servidor = configuracion["Correo:Smtp:Servidor"];
        if (string.IsNullOrWhiteSpace(servidor))
        {
            if (esProduccion)
            {
                problemas.Add(LectorDeSecretos.Variable("Correo:Smtp:Servidor") + ": falta");
            }

            return null;
        }

        return Capturar(
            problemas,
            () => new OpcionesDelCorreoSmtp(
                servidor,
                int.TryParse(configuracion["Correo:Smtp:Puerto"], out var puerto) ? puerto : 587,
                configuracion["Correo:Smtp:Cuenta"] ?? string.Empty,
                configuracion["Correo:Smtp:Credencial"] ?? string.Empty,
                configuracion["Correo:Smtp:Remitente"] ?? string.Empty));
    }
}

/// <summary>
/// La composición del servicio del E-commerce (COMP-1): <see cref="AgregarEcommerce"/> registra la plataforma (reloj, actor, huella, límites, credenciales de servicio), Auditoría, el cliente del Inventario, el correo, Identidad,
/// Catálogo, Carrito y la sesión de la web, con sus trabajos de fondo (solo en el modo normal); <see cref="UsarEcommerce"/> arma el pipeline y mapea los endpoints, y <see cref="ExigirComposicionDeProduccion"/> comprueba al arrancar
/// que ningún adaptador provisional quedó donde ya existe el real. Las migraciones de los esquemas las aplica el despliegue con la cuenta propietaria, nunca la aplicación.
/// </summary>
public static class AnfitrionDelEcommerce
{
    public static OpcionesDelAnfitrionDelEcommerce AgregarEcommerce(this WebApplicationBuilder builder, ModoDeArranque modo)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var esProduccion = builder.Environment.IsProduction();
        var opciones = OpcionesDelAnfitrionDelEcommerce.Leer(builder.Configuration, esProduccion);
        var servicios = builder.Services;
        var soloEnModoNormal = modo == ModoDeArranque.Normal;

        // ---------- Plataforma ----------
        servicios.AgregarManejoDeErrores(new OpcionesDeErrores("ECO"));
        servicios.AddHttpContextAccessor();
        servicios.AddSingleton<ActorDelFlujo>();
        servicios.AddSingleton<IActorActual>(sp => sp.GetRequiredService<ActorDelFlujo>());
        servicios.AddSingleton<IEstablecedorDeActor>(sp => sp.GetRequiredService<ActorDelFlujo>());
        servicios.AddScoped<IOrigenDeSolicitud, OrigenDeLaSolicitudHttp>();
        servicios.AgregarOrigenDelBorde(new OpcionesDeOrigenDelBorde(opciones.Proxies.Count > 0 ? opciones.Proxies : ["127.0.0.1", "::1"], aceptarDireccionesInternas: !esProduccion));
        servicios.AgregarLimitesHttp(new OpcionesDeLimitesHttp
        {
            TopePrevio = PoliticasDeLimite.SolicitudesPreviasPorOrigen,
            TopePrevioDelOrigenDesconocido = PoliticasDeLimite.SolicitudesDelOrigenDesconocido,
            SinSesionPorOrigen = PoliticasDeLimite.SolicitudesSinSesionPorOrigen,
            PorUsuario = PoliticasDeLimite.SolicitudesPorUsuario,
        });
        var aplicacion = BasesDelEcommerce.Abrir(opciones.BaseDeLaAplicacion);
        servicios.AgregarPlataformaDelEcommerce(aplicacion, opciones.Configuracion.Seguridad, [], opciones.Redis);
        servicios.AgregarCredencialDeServicio(new OpcionesDeSuperficieDeServicio { TopeDeLaDireccionDirecta = PoliticasDeLimite.SolicitudesPorHuellaDirecta }, opciones.Configuracion.Seguridad.CredencialesDeEntrada);

        // ---------- Auditoría ----------
        servicios.AddSingleton<IPlantillasDeRuta>(sp => new PlantillasDeRutaDeLaApi(sp.GetRequiredService<EndpointDataSource>()));
        servicios.AddScoped<IRutaDeLaSolicitud, RutaDeLaSolicitudHttp>();
        servicios.AgregarModuloAuditoria(new OpcionesDelModuloDeAuditoria(BasesDelEcommerce.Abrir(opciones.BaseDeDepuracionDeAuditoria), new ClavesDeCursorDelEntorno(opciones.ClaveDelCursor, opciones.VersionDelCursor)));
        servicios.AgregarRegistroDeDenegaciones();
        servicios.AgregarTrabajosDeAuditoria(soloEnModoNormal);

        // ---------- El Inventario: el token del usuario sale de la sesión (regla 9) y solo lo consume el manejador de salida ----------
        servicios.AgregarTokenDelUsuarioDeLaSesion();
        servicios.AgregarClienteDelInventario(opciones.Inventario);
        servicios.AgregarClienteDeAuditoriaDelInventario(opciones.Inventario);

        // ---------- Correo, supresiones e Identidad ----------
        servicios.AddSingleton<IRegistroDeSupresiones>(_ => new RegistroDeSupresionesEnObjetos(ClienteDelAlmacenDeObjetos.Crear(opciones.AlmacenDeSupresiones), opciones.AlmacenDeSupresiones.Contenedor));
        // En el ensayo y al restaurar no sale ningún correo: el transporte es el nulo aunque haya SMTP configurado (una base restaurada tiene personas reales).
        var smtp = soloEnModoNormal ? opciones.Smtp : null;
        servicios.AgregarCorreoDelEcommerce(opciones.ClaveDeLaCola, smtp);
        servicios.AddSingleton<IAdaptadorSaliente>(new AdaptadorSalienteDelCorreo(esNulo: smtp is null));
        servicios.AgregarNotificacionesPendientes(esProduccion && soloEnModoNormal);
        var depuracionDeRegistros = BasesDelEcommerce.Abrir(opciones.BaseDeDepuracionDeRegistros);
        servicios.AgregarIdentidad(opciones.Identidad, soloEnModoNormal, AlcanceDelSistemaDeIdentidad, _ => depuracionDeRegistros);
        servicios.AgregarTrabajosDeIdentidad(soloEnModoNormal, AlcanceDelSistemaDeIdentidad);

        // ---------- Catálogo ----------
        servicios.AgregarCatalogo(new OpcionesDelCatalogo(ClienteDelAlmacenDeObjetos.Crear(opciones.AlmacenDelCatalogo), opciones.ClaveDeIngresos, ContenedorDeLibros: opciones.AlmacenDelCatalogo.Contenedor));
        servicios.AgregarTrabajosDelCatalogo(soloEnModoNormal);

        // ---------- La web: textos legales, sesión, Carrito y verificación de persona ----------
        servicios.AgregarInterfazWeb();
        servicios.AgregarHostWeb(opciones.Host);
        servicios.AgregarProteccionContraFalsificacion();
        servicios.AgregarFiltroDeAccesoDeLaInterfaz();
        servicios.AgregarCabecerasDeSeguridad(OpcionesDeContenidoWeb.Leer(builder.Configuration));
        servicios.AgregarModuloWeb(permitirVersionesProvisionales: !esProduccion);
        servicios.AgregarSesionWeb(opciones.Sesiones);
        servicios.AgregarPoliticaDeTextosPendientes();
        servicios.AgregarPurgaDeSesionesWeb(modo);
        servicios.AgregarVerificacionDePersonaWeb(opciones.VerificacionWeb);
        servicios.AgregarCarrito(opciones.CostoDeEnvio, esProduccion && soloEnModoNormal);

        servicios.AgregarTrabajosDelCarritoEnLaApi(soloEnModoNormal);
        servicios.AgregarBibliotecaPendiente();
        return opciones;
    }

    /// <summary>El pipeline del E-commerce, en el orden que importa: cabeceras, errores, tope previo, credencial de servicio, sesión web (que fija el actor), contexto de usuario de servicio y límites generales; después los endpoints. Los archivos estáticos se sirven antes de los topes: no llevan sesión ni datos.</summary>
    public static WebApplication UsarEcommerce(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseForwardedHeaders();
        app.UsarCabecerasDeSeguridad();
        app.UsarManejoDeErrores();
        app.UseStaticFiles();
        app.UsarOrigenDelBorde();
        app.UsarRegistroDeDenegaciones();
        app.UsarTopePrevio();
        app.UsarCredencialDeServicio();
        app.UsarSesionWeb();
        app.UsarContextoDeUsuarioDeServicio();
        app.UsarLimitesGenerales();
        app.UsarLimiteDeCuerpoDelCarrito();
        app.UsarPoliticaDeTextosPendientes();
        app.MapearSalud();
        app.MapDefaultControllerRoute();
        return app;
    }

    /// <summary>La comprobación de arranque: toda operación declarada en la matriz de alcances (D10) y las validaciones de rutas de la web.</summary>
    public static void ExigirComposicionDeProduccion(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var fuente = new CompositeEndpointDataSource(((IEndpointRouteBuilder)app).DataSources);
        var problemas = SuperficieDeServicio.Verificar(
            fuente,
            app.Services.GetRequiredService<IMatrizDeAlcancesDeServicio>(),
            app.Services.GetRequiredService<OpcionesDeSuperficieDeServicio>());
        if (problemas.Count > 0)
        {
            throw new InvalidOperationException("La superficie entre servicios no declara todas sus operaciones: " + string.Join("; ", problemas));
        }

        // Una consulta automática (hx-trigger="every …") admite solo GET; la política de textos pendientes y el contenido de terceros solo en sus rutas exactas (T15, T17, T37).
        ConsultasAutomaticas.Validar(fuente.Endpoints);
        PoliticaDeTextosPendientes.Validar(fuente.Endpoints);
        RutasConContenidoDeTerceros.Validar(fuente.Endpoints);
    }

    private static Func<IServiceProvider, Infrastructure.Identidad.AlcanceDeSistemaDeIdentidad> AlcanceDelSistemaDeIdentidad =>
        sp => (corrida, cancelacion) => ComoSistema(sp, corrida, cancelacion);

    /// <summary>Fija el actor «sistema» alrededor de una corrida de fondo (nunca el de una persona) y lo limpia al terminar.</summary>
    private static async Task ComoSistema(IServiceProvider sp, Func<CancellationToken, Task> corrida, CancellationToken cancelacion)
    {
        var actor = sp.GetRequiredService<IEstablecedorDeActor>();
        actor.Establecer(ActorActual.DelSistema());
        try
        {
            await corrida(cancelacion).ConfigureAwait(false);
        }
        finally
        {
            actor.Limpiar();
        }
    }
}

/// <summary>El correo como adaptador saliente hacia un proveedor (la cuenta SMTP): es nulo cuando no hay servidor o el modo no es el normal, y <see cref="ArranqueSeguro.ExigirCoherencia"/> lo comprueba.</summary>
internal sealed class AdaptadorSalienteDelCorreo(bool esNulo) : IAdaptadorSaliente
{
    public string Nombre => "correo";

    public bool EsHaciaProveedor => true;

    public bool EsNulo => esNulo;
}
