using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;
using AlertaAgrupada = BabelHub.Ecommerce.Domain.Auditoria.AlertaDeSeguridad;

namespace BabelHub.Ecommerce.Application.Auditoria.Interno;

/// <summary>Lo que el puerto de correo hizo con un aviso: lo envió, lo dejó en su cola o lo suprimió por el tope diario de IDE-82 (24 por motivo y servicio, AUD-42).</summary>
public enum ResultadoDeEnvioDeAlerta
{
    Enviado,
    Encolado,

    /// <summary>El puerto de correo, fuente única del tope diario (IDE-82), no lo envió: la alerta queda en el registro técnico sin correo (AUD-42).</summary>
    Suprimido,
}

/// <summary>
/// El correo de una alerta, <b>sin datos sensibles</b> (SEG-10, AUD-29): el servicio, el motivo del catálogo cerrado, la clase, la primera ocurrencia (UTC; la hora de Lima
/// la pone la plantilla) y el número de ocurrencias agrupadas. Nunca lleva el texto de una excepción, una cadena de conexión ni datos de una solicitud.
/// </summary>
public sealed record CorreoDeAlerta(ServicioAuditado Servicio, CodigoDeAlerta Motivo, ClaseDeAlertaConsultada Clase, DateTimeOffset PrimeraOcurrencia, int Ocurrencias);

/// <summary>
/// El puerto con el que Auditoría envía el correo de una alerta a los administradores centrales activos. <b>Lo implementa el adaptador de <c>Plataforma</c> sobre
/// <c>IEnviadorDeCorreo</c></b> (identidad-seguridad T11 y T34: cuota, tope de 24 al día por motivo y servicio y cola); mientras ese contrato no exista, este puerto
/// propio lo reemplaza y se prueba con un doble. Un fallo del envío se expresa con una excepción: Auditoría la reintenta con esperas crecientes (AUD-40).
/// </summary>
public interface IEnvioDeCorreoDeAlertas
{
    Task<ResultadoDeEnvioDeAlerta> Enviar(CorreoDeAlerta correo, IReadOnlyList<DestinatarioDeAlerta> destinatarios, CancellationToken cancelacion = default);
}

/// <summary>Una alerta propia cuyo correo sigue pendiente, con los intentos ya hechos (para la espera creciente).</summary>
public sealed record AlertaConCorreoPendiente(AlertaAgrupada Alerta, int Intentos);

/// <summary>Las alertas propias pendientes de correo (el reintento con esperas crecientes del E-commerce, AUD-40); la Infraestructura lo implementa sobre <c>auditoria.alertas</c> (T22).</summary>
public interface IAlmacenDeCorreoDeAlertas
{
    /// <summary>Las alertas enviables con el correo pendiente cuyo próximo intento ya llegó, de la más antigua a la más reciente, hasta <paramref name="maximo"/>.</summary>
    Task<IReadOnlyList<AlertaConCorreoPendiente>> ConCorreoPendiente(DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default);

    Task RegistrarIntentoFallido(string alertaId, int intentos, DateTimeOffset proximoIntento, CancellationToken cancelacion = default);
}

/// <summary>
/// <c>auditoria.alertas_ya_enviadas</c> (E-commerce): los identificadores de las alertas del Inventario cuyo correo ya se atendió (una por alerta o ventana, AUD-39), para no
/// duplicarlo tras una recuperación (AUD-38).
/// </summary>
public interface IAlertasYaEnviadas
{
    Task<bool> Contiene(string alertaId, CancellationToken cancelacion = default);

    Task Marcar(string alertaId, CancellationToken cancelacion = default);
}

/// <summary>Hasta dónde llegó la lectura de las alertas del Inventario sin pendientes (la marca solo avanza si todas se atendieron); persistente, para que sobreviva a un reinicio.</summary>
public interface IMarcaDeLecturaDeAlertas
{
    Task<DateTimeOffset?> Obtener(CancellationToken cancelacion = default);

    Task Guardar(DateTimeOffset marca, CancellationToken cancelacion = default);
}

/// <summary>Lo que hizo una corrida de <see cref="AgruparYEnviarAlertas"/> (cantidades; nunca datos).</summary>
public sealed record ResultadoDeEnvioDeAlertas(int Enviadas, int Encoladas, int Suprimidas, int Fallidas, bool InventarioNoDisponible, bool SinDestinatarios);

/// <summary>Las esperas crecientes de los reintentos del correo (AUD-40): 1, 2, 4, 8… minutos con tope de una hora.</summary>
public static class EsperaCrecienteDeCorreo
{
    public static readonly TimeSpan Maxima = TimeSpan.FromHours(1);

    /// <summary>Se deja de reintentar una alerta cuando tiene más de 24 horas desde su última ocurrencia: ya no informa nada nuevo y queda en el registro técnico.</summary>
    public static readonly TimeSpan Caducidad = TimeSpan.FromHours(24);

    public static TimeSpan Para(int intentos)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(intentos, 1);
        var minutos = Math.Pow(2, Math.Min(intentos - 1, 12));
        return TimeSpan.FromMinutes(Math.Min(minutos, Maxima.TotalMinutes));
    }
}

/// <summary>
/// El envío del correo de las alertas (AUD-29, AUD-38, AUD-39, AUD-40, AUD-42; E-commerce): una alerta se envía <b>una sola vez</b> a los administradores centrales activos (las
/// ocurrencias que se suman en su ventana de 15 minutos no reenvían) y el tope de 24 al día por motivo y servicio lo impone el puerto de correo (IDE-82, fuente única): una
/// alerta suprimida queda en el registro técnico. Atiende las alertas propias con el correo pendiente y las del Inventario, que lee con la credencial de servicio de lectura
/// de alertas, sin duplicar tras una recuperación (<see cref="IAlertasYaEnviadas"/>). Un fallo del correo reintenta con esperas crecientes y publica
/// <see cref="MotivosDeAlerta.CorreoDeAlertasNoDisponible"/>, que <b>no se envía por correo</b> (bucle) y nunca interrumpe nada. La corrida es idempotente y la serializa un bloqueo asesor en el trabajo de fondo (T27).
/// </summary>
public sealed class AgruparYEnviarAlertas(
    IAlmacenDeAlertas alertas,
    IAlmacenDeCorreoDeAlertas pendientes,
    IAlertasYaEnviadas yaEnviadas,
    IMarcaDeLecturaDeAlertas marca,
    IClienteDeAuditoriaDelInventario inventario,
    IDestinatariosDeAlertas destinatarios,
    IEnvioDeCorreoDeAlertas correo,
    IAlertas avisos,
    CatalogoDeMotivosDeAlerta catalogo,
    IUnidadDeTrabajo unidad,
    IReloj reloj)
{
    public const int MaximoPorCorrida = 50;

    /// <summary>La lectura del Inventario retrocede esta ventana desde la marca: cubre una alerta que se agrupó mientras se leía.</summary>
    public static readonly TimeSpan MargenDeLectura = VentanaDeAgrupacion.Duracion;

    public async Task<ResultadoDeEnvioDeAlertas> Ejecutar(CancellationToken cancelacion = default)
    {
        var ahora = reloj.Ahora;
        var contadores = new Contadores();

        var activos = await destinatarios.Activos(cancelacion).ConfigureAwait(false);
        if (activos.Count == 0)
        {
            // Sin a quién enviar no hay intento fallido ni correo caído: las alertas siguen pendientes y se atienden cuando haya administradores activos.
            return new ResultadoDeEnvioDeAlertas(0, 0, 0, 0, false, SinDestinatarios: true);
        }

        await EnviarLasPropias(activos, ahora, contadores, cancelacion).ConfigureAwait(false);
        var inventarioCaido = await EnviarLasDelInventario(activos, ahora, contadores, cancelacion).ConfigureAwait(false);

        if (contadores.Fallidas > 0)
        {
            await PublicarCorreoCaido(cancelacion).ConfigureAwait(false);
        }

        return new ResultadoDeEnvioDeAlertas(contadores.Enviadas, contadores.Encoladas, contadores.Suprimidas, contadores.Fallidas, inventarioCaido, SinDestinatarios: false);
    }

    private async Task EnviarLasPropias(IReadOnlyList<DestinatarioDeAlerta> activos, DateTimeOffset ahora, Contadores contadores, CancellationToken cancelacion)
    {
        var lista = await pendientes.ConCorreoPendiente(ahora, MaximoPorCorrida, cancelacion).ConfigureAwait(false);
        foreach (var pendiente in lista)
        {
            var alerta = pendiente.Alerta;
            if (!catalogo.Contiene(alerta.Motivo) || !catalogo.Obtener(alerta.Motivo).EnviablePorCorreo || ahora - alerta.UltimaOcurrencia > EsperaCrecienteDeCorreo.Caducidad)
            {
                // Una alerta originada por el propio correo, o que ya caducó, no se envía: se deja de reintentar y queda en el registro técnico.
                await Anotar(() => Marcar(alerta, cancelacion)).ConfigureAwait(false);
                continue;
            }

            try
            {
                var resultado = await correo.Enviar(ADelCorreo(alerta), activos, cancelacion).ConfigureAwait(false);
                await Anotar(() => Marcar(alerta, cancelacion)).ConfigureAwait(false);
                contadores.Sumar(resultado);
            }
            catch (Exception falla) when (falla is not OperationCanceledException)
            {
                var intentos = pendiente.Intentos + 1;
                contadores.Fallidas++;
                await RegistrarFallo(alerta.Id, intentos, ahora, cancelacion).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> EnviarLasDelInventario(IReadOnlyList<DestinatarioDeAlerta> activos, DateTimeOffset ahora, Contadores contadores, CancellationToken cancelacion)
    {
        var marcaActual = await marca.Obtener(cancelacion).ConfigureAwait(false);
        var desde = (marcaActual ?? ahora - EsperaCrecienteDeCorreo.Caducidad) - MargenDeLectura;
        var respuesta = await inventario.LeerAlertasNuevas(desde, cancelacion).ConfigureAwait(false);
        if (respuesta.Estado != EstadoDeRespuestaDelInventario.Disponible)
        {
            // El Inventario no responde (o rechaza la lectura): sus alertas esperan allí; la marca no avanza y se avisa por el registro técnico.
            await PublicarTecnica(MotivosDeAlerta.LecturaDeAlertasDelInventarioNoDisponible, cancelacion).ConfigureAwait(false);
            return true;
        }

        var todasAtendidas = true;
        var ultimaLeida = marcaActual;
        foreach (var alerta in respuesta.Valor.Where(a => a.Servicio == ServicioAuditado.Inventario).OrderBy(a => a.PrimeraOcurrencia).ThenBy(a => a.Id, StringComparer.Ordinal))
        {
            ultimaLeida = ultimaLeida is null || alerta.UltimaOcurrencia > ultimaLeida ? alerta.UltimaOcurrencia : ultimaLeida;
            if (await yaEnviadas.Contiene(alerta.Id, cancelacion).ConfigureAwait(false))
            {
                continue;
            }

            if (!catalogo.Contiene(alerta.Motivo.Valor) || !catalogo.Obtener(alerta.Motivo.Valor).EnviablePorCorreo)
            {
                await Anotar(() => yaEnviadas.Marcar(alerta.Id, cancelacion)).ConfigureAwait(false);
                continue;
            }

            try
            {
                var resultado = await correo.Enviar(new CorreoDeAlerta(alerta.Servicio, alerta.Motivo, alerta.Clase, alerta.PrimeraOcurrencia, alerta.Ocurrencias), activos, cancelacion).ConfigureAwait(false);
                await Anotar(() => yaEnviadas.Marcar(alerta.Id, cancelacion)).ConfigureAwait(false);
                contadores.Sumar(resultado);
            }
            catch (Exception falla) when (falla is not OperationCanceledException)
            {
                todasAtendidas = false;
                contadores.Fallidas++;
            }
        }

        // La marca solo avanza si no quedó ninguna sin atender: la siguiente lectura la vuelve a ver y, si ya se envió, el registro de enviadas la salta. Si la lectura llegó al tope
        // (el Inventario devolvió su máximo), avanza solo hasta la última ocurrencia leída, no hasta ahora: lo que no cupo se lee en la corrida siguiente y nunca se pierde (AUD-38).
        if (todasAtendidas)
        {
            var truncada = respuesta.Valor.Count >= HorizonteDeAlertas.MaximoPorLectura;
            var nueva = truncada && ultimaLeida is { } leida ? leida : ahora;
            await Anotar(() => marca.Guardar(nueva, cancelacion)).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>
    /// Una anotación posterior al envío (marcar la alerta o el registro de enviadas, avanzar la marca) que falla <b>no es un fallo del correo</b>: no cuenta como intento ni publica
    /// «correo no disponible» y no aborta la corrida; la corrida siguiente lo vuelve a intentar (a lo sumo repite un correo, nunca lo pierde).
    /// </summary>
    private static async Task Anotar(Func<Task> anotacion)
    {
        try
        {
            await anotacion().ConfigureAwait(false);
        }
        catch (Exception falla) when (falla is not OperationCanceledException)
        {
            // Se reintenta en la próxima corrida.
        }
    }

    private static CorreoDeAlerta ADelCorreo(AlertaAgrupada alerta) =>
        new(
            alerta.Servicio == ServicioDeOrigen.Ecommerce ? ServicioAuditado.Ecommerce : ServicioAuditado.Inventario,
            new CodigoDeAlerta(alerta.Motivo),
            alerta.Clase == ClaseDeAlerta.Seguridad ? ClaseDeAlertaConsultada.Seguridad : ClaseDeAlertaConsultada.Tecnica,
            alerta.PrimeraOcurrencia,
            alerta.Ocurrencias);

    private async Task Marcar(AlertaAgrupada alerta, CancellationToken cancelacion) =>
        await unidad.Ejecutar(
            async ct =>
            {
                // Se relee: una ocurrencia que llegó durante el envío no se pierde al marcar.
                var actual = await alertas.Obtener(alerta.Id, ct).ConfigureAwait(false) ?? alerta;
                await alertas.Guardar(actual.MarcarComoEnviada(), ct).ConfigureAwait(false);
                return true;
            },
            cancelacion).ConfigureAwait(false);

    private async Task RegistrarFallo(string alertaId, int intentos, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        try
        {
            await pendientes.RegistrarIntentoFallido(alertaId, intentos, ahora + EsperaCrecienteDeCorreo.Para(intentos), cancelacion).ConfigureAwait(false);
        }
        catch (Exception falla) when (falla is not OperationCanceledException)
        {
            // Sin dónde anotar el intento: la alerta sigue pendiente y la próxima corrida la reintenta.
        }
    }

    private Task PublicarCorreoCaido(CancellationToken cancelacion) => PublicarTecnica(MotivosDeAlerta.CorreoDeAlertasNoDisponible, cancelacion);

    private async Task PublicarTecnica(string motivo, CancellationToken cancelacion)
    {
        try
        {
            await avisos.PublicarTecnica(new CodigoDeAlerta(motivo), cancelacion).ConfigureAwait(false);
        }
        catch (Exception falla) when (falla is not OperationCanceledException)
        {
            // La alerta nunca interrumpe: el registro técnico tampoco responde.
        }
    }

    private sealed class Contadores
    {
        public int Enviadas { get; set; }

        public int Encoladas { get; set; }

        public int Suprimidas { get; set; }

        public int Fallidas { get; set; }

        public void Sumar(ResultadoDeEnvioDeAlerta resultado)
        {
            switch (resultado)
            {
                case ResultadoDeEnvioDeAlerta.Enviado:
                    Enviadas++;
                    break;
                case ResultadoDeEnvioDeAlerta.Encolado:
                    Encoladas++;
                    break;
                default:
                    Suprimidas++;
                    break;
            }
        }
    }
}
