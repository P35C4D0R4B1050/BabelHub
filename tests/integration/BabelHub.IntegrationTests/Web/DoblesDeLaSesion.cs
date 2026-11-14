using System.Collections.Concurrent;
using System.Text;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Web;

namespace BabelHub.IntegrationTests.Web;

internal sealed class RelojDePrueba(DateTimeOffset inicio) : IReloj
{
    public DateTimeOffset Ahora { get; set; } = inicio;

    public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
}

/// <summary>Cifrado transparente: solo comprueba que el texto se descifre con el mismo propósito y los mismos datos asociados. El cifrado real tiene sus propias pruebas.</summary>
internal sealed class CifradorTransparente : ICifradorDeSecretosDeSesion
{
    public short VersionVigente { get; set; } = 1;

    public TextoCifrado Cifrar(string texto, PropositoDeSecreto proposito, string datosAsociados) =>
        new(Encoding.UTF8.GetBytes($"{proposito}|{datosAsociados}|{texto}"), VersionVigente);

    public string Descifrar(byte[] cifrado, short versionDeClave, PropositoDeSecreto proposito, string datosAsociados)
    {
        var texto = Encoding.UTF8.GetString(cifrado);
        var prefijo = $"{proposito}|{datosAsociados}|";
        return texto.StartsWith(prefijo, StringComparison.Ordinal) ? texto[prefijo.Length..] : throw new SecretoDeSesionIlegibleException();
    }
}

/// <summary>
/// Identidad con rotación de la renovación (Identidad rota la renovación en cada uso): la vigente rota y devuelve una credencial nueva; la anterior solo se tolera
/// si se pide (R10); cualquier otra es un rechazo por reutilización. Segura entre hilos.
/// </summary>
internal sealed class IdentidadConRotacion(RelojDePrueba reloj) : ISesionesDeIdentidad
{
    private readonly object _candado = new();
    private int _numero = 1;
    private string? _anterior;
    private int _llamadas;
    private int _revocaciones;

    public int LlamadasARenovar => Volatile.Read(ref _llamadas);

    public int Revocaciones => Volatile.Read(ref _revocaciones);

    public TimeSpan Demora { get; set; } = TimeSpan.Zero;

    public bool TolerarLaRenovacionAnterior { get; set; }

    /// <summary>Si es verdadero, Identidad rota la renovación pero la respuesta no llega (tiempo agotado): un fallo transitorio con la renovación incierta.</summary>
    public bool PerderLaRespuesta { get; set; }

    public bool ConfirmaRevocaciones { get; set; } = true;

    public ConcurrentBag<string> Revocadas { get; } = [];

    public string RenovacionVigente
    {
        get
        {
            lock (_candado)
            {
                return $"renov-{_numero}";
            }
        }
    }

    public async Task<ResultadoDeRenovacion> Renovar(string renovacion, CancellationToken cancelacion = default)
    {
        Interlocked.Increment(ref _llamadas);
        if (Demora > TimeSpan.Zero)
        {
            await Task.Delay(Demora, cancelacion);
        }

        lock (_candado)
        {
            var vigente = $"renov-{_numero}";
            if (renovacion != vigente && !(TolerarLaRenovacionAnterior && renovacion == _anterior))
            {
                return new ResultadoDeRenovacion.Rechazada();
            }

            _anterior = vigente;
            _numero++;
            if (PerderLaRespuesta)
            {
                return new ResultadoDeRenovacion.Transitoria();
            }

            return new ResultadoDeRenovacion.Renovada($"jwt-ok-{_numero}", reloj.Ahora.AddMinutes(15), $"renov-{_numero}");
        }
    }

    public Task<bool> RevocarRenovacion(string renovacion, CancellationToken cancelacion = default)
    {
        Interlocked.Increment(ref _revocaciones);
        Revocadas.Add(renovacion);
        return Task.FromResult(ConfirmaRevocaciones);
    }

    public Task<bool> CerrarTodasLasSesiones(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(true);
}

/// <summary>Un almacén que cae al guardar: modela la caída del proceso entre la llamada a Identidad y la confirmación (la unidad de trabajo se revierte).</summary>
internal sealed class AlmacenQueFallaAlGuardar(IAlmacenDeSesionesWeb interno) : IAlmacenDeSesionesWeb
{
    public Task Insertar(SesionGuardada sesion, CancellationToken cancelacion = default) => interno.Insertar(sesion, cancelacion);

    public Task<SesionGuardada?> Cargar(HashDeSesion id, CancellationToken cancelacion = default) => interno.Cargar(id, cancelacion);

    public Task<SesionGuardada?> CargarParaRenovar(HashDeSesion id, CancellationToken cancelacion = default) => interno.CargarParaRenovar(id, cancelacion);

    public bool Cae { get; set; } = true;

    public Task Guardar(SesionGuardada sesion, CancellationToken cancelacion = default) =>
        Cae ? throw new InvalidOperationException("Caída del proceso antes de confirmar.") : interno.Guardar(sesion, cancelacion);

    public Task ActualizarActividad(HashDeSesion id, DateTimeOffset ultimaActividad, CancellationToken cancelacion = default) => interno.ActualizarActividad(id, ultimaActividad, cancelacion);

    public Task<IReadOnlyList<SesionGuardada>> CargarAbiertasDeUnUsuario(Guid usuarioId, CancellationToken cancelacion = default) => interno.CargarAbiertasDeUnUsuario(usuarioId, cancelacion);
}

internal sealed class VerificadorPorPrefijo : IVerificadorDeCredencial
{
    public static readonly Guid Usuario = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public VerificacionDeCredencial Verificar(string credencial, DateTimeOffset ahora) =>
        credencial.StartsWith("jwt-ok", StringComparison.Ordinal)
            ? new VerificacionDeCredencial(EstadoDeVerificacion.Valida, new ActorVerificado(Usuario, RolWeb.Cliente, null))
            : new VerificacionDeCredencial(EstadoDeVerificacion.Invalida, null);
}

internal sealed class RegistroDeSeguridadEspiaSeguro : IRegistroDeSeguridadDeLaWeb
{
    public ConcurrentBag<MotivoDeDenegacionWeb> Eventos { get; } = [];

    public Task AccesoDenegado(MotivoDeDenegacionWeb motivo, Guid? usuarioId, CancellationToken cancelacion = default)
    {
        Eventos.Add(motivo);
        return Task.CompletedTask;
    }
}

internal sealed class AlertasEspiaSeguras : IPublicadorDeAlertasTecnicas
{
    public ConcurrentBag<AlertaTecnica> Alertas { get; } = [];

    public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
    {
        Alertas.Add(alerta);
        return Task.CompletedTask;
    }
}

/// <summary>El módulo <c>Web</c> compuesto con PostgreSQL real y dobles solo de lo que aún no existe (Identidad y el verificador de la credencial).</summary>
internal sealed class EntornoDeSesiones
{
    public static readonly DateTimeOffset Inicio = new(2026, 11, 14, 15, 0, 0, TimeSpan.FromHours(-5));

    public EntornoDeSesiones(
        PostgresDeLaWebFixture bd,
        TimeSpan? plazoDeBloqueo = null,
        ICifradorDeSecretosDeSesion? cifrador = null,
        LimitadorDeRenovaciones? limitador = null,
        Func<IAlmacenDeSesionesWeb, IAlmacenDeSesionesWeb>? decorarAlmacen = null)
    {
        Reloj = new RelojDePrueba(Inicio);
        Identidad = new IdentidadConRotacion(Reloj);
        Unidad = new UnidadDeTrabajoPostgres(bd.Acceso);
        Sesiones = new AlmacenDeSesionesWebPostgres(Unidad, Unidad, plazoDeBloqueo ?? TimeSpan.FromSeconds(5));
        Borradores = new AlmacenDeBorradoresPostgres(Unidad, Unidad);
        Cifrador = cifrador ?? new CifradorTransparente();
        Aleatorios = new AleatoriosSeguros();
        Limitador = limitador ?? new LimitadorDeRenovaciones();
        IAlmacenDeSesionesWeb almacen = decorarAlmacen is null ? Sesiones : decorarAlmacen(Sesiones);
        Web = new SesionesWeb(almacen, Cifrador, Identidad, new VerificadorPorPrefijo(), Seguridad, Alertas, Unidad, Aleatorios, Reloj, Limitador);
        BorradoresDePantalla = new BorradoresDePantalla(almacen, Borradores, Cifrador, Seguridad, Unidad, Reloj);
    }

    public RelojDePrueba Reloj { get; }

    public IdentidadConRotacion Identidad { get; }

    public UnidadDeTrabajoPostgres Unidad { get; }

    public AlmacenDeSesionesWebPostgres Sesiones { get; }

    public AlmacenDeBorradoresPostgres Borradores { get; }

    public ICifradorDeSecretosDeSesion Cifrador { get; }

    public AleatoriosSeguros Aleatorios { get; }

    public LimitadorDeRenovaciones Limitador { get; }

    public RegistroDeSeguridadEspiaSeguro Seguridad { get; } = new();

    public AlertasEspiaSeguras Alertas { get; } = new();

    public SesionesWeb Web { get; }

    public BorradoresDePantalla BorradoresDePantalla { get; }

    /// <summary>Abre una sesión por el camino real y devuelve el valor de su cookie.</summary>
    public async Task<string> Abrir(HostWeb host = HostWeb.Tienda, RolWeb rol = RolWeb.Cliente, string? cookieAnterior = null)
    {
        var ingreso = new IngresoCompletado(VerificadorPorPrefijo.Usuario, rol, "jwt-ok-1", Reloj.Ahora.AddMinutes(15), Identidad.RenovacionVigente);
        var resultado = await Web.Iniciar(new SolicitudDeInicioDeSesion(host, ingreso, cookieAnterior, true));
        return resultado.Valor.ValorDeLaCookie;
    }

    public HashDeSesion Hash(string cookie) => ValorDeCookieDeSesion.Hash(cookie);
}
