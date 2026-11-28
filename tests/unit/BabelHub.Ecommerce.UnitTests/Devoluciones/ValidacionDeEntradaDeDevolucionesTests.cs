using System.Text.Json;
using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>
/// La validación de entrada de las solicitudes, los reembolsos de un digital y los reportes (T18; DEV-29, DEV-31; AC-DEV-31): el largo acotado del motivo (300) y del texto (2000) sin caracteres de control ni de formato, de 1 a 20 líneas con cantidades válidas, y ningún error que repita lo escrito.
/// El rechazo de un adjunto y la salida codificada son de la Presentación (T23, T24 y T28).
/// </summary>
public sealed class ValidacionDeEntradaDeDevolucionesTests
{
    private static readonly DateTimeOffset Ahora = EntornoDeDevoluciones.AhoraInicial;

    private const string Sembrado = "<script>alert('SEMBRADO')</script>";

    private static (EntornoDeDevoluciones Entorno, Guid PedidoId, Guid[] Lineas) ConImpresos(int cantidad)
    {
        var entorno = new EntornoDeDevoluciones();
        var lineas = Enumerable.Range(0, cantidad).Select(_ => EntornoDeDevoluciones.Entregada(Ahora.AddDays(-2), 3, 10m)).ToArray();
        var pedido = entorno.SembrarPedido(lineas);
        return (entorno, pedido.PedidoId, [.. lineas.Select(l => l.LineaId)]);
    }

    private static SolicitarDevolucionDeImpresoEntrada Solicitud(Guid pedido, IEnumerable<Guid> lineas, string? motivo, int cantidad = 1) =>
        new(pedido, [.. lineas.Select(l => new LineaPedidaParaDevolver(l, cantidad))], new TextoLibre(motivo), "lima-central");

    private static string Repeticion(int largo) => new('a', largo);

    // ---------- El motivo y el texto ----------

    [Theory]
    [Trait("Criterio", "AC-DEV-31")]
    [InlineData(300, true)]
    [InlineData(301, false)]
    [InlineData(1, true)]
    public async Task El_motivo_de_una_solicitud_admite_hasta_300_caracteres(int largo, bool valido)
    {
        var (entorno, pedido, lineas) = ConImpresos(1);

        var resultado = await entorno.SolicitarImpreso.Ejecutar(Solicitud(pedido, lineas, Repeticion(largo)));

        Assert.Equal(valido, resultado.EsExito);
        if (!valido)
        {
            Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
            Assert.Empty(entorno.Repositorio.Todas);
        }
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-31")]
    [InlineData(300, 2000, true)]
    [InlineData(301, 10, false)]
    [InlineData(10, 2000, true)]
    [InlineData(10, 2001, false)]
    public async Task El_reporte_admite_hasta_300_de_motivo_y_2000_de_texto(int motivo, int texto, bool valido)
    {
        var cliente = new EntornoDeDevoluciones();
        var digital = EntornoDeDevoluciones.Digital();
        var pedido = new PedidoParaDevolucionesParaPruebas(cliente, digital).Pedido;
        cliente.Accesos.Sembrar(pedido.CuentaId, pedido.PedidoId, digital.EdicionId, enlaceEntregado: true);

        var resultado = await cliente.Reportar.Ejecutar(new ReportarArchivoDefectuosoEntrada(pedido.PedidoId, digital.LineaId, new TextoLibre(Repeticion(motivo)), new TextoLibre(Repeticion(texto))));

        Assert.Equal(valido, resultado.EsExito);
        Assert.Equal(valido ? 1 : 0, cliente.Repositorio.Todas.Count);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-31")]
    [InlineData("salto\nde linea")]
    [InlineData("nulo\0aqui")]
    [InlineData("derecha-a-izquierda‮oculta")]
    [InlineData("ancho-cero​oculto")]
    [InlineData("   ")]
    public async Task Un_motivo_con_caracteres_de_control_o_de_formato_o_vacio_es_validacion_en_las_tres_entradas(string motivo)
    {
        var (entorno, pedido, lineas) = ConImpresos(1);
        var digital = EntornoDeDevoluciones.Digital();
        var pedidoDigital = new PedidoParaDevolucionesParaPruebas(entorno, digital).Pedido;
        entorno.Accesos.Sembrar(pedidoDigital.CuentaId, pedidoDigital.PedidoId, digital.EdicionId, enlaceEntregado: true);

        var impreso = await entorno.SolicitarImpreso.Ejecutar(Solicitud(pedido, lineas, motivo));
        var reembolso = await entorno.SolicitarDigital.Ejecutar(new PedirReembolsoDeDigitalEntrada(pedidoDigital.PedidoId, digital.LineaId, new TextoLibre(motivo)));
        var reporte = await entorno.Reportar.Ejecutar(new ReportarArchivoDefectuosoEntrada(pedidoDigital.PedidoId, digital.LineaId, new TextoLibre(motivo), new TextoLibre("texto valido")));
        var textoMalo = await entorno.Reportar.Ejecutar(new ReportarArchivoDefectuosoEntrada(pedidoDigital.PedidoId, digital.LineaId, new TextoLibre("motivo valido"), new TextoLibre(motivo)));

        Assert.All(new[] { impreso, reembolso, reporte, textoMalo }, r => Assert.Equal(CategoriaDeError.Validacion, r.Error!.Categoria));
        Assert.Empty(entorno.Repositorio.Todas);
        Assert.Equal(0, entorno.Pedidos.Lecturas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-31")]
    public async Task Un_script_sembrado_es_texto_valido_se_guarda_tal_cual_y_no_llega_a_la_auditoria_ni_a_los_avisos()
    {
        var (entorno, pedido, lineas) = ConImpresos(1);

        var resultado = await entorno.SolicitarImpreso.Ejecutar(Solicitud(pedido, lineas, Sembrado));
        var admin = new EntornoDeDevoluciones(ActorDePrueba.DeAdministradorCentral()).ConLosDatosDe(entorno);
        await admin.Gestion.Aprobar(resultado.Valor.DevolucionId);

        Assert.True(resultado.EsExito);
        Assert.Equal(Sembrado, entorno.Repositorio.Todas.Single().Motivo.Valor);
        var salidas = JsonSerializer.Serialize(new { auditoria = entorno.Auditoria.Solicitudes.Select(s => s.Detalle), avisos = entorno.Notificador.Solicitudes.Select(a => a.Campos), vista = resultado.Valor });
        Assert.DoesNotContain("SEMBRADO", salidas, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", salidas, StringComparison.Ordinal);
        Assert.DoesNotContain("003Cscript", salidas, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Las líneas ----------

    [Theory]
    [Trait("Criterio", "AC-DEV-31")]
    [InlineData(1, true)]
    [InlineData(20, true)]
    [InlineData(21, false)]
    [InlineData(0, false)]
    public async Task Una_solicitud_admite_de_1_a_20_lineas(int cantidad, bool valido)
    {
        var (entorno, pedido, lineas) = ConImpresos(Math.Max(cantidad, 1));

        var resultado = await entorno.SolicitarImpreso.Ejecutar(Solicitud(pedido, lineas.Take(cantidad), "defecto"));

        Assert.Equal(valido, resultado.EsExito);
        if (!valido)
        {
            Assert.Equal("lineas", Assert.Single(resultado.Error!.Errores).Campo);
        }
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-31")]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Una_cantidad_que_no_es_positiva_es_validacion_y_una_linea_repetida_tambien(int cantidad)
    {
        var (entorno, pedido, lineas) = ConImpresos(1);

        var invalida = await entorno.SolicitarImpreso.Ejecutar(Solicitud(pedido, lineas, "defecto", cantidad));
        var repetida = await entorno.SolicitarImpreso.Ejecutar(Solicitud(pedido, [lineas[0], lineas[0]], "defecto"));

        Assert.Equal(CategoriaDeError.Validacion, invalida.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, repetida.Error!.Categoria);
        Assert.Empty(entorno.Repositorio.Todas);
        Assert.Equal(0, entorno.Pedidos.Lecturas);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-41")]
    public async Task Dos_lineas_de_la_misma_edicion_impresa_se_aceptan_en_una_devolucion()
    {
        var entorno = new EntornoDeDevoluciones();
        var edicion = Guid.NewGuid();
        var a = EntornoDeDevoluciones.Entregada(Ahora.AddDays(-2), 3, 10m) with { EdicionId = edicion };
        var b = EntornoDeDevoluciones.Entregada(Ahora.AddDays(-2), 3, 10m) with { EdicionId = edicion };
        var pedido = entorno.SembrarPedido(a, b);

        var resultado = await entorno.SolicitarImpreso.Ejecutar(Solicitud(pedido.PedidoId, [a.LineaId, b.LineaId], "defecto"));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(new[] { a.LineaId, b.LineaId }, Assert.Single(entorno.Repositorio.Todas).Lineas.Select(l => l.LineaDePedidoId));
    }
}
