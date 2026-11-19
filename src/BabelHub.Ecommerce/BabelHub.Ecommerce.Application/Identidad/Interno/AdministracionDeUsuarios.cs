using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>
/// La administración de usuarios por el administrador central (T19; IDE-04 a IDE-07, IDE-23, IDE-38, IDE-44, IDE-45, IDE-49, IDE-87, AC-IDE-04, AC-IDE-05, AC-IDE-06, AC-IDE-21, AC-IDE-35, AC-IDE-44, AC-IDE-66). Quien llama
/// debe ser el administrador central <b>activo</b> según la base (no basta la afirmación de la credencial) y su identificador debe ser el del actor; si no, <c>Prohibido</c> y denegación registrada. Cada cambio se hace
/// bajo los bloqueos de las filas afectadas, aplica las reglas del Dominio (nunca sobre uno mismo, nunca el último administrador, un encargado por sucursal, correo verificado para el personal) y se audita en la misma
/// transacción (AUD-04): si no se puede auditar, no se aplica. Las contraseñas temporales (24 horas, IDE-23) se muestran una sola vez al administrador y no se registran en la auditoría ni en ningún log.
/// </summary>
public sealed class AdministracionDeUsuarios(
    IRepositorioDeCuentas cuentas,
    AlcanceDeLaCuenta alcance,
    IHasherDeContrasenas hasher,
    IGeneradorDeSecretos secretos,
    ISegundoFactorAlmacen segundoFactor,
    RevocacionDeSesiones revocacion,
    ICancelacionDeAvisos cancelacionDeAvisos,
    AvisosDeCuenta avisos,
    ICatalogoDeSucursales sucursales,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IReloj reloj) : IAdministracionDeUsuarios
{
    /// <summary>Cuánto dura una contraseña temporal (IDE-23).</summary>
    public static readonly TimeSpan VigenciaDeLaTemporal = TimeSpan.FromHours(24);

    public async Task<Resultado<ContrasenaTemporalEntregada>> Crear(Guid administradorId, string nombre, string correo, RolDeCuenta rol, string? codigoDeSucursal, CancellationToken cancelacion = default)
    {
        var (error, administrador) = await Administrador(administradorId, cancelacion).ConfigureAwait(false);
        if (error is not null)
        {
            return Resultado<ContrasenaTemporalEntregada>.Fallo(error);
        }

        if (!Enum.IsDefined(rol))
        {
            return Resultado<ContrasenaTemporalEntregada>.Fallo(FallosDeIdentidad.Validacion("rol", "invalido"));
        }

        var rolDeUsuario = RolDeUsuarioDe(rol);
        if (!EntradasDeIdentidad.NombreValido(nombre))
        {
            return Resultado<ContrasenaTemporalEntregada>.Fallo(FallosDeIdentidad.Validacion("nombre", "invalido"));
        }

        if (!EntradasDeIdentidad.TryNormalizarElCorreo(correo, out var correoNormalizado))
        {
            return Resultado<ContrasenaTemporalEntregada>.Fallo(FallosDeIdentidad.Validacion("correo", "invalido"));
        }

        // Aquí se crea personal: un cliente se registra solo (IDE-01).
        if (!rolDeUsuario.EsDePersonal())
        {
            return Resultado<ContrasenaTemporalEntregada>.Fallo(FallosDeIdentidad.Validacion("rol", "no-es-de-personal"));
        }

        var (falloDeSucursal, sucursal) = await ResolverLaSucursal(rolDeUsuario, codigoDeSucursal, cancelacion).ConfigureAwait(false);
        if (falloDeSucursal is not null)
        {
            return Resultado<ContrasenaTemporalEntregada>.Fallo(falloDeSucursal);
        }

        if (!Usuario.SucursalCoincideConElRol(rolDeUsuario, sucursal))
        {
            return Resultado<ContrasenaTemporalEntregada>.Fallo(FallosDeIdentidad.Validacion("sucursal", "invalida"));
        }

        var temporal = secretos.ContrasenaTemporal();
        var resumen = await ResumirLaTemporal(temporal, cancelacion).ConfigureAwait(false);
        return await unidad.Ejecutar(
            async ct =>
            {
                if (rolDeUsuario.LlevaSucursal() && await cuentas.EncargadoActivoDe(sucursal!.Value, ct).ConfigureAwait(false) is { } ocupada)
                {
                    return Resultado<ContrasenaTemporalEntregada>.Fallo(SucursalConEncargado(ocupada.UsuarioId));
                }

                var ahora = reloj.Ahora;

                // El personal nace con el correo verificado: lo da de alta el administrador y la regla IDE-87 exige que todo rol de personal lo tenga.
                var usuario = Usuario.Restaurar(secretos.NuevoIdentificador(), correoNormalizado, rolDeUsuario, sucursal, correoVerificado: true, activo: true);
                var cuenta = new CuentaDeIdentidad
                {
                    Usuario = usuario,
                    Nombre = nombre.Trim(),
                    ResumenDeAcceso = resumen,
                    ContrasenaTemporalHasta = ahora + VigenciaDeLaTemporal,
                    DebeCambiarLaTemporal = true,
                    Aceptacion = AceptacionDeTextos.Vacia(),
                    CreadaEn = ahora,
                };
                if (!await cuentas.Insertar(cuenta, ct).ConfigureAwait(false))
                {
                    return Resultado<ContrasenaTemporalEntregada>.Fallo(FallosDeIdentidad.CorreoEnUso);
                }

                await Auditar(AccionesAuditables.UsuarioDadoDeAlta, usuario.Id, motivo: null, new Dictionary<string, string?> { ["rolAsignado"] = rolDeUsuario.ToString() }, ct).ConfigureAwait(false);
                return Resultado<ContrasenaTemporalEntregada>.Exito(new ContrasenaTemporalEntregada(Modelo(cuenta), new ValorSecreto(temporal), ahora + VigenciaDeLaTemporal));
            },
            cancelacion).ConfigureAwait(false);
    }

    public async Task<Resultado<UsuarioAdministrado>> CambiarElRol(Guid administradorId, Guid usuarioId, RolDeCuenta rol, string? codigoDeSucursal, CancellationToken cancelacion = default)
    {
        var (error, administrador) = await Administrador(administradorId, cancelacion).ConfigureAwait(false);
        if (error is not null)
        {
            return Resultado<UsuarioAdministrado>.Fallo(error);
        }

        if (!Enum.IsDefined(rol))
        {
            return Resultado<UsuarioAdministrado>.Fallo(FallosDeIdentidad.Validacion("rol", "invalido"));
        }

        var nuevoRol = RolDeUsuarioDe(rol);
        var (falloDeSucursal, sucursal) = await ResolverLaSucursal(nuevoRol, codigoDeSucursal, cancelacion).ConfigureAwait(false);
        if (falloDeSucursal is not null)
        {
            return Resultado<UsuarioAdministrado>.Fallo(falloDeSucursal);
        }

        return await unidad.Ejecutar(
            async ct =>
            {
                var objetivo = await cuentas.PorId(usuarioId, paraActualizar: true, ct).ConfigureAwait(false);
                if (objetivo is null)
                {
                    return Resultado<UsuarioAdministrado>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
                }

                if (objetivo.Usuario.Rol == nuevoRol && objetivo.Usuario.Sucursal == sucursal)
                {
                    return Resultado<UsuarioAdministrado>.Exito(Modelo(objetivo));
                }

                var ahora = reloj.Ahora;
                var administradores = await cuentas.ContarAdministradoresActivos(ct).ConfigureAwait(false);
                var encargado = nuevoRol.LlevaSucursal() && sucursal is { } destino ? await cuentas.EncargadoActivoDe(destino, ct).ConfigureAwait(false) : null;
                var decision = ReglasDeAdministracion.CambiarRolODesactivar(administrador!.Usuario, objetivo.Usuario, nuevoRol, sucursal, administradores, ahora, encargado);
                if (!decision.EsPermitida)
                {
                    return Resultado<UsuarioAdministrado>.Fallo(await Rechazo(decision, ct).ConfigureAwait(false));
                }

                var anterior = objetivo.Usuario.Rol;
                objetivo.Usuario.CambiarRol(nuevoRol, sucursal, ahora);
                await cuentas.Guardar(objetivo, ct).ConfigureAwait(false);

                // Cambiar de rol invalida las sesiones: el rol nuevo se exige en un ingreso completo (IDE-21).
                await revocacion.Todas(objetivo, ct).ConfigureAwait(false);
                await Auditar(
                    AccionesAuditables.CambioDeRol,
                    usuarioId,
                    "asignacion-de-administracion",
                    new Dictionary<string, string?> { ["rolAnterior"] = anterior.ToString(), ["rolNuevo"] = nuevoRol.ToString() },
                    ct).ConfigureAwait(false);
                return Resultado<UsuarioAdministrado>.Exito(Modelo(objetivo));
            },
            cancelacion).ConfigureAwait(false);
    }

    public async Task<Resultado<PaginaDeUsuarios>> Listar(Guid administradorId, FiltroDeUsuarios filtro, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var (error, _) = await Administrador(administradorId, cancelacion).ConfigureAwait(false);
        if (error is not null)
        {
            return Resultado<PaginaDeUsuarios>.Fallo(error);
        }

        if (filtro.Rol is { } rolPedido && !Enum.IsDefined(rolPedido))
        {
            return Resultado<PaginaDeUsuarios>.Fallo(FallosDeIdentidad.Validacion("rol", "invalido"));
        }

        string? correo = null;
        if (!string.IsNullOrWhiteSpace(filtro.Correo))
        {
            if (!EntradasDeIdentidad.TryNormalizarElCorreo(filtro.Correo, out var normalizado))
            {
                return Resultado<PaginaDeUsuarios>.Fallo(FallosDeIdentidad.Validacion("correo", "invalido"));
            }

            correo = normalizado;
        }

        // La lista de clientes no se entrega: un cliente se encuentra solo por su correo exacto.
        if (correo is null && filtro.Rol == RolDeCuenta.Cliente)
        {
            return Resultado<PaginaDeUsuarios>.Fallo(FallosDeIdentidad.Validacion("rol", "cliente-solo-por-correo"));
        }

        if (filtro.Despues is { Length: > 254 })
        {
            return Resultado<PaginaDeUsuarios>.Fallo(FallosDeIdentidad.Validacion("despues", "invalido"));
        }

        var tamano = filtro.Tamano <= 0 ? 20 : Math.Min(filtro.Tamano, FiltroDeUsuarios.TamanoMaximo);
        var encontradas = await cuentas.Listar(filtro.Rol is { } r ? RolDeUsuarioDe(r) : null, filtro.Activo, correo, filtro.Despues, soloPersonal: correo is null, tamano + 1, cancelacion).ConfigureAwait(false);
        var pagina = encontradas.Take(tamano).ToList();
        return Resultado<PaginaDeUsuarios>.Exito(new PaginaDeUsuarios([.. pagina.Select(Modelo)], encontradas.Count > tamano ? pagina[^1].Usuario.Correo : null));
    }

    public async Task<Resultado<UsuarioAdministrado>> Desactivar(Guid administradorId, Guid usuarioId, CancellationToken cancelacion = default)
    {
        var (error, administrador) = await Administrador(administradorId, cancelacion).ConfigureAwait(false);
        if (error is not null)
        {
            return Resultado<UsuarioAdministrado>.Fallo(error);
        }

        return await unidad.Ejecutar(
            async ct =>
            {
                var objetivo = await cuentas.PorId(usuarioId, paraActualizar: true, ct).ConfigureAwait(false);
                if (objetivo is null)
                {
                    return Resultado<UsuarioAdministrado>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
                }

                var administradores = await cuentas.ContarAdministradoresActivos(ct).ConfigureAwait(false);
                var decision = ReglasDeAdministracion.CambiarRolODesactivar(administrador!.Usuario, objetivo.Usuario, nuevoRol: null, sucursal: null, administradores, reloj.Ahora, encargadoActualDeLaSucursal: null);
                if (!decision.EsPermitida)
                {
                    return Resultado<UsuarioAdministrado>.Fallo(await Rechazo(decision, ct).ConfigureAwait(false));
                }

                objetivo.Usuario.Desactivar();
                await cuentas.Guardar(objetivo, ct).ConfigureAwait(false);

                // Una cuenta desactivada no renueva ni recibe avisos (IDE-08, IDE-69): sus sesiones se revocan y sus avisos pendientes se cancelan en la misma transacción.
                await revocacion.Todas(objetivo, ct).ConfigureAwait(false);
                await cancelacionDeAvisos.CancelarLosAvisosDe(usuarioId, ct).ConfigureAwait(false);
                await Auditar(AccionesAuditables.UsuarioDesactivado, usuarioId, motivo: null, detalle: null, ct).ConfigureAwait(false);
                return Resultado<UsuarioAdministrado>.Exito(Modelo(objetivo));
            },
            cancelacion).ConfigureAwait(false);
    }

    public async Task<Resultado<UsuarioAdministrado>> Reactivar(Guid administradorId, Guid usuarioId, CancellationToken cancelacion = default)
    {
        var (error, administrador) = await Administrador(administradorId, cancelacion).ConfigureAwait(false);
        if (error is not null)
        {
            return Resultado<UsuarioAdministrado>.Fallo(error);
        }

        return await unidad.Ejecutar(
            async ct =>
            {
                var objetivo = await cuentas.PorId(usuarioId, paraActualizar: true, ct).ConfigureAwait(false);
                if (objetivo is null)
                {
                    return Resultado<UsuarioAdministrado>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
                }

                var decision = ReglasDeAdministracion.Restablecer(administrador!.Usuario, objetivo.Usuario);
                if (!decision.EsPermitida)
                {
                    return Resultado<UsuarioAdministrado>.Fallo(await Rechazo(decision, ct).ConfigureAwait(false));
                }

                // Una cuenta cancelada no vuelve (IDE-47) y una activa no se reactiva.
                if (objetivo.CanceladaEn is not null || objetivo.Usuario.Activo)
                {
                    return Resultado<UsuarioAdministrado>.Fallo(CuentaNoActiva);
                }

                // Mientras estuvo desactivado otro pudo ocupar su sucursal (IDE-06).
                if (objetivo.Usuario.Rol.LlevaSucursal()
                    && await cuentas.EncargadoActivoDe(objetivo.Usuario.Sucursal!.Value, ct).ConfigureAwait(false) is { } ocupada
                    && ocupada.UsuarioId != usuarioId)
                {
                    return Resultado<UsuarioAdministrado>.Fallo(SucursalConEncargado(ocupada.UsuarioId));
                }

                objetivo.Usuario.Reactivar();
                await cuentas.Guardar(objetivo, ct).ConfigureAwait(false);
                await Auditar(AccionesAuditables.UsuarioReactivado, usuarioId, motivo: null, detalle: null, ct).ConfigureAwait(false);
                return Resultado<UsuarioAdministrado>.Exito(Modelo(objetivo));
            },
            cancelacion).ConfigureAwait(false);
    }

    public async Task<Resultado<ContrasenaTemporalEntregada>> RestablecerLaContrasena(Guid administradorId, Guid usuarioId, CancellationToken cancelacion = default)
    {
        var (error, administrador) = await Administrador(administradorId, cancelacion).ConfigureAwait(false);
        if (error is not null)
        {
            return Resultado<ContrasenaTemporalEntregada>.Fallo(error);
        }

        var temporal = secretos.ContrasenaTemporal();
        var resumen = await ResumirLaTemporal(temporal, cancelacion).ConfigureAwait(false);
        Usuario? avisado = null;
        var resultado = await unidad.Ejecutar(
            async ct =>
            {
                var objetivo = await cuentas.PorId(usuarioId, paraActualizar: true, ct).ConfigureAwait(false);
                if (objetivo is null)
                {
                    return Resultado<ContrasenaTemporalEntregada>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
                }

                var decision = ReglasDeAdministracion.Restablecer(administrador!.Usuario, objetivo.Usuario);
                if (!decision.EsPermitida)
                {
                    return Resultado<ContrasenaTemporalEntregada>.Fallo(await Rechazo(decision, ct).ConfigureAwait(false));
                }

                if (objetivo.CanceladaEn is not null || !objetivo.Usuario.Activo)
                {
                    return Resultado<ContrasenaTemporalEntregada>.Fallo(CuentaNoActiva);
                }

                // La contraseña de un cliente nunca la conoce la administración: él la recupera por su correo (IDE-17).
                if (!objetivo.Usuario.Rol.EsDePersonal())
                {
                    return Resultado<ContrasenaTemporalEntregada>.Fallo(SoloPersonal);
                }

                var ahora = reloj.Ahora;
                objetivo.ResumenDeAcceso = resumen;
                objetivo.ContrasenaTemporalHasta = ahora + VigenciaDeLaTemporal;
                objetivo.DebeCambiarLaTemporal = true;
                await cuentas.Guardar(objetivo, ct).ConfigureAwait(false);

                // Otra persona restablece la contraseña: se revocan todas las renovaciones (IDE-44).
                await revocacion.Todas(objetivo, ct).ConfigureAwait(false);
                await Auditar(AccionesAuditables.ContrasenaRestablecidaPorAdministracion, usuarioId, motivo: null, detalle: null, ct).ConfigureAwait(false);
                avisado = objetivo.Usuario;
                return Resultado<ContrasenaTemporalEntregada>.Exito(new ContrasenaTemporalEntregada(Modelo(objetivo), new ValorSecreto(temporal), ahora + VigenciaDeLaTemporal));
            },
            cancelacion).ConfigureAwait(false);

        if (resultado.EsExito && avisado is not null)
        {
            await avisos.ContrasenaCambiada(avisado, cancelacion).ConfigureAwait(false);
        }

        return resultado;
    }

    public async Task<Resultado<RespuestaUniforme>> RestablecerElSegundoFactor(Guid administradorId, Guid usuarioId, CancellationToken cancelacion = default)
    {
        var (error, administrador) = await Administrador(administradorId, cancelacion).ConfigureAwait(false);
        if (error is not null)
        {
            return Resultado<RespuestaUniforme>.Fallo(error);
        }

        Usuario? avisado = null;
        var resultado = await unidad.Ejecutar(
            async ct =>
            {
                var objetivo = await cuentas.PorId(usuarioId, paraActualizar: true, ct).ConfigureAwait(false);
                if (objetivo is null)
                {
                    return Resultado<RespuestaUniforme>.Fallo(AlcanceDeLaCuenta.CuentaNoEncontrada);
                }

                var decision = ReglasDeAdministracion.Restablecer(administrador!.Usuario, objetivo.Usuario);
                if (!decision.EsPermitida)
                {
                    return Resultado<RespuestaUniforme>.Fallo(await Rechazo(decision, ct).ConfigureAwait(false));
                }

                if (objetivo.CanceladaEn is not null || !objetivo.Usuario.Activo)
                {
                    return Resultado<RespuestaUniforme>.Fallo(CuentaNoActiva);
                }

                // Se borra la configuración: el personal debe configurarlo de nuevo en su siguiente ingreso (IDE-21, IDE-38) y todas sus renovaciones se revocan.
                await segundoFactor.Desactivar(usuarioId, ct).ConfigureAwait(false);
                await revocacion.Todas(objetivo, ct).ConfigureAwait(false);
                await Auditar(AccionesAuditables.SegundoFactorRestablecidoPorAdministracion, usuarioId, motivo: null, detalle: null, ct).ConfigureAwait(false);
                avisado = objetivo.Usuario;
                return Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme());
            },
            cancelacion).ConfigureAwait(false);

        if (resultado.EsExito && avisado is not null)
        {
            await avisos.SegundoFactorRestablecido(avisado, cancelacion).ConfigureAwait(false);
        }

        return resultado;
    }

    /// <summary>
    /// D20: la sucursal de un encargado se asigna <b>por código</b>. Un código sin la forma de un código de sucursal se rechaza; el de un rol que lleva sucursal debe estar en <c>inventario.sucursales.listar</c> (si el Inventario no responde,
    /// <c>servicio-no-disponible</c>: nunca se asigna una sucursal sin comprobarla); y el identificador guardado en la cuenta y emitido en la credencial es el derivado del código (<see cref="IdentificadorDeSucursal"/>). Se llama
    /// <b>fuera</b> de la transacción (no hay una llamada de red con filas bloqueadas). Un rol que no lleva sucursal con un código se deriva igual y lo rechazan las reglas del Dominio.
    /// </summary>
    private async Task<(ErrorDeAplicacion? Error, Guid? Sucursal)> ResolverLaSucursal(RolDeUsuario rol, string? codigoDeSucursal, CancellationToken cancelacion)
    {
        if (string.IsNullOrWhiteSpace(codigoDeSucursal))
        {
            return (null, null);
        }

        if (IdentificadorDeSucursal.DesdeCodigo(codigoDeSucursal) is not { } derivado)
        {
            return (FallosDeIdentidad.Validacion("sucursal", "invalida"), null);
        }

        if (!rol.LlevaSucursal())
        {
            return (null, derivado);
        }

        return await sucursales.Existe(codigoDeSucursal.Trim(), cancelacion).ConfigureAwait(false) switch
        {
            ExistenciaDeSucursal.Existe => (null, derivado),
            ExistenciaDeSucursal.NoExiste => (FallosDeIdentidad.Validacion("sucursal", "desconocida"), null),
            _ => (FallosDeIdentidad.NoDisponible, null),
        };
    }

    private static ErrorDeAplicacion CuentaNoActiva { get; } = new(CategoriaDeError.Conflicto, "cuenta-no-activa");

    private static ErrorDeAplicacion SoloPersonal { get; } = new(CategoriaDeError.Conflicto, "solo-personal");

    /// <summary>La sucursal ya tiene encargado (IDE-06): el rechazo indica <b>quién</b> con su identificador opaco.</summary>
    private static ErrorDeAplicacion SucursalConEncargado(Guid encargadoActual) =>
        new(CategoriaDeError.Conflicto, "sucursal-con-encargado", [new ErrorDeCampo("encargadoActual", encargadoActual.ToString("D"))]);

    private static RolDeUsuario RolDeUsuarioDe(RolDeCuenta rol) => rol switch
    {
        RolDeCuenta.Cliente => RolDeUsuario.Cliente,
        RolDeCuenta.EncargadoDeSucursal => RolDeUsuario.EncargadoDeSucursal,
        RolDeCuenta.Logistica => RolDeUsuario.Logistica,
        RolDeCuenta.AdministradorCentral => RolDeUsuario.AdministradorCentral,
        _ => throw new ArgumentOutOfRangeException(nameof(rol), rol, "Rol desconocido."),
    };

    private static UsuarioAdministrado Modelo(CuentaDeIdentidad cuenta) => new(
        cuenta.Usuario.Id,
        cuenta.Nombre,
        cuenta.Usuario.Correo,
        EmisionDeSesion.RolDeCuentaDe(cuenta.Usuario.Rol),
        cuenta.Usuario.Sucursal,
        cuenta.Usuario.Activo,
        cuenta.Usuario.CorreoVerificado);

    /// <summary>El administrador que llama: el actor debe ser él y la base debe confirmarlo como administrador central activo; si no, <c>Prohibido</c> con la denegación registrada.</summary>
    private Task<(ErrorDeAplicacion? Error, CuentaDeIdentidad? Cuenta)> Administrador(Guid administradorId, CancellationToken cancelacion)
    {
        return new AlcanceDeLaAdministracion(alcance, cuentas).Exigir(administradorId, cancelacion);
    }

    private async Task<ErrorDeAplicacion> Rechazo(DecisionDeAdministracion decision, CancellationToken cancelacion) => decision.Motivo switch
    {
        MotivoDeRechazoDeUsuario.NoEsAdministrador => await alcance.Denegar(cancelacion, "fuera-de-rol").ConfigureAwait(false),
        MotivoDeRechazoDeUsuario.SobreSiMismo => new ErrorDeAplicacion(CategoriaDeError.Conflicto, "sobre-si-mismo"),
        MotivoDeRechazoDeUsuario.UltimoAdministrador => new ErrorDeAplicacion(CategoriaDeError.Conflicto, "ultimo-administrador"),
        MotivoDeRechazoDeUsuario.SucursalYaTieneEncargado => SucursalConEncargado(decision.EncargadoActual!.Value),
        MotivoDeRechazoDeUsuario.SucursalInvalida => FallosDeIdentidad.Validacion("sucursal", "invalida"),
        MotivoDeRechazoDeUsuario.CorreoSinVerificar => new ErrorDeAplicacion(CategoriaDeError.Conflicto, "correo-sin-verificar"),
        MotivoDeRechazoDeUsuario.CuentaNoActiva => CuentaNoActiva,
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision.Motivo, "Motivo de rechazo desconocido."),
    };

    /// <summary>El resumen de la contraseña temporal: la generó un CSPRNG con al menos 20 caracteres, de modo que siempre cumple la política.</summary>
    private async Task<string> ResumirLaTemporal(string temporal, CancellationToken cancelacion)
    {
        var (contrasena, _) = Contrasena.Crear(temporal, _ => false);
        return contrasena is null
            ? throw new InvalidOperationException("La contraseña temporal generada no cumple la política.")
            : await hasher.Resumir(contrasena, cancelacion).ConfigureAwait(false);
    }

    private Task Auditar(AccionAuditable accion, Guid usuarioId, string? motivo, IReadOnlyDictionary<string, string?>? detalle, CancellationToken cancelacion)
    {
        var solicitud = new SolicitudDeRegistro(accion) { ObjetoTipo = "usuario", ObjetoId = usuarioId.ToString("D"), Motivo = motivo };
        return auditoria.Registrar(detalle is null ? solicitud : solicitud with { Detalle = detalle }, cancelacion);
    }
}
