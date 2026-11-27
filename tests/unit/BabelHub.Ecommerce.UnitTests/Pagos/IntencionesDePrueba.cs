using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>Constructores de intenciones, instantáneas e intentos para las pruebas del módulo <c>Pagos</c>.</summary>
internal static class IntencionesDePrueba
{
    public static readonly DateTimeOffset Inicio = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    public static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly Guid PedidoId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly Guid LineaImpresa = Guid.Parse("33333333-3333-3333-3333-333333333331");

    public static readonly Guid LineaDigital = Guid.Parse("33333333-3333-3333-3333-333333333332");

    public static LineaDeIntencion Impresa(decimal precio = 50m, int cantidad = 2) =>
        new(LineaImpresa, Guid.Parse("44444444-4444-4444-4444-444444444441"), EsDigital: false, "La ciudad y los perros", "9780306406157", cantidad, Importe.De(precio), Importe.De(precio * cantidad));

    public static LineaDeIntencion Digital(decimal precio = 25m) =>
        new(LineaDigital, Guid.Parse("44444444-4444-4444-4444-444444444442"), EsDigital: true, "Rayuela", "9780306406164", 1, Importe.De(precio), Importe.De(precio));

    public static ContactoDeEntrega Contacto() => new("Ana Pérez Quispe", "987654321", "Av. Los Olivos 123, Ayacucho");

    public static TextoAceptado Texto(string version = "2026-11") => new(version, Inicio.AddMinutes(-5));

    /// <summary>Una compra mixta a domicilio: 2 impresos de S/ 50 + 1 digital de S/ 25 + envío S/ 15 = S/ 140.</summary>
    public static InstantaneaDeIntencion Mixta(string huella = "huella-a", decimal envio = 15m) =>
        InstantaneaDeIntencion.Crear(
            Cuenta,
            TipoDeEntrega.Domicilio,
            codigoDeSucursalDeRecojo: null,
            regionDeEntrega: "AYA",
            new[] { Impresa(), Digital() },
            Importe.De(envio),
            Importe.De(100m + 25m + envio),
            huella,
            Texto(),
            Texto(),
            Texto(),
            Contacto(),
            [new EnvioDeSucursalDeIntencion("ayacucho", Importe.De(envio))]);

    /// <summary>Una compra solo digital de S/ 25, sin envío ni contacto.</summary>
    public static InstantaneaDeIntencion SoloDigital(string huella = "huella-d") =>
        InstantaneaDeIntencion.Crear(
            Cuenta,
            TipoDeEntrega.SinEntrega,
            codigoDeSucursalDeRecojo: null,
            regionDeEntrega: null,
            new[] { Digital() },
            Importe.Cero,
            Importe.De(25m),
            huella,
            Texto(),
            Texto(),
            Texto(),
            contacto: null);

    /// <summary>Una compra con recojo en sucursal: sin contacto de entrega.</summary>
    public static InstantaneaDeIntencion ConRecojo(string huella = "huella-r") =>
        InstantaneaDeIntencion.Crear(
            Cuenta,
            TipoDeEntrega.Recojo,
            codigoDeSucursalDeRecojo: "AYA-01",
            regionDeEntrega: null,
            new[] { Impresa() },
            Importe.Cero,
            Importe.De(100m),
            huella,
            Texto(),
            Texto(),
            reembolsoDigital: null,
            contacto: null);

    public static IntencionDePago Abierta(InstantaneaDeIntencion? instantanea = null, DateTimeOffset? ahora = null) =>
        IntencionDePago.Crear(PedidoId, instantanea ?? Mixta(), (ahora ?? Inicio) + PlazosDelPago.VigenciaConImpresos, ahora ?? Inicio);

    public static IntencionDePago AbiertaSoloDigital(DateTimeOffset? ahora = null) =>
        IntencionDePago.Crear(PedidoId, SoloDigital(), reservaExpiraEn: null, ahora ?? Inicio);

    /// <summary>Una intención mixta con un intento <c>iniciado</c> (<c>con_cobro</c>).</summary>
    public static IntencionDePago ConCobro(DateTimeOffset? ahora = null)
    {
        var intencion = Abierta();
        Assert.Equal(ValidacionDelCobro.Permitido, intencion.IniciarCobro(ahora ?? Inicio.AddSeconds(30)));
        return intencion;
    }

    /// <summary>Una intención mixta con el intento aprobado a tiempo.</summary>
    public static IntencionDePago Aprobada(DateTimeOffset? aprobadoEn = null)
    {
        var intencion = ConCobro();
        Assert.True(intencion.MarcarAprobada(aprobadoEn ?? Inicio.AddMinutes(1)));
        return intencion;
    }
}
