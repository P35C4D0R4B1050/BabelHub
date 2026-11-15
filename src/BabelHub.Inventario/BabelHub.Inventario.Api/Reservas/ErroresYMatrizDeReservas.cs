using BabelHub.Inventario.Api.Fuentes;
using BabelHub.Inventario.Api.Plataforma;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Inventario;
using BabelHub.Shared.Errores;

namespace BabelHub.Inventario.Api.Reservas;

/// <summary>
/// La traducción de los fallos de <c>Reservas</c> a los códigos <c>INV-…</c> de <c>docs/03/08</c> (D11): los conflictos de estado y de idempotencia, la falta de stock con el detalle por línea, la edición que no es impresa y
/// la <b>respuesta única del límite</b> (429 con <c>Retry-After</c>, SEL-15): la misma para los tres motivos, de modo que solo cambian el motivo, la espera y el instante de nueva disponibilidad, y nunca lleva datos
/// de cuentas ni de otros clientes. Cada código se da por la clave simbólica de la Aplicación, sin tocar el manejo común de la plataforma.
/// </summary>
public static class ErroresDeReservas
{
    /// <summary>Los códigos específicos de las reglas de negocio de Reservas, para <see cref="OpcionesDeErrores"/> (422).</summary>
    public static IReadOnlyDictionary<string, MensajeEspecifico> Especificos { get; } = new Dictionary<string, MensajeEspecifico>
    {
        ["edicion-no-impresa"] = new(CodigosDeErrorDelInventario.EdicionNoImpresa, "La edición no es impresa: no tiene stock físico."),
    };

    /// <summary>Escribe el problema de un fallo de la Aplicación y devuelve un resultado vacío (la respuesta ya se escribió).</summary>
    public static async Task<IResult> Falla(HttpContext contexto, OpcionesDeErrores opciones, ErrorDeAplicacion error)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(opciones);
        ArgumentNullException.ThrowIfNull(error);

        if (error.Categoria == CategoriaDeError.Limite && DetalleDeLimiteDeReservas.TryLeer(error.Errores, out var rechazo))
        {
            await EscribirLimite(contexto, opciones, rechazo!).ConfigureAwait(false);
            return Results.Empty;
        }

        await ProblemasHttp.Escribir(contexto, Problema(contexto, opciones, error)).ConfigureAwait(false);
        return Results.Empty;
    }

    /// <summary>El problema de un fallo de la Aplicación, con el código específico de cada clave de Reservas.</summary>
    public static ProblemaDto Problema(HttpContext contexto, OpcionesDeErrores opciones, ErrorDeAplicacion error)
    {
        var problema = ProblemasHttp.Crear(contexto, opciones, error);

        return error.Clave switch
        {
            DetalleDeFaltaDeStock.ClaveDeStockInsuficiente => StockInsuficiente(problema, error),
            "reserva-estado-invalido" => problema with { Code = CodigosDeErrorDelInventario.ReservaEstadoInvalido, Detail = "La reserva no está en un estado que admita la operación." },
            "idempotencia-en-conflicto" => problema with { Code = CodigosDeErrorDelInventario.IdempotenciaEnConflicto, Detail = "La clave de idempotencia ya se usó con otra solicitud." },

            // Otra solicitud del mismo pedido ganó la inserción: la misma clave sigue procesándose y el cliente reintenta.
            "reserva-existente" => problema with { Code = CodigosDeErrorDelInventario.IdempotenciaEnCurso, Detail = "La misma solicitud sigue procesándose." },
            _ => problema,
        };
    }

    private static ProblemaDeStockDto StockInsuficiente(ProblemaDto problema, ErrorDeAplicacion error)
    {
        var lineas = new List<LineaSinStockDto>();
        foreach (var campo in error.Errores)
        {
            if (DetalleDeFaltaDeStock.TryLeer(campo, out var linea) && linea is not null)
            {
                lineas.Add(new LineaSinStockDto(linea.LineaId, linea.EdicionId, linea.Solicitada, linea.DisponibleEnLaRed));
            }
        }

        return new ProblemaDeStockDto
        {
            Type = problema.Type,
            Title = problema.Title,
            Status = problema.Status,
            Detail = "Alguna línea no tiene stock suficiente; no se reservó ninguna.",
            Code = CodigosDeErrorDelInventario.StockInsuficiente,
            TraceId = problema.TraceId,
            Errores = [],
            Lineas = lineas,
        };
    }

    /// <summary>
    /// La respuesta única de un límite de reservas (429 con <c>Retry-After</c> y <see cref="RespuestaDeLimiteDto"/>): el código, el motivo (la clave de la política), la espera y el instante de nueva disponibilidad. Nada
    /// más: la respuesta no distingue entre cuentas que existen y las que no, ni dice cuántas solicitudes quedan.
    /// </summary>
    private static Task EscribirLimite(HttpContext contexto, OpcionesDeErrores opciones, RechazoPorLimite rechazo)
    {
        var espera = Math.Max(1, rechazo.EsperaEnSegundos);
        var problema = new RespuestaDeLimiteDto
        {
            Title = ProblemasHttp.TituloDe(CategoriaDeError.Limite),
            Status = StatusCodes.Status429TooManyRequests,
            Detail = CategoriaDeError.Limite.MensajeGenerico(),
            Code = CodigosDeErrorDelInventario.LimiteExcedido,
            TraceId = contexto.TraceIdentifier,
            Motivo = rechazo.Motivo,
            RetryAfterSeconds = espera,
            DisponibleDesde = rechazo.DisponibleDesde,
        };
        var cabeceras = new Dictionary<string, string> { ["Retry-After"] = espera.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        return ProblemasHttp.Escribir(contexto, problema, cabeceras);
    }
}

/// <summary>La matriz operación → alcance de la credencial de las operaciones de <c>Reservas</c>, tomada del catálogo de contratos v1 (una operación declara un solo alcance, rechazo por defecto).</summary>
public static class MatrizDeReservas
{
    /// <summary>Los nombres de las operaciones que implementa <see cref="EndpointsDeReservas"/>.</summary>
    public static IReadOnlyList<string> Operaciones { get; } =
    [
        "inventario.reservas.consulta-previa", "inventario.reservas.crear", "inventario.reservas.consultar", "inventario.reservas.confirmar", "inventario.reservas.extender",
        "inventario.reservas.liberar", "inventario.reservas.cerrar", "inventario.titulares.consultar-pendientes", "inventario.titulares.baja",
    ];

    public static IEnumerable<(OperacionDeServicio Operacion, AlcanceDeServicio Alcance)> Declaraciones() =>
        Operaciones.Select(nombre =>
        {
            var contrato = OperacionesDelInventario.Todas.Single(o => o.Nombre == nombre);
            return (new OperacionDeServicio(nombre), AlcanceDe(contrato.Alcance));
        });

    private static AlcanceDeServicio AlcanceDe(string alcance) => alcance switch
    {
        AlcancesDeCredencial.OperacionDeUsuario => AlcanceDeServicio.OperacionDeUsuario,
        AlcancesDeCredencial.LecturaDeDisponibilidad => AlcanceDeServicio.LecturaDeDisponibilidad,
        AlcancesDeCredencial.ExpiracionDeReservas => AlcanceDeServicio.ExpiracionDeReservas,
        AlcancesDeCredencial.ConsultaDePendientes => AlcanceDeServicio.ConsultaDePendientes,
        AlcancesDeCredencial.BajaDeTitular => AlcanceDeServicio.BajaDeTitular,
        _ => throw new ArgumentOutOfRangeException(nameof(alcance), "Una operación de Reservas usa un alcance que este módulo no conoce."),
    };
}
