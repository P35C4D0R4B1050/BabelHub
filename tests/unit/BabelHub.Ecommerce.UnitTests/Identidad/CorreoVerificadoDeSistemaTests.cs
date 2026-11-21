using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// El correo verificado como contrato <b>de sistema</b> de Identidad (hallazgo de ECO-11c/11d; IDE-84, PLA-19): lo lee cualquier actor, incluso ninguno, porque no comprueba titularidad; solo entrega un correo <b>verificado</b>
/// y normalizado; y si no puede leer, lanza (nunca dice «sin correo» por no saber).
/// </summary>
public sealed class CorreoVerificadoDeSistemaTests
{
    [Fact]
    [Trait("Criterio", "AC-IDE-63")]
    public async Task UnaCuentaConElCorreoVerificado_EntregaElCorreoNormalizado_ConCualquierActorOSinActor()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("Ana@Example.com");
        var puerto = new CorreoVerificadoDeSistemaDeIdentidad(e.Cuentas);

        e.Actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), "cliente"); // otra persona (un visitante que reemplaza una cuenta, IDE-84)
        var comoOtro = await puerto.Consultar(cuenta.Usuario.Id);
        e.Actor.Actual = ActorActual.DelSistema(); // ningún usuario (la reaplicación tras restaurar, PLA-19)
        var sinUsuario = await puerto.Consultar(cuenta.Usuario.Id);

        Assert.Equal("ana@example.com", comoOtro);
        Assert.Equal("ana@example.com", sinUsuario);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-63")]
    public async Task UnaCuentaSinVerificarOInexistente_NoTieneCorreoVerificado()
    {
        var e = new EntornoDeIdentidad();
        var sinVerificar = e.Cuenta("sin@example.com", verificada: false);
        var puerto = new CorreoVerificadoDeSistemaDeIdentidad(e.Cuentas);

        Assert.Null(await puerto.Consultar(sinVerificar.Usuario.Id));
        Assert.Null(await puerto.Consultar(Guid.NewGuid()));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-63")]
    public async Task UnaCuentaCancelada_NoEntregaSuCorreo_AunqueSigaMarcadaComoVerificada()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("ana@example.com");
        cuenta.CanceladaEn = e.Reloj.Ahora;

        Assert.Null(await new CorreoVerificadoDeSistemaDeIdentidad(e.Cuentas).Consultar(cuenta.Usuario.Id));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-63")]
    public async Task SinLaCuenta_SeRechazaLaConsulta_YSoloLee()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("ana@example.com");
        var puerto = new CorreoVerificadoDeSistemaDeIdentidad(e.Cuentas);
        var antes = cuenta.Usuario.Correo;

        await Assert.ThrowsAsync<ArgumentException>(() => puerto.Consultar(Guid.Empty));
        await puerto.Consultar(cuenta.Usuario.Id);

        Assert.Equal(antes, e.Bd.Cuentas[cuenta.Usuario.Id].Usuario.Correo);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-63")]
    public async Task SiElRepositorioNoPuedeLeer_LaExcepcionSube_YNuncaSeDiceSinCorreo()
    {
        var repositorio = System.Reflection.DispatchProxy.Create<IRepositorioDeCuentas, RepositorioQueFalla>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new CorreoVerificadoDeSistemaDeIdentidad(repositorio).Consultar(Guid.NewGuid()));
    }

    /// <summary>Un repositorio caído: cualquier lectura lanza.</summary>
    public class RepositorioQueFalla : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) => throw new InvalidOperationException("La base no responde.");
    }
}
