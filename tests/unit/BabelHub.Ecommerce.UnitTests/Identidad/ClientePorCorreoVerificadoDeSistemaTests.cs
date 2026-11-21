using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// La cuenta de un cliente por su correo verificado para el Libro de Reclamaciones (identidad-seguridad T60; AC-IDE-73, IDE-99): solo el cliente activo, no cancelado y con el correo verificado devuelve su
/// identificador; el personal, una cuenta sin verificar, cancelada o desactivada y un correo sin cuenta no devuelven nada, y la consulta no deja el correo ni en la auditoría ni en los eventos.
/// </summary>
public sealed class ClientePorCorreoVerificadoDeSistemaTests
{
    private const string Correo = "ana@example.com";

    private static ClientePorCorreoVerificadoDeSistemaDeIdentidad Consulta(EntornoDeIdentidad e) => new(e.Cuentas);

    [Fact]
    [Trait("Criterio", "AC-IDE-73")]
    public async Task UnClienteVerificadoActivoYSinCancelar_DevuelveSuIdentificador_AunqueElCorreoLleguePorOtraEscritura()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta(Correo);

        var porNormalizado = await Consulta(e).Consultar(Correo);
        var porOtraEscritura = await Consulta(e).Consultar("  ANA@Example.com ");

        Assert.Equal(cuenta.Usuario.Id, porNormalizado);
        Assert.Equal(cuenta.Usuario.Id, porOtraEscritura);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-73")]
    public async Task UnClienteSinVerificar_NoDevuelveNada()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta(Correo, verificada: false);

        Assert.Null(await Consulta(e).Consultar(Correo));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-73")]
    public async Task UnMiembroDelPersonal_NoDevuelveNada()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta(Correo, RolDeUsuario.Logistica);

        Assert.Null(await Consulta(e).Consultar(Correo));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-73")]
    public async Task UnaCuentaCancelada_NoDevuelveNada()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta(Correo);
        cuenta.CanceladaEn = EntornoDeIdentidad.Inicio;

        Assert.Null(await Consulta(e).Consultar(Correo));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-73")]
    public async Task UnaCuentaDesactivada_NoDevuelveNada()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta(Correo, activa: false);

        Assert.Null(await Consulta(e).Consultar(Correo));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-73")]
    public async Task UnCorreoSinCuenta_NoDevuelveNada_YLaConsultaNoDejaElCorreoEnLaAuditoriaNiEnLosEventos()
    {
        var e = new EntornoDeIdentidad();

        Assert.Null(await Consulta(e).Consultar("nadie@example.com"));
        Assert.Empty(e.Auditoria.Registradas);
        Assert.Empty(e.Seguridad.Eventos);
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-73")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SinCorreo_LaConsultaFalla_YNoSeConfundeConSinCuenta(string correo)
    {
        var e = new EntornoDeIdentidad();

        await Assert.ThrowsAsync<ArgumentException>(() => Consulta(e).Consultar(correo));
    }
}
