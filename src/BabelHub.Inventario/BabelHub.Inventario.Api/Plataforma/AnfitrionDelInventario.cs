using BabelHub.Inventario.Api.Ajustes;
using BabelHub.Inventario.Api.Auditoria;
using BabelHub.Inventario.Api.Fuentes;
using BabelHub.Inventario.Api.Reservas;
using BabelHub.Inventario.Api.Traslados;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Plataforma;
using BabelHub.Inventario.Infrastructure.Auditoria;
using BabelHub.Inventario.Infrastructure.Composicion;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace BabelHub.Inventario.Api.Plataforma;

/// <summary>
/// Lo que la composición del Inventario necesita del entorno (COMP-1; SEG-11, D10): las tres cadenas de conexión (el rol de acceso de la aplicación y las dos cuentas de depuración, distintas entre sí), la clave del cursor de
/// auditoría, las direcciones internas de la clave de verificación de Identidad y del E-commerce, y los secretos comunes (huella y credenciales de servicio). <b>Todo es obligatorio en producción</b> y el arranque falla,
/// nombrando la variable y nunca su valor, si falta algo o es débil; fuera de producción también se exige lo que no tiene un valor seguro por omisión.
/// </summary>
public sealed class OpcionesDelAnfitrionDelInventario
{
    public const string RutaDeLaBaseDeLaAplicacion = "Inventario:Base:Aplicacion";
    public const string RutaDeLaBaseDeDepuracionDeAjustes = "Inventario:Base:DepuracionDeAjustes";
    public const string RutaDeLaBaseDeDepuracionDeAuditoria = "Inventario:Base:DepuracionDeAuditoria";
    public const string RutaDeLaClaveDelCursor = "Auditoria:ClaveDeCursor";
    public const string RutaDeLaVersionDelCursor = "Auditoria:VersionDelCursor";
    public const string RutaDeLaClaveDeVerificacionDeIdentidad = "Identidad:UrlDeLaClaveDeVerificacion";
    public const string RutaDeLasRevocacionesDeIdentidad = "Identidad:UrlDeLasRevocaciones";
    public const string RutaDelEcommerce = "Inventario:Ecommerce:Direccion";

    /// <summary>Los alcances de servicio que el Inventario acepta o presenta: cada uno es una variable <c>BABELHUB__Seguridad__Credenciales__&lt;Alcance&gt;</c>.</summary>
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

    private OpcionesDelAnfitrionDelInventario(
        string baseDeLaAplicacion,
        string baseDeDepuracionDeAjustes,
        string baseDeDepuracionDeAuditoria,
        string claveDelCursor,
        string versionDelCursor,
        OpcionesDeClavesPublicadas clavesDeIdentidad,
        Uri ecommerce,
        ConfiguracionDeSeguridad seguridad)
    {
        BaseDeLaAplicacion = baseDeLaAplicacion;
        BaseDeDepuracionDeAjustes = baseDeDepuracionDeAjustes;
        BaseDeDepuracionDeAuditoria = baseDeDepuracionDeAuditoria;
        ClaveDelCursor = claveDelCursor;
        VersionDelCursor = versionDelCursor;
        ClavesDeIdentidad = clavesDeIdentidad;
        Ecommerce = ecommerce;
        Seguridad = seguridad;
    }

    internal string BaseDeLaAplicacion { get; }

    internal string BaseDeDepuracionDeAjustes { get; }

    internal string BaseDeDepuracionDeAuditoria { get; }

    internal string ClaveDelCursor { get; }

    public string VersionDelCursor { get; }

    public OpcionesDeClavesPublicadas ClavesDeIdentidad { get; }

    public Uri Ecommerce { get; }

    internal ConfiguracionDeSeguridad Seguridad { get; }

    public override string ToString() => "[configuración del anfitrión del Inventario]";

    /// <summary>Lee y valida todo junto: junta todos los problemas y falla una sola vez con el nombre de cada variable (nunca su valor).</summary>
    public static OpcionesDelAnfitrionDelInventario Leer(IConfiguration configuracion, bool esProduccion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var lector = new LectorDeSecretos(configuracion);
        if (configuracion.GetSection("Migrador").GetChildren().Any())
        {
            lector.Problema("Migrador", "la cuenta propietaria de las bases es solo para el modo migrar y no debe estar en el entorno del servicio");
        }

        var baseDeLaAplicacion = BasesDelInventario.Leer(lector, RutaDeLaBaseDeLaAplicacion, esProduccion);
        var depuracionDeAjustes = BasesDelInventario.Leer(lector, RutaDeLaBaseDeDepuracionDeAjustes, esProduccion);
        var depuracionDeAuditoria = BasesDelInventario.Leer(lector, RutaDeLaBaseDeDepuracionDeAuditoria, esProduccion);
        BasesDelInventario.ComprobarCuentasDistintas(lector, (RutaDeLaBaseDeLaAplicacion, baseDeLaAplicacion), (RutaDeLaBaseDeDepuracionDeAjustes, depuracionDeAjustes), (RutaDeLaBaseDeDepuracionDeAuditoria, depuracionDeAuditoria));

        var claveDelCursor = lector.Obligatorio(RutaDeLaClaveDelCursor);
        if (claveDelCursor is not null)
        {
            lector.Comprobar(RutaDeLaClaveDelCursor, claveDelCursor);
            RepeticionDeSecretos.Comprobar(lector, configuracion, RutaDeLaClaveDelCursor, claveDelCursor);
        }

        if (ClaveDeBajasDelEntorno.Leer(lector) is not null && configuracion["Inventario:ClaveDeBajas"] is { Length: > 0 } clave)
        {
            RepeticionDeSecretos.Comprobar(lector, configuracion, "Inventario:ClaveDeBajas", clave);
        }

        var version = lector.Opcional(RutaDeLaVersionDelCursor) ?? "v1";
        var claves = DireccionesDeIdentidad(lector);
        var ecommerce = Direccion(lector, RutaDelEcommerce);
        if (ecommerce is not null && esProduccion && ecommerce.Scheme != Uri.UriSchemeHttps)
        {
            lector.Problema(RutaDelEcommerce, "en producción la salida al E-commerce va por HTTPS");
        }

        // Los secretos comunes (huella y credenciales de servicio) se leen aparte: sus problemas se juntan con los de arriba para fallar una sola vez.
        ConfiguracionDeSeguridad? seguridad = null;
        IReadOnlyList<string> problemasDeSeguridad = [];
        try
        {
            seguridad = ConfiguracionDeSeguridad.Cargar(configuracion, AlcancesRequeridos, []);
        }
        catch (ConfiguracionDeSecretosInvalidaException e)
        {
            problemasDeSeguridad = e.Problemas;
        }

        if (lector.Problemas.Count > 0 || problemasDeSeguridad.Count > 0)
        {
            throw new ConfiguracionDeSecretosInvalidaException([.. lector.Problemas, .. problemasDeSeguridad]);
        }


        return new OpcionesDelAnfitrionDelInventario(baseDeLaAplicacion!, depuracionDeAjustes!, depuracionDeAuditoria!, claveDelCursor!, version, claves!, ecommerce!, seguridad!);
    }

    private static Uri? Direccion(LectorDeSecretos lector, string ruta)
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

        return direccion;
    }

    private static OpcionesDeClavesPublicadas? DireccionesDeIdentidad(LectorDeSecretos lector)
    {
        var jwks = Direccion(lector, RutaDeLaClaveDeVerificacionDeIdentidad);
        var revocaciones = Direccion(lector, RutaDeLasRevocacionesDeIdentidad);
        if (jwks is null || revocaciones is null)
        {
            return null;
        }

        try
        {
            return new OpcionesDeClavesPublicadas(jwks, revocaciones);
        }
        catch (ArgumentException)
        {
            lector.Problema(RutaDeLaClaveDeVerificacionDeIdentidad, "debe ser HTTPS, o HTTP solo hacia la propia máquina (la réplica de gestión de esta VM)");
            return null;
        }
    }
}

/// <summary>
/// La composición del servicio del Inventario (COMP-1): lo que antes solo existía en los anfitriones de prueba. <see cref="AgregarInventario"/> registra la plataforma (reloj, actor, huella, límites sobre PostgreSQL, credenciales
/// de servicio), el módulo de auditoría, los cuatro módulos (Fuentes, Reservas, Ajustes y Traslados), la verificación de credenciales de usuario (T41) y los trabajos de fondo (solo en el modo normal); <see cref="UsarInventario"/> arma
/// el pipeline y mapea los endpoints, y <see cref="ExigirComposicionDeProduccion"/> comprueba al arrancar que ningún adaptador nulo quedó donde ya existe el real. Las migraciones de los esquemas las aplica el despliegue con la
/// cuenta propietaria, nunca la aplicación.
/// </summary>
public static class AnfitrionDelInventario
{
    public static OpcionesDelAnfitrionDelInventario AgregarInventario(this WebApplicationBuilder builder, ModoDeArranque modo)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var opciones = OpcionesDelAnfitrionDelInventario.Leer(builder.Configuration, builder.Environment.IsProduction());
        var servicios = builder.Services;
        var soloEnModoNormal = modo == ModoDeArranque.Normal;

        // ---------- Plataforma ----------
        var errores = new OpcionesDeErrores(
            "INV",
            ErroresDeFuentes.Especificos.Concat(ErroresDeReservas.Especificos).Concat(ErroresDeAjustes.Especificos).Concat(ErroresDeTraslados.Especificos).ToDictionary(p => p.Key, p => p.Value));
        servicios.AgregarManejoDeErrores(errores);
        servicios.AgregarDocumentacionOpenApi();
        servicios.AddHttpContextAccessor();
        servicios.AddSingleton<IReloj, RelojDelSistema>();
        servicios.AddSingleton<ActorDelFlujo>();
        servicios.AddSingleton<IActorActual>(sp => sp.GetRequiredService<ActorDelFlujo>());
        servicios.AddSingleton<IEstablecedorDeActor>(sp => sp.GetRequiredService<ActorDelFlujo>());
        servicios.AddScoped<IOrigenDeSolicitud, OrigenDeLaSolicitudHttp>();
        servicios.AddSingleton<ICalculadorDeHuella>(new CalculadorDeHuellaHmac(opciones.Seguridad.Huella));
        servicios.AddSingleton(opciones.Seguridad.CredencialesDeEntrada);
        servicios.AddSingleton(opciones.Seguridad.CredencialesDeSalida);
        servicios.AgregarLimitesHttp(new OpcionesDeLimitesHttp
        {
            PorUsuario = PoliticasDeLimite.SolicitudesPorUsuario,
        });

        // La matriz de alcances: lo que cada operación declarada permite a cada credencial de servicio (rechazo por defecto).
        servicios.AgregarSeguridadYLimitesDelInventario([.. MatrizDeFuentes.Declaraciones(), .. MatrizDeReservas.Declaraciones(), .. MatrizDeAjustes.Declaraciones(), .. MatrizDeTraslados.Declaraciones(), .. MatrizDeAuditoria.Declaraciones()]);
        servicios.AgregarCredencialDeServicio(new OpcionesDeSuperficieDeServicio { TopeDeLaDireccionDirecta = PoliticasDeLimite.SolicitudesPorHuellaDirecta }, opciones.Seguridad.CredencialesDeEntrada);

        // ---------- Datos: el rol de acceso de la aplicación y las cuentas de depuración, aparte ----------
        BasesDelInventario.RegistrarLaDeLaAplicacion(servicios, opciones.BaseDeLaAplicacion);
        if (soloEnModoNormal)
        {
            // La salud (/health/ready) comprueba la base de la aplicación: en ensayo y restauración no hay una base con la que comprobar.
            servicios.AddSingleton<ComprobacionDeLaBaseDeDatos>();
            servicios.AddSingleton<IComprobacionDeSalud, ComprobacionDeSaludDeLaBase>();
        }

        servicios.AddSingleton<IRegistroDeTrabajosDeFondo>(sp => new RegistroDeTrabajosDeFondoPostgres(sp.GetRequiredService<UnidadDeTrabajoPostgres>()));

        // ---------- Auditoría y los cuatro módulos ----------
        servicios.AddSingleton<IPlantillasDeRuta>(sp => new PlantillasDeRutaDeLaApi(sp.GetRequiredService<EndpointDataSource>()));
        servicios.AgregarModuloAuditoria(new OpcionesDelModuloDeAuditoria(BasesDelInventario.Abrir(opciones.BaseDeDepuracionDeAuditoria), new ClavesDeCursorDelEntorno(opciones.ClaveDelCursor, opciones.VersionDelCursor)));
        servicios.AgregarRegistroDeDenegaciones();
        servicios.AgregarModuloFuentes();
        servicios.AgregarModuloReservas(builder.Configuration);
        servicios.AgregarModuloAjustes(new OpcionesDelModuloDeAjustes(BasesDelInventario.Abrir(opciones.BaseDeDepuracionDeAjustes)));
        servicios.AgregarModuloTraslados();
        servicios.AgregarSalidaAlEcommerce(opciones.Ecommerce, permitirHttp: !builder.Environment.IsProduction());

        // ---------- Verificación de las credenciales de usuario (T41) ----------
        servicios.AgregarVerificacionDeCredencialesDeUsuario(opciones.ClavesDeIdentidad, soloEnModoNormal);

        // ---------- Trabajos de fondo: solo en el modo normal (PLA-19) ----------
        servicios.AgregarTrabajosDeAuditoria(soloEnModoNormal);
        servicios.AgregarTrabajosDeReservas(soloEnModoNormal);
        servicios.AgregarTrabajosDeAjustes(soloEnModoNormal);
        servicios.AgregarTrabajosDeTraslados(soloEnModoNormal);
        return opciones;
    }

    /// <summary>El pipeline del Inventario, en el orden que importa: errores, tope previo, credencial de servicio, usuario verificado, contexto de usuario, actor de servicio y límites generales; después los endpoints.</summary>
    public static WebApplication UsarInventario(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UsarManejoDeErrores();
        app.UsarRegistroDeDenegaciones();
        app.UsarTopePrevio();
        app.UsarCredencialDeServicio();
        app.UsarCredencialDeUsuario();
        app.UsarContextoDeUsuarioDeServicio();
        app.UsarActorDeServicio();
        app.UsarLimitesGenerales();
        app.MapearSalud();
        app.MapearFuentes();
        app.MapearReservas();
        app.MapearAjustes();
        app.MapearTraslados();
        app.MapearAuditoria();
        app.MapearDocumentacionOpenApi();
        return app;
    }

    /// <summary>La comprobación de arranque: ningún puerto resuelto por un adaptador nulo cuando su módulo real existe, la clave de bajas y la estrategia de selección válidas, y toda operación declarada en la matriz (D10).</summary>
    public static void ExigirComposicionDeProduccion(this WebApplication app, bool esProduccion)
    {
        ArgumentNullException.ThrowIfNull(app);
        ComposicionDeFuentes.ExigirAdaptadoresReales(app.Services, esProduccion);
        _ = ComposicionDeReservas.ExigirClaveDeBajas(app.Services);
        _ = ComposicionDeReservas.ExigirEstrategiaDeSeleccion(app.Services);
        var fuente = new CompositeEndpointDataSource(((IEndpointRouteBuilder)app).DataSources);
        var superficie = app.Services.GetRequiredService<OpcionesDeSuperficieDeServicio>();
        var problemas = SuperficieDeServicio.Verificar(fuente, app.Services.GetRequiredService<IMatrizDeAlcancesDeServicio>(), superficie).ToList();

        if (problemas.Count > 0)
        {
            throw new InvalidOperationException("La superficie entre servicios no declara todas sus operaciones: " + string.Join("; ", problemas));
        }
    }
}
