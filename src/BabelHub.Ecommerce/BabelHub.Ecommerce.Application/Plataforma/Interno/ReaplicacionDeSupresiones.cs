using BabelHub.Ecommerce.Domain.Plataforma;

namespace BabelHub.Ecommerce.Application.Plataforma.Interno;

/// <summary>Traducciones entre el modelo del contrato y el del Dominio.</summary>
internal static class ConversionesDeSupresion
{
    public static TipoDeSupresion ATipo(ClaseDeSupresion clase) => clase switch
    {
        ClaseDeSupresion.Cancelacion => TipoDeSupresion.Cancelacion,
        ClaseDeSupresion.Anonimizacion => TipoDeSupresion.Anonimizacion,
        _ => throw new ArgumentOutOfRangeException(nameof(clase)),
    };

    public static FaseDeSupresion AFase(EtapaDeSupresion etapa) => etapa switch
    {
        EtapaDeSupresion.Intencion => FaseDeSupresion.Intencion,
        EtapaDeSupresion.Cierre => FaseDeSupresion.Cierre,
        _ => throw new ArgumentOutOfRangeException(nameof(etapa)),
    };

    /// <summary>La entrada tal como la leyó el almacén, con la suma que traía; una entrada malformada lanza <see cref="ArgumentException"/>.</summary>
    public static EntradaLeida ADominio(EntradaLeidaDto leida) =>
        new(
            leida.ClaveEnElAlmacen,
            EntradaDeSupresion.Reconstruir(leida.Entrada.IdentificadorOpaco, ATipo(leida.Entrada.Clase), AFase(leida.Entrada.Etapa), leida.Entrada.Fecha, leida.Entrada.Suma),
            leida.ModificadaEn);

    public static EstadoDelRegistro AEstado(EstadoDeVerificacionDelRegistro estado) => estado switch
    {
        EstadoDeVerificacionDelRegistro.Valido => EstadoDelRegistro.Valido,
        EstadoDeVerificacionDelRegistro.RegistroAusente => EstadoDelRegistro.RegistroAusente,
        EstadoDeVerificacionDelRegistro.SelloAusente => EstadoDelRegistro.SelloAusente,
        EstadoDeVerificacionDelRegistro.EntradaAlterada => EstadoDelRegistro.EntradaAlterada,
        EstadoDeVerificacionDelRegistro.EntradasAusentes => EstadoDelRegistro.EntradasAusentes,
        EstadoDeVerificacionDelRegistro.SumaDelConjuntoNoCoincide => EstadoDelRegistro.SumaDelConjuntoNoCoincide,
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };
}

/// <summary>
/// Verifica el registro de supresiones y su sello antes de abrir el servicio restaurado (PLA-19, plan §5.1 paso 1). El resultado es un
/// dato, no un fallo: <see cref="ResultadoDeVerificacionDelRegistroDto.PermiteAbrir"/> dice si el servicio puede abrirse. Con el
/// registro ausente o una entrada alterada nunca se abre; con el sello ausente, con menos entradas que las selladas o con una suma que
/// no coincide solo se abre si la persona responsable registró su decisión para ese respaldo (vía de excepción).
/// </summary>
public sealed class VerificarRegistroDeSupresiones(IRegistroDeSupresiones registro, ISellosDelRegistro sellos, IExcepcionesDelRegistro excepciones)
    : ICasoDeUso<DateTimeOffset, ResultadoDeVerificacionDelRegistroDto>
{
    public async Task<Resultado<ResultadoDeVerificacionDelRegistroDto>> Ejecutar(DateTimeOffset instanteDelRespaldo, CancellationToken cancelacion = default)
    {
        IReadOnlyList<EntradaLeidaDto> leidas;
        try
        {
            leidas = await registro.LeerConMetadatos(cancelacion).ConfigureAwait(false);
        }
        catch (RegistroDeSupresionesNoDisponibleException)
        {
            return Exito(EstadoDelRegistro.RegistroAusente);
        }

        var entradas = new List<EntradaLeida>();
        var malformadas = new List<string>();
        foreach (var leida in leidas)
        {
            try
            {
                entradas.Add(ConversionesDeSupresion.ADominio(leida));
            }
            catch (ArgumentException)
            {
                malformadas.Add(leida.ClaveEnElAlmacen);
            }
        }

        if (malformadas.Count > 0)
        {
            return Exito(EstadoDelRegistro.EntradaAlterada, [.. malformadas.Order(StringComparer.Ordinal)]);
        }

        SelloDelRegistro? sello;
        try
        {
            sello = await ObtenerSello(instanteDelRespaldo, cancelacion).ConfigureAwait(false);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Un sello con una cantidad imposible no es un sello: no coincide con nada.
            return await ConExcepcion(EstadoDelRegistro.SumaDelConjuntoNoCoincide, [], instanteDelRespaldo, cancelacion).ConfigureAwait(false);
        }

        var verificado = VerificadorDelRegistro.Verificar(entradas, sello);
        var estado = ConversionesDeSupresion.AEstado(verificado.Estado);
        return estado is EstadoDelRegistro.SelloAusente or EstadoDelRegistro.EntradasAusentes or EstadoDelRegistro.SumaDelConjuntoNoCoincide
            ? await ConExcepcion(estado, verificado.ClavesAfectadas, instanteDelRespaldo, cancelacion).ConfigureAwait(false)
            : Exito(estado, verificado.ClavesAfectadas);
    }

    private async Task<SelloDelRegistro?> ObtenerSello(DateTimeOffset instante, CancellationToken cancelacion)
    {
        try
        {
            var dto = await sellos.ObtenerAnteriorA(instante, cancelacion).ConfigureAwait(false);
            return dto is null ? null : new SelloDelRegistro(dto.Instante, dto.Cantidad, dto.SumaDelConjunto);
        }
        catch (RegistroDeSupresionesNoDisponibleException)
        {
            return null;
        }
    }

    private async Task<Resultado<ResultadoDeVerificacionDelRegistroDto>> ConExcepcion(EstadoDelRegistro estado, IReadOnlyList<string> claves, DateTimeOffset instante, CancellationToken cancelacion)
    {
        var decision = await excepciones.Obtener(instante, cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeVerificacionDelRegistroDto>.Exito(new(estado, claves, decision is not null && decision.InstanteDelRespaldo == instante));
    }

    private static Resultado<ResultadoDeVerificacionDelRegistroDto> Exito(EstadoDelRegistro estado, IReadOnlyList<string>? claves = null) =>
        Resultado<ResultadoDeVerificacionDelRegistroDto>.Exito(new(estado, claves ?? [], false));
}

/// <summary>Cómo se reaplican las supresiones al restaurar: el instante del respaldo, el umbral de entradas nuevas y la confirmación de la persona responsable.</summary>
public sealed record OpcionesDeReaplicacion
{
    public OpcionesDeReaplicacion(DateTimeOffset instanteDelRespaldo, int maximoDeEntradasNuevas = 50, bool confirmaConteoAnomalo = false)
    {
        if (maximoDeEntradasNuevas <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximoDeEntradasNuevas), "El umbral de entradas nuevas debe ser positivo.");
        }

        InstanteDelRespaldo = instanteDelRespaldo;
        MaximoDeEntradasNuevas = maximoDeEntradasNuevas;
        ConfirmaConteoAnomalo = confirmaConteoAnomalo;
    }

    public DateTimeOffset InstanteDelRespaldo { get; }

    /// <summary>
    /// Cuántas entradas puede haber más allá de las selladas antes de pedir confirmación. Sin sello, todas cuentan. Una entrada falsa
    /// de un servicio comprometido no se aplica en silencio: si hay más que este número, no se reaplica nada sin confirmar.
    /// </summary>
    public int MaximoDeEntradasNuevas { get; }

    /// <summary>La persona responsable confirmó explícitamente reaplicar pese a que el conteo supera el umbral.</summary>
    public bool ConfirmaConteoAnomalo { get; }
}

/// <summary>
/// Vuelve a aplicar, de forma idempotente, las supresiones y anonimizaciones con entrada de cierre antes de abrir el servicio restaurado
/// (PLA-19, plan §5.1 paso 2). Solo reaplica un cierre que tiene intención previa; las intenciones sin cierre (cancelación no
/// completada) siguen activas y se informan, y un cierre sin intención se rechaza y se informa. Se supone que el registro ya se
/// verificó. Ante más entradas nuevas que el umbral no reaplica nada sin la confirmación de la persona responsable.
/// </summary>
public sealed class ReaplicarSupresiones(
    IRegistroDeSupresiones registro,
    IAplicadorDeSupresion aplicador,
    ISellosDelRegistro sellos,
    OpcionesDeReaplicacion opciones) : IReaplicadorDeSupresiones
{
    public async Task<InformeDeReaplicacion> Reaplicar(CancellationToken cancelacion = default)
    {
        var entradas = await registro.LeerConMetadatos(cancelacion).ConfigureAwait(false);

        var porCuenta = entradas
            .GroupBy(e => (e.Entrada.IdentificadorOpaco, e.Entrada.Clase))
            .OrderBy(g => g.Key.IdentificadorOpaco, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Clase)
            .Select(g => (g.Key.IdentificadorOpaco, g.Key.Clase, Intencion: g.Any(e => e.Entrada.Etapa == EtapaDeSupresion.Intencion), Cierre: g.Any(e => e.Entrada.Etapa == EtapaDeSupresion.Cierre)))
            .ToList();
        var intencionesSinCierre = Identificadores(porCuenta.Where(c => c.Intencion && !c.Cierre).Select(c => c.IdentificadorOpaco));
        var cierresSinIntencion = Identificadores(porCuenta.Where(c => c.Cierre && !c.Intencion).Select(c => c.IdentificadorOpaco));

        var superaElUmbral = await EntradasNuevas(entradas, cancelacion).ConfigureAwait(false) > opciones.MaximoDeEntradasNuevas;
        if (superaElUmbral && !opciones.ConfirmaConteoAnomalo)
        {
            return new InformeDeReaplicacion(0, 0, intencionesSinCierre, cierresSinIntencion, [], RequiereConfirmacion: true, ConfirmadaPorUmbral: false);
        }

        var reaplicadas = 0;
        var yaAplicadas = 0;
        var fallidas = new List<string>();
        foreach (var cuenta in porCuenta.Where(c => c.Intencion && c.Cierre))
        {
            switch (await aplicador.Aplicar(cuenta.IdentificadorOpaco, cuenta.Clase, cancelacion).ConfigureAwait(false))
            {
                case ResultadoDeAplicacionDeSupresion.Aplicada:
                    reaplicadas++;
                    break;
                case ResultadoDeAplicacionDeSupresion.YaEstabaAplicada:
                    yaAplicadas++;
                    break;
                default:
                    fallidas.Add(cuenta.IdentificadorOpaco);
                    break;
            }
        }

        return new InformeDeReaplicacion(reaplicadas, yaAplicadas, intencionesSinCierre, cierresSinIntencion, Identificadores(fallidas), RequiereConfirmacion: false, ConfirmadaPorUmbral: superaElUmbral);
    }

    private async Task<int> EntradasNuevas(IReadOnlyList<EntradaLeidaDto> entradas, CancellationToken cancelacion)
    {
        SelloDelRegistroDto? sello;
        try
        {
            sello = await sellos.ObtenerAnteriorA(opciones.InstanteDelRespaldo, cancelacion).ConfigureAwait(false);
        }
        catch (RegistroDeSupresionesNoDisponibleException)
        {
            sello = null;
        }

        // Lo que el sello no cubre: modificado desde su instante menos el margen. Sin sello, todo cuenta como nuevo.
        return sello is null ? entradas.Count : entradas.Count(e => e.ModificadaEn >= sello.Instante - SelloDelRegistro.Margen);
    }

    private static IReadOnlyList<string> Identificadores(IEnumerable<string> ids) => [.. ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}
