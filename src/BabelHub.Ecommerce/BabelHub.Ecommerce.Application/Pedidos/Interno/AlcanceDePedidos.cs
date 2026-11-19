using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>Los roles de Pedidos. La Aplicación no referencia <c>Shared</c> (regla 6): una prueba de arquitectura compara estos valores con <c>RolesDeUsuario</c> y falla si divergen.</summary>
internal static class RolesDePedidos
{
    public const string Cliente = "cliente";
    public const string EncargadoDeSucursal = "encargado-de-sucursal";
    public const string PersonalDeLogistica = "personal-de-logistica";
    public const string AdministradorCentral = "administrador-central";
}

/// <summary>Las claves simbólicas de los fallos de Pedidos; la Api las traduce a los códigos <c>ECO-…</c> (plan §4).</summary>
internal static class ClavesDePedidos
{
    public const string NoAutenticado = "no-autenticado";
    public const string Prohibido = "prohibido";

    /// <summary>Uniforme para un pedido ajeno, inexistente, de otra sucursal, a domicilio para un encargado o de recojo para logística (PED-41): el mismo código, cuerpo y tiempo.</summary>
    public const string NoEncontrado = "no-encontrado";

    public const string ValidacionFallida = "validacion-fallida";
    public const string EstadoInvalido = "pedido-estado-invalido";
    public const string NoCancelable = "pedido-no-cancelable";
    public const string OperacionEnCurso = "pedido-operacion-en-curso";
    public const string LineaEsperaTraslado = "linea-espera-traslado";
    public const string SinLineasAnulables = "pedido-sin-lineas-anulables";
    public const string TextosPendientes = "textos-pendientes";
    public const string Conflicto = "conflicto";
    public const string ServicioNoDisponible = "servicio-no-disponible";
    public const string LimiteExcedido = "limite-excedido";
}

/// <summary>Los motivos de las denegaciones de Pedidos (códigos cerrados y sin datos personales, AUD-06).</summary>
internal static class MotivosDeDenegacionDePedidos
{
    public const string SinSesion = "sin-sesion";
    public const string FueraDeRol = "fuera-de-rol";
    public const string FueraDeSucursal = "fuera-de-sucursal";

    /// <summary>Un pedido o una línea que no es de quien pide (o no existe): se registra con este motivo y el mismo «no encontrado» (PED-41).</summary>
    public const string RecursoAjeno = "recurso-ajeno";
}

/// <summary>Lo que pide cada operación a quien la hace (matriz rol × operación, plan §4).</summary>
internal enum OperacionDePedidos
{
    /// <summary>Mis pedidos, su detalle y cancelar: solo el titular.</summary>
    PedidosDelCliente,

    /// <summary>Preparar, marcar listas, entregar y ver los pedidos de recojo: solo el encargado de su sucursal (la administración central no figura en esta capacidad).</summary>
    GestionDeRecojo,

    /// <summary>Preparar y marcar listas pedidos a domicilio y verlos: logística y la administración central.</summary>
    GestionDeDespacho,

    /// <summary>Todos los pedidos, anular, reintentar el reembolso y las operaciones fallidas: la administración central.</summary>
    Administracion,
}

internal sealed record SolicitanteDePedidos(string Rol, Guid UsuarioId, Guid? SucursalId)
{
    public bool EsCliente => Rol == RolesDePedidos.Cliente;

    public bool EsEncargado => Rol == RolesDePedidos.EncargadoDeSucursal;

    public bool EsLogistica => Rol == RolesDePedidos.PersonalDeLogistica;

    public bool EsAdministracion => Rol == RolesDePedidos.AdministradorCentral;
}

/// <summary>
/// La autorización y el alcance de Pedidos (T09; PED-07, PED-08, PED-26, PED-28, PED-40, PED-41), con el <b>orden fijo</b> de plan §4: (1) el <b>rol</b>; (2) el <b>pedido y su alcance</b>: un pedido inexistente, ajeno, de otra sucursal, a domicilio para un encargado o de recojo para logística es el
/// <b>mismo</b> «no encontrado» (cuerpo y tiempo) con un evento de acceso denegado por <b>cada</b> intento (sin el valor del identificador); (3) la <b>pertenencia de los <c>lineaIds</c></b> (uno ajeno al pedido también es «no encontrado»); y (4) el estado, que es de cada caso de uso. «Prohibido» solo por
/// rol o por el parámetro de sucursal de la ruta; una sucursal que no se puede resolver se niega y se registra. Los trabajos de «sistema» no pasan por aquí: sus casos de uso exigen el actor de sistema. Cada evento se registra fuera de la transacción del caso de uso.
/// </summary>
internal sealed class AutorizadorDePedidos(IActorActual actores, IDenegacionDeAcceso denegaciones, IResolucionDeSucursal sucursales)
{
    public ErrorDeAplicacion NoEncontrado() => new(CategoriaDeError.NoEncontrado, ClavesDePedidos.NoEncontrado);

    /// <summary>El paso 1: el rol. Sin contexto o sin sesión es «no autenticado» y un rol que no es de la operación, «prohibido»; ambos dejan su evento.</summary>
    public async Task<Resultado<SolicitanteDePedidos>> ExigirRol(OperacionDePedidos operacion, CancellationToken cancelacion)
    {
        var actor = actores.Actual;
        if (actor is null or { Tipo: TipoDeActor.Anonimo })
        {
            await Denegar(MotivosDeDenegacionDePedidos.SinSesion, "ECO-NO-AUTENTICADO", cancelacion).ConfigureAwait(false);
            return Resultado<SolicitanteDePedidos>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, ClavesDePedidos.NoAutenticado));
        }

        if (actor is { Tipo: TipoDeActor.Usuario, UsuarioId: { } usuario, Rol: { } rol } && RolPermitido(operacion, rol))
        {
            return Resultado<SolicitanteDePedidos>.Exito(new SolicitanteDePedidos(rol, usuario, actor.SucursalId));
        }

        await Denegar(MotivosDeDenegacionDePedidos.FueraDeRol, "ECO-PROHIBIDO", cancelacion).ConfigureAwait(false);
        return Resultado<SolicitanteDePedidos>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDePedidos.Prohibido));
    }

    /// <summary>Solo el sistema (un trabajo de fondo): cualquier otro actor, también el personal, se rechaza con su evento.</summary>
    public async Task<ErrorDeAplicacion?> ExigirSistema(CancellationToken cancelacion)
    {
        if (actores.Actual is { Tipo: TipoDeActor.Sistema })
        {
            return null;
        }

        var sinContexto = actores.Actual is null;
        await Denegar(sinContexto ? MotivosDeDenegacionDePedidos.SinSesion : MotivosDeDenegacionDePedidos.FueraDeRol, sinContexto ? "ECO-NO-AUTENTICADO" : "ECO-PROHIBIDO", cancelacion).ConfigureAwait(false);
        return sinContexto ? new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, ClavesDePedidos.NoAutenticado) : new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDePedidos.Prohibido);
    }

    /// <summary>
    /// El parámetro de sucursal de la ruta de la gestión de recojo: el encargado solo opera en <b>su</b> sucursal. Una sucursal que no se puede resolver o que no es la suya es «prohibido» (el parámetro de la ruta) y deja el evento de fuera de sucursal.
    /// </summary>
    public async Task<ErrorDeAplicacion?> ExigirSucursalDeLaRuta(SolicitanteDePedidos solicitante, string codigoDeSucursal, CancellationToken cancelacion)
    {
        var resuelta = string.IsNullOrWhiteSpace(codigoDeSucursal) ? null : await sucursales.IdentificadorDe(codigoDeSucursal, cancelacion).ConfigureAwait(false);
        if (resuelta is { } id && solicitante.SucursalId == id)
        {
            return null;
        }

        await Denegar(MotivosDeDenegacionDePedidos.FueraDeSucursal, "ECO-PROHIBIDO", cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDePedidos.Prohibido);
    }

    /// <summary>
    /// El paso 2: el pedido y su alcance. Devuelve <see langword="null"/> si <paramref name="pedido"/> es del alcance de quien pide; si no, el «no encontrado» uniforme, con un evento por cada intento.
    /// </summary>
    public async Task<ErrorDeAplicacion?> ComprobarAlcance(Pedido? pedido, SolicitanteDePedidos solicitante, CancellationToken cancelacion)
    {
        if (pedido is not null && await EsDelAlcance(pedido, solicitante, cancelacion).ConfigureAwait(false))
        {
            return null;
        }

        await Denegar(MotivosDeDenegacionDePedidos.RecursoAjeno, "ECO-NO-ENCONTRADO", cancelacion).ConfigureAwait(false);
        return NoEncontrado();
    }

    /// <summary>El paso 3: cada <c>lineaId</c> debe ser del pedido; uno ajeno (o repetido) es el mismo «no encontrado» con su evento.</summary>
    public async Task<ErrorDeAplicacion?> ComprobarLineas(Pedido pedido, IReadOnlyCollection<Guid> lineaIds, CancellationToken cancelacion)
    {
        if (lineaIds.Count > 0 && lineaIds.Distinct().Count() == lineaIds.Count && lineaIds.All(id => pedido.Lineas.Any(l => l.Id == id)))
        {
            return null;
        }

        await Denegar(MotivosDeDenegacionDePedidos.RecursoAjeno, "ECO-NO-ENCONTRADO", cancelacion).ConfigureAwait(false);
        return NoEncontrado();
    }

    private async Task<bool> EsDelAlcance(Pedido pedido, SolicitanteDePedidos solicitante, CancellationToken cancelacion)
    {
        if (solicitante.EsCliente)
        {
            return pedido.CuentaId == solicitante.UsuarioId;
        }

        if (solicitante.EsAdministracion)
        {
            return true;
        }

        if (solicitante.EsLogistica)
        {
            return pedido.Tipo != TipoDeEntregaDelPedido.Recojo;
        }

        if (solicitante.EsEncargado && pedido.Tipo == TipoDeEntregaDelPedido.Recojo && pedido.CodigoDeSucursalDeRecojo is { } codigo)
        {
            return await sucursales.IdentificadorDe(codigo, cancelacion).ConfigureAwait(false) is { } id && solicitante.SucursalId == id;
        }

        return false;
    }

    private static bool RolPermitido(OperacionDePedidos operacion, string rol) => operacion switch
    {
        OperacionDePedidos.PedidosDelCliente => rol == RolesDePedidos.Cliente,
        OperacionDePedidos.GestionDeRecojo => rol == RolesDePedidos.EncargadoDeSucursal,
        OperacionDePedidos.GestionDeDespacho => rol is RolesDePedidos.PersonalDeLogistica or RolesDePedidos.AdministradorCentral,
        OperacionDePedidos.Administracion => rol == RolesDePedidos.AdministradorCentral,
        _ => false,
    };

    private Task Denegar(string motivo, string codigoDeError, CancellationToken cancelacion) =>
        denegaciones.Registrar(new DenegacionDeAcceso(motivo, ruta: null) { CodigoDeError = codigoDeError }, cancelacion);
}
