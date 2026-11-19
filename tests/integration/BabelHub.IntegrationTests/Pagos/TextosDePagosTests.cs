using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Infrastructure.Pagos;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// T24: el esquema <c>pagos</c> y el dominio no se desfasan (sin base de datos): cada miembro de las listas cerradas que el esquema guarda como texto tiene su valor estable en el <c>CHECK</c> de la tabla, y la conversión es de ida y vuelta.
/// Si se añade un miembro al dominio sin añadirlo al esquema, esta prueba falla antes de que lo haga una escritura en producción.
/// </summary>
public sealed class TextosDePagosTests
{
    public static TheoryData<Type> Listas => new()
    {
        typeof(EstadoDeIntencion),
        typeof(MotivoDeCierre),
        typeof(TipoDeEntrega),
        typeof(MedioDePago),
        typeof(EstadoDeIntento),
        typeof(MotivoDeRechazo),
        typeof(MotivoDeCompensacion),
        typeof(VarianteDeCompensacion),
        typeof(MotivoDeLiberacion),
        typeof(OrigenDeReembolso),
        typeof(EstadoDeReembolso),
        typeof(MotivoPendienteDeReembolso),
        typeof(TransicionDeAviso),
        typeof(EstadoDeAvisoACarrito),
        typeof(EventoDeAvisoDePago),
        typeof(EstadoDeAvisoDePago),
    };

    [Theory]
    [MemberData(nameof(Listas))]
    public void Cada_miembro_de_una_lista_cerrada_tiene_su_texto_en_el_esquema_y_vuelve_igual(Type lista)
    {
        var metodoA = typeof(TextosDePagos).GetMethods().Single(m => m.Name == "A" && !m.GetParameters()[0].ParameterType.IsGenericType && m.IsGenericMethodDefinition && m.GetParameters()[0].ParameterType.IsGenericParameter);
        var metodoDe = typeof(TextosDePagos).GetMethod("De")!;

        foreach (var miembro in Enum.GetValues(lista))
        {
            var texto = (string)metodoA.MakeGenericMethod(lista).Invoke(null, [miembro])!;

            Assert.Contains($"'{texto}'", DefinicionDePagos.Sql, StringComparison.Ordinal);
            Assert.Equal(miembro, metodoDe.MakeGenericMethod(lista).Invoke(null, [texto]));
        }
    }

    [Fact]
    public void Un_texto_desconocido_no_se_adivina()
    {
        Assert.Throws<InvalidOperationException>(() => TextosDePagos.De<EstadoDeIntencion>("inventado"));
    }
}
