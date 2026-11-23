using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

/// <summary>Genera el archivo de muestra de una edición digital de la carga (CIN-08, D03): un EPUB válido en memoria, de contenido fijo, que dice que no es el libro. Lo implementa la Infraestructura.</summary>
internal interface IGeneradorDeLibroDeMuestra
{
    byte[] GenerarEpub(string titulo);
}

/// <summary>Lo que deja el registro de una corrida de la carga del catálogo en la auditoría (CIN-07): sin datos personales.</summary>
internal sealed record CorridaDeCargaDelCatalogo(Guid CorridaId, string HuellaSha256, int Cargadas, int YaCargadas, int Rechazadas);

/// <summary>El registro de la corrida (<c>CargaInicialEjecutada</c>) en la auditoría, con los primeros 16 hexadecimales de la huella (la auditoría redacta un valor hexadecimal largo como si fuera un secreto, AUD-06), en su propia transacción: si no se puede auditar la operación falla (AUD-04) y la carga lo informa sin deshacer lo cargado.</summary>
internal sealed class RegistrarCorridaDeCargaDelCatalogo(AutorizadorDelCatalogo autorizador, IAuditoria auditoria) : ICasoDeUso<CorridaDeCargaDelCatalogo, bool>
{
    public async Task<Resultado<bool>> Ejecutar(CorridaDeCargaDelCatalogo entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDelCatalogo.Fallo<bool>(rechazo);
        }

        await auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.CargaInicialEjecutada)
            {
                Detalle = new Dictionary<string, string?>
                {
                    ["corridaId"] = entrada.CorridaId.ToString("D"),
                    ["huellaSha256"] = entrada.HuellaSha256.Length > 16 ? entrada.HuellaSha256[..16] : entrada.HuellaSha256,
                    ["cargadas"] = entrada.Cargadas.ToString(CultureInfo.InvariantCulture),
                    ["yaCargadas"] = entrada.YaCargadas.ToString(CultureInfo.InvariantCulture),
                    ["rechazadas"] = entrada.Rechazadas.ToString(CultureInfo.InvariantCulture),
                },
            },
            cancelacion).ConfigureAwait(false);
        return Resultado<bool>.Exito(true);
    }
}

/// <summary>
/// La carga inicial del catálogo (CIN-01 a CIN-10, CIN-15, CIN-19; plan §1 y §11): <b>no escribe por ningún camino distinto</b> de los casos de uso de la administración. Autoriza (solo administración central), lee y valida el archivo entero antes
/// de escribir y procesa las filas en orden. Por fila: (1) busca por <c>ListarObras</c> (por páginas de 50, con las dadas de baja) si alguno de sus ISBN ya existe: si es de la misma obra (título y autor, sin distinguir mayúsculas) es «ya cargada» y no
/// toca nada, y si es de otra se rechaza con el ISBN repetido; (2) sube la portada (<c>SubirArchivo</c>: tipo por contenido, 2 MB, huella) y, si hay digital, el EPUB de muestra; (3) registra la obra con sus ediciones sin stock inicial (el stock lo carga
/// el Inventario). Lo que falla es el resultado de esa fila; el almacén caído deja la fila sin cargar. Al terminar deja el plan de inventario y el registro de la corrida.
/// </summary>
internal sealed class CargarCatalogoInicial(
    AutorizadorDelCatalogo autorizador,
    IAdministracionDelCatalogo administracion,
    IArchivosDelCatalogo archivos,
    IGeneradorDeLibroDeMuestra muestra,
    ICasoDeUso<CorridaDeCargaDelCatalogo, bool> registrarCorrida) : ICargaInicialDelCatalogo
{
    private const int TamanoDePagina = 50;

    private const int MaximoDePaginas = 20;

    private const string ClaveDeIsbnDuplicado = "isbn-duplicado";

    public async Task<Resultado<ResumenDeLaCargaDelCatalogo>> Cargar(SolicitudDeCargaDelCatalogo solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDelCatalogo.Fallo<ResumenDeLaCargaDelCatalogo>(rechazo);
        }

        var leido = LectorDelArchivoDeCarga.Leer(solicitud.TextoDeDatos);
        if (leido.MotivoDelArchivo is { } motivo)
        {
            return FallosDelCatalogo.De<ResumenDeLaCargaDelCatalogo>(CategoriaDeError.Validacion, ClavesDelCatalogo.ValidacionFallida, [new ErrorDeCampo("archivo", motivo)]);
        }

        var huella = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(solicitud.TextoDeDatos)));
        var filas = leido.Errores.Select(e => new FilaDeLaCarga(e.Fila, e.Clave, ResultadoDeFilaDeCarga.Rechazada, e.Campo, e.Motivo)).ToList();
        var plan = new List<EdicionDelPlanDeInventario>();

        foreach (var fila in leido.Filas)
        {
            cancelacion.ThrowIfCancellationRequested();
            FilaProcesada procesada;
            try
            {
                procesada = await Procesar(fila, solicitud.Portadas, cancelacion).ConfigureAwait(false);
            }
            catch (AuditoriaNoDisponibleException)
            {
                // Sin auditoría no se aplica nada (AUD-04): la alta ya se revirtió y esta fila no se carga.
                procesada = new FilaProcesada(new FilaDeLaCarga(fila.Numero, fila.Clave, ResultadoDeFilaDeCarga.Rechazada, "auditoria", "auditoria-no-disponible"), null);
            }

            filas.Add(procesada.Fila);
            if (procesada.EdicionImpresaId is { } edicionId && fila.Stock.Count > 0)
            {
                plan.Add(new EdicionDelPlanDeInventario(edicionId, fila.IsbnImpreso.Isbn13, fila.Stock));
            }
        }

        filas = [.. filas.OrderBy(f => f.Fila)];
        var corridaId = Guid.NewGuid();
        var cargadas = filas.Count(f => f.Resultado == ResultadoDeFilaDeCarga.Cargada);
        var yaCargadas = filas.Count(f => f.Resultado == ResultadoDeFilaDeCarga.YaCargada);
        var rechazadas = filas.Count(f => f.Resultado == ResultadoDeFilaDeCarga.Rechazada);
        var registroFallo = false;
        try
        {
            var registrada = await registrarCorrida.Ejecutar(new CorridaDeCargaDelCatalogo(corridaId, huella, cargadas, yaCargadas, rechazadas), cancelacion).ConfigureAwait(false);
            registroFallo = !registrada.EsExito;
        }
        catch (AuditoriaNoDisponibleException)
        {
            registroFallo = true;
        }

        return Resultado<ResumenDeLaCargaDelCatalogo>.Exito(new ResumenDeLaCargaDelCatalogo(corridaId, huella, filas, new PlanDeInventarioDeLaCarga(plan), registroFallo));
    }

    private async Task<FilaProcesada> Procesar(FilaDelArchivo fila, IOrigenDeArchivosDeLaCarga portadas, CancellationToken cancelacion)
    {
        FilaProcesada Rechazada(string campo, string motivo) => new(new FilaDeLaCarga(fila.Numero, fila.Clave, ResultadoDeFilaDeCarga.Rechazada, campo, motivo), null);

        // (1) ¿Ya está? Por el ISBN y, para decir «la misma obra», por título y autor.
        var busqueda = await BuscarPorIsbn(fila, cancelacion).ConfigureAwait(false);
        if (busqueda.Fallo)
        {
            return Rechazada("obra", "busqueda-fallida");
        }

        if (busqueda.Obra is { } existente)
        {
            return MismaObra(existente, fila)
                ? new FilaProcesada(new FilaDeLaCarga(fila.Numero, fila.Clave, ResultadoDeFilaDeCarga.YaCargada), IdDeLaImpresa(existente, fila))
                : Rechazada("isbn_impreso", "isbn-repetido");
        }

        // (2) La portada y, si la hay, el archivo de muestra de la digital.
        await using var portada = await portadas.AbrirPortada(fila.Portada, cancelacion).ConfigureAwait(false);
        if (portada is null)
        {
            return Rechazada("portada", "portada-ausente");
        }

        var portadaSubida = await archivos.SubirArchivo(new SubirArchivoEntrada(FinalidadDeLaSubida.Portada, portada), cancelacion).ConfigureAwait(false);
        if (!portadaSubida.EsExito)
        {
            var (campo, motivoDeSubida) = DeLaSubida("portada", portadaSubida.Error!);
            return Rechazada(campo, motivoDeSubida);
        }

        var ediciones = new List<NuevaEdicionEntrada> { new(FormatoDeEdicion.Impresa, fila.IsbnImpreso.TextoIngresado, fila.PrecioImpreso, null, null, null) };
        if (fila.Digital is { } digital)
        {
            await using var libro = new MemoryStream(muestra.GenerarEpub(fila.Titulo), writable: false);
            var libroSubido = await archivos.SubirArchivo(new SubirArchivoEntrada(FinalidadDeLaSubida.Libro, libro), cancelacion).ConfigureAwait(false);
            if (!libroSubido.EsExito)
            {
                var (campo, motivoDeSubida) = DeLaSubida("formato_digital", libroSubido.Error!);
                return Rechazada(campo, motivoDeSubida);
            }

            ediciones.Add(new NuevaEdicionEntrada(
                FormatoDeEdicion.Digital,
                digital.Isbn.TextoIngresado,
                digital.Precio,
                new LicenciaDeEdicionEntrada(digital.Licencia, digital.Titular, ["PE"], digital.VigenteHasta, digital.Formato),
                libroSubido.Valor.ReferenciaId,
                null));
        }

        // (3) El alta de la administración, sin stock inicial (CIN-12: el stock lo carga el Inventario).
        var alta = await administracion.RegistrarObra(
            new RegistrarObraEntrada(fila.Titulo, fila.Autor, fila.Editorial, fila.Categoria, portadaSubida.Valor.ReferenciaId, ediciones, fila.Descripcion),
            cancelacion).ConfigureAwait(false);
        if (alta.EsExito)
        {
            var creada = await administracion.ObtenerObra(alta.Valor.ObraId, cancelacion).ConfigureAwait(false);
            return new FilaProcesada(
                new FilaDeLaCarga(fila.Numero, fila.Clave, ResultadoDeFilaDeCarga.Cargada),
                creada.EsExito ? IdDeLaImpresa(creada.Valor, fila) : null);
        }

        return await TraducirElRechazoDelAlta(fila, alta.Error!, cancelacion).ConfigureAwait(false);
    }

    private async Task<FilaProcesada> TraducirElRechazoDelAlta(FilaDelArchivo fila, ErrorDeAplicacion error, CancellationToken cancelacion)
    {
        if (error.Clave == ClaveDeIsbnDuplicado)
        {
            // Una carrera (otra ejecución cargó la misma obra a la vez): se vuelve a mirar y, si es la misma obra, es «ya cargada».
            var titulo = error.Errores.FirstOrDefault(e => e.Campo == "tituloExistente")?.Clave;
            if (string.Equals(titulo?.Trim(), fila.Titulo, StringComparison.OrdinalIgnoreCase))
            {
                var otra = await BuscarPorIsbn(fila, cancelacion).ConfigureAwait(false);
                if (otra.Obra is { } existente && MismaObra(existente, fila))
                {
                    return new FilaProcesada(new FilaDeLaCarga(fila.Numero, fila.Clave, ResultadoDeFilaDeCarga.YaCargada), IdDeLaImpresa(existente, fila));
                }
            }

            return new FilaProcesada(new FilaDeLaCarga(fila.Numero, fila.Clave, ResultadoDeFilaDeCarga.Rechazada, "isbn_impreso", "isbn-repetido"), null);
        }

        var (campo, motivo) = error.Clave switch
        {
            ClavesDelCatalogo.ValidacionFallida when error.Errores.Count > 0 => (error.Errores[0].Campo, error.Errores[0].Clave),
            ClavesDelCatalogo.LicenciaInvalida => ("licencia_digital", ClavesDelCatalogo.LicenciaInvalida),
            _ => ("obra", error.Clave),
        };
        return new FilaProcesada(new FilaDeLaCarga(fila.Numero, fila.Clave, ResultadoDeFilaDeCarga.Rechazada, campo, motivo), null);
    }

    private static (string Campo, string Motivo) DeLaSubida(string campo, ErrorDeAplicacion error) =>
        error.Clave switch
        {
            ClavesDelCatalogo.AlmacenNoDisponible => ("almacen", "almacen-no-disponible"),
            ClavesDelCatalogo.LimiteDeSubidas => ("almacen", "limite-de-subidas"),
            ClavesDelCatalogo.ArchivoInvalido or ClavesDelCatalogo.ArchivoDemasiadoGrande => (campo, error.Errores.FirstOrDefault()?.Clave ?? error.Clave),
            _ => (campo, error.Clave),
        };

    private static bool MismaObra(ObraDeAdministracion obra, FilaDelArchivo fila) =>
        string.Equals(obra.Titulo.Trim(), fila.Titulo, StringComparison.OrdinalIgnoreCase) && string.Equals(obra.Autor.Trim(), fila.Autor, StringComparison.OrdinalIgnoreCase);

    private static Guid? IdDeLaImpresa(ObraDeAdministracion obra, FilaDelArchivo fila) =>
        obra.Ediciones.FirstOrDefault(e => e.Formato == FormatoDeEdicion.Impresa && e.Isbn == fila.IsbnImpreso.Isbn13)?.EdicionId;

    /// <summary>Busca, por el título de la fila y recorriendo las páginas, la obra que ya usa alguno de sus ISBN-13.</summary>
    private async Task<ResultadoDeBusqueda> BuscarPorIsbn(FilaDelArchivo fila, CancellationToken cancelacion)
    {
        var titulo = fila.Titulo.Length > LimitesDelCatalogo.LargoMaximoDeLaBusqueda ? fila.Titulo[..LimitesDelCatalogo.LargoMaximoDeLaBusqueda] : fila.Titulo;
        if (titulo.Length < LimitesDelCatalogo.LargoMinimoDeLaBusqueda)
        {
            return new ResultadoDeBusqueda(null, false);
        }

        var isbns = new HashSet<string>(StringComparer.Ordinal) { fila.IsbnImpreso.Isbn13 };
        if (fila.Digital is { } digital)
        {
            isbns.Add(digital.Isbn.Isbn13);
        }

        for (var pagina = 1; pagina <= MaximoDePaginas; pagina++)
        {
            var respuesta = await administracion.ListarObras(new ListarObrasDeAdministracionEntrada(titulo, IncluirDadasDeBaja: true, Pagina: pagina, Tamano: TamanoDePagina), cancelacion).ConfigureAwait(false);
            if (!respuesta.EsExito)
            {
                return new ResultadoDeBusqueda(null, true);
            }

            var coincidente = respuesta.Valor.Obras.FirstOrDefault(o => o.Ediciones.Any(e => isbns.Contains(e.Isbn)));
            if (coincidente is not null)
            {
                return new ResultadoDeBusqueda(coincidente, false);
            }

            if (respuesta.Valor.Obras.Count == 0 || pagina * TamanoDePagina >= respuesta.Valor.Total)
            {
                break;
            }
        }

        return new ResultadoDeBusqueda(null, false);
    }

    private sealed record ResultadoDeBusqueda(ObraDeAdministracion? Obra, bool Fallo);

    private sealed record FilaProcesada(FilaDeLaCarga Fila, Guid? EdicionImpresaId);
}
