using System.Globalization;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Reclamaciones;

namespace BabelHub.Ecommerce.Application.Reclamaciones.Interno;

internal static class MapeoDeHojas
{
    public static ConstanciaDeHoja ConstanciaDe(HojaDeReclamacion hoja) =>
        new(
            hoja.Numero.ToString(),
            hoja.FechaRegistro,
            hoja.Tipo.ATexto(),
            hoja.BienContratado,
            hoja.MontoReclamado,
            hoja.Detalle,
            hoja.PedidoReferencia,
            hoja.Datos.Nombre,
            hoja.Datos.Documento,
            hoja.Datos.Domicilio,
            hoja.Datos.Correo,
            PlazoDeRespuesta.Dias,
            hoja.FechaLimiteDeRespuesta);

    public static EstadoDeLaHoja EstadoDe(EstadoDeHoja estado) => estado switch
    {
        EstadoDeHoja.Pendiente => EstadoDeLaHoja.Pendiente,
        EstadoDeHoja.Respondido => EstadoDeLaHoja.Respondido,
        EstadoDeHoja.Descartado => EstadoDeLaHoja.Descartado,
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };

    public static EstadoDeHoja EstadoDeDominio(EstadoDeLaHoja estado) => estado switch
    {
        EstadoDeLaHoja.Pendiente => EstadoDeHoja.Pendiente,
        EstadoDeLaHoja.Respondido => EstadoDeHoja.Respondido,
        EstadoDeLaHoja.Descartado => EstadoDeHoja.Descartado,
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };

    public static AvisoDeLaRespuesta? AvisoDe(AvisoDeRespuesta? aviso) => aviso switch
    {
        null => null,
        AvisoDeRespuesta.Pendiente => AvisoDeLaRespuesta.Pendiente,
        AvisoDeRespuesta.Enviado => AvisoDeLaRespuesta.Enviado,
        _ => throw new ArgumentOutOfRangeException(nameof(aviso)),
    };

    public static ResumenDeHoja ResumenDe(HojaDeReclamacion hoja) =>
        new(hoja.Id, hoja.Numero.ToString(), hoja.FechaRegistro, hoja.Tipo.ATexto(), EstadoDe(hoja.Estado), AvisoDe(hoja.AvisoDeRespuesta));

    public static ResultadoDeLaHoja ResultadoDe(HojaDeReclamacion hoja) => new(hoja.Numero.ToString(), EstadoDe(hoja.Estado), AvisoDe(hoja.AvisoDeRespuesta));
}

/// <summary>
/// Registrar una hoja (REC-02, REC-03, REC-04, REC-05, REC-11, REC-18; D08, D09, D10). El orden es fijo: verificación de persona, validación estructural, cupo del origen con la clave del
/// formulario, y una transacción corta que toma el número y guarda la hoja. No exige sesión ni consulta a Identidad. Un fallo después de reservar el cupo suelta la clave (no queda en curso), pero el cupo ya contado se pierde: la política no es liberable.
/// </summary>
internal sealed partial class RegistrarHoja(
    IExigenciaDeVerificacionDePersona verificacion,
    ICalculadorDeHuella huellas,
    IControlDeLimites limites,
    IRepositorioDeHojas hojas,
    IUnidadDeTrabajo unidad,
    IReloj reloj) : ICasoDeUso<RegistrarHojaEntrada, ConstanciaDeHoja>
{
    public const string Ruta = "/reclamaciones/nueva";

    public const int LongitudMaximaDeLaClave = 200;

    private const string ClaveDelFormulario = "claveDelFormulario";

    public async Task<Resultado<ConstanciaDeHoja>> Ejecutar(RegistrarHojaEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        // (0) Verificación de persona: antes de validar y de reservar el cupo (SEG-31). Superarla no exime del límite.
        switch (await verificacion.Exigir(AccionProtegida.HojaDeReclamacion, entrada.TokenDeVerificacion, cancelacion).ConfigureAwait(false))
        {
            case ResultadoDeExigencia.Requerida or ResultadoDeExigencia.Fallida:
                return FallosDeReclamaciones.De<ConstanciaDeHoja>(CategoriaDeError.Prohibido, ClavesDeReclamaciones.VerificacionRequerida);
            case ResultadoDeExigencia.NoDisponible:
                return FallosDeReclamaciones.De<ConstanciaDeHoja>(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }

        // (1) Validación: la lista de campos que faltan o son inválidos, sin eco de los valores.
        var errores = Validar(entrada);
        if (errores.Count > 0)
        {
            return FallosDeReclamaciones.De<ConstanciaDeHoja>(CategoriaDeError.Validacion, ClavesDeReclamaciones.ValidacionFallida, errores);
        }

        _ = TipoDeHojaExtensiones.TryDesdeTexto(entrada.Tipo, out var tipo);
        _ = CorreoNormalizado.TryCrear(entrada.Correo, out var correoNormalizado);
        // Las huellas son con clave y normalizan el texto (SEG-25: minúsculas y NFKC): se les pasa el contenido y la clave en hexadecimal para que sean exactos y dos hojas que solo difieren
        // en mayúsculas no den la misma huella. La clave actual va primero; durante la rotación de la clave también la anterior.
        var huellasDelContenido = huellas.DeSujetoEnRotacion(EnHexadecimal(ContenidoCanonico(entrada, tipo, correoNormalizado!)), "hoja-contenido");
        var huellasDeLaClave = huellas.DeSujetoEnRotacion(EnHexadecimal(entrada.ClaveDelFormulario!.Trim()), "hoja-clave");
        var contenidoHash = huellasDelContenido[0].Valor;
        var claveHash = huellasDeLaClave[0].Valor;
        var contenidosPosibles = huellasDelContenido.Select(h => h.Valor).ToArray();
        var clavesPosibles = huellasDeLaClave.Select(h => h.Valor).ToArray();

        // (2) Cupo: 3 hojas por origen y hora; la clave del formulario hace idempotente la reserva (la repetición no cuenta).
        var limite = await limites.Reservar(
            PoliticasDeLimite.HojasPorOrigen,
            idempotencia: new ClaveDeIdempotencia(claveHash, Ruta, contenidoHash),
            cancelacion: cancelacion).ConfigureAwait(false);
        switch (limite.Estado)
        {
            case EstadoDeLimite.Excedido:
                return FallosDeReclamaciones.De<ConstanciaDeHoja>(
                    CategoriaDeError.Limite,
                    ClavesDeReclamaciones.LimiteExcedido,
                    [new ErrorDeCampo(ClavesDeReclamaciones.EsperaEnSegundos, (limite.EsperaEnSegundos ?? 1).ToString(CultureInfo.InvariantCulture))]);
            case EstadoDeLimite.RepeticionCompletada:
                return await ConstanciaYaCreada(clavesPosibles, contenidosPosibles, cancelacion).ConfigureAwait(false);
            case EstadoDeLimite.RepeticionEnCurso or EstadoDeLimite.ClaveEnConflicto:
                return FallosDeReclamaciones.De<ConstanciaDeHoja>(CategoriaDeError.Conflicto, ClavesDeReclamaciones.Conflicto);
            case EstadoDeLimite.NoDisponible:
                return FallosDeReclamaciones.De<ConstanciaDeHoja>(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }

        // (3) Transacción corta: el siguiente número del año de Lima y la hoja.
        var reserva = limite.Reserva;
        var ahora = reloj.Ahora;
        try
        {
            var hoja = await unidad.Ejecutar(
                async token =>
                {
                    var nueva = HojaDeReclamacion.Registrar(
                        Guid.NewGuid(),
                        NumeroDeHoja.Crear(DiaDeLima.Anio(ahora), await hojas.SiguienteSecuencia(DiaDeLima.Anio(ahora), token).ConfigureAwait(false)),
                        tipo,
                        new DatosDelConsumidor(entrada.Nombre!, entrada.Documento!, entrada.Domicilio!, entrada.Correo!),
                        entrada.BienContratado!,
                        entrada.MontoReclamado!.Value,
                        entrada.Detalle!,
                        entrada.PedidoReferencia,
                        contenidoHash,
                        claveHash,
                        ahora);
                    if (await hojas.Agregar(nueva, token).ConfigureAwait(false) == ResultadoDeAgregado.ClaveRepetida)
                    {
                        throw new ClaveDeHojaRepetidaException();
                    }

                    return nueva;
                },
                cancelacion).ConfigureAwait(false);

            await CompletarElCupo(reserva, hoja, cancelacion).ConfigureAwait(false);
            return Resultado<ConstanciaDeHoja>.Exito(MapeoDeHojas.ConstanciaDe(hoja));
        }
        catch (ClaveDeHojaRepetidaException)
        {
            // Una carrera con la misma clave: la transacción se revirtió (sin hueco en la numeración). Si el contenido coincide, es la constancia ya creada.
            var existente = await ConstanciaYaCreada(clavesPosibles, contenidosPosibles, cancelacion).ConfigureAwait(false);
            await (existente.EsExito ? CompletarElCupo(reserva, null, cancelacion) : Devolver(reserva, cancelacion)).ConfigureAwait(false);
            return existente;
        }
        catch (HojasNoDisponiblesException)
        {
            await Devolver(reserva, cancelacion).ConfigureAwait(false);
            return FallosDeReclamaciones.De<ConstanciaDeHoja>(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }
        catch (Exception) when (reserva is not null)
        {
            // Un fallo de la base no deja la clave en curso 60 s. El cupo ya contado no se devuelve (la política no es liberable): es lo conservador frente al abuso.
            await Devolver(reserva, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static List<ErrorDeCampo> Validar(RegistrarHojaEntrada entrada)
    {
        var resultado = ValidacionDeHoja.Validar(
            new EntradaDeHoja(
                entrada.Nombre,
                entrada.Documento,
                entrada.Domicilio,
                entrada.Correo,
                entrada.BienContratado,
                entrada.MontoReclamado,
                entrada.Tipo,
                entrada.Detalle,
                entrada.PedidoReferencia));
        var errores = resultado.Faltan.Select(c => new ErrorDeCampo(FallosDeReclamaciones.NombreDelCampo(c), "falta"))
            .Concat(resultado.Invalidos.Select(c => new ErrorDeCampo(FallosDeReclamaciones.NombreDelCampo(c), "invalido")))
            .ToList();

        if (string.IsNullOrWhiteSpace(entrada.ClaveDelFormulario))
        {
            errores.Add(new ErrorDeCampo(ClaveDelFormulario, "falta"));
        }
        else if (!FormaDeLaClave().IsMatch(entrada.ClaveDelFormulario.Trim()))
        {
            errores.Add(new ErrorDeCampo(ClaveDelFormulario, "invalido"));
        }

        return errores;
    }

    private static string ContenidoCanonico(RegistrarHojaEntrada entrada, TipoDeHoja tipo, string correoNormalizado) =>
        string.Join(
            '\u001f',
            tipo.ATexto(),
            entrada.Nombre!.Trim(),
            entrada.Documento!.Trim(),
            entrada.Domicilio!.Trim(),
            correoNormalizado,
            entrada.BienContratado!.Trim(),
            entrada.MontoReclamado!.Value.ToString("0.00", CultureInfo.InvariantCulture),
            entrada.Detalle!.Trim(),
            entrada.PedidoReferencia?.Trim() ?? string.Empty);

    private async Task<Resultado<ConstanciaDeHoja>> ConstanciaYaCreada(IReadOnlyList<string> clavesHash, IReadOnlyList<string> contenidosHash, CancellationToken cancelacion)
    {
        HojaDeReclamacion? existente;
        try
        {
            existente = await unidad.Ejecutar(token => hojas.BuscarPorClave(clavesHash, token), cancelacion).ConfigureAwait(false);
        }
        catch (HojasNoDisponiblesException)
        {
            return FallosDeReclamaciones.De<ConstanciaDeHoja>(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }

        return existente?.ContenidoHash is { } resumen && contenidosHash.Contains(resumen, StringComparer.Ordinal)
            ? Resultado<ConstanciaDeHoja>.Exito(MapeoDeHojas.ConstanciaDe(existente))
            : FallosDeReclamaciones.De<ConstanciaDeHoja>(CategoriaDeError.Conflicto, ClavesDeReclamaciones.Conflicto);
    }

    private static string EnHexadecimal(string texto) => Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(texto)).ToLowerInvariant();

    /// <summary>La clave de un solo uso que emite el servidor: al menos 128 bits en base64url (22 caracteres) y hasta 200.</summary>
    [GeneratedRegex(@"\A[A-Za-z0-9_-]{22,200}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex FormaDeLaClave();

    private async Task CompletarElCupo(ReservaDeLimite? reserva, HojaDeReclamacion? hoja, CancellationToken cancelacion)
    {
        if (reserva is null)
        {
            return;
        }

        try
        {
            await limites.Completar(reserva, new ResultadoMinimo("registrada", hoja?.Id.ToString("D")), cancelacion).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // La hoja ya está guardada y el índice único de la clave garantiza la idempotencia: un fallo del almacén de contadores no deshace el registro.
        }
    }

    private async Task Devolver(ReservaDeLimite? reserva, CancellationToken cancelacion)
    {
        if (reserva is null)
        {
            return;
        }

        try
        {
            await limites.Fallar(reserva, cancelacion).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Si el almacén no responde, la clave vence sola.
        }
    }
}
