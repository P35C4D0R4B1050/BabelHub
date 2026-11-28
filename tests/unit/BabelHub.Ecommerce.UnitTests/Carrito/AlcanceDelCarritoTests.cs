using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>T09: alcance y propiedad de Carrito (CAR-35, AC-CAR-31): la cuenta sale del actor, un identificador ajeno deja un evento con el nombre del campo y no su valor, y la cookie del visitante es opaca.</summary>
public sealed class AlcanceDelCarritoTests
{
    private static (AutorizadorDelCarrito Autorizador, DenegacionesDelCatalogoEspia Denegaciones) Crear(ActorDePrueba actor)
    {
        var denegaciones = new DenegacionesDelCatalogoEspia();
        return (new AutorizadorDelCarrito(actor, denegaciones), denegaciones);
    }

    // ---------- Quién pide ----------

    [Theory]
    [InlineData("encargado")]
    [InlineData("sistema")]
    [InlineData("integracion")]
    [InlineData("sin-contexto")]
    public void El_personal_el_sistema_y_las_integraciones_no_son_clientes_ni_visitantes(string tipo)
    {
        var actor = tipo switch
        {
            "encargado" => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
            "logistica" => ActorDePrueba.DePersonalDeLogistica(),
            "administrador" => ActorDePrueba.DeAdministradorCentral(),
            "sistema" => ActorDePrueba.DelSistema(),
            "integracion" => ActorDePrueba.DeIntegracion("pagos"),
            _ => ActorDePrueba.SinContexto(),
        };
        var (autorizador, _) = Crear(actor);

        Assert.Equal(TipoDeSolicitante.Rechazado, autorizador.QuienPide().Tipo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-31")]
    public async Task El_personal_sin_rol_de_cliente_se_rechaza_como_prohibido_y_deja_el_acceso_denegado()
    {
        var (autorizador, denegaciones) = Crear(ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));

        var resultado = await autorizador.ExigirCuenta(default);

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        var evento = Assert.Single(denegaciones.Eventos);
        Assert.Equal("fuera-de-rol", evento.Motivo);
    }

    [Fact]
    public async Task Un_visitante_que_pide_algo_de_cuenta_recibe_no_autenticado_sin_evento_de_acceso_ajeno()
    {
        var (autorizador, denegaciones) = Crear(ActorDePrueba.Anonimo());

        var resultado = await autorizador.ExigirCuenta(default);

        Assert.Equal(CategoriaDeError.NoAutenticado, resultado.Error!.Categoria);
        Assert.Equal("sin-sesion", Assert.Single(denegaciones.Eventos).Motivo);
    }

    [Fact]
    public async Task Sin_contexto_se_rechaza_como_no_autenticado_nunca_se_concede_por_omision()
    {
        var (autorizador, _) = Crear(ActorDePrueba.SinContexto());

        var resultado = await autorizador.ExigirCuenta(default);

        Assert.Equal(CategoriaDeError.NoAutenticado, resultado.Error!.Categoria);
    }

    // ---------- Identificadores ajenos ----------

    [Theory]
    [Trait("Criterio", "AC-CAR-31")]
    [InlineData(CampoDeIdentificadorAjeno.CarritoId, "recurso-ajeno-carrito-id")]
    [InlineData(CampoDeIdentificadorAjeno.CheckoutId, "recurso-ajeno-checkout-id")]
    [InlineData(CampoDeIdentificadorAjeno.CuentaId, "recurso-ajeno-cuenta-id")]
    [InlineData(CampoDeIdentificadorAjeno.PedidoId, "recurso-ajeno-pedido-id")]
    public async Task Un_identificador_ajeno_deja_un_evento_con_el_nombre_del_campo_y_responde_no_encontrado_uniforme(CampoDeIdentificadorAjeno campo, string motivo)
    {
        var (autorizador, denegaciones) = Crear(ActorDePrueba.DeCliente());

        var error = await autorizador.RechazarIdentificadorAjeno(campo, default);

        Assert.Equal(CategoriaDeError.NoEncontrado, error.Categoria);
        Assert.Equal(ClavesDelCarrito.NoEncontrado, error.Clave);
        var evento = Assert.Single(denegaciones.Eventos);
        Assert.Equal(motivo, evento.Motivo);
        Assert.Equal("ECO-NO-ENCONTRADO", evento.CodigoDeError);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-31")]
    public async Task El_evento_de_un_identificador_ajeno_solo_lleva_el_nombre_del_campo()
    {
        var (autorizador, denegaciones) = Crear(ActorDePrueba.DeCliente());
        await autorizador.RechazarIdentificadorAjeno(CampoDeIdentificadorAjeno.CarritoId, default);

        // El método no recibe el valor del identificador: el evento solo puede llevar el nombre del campo y ninguna ruta.
        var evento = Assert.Single(denegaciones.Eventos);
        Assert.Null(evento.Ruta);
        Assert.Equal("recurso-ajeno-carrito-id", evento.Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-31")]
    public async Task La_respuesta_a_un_identificador_ajeno_es_la_misma_que_la_de_uno_inexistente()
    {
        var (autorizador, _) = Crear(ActorDePrueba.DeCliente());

        var ajeno = await autorizador.RechazarIdentificadorAjeno(CampoDeIdentificadorAjeno.CheckoutId, default);
        var inexistente = autorizador.NoEncontrado();

        Assert.Equal((inexistente.Categoria, inexistente.Clave), (ajeno.Categoria, ajeno.Clave));
    }

    // ---------- pedidoId oculto ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-31")]
    public async Task Un_pedido_oculto_de_otra_cuenta_o_inexistente_responde_no_encontrado_uniforme_y_deja_el_evento()
    {
        var cuenta = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        var (autorizador, denegaciones) = Crear(ActorDePrueba.DeCliente(cuenta));

        var deOtra = await autorizador.ComprobarPedidoOculto(pedido, cuenta, new PedidoVisto(Guid.NewGuid(), EsElVivo: true), default);
        var inexistente = await autorizador.ComprobarPedidoOculto(pedido, cuenta, null, default);

        Assert.Equal(CategoriaDeError.NoEncontrado, deOtra!.Categoria);
        Assert.Equal((deOtra.Categoria, deOtra.Clave), (inexistente!.Categoria, inexistente.Clave));
        Assert.Equal(2, denegaciones.Eventos.Count);
        Assert.All(denegaciones.Eventos, e => Assert.Equal("recurso-ajeno-pedido-id", e.Motivo));
    }

    [Fact]
    public async Task Un_pedido_propio_ya_terminado_o_descartado_recibe_un_error_amable_y_no_deja_evento()
    {
        var cuenta = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        var (autorizador, denegaciones) = Crear(ActorDePrueba.DeCliente(cuenta));

        var error = await autorizador.ComprobarPedidoOculto(pedido, cuenta, new PedidoVisto(cuenta, EsElVivo: false), default);

        Assert.Equal(CategoriaDeError.Conflicto, error!.Categoria);
        Assert.Equal(ClavesDelCarrito.PedidoObsoleto, error.Clave);
        Assert.Empty(denegaciones.Eventos);
    }

    // ---------- Cookie del visitante ----------

    [Fact]
    public void Una_cookie_de_256_bits_en_base64url_es_valida_y_su_huella_es_un_SHA256_de_32_bytes()
    {
        var cookie = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i + 1)).ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.True(HuellaDeLaCookie.TryDe(cookie, out var huella));

        Assert.Equal(32, huella!.Length);
        Assert.True(HuellaDeLaCookie.TryDe(cookie, out var otra));
        Assert.Equal(huella, otra);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("corta")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("ÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁÁ")]
    public void Una_cookie_mal_formada_no_tiene_huella_y_es_sin_carrito(string? cookie) => Assert.False(HuellaDeLaCookie.TryDe(cookie, out _));
}
