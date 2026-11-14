using System.Globalization;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Fuentes;

namespace BabelHub.Inventario.Application.Fuentes.Interno;

/// <summary>
/// Define o cambia el plazo, en días, del traslado de una fuente a otra (FUE-12, FUE-14, FUE-17): solo la administración central. Se rechazan, antes de leer nada, el
/// plazo que no es mayor que cero y una ruta de una fuente a sí misma (<c>INV-RUTA-INVALIDA</c>); una fuente inexistente es no encontrada. El repositorio devuelve el
/// plazo anterior leído bajo el bloqueo de la fila, de modo que con dos administradores simultáneos la auditoría guarda para cada cambio el valor que de verdad
/// reemplazó. Repetir el mismo plazo no audita nada (idempotente).
/// </summary>
internal sealed class RegistrarRuta(
    AutorizadorDeFuentes autorizador,
    IValidador<RegistrarRutaEntrada> validador,
    IRepositorioDeFuentes fuentes,
    IRepositorioDeRutas rutas,
    IAuditoria auditoria) : ICasoDeUso<RegistrarRutaEntrada, RutaDefinida>
{
    public async Task<Resultado<RutaDefinida>> Ejecutar(RegistrarRutaEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeFuentes.Fallo<RutaDefinida>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeFuentes.Validacion<RutaDefinida>(validacion);
        }

        if (entrada.Dias <= 0 || string.Equals(entrada.CodigoDeOrigen, entrada.CodigoDeDestino, StringComparison.Ordinal))
        {
            return FallosDeFuentes.De<RutaDefinida>(CategoriaDeError.ReglaDeNegocio, ClavesDeFuentes.RutaInvalida);
        }

        var origen = await fuentes.PorCodigo(entrada.CodigoDeOrigen, cancelacion).ConfigureAwait(false);
        var destino = await fuentes.PorCodigo(entrada.CodigoDeDestino, cancelacion).ConfigureAwait(false);
        if (origen is null || destino is null)
        {
            return FallosDeFuentes.NoEncontrada<RutaDefinida>();
        }

        // El Dominio vuelve a validar la ruta ya con los identificadores (origen distinto de destino, plazo positivo).
        if (RutaEntreFuentes.Validar(origen.Id, destino.Id, entrada.Dias) is not null)
        {
            return FallosDeFuentes.De<RutaDefinida>(CategoriaDeError.ReglaDeNegocio, ClavesDeFuentes.RutaInvalida);
        }

        var anterior = await rutas.Definir(RutaEntreFuentes.Definir(origen.Id, destino.Id, entrada.Dias), cancelacion).ConfigureAwait(false);
        if (anterior != entrada.Dias)
        {
            await auditoria.Registrar(Solicitud(origen, destino, anterior, entrada.Dias), cancelacion).ConfigureAwait(false);
        }

        return Resultado<RutaDefinida>.Exito(new RutaDefinida(origen.Codigo, destino.Codigo, entrada.Dias, anterior));
    }

    private static SolicitudDeRegistro Solicitud(Fuente origen, Fuente destino, int? anterior, int nuevo)
    {
        var detalle = new Dictionary<string, string?> { ["plazoNuevoEnDias"] = nuevo.ToString(CultureInfo.InvariantCulture) };
        if (anterior is { } dias)
        {
            detalle["plazoAnteriorEnDias"] = dias.ToString(CultureInfo.InvariantCulture);
        }

        return new SolicitudDeRegistro(anterior is null ? AccionesAuditables.RutaRegistrada : AccionesAuditables.RutaModificada)
        {
            ObjetoTipo = "ruta",
            ObjetoId = $"{origen.Codigo}:{destino.Codigo}",
            SucursalOrigen = origen.Id,
            SucursalDestino = destino.Id,
            Detalle = detalle,
        };
    }
}

/// <summary>
/// El plazo entre dos fuentes (FUE-13): los días de la ruta o «ruta no definida» (<see langword="null"/>), que no impide vender. La administración central y los
/// encargados; logística y cliente se rechazan con su evento. Una fuente inexistente es no encontrada (después de autorizar).
/// </summary>
internal sealed class ConsultarPlazoDeRuta(
    AutorizadorDeFuentes autorizador,
    IValidador<ConsultarPlazoDeRutaEntrada> validador,
    ILecturaCoherente lectura,
    IRepositorioDeFuentes fuentes,
    IRepositorioDeRutas rutas) : ICasoDeUso<ConsultarPlazoDeRutaEntrada, PlazoDeRutaConsultado>
{
    public async Task<Resultado<PlazoDeRutaConsultado>> Ejecutar(ConsultarPlazoDeRutaEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirAdministracionOEncargado(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeFuentes.Fallo<PlazoDeRutaConsultado>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeFuentes.Validacion<PlazoDeRutaConsultado>(validacion);
        }

        return await lectura.EjecutarLectura(
            async token =>
            {
                var origen = await fuentes.PorCodigo(entrada.CodigoDeOrigen, token).ConfigureAwait(false);
                var destino = await fuentes.PorCodigo(entrada.CodigoDeDestino, token).ConfigureAwait(false);
                if (origen is null || destino is null)
                {
                    return FallosDeFuentes.NoEncontrada<PlazoDeRutaConsultado>();
                }

                var ruta = await rutas.Obtener(origen.Id, destino.Id, token).ConfigureAwait(false);
                return Resultado<PlazoDeRutaConsultado>.Exito(new PlazoDeRutaConsultado(origen.Codigo, destino.Codigo, ruta?.PlazoEnDias));
            },
            cancelacion).ConfigureAwait(false);
    }
}

/// <summary>Las rutas definidas, ordenadas por código de origen y de destino: la administración central y los encargados (logística y cliente se rechazan con su evento).</summary>
internal sealed class ListarRutas(
    AutorizadorDeFuentes autorizador,
    ILecturaCoherente lectura,
    IRepositorioDeFuentes fuentes,
    IRepositorioDeRutas rutas) : ICasoDeUso<ListarRutasEntrada, IReadOnlyList<RutaListada>>
{
    public async Task<Resultado<IReadOnlyList<RutaListada>>> Ejecutar(ListarRutasEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirAdministracionOEncargado(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeFuentes.Fallo<IReadOnlyList<RutaListada>>(rechazo);
        }

        var lista = await lectura.EjecutarLectura(
            async token =>
            {
                var codigos = (await fuentes.Listar(token).ConfigureAwait(false)).ToDictionary(f => f.Id, f => f.Codigo);
                return (await rutas.Todas(token).ConfigureAwait(false))
                    .Where(r => codigos.ContainsKey(r.OrigenId) && codigos.ContainsKey(r.DestinoId))
                    .Select(r => new RutaListada(codigos[r.OrigenId], codigos[r.DestinoId], r.PlazoEnDias))
                    .OrderBy(r => r.CodigoDeOrigen, StringComparer.Ordinal)
                    .ThenBy(r => r.CodigoDeDestino, StringComparer.Ordinal)
                    .ToList();
            },
            cancelacion).ConfigureAwait(false);

        return Resultado<IReadOnlyList<RutaListada>>.Exito(lista);
    }
}
