namespace BabelHub.IntegrationTests.Almacen;

/// <summary>
/// PLA-16, PLA-35 y PLA-38 (AC-PLA-16, AC-PLA-35): la definición de las credenciales y de la retención del almacén. La credencial de
/// respaldo solo agrega; cada credencial rechaza todo lo ajeno (los libros no tocan los respaldos y los respaldos no tocan los libros, el
/// servicio no escribe sellos); nadie salvo el catálogo (sobre los libros y las portadas) borra o sobrescribe, y la biblioteca solo lee los libros; y el registro de supresiones y el archivo de cambios se
/// retienen con bloqueo al menos 100 días. Que el almacén de Oracle aplique esto lo comprueba <c>scripts/almacen.sh comprobar</c>.
/// </summary>
public sealed class PoliticasDelAlmacenTests
{
    private static readonly PoliticasDelAlmacen Politicas = PoliticasDelAlmacen.Cargar();

    private static readonly string[] ContenedoresDelCatalogo = ["libros", "portadas"];

    private static readonly string[] Respaldos = ["respaldos-diarios", "respaldos-semanales", "respaldos-mensuales", "respaldos-wal"];

    [Fact]
    public void LasCredencialesSonLasDelPlan_YCadaContenedorLoUsaAlguien()
    {
        Assert.Equal(
            ["biblioteca-lector", "catalogo-archivos", "cuota-lectura", "respaldos-copia-registro", "respaldos-escritor", "respaldos-lector", "respaldos-vigilante", "supresiones-registro", "supresiones-sello"],
            [.. Politicas.Credenciales.Order(StringComparer.Ordinal)]);
        Assert.All(Politicas.Contenedores, c => Assert.Contains(Politicas.Concesiones, g => g.Contenedor == c.Nombre));
    }

    [Theory]
    [MemberData(nameof(ContenedoresDeRespaldo))]
    [Trait("Criterio", "AC-PLA-16")]
    public void LaCredencialDeRespaldo_AgregaUnRespaldoNuevo_PeroNoBorraNiSobrescribeNiLee(string contenedor)
    {
        Assert.True(Politicas.Permite("respaldos-escritor", "crear", contenedor));
        Assert.False(Politicas.Permite("respaldos-escritor", "borrar", contenedor));
        Assert.False(Politicas.Permite("respaldos-escritor", "sobrescribir", contenedor));
        Assert.False(Politicas.Permite("respaldos-escritor", "leer", contenedor));
        Assert.False(Politicas.Permite("respaldos-escritor", "listar", contenedor));
    }

    public static TheoryData<string> ContenedoresDeRespaldo => new(Respaldos);

    [Fact]
    [Trait("Criterio", "AC-PLA-16")]
    public void LaCredencialDeRespaldo_NoTocaNingunOtroContenedor()
    {
        foreach (var contenedor in Politicas.Contenedores.Select(c => c.Nombre).Except(Respaldos))
        {
            Assert.All(PoliticasDelAlmacen.Acciones, accion => Assert.False(Politicas.Permite("respaldos-escritor", accion, contenedor), $"respaldos-escritor puede {accion} en {contenedor}"));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-16")]
    public void NingunaCredencialDeRespaldosNiDeSupresionesBorraOSobrescribe_PorqueElRetiroLoHaceElAlmacen()
    {
        var conBorrado = Politicas.Concesiones
            .Where(g => g.Acciones.Contains("borrar") || g.Acciones.Contains("sobrescribir"))
            .Select(g => g.Credencial)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["catalogo-archivos"], conBorrado);
        Assert.All(Politicas.Concesiones.Where(g => g.Acciones.Contains("borrar") || g.Acciones.Contains("sobrescribir")), g => Assert.Contains(g.Contenedor, ContenedoresDelCatalogo));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-35")]
    public void LasCredencialesDelCatalogoYLaBiblioteca_NoLeenNiEscribenNiBorranEnNingunContenedorDeRespaldosNiDeSupresiones()
    {
        foreach (var credencial in new[] { "catalogo-archivos", "biblioteca-lector" })
        {
            foreach (var contenedor in Politicas.Contenedores.Select(c => c.Nombre).Where(n => !ContenedoresDelCatalogo.Contains(n)))
            {
                Assert.All(PoliticasDelAlmacen.Acciones, accion => Assert.False(Politicas.Permite(credencial, accion, contenedor), $"{credencial} puede {accion} en {contenedor}"));
            }
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-35")]
    public void LasCredencialesDeRespaldoYDeSupresiones_NoTocanLosLibrosNiLasPortadas_SalvoMedirSuTamano()
    {
        foreach (var credencial in Politicas.Credenciales.Where(c => c is not ("catalogo-archivos" or "biblioteca-lector")))
        {
            foreach (var contenedor in ContenedoresDelCatalogo)
            {
                var permitidas = PoliticasDelAlmacen.Acciones.Where(a => Politicas.Permite(credencial, a, contenedor)).ToList();
                Assert.True(permitidas.Count == 0 || (credencial == "cuota-lectura" && permitidas.SequenceEqual(["uso"])), $"{credencial} toca {contenedor}: {string.Join(", ", permitidas)}");
            }
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void ElCatalogoCreaLeeYBorraSusArchivosYPortadas_SinSobrescribirNiListar_YLaBibliotecaSoloLeeLosLibros()
    {
        foreach (var contenedor in ContenedoresDelCatalogo)
        {
            Assert.True(Politicas.Permite("catalogo-archivos", "crear", contenedor));
            Assert.True(Politicas.Permite("catalogo-archivos", "leer", contenedor));
            Assert.True(Politicas.Permite("catalogo-archivos", "borrar", contenedor));
            Assert.False(Politicas.Permite("catalogo-archivos", "sobrescribir", contenedor));
            Assert.False(Politicas.Permite("catalogo-archivos", "listar", contenedor));
        }

        Assert.True(Politicas.Permite("biblioteca-lector", "leer", "libros"));
        Assert.Equal(["leer"], PoliticasDelAlmacen.Acciones.Where(a => Politicas.Permite("biblioteca-lector", a, "libros")));
        Assert.All(PoliticasDelAlmacen.Acciones, a => Assert.False(Politicas.Permite("biblioteca-lector", a, "portadas"), $"biblioteca-lector puede {a} en portadas"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void LosContenedoresDelCatalogo_NoExpiranNiSeBloquean_YAbortanLasSubidasMultipartIncompletas()
    {
        foreach (var nombre in ContenedoresDelCatalogo)
        {
            var contenedor = Politicas.Contenedor(nombre);
            Assert.Equal(0, contenedor.RetencionDias);
            Assert.False(contenedor.Bloqueada);
            Assert.Null(contenedor.ExpiraDias);
            Assert.Equal(1, contenedor.SubidasIncompletasDias);
        }

        Assert.All(Politicas.Contenedores.Where(c => !ContenedoresDelCatalogo.Contains(c.Nombre)), c => Assert.Null(c.SubidasIncompletasDias));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void ElServicioDeIdentidad_AgregaYLeeEntradas_PeroNoEscribeSellosNiTocaLoDemas()
    {
        Assert.True(Politicas.Permite("supresiones-registro", "crear", "supresiones-entradas"));
        Assert.True(Politicas.Permite("supresiones-registro", "leer", "supresiones-entradas"));
        Assert.False(Politicas.Permite("supresiones-registro", "borrar", "supresiones-entradas"));
        Assert.False(Politicas.Permite("supresiones-registro", "sobrescribir", "supresiones-entradas"));

        // Un servicio comprometido no puede pre-crear sellos futuros ni leer o alterar lo demás.
        foreach (var contenedor in Politicas.Contenedores.Select(c => c.Nombre).Where(n => n != "supresiones-entradas"))
        {
            Assert.All(PoliticasDelAlmacen.Acciones, accion => Assert.False(Politicas.Permite("supresiones-registro", accion, contenedor), $"supresiones-registro puede {accion} en {contenedor}"));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void VmAux_EscribeSellosYCopiasYSoloLeeLasEntradas_SinPoderAlterarlas()
    {
        Assert.True(Politicas.Permite("supresiones-sello", "crear", "supresiones-sellos"));
        Assert.True(Politicas.Permite("supresiones-sello", "leer", "supresiones-entradas"));
        Assert.False(Politicas.Permite("supresiones-sello", "crear", "supresiones-entradas"));
        Assert.False(Politicas.Permite("supresiones-sello", "leer", "supresiones-sellos"));
        Assert.True(Politicas.Permite("respaldos-copia-registro", "crear", "supresiones-copia"));
        Assert.False(Politicas.Permite("respaldos-copia-registro", "crear", "supresiones-entradas"));
        Assert.False(Politicas.Permite("respaldos-copia-registro", "leer", "supresiones-copia"));
        Assert.False(Politicas.Permite("respaldos-vigilante", "crear", "respaldos-diarios"));
        Assert.False(Politicas.Permite("respaldos-vigilante", "leer", "respaldos-diarios"));
        Assert.True(Politicas.Permite("respaldos-vigilante", "listar", "respaldos-diarios"));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-16")]
    public void SoloLaPersonaResponsableLeeLosRespaldos_NingunaCredencialDeUnaVmDeNegocioPuede()
    {
        var quienLee = Politicas.Credenciales.Where(c => Respaldos.Any(r => Politicas.Permite(c, "leer", r))).ToList();

        Assert.Equal(["respaldos-lector"], quienLee);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void ElRegistroDeSupresionesYElArchivoDeCambios_SeRetienenConBloqueoAlMenos100Dias()
    {
        foreach (var nombre in new[] { "respaldos-wal", "supresiones-entradas", "supresiones-sellos", "supresiones-copia" })
        {
            var contenedor = Politicas.Contenedor(nombre);
            Assert.True(contenedor.RetencionDias >= 100, $"{nombre}: {contenedor.RetencionDias} días");
            Assert.True(contenedor.Bloqueada, $"{nombre} sin bloqueo");
        }

        Assert.Null(Politicas.Contenedor("supresiones-entradas").ExpiraDias);
        Assert.Null(Politicas.Contenedor("supresiones-sellos").ExpiraDias);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-16")]
    public void LosRespaldosSeRetienenConBloqueoSegunSuClase_YElCicloDeVidaNuncaLlegaAntesQueLaRetencion()
    {
        Assert.Equal([7, 28, 92, 100], [.. Respaldos.Select(r => Politicas.Contenedor(r).RetencionDias)]);
        Assert.All(Respaldos, r => Assert.True(Politicas.Contenedor(r).Bloqueada));
        Assert.All(Politicas.Contenedores.Where(c => c.ExpiraDias is not null), c => Assert.True(c.ExpiraDias >= c.RetencionDias, c.Nombre));
        Assert.False(Politicas.Contenedor("libros").Bloqueada);
        Assert.Equal(0, Politicas.Contenedor("libros").RetencionDias);
    }

    [Fact]
    public void TodaConcesionNombraUnContenedorQueExiste_YSoloAccionesConocidas()
    {
        var nombres = Politicas.Contenedores.Select(c => c.Nombre).ToHashSet(StringComparer.Ordinal);

        Assert.All(Politicas.Concesiones, g =>
        {
            Assert.Contains(g.Contenedor, nombres);
            Assert.All(g.Acciones, a => Assert.Contains(a, PoliticasDelAlmacen.Acciones));
        });
    }
}
