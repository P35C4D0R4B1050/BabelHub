using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Domain.Traslados;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>
/// La cola de entrega en memoria, con la semántica del puerto: solo los pendientes cuya fecha llegó y cuyo arrendamiento venció, del más antiguo al más nuevo, y un estado de pedido solo si no hay otro anterior del mismo
/// traslado todavía pendiente. Comparte los mensajes con <see cref="BuzonDeSalidaEnMemoria"/>.
/// </summary>
internal sealed class ColaDeMensajesEnMemoria(BuzonDeSalidaEnMemoria buzon) : IColaDeMensajesSalientes
{
    public sealed class Estado
    {
        public string Valor { get; set; } = "pendiente";

        public int Intentos { get; set; }

        public DateTimeOffset ProximaFecha { get; set; }

        public DateTimeOffset? ArrendadoHasta { get; set; }

        public bool Alertado { get; set; }

        public string? MotivoDeFallo { get; set; }

        public DateTimeOffset? EnviadoEn { get; set; }
    }

    public Dictionary<Guid, Estado> Estados { get; } = [];

    public int Reclamos { get; private set; }

    public Estado De(Guid id)
    {
        if (!Estados.TryGetValue(id, out var estado))
        {
            var mensaje = buzon.Mensajes.Single(m => m.Id == id);
            estado = new Estado { ProximaFecha = mensaje.CreadoEn };
            Estados[id] = estado;
        }

        return estado;
    }

    public Estado De(MensajeSaliente mensaje) => De(mensaje.Id);

    private static string Traslado(MensajeSaliente m) => m.ClaveDeEvento.Split(':')[1];

    public Task<IReadOnlyList<MensajeParaEntregar>> Reclamar(int maximo, DateTimeOffset ahora, TimeSpan arrendamiento, CancellationToken cancelacion = default)
    {
        Reclamos++;
        var pendientes = buzon.Mensajes.Where(m => De(m).Valor == "pendiente").ToList();
        var elegibles = pendientes
            .Where(m => De(m).ProximaFecha <= ahora && (De(m).ArrendadoHasta is null || De(m).ArrendadoHasta <= ahora))
            .Where(m => m.Tipo != TiposDeMensaje.EstadoDePedido
                || !pendientes.Any(p => p.Tipo == TiposDeMensaje.EstadoDePedido && Traslado(p) == Traslado(m) && p.CreadoEn < m.CreadoEn))
            .OrderBy(m => m.CreadoEn).ThenBy(m => m.Id)
            .Take(maximo)
            .ToList();

        foreach (var m in elegibles)
        {
            De(m).ArrendadoHasta = ahora + arrendamiento;
        }

        return Task.FromResult<IReadOnlyList<MensajeParaEntregar>>([.. elegibles.Select(m => new MensajeParaEntregar(m.Id, m.Tipo, m.ClaveDeEvento, m.SucursalCodigo, m.Carga, De(m).Intentos, m.CreadoEn, De(m).Alertado))]);
    }

    public Task MarcarEnviado(Guid mensajeId, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        var e = De(mensajeId);
        (e.Valor, e.EnviadoEn, e.ArrendadoHasta) = ("enviado", ahora, null);
        return Task.CompletedTask;
    }

    public Task Reprogramar(Guid mensajeId, int intentos, DateTimeOffset proximaFecha, bool alertado, CancellationToken cancelacion = default)
    {
        var e = De(mensajeId);
        (e.Intentos, e.ProximaFecha, e.Alertado, e.ArrendadoHasta) = (intentos, proximaFecha, alertado, null);
        return Task.CompletedTask;
    }

    public Task MarcarFallido(Guid mensajeId, MotivoDeFalloDeMensaje motivo, int intentos, CancellationToken cancelacion = default)
    {
        var e = De(mensajeId);
        (e.Valor, e.Intentos, e.ArrendadoHasta, e.MotivoDeFallo) = ("fallido", intentos, null, motivo.ToString().ToLowerInvariant());
        return Task.CompletedTask;
    }

    public Task<int> PurgarTerminados(DateTimeOffset antesDe, CancellationToken cancelacion = default)
    {
        var purgables = buzon.Mensajes.Where(m => De(m).Valor != "pendiente" && (De(m).EnviadoEn ?? m.CreadoEn) < antesDe).ToList();
        foreach (var m in purgables)
        {
            buzon.Mensajes.Remove(m);
            Estados.Remove(m.Id);
        }

        return Task.FromResult(purgables.Count);
    }
}

/// <summary>Lo que se envió al E-commerce y lo que respondió: un doble que nunca lanza (salvo que se le pida, para probar el adaptador defectuoso).</summary>
internal sealed class EnvioAlEcommerceDoble : IEnvioAlEcommerce
{
    public List<AvisoDeTrasladoParaEnviar> Avisos { get; } = [];

    public List<EstadoDeTrasladoDePedidoParaEnviar> Estados { get; } = [];

    public ResultadoDeEnvio Respuesta { get; set; } = ResultadoDeEnvio.Entregado;

    public Exception? Lanza { get; set; }

    /// <summary>Si se indica, quien llama puede comprobar que el envío ocurrió fuera de toda transacción.</summary>
    public Func<bool>? EnTransaccion { get; set; }

    public bool EnvioDentroDeUnaTransaccion { get; private set; }

    public Task<ResultadoDeEnvio> EnviarAviso(AvisoDeTrasladoParaEnviar aviso, CancellationToken cancelacion = default)
    {
        Avisos.Add(aviso);
        return Responder();
    }

    public Task<ResultadoDeEnvio> InformarEstadoDelPedido(EstadoDeTrasladoDePedidoParaEnviar estado, CancellationToken cancelacion = default)
    {
        Estados.Add(estado);
        return Responder();
    }

    private Task<ResultadoDeEnvio> Responder()
    {
        EnvioDentroDeUnaTransaccion |= EnTransaccion?.Invoke() ?? false;
        return Lanza is not null ? Task.FromException<ResultadoDeEnvio>(Lanza) : Task.FromResult(Respuesta);
    }
}

/// <summary>Una unidad de trabajo que cuenta si hay una transacción abierta mientras corre el trabajo y registra cuántas abrió.</summary>
internal sealed class UnidadDeTrabajoDeTrasladosDoble : IUnidadDeTrabajo
{
    public bool Abierta { get; private set; }

    public int Transacciones { get; private set; }

    public async Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default)
    {
        Transacciones++;
        Abierta = true;
        try
        {
            return await trabajo(cancelacion);
        }
        finally
        {
            Abierta = false;
        }
    }

    public Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default) => bloque(cancelacion);
}

/// <summary>Las alertas que se publicaron; como el catálogo real, rechaza con <see cref="ArgumentException"/> un motivo que este no conoce.</summary>
internal sealed class AlertasDeTrasladosEspia : IAlertas
{
    private readonly BabelHub.Inventario.Domain.Auditoria.CatalogoDeMotivosDeAlerta _catalogo = CatalogoDeAlertasDeAuditoria.Crear();

    public List<string> Tecnicas { get; } = [];

    public bool Falla { get; set; }

    public Task PublicarTecnica(CodigoDeAlerta motivo, CancellationToken cancelacion = default)
    {
        if (!_catalogo.Contiene(motivo.Valor))
        {
            throw new ArgumentException("El motivo no consta en el catálogo.", nameof(motivo));
        }

        if (Falla)
        {
            return Task.FromException(new InvalidOperationException("No se pudo alertar."));
        }

        Tecnicas.Add(motivo.Valor);
        return Task.CompletedTask;
    }

    public Task PublicarDeSeguridad(CodigoDeAlerta motivo, CancellationToken cancelacion = default) => throw new NotSupportedException();
}

internal sealed partial class EntornoDeTraslados
{
    private ColaDeMensajesEnMemoria? _cola;

    public ColaDeMensajesEnMemoria Cola => _cola ??= new ColaDeMensajesEnMemoria(Buzon);

    public EnvioAlEcommerceDoble Envio { get; } = new();

    public UnidadDeTrabajoDeTrasladosDoble Unidad { get; } = new();

    public AlertasDeTrasladosEspia Alertas { get; } = new();

    public EntregarMensajesSalientes Entrega()
    {
        Envio.EnTransaccion = () => Unidad.Abierta;
        return new EntregarMensajesSalientes(Autorizador, Unidad, Cola, Envio, Alertas, Base.Reloj);
    }

    public RevisarTrasladosSinAprobar Revision() => new(Autorizador, Traslados, Alertas, Base.Reloj);

    public DepurarTraslados Depuracion() => new(Autorizador, Traslados, Cola, Base.Reloj);

    /// <summary>Un traslado ya cerrado hace tiempo (recibido), con solicitante, motivo y guía, para probar la depuración de los 2 años.</summary>
    public Traslado Cerrado(Guid edicion, DateTimeOffset cerradoEn)
    {
        var t = Traslado.SolicitarReposicion(Guid.NewGuid(), edicion, Cusco, Ayacucho, 1, "motivo viejo", Guid.NewGuid(), cerradoEn.AddDays(-3));
        t.Aprobar(cerradoEn.AddDays(-2));
        t.Despachar(cerradoEn.AddDays(-1));
        t.RegistrarGuia("CA-1111");
        t.Recibir(cerradoEn);
        return Traslados.Sembrar(t);
    }
}
