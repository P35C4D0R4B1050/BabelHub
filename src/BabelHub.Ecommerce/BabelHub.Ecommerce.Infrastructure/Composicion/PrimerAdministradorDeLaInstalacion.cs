using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>Qué hizo la operación del primer administrador.</summary>
public enum ResultadoDelPrimerAdministrador
{
    /// <summary>Creó la cuenta con una contraseña temporal.</summary>
    Creado,

    /// <summary>La cuenta existía y aún no completó su primer ingreso: se generó otra contraseña temporal.</summary>
    ContrasenaTemporalRegenerada,

    /// <summary>La cuenta ya existe y ya completó su primer ingreso: no se cambió nada.</summary>
    YaExiste,
}

/// <summary>El primer administrador y, solo si hubo cambio, su contraseña temporal (se muestra una sola vez). Nunca imprime la contraseña por sí sola.</summary>
public sealed record PrimerAdministradorCreado(Guid UsuarioId, string Correo, ResultadoDelPrimerAdministrador Resultado, ValorSecreto? ContrasenaTemporal)
{
    public override string ToString() => $"PrimerAdministradorCreado {{ UsuarioId = {UsuarioId}, Resultado = {Resultado} }}";
}

/// <summary>
/// La semilla del <b>primer administrador central</b> de la instalación (PAG-44; <c>crear-administrador</c>): sin él nadie entra a la gestión, carga el catálogo ni administra pedidos, y los demás administradores y el personal los da de alta el administrador.
/// Operación de consola, en <c>Staging</c> y en <c>Production</c> (nunca un endpoint), con el correo por parámetro (no va al repositorio). Crea la cuenta con una <b>contraseña temporal aleatoria mostrada una sola vez</b> que debe cambiarse en
/// el primer ingreso (24 horas), y como todo el personal <b>debe configurar su segundo factor (TOTP) en ese primer ingreso</b> antes de recibir una credencial (IDE-21). <b>Guarda explícita:</b> si ya existe un administrador central activo, no crea otro
/// (esta vía solo siembra el primero); repetirla con el mismo correo no crea otra cuenta y, si esa cuenta aún no completó su primer ingreso, solo genera otra contraseña temporal. Audita cada cambio en su misma transacción (actor «sistema»);
/// si no se puede auditar, no se aplica.
/// </summary>
public sealed class PrimerAdministradorDeLaInstalacion(
    IRepositorioDeCuentas cuentas,
    IHasherDeContrasenas hasher,
    IListaDeContrasenasComunes comunes,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    RevocacionDeSesiones revocacion,
    CuentaDeRevisionDeLaPasarela claveInicial,
    IReloj reloj)
{
    /// <summary>La vigencia de la contraseña temporal, la misma que la de una cuenta de personal dada de alta por la administración.</summary>
    public static readonly TimeSpan VigenciaDeLaTemporal = TimeSpan.FromHours(24);

    public const string NombrePorDefecto = "Administración central";

    public async Task<PrimerAdministradorCreado> Crear(string entorno, string? correo, string? nombre, CancellationToken cancelacion = default)
    {
        if (!string.Equals(entorno, "Staging", StringComparison.OrdinalIgnoreCase) && !string.Equals(entorno, "Production", StringComparison.OrdinalIgnoreCase))
        {
            throw new CuentaDeRevisionNoPermitidaException($"El primer administrador solo se crea en Staging o Production; este entorno es «{entorno}» y no se toca la base.");
        }

        if (string.IsNullOrWhiteSpace(correo) || !EntradasDeIdentidad.TryNormalizarElCorreo(correo, out var correoNormalizado))
        {
            throw new CuentaDeRevisionNoPermitidaException("Falta el correo del administrador o no tiene un formato válido (--correo <dirección>).");
        }

        var nombreDeLaCuenta = string.IsNullOrWhiteSpace(nombre) ? NombrePorDefecto : nombre.Trim();
        if (!EntradasDeIdentidad.NombreValido(nombreDeLaCuenta))
        {
            throw new CuentaDeRevisionNoPermitidaException("El nombre del administrador no es válido (hasta 100 caracteres, sin caracteres de control).");
        }

        await claveInicial.AsegurarLaPrimeraClaveDeFirma(cancelacion).ConfigureAwait(false);

        var temporal = CuentaDeRevisionDeLaPasarela.ContrasenaAleatoria();
        var (contrasena, _) = Contrasena.Crear(temporal, comunes.EsComun);
        var resumen = contrasena is null
            ? throw new InvalidOperationException("La contraseña generada no cumplió la política de contraseñas.")
            : await hasher.Resumir(contrasena, cancelacion).ConfigureAwait(false);
        var ahora = reloj.Ahora;

        return await unidad.Ejecutar(
            async ct =>
            {
                var existente = await cuentas.PorCorreo(correoNormalizado, ct).ConfigureAwait(false);
                if (existente is null)
                {
                    // La guarda explícita: esta vía solo siembra el primero.
                    if (await cuentas.ContarAdministradoresActivos(ct).ConfigureAwait(false) > 0)
                    {
                        throw new CuentaDeRevisionNoPermitidaException("Ya existe un administrador central: esta operación solo crea el primero. Los demás los da de alta la administración. No se tocó nada.");
                    }

                    var cuenta = new CuentaDeIdentidad
                    {
                        Usuario = Usuario.Restaurar(Guid.NewGuid(), correoNormalizado, RolDeUsuario.AdministradorCentral, null, correoVerificado: true, activo: true),
                        Nombre = nombreDeLaCuenta,
                        ResumenDeAcceso = resumen,
                        ContrasenaTemporalHasta = ahora + VigenciaDeLaTemporal,
                        DebeCambiarLaTemporal = true,
                        Aceptacion = AceptacionDeTextos.Vacia(),
                        CreadaEn = ahora,
                    };
                    if (!await cuentas.Insertar(cuenta, ct).ConfigureAwait(false))
                    {
                        throw new CuentaDeRevisionNoPermitidaException("No se pudo crear al administrador: otra operación lo creó a la vez. Repite la operación.");
                    }

                    await auditoria.Registrar(
                        new SolicitudDeRegistro(AccionesAuditables.UsuarioDadoDeAlta) { ObjetoTipo = "usuario", ObjetoId = cuenta.Usuario.Id.ToString("D"), Detalle = new Dictionary<string, string?> { ["rolAsignado"] = nameof(RolDeUsuario.AdministradorCentral) } },
                        ct).ConfigureAwait(false);
                    return new PrimerAdministradorCreado(cuenta.Usuario.Id, correoNormalizado, ResultadoDelPrimerAdministrador.Creado, new ValorSecreto(temporal));
                }

                var previa = await cuentas.PorId(existente.Usuario.Id, paraActualizar: true, ct).ConfigureAwait(false);
                if (previa is not { CanceladaEn: null, Usuario: { Rol: RolDeUsuario.AdministradorCentral, Activo: true } })
                {
                    throw new CuentaDeRevisionNoPermitidaException("Ese correo ya pertenece a otra cuenta: usa otro con --correo. No se tocó nada.");
                }

                if (!previa.DebeCambiarLaTemporal)
                {
                    // Ya completó su primer ingreso: idempotente, no hay nada que hacer (para otra contraseña, el administrador usa la recuperación de acceso).
                    return new PrimerAdministradorCreado(previa.Usuario.Id, correoNormalizado, ResultadoDelPrimerAdministrador.YaExiste, null);
                }

                // Solo se regenera la temporal del único administrador central: la de uno dado de alta después por la administración (cuando ya hay otro) no se muestra en la consola.
                if (await cuentas.ContarAdministradoresActivos(ct).ConfigureAwait(false) != 1)
                {
                    throw new CuentaDeRevisionNoPermitidaException("Ya existe otro administrador central: esta operación solo regenera la contraseña del único administrador que aún no ingresó. No se tocó nada.");
                }

                previa.ResumenDeAcceso = resumen;
                previa.ContrasenaTemporalHasta = ahora + VigenciaDeLaTemporal;
                previa.DebeCambiarLaTemporal = true;
                await cuentas.Guardar(previa, ct).ConfigureAwait(false);
                await revocacion.Todas(previa, ct).ConfigureAwait(false);
                await auditoria.Registrar(new SolicitudDeRegistro(AccionesAuditables.ContrasenaRestablecidaPorAdministracion) { ObjetoTipo = "usuario", ObjetoId = previa.Usuario.Id.ToString("D") }, ct).ConfigureAwait(false);
                return new PrimerAdministradorCreado(previa.Usuario.Id, correoNormalizado, ResultadoDelPrimerAdministrador.ContrasenaTemporalRegenerada, new ValorSecreto(temporal));
            },
            cancelacion).ConfigureAwait(false);
    }
}
