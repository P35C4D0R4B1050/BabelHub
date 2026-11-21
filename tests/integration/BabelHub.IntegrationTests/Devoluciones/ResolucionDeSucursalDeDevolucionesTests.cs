using BabelHub.Ecommerce.Application.Devoluciones.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Devoluciones;

namespace BabelHub.IntegrationTests.Devoluciones;

/// <summary>
/// La resolución de la sucursal de un actor sobre la lista de sucursales activas y la equivalencia canónica código ↔ identificador (D20; plan §6, DEV-13; AC-DEV-12, AC-DEV-30): el identificador del token se compara con el que se deriva de cada código activo;
/// una sucursal desconocida, inactiva o con el Inventario caído no se resuelve (se niega y se registra, falla cerrado).
/// </summary>
public sealed class ResolucionDeSucursalDeDevolucionesTests
{
    private sealed class InventarioDePrueba(ResultadoDeSucursalesActivas respuesta) : IInventarioDeDevoluciones
    {
        public int Consultas { get; private set; }

        public Task<ResultadoDelReingreso> Reingresar(ReingresoPorDevolucion reingreso, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDeSucursalesActivas> ListarSucursalesActivas(CancellationToken cancelacion = default)
        {
            Consultas++;
            return Task.FromResult(respuesta);
        }
    }

    private static ResolucionDeSucursalDeDevoluciones Crear(ResultadoDeSucursalesActivas respuesta) => new(new InventarioDePrueba(respuesta));

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public async Task Resuelve_el_codigo_cuyo_identificador_derivado_es_el_del_actor()
    {
        var resolucion = Crear(new ResultadoDeSucursalesActivas.Lista(["lima-central", "arequipa"]));

        var codigo = await resolucion.CodigoDe(IdentificadorDeSucursal.DesdeCodigo("arequipa")!.Value);

        Assert.Equal("arequipa", codigo);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-30")]
    public async Task Una_sucursal_que_no_esta_entre_las_activas_no_se_resuelve()
    {
        var resolucion = Crear(new ResultadoDeSucursalesActivas.Lista(["lima-central"]));

        Assert.Null(await resolucion.CodigoDe(IdentificadorDeSucursal.DesdeCodigo("arequipa")!.Value));
        Assert.Null(await resolucion.CodigoDe(Guid.Empty));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-30")]
    public async Task Con_el_inventario_caido_no_se_resuelve_ninguna()
    {
        var resolucion = Crear(new ResultadoDeSucursalesActivas.NoDisponible());

        Assert.Null(await resolucion.CodigoDe(IdentificadorDeSucursal.DesdeCodigo("lima-central")!.Value));
    }
}
