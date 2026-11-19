using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Reclamaciones;

/// <summary>Las hojas de quien las escribió: solo con una sesión de correo verificado igual al de la hoja y sin las de una cuenta cancelada (T08; REC-10, REC-14, REC-20).</summary>
public sealed class MisHojasTests
{
    private static readonly Guid Usuario = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string CorreoDeAna = "ana.perez@correo.com";

    private static async Task<(EntornoDeConsumidor E, Guid Hoja)> ConUnaHoja(string? correoVerificado = CorreoDeAna)
    {
        var e = EntornoDeConsumidor.Nuevo(Usuario, correoVerificado);
        var id = await e.RegistrarUna();
        return (e, id);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-12")]
    public async Task Un_consumidor_con_correo_verificado_ve_solo_sus_dos_hojas_con_estado_y_respuesta()
    {
        var (e, _) = await ConUnaHoja();
        await e.RegistrarUna();
        await e.RegistrarDeOtroCorreo("otra.persona@correo.com");

        var resultado = await e.Consumidor.Listar(new ListarMisHojasEntrada());

        Assert.True(resultado.EsExito);
        Assert.Equal(2, resultado.Valor.Total);
        Assert.All(resultado.Valor.Hojas, h => Assert.Equal(EstadoDeLaHoja.Pendiente, h.Estado));
        Assert.Equal(["2026-000002", "2026-000001"], resultado.Valor.Hojas.Select(h => h.Numero).ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-REC-12")]
    public async Task La_comparacion_es_por_el_correo_normalizado_sin_importar_mayusculas_ni_espacios()
    {
        var (e, _) = await ConUnaHoja("ANA.PEREZ@Correo.com");

        var resultado = await e.Consumidor.Listar(new ListarMisHojasEntrada());

        Assert.Equal(1, resultado.Valor.Total);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-12")]
    public async Task Ve_tambien_la_respuesta_de_una_hoja_respondida_y_las_descartadas()
    {
        var (e, id) = await ConUnaHoja();
        var segunda = await e.RegistrarUna();
        await e.Administrar.Responder(new ResponderHojaEntrada(id, "Le reponemos el libro."));
        await e.Administrar.Descartar(segunda);

        var resultado = await e.Consumidor.Listar(new ListarMisHojasEntrada());

        Assert.Equal(2, resultado.Valor.Total);
        var respondida = resultado.Valor.Hojas.Single(h => h.Estado == EstadoDeLaHoja.Respondido);
        Assert.Equal("Le reponemos el libro.", respondida.Respuesta);
        Assert.NotNull(respondida.RespondidaEn);
        Assert.Contains(resultado.Valor.Hojas, h => h.Estado == EstadoDeLaHoja.Descartado);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-12")]
    public async Task Una_sesion_sin_correo_verificado_recibe_correo_no_verificado_en_la_lista_y_sin_datos()
    {
        var (e, _) = await ConUnaHoja(correoVerificado: null);

        var resultado = await e.Consumidor.Listar(new ListarMisHojasEntrada());

        Assert.Equal(ClavesDeReclamaciones.CorreoNoVerificado, resultado.Error!.Clave);
        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error.Categoria);
        Assert.Single(e.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-18")]
    public async Task Las_hojas_de_una_cuenta_cancelada_no_salen_en_la_lista_de_otra_persona_con_el_mismo_correo()
    {
        var (e, _) = await ConUnaHoja();
        var hoja = e.Hojas.Todas.Single();
        hoja.MarcarDeCuentaCancelada();
        e.Hojas.Sembrar(hoja);

        var resultado = await e.Consumidor.Listar(new ListarMisHojasEntrada());

        Assert.Equal(0, resultado.Valor.Total);
        Assert.Empty(resultado.Valor.Hojas);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-12")]
    public async Task Obtener_una_hoja_propia_por_su_numero_devuelve_su_estado_y_respuesta()
    {
        var (e, id) = await ConUnaHoja();
        await e.Administrar.Responder(new ResponderHojaEntrada(id, "Atendido."));

        var resultado = await e.Consumidor.Obtener("2026-000001");

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDeLaHoja.Respondido, resultado.Valor.Estado);
        Assert.Equal("Atendido.", resultado.Valor.Respuesta);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-08")]
    [InlineData("ajena")]
    [InlineData("inexistente")]
    [InlineData("cancelada")]
    [InlineData("formato")]
    [InlineData("sin-correo")]
    public async Task Una_hoja_ajena_inexistente_de_cuenta_cancelada_o_mal_escrita_da_el_mismo_no_encontrado_y_un_acceso_denegado_por_intento(string caso)
    {
        var (e, _) = await ConUnaHoja(caso == "sin-correo" ? null : CorreoDeAna);
        await e.RegistrarDeOtroCorreo("otra.persona@correo.com");
        var numero = caso switch
        {
            "ajena" => "2026-000002",
            "inexistente" => "2026-000099",
            "formato" => "no es un número",
            _ => "2026-000001",
        };
        if (caso == "cancelada")
        {
            var hoja = e.Hojas.Todas.Single(h => h.Numero.ToString() == "2026-000001");
            hoja.MarcarDeCuentaCancelada();
            e.Hojas.Sembrar(hoja);
        }

        var primero = await e.Consumidor.Obtener(numero);
        var segundo = await e.Consumidor.Obtener(numero);

        Assert.Equal(CategoriaDeError.NoEncontrado, primero.Error!.Categoria);
        Assert.Equal(primero.Error.Clave, segundo.Error!.Clave);
        Assert.Equal(2, e.Denegaciones.Eventos.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-08")]
    public async Task La_respuesta_a_una_hoja_ajena_y_a_una_inexistente_es_identica()
    {
        var (e, _) = await ConUnaHoja();
        await e.RegistrarDeOtroCorreo("otra.persona@correo.com");

        var ajena = await e.Consumidor.Obtener("2026-000002");
        var inexistente = await e.Consumidor.Obtener("2026-000050");

        Assert.Equal(ajena.Error, inexistente.Error);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-12")]
    public async Task Si_Identidad_no_responde_no_se_muestra_nada_y_es_servicio_no_disponible()
    {
        var (e, _) = await ConUnaHoja();
        e.Correos.Falla = true;

        var lista = await e.Consumidor.Listar(new ListarMisHojasEntrada());
        var una = await e.Consumidor.Obtener("2026-000001");

        Assert.Equal(CategoriaDeError.NoDisponible, lista.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, una.Error!.Categoria);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-12")]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public async Task La_paginacion_invalida_se_rechaza_sin_consultar_las_hojas(int pagina, int tamano)
    {
        var (e, _) = await ConUnaHoja();
        var antes = e.Hojas.Operaciones;

        var resultado = await e.Consumidor.Listar(new ListarMisHojasEntrada(pagina, tamano));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal(antes, e.Hojas.Operaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-12")]
    public async Task La_lista_pagina_y_no_incluye_documento_ni_domicilio()
    {
        var (e, _) = await ConUnaHoja();
        await e.RegistrarUna();
        await e.RegistrarUna();

        var pagina = await e.Consumidor.Listar(new ListarMisHojasEntrada(2, 2));

        Assert.Equal(3, pagina.Valor.Total);
        Assert.Single(pagina.Valor.Hojas);
        Assert.DoesNotContain("12345678", pagina.Valor.ToString() + pagina.Valor.Hojas.Single());
    }
}
