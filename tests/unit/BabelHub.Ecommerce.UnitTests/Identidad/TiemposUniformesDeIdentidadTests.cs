using System.Diagnostics;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// Tiempo y respuesta uniformes con el correo caído (identidad-seguridad T48; AC-IDE-54; IDE-03, IDE-17, IDE-65): la recuperación, el reenvío y el registro responden <b>exactamente lo mismo</b> exista o no la cuenta, el
/// envío va en segundo plano (el correo caído no se toca en el camino síncrono) y, en 50 intentos de cada caso, la diferencia de medianas es de menos de 100 ms y la de los percentiles 90 también: se compara la
/// <b>distribución</b>, no solo la mediana, para que un camino más lento en la cola no delate la existencia de una cuenta.
/// </summary>
public sealed class TiemposUniformesDeIdentidadTests
{
    private const int Intentos = 50;

    private static readonly TimeSpan Margen = TimeSpan.FromMilliseconds(100);

    private static async Task<List<TimeSpan>> Medir(Func<Task> operacion)
    {
        await operacion().ConfigureAwait(false);
        var tiempos = new List<TimeSpan>(Intentos);
        for (var i = 0; i < Intentos; i++)
        {
            var reloj = Stopwatch.StartNew();
            await operacion().ConfigureAwait(false);
            tiempos.Add(reloj.Elapsed);
        }

        return tiempos;
    }

    private static TimeSpan Percentil(List<TimeSpan> tiempos, double p)
    {
        var ordenados = tiempos.Order().ToList();
        return ordenados[Math.Min(ordenados.Count - 1, (int)Math.Ceiling(p * ordenados.Count) - 1)];
    }

    private static void ComprobarDistribucion(List<TimeSpan> existente, List<TimeSpan> inexistente)
    {
        Assert.Equal(Intentos, existente.Count);
        Assert.Equal(Intentos, inexistente.Count);
        Assert.True((Percentil(existente, 0.5) - Percentil(inexistente, 0.5)).Duration() < Margen, "La diferencia de medianas es de 100 ms o más.");
        Assert.True((Percentil(existente, 0.9) - Percentil(inexistente, 0.9)).Duration() < Margen, "La diferencia de percentiles 90 es de 100 ms o más.");
        Assert.True((Percentil(existente, 1.0) - Percentil(inexistente, 1.0)).Duration() < Margen * 5, "Un caso tiene colas mucho más largas que el otro.");
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-54")]
    public async Task LaRecuperacion_ConElCorreoCaido_RespondeLoMismoYEnElMismoTiempo_ExistaONoLaCuenta()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("existe@example.com");
        e.Correo.Falla = true;
        e.Limites.Maximos["codigos-por-origen"] = 100_000;
        var recuperacion = e.Recuperacion();

        var respuestaExistente = await recuperacion.Solicitar("existe@example.com");
        var respuestaInexistente = await recuperacion.Solicitar("nadie@example.com");
        var existente = await Medir(() => recuperacion.Solicitar("existe@example.com"));
        var inexistente = await Medir(() => recuperacion.Solicitar("nadie@example.com"));

        Assert.True(respuestaExistente.EsExito && respuestaInexistente.EsExito);
        Assert.Equal(respuestaExistente.Valor, respuestaInexistente.Valor);
        Assert.Empty(e.Correo.Codigos);
        ComprobarDistribucion(existente, inexistente);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-54")]
    public async Task ElReenvio_ConElCorreoCaido_RespondeLoMismoYEnElMismoTiempo_ExistaONoLaCuenta()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("existe@example.com", verificada: false);
        e.Correo.Falla = true;
        e.Limites.Maximos["codigos-por-origen"] = 100_000;
        e.Limites.Maximos["reenvio-por-correo"] = 100_000;
        var registro = e.Registro();

        var respuestaExistente = await registro.ReenviarCodigo("existe@example.com");
        var respuestaInexistente = await registro.ReenviarCodigo("nadie@example.com");
        var existente = await Medir(() => registro.ReenviarCodigo("existe@example.com"));
        var inexistente = await Medir(() => registro.ReenviarCodigo("nadie@example.com"));

        Assert.True(respuestaExistente.EsExito && respuestaInexistente.EsExito);
        Assert.Equal(respuestaExistente.Valor, respuestaInexistente.Valor);
        Assert.Empty(e.Correo.Codigos);
        ComprobarDistribucion(existente, inexistente);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-54")]
    public async Task ElRegistroConUnCorreoYaEnUso_RespondeLoMismoAunqueUnPuertoFalle_YEnElMismoTiempo()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("verificada@example.com");
        e.Cuenta("sin-verificar@example.com", verificada: false);
        e.Correo.Falla = true;
        e.Limites.Maximos["registros-por-origen"] = 100_000;
        e.Limites.Maximos["codigos-por-origen"] = 100_000;
        var registro = e.Registro();

        // Un correo verificado se rechaza de inmediato; uno sin verificar intenta reemplazar la cuenta y, si el puerto del carrito falla, se rechaza igual (IDE-13, IDE-88).
        e.Carrito.Falla = true;
        var verificada = await registro.Registrar(e.Alta("verificada@example.com"));
        var sinVerificar = await registro.Registrar(e.Alta("sin-verificar@example.com"));
        var tiemposVerificada = await Medir(() => registro.Registrar(e.Alta("verificada@example.com")));
        var tiemposSinVerificar = await Medir(() => registro.Registrar(e.Alta("sin-verificar@example.com")));

        Assert.False(verificada.EsExito);
        Assert.False(sinVerificar.EsExito);
        Assert.Equal((CategoriaDeError.Conflicto, "correo-en-uso"), (verificada.Error!.Categoria, verificada.Error.Clave));
        Assert.Equal((verificada.Error.Categoria, verificada.Error.Clave), (sinVerificar.Error!.Categoria, sinVerificar.Error.Clave));
        ComprobarDistribucion(tiemposVerificada, tiemposSinVerificar);
    }
}
