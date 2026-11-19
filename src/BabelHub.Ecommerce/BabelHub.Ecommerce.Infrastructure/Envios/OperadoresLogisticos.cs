using System.Collections.Concurrent;
using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Domain.Envios;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.Ecommerce.Infrastructure.Envios;

/// <summary>Cómo responde el simulador del operador (solo en <c>Development</c> y <c>Test</c>).</summary>
internal enum ModoDelSimuladorDeOperador
{
    /// <summary>Crea la guía y la devuelve; repetir la clave devuelve la misma.</summary>
    Guion,

    /// <summary>No responde (plazo agotado): <see cref="ResultadoDelOperador.NoDisponible"/>.</summary>
    NoResponde,

    /// <summary>Rechaza la solicitud (por ejemplo, una dirección que no puede atender).</summary>
    Rechaza,

    /// <summary>Devuelve algo que no cumple el formato de la guía (SEG-13).</summary>
    RespuestaInvalida,

    /// <summary>Crea la guía pero la respuesta se pierde la primera vez: el reintento con la misma clave la recupera (ENV-18).</summary>
    RespuestaPerdida,
}

/// <summary>
/// El operador logístico simulado (D02): <b>idempotente por clave de solicitud</b> (la misma clave devuelve la misma guía, incluso con peticiones simultáneas), con los modos de no respuesta, rechazo, respuesta inválida y
/// respuesta perdida. Solo existe en <c>Development</c> y <c>Test</c>: la guarda de arranque lo rechaza en cualquier otro entorno. <b>No guarda ni imprime el contacto</b> que recibe.
/// </summary>
internal sealed class SimuladorOperadorLogistico : IOperadorLogistico
{
    private readonly ConcurrentDictionary<Guid, NumeroDeGuia> _guias = new();
    private readonly ConcurrentDictionary<Guid, bool> _perdidas = new();
    private int _secuencia;

    public string Nombre => "simulador";

    public ModoDelSimuladorDeOperador Modo { get; set; } = ModoDelSimuladorDeOperador.Guion;

    /// <summary>Cuántas guías distintas creó (para comprobar que un reintento no duplica).</summary>
    public int GuiasCreadas => _guias.Count;

    public Task<ResultadoDelOperador> CrearOObtenerGuia(Guid claveDeSolicitud, DestinatarioParaGuia destinatario, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(destinatario);
        switch (Modo)
        {
            case ModoDelSimuladorDeOperador.NoResponde:
                return Task.FromResult<ResultadoDelOperador>(new ResultadoDelOperador.NoDisponible());
            case ModoDelSimuladorDeOperador.Rechaza:
                return Task.FromResult<ResultadoDelOperador>(new ResultadoDelOperador.Rechazada());
            case ModoDelSimuladorDeOperador.RespuestaInvalida:
                return Task.FromResult<ResultadoDelOperador>(new ResultadoDelOperador.RespuestaInvalida());
        }

        var numero = _guias.GetOrAdd(claveDeSolicitud, _ => NumeroDeGuia.Crear($"SIM-{Interlocked.Increment(ref _secuencia):D8}"));
        if (Modo == ModoDelSimuladorDeOperador.RespuestaPerdida && _perdidas.TryAdd(claveDeSolicitud, true))
        {
            return Task.FromResult<ResultadoDelOperador>(new ResultadoDelOperador.NoDisponible());
        }

        return Task.FromResult<ResultadoDelOperador>(new ResultadoDelOperador.GuiaCreada(numero));
    }
}

/// <summary>
/// El operador nulo (D02): <b>falla cerrado</b> con «no disponible». Es el valor por defecto en <c>Normal</c> y en producción mientras no exista un operador real (no impide arrancar el E-commerce) y el único
/// permitido en los modos <c>Restauracion</c> y <c>Ensayo</c>.
/// </summary>
internal sealed class NuloOperadorLogistico : IOperadorLogistico
{
    public string Nombre => "nulo";

    public Task<ResultadoDelOperador> CrearOObtenerGuia(Guid claveDeSolicitud, DestinatarioParaGuia destinatario, CancellationToken cancelacion = default) =>
        Task.FromResult<ResultadoDelOperador>(new ResultadoDelOperador.NoDisponible());
}

/// <summary>Se rechaza el arranque: el mensaje nombra la variable y el motivo (enumerado), nunca un valor secreto.</summary>
public sealed class ConfiguracionDeEnviosInvalidaException : InvalidOperationException
{
    public ConfiguracionDeEnviosInvalidaException(string mensaje)
        : base(mensaje)
    {
    }

    public ConfiguracionDeEnviosInvalidaException()
    {
    }

    public ConfiguracionDeEnviosInvalidaException(string mensaje, Exception interna)
        : base(mensaje, interna)
    {
    }
}

/// <summary>Lo que decide el arranque del módulo <c>Envios</c>: el operador elegido, el entorno y si el modo prohíbe llamadas reales (<c>Restauracion</c> y <c>Ensayo</c>).</summary>
public sealed record OpcionesDeArranqueDeEnvios(string? Operador, string Entorno, bool SinLlamadasReales);

/// <summary>
/// La guarda de arranque de Envíos (D02): <c>Envios:Operador</c> vale <c>nulo</c> (por defecto) o <c>simulador</c>. El simulador solo corre en <c>Development</c> y <c>Test</c> (lista permitida, no «no producción») y
/// con un modo que prohíbe llamadas reales solo se admite el nulo. <b>Un valor desconocido, o un operador real que todavía no existe, rechaza el arranque</b>: nunca cae en silencio al nulo ni al simulador.
/// </summary>
public static class GuardaDeArranqueDeEnvios
{
    public const string VariableOperador = "Envios:Operador";

    public const string Nulo = "nulo";

    public const string Simulador = "simulador";

    private static readonly string[] EntornosDelSimulador = ["Development", "Test"];

    public static string Exigir(OpcionesDeArranqueDeEnvios opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        var operador = string.IsNullOrWhiteSpace(opciones.Operador) ? Nulo : opciones.Operador.Trim().ToLowerInvariant();
        switch (operador)
        {
            case Nulo:
                return Nulo;
            case Simulador:
                if (opciones.SinLlamadasReales)
                {
                    throw new ConfiguracionDeEnviosInvalidaException($"{VariableOperador}: el simulador no se admite en un modo que prohíbe llamadas reales (Restauracion y Ensayo solo admiten «{Nulo}»).");
                }

                if (!EntornosDelSimulador.Contains(opciones.Entorno, StringComparer.Ordinal))
                {
                    throw new ConfiguracionDeEnviosInvalidaException($"{VariableOperador}: el simulador solo corre en Development y Test, no en {opciones.Entorno}.");
                }

                return Simulador;
            default:
                throw new ConfiguracionDeEnviosInvalidaException($"{VariableOperador}: el valor no es «{Nulo}» ni «{Simulador}», y el adaptador del operador real todavía no existe (D02); el arranque no cae en silencio a otro.");
        }
    }
}

public static class ComposicionDelOperadorLogistico
{
    /// <summary>Registra el operador que decide la guarda de arranque (el nulo por defecto). Con <c>TryAdd</c>, no pisa lo registrado antes.</summary>
    public static IServiceCollection AgregarOperadorLogisticoDeEnvios(this IServiceCollection servicios, OpcionesDeArranqueDeEnvios opciones)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        var elegido = GuardaDeArranqueDeEnvios.Exigir(opciones);
        if (elegido == GuardaDeArranqueDeEnvios.Simulador)
        {
            servicios.AddSingleton<SimuladorOperadorLogistico>();
            servicios.AddSingleton<IOperadorLogistico>(p => p.GetRequiredService<SimuladorOperadorLogistico>());
        }
        else
        {
            servicios.AddSingleton<IOperadorLogistico, NuloOperadorLogistico>();
        }

        return servicios;
    }
}
