using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Domain.Fuentes;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.UnitTests.Fuentes;
using BabelHub.Inventario.UnitTests.Soporte;
using static BabelHub.Inventario.UnitTests.Reservas.EntornoDeReservas;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>T07: <c>ReservarUnidades</c>, el núcleo (SEL-01, SEL-05, SEL-12): el cliente sale del actor, todo o nada, selección sobre filas bloqueadas y la jerarquía de bloqueos.</summary>
public sealed class ReservarUnidadesTests
{
    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task Reservar_UnaLineaImpresa_CreaLaReservaPendienteConSuExpiracionYBajaElVendible()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 8, 0);
        var lineaId = Guid.NewGuid();

        var resultado = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro, 2, lineaId)]));

        Assert.True(resultado.EsExito);
        var reserva = resultado.Valor;
        Assert.Equal(EstadoDeReserva.Pendiente, reserva.Estado);
        Assert.Equal(EntornoDeFuentes_Ahora, reserva.CreadaEn);
        Assert.Equal(EntornoDeFuentes_Ahora.AddMinutes(15), reserva.ExpiraEn);
        Assert.False(reserva.EsRepeticion);
        Assert.Empty(reserva.Traslados);
        var linea = Assert.Single(reserva.Lineas);
        Assert.Equal((lineaId, libro, 2, EstadoDeReserva.Pendiente, "lima", false, null), (linea.LineaId, linea.EdicionId, linea.Cantidad, linea.Estado, linea.CodigoDeFuente, linea.RequiereTraslado, linea.PlazoEstimadoEnDias));
        Assert.Equal((8, 2), entorno.FilaDe(entorno.Base.Lima, libro));
        Assert.Single(entorno.Reservas.Guardadas);
    }

    private static DateTimeOffset EntornoDeFuentes_Ahora => EntornoDeFuentes.Ahora;

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task Reservar_ElClienteDeLaReservaEsElDelActorVerificado()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 3, 0);

        var resultado = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));

        Assert.True(resultado.EsExito);
        Assert.Equal(entorno.ClienteId, Assert.Single(entorno.Reservas.Guardadas).ClienteId);
        Assert.Equal([entorno.ClienteId], entorno.Bloqueos.Clientes);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-05")]
    public async Task Reservar_SiLaSegundaLineaNoTieneStock_RechazaConElDetalleYNoAplicaNingunMovimiento()
    {
        var entorno = new EntornoDeReservas();
        var hay = entorno.Impresa();
        var agotado = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, hay, 5, 0);
        entorno.Base.Existencias.Poner(entorno.Base.Cusco.Id, agotado, 1, 0);
        var lineaSinStock = Guid.NewGuid();

        var resultado = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(hay, 1), Linea(agotado, 4, lineaSinStock)]));

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
        Assert.Equal("stock-insuficiente", resultado.Error.Clave);
        var detalle = Assert.Single(resultado.Error.Errores);
        Assert.True(DetalleDeFaltaDeStock.TryLeer(detalle, out var linea));
        Assert.Equal(new LineaSinStock(lineaSinStock, agotado, 4, 1), linea);
        Assert.Empty(entorno.Base.Existencias.Movimientos);
        Assert.Empty(entorno.Reservas.Guardadas);
        Assert.Equal((5, 0), entorno.FilaDe(entorno.Base.Lima, hay));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-05")]
    public async Task Reservar_ConVariasLineasSinStock_LasNombraTodas()
    {
        var entorno = new EntornoDeReservas();
        var a = entorno.Impresa();
        var b = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, a, 1, 0);
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, b, 1, 0);

        var resultado = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(a, 2), Linea(b, 3)]));

        Assert.Equal(2, resultado.Error!.Errores.Count);
        Assert.All(resultado.Error.Errores, e => Assert.True(DetalleDeFaltaDeStock.TryLeer(e, out _)));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-12")]
    public async Task Reservar_ConUnaEdicionDigital_LaRechazaSinTocarElStock()
    {
        var entorno = new EntornoDeReservas();
        var impresa = entorno.Impresa();
        var digital = entorno.Digital();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, impresa, 5, 0);

        var resultado = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(impresa), Linea(digital)]));

        Assert.Equal((CategoriaDeError.ReglaDeNegocio, "edicion-no-impresa"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.DoesNotContain("existencias.GarantizarFilas", entorno.Base.Bitacora.Llamadas);
        Assert.Empty(entorno.Base.Existencias.Movimientos);
        Assert.Empty(entorno.Reservas.Guardadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-12")]
    public async Task Reservar_ConUnaEdicionDesconocida_EsNoEncontrada()
    {
        var entorno = new EntornoDeReservas();

        var resultado = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(Guid.NewGuid())]));

        Assert.Equal((CategoriaDeError.NoEncontrado, "edicion-no-encontrada"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Empty(entorno.Reservas.Guardadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task Reservar_TomaLosBloqueosEnElOrdenDeLaJerarquiaYAplicaAlFinal()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 0);

        await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));

        var orden = entorno.Base.Bitacora.Llamadas;
        string[] esperado =
        [
            "bloqueo.Cliente",
            "bloqueo.Pedido",
            "existencias.GarantizarFilas",
            "reservas.PedidosConVencidas",
            "existencias.BloquearFilas",
            "existencias.Aplicar(0,1)",
            "reservas.Insertar",
        ];
        Assert.Equal(esperado, orden.Where(esperado.Contains).ToArray());
        Assert.True(orden.IndexOf("bloqueo.Pedido") < orden.IndexOf("existencias.BloquearFilas"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task Reservar_AplicaLosMovimientosAgregadosYOrdenadosPorEdicionYFuente()
    {
        var entorno = new EntornoDeReservas();
        var a = entorno.Impresa();
        var b = entorno.Impresa();
        var c = entorno.Impresa();
        foreach (var edicion in new[] { a, b, c })
        {
            entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, edicion, 9, 0);
        }

        var resultado = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(c, 2), Linea(a, 3), Linea(b, 4)]));

        Assert.True(resultado.EsExito);
        var movimientos = entorno.Base.Existencias.Movimientos;
        Assert.Equal(3, movimientos.Count);
        Assert.All(movimientos, m => Assert.Equal(0, m.DeltaFisico));
        Assert.Equal(movimientos.OrderBy(m => m.EdicionId, OrdenDeBloqueo.DeIdentificadores).Select(m => m.EdicionId), movimientos.Select(m => m.EdicionId));
        Assert.Equal(9, movimientos.Sum(m => m.DeltaReservado));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-26")]
    public async Task Reservar_SiLaSucursalNoTieneStock_ReasignaALaFuenteConStockYMarcaElTraslado()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 0, 0);
        entorno.Base.Existencias.Poner(entorno.Base.Cusco.Id, libro, 5, 0);
        entorno.Base.Rutas.Rutas.Add(RutaEntreFuentes.Definir(entorno.Base.Cusco.Id, entorno.Base.Lima.Id, 3));

        var resultado = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro, 2)]));

        var linea = Assert.Single(resultado.Valor.Lineas);
        Assert.Equal(("cusco", true, 3), (linea.CodigoDeFuente, linea.RequiereTraslado, linea.PlazoEstimadoEnDias));
        Assert.Equal((5, 2), entorno.FilaDe(entorno.Base.Cusco, libro));
        Assert.Equal(entorno.Base.Lima.Id, Assert.Single(entorno.Reservas.Guardadas).SucursalDeRecojoId);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-34")]
    public async Task Reservar_UnaLineaNuncaSeDivideEntreFuentes()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 3, 0);
        entorno.Base.Existencias.Poner(entorno.Base.Cusco.Id, libro, 3, 0);

        var resultado = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro, 5)]));

        Assert.Equal("stock-insuficiente", resultado.Error!.Clave);
        Assert.True(DetalleDeFaltaDeStock.TryLeer(Assert.Single(resultado.Error.Errores), out var linea));
        Assert.Equal(6, linea!.DisponibleEnLaRed);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-24")]
    public async Task Reservar_ADomicilio_NuncaHayTrasladoYSeUsaLaFuenteDeLaRegion()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 0);
        entorno.Base.Existencias.Poner(entorno.Base.Cusco.Id, libro, 5, 0);

        var resultado = await entorno.Reservar().Ejecutar(ADomicilio("cusco", [Linea(libro, 1)]));

        var linea = Assert.Single(resultado.Valor.Lineas);
        Assert.Equal(("cusco", false, null), (linea.CodigoDeFuente, linea.RequiereTraslado, linea.PlazoEstimadoEnDias));
        var guardada = Assert.Single(entorno.Reservas.Guardadas);
        Assert.Equal(("cusco", null), (guardada.RegionDeEntrega, guardada.SucursalDeRecojoId));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-33")]
    public async Task Reservar_ADomicilioEnUnaRegionSinVendible_ElijeLaFuenteDeMenorPlazoDeRutaYNoGeneraTraslado()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Cusco.Id, libro, 0, 0);
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 0);
        entorno.Base.Existencias.Poner(entorno.Base.Ayacucho.Id, libro, 5, 0);
        entorno.Base.Rutas.Rutas.Add(RutaEntreFuentes.Definir(entorno.Base.Ayacucho.Id, entorno.Base.Cusco.Id, 1));
        entorno.Base.Rutas.Rutas.Add(RutaEntreFuentes.Definir(entorno.Base.Lima.Id, entorno.Base.Cusco.Id, 3));

        var resultado = await entorno.Reservar().Ejecutar(ADomicilio("cusco", [Linea(libro, 1)]));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        var linea = Assert.Single(resultado.Valor.Lineas);
        Assert.Equal(("ayacucho", false), (linea.CodigoDeFuente, linea.RequiereTraslado));
        Assert.Empty(resultado.Valor.Traslados);
        Assert.Equal((5, 1), entorno.FilaDe(entorno.Base.Ayacucho, libro));
        Assert.Equal((5, 0), entorno.FilaDe(entorno.Base.Lima, libro));
        Assert.Equal(0, entorno.Traslados.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-25")]
    public async Task Reservar_ADomicilioEnUnaRegionSinSucursal_ElijeLaDeMayorVendibleYAIgualdadLaDeCodigoMenor()
    {
        // Un cliente solo puede tener una reserva pendiente (SEL-13): cada caso usa su propio entorno.
        static async Task<string> FuenteElegida(int enLima, int enCusco)
        {
            var entorno = new EntornoDeReservas();
            var libro = entorno.Impresa();
            entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, enLima, 0);
            entorno.Base.Existencias.Poner(entorno.Base.Cusco.Id, libro, enCusco, 0);

            var resultado = await entorno.Reservar().Ejecutar(ADomicilio("puno", [Linea(libro, 1)]));

            Assert.True(resultado.EsExito, resultado.Error?.Clave);
            return Assert.Single(resultado.Valor.Lineas).CodigoDeFuente;
        }

        Assert.Equal("cusco", await FuenteElegida(enLima: 5, enCusco: 5));
        Assert.Equal("cusco", await FuenteElegida(enLima: 5, enCusco: 8));
        Assert.Equal("lima", await FuenteElegida(enLima: 8, enCusco: 5));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-34")]
    public async Task Reservar_ARecojoConDosFuentesConStock_PrefiereLaDeRutaDefinidaSobreLaQueNoLaTiene()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Cusco.Id, libro, 5, 0);
        entorno.Base.Existencias.Poner(entorno.Base.Ayacucho.Id, libro, 5, 0);
        entorno.Base.Rutas.Rutas.Add(RutaEntreFuentes.Definir(entorno.Base.Ayacucho.Id, entorno.Base.Lima.Id, 4));

        var resultado = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro, 1)]));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        var linea = Assert.Single(resultado.Valor.Lineas);
        Assert.Equal(("ayacucho", true, 4), (linea.CodigoDeFuente, linea.RequiereTraslado, linea.PlazoEstimadoEnDias));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-10")]
    public async Task Reservar_UsaLaEstrategiaConfiguradaSinCambiarElContrato()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 0);
        entorno.Base.Existencias.Poner(entorno.Base.Ayacucho.Id, libro, 5, 0);

        var resultado = await entorno.Reservar(new SiempreLaDeCodigoMayor()).Ejecutar(ARecojo("lima", [Linea(libro, 1)]));

        Assert.Equal("lima", Assert.Single(resultado.Valor.Lineas).CodigoDeFuente);
    }

    private sealed class SiempreLaDeCodigoMayor : IEstrategiaDeSeleccion
    {
        public string Nombre => "de-prueba";

        public Seleccion Seleccionar(SolicitudDeSeleccion solicitud)
        {
            var elegida = solicitud.Candidatas.Where(c => c.Vendible >= solicitud.Cantidad).OrderByDescending(c => c.Codigo, StringComparer.Ordinal).First();
            return new Seleccion(elegida.FuenteId, elegida.Codigo, false, null, 0);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task Reservar_ConUnActorQueNoEsCliente_LoRechazaConSuEventoYSinBloquearNada()
    {
        var entorno = new EntornoDeReservas(ActorDePrueba.DeAdministradorCentral());
        var libro = entorno.Impresa();

        var resultado = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));

        Assert.Equal((CategoriaDeError.Prohibido, "prohibido"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(["fuera-de-rol"], entorno.Base.MotivosDeDenegacion);
        Assert.Empty(entorno.Bloqueos.Clientes);
        Assert.Empty(entorno.Base.Existencias.Movimientos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task Reservar_SinActorOConUnVisitante_EsNoAutenticadoYNoSeConcedePorOmision()
    {
        foreach (var actor in new[] { ActorDePrueba.SinContexto(), ActorDePrueba.Anonimo() })
        {
            var entorno = new EntornoDeReservas(actor);
            var libro = entorno.Impresa();

            var resultado = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));

            Assert.Equal((CategoriaDeError.NoAutenticado, "no-autenticado"), (resultado.Error!.Categoria, resultado.Error.Clave));
            Assert.Equal(["sin-sesion"], entorno.Base.MotivosDeDenegacion);
            Assert.Empty(entorno.Bloqueos.Pedidos);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-16")]
    public async Task Reservar_UnPedidoDeOtroCliente_EsNoEncontradoConElEventoYSinDarPistas()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 0);
        var pedido = Guid.NewGuid();
        var ajena = ReservaDePedido.Iniciar(
            Guid.NewGuid(), pedido, Guid.NewGuid(), TipoDeEntrega.Recojo, entorno.Base.Lima.Id, null, EntornoDeFuentes_Ahora.AddMinutes(-1),
            [LineaDeReserva.Pendiente(Guid.NewGuid(), Guid.NewGuid(), libro, entorno.Base.Lima.Id, 1, EntornoDeFuentes_Ahora)]);
        entorno.Reservas.Guardadas.Add(ajena);

        var resultado = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)], pedido));

        Assert.Equal((CategoriaDeError.NoEncontrado, "reserva-no-encontrada"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(["recurso-ajeno"], entorno.Base.MotivosDeDenegacion);
        Assert.Empty(entorno.Base.Existencias.Movimientos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task Reservar_ConLaSucursalDeRecojoInexistente_EsUnaValidacionDelCampo()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();

        var resultado = await entorno.Reservar().Ejecutar(ARecojo("no-existe", [Linea(libro)]));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal([new ErrorDeCampo("codigoDeSucursalDeRecojo", "inexistente")], resultado.Error.Errores);
        Assert.Empty(entorno.Base.Existencias.Movimientos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task Reservar_SiNoSeObtieneUnBloqueo_EsBloqueoOcupadoYNoCambiaNada()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 0);
        entorno.Bloqueos.EsperaAgotada = true;

        var resultado = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));

        Assert.Equal((CategoriaDeError.NoDisponible, "bloqueo-ocupado"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Empty(entorno.Base.Existencias.Movimientos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task Reservar_SiLaPoliticaDeFilasRechazaLaEscritura_EsProhibidoConSuEvento()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 0);
        entorno.Base.Existencias.FuentesFueraDelAlcance.Add(entorno.Base.Lima.Id);

        var resultado = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));

        Assert.Equal((CategoriaDeError.Prohibido, "prohibido"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(["fuera-de-rol"], entorno.Base.MotivosDeDenegacion);
        Assert.Empty(entorno.Reservas.Guardadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task Reservar_GuardaLaClaveYLaHuellaDeLaCargaDeLaSolicitud()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 0);
        var entrada = ARecojo("lima", [Linea(libro, 2)]);

        await entorno.Reservar().Ejecutar(entrada);

        var solicitud = Assert.Single(entorno.Reservas.Solicitudes);
        Assert.Equal(entrada.ClaveDeIdempotencia, solicitud.ClaveDeIdempotencia);
        Assert.Equal(CargaCanonicaDeReservar.Huella(entrada), solicitud.HuellaDeLaCarga);
        Assert.Matches("^[0-9a-f]{64}$", solicitud.HuellaDeLaCarga);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-31")]
    public void Huella_NoDependeDelOrdenDeLasLineasPeroSiDeLaCarga()
    {
        var pedido = Guid.NewGuid();
        var a = Linea(Guid.NewGuid(), 1);
        var b = Linea(Guid.NewGuid(), 2);

        var base1 = CargaCanonicaDeReservar.Huella(ARecojo("lima", [a, b], pedido, "k1"));
        var base2 = CargaCanonicaDeReservar.Huella(ARecojo("lima", [b, a], pedido, "otra-clave"));
        var distinta = CargaCanonicaDeReservar.Huella(ARecojo("lima", [a, b with { Cantidad = 3 }], pedido));
        var otraSucursal = CargaCanonicaDeReservar.Huella(ARecojo("cusco", [a, b], pedido));

        Assert.Equal(base1, base2);
        Assert.NotEqual(base1, distinta);
        Assert.NotEqual(base1, otraSucursal);
    }

    public static TheoryData<string, ReservarEntrada, string> EntradasInvalidas()
    {
        var libro = Guid.NewGuid();
        var otro = Guid.NewGuid();
        var datos = new TheoryData<string, ReservarEntrada, string>
        {
            { "sin líneas", ARecojo("lima", []), "lineas" },
            { "cantidad cero", ARecojo("lima", [Linea(libro, 0)]), "lineas.cantidad" },
            { "cantidad sobre el tope", ARecojo("lima", [Linea(libro, 21)]), "lineas.cantidad" },
            { "edición repetida", ARecojo("lima", [Linea(libro), Linea(libro)]), "lineas" },
            { "línea repetida", ARecojo("lima", [Linea(libro, 1, otro), Linea(Guid.NewGuid(), 1, otro)]), "lineas" },
            { "edición vacía", ARecojo("lima", [new LineaSolicitada(Guid.NewGuid(), Guid.Empty, 1)]), "lineas" },
            { "demasiadas líneas", ARecojo("lima", [.. Enumerable.Range(0, 31).Select(_ => Linea(Guid.NewGuid()))]), "lineas" },
            { "pedido vacío", ARecojo("lima", [Linea(libro)], Guid.Empty), "pedidoId" },
            { "recojo sin sucursal", new ReservarEntrada(Guid.NewGuid(), TipoDeEntregaDeReserva.Recojo, null, null, [Linea(libro)], ClaveNueva()), "codigoDeSucursalDeRecojo" },
            { "recojo con región", new ReservarEntrada(Guid.NewGuid(), TipoDeEntregaDeReserva.Recojo, "lima", "cusco", [Linea(libro)], ClaveNueva()), "regionDeEntrega" },
            { "domicilio sin región", new ReservarEntrada(Guid.NewGuid(), TipoDeEntregaDeReserva.Domicilio, null, null, [Linea(libro)], ClaveNueva()), "regionDeEntrega" },
            { "domicilio con región desconocida", ADomicilio("narnia", [Linea(libro)]), "regionDeEntrega" },
            { "domicilio con sucursal", new ReservarEntrada(Guid.NewGuid(), TipoDeEntregaDeReserva.Domicilio, "lima", "cusco", [Linea(libro)], ClaveNueva()), "codigoDeSucursalDeRecojo" },
            { "clave corta", ARecojo("lima", [Linea(libro)], null, "abc"), "idempotencyKey" },
            { "clave con símbolos", ARecojo("lima", [Linea(libro)], null, "una clave con espacios y más"), "idempotencyKey" },
            { "tipo de entrega inventado", new ReservarEntrada(Guid.NewGuid(), (TipoDeEntregaDeReserva)9, null, null, [Linea(libro)], ClaveNueva()), "tipoDeEntrega" },
        };
        return datos;
    }

    [Theory]
    [MemberData(nameof(EntradasInvalidas))]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task Reservar_ConUnaEntradaInvalida_EsValidacionConElCampoYNoTocaNada(string caso, ReservarEntrada entrada, string campo)
    {
        var entorno = new EntornoDeReservas();

        var resultado = await entorno.Reservar().Ejecutar(entrada);

        Assert.True(resultado.Error is { Categoria: CategoriaDeError.Validacion }, caso);
        Assert.Contains(resultado.Error!.Errores, e => e.Campo == campo);
        Assert.Empty(entorno.Bloqueos.Clientes);
        Assert.Empty(entorno.Base.Existencias.Movimientos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task Reservar_ElFalloDeValidacionNoRepiteElValorRecibido()
    {
        var entorno = new EntornoDeReservas();
        var entrada = ARecojo("lima", [Linea(Guid.NewGuid())], null, "clave con espacios que no vale");

        var resultado = await entorno.Reservar().Ejecutar(entrada);

        Assert.All(resultado.Error!.Errores, e => Assert.DoesNotContain("espacios", e.Clave + e.Campo, StringComparison.Ordinal));
    }
}

/// <summary>Lo que el contrato público no debe imprimir ni dejar pasar (SEG-25, AGENTS §5) y la última defensa de unicidad.</summary>
public sealed class ReservarUnidadesDefensasTests
{
    [Fact]
    [Trait("Criterio", "AC-SEL-09")]
    public async Task Reservar_SiLaBaseRechazaPorUnicidadUnaReservaConcurrente_EsConflictoYNuncaUnaExcepcion()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 0);
        entorno.Reservas.InsertarChocaConUnaConcurrente = true;

        var resultado = await entorno.Reservar().Ejecutar(EntornoDeReservas.ARecojo("lima", [EntornoDeReservas.Linea(libro)]));

        Assert.Equal((CategoriaDeError.Conflicto, "reserva-existente"), (resultado.Error!.Categoria, resultado.Error.Clave));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-09")]
    public void LosModelosConClaveOHuellaNoImprimenSusDatos()
    {
        var entrada = EntornoDeReservas.ARecojo("lima", [EntornoDeReservas.Linea(Guid.NewGuid())], clave: "clave-secreta-de-prueba-0123");
        var datos = new DatosDeLaSolicitud("clave-secreta-de-prueba-0123", new string('a', 64), "huella-de-origen-0123456789");

        foreach (var texto in new[] { entrada.ToString(), datos.ToString(), new ConfirmarReservasEntrada(Guid.NewGuid(), "clave-secreta-de-prueba-0123").ToString(), new CerrarReservasEntrada(Guid.NewGuid(), MotivoDeCierreDeReserva.Entrega, [], null, "clave-secreta-de-prueba-0123").ToString() })
        {
            Assert.DoesNotContain("secreta", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("huella-de-origen", texto, StringComparison.Ordinal);
        }
    }
}

/// <summary>T11: la idempotencia de reservar (SEL-09, SEL-31): la clave es única por cliente y lleva la huella de su carga canónica.</summary>
public sealed class ReservarUnidadesIdempotenciaTests
{
    private static async Task<(EntornoDeReservas Entorno, Guid Libro, ReservarEntrada Entrada)> UnaReservaHecha(int cantidad = 2)
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 10, 0);
        var entrada = EntornoDeReservas.ARecojo("lima", [EntornoDeReservas.Linea(libro, cantidad)]);
        Assert.True((await entorno.Reservar().Ejecutar(entrada)).EsExito);
        return (entorno, libro, entrada);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-09")]
    public async Task LaMismaClaveConLaMismaCarga_DevuelveLaReservaOriginalSinBajarElVendibleOtraVez()
    {
        var (entorno, libro, entrada) = await UnaReservaHecha();
        var original = entorno.Reservas.Guardadas.Single();

        var repetida = await entorno.Reservar().Ejecutar(entrada);

        Assert.True(repetida.EsExito);
        Assert.True(repetida.Valor.EsRepeticion);
        Assert.Equal(original.FechaDeExpiracion, repetida.Valor.ExpiraEn);
        Assert.Equal(original.Lineas.Single().LineaId, repetida.Valor.Lineas.Single().LineaId);
        Assert.Equal((10, 2), entorno.FilaDe(entorno.Base.Lima, libro));
        Assert.Single(entorno.Reservas.Guardadas);
        Assert.Single(entorno.Base.Existencias.Movimientos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-09")]
    public async Task LaRepeticionNoDependeDelOrdenDeLasLineas()
    {
        var entorno = new EntornoDeReservas();
        var a = entorno.Impresa();
        var b = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, a, 5, 0);
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, b, 5, 0);
        var lineas = new[] { EntornoDeReservas.Linea(a, 1), EntornoDeReservas.Linea(b, 2) };
        var entrada = EntornoDeReservas.ARecojo("lima", lineas);
        Assert.True((await entorno.Reservar().Ejecutar(entrada)).EsExito);

        var repetida = await entorno.Reservar().Ejecutar(entrada with { Lineas = [lineas[1], lineas[0]] });

        Assert.True(repetida.EsExito);
        Assert.True(repetida.Valor.EsRepeticion);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-31")]
    public async Task LaMismaClaveConOtrasCantidades_EsConflictoYNoCambiaNada()
    {
        var (entorno, libro, entrada) = await UnaReservaHecha();

        var otra = await entorno.Reservar().Ejecutar(entrada with { Lineas = [entrada.Lineas[0] with { Cantidad = 5 }] });

        Assert.Equal((CategoriaDeError.Conflicto, "idempotencia-en-conflicto"), (otra.Error!.Categoria, otra.Error.Clave));
        Assert.Equal((10, 2), entorno.FilaDe(entorno.Base.Lima, libro));
        Assert.Single(entorno.Reservas.Guardadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-31")]
    public async Task LaMismaClaveConOtroPedido_TambienEsConflicto()
    {
        var (entorno, _, entrada) = await UnaReservaHecha();

        var otra = await entorno.Reservar().Ejecutar(entrada with { PedidoId = Guid.NewGuid() });

        Assert.Equal("idempotencia-en-conflicto", otra.Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-31")]
    public async Task OtroClientePuedeUsarLaMismaClaveSinConflicto()
    {
        var (primero, libro, entrada) = await UnaReservaHecha(1);
        var segundo = new EntornoDeReservas();
        var libro2 = segundo.Impresa();
        segundo.Base.Existencias.Poner(segundo.Base.Lima.Id, libro2, 5, 0);

        // Otro cliente (otro actor) con la misma clave en un entorno que ya conoce la del primero.
        foreach (var (reserva, solicitud) in primero.Reservas.Guardadas.Zip(primero.Reservas.Solicitudes))
        {
            segundo.Reservas.Sembrar(reserva, solicitud);
        }
        var resultado = await segundo.Reservar().Ejecutar(EntornoDeReservas.ARecojo("lima", [EntornoDeReservas.Linea(libro2, 1)], clave: entrada.ClaveDeIdempotencia));

        Assert.True(resultado.EsExito);
        Assert.False(resultado.Valor.EsRepeticion);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-09")]
    public async Task LaRepeticionTomaPrimeroElBloqueoDelCliente_YNoTocaElStock()
    {
        var (entorno, _, entrada) = await UnaReservaHecha();
        entorno.Base.Bitacora.Llamadas.Clear();

        await entorno.Reservar().Ejecutar(entrada);

        var orden = entorno.Base.Bitacora.Llamadas;
        Assert.Equal("bloqueo.Cliente", orden[0]);
        Assert.DoesNotContain("bloqueo.Pedido", orden);
        Assert.DoesNotContain("existencias.BloquearFilas", orden);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task UnTitularDadoDeBajaNoReserva()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 0);
        entorno.Reservas.Bajas.Add(entorno.ClienteId);

        var resultado = await entorno.Reservar().Ejecutar(EntornoDeReservas.ARecojo("lima", [EntornoDeReservas.Linea(libro)]));

        Assert.Equal((CategoriaDeError.Prohibido, "titular-dado-de-baja"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(["fuera-de-rol"], entorno.Base.MotivosDeDenegacion);
        Assert.Empty(entorno.Reservas.Guardadas);
    }
}
