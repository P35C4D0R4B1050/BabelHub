using System.Text;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>Codificación Base32 (RFC 4648, sin relleno) del secreto del segundo factor, como la leen las aplicaciones de autenticación.</summary>
public static class Base32
{
    private const string Alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Codificar(ReadOnlySpan<byte> datos)
    {
        var resultado = new StringBuilder((datos.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;
        foreach (var b in datos)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                resultado.Append(Alfabeto[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            resultado.Append(Alfabeto[(buffer << (5 - bits)) & 31]);
        }

        return resultado.ToString();
    }
}

/// <summary>La categoría de cada correo de seguridad de Identidad (T29; IDE-67, IDE-74, IDE-76, IDE-78): una sola clasificación para toda la cuenta, de modo que ningún caso de uso decida su propio trato.</summary>
public static class ClasificacionDeAvisosDeCuenta
{
    private static readonly IReadOnlyDictionary<string, CategoriaDeCorreo> Categorias = new Dictionary<string, CategoriaDeCorreo>(StringComparer.Ordinal)
    {
        // Críticos (IDE-74): dispositivo o región no reconocidos, restablecimiento del segundo factor y cambio de contraseña.
        [AvisosDeCuenta.PlantillaDeContrasenaCambiada] = CategoriaDeCorreo.AlertaCriticaDeCuenta,
        [AvisosDeCuenta.PlantillaDeSegundoFactorRestablecido] = CategoriaDeCorreo.AlertaCriticaDeCuenta,
        [AvisosDeCuenta.PlantillaDeCambioDeCorreoNoReconocido] = CategoriaDeCorreo.AlertaCriticaDeCuenta,

        // No críticos: sin cupo no salen y quedan en la cuenta (IDE-79).
        [AvisosDeCuenta.PlantillaDeCancelacionDiferida] = CategoriaDeCorreo.AvisoDeSeguridad,
        [AvisosDeCuenta.PlantillaDePendientesCerrados] = CategoriaDeCorreo.AvisoDeSeguridad,
        [AvisosDeCuenta.PlantillaDeCancelacionDescartada] = CategoriaDeCorreo.AvisoDeSeguridad,
        [AvisosDeCuenta.PlantillaDeSolicitudDeDatosRespondida] = CategoriaDeCorreo.AvisoDeSeguridad,
        [AvisosDeCuenta.PlantillaDeCorreoCambiado] = CategoriaDeCorreo.AvisoDeSeguridad,
    };

    /// <summary>Las plantillas de este módulo y su categoría.</summary>
    public static IReadOnlyDictionary<string, CategoriaDeCorreo> Todas => Categorias;

    public static CategoriaDeCorreo De(string plantilla) =>
        Categorias.TryGetValue(plantilla, out var categoria) ? categoria : throw new ArgumentOutOfRangeException(nameof(plantilla), plantilla, "Plantilla sin clasificar.");

    /// <summary>
    /// Las plantillas cuyo caso de uso <b>ya deja su propio aviso en la cuenta</b> en la transacción del hecho (cancelación, cambio de correo, solicitud sobre datos): si el correo se suprime, no se duplica. Las demás (la
    /// contraseña y el segundo factor) lo dejan aquí al suprimirse (IDE-79).
    /// </summary>
    public static bool YaDejaSuAvisoEnLaCuenta(string plantilla) =>
        plantilla is AvisosDeCuenta.PlantillaDeCancelacionDiferida or AvisosDeCuenta.PlantillaDePendientesCerrados or AvisosDeCuenta.PlantillaDeCancelacionDescartada
            or AvisosDeCuenta.PlantillaDeSolicitudDeDatosRespondida or AvisosDeCuenta.PlantillaDeCorreoCambiado or AvisosDeCuenta.PlantillaDeCambioDeCorreoNoReconocido;
}

/// <summary>
/// El aviso de seguridad por correo (T28 y T29; IDE-45, IDE-67, IDE-74, IDE-76, IDE-79, IDE-86): <b>sin datos sensibles ni enlaces</b>. Antes de enviar, <b>un correo sin verificar no recibe avisos</b> (solo se muestran
/// en la cuenta, IDE-86); un aviso no crítico cuenta contra el tope de la cuenta (3 por 24 horas) y una alerta crítica es <b>la primera de su tipo por cuenta en 24 horas</b> y cuenta contra el tope diario del origen. Un aviso
/// que no sale por un tope, por la cuota o por el correo sin verificar deja un <b>evento de seguridad</b> y, si su caso de uso no lo dejó, un aviso en la cuenta. Si el almacén de contadores no responde, un aviso no crítico se
/// suprime (falla cerrada) y uno crítico sale igual: su cupo exclusivo de la cuota ya lo acota. <b>Nunca interrumpe</b> la operación (AGENTS.md §3.10).
/// </summary>
public sealed class AvisosDeCuenta(
    IEnviadorDeCorreo correo,
    IControlDeLimites limites,
    EventosDeSeguridadDeIdentidad eventos,
    IAvisosDeCuentaAlmacen avisosDeCuenta,
    IUnidadDeTrabajo unidad,
    IReloj reloj)
{
    public const string PlantillaDeContrasenaCambiada = "contrasena-cambiada";

    public const string PlantillaDeSegundoFactorRestablecido = "segundo-factor-restablecido";

    public const string PlantillaDeCancelacionDiferida = "cancelacion-diferida";

    public const string PlantillaDePendientesCerrados = "cancelacion-pendientes-cerrados";

    public const string PlantillaDeCancelacionDescartada = "cancelacion-descartada";

    public const string PlantillaDeSolicitudDeDatosRespondida = "solicitud-de-datos-respondida";

    public const string PlantillaDeCambioDeCorreoNoReconocido = "cambio-de-correo-no-reconocido";

    public const string PlantillaDeCorreoCambiado = "correo-cambiado";

    public const string MotivoCuotaDeCorreo = "cuota-de-correo";

    public const string MotivoTopePorCuenta = "tope-por-cuenta";

    public const string MotivoTopePorOrigen = "tope-por-origen";

    public const string MotivoCorreoSinVerificar = "correo-sin-verificar";

    /// <summary>El aviso de IDE-27: la cancelación quedó diferida por los pendientes y puede desistir. Devuelve si <b>salió</b> por correo.</summary>
    public Task<bool> CancelacionDiferida(Usuario usuario, CancellationToken cancelacion) => Avisar(usuario, PlantillaDeCancelacionDiferida, cancelacion);

    /// <summary>El aviso de IDE-68: se cerraron los pendientes y puede reconfirmar. Si no sale, el plazo de 7 días cuenta desde que se muestre en la cuenta (IDE-76).</summary>
    public Task<bool> PendientesCerrados(Usuario usuario, CancellationToken cancelacion) => Avisar(usuario, PlantillaDePendientesCerrados, cancelacion);

    /// <summary>El aviso de IDE-81: la cancelación se descartó y puede volver a comprar.</summary>
    public Task<bool> CancelacionDescartada(Usuario usuario, CancellationToken cancelacion) => Avisar(usuario, PlantillaDeCancelacionDescartada, cancelacion);

    /// <summary>El aviso de que la administración respondió una solicitud sobre datos (IDE-26): <b>sin el contenido</b> de la respuesta, que el titular ve en su cuenta.</summary>
    public Task SolicitudDeDatosRespondida(Usuario usuario, CancellationToken cancelacion) => Avisar(usuario, PlantillaDeSolicitudDeDatosRespondida, cancelacion);

    public Task ContrasenaCambiada(Usuario usuario, CancellationToken cancelacion) => Avisar(usuario, PlantillaDeContrasenaCambiada, cancelacion);

    /// <summary>La alerta al correo <b>anterior</b> por un cambio de correo desde un dispositivo o una región no reconocidos (IDE-54): crítica, sin enlaces ni el correo nuevo.</summary>
    public Task CambioDeCorreoNoReconocido(Usuario usuario, CancellationToken cancelacion) => Avisar(usuario, PlantillaDeCambioDeCorreoNoReconocido, cancelacion);

    /// <summary>El aviso al correo <b>nuevo</b> ya aplicado el cambio (IDE-53); nada sale al correo anterior.</summary>
    public Task CorreoCambiado(Usuario usuarioConElCorreoNuevo, CancellationToken cancelacion) => Avisar(usuarioConElCorreoNuevo, PlantillaDeCorreoCambiado, cancelacion);

    /// <summary>El aviso al titular cuando la administración restablece su segundo factor (IDE-38): sin enlaces ni códigos.</summary>
    public Task SegundoFactorRestablecido(Usuario usuario, CancellationToken cancelacion) => Avisar(usuario, PlantillaDeSegundoFactorRestablecido, cancelacion);

    /// <summary>Envía el aviso; devuelve verdadero solo si <b>salió</b> (no si se encoló o se suprimió: el plazo de IDE-68 no empieza sin un aviso que el titular haya podido ver, IDE-76).</summary>
    private async Task<bool> Avisar(Usuario usuario, string plantilla, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        var categoria = ClasificacionDeAvisosDeCuenta.De(plantilla);
        if (!usuario.CorreoVerificado)
        {
            // Un aviso de seguridad no sale a un correo sin verificar (IDE-86): solo se muestra en la cuenta.
            await SuprimirSinInterrumpir(usuario.Id, plantilla, MotivoCorreoSinVerificar, cancelacion).ConfigureAwait(false);
            return false;
        }

        var reservas = new List<ReservaDeLimite>();
        var motivoDeTope = await ReservarTopes(usuario.Id, plantilla, categoria, reservas, cancelacion).ConfigureAwait(false);
        if (motivoDeTope is not null)
        {
            await SuprimirSinInterrumpir(usuario.Id, plantilla, motivoDeTope, cancelacion).ConfigureAwait(false);
            return false;
        }

        try
        {
            var resultado = await correo.Enviar(new CorreoSaliente(categoria, usuario.Correo, plantilla), cancelacion).ConfigureAwait(false);
            if (resultado == ResultadoDeEnvio.Suprimido)
            {
                await DevolverLosTopes(reservas).ConfigureAwait(false);
                await SuprimirSinInterrumpir(usuario.Id, plantilla, MotivoCuotaDeCorreo, cancelacion).ConfigureAwait(false);
            }

            return resultado == ResultadoDeEnvio.Enviado;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // La operación ya se hizo; el correo no es necesario para ella. Un aviso que no salió no gasta el tope.
            await DevolverLosTopes(reservas).ConfigureAwait(false);
            return false;
        }
    }

    /// <summary>
    /// Reserva los topes de la categoría y devuelve el motivo del evento si alguno se superó, o <see langword="null"/> si el aviso puede salir. Un aviso no crítico cuenta contra el tope de la cuenta (falla cerrada); una
    /// alerta crítica contra «la primera de su tipo» de la cuenta y contra el tope diario del origen (si el almacén no responde, sale igual). Los cupos tomados quedan en <paramref name="reservas"/> para devolverlos si el
    /// aviso no sale.
    /// </summary>
    private async Task<string?> ReservarTopes(Guid usuarioId, string plantilla, CategoriaDeCorreo categoria, List<ReservaDeLimite> reservas, CancellationToken cancelacion)
    {
        try
        {
            if (categoria == CategoriaDeCorreo.AvisoDeSeguridad)
            {
                var porCuenta = await limites.Reservar(PoliticasDeLimite.AvisosNoCriticosPorCuenta, usuarioId.ToString("N"), cancelacion: cancelacion).ConfigureAwait(false);
                if (porCuenta.Reserva is { } propia)
                {
                    reservas.Add(propia);
                }

                return porCuenta.EsPermitido ? null : MotivoTopePorCuenta;
            }

            var porTipo = await limites.Reservar(PoliticasDeLimite.AlertasCriticasPorTipoYCuenta, $"{usuarioId:N}.{plantilla}", cancelacion: cancelacion).ConfigureAwait(false);
            if (porTipo.Estado == EstadoDeLimite.Excedido)
            {
                return MotivoTopePorCuenta;
            }

            if (porTipo.Reserva is { } deTipo)
            {
                reservas.Add(deTipo);
            }

            var porOrigen = await limites.Reservar(PoliticasDeLimite.AlertasCriticasPorOrigen, cancelacion: cancelacion).ConfigureAwait(false);
            if (porOrigen.Estado == EstadoDeLimite.Excedido)
            {
                await DevolverLosTopes(reservas).ConfigureAwait(false);
                return MotivoTopePorOrigen;
            }

            if (porOrigen.Reserva is { } deOrigen)
            {
                reservas.Add(deOrigen);
            }

            return null;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return categoria == CategoriaDeCorreo.AvisoDeSeguridad ? MotivoTopePorCuenta : null;
        }
    }

    private async Task DevolverLosTopes(List<ReservaDeLimite> reservas)
    {
        foreach (var reserva in reservas)
        {
            try
            {
                // Con CancellationToken.None: un aviso que no salió no debe gastar el tope aunque quien llama haya cancelado.
                await limites.Liberar(reserva, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Lo peor es contar un aviso de más hasta que venza la ventana.
            }
        }

        reservas.Clear();
    }

    /// <summary>El evento de seguridad de la supresión y, si el caso de uso no la dejó, el aviso en la cuenta (IDE-79). Nada de esto interrumpe la operación.</summary>
    private async Task SuprimirSinInterrumpir(Guid usuarioId, string plantilla, string motivo, CancellationToken cancelacion)
    {
        await eventos.AvisoSuprimido(usuarioId, motivo, cancelacion).ConfigureAwait(false);
        if (ClasificacionDeAvisosDeCuenta.YaDejaSuAvisoEnLaCuenta(plantilla))
        {
            return;
        }

        try
        {
            var ahora = reloj.Ahora;
            await unidad.Ejecutar(
                async ct =>
                {
                    await avisosDeCuenta.Agregar(usuarioId, plantilla, ahora, ct).ConfigureAwait(false);
                    return true;
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // El aviso en la cuenta es un respaldo de visibilidad; si no se guarda, el evento de seguridad ya quedó.
        }
    }
}

/// <summary>
/// El ingreso en dos pasos (T15; IDE-02, IDE-03, IDE-08, IDE-19 a IDE-23, IDE-42, IDE-50): la credencial de 15 minutos solo sale cuando se cumplió todo lo que le toca a la persona. <b>Datos incorrectos, cuenta
/// inexistente, desactivada, cancelada o con la contraseña temporal vencida responden lo mismo</b> y cuestan lo mismo (cálculo señuelo); se reserva el límite de ingresos fallidos por cuenta y origen <b>antes</b> de
/// verificar y se devuelve al acertar la contraseña (SEG-06). El personal, y el cliente que lo activó, presentan además un TOTP o un código de respaldo; el personal sin segundo factor lo configura antes de recibir
/// credencial (IDE-21); una contraseña temporal solo permite el desafío de cambio (IDE-23). Cada desafío dura 5 minutos como máximo, se consume una sola vez y solo permite lo de su modo.
/// </summary>
public sealed class Ingreso(
    IRepositorioDeCuentas cuentas,
    IHasherDeContrasenas hasher,
    IListaDeContrasenasComunes comunes,
    IControlDeLimites limites,
    ICalculadorDeHuella huellas,
    IRepositorioDeDesafios desafios,
    IHuellasDeIdentidad huellasDeIdentidad,
    IGeneradorDeSecretos secretos,
    ISegundoFactorAlmacen segundoFactor,
    ICifradorDeSecretos cifrador,
    EmisionDeSesion sesiones,
    IRepositorioDeRenovaciones renovaciones,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    EventosDeSeguridadDeIdentidad eventos,
    AvisosDeCuenta avisos,
    IReloj reloj) : IIngreso
{
    public const string Emisor = "BabelHub";

    /// <summary>Otro desafío igual ganó la carrera por consumirlo: se revierte la transacción entera y el rechazo es el del desafío inválido.</summary>
    private sealed class DesafioYaConsumidoException : Exception;

    public async Task<Resultado<ResultadoDelIngreso>> Ingresar(SolicitudDeIngreso solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var correoValido = EntradasDeIdentidad.TryNormalizarElCorreo(solicitud.Correo, out var correo);
        var presentada = Contrasena.Presentada(solicitud.Contrasena?.Revelar());

        // La reserva va antes de todo y cuenta igual los correos que no existen (SEG-06).
        var reserva = await limites.Reservar(PoliticasDeLimite.IngresosFallidos, huellaDelLlamador: huellas.DeSujeto(correoValido ? correo : "invalido", "ingresos-fallidos"), cancelacion: cancelacion).ConfigureAwait(false);
        if (!reserva.EsPermitido)
        {
            if (reserva.Estado == EstadoDeLimite.Excedido)
            {
                await eventos.BloqueoTemporal(null, cancelacion).ConfigureAwait(false);
            }

            return Resultado<ResultadoDelIngreso>.Fallo(LimitesDeIdentidad.Rechazo(reserva)!);
        }

        var cuenta = correoValido ? await cuentas.PorCorreo(correo, cancelacion).ConfigureAwait(false) : null;
        var coincide = presentada is not null && await hasher.Verificar(cuenta?.ResumenDeAcceso, presentada, cancelacion).ConfigureAwait(false);
        var ahora = reloj.Ahora;
        var temporalVencida = cuenta is { DebeCambiarLaTemporal: true, ContrasenaTemporalHasta: { } hasta } && ahora >= hasta;
        if (cuenta is null || !coincide || !cuenta.Usuario.Activo || cuenta.CanceladaEn is not null || temporalVencida)
        {
            await eventos.IngresoFallido(cuenta?.Usuario.Id, cancelacion).ConfigureAwait(false);
            return Resultado<ResultadoDelIngreso>.Fallo(FallosDeIdentidad.CredencialesInvalidas);
        }

        // La contraseña es correcta: el cupo de ingresos fallidos se devuelve (el segundo factor tiene sus propios límites).
        await limites.Liberar(reserva.Reserva!, cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDelIngreso>.Exito(await SiguientePaso(cuenta, creado: null, cancelacion).ConfigureAwait(false));
    }

    public async Task<Resultado<ResultadoDelIngreso>> PresentarSegundoFactor(ValorSecreto desafio, ValorSecreto codigo, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(desafio);
        ArgumentNullException.ThrowIfNull(codigo);
        var vigente = await DesafioVigente(desafio, AccionConDesafio.PresentarSegundoFactor, cancelacion).ConfigureAwait(false);
        if (vigente is null)
        {
            return Resultado<ResultadoDelIngreso>.Fallo(FallosDeIdentidad.DesafioInvalido);
        }

        var cuenta = await cuentas.PorId(vigente.UsuarioId, cancelacion: cancelacion).ConfigureAwait(false);
        if (cuenta is not { CanceladaEn: null, Usuario.Activo: true })
        {
            return Resultado<ResultadoDelIngreso>.Fallo(FallosDeIdentidad.CredencialesInvalidas);
        }

        var usuarioId = cuenta.Usuario.Id;
        var (rechazo, reservas) = await LimitesDelSegundoFactor.Reservar(limites, usuarioId, cancelacion).ConfigureAwait(false);
        if (rechazo is not null)
        {
            return Resultado<ResultadoDelIngreso>.Fallo(rechazo);
        }

        var guardado = await segundoFactor.Obtener(usuarioId, cancelacion).ConfigureAwait(false);
        if (guardado is not { Factor.EstaActivo: true })
        {
            return Resultado<ResultadoDelIngreso>.Fallo(FallosDeIdentidad.DesafioInvalido);
        }

        var texto = codigo.Revelar();
        var ahora = reloj.Ahora;

        // Lo que decide el Dominio (sin tocar la base): si el código es un TOTP del paso aceptable o un código de respaldo de los que quedan.
        long? paso = null;
        string? huellaDeRespaldo = null;
        var restantes = 0;
        if (CodigoDeUnSoloUso.TieneLaForma(texto))
        {
            var secreto = cifrador.Descifrar(guardado.SecretoCifrado, usuarioId);
            if (guardado.Factor.Presentar(secreto, texto, ahora) == ResultadoDeSegundoFactor.Aceptado)
            {
                paso = guardado.Factor.UltimoPasoUsado;
            }
        }
        else if (CodigosDeRespaldo.TieneLaForma(texto))
        {
            var huella = huellasDeIdentidad.DeCodigoDeRespaldo(usuarioId, CodigosDeRespaldo.Canonico(texto));
            var uso = guardado.Factor.UsarCodigoDeRespaldo(huella);
            if (uso.Aceptado)
            {
                huellaDeRespaldo = huella;
                restantes = uso.Restantes;
            }
        }

        if (paso is null && huellaDeRespaldo is null)
        {
            await eventos.SegundoFactorFallido(usuarioId, cancelacion).ConfigureAwait(false);
            return Resultado<ResultadoDelIngreso>.Fallo(FallosDeIdentidad.CodigoInvalido);
        }

        // Aceptar el paso o consumir el código de respaldo, consumir el desafío, auditar y emitir la sesión son UNA transacción: si la auditoría del código de respaldo falla, no se ingresa ni se consume nada (AUD-04, IDE-22).
        var perdioLaCarrera = false;
        Resultado<ResultadoDelIngreso> resultado;
        try
        {
            resultado = await unidad.Ejecutar(
                async ct =>
                {
                    var aceptado = paso is { } p
                        ? await segundoFactor.AceptarPaso(usuarioId, p, ct).ConfigureAwait(false)
                        : await segundoFactor.ConsumirCodigoDeRespaldo(usuarioId, huellaDeRespaldo!, ahora, ct).ConfigureAwait(false);
                    if (!aceptado)
                    {
                        perdioLaCarrera = true;
                        return Resultado<ResultadoDelIngreso>.Fallo(FallosDeIdentidad.CodigoInvalido);
                    }
    
                    if (!vigente.Consumir(ahora, AccionConDesafio.PresentarSegundoFactor) || !await desafios.MarcarUsado(vigente.Id, ct).ConfigureAwait(false))
                    {
                        throw new DesafioYaConsumidoException();
                    }
    
                    if (huellaDeRespaldo is not null)
                    {
                        await auditoria.Registrar(
                            new SolicitudDeRegistro(AccionesAuditables.CodigoDeRespaldoUsado) { ObjetoTipo = "cuenta", ObjetoId = usuarioId.ToString("D"), Motivo = "uso-de-codigo-de-respaldo" },
                            ct).ConfigureAwait(false);
                    }
    
                    var credencial = await sesiones.Nueva(cuenta.Usuario, ahora, ct).ConfigureAwait(false);
                    return Resultado<ResultadoDelIngreso>.Exito(new ResultadoDelIngreso { Credencial = credencial, CodigosDeRespaldoRestantes = huellaDeRespaldo is null ? null : restantes });
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (DesafioYaConsumidoException)
        {
            return Resultado<ResultadoDelIngreso>.Fallo(FallosDeIdentidad.DesafioInvalido);
        }

        if (perdioLaCarrera)
        {
            await eventos.SegundoFactorFallido(usuarioId, cancelacion).ConfigureAwait(false);
        }

        if (resultado.EsExito)
        {
            await LimitesDelSegundoFactor.Devolver(limites, reservas, cancelacion).ConfigureAwait(false);
        }

        return resultado;
    }

    public async Task<Resultado<SecretoDelSegundoFactor>> IniciarConfiguracionDelSegundoFactor(ValorSecreto desafio, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(desafio);
        var vigente = await DesafioVigente(desafio, AccionConDesafio.ConfigurarSegundoFactor, cancelacion).ConfigureAwait(false);
        var cuenta = vigente is null ? null : await cuentas.PorId(vigente.UsuarioId, cancelacion: cancelacion).ConfigureAwait(false);
        if (cuenta is not { CanceladaEn: null, Usuario.Activo: true })
        {
            return Resultado<SecretoDelSegundoFactor>.Fallo(FallosDeIdentidad.DesafioInvalido);
        }

        // Un desafío de configuración rezagado no puede reemplazar el secreto de un segundo factor que otro ingreso ya activó (IDE-21).
        if (await segundoFactor.Obtener(cuenta.Usuario.Id, cancelacion).ConfigureAwait(false) is { Factor.EstaActivo: true })
        {
            return Resultado<SecretoDelSegundoFactor>.Fallo(FallosDeIdentidad.DesafioInvalido);
        }

        var secreto = secretos.SecretoDelSegundoFactor();
        await segundoFactor.GuardarSinConfirmar(cuenta.Usuario.Id, cifrador.Cifrar(secreto, cuenta.Usuario.Id), cancelacion).ConfigureAwait(false);
        return Resultado<SecretoDelSegundoFactor>.Exito(new SecretoDelSegundoFactor(new ValorSecreto(Base32.Codificar(secreto)), Emisor, cuenta.Usuario.Correo));
    }

    public async Task<Resultado<ResultadoDelIngreso>> ConfirmarElSegundoFactor(ValorSecreto desafio, ValorSecreto codigo, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(desafio);
        ArgumentNullException.ThrowIfNull(codigo);
        var vigente = await DesafioVigente(desafio, AccionConDesafio.ConfigurarSegundoFactor, cancelacion).ConfigureAwait(false);
        var cuenta = vigente is null ? null : await cuentas.PorId(vigente.UsuarioId, cancelacion: cancelacion).ConfigureAwait(false);
        if (cuenta is not { CanceladaEn: null, Usuario.Activo: true })
        {
            return Resultado<ResultadoDelIngreso>.Fallo(FallosDeIdentidad.DesafioInvalido);
        }

        var usuarioId = cuenta.Usuario.Id;
        var (rechazo, reservas) = await LimitesDelSegundoFactor.Reservar(limites, usuarioId, cancelacion).ConfigureAwait(false);
        if (rechazo is not null)
        {
            return Resultado<ResultadoDelIngreso>.Fallo(rechazo);
        }

        var guardado = await segundoFactor.Obtener(usuarioId, cancelacion).ConfigureAwait(false);
        if (guardado is null)
        {
            return Resultado<ResultadoDelIngreso>.Fallo(FallosDeIdentidad.DesafioInvalido);
        }

        var respaldo = CodigosDeRespaldo.Generar();
        var huellasDeRespaldo = respaldo.Select(c => huellasDeIdentidad.DeCodigoDeRespaldo(usuarioId, CodigosDeRespaldo.Canonico(c))).ToList();
        var ahora = reloj.Ahora;
        var confirmacion = guardado.Factor.Confirmar(cifrador.Descifrar(guardado.SecretoCifrado, usuarioId), codigo.Revelar(), ahora, huellasDeRespaldo);
        if (confirmacion != ResultadoDeSegundoFactor.Aceptado)
        {
            await eventos.SegundoFactorFallido(usuarioId, cancelacion).ConfigureAwait(false);
            return Resultado<ResultadoDelIngreso>.Fallo(confirmacion == ResultadoDeSegundoFactor.YaActivo ? FallosDeIdentidad.DesafioInvalido : FallosDeIdentidad.CodigoInvalido);
        }

        var confirmada = await unidad.Ejecutar(
            async ct =>
            {
                if (!vigente!.Consumir(ahora, AccionConDesafio.ConfigurarSegundoFactor) || !await desafios.MarcarUsado(vigente.Id, ct).ConfigureAwait(false))
                {
                    return Resultado<ResultadoDelIngreso>.Fallo(FallosDeIdentidad.DesafioInvalido);
                }

                await segundoFactor.Activar(usuarioId, guardado.Factor.UltimoPasoUsado!.Value, huellasDeRespaldo, ct).ConfigureAwait(false);
                await auditoria.Registrar(
                    new SolicitudDeRegistro(AccionesAuditables.SegundoFactorActivado) { ObjetoTipo = "cuenta", ObjetoId = usuarioId.ToString("D"), Motivo = "activacion" },
                    ct).ConfigureAwait(false);
                var credencial = await sesiones.Nueva(cuenta.Usuario, ahora, ct).ConfigureAwait(false);
                return Resultado<ResultadoDelIngreso>.Exito(new ResultadoDelIngreso { Credencial = credencial, CodigosDeRespaldo = [.. respaldo.Select(c => new ValorSecreto(c))] });
            },
            cancelacion).ConfigureAwait(false);

        if (confirmada.EsExito)
        {
            await LimitesDelSegundoFactor.Devolver(limites, reservas, cancelacion).ConfigureAwait(false);
        }

        return confirmada;
    }

    public async Task<Resultado<ResultadoDelIngreso>> CambiarLaContrasenaTemporal(ValorSecreto desafio, ValorSecreto contrasenaNueva, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(desafio);
        ArgumentNullException.ThrowIfNull(contrasenaNueva);
        var vigente = await DesafioVigente(desafio, AccionConDesafio.CambiarContrasenaTemporal, cancelacion).ConfigureAwait(false);
        var cuenta = vigente is null ? null : await cuentas.PorId(vigente.UsuarioId, cancelacion: cancelacion).ConfigureAwait(false);
        var ahora = reloj.Ahora;
        if (cuenta is not { CanceladaEn: null, Usuario.Activo: true, DebeCambiarLaTemporal: true } || (cuenta.ContrasenaTemporalHasta is { } hasta && ahora >= hasta))
        {
            return Resultado<ResultadoDelIngreso>.Fallo(FallosDeIdentidad.DesafioInvalido);
        }

        var (nueva, evaluacion) = Contrasena.Crear(contrasenaNueva.Revelar(), comunes.EsComun);
        if (nueva is null)
        {
            return Resultado<ResultadoDelIngreso>.Fallo(FallosDeIdentidad.Validacion("contrasena", evaluacion switch
            {
                ResultadoDeContrasena.Corta => "demasiado-corta",
                ResultadoDeContrasena.Larga => "demasiado-larga",
                ResultadoDeContrasena.Comun => "demasiado-comun",
                _ => "obligatoria",
            }));
        }

        var resumen = await hasher.Resumir(nueva, cancelacion).ConfigureAwait(false);
        Resultado<ResultadoDelIngreso> resultado;
        try
        {
            resultado = await unidad.Ejecutar(
                async ct =>
                {
                    if (!vigente!.Consumir(ahora, AccionConDesafio.CambiarContrasenaTemporal) || !await desafios.MarcarUsado(vigente.Id, ct).ConfigureAwait(false))
                    {
                        return Resultado<ResultadoDelIngreso>.Fallo(FallosDeIdentidad.DesafioInvalido);
                    }
    
                    // Se relee bajo el bloqueo de la fila: un cambio de la administración (desactivar, restablecer) entre la lectura y ahora no se pisa (IDE-08, IDE-44).
                    var actual = await cuentas.PorId(cuenta.Usuario.Id, paraActualizar: true, ct).ConfigureAwait(false);
                    if (actual is not { CanceladaEn: null, Usuario.Activo: true, DebeCambiarLaTemporal: true })
                    {
                        throw new DesafioYaConsumidoException();
                    }
    
                    actual.ResumenDeAcceso = resumen;
                    actual.ContrasenaTemporalHasta = null;
                    actual.DebeCambiarLaTemporal = false;
                    actual.Usuario.AvanzarElCorteDeSesiones(ahora);
                    await cuentas.Guardar(actual, ct).ConfigureAwait(false);
                    await renovaciones.RevocarTodas(actual.Usuario.Id, ahora, ct).ConfigureAwait(false);
                    await auditoria.Registrar(
                        new SolicitudDeRegistro(AccionesAuditables.ContrasenaCambiada) { ObjetoTipo = "cuenta", ObjetoId = actual.Usuario.Id.ToString("D"), Motivo = "cambio-por-el-titular" },
                        ct).ConfigureAwait(false);
                    return Resultado<ResultadoDelIngreso>.Exito(await SiguientePaso(actual, vigente.Creado, ct).ConfigureAwait(false));
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (DesafioYaConsumidoException)
        {
            return Resultado<ResultadoDelIngreso>.Fallo(FallosDeIdentidad.DesafioInvalido);
        }

        if (resultado.EsExito)
        {
            await avisos.ContrasenaCambiada(cuenta.Usuario, cancelacion).ConfigureAwait(false);
        }

        return resultado;
    }

    /// <summary>
    /// Lo que sigue después de una contraseña correcta (IDE-20 a IDE-23, IDE-50): cambiar la temporal; configurar o presentar el segundo factor si le toca; o la credencial. Un desafío encadenado hereda la hora
    /// de creación del anterior: los 5 minutos corren desde la contraseña.
    /// </summary>
    private async Task<ResultadoDelIngreso> SiguientePaso(CuentaDeIdentidad cuenta, DateTimeOffset? creado, CancellationToken cancelacion)
    {
        var ahora = reloj.Ahora;
        if (cuenta.DebeCambiarLaTemporal)
        {
            return await NuevoDesafio(cuenta.Usuario.Id, ModoDelDesafio.CambiarContrasenaTemporal, PasoPendienteDelIngreso.CambiarContrasenaTemporal, creado, cancelacion).ConfigureAwait(false);
        }

        var guardado = await segundoFactor.Obtener(cuenta.Usuario.Id, cancelacion).ConfigureAwait(false);
        var activo = guardado is { Factor.EstaActivo: true };
        if (activo)
        {
            return await NuevoDesafio(cuenta.Usuario.Id, ModoDelDesafio.PresentarSegundoFactor, PasoPendienteDelIngreso.PresentarSegundoFactor, creado, cancelacion).ConfigureAwait(false);
        }

        if (cuenta.Usuario.Rol.EsDePersonal())
        {
            return await NuevoDesafio(cuenta.Usuario.Id, ModoDelDesafio.ConfigurarSegundoFactor, PasoPendienteDelIngreso.ConfigurarSegundoFactor, creado, cancelacion).ConfigureAwait(false);
        }

        return new ResultadoDelIngreso { Credencial = await sesiones.Nueva(cuenta.Usuario, ahora, cancelacion).ConfigureAwait(false) };
    }

    private async Task<ResultadoDelIngreso> NuevoDesafio(Guid usuarioId, ModoDelDesafio modo, PasoPendienteDelIngreso paso, DateTimeOffset? creado, CancellationToken cancelacion)
    {
        var ahora = reloj.Ahora;
        var valor = secretos.ValorOpaco();
        var id = secretos.NuevoIdentificador();
        var desafio = creado is { } herencia
            ? DesafioDeIngreso.Restaurar(id, usuarioId, modo, herencia, usado: false)
            : DesafioDeIngreso.Crear(id, usuarioId, modo, ahora);
        await desafios.Guardar(desafio, huellasDeIdentidad.DeValorOpaco(valor), cancelacion).ConfigureAwait(false);
        return new ResultadoDelIngreso { Desafio = new DesafioDelIngreso(new ValorSecreto(valor), paso, desafio.Vence) };
    }

    /// <summary>El desafío vigente que permite esa acción, o <see langword="null"/> si no existe, venció, ya se usó o es de otro modo (IDE-42).</summary>
    private async Task<DesafioDeIngreso?> DesafioVigente(ValorSecreto valor, AccionConDesafio accion, CancellationToken cancelacion)
    {
        var desafio = await desafios.PorHuella(huellasDeIdentidad.DeValorOpaco(valor.Revelar()), cancelacion).ConfigureAwait(false);
        return desafio is not null && desafio.Evaluar(reloj.Ahora) == EstadoDelDesafio.Vigente && desafio.Permite(accion) ? desafio : null;
    }
}
