using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using BabelHub.Shared.Contratos.Inventario;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// T26: <c>RestReservasDelPago</c> sobre <c>IClienteDelInventario</c> (PAG-13, AC-PAG-13): <c>liberar</c> con el motivo estable del contrato y sin líneas, la traducción del estado inválido y de «no existe», la validación de la respuesta (SEG-13), que
/// toda otra falla es «sin respuesta» sin excepción; y <c>consultar</c>, <c>extender</c> y <c>confirmar</c> sobre las operaciones del cliente (expiración y vigencia con el reloj, «sin cambios» como éxito, fuente y traslado de cada línea, falla cerrada si algo no cuadra).
/// </summary>
public sealed class RestReservasDelPagoTests
{
    private static readonly Guid Pedido = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ReservaDelInventario Reserva(string estado, Guid? pedido = null) =>
        new(pedido ?? Pedido, estado, DateTimeOffset.UnixEpoch, null, [], []);

    private static (RestReservasDelPago Adaptador, ClienteFalso Cliente) Crear(Func<ResultadoDelInventario<ReservaDelInventario>> respuesta)
    {
        var cliente = new ClienteFalso(respuesta);
        return (new RestReservasDelPago(cliente, new RelojDePrueba()), cliente);
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

    // ---------- consultar, extender y confirmar ----------

    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Linea1 = Guid.Parse("33333333-3333-3333-3333-333333333331");
    private static readonly Guid Linea2 = Guid.Parse("33333333-3333-3333-3333-333333333332");
    private static readonly Guid Edicion1 = Guid.Parse("44444444-4444-4444-4444-444444444441");
    private static readonly Guid Edicion2 = Guid.Parse("44444444-4444-4444-4444-444444444442");
    private static readonly Guid Traslado = Guid.Parse("55555555-5555-5555-5555-555555555551");

    private static ReservaDelInventario ReservaConExpiracion(string estado, DateTimeOffset? expira, Guid? pedido = null) =>
        new(pedido ?? Pedido, estado, DateTimeOffset.UnixEpoch, expira, [], []);

    private static ReservaDelInventario Confirmada(bool conTraslado = true, Guid? pedido = null) =>
        new(
            pedido ?? Pedido,
            EstadosDeReserva.Confirmada,
            DateTimeOffset.UnixEpoch,
            null,
            [new LineaReservada(Linea1, Edicion1, 2, "confirmada", "AYA-01", false, null), new LineaReservada(Linea2, Edicion2, 1, "confirmada", "LIM-02", true, 5)],
            conTraslado ? [new TrasladoDeLaReserva(Traslado, Edicion2, 1, "LIM-02", "AYA-01", "solicitado", new DateOnly(2026, 11, 25))] : []);

    private static (RestReservasDelPago Adaptador, ClienteFalso Cliente) CrearSinLiberar() => Crear(() => throw new NotSupportedException());

    [Fact]
    public async Task Consultar_traduce_la_expiracion_y_la_vigencia_con_la_hora_del_reloj()
    {
        var futuro = Ahora.AddMinutes(10);
        var (adaptador, cliente) = CrearSinLiberar();

        cliente.Consultada = () => ResultadoDelInventario<ReservaDelInventario>.ConValor(ReservaConExpiracion(EstadosDeReserva.Pendiente, futuro));
        var vigente = await adaptador.Consultar(Pedido, Guid.NewGuid());
        cliente.Consultada = () => ResultadoDelInventario<ReservaDelInventario>.ConValor(ReservaConExpiracion(EstadosDeReserva.Pendiente, Ahora.AddMinutes(-1)));
        var vencida = await adaptador.Consultar(Pedido, Guid.NewGuid());
        cliente.Consultada = () => ResultadoDelInventario<ReservaDelInventario>.ConValor(ReservaConExpiracion(EstadosDeReserva.Confirmada, futuro));
        var confirmada = await adaptador.Consultar(Pedido, Guid.NewGuid());

        Assert.Equal((futuro, true), (vigente.Valor!.ExpiraEn, vigente.Valor.Vigente));
        Assert.False(vencida.Valor!.Vigente);
        Assert.False(confirmada.Valor!.Vigente);
    }

    [Fact]
    public async Task Consultar_sin_expiracion_de_otro_pedido_o_con_rechazo_se_traduce_y_nunca_lanza()
    {
        var (adaptador, cliente) = CrearSinLiberar();

        cliente.Consultada = () => ResultadoDelInventario<ReservaDelInventario>.ConValor(ReservaConExpiracion(EstadosDeReserva.Pendiente, null));
        var sinExpiracion = await adaptador.Consultar(Pedido, Guid.NewGuid());
        cliente.Consultada = () => ResultadoDelInventario<ReservaDelInventario>.ConValor(ReservaConExpiracion(EstadosDeReserva.Pendiente, Ahora.AddMinutes(5), Guid.NewGuid()));
        var ajena = await adaptador.Consultar(Pedido, Guid.NewGuid());
        cliente.Consultada = () => ResultadoDelInventario<ReservaDelInventario>.Rechazo(404, CodigosDeErrorDelInventario.NoEncontrado);
        var inexistente = await adaptador.Consultar(Pedido, Guid.NewGuid());
        cliente.Consultada = () => ResultadoDelInventario<ReservaDelInventario>.Rechazo(403, "INV-PROHIBIDO");
        var prohibida = await adaptador.Consultar(Pedido, Guid.NewGuid());
        cliente.Consultada = () => throw new HttpRequestException("se cayó");
        var lanza = await adaptador.Consultar(Pedido, Guid.NewGuid());

        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, sinExpiracion.Resultado);
        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, ajena.Resultado);
        Assert.Equal(ResultadoDeLaLlamadaDeReserva.NoExiste, inexistente.Resultado);
        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, prohibida.Resultado);
        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, lanza.Resultado);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Criterio", "AC-PAG-11")]
    public async Task Extender_devuelve_la_expiracion_actual_y_si_esta_llamada_extendio_y_sin_cambios_no_es_un_error(bool extendida)
    {
        var expira = Ahora.AddMinutes(30);
        var (adaptador, cliente) = CrearSinLiberar();
        cliente.Extension = () => ResultadoDelInventario<ExtensionDeLaReserva>.ConValor(new ExtensionDeLaReserva(extendida, ReservaConExpiracion(EstadosDeReserva.Pendiente, expira)));

        var respuesta = await adaptador.Extender(Pedido);

        Assert.True(respuesta.EsCorrecta);
        Assert.Equal((expira, extendida), (respuesta.Valor!.ExpiraEn, respuesta.Valor.Extendida));
    }

    [Fact]
    public async Task Extender_con_rechazo_sin_expiracion_de_otro_pedido_o_con_excepcion_se_traduce_y_nunca_lanza()
    {
        var (adaptador, cliente) = CrearSinLiberar();

        cliente.Extension = () => ResultadoDelInventario<ExtensionDeLaReserva>.ConValor(new ExtensionDeLaReserva(true, ReservaConExpiracion(EstadosDeReserva.Pendiente, null)));
        var sinExpiracion = await adaptador.Extender(Pedido);
        cliente.Extension = () => ResultadoDelInventario<ExtensionDeLaReserva>.ConValor(new ExtensionDeLaReserva(true, ReservaConExpiracion(EstadosDeReserva.Pendiente, Ahora.AddMinutes(5), Guid.NewGuid())));
        var ajena = await adaptador.Extender(Pedido);
        cliente.Extension = () => ResultadoDelInventario<ExtensionDeLaReserva>.Rechazo(404, CodigosDeErrorDelInventario.NoEncontrado);
        var inexistente = await adaptador.Extender(Pedido);
        cliente.Extension = () => throw new HttpRequestException("se cayó");
        var lanza = await adaptador.Extender(Pedido);

        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, sinExpiracion.Resultado);
        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, ajena.Resultado);
        Assert.Equal(ResultadoDeLaLlamadaDeReserva.NoExiste, inexistente.Resultado);
        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, lanza.Resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public async Task Confirmar_envia_la_clave_y_devuelve_la_fuente_y_el_traslado_de_cada_linea()
    {
        var (adaptador, cliente) = CrearSinLiberar();
        cliente.Confirmacion = () => ResultadoDelInventario<ReservaDelInventario>.ConValor(Confirmada());

        var respuesta = await adaptador.Confirmar(Pedido, "conf-intento-1");

        Assert.True(respuesta.EsCorrecta);
        Assert.Equal([(Pedido, "conf-intento-1")], cliente.Confirmaciones);
        var lineas = respuesta.Valor!;
        Assert.Equal(("AYA-01", false, (Guid?)null, (DateOnly?)null), (lineas[0].CodigoDeFuente, lineas[0].RequiereTraslado, lineas[0].TrasladoId, lineas[0].FechaEstimadaDeLlegada));
        Assert.Equal(("LIM-02", true, (Guid?)Traslado, (DateOnly?)new DateOnly(2026, 11, 25)), (lineas[1].CodigoDeFuente, lineas[1].RequiereTraslado, lineas[1].TrasladoId, lineas[1].FechaEstimadaDeLlegada));
    }

    [Fact]
    public async Task Confirmar_empareja_el_traslado_con_la_fuente_de_la_linea_y_un_traslado_de_otro_origen_no_sirve()
    {
        var (adaptador, cliente) = CrearSinLiberar();
        var distinto = new ReservaDelInventario(
            Pedido,
            EstadosDeReserva.Confirmada,
            DateTimeOffset.UnixEpoch,
            null,
            [new LineaReservada(Linea2, Edicion2, 1, "confirmada", "LIM-02", true, 5)],
            [new TrasladoDeLaReserva(Traslado, Edicion2, 1, "CUS-03", "AYA-01", "solicitado", new DateOnly(2026, 11, 25))]);
        cliente.Confirmacion = () => ResultadoDelInventario<ReservaDelInventario>.ConValor(distinto);

        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, (await adaptador.Confirmar(Pedido, "conf-1")).Resultado);
    }

    [Fact]
    public async Task Extender_con_extendida_verdadera_exige_la_reserva_pendiente()
    {
        var (adaptador, cliente) = CrearSinLiberar();
        cliente.Extension = () => ResultadoDelInventario<ExtensionDeLaReserva>.ConValor(new ExtensionDeLaReserva(true, ReservaConExpiracion(EstadosDeReserva.Confirmada, Ahora.AddMinutes(30))));

        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, (await adaptador.Extender(Pedido)).Resultado);
    }

    [Fact]
    public async Task Confirmar_falla_cerrado_si_falta_el_traslado_de_una_linea_la_reserva_no_quedo_confirmada_o_es_de_otro_pedido()
    {
        var (adaptador, cliente) = CrearSinLiberar();

        cliente.Confirmacion = () => ResultadoDelInventario<ReservaDelInventario>.ConValor(Confirmada(conTraslado: false));
        var sinTraslado = await adaptador.Confirmar(Pedido, "conf-1");
        cliente.Confirmacion = () => ResultadoDelInventario<ReservaDelInventario>.ConValor(new ReservaDelInventario(Pedido, EstadosDeReserva.Pendiente, DateTimeOffset.UnixEpoch, null, [new LineaReservada(Linea1, Edicion1, 1, "pendiente", "AYA-01", false, null)], []));
        var pendiente = await adaptador.Confirmar(Pedido, "conf-1");
        cliente.Confirmacion = () => ResultadoDelInventario<ReservaDelInventario>.ConValor(Confirmada(pedido: Guid.NewGuid()));
        var ajena = await adaptador.Confirmar(Pedido, "conf-1");
        cliente.Confirmacion = () => ResultadoDelInventario<ReservaDelInventario>.ConValor(ReservaConExpiracion(EstadosDeReserva.Confirmada, null));
        var sinLineas = await adaptador.Confirmar(Pedido, "conf-1");

        Assert.All(new[] { sinTraslado, pendiente, ajena, sinLineas }, r => Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, r.Resultado));
    }

    [Theory]
    [InlineData(409, CodigosDeErrorDelInventario.ReservaEstadoInvalido, "EstadoInvalido")]
    [InlineData(404, CodigosDeErrorDelInventario.NoEncontrado, "NoExiste")]
    [InlineData(403, "INV-PROHIBIDO", "SinRespuesta")]
    public async Task Confirmar_traduce_el_estado_invalido_y_la_reserva_inexistente_y_el_resto_es_sin_respuesta(int estado, string codigo, string esperado)
    {
        var (adaptador, cliente) = CrearSinLiberar();
        cliente.Confirmacion = () => ResultadoDelInventario<ReservaDelInventario>.Rechazo(estado, codigo);

        Assert.Equal(esperado, (await adaptador.Confirmar(Pedido, "conf-1")).Resultado.ToString());
    }

    [Fact]
    public async Task Confirmar_extender_y_consultar_sin_pedido_o_sin_clave_son_errores_de_programacion_y_la_cancelacion_no_se_traga()
    {
        var (adaptador, cliente) = CrearSinLiberar();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => adaptador.Confirmar(Pedido, " "));
        await Assert.ThrowsAsync<ArgumentException>(() => adaptador.Extender(Guid.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => adaptador.Consultar(Guid.Empty, Guid.NewGuid()));
        cliente.Extension = () => throw new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(() => adaptador.Extender(Pedido));
    }

    private sealed class RelojDePrueba : IReloj
    {
        public DateTimeOffset Ahora => RestReservasDelPagoTests.Ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.UtcDateTime.AddHours(-5));
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

        public Func<ResultadoDelInventario<ReservaDelInventario>>? Consultada { get; set; }

        public Func<ResultadoDelInventario<ExtensionDeLaReserva>>? Extension { get; set; }

        public Func<ResultadoDelInventario<ReservaDelInventario>>? Confirmacion { get; set; }

        public List<(Guid Pedido, string Clave)> Confirmaciones { get; } = [];

        public Task<ResultadoDelInventario<ReservaDelInventario>> ConsultarReserva(Guid pedidoId, CancellationToken cancelacion = default) => Task.FromResult(Consultada!());

        public Task<ResultadoDelInventario<ExtensionDeLaReserva>> ExtenderReserva(Guid pedidoId, CancellationToken cancelacion = default) => Task.FromResult(Extension!());

        public Task<ResultadoDelInventario<ReservaDelInventario>> ConfirmarReserva(Guid pedidoId, string claveDeIdempotencia, CancellationToken cancelacion = default)
        {
            Confirmaciones.Add((pedidoId, claveDeIdempotencia));
            return Task.FromResult(Confirmacion!());
        }

        public Task<ResultadoDelInventario<CierreDeLaReserva>> CerrarReserva(Guid pedidoId, string motivo, IReadOnlyList<Guid> lineaIds, Guid? envioId, string claveDeIdempotencia, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<IReadOnlyList<SucursalPublica>>> ListarSucursales(CancellationToken cancelacion = default) => throw new NotSupportedException();
    }
}
