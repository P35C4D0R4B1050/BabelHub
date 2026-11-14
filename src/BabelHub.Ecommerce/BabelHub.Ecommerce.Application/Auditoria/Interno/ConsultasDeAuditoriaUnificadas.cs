using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;

namespace BabelHub.Ecommerce.Application.Auditoria.Interno;

/// <summary>Dónde va una fuente en la lista unificada: la página que se está consumiendo (su cursor, nulo si es la primera), cuántos de sus registros ya se entregaron y si se agotó.</summary>
public sealed record PosicionDeFuente(string? Pagina, int Consumidos, bool Agotada = false);

/// <summary>Lo que lleva el cursor de la lista unificada: la posición en cada una de las tres fuentes (los subcursores, AUD-09, AUD-30).</summary>
public sealed record CargaDeCursorUnificado(PosicionDeFuente Auditoria, PosicionDeFuente Identidad, PosicionDeFuente Inventario);

/// <summary>
/// Una fuente de la lista unificada vista como una cola de registros: pide páginas por cursor, salta lo ya entregado y recuerda dónde quedó. Si la fuente no
/// responde (<see cref="Faltan"/>) no aporta registros y su posición no cambia.
/// </summary>
internal sealed class FuenteFusionada(Func<string?, CancellationToken, Task<PaginaDeRegistros?>> pedir, PosicionDeFuente inicial)
{
    private sealed record Elemento(RegistroAuditado Registro, string? Pagina, int Indice, int Tamano, string? Siguiente);

    private readonly Queue<Elemento> _cola = new();
    private string? _porCargar = inicial.Pagina;
    private int _saltar = inicial.Consumidos;
    private bool _agotada = inicial.Agotada;
    private Elemento? _ultimo;

    /// <summary>La fuente no respondió (o respondió algo inválido): la página trae lo demás y avisa (AUD-18).</summary>
    public bool Faltan { get; private set; }

    public async Task Llenar(int minimo, CancellationToken cancelacion)
    {
        while (_cola.Count < minimo && !_agotada && !Faltan)
        {
            var pagina = await pedir(_porCargar, cancelacion).ConfigureAwait(false);
            if (pagina is null)
            {
                Faltan = true;
                return;
            }

            var id = pagina.CursorDeLaPagina ?? _porCargar;
            if (id is null && pagina.Registros.Count > 0)
            {
                throw new InvalidOperationException("La fuente no devolvió el cursor de la página: no se puede paginar sin repetir ni omitir.");
            }

            for (var i = _saltar; i < pagina.Registros.Count; i++)
            {
                _cola.Enqueue(new Elemento(pagina.Registros[i], id, i, pagina.Registros.Count, pagina.SiguienteCursor));
            }

            _saltar = 0;
            if (pagina.SiguienteCursor is null)
            {
                _agotada = true;
            }
            else
            {
                _porCargar = pagina.SiguienteCursor;
            }
        }
    }

    public RegistroAuditado? Cabeza => _cola.TryPeek(out var elemento) ? elemento.Registro : null;

    public RegistroAuditado Tomar()
    {
        _ultimo = _cola.Dequeue();
        return _ultimo.Registro;
    }

    /// <summary>La posición con la que se retoma en la página siguiente y si aún queda algo por entregar.</summary>
    public (PosicionDeFuente Posicion, bool Pendiente) Estado()
    {
        if (_ultimo is null)
        {
            // Una fuente que no respondió no aporta nada a esta lista: no deja un cursor pendiente (la página lo avisa, AUD-18).
            return (inicial, !inicial.Agotada && (_cola.Count > 0 || (!_agotada && !Faltan)));
        }

        if (_ultimo.Indice + 1 < _ultimo.Tamano)
        {
            return (new PosicionDeFuente(_ultimo.Pagina, _ultimo.Indice + 1), true);
        }

        return _ultimo.Siguiente is null
            ? (new PosicionDeFuente(null, 0, Agotada: true), false)
            : (new PosicionDeFuente(_ultimo.Siguiente, 0), true);
    }
}

/// <summary>
/// La lista unificada de auditoría del administrador central (AUD-07, AUD-09, AUD-17, AUD-18, AUD-27, AUD-30, D05): fusiona la auditoría propia, la de
/// Identidad (aislada) y la del Inventario con <b>un único orden</b> <c>(instante_utc desc, servicio, sucursal_origen con nulos al final, id)</c>, en páginas de 20, con un
/// cursor firmado ligado al usuario y a los filtros cuya carga son los subcursores de las tres fuentes. El rol se revalida en <b>cada</b> página (nunca sale del
/// cursor). Si el Inventario no responde en 10 segundos, responde con un error o con algo inválido, la página trae lo del E-commerce e indica que faltan sus
/// registros (200, AUD-18); solo falla (<see cref="CategoriaDeError.NoDisponible"/>) si fallan las fuentes propias. Un cursor inválido es siempre la misma
/// validación uniforme.
/// </summary>
public sealed class ConsultasDeAuditoriaUnificadas(
    IActorActual actores,
    CatalogoDeAcciones catalogo,
    FirmadorDeCursor firmador,
    IConsultaLocalDeRegistros local,
    IFuenteDeRegistrosAislados identidad,
    IClienteDeAuditoriaDelInventario inventario) : IConsultasDeAuditoria
{
    public static readonly ErrorDeAplicacion CursorInvalido = new(CategoriaDeError.Validacion, "cursor-invalido", [new ErrorDeCampo("cursor", "cursor-invalido")]);

    public async Task<Resultado<PaginaDeRegistros>> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        var actor = actores.Actual;
        if (!RolesDeConsultaDeAuditoria.EsAdministradorCentral(actor))
        {
            // El evento de acceso denegado lo registra la Api con la ruta de la solicitud (un solo punto, AUD-05); aquí solo se rechaza.
            return Resultado<PaginaDeRegistros>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "fuera-de-rol"));
        }

        var filtro = consulta.Filtro ?? new FiltroDeAuditoria();
        if (ValidacionDeFiltroDeAuditoria.Validar(filtro, catalogo) is { } invalido)
        {
            return Resultado<PaginaDeRegistros>.Fallo(invalido);
        }

        var huella = ValidacionDeFiltroDeAuditoria.Huella(filtro);
        var usuario = actor!.UsuarioId!.Value;
        var carga = new CargaDeCursorUnificado(new(null, 0), new(null, 0), new(null, 0));
        if (consulta.Cursor is not null)
        {
            carga = firmador.Abrir<CargaDeCursorUnificado>(consulta.Cursor, usuario, huella)!;
            if (carga is null || carga.Auditoria is null || carga.Identidad is null || carga.Inventario is null)
            {
                return Resultado<PaginaDeRegistros>.Fallo(CursorInvalido);
            }
        }

        var propia = new FuenteFusionada((cursor, ct) => PedirPropia(local.Consultar, filtro, cursor, ct), carga.Auditoria);
        var aislada = new FuenteFusionada((cursor, ct) => PedirPropia(identidad.Consultar, filtro, cursor, ct), carga.Identidad);
        var remota = new FuenteFusionada((cursor, ct) => PedirAlInventario(filtro, cursor, ct), carga.Inventario);

        try
        {
            await Task.WhenAll(
                propia.Llenar(PaginaDeRegistros.TamanoDePagina, cancelacion),
                aislada.Llenar(PaginaDeRegistros.TamanoDePagina, cancelacion),
                remota.Llenar(PaginaDeRegistros.TamanoDePagina, cancelacion)).ConfigureAwait(false);
        }
        catch (ErrorDelInventarioException)
        {
            return Resultado<PaginaDeRegistros>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "inventario-rechazo"));
        }
        catch (Exception falla) when (falla is not OperationCanceledException)
        {
            // Una fuente propia que falla o se contradice: el 503 es solo del propio E-commerce; el detalle va al registro estructurado, no a la respuesta.
            return Resultado<PaginaDeRegistros>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "auditoria-no-disponible"));
        }

        var fuentes = new[] { propia, aislada, remota };
        var elegidos = new List<RegistroAuditado>(PaginaDeRegistros.TamanoDePagina);
        while (elegidos.Count < PaginaDeRegistros.TamanoDePagina)
        {
            var siguiente = fuentes.Where(f => f.Cabeza is not null).MinBy(f => f.Cabeza!, OrdenDeLaLista.Instancia);
            if (siguiente is null)
            {
                break;
            }

            elegidos.Add(siguiente.Tomar());
        }

        var estados = fuentes.Select(f => f.Estado()).ToArray();
        string? siguienteCursor = null;
        if (estados.Any(e => e.Pendiente))
        {
            siguienteCursor = firmador.Firmar(usuario, huella, new CargaDeCursorUnificado(estados[0].Posicion, estados[1].Posicion, estados[2].Posicion));
        }

        return Resultado<PaginaDeRegistros>.Exito(new PaginaDeRegistros(elegidos, siguienteCursor, faltanDelInventario: remota.Faltan));
    }

    private static async Task<PaginaDeRegistros?> PedirPropia(
        Func<ConsultaDeAuditoria, CancellationToken, Task<PaginaDeRegistros>> fuente,
        FiltroDeAuditoria filtro,
        string? cursor,
        CancellationToken cancelacion) =>
        await fuente(new ConsultaDeAuditoria(filtro, cursor), cancelacion).ConfigureAwait(false);

    private async Task<PaginaDeRegistros?> PedirAlInventario(FiltroDeAuditoria filtro, string? cursor, CancellationToken cancelacion)
    {
        var respuesta = await inventario.Consultar(new ConsultaDeAuditoria(filtro, cursor), cancelacion).ConfigureAwait(false);
        return respuesta.Estado switch
        {
            EstadoDeRespuestaDelInventario.Disponible => respuesta.Valor is { CursorDeLaPagina: null, Registros.Count: > 0 } && cursor is null ? null : respuesta.Valor,
            EstadoDeRespuestaDelInventario.NoDisponible => null,
            _ => throw new ErrorDelInventarioException(),
        };
    }

    /// <summary>El Inventario rechazó la consulta (401, 403, 404, 409 o 400): no es una falla del servicio y no se disfraza de «faltan registros».</summary>
    private sealed class ErrorDelInventarioException : Exception;
}
