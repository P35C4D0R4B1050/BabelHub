using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web.Interno;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>Al restaurar un respaldo ninguna sesión web restaurada sigue válida (T08, D14, PLA-39, PLA-43).</summary>
public sealed class PasoDeRestauracionDeSesionesTests
{
    private sealed class VaciadoFalso(bool falla = false) : IVaciadoDeLaWeb
    {
        public int Llamadas { get; private set; }

        public Task VaciarSesionesYBorradores(CancellationToken cancelacion = default)
        {
            Llamadas++;
            return falla ? throw new InvalidOperationException("la base no responde") : Task.CompletedTask;
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task El_paso_vacia_las_sesiones_y_los_borradores_una_vez()
    {
        var vaciado = new VaciadoFalso();

        var resultado = await new PasoDeRestauracionDeSesionesWeb(vaciado).Ejecutar();

        Assert.Equal(ResultadoDePaso.Correcto, resultado);
        Assert.Equal(1, vaciado.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Si_no_se_puede_vaciar_el_servicio_no_se_abre()
    {
        var ejecutor = new EjecutorDePasosDeRestauracion([new PasoDeRestauracionDeSesionesWeb(new VaciadoFalso(falla: true))]);

        var resultado = await ejecutor.Ejecutar();

        Assert.False(resultado.PermiteAbrir);
        Assert.Equal(PasoDeRestauracionDeSesionesWeb.NombreDelPaso, resultado.PasoFallido);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public void El_paso_corre_despues_de_Identidad_y_antes_de_Auditoria_y_su_nombre_no_lleva_datos()
    {
        var paso = new PasoDeRestauracionDeSesionesWeb(new VaciadoFalso());

        Assert.InRange(paso.Orden, OrdenDePasoDeRestauracion.Identidad + 1, OrdenDePasoDeRestauracion.Auditoria - 1);
        Assert.Equal("web-vaciar-sesiones", paso.Nombre);
    }
}
