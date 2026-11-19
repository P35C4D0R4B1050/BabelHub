using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>Un <c>ICheckoutDelCliente</c> que anota cada llamada (con su entrada) y responde lo que la prueba le pida: sirve para probar la Presentación del checkout sin la Aplicación.</summary>
public sealed class CheckoutDelClienteFalso : ICheckoutDelCliente
{
    public static readonly VistaDelCheckout VistaVacia = new(EstadoVisibleDelCheckout.EnEdicion, 1, 5, [], null, null, null, 0m, null, 0m, null, [], [], null, null, false);

    public List<string> Llamadas { get; } = [];

    public List<object> Entradas { get; } = [];

    public Resultado<VistaDelCheckout> RespuestaDeVista { get; set; } = Resultado<VistaDelCheckout>.Exito(VistaVacia);

    /// <summary>Una respuesta distinta para una llamada concreta (por su nombre: <c>entrega</c>, <c>aceptaciones</c>…); <see langword="null"/> usa <see cref="RespuestaDeVista"/>.</summary>
    public Func<string, Resultado<VistaDelCheckout>?>? RespuestaDeVistaPorLlamada { get; set; }

    public Resultado<ResultadoDePasarAlPago> RespuestaDePago { get; set; } = Resultado<ResultadoDePasarAlPago>.Exito(new ResultadoDePasarAlPago(Guid.Parse("f0000000-0000-0000-0000-000000000001"), EstadoVisibleDelCheckout.EnPago));

    public Resultado<VistaDelCarrito> RespuestaDeSalida { get; set; } = Resultado<VistaDelCarrito>.Exito(new VistaDelCarrito([], 0m, [], false));

    public Task<Resultado<VistaDelCheckout>> Iniciar(IniciarCheckoutEntrada entrada, CancellationToken cancelacion = default) => Vista("iniciar", entrada);

    public Task<Resultado<VistaDelCheckout>> Obtener(CancellationToken cancelacion = default) => Vista("obtener", null);

    public Task<Resultado<VistaDelCheckout>> ElegirEntrega(ElegirEntregaEntrada entrada, CancellationToken cancelacion = default) => Vista("entrega", entrada);

    public Task<Resultado<VistaDelCheckout>> RegistrarAceptaciones(RegistrarAceptacionesEntrada entrada, CancellationToken cancelacion = default) => Vista("aceptaciones", entrada);

    public Task<Resultado<ResultadoDePasarAlPago>> PasarAlPago(PasarAlPagoEntrada entrada, CancellationToken cancelacion = default)
    {
        Llamadas.Add("pagar");
        Entradas.Add(entrada);
        return Task.FromResult(RespuestaDePago);
    }

    public Task<Resultado<VistaDelCarrito>> VolverAlCarrito(SalirDelCheckoutEntrada entrada, CancellationToken cancelacion = default) => Salida("volver", entrada);

    public Task<Resultado<VistaDelCarrito>> Reiniciar(SalirDelCheckoutEntrada entrada, CancellationToken cancelacion = default) => Salida("reiniciar", entrada);

    public Task<Resultado<CheckoutVivoParaPagar>> ObtenerParaPagar(CancellationToken cancelacion = default) => throw new NotSupportedException();

    private Task<Resultado<VistaDelCheckout>> Vista(string llamada, object? entrada)
    {
        Llamadas.Add(llamada);
        if (entrada is not null)
        {
            Entradas.Add(entrada);
        }

        return Task.FromResult(RespuestaDeVistaPorLlamada?.Invoke(llamada) ?? RespuestaDeVista);
    }

    private Task<Resultado<VistaDelCarrito>> Salida(string llamada, object entrada)
    {
        Llamadas.Add(llamada);
        Entradas.Add(entrada);
        return Task.FromResult(RespuestaDeSalida);
    }
}

/// <summary>La verificación de persona de las pruebas: responde lo que se le indique y anota el token y la acción que recibe.</summary>
public sealed class ExigenciaDeVerificacionFalsa : IExigenciaDeVerificacionDePersona
{
    public ResultadoDeExigencia Resultado { get; set; } = ResultadoDeExigencia.NoExigida;

    public List<(AccionProtegida Accion, string? Token)> Llamadas { get; } = [];

    public Task<ResultadoDeExigencia> Exigir(AccionProtegida accion, string? tokenDeVerificacion, CancellationToken cancelacion = default)
    {
        Llamadas.Add((accion, tokenDeVerificacion));
        return Task.FromResult(Resultado);
    }
}
