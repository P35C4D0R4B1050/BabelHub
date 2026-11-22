using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using DominioEstado = BabelHub.Ecommerce.Domain.Identidad.EstadoDeCancelacion;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>
/// La solicitud de cancelación de la cuenta de un cliente (T22; IDE-27, IDE-28, IDE-68, IDE-71, IDE-76, IDE-81; AC-IDE-24, AC-IDE-25, AC-IDE-61). Exige la contraseña actual y el segundo factor si lo tiene. <b>Sin pendientes</b>
/// pide ejecutar en esa misma operación; <b>con pendientes</b> queda diferida (no compra, ve cuántos faltan y recibe un aviso). La reconfirmación vuelve a contar los pendientes <b>dentro de la operación que ejecuta</b> (IDE-71)
/// y, si no se puede consultar el puerto, no se ejecuta nada (IDE-70). La ejecución misma es del <see cref="IEjecutorDeLaCancelacion"/> (T23) y se hace en la misma transacción. La lectura de la marca con bloqueo toma la fila
/// del usuario: la cancelación y el paso al pago del mismo usuario se serializan (IDE-71).
/// </summary>
public sealed class CancelacionDeCuenta(
    IRepositorioDeCuentas cuentas,
    AlcanceDeLaCuenta alcance,
    ConfianzaDelDispositivo confianza,
    Reautenticacion reautenticacion,
    IRepositorioDeCancelaciones cancelaciones,
    IPendientesDeCuenta pendientes,
    IEjecutorDeLaCancelacion ejecutor,
    IAvisosDeCuentaAlmacen avisosDeCuenta,
    AvisosDeCuenta avisos,
    IGeneradorDeSecretos secretos,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IReloj reloj) : ICancelacionDeCuenta, IMarcaDeCancelacionDeCuenta
{
    public const string AvisoDeCancelacionDiferida = "cancelacion-diferida";

    public const string AvisoDePendientesCerrados = "cancelacion-pendientes-cerrados";

    public const string AvisoDeCancelacionDescartada = "cancelacion-descartada";

    private static ErrorDeAplicacion CancelacionEnCurso { get; } = new(CategoriaDeError.Conflicto, "cancelacion-en-curso");

    private static ErrorDeAplicacion SinCancelacionEnCurso { get; } = new(CategoriaDeError.Conflicto, "sin-cancelacion-en-curso");

    private static ErrorDeAplicacion SinReconfirmacionPendiente { get; } = new(CategoriaDeError.Conflicto, "sin-reconfirmacion-pendiente");

    private static ErrorDeAplicacion CancelacionDescartada { get; } = new(CategoriaDeError.Conflicto, "cancelacion-descartada");

    private static ErrorDeAplicacion EjecucionEnCurso { get; } = new(CategoriaDeError.Conflicto, "ejecucion-en-curso");

    public async Task<Resultado<EstadoDeLaCancelacionDeCuenta>> Solicitar(Guid usuarioId, ValorSecreto contrasena, ValorSecreto? codigoDelSegundoFactor, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(contrasena);
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(prohibido);
        }

        // Con un cambio de correo no reconocido pendiente, pedir o reconfirmar la cancelación exige un dispositivo reconocido (IDE-96); desistir sigue permitido.
        if (await confianza.ComprobarQueNoEsteCongelado(usuarioId, marcaDeDispositivo, cancelacion).ConfigureAwait(false) is { } congelado)
        {
            return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(congelado);
        }

        var cuenta = await Cargar(usuarioId, false, cancelacion).ConfigureAwait(false);
        if (cuenta is null)
        {
            return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
        }

        // La cuenta del personal la da de baja la administración (desactivarla), no ellos mismos.
        if (cuenta.Usuario.Rol != RolDeUsuario.Cliente)
        {
            return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "solo-clientes"));
        }

        var verificacion = await reautenticacion.ContrasenaYCodigoSiLoTiene(cuenta, contrasena, codigoDelSegundoFactor, cancelacion).ConfigureAwait(false);
        if (verificacion.Error is not null)
        {
            return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(verificacion.Error);
        }

        var perdioLaCarrera = false;
        Usuario? avisado = null;

        // Aceptar el código, consultar los pendientes y ejecutar o diferir son UNA transacción: si los pendientes no se pueden consultar o la ejecución falla, no queda nada a medias (IDE-70).
        var resultado = await unidad.Ejecutar(
            async ct =>
            {
                var bloqueada = await Cargar(usuarioId, true, ct).ConfigureAwait(false);
                if (bloqueada is null)
                {
                    return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
                }

                if (await cancelaciones.AbiertaDe(usuarioId, paraActualizar: true, ct).ConfigureAwait(false) is not null)
                {
                    return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(CancelacionEnCurso);
                }

                if (verificacion.Codigo is not null && !await reautenticacion.Aceptar(usuarioId, verificacion.Codigo, ct).ConfigureAwait(false))
                {
                    perdioLaCarrera = true;
                    return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(FallosDeIdentidad.CodigoInvalido);
                }

                var (abiertos, desglose) = await ContarYDesglosar(usuarioId, ct).ConfigureAwait(false);
                if (abiertos == 0)
                {
                    await ejecutor.Ejecutar(bloqueada, ct).ConfigureAwait(false);
                    return Resultado<EstadoDeLaCancelacionDeCuenta>.Exito(new EstadoDeLaCancelacionDeCuenta(EstadoDeLaCancelacion.Ejecutada, 0, null, null, false));
                }

                var ahora = reloj.Ahora;
                var solicitud = SolicitudDeCancelacion.Diferir(secretos.NuevoIdentificador(), usuarioId, ahora);
                await cancelaciones.Guardar(solicitud, ct).ConfigureAwait(false);
                await avisosDeCuenta.Agregar(usuarioId, AvisoDeCancelacionDiferida, ahora, ct).ConfigureAwait(false);
                avisado = bloqueada.Usuario;
                return Resultado<EstadoDeLaCancelacionDeCuenta>.Exito(Estado(solicitud, abiertos, desglose));
            },
            cancelacion).ConfigureAwait(false);

        if (perdioLaCarrera)
        {
            await reautenticacion.CodigoIncorrecto(usuarioId, cancelacion).ConfigureAwait(false);
        }

        if (resultado.EsExito)
        {
            await reautenticacion.DevolverLosCupos(verificacion.Reservas, cancelacion).ConfigureAwait(false);
        }

        // El aviso de «queda diferida y puede desistir» (IDE-27) sale con la solicitud ya guardada y nunca interrumpe; si no sale, el aviso en la cuenta lo sustituye (IDE-76).
        if (avisado is not null)
        {
            await avisos.CancelacionDiferida(avisado, cancelacion).ConfigureAwait(false);
        }

        return resultado;
    }

    public async Task<Resultado<EstadoDeLaCancelacionDeCuenta>> Consultar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(prohibido);
        }

        if (await Cargar(usuarioId, false, cancelacion).ConfigureAwait(false) is null)
        {
            return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
        }

        var solicitud = await cancelaciones.UltimaDe(usuarioId, cancelacion).ConfigureAwait(false);
        if (solicitud is null)
        {
            return Resultado<EstadoDeLaCancelacionDeCuenta>.Exito(new EstadoDeLaCancelacionDeCuenta(EstadoDeLaCancelacion.SinSolicitud, 0, null, null, false));
        }

        // Cuántos faltan solo se pregunta mientras la solicitud está diferida; si el puerto no responde, la consulta falla (no se inventa un número).
        var (abiertos, desglose) = solicitud.Estado == DominioEstado.Diferida ? await ContarYDesglosar(usuarioId, cancelacion).ConfigureAwait(false) : (0, (DesgloseDePendientes?)null);
        return Resultado<EstadoDeLaCancelacionDeCuenta>.Exito(Estado(solicitud, abiertos, desglose));
    }

    public async Task<Resultado<EstadoDeLaCancelacionDeCuenta>> Reconfirmar(Guid usuarioId, ValorSecreto contrasena, ValorSecreto? codigoDelSegundoFactor, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(contrasena);
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(prohibido);
        }

        // Con un cambio de correo no reconocido pendiente, pedir o reconfirmar la cancelación exige un dispositivo reconocido (IDE-96); desistir sigue permitido.
        if (await confianza.ComprobarQueNoEsteCongelado(usuarioId, marcaDeDispositivo, cancelacion).ConfigureAwait(false) is { } congelado)
        {
            return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(congelado);
        }

        var cuenta = await Cargar(usuarioId, false, cancelacion).ConfigureAwait(false);
        if (cuenta is null)
        {
            return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
        }

        if (await cancelaciones.AbiertaDe(usuarioId, paraActualizar: false, cancelacion).ConfigureAwait(false) is null)
        {
            return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(SinCancelacionEnCurso);
        }

        var verificacion = await reautenticacion.ContrasenaYCodigoSiLoTiene(cuenta, contrasena, codigoDelSegundoFactor, cancelacion).ConfigureAwait(false);
        if (verificacion.Error is not null)
        {
            return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(verificacion.Error);
        }

        var perdioLaCarrera = false;
        Usuario? descartada = null;
        Resultado<EstadoDeLaCancelacionDeCuenta> resultado;
        try
        {
            // Aceptar el código, volver a consultar los pendientes y ejecutar son UNA transacción (IDE-71): si algo falla —incluido el puerto de pendientes— no se ejecuta ni queda nada a medias (IDE-70).
            resultado = await unidad.Ejecutar(
                async ct =>
                {
                    var bloqueada = await Cargar(usuarioId, true, ct).ConfigureAwait(false);
                    var solicitud = bloqueada is null ? null : await cancelaciones.AbiertaDe(usuarioId, paraActualizar: true, ct).ConfigureAwait(false);
                    if (bloqueada is null || solicitud is null)
                    {
                        return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(SinCancelacionEnCurso);
                    }

                    if (verificacion.Codigo is not null && !await reautenticacion.Aceptar(usuarioId, verificacion.Codigo, ct).ConfigureAwait(false))
                    {
                        perdioLaCarrera = true;
                        return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(FallosDeIdentidad.CodigoInvalido);
                    }

                    var (abiertos, desglose) = await ContarYDesglosar(usuarioId, ct).ConfigureAwait(false);
                    var ahora = reloj.Ahora;
                    switch (solicitud.Reconfirmar(ahora, hayPendientes: abiertos > 0))
                    {
                        case ResultadoDeReconfirmacion.Ejecutar:
                            await ejecutor.Ejecutar(bloqueada, ct).ConfigureAwait(false);
                            solicitud.Ejecutada(ahora);
                            await cancelaciones.Guardar(solicitud, ct).ConfigureAwait(false);
                            return Resultado<EstadoDeLaCancelacionDeCuenta>.Exito(Estado(solicitud, 0));

                        case ResultadoDeReconfirmacion.MantenerDiferida:
                            // Apareció un pendiente: no se ejecuta, se le dice cuántos son y sigue diferida (IDE-71).
                            await cancelaciones.Guardar(solicitud, ct).ConfigureAwait(false);
                            return Resultado<EstadoDeLaCancelacionDeCuenta>.Exito(Estado(solicitud, abiertos, desglose));

                        case ResultadoDeReconfirmacion.FueraDePlazo:
                            await DescartarAsync(solicitud, ct).ConfigureAwait(false);
                            descartada = bloqueada.Usuario;
                            return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(CancelacionDescartada);

                        default:
                            return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(SinReconfirmacionPendiente);
                    }
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Falló cerrada: no quedó nada. Si el reintento cruza el día 30, la solicitud se descarta (IDE-81); si esto también falla, el error original es el que sube.
            await RegistrarElFalloDeLaEjecucion(usuarioId, cancelacion).ConfigureAwait(false);
            throw;
        }

        if (perdioLaCarrera)
        {
            await reautenticacion.CodigoIncorrecto(usuarioId, cancelacion).ConfigureAwait(false);
        }

        if (resultado.EsExito)
        {
            await reautenticacion.DevolverLosCupos(verificacion.Reservas, cancelacion).ConfigureAwait(false);
        }

        if (descartada is not null)
        {
            await avisos.CancelacionDescartada(descartada, cancelacion).ConfigureAwait(false);
        }

        return resultado;
    }

    public async Task<Resultado<EstadoDeLaCancelacionDeCuenta>> Desistir(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(prohibido);
        }

        return await unidad.Ejecutar(
            async ct =>
            {
                if (await Cargar(usuarioId, true, ct).ConfigureAwait(false) is null)
                {
                    return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
                }

                var solicitud = await cancelaciones.AbiertaDe(usuarioId, paraActualizar: true, ct).ConfigureAwait(false);
                if (solicitud is null)
                {
                    return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(SinCancelacionEnCurso);
                }

                // Una ejecución empezada se termina o falla cerrada: no se descarta a mitad (IDE-70).
                if (!solicitud.Desistir(reloj.Ahora))
                {
                    return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(EjecucionEnCurso);
                }

                await cancelaciones.Guardar(solicitud, ct).ConfigureAwait(false);
                await Auditar(usuarioId, ct).ConfigureAwait(false);
                return Resultado<EstadoDeLaCancelacionDeCuenta>.Exito(Estado(solicitud, 0));
            },
            cancelacion).ConfigureAwait(false);
    }

    public async Task<Resultado<EstadoDeLaCancelacionDeCuenta>> MostrarElAviso(Guid usuarioId, bool desdeUnaSesionReconocida, CancellationToken cancelacion = default)
    {
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(prohibido);
        }

        // Solo una sesión reconocida del titular hace que el aviso cuente como mostrado (IDE-76).
        if (!desdeUnaSesionReconocida)
        {
            return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "sesion-no-reconocida"));
        }

        return await unidad.Ejecutar(
            async ct =>
            {
                if (await Cargar(usuarioId, true, ct).ConfigureAwait(false) is null)
                {
                    return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
                }

                var solicitud = await cancelaciones.AbiertaDe(usuarioId, paraActualizar: true, ct).ConfigureAwait(false);
                if (solicitud is null)
                {
                    return Resultado<EstadoDeLaCancelacionDeCuenta>.Fallo(SinCancelacionEnCurso);
                }

                solicitud.AvisoMostrado(reloj.Ahora);
                await cancelaciones.Guardar(solicitud, ct).ConfigureAwait(false);
                return Resultado<EstadoDeLaCancelacionDeCuenta>.Exito(Estado(solicitud, 0));
            },
            cancelacion).ConfigureAwait(false);
    }

    public async Task<EstadoDeLaMarca> LeerConBloqueo(Guid usuarioId, CancellationToken cancelacion = default)
    {
        // Lo primero que se bloquea es la fila del usuario (D10): la cancelación y el paso al pago del mismo usuario se serializan.
        var cuenta = await cuentas.PorId(usuarioId, paraActualizar: true, cancelacion).ConfigureAwait(false);
        if (cuenta is not { CanceladaEn: null, Usuario.Activo: true })
        {
            return EstadoDeLaMarca.Ejecutada;
        }

        var abierta = await cancelaciones.AbiertaDe(usuarioId, paraActualizar: true, cancelacion).ConfigureAwait(false);
        return abierta switch
        {
            null => EstadoDeLaMarca.Libre,
            { EjecucionEnCurso: true } => EstadoDeLaMarca.EnEjecucion,
            _ => EstadoDeLaMarca.Diferida,
        };
    }

    /// <summary>Descarta la solicitud (ya hecho por el Dominio): la guarda, audita el descarte y deja el aviso en la cuenta; el correo lo envía quien llama con el resultado ya confirmado.</summary>
    private async Task DescartarAsync(SolicitudDeCancelacion solicitud, CancellationToken cancelacion)
    {
        await cancelaciones.Guardar(solicitud, cancelacion).ConfigureAwait(false);
        await Auditar(solicitud.UsuarioId, cancelacion).ConfigureAwait(false);
        await avisosDeCuenta.Agregar(solicitud.UsuarioId, AvisoDeCancelacionDescartada, reloj.Ahora, cancelacion).ConfigureAwait(false);
    }

    private async Task RegistrarElFalloDeLaEjecucion(Guid usuarioId, CancellationToken cancelacion)
    {
        try
        {
            await unidad.Ejecutar(
                async ct =>
                {
                    var solicitud = await cancelaciones.AbiertaDe(usuarioId, paraActualizar: true, ct).ConfigureAwait(false);
                    if (solicitud is null)
                    {
                        return false;
                    }

                    solicitud.EjecucionFallo(reloj.Ahora);
                    if (solicitud.Estado == DominioEstado.Descartada)
                    {
                        await DescartarAsync(solicitud, ct).ConfigureAwait(false);
                    }

                    return true;
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Lo que sube es el error de la ejecución; la revisión periódica descarta lo vencido.
        }
    }

    /// <summary>Cuenta los pendientes y, si el puerto sabe separarlos en grupos (D5), también el desglose; una sola consulta, y el total manda aunque no haya desglose.</summary>
    private async Task<(int Total, DesgloseDePendientes? Desglose)> ContarYDesglosar(Guid usuarioId, CancellationToken cancelacion)
    {
        if (pendientes is IDesgloseDePendientesDeCuenta separable)
        {
            var desglose = await separable.Desglosar(usuarioId, cancelacion).ConfigureAwait(false);
            return (desglose.Total, desglose);
        }

        return (await pendientes.Contar(usuarioId, cancelacion).ConfigureAwait(false), null);
    }

    private Task Auditar(Guid usuarioId, CancellationToken cancelacion) =>
        auditoria.Registrar(new SolicitudDeRegistro(AccionesAuditables.CancelacionDiferidaDescartada) { ObjetoTipo = "cuenta", ObjetoId = usuarioId.ToString("D") }, cancelacion);

    private async Task<CuentaDeIdentidad?> Cargar(Guid usuarioId, bool paraActualizar, CancellationToken cancelacion)
    {
        var cuenta = await cuentas.PorId(usuarioId, paraActualizar, cancelacion).ConfigureAwait(false);
        return cuenta is { CanceladaEn: null, Usuario.Activo: true } ? cuenta : null;
    }

    internal static EstadoDeLaCancelacionDeCuenta Estado(SolicitudDeCancelacion solicitud, int pendientesAbiertos, DesgloseDePendientes? desglose = null) => new(
        solicitud.Estado switch
        {
            DominioEstado.Diferida => EstadoDeLaCancelacion.Diferida,
            DominioEstado.EsperandoReconfirmacion => EstadoDeLaCancelacion.EsperandoReconfirmacion,
            DominioEstado.Ejecutada => EstadoDeLaCancelacion.Ejecutada,
            _ => EstadoDeLaCancelacion.Descartada,
        },
        pendientesAbiertos,
        solicitud.ReconfirmarHasta,
        solicitud.ImpideComprar ? solicitud.FechaTope : null,
        solicitud.AvisoVistoEn is not null,
        desglose);
}

/// <summary>
/// La revisión periódica de las cancelaciones abiertas (T22; IDE-68, IDE-76, IDE-81; AC-IDE-61): cuando se cierran los pendientes de una cancelación diferida avisa al titular y abre el plazo de 7 días (sin pasar del día 30);
/// si el correo no salió, el plazo cuenta desde que el aviso se muestre en una sesión reconocida; y descarta lo vencido (el plazo sin reconfirmar o el día 30) con el aviso en la cuenta y por correo. Corre con el actor del sistema y
/// una solicitud que falla no detiene las demás: se reintenta en la siguiente pasada.
/// </summary>
public sealed class RevisionDeLasCancelaciones(
    IRepositorioDeCuentas cuentas,
    IRepositorioDeCancelaciones cancelaciones,
    IPendientesDeCuenta pendientes,
    IAvisosDeCuentaAlmacen avisosDeCuenta,
    AvisosDeCuenta avisos,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IReloj reloj)
{
    public const int LimitePorPasada = 100;

    public async Task<ResultadoDeLaRevision> Revisar(CancellationToken cancelacion = default)
    {
        var abiertas = await cancelaciones.AbiertasParaRevisar(LimitePorPasada, cancelacion).ConfigureAwait(false);
        var pendientesCerrados = 0;
        var descartadas = 0;
        var fallidas = 0;
        foreach (var abierta in abiertas)
        {
            try
            {
                var (cerro, descarto) = await Procesar(abierta.UsuarioId, cancelacion).ConfigureAwait(false);
                pendientesCerrados += cerro ? 1 : 0;
                descartadas += descarto ? 1 : 0;
            }
            catch (Exception) when (!cancelacion.IsCancellationRequested)
            {
                // Una que falla (el puerto de pendientes, la auditoría) se reintenta en la siguiente pasada; no detiene las demás.
                fallidas++;
            }
        }

        return new ResultadoDeLaRevision(abiertas.Count, pendientesCerrados, descartadas, fallidas);
    }

    private async Task<(bool PendientesCerrados, bool Descartada)> Procesar(Guid usuarioId, CancellationToken cancelacion)
    {
        var cuenta = await cuentas.PorId(usuarioId, paraActualizar: false, cancelacion).ConfigureAwait(false);
        if (cuenta is not { CanceladaEn: null, Usuario.Activo: true })
        {
            return (false, false);
        }

        var cerro = false;
        var descartadaAlCerrar = false;
        var solicitud = await cancelaciones.AbiertaDe(usuarioId, paraActualizar: false, cancelacion).ConfigureAwait(false);
        if (solicitud is { Estado: DominioEstado.Diferida } && await pendientes.Contar(usuarioId, cancelacion).ConfigureAwait(false) == 0)
        {
            // El aviso sale antes de abrir el plazo: si no pudo salir, el plazo no empieza hasta que se muestre en la cuenta (IDE-76).
            var salio = await avisos.PendientesCerrados(cuenta.Usuario, cancelacion).ConfigureAwait(false);
            cerro = await unidad.Ejecutar(
                async ct =>
                {
                    var bloqueada = await cancelaciones.AbiertaDe(usuarioId, paraActualizar: true, ct).ConfigureAwait(false);
                    if (bloqueada is not { Estado: DominioEstado.Diferida } || await pendientes.Contar(usuarioId, ct).ConfigureAwait(false) != 0)
                    {
                        return false;
                    }

                    bloqueada.PendientesCerrados(reloj.Ahora, salio);
                    await cancelaciones.Guardar(bloqueada, ct).ConfigureAwait(false);
                    if (bloqueada.Estado == DominioEstado.Descartada)
                    {
                        await DescartarAsync(bloqueada, ct).ConfigureAwait(false);
                        descartadaAlCerrar = true;
                        return false;
                    }

                    await avisosDeCuenta.Agregar(usuarioId, CancelacionDeCuenta.AvisoDePendientesCerrados, reloj.Ahora, ct).ConfigureAwait(false);
                    return true;
                },
                cancelacion).ConfigureAwait(false);
        }

        var descarto = false;
        await unidad.Ejecutar(
            async ct =>
            {
                var bloqueada = await cancelaciones.AbiertaDe(usuarioId, paraActualizar: true, ct).ConfigureAwait(false);
                if (bloqueada is not null && bloqueada.DescartarSiVencio(reloj.Ahora))
                {
                    await DescartarAsync(bloqueada, ct).ConfigureAwait(false);
                    descarto = true;
                }

                return true;
            },
            cancelacion).ConfigureAwait(false);

        // El correo del descarte sale con el descarte ya confirmado y nunca interrumpe (IDE-81).
        if (descarto || descartadaAlCerrar)
        {
            await avisos.CancelacionDescartada(cuenta.Usuario, cancelacion).ConfigureAwait(false);
            descarto = true;
        }

        return (cerro, descarto);
    }

    private async Task DescartarAsync(SolicitudDeCancelacion solicitud, CancellationToken cancelacion)
    {
        await cancelaciones.Guardar(solicitud, cancelacion).ConfigureAwait(false);
        await auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.CancelacionDiferidaDescartada) { ObjetoTipo = "cuenta", ObjetoId = solicitud.UsuarioId.ToString("D") },
            cancelacion).ConfigureAwait(false);
        await avisosDeCuenta.Agregar(solicitud.UsuarioId, CancelacionDeCuenta.AvisoDeCancelacionDescartada, reloj.Ahora, cancelacion).ConfigureAwait(false);
    }
}

/// <summary>Lo que hizo una pasada de la revisión: cuántas revisó, a cuántas se les cerraron los pendientes, cuántas se descartaron y cuántas fallaron (se reintentan).</summary>
public sealed record ResultadoDeLaRevision(int Revisadas, int PendientesCerrados, int Descartadas, int Fallidas);
