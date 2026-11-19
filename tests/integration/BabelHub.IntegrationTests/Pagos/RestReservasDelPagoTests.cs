using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using BabelHub.Shared.Contratos.Inventario;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// T26: <c>RestReservasDelPago</c> sobre <c>IClienteDelInventario</c> (PAG-13, AC-PAG-13): <c>liberar</c> con el motivo estable del contrato y sin líneas, la traducción del estado inválido y de «no existe», la validación de la respuesta (SEG-13), que
/// toda otra falla es «sin respuesta» sin excepción, y que <c>consultar</c>, <c>extender</c> y <c>confirmar</c> —que el cliente aún no tiene— fallan cerrado.
/// </summary>
public sealed class RestReservasDelPagoTests
{
    private static readonly Guid Pedido = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ReservaDelInventario Reserva(string estado, Guid? pedido = null) =>
        new(pedido ?? Pedido, estado, DateTimeOffset.UnixEpoch, null, [], []);

    private static (RestReservasDelPago Adaptador, ClienteFalso Cliente) Crear(Func<ResultadoDelInventario<ReservaDelInventario>> respuesta)
    {
        var cliente = new ClienteFalso(respuesta);
        return (new RestReservasDelPago(cliente), cliente);
    }

    [Theory]
    [InlineData(MotivoDeLiberacion.FallaPosteriorAlPago, MotivosDeLiberacion.FallaPosteriorAlPago)]
    [InlineData(MotivoDeLiberacion.PagoVencido, MotivosDeLiberacion.PagoVencido)]
    [Trait("Criterio", "AC-PAG-13")]
    public async Task Liberar_envia_el_motivo_estable_del_contrato_sin_lineas_y_responde_correcto(MotivoDeLiberacion motivo, string esperado)
    {
        var (adaptador, cliente) = Crear(() => ResultadoDelInventario<ReservaDelInventario>.ConValor(Reserva(EstadosDeReserva.Liberada)));

        var respuesta = await adaptador.Liberar(Pedido, motivo);

        Assert.True(respuesta.EsCorrecta);
        Assert.True(respuesta.Valor);
        Assert.Equal((Pedido, esperado, (IReadOnlyList<Guid>?)null), cliente.Llamadas.Single());
    }

    [Fact]
    public async Task Liberar_una_reserva_ya_expirada_es_un_exito()
    {
        var (adaptador, _) = Crear(() => ResultadoDelInventario<ReservaDelInventario>.ConValor(Reserva(EstadosDeReserva.Expirada)));

        Assert.True((await adaptador.Liberar(Pedido, MotivoDeLiberacion.PagoVencido)).EsCorrecta);
    }

    [Theory]
    [InlineData(EstadosDeReserva.Pendiente)]
    [InlineData(EstadosDeReserva.Confirmada)]
    [InlineData(EstadosDeReserva.Cerrada)]
    public async Task Una_liberacion_que_deja_la_reserva_en_otro_estado_no_es_correcta(string estado)
    {
        var (adaptador, _) = Crear(() => ResultadoDelInventario<ReservaDelInventario>.ConValor(Reserva(estado)));

        var respuesta = await adaptador.Liberar(Pedido, MotivoDeLiberacion.FallaPosteriorAlPago);

        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, respuesta.Resultado);
    }

    [Fact]
    public async Task Una_reserva_de_otro_pedido_en_la_respuesta_no_es_correcta()
    {
        var (adaptador, _) = Crear(() => ResultadoDelInventario<ReservaDelInventario>.ConValor(Reserva(EstadosDeReserva.Liberada, Guid.NewGuid())));

        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, (await adaptador.Liberar(Pedido, MotivoDeLiberacion.PagoVencido)).Resultado);
    }

    [Fact]
    public async Task El_estado_invalido_y_la_reserva_inexistente_se_traducen_y_cualquier_otro_rechazo_es_sin_respuesta()
    {
        var invalido = await Crear(() => ResultadoDelInventario<ReservaDelInventario>.Rechazo(409, CodigosDeErrorDelInventario.ReservaEstadoInvalido)).Adaptador.Liberar(Pedido, MotivoDeLiberacion.PagoVencido);
        var inexistente = await Crear(() => ResultadoDelInventario<ReservaDelInventario>.Rechazo(404, CodigosDeErrorDelInventario.NoEncontrado)).Adaptador.Liberar(Pedido, MotivoDeLiberacion.PagoVencido);
        var prohibido = await Crear(() => ResultadoDelInventario<ReservaDelInventario>.Rechazo(403, "INV-PROHIBIDO")).Adaptador.Liberar(Pedido, MotivoDeLiberacion.PagoVencido);

        Assert.Equal(ResultadoDeLaLlamadaDeReserva.EstadoInvalido, invalido.Resultado);
        Assert.Equal(ResultadoDeLaLlamadaDeReserva.NoExiste, inexistente.Resultado);
        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, prohibido.Resultado);
        Assert.All(new[] { invalido, inexistente, prohibido }, r => Assert.False(r.Valor));
    }

    [Fact]
    public async Task El_limite_la_falta_de_respuesta_y_las_excepciones_son_sin_respuesta_sin_lanzar()
    {
        var limite = await Crear(() => ResultadoDelInventario<ReservaDelInventario>.Limitada("INV-LIMITE-EXCEDIDO")).Adaptador.Liberar(Pedido, MotivoDeLiberacion.PagoVencido);
        var caido = await Crear(() => ResultadoDelInventario<ReservaDelInventario>.SinRespuesta).Adaptador.Liberar(Pedido, MotivoDeLiberacion.PagoVencido);
        var lanza = await Crear(() => throw new HttpRequestException("se cayó")).Adaptador.Liberar(Pedido, MotivoDeLiberacion.PagoVencido);

        Assert.All(new[] { limite, caido, lanza }, r => Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, r.Resultado));
    }

    [Fact]
    public async Task La_cancelacion_no_se_traga()
    {
        var (adaptador, _) = Crear(() => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => adaptador.Liberar(Pedido, MotivoDeLiberacion.PagoVencido));
    }

    [Fact]
    public async Task Un_pedido_vacio_es_un_error_de_programacion()
    {
        var (adaptador, _) = Crear(() => ResultadoDelInventario<ReservaDelInventario>.SinRespuesta);

        await Assert.ThrowsAsync<ArgumentException>(() => adaptador.Liberar(Guid.Empty, MotivoDeLiberacion.PagoVencido));
    }

    [Fact]
    public async Task Consultar_extender_y_confirmar_fallan_cerrado_hasta_que_el_cliente_del_inventario_los_tenga_y_no_llaman_a_nada()
    {
        var (adaptador, cliente) = Crear(() => ResultadoDelInventario<ReservaDelInventario>.SinRespuesta);

        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, (await adaptador.Consultar(Pedido, Guid.NewGuid())).Resultado);
        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, (await adaptador.Extender(Pedido)).Resultado);
        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, (await adaptador.Confirmar(Pedido, "conf-1")).Resultado);
        Assert.Empty(cliente.Llamadas);
    }

    private sealed class ClienteFalso(Func<ResultadoDelInventario<ReservaDelInventario>> respuesta) : IClienteDelInventario
    {
        public List<(Guid Pedido, string Motivo, IReadOnlyList<Guid>? Lineas)> Llamadas { get; } = [];

        public Task<ResultadoDelInventario<ReservaDelInventario>> Liberar(Guid pedidoId, string motivo, IReadOnlyList<Guid>? lineaIds = null, CancellationToken cancelacion = default)
        {
            Llamadas.Add((pedidoId, motivo, lineaIds));
            return Task.FromResult(respuesta());
        }

        public Task<ResultadoDelInventario> RegistrarEdicion(EdicionARegistrar edicion, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<DisponibilidadDeVariasEdiciones>> ConsultarDisponibilidad(IReadOnlyList<Guid> edicionIds, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<IngresoRegistrado>> RegistrarIngreso(IngresoARegistrar ingreso, string claveDeIdempotencia, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<IReadOnlyList<IngresoRegistrado>>> ListarIngresos(Guid edicionId, int limite, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<PendientesDeTitular>> ConsultarPendientes(Guid titularId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario> DarDeBajaAlTitular(Guid titularId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<RespuestaDeConsultaPrevia>> ConsultarPrevia(ConsultaPrevia consulta, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<ReservaDelInventario>> Reservar(SolicitudDeReservaDeStock solicitud, string claveDeIdempotencia, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<ReservaDelInventario>> ConsultarReserva(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<ExtensionDeLaReserva>> ExtenderReserva(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<ReservaDelInventario>> ConfirmarReserva(Guid pedidoId, string claveDeIdempotencia, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<CierreDeLaReserva>> CerrarReserva(Guid pedidoId, string motivo, IReadOnlyList<Guid> lineaIds, Guid? envioId, string claveDeIdempotencia, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<IReadOnlyList<SucursalPublica>>> ListarSucursales(CancellationToken cancelacion = default) => throw new NotSupportedException();
    }
}
