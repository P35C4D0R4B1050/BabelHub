using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Web;

/// <summary>
/// Lo que una ruta declara que se puede hacer con ella mientras la persona tiene textos legales pendientes de aceptar (WEB-46). Es el tipo del contrato público: la Presentación lo
/// usa en su atributo y no conoce el del Dominio.
/// </summary>
public enum CapacidadWeb
{
    Cuenta,
    Pedidos,
    Aceptar,
    CerrarSesion,
    TextosLegales,
    LibroDeReclamaciones,
    EnviarHoja,
}

/// <summary>Lo que la Presentación sabe de la solicitud y entrega a la política: nada de HTTP, solo datos ya reducidos.</summary>
/// <param name="Actor">El actor verificado de la sesión, o <see langword="null"/> si no hay sesión válida (un visitante).</param>
/// <param name="ValorDeLaCookie">La cookie de la sesión, solo para guardar el retorno tras aceptar; no se imprime.</param>
/// <param name="Area">El área del endpoint, o <see langword="null"/> si es compartido.</param>
/// <param name="Capacidad">Lo que el endpoint declara permitido con los textos pendientes, o <see langword="null"/> si no declara nada (se niega).</param>
/// <param name="Metodo">El método HTTP tal como llegó (se compara ordinal: <c>get</c> o un método no estándar no son <c>GET</c>).</param>
/// <param name="SucursalDeLaRuta">El valor de sucursal que la ruta declara con <c>[ParametroDeSucursal]</c>, si lo hay.</param>
/// <param name="SinConsultaDeEstado">El endpoint se sirve sin consultar a Identidad (estáticos, salud y los destinos de las propias redirecciones).</param>
/// <param name="PlantillaDeRuta">La plantilla del endpoint (nunca la dirección con sus valores): es lo que se registra en el evento del bloqueo.</param>
/// <param name="RutaParaElRetorno">La ruta local (sin consulta ni fragmento) a la que volver tras aceptar, o <see langword="null"/>.</param>
/// <param name="EsPantallaCompleta">Es una pantalla completa y no un fragmento ni un sondeo (solo esas se guardan como retorno).</param>
/// <param name="SinRetorno">El endpoint declara <c>[SinRetorno]</c>: nunca se guarda como destino de vuelta.</param>
public sealed record SolicitudDeAcceso(
    ActorVerificado? Actor,
    string? ValorDeLaCookie,
    AreaWeb? Area,
    CapacidadWeb? Capacidad,
    string Metodo,
    Guid? SucursalDeLaRuta,
    bool SinConsultaDeEstado,
    string? PlantillaDeRuta,
    string? RutaParaElRetorno,
    bool EsPantallaCompleta,
    bool SinRetorno)
{
    public override string ToString() => $"SolicitudDeAcceso {{ Area = {Area}, Capacidad = {Capacidad}, Metodo = {Metodo} }}";
}

public enum TipoDeDecisionDeAcceso
{
    /// <summary>La solicitud sigue su camino (con <see cref="DecisionDeAcceso.TextosPendientes"/> si la persona aún debe aceptar los textos y la ruta es de las permitidas).</summary>
    Permitida,

    /// <summary>Una pantalla que el rol permite pero los textos bloquean: se lleva a aceptarlos.</summary>
    Redirige,

    /// <summary>Una acción (o un método o una ruta no declarada) bloqueada: se rechaza con el código estable <c>ECO-TEXTOS-PENDIENTES</c>.</summary>
    Bloqueada,

    /// <summary>No se pudo saber si la persona aceptó (Identidad no responde, tarda, responde mal o el tope de consultas está lleno): la web falla cerrada.</summary>
    NoDisponible,

    /// <summary>El rol no ve el área o el alcance no es el suyo: la pantalla de sin permiso, sin consultar a Identidad.</summary>
    SinPermiso,

    /// <summary>No hay sesión y la ruta la pide.</summary>
    SinSesion,
}

/// <summary>Por qué se bloqueó una solicitud con los textos pendientes (para el mensaje y el registro; nunca datos de la persona).</summary>
public enum MotivoDeBloqueoPorTextos
{
    Ninguno,
    AccionNoPermitida,
    RutaNoDeclarada,
    MetodoNoDeclarado,
    MetodoNoEstandar,
    IdentidadNoDisponible,
}

public sealed record DecisionDeAcceso(TipoDeDecisionDeAcceso Tipo, bool TextosPendientes = false, MotivoDeBloqueoPorTextos Motivo = MotivoDeBloqueoPorTextos.Ninguno, bool AlcanceFueraDeLaSucursal = false)
{
    public static DecisionDeAcceso Permitida(bool textosPendientes = false) => new(TipoDeDecisionDeAcceso.Permitida, textosPendientes);
}

/// <summary>
/// Contrato público de la política de acceso con los textos legales pendientes (WEB-41 a WEB-49, D10): <b>en cada solicitud</b> y sin caché decide si la persona puede seguir, debe aceptar
/// los textos, está bloqueada o no se pudo saber. Orden: sesión, rol y alcance (sin consultar a Identidad) y solo después el estado de aceptación. Falla cerrada.
/// </summary>
public interface IPoliticaDeAccesoPorTextos
{
    Task<DecisionDeAcceso> Resolver(SolicitudDeAcceso solicitud, CancellationToken cancelacion = default);

    /// <summary>
    /// Entrega <b>una sola vez</b> la ruta guardada para volver tras aceptar (y la borra), o <see langword="null"/> si no hay. Solo si sigue siendo una ruta local de una pantalla que el rol
    /// permite; nunca algo que llegue de la consulta, un formulario o una cabecera.
    /// </summary>
    Task<string?> TomarRetorno(string valorDeLaCookie, RolWeb rol, CancellationToken cancelacion = default);
}

/// <summary>Lo que se muestra para aceptar: el identificador de la versión vigente del conjunto y los textos (WEB-34, WEB-41).</summary>
public sealed record VersionParaAceptar(string Identificador, IReadOnlyList<TextoLegal> Textos);

public enum ResultadoDeAceptacion
{
    Aceptada,

    /// <summary>La versión que la persona vio ya no es la vigente (se publicó otra mientras leía): debe revisarla de nuevo.</summary>
    VersionCambiada,

    NoDisponible,
}

/// <summary>Contrato público: mostrar y aceptar la versión vigente de los textos (T38). Identidad compara la versión enviada con la vigente (R12).</summary>
public interface IAceptacionDeTextos
{
    Task<Resultado<VersionParaAceptar>> VersionVigente(CancellationToken cancelacion = default);

    Task<ResultadoDeAceptacion> Aceptar(ActorVerificado actor, string versionMostrada, CancellationToken cancelacion = default);
}
