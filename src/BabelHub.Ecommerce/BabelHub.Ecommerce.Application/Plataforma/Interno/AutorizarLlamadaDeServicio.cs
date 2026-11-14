namespace BabelHub.Ecommerce.Application.Plataforma.Interno;

/// <summary>
/// Matriz operación → alcance de la credencial de servicio. Cada operación declara el único alcance que la autoriza; una
/// operación sin declarar no la autoriza ninguno (rechazo por defecto).
/// </summary>
public sealed class MatrizDeAlcances : IMatrizDeAlcancesDeServicio
{
    private readonly Dictionary<OperacionDeServicio, AlcanceDeServicio> _exigidos;

    public MatrizDeAlcances(IEnumerable<(OperacionDeServicio Operacion, AlcanceDeServicio Alcance)> declaraciones)
    {
        ArgumentNullException.ThrowIfNull(declaraciones);
        _exigidos = [];
        foreach (var (operacion, alcance) in declaraciones)
        {
            if (!_exigidos.TryAdd(operacion, alcance))
            {
                throw new ArgumentException($"La operación «{operacion}» declara su alcance dos veces.", nameof(declaraciones));
            }
        }
    }

    public IReadOnlyCollection<OperacionDeServicio> Operaciones => _exigidos.Keys;

    public AlcanceDeServicio? Exigido(OperacionDeServicio operacion) =>
        _exigidos.TryGetValue(operacion, out var alcance) ? alcance : null;
}

/// <summary>
/// Implementa <see cref="IAutorizadorDeLlamadasDeServicio"/>: sin credencial, con una operación sin declarar, con el alcance
/// de otra operación o, en una operación de usuario, sin el contexto de un usuario, la llamada se rechaza y queda un evento de
/// seguridad con el origen y el motivo.
/// </summary>
public sealed class AutorizarLlamadaDeServicio(
    MatrizDeAlcances matriz,
    IActorActual actores,
    IOrigenDeSolicitud origenes,
    IPublicadorDeEventosDeSeguridad eventos,
    IReloj reloj) : IAutorizadorDeLlamadasDeServicio
{
    public async Task<ResultadoDeAutorizacion> Autorizar(LlamadaDeServicio llamada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(llamada);

        var motivo = Evaluar(llamada);
        if (motivo is null)
        {
            return ResultadoDeAutorizacion.Permitida;
        }

        var origen = origenes.Actual;
        await eventos.Publicar(
            new EventoDeSeguridad(
                TipoDeEventoDeSeguridad.LlamadaEntreServiciosSinCredencial,
                reloj.Ahora,
                origen: origen.Huella,
                direccionCompleta: origen.Direccion,
                motivo: NombreDelMotivo(motivo.Value)),
            cancelacion).ConfigureAwait(false);

        return ResultadoDeAutorizacion.Rechazada(motivo.Value);
    }

    private static string NombreDelMotivo(MotivoDeRechazoDeLlamada motivo) => motivo switch
    {
        MotivoDeRechazoDeLlamada.SinCredencial => "sin-credencial",
        MotivoDeRechazoDeLlamada.OperacionNoDeclarada => "operacion-no-declarada",
        MotivoDeRechazoDeLlamada.OtroAlcance => "otro-alcance",
        _ => "sin-contexto-de-usuario",
    };

    private MotivoDeRechazoDeLlamada? Evaluar(LlamadaDeServicio llamada)
    {
        if (llamada.AlcanceDeLaCredencial is not { } alcance)
        {
            return MotivoDeRechazoDeLlamada.SinCredencial;
        }

        if (matriz.Exigido(llamada.Operacion) is not { } exigido)
        {
            return MotivoDeRechazoDeLlamada.OperacionNoDeclarada;
        }

        if (exigido != alcance)
        {
            return MotivoDeRechazoDeLlamada.OtroAlcance;
        }

        // Una llamada que actúa por un usuario lleva su contexto; el actor «sistema» o «integración» sale del alcance.
        if (exigido == AlcanceDeServicio.OperacionDeUsuario && !llamada.ContextoDeUsuarioDiferido && actores.Actual?.Tipo != TipoDeActor.Usuario)
        {
            return MotivoDeRechazoDeLlamada.SinContextoDeUsuario;
        }

        return null;
    }
}

/// <summary>Implementa <see cref="IResolutorDeOrigenDeServicio"/> (SEG-17, SEG-32; AUD-44).</summary>
public sealed class ResolverOrigenDeLlamadaDeServicio(
    ICalculadorDeHuella huellas,
    IPublicadorDeEventosDeSeguridad eventos,
    IReloj reloj) : IResolutorDeOrigenDeServicio
{
    public async Task<OrigenResuelto> Resolver(
        string? direccionDirecta,
        string? direccionReenviada,
        bool credencialDeOrigenValida,
        CancellationToken cancelacion = default)
    {
        var directo = string.IsNullOrWhiteSpace(direccionDirecta)
            ? OrigenDeSolicitud.Desconocido
            : new OrigenDeSolicitud(huellas.DeOrigen(direccionDirecta), direccionDirecta);

        if (string.IsNullOrWhiteSpace(direccionReenviada))
        {
            return new OrigenResuelto(directo, AplicaLimitesPorOrigen: true, ReenvioIgnorado: false);
        }

        if (credencialDeOrigenValida)
        {
            return new OrigenResuelto(new OrigenDeSolicitud(huellas.DeOrigen(direccionReenviada), direccionReenviada), AplicaLimitesPorOrigen: true, ReenvioIgnorado: false);
        }

        // Sin la credencial se ignora la dirección reenviada: el evento no la conserva (AUD-44) y la llamada cuenta
        // en un cubo propio (la huella de la dirección directa) sin los límites por origen.
        await eventos.Publicar(
            new EventoDeSeguridad(TipoDeEventoDeSeguridad.OrigenReenviadoSinCredencial, reloj.Ahora, origen: directo.Huella),
            cancelacion).ConfigureAwait(false);

        return new OrigenResuelto(directo with { AplicaLimitesPorOrigen = false }, AplicaLimitesPorOrigen: false, ReenvioIgnorado: true);
    }
}
