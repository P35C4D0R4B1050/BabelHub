using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web.Indice;

namespace BabelHub.Ecommerce.Application.Web.Indice;

/// <summary>
/// El caso de uso del índice infinito (web-indice-infinito T05; D01, D05, D07): valida la dirección, la convierte en una página con el generador puro y elige direcciones al azar. <b>Sin transacción, repositorio, caché ni puertos nuevos</b>: solo
/// depende de <see cref="IAleatoriosSeguros"/> (el puerto de bytes criptográficos del módulo <c>Web</c>) para elegir enteros por <b>muestreo por rechazo</b> (sin sesgo). Vive en su propio espacio de nombres para que la prueba de arquitectura lo
/// aísle de <c>Plataforma</c> y de los demás módulos de negocio. Nunca devuelve ni registra el texto recibido.
/// </summary>
internal sealed class ConsultarIndiceInfinito(IAleatoriosSeguros aleatorios) : IIndiceInfinito
{
    /// <summary>El tope de reintentos si el azar repite la dirección actual (D05); pasado el tope se avanza la página de forma determinista.</summary>
    internal const int ReintentosMaximos = 8;

    /// <summary>El tope de sorteos fallidos del muestreo por rechazo: con 32 bits la probabilidad de rechazar es menor que 410 / 2^32, así que alcanzarlo equivale a un generador roto.</summary>
    internal const int SorteosMaximos = 8;

    public ResultadoDelIndice Abrir(string? direccion)
    {
        var evaluada = DireccionDelIndice.Evaluar(direccion);
        return evaluada.Estado switch
        {
            EstadoDeDireccion.Valida => new ResultadoDelIndice.Mostrar(Pagina(evaluada.Direccion!)),
            EstadoDeDireccion.Vacia => new ResultadoDelIndice.Redirigir(ElegirAlAzar()),
            _ => NoValida(),
        };
    }

    public ResultadoDelIndice Entrar(string? textoDelFormulario)
    {
        // Solo el espacio U+0020 de los extremos: el resto de los caracteres (un salto de línea, un tabulador) hace la dirección no válida.
        var recortado = textoDelFormulario?.Trim(' ');
        var evaluada = DireccionDelIndice.Evaluar(recortado);
        return evaluada.Estado switch
        {
            EstadoDeDireccion.Valida => new ResultadoDelIndice.Redirigir(evaluada.Direccion!),
            EstadoDeDireccion.Vacia => new ResultadoDelIndice.Redirigir(ElegirAlAzar()),
            _ => NoValida(),
        };
    }

    public string ElegirAlAzar(string? desde = null)
    {
        // Un «desde» no válido se ignora: ninguna dirección al azar puede coincidir con él, así que no hay nada que evitar.
        var actual = DireccionDelIndice.EsValida(desde ?? string.Empty) ? desde : null;
        for (var intento = 0; intento < ReintentosMaximos; intento++)
        {
            var nueva = DireccionAlAzar.Crear(Entero);
            if (nueva != actual)
            {
                return nueva;
            }
        }

        // El azar repitió la actual 8 veces seguidas: si coincide con una dirección al azar es de la forma y se avanza su página (sin bucle infinito).
        return DireccionAlAzar.AvanzarPagina(actual!);
    }

    private ResultadoDelIndice NoValida() => new ResultadoDelIndice.DireccionNoValida(Pagina(ElegirAlAzar()));

    private static PaginaDelIndice Pagina(string direccion) => new(direccion, GeneradorDePaginasDelIndice.Generar(direccion));

    /// <summary>Un entero de 0 a <paramref name="tamano"/> menos uno, sin sesgo: se descartan los sorteos de 32 bits que caen en el resto final de la división.</summary>
    private int Entero(int tamano)
    {
        const ulong Espacio = 1UL << 32;
        var limite = Espacio - (Espacio % (ulong)tamano);
        for (var sorteo = 0; sorteo < SorteosMaximos; sorteo++)
        {
            var valor = BitConverter.ToUInt32(aleatorios.Bytes(4));
            if (valor < limite)
            {
                return (int)(valor % (ulong)tamano);
            }
        }

        throw new InvalidOperationException("El generador de bytes aleatorios no produjo un valor utilizable.");
    }
}
