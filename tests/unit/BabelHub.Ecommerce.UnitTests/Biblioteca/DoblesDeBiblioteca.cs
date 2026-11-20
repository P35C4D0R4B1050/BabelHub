using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;
using BabelHub.Ecommerce.UnitTests.Catalogo;

namespace BabelHub.Ecommerce.UnitTests.Biblioteca;

/// <summary>
/// Los repositorios de Biblioteca en memoria. Modelan las <b>sentencias condicionales</b> del plan (cada operación decide con el estado actual, como el <c>UPDATE … WHERE</c>) y <b>se revierten con la unidad de trabajo</b>.
/// El gancho <see cref="DespuesDeLeerUnAcceso"/> permite simular que otro proceso cambia o elimina el acceso entre la lectura sin bloqueo y la transacción corta.
/// </summary>
internal sealed class RepositorioDeAccesosEnMemoria : IRepositorioDeAccesos, IInstantanea
{
    private Dictionary<Guid, Acceso> _accesos = [];

    public IReadOnlyCollection<Acceso> Todos => _accesos.Values;

    /// <summary>Se ejecuta justo después de cada lectura sin bloqueo de un acceso (para simular una carrera).</summary>
    public Action? DespuesDeLeerUnAcceso { get; set; }

    public bool NoDisponible { get; set; }

    public int Conteos { get; private set; }

    public Acceso Sembrar(Guid cuentaId, Guid edicionId, Guid? pedidoId = null, DateTimeOffset? ahora = null)
    {
        var acceso = Acceso.Conceder(cuentaId, edicionId, pedidoId ?? Guid.NewGuid(), ahora ?? new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        _accesos[acceso.Id] = acceso;
        return acceso;
    }

    public void Eliminar(Guid accesoId) => _accesos.Remove(accesoId);

    public Acceso? Buscar(Guid accesoId) => _accesos.GetValueOrDefault(accesoId);

    public static Acceso Clonar(Acceso a) =>
        Acceso.Reconstruir(a.Id, a.CuentaId, a.EdicionId, a.PedidoId, a.Estado, a.DescargasUsadas, a.EnlacesEntregados, a.ConcedidoEn, a.ReactivadoEn, a.RevocadoEn, a.CausaDeRevocacion, a.SuspendidoEn);

    private void Verificar()
    {
        if (NoDisponible)
        {
            throw new BibliotecaNoDisponibleException("sin base");
        }
    }

    public Task<Acceso?> Obtener(Guid accesoId, Guid? cuentaId, CancellationToken cancelacion = default)
    {
        Verificar();
        Acceso? resultado = _accesos.TryGetValue(accesoId, out var a) && (cuentaId is null || a.CuentaId == cuentaId) ? Clonar(a) : null;
        DespuesDeLeerUnAcceso?.Invoke();
        return Task.FromResult(resultado);
    }

    public Task<IReadOnlyList<Acceso>> ListarDeLaCuenta(Guid cuentaId, int salto, int tomar, CancellationToken cancelacion = default)
    {
        Verificar();
        IReadOnlyList<Acceso> lista = [.. _accesos.Values.Where(a => a.CuentaId == cuentaId).OrderByDescending(a => a.FechaDeCompra).ThenBy(a => a.Id).Skip(salto).Take(tomar).Select(Clonar)];
        return Task.FromResult(lista);
    }

    public Task<int> ContarDeLaCuenta(Guid cuentaId, CancellationToken cancelacion = default)
    {
        Verificar();
        return Task.FromResult(_accesos.Values.Count(a => a.CuentaId == cuentaId));
    }

    public Task<IReadOnlyList<Guid>> EdicionesPoseidas(Guid cuentaId, IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        Verificar();
        IReadOnlyList<Guid> poseidas = [.. _accesos.Values.Where(a => a.CuentaId == cuentaId && a.Estado != EstadoDeAcceso.Revocado && edicionIds.Contains(a.EdicionId)).Select(a => a.EdicionId)];
        return Task.FromResult(poseidas);
    }

    public Task<Acceso?> ObtenerDeLaCompra(Guid cuentaId, Guid pedidoId, Guid edicionId, CancellationToken cancelacion = default)
    {
        Verificar();
        var acceso = _accesos.Values.FirstOrDefault(a => a.CuentaId == cuentaId && a.PedidoId == pedidoId && a.EdicionId == edicionId);
        return Task.FromResult(acceso is null ? null : Clonar(acceso));
    }

    public Task<ResultadoDeConcesion> Conceder(Guid cuentaId, Guid edicionId, Guid pedidoId, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        Verificar();
        var existente = _accesos.Values.FirstOrDefault(a => a.CuentaId == cuentaId && a.EdicionId == edicionId);
        if (existente is null)
        {
            var nuevo = Acceso.Conceder(cuentaId, edicionId, pedidoId, ahora);
            _accesos[nuevo.Id] = nuevo;
            return Task.FromResult(ResultadoDeConcesion.Concedido);
        }

        return Task.FromResult(existente.Reconceder(pedidoId, ahora));
    }

    public Task<CambioDeAcceso<ResultadoDeRevocacion>> Revocar(Guid pedidoId, Guid edicionId, CausaDeRevocacion causa, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        Verificar();
        var acceso = _accesos.Values.FirstOrDefault(a => a.PedidoId == pedidoId && a.EdicionId == edicionId);
        if (acceso is null)
        {
            return Task.FromResult(new CambioDeAcceso<ResultadoDeRevocacion>(ResultadoDeRevocacion.YaRevocado, null));
        }

        var resultado = acceso.Revocar(pedidoId, causa, ahora);
        return Task.FromResult(new CambioDeAcceso<ResultadoDeRevocacion>(resultado, Clonar(acceso)));
    }

    public Task<CambioDeAcceso<ResultadoDeRevocacionSinEnlace>> RevocarSiNoHayEnlace(Guid cuentaId, Guid pedidoId, Guid edicionId, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        Verificar();
        var acceso = _accesos.Values.FirstOrDefault(a => a.CuentaId == cuentaId && a.EdicionId == edicionId);
        if (acceso is null)
        {
            return Task.FromResult(new CambioDeAcceso<ResultadoDeRevocacionSinEnlace>(ResultadoDeRevocacionSinEnlace.SinAcceso, null));
        }

        var resultado = acceso.RevocarSiNoHayEnlace(pedidoId, CausaDeRevocacion.Reembolso, ahora);
        return Task.FromResult(new CambioDeAcceso<ResultadoDeRevocacionSinEnlace>(resultado, resultado == ResultadoDeRevocacionSinEnlace.SinAcceso ? null : Clonar(acceso)));
    }

    public Task<CambioDeAcceso<ResultadoDeSuspension>> Suspender(Guid cuentaId, Guid accesoId, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        Verificar();
        if (!_accesos.TryGetValue(accesoId, out var acceso) || acceso.CuentaId != cuentaId)
        {
            return Task.FromResult(new CambioDeAcceso<ResultadoDeSuspension>(ResultadoDeSuspension.NoDisponible, null));
        }

        return Task.FromResult(new CambioDeAcceso<ResultadoDeSuspension>(acceso.Suspender(ahora), Clonar(acceso)));
    }

    public Task<CambioDeAcceso<ResultadoDeReanudacion>> Reanudar(Guid cuentaId, Guid accesoId, CancellationToken cancelacion = default)
    {
        Verificar();
        if (!_accesos.TryGetValue(accesoId, out var acceso) || acceso.CuentaId != cuentaId)
        {
            return Task.FromResult(new CambioDeAcceso<ResultadoDeReanudacion>(ResultadoDeReanudacion.NoDisponible, null));
        }

        return Task.FromResult(new CambioDeAcceso<ResultadoDeReanudacion>(acceso.Reanudar(), Clonar(acceso)));
    }

    public Task<ResultadoDelConteoAtomico> ConsumirCupo(Guid accesoId, Guid cuentaId, CancellationToken cancelacion = default)
    {
        Verificar();
        Conteos++;
        var contada = _accesos.TryGetValue(accesoId, out var acceso) && acceso.CuentaId == cuentaId && acceso.ConsumirDescarga() == ResultadoDeConsumo.Consumida;
        return Task.FromResult(contada ? ResultadoDelConteoAtomico.Contada : ResultadoDelConteoAtomico.NoAfecto);
    }

    public Task<Acceso?> Restituir(Guid accesoId, int cantidad, CancellationToken cancelacion = default)
    {
        Verificar();
        if (_accesos.TryGetValue(accesoId, out var acceso) && acceso.Restituir(cantidad) == ResultadoDeRestitucion.Restituida)
        {
            return Task.FromResult<Acceso?>(Clonar(acceso));
        }

        return Task.FromResult<Acceso?>(null);
    }

    public Task<int> EliminarDeLaCuenta(Guid cuentaId, CancellationToken cancelacion = default)
    {
        Verificar();
        var ids = _accesos.Values.Where(a => a.CuentaId == cuentaId).Select(a => a.Id).ToList();
        foreach (var id in ids)
        {
            _accesos.Remove(id);
        }

        EnCascada?.Invoke(ids);
        return Task.FromResult(ids.Count);
    }

    /// <summary>La cascada de las claves foráneas hacia el repositorio de descargas.</summary>
    public Action<IReadOnlyList<Guid>>? EnCascada { get; set; }

    public object Capturar() => _accesos.ToDictionary(p => p.Key, p => Clonar(p.Value));

    public void Restaurar(object instantanea) => _accesos = (Dictionary<Guid, Acceso>)instantanea;
}

internal sealed class RepositorioDeDescargasEnMemoria(RepositorioDeAccesosEnMemoria accesos) : IRepositorioDeDescargas, IInstantanea
{
    private List<RegistroDeDescarga> _registros = [];
    private Dictionary<(Guid Acceso, string Clave), ClaveRegistrada> _claves = [];
    private List<SolicitudDeAyuda> _ayudas = [];

    public IReadOnlyList<RegistroDeDescarga> Registros => _registros;

    public IReadOnlyCollection<ClaveRegistrada> Claves => _claves.Values;

    public IReadOnlyList<SolicitudDeAyuda> Ayudas => _ayudas;

    /// <summary>Si es verdadero, el registro de descargas falla (para probar la reversión de la cancelación, AC-BIB-25).</summary>
    public bool FallaAlEliminar { get; set; }

    /// <summary>Si es verdadero, registrar la clave falla como una violación de la clave foránea (23503): el acceso desapareció tras el conteo (BIB-28).</summary>
    public bool ForzarAccesoInexistente { get; set; }

    public void Enlazar()
    {
        accesos.EnCascada = ids =>
        {
            if (FallaAlEliminar)
            {
                throw new BibliotecaNoDisponibleException("no se puede eliminar el registro de descargas");
            }

            _registros.RemoveAll(r => r.AccesoId is { } a && ids.Contains(a));
            foreach (var clave in _claves.Keys.Where(k => ids.Contains(k.Acceso)).ToList())
            {
                _claves.Remove(clave);
            }

            _ayudas.RemoveAll(a => ids.Contains(a.AccesoId));
        };
    }

    public void SembrarRegistro(RegistroDeDescarga registro) => _registros.Add(registro);

    public void SembrarAyuda(SolicitudDeAyuda ayuda) => _ayudas.Add(ayuda);

    public Task Registrar(RegistroDeDescarga registro, CancellationToken cancelacion = default)
    {
        _registros.Add(registro);
        return Task.CompletedTask;
    }

    public Task<ClaveRegistrada?> ObtenerClave(Guid accesoId, ClaveDeDescarga clave, CancellationToken cancelacion = default) =>
        Task.FromResult(_claves.GetValueOrDefault((accesoId, clave.Hexadecimal)));

    public Task<ResultadoDeRegistrarClave> RegistrarClave(ClaveRegistrada clave, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        if (ForzarAccesoInexistente || accesos.Buscar(clave.AccesoId) is null)
        {
            return Task.FromResult(ResultadoDeRegistrarClave.AccesoInexistente);
        }

        var llave = (clave.AccesoId, clave.Clave.Hexadecimal);
        if (_claves.TryGetValue(llave, out var existente) && existente.VenceEn > ahora)
        {
            return Task.FromResult(ResultadoDeRegistrarClave.RepeticionVigente);
        }

        _claves[llave] = clave;
        return Task.FromResult(ResultadoDeRegistrarClave.Registrada);
    }

    public Task<int> PurgarClaves(DateTimeOffset antes, CancellationToken cancelacion = default)
    {
        var vencidas = _claves.Where(p => p.Value.VenceEn <= antes).Select(p => p.Key).ToList();
        foreach (var llave in vencidas)
        {
            _claves.Remove(llave);
        }

        return Task.FromResult(vencidas.Count);
    }

    public Task<int> DepurarRegistros(DateOnly diaLimite, CancellationToken cancelacion = default)
    {
        var cuantos = 0;
        for (var i = 0; i < _registros.Count; i++)
        {
            if (!_registros[i].EstaDepurado && _registros[i].Dia <= diaLimite)
            {
                _registros[i] = _registros[i].Depurar();
                cuantos++;
            }
        }

        return Task.FromResult(cuantos);
    }

    public Task<SolicitudDeAyuda?> ObtenerAyudaAbierta(Guid accesoId, CancellationToken cancelacion = default) =>
        Task.FromResult(_ayudas.FirstOrDefault(a => a.AccesoId == accesoId && a.Estado == EstadoDeAyuda.Abierta));

    public Task<SolicitudDeAyuda?> ObtenerAyuda(Guid ayudaId, CancellationToken cancelacion = default) =>
        Task.FromResult(_ayudas.FirstOrDefault(a => a.Id == ayudaId));

    public Task<bool> AgregarAyuda(SolicitudDeAyuda ayuda, CancellationToken cancelacion = default)
    {
        if (_ayudas.Any(a => a.AccesoId == ayuda.AccesoId && a.Estado == EstadoDeAyuda.Abierta))
        {
            return Task.FromResult(false);
        }

        _ayudas.Add(ayuda);
        return Task.FromResult(true);
    }

    public Task<bool> CerrarAyuda(Guid ayudaId, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        var ayuda = _ayudas.FirstOrDefault(a => a.Id == ayudaId);
        return Task.FromResult(ayuda is not null && ayuda.Cerrar(ahora));
    }

    public Task<(IReadOnlyList<SolicitudDeAyuda> Ayudas, int Total)> ListarAyudas(EstadoDeAyuda? estado, int salto, int tomar, CancellationToken cancelacion = default)
    {
        var filtradas = _ayudas.Where(a => estado is null || a.Estado == estado).OrderByDescending(a => a.CreadaEn).ThenBy(a => a.Id).ToList();
        return Task.FromResult<(IReadOnlyList<SolicitudDeAyuda>, int)>(([.. filtradas.Skip(salto).Take(tomar)], filtradas.Count));
    }

    public Task<IReadOnlyList<Guid>> AccesosConAyudaAbierta(IReadOnlyCollection<Guid> accesoIds, CancellationToken cancelacion = default)
    {
        IReadOnlyList<Guid> con = [.. _ayudas.Where(a => a.Estado == EstadoDeAyuda.Abierta && accesoIds.Contains(a.AccesoId)).Select(a => a.AccesoId).Distinct()];
        return Task.FromResult(con);
    }

    public Task<int> DepurarAyudas(DateOnly diaLimite, CancellationToken cancelacion = default)
    {
        var cuantas = _ayudas.RemoveAll(a => DateOnly.FromDateTime(a.CreadaEn.ToOffset(TimeSpan.FromHours(-5)).DateTime) <= diaLimite);
        return Task.FromResult(cuantas);
    }

    public object Capturar() => (_registros.ToList(), new Dictionary<(Guid, string), ClaveRegistrada>(_claves), _ayudas.Select(a => SolicitudDeAyuda.Reconstruir(a.Id, a.AccesoId, a.Motivo, a.Estado, a.CreadaEn, a.CerradaEn)).ToList());

    public void Restaurar(object instantanea)
    {
        var (registros, claves, ayudas) = ((List<RegistroDeDescarga>, Dictionary<(Guid, string), ClaveRegistrada>, List<SolicitudDeAyuda>))instantanea;
        _registros = registros;
        _claves = claves;
        _ayudas = ayudas;
    }
}

/// <summary>El almacén de objetos de prueba: contenido sintético, nunca un archivo. La firma es determinista y <b>no usa el reloj</b>; cuenta las llamadas y si ocurrieron dentro de una transacción (ninguna red dentro de una, D05).</summary>
internal sealed class EntregaDeArchivosDoble(UnidadDeTrabajoReversible unidad) : IEntregaDeArchivos
{
    private readonly Dictionary<string, ExistenciaDelArchivo> _existencia = [];

    public ExistenciaDelArchivo PorOmision { get; set; } = ExistenciaDelArchivo.Existe;

    public int Comprobaciones { get; private set; }

    public int Firmas { get; private set; }

    public int LlamadasDentroDeLaTransaccion { get; private set; }

    public List<(string Clave, string Nombre, DateTimeOffset FirmadoEn, int Vigencia)> Enlaces { get; } = [];

    public bool FallaLaFirma { get; set; }

    public void Poner(string clave, ExistenciaDelArchivo existencia) => _existencia[clave] = existencia;

    public Task<ExistenciaDelArchivo> ExisteArchivo(string claveEnAlmacen, CancellationToken cancelacion = default)
    {
        Comprobaciones++;
        if (unidad.EnCurso)
        {
            LlamadasDentroDeLaTransaccion++;
        }

        return Task.FromResult(_existencia.GetValueOrDefault(claveEnAlmacen, PorOmision));
    }

    public Uri FirmarEnlace(string claveEnAlmacen, string nombreDeArchivo, DateTimeOffset firmadoEn, int vigenciaEnSegundos)
    {
        Firmas++;
        if (FallaLaFirma)
        {
            throw new InvalidOperationException("firma imposible");
        }

        Enlaces.Add((claveEnAlmacen, nombreDeArchivo, firmadoEn, vigenciaEnSegundos));
        return new Uri($"https://almacen.prueba/{claveEnAlmacen}?X-Amz-Date={firmadoEn.UtcTicks}&X-Amz-Expires={vigenciaEnSegundos}&X-Amz-Signature=firma-sintetica");
    }
}

/// <summary>La referencia de los archivos del Catálogo para Biblioteca: se siembra por edición; sin sembrar, «no encontrada».</summary>
internal sealed class ReferenciaDeArchivosFalsa : IReferenciaDeArchivosDelCatalogo
{
    private readonly Dictionary<Guid, ReferenciaDeArchivoDigital> _referencias = [];

    public bool Caida { get; set; }

    public ReferenciaDeArchivoDigital Poner(Guid edicionId, string clave = "libros/sintetico.epub", FormatoDeArchivoDigital formato = FormatoDeArchivoDigital.Epub)
    {
        var referencia = new ReferenciaDeArchivoDigital(edicionId, clave, formato, new string('a', 64), 1024);
        _referencias[edicionId] = referencia;
        return referencia;
    }

    public Task<Resultado<ReferenciaDeArchivoDigital>> ObtenerDeEdicion(Guid edicionId, CancellationToken cancelacion = default)
    {
        if (Caida)
        {
            return Task.FromResult(Resultado<ReferenciaDeArchivoDigital>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "catalogo-no-disponible")));
        }

        return Task.FromResult(_referencias.TryGetValue(edicionId, out var r)
            ? Resultado<ReferenciaDeArchivoDigital>.Exito(r)
            : Resultado<ReferenciaDeArchivoDigital>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "edicion-no-encontrada")));
    }
}

/// <summary>Los límites de Biblioteca en memoria: un tope por política, el rastro de lo reservado (sujeto y clave del llamador) y la idempotencia por <c>claveDelLlamador</c> (la misma clave no cuenta otra vez, SEG-22).</summary>
internal sealed class LimitesDeBibliotecaDoble : IControlDeLimites
{
    private readonly Dictionary<string, int> _usadas = [];
    private readonly HashSet<string> _claves = [];

    public Dictionary<string, int> Maximos { get; } = [];

    public bool Caido { get; set; }

    /// <summary>La reserva de la clave del llamador está «en curso» en el almacén de contadores (devuelve conflicto de idempotencia).</summary>
    public bool EnCurso { get; set; }

    public List<(string Politica, string? Sujeto, string? ClaveDelLlamador)> Reservas { get; } = [];

    public int Usadas(string politica) => _usadas.GetValueOrDefault(politica);

    public Task<ResultadoDeLimite> Reservar(
        ClaveDePolitica politica,
        string? sujeto = null,
        string? claveDelLlamador = null,
        HuellaDeSujeto? huellaDelLlamador = null,
        ClaveDeIdempotencia? idempotencia = null,
        CancellationToken cancelacion = default)
    {
        Reservas.Add((politica.Valor, sujeto, claveDelLlamador));
        if (Caido)
        {
            return Task.FromResult(ResultadoDeLimite.NoDisponible());
        }

        if (EnCurso)
        {
            return Task.FromResult(ResultadoDeLimite.RepeticionEnCurso());
        }

        var reserva = new ReservaDeLimite(politica.Valor, "usuario", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
        if (claveDelLlamador is not null && !_claves.Add($"{politica.Valor}|{sujeto}|{claveDelLlamador}"))
        {
            return Task.FromResult(ResultadoDeLimite.Permitido(reserva));
        }

        var usadas = _usadas.GetValueOrDefault(politica.Valor);
        if (usadas >= Maximos.GetValueOrDefault(politica.Valor, int.MaxValue))
        {
            return Task.FromResult(ResultadoDeLimite.Excedido(politica.Valor, 1800, DateTimeOffset.UtcNow.AddMinutes(30)));
        }

        _usadas[politica.Valor] = usadas + 1;
        return Task.FromResult(ResultadoDeLimite.Permitido(reserva));
    }

    public Task Liberar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;
}
