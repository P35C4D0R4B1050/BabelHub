using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Soporte;
using PedidoDeDominio = BabelHub.Ecommerce.Domain.Pedidos.Pedido;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>ECO-6f: las lecturas que Pedidos da a Envíos y Devoluciones, el contacto auditado, las operaciones fallidas de la administración y las fachadas públicas por rol.</summary>
public sealed class FachadasYLecturasDePedidosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static int _contador;

    private sealed class Entorno(ActorDePrueba actor)
    {
        public RepositorioDePedidosFalso Repositorio { get; } = new();

        public DenegacionesDelCatalogoEspia Denegaciones { get; } = new();

        public LimitesPermisivosDePedidos Limites { get; } = new();

        public AuditoriaDeContactoFalsa AuditoriaDeContacto { get; } = new();

        public AutorizadorDePedidos Autorizador => new(actor, Denegaciones, new ResolucionDeSucursalFalsa());

        public ControlesDeGestionDePedidos Controles => new(Limites, new EstadoDeCuentaFalso(), Repositorio);

        public ContactoDeEntregaDelPedidoDeLaAplicacion Contacto => new(Autorizador, Controles, Repositorio, AuditoriaDeContacto);

        public OperacionesFallidasDePedidos Fallidas => new(Autorizador, Controles, Repositorio);
    }

    private static string Numero() => "ABCD23456" + "ABCDEFGHJKLMNPQRSTUVWXYZ"[Interlocked.Increment(ref _contador) % 24];

    private static PedidoDeDominio Domicilio(Guid? cuenta = null)
    {
        var lineas = new[]
        {
            new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Impreso", null, 2, 40m, 80m, "CUS", false, null, null),
            new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), true, "Digital", null, 1, 30m, 30m, null, false, null, null),
        };
        var contacto = BabelHub.Ecommerce.Domain.Pedidos.ContactoDeEntrega.Crear("Ana Pérez", "987654321", "Jr. Lima 123");
        return PedidoDeDominio.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), Numero(), cuenta ?? Cuenta, TipoDeEntregaDelPedido.Domicilio, null, "ayacucho", 12m, 122m, contacto, new("t-1", Ahora), new("p-1", Ahora), new("rd-1", Ahora), lineas, Ahora)).Pedido!;
    }

    // ---------- Lecturas para Envíos y Devoluciones ----------

    [Fact]
    public async Task Envios_lee_el_pedido_con_la_fuente_y_si_la_linea_esta_lista_sin_precios_ni_contacto_y_uno_inexistente_es_no_encontrado()
    {
        var entorno = new Entorno(ActorDePrueba.DePersonalDeLogistica());
        var pedido = Domicilio();
        var impresa = pedido.Lineas[0].Id;
        pedido.PrepararLineas([impresa], Ahora);
        pedido.MarcarListas([impresa], Ahora);
        entorno.Repositorio.Poner(pedido);
        var lectura = new PedidosParaEnviosDeLaAplicacion(entorno.Repositorio);

        var leido = await lectura.Obtener(pedido.Id);
        var inexistente = await lectura.Obtener(Guid.NewGuid());

        Assert.True(leido.EsExito);
        Assert.Equal((pedido.Numero.Valor, Cuenta, true), (leido.Valor.Numero, leido.Valor.CuentaId, leido.Valor.EsADomicilio));
        Assert.Equal([(false, "CUS", true), (true, null, false)], leido.Valor.Lineas.Select(l => (l.EsDigital, l.CodigoDeFuente, l.EstaLista)).Select(t => (t.Item1, t.Item2, t.Item3)));
        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
    }

    [Fact]
    public async Task Devoluciones_lee_el_pedido_con_el_estado_y_la_fecha_de_entrega_y_lista_los_pedidos_de_la_cuenta_acotados_a_100()
    {
        var entorno = new Entorno(ActorDePrueba.DeCliente(Cuenta));
        var pedido = Domicilio();
        var ids = pedido.Lineas.Select(l => l.Id).ToList();
        pedido.PrepararLineas([ids[0]], Ahora);
        pedido.MarcarListas([ids[0]], Ahora);
        pedido.DespacharLineas([ids[0]], Ahora);
        pedido.EntregarLineas([ids[0]], Ahora.AddDays(1));
        entorno.Repositorio.Poner(pedido);
        entorno.Repositorio.Poner(Domicilio(Guid.NewGuid()));
        var lectura = new PedidosParaDevolucionesDeLaAplicacion(entorno.Repositorio);

        var leido = await lectura.Obtener(pedido.Id);
        var lista = await lectura.ListarDeUnaCuenta(Cuenta, 1, 5000);

        Assert.Equal((EstadoVisibleDeLaLinea.Entregado, (DateTimeOffset?)Ahora.AddDays(1)), (leido.Valor.Lineas[0].Estado, leido.Valor.Lineas[0].EntregadaEn));
        Assert.Equal(EstadoVisibleDeLaLinea.DisponibleEnLaBiblioteca, leido.Valor.Lineas[1].Estado);
        Assert.Equal([pedido.Id], lista.Valor.PedidoIds);
        Assert.Equal((100, 1), (lista.Valor.Tamano, lista.Valor.Total));
        Assert.Equal(CategoriaDeError.Validacion, (await lectura.ListarDeUnaCuenta(Guid.Empty, 1, 10)).Error!.Categoria);
    }

    // ---------- El contacto para la guía ----------

    [Fact]
    public async Task Logistica_lee_el_contacto_con_su_region_y_cada_lectura_se_audita_con_su_rol()
    {
        var entorno = new Entorno(ActorDePrueba.DePersonalDeLogistica());
        var pedido = Domicilio();
        entorno.Repositorio.Poner(pedido);

        var lectura = await entorno.Contacto.Leer(pedido.Id);

        Assert.Equal(EstadoDeLecturaDelContacto.Disponible, lectura.Estado);
        Assert.Equal(("Ana Pérez", "987654321", "Jr. Lima 123", "ayacucho"), (lectura.Contacto!.Nombre, lectura.Contacto.Telefono, lectura.Contacto.Direccion, lectura.Contacto.Region));
        Assert.Equal([(pedido.Id, "personal-de-logistica")], entorno.AuditoriaDeContacto.Lecturas);
        Assert.DoesNotContain("Ana", lectura.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_poder_auditar_sin_presupuesto_o_con_otro_actor_el_contacto_no_se_entrega_y_uno_anonimizado_se_dice()
    {
        var pedido = Domicilio();

        var sinAuditoria = new Entorno(ActorDePrueba.DePersonalDeLogistica());
        sinAuditoria.Repositorio.Poner(pedido);
        sinAuditoria.AuditoriaDeContacto.Caida = true;
        Assert.Equal(EstadoDeLecturaDelContacto.NoDisponible, (await sinAuditoria.Contacto.Leer(pedido.Id)).Estado);

        var sinPresupuesto = new Entorno(ActorDePrueba.DeAdministradorCentral());
        sinPresupuesto.Repositorio.Poner(pedido);
        sinPresupuesto.Limites.Contacto = ResultadoDeLimiteDePedidos.NoDisponible;
        Assert.Equal(EstadoDeLecturaDelContacto.NoDisponible, (await sinPresupuesto.Contacto.Leer(pedido.Id)).Estado);
        Assert.Empty(sinPresupuesto.AuditoriaDeContacto.Lecturas);

        foreach (var actor in new[] { ActorDePrueba.DelSistema(), ActorDePrueba.DeCliente(Cuenta), ActorDePrueba.DeEncargadoDe(ResolucionDeSucursalFalsa.Lima) })
        {
            var otro = new Entorno(actor);
            otro.Repositorio.Poner(pedido);
            Assert.Equal(EstadoDeLecturaDelContacto.NoDisponible, (await otro.Contacto.Leer(pedido.Id)).Estado);
            Assert.Empty(otro.AuditoriaDeContacto.Lecturas);
        }

        var anonimizado = Domicilio();
        anonimizado.AnonimizarContacto(Ahora);
        var entorno = new Entorno(ActorDePrueba.DePersonalDeLogistica());
        entorno.Repositorio.Poner(anonimizado);
        Assert.Equal(EstadoDeLecturaDelContacto.Anonimizado, (await entorno.Contacto.Leer(anonimizado.Id)).Estado);
        Assert.Equal(EstadoDeLecturaDelContacto.NoDisponible, (await entorno.Contacto.Leer(Guid.NewGuid())).Estado);
    }

    // ---------- Las operaciones fallidas ----------

    [Fact]
    [Trait("Criterio", "AC-PED-19")]
    public async Task La_administracion_ve_las_operaciones_fallidas_sin_datos_personales_y_ningun_otro_rol_las_ve()
    {
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());
        var pedido = Domicilio();
        entorno.Repositorio.Poner(pedido);
        var anulacion = Anulacion.Iniciar(Guid.NewGuid(), pedido.Id, pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.PagoDuplicado).Plan!, new IniciadorDeOperacion(Guid.NewGuid(), "administrador-central", null), Ahora);
        anulacion.MarcarPaso(PasoDeAnulacion.EnviosCancelados, Ahora);
        anulacion.Fallar(Ahora);
        var entrega = Entrega.Iniciar(Guid.NewGuid(), pedido.Id, [pedido.Lineas[0].Id], new IniciadorDeOperacion(Guid.NewGuid(), "encargado-de-sucursal", null), Ahora.AddMinutes(1));
        entrega.Fallar(Ahora);
        entorno.Repositorio.Anulaciones.Add(anulacion);
        entorno.Repositorio.Entregas.Add(entrega);

        var pagina = await entorno.Fallidas.Listar(1, 20, default);

        Assert.True(pagina.EsExito);
        Assert.Equal(2, pagina.Valor.Total);
        Assert.Equal(["entrega", "anulacion"], pagina.Valor.Operaciones.Select(o => o.Tipo));
        var delaAnulacion = pagina.Valor.Operaciones.Single(o => o.Tipo == "anulacion");
        Assert.Equal("pago-duplicado", delaAnulacion.Motivo);
        Assert.Equal(["EnviosCancelados"], delaAnulacion.PasosHechos);

        foreach (var actor in new[] { ActorDePrueba.DeEncargadoDe(ResolucionDeSucursalFalsa.Lima), ActorDePrueba.DePersonalDeLogistica(), ActorDePrueba.DeCliente(Cuenta) })
        {
            var otro = new Entorno(actor);
            Assert.Equal("prohibido", (await otro.Fallidas.Listar(1, 20, default)).Error!.Clave);
        }
    }

    // ---------- Las fachadas ----------

    [Fact]
    public async Task La_fachada_de_administracion_y_la_del_cliente_traducen_a_los_casos_de_uso_con_el_rol_de_cada_una()
    {
        var admin = new Entorno(ActorDePrueba.DeAdministradorCentral());
        var consultasAdmin = new ConsultasDePedidos(admin.Autorizador, admin.Controles, admin.Repositorio, new ReembolsosFalsos(), admin.AuditoriaDeContacto);
        var pedido = Domicilio();
        admin.Repositorio.Poner(pedido);
        var fachadaAdmin = new AdministracionDePedidosDeLaAplicacion(consultasAdmin, null!, null!, admin.Fallidas);

        var lista = await fachadaAdmin.Listar(new FiltroDePedidos(null, 1, 10));
        var fallidas = await fachadaAdmin.ListarOperacionesFallidas(1, 10);

        Assert.Equal(1, lista.Valor.Total);
        Assert.True(fallidas.EsExito);

        var cliente = new Entorno(ActorDePrueba.DeCliente(Cuenta));
        cliente.Repositorio.Poner(pedido);
        var consultasCliente = new ConsultasDePedidos(cliente.Autorizador, cliente.Controles, cliente.Repositorio, new ReembolsosFalsos(), cliente.AuditoriaDeContacto);
        var fachadaCliente = new PedidosDelClienteDeLaAplicacion(consultasCliente, null!);

        Assert.Equal(1, (await fachadaCliente.Listar(new FiltroDePedidos(null, 1, 10))).Valor.Total);
        Assert.Equal(pedido.Numero.Valor, (await fachadaCliente.Obtener(pedido.Id)).Valor.Resumen.Numero);
    }
}
