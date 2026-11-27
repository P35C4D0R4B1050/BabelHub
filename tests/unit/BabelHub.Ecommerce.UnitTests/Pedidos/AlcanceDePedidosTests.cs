using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Soporte;
using BabelHub.Shared.Contratos;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T09 (PED-07, PED-08, PED-26, PED-28, PED-40, PED-41; AC-PED-06, AC-PED-25, AC-PED-34, AC-PED-35): rol, alcance del pedido, pertenencia de las líneas y la matriz rol × operación.</summary>
public sealed class AlcanceDePedidosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Lima = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Ayacucho = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class SucursalesFalsas : IResolucionDeSucursal
    {
        public Task<Guid?> IdentificadorDe(string codigoDeSucursal, CancellationToken cancelacion = default) =>
            Task.FromResult<Guid?>(codigoDeSucursal switch { "LIM" => Lima, "AYA" => Ayacucho, _ => null });
    }

    private static (AutorizadorDePedidos Autorizador, DenegacionesDelCatalogoEspia Denegaciones) Crear(ActorDePrueba actor)
    {
        var denegaciones = new DenegacionesDelCatalogoEspia();
        return (new AutorizadorDePedidos(actor, denegaciones, new SucursalesFalsas()), denegaciones);
    }

    private static Pedido Pedido(TipoDeEntregaDelPedido tipo, Guid? cuenta = null, string? sucursal = "LIM")
    {
        var linea = new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Libro", null, 1, 50m, 50m, "LIM", false, null, null);
        var contacto = tipo == TipoDeEntregaDelPedido.Domicilio ? ContactoDeEntrega.Crear("Ana", "987654321", "Jr. Lima 123") : null;
        var datos = new DatosDePedidoNuevo(
            Guid.NewGuid(), "ABCD234567", cuenta ?? Cuenta, tipo, tipo == TipoDeEntregaDelPedido.Recojo ? sucursal : null, tipo == TipoDeEntregaDelPedido.Domicilio ? "ayacucho" : null, tipo == TipoDeEntregaDelPedido.Domicilio ? 12m : 0m,
            tipo == TipoDeEntregaDelPedido.Domicilio ? 62m : 50m, contacto, new("t-1", Ahora), new("p-1", Ahora), null, [linea], Ahora);
        return Domain.Pedidos.Pedido.Crear(datos).Pedido!;
    }

    private static ActorDePrueba Logistica() => ActorDePrueba.DePersonalDeLogistica();

    private static ActorDePrueba Admin() => ActorDePrueba.DeAdministradorCentral();

    [Fact]
    public void Las_constantes_de_los_roles_de_Pedidos_son_las_de_Shared()
    {
        Assert.Equal(RolesDeUsuario.Cliente, RolesDePedidos.Cliente);
        Assert.Equal(RolesDeUsuario.EncargadoDeSucursal, RolesDePedidos.EncargadoDeSucursal);
        Assert.Equal(RolesDeUsuario.PersonalDeLogistica, RolesDePedidos.PersonalDeLogistica);
        Assert.Equal(RolesDeUsuario.AdministradorCentral, RolesDePedidos.AdministradorCentral);
    }

    // ---------- Paso 1: el rol (matriz rol × operación) ----------

    [Theory]
    [InlineData("cliente", "PedidosDelCliente", true)]
    [InlineData("cliente", "GestionDeRecojo", false)]
    [InlineData("cliente", "GestionDeDespacho", false)]
    [InlineData("cliente", "Administracion", false)]
    [InlineData("encargado", "GestionDeRecojo", true)]
    [InlineData("encargado", "PedidosDelCliente", false)]
    [InlineData("encargado", "GestionDeDespacho", false)]
    [InlineData("encargado", "Administracion", false)]
    [InlineData("logistica", "GestionDeDespacho", true)]
    [InlineData("logistica", "GestionDeRecojo", false)]
    [InlineData("logistica", "Administracion", false)]
    [InlineData("administrador", "Administracion", true)]
    [InlineData("administrador", "GestionDeDespacho", true)]
    [InlineData("administrador", "GestionDeRecojo", false)]
    [InlineData("administrador", "PedidosDelCliente", false)]
    [Trait("Criterio", "AC-PED-25")]
    public async Task La_matriz_rol_por_operacion_permite_solo_lo_que_el_plan_dice_y_lo_demas_es_prohibido_con_su_evento(string rol, string nombreDeLaOperacion, bool permitido)
    {
        var actor = rol switch
        {
            "cliente" => ActorDePrueba.DeCliente(Cuenta),
            "encargado" => ActorDePrueba.DeEncargadoDe(Lima),
            "logistica" => Logistica(),
            _ => Admin(),
        };
        var operacion = Enum.Parse<OperacionDePedidos>(nombreDeLaOperacion);
        var (autorizador, denegaciones) = Crear(actor);

        var resultado = await autorizador.ExigirRol(operacion, default);

        Assert.Equal(permitido, resultado.EsExito);
        if (permitido)
        {
            Assert.Empty(denegaciones.Eventos);
        }
        else
        {
            Assert.Equal((CategoriaDeError.Prohibido, "prohibido"), (resultado.Error!.Categoria, resultado.Error.Clave));
            Assert.Equal(["fuera-de-rol"], denegaciones.Eventos.Select(e => e.Motivo));
        }
    }

    [Fact]
    public async Task Sin_sesion_o_sin_contexto_es_no_autenticado_con_su_evento()
    {
        var (anonimo, eventosA) = Crear(ActorDePrueba.Anonimo());
        var (sinContexto, eventosB) = Crear(ActorDePrueba.SinContexto());

        var a = await anonimo.ExigirRol(OperacionDePedidos.PedidosDelCliente, default);
        var b = await sinContexto.ExigirRol(OperacionDePedidos.PedidosDelCliente, default);

        Assert.Equal(CategoriaDeError.NoAutenticado, a.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoAutenticado, b.Error!.Categoria);
        Assert.Equal(["sin-sesion"], eventosA.Eventos.Select(e => e.Motivo));
        Assert.Equal(["sin-sesion"], eventosB.Eventos.Select(e => e.Motivo));
    }

    [Fact]
    public async Task Un_trabajo_de_sistema_o_una_integracion_no_pasan_por_el_rol_de_una_persona_y_solo_el_sistema_pasa_por_ExigirSistema()
    {
        var (sistema, _) = Crear(ActorDePrueba.DelSistema());
        var (integracion, eventos) = Crear(ActorDePrueba.DeIntegracion("solicitar-aviso"));

        Assert.False((await integracion.ExigirRol(OperacionDePedidos.Administracion, default)).EsExito);
        Assert.Null(await sistema.ExigirSistema(default));
        Assert.Equal(CategoriaDeError.Prohibido, (await integracion.ExigirSistema(default))!.Categoria);
        Assert.Equal(2, eventos.Eventos.Count);
    }

    // ---------- Paso 2: el pedido y su alcance ----------

    [Fact]
    [Trait("Criterio", "AC-PED-35")]
    public async Task Un_cliente_que_pide_un_pedido_ajeno_o_inexistente_recibe_el_mismo_no_encontrado_y_queda_un_evento_por_cada_intento()
    {
        var (autorizador, denegaciones) = Crear(ActorDePrueba.DeCliente(Cuenta));
        var solicitante = (await autorizador.ExigirRol(OperacionDePedidos.PedidosDelCliente, default)).Valor;
        var ajeno = Pedido(TipoDeEntregaDelPedido.Recojo, cuenta: Guid.NewGuid());

        var deOtro = await autorizador.ComprobarAlcance(ajeno, solicitante, default);
        var inexistente = await autorizador.ComprobarAlcance(null, solicitante, default);

        Assert.Equal(deOtro, inexistente);
        Assert.Equal((CategoriaDeError.NoEncontrado, "no-encontrado"), (deOtro!.Categoria, deOtro.Clave));
        Assert.Equal(["recurso-ajeno", "recurso-ajeno"], denegaciones.Eventos.Select(e => e.Motivo));
    }

    [Fact]
    public async Task Un_cliente_ve_su_pedido_y_la_administracion_ve_todos_sin_evento()
    {
        var (cliente, eventosC) = Crear(ActorDePrueba.DeCliente(Cuenta));
        var (admin, eventosA) = Crear(Admin());
        var propio = Pedido(TipoDeEntregaDelPedido.Domicilio, cuenta: Cuenta);
        var deOtro = Pedido(TipoDeEntregaDelPedido.Recojo, cuenta: Guid.NewGuid());

        Assert.Null(await cliente.ComprobarAlcance(propio, (await cliente.ExigirRol(OperacionDePedidos.PedidosDelCliente, default)).Valor, default));
        var solicitanteAdmin = (await admin.ExigirRol(OperacionDePedidos.Administracion, default)).Valor;
        Assert.Null(await admin.ComprobarAlcance(propio, solicitanteAdmin, default));
        Assert.Null(await admin.ComprobarAlcance(deOtro, solicitanteAdmin, default));
        Assert.Empty(eventosC.Eventos);
        Assert.Empty(eventosA.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-06")]
    public async Task Un_encargado_ve_solo_los_pedidos_de_recojo_de_su_sucursal_y_los_de_otra_o_a_domicilio_son_no_encontrado()
    {
        var (autorizador, denegaciones) = Crear(ActorDePrueba.DeEncargadoDe(Ayacucho));
        var solicitante = (await autorizador.ExigirRol(OperacionDePedidos.GestionDeRecojo, default)).Valor;
        var propio = Pedido(TipoDeEntregaDelPedido.Recojo, sucursal: "AYA");
        var deLima = Pedido(TipoDeEntregaDelPedido.Recojo, sucursal: "LIM");
        var domicilio = Pedido(TipoDeEntregaDelPedido.Domicilio);

        Assert.Null(await autorizador.ComprobarAlcance(propio, solicitante, default));
        var otra = await autorizador.ComprobarAlcance(deLima, solicitante, default);
        var aDomicilio = await autorizador.ComprobarAlcance(domicilio, solicitante, default);

        Assert.Equal(CategoriaDeError.NoEncontrado, otra!.Categoria);
        Assert.Equal(otra, aDomicilio);
        Assert.Equal(["recurso-ajeno", "recurso-ajeno"], denegaciones.Eventos.Select(e => e.Motivo));
    }

    [Fact]
    public async Task Una_sucursal_de_recojo_que_no_se_resuelve_se_niega_y_se_registra()
    {
        var (autorizador, denegaciones) = Crear(ActorDePrueba.DeEncargadoDe(Lima));
        var solicitante = (await autorizador.ExigirRol(OperacionDePedidos.GestionDeRecojo, default)).Valor;
        var sinResolver = Pedido(TipoDeEntregaDelPedido.Recojo, sucursal: "ZZZ");

        var error = await autorizador.ComprobarAlcance(sinResolver, solicitante, default);

        Assert.Equal(CategoriaDeError.NoEncontrado, error!.Categoria);
        Assert.Single(denegaciones.Eventos);
    }

    [Fact]
    public async Task Logistica_ve_los_pedidos_a_domicilio_de_toda_la_red_y_los_de_recojo_son_no_encontrado()
    {
        var (autorizador, denegaciones) = Crear(Logistica());
        var solicitante = (await autorizador.ExigirRol(OperacionDePedidos.GestionDeDespacho, default)).Valor;

        Assert.Null(await autorizador.ComprobarAlcance(Pedido(TipoDeEntregaDelPedido.Domicilio, cuenta: Guid.NewGuid()), solicitante, default));
        var recojo = await autorizador.ComprobarAlcance(Pedido(TipoDeEntregaDelPedido.Recojo), solicitante, default);

        Assert.Equal(CategoriaDeError.NoEncontrado, recojo!.Categoria);
        Assert.Single(denegaciones.Eventos);
    }

    // ---------- El parámetro de sucursal de la ruta ----------

    [Fact]
    public async Task El_encargado_opera_en_su_sucursal_y_en_otra_o_en_una_que_no_se_resuelve_es_prohibido_con_el_evento_de_sucursal()
    {
        var (autorizador, denegaciones) = Crear(ActorDePrueba.DeEncargadoDe(Lima));
        var solicitante = (await autorizador.ExigirRol(OperacionDePedidos.GestionDeRecojo, default)).Valor;

        Assert.Null(await autorizador.ExigirSucursalDeLaRuta(solicitante, "LIM", default));
        var otra = await autorizador.ExigirSucursalDeLaRuta(solicitante, "AYA", default);
        var desconocida = await autorizador.ExigirSucursalDeLaRuta(solicitante, "ZZZ", default);
        var vacia = await autorizador.ExigirSucursalDeLaRuta(solicitante, " ", default);

        Assert.All(new[] { otra, desconocida, vacia }, e => Assert.Equal((CategoriaDeError.Prohibido, "prohibido"), (e!.Categoria, e.Clave)));
        Assert.Equal(["fuera-de-sucursal", "fuera-de-sucursal", "fuera-de-sucursal"], denegaciones.Eventos.Select(e => e.Motivo));
    }

    // ---------- Paso 3: pertenencia de las líneas ----------

    [Fact]
    public async Task Una_linea_ajena_repetida_o_ninguna_es_el_mismo_no_encontrado_con_su_evento()
    {
        var (autorizador, denegaciones) = Crear(Admin());
        var pedido = Pedido(TipoDeEntregaDelPedido.Domicilio);
        var propia = pedido.Lineas[0].Id;
        var quien = new SolicitanteDePedidos(RolesDePedidos.AdministradorCentral, Guid.NewGuid(), null);

        Assert.Null(await autorizador.ComprobarLineas(pedido, quien, [propia], default));
        var ajena = await autorizador.ComprobarLineas(pedido, quien, [propia, Guid.NewGuid()], default);
        var repetida = await autorizador.ComprobarLineas(pedido, quien, [propia, propia], default);
        var vacia = await autorizador.ComprobarLineas(pedido, quien, [], default);

        Assert.All(new[] { ajena, repetida, vacia }, e => Assert.Equal((CategoriaDeError.NoEncontrado, "no-encontrado"), (e!.Categoria, e.Clave)));
        Assert.Equal(3, denegaciones.Eventos.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-34")]
    public async Task Los_eventos_de_denegacion_nunca_llevan_el_identificador_ni_el_contacto()
    {
        var (autorizador, denegaciones) = Crear(ActorDePrueba.DeCliente(Cuenta));
        var solicitante = (await autorizador.ExigirRol(OperacionDePedidos.PedidosDelCliente, default)).Valor;
        var ajeno = Pedido(TipoDeEntregaDelPedido.Domicilio, cuenta: Guid.NewGuid());

        await autorizador.ComprobarAlcance(ajeno, solicitante, default);

        var evento = Assert.Single(denegaciones.Eventos);
        Assert.DoesNotContain(ajeno.Id.ToString(), evento.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ana", evento.ToString(), StringComparison.Ordinal);
    }
}
