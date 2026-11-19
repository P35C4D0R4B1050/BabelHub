using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>Los accesos de Biblioteca para Devoluciones: sembrados por cuenta, pedido y edición; la revocación y la suspensión cambian el estado y <see cref="NoResponde"/> es «no disponible».</summary>
internal sealed class AccesosParaDevolucionesDoble : IAccesosParaDevoluciones, IInstantanea
{
    private Dictionary<(Guid Cuenta, Guid Pedido, Guid Edicion), AccesoParaDevoluciones> _accesos = [];

    /// <summary>Lo que responde <c>Suspender</c> (por omisión suspende de verdad) y <c>Reanudar</c>.</summary>
    public ResultadoDeSuspenderAcceso SuspenderResponde { get; set; } = ResultadoDeSuspenderAcceso.Suspendido;

    public ResultadoDeReanudarAcceso ReanudarResponde { get; set; } = ResultadoDeReanudarAcceso.Reanudado;

    public bool ReanudarFalla { get; set; }

    public object Capturar() => new Dictionary<(Guid, Guid, Guid), AccesoParaDevoluciones>(_accesos);

    public void Restaurar(object instantanea) => _accesos = (Dictionary<(Guid Cuenta, Guid Pedido, Guid Edicion), AccesoParaDevoluciones>)instantanea;

    public bool NoResponde { get; set; }

    /// <summary>El enlace se entrega justo antes de revocar (la carrera de DEV-20): <c>RevocarSiNoHayEnlace</c> responde <c>EnlaceYaEntregado</c>.</summary>
    public bool EnlaceLlegaAntesDeRevocar { get; set; }

    public List<string> Llamadas { get; } = [];

    public void Sembrar(Guid cuenta, Guid pedido, Guid edicion, EstadoDelAccesoParaDevoluciones estado = EstadoDelAccesoParaDevoluciones.Vigente, bool enlaceEntregado = false) =>
        _accesos[(cuenta, pedido, edicion)] = new AccesoParaDevoluciones(Guid.NewGuid(), estado, enlaceEntregado);

    public AccesoParaDevoluciones? Estado(Guid cuenta, Guid pedido, Guid edicion) => _accesos.GetValueOrDefault((cuenta, pedido, edicion));

    public Task<Resultado<AccesoParaDevoluciones?>> Consultar(Guid cuentaId, Guid pedidoId, Guid edicionId, CancellationToken cancelacion = default)
    {
        Llamadas.Add("consultar");
        return Task.FromResult(NoResponde
            ? Resultado<AccesoParaDevoluciones?>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"))
            : Resultado<AccesoParaDevoluciones?>.Exito(_accesos.GetValueOrDefault((cuentaId, pedidoId, edicionId))));
    }

    public Task<Resultado<ResultadoDeRevocarSiNoHayEnlace>> RevocarSiNoHayEnlace(Guid cuentaId, Guid pedidoId, Guid edicionId, CancellationToken cancelacion = default)
    {
        Llamadas.Add("revocar");
        if (NoResponde)
        {
            return Task.FromResult(Resultado<ResultadoDeRevocarSiNoHayEnlace>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible")));
        }

        if (!_accesos.TryGetValue((cuentaId, pedidoId, edicionId), out var acceso))
        {
            return Task.FromResult(Resultado<ResultadoDeRevocarSiNoHayEnlace>.Exito(ResultadoDeRevocarSiNoHayEnlace.SinAcceso));
        }

        if (EnlaceLlegaAntesDeRevocar || acceso.EnlaceEntregado)
        {
            return Task.FromResult(Resultado<ResultadoDeRevocarSiNoHayEnlace>.Exito(ResultadoDeRevocarSiNoHayEnlace.EnlaceYaEntregado));
        }

        var resultado = acceso.Estado switch
        {
            EstadoDelAccesoParaDevoluciones.Revocado => ResultadoDeRevocarSiNoHayEnlace.YaRevocado,
            EstadoDelAccesoParaDevoluciones.Suspendido => ResultadoDeRevocarSiNoHayEnlace.NoDisponible,
            _ => ResultadoDeRevocarSiNoHayEnlace.Revocado,
        };
        if (resultado == ResultadoDeRevocarSiNoHayEnlace.Revocado)
        {
            _accesos[(cuentaId, pedidoId, edicionId)] = acceso with { Estado = EstadoDelAccesoParaDevoluciones.Revocado };
        }

        return Task.FromResult(Resultado<ResultadoDeRevocarSiNoHayEnlace>.Exito(resultado));
    }

    public Task<Resultado<ResultadoDeSuspenderAcceso>> Suspender(Guid cuentaId, Guid accesoId, CancellationToken cancelacion = default)
    {
        Llamadas.Add("suspender");
        var clave = _accesos.Where(a => a.Key.Cuenta == cuentaId && a.Value.AccesoId == accesoId).Select(a => (a.Key, a.Value)).FirstOrDefault();
        if (SuspenderResponde == ResultadoDeSuspenderAcceso.Suspendido && clave.Value is not null)
        {
            _accesos[clave.Key] = clave.Value with { Estado = EstadoDelAccesoParaDevoluciones.Suspendido };
        }

        return Task.FromResult(Resultado<ResultadoDeSuspenderAcceso>.Exito(SuspenderResponde));
    }

    public Task<Resultado<ResultadoDeReanudarAcceso>> Reanudar(Guid cuentaId, Guid accesoId, CancellationToken cancelacion = default)
    {
        Llamadas.Add("reanudar");
        if (ReanudarFalla)
        {
            return Task.FromResult(Resultado<ResultadoDeReanudarAcceso>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible")));
        }

        var clave = _accesos.Where(a => a.Key.Cuenta == cuentaId && a.Value.AccesoId == accesoId).Select(a => (a.Key, a.Value)).FirstOrDefault();
        if (ReanudarResponde == ResultadoDeReanudarAcceso.Reanudado && clave.Value is { Estado: EstadoDelAccesoParaDevoluciones.Suspendido })
        {
            _accesos[clave.Key] = clave.Value with { Estado = EstadoDelAccesoParaDevoluciones.Vigente };
        }

        return Task.FromResult(Resultado<ResultadoDeReanudarAcceso>.Exito(ReanudarResponde));
    }
}

/// <summary>Los reembolsos de Pagos: cada <c>Registrar</c> se anota (y se revierte con la transacción); <c>Procesar</c> responde lo que la prueba disponga.</summary>
internal sealed class ReembolsosDoble : IReembolsos, IInstantanea
{
    private List<RegistrarReembolsoEntrada> _registrados = [];

    /// <summary>La unidad de trabajo, asignada tras crearla (la unidad necesita este doble para revertirlo).</summary>
    public UnidadDeTrabajoReversible? Unidad { get; set; }

    public IReadOnlyList<RegistrarReembolsoEntrada> Registrados => _registrados;

    /// <summary>Si es verdadero, <c>Registrar</c> rechaza por el tope de la línea (ya reembolsada por una anulación).</summary>
    public bool RechazaPorElTope { get; set; }

    public bool NoDisponible { get; set; }

    public EstadoDelReembolso ProcesarResponde { get; set; } = EstadoDelReembolso.Reembolsado;

    public bool ProcesarLanza { get; set; }

    public int Procesados { get; private set; }

    public int ProcesosDentroDeLaTransaccion { get; private set; }

    public Task<Resultado<ReembolsoRegistrado>> Registrar(RegistrarReembolsoEntrada entrada, CancellationToken cancelacion = default)
    {
        if (NoDisponible)
        {
            return Task.FromResult(Resultado<ReembolsoRegistrado>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible")));
        }

        if (RechazaPorElTope)
        {
            return Task.FromResult(Resultado<ReembolsoRegistrado>.Fallo(new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, "reembolso-supera-lo-cobrado")));
        }

        var repetido = _registrados.Any(r => r.ClaveNatural == entrada.ClaveNatural);
        if (!repetido)
        {
            _registrados.Add(entrada);
        }

        return Task.FromResult(Resultado<ReembolsoRegistrado>.Exito(new ReembolsoRegistrado(Guid.NewGuid(), entrada.Lineas.Sum(l => l.Monto), EstadoDelReembolso.Pendiente, repetido)));
    }

    public Task<Resultado<EstadoDelReembolso>> Procesar(Guid reembolsoId, CancellationToken cancelacion = default)
    {
        Procesados++;
        if (Unidad?.EnCurso == true)
        {
            ProcesosDentroDeLaTransaccion++;
        }

        return ProcesarLanza
            ? throw new InvalidOperationException("La pasarela falló.")
            : Task.FromResult(Resultado<EstadoDelReembolso>.Exito(ProcesarResponde));
    }

    public Task<int> ContarPendientes(Guid cuentaId, OrigenDelReembolso origen, CancellationToken cancelacion = default) => Task.FromResult(0);

    /// <summary>El estado que Pagos informa por clave natural (el reintento de la administración lo deja reembolsado); sin entrada, Pagos no conoce el reembolso.</summary>
    public Dictionary<string, EstadoDelReembolso> PorClaveNatural { get; } = [];

    public bool ConsultaFalla { get; set; }

    public Task<Resultado<ReembolsoPendienteVista?>> ConsultarPorOrigen(OrigenDelReembolso origen, string claveNatural, CancellationToken cancelacion = default)
    {
        if (ConsultaFalla)
        {
            return Task.FromResult(Resultado<ReembolsoPendienteVista?>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible")));
        }

        return Task.FromResult(Resultado<ReembolsoPendienteVista?>.Exito(PorClaveNatural.TryGetValue(claveNatural, out var estado)
            ? new ReembolsoPendienteVista(Guid.NewGuid(), origen, 10m, estado, null, DateTimeOffset.UtcNow, 1)
            : null));
    }

    public Task<Resultado<PaginaDeReembolsos>> ListarPendientes(int pagina, int tamano, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<PaginaDeReembolsos>.Exito(new PaginaDeReembolsos([], pagina, tamano, 0)));

    public Task<Resultado<EstadoDelReembolso>> Reintentar(Guid reembolsoId, CancellationToken cancelacion = default) => Task.FromResult(Resultado<EstadoDelReembolso>.Exito(ProcesarResponde));

    public object Capturar() => _registrados.ToList();

    public void Restaurar(object instantanea) => _registrados = (List<RegistrarReembolsoEntrada>)instantanea;
}

/// <summary>La revocación de Biblioteca que usa Devoluciones al aprobar un reporte: anota cada llamada y responde «ya revocado» si la prueba lo pide.</summary>
internal sealed class BibliotecaParaDevolucionesDoble : IAccesosDeBiblioteca
{
    public bool YaRevocado { get; set; }

    public bool NoResponde { get; set; }

    public List<(Guid PedidoId, Guid EdicionId, MotivoDeRevocacionDeAcceso Motivo)> Revocados { get; } = [];

    public Task<Resultado<ResultadoDeConcederAcceso>> Conceder(ConcederAccesoEntrada entrada, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<ResultadoDeConcederAcceso>.Exito(ResultadoDeConcederAcceso.Concedido));

    public Task<Resultado<ResultadoDeRevocarAcceso>> Revocar(RevocarAccesoEntrada entrada, CancellationToken cancelacion = default)
    {
        Revocados.Add((entrada.PedidoId, entrada.EdicionId, entrada.Motivo));
        return Task.FromResult(NoResponde
            ? Resultado<ResultadoDeRevocarAcceso>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"))
            : Resultado<ResultadoDeRevocarAcceso>.Exito(YaRevocado ? ResultadoDeRevocarAcceso.YaRevocado : ResultadoDeRevocarAcceso.Revocado));
    }
}
