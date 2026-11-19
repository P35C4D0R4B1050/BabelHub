using System.Globalization;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

/// <summary>
/// Lo que comparten los casos de uso del carrito (CAR-01 a CAR-04, CAR-20, CAR-26, CAR-35; plan §1 «Ninguna llamada de red dentro de una transacción»): quién pide, el estado de la cuenta, los límites, la disponibilidad del
/// Catálogo y la posesión (que se obtienen <b>antes</b> de abrir la transacción y se revalidan al bloquear), la compuerta de edición según el checkout vivo y la vista del carrito. Un orquestador no tiene estado: todo
/// lo que guarda lo guardan los repositorios dentro de la transacción de la unidad de trabajo.
/// </summary>
internal sealed class ServiciosDelCarrito(
    AutorizadorDelCarrito autorizador,
    IConsultaDeCatalogo catalogo,
    IDisponibilidadDeEdiciones disponibilidad,
    IPosesionDeEdiciones posesion,
    IEstadoDeCuenta estadoDeCuenta,
    ILimitadorDelCarrito limites,
    IRepositorioDeCarritos carritos,
    IRepositorioDeCheckouts checkouts,
    IGeneradorDeIdentificadores identificadores,
    IReloj reloj,
    IUnidadDeTrabajo unidad,
    EdicionConCheckoutVivo edicionConCheckoutVivo,
    IAuditoriaDelCarrito auditoria)
{
    public IAuditoriaDelCarrito Auditoria => auditoria;

    /// <summary>El máximo que dice el Inventario para un impreso con dato fresco: el de la red, o cero si está agotado. <see langword="null"/> si no se puede confirmar (la línea se acepta, D03).</summary>
    public static int? MaximoConocido(DisponibilidadDeEdicion? disponibilidad) =>
        disponibilidad is { Estado: EstadoDeDisponibilidad.Disponible or EstadoDeDisponibilidad.Agotada } ? Math.Max(0, disponibilidad.VendibleEnLaRed ?? 0) : null;

    public AutorizadorDelCarrito Autorizador => autorizador;

    public EdicionConCheckoutVivo Edicion => edicionConCheckoutVivo;

    public IConsultaDeCatalogo Catalogo => catalogo;

    public IDisponibilidadDeEdiciones Disponibilidad => disponibilidad;

    public IPosesionDeEdiciones Posesion => posesion;

    public ILimitadorDelCarrito Limites => limites;

    public IRepositorioDeCarritos Carritos => carritos;

    public IRepositorioDeCheckouts Checkouts => checkouts;

    public IGeneradorDeIdentificadores Identificadores => identificadores;

    public IReloj Reloj => reloj;

    public IUnidadDeTrabajo Unidad => unidad;

    /// <summary>Si la cuenta puede comprar (no cancelada, correo verificado, textos al día, sin cancelación diferida): lo que condiciona guardar el consentimiento de avisos. Ante una falla de Identidad, no.</summary>
    public async Task<bool> PuedeComprar(Guid cuentaId, CancellationToken cancelacion)
    {
        try
        {
            return await estadoDeCuenta.Consultar(cuentaId, cancelacion).ConfigureAwait(false) is { PuedeComprar: true };
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return false;
        }
    }

    // ---------- Errores ----------

    public static Resultado<T> Fallo<T>(CategoriaDeError categoria, string clave, IReadOnlyList<ErrorDeCampo>? errores = null) => Resultado<T>.Fallo(new ErrorDeAplicacion(categoria, clave, errores));

    public static Resultado<T> Validacion<T>(ResultadoDeValidacion validacion) => Fallo<T>(CategoriaDeError.Validacion, ClavesDelCarrito.ValidacionFallida, validacion.Errores);

    public static Resultado<T> ServicioNoDisponible<T>() => Fallo<T>(CategoriaDeError.NoDisponible, ClavesDelCarrito.ServicioNoDisponible);

    /// <summary>Un límite que no deja pasar: excedido (con el instante de reintento) o el almacén de contadores caído, que falla cerrado (SEG-26).</summary>
    public static ErrorDeAplicacion? ErrorDeLimite(ResultadoDeLimiteDelCarrito limite) => limite.Decision switch
    {
        DecisionDeLimiteDelCarrito.Permitido => null,
        DecisionDeLimiteDelCarrito.Excedido => new ErrorDeAplicacion(
            CategoriaDeError.Limite,
            ClavesDelCarrito.LimiteExcedido,
            limite.ReintentarEn is { } desde ? [new ErrorDeCampo("disponibleDesde", desde.ToString("O", CultureInfo.InvariantCulture))] : null),
        _ => new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDelCarrito.ServicioNoDisponible),
    };

    /// <summary>
    /// La cuenta del cliente debe estar activa: una cancelada, desactivada o inexistente no opera (el token vive hasta 15 minutos), y si Identidad no responde se falla cerrado (CAR-37). No lleva datos personales.
    /// </summary>
    public async Task<ErrorDeAplicacion?> ExigirCuentaActiva(Guid cuentaId, CancellationToken cancelacion)
    {
        EstadoDeCuenta? estado;
        try
        {
            estado = await estadoDeCuenta.Consultar(cuentaId, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDelCarrito.ServicioNoDisponible);
        }

        return estado is { CuentaCanceladaODesactivada: false }
            ? null
            : new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDelCarrito.CuentaNoPuedeOperar);
    }

    // ---------- El carrito de quien pide ----------

    /// <summary>El carrito que ve quien pide, sin bloqueo ni efectos: el de la cuenta, o el del visitante por su cookie (uno caducado, fusionado, desconocido o mal formado es «sin carrito»).</summary>
    public async Task<(CarritoDeDominio? Carrito, bool CookieInvalida)> LeerCarrito(SolicitanteDelCarrito solicitante, string? cookie, CancellationToken cancelacion)
    {
        if (solicitante.Tipo == TipoDeSolicitante.Cuenta)
        {
            return (await carritos.ObtenerDeCuenta(solicitante.CuentaId!.Value, conBloqueo: false, cancelacion).ConfigureAwait(false), false);
        }

        if (!HuellaDeLaCookie.TryDe(cookie, out var huella))
        {
            return (null, cookie is not null);
        }

        var carrito = await carritos.ObtenerDeVisitante(huella!, conBloqueo: false, cancelacion).ConfigureAwait(false);
        return EsUtil(carrito) ? (carrito, false) : (null, true);
    }

    public bool EsUtil(CarritoDeDominio? carrito) => carrito is { Estado: EstadoDeCarrito.Abierto } && !carrito.EstaCaducado(reloj.Ahora);

    // ---------- La vista ----------

    public async Task<VistaDelCarrito> ConstruirVista(CarritoDeDominio? carrito, Guid? cuentaId, IReadOnlyDictionary<Guid, EdicionConsultada>? conocidas, CancellationToken cancelacion)
    {
        if (carrito is null || carrito.Lineas.Count == 0)
        {
            var avisosVacios = cuentaId is { } c ? await carritos.AvisosSinVer(c, cancelacion).ConfigureAwait(false) : [];
            return new VistaDelCarrito([], 0m, avisosVacios, false);
        }

        var ids = carrito.Lineas.Select(l => l.EdicionId).ToList();
        var ediciones = new Dictionary<Guid, EdicionConsultada>(conocidas ?? new Dictionary<Guid, EdicionConsultada>());
        var catalogoFallo = false;
        var faltan = ids.Where(id => !ediciones.ContainsKey(id)).ToList();
        if (faltan.Count > 0)
        {
            var consultadas = await catalogo.ObtenerEdiciones(faltan, cancelacion).ConfigureAwait(false);
            if (consultadas.EsExito)
            {
                foreach (var edicion in consultadas.Valor)
                {
                    ediciones[edicion.EdicionId] = edicion;
                }
            }
            else
            {
                catalogoFallo = true;
            }
        }

        var disponibles = new Dictionary<Guid, DisponibilidadDeEdicion>();
        var consultaDeDisponibilidad = await disponibilidad.Consultar(ids, cancelacion).ConfigureAwait(false);
        if (consultaDeDisponibilidad.EsExito)
        {
            foreach (var d in consultaDeDisponibilidad.Valor)
            {
                disponibles[d.EdicionId] = d;
            }
        }

        var lineas = new List<LineaDelCarritoVista>();
        foreach (var linea in carrito.Lineas)
        {
            ediciones.TryGetValue(linea.EdicionId, out var edicion);
            disponibles.TryGetValue(linea.EdicionId, out var dato);
            // Una edición que el Catálogo no devolvió por un fallo no es una edición retirada: no se marca así, y los precios quedan sin confirmar.
            var retirada = linea.RetiroPendiente || (edicion is null ? !catalogoFallo : edicion.Estado != EstadoDeEdicionConsultado.Activa);
            var precio = edicion?.Precio ?? 0m;
            lineas.Add(new LineaDelCarritoVista(
                linea.EdicionId,
                edicion?.Titulo ?? "Edición no disponible",
                edicion?.Isbn,
                edicion?.Formato ?? (linea.EsDigital ? FormatoDeEdicion.Digital : FormatoDeEdicion.Impresa),
                linea.Cantidad.Valor,
                precio,
                Soles.Importe(precio, linea.Cantidad.Valor),
                dato,
                retirada));
        }

        var subtotal = Soles.Redondear(lineas.Where(l => !l.Retirada).Sum(l => l.Importe));
        var avisos = cuentaId is { } cuenta ? await carritos.AvisosSinVer(cuenta, cancelacion).ConfigureAwait(false) : [];
        var estado = cuentaId is { } propietaria ? await checkouts.LeerEstadoSinBloqueo(propietaria, cancelacion).ConfigureAwait(false) : null;
        var hayCompra = estado is EstadoDelCheckout.Reservando or EstadoDelCheckout.EnPago or EstadoDelCheckout.PagoPendiente;
        return new VistaDelCarrito(lineas, subtotal, avisos, hayCompra, catalogoFallo);
    }

    /// <summary>Convierte las ediciones retiradas con anotación huérfana en avisos persistentes («X fue retirada del catálogo»).</summary>
    public IReadOnlyList<AvisoDelCarrito> AvisosDeRetiro(IReadOnlyList<EdicionRetirada> retiradas, IReadOnlyDictionary<Guid, EdicionConsultada> ediciones, DateTimeOffset ahora) =>
    [
        .. retiradas.Select(r => new AvisoDelCarrito(
            identificadores.NuevoId(),
            TipoDeAvisoDelCarrito.EdicionRetirada,
            r.EdicionId,
            ediciones.TryGetValue(r.EdicionId, out var e) ? e.Titulo : "Edición retirada",
            null,
            null,
            ahora)),
    ];
}

/// <summary>El resultado de armar el terreno de una edición del carrito antes de abrir la transacción.</summary>
internal sealed record TerrenoDeLaEdicion(IReadOnlyDictionary<Guid, EdicionConsultada> Ediciones, DisponibilidadDeEdicion? Disponibilidad);

/// <summary>
/// <c>ObtenerCarrito</c> (CAR-01, CAR-25, CAR-29, AC-CAR-01): el carrito de la cuenta o del visitante con el precio vigente, la disponibilidad y los avisos. <b>No tiene efectos</b>: no crea carrito, no toma límites, no guarda y no
/// mueve la caducidad. Sin carrito (o con una cookie desconocida, mal formada, caducada o de un carrito fusionado) devuelve uno vacío y, para el visitante, la orden de borrar la cookie.
/// </summary>
internal sealed class ObtenerCarrito(ServiciosDelCarrito servicios) : ICasoDeUso<SolicitudSobreElCarrito, ResultadoDeCarrito>
{
    public async Task<Resultado<ResultadoDeCarrito>> Ejecutar(SolicitudSobreElCarrito entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var quien = await servicios.Autorizador.ExigirClienteOVisitante(cancelacion).ConfigureAwait(false);
        if (!quien.EsExito)
        {
            return Resultado<ResultadoDeCarrito>.Fallo(quien.Error!);
        }

        var solicitante = quien.Valor;
        var (carrito, cookieInvalida) = await servicios.LeerCarrito(solicitante, entrada.CookieDelVisitante, cancelacion).ConfigureAwait(false);
        var vista = await servicios.ConstruirVista(carrito, solicitante.CuentaId, null, cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeCarrito>.Exito(new ResultadoDeCarrito(vista, null, solicitante.Tipo == TipoDeSolicitante.Visitante && cookieInvalida));
    }
}

/// <summary>Lo que cambia entre agregar, modificar y quitar una línea: la validación, lo que se obtiene antes de la transacción y la operación sobre el carrito ya bloqueado.</summary>
internal abstract class EdicionDeLineaDelCarrito<TEntrada>(ServiciosDelCarrito servicios) : ICasoDeUso<TEntrada, ResultadoDeCarrito>
    where TEntrada : notnull
{
    protected ServiciosDelCarrito Servicios => servicios;

    protected abstract ResultadoDeValidacion Validar(TEntrada entrada);

    protected abstract string? Cookie(TEntrada entrada);

    /// <summary>Solo agregar crea el carrito del visitante cuando no existe.</summary>
    protected abstract bool PuedeCrearElCarrito { get; }

    /// <summary>
    /// Obtiene del Catálogo, de la disponibilidad y de la posesión lo que la operación necesita, <b>antes</b> de abrir la transacción. Devuelve el error que impide seguir o <see langword="null"/>. Las ediciones que ya están
    /// en el carrito se piden también, para los títulos de los avisos y de la vista.
    /// </summary>
    protected abstract Task<(ErrorDeAplicacion? Error, TerrenoDeLaEdicion Terreno)> Preparar(TEntrada entrada, SolicitanteDelCarrito solicitante, CarritoDeDominio? existente, CancellationToken cancelacion);

    /// <summary>Aplica la operación al carrito bloqueado y devuelve el rechazo, o <see langword="null"/> si quedó aplicada. Debe revalidar contra lo obtenido antes.</summary>
    protected abstract ErrorDeAplicacion? Aplicar(ContextoDeEdicion contexto);

    /// <summary>Un cambio que no cambia nada (quitar una línea que no está, pedir la misma cantidad, agregar una digital que ya está): no reabre la intención ni abre la transacción.</summary>
    protected virtual bool EsNoOp(TEntrada entrada, CarritoDeDominio existente) => false;

    /// <summary>Lo que se puede rechazar con lo ya obtenido (el máximo del stock) <b>antes</b> de reabrir la intención de Pagos: así un rechazo no deja la compra reabierta. Aplicar lo revalida al bloquear.</summary>
    protected virtual ErrorDeAplicacion? Prevalidar(TEntrada entrada, CarritoDeDominio? existente, TerrenoDeLaEdicion terreno) => null;

    protected sealed record ContextoDeEdicion(TEntrada Entrada, CarritoDeDominio Carrito, TerrenoDeLaEdicion Terreno, DateTimeOffset Ahora);

    public async Task<Resultado<ResultadoDeCarrito>> Ejecutar(TEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        // 1. Alcance y entrada: el personal se rechaza sin tocar nada y la cuenta sale del actor.
        var quien = await servicios.Autorizador.ExigirClienteOVisitante(cancelacion).ConfigureAwait(false);
        if (!quien.EsExito)
        {
            return Resultado<ResultadoDeCarrito>.Fallo(quien.Error!);
        }

        var solicitante = quien.Valor;
        var validacion = Validar(entrada);
        if (!validacion.EsValido)
        {
            return ServiciosDelCarrito.Validacion<ResultadoDeCarrito>(validacion);
        }

        // 2. Todo lo que es red, antes de la transacción (D19).
        if (solicitante.Tipo == TipoDeSolicitante.Cuenta)
        {
            if (await servicios.ExigirCuentaActiva(solicitante.CuentaId!.Value, cancelacion).ConfigureAwait(false) is { } cuentaRechazada)
            {
                return Resultado<ResultadoDeCarrito>.Fallo(cuentaRechazada);
            }
        }

        if (ServiciosDelCarrito.ErrorDeLimite(await servicios.Limites.EditarLineas(solicitante.CuentaId, cancelacion).ConfigureAwait(false)) is { } limite)
        {
            return Resultado<ResultadoDeCarrito>.Fallo(limite);
        }

        var cookie = Cookie(entrada);
        var (existente, _) = await servicios.LeerCarrito(solicitante, cookie, cancelacion).ConfigureAwait(false);
        var crearCarrito = existente is null;
        if (crearCarrito && !PuedeCrearElCarrito)
        {
            return await SinCarrito(solicitante, entrada, cookie, cancelacion).ConfigureAwait(false);
        }

        if (crearCarrito && solicitante.Tipo == TipoDeSolicitante.Visitante
            && ServiciosDelCarrito.ErrorDeLimite(await servicios.Limites.CrearCarritoDeVisitante(cancelacion).ConfigureAwait(false)) is { } limiteDeCarritos)
        {
            return Resultado<ResultadoDeCarrito>.Fallo(limiteDeCarritos);
        }

        var preparado = await Preparar(entrada, solicitante, existente, cancelacion).ConfigureAwait(false);
        if (preparado.Error is { } error)
        {
            return Resultado<ResultadoDeCarrito>.Fallo(error);
        }

        if (existente is not null && EsNoOp(entrada, existente))
        {
            var igual = await servicios.ConstruirVista(existente, solicitante.CuentaId, preparado.Terreno.Ediciones, cancelacion).ConfigureAwait(false);
            return Resultado<ResultadoDeCarrito>.Exito(new ResultadoDeCarrito(igual, null, BorrarLaCookie: false));
        }

        if (Prevalidar(entrada, existente, preparado.Terreno) is { } rechazoPrevio)
        {
            return Resultado<ResultadoDeCarrito>.Fallo(rechazoPrevio);
        }

        // La edición con el checkout vivo (T11): Reabrir fuera de la transacción y antes de bloquear; solo para una cuenta con sesión.
        var puerta = PuertaDeEdicion.Libre;
        if (solicitante.Tipo == TipoDeSolicitante.Cuenta)
        {
            var decidida = await servicios.Edicion.PrepararAntesDeBloquear(solicitante.CuentaId!.Value, cancelacion).ConfigureAwait(false);
            if (!decidida.EsExito)
            {
                return Resultado<ResultadoDeCarrito>.Fallo(decidida.Error!);
            }

            puerta = decidida.Valor;
        }

        // 3. La transacción: el checkout primero y el carrito después (D10), sin red.
        (Resultado<CarritoDeDominio> Resultado, string? CookieNueva) transaccion;
        try
        {
            transaccion = await servicios.Unidad.Ejecutar(token => EnLaTransaccion(entrada, solicitante, cookie, preparado.Terreno, puerta, token), cancelacion).ConfigureAwait(false);
        }
        catch (CarritoYaExisteException)
        {
            // Otro dispositivo creó el carrito a la vez (índice único): la transacción se rehace y ahora lo encuentra y lo bloquea.
            transaccion = await servicios.Unidad.Ejecutar(token => EnLaTransaccion(entrada, solicitante, cookie, preparado.Terreno, puerta, token), cancelacion).ConfigureAwait(false);
        }

        var (resultado, cookieNueva) = transaccion;
        if (!resultado.EsExito)
        {
            return Resultado<ResultadoDeCarrito>.Fallo(resultado.Error!);
        }

        // 4. La vista, fuera de la transacción.
        var vista = await servicios.ConstruirVista(resultado.Valor, solicitante.CuentaId, preparado.Terreno.Ediciones, cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeCarrito>.Exito(new ResultadoDeCarrito(vista, cookieNueva, BorrarLaCookie: false));
    }

    private async Task<Resultado<ResultadoDeCarrito>> SinCarrito(SolicitanteDelCarrito solicitante, TEntrada entrada, string? cookie, CancellationToken cancelacion)
    {
        var vacio = await servicios.ConstruirVista(null, solicitante.CuentaId, null, cancelacion).ConfigureAwait(false);
        return SinCarritoRespuesta(vacio, solicitante, cookie, entrada);
    }

    /// <summary>La respuesta cuando no hay carrito y la operación no lo crea: quitar es un éxito vacío; cambiar una línea que no está es «no encontrada».</summary>
    protected abstract Resultado<ResultadoDeCarrito> SinCarritoRespuesta(VistaDelCarrito vacio, SolicitanteDelCarrito solicitante, string? cookie, TEntrada entrada);

    private async Task<(Resultado<CarritoDeDominio> Resultado, string? CookieNueva)> EnLaTransaccion(TEntrada entrada, SolicitanteDelCarrito solicitante, string? cookie, TerrenoDeLaEdicion terreno, PuertaDeEdicion puerta, CancellationToken cancelacion)
    {
        var ahora = servicios.Reloj.Ahora;
        Checkout? checkout = null;
        if (solicitante.Tipo == TipoDeSolicitante.Cuenta)
        {
            // El checkout se bloquea antes que el carrito (D10); la compuerta se aplica con los dos ya bloqueados y el estado releído.
            checkout = await servicios.Checkouts.ObtenerVivoDeCuenta(solicitante.CuentaId!.Value, conBloqueo: true, cancelacion).ConfigureAwait(false);
        }

        string? cookieNueva = null;
        CarritoDeDominio? carrito;
        var esNuevo = false;
        if (solicitante.Tipo == TipoDeSolicitante.Cuenta)
        {
            carrito = await servicios.Carritos.ObtenerDeCuenta(solicitante.CuentaId!.Value, conBloqueo: true, cancelacion).ConfigureAwait(false);
            if (carrito is null && PuedeCrearElCarrito)
            {
                carrito = CarritoDeDominio.Crear(servicios.Identificadores.NuevoId(), PropietarioDelCarrito.DeCuenta(solicitante.CuentaId.Value), ahora);
                esNuevo = true;
            }
        }
        else
        {
            carrito = null;
            if (HuellaDeLaCookie.TryDe(cookie, out var huella))
            {
                var leido = await servicios.Carritos.ObtenerDeVisitante(huella!, conBloqueo: true, cancelacion).ConfigureAwait(false);
                carrito = servicios.EsUtil(leido) ? leido : null;
            }

            if (carrito is null && PuedeCrearElCarrito)
            {
                cookieNueva = servicios.Identificadores.NuevaCookieDeVisitante();
                HuellaDeLaCookie.TryDe(cookieNueva, out var huellaNueva);
                carrito = CarritoDeDominio.Crear(servicios.Identificadores.NuevoId(), PropietarioDelCarrito.DeVisitante(huellaNueva!), ahora);
                esNuevo = true;
            }
        }

        if (carrito is null)
        {
            return (ServiciosDelCarrito.Fallo<CarritoDeDominio>(CategoriaDeError.NoEncontrado, ClavesDelCarrito.LineaNoEncontrada), null);
        }

        // La compuerta del checkout vivo (T11): con los dos bloqueados, aplica lo decidido antes de bloquear o rechaza si el checkout cambió entre tanto.
        var (cerrada, retiradasAlEditar) = servicios.Edicion.AplicarDentroDeLaTransaccion(checkout, carrito, puerta, ahora);
        if (cerrada is not null)
        {
            return (Resultado<CarritoDeDominio>.Fallo(cerrada), null);
        }

        // Una anotación de retiro huérfana (sin checkout vivo, o con el checkout ya en edición) la quita la primera lectura con bloqueo del carrito, dejando el aviso.
        IReadOnlyList<EdicionRetirada> huerfanas = [.. retiradasAlEditar, .. carrito.QuitarRetirosHuerfanos(checkout?.Estado, ahora)];

        var rechazo = Aplicar(new ContextoDeEdicion(entrada, carrito, terreno, ahora));

        // Con la intención ya reabierta (irreversible) o con retiros pendientes aplicados, lo que cambió se guarda aunque la operación se rechace: la unidad de trabajo confirma un fallo esperado y, sin esto, el checkout quedaría en pago con una intención ya inservible. Sin efectos previos, un rechazo no guarda nada.
        var huboEfectosPrevios = puerta.Decision != DecisionDeLaPuerta.Libre || huerfanas.Count > 0;
        if (rechazo is not null && !huboEfectosPrevios)
        {
            return (Resultado<CarritoDeDominio>.Fallo(rechazo), null);
        }

        if (checkout is not null && puerta.Decision != DecisionDeLaPuerta.Libre)
        {
            await servicios.Checkouts.Guardar(checkout, cancelacion).ConfigureAwait(false);
        }

        if (esNuevo)
        {
            await servicios.Carritos.Agregar(carrito, cancelacion).ConfigureAwait(false);
        }
        else
        {
            await servicios.Carritos.Guardar(carrito, cancelacion).ConfigureAwait(false);
        }

        if (huerfanas.Count > 0 && solicitante.CuentaId is { } cuenta)
        {
            await servicios.Carritos.AgregarAvisos(cuenta, servicios.AvisosDeRetiro(huerfanas, terreno.Ediciones, ahora), cancelacion).ConfigureAwait(false);
        }

        return (rechazo is null ? Resultado<CarritoDeDominio>.Exito(carrito) : Resultado<CarritoDeDominio>.Fallo(rechazo), cookieNueva);
    }

    protected static ErrorDeAplicacion DeRechazo(RechazoDeCarrito rechazo) => rechazo switch
    {
        RechazoDeCarrito.CantidadInvalida => new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDelCarrito.ValidacionFallida, [new ErrorDeCampo("cantidad", ConstructorDeValidacion.FueraDeRango)]),
        RechazoDeCarrito.TopeDeLineas => new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDelCarrito.LimiteDeLineas),
        RechazoDeCarrito.TopeDeUnidades => new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDelCarrito.LimiteDeUnidades),
        RechazoDeCarrito.EdicionNoEstaEnElCarrito => new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, ClavesDelCarrito.LineaNoEncontrada),
        RechazoDeCarrito.EdicionRetirada => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.EdicionNoDisponible),
        _ => new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, ClavesDelCarrito.NoEncontrado),
    };

    protected static ErrorDeAplicacion SinStock(int maximo) =>
        new(CategoriaDeError.Conflicto, ClavesDelCarrito.SinStock, [new ErrorDeCampo("maximo", maximo.ToString(CultureInfo.InvariantCulture))]);

    protected static int? MaximoConocido(DisponibilidadDeEdicion? disponibilidad) => ServiciosDelCarrito.MaximoConocido(disponibilidad);

    protected async Task<IReadOnlyDictionary<Guid, EdicionConsultada>?> EdicionesDeLaVista(Guid edicionId, CarritoDeDominio? existente, CancellationToken cancelacion)
    {
        var ids = new HashSet<Guid> { edicionId };
        if (existente is not null)
        {
            ids.UnionWith(existente.Lineas.Select(l => l.EdicionId));
        }

        var consulta = await servicios.Catalogo.ObtenerEdiciones(ids, cancelacion).ConfigureAwait(false);
        return consulta.EsExito ? consulta.Valor.ToDictionary(e => e.EdicionId) : null;
    }
}

/// <summary>
/// <c>AgregarLinea</c> (CAR-02, CAR-03, CAR-04, CAR-20; AC-CAR-02, AC-CAR-03, AC-CAR-18, AC-CAR-25): suma una edición de 1 a 99 (la digital queda en una), rechaza la que el Catálogo no ofrece, la digital que la cuenta ya posee
/// (solo con sesión) y más unidades de un impreso que el stock vendible de la red indicando el máximo; si no se puede confirmar el stock o la posesión, la línea se acepta (D03). Crea el carrito del visitante con su cookie.
/// </summary>
internal sealed class AgregarLinea(ServiciosDelCarrito servicios) : EdicionDeLineaDelCarrito<AgregarLineaEntrada>(servicios)
{
    protected override bool PuedeCrearElCarrito => true;

    protected override string? Cookie(AgregarLineaEntrada entrada) => entrada.CookieDelVisitante;

    protected override ResultadoDeValidacion Validar(AgregarLineaEntrada entrada) => ValidadorDeLineas.Validar(entrada.EdicionId, entrada.Cantidad);

    protected override Resultado<ResultadoDeCarrito> SinCarritoRespuesta(VistaDelCarrito vacio, SolicitanteDelCarrito solicitante, string? cookie, AgregarLineaEntrada entrada) =>
        throw new InvalidOperationException("Agregar siempre puede crear el carrito.");

    protected override async Task<(ErrorDeAplicacion? Error, TerrenoDeLaEdicion Terreno)> Preparar(AgregarLineaEntrada entrada, SolicitanteDelCarrito solicitante, CarritoDeDominio? existente, CancellationToken cancelacion)
    {
        var vacio = new TerrenoDeLaEdicion(new Dictionary<Guid, EdicionConsultada>(), null);
        var consulta = await Servicios.Catalogo.ObtenerEdiciones(Ids(entrada.EdicionId, existente), cancelacion).ConfigureAwait(false);
        if (!consulta.EsExito)
        {
            return (new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDelCarrito.ServicioNoDisponible), vacio);
        }

        var ediciones = consulta.Valor.ToDictionary(e => e.EdicionId);
        if (!ediciones.TryGetValue(entrada.EdicionId, out var edicion) || edicion.Estado != EstadoDeEdicionConsultado.Activa || edicion.MotivoDeNoDisponible is not null)
        {
            return (new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.EdicionNoDisponible), new TerrenoDeLaEdicion(ediciones, null));
        }

        if (edicion.Formato == FormatoDeEdicion.Digital)
        {
            if (solicitante.Tipo == TipoDeSolicitante.Cuenta
                && await Servicios.Posesion.Posee(solicitante.CuentaId!.Value, [entrada.EdicionId], cancelacion).ConfigureAwait(false) is { Estado: EstadoDePosesion.Posee } poseida
                && poseida.Poseidas.Contains(entrada.EdicionId))
            {
                return (new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.DigitalYaPoseida), new TerrenoDeLaEdicion(ediciones, null));
            }

            return (null, new TerrenoDeLaEdicion(ediciones, null));
        }

        var disponibilidad = await Servicios.Disponibilidad.Consultar([entrada.EdicionId], cancelacion).ConfigureAwait(false);
        var dato = disponibilidad.EsExito ? disponibilidad.Valor.FirstOrDefault(d => d.EdicionId == entrada.EdicionId) : null;
        return (null, new TerrenoDeLaEdicion(ediciones, dato));
    }

    protected override bool EsNoOp(AgregarLineaEntrada entrada, CarritoDeDominio existente) =>
        existente.Lineas.Any(l => l.EdicionId == entrada.EdicionId && l.EsDigital && !l.RetiroPendiente);

    protected override ErrorDeAplicacion? Prevalidar(AgregarLineaEntrada entrada, CarritoDeDominio? existente, TerrenoDeLaEdicion terreno)
    {
        var esDigital = terreno.Ediciones.TryGetValue(entrada.EdicionId, out var edicion) && edicion.Formato == FormatoDeEdicion.Digital;
        if (esDigital || MaximoConocido(terreno.Disponibilidad) is not { } maximo)
        {
            return null;
        }

        var yaHay = existente?.Lineas.FirstOrDefault(l => l.EdicionId == entrada.EdicionId)?.Cantidad.Valor ?? 0;
        return yaHay + entrada.Cantidad > maximo ? SinStock(maximo) : null;
    }

    protected override ErrorDeAplicacion? Aplicar(ContextoDeEdicion contexto)
    {
        var ediciones = contexto.Terreno.Ediciones;
        var esDigital = ediciones.TryGetValue(contexto.Entrada.EdicionId, out var edicion) && edicion.Formato == FormatoDeEdicion.Digital;

        // El máximo se revalida al bloquear, contra lo que ya hay en el carrito (otro dispositivo pudo sumar mientras tanto).
        if (!esDigital && MaximoConocido(contexto.Terreno.Disponibilidad) is { } maximo)
        {
            var yaHay = contexto.Carrito.Lineas.FirstOrDefault(l => l.EdicionId == contexto.Entrada.EdicionId)?.Cantidad.Valor ?? 0;
            if (yaHay + contexto.Entrada.Cantidad > maximo)
            {
                return SinStock(maximo);
            }
        }

        var resultado = contexto.Carrito.Agregar(contexto.Entrada.EdicionId, esDigital, contexto.Entrada.Cantidad, contexto.Ahora);
        return resultado.EsExito ? null : DeRechazo(resultado.Rechazo!.Value);
    }

    private static HashSet<Guid> Ids(Guid edicionId, CarritoDeDominio? existente)
    {
        var ids = new HashSet<Guid> { edicionId };
        if (existente is not null)
        {
            ids.UnionWith(existente.Lineas.Select(l => l.EdicionId));
        }

        return ids;
    }
}

/// <summary>
/// <c>ModificarLinea</c> (CAR-02, CAR-04): cambia la cantidad de una línea. Subir por encima del stock vendible se rechaza con el máximo; bajar siempre se puede. Una línea que no está es «no encontrada»; una línea con
/// retiro pendiente no se puede cambiar (solo quitar).
/// </summary>
internal sealed class ModificarLinea(ServiciosDelCarrito servicios) : EdicionDeLineaDelCarrito<ModificarLineaEntrada>(servicios)
{
    protected override bool PuedeCrearElCarrito => false;

    protected override string? Cookie(ModificarLineaEntrada entrada) => entrada.CookieDelVisitante;

    protected override ResultadoDeValidacion Validar(ModificarLineaEntrada entrada) => ValidadorDeLineas.Validar(entrada.EdicionId, entrada.Cantidad);

    protected override Resultado<ResultadoDeCarrito> SinCarritoRespuesta(VistaDelCarrito vacio, SolicitanteDelCarrito solicitante, string? cookie, ModificarLineaEntrada entrada) =>
        ServiciosDelCarrito.Fallo<ResultadoDeCarrito>(CategoriaDeError.NoEncontrado, ClavesDelCarrito.LineaNoEncontrada);

    protected override async Task<(ErrorDeAplicacion? Error, TerrenoDeLaEdicion Terreno)> Preparar(ModificarLineaEntrada entrada, SolicitanteDelCarrito solicitante, CarritoDeDominio? existente, CancellationToken cancelacion)
    {
        var ediciones = await EdicionesDeLaVista(entrada.EdicionId, existente, cancelacion).ConfigureAwait(false) ?? new Dictionary<Guid, EdicionConsultada>();
        var esImpresa = existente?.Lineas.Any(l => l.EdicionId == entrada.EdicionId && !l.EsDigital) ?? false;
        if (!esImpresa)
        {
            return (null, new TerrenoDeLaEdicion(ediciones, null));
        }

        var disponibilidad = await Servicios.Disponibilidad.Consultar([entrada.EdicionId], cancelacion).ConfigureAwait(false);
        var dato = disponibilidad.EsExito ? disponibilidad.Valor.FirstOrDefault(d => d.EdicionId == entrada.EdicionId) : null;
        return (null, new TerrenoDeLaEdicion(ediciones, dato));
    }

    protected override bool EsNoOp(ModificarLineaEntrada entrada, CarritoDeDominio existente) =>
        existente.Lineas.FirstOrDefault(l => l.EdicionId == entrada.EdicionId) is { RetiroPendiente: false } linea
        && linea.Cantidad.Valor == (linea.EsDigital ? 1 : entrada.Cantidad);

    protected override ErrorDeAplicacion? Prevalidar(ModificarLineaEntrada entrada, CarritoDeDominio? existente, TerrenoDeLaEdicion terreno) =>
        existente?.Lineas.FirstOrDefault(l => l.EdicionId == entrada.EdicionId) is { EsDigital: false } linea
        && entrada.Cantidad > linea.Cantidad.Valor
        && MaximoConocido(terreno.Disponibilidad) is { } maximo
        && entrada.Cantidad > maximo
            ? SinStock(maximo)
            : null;

    protected override ErrorDeAplicacion? Aplicar(ContextoDeEdicion contexto)
    {
        var linea = contexto.Carrito.Lineas.FirstOrDefault(l => l.EdicionId == contexto.Entrada.EdicionId);
        if (linea is { EsDigital: false } && contexto.Entrada.Cantidad > linea.Cantidad.Valor && MaximoConocido(contexto.Terreno.Disponibilidad) is { } maximo && contexto.Entrada.Cantidad > maximo)
        {
            return SinStock(maximo);
        }

        var resultado = contexto.Carrito.Modificar(contexto.Entrada.EdicionId, contexto.Entrada.Cantidad, contexto.Ahora);
        return resultado.EsExito ? null : DeRechazo(resultado.Rechazo!.Value);
    }
}

/// <summary>
/// <c>EliminarLinea</c> (CAR-02): quita una línea; repetirlo, o quitar sin carrito, es un éxito sin cambios (idempotente). Es la operación que se permite sobre una línea con retiro pendiente.
/// </summary>
internal sealed class EliminarLinea(ServiciosDelCarrito servicios) : EdicionDeLineaDelCarrito<EliminarLineaEntrada>(servicios)
{
    protected override bool PuedeCrearElCarrito => false;

    protected override string? Cookie(EliminarLineaEntrada entrada) => entrada.CookieDelVisitante;

    protected override ResultadoDeValidacion Validar(EliminarLineaEntrada entrada) => ValidadorDeLineas.Validar(entrada.EdicionId, cantidad: null);

    protected override Resultado<ResultadoDeCarrito> SinCarritoRespuesta(VistaDelCarrito vacio, SolicitanteDelCarrito solicitante, string? cookie, EliminarLineaEntrada entrada) =>
        Resultado<ResultadoDeCarrito>.Exito(new ResultadoDeCarrito(vacio, null, solicitante.Tipo == TipoDeSolicitante.Visitante && cookie is not null));

    protected override async Task<(ErrorDeAplicacion? Error, TerrenoDeLaEdicion Terreno)> Preparar(EliminarLineaEntrada entrada, SolicitanteDelCarrito solicitante, CarritoDeDominio? existente, CancellationToken cancelacion)
    {
        var ediciones = await EdicionesDeLaVista(entrada.EdicionId, existente, cancelacion).ConfigureAwait(false) ?? new Dictionary<Guid, EdicionConsultada>();
        return (null, new TerrenoDeLaEdicion(ediciones, null));
    }

    protected override bool EsNoOp(EliminarLineaEntrada entrada, CarritoDeDominio existente) => existente.Lineas.All(l => l.EdicionId != entrada.EdicionId);

    protected override ErrorDeAplicacion? Aplicar(ContextoDeEdicion contexto)
    {
        var resultado = contexto.Carrito.Eliminar(contexto.Entrada.EdicionId, contexto.Ahora);
        return resultado.EsExito || resultado.Rechazo == RechazoDeCarrito.EdicionNoEstaEnElCarrito ? null : DeRechazo(resultado.Rechazo!.Value);
    }
}

/// <summary>La validación de la entrada de una línea (SEG-01): la edición no puede ser vacía y la cantidad va de 1 a 99. Los errores llevan el campo y una clave, nunca el valor recibido.</summary>
internal static class ValidadorDeLineas
{
    public static ResultadoDeValidacion Validar(Guid edicionId, int? cantidad)
    {
        var errores = new List<ErrorDeCampo>();
        if (edicionId == Guid.Empty)
        {
            errores.Add(new ErrorDeCampo("edicionId", ConstructorDeValidacion.Obligatorio));
        }

        if (cantidad is { } valor && valor is < Cantidad.Minima or > Cantidad.Maxima)
        {
            errores.Add(new ErrorDeCampo("cantidad", ConstructorDeValidacion.FueraDeRango));
        }

        return errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
    }
}

/// <summary>Los avisos que la persona marca como vistos (CAT-18, D15): hasta 50 identificadores, solo de su cuenta (el repositorio los filtra por la cuenta del actor, nunca por una cuenta recibida).</summary>
public sealed record MarcarAvisosEntrada(IReadOnlyCollection<Guid> AvisoIds);

/// <summary>
/// <c>MarcarAvisosComoVistos</c> (CAT-18): marca los avisos del carrito de la cuenta como vistos (los de retiro y de ajuste por fusión), en una transacción corta; un identificador ajeno o inexistente no hace nada ni revela si
/// existe. Solo un cliente con sesión: un visitante no tiene avisos. Devuelve el carrito para que la pantalla se redibuje sin los avisos vistos.
/// </summary>
internal sealed class MarcarAvisosComoVistos(ServiciosDelCarrito servicios) : ICasoDeUso<MarcarAvisosEntrada, ResultadoDeCarrito>
{
    public const int MaximoDeAvisos = 50;

    public async Task<Resultado<ResultadoDeCarrito>> Ejecutar(MarcarAvisosEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var cuenta = await servicios.Autorizador.ExigirCuenta(cancelacion).ConfigureAwait(false);
        if (!cuenta.EsExito)
        {
            return Resultado<ResultadoDeCarrito>.Fallo(cuenta.Error!);
        }

        if (entrada.AvisoIds is null || entrada.AvisoIds.Count is 0 or > MaximoDeAvisos || entrada.AvisoIds.Any(id => id == Guid.Empty))
        {
            return ServiciosDelCarrito.Validacion<ResultadoDeCarrito>(ResultadoDeValidacion.Invalido([new ErrorDeCampo("avisoIds", ConstructorDeValidacion.FueraDeRango)]));
        }

        var ahora = servicios.Reloj.Ahora;
        await servicios.Unidad.Ejecutar(
            async token =>
            {
                await servicios.Carritos.MarcarAvisosComoVistos(cuenta.Valor, [.. entrada.AvisoIds.Distinct()], ahora, token).ConfigureAwait(false);
                return true;
            },
            cancelacion).ConfigureAwait(false);

        var carrito = await servicios.Carritos.ObtenerDeCuenta(cuenta.Valor, conBloqueo: false, cancelacion).ConfigureAwait(false);
        var vista = await servicios.ConstruirVista(carrito, cuenta.Valor, null, cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeCarrito>.Exito(new ResultadoDeCarrito(vista, null, BorrarLaCookie: false));
    }
}
