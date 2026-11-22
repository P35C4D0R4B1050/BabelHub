using BabelHub.Inventario.Application.Ajustes;
using BabelHub.Inventario.Application.Ajustes.Interno;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Ajustes;
using BabelHub.Inventario.UnitTests.Fuentes;
using BabelHub.Inventario.UnitTests.Soporte;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.UnitTests.Ajustes;

/// <summary>El motor de límites de las pruebas: deja pasar salvo que se le pida rechazar, y recuerda a quién contó.</summary>
internal sealed class ControlDeLimitesDoble : IControlDeLimites
{
    public List<(string Politica, string? Sujeto)> Reservas { get; } = [];

    public ResultadoDeLimite? Forzado { get; set; }

    public Task<ResultadoDeLimite> Reservar(ClaveDePolitica politica, string? sujeto = null, string? claveDelLlamador = null, BabelHub.Inventario.Application.Plataforma.ClaveDeIdempotencia? idempotencia = null, CancellationToken cancelacion = default)
    {
        Reservas.Add((politica.Valor, sujeto));
        return Task.FromResult(Forzado ?? ResultadoDeLimite.PermitidoSinContar());
    }

    public Task Liberar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;
}

internal sealed class BloqueosDeAjustesEspia(BitacoraDeLlamadas bitacora) : IBloqueosAsesores
{
    public int Recepciones { get; private set; }

    public List<Guid> Devoluciones { get; } = [];

    public bool EsperaAgotada { get; set; }

    public Task Recepcion(CancellationToken cancelacion = default)
    {
        bitacora.Anotar("bloqueo.Recepcion");
        Recepciones++;
        return EsperaAgotada ? Task.FromException(new EsperaDeBloqueoAgotadaException()) : Task.CompletedTask;
    }

    public Task Devolucion(Guid devolucionId, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("bloqueo.Devolucion");
        Devoluciones.Add(devolucionId);
        return EsperaAgotada ? Task.FromException(new EsperaDeBloqueoAgotadaException()) : Task.CompletedTask;
    }

    public Task Cliente(Guid clienteId, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task Pedido(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<bool> IntentarPedido(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();
}

internal sealed class RepositorioDeAjustesEnMemoria(BitacoraDeLlamadas bitacora) : IRepositorioDeAjustes
{
    public List<Ajuste> Guardados { get; } = [];

    public bool InsertarChocaConUnaConcurrente { get; set; }

    public Task Insertar(Ajuste ajuste, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("ajustes.Insertar");
        if (InsertarChocaConUnaConcurrente || (ajuste.DevolucionId is { } d && Guardados.Any(a => a.DevolucionId == d && a.EdicionId == ajuste.EdicionId)))
        {
            throw new AjusteDuplicadoException();
        }

        Guardados.Add(ajuste);
        return Task.CompletedTask;
    }

    public Task<Ajuste?> PorId(Guid ajusteId, CancellationToken cancelacion = default) => Task.FromResult(Guardados.FirstOrDefault(a => a.Id == ajusteId));

    public Task<Ajuste?> PorDevolucionYEdicion(Guid devolucionId, Guid edicionId, CancellationToken cancelacion = default) =>
        Task.FromResult(Guardados.FirstOrDefault(a => a.DevolucionId == devolucionId && a.EdicionId == edicionId));

    public Task<PaginaDeAjustesGuardados> Historial(Guid fuenteId, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        var todos = Guardados.Where(a => a.FuenteId == fuenteId).OrderByDescending(a => a.Fecha).ThenByDescending(a => a.Id).ToList();
        return Task.FromResult(new PaginaDeAjustesGuardados([.. todos.Skip((pagina - 1) * tamano).Take(tamano)], todos.Count));
    }

    public Task<IReadOnlyList<UnidadesReingresadas>> ReingresadasPorPedidos(IReadOnlyCollection<Guid> pedidoIds, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<UnidadesReingresadas>>(
            [.. Guardados
                .Where(a => a.Motivo == MotivoDeAjuste.Devolucion && a.PedidoId is { } p && pedidoIds.Contains(p))
                .GroupBy(a => (Pedido: a.PedidoId!.Value, a.EdicionId))
                .Select(g => new UnidadesReingresadas(g.Key.Pedido, g.Key.EdicionId, g.Sum(a => a.Diferencia)))]);
}

internal sealed class RepositorioDeIngresosEnMemoria : IRepositorioDeIngresos
{
    public List<IngresoDeMercaderia> Guardados { get; } = [];

    public Task Insertar(IngresoDeMercaderia ingreso, CancellationToken cancelacion = default)
    {
        Guardados.Add(ingreso);
        return Task.CompletedTask;
    }

    public Task<IngresoDeMercaderia?> PorId(Guid ingresoId, CancellationToken cancelacion = default) => Task.FromResult(Guardados.FirstOrDefault(i => i.Id == ingresoId));

    public Task<IReadOnlyList<IngresoDeMercaderia>> Ultimos(Guid? edicionId, int limite, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<IngresoDeMercaderia>>([.. Guardados.Where(i => edicionId is null || i.EdicionId == edicionId).OrderByDescending(i => i.Fecha).Take(limite)]);
}

internal sealed class RepositorioDeOperacionesEnMemoria : IRepositorioDeOperacionesIdempotentes
{
    private readonly Dictionary<(Guid, string, string), OperacionIdempotenteGuardada> _guardadas = [];

    public int Cantidad => _guardadas.Count;

    /// <summary>Simula que la clave ya la tiene tomada otra transacción y la espera agota el plazo de bloqueo.</summary>
    public bool EsperaAgotada { get; set; }

    public Task<OperacionIdempotenteGuardada?> Registrar(Guid usuarioId, string operacion, string clave, string huellaDeLaCarga, string resultado, CancellationToken cancelacion = default)
    {
        if (EsperaAgotada)
        {
            return Task.FromException<OperacionIdempotenteGuardada?>(new EsperaDeBloqueoAgotadaException());
        }

        if (_guardadas.TryGetValue((usuarioId, operacion, clave), out var existente))
        {
            return Task.FromResult<OperacionIdempotenteGuardada?>(existente);
        }

        _guardadas[(usuarioId, operacion, clave)] = new OperacionIdempotenteGuardada(huellaDeLaCarga, resultado);
        return Task.FromResult<OperacionIdempotenteGuardada?>(null);
    }
}

/// <summary>Todo lo que los casos de uso de <c>Ajustes</c> necesitan, en memoria, sobre el entorno de <c>Fuentes</c> (Lima la de recepción, Cusco y Ayacucho) con el <c>MovimientosDeStock</c> y el directorio reales.</summary>
internal sealed class EntornoDeAjustes
{
    public EntornoDeAjustes(ActorDePrueba? actor = null)
    {
        Base = new EntornoDeFuentes(actor);
        Ajustes = new RepositorioDeAjustesEnMemoria(Base.Bitacora);
        Bloqueos = new BloqueosDeAjustesEspia(Base.Bitacora);
        Directorio = new DirectorioDeFuentes(Base.Lectura, Base.Fuentes, Base.Rutas, Base.Fichas);
        Movimientos = new MovimientosDeStock(Base.Fichas, Base.Existencias, Base.Reservas, new AplicadorDeMovimientos(Base.Existencias));
    }

    public EntornoDeFuentes Base { get; }

    public RepositorioDeAjustesEnMemoria Ajustes { get; }

    public RepositorioDeIngresosEnMemoria Ingresos { get; } = new();

    public RepositorioDeOperacionesEnMemoria Operaciones { get; } = new();

    public ControlDeLimitesDoble Limites { get; } = new();

    public BloqueosDeAjustesEspia Bloqueos { get; }

    public DirectorioDeFuentes Directorio { get; }

    public MovimientosDeStock Movimientos { get; }

    public ActorDePrueba Actor => Base.Actor;

    public Guid Lima => Base.Lima.Id;

    public Guid Cusco => Base.Cusco.Id;

    public Guid Ayacucho => Base.Ayacucho.Id;

    public AutorizadorDeAjustes Autorizador => new(Actor, Base.Denegaciones);

    public RegistrarAjusteManual AjusteManual() =>
        new(Autorizador, new ValidadorDeRegistrarAjusteManual(), new PresupuestoDelPersonal(Limites, Base.Reloj), Directorio, Directorio, Movimientos, Base.Reservas, Ajustes, Operaciones, Base.Auditoria, Base.Reloj);

    public RegistrarIngresoDeMercaderia Ingreso() =>
        new(Autorizador, new ValidadorDeRegistrarIngreso(), new PresupuestoDelPersonal(Limites, Base.Reloj), Bloqueos, Directorio, Directorio, Movimientos, Ajustes, Ingresos, Operaciones, Base.Auditoria, Base.Reloj);

    public ConsultarIngresos ListaDeIngresos() => new(Autorizador, new ValidadorDeConsultarIngresos(), Directorio, Ingresos);

    public ReingresarPorDevolucion Reingreso() =>
        new(Autorizador, new ValidadorDeReingresarPorDevolucion(), Bloqueos, Directorio, Directorio, Movimientos, Ajustes, Base.Auditoria, Base.Reloj);

    public ConsultarHistorialDeAjustes Historial() => new(Autorizador, new ValidadorDeConsultarHistorialDeAjustes(), Directorio, Ajustes);

    public ConsultarReingresosPorDevolucion ReingresosPorDevolucion() => new(Ajustes);

    public Guid Impresa() => Base.Impresa();

    public Guid Digital() => Base.Fichas.Agregar(Domain.Fuentes.FormatoDeEdicion.Digital).EdicionId;

    public void Poner(Guid fuente, Guid edicion, int fisico, int reservado) => Base.Existencias.Poner(fuente, edicion, fisico, reservado);

    public (int Fisico, int Reservado) Stock(Guid fuente, Guid edicion) => Base.Existencias.Filas[(fuente, edicion)];

    public void ComoEncargadoDe(Guid sucursal) => Actor.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, sucursal));

    public void Como(string rol, Guid? sucursal = null) => Actor.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), rol, sucursal));

    public static string Clave() => Guid.NewGuid().ToString("N");
}
