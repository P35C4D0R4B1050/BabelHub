using System.Globalization;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

internal static class MapeoDeReembolsos
{
    public static OrigenDeReembolso ADominio(this OrigenDelReembolso origen) => Enum.Parse<OrigenDeReembolso>(origen.ToString());

    public static OrigenDelReembolso APublico(this OrigenDeReembolso origen) => Enum.Parse<OrigenDelReembolso>(origen.ToString());

    public static EstadoDelReembolso APublico(this EstadoDeReembolso estado) => Enum.Parse<EstadoDelReembolso>(estado.ToString());

    public static ReembolsoPendienteVista AVista(this Reembolso r) =>
        new(r.Id, r.Origen.APublico(), r.Monto.Valor, r.Estado.APublico(), r.MotivoPendiente is { } m ? Enum.Parse<MotivoPendienteDelReembolso>(m.ToString()) : null, r.CreadoEn, r.Intentos);
}

/// <summary>
/// Registrar un reembolso (PAG-10, D08): <b>solo inserta</b>, bloqueando la intención, dentro de la transacción de quien lo pide (Pedidos, Devoluciones, Carrito o la compensación). Es idempotente por origen y clave natural
/// (repetir con el mismo monto devuelve el original; con otro, se rechaza) y valida el tope por cargo y por línea contra los demás reembolsos del mismo cargo. Audita en la misma transacción, sin datos de pago.
/// </summary>
internal sealed class RegistrarReembolso(
    IRepositorioDePagos pagos,
    IRepositorioDeReembolsos reembolsos,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IReloj reloj) : ICasoDeUso<RegistrarReembolsoEntrada, ReembolsoRegistrado>
{
    public Task<Resultado<ReembolsoRegistrado>> Ejecutar(RegistrarReembolsoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (Validar(entrada) is { } invalido)
        {
            return Task.FromResult(invalido);
        }

        return unidad.Ejecutar(token => RegistrarEnLaTransaccion(entrada, token), cancelacion);
    }

    /// <summary>Para quien ya tiene su transacción abierta (la compensación): inserta el reembolso del cargo completo de un intento, idempotente por <c>comp:&lt;intentoId&gt;</c>. La intención ya viene bloqueada.</summary>
    public async Task<Reembolso> RegistrarDeCompensacion(IntentoDeCobro intento, CancellationToken token)
    {
        var clave = ClaveDeReembolso.Compensacion(intento.Id);
        if (await reembolsos.BuscarPorOrigen(OrigenDeReembolso.Compensacion, clave, token).ConfigureAwait(false) is { } existente)
        {
            return existente;
        }

        var reembolso = Reembolso.CrearDeCargo(Guid.NewGuid(), OrigenDeReembolso.Compensacion, clave, intento.IntencionId, intento.Id, intento.Importe, reloj.Ahora);
        await reembolsos.Agregar(reembolso, token).ConfigureAwait(false);
        await Auditar(intento.IntencionId, reembolso, token).ConfigureAwait(false);
        return reembolso;
    }

    private async Task<Resultado<ReembolsoRegistrado>> RegistrarEnLaTransaccion(RegistrarReembolsoEntrada entrada, CancellationToken token)
    {
        // Quien registra ya tiene su propia transacción (Pedidos, Devoluciones, Carrito) y su orden de bloqueo: aquí solo se bloquea la intención, sin tomar la marca de la cuenta (cuenta → pedido → intención).
        var intencion = await pagos.ObtenerIntencion(entrada.PedidoId, paraActualizar: true, token).ConfigureAwait(false);
        if (intencion is null)
        {
            return FallosDePagos.De<ReembolsoRegistrado>(CategoriaDeError.NoEncontrado, ClavesDePagos.NoEncontrado);
        }

        // El cargo que se reembolsa es el intento aprobado que originó el pedido.
        var cargo = await pagos.ObtenerIntentoActivo(intencion.Id, token).ConfigureAwait(false);
        if (cargo is not { Estado: EstadoDeIntento.Aprobado } || intencion.Estado != EstadoDeIntencion.Completada)
        {
            return FallosDePagos.De<ReembolsoRegistrado>(CategoriaDeError.Conflicto, ClavesDePagos.SinCargoQueReembolsar);
        }

        var origen = entrada.Origen.ADominio();
        var clave = entrada.ClaveNatural.Trim();
        var envio = Importe.De(entrada.Envio);
        var lineas = entrada.Lineas.Select(l => new LineaDeReembolso(l.LineaId, Importe.De(l.Monto))).ToArray();
        var nuevo = Reembolso.Crear(Guid.NewGuid(), origen, clave, intencion.Id, cargo.Id, lineas, envio, reloj.Ahora);

        var existente = await reembolsos.BuscarPorOrigen(origen, clave, token).ConfigureAwait(false);
        switch (ReglasDeReembolso.ResolverSolicitud(existente, origen, clave, nuevo.Monto))
        {
            case ResultadoDeSolicitud.Repetida:
                return Resultado<ReembolsoRegistrado>.Exito(new ReembolsoRegistrado(existente!.Id, existente.Monto.Valor, existente.Estado.APublico(), EraRepetido: true));
            case ResultadoDeSolicitud.MontoDistinto:
                return FallosDePagos.De<ReembolsoRegistrado>(CategoriaDeError.Conflicto, ClavesDePagos.ReembolsoDistinto);
        }

        var importesDeLasLineas = intencion.Lineas.ToDictionary(l => l.LineaId, l => l.Importe);
        var existentes = await reembolsos.ListarDeLaIntencion(intencion.Id, token).ConfigureAwait(false);
        switch (ReglasDeReembolso.ValidarTope(cargo.Importe, intencion.Envio, importesDeLasLineas, existentes, nuevo))
        {
            case ResultadoDeTope.LineaDesconocida:
                return FallosDePagos.De<ReembolsoRegistrado>(CategoriaDeError.Validacion, ClavesDePagos.ValidacionFallida, [new ErrorDeCampo("lineas", "desconocida")]);
            case not ResultadoDeTope.Dentro:
                return FallosDePagos.De<ReembolsoRegistrado>(CategoriaDeError.Conflicto, ClavesDePagos.ReembolsoSuperaElTope);
        }

        await reembolsos.Agregar(nuevo, token).ConfigureAwait(false);
        await Auditar(intencion.Id, nuevo, token).ConfigureAwait(false);
        return Resultado<ReembolsoRegistrado>.Exito(new ReembolsoRegistrado(nuevo.Id, nuevo.Monto.Valor, nuevo.Estado.APublico(), EraRepetido: false));
    }

    private Task Auditar(Guid pedidoId, Reembolso reembolso, CancellationToken token) =>
        auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.ReembolsoRegistrado)
            {
                ObjetoTipo = "pedido",
                ObjetoId = pedidoId.ToString("D"),
                Detalle = new Dictionary<string, string?>
                {
                    ["monto"] = reembolso.Monto.Valor.ToString("0.00", CultureInfo.InvariantCulture),
                    ["moneda"] = Importe.Moneda,
                    ["modalidad"] = reembolso.Origen == OrigenDeReembolso.Compensacion ? "automatico" : "manual",
                },
            },
            token);

    private static Resultado<ReembolsoRegistrado>? Validar(RegistrarReembolsoEntrada entrada)
    {
        var errores = new List<ErrorDeCampo>();
        if (entrada.PedidoId == Guid.Empty)
        {
            errores.Add(new ErrorDeCampo("pedidoId", "invalido"));
        }

        // La compensación reembolsa el cargo completo por su propia vía: no se pide desde fuera.
        if (!Enum.IsDefined(entrada.Origen) || entrada.Origen == OrigenDelReembolso.Compensacion)
        {
            errores.Add(new ErrorDeCampo("origen", "invalido"));
        }
        else if (!ClaveNaturalValida(entrada.Origen, entrada.ClaveNatural))
        {
            errores.Add(new ErrorDeCampo("claveNatural", "invalida"));
        }

        if (entrada.Lineas is null || entrada.Lineas.Any(l => l is null || l.LineaId == Guid.Empty || l.Monto <= 0m || decimal.Round(l.Monto, 2) != l.Monto) || entrada.Lineas.Select(l => l.LineaId).Distinct().Count() != entrada.Lineas.Count)
        {
            errores.Add(new ErrorDeCampo("lineas", "invalidas"));
        }

        if (entrada.Envio < 0m || decimal.Round(entrada.Envio, 2) != entrada.Envio)
        {
            errores.Add(new ErrorDeCampo("envio", "invalido"));
        }
        else if (errores.All(e => e.Campo != "lineas") && entrada.Lineas!.Count == 0 && entrada.Envio == 0m)
        {
            errores.Add(new ErrorDeCampo("lineas", "vacias"));
        }

        return errores.Count == 0 ? null : FallosDePagos.De<ReembolsoRegistrado>(CategoriaDeError.Validacion, ClavesDePagos.ValidacionFallida, errores);
    }

    /// <summary>Cada origen tiene su forma de clave (D08): <c>anul:</c>, <c>canc:</c> o <c>dev:</c> con identificadores reales, de hasta 100 caracteres.</summary>
    private static bool ClaveNaturalValida(OrigenDelReembolso origen, string? clave)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Trim().Length > Reembolso.LongitudMaximaDeLaClave)
        {
            return false;
        }

        var partes = clave.Trim().Split(':');
        return origen switch
        {
            OrigenDelReembolso.Anulacion => partes is ["anul", var id] && Guid.TryParseExact(id, "D", out _),
            OrigenDelReembolso.CancelacionDelCliente => partes is ["canc", var id] && Guid.TryParseExact(id, "D", out _),
            OrigenDelReembolso.Devolucion => partes is ["dev", var devolucion, var linea] && Guid.TryParseExact(devolucion, "D", out _) && Guid.TryParseExact(linea, "D", out _),
            _ => false,
        };
    }
}

/// <summary>
/// Procesar un reembolso (PAG-10, D08, D03): <b>tres tiempos</b>. (1) Una transacción corta que lo reclama con una transición condicional (<c>pendiente → solicitado</c>, respetando la espera tras un <c>sin_respuesta</c>); (2) la
/// llamada a la pasarela <b>fuera de toda transacción</b>, enviando el <c>reembolsos.id</c> en los metadatos y, en un reintento, consultando antes los reembolsos del cargo para <b>no pedir otra vez a ciegas</b> (si no se puede
/// consultar, queda pendiente); (3) una transacción corta que registra lo que pasó. Una falla tras pedirlo deja el reembolso <c>solicitado</c> (huérfano) para que el trabajo lo reanude consultando a la pasarela.
/// </summary>
internal sealed class ProcesarReembolso(
    IRepositorioDePagos pagos,
    IRepositorioDeReembolsos reembolsos,
    IUnidadDeTrabajo unidad,
    IPasarelaDePago pasarela,
    IAuditoria auditoria,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj,
    OpcionesDelCobro opciones) : ICasoDeUso<Guid, EstadoDelReembolso>
{
    public const string MotivoDeAlertaReembolsoSinRegistrar = "reembolso-pedido-sin-registrar";

    public async Task<Resultado<EstadoDelReembolso>> Ejecutar(Guid reembolsoId, CancellationToken cancelacion = default)
    {
        // (1) Reclamar.
        var reclamo = await unidad.Ejecutar(token => Reclamar(reembolsoId, token), cancelacion).ConfigureAwait(false);
        if (reclamo.Reembolso is null)
        {
            return FallosDePagos.De<EstadoDelReembolso>(CategoriaDeError.NoEncontrado, ClavesDePagos.NoEncontrado);
        }

        if (!reclamo.Reclamado)
        {
            // Ya concluido, en manos de otro ejecutor o esperando tras un sin_respuesta: no se pide nada.
            return Resultado<EstadoDelReembolso>.Exito(reclamo.Reembolso.Estado.APublico());
        }

        // (2) La pasarela, fuera de toda transacción y sin depender de la cancelación del llamador.
        var resultado = await PedirALaPasarela(reclamo.Reembolso, reclamo.ReferenciaDelCargo!).ConfigureAwait(false);

        // (3) Registrar lo que pasó.
        try
        {
            return await unidad.Ejecutar(token => Registrar(reembolsoId, resultado, token), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La pasarela pudo haberlo aplicado: queda solicitado (huérfano) y lo reanuda el trabajo consultando a la pasarela.
            await Alertar(MotivoDeAlertaReembolsoSinRegistrar).ConfigureAwait(false);
            return Resultado<EstadoDelReembolso>.Exito(EstadoDelReembolso.Solicitado);
        }
    }

    /// <summary>
    /// Reanuda un reembolso <c>solicitado</c> huérfano (el proceso cayó tras pedirlo): <b>consulta a la pasarela</b> qué aplicó de ese cargo. Si ya lo aplicó, lo marca reembolsado sin pedir otro; si no, vuelve a pendiente para que el
    /// reintento lo pida (consultando antes, sin pedir a ciegas); y si no se puede consultar, lo deja como está. Fuera de toda transacción la consulta.
    /// </summary>
    public async Task<Resultado<EstadoDelReembolso>> Reanudar(Guid reembolsoId, CancellationToken cancelacion = default)
    {
        var (reembolso, referencia) = await unidad.Ejecutar(
            async token =>
            {
                var r = await reembolsos.Obtener(reembolsoId, paraActualizar: false, token).ConfigureAwait(false);
                var cargo = r is null ? null : await pagos.ObtenerIntento(r.IntentoId, token).ConfigureAwait(false);
                return (r, cargo?.ReferenciaDePasarela);
            },
            cancelacion).ConfigureAwait(false);
        if (reembolso is null)
        {
            return FallosDePagos.De<EstadoDelReembolso>(CategoriaDeError.NoEncontrado, ClavesDePagos.NoEncontrado);
        }

        if (!reembolso.EsHuerfano(reloj.Ahora) || referencia is null)
        {
            return Resultado<EstadoDelReembolso>.Exito(reembolso.Estado.APublico());
        }

        IReadOnlyList<ReembolsoEnPasarela>? existentes;
        using (var plazo = new CancellationTokenSource(opciones.TiempoDeRespuesta))
        {
            try
            {
                existentes = await pasarela.ConsultarReembolsos(referencia, plazo.Token).ConfigureAwait(false);
            }
            catch (Exception) when (!cancelacion.IsCancellationRequested)
            {
                existentes = null;
            }
        }

        if (existentes is null)
        {
            return Resultado<EstadoDelReembolso>.Exito(EstadoDelReembolso.Solicitado);
        }

        return await unidad.Ejecutar(
            async token =>
            {
                var actual = (await reembolsos.Obtener(reembolsoId, paraActualizar: true, token).ConfigureAwait(false))!;
                if (existentes.FirstOrDefault(e => e.ReembolsoId == reembolsoId) is { } aplicado)
                {
                    actual.MarcarReembolsado(aplicado.Referencia, reloj.Ahora);
                }
                else
                {
                    actual.DejarPendiente(MotivoPendienteDeReembolso.SinRespuesta, reloj.Ahora);
                }

                await reembolsos.Guardar(actual, token).ConfigureAwait(false);
                await AuditarResolucion(actual, token).ConfigureAwait(false);
                return Resultado<EstadoDelReembolso>.Exito(actual.Estado.APublico());
            },
            cancelacion).ConfigureAwait(false);
    }

    /// <summary>Audita en la misma transacción cómo quedó el reembolso (reembolsado o pendiente con su motivo de lista cerrada), sin datos de pago; el actor es el administrador central o el sistema.</summary>
    private Task AuditarResolucion(Reembolso reembolso, CancellationToken token)
    {
        var detalle = new Dictionary<string, string?> { ["resultado"] = reembolso.Estado == EstadoDeReembolso.Reembolsado ? "reembolsado" : "pendiente" };
        if (reembolso.Estado != EstadoDeReembolso.Reembolsado && reembolso.MotivoPendiente is { } motivo)
        {
            detalle["motivo"] = motivo switch
            {
                MotivoPendienteDeReembolso.RechazadoPorPasarela => "rechazado-por-pasarela",
                MotivoPendienteDeReembolso.NoSoportado => "no-soportado",
                _ => "sin-respuesta",
            };
        }

        return auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.ReembolsoResuelto)
            {
                ObjetoTipo = "pedido",
                ObjetoId = reembolso.IntencionId.ToString("D"),
                Detalle = detalle,
            },
            token);
    }

    private sealed record Reclamo(Reembolso? Reembolso, bool Reclamado, string? ReferenciaDelCargo);

    private async Task<Reclamo> Reclamar(Guid reembolsoId, CancellationToken token)
    {
        var reembolso = await reembolsos.Obtener(reembolsoId, paraActualizar: true, token).ConfigureAwait(false);
        if (reembolso is null)
        {
            return new Reclamo(null, false, null);
        }

        var cargo = await pagos.ObtenerIntento(reembolso.IntentoId, token).ConfigureAwait(false);
        if (cargo?.ReferenciaDePasarela is not { Length: > 0 } referencia)
        {
            // Sin la referencia del cargo no hay qué reembolsar: queda pendiente a la vista de la administración.
            return new Reclamo(reembolso, false, null);
        }

        if (!reembolso.Reclamar(reloj.Ahora))
        {
            return new Reclamo(reembolso, false, referencia);
        }

        await reembolsos.Guardar(reembolso, token).ConfigureAwait(false);
        return new Reclamo(reembolso, true, referencia);
    }

    private async Task<ResultadoDeReembolsoEnPasarela> PedirALaPasarela(Reembolso reembolso, string referenciaDelCargo)
    {
        using var plazo = new CancellationTokenSource(opciones.TiempoDeRespuesta);
        try
        {
            if (reembolso.Intentos > 1)
            {
                // Un reintento: antes de pedir otra vez se consulta lo que la pasarela ya aplicó, para no reembolsar dos veces.
                var existentes = await pasarela.ConsultarReembolsos(referenciaDelCargo, plazo.Token).ConfigureAwait(false);
                if (existentes is null)
                {
                    return ResultadoDeReembolsoEnPasarela.SinRespuesta;
                }

                if (existentes.FirstOrDefault(e => e.ReembolsoId == reembolso.Id) is { } aplicado)
                {
                    return ResultadoDeReembolsoEnPasarela.Aplicado(aplicado.Referencia);
                }
            }

            return await pasarela.Reembolsar(reembolso.Id, referenciaDelCargo, reembolso.Monto, plazo.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return ResultadoDeReembolsoEnPasarela.SinRespuesta;
        }
    }

    private async Task<Resultado<EstadoDelReembolso>> Registrar(Guid reembolsoId, ResultadoDeReembolsoEnPasarela resultado, CancellationToken token)
    {
        var reembolso = (await reembolsos.Obtener(reembolsoId, paraActualizar: true, token).ConfigureAwait(false))!;
        var ahora = reloj.Ahora;
        switch (resultado.Tipo)
        {
            case TipoDeResultadoDeReembolsoEnPasarela.Aplicado:
                reembolso.MarcarReembolsado(resultado.Referencia!, ahora);
                break;
            case TipoDeResultadoDeReembolsoEnPasarela.Rechazado:
                reembolso.DejarPendiente(MotivoPendienteDeReembolso.RechazadoPorPasarela, ahora);
                break;
            case TipoDeResultadoDeReembolsoEnPasarela.NoSoportado:
                reembolso.DejarPendiente(MotivoPendienteDeReembolso.NoSoportado, ahora);
                break;
            default:
                reembolso.DejarPendiente(MotivoPendienteDeReembolso.SinRespuesta, ahora);
                break;
        }

        await reembolsos.Guardar(reembolso, token).ConfigureAwait(false);
        await AuditarResolucion(reembolso, token).ConfigureAwait(false);
        return Resultado<EstadoDelReembolso>.Exito(reembolso.Estado.APublico());
    }

    private async Task Alertar(string motivo)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(motivo, reloj.Ahora), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una alerta que no sale no interrumpe el reembolso.
        }
    }
}

/// <summary>Reintentar un reembolso pendiente desde la administración central (PAG-10): solo el administrador central, y solo uno que se pueda pedir ahora (tras un sin_respuesta se respeta la espera).</summary>
internal sealed class ReintentarReembolso(AutorizadorDePagos autorizador, IRepositorioDeReembolsos reembolsos, IUnidadDeTrabajo unidad, ProcesarReembolso procesar, IReloj reloj) : ICasoDeUso<Guid, EstadoDelReembolso>
{
    public async Task<Resultado<EstadoDelReembolso>> Ejecutar(Guid reembolsoId, CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<EstadoDelReembolso>.Fallo(rechazo);
        }

        var reembolso = await unidad.Ejecutar(token => reembolsos.Obtener(reembolsoId, paraActualizar: false, token), cancelacion).ConfigureAwait(false);
        if (reembolso is null)
        {
            return FallosDePagos.De<EstadoDelReembolso>(CategoriaDeError.NoEncontrado, ClavesDePagos.NoEncontrado);
        }

        if (!reembolso.PuedeReintentarse(reloj.Ahora))
        {
            return FallosDePagos.De<EstadoDelReembolso>(CategoriaDeError.Conflicto, ClavesDePagos.ReembolsoNoReintentable);
        }

        return await procesar.Ejecutar(reembolsoId, cancelacion).ConfigureAwait(false);
    }
}

internal sealed record EntradaDeListarReembolsos(int Pagina, int Tamano);

/// <summary>Los reembolsos pendientes de la plataforma para la administración central (PAG-10, PAG-24): sin datos de pago ni de la persona.</summary>
internal sealed class ListarReembolsosPendientes(AutorizadorDePagos autorizador, IRepositorioDeReembolsos reembolsos, IUnidadDeTrabajo unidad) : ICasoDeUso<EntradaDeListarReembolsos, PaginaDeReembolsos>
{
    public const int TamanoMaximo = 100;

    public async Task<Resultado<PaginaDeReembolsos>> Ejecutar(EntradaDeListarReembolsos entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<PaginaDeReembolsos>.Fallo(rechazo);
        }

        if (entrada.Pagina < 1 || entrada.Tamano < 1 || entrada.Tamano > TamanoMaximo)
        {
            return FallosDePagos.De<PaginaDeReembolsos>(CategoriaDeError.Validacion, ClavesDePagos.ValidacionFallida, [new ErrorDeCampo(entrada.Pagina < 1 ? "pagina" : "tamano", "invalido")]);
        }

        var (lista, total) = await unidad.Ejecutar(token => reembolsos.ListarPendientes(entrada.Pagina, entrada.Tamano, token), cancelacion).ConfigureAwait(false);
        return Resultado<PaginaDeReembolsos>.Exito(new PaginaDeReembolsos(lista.Select(r => r.AVista()).ToArray(), entrada.Pagina, entrada.Tamano, total));
    }
}

/// <summary>El contrato público <see cref="IReembolsos"/> sobre sus casos de uso (D08).</summary>
internal sealed class ReembolsosDePagos(
    RegistrarReembolso registrar,
    ProcesarReembolso procesar,
    ReintentarReembolso reintentar,
    ListarReembolsosPendientes listar,
    IRepositorioDeReembolsos reembolsos,
    IUnidadDeTrabajo unidad) : IReembolsos
{
    public Task<Resultado<ReembolsoRegistrado>> Registrar(RegistrarReembolsoEntrada entrada, CancellationToken cancelacion = default) => registrar.Ejecutar(entrada, cancelacion);

    public Task<Resultado<EstadoDelReembolso>> Procesar(Guid reembolsoId, CancellationToken cancelacion = default) => procesar.Ejecutar(reembolsoId, cancelacion);

    public Task<int> ContarPendientes(Guid cuentaId, OrigenDelReembolso origen, CancellationToken cancelacion = default) =>
        unidad.Ejecutar(token => reembolsos.ContarPendientesDeLaCuenta(cuentaId, origen.ADominio(), token), cancelacion);

    public async Task<Resultado<ReembolsoPendienteVista?>> ConsultarPorOrigen(OrigenDelReembolso origen, string claveNatural, CancellationToken cancelacion = default)
    {
        if (string.IsNullOrWhiteSpace(claveNatural) || claveNatural.Trim().Length > Reembolso.LongitudMaximaDeLaClave)
        {
            return FallosDePagos.De<ReembolsoPendienteVista?>(CategoriaDeError.Validacion, ClavesDePagos.ValidacionFallida, [new ErrorDeCampo("claveNatural", "invalida")]);
        }

        var reembolso = await unidad.Ejecutar(token => reembolsos.BuscarPorOrigen(origen.ADominio(), claveNatural.Trim(), token), cancelacion).ConfigureAwait(false);
        return Resultado<ReembolsoPendienteVista?>.Exito(reembolso?.AVista());
    }

    public Task<Resultado<PaginaDeReembolsos>> ListarPendientes(int pagina, int tamano, CancellationToken cancelacion = default) => listar.Ejecutar(new EntradaDeListarReembolsos(pagina, tamano), cancelacion);

    public Task<Resultado<EstadoDelReembolso>> Reintentar(Guid reembolsoId, CancellationToken cancelacion = default) => reintentar.Ejecutar(reembolsoId, cancelacion);
}
