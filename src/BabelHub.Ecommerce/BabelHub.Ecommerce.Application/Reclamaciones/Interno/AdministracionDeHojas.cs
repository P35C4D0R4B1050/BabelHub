using System.Globalization;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Reclamaciones;

namespace BabelHub.Ecommerce.Application.Reclamaciones.Interno;

/// <summary>
/// Lo que hace falta para avisar la respuesta, copiado dentro de la transacción: el aviso se envía <b>después</b> de confirmarla, fuera de toda transacción (D11, regla 10). Lleva el correo
/// de contacto y el texto de la respuesta, de modo que su texto nunca los repite.
/// </summary>
internal sealed record AvisoParaEnviar(Guid HojaId, string Numero, string Correo, string Respuesta, DateOnly FechaDeLaRespuesta)
{
    public override string ToString() => $"AvisoParaEnviar {{ Numero = {Numero} }}";
}

/// <summary>
/// El aviso por correo de la respuesta (REC-07, REC-13, D11): la categoría <c>reclamo</c> lleva el número, la fecha y la respuesta, <b>sin el documento ni el domicilio</b>. <c>Enviado</c> o
/// <c>Encolado</c> marcan el aviso <c>enviado</c>; un fallo, o un resultado suprimido (IDE-78 lo prohíbe, así que cuenta como fallo), deja la hoja respondida con el aviso <c>pendiente</c> y
/// suelta el arrendamiento para reintentar. Una falla del correo nunca interrumpe el caso de uso.
/// </summary>
internal sealed class EnvioDelAvisoDeRespuesta(IEnviadorDeCorreo correo, IRepositorioDeHojas hojas, IUnidadDeTrabajo unidad, IReloj reloj)
{
    public const string Plantilla = "respuesta-de-reclamo";

    /// <summary>Corre después de confirmar la respuesta: no se cancela con la solicitud (un cliente que cierra la conexión no deja el correo entregado sin su marca).</summary>
    public async Task<AvisoDeLaRespuesta> Enviar(AvisoParaEnviar aviso)
    {
        ArgumentNullException.ThrowIfNull(aviso);
        var salio = false;
        try
        {
            var resultado = await correo.Enviar(
                new CorreoSaliente(
                    CategoriaDeCorreo.RespuestaDeReclamo,
                    aviso.Correo,
                    Plantilla,
                    new Dictionary<string, string>
                    {
                        ["numero"] = aviso.Numero,
                        ["fecha"] = aviso.FechaDeLaRespuesta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["respuesta"] = aviso.Respuesta,
                    }),
                CancellationToken.None).ConfigureAwait(false);
            salio = resultado is ResultadoDeEnvio.Enviado or ResultadoDeEnvio.Encolado;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // El correo no interrumpe el caso de uso: la hoja queda respondida con el aviso pendiente.
        }

        try
        {
            return await unidad.Ejecutar(
                async token =>
                {
                    var hoja = await hojas.Obtener(aviso.HojaId, paraActualizar: true, token).ConfigureAwait(false);
                    if (hoja is null)
                    {
                        return AvisoDeLaRespuesta.Pendiente;
                    }

                    if (salio)
                    {
                        hoja.MarcarAvisoEnviado(reloj.Ahora);
                    }
                    else
                    {
                        hoja.LiberarArrendamientoDeAviso();
                    }

                    await hojas.Guardar(hoja, token).ConfigureAwait(false);
                    return MapeoDeHojas.AvisoDe(hoja.AvisoDeRespuesta) ?? AvisoDeLaRespuesta.Pendiente;
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Si la marca no se pudo guardar, el aviso sigue pendiente y el arrendamiento vence solo.
            return AvisoDeLaRespuesta.Pendiente;
        }
    }
}

/// <summary>La entrada de descartar: un tipo propio para que su caso de uso no comparta interfaz con el de reintentar el aviso (un registro por tipo de entrada).</summary>
internal sealed record DescartarHojaEntrada(Guid HojaId);

/// <summary>La entrada de reintentar el aviso de una respuesta.</summary>
internal sealed record ReintentarAvisoEntrada(Guid HojaId);

/// <summary>Responder una hoja pendiente (REC-07, REC-08, REC-13, REC-16, D11): solo la administración central; guarda, audita y deja el aviso pendiente en una transacción; envía después de confirmar.</summary>
internal sealed class ResponderHoja(
    AutorizadorDeReclamaciones autorizador,
    IRepositorioDeHojas hojas,
    IUnidadDeTrabajo unidad,
    IReloj reloj,
    IAuditoria auditoria,
    EnvioDelAvisoDeRespuesta envio) : ICasoDeUso<ResponderHojaEntrada, ResultadoDeLaHoja>
{
    public async Task<Resultado<ResultadoDeLaHoja>> Ejecutar(ResponderHojaEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReclamaciones.Fallo<ResultadoDeLaHoja>(rechazo);
        }

        if (string.IsNullOrWhiteSpace(entrada.Respuesta))
        {
            return FallosDeReclamaciones.De<ResultadoDeLaHoja>(CategoriaDeError.Validacion, ClavesDeReclamaciones.ValidacionFallida, [new ErrorDeCampo("respuesta", "falta")]);
        }

        if (entrada.Respuesta.Trim().Length > HojaDeReclamacion.LongitudMaximaDeLaRespuesta || !TextoDeHoja.EsSeguro(entrada.Respuesta, permitirSaltos: true))
        {
            return FallosDeReclamaciones.De<ResultadoDeLaHoja>(CategoriaDeError.Validacion, ClavesDeReclamaciones.ValidacionFallida, [new ErrorDeCampo("respuesta", "invalido")]);
        }

        var usuarioId = autorizador.UsuarioId!.Value;
        (Resultado<ResultadoDeLaHoja>? Fallo, AvisoParaEnviar? Aviso) paso;
        try
        {
            paso = await unidad.Ejecutar(
                async token =>
                {
                    var hoja = await hojas.Obtener(entrada.HojaId, paraActualizar: true, token).ConfigureAwait(false);
                    if (hoja is null)
                    {
                        return (FallosDeReclamaciones.De<ResultadoDeLaHoja>(CategoriaDeError.NoEncontrado, ClavesDeReclamaciones.HojaNoEncontrada), (AvisoParaEnviar?)null);
                    }

                    var ahora = reloj.Ahora;
                    if (hoja.Responder(entrada.Respuesta, usuarioId, ahora) == ResultadoDeTransicion.EstadoInvalido)
                    {
                        return (FallosDeReclamaciones.De<ResultadoDeLaHoja>(CategoriaDeError.Conflicto, ClavesDeReclamaciones.EstadoInvalido), (AvisoParaEnviar?)null);
                    }

                    await hojas.Guardar(hoja, token).ConfigureAwait(false);
                    await auditoria.Registrar(
                        new SolicitudDeRegistro(AccionesAuditables.HojaDeReclamacionRespondida)
                        {
                            ObjetoTipo = "hoja-de-reclamacion",
                            ObjetoId = hoja.Numero.ToString(),
                            Detalle = new Dictionary<string, string?> { ["estadoNuevo"] = "respondido" },
                        },
                        token).ConfigureAwait(false);
                    return ((Resultado<ResultadoDeLaHoja>?)null, (AvisoParaEnviar?)new AvisoParaEnviar(hoja.Id, hoja.Numero.ToString(), hoja.Datos.Correo, hoja.Respuesta!, DiaDeLima.De(ahora)));
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (AuditoriaNoDisponibleException)
        {
            // Sin auditoría no se responde (AGENTS §4): la transacción se revirtió y la hoja sigue pendiente.
            return FallosDeReclamaciones.De<ResultadoDeLaHoja>(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }
        catch (HojasNoDisponiblesException)
        {
            return FallosDeReclamaciones.De<ResultadoDeLaHoja>(CategoriaDeError.Conflicto, ClavesDeReclamaciones.Conflicto);
        }

        if (paso.Fallo is not null)
        {
            return paso.Fallo;
        }

        var aviso = await envio.Enviar(paso.Aviso!).ConfigureAwait(false);
        return Resultado<ResultadoDeLaHoja>.Exito(new ResultadoDeLaHoja(paso.Aviso!.Numero, EstadoDeLaHoja.Respondido, aviso));
    }
}

/// <summary>Reintentar el aviso de una respuesta (REC-13, D11): toma el arrendamiento corto bajo el bloqueo de la hoja, envía fuera de transacción y marca; un segundo reintento simultáneo no envía otro.</summary>
internal sealed class ReintentarAvisoDeRespuesta(
    AutorizadorDeReclamaciones autorizador,
    IRepositorioDeHojas hojas,
    IUnidadDeTrabajo unidad,
    IReloj reloj,
    EnvioDelAvisoDeRespuesta envio) : ICasoDeUso<ReintentarAvisoEntrada, ResultadoDeLaHoja>
{
    public async Task<Resultado<ResultadoDeLaHoja>> Ejecutar(ReintentarAvisoEntrada entrada, CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReclamaciones.Fallo<ResultadoDeLaHoja>(rechazo);
        }

        (Resultado<ResultadoDeLaHoja>? Fallo, AvisoParaEnviar? Aviso) paso;
        try
        {
            paso = await unidad.Ejecutar(
                async token =>
                {
                    var hoja = await hojas.Obtener(entrada.HojaId, paraActualizar: true, token).ConfigureAwait(false);
                    if (hoja is null)
                    {
                        return (FallosDeReclamaciones.De<ResultadoDeLaHoja>(CategoriaDeError.NoEncontrado, ClavesDeReclamaciones.HojaNoEncontrada), (AvisoParaEnviar?)null);
                    }

                    switch (hoja.TomarArrendamientoDeAviso(reloj.Ahora))
                    {
                        case ResultadoDeReintento.EnCurso:
                            return (FallosDeReclamaciones.De<ResultadoDeLaHoja>(CategoriaDeError.Conflicto, ClavesDeReclamaciones.Conflicto), (AvisoParaEnviar?)null);
                        case ResultadoDeReintento.NoRespondida or ResultadoDeReintento.YaEnviado or ResultadoDeReintento.Anonimizada:
                            return (FallosDeReclamaciones.De<ResultadoDeLaHoja>(CategoriaDeError.Conflicto, ClavesDeReclamaciones.EstadoInvalido), (AvisoParaEnviar?)null);
                    }

                    await hojas.Guardar(hoja, token).ConfigureAwait(false);
                    return ((Resultado<ResultadoDeLaHoja>?)null, (AvisoParaEnviar?)new AvisoParaEnviar(hoja.Id, hoja.Numero.ToString(), hoja.Datos.Correo, hoja.Respuesta!, DiaDeLima.De(hoja.RespondidaEn!.Value)));
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (HojasNoDisponiblesException)
        {
            return FallosDeReclamaciones.De<ResultadoDeLaHoja>(CategoriaDeError.Conflicto, ClavesDeReclamaciones.Conflicto);
        }

        if (paso.Fallo is not null)
        {
            return paso.Fallo;
        }

        var aviso = await envio.Enviar(paso.Aviso!).ConfigureAwait(false);
        return Resultado<ResultadoDeLaHoja>.Exito(new ResultadoDeLaHoja(paso.Aviso!.Numero, EstadoDeLaHoja.Respondido, aviso));
    }
}

/// <summary>Descartar una hoja pendiente por ser spam (REC-12): la marca, no la elimina, y audita en la misma transacción.</summary>
internal sealed class DescartarHoja(
    AutorizadorDeReclamaciones autorizador,
    IRepositorioDeHojas hojas,
    IUnidadDeTrabajo unidad,
    IReloj reloj,
    IAuditoria auditoria) : ICasoDeUso<DescartarHojaEntrada, ResultadoDeLaHoja>
{
    public async Task<Resultado<ResultadoDeLaHoja>> Ejecutar(DescartarHojaEntrada entrada, CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReclamaciones.Fallo<ResultadoDeLaHoja>(rechazo);
        }

        try
        {
            return await unidad.Ejecutar(
                async token =>
                {
                    var hoja = await hojas.Obtener(entrada.HojaId, paraActualizar: true, token).ConfigureAwait(false);
                    if (hoja is null)
                    {
                        return FallosDeReclamaciones.De<ResultadoDeLaHoja>(CategoriaDeError.NoEncontrado, ClavesDeReclamaciones.HojaNoEncontrada);
                    }

                    if (hoja.Descartar(reloj.Ahora) == ResultadoDeTransicion.EstadoInvalido)
                    {
                        return FallosDeReclamaciones.De<ResultadoDeLaHoja>(CategoriaDeError.Conflicto, ClavesDeReclamaciones.EstadoInvalido);
                    }

                    await hojas.Guardar(hoja, token).ConfigureAwait(false);
                    await auditoria.Registrar(
                        new SolicitudDeRegistro(AccionesAuditables.HojaDeReclamacionDescartada)
                        {
                            ObjetoTipo = "hoja-de-reclamacion",
                            ObjetoId = hoja.Numero.ToString(),
                            Detalle = new Dictionary<string, string?> { ["causa"] = "spam" },
                        },
                        token).ConfigureAwait(false);
                    return Resultado<ResultadoDeLaHoja>.Exito(MapeoDeHojas.ResultadoDe(hoja));
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (AuditoriaNoDisponibleException)
        {
            return FallosDeReclamaciones.De<ResultadoDeLaHoja>(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }
        catch (HojasNoDisponiblesException)
        {
            return FallosDeReclamaciones.De<ResultadoDeLaHoja>(CategoriaDeError.Conflicto, ClavesDeReclamaciones.Conflicto);
        }
    }
}

/// <summary>Listar las hojas (REC-09, REC-12): solo la administración central, paginado con tope de 50 y <b>sin datos personales</b>; las descartadas se ocultan salvo al filtrar por ellas.</summary>
internal sealed class ListarHojas(AutorizadorDeReclamaciones autorizador, IRepositorioDeHojas hojas, IUnidadDeTrabajo unidad) : ICasoDeUso<ListarHojasEntrada, PaginaDeHojas>
{
    public const int TamanoMaximoDePagina = 50;

    public async Task<Resultado<PaginaDeHojas>> Ejecutar(ListarHojasEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReclamaciones.Fallo<PaginaDeHojas>(rechazo);
        }

        var errores = new List<ErrorDeCampo>();
        if (entrada.Pagina < 1 || entrada.Pagina > 1_000_000)
        {
            errores.Add(new ErrorDeCampo("pagina", "invalido"));
        }

        if (entrada.Tamano is < 1 or > TamanoMaximoDePagina)
        {
            errores.Add(new ErrorDeCampo("tamano", "invalido"));
        }

        if (entrada.Estado is { } estado && !Enum.IsDefined(estado))
        {
            errores.Add(new ErrorDeCampo("estado", "invalido"));
        }

        if (errores.Count > 0)
        {
            return FallosDeReclamaciones.De<PaginaDeHojas>(CategoriaDeError.Validacion, ClavesDeReclamaciones.ValidacionFallida, errores);
        }

        var filtro = entrada.Estado is { } e ? MapeoDeHojas.EstadoDeDominio(e) : (EstadoDeHoja?)null;
        var listado = await unidad.Ejecutar(token => hojas.Listar(filtro, entrada.Pagina, entrada.Tamano, token), cancelacion).ConfigureAwait(false);
        return Resultado<PaginaDeHojas>.Exito(new PaginaDeHojas(listado.Hojas.Select(MapeoDeHojas.ResumenDe).ToList(), entrada.Pagina, entrada.Tamano, listado.Total));
    }
}

internal sealed class ReclamacionesDeLaAplicacion(ICasoDeUso<RegistrarHojaEntrada, ConstanciaDeHoja> registrar) : IReclamaciones
{
    public Task<Resultado<ConstanciaDeHoja>> Registrar(RegistrarHojaEntrada entrada, CancellationToken cancelacion = default) => registrar.Ejecutar(entrada, cancelacion);
}

internal sealed class AdministracionDeReclamacionesDeLaAplicacion(
    ICasoDeUso<ListarHojasEntrada, PaginaDeHojas> listar,
    ICasoDeUso<ResponderHojaEntrada, ResultadoDeLaHoja> responder,
    ICasoDeUso<DescartarHojaEntrada, ResultadoDeLaHoja> descartar,
    ICasoDeUso<ReintentarAvisoEntrada, ResultadoDeLaHoja> reintentar) : IAdministracionDeReclamaciones
{
    public Task<Resultado<PaginaDeHojas>> Listar(ListarHojasEntrada entrada, CancellationToken cancelacion = default) => listar.Ejecutar(entrada, cancelacion);

    public Task<Resultado<ResultadoDeLaHoja>> Responder(ResponderHojaEntrada entrada, CancellationToken cancelacion = default) => responder.Ejecutar(entrada, cancelacion);

    public Task<Resultado<ResultadoDeLaHoja>> Descartar(Guid hojaId, CancellationToken cancelacion = default) => descartar.Ejecutar(new DescartarHojaEntrada(hojaId), cancelacion);

    public Task<Resultado<ResultadoDeLaHoja>> ReintentarAviso(Guid hojaId, CancellationToken cancelacion = default) => reintentar.Ejecutar(new ReintentarAvisoEntrada(hojaId), cancelacion);
}
