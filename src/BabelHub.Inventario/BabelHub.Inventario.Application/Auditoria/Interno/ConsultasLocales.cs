using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Auditoria;
using AlertaAgrupada = BabelHub.Inventario.Domain.Auditoria.AlertaDeSeguridad;
using TipoDeActor = BabelHub.Inventario.Application.Plataforma.TipoDeActor;

namespace BabelHub.Inventario.Application.Auditoria.Interno;

/// <summary>
/// Puerto de lectura del historial de ajustes y traslados de una sucursal (AUD-08, AJU-17): los registros de ajuste y de traslado cuya sucursal de origen o de
/// destino es la indicada, con el mismo orden, páginas y cursor de instantánea que la auditoría (T25).
/// </summary>
public interface IHistorialLocal
{
    Task<PaginaDeRegistros> Consultar(Guid sucursal, string? cursor, CancellationToken cancelacion = default);
}

/// <summary>El cursor que el Inventario entrega: el de su almacén envuelto en un sobre firmado, ligado al usuario y a los filtros.</summary>
public sealed record CargaDeCursorLocal(string Cursor);

/// <summary>
/// La auditoría del Inventario para el E-commerce (AUD-07, AUD-17, AUD-27): solo la administración central, con el token del usuario verificable (la credencial de
/// servicio sola no basta, AUD-23), los mismos filtros, orden y páginas de 20 que el E-commerce, y un cursor firmado con la clave del Inventario (la secuencia
/// máxima de apertura vive en el cursor de su almacén). El rol se revalida en cada página. El evento de acceso denegado lo registra la Api con la ruta (AUD-05).
/// </summary>
public sealed class ConsultasDeAuditoriaLocales(
    IActorActual actores,
    CatalogoDeAcciones catalogo,
    FirmadorDeCursor firmador,
    IConsultaLocalDeRegistros local) : IConsultasDeAuditoria
{
    public static readonly ErrorDeAplicacion CursorInvalido = new(CategoriaDeError.Validacion, "cursor-invalido", [new ErrorDeCampo("cursor", "cursor-invalido")]);

    public async Task<Resultado<PaginaDeRegistros>> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        var actor = actores.Actual;
        if (!RolesDeConsultaDeAuditoria.EsAdministradorCentral(actor))
        {
            return Resultado<PaginaDeRegistros>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "fuera-de-rol"));
        }

        var filtro = consulta.Filtro ?? new FiltroDeAuditoria();
        if (ValidacionDeFiltroDeAuditoria.Validar(filtro, catalogo) is { } invalido)
        {
            return Resultado<PaginaDeRegistros>.Fallo(invalido);
        }

        var huella = ValidacionDeFiltroDeAuditoria.Huella(filtro);
        var usuario = actor!.UsuarioId!.Value;
        string? cursorDelAlmacen = null;
        if (consulta.Cursor is not null)
        {
            var carga = firmador.Abrir<CargaDeCursorLocal>(consulta.Cursor, usuario, huella);
            if (carga is null || string.IsNullOrEmpty(carga.Cursor))
            {
                return Resultado<PaginaDeRegistros>.Fallo(CursorInvalido);
            }

            cursorDelAlmacen = carga.Cursor;
        }

        try
        {
            var pagina = await local.Consultar(new ConsultaDeAuditoria(filtro, cursorDelAlmacen), cancelacion).ConfigureAwait(false);
            var siguiente = pagina.SiguienteCursor is null ? null : firmador.Firmar(usuario, huella, new CargaDeCursorLocal(pagina.SiguienteCursor));
            // La página lleva también su propio cursor (el del E-commerce lo necesita para fusionar sin repetir): firmado igual, nunca el crudo del almacén.
            var deLaPagina = pagina.CursorDeLaPagina is null ? null : firmador.Firmar(usuario, huella, new CargaDeCursorLocal(pagina.CursorDeLaPagina));
            return Resultado<PaginaDeRegistros>.Exito(new PaginaDeRegistros(pagina.Registros, siguiente) { CursorDeLaPagina = deLaPagina });
        }
        catch (Exception falla) when (falla is not OperationCanceledException)
        {
            return Resultado<PaginaDeRegistros>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "auditoria-no-disponible"));
        }
    }
}

/// <summary>
/// Las alertas del Inventario (AUD-13, AUD-27, AUD-34, AUD-38): el E-commerce las lee con la credencial de servicio de lectura de alertas y las del propio servicio
/// se marcan con el token de la administración central. No envía correo (el Inventario no lo tiene; las entrega el E-commerce, AUD-29). Marcar llega hasta el contador visto y
/// solo si se pudo registrar en la auditoría.
/// </summary>
public sealed class ConsultaDeAlertasLocales(
    IActorActual actores,
    FirmadorDeCursor firmador,
    IAlmacenDeAlertas almacen,
    IAuditoria auditoria,
    IUnidadDeTrabajo unidad,
    IReloj reloj) : IConsultaDeAlertas
{
    public const int MaximoPorLectura = 500;

    private const string HuellaDeLaLista = "alertas-de-seguridad";

    public async Task<Resultado<PaginaDeAlertas>> Listar(string? cursor, CancellationToken cancelacion = default)
    {
        var actor = actores.Actual;
        if (!RolesDeConsultaDeAuditoria.EsAdministradorCentral(actor))
        {
            return Resultado<PaginaDeAlertas>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "fuera-de-rol"));
        }

        var usuario = actor!.UsuarioId!.Value;
        PosicionDeAlerta? despuesDe = null;
        if (cursor is not null)
        {
            despuesDe = firmador.Abrir<PosicionDeAlerta>(cursor, usuario, HuellaDeLaLista);
            if (despuesDe is null)
            {
                return Resultado<PaginaDeAlertas>.Fallo(ConsultasDeAuditoriaLocales.CursorInvalido);
            }
        }

        try
        {
            var alertas = await almacen.ListarDeSeguridad(despuesDe, PaginaDeRegistros.TamanoDePagina + 1, cancelacion).ConfigureAwait(false);
            var pagina = alertas.Take(PaginaDeRegistros.TamanoDePagina).Select(ADelContrato).ToList();
            var siguiente = alertas.Count > PaginaDeRegistros.TamanoDePagina ? firmador.Firmar(usuario, HuellaDeLaLista, new PosicionDeAlerta(pagina[^1].UltimaOcurrencia, pagina[^1].Id)) : null;
            return Resultado<PaginaDeAlertas>.Exito(new PaginaDeAlertas(pagina, siguiente));
        }
        catch (Exception falla) when (falla is not OperationCanceledException)
        {
            return Resultado<PaginaDeAlertas>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "alertas-no-disponibles"));
        }
    }

    public async Task<Resultado<IReadOnlyList<AlertaConsultada>>> LeerNuevas(DateTimeOffset desde, CancellationToken cancelacion = default)
    {
        var actor = actores.Actual;
        if (!RolesDeConsultaDeAuditoria.EsAdministradorCentral(actor) && actor is not { Tipo: TipoDeActor.Integracion, Integracion: IntegracionesDeAuditoria.LecturaDeAlertas })
        {
            return Resultado<IReadOnlyList<AlertaConsultada>>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "fuera-de-rol"));
        }

        try
        {
            var alertas = await almacen.Desde(desde.ToUniversalTime(), MaximoPorLectura, cancelacion).ConfigureAwait(false);
            return Resultado<IReadOnlyList<AlertaConsultada>>.Exito([.. alertas.Select(ADelContrato)]);
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
            return Resultado<AlertaConsultada>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "fuera-de-rol"));
        }

        if (!IdentificadorDeAlerta.EsValido(alertaId, "inv:") || contadorVisto < 1)
        {
            return Resultado<AlertaConsultada>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, "marca-invalida", [new ErrorDeCampo("alerta", "marca-invalida")]));
        }

        try
        {
            return await unidad.Ejecutar(async ct => await MarcarLocal(alertaId, contadorVisto, actor!.UsuarioId!.Value, ct).ConfigureAwait(false), cancelacion).ConfigureAwait(false);
        }
        catch (AuditoriaNoDisponibleException)
        {
            // La marca no se pudo registrar: la alerta sigue pendiente (AUD-34).
            return Resultado<AlertaConsultada>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "auditoria-no-disponible"));
        }
        catch (Exception falla) when (falla is not OperationCanceledException)
        {
            return Resultado<AlertaConsultada>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "alertas-no-disponibles"));
        }
    }

    private async Task<Resultado<AlertaConsultada>> MarcarLocal(string alertaId, int contadorVisto, Guid usuario, CancellationToken cancelacion)
    {
        var alerta = await almacen.Obtener(alertaId, cancelacion).ConfigureAwait(false);
        if (alerta is null || !alerta.TienePantalla)
        {
            return Resultado<AlertaConsultada>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "alerta-no-encontrada"));
        }

        if (contadorVisto > alerta.Ocurrencias)
        {
            return Resultado<AlertaConsultada>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, "contador-invalido", [new ErrorDeCampo("contadorVisto", "contador-invalido")]));
        }

        var marcada = alerta.MarcarRevisada(contadorVisto, usuario, reloj.Ahora);
        if (ReferenceEquals(marcada, alerta))
        {
            return Resultado<AlertaConsultada>.Exito(ADelContrato(alerta));
        }

        // Primero la auditoría y después la marca: si no se puede registrar, la alerta no cambia (AUD-34).
        await auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.AlertaMarcadaRevisada)
            {
                ObjetoTipo = "alerta",
                ObjetoId = alerta.Id,
                Detalle = new Dictionary<string, string?>
                {
                    ["servicio"] = "inventario",
                    ["motivo"] = alerta.Motivo,
                    ["contadorVisto"] = contadorVisto.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
            },
            cancelacion).ConfigureAwait(false);
        await almacen.Guardar(marcada, cancelacion).ConfigureAwait(false);
        return Resultado<AlertaConsultada>.Exito(ADelContrato(marcada));
    }

    private static AlertaConsultada ADelContrato(AlertaAgrupada alerta) =>
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
}

/// <summary>
/// El historial de ajustes y traslados de una sucursal (AUD-08, AUD-27, AC-AUD-07): <b>la sucursal sale de la credencial</b> del encargado; sin sucursal pedida
/// devuelve la suya y, si pide otra, lo rechaza (la Api lo registra como acceso denegado con la ruta, AUD-05). La administración central indica la que quiere ver.
/// </summary>
public sealed class HistorialDeSucursalLocal(IActorActual actores, IHistorialLocal historial, FirmadorDeCursor firmador) : IHistorialDeSucursal
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
            if (actor.SucursalId is not { } propia || (sucursalPedida is { } pedida && pedida != propia))
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
            return Resultado<PaginaDeRegistros>.Fallo(ConsultasDeAuditoriaLocales.CursorInvalido);
        }

        // El cursor del almacén viaja en un sobre firmado, ligado al usuario y a la sucursal pedida (un cursor de otra sucursal no sirve, AUD-08, AUD-27).
        var usuario = actor.UsuarioId!.Value;
        var huella = "historial:" + sucursal.ToString("N");
        string? cursorDelAlmacen = null;
        if (cursor is not null)
        {
            var carga = firmador.Abrir<CargaDeCursorLocal>(cursor, usuario, huella);
            if (carga is null || string.IsNullOrEmpty(carga.Cursor))
            {
                return Resultado<PaginaDeRegistros>.Fallo(ConsultasDeAuditoriaLocales.CursorInvalido);
            }

            cursorDelAlmacen = carga.Cursor;
        }

        try
        {
            var pagina = await historial.Consultar(sucursal, cursorDelAlmacen, cancelacion).ConfigureAwait(false);
            var siguiente = pagina.SiguienteCursor is null ? null : firmador.Firmar(usuario, huella, new CargaDeCursorLocal(pagina.SiguienteCursor));
            // La página lleva también su propio cursor (el del E-commerce lo necesita para fusionar sin repetir): firmado igual, nunca el crudo del almacén.
            var deLaPagina = pagina.CursorDeLaPagina is null ? null : firmador.Firmar(usuario, huella, new CargaDeCursorLocal(pagina.CursorDeLaPagina));
            return Resultado<PaginaDeRegistros>.Exito(new PaginaDeRegistros(pagina.Registros, siguiente) { CursorDeLaPagina = deLaPagina });
        }
        catch (Exception falla) when (falla is not OperationCanceledException)
        {
            return Resultado<PaginaDeRegistros>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "auditoria-no-disponible"));
        }
    }
}
