using BabelHub.Inventario.Api.Fuentes;
using BabelHub.Inventario.Api.Plataforma;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Inventario;

namespace BabelHub.Inventario.Api.Reservas;

/// <summary>
/// Los endpoints del módulo <c>Reservas</c> (T26 y T27), bajo <c>/api/inventario/v1</c> y con los nombres de operación y los DTOs de ECO-0. No contienen reglas: traducen HTTP, leen el cuerpo con <b>esquema estricto</b> (un
/// <c>clienteId</c> o cualquier campo de más se rechaza), leen la <c>Idempotency-Key</c>, llaman al caso de uso por su contrato de la raíz y traducen su <see cref="Resultado{T}"/> a <c>application/problem+json</c> con los
/// códigos <c>INV-…</c> (<see cref="ErroresDeReservas"/>). Cada endpoint declara <b>una operación</b>: la matriz de alcances decide qué credencial la autoriza (rechazo por defecto) y el actor lo arma el servidor —el de
/// integración con el nombre del alcance, o el del usuario del token verificado—, nunca el cuerpo. La Api no decide roles ni sucursales: es la Aplicación, que además comprueba el alcance <b>antes</b> de leer nada.
/// Una solicitud mal formada llega a la Aplicación como una entrada que su validación rechaza, de modo que quien no tiene alcance recibe su 403 y solo quien lo tiene recibe el 400 (alcance primero, AGENTS §5).
/// </summary>
public static class EndpointsDeReservas
{
    /// <summary>Las líneas de un pedido y su cuerpo caben de sobra en 32 KB.</summary>
    private const int TopeDelCuerpo = 32 * 1024;

    public static IEndpointRouteBuilder MapearReservas(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);
        var api = rutas.MapGroup(RutasDeServicio.Inventario);
        api.WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(TopeDelCuerpo));

        // La auditoría caída no deja pasar la operación (AUD-04, la transacción ya revirtió): «servicio no disponible», no un error inesperado.
        api.AddEndpointFilter(async (contexto, siguiente) =>
        {
            try
            {
                return await siguiente(contexto).ConfigureAwait(false);
            }
            catch (AuditoriaNoDisponibleException)
            {
                var http = contexto.HttpContext;
                return await ErroresDeReservas.Falla(http, http.RequestServices.GetRequiredService<OpcionesDeErrores>(), new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "auditoria-no-disponible")).ConfigureAwait(false);
            }
        });

        // --- T26: crear, consulta previa, consultar, confirmar, extender y liberar ---
        api.MapPost("/reservas/consulta-previa", ConsultaPrevia).Operacion("inventario.reservas.consulta-previa");
        api.MapPost("/reservas", Crear).Operacion("inventario.reservas.crear");
        api.MapGet("/reservas/{pedidoId:guid}", Consultar).Operacion("inventario.reservas.consultar");
        api.MapPost("/reservas/{pedidoId:guid}/confirmar", Confirmar).Operacion("inventario.reservas.confirmar");
        api.MapPost("/reservas/{pedidoId:guid}/extender", Extender).Operacion("inventario.reservas.extender");
        api.MapPost("/reservas/{pedidoId:guid}/liberar", Liberar).Operacion("inventario.reservas.liberar");

        // --- T27: cerrar, pendientes y baja del titular ---
        api.MapPost("/reservas/{pedidoId:guid}/cerrar", Cerrar).Operacion("inventario.reservas.cerrar");
        api.MapPost("/titulares/consultas-de-pendientes", Pendientes).Operacion("inventario.titulares.consultar-pendientes");
        api.MapPost("/bajas-de-titular", Baja).Operacion("inventario.titulares.baja");
        return rutas;
    }

    private static RouteHandlerBuilder Operacion(this RouteHandlerBuilder ruta, string nombre) => ruta.WithMetadata(new OperacionDeServicioAttribute(nombre));

    // ---------- T26 ----------

    private static async Task<IResult> ConsultaPrevia(HttpContext c, OpcionesDeErrores e, ICasoDeUso<ConsultarSeleccionEntrada, SeleccionConsultada> caso)
    {
        var (cuerpo, clave) = await CuerpoEstricto.Leer<ConsultaPreviaDto>(c).ConfigureAwait(false);
        if (cuerpo is null)
        {
            return await AutorizarAntes(caso, EntradaInvalidaDeConsultaPrevia, c, e, "cuerpo", clave!).ConfigureAwait(false);
        }

        if (!TipoDeEntrega(cuerpo.TipoDeEntrega, out var tipo))
        {
            return await AutorizarAntes(caso, EntradaInvalidaDeConsultaPrevia, c, e, "tipoDeEntrega", "formato-invalido").ConfigureAwait(false);
        }

        var resultado = await caso.Ejecutar(new ConsultarSeleccionEntrada(tipo, cuerpo.CodigoDeSucursalDeRecojo, cuerpo.RegionDeEntrega, Lineas(cuerpo.Lineas)), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(new ConsultaPreviaRespuestaDto([.. resultado.Valor.Lineas.Select(ASeleccion)], resultado.Valor.ConsultadaEn))
            : await ErroresDeReservas.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> Crear(HttpContext c, OpcionesDeErrores e, ICasoDeUso<ReservarEntrada, ReservaDelPedido> caso)
    {
        var (cuerpo, clave) = await CuerpoEstricto.Leer<CrearReservaDto>(c).ConfigureAwait(false);
        if (cuerpo is null)
        {
            return await AutorizarAntes(caso, EntradaInvalidaDeCrear, c, e, "cuerpo", clave!).ConfigureAwait(false);
        }

        if (!TipoDeEntrega(cuerpo.TipoDeEntrega, out var tipo))
        {
            return await AutorizarAntes(caso, EntradaInvalidaDeCrear, c, e, "tipoDeEntrega", "formato-invalido").ConfigureAwait(false);
        }

        var resultado = await caso.Ejecutar(
            new ReservarEntrada(cuerpo.PedidoId, tipo, cuerpo.CodigoDeSucursalDeRecojo, cuerpo.RegionDeEntrega, Lineas(cuerpo.Lineas), ClaveDeIdempotencia(c)),
            c.RequestAborted).ConfigureAwait(false);

        // Repetir con la misma clave devuelve la respuesta original con el mismo estado (la repetición no cambia nada).
        return resultado.EsExito
            ? Results.Json(AReserva(resultado.Valor), statusCode: StatusCodes.Status201Created)
            : await ErroresDeReservas.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> Consultar(Guid pedidoId, HttpContext c, OpcionesDeErrores e, ICasoDeUso<ConsultarReservaEntrada, ReservaDelPedido> caso)
    {
        var resultado = await caso.Ejecutar(new ConsultarReservaEntrada(pedidoId), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? Results.Json(AReserva(resultado.Valor)) : await ErroresDeReservas.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> Confirmar(Guid pedidoId, HttpContext c, OpcionesDeErrores e, ICasoDeUso<ConfirmarReservasEntrada, ReservaDelPedido> caso)
    {
        var resultado = await caso.Ejecutar(new ConfirmarReservasEntrada(pedidoId, ClaveDeIdempotencia(c)), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? Results.Json(AReserva(resultado.Valor)) : await ErroresDeReservas.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> Extender(Guid pedidoId, HttpContext c, OpcionesDeErrores e, ICasoDeUso<ExtenderReservasEntrada, ExtensionDeReserva> caso)
    {
        var resultado = await caso.Ejecutar(new ExtenderReservasEntrada(pedidoId), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(new ExtensionDeReservaDto(resultado.Valor.Extendida, AReserva(resultado.Valor.Reserva)))
            : await ErroresDeReservas.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> Liberar(Guid pedidoId, HttpContext c, OpcionesDeErrores e, ICasoDeUso<LiberarReservasEntrada, ReservaDelPedido> caso)
    {
        var (cuerpo, clave) = await CuerpoEstricto.Leer<LiberarReservaDto>(c).ConfigureAwait(false);
        if (cuerpo is null)
        {
            return await AutorizarAntes(caso, EntradaInvalidaDeLiberar, c, e, "cuerpo", clave!).ConfigureAwait(false);
        }

        if (!MotivoDeLiberacion(cuerpo.Motivo, out var motivo))
        {
            return await AutorizarAntes(caso, EntradaInvalidaDeLiberar, c, e, "motivo", "formato-invalido").ConfigureAwait(false);
        }

        var resultado = await caso.Ejecutar(new LiberarReservasEntrada(pedidoId, motivo, cuerpo.LineaIds), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? Results.Json(AReserva(resultado.Valor)) : await ErroresDeReservas.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    // ---------- T27 ----------

    private static async Task<IResult> Cerrar(Guid pedidoId, HttpContext c, OpcionesDeErrores e, ICasoDeUso<CerrarReservasEntrada, CierreDeReserva> caso)
    {
        var (cuerpo, clave) = await CuerpoEstricto.Leer<CerrarReservaDto>(c).ConfigureAwait(false);
        if (cuerpo is null)
        {
            return await AutorizarAntes(caso, EntradaInvalidaDeCerrar, c, e, "cuerpo", clave!).ConfigureAwait(false);
        }

        if (!MotivoDeCierre(cuerpo.Motivo, out var motivo))
        {
            return await AutorizarAntes(caso, EntradaInvalidaDeCerrar, c, e, "motivo", "formato-invalido").ConfigureAwait(false);
        }

        var resultado = await caso.Ejecutar(new CerrarReservasEntrada(pedidoId, motivo, cuerpo.LineaIds ?? [], cuerpo.EnvioId, ClaveDeIdempotencia(c)), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(new CierreDeReservaDto(resultado.Valor.PedidoId, [.. resultado.Valor.Lineas.Select(l => new ResultadoDeCierreDto(l.LineaId, l.YaCerrada ? ResultadosDeCierre.YaCerrada : ResultadosDeCierre.Cerrada))]))
            : await ErroresDeReservas.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> Pendientes(HttpContext c, OpcionesDeErrores e, ICasoDeUso<ConsultarPendientesEntrada, PendientesDeTitular> caso)
    {
        var (cuerpo, clave) = await CuerpoEstricto.Leer<ConsultaDePendientesDto>(c).ConfigureAwait(false);
        if (cuerpo is null)
        {
            return await AutorizarAntes(caso, new ConsultarPendientesEntrada(Guid.Empty), c, e, "cuerpo", clave!).ConfigureAwait(false);
        }

        var resultado = await caso.Ejecutar(new ConsultarPendientesEntrada(cuerpo.TitularId), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(new PendientesDeTitularDto(resultado.Valor.ReservasActivas))
            : await ErroresDeReservas.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> Baja(HttpContext c, OpcionesDeErrores e, ICasoDeUso<ProcesarBajaDeTitularEntrada, BajaDeTitularProcesada> caso)
    {
        var (cuerpo, clave) = await CuerpoEstricto.Leer<BajaDeTitularDto>(c).ConfigureAwait(false);
        if (cuerpo is null)
        {
            return await AutorizarAntes(caso, new ProcesarBajaDeTitularEntrada(Guid.Empty), c, e, "cuerpo", clave!).ConfigureAwait(false);
        }

        // Repetirla responde 204 igual: la baja es idempotente por su propio estado.
        var resultado = await caso.Ejecutar(new ProcesarBajaDeTitularEntrada(cuerpo.TitularId), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? Results.NoContent() : await ErroresDeReservas.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    // ---------- entradas inválidas (alcance primero) ----------

    private static readonly ConsultarSeleccionEntrada EntradaInvalidaDeConsultaPrevia = new(TipoDeEntregaDeReserva.Recojo, null, null, []);

    private static readonly ReservarEntrada EntradaInvalidaDeCrear = new(Guid.Empty, TipoDeEntregaDeReserva.Recojo, null, null, [], string.Empty);

    private static readonly LiberarReservasEntrada EntradaInvalidaDeLiberar = new(Guid.Empty, MotivoDeLiberacionDeReserva.CheckoutCancelado, null);

    private static readonly CerrarReservasEntrada EntradaInvalidaDeCerrar = new(Guid.Empty, MotivoDeCierreDeReserva.Entrega, [], null, string.Empty);

    /// <summary>
    /// Un cuerpo o un valor mal formado no se responde con 400 antes de autorizar: se pasa a la Aplicación una entrada que su validación rechaza, de modo que quien no tiene alcance recibe su 403 con el evento de acceso
    /// denegado y solo quien sí lo tiene recibe el 400 de forma. La entrada inválida nunca llega a leer ni a escribir nada.
    /// </summary>
    private static async Task<IResult> AutorizarAntes<TEntrada, TSalida>(ICasoDeUso<TEntrada, TSalida> caso, TEntrada entradaInvalida, HttpContext c, OpcionesDeErrores e, string campo, string clave)
    {
        var resultado = await caso.Ejecutar(entradaInvalida, c.RequestAborted).ConfigureAwait(false);
        return resultado.Error is { Categoria: CategoriaDeError.Prohibido or CategoriaDeError.NoAutenticado }
            ? await ErroresDeReservas.Falla(c, e, resultado.Error).ConfigureAwait(false)
            : await ErroresDeFuentes.Validacion(c, e, campo, clave).ConfigureAwait(false);
    }

    // ---------- traducciones ----------

    /// <summary>La cabecera <c>Idempotency-Key</c> tal cual llegó (la Aplicación valida su forma, después de autorizar); ausente es vacía.</summary>
    private static string ClaveDeIdempotencia(HttpContext c) =>
        c.Request.Headers.TryGetValue(CabecerasDeServicio.ClaveDeIdempotencia, out var valores) && valores.Count == 1 ? valores[0] ?? string.Empty : string.Empty;

    private static IReadOnlyList<LineaSolicitada> Lineas(IReadOnlyList<LineaDeSolicitudDto>? lineas) =>
        [.. (lineas ?? []).Select(l => new LineaSolicitada(l.LineaId, l.EdicionId, l.Cantidad))];

    private static bool TipoDeEntrega(string? texto, out TipoDeEntregaDeReserva tipo)
    {
        tipo = TipoDeEntregaDeReserva.Recojo;
        switch (texto)
        {
            case TiposDeEntrega.Recojo:
                return true;
            case TiposDeEntrega.Domicilio:
                tipo = TipoDeEntregaDeReserva.Domicilio;
                return true;
            default:
                return false;
        }
    }

    private static bool MotivoDeLiberacion(string? texto, out MotivoDeLiberacionDeReserva motivo)
    {
        MotivoDeLiberacionDeReserva? encontrado = texto switch
        {
            MotivosDeLiberacion.CheckoutCancelado => MotivoDeLiberacionDeReserva.CheckoutCancelado,
            MotivosDeLiberacion.PagoVencido => MotivoDeLiberacionDeReserva.PagoVencido,
            MotivosDeLiberacion.PedidoAnulado => MotivoDeLiberacionDeReserva.PedidoAnulado,
            MotivosDeLiberacion.CanceladoPorElCliente => MotivoDeLiberacionDeReserva.CanceladoPorElCliente,
            MotivosDeLiberacion.FallaPosteriorAlPago => MotivoDeLiberacionDeReserva.FallaPosteriorAlPago,
            MotivosDeLiberacion.NoRecogido => MotivoDeLiberacionDeReserva.NoRecogido,
            _ => null,
        };
        motivo = encontrado ?? MotivoDeLiberacionDeReserva.CheckoutCancelado;
        return encontrado is not null;
    }

    private static bool MotivoDeCierre(string? texto, out MotivoDeCierreDeReserva motivo)
    {
        MotivoDeCierreDeReserva? encontrado = texto switch
        {
            MotivosDeCierre.Entrega => MotivoDeCierreDeReserva.Entrega,
            MotivosDeCierre.Reparto => MotivoDeCierreDeReserva.Reparto,
            _ => null,
        };
        motivo = encontrado ?? MotivoDeCierreDeReserva.Entrega;
        return encontrado is not null;
    }

    private static SeleccionDeLineaDto ASeleccion(SeleccionDeLinea s) =>
        new(s.LineaId, s.EdicionId, s.HayStock ? ResultadosDeSeleccion.Disponible : ResultadosDeSeleccion.SinStock, s.CodigoDeFuente, s.RequiereTraslado, s.PlazoEstimadoEnDias, s.DisponibleEnLaRed);

    private static ReservaDto AReserva(ReservaDelPedido r) =>
        new(
            r.PedidoId,
            EstadoComoTexto(r.Estado),
            r.CreadaEn,
            r.ExpiraEn,
            [.. r.Lineas.Select(l => new LineaDeReservaDto(l.LineaId, l.EdicionId, l.Cantidad, EstadoComoTexto(l.Estado), l.CodigoDeFuente, l.RequiereTraslado, l.PlazoEstimadoEnDias))],
            [.. r.Traslados.Select(t => new TrasladoVinculadoDto(t.TrasladoId, t.EdicionId, t.Cantidad, t.CodigoDeOrigen, t.CodigoDeDestino, t.Estado, t.FechaEstimadaDeLlegada))]);

    private static string EstadoComoTexto(EstadoDeReserva estado) => estado switch
    {
        EstadoDeReserva.Pendiente => EstadosDeReserva.Pendiente,
        EstadoDeReserva.Confirmada => EstadosDeReserva.Confirmada,
        EstadoDeReserva.Cerrada => EstadosDeReserva.Cerrada,
        EstadoDeReserva.Liberada => EstadosDeReserva.Liberada,
        _ => EstadosDeReserva.Expirada,
    };
}
