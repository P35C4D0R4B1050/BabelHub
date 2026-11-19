using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.UnitTests.Auditoria;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;
using PedidoDeDominio = BabelHub.Ecommerce.Domain.Pedidos.Pedido;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T24 a T26 (PED-33 a PED-35, PED-37, PED-39; AC-PED-27 a 29, 31, 33): la anonimización del contacto y su liberación en Pagos.</summary>
public sealed class PrivacidadDeLosContactosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static int _contador;

    private static string Numero() => "ABCD23456" + "ABCDEFGHJKLMNPQRSTUVWXYZ"[Interlocked.Increment(ref _contador) % 24];

    private static PedidoDeDominio Domicilio(Guid cuenta, DateTimeOffset? anuladoEn = null)
    {
        var linea = new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Libro", null, 1, 50m, 50m, "LIM", false, null, null);
        var contacto = BabelHub.Ecommerce.Domain.Pedidos.ContactoDeEntrega.Crear("Ana Pérez", "987654321", "Jr. Lima 123");
        var pedido = PedidoDeDominio.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), Numero(), cuenta, TipoDeEntregaDelPedido.Domicilio, null, "ayacucho", 12m, 62m, contacto, new("t-1", Ahora), new("p-1", Ahora), null, [linea], Ahora)).Pedido!;
        if (anuladoEn is { } fecha)
        {
            pedido.AplicarAnulacion(pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro).Plan!, fecha);
        }

        return pedido;
    }

    private static PedidoDeDominio Recojo(Guid cuenta)
    {
        var linea = new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Libro", null, 1, 50m, 50m, "LIM", false, null, null);
        return PedidoDeDominio.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), Numero(), cuenta, TipoDeEntregaDelPedido.Recojo, "LIM", null, 0m, 50m, null, new("t-1", Ahora), new("p-1", Ahora), null, [linea], Ahora)).Pedido!;
    }

    // ---------- T24: la cancelación de la cuenta ----------

    [Fact]
    [Trait("Criterio", "AC-PED-28")]
    public async Task La_cancelacion_anonimiza_el_contacto_de_los_pedidos_de_la_cuenta_y_conserva_lo_demas_sin_tocar_al_ajeno()
    {
        var repositorio = new RepositorioDePedidosFalso();
        var auditoria = new AuditoriaDePedidosEspia();
        var entregado = Domicilio(Cuenta, Ahora - TimeSpan.FromDays(30));
        var deRecojo = Recojo(Cuenta);
        var ajeno = Domicilio(Guid.NewGuid());
        foreach (var pedido in new[] { entregado, deRecojo, ajeno })
        {
            repositorio.Poner(pedido);
        }

        await new AnonimizarContactoDeUnaCuenta(repositorio, auditoria, new RelojFijo(Ahora)).Aplicar(Cuenta, Guid.NewGuid());

        Assert.Null(entregado.Contacto);
        Assert.NotNull(ajeno.Contacto);
        Assert.Equal((Cuenta, 62m, 1), (entregado.CuentaId, entregado.Total, entregado.Lineas.Count));
        var registro = Assert.Single(auditoria.Solicitudes);
        Assert.Equal((AccionesAuditables.ContactoDePedidoAnonimizado, "1", "cancelacion-de-la-cuenta"), (registro.Accion, registro.Detalle["cantidad"], registro.Detalle["causa"]));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-31")]
    public async Task Si_un_pedido_no_se_puede_anonimizar_lanza_para_que_no_se_ejecute_la_cancelacion_y_al_reintentar_el_ya_anonimizado_no_falla()
    {
        var repositorio = new RepositorioDePedidosFalso();
        var ya = Domicilio(Cuenta);
        ya.AnonimizarContacto(Ahora);
        var fallido = Domicilio(Cuenta);
        var otro = Domicilio(Cuenta);
        foreach (var pedido in new[] { ya, fallido, otro })
        {
            repositorio.Poner(pedido);
        }

        repositorio.ActualizacionesQueFallan.Add(fallido.Id);
        var caso = new AnonimizarContactoDeUnaCuenta(repositorio, new AuditoriaDePedidosEspia(), new RelojFijo(Ahora));

        await Assert.ThrowsAsync<InvalidOperationException>(() => caso.Aplicar(Cuenta, Guid.NewGuid()));

        // Identidad revierte su transacción; se reintenta cuando el pedido ya se puede actualizar.
        repositorio.ActualizacionesQueFallan.Clear();
        await caso.Aplicar(Cuenta, Guid.NewGuid());
        await caso.Aplicar(Cuenta, Guid.NewGuid());

        Assert.All(new[] { ya, fallido, otro }, p => Assert.Null(p.Contacto));
    }

    // ---------- T25: la depuración por retención ----------

    private sealed class EntornoDeDepuracion(ActorDePrueba? actor = null)
    {
        public RepositorioDePedidosFalso Repositorio { get; } = new();

        public AuditoriaDePedidosEspia Auditoria { get; } = new();

        public AlertasTecnicasEspia Alertas { get; } = new();

        public UnidadDeTrabajoEspia Unidad { get; } = new();

        public RelojFijo Reloj { get; } = new(Ahora);

        public AnonimizarContactosVencidos Caso => new(
            new AutorizadorDePedidos(actor ?? ActorDePrueba.DelSistema(), new DenegacionesDelCatalogoEspia(), new ResolucionDeSucursalFalsa()),
            Repositorio,
            Unidad,
            Auditoria,
            Alertas,
            Reloj);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-29")]
    public async Task La_depuracion_anonimiza_los_de_2_anos_y_1_dia_incluido_el_iniciador_no_toca_los_de_2_anos_menos_1_dia_ni_el_que_sigue_en_curso_y_repetirla_no_cambia_nada()
    {
        var entorno = new EntornoDeDepuracion();
        var vencido = Domicilio(Cuenta, Ahora.AddYears(-2).AddDays(-1));
        var vigente = Domicilio(Cuenta, Ahora.AddYears(-2).AddDays(1));
        var enCurso = Domicilio(Cuenta);
        foreach (var pedido in new[] { vencido, vigente, enCurso })
        {
            entorno.Repositorio.Poner(pedido);
        }

        var iniciador = new IniciadorDeOperacion(Guid.NewGuid(), "administrador-central", null);
        var fila = Anulacion.Iniciar(Guid.NewGuid(), vencido.Id, vencido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro, new HashSet<Guid>()).Plan ?? Domicilio(Cuenta).PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro).Plan!, iniciador, Ahora);
        entorno.Repositorio.Anulaciones.Add(fila);

        var primera = await entorno.Caso.Depurar();
        var repetida = await entorno.Caso.Depurar();

        Assert.Equal(1, primera.Valor.Anonimizados);
        Assert.Null(vencido.Contacto);
        Assert.NotNull(vigente.Contacto);
        Assert.NotNull(enCurso.Contacto);
        Assert.True(fila.Iniciador.EstaAnonimizado);
        Assert.Equal(0, repetida.Valor.Anonimizados);
        Assert.Equal(("Ana Pérez", "ayacucho"), (vigente.Contacto!.Nombre, vigente.RegionDeEntrega));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-33")]
    public async Task Cada_corrida_deja_su_auditoria_con_el_conteo_tambien_en_cero_y_sin_poder_auditar_no_se_anonimiza_y_se_alerta_a_las_24_horas()
    {
        var entorno = new EntornoDeDepuracion();
        var vencido = Domicilio(Cuenta, Ahora.AddYears(-2).AddDays(-1));
        entorno.Repositorio.Poner(vencido);

        await entorno.Caso.Depurar();
        await entorno.Caso.Depurar();

        Assert.Equal(["1", "0"], entorno.Auditoria.Solicitudes.Select(s => s.Detalle["cantidad"]));
        Assert.All(entorno.Auditoria.Solicitudes, s => Assert.Equal(AccionesAuditables.PedidosDepurados, s.Accion));

        var sinAuditoria = new EntornoDeDepuracion();
        var pendiente = Domicilio(Cuenta, Ahora.AddYears(-3));
        sinAuditoria.Repositorio.Poner(pendiente);
        sinAuditoria.Auditoria.Caida = true;
        sinAuditoria.Repositorio.UltimaDepuracion = Ahora - TimeSpan.FromHours(23);

        var fallo = await sinAuditoria.Caso.Depurar();

        Assert.Equal("servicio-no-disponible", fallo.Error!.Clave);
        Assert.NotNull(pendiente.Contacto);
        Assert.Empty(sinAuditoria.Alertas.Alertas);

        sinAuditoria.Reloj.Ahora += TimeSpan.FromHours(2);
        await sinAuditoria.Caso.Depurar();

        Assert.Contains(sinAuditoria.Alertas.Alertas, a => a.Motivo == AnonimizarContactosVencidos.AlertaSinCorrida);
    }

    [Fact]
    public async Task Un_filtro_del_adaptador_mas_amplio_no_anonimiza_antes_de_tiempo_ni_infla_el_conteo()
    {
        var entorno = new EntornoDeDepuracion();
        var noVencido = Domicilio(Cuenta, Ahora.AddYears(-2).AddDays(1));
        entorno.Repositorio.Poner(noVencido);
        entorno.Repositorio.ForzadosParaDepurar.Add(noVencido.Id);
        var fila = Anulacion.Iniciar(Guid.NewGuid(), noVencido.Id, noVencido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro).Plan ?? Domicilio(Cuenta).PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro).Plan!, new IniciadorDeOperacion(Guid.NewGuid(), "administrador-central", null), Ahora);
        entorno.Repositorio.Anulaciones.Add(fila);

        var resultado = await entorno.Caso.Depurar();

        Assert.Equal(0, resultado.Valor.Anonimizados);
        Assert.NotNull(noVencido.Contacto);
        Assert.False(fila.Iniciador.EstaAnonimizado);
    }

    [Fact]
    public async Task Solo_el_sistema_depura()
    {
        foreach (var actor in new[] { ActorDePrueba.DeAdministradorCentral(), ActorDePrueba.DeCliente(Cuenta) })
        {
            var resultado = await new EntornoDeDepuracion(actor).Caso.Depurar();

            Assert.Equal("prohibido", resultado.Error!.Clave);
        }
    }

    // ---------- T26: la liberación del contacto en Pagos ----------

    private sealed class LiberacionDePagosFalsa : ILiberacionDeContactoDePago
    {
        public List<Guid> Pedidos { get; } = [];

        public bool Falla { get; set; }

        public Task Liberar(Guid pedidoId, CancellationToken cancelacion = default)
        {
            if (Falla)
            {
                throw new InvalidOperationException("Pagos no responde");
            }

            Pedidos.Add(pedidoId);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task La_liberacion_de_contactos_solo_la_corre_el_sistema_y_una_fila_de_un_pedido_que_no_existe_queda_pendiente_en_vez_de_borrarse()
    {
        var repositorio = new RepositorioDePedidosFalso();
        var pagos = new LiberacionDePagosFalsa();
        LiberarContactoDeIntenciones Armar(ActorDePrueba actor) => new(new AutorizadorDePedidos(actor, new DenegacionesDelCatalogoEspia(), new ResolucionDeSucursalFalsa()), repositorio, pagos, new AlertasTecnicasEspia(), new RelojFijo(Ahora));

        Assert.Equal("prohibido", (await Armar(ActorDePrueba.DeAdministradorCentral()).Liberar()).Error!.Clave);

        await repositorio.RegistrarLiberacionDeContacto(Guid.NewGuid(), Ahora, default);
        var resultado = await Armar(ActorDePrueba.DelSistema()).Liberar();

        Assert.Equal((0, 1), (resultado.Valor.Liberados, resultado.Valor.Pendientes));
        Assert.Single(repositorio.LiberacionesDeContacto);
        Assert.Empty(pagos.Pedidos);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-27")]
    public async Task Pide_a_Pagos_eliminar_el_contacto_de_la_intencion_solo_de_pedidos_con_contacto_y_si_Pagos_no_responde_repite_con_alerta_a_las_24_horas()
    {
        var repositorio = new RepositorioDePedidosFalso();
        var pagos = new LiberacionDePagosFalsa();
        var alertas = new AlertasTecnicasEspia();
        var reloj = new RelojFijo(Ahora);
        var caso = new LiberarContactoDeIntenciones(new AutorizadorDePedidos(ActorDePrueba.DelSistema(), new DenegacionesDelCatalogoEspia(), new ResolucionDeSucursalFalsa()), repositorio, pagos, alertas, reloj);
        var domicilio = Domicilio(Cuenta);
        var deRecojo = Recojo(Cuenta);
        repositorio.Poner(domicilio);
        repositorio.Poner(deRecojo);
        await repositorio.RegistrarLiberacionDeContacto(domicilio.Id, Ahora, default);
        await repositorio.RegistrarLiberacionDeContacto(deRecojo.Id, Ahora, default);
        pagos.Falla = true;

        var primera = await caso.Liberar();

        Assert.Equal((1, 1), (primera.Valor.Liberados, primera.Valor.Pendientes));
        Assert.Equal(1, repositorio.IntentosDeContacto[domicilio.Id]);
        Assert.Empty(alertas.Alertas);

        // El de recojo no tiene contacto: se borró sin pedir nada. Pagos vuelve y la fila pendiente se libera.
        pagos.Falla = false;
        var segunda = await caso.Liberar();

        Assert.Equal((1, 0), (segunda.Valor.Liberados, segunda.Valor.Pendientes));
        Assert.Equal([domicilio.Id], pagos.Pedidos);
        Assert.Empty(repositorio.LiberacionesDeContacto);

        await repositorio.RegistrarLiberacionDeContacto(domicilio.Id, Ahora, default);
        pagos.Falla = true;
        reloj.Ahora += TimeSpan.FromHours(25);
        await caso.Liberar();

        Assert.Contains(alertas.Alertas, a => a.Motivo == LiberarContactoDeIntenciones.AlertaSinLiberar);
    }
}
