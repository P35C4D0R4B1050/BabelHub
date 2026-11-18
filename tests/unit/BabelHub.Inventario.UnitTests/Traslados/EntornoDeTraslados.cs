using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Domain.Traslados;
using BabelHub.Inventario.UnitTests.Ajustes;
using BabelHub.Inventario.UnitTests.Fuentes;
using BabelHub.Inventario.UnitTests.Soporte;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>Los traslados guardados, en memoria: cada lectura devuelve una <b>copia</b> (como una base de datos), de modo que un caso de uso que muta el traslado y falla no deja rastro: lo que no se guarda no cambia.</summary>
internal sealed class RepositorioDeTrasladosEnMemoria : IRepositorioDeTraslados
{
    private readonly Dictionary<Guid, Traslado> _guardados = [];

    private long _siguienteNumero = 1;

    public int Lecturas { get; private set; }

    public int LecturasBloqueadas { get; private set; }

    public int Escrituras { get; private set; }

    public IReadOnlyList<Traslado> Todos => [.. _guardados.Values.Select(Copiar)];

    public Traslado Almacenado(Guid id) => Copiar(_guardados[id]);

    /// <summary>Guarda un traslado tal como está (con su estado y su número) para empezar una prueba desde ahí.</summary>
    public Traslado Sembrar(Traslado traslado)
    {
        if (traslado.Numero == 0)
        {
            traslado.AsignarNumero(_siguienteNumero++);
        }

        _guardados[traslado.Id] = Copiar(traslado);
        return traslado;
    }

    public Task Insertar(Traslado traslado, CancellationToken cancelacion = default)
    {
        traslado.AsignarNumero(_siguienteNumero++);
        _guardados[traslado.Id] = Copiar(traslado);
        Escrituras++;
        return Task.CompletedTask;
    }

    public Task<Traslado?> PorId(Guid trasladoId, CancellationToken cancelacion = default)
    {
        Lecturas++;
        return Task.FromResult(_guardados.TryGetValue(trasladoId, out var t) ? Copiar(t) : null);
    }

    public Task<Traslado?> PorIdBloqueado(Guid trasladoId, CancellationToken cancelacion = default)
    {
        LecturasBloqueadas++;
        return Task.FromResult(_guardados.TryGetValue(trasladoId, out var t) ? Copiar(t) : null);
    }

    public Task<bool> Guardar(Traslado traslado, long versionAnterior, CancellationToken cancelacion = default)
    {
        if (!_guardados.TryGetValue(traslado.Id, out var actual) || actual.Version != versionAnterior)
        {
            return Task.FromResult(false);
        }

        _guardados[traslado.Id] = Copiar(traslado);
        Escrituras++;
        return Task.FromResult(true);
    }

    /// <summary>Simula que otra confirmación del mismo pedido creó el traslado entre la lectura y la inserción: la inserción choca con el índice único.</summary>
    public bool ChocaConUnoConcurrente { get; set; }

    public Task<bool> InsertarVinculado(Traslado traslado, CancellationToken cancelacion = default)
    {
        if (ChocaConUnoConcurrente)
        {
            var otro = Traslado.VincularAUnPedido(Guid.NewGuid(), traslado.EdicionId, traslado.OrigenId, traslado.DestinoId, traslado.Cantidad, traslado.PedidoId!.Value, new EstimacionDeLlegada(traslado.FechaEstimada, traslado.EstimadaPorConfirmar), null, traslado.SolicitadoEn);
            Sembrar(otro);
            ChocaConUnoConcurrente = false;
            return Task.FromResult(false);
        }

        if (_guardados.Values.Any(t => t.PedidoId == traslado.PedidoId && t.EdicionId == traslado.EdicionId && t.OrigenId == traslado.OrigenId && t.Estado != EstadoDeTraslado.Cancelado))
        {
            return Task.FromResult(false);
        }

        traslado.AsignarNumero(_siguienteNumero++);
        _guardados[traslado.Id] = Copiar(traslado);
        Escrituras++;
        return Task.FromResult(true);
    }

    public Task<Traslado?> VinculadoActivo(Guid pedidoId, Guid edicionId, Guid origenId, CancellationToken cancelacion = default)
    {
        var t = _guardados.Values.FirstOrDefault(x => x.PedidoId == pedidoId && x.EdicionId == edicionId && x.OrigenId == origenId && x.Estado != EstadoDeTraslado.Cancelado);
        return Task.FromResult(t is null ? null : Copiar(t));
    }

    public Task<IReadOnlyList<Traslado>> PorIdsBloqueados(IReadOnlyCollection<Guid> trasladoIds, CancellationToken cancelacion = default)
    {
        LecturasBloqueadas++;
        return Task.FromResult<IReadOnlyList<Traslado>>([.. _guardados.Values.Where(t => trasladoIds.Contains(t.Id)).OrderBy(t => t.Id).Select(Copiar)]);
    }


    public Task<PaginaDeTrasladosGuardados> Listar(FiltroDeTraslados filtro, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        var todos = _guardados.Values
            .Where(t => filtro.Estado is null || t.Estado == filtro.Estado)
            .Where(t => filtro.FuenteId is null || t.OrigenId == filtro.FuenteId || t.DestinoId == filtro.FuenteId)
            .Where(t => filtro.Vinculado is null || t.EsVinculado == filtro.Vinculado)
            .Where(t => filtro.SolicitadoAntesDe is null || (t.Estado == EstadoDeTraslado.Solicitado && t.SolicitadoEn <= filtro.SolicitadoAntesDe))
            .OrderByDescending(t => t.SolicitadoEn).ThenByDescending(t => t.Id)
            .ToList();
        return Task.FromResult(new PaginaDeTrasladosGuardados([.. todos.Skip((pagina - 1) * tamano).Take(tamano).Select(Copiar)], todos.Count));
    }

    public Task<IReadOnlyList<Traslado>> DelPedido(Guid pedidoId, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<Traslado>>([.. _guardados.Values.Where(t => t.PedidoId == pedidoId).OrderByDescending(t => t.SolicitadoEn).ThenByDescending(t => t.Id).Select(Copiar)]);


    public int Anonimizaciones { get; private set; }

    public Task<IReadOnlyList<Guid>> VinculadosSinAprobarVencidos(DateTimeOffset solicitadosAntesDe, int maximo, CancellationToken cancelacion = default)
    {
        LecturasBloqueadas++;
        return Task.FromResult<IReadOnlyList<Guid>>([.. _guardados.Values
            .Where(t => t.EsVinculado && t.Estado == EstadoDeTraslado.Solicitado && t.SolicitadoEn < solicitadosAntesDe && t.AlertaDeDemoraEn is null)
            .OrderBy(t => t.Id).Take(maximo).Select(t => t.Id)]);
    }

    public Task<bool> MarcarAlertaDeDemora(Guid trasladoId, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        if (!_guardados.TryGetValue(trasladoId, out var t) || t.Estado != EstadoDeTraslado.Solicitado || t.AlertaDeDemoraEn is not null)
        {
            return Task.FromResult(false);
        }

        _guardados[trasladoId] = Traslado.Rehidratar(t.Id, t.Numero, t.EdicionId, t.OrigenId, t.DestinoId, t.Cantidad, t.Estado, t.Motivo, t.MotivoDeCierre, t.PedidoId, t.PedidoAnulado, t.FechaEstimada, t.EstimadaPorConfirmar, t.Guia, t.SolicitadoPor, t.SolicitadoEn, t.AprobadoEn, t.DespachadoEn, t.RecibidoEn, t.CerradoEn, ahora, t.Version);
        return Task.FromResult(true);
    }

    public Task<int> AnonimizarCerrados(DateTimeOffset cerradosAntesDe, int lote, CancellationToken cancelacion = default)
    {
        var cambiar = _guardados.Values
            .Where(t => t.CerradoEn < cerradosAntesDe && (t.SolicitadoPor is not null || t.Motivo is not null || t.MotivoDeCierre is not null || t.Guia is not null))
            .OrderBy(t => t.CerradoEn).Take(lote).ToList();
        foreach (var t in cambiar)
        {
            _guardados[t.Id] = Traslado.Rehidratar(t.Id, t.Numero, t.EdicionId, t.OrigenId, t.DestinoId, t.Cantidad, t.Estado, null, null, t.PedidoId, t.PedidoAnulado, t.FechaEstimada, t.EstimadaPorConfirmar, null, null, t.SolicitadoEn, t.AprobadoEn, t.DespachadoEn, t.RecibidoEn, t.CerradoEn, t.AlertaDeDemoraEn, t.Version);
        }

        Anonimizaciones += cambiar.Count;
        return Task.FromResult(cambiar.Count);
    }


    public static Traslado Copiar(Traslado t) =>
        Traslado.Rehidratar(
            t.Id, t.Numero, t.EdicionId, t.OrigenId, t.DestinoId, t.Cantidad, t.Estado, t.Motivo, t.MotivoDeCierre, t.PedidoId, t.PedidoAnulado, t.FechaEstimada, t.EstimadaPorConfirmar, t.Guia, t.SolicitadoPor, t.SolicitadoEn,
            t.AprobadoEn, t.DespachadoEn, t.RecibidoEn, t.CerradoEn, t.AlertaDeDemoraEn, t.Version);
}

/// <summary>El buzón de salida en memoria: único por <c>(clave de evento, sucursal, tipo)</c>, como el índice de la base, sin abortar por un duplicado.</summary>
internal sealed class BuzonDeSalidaEnMemoria(BitacoraDeLlamadas bitacora) : IBuzonDeSalida
{
    public List<MensajeSaliente> Mensajes { get; } = [];

    public Task Insertar(IReadOnlyCollection<MensajeSaliente> mensajes, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("buzon.Insertar");
        foreach (var mensaje in mensajes)
        {
            if (!Mensajes.Any(m => m.ClaveDeEvento == mensaje.ClaveDeEvento && m.SucursalCodigo == mensaje.SucursalCodigo && m.Tipo == mensaje.Tipo))
            {
                Mensajes.Add(mensaje);
            }
        }

        return Task.CompletedTask;
    }
}

internal sealed class BloqueosDeTrasladosEspia(BitacoraDeLlamadas bitacora) : IBloqueosAsesores
{
    public List<Guid> Pedidos { get; } = [];

    public bool EsperaAgotada { get; set; }

    public Task Pedido(Guid pedidoId, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("bloqueo.Pedido");
        Pedidos.Add(pedidoId);
        return EsperaAgotada ? Task.FromException(new EsperaDeBloqueoAgotadaException()) : Task.CompletedTask;
    }

    public Task Cliente(Guid clienteId, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<bool> IntentarPedido(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task Recepcion(CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task Devolucion(Guid devolucionId, CancellationToken cancelacion = default) => throw new NotSupportedException();
}

/// <summary>
/// <see cref="IReservasParaTraslados"/> de prueba: devuelve lo que se le indica y, cuando aplica, mueve el stock en memoria como lo hace el real (consumir baja el físico y lo reservado del origen; recrear los sube en el destino). Recuerda
/// cada llamada. Con el módulo real se prueba en integración (T16).
/// </summary>
internal sealed class ReservasParaTrasladosDoble(RepositorioDeExistenciasEnMemoria existencias, BitacoraDeLlamadas bitacora) : IReservasParaTraslados
{
    public ReservaConfirmadaDeLaFuente? Confirmada { get; set; }

    public ResultadoDeMovimientoDeReserva ResultadoAlConsumir { get; set; } = ResultadoDeMovimientoDeReserva.Aplicado;

    public ResultadoDeMovimientoDeReserva ResultadoAlRecrear { get; set; } = ResultadoDeMovimientoDeReserva.Aplicado;

    public List<(Guid PedidoId, Guid FuenteId, Guid EdicionId)> Consumos { get; } = [];

    public List<(Guid PedidoId, Guid FuenteId, Guid EdicionId, int Cantidad, Guid TrasladoId)> Recreaciones { get; } = [];

    public Task<ReservaConfirmadaDeLaFuente?> ConsultarConfirmada(Guid pedidoId, Guid fuenteId, Guid edicionId, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.ConsultarConfirmada");
        return Task.FromResult(Confirmada is { } c && c.PedidoId == pedidoId && c.FuenteId == fuenteId && c.EdicionId == edicionId ? c : null);
    }

    public Task<ResultadoDeMovimientoDeReserva> ConsumirEnElOrigen(Guid pedidoId, Guid fuenteDeOrigenId, Guid edicionId, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.ConsumirEnElOrigen");
        Consumos.Add((pedidoId, fuenteDeOrigenId, edicionId));
        if (ResultadoAlConsumir == ResultadoDeMovimientoDeReserva.Aplicado && Confirmada is { } c)
        {
            var (fisico, reservado) = existencias.Filas[(fuenteDeOrigenId, edicionId)];
            existencias.Filas[(fuenteDeOrigenId, edicionId)] = (fisico - c.Cantidad, reservado - c.Cantidad);
        }

        return Task.FromResult(ResultadoAlConsumir);
    }

    public Task<ResultadoDeMovimientoDeReserva> RecrearEnElDestino(Guid pedidoId, Guid fuenteDeDestinoId, Guid edicionId, int cantidad, Guid trasladoId, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.RecrearEnElDestino");
        Recreaciones.Add((pedidoId, fuenteDeDestinoId, edicionId, cantidad, trasladoId));
        if (ResultadoAlRecrear == ResultadoDeMovimientoDeReserva.Aplicado)
        {
            var (fisico, reservado) = existencias.Filas.GetValueOrDefault((fuenteDeDestinoId, edicionId));
            existencias.Filas[(fuenteDeDestinoId, edicionId)] = (fisico + cantidad, reservado + cantidad);
        }

        return Task.FromResult(ResultadoAlRecrear);
    }
}

/// <summary>Todo lo que los casos de uso de <c>Traslados</c> necesitan, en memoria, sobre el entorno de <c>Fuentes</c> (Lima la de recepción, Cusco y Ayacucho) con el <c>MovimientosDeStock</c> y el directorio reales.</summary>
internal sealed partial class EntornoDeTraslados
{
    public static readonly DateTimeOffset Ahora = EntornoDeFuentes.Ahora;

    public EntornoDeTraslados(ActorDePrueba? actor = null)
    {
        Base = new EntornoDeFuentes(actor);
        Bloqueos = new BloqueosDeTrasladosEspia(Base.Bitacora);
        Buzon = new BuzonDeSalidaEnMemoria(Base.Bitacora);
        Reservas = new ReservasParaTrasladosDoble(Base.Existencias, Base.Bitacora);
        Directorio = new DirectorioDeFuentes(Base.Lectura, Base.Fuentes, Base.Rutas, Base.Fichas);
        Movimientos = new MovimientosDeStock(Base.Fichas, Base.Existencias, Base.Reservas, new AplicadorDeMovimientos(Base.Existencias));
        Existencias = new ConsultaDeExistencias(Base.Lectura, Base.Fuentes, Base.Existencias, Base.Reservas);
    }

    public EntornoDeFuentes Base { get; }

    public RepositorioDeTrasladosEnMemoria Traslados { get; } = new();

    public BuzonDeSalidaEnMemoria Buzon { get; }

    public ControlDeLimitesDoble Limites { get; } = new();

    public BloqueosDeTrasladosEspia Bloqueos { get; }

    public ReservasParaTrasladosDoble Reservas { get; }

    public DirectorioDeFuentes Directorio { get; }

    public MovimientosDeStock Movimientos { get; }

    public ConsultaDeExistencias Existencias { get; }

    public ActorDePrueba Actor => Base.Actor;

    public Guid Lima => Base.Lima.Id;

    public Guid Cusco => Base.Cusco.Id;

    public Guid Ayacucho => Base.Ayacucho.Id;

    public BabelHub.Inventario.Application.Auditoria.IAuditoria? AuditoriaDeLaPrueba { get; set; }

    public BabelHub.Inventario.Application.Auditoria.IAuditoria Auditoria => AuditoriaDeLaPrueba ?? Base.Auditoria;

    public AutorizadorDeTraslados Autorizador => new(Actor, Base.Denegaciones);

    public SolicitarTraslado Solicitar() =>
        new(Autorizador, new ValidadorDeSolicitarTraslado(), new PresupuestoDeTraslados(Limites, Base.Reloj), Directorio, Directorio, Existencias, Traslados, Buzon, Auditoria, Base.Reloj);

    public ConsultarOrigenesPosibles Origenes() => new(Autorizador, new ValidadorDeConsultarOrigenesPosibles(), Directorio, Directorio, Existencias);

    public Guid Impresa() => Base.Impresa();

    public Guid Digital() => Base.Fichas.Agregar(Domain.Fuentes.FormatoDeEdicion.Digital).EdicionId;

    public void Poner(Guid fuente, Guid edicion, int fisico, int reservado) => Base.Existencias.Poner(fuente, edicion, fisico, reservado);

    public (int Fisico, int Reservado) Stock(Guid fuente, Guid edicion) => Base.Existencias.Filas[(fuente, edicion)];

    public void ComoEncargadoDe(Guid sucursal) => Actor.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, sucursal));

    public void Como(string rol, Guid? sucursal = null) => Actor.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), rol, sucursal));

    /// <summary>Un traslado de reposición ya guardado en el estado pedido (sembrado con la solicitud del actor actual).</summary>
    public Traslado Reposicion(Guid edicion, Guid origen, Guid destino, int cantidad, EstadoDeTraslado estado = EstadoDeTraslado.Solicitado, string? guia = null)
    {
        var t = Traslado.SolicitarReposicion(Guid.NewGuid(), edicion, origen, destino, cantidad, "reposición", Guid.NewGuid(), Ahora.AddDays(-1));
        Avanzar(t, estado);
        if (guia is not null)
        {
            t.RegistrarGuia(guia);
        }

        return Traslados.Sembrar(t);
    }

    /// <summary>Un traslado vinculado a un pedido ya guardado en el estado pedido.</summary>
    public Traslado Vinculado(Guid edicion, Guid origen, Guid destino, int cantidad, Guid pedido, EstadoDeTraslado estado = EstadoDeTraslado.Solicitado)
    {
        var t = Traslado.VincularAUnPedido(Guid.NewGuid(), edicion, origen, destino, cantidad, pedido, EstimacionDeLlegada.Calcular(DateOnly.FromDateTime(Ahora.UtcDateTime), 2), null, Ahora.AddDays(-1));
        if (estado == EstadoDeTraslado.Cancelado)
        {
            t.AnularPorPedido(Ahora.AddHours(-12));
        }
        else
        {
            Avanzar(t, estado);
        }
        return Traslados.Sembrar(t);
    }

    private static void Avanzar(Traslado t, EstadoDeTraslado estado)
    {
        var ayer = Ahora.AddHours(-12);
        switch (estado)
        {
            case EstadoDeTraslado.Aprobado: t.Aprobar(ayer); break;
            case EstadoDeTraslado.EnTransito: t.Aprobar(ayer); t.Despachar(ayer); break;
            case EstadoDeTraslado.Recibido: t.Aprobar(ayer); t.Despachar(ayer); t.Recibir(ayer); break;
            case EstadoDeTraslado.Rechazado: t.Rechazar("sin stock", ayer); break;
            case EstadoDeTraslado.Cancelado: t.Cancelar("error de pedido", ayer); break;
        }
    }
}
