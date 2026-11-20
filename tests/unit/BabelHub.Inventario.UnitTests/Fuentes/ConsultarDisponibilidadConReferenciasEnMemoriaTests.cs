using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Fuentes;

/// <summary>
/// La consulta pública de disponibilidad puede leer la lista de fuentes y el formato de las ediciones de una memoria de 5 s (<see cref="CacheDeReferencias"/>) para no ir a la base por ellos en cada solicitud
/// (RNF02, hallazgo de capacidad de K6-1). Lo que se prueba: que ahorra esas lecturas, que <b>nunca</b> guarda stock ni vencidas ni lo que decide una reserva, que vence a los 5 s, que no guarda ausencias y que
/// quien decide (<see cref="ConsultaDeExistencias.Disponibilidad"/>) no la usa.
/// </summary>
public sealed class ConsultarDisponibilidadConReferenciasEnMemoriaTests
{
    private static (ConsultarDisponibilidad Caso, ConsultaDeExistencias Existencias, EntornoDeFuentes Entorno) Montar()
    {
        var e = new EntornoDeFuentes(ActorDePrueba.DeIntegracion("e-commerce"));
        var referencias = new CacheDeReferencias(e.Reloj, CacheDeReferencias.VidaSugerida);
        var existencias = new ConsultaDeExistencias(e.Lectura, e.Fuentes, e.Existencias, e.Reservas, referencias);
        var caso = new ConsultarDisponibilidad(e.Autorizador, new ValidadorDeConsultarDisponibilidad(), e.Lectura, e.Fichas, existencias, e.Reloj, referencias);
        return (caso, existencias, e);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-08")]
    public async Task LaSegundaConsulta_NoVuelveALeerLasFuentesNiLaFicha_PeroSiElStock()
    {
        var (caso, _, e) = Montar();
        var libro = e.Impresa();
        e.Existencias.Poner(e.Lima.Id, libro, 8, 0);
        await caso.Ejecutar(new ConsultarDisponibilidadEntrada([libro]));
        var fuentesLeidas = e.Fuentes.LecturasDeLaLista;
        var fichasPedidas = e.Fichas.LecturasPorIds.Count;
        e.Bitacora.Llamadas.Clear();

        var segunda = await caso.Ejecutar(new ConsultarDisponibilidadEntrada([libro]));

        Assert.Equal(fuentesLeidas, e.Fuentes.LecturasDeLaLista);
        Assert.Equal(fichasPedidas, e.Fichas.LecturasPorIds.Count);
        Assert.Contains("existencias.Leer", e.Bitacora.Llamadas);
        Assert.Contains("reservas.VencidasPorEdicion", e.Bitacora.Llamadas);
        Assert.Equal(8, Assert.Single(segunda.Valor.Ediciones).VendibleEnLaRed);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-08")]
    public async Task ElStockQueCambiaEntreDosConsultas_SeVeAlInstante_AunqueLasReferenciasEstenEnMemoria()
    {
        var (caso, _, e) = Montar();
        var libro = e.Impresa();
        e.Existencias.Poner(e.Lima.Id, libro, 8, 0);
        await caso.Ejecutar(new ConsultarDisponibilidadEntrada([libro]));

        e.Existencias.Poner(e.Lima.Id, libro, 3, 1);
        var segunda = await caso.Ejecutar(new ConsultarDisponibilidadEntrada([libro]));

        Assert.Equal(2, Assert.Single(segunda.Valor.Ediciones).VendibleEnLaRed);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-08")]
    public async Task PasadosCincoSegundos_VuelveALeerLasReferenciasDeLaBase()
    {
        var (caso, _, e) = Montar();
        var libro = e.Impresa();
        e.Existencias.Poner(e.Lima.Id, libro, 8, 0);
        await caso.Ejecutar(new ConsultarDisponibilidadEntrada([libro]));
        var fuentesLeidas = e.Fuentes.LecturasDeLaLista;

        e.Reloj.Ahora += CacheDeReferencias.VidaSugerida + TimeSpan.FromMilliseconds(1);
        await caso.Ejecutar(new ConsultarDisponibilidadEntrada([libro]));

        Assert.Equal(fuentesLeidas + 1, e.Fuentes.LecturasDeLaLista);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-08")]
    public async Task UnaEdicionNoRegistrada_NoSeGuarda_YSeVuelveAPreguntarEnCadaConsulta()
    {
        var (caso, _, e) = Montar();
        var inventada = Guid.NewGuid();

        var primera = await caso.Ejecutar(new ConsultarDisponibilidadEntrada([inventada]));
        var segunda = await caso.Ejecutar(new ConsultarDisponibilidadEntrada([inventada]));

        Assert.Equal([inventada], primera.Valor.NoRegistradas);
        Assert.Equal([inventada], segunda.Valor.NoRegistradas);
        Assert.Equal(2, e.Fichas.LecturasPorIds.Count(l => l.Contains(inventada)));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-08")]
    public async Task ElCasoQueDecide_LeeSiempreLaBase_AunqueLaConsultaPublicaYaGuardaraLasReferencias()
    {
        var (caso, existencias, e) = Montar();
        var libro = e.Impresa();
        e.Existencias.Poner(e.Lima.Id, libro, 8, 0);
        await caso.Ejecutar(new ConsultarDisponibilidadEntrada([libro]));
        var fuentesLeidas = e.Fuentes.LecturasDeLaLista;

        await existencias.Disponibilidad([libro]);

        Assert.Equal(fuentesLeidas + 1, e.Fuentes.LecturasDeLaLista);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-08")]
    public async Task UnaFuenteNuevaAparece_ApenasVenceLaMemoria()
    {
        var (caso, _, e) = Montar();
        var libro = e.Impresa();
        e.Existencias.Poner(e.Lima.Id, libro, 8, 0);
        var antes = await caso.Ejecutar(new ConsultarDisponibilidadEntrada([libro]));
        e.Fuentes.Agregar("arequipa", "arequipa");

        e.Reloj.Ahora += CacheDeReferencias.VidaSugerida + TimeSpan.FromMilliseconds(1);
        var despues = await caso.Ejecutar(new ConsultarDisponibilidadEntrada([libro]));

        Assert.Equal(3, Assert.Single(antes.Valor.Ediciones).PorFuente.Count);
        Assert.Equal(4, Assert.Single(despues.Valor.Ediciones).PorFuente.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-08")]
    public async Task SinConfigurarLaVida_LaMemoriaEstaApagada_YCadaConsultaLeeLaBase()
    {
        var e = new EntornoDeFuentes(ActorDePrueba.DeIntegracion("e-commerce"));
        var referencias = new CacheDeReferencias(e.Reloj);
        var existencias = new ConsultaDeExistencias(e.Lectura, e.Fuentes, e.Existencias, e.Reservas, referencias);
        var caso = new ConsultarDisponibilidad(e.Autorizador, new ValidadorDeConsultarDisponibilidad(), e.Lectura, e.Fichas, existencias, e.Reloj, referencias);
        var libro = e.Impresa();
        e.Existencias.Poner(e.Lima.Id, libro, 8, 0);

        await caso.Ejecutar(new ConsultarDisponibilidadEntrada([libro]));
        var fuentesLeidas = e.Fuentes.LecturasDeLaLista;
        await caso.Ejecutar(new ConsultarDisponibilidadEntrada([libro]));

        Assert.Equal(TimeSpan.Zero, referencias.Vida);
        Assert.Equal(fuentesLeidas + 1, e.Fuentes.LecturasDeLaLista);
        Assert.Equal(2, e.Fichas.LecturasPorIds.Count);
    }
}
