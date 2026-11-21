using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.UnitTests.Soporte;
using static BabelHub.Inventario.UnitTests.Reservas.EntornoDeReservas;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>
/// El tope de unidades de una reserva (decisión de la persona, 2026-11-19): <b>20 unidades por línea y 30 líneas</b> (antes 100 y 50), de modo que una sola reserva retiene a lo más 600 unidades. La frontera se prueba por los dos lados: 20 y 30 se aceptan, 21 y 31 se rechazan con su campo.
/// </summary>
public sealed class TopesDeReservasTests
{
    [Fact]
    [Trait("Criterio", "AC-SEL-41")]
    public async Task Una_linea_de_20_unidades_se_reserva_y_una_de_21_se_rechaza_en_su_campo()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 100, 0);

        var veinte = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro, 20)]));
        var veintiuna = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro, 21)]));

        Assert.True(veinte.EsExito, veinte.Error?.Clave);
        Assert.Contains(veintiuna.Error!.Errores!, e => e.Campo == "lineas.cantidad");
        Assert.Equal((100, 20), entorno.FilaDe(entorno.Base.Lima, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-41")]
    public async Task Una_reserva_de_30_lineas_se_acepta_y_una_de_31_se_rechaza_sin_apartar_nada()
    {
        var entorno = new EntornoDeReservas();
        var libros = Enumerable.Range(0, 31).Select(_ => entorno.Impresa()).ToList();
        foreach (var libro in libros)
        {
            entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 5, 0);
        }

        var treinta = await entorno.Reservar().Ejecutar(ARecojo("lima", [.. libros.Take(30).Select(l => Linea(l))]));
        var treintaYUna = await entorno.Reservar().Ejecutar(ARecojo("lima", [.. libros.Select(l => Linea(l))]));

        Assert.True(treinta.EsExito, treinta.Error?.Clave);
        Assert.Contains(treintaYUna.Error!.Errores!, e => e.Campo == "lineas");
        Assert.Equal((5, 0), entorno.FilaDe(entorno.Base.Lima, libros[30]));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-41")]
    public async Task La_consulta_previa_aplica_los_mismos_topes_y_no_toca_el_stock()
    {
        var entorno = new EntornoDeReservas(ActorDePrueba.DeIntegracion("lectura-de-disponibilidad"));
        var libros = Enumerable.Range(0, 31).Select(_ => entorno.Impresa()).ToList();
        foreach (var libro in libros)
        {
            entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 100, 0);
        }

        var valida = await entorno.ConsultarSeleccion().Ejecutar(new ConsultarSeleccionEntrada(TipoDeEntregaDeReserva.Recojo, "lima", null, [.. libros.Take(30).Select(l => Linea(l, 20))]));
        var demasiadasLineas = await entorno.ConsultarSeleccion().Ejecutar(new ConsultarSeleccionEntrada(TipoDeEntregaDeReserva.Recojo, "lima", null, [.. libros.Select(l => Linea(l))]));
        var demasiadasUnidades = await entorno.ConsultarSeleccion().Ejecutar(new ConsultarSeleccionEntrada(TipoDeEntregaDeReserva.Recojo, "lima", null, [Linea(libros[0], 21)]));

        Assert.True(valida.EsExito, valida.Error?.Clave);
        Assert.Contains(demasiadasLineas.Error!.Errores!, e => e.Campo == "lineas");
        Assert.Contains(demasiadasUnidades.Error!.Errores!, e => e.Campo == "lineas.cantidad");
        Assert.Equal((100, 0), entorno.FilaDe(entorno.Base.Lima, libros[0]));
    }

    [Fact]
    public void Los_topes_son_los_decididos_y_una_reserva_retiene_a_lo_mas_600_unidades()
    {
        Assert.Equal((30, 20, 600), (LimitesDeReservas.MaximoDeLineas, LimitesDeReservas.MaximoDeCantidad, LimitesDeReservas.MaximoDeLineas * LimitesDeReservas.MaximoDeCantidad));
    }
}
