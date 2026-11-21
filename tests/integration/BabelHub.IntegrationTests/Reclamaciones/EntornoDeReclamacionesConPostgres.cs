using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Reclamaciones;
using RolesDeUsuario = BabelHub.Shared.Contratos.RolesDeUsuario;

namespace BabelHub.IntegrationTests.Reclamaciones;

/// <summary>El actor de la solicitud que la prueba cambia a voluntad (administración central, sistema, cliente, visitante).</summary>
internal sealed class ActorMutable : IActorActual
{
    public ActorActual? Actual { get; set; } = ActorActual.DeUsuario(Guid.Parse("c0000000-0000-0000-0000-0000000000ad"), RolesDeUsuario.AdministradorCentral, null);

    public void ComoAdministracion() => Actual = ActorActual.DeUsuario(Guid.Parse("c0000000-0000-0000-0000-0000000000ad"), RolesDeUsuario.AdministradorCentral, null);

    public void ComoSistema() => Actual = ActorActual.DelSistema();
}

internal sealed class DenegacionesDeHojasEspia : IDenegacionDeAcceso
{
    public List<DenegacionDeAcceso> Eventos { get; } = [];

    public Task Registrar(DenegacionDeAcceso denegacion, CancellationToken cancelacion = default)
    {
        lock (Eventos)
        {
            Eventos.Add(denegacion);
        }

        return Task.CompletedTask;
    }
}

internal sealed class AuditoriaDeHojasEspia : IAuditoria
{
    public List<SolicitudDeRegistro> Solicitudes { get; } = [];

    public bool Caida { get; set; }

    public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
    {
        if (Caida)
        {
            return Task.FromException(new AuditoriaNoDisponibleException("sin auditoría"));
        }

        lock (Solicitudes)
        {
            Solicitudes.Add(solicitud);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Un motor de límites en memoria seguro entre hilos: el cupo de hojas por origen y hora (3), las claves de idempotencia del formulario y el tope de consultas del consumidor.</summary>
internal sealed class LimitesDeHojasEnMemoria : IControlDeLimites
{
    private readonly Lock _candado = new();
    private readonly Dictionary<string, (string Cuerpo, ResultadoMinimo? Resultado)> _claves = [];
    private int _usadas;

    public int Maximo { get; set; } = 3;

    /// <summary>Simula un almacén que perdió su estado: ignora las claves y el índice único de la base es la autoridad final (REC-18).</summary>
    public bool IgnoraLasClaves { get; set; }

    public Task<ResultadoDeLimite> Reservar(ClaveDePolitica politica, string? sujeto = null, string? claveDelLlamador = null, HuellaDeSujeto? huellaDelLlamador = null, ClaveDeIdempotencia? idempotencia = null, CancellationToken cancelacion = default)
    {
        lock (_candado)
        {
            if (politica == PoliticasDeLimite.ConsultasDeHojasPorUsuario)
            {
                return Task.FromResult(ResultadoDeLimite.Permitido(new ReservaDeLimite(politica.Valor, "usuario", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow)));
            }

            if (idempotencia is not null && !IgnoraLasClaves && _claves.TryGetValue(idempotencia.ClaveDelCliente, out var previa))
            {
                if (previa.Cuerpo != idempotencia.HuellaDelCuerpo)
                {
                    return Task.FromResult(ResultadoDeLimite.ClaveEnConflicto());
                }

                return Task.FromResult(previa.Resultado is { } r ? ResultadoDeLimite.RepeticionCompletada(r) : ResultadoDeLimite.RepeticionEnCurso());
            }

            if (_usadas >= Maximo)
            {
                return Task.FromResult(ResultadoDeLimite.Excedido(politica.Valor, 1_800, DateTimeOffset.UtcNow.AddSeconds(1_800)));
            }

            _usadas++;
            if (idempotencia is not null)
            {
                _claves[idempotencia.ClaveDelCliente] = (idempotencia.HuellaDelCuerpo, null);
            }

            return Task.FromResult(ResultadoDeLimite.Permitido(new ReservaDeLimite(politica.Valor, "origen", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, null, idempotencia?.ClaveDelCliente)));
        }
    }

    public Task Liberar(ReservaDeLimite reserva, CancellationToken cancelacion = default)
    {
        lock (_candado)
        {
            if (_usadas > 0)
            {
                _usadas--;
            }
        }

        return Task.CompletedTask;
    }

    public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion = default)
    {
        lock (_candado)
        {
            if (reserva.ClaveDeIdempotencia is { } clave && _claves.TryGetValue(clave, out var actual))
            {
                _claves[clave] = (actual.Cuerpo, resultado);
            }
        }

        return Task.CompletedTask;
    }

    public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion = default)
    {
        lock (_candado)
        {
            if (reserva.ClaveDeIdempotencia is { } clave)
            {
                _claves.Remove(clave);
            }
        }

        return Task.CompletedTask;
    }
}

internal sealed class VerificacionNoExigida : IExigenciaDeVerificacionDePersona
{
    public Task<ResultadoDeExigencia> Exigir(AccionProtegida accion, string? tokenDeVerificacion, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeExigencia.NoExigida);
}

/// <summary>El correo de las respuestas: guarda lo enviado y puede caerse.</summary>
internal sealed class CorreoDeHojasEspia : IEnviadorDeCorreo
{
    public List<CorreoSaliente> Correos { get; } = [];

    public bool Falla { get; set; }

    public Task<ResultadoDeEnvio> Enviar(CorreoSaliente correo, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            throw new InvalidOperationException("SMTP caído.");
        }

        lock (Correos)
        {
            Correos.Add(correo);
        }

        return Task.FromResult(ResultadoDeEnvio.Enviado);
    }

    public Task<ResultadoDeEnvio> EnviarCodigo(CorreoDeCodigo correo, CancellationToken cancelacion = default) => throw new NotSupportedException();
}

internal sealed class HuellasNormalizantes : ICalculadorDeHuella
{
    private static string Texto(string valor) =>
        Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(valor.Trim().Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant()))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public HuellaDeOrigen DeOrigen(string direccion) => HuellaDeOrigen.Crear(Texto(direccion), "v1");

    public HuellaDeOrigen? DeOrigenPorRed48(string direccion) => null;

    public IReadOnlyList<HuellaDeOrigen> DeOrigenEnRotacion(string direccion) => [DeOrigen(direccion)];

    public HuellaDeSujeto DeSujeto(string valor, string proposito) => HuellaDeSujeto.Crear(Texto(proposito + "|" + valor), "v1");

    public IReadOnlyList<HuellaDeSujeto> DeSujetoEnRotacion(string valor, string proposito) => [DeSujeto(valor, proposito)];
}

/// <summary>Los casos de uso reales de las hojas sobre PostgreSQL real (el repositorio y la función de anonimización), con la hora real y dobles para lo externo (límites, verificación, correo, auditoría, actor).</summary>
internal sealed class EntornoDeReclamacionesConPostgres
{
    private sealed class RelojReal : IReloj
    {
        public DateTimeOffset Ahora => DateTimeOffset.UtcNow;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));
    }

    private int _claves;

    public EntornoDeReclamacionesConPostgres(PostgresDeReclamacionesFixture bd)
    {
        Actor = new ActorMutable();
        Limites = new LimitesDeHojasEnMemoria();
        Correo = new CorreoDeHojasEspia();
        Auditoria = new AuditoriaDeHojasEspia();
        Denegaciones = new DenegacionesDeHojasEspia();
        var reloj = new RelojReal();
        Hojas = bd.Hojas;
        Anonimizador = bd.Anonimizador;
        Vinculos = bd.Vinculos;
        Unidad = bd.Unidad;
        Vinculador = new VinculadorDeHojas(bd.Vinculos, bd.Unidad, Auditoria);
        var autorizador = new AutorizadorDeReclamaciones(Actor, Denegaciones);
        var envio = new EnvioDelAvisoDeRespuesta(Correo, bd.Hojas, bd.Unidad, reloj);
        Reclamaciones = new ReclamacionesDeLaAplicacion(new RegistrarHoja(new VerificacionNoExigida(), new HuellasNormalizantes(), Limites, bd.Hojas, bd.Unidad, reloj, Actor, Vinculador));
        Administracion = new AdministracionDeReclamacionesDeLaAplicacion(
            new ListarHojas(autorizador, bd.Hojas, bd.Unidad),
            new ObtenerHojaParaAdministracion(autorizador, bd.Hojas, bd.Unidad, Auditoria),
            new ResponderHoja(autorizador, bd.Hojas, bd.Unidad, reloj, Auditoria, envio),
            new DescartarHoja(autorizador, bd.Hojas, bd.Unidad, reloj, Auditoria),
            new ReintentarAvisoDeRespuesta(autorizador, bd.Hojas, bd.Unidad, reloj, envio));
        Anonimizacion = new AnonimizarHojasVencidas(autorizador, bd.Anonimizador, bd.Unidad, Auditoria);
    }

    public ActorMutable Actor { get; }

    public LimitesDeHojasEnMemoria Limites { get; }

    public CorreoDeHojasEspia Correo { get; }

    public AuditoriaDeHojasEspia Auditoria { get; }

    public DenegacionesDeHojasEspia Denegaciones { get; }

    internal RepositorioDeHojasPostgres Hojas { get; }

    internal AnonimizadorDeHojasPostgres Anonimizador { get; }

    internal VinculosDeHojasPostgres Vinculos { get; }

    internal VinculadorDeHojas Vinculador { get; }

    public UnidadDeTrabajoPostgres Unidad { get; }

    public IReclamaciones Reclamaciones { get; }

    public IAdministracionDeReclamaciones Administracion { get; }

    public AnonimizarHojasVencidas Anonimizacion { get; }

    /// <summary>Una hoja válida de «Ana Pérez», con una clave nueva por llamada salvo que se indique.</summary>
    public RegistrarHojaEntrada Entrada(string? clave = null, string detalle = "Llegó con las hojas sueltas.") =>
        new(
            "Ana Pérez Quispe",
            "12345678",
            "Av. Los Olivos 123, Ayacucho",
            "Ana.Perez@Correo.COM",
            "Libro «La ciudad y los perros»",
            49.90m,
            "reclamo",
            detalle,
            "PED-2026-0007",
            clave ?? $"clave-del-formulario-{Interlocked.Increment(ref _claves):D6}",
            null);
}
