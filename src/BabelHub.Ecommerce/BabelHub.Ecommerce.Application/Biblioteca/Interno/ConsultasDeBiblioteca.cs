using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Biblioteca.Interno;

internal sealed record ListarMiBibliotecaEntrada(int Pagina, int Tamano);

/// <summary><c>IBibliotecaDelCliente.Listar</c> (T05; BIB-02, BIB-12): solo lo del actor, paginado (tope de 50), sin transacción.</summary>
internal sealed class ListarMiBiblioteca(AutorizadorDeBiblioteca autorizador, IRepositorioDeAccesos accesos, ProyectorDeAccesos proyector) : ICasoDeUso<ListarMiBibliotecaEntrada, PaginaDeAccesos>
{
    public async Task<Resultado<PaginaDeAccesos>> Ejecutar(ListarMiBibliotecaEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var cliente = await autorizador.ExigirCliente(cancelacion).ConfigureAwait(false);
        if (!cliente.EsExito)
        {
            return FallosDeBiblioteca.Fallo<PaginaDeAccesos>(cliente.Error!);
        }

        var cuenta = cliente.Valor;
        if (await autorizador.ExigirTextosAlDia(cuenta, cancelacion).ConfigureAwait(false) is { } pendiente)
        {
            return FallosDeBiblioteca.Fallo<PaginaDeAccesos>(pendiente);
        }

        if (!PaginacionDeBiblioteca.EsValida(entrada.Pagina, entrada.Tamano))
        {
            return FallosDeBiblioteca.Validacion<PaginaDeAccesos>();
        }

        IReadOnlyList<Domain.Biblioteca.Acceso> pagina;
        int total;
        try
        {
            pagina = await accesos.ListarDeLaCuenta(cuenta, (entrada.Pagina - 1) * entrada.Tamano, entrada.Tamano, cancelacion).ConfigureAwait(false);
            total = await accesos.ContarDeLaCuenta(cuenta, cancelacion).ConfigureAwait(false);
        }
        catch (BibliotecaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<PaginaDeAccesos>();
        }

        var proyectados = await proyector.Proyectar(pagina, cancelacion).ConfigureAwait(false);
        return proyectados.EsExito
            ? Resultado<PaginaDeAccesos>.Exito(new PaginaDeAccesos(proyectados.Valor, entrada.Pagina, entrada.Tamano, total))
            : FallosDeBiblioteca.Fallo<PaginaDeAccesos>(proyectados.Error!);
    }
}

/// <summary><c>IBibliotecaDelCliente.Obtener</c> (T05; BIB-03, BIB-07): un acceso propio; uno ajeno o inexistente responde lo mismo y deja un acceso denegado por intento.</summary>
internal sealed class ObtenerAcceso(AutorizadorDeBiblioteca autorizador, IRepositorioDeAccesos accesos, ProyectorDeAccesos proyector) : ICasoDeUso<Guid, AccesoDeBiblioteca>
{
    public async Task<Resultado<AccesoDeBiblioteca>> Ejecutar(Guid accesoId, CancellationToken cancelacion = default)
    {
        var cliente = await autorizador.ExigirCliente(cancelacion).ConfigureAwait(false);
        if (!cliente.EsExito)
        {
            return FallosDeBiblioteca.Fallo<AccesoDeBiblioteca>(cliente.Error!);
        }

        var cuenta = cliente.Valor;
        if (await autorizador.ExigirTextosAlDia(cuenta, cancelacion).ConfigureAwait(false) is { } pendiente)
        {
            return FallosDeBiblioteca.Fallo<AccesoDeBiblioteca>(pendiente);
        }

        Domain.Biblioteca.Acceso? acceso;
        try
        {
            // Filtrado por la cuenta del actor: nunca distingue un acceso ajeno de uno inexistente.
            acceso = accesoId == Guid.Empty ? null : await accesos.Obtener(accesoId, cuenta, cancelacion).ConfigureAwait(false);
        }
        catch (BibliotecaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<AccesoDeBiblioteca>();
        }

        if (acceso is null)
        {
            return FallosDeBiblioteca.Fallo<AccesoDeBiblioteca>(await autorizador.NoEncontradoYDenegado(cancelacion).ConfigureAwait(false));
        }

        var proyectado = await proyector.Proyectar([acceso], cancelacion).ConfigureAwait(false);
        return proyectado.EsExito ? Resultado<AccesoDeBiblioteca>.Exito(proyectado.Valor[0]) : FallosDeBiblioteca.Fallo<AccesoDeBiblioteca>(proyectado.Error!);
    }
}

/// <summary>
/// <c>IAdministracionDeLaBiblioteca.ConsultarBibliotecaDeUnCliente</c> (T05; BIB-12, D07): solo el administrador central. Lee la biblioteca y el título y el formato de Catálogo <b>antes</b> de abrir la transacción de la auditoría; la consulta
/// se audita en esa transacción y <b>falla cerrada</b>: si no se puede auditar, no se muestra.
/// </summary>
internal sealed class ConsultarBibliotecaDeUnCliente(
    AutorizadorDeBiblioteca autorizador,
    IRepositorioDeAccesos accesos,
    ProyectorDeAccesos proyector,
    IAuditoria auditoria,
    IUnidadDeTrabajo unidad) : ICasoDeUso<Guid, IReadOnlyList<AccesoDeBiblioteca>>
{
    /// <summary>La biblioteca de un cliente se entrega completa hasta este tope (un cliente real tiene decenas de libros).</summary>
    public const int Tope = 200;

    public async Task<Resultado<IReadOnlyList<AccesoDeBiblioteca>>> Ejecutar(Guid cuentaId, CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirAdministradorCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeBiblioteca.Fallo<IReadOnlyList<AccesoDeBiblioteca>>(rechazo);
        }

        if (cuentaId == Guid.Empty)
        {
            return FallosDeBiblioteca.Validacion<IReadOnlyList<AccesoDeBiblioteca>>();
        }

        var (existe, noDisponible) = await autorizador.ExisteLaCuenta(cuentaId, cancelacion).ConfigureAwait(false);
        if (noDisponible)
        {
            return FallosDeBiblioteca.NoDisponible<IReadOnlyList<AccesoDeBiblioteca>>();
        }

        if (!existe)
        {
            return FallosDeBiblioteca.NoEncontrado<IReadOnlyList<AccesoDeBiblioteca>>();
        }

        IReadOnlyList<Domain.Biblioteca.Acceso> delCliente;
        try
        {
            delCliente = await accesos.ListarDeLaCuenta(cuentaId, 0, Tope, cancelacion).ConfigureAwait(false);
        }
        catch (BibliotecaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<IReadOnlyList<AccesoDeBiblioteca>>();
        }

        var proyectados = await proyector.Proyectar(delCliente, cancelacion).ConfigureAwait(false);
        if (!proyectados.EsExito)
        {
            return proyectados;
        }

        try
        {
            await unidad.Ejecutar<bool>(
                async token =>
                {
                    await auditoria.Registrar(
                        new SolicitudDeRegistro(AccionesAuditables.BibliotecaDeClienteConsultada) { ObjetoTipo = "cliente", ObjetoId = cuentaId.ToString() },
                        token).ConfigureAwait(false);
                    return true;
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (AuditoriaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<IReadOnlyList<AccesoDeBiblioteca>>();
        }

        return proyectados;
    }
}

/// <summary>Las fachadas de los contratos de lectura por los casos de uso.</summary>
internal sealed class BibliotecaDelClienteDeLaAplicacion(ListarMiBiblioteca listar, ObtenerAcceso obtener) : IBibliotecaDelCliente
{
    public Task<Resultado<PaginaDeAccesos>> Listar(int pagina, int tamano, CancellationToken cancelacion = default) => listar.Ejecutar(new ListarMiBibliotecaEntrada(pagina, tamano), cancelacion);

    public Task<Resultado<AccesoDeBiblioteca>> Obtener(Guid accesoId, CancellationToken cancelacion = default) => obtener.Ejecutar(accesoId, cancelacion);
}
