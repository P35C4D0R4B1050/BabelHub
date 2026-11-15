using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.UnitTests.Fuentes;
using BabelHub.Inventario.UnitTests.Soporte;
using static BabelHub.Inventario.UnitTests.Reservas.EntornoDeReservas;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>T13: repetir el pedido con una reserva pendiente la reemplaza (SEL-16) sin consumir límites, con el tope de 3 (SEL-36, SEL-37) y la anterior intacta si la nueva falla.</summary>
public sealed class ReservarUnidadesReemplazoTests
{
    private static (EntornoDeReservas Entorno, Guid Libro, Guid Pedido) Escenario(int stock = 10)
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, stock, 0);
        return (entorno, libro, Guid.NewGuid());
    }

    private static async Task<ReservaDelPedido> Reemplazar(EntornoDeReservas entorno, Guid pedido, Guid libro, int cantidad)
    {
        var resultado = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro, cantidad)], pedido));
        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        return resultado.Valor;
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-16")]
    public async Task RepetirElPedidoConUnaReservaPendiente_LaReemplaza_LiberaLaAnteriorYReservaLaNueva()
    {
        var (entorno, libro, pedido) = Escenario();
        await Reemplazar(entorno, pedido, libro, 4);
        Assert.Equal((10, 4), entorno.FilaDe(entorno.Base.Lima, libro));
        entorno.Base.Reloj.Ahora = entorno.Base.Reloj.Ahora.AddMinutes(2);

        var nueva = await Reemplazar(entorno, pedido, libro, 2);

        Assert.Equal((10, 2), entorno.FilaDe(entorno.Base.Lima, libro));
        Assert.Equal(EntornoDeFuentes.Ahora.AddMinutes(17), nueva.ExpiraEn);
        var estados = entorno.Reservas.Guardadas.OrderBy(r => r.Reemplazos).Select(r => r.Lineas.Single().Estado).ToList();
        Assert.Equal([EstadoDeLinea.Liberada, EstadoDeLinea.Pendiente], estados);
        Assert.Equal(1, entorno.Reservas.Guardadas.Max(r => r.Reemplazos));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-16")]
    public async Task ElReemplazoVeComoDisponibleLoQueApartabaLaAnterior_AunSiLaRedNoTieneMas()
    {
        var (entorno, libro, pedido) = Escenario(stock: 3);
        await Reemplazar(entorno, pedido, libro, 3);

        // Sin liberar la anterior no habría stock para 3 más: el reemplazo cuenta lo que apartaba la reserva que sustituye.
        var nueva = await Reemplazar(entorno, pedido, libro, 3);

        Assert.Single(nueva.Lineas);
        Assert.Equal((3, 3), entorno.FilaDe(entorno.Base.Lima, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-16")]
    public async Task LaLiberacionPorReemplazoSeAudita_ConLaCausaReemplazoEnLaMismaTransaccion()
    {
        var (entorno, libro, pedido) = Escenario();
        await Reemplazar(entorno, pedido, libro, 4);

        await Reemplazar(entorno, pedido, libro, 2);

        var (solicitud, _) = Assert.Single(entorno.Auditoria.Registros);
        Assert.Equal(AccionesAuditables.ReservaLiberada, solicitud.Accion);
        Assert.Equal(("pedido", pedido.ToString("D"), "reemplazo", "4"), (solicitud.ObjetoTipo, solicitud.ObjetoId, solicitud.Detalle["causa"], solicitud.Detalle["cantidad"]));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-16")]
    public async Task UnReemplazoNoCuentaNiConsumeLosDemasLimites()
    {
        var (entorno, libro, pedido) = Escenario();
        await Reemplazar(entorno, pedido, libro, 1);

        // Dos reemplazos no consumen los 5 por hora: solo cuenta la primera reserva del pedido.
        await Reemplazar(entorno, pedido, libro, 1);
        await Reemplazar(entorno, pedido, libro, 1);

        Assert.Empty(entorno.Eventos.Eventos);
        Assert.Equal(2, entorno.Reservas.Guardadas.Max(r => r.Reemplazos));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-37")]
    public async Task ElCuartoReemplazo_SeRechazaConElMotivoDelTopeYElInstanteDeExpiracionDeLaVigente()
    {
        var (entorno, libro, pedido) = Escenario();
        await Reemplazar(entorno, pedido, libro, 1);
        for (var i = 0; i < 3; i++)
        {
            await Reemplazar(entorno, pedido, libro, 1);
        }

        var vigente = entorno.Reservas.Guardadas.OrderByDescending(r => r.Reemplazos).First();
        var cuarto = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro, 1)], pedido));

        Assert.Equal((CategoriaDeError.Limite, "limite-excedido"), (cuarto.Error!.Categoria, cuarto.Error.Clave));
        Assert.True(DetalleDeLimiteDeReservas.TryLeer(cuarto.Error.Errores, out var rechazo));
        Assert.Equal((MotivosDeLimiteDeReservas.ReemplazosPorPedido, vigente.FechaDeExpiracion), (rechazo!.Motivo, rechazo.DisponibleDesde));
        Assert.Equal(3, vigente.Reemplazos);
        Assert.Equal(EstadoDeLinea.Pendiente, vigente.Lineas.Single().Estado);
        Assert.Equal(1, entorno.Eventos.Eventos.Count(e => e.Limite == MotivosDeLimiteDeReservas.ReemplazosPorPedido));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-37")]
    public async Task UnReemplazoRechazadoPorFaltaDeStock_NoIncrementaLaCuenta_YConservaLaAnterior()
    {
        var (entorno, libro, pedido) = Escenario(stock: 3);
        await Reemplazar(entorno, pedido, libro, 2);

        var fallido = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro, 50)], pedido));

        Assert.Equal("stock-insuficiente", fallido.Error!.Clave);

        // En producción la transacción revierte la liberación: aquí el doble no es transaccional, pero la cuenta no avanzó y no se insertó otra cabecera.
        Assert.Single(entorno.Reservas.Guardadas);
        Assert.Equal(0, entorno.Reservas.Guardadas.Single().Reemplazos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-24")]
    public async Task ElReemplazoHeredaLaMarcaDeYaExtendida()
    {
        var (entorno, libro, pedido) = Escenario();
        await Reemplazar(entorno, pedido, libro, 1);
        Assert.True(entorno.Reservas.Guardadas.Single().Extender(entorno.Base.Reloj.Ahora));

        var nueva = await Reemplazar(entorno, pedido, libro, 1);

        Assert.Equal(EntornoDeFuentes.Ahora.AddMinutes(15), nueva.ExpiraEn);
        Assert.True(entorno.Reservas.Guardadas.OrderByDescending(r => r.Reemplazos).First().Extendida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-16")]
    public async Task UnPedidoDeOtroCliente_NoSeReemplaza_EsNoEncontradoConElEvento()
    {
        var (entorno, libro, pedido) = Escenario();
        await Reemplazar(entorno, pedido, libro, 1);
        entorno.Base.Actor.Establecer(ActorDePrueba.DeCliente().Actual);

        var ajeno = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro, 1)], pedido));

        Assert.Equal((CategoriaDeError.NoEncontrado, "reserva-no-encontrada"), (ajeno.Error!.Categoria, ajeno.Error.Clave));
        Assert.Equal(["recurso-ajeno"], entorno.Base.MotivosDeDenegacion);
        Assert.Equal(EstadoDeLinea.Pendiente, entorno.Reservas.Guardadas.Single().Lineas.Single().Estado);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-38")]
    [InlineData("confirmada")]
    [InlineData("expirada")]
    [InlineData("liberada")]
    [InlineData("vencida")]
    public async Task UnPedidoCuyaReservaYaNoEstaPendiente_RespondeEstadoInvalido_YElClienteReiniciaConUnoNuevo(string estado)
    {
        var (entorno, libro, pedido) = Escenario();
        await Reemplazar(entorno, pedido, libro, 1);
        var reserva = entorno.Reservas.Guardadas.Single();
        switch (estado)
        {
            case "confirmada":
                Assert.True(reserva.Confirmar(entorno.Base.Reloj.Ahora));
                break;
            case "expirada":
                reserva.Lineas.Single().Expirar(entorno.Base.Reloj.Ahora);
                break;
            case "liberada":
                reserva.Lineas.Single().Liberar(entorno.Base.Reloj.Ahora);
                break;
            default:
                entorno.Base.Reloj.Ahora = entorno.Base.Reloj.Ahora.AddMinutes(20);
                break;
        }

        var otra = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro, 1)], pedido));

        Assert.Equal((CategoriaDeError.Conflicto, "reserva-estado-invalido"), (otra.Error!.Categoria, otra.Error.Clave));
        Assert.Single(entorno.Reservas.Guardadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-16")]
    public async Task ElReemplazoPuedeCambiarLasEdicionesYLasCantidades_YBloqueaLasFilasDeTodas()
    {
        var entorno = new EntornoDeReservas();
        var a = entorno.Impresa();
        var b = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, a, 5, 0);
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, b, 5, 0);
        var pedido = Guid.NewGuid();
        Assert.True((await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(a, 2)], pedido))).EsExito);

        var nueva = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(b, 3)], pedido));

        Assert.True(nueva.EsExito, nueva.Error?.Clave);
        Assert.Equal((5, 0), entorno.FilaDe(entorno.Base.Lima, a));
        Assert.Equal((5, 3), entorno.FilaDe(entorno.Base.Lima, b));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-16")]
    public async Task ElOrdenDeLosBloqueosDelReemplazoEsElDeLaJerarquia()
    {
        var (entorno, libro, pedido) = Escenario();
        await Reemplazar(entorno, pedido, libro, 1);
        entorno.Base.Bitacora.Llamadas.Clear();

        await Reemplazar(entorno, pedido, libro, 1);

        var orden = entorno.Base.Bitacora.Llamadas;
        Assert.True(orden.IndexOf("bloqueo.Cliente") < orden.IndexOf("bloqueo.Pedido"));
        Assert.True(orden.IndexOf("bloqueo.Pedido") < orden.IndexOf("existencias.BloquearFilas"));
        Assert.True(orden.IndexOf("reservas.BloquearLineas") < orden.IndexOf("existencias.BloquearFilas"));
        Assert.True(orden.IndexOf("existencias.BloquearFilas") < orden.IndexOf("reservas.LiberarLineas"));
        Assert.True(orden.IndexOf("reservas.LiberarLineas") < orden.IndexOf("existencias.Aplicar(0,-1)"));
        Assert.True(orden.IndexOf("existencias.Aplicar(0,-1)") < orden.IndexOf("reservas.Insertar"));
    }
}
