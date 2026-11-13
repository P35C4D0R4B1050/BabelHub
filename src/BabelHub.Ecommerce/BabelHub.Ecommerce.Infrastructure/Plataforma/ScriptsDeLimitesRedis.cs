using System.Security.Cryptography;
using System.Text;

namespace BabelHub.Ecommerce.Infrastructure.Plataforma;

/// <summary>
/// Scripts Lua del motor de límites. Cada uno es una unidad atómica en Redis, lee el instante con <c>TIME</c> (milisegundos
/// enteros) y recibe todas las claves en <c>KEYS</c>; el usuario ACL del motor solo puede ejecutarlos y usar los comandos que
/// invocan. El separador de campos del estado de idempotencia es el carácter 31 (<c>\31</c>).
/// </summary>
internal static class ScriptsDeLimitesRedis
{
    /// <summary>
    /// KEYS: 1 contador, 2 bloqueo, 3 reserva del llamador, 4 idempotencia del cliente.
    /// ARGV: 1 máximo, 2 ventana (ms), 3 bloqueo (ms, 0 = sin bloqueo), 4 identificador nuevo, 5 tope de claves, 6 margen (ms),
    /// 7 con reserva del llamador, 8 vigencia de la reserva (ms), 9 con idempotencia, 10 vigencia «en curso» (ms), 11 huella del cuerpo.
    /// Devuelve {estado, ahora, ...}: 1 permitido {id}, 2 excedido {disponibleDesde}, 3 repetición completada {desenlace, recurso},
    /// 4 repetición en curso, 5 conflicto de clave, 6 sin capacidad (tope de claves).
    /// </summary>
    public const string Reservar = """
        local t = redis.call('TIME')
        local ahora = tonumber(t[1]) * 1000 + math.floor(tonumber(t[2]) / 1000)
        local maximo = tonumber(ARGV[1])
        local ventana = tonumber(ARGV[2])
        local bloqueo = tonumber(ARGV[3])

        if ARGV[9] == '1' then
          local previo = redis.call('GET', KEYS[4])
          if previo then
            local estado, huella, desenlace, recurso = string.match(previo, '^([^\31]*)\31([^\31]*)\31([^\31]*)\31(.*)$')
            if huella ~= ARGV[11] then return {5, ahora} end
            if estado == 'completada' then return {3, ahora, desenlace, recurso} end
            return {4, ahora}
          end
        end

        if ARGV[7] == '1' then
          local previa = redis.call('GET', KEYS[3])
          if previa then
            local id, instante = string.match(previa, '^([^\31]*)\31(.*)$')
            return {1, tonumber(instante), id}
          end
        end

        local restante = redis.call('PTTL', KEYS[2])
        if restante > 0 then return {2, ahora, ahora + restante} end

        -- Las lecturas van primero: un rechazo no escribe nada y, con la memoria llena, el primer comando de escritura
        -- (ZADD) es el que Redis rechaza con OOM, como corresponde a «almacén no disponible» y nunca a permitir.
        local corte = ahora - ventana
        local cuenta = redis.call('ZCOUNT', KEYS[1], '(' .. corte, '+inf')
        if cuenta >= maximo then
          local saliente = redis.call('ZRANGE', KEYS[1], '(' .. corte, '+inf', 'BYSCORE', 'LIMIT', cuenta - maximo, 1, 'WITHSCORES')
          return {2, ahora, tonumber(saliente[2]) + ventana}
        end

        if redis.call('EXISTS', KEYS[1]) == 0 and redis.call('DBSIZE') >= tonumber(ARGV[5]) then
          return {6, ahora}
        end

        local id = ARGV[4]
        redis.call('ZADD', KEYS[1], ahora, id)
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', corte)
        redis.call('PEXPIRE', KEYS[1], ventana + tonumber(ARGV[6]))
        if bloqueo > 0 and cuenta + 1 >= maximo then
          redis.call('SET', KEYS[2], id, 'PX', bloqueo)
        end
        if ARGV[7] == '1' then
          redis.call('SET', KEYS[3], id .. '\31' .. ahora, 'PX', tonumber(ARGV[8]))
        end
        if ARGV[9] == '1' then
          redis.call('SET', KEYS[4], 'en_curso\31' .. ARGV[11] .. '\31\31', 'PX', tonumber(ARGV[10]))
        end
        return {1, ahora, id}
        """;

    /// <summary>KEYS: 1 contador, 2 bloqueo, 3 reserva del llamador. ARGV: 1 identificador de la reserva, 2 con reserva del llamador.</summary>
    public const string Liberar = """
        redis.call('ZREM', KEYS[1], ARGV[1])
        if redis.call('GET', KEYS[2]) == ARGV[1] then
          redis.call('DEL', KEYS[2])
        end
        if ARGV[2] == '1' then
          redis.call('DEL', KEYS[3])
        end
        return 1
        """;

    /// <summary>KEYS: 1 idempotencia del cliente. ARGV: 1 desenlace, 2 identificador del recurso, 3 vigencia (ms).</summary>
    public const string Completar = """
        local previo = redis.call('GET', KEYS[1])
        if not previo then return 0 end
        local huella = string.match(previo, '^[^\31]*\31([^\31]*)')
        redis.call('SET', KEYS[1], 'completada\31' .. huella .. '\31' .. ARGV[1] .. '\31' .. ARGV[2], 'PX', tonumber(ARGV[3]))
        return 1
        """;

    /// <summary>
    /// KEYS: 1 rechazos del origen. ARGV: 1 ventana (ms), 2 identificador único, 3 registrar (1) o solo contar (0), 4 tope de claves,
    /// 5 margen (ms). Devuelve los rechazos dentro de la ventana.
    /// </summary>
    public const string RechazosDelOrigen = """
        local t = redis.call('TIME')
        local ahora = tonumber(t[1]) * 1000 + math.floor(tonumber(t[2]) / 1000)
        local ventana = tonumber(ARGV[1])
        local corte = ahora - ventana
        if ARGV[3] == '1' and (redis.call('EXISTS', KEYS[1]) == 1 or redis.call('DBSIZE') < tonumber(ARGV[4])) then
          redis.call('ZADD', KEYS[1], ahora, ARGV[2])
          redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', corte)
          redis.call('PEXPIRE', KEYS[1], ventana + tonumber(ARGV[5]))
        end
        return redis.call('ZCOUNT', KEYS[1], '(' .. corte, '+inf')
        """;

    public static string Huella(string script) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(script))).ToLowerInvariant();
}
