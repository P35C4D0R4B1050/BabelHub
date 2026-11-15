using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>La clave de firma de las credenciales (T10; AC-IDE-33; IDE-36, IDE-61).</summary>
public sealed class ClaveDeFirmaTests
{
    private static readonly DateTimeOffset Dia0 = new(2026, 8, 1, 3, 0, 0, TimeSpan.FromHours(-5));

    private static JuegoDeClavesDeFirma Juego() => JuegoDeClavesDeFirma.Iniciar(ClaveDeFirma.Nueva("kid-inicial-01", Dia0));

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public void Se_rota_a_los_90_dias_desde_la_ultima_rotacion_y_no_antes()
    {
        var juego = Juego();

        Assert.False(juego.DebeRotar(Dia0.AddDays(90).AddSeconds(-1)));
        Assert.True(juego.DebeRotar(Dia0.AddDays(90)));
        Assert.True(juego.DebeRotar(Dia0.AddDays(91)));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public void Con_una_clave_de_91_dias_la_rotacion_acepta_las_credenciales_de_la_anterior_solo_hasta_que_venzan()
    {
        var juego = Juego();
        var rotacion = Dia0.AddDays(91);

        juego.Rotar(ClaveDeFirma.Nueva("kid-nuevo-0002", rotacion), rotacion);

        Assert.Equal("kid-nuevo-0002", juego.Activa.Kid);
        Assert.True(juego.Acepta("kid-inicial-01", rotacion.AddMinutes(14).AddSeconds(59)));
        Assert.False(juego.Acepta("kid-inicial-01", rotacion.AddMinutes(15)));
        Assert.True(juego.Acepta("kid-nuevo-0002", rotacion.AddMinutes(15)));
        Assert.False(juego.DebeRotar(rotacion.AddDays(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public void Siempre_hay_exactamente_una_clave_activa()
    {
        var juego = Juego();
        juego.Rotar(ClaveDeFirma.Nueva("kid-nuevo-0002", Dia0.AddDays(91)), Dia0.AddDays(91));
        juego.Rotar(ClaveDeFirma.Nueva("kid-nuevo-0003", Dia0.AddDays(182)), Dia0.AddDays(182));

        Assert.Single(juego.Claves, c => c.Estado == EstadoDeClaveDeFirma.Activa);
        Assert.Throws<ArgumentException>(() => JuegoDeClavesDeFirma.Restaurar([ClaveDeFirma.Nueva("kid-aaaaaaaa", Dia0), ClaveDeFirma.Nueva("kid-bbbbbbbb", Dia0)]));
        Assert.Throws<ArgumentException>(() => JuegoDeClavesDeFirma.Restaurar([ClaveDeFirma.Restaurar("kid-aaaaaaaa", Dia0, EstadoDeClaveDeFirma.Anterior, Dia0, null)]));
        Assert.Throws<ArgumentException>(() => JuegoDeClavesDeFirma.Restaurar([ClaveDeFirma.Nueva("kid-aaaaaaaa", Dia0), ClaveDeFirma.Restaurar("kid-aaaaaaaa", Dia0, EstadoDeClaveDeFirma.Anterior, Dia0, null)]));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public void Una_clave_nueva_repetida_o_que_no_es_activa_no_se_acepta_en_la_rotacion()
    {
        var juego = Juego();

        Assert.Throws<ArgumentException>(() => juego.Rotar(ClaveDeFirma.Nueva("kid-inicial-01", Dia0.AddDays(91)), Dia0.AddDays(91)));
        Assert.Throws<ArgumentException>(() => juego.Rotar(ClaveDeFirma.Restaurar("kid-nuevo-0002", Dia0, EstadoDeClaveDeFirma.Anterior, Dia0, null), Dia0.AddDays(91)));
        Assert.Equal("kid-inicial-01", juego.Activa.Kid);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public void Al_declarar_comprometida_la_activa_se_rota_se_rechazan_sus_credenciales_y_se_revocan_las_renovaciones()
    {
        var juego = Juego();
        var momento = Dia0.AddDays(10);

        var resultado = juego.DeclararComprometida("kid-inicial-01", ClaveDeFirma.Nueva("kid-nuevo-0002", momento), momento);

        Assert.NotNull(resultado);
        Assert.Equal("kid-nuevo-0002", resultado.ClaveActiva.Kid);
        Assert.True(resultado.RevocarRenovaciones);
        Assert.True(resultado.PublicarRevocacion);
        Assert.False(juego.Acepta("kid-inicial-01", momento));
        Assert.False(juego.Acepta("kid-inicial-01", momento.AddSeconds(-1)));
        Assert.True(juego.Acepta("kid-nuevo-0002", momento));
        Assert.Equal(["kid-inicial-01"], juego.Revocadas);
        Assert.DoesNotContain(juego.Publicables(momento), c => c.Kid == "kid-inicial-01");
        Assert.Equal(momento, juego.Claves.Single(c => c.Kid == "kid-inicial-01").ComprometidaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public void Declarar_comprometida_la_activa_sin_clave_nueva_se_rechaza_y_no_cambia_nada()
    {
        var juego = Juego();

        Assert.Throws<ArgumentException>(() => juego.DeclararComprometida("kid-inicial-01", null, Dia0));
        Assert.Equal(EstadoDeClaveDeFirma.Activa, juego.Activa.Estado);
        Assert.Empty(juego.Revocadas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public void Declarar_comprometida_una_anterior_la_rechaza_de_inmediato_aunque_no_hayan_vencido_sus_credenciales()
    {
        var juego = Juego();
        var rotacion = Dia0.AddDays(91);
        juego.Rotar(ClaveDeFirma.Nueva("kid-nuevo-0002", rotacion), rotacion);
        Assert.True(juego.Acepta("kid-inicial-01", rotacion.AddMinutes(5)));

        var resultado = juego.DeclararComprometida("kid-inicial-01", null, rotacion.AddMinutes(5));

        Assert.NotNull(resultado);
        Assert.False(juego.Acepta("kid-inicial-01", rotacion.AddMinutes(5)));
        Assert.Equal("kid-nuevo-0002", juego.Activa.Kid);
        Assert.Null(juego.DeclararComprometida("kid-inicial-01", null, rotacion.AddMinutes(6)));
        Assert.Null(juego.DeclararComprometida("kid-que-no-existe", null, rotacion.AddMinutes(6)));
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-33")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("corto")]
    [InlineData("kid con espacios")]
    [InlineData("kid/raro/0001")]
    public void Un_kid_desconocido_o_con_mala_forma_no_se_acepta(string? kid)
    {
        var juego = Juego();

        Assert.False(juego.Acepta(kid, Dia0));
        if (kid is not null)
        {
            Assert.Throws<ArgumentException>(() => ClaveDeFirma.Nueva(kid, Dia0));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public void Se_publican_la_activa_y_las_anteriores_que_aun_se_aceptan_y_se_retiran_las_vencidas_al_rotar()
    {
        var juego = Juego();
        var primera = Dia0.AddDays(91);
        juego.Rotar(ClaveDeFirma.Nueva("kid-nuevo-0002", primera), primera);
        Assert.Equal(2, juego.Publicables(primera.AddMinutes(1)).Count);
        Assert.Single(juego.Publicables(primera.AddMinutes(16)));

        var segunda = Dia0.AddDays(182);
        juego.Rotar(ClaveDeFirma.Nueva("kid-nuevo-0003", segunda), segunda);

        Assert.Equal(EstadoDeClaveDeFirma.Retirada, juego.Claves.Single(c => c.Kid == "kid-inicial-01").Estado);
        Assert.Equal(EstadoDeClaveDeFirma.Anterior, juego.Claves.Single(c => c.Kid == "kid-nuevo-0002").Estado);
        Assert.Equal(3, juego.Claves.Count);
    }
}
