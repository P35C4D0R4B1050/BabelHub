using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.UnitTests.Catalogo;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>Los accesos de Biblioteca para Devoluciones: sembrados por cuenta, pedido y edición; la revocación y la suspensión cambian el estado y <see cref="NoResponde"/> es «no disponible».</summary>
internal sealed class AccesosParaDevolucionesDoble : IAccesosParaDevoluciones
{
    private readonly Dictionary<(Guid Cuenta, Guid Pedido, Guid Edicion), AccesoParaDevoluciones> _accesos = [];

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
        return Task.FromResult(Resultado<ResultadoDeSuspenderAcceso>.Exito(ResultadoDeSuspenderAcceso.Suspendido));
    }

    public Task<Resultado<ResultadoDeReanudarAcceso>> Reanudar(Guid cuentaId, Guid accesoId, CancellationToken cancelacion = default)
    {
        Llamadas.Add("reanudar");
        return Task.FromResult(Resultado<ResultadoDeReanudarAcceso>.Exito(ResultadoDeReanudarAcceso.Reanudado));
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

    public Task<Resultado<ReembolsoPendienteVista?>> ConsultarPorOrigen(OrigenDelReembolso origen, string claveNatural, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<ReembolsoPendienteVista?>.Exito(null));

    public Task<Resultado<PaginaDeReembolsos>> ListarPendientes(int pagina, int tamano, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<PaginaDeReembolsos>.Exito(new PaginaDeReembolsos([], pagina, tamano, 0)));

    public Task<Resultado<EstadoDelReembolso>> Reintentar(Guid reembolsoId, CancellationToken cancelacion = default) => Task.FromResult(Resultado<EstadoDelReembolso>.Exito(ProcesarResponde));

    public object Capturar() => _registrados.ToList();

    public void Restaurar(object instantanea) => _registrados = (List<RegistrarReembolsoEntrada>)instantanea;
}
