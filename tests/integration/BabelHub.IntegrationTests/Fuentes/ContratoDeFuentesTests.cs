using System.Net;
using System.Text.Json;
using BabelHub.Inventario.Api.Fuentes;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Inventario;
using Microsoft.AspNetCore.Routing;

namespace BabelHub.IntegrationTests.Fuentes;

/// <summary>
/// T24: el Inventario cumple los contratos v1 de ECO-0 que consume el E-commerce. Un E-commerce simulado llama a disponibilidad, sucursales y contacto de notificación por HTTP con la credencial
/// de cada alcance y deserializa las respuestas con los DTOs de <c>BabelHub.Shared</c> (si el contrato cambia, esto no compila o falla); y cada operación de <c>Fuentes</c> está publicada con el método, la ruta y el
/// alcance que declara el catálogo.
/// </summary>
public sealed class ContratoDeFuentesTests(PostgresDeFuentesFixture bd) : IClassFixture<PostgresDeFuentesFixture>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private ApiDeFuentesHost _api = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _api = await ApiDeFuentesHost.Crear(bd);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    [Trait("Criterio", "AC-FUE-03")]
    public async Task ElEcommerce_ConsultaLaDisponibilidad_YLaRespuestaCumpleElDto()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 8, 0);
        await bd.PonerStock(cusco, libro, 3, 0);

        var respuesta = await _api.Enviar(HttpMethod.Get, $"{RutasDeServicio.Inventario}/disponibilidad/{libro}", ApiDeFuentesHost.CredencialDeDisponibilidad);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var dto = JsonSerializer.Deserialize<DisponibilidadDeEdicionDto>(await respuesta.Content.ReadAsStringAsync(), Web)!;
        Assert.Equal((libro, 11), (dto.EdicionId, dto.VendibleEnLaRed));
        Assert.Equal([("cusco", 3, "normal"), ("lima", 8, "normal")], dto.Fuentes.OrderBy(f => f.CodigoDeFuente).Select(f => (f.CodigoDeFuente, f.Vendible, f.EstadoDeStock)));
        Assert.All(dto.Fuentes, f => Assert.Contains(f.EstadoDeStock, EstadosDeStock.Todos));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-05")]
    public async Task ElEcommerce_ConsultaLasSucursales_YElContactoDeNotificacion_ConSusDtos()
    {
        await bd.AgregarFuente("lima", region: "lima", recepcion: true);
        await bd.AgregarFuente("cusco", region: "cusco");
        await bd.Ejecutar("UPDATE fuentes.fuentes SET contacto_aviso = '+51 966 123 456' WHERE codigo = 'cusco'");

        var sucursales = await _api.Enviar(HttpMethod.Get, $"{RutasDeServicio.Inventario}/sucursales", ApiDeFuentesHost.CredencialDeDisponibilidad);
        var contacto = await _api.Enviar(HttpMethod.Get, $"{RutasDeServicio.Inventario}/sucursales/cusco/contacto-de-notificacion", ApiDeFuentesHost.CredencialDeContacto);
        var inexistente = await _api.Enviar(HttpMethod.Get, $"{RutasDeServicio.Inventario}/sucursales/nunca/contacto-de-notificacion", ApiDeFuentesHost.CredencialDeContacto);

        var lista = JsonSerializer.Deserialize<SucursalesDto>(await sucursales.Content.ReadAsStringAsync(), Web)!;
        var numero = JsonSerializer.Deserialize<ContactoDeSucursalDto>(await contacto.Content.ReadAsStringAsync(), Web)!;
        Assert.Equal([("cusco", "Cusco", "cusco"), ("lima", "Lima", "lima")], lista.Sucursales.Select(s => (s.Codigo, s.Nombre, s.Region)));
        Assert.DoesNotContain("966", await sucursales.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(("cusco", "+51 966 123 456"), (numero.Codigo, numero.Numero));
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-12")]
    public async Task ElError404YElProblema_SiguenElContratoUniforme()
    {
        var respuesta = await _api.Enviar(HttpMethod.Get, $"{RutasDeServicio.Inventario}/disponibilidad/{Guid.NewGuid()}", ApiDeFuentesHost.CredencialDeDisponibilidad);

        var problema = JsonSerializer.Deserialize<BabelHub.Shared.Errores.ProblemaDto>(await respuesta.Content.ReadAsStringAsync(), Web)!;
        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal(CodigosDeErrorDelInventario.NoEncontrado, problema.Code);
        Assert.Equal(404, problema.Status);
        Assert.False(string.IsNullOrEmpty(problema.TraceId));
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-03")]
    public void CadaOperacionDeFuentes_EstaPublicadaConElMetodoLaRutaYElAlcanceDelCatalogo()
    {
        var publicadas = _api.Endpoints
            .Select(e => (
                Ruta: System.Text.RegularExpressions.Regex.Replace(e.RoutePattern.RawText ?? string.Empty, @"\{(\w+):[^}]+\}", "{$1}"),
                Metodo: e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Single(),
                Operacion: e.Metadata.GetMetadata<BabelHub.Inventario.Api.Plataforma.OperacionDeServicioAttribute>()?.Operacion.ToString()))
            .Where(p => p.Operacion is not null && p.Operacion.StartsWith("inventario.", StringComparison.Ordinal))
            .ToList();

        foreach (var nombre in MatrizDeFuentes.Operaciones)
        {
            var contrato = OperacionesDelInventario.Todas.Single(o => o.Nombre == nombre);
            var publicada = Assert.Single(publicadas, p => p.Operacion == nombre);
            Assert.Equal(contrato.Ruta, publicada.Ruta);
            Assert.Equal(contrato.Metodo, publicada.Metodo);
        }

        Assert.Equal(MatrizDeFuentes.Operaciones.Order(), publicadas.Select(p => p.Operacion!).Order());
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-03")]
    public void LaMatrizDeAlcances_DeclaraUnSoloAlcancePorOperacion_ComoElCatalogo()
    {
        var declaraciones = MatrizDeFuentes.Declaraciones().ToList();

        Assert.Equal(MatrizDeFuentes.Operaciones.Count, declaraciones.Select(d => d.Operacion.ToString()).Distinct().Count());
        Assert.Contains(declaraciones, d => d.Operacion.ToString() == "inventario.sucursales.contacto" && d.Alcance == BabelHub.Inventario.Application.Plataforma.AlcanceDeServicio.LecturaDeContacto);
        Assert.Contains(declaraciones, d => d.Operacion.ToString() == "inventario.disponibilidad.consultar" && d.Alcance == BabelHub.Inventario.Application.Plataforma.AlcanceDeServicio.LecturaDeDisponibilidad);
        Assert.All(
            declaraciones.Where(d => OperacionesDelInventario.Todas.Single(o => o.Nombre == d.Operacion.ToString()).RequiereTokenDeUsuario),
            d => Assert.Equal(BabelHub.Inventario.Application.Plataforma.AlcanceDeServicio.OperacionDeUsuario, d.Alcance));
    }
}
