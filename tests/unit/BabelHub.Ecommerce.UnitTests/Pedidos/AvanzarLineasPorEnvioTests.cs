using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;
using PedidoDeDominio = BabelHub.Ecommerce.Domain.Pedidos.Pedido;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T17 (PED-20, PED-29; AC-PED-20, 21, 24): el progreso de los envíos hacia las líneas, idempotente por envío.</summary>
public sealed class AvanzarLineasPorEnvioTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Entrega = new(2026, 11, 21, 10, 30, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EnvioCusco = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid EnvioAyacucho = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private sealed class Entorno
    {
        public Entorno()
        {
            Pedido = Domicilio();
            Repositorio.Poner(Pedido);
        }

        public PedidoDeDominio Pedido { get; }

        public Guid Cusco => Pedido.Lineas.Single(l => l.CodigoDeFuente == "CUS").Id;

        public Guid Ayacucho => Pedido.Lineas.Single(l => l.CodigoDeFuente == "AYA").Id;

        public RepositorioDePedidosFalso Repositorio { get; } = new();

        public UnidadDeTrabajoEspia Unidad { get; } = new();

        public AuditoriaDePedidosEspia Auditoria { get; } = new();

        public NotificadorDePedidosEspia Notificador { get; } = new();

        public AvanzarLineasPorEnvio Caso => new(Repositorio, Unidad, Auditoria, Notificador, new RelojFijo(Ahora));
    }

    /// <summary>Un pedido a domicilio con una línea de Cusco y otra de Ayacucho, ambas listas y con su envío (AC-PED-20).</summary>
    private static PedidoDeDominio Domicilio()
    {
        var lineas = new[]
        {
            new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "De Cusco", null, 1, 50m, 50m, "CUS", false, null, null),
            new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "De Ayacucho", null, 1, 50m, 50m, "AYA", false, null, null),
        };
        var contacto = BabelHub.Ecommerce.Domain.Pedidos.ContactoDeEntrega.Crear("Ana", "987654321", "Jr. Lima 123");
        var pedido = PedidoDeDominio.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), "ABCD234567", Cuenta, TipoDeEntregaDelPedido.Domicilio, null, "ayacucho", 12m, 112m, contacto, new("t-1", Ahora), new("p-1", Ahora), null, lineas, Ahora)).Pedido!;
        var ids = pedido.Lineas.Select(l => l.Id).ToList();
        pedido.PrepararLineas(ids, Ahora);
        pedido.MarcarListas(ids, Ahora);
        pedido.AsignarEnvio([pedido.Lineas.Single(l => l.CodigoDeFuente == "CUS").Id], EnvioCusco);
        pedido.AsignarEnvio([pedido.Lineas.Single(l => l.CodigoDeFuente == "AYA").Id], EnvioAyacucho);
        return pedido;
    }

    [Fact]
    public async Task Un_envio_en_reparto_pasa_sus_lineas_a_despachadas_con_auditoria_del_estado_y_un_aviso_por_envio()
    {
        var entorno = new Entorno();

        var resultado = await entorno.Caso.EnvioEnReparto(new EnvioEnRepartoEntrada(entorno.Pedido.Id, EnvioCusco, [entorno.Cusco]), default);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.False(resultado.Valor.YaAplicado);
        Assert.Equal(EstadoDeLineaImpresa.Despachado, entorno.Pedido.Lineas.Single(l => l.Id == entorno.Cusco).EstadoImpreso);
        Assert.Equal(EstadoDeLineaImpresa.EnPreparacion, entorno.Pedido.Lineas.Single(l => l.Id == entorno.Ayacucho).EstadoImpreso);
        // El pedido sigue en el estado de su línea menos avanzada.
        Assert.Equal(EstadoDePedido.EnPreparacion, entorno.Pedido.Estado);
        Assert.Empty(entorno.Auditoria.Solicitudes);
        Assert.All(entorno.Notificador.Solicitudes, s => Assert.Equal("despachado", s.Campos["estado"]));
        Assert.Equal(2, entorno.Notificador.Solicitudes.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-24")]
    public async Task Un_envio_entregado_pasa_sus_lineas_a_entregadas_con_la_fecha_de_entrega_y_audita_cada_linea()
    {
        var entorno = new Entorno();
        await entorno.Caso.EnvioEnReparto(new EnvioEnRepartoEntrada(entorno.Pedido.Id, EnvioCusco, [entorno.Cusco]), default);
        entorno.Notificador.Solicitudes.Clear();

        var resultado = await entorno.Caso.EnvioEntregado(new EnvioEntregadoEntrada(entorno.Pedido.Id, EnvioCusco, [entorno.Cusco], Entrega), default);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        var linea = entorno.Pedido.Lineas.Single(l => l.Id == entorno.Cusco);
        Assert.Equal((EstadoDeLineaImpresa.Entregado, (DateTimeOffset?)Entrega), (linea.EstadoImpreso!.Value, linea.EntregadaEn));
        var auditoria = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.LineaDePedidoEntregada, auditoria.Accion);
        Assert.Equal(EnvioCusco.ToString("D"), auditoria.Detalle["envioId"]);
        Assert.All(entorno.Notificador.Solicitudes, s => Assert.Equal("entregado", s.Campos["estado"]));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-21")]
    public async Task Con_una_linea_anulada_el_pedido_queda_entregado_al_entregarse_la_otra_y_anulado_si_todas_lo_estan()
    {
        var entorno = new Entorno();
        var plan = entorno.Pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro, new HashSet<Guid> { entorno.Cusco }).Plan!;
        entorno.Pedido.AplicarAnulacion(plan, Ahora);
        Assert.Equal(EstadoDeLineaImpresa.Anulado, entorno.Pedido.Lineas.Single(l => l.Id == entorno.Ayacucho).EstadoImpreso);

        await entorno.Caso.EnvioEnReparto(new EnvioEnRepartoEntrada(entorno.Pedido.Id, EnvioCusco, [entorno.Cusco]), default);
        Assert.Equal(EstadoDePedido.Despachado, entorno.Pedido.Estado);
        var entregado = await entorno.Caso.EnvioEntregado(new EnvioEntregadoEntrada(entorno.Pedido.Id, EnvioCusco, [entorno.Cusco], Entrega), default);

        Assert.True(entregado.EsExito, entregado.Error?.Clave);
        Assert.Equal(EstadoDePedido.Entregado, entorno.Pedido.Estado);
        Assert.Equal(Entrega, entorno.Pedido.FechaEstadoFinal);
        Assert.Contains(entorno.Auditoria.Solicitudes, s => s.Accion == AccionesAuditables.EstadoDePedidoCambiado && s.Detalle["estadoNuevo"] == "entregado");
    }

    [Fact]
    public async Task Repetir_la_llamada_ya_aplicada_es_exito_sin_cambios_ni_auditoria_ni_avisos()
    {
        var entorno = new Entorno();
        var entrada = new EnvioEnRepartoEntrada(entorno.Pedido.Id, EnvioCusco, [entorno.Cusco]);
        await entorno.Caso.EnvioEnReparto(entrada, default);
        var avisos = entorno.Notificador.Solicitudes.Count;
        var actualizaciones = entorno.Repositorio.Llamadas.Count(l => l == "actualizar");

        var repetida = await entorno.Caso.EnvioEnReparto(entrada, default);
        var entregaPrevia = await entorno.Caso.EnvioEntregado(new EnvioEntregadoEntrada(entorno.Pedido.Id, EnvioCusco, [entorno.Cusco], Entrega), default);
        var entregaRepetida = await entorno.Caso.EnvioEntregado(new EnvioEntregadoEntrada(entorno.Pedido.Id, EnvioCusco, [entorno.Cusco], Entrega), default);
        var reparto = await entorno.Caso.EnvioEnReparto(entrada, default);

        Assert.True(repetida.EsExito);
        Assert.True(repetida.Valor.YaAplicado);
        Assert.False(entregaPrevia.Valor.YaAplicado);
        Assert.True(entregaRepetida.Valor.YaAplicado);
        // El reparto informado tarde, con la línea ya entregada, tampoco retrocede nada.
        Assert.True(reparto.Valor.YaAplicado);
        Assert.Equal(EstadoDeLineaImpresa.Entregado, entorno.Pedido.Lineas.Single(l => l.Id == entorno.Cusco).EstadoImpreso);
        Assert.Equal(avisos + 2, entorno.Notificador.Solicitudes.Count);
        Assert.Equal(actualizaciones + 1, entorno.Repositorio.Llamadas.Count(l => l == "actualizar"));
    }

    [Fact]
    public async Task La_entrega_sin_reparto_previo_es_estado_invalido_y_no_cambia_nada()
    {
        var entorno = new Entorno();

        var resultado = await entorno.Caso.EnvioEntregado(new EnvioEntregadoEntrada(entorno.Pedido.Id, EnvioCusco, [entorno.Cusco], Entrega), default);

        Assert.Equal((CategoriaDeError.Conflicto, "pedido-estado-invalido"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(EstadoDeLineaImpresa.EnPreparacion, entorno.Pedido.Lineas.Single(l => l.Id == entorno.Cusco).EstadoImpreso);
        Assert.Empty(entorno.Notificador.Solicitudes);
    }

    [Fact]
    public async Task Una_linea_anulada_se_omite_sin_error_y_si_no_queda_ninguna_vigente_responde_ya_aplicado()
    {
        var entorno = new Entorno();
        var plan = entorno.Pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro, new HashSet<Guid> { entorno.Cusco }).Plan!;
        entorno.Pedido.AplicarAnulacion(plan, Ahora);

        var resultado = await entorno.Caso.EnvioEnReparto(new EnvioEnRepartoEntrada(entorno.Pedido.Id, EnvioAyacucho, [entorno.Ayacucho]), default);

        Assert.True(resultado.EsExito);
        Assert.True(resultado.Valor.YaAplicado);
        Assert.Equal(EstadoDeLineaImpresa.Anulado, entorno.Pedido.Lineas.Single(l => l.Id == entorno.Ayacucho).EstadoImpreso);
        Assert.Empty(entorno.Notificador.Solicitudes);
    }

    [Fact]
    public async Task Una_linea_que_no_es_del_envio_ni_del_pedido_es_validacion_y_un_pedido_inexistente_es_no_encontrado()
    {
        var entorno = new Entorno();

        var deOtroEnvio = await entorno.Caso.EnvioEnReparto(new EnvioEnRepartoEntrada(entorno.Pedido.Id, EnvioCusco, [entorno.Ayacucho]), default);
        var inexistente = await entorno.Caso.EnvioEnReparto(new EnvioEnRepartoEntrada(entorno.Pedido.Id, EnvioCusco, [Guid.NewGuid()]), default);
        var sinPedido = await entorno.Caso.EnvioEnReparto(new EnvioEnRepartoEntrada(Guid.NewGuid(), EnvioCusco, [entorno.Cusco]), default);
        var sinLineas = await entorno.Caso.EnvioEnReparto(new EnvioEnRepartoEntrada(entorno.Pedido.Id, EnvioCusco, []), default);

        Assert.Equal("validacion-fallida", deOtroEnvio.Error!.Clave);
        Assert.Equal("validacion-fallida", inexistente.Error!.Clave);
        Assert.Equal((CategoriaDeError.NoEncontrado, "no-encontrado"), (sinPedido.Error!.Categoria, sinPedido.Error.Clave));
        Assert.Equal("validacion-fallida", sinLineas.Error!.Clave);
        Assert.All(entorno.Pedido.Lineas, l => Assert.Equal(EstadoDeLineaImpresa.EnPreparacion, l.EstadoImpreso));
    }

    [Fact]
    public async Task Con_una_operacion_en_curso_sobre_la_linea_responde_servicio_no_disponible_para_que_Envios_reintente()
    {
        var entorno = new Entorno();
        var plan = entorno.Pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro, new HashSet<Guid> { entorno.Ayacucho }).Plan!;
        entorno.Repositorio.Anulaciones.Add(Anulacion.Iniciar(Guid.NewGuid(), entorno.Pedido.Id, plan, new IniciadorDeOperacion(Guid.NewGuid(), "administrador-central", null), Ahora));

        var resultado = await entorno.Caso.EnvioEnReparto(new EnvioEnRepartoEntrada(entorno.Pedido.Id, EnvioCusco, [entorno.Cusco]), default);

        Assert.Equal((CategoriaDeError.NoDisponible, "servicio-no-disponible"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(EstadoDeLineaImpresa.EnPreparacion, entorno.Pedido.Lineas.Single(l => l.Id == entorno.Cusco).EstadoImpreso);
    }

    [Fact]
    public async Task Sin_poder_auditar_la_entrega_no_se_aplica_y_Envios_la_reintenta()
    {
        var entorno = new Entorno();
        await entorno.Caso.EnvioEnReparto(new EnvioEnRepartoEntrada(entorno.Pedido.Id, EnvioCusco, [entorno.Cusco]), default);
        entorno.Auditoria.Caida = true;

        var resultado = await entorno.Caso.EnvioEntregado(new EnvioEntregadoEntrada(entorno.Pedido.Id, EnvioCusco, [entorno.Cusco], Entrega), default);

        Assert.Equal("servicio-no-disponible", resultado.Error!.Clave);
        Assert.Equal(1, entorno.Unidad.Revertidas);
    }
}
