using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

public sealed class ReservaYUmbralesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReservaDeLimite_ConDatosValidos_IdentificaElCupo()
    {
        var reserva = new ReservaDeLimite("ingresos-fallidos", "c:abc", "e1", Ahora, claveDelLlamador: "aviso-7");

        Assert.Equal("ingresos-fallidos", reserva.Politica);
        Assert.Equal("c:abc", reserva.Sujeto);
        Assert.Equal("e1", reserva.Identificador);
        Assert.Equal(Ahora, reserva.ReservadaEn);
        Assert.Equal("aviso-7", reserva.ClaveDelLlamador);
        Assert.Null(new ReservaDeLimite("p", "s", "i", Ahora).ClaveDelLlamador);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-09")]
    public void AccesosDenegados_ConDiezEnCincoMinutos_NoAlertaYConOnceSi()
    {
        var umbral = UmbralesDeAlerta.AccesosDenegados;

        Assert.Equal(TimeSpan.FromMinutes(5), umbral.Ventana);
        Assert.False(umbral.Supera(10));
        Assert.True(umbral.Supera(11));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-09")]
    public void IngresosFallidos_ConVeinteEnCincoMinutos_NoAlertaYConVeintiunoSi()
    {
        var umbral = UmbralesDeAlerta.IngresosFallidos;

        Assert.Equal(TimeSpan.FromMinutes(5), umbral.Ventana);
        Assert.False(umbral.Supera(20));
        Assert.True(umbral.Supera(21));
        Assert.False(umbral.Supera(0));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-09")]
    public void Umbrales_TienenMotivosEstables()
    {
        Assert.Equal(["accesos-denegados", "ingresos-fallidos"], UmbralesDeAlerta.Todos.Select(u => u.Motivo));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-03")]
    public void CategoriaDeError_TieneLasNueveCategoriasDeLaSpec()
    {
        var esperadas = new[]
        {
            "Validacion", "ReglaDeNegocio", "NoAutenticado", "Prohibido", "NoEncontrado",
            "Conflicto", "Limite", "Inesperado", "NoDisponible",
        };

        Assert.Equal(esperadas.Order(), Enum.GetNames<CategoriaDeError>().Order());
    }
}
