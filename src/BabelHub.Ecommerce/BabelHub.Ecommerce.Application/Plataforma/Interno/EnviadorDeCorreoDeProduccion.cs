using BabelHub.Ecommerce.Domain.Plataforma;

namespace BabelHub.Ecommerce.Application.Plataforma.Interno;

/// <summary>
/// Un correo ya con su asunto y su cuerpo, listo para el transporte. <b>Nunca se imprime</b> (lleva direcciones y, en un código, el código): su representación en texto es fija.
/// </summary>
public sealed record MensajeDeCorreo(IReadOnlyList<string> Destinatarios, string Asunto, string Cuerpo)
{
    public override string ToString() => "[mensaje de correo]";
}

/// <summary>El transporte SMTP de la cuenta de envío (MailKit, T34, en la Infraestructura): lanza si no pudo entregar el mensaje al servidor; nunca decide cuota ni cola.</summary>
public interface ITransporteDeCorreo
{
    Task Enviar(MensajeDeCorreo mensaje, CancellationToken cancelacion = default);
}

/// <summary>
/// Pone en palabras cada correo con las plantillas del catálogo cerrado (T29; AC-IDE-56): sin enlaces, sin pedir datos ni contraseñas, nombrando a BabelHub y diciendo que nunca se pedirá el código por otro medio; el aviso de
/// una respuesta a una solicitud no lleva su contenido. Lanza si la plantilla no existe: un correo sin plantilla no sale.
/// </summary>
public interface IRenderizadorDeCorreo
{
    MensajeDeCorreo De(CorreoSaliente correo);

    MensajeDeCorreo DeCodigo(CorreoDeCodigo correo);

    /// <summary>La alerta a la administración, ya con las direcciones de sus destinatarios: solo el servicio, el motivo del catálogo cerrado, la primera ocurrencia y cuántas se agruparon (SEG-10).</summary>
    MensajeDeCorreo DeAlerta(string servicio, string motivo, DateTimeOffset primeraOcurrencia, int ocurrencias, IReadOnlyList<string> direcciones);
}

/// <summary>Un correo que quedó pendiente: su contenido (cifrado en la base), su prioridad y sus intentos.</summary>
public sealed record CorreoPendiente(Guid Id, CorreoSaliente Correo, int Prioridad, int Intentos, DateTimeOffset Creado);

/// <summary>
/// La cola <c>plataforma.correos_pendientes</c> (T34): solo alertas a la administración, alertas críticas de cuenta, respuestas a reclamos y pagos; el <b>contenido se guarda cifrado</b> (AES-GCM) y lo enviado se purga.
/// <see cref="Vencidos"/> los entrega por prioridad (la administración primero) y, a igual prioridad, del más antiguo al más reciente.
/// </summary>
public interface IColaDeCorreosPendientes
{
    Task Agregar(CorreoSaliente correo, int prioridad, DateTimeOffset proximoIntento, CancellationToken cancelacion = default);

    Task<IReadOnlyList<CorreoPendiente>> Vencidos(DateTimeOffset ahora, int limite, CancellationToken cancelacion = default);

    /// <summary>Se envió: se purga.</summary>
    Task Eliminar(Guid id, CancellationToken cancelacion = default);

    Task Reprogramar(Guid id, int intentos, DateTimeOffset proximoIntento, CancellationToken cancelacion = default);
}

/// <summary>
/// El enviador de correo de la cuenta de envío (T34; IDE-65, IDE-66, IDE-73, IDE-78, IDE-79, IDE-80, IDE-82): <b>todo correo cuenta contra la cuota</b> (<see cref="CuotaDeCorreo"/> decide el cupo, el tope de 24 al día por
/// motivo y servicio y la alerta del 80 %). Un correo que sale se entrega por el transporte y, si este falla, <b>se devuelve el cupo</b> (la cuota solo cuenta lo que salió): lo que se reintenta (alertas, críticas, reclamos y
/// pagos) se encola con su prioridad y lo demás se suprime. Un código sale por el canal en memoria <b>sin cola ni reintento</b> (vence a los 10 minutos). <b>Nada de aquí lanza a quien llama</b> (AGENTS.md §3.10): un fallo
/// es un valor y una alerta técnica; ni el contenido ni las direcciones llegan a un registro.
/// </summary>
public sealed class EnviadorDeCorreoDeProduccion(
    CuotaDeCorreo cuota,
    ITransporteDeCorreo transporte,
    IRenderizadorDeCorreo plantillas,
    IColaDeCorreosPendientes cola,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj) : IEnviadorDeCorreo
{
    /// <summary>El motivo del catálogo cerrado para el correo que no pudo salir (IDE-65): una dependencia no necesaria que falló.</summary>
    public const string MotivoDeFallaDelCorreo = "dependencia-no-necesaria-fallida";

    public async Task<ResultadoDeEnvio> Enviar(CorreoSaliente correo, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(correo);
        var clase = CuotaDeCorreo.Clase(correo.Categoria);
        DecisionDeSalidaDeCorreo decision;
        try
        {
            decision = await cuota.Decidir(correo, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // El almacén de la cuota no responde: el correo no se pierde ni se da por enviado; si se reintenta, se encola, y se avisa.
            await AlertarSinInterrumpir(cancelacion).ConfigureAwait(false);
            return await EncolarOSuprimir(correo, clase, cancelacion).ConfigureAwait(false);
        }

        switch (decision.Resultado)
        {
            case ResultadoDeEnvio.Suprimido:
                return ResultadoDeEnvio.Suprimido;

            case ResultadoDeEnvio.Encolado:
                return await EncolarOSuprimir(correo, clase, cancelacion, decision.Prioridad).ConfigureAwait(false);
        }

        try
        {
            await transporte.Enviar(plantillas.De(correo), cancelacion).ConfigureAwait(false);
            return ResultadoDeEnvio.Enviado;
        }
        catch (Exception)
        {
            await DevolverSinInterrumpir(decision.Reserva!, cancelacion).ConfigureAwait(false);
            await AlertarSinInterrumpir(cancelacion).ConfigureAwait(false);
            return await EncolarOSuprimir(correo, clase, cancelacion).ConfigureAwait(false);
        }
    }

    public async Task<ResultadoDeEnvio> EnviarCodigo(CorreoDeCodigo correo, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(correo);
        DecisionDeSalidaDeCorreo decision;
        try
        {
            decision = await cuota.DecidirCodigo(cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            await AlertarSinInterrumpir(cancelacion).ConfigureAwait(false);
            return ResultadoDeEnvio.Suprimido;
        }

        if (decision.Resultado != ResultadoDeEnvio.Enviado)
        {
            return ResultadoDeEnvio.Suprimido;
        }

        try
        {
            await transporte.Enviar(plantillas.DeCodigo(correo), cancelacion).ConfigureAwait(false);
            return ResultadoDeEnvio.Enviado;
        }
        catch (Exception)
        {
            // Sin reintento: el código vence a los 10 minutos y la persona pide otro (IDE-65). El cupo se devuelve y queda la alerta técnica.
            await DevolverSinInterrumpir(decision.Reserva!, cancelacion).ConfigureAwait(false);
            await AlertarSinInterrumpir(cancelacion).ConfigureAwait(false);
            return ResultadoDeEnvio.Suprimido;
        }
    }

    private async Task<ResultadoDeEnvio> EncolarOSuprimir(CorreoSaliente correo, ClaseDeCorreoParaCuota clase, CancellationToken cancelacion, int? prioridad = null)
    {
        if (PoliticaDeCuotaDeCorreo.SinCupo(clase) != SinCupoDeCorreo.Encolar)
        {
            return ResultadoDeEnvio.Suprimido;
        }

        try
        {
            await cola.Agregar(correo, prioridad ?? PoliticaDeCuotaDeCorreo.Prioridad(clase), reloj.Ahora, cancelacion).ConfigureAwait(false);
            return ResultadoDeEnvio.Encolado;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Sin cola no hay dónde guardarlo: no se promete lo que no se puede cumplir.
            await AlertarSinInterrumpir(cancelacion).ConfigureAwait(false);
            return ResultadoDeEnvio.Suprimido;
        }
    }

    private async Task DevolverSinInterrumpir(ReservaDeCupoDeCorreo reserva, CancellationToken cancelacion)
    {
        try
        {
            // Con CancellationToken.None: si quien llama canceló, el cupo y el tope igual se devuelven (no se gasta una alerta del día sin que saliera correo).
            await cuota.Devolver(reserva, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // El cupo se renueva solo a la medianoche de Lima: lo peor es contar uno de más.
        }
    }

    private async Task AlertarSinInterrumpir(CancellationToken cancelacion)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(MotivoDeFallaDelCorreo, reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Sin a dónde mandar la alerta: el flujo sigue.
        }
    }
}

/// <summary>
/// El reintento de lo pendiente al renovarse la cuota diaria (T34; IDE-78, IDE-80): toma lo vencido <b>por prioridad</b> (las alertas a la administración primero, luego las críticas y por último reclamos y pagos), vuelve a pasar
/// cada correo por la cuota y lo entrega; lo enviado se purga, lo que no tiene cupo espera, y lo que falla en el transporte se reprograma con esperas crecientes. Una falla de uno no detiene los demás.
/// </summary>
public sealed class ReintentoDeCorreosPendientes(
    CuotaDeCorreo cuota,
    ITransporteDeCorreo transporte,
    IRenderizadorDeCorreo plantillas,
    IColaDeCorreosPendientes cola,
    IReloj reloj)
{
    public const int LimitePorPasada = 100;

    public async Task<ResultadoDelReintentoDeCorreos> Procesar(CancellationToken cancelacion = default)
    {
        var ahora = reloj.Ahora;
        var vencidos = await cola.Vencidos(ahora, LimitePorPasada, cancelacion).ConfigureAwait(false);
        var enviados = 0;
        var sinCupo = 0;
        var fallidos = 0;
        foreach (var pendiente in vencidos)
        {
            try
            {
                var decision = await cuota.Decidir(pendiente.Correo, cancelacion).ConfigureAwait(false);
                if (decision.Resultado == ResultadoDeEnvio.Suprimido)
                {
                    // Pasó el tope de 24 al día de su motivo: ya no sale por correo (IDE-82) y se purga de la cola.
                    await cola.Eliminar(pendiente.Id, cancelacion).ConfigureAwait(false);
                    continue;
                }

                if (decision.Resultado == ResultadoDeEnvio.Encolado)
                {
                    sinCupo++;
                    continue;
                }

                try
                {
                    await transporte.Enviar(plantillas.De(pendiente.Correo), cancelacion).ConfigureAwait(false);
                }
                catch (Exception) when (!cancelacion.IsCancellationRequested)
                {
                    // Devolver el cupo es una cortesía: si falla, igual se reprograma con la espera creciente (no se reintenta sin espera).
                    try
                    {
                        await cuota.Devolver(decision.Reserva!, cancelacion).ConfigureAwait(false);
                    }
                    catch (Exception) when (!cancelacion.IsCancellationRequested)
                    {
                        // El cupo se renueva a la medianoche de Lima.
                    }

                    var intentos = pendiente.Intentos + 1;
                    await cola.Reprogramar(pendiente.Id, intentos, ahora + EsperaDelReintentoDeCorreo.Para(intentos), cancelacion).ConfigureAwait(false);
                    fallidos++;
                    continue;
                }

                enviados++;

                // Entrega «al menos una vez»: si purgarlo falla tras un envío correcto, la siguiente pasada lo reenvía (preferible a perder un pago o un reclamo); se cuenta como enviado y la falla de la cola no lo contradice.
                await cola.Eliminar(pendiente.Id, cancelacion).ConfigureAwait(false);
            }
            catch (Exception) when (!cancelacion.IsCancellationRequested)
            {
                // La cuota o la cola fallaron para este correo: se queda en la cola y se reintenta en la siguiente pasada.
                fallidos++;
            }
        }

        return new ResultadoDelReintentoDeCorreos(vencidos.Count, enviados, sinCupo, fallidos);
    }
}

/// <summary>Lo que hizo una pasada: cuántos correos había vencidos, cuántos salieron, cuántos esperan cupo y cuántos fallaron.</summary>
public sealed record ResultadoDelReintentoDeCorreos(int Vencidos, int Enviados, int SinCupo, int Fallidos);

/// <summary>Las esperas crecientes entre los reintentos de un correo pendiente que falló en el transporte: 1, 2, 4, 8… minutos con tope de una hora.</summary>
public static class EsperaDelReintentoDeCorreo
{
    public static readonly TimeSpan Maxima = TimeSpan.FromHours(1);

    public static TimeSpan Para(int intentos)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(intentos, 1);
        return TimeSpan.FromMinutes(Math.Min(Math.Pow(2, Math.Min(intentos - 1, 12)), Maxima.TotalMinutes));
    }
}
