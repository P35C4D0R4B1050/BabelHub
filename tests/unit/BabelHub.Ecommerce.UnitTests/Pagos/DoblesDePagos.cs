using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>La cuenta del pagador: puede pagar, tiene la cancelación en ejecución o cae. Anota el orden de las lecturas y si alguna ocurrió dentro de una transacción.</summary>
internal sealed class CuentaDelPagadorDoble(UnidadDeTrabajoReversible unidad, List<string> orden) : ICuentaDelPagador
{
    public EstadoDeLaCuentaParaPagar? Estado { get; set; } = new(true, null);

    public bool Falla { get; set; }

    public bool CancelacionEnCurso { get; set; }

    public string? Correo { get; set; } = "ana@correo.com";

    public int ConsultasDentroDeUnaTransaccion { get; private set; }

    public int Consultas { get; private set; }

    public int MarcasLeidasConBloqueo { get; private set; }

    public Task<EstadoDeLaCuentaParaPagar?> Consultar(Guid cuentaId, CancellationToken cancelacion = default)
    {
        Consultas++;
        orden.Add("cuenta");
        if (unidad.EnCurso)
        {
            ConsultasDentroDeUnaTransaccion++;
        }

        return Falla ? throw new InvalidOperationException("Identidad no responde.") : Task.FromResult(Estado);
    }

    /// <summary>Lo que ocurre justo al leer la marca (por ejemplo, que otra solicitud desvincule la intención entre las dos lecturas).</summary>
    public Action? AlLeerLaMarca { get; set; }

    public Task<bool> CancelacionEnEjecucion(Guid cuentaId, bool bloquear, CancellationToken cancelacion = default)
    {
        orden.Add(bloquear ? "marca-con-bloqueo" : "marca");
        AlLeerLaMarca?.Invoke();
        if (bloquear)
        {
            MarcasLeidasConBloqueo++;
        }

        return Falla ? throw new InvalidOperationException("Identidad no responde.") : Task.FromResult(CancelacionEnCurso);
    }

    public Task<string?> CorreoDelTitular(Guid cuentaId, CancellationToken cancelacion = default) => Task.FromResult(Correo);
}

internal sealed class PosesionDeEdicionesDoble : IPosesionDeEdiciones
{
    public ResultadoDePosesion Resultado { get; set; } = ResultadoDePosesion.NoPosee;

    public bool Falla { get; set; }

    public List<IReadOnlyCollection<Guid>> Consultadas { get; } = [];

    public Task<ResultadoDePosesion> Posee(Guid cuentaId, IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        Consultadas.Add(edicionIds);
        return Falla ? throw new InvalidOperationException("Biblioteca no responde.") : Task.FromResult(Resultado);
    }
}

/// <summary>Las reservas del Inventario: respuestas configurables y el registro de cuántas llamadas ocurrieron dentro de una transacción (no debe haber ninguna, D03).</summary>
internal sealed class ReservasDelPagoDoble(UnidadDeTrabajoReversible unidad, RelojFijo reloj) : IReservasDelPago
{
    public RespuestaDeReserva<EstadoDeLaReserva>? Consulta { get; set; }

    public RespuestaDeReserva<ResultadoDeExtension>? Extension { get; set; }

    public RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>? Confirmacion { get; set; }

    public RespuestaDeReserva<bool>? Liberacion { get; set; }

    public bool Falla { get; set; }

    public int LlamadasDentroDeUnaTransaccion { get; private set; }

    /// <summary>Los instantes de expiración que devolvió el Inventario en cada pedido de extensión.</summary>
    public List<DateTimeOffset> Extensiones { get; } = [];

    public List<string> Confirmaciones { get; } = [];

    public List<MotivoDeLiberacion> Liberaciones { get; } = [];

    public int Consultas { get; private set; }

    private void Anotar()
    {
        if (unidad.EnCurso)
        {
            LlamadasDentroDeUnaTransaccion++;
        }

        if (Falla)
        {
            throw new InvalidOperationException("El Inventario no responde.");
        }
    }

    public Task<RespuestaDeReserva<EstadoDeLaReserva>> Consultar(Guid pedidoId, Guid cuentaId, CancellationToken cancelacion = default)
    {
        Consultas++;
        Anotar();
        return Task.FromResult(Consulta ?? new RespuestaDeReserva<EstadoDeLaReserva>(ResultadoDeLaLlamadaDeReserva.Correcto, new EstadoDeLaReserva(reloj.Ahora.AddMinutes(10), true)));
    }

    public Task<RespuestaDeReserva<ResultadoDeExtension>> Extender(Guid pedidoId, CancellationToken cancelacion = default)
    {
        Anotar();
        var respuesta = Extension ?? new RespuestaDeReserva<ResultadoDeExtension>(ResultadoDeLaLlamadaDeReserva.Correcto, new ResultadoDeExtension(IntencionesDePrueba.Inicio.AddMinutes(30), true));
        if (respuesta.Valor is { } valor)
        {
            Extensiones.Add(valor.ExpiraEn);
        }

        return Task.FromResult(respuesta);
    }

    /// <summary>Lo que ocurre justo al llamar a <c>reservas.confirmar</c> (por ejemplo, anotar el estado de la intención en ese momento).</summary>
    public Action? AlConfirmar { get; set; }

    public Task<RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>> Confirmar(Guid pedidoId, string claveDeIdempotencia, CancellationToken cancelacion = default)
    {
        Anotar();
        Confirmaciones.Add(claveDeIdempotencia);
        AlConfirmar?.Invoke();
        return Task.FromResult(Confirmacion ?? new RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>(ResultadoDeLaLlamadaDeReserva.Correcto, []));
    }

    public Task<RespuestaDeReserva<bool>> Liberar(Guid pedidoId, MotivoDeLiberacion motivo, CancellationToken cancelacion = default)
    {
        Anotar();
        Liberaciones.Add(motivo);
        return Task.FromResult(Liberacion ?? new RespuestaDeReserva<bool>(ResultadoDeLaLlamadaDeReserva.Correcto, true));
    }
}

/// <summary>El motor de límites del cobro: permite todo, excede o cae, y anota cuántas reservas se pidieron.</summary>
internal sealed class LimitesDeCobroDoble : IControlDeLimites
{
    public bool Excedido { get; set; }

    public bool Caido { get; set; }

    public int Reservas { get; private set; }

    public Task<ResultadoDeLimite> Reservar(ClaveDePolitica politica, string? sujeto = null, string? claveDelLlamador = null, HuellaDeSujeto? huellaDelLlamador = null, ClaveDeIdempotencia? idempotencia = null, CancellationToken cancelacion = default)
    {
        Reservas++;
        if (Caido)
        {
            return Task.FromResult(ResultadoDeLimite.NoDisponible());
        }

        return Task.FromResult(Excedido
            ? ResultadoDeLimite.Excedido(politica.Valor, 900, DateTimeOffset.UtcNow.AddSeconds(900))
            : ResultadoDeLimite.Permitido(new ReservaDeLimite(politica.Valor, "sujeto", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow)));
    }

    public Task Liberar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;
}

/// <summary>La pasarela: responde lo que la prueba fije, puede tardar más que el plazo o fallar, y anota lo que recibió y si la llamaron dentro de una transacción.</summary>
internal sealed class PasarelaDePagoDoble(UnidadDeTrabajoReversible unidad) : IPasarelaDePago
{
    public Func<SolicitudDeCobroEnPasarela, ResultadoDeCobro>? Respuesta { get; set; }

    public bool NoResponde { get; set; }

    public bool Lanza { get; set; }

    public List<SolicitudDeCobroEnPasarela> Solicitudes { get; } = [];

    public int LlamadasDentroDeUnaTransaccion { get; private set; }

    public async Task<ResultadoDeCobro> Cobrar(SolicitudDeCobroEnPasarela solicitud, CancellationToken cancelacion = default)
    {
        Solicitudes.Add(solicitud);
        if (unidad.EnCurso)
        {
            LlamadasDentroDeUnaTransaccion++;
        }

        if (Lanza)
        {
            throw new HttpRequestException("La pasarela falló.");
        }

        if (NoResponde)
        {
            await Task.Delay(Timeout.Infinite, cancelacion);
        }

        return Respuesta is { } f ? f(solicitud) : ResultadoDeCobro.Aprobado("ch_1", solicitud.Importe, "PEN", solicitud.ReferenciaOpaca);
    }

    /// <summary>Lo que responde la pasarela al consultar un cargo (por defecto, que no lo conoce).</summary>
    public Func<Guid, ResultadoDeCobro>? RespuestaDeConsulta { get; set; }

    public bool ConsultaLanza { get; set; }

    public List<Guid> Consultas { get; } = [];

    public int ConsultasDentroDeUnaTransaccion { get; private set; }

    /// <summary>Cómo se lee un aviso entrante (por defecto, no se entiende).</summary>
    public Func<ReadOnlyMemory<byte>, AvisoDeLaPasarela?>? Interprete { get; set; }

    public AvisoDeLaPasarela? InterpretarAviso(ReadOnlyMemory<byte> cuerpo) => Interprete?.Invoke(cuerpo);

    public Task<ResultadoDeCobro> ConsultarEstado(Guid referenciaOpaca, CancellationToken cancelacion = default)
    {
        Consultas.Add(referenciaOpaca);
        if (unidad.EnCurso)
        {
            ConsultasDentroDeUnaTransaccion++;
        }

        if (ConsultaLanza)
        {
            throw new HttpRequestException("La pasarela falló.");
        }

        return Task.FromResult(RespuestaDeConsulta is { } f ? f(referenciaOpaca) : ResultadoDeCobro.SinRespuesta());
    }

    /// <summary>Lo que responde la pasarela a un pedido de reembolso (por defecto, lo aplica).</summary>
    public Func<Guid, ResultadoDeReembolsoEnPasarela>? RespuestaDeReembolso { get; set; }

    /// <summary>Los reembolsos que la pasarela dice tener del cargo; <c>null</c> si no se puede consultar.</summary>
    public IReadOnlyList<ReembolsoEnPasarela>? ReembolsosDelCargo { get; set; } = [];

    public bool ReembolsarLanza { get; set; }

    public List<(Guid ReembolsoId, string Cargo, Importe Monto)> Reembolsos { get; } = [];

    public int ConsultasDeReembolsos { get; private set; }

    public int ReembolsosDentroDeUnaTransaccion { get; private set; }

    public Task<ResultadoDeReembolsoEnPasarela> Reembolsar(Guid reembolsoId, string referenciaDelCargo, Importe monto, CancellationToken cancelacion = default)
    {
        Reembolsos.Add((reembolsoId, referenciaDelCargo, monto));
        if (unidad.EnCurso)
        {
            ReembolsosDentroDeUnaTransaccion++;
        }

        if (ReembolsarLanza)
        {
            throw new HttpRequestException("La pasarela falló.");
        }

        return Task.FromResult(RespuestaDeReembolso is { } f ? f(reembolsoId) : ResultadoDeReembolsoEnPasarela.Aplicado("re_1"));
    }

    public Task<IReadOnlyList<ReembolsoEnPasarela>?> ConsultarReembolsos(string referenciaDelCargo, CancellationToken cancelacion = default)
    {
        ConsultasDeReembolsos++;
        if (unidad.EnCurso)
        {
            ReembolsosDentroDeUnaTransaccion++;
        }

        return Task.FromResult(ReembolsosDelCargo);
    }
}

/// <summary>Los reembolsos de toda la plataforma en memoria: copias, clave natural única por origen y reversión con la unidad de trabajo.</summary>
internal sealed class RepositorioDeReembolsosEnMemoria(RepositorioDePagosEnMemoria pagos) : IRepositorioDeReembolsos, IInstantanea
{
    private Dictionary<Guid, Reembolso> _reembolsos = [];

    public IReadOnlyCollection<Reembolso> Todos => _reembolsos.Values;

    /// <summary>Hace fallar toda escritura de un reembolso existente (la base cae tras pedirlo a la pasarela).</summary>
    public bool GuardarLanza { get; set; }

    public int LecturasConBloqueo { get; private set; }

    public Task<Reembolso?> Obtener(Guid reembolsoId, bool paraActualizar, CancellationToken cancelacion = default)
    {
        if (paraActualizar)
        {
            LecturasConBloqueo++;
        }

        return Task.FromResult(_reembolsos.TryGetValue(reembolsoId, out var r) ? r.Copiar() : null);
    }

    public Task<Reembolso?> BuscarPorOrigen(OrigenDeReembolso origen, string claveNatural, CancellationToken cancelacion = default) =>
        Task.FromResult(_reembolsos.Values.FirstOrDefault(r => r.Origen == origen && r.ClaveNatural == claveNatural) is { } e ? e.Copiar() : null);

    public Task<IReadOnlyList<Reembolso>> ListarDeLaIntencion(Guid intencionId, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<Reembolso>>(_reembolsos.Values.Where(r => r.IntencionId == intencionId).Select(r => r.Copiar()).ToArray());

    public Task Agregar(Reembolso reembolso, CancellationToken cancelacion = default)
    {
        if (_reembolsos.Values.Any(r => r.Origen == reembolso.Origen && r.ClaveNatural == reembolso.ClaveNatural))
        {
            throw new InvalidOperationException("La clave natural del reembolso ya existe.");
        }

        _reembolsos.Add(reembolso.Id, reembolso.Copiar());
        return Task.CompletedTask;
    }

    public Task Guardar(Reembolso reembolso, CancellationToken cancelacion = default)
    {
        if (GuardarLanza)
        {
            throw new InvalidOperationException("La base no responde.");
        }

        _reembolsos[reembolso.Id] = reembolso.Copiar();
        return Task.CompletedTask;
    }

    public Task<int> ContarPendientesDeLaCuenta(Guid cuentaId, OrigenDeReembolso origen, CancellationToken cancelacion = default) =>
        Task.FromResult(_reembolsos.Values.Count(r => r.Origen == origen && r.CuentaComoPendiente && pagos.Intenciones.Any(i => i.Id == r.IntencionId && i.CuentaId == cuentaId)));

    public Task<(IReadOnlyList<Reembolso> Reembolsos, int Total)> ListarPendientes(int pagina, int tamano, CancellationToken cancelacion = default)
    {
        var pendientes = _reembolsos.Values.Where(r => r.CuentaComoPendiente).OrderBy(r => r.CreadoEn).ToArray();
        return Task.FromResult(((IReadOnlyList<Reembolso>)pendientes.Skip((pagina - 1) * tamano).Take(tamano).Select(r => r.Copiar()).ToArray(), pendientes.Length));
    }

    public Task<IReadOnlyList<Guid>> ListarHuerfanos(DateTimeOffset ahora, int limite, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(_reembolsos.Values.Where(r => r.EsHuerfano(ahora)).OrderBy(r => r.CreadoEn).Select(r => r.Id).Take(limite).ToArray());

    public Task<IReadOnlyList<Guid>> ListarReintentables(DateTimeOffset ahora, int limite, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(_reembolsos.Values.Where(r => r.PuedeReintentarse(ahora)).OrderBy(r => r.CreadoEn).Select(r => r.Id).Take(limite).ToArray());

    public object Capturar() => _reembolsos.ToDictionary(p => p.Key, p => p.Value.Copiar());

    public void Restaurar(object instantanea) => _reembolsos = (Dictionary<Guid, Reembolso>)instantanea;
}

/// <summary>La confirmación de la compra aprobada (T14): anota la llamada y si ocurrió dentro de una transacción.</summary>
internal sealed class ConfirmadorDeCompraDoble(UnidadDeTrabajoReversible unidad) : IConfirmadorDeCompraAprobada
{
    public List<(Guid PedidoId, Guid IntentoId, string Referencia)> Llamadas { get; } = [];

    public int LlamadasDentroDeUnaTransaccion { get; private set; }

    public Task<ResultadoDelCobroDelCliente> Confirmar(Guid pedidoId, Guid intentoId, string referenciaDePasarela, CancellationToken cancelacion = default)
    {
        Llamadas.Add((pedidoId, intentoId, referenciaDePasarela));
        if (unidad.EnCurso)
        {
            LlamadasDentroDeUnaTransaccion++;
        }

        return Task.FromResult(new ResultadoDelCobroDelCliente(Application.Pagos.EstadoDelCobro.Aprobado, null, intentoId));
    }
}

/// <summary>Lo que Pedidos implementa (D01): guarda lo que recibió, anota si lo llamaron dentro de la transacción y devuelve lo que la prueba fije o lanza.</summary>
internal sealed class ConfirmacionDeCompraDoble(UnidadDeTrabajoReversible unidad) : IConfirmacionDeCompra
{
    public ResultadoDeConfirmarCompra Resultado { get; set; } = ResultadoDeConfirmarCompra.Creado;

    public bool Lanza { get; set; }

    public List<CompraAprobada> Compras { get; } = [];

    public int LlamadasDentroDeUnaTransaccion { get; private set; }

    public Task<ResultadoDeConfirmarCompra> Confirmar(CompraAprobada compra, CancellationToken cancelacion = default)
    {
        Compras.Add(compra);
        if (unidad.EnCurso)
        {
            LlamadasDentroDeUnaTransaccion++;
        }

        return Lanza ? throw new InvalidOperationException("Pedidos cayó a mitad de la transacción.") : Task.FromResult(Resultado);
    }
}

/// <summary>El paso (c) de la confirmación (T15): devuelve lo que la prueba fije y anota si lo llamaron dentro de una transacción.</summary>
internal sealed class CompletadorDeCompraDoble(UnidadDeTrabajoReversible unidad) : ICompletadorDeCompra
{
    public ResultadoDeCompletarCompra Resultado { get; set; } = new(TipoDeCompletar.Completada);

    public List<(Guid PedidoId, Guid IntentoId)> Llamadas { get; } = [];

    public int LlamadasDentroDeUnaTransaccion { get; private set; }

    public Task<ResultadoDeCompletarCompra> Completar(Guid pedidoId, Guid intentoId, CancellationToken cancelacion = default)
    {
        Llamadas.Add((pedidoId, intentoId));
        if (unidad.EnCurso)
        {
            LlamadasDentroDeUnaTransaccion++;
        }

        return Task.FromResult(Resultado);
    }
}

/// <summary>El orquestador de la compensación (T16): anota con qué motivo y variante lo llamaron.</summary>
internal sealed class CompensadorDeCompraDoble(UnidadDeTrabajoReversible unidad) : ICompensadorDeCompra
{
    public List<(Guid PedidoId, Guid IntentoId, MotivoDeCompensacion Motivo, VarianteDeCompensacion Variante)> Llamadas { get; } = [];

    public int LlamadasDentroDeUnaTransaccion { get; private set; }

    public bool Lanza { get; set; }

    public Task Compensar(Guid pedidoId, Guid intentoId, MotivoDeCompensacion motivo, VarianteDeCompensacion variante, CancellationToken cancelacion = default)
    {
        Llamadas.Add((pedidoId, intentoId, motivo, variante));
        if (unidad.EnCurso)
        {
            LlamadasDentroDeUnaTransaccion++;
        }

        return Lanza ? throw new InvalidOperationException("La compensación cayó.") : Task.CompletedTask;
    }
}

internal sealed class AlertasTecnicasEspia : IPublicadorDeAlertasTecnicas
{
    public List<AlertaTecnica> Publicadas { get; } = [];

    public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
    {
        Publicadas.Add(alerta);
        return Task.CompletedTask;
    }
}

/// <summary>El envío de avisos a Carrito doblado: anota las intenciones avisadas y los pendientes liberados, y si lo llamaron dentro de una transacción.</summary>
internal sealed class AvisadorACarritoDoble(UnidadDeTrabajoReversible unidad) : IAvisadorACarrito
{
    public List<Guid> Enviados { get; } = [];

    public List<(Guid PedidoId, Guid IntentoId)> Liberados { get; } = [];

    public int LlamadasDentroDeUnaTransaccion { get; private set; }

    public Task Enviar(Guid pedidoId, CancellationToken cancelacion = default)
    {
        Enviados.Add(pedidoId);
        if (unidad.EnCurso)
        {
            LlamadasDentroDeUnaTransaccion++;
        }

        return Task.CompletedTask;
    }

    public Task LiberarElPendienteRetenido(Guid pedidoId, Guid intentoId, CancellationToken cancelacion = default)
    {
        Liberados.Add((pedidoId, intentoId));
        return Task.CompletedTask;
    }
}

/// <summary>Carrito: responde lo que la prueba fije (o cae) y anota cada aviso recibido con sus instantes.</summary>
internal sealed class ProgresoDelCheckoutDoble(UnidadDeTrabajoReversible unidad) : IProgresoDelCheckout
{
    public ResultadoDelAvisoDePago Respuesta { get; set; } = ResultadoDelAvisoDePago.Aplicado;

    public bool Falla { get; set; }

    public List<string> Recibidos { get; } = [];

    public List<(DateTimeOffset LimiteEn, DateTimeOffset ReservaExpiraEn)> InstantesDePendiente { get; } = [];

    public int LlamadasDentroDeUnaTransaccion { get; private set; }

    public Task<ResultadoDelAvisoDePago> PagoPendiente(Guid pedidoId, DateTimeOffset limiteEn, DateTimeOffset reservaExpiraEn, CancellationToken cancelacion = default)
    {
        InstantesDePendiente.Add((limiteEn, reservaExpiraEn));
        return Anotar("pendiente");
    }

    public Task<ResultadoDelAvisoDePago> PagoAprobado(Guid pedidoId, CancellationToken cancelacion = default) => Anotar("aprobado");

    public Task<ResultadoDelAvisoDePago> PagoResuelto(Guid pedidoId, MotivoDePagoResueltoDeCompra motivo, CancellationToken cancelacion = default) => Anotar($"resuelto:{motivo}");

    private Task<ResultadoDelAvisoDePago> Anotar(string aviso)
    {
        Recibidos.Add(aviso);
        if (unidad.EnCurso)
        {
            LlamadasDentroDeUnaTransaccion++;
        }

        return Falla ? throw new HttpRequestException("Carrito no responde.") : Task.FromResult(Respuesta);
    }
}

/// <summary>El procesador de entradas de integración de Plataforma doblado: verifica «la firma» (un interruptor), deduplica por identificador y libera el reclamo si la transacción se revierte.</summary>
internal sealed class ProcesadorDeEntradasDoble : IProcesadorDeEntradasDeIntegracion, IInstantanea
{
    private HashSet<string> _reclamadas = [];
    private HashSet<string> _completadas = [];

    public bool FirmaValida { get; set; } = true;

    /// <summary>Fuerza que otra llamada con el mismo identificador siga en curso.</summary>
    public bool EnCurso { get; set; }

    public List<EntradaDeIntegracion> Recibidas { get; } = [];

    public IReadOnlyCollection<string> Completadas => _completadas;

    public Task<ResultadoDeEntradaDeIntegracion> Recibir(EntradaDeIntegracion entrada, CancellationToken cancelacion = default)
    {
        Recibidas.Add(entrada);
        if (!FirmaValida || string.IsNullOrEmpty(entrada.IdDeEntrada))
        {
            return Task.FromResult(new ResultadoDeEntradaDeIntegracion(EstadoDeEntradaDeIntegracion.Rechazada));
        }

        if (_completadas.Contains(entrada.IdDeEntrada))
        {
            return Task.FromResult(new ResultadoDeEntradaDeIntegracion(EstadoDeEntradaDeIntegracion.Repetida));
        }

        if (EnCurso || !_reclamadas.Add(entrada.IdDeEntrada))
        {
            return Task.FromResult(new ResultadoDeEntradaDeIntegracion(EstadoDeEntradaDeIntegracion.EnCurso));
        }

        return Task.FromResult(new ResultadoDeEntradaDeIntegracion(EstadoDeEntradaDeIntegracion.Aceptada));
    }

    public Task Completar(string proveedor, string idDeEntrada, ResultadoMinimo resultado, CancellationToken cancelacion = default)
    {
        _completadas.Add(idDeEntrada);
        return Task.CompletedTask;
    }

    public object Capturar() => (new HashSet<string>(_reclamadas), new HashSet<string>(_completadas));

    public void Restaurar(object instantanea)
    {
        var (reclamadas, completadas) = ((HashSet<string>, HashSet<string>))instantanea;
        _reclamadas = reclamadas;
        _completadas = completadas;
    }
}
