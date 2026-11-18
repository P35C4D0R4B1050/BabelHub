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

/// <summary>
/// El aviso de seguridad por correo de un cambio de contraseña (IDE-45): <b>sin la contraseña</b> y sin enlaces. <b>Nunca interrumpe</b> el cambio (AGENTS.md §3.10): si el correo falla o la cuota no lo deja salir, el
/// cambio ya está hecho. La clasificación fina, los topes por cuenta y el evento de supresión son de T28 y T29.
/// </summary>
public sealed class AvisosDeCuenta(IEnviadorDeCorreo correo)
{
    public const string PlantillaDeContrasenaCambiada = "contrasena-cambiada";

    public async Task ContrasenaCambiada(Usuario usuario, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        if (!usuario.CorreoVerificado)
        {
            // Un aviso de seguridad no sale a un correo sin verificar (IDE-86): solo se muestra en la cuenta.
            return;
        }

        try
        {
            await correo.Enviar(new CorreoSaliente(CategoriaDeCorreo.AlertaCriticaDeCuenta, usuario.Correo, PlantillaDeContrasenaCambiada), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // El cambio ya se hizo; el correo no es necesario para él.
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
        var coincide = presentada is not null && await hasher.Verificar(cuenta?.ResumenDeContrasena, presentada, cancelacion).ConfigureAwait(false);
        var ahora = reloj.Ahora;
        var temporalVencida = cuenta is { DebeCambiarLaContrasena: true, ContrasenaTemporalHasta: { } hasta } && ahora >= hasta;
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
        var rechazo = await ReservarLosLimitesDelSegundoFactor(usuarioId, cancelacion).ConfigureAwait(false);
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
        var rechazo = await ReservarLosLimitesDelSegundoFactor(usuarioId, cancelacion).ConfigureAwait(false);
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

        return await unidad.Ejecutar(
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
    }

    public async Task<Resultado<ResultadoDelIngreso>> CambiarLaContrasenaTemporal(ValorSecreto desafio, ValorSecreto contrasenaNueva, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(desafio);
        ArgumentNullException.ThrowIfNull(contrasenaNueva);
        var vigente = await DesafioVigente(desafio, AccionConDesafio.CambiarContrasenaTemporal, cancelacion).ConfigureAwait(false);
        var cuenta = vigente is null ? null : await cuentas.PorId(vigente.UsuarioId, cancelacion: cancelacion).ConfigureAwait(false);
        var ahora = reloj.Ahora;
        if (cuenta is not { CanceladaEn: null, Usuario.Activo: true, DebeCambiarLaContrasena: true } || (cuenta.ContrasenaTemporalHasta is { } hasta && ahora >= hasta))
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
        var resultado = await unidad.Ejecutar(
            async ct =>
            {
                if (!vigente!.Consumir(ahora, AccionConDesafio.CambiarContrasenaTemporal) || !await desafios.MarcarUsado(vigente.Id, ct).ConfigureAwait(false))
                {
                    return Resultado<ResultadoDelIngreso>.Fallo(FallosDeIdentidad.DesafioInvalido);
                }

                cuenta.ResumenDeContrasena = resumen;
                cuenta.ContrasenaTemporalHasta = null;
                cuenta.DebeCambiarLaContrasena = false;
                cuenta.Usuario.AvanzarElCorteDeSesiones(ahora);
                await cuentas.Guardar(cuenta, ct).ConfigureAwait(false);
                await renovaciones.RevocarTodas(cuenta.Usuario.Id, ahora, ct).ConfigureAwait(false);
                await auditoria.Registrar(
                    new SolicitudDeRegistro(AccionesAuditables.ContrasenaCambiada) { ObjetoTipo = "cuenta", ObjetoId = cuenta.Usuario.Id.ToString("D"), Motivo = "cambio-por-el-titular" },
                    ct).ConfigureAwait(false);
                return Resultado<ResultadoDelIngreso>.Exito(await SiguientePaso(cuenta, vigente.Creado, ct).ConfigureAwait(false));
            },
            cancelacion).ConfigureAwait(false);

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
        if (cuenta.DebeCambiarLaContrasena)
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

    private async Task<ErrorDeAplicacion?> ReservarLosLimitesDelSegundoFactor(Guid usuarioId, CancellationToken cancelacion)
    {
        var sujeto = usuarioId.ToString("N");
        var porOrigen = await limites.Reservar(PoliticasDeLimite.SegundoFactorPorOrigen, sujeto, cancelacion: cancelacion).ConfigureAwait(false);
        if (LimitesDeIdentidad.Rechazo(porOrigen) is { } rechazoPorOrigen)
        {
            return rechazoPorOrigen;
        }

        var porCuenta = await limites.Reservar(PoliticasDeLimite.SegundoFactorPorCuenta, sujeto, cancelacion: cancelacion).ConfigureAwait(false);
        return LimitesDeIdentidad.Rechazo(porCuenta);
    }
}
