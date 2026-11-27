using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

/// <summary>La cookie del visitante cuyo carrito se fusiona con el de la cuenta de la sesión (la cuenta sale del actor, nunca de la entrada).</summary>
public sealed record FusionarCarritoEntrada(string? CookieDelVisitante)
{
    /// <summary>La cookie del visitante es una capacidad: no se imprime.</summary>
    private bool PrintMembers(System.Text.StringBuilder constructor)
    {
        constructor.Append("ConCookie = ").Append(CookieDelVisitante is not null);
        return true;
    }
}

/// <summary>
/// <c>FusionarCarritoDeVisitante</c> (CAR-24, AC-CAR-22): al terminar el ingreso o el registro suma el carrito del visitante al de la cuenta con la función pura <c>Fusion</c>, <b>idempotente</b> (el carrito del visitante
/// pasa a <c>fusionado</c> y su constancia a <c>fusiones_aplicadas</c> en la misma transacción; una repetición, o un segundo dispositivo con la misma cookie, no suma otra vez) y <b>sin red dentro de la transacción</b>:
/// el Catálogo, la disponibilidad y la posesión se obtienen antes. Con una compra en curso (pago pendiente, <c>reservando</c> o <c>en_pago</c> con un intento activo) se <b>aplaza</b>: el carrito y la cookie del visitante
/// siguen y se reintenta en el siguiente <c>POST</c> de la misma sesión. Los dos carritos se bloquean por <c>id</c> ascendente, después del checkout (D10). Un fallo igual conserva el carrito del visitante.
/// </summary>
internal sealed class FusionarCarritoDeVisitante(ServiciosDelCarrito servicios) : ICasoDeUso<FusionarCarritoEntrada, ResultadoDeFusionDeCarrito>
{
    public async Task<Resultado<ResultadoDeFusionDeCarrito>> Ejecutar(FusionarCarritoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        var cuentaDelActor = await servicios.Autorizador.ExigirCuenta(cancelacion).ConfigureAwait(false);
        if (!cuentaDelActor.EsExito)
        {
            return Resultado<ResultadoDeFusionDeCarrito>.Fallo(cuentaDelActor.Error!);
        }

        var cuenta = cuentaDelActor.Valor;
        if (await servicios.ExigirCuentaActiva(cuenta, cancelacion).ConfigureAwait(false) is { } cuentaRechazada)
        {
            return Resultado<ResultadoDeFusionDeCarrito>.Fallo(cuentaRechazada);
        }

        // Sin cookie utilizable, o con una de un carrito caducado, desconocido o ya fusionado, no hay nada que fusionar: la cookie se borra.
        var solicitante = new SolicitanteDelCarrito(TipoDeSolicitante.Cuenta, cuenta);
        var visitante = HuellaDeLaCookie.TryDe(entrada.CookieDelVisitante, out var huella)
            ? await servicios.Carritos.ObtenerDeVisitante(huella!, conBloqueo: false, cancelacion).ConfigureAwait(false)
            : null;
        var delaCuenta = await servicios.Carritos.ObtenerDeCuenta(cuenta, conBloqueo: false, cancelacion).ConfigureAwait(false);
        if (!servicios.EsUtil(visitante))
        {
            return await Responder(delaCuenta, cuenta, [], aplazada: false, borrarLaCookie: true, null, cancelacion).ConfigureAwait(false);
        }

        // Un visitante sin líneas no toca el carrito de la cuenta ni el checkout: solo queda marcado.
        var hayLineas = visitante!.Lineas.Count > 0;
        var puerta = PuertaDeEdicion.Libre;
        if (hayLineas)
        {
            var decidida = await servicios.Edicion.PrepararAntesDeBloquear(cuenta, cancelacion).ConfigureAwait(false);
            if (!decidida.EsExito)
            {
                // Con una compra en curso la fusión se aplaza (CAR-26); cualquier otra falla (Pagos no responde) la deja sin hacer y el carrito del visitante sigue.
                return decidida.Error!.Clave is ClavesDelCarrito.PagoPendiente or ClavesDelCarrito.EnCurso
                    ? await Responder(delaCuenta, cuenta, [], aplazada: true, borrarLaCookie: false, null, cancelacion).ConfigureAwait(false)
                    : Resultado<ResultadoDeFusionDeCarrito>.Fallo(decidida.Error);
            }

            puerta = decidida.Valor;
        }

        // Todo lo que es red, antes de la transacción (D19).
        var terreno = await ObtenerTerreno(visitante, cuenta, cancelacion).ConfigureAwait(false);
        if (terreno.Error is { } error)
        {
            return Resultado<ResultadoDeFusionDeCarrito>.Fallo(error);
        }

        var visitanteId = visitante.Id;
        (ResultadoDeLaFusion Estado, CarritoDeDominio? DeLaCuenta) transaccion;
        try
        {
            transaccion = await servicios.Unidad.Ejecutar(token => EnLaTransaccion(cuenta, visitanteId, huella!, delaCuenta?.Id, terreno.Valor!, puerta, token), cancelacion).ConfigureAwait(false);
        }
        catch (CarritoYaExisteException)
        {
            // Otro dispositivo creó el carrito de la cuenta a la vez: se rehace la transacción y ahora lo encuentra y lo bloquea (en orden).
            transaccion = await servicios.Unidad.Ejecutar(token => EnLaTransaccion(cuenta, visitanteId, huella!, delaCuenta?.Id, terreno.Valor!, puerta, token), cancelacion).ConfigureAwait(false);
        }

        var (aplicada, delaCuentaFinal) = transaccion;
        return aplicada switch
        {
            ResultadoDeLaFusion.Aplazada => await Responder(delaCuentaFinal ?? delaCuenta, cuenta, [], aplazada: true, borrarLaCookie: false, null, cancelacion).ConfigureAwait(false),
            _ => await Responder(delaCuentaFinal ?? delaCuenta, cuenta, [], aplazada: false, borrarLaCookie: true, terreno.Valor!.Ediciones, cancelacion).ConfigureAwait(false),
        };
    }

    private async Task<Resultado<ResultadoDeFusionDeCarrito>> Responder(CarritoDeDominio? delaCuenta, Guid cuenta, IReadOnlyList<AvisoDelCarrito> ajustes, bool aplazada, bool borrarLaCookie, IReadOnlyDictionary<Guid, EdicionConsultada>? ediciones, CancellationToken cancelacion)
    {
        var vista = await servicios.ConstruirVista(delaCuenta, cuenta, ediciones, cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeFusionDeCarrito>.Exito(new ResultadoDeFusionDeCarrito(vista, [.. vista.Avisos.Where(a => a.Tipo == TipoDeAvisoDelCarrito.FusionAjustada)], aplazada, borrarLaCookie));
    }

    private sealed record Terreno(IReadOnlyDictionary<Guid, EdicionConsultada> Ediciones, IReadOnlyDictionary<Guid, EdicionParaFusion> ParaLaFusion, IReadOnlySet<Guid> Poseidas);

    private async Task<(ErrorDeAplicacion? Error, Terreno? Valor)> ObtenerTerreno(CarritoDeDominio visitante, Guid cuenta, CancellationToken cancelacion)
    {
        var ids = visitante.Lineas.Select(l => l.EdicionId).Distinct().ToList();
        if (ids.Count == 0)
        {
            return (null, new Terreno(new Dictionary<Guid, EdicionConsultada>(), new Dictionary<Guid, EdicionParaFusion>(), new HashSet<Guid>()));
        }

        var consulta = await servicios.Catalogo.ObtenerEdiciones(ids, cancelacion).ConfigureAwait(false);
        if (!consulta.EsExito)
        {
            return (new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDelCarrito.ServicioNoDisponible), null);
        }

        var ediciones = consulta.Valor.ToDictionary(e => e.EdicionId);
        var impresas = ids.Where(id => ediciones.TryGetValue(id, out var e) && e.Formato == FormatoDeEdicion.Impresa).ToList();
        var vendibles = new Dictionary<Guid, IReadOnlyDictionary<string, int>?>();
        if (impresas.Count > 0)
        {
            var disponibilidad = await servicios.Disponibilidad.Consultar(impresas, cancelacion).ConfigureAwait(false);
            if (disponibilidad.EsExito)
            {
                foreach (var dato in disponibilidad.Valor)
                {
                    vendibles[dato.EdicionId] = ServiciosDelCarrito.VendiblePorSucursal(dato);
                }
            }
        }

        var digitales = ids.Where(id => ediciones.TryGetValue(id, out var e) && e.Formato == FormatoDeEdicion.Digital).ToList();
        var poseidas = new HashSet<Guid>();
        if (digitales.Count > 0)
        {
            var posesion = await servicios.Posesion.Posee(cuenta, digitales, cancelacion).ConfigureAwait(false);
            if (posesion.Estado == EstadoDePosesion.Posee)
            {
                poseidas.UnionWith(posesion.Poseidas);
            }
        }

        var paraLaFusion = new Dictionary<Guid, EdicionParaFusion>();
        foreach (var id in ids)
        {
            if (ediciones.TryGetValue(id, out var edicion))
            {
                var esVendible = edicion.Estado == EstadoDeEdicionConsultado.Activa && edicion.MotivoDeNoDisponible is null;
                paraLaFusion[id] = new EdicionParaFusion(id, edicion.Formato == FormatoDeEdicion.Digital, esVendible, vendibles.GetValueOrDefault(id));
            }
        }

        return (null, new Terreno(ediciones, paraLaFusion, poseidas));
    }

    private enum ResultadoDeLaFusion
    {
        Aplicada,
        Aplazada,
    }

    private async Task<(ResultadoDeLaFusion Estado, CarritoDeDominio? DeLaCuenta)> EnLaTransaccion(Guid cuenta, Guid visitanteId, byte[] huella, Guid? cuentaCarritoId, Terreno terreno, PuertaDeEdicion puerta, CancellationToken cancelacion)
    {
        var ahora = servicios.Reloj.Ahora;

        // El checkout primero y los dos carritos después, por id ascendente (D10).
        var checkout = await servicios.Checkouts.ObtenerVivoDeCuenta(cuenta, conBloqueo: true, cancelacion).ConfigureAwait(false);
        CarritoDeDominio? visitante = null;
        CarritoDeDominio? delaCuenta = null;
        // Si la cuenta no tenía carrito en la primera lectura, otro dispositivo pudo crearlo: se relee su id sin bloqueo para bloquear siempre en orden ascendente (D10).
        var idDeLaCuenta = cuentaCarritoId ?? (await servicios.Carritos.ObtenerDeCuenta(cuenta, conBloqueo: false, cancelacion).ConfigureAwait(false))?.Id;
        foreach (var id in new[] { visitanteId, idDeLaCuenta ?? Guid.Empty }.Where(i => i != Guid.Empty).Order())
        {
            var carrito = await servicios.Carritos.Obtener(id, conBloqueo: true, cancelacion).ConfigureAwait(false);
            if (id == visitanteId)
            {
                visitante = carrito;
            }
            else
            {
                delaCuenta = carrito;
            }
        }

        // Una fusión ya aplicada (otro dispositivo, o una repetición) no suma otra vez.
        if (!servicios.EsUtil(visitante))
        {
            return (ResultadoDeLaFusion.Aplicada, delaCuenta);
        }

        // Si el terreno quedó obsoleto (otra pestaña con la misma cookie agregó una línea después de armarlo), no se fusiona a ciegas: se aplaza y se reintenta, sin marcar fusionado.
        if (visitante!.Lineas.Any(l => !terreno.ParaLaFusion.ContainsKey(l.EdicionId)))
        {
            return (ResultadoDeLaFusion.Aplazada, delaCuenta);
        }

        var nuevo = delaCuenta is null;
        delaCuenta ??= CarritoDeDominio.Crear(servicios.Identificadores.NuevoId(), PropietarioDelCarrito.DeCuenta(cuenta), ahora);

        var hayLineas = visitante!.Lineas.Count > 0;
        IReadOnlyList<EdicionRetirada> retiradas = [];
        if (hayLineas)
        {
            var (cerrada, alEditar) = servicios.Edicion.AplicarDentroDeLaTransaccion(checkout, delaCuenta, puerta, ahora);
            if (cerrada is not null)
            {
                // El checkout cambió entre Reabrir y el bloqueo: la fusión se aplaza y se reintenta.
                return (ResultadoDeLaFusion.Aplazada, delaCuenta);
            }

            retiradas = [.. alEditar, .. delaCuenta.QuitarRetirosHuerfanos(checkout?.Estado, ahora)];

            if (checkout is not null && puerta.Decision != DecisionDeLaPuerta.Libre)
            {
                await servicios.Checkouts.Guardar(checkout, cancelacion).ConfigureAwait(false);
            }
        }

        var avisos = new List<AvisoDelCarrito>();
        if (hayLineas)
        {
            var resultado = Fusion.Fusionar(delaCuenta.Lineas, visitante.Lineas, terreno.ParaLaFusion, terreno.Poseidas);
            delaCuenta.AplicarFusion(resultado, ahora);
            avisos.AddRange(resultado.Ajustes.Select(a => new AvisoDelCarrito(
                servicios.Identificadores.NuevoId(),
                TipoDeAvisoDelCarrito.FusionAjustada,
                a.EdicionId,
                terreno.Ediciones.TryGetValue(a.EdicionId, out var edicion) ? edicion.Titulo : "Edición",
                DeAjuste(a.Motivo),
                a.CantidadFinal,
                ahora)));
        }

        var lineasDelVisitante = visitante.Lineas.Count;
        visitante.MarcarFusionado(ahora);

        if (nuevo)
        {
            await servicios.Carritos.Agregar(delaCuenta, cancelacion).ConfigureAwait(false);
        }
        else
        {
            await servicios.Carritos.Guardar(delaCuenta, cancelacion).ConfigureAwait(false);
        }

        await servicios.Carritos.Guardar(visitante, cancelacion).ConfigureAwait(false);
        await servicios.Carritos.RegistrarFusion(huella, delaCuenta.Id, ahora, cancelacion).ConfigureAwait(false);
        avisos.AddRange(servicios.AvisosDeRetiro(retiradas, terreno.Ediciones, ahora));
        if (avisos.Count > 0)
        {
            await servicios.Carritos.AgregarAvisos(cuenta, avisos, cancelacion).ConfigureAwait(false);
        }

        // La auditoría va en la misma transacción: si no se puede registrar, la excepción sube y la operación se revierte. No lleva la cookie ni la huella del visitante.
        await servicios.Auditoria.FusionAplicada(cuenta, lineasDelVisitante, [.. avisos.Where(a => a.Motivo is not null).Select(a => a.Motivo!.Value)], cancelacion).ConfigureAwait(false);
        return (ResultadoDeLaFusion.Aplicada, delaCuenta);
    }

    private static MotivoDeAjusteDeAviso DeAjuste(MotivoDeAjusteDeFusion motivo) => motivo switch
    {
        MotivoDeAjusteDeFusion.SinStock => MotivoDeAjusteDeAviso.SinStock,
        MotivoDeAjusteDeFusion.YaPoseida => MotivoDeAjusteDeAviso.YaPoseida,
        MotivoDeAjusteDeFusion.NoVendible => MotivoDeAjusteDeAviso.NoVendible,
        MotivoDeAjusteDeFusion.RecortadaAlMaximo => MotivoDeAjusteDeAviso.RecortadaAlMaximo,
        MotivoDeAjusteDeFusion.DigitalAUnaUnidad => MotivoDeAjusteDeAviso.DigitalAUnaUnidad,
        MotivoDeAjusteDeFusion.TopeDeLineas => MotivoDeAjusteDeAviso.TopeDeLineas,
        MotivoDeAjusteDeFusion.TopeDeUnidades => MotivoDeAjusteDeAviso.TopeDeUnidades,
        _ => throw new ArgumentOutOfRangeException(nameof(motivo)),
    };
}
