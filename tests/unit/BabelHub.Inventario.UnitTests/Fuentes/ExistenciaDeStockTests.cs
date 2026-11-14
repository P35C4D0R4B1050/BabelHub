using BabelHub.Inventario.Domain.Fuentes;

namespace BabelHub.Inventario.UnitTests.Fuentes;

public sealed class ExistenciaDeStockTests
{
    [Fact]
    [Trait("Criterio", "AC-FUE-01")]
    public void Vendible_ConFisico9YUnaReservaConfirmadaDe1_Es8()
    {
        var existencia = new ExistenciaDeStock(fisico: 9, reservado: 1);

        Assert.Equal(9, existencia.Fisico);
        Assert.Equal(1, existencia.Reservado);
        Assert.Equal(8, existencia.Vendible());
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public void Vendible_ConTodoReservado_EsCero()
    {
        Assert.Equal(0, new ExistenciaDeStock(5, 5).Vendible());
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public void Vendible_ConUnaReservaPendienteVencidaSinProcesar_NoLaDescuenta()
    {
        // Físico 5 y reservado 5 (la reserva vencida de 2 sigue sumada en reservado): las 2 vencidas vuelven al vendible.
        Assert.Equal(2, new ExistenciaDeStock(5, 5).Vendible(vencidas: 2));
        Assert.Equal(5, new ExistenciaDeStock(5, 2).Vendible(vencidas: 2));
        Assert.Equal(3, new ExistenciaDeStock(5, 4).Vendible(vencidas: 2));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(10, 3, 3)]
    [InlineData(7, 7, 7)]
    public void Vendible_NuncaEsNegativoNiSuperaAlFisico(int fisico, int reservado, int vencidas)
    {
        var vendible = new ExistenciaDeStock(fisico, reservado).Vendible(vencidas);

        Assert.InRange(vendible, 0, fisico);
    }

    [Fact]
    public void Vendible_ConVencidasFueraDeLasReservadas_Lanza()
    {
        var existencia = new ExistenciaDeStock(5, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => existencia.Vendible(vencidas: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => existencia.Vendible(vencidas: 3));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(5, -1)]
    [InlineData(5, 6)]
    [InlineData(0, 1)]
    public void Constructor_ConUnEstadoImposible_Lanza(int fisico, int reservado)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExistenciaDeStock(fisico, reservado));
    }

    [Fact]
    public void Vacia_EsFisicoCeroYReservadoCero()
    {
        Assert.Equal(new ExistenciaDeStock(0, 0), ExistenciaDeStock.Vacia);
        Assert.Equal(0, ExistenciaDeStock.Vacia.Vendible());
    }

    // --- la regla general ---

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public void Aplicar_QueDejaElReservadoPorEncimaDelFisico_RechazaYNoCambiaNada()
    {
        var existencia = new ExistenciaDeStock(5, 5);

        var resultado = existencia.Aplicar(deltaFisico: 0, deltaReservado: 1);

        Assert.False(resultado.Aplicado);
        Assert.Equal(MotivoDeRechazoDeStock.ReservadoSuperaAlFisico, resultado.Motivo);
        Assert.Equal(existencia, resultado.Existencia);
    }

    [Theory]
    [InlineData(5, 2, -6, 0, MotivoDeRechazoDeStock.FisicoNegativo)]
    [InlineData(5, 2, 0, -3, MotivoDeRechazoDeStock.ReservadoNegativo)]
    [InlineData(5, 2, 0, 4, MotivoDeRechazoDeStock.ReservadoSuperaAlFisico)]
    [InlineData(5, 4, -2, 0, MotivoDeRechazoDeStock.ReservadoSuperaAlFisico)]
    public void Aplicar_ConUnaCombinacionImposible_RechazaConSuMotivo(int fisico, int reservado, int df, int dr, MotivoDeRechazoDeStock esperado)
    {
        var resultado = new ExistenciaDeStock(fisico, reservado).Aplicar(df, dr);

        Assert.False(resultado.Aplicado);
        Assert.Equal(esperado, resultado.Motivo);
        Assert.Equal(new ExistenciaDeStock(fisico, reservado), resultado.Existencia);
    }

    [Theory]
    [InlineData(5, 2, 3, 0, 8, 2)]
    [InlineData(5, 2, 0, 3, 5, 5)]
    [InlineData(5, 2, -3, 0, 2, 2)]
    [InlineData(5, 2, -2, -2, 3, 0)]
    [InlineData(5, 2, 0, 0, 5, 2)]
    public void Aplicar_ConUnEstadoValido_DevuelveLaExistenciaPosterior(int fisico, int reservado, int df, int dr, int fisicoEsperado, int reservadoEsperado)
    {
        var resultado = new ExistenciaDeStock(fisico, reservado).Aplicar(df, dr);

        Assert.True(resultado.Aplicado);
        Assert.Null(resultado.Motivo);
        Assert.Equal(new ExistenciaDeStock(fisicoEsperado, reservadoEsperado), resultado.Existencia);
    }

    [Fact]
    public void Aplicar_ConUnDeltaQueDesbordaElEntero_RechazaEnVezDeEnvolver()
    {
        var existencia = new ExistenciaDeStock(int.MaxValue, 0);

        var resultado = existencia.Aplicar(1, 0);

        Assert.False(resultado.Aplicado);
        Assert.Equal(existencia, resultado.Existencia);
        Assert.False(existencia.Aplicar(int.MinValue, 0).Aplicado);
    }

    // --- las transiciones nombradas ---

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public void Apartar_ConVendibleSuficiente_SubeLasReservadas()
    {
        var resultado = new ExistenciaDeStock(5, 2).Apartar(3);

        Assert.True(resultado.Aplicado);
        Assert.Equal(new ExistenciaDeStock(5, 5), resultado.Existencia);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public void Apartar_ConLaUltimaUnidadYaTomada_Rechaza()
    {
        // Dos clientes por la última unidad: el primero la aparta y el segundo ve el vendible en cero.
        var primero = new ExistenciaDeStock(5, 4).Apartar(1);
        var segundo = primero.Existencia.Apartar(1);

        Assert.True(primero.Aplicado);
        Assert.False(segundo.Aplicado);
        Assert.Equal(MotivoDeRechazoDeStock.ReservadoSuperaAlFisico, segundo.Motivo);
        Assert.Equal(0, segundo.Existencia.Vendible());
    }

    [Fact]
    public void DevolverApartado_BajaLasReservadasYNoPuedeDevolverMasDeLasQueHay()
    {
        var existencia = new ExistenciaDeStock(5, 2);

        Assert.Equal(new ExistenciaDeStock(5, 0), existencia.DevolverApartado(2).Existencia);
        Assert.Equal(MotivoDeRechazoDeStock.ReservadoNegativo, existencia.DevolverApartado(3).Motivo);
    }

    [Fact]
    public void Descontar_BajaElFisicoYLasReservadasPorIgual()
    {
        var resultado = new ExistenciaDeStock(5, 2).Descontar(2);

        Assert.True(resultado.Aplicado);
        Assert.Equal(new ExistenciaDeStock(3, 0), resultado.Existencia);
        Assert.Equal(3, resultado.Existencia.Vendible());
        Assert.False(new ExistenciaDeStock(5, 2).Descontar(3).Aplicado);
    }

    [Fact]
    public void Sumar_SubeElFisicoYDejaElReservado()
    {
        var resultado = new ExistenciaDeStock(5, 2).Sumar(10);

        Assert.True(resultado.Aplicado);
        Assert.Equal(new ExistenciaDeStock(15, 2), resultado.Existencia);
        Assert.Equal(13, resultado.Existencia.Vendible());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LasTransicionesNombradas_ConUnaCantidadNoPositiva_Rechazan(int unidades)
    {
        var existencia = new ExistenciaDeStock(5, 2);

        foreach (var resultado in new[] { existencia.Apartar(unidades), existencia.DevolverApartado(unidades), existencia.Descontar(unidades), existencia.Sumar(unidades) })
        {
            Assert.False(resultado.Aplicado);
            Assert.Equal(MotivoDeRechazoDeStock.CantidadNoPositiva, resultado.Motivo);
            Assert.Equal(existencia, resultado.Existencia);
        }
    }

    [Fact]
    public void CualquierSecuenciaDeTransiciones_ConservaElInvariante()
    {
        var existencia = ExistenciaDeStock.Vacia;
        var azar = new Random(20261114);

        for (var paso = 0; paso < 5000; paso++)
        {
            var unidades = azar.Next(1, 6);
            var resultado = azar.Next(4) switch
            {
                0 => existencia.Apartar(unidades),
                1 => existencia.DevolverApartado(unidades),
                2 => existencia.Descontar(unidades),
                _ => existencia.Sumar(unidades),
            };
            existencia = resultado.Existencia;

            Assert.InRange(existencia.Reservado, 0, existencia.Fisico);
            Assert.True(existencia.Vendible() >= 0);
        }
    }

    // --- el estado de stock ---

    [Theory]
    [Trait("Criterio", "AC-FUE-15")]
    [InlineData(0, EstadoDeStock.SinStock)]
    [InlineData(1, EstadoDeStock.Bajo)]
    [InlineData(2, EstadoDeStock.Bajo)]
    [InlineData(3, EstadoDeStock.Normal)]
    [InlineData(500, EstadoDeStock.Normal)]
    public void DeVendible_ClasificaSinStockBajoYNormal(int vendible, EstadoDeStock esperado)
    {
        Assert.Equal(esperado, EstadoDeStockExtensiones.DeVendible(vendible));
    }

    [Fact]
    public void DeVendible_ConUnVendibleNegativo_Lanza()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EstadoDeStockExtensiones.DeVendible(-1));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-15")]
    public void Estado_SaleDelVendibleConLasVencidas()
    {
        Assert.Equal(EstadoDeStock.SinStock, new ExistenciaDeStock(5, 5).Estado());
        Assert.Equal(EstadoDeStock.Bajo, new ExistenciaDeStock(5, 5).Estado(vencidas: 2));
        Assert.Equal(EstadoDeStock.Normal, new ExistenciaDeStock(8, 5).Estado());
    }
}
