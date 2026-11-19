using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.UnitTests.Soporte;
using static BabelHub.Ecommerce.UnitTests.Pagos.InstantaneasDeCompraDePrueba;
using AppMedioDePago = BabelHub.Ecommerce.Application.Pagos.MedioDePago;
using DomMedioDePago = BabelHub.Ecommerce.Domain.Pagos.MedioDePago;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>
/// El estado del pago de una compra para la web (T27; PAG-07, PAG-14, PAG-21, PAG-24): solo el cliente dueño, el pedido ajeno o inexistente responde igual y deja un acceso denegado, la lectura no escribe ni bloquea, y cada estado de la intención se
/// traduce a lo que el cliente puede esperar o hacer (pagar, esperar la confirmación, comprar completada, cerrada), con el último motivo de rechazo y los plazos. También comprueba que la fachada <see cref="IPagos"/> delega en los casos de uso.
/// </summary>
public sealed class ObtenerEstadoDelPagoTests
{
    private static async Task<(EntornoDePagos Entorno, ObtenerEstadoDelPago Estado)> Crear()
    {
        var e = new EntornoDePagos();
        await e.CrearMixta();
        return (e, new ObtenerEstadoDelPago(e.Autorizador, e.Repositorio, e.Unidad, e.Reloj));
    }

    private static async Task<IntentoDeCobro> SembrarIntento(EntornoDePagos e, Action<IntentoDeCobro> alterar)
    {
        var intento = IntentoDeCobro.Iniciar(Guid.NewGuid(), Pedido, DomMedioDePago.Tarjeta, Importe.De(140m), e.Reloj.Ahora);
        alterar(intento);
        await e.Unidad.Ejecutar(async token =>
        {
            await e.Repositorio.AgregarIntento(intento, token);
            return 0;
        });
        return intento;
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public async Task Una_intencion_abierta_esta_lista_para_pagar_con_su_vigencia_y_su_reserva_y_sin_escribir_ni_bloquear()
    {
        var (e, estado) = await Crear();
        var bloqueos = e.Repositorio.LecturasConBloqueo;
        var guardados = e.Repositorio.GuardadosDeIntenciones;

        var resultado = await estado.Ejecutar(Pedido);

        Assert.True(resultado.EsExito);
        var vista = resultado.Valor;
        Assert.Equal(EstadoDelPagoDeLaCompra.ListoParaPagar, vista.Estado);
        Assert.Equal(e.Reloj.Ahora.AddMinutes(15), vista.LimiteEn);
        Assert.Equal(e.ReservaExpiraEn, vista.ReservaExpiraEn);
        Assert.Null(vista.UltimoMotivo);
        Assert.True(vista.PuedeReintentar);
        Assert.Equal(bloqueos, e.Repositorio.LecturasConBloqueo);
        Assert.Equal(guardados, e.Repositorio.GuardadosDeIntenciones);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public async Task Con_la_reserva_expirada_ya_no_se_puede_pagar()
    {
        var (e, estado) = await Crear();
        e.Reloj.Ahora = e.Reloj.Ahora.AddMinutes(16);

        var vista = (await estado.Ejecutar(Pedido)).Valor;

        Assert.Equal(EstadoDelPagoDeLaCompra.ListoParaPagar, vista.Estado);
        Assert.False(vista.PuedeReintentar);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Tras_un_rechazo_se_puede_reintentar_y_se_informa_el_ultimo_motivo()
    {
        var (e, estado) = await Crear();
        e.Reloj.Ahora = IntencionesDePrueba.Inicio.AddSeconds(30);
        await SembrarIntento(e, i => i.MarcarRechazado(MotivoDeRechazo.FondosInsuficientes, e.Reloj.Ahora));

        var vista = (await estado.Ejecutar(Pedido)).Valor;

        Assert.Equal(EstadoDelPagoDeLaCompra.ListoParaPagar, vista.Estado);
        Assert.Equal(MotivoDeRechazoDePago.FondosInsuficientes, vista.UltimoMotivo);
        Assert.True(vista.PuedeReintentar);
    }

    [Fact]
    public async Task Con_varios_intentos_el_ultimo_motivo_es_el_del_mas_reciente()
    {
        var (e, estado) = await Crear();
        await SembrarIntento(e, i => i.MarcarRechazado(MotivoDeRechazo.TarjetaVencida, e.Reloj.Ahora));
        e.Reloj.Ahora = e.Reloj.Ahora.AddMinutes(1);
        await SembrarIntento(e, i => i.MarcarRechazado(MotivoDeRechazo.YapeRechazado, e.Reloj.Ahora));

        Assert.Equal(MotivoDeRechazoDePago.YapeRechazado, (await estado.Ejecutar(Pedido)).Valor.UltimoMotivo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task Con_un_cobro_sin_resolver_se_espera_la_confirmacion_hasta_el_limite_del_pendiente()
    {
        var e = await EntornoDePagosExtensiones.ConIntencionLista();
        var estado = new ObtenerEstadoDelPago(e.Autorizador, e.Repositorio, e.Unidad, e.Reloj);
        var intencion = await e.Unidad.Ejecutar(token => e.Repositorio.ObtenerIntencion(Pedido, false, token));
        Assert.Equal(ValidacionDelCobro.Permitido, intencion!.IniciarCobro(e.Reloj.Ahora));
        await e.Unidad.Ejecutar(async token =>
        {
            await e.Repositorio.GuardarIntencion(intencion, token);
            await e.Repositorio.AgregarIntento(IntentoDeCobro.Iniciar(Guid.NewGuid(), Pedido, DomMedioDePago.Yape, Importe.De(140m), e.Reloj.Ahora), token);
            return 0;
        });

        var vista = (await estado.Ejecutar(Pedido)).Valor;

        Assert.Equal(EstadoDelPagoDeLaCompra.ConfirmandoElPago, vista.Estado);
        Assert.Equal(intencion.LimitePendienteEn, vista.LimiteEn);
        Assert.False(vista.PuedeReintentar);
    }

    [Fact]
    public async Task Una_compra_aprobada_completada_o_cerrada_se_traduce_a_su_estado()
    {
        var e = new EntornoDePagos();
        e.Repositorio.Sembrar(IntencionesDePrueba.Aprobada());
        var estado = new ObtenerEstadoDelPago(e.Autorizador, e.Repositorio, e.Unidad, e.Reloj);
        Assert.Equal(EstadoDelPagoDeLaCompra.Aprobado, (await estado.Ejecutar(Pedido)).Valor.Estado);

        var completada = IntencionesDePrueba.Aprobada();
        completada.MarcarConfirmacionIniciada(e.Reloj.Ahora);
        completada.MarcarReservasConfirmadas(e.Reloj.Ahora, [new ReservaConfirmadaDeLinea(IntencionesDePrueba.LineaImpresa, "AYA-01", false, null, null)]);
        Assert.Equal(ResultadoDeCompletar.Completada, completada.Completar(e.Reloj.Ahora));
        e.Repositorio.Sembrar(completada);
        var vista = (await estado.Ejecutar(Pedido)).Valor;
        Assert.Equal(EstadoDelPagoDeLaCompra.Completado, vista.Estado);
        Assert.False(vista.PuedeReintentar);

        var cerrada = IntencionesDePrueba.Abierta();
        Assert.Equal(ResultadoDeCancelacion.Cancelada, cerrada.Cancelar(e.Reloj.Ahora));
        e.Repositorio.Sembrar(cerrada);
        Assert.Equal(EstadoDelPagoDeLaCompra.Cerrado, (await estado.Ejecutar(Pedido)).Valor.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Un_pedido_ajeno_o_inexistente_responde_igual_y_deja_un_acceso_denegado_por_cada_intento()
    {
        var (e, estado) = await Crear();
        e.Actor.Establecer(ActorDePrueba.DeCliente().Actual);

        var ajeno = await estado.Ejecutar(Pedido);
        var inexistente = await estado.Ejecutar(Guid.NewGuid());

        Assert.Equal(CategoriaDeError.NoEncontrado, ajeno.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
        Assert.Equal(ajeno.Error.Clave, inexistente.Error.Clave);
        Assert.Equal(2, e.Denegaciones.Eventos.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Un_visitante_o_sin_contexto_recibe_no_autenticado_sin_leer_nada()
    {
        var (e, estado) = await Crear();
        var operaciones = e.Repositorio.Operaciones;

        e.Actor.Establecer(ActorDePrueba.Anonimo().Actual);
        var visitante = await estado.Ejecutar(Pedido);
        e.Actor.Establecer(null);
        var sinContexto = await estado.Ejecutar(Pedido);

        Assert.Equal(CategoriaDeError.NoAutenticado, visitante.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoAutenticado, sinContexto.Error!.Categoria);
        Assert.Equal(operaciones, e.Repositorio.Operaciones);
    }

    [Fact]
    public async Task Un_pedido_vacio_es_una_validacion_fallida()
    {
        var (_, estado) = await Crear();

        var resultado = await estado.Ejecutar(Guid.Empty);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
    }

    [Fact]
    public async Task La_fachada_publica_delega_en_la_clave_el_cobro_y_el_estado()
    {
        var e = await EntornoDePagosExtensiones.ConIntencionLista();
        var pagos = new PagosDelCliente(e.EmitirClave, e.Cobrar, new ObtenerEstadoDelPago(e.Autorizador, e.Repositorio, e.Unidad, e.Reloj));

        var clave = await pagos.EmitirClaveDeIntento(new EmitirClaveDeIntentoEntrada(Pedido));
        var estado = await pagos.ObtenerEstado(Pedido);

        Assert.True(clave.EsExito);
        Assert.Equal(EstadoDelPagoDeLaCompra.ListoParaPagar, estado.Valor.Estado);
        var cobro = await pagos.Cobrar(new CobrarEntrada(Pedido, clave.Valor.Token, AppMedioDePago.Tarjeta,TokenDelMedioDePago.Crear("tok_aprobado")));
        Assert.True(cobro.EsExito);
        Assert.Equal(EstadoDelCobro.Aprobado, cobro.Valor.Estado);
    }
}
