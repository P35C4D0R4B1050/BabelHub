using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>Cómo terminó el cierre de un ingreso: la sesión se abrió y la persona sigue en <see cref="Destino"/>, o no se pudo abrir (la pareja de rol y host no corresponde, o el servicio no responde).</summary>
public sealed record ResultadoDelCierreDelIngreso(bool Iniciada, string Destino, bool ServicioNoDisponible)
{
    public static ResultadoDelCierreDelIngreso Abierta(string destino) => new(true, destino, false);

    public static ResultadoDelCierreDelIngreso Rechazada { get; } = new(false, string.Empty, false);

    public static ResultadoDelCierreDelIngreso NoDisponible { get; } = new(false, string.Empty, true);
}

/// <summary>
/// El final de un ingreso, de un registro verificado o de una recuperación con credencial (T28; WEB-19, WEB-20, WEB-31, WEB-37): entrega la credencial que Identidad emitió <b>solo</b> a la sesión del servidor (<see cref="ISesionesWeb.Iniciar"/>, que acepta o rechaza la pareja de rol y host), escribe la cookie
/// de sesión, borra la del desafío, emite la marca de dispositivo si hace falta y, para un cliente, <b>fusiona el carrito del visitante</b> con el de su cuenta (idempotente; si falla se reintenta en el siguiente <c>POST</c> del carrito y nunca impide el ingreso). El navegador nunca ve la credencial ni la renovación.
/// </summary>
public sealed class CierreDelIngresoWeb(ISesionesWeb sesiones, OpcionesDelHostWeb host, IEstablecedorDeActor actor, ICarritoDelCliente carrito)
{
    private const string Portada = "/";

    public async Task<ResultadoDelCierreDelIngreso> Completar(HttpContext contexto, CredencialDeSesion credencial, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(credencial);
        var rol = Rol(credencial.Rol);
        var dispositivo = DatosDelBorde.Leer(contexto.Request);
        Resultado<SesionIniciada> iniciada;
        try
        {
            iniciada = await sesiones.Iniciar(
                new SolicitudDeInicioDeSesion(
                    host.Host,
                    new IngresoCompletado(credencial.UsuarioId, rol, credencial.Credencial.Revelar(), credencial.CredencialExpiraEn, credencial.Renovacion.Revelar()),
                    CookieDeSesionWeb.Leer(contexto.Request, host.Host),
                    dispositivo.Marca is not null),
                cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return ResultadoDelCierreDelIngreso.NoDisponible;
        }

        if (!iniciada.EsExito)
        {
            return iniciada.Error!.Categoria == CategoriaDeError.NoDisponible ? ResultadoDelCierreDelIngreso.NoDisponible : ResultadoDelCierreDelIngreso.Rechazada;
        }

        CookieDeSesionWeb.Escribir(contexto.Response, host.Host, iniciada.Valor.ValorDeLaCookie);
        if (iniciada.Valor.MarcaDeDispositivoNueva is { } marca)
        {
            CookieDeDispositivo.Escribir(contexto.Response, marca);
        }

        CookieDelDesafio.Borrar(contexto.Response);
        if (rol == RolWeb.Cliente)
        {
            await FusionarElCarrito(contexto, credencial, cancelacion).ConfigureAwait(false);
        }

        contexto.Response.Headers.CacheControl = "no-store";
        return ResultadoDelCierreDelIngreso.Abierta(DestinoPorRol(rol));
    }

    /// <summary>Quien ingresa llega a lo que su rol usa: la portada de la tienda, la logística de su sucursal o el catálogo nacional.</summary>
    public static string DestinoPorRol(RolWeb rol) => rol switch
    {
        RolWeb.Encargado or RolWeb.Logistica => "/logistica/envios",
        RolWeb.AdministradorCentral => "/administracion/obras",
        _ => Portada,
    };

    public static RolWeb Rol(RolDeCuenta rol) => rol switch
    {
        RolDeCuenta.Cliente => RolWeb.Cliente,
        RolDeCuenta.EncargadoDeSucursal => RolWeb.Encargado,
        RolDeCuenta.Logistica => RolWeb.Logistica,
        RolDeCuenta.AdministradorCentral => RolWeb.AdministradorCentral,
        _ => throw new ArgumentOutOfRangeException(nameof(rol)),
    };

    private async Task FusionarElCarrito(HttpContext contexto, CredencialDeSesion credencial, CancellationToken cancelacion)
    {
        if (CookieDelCarrito.Leer(contexto.Request) is not { } cookie)
        {
            return;
        }

        try
        {
            // La sesión nueva todavía no es el actor de esta solicitud: la cuenta que acaba de ingresar lo es solo mientras dura la fusión.
            actor.Establecer(ActorActual.DeUsuario(credencial.UsuarioId, RolesDeUsuario(credencial.Rol), credencial.Sucursal));
            var fusion = await carrito.FusionarCarritoDeVisitante(cookie, cancelacion).ConfigureAwait(false);
            if (fusion.EsExito)
            {
                CookieDelCarrito.Aplicar(contexto.Response, null, fusion.Valor.BorrarLaCookie);
            }
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // La fusión nunca impide el ingreso: se reintenta en el siguiente POST del carrito.
        }
        finally
        {
            actor.Establecer(ActorActual.Anonimo());
        }
    }

    private static string RolesDeUsuario(RolDeCuenta rol) => rol switch
    {
        RolDeCuenta.EncargadoDeSucursal => Shared.Contratos.RolesDeUsuario.EncargadoDeSucursal,
        RolDeCuenta.Logistica => Shared.Contratos.RolesDeUsuario.PersonalDeLogistica,
        RolDeCuenta.AdministradorCentral => Shared.Contratos.RolesDeUsuario.AdministradorCentral,
        _ => Shared.Contratos.RolesDeUsuario.Cliente,
    };
}
