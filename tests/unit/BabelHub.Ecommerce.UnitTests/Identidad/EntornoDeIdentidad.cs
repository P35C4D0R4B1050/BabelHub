using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using Domain = BabelHub.Ecommerce.Domain;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Web;
using PropositoDeCodigo = BabelHub.Ecommerce.Domain.Identidad.PropositoDeCodigo;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>El entorno común de las pruebas de los casos de uso de Identidad: todos los puertos son dobles en memoria y la unidad de trabajo revierte si el trabajo falla.</summary>
internal sealed class EntornoDeIdentidad
{
    public static readonly DateTimeOffset Inicio = new(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);

    public const string ContrasenaSegura = "una frase larga y segura";

    public BdIdentidad Bd { get; } = new();

    public RelojFijo Reloj { get; } = new(Inicio);

    public HasherDePrueba Hasher { get; } = new();

    public LimitesEnMemoria Limites { get; } = new();

    public PendientesDePrueba Pendientes { get; } = new();

    public CarritoDePrueba Carrito { get; }

    public SeguridadDePrueba Seguridad { get; } = new();

    public CodigosEnMemoria Codigos { get; } = new();

    public SolicitudesDeCodigoDePrueba Solicitudes { get; } = new();

    public ColaDeCodigosDePrueba Cola { get; } = new();

    public EnviadorDeCorreoDePrueba Correo { get; } = new();

    public AlertasTecnicasEspia Alertas { get; } = new();

    public EmisorDeCredencialesDePrueba Emisor { get; } = new();

    public VersionesDePrueba Versiones { get; } = new(new VersionesDeTextos("t1", "p1", "r1"));

    public CalculadorDeHuellaDePrueba Huellas { get; } = new();

    public HuellasDeIdentidadDePrueba HuellasDeIdentidad { get; } = new();

    public GeneradorDeSecretosDePrueba Secretos { get; } = new();

    public CifradorDePrueba Cifrador { get; } = new();

    public SegundoFactorEnMemoria SegundoFactor { get; } = new();

    public UnidadDeTrabajoEnMemoria Unidad { get; }

    public RepositorioDeCuentasEnMemoria Cuentas { get; }

    public RenovacionesEnMemoria Renovaciones { get; }

    public DesafiosEnMemoria Desafios { get; }

    public AuditoriaDePrueba Auditoria { get; }

    public EmisionDeSesion Sesiones { get; }

    public EmisionDeCodigos EmisionDeCodigos { get; }

    public ActorDePrueba Actor { get; } = new();

    public DenegacionesDePrueba Denegaciones { get; } = new();

    public CancelacionDeAvisosDePrueba CancelacionDeAvisos { get; } = new();

    public AvisosDeCuentaEnMemoria AvisosGuardados { get; }

    public DispositivosEnMemoria Dispositivos { get; }

    public CambiosDeCorreoEnMemoria Cambios { get; }

    public SolicitudesDeDatosEnMemoria SolicitudesGuardadas { get; }

    public CancelacionesEnMemoria CancelacionesGuardadas { get; }

    public BajasEnMemoria BajasGuardadas { get; }

    public PurgaDeIdentidadDePrueba Purga { get; }

    public EntregaDeBajasDePrueba EntregaDeBajas { get; } = new();

    public Plataforma.RegistroDeSupresionesEnMemoria Supresiones { get; } = new();

    public List<AplicadorDePrueba> Aplicadores { get; } = [new("pedidos"), new("pagos"), new("devoluciones"), new("biblioteca"), new("reclamaciones"), new("carrito")];

    public EjecutorDeCancelacionDePrueba Ejecutor { get; }

    public AvisosDeCuenta Avisos { get; }

    public RevocacionDeSesiones Revocacion { get; }

    public AlertasDeAuditoriaDePrueba AlertasDeSeguridad { get; } = new();

    public EventosDeSeguridadDeIdentidad Eventos { get; }

    public ProcesarSolicitudDeCodigo SegundoPlano { get; }

    public EntornoDeIdentidad()
    {
        Unidad = new UnidadDeTrabajoEnMemoria(Bd);
        Carrito = new CarritoDePrueba(Bd);
        Cuentas = new RepositorioDeCuentasEnMemoria(Bd);
        AvisosGuardados = new AvisosDeCuentaEnMemoria(Bd);
        Dispositivos = new DispositivosEnMemoria(Bd);
        Cambios = new CambiosDeCorreoEnMemoria(Bd);
        SolicitudesGuardadas = new SolicitudesDeDatosEnMemoria(Bd);
        CancelacionesGuardadas = new CancelacionesEnMemoria(Bd);
        BajasGuardadas = new BajasEnMemoria(Bd);
        Purga = new PurgaDeIdentidadDePrueba(Bd);
        Ejecutor = new EjecutorDeCancelacionDePrueba(Reloj);
        Renovaciones = new RenovacionesEnMemoria(Bd);
        Desafios = new DesafiosEnMemoria(Bd);
        Auditoria = new AuditoriaDePrueba(Bd);
        Sesiones = new EmisionDeSesion(Renovaciones, Emisor, Secretos, HuellasDeIdentidad);
        EmisionDeCodigos = new EmisionDeCodigos(Codigos, HuellasDeIdentidad, Correo, Alertas, Reloj);
        Eventos = new EventosDeSeguridadDeIdentidad(Seguridad);
        Avisos = new AvisosDeCuenta(Correo);
        Revocacion = new RevocacionDeSesiones(Cuentas, Renovaciones, Reloj);
        SegundoPlano = new ProcesarSolicitudDeCodigo(Cuentas, EmisionDeCodigos, Eventos);
    }

    public RegistroDeCuentas Registro() => new(
        Cuentas, Hasher, new ListaDeContrasenasDePrueba(), Versiones, Limites, Huellas, Unidad, Pendientes, Carrito, Renovaciones, SegundoFactor, Auditoria, Codigos, HuellasDeIdentidad, Solicitudes, Cola, Sesiones, Eventos, Alertas, Secretos, Reloj);

    public SolicitudDeAltaDeCuenta Alta(string correo = "ana@example.com", string nombre = "Ana", string frase = ContrasenaSegura, string? terminos = "t1", string? privacidad = "p1", bool mayor = true) =>
        new(nombre, correo, new ValorSecreto(frase), terminos, privacidad, mayor);

    /// <summary>Siembra una cuenta ya guardada con la contraseña dada (su resumen), para probar ingreso, renovación y recuperación.</summary>
    public CuentaDeIdentidad Cuenta(
        string correo = "ana@example.com",
        RolDeUsuario rol = RolDeUsuario.Cliente,
        bool verificada = true,
        bool activa = true,
        string frase = ContrasenaSegura,
        Guid? sucursal = null)
    {
        var usuario = Usuario.Restaurar(Guid.NewGuid(), correo, rol, rol == RolDeUsuario.EncargadoDeSucursal ? sucursal ?? Guid.NewGuid() : null, verificada, activa);
        var cuenta = new CuentaDeIdentidad
        {
            Usuario = usuario,
            Nombre = "Persona",
            ResumenDeAcceso = HasherDePrueba.ResumenDe(PoliticaDeContrasenas.Normalizar(frase)),
            Aceptacion = AceptacionDeTextos.EnElRegistro(Versiones.Vigentes, "t1", "p1", mayorDeEdad: true, Inicio).Aceptacion!,
            CreadaEn = Inicio.AddDays(-30),
        };
        Bd.Cuentas[usuario.Id] = cuenta;
        return cuenta;
    }

    /// <summary>Corre lo que el trabajo en segundo plano haría con lo encolado (la cola en memoria de los códigos).</summary>
    public async Task ProcesarLaCola()
    {
        foreach (var solicitud in Cola.Encoladas.ToList())
        {
            await SegundoPlano.Ejecutar(solicitud);
        }

        Cola.Encoladas.Clear();
    }

    /// <summary>El último código enviado por correo (el que la persona escribiría).</summary>
    public string UltimoCodigoEnviado() => Correo.Codigos[^1].Codigo.Revelar();
}

internal static class EntornoDeIdentidadExtensiones
{
    public static Application.Identidad.Interno.Sesiones Gestor(this EntornoDeIdentidad e) => new(e.Cuentas, e.Renovaciones, e.HuellasDeIdentidad, e.Secretos, e.Sesiones, e.Revocacion, e.Unidad, e.Eventos, e.AlertasDeSeguridad, e.Actor, e.Denegaciones, e.Reloj);

    public static RecuperacionDeAcceso Recuperacion(this EntornoDeIdentidad e) => new(e.Cuentas, e.Hasher, new ListaDeContrasenasDePrueba(), e.Limites, e.Huellas, e.Solicitudes, e.Cola, e.Codigos, e.HuellasDeIdentidad, e.Revocacion, e.Unidad, e.Auditoria, e.Eventos, e.Avisos, e.Alertas, e.Reloj);

    public static Ingreso Ingreso(this EntornoDeIdentidad e) => new(
        e.Cuentas, e.Hasher, new ListaDeContrasenasDePrueba(), e.Limites, e.Huellas, e.Desafios, e.HuellasDeIdentidad, e.Secretos, e.SegundoFactor, e.Cifrador, e.Sesiones, e.Renovaciones, e.Unidad, e.Auditoria, e.Eventos, e.Avisos, e.Reloj);

    /// <summary>Deja activo el segundo factor de una cuenta con un secreto conocido y 8 códigos de respaldo; devuelve el secreto y los códigos en claro.</summary>
    public static (byte[] Secreto, IReadOnlyList<string> Respaldo) ConSegundoFactor(this EntornoDeIdentidad e, CuentaDeIdentidad cuenta)
    {
        var secreto = e.Secretos.SecretoDelSegundoFactor();
        var respaldo = CodigosDeRespaldo.Generar();
        e.SegundoFactor.Sembrar(cuenta.Usuario.Id, secreto, respaldo.Select(c => e.HuellasDeIdentidad.DeCodigoDeRespaldo(cuenta.Usuario.Id, CodigosDeRespaldo.Canonico(c))), e.Cifrador);
        return (secreto, respaldo);
    }

    /// <summary>El código TOTP que mostraría la aplicación de autenticación en el instante del reloj del entorno (con un desplazamiento opcional de pasos).</summary>
    public static string Totp(this EntornoDeIdentidad e, byte[] secreto, int pasos = 0) =>
        Domain.Identidad.Totp.Codigo(secreto, Domain.Identidad.Totp.PasoDe(e.Reloj.Ahora) + pasos);
}

internal static class EntornoDeLaCuentaExtensiones
{
    public static Reautenticacion Reautenticacion(this EntornoDeIdentidad e) =>
        new(e.Hasher, e.Limites, e.Huellas, e.SegundoFactor, e.Cifrador, e.HuellasDeIdentidad, e.Eventos, e.Reloj);

    public static GestionDeLaCuenta Gestion(this EntornoDeIdentidad e) => new(
        e.Cuentas,
        new AlcanceDeLaCuenta(e.Actor, e.Denegaciones),
        e.Confianza(),
        e.Reautenticacion(),
        e.Limites,
        new ListaDeContrasenasDePrueba(),
        e.Hasher,
        e.Secretos,
        e.HuellasDeIdentidad,
        e.Cifrador,
        e.SegundoFactor,
        e.Renovaciones,
        e.Revocacion,
        e.AvisosGuardados,
        e.CancelacionDeAvisos,
        e.Avisos,
        e.Unidad,
        e.Auditoria,
        e.Reloj);

    /// <summary>Hace que el actor de la prueba sea el cliente dado (la persona autenticada).</summary>
    public static void ActuarComo(this EntornoDeIdentidad e, CuentaDeIdentidad cuenta) =>
        e.Actor.Actual = ActorActual.DeUsuario(cuenta.Usuario.Id, "cliente");
}

internal static class EntornoDeLaAdministracionExtensiones
{
    public static AdministracionDeUsuarios Administracion(this EntornoDeIdentidad e) => new(
        e.Cuentas,
        new AlcanceDeLaCuenta(e.Actor, e.Denegaciones),
        e.Hasher,
        e.Secretos,
        e.SegundoFactor,
        e.Revocacion,
        e.CancelacionDeAvisos,
        e.Avisos,
        e.Unidad,
        e.Auditoria,
        e.Reloj);

    /// <summary>Siembra un administrador central activo y hace que sea el actor de la prueba.</summary>
    public static CuentaDeIdentidad ActuarComoAdministrador(this EntornoDeIdentidad e, string correo = "admin@example.com")
    {
        var admin = e.Cuenta(correo, RolDeUsuario.AdministradorCentral);
        e.Actor.Actual = ActorActual.DeUsuario(admin.Usuario.Id, "administrador-central");
        return admin;
    }
}

internal static class EntornoDelCambioDeCorreoExtensiones
{
    public static ConfianzaDelDispositivo Confianza(this EntornoDeIdentidad e) => new(e.Dispositivos, e.Cambios, e.HuellasDeIdentidad, e.Reloj);

    public static CambioDeCorreo CambioDeCorreo(this EntornoDeIdentidad e) => new(
        e.Cuentas,
        new AlcanceDeLaCuenta(e.Actor, e.Denegaciones),
        e.Reautenticacion(),
        e.Confianza(),
        e.Dispositivos,
        e.Cambios,
        e.EmisionDeCodigos,
        e.Codigos,
        e.HuellasDeIdentidad,
        e.Secretos,
        e.Limites,
        e.Huellas,
        e.AvisosGuardados,
        e.Avisos,
        e.Eventos,
        e.Unidad,
        e.Auditoria,
        e.Reloj);

    public static DispositivosDeLaCuenta DispositivosDeLaCuenta(this EntornoDeIdentidad e) =>
        new(new AlcanceDeLaCuenta(e.Actor, e.Denegaciones), e.Dispositivos, e.HuellasDeIdentidad, e.Secretos, e.Unidad, e.Reloj);

    /// <summary>Siembra un dispositivo ya reconocido (con la antigüedad que exige el Dominio) y devuelve su marca en claro.</summary>
    public static ValorSecreto DispositivoReconocido(this EntornoDeIdentidad e, CuentaDeIdentidad cuenta, string region = "PE", int diasDeAntiguedad = 5)
    {
        var marca = e.Secretos.ValorOpaco();
        e.Bd.Dispositivos.Add(Dispositivo.Emitir(cuenta.Usuario.Id, e.HuellasDeIdentidad.DeValorOpaco(marca), region, e.Reloj.Ahora.AddDays(-diasDeAntiguedad)));
        return new ValorSecreto(marca);
    }
}

internal static class EntornoDeLasSolicitudesExtensiones
{
    public static AtencionDeSolicitudesDeDatos Solicitudes(this EntornoDeIdentidad e) => new(
        e.Cuentas,
        new AlcanceDeLaCuenta(e.Actor, e.Denegaciones),
        new AlcanceDeLaAdministracion(new AlcanceDeLaCuenta(e.Actor, e.Denegaciones), e.Cuentas),
        e.SolicitudesGuardadas,
        e.AvisosGuardados,
        e.Avisos,
        e.Secretos,
        e.Unidad,
        e.Auditoria,
        e.Reloj);
}

internal static class EntornoDeLaCancelacionExtensiones
{
    public static CancelacionDeCuenta Cancelacion(this EntornoDeIdentidad e) => new(
        e.Cuentas,
        new AlcanceDeLaCuenta(e.Actor, e.Denegaciones),
        e.Reautenticacion(),
        e.CancelacionesGuardadas,
        e.Pendientes,
        e.Ejecutor,
        e.AvisosGuardados,
        e.Avisos,
        e.Secretos,
        e.Unidad,
        e.Auditoria,
        e.Reloj);

    public static RevisionDeLasCancelaciones Revision(this EntornoDeIdentidad e) => new(
        e.Cuentas,
        e.CancelacionesGuardadas,
        e.Pendientes,
        e.AvisosGuardados,
        e.Avisos,
        e.Unidad,
        e.Auditoria,
        e.Reloj);
}

internal static class EntornoDeLaEjecucionExtensiones
{
    public static EjecutorDeLaCancelacion EjecutorReal(this EntornoDeIdentidad e) => new(
        e.Supresiones,
        e.Aplicadores,
        e.Purga,
        e.CancelacionDeAvisos,
        e.Cuentas,
        e.Revocacion,
        e.CancelacionesGuardadas,
        e.BajasGuardadas,
        e.Secretos,
        e.Auditoria,
        e.Reloj);

    /// <summary>La cancelación con el ejecutor <b>real</b> (el de las pruebas anteriores es un doble).</summary>
    public static CancelacionDeCuenta CancelacionReal(this EntornoDeIdentidad e) => new(
        e.Cuentas,
        new AlcanceDeLaCuenta(e.Actor, e.Denegaciones),
        e.Reautenticacion(),
        e.CancelacionesGuardadas,
        e.Pendientes,
        e.EjecutorReal(),
        e.AvisosGuardados,
        e.Avisos,
        e.Secretos,
        e.Unidad,
        e.Auditoria,
        e.Reloj);

    public static ReintentoDeBajas Reintento(this EntornoDeIdentidad e) => new(e.BajasGuardadas, e.Supresiones, e.EntregaDeBajas, e.AlertasDeSeguridad, e.Reloj);
}
