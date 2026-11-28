using System.Reflection;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

public sealed class ContratoDePlataformaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly ReservaDeLimite Reserva = new("ingresos-fallidos", "c:abc", "e1", Ahora);

    [Fact]
    public void ElContratoPublico_NoExponeTiposDelDominioNiDeShared()
    {
        // Regla 13: otro módulo no puede depender del Dominio de Plataforma; el contrato usa solo modelos propios.
        var ensamblado = typeof(IControlDeLimites).Assembly;
        var espacio = typeof(IControlDeLimites).Namespace!;
        var prohibidos = new[] { "BabelHub.Ecommerce.Domain", "BabelHub.Shared" };

        var infracciones = ensamblado.GetExportedTypes()
            .Where(t => t.Namespace == espacio)
            .SelectMany(t => TiposDeLaFirma(t).Select(r => (Tipo: t.Name, Usa: r)))
            .Where(p => p.Usa.Namespace is { } n && prohibidos.Any(x => n.StartsWith(x, StringComparison.Ordinal)))
            .Select(p => $"{p.Tipo} usa {p.Usa.FullName}")
            .Distinct()
            .ToList();

        Assert.Empty(infracciones);
    }

    [Fact]
    public void LasClavesDeLosModulos_CoincidenConLasPoliticasDelDominio()
    {
        Assert.Equal(
            PoliticasDeSeguridad.Todas.Select(p => p.Nombre).Order(),
            PoliticasDeLimite.Todas.Select(c => c.Valor).Order());
    }

    [Fact]
    public void LasClavesGenerales_CoincidenConLasPoliticasGeneralesDelDominio()
    {
        Assert.Equal(
            PoliticasDeSeguridad.Generales.Select(p => p.Nombre).Order(),
            PoliticasDeLimite.Generales.Select(c => c.Valor).Order());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Mayusculas")]
    [InlineData("con espacio")]
    [InlineData("a")]
    [InlineData("1-empieza-con-numero")]
    public void ClaveDePolitica_ConUnValorInvalido_Lanza(string valor)
    {
        Assert.Throws<ArgumentException>(() => new ClaveDePolitica(valor));
    }

    [Fact]
    public void ResultadoDeLimite_Excedido_TraeMotivoEsperaYDisponibilidadSinReserva()
    {
        var resultado = ResultadoDeLimite.Excedido("ingresos-fallidos", 90, Ahora.AddSeconds(90));

        Assert.False(resultado.EsPermitido);
        Assert.Equal(EstadoDeLimite.Excedido, resultado.Estado);
        Assert.Equal(90, resultado.EsperaEnSegundos);
        Assert.Equal(Ahora.AddSeconds(90), resultado.DisponibleDesde);
        Assert.Null(resultado.Reserva);
    }

    [Fact]
    public void ResultadoDeLimite_ConUnaRepeticionOUnConflicto_NoPermiteNiTraeReserva()
    {
        var completada = ResultadoDeLimite.RepeticionCompletada(new ResultadoMinimo("creado", "pedido-9"));

        Assert.False(completada.EsPermitido);
        Assert.Equal("pedido-9", completada.ResultadoGuardado!.IdDelRecurso);
        Assert.Equal(EstadoDeLimite.RepeticionEnCurso, ResultadoDeLimite.RepeticionEnCurso().Estado);
        Assert.Equal(EstadoDeLimite.ClaveEnConflicto, ResultadoDeLimite.ClaveEnConflicto().Estado);
    }

    [Fact]
    public void OrigenDeSolicitud_ConocidoNecesitaDireccionYDesconocidoNoLaTrae()
    {
        var huella = HuellaDeOrigen.Crear("k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE", "v1");

        Assert.Equal("203.0.113.7", new OrigenDeSolicitud(huella, "203.0.113.7").Direccion);
        Assert.Throws<ArgumentException>(() => new OrigenDeSolicitud(huella, null));
        Assert.Throws<ArgumentException>(() => new OrigenDeSolicitud(HuellaDeOrigen.Desconocido, "203.0.113.7"));
        Assert.Null(OrigenDeSolicitud.Desconocido.Direccion);
        Assert.True(OrigenDeSolicitud.Desconocido.Huella.EsDesconocido);
    }

    [Theory]
    [InlineData("identidad")]
    [InlineData("notificaciones")]
    [InlineData("plataforma_latido")]
    public void EsquemaDeModulo_ConUnNombreValido_LoConserva(string nombre)
    {
        Assert.Equal(nombre, EsquemaDeModulo.Crear(nombre).Nombre);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Identidad")]
    [InlineData("identidad; DROP")]
    [InlineData("a")]
    public void EsquemaDeModulo_ConUnNombreInvalido_Lanza(string nombre)
    {
        Assert.Throws<ArgumentException>(() => EsquemaDeModulo.Crear(nombre));
    }

    [Fact]
    public void EsquemaDeModulo_NoSePuedeConstruirFueraDeLaInfraestructura()
    {
        Assert.Empty(typeof(EsquemaDeModulo).GetConstructors());
        Assert.DoesNotContain(typeof(EsquemaDeModulo).GetMethods(), m => m.IsPublic && m.IsStatic && m.ReturnType == typeof(EsquemaDeModulo));
    }

    [Fact]
    public void EventoDeSeguridad_DeLimiteExcedido_ExigePoliticaYOrigenOUsuario()
    {
        var huella = HuellaDeOrigen.Crear("k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE", "v1");

        var porOrigen = new EventoDeSeguridad(TipoDeEventoDeSeguridad.LimiteExcedido, Ahora, "ingresos-fallidos", huella, "203.0.113.7");
        var porUsuario = new EventoDeSeguridad(TipoDeEventoDeSeguridad.LimiteExcedido, Ahora, "ajustes-del-personal", usuario: "u-1");

        Assert.Equal("203.0.113.7", porOrigen.DireccionCompleta);
        Assert.Equal("u-1", porUsuario.Usuario);
        Assert.Throws<ArgumentException>(() => new EventoDeSeguridad(TipoDeEventoDeSeguridad.LimiteExcedido, Ahora, origen: huella));
        Assert.Throws<ArgumentException>(() => new EventoDeSeguridad(TipoDeEventoDeSeguridad.LimiteExcedido, Ahora, "ingresos-fallidos"));
    }

    [Fact]
    public void EventoDeSeguridad_DeOrigenReenviadoSinCredencial_NoConservaLaDireccion()
    {
        Assert.Throws<ArgumentException>(() =>
            new EventoDeSeguridad(TipoDeEventoDeSeguridad.OrigenReenviadoSinCredencial, Ahora, direccionCompleta: "203.0.113.7"));
        Assert.Null(new EventoDeSeguridad(TipoDeEventoDeSeguridad.OrigenReenviadoSinCredencial, Ahora).DireccionCompleta);
    }

    private static IEnumerable<Type> TiposDeLaFirma(Type tipo)
    {
        var encontrados = new List<Type>();
        const BindingFlags Todos = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        void Agregar(Type? t)
        {
            if (t is null)
            {
                return;
            }

            if (t.HasElementType)
            {
                Agregar(t.GetElementType());
                return;
            }

            encontrados.Add(t);
            foreach (var argumento in t.GenericTypeArguments)
            {
                Agregar(argumento);
            }
        }

        Agregar(tipo.BaseType);
        foreach (var i in tipo.GetInterfaces())
        {
            Agregar(i);
        }

        foreach (var p in tipo.GetProperties(Todos))
        {
            Agregar(p.PropertyType);
        }

        foreach (var f in tipo.GetFields(Todos))
        {
            Agregar(f.FieldType);
        }

        foreach (var m in tipo.GetMethods(Todos))
        {
            Agregar(m.ReturnType);
            foreach (var p in m.GetParameters())
            {
                Agregar(p.ParameterType);
            }
        }

        foreach (var c in tipo.GetConstructors(Todos))
        {
            foreach (var p in c.GetParameters())
            {
                Agregar(p.ParameterType);
            }
        }

        return encontrados;
    }
}
