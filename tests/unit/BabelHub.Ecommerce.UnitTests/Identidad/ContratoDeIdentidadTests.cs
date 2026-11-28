using System.Reflection;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// El contrato público de Identidad (identidad-seguridad T13; AC-IDE-26, AC-IDE-30; IDE-29, IDE-33): la raíz del módulo usa modelos propios (ningún tipo del Dominio ni de <c>Shared</c>), existen todas las
/// interfaces del plan y ningún valor secreto se imprime.
/// </summary>
public sealed class ContratoDeIdentidadTests
{
    private static readonly string Espacio = typeof(IIngreso).Namespace!;

    private static IEnumerable<Type> TiposDeLaFirma(Type tipo)
    {
        const BindingFlags Todo = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var usados = new List<Type>();
        usados.AddRange(tipo.GetProperties(Todo).Select(p => p.PropertyType));
        usados.AddRange(tipo.GetMethods(Todo).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)));
        usados.AddRange(tipo.GetConstructors().SelectMany(c => c.GetParameters().Select(p => p.ParameterType)));
        return usados.SelectMany(Desarmar);
    }

    private static IEnumerable<Type> Desarmar(Type tipo)
    {
        if (tipo.IsGenericType)
        {
            foreach (var argumento in tipo.GetGenericArguments().SelectMany(Desarmar))
            {
                yield return argumento;
            }
        }

        if (tipo.HasElementType)
        {
            foreach (var elemento in Desarmar(tipo.GetElementType()!))
            {
                yield return elemento;
            }
        }

        yield return tipo;
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public void ElContratoPublico_NoExponeTiposDelDominioNiDeShared()
    {
        var prohibidos = new[] { "BabelHub.Ecommerce.Domain", "BabelHub.Shared" };

        var infracciones = typeof(IIngreso).Assembly.GetExportedTypes()
            .Where(t => t.Namespace == Espacio)
            .SelectMany(t => TiposDeLaFirma(t).Select(u => (Tipo: t.Name, Usa: u)))
            .Where(p => p.Usa.Namespace is { } n && prohibidos.Any(x => n.StartsWith(x, StringComparison.Ordinal)))
            .Select(p => $"{p.Tipo} usa {p.Usa.FullName}")
            .Distinct()
            .ToList();

        Assert.Empty(infracciones);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public void LaCredencialYLaRenovacion_SonSecretosQueNoSeImprimen()
    {
        var credencial = "jwt.secreto." + Guid.NewGuid().ToString("N");
        var renovacion = "renovacion-" + Guid.NewGuid().ToString("N");
        var sesion = new CredencialDeSesion(new ValorSecreto(credencial), DateTimeOffset.UtcNow, new ValorSecreto(renovacion), DateTimeOffset.UtcNow, Guid.NewGuid(), RolDeCuenta.Cliente, null, true);
        var desafio = new DesafioDelIngreso(new ValorSecreto("desafio-" + Guid.NewGuid().ToString("N")), PasoPendienteDelIngreso.PresentarSegundoFactor, DateTimeOffset.UtcNow);
        var resultado = new ResultadoDelIngreso { Credencial = sesion, Desafio = desafio, CodigosDeRespaldo = [new ValorSecreto("ABCD-EFGH-IJKL")] };

        var texto = sesion + " " + desafio + " " + resultado;

        Assert.DoesNotContain(credencial, texto, StringComparison.Ordinal);
        Assert.DoesNotContain(renovacion, texto, StringComparison.Ordinal);
        Assert.DoesNotContain("desafio-", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("ABCD", texto, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public void LosMotivosDeAlertaQueUsaLaAplicacion_ConstanEnElCatalogoCerradoDeAuditoria()
    {
        var catalogo = BabelHub.Ecommerce.Application.Auditoria.Interno.CatalogoDeAlertasDeAuditoria.Crear();

        foreach (var motivo in new[]
        {
            Application.Identidad.Interno.Sesiones.MotivoDeRenovacionReutilizada,
            Application.Identidad.Interno.EmisionDeCodigos.MotivoDeFallaDelCorreo,
            Application.Plataforma.Interno.CuotaDeCorreo.MotivoDeCuotaAlta,
            Application.Plataforma.Interno.CuotaDeCorreo.MotivoDeCupoDeCodigosAlto,
        })
        {
            Assert.True(catalogo.Contiene(motivo), motivo);
        }

        Assert.Equal(BabelHub.Ecommerce.Domain.Auditoria.ClaseDeAlerta.Seguridad, catalogo.Obtener(Application.Identidad.Interno.Sesiones.MotivoDeRenovacionReutilizada).Clase);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public void LaCredencialEmitida_NoImprimeElJwt()
    {
        var jwt = "eyJ.secreto." + Guid.NewGuid().ToString("N");

        Assert.DoesNotContain(jwt, new Application.Identidad.Interno.CredencialEmitida(jwt, DateTimeOffset.UtcNow).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-26")]
    public void ElEstadoDeCuenta_NoLlevaDatosPersonales()
    {
        var propiedades = typeof(EstadoDeCuenta).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(propiedades, n => n is "Correo" or "Nombre" or "Numero" or "NumeroDeWhatsApp");
        Assert.Contains("PuedeComprar", propiedades);
        Assert.Contains("CorreoVerificado", propiedades);
    }
}
