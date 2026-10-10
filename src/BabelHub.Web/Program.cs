var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
var app = builder.Build();

app.UseStaticFiles();
// Salud para Docker Compose y el reinicio automático (RNF03). La lógica llega con cada spec.
app.MapGet("/health/live", () => Results.Ok(new { estado = "vivo" }));
app.MapGet("/health/ready", () => Results.Ok(new { estado = "listo" }));
app.MapDefaultControllerRoute();

app.Run();
