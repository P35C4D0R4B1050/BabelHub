using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using BabelHub.Ecommerce.Domain.Reclamaciones;

namespace BabelHub.Ecommerce.UnitTests.Reclamaciones;

public sealed class ContarHojasPendientesDeUnCorreoTests
{
    private static readonly Guid Ana = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Beto = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid Carla = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static ContarHojasPendientesDeUnCorreo Contador(EntornoDeConsumidor e) => new(e.Correos, e.Hojas);

    [Fact]
    [Trait("Criterio", "AC-REC-17")]
    public async Task Cuenta_solo_las_pendientes_de_cada_consumidor_y_no_las_respondidas_ni_las_descartadas()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, "ana@correo.com");
        e.Correos.Fijar(Beto, "beto@correo.com");
        e.Correos.Fijar(Carla, "carla@correo.com");
        await e.RegistrarDeOtroCorreo("ana@correo.com");
        var respondida = await e.RegistrarDeOtroCorreo("beto@correo.com");
        var descartada = await e.RegistrarDeOtroCorreo("carla@correo.com");
        await e.Administrar.Responder(new ResponderHojaEntrada(respondida, "Atendido."));
        await e.Administrar.Descartar(descartada);
        var contador = Contador(e);

        Assert.Equal(1, await contador.Contar(Ana));
        Assert.Equal(0, await contador.Contar(Beto));
        Assert.Equal(0, await contador.Contar(Carla));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-17")]
    public async Task Cuenta_cada_hoja_pendiente_del_correo_incluso_con_otras_mayusculas()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, "ANA@Correo.com");
        await e.RegistrarDeOtroCorreo("ana@correo.com");
        await e.RegistrarDeOtroCorreo(" Ana@CORREO.com ");

        Assert.Equal(2, await Contador(e).Contar(Ana));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-17")]
    public async Task Una_hoja_de_una_cuenta_cancelada_no_cuenta_como_pendiente()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, "ana@correo.com");
        await e.RegistrarDeOtroCorreo("ana@correo.com");
        var hoja = e.Hojas.Todas.Single();
        hoja.MarcarDeCuentaCancelada();
        e.Hojas.Sembrar(hoja);

        Assert.Equal(0, await Contador(e).Contar(Ana));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-17")]
    public async Task Una_cuenta_sin_correo_verificado_cuenta_cero_sin_consultar_las_hojas()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, null);
        await e.RegistrarDeOtroCorreo("ana@correo.com");
        var antes = e.Hojas.Operaciones;

        Assert.Equal(0, await Contador(e).Contar(Ana));
        Assert.Equal(antes, e.Hojas.Operaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-17")]
    public async Task Si_no_se_puede_contar_lanza_y_nunca_se_asume_cero()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, "ana@correo.com");
        e.Correos.Falla = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Contador(e).Contar(Ana));
        await Assert.ThrowsAsync<ArgumentException>(() => Contador(e).Contar(Guid.Empty));
    }
}

public sealed class MarcarHojasDeUnaCuentaCanceladaTests
{
    private static readonly Guid Ana = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Otra = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private static MarcarHojasDeUnaCuentaCancelada Aplicador(EntornoDeConsumidor e) => new(e.Correos, e.Hojas);

    [Fact]
    [Trait("Criterio", "AC-REC-18")]
    public async Task La_cancelacion_marca_todas_las_hojas_del_correo_sin_alterar_ningun_otro_dato()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, "ana@correo.com");
        await e.RegistrarDeOtroCorreo("ana@correo.com");
        var respondida = await e.RegistrarDeOtroCorreo("ana@correo.com");
        await e.Administrar.Responder(new ResponderHojaEntrada(respondida, "Atendido."));
        var antes = e.Hojas.Todas.ToDictionary(h => h.Id, h => (h.Estado, h.Respuesta, h.Datos.Nombre, h.Datos.Documento, h.Detalle, h.FechaRegistro));

        await Aplicador(e).Aplicar(Ana, Guid.NewGuid());

        Assert.All(e.Hojas.Todas, h =>
        {
            Assert.True(h.DeCuentaCancelada);
            Assert.Equal(antes[h.Id], (h.Estado, h.Respuesta, h.Datos.Nombre, h.Datos.Documento, h.Detalle, h.FechaRegistro));
        });
        Assert.Equal(2, e.Hojas.Todas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-18")]
    public async Task La_administracion_sigue_viendo_la_hoja_y_otra_persona_con_el_mismo_correo_no_la_ve_ni_la_consulta_por_su_numero()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, "ana@correo.com");
        var id = await e.RegistrarDeOtroCorreo("ana@correo.com");
        await Aplicador(e).Aplicar(Ana, Guid.NewGuid());
        var nueva = EntornoDeConsumidor.Nuevo(Otra, "ana@correo.com");
        nueva.Hojas.Sembrar(e.Hojas.Todas.Single());

        var lista = await nueva.Consumidor.Listar(new ListarMisHojasEntrada());
        var porNumero = await nueva.Consumidor.Obtener("2026-000001");
        var admin = await e.Administrar.Obtener(id);

        Assert.Equal(0, lista.Valor.Total);
        Assert.False(porNumero.EsExito);
        Assert.True(admin.EsExito);
        Assert.Equal("Ana Pérez Quispe", admin.Valor.Nombre);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-18")]
    public async Task Es_idempotente_y_no_falla_si_la_cuenta_no_tiene_hojas()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, "ana@correo.com");

        await Aplicador(e).Aplicar(Ana, Guid.NewGuid());

        await e.RegistrarDeOtroCorreo("ana@correo.com");
        await Aplicador(e).Aplicar(Ana, Guid.NewGuid());
        await Aplicador(e).Aplicar(Ana, Guid.NewGuid());
        Assert.True(e.Hojas.Todas.Single().DeCuentaCancelada);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-18")]
    public async Task No_toca_las_hojas_de_otro_correo()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, "ana@correo.com");
        await e.RegistrarDeOtroCorreo("ana@correo.com");
        await e.RegistrarDeOtroCorreo("otra@correo.com");

        await Aplicador(e).Aplicar(Ana, Guid.NewGuid());

        Assert.Equal(1, e.Hojas.Todas.Count(h => h.DeCuentaCancelada));
        Assert.False(e.Hojas.Todas.Single(h => h.Datos.CorreoNormalizado == "otra@correo.com").DeCuentaCancelada);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-18")]
    public async Task Una_cuenta_sin_correo_verificado_no_marca_nada_porque_el_correo_podria_ser_de_otra_persona()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, null);
        await e.RegistrarDeOtroCorreo("ana@correo.com");

        await Aplicador(e).Aplicar(Ana, Guid.NewGuid());

        Assert.False(e.Hojas.Todas.Single().DeCuentaCancelada);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-18")]
    public async Task Si_algo_falla_a_mitad_lanza_y_la_transaccion_de_Identidad_revierte_todas_las_marcas()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, "ana@correo.com");
        await e.RegistrarDeOtroCorreo("ana@correo.com");
        await e.RegistrarDeOtroCorreo("ana@correo.com");
        e.Hojas.FallaAlMarcar = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Base.Unidad.Ejecutar(async token =>
        {
            await Aplicador(e).Aplicar(Ana, Guid.NewGuid(), token);
            return true;
        }));

        Assert.All(e.Hojas.Todas, h => Assert.False(h.DeCuentaCancelada));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-18")]
    public async Task Si_Identidad_no_da_el_correo_lanza_para_que_la_cancelacion_no_se_ejecute()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, "ana@correo.com");
        e.Correos.Falla = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Aplicador(e).Aplicar(Ana, Guid.NewGuid()));
        Assert.Equal("reclamaciones", Aplicador(e).Modulo);
    }
}

public sealed class CorreoVerificadoNoResolubleTests
{
    private static readonly Guid Ana = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    [Trait("Criterio", "AC-REC-18")]
    public async Task Un_valor_que_no_es_una_direccion_no_se_toma_por_sin_correo_verificado_y_el_conteo_y_la_cancelacion_fallan_cerrados()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, "esto no es un correo");
        await e.RegistrarDeOtroCorreo("ana@correo.com");

        await Assert.ThrowsAsync<InvalidOperationException>(() => new ContarHojasPendientesDeUnCorreo(e.Correos, e.Hojas).Contar(Ana));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new MarcarHojasDeUnaCuentaCancelada(e.Correos, e.Hojas).Aplicar(Ana, Guid.NewGuid()));
        Assert.False(e.Hojas.Todas.Single().DeCuentaCancelada);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-12")]
    public async Task En_mis_hojas_un_correo_irresoluble_es_servicio_no_disponible_y_no_correo_no_verificado()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, "esto no es un correo");

        var lista = await e.Consumidor.Listar(new ListarMisHojasEntrada());
        var una = await e.Consumidor.Obtener("2026-000001");

        Assert.Equal(Application.Plataforma.CategoriaDeError.NoDisponible, lista.Error!.Categoria);
        Assert.Equal(Application.Plataforma.CategoriaDeError.NoDisponible, una.Error!.Categoria);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-12")]
    [InlineData(10_001)]
    [InlineData(1_000_000)]
    public async Task La_paginacion_muy_profunda_se_rechaza_sin_consultar_las_hojas(int pagina)
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, "ana@correo.com");
        var antes = e.Hojas.Operaciones;

        var resultado = await e.Consumidor.Listar(new ListarMisHojasEntrada(pagina, 50));

        Assert.Equal(Application.Plataforma.CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal(antes, e.Hojas.Operaciones);
    }
}
