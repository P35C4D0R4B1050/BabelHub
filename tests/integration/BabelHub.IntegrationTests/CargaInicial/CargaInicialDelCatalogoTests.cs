using System.Globalization;
using System.IO.Compression;
using System.Text;
using Amazon.S3.Model;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Catalogo;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.IntegrationTests.Catalogo;
using BabelHub.IntegrationTests.Plataforma;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using static BabelHub.IntegrationTests.Catalogo.ArchivosDeMuestra;

namespace BabelHub.IntegrationTests.CargaInicial;

/// <summary>
/// La carga inicial del catálogo (T03, T04, T08; AC-CIN-01, AC-CIN-02, AC-CIN-05, AC-CIN-06, AC-CIN-07) contra PostgreSQL real, un almacén S3 real y la <b>auditoría real</b>: tres obras con su descripción, precio y portada (una con digital de dominio
/// público y otra con licencia comercial) cuyos archivos viven solo en el almacén; repetirla y correrla dos veces a la vez no duplica nada; los nombres de portada hostiles se rechazan; cada alta queda auditada con la identidad de la carga y la corrida con la huella
/// del archivo; con la auditoría caída la fila no se carga; y quien no es la administración central es rechazado.
/// </summary>
[Collection(ColeccionDeLaApiDelCatalogo.Nombre)]
public sealed class CargaInicialDelCatalogoTests(PostgresDelCatalogoYAuditoriaFixture bd, AlmacenDeObjetosFixture almacen) : IAsyncLifetime
{
    private static readonly Guid Operacion = Guid.Parse(IdentidadesDeOperacion.CargaInicial);

    private CatalogoApiHost _host = null!;
    private string _carpeta = null!;
    private int _portadasAlInicio;
    private int _librosAlInicio;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _host = await CatalogoApiHost.Crear(bd, almacen.Cliente, servicios => servicios.AgregarCargaInicialDelCatalogo());
        _portadasAlInicio = await ObjetosEnElAlmacen(CatalogoApiHost.Portadas);
        _librosAlInicio = await ObjetosEnElAlmacen(CatalogoApiHost.Libros);
        _carpeta = Path.Combine(Path.GetTempPath(), "babelhub-portadas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_carpeta);
        foreach (var nombre in new[] { "una.png", "dos.png", "tres.png" })
        {
            await File.WriteAllBytesAsync(Path.Combine(_carpeta, nombre), Png(60, 90, 1200));
        }
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        Directory.Delete(_carpeta, recursive: true);
    }

    private static string Isbn(int n)
    {
        var cuerpo = "9790" + n.ToString("D8", CultureInfo.InvariantCulture);
        var suma = cuerpo.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return cuerpo + ((10 - (suma % 10)) % 10).ToString(CultureInfo.InvariantCulture);
    }

    private const string Cabecera = "clave;titulo;autor;editorial;categoria;descripcion;portada;isbn_impreso;precio_impreso;isbn_digital;precio_digital;formato_digital;titular_digital;licencia_digital;vigente_hasta;stock_lima;stock_cusco";

    private static string Fila(string clave, string titulo, string autor, string portada, int isbn, string precio = "45.90", string digital = ";;;;;", string stock = "12;5", string descripcion = "Una sinopsis breve.") =>
        $"{clave};{titulo};{autor};Andina;Ficción;{descripcion};{portada};{Isbn(isbn)};{precio};{digital};{stock}";

    private static string TresObras() => Cabecera + "\n"
        + Fila("una", "Una obra", "Ana", "una.png", 1) + "\n"
        + Fila("dos", "Otra obra", "Luis", "dos.png", 2, "38.50", $"{Isbn(10)};19.90;epub;Dominio público;dominio-publico;") + "\n"
        + Fila("tres", "Obra comercial", "Rosa", "tres.png", 3, "52.00", $"{Isbn(11)};24.90;epub;Editorial de ejemplo;comercial;2030-12-31") + "\n";

    private async Task<Resultado<ResumenDeLaCargaDelCatalogo>> Cargar(string archivo, IOrigenDeArchivosDeLaCarga? origen = null, ActorActual? actor = null)
    {
        await using var alcance = _host.Servicios.CreateAsyncScope();
        alcance.ServiceProvider.GetRequiredService<IEstablecedorDeActor>().Establecer(actor ?? ActorActual.DeUsuario(Operacion, RolesDeUsuario.AdministradorCentral));
        try
        {
            return await alcance.ServiceProvider.GetRequiredService<ICargaInicialDelCatalogo>().Cargar(new SolicitudDeCargaDelCatalogo(archivo, origen ?? new CarpetaDePortadasDeLaCarga(_carpeta)));
        }
        finally
        {
            alcance.ServiceProvider.GetRequiredService<IEstablecedorDeActor>().Limpiar();
        }
    }

    private async Task<int> NuevosPortadas() => await ObjetosEnElAlmacen(CatalogoApiHost.Portadas) - _portadasAlInicio;

    private async Task<int> NuevosLibros() => await ObjetosEnElAlmacen(CatalogoApiHost.Libros) - _librosAlInicio;

    private async Task<int> ObjetosEnElAlmacen(string contenedor)
    {
        var respuesta = await almacen.Cliente.ListObjectsV2Async(new ListObjectsV2Request { BucketName = contenedor });
        return respuesta.S3Objects?.Count ?? 0;
    }

    private Task<long> Contar(string tabla) => bd.Consultar<long>($"SELECT count(*) FROM catalogo.{tabla}");

    [Fact]
    [Trait("Criterio", "AC-CIN-01")]
    [Trait("Criterio", "AC-CIN-07")]
    public async Task Carga_tres_obras_con_descripcion_precio_y_portada_y_los_archivos_viven_solo_en_el_almacen()
    {
        var resultado = await Cargar(TresObras());

        Assert.True(resultado.EsExito);
        var resumen = resultado.Valor;
        Assert.Equal((3, 0, 0), (resumen.Cargadas, resumen.YaCargadas, resumen.Rechazadas));
        Assert.True(resumen.Completa);
        Assert.Equal(3, await Contar("obras"));
        Assert.Equal(5, await Contar("ediciones"));
        Assert.Equal(2, await Contar("licencias"));
        Assert.Equal(2, await Contar("archivos_digitales"));
        Assert.Equal(3, await NuevosPortadas());
        Assert.Equal(2, await NuevosLibros());

        // Lo que ve el cliente: la obra con su descripción, su portada y el precio de cada edición.
        await using var alcance = _host.Servicios.CreateAsyncScope();
        var consulta = alcance.ServiceProvider.GetRequiredService<IConsultaDeCatalogo>();
        var pagina = (await consulta.BuscarObras(new BusquedaDeObras(Tamano: 50))).Valor;
        Assert.Equal(3, pagina.Total);
        Assert.All(pagina.Obras, o =>
        {
            Assert.Equal("Una sinopsis breve.", o.Descripcion);
            Assert.Equal(64, o.PortadaHuella.Length);
            Assert.All(o.Ediciones, e => Assert.True(e.Precio > 0));
        });
        var comercial = pagina.Obras.Single(o => o.Titulo == "Obra comercial");
        Assert.Equal(new[] { 24.90m, 52.00m }, comercial.Ediciones.Select(e => e.Precio).OrderBy(p => p).ToArray());

        // La edición digital está a la venta (licencia vigente) y su archivo es el EPUB de muestra: dice que no es el libro.
        var referencia = alcance.ServiceProvider.GetRequiredService<IReferenciaDeArchivosDelCatalogo>();
        var digital = comercial.Ediciones.Single(e => e.Formato == FormatoDeEdicion.Digital);
        Assert.Equal(EstadoDeDisponibilidad.DisponibleAlInstante, digital.Disponibilidad.Estado);
        var archivo = (await referencia.ObtenerDeEdicion(digital.EdicionId)).Valor;
        Assert.Equal(FormatoDeArchivoDigital.Epub, archivo.Formato);
        using var objeto = await almacen.Cliente.GetObjectAsync(CatalogoApiHost.Libros, archivo.ClaveEnAlmacen);
        using var memoria = new MemoryStream();
        await objeto.ResponseStream.CopyToAsync(memoria);
        using var zip = new ZipArchive(memoria);
        using var contenido = new StreamReader(zip.GetEntry("OEBPS/muestra.xhtml")!.Open());
        Assert.Contains("no es el libro", await contenido.ReadToEndAsync(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-02")]
    public async Task Repetir_la_carga_informa_ya_cargadas_y_no_cambia_obras_ediciones_portadas_ni_objetos()
    {
        await Cargar(TresObras());
        var objetosAntes = (await NuevosPortadas(), await NuevosLibros());

        var resumen = (await Cargar(TresObras())).Valor;

        Assert.Equal((0, 3, 0), (resumen.Cargadas, resumen.YaCargadas, resumen.Rechazadas));
        Assert.Equal(3, await Contar("obras"));
        Assert.Equal(5, await Contar("ediciones"));
        Assert.Equal(objetosAntes, (await NuevosPortadas(), await NuevosLibros()));
        Assert.Equal(3, resumen.Plan.Ediciones.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-02")]
    public async Task Dos_ejecuciones_a_la_vez_sobre_un_catalogo_vacio_dejan_tres_obras_sin_duplicar()
    {
        var resultados = await Task.WhenAll(Cargar(TresObras()), Cargar(TresObras()));

        Assert.All(resultados, r => Assert.True(r.EsExito));
        Assert.Equal(3, await Contar("obras"));
        Assert.Equal(5, await Contar("ediciones"));
        Assert.Equal(3, await bd.Consultar<long>("SELECT count(DISTINCT isbn13) FROM catalogo.ediciones WHERE tipo = 'impresa'"));
        // Cada fila la cargó una de las dos ejecuciones y la otra la informó como ya cargada (o la rechazó por el ISBN repetido de la carrera): nunca se perdió una obra.
        Assert.All(Enumerable.Range(0, 3), i => Assert.Contains(resultados, r => r.Valor.Filas[i].Resultado == ResultadoDeFilaDeCarga.Cargada));
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-05")]
    public async Task Los_nombres_de_portada_hostiles_y_un_archivo_que_no_es_una_imagen_se_rechazan_sin_escribir_en_el_almacen()
    {
        await File.WriteAllTextAsync(Path.Combine(_carpeta, "texto.png"), "esto no es una imagen");
        var fuera = Path.Combine(Path.GetDirectoryName(_carpeta)!, "fuera-" + Guid.NewGuid().ToString("N") + ".png");
        await File.WriteAllBytesAsync(fuera, Png(60, 90, 1200));
        var enlace = Path.Combine(_carpeta, "enlace.png");
        var conEnlace = false;
        try
        {
            File.CreateSymbolicLink(enlace, fuera);
            conEnlace = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Sin privilegio para crear enlaces simbólicos (Windows sin modo de desarrollo): esa fila no se prueba aquí.
        }

        try
        {
            var archivo = Cabecera + "\n"
                + Fila("a", "Con ruta relativa", "A", "../otra.png", 1) + "\n"
                + Fila("b", "Con ruta absoluta", "B", fuera.Replace(';', '_'), 2) + "\n"
                + Fila("c", "Con texto", "C", "texto.png", 3) + "\n"
                + (conEnlace ? Fila("d", "Con enlace", "D", "enlace.png", 4) + "\n" : string.Empty);

            var resumen = (await Cargar(archivo)).Valor;

            Assert.Equal(conEnlace ? 4 : 3, resumen.Rechazadas);
            Assert.Equal(0, resumen.Cargadas);
            Assert.Equal(("portada", "portada-invalida"), (resumen.Filas[0].Campo, resumen.Filas[0].Motivo));
            Assert.Equal(("portada", "portada-invalida"), (resumen.Filas[1].Campo, resumen.Filas[1].Motivo));
            Assert.Equal("portada", resumen.Filas[2].Campo);
            if (conEnlace)
            {
                Assert.Equal(("portada", "portada-ausente"), (resumen.Filas[3].Campo, resumen.Filas[3].Motivo));
            }

            Assert.Equal(0, await Contar("obras"));
            Assert.Equal(0, await NuevosPortadas());
        }
        finally
        {
            File.Delete(fuera);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-06")]
    public async Task Cada_alta_queda_auditada_con_la_identidad_de_la_carga_y_la_corrida_con_la_huella_del_archivo_sin_el_contenido()
    {
        var archivo = TresObras();
        var obrasAntes = await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'ObraRegistrada' AND actor_id = '{Operacion:D}'");
        var edicionesAntes = await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'EdicionRegistrada' AND actor_id = '{Operacion:D}'");

        var resumen = (await Cargar(archivo)).Valor;

        Assert.Equal(obrasAntes + 3, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'ObraRegistrada' AND actor_id = '{Operacion:D}'"));
        Assert.Equal(edicionesAntes + 5, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'EdicionRegistrada' AND actor_id = '{Operacion:D}'"));
        var detalle = await bd.Consultar<string>($"SELECT detalle::text FROM auditoria.registros WHERE accion = 'CargaInicialEjecutada' AND actor_id = '{Operacion:D}' ORDER BY instante_utc DESC LIMIT 1");
        Assert.NotNull(detalle);
        Assert.Contains(resumen.CorridaId.ToString("D"), detalle, StringComparison.Ordinal);
        Assert.Contains("cargadas", detalle, StringComparison.Ordinal);
        Assert.Equal(resumen.HuellaDelTexto[..16], await bd.Consultar<string>($"SELECT detalle->>'huellaSha256' FROM auditoria.registros WHERE accion = 'CargaInicialEjecutada' AND actor_id = '{Operacion:D}' ORDER BY instante_utc DESC LIMIT 1"));
        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'CargaInicialEjecutada' AND (detalle::text ILIKE '%sinopsis%' OR detalle::text ILIKE '%.png%')"));
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-06")]
    public async Task Con_la_auditoria_caida_ninguna_fila_se_carga_y_al_volver_se_puede_repetir()
    {
        _host.AuditoriaCaida = true;

        var resumen = (await Cargar(TresObras())).Valor;

        Assert.Equal(3, resumen.Rechazadas);
        Assert.All(resumen.Filas, f => Assert.Equal(("auditoria", "auditoria-no-disponible"), (f.Campo, f.Motivo)));
        Assert.True(resumen.RegistroDeLaCorridaFallo);
        Assert.False(resumen.Completa);
        Assert.Equal(0, await Contar("obras"));
        Assert.Equal(0, await Contar("ediciones"));

        _host.AuditoriaCaida = false;
        var repetida = (await Cargar(TresObras())).Valor;
        Assert.Equal(3, repetida.Cargadas);
        Assert.Equal(3, await Contar("obras"));
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-06")]
    public async Task Quien_no_es_la_administracion_central_es_rechazado_con_un_acceso_denegado_y_no_se_escribe_nada()
    {
        var antes = await bd.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'AccesoDenegado'");

        var resultado = await Cargar(TresObras(), actor: ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, Guid.NewGuid()));

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(antes + 1, await bd.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'AccesoDenegado'"));
        Assert.Equal(0, await Contar("obras"));
        Assert.Equal(0, await NuevosPortadas());
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-13")]
    public async Task Con_el_almacen_de_objetos_caido_el_resumen_solo_lleva_claves_y_ninguna_fila_se_carga()
    {
        await _host.DisposeAsync();
        _host = await CatalogoApiHost.Crear(
            bd,
            new Amazon.S3.AmazonS3Client(
                new Amazon.Runtime.BasicAWSCredentials("acceso-de-pruebas-0123456789", "secreta-de-pruebas-0123456789-abcdefghijkl"),
                new Amazon.S3.AmazonS3Config { ServiceURL = "http://127.0.0.1:1", ForcePathStyle = true, MaxErrorRetry = 0, Timeout = TimeSpan.FromSeconds(5), AuthenticationRegion = "us-east-1" }),
            servicios => servicios.AgregarCargaInicialDelCatalogo(),
            crearContenedores: false);

        var resumen = (await Cargar(TresObras())).Valor;

        Assert.False(resumen.Completa);
        Assert.Equal(3, resumen.Rechazadas);
        Assert.All(resumen.Filas, f =>
        {
            Assert.Equal(("almacen", "almacen-no-disponible"), (f.Campo, f.Motivo));
            Assert.DoesNotContain("http", f.Motivo, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(0, await Contar("obras"));
        Assert.Equal(Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(resumen.Plan.ComoJson())), resumen.Plan.ComoJson());
    }
}
