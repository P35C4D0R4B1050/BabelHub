using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;
using TipoDeActor = BabelHub.Ecommerce.Application.Plataforma.TipoDeActor;

namespace BabelHub.Ecommerce.Application.Auditoria.Interno;

/// <summary>Una clave de firma de cursores con su versión (<c>BABELHUB__Auditoria__ClaveDeCursor</c> y su <c>…Anterior</c>, rotación en dos pasos).</summary>
public sealed record ClaveDeCursor(string Version, ValorSecreto Clave);

/// <summary>Las claves de firma de cursores del servicio (una por servicio, distinta entre ellos): la Infraestructura las lee del entorno. La vigente firma; la anterior solo verifica.</summary>
public interface IClavesDeCursor
{
    ClaveDeCursor Vigente { get; }

    ClaveDeCursor? Buscar(string version);
}

/// <summary>
/// Firma y verifica los cursores de las listas (AUD-09, AUD-30, D05): <c>versión.carga.firma</c> con HMAC-SHA256 (el algoritmo es fijo: el cursor no lo negocia),
/// ligado al usuario y a los filtros, con caducidad corta. Un cursor alterado, truncado, de otro usuario, con otros filtros, caducado, de una clave
/// desconocida o con otra forma se rechaza igual (quien llama responde 400 uniforme): el motivo nunca se distingue. El rol y la sucursal no viajan en el
/// cursor: se revalidan en cada página.
/// </summary>
public sealed class FirmadorDeCursor(IClavesDeCursor claves, IReloj reloj)
{
    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(30);

    private sealed record Sobre(string U, string F, long E, JsonElement D);

    public string Firmar<T>(Guid usuario, string huellaDeFiltro, T carga)
    {
        ArgumentNullException.ThrowIfNull(carga);
        var clave = claves.Vigente;
        var sobre = new Sobre(usuario.ToString("N"), huellaDeFiltro, (reloj.Ahora + Vigencia).ToUnixTimeSeconds(), JsonSerializer.SerializeToElement(carga));
        var cuerpo = clave.Version + "." + Base64Url(JsonSerializer.SerializeToUtf8Bytes(sobre));
        var cursor = cuerpo + "." + Base64Url(Calcular(clave, cuerpo));
        return cursor.Length > PaginaDeRegistros.MaximoDeCaracteresDelCursor
            ? throw new InvalidOperationException("El cursor supera el tope de caracteres.")
            : cursor;
    }

    /// <summary>La carga del cursor si es auténtico, vigente, del mismo usuario y de los mismos filtros; <see langword="null"/> en cualquier otro caso.</summary>
    public T? Abrir<T>(string? cursor, Guid usuario, string huellaDeFiltro)
        where T : class
    {
        try
        {
            if (string.IsNullOrEmpty(cursor) || cursor.Length > PaginaDeRegistros.MaximoDeCaracteresDelCursor)
            {
                return null;
            }

            var partes = cursor.Split('.');
            if (partes.Length != 3 || claves.Buscar(partes[0]) is not { } clave)
            {
                return null;
            }

            var esperada = Calcular(clave, partes[0] + "." + partes[1]);
            if (!CryptographicOperations.FixedTimeEquals(esperada, DeBase64Url(partes[2])))
            {
                return null;
            }

            var sobre = JsonSerializer.Deserialize<Sobre>(DeBase64Url(partes[1]));
            if (sobre is null || sobre.U != usuario.ToString("N") || sobre.F != huellaDeFiltro || sobre.E < reloj.Ahora.ToUnixTimeSeconds())
            {
                return null;
            }

            return sobre.D.Deserialize<T>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[] Calcular(ClaveDeCursor clave, string cuerpo) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(clave.Clave.Revelar()), Encoding.UTF8.GetBytes(cuerpo));

    private static string Base64Url(byte[] datos) => Convert.ToBase64String(datos).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DeBase64Url(string texto)
    {
        var base64 = texto.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='));
    }
}

/// <summary>
/// Valida los filtros de la consulta de auditoría (AUD-07): acción de la lista cerrada del catálogo, origen con su formato, texto de 3 a 100 caracteres sin
/// caracteres de control (los comodines se escapan en la consulta, nunca se rechazan ni se interpretan), rango de fechas coherente y de como máximo un año.
/// Un filtro inválido es una <see cref="CategoriaDeError.Validacion"/> (<c>ECO-VALIDACION-INVALIDA</c>); la validación es la misma en cada página.
/// </summary>
public static partial class ValidacionDeFiltroDeAuditoria
{
    public const int MinimoDeCaracteresDelTexto = 3;

    public const int MaximoDeCaracteresDelTexto = 100;

    public const int MaximoDeDiasDelRango = 366;

    public static ErrorDeAplicacion? Validar(FiltroDeAuditoria filtro, CatalogoDeAcciones catalogo)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        ArgumentNullException.ThrowIfNull(catalogo);
        var errores = new List<ErrorDeCampo>();

        if (filtro.Accion is not null && !catalogo.TryObtener(filtro.Accion, out _))
        {
            errores.Add(new ErrorDeCampo("accion", "accion-desconocida"));
        }

        if (filtro.Origen is not null && !FormatoDelOrigen().IsMatch(filtro.Origen))
        {
            errores.Add(new ErrorDeCampo("origen", "origen-invalido"));
        }

        if (filtro.Texto is not null)
        {
            var texto = filtro.Texto.Trim();
            if (texto.Length is < MinimoDeCaracteresDelTexto or > MaximoDeCaracteresDelTexto || texto.Any(char.IsControl))
            {
                errores.Add(new ErrorDeCampo("texto", "texto-fuera-de-rango"));
            }
        }

        if (filtro.Desde is { } desde && filtro.Hasta is { } hasta && (hasta < desde || (hasta - desde).TotalDays > MaximoDeDiasDelRango))
        {
            errores.Add(new ErrorDeCampo("rango", "rango-invalido"));
        }

        return errores.Count == 0 ? null : new ErrorDeAplicacion(CategoriaDeError.Validacion, "filtro-invalido", errores);
    }

    /// <summary>
    /// Un resumen canónico del filtro (ligado al cursor: otro filtro, otro cursor). Las fechas en UTC y el texto sin los espacios de los extremos; no es secreto,
    /// pero el texto buscado no se imprime ni se registra: solo viaja dentro del cursor firmado como su resumen.
    /// </summary>
    public static string Huella(FiltroDeAuditoria filtro)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var canonico = string.Join(
            '\u001f',
            filtro.Accion ?? string.Empty,
            filtro.UsuarioId?.ToString("N") ?? string.Empty,
            filtro.Origen ?? string.Empty,
            filtro.Desde?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            filtro.Hasta?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            filtro.Texto?.Trim() ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonico)));
    }

    [GeneratedRegex(@"^[A-Za-z0-9:._-]{1,64}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormatoDelOrigen();
}

/// <summary>El rol que puede consultar la auditoría completa (AUD-27) y el del historial de una sucursal (AUD-08).</summary>
internal static class RolesDeConsultaDeAuditoria
{
    public const string AdministradorCentral = "administrador-central";

    public const string EncargadoDeSucursal = "encargado-de-sucursal";

    public static bool EsAdministradorCentral(ActorActual? actor) => actor is { Tipo: TipoDeActor.Usuario, UsuarioId: not null } && actor.Rol == AdministradorCentral;
}

/// <summary>
/// Puerto de lectura de los registros propios del servicio (<c>auditoria.registros</c>, T25): una página con los filtros ya validados, el orden fijo
/// <c>(instante_utc desc, servicio, sucursal_origen, id)</c>, <c>statement_timeout</c> y el texto buscado con <c>%</c>, <c>_</c> y <c>\</c> escapados
/// (<c>ILIKE</c> con <c>pg_trgm</c>). El cursor es opaco y fija la secuencia máxima al abrir (AUD-30): la primera página (sin cursor) abre la instantánea
/// y devuelve <see cref="PaginaDeRegistros.CursorDeLaPagina"/>.
/// </summary>
public interface IConsultaLocalDeRegistros
{
    Task<PaginaDeRegistros> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default);
}

/// <summary>El orden de la lista unificada (D05, AUD-09): fecha UTC descendente y, ante igual fecha, servicio, sucursal de origen (sin sucursal, al final) e identificador.</summary>
internal sealed class OrdenDeLaLista : IComparer<RegistroAuditado>
{
    public static readonly OrdenDeLaLista Instancia = new();

    public int Compare(RegistroAuditado? x, RegistroAuditado? y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);

        var porFecha = y.InstanteUtc.CompareTo(x.InstanteUtc);
        if (porFecha != 0)
        {
            return porFecha;
        }

        var porServicio = x.Servicio.CompareTo(y.Servicio);
        if (porServicio != 0)
        {
            return porServicio;
        }

        if (x.SucursalOrigen is null != y.SucursalOrigen is null)
        {
            return x.SucursalOrigen is null ? 1 : -1;
        }

        var porSucursal = string.CompareOrdinal(x.SucursalOrigen, y.SucursalOrigen);
        return porSucursal != 0 ? porSucursal : string.CompareOrdinal(x.Id.ToString("D"), y.Id.ToString("D"));
    }
}
