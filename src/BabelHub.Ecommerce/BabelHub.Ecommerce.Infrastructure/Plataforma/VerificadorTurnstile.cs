using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Infrastructure.Plataforma;

public sealed record OpcionesDeTurnstile
{
    public OpcionesDeTurnstile(ValorSecreto secreto, IEnumerable<string> hostnamesPermitidos)
    {
        ArgumentNullException.ThrowIfNull(secreto);
        ArgumentNullException.ThrowIfNull(hostnamesPermitidos);
        Secreto = secreto;
        HostnamesPermitidos = hostnamesPermitidos.Select(h => h.Trim().ToLowerInvariant()).Where(h => h.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (HostnamesPermitidos.Count == 0)
        {
            throw new ArgumentException("Se necesita al menos un hostname permitido.", nameof(hostnamesPermitidos));
        }
    }

    /// <summary>Secreto del sitio en Cloudflare (variable de entorno); nunca llega a un registro: su texto siempre está redactado.</summary>
    public ValorSecreto Secreto { get; }

    /// <summary>Hostnames en los que se admite el desafío: el de la tienda; un token de otro sitio no sirve.</summary>
    public IReadOnlySet<string> HostnamesPermitidos { get; }

    /// <summary>Verificaciones simultáneas como máximo (bulkhead): el exceso se rechaza al instante como «no disponible».</summary>
    public int MaximoDeVerificacionesSimultaneas { get; init; } = 20;

    /// <summary>Cuánto se recuerda un token usado para rechazar su reutilización (un token de Turnstile vale 5 minutos).</summary>
    public TimeSpan VigenciaDelTokenUsado { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>Respuesta de <c>siteverify</c> de Cloudflare Turnstile.</summary>
public sealed record RespuestaDeTurnstile(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("hostname")] string? Hostname,
    [property: JsonPropertyName("action")] string? Action,
    [property: JsonPropertyName("error-codes")] IReadOnlyList<string>? ErrorCodes);

/// <summary>Una respuesta con éxito debe traer el hostname y la acción con forma razonable; si no, se descarta como si no hubiera respondido (SEG-13).</summary>
public sealed class ValidadorDeRespuestaDeTurnstile : IValidadorDeRespuestaExterna<RespuestaDeTurnstile>
{
    public ResultadoDeValidacion Validar(RespuestaDeTurnstile respuesta)
    {
        var validacion = new ConstructorDeValidacion();
        if (respuesta.Success)
        {
            validacion.Texto("hostname", respuesta.Hostname, 1, 253).Texto("action", respuesta.Action, 1, 32);
        }

        if (respuesta.ErrorCodes is { Count: > 20 } || (respuesta.ErrorCodes?.Any(c => c is null || c.Length > 64) ?? false))
        {
            return ResultadoDeValidacion.Invalido([new ErrorDeCampo("error-codes", ConstructorDeValidacion.FormatoInvalido)]);
        }

        return validacion.Resultado();
    }
}

/// <summary>
/// Verifica en el servidor que quien solicita es una persona (SEG-31) con <c>siteverify</c> de Cloudflare Turnstile: comprueba
/// <c>success</c>, la acción esperada de la página y el hostname, y que el token no se haya usado antes (por Cloudflare y, además,
/// en esta instancia). Una sola consulta con 10 s de espera (SEG-14) y un tope de verificaciones simultáneas (bulkhead). Si no
/// responde, responde con un formato inesperado o está saturado, informa «no disponible»: falla cerrada para las páginas que la
/// exigen. No se envía la dirección de la persona a Cloudflare; el token y el secreto no se registran.
/// </summary>
public sealed class VerificadorTurnstile : IVerificadorDePersona, IDisposable
{
    private const int TopeDeTokensRecordados = 100_000;

    private readonly HttpClient _http;
    private readonly OpcionesDeTurnstile _opciones;
    private readonly IDependenciasExternas _dependencias;
    private readonly TimeProvider _tiempo;
    private readonly SemaphoreSlim _bulkhead;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _usados = new(StringComparer.Ordinal);
    private readonly ValidadorDeRespuestaDeTurnstile _validador = new();

    public VerificadorTurnstile(HttpClient http, OpcionesDeTurnstile opciones, IDependenciasExternas dependencias, TimeProvider? tiempo = null)
    {
        _http = http;
        _opciones = opciones;
        _dependencias = dependencias;
        _tiempo = tiempo ?? TimeProvider.System;
        _bulkhead = new SemaphoreSlim(opciones.MaximoDeVerificacionesSimultaneas);
    }

    public async Task<ResultadoDeVerificacionDePersona> Verificar(string token, string accion, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accion);

        // Validación previa: un token vacío o desmedido ni siquiera sale hacia Cloudflare.
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
        {
            return ResultadoDeVerificacionDePersona.Fallida;
        }

        var huella = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        if (!RegistrarUso(huella))
        {
            return ResultadoDeVerificacionDePersona.Fallida;
        }

        if (!await _bulkhead.WaitAsync(0, cancelacion).ConfigureAwait(false))
        {
            _usados.TryRemove(huella, out _);
            return ResultadoDeVerificacionDePersona.NoDisponible;
        }

        try
        {
            var respuesta = await _dependencias.Consultar(
                "turnstile",
                ClaseDeDependencia.Normal,
                token2 => Consultar(token, token2),
                _validador,
                cancelacion).ConfigureAwait(false);

            if (!respuesta.HayRespuesta)
            {
                // No se pudo comprobar: el token no se consumió y la persona puede reintentar.
                _usados.TryRemove(huella, out _);
                return ResultadoDeVerificacionDePersona.NoDisponible;
            }

            var datos = respuesta.Valor;
            var valida = datos.Success
                && string.Equals(datos.Action, accion, StringComparison.Ordinal)
                && datos.Hostname is { } host && _opciones.HostnamesPermitidos.Contains(host.ToLowerInvariant());
            return valida ? ResultadoDeVerificacionDePersona.Verificada : ResultadoDeVerificacionDePersona.Fallida;
        }
        finally
        {
            _bulkhead.Release();
        }
    }

    public void Dispose() => _bulkhead.Dispose();

    private async Task<RespuestaDeTurnstile> Consultar(string token, CancellationToken cancelacion)
    {
        using var contenido = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("secret", _opciones.Secreto.Revelar()),
            new KeyValuePair<string, string>("response", token),
        ]);
        using var respuesta = await _http.PostAsync((Uri?)null, contenido, cancelacion).ConfigureAwait(false);
        respuesta.EnsureSuccessStatusCode();

        await using var flujo = await respuesta.Content.ReadAsStreamAsync(cancelacion).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<RespuestaDeTurnstile>(flujo, cancellationToken: cancelacion).ConfigureAwait(false)
            ?? throw new JsonException("La respuesta de Turnstile está vacía.");
    }

    /// <summary>Recuerda el token como usado; devuelve falso si ya lo estaba (reutilización).</summary>
    private bool RegistrarUso(string huella)
    {
        var ahora = _tiempo.GetUtcNow();
        if (_usados.Count >= TopeDeTokensRecordados)
        {
            foreach (var vencido in _usados.Where(p => p.Value <= ahora).Select(p => p.Key).ToList())
            {
                _usados.TryRemove(vencido, out _);
            }
        }

        if (_usados.Count >= TopeDeTokensRecordados)
        {
            // Sin lugar para recordarlo, se confía en que Cloudflare rechace la reutilización (un token vale una vez).
            return true;
        }

        var vence = ahora + _opciones.VigenciaDelTokenUsado;
        while (true)
        {
            if (_usados.TryAdd(huella, vence))
            {
                return true;
            }

            if (_usados.TryGetValue(huella, out var existente) && existente > ahora)
            {
                return false;
            }

            _usados.TryRemove(huella, out _);
        }
    }
}
