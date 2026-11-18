using System.Globalization;
using BabelHub.Inventario.Api.Fuentes;
using BabelHub.Inventario.Api.Plataforma;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Inventario;
using BabelHub.Shared.Errores;

namespace BabelHub.Inventario.Api.Traslados;

/// <summary>
/// La traducción de los fallos de <c>Traslados</c> a los códigos <c>INV-…</c> de <c>docs/03/08</c> (D11): la falta de stock vendible en el origen o de reserva confirmada (409, sin cifras), la transición no permitida desde el estado actual
/// (409), el rechazo o la cancelación manual de un traslado vinculado (422, con la alternativa de anular el pedido y <b>sin evento de seguridad</b>), la edición digital (422) y la respuesta única del presupuesto del personal (429 con
/// <c>Retry-After</c>). Ningún error devuelve el motivo, el motivo de cierre ni la guía (D07).
/// </summary>
public static class ErroresDeTraslados
{
    /// <summary>Los códigos específicos de las reglas de negocio de Traslados, para <see cref="OpcionesDeErrores"/> (422). <c>edicion-no-impresa</c> ya lo declara <c>Reservas</c>: la colisión de claves rompería la composición.</summary>
    public static IReadOnlyDictionary<string, MensajeEspecifico> Especificos { get; } = new Dictionary<string, MensajeEspecifico>
    {
        ["traslado-vinculado-no-se-rechaza"] = new(CodigosDeErrorDelInventario.TrasladoVinculadoNoSeRechaza, "Un traslado vinculado a un pedido solo puede aprobarse; para detenerlo, anule el pedido."),
    };

    public static async Task<IResult> Falla(HttpContext contexto, OpcionesDeErrores opciones, ErrorDeAplicacion error)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(opciones);
        ArgumentNullException.ThrowIfNull(error);

        if (error.Categoria == CategoriaDeError.Limite && DetalleDeLimiteDeTraslados.TryLeer(error.Errores, out var rechazo))
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
            "stock-insuficiente" => problema with { Code = CodigosDeErrorDelInventario.StockInsuficiente, Detail = "El origen no tiene stock vendible suficiente, o el pedido ya no tiene la reserva confirmada." },
            "traslado-estado-invalido" => problema with { Code = CodigosDeErrorDelInventario.TrasladoEstadoInvalido, Detail = "El traslado no está en un estado que admita la operación." },
            _ => problema,
        };
    }
}

/// <summary>
/// La matriz operación → alcance de la credencial de las operaciones de <c>Traslados</c> (una operación declara un solo alcance, rechazo por defecto): todas son <c>operacion-de-usuario</c>, con el token del usuario (rol y sucursal). Dos
/// operaciones <b>no tienen entrada en ECO-0</b> y se declaran aquí como supuesto de contrato hasta que ECO-0 las defina: los orígenes posibles de una edición y los traslados de un pedido.
/// </summary>
public static class MatrizDeTraslados
{
    public const string OperacionDeOrigenesPosibles = "inventario.traslados.origenes-posibles";

    public const string OperacionDeTrasladosDeUnPedido = "inventario.traslados.de-un-pedido";

    public static IReadOnlyList<string> Operaciones { get; } =
    [
        "inventario.traslados.solicitar", OperacionDeOrigenesPosibles, "inventario.traslados.aprobar", "inventario.traslados.rechazar", "inventario.traslados.cancelar", "inventario.traslados.despachar",
        "inventario.traslados.recibir", "inventario.traslados.registrar-guia", "inventario.traslados.consultar", "inventario.traslados.listar", OperacionDeTrasladosDeUnPedido,
    ];

    public static IEnumerable<(OperacionDeServicio Operacion, AlcanceDeServicio Alcance)> Declaraciones() =>
        Operaciones.Select(nombre => (new OperacionDeServicio(nombre), AlcanceDeServicio.OperacionDeUsuario));
}

/// <summary>
/// Los endpoints del módulo <c>Traslados</c> (T23), bajo <c>/api/inventario/v1</c> y con los nombres de operación y los DTOs de ECO-0. No contienen reglas: traducen HTTP, leen el cuerpo con <b>esquema estricto</b>, llaman al caso de uso
/// por su contrato y traducen su <see cref="Resultado{T}"/> a <c>application/problem+json</c> (<see cref="ErroresDeTraslados"/>). El actor sale del token verificado: nunca del cuerpo. Una solicitud mal formada llega a la Aplicación como
/// una entrada que su validación rechaza, de modo que quien no tiene alcance recibe su 403 y solo quien lo tiene recibe el 400 (alcance primero). <b>Ningún endpoint alcanza</b> a <c>GenerarTrasladosDePedido</c>, a
/// <c>CancelarTrasladosDePedido</c> ni a la entrega, la revisión o la depuración: son puertos de <c>Reservas</c> y trabajos de fondo.
/// </summary>
public static class EndpointsDeTraslados
{
    /// <summary>Un traslado, un motivo o una guía caben de sobra en 4 KB (los textos admiten 200 caracteres).</summary>
    private const int TopeDelCuerpo = 4 * 1024;

    private const int TamanoPorDefecto = 20;

    public static IEndpointRouteBuilder MapearTraslados(this IEndpointRouteBuilder rutas)
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
                return await ErroresDeTraslados.Falla(http, http.RequestServices.GetRequiredService<OpcionesDeErrores>(), new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "auditoria-no-disponible")).ConfigureAwait(false);
            }
        });

        api.MapPost("/traslados", Solicitar).Operacion("inventario.traslados.solicitar");
        api.MapGet("/ediciones/{edicionId:guid}/origenes-posibles", OrigenesPosibles).Operacion(MatrizDeTraslados.OperacionDeOrigenesPosibles);
        api.MapPost("/traslados/{trasladoId:guid}/aprobar", Aprobar).Operacion("inventario.traslados.aprobar");
        api.MapPost("/traslados/{trasladoId:guid}/rechazar", Rechazar).Operacion("inventario.traslados.rechazar");
        api.MapPost("/traslados/{trasladoId:guid}/cancelar", Cancelar).Operacion("inventario.traslados.cancelar");
        api.MapPost("/traslados/{trasladoId:guid}/despachar", Despachar).Operacion("inventario.traslados.despachar");
        api.MapPost("/traslados/{trasladoId:guid}/recibir", Recibir).Operacion("inventario.traslados.recibir");
        api.MapPut("/traslados/{trasladoId:guid}/guia", RegistrarGuia).Operacion("inventario.traslados.registrar-guia");
        api.MapGet("/traslados/{trasladoId:guid}", Consultar).Operacion("inventario.traslados.consultar");
        api.MapGet("/traslados", Listar).Operacion("inventario.traslados.listar");
        api.MapGet("/pedidos/{pedidoId:guid}/traslados", DeUnPedido).Operacion(MatrizDeTraslados.OperacionDeTrasladosDeUnPedido);
        return rutas;
    }

    private static RouteHandlerBuilder Operacion(this RouteHandlerBuilder ruta, string nombre) => ruta.WithMetadata(new OperacionDeServicioAttribute(nombre));

    private static async Task<IResult> Solicitar(HttpContext c, OpcionesDeErrores e, ICasoDeUso<SolicitarTrasladoEntrada, TrasladoRegistrado> caso)
    {
        var (cuerpo, clave) = await CuerpoEstricto.Leer<SolicitarTrasladoDto>(c).ConfigureAwait(false);
        if (cuerpo is null)
        {
            return await AutorizarAntes(caso, new SolicitarTrasladoEntrada(Guid.Empty, 0, string.Empty, string.Empty, null), c, e, "cuerpo", clave!).ConfigureAwait(false);
        }

        var resultado = await caso.Ejecutar(new SolicitarTrasladoEntrada(cuerpo.EdicionId, cuerpo.Cantidad, cuerpo.CodigoDeOrigen, cuerpo.CodigoDeDestino, cuerpo.Motivo), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? Results.Json(ATraslado(resultado.Valor), statusCode: StatusCodes.Status201Created) : await ErroresDeTraslados.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> OrigenesPosibles(Guid edicionId, HttpContext c, OpcionesDeErrores e, ICasoDeUso<ConsultarOrigenesPosiblesEntrada, OrigenesPosibles> caso)
    {
        var destinos = c.Request.Query["codigoDeDestino"];
        if (destinos.Count != 1)
        {
            return await AutorizarAntes(caso, new ConsultarOrigenesPosiblesEntrada(Guid.Empty, string.Empty), c, e, "codigoDeDestino", "obligatorio").ConfigureAwait(false);
        }

        var resultado = await caso.Ejecutar(new ConsultarOrigenesPosiblesEntrada(edicionId, destinos[0]!), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(new OrigenesPosiblesDto([.. resultado.Valor.Origenes.Select(o => new OrigenPosibleDto(o.Codigo, o.Nombre))]))
            : await ErroresDeTraslados.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static Task<IResult> Aprobar(Guid trasladoId, HttpContext c, OpcionesDeErrores e, ICasoDeUso<AprobarTrasladoEntrada, TrasladoRegistrado> caso) =>
        Transicion(c, e, caso, new AprobarTrasladoEntrada(trasladoId));

    private static Task<IResult> Despachar(Guid trasladoId, HttpContext c, OpcionesDeErrores e, ICasoDeUso<DespacharTrasladoEntrada, TrasladoRegistrado> caso) =>
        Transicion(c, e, caso, new DespacharTrasladoEntrada(trasladoId));

    private static Task<IResult> Recibir(Guid trasladoId, HttpContext c, OpcionesDeErrores e, ICasoDeUso<RecibirTrasladoEntrada, TrasladoRegistrado> caso) =>
        Transicion(c, e, caso, new RecibirTrasladoEntrada(trasladoId));

    private static async Task<IResult> Rechazar(Guid trasladoId, HttpContext c, OpcionesDeErrores e, ICasoDeUso<RechazarTrasladoEntrada, TrasladoRegistrado> caso)
    {
        var (cuerpo, clave) = await CuerpoEstricto.Leer<MotivoDeTrasladoDto>(c).ConfigureAwait(false);
        return cuerpo is null
            ? await AutorizarAntes(caso, new RechazarTrasladoEntrada(Guid.Empty, string.Empty), c, e, "cuerpo", clave!).ConfigureAwait(false)
            : await Transicion(c, e, caso, new RechazarTrasladoEntrada(trasladoId, cuerpo.Motivo)).ConfigureAwait(false);
    }

    private static async Task<IResult> Cancelar(Guid trasladoId, HttpContext c, OpcionesDeErrores e, ICasoDeUso<CancelarTrasladoEntrada, TrasladoRegistrado> caso)
    {
        var (cuerpo, clave) = await CuerpoEstricto.Leer<MotivoDeTrasladoDto>(c).ConfigureAwait(false);
        return cuerpo is null
            ? await AutorizarAntes(caso, new CancelarTrasladoEntrada(Guid.Empty, string.Empty), c, e, "cuerpo", clave!).ConfigureAwait(false)
            : await Transicion(c, e, caso, new CancelarTrasladoEntrada(trasladoId, cuerpo.Motivo)).ConfigureAwait(false);
    }

    private static async Task<IResult> RegistrarGuia(Guid trasladoId, HttpContext c, OpcionesDeErrores e, ICasoDeUso<RegistrarGuiaDeTrasladoEntrada, TrasladoRegistrado> caso)
    {
        var (cuerpo, clave) = await CuerpoEstricto.Leer<RegistrarGuiaDto>(c).ConfigureAwait(false);
        return cuerpo is null
            ? await AutorizarAntes(caso, new RegistrarGuiaDeTrasladoEntrada(Guid.Empty, string.Empty), c, e, "cuerpo", clave!).ConfigureAwait(false)
            : await Transicion(c, e, caso, new RegistrarGuiaDeTrasladoEntrada(trasladoId, cuerpo.Guia)).ConfigureAwait(false);
    }

    private static async Task<IResult> Consultar(Guid trasladoId, HttpContext c, OpcionesDeErrores e, ICasoDeUso<ConsultarTrasladoEntrada, TrasladoRegistrado> caso)
    {
        var resultado = await caso.Ejecutar(new ConsultarTrasladoEntrada(trasladoId), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? Results.Json(ATraslado(resultado.Valor)) : await ErroresDeTraslados.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> Listar(HttpContext c, OpcionesDeErrores e, ICasoDeUso<ListarTrasladosEntrada, PaginaDeTraslados> caso)
    {
        var consulta = c.Request.Query;
        var invalida = new ListarTrasladosEntrada(null, null, null, null, 0, 0);

        if (!Texto(consulta["estado"], out var estado))
        {
            return await AutorizarAntes(caso, invalida, c, e, "estado", "formato-invalido").ConfigureAwait(false);
        }

        if (!Texto(consulta["codigoDeFuente"], out var fuente))
        {
            return await AutorizarAntes(caso, invalida, c, e, "codigoDeFuente", "formato-invalido").ConfigureAwait(false);
        }

        if (!Entero(consulta["pagina"], 1, out var pagina))
        {
            return await AutorizarAntes(caso, invalida, c, e, "pagina", "no-es-numero").ConfigureAwait(false);
        }

        if (!Entero(consulta["tamano"], TamanoPorDefecto, out var tamano))
        {
            return await AutorizarAntes(caso, invalida, c, e, "tamano", "no-es-numero").ConfigureAwait(false);
        }

        bool? vinculado = null;
        var textoVinculado = consulta["vinculado"];
        if (textoVinculado.Count > 0)
        {
            if (textoVinculado.Count != 1 || !bool.TryParse(textoVinculado[0], out var valor))
            {
                return await AutorizarAntes(caso, invalida, c, e, "vinculado", "formato-invalido").ConfigureAwait(false);
            }

            vinculado = valor;
        }

        int? demora = null;
        if (consulta["demoraMinimaHoras"].Count > 0)
        {
            if (!Entero(consulta["demoraMinimaHoras"], 0, out var horas))
            {
                return await AutorizarAntes(caso, invalida, c, e, "demoraMinimaHoras", "no-es-numero").ConfigureAwait(false);
            }

            demora = horas;
        }

        var resultado = await caso.Ejecutar(new ListarTrasladosEntrada(estado, fuente, vinculado, demora, pagina, tamano), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(new PaginaDto<TrasladoDto>([.. resultado.Valor.Elementos.Select(ATraslado)], resultado.Valor.Pagina, resultado.Valor.Tamano, resultado.Valor.Total))
            : await ErroresDeTraslados.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> DeUnPedido(Guid pedidoId, HttpContext c, OpcionesDeErrores e, ICasoDeUso<ConsultarTrasladosDeUnPedidoEntrada, TrasladosDeUnPedido> caso)
    {
        var resultado = await caso.Ejecutar(new ConsultarTrasladosDeUnPedidoEntrada(pedidoId), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(new TrasladosDeUnPedidoDto([.. resultado.Valor.Traslados.Select(ATraslado)]))
            : await ErroresDeTraslados.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> Transicion<TEntrada>(HttpContext c, OpcionesDeErrores e, ICasoDeUso<TEntrada, TrasladoRegistrado> caso, TEntrada entrada)
    {
        var resultado = await caso.Ejecutar(entrada, c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? Results.Json(ATraslado(resultado.Valor)) : await ErroresDeTraslados.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> AutorizarAntes<TEntrada, TSalida>(ICasoDeUso<TEntrada, TSalida> caso, TEntrada entradaInvalida, HttpContext c, OpcionesDeErrores e, string campo, string clave)
    {
        var resultado = await caso.Ejecutar(entradaInvalida, c.RequestAborted).ConfigureAwait(false);
        return resultado.Error is { Categoria: CategoriaDeError.Prohibido or CategoriaDeError.NoAutenticado }
            ? await ErroresDeTraslados.Falla(c, e, resultado.Error).ConfigureAwait(false)
            : await ErroresDeFuentes.Validacion(c, e, campo, clave).ConfigureAwait(false);
    }

    private static bool Texto(Microsoft.Extensions.Primitives.StringValues valores, out string? valor)
    {
        valor = valores.Count == 1 ? valores[0] : null;
        return valores.Count <= 1;
    }

    private static bool Entero(Microsoft.Extensions.Primitives.StringValues valores, int porDefecto, out int valor)
    {
        valor = porDefecto;
        return valores.Count == 0 || (valores.Count == 1 && int.TryParse(valores[0], NumberStyles.None, CultureInfo.InvariantCulture, out valor));
    }

    private static TrasladoDto ATraslado(TrasladoRegistrado t) =>
        new(t.TrasladoId, t.EdicionId, t.Cantidad, t.CodigoDeOrigen, t.CodigoDeDestino, t.Estado, t.Motivo, t.MotivoDeCierre, t.PedidoId, t.PedidoAnulado, t.Guia, t.SolicitadoEn, t.FechaEstimadaDeLlegada);
}
