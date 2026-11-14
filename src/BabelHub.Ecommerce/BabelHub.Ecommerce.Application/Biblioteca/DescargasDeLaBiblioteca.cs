using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Biblioteca;

/// <summary>El enlace firmado de corta duración con el que el navegador descarga el archivo del almacén de objetos (BIB-04, RNF14). Nunca se guarda ni se registra.</summary>
public sealed record EnlaceDeDescarga(Uri Url, DateTimeOffset VenceEn)
{
    /// <summary>El texto del registro nunca incluye la URL firmada (SEG-10): un `ToString` autogenerado la filtraría en una interpolación.</summary>
    private bool PrintMembers(System.Text.StringBuilder constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);
        constructor.Append("VenceEn = ").Append(VenceEn.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }
}

/// <summary>
/// Contrato público de la Biblioteca digital para pedir una descarga. <b>Declaración mínima</b> hecha por <c>web-base</c> T27: solo lo que la interfaz
/// necesita. El plan de <c>ecommerce-biblioteca-digital</c> la implementa y la amplía sin cambiar lo que ya hay aquí.
/// </summary>
public interface IDescargasDeLaBiblioteca
{
    /// <summary>
    /// Verifica la titularidad y el cupo del acceso del usuario actual y entrega el enlace firmado, contando la descarga en ese momento (BIB-05).
    /// Con la misma <paramref name="claveDeIdempotencia"/> repite el mismo enlace sin contar otra descarga (SEG-22, BIB-05).
    /// </summary>
    Task<Resultado<EnlaceDeDescarga>> SolicitarEnlace(Guid accesoId, string claveDeIdempotencia, CancellationToken cancelacion = default);
}
