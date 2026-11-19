using System.Text.Json;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T06 (PED-33, PED-34, PED-39, AC-PED-28, AC-PED-29): anonimización del contacto y del iniciador y la retención de dos años por fecha de Lima.</summary>
public sealed class AnonimizacionTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Pedido Domicilio()
    {
        var linea = new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Libro", "9780000000001", 1, 50m, 50m, "LIM", false, null, null);
        var datos = new DatosDePedidoNuevo(
            Guid.NewGuid(), "ABCD234567", Cuenta, TipoDeEntregaDelPedido.Domicilio, null, "ayacucho", 12m, 62m, ContactoDeEntrega.Crear("Ana Pérez", "987654321", "Jr. Lima 123"),
            new AceptacionDeTexto("t-1", Inicio), new AceptacionDeTexto("p-1", Inicio), null, [linea], Inicio);
        return Pedido.Crear(datos).Pedido!;
    }

    private static Pedido Recojo()
    {
        var linea = new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Libro", "9780000000001", 1, 50m, 50m, "LIM", false, null, null);
        var datos = new DatosDePedidoNuevo(Guid.NewGuid(), "ABCD234568", Cuenta, TipoDeEntregaDelPedido.Recojo, "LIM", null, 0m, 50m, null, new("t-1", Inicio), new("p-1", Inicio), null, [linea], Inicio);
        return Pedido.Crear(datos).Pedido!;
    }

    [Fact]
    [Trait("Criterio", "AC-PED-28")]
    public void Anonimizar_el_contacto_borra_nombre_telefono_y_direccion_y_conserva_lineas_importes_fechas_y_la_cuenta_opaca()
    {
        var pedido = Domicilio();
        var linea = pedido.Lineas.Single();

        var cambio = pedido.AnonimizarContacto(Inicio.AddDays(10));

        Assert.True(cambio);
        Assert.Null(pedido.Contacto);
        Assert.Equal(Inicio.AddDays(10), pedido.ContactoAnonimizadoEn);
        Assert.True(pedido.ContactoEstaAnonimizado);
        Assert.Equal((Cuenta, 62m, "Libro", 50m, Inicio), (pedido.CuentaId, pedido.Total, linea.Titulo, linea.Importe, pedido.CreadoEn));
        Assert.Equal("ayacucho", pedido.RegionDeEntrega);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-31")]
    public void Anonimizar_de_nuevo_un_pedido_ya_anonimizado_no_falla_ni_cambia_la_fecha_y_uno_de_recojo_termina_sin_error()
    {
        var pedido = Domicilio();
        pedido.AnonimizarContacto(Inicio.AddDays(1));

        var repetida = pedido.AnonimizarContacto(Inicio.AddDays(30));
        var recojo = Recojo().AnonimizarContacto(Inicio);

        Assert.False(repetida);
        Assert.Equal(Inicio.AddDays(1), pedido.ContactoAnonimizadoEn);
        Assert.False(recojo);
    }

    [Fact]
    public void Un_pedido_anonimizado_no_deja_rastro_del_contacto_en_su_texto_ni_en_su_serializacion()
    {
        var pedido = Domicilio();
        pedido.AnonimizarContacto(Inicio);

        var texto = pedido.ToString() + JsonSerializer.Serialize(pedido.Lineas.Select(l => l.ToString()));

        Assert.Null(pedido.Contacto);
        Assert.NotNull(pedido.ContactoAnonimizadoEn);
        Assert.DoesNotContain("Ana", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("987654321", texto, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PED-29")]
    [InlineData(2, 1, true)]
    [InlineData(2, 0, true)]
    [InlineData(1, -1, false)]
    public void La_retencion_vence_a_los_dos_anos_calendario_en_hora_de_Lima_y_no_un_dia_antes(int anios, int diasDeMas, bool vencida)
    {
        // El último cambio a entregado fue el 2024-03-10 a las 12:00 de Lima.
        var fechaFinal = new DateTimeOffset(2024, 3, 10, 17, 0, 0, TimeSpan.Zero);
        var hoy = new DateOnly(2024 + anios, 3, 10).AddDays(diasDeMas);

        Assert.Equal(vencida, PoliticaDeRetencion.EstaVencida(fechaFinal, hoy));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-29")]
    public void Dos_anos_menos_un_dia_no_vencen_y_el_borde_se_mide_con_la_fecha_de_Lima_y_no_la_UTC()
    {
        // 2024-03-10 23:30 en Lima es 2024-03-11 04:30 UTC: cuenta el día 10 de Lima.
        var tarde = new DateTimeOffset(2024, 3, 11, 4, 30, 0, TimeSpan.Zero);

        Assert.False(PoliticaDeRetencion.EstaVencida(tarde, new DateOnly(2026, 3, 9)));
        Assert.True(PoliticaDeRetencion.EstaVencida(tarde, new DateOnly(2026, 3, 10)));
        Assert.Equal(new DateOnly(2024, 3, 10), PoliticaDeRetencion.FechaEnLima(tarde));
    }

    [Fact]
    public void Un_pedido_del_29_de_febrero_vence_el_28_de_febrero_dos_anios_despues_y_sin_fecha_final_nunca_vence()
    {
        var bisiesto = new DateTimeOffset(2024, 2, 29, 17, 0, 0, TimeSpan.Zero);

        Assert.True(PoliticaDeRetencion.EstaVencida(bisiesto, new DateOnly(2026, 2, 28)));
        Assert.False(PoliticaDeRetencion.EstaVencida(bisiesto, new DateOnly(2026, 2, 27)));
        Assert.False(Domicilio().EstaVencidoParaDepurar(new DateOnly(2100, 1, 1)));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-29")]
    public void Un_pedido_entregado_hace_dos_anos_y_un_dia_se_depura_y_repetir_la_depuracion_no_cambia_nada()
    {
        var pedido = Domicilio();
        var linea = pedido.Lineas.Single();
        pedido.PrepararLineas([linea.Id], Inicio);
        pedido.MarcarListas([linea.Id], Inicio);
        pedido.DespacharLineas([linea.Id], Inicio);
        pedido.EntregarLineas([linea.Id], Inicio.AddDays(3));
        var hoy = PoliticaDeRetencion.FechaEnLima(Inicio.AddDays(3)).AddYears(2).AddDays(1);

        var primera = pedido.AnonimizarPorRetencion(hoy, Inicio.AddYears(2).AddDays(10));
        var segunda = pedido.AnonimizarPorRetencion(hoy.AddDays(1), Inicio.AddYears(2).AddDays(11));

        Assert.True(primera);
        Assert.False(segunda);
        Assert.Null(pedido.Contacto);
    }

    [Fact]
    public void Un_pedido_en_curso_o_a_dos_anios_menos_un_dia_no_se_depura()
    {
        var enCurso = Domicilio();
        var entregado = Domicilio();
        var linea = entregado.Lineas.Single();
        entregado.PrepararLineas([linea.Id], Inicio);
        entregado.MarcarListas([linea.Id], Inicio);
        entregado.DespacharLineas([linea.Id], Inicio);
        entregado.EntregarLineas([linea.Id], Inicio);
        var vispera = PoliticaDeRetencion.FechaEnLima(Inicio).AddYears(2).AddDays(-1);

        Assert.False(enCurso.AnonimizarPorRetencion(new DateOnly(2100, 1, 1), Inicio));
        Assert.False(entregado.AnonimizarPorRetencion(vispera, Inicio));
        Assert.NotNull(entregado.Contacto);
    }

    [Fact]
    public void El_iniciador_de_una_operacion_se_anonimiza_dos_veces_sin_cambiar_el_resultado()
    {
        var sucursal = Guid.NewGuid();
        var entrega = Entrega.Iniciar(Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], new IniciadorDeOperacion(Guid.NewGuid(), "encargado-de-sucursal", sucursal), Inicio);

        entrega.AnonimizarIniciador();
        entrega.AnonimizarIniciador();

        Assert.Equal(new IniciadorDeOperacion(Guid.Empty, null, null), entrega.Iniciador);
    }

    [Fact]
    public void La_retencion_aplica_a_todo_pedido_tambien_de_recojo_para_el_iniciador_y_no_depende_del_contacto()
    {
        var recojo = Recojo();
        var linea = recojo.Lineas.Single();
        recojo.PrepararLineas([linea.Id], Inicio);
        recojo.MarcarListas([linea.Id], Inicio);
        recojo.EntregarLineas([linea.Id], Inicio);
        var hoy = PoliticaDeRetencion.FechaEnLima(Inicio).AddYears(2);

        Assert.True(recojo.EstaVencidoParaDepurar(hoy));
        Assert.False(recojo.AnonimizarPorRetencion(hoy, Inicio.AddYears(2)));
    }
}
