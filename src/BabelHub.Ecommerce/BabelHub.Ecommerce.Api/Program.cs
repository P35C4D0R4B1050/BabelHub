using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;

// El modo migrar (COMP-2): aplica los esquemas y los roles de la base con la cuenta propietaria y termina; no arranca el servidor.
if (args.Length > 0 && args[0] == ModoMigrar.Argumento)
{
    return await ModoMigrar.Ejecutar(new ConfigurationBuilder().AddEnvironmentVariables(prefix: "BABELHUB__").Build(), Console.Out, Console.Error);
}

// La orden de operación cargar-catalogo (transversal-carga-inicial): carga obras, ediciones, descripciones, precios y portadas por los casos de uso del Catálogo con el modo Operacion; no abre puertos ni arranca el servidor.
if (args.Length > 0 && args[0] == ModoCargarCatalogo.Argumento)
{
    return await ModoCargarCatalogo.Ejecutar(args, Console.Out, Console.Error);
}

var builder = WebApplication.CreateBuilder(args);

// Las variables BABELHUB__… son la configuración del servicio (SEG-11): BABELHUB__Modo se lee como «Modo».
builder.Configuration.AddEnvironmentVariables(prefix: "BABELHUB__");

// El modo de arranque (PLA-19, PLA-43): en producción, Restauracion y Ensayo solo arrancan con la marca de un solo uso de restaurar.sh.
var modo = ArranqueSeguro.Resolver(
    builder.Configuration,
    builder.Environment.IsProduction(),
    TimeProvider.System.GetUtcNow(),
    new AlmacenDeNoncesEnArchivos(builder.Configuration["Restauracion:CarpetaDeMarcas"] ?? "/var/lib/babelhub/marcas"));
builder.Services.AddSingleton(typeof(ModoDeArranque), modo);
builder.Services.AddScoped<EjecutorDePasosDeRestauracion>();
builder.Services.AddControllersWithViews();
// Detrás del borde la aplicación recibe HTTP: solo se toma el esquema (X-Forwarded-Proto) para que las cookies __Host- y el antiforgery vean HTTPS. El Api solo es alcanzable desde la red del borde (SEG-17); el origen del cliente lo fija
// OrigenDelBorde con la lista de proxies conocidos (BABELHUB__Ecommerce__ProxiesConocidos).
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// La composición real del servicio (COMP-1): plataforma, Auditoría, Identidad, Catálogo, Carrito, la sesión de la web y los trabajos de fondo.
// Falta una variable obligatoria o es débil: el arranque falla nombrando la variable (nunca su valor).
builder.AgregarEcommerce(modo);

var app = builder.Build();
ArranqueSeguro.ExigirCoherencia(modo, builder.Environment.IsProduction(), app.Services.GetServices<IAdaptadorSaliente>());

if (modo == ModoDeArranque.Restauracion)
{
    // Antes de abrir: solo los pasos de restauración, en orden, sin trabajos de fondo ni adaptadores hacia proveedores. El servicio no
    // escucha: termina con 0 si puede abrirse y con 1 si no, y restaurar.sh lo arranca después en modo normal.
    await using var alcance = app.Services.CreateAsyncScope();
    var resultado = await alcance.ServiceProvider.GetRequiredService<EjecutorDePasosDeRestauracion>().Ejecutar();
    foreach (var paso in resultado.PasosEjecutados)
    {
        Console.WriteLine($"paso: {paso}");
    }

    Console.WriteLine(resultado.PermiteAbrir ? "restauracion: puede abrirse" : $"restauracion: no se abre, falló el paso {resultado.PasoFallido}");
    return resultado.PermiteAbrir ? 0 : 1;
}

app.UsarEcommerce();
// Con los endpoints ya mapeados: toda operación declarada en la matriz de alcances y las validaciones de rutas de la web.
app.ExigirComposicionDeProduccion();

app.Run();
return 0;
