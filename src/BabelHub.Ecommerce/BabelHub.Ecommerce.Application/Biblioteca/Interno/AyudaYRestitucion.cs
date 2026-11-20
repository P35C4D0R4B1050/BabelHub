using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;

namespace BabelHub.Ecommerce.Application.Biblioteca.Interno;

internal sealed record PedirAyudaEntrada(Guid AccesoId, string? Motivo);

internal sealed record ListarAyudasEntrada(EstadoDeLaAyudaDeBiblioteca? Estado, int Pagina, int Tamano);

/// <summary>
/// <c>IAyudaDeLaBiblioteca.Pedir</c> (T08; BIB-16): el titular pide ayuda sobre un acceso <b>propio</b> vigente o suspendido (uno revocado responde <c>ECO-BIB-ACCESO-NO-DISPONIBLE</c>; uno ajeno o inexistente, el mismo <c>ECO-NO-ENCONTRADO</c> de siempre). Motivo de lista cerrada, sin
/// texto libre (D06); a lo más una abierta por acceso: pedirla de nuevo devuelve la existente. No se audita (no es una operación sensible).
/// </summary>
internal sealed class PedirAyuda(AutorizadorDeBiblioteca autorizador, IRepositorioDeAccesos accesos, IRepositorioDeDescargas descargas, IUnidadDeTrabajo unidad, IReloj reloj)
    : ICasoDeUso<PedirAyudaEntrada, AyudaDeBiblioteca>
{
    public async Task<Resultado<AyudaDeBiblioteca>> Ejecutar(PedirAyudaEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var cliente = await autorizador.ExigirCliente(cancelacion).ConfigureAwait(false);
        if (!cliente.EsExito)
        {
            return FallosDeBiblioteca.Fallo<AyudaDeBiblioteca>(cliente.Error!);
        }

        var cuenta = cliente.Valor;
        if (await autorizador.ExigirTextosAlDia(cuenta, cancelacion).ConfigureAwait(false) is { } pendiente)
        {
            return FallosDeBiblioteca.Fallo<AyudaDeBiblioteca>(pendiente);
        }

        if (CodigosDeMotivos.AyudaDesdeCodigo(entrada.Motivo) is not { } motivo)
        {
            return FallosDeBiblioteca.Validacion<AyudaDeBiblioteca>();
        }

        try
        {
            var acceso = entrada.AccesoId == Guid.Empty ? null : await accesos.Obtener(entrada.AccesoId, cuenta, cancelacion).ConfigureAwait(false);
            if (acceso is null)
            {
                return FallosDeBiblioteca.Fallo<AyudaDeBiblioteca>(await autorizador.NoEncontradoYDenegado(cancelacion).ConfigureAwait(false));
            }

            if (acceso.Estado == EstadoDeAcceso.Revocado)
            {
                return FallosDeBiblioteca.De<AyudaDeBiblioteca>(CategoriaDeError.Conflicto, ClavesDeBiblioteca.AccesoNoDisponible);
            }

            var existente = await descargas.ObtenerAyudaAbierta(acceso.Id, cancelacion).ConfigureAwait(false);
            if (existente is not null)
            {
                return Resultado<AyudaDeBiblioteca>.Exito(MapeoDeBiblioteca.Ayuda(existente));
            }

            var nueva = SolicitudDeAyuda.Abrir(acceso.Id, motivo, reloj.Ahora);
            var agregada = await unidad.Ejecutar(token => descargas.AgregarAyuda(nueva, token), cancelacion).ConfigureAwait(false);
            if (agregada)
            {
                return Resultado<AyudaDeBiblioteca>.Exito(MapeoDeBiblioteca.Ayuda(nueva));
            }

            // Otra solicitud simultánea ganó el único parcial: se devuelve la que quedó abierta.
            var ganadora = await descargas.ObtenerAyudaAbierta(acceso.Id, cancelacion).ConfigureAwait(false);
            return ganadora is null ? FallosDeBiblioteca.NoDisponible<AyudaDeBiblioteca>() : Resultado<AyudaDeBiblioteca>.Exito(MapeoDeBiblioteca.Ayuda(ganadora));
        }
        catch (BibliotecaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<AyudaDeBiblioteca>();
        }
    }
}

/// <summary><c>IAdministracionDeLaBiblioteca.ListarAyudas</c> (T08): solo la administración central; cada solicitud con su acceso, motivo y fecha, <b>sin datos de la cuenta</b>.</summary>
internal sealed class ListarAyudas(AutorizadorDeBiblioteca autorizador, IRepositorioDeDescargas descargas) : ICasoDeUso<ListarAyudasEntrada, PaginaDeAyudas>
{
    public async Task<Resultado<PaginaDeAyudas>> Ejecutar(ListarAyudasEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministradorCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeBiblioteca.Fallo<PaginaDeAyudas>(rechazo);
        }

        if (!PaginacionDeBiblioteca.EsValida(entrada.Pagina, entrada.Tamano) || (entrada.Estado is { } e && !Enum.IsDefined(e)))
        {
            return FallosDeBiblioteca.Validacion<PaginaDeAyudas>();
        }

        try
        {
            EstadoDeAyuda? estado = entrada.Estado is null ? null : (entrada.Estado == EstadoDeLaAyudaDeBiblioteca.Abierta ? EstadoDeAyuda.Abierta : EstadoDeAyuda.Cerrada);
            var (ayudas, total) = await descargas.ListarAyudas(estado, (entrada.Pagina - 1) * entrada.Tamano, entrada.Tamano, cancelacion).ConfigureAwait(false);
            return Resultado<PaginaDeAyudas>.Exito(new PaginaDeAyudas([.. ayudas.Select(MapeoDeBiblioteca.Ayuda)], entrada.Pagina, entrada.Tamano, total));
        }
        catch (BibliotecaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<PaginaDeAyudas>();
        }
    }
}

/// <summary>
/// <c>IAdministracionDeLaBiblioteca.RestituirDescargas</c> (T08; BIB-08, AC-BIB-07): solo la administración central. Devuelve de 1 hasta las usadas con una resta condicionada (no toca un acceso revocado) y motivo de lista cerrada, audita <c>DescargasRestituidas</c> en
/// la misma transacción (sin auditar no se aplica) y, si trae <c>ayudaId</c>, comprueba que <b>la ayuda sea del mismo acceso</b> y la cierra.
/// </summary>
internal sealed class RestituirDescargas(
    AutorizadorDeBiblioteca autorizador,
    IRepositorioDeAccesos accesos,
    IRepositorioDeDescargas descargas,
    ProyectorDeAccesos proyector,
    IAuditoria auditoria,
    IUnidadDeTrabajo unidad,
    IReloj reloj) : ICasoDeUso<RestituirDescargasEntrada, AccesoDeBiblioteca>
{
    public async Task<Resultado<AccesoDeBiblioteca>> Ejecutar(RestituirDescargasEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministradorCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeBiblioteca.Fallo<AccesoDeBiblioteca>(rechazo);
        }

        if (entrada.AccesoId == Guid.Empty || entrada.Cantidad < 1 || entrada.AyudaId == Guid.Empty || CodigosDeMotivos.RestitucionDesdeCodigo(entrada.Motivo) is not { } motivo)
        {
            return FallosDeBiblioteca.Validacion<AccesoDeBiblioteca>();
        }

        try
        {
            var acceso = await accesos.Obtener(entrada.AccesoId, null, cancelacion).ConfigureAwait(false);
            if (acceso is null)
            {
                return FallosDeBiblioteca.NoEncontrado<AccesoDeBiblioteca>();
            }

            if (acceso.Estado == EstadoDeAcceso.Revocado)
            {
                return FallosDeBiblioteca.De<AccesoDeBiblioteca>(CategoriaDeError.Conflicto, ClavesDeBiblioteca.AccesoNoDisponible);
            }

            if (entrada.Cantidad > acceso.DescargasUsadas)
            {
                return FallosDeBiblioteca.Validacion<AccesoDeBiblioteca>();
            }

            if (entrada.AyudaId is { } ayudaId)
            {
                var ayuda = await descargas.ObtenerAyuda(ayudaId, cancelacion).ConfigureAwait(false);
                if (ayuda is null || ayuda.AccesoId != acceso.Id)
                {
                    return FallosDeBiblioteca.NoEncontrado<AccesoDeBiblioteca>();
                }
            }

            var restituido = await unidad.Ejecutar<Acceso?>(
                async token =>
                {
                    var actualizado = await accesos.Restituir(acceso.Id, entrada.Cantidad, token).ConfigureAwait(false);
                    if (actualizado is null)
                    {
                        return null;
                    }

                    await auditoria.Registrar(
                        new SolicitudDeRegistro(AccionesAuditables.DescargasRestituidas)
                        {
                            ObjetoTipo = AuditoriaDeBiblioteca.TipoDeObjeto,
                            ObjetoId = acceso.Id.ToString(),
                            Detalle = new Dictionary<string, string?>
                            {
                                ["edicionId"] = acceso.EdicionId.ToString(),
                                ["cantidad"] = entrada.Cantidad.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                ["motivo"] = motivo.Codigo(),
                            },
                        },
                        token).ConfigureAwait(false);
                    if (entrada.AyudaId is { } cerrar)
                    {
                        await descargas.CerrarAyuda(cerrar, reloj.Ahora, token).ConfigureAwait(false);
                    }

                    return actualizado;
                },
                cancelacion).ConfigureAwait(false);

            if (restituido is null)
            {
                // La resta condicionada no afectó filas: otro proceso revocó el acceso o bajó las usadas tras la lectura.
                return FallosDeBiblioteca.De<AccesoDeBiblioteca>(CategoriaDeError.Conflicto, ClavesDeBiblioteca.Conflicto);
            }

            var proyectado = await proyector.Proyectar([restituido], cancelacion).ConfigureAwait(false);
            return proyectado.EsExito ? Resultado<AccesoDeBiblioteca>.Exito(proyectado.Valor[0]) : FallosDeBiblioteca.Fallo<AccesoDeBiblioteca>(proyectado.Error!);
        }
        catch (Exception ex) when (ex is BibliotecaNoDisponibleException or AuditoriaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<AccesoDeBiblioteca>();
        }
    }
}

/// <summary><c>IAdministracionDeLaBiblioteca.CerrarAyuda</c> (T08): solo la administración central; cerrar una ya cerrada es un conflicto.</summary>
internal sealed class CerrarAyuda(AutorizadorDeBiblioteca autorizador, IRepositorioDeDescargas descargas, IUnidadDeTrabajo unidad, IReloj reloj) : ICasoDeUso<Guid, AyudaDeBiblioteca>
{
    public async Task<Resultado<AyudaDeBiblioteca>> Ejecutar(Guid ayudaId, CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirAdministradorCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeBiblioteca.Fallo<AyudaDeBiblioteca>(rechazo);
        }

        if (ayudaId == Guid.Empty)
        {
            return FallosDeBiblioteca.Validacion<AyudaDeBiblioteca>();
        }

        try
        {
            var ayuda = await descargas.ObtenerAyuda(ayudaId, cancelacion).ConfigureAwait(false);
            if (ayuda is null)
            {
                return FallosDeBiblioteca.NoEncontrado<AyudaDeBiblioteca>();
            }

            var cerrada = await unidad.Ejecutar(token => descargas.CerrarAyuda(ayudaId, reloj.Ahora, token), cancelacion).ConfigureAwait(false);
            if (!cerrada)
            {
                return FallosDeBiblioteca.De<AyudaDeBiblioteca>(CategoriaDeError.Conflicto, ClavesDeBiblioteca.Conflicto);
            }

            var actual = await descargas.ObtenerAyuda(ayudaId, cancelacion).ConfigureAwait(false);
            return actual is null ? FallosDeBiblioteca.NoEncontrado<AyudaDeBiblioteca>() : Resultado<AyudaDeBiblioteca>.Exito(MapeoDeBiblioteca.Ayuda(actual));
        }
        catch (BibliotecaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<AyudaDeBiblioteca>();
        }
    }
}

internal sealed class AyudaDeLaBibliotecaDeLaAplicacion(PedirAyuda pedir) : IAyudaDeLaBiblioteca
{
    public Task<Resultado<AyudaDeBiblioteca>> Pedir(Guid accesoId, string motivo, CancellationToken cancelacion = default) => pedir.Ejecutar(new PedirAyudaEntrada(accesoId, motivo), cancelacion);
}

internal sealed class AdministracionDeLaBibliotecaDeLaAplicacion(ConsultarBibliotecaDeUnCliente consultar, ListarAyudas listar, RestituirDescargas restituir, CerrarAyuda cerrar) : IAdministracionDeLaBiblioteca
{
    public Task<Resultado<IReadOnlyList<AccesoDeBiblioteca>>> ConsultarBibliotecaDeUnCliente(Guid cuentaId, CancellationToken cancelacion = default) => consultar.Ejecutar(cuentaId, cancelacion);

    public Task<Resultado<PaginaDeAyudas>> ListarAyudas(EstadoDeLaAyudaDeBiblioteca? estado, int pagina, int tamano, CancellationToken cancelacion = default) =>
        listar.Ejecutar(new ListarAyudasEntrada(estado, pagina, tamano), cancelacion);

    public Task<Resultado<AccesoDeBiblioteca>> RestituirDescargas(RestituirDescargasEntrada entrada, CancellationToken cancelacion = default) => restituir.Ejecutar(entrada, cancelacion);

    public Task<Resultado<AyudaDeBiblioteca>> CerrarAyuda(Guid ayudaId, CancellationToken cancelacion = default) => cerrar.Ejecutar(ayudaId, cancelacion);
}
