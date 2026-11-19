using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>
/// La revisión horaria de plazos y la de devoluciones estancadas (T12; DEV-12, DEV-14, DEV-16; AC-DEV-13, AC-DEV-15): el rechazo por el plazo de recepción como «sistema», bajo el bloqueo de cada devolución con <c>SKIP LOCKED</c> y respetando el marcador de reingreso.
/// </summary>
public sealed class RevisionDePlazosDeDevolucionesTests
{
    private static readonly DateTimeOffset Ahora = EntornoDeDevoluciones.AhoraInicial;

    /// <summary>Una devolución de dos líneas aprobada en <see cref="Ahora"/> y vista por el «sistema» con el mismo almacén y el mismo reloj.</summary>
    private static async Task<(EntornoDeDevoluciones Sistema, Guid DevolucionId, Guid[] Lineas)> ConAprobada()
    {
        var cliente = new EntornoDeDevoluciones();
        var a = EntornoDeDevoluciones.Entregada(Ahora.AddDays(-2), 3, 50m);
        var b = EntornoDeDevoluciones.Entregada(Ahora.AddDays(-2), 2, 20m);
        var pedido = cliente.SembrarPedido(a, b);
        var solicitada = await cliente.SolicitarImpreso.Ejecutar(new SolicitarDevolucionDeImpresoEntrada(pedido.PedidoId, [new LineaPedidaParaDevolver(a.LineaId, 2), new LineaPedidaParaDevolver(b.LineaId, 1)], new TextoLibre("Defecto"), "lima-central"));
        Assert.True(solicitada.EsExito);
        var admin = new EntornoDeDevoluciones(ActorDePrueba.DeAdministradorCentral()).ConLosDatosDe(cliente);
        Assert.True((await admin.Gestion.Aprobar(solicitada.Valor.DevolucionId)).EsExito);
        return (new EntornoDeDevoluciones(ActorDePrueba.DelSistema()).ConLosDatosDe(admin), solicitada.Valor.DevolucionId, [a.LineaId, b.LineaId]);
    }

    private static Devolucion Guardada(EntornoDeDevoluciones entorno, Guid id) => entorno.Repositorio.Obtener(id, false).GetAwaiter().GetResult()!;

    // ---------- RechazarDevolucionesVencidas ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-13")]
    public async Task La_aprobada_hace_mas_de_10_dias_sin_marcador_pasa_a_rechazada_como_sistema_con_el_cupo_devuelto_y_sin_reembolso()
    {
        var (sistema, id, lineas) = await ConAprobada();
        sistema.Reloj.Ahora = Ahora.AddDays(10).AddMinutes(1);

        var resultado = await sistema.Vencidas.Ejecutar(50);

        Assert.Equal(1, resultado.Valor);
        var devolucion = Guardada(sistema, id);
        Assert.All(devolucion.Lineas, l =>
        {
            Assert.Equal(EstadoDeLineaDeDevolucion.Rechazada, l.Estado);
            Assert.Equal(ResponsableDelCambio.Sistema, l.Cambios[^1].Responsable);
            Assert.Equal(Ahora.AddDays(10).AddMinutes(1), l.Cambios[^1].Instante);
        });
        Assert.All(lineas, l => Assert.Equal(0, sistema.Repositorio.Cupos[l].CantidadComprometida));
        Assert.Empty(sistema.Reembolsos.Registrados);
        Assert.Empty(sistema.Inventario.Reingresos);
        var auditoria = sistema.Auditoria.Solicitudes.Last();
        Assert.Equal("EstadoDeDevolucionCambiado", auditoria.Accion.ToString());
        Assert.Equal("aprobada", auditoria.Detalle["estadoAnterior"]);
        Assert.Equal("rechazada", auditoria.Detalle["estadoNuevo"]);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-13")]
    public async Task Diez_dias_exactos_no_vencen_y_el_cupo_de_la_linea_rechazada_queda_libre()
    {
        var (sistema, id, lineas) = await ConAprobada();
        sistema.Reloj.Ahora = Ahora.AddDays(10);

        var enElBorde = await sistema.Vencidas.Ejecutar(50);
        sistema.Reloj.Ahora = Ahora.AddDays(10).AddSeconds(1);
        var pasado = await sistema.Vencidas.Ejecutar(50);

        Assert.Equal(0, enElBorde.Valor);
        Assert.Equal(1, pasado.Valor);
        Assert.Equal(EstadoDeLineaDeDevolucion.Rechazada, Guardada(sistema, id).Lineas[0].Estado);
        Assert.Equal(0, sistema.Repositorio.Cupos[lineas[0]].CantidadComprometida);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-15")]
    public async Task Con_el_marcador_de_reingreso_la_revision_respeta_la_recepcion_en_curso_aunque_hayan_pasado_los_10_dias()
    {
        var (sistema, id, _) = await ConAprobada();
        var devolucion = Guardada(sistema, id);
        devolucion.IniciarReingreso(Ahora.AddDays(9));
        await sistema.Repositorio.Guardar(devolucion);
        sistema.Reloj.Ahora = Ahora.AddDays(11);

        var resultado = await sistema.Vencidas.Ejecutar(50);

        Assert.Equal(0, resultado.Valor);
        Assert.All(Guardada(sistema, id).Lineas, l => Assert.Equal(EstadoDeLineaDeDevolucion.Aprobada, l.Estado));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-15")]
    public async Task Una_devolucion_que_otra_transaccion_tiene_bloqueada_se_salta_y_se_rechaza_en_la_revision_siguiente()
    {
        var (sistema, id, _) = await ConAprobada();
        sistema.Reloj.Ahora = Ahora.AddDays(11);
        sistema.Repositorio.Bloqueadas.Add(id);

        var bloqueada = await sistema.Vencidas.Ejecutar(50);
        sistema.Repositorio.Bloqueadas.Clear();
        var liberada = await sistema.Vencidas.Ejecutar(50);

        Assert.Equal(0, bloqueada.Valor);
        Assert.Equal(1, liberada.Valor);
        Assert.All(Guardada(sistema, id).Lineas, l => Assert.Equal(EstadoDeLineaDeDevolucion.Rechazada, l.Estado));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-13")]
    public async Task Repetir_la_revision_no_cambia_nada_ni_audita_otra_vez()
    {
        var (sistema, id, _) = await ConAprobada();
        sistema.Reloj.Ahora = Ahora.AddDays(11);
        await sistema.Vencidas.Ejecutar(50);
        var auditorias = sistema.Auditoria.Solicitudes.Count;

        var repetida = await sistema.Vencidas.Ejecutar(50);

        Assert.Equal(0, repetida.Valor);
        Assert.Equal(auditorias, sistema.Auditoria.Solicitudes.Count);
        Assert.Equal(EstadoDeLineaDeDevolucion.Rechazada, Guardada(sistema, id).Lineas[0].Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-13")]
    public async Task Con_la_auditoria_caida_no_se_rechaza_nada_la_revision_no_falla_y_la_siguiente_si_lo_hace()
    {
        var (sistema, id, lineas) = await ConAprobada();
        sistema.Reloj.Ahora = Ahora.AddDays(11);
        sistema.Auditoria.Caida = true;

        var sinAuditoria = await sistema.Vencidas.Ejecutar(50);
        sistema.Auditoria.Caida = false;

        Assert.True(sinAuditoria.EsExito);
        Assert.Equal(0, sinAuditoria.Valor);
        Assert.All(Guardada(sistema, id).Lineas, l => Assert.Equal(EstadoDeLineaDeDevolucion.Aprobada, l.Estado));
        Assert.Equal(2, sistema.Repositorio.Cupos[lineas[0]].CantidadComprometida);
        Assert.Equal(1, (await sistema.Vencidas.Ejecutar(50)).Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-13")]
    public async Task Una_devolucion_que_falla_al_bloquearse_no_detiene_a_las_demas()
    {
        var cliente = new EntornoDeDevoluciones();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var linea = EntornoDeDevoluciones.Entregada(Ahora.AddDays(-2), 3, 50m);
            var pedido = cliente.SembrarPedido(linea);
            var solicitada = await cliente.SolicitarImpreso.Ejecutar(new SolicitarDevolucionDeImpresoEntrada(pedido.PedidoId, [new LineaPedidaParaDevolver(linea.LineaId, 1)], new TextoLibre("x"), "lima-central"));
            ids.Add(solicitada.Valor.DevolucionId);
        }

        var admin = new EntornoDeDevoluciones(ActorDePrueba.DeAdministradorCentral()).ConLosDatosDe(cliente);
        foreach (var id in ids)
        {
            await admin.Gestion.Aprobar(id);
        }

        var sistema = new EntornoDeDevoluciones(ActorDePrueba.DelSistema()).ConLosDatosDe(cliente);
        sistema.Reloj.Ahora = Ahora.AddDays(11);
        sistema.Repositorio.FallanAlBloquear.Add(ids.OrderBy(i => i).First());

        var resultado = await sistema.Vencidas.Ejecutar(50);

        Assert.Equal(2, resultado.Valor);
        Assert.Equal(1, ids.Count(i => Guardada(sistema, i).Lineas[0].Estado == EstadoDeLineaDeDevolucion.Aprobada));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-13")]
    public async Task Con_el_almacen_caido_responde_servicio_no_disponible()
    {
        var (sistema, _, _) = await ConAprobada();
        sistema.Repositorio.NoDisponible = true;

        var resultado = await sistema.Vencidas.Ejecutar(50);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-13")]
    [InlineData("cliente")]
    [InlineData("encargado")]
    [InlineData("administracion")]
    [InlineData("integracion")]
    [InlineData("anonimo")]
    public async Task Solo_el_sistema_revisa_y_cada_intento_de_otro_actor_deja_un_evento(string quien)
    {
        var (sistema, id, _) = await ConAprobada();
        sistema.Reloj.Ahora = Ahora.AddDays(11);
        var actor = quien switch
        {
            "cliente" => ActorDePrueba.DeCliente(),
            "encargado" => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
            "administracion" => ActorDePrueba.DeAdministradorCentral(),
            "integracion" => ActorDePrueba.DeIntegracion("n8n"),
            _ => ActorDePrueba.Anonimo(),
        };
        var otro = new EntornoDeDevoluciones(actor).ConLosDatosDe(sistema);

        var resultado = await otro.Vencidas.Ejecutar(50);

        Assert.True(resultado.Error!.Categoria is CategoriaDeError.Prohibido or CategoriaDeError.NoAutenticado);
        Assert.Single(otro.Denegaciones.Eventos);
        Assert.All(Guardada(otro, id).Lineas, l => Assert.Equal(EstadoDeLineaDeDevolucion.Aprobada, l.Estado));
    }

    // ---------- RevisarDevolucionesEstancadas ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-15")]
    public async Task Un_reingreso_iniciado_hace_mas_de_24_horas_sin_completarse_publica_una_alerta_tecnica_sin_datos_de_la_devolucion()
    {
        var (sistema, id, _) = await ConAprobada();
        var devolucion = Guardada(sistema, id);
        devolucion.IniciarReingreso(Ahora);
        await sistema.Repositorio.Guardar(devolucion);
        sistema.Reloj.Ahora = Ahora.AddHours(24).AddMinutes(1);

        var resultado = await sistema.Estancadas.Ejecutar();

        Assert.Equal(1, resultado.Valor);
        var alerta = Assert.Single(sistema.Alertas.Alertas);
        Assert.DoesNotContain(id.ToString(), alerta.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-15")]
    public async Task Veinticuatro_horas_exactas_una_devolucion_ya_recibida_o_sin_marcador_no_alertan()
    {
        var (sistema, id, _) = await ConAprobada();
        var devolucion = Guardada(sistema, id);
        devolucion.IniciarReingreso(Ahora);
        await sistema.Repositorio.Guardar(devolucion);
        sistema.Reloj.Ahora = Ahora.AddHours(24);

        var enElBorde = await sistema.Estancadas.Ejecutar();
        devolucion.ConfirmarReingreso(Ahora);
        devolucion.Recibir(Ahora, ResponsableDelCambio.Personal(Guid.NewGuid()));
        await sistema.Repositorio.Guardar(devolucion);
        sistema.Reloj.Ahora = Ahora.AddDays(3);
        var recibida = await sistema.Estancadas.Ejecutar();

        Assert.Equal(0, enElBorde.Valor);
        Assert.Equal(0, recibida.Valor);
        Assert.Empty(sistema.Alertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-15")]
    public async Task Una_alerta_que_no_sale_no_interrumpe_la_revision_y_solo_el_sistema_revisa()
    {
        var (sistema, id, _) = await ConAprobada();
        var devolucion = Guardada(sistema, id);
        devolucion.IniciarReingreso(Ahora);
        await sistema.Repositorio.Guardar(devolucion);
        sistema.Reloj.Ahora = Ahora.AddDays(2);
        sistema.Alertas.Falla = new InvalidOperationException("sin canal");
        var comoAdmin = new EntornoDeDevoluciones(ActorDePrueba.DeAdministradorCentral()).ConLosDatosDe(sistema);

        var resultado = await sistema.Estancadas.Ejecutar();
        var prohibido = await comoAdmin.Estancadas.Ejecutar();

        Assert.Equal(1, resultado.Valor);
        Assert.Equal(CategoriaDeError.Prohibido, prohibido.Error!.Categoria);
    }
}
