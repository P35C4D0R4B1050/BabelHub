using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Soporte;
using ContactoDominio = BabelHub.Ecommerce.Domain.Pedidos.ContactoDeEntrega;
using ContactoPublico = BabelHub.Ecommerce.Application.Pedidos.ContactoDeEntrega;
using PedidoDeDominio = BabelHub.Ecommerce.Domain.Pedidos.Pedido;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T12 (PED-04, PED-07, PED-08, PED-14, PED-26, PED-40, PED-41; AC-PED-03, 06, 12, 34, 35): los listados sin contacto, el detalle con el contacto solo para quien corresponde y el filtro, la paginación y el traslado.</summary>
public sealed class ConsultasDePedidosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class Entorno(ActorDePrueba actor)
    {
        public RepositorioDePedidosFalso Repositorio { get; } = new();

        public DenegacionesDelCatalogoEspia Denegaciones { get; } = new();

        public LimitesPermisivosDePedidos Limites { get; } = new();

        public AuditoriaDeContactoFalsa AuditoriaDeContacto { get; } = new();

        public ReembolsosFalsos Reembolsos { get; } = new();

        public ConsultasDePedidos Consultas => new(
            new AutorizadorDePedidos(actor, Denegaciones, new ResolucionDeSucursalFalsa()),
            new ControlesDeGestionDePedidos(Limites, new EstadoDeCuentaFalso(), Repositorio),
            Repositorio,
            Reembolsos,
            AuditoriaDeContacto);
    }

    private static DatosDeLineaNueva Impresa(string titulo, bool traslado = false, DateOnly? llegada = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), false, titulo, "9780000000001", 1, 50m, 50m, "LIM", traslado, traslado ? Guid.NewGuid() : null, llegada);

    private static DatosDeLineaNueva Digital(string titulo = "Digital") => new(Guid.NewGuid(), Guid.NewGuid(), true, titulo, null, 1, 20m, 20m, null, false, null, null);

    private static PedidoDeDominio Crear(TipoDeEntregaDelPedido tipo, Guid? cuenta = null, string sucursal = "LIM", DateTimeOffset? creado = null, params DatosDeLineaNueva[] lineas)
    {
        lineas = lineas.Length == 0 ? [Impresa("Libro")] : lineas;
        var contacto = tipo == TipoDeEntregaDelPedido.Domicilio ? ContactoDominio.Crear("Ana Pérez", "987654321", "Jr. Lima 123") : null;
        var envio = tipo == TipoDeEntregaDelPedido.Domicilio ? 12m : 0m;
        var datos = new DatosDePedidoNuevo(
            Guid.NewGuid(), NumeroDePedido.Generar(m => (int)(DateTimeOffset.UtcNow.Ticks % m)).Valor, cuenta ?? Cuenta, tipo, tipo == TipoDeEntregaDelPedido.Recojo ? sucursal : null, tipo == TipoDeEntregaDelPedido.Domicilio ? "ayacucho" : null, envio,
            lineas.Sum(l => l.Importe) + envio, contacto, new("t-1", Ahora), new("p-1", Ahora), lineas.Any(l => l.EsDigital) ? new("r-1", Ahora) : null, lineas, creado ?? Ahora);
        return PedidoDeDominio.Crear(datos).Pedido!;
    }

    // ---------- Listados ----------

    [Fact]
    [Trait("Criterio", "AC-PED-06")]
    public async Task Cada_rol_ve_solo_lo_que_le_corresponde_en_su_listado_y_ningun_listado_lleva_contacto()
    {
        var propio = Crear(TipoDeEntregaDelPedido.Domicilio, Cuenta);
        var ajeno = Crear(TipoDeEntregaDelPedido.Domicilio, Guid.NewGuid());
        var recojoAya = Crear(TipoDeEntregaDelPedido.Recojo, Guid.NewGuid(), "AYA");
        var recojoLim = Crear(TipoDeEntregaDelPedido.Recojo, Guid.NewGuid(), "LIM");

        async Task<IReadOnlyList<Guid>> Ids(ActorDePrueba actor, Func<ConsultasDePedidos, Task<Resultado<PaginaDePedidos>>> consulta)
        {
            var entorno = new Entorno(actor);
            foreach (var p in new[] { propio, ajeno, recojoAya, recojoLim })
            {
                entorno.Repositorio.Poner(p);
            }

            var resultado = await consulta(entorno.Consultas);
            Assert.True(resultado.EsExito, resultado.Error?.Clave);
            return [.. resultado.Valor.Pedidos.Select(r => r.PedidoId)];
        }

        var cliente = await Ids(ActorDePrueba.DeCliente(Cuenta), c => c.ListarDelCliente(new FiltroDePedidos(), default));
        var encargado = await Ids(ActorDePrueba.DeEncargadoDe(ResolucionDeSucursalFalsa.Ayacucho), c => c.ListarDeRecojo("AYA", new FiltroDePedidos(), default));
        var logistica = await Ids(ActorDePrueba.DePersonalDeLogistica(), c => c.ListarADomicilio(new FiltroDePedidos(), default));
        var administracion = await Ids(ActorDePrueba.DeAdministradorCentral(), c => c.ListarDeLaAdministracion(new FiltroDePedidos(), default));

        Assert.Equal([propio.Id], cliente);
        Assert.Equal([recojoAya.Id], encargado);
        Assert.Equal(new[] { propio.Id, ajeno.Id }.Order(), logistica.Order());
        Assert.Equal(4, administracion.Count);
        Assert.DoesNotContain(typeof(ResumenDePedido).GetProperties(), p => p.PropertyType == typeof(ContactoPublico));
    }

    [Fact]
    public async Task Logistica_no_ve_pedidos_solo_digitales_ni_el_detalle_con_contacto_se_abre_con_la_operacion_del_encargado_ni_una_ruta_sin_sucursal_pasa()
    {
        var digital = Crear(TipoDeEntregaDelPedido.Digital, null, "LIM", null, Digital());
        var logistica = new Entorno(ActorDePrueba.DePersonalDeLogistica());
        logistica.Repositorio.Poner(digital);
        var encargado = new Entorno(ActorDePrueba.DeEncargadoDe(ResolucionDeSucursalFalsa.Lima));

        var detalle = await logistica.Consultas.ObtenerParaElPersonal(OperacionDePedidos.GestionDeDespacho, digital.Id, default);
        var listado = await logistica.Consultas.ListarADomicilio(new FiltroDePedidos(), default);
        var sinRuta = await encargado.Consultas.ListarDeRecojo(null!, new FiltroDePedidos(), default);

        Assert.Equal("no-encontrado", detalle.Error!.Clave);
        Assert.Empty(listado.Valor.Pedidos);
        Assert.Equal("prohibido", sinRuta.Error!.Clave);
        Assert.Contains("fuera-de-sucursal", encargado.Denegaciones.Eventos.Select(e => e.Motivo));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => encargado.Consultas.ObtenerParaElPersonal(OperacionDePedidos.GestionDeRecojo, digital.Id, default));
    }

    [Fact]
    public async Task El_encargado_no_lista_otra_sucursal_y_un_cliente_no_lista_de_gestion()
    {
        var encargado = new Entorno(ActorDePrueba.DeEncargadoDe(ResolucionDeSucursalFalsa.Lima));
        var cliente = new Entorno(ActorDePrueba.DeCliente(Cuenta));

        var otraSucursal = await encargado.Consultas.ListarDeRecojo("AYA", new FiltroDePedidos(), default);
        var deGestion = await cliente.Consultas.ListarDeRecojo("LIM", new FiltroDePedidos(), default);

        Assert.Equal("prohibido", otraSucursal.Error!.Clave);
        Assert.Equal("prohibido", deGestion.Error!.Clave);
        Assert.Contains("fuera-de-sucursal", encargado.Denegaciones.Eventos.Select(e => e.Motivo));
        Assert.Contains("fuera-de-rol", cliente.Denegaciones.Eventos.Select(e => e.Motivo));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-12")]
    public async Task El_filtro_por_estado_muestra_solo_los_pedidos_en_ese_estado_y_el_tamano_se_acota_a_50()
    {
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());
        var enTransito = Crear(TipoDeEntregaDelPedido.Recojo, null, "LIM", null, Impresa("Con traslado", traslado: true), Digital());
        enTransito.RegistrarTraslado(enTransito.Lineas.Single(l => !l.EsDigital).Id, EstadoDeLineaImpresa.EnTransito, null, Ahora);
        var confirmado = Crear(TipoDeEntregaDelPedido.Recojo);
        foreach (var p in new[] { enTransito, confirmado })
        {
            entorno.Repositorio.Poner(p);
        }

        var filtrado = await entorno.Consultas.ListarDeLaAdministracion(new FiltroDePedidos(EstadoVisibleDelPedido.EnTransito), default);
        var grande = await entorno.Consultas.ListarDeLaAdministracion(new FiltroDePedidos(null, 0, 500), default);

        Assert.Equal([enTransito.Id], filtrado.Valor.Pedidos.Select(p => p.PedidoId));
        Assert.Equal(EstadoVisibleDelPedido.EnTransito, filtrado.Valor.Pedidos[0].Estado);
        Assert.Equal((1, 50, 2), (grande.Valor.Pagina, grande.Valor.Tamano, grande.Valor.Total));
    }

    [Fact]
    public async Task La_paginacion_devuelve_del_mas_reciente_al_mas_antiguo_con_el_total()
    {
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());
        var viejo = Crear(TipoDeEntregaDelPedido.Recojo, null, "LIM", Ahora.AddDays(-2));
        var medio = Crear(TipoDeEntregaDelPedido.Recojo, null, "LIM", Ahora.AddDays(-1));
        var nuevo = Crear(TipoDeEntregaDelPedido.Recojo, null, "LIM", Ahora);
        foreach (var p in new[] { viejo, medio, nuevo })
        {
            entorno.Repositorio.Poner(p);
        }

        var primera = await entorno.Consultas.ListarDeLaAdministracion(new FiltroDePedidos(null, 1, 2), default);
        var segunda = await entorno.Consultas.ListarDeLaAdministracion(new FiltroDePedidos(null, 2, 2), default);

        Assert.Equal([nuevo.Id, medio.Id], primera.Valor.Pedidos.Select(p => p.PedidoId));
        Assert.Equal([viejo.Id], segunda.Valor.Pedidos.Select(p => p.PedidoId));
        Assert.Equal(3, primera.Valor.Total);
    }

    [Fact]
    public async Task Un_exceso_de_consultas_se_rechaza_antes_de_tocar_el_repositorio()
    {
        var entorno = new Entorno(ActorDePrueba.DeCliente(Cuenta));
        var cliente = new ControlesDeGestionDePedidos(new LimitesQueExcedenConsultas(), new EstadoDeCuentaFalso(), entorno.Repositorio);
        var consultas = new ConsultasDePedidos(new AutorizadorDePedidos(ActorDePrueba.DeCliente(Cuenta), entorno.Denegaciones, new ResolucionDeSucursalFalsa()), cliente, entorno.Repositorio, entorno.Reembolsos, entorno.AuditoriaDeContacto);

        var resultado = await consultas.ListarDelCliente(new FiltroDePedidos(), default);

        Assert.Equal("limite-excedido", resultado.Error!.Clave);
        Assert.DoesNotContain("listar", entorno.Repositorio.Llamadas);
    }

    private sealed class LimitesQueExcedenConsultas : ILimitadorDePedidos
    {
        public Task<ResultadoDeLimiteDePedidos> Consultar(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(new ResultadoDeLimiteDePedidos(DecisionDeLimiteDePedidos.Excedido, Ahora));

        public Task<ResultadoDeLimiteDePedidos> Gestionar(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLimiteDePedidos.Permitido);

        public Task<ResultadoDeLimiteDePedidos> LeerContacto(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLimiteDePedidos.Permitido);
    }

    // ---------- Detalle ----------

    [Fact]
    [Trait("Criterio", "AC-PED-03")]
    public async Task El_detalle_del_cliente_muestra_cada_linea_con_su_estado_y_el_traslado_en_curso_con_su_fecha_estimada()
    {
        var entorno = new Entorno(ActorDePrueba.DeCliente(Cuenta));
        var llegada = new DateOnly(2026, 11, 25);
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, null, "LIM", null, Impresa("Con traslado", traslado: true, llegada: llegada), Impresa("Sin fecha", traslado: true), Digital());
        var conTraslado = pedido.Lineas.Single(l => l.Titulo == "Con traslado");
        pedido.RegistrarTraslado(conTraslado.Id, EstadoDeLineaImpresa.EnTransito, null, Ahora);
        entorno.Repositorio.Poner(pedido);

        var detalle = (await entorno.Consultas.ObtenerParaElCliente(pedido.Id, default)).Valor;

        var linea = detalle.Lineas.Single(l => l.Titulo == "Con traslado");
        Assert.Equal((EstadoVisibleDeLaLinea.EnTransito, true, llegada), (linea.Estado, linea.Traslado!.EstaEnTransito, linea.Traslado.FechaEstimadaDeLlegada));
        var sinFecha = detalle.Lineas.Single(l => l.Titulo == "Sin fecha");
        Assert.Equal((EstadoVisibleDeLaLinea.Confirmado, false), (sinFecha.Estado, sinFecha.Traslado!.EstaEnTransito));
        Assert.Null(sinFecha.Traslado.FechaEstimadaDeLlegada);
        Assert.Equal(EstadoVisibleDeLaLinea.DisponibleEnLaBiblioteca, detalle.Lineas.Single(l => l.EsDigital).Estado);
        Assert.Equal(EstadoVisibleDelPedido.Confirmado, detalle.Resumen.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-34")]
    public async Task El_contacto_lo_ven_el_titular_logistica_y_la_administracion_y_el_encargado_no()
    {
        var pedidoDomicilio = Crear(TipoDeEntregaDelPedido.Domicilio, Cuenta);
        var titular = new Entorno(ActorDePrueba.DeCliente(Cuenta));
        var logistica = new Entorno(ActorDePrueba.DePersonalDeLogistica());
        var admin = new Entorno(ActorDePrueba.DeAdministradorCentral());
        foreach (var e in new[] { titular, logistica, admin })
        {
            e.Repositorio.Poner(pedidoDomicilio);
        }

        var delTitular = (await titular.Consultas.ObtenerParaElCliente(pedidoDomicilio.Id, default)).Valor;
        var deLogistica = (await logistica.Consultas.ObtenerParaElPersonal(OperacionDePedidos.GestionDeDespacho, pedidoDomicilio.Id, default)).Valor;
        var deAdmin = (await admin.Consultas.ObtenerParaElPersonal(OperacionDePedidos.Administracion, pedidoDomicilio.Id, default)).Valor;

        Assert.Equal("Ana Pérez", delTitular.Contacto!.Nombre);
        Assert.Equal(("987654321", "Jr. Lima 123"), (deLogistica.Contacto!.Telefono, deLogistica.Contacto.Direccion));
        Assert.NotNull(deAdmin.Contacto);
        Assert.False(deLogistica.ContactoOculto);
        // La lectura del personal se audita (pedido y rol); la del titular no.
        Assert.Equal([(pedidoDomicilio.Id, "personal-de-logistica")], logistica.AuditoriaDeContacto.Lecturas);
        Assert.Equal([(pedidoDomicilio.Id, "administrador-central")], admin.AuditoriaDeContacto.Lecturas);
        Assert.Empty(titular.AuditoriaDeContacto.Lecturas);
        Assert.DoesNotContain(typeof(DetalleDelPedidoDeSucursal).GetProperties(), p => p.PropertyType == typeof(ContactoPublico));
    }

    [Fact]
    public async Task El_encargado_recibe_el_detalle_de_recojo_sin_contacto_y_uno_a_domicilio_es_no_encontrado()
    {
        var entorno = new Entorno(ActorDePrueba.DeEncargadoDe(ResolucionDeSucursalFalsa.Lima));
        var recojo = Crear(TipoDeEntregaDelPedido.Recojo, null, "LIM");
        var domicilio = Crear(TipoDeEntregaDelPedido.Domicilio);
        entorno.Repositorio.Poner(recojo);
        entorno.Repositorio.Poner(domicilio);

        var propio = await entorno.Consultas.ObtenerParaLaSucursal("LIM", recojo.Id, default);
        var aDomicilio = await entorno.Consultas.ObtenerParaLaSucursal("LIM", domicilio.Id, default);

        Assert.True(propio.EsExito);
        Assert.Equal("no-encontrado", aDomicilio.Error!.Clave);
        Assert.Equal(1, entorno.Denegaciones.Eventos.Count(e => e.Motivo == "recurso-ajeno"));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-35")]
    public async Task Un_pedido_ajeno_y_uno_inexistente_dan_la_misma_respuesta_y_dejan_un_acceso_denegado_cada_uno()
    {
        var entorno = new Entorno(ActorDePrueba.DeCliente(Cuenta));
        var ajeno = Crear(TipoDeEntregaDelPedido.Recojo, Guid.NewGuid());
        entorno.Repositorio.Poner(ajeno);

        var deOtro = await entorno.Consultas.ObtenerParaElCliente(ajeno.Id, default);
        var inexistente = await entorno.Consultas.ObtenerParaElCliente(Guid.NewGuid(), default);

        Assert.Equal(deOtro.Error, inexistente.Error);
        Assert.Equal(2, entorno.Denegaciones.Eventos.Count(e => e.Motivo == "recurso-ajeno"));
    }

    [Fact]
    public async Task Si_no_se_puede_auditar_la_lectura_o_se_agoto_el_presupuesto_se_oculta_solo_el_contacto()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio);
        var sinAuditoria = new Entorno(ActorDePrueba.DeAdministradorCentral());
        sinAuditoria.Repositorio.Poner(pedido);
        sinAuditoria.AuditoriaDeContacto.Caida = true;
        var sinCupo = new Entorno(ActorDePrueba.DePersonalDeLogistica());
        sinCupo.Repositorio.Poner(pedido);
        sinCupo.Limites.Contacto = ResultadoDeLimiteDePedidos.NoDisponible;

        var a = (await sinAuditoria.Consultas.ObtenerParaElPersonal(OperacionDePedidos.Administracion, pedido.Id, default)).Valor;
        var b = (await sinCupo.Consultas.ObtenerParaElPersonal(OperacionDePedidos.GestionDeDespacho, pedido.Id, default)).Valor;

        Assert.All(new[] { a, b }, d =>
        {
            Assert.Null(d.Contacto);
            Assert.True(d.ContactoOculto);
            Assert.Single(d.Lineas);
            Assert.Equal(pedido.Total, d.Resumen.Total);
        });
        Assert.Empty(sinCupo.AuditoriaDeContacto.Lecturas);
    }

    [Fact]
    public void Los_estados_visibles_corresponden_uno_a_uno_con_los_del_dominio_y_el_tipo_de_entrega()
    {
        foreach (var estado in Enum.GetValues<EstadoDePedido>())
        {
            Assert.Equal(estado, ProyeccionesDePedidos.AEstadoDeDominio(ProyeccionesDePedidos.AEstadoVisible(estado)));
        }

        foreach (var estado in Enum.GetValues<EstadoDeLineaImpresa>())
        {
            Assert.Contains(estado.ToString(), Enum.GetNames<EstadoVisibleDeLaLinea>());
        }

        foreach (var tipo in Enum.GetValues<TipoDeEntregaDelPedido>())
        {
            Assert.Equal(tipo.ToString(), ProyeccionesDePedidos.Tipo(tipo).ToString());
        }
    }

    [Fact]
    public async Task Un_pedido_de_recojo_sin_contacto_no_audita_ninguna_lectura_de_contacto_y_un_anonimizado_tampoco_lo_muestra()
    {
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());
        var recojo = Crear(TipoDeEntregaDelPedido.Recojo);
        var anonimizado = Crear(TipoDeEntregaDelPedido.Domicilio);
        anonimizado.AnonimizarContacto(Ahora);
        entorno.Repositorio.Poner(recojo);
        entorno.Repositorio.Poner(anonimizado);

        var a = (await entorno.Consultas.ObtenerParaElPersonal(OperacionDePedidos.Administracion, recojo.Id, default)).Valor;
        var b = (await entorno.Consultas.ObtenerParaElPersonal(OperacionDePedidos.Administracion, anonimizado.Id, default)).Valor;

        Assert.All(new[] { a, b }, d => Assert.False(d.ContactoOculto));
        Assert.All(new[] { a, b }, d => Assert.Null(d.Contacto));
        Assert.Empty(entorno.AuditoriaDeContacto.Lecturas);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-16")]
    public async Task El_detalle_dice_si_se_puede_cancelar_y_la_alternativa_cuando_no()
    {
        var entorno = new Entorno(ActorDePrueba.DeCliente(Cuenta));
        var cancelable = Crear(TipoDeEntregaDelPedido.Recojo);
        var conDigital = Crear(TipoDeEntregaDelPedido.Recojo, null, "LIM", null, Impresa("Impreso"), Digital());
        var enPreparacion = Crear(TipoDeEntregaDelPedido.Recojo);
        enPreparacion.PrepararLineas([enPreparacion.Lineas[0].Id], Ahora);
        foreach (var p in new[] { cancelable, conDigital, enPreparacion })
        {
            entorno.Repositorio.Poner(p);
        }

        var a = (await entorno.Consultas.ObtenerParaElCliente(cancelable.Id, default)).Valor;
        var b = (await entorno.Consultas.ObtenerParaElCliente(conDigital.Id, default)).Valor;
        var c = (await entorno.Consultas.ObtenerParaElCliente(enPreparacion.Id, default)).Valor;

        Assert.Equal((true, AlternativaDeCancelacion.Ninguna), (a.PuedeCancelar, a.Alternativa));
        Assert.Equal((false, AlternativaDeCancelacion.PoliticaDeReembolsoDigital), (b.PuedeCancelar, b.Alternativa));
        Assert.Equal((false, AlternativaDeCancelacion.Devolucion), (c.PuedeCancelar, c.Alternativa));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-19")]
    public async Task Un_pedido_anulado_con_el_reembolso_pendiente_lo_muestra_y_si_Pagos_no_responde_no_lo_muestra()
    {
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo);
        pedido.AplicarAnulacion(pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.PagoDuplicado).Plan!, Ahora);
        entorno.Repositorio.Poner(pedido);
        entorno.Reembolsos.Pendientes[$"anul:{pedido.Id:D}"] = new ReembolsoPendienteVista(Guid.NewGuid(), OrigenDelReembolso.Anulacion, 50m, EstadoDelReembolso.Pendiente, MotivoPendienteDelReembolso.SinRespuesta, Ahora, 1);

        var con = (await entorno.Consultas.ObtenerParaElPersonal(OperacionDePedidos.Administracion, pedido.Id, default)).Valor;
        entorno.Reembolsos.Falla = true;
        var caido = (await entorno.Consultas.ObtenerParaElPersonal(OperacionDePedidos.Administracion, pedido.Id, default)).Valor;

        Assert.Equal(50m, con.ReembolsoPendiente!.Monto);
        Assert.Equal(EstadoVisibleDelPedido.Anulado, con.Resumen.Estado);
        Assert.Null(caido.ReembolsoPendiente);
    }
}
