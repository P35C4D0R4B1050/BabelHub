using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;
using TipoDeActor = BabelHub.Ecommerce.Application.Plataforma.TipoDeActor;
using AlertaAgrupada = BabelHub.Ecommerce.Domain.Auditoria.AlertaDeSeguridad;

namespace BabelHub.Ecommerce.Application.Auditoria.Interno;

/// <summary>Las alertas del Inventario que la lista unificada mira hacia atrás (T17): las de los últimos 90 días; las más viejas se consultan en el propio servicio.</summary>
public static class HorizonteDeAlertas
{
    public static readonly TimeSpan Dias90 = TimeSpan.FromDays(90);

    /// <summary>El máximo de alertas que una lectura devuelve de una vez (<see cref="IConsultaDeAlertas.LeerNuevas"/>).</summary>
    public const int MaximoPorLectura = 500;
}

internal static class ConversionDeAlertas
{
    public static AlertaConsultada ADelContrato(AlertaAgrupada alerta) =>
        new(
            alerta.Id,
            alerta.Servicio == ServicioDeOrigen.Ecommerce ? ServicioAuditado.Ecommerce : ServicioAuditado.Inventario,
            new CodigoDeAlerta(alerta.Motivo),
            alerta.Clase == ClaseDeAlerta.Seguridad ? ClaseDeAlertaConsultada.Seguridad : ClaseDeAlertaConsultada.Tecnica,
            alerta.PrimeraOcurrencia,
            alerta.UltimaOcurrencia,
            alerta.Ocurrencias,
            alerta.RevisadasHasta)
        {
            RevisadaPor = alerta.RevisadaPor,
            RevisadaEn = alerta.RevisadaEn,
        };

    /// <summary>El orden de la lista: la más reciente primero y, ante igual instante, por identificador (determinista).</summary>
    public static readonly IComparer<AlertaConsultada> Orden = Comparer<AlertaConsultada>.Create((x, y) =>
    {
        var porFecha = y.UltimaOcurrencia.CompareTo(x.UltimaOcurrencia);
        return porFecha != 0 ? porFecha : string.CompareOrdinal(x.Id, y.Id);
    });
}

/// <summary>
/// Las alertas de seguridad del administrador central (AUD-13, AUD-18, AUD-27, AUD-34, AUD-38): la lista fusiona las del E-commerce y las del Inventario (que lee con
/// la credencial de servicio de lectura de alertas) de la más reciente a la más antigua, en páginas de 20 con un cursor firmado (posición de la última
/// entregada); si el Inventario no responde, trae las propias e indica que faltan las suyas (AUD-18). Las técnicas no tienen pantalla. Marcar una alerta
/// como revisada llega hasta el contador visto y <b>solo si se pudo registrar en la auditoría</b> (si no, sigue pendiente, AUD-34); una alerta <c>inv:</c> se reenvía al
/// Inventario con el token del usuario. El rol se revalida en cada llamada; el evento de acceso denegado lo registra la Api con la ruta (AUD-05).
/// </summary>
public sealed class ConsultaDeAlertasUnificada(
    IActorActual actores,
    FirmadorDeCursor firmador,
    IAlmacenDeAlertas almacen,
    IClienteDeAuditoriaDelInventario inventario,
    IAuditoria auditoria,
    IUnidadDeTrabajo unidad,
    IReloj reloj) : IConsultaDeAlertas
{
    private const string HuellaDeLaLista = "alertas-de-seguridad";

    public async Task<Resultado<PaginaDeAlertas>> Listar(string? cursor, CancellationToken cancelacion = default)
    {
        var actor = actores.Actual;
        if (!RolesDeConsultaDeAuditoria.EsAdministradorCentral(actor))
        {
            return Resultado<PaginaDeAlertas>.Fallo(Prohibido("fuera-de-rol"));
        }

        var usuario = actor!.UsuarioId!.Value;
        PosicionDeAlerta? despuesDe = null;
        if (cursor is not null)
        {
            despuesDe = firmador.Abrir<PosicionDeAlerta>(cursor, usuario, HuellaDeLaLista);
            if (despuesDe is null)
            {
                return Resultado<PaginaDeAlertas>.Fallo(ConsultasDeAuditoriaUnificadas.CursorInvalido);
            }
        }

        var candidatas = new List<AlertaConsultada>();
        try
        {
            var propias = await almacen.ListarDeSeguridad(despuesDe, PaginaDeRegistros.TamanoDePagina + 1, cancelacion).ConfigureAwait(false);
            candidatas.AddRange(propias.Select(ConversionDeAlertas.ADelContrato));
        }
        catch (Exception falla) when (falla is not OperationCanceledException)
        {
            return Resultado<PaginaDeAlertas>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "alertas-no-disponibles"));
        }

        var faltan = false;
        var remotas = await inventario.LeerAlertasNuevas(reloj.Ahora - HorizonteDeAlertas.Dias90, cancelacion).ConfigureAwait(false);
        switch (remotas.Estado)
        {
            case EstadoDeRespuestaDelInventario.Disponible:
                candidatas.AddRange(remotas.Valor.Where(a => a.Clase == ClaseDeAlertaConsultada.Seguridad && a.Servicio == ServicioAuditado.Inventario && DespuesDe(a, despuesDe)));
                break;
            case EstadoDeRespuestaDelInventario.NoDisponible:
                faltan = true;
                break;
            default:
                return Resultado<PaginaDeAlertas>.Fallo(Prohibido("inventario-rechazo"));
        }

        candidatas.Sort(ConversionDeAlertas.Orden);
        var pagina = candidatas.Take(PaginaDeRegistros.TamanoDePagina).ToList();
        var hayMas = candidatas.Count > PaginaDeRegistros.TamanoDePagina;
        var siguiente = hayMas ? firmador.Firmar(usuario, HuellaDeLaLista, new PosicionDeAlerta(pagina[^1].UltimaOcurrencia, pagina[^1].Id)) : null;
        return Resultado<PaginaDeAlertas>.Exito(new PaginaDeAlertas(pagina, siguiente, faltan));
    }

    public async Task<Resultado<IReadOnlyList<AlertaConsultada>>> LeerNuevas(DateTimeOffset desde, CancellationToken cancelacion = default)
    {
        var actor = actores.Actual;
        if (!RolesDeConsultaDeAuditoria.EsAdministradorCentral(actor) && actor is not { Tipo: TipoDeActor.Integracion, Integracion: IntegracionesDeAuditoria.LecturaDeAlertas })
        {
            return Resultado<IReadOnlyList<AlertaConsultada>>.Fallo(Prohibido("fuera-de-rol"));
        }

        try
        {
            var alertas = await almacen.Desde(desde.ToUniversalTime(), HorizonteDeAlertas.MaximoPorLectura, cancelacion).ConfigureAwait(false);
            return Resultado<IReadOnlyList<AlertaConsultada>>.Exito([.. alertas.Select(ConversionDeAlertas.ADelContrato)]);
        }
        catch (Exception falla) when (falla is not OperationCanceledException)
        {
            return Resultado<IReadOnlyList<AlertaConsultada>>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "alertas-no-disponibles"));
        }
    }

    public async Task<Resultado<AlertaConsultada>> MarcarRevisada(string alertaId, int contadorVisto, CancellationToken cancelacion = default)
    {
        var actor = actores.Actual;
        if (!RolesDeConsultaDeAuditoria.EsAdministradorCentral(actor))
        {
            return Resultado<AlertaConsultada>.Fallo(Prohibido("fuera-de-rol"));
        }

        if (string.IsNullOrEmpty(alertaId) || contadorVisto < 1)
        {
            return Resultado<AlertaConsultada>.Fallo(Invalido("alerta", "marca-invalida"));
        }

        if (IdentificadorDeAlerta.EsValido(alertaId, "inv:"))
        {
            var remota = await inventario.MarcarAlertaRevisada(alertaId, contadorVisto, cancelacion).ConfigureAwait(false);
            return remota.Estado switch
            {
                EstadoDeRespuestaDelInventario.Disponible => Resultado<AlertaConsultada>.Exito(remota.Valor),
                EstadoDeRespuestaDelInventario.NoDisponible => Resultado<AlertaConsultada>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "inventario-no-disponible")),
                _ => Resultado<AlertaConsultada>.Fallo(Prohibido("inventario-rechazo")),
            };
        }

        if (!IdentificadorDeAlerta.EsValido(alertaId, "eco:"))
        {
            return Resultado<AlertaConsultada>.Fallo(Invalido("alerta", "alerta-invalida"));
        }

        try
        {
            return await unidad.Ejecutar(async ct => await MarcarLocal(alertaId, contadorVisto, actor!.UsuarioId!.Value, ct).ConfigureAwait(false), cancelacion).ConfigureAwait(false);
        }
        catch (AuditoriaNoDisponibleException)
        {
            // La marca no se pudo registrar: la alerta sigue pendiente (AUD-34) y se informa «servicio no disponible».
            return Resultado<AlertaConsultada>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "auditoria-no-disponible"));
        }
        catch (Exception falla) when (falla is not OperationCanceledException and not ArgumentException)
        {
            return Resultado<AlertaConsultada>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "alertas-no-disponibles"));
        }
    }

    private async Task<Resultado<AlertaConsultada>> MarcarLocal(string alertaId, int contadorVisto, Guid usuario, CancellationToken cancelacion)
    {
        var alerta = await almacen.Obtener(alertaId, cancelacion).ConfigureAwait(false);

        // Las técnicas no tienen pantalla ni marca: para quien llama, no existen.
        if (alerta is null || !alerta.TienePantalla)
        {
            return Resultado<AlertaConsultada>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "alerta-no-encontrada"));
        }

        if (contadorVisto > alerta.Ocurrencias)
        {
            return Resultado<AlertaConsultada>.Fallo(Invalido("contadorVisto", "contador-invalido"));
        }

        var marcada = alerta.MarcarRevisada(contadorVisto, usuario, reloj.Ahora);
        if (ReferenceEquals(marcada, alerta))
        {
            return Resultado<AlertaConsultada>.Exito(ConversionDeAlertas.ADelContrato(alerta));
        }

        // Primero la auditoría y después la marca: si no se puede registrar, la alerta no cambia (AUD-34); y la unidad de trabajo revierte ambas.
        await auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.AlertaMarcadaRevisada)
            {
                ObjetoTipo = "alerta",
                ObjetoId = alerta.Id,
                Detalle = new Dictionary<string, string?>
                {
                    ["servicio"] = "ecommerce",
                    ["motivo"] = alerta.Motivo,
                    ["contadorVisto"] = contadorVisto.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
            },
            cancelacion).ConfigureAwait(false);
        await almacen.Guardar(marcada, cancelacion).ConfigureAwait(false);
        return Resultado<AlertaConsultada>.Exito(ConversionDeAlertas.ADelContrato(marcada));
    }

    private static bool DespuesDe(AlertaConsultada alerta, PosicionDeAlerta? posicion) =>
        posicion is null
        || alerta.UltimaOcurrencia < posicion.UltimaOcurrencia
        || (alerta.UltimaOcurrencia == posicion.UltimaOcurrencia && string.CompareOrdinal(alerta.Id, posicion.Id) > 0);

    private static ErrorDeAplicacion Prohibido(string clave) => new(CategoriaDeError.Prohibido, clave);

    private static ErrorDeAplicacion Invalido(string campo, string clave) => new(CategoriaDeError.Validacion, clave, [new ErrorDeCampo(campo, clave)]);
}

/// <summary>
/// El historial de ajustes y traslados de una sucursal (AUD-08, AUD-27): lo guarda y lo entrega el Inventario; el E-commerce lo pide con el token del usuario y
/// la credencial de servicio de alcance <c>historial</c> y no decide el permiso por su cuenta más que lo básico. <b>La sucursal sale de la credencial</b> del encargado:
/// sin sucursal pedida devuelve la suya y, si pide otra, lo rechaza (la Api lo registra como acceso denegado con la ruta, AUD-05). La administración central indica la sucursal que
/// quiere ver. El cursor es el del Inventario (opaco, firmado por él y ligado al usuario).
/// </summary>
public sealed class HistorialDeSucursalUnificado(IActorActual actores, IClienteDeAuditoriaDelInventario inventario) : IHistorialDeSucursal
{
    public async Task<Resultado<PaginaDeRegistros>> Consultar(Guid? sucursalPedida, string? cursor, CancellationToken cancelacion = default)
    {
        var actor = actores.Actual;
        if (actor is not { Tipo: TipoDeActor.Usuario, UsuarioId: not null })
        {
            return Resultado<PaginaDeRegistros>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "fuera-de-rol"));
        }

        Guid sucursal;
        if (actor.Rol == RolesDeConsultaDeAuditoria.EncargadoDeSucursal)
        {
            if (actor.SucursalId is not { } propia)
            {
                return Resultado<PaginaDeRegistros>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "fuera-de-sucursal"));
            }

            if (sucursalPedida is { } pedida && pedida != propia)
            {
                return Resultado<PaginaDeRegistros>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "fuera-de-sucursal"));
            }

            sucursal = propia;
        }
        else if (RolesDeConsultaDeAuditoria.EsAdministradorCentral(actor))
        {
            if (sucursalPedida is not { } pedida)
            {
                return Resultado<PaginaDeRegistros>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, "sucursal-requerida", [new ErrorDeCampo("sucursal", "sucursal-requerida")]));
            }

            sucursal = pedida;
        }
        else
        {
            return Resultado<PaginaDeRegistros>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "fuera-de-rol"));
        }

        if (cursor is { Length: > PaginaDeRegistros.MaximoDeCaracteresDelCursor })
        {
            return Resultado<PaginaDeRegistros>.Fallo(ConsultasDeAuditoriaUnificadas.CursorInvalido);
        }

        var respuesta = await inventario.ConsultarHistorial(sucursal, cursor, cancelacion).ConfigureAwait(false);
        return respuesta.Estado switch
        {
            EstadoDeRespuestaDelInventario.Disponible => Resultado<PaginaDeRegistros>.Exito(respuesta.Valor),
            EstadoDeRespuestaDelInventario.NoDisponible => Resultado<PaginaDeRegistros>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "inventario-no-disponible")),
            _ => Resultado<PaginaDeRegistros>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "inventario-rechazo")),
        };
    }
}
