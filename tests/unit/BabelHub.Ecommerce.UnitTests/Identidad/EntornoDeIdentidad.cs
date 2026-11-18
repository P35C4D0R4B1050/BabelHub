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
        Renovaciones = new RenovacionesEnMemoria(Bd);
        Desafios = new DesafiosEnMemoria(Bd);
        Auditoria = new AuditoriaDePrueba(Bd);
        Sesiones = new EmisionDeSesion(Renovaciones, Emisor, Secretos, HuellasDeIdentidad);
        EmisionDeCodigos = new EmisionDeCodigos(Codigos, HuellasDeIdentidad, Correo, Alertas, Reloj);
        Eventos = new EventosDeSeguridadDeIdentidad(Seguridad);
        Avisos = new AvisosDeCuenta(Correo);
        Revocacion = new RevocacionDeSesiones(Cuentas, Renovaciones, Reloj);
        SegundoPlano = new ProcesarSolicitudDeCodigo(Cuentas, EmisionDeCodigos);
    }

    public RegistroDeCuentas Registro() => new(
        Cuentas, Hasher, new ListaDeContrasenasDePrueba(), Versiones, Limites, Huellas, Unidad, Pendientes, Carrito, Renovaciones, Auditoria, Codigos, HuellasDeIdentidad, Solicitudes, Cola, Sesiones, Eventos, Alertas, Secretos, Reloj);

    public SolicitudDeAltaDeCuenta Alta(string correo = "ana@example.com", string nombre = "Ana", string contrasena = ContrasenaSegura, string? terminos = "t1", string? privacidad = "p1", bool mayor = true) =>
        new(nombre, correo, new ValorSecreto(contrasena), terminos, privacidad, mayor);

    /// <summary>Siembra una cuenta ya guardada con la contraseña dada (su resumen), para probar ingreso, renovación y recuperación.</summary>
    public CuentaDeIdentidad Cuenta(
        string correo = "ana@example.com",
        RolDeUsuario rol = RolDeUsuario.Cliente,
        bool verificada = true,
        bool activa = true,
        string contrasena = ContrasenaSegura,
        Guid? sucursal = null)
    {
        var usuario = Usuario.Restaurar(Guid.NewGuid(), correo, rol, rol == RolDeUsuario.EncargadoDeSucursal ? sucursal ?? Guid.NewGuid() : null, verificada, activa);
        var cuenta = new CuentaDeIdentidad
        {
            Usuario = usuario,
            Nombre = "Persona",
            ResumenDeContrasena = HasherDePrueba.ResumenDe(PoliticaDeContrasenas.Normalizar(contrasena)),
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
    public static Application.Identidad.Interno.Sesiones Gestor(this EntornoDeIdentidad e) => new(e.Cuentas, e.Renovaciones, e.HuellasDeIdentidad, e.Secretos, e.Sesiones, e.Revocacion, e.Unidad, e.Eventos, e.AlertasDeSeguridad, e.Reloj);

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
