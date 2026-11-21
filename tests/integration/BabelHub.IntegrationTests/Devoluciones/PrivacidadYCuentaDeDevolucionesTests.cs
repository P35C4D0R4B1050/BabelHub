using System.Text.Json;
using BabelHub.Ecommerce.Application.Devoluciones;

namespace BabelHub.IntegrationTests.Devoluciones;

/// <summary>
/// T31 (AC-DEV-25, AC-DEV-27, AC-DEV-28, AC-DEV-29, AC-DEV-31): la Aplicación real de Devoluciones sobre PostgreSQL real: el motivo sembrado no llega a la auditoría, a los avisos, a las alertas ni a un error en todo el recorrido; la redacción por cuenta y la depuración a los
/// dos años reemplazan el texto por el valor fijo conservando pedido, líneas, importes, estados y fechas, y no tocan lo de otra cuenta.
/// </summary>
[Collection(ColeccionPostgresDeDevoluciones.Nombre)]
public sealed class PrivacidadYCuentaDeDevolucionesTests(PostgresDeDevolucionesFixture bd) : IAsyncLifetime
{
    private const string Sembrado = "SEMBRADO-motivo-9f3a71";

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Serializar(object? valor) => JsonSerializer.Serialize(valor, valor?.GetType() ?? typeof(object));

    [Fact]
    [Trait("Criterio", "AC-DEV-29")]
    [Trait("Criterio", "AC-DEV-31")]
    public async Task El_motivo_sembrado_no_llega_a_la_auditoria_a_los_avisos_a_las_alertas_ni_a_ningun_error_en_todo_el_recorrido()
    {
        var e = new EscenarioDeDevolucionesConPostgres(bd);
        var (cliente, pedido, linea) = e.SembrarPedidoDeImpresos();

        var solicitada = await e.Solicitar(cliente, pedido.PedidoId, linea.LineaId, 1, motivo: Sembrado);
        Assert.True(solicitada.EsExito, solicitada.Error?.Clave);
        var devolucionId = solicitada.Valor.DevolucionId;
        var aprobada = await e.Aprobar(devolucionId);
        var recibida = await e.Recibir(devolucionId);
        var ajena = await e.AprobarComo(BabelHub.Ecommerce.Application.Plataforma.ActorActual.DeUsuario(Guid.NewGuid(), BabelHub.Shared.Contratos.RolesDeUsuario.Cliente), devolucionId);
        var otroSobrante = await e.Solicitar(cliente, pedido.PedidoId, linea.LineaId, 1, motivo: Sembrado);

        Assert.True(aprobada.EsExito && recibida.EsExito);
        var salidas = string.Join('\n', new[]
        {
            Serializar(e.Auditoria.Solicitudes.ToArray()),
            Serializar(e.Notificador.Solicitudes.ToArray()),
            Serializar(e.Alertas.Motivos.ToArray()),
            Serializar(e.Denegaciones.Registradas.ToArray()),
            Serializar(ajena.Error),
            Serializar(otroSobrante.Error),
            ajena.Error?.ToString() ?? string.Empty,
            otroSobrante.Error?.ToString() ?? string.Empty,
        });
        Assert.NotEmpty(e.Auditoria.Solicitudes);
        Assert.NotEmpty(e.Notificador.Solicitudes);
        Assert.DoesNotContain(Sembrado, salidas, StringComparison.Ordinal);
        Assert.Equal(Sembrado, await bd.Consultar<string>($"SELECT motivo FROM devoluciones.devoluciones WHERE id = '{devolucionId:D}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-25")]
    [Trait("Criterio", "AC-DEV-28")]
    public async Task La_redaccion_por_cuenta_reemplaza_el_motivo_de_las_suyas_conserva_el_resto_y_no_toca_las_de_otra_cuenta()
    {
        var e = new EscenarioDeDevolucionesConPostgres(bd);
        var (cliente, pedido, linea) = e.SembrarPedidoDeImpresos();
        var (otro, pedidoAjeno, lineaAjena) = e.SembrarPedidoDeImpresos();
        var suya = (await e.Solicitar(cliente, pedido.PedidoId, linea.LineaId, 1, motivo: Sembrado)).Valor.DevolucionId;
        var ajena = (await e.Solicitar(otro, pedidoAjeno.PedidoId, lineaAjena.LineaId, 1, motivo: Sembrado)).Valor.DevolucionId;

        await e.Redactar(cliente);
        await e.Redactar(cliente);

        Assert.Equal(TextoLibre.Redactado, await bd.Consultar<string>($"SELECT motivo FROM devoluciones.devoluciones WHERE id = '{suya:D}'"));
        Assert.NotNull(await bd.Consultar<DateTime?>($"SELECT redactada_en FROM devoluciones.devoluciones WHERE id = '{suya:D}'"));
        Assert.Equal(Sembrado, await bd.Consultar<string>($"SELECT motivo FROM devoluciones.devoluciones WHERE id = '{ajena:D}'"));
        Assert.Equal("solicitada", await bd.Consultar<string>($"SELECT estado FROM devoluciones.lineas_de_devolucion WHERE devolucion_id = '{suya:D}'"));
        Assert.Equal(50m, await bd.Consultar<decimal>($"SELECT monto FROM devoluciones.lineas_de_devolucion WHERE devolucion_id = '{suya:D}'"));
        Assert.Equal(pedido.PedidoId, await bd.Consultar<Guid>($"SELECT pedido_id FROM devoluciones.devoluciones WHERE id = '{suya:D}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-27")]
    public async Task La_depuracion_a_los_dos_anos_redacta_solo_lo_cerrado_hace_mas_de_dos_anos_por_dia_de_Lima()
    {
        var e = new EscenarioDeDevolucionesConPostgres(bd);
        var (cliente, pedido, linea) = e.SembrarPedidoDeImpresos();
        var cerrada = (await e.Solicitar(cliente, pedido.PedidoId, linea.LineaId, 1, motivo: Sembrado)).Valor.DevolucionId;
        Assert.True((await e.Aprobar(cerrada)).EsExito);
        Assert.True((await e.Recibir(cerrada)).EsExito);
        Assert.Equal("reembolsada", await bd.Consultar<string>($"SELECT estado FROM devoluciones.lineas_de_devolucion WHERE devolucion_id = '{cerrada:D}'"));
        var (otro, pedidoAbierto, lineaAbierta) = e.SembrarPedidoDeImpresos();
        var abierta = (await e.Solicitar(otro, pedidoAbierto.PedidoId, lineaAbierta.LineaId, 1, motivo: Sembrado)).Valor.DevolucionId;

        e.Reloj.Ahora = e.Reloj.Ahora.AddDays(365);
        var antes = await e.Depurar();

        Assert.True(antes.EsExito, antes.Error?.Clave);
        Assert.Equal(0, antes.Valor);
        Assert.Equal(Sembrado, await bd.Consultar<string>($"SELECT motivo FROM devoluciones.devoluciones WHERE id = '{cerrada:D}'"));

        e.Reloj.Ahora = e.Reloj.Ahora.AddDays(366);
        var despues = await e.Depurar();

        Assert.True(despues.EsExito, despues.Error?.Clave);
        Assert.Equal(TextoLibre.Redactado, await bd.Consultar<string>($"SELECT motivo FROM devoluciones.devoluciones WHERE id = '{cerrada:D}'"));
        Assert.Equal(Sembrado, await bd.Consultar<string>($"SELECT motivo FROM devoluciones.devoluciones WHERE id = '{abierta:D}'"));
    }
}
