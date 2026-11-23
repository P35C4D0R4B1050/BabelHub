using BabelHub.Ecommerce.Api.Auditoria;
using BabelHub.Ecommerce.Api.Identidad;
using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Catalogo;
using BabelHub.Ecommerce.Api.Biblioteca;
using BabelHub.Ecommerce.Api.Devoluciones;
using BabelHub.Ecommerce.Api.Envios;
using BabelHub.Ecommerce.Api.Notificaciones;
using BabelHub.Ecommerce.Api.Pedidos;
using BabelHub.Ecommerce.Api.Pagos;
using BabelHub.Ecommerce.Api.Reclamaciones;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Contacto;
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
using BabelHub.Ecommerce.Infrastructure.Envios;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using BabelHub.Ecommerce.Infrastructure.Pedidos;
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

    internal OpcionesDeNotificaciones Notificaciones { get; private init; } = null!;

    internal string ClaveDeIngresos { get; private init; } = string.Empty;

    internal OpcionesDelAlmacenDeObjetos AlmacenDelCatalogo { get; private init; } = null!;

    internal OpcionesDelAlmacenDeObjetos AlmacenDeSupresiones { get; private init; } = null!;

    /// <summary>El almacén de libros de solo lectura de Biblioteca (<c>Almacen:Biblioteca:</c>): obligatorio en producción; fuera de ella, si falta, Biblioteca entrega contenido sintético en memoria.</summary>
    internal OpcionesDelAlmacenDeObjetos? AlmacenDeBiblioteca { get; private init; }

    internal OpcionesDelHostWeb Host { get; private init; } = null!;

    internal ConfiguracionDeSesionesWeb Sesiones { get; private init; } = null!;

    internal OpcionesDeVerificacionWeb VerificacionWeb { get; private init; } = null!;

    internal byte[] ClaveDeLaCola { get; private init; } = [];

    internal OpcionesDelCorreoSmtp? Smtp { get; private init; }

    internal AppSettingsOpcionesDeEnvio CostoDeEnvio { get; private init; } = null!;

    internal IReadOnlyList<string> Proxies { get; private init; } = [];

    /// <summary>El contacto del comercio que muestran el pie y <c>/contacto</c> (COM-01 a COM-05).</summary>
    internal DatosDeContacto Contacto { get; private init; } = null!;

    public override string ToString() => "[configuración del anfitrión del E-commerce]";

    /// <summary>
    /// Lee y valida todo junto: junta todos los problemas y falla una sola vez con el nombre de cada variable (nunca su valor). <paramref name="permitirContactoDeEjemplo"/> es verdadero solo en
    /// <c>Development</c> y <c>Test</c>: allí un dato de contacto ausente se sustituye por el ficticio; en cualquier otro entorno es obligatorio (COM-03, COM-05).
    /// </summary>
    public static OpcionesDelAnfitrionDelEcommerce Leer(IConfiguration configuracion, bool esProduccion, bool permitirContactoDeEjemplo, bool soloEnModoNormal = true)
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
            "Almacen:Biblioteca:ClaveDeAcceso",
            "Almacen:Biblioteca:ClaveSecreta",
            "Pagos:ClaveDeIntentos",
            "Pagos:ClaveDeIntentosAnterior",
            "Pagos:LlavePrivada",
            OpcionesDeNotificaciones.RutaDeLaClaveDeHuella,
            OpcionesDeNotificaciones.RutaDeLaClaveDeHuellaAnterior,
            OpcionesDeNotificaciones.RutaDeLaClaveSaliente,
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

        ConfiguracionDelEcommerce? comun = Capturar(extra, () => ConfiguracionDelEcommerce.Cargar(configuracion, AlcancesRequeridos, OpcionesDeNotificaciones.ProveedoresDeFirma(configuracion, soloEnModoNormal)));
        var notificaciones = Capturar(extra, () => OpcionesDeNotificaciones.Leer(configuracion, esProduccion, soloEnModoNormal));
        var identidad = Capturar(extra, () => OpcionesDeIdentidad.Leer(configuracion, esProduccion));
        var almacenDelCatalogo = Capturar(extra, () => OpcionesDelAlmacenDeObjetos.Cargar(configuracion, permitirHttp: !esProduccion, raiz: "Almacen:Catalogo:"));
        var almacenDeSupresiones = Capturar(extra, () => OpcionesDelAlmacenDeObjetos.Cargar(configuracion, permitirHttp: !esProduccion, raiz: "Almacen:Supresiones:"));
        var almacenDeBiblioteca = esProduccion || configuracion["Almacen:Biblioteca:Endpoint"] is { Length: > 0 } ? Capturar(extra, () => OpcionesDelAlmacenDeObjetos.Cargar(configuracion, permitirHttp: !esProduccion, raiz: "Almacen:Biblioteca:")) : null;
        var host = Capturar(extra, () => OpcionesDelHostWeb.Leer(configuracion, esProduccion));
        var sesiones = host is null ? null : Capturar(extra, () => ConfiguracionDeSesionesWeb.Leer(configuracion, host.Host == HostWeb.Tienda ? HostDeSesion.Tienda : HostDeSesion.Gestion));
        var verificacion = Capturar(extra, () => new OpcionesDeVerificacionWeb(configuracion["Seguridad:Turnstile:ClaveDeSitio"] ?? string.Empty));
        // Pagos: la pasarela se declara siempre (el simulador solo vale en Development, Test y Staging; «culqi» aún no tiene adaptador) y la clave de los intentos es un secreto; el resto lo valida su guarda de arranque nombrando la variable.
        _ = lector.Obligatorio("Pagos:Pasarela");
        if (lector.Obligatorio("Pagos:ClaveDeIntentos") is { } claveDeIntentos)
        {
            lector.Comprobar("Pagos:ClaveDeIntentos", claveDeIntentos);
        }

        var costo = Capturar(extra, () => AppSettingsOpcionesDeEnvio.Cargar(configuracion));
        var smtp = LeerSmtp(configuracion, esProduccion, extra);
        // Los problemas del contacto salen por el lector (nombran la variable, nunca el valor).
        var contacto = DatosDeContacto.Leer(lector, permitirContactoDeEjemplo);

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
            Notificaciones = notificaciones!,
            ClaveDeIngresos = claveDeIngresos!,
            AlmacenDelCatalogo = almacenDelCatalogo!,
            AlmacenDeSupresiones = almacenDeSupresiones!,
            AlmacenDeBiblioteca = almacenDeBiblioteca,
            Host = host!,
            Sesiones = sesiones!,
            VerificacionWeb = verificacion!,
            ClaveDeLaCola = System.Text.Encoding.UTF8.GetBytes(claveDeLaCola!),
            Smtp = smtp,
            CostoDeEnvio = costo!,
            Proxies = proxies,
            Contacto = contacto!,
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
        // Producción y Staging se endurecen igual (PAG-34): lo único que Staging afloja es el cobro, con la pasarela simulada.
        var esProduccion = builder.Environment.EsDeTipoProduccion();
        var soloEnModoNormal = modo == ModoDeArranque.Normal;
        // Los valores ficticios del contacto solo valen en Development y Test; cualquier otro nombre de entorno (Staging, Production, ensayo…) exige las tres variables (COM-03, COM-05).
        var permitirContactoDeEjemplo = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test");
        var opciones = OpcionesDelAnfitrionDelEcommerce.Leer(builder.Configuration, esProduccion, permitirContactoDeEjemplo, soloEnModoNormal);
        var servicios = builder.Services;
        servicios.AddSingleton(opciones.Contacto);

        // ---------- Plataforma ----------
        servicios.AgregarManejoDeErrores(new OpcionesDeErrores("ECO"));
        servicios.AgregarDocumentacionOpenApi();
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
            // La entrada de avisos de la pasarela no cuenta en el tope previo ni en el límite sin sesión (SEG-18): su único límite es la ráfaga por origen, antes de verificar la firma.
            RutasExentas = ["/health", AvisosDeLaPasarelaEnLaApi.Ruta, EstadosDeEntregaDeN8nEnLaApi.Ruta],
        });
        var aplicacion = BasesDelEcommerce.Abrir(opciones.BaseDeLaAplicacion);
        // La superficie de servicio del E-commerce declara las operaciones que atiende hoy: la solicitud de aviso del Inventario (alcance solicitar-aviso).
        (OperacionDeServicio Operacion, AlcanceDeServicio Alcance)[] declaraciones =
        [
            (new OperacionDeServicio(EndpointsDeNotificaciones.OperacionDeLasSolicitudesDelInventario), AlcanceDeServicio.SolicitarAviso),
            (new OperacionDeServicio(EndpointsDePedidos.OperacionDeLosEstadosDeTraslado), AlcanceDeServicio.SolicitarAviso),
        ];
        servicios.AgregarPlataformaDelEcommerce(aplicacion, opciones.Configuracion.Seguridad, opciones.Configuracion.Turnstile, soloEnModoNormal, declaraciones, opciones.Redis);
        servicios.AddSingleton<IAdaptadorSaliente>(new AdaptadorSalienteDelProveedor("turnstile", esNulo: !soloEnModoNormal));
        if (soloEnModoNormal)
        {
            // La salud (/health/ready) comprueba la base de la aplicación: en ensayo y restauración no hay una base con la que comprobar.
            servicios.AddSingleton(new ComprobacionDeLaBaseDeDatos(aplicacion));
            servicios.AddSingleton<IComprobacionDeSalud, ComprobacionDeSaludDeLaBase>();
        }
        // Solo en Development y con la variable explícita: las pruebas E2E locales llaman entre procesos por HTTP (no hay TLS entre ellos). Nunca se enciende en producción ni por defecto.
        var servicioSinHttps = builder.Environment.IsDevelopment() && string.Equals(builder.Configuration["Seguridad:ServicioSinHttps"], "true", StringComparison.OrdinalIgnoreCase);
        servicios.AgregarCredencialDeServicio(new OpcionesDeSuperficieDeServicio { ExigirHttps = !servicioSinHttps, TopeDeLaDireccionDirecta = PoliticasDeLimite.SolicitudesPorHuellaDirecta }, opciones.Configuracion.Seguridad.CredencialesDeEntrada);

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
        // Notificaciones: el envío es n8n (o el simulado en desarrollo y pruebas, o el nulo en el ensayo y al restaurar) y reemplaza a los dobles provisionales; el adaptador declara su estado real y ExigirCoherencia lo comprueba.
        servicios.AgregarNotificaciones(opciones.Notificaciones);
        servicios.AddSingleton<IAdaptadorSaliente>(new AdaptadorSalienteDeLaInstancia("notificador", esNulo: opciones.Notificaciones.Envio == ModoDelEnvioDeAvisos.Nulo));
        // El trabajo de envío corre solo en la réplica de la tienda (VM-VENTAS, que alcanza a n8n): la de gestión (VM-INVENTARIO) no tiene ruta al puerto 5678 y competiría por los avisos fallándolos.
        servicios.AgregarTrabajosDeNotificacionesEnLaApi(soloEnModoNormal && opciones.Host.Host == HostWeb.Tienda);
        var depuracionDeRegistros = BasesDelEcommerce.Abrir(opciones.BaseDeDepuracionDeRegistros);
        servicios.AgregarIdentidad(opciones.Identidad, soloEnModoNormal, AlcanceDelSistemaDeIdentidad, _ => depuracionDeRegistros);
        servicios.AgregarTrabajosDeIdentidad(soloEnModoNormal, AlcanceDelSistemaDeIdentidad);

        // ---------- Catálogo ----------
        // Los libros y las portadas del catálogo van a sus contenedores («libros» y «portadas», infra/almacen/contenedores.txt): el contenedor del registro de supresiones (el valor por omisión de las opciones del almacén) no es el de los libros.
        servicios.AgregarCatalogo(new OpcionesDelCatalogo(ClienteDelAlmacenDeObjetos.Crear(opciones.AlmacenDelCatalogo), opciones.ClaveDeIngresos));
        servicios.AgregarTrabajosDelCatalogo(soloEnModoNormal);

        // ---------- La web: textos legales, sesión, Carrito y verificación de persona ----------
        servicios.AgregarInterfazWeb();
        servicios.AgregarIngresoWeb();
        servicios.AgregarHostWeb(opciones.Host);
        servicios.AgregarProteccionContraFalsificacion();
        servicios.AgregarFiltroDeAccesoDeLaInterfaz();
        servicios.AgregarCabecerasDeSeguridad(OpcionesDeContenidoWeb.Leer(builder.Configuration));
        servicios.AgregarModuloWeb(permitirVersionesProvisionales: !esProduccion);
        servicios.AgregarSesionWeb(opciones.Sesiones);
        servicios.AgregarPoliticaDeTextosPendientes();
        servicios.AgregarPurgaDeSesionesWeb(modo);
        servicios.AgregarVerificacionDePersonaWeb(opciones.VerificacionWeb);
        // Las guardas de producción de los dobles provisionales (Carrito, Pagos y Pedidos) corren al final, cuando ya están todas las implementaciones reales: cada módulo reemplaza o completa lo del anterior.
        servicios.AgregarCarrito(opciones.CostoDeEnvio, esProduccion: false);

        servicios.AgregarTrabajosDelCarritoEnLaApi(soloEnModoNormal);

        // ---------- Pagos, Envíos, Reclamaciones y Pedidos ----------
        // Pagos reemplaza al creador de intenciones provisional de Carrito; en el ensayo y al restaurar la pasarela es siempre la nula (nunca una llamada a un proveedor). «culqi» aún no tiene adaptador: su guarda lo rechaza nombrando la variable.
        var guardarProduccion = esProduccion && soloEnModoNormal;
        var opcionesDePagos = LectorDeOpcionesDePagos.Leer(builder.Configuration, builder.Environment.EnvironmentName, sinLlamadasReales: !soloEnModoNormal, pedidosDisponible: true);
        servicios.AgregarPagos(opcionesDePagos, esProduccion: false);
        servicios.AgregarTrabajosDePagosEnLaApi(soloEnModoNormal);

        // Envíos antes de los dobles de Pedidos (su guarda exige que las implementaciones reales ya estén); el operador logístico es el nulo salvo el simulador en Development y Test. En el ensayo y al restaurar siempre es el nulo (nunca una llamada a un proveedor).
        servicios.AgregarEnvios(guardarProduccion);
        var opcionesDeEnvios = new OpcionesDeArranqueDeEnvios(soloEnModoNormal ? builder.Configuration["Envios:Operador"] : GuardaDeArranqueDeEnvios.Nulo, builder.Environment.EnvironmentName, SinLlamadasReales: !soloEnModoNormal);
        servicios.AgregarOperadorLogisticoDeEnvios(opcionesDeEnvios);
        servicios.AgregarTrabajosDeEnviosEnLaApi(soloEnModoNormal);
        // Los dos adaptadores hacia un proveedor declaran su estado real (leído de las mismas opciones con las que se compuso, no de una suposición aparte): ExigirCoherencia lo comprueba en el ensayo y al restaurar.
        servicios.AddSingleton<IAdaptadorSaliente>(new AdaptadorSalienteDeLaInstancia("pasarela-de-pago", esNulo: opcionesDePagos.PasarelaEsNula));
        // El aviso «Pagos simulados» (PAG-33) sale de las mismas opciones con las que se compuso la pasarela: nunca dice una cosa y hace otra.
        servicios.AddSingleton(new ModoDePagosDeLaWeb(Simulado: opcionesDePagos is { Pasarela: GuardaDeArranqueDePagos.Simulador, PasarelaEsNula: false }));
        var operadorElegido = GuardaDeArranqueDeEnvios.Exigir(opcionesDeEnvios);
        servicios.AddSingleton<IAdaptadorSaliente>(new AdaptadorSalienteDeLaInstancia("operador-logistico", esNulo: operadorElegido == GuardaDeArranqueDeEnvios.Nulo));
        // El aviso «Los envíos son simulados» (PAG-42) sale de la misma elección: mientras el operador es el simulador (no hay uno real, DD-22) la tienda lo dice, en cualquier entorno.
        servicios.AddSingleton(new ModoDeEnviosDeLaWeb(Simulado: operadorElegido == GuardaDeArranqueDeEnvios.Simulador && soloEnModoNormal));
        servicios.AgregarReclamaciones();
        servicios.AgregarTrabajosDeReclamacionesEnLaApi(soloEnModoNormal);
        servicios.AgregarPedidos(esProduccion: false);
        servicios.AgregarTrabajosDePedidosEnLaApi(soloEnModoNormal);
        servicios.AgregarBiblioteca(guardarProduccion, opciones.AlmacenDeBiblioteca);
        servicios.AgregarTrabajosDeBibliotecaEnLaApi(soloEnModoNormal);
        // Devoluciones va después de Pagos, Pedidos y Biblioteca (necesita IReembolsos, el contrato de lectura de Pedidos y IAccesosParaDevoluciones reales) y del cliente del Inventario; sus trabajos de fondo solo corren en modo normal.
        servicios.AgregarDevoluciones(guardarProduccion);
        servicios.AgregarTrabajosDeDevolucionesEnLaApi(soloEnModoNormal);
        servicios.AgregarDoblesProvisionalesDelCarrito(guardarProduccion);
        servicios.AgregarDoblesProvisionalesDePagos(guardarProduccion);
        servicios.AgregarDoblesProvisionalesDePedidos(guardarProduccion);
#if DEBUG
        // Solo en una compilación Debug y en Development (nunca en la imagen de producción, que se publica en Release): el sembrador de sesiones de los recorridos E2E.
        servicios.AgregarSembradorDeSesiones(builder.Environment.IsDevelopment());
#endif
        return opciones;
    }

    /// <summary>El pipeline del E-commerce, en el orden que importa: cabeceras, errores, tope previo, credencial de servicio, sesión web (que fija el actor), contexto de usuario de servicio y límites generales; después los endpoints. Los archivos estáticos se sirven antes de los topes: no llevan sesión ni datos.</summary>
    public static WebApplication UsarEcommerce(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseForwardedHeaders();
        // Detrás de Cloudflare, lo que el borde declara HTTP se redirige a HTTPS aunque la regla del borde no esté activa; en Development y Test no (los recorridos locales van por HTTP).
        if (app.Environment.EsDeTipoProduccion())
        {
            app.UsarRedireccionAHttps();
        }

        app.UsarCabecerasDeSeguridad();
        app.UsarCabecerasDelIndiceInfinito();
        app.UsarManejoDeErrores();
        app.UseStaticFiles();
        app.UsarOrigenDelBorde();
        app.UsarLimiteDeAvisosDeLaPasarela();
        app.UsarRegistroDeDenegaciones();
        app.UsarTopePrevio();
        app.UsarCredencialDeServicio();
        app.UsarSesionWeb();
        app.UsarContextoDeUsuarioDeServicio();
        app.UsarLimitesGenerales();
        app.UsarLimiteDeCuerpoDelCarrito();
        app.UsarPoliticaDeTextosPendientes();
        app.MapearSalud();
        // La entrada de avisos de la pasarela solo se mapea con un esquema de firma verificable (hoy ninguno: «culqi» no tiene adaptador), así que responde 404.
        app.MapearAvisosDeLaPasarela(habilitada: false);
        // Notificaciones: la solicitud del Inventario y la administración (desactivar, reactivar y consultar); los estados de entrega de n8n solo existen si el envío es por n8n (con su firma verificable) y son de la red interna.
        app.MapearNotificaciones();
        // Pedidos: los estados de traslado que informa el Inventario (alcance solicitar-aviso, operación ecommerce.traslados.informar-estado).
        app.MapearPedidos();
        app.MapearEstadosDeEntregaDeN8n(habilitada: app.Services.GetService<OpcionesDeEnvioPorN8n>() is not null);
        // Auditoría, alertas e historial (T28) y las claves de Identidad (T44): la publicación de la clave de verificación y de las revocaciones va donde la sirva la réplica; rotar y declarar comprometida solo en el host de gestión (D03).
        app.MapearAuditoria();
        app.MapearClavesDeIdentidad(conAdministracion: app.Services.GetRequiredService<OpcionesDelHostWeb>().Host == HostWeb.Gestion);
#if DEBUG
        app.MapearSembradorDeSesiones();
#endif
        app.MapearDocumentacionOpenApi();
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

/// <summary>Un adaptador hacia un proveedor externo (por ahora Turnstile): es nulo cuando el modo no es el normal, y <see cref="ArranqueSeguro.ExigirCoherencia"/> lo comprueba.</summary>
internal sealed class AdaptadorSalienteDelProveedor(string nombre, bool esNulo) : IAdaptadorSaliente
{
    public string Nombre => nombre;

    public bool EsHaciaProveedor => true;

    public bool EsNulo => esNulo;
}

/// <summary>Un adaptador saliente cuyo estado sale de las opciones con las que se compuso el adaptador real.</summary>
internal sealed class AdaptadorSalienteDeLaInstancia(string nombre, bool esNulo) : IAdaptadorSaliente
{
    public string Nombre => nombre;

    public bool EsHaciaProveedor => true;

    public bool EsNulo => esNulo;
}
