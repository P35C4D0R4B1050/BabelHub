using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>
/// El alcance de las operaciones de la propia cuenta (AGENTS.md §5): solo la persona autenticada opera sobre su cuenta; el actor se compara con el identificador <b>en la Aplicación</b>, no solo en el controlador.
/// Otro actor (otro cliente, el personal, el sistema, un visitante) recibe <c>Prohibido</c> y la denegación queda registrada sin interrumpir.
/// </summary>
public sealed class AlcanceDeLaCuenta(IActorActual actor, IDenegacionDeAcceso denegaciones)
{
    public static ErrorDeAplicacion Prohibido { get; } = new(CategoriaDeError.Prohibido, "acceso-denegado");

    public static ErrorDeAplicacion CuentaNoEncontrada { get; } = new(CategoriaDeError.NoEncontrado, "cuenta-no-encontrada");

    /// <summary><see langword="null"/> si quien llama es el titular; si no, el rechazo (ya registrado).</summary>
    public async Task<ErrorDeAplicacion?> Comprobar(Guid usuarioId, CancellationToken cancelacion)
    {
        if (actor.Actual is { Tipo: TipoDeActor.Usuario, UsuarioId: { } propio } && propio == usuarioId)
        {
            return null;
        }

        try
        {
            await denegaciones.Registrar(new DenegacionDeAcceso("recurso-ajeno", ruta: null), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // IDenegacionDeAcceso nunca interrumpe y deja su alerta técnica si falla.
        }

        return Prohibido;
    }
}

/// <summary>Los dos cupos del segundo factor (SEG-20, SEG-29): se reservan antes de verificar un código y se devuelven al acertar, porque los límites cuentan los códigos <b>incorrectos</b>.</summary>
public static class LimitesDelSegundoFactor
{
    public static async Task<(ErrorDeAplicacion? Error, List<ReservaDeLimite> Reservas)> Reservar(IControlDeLimites limites, Guid usuarioId, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(limites);
        var reservas = new List<ReservaDeLimite>();
        var sujeto = usuarioId.ToString("N");
        var porOrigen = await limites.Reservar(PoliticasDeLimite.SegundoFactorPorOrigen, sujeto, cancelacion: cancelacion).ConfigureAwait(false);
        if (LimitesDeIdentidad.Rechazo(porOrigen) is { } rechazoPorOrigen)
        {
            return (rechazoPorOrigen, reservas);
        }

        if (porOrigen.Reserva is { } reservaPorOrigen)
        {
            reservas.Add(reservaPorOrigen);
        }

        var porCuenta = await limites.Reservar(PoliticasDeLimite.SegundoFactorPorCuenta, sujeto, cancelacion: cancelacion).ConfigureAwait(false);
        if (porCuenta.Reserva is { } reservaPorCuenta)
        {
            reservas.Add(reservaPorCuenta);
        }

        return (LimitesDeIdentidad.Rechazo(porCuenta), reservas);
    }

    public static async Task Devolver(IControlDeLimites limites, IEnumerable<ReservaDeLimite> reservas, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(limites);
        ArgumentNullException.ThrowIfNull(reservas);
        foreach (var reserva in reservas)
        {
            await limites.Liberar(reserva, cancelacion).ConfigureAwait(false);
        }
    }
}

/// <summary>Un código del segundo factor ya evaluado por el Dominio y aún sin aceptar en la base: el paso TOTP o la huella del código de respaldo, y cuántos respaldos quedarían.</summary>
public sealed record CodigoPresentado(long? Paso, string? HuellaDeRespaldo, int RespaldosRestantes);

/// <summary>
/// La reautenticación de quien ya tiene sesión antes de una operación sensible (IDE-25, IDE-37, IDE-52): la contraseña actual y, si hace falta, un código del segundo factor. Una contraseña equivocada cuenta
/// contra el límite de ingresos fallidos de la cuenta (se reserva <b>antes</b> de verificar y se devuelve al acertar) y deja el evento de seguridad; un código equivocado cuenta contra los dos límites del segundo factor.
/// Evaluar un código no toca la base: aceptarlo (paso o respaldo) es una actualización condicional atómica que hace el caso de uso dentro de su transacción (D04).
/// </summary>
public sealed class Reautenticacion(
    IHasherDeContrasenas hasher,
    IControlDeLimites limites,
    ICalculadorDeHuella huellas,
    ISegundoFactorAlmacen segundoFactor,
    ICifradorDeSecretos cifrador,
    IHuellasDeIdentidad huellasDeIdentidad,
    EventosDeSeguridadDeIdentidad eventos,
    IReloj reloj)
{
    /// <summary>La contraseña actual equivocada o ausente: sin decir cuál dato falló más allá del campo que se pidió.</summary>
    public static ErrorDeAplicacion ContrasenaActualIncorrecta { get; } = FallosDeIdentidad.Validacion("contrasenaActual", "incorrecta");

    public async Task<ErrorDeAplicacion?> ContrasenaActual(CuentaDeIdentidad cuenta, ValorSecreto? presentada, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(cuenta);
        var usuarioId = cuenta.Usuario.Id;
        var reserva = await limites.Reservar(PoliticasDeLimite.IngresosFallidos, huellaDelLlamador: huellas.DeSujeto(usuarioId.ToString("N"), "reautenticacion"), cancelacion: cancelacion).ConfigureAwait(false);
        if (LimitesDeIdentidad.Rechazo(reserva) is { } rechazo)
        {
            if (reserva.Estado == EstadoDeLimite.Excedido)
            {
                await eventos.BloqueoTemporal(usuarioId, cancelacion).ConfigureAwait(false);
            }

            return rechazo;
        }

        var contrasena = Contrasena.Presentada(presentada?.Revelar());
        var coincide = contrasena is not null && await hasher.Verificar(cuenta.ResumenDeAcceso, contrasena, cancelacion).ConfigureAwait(false);
        if (!coincide)
        {
            await eventos.IngresoFallido(usuarioId, cancelacion).ConfigureAwait(false);
            return ContrasenaActualIncorrecta;
        }

        if (reserva.Reserva is { } cupo)
        {
            await limites.Liberar(cupo, cancelacion).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>Lo que decide el Dominio sobre el código (sin tocar la base): un TOTP de la ventana aceptable o, si se permite, un código de respaldo de los que quedan; <see langword="null"/> si no vale.</summary>
    public CodigoPresentado? Evaluar(Guid usuarioId, SegundoFactorGuardado guardado, string? codigo, bool permiteRespaldo)
    {
        ArgumentNullException.ThrowIfNull(guardado);
        if (codigo is null || !guardado.Factor.EstaActivo)
        {
            return null;
        }

        if (CodigoDeUnSoloUso.TieneLaForma(codigo))
        {
            var secreto = cifrador.Descifrar(guardado.SecretoCifrado, usuarioId);
            return guardado.Factor.Presentar(secreto, codigo, reloj.Ahora) == ResultadoDeSegundoFactor.Aceptado
                ? new CodigoPresentado(guardado.Factor.UltimoPasoUsado, null, guardado.Factor.CodigosDeRespaldoRestantes)
                : null;
        }

        if (permiteRespaldo && CodigosDeRespaldo.TieneLaForma(codigo))
        {
            var huella = huellasDeIdentidad.DeCodigoDeRespaldo(usuarioId, CodigosDeRespaldo.Canonico(codigo));
            var uso = guardado.Factor.UsarCodigoDeRespaldo(huella);
            return uso.Aceptado ? new CodigoPresentado(null, huella, uso.Restantes) : null;
        }

        return null;
    }

    /// <summary>Acepta el código evaluado con la actualización condicional atómica: <see langword="false"/> si otra solicitud lo aceptó antes.</summary>
    public Task<bool> Aceptar(Guid usuarioId, CodigoPresentado presentado, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(presentado);
        return presentado.Paso is { } paso
            ? segundoFactor.AceptarPaso(usuarioId, paso, cancelacion)
            : segundoFactor.ConsumirCodigoDeRespaldo(usuarioId, presentado.HuellaDeRespaldo!, reloj.Ahora, cancelacion);
    }

    public Task CodigoIncorrecto(Guid usuarioId, CancellationToken cancelacion) => eventos.SegundoFactorFallido(usuarioId, cancelacion);
}
