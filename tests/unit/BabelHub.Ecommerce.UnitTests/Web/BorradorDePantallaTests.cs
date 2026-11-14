using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>El borrador de pantalla (T02, WEB-25, D06): lo escrito al vencer la sesión, solo de una lista cerrada de plantillas, ligado al usuario y de entrega única.</summary>
public sealed class BorradorDePantallaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.FromHours(-5));
    private static readonly Guid Ana = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Beto = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private const string Plantilla = "/reclamaciones/nueva";

    private static BorradorDePantalla Crear(IReadOnlyDictionary<string, string>? campos = null, string plantilla = Plantilla) =>
        BorradorDePantalla.Crear(Guid.NewGuid(), Ana, plantilla, campos ?? new Dictionary<string, string> { ["detalle"] = "Mi reclamo", ["monto"] = "12,50" }, Ahora);

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public void Un_borrador_conserva_los_campos_30_minutos_y_se_entrega_al_mismo_usuario()
    {
        var borrador = Crear();

        var entrega = borrador.Entregar(Ana, Ahora.AddMinutes(29));

        Assert.Equal(ResultadoDeEntrega.Entregado, entrega.Resultado);
        Assert.Equal("Mi reclamo", entrega.Campos!["detalle"]);
        Assert.Equal(Ahora.AddMinutes(30), borrador.VenceEn);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public void La_entrega_es_unica_y_pasados_30_minutos_ya_no_existe()
    {
        var unico = Crear();
        var vencido = Crear();

        Assert.Equal(ResultadoDeEntrega.Entregado, unico.Entregar(Ana, Ahora.AddMinutes(1)).Resultado);
        Assert.Equal(ResultadoDeEntrega.YaEntregado, unico.Entregar(Ana, Ahora.AddMinutes(2)).Resultado);
        Assert.Equal(ResultadoDeEntrega.Vencido, vencido.Entregar(Ana, Ahora.AddMinutes(30)).Resultado);
        Assert.Null(vencido.Entregar(Ana, Ahora.AddMinutes(31)).Campos);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public void Otra_persona_que_ingresa_en_el_mismo_equipo_no_ve_lo_escrito_y_no_lo_gasta()
    {
        var borrador = Crear();

        var ajeno = borrador.Entregar(Beto, Ahora.AddMinutes(1));
        var propio = borrador.Entregar(Ana, Ahora.AddMinutes(2));

        Assert.Equal(ResultadoDeEntrega.NoEsDelUsuario, ajeno.Resultado);
        Assert.Null(ajeno.Campos);
        Assert.Equal(ResultadoDeEntrega.Entregado, propio.Resultado);
    }

    [Theory]
    [InlineData("/ingreso")]
    [InlineData("/recuperacion")]
    [InlineData("/admin/usuarios")]
    [InlineData("/reclamaciones/nueva?x=1")]
    [InlineData("https://externo.example/reclamaciones/nueva")]
    [InlineData("")]
    [Trait("Criterio", "AC-WEB-20")]
    public void Solo_las_plantillas_de_una_lista_cerrada_pueden_tener_borrador(string plantilla) =>
        Assert.Throws<ArgumentException>(() => Crear(plantilla: plantilla));

    [Theory]
    [InlineData("contrasena")]
    [InlineData("Contraseña")]
    [InlineData("password")]
    [InlineData("codigo")]
    [InlineData("codigoDeVerificacion")]
    [InlineData("token")]
    [InlineData("__RequestVerificationToken")]
    [InlineData("claveSecreta")]
    [Trait("Criterio", "AC-WEB-20")]
    public void Un_campo_de_contrasena_codigo_o_token_nunca_se_conserva_aunque_alguien_lo_marque(string campo) =>
        Assert.Throws<ArgumentException>(() => Crear(new Dictionary<string, string> { [campo] = "x" }));

    [Theory]
    [InlineData("")]
    [InlineData("1campo")]
    [InlineData("campo con espacio")]
    [InlineData("a;b")]
    [Trait("Criterio", "AC-WEB-20")]
    public void Los_nombres_de_campo_tienen_una_forma_estricta(string campo) =>
        Assert.Throws<ArgumentException>(() => Crear(new Dictionary<string, string> { [campo] = "x" }));

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public void Sin_campos_no_hay_borrador() =>
        Assert.Throws<ArgumentException>(() => Crear(new Dictionary<string, string>()));

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public void El_cuerpo_no_pasa_de_32_KB_medidos_en_UTF8()
    {
        var justo = new Dictionary<string, string> { ["detalle"] = new string('a', PoliticaDeBorradores.TamanoMaximoEnBytes - "detalle".Length) };
        var excedido = new Dictionary<string, string> { ["detalle"] = new string('a', PoliticaDeBorradores.TamanoMaximoEnBytes - "detalle".Length + 1) };
        var multibyte = new Dictionary<string, string> { ["detalle"] = new string('ñ', PoliticaDeBorradores.TamanoMaximoEnBytes / 2) };

        Assert.NotNull(Crear(justo));
        Assert.Throws<ArgumentException>(() => Crear(excedido));
        Assert.Throws<ArgumentException>(() => Crear(multibyte));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public void Un_usuario_vacio_no_puede_tener_borrador() =>
        Assert.Throws<ArgumentException>(() => BorradorDePantalla.Crear(Guid.NewGuid(), Guid.Empty, Plantilla, new Dictionary<string, string> { ["a"] = "b" }, Ahora));

    [Theory]
    [InlineData(4, false, true)]
    [InlineData(5, false, false)]
    [InlineData(5, true, true)]
    [InlineData(0, false, true)]
    [Trait("Criterio", "AC-WEB-20")]
    public void Hay_hasta_5_borradores_por_usuario_y_reemplazar_el_de_la_misma_plantilla_no_cuenta(int existentes, bool reemplaza, bool permitido) =>
        Assert.Equal(permitido, PoliticaDeBorradores.PuedeGuardar(existentes, reemplaza));

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public void Los_campos_entregados_son_una_copia_que_no_cambia_el_borrador()
    {
        var origen = new Dictionary<string, string> { ["detalle"] = "uno" };
        var borrador = Crear(origen);
        origen["detalle"] = "cambiado";

        Assert.Equal("uno", borrador.Entregar(Ana, Ahora.AddMinutes(1)).Campos!["detalle"]);
    }
}
