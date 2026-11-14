using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.Configuration;

namespace BabelHub.IntegrationTests.Plataforma;

/// <summary>
/// El registro de supresiones sobre un almacén de objetos S3 real (PLA-37, PLA-38, AC-PLA-37): clave determinista, contenido sin datos
/// personales, solo agregar, un duplicado (también simultáneo) es éxito y no cambia lo escrito, y si el almacén no responde la
/// supresión no se completa.
/// </summary>
[Collection(AlmacenDeObjetosCollection.Nombre)]
public sealed class RegistroDeSupresionesEnObjetosTests(AlmacenDeObjetosFixture almacen)
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed class RelojDePrueba(DateTimeOffset ahora) : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private RegistroDeSupresionesEnObjetos Registro() => new(almacen.Cliente, AlmacenDeObjetosFixture.Contenedor);

    /// <summary>Un identificador distinto por prueba: el almacén se comparte entre ellas.</summary>
    private static string Cuenta() => Guid.NewGuid().ToString("N");

    private async Task<string> Texto(string clave)
    {
        using var respuesta = await almacen.Cliente.GetObjectAsync(AlmacenDeObjetosFixture.Contenedor, clave);
        using var lector = new StreamReader(respuesta.ResponseStream);
        return await lector.ReadToEndAsync();
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public async Task LaIntencionSeEscribeConLaClaveDeterministaYUnContenidoSinDatosPersonales()
    {
        var cuenta = Cuenta();

        var escrita = await Registro().Agregar(cuenta, ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Intencion, Inicio);

        Assert.False(escrita.YaExistia);
        var contenido = JsonDocument.Parse(await Texto($"entradas/{cuenta}/cancelacion-intencion.json")).RootElement;
        Assert.Equal(
            ["fase", "fecha", "identificadorOpaco", "suma", "tipo"],
            contenido.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(cuenta, contenido.GetProperty("identificadorOpaco").GetString());
        Assert.Equal("cancelacion", contenido.GetProperty("tipo").GetString());
        Assert.Equal("intencion", contenido.GetProperty("fase").GetString());
        Assert.Equal("2026-11-13T12:00:00.0000000Z", contenido.GetProperty("fecha").GetString());
        Assert.Equal(escrita.Suma, contenido.GetProperty("suma").GetString());
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public async Task UnaEntradaRepetida_EsExitoYNoCambiaLoEscrito()
    {
        var cuenta = Cuenta();
        var registro = Registro();
        var primera = await registro.Agregar(cuenta, ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Intencion, Inicio);
        var antes = await Texto($"entradas/{cuenta}/cancelacion-intencion.json");

        var repetida = await registro.Agregar(cuenta, ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Intencion, Inicio.AddHours(5));

        Assert.True(repetida.YaExistia);
        Assert.Equal(Inicio, repetida.Fecha);
        Assert.Equal(primera.Suma, repetida.Suma);
        Assert.Equal(antes, await Texto($"entradas/{cuenta}/cancelacion-intencion.json"));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public async Task VeintePeticionesSimultaneasDelMismoIdentificador_SeEscribeUnaVezYTodasSonExitosas()
    {
        var cuenta = Cuenta();
        var registro = Registro();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            Task.Run(() => registro.Agregar(cuenta, ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Intencion, Inicio.AddMinutes(i)))));

        Assert.Equal(1, resultados.Count(r => !r.YaExistia));
        var ganadora = resultados.Single(r => !r.YaExistia);
        Assert.All(resultados, r => Assert.Equal(ganadora.Fecha, r.Fecha));
        Assert.All(resultados, r => Assert.Equal(ganadora.Suma, r.Suma));
        var contenido = JsonDocument.Parse(await Texto($"entradas/{cuenta}/cancelacion-intencion.json")).RootElement;
        Assert.Equal(ganadora.Suma, contenido.GetProperty("suma").GetString());
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public async Task ObtenerDevuelveLaEntradaOnullSiNoExiste()
    {
        var cuenta = Cuenta();
        var registro = Registro();

        Assert.Null(await registro.Obtener(cuenta, ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Intencion));
        await registro.Agregar(cuenta, ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Intencion, Inicio);

        var obtenida = await registro.Obtener(cuenta, ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Intencion);
        Assert.Equal(Inicio, obtenida?.Fecha);
        Assert.Null(await registro.Obtener(cuenta, ClaseDeSupresion.Anonimizacion, EtapaDeSupresion.Intencion));
        Assert.Null(await registro.Obtener(cuenta, ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Cierre));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public async Task ElFlujoCompletoConLosCasosDeUso_IntencionLuegoCierreConSusFechas()
    {
        var cuenta = Cuenta();
        var reloj = new RelojDePrueba(Inicio);
        var registro = Registro();
        var intencion = new AgregarIntencionDeSupresion(registro, reloj);
        var cierre = new CerrarSupresion(registro, reloj);

        var sinIntencion = await cierre.Ejecutar(new SupresionDeCuenta(cuenta, ClaseDeSupresion.Cancelacion));
        var conIntencion = await intencion.Ejecutar(new SupresionDeCuenta(cuenta, ClaseDeSupresion.Cancelacion));
        reloj.Ahora = Inicio.AddMinutes(7);
        var cerrada = await cierre.Ejecutar(new SupresionDeCuenta(cuenta, ClaseDeSupresion.Cancelacion));

        Assert.Equal("supresion-sin-intencion", sinIntencion.Error!.Clave);
        Assert.Equal(Inicio, conIntencion.Valor.Fecha);
        Assert.Equal(Inicio.AddMinutes(7), cerrada.Valor.Fecha);
        Assert.NotNull(await almacen.Cliente.GetObjectMetadataAsync(AlmacenDeObjetosFixture.Contenedor, $"entradas/{cuenta}/cancelacion-cierre.json"));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task LeerConMetadatos_ListaLasEntradasConSuHoraDeModificacionEnElAlmacen()
    {
        var cuenta = Cuenta();
        var registro = Registro();
        await registro.Agregar(cuenta, ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Intencion, Inicio);
        await registro.Agregar(cuenta, ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Cierre, Inicio.AddMinutes(1));

        var entradas = (await registro.LeerConMetadatos()).Where(e => e.Entrada.IdentificadorOpaco == cuenta).ToList();

        Assert.Equal(
            [$"entradas/{cuenta}/cancelacion-cierre.json", $"entradas/{cuenta}/cancelacion-intencion.json"],
            entradas.Select(e => e.ClaveEnElAlmacen));
        Assert.All(entradas, e => Assert.InRange(e.ModificadaEn, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task LeerConMetadatos_RecorreMasDeUnaPaginaDeResultados()
    {
        var registro = Registro();
        var prefijo = Guid.NewGuid().ToString("N");
        // Más de mil objetos propios: el listado se parte en páginas y el registro debe recorrerlas todas.
        await Task.WhenAll(Enumerable.Range(0, 1010).Select(i =>
            almacen.Cliente.PutObjectAsync(new PutObjectRequest
            {
                BucketName = AlmacenDeObjetosFixture.Contenedor,
                Key = $"entradas/{prefijo}-{i:D5}/cancelacion-intencion.json",
                ContentBody = "{}",
            })));

        var propias = (await registro.LeerConMetadatos()).Count(e => e.ClaveEnElAlmacen.StartsWith($"entradas/{prefijo}-", StringComparison.Ordinal));

        Assert.Equal(1010, propias);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task UnObjetoQueNoSePuedeInterpretar_SeVerificaComoEntradaAlterada()
    {
        var cuenta = Cuenta();
        var registro = Registro();
        await registro.Agregar(cuenta, ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Intencion, Inicio);
        await almacen.Cliente.PutObjectAsync(new PutObjectRequest
        {
            BucketName = AlmacenDeObjetosFixture.Contenedor,
            Key = $"entradas/{cuenta}/cancelacion-cierre.json",
            ContentBody = "esto no es json",
        });

        var sinSellos = new SellosVacios();
        var resultado = await new VerificarRegistroDeSupresiones(registro, sinSellos, new SinExcepciones()).Ejecutar(DateTimeOffset.UtcNow);

        Assert.Equal(EstadoDelRegistro.EntradaAlterada, resultado.Valor.Estado);
        Assert.Contains($"entradas/{cuenta}/cancelacion-cierre.json", resultado.Valor.ClavesAfectadas);
        Assert.False(resultado.Valor.PermiteAbrir);
    }

    private sealed class SellosVacios : ISellosDelRegistro
    {
        public Task<SelloDelRegistroDto?> ObtenerAnteriorA(DateTimeOffset instante, CancellationToken cancelacion = default) => Task.FromResult<SelloDelRegistroDto?>(null);
    }

    private sealed class SinExcepciones : IExcepcionesDelRegistro
    {
        public Task<ExcepcionDelRegistroDto?> Obtener(DateTimeOffset instanteDelRespaldo, CancellationToken cancelacion = default) => Task.FromResult<ExcepcionDelRegistroDto?>(null);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public async Task UnObjetoIlegibleAlPedirUnaEntradaConcreta_SeInformaComoNoDisponible()
    {
        var cuenta = Cuenta();
        await almacen.Cliente.PutObjectAsync(new PutObjectRequest
        {
            BucketName = AlmacenDeObjetosFixture.Contenedor,
            Key = $"entradas/{cuenta}/cancelacion-intencion.json",
            ContentBody = "{\"tipo\":\"otra-cosa\"}",
        });

        await Assert.ThrowsAsync<RegistroDeSupresionesNoDisponibleException>(
            () => Registro().Obtener(cuenta, ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Intencion));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public async Task SiElAlmacenNoResponde_LaEscrituraLaLecturaYElListadoFallanComoNoDisponible_YLaSupresionNoSeCompleta()
    {
        var sinServidor = ClienteDelAlmacenDeObjetos.Crear(almacen.Opciones with { Endpoint = new Uri("http://127.0.0.1:1") });
        var registro = new RegistroDeSupresionesEnObjetos(sinServidor, AlmacenDeObjetosFixture.Contenedor);
        var reloj = new RelojDePrueba(Inicio);

        await Assert.ThrowsAsync<RegistroDeSupresionesNoDisponibleException>(() => registro.Agregar(Cuenta(), ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Intencion, Inicio));
        await Assert.ThrowsAsync<RegistroDeSupresionesNoDisponibleException>(() => registro.Obtener(Cuenta(), ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Intencion));
        await Assert.ThrowsAsync<RegistroDeSupresionesNoDisponibleException>(() => registro.LeerConMetadatos());
        var resultado = await new AgregarIntencionDeSupresion(registro, reloj).Ejecutar(new SupresionDeCuenta(Cuenta(), ClaseDeSupresion.Cancelacion));
        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public async Task UnIdentificadorQueNoEsElInterno_SeRechazaSinEscribirNada()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Registro().Agregar("lector@correo.pe", ClaseDeSupresion.Cancelacion, EtapaDeSupresion.Intencion, Inicio));

        var listado = await almacen.Cliente.ListObjectsV2Async(new ListObjectsV2Request { BucketName = AlmacenDeObjetosFixture.Contenedor, Prefix = "entradas/lector" });
        Assert.Empty(listado.S3Objects ?? []);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-38")]
    public void ElRegistroSoloAgrega_NuncaBorraNiCopiaNiMueveObjetos()
    {
        var fuente = File.ReadAllText(Path.Combine(RaizDelRepositorio(), "src", "BabelHub.Ecommerce", "BabelHub.Ecommerce.Infrastructure", "Plataforma", "RegistroDeSupresionesEnObjetos.cs"));

        foreach (var prohibido in new[] { "DeleteObject", "DeleteObjects", "CopyObject", "CopyPart", "PutACL", "PutBucket", "DeleteBucket", "AbortMultipartUpload" })
        {
            Assert.DoesNotContain(prohibido, fuente, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("IfNoneMatch = \"*\"", fuente, StringComparison.Ordinal);
    }

    private static string RaizDelRepositorio()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "BabelHub.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    // ---------- Configuración ----------

    private static IConfiguration Configuracion(params (string Ruta, string Valor)[] valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores.ToDictionary(v => v.Ruta, v => (string?)v.Valor)).Build();

    private static readonly (string, string)[] Completa =
    [
        ("Almacen:Supresiones:Endpoint", "https://almacen.ejemplo.pe"),
        ("Almacen:Supresiones:Region", "sa-lima-1"),
        ("Almacen:Supresiones:ClaveDeAcceso", "acceso-" + Guid.NewGuid().ToString("N") + "-de-prueba"),
        ("Almacen:Supresiones:ClaveSecreta", "secreta-" + Guid.NewGuid().ToString("N") + "-de-prueba"),
    ];

    [Fact]
    public void LaConfiguracionCompleta_SeCargaConLaClaveSecretaOculta()
    {
        var opciones = OpcionesDelAlmacenDeObjetos.Cargar(Configuracion(Completa));

        Assert.Equal(new Uri("https://almacen.ejemplo.pe"), opciones.Endpoint);
        Assert.Equal("sa-lima-1", opciones.Region);
        Assert.Equal("supresiones", opciones.Contenedor);
        Assert.DoesNotContain("secreta-", opciones.ClaveSecreta.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SiFaltaUnaVariableOEsDebilOElEndpointNoEsHttps_FallaNombrandoLaVariableYNuncaElValor()
    {
        var incompleta = Completa.Where(v => !v.Item1.EndsWith("Region", StringComparison.Ordinal)).Select(v => v.Item1.EndsWith("ClaveSecreta", StringComparison.Ordinal) ? (v.Item1, "corta") : v).ToArray();
        var sinHttps = Completa.Select(v => v.Item1.EndsWith("Endpoint", StringComparison.Ordinal) ? (v.Item1, "http://almacen.ejemplo.pe") : v).ToArray();

        var error = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => OpcionesDelAlmacenDeObjetos.Cargar(Configuracion(incompleta)));
        var errorHttp = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => OpcionesDelAlmacenDeObjetos.Cargar(Configuracion(sinHttps)));

        Assert.Contains("BABELHUB__Almacen__Supresiones__Region", error.Message, StringComparison.Ordinal);
        Assert.Contains("BABELHUB__Almacen__Supresiones__ClaveSecreta", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("corta", error.Message.Replace("corta de", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("BABELHUB__Almacen__Supresiones__Endpoint", errorHttp.Message, StringComparison.Ordinal);
        Assert.NotNull(OpcionesDelAlmacenDeObjetos.Cargar(Configuracion(sinHttps), permitirHttp: true));
    }
}
