using System.Reflection;
using System.Text.Json;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Domain.Auditoria;
using BabelHub.Shared.Contratos;
using BabelHub.UnitTests.Compartido;
using Contrato = BabelHub.Shared.Contratos.Auditoria;
using TipoDeActorDeAplicacion = BabelHub.Inventario.Application.Plataforma.TipoDeActor;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>
/// La prueba de contrato de <c>Shared</c> (T21; AC-AUD-15, AC-AUD-23, AUD-17, AUD-26): las constantes y los DTOs que viajan entre los servicios y hacia la interfaz web coinciden con el catálogo del
/// Dominio, con el contrato de la Aplicación y con los enumerados del módulo. Una acción o un motivo nuevo sin su constante, o una constante sin su acción, hace fallar esta prueba (y su gemela del otro
/// servicio). El corpus común de datos prohibidos se prueba contra la lista blanca de cada servicio.
/// </summary>
public sealed class ContratoCompartidoDeAuditoriaTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static IEnumerable<string> ConstantesDeTexto(Type tipo) =>
        tipo.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    // ---------- Acciones ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-23")]
    public void LasConstantesDeAcciones_SonLasDelCatalogo_YDeLaRaizDeLaAplicacion()
    {
        var catalogo = CatalogoDeAccionesDeAuditoria.Crear().Acciones.Select(a => a.Identificador).Order(StringComparer.Ordinal).ToList();
        var constantes = ConstantesDeTexto(typeof(Contrato.AccionesDeAuditoria)).Order(StringComparer.Ordinal).ToList();
        var lista = Contrato.AccionesDeAuditoria.Todas.Order(StringComparer.Ordinal).ToList();
        var raiz = typeof(AccionesAuditables).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(AccionAuditable))
            .Select(f => ((AccionAuditable)f.GetValue(null)!).Identificador)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(catalogo, constantes);
        Assert.Equal(catalogo, lista);
        Assert.Equal(catalogo, raiz);
        Assert.Equal(lista.Count, lista.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-23")]
    public void CadaConstanteDeAccion_TieneElNombreDeSuValor()
    {
        foreach (var campo in typeof(Contrato.AccionesDeAuditoria).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral))
        {
            Assert.Equal(campo.Name, (string)campo.GetRawConstantValue()!);
        }
    }

    // ---------- Motivos de alerta ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-26")]
    public void LosMotivosDeAlerta_SonLosDelCatalogoCerrado_YCadaUnoEstaEnElContrato()
    {
        var catalogo = CatalogoDeAlertasDeAuditoria.Crear();
        var internos = ConstantesDeTexto(typeof(MotivosDeAlerta)).Order(StringComparer.Ordinal).ToList();
        var contrato = Contrato.MotivosDeAlertaDeAuditoria.Todos.Order(StringComparer.Ordinal).ToList();

        Assert.Equal(internos, contrato);
        Assert.All(contrato, motivo => Assert.True(catalogo.Contiene(motivo), $"El motivo {motivo} no está en el catálogo."));
        Assert.Equal(ConstantesDeTexto(typeof(Contrato.MotivosDeAlertaDeAuditoria)).Order(StringComparer.Ordinal), contrato);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-26")]
    public void LaClaseYElEstadoDeLasAlertas_SonLosDelContrato()
    {
        Assert.Equal(Enum.GetNames<ClaseDeAlerta>().Order(), Contrato.ClasesDeAlerta.Todas.Order());
        Assert.Equal(Enum.GetNames<ClaseDeAlertaConsultada>().Order(), Contrato.ClasesDeAlerta.Todas.Order());
        Assert.Equal(Enum.GetNames<EstadoDeAlertaConsultada>().Order(), Contrato.EstadosDeAlerta.Todos.Order());
    }

    // ---------- Valores cerrados ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-15")]
    public void LosValoresCerradosDelContrato_SonLosNombresDeLosEnumerados()
    {
        Assert.Equal(Enum.GetNames<ServicioAuditado>().Order(), Contrato.ServiciosAuditados.Todos.Order());
        Assert.Equal(Enum.GetNames<TipoDeRegistroAuditado>().Order(), Contrato.TiposDeRegistro.Todos.Order());
        Assert.Equal(Enum.GetNames<TipoDeActorDeAplicacion>().Order(), Contrato.TiposDeActorAuditado.Todos.Order());
        Assert.Equal(Enum.GetNames<ResultadoAuditado>().Order(), Contrato.ResultadosAuditados.Todos.Order());
        Assert.Equal("eco:", Contrato.PrefijosDeAlerta.Ecommerce);
        Assert.Equal("inv:", Contrato.PrefijosDeAlerta.Inventario);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-15")]
    public void LosTopesDelContrato_SonLosDeLaAplicacion()
    {
        Assert.Equal(PaginaDeRegistros.TamanoDePagina, Contrato.ParametrosDeAuditoria.TamanoDePagina);
        Assert.Equal(RegistroAuditado.MaximoDeCaracteresDelDetalle, Contrato.ParametrosDeAuditoria.MaximoDeCaracteresDelDetalle);
        Assert.Equal(DetalleSeguro.MaximoDeCaracteres, Contrato.ParametrosDeAuditoria.MaximoDeCaracteresDelDetalle);
        Assert.Equal(ValidacionDeFiltroDeAuditoria.MinimoDeCaracteresDelTexto, Contrato.ParametrosDeAuditoria.MinimoDeCaracteresDelTexto);
    }

    // ---------- Alcances ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-30")]
    public void LosAlcancesDeAuditoria_EstanEnLaListaDeCredenciales_YCadaRutaDelInventarioUsaElSuyo()
    {
        string[] esperados = ["consulta-de-auditoria", "marcar-alerta", "historial", "lectura-alertas"];
        Assert.All(esperados, alcance => Assert.Contains(alcance, AlcancesDeCredencial.Todos));
        Assert.Equal("consulta-de-auditoria", AlcancesDeCredencial.ConsultaDeAuditoria);
        Assert.Equal("marcar-alerta", AlcancesDeCredencial.MarcarAlerta);
        Assert.Equal("historial", AlcancesDeCredencial.Historial);
        Assert.Equal("lectura-alertas", AlcancesDeCredencial.LecturaDeAlertas);

        var operaciones = CatalogoDeOperaciones.V1.ToDictionary(o => o.Nombre);
        Assert.Equal(AlcancesDeCredencial.ConsultaDeAuditoria, operaciones["inventario.auditoria.consultar"].Alcance);
        Assert.Equal(AlcancesDeCredencial.LecturaDeAlertas, operaciones["inventario.alertas.listar"].Alcance);
        Assert.Equal(AlcancesDeCredencial.MarcarAlerta, operaciones["inventario.alertas.marcar-revisada"].Alcance);
        Assert.Equal(AlcancesDeCredencial.Historial, operaciones["inventario.historial.consultar"].Alcance);
    }

    // ---------- DTOs ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-15")]
    public void ElRegistroDto_TieneUnaPropiedadPorCadaCampoDelRegistroAuditado_ConNombresEstables()
    {
        var propiedadesDelDto = typeof(Contrato.RegistroDto).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        // El contrato nombra al actor sin la palabra «actor» en el JSON (la regla de DTO lo prohíbe): «ActorRol» es «Rol», «ActorId» es «UsuarioId» y «ActorTipo» es «TipoDeActor»; «InstanteEnLima» es solo para mostrar y la interfaz lo calcula del UTC.
        var equivalencias = new Dictionary<string, string> { ["ActorRol"] = "Rol", ["ActorId"] = "UsuarioId", ["ActorTipo"] = "TipoDeActor" };
        foreach (var propiedad in typeof(RegistroAuditado).GetProperties().Where(p => p.Name != nameof(RegistroAuditado.InstanteEnLima)))
        {
            var nombre = equivalencias.GetValueOrDefault(propiedad.Name, propiedad.Name);
            Assert.True(propiedadesDelDto.Contains(nombre), $"El contrato no tiene el campo {nombre}.");
        }

        var registro = new Contrato.RegistroDto(
            Guid.Parse("11111111-1111-1111-1111-111111111111"), "Ecommerce", "Auditoria", "Usuario", null, "administrador-central", new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero),
            "PedidoAnulado", "Exitoso", "pedido", "P-1", null, null, "motivo-x", null, "/api/x", "{}", ["correo"], false, "abc12345", null);
        using var documento = JsonDocument.Parse(JsonSerializer.Serialize(registro, Json));
        Assert.Equal(
            ["id", "servicio", "tipo", "tipoDeActor", "usuarioId", "rol", "instanteUtc", "accion", "resultado", "objetoTipo", "objetoId", "sucursalOrigen", "sucursalDestino", "motivo", "origen", "ruta", "detalle", "clavesPersonales", "truncado", "correlacion", "anonimizadoEn"],
            documento.RootElement.EnumerateObject().Select(p => p.Name).ToList());
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-15")]
    public void LaPaginaYLaAlerta_ViajanConLosNombresDelContrato()
    {
        var pagina = new Contrato.PaginaDeRegistrosDto([], "cursor", "cursor-de-la-pagina", true);
        Assert.Equal("""{"registros":[],"siguienteCursor":"cursor","cursorDeLaPagina":"cursor-de-la-pagina","faltanDelInventario":true}""", JsonSerializer.Serialize(pagina, Json));

        var alerta = new Contrato.AlertaDto(
            "inv:abc", "Inventario", "accesos-denegados", "Seguridad", new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 14, 15, 5, 0, TimeSpan.Zero), 12, 10, null, null, "Pendiente");
        using var documento = JsonDocument.Parse(JsonSerializer.Serialize(alerta, Json));
        Assert.Equal(
            ["id", "servicio", "motivo", "clase", "primeraOcurrencia", "ultimaOcurrencia", "ocurrencias", "revisadasHasta", "revisadaPor", "revisadaEn", "estado"],
            documento.RootElement.EnumerateObject().Select(p => p.Name).ToList());

        Assert.Equal("""{"contadorVisto":7}""", JsonSerializer.Serialize(new Contrato.MarcarAlertaRevisadaDto(7), Json));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-15")]
    public void UnaPaginaSinFaltantesNiCursorDeLaPagina_SeLeeConLosValoresPorDefecto()
    {
        var pagina = JsonSerializer.Deserialize<Contrato.PaginaDeRegistrosDto>("""{"registros":[],"siguienteCursor":null}""", Json);

        Assert.NotNull(pagina);
        Assert.False(pagina.FaltanDelInventario);
        Assert.Null(pagina.CursorDeLaPagina);
    }

    // ---------- Corpus común de datos prohibidos ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-05")]
    public void ElCorpusComunDeDatosProhibidos_NoLlegaAlDetalle_ConLaListaBlancaDeCadaServicio()
    {
        IReadOnlyCollection<ClaveDeDetalle> claves = [new("motivoLibre", false), new("nombre", true)];
        foreach (var (clase, valor) in CorpusDeDatosProhibidos.Valores)
        {
            var detalle = DetalleSeguro.Construir(claves, new Dictionary<string, string?> { ["motivoLibre"] = valor });

            Assert.True(detalle.ClavesRedactadas.Contains("motivoLibre"), $"El valor de clase «{clase}» no se redactó.");
            foreach (var fragmento in CorpusDeDatosProhibidos.FragmentosQueNoDebenQuedar)
            {
                Assert.DoesNotContain(fragmento, detalle.Texto, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
