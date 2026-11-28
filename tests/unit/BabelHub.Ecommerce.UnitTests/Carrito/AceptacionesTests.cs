using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T05: <see cref="AceptacionDeTexto"/> por texto legal (términos y privacidad siempre, reembolso digital si hay digitales), cada una con su versión y su fecha, y su invalidación cuando cambia la versión vigente antes de pagar
/// (CAR-12, CAR-13, CAR-22, CAR-28).
/// </summary>
public sealed class AceptacionesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CarritoId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid LineaFija = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Func<string, string> Identidad = s => s;
    private static readonly CostoDeEnvio Envio = CostoDeEnvio.Crear(12.00m);

    private static Guid Edicion(int n) => new(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);

    private static LineaParaCheckout Impreso(int n) => new(Edicion(n), false, $"Libro {n}", "9780000000019", 1, 50m, Edicion(n), Sucursal.Principal);

    private static LineaParaCheckout Digital(int n) => new(Edicion(n), true, $"Digital {n}", "9780000000026", 1, 20m);

    private static Checkout Nuevo(params LineaParaCheckout[] lineas)
    {
        var checkout = Checkout.Iniciar(Guid.NewGuid(), Cuenta, CarritoId, lineas, () => LineaFija, Ahora);
        if (checkout.RequiereEntrega)
        {
            checkout.ElegirRecojo(Ahora);
        }

        return checkout;
    }

    private static CarritoDeDominio CarritoCon(params LineaParaCheckout[] lineas)
    {
        var carrito = CarritoDeDominio.Crear(CarritoId, PropietarioDelCarrito.DeCuenta(Cuenta), Ahora);
        foreach (var l in lineas)
        {
            carrito.Agregar(l.EdicionId, l.EsDigital, l.Cantidad, Ahora);
        }

        return carrito;
    }

    private static ResultadoDeCheckout Preparar(Checkout c, CarritoDeDominio k) => c.PrepararPasoAlPago(Guid.NewGuid(), Envio, Identidad, Identidad, k, Ahora);

    [Fact]
    public void Los_terminos_y_la_privacidad_se_exigen_siempre_y_el_reembolso_digital_solo_si_hay_digitales()
    {
        Assert.Equal([TextoLegal.Terminos, TextoLegal.Privacidad], AceptacionesDelCheckout.Requeridas(hayDigitales: false));
        Assert.Equal([TextoLegal.Terminos, TextoLegal.Privacidad, TextoLegal.ReembolsoDigital], AceptacionesDelCheckout.Requeridas(hayDigitales: true));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-15")]
    public void Sin_aceptar_los_terminos_y_la_privacidad_no_se_puede_pasar_al_pago_y_nada_cambia()
    {
        var checkout = Nuevo(Impreso(1));
        var carrito = CarritoCon(Impreso(1));

        Assert.Equal(RechazoDeCheckout.AceptacionRequerida, Preparar(checkout, carrito).Rechazo);
        checkout.Aceptar(TextoLegal.Terminos, "t-1", Ahora);
        Assert.Equal(RechazoDeCheckout.AceptacionRequerida, Preparar(checkout, carrito).Rechazo);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
        Assert.Null(checkout.PedidoId);

        checkout.Aceptar(TextoLegal.Privacidad, "p-1", Ahora);
        Assert.Equal(EfectoDeTransicion.Aplicada, Preparar(checkout, carrito).Efecto);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-11")]
    public void Con_un_libro_digital_sin_aceptar_la_politica_de_reembolso_no_se_puede_pasar_al_pago()
    {
        var checkout = Nuevo(Impreso(1), Digital(2));
        var carrito = CarritoCon(Impreso(1), Digital(2));
        checkout.Aceptar(TextoLegal.Terminos, "t-1", Ahora);
        checkout.Aceptar(TextoLegal.Privacidad, "p-1", Ahora);

        Assert.Equal(RechazoDeCheckout.AceptacionRequerida, Preparar(checkout, carrito).Rechazo);
        Assert.Equal([TextoLegal.ReembolsoDigital], checkout.TextosPorAceptar());

        checkout.Aceptar(TextoLegal.ReembolsoDigital, "r-1", Ahora);
        Assert.Equal(EfectoDeTransicion.Aplicada, Preparar(checkout, carrito).Efecto);
        Assert.Empty(checkout.TextosPorAceptar());
    }

    [Fact]
    public void Un_carrito_sin_digitales_no_pide_la_politica_de_reembolso_y_aceptarla_no_hace_nada()
    {
        var checkout = Nuevo(Impreso(1));

        var resultado = checkout.Aceptar(TextoLegal.ReembolsoDigital, "r-1", Ahora);

        Assert.Equal(EfectoDeTransicion.SinCambios, resultado.Efecto);
        Assert.Empty(checkout.Aceptaciones.Aceptadas);
        Assert.Equal([TextoLegal.Terminos, TextoLegal.Privacidad], checkout.TextosPorAceptar());
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-20")]
    public void Cada_texto_aceptado_guarda_su_version_y_su_fecha()
    {
        var checkout = Nuevo(Impreso(1), Digital(2));

        checkout.Aceptar(TextoLegal.Terminos, "t-3", Ahora.AddMinutes(1));
        checkout.Aceptar(TextoLegal.Privacidad, "p-2", Ahora.AddMinutes(2));
        checkout.Aceptar(TextoLegal.ReembolsoDigital, "r-1", Ahora.AddMinutes(3));

        Assert.Equal(
            [(TextoLegal.Terminos, "t-3", Ahora.AddMinutes(1)), (TextoLegal.Privacidad, "p-2", Ahora.AddMinutes(2)), (TextoLegal.ReembolsoDigital, "r-1", Ahora.AddMinutes(3))],
            checkout.Aceptaciones.Aceptadas.Select(a => (a.Texto, a.Version, a.AceptadoEn)));
    }

    [Fact]
    public void Aceptar_otra_vez_la_misma_version_conserva_la_primera_fecha_y_no_cambia_nada()
    {
        var checkout = Nuevo(Impreso(1));
        checkout.Aceptar(TextoLegal.Terminos, "t-1", Ahora.AddMinutes(1));
        var version = checkout.Version;

        var otraVez = checkout.Aceptar(TextoLegal.Terminos, "t-1", Ahora.AddMinutes(9));

        Assert.Equal(EfectoDeTransicion.SinCambios, otraVez.Efecto);
        Assert.Equal(Ahora.AddMinutes(1), Assert.Single(checkout.Aceptaciones.Aceptadas).AceptadoEn);
        Assert.Equal(version, checkout.Version);
        Assert.Equal(Ahora.AddMinutes(1), checkout.ModificadoPorLaPersonaEn);
    }

    [Fact]
    public void Aceptar_otra_version_reemplaza_la_version_y_la_fecha()
    {
        var checkout = Nuevo(Impreso(1));
        checkout.Aceptar(TextoLegal.Terminos, "t-1", Ahora.AddMinutes(1));

        var resultado = checkout.Aceptar(TextoLegal.Terminos, "t-2", Ahora.AddMinutes(5));

        Assert.Equal(EfectoDeTransicion.Aplicada, resultado.Efecto);
        var aceptacion = Assert.Single(checkout.Aceptaciones.Aceptadas);
        Assert.Equal(("t-2", Ahora.AddMinutes(5)), (aceptacion.Version, aceptacion.AceptadoEn));
        Assert.Equal(Ahora.AddMinutes(5), checkout.ModificadoPorLaPersonaEn);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v\n1")]
    [InlineData("v\u00001")]
    public void Una_version_vacia_o_con_caracteres_de_control_se_rechaza(string? version)
    {
        var checkout = Nuevo(Impreso(1));

        Assert.Throws<ArgumentException>(() => checkout.Aceptar(TextoLegal.Terminos, version!, Ahora));
        Assert.Throws<ArgumentException>(() => checkout.Aceptar(TextoLegal.Terminos, new string('v', 41), Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-20")]
    public void Si_cambia_la_version_vigente_de_un_texto_su_aceptacion_se_invalida_y_se_vuelve_a_pedir_antes_de_pagar()
    {
        var checkout = Nuevo(Impreso(1), Digital(2));
        var carrito = CarritoCon(Impreso(1), Digital(2));
        checkout.Aceptar(TextoLegal.Terminos, "t-1", Ahora);
        checkout.Aceptar(TextoLegal.Privacidad, "p-1", Ahora);
        checkout.Aceptar(TextoLegal.ReembolsoDigital, "r-1", Ahora);
        var vigentes = new Dictionary<TextoLegal, string> { [TextoLegal.Terminos] = "t-2", [TextoLegal.Privacidad] = "p-1", [TextoLegal.ReembolsoDigital] = "r-1" };

        var invalidadas = checkout.RevalidarTextos(vigentes, Ahora.AddMinutes(1));

        Assert.Equal([TextoLegal.Terminos], invalidadas.Invalidadas);
        Assert.Equal([TextoLegal.Terminos], checkout.TextosPorAceptar());
        Assert.Equal(RechazoDeCheckout.AceptacionRequerida, Preparar(checkout, carrito).Rechazo);

        checkout.Aceptar(TextoLegal.Terminos, "t-2", Ahora.AddMinutes(2));
        Assert.Equal(EfectoDeTransicion.Aplicada, Preparar(checkout, carrito).Efecto);
    }

    [Fact]
    public void Revalidar_con_las_mismas_versiones_no_invalida_nada_ni_cambia_la_version_del_checkout()
    {
        var checkout = Nuevo(Impreso(1));
        checkout.Aceptar(TextoLegal.Terminos, "t-1", Ahora);
        checkout.Aceptar(TextoLegal.Privacidad, "p-1", Ahora);
        var version = checkout.Version;

        var resultado = checkout.RevalidarTextos(new Dictionary<TextoLegal, string> { [TextoLegal.Terminos] = "t-1", [TextoLegal.Privacidad] = "p-1", [TextoLegal.ReembolsoDigital] = "r-9" }, Ahora);

        Assert.Empty(resultado.Invalidadas);
        Assert.Equal(version, checkout.Version);
    }

    [Fact]
    public void Una_version_vigente_que_no_se_conoce_invalida_la_aceptacion_porque_no_se_puede_comparar()
    {
        var checkout = Nuevo(Impreso(1));
        checkout.Aceptar(TextoLegal.Terminos, "t-1", Ahora);

        var resultado = checkout.RevalidarTextos(new Dictionary<TextoLegal, string>(), Ahora);

        Assert.Equal([TextoLegal.Terminos], resultado.Invalidadas);
    }

    [Fact]
    public void Las_aceptaciones_solo_se_cambian_en_edicion_y_con_el_checkout_en_pago_hay_que_reabrir_antes()
    {
        var checkout = Nuevo(Impreso(1));
        var carrito = CarritoCon(Impreso(1));
        checkout.Aceptar(TextoLegal.Terminos, "t-1", Ahora);
        checkout.Aceptar(TextoLegal.Privacidad, "p-1", Ahora);
        Preparar(checkout, carrito);

        Assert.Equal(RechazoDeCheckout.EnCurso, checkout.Aceptar(TextoLegal.Terminos, "t-2", Ahora).Rechazo);
        Assert.Equal(RechazoDeCheckout.EnCurso, checkout.RevalidarTextos(new Dictionary<TextoLegal, string>(), Ahora).Rechazo);

        checkout.ConfirmarEnPago(checkout.PedidoId!.Value, checkout.GeneracionDeReserva, checkout.HuellaDeInstantanea!, Ahora.AddMinutes(15), carrito);
        Assert.Equal(RechazoDeCheckout.EstadoNoPermite, checkout.Aceptar(TextoLegal.Terminos, "t-2", Ahora).Rechazo);

        checkout.Editar(carrito, Ahora.AddMinutes(1));
        Assert.Equal(EfectoDeTransicion.Aplicada, checkout.Aceptar(TextoLegal.Terminos, "t-2", Ahora.AddMinutes(1)).Efecto);
    }

    [Fact]
    public void La_huella_de_instantanea_cambia_con_la_version_de_un_texto_pero_no_con_su_fecha()
    {
        static string? Huella(string versionDeTerminos, DateTimeOffset fecha)
        {
            string? h = null;
            var checkout = Nuevo(Impreso(1));
            checkout.Aceptar(TextoLegal.Terminos, versionDeTerminos, fecha);
            checkout.Aceptar(TextoLegal.Privacidad, "p-1", fecha);
            checkout.PrepararPasoAlPago(Guid.NewGuid(), Envio, Identidad, s => h = s, CarritoCon(Impreso(1)), Ahora);
            return h;
        }

        var baseA = Huella("t-1", Ahora);

        Assert.Equal(baseA, Huella("t-1", Ahora.AddHours(3)));
        Assert.NotEqual(baseA, Huella("t-2", Ahora));
    }

    [Fact]
    public void La_huella_de_instantanea_ignora_la_aceptacion_del_reembolso_si_ya_no_hay_digitales()
    {
        static string? Huella(bool conReembolsoAceptado)
        {
            string? h = null;
            var checkout = Nuevo(Impreso(1));
            checkout.Aceptar(TextoLegal.Terminos, "t-1", Ahora);
            checkout.Aceptar(TextoLegal.Privacidad, "p-1", Ahora);
            if (conReembolsoAceptado)
            {
                checkout.SincronizarLineas([Impreso(1), Digital(2)], () => LineaFija, Ahora);
                checkout.Aceptar(TextoLegal.ReembolsoDigital, "r-1", Ahora);
                checkout.SincronizarLineas([Impreso(1)], () => LineaFija, Ahora);
            }

            checkout.PrepararPasoAlPago(Guid.NewGuid(), Envio, Identidad, s => h = s, CarritoCon(Impreso(1)), Ahora);
            return h;
        }

        Assert.Equal(Huella(conReembolsoAceptado: false), Huella(conReembolsoAceptado: true));
    }
}
