using BabelHub.Ecommerce.Application.Identidad.Interno;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// El contacto de avisos que Identidad le entrega a Notificaciones justo antes de enviar (transversal-notificaciones T08; AC-NOT-02, AC-NOT-07, NOT-03, NOT-08): consentimiento y número vigentes, cuenta cancelada o
/// desactivada, cuenta inexistente; nunca se imprime el número.
/// </summary>
public sealed class ContactoDeAvisosDeIdentidadTests
{
    [Fact]
    [Trait("Criterio", "AC-NOT-02")]
    public async Task UnaCuentaConNumeroYConsentimiento_DevuelveElNumeroEnE164()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("ana@example.com");
        Assert.True(cuenta.Whatsapp.GuardarNumero("+51987654321", e.Reloj.Ahora));
        Assert.True(cuenta.Whatsapp.Consentir("+51987654321", e.Reloj.Ahora));

        var contacto = await new ContactoDeAvisosDeIdentidad(e.Cuentas).Consultar(cuenta.Usuario.Id);

        Assert.NotNull(contacto);
        Assert.True(contacto.Consiente);
        Assert.False(contacto.CuentaCanceladaODesactivada);
        Assert.Equal("+51987654321", contacto.NumeroE164);
        Assert.DoesNotContain("987654321", contacto.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-07")]
    public async Task ConElNumeroCambiadoORevocado_NoConsiente_YSinNumero_NoHayNumero()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("ana@example.com");
        cuenta.Whatsapp.GuardarNumero("+51987654321", e.Reloj.Ahora);
        cuenta.Whatsapp.Consentir("+51987654321", e.Reloj.Ahora);
        var adaptador = new ContactoDeAvisosDeIdentidad(e.Cuentas);

        cuenta.Whatsapp.GuardarNumero("+51911111111", e.Reloj.Ahora);
        var cambiado = await adaptador.Consultar(cuenta.Usuario.Id);
        cuenta.Whatsapp.Consentir("+51911111111", e.Reloj.Ahora);
        cuenta.Whatsapp.Revocar(e.Reloj.Ahora);
        var revocado = await adaptador.Consultar(cuenta.Usuario.Id);
        var sinNumero = await adaptador.Consultar(e.Cuenta("sin@example.com").Usuario.Id);

        Assert.False(cambiado!.Consiente);
        Assert.Equal("+51911111111", cambiado.NumeroE164);
        Assert.False(revocado!.Consiente);
        Assert.False(sinNumero!.Consiente);
        Assert.Null(sinNumero.NumeroE164);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-07")]
    public async Task UnaCuentaDesactivada_SeMarcaComoCanceladaODesactivada()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("ana@example.com");
        cuenta.Whatsapp.GuardarNumero("+51987654321", e.Reloj.Ahora);
        cuenta.Whatsapp.Consentir("+51987654321", e.Reloj.Ahora);
        cuenta.Usuario.Desactivar();

        var contacto = await new ContactoDeAvisosDeIdentidad(e.Cuentas).Consultar(cuenta.Usuario.Id);

        Assert.True(contacto!.CuentaCanceladaODesactivada);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-07")]
    public async Task UnaCuentaInexistenteOUnIdentificadorVacio_NoTienenContacto()
    {
        var e = new EntornoDeIdentidad();
        var adaptador = new ContactoDeAvisosDeIdentidad(e.Cuentas);

        Assert.Null(await adaptador.Consultar(Guid.NewGuid()));
        Assert.Null(await adaptador.Consultar(Guid.Empty));
    }
}
