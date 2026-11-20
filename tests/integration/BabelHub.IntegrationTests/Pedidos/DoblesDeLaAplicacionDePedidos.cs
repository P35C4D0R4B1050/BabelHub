using System.Collections.Concurrent;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Shared.Errores;

namespace BabelHub.IntegrationTests.Pedidos;

// Los bordes de Pedidos para las pruebas de integración con la Aplicación real sobre PostgreSQL real (ECO-6f): el Inventario, Pagos, la Biblioteca, Envíos, los avisos y la auditoría son dobles
// seguros para hilos (las pruebas de concurrencia los llaman desde varias tareas a la vez). Parten de los dobles de las pruebas unitarias del módulo.

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
    public ConcurrentBag<(Guid CuentaId, Guid PedidoId, Guid EdicionId)> Concedidos { get; } = [];

    public ConcurrentBag<(Guid PedidoId, Guid EdicionId, MotivoDeRevocacionDeAcceso Motivo)> Revocados { get; } = [];

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
    public ConcurrentBag<(Guid PedidoId, string Rol)> Lecturas { get; } = [];

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

    public ConcurrentBag<RegistrarReembolsoEntrada> Registrados { get; } = [];

    public bool Falla { get; set; }

    public bool FallaAlRegistrar { get; set; }

    /// <summary>El estado con el que responde <c>Procesar</c>; <see langword="null"/> simula la pasarela caída (un <c>Fallo</c>).</summary>
    public EstadoDelReembolso? EstadoAlProcesar { get; set; } = EstadoDelReembolso.Reembolsado;

    public ConcurrentBag<Guid> Procesados { get; } = [];

    public ConcurrentBag<Guid> Reintentados { get; } = [];

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

    public Dictionary<(Guid CuentaId, OrigenDelReembolso Origen), int> PendientesPorCuenta { get; } = [];

    public bool FallaAlContar { get; set; }

    public Task<int> ContarPendientes(Guid cuentaId, OrigenDelReembolso origen, CancellationToken cancelacion = default) =>
        FallaAlContar ? throw new InvalidOperationException("Pagos caído") : Task.FromResult(PendientesPorCuenta.GetValueOrDefault((cuentaId, origen)));

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
    public ConcurrentBag<Guid> Cancelaciones { get; } = [];

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
    public ConcurrentBag<BabelHub.Ecommerce.Application.Notificaciones.SolicitudDeAviso> Solicitudes { get; } = [];

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

internal sealed class ReservasFalsas : IReservasDelPedido
{
    public ConcurrentBag<(Guid Pedido, Guid Linea, string Clave)> Cierres { get; } = [];

    public ConcurrentBag<(Guid Pedido, string Motivo, IReadOnlyList<Guid> Lineas)> Liberaciones { get; } = [];

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

internal sealed class AuditoriaDePedidosEspia : IAuditoria
{
    public ConcurrentBag<SolicitudDeRegistro> Solicitudes { get; } = [];

    public bool Caida { get; set; }

    /// <summary>Si se fija, las llamadas posteriores a esa cantidad de registros fallan.</summary>
    public int? CaeDespuesDe { get; set; }

    public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
    {
        if (Caida || (CaeDespuesDe is { } limite && Solicitudes.Count >= limite))
        {
            return Task.FromException(new AuditoriaNoDisponibleException("sin auditoría"));
        }

        VerificarContraElCatalogo(solicitud);
        Solicitudes.Add(solicitud);
        return Task.CompletedTask;
    }

    private static readonly BabelHub.Ecommerce.Domain.Auditoria.CatalogoDeAcciones Catalogo = BabelHub.Ecommerce.Application.Auditoria.Interno.CatalogoDeAccionesDeAuditoria.Crear();

    /// <summary>
    /// Lo que persistiría la auditoría real: una acción que no está en el catálogo, una clave del detalle que no declara o un valor fuera de su lista cerrada se descartan en silencio (<c>DetalleSeguro</c>). Este doble lo hace fallar para que una prueba no afirme un detalle que producción perdería (hallazgo de la revisión de ECO-6c).
    /// </summary>
    private static void VerificarContraElCatalogo(SolicitudDeRegistro solicitud)
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

/// <summary>La liberación del contacto de la intención de Pagos: anota los pedidos liberados y puede fallar.</summary>
internal sealed class LiberacionDeContactoDePagosFalsa : ILiberacionDeContactoDePago
{
    public ConcurrentBag<Guid> Liberados { get; } = [];

    public bool Falla { get; set; }

    public Task Liberar(Guid pedidoId, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            throw new InvalidOperationException("Pagos caído");
        }

        Liberados.Add(pedidoId);
        return Task.CompletedTask;
    }
}
