using System.Security.Cryptography;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Infrastructure.Identidad;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>
/// Las claves del entorno de Identidad (identidad-seguridad T42 reducido, SEG-11, D04): la <b>clave de cifrado</b> de los secretos (segundo factor, clave privada de firma; una por propósito derivada con HKDF) y la
/// <b>pimienta</b> de las huellas (códigos, renovaciones, dispositivos). Vienen de variables <c>BABELHUB__Identidad__…</c> con el formato <c>versión:base64</c> de al menos 32 bytes, nunca del repositorio. <b>En
/// producción falta una o es débil → el arranque falla</b> nombrando la variable, sin imprimir ningún valor; fuera de producción, sin variable se generan claves efímeras de desarrollo (cada arranque, otras).
/// </summary>
public sealed class OpcionesDeIdentidad
{
    public const string VariableDeLaClaveDeCifrado = "Identidad:ClaveDeCifradoDeClaves";
    public const string VariableDeLasClavesAnteriores = "Identidad:ClavesDeCifradoAnteriores";
    public const string VariableDeLaPimienta = "Identidad:Pimienta";

    public OpcionesDeIdentidad(ClaveVersionadaDeIdentidad claveDeCifrado, ClaveVersionadaDeIdentidad pimienta, IReadOnlyList<ClaveVersionadaDeIdentidad>? clavesAnteriores = null)
    {
        ArgumentNullException.ThrowIfNull(claveDeCifrado);
        ArgumentNullException.ThrowIfNull(pimienta);
        ClaveDeCifrado = claveDeCifrado;
        Pimienta = pimienta;
        ClavesAnteriores = clavesAnteriores ?? [];
    }

    public ClaveVersionadaDeIdentidad ClaveDeCifrado { get; }

    public ClaveVersionadaDeIdentidad Pimienta { get; }

    public IReadOnlyList<ClaveVersionadaDeIdentidad> ClavesAnteriores { get; }

    public override string ToString() => "[opciones de identidad]";

    public static OpcionesDeIdentidad Leer(IConfiguration configuracion, bool esProduccion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var cifrado = Clave(configuracion, VariableDeLaClaveDeCifrado, esProduccion);
        var pimienta = Clave(configuracion, VariableDeLaPimienta, esProduccion);
        if (CryptographicOperations.FixedTimeEquals(cifrado.Clave, pimienta.Clave))
        {
            throw new ArgumentException($"«{VariableDeLaClaveDeCifrado}» y «{VariableDeLaPimienta}» deben ser claves distintas.");
        }

        var anteriores = (configuracion[VariableDeLasClavesAnteriores] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select((valor, i) => ClaveVersionadaDeIdentidad.Analizar(valor, $"{VariableDeLasClavesAnteriores}[{i}]"))
            .ToList();
        return new OpcionesDeIdentidad(cifrado, pimienta, anteriores);
    }

    private static ClaveVersionadaDeIdentidad Clave(IConfiguration configuracion, string nombre, bool esProduccion)
    {
        var valor = configuracion[nombre];
        if (string.IsNullOrWhiteSpace(valor))
        {
            return esProduccion
                ? throw new InvalidOperationException($"Falta la variable de entorno de Identidad «{nombre}»: en producción el arranque no sigue sin ella (SEG-11).")
                : Efimera();
        }

        return ClaveVersionadaDeIdentidad.Analizar(valor, nombre);
    }

    /// <summary>Una clave de desarrollo, distinta en cada arranque y nunca igual entre dos usos; en producción no se llama.</summary>
    private static ClaveVersionadaDeIdentidad Efimera() =>
        ClaveVersionadaDeIdentidad.Analizar($"dev:{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}", nameof(Efimera));
}

/// <summary>
/// La composición real del módulo Identidad en el E-commerce (identidad-seguridad T42 reducido por la regla 9 y web-base §1: las pantallas llaman a estos contratos <b>en proceso</b>, no por HTTP): criptografía
/// (Argon2id, AES-GCM, HMAC, ES256), repositorios de PostgreSQL bajo <c>rol_identidad</c>, los casos de uso con su contrato público, la verificación de credenciales (la caché de claves, el verificador de la web y
/// el firmador del contexto de lectura), el canal en memoria de códigos y los puertos que la capa web espera de Identidad (que <b>reemplazan</b> a los provisionales aunque ya estén registrados). Necesita ya
/// registrados lo que es de otros módulos y de Plataforma: <see cref="IUnidadDeTrabajo"/>, <see cref="IConexionDeLaUnidadDeTrabajo"/>, <see cref="IActorActual"/>, <see cref="IReloj"/>, <see cref="IControlDeLimites"/>,
/// <see cref="ICalculadorDeHuella"/>, <see cref="IEnviadorDeCorreo"/>, <see cref="IClienteDelInventario"/>, <see cref="IRegistroDeSupresiones"/>, <see cref="IDenegacionDeAcceso"/>, <see cref="IAlertas"/>,
/// <see cref="IPublicadorDeAlertasTecnicas"/>, <see cref="IRegistroDeSeguridad"/>, <see cref="IAuditoria"/>, <see cref="ICarritoDeCuentaReemplazada"/> (Carrito) e <see cref="ICancelacionDeAvisos"/> (Notificaciones); los demás módulos aportan
/// sus <see cref="IPendientesDeCuenta"/> y <see cref="IAplicadorDeCancelacion"/> como enumeración, y Identidad suma los del Inventario.
/// </summary>
public static class ComposicionDeIdentidad
{
    public static IServiceCollection AgregarIdentidad(
        this IServiceCollection servicios,
        OpcionesDeIdentidad opciones,
        bool soloEnModoNormal,
        Func<IServiceProvider, AlcanceDeSistemaDeIdentidad> alcanceDeSistema,
        Func<IServiceProvider, NpgsqlDataSource> depuracionDeRegistros)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(opciones);
        ArgumentNullException.ThrowIfNull(alcanceDeSistema);
        ArgumentNullException.ThrowIfNull(depuracionDeRegistros);

        // ---------- Criptografía (singletons: sin estado por solicitud; las claves salen del entorno) ----------
        servicios.TryAddSingleton(new AesGcmCifradorDeSecretos(opciones.ClaveDeCifrado, opciones.ClavesAnteriores));
        servicios.TryAddSingleton<ICifradorDeSecretos>(sp => sp.GetRequiredService<AesGcmCifradorDeSecretos>());
        servicios.TryAddSingleton<IHuellasDeIdentidad>(new HmacHuellasDeIdentidad(opciones.Pimienta));
        servicios.TryAddSingleton<IGeneradorDeSecretos, CsprngGeneradorDeSecretos>();
        servicios.TryAddSingleton<IHasherDeContrasenas, Argon2idHasherDeContrasenas>();
        servicios.TryAddSingleton<IListaDeContrasenasComunes, RecursoListaDeContrasenasComunes>();
        servicios.TryAddSingleton<CanalDeCodigosEnMemoria>();
        servicios.TryAddSingleton<IColaDeCodigos>(sp => sp.GetRequiredService<CanalDeCodigosEnMemoria>());

        // ---------- Repositorios (por solicitud: usan la transacción en curso) ----------
        servicios.TryAddScoped<AccesoAlEsquemaDeIdentidad>();
        servicios.TryAddScoped<IRepositorioDeCuentas, PostgresRepositorioDeCuentas>();
        servicios.TryAddScoped<IVersionesDeTextosVigentes, PostgresVersionesDeTextosVigentes>();
        servicios.TryAddScoped<ICodigosDeUnSoloUso, PostgresCodigosDeUnSoloUso>();
        servicios.TryAddScoped<IRegistroDeSolicitudesDeCodigo, PostgresRegistroDeSolicitudesDeCodigo>();
        servicios.TryAddScoped<IAvisosDeCuentaAlmacen, PostgresAvisosDeCuentaAlmacen>();
        servicios.TryAddScoped<IRepositorioDeDispositivos, PostgresRepositorioDeDispositivos>();
        servicios.TryAddScoped<IRepositorioDeCambiosDeCorreo, PostgresRepositorioDeCambiosDeCorreo>();
        servicios.TryAddScoped<IRepositorioDeSolicitudesDeDatos, PostgresRepositorioDeSolicitudesDeDatos>();
        servicios.TryAddScoped<IRepositorioDeCancelaciones, PostgresRepositorioDeCancelaciones>();
        servicios.TryAddScoped<IRepositorioDeBajas, PostgresRepositorioDeBajas>();
        servicios.TryAddScoped<IPorAnonimizar, PostgresPorAnonimizar>();
        servicios.TryAddScoped<IPurgaDeIdentidad, PostgresPurgaDeIdentidad>();
        servicios.TryAddScoped<IPurgaDeVencidos, PostgresPurgaDeVencidos>();
        servicios.TryAddScoped<IRepositorioDeRenovaciones, PostgresRepositorioDeRenovaciones>();
        servicios.TryAddScoped<IRepositorioDeDesafios, PostgresRepositorioDeDesafios>();
        servicios.TryAddScoped<ISegundoFactorAlmacen, PostgresSegundoFactorAlmacen>();
        servicios.TryAddScoped<PostgresAlmacenDeClavesDeFirma>();
        servicios.TryAddScoped<IAlmacenDeClavesDeFirma>(sp => sp.GetRequiredService<PostgresAlmacenDeClavesDeFirma>());
        servicios.TryAddScoped<IAlmacenDeClavesPrivadas>(sp => sp.GetRequiredService<PostgresAlmacenDeClavesDeFirma>());
        servicios.TryAddScoped<IClavePrivadaDeFirma, PostgresClavePrivadaDeFirma>();

        // ---------- Credenciales (ES256): emisor y generador de claves por solicitud; verificación con la caché de claves en proceso ----------
        servicios.TryAddScoped<IEmisorDeCredenciales, Es256EmisorDeCredenciales>();
        servicios.TryAddScoped<IGeneradorDeClavesDeFirma, Es256GeneradorDeClavesDeFirma>();
        servicios.TryAddSingleton(sp => new CacheDeClavesDeVerificacion(new ClavesPublicadasPorAlcance(sp.GetRequiredService<IServiceScopeFactory>())));
        servicios.TryAddSingleton<IFuenteDeClavesDeVerificacion>(sp => sp.GetRequiredService<CacheDeClavesDeVerificacion>());
        servicios.TryAddSingleton<Es256VerificadorDeCredenciales>(sp => new Es256VerificadorDeCredenciales(sp.GetRequiredService<IFuenteDeClavesDeVerificacion>()));
        servicios.TryAddSingleton<IVerificadorDeCredencial, VerificadorDeCredencialDeLaWeb>();
        servicios.TryAddScoped<IFirmaDeContextosDeLectura>(sp => new Es256FirmaDeContextosDeLectura(sp.GetRequiredService<IClavePrivadaDeFirma>(), sp.GetRequiredService<IFuenteDeClavesDeVerificacion>()));

        // ---------- Adaptadores sobre el Inventario ----------
        servicios.TryAddScoped<IEntregaDeBajas, InventarioEntregaDeBajas>();
        servicios.TryAddEnumerable(ServiceDescriptor.Scoped<IPendientesDeCuenta, InventarioPendientesDeCuenta>());

        // ---------- Aplicación: servicios comunes ----------
        servicios.TryAddScoped<EventosDeSeguridadDeIdentidad>();
        servicios.TryAddScoped<AlcanceDeLaCuenta>();
        servicios.TryAddScoped<AlcanceDeLaAdministracion>();
        servicios.TryAddScoped<RevocacionDeSesiones>();
        servicios.TryAddScoped<EmisionDeSesion>();
        servicios.TryAddScoped<EmisionDeCodigos>();
        servicios.TryAddScoped<AvisosDeCuenta>();
        servicios.TryAddScoped<Reautenticacion>();
        servicios.TryAddScoped<ConfianzaDelDispositivo>();
        servicios.TryAddScoped<ProcesarSolicitudDeCodigo>();
        servicios.TryAddScoped<ReintentoDeBajas>();
        servicios.TryAddScoped<RevisionDeLasCancelaciones>();
        servicios.TryAddScoped<PurgarVencidosDeIdentidad>();

        // Los pendientes de la cuenta suman todos los módulos y el Inventario; el compuesto se construye por fábrica con la lista explícita para no recursar (nunca se registra como IPendientesDeCuenta).
        servicios.TryAddScoped(sp => new PendientesDeCuentaCompuestos([.. sp.GetServices<IPendientesDeCuenta>().Where(m => m is not PendientesDeCuentaCompuestos)]));
        servicios.TryAddScoped(sp => ActivatorUtilities.CreateInstance<EjecutorDeLaCancelacion>(sp));
        servicios.TryAddScoped<IEjecutorDeLaCancelacion>(sp => sp.GetRequiredService<EjecutorDeLaCancelacion>());

        // ---------- Aplicación: el contrato público (las pantallas lo llaman en proceso) ----------
        servicios.TryAddScoped<IRegistroDeCuentas>(sp => ConPendientes<RegistroDeCuentas>(sp));
        servicios.TryAddScoped<IIngreso, Ingreso>();
        servicios.TryAddScoped<ISesiones, Sesiones>();
        servicios.TryAddScoped<IRecuperacionDeAcceso, RecuperacionDeAcceso>();
        // Reemplaza al doble provisional de Carrito (TryAddSingleton) aunque ya esté registrado: la implementación real nunca se ignora en silencio.
        servicios.Replace(ServiceDescriptor.Scoped<IEstadoDeCuenta, EstadoDeCuentaDeIdentidad>());
        servicios.TryAddScoped<ICuentaDelCliente, GestionDeLaCuenta>();
        servicios.TryAddScoped<ICambioDeCorreo, CambioDeCorreo>();
        servicios.TryAddScoped<IDispositivosDeLaCuenta, DispositivosDeLaCuenta>();
        servicios.TryAddScoped<ISolicitudesDeDatos, AtencionDeSolicitudesDeDatos>();
        servicios.TryAddScoped<CancelacionDeCuenta>(sp => ConPendientes<CancelacionDeCuenta>(sp));
        servicios.TryAddScoped<ICancelacionDeCuenta>(sp => sp.GetRequiredService<CancelacionDeCuenta>());
        servicios.TryAddScoped<IMarcaDeCancelacionDeCuenta>(sp => sp.GetRequiredService<CancelacionDeCuenta>());
        servicios.TryAddScoped<IAdministracionDeUsuarios, AdministracionDeUsuarios>();
        servicios.TryAddScoped<ClavesDeFirma>();
        servicios.TryAddScoped<IClavesDeFirma>(sp => sp.GetRequiredService<ClavesDeFirma>());
        servicios.TryAddScoped<IContextoDeLectura, ContextoDeLecturaDelAsistente>();

        // ---------- Puertos que Identidad implementa para otros módulos (los de Auditoría, aparte: solo Identidad y Auditoría pueden depender de ellos) ----------
        servicios.AgregarPuertosDeAuditoriaDeIdentidad(depuracionDeRegistros);
        servicios.TryAddScoped<IDestinatariosDeAlertas, DestinatariosDeAlertasDeIdentidad>();
        servicios.TryAddScoped<IAplicadorDeSupresion, AplicadorDeSupresionDeIdentidad>();
        servicios.TryAddScoped<IRestauracionDeIdentidad, RestauracionDeIdentidad>();
        servicios.TryAddScoped<IDireccionesDeCuentas, DireccionesDeCuentasDeIdentidad>();
        servicios.TryAddEnumerable(ServiceDescriptor.Scoped<IPasoDeRestauracion, PasoDeRestauracionRotarClaves>());
        servicios.TryAddEnumerable(ServiceDescriptor.Scoped<IPasoDeRestauracion, PasoDeRestauracionReenviarBajas>());

        // ---------- Lo que la capa web espera de Identidad: reemplaza a lo provisional aunque ya estuviera registrado ----------
        servicios.Replace(ServiceDescriptor.Scoped<IEstadoDeCuentaDeLaWeb, EstadoDeCuentaDeLaWebSobreIdentidad>());
        servicios.Replace(ServiceDescriptor.Scoped<IAceptacionDeTextosEnIdentidad, AceptacionDeTextosSobreIdentidad>());
        servicios.Replace(ServiceDescriptor.Scoped<ISesionesDeIdentidad, SesionesDeIdentidadParaLaWeb>());
        servicios.Replace(ServiceDescriptor.Scoped<IDesafioDelIngreso, DesafioDelIngresoSobreIdentidad>());
        servicios.TryAddScoped<IContextoDelAsistente, ContextoDelAsistenteDeLaWeb>();

        // ---------- Códigos en segundo plano: solo en el modo normal ----------
        if (soloEnModoNormal)
        {
            servicios.AddHostedService(sp => new ServicioDeCodigosEnSegundoPlano(
                sp.GetRequiredService<CanalDeCodigosEnMemoria>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                alcanceDeSistema(sp)));
        }

        return servicios;
    }

    private static T ConPendientes<T>(IServiceProvider sp)
        where T : class =>
        ActivatorUtilities.CreateInstance<T>(sp, (IPendientesDeCuenta)sp.GetRequiredService<PendientesDeCuentaCompuestos>());
}

/// <summary>
/// <see cref="IClavesDeFirma"/> para la caché de claves, que vive más que una solicitud: cada lectura se hace en su propio alcance, con el contrato real. Solo lee lo publicado; rotar o declarar una clave comprometida
/// no son operaciones de la caché y lanzan.
/// </summary>
internal sealed class ClavesPublicadasPorAlcance(IServiceScopeFactory fabrica) : IClavesDeFirma
{
    public async Task<IReadOnlyList<ClavePublicaDeFirma>> Publicadas(CancellationToken cancelacion = default)
    {
        using var alcance = fabrica.CreateScope();
        return await alcance.ServiceProvider.GetRequiredService<IClavesDeFirma>().Publicadas(cancelacion).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> Revocadas(CancellationToken cancelacion = default)
    {
        using var alcance = fabrica.CreateScope();
        return await alcance.ServiceProvider.GetRequiredService<IClavesDeFirma>().Revocadas(cancelacion).ConfigureAwait(false);
    }

    public Task<Resultado<ClavePublicaDeFirma>> Rotar(Guid administradorId, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<ClavePublicaDeFirma>> DeclararComprometida(Guid administradorId, string kid, CancellationToken cancelacion = default) => throw new NotSupportedException();
}
