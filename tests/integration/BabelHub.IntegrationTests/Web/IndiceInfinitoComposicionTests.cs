using System.Data.Common;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Infrastructure.Web;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// La composición del índice infinito (web-indice-infinito T06; IND-03, IND-06, AC-IND-05): el contenedor resuelve <see cref="IIndiceInfinito"/> con el puerto de bytes aleatorios criptográficos, sin duplicar un registro previo, y cien páginas se piden
/// con dobles de la unidad de trabajo y de la conexión que fallan ante cualquier llamada (sin base de datos). Sin Docker.
/// </summary>
public sealed class IndiceInfinitoComposicionTests
{
    private sealed class UnidadDeTrabajoProhibida : IUnidadDeTrabajo
    {
        public Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default) => throw new InvalidOperationException("El índice infinito no usa la unidad de trabajo.");

        public Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default) => throw new InvalidOperationException("El índice infinito no usa la unidad de trabajo.");
    }

    private sealed class ConexionProhibida : IConexionDeLaUnidadDeTrabajo
    {
        public bool HayTransaccion => throw new InvalidOperationException("El índice infinito no abre conexiones.");

        public DbConnection Conexion => throw new InvalidOperationException("El índice infinito no abre conexiones.");

        public DbTransaction Transaccion => throw new InvalidOperationException("El índice infinito no abre conexiones.");
    }

    [Fact]
    [Trait("Criterio", "AC-IND-05")]
    public void El_contenedor_resuelve_el_indice_y_cien_paginas_no_tocan_la_unidad_de_trabajo_ni_la_conexion()
    {
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IUnidadDeTrabajo, UnidadDeTrabajoProhibida>();
        servicios.AddSingleton<IConexionDeLaUnidadDeTrabajo, ConexionProhibida>();
        servicios.AgregarModuloWeb(permitirVersionesProvisionales: true);
        using var proveedor = servicios.BuildServiceProvider();

        var indice = proveedor.GetRequiredService<IIndiceInfinito>();
        var textos = new HashSet<string>();
        for (var i = 0; i < 100; i++)
        {
            var azar = indice.ElegirAlAzar();
            textos.Add(Assert.IsType<ResultadoDelIndice.Mostrar>(indice.Abrir(azar)).Pagina.Texto);
        }

        Assert.True(textos.Count > 90);
        Assert.IsType<AleatoriosSeguros>(proveedor.GetRequiredService<IAleatoriosSeguros>());
    }

    private sealed class AleatoriosPropios : IAleatoriosSeguros
    {
        public byte[] Bytes(int cantidad) => new byte[cantidad];
    }
}
