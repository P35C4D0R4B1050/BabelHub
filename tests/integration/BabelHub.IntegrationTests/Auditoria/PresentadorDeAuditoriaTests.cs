using System.Reflection;
using BabelHub.Ecommerce.Api.Web.Auditoria;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.IntegrationTests.Auditoria;

/// <summary>
/// El contrato que consumen las pantallas de auditoría y alertas (T29; AC-AUD-16, AC-AUD-22; AUD-18, AUD-25): la hora de Lima, los valores como texto sin codificar (la vista los codifica), el aviso del Inventario que no
/// respondió, los estados estándar y los textos de todos los motivos del catálogo cerrado.
/// </summary>
public sealed class PresentadorDeAuditoriaTests
{
    private static readonly DateTimeOffset Utc = new(2026, 11, 15, 2, 30, 5, TimeSpan.Zero);

    private static RegistroAuditado Registro(string? detalle = null, string? motivo = null, string? origen = null, TipoDeActor actor = TipoDeActor.Usuario, Guid? usuario = null, bool anonimizado = false) =>
        new(Guid.NewGuid(), ServicioAuditado.Inventario, TipoDeRegistroAuditado.Auditoria, actor, actor == TipoDeActor.Usuario ? usuario ?? Guid.Parse("a1b2c3d4-0000-4000-8000-000000000001") : null, "administrador-central", Utc, "AjusteRegistrado", ResultadoAuditado.Exitoso)
        {
            ObjetoTipo = "ajuste",
            ObjetoId = "AJ-0001",
            Detalle = detalle,
            Motivo = motivo,
            Origen = origen,
            AnonimizadoEn = anonimizado ? Utc : null,
        };

    [Fact]
    [Trait("Criterio", "AC-AUD-22")]
    public void LaHoraSeMuestraEnLima_UTCMenosCinco_SinHorarioDeVerano()
    {
        var fila = PresentadorDeAuditoria.ARegistro(Registro());

        Assert.Equal("14/11/2026 21:30:05", fila.HoraDeLima);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-22")]
    public void UnValorConMarcasHtml_LlegaComoTextoSinCodificar_ParaQueLaVistaLoCodifiqueUnaVez()
    {
        const string Xss = "<script>alert(1)</script>";
        var fila = PresentadorDeAuditoria.ARegistro(Registro(detalle: "{\"motivoLibre\":\"" + Xss + "\",\"cantidad\":5,\"ok\":true,\"anidado\":{\"x\":1}}", motivo: "codigo-x", origen: "v1:abc"));

        var motivoLibre = Assert.Single(fila.Detalle, d => d.Clave == "motivoLibre");
        Assert.Equal(Xss, motivoLibre.Valor);
        Assert.Equal("5", Assert.Single(fila.Detalle, d => d.Clave == "cantidad").Valor);
        Assert.Equal("true", Assert.Single(fila.Detalle, d => d.Clave == "ok").Valor);
        Assert.DoesNotContain(fila.Detalle, d => d.Clave == "anidado");
        Assert.DoesNotContain("&lt;", string.Concat(fila.Detalle.Select(d => d.Valor)), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-AUD-22")]
    [InlineData("no es json")]
    [InlineData("{\"a\":")]
    [InlineData("[1,2]")]
    [InlineData("")]
    [InlineData(null)]
    public void UnDetalleQueNoSePuedeLeer_SaleSinDatos_ConLaFilaIntacta(string? detalle)
    {
        var fila = PresentadorDeAuditoria.ARegistro(Registro(detalle));

        Assert.Empty(fila.Detalle);
        Assert.Equal("AjusteRegistrado", fila.Accion);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-22")]
    public void ElUsuarioSeMuestraSinElIdentificadorCompleto_YLosActoresDelServidorPorSuNombre()
    {
        Assert.Equal("Usuario a1b2c3d4", PresentadorDeAuditoria.ARegistro(Registro()).Usuario);
        Assert.Equal("Sistema", PresentadorDeAuditoria.ARegistro(Registro(actor: TipoDeActor.Sistema)).Usuario);
        Assert.Equal("Integración", PresentadorDeAuditoria.ARegistro(Registro(actor: TipoDeActor.Integracion)).Usuario);
        Assert.Equal("Anónimo", PresentadorDeAuditoria.ARegistro(Registro(actor: TipoDeActor.Anonimo)).Usuario);
        var anonimizado = PresentadorDeAuditoria.ARegistro(Registro(anonimizado: true));
        Assert.True(anonimizado.Anonimizado);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-16")]
    public void SiElInventarioNoRespondio_LaPaginaTraeElAviso_YSinElNoHayNinguno()
    {
        var conFaltantes = PresentadorDeAuditoria.Presentar(new PaginaDeRegistros([Registro()], null, faltanDelInventario: true));
        var completa = PresentadorDeAuditoria.Presentar(new PaginaDeRegistros([Registro()], "cursor"));

        Assert.Equal(PresentadorDeAuditoria.AvisoFaltanLosDelInventario, conFaltantes.Aviso);
        Assert.Equal(EstadoDePantalla.Exito, conFaltantes.Estado.Estado);
        Assert.Null(completa.Aviso);
        Assert.Equal("cursor", completa.SiguienteCursor);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-16")]
    public void ElAvisoDeLasAlertas_EsPropio_YUnaPaginaVaciaEsElEstadoVacio()
    {
        var vacia = PresentadorDeAuditoria.Presentar(new PaginaDeAlertas([], null, faltanDelInventario: true));
        var sinRegistros = PresentadorDeAuditoria.Presentar(new PaginaDeRegistros([], null));

        Assert.Equal(PresentadorDeAuditoria.AvisoFaltanLasAlertasDelInventario, vacia.Aviso);
        Assert.Equal(EstadoDePantalla.Vacio, vacia.Estado.Estado);
        Assert.Equal(EstadoDePantalla.Vacio, sinRegistros.Estado.Estado);
        Assert.Null(sinRegistros.Aviso);
    }

    [Theory]
    [Trait("Criterio", "AC-AUD-16")]
    [InlineData(CategoriaDeError.Prohibido, EstadoDePantalla.SinPermiso)]
    [InlineData(CategoriaDeError.NoAutenticado, EstadoDePantalla.SinPermiso)]
    [InlineData(CategoriaDeError.NoDisponible, EstadoDePantalla.ServicioNoDisponible)]
    [InlineData(CategoriaDeError.Validacion, EstadoDePantalla.Error)]
    [InlineData(CategoriaDeError.Inesperado, EstadoDePantalla.Error)]
    public void LosFallosDeLaAplicacion_SeMuestranConElEstadoEstandar_SinElMotivoTecnico(CategoriaDeError categoria, EstadoDePantalla esperado)
    {
        var estado = PresentadorDeAuditoria.EstadoDelFallo(new ErrorDeAplicacion(categoria, "cursor-invalido-con-detalle-interno"), "/admin/auditoria");

        Assert.Equal(esperado, estado.Estado);
        Assert.DoesNotContain("cursor-invalido", estado.Mensaje ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("cursor-invalido", estado.Titulo, StringComparison.Ordinal);
        if (esperado is EstadoDePantalla.ServicioNoDisponible or EstadoDePantalla.Error)
        {
            Assert.Equal("/admin/auditoria", estado.RutaDeReintento);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-16")]
    public void TodoMotivoDelCatalogoDeAlertas_TieneSuTextoEnEspanol()
    {
        var motivos = typeof(MotivosDeAlerta).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(m => m != MotivosDeAlerta.MotivoNoCatalogado);

        foreach (var motivo in motivos)
        {
            var alerta = new AlertaConsultada($"eco:{Guid.NewGuid():D}", ServicioAuditado.Ecommerce, new CodigoDeAlerta(motivo), ClaseDeAlertaConsultada.Seguridad, Utc, Utc, 1, 0);

            Assert.NotEqual("Motivo no catalogado", PresentadorDeAuditoria.AAlerta(alerta).Motivo);
        }

        var desconocida = new AlertaConsultada($"eco:{Guid.NewGuid():D}", ServicioAuditado.Ecommerce, new CodigoDeAlerta("un-motivo-nuevo"), ClaseDeAlertaConsultada.Seguridad, Utc, Utc, 1, 0);
        Assert.Equal("Motivo no catalogado", PresentadorDeAuditoria.AAlerta(desconocida).Motivo);
    }
}
