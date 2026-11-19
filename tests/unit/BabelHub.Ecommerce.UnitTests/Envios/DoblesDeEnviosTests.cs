using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;
using BabelHub.Ecommerce.UnitTests.Catalogo;

namespace BabelHub.Ecommerce.UnitTests.Envios;

/// <summary>Los dobles de prueba de Envíos (T03; ENV-01, ENV-15): imitan al almacén y a los contratos de Pedidos y fallan cerrado.</summary>
public sealed class DoblesDeEnviosTests
{
    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task El_repositorio_en_memoria_respeta_el_unico_por_linea_y_revierte_con_la_unidad_de_trabajo()
    {
        var repo = new RepositorioDeEnviosEnMemoria();
        var unidad = new UnidadDeTrabajoReversible(repo);
        var linea = Guid.NewGuid();
        var primero = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "lima-central", [linea], DateTimeOffset.UnixEpoch);
        var segundo = Envio.Crear(Guid.NewGuid(), primero.PedidoId, "lima-central", [linea, Guid.NewGuid()], DateTimeOffset.UnixEpoch);

        Assert.Equal(ResultadoDeAgregadoDeEnvio.Agregado, await repo.Agregar(primero));
        Assert.Equal(ResultadoDeAgregadoDeEnvio.LineaRepetida, await repo.Agregar(segundo));

        await Assert.ThrowsAsync<InvalidOperationException>(() => unidad.Ejecutar<int>(async token =>
        {
            await repo.Agregar(Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "lima-central", [Guid.NewGuid()], DateTimeOffset.UnixEpoch), token);
            throw new InvalidOperationException("falla");
        }));
        Assert.Single(repo.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task Pedidos_doble_responde_no_encontrado_o_no_disponible_y_cuenta_las_lecturas_dentro_de_la_transaccion()
    {
        var repo = new RepositorioDeEnviosEnMemoria();
        var unidad = new UnidadDeTrabajoReversible(repo);
        var pedidos = new PedidosParaEnviosDoble(unidad);
        var pedido = new PedidoParaEnvios(Guid.NewGuid(), "PED-2026-0001", Guid.NewGuid(), true, [new LineaDePedidoParaEnvios(Guid.NewGuid(), false, "lima-central", true)]);
        pedidos.Sembrar(pedido);

        Assert.Equal(CategoriaDeError.NoEncontrado, (await pedidos.Obtener(Guid.NewGuid())).Error!.Categoria);
        Assert.True((await pedidos.Obtener(pedido.PedidoId)).EsExito);
        Assert.Equal(0, pedidos.LecturasDentroDeLaTransaccion);

        await unidad.Ejecutar(async token => (await pedidos.Obtener(pedido.PedidoId, token)).EsExito);
        Assert.Equal(1, pedidos.LecturasDentroDeLaTransaccion);

        pedidos.NoDisponible = true;
        Assert.Equal(CategoriaDeError.NoDisponible, (await pedidos.Obtener(pedido.PedidoId)).Error!.Categoria);
    }
}
