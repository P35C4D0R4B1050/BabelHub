using System.Text.Json;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T03 (PED-01, PED-03, PED-12, PED-32, PED-36, AC-PED-01, AC-PED-10, AC-PED-30): crear un pedido desde una compra aprobada, con el contacto redactado, el número aleatorio y las aceptaciones.</summary>
public sealed class PedidoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly AceptacionDeTexto Terminos = new("t-1", Ahora.AddMinutes(-3));
    private static readonly AceptacionDeTexto Privacidad = new("p-1", Ahora.AddMinutes(-3));
    private static readonly AceptacionDeTexto Reembolso = new("r-1", Ahora.AddMinutes(-3));

    private static DatosDeLineaNueva Impresa(string titulo = "Estructuras de datos", int cantidad = 2, decimal precio = 60m, string fuente = "LIM", bool traslado = false, Guid? trasladoId = null, DateOnly? llegada = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), false, titulo, "9780000000001", cantidad, precio, precio * cantidad, fuente, traslado, traslado ? trasladoId ?? Guid.NewGuid() : null, llegada);

    private static DatosDeLineaNueva Digital(string titulo = "Cien años de soledad", decimal precio = 20m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), true, titulo, "9780000000002", 1, precio, precio, null, false, null, null);

    private static ContactoDeEntrega Contacto() => ContactoDeEntrega.Crear("Ana Pérez", "987654321", "Jr. Lima 123")!;

    private static DatosDePedidoNuevo Datos(TipoDeEntregaDelPedido tipo, IReadOnlyList<DatosDeLineaNueva> lineas, decimal envio = 0m, ContactoDeEntrega? contacto = null, string? sucursal = null, string? region = null, bool conReembolso = false, decimal? total = null) =>
        new(Guid.NewGuid(), "ABCD234567", Cuenta, tipo, sucursal, region, envio, total ?? lineas.Sum(l => l.Importe) + envio, contacto, Terminos, Privacidad, conReembolso ? Reembolso : null, lineas, Ahora);

    [Fact]
    [Trait("Criterio", "AC-PED-01")]
    public void Un_pedido_con_un_impreso_y_un_digital_copia_titulo_isbn_y_precio_y_deja_el_impreso_confirmado_y_el_digital_disponible()
    {
        var impresa = Impresa(precio: 60m, cantidad: 1);
        var digital = Digital();

        var resultado = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [impresa, digital], sucursal: "LIM", conReembolso: true));

        Assert.True(resultado.Creado, resultado.Rechazo?.ToString());
        var pedido = resultado.Pedido!;
        Assert.Equal(EstadoDePedido.Confirmado, pedido.Estado);
        var (linea, otra) = (pedido.Lineas.Single(l => !l.EsDigital), pedido.Lineas.Single(l => l.EsDigital));
        Assert.Equal(("Estructuras de datos", "9780000000001", 60m, 1), (linea.Titulo, linea.Isbn, linea.PrecioUnitario, linea.Cantidad));
        Assert.Equal(EstadoDeLineaImpresa.Confirmado, linea.EstadoImpreso);
        Assert.Equal(EstadoDeLineaDigital.Disponible, otra.EstadoDigital);
        Assert.Equal(("Cien años de soledad", 20m), (otra.Titulo, otra.PrecioUnitario));
        Assert.Equal(80m, pedido.Total);
        Assert.Equal(Ahora, pedido.CreadoEn);
        Assert.Null(pedido.FechaEstadoFinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-10")]
    public void Un_pedido_solo_digital_nace_entregado_con_su_fecha_de_estado_final()
    {
        var resultado = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Digital, [Digital()], conReembolso: true));

        Assert.True(resultado.Creado, resultado.Rechazo?.ToString());
        Assert.Equal(EstadoDePedido.Entregado, resultado.Pedido!.Estado);
        Assert.Equal(Ahora, resultado.Pedido.FechaEstadoFinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-27")]
    public void Un_pedido_a_domicilio_guarda_el_contacto_y_uno_de_recojo_no()
    {
        var domicilio = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Domicilio, [Impresa()], envio: 12m, contacto: Contacto(), region: "ayacucho"));
        var recojo = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa()], sucursal: "LIM"));

        Assert.NotNull(domicilio.Pedido!.Contacto);
        Assert.Equal(("Ana Pérez", "987654321", "Jr. Lima 123"), (domicilio.Pedido.Contacto!.Nombre, domicilio.Pedido.Contacto.Telefono, domicilio.Pedido.Contacto.Direccion));
        Assert.Equal(12m, domicilio.Pedido.CostoDeEnvio);
        Assert.Equal("ayacucho", domicilio.Pedido.RegionDeEntrega);
        Assert.Null(recojo.Pedido!.Contacto);
        Assert.Equal("LIM", recojo.Pedido.CodigoDeSucursalDeRecojo);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-30")]
    public void Un_pedido_a_domicilio_sin_contacto_no_se_crea_para_que_Pagos_compense()
    {
        var resultado = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Domicilio, [Impresa()], envio: 12m, contacto: null, region: "ayacucho"));

        Assert.False(resultado.Creado);
        Assert.Equal(RechazoDeCrearPedido.ContactoFaltante, resultado.Rechazo);
        Assert.Null(resultado.Pedido);
    }

    [Theory]
    [InlineData(TipoDeEntregaDelPedido.Recojo)]
    [InlineData(TipoDeEntregaDelPedido.Digital)]
    public void Solo_un_pedido_a_domicilio_lleva_contacto(TipoDeEntregaDelPedido tipo)
    {
        IReadOnlyList<DatosDeLineaNueva> lineas = tipo == TipoDeEntregaDelPedido.Digital ? [Digital()] : [Impresa()];

        var resultado = Pedido.Crear(Datos(tipo, lineas, contacto: Contacto(), sucursal: tipo == TipoDeEntregaDelPedido.Recojo ? "LIM" : null, conReembolso: tipo == TipoDeEntregaDelPedido.Digital));

        Assert.Equal(RechazoDeCrearPedido.ContactoNoCorresponde, resultado.Rechazo);
    }

    [Fact]
    public void El_contacto_se_redacta_al_imprimirlo_al_serializarlo_y_en_el_pedido()
    {
        var contacto = Contacto();
        var pedido = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Domicilio, [Impresa()], envio: 12m, contacto: contacto, region: "ayacucho")).Pedido!;

        var texto = contacto.ToString() + JsonSerializer.Serialize(contacto) + JsonSerializer.Serialize(new { contacto });

        Assert.DoesNotContain("Ana", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("987654321", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Jr. Lima", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Ana", pedido.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "987654321", "Jr. Lima 123")]
    [InlineData("Ana", null, "Jr. Lima 123")]
    [InlineData("Ana", "abc-defg", "Jr. Lima 123")]
    [InlineData("Ana", "()", "Jr. Lima 123")]
    [InlineData("Ana", "987654321", " ")]
    [InlineData("Ana\n", "987654321", "Jr. Lima 123")]
    public void Un_contacto_incompleto_o_con_caracteres_de_control_no_se_crea(string? nombre, string? telefono, string? direccion)
    {
        Assert.Null(ContactoDeEntrega.Crear(nombre, telefono, direccion));
    }

    [Fact]
    public void Una_linea_impresa_con_traslado_guarda_la_fuente_el_traslado_y_su_fecha_estimada_y_sin_fecha_es_a_confirmar()
    {
        var traslado = Guid.NewGuid();
        var llegada = new DateOnly(2026, 11, 25);

        var pedido = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa(fuente: "CUS", traslado: true, trasladoId: traslado, llegada: llegada), Impresa("Otro", fuente: "AYA", traslado: true)], sucursal: "LIM")).Pedido!;

        var con = pedido.Lineas.Single(l => l.Titulo == "Estructuras de datos");
        var sin = pedido.Lineas.Single(l => l.Titulo == "Otro");
        Assert.Equal(("CUS", true, traslado, llegada), (con.CodigoDeFuente, con.RequiereTraslado, con.TrasladoId, con.FechaEstimadaDeLlegada));
        Assert.Null(sin.FechaEstimadaDeLlegada);
    }

    [Fact]
    public void El_numero_de_pedido_es_de_diez_caracteres_del_alfabeto_sin_ambiguos_y_uno_mal_formado_se_rechaza()
    {
        Assert.NotNull(NumeroDePedido.Crear("ABCD234567"));
        Assert.Equal(10, NumeroDePedido.Longitud);
        Assert.All(new[] { "abcd234567", "ABCD23456", "ABCD2345678", "ABCD23456I", "ABCD23456O", "ABCD23456 ", "ABCD23456-" }, n => Assert.Null(NumeroDePedido.Crear(n)));
        var generado = NumeroDePedido.Generar(maximoExclusivo => 3 % maximoExclusivo);
        Assert.Equal(10, generado.Valor.Length);
        Assert.NotNull(NumeroDePedido.Crear(generado.Valor));
    }

    [Fact]
    public void Un_pedido_con_un_numero_mal_formado_no_se_crea()
    {
        var datos = Datos(TipoDeEntregaDelPedido.Recojo, [Impresa()], sucursal: "LIM") with { Numero = "mal" };

        Assert.Equal(RechazoDeCrearPedido.NumeroInvalido, Pedido.Crear(datos).Rechazo);
    }

    [Fact]
    public void Un_pedido_con_identificadores_vacios_precio_cero_o_version_vacia_no_se_crea()
    {
        var valida = Datos(TipoDeEntregaDelPedido.Recojo, [Impresa()], sucursal: "LIM");

        Assert.Equal(RechazoDeCrearPedido.PedidoInvalido, Pedido.Crear(valida with { PedidoId = Guid.Empty }).Rechazo);
        Assert.Equal(RechazoDeCrearPedido.PedidoInvalido, Pedido.Crear(valida with { CuentaId = Guid.Empty }).Rechazo);
        Assert.Equal(RechazoDeCrearPedido.AceptacionFaltante, Pedido.Crear(valida with { Terminos = new AceptacionDeTexto(" ", Ahora) }).Rechazo);
        Assert.Equal(RechazoDeCrearPedido.LineaInvalida, Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa(precio: 0m)], sucursal: "LIM", total: 1m)).Rechazo);
    }

    [Fact]
    public void Se_exigen_los_terminos_y_la_privacidad_y_el_reembolso_solo_con_digitales()
    {
        var sinTerminos = Datos(TipoDeEntregaDelPedido.Recojo, [Impresa()], sucursal: "LIM") with { Terminos = null! };
        var sinPrivacidad = Datos(TipoDeEntregaDelPedido.Recojo, [Impresa()], sucursal: "LIM") with { Privacidad = null! };
        var digitalSinReembolso = Datos(TipoDeEntregaDelPedido.Recojo, [Impresa(), Digital()], sucursal: "LIM", conReembolso: false);
        var impresoSinReembolso = Datos(TipoDeEntregaDelPedido.Recojo, [Impresa()], sucursal: "LIM", conReembolso: false);

        Assert.Equal(RechazoDeCrearPedido.AceptacionFaltante, Pedido.Crear(sinTerminos).Rechazo);
        Assert.Equal(RechazoDeCrearPedido.AceptacionFaltante, Pedido.Crear(sinPrivacidad).Rechazo);
        Assert.Equal(RechazoDeCrearPedido.AceptacionFaltante, Pedido.Crear(digitalSinReembolso).Rechazo);
        Assert.True(Pedido.Crear(impresoSinReembolso).Creado);
    }

    [Fact]
    public void Las_aceptaciones_guardan_la_version_y_la_fecha_de_cada_texto()
    {
        var pedido = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa(), Digital()], sucursal: "LIM", conReembolso: true)).Pedido!;

        Assert.Equal(("t-1", Ahora.AddMinutes(-3)), (pedido.Terminos.Version, pedido.Terminos.AceptadoEn));
        Assert.Equal("p-1", pedido.Privacidad.Version);
        Assert.Equal("r-1", pedido.ReembolsoDigital!.Version);
    }

    [Fact]
    public void Una_edicion_o_una_linea_repetida_o_un_importe_o_total_incoherente_se_rechaza()
    {
        var uno = Impresa();
        var igualEdicion = uno with { LineaId = Guid.NewGuid() };
        var igualLinea = Impresa() with { LineaId = uno.LineaId };
        var importeMal = uno with { Importe = uno.Importe + 1m };

        Assert.Equal(RechazoDeCrearPedido.LineaRepetida, Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [uno, igualEdicion], sucursal: "LIM")).Rechazo);
        Assert.Equal(RechazoDeCrearPedido.LineaRepetida, Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [uno, igualLinea], sucursal: "LIM")).Rechazo);
        Assert.Equal(RechazoDeCrearPedido.LineaInvalida, Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [importeMal], sucursal: "LIM")).Rechazo);
        Assert.Equal(RechazoDeCrearPedido.TotalIncoherente, Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [uno], sucursal: "LIM", total: 1m)).Rechazo);
        Assert.Equal(RechazoDeCrearPedido.SinLineas, Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [], sucursal: "LIM", total: 1m)).Rechazo);
    }

    [Fact]
    public void Una_linea_con_cantidad_o_titulo_invalidos_o_un_impreso_sin_fuente_o_con_traslado_incoherente_se_rechaza()
    {
        Assert.Equal(RechazoDeCrearPedido.LineaInvalida, Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa(cantidad: 0)], sucursal: "LIM", total: 1m)).Rechazo);
        Assert.Equal(RechazoDeCrearPedido.LineaInvalida, Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa(titulo: " ")], sucursal: "LIM")).Rechazo);
        Assert.Equal(RechazoDeCrearPedido.FuenteFaltante, Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa(fuente: "")], sucursal: "LIM")).Rechazo);
        Assert.Equal(RechazoDeCrearPedido.TrasladoIncoherente, Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa() with { TrasladoId = Guid.NewGuid() }], sucursal: "LIM")).Rechazo);
        Assert.Equal(RechazoDeCrearPedido.TrasladoIncoherente, Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa(traslado: true) with { TrasladoId = null }], sucursal: "LIM")).Rechazo);
    }

    [Fact]
    public void La_entrega_debe_ser_coherente_con_las_lineas_y_con_la_sucursal_o_la_region()
    {
        Assert.Equal(RechazoDeCrearPedido.EntregaIncoherente, Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa()], sucursal: null)).Rechazo);
        Assert.Equal(RechazoDeCrearPedido.EntregaIncoherente, Pedido.Crear(Datos(TipoDeEntregaDelPedido.Domicilio, [Impresa()], envio: 12m, contacto: Contacto(), region: null)).Rechazo);
        Assert.Equal(RechazoDeCrearPedido.EntregaIncoherente, Pedido.Crear(Datos(TipoDeEntregaDelPedido.Digital, [Impresa()], conReembolso: true)).Rechazo);
        Assert.Equal(RechazoDeCrearPedido.EntregaIncoherente, Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Digital()], sucursal: "LIM", conReembolso: true)).Rechazo);
        Assert.Equal(RechazoDeCrearPedido.EntregaIncoherente, Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa()], envio: 5m, sucursal: "LIM")).Rechazo);
    }

    [Fact]
    public void El_estado_del_pedido_se_recalcula_al_cambiar_una_linea_y_fija_la_fecha_final_al_entregarse_o_anularse()
    {
        var pedido = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa()], sucursal: "LIM")).Pedido!;
        var linea = pedido.Lineas.Single();

        Assert.True(linea.PasarA(EstadoDeLineaImpresa.Anulado, Ahora.AddHours(1)));
        pedido.RecalcularEstado(Ahora.AddHours(1));

        Assert.Equal(EstadoDePedido.Anulado, pedido.Estado);
        Assert.Equal(Ahora.AddHours(1), pedido.FechaEstadoFinal);
    }
}
