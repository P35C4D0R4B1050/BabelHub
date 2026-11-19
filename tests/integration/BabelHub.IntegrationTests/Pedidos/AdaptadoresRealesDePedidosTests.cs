using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Pedidos;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>
/// Los adaptadores reales de Pedidos sobre lo que entregó el carril de plataforma (PED-11, PED-25, PED-40, AC-PED-09, AC-PED-34, AC-PED-35): las reservas con una llamada por línea y su clave, los límites con la lectura del contacto que falla cerrada y la auditoría
/// de esa lectura sin el contacto.
/// </summary>
public sealed class AdaptadoresRealesDePedidosTests
{
    private static readonly Guid Pedido = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Linea = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    private sealed class ClienteFalso : IClienteDelInventario
    {
        public Func<ResultadoDelInventario<CierreDeLaReserva>> Cierre { get; set; } = () => ResultadoDelInventario<CierreDeLaReserva>.SinRespuesta;

        public Func<ResultadoDelInventario<ReservaDelInventario>> Liberacion { get; set; } = () => ResultadoDelInventario<ReservaDelInventario>.SinRespuesta;

        public List<(Guid Pedido, string Motivo, IReadOnlyList<Guid> Lineas, Guid? Envio, string Clave)> Cierres { get; } = [];

        public List<(Guid Pedido, string Motivo, IReadOnlyList<Guid>? Lineas)> Liberaciones { get; } = [];

        public Task<ResultadoDelInventario<CierreDeLaReserva>> CerrarReserva(Guid pedidoId, string motivo, IReadOnlyList<Guid> lineaIds, Guid? envioId, string claveDeIdempotencia, CancellationToken cancelacion = default)
        {
            Cierres.Add((pedidoId, motivo, lineaIds, envioId, claveDeIdempotencia));
            return Task.FromResult(Cierre());
        }

        public Task<ResultadoDelInventario<ReservaDelInventario>> Liberar(Guid pedidoId, string motivo, IReadOnlyList<Guid>? lineaIds = null, CancellationToken cancelacion = default)
        {
            Liberaciones.Add((pedidoId, motivo, lineaIds));
            return Task.FromResult(Liberacion());
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

        public Task<ResultadoDelInventario<IReadOnlyList<SucursalPublica>>> ListarSucursales(CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<ContactoDeSucursalDelInventario>> ConsultarContactoDeSucursal(string codigoDeSucursal, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private static ResultadoDelInventario<CierreDeLaReserva> Cerrada(string resultado, Guid? pedido = null, Guid? linea = null) =>
        ResultadoDelInventario<CierreDeLaReserva>.ConValor(new CierreDeLaReserva(pedido ?? Pedido, [new ResultadoDeCierreDeLinea(linea ?? Linea, resultado)]));

    // ---------- Reservas ----------

    [Fact]
    [Trait("Criterio", "AC-PED-09")]
    public async Task Cerrar_manda_una_sola_linea_con_motivo_entrega_y_su_clave_y_traduce_cerrada_y_ya_cerrada()
    {
        var cliente = new ClienteFalso { Cierre = () => Cerrada("cerrada") };
        var adaptador = new RestReservasDelPedido(cliente);

        var primera = await adaptador.Cerrar(Pedido, Linea, "cierre-x");
        cliente.Cierre = () => Cerrada("ya-cerrada");
        var repetida = await adaptador.Cerrar(Pedido, Linea, "cierre-x");

        Assert.Equal((ResultadoDeCerrarReserva.Cerrada, ResultadoDeCerrarReserva.YaCerrada), (primera, repetida));
        var (pedido, motivo, lineas, envio, clave) = cliente.Cierres[0];
        Assert.Equal((Pedido, "entrega", (Guid?)null, "cierre-x"), (pedido, motivo, envio, clave));
        Assert.Equal([Linea], lineas);
    }

    [Fact]
    public async Task Una_linea_que_ya_salio_es_ya_salio_y_los_resultados_inciertos_no_se_toman_por_cierre()
    {
        var cliente = new ClienteFalso();
        var adaptador = new RestReservasDelPedido(cliente);

        cliente.Cierre = () => ResultadoDelInventario<CierreDeLaReserva>.Rechazo(409, "INV-RESERVA-ESTADO-INVALIDO");
        var salio = await adaptador.Cerrar(Pedido, Linea, "c");
        var inciertos = new List<ResultadoDeCerrarReserva>();
        foreach (var respuesta in new Func<ResultadoDelInventario<CierreDeLaReserva>>[]
        {
            () => ResultadoDelInventario<CierreDeLaReserva>.SinRespuesta,
            () => ResultadoDelInventario<CierreDeLaReserva>.Limitada("INV-LIMITE-EXCEDIDO"),
            () => ResultadoDelInventario<CierreDeLaReserva>.Rechazo(409, "INV-IDEMPOTENCIA-EN-CURSO"),
            () => ResultadoDelInventario<CierreDeLaReserva>.Rechazo(409, "INV-IDEMPOTENCIA-EN-CONFLICTO"),
            () => ResultadoDelInventario<CierreDeLaReserva>.Rechazo(401, "INV-NO-AUTENTICADO"),
            () => Cerrada("desconocido"),
            () => Cerrada("cerrada", pedido: Guid.NewGuid()),
            () => Cerrada("cerrada", linea: Guid.NewGuid()),
            () => throw new HttpRequestException("caído"),
        })
        {
            cliente.Cierre = respuesta;
            inciertos.Add(await adaptador.Cerrar(Pedido, Linea, "c"));
        }

        cliente.Cierre = () => ResultadoDelInventario<CierreDeLaReserva>.Rechazo(400, "INV-VALIDACION-INVALIDA");
        var rechazada = await adaptador.Cerrar(Pedido, Linea, "c");

        Assert.Equal(ResultadoDeCerrarReserva.YaSalio, salio);
        Assert.All(inciertos, r => Assert.Equal(ResultadoDeCerrarReserva.Incierto, r));
        Assert.Equal(ResultadoDeCerrarReserva.Rechazada, rechazada);
    }

    [Fact]
    public async Task Liberar_manda_el_motivo_y_solo_las_lineas_dadas_y_traduce_cada_resultado()
    {
        var cliente = new ClienteFalso();
        var adaptador = new RestReservasDelPedido(cliente);
        var reserva = new ReservaDelInventario(Pedido, "liberada", Ahora, null, [], []);

        cliente.Liberacion = () => ResultadoDelInventario<ReservaDelInventario>.ConValor(reserva);
        var liberada = await adaptador.Liberar(Pedido, "pedido-anulado", [Linea]);
        cliente.Liberacion = () => ResultadoDelInventario<ReservaDelInventario>.Rechazo(404, "INV-NO-ENCONTRADO");
        var noExistia = await adaptador.Liberar(Pedido, "no-recogido", [Linea]);
        cliente.Liberacion = () => ResultadoDelInventario<ReservaDelInventario>.Rechazo(409, "INV-RESERVA-ESTADO-INVALIDO");
        var yaCerrada = await adaptador.Liberar(Pedido, "no-recogido", [Linea]);
        cliente.Liberacion = () => throw new TimeoutException();
        var lanza = await adaptador.Liberar(Pedido, "no-recogido", [Linea]);
        cliente.Liberacion = () => ResultadoDelInventario<ReservaDelInventario>.Limitada("INV-LIMITE-EXCEDIDO");
        var limitada = await adaptador.Liberar(Pedido, "no-recogido", [Linea]);

        Assert.Equal((ResultadoDeLiberarReserva.Liberada, ResultadoDeLiberarReserva.NoExistia, ResultadoDeLiberarReserva.YaCerrada), (liberada, noExistia, yaCerrada));
        Assert.Equal((ResultadoDeLiberarReserva.NoSePudo, ResultadoDeLiberarReserva.NoSePudo), (lanza, limitada));
        Assert.Equal("pedido-anulado", cliente.Liberaciones[0].Motivo);
        Assert.Equal([Linea], cliente.Liberaciones[0].Lineas);
    }

    // ---------- Límites ----------

    private sealed class LimitesFalsos : IControlDeLimites
    {
        public List<(ClaveDePolitica Politica, string? Sujeto)> Llamadas { get; } = [];

        public Func<ResultadoDeLimite> Respuesta { get; set; } = ResultadoDeLimite.PermitidoSinContar;

        public Task<ResultadoDeLimite> Reservar(ClaveDePolitica politica, string? sujeto = null, string? claveDelLlamador = null, HuellaDeSujeto? huellaDelLlamador = null, ClaveDeIdempotencia? idempotencia = null, CancellationToken cancelacion = default)
        {
            Llamadas.Add((politica, sujeto));
            return Task.FromResult(Respuesta());
        }

        public Task Liberar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Cada_operacion_usa_su_politica_con_el_usuario_como_sujeto()
    {
        var limites = new LimitesFalsos();
        var limitador = new LimitadorDePedidosSobreLimites(limites);
        var usuario = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await limitador.Consultar(usuario);
        await limitador.Gestionar(usuario);
        await limitador.LeerContacto(usuario);

        Assert.Equal(
            [
                (PoliticasDeLimite.ConsultasDePedidosPorUsuario, usuario.ToString("N")),
                (PoliticasDeLimite.GestionDePedidosPorUsuario, usuario.ToString("N")),
                (PoliticasDeLimite.LecturasDelContactoDePedidosPorPersona, usuario.ToString("N")),
            ],
            limites.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-35")]
    public async Task Un_exceso_trae_el_instante_y_sin_el_almacen_o_con_una_excepcion_no_se_sirve_nada_en_especial_el_contacto()
    {
        var limites = new LimitesFalsos { Respuesta = () => ResultadoDeLimite.Excedido("x", 30, Ahora.AddSeconds(30)) };
        var limitador = new LimitadorDePedidosSobreLimites(limites);

        var excedido = await limitador.Consultar(Guid.NewGuid());
        limites.Respuesta = ResultadoDeLimite.NoDisponible;
        var caido = await limitador.LeerContacto(Guid.NewGuid());
        var lanza = await new LimitadorDePedidosSobreLimites(new LimitesQueLanzan()).LeerContacto(Guid.NewGuid());

        Assert.Equal(new ResultadoDeLimiteDePedidos(DecisionDeLimiteDePedidos.Excedido, Ahora.AddSeconds(30)), excedido);
        Assert.Equal(DecisionDeLimiteDePedidos.NoDisponible, caido.Decision);
        Assert.Equal(DecisionDeLimiteDePedidos.NoDisponible, lanza.Decision);
    }

    private sealed class LimitesQueLanzan : IControlDeLimites
    {
        public Task<ResultadoDeLimite> Reservar(ClaveDePolitica politica, string? sujeto = null, string? claveDelLlamador = null, HuellaDeSujeto? huellaDelLlamador = null, ClaveDeIdempotencia? idempotencia = null, CancellationToken cancelacion = default) =>
            throw new InvalidOperationException("sin almacén");

        public Task Liberar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    // ---------- Auditoría del contacto ----------

    private sealed class AuditoriaEspia : IAuditoria
    {
        public List<SolicitudDeRegistro> Solicitudes { get; } = [];

        public bool Cae { get; set; }

        public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
        {
            if (Cae)
            {
                throw new AuditoriaNoDisponibleException();
            }

            Solicitudes.Add(solicitud);
            return Task.CompletedTask;
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PED-34")]
    public async Task La_lectura_del_contacto_se_audita_con_el_pedido_y_el_rol_y_si_no_se_puede_auditar_la_excepcion_sube()
    {
        var auditoria = new AuditoriaEspia();
        var adaptador = new AuditoriaDeContactoSobreAuditoria(auditoria);

        await adaptador.ContactoConsultado(Pedido, "personal-de-logistica");
        auditoria.Cae = true;

        var solicitud = Assert.Single(auditoria.Solicitudes);
        Assert.Equal((AccionesAuditables.ContactoDePedidoConsultado, "pedido", Pedido.ToString("D")), (solicitud.Accion, solicitud.ObjetoTipo, solicitud.ObjetoId));
        Assert.Equal(["rol"], solicitud.Detalle.Keys);
        Assert.Equal("personal-de-logistica", solicitud.Detalle["rol"]);
        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => adaptador.ContactoConsultado(Pedido, "administrador-central"));
    }

    // ---------- Composición ----------

    [Fact]
    public void Con_lo_de_Plataforma_y_Auditoria_registrado_se_usan_los_adaptadores_reales_y_la_guarda_ya_no_los_nombra()
    {
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IClienteDelInventario>(new ClienteFalso());
        servicios.AddSingleton<IControlDeLimites>(new LimitesFalsos());
        servicios.AddSingleton<IAuditoria>(new AuditoriaEspia());

        var error = Assert.Throws<InvalidOperationException>(() => servicios.AgregarPedidos(esProduccion: true));

        foreach (var real in new[] { nameof(IReservasDelPedido), nameof(ILimitadorDePedidos), nameof(IAuditoriaDeContacto) })
        {
            Assert.DoesNotContain(real, error.Message, StringComparison.Ordinal);
        }

        Assert.Contains("IAccesosDeBiblioteca", error.Message, StringComparison.Ordinal);
        // La sucursal ya se resuelve por su código (T30): deja de ser un doble provisional.
        Assert.DoesNotContain("IResolucionDeSucursal", error.Message, StringComparison.Ordinal);
        Assert.Contains(servicios, d => d.ServiceType == typeof(IResolucionDeSucursal) && d.ImplementationType == typeof(CodigoResolucionDeSucursal));
        Assert.Contains(servicios, d => d.ServiceType == typeof(IReservasDelPedido) && d.ImplementationType == typeof(RestReservasDelPedido));
        Assert.Contains(servicios, d => d.ServiceType == typeof(ILimitadorDePedidos) && d.ImplementationType == typeof(LimitadorDePedidosSobreLimites));
        Assert.Contains(servicios, d => d.ServiceType == typeof(IAuditoriaDeContacto) && d.ImplementationType == typeof(AuditoriaDeContactoSobreAuditoria));
    }
}
