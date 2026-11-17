using System.Globalization;
using BabelHub.Inventario.Api.Fuentes;
using BabelHub.Inventario.Api.Plataforma;
using BabelHub.Inventario.Application.Ajustes;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Inventario;
using BabelHub.Shared.Errores;

namespace BabelHub.Inventario.Api.Ajustes;

/// <summary>
/// La traducción de los fallos de <c>Ajustes</c> a los códigos <c>INV-…</c> de <c>docs/03/08</c> (D11): la falta de vendible con el físico, el reservado y el vendible, el conteo menor que las reservas con los pedidos, el conteo sin
/// cambios, el ingreso fuera de la recepción, los conflictos de idempotencia y la respuesta única del presupuesto del personal (429 con <c>Retry-After</c>). Ningún error devuelve la nota ni la referencia (AUD-06).
/// </summary>
public static class ErroresDeAjustes
{
    /// <summary>Los códigos específicos de las reglas de negocio de Ajustes, para <see cref="OpcionesDeErrores"/> (422).</summary>
    public static IReadOnlyDictionary<string, MensajeEspecifico> Especificos { get; } = new Dictionary<string, MensajeEspecifico>
    {
        ["edicion-no-impresa"] = new(CodigosDeErrorDelInventario.EdicionNoImpresa, "La edición no es impresa: no tiene stock físico."),
        ["sin-cambios"] = new(CodigosDeErrorDelInventario.SinCambios, "El conteo es igual al físico registrado: no hay nada que ajustar."),
        ["fuente-no-es-de-recepcion"] = new(CodigosDeErrorDelInventario.FuenteNoEsDeRecepcion, "El ingreso de mercadería solo entra en la fuente de recepción."),
        ["conteo-menor-que-reservas"] = new(CodigosDeErrorDelInventario.ConteoMenorQueReservas, "El conteo es menor que las unidades reservadas."),
    };

    public static async Task<IResult> Falla(HttpContext contexto, OpcionesDeErrores opciones, ErrorDeAplicacion error)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(opciones);
        ArgumentNullException.ThrowIfNull(error);

        if (error.Categoria == CategoriaDeError.Limite && DetalleDeLimiteDeAjustes.TryLeer(error.Errores, out var rechazo))
        {
            var espera = Math.Max(1, rechazo!.EsperaEnSegundos);
            var limite = new RespuestaDeLimiteDto
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
            await ProblemasHttp.Escribir(contexto, limite, new Dictionary<string, string> { ["Retry-After"] = espera.ToString(CultureInfo.InvariantCulture) }).ConfigureAwait(false);
            return Results.Empty;
        }

        await ProblemasHttp.Escribir(contexto, Problema(contexto, opciones, error)).ConfigureAwait(false);
        return Results.Empty;
    }

    public static ProblemaDto Problema(HttpContext contexto, OpcionesDeErrores opciones, ErrorDeAplicacion error)
    {
        var problema = ProblemasHttp.Crear(contexto, opciones, error);

        return error.Clave switch
        {
            "stock-insuficiente" => StockInsuficiente(problema, error),
            "conteo-menor-que-reservas" => ConteoMenorQueReservas(problema, error),
            "idempotencia-en-conflicto" => problema with { Code = CodigosDeErrorDelInventario.IdempotenciaEnConflicto, Detail = "La clave de idempotencia ya se usó con otra solicitud." },
            "idempotencia-en-curso" => problema with { Code = CodigosDeErrorDelInventario.IdempotenciaEnCurso, Detail = "La misma solicitud sigue procesándose." },
            _ => problema,
        };
    }

    private static ProblemaDto StockInsuficiente(ProblemaDto problema, ErrorDeAplicacion error)
    {
        // El detalle son tres números de la fila (físico, reservado y vendible), sin datos de personas.
        var detalle = DetalleDeStockInsuficienteDeAjuste.TryLeer(error.Errores, out var d) ? d : default;
        return problema with
        {
            Code = CodigosDeErrorDelInventario.StockInsuficiente,
            Detail = "La cantidad supera el stock vendible: lo reservado de pedidos en curso no se descuenta.",
            Errores =
            [
                new ErrorDeCampoDto("fisico", detalle.Fisico.ToString(CultureInfo.InvariantCulture)),
                new ErrorDeCampoDto("reservado", detalle.Reservado.ToString(CultureInfo.InvariantCulture)),
                new ErrorDeCampoDto("vendible", detalle.Vendible.ToString(CultureInfo.InvariantCulture)),
            ],
        };
    }

    private static ProblemaDeConteoDto ConteoMenorQueReservas(ProblemaDto problema, ErrorDeAplicacion error)
    {
        DetalleDeConteoMenorQueReservas.TryLeer(error.Errores, out var pedidos, out _);
        return new ProblemaDeConteoDto
        {
            Type = problema.Type,
            Title = problema.Title,
            Status = problema.Status,
            Detail = "El conteo es menor que las unidades reservadas; se indican los pedidos que las reservan.",
            Code = CodigosDeErrorDelInventario.ConteoMenorQueReservas,
            TraceId = problema.TraceId,
            Errores = [],
            PedidosQueReservan = pedidos,
        };
    }
}

/// <summary>
/// La matriz operación → alcance de la credencial de las operaciones de <c>Ajustes</c> (una operación declara un solo alcance, rechazo por defecto): todas son <c>operacion-de-usuario</c>, con el token del usuario (rol y sucursal).
/// El historial de ajustes de una fuente (AJU-17) <b>no tiene operación en ECO-0</b> (que solo define <c>GET /historial</c> de la auditoría): se declara aquí como supuesto de contrato hasta que ECO-0 la defina.
/// </summary>
public static class MatrizDeAjustes
{
    /// <summary>La operación propia de este módulo que ECO-0 todavía no define: el historial de ajustes de una fuente.</summary>
    public const string OperacionDelHistorial = "inventario.ajustes.historial";

    public static IReadOnlyList<string> Operaciones { get; } =
        ["inventario.ajustes.registrar", "inventario.ingresos.registrar", "inventario.ingresos.listar", OperacionDelHistorial];

    public static IEnumerable<(OperacionDeServicio Operacion, AlcanceDeServicio Alcance)> Declaraciones() =>
        Operaciones.Select(nombre => (new OperacionDeServicio(nombre), AlcanceDeServicio.OperacionDeUsuario));
}

/// <summary>
/// Los endpoints del módulo <c>Ajustes</c> (T14), bajo <c>/api/inventario/v1</c> y con los nombres de operación y los DTOs de ECO-0. No contienen reglas: traducen HTTP, leen el cuerpo con <b>esquema estricto</b>, leen la
/// <c>Idempotency-Key</c>, llaman al caso de uso por su contrato y traducen su <see cref="Resultado{T}"/> a <c>application/problem+json</c> (<see cref="ErroresDeAjustes"/>). El actor sale del token verificado: nunca del cuerpo.
/// Una solicitud mal formada llega a la Aplicación como una entrada que su validación rechaza, de modo que quien no tiene alcance recibe su 403 y solo quien lo tiene recibe el 400 (alcance primero).
/// </summary>
public static class EndpointsDeAjustes
{
    /// <summary>Un ajuste o un ingreso caben de sobra en 4 KB (la nota admite 200 caracteres).</summary>
    private const int TopeDelCuerpo = 4 * 1024;

    private const int LimitePorDefectoDeIngresos = 50;

    private const int TamanoPorDefecto = 20;

    public static IEndpointRouteBuilder MapearAjustes(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);
        var api = rutas.MapGroup(RutasDeServicio.Inventario);
        api.WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(TopeDelCuerpo));

        // La auditoría caída no deja pasar la operación (AUD-04, la transacción ya revirtió): «servicio no disponible».
        api.AddEndpointFilter(async (contexto, siguiente) =>
        {
            try
            {
                return await siguiente(contexto).ConfigureAwait(false);
            }
            catch (AuditoriaNoDisponibleException)
            {
                var http = contexto.HttpContext;
                return await ErroresDeAjustes.Falla(http, http.RequestServices.GetRequiredService<OpcionesDeErrores>(), new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "auditoria-no-disponible")).ConfigureAwait(false);
            }
        });

        api.MapPost("/ajustes", Registrar).Operacion("inventario.ajustes.registrar");
        api.MapPost("/ingresos-de-mercaderia", RegistrarIngreso).Operacion("inventario.ingresos.registrar");
        api.MapGet("/ingresos-de-mercaderia", ListarIngresos).Operacion("inventario.ingresos.listar");
        api.MapGet("/ajustes", Historial).Operacion(MatrizDeAjustes.OperacionDelHistorial);
        return rutas;
    }

    private static RouteHandlerBuilder Operacion(this RouteHandlerBuilder ruta, string nombre) => ruta.WithMetadata(new OperacionDeServicioAttribute(nombre));

    private static readonly RegistrarAjusteManualEntrada EntradaInvalidaDeAjuste = new(string.Empty, Guid.Empty, string.Empty, null, null, null, string.Empty);

    private static readonly RegistrarIngresoDeMercaderiaEntrada EntradaInvalidaDeIngreso = new(Guid.Empty, null, 0, null, null, string.Empty);

    private static async Task<IResult> Registrar(HttpContext c, OpcionesDeErrores e, ICasoDeUso<RegistrarAjusteManualEntrada, AjusteRegistrado> caso)
    {
        var (cuerpo, clave) = await CuerpoEstricto.Leer<RegistrarAjusteDto>(c).ConfigureAwait(false);
        if (cuerpo is null)
        {
            return await AutorizarAntes(caso, EntradaInvalidaDeAjuste, c, e, "cuerpo", clave!).ConfigureAwait(false);
        }

        var resultado = await caso.Ejecutar(new RegistrarAjusteManualEntrada(cuerpo.CodigoDeFuente, cuerpo.EdicionId, cuerpo.Motivo, cuerpo.Cantidad, cuerpo.ConteoReal, cuerpo.Nota, ClaveDeIdempotencia(c)), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(AAjuste(resultado.Valor), statusCode: StatusCodes.Status201Created)
            : await ErroresDeAjustes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> RegistrarIngreso(HttpContext c, OpcionesDeErrores e, ICasoDeUso<RegistrarIngresoDeMercaderiaEntrada, IngresoRegistrado> caso)
    {
        var (cuerpo, clave) = await CuerpoEstricto.Leer<RegistrarIngresoDto>(c).ConfigureAwait(false);
        if (cuerpo is null)
        {
            return await AutorizarAntes(caso, EntradaInvalidaDeIngreso, c, e, "cuerpo", clave!).ConfigureAwait(false);
        }

        var resultado = await caso.Ejecutar(new RegistrarIngresoDeMercaderiaEntrada(cuerpo.EdicionId, cuerpo.CodigoDeFuente, cuerpo.Cantidad, cuerpo.Referencia, cuerpo.Nota, ClaveDeIdempotencia(c)), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(
                new IngresoDeMercaderiaDto(resultado.Valor.IngresoId, resultado.Valor.EdicionId, resultado.Valor.CodigoDeFuente, resultado.Valor.Cantidad, resultado.Valor.Referencia, resultado.Valor.Nota, resultado.Valor.UsuarioId, resultado.Valor.Instante),
                statusCode: StatusCodes.Status201Created)
            : await ErroresDeAjustes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> ListarIngresos(HttpContext c, OpcionesDeErrores e, ICasoDeUso<ConsultarIngresosEntrada, IngresosConsultados> caso)
    {
        var consulta = c.Request.Query;
        if (!Entero(consulta["limite"], LimitePorDefectoDeIngresos, out var limite))
        {
            return await AutorizarAntes(caso, new ConsultarIngresosEntrada(null, 0), c, e, "limite", "no-es-numero").ConfigureAwait(false);
        }

        Guid? edicion = null;
        if (consulta.TryGetValue("edicionId", out var texto) && texto.Count > 0)
        {
            if (texto.Count != 1 || !Guid.TryParse(texto[0], out var id))
            {
                return await AutorizarAntes(caso, new ConsultarIngresosEntrada(null, 0), c, e, "edicionId", "formato-invalido").ConfigureAwait(false);
            }

            edicion = id;
        }

        var resultado = await caso.Ejecutar(new ConsultarIngresosEntrada(edicion, limite), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(new ListaDeIngresosDto([.. resultado.Valor.Ingresos.Select(i => new IngresoDeMercaderiaDto(i.IngresoId, i.EdicionId, i.CodigoDeFuente, i.Cantidad, i.Referencia, i.Nota, i.UsuarioId, i.Instante))]))
            : await ErroresDeAjustes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> Historial(HttpContext c, OpcionesDeErrores e, ICasoDeUso<ConsultarHistorialDeAjustesEntrada, PaginaDeAjustes> caso)
    {
        var consulta = c.Request.Query;
        var codigos = consulta["codigoDeFuente"];
        var invalida = new ConsultarHistorialDeAjustesEntrada(string.Empty, 0, 0);
        if (codigos.Count != 1 || !Entero(consulta["pagina"], 1, out var pagina))
        {
            return await AutorizarAntes(caso, invalida, c, e, codigos.Count != 1 ? "codigoDeFuente" : "pagina", codigos.Count != 1 ? "obligatorio" : "no-es-numero").ConfigureAwait(false);
        }

        if (!Entero(consulta["tamano"], TamanoPorDefecto, out var tamano))
        {
            return await AutorizarAntes(caso, invalida, c, e, "tamano", "no-es-numero").ConfigureAwait(false);
        }

        var resultado = await caso.Ejecutar(new ConsultarHistorialDeAjustesEntrada(codigos[0]!, pagina, tamano), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(new PaginaDto<AjusteRegistradoDto>([.. resultado.Valor.Elementos.Select(AAjuste)], resultado.Valor.Pagina, resultado.Valor.Tamano, resultado.Valor.Total))
            : await ErroresDeAjustes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> AutorizarAntes<TEntrada, TSalida>(ICasoDeUso<TEntrada, TSalida> caso, TEntrada entradaInvalida, HttpContext c, OpcionesDeErrores e, string campo, string clave)
    {
        var resultado = await caso.Ejecutar(entradaInvalida, c.RequestAborted).ConfigureAwait(false);
        return resultado.Error is { Categoria: CategoriaDeError.Prohibido or CategoriaDeError.NoAutenticado }
            ? await ErroresDeAjustes.Falla(c, e, resultado.Error).ConfigureAwait(false)
            : await ErroresDeFuentes.Validacion(c, e, campo, clave).ConfigureAwait(false);
    }

    private static string ClaveDeIdempotencia(HttpContext c) =>
        c.Request.Headers.TryGetValue(CabecerasDeServicio.ClaveDeIdempotencia, out var valores) && valores.Count == 1 ? valores[0] ?? string.Empty : string.Empty;

    private static bool Entero(Microsoft.Extensions.Primitives.StringValues valores, int porDefecto, out int valor)
    {
        valor = porDefecto;
        return valores.Count == 0 || (valores.Count == 1 && int.TryParse(valores[0], NumberStyles.None, CultureInfo.InvariantCulture, out valor));
    }

    private static AjusteRegistradoDto AAjuste(AjusteRegistrado a) =>
        new(a.AjusteId, a.CodigoDeFuente, a.EdicionId, a.Motivo, a.UsuarioId, a.Instante, a.FisicoAnterior, a.FisicoPosterior, a.Nota, a.Referencia);
}
