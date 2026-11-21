using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using PropositoDeCodigo = BabelHub.Ecommerce.Domain.Identidad.PropositoDeCodigo;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>Validación de lo que escribe la persona antes de tocar nada: el correo y el nombre (IDE-01). La contraseña la valida <see cref="PoliticaDeContrasenas"/>.</summary>
public static class EntradasDeIdentidad
{
    public const int LargoMaximoDelNombre = 100;

    /// <summary>Normaliza el correo (minúsculas, sin espacios en los extremos) y comprueba que sea una sola dirección razonable; no es una validación de entrega (la verifica el código).</summary>
    public static bool TryNormalizarElCorreo(string? correo, out string normalizado)
    {
        normalizado = string.Empty;
        if (string.IsNullOrWhiteSpace(correo))
        {
            return false;
        }

        var texto = Usuario.NormalizarCorreo(correo);
        var arroba = texto.IndexOf('@', StringComparison.Ordinal);
        if (texto.Length > 254 || arroba < 1 || arroba != texto.LastIndexOf('@') || arroba == texto.Length - 1 || texto.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is ',' or ';' or '<' or '>' or '"' or '(' or ')'))
        {
            return false;
        }

        // La misma regla que el correo de un código (SEG-28): una cuenta cuyo correo no admite un código sería imposible de verificar y generaría alertas en cada intento.
        try
        {
            _ = CorreoDeCodigo.ParaElTitular(texto, Plataforma.PropositoDeCodigo.VerificacionDeCuenta, new ValorSecreto("000000"));
        }
        catch (ArgumentException)
        {
            return false;
        }

        normalizado = texto;
        return true;
    }

    /// <summary>La clave de validación del campo de una contraseña nueva que no cumple la política (IDE-59).</summary>
    public static string ClaveDeValidacion(ResultadoDeContrasena resultado) => resultado switch
    {
        ResultadoDeContrasena.Corta => "demasiado-corta",
        ResultadoDeContrasena.Larga => "demasiado-larga",
        ResultadoDeContrasena.Comun => "demasiado-comun",
        _ => "obligatoria",
    };

    public static bool NombreValido(string? nombre) => !string.IsNullOrWhiteSpace(nombre) && nombre.Trim().Length <= LargoMaximoDelNombre && !nombre.Any(char.IsControl);

    /// <summary>
    /// El nombre que un usuario corrige en su cuenta (IDE-90): no vacío, hasta 100 caracteres ya recortados y sin caracteres de control ni de formato (por ejemplo, los que invierten el sentido del texto). Es un validador
    /// propio de la corrección: el del registro y el alta (<see cref="NombreValido"/>) no cambia.
    /// </summary>
    public static bool NombreCorregidoValido(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length > LargoMaximoDelNombre)
        {
            return false;
        }

        // Por runas, no por unidades UTF-16: los caracteres de etiqueta (U+E0000 a U+E007F, formato) son pares sustitutos y un sustituto suelto no es texto válido.
        for (var i = 0; i < nombre.Length;)
        {
            if (!System.Text.Rune.TryGetRuneAt(nombre, i, out var runa)
                || System.Text.Rune.IsControl(runa)
                || System.Text.Rune.GetUnicodeCategory(runa) == System.Globalization.UnicodeCategory.Format)
            {
                return false;
            }

            i += runa.Utf16SequenceLength;
        }

        return true;
    }
}

/// <summary>
/// El trabajo en segundo plano de las solicitudes de código (IDE-65, D05): busca la cuenta, decide si le toca el código y lo emite y lo envía. Es el <b>único</b> lugar que lo sabe, de modo que el camino síncrono
/// de la solicitud es idéntico exista o no la cuenta. El código de verificación sale solo a un cliente activo con el correo sin verificar; el de recuperación, solo a un cliente activo con el correo verificado
/// (IDE-17, IDE-29); nunca al personal ni a una cuenta cancelada.
/// </summary>
public sealed class ProcesarSolicitudDeCodigo(IRepositorioDeCuentas cuentas, EmisionDeCodigos emision, EventosDeSeguridadDeIdentidad eventos)
{
    public async Task Ejecutar(SolicitudDeCodigoEnSegundoPlano solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var cuenta = await cuentas.PorCorreo(solicitud.CorreoNormalizado, cancelacion).ConfigureAwait(false);
        if (solicitud.Proposito == PropositoDeCodigo.RecuperacionDeAcceso)
        {
            // Toda solicitud de recuperación deja su evento (IDE-19, AUD-10); el identificador de la cuenta solo se guarda si existe.
            await eventos.RecuperacionSolicitada(cuenta?.Usuario.Id, cancelacion).ConfigureAwait(false);
        }

        if (cuenta is null || !LeToca(cuenta, solicitud.Proposito))
        {
            return;
        }

        // Sin origen en segundo plano: el cupo del origen (IDE-83) ya lo reservó el camino síncrono de la solicitud.
        await emision.Emitir(cuenta.Usuario, solicitud.Proposito, cancelacion, cupoYaReservado: true).ConfigureAwait(false);
    }

    private static bool LeToca(CuentaDeIdentidad cuenta, PropositoDeCodigo proposito) =>
        cuenta.CanceladaEn is null
        && cuenta.Usuario is { Activo: true, Rol: RolDeUsuario.Cliente }
        && proposito switch
        {
            PropositoDeCodigo.VerificacionDeCorreo => !cuenta.Usuario.CorreoVerificado,
            PropositoDeCodigo.RecuperacionDeAcceso => cuenta.Usuario.CorreoVerificado,
            _ => false,
        };
}

/// <summary>
/// Registro de una cuenta de cliente, verificación del correo y reenvío del código (T14; IDE-01, IDE-13, IDE-16, IDE-29, IDE-34, IDE-39, IDE-56, IDE-84, IDE-88): exige aceptar los textos <b>vigentes</b> y declarar la
/// mayoría de edad; el correo ya usado se rechaza sin revelar datos (<see cref="FallosDeIdentidad.CorreoEnUso"/>), salvo el reemplazo de una cuenta de cliente <b>sin verificar</b> y sin pendientes, que se hace en una
/// transacción y que, si cualquier puerto falla, se rechaza <b>igual que IDE-13</b> y sin efectos parciales (IDE-88). La contraseña se resume antes de mirar si el correo existe, para que el tiempo no lo delate; el
/// código sale en segundo plano en los dos caminos.
/// </summary>
public sealed class RegistroDeCuentas(
    IRepositorioDeCuentas cuentas,
    IHasherDeContrasenas hasher,
    IListaDeContrasenasComunes comunes,
    IVersionesDeTextosVigentes versiones,
    IControlDeLimites limites,
    ICalculadorDeHuella huellas,
    IUnidadDeTrabajo unidad,
    IPendientesDeCuenta pendientes,
    ICarritoDeCuentaReemplazada carrito,
    ReceptoresDeCambiosDeCuentaCompuestos receptores,
    IRepositorioDeRenovaciones renovaciones,
    ISegundoFactorAlmacen segundoFactor,
    IAuditoria auditoria,
    ICodigosDeUnSoloUso codigos,
    IHuellasDeIdentidad huellasDeIdentidad,
    IRegistroDeSolicitudesDeCodigo solicitudesDeCodigo,
    IColaDeCodigos cola,
    EmisionDeSesion sesiones,
    EventosDeSeguridadDeIdentidad eventos,
    IPublicadorDeAlertasTecnicas alertas,
    IGeneradorDeSecretos secretos,
    IReloj reloj) : IRegistroDeCuentas
{
    /// <summary>Se lanza dentro de la transacción del reemplazo para revertirla entera cuando un puerto falla (IDE-88).</summary>
    private sealed class ReemplazoNoRealizableException : Exception;

    public async Task<Resultado<RespuestaUniforme>> Registrar(SolicitudDeAltaDeCuenta solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        if (!EntradasDeIdentidad.NombreValido(solicitud.Nombre))
        {
            return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.Validacion("nombre", "obligatorio"));
        }

        if (!EntradasDeIdentidad.TryNormalizarElCorreo(solicitud.Correo, out var correo))
        {
            return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.Validacion("correo", "formato-invalido"));
        }

        var (contrasena, evaluacion) = Contrasena.Crear(solicitud.Contrasena?.Revelar(), comunes.EsComun);
        if (contrasena is null)
        {
            return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.Validacion("contrasena", ClaveDeLaContrasena(evaluacion)));
        }

        var ahora = reloj.Ahora;
        var vigentes = await versiones.Obtener(cancelacion).ConfigureAwait(false);
        var (aceptacion, resultadoDeTextos) = AceptacionDeTextos.EnElRegistro(vigentes, solicitud.VersionDeTerminos, solicitud.VersionDePrivacidad, solicitud.DeclaraSerMayorDeEdad, ahora);
        if (aceptacion is null)
        {
            return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.Validacion("aceptacion", resultadoDeTextos == ResultadoDeAceptacion.VersionNoVigente ? "version-no-vigente" : "incompleta"));
        }

        var limite = LimitesDeIdentidad.Rechazo(await limites.Reservar(PoliticasDeLimite.RegistrosPorOrigen, cancelacion: cancelacion).ConfigureAwait(false));
        if (limite is not null)
        {
            return Resultado<RespuestaUniforme>.Fallo(limite);
        }

        // El resumen va antes de mirar si el correo existe: el costo es el mismo en los dos caminos (IDE-13).
        var resumen = await hasher.Resumir(contrasena, cancelacion).ConfigureAwait(false);
        var nueva = new CuentaDeIdentidad
        {
            Usuario = Usuario.NuevoCliente(secretos.NuevoIdentificador(), correo),
            Nombre = solicitud.Nombre.Trim(),
            ResumenDeAcceso = resumen,
            Aceptacion = aceptacion,
            CreadaEn = ahora,
        };

        bool creada;
        try
        {
            creada = await unidad.Ejecutar(ct => CrearOReemplazar(nueva, ct), cancelacion).ConfigureAwait(false);
        }
        catch (ReemplazoNoRealizableException)
        {
            return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.CorreoEnUso);
        }

        if (!creada)
        {
            return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.CorreoEnUso);
        }

        // El cupo de códigos de 10 al día por origen (IDE-83) se reserva aquí, donde existe el origen: el trabajo en segundo plano no lo tiene y lo da por reservado. Superado el tope, o sin el almacén de contadores (falla cerrada), el código
        // no se encola y la respuesta es la misma: la cuenta queda creada y la persona puede pedir otro código más tarde (el origen que agota su cupo no agota el de los demás).
        if (await ReservarElCupoDeCodigosDelOrigen(cancelacion).ConfigureAwait(false))
        {
            EncolarElCodigo(correo, PropositoDeCodigo.VerificacionDeCorreo);
        }

        return Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme());
    }

    private async Task<bool> ReservarElCupoDeCodigosDelOrigen(CancellationToken cancelacion)
    {
        try
        {
            return (await limites.Reservar(PoliticasDeLimite.CupoDeCodigosPorOrigen, cancelacion: cancelacion).ConfigureAwait(false)).EsPermitido;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<Resultado<ResultadoDelIngreso>> VerificarCorreo(string correo, ValorSecreto codigo, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(codigo);
        if (!EntradasDeIdentidad.TryNormalizarElCorreo(correo, out var normalizado) || !CodigoDeUnSoloUso.TieneLaForma(codigo.Revelar()))
        {
            return Resultado<ResultadoDelIngreso>.Fallo(FallosDeIdentidad.CodigoInvalido);
        }

        var cuenta = await cuentas.PorCorreo(normalizado, cancelacion).ConfigureAwait(false);
        if (cuenta is null || !PuedeVerificar(cuenta))
        {
            await eventos.CodigoRechazado(cuenta?.Usuario.Id, ResultadoDelCodigo.NoVigente, cancelacion).ConfigureAwait(false);
            return Resultado<ResultadoDelIngreso>.Fallo(FallosDeIdentidad.CodigoInvalido);
        }

        var usuarioId = cuenta.Usuario.Id;
        var huella = huellasDeIdentidad.DeCodigo(usuarioId, PropositoDeCodigo.VerificacionDeCorreo, codigo.Revelar());
        ResultadoDelCodigo? rechazado = null;

        // El intento y el consumo del código, marcar el correo y emitir la sesión son UNA transacción (como en la recuperación): si algo falla después de consumir el código, se revierte entera.
        var resultado = await unidad.Ejecutar(
            async ct =>
            {
                var verificacion = await codigos.Verificar(usuarioId, PropositoDeCodigo.VerificacionDeCorreo, huella, reloj.Ahora, ct).ConfigureAwait(false);
                if (verificacion != ResultadoDelCodigo.Correcto)
                {
                    rechazado = verificacion;
                    return null;
                }

                var actual = await cuentas.PorId(usuarioId, paraActualizar: true, ct).ConfigureAwait(false);
                if (actual is null || !PuedeVerificar(actual))
                {
                    rechazado = ResultadoDelCodigo.NoVigente;
                    return null;
                }

                actual.Usuario.VerificarCorreo();
                await cuentas.Guardar(actual, ct).ConfigureAwait(false);

                // Con el segundo factor activo el correo queda verificado pero NO se emite sesión: solo ingresando con contraseña y segundo factor (IDE-50).
                if (await segundoFactor.Obtener(usuarioId, ct).ConfigureAwait(false) is { Factor.EstaActivo: true })
                {
                    return new ResultadoDelIngreso();
                }

                return new ResultadoDelIngreso { Credencial = await sesiones.Nueva(actual.Usuario, reloj.Ahora, ct).ConfigureAwait(false) };
            },
            cancelacion).ConfigureAwait(false);

        if (resultado is null)
        {
            await eventos.CodigoRechazado(usuarioId, rechazado!.Value, cancelacion).ConfigureAwait(false);
            return Resultado<ResultadoDelIngreso>.Fallo(FallosDeIdentidad.CodigoInvalido);
        }

        return Resultado<ResultadoDelIngreso>.Exito(resultado);
    }

    private static bool PuedeVerificar(CuentaDeIdentidad cuenta) => cuenta is { CanceladaEn: null, Usuario: { CorreoVerificado: false, Activo: true } };

    public async Task<Resultado<RespuestaUniforme>> ReenviarCodigo(string correo, CancellationToken cancelacion = default)
    {
        if (!EntradasDeIdentidad.TryNormalizarElCorreo(correo, out var normalizado))
        {
            return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.Validacion("correo", "formato-invalido"));
        }

        var rechazo = await SolicitudDeCodigos.Reservar(limites, huellas, normalizado, "codigos-verificacion", cancelacion).ConfigureAwait(false);
        if (rechazo is not null)
        {
            return Resultado<RespuestaUniforme>.Fallo(rechazo);
        }

        // La fila idéntica del camino síncrono, exista o no la cuenta (IDE-65): la búsqueda y el envío van en segundo plano.
        // La columna guarda 64 hexadecimales: el resumen de la huella con clave del correo.
        await solicitudesDeCodigo.Registrar(huellasDeIdentidad.DeValorOpaco(huellas.DeSujeto(normalizado, "codigos-verificacion").Valor), PropositoDeCodigo.VerificacionDeCorreo, reloj.Ahora, cancelacion).ConfigureAwait(false);
        EncolarElCodigo(normalizado, PropositoDeCodigo.VerificacionDeCorreo);
        return Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme());
    }

    private async Task<bool> CrearOReemplazar(CuentaDeIdentidad nueva, CancellationToken cancelacion)
    {
        var existente = await cuentas.PorCorreo(nueva.Usuario.Correo, cancelacion).ConfigureAwait(false);
        if (existente is null)
        {
            return await cuentas.Insertar(nueva, cancelacion).ConfigureAwait(false);
        }

        // IDE-84: solo una cuenta de cliente activa, sin verificar y sin cancelar; nunca una del personal.
        if (existente is not { CanceladaEn: null, Usuario: { Rol: RolDeUsuario.Cliente, CorreoVerificado: false, Activo: true } })
        {
            return false;
        }

        try
        {
            // Se relee bajo el bloqueo de la fila y se repiten las condiciones: si el titular verificó su correo entre la lectura y ahora, no se elimina una cuenta ya verificada (IDE-84).
            if (await cuentas.PorId(existente.Usuario.Id, paraActualizar: true, cancelacion).ConfigureAwait(false) is not { CanceladaEn: null, Usuario: { Rol: RolDeUsuario.Cliente, CorreoVerificado: false, Activo: true } })
            {
                return false;
            }

            if (await pendientes.Contar(existente.Usuario.Id, cancelacion).ConfigureAwait(false) != 0)
            {
                return false;
            }

            await renovaciones.RevocarTodas(existente.Usuario.Id, reloj.Ahora, cancelacion).ConfigureAwait(false);
            await carrito.Eliminar(existente.Usuario.Id, cancelacion).ConfigureAwait(false);

            // Los módulos receptores desvinculan lo suyo antes de que la cuenta se elimine y su correo se reasigne (IDE-99); si uno falla, o no hay ninguno, el reemplazo no se hace y el rechazo es el de IDE-13 (IDE-88).
            await receptores.AlReemplazarLaCuenta(existente.Usuario.Id, cancelacion).ConfigureAwait(false);
            await auditoria.Registrar(
                new SolicitudDeRegistro(AccionesAuditables.CuentaReemplazada) { ObjetoTipo = "cuenta", ObjetoId = existente.Usuario.Id.ToString("D") },
                cancelacion).ConfigureAwait(false);
            await cuentas.Eliminar(existente.Usuario.Id, cancelacion).ConfigureAwait(false);
            return await cuentas.Insertar(nueva, cancelacion).ConfigureAwait(false) ? true : throw new ReemplazoNoRealizableException();
        }
        catch (Exception ex) when (ex is not ReemplazoNoRealizableException && !cancelacion.IsCancellationRequested)
        {
            // Un puerto que no responde, el carrito que no se puede eliminar o la auditoría caída: no se reemplaza nada y el rechazo es el mismo de IDE-13 (IDE-88); la causa no se pierde: queda una alerta técnica.
            await AlertarCanalLleno().ConfigureAwait(false);
            throw new ReemplazoNoRealizableException();
        }
    }

    private void EncolarElCodigo(string correoNormalizado, PropositoDeCodigo proposito)
    {
        if (cola.Encolar(new SolicitudDeCodigoEnSegundoPlano(correoNormalizado, proposito)))
        {
            return;
        }

        // El canal está lleno: el trabajo se descarta en silencio, la respuesta es la misma y queda una alerta técnica (IDE-65).
        _ = AlertarCanalLleno();
    }

    private async Task AlertarCanalLleno()
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(EmisionDeCodigos.MotivoDeFallaDelCorreo, reloj.Ahora)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Sin a dónde mandar la falla.
        }
    }

    private static string ClaveDeLaContrasena(ResultadoDeContrasena resultado) => resultado switch
    {
        ResultadoDeContrasena.Vacia => "obligatoria",
        ResultadoDeContrasena.Corta => "demasiado-corta",
        ResultadoDeContrasena.Larga => "demasiado-larga",
        ResultadoDeContrasena.Comun => "demasiado-comun",
        _ => "invalida",
    };
}

/// <summary>El tope de solicitudes de código por correo y por origen (SEG-19): rechaza igual exista o no la cuenta.</summary>
public static class SolicitudDeCodigos
{
    public static async Task<ErrorDeAplicacion?> Reservar(IControlDeLimites limites, ICalculadorDeHuella huellas, string correoNormalizado, string proposito, CancellationToken cancelacion)
    {
        var porCorreo = await limites.Reservar(PoliticasDeLimite.CodigosPorCorreo, huellaDelLlamador: huellas.DeSujeto(correoNormalizado, proposito), cancelacion: cancelacion).ConfigureAwait(false);
        if (LimitesDeIdentidad.Rechazo(porCorreo) is { } rechazoPorCorreo)
        {
            return rechazoPorCorreo;
        }

        var porOrigen = await limites.Reservar(PoliticasDeLimite.CodigosPorOrigen, cancelacion: cancelacion).ConfigureAwait(false);
        if (LimitesDeIdentidad.Rechazo(porOrigen) is { } rechazoPorOrigen)
        {
            return rechazoPorOrigen;
        }

        // El cupo de códigos de 10 al día por origen (IDE-83): aquí existe el origen; en el trabajo en segundo plano no.
        var cupoDelOrigen = await limites.Reservar(PoliticasDeLimite.CupoDeCodigosPorOrigen, cancelacion: cancelacion).ConfigureAwait(false);
        return LimitesDeIdentidad.Rechazo(cupoDelOrigen);
    }
}
