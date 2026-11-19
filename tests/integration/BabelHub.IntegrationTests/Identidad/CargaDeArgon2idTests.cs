using System.Diagnostics;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Domain.Identidad;
using BabelHub.Ecommerce.Infrastructure.Identidad;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// La carga de Argon2id con el semáforo de concurrencia (identidad-seguridad T47; AC-IDE-08, AC-IDE-57; IDE-09, D03): la VM de ventas (6 GB) comparte el hash de contraseñas con el resto del sistema, así que <b>a lo sumo
/// 4 cálculos simultáneos</b> de 19 MiB cada uno, aunque lleguen decenas de ingresos a la vez; todos los ingresos legítimos terminan con el resultado correcto, la memoria administrada no crece más allá de lo que esos turnos
/// permiten y, saturado, se responde con una excepción y <b>nunca</b> con un cálculo más barato ni con un «sí» (la cuenta inexistente cuesta lo mismo y no acierta). La reversión de la cancelación por módulo y por verbo
/// (pendientes, anonimizar, desvincular, eliminar, marcar y auditar) la prueban <c>EjecucionDeLaCancelacionTests</c> y <c>CancelacionDeCuentaTests</c> (AC-IDE-57).
/// </summary>
public sealed class CargaDeArgon2idTests
{
    private const int TurnosPorDefecto = Argon2idHasherDeContrasenas.ConcurrenciaPorDefecto;

    private static Contrasena Frase(string texto) => Contrasena.Presentada(texto)!;

    [Fact]
    [Trait("Criterio", "AC-IDE-08")]
    public async Task ConDecenasDeIngresosSimultaneos_NuncaHayMasDeLosTurnosPermitidos_YTodosTerminanBien()
    {
        using var hasher = new Argon2idHasherDeContrasenas(espera: TimeSpan.FromMinutes(2));
        var correcta = Frase("la frase correcta de la cuenta");
        var resumen = await hasher.Resumir(correcta);
        var picoDeTurnos = 0;
        using var cierre = new CancellationTokenSource();
        var muestreo = Task.Run(async () =>
        {
            while (!cierre.IsCancellationRequested)
            {
                picoDeTurnos = Math.Max(picoDeTurnos, TurnosPorDefecto - hasher.TurnosLibres);
                await Task.Delay(2).ConfigureAwait(false);
            }
        });

        var reloj = Stopwatch.StartNew();
        var intentos = Enumerable.Range(0, 32).Select(i => Task.Run(async () => (i % 4) switch
        {
            0 => (Esperado: true, Obtenido: await hasher.Verificar(resumen, correcta)),
            1 => (Esperado: false, Obtenido: await hasher.Verificar(resumen, Frase("una frase equivocada cualquiera"))),
            2 => (Esperado: false, Obtenido: await hasher.Verificar(null, correcta)),
            _ => (Esperado: false, Obtenido: await hasher.Verificar("no es un resumen", correcta)),
        })).ToArray();
        var resultados = await Task.WhenAll(intentos);
        reloj.Stop();
        await cierre.CancelAsync();
        await muestreo;

        Assert.All(resultados, r => Assert.Equal(r.Esperado, r.Obtenido));
        Assert.Equal(TurnosPorDefecto, picoDeTurnos);
        Assert.Equal(TurnosPorDefecto, hasher.TurnosLibres);
        Assert.True(reloj.Elapsed < TimeSpan.FromMinutes(2), "La carga no terminó en el plazo de espera.");
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-08")]
    public async Task SaturadoElSemaforo_SeLanzaLaExcepcion_NuncaSeAciertaNiSeBajaElCosto_YLosTurnosSeDevuelven()
    {
        using var hasher = new Argon2idHasherDeContrasenas(concurrenciaMaxima: 1, espera: TimeSpan.FromMilliseconds(30));
        var frase = Frase("una frase para saturar el semaforo");
        var resumen = await hasher.Resumir(frase);

        var tareas = Enumerable.Range(0, 24).Select(_ => Task.Run(async () =>
        {
            try
            {
                return (Saturada: false, Acierto: await hasher.Verificar(resumen, frase));
            }
            catch (CalculoDeContrasenasSaturadoException)
            {
                return (Saturada: true, Acierto: false);
            }
        })).ToArray();
        var resultados = await Task.WhenAll(tareas);

        Assert.Contains(resultados, r => r.Saturada);
        Assert.All(resultados.Where(r => !r.Saturada), r => Assert.True(r.Acierto));
        Assert.Equal(1, hasher.TurnosLibres);
        Assert.True(await hasher.Verificar(resumen, frase));
    }
}
