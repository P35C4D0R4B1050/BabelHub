using System.Text.RegularExpressions;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Auditoria;

/// <summary>
/// Lo que un acceso denegado necesita saber (AUD-05): el motivo (un código de catálogo: fuera de rol, fuera de sucursal, sin sesión…) y la ruta tal como
/// llegó, que el módulo reduce a su plantilla sin parámetros, sin consulta, sin «;» y sin fragmento (o «sin-coincidencia») antes de guardarla. El usuario
/// o «anónimo», su rol y el origen salen del contexto verificado, no de aquí.
/// </summary>
public sealed partial record DenegacionDeAcceso
{
    public DenegacionDeAcceso(string motivo, string? ruta)
    {
        if (string.IsNullOrEmpty(motivo) || !FormaDelMotivo().IsMatch(motivo))
        {
            throw new ArgumentException("El motivo de una denegación es un código del catálogo (minúsculas, dígitos y guiones).", nameof(motivo));
        }

        Motivo = motivo;
        Ruta = ruta;
    }

    public string Motivo { get; }

    /// <summary>
    /// El código estable del error que se devolvió (<c>ECO-PROHIBIDO</c>, <c>INV-NO-ENCONTRADO</c>…), que el manejador de errores conoce; opcional. Un
    /// texto que no sea un código de esa forma se rechaza.
    /// </summary>
    public string? CodigoDeError
    {
        get;
        init
        {
            if (value is not null && !FormaDelCodigo().IsMatch(value))
            {
                throw new ArgumentException("El código de error es de la forma ECO-… o INV-… (mayúsculas, dígitos y guiones).", nameof(value));
            }

            field = value;
        }
    }

    /// <summary>La ruta cruda de la solicitud (nunca se guarda tal cual).</summary>
    public string? Ruta { get; }

    [GeneratedRegex(@"^[a-z][a-z0-9-]{1,63}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormaDelMotivo();

    [GeneratedRegex(@"^(ECO|INV)-[A-Z0-9][A-Z0-9-]{1,58}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormaDelCodigo();
}

/// <summary>
/// Registra un acceso denegado como <b>un único evento por solicitud</b> (AUD-05), con una correlación que genera el servidor y escrita en su propia
/// transacción (la de la operación ya se deshizo). Lo usan el manejador de errores, el de autorización, los eventos de autenticación y el middleware
/// de credencial de servicio, de modo que un mismo rechazo que pasa por varios no se cuente dos veces. Nunca interrumpe: si no se puede registrar,
/// mantiene el rechazo y deja una alerta técnica (AUD-12).
/// </summary>
public interface IDenegacionDeAcceso
{
    Task Registrar(DenegacionDeAcceso denegacion, CancellationToken cancelacion = default);
}

/// <summary>
/// Los filtros de la consulta de auditoría (AUD-07): acción, usuario, origen, rango de fechas y texto del detalle. Todos son opcionales; la validación
/// (texto de al menos 3 caracteres, comodines escapados, formato del usuario y del origen, tope del rango) es del caso de uso.
/// </summary>
public sealed record FiltroDeAuditoria
{
    public string? Accion { get; init; }

    public Guid? UsuarioId { get; init; }

    public string? Origen { get; init; }

    /// <summary>El inicio del rango como un instante (quien llama convierte la hora de Lima); el servicio lo normaliza a UTC.</summary>
    public DateTimeOffset? Desde { get; init; }

    public DateTimeOffset? Hasta { get; init; }

    /// <summary>Texto que se busca en el detalle: se escapa y no entra al registro de acceso.</summary>
    public string? Texto { get; init; }
}

/// <summary>Una consulta de la lista de auditoría: los filtros y el cursor de la página (nulo en la primera).</summary>
public sealed record ConsultaDeAuditoria(FiltroDeAuditoria Filtro, string? Cursor = null);

/// <summary>
/// La consulta de auditoría y de eventos de seguridad, solo para el administrador central (AUD-27). En el E-commerce entrega la lista unificada
/// (la suya, la de Identidad y la del Inventario, AUD-09, AUD-17); en el Inventario, la suya. Los fallos esperados son <see cref="ErrorDeAplicacion"/>.
/// </summary>
public interface IConsultasDeAuditoria
{
    Task<Resultado<PaginaDeRegistros>> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default);
}

/// <summary>
/// El historial de ajustes y traslados de una sucursal (AUD-08): sin sucursal pedida devuelve el de la del usuario, tomada de su credencial; si pide
/// otra, lo rechaza y lo registra como acceso denegado.
/// </summary>
public interface IHistorialDeSucursal
{
    Task<Resultado<PaginaDeRegistros>> Consultar(Guid? sucursalPedida, string? cursor, CancellationToken cancelacion = default);
}

/// <summary>
/// Las plantillas de ruta de los endpoints del servicio, que la Api obtiene de sus rutas registradas y <b>normaliza</b> a segmentos <c>{nombre}</c>
/// (sin restricciones como <c>{id:guid}</c>, opcionales ni comodines): con ellas se reduce la ruta cruda a su plantilla (AUD-05, AUD-06). Una plantilla
/// que no cumpla la forma se descarta (<c>ResolutorDeRutas</c>) y no impide registrar el evento.
/// </summary>
public interface IPlantillasDeRuta
{
    IReadOnlyCollection<string> Todas { get; }
}
