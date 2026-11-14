using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>
/// Abre la sesión web tras un ingreso correcto de Identidad (WEB-28, T05). Rechaza la pareja rol y host que no corresponde con el mismo error que una
/// credencial inválida y revoca la renovación recién emitida; si corresponde, crea la sesión con un identificador nuevo, cierra y revoca la que presentó la
/// cookie, guarda la credencial y la renovación cifradas y emite la marca de dispositivo si no hay una válida (sin rotarla).
/// </summary>
internal sealed class IniciarSesionWeb(ServiciosDeSesion comun, IUnidadDeTrabajo unidad, IAreasDeInterfaz areas, IAleatoriosSeguros aleatorios)
{
    public const string ClaveDeCredencialInvalida = "credenciales-invalidas";
    private const int BytesDeLaMarca = 32;

    public async Task<Resultado<SesionIniciada>> Ejecutar(SolicitudDeInicioDeSesion solicitud, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var ingreso = solicitud.Ingreso;

        if (!areas.ParejaDeRolYHostValida(ingreso.Rol, solicitud.Host))
        {
            await RevocarSinSesion(ingreso.Renovacion, cancelacion).ConfigureAwait(false);
            await comun.Seguridad.AccesoDenegado(MotivoDeDenegacionWeb.ParejaDeRolYHostInvalida, ingreso.UsuarioId, cancelacion).ConfigureAwait(false);
            return Resultado<SesionIniciada>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, ClaveDeCredencialInvalida));
        }

        return await unidad.Ejecutar(
            async ct =>
            {
                var valor = ValorDeCookieDeSesion.Nuevo(aleatorios);
                var id = ValorDeCookieDeSesion.Hash(valor);
                var ahora = comun.Reloj.Ahora;
                var sesion = SesionWeb.Abrir(id, ingreso.UsuarioId, solicitud.Host == HostWeb.Tienda ? HostDeSesion.Tienda : HostDeSesion.Gestion, AreasDeInterfaz.Aplicar(ingreso.Rol).EsPersonal(), ahora);
                var credencial = comun.Cifrador.Cifrar(ingreso.Credencial, PropositoDeSecreto.Credencial, ServiciosDeSesion.DatosAsociados(id, PropositoDeSecreto.Credencial, ingreso.UsuarioId));
                var renovacion = comun.Cifrador.Cifrar(ingreso.Renovacion, PropositoDeSecreto.Renovacion, ServiciosDeSesion.DatosAsociados(id, PropositoDeSecreto.Renovacion, ingreso.UsuarioId));

                // Primero se cierra la que presentó la cookie (el orden de bloqueo es siempre primero las filas web y después Identidad).
                await CerrarLaAnterior(solicitud.ValorDeLaCookieAnterior, ct).ConfigureAwait(false);

                await comun.Sesiones.Insertar(
                    new SesionGuardada
                    {
                        Sesion = sesion,
                        CredencialCifrada = credencial.Bytes,
                        CredencialVenceEn = ingreso.CredencialVenceEn,
                        RenovacionCifrada = renovacion.Bytes,
                        VersionDeClave = credencial.VersionDeClave,
                    },
                    ct).ConfigureAwait(false);

                // La marca de dispositivo (256 bits) se emite solo si no hay una válida: una marca válida no se rota.
                var marca = solicitud.TieneMarcaDeDispositivoValida ? null : ValorOpaco(aleatorios.Bytes(BytesDeLaMarca));
                return Resultado<SesionIniciada>.Exito(new SesionIniciada(valor, marca));
            },
            cancelacion).ConfigureAwait(false);
    }

    private async Task CerrarLaAnterior(string? valorAnterior, CancellationToken cancelacion)
    {
        if (!ValorDeCookieDeSesion.TieneFormaValida(valorAnterior))
        {
            return;
        }

        var fila = await comun.Sesiones.CargarParaRenovar(ValorDeCookieDeSesion.Hash(valorAnterior!), cancelacion).ConfigureAwait(false);
        if (fila is null || fila.Sesion.Estado != EstadoDeSesion.Abierta)
        {
            return;
        }

        await comun.CerrarYRevocar(fila, MotivoDeCierre.CerradaPorRevocacion, cancelacion).ConfigureAwait(false);
        await comun.Sesiones.Guardar(fila, cancelacion).ConfigureAwait(false);
    }

    private async Task RevocarSinSesion(string renovacion, CancellationToken cancelacion)
    {
        try
        {
            await comun.Identidad.RevocarRenovacion(renovacion, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // La renovación recién emitida vence sola; el rechazo igual se informa como credencial inválida.
        }
    }

    private static string ValorOpaco(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
