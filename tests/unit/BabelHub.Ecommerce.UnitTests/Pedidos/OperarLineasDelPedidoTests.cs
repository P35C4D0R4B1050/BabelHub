using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;
using PedidoDeDominio = BabelHub.Ecommerce.Domain.Pedidos.Pedido;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T15 (PED-09, PED-10, PED-19, PED-28; AC-PED-03, 07, 08, 23, 25): preparar y marcar listas en recojo y a domicilio, con el envío creado en la misma transacción.</summary>
public sealed class OperarLineasDelPedidoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class DespachoFalso : IDespachoDeEnvios
    {
        public List<CrearEnvioEntrada> Creados { get; } = [];

        public bool Falla { get; set; }

        public bool Lanza { get; set; }

        public Task<Resultado<ResultadoDeCrearEnvio>> Crear(CrearEnvioEntrada entrada, CancellationToken cancelacion = default)
        {
            if (Lanza)
            {
                throw new InvalidOperationException("Envíos cayó a mitad de la operación");
            }

            if (Falla)
            {
                return Task.FromResult(Resultado<ResultadoDeCrearEnvio>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible")));
            }

            Creados.Add(entrada);
            return Task.FromResult(Resultado<ResultadoDeCrearEnvio>.Exito(new ResultadoDeCrearEnvio(Guid.NewGuid(), false, entrada.LineaIds, [])));
        }

        public Task<Resultado<IReadOnlyList<ResultadoDeCancelarEnvio>>> Cancelar(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class Entorno(ActorDePrueba actor)
    {
        public RepositorioDePedidosFalso Repositorio { get; } = new();

        public DenegacionesDelCatalogoEspia Denegaciones { get; } = new();

        public DespachoFalso Despacho { get; } = new();

        public UnidadDeTrabajoEspia Unidad { get; } = new();

        public AuditoriaDelCatalogoEspia Auditoria { get; } = new();

        public NotificadorDePedidosEspia Notificador { get; } = new();

        public LimitesPermisivosDePedidos Limites { get; } = new();

        public OperarLineasDelPedido Caso => new(
            new AutorizadorDePedidos(actor, Denegaciones, new ResolucionDeSucursalFalsa()),
            new ControlesDeGestionDePedidos(Limites, new EstadoDeCuentaFalso(), Repositorio),
            Repositorio,
            Unidad,
            Despacho,
            Auditoria,
            Notificador,
            new RelojFijo(Ahora));
    }

    private static DatosDeLineaNueva Impresa(string titulo, string fuente = "LIM", bool traslado = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), false, titulo, null, 1, 50m, 50m, fuente, traslado, traslado ? Guid.NewGuid() : null, null);

    private static PedidoDeDominio Crear(TipoDeEntregaDelPedido tipo, string sucursal = "LIM", params DatosDeLineaNueva[] lineas)
    {
        var contacto = tipo == TipoDeEntregaDelPedido.Domicilio ? BabelHub.Ecommerce.Domain.Pedidos.ContactoDeEntrega.Crear("Ana", "987654321", "Jr. Lima 123") : null;
        var envio = tipo == TipoDeEntregaDelPedido.Domicilio ? 12m : 0m;
        var datos = new DatosDePedidoNuevo(
            Guid.NewGuid(), "ABCD234567", Cuenta, tipo, tipo == TipoDeEntregaDelPedido.Recojo ? sucursal : null, tipo == TipoDeEntregaDelPedido.Domicilio ? "ayacucho" : null, envio, lineas.Sum(l => l.Importe) + envio, contacto,
            new("t-1", Ahora), new("p-1", Ahora), null, lineas, Ahora);
        return PedidoDeDominio.Crear(datos).Pedido!;
    }

    private static ActorDePrueba Encargado(Guid? sucursal = null) => ActorDePrueba.DeEncargadoDe(sucursal ?? ResolucionDeSucursalFalsa.Lima);

    [Fact]
    [Trait("Criterio", "AC-PED-07")]
    public async Task El_encargado_prepara_y_marca_listas_las_lineas_de_su_sucursal_con_un_aviso_por_operacion_y_la_auditoria()
    {
        var entorno = new Entorno(Encargado());
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, "LIM", Impresa("Uno"), Impresa("Dos"));
        entorno.Repositorio.Poner(pedido);
        var ids = pedido.Lineas.Select(l => l.Id).ToList();

        var preparadas = await entorno.Caso.PrepararDeRecojo("LIM", new OperarLineasEntrada(pedido.Id, ids), default);
        var listas = await entorno.Caso.MarcarListasDeRecojo("LIM", new OperarLineasEntrada(pedido.Id, ids), default);

        Assert.True(preparadas.EsExito);
        Assert.Equal(EstadoVisibleDelPedido.EnPreparacion, preparadas.Valor.Estado);
        Assert.Equal(EstadoVisibleDelPedido.ListoParaRecojo, listas.Valor.Estado);
        Assert.All(listas.Valor.Lineas, l => Assert.Equal(ResultadoVisibleDeLinea.Aplicada, l.Resultado));
        // Dos operaciones, dos estados destino: un aviso al cliente y uno a la sucursal por cada una (no uno por línea).
        Assert.Equal(4, entorno.Notificador.Solicitudes.Count);
        Assert.Equal(["en-preparacion", "listo-para-recojo"], entorno.Notificador.Solicitudes.Select(s => s.Campos["estado"]).Distinct());
        Assert.Equal(2, entorno.Auditoria.Solicitudes.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-08")]
    public async Task Una_linea_que_espera_el_traslado_se_rechaza_y_la_otra_se_prepara()
    {
        var entorno = new Entorno(Encargado());
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, "LIM", Impresa("Espera", traslado: true), Impresa("Aqui"));
        entorno.Repositorio.Poner(pedido);
        var espera = pedido.Lineas.Single(l => l.Titulo == "Espera");
        var aqui = pedido.Lineas.Single(l => l.Titulo == "Aqui");

        var resultado = await entorno.Caso.PrepararDeRecojo("LIM", new OperarLineasEntrada(pedido.Id, [espera.Id, aqui.Id]), default);

        Assert.True(resultado.EsExito);
        Assert.Equal(ResultadoVisibleDeLinea.EsperaElTraslado, resultado.Valor.Lineas.Single(l => l.LineaId == espera.Id).Resultado);
        Assert.Equal(ResultadoVisibleDeLinea.Aplicada, resultado.Valor.Lineas.Single(l => l.LineaId == aqui.Id).Resultado);
        Assert.Equal(EstadoDeLineaImpresa.Confirmado, espera.EstadoImpreso);
    }

    [Fact]
    public async Task Si_ninguna_se_aplica_el_error_es_el_motivo_comun_espera_de_traslado_o_estado_invalido()
    {
        var entorno = new Entorno(Encargado());
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, "LIM", Impresa("Espera", traslado: true), Impresa("Otra"));
        entorno.Repositorio.Poner(pedido);

        var espera = await entorno.Caso.PrepararDeRecojo("LIM", new OperarLineasEntrada(pedido.Id, [pedido.Lineas[0].Id]), default);
        var invalido = await entorno.Caso.MarcarListasDeRecojo("LIM", new OperarLineasEntrada(pedido.Id, [pedido.Lineas[1].Id]), default);

        Assert.Equal((CategoriaDeError.Conflicto, "linea-espera-traslado"), (espera.Error!.Categoria, espera.Error.Clave));
        Assert.Equal((CategoriaDeError.Conflicto, "pedido-estado-invalido"), (invalido.Error!.Categoria, invalido.Error.Clave));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-25")]
    public async Task El_cliente_o_el_encargado_no_preparan_un_pedido_a_domicilio_y_queda_el_acceso_denegado()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, "LIM", Impresa("Uno"));
        var cliente = new Entorno(ActorDePrueba.DeCliente(Cuenta));
        var encargado = new Entorno(Encargado());
        cliente.Repositorio.Poner(pedido);
        encargado.Repositorio.Poner(pedido);

        var deCliente = await cliente.Caso.PrepararADomicilio(new OperarLineasEntrada(pedido.Id, [pedido.Lineas[0].Id]), default);
        var deEncargado = await encargado.Caso.PrepararADomicilio(new OperarLineasEntrada(pedido.Id, [pedido.Lineas[0].Id]), default);

        Assert.Equal("prohibido", deCliente.Error!.Clave);
        Assert.Equal("prohibido", deEncargado.Error!.Clave);
        Assert.Contains("fuera-de-rol", cliente.Denegaciones.Eventos.Select(e => e.Motivo));
        Assert.Contains("fuera-de-rol", encargado.Denegaciones.Eventos.Select(e => e.Motivo));
        Assert.Equal(EstadoDeLineaImpresa.Confirmado, pedido.Lineas[0].EstadoImpreso);
    }

    [Fact]
    public async Task El_encargado_no_opera_en_otra_sucursal_ni_sobre_un_pedido_de_otra_y_logistica_no_toca_recojo()
    {
        var entorno = new Entorno(Encargado(ResolucionDeSucursalFalsa.Lima));
        var deAyacucho = Crear(TipoDeEntregaDelPedido.Recojo, "AYA", Impresa("Uno"));
        entorno.Repositorio.Poner(deAyacucho);

        var rutaAjena = await entorno.Caso.PrepararDeRecojo("AYA", new OperarLineasEntrada(deAyacucho.Id, [deAyacucho.Lineas[0].Id]), default);
        var pedidoAjeno = await entorno.Caso.PrepararDeRecojo("LIM", new OperarLineasEntrada(deAyacucho.Id, [deAyacucho.Lineas[0].Id]), default);
        var logistica = new Entorno(ActorDePrueba.DePersonalDeLogistica());
        logistica.Repositorio.Poner(deAyacucho);
        var deLogistica = await logistica.Caso.PrepararADomicilio(new OperarLineasEntrada(deAyacucho.Id, [deAyacucho.Lineas[0].Id]), default);

        Assert.Equal("prohibido", rutaAjena.Error!.Clave);
        Assert.Equal("no-encontrado", pedidoAjeno.Error!.Clave);
        Assert.Equal("no-encontrado", deLogistica.Error!.Clave);
        Assert.Equal(EstadoDeLineaImpresa.Confirmado, deAyacucho.Lineas[0].EstadoImpreso);
    }

    [Fact]
    public async Task Una_linea_ajena_repetida_vacia_o_de_mas_de_50_se_trata_segun_el_plan()
    {
        var entorno = new Entorno(Encargado());
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, "LIM", Impresa("Uno"));
        entorno.Repositorio.Poner(pedido);
        var propia = pedido.Lineas[0].Id;

        var ajena = await entorno.Caso.PrepararDeRecojo("LIM", new OperarLineasEntrada(pedido.Id, [propia, Guid.NewGuid()]), default);
        var repetida = await entorno.Caso.PrepararDeRecojo("LIM", new OperarLineasEntrada(pedido.Id, [propia, propia]), default);
        var vacia = await entorno.Caso.PrepararDeRecojo("LIM", new OperarLineasEntrada(pedido.Id, []), default);
        var demasiadas = await entorno.Caso.PrepararDeRecojo("LIM", new OperarLineasEntrada(pedido.Id, [.. Enumerable.Range(0, 51).Select(_ => Guid.NewGuid())]), default);

        Assert.Equal("no-encontrado", ajena.Error!.Clave);
        Assert.True(repetida.EsExito);
        Assert.Equal(("validacion-fallida", "validacion-fallida"), (vacia.Error!.Clave, demasiadas.Error!.Clave));
        // El repetido se desduplica sin dejar un evento de seguridad.
        Assert.Single(entorno.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-23")]
    public async Task A_domicilio_logistica_prepara_y_marca_listas_y_se_crea_un_envio_por_fuente_en_la_misma_transaccion()
    {
        var entorno = new Entorno(ActorDePrueba.DePersonalDeLogistica());
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, "LIM", Impresa("Cusco 1", "CUS"), Impresa("Cusco 2", "CUS"), Impresa("Lima", "LIM"));
        entorno.Repositorio.Poner(pedido);
        var ids = pedido.Lineas.Select(l => l.Id).ToList();
        await entorno.Caso.PrepararADomicilio(new OperarLineasEntrada(pedido.Id, ids), default);

        var listas = await entorno.Caso.MarcarListasADomicilio(new OperarLineasEntrada(pedido.Id, ids), default);

        Assert.True(listas.EsExito);
        Assert.Equal(["CUS", "LIM"], entorno.Despacho.Creados.Select(c => c.CodigoDeFuente));
        Assert.Equal(2, entorno.Despacho.Creados.Single(c => c.CodigoDeFuente == "CUS").LineaIds.Count);
        Assert.All(pedido.Lineas, l => Assert.NotNull(l.EnvioId));
        Assert.Equal(pedido.Lineas.Single(l => l.Titulo == "Cusco 1").EnvioId, pedido.Lineas.Single(l => l.Titulo == "Cusco 2").EnvioId);
        Assert.Equal(EstadoVisibleDelPedido.EnPreparacion, listas.Valor.Estado);
        Assert.All(pedido.Lineas, l => Assert.NotNull(l.ListoEn));
        // Una repetición ya no tiene líneas por marcar: estado inválido y no se crea otro envío.
        Assert.Equal("pedido-estado-invalido", (await entorno.Caso.MarcarListasADomicilio(new OperarLineasEntrada(pedido.Id, ids), default)).Error!.Clave);
        Assert.Equal(2, entorno.Despacho.Creados.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Criterio", "AC-PED-23")]
    public async Task Un_fallo_o_una_excepcion_de_Envios_revierte_toda_la_operacion_y_responde_servicio_no_disponible(bool lanza)
    {
        var entorno = new Entorno(ActorDePrueba.DePersonalDeLogistica());
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, "LIM", Impresa("Uno", "CUS"));
        entorno.Repositorio.Poner(pedido);
        await entorno.Caso.PrepararADomicilio(new OperarLineasEntrada(pedido.Id, [pedido.Lineas[0].Id]), default);
        entorno.Despacho.Falla = !lanza;
        entorno.Despacho.Lanza = lanza;
        var avisosAntes = entorno.Notificador.Solicitudes.Count;
        var auditoriasAntes = entorno.Auditoria.Solicitudes.Count;

        var resultado = await entorno.Caso.MarcarListasADomicilio(new OperarLineasEntrada(pedido.Id, [pedido.Lineas[0].Id]), default);

        Assert.Equal((CategoriaDeError.NoDisponible, "servicio-no-disponible"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(1, entorno.Unidad.Revertidas);
        Assert.Equal(avisosAntes, entorno.Notificador.Solicitudes.Count);
        Assert.Equal(auditoriasAntes, entorno.Auditoria.Solicitudes.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-17")]
    public async Task Una_operacion_en_curso_sobre_la_linea_rechaza_la_segunda_y_un_limite_excedido_ni_abre_la_transaccion()
    {
        var entorno = new Entorno(Encargado());
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, "LIM", Impresa("Uno"));
        entorno.Repositorio.Poner(pedido);
        var linea = pedido.Lineas[0];
        entorno.Repositorio.Anulaciones.Add(Anulacion.Iniciar(Guid.NewGuid(), pedido.Id, new PlanDeAnulacion(OrigenDeLaAnulacion.Administracion, MotivoDeAnulacion.Otro, [linea.Id], [], 0m, 50m, []), new IniciadorDeOperacion(Guid.NewGuid(), "administrador-central", null), Ahora));

        var enCurso = await entorno.Caso.PrepararDeRecojo("LIM", new OperarLineasEntrada(pedido.Id, [linea.Id]), default);
        entorno.Limites.Gestion = new ResultadoDeLimiteDePedidos(DecisionDeLimiteDePedidos.Excedido, Ahora);
        var antes = entorno.Unidad.Ejecuciones;
        var limitada = await entorno.Caso.PrepararDeRecojo("LIM", new OperarLineasEntrada(pedido.Id, [linea.Id]), default);

        Assert.Equal("pedido-operacion-en-curso", enCurso.Error!.Clave);
        Assert.Equal("limite-excedido", limitada.Error!.Clave);
        Assert.Equal(antes, entorno.Unidad.Ejecuciones);
        Assert.Equal(EstadoDeLineaImpresa.Confirmado, linea.EstadoImpreso);
    }

    [Fact]
    public async Task Si_el_Notificador_lanza_la_operacion_se_aplica_igual()
    {
        var entorno = new Entorno(Encargado());
        entorno.Notificador.Lanza = true;
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, "LIM", Impresa("Uno"));
        entorno.Repositorio.Poner(pedido);

        var resultado = await entorno.Caso.PrepararDeRecojo("LIM", new OperarLineasEntrada(pedido.Id, [pedido.Lineas[0].Id]), default);

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDeLineaImpresa.EnPreparacion, pedido.Lineas[0].EstadoImpreso);
    }
}
