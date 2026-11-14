using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Soporte;

/// <summary>
/// Doble de <see cref="ITokenDelUsuarioDeLaSesion"/> para las pruebas de los clientes REST y de la web mientras la sesión real (<c>web-base</c> T14)
/// no exista. Solo existe en las pruebas: ninguna Api lo registra. El token que entrega se genera distinto en cada instancia (nunca un secreto fijo).
/// </summary>
public sealed class TokenDelUsuarioDePrueba : ITokenDelUsuarioDeLaSesion
{
    private ValorSecreto? _token;

    private TokenDelUsuarioDePrueba(ValorSecreto? token) => _token = token;

    /// <summary>Cuántas veces se pidió el token.</summary>
    public int Pedidos { get; private set; }

    /// <summary>Con sesión y con un token válido nuevo.</summary>
    public static TokenDelUsuarioDePrueba ConSesion() => new(new ValorSecreto("jwt." + Guid.NewGuid().ToString("N")));

    /// <summary>Sin sesión o sin credencial utilizable.</summary>
    public static TokenDelUsuarioDePrueba SinSesion() => new(null);

    /// <summary>Cambia el token durante la prueba (una renovación) o lo quita (<see langword="null"/>).</summary>
    public void Establecer(ValorSecreto? token) => _token = token;

    public Task<ValorSecreto?> Actual(CancellationToken cancelacion = default)
    {
        Pedidos++;
        return Task.FromResult(_token);
    }
}
