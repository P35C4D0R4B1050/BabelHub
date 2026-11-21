using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>
/// La cuenta del cliente (T18; IDE-11, IDE-24, IDE-25, IDE-31, IDE-37, IDE-44, IDE-45, IDE-52). Cada operación comprueba <b>primero</b> que quien llama es el titular (<see cref="AlcanceDeLaCuenta"/>) y las sensibles
/// reautentican con la contraseña actual (y un código del segundo factor cuando toca) con los límites de ingresos fallidos. Los cambios de contraseña y de segundo factor se hacen bajo el bloqueo de la fila del usuario,
/// auditados en la misma transacción (AUD-04) y revocan las <b>demás</b> renovaciones conservando la sesión desde la que se hacen (IDE-44, D17). Cambiar el número o el consentimiento cancela los avisos pendientes
/// por <see cref="ICancelacionDeAvisos"/> dentro de la transacción: si el módulo de avisos falla, no se cambia nada. El número de WhatsApp solo lo ve su titular: a Carrito se le ofrece el consentimiento con la
/// huella enmascarada del número, no con el número.
/// </summary>
public sealed class GestionDeLaCuenta(
    IRepositorioDeCuentas cuentas,
    AlcanceDeLaCuenta alcance,
    ConfianzaDelDispositivo confianza,
    Reautenticacion reautenticacion,
    IControlDeLimites limites,
    IListaDeContrasenasComunes comunes,
    IHasherDeContrasenas hasher,
    IGeneradorDeSecretos secretos,
    IHuellasDeIdentidad huellasDeIdentidad,
    ICifradorDeSecretos cifrador,
    ISegundoFactorAlmacen segundoFactor,
    IRepositorioDeRenovaciones renovaciones,
    RevocacionDeSesiones revocacion,
    IAvisosDeCuentaAlmacen avisosDeCuenta,
    ICancelacionDeAvisos cancelacionDeAvisos,
    AvisosDeCuenta avisos,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IReloj reloj) : ICuentaDelCliente
{
    /// <summary>El número de WhatsApp y su consentimiento son del cliente (AGENTS.md §5): el personal no los tiene.</summary>
    private static ErrorDeAplicacion SoloClientes { get; } = new(CategoriaDeError.Prohibido, "solo-clientes");

    public async Task<Resultado<CuentaDelCliente>> Consultar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<CuentaDelCliente>.Fallo(prohibido);
        }

        var cuenta = await Cargar(usuarioId, false, cancelacion).ConfigureAwait(false);
        return cuenta is null
            ? Resultado<CuentaDelCliente>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada)
            : Resultado<CuentaDelCliente>.Exito(await Modelo(cuenta, cancelacion).ConfigureAwait(false));
    }

    public async Task<Resultado<CuentaDelCliente>> CorregirElNombre(Guid usuarioId, string nombre, CancellationToken cancelacion = default)
    {
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<CuentaDelCliente>.Fallo(prohibido);
        }

        if (!EntradasDeIdentidad.NombreCorregidoValido(nombre))
        {
            return Resultado<CuentaDelCliente>.Fallo(FallosDeIdentidad.Validacion("nombre", "invalido"));
        }

        // El tope por cuenta (IDE-98) cuenta toda corrección válida de forma, cambie o no el valor: acota el inflado de la auditoría (AUD-45).
        var reserva = await limites.Reservar(PoliticasDeLimite.CorreccionesDeNombrePorCuenta, usuarioId.ToString("N"), cancelacion: cancelacion).ConfigureAwait(false);
        if (LimitesDeIdentidad.Rechazo(reserva) is { } limite)
        {
            return Resultado<CuentaDelCliente>.Fallo(limite);
        }

        var nuevo = nombre.Trim();
        return await unidad.Ejecutar(
            async ct =>
            {
                var cuenta = await Cargar(usuarioId, true, ct).ConfigureAwait(false);
                if (cuenta is null)
                {
                    return Resultado<CuentaDelCliente>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
                }

                // Solo la corrección que cambia el valor se guarda y se audita, en la misma transacción y sin ningún nombre en el detalle (AUD-04, AUD-45).
                if (!string.Equals(cuenta.Nombre, nuevo, StringComparison.Ordinal))
                {
                    cuenta.Nombre = nuevo;
                    await cuentas.Guardar(cuenta, ct).ConfigureAwait(false);
                    await auditoria.Registrar(
                        new SolicitudDeRegistro(AccionesAuditables.UsuarioActualizado) { ObjetoTipo = "usuario", ObjetoId = usuarioId.ToString("D") },
                        ct).ConfigureAwait(false);
                }

                return Resultado<CuentaDelCliente>.Exito(await Modelo(cuenta, ct).ConfigureAwait(false));
            },
            cancelacion).ConfigureAwait(false);
    }

    public async Task<Resultado<RespuestaUniforme>> CambiarLaContrasena(Guid usuarioId, ValorSecreto actual, ValorSecreto nueva, ValorSecreto? renovacionDeLaSesion = null, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(nueva);
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<RespuestaUniforme>.Fallo(prohibido);
        }

        if (await confianza.ComprobarQueNoEsteCongelado(usuarioId, marcaDeDispositivo, cancelacion).ConfigureAwait(false) is { } congelado)
        {
            return Resultado<RespuestaUniforme>.Fallo(congelado);
        }

        var cuenta = await Cargar(usuarioId, false, cancelacion).ConfigureAwait(false);
        if (cuenta is null)
        {
            return Resultado<RespuestaUniforme>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
        }

        var resumenVerificado = cuenta.ResumenDeAcceso;
        if (await reautenticacion.ContrasenaActual(cuenta, actual, cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<RespuestaUniforme>.Fallo(rechazo);
        }

        var (contrasena, evaluacion) = Contrasena.Crear(nueva.Revelar(), comunes.EsComun);
        if (contrasena is null)
        {
            return Resultado<RespuestaUniforme>.Fallo(FallosDeIdentidad.Validacion("contrasena", EntradasDeIdentidad.ClaveDeValidacion(evaluacion)));
        }

        var resumen = await hasher.Resumir(contrasena, cancelacion).ConfigureAwait(false);
        var familia = await FamiliaDeLaSesion(usuarioId, renovacionDeLaSesion, cancelacion).ConfigureAwait(false);
        var cambioConcurrente = false;
        var hecho = await unidad.Ejecutar(
            async ct =>
            {
                var bloqueada = await Cargar(usuarioId, true, ct).ConfigureAwait(false);
                if (bloqueada is null)
                {
                    return false;
                }

                // La contraseña actual se verificó contra el resumen leído sin bloqueo: si una recuperación o un restablecimiento lo cambió mientras tanto, no se pisa con una verificación vieja.
                if (!string.Equals(bloqueada.ResumenDeAcceso, resumenVerificado, StringComparison.Ordinal))
                {
                    cambioConcurrente = true;
                    return false;
                }

                bloqueada.ResumenDeAcceso = resumen;
                bloqueada.ContrasenaTemporalHasta = null;
                bloqueada.DebeCambiarLaTemporal = false;
                await cuentas.Guardar(bloqueada, ct).ConfigureAwait(false);
                await ConservarSoloLaSesion(bloqueada, familia, ct).ConfigureAwait(false);
                await Auditar(AccionesAuditables.ContrasenaCambiada, usuarioId, "cambio-por-el-titular", ct).ConfigureAwait(false);
                return true;
            },
            cancelacion).ConfigureAwait(false);

        if (!hecho)
        {
            return Resultado<RespuestaUniforme>.Fallo(cambioConcurrente ? Reautenticacion.ContrasenaActualIncorrecta : AlcanceDeLaCuenta.CuentaNoEncontrada);
        }

        await avisos.ContrasenaCambiada(cuenta.Usuario, cancelacion).ConfigureAwait(false);
        return Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme());
    }

    public async Task<Resultado<CuentaDelCliente>> GuardarElNumeroDeWhatsApp(Guid usuarioId, string? numero, CancellationToken cancelacion = default)
    {
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<CuentaDelCliente>.Fallo(prohibido);
        }

        return await unidad.Ejecutar(
            async ct =>
            {
                var cuenta = await Cargar(usuarioId, true, ct).ConfigureAwait(false);
                if (cuenta is null)
                {
                    return Resultado<CuentaDelCliente>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
                }

                if (cuenta.Usuario.Rol != RolDeUsuario.Cliente)
                {
                    return Resultado<CuentaDelCliente>.Fallo(SoloClientes);
                }

                var antes = cuenta.Whatsapp.Numero?.Valor;
                if (numero is null)
                {
                    cuenta.Whatsapp.Eliminar();
                }
                else if (!cuenta.Whatsapp.GuardarNumero(numero, reloj.Ahora))
                {
                    return Resultado<CuentaDelCliente>.Fallo(FallosDeIdentidad.Validacion("numeroDeWhatsApp", "formato-invalido"));
                }

                await cuentas.Guardar(cuenta, ct).ConfigureAwait(false);

                // Un número distinto (o ninguno) deja sin efecto el consentimiento: los avisos pendientes del número anterior no salen (IDE-31).
                if (antes != cuenta.Whatsapp.Numero?.Valor)
                {
                    await cancelacionDeAvisos.CancelarLosAvisosDe(usuarioId, ct).ConfigureAwait(false);
                }

                return Resultado<CuentaDelCliente>.Exito(await Modelo(cuenta, ct).ConfigureAwait(false));
            },
            cancelacion).ConfigureAwait(false);
    }

    public async Task<Resultado<CuentaDelCliente>> CambiarElConsentimientoDeWhatsApp(Guid usuarioId, bool consiente, string? numeroConfirmado, CancellationToken cancelacion = default)
    {
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<CuentaDelCliente>.Fallo(prohibido);
        }

        return await unidad.Ejecutar(
            async ct =>
            {
                var cuenta = await Cargar(usuarioId, true, ct).ConfigureAwait(false);
                if (cuenta is null)
                {
                    return Resultado<CuentaDelCliente>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
                }

                if (cuenta.Usuario.Rol != RolDeUsuario.Cliente)
                {
                    return Resultado<CuentaDelCliente>.Fallo(SoloClientes);
                }

                if (consiente)
                {
                    if (!cuenta.Whatsapp.Consentir(numeroConfirmado, reloj.Ahora))
                    {
                        return Resultado<CuentaDelCliente>.Fallo(FallosDeIdentidad.Validacion("numeroConfirmado", "no-coincide"));
                    }
                }
                else
                {
                    cuenta.Whatsapp.Revocar(reloj.Ahora);
                }

                await cuentas.Guardar(cuenta, ct).ConfigureAwait(false);
                if (!consiente)
                {
                    await cancelacionDeAvisos.CancelarLosAvisosDe(usuarioId, ct).ConfigureAwait(false);
                }

                return Resultado<CuentaDelCliente>.Exito(await Modelo(cuenta, ct).ConfigureAwait(false));
            },
            cancelacion).ConfigureAwait(false);
    }

    public async Task<Resultado<OfertaDeConsentimientoDeAvisos>> ConsultarLaOfertaDeConsentimiento(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<OfertaDeConsentimientoDeAvisos>.Fallo(prohibido);
        }

        var cuenta = await Cargar(usuarioId, false, cancelacion).ConfigureAwait(false);
        if (cuenta is null)
        {
            return Resultado<OfertaDeConsentimientoDeAvisos>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
        }

        var whatsapp = cuenta.Whatsapp;
        var seOfrece = whatsapp.Numero is not null && !whatsapp.Consentido;
        return Resultado<OfertaDeConsentimientoDeAvisos>.Exito(new OfertaDeConsentimientoDeAvisos(
            seOfrece,
            whatsapp.PuedeRecibirAvisos,
            seOfrece ? Enmascarar(whatsapp.Numero!.Value.Valor) : null));
    }

    public async Task<Resultado<RespuestaUniforme>> GuardarElConsentimientoPorHuella(Guid usuarioId, string huellaEnmascaradaDelNumero, CancellationToken cancelacion = default)
    {
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<RespuestaUniforme>.Fallo(prohibido);
        }

        return await unidad.Ejecutar(
            async ct =>
            {
                var cuenta = await Cargar(usuarioId, true, ct).ConfigureAwait(false);
                if (cuenta is null)
                {
                    return Resultado<RespuestaUniforme>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
                }

                if (cuenta.Usuario.Rol != RolDeUsuario.Cliente)
                {
                    return Resultado<RespuestaUniforme>.Fallo(SoloClientes);
                }

                // Si el número cambió entre la oferta y el guardado, la huella ya no coincide y no se guarda nada (CAR-18).
                if (cuenta.Whatsapp.Numero is not { } numero || !string.Equals(Enmascarar(numero.Valor), huellaEnmascaradaDelNumero, StringComparison.Ordinal))
                {
                    return Resultado<RespuestaUniforme>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, "numero-cambio"));
                }

                cuenta.Whatsapp.Consentir(numero.Valor, reloj.Ahora);
                await cuentas.Guardar(cuenta, ct).ConfigureAwait(false);
                return Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme());
            },
            cancelacion).ConfigureAwait(false);
    }

    public async Task<Resultado<SecretoDelSegundoFactor>> IniciarElSegundoFactor(Guid usuarioId, ValorSecreto contrasenaActual, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(contrasenaActual);
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<SecretoDelSegundoFactor>.Fallo(prohibido);
        }

        if (await confianza.ComprobarQueNoEsteCongelado(usuarioId, marcaDeDispositivo, cancelacion).ConfigureAwait(false) is { } congelado)
        {
            return Resultado<SecretoDelSegundoFactor>.Fallo(congelado);
        }

        var cuenta = await Cargar(usuarioId, false, cancelacion).ConfigureAwait(false);
        if (cuenta is null)
        {
            return Resultado<SecretoDelSegundoFactor>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
        }

        if (await reautenticacion.ContrasenaActual(cuenta, contrasenaActual, cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<SecretoDelSegundoFactor>.Fallo(rechazo);
        }

        if (await segundoFactor.Obtener(usuarioId, cancelacion).ConfigureAwait(false) is { Factor.EstaActivo: true })
        {
            return Resultado<SecretoDelSegundoFactor>.Fallo(YaActivo);
        }

        var secreto = secretos.SecretoDelSegundoFactor();
        await segundoFactor.GuardarSinConfirmar(usuarioId, cifrador.Cifrar(secreto, usuarioId), cancelacion).ConfigureAwait(false);
        return Resultado<SecretoDelSegundoFactor>.Exito(new SecretoDelSegundoFactor(new ValorSecreto(Base32.Codificar(secreto)), Ingreso.Emisor, cuenta.Usuario.Correo));
    }

    public async Task<Resultado<IReadOnlyList<ValorSecreto>>> ConfirmarElSegundoFactor(Guid usuarioId, ValorSecreto codigo, ValorSecreto? renovacionDeLaSesion = null, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(codigo);
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<IReadOnlyList<ValorSecreto>>.Fallo(prohibido);
        }

        if (await confianza.ComprobarQueNoEsteCongelado(usuarioId, marcaDeDispositivo, cancelacion).ConfigureAwait(false) is { } congelado)
        {
            return Resultado<IReadOnlyList<ValorSecreto>>.Fallo(congelado);
        }

        if (await Cargar(usuarioId, false, cancelacion).ConfigureAwait(false) is null)
        {
            return Resultado<IReadOnlyList<ValorSecreto>>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
        }

        var (limite, reservas) = await LimitesDelSegundoFactor.Reservar(limites, usuarioId, cancelacion).ConfigureAwait(false);
        if (limite is not null)
        {
            return Resultado<IReadOnlyList<ValorSecreto>>.Fallo(limite);
        }

        var guardado = await segundoFactor.Obtener(usuarioId, cancelacion).ConfigureAwait(false);
        if (guardado is null)
        {
            // Sin haber probado ningún código no cuenta como intento: se devuelven los cupos (no se autobloquea quien se equivoca de paso).
            await LimitesDelSegundoFactor.Devolver(limites, reservas, cancelacion).ConfigureAwait(false);
            return Resultado<IReadOnlyList<ValorSecreto>>.Fallo(NoIniciado);
        }

        var respaldo = CodigosDeRespaldo.Generar();
        var huellasDeRespaldo = respaldo.Select(c => huellasDeIdentidad.DeCodigoDeRespaldo(usuarioId, CodigosDeRespaldo.Canonico(c))).ToList();
        var confirmacion = guardado.Factor.Confirmar(cifrador.Descifrar(guardado.SecretoCifrado, usuarioId), codigo.Revelar(), reloj.Ahora, huellasDeRespaldo);
        if (confirmacion == ResultadoDeSegundoFactor.YaActivo)
        {
            await LimitesDelSegundoFactor.Devolver(limites, reservas, cancelacion).ConfigureAwait(false);
            return Resultado<IReadOnlyList<ValorSecreto>>.Fallo(YaActivo);
        }

        if (confirmacion != ResultadoDeSegundoFactor.Aceptado)
        {
            await reautenticacion.CodigoIncorrecto(usuarioId, cancelacion).ConfigureAwait(false);
            return Resultado<IReadOnlyList<ValorSecreto>>.Fallo(FallosDeIdentidad.CodigoInvalido);
        }

        var familia = await FamiliaDeLaSesion(usuarioId, renovacionDeLaSesion, cancelacion).ConfigureAwait(false);
        var resultado = await unidad.Ejecutar(
            async ct =>
            {
                var cuenta = await Cargar(usuarioId, true, ct).ConfigureAwait(false);
                if (cuenta is null)
                {
                    return Resultado<IReadOnlyList<ValorSecreto>>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
                }

                // Bajo el bloqueo se relee: dos confirmaciones simultáneas no activan dos veces ni pisan los códigos de respaldo que ya se entregaron.
                if (await segundoFactor.Obtener(usuarioId, ct).ConfigureAwait(false) is { Factor.EstaActivo: true })
                {
                    return Resultado<IReadOnlyList<ValorSecreto>>.Fallo(YaActivo);
                }

                await segundoFactor.Activar(usuarioId, guardado.Factor.UltimoPasoUsado!.Value, huellasDeRespaldo, ct).ConfigureAwait(false);
                await ConservarSoloLaSesion(cuenta, familia, ct).ConfigureAwait(false);
                await Auditar(AccionesAuditables.SegundoFactorActivado, usuarioId, "activacion", ct).ConfigureAwait(false);
                return Resultado<IReadOnlyList<ValorSecreto>>.Exito([.. respaldo.Select(c => new ValorSecreto(c))]);
            },
            cancelacion).ConfigureAwait(false);

        if (resultado.EsExito)
        {
            await LimitesDelSegundoFactor.Devolver(limites, reservas, cancelacion).ConfigureAwait(false);
        }

        return resultado;
    }

    public async Task<Resultado<RespuestaUniforme>> DesactivarElSegundoFactor(Guid usuarioId, ValorSecreto contrasenaActual, ValorSecreto codigo, ValorSecreto? renovacionDeLaSesion = null, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(contrasenaActual);
        ArgumentNullException.ThrowIfNull(codigo);
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<RespuestaUniforme>.Fallo(prohibido);
        }

        if (await confianza.ComprobarQueNoEsteCongelado(usuarioId, marcaDeDispositivo, cancelacion).ConfigureAwait(false) is { } congelado)
        {
            return Resultado<RespuestaUniforme>.Fallo(congelado);
        }

        var cuenta = await Cargar(usuarioId, false, cancelacion).ConfigureAwait(false);
        if (cuenta is null)
        {
            return Resultado<RespuestaUniforme>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
        }

        // El personal lo tiene siempre (IDE-21): no se desactiva, solo lo restablece la administración (IDE-49).
        if (cuenta.Usuario.Rol.EsDePersonal())
        {
            return Resultado<RespuestaUniforme>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "segundo-factor-obligatorio"));
        }

        var verificacion = await VerificarContrasenaYCodigo(cuenta, contrasenaActual, codigo, permiteRespaldo: true, cancelacion).ConfigureAwait(false);
        if (verificacion.Error is not null)
        {
            return Resultado<RespuestaUniforme>.Fallo(verificacion.Error);
        }

        var familia = await FamiliaDeLaSesion(usuarioId, renovacionDeLaSesion, cancelacion).ConfigureAwait(false);
        var resultado = await EjecutarConElCodigo(
            usuarioId,
            verificacion.Codigo!,
            async (bloqueada, ct) =>
            {
                await segundoFactor.Desactivar(usuarioId, ct).ConfigureAwait(false);
                await ConservarSoloLaSesion(bloqueada, familia, ct).ConfigureAwait(false);
                await Auditar(AccionesAuditables.SegundoFactorDesactivado, usuarioId, "desactivacion", ct).ConfigureAwait(false);
                return Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme());
            },
            cancelacion).ConfigureAwait(false);

        if (resultado.EsExito)
        {
            await LimitesDelSegundoFactor.Devolver(limites, verificacion.Reservas, cancelacion).ConfigureAwait(false);
        }

        return resultado;
    }

    public async Task<Resultado<IReadOnlyList<ValorSecreto>>> RegenerarLosCodigosDeRespaldo(Guid usuarioId, ValorSecreto contrasenaActual, ValorSecreto codigo, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(contrasenaActual);
        ArgumentNullException.ThrowIfNull(codigo);
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<IReadOnlyList<ValorSecreto>>.Fallo(prohibido);
        }

        if (await confianza.ComprobarQueNoEsteCongelado(usuarioId, marcaDeDispositivo, cancelacion).ConfigureAwait(false) is { } congelado)
        {
            return Resultado<IReadOnlyList<ValorSecreto>>.Fallo(congelado);
        }

        var cuenta = await Cargar(usuarioId, false, cancelacion).ConfigureAwait(false);
        if (cuenta is null)
        {
            return Resultado<IReadOnlyList<ValorSecreto>>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
        }

        // Regenerar exige un código TOTP vigente: un código de respaldo no basta (IDE-52).
        var verificacion = await VerificarContrasenaYCodigo(cuenta, contrasenaActual, codigo, permiteRespaldo: false, cancelacion).ConfigureAwait(false);
        if (verificacion.Error is not null)
        {
            return Resultado<IReadOnlyList<ValorSecreto>>.Fallo(verificacion.Error);
        }

        var nuevos = CodigosDeRespaldo.Generar();
        var huellasNuevas = nuevos.Select(c => huellasDeIdentidad.DeCodigoDeRespaldo(usuarioId, CodigosDeRespaldo.Canonico(c))).ToList();
        var resultado = await EjecutarConElCodigo(
            usuarioId,
            verificacion.Codigo!,
            async (_, ct) =>
            {
                await segundoFactor.ReemplazarCodigosDeRespaldo(usuarioId, huellasNuevas, ct).ConfigureAwait(false);
                await Auditar(AccionesAuditables.SegundoFactorRegenerado, usuarioId, "regeneracion", ct).ConfigureAwait(false);
                return Resultado<IReadOnlyList<ValorSecreto>>.Exito([.. nuevos.Select(c => new ValorSecreto(c))]);
            },
            cancelacion).ConfigureAwait(false);

        if (resultado.EsExito)
        {
            await LimitesDelSegundoFactor.Devolver(limites, verificacion.Reservas, cancelacion).ConfigureAwait(false);
        }

        return resultado;
    }

    public async Task<Resultado<IReadOnlyList<AvisoDeCuenta>>> ListarLosAvisos(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<IReadOnlyList<AvisoDeCuenta>>.Fallo(prohibido);
        }

        if (await Cargar(usuarioId, false, cancelacion).ConfigureAwait(false) is null)
        {
            return Resultado<IReadOnlyList<AvisoDeCuenta>>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
        }

        return Resultado<IReadOnlyList<AvisoDeCuenta>>.Exito(await avisosDeCuenta.Listar(usuarioId, cancelacion).ConfigureAwait(false));
    }

    public async Task<Resultado<RespuestaUniforme>> MarcarElAvisoComoVisto(Guid usuarioId, Guid avisoId, bool desdeUnaSesionReconocida, CancellationToken cancelacion = default)
    {
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<RespuestaUniforme>.Fallo(prohibido);
        }

        // Un aviso solo cuenta como visto desde una sesión reconocida del titular (WEB-36, WEB-39, IDE-76): desde otra no se marca.
        if (!desdeUnaSesionReconocida)
        {
            return Resultado<RespuestaUniforme>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "sesion-no-reconocida"));
        }

        return await avisosDeCuenta.MarcarVisto(usuarioId, avisoId, reloj.Ahora, cancelacion).ConfigureAwait(false)
            ? Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme())
            : Resultado<RespuestaUniforme>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "aviso-no-encontrado"));
    }

    private static ErrorDeAplicacion YaActivo { get; } = new(CategoriaDeError.Conflicto, "segundo-factor-ya-activo");

    private static ErrorDeAplicacion NoActivo { get; } = new(CategoriaDeError.Conflicto, "segundo-factor-no-activo");

    private static ErrorDeAplicacion NoIniciado { get; } = new(CategoriaDeError.Conflicto, "segundo-factor-no-iniciado");

    /// <summary>La forma enmascarada del número que ve Carrito (CAR-18): el prefijo y los tres últimos dígitos; el número completo no sale de Identidad.</summary>
    public static string Enmascarar(string numeroE164)
    {
        ArgumentException.ThrowIfNullOrEmpty(numeroE164);
        return numeroE164.Length <= 6 ? new string('*', numeroE164.Length) : string.Concat(numeroE164.AsSpan(0, 3), new string('*', numeroE164.Length - 6), numeroE164.AsSpan(numeroE164.Length - 3));
    }

    private async Task<CuentaDeIdentidad?> Cargar(Guid usuarioId, bool paraActualizar, CancellationToken cancelacion)
    {
        var cuenta = await cuentas.PorId(usuarioId, paraActualizar, cancelacion).ConfigureAwait(false);
        return cuenta is { CanceladaEn: null, Usuario.Activo: true } ? cuenta : null;
    }

    private async Task<CuentaDelCliente> Modelo(CuentaDeIdentidad cuenta, CancellationToken cancelacion)
    {
        var guardado = await segundoFactor.Obtener(cuenta.Usuario.Id, cancelacion).ConfigureAwait(false);
        var activo = guardado is { Factor.EstaActivo: true };
        return new CuentaDelCliente(
            cuenta.Usuario.Id,
            cuenta.Nombre,
            cuenta.Usuario.Correo,
            cuenta.Usuario.CorreoVerificado,
            cuenta.Whatsapp.Numero?.Valor,
            cuenta.Whatsapp.PuedeRecibirAvisos,
            activo,
            activo ? guardado!.Factor.CodigosDeRespaldoRestantes : 0);
    }

    /// <summary>La familia de la sesión desde la que se hace el cambio, solo si es de este usuario: si no se puede identificar, se revocan todas (lo más seguro).</summary>
    private async Task<Guid?> FamiliaDeLaSesion(Guid usuarioId, ValorSecreto? renovacion, CancellationToken cancelacion)
    {
        if (renovacion is null)
        {
            return null;
        }

        var actual = await renovaciones.PorHuella(huellasDeIdentidad.DeValorOpaco(renovacion.Revelar()), cancelacion).ConfigureAwait(false);
        return actual is not null && actual.UsuarioId == usuarioId ? actual.Familia : null;
    }

    private Task ConservarSoloLaSesion(CuentaDeIdentidad cuenta, Guid? familia, CancellationToken cancelacion) =>
        familia is { } conservada ? revocacion.LasDemas(cuenta, conservada, cancelacion) : revocacion.Todas(cuenta, cancelacion);

    private Task Auditar(AccionAuditable accion, Guid usuarioId, string motivo, CancellationToken cancelacion) =>
        auditoria.Registrar(new SolicitudDeRegistro(accion) { ObjetoTipo = "cuenta", ObjetoId = usuarioId.ToString("D"), Motivo = motivo }, cancelacion);

    private sealed record Verificacion(ErrorDeAplicacion? Error, CodigoPresentado? Codigo, List<ReservaDeLimite> Reservas);

    /// <summary>La contraseña actual y después un código del segundo factor, con sus límites; sin segundo factor activo no hay nada que hacer con él.</summary>
    private async Task<Verificacion> VerificarContrasenaYCodigo(CuentaDeIdentidad cuenta, ValorSecreto contrasenaActual, ValorSecreto codigo, bool permiteRespaldo, CancellationToken cancelacion)
    {
        var usuarioId = cuenta.Usuario.Id;
        if (await reautenticacion.ContrasenaActual(cuenta, contrasenaActual, cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return new Verificacion(rechazo, null, []);
        }

        var (limite, reservas) = await LimitesDelSegundoFactor.Reservar(limites, usuarioId, cancelacion).ConfigureAwait(false);
        if (limite is not null)
        {
            return new Verificacion(limite, null, reservas);
        }

        var guardado = await segundoFactor.Obtener(usuarioId, cancelacion).ConfigureAwait(false);
        if (guardado is not { Factor.EstaActivo: true })
        {
            await LimitesDelSegundoFactor.Devolver(limites, reservas, cancelacion).ConfigureAwait(false);
            return new Verificacion(NoActivo, null, []);
        }

        var presentado = reautenticacion.Evaluar(usuarioId, guardado, codigo.Revelar(), permiteRespaldo);
        if (presentado is null)
        {
            await reautenticacion.CodigoIncorrecto(usuarioId, cancelacion).ConfigureAwait(false);
            return new Verificacion(FallosDeIdentidad.CodigoInvalido, null, reservas);
        }

        return new Verificacion(null, presentado, reservas);
    }

    /// <summary>
    /// Acepta el código con la actualización condicional atómica <b>dentro</b> de la transacción del cambio: si otra solicitud lo aceptó antes, no se hace nada (código inválido). Bajo el bloqueo de la fila del usuario.
    /// </summary>
    private async Task<Resultado<T>> EjecutarConElCodigo<T>(Guid usuarioId, CodigoPresentado presentado, Func<CuentaDeIdentidad, CancellationToken, Task<Resultado<T>>> cambio, CancellationToken cancelacion)
    {
        var perdioLaCarrera = false;
        var resultado = await unidad.Ejecutar(
            async ct =>
            {
                var cuenta = await Cargar(usuarioId, true, ct).ConfigureAwait(false);
                if (cuenta is null)
                {
                    return Resultado<T>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
                }

                if (!await reautenticacion.Aceptar(usuarioId, presentado, ct).ConfigureAwait(false))
                {
                    perdioLaCarrera = true;
                    return Resultado<T>.Fallo(FallosDeIdentidad.CodigoInvalido);
                }

                return await cambio(cuenta, ct).ConfigureAwait(false);
            },
            cancelacion).ConfigureAwait(false);

        if (perdioLaCarrera)
        {
            await reautenticacion.CodigoIncorrecto(usuarioId, cancelacion).ConfigureAwait(false);
        }

        return resultado;
    }
}
