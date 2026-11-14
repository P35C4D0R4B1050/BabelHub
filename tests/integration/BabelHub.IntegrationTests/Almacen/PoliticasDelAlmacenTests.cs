namespace BabelHub.IntegrationTests.Almacen;

/// <summary>
/// PLA-16, PLA-35 y PLA-38 (AC-PLA-16, AC-PLA-35): la definición de las credenciales y de la retención del almacén. La credencial de
/// respaldo solo agrega; cada credencial rechaza todo lo ajeno (los libros no tocan los respaldos y los respaldos no tocan los libros, el
/// servicio no escribe sellos); nadie salvo los libros borra o sobrescribe; y el registro de supresiones y el archivo de cambios se
/// retienen con bloqueo al menos 100 días. Que el almacén de Oracle aplique esto lo comprueba <c>scripts/almacen.sh comprobar</c>.
/// </summary>
public sealed class PoliticasDelAlmacenTests
{
    private static readonly PoliticasDelAlmacen Politicas = PoliticasDelAlmacen.Cargar();

    private static readonly string[] Respaldos = ["respaldos-diarios", "respaldos-semanales", "respaldos-mensuales", "respaldos-wal"];

    [Fact]
    public void LasCredencialesSonLasDelPlan_YCadaContenedorLoUsaAlguien()
    {
        Assert.Equal(
            ["cuota-lectura", "libros", "respaldos-copia-registro", "respaldos-escritor", "respaldos-lector", "respaldos-vigilante", "supresiones-registro", "supresiones-sello"],
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

        Assert.Equal(["libros"], conBorrado);
        Assert.All(Politicas.Concesiones.Where(g => g.Acciones.Contains("borrar") || g.Acciones.Contains("sobrescribir")), g => Assert.Equal("libros", g.Contenedor));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-35")]
    public void LaCredencialDeLosLibros_NoLeeNiEscribeNiBorraEnNingunContenedorDeRespaldos()
    {
        foreach (var contenedor in Politicas.Contenedores.Select(c => c.Nombre).Where(n => n != "libros"))
        {
            Assert.All(PoliticasDelAlmacen.Acciones, accion => Assert.False(Politicas.Permite("libros", accion, contenedor), $"libros puede {accion} en {contenedor}"));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-35")]
    public void LasCredencialesDeRespaldo_NoTocanLosLibros_SalvoMedirSuTamano()
    {
        foreach (var credencial in Politicas.Credenciales.Where(c => c != "libros"))
        {
            var permitidas = PoliticasDelAlmacen.Acciones.Where(a => Politicas.Permite(credencial, a, "libros")).ToList();
            Assert.True(permitidas.Count == 0 || (credencial == "cuota-lectura" && permitidas.SequenceEqual(["uso"])), $"{credencial} toca los libros: {string.Join(", ", permitidas)}");
        }
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
