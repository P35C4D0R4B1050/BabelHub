using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>Aprobar y rechazar la devolución de un impreso (T09; DEV-10, DEV-11, DEV-12, DEV-13, DEV-17; AC-DEV-10, AC-DEV-11, AC-DEV-12, AC-DEV-17).</summary>
public sealed class AprobarYRechazarDevolucionTests
{
    private static readonly DateTimeOffset Ahora = EntornoDeDevoluciones.AhoraInicial;

    private static readonly Guid Sucursal = Guid.Parse("a1000000-0000-0000-0000-000000000001");

    /// <summary>Una devolución de impresos solicitada por un cliente, vista por el actor dado (el almacén es el mismo).</summary>
    private static async Task<(EntornoDeDevoluciones Vista, Guid DevolucionId, Guid LineaId)> ConSolicitud(ActorDePrueba actor, string sucursal = "lima-central")
    {
        var cliente = new EntornoDeDevoluciones();
        var linea = EntornoDeDevoluciones.Entregada(Ahora.AddDays(-2), 3);
        var pedido = cliente.SembrarPedido(linea);
        var solicitada = await cliente.SolicitarImpreso.Ejecutar(new SolicitarDevolucionDeImpresoEntrada(pedido.PedidoId, [new LineaPedidaParaDevolver(linea.LineaId, 2)], new TextoLibre("Defecto"), sucursal));
        Assert.True(solicitada.EsExito);
        var vista = new EntornoDeDevoluciones(actor).ConLosDatosDe(cliente);
        return (vista, solicitada.Valor.DevolucionId, linea.LineaId);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-10")]
    public async Task El_encargado_de_la_sucursal_receptora_aprueba_la_devolucion_completa_con_su_fecha_responsable_y_auditoria()
    {
        var (entorno, id, _) = await ConSolicitud(ActorDePrueba.DeEncargadoDe(Sucursal));
        entorno.Sucursales.Codigos[Sucursal] = "lima-central";

        var resultado = await entorno.Gestion.Aprobar(id);

        Assert.True(resultado.EsExito);
        Assert.All(resultado.Valor.Lineas, l => Assert.Equal(EstadoDeLaLineaDeDevolucion.Aprobada, l.Estado));
        var cambio = entorno.Repositorio.Todas.Single().Lineas[0].Cambios[1];
        Assert.Equal(ResponsableDelCambio.Personal(entorno.Actor.Actual!.UsuarioId!.Value), cambio.Responsable);
        Assert.Equal(Ahora, cambio.Instante);
        Assert.Equal(["DevolucionSolicitada", "EstadoDeDevolucionCambiado"], entorno.Auditoria.Solicitudes.Select(s => s.Accion.ToString()));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-11")]
    public async Task Rechazar_devuelve_el_cupo_y_permite_volver_a_solicitar_la_linea()
    {
        var (entorno, id, linea) = await ConSolicitud(ActorDePrueba.DeAdministradorCentral());

        var resultado = await entorno.Gestion.Rechazar(id);

        Assert.True(resultado.EsExito);
        Assert.All(resultado.Valor.Lineas, l => Assert.Equal(EstadoDeLaLineaDeDevolucion.Rechazada, l.Estado));
        Assert.Equal(0, entorno.Repositorio.Cupos[linea].CantidadComprometida);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-17")]
    public async Task Repetir_aprobar_o_rechazar_es_estado_invalido_y_no_cambia_nada()
    {
        var (entorno, id, linea) = await ConSolicitud(ActorDePrueba.DeAdministradorCentral());
        await entorno.Gestion.Aprobar(id);
        var auditorias = entorno.Auditoria.Solicitudes.Count;

        var aprobarOtraVez = await entorno.Gestion.Aprobar(id);
        var rechazarAprobada = await entorno.Gestion.Rechazar(id);

        Assert.Equal("estado-invalido", aprobarOtraVez.Error!.Clave);
        Assert.Equal(CategoriaDeError.Conflicto, rechazarAprobada.Error!.Categoria);
        Assert.Equal(auditorias, entorno.Auditoria.Solicitudes.Count);
        Assert.Equal(2, entorno.Repositorio.Cupos[linea].CantidadComprometida);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public async Task Un_encargado_de_otra_sucursal_recibe_no_encontrado_con_un_evento_y_no_cambia_nada()
    {
        var (entorno, id, _) = await ConSolicitud(ActorDePrueba.DeEncargadoDe(Sucursal), sucursal: "arequipa");
        entorno.Sucursales.Codigos[Sucursal] = "lima-central";

        var resultado = await entorno.Gestion.Aprobar(id);

        Assert.Equal(CategoriaDeError.NoEncontrado, resultado.Error!.Categoria);
        Assert.Equal("fuera-de-sucursal", Assert.Single(entorno.Denegaciones.Eventos).Motivo);
        Assert.Equal(EstadoDeLaLineaDeDevolucion.Solicitada, (await entorno.Repositorio.Obtener(id, false))!.Lineas[0].Estado == EstadoDeLineaDeDevolucion.Solicitada ? EstadoDeLaLineaDeDevolucion.Solicitada : EstadoDeLaLineaDeDevolucion.Aprobada);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public async Task Una_devolucion_inexistente_responde_lo_mismo_que_una_ajena()
    {
        var (entorno, id, _) = await ConSolicitud(ActorDePrueba.DeEncargadoDe(Sucursal), sucursal: "arequipa");
        entorno.Sucursales.Codigos[Sucursal] = "lima-central";

        var ajena = await entorno.Gestion.Aprobar(id);
        var inexistente = await entorno.Gestion.Aprobar(Guid.NewGuid());

        Assert.Equal(ajena.Error!.Categoria, inexistente.Error!.Categoria);
        Assert.Equal(ajena.Error.Clave, inexistente.Error.Clave);
        Assert.Equal(2, entorno.Denegaciones.Eventos.Count);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-12")]
    [InlineData("cliente")]
    [InlineData("logistica")]
    [InlineData("anonimo")]
    public async Task Cliente_logistica_y_anonimo_no_aprueban_ni_rechazan_y_no_se_lee_nada(string quien)
    {
        var actor = quien switch
        {
            "cliente" => ActorDePrueba.DeCliente(),
            "logistica" => ActorDePrueba.DePersonalDeLogistica(),
            _ => ActorDePrueba.Anonimo(),
        };
        var (entorno, id, _) = await ConSolicitud(actor);

        var resultado = await entorno.Gestion.Aprobar(id);

        Assert.True(resultado.Error!.Categoria is CategoriaDeError.Prohibido or CategoriaDeError.NoAutenticado);
        Assert.Single(entorno.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public async Task Un_reporte_o_un_digital_no_se_aprueba_por_esta_via_ni_siquiera_la_administracion()
    {
        var cliente = new EntornoDeDevoluciones();
        var linea = EntornoDeDevoluciones.Digital();
        var pedido = new PedidoParaDevolucionesParaPruebas(cliente, linea).Pedido;
        cliente.Accesos.Sembrar(pedido.CuentaId, pedido.PedidoId, linea.EdicionId, enlaceEntregado: true);
        await cliente.Reportar.Ejecutar(new ReportarArchivoDefectuosoEntrada(pedido.PedidoId, linea.LineaId, new TextoLibre("x"), new TextoLibre("y")));
        var admin = new EntornoDeDevoluciones(ActorDePrueba.DeAdministradorCentral()).ConLosDatosDe(cliente);

        var resultado = await admin.Gestion.Aprobar(cliente.Repositorio.Todas.Single().Id);

        Assert.Equal("estado-invalido", resultado.Error!.Clave);
        Assert.Empty(admin.Reembolsos.Registrados);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-10")]
    public async Task Con_la_auditoria_caida_el_cambio_no_se_guarda_y_con_el_almacen_caido_es_servicio_no_disponible()
    {
        var (sinAuditoria, idA, _) = await ConSolicitud(ActorDePrueba.DeAdministradorCentral());
        sinAuditoria.Auditoria.Caida = true;
        var (sinAlmacen, idB, _) = await ConSolicitud(ActorDePrueba.DeAdministradorCentral());
        sinAlmacen.Repositorio.NoDisponible = true;

        var excepcion = await Record.ExceptionAsync(() => sinAuditoria.Gestion.Aprobar(idA));
        var caido = await sinAlmacen.Gestion.Aprobar(idB);

        Assert.NotNull(excepcion);
        Assert.Equal(EstadoDeLineaDeDevolucion.Solicitada, (await sinAuditoria.Repositorio.Obtener(idA, false))!.Lineas[0].Estado);
        Assert.Equal(CategoriaDeError.NoDisponible, caido.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-10")]
    public async Task El_limite_de_operaciones_se_aplica_despues_del_rol_y_su_exceso_es_el_429_uniforme()
    {
        var (entorno, id, _) = await ConSolicitud(ActorDePrueba.DeAdministradorCentral());
        entorno.Limites.Maximos["operaciones-de-devoluciones-por-usuario"] = 0;

        var resultado = await entorno.Gestion.Aprobar(id);

        Assert.Equal(CategoriaDeError.Limite, resultado.Error!.Categoria);
        Assert.Equal(EstadoDeLineaDeDevolucion.Solicitada, (await entorno.Repositorio.Obtener(id, false))!.Lineas[0].Estado);
    }
}

/// <summary>Siembra un pedido digital de la cuenta de un entorno (ayuda de las pruebas).</summary>
internal sealed class PedidoParaDevolucionesParaPruebas
{
    public PedidoParaDevolucionesParaPruebas(EntornoDeDevoluciones entorno, BabelHub.Ecommerce.Application.Pedidos.LineaDePedidoParaDevoluciones linea)
    {
        Pedido = new BabelHub.Ecommerce.Application.Pedidos.PedidoParaDevoluciones(Guid.NewGuid(), "PED-2026-0004", entorno.Actor.Actual!.UsuarioId!.Value, EntornoDeDevoluciones.AhoraInicial.AddDays(-3), [linea]);
        entorno.Pedidos.Sembrar(Pedido);
    }

    public BabelHub.Ecommerce.Application.Pedidos.PedidoParaDevoluciones Pedido { get; }
}
