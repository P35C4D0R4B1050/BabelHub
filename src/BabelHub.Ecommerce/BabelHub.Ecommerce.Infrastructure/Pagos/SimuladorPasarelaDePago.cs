using System.Text.Json;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.Infrastructure.Pagos;

/// <summary>
/// Los tokens de prueba del simulador: el valor del token del medio decide qué responde la pasarela simulada (como las tarjetas de prueba de una pasarela real). Un token que no es de esta lista se rechaza con el motivo <c>otro</c>:
/// el simulador nunca aprueba por defecto. Ninguno parece un número de tarjeta.
/// </summary>
public static class EscenariosDelSimulador
{
    /// <summary>Aprueba por el importe pedido.</summary>
    public const string Aprobado = "tok_aprobado";

    /// <summary>Rechazado por <see cref="MotivoDeRechazo.FondosInsuficientes"/>.</summary>
    public const string FondosInsuficientes = "tok_fondos_insuficientes";

    /// <summary>Rechazado por <see cref="MotivoDeRechazo.TarjetaRechazada"/>.</summary>
    public const string TarjetaRechazada = "tok_tarjeta_rechazada";

    /// <summary>Rechazado por <see cref="MotivoDeRechazo.YapeRechazado"/>.</summary>
    public const string YapeRechazado = "tok_yape_rechazado";

    /// <summary>Un desafío de autenticación sin resolver, que se trata como un rechazo (R03).</summary>
    public const string AutenticacionRequerida = "tok_autenticacion_requerida";

    /// <summary>Aprueba por un sol más que el importe pedido: el aprobado dudoso que no se acepta (AM11).</summary>
    public const string OtroImporte = "tok_otro_importe";

    /// <summary>Tarda 11 segundos en responder: más que el plazo de 10 de la llamada. El cargo queda aprobado al terminar ese tiempo y se confirma por consulta o por aviso.</summary>
    public const string Lento = "tok_lento";

    /// <summary>Aplica el cargo y no responde: el cobro existe y el sistema no lo sabe hasta consultar.</summary>
    public const string AplicaSinResponder = "tok_aplica_sin_responder";

    /// <summary>No responde y no cobra nada.</summary>
    public const string SinRespuesta = "tok_sin_respuesta";

    /// <summary>La llamada falla con una excepción de red.</summary>
    public const string FallaDeRed = "tok_falla_de_red";

    /// <summary>Aprueba y, al reembolsarlo, aplica el reembolso sin responder (el sistema debe consultar antes de pedirlo otra vez).</summary>
    public const string ReembolsoSinRespuesta = "tok_reembolso_sin_respuesta";

    /// <summary>Aprueba y rechaza cualquier reembolso.</summary>
    public const string ReembolsoRechazado = "tok_reembolso_rechazado";
}

/// <summary>
/// La pasarela simulada (D05): una pasarela de mentira con estado, fiel al contrato de <see cref="IPasarelaDePago"/>, <b>solo para <c>Development</c> y <c>Test</c></b> (la guarda de arranque lo exige). El token del medio elige el escenario
/// (<see cref="EscenariosDelSimulador"/>); recuerda los cargos por su referencia opaca, de modo que <c>ConsultarEstado</c> responde lo que respondió o lo que quedó aplicado, y los reembolsos aplicados, que <c>ConsultarReembolsos</c> lista.
/// <b>No es idempotente a propósito</b> (no se supone que Culqi lo sea): cobrar dos veces con la misma referencia crea dos cargos. Es seguro entre hilos. No guarda tokens, correos ni contactos: solo importes, referencias y estados.
/// </summary>
internal sealed class SimuladorPasarelaDePago(IReloj reloj, Func<TimeSpan, CancellationToken, Task>? espera = null) : IPasarelaDePago
{
    /// <summary>Distingue los cargos de esta instancia de los de una anterior: la referencia de la pasarela es única en la base de datos y un reinicio del simulador (que vuelve a contar desde 1) no puede repetir una ya guardada.</summary>
    private readonly string _instancia = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Lo que tarda el escenario <see cref="EscenariosDelSimulador.Lento"/>: más que el plazo de respuesta de la llamada.</summary>
    public static readonly TimeSpan RetrasoDelCobroLento = TimeSpan.FromSeconds(11);

    private readonly Lock _candado = new();
    private readonly List<Cargo> _cargos = [];
    private readonly Func<TimeSpan, CancellationToken, Task> _espera = espera ?? ((plazo, cancelacion) => Task.Delay(plazo, cancelacion));
    private int _secuencia;

    /// <summary>Si Yape admite reembolsos por la API (B05 no lo sabe): con <c>false</c> el reembolso de un cobro con Yape es <c>no_soportado</c>.</summary>
    public bool ReembolsaYape { get; set; } = true;

    /// <summary>Cuántas veces se llamó a <c>Cobrar</c>.</summary>
    public int LlamadasDeCobrar { get; private set; }

    /// <summary>Los cargos que existen (aprobados o rechazados) con esa referencia opaca.</summary>
    public int CargosDe(Guid referenciaOpaca)
    {
        lock (_candado)
        {
            return _cargos.Count(c => c.ReferenciaOpaca == referenciaOpaca);
        }
    }

    /// <summary>Un aviso entrante como lo entiende <see cref="InterpretarAviso"/>: el identificador del evento y la referencia opaca del cargo.</summary>
    public static byte[] CrearAviso(string idDeEntrada, Guid referenciaOpaca) =>
        JsonSerializer.SerializeToUtf8Bytes(new { id = idDeEntrada, referencia = referenciaOpaca });

    public async Task<ResultadoDeCobro> Cobrar(SolicitudDeCobroEnPasarela solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        lock (_candado)
        {
            LlamadasDeCobrar++;
        }

        var escenario = solicitud.Token.Revelar();
        switch (escenario)
        {
            case EscenariosDelSimulador.Aprobado:
            case EscenariosDelSimulador.ReembolsoSinRespuesta:
            case EscenariosDelSimulador.ReembolsoRechazado:
                return Aprobado(Registrar(solicitud, solicitud.Importe, aprobadoDesde: reloj.Ahora, escenario), solicitud);

            case EscenariosDelSimulador.OtroImporte:
                return Aprobado(Registrar(solicitud, solicitud.Importe + Importe.De(1m), reloj.Ahora, escenario), solicitud);

            case EscenariosDelSimulador.Lento:
                var lento = Registrar(solicitud, solicitud.Importe, reloj.Ahora + RetrasoDelCobroLento, escenario);
                await _espera(RetrasoDelCobroLento, cancelacion).ConfigureAwait(false);
                return Aprobado(lento, solicitud);

            case EscenariosDelSimulador.AplicaSinResponder:
                Registrar(solicitud, solicitud.Importe, reloj.Ahora, escenario);
                return ResultadoDeCobro.SinRespuesta();

            case EscenariosDelSimulador.SinRespuesta:
                return ResultadoDeCobro.SinRespuesta();

            case EscenariosDelSimulador.FallaDeRed:
                throw new HttpRequestException("La pasarela simulada falló.");

            default:
                var motivo = escenario switch
                {
                    EscenariosDelSimulador.FondosInsuficientes => MotivoDeRechazo.FondosInsuficientes,
                    EscenariosDelSimulador.TarjetaRechazada => MotivoDeRechazo.TarjetaRechazada,
                    EscenariosDelSimulador.YapeRechazado => MotivoDeRechazo.YapeRechazado,
                    EscenariosDelSimulador.AutenticacionRequerida => MotivoDeRechazo.AutenticacionRequerida,
                    _ => MotivoDeRechazo.Otro,
                };
                Registrar(solicitud, solicitud.Importe, aprobadoDesde: null, escenario, motivo);
                return ResultadoDeCobro.Rechazado(motivo);
        }
    }

    public Task<ResultadoDeCobro> ConsultarEstado(Guid referenciaOpaca, CancellationToken cancelacion = default, DateTimeOffset? iniciadoEn = null)
    {
        lock (_candado)
        {
            // El cargo más reciente con esa referencia: un cargo aprobado es el que importa cuando hay uno rechazado y otro aprobado.
            var cargo = _cargos.Where(c => c.ReferenciaOpaca == referenciaOpaca).OrderByDescending(c => c.Aprobado).ThenByDescending(c => c.Secuencia).FirstOrDefault();
            if (cargo is null)
            {
                return Task.FromResult(ResultadoDeCobro.SinRespuesta());
            }

            if (!cargo.Aprobado)
            {
                return Task.FromResult(ResultadoDeCobro.Rechazado(cargo.Motivo!.Value));
            }

            return Task.FromResult(cargo.AprobadoDesde <= reloj.Ahora
                ? ResultadoDeCobro.Aprobado(cargo.Referencia, cargo.Importe, Importe.Moneda, cargo.ReferenciaOpaca)
                : ResultadoDeCobro.SinRespuesta());
        }
    }

    public Task<ResultadoDeReembolsoEnPasarela> Reembolsar(Guid reembolsoId, string referenciaDelCargo, Importe monto, CancellationToken cancelacion = default)
    {
        lock (_candado)
        {
            var cargo = _cargos.FirstOrDefault(c => c.Aprobado && c.Referencia == referenciaDelCargo);
            if (cargo is null || monto.Valor <= 0m || cargo.Escenario == EscenariosDelSimulador.ReembolsoRechazado)
            {
                return Task.FromResult(ResultadoDeReembolsoEnPasarela.Rechazado);
            }

            if (cargo.Medio == MedioDePago.Yape && !ReembolsaYape)
            {
                return Task.FromResult(ResultadoDeReembolsoEnPasarela.NoSoportado);
            }

            var yaReembolsado = cargo.Reembolsos.Aggregate(0m, (suma, r) => suma + r.Monto.Valor);
            if (yaReembolsado + monto.Valor > cargo.Importe.Valor)
            {
                return Task.FromResult(ResultadoDeReembolsoEnPasarela.Rechazado);
            }

            var referencia = $"re_sim_{++_secuencia}";
            cargo.Reembolsos.Add(new ReembolsoEnPasarela(reembolsoId, referencia, monto));
            return Task.FromResult(cargo.Escenario == EscenariosDelSimulador.ReembolsoSinRespuesta ? ResultadoDeReembolsoEnPasarela.SinRespuesta : ResultadoDeReembolsoEnPasarela.Aplicado(referencia));
        }
    }

    public Task<IReadOnlyList<ReembolsoEnPasarela>?> ConsultarReembolsos(string referenciaDelCargo, CancellationToken cancelacion = default)
    {
        lock (_candado)
        {
            var cargo = _cargos.FirstOrDefault(c => c.Referencia == referenciaDelCargo);
            return Task.FromResult<IReadOnlyList<ReembolsoEnPasarela>?>(cargo is null ? [] : cargo.Reembolsos.ToArray());
        }
    }

    /// <summary>Lee <c>{"id": "…", "referencia": "&lt;guid&gt;"}</c>; lo que no se entiende es <c>null</c>.</summary>
    public AvisoDeLaPasarela? InterpretarAviso(ReadOnlyMemory<byte> cuerpo)
    {
        try
        {
            using var documento = JsonDocument.Parse(cuerpo);
            var raiz = documento.RootElement;
            if (raiz.ValueKind == JsonValueKind.Object
                && raiz.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 and <= 100 } idDeEntrada
                && raiz.TryGetProperty("referencia", out var referencia) && referencia.ValueKind == JsonValueKind.String && Guid.TryParse(referencia.GetString(), out var opaca) && opaca != Guid.Empty)
            {
                return new AvisoDeLaPasarela(idDeEntrada, opaca);
            }
        }
        catch (JsonException)
        {
            // Un cuerpo que no es JSON no se entiende.
        }

        return null;
    }

    private Cargo Registrar(SolicitudDeCobroEnPasarela solicitud, Importe importe, DateTimeOffset? aprobadoDesde, string escenario, MotivoDeRechazo? motivo = null)
    {
        lock (_candado)
        {
            var cargo = new Cargo(++_secuencia, $"ch_sim_{_instancia}_{_secuencia}", solicitud.ReferenciaOpaca, importe, solicitud.Medio, aprobadoDesde, motivo, escenario);
            _cargos.Add(cargo);
            return cargo;
        }
    }

    private static ResultadoDeCobro Aprobado(Cargo cargo, SolicitudDeCobroEnPasarela solicitud) =>
        ResultadoDeCobro.Aprobado(cargo.Referencia, cargo.Importe, Importe.Moneda, solicitud.ReferenciaOpaca);

    private sealed class Cargo(int secuencia, string referencia, Guid referenciaOpaca, Importe importe, MedioDePago medio, DateTimeOffset? aprobadoDesde, MotivoDeRechazo? motivo, string escenario)
    {
        public int Secuencia { get; } = secuencia;

        public string Referencia { get; } = referencia;

        public Guid ReferenciaOpaca { get; } = referenciaOpaca;

        public Importe Importe { get; } = importe;

        public MedioDePago Medio { get; } = medio;

        /// <summary>Desde cuándo está aprobado; <c>null</c> si se rechazó.</summary>
        public DateTimeOffset? AprobadoDesde { get; } = aprobadoDesde;

        public bool Aprobado => AprobadoDesde is not null;

        public MotivoDeRechazo? Motivo { get; } = motivo;

        public string Escenario { get; } = escenario;

        public List<ReembolsoEnPasarela> Reembolsos { get; } = [];
    }
}

/// <summary>
/// La pasarela nula de los modos <c>Restauracion</c> y <c>Ensayo</c> (y de cualquier arranque sin pasarela configurada para cobrar): ninguna llamada real, siempre «sin servicio», <b>nunca <c>Aprobado</c></b>. Un cobro con ella queda
/// pendiente de confirmación y vence; un reembolso queda pendiente y visible para la administración.
/// </summary>
internal sealed class NuloPasarelaDePago : IPasarelaDePago
{
    public Task<ResultadoDeCobro> Cobrar(SolicitudDeCobroEnPasarela solicitud, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeCobro.SinRespuesta());

    public Task<ResultadoDeCobro> ConsultarEstado(Guid referenciaOpaca, CancellationToken cancelacion = default, DateTimeOffset? iniciadoEn = null) => Task.FromResult(ResultadoDeCobro.SinRespuesta());

    public Task<ResultadoDeReembolsoEnPasarela> Reembolsar(Guid reembolsoId, string referenciaDelCargo, Importe monto, CancellationToken cancelacion = default) =>
        Task.FromResult(ResultadoDeReembolsoEnPasarela.SinRespuesta);

    public Task<IReadOnlyList<ReembolsoEnPasarela>?> ConsultarReembolsos(string referenciaDelCargo, CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<ReembolsoEnPasarela>?>(null);

    public AvisoDeLaPasarela? InterpretarAviso(ReadOnlyMemory<byte> cuerpo) => null;
}
