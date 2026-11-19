using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using DominioEstado = BabelHub.Ecommerce.Domain.Identidad.EstadoDeSolicitudDeDatos;
using DominioTipo = BabelHub.Ecommerce.Domain.Identidad.TipoDeSolicitudDeDatos;
using TipoDelContrato = BabelHub.Ecommerce.Application.Identidad.TipoDeSolicitudDeDatos;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>
/// Exige que quien llama sea el administrador central: el actor debe ser él y la base debe confirmarlo como administrador central activo y no cancelado (no basta la afirmación de la credencial). Cualquier otro (un
/// encargado, logística, un cliente, un visitante) recibe <c>Prohibido</c> con la denegación registrada (AGENTS.md §5).
/// </summary>
public sealed class AlcanceDeLaAdministracion(AlcanceDeLaCuenta alcance, IRepositorioDeCuentas cuentas)
{
    public async Task<(ErrorDeAplicacion? Error, CuentaDeIdentidad? Cuenta)> Exigir(Guid administradorId, CancellationToken cancelacion)
    {
        if (await alcance.Comprobar(administradorId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return (prohibido, null);
        }

        var cuenta = await cuentas.PorId(administradorId, paraActualizar: false, cancelacion).ConfigureAwait(false);
        return cuenta is { CanceladaEn: null, Usuario: { Rol: RolDeUsuario.AdministradorCentral, Activo: true } }
            ? (null, cuenta)
            : (await alcance.Denegar(cancelacion, "fuera-de-rol").ConfigureAwait(false), null);
    }
}

/// <summary>
/// Las solicitudes sobre datos personales (T21; IDE-12, IDE-26, IDE-29, IDE-32, IDE-63; AC-IDE-11, AC-IDE-23, AC-IDE-26, AC-IDE-29, AC-IDE-52). El cliente con el correo verificado las presenta (queda <c>recibida</c> con
/// su fecha límite en días hábiles de Lima: 20 el acceso, 10 los demás); la administración las ve ordenadas por fecha límite con las vencidas marcadas, sin datos personales; <b>abrir el detalle se audita en la misma
/// operación y, si no se puede auditar, no se muestra</b> (AUD-04); responder (en atención, atendida o rechazada con motivo) se audita, deja la respuesta en la cuenta del titular y le avisa por correo
/// <b>sin el contenido</b>. Una solicitud cerrada no cambia.
/// </summary>
public sealed class AtencionDeSolicitudesDeDatos(
    IRepositorioDeCuentas cuentas,
    AlcanceDeLaCuenta alcance,
    AlcanceDeLaAdministracion administracion,
    IRepositorioDeSolicitudesDeDatos solicitudes,
    IAvisosDeCuentaAlmacen avisosDeCuenta,
    AvisosDeCuenta avisos,
    IGeneradorDeSecretos secretos,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IReloj reloj) : ISolicitudesDeDatos
{
    /// <summary>Cuántas solicitudes abiertas puede tener una persona a la vez: acota el abuso sin impedir el uso normal.</summary>
    public const int MaximoDeAbiertasPorPersona = 10;

    public const int LargoMaximoDeLaRespuesta = 2000;

    public const int LargoMaximoDelMotivo = 500;

    public const string AvisoDeSolicitudRespondida = "solicitud-de-datos-respondida";

    public async Task<Resultado<SolicitudSobreDatos>> Presentar(Guid usuarioId, TipoDelContrato tipo, CancellationToken cancelacion = default)
    {
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<SolicitudSobreDatos>.Fallo(prohibido);
        }

        if (!Enum.IsDefined(tipo))
        {
            return Resultado<SolicitudSobreDatos>.Fallo(FallosDeIdentidad.Validacion("tipo", "invalido"));
        }

        return await unidad.Ejecutar(
            async ct =>
            {
                var cuenta = await cuentas.PorId(usuarioId, paraActualizar: true, ct).ConfigureAwait(false);
                if (cuenta is not { CanceladaEn: null, Usuario.Activo: true })
                {
                    return Resultado<SolicitudSobreDatos>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
                }

                // Con el correo sin verificar no se presentan solicitudes (IDE-29); lo demás de la cuenta sigue funcionando.
                if (!cuenta.Usuario.CorreoVerificado)
                {
                    return Resultado<SolicitudSobreDatos>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, "correo-sin-verificar"));
                }

                var propias = await solicitudes.DelUsuario(usuarioId, ct).ConfigureAwait(false);
                if (propias.Count(s => !s.EstaCerrada) >= MaximoDeAbiertasPorPersona)
                {
                    return Resultado<SolicitudSobreDatos>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, "demasiadas-solicitudes-abiertas"));
                }

                var solicitud = SolicitudDeDatos.Recibir(secretos.NuevoIdentificador(), usuarioId, (DominioTipo)tipo, reloj.Ahora, reloj.HoyEnLima);
                await solicitudes.Guardar(solicitud, ct).ConfigureAwait(false);
                return Resultado<SolicitudSobreDatos>.Exito(Modelo(solicitud, reloj.HoyEnLima));
            },
            cancelacion).ConfigureAwait(false);
    }

    public async Task<Resultado<IReadOnlyList<SolicitudDelCliente>>> ListarLasDelCliente(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<IReadOnlyList<SolicitudDelCliente>>.Fallo(prohibido);
        }

        if (await cuentas.PorId(usuarioId, paraActualizar: false, cancelacion).ConfigureAwait(false) is not { CanceladaEn: null, Usuario.Activo: true })
        {
            return Resultado<IReadOnlyList<SolicitudDelCliente>>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
        }

        var hoy = reloj.HoyEnLima;
        var propias = await solicitudes.DelUsuario(usuarioId, cancelacion).ConfigureAwait(false);
        return Resultado<IReadOnlyList<SolicitudDelCliente>>.Exito(
            [.. SolicitudDeDatos.Ordenar(propias, hoy).Select(s => new SolicitudDelCliente(Modelo(s.Solicitud, hoy, s.Vencida), s.Solicitud.Respuesta, s.Solicitud.Motivo))]);
    }

    public async Task<Resultado<IReadOnlyList<SolicitudSobreDatos>>> Listar(Guid administradorId, CancellationToken cancelacion = default)
    {
        var (error, _) = await administracion.Exigir(administradorId, cancelacion).ConfigureAwait(false);
        if (error is not null)
        {
            return Resultado<IReadOnlyList<SolicitudSobreDatos>>.Fallo(error);
        }

        var hoy = reloj.HoyEnLima;
        var todas = await solicitudes.Todas(cancelacion).ConfigureAwait(false);
        return Resultado<IReadOnlyList<SolicitudSobreDatos>>.Exito([.. SolicitudDeDatos.Ordenar(todas, hoy).Select(s => Modelo(s.Solicitud, hoy, s.Vencida))]);
    }

    public async Task<Resultado<DetalleDeSolicitud>> Consultar(Guid administradorId, Guid solicitudId, CancellationToken cancelacion = default)
    {
        var (error, _) = await administracion.Exigir(administradorId, cancelacion).ConfigureAwait(false);
        if (error is not null)
        {
            return Resultado<DetalleDeSolicitud>.Fallo(error);
        }

        // La consulta se audita ANTES de entregar el detalle y en la misma transacción: si la auditoría falla, la excepción sube y no se muestra nada (IDE-63, AUD-04).
        return await unidad.Ejecutar(
            async ct =>
            {
                var solicitud = await solicitudes.Obtener(solicitudId, paraActualizar: false, ct).ConfigureAwait(false);
                if (solicitud is null)
                {
                    return Resultado<DetalleDeSolicitud>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "solicitud-no-encontrada"));
                }

                await auditoria.Registrar(
                    new SolicitudDeRegistro(AccionesAuditables.SolicitudDeDatosConsultada) { ObjetoTipo = "solicitud-de-datos", ObjetoId = solicitud.Id.ToString("D") },
                    ct).ConfigureAwait(false);
                return Resultado<DetalleDeSolicitud>.Exito(new DetalleDeSolicitud(Modelo(solicitud, reloj.HoyEnLima), solicitud.UsuarioId, solicitud.Respuesta, solicitud.Motivo));
            },
            cancelacion).ConfigureAwait(false);
    }

    public async Task<Resultado<SolicitudSobreDatos>> Responder(Guid administradorId, Guid solicitudId, ResolucionDeSolicitud resolucion, string? respuesta, string? motivo, CancellationToken cancelacion = default)
    {
        var (error, _) = await administracion.Exigir(administradorId, cancelacion).ConfigureAwait(false);
        if (error is not null)
        {
            return Resultado<SolicitudSobreDatos>.Fallo(error);
        }

        var invalida = Validar(resolucion, respuesta, motivo);
        if (invalida is not null)
        {
            return Resultado<SolicitudSobreDatos>.Fallo(invalida);
        }

        Guid? titular = null;
        var resultado = await unidad.Ejecutar(
            async ct =>
            {
                var solicitud = await solicitudes.Obtener(solicitudId, paraActualizar: true, ct).ConfigureAwait(false);
                if (solicitud is null)
                {
                    return Resultado<SolicitudSobreDatos>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "solicitud-no-encontrada"));
                }

                var ahora = reloj.Ahora;
                var cambio = resolucion switch
                {
                    ResolucionDeSolicitud.Atendida => solicitud.Atender(ahora, respuesta!.Trim()),
                    ResolucionDeSolicitud.Rechazada => solicitud.Rechazar(ahora, motivo!.Trim()),
                    _ => PonerEnAtencion(solicitud),
                };
                if (!cambio)
                {
                    return Resultado<SolicitudSobreDatos>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, "solicitud-ya-resuelta"));
                }

                await solicitudes.Guardar(solicitud, ct).ConfigureAwait(false);
                await auditoria.Registrar(
                    new SolicitudDeRegistro(AccionesAuditables.SolicitudDeDatosAtendida)
                    {
                        ObjetoTipo = "solicitud-de-datos",
                        ObjetoId = solicitud.Id.ToString("D"),
                        Detalle = new Dictionary<string, string?> { ["tipoDeSolicitud"] = solicitud.Tipo.ToString(), ["resolucion"] = ClaveDeLaResolucion(resolucion) },
                    },
                    ct).ConfigureAwait(false);
                if (resolucion != ResolucionDeSolicitud.EnAtencion)
                {
                    await avisosDeCuenta.Agregar(solicitud.UsuarioId, AvisoDeSolicitudRespondida, ahora, ct).ConfigureAwait(false);
                    titular = solicitud.UsuarioId;
                }

                return Resultado<SolicitudSobreDatos>.Exito(Modelo(solicitud, reloj.HoyEnLima));
            },
            cancelacion).ConfigureAwait(false);

        // El aviso por correo sale con la respuesta ya guardada y nunca interrumpe; no lleva el contenido (IDE-26).
        if (resultado.EsExito && titular is { } usuarioId && await cuentas.PorId(usuarioId, paraActualizar: false, cancelacion).ConfigureAwait(false) is { CanceladaEn: null, Usuario.Activo: true } cuenta)
        {
            await avisos.SolicitudDeDatosRespondida(cuenta.Usuario, cancelacion).ConfigureAwait(false);
        }

        return resultado;
    }

    private static bool PonerEnAtencion(SolicitudDeDatos solicitud)
    {
        if (solicitud.EstaCerrada)
        {
            return false;
        }

        solicitud.PonerEnAtencion();
        return true;
    }

    private static ErrorDeAplicacion? Validar(ResolucionDeSolicitud resolucion, string? respuesta, string? motivo) => resolucion switch
    {
        ResolucionDeSolicitud.EnAtencion => null,
        ResolucionDeSolicitud.Atendida => TextoValido(respuesta, LargoMaximoDeLaRespuesta) ? null : FallosDeIdentidad.Validacion("respuesta", "invalida"),
        ResolucionDeSolicitud.Rechazada => TextoValido(motivo, LargoMaximoDelMotivo) ? null : FallosDeIdentidad.Validacion("motivo", "invalido"),
        _ => FallosDeIdentidad.Validacion("resolucion", "invalida"),
    };

    private static bool TextoValido(string? texto, int largoMaximo) =>
        !string.IsNullOrWhiteSpace(texto) && texto.Trim().Length <= largoMaximo && !texto.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));

    private static string ClaveDeLaResolucion(ResolucionDeSolicitud resolucion) => resolucion switch
    {
        ResolucionDeSolicitud.Atendida => "atendida",
        ResolucionDeSolicitud.Rechazada => "rechazada",
        _ => "en-atencion",
    };

    private static string ClaveDelEstado(DominioEstado estado) => estado switch
    {
        DominioEstado.Recibida => "recibida",
        DominioEstado.EnAtencion => "en-atencion",
        DominioEstado.Atendida => "atendida",
        _ => "rechazada",
    };

    private static SolicitudSobreDatos Modelo(SolicitudDeDatos solicitud, DateOnly hoyEnLima, bool? vencida = null) => new(
        solicitud.Id,
        (TipoDelContrato)solicitud.Tipo,
        ClaveDelEstado(solicitud.Estado),
        DateOnly.FromDateTime(solicitud.RecibidaEn.ToOffset(TimeSpan.FromHours(-5)).DateTime),
        solicitud.FechaLimite,
        vencida ?? solicitud.EstaVencida(hoyEnLima));
}
