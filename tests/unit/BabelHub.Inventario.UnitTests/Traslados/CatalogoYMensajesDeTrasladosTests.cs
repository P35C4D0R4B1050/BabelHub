using System.Text.Json;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Domain.Traslados;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>T04: el contrato del módulo, la auditoría con la lista blanca del catálogo y los mensajes del buzón de salida.</summary>
public sealed class CatalogoYMensajesDeTrasladosTests
{
    private const string Centinela = "CENTINELA-SECRETO-777";

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public void UnCambioDeEstadoAvisaALasDosSucursales_ConCamposTipadosYSinTextoLibre()
    {
        var traslado = Traslado.SolicitarReposicion(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, Centinela, Guid.NewGuid(), EntornoDeTraslados.Ahora);
        traslado.AsignarNumero(42);
        traslado.Aprobar(EntornoDeTraslados.Ahora);
        traslado.Despachar(EntornoDeTraslados.Ahora);
        traslado.RegistrarGuia(Centinela);

        var mensajes = ConversionDeTraslados.Mensajes(traslado, "cusco", "ayacucho", EntornoDeTraslados.Ahora);

        Assert.Equal(2, mensajes.Count);
        Assert.All(mensajes, m => Assert.Equal(("aviso", $"traslado:{traslado.Id:D}:en-transito"), (m.Tipo, m.ClaveDeEvento)));
        Assert.Equal(["ayacucho", "cusco"], mensajes.Select(m => m.SucursalCodigo).Order().ToArray());
        var alOrigen = JsonDocument.Parse(mensajes.Single(m => m.SucursalCodigo == "cusco").Carga).RootElement;
        Assert.Equal((42L, "en-transito", "origen", 3), (alOrigen.GetProperty("numeroDeTraslado").GetInt64(), alOrigen.GetProperty("estado").GetString(), alOrigen.GetProperty("rolDeLaSucursal").GetString(), alOrigen.GetProperty("cantidad").GetInt32()));
        Assert.All(mensajes, m => Assert.DoesNotContain("CENTINELA", m.Carga, StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-09")]
    [InlineData("despachado", false, true)]
    [InlineData("recibido", false, true)]
    [InlineData("recibido", true, false)]
    [InlineData("cancelado", false, true)]
    [InlineData("cancelado", true, true)]
    [InlineData("aprobado", false, false)]
    public void ElEstadoDelPedidoSeInformaSoloAlPasarAEnTransitoRecibidoOCancelado_YNuncaUnRecibidoDeUnPedidoAnulado(string cambio, bool pedidoAnulado, bool seInforma)
    {
        var traslado = Traslado.VincularAUnPedido(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), EstimacionDeLlegada.Calcular(new DateOnly(2026, 11, 14), 2), null, EntornoDeTraslados.Ahora);
        traslado.AsignarNumero(7);
        switch (cambio)
        {
            case "aprobado": traslado.Aprobar(EntornoDeTraslados.Ahora); break;
            case "despachado": traslado.Aprobar(EntornoDeTraslados.Ahora); traslado.Despachar(EntornoDeTraslados.Ahora); break;
            case "recibido":
                traslado.Aprobar(EntornoDeTraslados.Ahora);
                traslado.Despachar(EntornoDeTraslados.Ahora);
                if (pedidoAnulado)
                {
                    traslado.AnularPorPedido(EntornoDeTraslados.Ahora);
                }

                traslado.Recibir(EntornoDeTraslados.Ahora);
                break;
            default:
                traslado.AnularPorPedido(EntornoDeTraslados.Ahora);
                break;
        }

        var mensajes = ConversionDeTraslados.Mensajes(traslado, "cusco", "ayacucho", EntornoDeTraslados.Ahora);

        var delPedido = mensajes.SingleOrDefault(m => m.Tipo == "estado-de-pedido");
        Assert.Equal(seInforma, delPedido is not null);
        if (delPedido is not null)
        {
            Assert.Equal("ayacucho", delPedido.SucursalCodigo);
            var carga = JsonDocument.Parse(delPedido.Carga).RootElement;
            Assert.Equal(traslado.PedidoId!.Value.ToString("D"), carga.GetProperty("pedidoId").GetString());
            Assert.Equal("2026-11-16", carga.GetProperty("fechaEstimadaDeLlegada").GetString());
            Assert.False(carga.GetProperty("estimadaPorConfirmar").GetBoolean());
        }
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-09")]
    public void LaEstimacionPorConfirmarViajaSinFecha()
    {
        var traslado = Traslado.VincularAUnPedido(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), EstimacionDeLlegada.Calcular(new DateOnly(2026, 11, 14), null), null, EntornoDeTraslados.Ahora);
        traslado.AsignarNumero(8);
        traslado.Aprobar(EntornoDeTraslados.Ahora);
        traslado.Despachar(EntornoDeTraslados.Ahora);

        var delPedido = ConversionDeTraslados.Mensajes(traslado, "cusco", "ayacucho", EntornoDeTraslados.Ahora).Single(m => m.Tipo == "estado-de-pedido");

        var carga = JsonDocument.Parse(delPedido.Carga).RootElement;
        Assert.Equal((JsonValueKind.Null, true), (carga.GetProperty("fechaEstimadaDeLlegada").ValueKind, carga.GetProperty("estimadaPorConfirmar").GetBoolean()));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public void LasEntradasYLasSalidas_QueLlevanTextoLibre_NoLoImprimen()
    {
        var id = Guid.NewGuid();
        var traslado = new TrasladoRegistrado(id, 1, Guid.NewGuid(), "cusco", "ayacucho", 1, "aprobado", Centinela, Centinela, null, false, false, null, false, Centinela, null, EntornoDeTraslados.Ahora, null, null, null, null, 1);

        var textos = new object[]
        {
            new SolicitarTrasladoEntrada(Guid.NewGuid(), 1, "cusco", "ayacucho", Centinela),
            new RechazarTrasladoEntrada(id, Centinela),
            new CancelarTrasladoEntrada(id, Centinela),
            new RegistrarGuiaDeTrasladoEntrada(id, Centinela),
            traslado,
        };

        Assert.All(textos, t => Assert.DoesNotContain("CENTINELA", t.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public void ElDetalleDelLimiteDeTrasladosViajaYSeLeeSinPerdida()
    {
        var disponible = new DateTimeOffset(2026, 11, 14, 16, 0, 0, TimeSpan.Zero);

        var errores = DetalleDeLimiteDeTraslados.Codificar(new RechazoDePresupuestoDeTraslados("traslados-del-personal", 1800, disponible));

        Assert.True(DetalleDeLimiteDeTraslados.TryLeer(errores, out var rechazo));
        Assert.Equal(("traslados-del-personal", 1800, disponible), (rechazo!.Motivo, rechazo.EsperaEnSegundos, rechazo.DisponibleDesde));
        Assert.False(DetalleDeLimiteDeTraslados.TryLeer([], out _));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task ElBuzonNoAbortaPorUnDuplicado_YElMensajeNoSeGuardaDosVeces()
    {
        var entorno = new EntornoDeTraslados();
        var mensaje = new MensajeSaliente(Guid.NewGuid(), "aviso", "traslado:x:aprobado", "cusco", "{}", EntornoDeTraslados.Ahora);

        await entorno.Buzon.Insertar([mensaje]);
        await entorno.Buzon.Insertar([mensaje with { Id = Guid.NewGuid() }]);

        Assert.Single(entorno.Buzon.Mensajes);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-12")]
    public void ElPuertoDelEnvioAlEcommerceUsaModelosPropios_YNoReferenciaShared()
    {
        var tiposDelPuerto = typeof(IEnvioAlEcommerce).GetMethods().SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)).ToList();

        Assert.DoesNotContain(tiposDelPuerto.SelectMany(t => t.IsGenericType ? t.GetGenericArguments() : [t]), t => t.Namespace?.StartsWith("BabelHub.Shared", StringComparison.Ordinal) == true);
    }

}
