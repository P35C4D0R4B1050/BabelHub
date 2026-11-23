using System.Security.Cryptography;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Domain.Devoluciones;
using BabelHub.Ecommerce.Infrastructure.Web;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// El contenido de los tres textos legales que exige <c>web-informacion-del-comercio</c>: el comercio, el contacto, la ley peruana, «Cambios y devoluciones» coherente con
/// Devoluciones y la versión que acompaña a cada cuerpo (COM-09 a COM-15).
/// </summary>
public sealed class ContenidoLegalDeLaTiendaTests
{
    private static async Task<string> Texto(TipoDeTextoLegal tipo)
    {
        var cuerpo = await new ContenidoDeTextosEmbebido().Obtener(tipo);
        Assert.NotNull(cuerpo);
        return string.Join('\n', [cuerpo.Titulo, cuerpo.Resumen, .. cuerpo.Secciones.SelectMany(s => new[] { s.Titulo }.Concat(s.Parrafos))]);
    }

    [Fact]
    [Trait("Criterio", "AC-COM-08")]
    [Trait("Criterio", "AC-COM-10")]
    public async Task Cambios_y_devoluciones_informa_los_plazos_de_Devoluciones_y_los_cinco_estados()
    {
        var texto = await Texto(TipoDeTextoLegal.Reembolso);

        // Los plazos se leen de las reglas de Devoluciones: si cambian, este texto debe cambiar con una versión nueva (COM-15).
        foreach (var plazo in new[] { PoliticaDeDevolucion.PlazoParaSolicitarUnImpreso, PoliticaDeDevolucion.PlazoParaRecibirUnImpreso, PoliticaDeDevolucion.PlazoDelReembolsoAutomaticoDeUnDigital, PoliticaDeDevolucion.PlazoParaReportarUnArchivo })
        {
            Assert.Contains($"{(int)plazo.TotalDays} días calendario", texto, StringComparison.Ordinal);
        }

        foreach (var estado in new[] { "solicitada", "aprobada", "rechazada", "recibida", "reembolsada" })
        {
            Assert.Contains(estado, texto, StringComparison.Ordinal);
        }

        Assert.Contains("sin el costo de envío", texto, StringComparison.Ordinal);
        Assert.Contains("rechaza sin reembolso", texto, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-COM-09")]
    [Trait("Criterio", "AC-COM-11")]
    public async Task Cambios_y_devoluciones_distingue_impresos_de_digitales_y_no_ofrece_cambios_directos()
    {
        var texto = await Texto(TipoDeTextoLegal.Reembolso);

        Assert.Contains("Libros impresos", texto, StringComparison.Ordinal);
        Assert.Contains("Libros digitales", texto, StringComparison.Ordinal);
        Assert.Contains("tras la recepción", texto, StringComparison.Ordinal);
        Assert.Contains("no hace cambios directos de un libro por otro", texto, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TipoDeTextoLegal.Terminos)]
    [InlineData(TipoDeTextoLegal.Privacidad)]
    [Trait("Criterio", "AC-COM-12")]
    public async Task Los_terminos_y_la_privacidad_nombran_al_comercio_remiten_al_contacto_y_citan_la_ley_peruana(TipoDeTextoLegal tipo)
    {
        var texto = await Texto(tipo);

        foreach (var esperado in new[]
        {
            "BabelHub",
            "datos de contacto",
            "Ley N.° 29733",
            "Ley N.° 29571",
            "Código de Protección y Defensa del Consumidor",
            "leyes del Perú",
            "Indecopi",
            "desde tu cuenta",
            "consultas generales",
        })
        {
            Assert.Contains(esperado, texto, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(TipoDeTextoLegal.Terminos)]
    [InlineData(TipoDeTextoLegal.Privacidad)]
    [InlineData(TipoDeTextoLegal.Reembolso)]
    [Trait("Criterio", "AC-COM-12")]
    public async Task Ningun_texto_legal_trae_un_telefono_ni_un_correo_literal(TipoDeTextoLegal tipo)
    {
        var texto = await Texto(tipo);

        Assert.DoesNotMatch(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", texto);
        Assert.DoesNotMatch(@"(?<![0-9A-Za-z])(\+?51[ -]?)?9[0-9]{2}[ -]?[0-9]{3}[ -]?[0-9]{3}(?![0-9A-Za-z])", texto);
    }

    [Theory]
    [InlineData(TipoDeTextoLegal.Terminos, "terminos")]
    [InlineData(TipoDeTextoLegal.Privacidad, "privacidad")]
    [InlineData(TipoDeTextoLegal.Reembolso, "reembolso")]
    [Trait("Criterio", "AC-COM-13")]
    public void La_version_de_referencia_de_cada_texto_lleva_la_huella_de_su_cuerpo_y_cambiar_el_cuerpo_obliga_a_cambiar_la_version(TipoDeTextoLegal tipo, string clave)
    {
        var referencia = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(RaizDelRepositorio.Raiz, "tests", "integration", "BabelHub.IntegrationTests", "Web", "versiones-de-referencia-de-textos.json")))!;
        var ensamblado = typeof(ContenidoDeTextosEmbebido).Assembly;
        var recurso = ensamblado.GetManifestResourceNames().Single(r => r.EndsWith($".Textos.{TiposDeTextoLegal.Clave(tipo)}.txt", StringComparison.Ordinal));
        using var flujo = ensamblado.GetManifestResourceStream(recurso)!;
        var huella = Convert.ToHexString(SHA256.HashData(flujo)).ToLowerInvariant()[..8];

        Assert.True(referencia.TryGetValue(clave, out var version), $"Falta la versión de referencia de «{clave}».");
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}-[0-9a-f]{8}\z", version);
        Assert.True(
            version!.EndsWith("-" + huella, StringComparison.Ordinal),
            $"El cuerpo del texto «{clave}» cambió (huella {huella}): fije una nueva versión vigente de ese texto en versiones-de-referencia-de-textos.json (AAAA-MM-DD-{huella}) y en identidad.versiones_de_textos antes de publicarlo (COM-15).");
    }
}
