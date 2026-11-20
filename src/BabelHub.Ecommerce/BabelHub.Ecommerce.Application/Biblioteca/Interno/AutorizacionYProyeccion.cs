using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;

namespace BabelHub.Ecommerce.Application.Biblioteca.Interno;

internal static class RolesDeBiblioteca
{
    public const string Cliente = "cliente";

    public const string AdministradorCentral = "administrador-central";
}

internal static class MotivosDeDenegacionDeBiblioteca
{
    public const string FueraDeRol = "fuera-de-rol";

    public const string SinSesion = "sin-sesion";

    public const string RecursoAjeno = "recurso-ajeno";
}

/// <summary>
/// La comprobación de quién pide, siempre en el orden del plan §4: sesión y rol → textos legales pendientes → recurso. Un intento rechazado por rol deja un acceso denegado (SEG-09); un acceso ajeno o inexistente responde lo mismo
/// (<c>ECO-NO-ENCONTRADO</c>) y deja uno por cada intento (BIB-07). La cuenta del cliente sale siempre de <see cref="IActorActual"/>, nunca de la solicitud.
/// </summary>
internal sealed class AutorizadorDeBiblioteca(IActorActual actores, IDenegacionDeAcceso denegaciones, IEstadoDeCuenta cuentas)
{
    public ActorActual? Actor => actores.Actual is { Tipo: not TipoDeActor.Anonimo } actor ? actor : null;

    /// <summary>El cliente con sesión: su cuenta. Otro rol recibe <c>ECO-PROHIBIDO</c>; sin sesión, <c>ECO-NO-AUTENTICADO</c>.</summary>
    public async Task<Resultado<Guid>> ExigirCliente(CancellationToken cancelacion)
    {
        var actor = Actor;
        if (actor is not { Tipo: TipoDeActor.Usuario, UsuarioId: { } usuario })
        {
            await Denegar(MotivosDeDenegacionDeBiblioteca.SinSesion, "ECO-NO-AUTENTICADO", cancelacion).ConfigureAwait(false);
            return FallosDeBiblioteca.De<Guid>(CategoriaDeError.NoAutenticado, ClavesDeBiblioteca.NoAutenticado);
        }

        if (actor.Rol != RolesDeBiblioteca.Cliente)
        {
            await Denegar(MotivosDeDenegacionDeBiblioteca.FueraDeRol, "ECO-PROHIBIDO", cancelacion).ConfigureAwait(false);
            return FallosDeBiblioteca.De<Guid>(CategoriaDeError.Prohibido, ClavesDeBiblioteca.Prohibido);
        }

        return Resultado<Guid>.Exito(usuario);
    }

    public async Task<ErrorDeAplicacion?> ExigirAdministradorCentral(CancellationToken cancelacion)
    {
        var actor = Actor;
        if (actor is not { Tipo: TipoDeActor.Usuario, UsuarioId: not null })
        {
            await Denegar(MotivosDeDenegacionDeBiblioteca.SinSesion, "ECO-NO-AUTENTICADO", cancelacion).ConfigureAwait(false);
            return new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, ClavesDeBiblioteca.NoAutenticado);
        }

        if (actor.Rol == RolesDeBiblioteca.AdministradorCentral)
        {
            return null;
        }

        await Denegar(MotivosDeDenegacionDeBiblioteca.FueraDeRol, "ECO-PROHIBIDO", cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDeBiblioteca.Prohibido);
    }

    /// <summary>Defensa en profundidad de <c>web-base</c> (WEB-41, D11): con textos legales pendientes, la biblioteca y las descargas se rechazan también en la Aplicación. Se lee sin bloqueo, antes de cualquier transacción.</summary>
    public async Task<ErrorDeAplicacion?> ExigirTextosAlDia(Guid cuentaId, CancellationToken cancelacion)
    {
        EstadoDeCuenta? estado;
        try
        {
            estado = await cuentas.Consultar(cuentaId, cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeBiblioteca.ServicioNoDisponible);
        }

        if (estado is null || estado.CuentaCanceladaODesactivada)
        {
            return new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, ClavesDeBiblioteca.NoAutenticado);
        }

        return estado.Textos == EstadoDeAceptacionDeLosTextos.Pendiente
            ? new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDeBiblioteca.TextosPendientes)
            : null;
    }

    /// <summary>La cuenta que consulta la administración existe (<c>false</c> si no, o si Identidad no responde: <paramref name="noDisponible"/>).</summary>
    public async Task<(bool Existe, bool NoDisponible)> ExisteLaCuenta(Guid cuentaId, CancellationToken cancelacion)
    {
        try
        {
            return (await cuentas.Consultar(cuentaId, cancelacion).ConfigureAwait(false) is not null, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, true);
        }
    }

    /// <summary>Un acceso ajeno o inexistente: la misma respuesta y un acceso denegado por <b>cada</b> intento (BIB-07).</summary>
    public async Task<ErrorDeAplicacion> NoEncontradoYDenegado(CancellationToken cancelacion)
    {
        await Denegar(MotivosDeDenegacionDeBiblioteca.RecursoAjeno, "ECO-NO-ENCONTRADO", cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, ClavesDeBiblioteca.NoEncontrado);
    }

    private Task Denegar(string motivo, string codigoDeError, CancellationToken cancelacion) =>
        denegaciones.Registrar(new DenegacionDeAcceso(motivo, ruta: null) { CodigoDeError = codigoDeError }, cancelacion);
}

/// <summary>
/// Arma lo que ve el cliente: el título (de Catálogo, que incluye las ediciones retiradas o con licencia vencida: el acceso se conserva, BIB-09), el formato del archivo (de <see cref="IReferenciaDeArchivosDelCatalogo"/>, <c>null</c> si no responde) y
/// las descargas «usadas de 5». Es solo lectura y no abre ninguna transacción.
/// </summary>
internal sealed class ProyectorDeAccesos(IConsultaDeCatalogo catalogo, IReferenciaDeArchivosDelCatalogo archivos, IRepositorioDeDescargas descargas)
{
    public const string TituloPorOmision = "Libro digital";

    public async Task<Resultado<IReadOnlyList<AccesoDeBiblioteca>>> Proyectar(IReadOnlyList<Acceso> accesos, CancellationToken cancelacion)
    {
        if (accesos.Count == 0)
        {
            return Resultado<IReadOnlyList<AccesoDeBiblioteca>>.Exito([]);
        }

        IReadOnlyList<EdicionConsultada> ediciones;
        IReadOnlyList<Guid> conAyuda;
        try
        {
            var edicionIds = accesos.Select(a => a.EdicionId).Distinct().ToList();
            var consultadas = await catalogo.ObtenerEdiciones(edicionIds, cancelacion).ConfigureAwait(false);
            if (!consultadas.EsExito)
            {
                return FallosDeBiblioteca.NoDisponible<IReadOnlyList<AccesoDeBiblioteca>>();
            }

            ediciones = consultadas.Valor;
            conAyuda = await descargas.AccesosConAyudaAbierta([.. accesos.Select(a => a.Id)], cancelacion).ConfigureAwait(false);
        }
        catch (BibliotecaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<IReadOnlyList<AccesoDeBiblioteca>>();
        }

        var titulos = ediciones.ToDictionary(e => e.EdicionId, e => e.Titulo);
        var formatos = new Dictionary<Guid, FormatoDeArchivoDigital?>();
        foreach (var edicionId in accesos.Select(a => a.EdicionId).Distinct())
        {
            formatos[edicionId] = await FormatoDe(edicionId, cancelacion).ConfigureAwait(false);
        }

        IReadOnlyList<AccesoDeBiblioteca> proyectados =
        [
            .. accesos.Select(a => new AccesoDeBiblioteca(
                a.Id,
                a.EdicionId,
                titulos.GetValueOrDefault(a.EdicionId, TituloPorOmision),
                formatos[a.EdicionId],
                a.FechaDeCompra,
                MapeoDeBiblioteca.Estado(a.Estado),
                a.DescargasUsadas,
                PoliticaDeDescargas.MaximoDeDescargas,
                a.DescargasRestantes,
                conAyuda.Contains(a.Id))),
        ];
        return Resultado<IReadOnlyList<AccesoDeBiblioteca>>.Exito(proyectados);
    }

    private async Task<FormatoDeArchivoDigital?> FormatoDe(Guid edicionId, CancellationToken cancelacion)
    {
        try
        {
            var referencia = await archivos.ObtenerDeEdicion(edicionId, cancelacion).ConfigureAwait(false);
            return referencia.EsExito ? referencia.Valor.Formato : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}

internal static class PaginacionDeBiblioteca
{
    public const int TamanoMaximo = 50;

    public const int TamanoPorOmision = 20;

    public static bool EsValida(int pagina, int tamano) => pagina >= 1 && tamano is >= 1 and <= TamanoMaximo;
}
