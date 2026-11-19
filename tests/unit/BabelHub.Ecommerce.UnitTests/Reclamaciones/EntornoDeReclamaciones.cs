using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using BabelHub.Ecommerce.Domain.Reclamaciones;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Identidad;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;
using ActorDePrueba = BabelHub.Ecommerce.UnitTests.Soporte.ActorDePrueba;

namespace BabelHub.Ecommerce.UnitTests.Reclamaciones;

/// <summary>El repositorio de hojas en memoria: guarda copias (como una base de datos), numera por año, respeta el índice único de la clave y se restaura al revertir la unidad de trabajo.</summary>
internal sealed class RepositorioDeHojasEnMemoria : IRepositorioDeHojas, IInstantanea
{
    private Dictionary<Guid, HojaDeReclamacion> _hojas = [];
    private Dictionary<int, int> _secuencias = [];

    public IReadOnlyCollection<HojaDeReclamacion> Todas => _hojas.Values;

    /// <summary>La secuencia no responde a tiempo (un <c>lock_timeout</c>).</summary>
    public bool SecuenciaNoDisponible { get; set; }

    /// <summary>La inserción falla después de tomar el número (un error de la base).</summary>
    public bool FallaAlAgregar { get; set; }

    /// <summary>La lectura por clave no responde a tiempo.</summary>
    public bool BuscarNoDisponible { get; set; }

    /// <summary>El bloqueo de la hoja no responde (un <c>lock_timeout</c> de <c>FOR UPDATE</c>).</summary>
    public bool BloqueoNoDisponible { get; set; }

    /// <summary>Cuántas operaciones se pidieron al repositorio (para comprobar que un rechazo no consulta nada).</summary>
    public int Operaciones { get; private set; }

    /// <summary>Cuántas hojas se leyeron con <c>FOR UPDATE</c>.</summary>
    public int LecturasConBloqueo { get; private set; }

    public static HojaDeReclamacion Clonar(HojaDeReclamacion h) =>
        HojaDeReclamacion.Rehidratar(
            h.Id,
            h.Numero,
            h.Tipo,
            h.Datos,
            h.BienContratado,
            h.MontoReclamado,
            h.Detalle,
            h.PedidoReferencia,
            h.ContenidoHash,
            h.FechaRegistro,
            h.ClaveHash,
            h.Estado,
            h.Respuesta,
            h.RespondidaEn,
            h.RespondidaPor,
            h.AvisoDeRespuesta,
            h.AvisoEnviadoEn,
            h.AvisoEnCursoHasta,
            h.DescartadaEn,
            h.DeCuentaCancelada,
            h.AnonimizadaEn);

    public void Sembrar(HojaDeReclamacion hoja) => _hojas[hoja.Id] = Clonar(hoja);

    public Task<int> SiguienteSecuencia(int anio, CancellationToken cancelacion = default)
    {
        Operaciones++;
        if (SecuenciaNoDisponible)
        {
            throw new HojasNoDisponiblesException("lock_timeout");
        }

        _secuencias[anio] = _secuencias.GetValueOrDefault(anio) + 1;
        return Task.FromResult(_secuencias[anio]);
    }

    public Task<ResultadoDeAgregado> Agregar(HojaDeReclamacion hoja, CancellationToken cancelacion = default)
    {
        Operaciones++;
        if (FallaAlAgregar)
        {
            throw new InvalidOperationException("La base falló.");
        }

        if (_hojas.Values.Any(h => h.ClaveHash == hoja.ClaveHash))
        {
            return Task.FromResult(ResultadoDeAgregado.ClaveRepetida);
        }

        _hojas[hoja.Id] = Clonar(hoja);
        return Task.FromResult(ResultadoDeAgregado.Agregada);
    }

    public Task<HojaDeReclamacion?> BuscarPorClave(IReadOnlyList<string> clavesHash, CancellationToken cancelacion = default)
    {
        Operaciones++;
        if (BuscarNoDisponible)
        {
            throw new HojasNoDisponiblesException("lock_timeout");
        }

        return Task.FromResult(_hojas.Values.FirstOrDefault(h => clavesHash.Contains(h.ClaveHash)) is { } h ? Clonar(h) : null);
    }

    public Task<HojaDeReclamacion?> Obtener(Guid id, bool paraActualizar, CancellationToken cancelacion = default)
    {
        Operaciones++;
        if (paraActualizar)
        {
            if (BloqueoNoDisponible)
            {
                throw new HojasNoDisponiblesException("lock_timeout");
            }

            LecturasConBloqueo++;
        }

        return Task.FromResult(_hojas.TryGetValue(id, out var h) ? Clonar(h) : null);
    }

    public Task Guardar(HojaDeReclamacion hoja, CancellationToken cancelacion = default)
    {
        Operaciones++;
        _hojas[hoja.Id] = Clonar(hoja);
        return Task.CompletedTask;
    }

    public Task<ListadoDeHojas> Listar(EstadoDeHoja? estado, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        Operaciones++;
        var filtradas = _hojas.Values
            .Where(h => estado is null ? h.Estado != EstadoDeHoja.Descartado : h.Estado == estado)
            .OrderByDescending(h => h.FechaRegistro)
            .ThenByDescending(h => h.Numero.Secuencia)
            .ToList();
        return Task.FromResult(new ListadoDeHojas(filtradas.Skip((pagina - 1) * tamano).Take(tamano).Select(Clonar).ToList(), filtradas.Count));
    }

    public object Capturar() => (_hojas.ToDictionary(p => p.Key, p => Clonar(p.Value)), new Dictionary<int, int>(_secuencias));

    public void Restaurar(object instantanea)
    {
        var (hojas, secuencias) = ((Dictionary<Guid, HojaDeReclamacion>, Dictionary<int, int>))instantanea;
        _hojas = hojas;
        _secuencias = secuencias;
    }
}

/// <summary>
/// Los límites de las hojas: 3 por origen (sin ventana de tiempo), con la clave de idempotencia del formulario: una clave completada devuelve su resultado, una en curso es un conflicto y la misma
/// clave con otro cuerpo, un conflicto. <see cref="Fallar"/> libera la clave.
/// </summary>
internal sealed class LimitesDeHojasDoble : IControlDeLimites
{
    private readonly Dictionary<string, (string Cuerpo, ResultadoMinimo? Resultado)> _claves = [];

    public int Maximo { get; set; } = 3;

    public int Usadas { get; set; }

    public bool Caido { get; set; }

    /// <summary>Simula un almacén que perdió su estado: ignora las claves (el índice único de la base es entonces la autoridad).</summary>
    public bool IgnoraLasClaves { get; set; }

    /// <summary>Fuerza la respuesta del motor (por ejemplo, un primer envío todavía en curso, que no se puede provocar con un cuerpo distinto).</summary>
    public ResultadoDeLimite? Forzado { get; set; }

    public List<ClaveDeIdempotencia> Reservadas { get; } = [];

    public List<ReservaDeLimite> Completadas { get; } = [];

    public List<ReservaDeLimite> Falladas { get; } = [];

    public Task<ResultadoDeLimite> Reservar(
        ClaveDePolitica politica,
        string? sujeto = null,
        string? claveDelLlamador = null,
        HuellaDeSujeto? huellaDelLlamador = null,
        ClaveDeIdempotencia? idempotencia = null,
        CancellationToken cancelacion = default)
    {
        if (Forzado is not null)
        {
            return Task.FromResult(Forzado);
        }

        if (Caido)
        {
            return Task.FromResult(ResultadoDeLimite.NoDisponible());
        }

        if (idempotencia is not null)
        {
            Reservadas.Add(idempotencia);
            if (!IgnoraLasClaves && _claves.TryGetValue(idempotencia.ClaveDelCliente, out var previa))
            {
                if (previa.Cuerpo != idempotencia.HuellaDelCuerpo)
                {
                    return Task.FromResult(ResultadoDeLimite.ClaveEnConflicto());
                }

                return Task.FromResult(previa.Resultado is { } r ? ResultadoDeLimite.RepeticionCompletada(r) : ResultadoDeLimite.RepeticionEnCurso());
            }
        }

        if (Usadas >= Maximo)
        {
            return Task.FromResult(ResultadoDeLimite.Excedido(politica.Valor, 1_800, DateTimeOffset.UtcNow.AddSeconds(1_800)));
        }

        Usadas++;
        if (idempotencia is not null)
        {
            _claves[idempotencia.ClaveDelCliente] = (idempotencia.HuellaDelCuerpo, null);
        }

        return Task.FromResult(ResultadoDeLimite.Permitido(new ReservaDeLimite(politica.Valor, "origen", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, null, idempotencia?.ClaveDelCliente)));
    }

    public Task Liberar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion = default)
    {
        Completadas.Add(reserva);
        if (reserva.ClaveDeIdempotencia is { } clave && _claves.TryGetValue(clave, out var actual))
        {
            _claves[clave] = (actual.Cuerpo, resultado);
        }

        return Task.CompletedTask;
    }

    public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion = default)
    {
        Falladas.Add(reserva);
        if (reserva.ClaveDeIdempotencia is { } clave)
        {
            _claves.Remove(clave);
        }

        return Task.CompletedTask;
    }
}

internal sealed class VerificacionDePersonaDoble : IExigenciaDeVerificacionDePersona
{
    public ResultadoDeExigencia Resultado { get; set; } = ResultadoDeExigencia.NoExigida;

    public List<(AccionProtegida Accion, string? Token)> Llamadas { get; } = [];

    public Task<ResultadoDeExigencia> Exigir(AccionProtegida accion, string? tokenDeVerificacion, CancellationToken cancelacion = default)
    {
        Llamadas.Add((accion, tokenDeVerificacion));
        return Task.FromResult(Resultado);
    }
}

/// <summary>El correo de las respuestas: guarda lo enviado, puede fallar o responder encolado o suprimido, y comprueba que no se llame dentro de una transacción.</summary>
internal sealed class EnviadorDeRespuestasDoble(UnidadDeTrabajoReversible unidad) : IEnviadorDeCorreo
{
    public List<CorreoSaliente> Correos { get; } = [];

    public ResultadoDeEnvio Resultado { get; set; } = ResultadoDeEnvio.Enviado;

    public bool Falla { get; set; }

    public int LlamadasDentroDeUnaTransaccion { get; private set; }

    public Task<ResultadoDeEnvio> Enviar(CorreoSaliente correo, CancellationToken cancelacion = default)
    {
        if (unidad.EnCurso)
        {
            LlamadasDentroDeUnaTransaccion++;
        }

        if (Falla)
        {
            throw new InvalidOperationException("SMTP caído.");
        }

        if (Resultado is ResultadoDeEnvio.Enviado or ResultadoDeEnvio.Encolado)
        {
            Correos.Add(correo);
        }

        return Task.FromResult(Resultado);
    }

    public Task<ResultadoDeEnvio> EnviarCodigo(CorreoDeCodigo correo, CancellationToken cancelacion = default) => throw new NotSupportedException();
}

/// <summary>Todo lo que necesitan los casos de uso de las hojas, ya cableado.</summary>
internal sealed class EntornoDeReclamaciones
{
    public EntornoDeReclamaciones(ActorDePrueba? actor = null, DateTimeOffset? ahora = null)
    {
        Actor = actor ?? ActorDePrueba.DeAdministradorCentral();
        Reloj = new RelojFijo(ahora ?? new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero));
        Hojas = new RepositorioDeHojasEnMemoria();
        Unidad = new UnidadDeTrabajoReversible(Hojas);
        Limites = new LimitesDeHojasDoble();
        Verificacion = new VerificacionDePersonaDoble();
        Correo = new EnviadorDeRespuestasDoble(Unidad);
        Auditoria = new AuditoriaDelCatalogoEspia();
        Denegaciones = new DenegacionesDelCatalogoEspia();

        var autorizador = new AutorizadorDeReclamaciones(Actor, Denegaciones);
        var envio = new EnvioDelAvisoDeRespuesta(Correo, Hojas, Unidad, Reloj);
        Reclamaciones = new ReclamacionesDeLaAplicacion(new RegistrarHoja(Verificacion, new CalculadorDeHuellaNormalizante(), Limites, Hojas, Unidad, Reloj));
        Administracion = new AdministracionDeReclamacionesDeLaAplicacion(
            new ListarHojas(autorizador, Hojas, Unidad),
            new ObtenerHojaParaAdministracion(autorizador, Hojas, Unidad, Auditoria),
            new ResponderHoja(autorizador, Hojas, Unidad, Reloj, Auditoria, envio),
            new DescartarHoja(autorizador, Hojas, Unidad, Reloj, Auditoria),
            new ReintentarAvisoDeRespuesta(autorizador, Hojas, Unidad, Reloj, envio));
    }

    public ActorDePrueba Actor { get; }

    public RelojFijo Reloj { get; }

    public RepositorioDeHojasEnMemoria Hojas { get; }

    public UnidadDeTrabajoReversible Unidad { get; }

    public LimitesDeHojasDoble Limites { get; }

    public VerificacionDePersonaDoble Verificacion { get; }

    public EnviadorDeRespuestasDoble Correo { get; }

    public AuditoriaDelCatalogoEspia Auditoria { get; }

    public DenegacionesDelCatalogoEspia Denegaciones { get; }

    public IReclamaciones Reclamaciones { get; }

    public IAdministracionDeReclamaciones Administracion { get; }

    private int _claves;

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
            clave ?? $"clave-del-formulario-{++_claves:D6}",
            null);

    /// <summary>Registra una hoja válida y devuelve su identificador (para responderla o descartarla).</summary>
    public async Task<Guid> RegistrarUna(string? clave = null)
    {
        var resultado = await Reclamaciones.Registrar(Entrada(clave));
        Assert.True(resultado.EsExito);
        return Hojas.Todas.Single(h => h.Numero.ToString() == resultado.Valor.Numero).Id;
    }
}

/// <summary>Una huella que imita la real (<c>CalculadorDeHuellaHmac</c>): recorta, normaliza a NFKC y pasa a minúsculas antes de resumir, de modo que las pruebas vean lo mismo que verá la Infraestructura.</summary>
internal sealed class CalculadorDeHuellaNormalizante : ICalculadorDeHuella
{
    private static string Texto(string valor) =>
        Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(valor.Trim().Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant())))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public HuellaDeOrigen DeOrigen(string direccion) => HuellaDeOrigen.Crear(Texto(direccion), "v1");

    public HuellaDeOrigen? DeOrigenPorRed48(string direccion) => null;

    public IReadOnlyList<HuellaDeOrigen> DeOrigenEnRotacion(string direccion) => [DeOrigen(direccion)];

    public HuellaDeSujeto DeSujeto(string valor, string proposito) => HuellaDeSujeto.Crear(Texto(proposito + "|" + valor), "v1");

    public IReadOnlyList<HuellaDeSujeto> DeSujetoEnRotacion(string valor, string proposito) => [DeSujeto(valor, proposito)];
}
