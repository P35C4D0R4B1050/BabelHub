using BabelHub.Inventario.Api.Plataforma;
using BabelHub.Inventario.Application.Plataforma;

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
builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("INV"));
builder.Services.AddSingleton<EjecutorDePasosDeRestauracion>();

var app = builder.Build();
ArranqueSeguro.ExigirCoherencia(modo, builder.Environment.IsProduction(), app.Services.GetServices<IAdaptadorSaliente>());

if (modo == ModoDeArranque.Restauracion)
{
    // Antes de abrir: solo los pasos de restauración, en orden, sin trabajos de fondo. El servicio no escucha: termina con 0 si puede
    // abrirse y con 1 si no, y restaurar.sh lo arranca después en modo normal.
    var resultado = await app.Services.GetRequiredService<EjecutorDePasosDeRestauracion>().Ejecutar();
    foreach (var paso in resultado.PasosEjecutados)
    {
        Console.WriteLine($"paso: {paso}");
    }

    Console.WriteLine(resultado.PermiteAbrir ? "restauracion: puede abrirse" : $"restauracion: no se abre, falló el paso {resultado.PasoFallido}");
    return resultado.PermiteAbrir ? 0 : 1;
}

app.UsarManejoDeErrores();
// Salud para Docker Compose y el reinicio automático (RNF03): sin datos internos, el detalle solo en la red privada.
app.MapearSalud();

app.Run();
return 0;
