using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;
using Xunit.Abstractions;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>
/// La caché de disponibilidad del E-commerce (D03 de <c>ecommerce-catalogo</c>; palanca 4 del análisis de capacidad, DD-50) <b>medida con el resolutor real</b>: cuántas solicitudes por segundo le llegan al Inventario cuando el
/// catálogo recibe el tráfico del perfil realista de 2 000 sesiones (≈ 31 consultas de disponibilidad por segundo del catálogo y el carrito) con reloj simulado. No cambia comportamiento: <b>la lectura nunca simula stock</b>
/// (un dato invalidado o vencido se vuelve a pedir) y la decisión de reservar no pasa por aquí (CAR-06). Las cifras se escriben en la salida de la prueba y se citan en el análisis de la base de datos y la caché.
/// </summary>
public sealed class CacheDeDisponibilidadDeCargaTests(ITestOutputHelper salida)
{
    private const double ConsultasPorSegundo = 31; // 133 acciones/s × (18 % detalle + 5 % carrito)
    private const int Segundos = 300;

    [Fact]
    [Trait("Criterio", "AC-CAT-04")]
    public async Task ConElCatalogoDelPerfilDeCarga_DeVeinteEdiciones_ElInventarioRecibeUnaFraccionDeLasConsultas()
    {
        var (consultas, alInventario, _) = await Simular(ediciones: 20, exponente: 0.0);

        var porSegundoAlInventario = alInventario / (double)Segundos;
        salida.WriteLine($"20 ediciones, popularidad uniforme: {consultas} consultas ({ConsultasPorSegundo}/s) → {alInventario} al Inventario ({porSegundoAlInventario:F2}/s); acierto {1 - alInventario / (double)consultas:P1}");
        Assert.True(porSegundoAlInventario <= 20 / 30.0 * 1.2, $"El Inventario recibió {porSegundoAlInventario:F2} solicitudes por segundo");
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-04")]
    public async Task UnaConsultaDespuesDeLos30Segundos_VuelveAlInventarioYMuestraElStockActual_NuncaUnoInventado()
    {
        var e = new EntornoDelCatalogo();
        var edicion = Edicion.Impresa(Guid.NewGuid(), Isbn.Crear("9780000000019"), Precio.Crear(50m));
        e.Inventario.Ediciones[edicion.Id] = [new DisponibilidadPorFuente("lima", "Lima", 5)];

        var primera = await e.Resolutor.Resolver([edicion], new PresupuestoDeLlamadas(), CancellationToken.None);
        e.Inventario.Ediciones[edicion.Id] = [new DisponibilidadPorFuente("lima", "Lima", 0)];
        var dentro = await e.Resolutor.Resolver([edicion], new PresupuestoDeLlamadas(), CancellationToken.None);
        e.Reloj.Ahora = e.Reloj.Ahora.Add(ResolutorDeDisponibilidad.VigenciaDeLaCache);
        var fuera = await e.Resolutor.Resolver([edicion], new PresupuestoDeLlamadas(), CancellationToken.None);

        Assert.Equal(5, primera[edicion.Id].VendibleEnLaRed);
        Assert.Equal(5, dentro[edicion.Id].VendibleEnLaRed); // desfase acotado a 30 s: se dice «reciente», la reserva valida contra la base
        Assert.Equal(0, fuera[edicion.Id].VendibleEnLaRed);
        Assert.Equal(2, e.Inventario.LotesConsultados.Count);
    }

    private static async Task<(int Consultas, int AlInventario, int Ediciones)> Simular(int ediciones, double exponente)
    {
        var e = new EntornoDelCatalogo();
        var lista = new List<Edicion>();
        for (var i = 0; i < ediciones; i++)
        {
            var n = 100000 + i;
            var doce = "978" + n.ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
            var suma = doce.Select((c, k) => (c - '0') * (k % 2 == 0 ? 1 : 3)).Sum();
            var edicion = Edicion.Impresa(Guid.NewGuid(), Isbn.Crear(doce + ((10 - (suma % 10)) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture)), Precio.Crear(50m));
            e.Inventario.Ediciones[edicion.Id] = [new DisponibilidadPorFuente("lima", "Lima", 10)];
            lista.Add(edicion);
        }

        // Probabilidad acumulada de la popularidad 1 / rango^exponente (0 = uniforme), con una semilla fija: la corrida es repetible.
        var pesos = Enumerable.Range(1, ediciones).Select(r => 1.0 / Math.Pow(r, exponente)).ToArray();
        var total = pesos.Sum();
        var acumulada = new double[ediciones];
        var suma2 = 0.0;
        for (var i = 0; i < ediciones; i++)
        {
            suma2 += pesos[i] / total;
            acumulada[i] = suma2;
        }

        var azar = new Random(20261121);
        var consultas = 0;
        var pasoMs = 1000.0 / ConsultasPorSegundo;
        var inicio = e.Reloj.Ahora;
        for (var t = 0; t < Segundos * ConsultasPorSegundo; t++)
        {
            e.Reloj.Ahora = inicio.AddMilliseconds(t * pasoMs);
            var u = azar.NextDouble();
            var indice = Array.BinarySearch(acumulada, u);
            indice = indice < 0 ? ~indice : indice;
            await e.Resolutor.Resolver([lista[Math.Min(indice, ediciones - 1)]], new PresupuestoDeLlamadas(), CancellationToken.None);
            consultas++;
        }

        return (consultas, e.Inventario.LotesConsultados.Sum(l => l.Count), ediciones);
    }
}
