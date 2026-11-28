using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Indice;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web.Indice;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>
/// El caso de uso del índice infinito (web-indice-infinito T05; AC-IND-01, 02, 03, 05, 06; D05): validación, páginas y direcciones al azar sobre un doble de <see cref="IAleatoriosSeguros"/> con bytes fijos. No hay transacción, repositorio ni caché:
/// el caso solo recibe el puerto de bytes aleatorios.
/// </summary>
public sealed class ConsultarIndiceInfinitoTests
{
    /// <summary>Bytes fijos: cada sorteo de 4 bytes toma el siguiente valor de la lista (la secuencia se repite al terminar).</summary>
    private sealed class AleatoriosFijos(params uint[] valores) : IAleatoriosSeguros
    {
        private int _siguiente;

        public int Sorteos { get; private set; }

        public byte[] Bytes(int cantidad)
        {
            Assert.Equal(4, cantidad);
            Sorteos++;
            return BitConverter.GetBytes(valores[_siguiente++ % valores.Length]);
        }
    }

    private static ConsultarIndiceInfinito Caso(params uint[] valores) => new(new AleatoriosFijos(valores));

    // Con el valor 0 cada rango elige su mínimo: s1-p1-e1-v1-pg1.
    private const string TodoMinimo = "s1-p1-e1-v1-pg1";

    [Fact]
    [Trait("Criterio", "AC-IND-01")]
    public void Abrir_una_direccion_valida_muestra_su_pagina_siempre_igual()
    {
        var caso = Caso(0);

        var uno = Assert.IsType<ResultadoDelIndice.Mostrar>(caso.Abrir("s12-p3-e4-v5-pg100"));
        var dos = Assert.IsType<ResultadoDelIndice.Mostrar>(caso.Abrir("s12-p3-e4-v5-pg100"));

        Assert.Equal("s12-p3-e4-v5-pg100", uno.Pagina.Direccion);
        Assert.Equal(uno.Pagina.Texto, dos.Pagina.Texto);
        Assert.Equal(GeneradorDePaginasDelIndice.Generar("s12-p3-e4-v5-pg100"), uno.Pagina.Texto);
    }

    [Theory]
    [Trait("Criterio", "AC-IND-06")]
    [InlineData(null)]
    [InlineData("")]
    public void Abrir_una_direccion_vacia_redirige_a_una_al_azar(string? vacia)
    {
        var resultado = Assert.IsType<ResultadoDelIndice.Redirigir>(Caso(0).Abrir(vacia));

        Assert.Equal(TodoMinimo, resultado.Direccion);
    }

    [Theory]
    [Trait("Criterio", "AC-IND-06")]
    [InlineData("Mayuscula")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("a%0d%0aSet-Cookie: x")]
    public void Abrir_una_direccion_no_valida_da_el_aviso_con_una_pagina_al_azar_sin_repetir_el_texto(string recibida)
    {
        var resultado = Assert.IsType<ResultadoDelIndice.DireccionNoValida>(Caso(0).Abrir(recibida));

        Assert.Equal(TodoMinimo, resultado.PaginaAlAzar.Direccion);
        Assert.Equal(GeneradorDePaginasDelIndice.Generar(TodoMinimo), resultado.PaginaAlAzar.Texto);
        Assert.DoesNotContain(recibida, resultado.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IND-06")]
    public void Abrir_el_borde_de_64_caracteres_muestra_la_pagina_y_el_de_65_da_el_aviso()
    {
        var caso = Caso(0);

        Assert.IsType<ResultadoDelIndice.Mostrar>(caso.Abrir(new string('a', 64)));
        Assert.IsType<ResultadoDelIndice.DireccionNoValida>(caso.Abrir(new string('a', 65)));
    }

    [Theory]
    [Trait("Criterio", "AC-IND-03")]
    [InlineData("s12-p3-e4-v5-pg100", "s12-p3-e4-v5-pg100")]
    [InlineData("  s12-p3-e4-v5-pg100  ", "s12-p3-e4-v5-pg100")]
    public void Entrar_con_una_direccion_valida_redirige_a_la_canonica_recortando_los_espacios(string campo, string canonica)
    {
        var resultado = Assert.IsType<ResultadoDelIndice.Redirigir>(Caso(0).Entrar(campo));

        Assert.Equal(canonica, resultado.Direccion);
    }

    [Theory]
    [Trait("Criterio", "AC-IND-06")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Entrar_con_el_campo_vacio_o_de_solo_espacios_redirige_a_una_al_azar(string? campo)
    {
        var resultado = Assert.IsType<ResultadoDelIndice.Redirigir>(Caso(0).Entrar(campo));

        Assert.Equal(TodoMinimo, resultado.Direccion);
    }

    [Theory]
    [Trait("Criterio", "AC-IND-06")]
    [InlineData("\nabc")]
    [InlineData("Sala 1")]
    public void Entrar_solo_recorta_el_espacio_u0020_y_cualquier_otro_borde_es_no_valido(string campo)
    {
        var resultado = Assert.IsType<ResultadoDelIndice.DireccionNoValida>(Caso(0).Entrar(campo));

        Assert.DoesNotContain(campo.Trim(), resultado.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IND-02")]
    public void Elegir_al_azar_da_una_direccion_distinta_de_la_actual_y_una_pagina_distinta()
    {
        // El primer sorteo completo repite la actual (todo mínimo); el segundo da otra.
        var caso = Caso(0, 0, 0, 0, 0, /* segundo intento */ 1, 1, 1, 1, 1);

        var nueva = caso.ElegirAlAzar(TodoMinimo);

        Assert.Equal("s2-p2-e2-v2-pg2", nueva);
        Assert.NotEqual(GeneradorDePaginasDelIndice.Generar(TodoMinimo), GeneradorDePaginasDelIndice.Generar(nueva));
    }

    [Fact]
    [Trait("Criterio", "AC-IND-02")]
    public void Si_el_azar_repite_la_actual_ocho_veces_se_avanza_la_pagina_sin_bucle()
    {
        var aleatorios = new AleatoriosFijos(0);
        var caso = new ConsultarIndiceInfinito(aleatorios);

        var nueva = caso.ElegirAlAzar(TodoMinimo);

        Assert.Equal("s1-p1-e1-v1-pg2", nueva);
        Assert.Equal(8 * 5, aleatorios.Sorteos);
    }

    [Theory]
    [Trait("Criterio", "AC-IND-02")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<script>")]
    public void Elegir_al_azar_ignora_un_desde_vacio_o_no_valido_y_no_lo_repite(string? desde)
    {
        var nueva = Caso(0).ElegirAlAzar(desde);

        Assert.Equal(TodoMinimo, nueva);
    }

    private static uint Maximo(int n) => (uint)((1UL << 32) - ((1UL << 32) % (ulong)n) - 1);

    [Fact]
    [Trait("Criterio", "AC-IND-02")]
    public void Un_generador_roto_que_solo_da_valores_rechazados_falla_en_lugar_de_girar_sin_fin()
    {
        var caso = Caso(uint.MaxValue);

        Assert.Throws<InvalidOperationException>(() => caso.ElegirAlAzar());
    }

    [Fact]
    [Trait("Criterio", "AC-IND-05")]
    public void Cien_paginas_no_necesitan_ninguna_base_de_datos_el_caso_solo_recibe_bytes_aleatorios()
    {
        var parametros = typeof(ConsultarIndiceInfinito).GetConstructors().Single().GetParameters();
        Assert.Equal([typeof(IAleatoriosSeguros)], parametros.Select(p => p.ParameterType));

        var caso = Caso(0, 7, 13, 99, 12345, 4000000000u);
        var direcciones = new HashSet<string>();
        for (var i = 0; i < 100; i++)
        {
            var resultado = Assert.IsType<ResultadoDelIndice.Mostrar>(caso.Abrir($"s1-p1-e1-v1-pg{i + 1}"));
            direcciones.Add(resultado.Pagina.Texto);
        }

        Assert.Equal(100, direcciones.Count);
    }
}
