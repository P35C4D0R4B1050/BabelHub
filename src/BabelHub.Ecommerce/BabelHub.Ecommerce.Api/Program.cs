using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Biblioteca;
using BabelHub.Ecommerce.Infrastructure.Catalogo;
using BabelHub.Ecommerce.Infrastructure.Web;

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
builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("ECO"));
builder.Services.AddSingleton<EjecutorDePasosDeRestauracion>();
builder.Services.AgregarInterfazWeb();
// El host de este proceso (tienda o gestion) decide qué áreas se mapean (D03). En producción BABELHUB__Web__Host y BABELHUB__Web__HostsPermitidos son obligatorios: sin ellos el arranque falla nombrando la variable.
builder.Services.AgregarHostWeb(OpcionesDelHostWeb.Leer(builder.Configuration, builder.Environment.IsProduction()));
// El token del usuario sale de la sesión de la solicitud y solo lo consume el manejador de salida (T14, regla 9).
builder.Services.AgregarTokenDelUsuarioDeLaSesion();
// Antiforgery global, Origin y Sec-Fetch-Site y captura del borrador (T16); las claves de Data Protection las compone quien conecte la base (T12).
builder.Services.AgregarProteccionContraFalsificacion();
// Filtros globales de acceso: área, paso pendiente del ingreso y sesión requerida (T18).
builder.Services.AgregarFiltroDeAccesoDeLaInterfaz();
// Cabeceras de seguridad y política de contenido por ruta (T17); los orígenes de la pasarela y del almacén son configuración (B05).
builder.Services.AgregarCabecerasDeSeguridad(OpcionesDeContenidoWeb.Leer(builder.Configuration));
// La versión provisional de los textos legales nunca se compone en producción (hasta el contrato de Identidad, IDE-57).
builder.Services.AgregarModuloWeb(permitirVersionesProvisionales: !builder.Environment.IsProduction());
// Implementaciones provisionales de lo que aún no existe; un registro real anterior se respeta (TryAdd).
builder.Services.AgregarCatalogoPendiente();
builder.Services.AgregarBibliotecaPendiente();
builder.Services.AddControllersWithViews();
// Detrás del borde la aplicación recibe HTTP: solo se toma el esquema (X-Forwarded-Proto) para que las cookies __Host- y el antiforgery vean HTTPS. El Api solo es alcanzable desde la red del borde (SEG-17); la lista de proxies conocidos queda para la composición.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();
ArranqueSeguro.ExigirCoherencia(modo, builder.Environment.IsProduction(), app.Services.GetServices<IAdaptadorSaliente>());

if (modo == ModoDeArranque.Restauracion)
{
    // Antes de abrir: solo los pasos de restauración, en orden, sin trabajos de fondo ni adaptadores hacia proveedores. El servicio no
    // escucha: termina con 0 si puede abrirse y con 1 si no, y restaurar.sh lo arranca después en modo normal.
    var resultado = await app.Services.GetRequiredService<EjecutorDePasosDeRestauracion>().Ejecutar();
    foreach (var paso in resultado.PasosEjecutados)
    {
        Console.WriteLine($"paso: {paso}");
    }

    Console.WriteLine(resultado.PermiteAbrir ? "restauracion: puede abrirse" : $"restauracion: no se abre, falló el paso {resultado.PasoFallido}");
    return resultado.PermiteAbrir ? 0 : 1;
}

app.UseForwardedHeaders();
app.UsarCabecerasDeSeguridad();
app.UsarManejoDeErrores();
app.UseStaticFiles();
// Salud para Docker Compose y el reinicio automático (RNF03): sin datos internos, el detalle solo en la red privada.
app.MapearSalud();
app.MapDefaultControllerRoute();
// Una consulta automática (hx-trigger="every …") admite solo GET: si no, el arranque falla (T15).
ConsultasAutomaticas.Validar(((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints));
// La política de textos pendientes (T37): todo endpoint atribuido declara métodos de la clase de su capacidad y SinConsultaDeEstado solo en su lista cerrada.
PoliticaDeTextosPendientes.Validar(((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints));
// El contenido de terceros solo se abre en las rutas exactas que le corresponden (T17).
RutasConContenidoDeTerceros.Validar(((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints));

app.Run();
return 0;
