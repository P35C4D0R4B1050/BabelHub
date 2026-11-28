using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;
using static BabelHub.Ecommerce.UnitTests.Catalogo.CargaInicial.LectorDelArchivoDeCargaTests;

namespace BabelHub.Ecommerce.UnitTests.Catalogo.CargaInicial;

/// <summary>El caso de uso de la carga inicial del catálogo con dobles de los contratos públicos (T03; CIN-01, CIN-02, CIN-03, CIN-06, CIN-07, CIN-10; AC-CIN-01, AC-CIN-02, AC-CIN-03, AC-CIN-06, AC-CIN-08).</summary>
public sealed class CargarCatalogoInicialTests
{
    private readonly AdministracionEnMemoria _administracion = new();
    private readonly ArchivosEnMemoria _archivos = new();
    private readonly CorridaEspia _corrida = new();
    private readonly OrigenDePortadasEnMemoria _portadas = new();
    private readonly DenegacionesDelCatalogoEspia _denegaciones = new();

    private CargarCatalogoInicial Crear(ActorDePrueba? actor = null) =>
        new(new AutorizadorDelCatalogo(actor ?? ActorDePrueba.DeAdministradorCentral(), _denegaciones), _administracion, _archivos, new GeneradorDeMuestraFijo(), _corrida);

    private static string TresObras() => Archivo(
        Fila(clave: "una", titulo: "Una obra", autor: "Ana", isbn: Isbn(1), portada: "una.png", stock: "12;5"),
        Fila(clave: "dos", titulo: "Otra obra", autor: "Luis", isbn: Isbn(2), portada: "dos.png", stock: "8;", descripcion: "Segunda sinopsis."),
        Fila(clave: "tres", titulo: "Obra con digital", autor: "Rosa", isbn: Isbn(3), portada: "tres.png", digital: $"{Isbn(4)};19.90;epub;Dominio público;dominio-publico;", stock: ";6"));

    private void ConLasTresPortadas()
    {
        foreach (var nombre in new[] { "una.png", "dos.png", "tres.png" })
        {
            _portadas.Portadas[nombre] = new byte[2048];
        }
    }

    private SolicitudDeCargaDelCatalogo Solicitud(string archivo) => new(archivo, _portadas);

    [Fact]
    [Trait("Criterio", "AC-CIN-01")]
    public async Task Carga_tres_obras_con_su_descripcion_precio_portada_y_la_digital_con_su_archivo_de_muestra()
    {
        ConLasTresPortadas();

        var resultado = await Crear().Cargar(Solicitud(TresObras()));

        Assert.True(resultado.EsExito);
        var resumen = resultado.Valor;
        Assert.Equal(3, resumen.Cargadas);
        Assert.Equal(0, resumen.Rechazadas);
        Assert.Equal(3, _administracion.Obras.Count);
        Assert.Equal(3, _archivos.Portadas);
        Assert.Equal(1, _archivos.Libros);
        var conDigital = _administracion.Obras.Single(o => o.Titulo == "Obra con digital");
        Assert.Equal("Una sinopsis.", conDigital.Descripcion);
        Assert.Equal(2, conDigital.Ediciones.Count);
        Assert.All(_administracion.Entradas, e => Assert.All(e.Ediciones!, d => Assert.Null(d.StockInicial)));
        var digital = _administracion.Entradas.Single(e => e.Titulo == "Obra con digital").Ediciones!.Single(d => d.Formato == FormatoDeEdicion.Digital);
        Assert.Equal(TipoDeLicenciaDeEdicion.DominioPublico, digital.Licencia!.Tipo);
        Assert.Equal(new[] { "PE" }, digital.Licencia.Territorios);
        Assert.NotNull(digital.ArchivoRef);
        Assert.Equal(19.90m, digital.Precio);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-08")]
    public async Task El_plan_lleva_el_identificador_el_isbn_y_el_stock_de_cada_impresa_con_stock_y_es_el_mismo_al_repetir()
    {
        ConLasTresPortadas();
        var primera = (await Crear().Cargar(Solicitud(TresObras()))).Valor;
        var segunda = (await Crear().Cargar(Solicitud(TresObras()))).Valor;

        Assert.Equal(3, primera.Plan.Ediciones.Count);
        var una = primera.Plan.Ediciones.Single(e => e.Isbn == Isbn(1));
        Assert.Equal(new[] { "lima", "cusco" }, una.Stock.Select(s => s.Key));
        Assert.Equal(new[] { 12, 5 }, una.Stock.Select(s => s.Value));
        Assert.Equal(primera.Plan.ComoJson(), segunda.Plan.ComoJson());
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-02")]
    public async Task Repetir_la_carga_informa_ya_cargadas_sin_subir_nada_ni_duplicar()
    {
        ConLasTresPortadas();
        await Crear().Cargar(Solicitud(TresObras()));
        var subidasAntes = _archivos.Portadas + _archivos.Libros;

        var resumen = (await Crear().Cargar(Solicitud(TresObras()))).Valor;

        Assert.Equal(3, resumen.YaCargadas);
        Assert.Equal(0, resumen.Cargadas);
        Assert.Equal(3, _administracion.Obras.Count);
        Assert.Equal(subidasAntes, _archivos.Portadas + _archivos.Libros);
        Assert.True(resumen.Completa);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-03")]
    public async Task Una_fila_con_el_isbn_de_otra_obra_se_rechaza_como_isbn_repetido()
    {
        ConLasTresPortadas();
        await Crear().Cargar(Solicitud(Archivo(Fila(clave: "una", titulo: "Una obra", autor: "Ana", isbn: Isbn(1), portada: "una.png"))));

        var resumen = (await Crear().Cargar(Solicitud(Archivo(Fila(clave: "otra", titulo: "Una obra", autor: "Otro autor", isbn: Isbn(1), portada: "dos.png"))))).Valor;

        var fila = Assert.Single(resumen.Filas);
        Assert.Equal(ResultadoDeFilaDeCarga.Rechazada, fila.Resultado);
        Assert.Equal(("isbn_impreso", "isbn-repetido"), (fila.Campo, fila.Motivo));
        Assert.Single(_administracion.Obras);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-03")]
    public async Task Una_portada_ausente_o_de_mas_de_2_MB_rechaza_solo_esa_fila_y_las_demas_se_cargan()
    {
        _portadas.Portadas["una.png"] = new byte[2048];
        _portadas.Portadas["grande.png"] = new byte[3 * 1024 * 1024];
        var archivo = Archivo(
            Fila(clave: "una", titulo: "Una obra", isbn: Isbn(1), portada: "una.png"),
            Fila(clave: "ausente", titulo: "Sin portada", isbn: Isbn(2), portada: "no-esta.png"),
            Fila(clave: "grande", titulo: "Portada grande", isbn: Isbn(3), portada: "grande.png"));

        var resumen = (await Crear().Cargar(Solicitud(archivo))).Valor;

        Assert.Equal(new[] { ResultadoDeFilaDeCarga.Cargada, ResultadoDeFilaDeCarga.Rechazada, ResultadoDeFilaDeCarga.Rechazada }, resumen.Filas.Select(f => f.Resultado));
        Assert.Equal(("portada", "portada-ausente"), (resumen.Filas[1].Campo, resumen.Filas[1].Motivo));
        Assert.Equal(("portada", "demasiado-grande"), (resumen.Filas[2].Campo, resumen.Filas[2].Motivo));
        Assert.Single(_administracion.Obras);
        Assert.False(resumen.Completa);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-03")]
    public async Task Con_el_almacen_de_objetos_caido_ninguna_fila_se_carga_y_el_resultado_es_el_parcial()
    {
        ConLasTresPortadas();
        _archivos.AlmacenCaido = true;

        var resumen = (await Crear().Cargar(Solicitud(TresObras()))).Valor;

        Assert.Equal(3, resumen.Rechazadas);
        Assert.All(resumen.Filas, f => Assert.Equal(("almacen", "almacen-no-disponible"), (f.Campo, f.Motivo)));
        Assert.Empty(_administracion.Obras);
        Assert.False(resumen.Completa);
        Assert.Empty(resumen.Plan.Ediciones);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-06")]
    public async Task Con_la_auditoria_caida_la_fila_no_se_carga_y_se_informa()
    {
        ConLasTresPortadas();
        _administracion.AuditoriaCaida = true;

        var resumen = (await Crear().Cargar(Solicitud(TresObras()))).Valor;

        Assert.All(resumen.Filas, f => Assert.Equal(("auditoria", "auditoria-no-disponible"), (f.Campo, f.Motivo)));
        Assert.Empty(_administracion.Obras);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-06")]
    public async Task Registra_la_corrida_con_la_huella_sha256_del_archivo_y_los_conteos_y_su_fallo_se_informa()
    {
        ConLasTresPortadas();
        var archivo = TresObras();

        var resumen = (await Crear().Cargar(Solicitud(archivo))).Valor;

        var corrida = Assert.Single(_corrida.Corridas);
        Assert.Equal(resumen.CorridaId, corrida.CorridaId);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(archivo))), corrida.HuellaSha256);
        Assert.Equal((3, 0, 0), (corrida.Cargadas, corrida.YaCargadas, corrida.Rechazadas));
        Assert.False(resumen.RegistroDeLaCorridaFallo);

        _corrida.Falla = true;
        var otra = (await Crear().Cargar(Solicitud(archivo))).Valor;
        Assert.True(otra.RegistroDeLaCorridaFallo);
        Assert.False(otra.Completa);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-06")]
    public async Task Quien_no_es_la_administracion_central_es_rechazado_con_un_acceso_denegado_y_no_se_lee_ni_se_escribe_nada()
    {
        ConLasTresPortadas();

        var resultado = await Crear(ActorDePrueba.DeEncargadoDe(Guid.NewGuid())).Cargar(Solicitud(TresObras()));

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Single(_denegaciones.Eventos);
        Assert.Empty(_administracion.Obras);
        Assert.Equal(0, _archivos.Portadas);
        Assert.Empty(_corrida.Corridas);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-04")]
    public async Task Un_archivo_ilegible_en_conjunto_es_un_fallo_antes_de_escribir_nada()
    {
        ConLasTresPortadas();

        var resultado = await Crear().Cargar(Solicitud(Cabecera + "\n" + Fila() + "\u0001\n"));

        Assert.False(resultado.EsExito);
        Assert.Equal("archivo", resultado.Error!.Errores.Single().Campo);
        Assert.Equal("caracter-prohibido", resultado.Error.Errores.Single().Clave);
        Assert.Empty(_administracion.Obras);
        Assert.Equal(0, _archivos.Portadas);
        Assert.Empty(_corrida.Corridas);
    }

    // ---------- Dobles ----------

    private sealed class GeneradorDeMuestraFijo : IGeneradorDeLibroDeMuestra
    {
        public byte[] GenerarEpub(string titulo) => [1, 2, 3, 4];
    }

    private sealed class CorridaEspia : ICasoDeUso<CorridaDeCargaDelCatalogo, bool>
    {
        public List<CorridaDeCargaDelCatalogo> Corridas { get; } = [];

        public bool Falla { get; set; }

        public Task<Resultado<bool>> Ejecutar(CorridaDeCargaDelCatalogo entrada, CancellationToken cancelacion = default)
        {
            if (Falla)
            {
                throw new AuditoriaNoDisponibleException();
            }

            Corridas.Add(entrada);
            return Task.FromResult(Resultado<bool>.Exito(true));
        }
    }

    private sealed class OrigenDePortadasEnMemoria : IOrigenDeArchivosDeLaCarga
    {
        public Dictionary<string, byte[]> Portadas { get; } = new(StringComparer.Ordinal);

        public Task<Stream?> AbrirPortada(string nombre, CancellationToken cancelacion = default) =>
            Task.FromResult<Stream?>(Portadas.TryGetValue(nombre, out var bytes) ? new MemoryStream(bytes) : null);
    }

    private sealed class ArchivosEnMemoria : IArchivosDelCatalogo
    {
        public int Portadas { get; private set; }

        public int Libros { get; private set; }

        public bool AlmacenCaido { get; set; }

        public async Task<Resultado<ArchivoSubido>> SubirArchivo(SubirArchivoEntrada entrada, CancellationToken cancelacion = default)
        {
            if (AlmacenCaido)
            {
                return Resultado<ArchivoSubido>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDelCatalogo.AlmacenNoDisponible));
            }

            using var copia = new MemoryStream();
            await entrada.Contenido.CopyToAsync(copia, cancelacion);
            if (entrada.Finalidad == FinalidadDeLaSubida.Portada && copia.Length > 2 * 1024 * 1024)
            {
                return Resultado<ArchivoSubido>.Fallo(new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDelCatalogo.ArchivoDemasiadoGrande, [new ErrorDeCampo("archivo", "demasiado-grande")]));
            }

            if (entrada.Finalidad == FinalidadDeLaSubida.Portada)
            {
                Portadas++;
            }
            else
            {
                Libros++;
            }

            return Resultado<ArchivoSubido>.Exito(new ArchivoSubido(Guid.NewGuid(), entrada.Finalidad, "png", copia.Length, new string('a', 64), DateTimeOffset.UtcNow.AddHours(24)));
        }

        public Task<Resultado<PortadaServida>> ObtenerPortada(string huella, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class AdministracionEnMemoria : IAdministracionDelCatalogo
    {
        private readonly List<ObraDeAdministracion> _obras = [];

        public IReadOnlyList<ObraDeAdministracion> Obras => _obras;

        public List<RegistrarObraEntrada> Entradas { get; } = [];

        public (string Campo, string Clave)? RechazarAltasConValidacion { get; set; }

        public bool AuditoriaCaida { get; set; }

        public Task<Resultado<ResultadoDeAlta>> RegistrarObra(RegistrarObraEntrada entrada, CancellationToken cancelacion = default)
        {
            if (AuditoriaCaida)
            {
                throw new AuditoriaNoDisponibleException();
            }

            if (RechazarAltasConValidacion is { } rechazo)
            {
                return Task.FromResult(Resultado<ResultadoDeAlta>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDelCatalogo.ValidacionFallida, [new ErrorDeCampo(rechazo.Campo, rechazo.Clave)])));
            }

            foreach (var existente in _obras)
            {
                if (entrada.Ediciones!.Any(n => existente.Ediciones.Any(e => e.Isbn == Isbn13(n.Isbn!))))
                {
                    return Task.FromResult(Resultado<ResultadoDeAlta>.Fallo(new ErrorDeAplicacion(
                        CategoriaDeError.Conflicto, ClavesDelCatalogo.IsbnDuplicado, [new ErrorDeCampo("isbn", "duplicado"), new ErrorDeCampo("tituloExistente", existente.Titulo)])));
                }
            }

            Entradas.Add(entrada);
            var ediciones = entrada.Ediciones!
                .Select(n => new EdicionDeAdministracion(Guid.NewGuid(), n.Formato, Isbn13(n.Isbn!), n.Isbn!, n.Precio, EstadoDeEdicionConsultado.Activa, null, null, null, null))
                .ToList();
            var obra = new ObraDeAdministracion(Guid.NewGuid(), entrada.Titulo!, entrada.Autor!, entrada.Editorial!, entrada.Categoria!, new string('b', 64), 1, false, ediciones, entrada.Descripcion);
            _obras.Add(obra);
            return Task.FromResult(Resultado<ResultadoDeAlta>.Exito(new ResultadoDeAlta(obra.ObraId, 1, ediciones.Select(e => e.EdicionId).ToList(), [])));
        }

        public Task<Resultado<PaginaDeObrasDeAdministracion>> ListarObras(ListarObrasDeAdministracionEntrada entrada, CancellationToken cancelacion = default)
        {
            var coincidentes = _obras.Where(o => entrada.Texto is null || o.Titulo.Contains(entrada.Texto, StringComparison.OrdinalIgnoreCase)).ToList();
            var pagina = coincidentes.Skip((entrada.Pagina - 1) * entrada.Tamano).Take(entrada.Tamano).ToList();
            return Task.FromResult(Resultado<PaginaDeObrasDeAdministracion>.Exito(new PaginaDeObrasDeAdministracion(pagina, entrada.Pagina, entrada.Tamano, coincidentes.Count)));
        }

        public Task<Resultado<ObraDeAdministracion>> ObtenerObra(Guid obraId, CancellationToken cancelacion = default) =>
            Task.FromResult(Resultado<ObraDeAdministracion>.Exito(_obras.Single(o => o.ObraId == obraId)));

        public Task<Resultado<ResultadoDeEscritura>> EditarObra(EditarObraEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResultadoDeEscritura>> DarDeBajaObra(DarDeBajaObraEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResultadoDeAlta>> RegistrarEdicion(RegistrarEdicionEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResultadoDeEscritura>> EditarEdicion(EditarEdicionEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResultadoDeEscritura>> DarDeBajaEdicion(DarDeBajaEdicionEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResultadoDeEscritura>> RetirarEdicionDigital(RetirarEdicionDigitalEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

        private static string Isbn13(string texto) => BabelHub.Ecommerce.Domain.Catalogo.Isbn.Crear(texto).Isbn13;
    }
}
