using BabelHub.Inventario.Domain.Ajustes;

namespace BabelHub.Inventario.UnitTests.Ajustes;

/// <summary>T01 y T02: las reglas de cada motivo de ajuste y del ingreso de mercadería, sin base de datos (AJU-02 a AJU-07, AJU-12, AJU-13, AJU-19).</summary>
public sealed class ReglasDeAjusteTests
{
    private static readonly Guid Fuente = Guid.NewGuid();
    private static readonly Guid Edicion = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [Trait("Criterio", "AC-AJU-02")]
    [InlineData("merma", MotivoDeAjuste.Merma, true)]
    [InlineData("danio", MotivoDeAjuste.Danio, true)]
    [InlineData("correccion-de-conteo", MotivoDeAjuste.CorreccionDeConteo, true)]
    [InlineData("ingreso-de-mercaderia", MotivoDeAjuste.IngresoDeMercaderia, false)]
    [InlineData("devolucion", MotivoDeAjuste.Devolucion, false)]
    public void ElMotivoSeReconoceYSoloTresSonManuales(string identificador, MotivoDeAjuste esperado, bool manual)
    {
        Assert.Equal(esperado, MotivosDeAjuste.DeIdentificador(identificador));
        Assert.Equal(identificador, esperado.Identificador());
        Assert.Equal(manual, esperado.EsManual());
    }

    [Theory]
    [Trait("Criterio", "AC-AJU-02")]
    [InlineData("regalo")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("MERMA")]
    public void UnMotivoFueraDeLaListaNoSeReconoce(string? identificador) => Assert.Null(MotivosDeAjuste.DeIdentificador(identificador));

    [Fact]
    [Trait("Criterio", "AC-AJU-01")]
    public void UnaMermaDeDosConFisicoNueveBajaElFisicoASiete()
    {
        var decision = ReglasDeAjuste.Restar(9, 0, 2);

        Assert.True(decision.Aceptado);
        Assert.Equal((9, 7, -2), (decision.CantidadAnterior, decision.CantidadPosterior, decision.DeltaFisico));
    }

    [Theory]
    [Trait("Criterio", "AC-AJU-03")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void LaMermaYElDanioConCeroONegativoSeRechazanYNoCambianNada(int cantidad)
    {
        var decision = ReglasDeAjuste.Restar(9, 1, cantidad);

        Assert.False(decision.Aceptado);
        Assert.Equal(MotivoDeRechazoDeAjuste.CantidadNoPositiva, decision.Rechazo);
        Assert.Equal(0, decision.DeltaFisico);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-04")]
    public void UnDanioQueSuperaElVendibleSeRechaza_AunqueNoSupereElFisico()
    {
        // Físico 9, reservado 1 y vendible 8: descontar 9 se rechaza porque supera el vendible; 8 se acepta y deja el físico en 1 (igual a lo reservado).

        var rechazado = ReglasDeAjuste.Restar(9, 1, 9);
        var aceptado = ReglasDeAjuste.Restar(9, 1, 8);

        Assert.Equal(MotivoDeRechazoDeAjuste.CantidadSuperaAlVendible, rechazado.Rechazo);
        Assert.True(aceptado.Aceptado);
        Assert.Equal(1, aceptado.CantidadPosterior);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-04")]
    public void ConVendibleCeroNoSePuedeRegistrarMerma()
    {
        Assert.Equal(MotivoDeRechazoDeAjuste.CantidadSuperaAlVendible, ReglasDeAjuste.Restar(5, 5, 1).Rechazo);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-05")]
    public void UnConteoDeSieteConFisicoCincoYReservadoCincoSubeElFisicoYDejaElVendibleEnDos()
    {
        var decision = ReglasDeAjuste.Corregir(5, 5, 7);

        Assert.True(decision.Aceptado);
        Assert.Equal((5, 7, 2), (decision.CantidadAnterior, decision.CantidadPosterior, decision.DeltaFisico));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-06")]
    public void UnConteoMenorQueLoReservadoSeRechaza_YUnoIgualSeAceptaYDejaElVendibleEnCero()
    {

        var menor = ReglasDeAjuste.Corregir(8, 5, 4);
        var igual = ReglasDeAjuste.Corregir(8, 5, 5);

        Assert.Equal(MotivoDeRechazoDeAjuste.ConteoMenorQueLoReservado, menor.Rechazo);
        Assert.True(igual.Aceptado);
        Assert.Equal(0, igual.CantidadPosterior - 5);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-07")]
    public void UnConteoIgualAlFisicoSeRechazaSinNadaQueAjustar()
    {
        var decision = ReglasDeAjuste.Corregir(9, 0, 9);

        Assert.Equal(MotivoDeRechazoDeAjuste.SinCambios, decision.Rechazo);
    }

    [Theory]
    [Trait("Criterio", "AC-AJU-06")]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    [InlineData(int.MaxValue)]
    public void UnConteoNegativoOPasadoDelTopeTecnicoSeRechaza(int conteo) =>
        Assert.Equal(MotivoDeRechazoDeAjuste.ConteoFueraDeRango, ReglasDeAjuste.Corregir(9, 0, conteo).Rechazo);

    [Fact]
    [Trait("Criterio", "AC-AJU-05")]
    public void UnConteoDeCeroSinReservasEsValidoYDejaElFisicoEnCero()
    {
        var decision = ReglasDeAjuste.Corregir(9, 0, 0);

        Assert.True(decision.Aceptado);
        Assert.Equal(-9, decision.DeltaFisico);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-10")]
    public void SumarRespetaElTopeTecnico_YUnaCantidadNoPositivaSeRechaza()
    {
        Assert.True(ReglasDeAjuste.Sumar(0, 10).Aceptado);
        Assert.Equal(MotivoDeRechazoDeAjuste.CantidadNoPositiva, ReglasDeAjuste.Sumar(0, 0).Rechazo);
        Assert.Equal(MotivoDeRechazoDeAjuste.ConteoFueraDeRango, ReglasDeAjuste.Sumar(999_999, 2).Rechazo);
        Assert.True(ReglasDeAjuste.Sumar(999_999, 1).Aceptado);
    }

    // ---------- La entidad ----------

    [Fact]
    [Trait("Criterio", "AC-AJU-01")]
    public void UnAjusteDeMermaGuardaMotivoEdicionFuenteUsuarioCantidadesYNota()
    {
        var decision = ReglasDeAjuste.Restar(9, 0, 2);

        var ajuste = Ajuste.Restado(Guid.NewGuid(), Fuente, Edicion, MotivoDeAjuste.Merma, decision, "caja mojada", Usuario, Ahora);

        Assert.Equal((MotivoDeAjuste.Merma, Fuente, Edicion, 9, 7, -2, "caja mojada", Usuario), (ajuste.Motivo, ajuste.FuenteId, ajuste.EdicionId, ajuste.CantidadAnterior, ajuste.CantidadPosterior, ajuste.Diferencia, ajuste.Nota, ajuste.UsuarioId));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-03")]
    public void UnAjusteDeMermaOUnDanioQueSumaNoSePuedeCrear()
    {
        Assert.Throws<ArgumentException>(() => Ajuste.Rehidratar(Guid.NewGuid(), Fuente, Edicion, MotivoDeAjuste.Merma, 5, 7, null, null, Usuario, Ahora, null, null));
        Assert.Throws<ArgumentException>(() => Ajuste.Rehidratar(Guid.NewGuid(), Fuente, Edicion, MotivoDeAjuste.Danio, 5, 5, null, null, Usuario, Ahora, null, null));
        Assert.Throws<InvalidOperationException>(() => Ajuste.Restado(Guid.NewGuid(), Fuente, Edicion, MotivoDeAjuste.Merma, ReglasDeAjuste.Restar(5, 5, 1), null, Usuario, Ahora));
        Assert.Throws<ArgumentException>(() => Ajuste.Restado(Guid.NewGuid(), Fuente, Edicion, MotivoDeAjuste.CorreccionDeConteo, ReglasDeAjuste.Restar(5, 0, 1), null, Usuario, Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-05")]
    public void LaCorreccionGuardaElConteoRealYUnaDiferenciaConSigno()
    {
        var decision = ReglasDeAjuste.Corregir(5, 5, 7);

        var ajuste = Ajuste.Corregido(Guid.NewGuid(), Fuente, Edicion, decision, 7, null, Usuario, Ahora);

        Assert.Equal((7, 7, 2), (ajuste.ConteoReal, ajuste.CantidadPosterior, ajuste.Diferencia));
        Assert.Throws<ArgumentException>(() => Ajuste.Rehidratar(Guid.NewGuid(), Fuente, Edicion, MotivoDeAjuste.CorreccionDeConteo, 5, 7, 6, null, Usuario, Ahora, null, null));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-09")]
    public void ElAjusteDeUnaDevolucionExigeLaDevolucionYElPedido_ElDeOtroMotivoNoLosLleva()
    {
        var decision = ReglasDeAjuste.Sumar(3, 1);
        var devolucion = Guid.NewGuid();
        var pedido = Guid.NewGuid();

        var ajuste = Ajuste.Devuelto(Guid.NewGuid(), Fuente, Edicion, decision, Usuario, Ahora, devolucion, pedido);

        Assert.Equal((devolucion, pedido, MotivoDeAjuste.Devolucion), (ajuste.DevolucionId, ajuste.PedidoId, ajuste.Motivo));
        Assert.Throws<ArgumentException>(() => Ajuste.Rehidratar(Guid.NewGuid(), Fuente, Edicion, MotivoDeAjuste.Devolucion, 3, 4, null, null, Usuario, Ahora, null, pedido));
        Assert.Throws<ArgumentException>(() => Ajuste.Rehidratar(Guid.NewGuid(), Fuente, Edicion, MotivoDeAjuste.Merma, 4, 3, null, null, Usuario, Ahora, devolucion, pedido));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-17")]
    public void UnAjusteDepuradoPuedeRehidratarseSinUsuarioNiNota_PeroUnoNuevoExigeUsuario()
    {
        var depurado = Ajuste.Rehidratar(Guid.NewGuid(), Fuente, Edicion, MotivoDeAjuste.Merma, 9, 7, null, null, null, Ahora, null, null);

        Assert.Null(depurado.UsuarioId);
        Assert.Throws<ArgumentException>(() => Ajuste.Restado(Guid.NewGuid(), Fuente, Edicion, MotivoDeAjuste.Merma, ReglasDeAjuste.Restar(9, 0, 2), null, Guid.Empty, Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-01")]
    public void LaNotaNoPasaDe200Caracteres()
    {
        var decision = ReglasDeAjuste.Restar(9, 0, 2);

        Ajuste.Restado(Guid.NewGuid(), Fuente, Edicion, MotivoDeAjuste.Merma, decision, new string('a', 200), Usuario, Ahora);

        Assert.Throws<ArgumentException>(() => Ajuste.Restado(Guid.NewGuid(), Fuente, Edicion, MotivoDeAjuste.Merma, decision, new string('a', 201), Usuario, Ahora));
    }

    // ---------- T02: el ingreso ----------

    [Theory]
    [Trait("Criterio", "AC-AJU-15")]
    [InlineData(1, null, null)]
    [InlineData(5_000, "GR-001", null)]
    [InlineData(10, "F001-0000123/A.2 b", null)]
    [InlineData(0, null, MotivoDeRechazoDeIngreso.CantidadFueraDeRango)]
    [InlineData(6_000, null, MotivoDeRechazoDeIngreso.CantidadFueraDeRango)]
    [InlineData(-5, "GR-001", MotivoDeRechazoDeIngreso.CantidadFueraDeRango)]
    [InlineData(int.MaxValue, null, MotivoDeRechazoDeIngreso.CantidadFueraDeRango)]
    [InlineData(5, "guía; DROP TABLE", MotivoDeRechazoDeIngreso.ReferenciaInvalida)]
    [InlineData(5, "ñandú", MotivoDeRechazoDeIngreso.ReferenciaInvalida)]
    [InlineData(5, "", MotivoDeRechazoDeIngreso.ReferenciaInvalida)]
    public void LaCantidadDelIngresoVaDe1a5000YLaReferenciaTieneJuegoDeCaracteresAcotado(int cantidad, string? referencia, MotivoDeRechazoDeIngreso? esperado) =>
        Assert.Equal(esperado, ReglasDeIngreso.Evaluar(cantidad, referencia));

    [Fact]
    [Trait("Criterio", "AC-AJU-15")]
    public void LaReferenciaNoPasaDe60Caracteres()
    {
        Assert.Null(ReglasDeIngreso.Evaluar(5, new string('A', 60)));
        Assert.Equal(MotivoDeRechazoDeIngreso.ReferenciaInvalida, ReglasDeIngreso.Evaluar(5, new string('A', 61)));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-10")]
    public void UnIngresoGuardaEdicionCantidadReferenciaNotaYUsuarioYSuAjuste()
    {
        var ajuste = Guid.NewGuid();

        var ingreso = IngresoDeMercaderia.Nuevo(Guid.NewGuid(), Fuente, Edicion, 10, "GR-001", "caja 1", Usuario, Ahora, ajuste);

        Assert.Equal((Fuente, Edicion, 10, "GR-001", "caja 1", Usuario, ajuste), (ingreso.FuenteId, ingreso.EdicionId, ingreso.Cantidad, ingreso.Referencia, ingreso.Nota, ingreso.UsuarioId, ingreso.AjusteId));
        Assert.Throws<ArgumentException>(() => IngresoDeMercaderia.Nuevo(Guid.NewGuid(), Fuente, Edicion, 6_000, null, null, Usuario, Ahora, ajuste));
        Assert.Throws<ArgumentException>(() => IngresoDeMercaderia.Nuevo(Guid.NewGuid(), Fuente, Edicion, 5, "a;b", null, Usuario, Ahora, ajuste));
        Assert.Throws<ArgumentException>(() => IngresoDeMercaderia.Nuevo(Guid.NewGuid(), Fuente, Edicion, 5, null, null, Guid.Empty, Ahora, ajuste));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-17")]
    public void UnIngresoDepuradoSeRehidrataSinUsuarioYSuReferenciaSeConserva()
    {
        var ingreso = IngresoDeMercaderia.Rehidratar(Guid.NewGuid(), Fuente, Edicion, 10, "GR-001", null, null, Ahora, Guid.NewGuid());

        Assert.Null(ingreso.UsuarioId);
        Assert.Equal("GR-001", ingreso.Referencia);
    }
}
