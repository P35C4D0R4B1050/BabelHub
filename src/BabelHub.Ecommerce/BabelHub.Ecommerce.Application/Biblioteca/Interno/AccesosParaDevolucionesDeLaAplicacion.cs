using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;

namespace BabelHub.Ecommerce.Application.Biblioteca.Interno;

/// <summary>
/// La implementación de <see cref="IAccesosParaDevoluciones"/> (T09; BIB-10, BIB-21, BIB-22, BIB-23, BIB-24, D04, D16). Todas las operaciones <b>participan en la transacción del llamador</b> (Devoluciones) y son una sola sentencia condicional sobre la fila del acceso, que es lo
/// último en el orden de bloqueo global. La <c>cuenta</c> que reciben sale del contrato de lectura de Pedidos, nunca del actor: el acceso de otra cuenta aparece como «sin acceso». Si ya hubo un efecto y la auditoría cae, la excepción sube y el llamador revierte todo.
/// </summary>
internal sealed class AccesosParaDevolucionesDeLaAplicacion(IRepositorioDeAccesos accesos, IAuditoria auditoria, IReloj reloj) : IAccesosParaDevoluciones
{
    public async Task<Resultado<AccesoParaDevoluciones?>> Consultar(Guid cuentaId, Guid pedidoId, Guid edicionId, CancellationToken cancelacion = default)
    {
        if (cuentaId == Guid.Empty || pedidoId == Guid.Empty || edicionId == Guid.Empty)
        {
            return FallosDeBiblioteca.Validacion<AccesoParaDevoluciones?>();
        }

        try
        {
            var acceso = await accesos.ObtenerDeLaCompra(cuentaId, pedidoId, edicionId, cancelacion).ConfigureAwait(false);
            return Resultado<AccesoParaDevoluciones?>.Exito(
                acceso is null ? null : new AccesoParaDevoluciones(acceso.Id, MapeoDeBiblioteca.EstadoParaDevoluciones(acceso.Estado), acceso.EnlacesEntregados > 0));
        }
        catch (BibliotecaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<AccesoParaDevoluciones?>();
        }
    }

    public async Task<Resultado<ResultadoDeRevocarSiNoHayEnlace>> RevocarSiNoHayEnlace(Guid cuentaId, Guid pedidoId, Guid edicionId, CancellationToken cancelacion = default)
    {
        if (cuentaId == Guid.Empty || pedidoId == Guid.Empty || edicionId == Guid.Empty)
        {
            return FallosDeBiblioteca.Validacion<ResultadoDeRevocarSiNoHayEnlace>();
        }

        CambioDeAcceso<ResultadoDeRevocacionSinEnlace> cambio;
        try
        {
            cambio = await accesos.RevocarSiNoHayEnlace(cuentaId, pedidoId, edicionId, reloj.Ahora, cancelacion).ConfigureAwait(false);
        }
        catch (BibliotecaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<ResultadoDeRevocarSiNoHayEnlace>();
        }

        if (cambio.Resultado == ResultadoDeRevocacionSinEnlace.Revocado && cambio.Acceso is { } revocado)
        {
            // Audita igual que Revocar, con el motivo «reembolso»; si cae, la excepción sube y el llamador revierte (BIB-24).
            await auditoria.Registrar(AuditoriaDeBiblioteca.Revocado(revocado, CausaDeRevocacion.Reembolso), cancelacion).ConfigureAwait(false);
        }

        return Resultado<ResultadoDeRevocarSiNoHayEnlace>.Exito(cambio.Resultado switch
        {
            ResultadoDeRevocacionSinEnlace.Revocado => ResultadoDeRevocarSiNoHayEnlace.Revocado,
            ResultadoDeRevocacionSinEnlace.EnlaceYaEntregado => ResultadoDeRevocarSiNoHayEnlace.EnlaceYaEntregado,
            ResultadoDeRevocacionSinEnlace.YaRevocado => ResultadoDeRevocarSiNoHayEnlace.YaRevocado,
            ResultadoDeRevocacionSinEnlace.NoDisponible => ResultadoDeRevocarSiNoHayEnlace.NoDisponible,
            _ => ResultadoDeRevocarSiNoHayEnlace.SinAcceso,
        });
    }

    public async Task<Resultado<ResultadoDeSuspenderAcceso>> Suspender(Guid cuentaId, Guid accesoId, CancellationToken cancelacion = default)
    {
        if (cuentaId == Guid.Empty || accesoId == Guid.Empty)
        {
            return FallosDeBiblioteca.Validacion<ResultadoDeSuspenderAcceso>();
        }

        try
        {
            var cambio = await accesos.Suspender(cuentaId, accesoId, reloj.Ahora, cancelacion).ConfigureAwait(false);
            return Resultado<ResultadoDeSuspenderAcceso>.Exito(cambio.Resultado switch
            {
                ResultadoDeSuspension.Suspendido => ResultadoDeSuspenderAcceso.Suspendido,
                ResultadoDeSuspension.YaSuspendido => ResultadoDeSuspenderAcceso.YaSuspendido,
                _ => ResultadoDeSuspenderAcceso.NoDisponible,
            });
        }
        catch (BibliotecaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<ResultadoDeSuspenderAcceso>();
        }
    }

    public async Task<Resultado<ResultadoDeReanudarAcceso>> Reanudar(Guid cuentaId, Guid accesoId, CancellationToken cancelacion = default)
    {
        if (cuentaId == Guid.Empty || accesoId == Guid.Empty)
        {
            return FallosDeBiblioteca.Validacion<ResultadoDeReanudarAcceso>();
        }

        CambioDeAcceso<ResultadoDeReanudacion> cambio;
        try
        {
            cambio = await accesos.Reanudar(cuentaId, accesoId, cancelacion).ConfigureAwait(false);
        }
        catch (BibliotecaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<ResultadoDeReanudarAcceso>();
        }

        if (cambio.Resultado != ResultadoDeReanudacion.Reanudado || cambio.Acceso is null)
        {
            return Resultado<ResultadoDeReanudarAcceso>.Exito(ResultadoDeReanudarAcceso.NoDisponible);
        }

        await auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.ReporteDeArchivoResuelto)
            {
                ObjetoTipo = "reporte-de-archivo",
                ObjetoId = cambio.Acceso.Id.ToString(),
                Detalle = new Dictionary<string, string?> { ["edicionId"] = cambio.Acceso.EdicionId.ToString(), ["resolucion"] = "reanudado" },
            },
            cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeReanudarAcceso>.Exito(ResultadoDeReanudarAcceso.Reanudado);
    }
}
