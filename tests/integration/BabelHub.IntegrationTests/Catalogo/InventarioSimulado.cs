using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>
/// El Inventario simulado de las pruebas del catálogo (T21): un <see cref="IClienteDelInventario"/> en memoria con la semántica del contrato (clave de idempotencia: la misma clave con la misma carga devuelve el mismo ingreso y con otra carga es un
/// conflicto 409) y fallas a pedido: <see cref="Caido"/> (nada responde), <see cref="AplicarYNoResponder"/> (el ingreso se aplica y la respuesta se pierde, como un tiempo agotado tras aplicarse), un rechazo fijo, un 429 y un retraso.
/// </summary>
public sealed class InventarioSimulado(IActorActual actores) : IClienteDelInventario
{
    private readonly object _candado = new();
    private readonly HashSet<Guid> _registradas = [];
    private readonly List<IngresoRegistrado> _ingresos = [];
    private readonly Dictionary<string, (IngresoARegistrar Carga, IngresoRegistrado Ingreso)> _porClave = [];

    /// <summary>Nada responde: toda llamada es «sin respuesta».</summary>
    public volatile bool Caido;

    /// <summary>Solo <c>ingresos.registrar</c> y <c>ingresos.listar</c> dejan de responder (el resto funciona).</summary>
    public volatile bool IngresosCaidos;

    /// <summary>Solo <c>ingresos.listar</c> deja de responder.</summary>
    public volatile bool ListaCaida;

    /// <summary>Las próximas N llamadas de <c>ingresos.registrar</c> aplican el ingreso y pierden la respuesta.</summary>
    public int AplicarYNoResponder;

    /// <summary>Las próximas N llamadas de <c>ingresos.registrar</c> se rechazan con este estado y código sin aplicar nada.</summary>
    public (int Veces, int Estado, string? Codigo)? Rechazo;

    /// <summary>Las próximas N llamadas de <c>ingresos.registrar</c> devuelven 429.</summary>
    public int Limitar;

    /// <summary>Las llamadas de <c>ingresos.registrar</c> esperan esto antes de responder (para dos envíos simultáneos).</summary>
    public TimeSpan Retraso;

    public int LlamadasDeRegistrarEdicion;

    public int LlamadasDeRegistrarIngreso;

    public int LlamadasDeDisponibilidad;

    public int LlamadasDeListar;

    public IReadOnlyCollection<Guid> Registradas
    {
        get
        {
            lock (_candado)
            {
                return [.. _registradas];
            }
        }
    }

    public IReadOnlyList<IngresoRegistrado> Ingresos
    {
        get
        {
            lock (_candado)
            {
                return [.. _ingresos];
            }
        }
    }

    public int StockDe(Guid edicionId)
    {
        lock (_candado)
        {
            return _ingresos.Where(i => i.EdicionId == edicionId).Sum(i => i.Cantidad);
        }
    }

    /// <summary>Un ingreso aplicado por otra vía (otro usuario, otro canal): lo encuentra la conciliación sin que haya una clave de este lado.</summary>
    public IngresoRegistrado SembrarIngreso(Guid edicionId, int cantidad, Guid? usuario, DateTimeOffset instante, string? referencia = null)
    {
        var ingreso = new IngresoRegistrado(Guid.NewGuid(), edicionId, "LIMA", cantidad, referencia, null, usuario, instante);
        lock (_candado)
        {
            _registradas.Add(edicionId);
            _ingresos.Add(ingreso);
        }

        return ingreso;
    }

    public Task<ResultadoDelInventario> RegistrarEdicion(EdicionARegistrar edicion, CancellationToken cancelacion = default)
    {
        Interlocked.Increment(ref LlamadasDeRegistrarEdicion);
        if (Caido)
        {
            return Task.FromResult(ResultadoDelInventario.SinRespuesta);
        }

        lock (_candado)
        {
            _registradas.Add(edicion.EdicionId);
        }

        return Task.FromResult(ResultadoDelInventario.Correcta);
    }

    // Operaciones de la cuenta del titular (identidad-seguridad): el catálogo no las usa, el simulado no las atiende.
    public Task<ResultadoDelInventario<PendientesDeTitular>> ConsultarPendientes(Guid titularId, CancellationToken cancelacion = default) =>
        Task.FromResult(ResultadoDelInventario<PendientesDeTitular>.SinRespuesta);

    public Task<ResultadoDelInventario> DarDeBajaAlTitular(Guid titularId, CancellationToken cancelacion = default) =>
        Task.FromResult(ResultadoDelInventario.SinRespuesta);

    public Task<ResultadoDelInventario<DisponibilidadDeVariasEdiciones>> ConsultarDisponibilidad(IReadOnlyList<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        Interlocked.Increment(ref LlamadasDeDisponibilidad);
        if (Caido)
        {
            return Task.FromResult(ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.SinRespuesta);
        }

        lock (_candado)
        {
            var conocidas = edicionIds.Where(_registradas.Contains).Select(id =>
            {
                var vendible = _ingresos.Where(i => i.EdicionId == id).Sum(i => i.Cantidad);
                return new DisponibilidadDeLaEdicion(id, vendible, vendible > 0 ? [new FuenteConVendible("LIMA", "Lima", vendible, "disponible")] : [], DateTimeOffset.UtcNow);
            }).ToList();
            return Task.FromResult(ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.ConValor(new DisponibilidadDeVariasEdiciones(conocidas, [.. edicionIds.Where(id => !_registradas.Contains(id))])));
        }
    }

    public async Task<ResultadoDelInventario<IngresoRegistrado>> RegistrarIngreso(IngresoARegistrar ingreso, string claveDeIdempotencia, CancellationToken cancelacion = default)
    {
        Interlocked.Increment(ref LlamadasDeRegistrarIngreso);
        if (Retraso > TimeSpan.Zero)
        {
            await Task.Delay(Retraso, cancelacion).ConfigureAwait(false);
        }

        if (Caido || IngresosCaidos)
        {
            return ResultadoDelInventario<IngresoRegistrado>.SinRespuesta;
        }

        if (Consumir(ref Limitar))
        {
            return ResultadoDelInventario<IngresoRegistrado>.Limitada("INV-LIMITE-EXCEDIDO");
        }

        if (Rechazo is { Veces: > 0 } fijo)
        {
            Rechazo = fijo with { Veces = fijo.Veces - 1 };
            return ResultadoDelInventario<IngresoRegistrado>.Rechazo(fijo.Estado, fijo.Codigo);
        }

        IngresoRegistrado registrado;
        lock (_candado)
        {
            if (_porClave.TryGetValue(claveDeIdempotencia, out var previo))
            {
                return previo.Carga == ingreso
                    ? ResultadoDelInventario<IngresoRegistrado>.ConValor(previo.Ingreso)
                    : ResultadoDelInventario<IngresoRegistrado>.Rechazo(409, "INV-IDEMPOTENCIA-EN-CONFLICTO");
            }

            if (!_registradas.Contains(ingreso.EdicionId))
            {
                return ResultadoDelInventario<IngresoRegistrado>.Rechazo(404, "INV-EDICION-NO-ENCONTRADA");
            }

            registrado = new IngresoRegistrado(Guid.NewGuid(), ingreso.EdicionId, ingreso.CodigoDeFuente ?? "LIMA", ingreso.Cantidad, ingreso.Referencia, ingreso.Nota, actores.Actual?.UsuarioId, DateTimeOffset.UtcNow);
            _ingresos.Add(registrado);
            _porClave[claveDeIdempotencia] = (ingreso, registrado);
        }

        // El ingreso se aplicó; la respuesta puede perderse (tiempo agotado tras aplicarse).
        return Consumir(ref AplicarYNoResponder) ? ResultadoDelInventario<IngresoRegistrado>.SinRespuesta : ResultadoDelInventario<IngresoRegistrado>.ConValor(registrado);
    }

    public Task<ResultadoDelInventario<IReadOnlyList<IngresoRegistrado>>> ListarIngresos(Guid edicionId, int limite, CancellationToken cancelacion = default)
    {
        Interlocked.Increment(ref LlamadasDeListar);
        if (Caido || IngresosCaidos || ListaCaida)
        {
            return Task.FromResult(ResultadoDelInventario<IReadOnlyList<IngresoRegistrado>>.SinRespuesta);
        }

        lock (_candado)
        {
            IReadOnlyList<IngresoRegistrado> lista = [.. _ingresos.Where(i => i.EdicionId == edicionId).OrderByDescending(i => i.Instante).Take(limite)];
            return Task.FromResult(ResultadoDelInventario<IReadOnlyList<IngresoRegistrado>>.ConValor(lista));
        }
    }

public Task<ResultadoDelInventario<RespuestaDeConsultaPrevia>> ConsultarPrevia(ConsultaPrevia consulta, CancellationToken cancelacion = default) => throw new NotSupportedException();    public Task<ResultadoDelInventario<ReservaDelInventario>> Reservar(SolicitudDeReservaDeStock solicitud, string claveDeIdempotencia, CancellationToken cancelacion = default) => throw new NotSupportedException();    public Task<ResultadoDelInventario<ReservaDelInventario>> Liberar(Guid pedidoId, string motivo, IReadOnlyList<Guid>? lineaIds = null, CancellationToken cancelacion = default) => throw new NotSupportedException();
        public Task<ResultadoDelInventario<ReservaDelInventario>> ConsultarReserva(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<ExtensionDeLaReserva>> ExtenderReserva(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<ReservaDelInventario>> ConfirmarReserva(Guid pedidoId, string claveDeIdempotencia, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<CierreDeLaReserva>> CerrarReserva(Guid pedidoId, string motivo, IReadOnlyList<Guid> lineaIds, Guid? envioId, string claveDeIdempotencia, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<ResultadoDelInventario<IReadOnlyList<SucursalPublica>>> ListarSucursales(CancellationToken cancelacion = default) =>
        Task.FromResult(ResultadoDelInventario<IReadOnlyList<SucursalPublica>>.ConValor([]));

    public Task<ResultadoDelInventario<ContactoDeSucursalDelInventario>> ConsultarContactoDeSucursal(string codigoDeSucursal, CancellationToken cancelacion = default) => throw new NotSupportedException();

    private static bool Consumir(ref int contador)
    {
        while (true)
        {
            var actual = Volatile.Read(ref contador);
            if (actual <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref contador, actual - 1, actual) == actual)
            {
                return true;
            }
        }
    }
}
