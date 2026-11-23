using System.Collections.Concurrent;
using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Domain.Envios;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Ecommerce.Infrastructure.Envios;

/// <summary>Cómo responde el simulador del operador (los modos distintos de <c>Guion</c> solo los usan las pruebas).</summary>
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
/// respuesta perdida. Corre en <c>Development</c>, <c>Test</c>, <c>Staging</c> y <c>Production</c> (DD-88): el número de guía sale de la clave de solicitud, no de un contador del proceso, para no repetirse tras un reinicio ni entre las dos instancias del E-commerce. <b>No guarda ni imprime el contacto</b> que recibe.
/// </summary>
internal sealed class SimuladorOperadorLogistico : IOperadorLogistico
{
    private readonly ConcurrentDictionary<Guid, NumeroDeGuia> _guias = new();
    private readonly ConcurrentDictionary<Guid, bool> _perdidas = new();

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

        var numero = _guias.GetOrAdd(claveDeSolicitud, _ => NumeroDeGuia.Crear($"SIM-{claveDeSolicitud:N}"[..16].ToUpperInvariant()));
        if (Modo == ModoDelSimuladorDeOperador.RespuestaPerdida && _perdidas.TryAdd(claveDeSolicitud, true))
        {
            return Task.FromResult<ResultadoDelOperador>(new ResultadoDelOperador.NoDisponible());
        }

        return Task.FromResult<ResultadoDelOperador>(new ResultadoDelOperador.GuiaCreada(numero));
    }
}

/// <summary>
/// El operador nulo (D02): <b>falla cerrado</b> con «no disponible». Es el único permitido en los modos <c>Restauracion</c> y <c>Ensayo</c>; en un entorno de tipo producción en modo normal no se admite (hay que elegir el simulador, DD-88,
/// porque no habrá un operador real, DD-22).
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
/// La guarda de arranque de Envíos (D02): <c>Envios:Operador</c> vale <c>nulo</c> (por defecto) o <c>simulador</c>. El simulador corre en <c>Development</c>, <c>Test</c>, <c>Staging</c> y <c>Production</c> (lista permitida; DD-22, PAG-42) y
/// con un modo que prohíbe llamadas reales solo se admite el nulo. <b>Un valor desconocido, o un operador real que todavía no existe, rechaza el arranque</b>: nunca cae en silencio al nulo ni al simulador.
/// </summary>
public static class GuardaDeArranqueDeEnvios
{
    public const string VariableOperador = "Envios:Operador";

    public const string Nulo = "nulo";

    public const string Simulador = "simulador";

    // No existe un operador logístico real y no lo habrá (DD-22): el simulador también corre en Staging y en Production, donde la tienda lo anuncia (PAG-42).
    private static readonly string[] EntornosDelSimulador = ["Development", "Test", "Staging", "Production"];

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
                    throw new ConfiguracionDeEnviosInvalidaException($"{VariableOperador}: el simulador solo corre en Development, Test, Staging y Production, no en {opciones.Entorno}.");
                }

                return Simulador;
            default:
                throw new ConfiguracionDeEnviosInvalidaException($"{VariableOperador}: el valor no es «{Nulo}» ni «{Simulador}», y el adaptador del operador real todavía no existe (D02); el arranque no cae en silencio a otro.");
        }
    }
}

public static class ComposicionDelOperadorLogistico
{
    /// <summary>Registra el operador que decide la guarda de arranque (el nulo por defecto). Con <c>Replace</c>: la elección explícita de la guarda gana sobre el nulo por defecto de <c>AgregarEnvios</c>, se llame antes o después.</summary>
    public static IServiceCollection AgregarOperadorLogisticoDeEnvios(this IServiceCollection servicios, OpcionesDeArranqueDeEnvios opciones)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        var elegido = GuardaDeArranqueDeEnvios.Exigir(opciones);
        if (elegido == GuardaDeArranqueDeEnvios.Simulador)
        {
            servicios.TryAddSingleton<SimuladorOperadorLogistico>();
            servicios.Replace(ServiceDescriptor.Singleton<IOperadorLogistico>(p => p.GetRequiredService<SimuladorOperadorLogistico>()));
        }
        else
        {
            servicios.Replace(ServiceDescriptor.Singleton<IOperadorLogistico, NuloOperadorLogistico>());
        }

        return servicios;
    }
}
