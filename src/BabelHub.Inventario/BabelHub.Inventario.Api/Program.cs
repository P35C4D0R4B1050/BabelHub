using BabelHub.Inventario.Api.Plataforma;
using BabelHub.Inventario.Application.Plataforma;

// El modo migrar (COMP-2): aplica los esquemas y los roles de la base con la cuenta propietaria y termina; no arranca el servidor.
if (args.Length > 0 && args[0] == ModoMigrar.Argumento)
{
    return await ModoMigrar.Ejecutar(new ConfigurationBuilder().AddEnvironmentVariables(prefix: "BABELHUB__").Build(), Console.Out, Console.Error);
}

// La orden de operación cargar-stock (transversal-carga-inicial): carga las sucursales, las rutas y el stock inicial por los casos de uso del Inventario con el modo Operacion; no abre puertos ni arranca el servidor.
if (args.Length > 0 && args[0] == ModoCargarStock.Argumento)
{
    return await ModoCargarStock.Ejecutar(args, Console.Out, Console.Error);
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
builder.Services.AddSingleton<EjecutorDePasosDeRestauracion>();

// La composición real del servicio (COMP-1): plataforma, auditoría, Fuentes, Reservas, Ajustes, Traslados, la verificación de credenciales de usuario y los trabajos de fondo.
// Falta una variable obligatoria o es débil: el arranque falla nombrando la variable (nunca su valor).
builder.AgregarInventario(modo);

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

app.UsarInventario();
// Con los endpoints ya mapeados: ningún adaptador nulo donde existe el real y toda operación declarada en la matriz de alcances.
app.ExigirComposicionDeProduccion(builder.Environment.IsProduction());

app.Run();
return 0;
