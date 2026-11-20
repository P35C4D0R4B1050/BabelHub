#if DEBUG
using System.Security.Cryptography;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>Una sesión sembrada para los recorridos E2E: la cuenta creada y el valor de la cookie opaca de la sesión web (nunca la credencial ni la renovación, que se quedan en el servidor).</summary>
public sealed record SesionSembrada(Guid UsuarioId, string Correo, RolDeCuenta Rol, Guid? Sucursal, HostWeb Host, string ValorDeLaCookie)
{
    /// <summary>No imprime la cookie (una sesión viva).</summary>
    public override string ToString() => $"SesionSembrada {{ UsuarioId = {UsuarioId}, Rol = {Rol} }}";
}

/// <summary>
/// El sembrador de sesiones de <b>solo desarrollo</b> (los recorridos E2E con compra necesitan un cliente con sesión y la pantalla de ingreso no basta para sembrar personal): crea una cuenta con el correo verificado y el rol pedido
/// (un encargado con su sucursal <b>por código</b>, con el identificador canónico de <see cref="IdentificadorDeSucursal"/>) y la <b>hace ingresar por el mismo camino real</b> —<see cref="IIngreso"/> (y el segundo factor del personal,
/// configurado con su secreto) y <see cref="ISesionesWeb"/>—, de modo que lo sembrado es exactamente lo que produce un ingreso. <b>No existe fuera de una compilación <c>Debug</c></b>: todo este archivo va bajo <c>#if DEBUG</c> y la imagen
/// se publica en <c>Release</c>, así que no hay configuración, variable de entorno ni nombre de entorno que lo encienda en producción. Las contraseñas se generan al azar en cada siembra y no se devuelven.
/// </summary>
public sealed class SembradorDeSesionesDeDesarrollo(
    IRepositorioDeCuentas cuentas,
    IHasherDeContrasenas hasher,
    IListaDeContrasenasComunes comunes,
    IVersionesDeTextosVigentes versiones,
    IUnidadDeTrabajo unidad,
    IIngreso ingreso,
    ISesionesWeb sesiones,
    IAlmacenDeClavesDeFirma almacenDeClaves,
    IGeneradorDeClavesDeFirma generadorDeClaves,
    BabelHub.Ecommerce.Infrastructure.Identidad.CacheDeClavesDeVerificacion cacheDeVerificacion,
    IReloj reloj)
{
    private const string Alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public async Task<SesionSembrada> Sembrar(RolDeCuenta rol, string? codigoDeSucursal, HostWeb host, CancellationToken cancelacion = default)
    {
        if (!Enum.IsDefined(rol) || (rol == RolDeCuenta.Cliente) != (host == HostWeb.Tienda))
        {
            throw new ArgumentException("El cliente ingresa por la tienda y el personal por la gestión: esta instancia sirve otro host.", nameof(host));
        }

        Guid? sucursal = null;
        if (rol == RolDeCuenta.EncargadoDeSucursal)
        {
            sucursal = IdentificadorDeSucursal.DesdeCodigo(codigoDeSucursal) ?? throw new ArgumentException("Un encargado necesita el código de su sucursal.", nameof(codigoDeSucursal));
        }
        else if (!string.IsNullOrWhiteSpace(codigoDeSucursal))
        {
            throw new ArgumentException("Solo un encargado lleva sucursal.", nameof(codigoDeSucursal));
        }

        // Una instalación nueva no tiene clave de firma activa (la crea la operación): sin ella nadie ingresa. Se crea la primera, como haría la operación.
        var hayActiva = false;
        try
        {
            hayActiva = (await almacenDeClaves.Cargar(paraActualizar: false, cancelacion).ConfigureAwait(false)).Claves.Any(c => c.Estado == EstadoDeClaveDeFirma.Activa);
        }
        catch (InvalidOperationException)
        {
            // El almacén avisa así que no hay ninguna clave: se crea la primera.
        }

        if (!hayActiva)
        {
            var primera = (await generadorDeClaves.Generar(reloj.Ahora, cancelacion).ConfigureAwait(false)).Clave;
            await unidad.Ejecutar(async ct => { await almacenDeClaves.Guardar(JuegoDeClavesDeFirma.Iniciar(primera), ct).ConfigureAwait(false); return 0; }, cancelacion).ConfigureAwait(false);
            // El verificador de credenciales guarda una foto de las claves publicadas: se renueva para que la primera credencial emitida ya se verifique.
            await cacheDeVerificacion.Renovar(cancelacion).ConfigureAwait(false);
        }

        var texto = ContrasenaAleatoria();
        var (contrasena, _) = Contrasena.Crear(texto, comunes.EsComun);
        if (contrasena is null)
        {
            throw new InvalidOperationException("La contraseña generada no cumple la política.");
        }

        var ahora = reloj.Ahora;
        var vigentes = await versiones.Obtener(cancelacion).ConfigureAwait(false);
        var correo = $"e2e-{Etiqueta(rol)}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}@example.test";
        var cuenta = new CuentaDeIdentidad
        {
            Usuario = Usuario.Restaurar(Guid.NewGuid(), correo, RolDeUsuarioDe(rol), sucursal, correoVerificado: true, activo: true),
            Nombre = "Cuenta de pruebas E2E",
            ResumenDeAcceso = await hasher.Resumir(contrasena, cancelacion).ConfigureAwait(false),
            Aceptacion = AceptacionDeTextos.Restaurar(
                ahora,
                [(BabelHub.Ecommerce.Domain.Identidad.TextoLegal.Terminos, vigentes.Terminos, ahora), (BabelHub.Ecommerce.Domain.Identidad.TextoLegal.Privacidad, vigentes.Privacidad, ahora), (BabelHub.Ecommerce.Domain.Identidad.TextoLegal.Reembolso, vigentes.Reembolso, ahora)]),
            CreadaEn = ahora,
        };
        if (!await unidad.Ejecutar(async ct => await RetirarAlEncargadoE2eAnterior(sucursal, ct).ConfigureAwait(false) && await cuentas.Insertar(cuenta, ct).ConfigureAwait(false), cancelacion).ConfigureAwait(false))
        {
            throw new InvalidOperationException("No se pudo crear la cuenta de pruebas.");
        }

        var credencial = await Ingresar(correo, texto, ahora, cancelacion).ConfigureAwait(false);
        var iniciada = await sesiones.Iniciar(
            new SolicitudDeInicioDeSesion(host, new IngresoCompletado(credencial.UsuarioId, RolWebDe(rol), credencial.Credencial.Revelar(), credencial.CredencialExpiraEn, credencial.Renovacion.Revelar()), null, false),
            cancelacion).ConfigureAwait(false);
        return iniciada.EsExito
            ? new SesionSembrada(cuenta.Usuario.Id, correo, rol, sucursal, host, iniciada.Valor.ValorDeLaCookie)
            : throw new InvalidOperationException("La sesión web no se pudo abrir.");
    }

    /// <summary>Solo un encargado activo por sucursal (IDE-06): el que sembró una corrida anterior (una cuenta <c>e2e-…@example.test</c>) se retira para que cada recorrido tenga el suyo. Nunca toca a otra cuenta.</summary>
    private async Task<bool> RetirarAlEncargadoE2eAnterior(Guid? sucursal, CancellationToken cancelacion)
    {
        if (sucursal is not { } codigo || await cuentas.EncargadoActivoDe(codigo, cancelacion).ConfigureAwait(false) is not { } anterior)
        {
            return true;
        }

        var previa = await cuentas.PorId(anterior.UsuarioId, paraActualizar: true, cancelacion).ConfigureAwait(false);
        if (previa is null || !previa.Usuario.Correo.StartsWith("e2e-", StringComparison.Ordinal) || !previa.Usuario.Correo.EndsWith("@example.test", StringComparison.Ordinal))
        {
            return true;
        }

        previa.Usuario.Anonimizar($"retirado-{Guid.NewGuid():N}@example.test");
        await cuentas.Guardar(previa, cancelacion).ConfigureAwait(false);
        return true;
    }

    private async Task<CredencialDeSesion> Ingresar(string correo, string contrasena, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        var resultado = await ingreso.Ingresar(new SolicitudDeIngreso(correo, new ValorSecreto(contrasena)), cancelacion).ConfigureAwait(false);
        if (!resultado.EsExito)
        {
            throw new InvalidOperationException("El ingreso de la cuenta sembrada fue rechazado.");
        }

        if (resultado.Valor.Credencial is { } directa)
        {
            return directa;
        }

        // El personal debe configurar su segundo factor: se hace por el camino real, con el secreto que entrega y su código TOTP.
        var desafio = resultado.Valor.Desafio ?? throw new InvalidOperationException("El ingreso no trajo credencial ni desafío.");
        if (desafio.Paso != PasoPendienteDelIngreso.ConfigurarSegundoFactor)
        {
            throw new InvalidOperationException("El ingreso pide un paso que el sembrador no resuelve.");
        }

        var secreto = await ingreso.IniciarConfiguracionDelSegundoFactor(desafio.Identificador, cancelacion).ConfigureAwait(false);
        if (!secreto.EsExito)
        {
            throw new InvalidOperationException("No se pudo iniciar el segundo factor.");
        }

        var codigo = Totp.Codigo(DecodificarBase32(secreto.Valor.SecretoBase32.Revelar()), Totp.PasoDe(ahora));
        var confirmado = await ingreso.ConfirmarElSegundoFactor(desafio.Identificador, new ValorSecreto(codigo), cancelacion).ConfigureAwait(false);
        return confirmado is { EsExito: true, Valor.Credencial: { } credencial }
            ? credencial
            : throw new InvalidOperationException("No se pudo confirmar el segundo factor.");
    }

    private static string ContrasenaAleatoria()
    {
        var texto = new char[24];
        for (var i = 0; i < texto.Length; i++)
        {
            texto[i] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];
        }

        // Una mayúscula, una minúscula, un dígito y un signo siempre presentes: la política no depende de la suerte.
        return new string(texto) + "-7aQ";
    }

    private static byte[] DecodificarBase32(string texto)
    {
        const string alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var acumulador = 0;
        var bits = 0;
        foreach (var c in texto.TrimEnd('=').ToUpperInvariant())
        {
            var valor = alfabeto.IndexOf(c, StringComparison.Ordinal);
            if (valor < 0)
            {
                throw new FormatException("El secreto no es base32.");
            }

            acumulador = (acumulador << 5) | valor;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((acumulador >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. bytes];
    }

    private static string Etiqueta(RolDeCuenta rol) => rol switch
    {
        RolDeCuenta.Cliente => "cliente",
        RolDeCuenta.EncargadoDeSucursal => "encargado",
        RolDeCuenta.Logistica => "logistica",
        _ => "administrador",
    };

    private static RolDeUsuario RolDeUsuarioDe(RolDeCuenta rol) => rol switch
    {
        RolDeCuenta.Cliente => RolDeUsuario.Cliente,
        RolDeCuenta.EncargadoDeSucursal => RolDeUsuario.EncargadoDeSucursal,
        RolDeCuenta.Logistica => RolDeUsuario.Logistica,
        _ => RolDeUsuario.AdministradorCentral,
    };

    private static RolWeb RolWebDe(RolDeCuenta rol) => rol switch
    {
        RolDeCuenta.Cliente => RolWeb.Cliente,
        RolDeCuenta.EncargadoDeSucursal => RolWeb.Encargado,
        RolDeCuenta.Logistica => RolWeb.Logistica,
        _ => RolWeb.AdministradorCentral,
    };
}
#endif
