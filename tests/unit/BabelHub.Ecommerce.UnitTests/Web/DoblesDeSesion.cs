using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;
using BabelHub.Ecommerce.UnitTests.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>Los dobles de una sesión web en un instante controlado y las operaciones de apoyo de las pruebas de T05 a T08.</summary>
internal sealed class DoblesDeSesion
{
    public static readonly DateTimeOffset Inicio = new(2026, 11, 14, 15, 0, 0, TimeSpan.FromHours(-5));
    public static readonly Guid Usuario = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly ActorVerificado ActorCliente = new(Usuario, RolWeb.Cliente, null);

    public RelojFijo Reloj { get; } = new(Inicio);

    public AlmacenDeSesionesEnMemoria Almacen { get; } = new();

    public CifradorDePrueba Cifrador { get; } = new();

    public SesionesDeIdentidadFalsas Identidad { get; } = new();

    public VerificadorFalso Verificador { get; } = new();

    public RegistroDeSeguridadEspia Seguridad { get; } = new();

    public AlertasTecnicasEspia Alertas { get; } = new();

    public UnidadDeTrabajoSimple Unidad { get; } = new();

    public AleatoriosDePrueba Aleatorios { get; } = new();

    public ServiciosDeSesion Comun => new(Almacen, Cifrador, Identidad, Seguridad, Reloj);

    public DoblesDeSesion()
    {
        // Por defecto, una credencial que empieza por «jwt-ok» es válida para el cliente de las pruebas.
        Verificador.Regla = (texto, _) => texto.StartsWith("jwt-ok", StringComparison.Ordinal)
            ? new VerificacionDeCredencial(EstadoDeVerificacion.Valida, ActorCliente)
            : new VerificacionDeCredencial(EstadoDeVerificacion.Invalida, null);
    }

    public IniciarSesionWeb Iniciar => new(Comun, Unidad, new AreasDeInterfaz(), Aleatorios);

    public CargarSesionWeb Cargar => new(Comun, Unidad);

    public AsegurarCredencialDeSesion Asegurar => new(Comun, Unidad, Verificador, Alertas);

    public CerrarSesionWeb Cerrar => new(Comun, Unidad, Alertas);

    public AlmacenDeBorradoresEnMemoria Borradores { get; } = new();

    public PurgarSesionesWeb Purgar => PurgarDe(HostDeSesion.Tienda);

    public PurgarSesionesWeb PurgarDe(HostDeSesion host) => new(host, Almacen, Borradores, Cifrador, Identidad, Seguridad, Alertas, Unidad, Reloj);

    public static IngresoCompletado Ingreso(RolWeb rol = RolWeb.Cliente, string credencial = "jwt-ok-1", string renovacion = "renov-1", int vence = 15) =>
        new(Usuario, rol, credencial, Inicio.AddMinutes(vence), renovacion);

    /// <summary>Abre una sesión por el camino real y devuelve el valor de su cookie.</summary>
    public async Task<string> Abrir(HostWeb host = HostWeb.Tienda, RolWeb rol = RolWeb.Cliente, string credencial = "jwt-ok-1", string renovacion = "renov-1", int vence = 15)
    {
        var resultado = await Iniciar.Ejecutar(new SolicitudDeInicioDeSesion(host, Ingreso(rol, credencial, renovacion, vence), null, true), default);
        return resultado.Valor.ValorDeLaCookie;
    }

    public SesionGuardada Fila(string cookie) => Almacen.Filas[ValorDeCookieDeSesion.Hash(cookie).Valor];
}
