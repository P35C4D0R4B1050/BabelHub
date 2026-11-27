using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.UnitTests.Fuentes;
using BabelHub.Inventario.UnitTests.Soporte;
using static BabelHub.Inventario.UnitTests.Reservas.EntornoDeReservas;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>T37: reservar, reemplazar, consultar la selección y confirmar cuando cada línea declara su sucursal (SEL-41, SEL-43, SEL-44): sin estrategia, sin traslado ni plazo.</summary>
public sealed class ReservarPorSucursalTests
{
    private static (EntornoDeReservas Entorno, Guid Libro) Preparar(int enCusco, int enAyacucho, int enLima = 0, ActorDePrueba? actor = null)
    {
        var entorno = new EntornoDeReservas(actor);
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Cusco.Id, libro, enCusco, 0);
        entorno.Base.Existencias.Poner(entorno.Base.Ayacucho.Id, libro, enAyacucho, 0);
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, enLima, 0);
        return (entorno, libro);
    }

    private static ReservarEntrada Recojo(IReadOnlyList<LineaSolicitada> lineas, Guid? pedidoId = null, string? clave = null) =>
        new(pedidoId ?? Guid.NewGuid(), TipoDeEntregaDeReserva.Recojo, null, null, lineas, clave ?? ClaveNueva());

    private static List<LineaSinStock> SinStock(ErrorDeAplicacion error)
    {
        Assert.Equal((CategoriaDeError.Conflicto, "stock-insuficiente"), (error.Categoria, error.Clave));
        return [.. error.Errores.Select(e =>
        {
            Assert.True(DetalleDeFaltaDeStock.TryLeer(e, out var linea));
            return linea!;
        })];
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-42")]
    public async Task Recojo_ApartaCadaLineaEnSuSucursal_SinTraslado_YGuardaElRecojoSinSucursalDeCabecera()
    {
        var (entorno, libro) = Preparar(enCusco: 20, enAyacucho: 6);
        var deCusco = Linea(libro, 18, sucursal: "cusco");
        var deAyacucho = Linea(libro, 6, sucursal: "ayacucho");

        var resultado = await entorno.Reservar().Ejecutar(Recojo([deCusco, deAyacucho]));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((20, 18), entorno.FilaDe(entorno.Base.Cusco, libro));
        Assert.Equal((6, 6), entorno.FilaDe(entorno.Base.Ayacucho, libro));
        var porLinea = resultado.Valor.Lineas.ToDictionary(l => l.LineaId);
        Assert.Equal(("cusco", false, (int?)null), (porLinea[deCusco.LineaId].CodigoDeFuente, porLinea[deCusco.LineaId].RequiereTraslado, porLinea[deCusco.LineaId].PlazoEstimadoEnDias));
        Assert.Equal(("ayacucho", false, (int?)null), (porLinea[deAyacucho.LineaId].CodigoDeFuente, porLinea[deAyacucho.LineaId].RequiereTraslado, porLinea[deAyacucho.LineaId].PlazoEstimadoEnDias));
        var guardada = Assert.Single(entorno.Reservas.Guardadas);
        Assert.True(guardada.EsRecojoPorSucursal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-42")]
    public async Task Domicilio_SeApartaEnLaSucursalDeclarada_AunqueLaRegionDeLaDireccionTengaStock()
    {
        var (entorno, libro) = Preparar(enCusco: 3, enAyacucho: 0, enLima: 9);

        var resultado = await entorno.Reservar().Ejecutar(ADomicilio("lima", [Linea(libro, 2, sucursal: "cusco")]));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal("cusco", Assert.Single(resultado.Valor.Lineas).CodigoDeFuente);
        Assert.Equal((3, 2), entorno.FilaDe(entorno.Base.Cusco, libro));
        Assert.Equal((9, 0), entorno.FilaDe(entorno.Base.Lima, libro));
        Assert.Equal((TipoDeEntrega.Domicilio, "lima"), (entorno.Reservas.Guardadas[0].TipoDeEntrega, entorno.Reservas.Guardadas[0].RegionDeEntrega));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-43")]
    public async Task DosLineasQueJuntasNoCabenEnLaSucursal_RechazanTodo_IndicanLaSegundaYNoSeReasignan()
    {
        var (entorno, libro) = Preparar(enCusco: 30, enAyacucho: 5);
        var primera = Linea(libro, 3, sucursal: "ayacucho");
        var segunda = Linea(libro, 3, sucursal: "ayacucho");

        var resultado = await entorno.Reservar().Ejecutar(Recojo([primera, segunda]));

        var linea = Assert.Single(SinStock(resultado.Error!));
        Assert.Equal(new LineaSinStock(segunda.LineaId, libro, 3, 35, 5), linea);
        Assert.Empty(entorno.Base.Existencias.Movimientos);
        Assert.Empty(entorno.Reservas.Guardadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-43")]
    public async Task TresYDosCabenEnCinco()
    {
        var (entorno, libro) = Preparar(enCusco: 30, enAyacucho: 5);

        var resultado = await entorno.Reservar().Ejecutar(Recojo([Linea(libro, 3, sucursal: "ayacucho"), Linea(libro, 2, sucursal: "ayacucho")]));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((5, 5), entorno.FilaDe(entorno.Base.Ayacucho, libro));
        Assert.Equal((30, 0), entorno.FilaDe(entorno.Base.Cusco, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-43")]
    public async Task LaLineaQueNoCabeNoConsume_YSoloEllaSeIndica()
    {
        var (entorno, libro) = Preparar(enCusco: 30, enAyacucho: 5);
        var deSeis = Linea(libro, 6, sucursal: "ayacucho");

        var resultado = await entorno.Reservar().Ejecutar(Recojo([deSeis, Linea(libro, 3, sucursal: "ayacucho")]));

        Assert.Equal(deSeis.LineaId, Assert.Single(SinStock(resultado.Error!)).LineaId);
        Assert.Empty(entorno.Base.Existencias.Movimientos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-43")]
    public void ElDetalleDeLaFaltaDeStockViajaConYSinElVendibleDeLaSucursal()
    {
        var conSucursal = new LineaSinStock(Guid.NewGuid(), Guid.NewGuid(), 3, 35, 0);
        var enLaRed = new LineaSinStock(Guid.NewGuid(), Guid.NewGuid(), 3, 35);

        Assert.True(DetalleDeFaltaDeStock.TryLeer(DetalleDeFaltaDeStock.Codificar(conSucursal), out var primera));
        Assert.True(DetalleDeFaltaDeStock.TryLeer(DetalleDeFaltaDeStock.Codificar(enLaRed), out var segunda));
        Assert.Equal((conSucursal, enLaRed), (primera, segunda));
        Assert.False(DetalleDeFaltaDeStock.TryLeer(new ErrorDeCampo($"lineas.{Guid.NewGuid():D}", $"sin-stock:{Guid.NewGuid():D}:3:35:-1"), out _));
        Assert.False(DetalleDeFaltaDeStock.TryLeer(new ErrorDeCampo($"lineas.{Guid.NewGuid():D}", $"sin-stock:{Guid.NewGuid():D}:3:35:1:2"), out _));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-42")]
    public async Task RepetirLaClaveDeUnaReservaPorSucursal_DevuelveLaOriginalSinApartarOtraVez()
    {
        var (entorno, libro) = Preparar(enCusco: 10, enAyacucho: 10);
        var entrada = Recojo([Linea(libro, 4, sucursal: "cusco")]);
        Assert.True((await entorno.Reservar().Ejecutar(entrada)).EsExito);

        var repetida = await entorno.Reservar().Ejecutar(entrada);

        Assert.True(repetida.Valor.EsRepeticion);
        Assert.Equal((10, 4), entorno.FilaDe(entorno.Base.Cusco, libro));
        Assert.Single(entorno.Reservas.Guardadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-50")]
    public async Task ElReemplazoCuentaLoQueDevuelveLaAnterior_YRepartePorSucursales()
    {
        var (entorno, libro) = Preparar(enCusco: 30, enAyacucho: 6);
        var pedido = Guid.NewGuid();
        Assert.True((await entorno.Reservar().Ejecutar(Recojo([Linea(libro, 6, sucursal: "ayacucho")], pedido))).EsExito);

        var reemplazo = await entorno.Reservar().Ejecutar(Recojo([Linea(libro, 4, sucursal: "ayacucho"), Linea(libro, 2, sucursal: "cusco")], pedido));

        Assert.True(reemplazo.EsExito, reemplazo.Error?.Clave);
        Assert.Equal((6, 4), entorno.FilaDe(entorno.Base.Ayacucho, libro));
        Assert.Equal((30, 2), entorno.FilaDe(entorno.Base.Cusco, libro));
        Assert.Equal(1, entorno.Reservas.Guardadas.Max(r => r.Reemplazos));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-50")]
    public async Task UnReemplazoQuePideMasDeLoQueTieneUnaSucursal_SeRechazaSinCrearOtraReserva()
    {
        var (entorno, libro) = Preparar(enCusco: 30, enAyacucho: 6);
        var pedido = Guid.NewGuid();
        Assert.True((await entorno.Reservar().Ejecutar(Recojo([Linea(libro, 6, sucursal: "ayacucho")], pedido))).EsExito);

        var reemplazo = await entorno.Reservar().Ejecutar(Recojo([Linea(libro, 7, sucursal: "ayacucho")], pedido));

        // La transacción deshace la liberación de la anterior (se prueba con PostgreSQL); aquí, que no nace otra reserva y que el vendible informado cuenta lo que se devolvía.
        Assert.Equal(6, Assert.Single(SinStock(reemplazo.Error!)).DisponibleEnLaSucursal);
        Assert.Single(entorno.Reservas.Guardadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-50")]
    public async Task UnaReservaSinSucursalPorLinea_SePuedeReemplazarPorUnaPorSucursal()
    {
        var (entorno, libro) = Preparar(enCusco: 10, enAyacucho: 0, enLima: 3);
        var pedido = Guid.NewGuid();
        Assert.True((await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro, 3)], pedido))).EsExito);

        var reemplazo = await entorno.Reservar().Ejecutar(Recojo([Linea(libro, 2, sucursal: "cusco")], pedido));

        Assert.True(reemplazo.EsExito, reemplazo.Error?.Clave);
        Assert.Equal((3, 0), entorno.FilaDe(entorno.Base.Lima, libro));
        Assert.Equal((10, 2), entorno.FilaDe(entorno.Base.Cusco, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-42")]
    public async Task ConfirmarUnRecojoPorSucursal_NoPideNingunTraslado()
    {
        var (entorno, libro) = Preparar(enCusco: 20, enAyacucho: 6);
        var pedido = Guid.NewGuid();
        Assert.True((await entorno.Reservar().Ejecutar(Recojo([Linea(libro, 18, sucursal: "cusco"), Linea(libro, 6, sucursal: "ayacucho")], pedido))).EsExito);
        entorno.Base.Actor.Establecer(ActorDePrueba.DeIntegracion("expiracion-de-reservas").Actual);

        var confirmada = await entorno.Confirmar().Ejecutar(new ConfirmarReservasEntrada(pedido, ClaveNueva()));

        Assert.True(confirmada.EsExito, confirmada.Error?.Clave);
        Assert.Equal(EstadoDeReserva.Confirmada, confirmada.Valor.Estado);
        Assert.Empty(confirmada.Valor.Traslados);
        Assert.DoesNotContain(entorno.Base.Bitacora.Llamadas, l => l.StartsWith("traslados.", StringComparison.Ordinal));
        Assert.All(entorno.Reservas.Guardadas.Single().Lineas, l => Assert.Null(l.TrasladoId));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-45")]
    public async Task LaConsultaPrevia_RespondePorLineaConLaMismaAsignacion_SinTrasladoNiPlazoYSinReservar()
    {
        var (entorno, libro) = Preparar(enCusco: 30, enAyacucho: 5);
        var primera = Linea(libro, 3, sucursal: "ayacucho");
        var segunda = Linea(libro, 3, sucursal: "ayacucho");
        var deCusco = Linea(libro, 18, sucursal: "cusco");

        var resultado = await entorno.ConsultarSeleccion().Ejecutar(new ConsultarSeleccionEntrada(TipoDeEntregaDeReserva.Recojo, null, null, [primera, segunda, deCusco]));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(
            [
                new SeleccionDeLinea(primera.LineaId, libro, true, "ayacucho", false, null, 0),
                new SeleccionDeLinea(segunda.LineaId, libro, false, null, false, null, 0, 5),
                new SeleccionDeLinea(deCusco.LineaId, libro, true, "cusco", false, null, 0),
            ],
            resultado.Valor.Lineas);
        Assert.Empty(entorno.Base.Existencias.Movimientos);
        Assert.Empty(entorno.Reservas.Guardadas);
        Assert.Equal((5, 0), entorno.FilaDe(entorno.Base.Ayacucho, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-45")]
    public async Task LaConsultaPrevia_IgnoraLaLineaDigitalYSuSucursal()
    {
        var (entorno, libro) = Preparar(enCusco: 1, enAyacucho: 0);

        var resultado = await entorno.ConsultarSeleccion().Ejecutar(
            new ConsultarSeleccionEntrada(TipoDeEntregaDeReserva.Domicilio, null, "lima", [Linea(entorno.Digital(), sucursal: "tacna-9"), Linea(libro, 2, sucursal: "cusco")]));

        var linea = Assert.Single(resultado.Valor.Lineas);
        Assert.Equal((false, (int?)1), (linea.HayStock, linea.DisponibleEnLaSucursal));
    }
}
