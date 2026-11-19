using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Identidad;
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
