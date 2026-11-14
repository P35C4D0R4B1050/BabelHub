using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Infrastructure.Biblioteca;

/// <summary>
/// Implementación provisional de <see cref="IDescargasDeLaBiblioteca"/> hasta que exista el módulo Biblioteca digital (<c>ecommerce-biblioteca-digital</c>,
/// ECO-8): siempre responde «no disponible». No entrega ningún enlace; el registro real la reemplaza.
/// </summary>
public sealed class DescargasDeLaBibliotecaPendiente : IDescargasDeLaBiblioteca
{
    public Task<Resultado<EnlaceDeDescarga>> SolicitarEnlace(Guid accesoId, string claveDeIdempotencia, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<EnlaceDeDescarga>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "biblioteca-no-disponible")));
}

public static class BibliotecaPendienteExtensiones
{
    /// <summary>Registra la implementación provisional hasta que ECO-8 registre la real (con <c>Replace</c> o antes de esta llamada: <c>TryAdd</c> respeta lo ya registrado).</summary>
    public static IServiceCollection AgregarBibliotecaPendiente(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.TryAddScoped<IDescargasDeLaBiblioteca, DescargasDeLaBibliotecaPendiente>();
        return servicios;
    }
}
