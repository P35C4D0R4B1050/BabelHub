using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>El estado de aceptación de los textos legales que informa Identidad (<c>IEstadoDeCuenta</c>, R12): cerrado a dos valores y uno para lo que no se entiende.</summary>
public enum EstadoDeAceptacionDeTextos
{
    Aceptados,
    Pendientes,

    /// <summary>Respuesta mal formada o un estado que la web no conoce: se trata como «no se pudo saber» (WEB-45).</summary>
    Desconocido,
}

/// <summary>Puerto: Identidad informa si la persona aceptó la versión vigente de los textos. <b>Sin caché</b>: se pregunta en cada solicitud (WEB-44). Para cualquier persona con sesión, también el personal.</summary>
public interface IEstadoDeCuentaDeLaWeb
{
    Task<EstadoDeAceptacionDeTextos> Aceptacion(Guid usuarioId, CancellationToken cancelacion = default);
}

/// <summary>Puerto: Identidad registra la aceptación de una versión y la compara con la vigente (R12).</summary>
public interface IAceptacionDeTextosEnIdentidad
{
    Task<ResultadoDeAceptacion> Aceptar(Guid usuarioId, string versionMostrada, CancellationToken cancelacion = default);
}

/// <summary>La clave con la que se registra un bloqueo por textos pendientes: normalizada, sin datos de la persona (WEB-47).</summary>
public sealed record ClaveDeBloqueoPorTextos(string PlantillaDeRuta, string Metodo, MotivoDeBloqueoPorTextos Motivo, string Cuenta)
{
    public const string RutaNoDeclarada = "ruta no declarada";
    public const string MetodoNoEstandar = "método no estándar";

    /// <summary>El método de una lista cerrada o el valor fijo «método no estándar»; la ruta, la plantilla del endpoint o el valor fijo «ruta no declarada».</summary>
    public static ClaveDeBloqueoPorTextos Normalizar(string? plantilla, string metodo, MotivoDeBloqueoPorTextos motivo, Guid usuarioId)
    {
        var metodoNormalizado = metodo is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS" or "TRACE" ? metodo : MetodoNoEstandar;
        var ruta = string.IsNullOrWhiteSpace(plantilla) || plantilla.Length > 100 ? RutaNoDeclarada : plantilla;
        return new ClaveDeBloqueoPorTextos(ruta, metodoNormalizado, motivo, usuarioId.ToString("N"));
    }

    /// <summary>No imprime la cuenta.</summary>
    public override string ToString() => $"{Metodo} {PlantillaDeRuta} ({Motivo})";
}

/// <summary>
/// Decide el acceso de una persona con los textos legales pendientes (WEB-41 a WEB-49, T36). <b>Orden</b>: sesión, rol y alcance —sin consultar a Identidad—, y solo después el estado de aceptación,
/// <b>en cada solicitud, sin caché ni estado anterior</b> y con un tope de consultas simultáneas por proceso que falla cerrado de inmediato. Falla cerrada ante un error, el tiempo de espera de
/// SEG-14, una respuesta mal formada, un estado desconocido o la saturación: ni se cierra la sesión ni se toca el carrito. El retorno tras aceptar es de un solo uso y se valida.
/// </summary>
public sealed class ResolverAccesoConTextosPendientes(
    IAreasDeInterfaz areas,
    IEstadoDeCuentaDeLaWeb estado,
    IAlmacenDeSesionesWeb sesiones,
    IRegistroDeSeguridadDeLaWeb seguridad,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj,
    LimitadorDeConsultasDeEstado limitador,
    TimeSpan? espera = null) : IPoliticaDeAccesoPorTextos
{
    /// <summary>El plazo de SEG-14 para una dependencia normal.</summary>
    public static readonly TimeSpan EsperaPorOmision = TimeSpan.FromSeconds(10);

    private readonly TimeSpan _espera = espera ?? EsperaPorOmision;

    public const string MotivoDeAlertaPorIdentidadNoDisponible = "estado-de-aceptacion-no-disponible";

    private static readonly string[] RutasConToken = ["/ingreso", "/recuperacion", "/verificacion", "/checkout/retorno", "/textos/aceptar", "/sesion"];

    public async Task<DecisionDeAcceso> Resolver(SolicitudDeAcceso solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        // 1. Sesión: sin ella no hay nada que consultar; una visita ve solo las áreas que su rol ve (la Tienda).
        var rol = solicitud.Actor?.Rol ?? RolWeb.Visitante;
        if (solicitud.Area is { } area && !areas.VeLaArea(rol, area))
        {
            return new DecisionDeAcceso(solicitud.Actor is null ? TipoDeDecisionDeAcceso.SinSesion : TipoDeDecisionDeAcceso.SinPermiso);
        }

        if (solicitud.Actor is not { } actor)
        {
            return DecisionDeAcceso.Permitida();
        }

        // 2. Alcance: la sucursal que la ruta declara frente a la del actor (D15); el administrador central ve todas. Sin consultar a Identidad (WEB-46, R15).
        if (solicitud.SucursalDeLaRuta is { } sucursal && actor.Rol != RolWeb.AdministradorCentral && actor.SucursalId != sucursal)
        {
            return new DecisionDeAcceso(TipoDeDecisionDeAcceso.SinPermiso, AlcanceFueraDeLaSucursal: true);
        }

        if (solicitud.SinConsultaDeEstado)
        {
            return DecisionDeAcceso.Permitida();
        }

        // 3. Estado de aceptación: en cada solicitud, con tope de consultas que falla cerrado al saturarse.
        var aceptacion = await Consultar(actor, cancelacion).ConfigureAwait(false);
        switch (aceptacion)
        {
            case EstadoDeAceptacionDeTextos.Aceptados:
                return DecisionDeAcceso.Permitida();
            case EstadoDeAceptacionDeTextos.Pendientes:
                return await ConTextosPendientes(solicitud, actor, cancelacion).ConfigureAwait(false);
            default:
                return await ConIdentidadNoDisponible(solicitud, actor, cancelacion).ConfigureAwait(false);
        }
    }

    private async Task<EstadoDeAceptacionDeTextos> Consultar(ActorVerificado actor, CancellationToken cancelacion)
    {
        using var turno = limitador.Entrar();
        if (turno is null)
        {
            return EstadoDeAceptacionDeTextos.Desconocido;
        }

        using var plazo = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        Task<EstadoDeAceptacionDeTextos>? tarea = null;
        try
        {
            tarea = estado.Aceptacion(actor.UsuarioId, plazo.Token);
            var respuesta = await tarea.WaitAsync(_espera, cancelacion).ConfigureAwait(false);
            return respuesta is EstadoDeAceptacionDeTextos.Aceptados or EstadoDeAceptacionDeTextos.Pendientes ? respuesta : EstadoDeAceptacionDeTextos.Desconocido;
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // El tiempo de espera, un error o cualquier otra falla: no se supo (se falla cerrado). La llamada abandonada se cancela y su error no queda sin observar.
            await plazo.CancelAsync().ConfigureAwait(false);
            _ = tarea?.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            return EstadoDeAceptacionDeTextos.Desconocido;
        }
    }

    private async Task<DecisionDeAcceso> ConTextosPendientes(SolicitudDeAcceso solicitud, ActorVerificado actor, CancellationToken cancelacion)
    {
        var capacidades = PoliticaDeAccesoConTextosPendientes.Capacidades(AreasDeInterfaz.Aplicar(actor.Rol), identidadCaida: false);
        if (Permite(solicitud, capacidades))
        {
            return DecisionDeAcceso.Permitida(textosPendientes: true);
        }

        return await Bloquear(solicitud, actor, MotivoDelBloqueo(solicitud), cancelacion).ConfigureAwait(false);
    }

    private static MotivoDeBloqueoPorTextos MotivoDelBloqueo(SolicitudDeAcceso solicitud)
    {
        if (solicitud.Capacidad is not { } declarada)
        {
            return MotivoDeBloqueoPorTextos.RutaNoDeclarada;
        }

        return AreasDeInterfaz.Aplicar(declarada).AdmiteElMetodo(solicitud.Metodo)
            ? MotivoDeBloqueoPorTextos.AccionNoPermitida
            : EsMetodoEstandar(solicitud.Metodo) ? MotivoDeBloqueoPorTextos.MetodoNoDeclarado : MotivoDeBloqueoPorTextos.MetodoNoEstandar;
    }

    private static bool EsMetodoEstandar(string metodo) => metodo is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS" or "TRACE";

    private async Task<DecisionDeAcceso> ConIdentidadNoDisponible(SolicitudDeAcceso solicitud, ActorVerificado actor, CancellationToken cancelacion)
    {
        await PublicarAlerta(cancelacion).ConfigureAwait(false);

        // Con Identidad caída solo quedan cerrar la sesión, los textos legales, el Libro y enviar una hoja (WEB-45); lo demás ve «servicio no disponible» y la acción se rechaza.
        var capacidades = PoliticaDeAccesoConTextosPendientes.Capacidades(AreasDeInterfaz.Aplicar(actor.Rol), identidadCaida: true);
        return Permite(solicitud, capacidades)
            ? DecisionDeAcceso.Permitida()
            : new DecisionDeAcceso(TipoDeDecisionDeAcceso.NoDisponible, Motivo: MotivoDeBloqueoPorTextos.IdentidadNoDisponible);
    }

    private static bool Permite(SolicitudDeAcceso solicitud, IReadOnlySet<CapacidadDeAcceso> capacidades) =>
        solicitud.Capacidad is { } declarada
        && AreasDeInterfaz.Aplicar(declarada) is var capacidad
        && capacidades.Contains(capacidad)
        && capacidad.AdmiteElMetodo(solicitud.Metodo);

    /// <summary>
    /// Una pantalla (<c>GET</c>) que el rol permite pero los textos bloquean lleva a aceptarlos y guarda su ruta para volver; todo lo demás —una acción, otro método, HEAD, OPTIONS, TRACE o un
    /// método no estándar— se rechaza (WEB-46) y deja el evento propio. El rechazo sigue vigente aunque el registro falle.
    /// </summary>
    private async Task<DecisionDeAcceso> Bloquear(SolicitudDeAcceso solicitud, ActorVerificado actor, MotivoDeBloqueoPorTextos motivo, CancellationToken cancelacion)
    {
        if (string.Equals(solicitud.Metodo, "GET", StringComparison.Ordinal))
        {
            await GuardarRetorno(solicitud, actor, cancelacion).ConfigureAwait(false);
            return new DecisionDeAcceso(TipoDeDecisionDeAcceso.Redirige, TextosPendientes: true);
        }

        var clave = ClaveDeBloqueoPorTextos.Normalizar(solicitud.PlantillaDeRuta, solicitud.Metodo, motivo, actor.UsuarioId);
        try
        {
            await seguridad.TextosPendientesBloqueado(clave, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // El rechazo sigue vigente aunque el registro falle: no se convierte en un error ni en paso libre.
            await PublicarAlerta(cancelacion).ConfigureAwait(false);
        }

        return new DecisionDeAcceso(TipoDeDecisionDeAcceso.Bloqueada, TextosPendientes: true, Motivo: motivo);
    }

    private async Task PublicarAlerta(CancellationToken cancelacion)
    {
        if (!limitador.PuedeAlertar(reloj.Ahora))
        {
            return;
        }

        try
        {
            await alertas.Publicar(new AlertaTecnica(MotivoDeAlertaPorIdentidadNoDisponible, reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // La alerta técnica no cambia la decisión: la web ya falla cerrada.
        }
    }

    // ---- Retorno tras aceptar (WEB-49) ----

    private async Task GuardarRetorno(SolicitudDeAcceso solicitud, ActorVerificado actor, CancellationToken cancelacion)
    {
        if (solicitud.SinRetorno || !solicitud.EsPantallaCompleta || solicitud.ValorDeLaCookie is not { } cookie
            || solicitud.RutaParaElRetorno is not { } ruta || !RetornoValido(ruta, actor.Rol))
        {
            return;
        }

        try
        {
            if (!ValorDeCookieDeSesion.TieneFormaValida(cookie))
            {
                return;
            }

            await sesiones.GuardarRetorno(ValorDeCookieDeSesion.Hash(cookie), ruta, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Sin retorno la persona vuelve a la portada: no es motivo para fallar.
        }
    }

    public async Task<string?> TomarRetorno(string valorDeLaCookie, RolWeb rol, CancellationToken cancelacion = default)
    {
        if (!ValorDeCookieDeSesion.TieneFormaValida(valorDeLaCookie))
        {
            return null;
        }

        var guardado = await sesiones.TomarRetorno(ValorDeCookieDeSesion.Hash(valorDeLaCookie), cancelacion).ConfigureAwait(false);
        return guardado is not null && RetornoValido(guardado, rol) ? guardado : null;
    }

    /// <summary>
    /// Una ruta local y simple: empieza por una sola «/», sin consulta ni fragmento ni «\», «%», «:», «;» ni caracteres de control, de hasta 200 caracteres, que no sea una de las que llevan un
    /// token (ingreso, recuperación, verificación, retorno de la pasarela) ni la propia aceptación, y que el rol pueda abrir.
    /// </summary>
    internal static bool RetornoValido(string ruta, RolWeb rol)
    {
        if (string.IsNullOrEmpty(ruta) || ruta.Length > 200 || ruta[0] != '/' || ruta.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (ruta.Any(c => c is '\\' or '%' or ':' or ';' or '?' or '#' or '@' || char.IsControl(c) || char.IsWhiteSpace(c) || c > 126))
        {
            return false;
        }

        if (ruta.Contains("..", StringComparison.Ordinal) || RutasConToken.Any(prefijo => ruta.Equals(prefijo, StringComparison.OrdinalIgnoreCase) || ruta.StartsWith(prefijo + "/", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Una pantalla que el rol permite: la del personal solo si su rol ve esa área; el cliente, nunca la del personal.
        var esDelPersonal = ruta.StartsWith("/sucursal", StringComparison.OrdinalIgnoreCase) || ruta.StartsWith("/admin", StringComparison.OrdinalIgnoreCase);
        return !esDelPersonal || rol is RolWeb.Encargado or RolWeb.Logistica or RolWeb.AdministradorCentral;
    }

}

/// <summary>Un tope de consultas simultáneas a Identidad por proceso: sin turno libre no se espera, se falla cerrado (WEB-45).</summary>
public sealed class LimitadorDeConsultasDeEstado(int maximo = LimitadorDeConsultasDeEstado.MaximoPorProceso) : IDisposable
{
    public const int MaximoPorProceso = 50;

    private long _ultimaAlerta;

    /// <summary>Una alerta técnica como máximo por minuto y por proceso: con Identidad caída cada solicitud fallaría igual y no debe amplificarse en Auditoría.</summary>
    public bool PuedeAlertar(DateTimeOffset ahora)
    {
        var marca = ahora.UtcTicks;
        var anterior = Interlocked.Read(ref _ultimaAlerta);
        return (anterior == 0 || marca - anterior >= TimeSpan.TicksPerMinute) && Interlocked.CompareExchange(ref _ultimaAlerta, marca, anterior) == anterior;
    }

    private readonly SemaphoreSlim _turnos = new(maximo, maximo);

    /// <summary>Un turno, o <see langword="null"/> si el tope está lleno. Quien lo recibe lo libera con <see cref="IDisposable.Dispose"/>.</summary>
    public IDisposable? Entrar() => _turnos.Wait(0) ? new Turno(_turnos) : null;

    public void Dispose() => _turnos.Dispose();

    private sealed class Turno(SemaphoreSlim turnos) : IDisposable
    {
        private int _liberado;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _liberado, 1) == 0)
            {
                turnos.Release();
            }
        }
    }
}
