using System.Security.Cryptography;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>La operación no corresponde (otro entorno, un correo que no es de la cuenta de revisión, textos sin sembrar): el mensaje dice qué hacer y nunca lleva un dato de la cuenta ni un secreto.</summary>
public sealed class CuentaDeRevisionNoPermitidaException : InvalidOperationException
{
    public CuentaDeRevisionNoPermitidaException(string mensaje)
        : base(mensaje)
    {
    }

    public CuentaDeRevisionNoPermitidaException()
    {
    }

    public CuentaDeRevisionNoPermitidaException(string mensaje, Exception interna)
        : base(mensaje, interna)
    {
    }
}

/// <summary>La cuenta de revisión que dejó la operación: quién es y, <b>una sola vez</b>, su contraseña. Nunca imprime la contraseña por sí sola (<see cref="ToString"/>).</summary>
public sealed record CuentaDeRevisionCreada(Guid UsuarioId, string Correo, ValorSecreto Contrasena, bool ContrasenaRotada)
{
    public override string ToString() => $"CuentaDeRevisionCreada {{ UsuarioId = {UsuarioId}, ContrasenaRotada = {ContrasenaRotada} }}";
}

/// <summary>
/// La cuenta de cliente que la pasarela de pago (Culqi) usa para revisar la tienda en <c>Staging</c> (PAG-35). Es una <b>operación del despliegue</b> (la corre una persona por consola con <c>cuenta-de-prueba</c>, nunca un endpoint): crea
/// <b>una</b> cuenta de cliente con el correo verificado y los textos vigentes aceptados, con una contraseña aleatoria que se devuelve una sola vez y no se guarda en claro ni en el repositorio, el entorno ni los registros. Si la cuenta ya existe
/// (y es esta), solo rota su contraseña. <b>Falla cerrado fuera de <c>Staging</c></b> (en <c>Production</c> y en cualquier otro entorno se niega antes de tocar la base) y nunca toca una cuenta que no sea la suya. Si la instalación no tiene aún su
/// primera clave de firma, la crea, porque sin ella nadie ingresa; los textos legales vigentes los siembra el despliegue (<c>migrar</c>), no esta operación. Cada cambio se audita en su misma transacción (actor «sistema»): si no se puede auditar, no se aplica.
/// </summary>
public sealed class CuentaDeRevisionDeLaPasarela(
    IRepositorioDeCuentas cuentas,
    IHasherDeContrasenas hasher,
    IListaDeContrasenasComunes comunes,
    IVersionesDeTextosVigentes versiones,
    IUnidadDeTrabajo unidad,
    IAlmacenDeClavesDeFirma almacenDeClaves,
    IGeneradorDeClavesDeFirma generadorDeClaves,
    BabelHub.Ecommerce.Infrastructure.Identidad.CacheDeClavesDeVerificacion cacheDeVerificacion,
    IAuditoria auditoria,
    RevocacionDeSesiones revocacion,
    IEjecutorDeLaCancelacion ejecutorDeCancelacion,
    IReloj reloj)
{
    /// <summary>El único entorno donde existe esta operación.</summary>
    public const string EntornoPermitido = "Staging";

    /// <summary>El nombre con el que se reconoce la cuenta: solo una cuenta con este nombre se rota.</summary>
    public const string NombreDeLaCuenta = "Cuenta de revisión de la pasarela";

    /// <summary>Un correo de un dominio reservado para pruebas (RFC 2606): nunca llega a una persona.</summary>
    public const string CorreoPorDefecto = "revision@example.test";

    private const string Alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public async Task<CuentaDeRevisionCreada> Crear(string entorno, string? correo, CancellationToken cancelacion = default)
    {
        if (!string.Equals(entorno, EntornoPermitido, StringComparison.OrdinalIgnoreCase))
        {
            throw new CuentaDeRevisionNoPermitidaException($"La cuenta de prueba solo se crea en {EntornoPermitido}; este entorno es «{entorno}» y no se toca la base.");
        }

        var correoNormalizado = CorreoValido(correo);
        if (!CorreosDeTodasLasCuentas().Contains(correoNormalizado, StringComparer.Ordinal))
        {
            // El retiro y la purga solo buscan los correos propios de la operación (PAG-35, PAG-40, PAG-45): crear otro dejaría una cuenta que ellos no alcanzan.
            throw new CuentaDeRevisionNoPermitidaException($"El correo de una cuenta de prueba es el de la revisión ({CorreoPorDefecto}) o uno de los de las personas (persona-01 a persona-{MaximoDeCuentasParaPersonas:00}@example.test). No se tocó nada.");
        }

        var vigentes = await LeerLosTextosVigentes(cancelacion).ConfigureAwait(false);

        var texto = ContrasenaAleatoria();
        var (contrasena, _) = Contrasena.Crear(texto, comunes.EsComun);
        var resumen = contrasena is null
            ? throw new InvalidOperationException("La contraseña generada no cumplió la política de contraseñas.")
            : await hasher.Resumir(contrasena, cancelacion).ConfigureAwait(false);

        var ahora = reloj.Ahora;
        var claveCreada = false;
        var (id, rotada) = await unidad.Ejecutar(
            async ct =>
            {
                claveCreada = false;
                var existente = await cuentas.PorCorreo(correoNormalizado, ct).ConfigureAwait(false);
                if (existente is null)
                {
                    claveCreada = await AsegurarLaPrimeraClaveDeFirma(ct).ConfigureAwait(false);
                    var cuenta = new CuentaDeIdentidad
                    {
                        Usuario = Usuario.Restaurar(Guid.NewGuid(), correoNormalizado, RolDeUsuario.Cliente, null, correoVerificado: true, activo: true),
                        Nombre = NombreDeLaCuenta,
                        ResumenDeAcceso = resumen,
                        Aceptacion = AceptacionDeTextos.Restaurar(
                            ahora,
                            [(BabelHub.Ecommerce.Domain.Identidad.TextoLegal.Terminos, vigentes.Terminos, ahora), (BabelHub.Ecommerce.Domain.Identidad.TextoLegal.Privacidad, vigentes.Privacidad, ahora), (BabelHub.Ecommerce.Domain.Identidad.TextoLegal.Reembolso, vigentes.Reembolso, ahora)]),
                        CreadaEn = ahora,
                    };
                    if (!await cuentas.Insertar(cuenta, ct).ConfigureAwait(false))
                    {
                        throw new CuentaDeRevisionNoPermitidaException("No se pudo crear la cuenta de prueba: otra operación la creó a la vez. Repite la operación.");
                    }

                    await auditoria.Registrar(
                        new SolicitudDeRegistro(AccionesAuditables.UsuarioDadoDeAlta) { ObjetoTipo = "usuario", ObjetoId = cuenta.Usuario.Id.ToString("D"), Detalle = new Dictionary<string, string?> { ["rolAsignado"] = nameof(RolDeUsuario.Cliente) } },
                        ct).ConfigureAwait(false);
                    return (cuenta.Usuario.Id, false);
                }

                // Solo se rota la cuenta que esta operación creó: nunca la de una persona que casualmente tenga ese correo.
                var previa = await cuentas.PorId(existente.Usuario.Id, paraActualizar: true, ct).ConfigureAwait(false);
                if (!EsLaCuentaDeRevision(previa))
                {
                    throw new CuentaDeRevisionNoPermitidaException("Ese correo ya pertenece a otra cuenta, a una sin verificar o a la cuenta de revisión ya retirada: elige otro con --correo. No se tocó nada.");
                }

                claveCreada = await AsegurarLaPrimeraClaveDeFirma(ct).ConfigureAwait(false);
                previa!.ResumenDeAcceso = resumen;
                previa.ContrasenaTemporalHasta = null;
                previa.DebeCambiarLaTemporal = false;
                await cuentas.Guardar(previa, ct).ConfigureAwait(false);
                // Rotar es cortar una credencial que pudo conocer un tercero: la anterior deja de servir también para las sesiones y renovaciones ya abiertas (IDE-44).
                await revocacion.Todas(previa, ct).ConfigureAwait(false);
                await auditoria.Registrar(new SolicitudDeRegistro(AccionesAuditables.ContrasenaRestablecidaPorAdministracion) { ObjetoTipo = "usuario", ObjetoId = previa.Usuario.Id.ToString("D") }, ct).ConfigureAwait(false);
                return (previa.Usuario.Id, true);
            },
            cancelacion).ConfigureAwait(false);
        if (claveCreada)
        {
            await RenovarElVerificador(cancelacion).ConfigureAwait(false);
        }

        return new CuentaDeRevisionCreada(id, correoNormalizado, new ValorSecreto(texto), rotada);
    }

    /// <summary>
    /// Retira la cuenta de revisión (PAG-40): la desactiva, revoca todas sus sesiones y lo audita en la misma transacción. Existe en <c>Staging</c> y en <c>Production</c> (la cuenta llega viva a producción porque comparten base: es una
    /// credencial conocida por un tercero y debe retirarse antes de operar con clientes). Solo actúa sobre la cuenta que lleva el nombre de esta operación; idempotente (si ya está desactivada no hace nada).
    /// </summary>
    public async Task<bool> Retirar(string entorno, string? correo, CancellationToken cancelacion = default)
    {
        if (!string.Equals(entorno, EntornoPermitido, StringComparison.OrdinalIgnoreCase) && !string.Equals(entorno, "Production", StringComparison.OrdinalIgnoreCase))
        {
            throw new CuentaDeRevisionNoPermitidaException($"La cuenta de revisión solo se retira en {EntornoPermitido} o Production; este entorno es «{entorno}» y no se toca la base.");
        }

        var correoNormalizado = CorreoValido(correo);
        if (!CorreosDeTodasLasCuentas().Contains(correoNormalizado, StringComparer.Ordinal))
        {
            throw new CuentaDeRevisionNoPermitidaException($"El retiro solo admite los correos propios de la operación ({CorreoPorDefecto} y persona-01 a persona-{MaximoDeCuentasParaPersonas:00}@example.test). No se tocó nada.");
        }

        return await unidad.Ejecutar(
            async ct =>
            {
                var existente = await cuentas.PorCorreo(correoNormalizado, ct).ConfigureAwait(false);
                if (existente is null)
                {
                    return false;
                }

                var cuenta = await cuentas.PorId(existente.Usuario.Id, paraActualizar: true, ct).ConfigureAwait(false);
                if (cuenta is null || cuenta.Nombre != NombreDeLaCuenta || cuenta.Usuario.Rol != RolDeUsuario.Cliente)
                {
                    throw new CuentaDeRevisionNoPermitidaException("Ese correo pertenece a otra cuenta: no se tocó nada.");
                }

                if (!cuenta.Usuario.Activo)
                {
                    return false;
                }

                cuenta.Usuario.Desactivar();
                await cuentas.Guardar(cuenta, ct).ConfigureAwait(false);
                await revocacion.Todas(cuenta, ct).ConfigureAwait(false);
                await auditoria.Registrar(new SolicitudDeRegistro(AccionesAuditables.UsuarioDesactivado) { ObjetoTipo = "usuario", ObjetoId = cuenta.Usuario.Id.ToString("D") }, ct).ConfigureAwait(false);
                return true;
            },
            cancelacion).ConfigureAwait(false);
    }

    /// <summary>El tope de cuentas de prueba para las personas (PAG-43): una por participante, hasta este número.</summary>
    public const int MaximoDeCuentasParaPersonas = 20;

    /// <summary>Los correos de las cuentas para las pruebas con personas: <c>persona-01@example.test</c>, <c>persona-02@…</c> (PAG-43). Siempre los mismos, de modo que repetir la operación las rota y retirarlas o purgarlas las encuentra.</summary>
    public static string CorreoDeLaPersona(int numero) => $"persona-{numero:00}@example.test";

    /// <summary>
    /// Crea (o rota) varias cuentas de prueba para las pruebas con personas, solo en <c>Staging</c>, con una contraseña aleatoria cada una (PAG-43). Entre 1 y <see cref="MaximoDeCuentasParaPersonas"/>; un número fuera de ese rango se rechaza
    /// sin tocar nada. Cada cuenta se comporta como la de la revisión (verificada, textos aceptados, auditada, retirable y purgable).
    /// </summary>
    public async Task<IReadOnlyList<CuentaDeRevisionCreada>> CrearVarias(string entorno, int cantidad, Func<CuentaDeRevisionCreada, Task>? alCrearCada = null, CancellationToken cancelacion = default)
    {
        if (cantidad is < 1 or > MaximoDeCuentasParaPersonas)
        {
            throw new CuentaDeRevisionNoPermitidaException($"La cantidad de cuentas de prueba va de 1 a {MaximoDeCuentasParaPersonas}; no se hizo nada.");
        }

        var creadas = new List<CuentaDeRevisionCreada>();
        for (var i = 1; i <= cantidad; i++)
        {
            var cuenta = await Crear(entorno, CorreoDeLaPersona(i), cancelacion).ConfigureAwait(false);
            creadas.Add(cuenta);
            // Cada contraseña se entrega en cuanto existe: si una cuenta posterior falla, las anteriores (ya rotadas) no quedan sin mostrar.
            if (alCrearCada is not null)
            {
                await alCrearCada(cuenta).ConfigureAwait(false);
            }
        }

        return creadas;
    }

    /// <summary>Retira (desactiva) todas las cuentas de prueba: la de la revisión y las de las personas (PAG-40). Devuelve cuántas se retiraron.</summary>
    public async Task<int> RetirarTodas(string entorno, CancellationToken cancelacion = default)
    {
        var retiradas = 0;
        foreach (var correo in CorreosDeTodasLasCuentas())
        {
            if (await RetirarSiExiste(entorno, correo, cancelacion).ConfigureAwait(false))
            {
                retiradas++;
            }
        }

        return retiradas;
    }

    /// <summary>
    /// <b>Purga lo de prueba antes de operar con clientes</b> (PAG-45; Staging y Production): cancela con la cancelación de cuenta de Identidad (IDE-47, IDE-70) cada cuenta de prueba que exista, lo que hace que <b>cada módulo desvincule o anonimice lo suyo
    /// por su propio contrato</b> (pedidos, pagos, devoluciones, carritos, biblioteca y reclamaciones; los avisos pendientes se cancelan y la baja de las reservas del Inventario la entrega el trabajo de bajas por su contrato REST) y la cuenta queda anonimizada y
    /// sin ingreso. No toca el catálogo ni a ningún usuario real (solo las cuentas con el nombre y los correos propios) y no hace SQL sobre el esquema de otro módulo ni del otro servicio. <b>Las filas transaccionales se conservan anonimizadas</b> (la cancelación
    /// no las borra: lo exige la retención legal); la cancelación audita cada cuenta (<c>CuentaCancelada</c>). Idempotente: sin cuentas de prueba no hace nada. Devuelve cuántas se cancelaron.
    /// </summary>
    public async Task<int> Purgar(string entorno, CancellationToken cancelacion = default)
    {
        ExigirStagingOProduccion(entorno);
        var canceladas = 0;
        foreach (var correo in CorreosDeTodasLasCuentas())
        {
            var cancelada = await unidad.Ejecutar(
                async ct =>
                {
                    var existente = await cuentas.PorCorreo(correo, ct).ConfigureAwait(false);
                    if (existente is null)
                    {
                        return false;
                    }

                    var cuenta = await cuentas.PorId(existente.Usuario.Id, paraActualizar: true, ct).ConfigureAwait(false);
                    if (cuenta is null || cuenta.Nombre != NombreDeLaCuenta || cuenta.Usuario.Rol != RolDeUsuario.Cliente || cuenta.CanceladaEn is not null)
                    {
                        return false;
                    }

                    await ejecutorDeCancelacion.Ejecutar(cuenta, ct).ConfigureAwait(false);
                    return true;
                },
                cancelacion).ConfigureAwait(false);
            if (cancelada)
            {
                canceladas++;
            }
        }

        return canceladas;
    }

    private async Task<bool> RetirarSiExiste(string entorno, string correo, CancellationToken cancelacion)
    {
        ExigirStagingOProduccion(entorno);
        try
        {
            return await Retirar(entorno, correo, cancelacion).ConfigureAwait(false);
        }
        catch (CuentaDeRevisionNoPermitidaException)
        {
            // Una cuenta con ese correo que no es de prueba (de una persona) no se toca: se omite.
            return false;
        }
    }

    private static IEnumerable<string> CorreosDeTodasLasCuentas()
    {
        yield return CorreoPorDefecto;
        for (var i = 1; i <= MaximoDeCuentasParaPersonas; i++)
        {
            yield return CorreoDeLaPersona(i);
        }
    }

    private static void ExigirStagingOProduccion(string entorno)
    {
        if (!string.Equals(entorno, EntornoPermitido, StringComparison.OrdinalIgnoreCase) && !string.Equals(entorno, "Production", StringComparison.OrdinalIgnoreCase))
        {
            throw new CuentaDeRevisionNoPermitidaException($"Esta operación solo se hace en {EntornoPermitido} o Production; este entorno es «{entorno}» y no se toca la base.");
        }
    }

    /// <summary>Es la cuenta que creó esta operación y sigue siendo suya: cliente, con el nombre propio, el correo verificado, activa y no cancelada. Cualquier otra (por ejemplo, un registro sin verificar con el mismo correo) no se toca.</summary>
    private static bool EsLaCuentaDeRevision(CuentaDeIdentidad? cuenta) =>
        cuenta is { CanceladaEn: null, Usuario: { Rol: RolDeUsuario.Cliente, Activo: true, CorreoVerificado: true } } && cuenta.Nombre == NombreDeLaCuenta;

    /// <summary>
    /// El correo de la cuenta: por defecto el de un dominio reservado (RFC 2606); con <c>--correo</c> solo otro de un dominio reservado para pruebas (<c>.test</c>, <c>.invalid</c>, <c>.example</c>, <c>example.com</c>, <c>example.org</c>,
    /// <c>example.net</c>): crear una cuenta <b>verificada</b> sobre el correo real de una persona le impediría registrarse y le enviaría los avisos de compra de otra cuenta.
    /// </summary>
    private static string CorreoValido(string? correo)
    {
        if (!EntradasDeIdentidad.TryNormalizarElCorreo(string.IsNullOrWhiteSpace(correo) ? CorreoPorDefecto : correo, out var normalizado))
        {
            throw new CuentaDeRevisionNoPermitidaException("El correo de la cuenta de revisión no tiene un formato válido.");
        }

        var dominio = normalizado[(normalizado.IndexOf('@', StringComparison.Ordinal) + 1)..];
        if (!(dominio.EndsWith(".test", StringComparison.Ordinal) || dominio.EndsWith(".invalid", StringComparison.Ordinal) || dominio.EndsWith(".example", StringComparison.Ordinal)
            || dominio is "example.com" or "example.org" or "example.net"))
        {
            throw new CuentaDeRevisionNoPermitidaException("El correo de la cuenta de revisión debe ser de un dominio reservado para pruebas (.test, .invalid, .example o example.com/org/net): una cuenta verificada sobre un correo real no se crea.");
        }

        return normalizado;
    }

    private async Task<VersionesDeTextos> LeerLosTextosVigentes(CancellationToken cancelacion)
    {
        try
        {
            return await versiones.Obtener(cancelacion).ConfigureAwait(false);
        }
        catch (InvalidOperationException e)
        {
            // La fila de los textos vigentes la siembra el despliegue (BABELHUB__Migrador__Ecommerce__VersionDeTextos al migrar): sin ella ninguna cuenta puede aceptar los textos ni pagar.
            throw new CuentaDeRevisionNoPermitidaException("Faltan los textos legales vigentes: vuelve a migrar la base con BABELHUB__Migrador__Ecommerce__VersionDeTextos (ver la guía del primer despliegue) y repite.", e);
        }
    }

    /// <summary>
    /// Crea la primera clave de firma si no hay ninguna activa (PAG-46) <b>dentro de la transacción de quien la llama</b>, después de que este haya comprobado todo lo que puede negarse:
    /// así una negativa no la deja creada y, si falla la auditoría de la cuenta, la clave tampoco queda. Devuelve si la creó (entonces, tras confirmar, hay que llamar a <see cref="RenovarElVerificador"/>). La unicidad de la clave activa la sostiene el índice único parcial de la base: dos operaciones simultáneas no pueden crear dos.
    /// </summary>
    internal async Task<bool> AsegurarLaPrimeraClaveDeFirma(CancellationToken cancelacion)
    {
        var hayActiva = false;
        try
        {
            hayActiva = (await almacenDeClaves.Cargar(paraActualizar: false, cancelacion).ConfigureAwait(false)).Claves.Any(c => c.Estado == EstadoDeClaveDeFirma.Activa);
        }
        catch (InvalidOperationException)
        {
            // El almacén avisa así que no hay ninguna clave: se crea la primera.
        }

        if (hayActiva)
        {
            return false;
        }

        // Generar inserta la privada cifrada: va en la misma transacción que activarla y auditarla (como en la rotación), para que una falla no deje una clave huérfana.
        var primera = (await generadorDeClaves.Generar(reloj.Ahora, cancelacion).ConfigureAwait(false)).Clave;
        await almacenDeClaves.Guardar(JuegoDeClavesDeFirma.Iniciar(primera), cancelacion).ConfigureAwait(false);
        await auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.ClaveDeFirmaRotada) { ObjetoTipo = "clave-de-firma", ObjetoId = primera.Kid, Detalle = new Dictionary<string, string?> { ["versionNueva"] = primera.Kid } },
            cancelacion).ConfigureAwait(false);
        return true;
    }

    /// <summary>El verificador de credenciales guarda una foto de las claves publicadas: se renueva tras confirmar la transacción para que la primera credencial emitida ya se verifique.</summary>
    internal Task RenovarElVerificador(CancellationToken cancelacion) => cacheDeVerificacion.Renovar(cancelacion);

    internal static string ContrasenaAleatoria()
    {
        var texto = new char[28];
        for (var i = 0; i < texto.Length; i++)
        {
            texto[i] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];
        }

        // Una mayúscula, una minúscula, un dígito y un signo siempre presentes: la política no depende de la suerte.
        return new string(texto) + "-7aQ";
    }
}
