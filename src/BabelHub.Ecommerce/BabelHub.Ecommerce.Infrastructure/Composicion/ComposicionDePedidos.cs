using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Pedidos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>
/// La composición del módulo Pedidos (<c>ecommerce-pedidos</c>): registra los adaptadores reales de lo que ya entregaron Plataforma y Auditoría —las reservas sobre el cliente del Inventario, los límites y la auditoría de la lectura del contacto— <b>solo si lo que necesitan ya está registrado</b>, y al final los dobles
/// provisionales de lo que falta (Biblioteca, Envíos, Identidad…) con su guarda de producción, que nombra cada puerto que siga con un doble. Se llama <b>después</b> de registrar Plataforma, Auditoría e Identidad. Los repositorios, los casos de uso y las fachadas públicas se suman con las tareas T14 a T31.
/// </summary>
public static class ComposicionDePedidos
{
    public static IServiceCollection AgregarPedidos(this IServiceCollection servicios, bool esProduccion)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        if (Hay<IUnidadDeTrabajo>(servicios) && Hay<IConexionDeLaUnidadDeTrabajo>(servicios))
        {
            // El repositorio real (T28) sobre el esquema `pedidos` bajo `EjecutarComo(pedidos)`.
            servicios.TryAddScoped<AccesoAlEsquemaDePedidos>();
            servicios.TryAddScoped<IRepositorioDePedidos, RepositorioDePedidosPostgres>();
        }

        if (Hay<IClienteDelInventario>(servicios))
        {
            servicios.TryAddScoped<IReservasDelPedido, RestReservasDelPedido>();
        }

        if (Hay<IControlDeLimites>(servicios))
        {
            servicios.TryAddScoped<ILimitadorDePedidos, LimitadorDePedidosSobreLimites>();
        }

        if (Hay<IAuditoria>(servicios))
        {
            servicios.TryAddScoped<IAuditoriaDeContacto, AuditoriaDeContactoSobreAuditoria>();
        }

        // T30: la sucursal por su código, los generadores y los casos de uso de los trabajos de fondo (el anfitrión de la Api los hospeda, T32).
        servicios.TryAddSingleton<IGeneradorDeNumerosDePedido, AleatorioGeneradorDeNumerosDePedido>();
        servicios.TryAddSingleton<IGeneradorDeIdentificadoresDePedidos, GeneradorDeIdentificadoresDePedidos>();
        servicios.TryAddSingleton<IResolucionDeSucursal, CodigoResolucionDeSucursal>();
        servicios.TryAddScoped<AutorizadorDePedidos>();
        servicios.TryAddScoped<ControlesDeGestionDePedidos>();
        servicios.TryAddScoped<OrquestadorDeAnulaciones>();
        servicios.TryAddScoped<EntregarLineasDelPedido>();
        servicios.TryAddScoped<CompletarOperacionesEnCurso>();
        servicios.TryAddScoped<MarcarLineasNoRecogidas>();
        servicios.TryAddScoped<LiberarContactoDeIntenciones>();
        servicios.TryAddScoped<AnonimizarContactosVencidos>();
        servicios.TryAddScoped<RevisarPedidosSinEstadoFinal>();
        // Las fachadas públicas por rol y las lecturas para otros módulos (ECO-6f): delgadas traducciones a los casos de uso, sin reglas.
        servicios.TryAddScoped<ConsultasDePedidos>();
        servicios.TryAddScoped<OperarLineasDelPedido>();
        servicios.TryAddScoped<CancelarPedidoDelCliente>();
        servicios.TryAddScoped<AnularPedido>();
        servicios.TryAddScoped<ReintentarReembolsoDeAnulacion>();
        servicios.TryAddScoped<OperacionesFallidasDePedidos>();
        servicios.TryAddScoped<IPedidosDelCliente, PedidosDelClienteDeLaAplicacion>();
        servicios.TryAddScoped<IGestionDeRecojo, GestionDeRecojoDeLaAplicacion>();
        servicios.TryAddScoped<IGestionDeDespacho, GestionDeDespachoDeLaAplicacion>();
        servicios.TryAddScoped<IAdministracionDePedidos, AdministracionDePedidosDeLaAplicacion>();
        servicios.TryAddScoped<IPedidosParaEnvios, PedidosParaEnviosDeLaAplicacion>();
        servicios.TryAddScoped<IPedidosParaDevoluciones, PedidosParaDevolucionesDeLaAplicacion>();
        servicios.TryAddScoped<IContactoDeEntregaDelPedido, ContactoDeEntregaDelPedidoDeLaAplicacion>();
        servicios.TryAddScoped<IProgresoDeEnvios, AvanzarLineasPorEnvio>();
        servicios.TryAddScoped<IReceptorDeEstadosDeTraslado, RecibirEstadoDeTraslado>();
        servicios.TryAddEnumerable(ServiceDescriptor.Scoped<IPendientesDeCuenta, ContarPendientesDePedidos>());
        servicios.TryAddEnumerable(ServiceDescriptor.Scoped<IAplicadorDeCancelacion, AnonimizarContactoDeUnaCuenta>());

        return servicios.AgregarDoblesProvisionalesDePedidos(esProduccion);
    }

    private static bool Hay<T>(IServiceCollection servicios) => servicios.Any(d => d.ServiceType == typeof(T));
}
