using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>Implementa <see cref="ISesionesWeb"/> componiendo los casos de uso de la sesión (T05 a T08).</summary>
public sealed class SesionesWeb : ISesionesWeb
{
    private readonly IniciarSesionWeb _iniciar;
    private readonly CargarSesionWeb _cargar;
    private readonly AsegurarCredencialDeSesion _asegurar;
    private readonly CerrarSesionWeb _cerrar;

    public SesionesWeb(
        IAlmacenDeSesionesWeb sesiones,
        ICifradorDeSecretosDeSesion cifrador,
        ISesionesDeIdentidad identidad,
        IVerificadorDeCredencial verificador,
        IRegistroDeSeguridadDeLaWeb seguridad,
        IPublicadorDeAlertasTecnicas alertas,
        IUnidadDeTrabajo unidad,
        IAleatoriosSeguros aleatorios,
        IReloj reloj)
    {
        var comun = new ServiciosDeSesion(sesiones, cifrador, identidad, seguridad, reloj);
        _iniciar = new IniciarSesionWeb(comun, unidad, new AreasDeInterfaz(), aleatorios);
        _cargar = new CargarSesionWeb(comun, unidad);
        _asegurar = new AsegurarCredencialDeSesion(comun, unidad, verificador, alertas);
        _cerrar = new CerrarSesionWeb(comun, unidad, alertas);
    }

    public Task<Resultado<SesionIniciada>> Iniciar(SolicitudDeInicioDeSesion solicitud, CancellationToken cancelacion = default) =>
        _iniciar.Ejecutar(solicitud, cancelacion);

    public Task<ResultadoDeCarga> Cargar(string? valorDeLaCookie, HostWeb host, OrigenDeLaAccion origen, CancellationToken cancelacion = default) =>
        _cargar.Ejecutar(valorDeLaCookie, host, origen, cancelacion);

    public Task<ResultadoDeCredencial> AsegurarCredencial(string valorDeLaCookie, HostWeb host, CancellationToken cancelacion = default) =>
        _asegurar.Ejecutar(valorDeLaCookie, host, cancelacion);

    public Task Cerrar(string valorDeLaCookie, CancellationToken cancelacion = default) => _cerrar.Cerrar(valorDeLaCookie, cancelacion);

    public Task CerrarTodasDeUnUsuario(Guid usuarioId, CancellationToken cancelacion = default) => _cerrar.CerrarTodasDeUnUsuario(usuarioId, cancelacion);
}
