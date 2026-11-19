using System.Globalization;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;

namespace BabelHub.Ecommerce.Application.Devoluciones.Interno;

/// <summary>Las claves simbólicas de los fallos del módulo; la Api las traduce a los códigos <c>ECO-…</c> y <c>ECO-DEV-…</c> (plan §4).</summary>
internal static class ClavesDeDevoluciones
{
    public const string ValidacionFallida = "validacion-fallida";

    public const string ServicioNoDisponible = "servicio-no-disponible";

    public const string NoAutenticado = "no-autenticado";

    public const string Prohibido = "prohibido";

    /// <summary>El pedido o la devolución no existe, es de otra cuenta o de otra sucursal receptora, o es un reporte visto por un encargado: la misma respuesta (DEV-13, DEV-28).</summary>
    public const string NoEncontrado = "no-encontrado";

    /// <summary><c>ECO-TEXTOS-PENDIENTES</c> (403): las devoluciones quedan bloqueadas con textos legales pendientes (web-base WEB-41).</summary>
    public const string TextosPendientes = "textos-pendientes";

    /// <summary><c>ECO-CUENTA-EN-CANCELACION</c> (409): la cancelación de la cuenta ya se autorizó a ejecutar y no se escribe ningún registro (DEV-26).</summary>
    public const string CuentaEnCancelacion = "cuenta-en-cancelacion";

    public const string TiposMezclados = "tipos-mezclados";

    public const string LineaNoEntregada = "linea-no-entregada";

    public const string PlazoVencido = "plazo-vencido";

    public const string CantidadExcedida = "cantidad-excedida";

    public const string LineaNoDevolvible = "linea-no-devolvible";

    public const string PoliticaDigital = "politica-digital";

    public const string ReporteNoAdmitido = "reporte-no-admitido";

    public const string EstadoInvalido = "estado-invalido";

    public const string ReingresoRechazado = "reingreso-rechazado";

    public const string Conflicto = "conflicto";

    public const string LimiteExcedido = "limite-excedido";

    public const string EsperaEnSegundos = "espera-en-segundos";

    public const string Motivo = "motivo";

    public const string DisponibleDesde = "disponibleDesde";

    /// <summary>El motivo del catálogo cerrado de alertas técnicas para un reingreso rechazado, en conflicto o estancado (hasta que el carril A dé de alta motivos propios de Devoluciones).</summary>
    public const string AlertaGenerica = "dependencia-no-necesaria-fallida";
}

internal static class FallosDeDevoluciones
{
    public static Resultado<T> De<T>(CategoriaDeError categoria, string clave, IReadOnlyList<ErrorDeCampo>? errores = null) =>
        Resultado<T>.Fallo(new ErrorDeAplicacion(categoria, clave, errores));

    public static Resultado<T> Fallo<T>(ErrorDeAplicacion error) => Resultado<T>.Fallo(error);

    public static Resultado<T> NoDisponible<T>() => De<T>(CategoriaDeError.NoDisponible, ClavesDeDevoluciones.ServicioNoDisponible);
}

internal static class RolesDeDevoluciones
{
    public const string Cliente = "cliente";

    public const string EncargadoDeSucursal = "encargado-de-sucursal";

    public const string AdministradorCentral = "administrador-central";

    public static bool EsCliente(ActorActual? actor) => actor is { Tipo: TipoDeActor.Usuario, UsuarioId: not null, Rol: Cliente };

    public static bool EsEncargado(ActorActual? actor) => actor is { Tipo: TipoDeActor.Usuario, UsuarioId: not null, Rol: EncargadoDeSucursal };

    public static bool EsAdministracion(ActorActual? actor) => actor is { Tipo: TipoDeActor.Usuario, UsuarioId: not null, Rol: AdministradorCentral };
}

internal static class MotivosDeDenegacionDeDevoluciones
{
    public const string FueraDeRol = "fuera-de-rol";

    public const string SinSesion = "sin-sesion";

    public const string RecursoAjeno = "recurso-ajeno";

    /// <summary>Los motivos del catálogo de <c>AccesoDenegado</c>: la devolución es de otra sucursal receptora o la sucursal del actor no se puede resolver (<c>fuera-de-sucursal</c>), o el alcance no alcanza (un reporte o un digital visto por un encargado).</summary>
    public const string FueraDeSucursal = "fuera-de-sucursal";

    public const string AlcanceInsuficiente = "alcance-insuficiente";
}

/// <summary>
/// La autorización y el alcance de Devoluciones, con un <b>orden fijo</b> (plan §4): rol → textos legales pendientes → pedido o devolución y alcance → estado. Un pedido o una devolución ajena o inexistente, de otra sucursal receptora o un reporte visto por un
/// encargado responden <b>lo mismo</b> (<c>ECO-NO-ENCONTRADO</c>) y registran un acceso denegado por <b>cada</b> intento; una sucursal que no se puede resolver se niega (DEV-13, DEV-28). <c>ECO-PROHIBIDO</c> solo por rol.
/// </summary>
internal sealed class AutorizadorDeDevoluciones(IActorActual actores, IDenegacionDeAcceso denegaciones, IEstadoDeCuenta cuentas, IResolucionDeSucursal sucursales)
{
    public ActorActual? Actor => actores.Actual is { Tipo: not TipoDeActor.Anonimo } actor ? actor : null;

    /// <summary>El cliente: solo él solicita, reporta y ve las suyas (DEV-01).</summary>
    public Task<ErrorDeAplicacion?> ExigirCliente(CancellationToken cancelacion) => Exigir(RolesDeDevoluciones.EsCliente, cancelacion);

    /// <summary>El encargado de sucursal o la administración central: aprueban, rechazan y reciben un impreso (DEV-13).</summary>
    public Task<ErrorDeAplicacion?> ExigirPersonal(CancellationToken cancelacion) => Exigir(a => RolesDeDevoluciones.EsEncargado(a) || RolesDeDevoluciones.EsAdministracion(a), cancelacion);

    /// <summary>El actor «sistema» de los trabajos de fondo (conciliación, revisión de plazos, depuración): una persona o una integración no lo es. Sin sesión de persona, no hay textos legales que comprobar.</summary>
    public async Task<ErrorDeAplicacion?> ExigirSistema(CancellationToken cancelacion)
    {
        if (actores.Actual is { Tipo: TipoDeActor.Sistema })
        {
            return null;
        }

        await Denegar(MotivosDeDenegacionDeDevoluciones.FueraDeRol, "ECO-PROHIBIDO", cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDeDevoluciones.Prohibido);
    }

    /// <summary>Solo la administración central: ver todas y decidir un reporte (DEV-28).</summary>
    public Task<ErrorDeAplicacion?> ExigirAdministracion(CancellationToken cancelacion) => Exigir(RolesDeDevoluciones.EsAdministracion, cancelacion);

    private async Task<ErrorDeAplicacion?> Exigir(Func<ActorActual?, bool> permitido, CancellationToken cancelacion)
    {
        var actor = Actor;
        if (actor is null)
        {
            await Denegar(MotivosDeDenegacionDeDevoluciones.SinSesion, "ECO-NO-AUTENTICADO", cancelacion).ConfigureAwait(false);
            return new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, ClavesDeDevoluciones.NoAutenticado);
        }

        if (!permitido(actor))
        {
            await Denegar(MotivosDeDenegacionDeDevoluciones.FueraDeRol, "ECO-PROHIBIDO", cancelacion).ConfigureAwait(false);
            return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDeDevoluciones.Prohibido);
        }

        return await TextosAlDia(actor.UsuarioId!.Value, cancelacion).ConfigureAwait(false);
    }

    /// <summary>
    /// Los textos legales pendientes (defensa en profundidad de web-base WEB-41): la consulta a Identidad va <b>antes</b> de abrir una transacción. Una cuenta inexistente o cancelada no es una sesión válida; si Identidad no responde,
    /// «no disponible» y no se escribe nada.
    /// </summary>
    private async Task<ErrorDeAplicacion?> TextosAlDia(Guid usuarioId, CancellationToken cancelacion)
    {
        EstadoDeCuenta? estado;
        try
        {
            estado = await cuentas.Consultar(usuarioId, cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeDevoluciones.ServicioNoDisponible);
        }

        if (estado is null || estado.CuentaCanceladaODesactivada)
        {
            await Denegar(MotivosDeDenegacionDeDevoluciones.SinSesion, "ECO-NO-AUTENTICADO", cancelacion).ConfigureAwait(false);
            return new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, ClavesDeDevoluciones.NoAutenticado);
        }

        return estado.Textos == EstadoDeAceptacionDeLosTextos.AlDia ? null : new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDeDevoluciones.TextosPendientes);
    }

    /// <summary>La propiedad de un pedido por el cliente: la cuenta opaca sale del contrato de lectura de Pedidos, nunca del actor ni de una tabla propia (D01). Ajeno o inexistente: lo mismo.</summary>
    public async Task<ErrorDeAplicacion?> ExigirQueElPedidoEsDelCliente(Guid cuentaDelPedido, CancellationToken cancelacion) =>
        Actor?.UsuarioId == cuentaDelPedido ? null : await NoEncontrado(MotivosDeDenegacionDeDevoluciones.RecursoAjeno, cancelacion).ConfigureAwait(false);

    /// <summary>
    /// El alcance de una devolución para la gestión: la administración central ve todas; un encargado solo las de <b>impresos de una solicitud</b> con <b>su</b> sucursal receptora (nunca un reporte ni un digital). Si su sucursal
    /// no se puede resolver o es otra, responde «no encontrado» con un acceso denegado (DEV-13, DEV-28).
    /// </summary>
    public async Task<ErrorDeAplicacion?> ExigirAlcanceDeGestion(Devolucion devolucion, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(devolucion);
        var actor = Actor;
        if (RolesDeDevoluciones.EsAdministracion(actor))
        {
            return null;
        }

        if (!RolesDeDevoluciones.EsEncargado(actor) || actor!.SucursalId is not { } sucursalId)
        {
            return await NoEncontrado(MotivosDeDenegacionDeDevoluciones.FueraDeSucursal, cancelacion).ConfigureAwait(false);
        }

        if (devolucion.Tipo != TipoDeDevolucion.Impreso || devolucion.Origen != OrigenDeDevolucion.Solicitud)
        {
            return await NoEncontrado(MotivosDeDenegacionDeDevoluciones.AlcanceInsuficiente, cancelacion).ConfigureAwait(false);
        }

        string? codigo;
        try
        {
            codigo = await sucursales.CodigoDe(sucursalId, cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            codigo = null;
        }

        if (codigo is null)
        {
            return await NoEncontrado(MotivosDeDenegacionDeDevoluciones.FueraDeSucursal, cancelacion).ConfigureAwait(false);
        }

        return string.Equals(codigo, devolucion.CodigoDeSucursalReceptora, StringComparison.Ordinal)
            ? null
            : await NoEncontrado(MotivosDeDenegacionDeDevoluciones.FueraDeSucursal, cancelacion).ConfigureAwait(false);
    }

    /// <summary>
    /// El código de la sucursal con la que se listan las devoluciones de un encargado: su sucursal debe resolverse y coincidir con la de la ruta; la administración central lista la que pida. Un código que no es el suyo o que no se resuelve es
    /// <c>ECO-PROHIBIDO</c> «por el parámetro de sucursal de la ruta» (plan §4) con un acceso denegado.
    /// </summary>
    public async Task<ErrorDeAplicacion?> ExigirSucursalDeLaRuta(string codigoDeLaRuta, CancellationToken cancelacion)
    {
        var actor = Actor;
        if (RolesDeDevoluciones.EsAdministracion(actor))
        {
            return null;
        }

        if (!RolesDeDevoluciones.EsEncargado(actor))
        {
            await Denegar(MotivosDeDenegacionDeDevoluciones.FueraDeRol, "ECO-PROHIBIDO", cancelacion).ConfigureAwait(false);
            return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDeDevoluciones.Prohibido);
        }

        string? codigo = null;
        if (actor?.SucursalId is { } sucursalId)
        {
            try
            {
                codigo = await sucursales.CodigoDe(sucursalId, cancelacion).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                codigo = null;
            }
        }

        if (codigo is not null && string.Equals(codigo, codigoDeLaRuta, StringComparison.Ordinal))
        {
            return null;
        }

        await Denegar(MotivosDeDenegacionDeDevoluciones.FueraDeSucursal, "ECO-PROHIBIDO", cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDeDevoluciones.Prohibido);
    }

    /// <summary>La respuesta uniforme de lo ajeno o inexistente, con un acceso denegado <b>por cada intento</b>.</summary>
    public async Task<ErrorDeAplicacion> NoEncontrado(string motivo, CancellationToken cancelacion)
    {
        await Denegar(motivo, "ECO-NO-ENCONTRADO", cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, ClavesDeDevoluciones.NoEncontrado);
    }

    private Task Denegar(string motivo, string codigoDeError, CancellationToken cancelacion) =>
        denegaciones.Registrar(new DenegacionDeAcceso(motivo, ruta: null) { CodigoDeError = codigoDeError }, cancelacion);
}

/// <summary>Los límites de uso de Devoluciones (plan §6; SEG-05, SEG-07): consultas (60 por usuario y minuto) y operaciones (30 por usuario y minuto). Las claves están pendientes de su alta en el catálogo de Plataforma (hallazgo para el carril A): mientras no existan, falla cerrado.</summary>
internal sealed class LimitesDeDevoluciones(IControlDeLimites limites, IActorActual actores)
{
    public static readonly ClaveDePolitica ConsultasPorUsuario = new("consultas-de-devoluciones-por-usuario");

    public static readonly ClaveDePolitica OperacionesPorUsuario = new("operaciones-de-devoluciones-por-usuario");

    public Task<ErrorDeAplicacion?> Consulta(CancellationToken cancelacion) => Reservar(ConsultasPorUsuario, cancelacion);

    public Task<ErrorDeAplicacion?> Operacion(CancellationToken cancelacion) => Reservar(OperacionesPorUsuario, cancelacion);

    private async Task<ErrorDeAplicacion?> Reservar(ClaveDePolitica politica, CancellationToken cancelacion)
    {
        ResultadoDeLimite resultado;
        try
        {
            resultado = await limites.Reservar(politica, actores.Actual?.UsuarioId?.ToString("N") ?? "sin-usuario", cancelacion: cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeDevoluciones.ServicioNoDisponible);
        }

        return resultado.Estado switch
        {
            EstadoDeLimite.Permitido => null,
            EstadoDeLimite.Excedido => new ErrorDeAplicacion(
                CategoriaDeError.Limite,
                ClavesDeDevoluciones.LimiteExcedido,
                [
                    new ErrorDeCampo(ClavesDeDevoluciones.EsperaEnSegundos, (resultado.EsperaEnSegundos ?? 1).ToString(CultureInfo.InvariantCulture)),
                    new ErrorDeCampo(ClavesDeDevoluciones.Motivo, resultado.Motivo ?? politica.Valor),
                    new ErrorDeCampo(ClavesDeDevoluciones.DisponibleDesde, (resultado.DisponibleDesde ?? DateTimeOffset.UtcNow.AddSeconds(resultado.EsperaEnSegundos ?? 1)).ToString("O", CultureInfo.InvariantCulture)),
                ]),
            _ => new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeDevoluciones.ServicioNoDisponible),
        };
    }
}
