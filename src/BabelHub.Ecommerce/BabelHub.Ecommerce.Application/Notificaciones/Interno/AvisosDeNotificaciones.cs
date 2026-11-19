using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Notificaciones;

namespace BabelHub.Ecommerce.Application.Notificaciones.Interno;

/// <summary>
/// El almacén de avisos (<c>notificaciones.avisos</c>, T12): la clave única <c>(evento_id, destinatario_ref, plantilla)</c> impide dos avisos del mismo hecho (NOT-05). <see cref="InsertarSiNoExiste"/> inserta en un
/// <b>punto de guardado</b> con <c>ON CONFLICT DO NOTHING</c>: un fallo del <c>INSERT</c> no invalida la transacción del módulo que pidió el aviso.
/// </summary>
public interface IRepositorioDeAvisos
{
    /// <summary>Inserta el aviso; <see langword="false"/> si ya existía uno para ese evento, destinatario y plantilla.</summary>
    Task<bool> InsertarSiNoExiste(Aviso aviso, CancellationToken cancelacion = default);

    /// <summary>Los avisos no finales (pendientes y en envío) de un destinatario, bajo bloqueo: se cancelan en la misma operación que cambia el consentimiento, el número o la cuenta.</summary>
    Task<IReadOnlyList<Aviso>> NoFinalesDe(TipoDeDestinatario tipo, string destinatarioRef, CancellationToken cancelacion = default);

    Task Guardar(Aviso aviso, CancellationToken cancelacion = default);
}

/// <summary>La huella de un número con la versión de la clave que la calculó (NOT-13, NOT-14): el número no se conserva, solo su huella.</summary>
public sealed record HuellaDeNumero(int VersionDeLaClave, string Valor)
{
    /// <summary>No imprime la huella entera.</summary>
    public override string ToString() => $"[huella v{VersionDeLaClave}]";

    /// <summary>Los primeros caracteres, para la auditoría y la consulta: suficientes para distinguir, no para reconstruir.</summary>
    public string Parcial => Valor.Length <= 8 ? Valor : Valor[..8];
}

/// <summary>El cálculo de la huella de un número (<c>CalculadorDeHuellaDeNumero</c>, T13): HMAC con una clave y un propósito propios, versionada, sobre el número E.164.</summary>
public interface IHuellasDeNumeros
{
    /// <summary>La huella con la clave actual.</summary>
    HuellaDeNumero Actual(NumeroDeDestino numero);

    /// <summary>Las huellas con <b>todas las versiones vigentes</b> de la clave: un número desactivado con una versión anterior se reconoce igual.</summary>
    IReadOnlyList<HuellaDeNumero> Vigentes(NumeroDeDestino numero);
}

/// <summary>Un número cuyos avisos se desactivaron a pedido de su titular (<c>notificaciones.numeros_desactivados</c>, NOT-13): su huella, la fecha y el motivo.</summary>
public sealed record NumeroDesactivado(HuellaDeNumero Huella, DateTimeOffset DesactivadoEn, string Motivo);

public interface IRepositorioDeNumerosDesactivados
{
    /// <summary>Agrega la huella; <see langword="false"/> si ya estaba (con esa versión de la clave).</summary>
    Task<bool> Agregar(NumeroDesactivado numero, CancellationToken cancelacion = default);

    /// <summary>¿Está desactivada alguna de estas huellas (todas las versiones vigentes de la clave)?</summary>
    Task<bool> ExisteAlguna(IReadOnlyList<HuellaDeNumero> huellas, CancellationToken cancelacion = default);

    /// <summary>Quita las huellas (reactivación a pedido del titular); devuelve cuántas.</summary>
    Task<int> Eliminar(IReadOnlyList<HuellaDeNumero> huellas, CancellationToken cancelacion = default);
}

/// <summary>
/// La solicitud de un aviso (T04, T05; NOT-01 a NOT-05, NOT-07, NOT-09, AC-NOT-01, AC-NOT-02, AC-NOT-04, AC-NOT-08): <b>solo guarda</b>, en la transacción del llamador, y <b>nunca lanza ni toca la red</b>. Valida en memoria
/// antes de la base: la plantilla del catálogo cerrado (con su tipo de destinatario), el evento, la referencia opaca y los campos cerrados y tipados (sin enlaces, tarjetas ni texto libre). Un cliente que no consiente (o cuya
/// cuenta no existe o está cancelada) queda registrado como <b>omitido por falta de consentimiento</b> (NOT-03); una sucursal no necesita consentimiento. Dos solicitudes del mismo evento, destinatario y plantilla dejan un solo
/// aviso. Un fallo propio (la base, Identidad) <b>no se traga ni interrumpe</b>: se publica una alerta técnica y el resultado es «no se guardó»; el aviso no se crea sin conocer el consentimiento (falla cerrada).
/// </summary>
public sealed class SolicitarAviso(IRepositorioDeAvisos avisos, IEstadoDeCuenta estadoDeCuenta, IAlertas alertas, IReloj reloj) : INotificador
{
    /// <summary>El motivo del catálogo cerrado de alertas técnicas: una dependencia no necesaria que falló (aquí, el registro de un aviso).</summary>
    public const string MotivoDeFallaPropia = "dependencia-no-necesaria-fallida";

    public const string MotivoDeOmisionSinConsentimiento = "sin-consentimiento";

    public async Task<ResultadoDeLaSolicitudDeAviso> Solicitar(SolicitudDeAviso solicitud, CancellationToken cancelacion = default)
    {
        if (solicitud is null || Validar(solicitud) is not { } valida)
        {
            // Una solicitud inválida es un error del módulo que llama: se deja constancia técnica para que no se pierda en silencio.
            await AlertarSinInterrumpir(cancelacion).ConfigureAwait(false);
            return ResultadoDeLaSolicitudDeAviso.Rechazado;
        }

        try
        {
            var ahora = reloj.Ahora;
            var tipo = valida.Plantilla.Destinatario;
            var referencia = valida.Referencia;
            if (tipo == TipoDeDestinatario.Cliente && !await ConsienteElCliente(valida.CuentaId!.Value, cancelacion).ConfigureAwait(false))
            {
                var omitido = Aviso.OmitidoSinConsentimiento(Guid.NewGuid(), solicitud.EventoId, referencia, tipo, solicitud.Plantilla, valida.Plantilla.Version, MotivoDeOmisionSinConsentimiento, ahora);
                return await avisos.InsertarSiNoExiste(omitido, cancelacion).ConfigureAwait(false)
                    ? ResultadoDeLaSolicitudDeAviso.OmitidoSinConsentimiento
                    : ResultadoDeLaSolicitudDeAviso.YaExistia;
            }

            var aviso = Aviso.Crear(Guid.NewGuid(), solicitud.EventoId, referencia, tipo, solicitud.Plantilla, valida.Plantilla.Version, solicitud.Campos, ahora);
            return await avisos.InsertarSiNoExiste(aviso, cancelacion).ConfigureAwait(false) ? ResultadoDeLaSolicitudDeAviso.Creado : ResultadoDeLaSolicitudDeAviso.YaExistia;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            await AlertarSinInterrumpir(cancelacion).ConfigureAwait(false);
            return ResultadoDeLaSolicitudDeAviso.NoSeGuardo;
        }
    }

    private sealed record Valida(PlantillaDeAviso Plantilla, Guid? CuentaId, string Referencia);

    private static Valida? Validar(SolicitudDeAviso solicitud)
    {
        var plantilla = CatalogoDePlantillas.Obtener(solicitud.Plantilla);
        if (plantilla is null
            || plantilla.Destinatario != (solicitud.Destinatario == DestinatarioDelAviso.Cliente ? TipoDeDestinatario.Cliente : TipoDeDestinatario.Sucursal)
            || !PlantillaDeAviso.EventoValido(solicitud.EventoId)
            || !PlantillaDeAviso.ReferenciaValida(solicitud.DestinatarioRef)
            || solicitud.Campos is null
            || !plantilla.Validar(solicitud.Campos).EsValido)
        {
            return null;
        }

        if (plantilla.Destinatario == TipoDeDestinatario.Cliente)
        {
            // La referencia de un cliente es el id de su cuenta, guardada en su forma canónica («D», minúsculas): así la unicidad y la cancelación no dependen de cómo se escribió.
            return Guid.TryParse(solicitud.DestinatarioRef, out var cuenta) && cuenta != Guid.Empty ? new Valida(plantilla, cuenta, cuenta.ToString("D")) : null;
        }

        return new Valida(plantilla, null, solicitud.DestinatarioRef);
    }

    private async Task<bool> ConsienteElCliente(Guid cuentaId, CancellationToken cancelacion)
    {
        var estado = await estadoDeCuenta.Consultar(cuentaId, cancelacion).ConfigureAwait(false);
        return estado is { TieneWhatsAppConConsentimiento: true, CuentaCanceladaODesactivada: false };
    }

    private async Task AlertarSinInterrumpir(CancellationToken cancelacion)
    {
        try
        {
            await alertas.PublicarTecnica(new CodigoDeAlerta(MotivoDeFallaPropia), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Sin a dónde mandar la alerta: quien pidió el aviso sigue con su operación (NOT-04).
        }
    }
}

/// <summary>
/// La cancelación de los avisos pendientes de un destinatario (T06; NOT-08, AC-NOT-07): Identidad la llama <b>en la misma operación</b> en que el cliente revoca su consentimiento, cambia su número, cancela su cuenta o la
/// administración la desactiva; los avisos pendientes o en envío pasan a <b>cancelados</b> con su motivo y un aviso que ya era final no cambia. Si llega durante un envío, la comprobación del estado vigente de cada intento
/// (T08) lo detiene. Si no se puede cancelar, lanza: Identidad no cambia nada a medias.
/// </summary>
public sealed class CancelarAvisosPendientes(IRepositorioDeAvisos avisos, IReloj reloj) : ICancelacionDeAvisos
{
    public const string MotivoDeCancelacion = "destinatario-cambiado";

    public async Task CancelarLosAvisosDe(Guid usuarioId, CancellationToken cancelacion = default)
    {
        var ahora = reloj.Ahora;
        var pendientes = await avisos.NoFinalesDe(TipoDeDestinatario.Cliente, usuarioId.ToString("D"), cancelacion).ConfigureAwait(false);
        foreach (var aviso in pendientes)
        {
            if (aviso.Cancelar(MotivoDeCancelacion, ahora))
            {
                // El cupo del tope por número, si se había reservado, se devuelve al cancelar (T09); aquí solo se deja constancia en el aviso.
                await avisos.Guardar(aviso, cancelacion).ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// La desactivación de los avisos de un número a pedido de su titular (T07; NOT-13, AC-NOT-13): <b>solo la administración central</b> (otro actor, <c>Prohibido</c> con la denegación registrada). El número se normaliza a E.164
/// (escrito de cualquier forma es el mismo), <b>no se guarda</b>: solo su huella con la clave versionada; la desactivación se <b>audita en la misma transacción</b> con una huella parcial (sin el número, AUD-06) y si no se puede
/// auditar no se aplica. La exclusión vale para <b>todas las versiones vigentes de la clave</b> (<see cref="EstaDesactivado"/>) y la reactivación es solo a pedido del titular, otra operación administrativa auditada.
/// </summary>
public sealed class DesactivarAvisosDeUnNumero(
    IActorActual actor,
    IDenegacionDeAcceso denegaciones,
    IHuellasDeNumeros huellas,
    IRepositorioDeNumerosDesactivados desactivados,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IReloj reloj) : IDesactivacionDeNumeros
{
    public const string RolDeAdministracionCentral = "administrador-central";

    public const string CausaDeLaDesactivacion = "desactivado-a-pedido-del-titular";

    public const string CausaDeLaReactivacion = "reactivado-a-pedido-del-titular";

    public const int LargoMaximoDelMotivo = 100;

    public async Task<Resultado<ConfirmacionDeNotificaciones>> Desactivar(string numero, string motivo, CancellationToken cancelacion = default)
    {
        if (await ComprobarAdministracion(cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<ConfirmacionDeNotificaciones>.Fallo(prohibido);
        }

        if (!NumeroDeDestino.TryNormalizar(numero, out var normalizado))
        {
            return Resultado<ConfirmacionDeNotificaciones>.Fallo(Validacion("numero", "invalido"));
        }

        if (!MotivoValido(motivo))
        {
            return Resultado<ConfirmacionDeNotificaciones>.Fallo(Validacion("motivo", "invalido"));
        }

        var actual = huellas.Actual(normalizado);
        return await unidad.Ejecutar(
            async ct =>
            {
                // Idempotente: si la huella ya estaba, se responde igual y no se vuelve a auditar.
                if (await desactivados.Agregar(new NumeroDesactivado(actual, reloj.Ahora, motivo.Trim()), ct).ConfigureAwait(false))
                {
                    await Auditar(actual, CausaDeLaDesactivacion, ct).ConfigureAwait(false);
                }

                return Resultado<ConfirmacionDeNotificaciones>.Exito(new ConfirmacionDeNotificaciones());
            },
            cancelacion).ConfigureAwait(false);
    }

    public async Task<Resultado<ConfirmacionDeNotificaciones>> Reactivar(string numero, CancellationToken cancelacion = default)
    {
        if (await ComprobarAdministracion(cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<ConfirmacionDeNotificaciones>.Fallo(prohibido);
        }

        if (!NumeroDeDestino.TryNormalizar(numero, out var normalizado))
        {
            return Resultado<ConfirmacionDeNotificaciones>.Fallo(Validacion("numero", "invalido"));
        }

        var vigentes = huellas.Vigentes(normalizado);
        return await unidad.Ejecutar(
            async ct =>
            {
                if (await desactivados.Eliminar(vigentes, ct).ConfigureAwait(false) > 0)
                {
                    await Auditar(huellas.Actual(normalizado), CausaDeLaReactivacion, ct).ConfigureAwait(false);
                }

                return Resultado<ConfirmacionDeNotificaciones>.Exito(new ConfirmacionDeNotificaciones());
            },
            cancelacion).ConfigureAwait(false);
    }

    /// <summary>¿Está desactivado este número, escrito de cualquier forma y con cualquier versión vigente de la clave? Lo usa el envío antes de salir (T08): un número desactivado no recibe avisos.</summary>
    public async Task<bool> EstaDesactivado(NumeroDeDestino numero, CancellationToken cancelacion = default) =>
        await desactivados.ExisteAlguna(huellas.Vigentes(numero), cancelacion).ConfigureAwait(false);

    private async Task<ErrorDeAplicacion?> ComprobarAdministracion(CancellationToken cancelacion)
    {
        if (actor.Actual is { Tipo: TipoDeActor.Usuario, UsuarioId: not null, Rol: RolDeAdministracionCentral })
        {
            return null;
        }

        try
        {
            await denegaciones.Registrar(new DenegacionDeAcceso("fuera-de-rol", ruta: null), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // IDenegacionDeAcceso nunca interrumpe y deja su alerta técnica si falla.
        }

        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, "acceso-denegado");
    }

    private Task Auditar(HuellaDeNumero huella, string causa, CancellationToken cancelacion) =>
        auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.AvisosDeNumeroDesactivados)
            {
                ObjetoTipo = "numero",
                ObjetoId = huella.Parcial,
                Detalle = new Dictionary<string, string?> { ["causa"] = causa },
            },
            cancelacion);

    private static ErrorDeAplicacion Validacion(string campo, string clave) => new(CategoriaDeError.Validacion, "validacion-invalida", [new ErrorDeCampo(campo, clave)]);

    /// <summary>
    /// El motivo es un texto corto sin datos de contacto: ni un teléfono (siete o más cifras en total, de cualquier alfabeto y con los separadores que sean), ni una dirección de correo, ni un enlace, ni caracteres de control.
    /// </summary>
    private static bool MotivoValido(string? motivo) =>
        !string.IsNullOrWhiteSpace(motivo) && motivo.Trim().Length <= LargoMaximoDelMotivo && !motivo.Any(char.IsControl)
        && !motivo.Contains("://", StringComparison.Ordinal) && !motivo.Contains('@') && !motivo.Contains('＠')
        && motivo.Count(char.IsDigit) < 7;
}
