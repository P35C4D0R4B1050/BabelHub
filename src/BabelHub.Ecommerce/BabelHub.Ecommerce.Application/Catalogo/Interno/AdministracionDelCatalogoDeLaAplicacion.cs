using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

/// <summary>
/// Las lecturas de la administración (<c>ListarObras</c> y <c>ObtenerObra</c> de <see cref="IAdministracionDelCatalogo"/>): solo la administración central, comprobado antes de leer. Devuelven todas las ediciones
/// (también las dadas de baja) con lo que el público no ve (licencia, estado, motivo de retiro, ISBN como se escribió, huella corta del archivo y versión), nunca la clave del almacén.
/// </summary>
internal sealed class LecturasDeAdministracion(
    AutorizadorDelCatalogo autorizador,
    IValidador<ListarObrasDeAdministracionEntrada> validador,
    ILecturasDelCatalogo lecturas,
    IInventarioDelCatalogo inventario,
    IReloj reloj)
{
    public async Task<Resultado<PaginaDeObrasDeAdministracion>> Listar(ListarObrasDeAdministracionEntrada entrada, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDelCatalogo.Fallo<PaginaDeObrasDeAdministracion>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDelCatalogo.Validacion<PaginaDeObrasDeAdministracion>(validacion);
        }

        var pagina = await lecturas.BuscarParaAdministracion(entrada.Texto?.Trim(), entrada.IncluirDadasDeBaja, entrada.Pagina, entrada.Tamano, cancelacion).ConfigureAwait(false);
        return Resultado<PaginaDeObrasDeAdministracion>.Exito(new PaginaDeObrasDeAdministracion(pagina.Obras.Select(o => Mapear(o)).ToList(), entrada.Pagina, entrada.Tamano, pagina.Total));
    }

    public async Task<Resultado<ObraDeAdministracion>> Obtener(Guid obraId, CancellationToken cancelacion)
    {
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDelCatalogo.Fallo<ObraDeAdministracion>(rechazo);
        }

        if (obraId == Guid.Empty)
        {
            return FallosDelCatalogo.Validacion<ObraDeAdministracion>(ResultadoDeValidacion.Invalido([new ErrorDeCampo("obraId", ConstructorDeValidacion.FormatoInvalido)]));
        }

        var obra = await lecturas.ObraPorId(obraId, cancelacion).ConfigureAwait(false);
        if (obra is null)
        {
            return FallosDelCatalogo.Fallo<ObraDeAdministracion>(TraductorDeRechazos.ObraNoEncontrada());
        }

        var registro = await RegistroPendiente(obra, cancelacion).ConfigureAwait(false);
        return Resultado<ObraDeAdministracion>.Exito(Mapear(obra, registro));
    }

    /// <summary>
    /// El aviso «registro o ingreso pendiente» (el Inventario aún no conoce la edición impresa): <b>solo lo ve la administración central</b> (esta lectura ya comprobó el rol) y el público ve «agotada». <see langword="null"/> si el
    /// Inventario no respondió (no se sabe); un fallo del Inventario nunca hace fallar la lectura.
    /// </summary>
    private async Task<IReadOnlySet<Guid>?> RegistroPendiente(Obra obra, CancellationToken cancelacion)
    {
        var impresas = obra.Ediciones.Where(e => !e.EsDigital && !e.EstaDadaDeBaja).Select(e => e.Id).ToList();
        if (impresas.Count == 0)
        {
            return new HashSet<Guid>();
        }

        try
        {
            var respuesta = await inventario.ConsultarDisponibilidad(impresas, cancelacion).ConfigureAwait(false);
            return respuesta is null ? null : respuesta.NoRegistradas.ToHashSet();
        }
        catch (OperationCanceledException) when (!cancelacion.IsCancellationRequested)
        {
            return null;
        }
    }

    private ObraDeAdministracion Mapear(Obra obra, IReadOnlySet<Guid>? sinRegistrar = null) =>
        new(
            obra.Id,
            obra.Titulo,
            obra.Autor,
            obra.Editorial,
            obra.Categoria,
            obra.Portada.Huella.Hex,
            obra.Version,
            obra.EstaDadaDeBaja,
            obra.Ediciones.Select(e => Mapear(e, sinRegistrar)).ToList(),
            obra.Descripcion);

    private EdicionDeAdministracion Mapear(Edicion edicion, IReadOnlySet<Guid>? sinRegistrar) =>
        new(
            edicion.Id,
            edicion.EsDigital ? FormatoDeEdicion.Digital : FormatoDeEdicion.Impresa,
            edicion.Isbn.Isbn13,
            edicion.Isbn.TextoIngresado,
            edicion.Precio.Valor,
            edicion.Estado switch
            {
                EstadoDeEdicion.Activa => EstadoDeEdicionConsultado.Activa,
                EstadoDeEdicion.RetiradaDeVenta => EstadoDeEdicionConsultado.RetiradaDeVenta,
                _ => EstadoDeEdicionConsultado.DadaDeBaja,
            },
            edicion.MotivoDeRetiro,
            edicion.MotivoDeNoDisponiblePara(reloj.HoyEnLima) is { } motivo ? ResolutorDeDisponibilidad.Traducir(motivo) : null,
            edicion.Licencia is { } licencia
                ? new LicenciaDeEdicionDeAdministracion(
                    licencia.Tipo == TipoDeLicencia.Comercial ? TipoDeLicenciaDeEdicion.Comercial : TipoDeLicenciaDeEdicion.DominioPublico,
                    licencia.Titular,
                    licencia.Territorios,
                    licencia.VigenteHasta,
                    licencia.Formato == FormatoDigital.Epub ? FormatoDeArchivoDigital.Epub : FormatoDeArchivoDigital.Pdf)
                : null,
            edicion.Archivo is { } archivo ? new ArchivoDeEdicionDeAdministracion(TiposDeArchivo.Nombre(archivo.TipoDetectado), archivo.TamanoEnBytes, archivo.Huella.Corta, archivo.SubidoEn) : null,
            edicion.EsDigital || edicion.EstaDadaDeBaja || sinRegistrar is null ? null : sinRegistrar.Contains(edicion.Id));
}

/// <summary>
/// La administración del catálogo para la Api (T05, T12): el alta de una obra o de una edición encadena, con el <see cref="OrquestadorDeIngresos"/> <b>sin transacción</b>, el registro de la edición impresa en el
/// Inventario y el ingreso del stock inicial después de confirmar (CAT-20, D11, D15): «el Inventario no responde» deja la obra creada y el ingreso <c>Pendiente</c> o <c>Fallido</c> con su aviso, nunca la revierte. El resto
/// delega en su caso de uso transaccional.
/// </summary>
internal sealed class AdministracionDelCatalogoDeLaAplicacion(
    ICasoDeUso<RegistrarObraEntrada, ResultadoDeAlta> registrarObra,
    ICasoDeUso<EditarObraEntrada, ResultadoDeEscritura> editarObra,
    ICasoDeUso<DarDeBajaObraEntrada, ResultadoDeEscritura> darDeBajaObra,
    ICasoDeUso<RegistrarEdicionEntrada, ResultadoDeAlta> registrarEdicion,
    ICasoDeUso<EditarEdicionEntrada, ResultadoDeEscritura> editarEdicion,
    ICasoDeUso<DarDeBajaEdicionEntrada, ResultadoDeEscritura> darDeBajaEdicion,
    ICasoDeUso<RetirarEdicionDigitalEntrada, ResultadoDeEscritura> retirarEdicionDigital,
    LecturasDeAdministracion lecturas,
    OrquestadorDeIngresos orquestador,
    IRepositorioDeIngresos ingresos) : IAdministracionDelCatalogo
{
    public async Task<Resultado<ResultadoDeAlta>> RegistrarObra(RegistrarObraEntrada entrada, CancellationToken cancelacion = default)
    {
        var alta = await registrarObra.Ejecutar(entrada, cancelacion).ConfigureAwait(false);
        return alta.EsExito ? Resultado<ResultadoDeAlta>.Exito(await ContinuarAlta(alta.Valor, entrada.Ediciones!, cancelacion).ConfigureAwait(false)) : alta;
    }

    public async Task<Resultado<ResultadoDeAlta>> RegistrarEdicion(RegistrarEdicionEntrada entrada, CancellationToken cancelacion = default)
    {
        var alta = await registrarEdicion.Ejecutar(entrada, cancelacion).ConfigureAwait(false);
        return alta.EsExito ? Resultado<ResultadoDeAlta>.Exito(await ContinuarAlta(alta.Valor, [entrada.Edicion], cancelacion).ConfigureAwait(false)) : alta;
    }

    public Task<Resultado<ResultadoDeEscritura>> EditarObra(EditarObraEntrada entrada, CancellationToken cancelacion = default) => editarObra.Ejecutar(entrada, cancelacion);

    public Task<Resultado<ResultadoDeEscritura>> DarDeBajaObra(DarDeBajaObraEntrada entrada, CancellationToken cancelacion = default) => darDeBajaObra.Ejecutar(entrada, cancelacion);

    public Task<Resultado<ResultadoDeEscritura>> EditarEdicion(EditarEdicionEntrada entrada, CancellationToken cancelacion = default) => editarEdicion.Ejecutar(entrada, cancelacion);

    public Task<Resultado<ResultadoDeEscritura>> DarDeBajaEdicion(DarDeBajaEdicionEntrada entrada, CancellationToken cancelacion = default) => darDeBajaEdicion.Ejecutar(entrada, cancelacion);

    public Task<Resultado<ResultadoDeEscritura>> RetirarEdicionDigital(RetirarEdicionDigitalEntrada entrada, CancellationToken cancelacion = default) => retirarEdicionDigital.Ejecutar(entrada, cancelacion);

    public Task<Resultado<PaginaDeObrasDeAdministracion>> ListarObras(ListarObrasDeAdministracionEntrada entrada, CancellationToken cancelacion = default) => lecturas.Listar(entrada, cancelacion);

    public Task<Resultado<ObraDeAdministracion>> ObtenerObra(Guid obraId, CancellationToken cancelacion = default) => lecturas.Obtener(obraId, cancelacion);

    /// <summary>
    /// Tras confirmar el alta: cada edición impresa se registra en el Inventario (con o sin stock inicial) y, si pidió stock, se le envía el ingreso y se marca su resultado. <c>Ediciones[i]</c> de la entrada corresponde a
    /// <c>EdicionIds[i]</c> (el orden se conserva). Nada de esto puede hacer fallar el alta: los fallos del Inventario son estados.
    /// </summary>
    private async Task<ResultadoDeAlta> ContinuarAlta(ResultadoDeAlta alta, IReadOnlyList<NuevaEdicionEntrada> entradas, CancellationToken cancelacion)
    {
        var resultado = alta.Ingresos.ToList();
        var sinFicha = new List<Guid>();
        for (var i = 0; i < entradas.Count && i < alta.EdicionIds.Count; i++)
        {
            if (entradas[i].Formato != FormatoDeEdicion.Impresa)
            {
                continue;
            }

            var edicionId = alta.EdicionIds[i];
            var pedido = resultado.FindIndex(x => x.EdicionId == edicionId);
            if (pedido < 0)
            {
                if (!await orquestador.RegistrarFicha(edicionId, cancelacion).ConfigureAwait(false))
                {
                    sinFicha.Add(edicionId);
                }

                continue;
            }

            var fila = await ingresos.Obtener(resultado[pedido].IngresoSolicitadoId, paraActualizar: false, cancelacion).ConfigureAwait(false);
            if (fila is not null)
            {
                var estado = await orquestador.Enviar(fila, esReintento: false, cancelacion).ConfigureAwait(false);
                resultado[pedido] = resultado[pedido] with { Estado = estado };

                // Un primer envío fallido puede ser porque la ficha no se pudo confirmar (nada se envió) o porque el Inventario rechazó el ingreso: se comprueba la ficha para el aviso «registro pendiente» de la administración.
                if (estado == EstadoDeIngresoSolicitado.Fallido && !await orquestador.RegistrarFicha(edicionId, cancelacion).ConfigureAwait(false))
                {
                    sinFicha.Add(edicionId);
                }
            }
        }

        return alta with { Ingresos = resultado, FichasSinConfirmar = sinFicha };
    }
}
