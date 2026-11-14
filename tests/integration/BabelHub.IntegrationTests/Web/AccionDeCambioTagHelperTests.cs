using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Layout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Routing;

namespace BabelHub.IntegrationTests.Web;

/// <summary>El tag helper de las acciones de cambio y de pago no renderiza nada con los textos legales pendientes (WEB-42, T25).</summary>
public sealed class AccionDeCambioTagHelperTests
{
    private sealed class VistaNula : IView
    {
        public string Path => "nula";

        public Task RenderAsync(ViewContext context) => Task.CompletedTask;
    }

    private static (AccionDeCambioTagHelper Ayudante, TagHelperContext Contexto, TagHelperOutput Salida) Preparar(object? modelo)
    {
        var datos = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = modelo };
        var contextoDeVista = new ViewContext(
            new ActionContext(new Microsoft.AspNetCore.Http.DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            new VistaNula(),
            datos,
            new TempDataDictionary(new Microsoft.AspNetCore.Http.DefaultHttpContext(), new FakeTempDataProvider()),
            TextWriter.Null,
            new HtmlHelperOptions());
        var ayudante = new AccionDeCambioTagHelper { ViewContext = contextoDeVista };
        var contexto = new TagHelperContext("bh-accion-de-cambio", new TagHelperAttributeList(), new Dictionary<object, object>(), "id");
        var salida = new TagHelperOutput(
            "bh-accion-de-cambio",
            new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent().SetContent("Pagar ahora")));
        salida.Content.SetContent("Pagar ahora");
        return (ayudante, contexto, salida);
    }

    private sealed class FakeTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(Microsoft.AspNetCore.Http.HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(Microsoft.AspNetCore.Http.HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-36")]
    public void Con_textos_pendientes_no_renderiza_la_accion()
    {
        var (ayudante, contexto, salida) = Preparar(new ModeloBaseDePagina { TextosPendientes = true });

        ayudante.Process(contexto, salida);

        Assert.Null(salida.TagName);
        Assert.Equal(string.Empty, salida.Content.GetContent());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-36")]
    public void Sin_textos_pendientes_renderiza_la_accion_sin_envoltorio()
    {
        var (ayudante, contexto, salida) = Preparar(new ModeloBaseDePagina { TextosPendientes = false });

        ayudante.Process(contexto, salida);

        Assert.Null(salida.TagName);
        Assert.Equal("Pagar ahora", salida.Content.GetContent());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-36")]
    public void Si_el_modelo_no_es_una_pagina_base_falla_cerrado_y_no_renderiza_la_accion()
    {
        var (ayudante, contexto, salida) = Preparar(new object());

        ayudante.Process(contexto, salida);

        Assert.Equal(string.Empty, salida.Content.GetContent());
    }
}
