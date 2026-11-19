using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>El repositorio de pedidos en memoria de las pruebas de la Aplicación: guarda los pedidos y las operaciones por referencia y anota los bloqueos pedidos.</summary>
internal sealed class RepositorioDePedidosFalso : IRepositorioDePedidos
{
    private readonly Dictionary<Guid, Pedido> _pedidos = [];

    public List<Anulacion> Anulaciones { get; } = [];

    public List<Entrega> Entregas { get; } = [];

    public List<(Guid PedidoId, DateTimeOffset Desde)> LiberacionesDeContacto { get; } = [];

    public List<string> Llamadas { get; } = [];

    /// <summary>Hace que la próxima inserción diga «ya existía» sin guardar nada (una colisión de número, o de id si el pedido ya está).</summary>
    public int ColisionesDeNumero { get; set; }

    public IReadOnlyCollection<Pedido> Pedidos => _pedidos.Values;

    public void Poner(Pedido pedido) => _pedidos[pedido.Id] = pedido;

    public Task<Pedido?> Obtener(Guid pedidoId, bool conBloqueo, CancellationToken cancelacion = default)
    {
        Llamadas.Add(conBloqueo ? "obtener-con-bloqueo" : "obtener");
        return Task.FromResult(_pedidos.GetValueOrDefault(pedidoId));
    }

    public Task<bool> Existe(Guid pedidoId, CancellationToken cancelacion = default)
    {
        Llamadas.Add("existe");
        return Task.FromResult(_pedidos.ContainsKey(pedidoId));
    }

    public Task<bool> Agregar(Pedido pedido, CancellationToken cancelacion = default)
    {
        Llamadas.Add("agregar");
        if (ColisionesDeNumero > 0)
        {
            ColisionesDeNumero--;
            return Task.FromResult(false);
        }

        if (_pedidos.ContainsKey(pedido.Id) || _pedidos.Values.Any(p => p.Numero == pedido.Numero))
        {
            return Task.FromResult(false);
        }

        _pedidos[pedido.Id] = pedido;
        return Task.FromResult(true);
    }

    public Task Actualizar(Pedido pedido, CancellationToken cancelacion = default)
    {
        Llamadas.Add("actualizar");
        _pedidos[pedido.Id] = pedido;
        return Task.CompletedTask;
    }

    public Task<PaginaDeDominio> Listar(CriterioDeListado criterio, CancellationToken cancelacion = default)
    {
        Llamadas.Add("listar");
        var filtrados = _pedidos.Values
            .Where(p => criterio.CuentaId is null || p.CuentaId == criterio.CuentaId)
            .Where(p => criterio.CodigoDeSucursalDeRecojo is null || (p.Tipo == TipoDeEntregaDelPedido.Recojo && p.CodigoDeSucursalDeRecojo == criterio.CodigoDeSucursalDeRecojo))
            .Where(p => !criterio.SoloADomicilio || p.Tipo == TipoDeEntregaDelPedido.Domicilio)
            .Where(p => criterio.Estado is null || p.Estado == criterio.Estado)
            .OrderByDescending(p => p.CreadoEn)
            .ToList();
        return Task.FromResult(new PaginaDeDominio([.. filtrados.Skip((criterio.Pagina - 1) * criterio.Tamano).Take(criterio.Tamano)], filtrados.Count));
    }

    public Dictionary<string, EstadoDeTrasladoRegistrado> EstadosDeTraslado { get; } = [];

    public Task<EstadoDeTrasladoRegistrado?> ObtenerEstadoDeTraslado(string eventoId, CancellationToken cancelacion = default) =>
        Task.FromResult(EstadosDeTraslado.GetValueOrDefault(eventoId));

    public Task GuardarEstadoDeTraslado(EstadoDeTrasladoRegistrado estado, CancellationToken cancelacion = default)
    {
        Llamadas.Add("guardar-estado-de-traslado");
        EstadosDeTraslado[estado.EventoId] = estado;
        return Task.CompletedTask;
    }

    public Task RegistrarLiberacionDeContacto(Guid pedidoId, DateTimeOffset desde, CancellationToken cancelacion = default)
    {
        Llamadas.Add("liberacion-de-contacto");
        if (LiberacionesDeContacto.All(l => l.PedidoId != pedidoId))
        {
            LiberacionesDeContacto.Add((pedidoId, desde));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Anulacion>> AnulacionesEnCurso(Guid pedidoId, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<Anulacion>>([.. Anulaciones.Where(a => a.PedidoId == pedidoId && a.BloqueaLasLineas)]);

    public Task<IReadOnlyList<Entrega>> EntregasEnCurso(Guid pedidoId, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<Entrega>>([.. Entregas.Where(e => e.PedidoId == pedidoId && e.BloqueaLasLineas)]);
}

internal sealed class MarcaDeCancelacionFalsa(EstadoDeLaMarca estado = EstadoDeLaMarca.Libre) : IMarcaDeCancelacionDeCuenta
{
    public EstadoDeLaMarca Estado { get; set; } = estado;

    public int Lecturas { get; private set; }

    public Task<EstadoDeLaMarca> LeerConBloqueo(Guid usuarioId, CancellationToken cancelacion = default)
    {
        Lecturas++;
        return Task.FromResult(Estado);
    }
}

internal sealed class AccesosDeBibliotecaFalsos : IAccesosDeBiblioteca
{
    public List<(Guid CuentaId, Guid PedidoId, Guid EdicionId)> Concedidos { get; } = [];

    public List<(Guid PedidoId, Guid EdicionId, MotivoDeRevocacionDeAcceso Motivo)> Revocados { get; } = [];

    public ResultadoDeConcederAcceso Respuesta { get; set; } = ResultadoDeConcederAcceso.Concedido;

    public bool Falla { get; set; }

    public HashSet<Guid> YaPoseidas { get; } = [];

    public Task<Resultado<ResultadoDeConcederAcceso>> Conceder(ConcederAccesoEntrada entrada, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            return Task.FromResult(Resultado<ResultadoDeConcederAcceso>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible")));
        }

        Concedidos.Add((entrada.CuentaId, entrada.PedidoId, entrada.EdicionId));
        return Task.FromResult(Resultado<ResultadoDeConcederAcceso>.Exito(YaPoseidas.Contains(entrada.EdicionId) ? ResultadoDeConcederAcceso.YaPoseidoPorOtraCompra : Respuesta));
    }

    public Task<Resultado<ResultadoDeRevocarAcceso>> Revocar(RevocarAccesoEntrada entrada, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            return Task.FromResult(Resultado<ResultadoDeRevocarAcceso>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible")));
        }

        Revocados.Add((entrada.PedidoId, entrada.EdicionId, entrada.Motivo));
        return Task.FromResult(Resultado<ResultadoDeRevocarAcceso>.Exito(ResultadoDeRevocarAcceso.Revocado));
    }
}

/// <summary>Un generador de números determinista: devuelve los pedidos en orden y, agotados, sigue con otros nuevos.</summary>
internal sealed class GeneradorDeNumerosFalso : IGeneradorDeNumerosDePedido
{
    private int _siguiente;

    public List<string> Entregados { get; } = [];

    public NumeroDePedido Siguiente()
    {
        var n = _siguiente++;
        var numero = NumeroDePedido.Generar(maximo => (n + 1) % maximo);
        // Una sola letra distinta por número para no repetir.
        var texto = string.Create(10, n, (span, valor) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = NumeroDePedido.Alfabeto[(valor + i * 3) % NumeroDePedido.Alfabeto.Length];
            }
        });
        Entregados.Add(texto);
        return NumeroDePedido.Crear(texto) ?? numero;
    }
}

internal sealed class ResolucionDeSucursalFalsa : IResolucionDeSucursal
{
    public static readonly Guid Lima = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid Ayacucho = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    public Task<Guid?> IdentificadorDe(string codigoDeSucursal, CancellationToken cancelacion = default) =>
        Task.FromResult<Guid?>(codigoDeSucursal switch { "LIM" => Lima, "AYA" => Ayacucho, _ => null });
}

/// <summary>Límites que dejan pasar todo y pueden negar el contacto o la gestión.</summary>
internal sealed class LimitesPermisivosDePedidos : ILimitadorDePedidos
{
    public ResultadoDeLimiteDePedidos Contacto { get; set; } = ResultadoDeLimiteDePedidos.Permitido;

    public ResultadoDeLimiteDePedidos Gestion { get; set; } = ResultadoDeLimiteDePedidos.Permitido;

    public Task<ResultadoDeLimiteDePedidos> Consultar(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLimiteDePedidos.Permitido);

    public Task<ResultadoDeLimiteDePedidos> Gestionar(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(Gestion);

    public Task<ResultadoDeLimiteDePedidos> LeerContacto(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(Contacto);
}

internal sealed class EstadoDeCuentaFalso : IEstadoDeCuenta
{
    public EstadoDeCuenta? Estado { get; set; }

    public Task<EstadoDeCuenta?> Consultar(Guid usuarioId, CancellationToken cancelacion = default) =>
        Task.FromResult<EstadoDeCuenta?>(Estado ?? new EstadoDeCuenta(usuarioId, true, EstadoDeAceptacionDeLosTextos.AlDia, true, false, false));

    public Task<ResultadoDeLaAceptacion> AceptarLosTextos(Guid usuarioId, string versionMostrada, CancellationToken cancelacion = default) => throw new NotSupportedException();
}

internal sealed class AuditoriaDeContactoFalsa : IAuditoriaDeContacto
{
    public List<(Guid PedidoId, string Rol)> Lecturas { get; } = [];

    public bool Caida { get; set; }

    public Task ContactoConsultado(Guid pedidoId, string rol, CancellationToken cancelacion = default)
    {
        if (Caida)
        {
            throw new InvalidOperationException("auditoría caída");
        }

        Lecturas.Add((pedidoId, rol));
        return Task.CompletedTask;
    }
}

internal sealed class ReembolsosFalsos : IReembolsos
{
    public Dictionary<string, ReembolsoPendienteVista> Pendientes { get; } = [];

    public List<RegistrarReembolsoEntrada> Registrados { get; } = [];

    public bool Falla { get; set; }

    public Task<Resultado<ReembolsoRegistrado>> Registrar(RegistrarReembolsoEntrada entrada, CancellationToken cancelacion = default)
    {
        Registrados.Add(entrada);
        var monto = entrada.Lineas.Sum(l => l.Monto) + entrada.Envio;
        return Task.FromResult(Resultado<ReembolsoRegistrado>.Exito(new ReembolsoRegistrado(Guid.NewGuid(), monto, EstadoDelReembolso.Pendiente, false)));
    }

    public Task<Resultado<EstadoDelReembolso>> Procesar(Guid reembolsoId, CancellationToken cancelacion = default) => Task.FromResult(Resultado<EstadoDelReembolso>.Exito(EstadoDelReembolso.Reembolsado));

    public Task<int> ContarPendientes(Guid cuentaId, OrigenDelReembolso origen, CancellationToken cancelacion = default) => Task.FromResult(0);

    public Task<Resultado<ReembolsoPendienteVista?>> ConsultarPorOrigen(OrigenDelReembolso origen, string claveNatural, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            throw new InvalidOperationException("Pagos caído");
        }

        return Task.FromResult(Resultado<ReembolsoPendienteVista?>.Exito(Pendientes.GetValueOrDefault(claveNatural)));
    }

    public Task<Resultado<PaginaDeReembolsos>> ListarPendientes(int pagina, int tamano, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<EstadoDelReembolso>> Reintentar(Guid reembolsoId, CancellationToken cancelacion = default) => Task.FromResult(Resultado<EstadoDelReembolso>.Exito(EstadoDelReembolso.Reembolsado));
}

internal sealed class NotificadorDePedidosEspia : BabelHub.Ecommerce.Application.Notificaciones.INotificador
{
    public List<BabelHub.Ecommerce.Application.Notificaciones.SolicitudDeAviso> Solicitudes { get; } = [];

    public bool Lanza { get; set; }

    public Task<BabelHub.Ecommerce.Application.Notificaciones.ResultadoDeLaSolicitudDeAviso> Solicitar(BabelHub.Ecommerce.Application.Notificaciones.SolicitudDeAviso solicitud, CancellationToken cancelacion = default)
    {
        if (Lanza)
        {
            throw new InvalidOperationException("sin servicio de avisos");
        }

        Solicitudes.Add(solicitud);
        return Task.FromResult(BabelHub.Ecommerce.Application.Notificaciones.ResultadoDeLaSolicitudDeAviso.Creado);
    }
}

/// <summary>Una unidad de trabajo que cuenta cuántas veces se ejecutó y cuántas se revirtió (el trabajo lanzó): para probar que una operación rechazada dentro de su transacción no deja nada a medias.</summary>
internal sealed class UnidadDeTrabajoEspia : IUnidadDeTrabajo
{
    public int Ejecuciones { get; private set; }

    public int Revertidas { get; private set; }

    public async Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default)
    {
        Ejecuciones++;
        try
        {
            return await trabajo(cancelacion);
        }
        catch
        {
            Revertidas++;
            throw;
        }
    }

    public Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default) => bloque(cancelacion);
}
