using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>El desafío de ingreso y la renovación (T04; AC-IDE-12, AC-IDE-38, AC-IDE-41, AC-IDE-50; IDE-14, IDE-42, IDE-46, IDE-60).</summary>
public sealed class SesionesTests
{
    private static readonly DateTimeOffset Lunes8 = new(2026, 11, 16, 8, 0, 0, TimeSpan.FromHours(-5));

    private static string Huella(int n) => n.ToString("x").PadLeft(64, '0');

    // ---------- Desafío ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-38")]
    public void Un_desafio_dura_cinco_minutos_y_vencido_hay_que_reiniciar_el_ingreso()
    {
        var desafio = DesafioDeIngreso.Crear(Guid.NewGuid(), Guid.NewGuid(), ModoDelDesafio.PresentarSegundoFactor, Lunes8);

        Assert.Equal(EstadoDelDesafio.Vigente, desafio.Evaluar(Lunes8.AddMinutes(4).AddSeconds(59)));
        Assert.Equal(EstadoDelDesafio.Vencido, desafio.Evaluar(Lunes8.AddMinutes(5)));
        Assert.False(desafio.Consumir(Lunes8.AddMinutes(5), AccionConDesafio.PresentarSegundoFactor));
        Assert.False(desafio.Usado);
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-38")]
    [InlineData(ModoDelDesafio.PresentarSegundoFactor, AccionConDesafio.PresentarSegundoFactor, true)]
    [InlineData(ModoDelDesafio.PresentarSegundoFactor, AccionConDesafio.ConfigurarSegundoFactor, false)]
    [InlineData(ModoDelDesafio.PresentarSegundoFactor, AccionConDesafio.CambiarContrasenaTemporal, false)]
    [InlineData(ModoDelDesafio.ConfigurarSegundoFactor, AccionConDesafio.ConfigurarSegundoFactor, true)]
    [InlineData(ModoDelDesafio.ConfigurarSegundoFactor, AccionConDesafio.PresentarSegundoFactor, false)]
    [InlineData(ModoDelDesafio.CambiarContrasenaTemporal, AccionConDesafio.CambiarContrasenaTemporal, true)]
    [InlineData(ModoDelDesafio.CambiarContrasenaTemporal, AccionConDesafio.ConfigurarSegundoFactor, false)]
    public void El_desafio_solo_permite_lo_de_su_modo(ModoDelDesafio modo, AccionConDesafio accion, bool permitida)
    {
        var desafio = DesafioDeIngreso.Crear(Guid.NewGuid(), Guid.NewGuid(), modo, Lunes8);

        Assert.Equal(permitida, desafio.Permite(accion));
        Assert.Equal(permitida, desafio.Consumir(Lunes8.AddMinutes(1), accion));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-38")]
    public void Un_desafio_sirve_una_sola_vez()
    {
        var desafio = DesafioDeIngreso.Crear(Guid.NewGuid(), Guid.NewGuid(), ModoDelDesafio.PresentarSegundoFactor, Lunes8);

        Assert.True(desafio.Consumir(Lunes8.AddMinutes(1), AccionConDesafio.PresentarSegundoFactor));
        Assert.False(desafio.Consumir(Lunes8.AddMinutes(2), AccionConDesafio.PresentarSegundoFactor));
        Assert.Equal(EstadoDelDesafio.Usado, desafio.Evaluar(Lunes8.AddMinutes(2)));
    }

    // ---------- Renovación ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-41")]
    public void El_encargado_queda_sin_renovacion_a_las_16_horas_aunque_la_use_y_el_cliente_a_los_siete_dias_sin_que_cada_uso_extienda_el_plazo()
    {
        var encargado = Renovacion.Emitir(Huella(1), Guid.NewGuid(), RolDeUsuario.EncargadoDeSucursal, Lunes8);
        var cliente = Renovacion.Emitir(Huella(2), Guid.NewGuid(), RolDeUsuario.Cliente, Lunes8);

        Assert.Equal(Lunes8.AddHours(8), encargado.ExpiraAbsoluta);
        Assert.Equal(Lunes8.AddDays(7), cliente.ExpiraAbsoluta);

        // El encargado la usa a las 15:50: la siguiente hereda las 16:00 y a las 16:00 ya no rota.
        var (_, segunda) = encargado.Rotar(Lunes8.AddHours(7).AddMinutes(50), null, Huella(3));
        Assert.Equal(Lunes8.AddHours(8), segunda!.ExpiraAbsoluta);
        Assert.Equal(ResultadoDeRotacion.Vencida, segunda.Rotar(Lunes8.AddHours(8), null, Huella(4)).Resultado);

        // El cliente la usa el domingo: la siguiente vence igual el lunes siguiente.
        var (rotada, siguiente) = cliente.Rotar(Lunes8.AddDays(6).AddHours(23), null, Huella(5));
        Assert.Equal(ResultadoDeRotacion.Rotada, rotada);
        Assert.Equal(Lunes8.AddDays(7), siguiente!.ExpiraAbsoluta);
        Assert.False(siguiente.EstaVigente(Lunes8.AddDays(7)));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-41")]
    public void Rotar_usa_la_renovacion_y_la_siguiente_hereda_familia_y_vencimiento()
    {
        var primera = Renovacion.Emitir(Huella(1), Guid.NewGuid(), RolDeUsuario.Cliente, Lunes8);

        var (resultado, siguiente) = primera.Rotar(Lunes8.AddDays(1), null, Huella(2));

        Assert.Equal(ResultadoDeRotacion.Rotada, resultado);
        Assert.Equal(primera.Familia, siguiente!.Familia);
        Assert.Equal(primera.UsuarioId, siguiente.UsuarioId);
        Assert.NotEqual(primera.Huella, siguiente.Huella);
        Assert.NotNull(primera.UsadaEn);
        Assert.False(primera.EstaVigente(Lunes8.AddDays(1)));
        Assert.True(siguiente.EstaVigente(Lunes8.AddDays(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-41")]
    public void Reutilizar_una_renovacion_ya_rotada_indica_robo_y_ordena_revocar_la_familia()
    {
        var primera = Renovacion.Emitir(Huella(1), Guid.NewGuid(), RolDeUsuario.Cliente, Lunes8);
        primera.Rotar(Lunes8.AddHours(1), null, Huella(2));

        var (resultado, siguiente) = primera.Rotar(Lunes8.AddHours(2), null, Huella(3));
        var orden = primera.RevocacionDeFamilia(Lunes8.AddHours(2));

        Assert.Equal(ResultadoDeRotacion.Reutilizada, resultado);
        Assert.Null(siguiente);
        Assert.Equal(primera.Familia, orden.Familia);
        Assert.Equal(primera.UsuarioId, orden.UsuarioId);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-12")]
    public void Cerrar_sesion_revoca_la_renovacion_y_deja_de_funcionar()
    {
        var renovacion = Renovacion.Emitir(Huella(1), Guid.NewGuid(), RolDeUsuario.Cliente, Lunes8);
        Assert.True(renovacion.EstaVigente(Lunes8.AddHours(1)));

        renovacion.Revocar(Lunes8.AddHours(1));
        renovacion.Revocar(Lunes8.AddHours(2));

        Assert.False(renovacion.EstaVigente(Lunes8.AddHours(1)));
        Assert.Equal(Lunes8.AddHours(1), renovacion.RevocadaEn);
        Assert.Equal(ResultadoDeRotacion.Revocada, renovacion.Rotar(Lunes8.AddHours(3), null, Huella(2)).Resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-50")]
    public void Cerrar_todas_las_sesiones_deja_sin_efecto_las_tres_renovaciones_por_el_corte_de_sesiones()
    {
        var usuario = Guid.NewGuid();
        var renovaciones = Enumerable.Range(1, 3).Select(i => Renovacion.Emitir(Huella(i), usuario, RolDeUsuario.Cliente, Lunes8.AddHours(i))).ToList();
        var corte = CorteDeSesiones.Avanzar(null, Lunes8.AddHours(10));

        Assert.All(renovaciones, r => Assert.False(r.EstaVigente(Lunes8.AddHours(11), corte)));
        Assert.All(renovaciones, r => Assert.Equal(ResultadoDeRotacion.Invalidada, r.Rotar(Lunes8.AddHours(11), corte, Huella(9)).Resultado));

        // Una renovación emitida después del corte (un ingreso nuevo) sí sirve.
        var nueva = Renovacion.Emitir(Huella(8), usuario, RolDeUsuario.Cliente, Lunes8.AddHours(10).AddMinutes(1));
        Assert.True(nueva.EstaVigente(Lunes8.AddHours(11), corte));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-50")]
    public void El_corte_de_sesiones_solo_avanza()
    {
        var uno = Lunes8.AddHours(10);

        Assert.Equal(uno, CorteDeSesiones.Avanzar(null, uno));
        Assert.Equal(uno, CorteDeSesiones.Avanzar(uno, uno.AddHours(-1)));
        Assert.Equal(uno.AddHours(1), CorteDeSesiones.Avanzar(uno, uno.AddHours(1)));
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-41")]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("ZZZZ000000000000000000000000000000000000000000000000000000000000")]
    public void La_huella_de_la_renovacion_es_un_sha256_en_hexadecimal(string huella)
    {
        Assert.Throws<ArgumentException>(() => Renovacion.Emitir(huella, Guid.NewGuid(), RolDeUsuario.Cliente, Lunes8));
        Assert.Equal("[renovación]", Renovacion.Emitir(Huella(1), Guid.NewGuid(), RolDeUsuario.Cliente, Lunes8).ToString());
    }
}
