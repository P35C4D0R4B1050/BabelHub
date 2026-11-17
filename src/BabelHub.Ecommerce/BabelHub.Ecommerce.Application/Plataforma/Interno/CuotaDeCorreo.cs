using BabelHub.Ecommerce.Domain.Plataforma;

namespace BabelHub.Ecommerce.Application.Plataforma.Interno;

/// <summary>Lo que decidió la cuota para un correo: sale (con su reserva, que se devuelve si el envío falla), se encola con su prioridad o se suprime.</summary>
public sealed record DecisionDeSalidaDeCorreo
{
    private DecisionDeSalidaDeCorreo(ResultadoDeEnvio resultado, ReservaDeCupoDeCorreo? reserva, int prioridad)
    {
        Resultado = resultado;
        Reserva = reserva;
        Prioridad = prioridad;
    }

    public ResultadoDeEnvio Resultado { get; }

    /// <summary>La reserva de un correo que sale; <see langword="null"/> si se encola o se suprime.</summary>
    public ReservaDeCupoDeCorreo? Reserva { get; }

    /// <summary>La prioridad con la que se reintenta un correo encolado (0 es la primera).</summary>
    public int Prioridad { get; }

    public static DecisionDeSalidaDeCorreo Sale(ReservaDeCupoDeCorreo reserva) => new(ResultadoDeEnvio.Enviado, reserva, 0);

    public static DecisionDeSalidaDeCorreo Encola(int prioridad) => new(ResultadoDeEnvio.Encolado, null, prioridad);

    public static DecisionDeSalidaDeCorreo Suprime() => new(ResultadoDeEnvio.Suprimido, null, 0);
}

/// <summary>
/// La cuota diaria de correo aplicada (IDE-66, IDE-73, IDE-77, IDE-78, IDE-80, IDE-82): elige los cupos de la categoría, reserva en el almacén de forma atómica, aplica el tope de 24 al día por motivo y
/// servicio a las alertas a la administración, devuelve el cupo si el envío falla y publica <b>una</b> alerta técnica la vez que la cuota total o el cupo de códigos cruza el 80 %. La usa el
/// enviador de <c>Plataforma</c> (T34); no manda correo.
/// </summary>
public sealed class CuotaDeCorreo(PoliticaDeCuotaDeCorreo politica, IAlmacenDeCuotaDeCorreo almacen, IPublicadorDeAlertasTecnicas alertas, IReloj reloj)
{
    /// <summary>El motivo de la alerta técnica al llegar la cuota total al umbral (IDE-73).</summary>
    public const string MotivoDeCuotaAlta = "cuota-de-correo-alta";

    /// <summary>El motivo de la alerta técnica al llegar el cupo de códigos al umbral (IDE-73).</summary>
    public const string MotivoDeCupoDeCodigosAlto = "cupo-de-codigos-alto";

    public static ClaseDeCorreoParaCuota Clase(CategoriaDeCorreo categoria) => categoria switch
    {
        CategoriaDeCorreo.AlertaALaAdministracion => ClaseDeCorreoParaCuota.AlertaALaAdministracion,
        CategoriaDeCorreo.AlertaCriticaDeCuenta => ClaseDeCorreoParaCuota.AlertaCriticaDeCuenta,
        CategoriaDeCorreo.CodigoDeVerificacionORecuperacion => ClaseDeCorreoParaCuota.Codigo,
        CategoriaDeCorreo.AvisoDeSeguridad => ClaseDeCorreoParaCuota.AvisoNoCritico,
        CategoriaDeCorreo.RespuestaDeReclamo => ClaseDeCorreoParaCuota.Reclamo,
        CategoriaDeCorreo.Pago => ClaseDeCorreoParaCuota.Pago,
        _ => throw new ArgumentOutOfRangeException(nameof(categoria), categoria, "Categoría de correo desconocida."),
    };

    /// <summary>
    /// Decide si un correo sale. Una alerta a la administración supera primero el tope por motivo y servicio (IDE-82: lo que lo supera se suprime y queda en la lista de alertas); después se reserva en
    /// los cupos de su categoría. Sin cupo, se suprime o se encola según la categoría. Una falla del almacén no es una decisión: se propaga para que el enviador la trate (el correo no se pierde: se encola
    /// o se informa como falla del envío, nunca como enviado).
    /// </summary>
    public async Task<DecisionDeSalidaDeCorreo> Decidir(CorreoSaliente correo, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(correo);
        return await Decidir(Clase(correo.Categoria), correo.Motivo, correo.Servicio, cancelacion).ConfigureAwait(false);
    }

    /// <summary>Decide si sale un código de un solo uso (su cupo es el de códigos; sin cupo, se suprime).</summary>
    public Task<DecisionDeSalidaDeCorreo> DecidirCodigo(CancellationToken cancelacion = default) =>
        Decidir(ClaseDeCorreoParaCuota.Codigo, motivo: null, servicio: null, cancelacion);

    /// <summary>Devuelve el lugar de un envío que falló: la cuota solo cuenta lo que salió (IDE-66).</summary>
    public Task Devolver(ReservaDeCupoDeCorreo reserva, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(reserva);
        return almacen.Devolver(reloj.HoyEnLima, reserva.Cupo, cancelacion);
    }

    private async Task<DecisionDeSalidaDeCorreo> Decidir(ClaseDeCorreoParaCuota clase, string? motivo, string? servicio, CancellationToken cancelacion)
    {
        var dia = reloj.HoyEnLima;
        if (clase == ClaseDeCorreoParaCuota.AlertaALaAdministracion
            && !await almacen.ReservarAlertaDeAdministracion(dia, motivo!, servicio!, politica.Configuracion.TopeDeAlertasPorMotivoYServicio, cancelacion).ConfigureAwait(false))
        {
            return DecisionDeSalidaDeCorreo.Suprime();
        }

        var reserva = await almacen.Reservar(dia, politica.Candidatos(clase), cancelacion).ConfigureAwait(false);
        if (reserva is null)
        {
            return PoliticaDeCuotaDeCorreo.SinCupo(clase) == SinCupoDeCorreo.Encolar
                ? DecisionDeSalidaDeCorreo.Encola(PoliticaDeCuotaDeCorreo.Prioridad(clase))
                : DecisionDeSalidaDeCorreo.Suprime();
        }

        await AlertarSiCruzaElUmbral(reserva).ConfigureAwait(false);
        return DecisionDeSalidaDeCorreo.Sale(reserva);
    }

    private async Task AlertarSiCruzaElUmbral(ReservaDeCupoDeCorreo reserva)
    {
        var configuracion = politica.Configuracion;
        if (politica.CruzaElUmbral(reserva.EnviadosEnTotal - 1, reserva.EnviadosEnTotal, configuracion.TotalDiario))
        {
            await PublicarSinInterrumpir(MotivoDeCuotaAlta).ConfigureAwait(false);
        }

        if (reserva.Cupo == CupoDeCorreo.Codigos && politica.CruzaElUmbral(reserva.EnviadosEnElCupo - 1, reserva.EnviadosEnElCupo, configuracion.Tamano(CupoDeCorreo.Codigos)))
        {
            await PublicarSinInterrumpir(MotivoDeCupoDeCodigosAlto).ConfigureAwait(false);
        }
    }

    private async Task PublicarSinInterrumpir(string motivo)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(motivo, reloj.Ahora)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Una alerta que no se puede publicar no impide el correo (AUD-40).
        }
    }
}
