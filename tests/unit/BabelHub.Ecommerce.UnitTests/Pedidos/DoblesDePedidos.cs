using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
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

    public Task AgregarEntrega(Entrega entrega, CancellationToken cancelacion = default)
    {
        Llamadas.Add("agregar-entrega");
        Entregas.Add(entrega);
        return Task.CompletedTask;
    }

    public Task ActualizarEntrega(Entrega entrega, CancellationToken cancelacion = default)
    {
        Llamadas.Add("actualizar-entrega");
        return Task.CompletedTask;
    }

    public Task AgregarAnulacion(Anulacion anulacion, CancellationToken cancelacion = default)
    {
        Llamadas.Add("agregar-anulacion");
        Anulaciones.Add(anulacion);
        return Task.CompletedTask;
    }

    public Task ActualizarAnulacion(Anulacion anulacion, CancellationToken cancelacion = default)
    {
        Llamadas.Add("actualizar-anulacion");
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

    public Task<IReadOnlyList<Anulacion>> AnulacionesFallidas(Guid pedidoId, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<Anulacion>>([.. Anulaciones.Where(a => a.PedidoId == pedidoId && a.Estado == EstadoDeOperacion.Fallida)]);

    public Task<OperacionesPorReanudar> OperacionesEnCursoParaReanudar(DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default) =>
        Task.FromResult(new OperacionesPorReanudar(
            [.. Anulaciones.Where(a => a.Estado == EstadoDeOperacion.EnCurso && a.ProximoIntentoEn is { } p && p <= ahora).OrderBy(a => a.CreadaEn).Take(maximo)],
            [.. Entregas.Where(e => e.Estado == EstadoDeOperacion.EnCurso && e.ProximoIntentoEn is { } p && p <= ahora).OrderBy(e => e.CreadaEn).Take(maximo)]));

    public List<LiberacionDeReservaPendiente> LiberacionesDeReserva { get; } = [];

    public Task<IReadOnlyList<Guid>> PedidosConRecojoVencido(DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([.. _pedidos.Values
            .Where(p => p.Tipo == TipoDeEntregaDelPedido.Recojo && p.Lineas.Any(l => l.EstadoImpreso == EstadoDeLineaImpresa.ListoParaRecojo && l.ListoEn is { } listo && PoliticaDeNoRecogido.EstaVencida(listo, ahora)))
            .Select(p => p.Id)
            .Take(maximo)]);

    public Task RegistrarLiberacionesDeReserva(Guid pedidoId, IReadOnlyCollection<Guid> lineaIds, string motivo, DateTimeOffset desde, CancellationToken cancelacion = default)
    {
        Llamadas.Add("registrar-liberaciones-de-reserva");
        foreach (var linea in lineaIds.Where(l => LiberacionesDeReserva.All(x => x.LineaId != l)))
        {
            LiberacionesDeReserva.Add(new LiberacionDeReservaPendiente(pedidoId, linea, motivo, desde, 0));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LiberacionDeReservaPendiente>> LiberacionesDeReservaPendientes(int maximo, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<LiberacionDeReservaPendiente>>([.. LiberacionesDeReserva.OrderBy(l => l.PendienteDesde).Take(maximo)]);

    public Task QuitarLiberacionesDeReserva(IReadOnlyCollection<Guid> lineaIds, CancellationToken cancelacion = default)
    {
        LiberacionesDeReserva.RemoveAll(l => lineaIds.Contains(l.LineaId));
        return Task.CompletedTask;
    }

    public Task AnotarIntentoDeLiberacion(IReadOnlyCollection<Guid> lineaIds, CancellationToken cancelacion = default)
    {
        for (var i = 0; i < LiberacionesDeReserva.Count; i++)
        {
            if (lineaIds.Contains(LiberacionesDeReserva[i].LineaId))
            {
                LiberacionesDeReserva[i] = LiberacionesDeReserva[i] with { Intentos = LiberacionesDeReserva[i].Intentos + 1 };
            }
        }

        return Task.CompletedTask;
    }
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

    /// <summary>Se ejecuta justo antes de revocar (para comprobar el orden: el reembolso se registra antes).</summary>
    public Action? AntesDeRevocar { get; set; }

    public Task<Resultado<ResultadoDeRevocarAcceso>> Revocar(RevocarAccesoEntrada entrada, CancellationToken cancelacion = default)
    {
        AntesDeRevocar?.Invoke();
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

    public bool FallaAlRegistrar { get; set; }

    /// <summary>El estado con el que responde <c>Procesar</c>; <see langword="null"/> simula la pasarela caída (un <c>Fallo</c>).</summary>
    public EstadoDelReembolso? EstadoAlProcesar { get; set; } = EstadoDelReembolso.Reembolsado;

    public List<Guid> Procesados { get; } = [];

    public List<Guid> Reintentados { get; } = [];

    private readonly Dictionary<string, Guid> _porClave = [];

    public Task<Resultado<ReembolsoRegistrado>> Registrar(RegistrarReembolsoEntrada entrada, CancellationToken cancelacion = default)
    {
        if (FallaAlRegistrar)
        {
            return Task.FromResult(Resultado<ReembolsoRegistrado>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible")));
        }

        Registrados.Add(entrada);
        var monto = entrada.Lineas.Sum(l => l.Monto) + entrada.Envio;
        var repetido = _porClave.TryGetValue(entrada.ClaveNatural, out var id);
        if (!repetido)
        {
            id = Guid.NewGuid();
            _porClave[entrada.ClaveNatural] = id;
        }

        return Task.FromResult(Resultado<ReembolsoRegistrado>.Exito(new ReembolsoRegistrado(id, monto, EstadoDelReembolso.Pendiente, repetido)));
    }

    public Task<Resultado<EstadoDelReembolso>> Procesar(Guid reembolsoId, CancellationToken cancelacion = default)
    {
        Procesados.Add(reembolsoId);
        return Task.FromResult(EstadoAlProcesar is { } estado
            ? Resultado<EstadoDelReembolso>.Exito(estado)
            : Resultado<EstadoDelReembolso>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible")));
    }

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

    public Task<Resultado<EstadoDelReembolso>> Reintentar(Guid reembolsoId, CancellationToken cancelacion = default)
    {
        Reintentados.Add(reembolsoId);
        return Task.FromResult(EstadoAlProcesar is { } estado
            ? Resultado<EstadoDelReembolso>.Exito(estado)
            : Resultado<EstadoDelReembolso>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible")));
    }
}

/// <summary>El despacho de Envíos para las pruebas: crea y cancela con lo que se le configure (un envío cancelado o mantenido por identificador).</summary>
internal sealed class DespachoDeEnviosFalso : IDespachoDeEnvios
{
    public List<Guid> Cancelaciones { get; } = [];

    public List<ResultadoDeCancelarEnvio> Resultado { get; } = [];

    public bool Falla { get; set; }

    public bool Lanza { get; set; }

    public Task<Resultado<ResultadoDeCrearEnvio>> Crear(CrearEnvioEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<IReadOnlyList<ResultadoDeCancelarEnvio>>> Cancelar(Guid pedidoId, CancellationToken cancelacion = default)
    {
        if (Lanza)
        {
            throw new InvalidOperationException("Envíos cayó a mitad de la operación");
        }

        Cancelaciones.Add(pedidoId);
        return Task.FromResult(Falla
            ? Resultado<IReadOnlyList<ResultadoDeCancelarEnvio>>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"))
            : Resultado<IReadOnlyList<ResultadoDeCancelarEnvio>>.Exito([.. Resultado]));
    }
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

internal sealed class ReservasFalsas : IReservasDelPedido
{
    public List<(Guid Pedido, Guid Linea, string Clave)> Cierres { get; } = [];

    public List<(Guid Pedido, string Motivo, IReadOnlyList<Guid> Lineas)> Liberaciones { get; } = [];

    public Func<Guid, ResultadoDeCerrarReserva> Respuesta { get; set; } = _ => ResultadoDeCerrarReserva.Cerrada;

    public Func<ResultadoDeLiberarReserva> RespuestaDeLiberar { get; set; } = () => ResultadoDeLiberarReserva.Liberada;

    /// <summary>Si se fija, decide la respuesta de <c>Liberar</c> según las líneas pedidas (un 409 solo para alguna).</summary>
    public Func<IReadOnlyList<Guid>, ResultadoDeLiberarReserva>? RespuestaPorLineas { get; set; }

    public Task<ResultadoDeCerrarReserva> Cerrar(Guid pedidoId, Guid lineaId, string claveDeCierre, CancellationToken cancelacion = default)
    {
        Cierres.Add((pedidoId, lineaId, claveDeCierre));
        return Task.FromResult(Respuesta(lineaId));
    }

    public Task<ResultadoDeLiberarReserva> Liberar(Guid pedidoId, string motivo, IReadOnlyList<Guid> lineaIds, CancellationToken cancelacion = default)
    {
        Liberaciones.Add((pedidoId, motivo, lineaIds));
        return Task.FromResult(RespuestaPorLineas is { } porLineas ? porLineas(lineaIds) : RespuestaDeLiberar());
    }
}

internal sealed class GeneradorDeIdentificadoresFalso : IGeneradorDeIdentificadoresDePedidos
{
    public Guid Nuevo() => Guid.NewGuid();
}

/// <summary>Una auditoría que guarda lo registrado y puede caer desde el principio o a partir de la N-ésima llamada (probar el tiempo 3 de una operación).</summary>
internal sealed class AuditoriaDePedidosEspia : BabelHub.Ecommerce.Application.Auditoria.IAuditoria
{
    public List<BabelHub.Ecommerce.Application.Auditoria.SolicitudDeRegistro> Solicitudes { get; } = [];

    public bool Caida { get; set; }

    /// <summary>Si se fija, las llamadas posteriores a esa cantidad de registros fallan.</summary>
    public int? CaeDespuesDe { get; set; }

    public Task Registrar(BabelHub.Ecommerce.Application.Auditoria.SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
    {
        if (Caida || (CaeDespuesDe is { } limite && Solicitudes.Count >= limite))
        {
            return Task.FromException(new BabelHub.Ecommerce.Application.Auditoria.AuditoriaNoDisponibleException("sin auditoría"));
        }

        VerificarContraElCatalogo(solicitud);
        Solicitudes.Add(solicitud);
        return Task.CompletedTask;
    }

    private static readonly BabelHub.Ecommerce.Domain.Auditoria.CatalogoDeAcciones Catalogo = BabelHub.Ecommerce.Application.Auditoria.Interno.CatalogoDeAccionesDeAuditoria.Crear();

    /// <summary>
    /// Lo que persistiría la auditoría real: una acción que no está en el catálogo, una clave del detalle que no declara o un valor fuera de su lista cerrada se descartan en silencio (<c>DetalleSeguro</c>). Este doble lo hace fallar para que una prueba no afirme un detalle que producción perdería (hallazgo de la revisión de ECO-6c).
    /// </summary>
    private static void VerificarContraElCatalogo(BabelHub.Ecommerce.Application.Auditoria.SolicitudDeRegistro solicitud)
    {
        if (!Catalogo.TryObtener(solicitud.Accion.Identificador, out var accion) || accion is null)
        {
            throw new InvalidOperationException($"La acción {solicitud.Accion} no está en el catálogo de auditoría.");
        }

        foreach (var (clave, valor) in solicitud.Detalle)
        {
            var declarada = accion.Detalle.FirstOrDefault(c => c.Nombre == clave)
                ?? throw new InvalidOperationException($"La clave «{clave}» no está declarada para {solicitud.Accion}: la auditoría real la descartaría.");
            if (declarada.Valores is { Count: > 0 } valores && valor is not null && !valores.Contains(valor))
            {
                throw new InvalidOperationException($"El valor de «{clave}» no está en la lista cerrada de {solicitud.Accion}.");
            }
        }
    }
}
