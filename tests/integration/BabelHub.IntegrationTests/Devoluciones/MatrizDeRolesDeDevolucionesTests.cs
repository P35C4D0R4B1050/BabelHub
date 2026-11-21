using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Contratos;

namespace BabelHub.IntegrationTests.Devoluciones;

/// <summary>
/// T32 (AC-DEV-12, AC-DEV-23, AC-DEV-30): la matriz rol × operación de la Aplicación real sobre PostgreSQL real. <c>ECO-PROHIBIDO</c> solo por rol; una devolución ajena, de otra sucursal receptora o con una sucursal que no se resuelve responde lo mismo que una inexistente
/// (<c>no-encontrado</c>) y registra un acceso denegado por <b>cada</b> intento; el encargado de la sucursal receptora y la administración central sí operan.
/// </summary>
[Collection(ColeccionPostgresDeDevoluciones.Nombre)]
public sealed class MatrizDeRolesDeDevolucionesTests(PostgresDeDevolucionesFixture bd) : IAsyncLifetime
{
    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static ActorActual Encargado(string sucursal) => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, IdentificadorDeSucursal.DesdeCodigo(sucursal));

    public static TheoryData<string, string?> Casos => new()
    {
        { "cliente-duenio", "prohibido" },
        { "cliente-ajeno", "prohibido" },
        { "encargado-de-otra-sucursal", "no-encontrado" },
        { "encargado-sin-sucursal", "no-encontrado" },
        { "logistica", "prohibido" },
        { "anonimo", "no-autenticado" },
        { "encargado-de-la-sucursal-receptora", null },
        { "administracion-central", null },
    };

    [Theory]
    [MemberData(nameof(Casos))]
    [Trait("Criterio", "AC-DEV-12")]
    [Trait("Criterio", "AC-DEV-30")]
    public async Task Aprobar_un_impreso_responde_segun_el_rol_y_el_alcance_y_registra_una_denegacion_por_intento(string quien, string? errorEsperado)
    {
        var e = new EscenarioDeDevolucionesConPostgres(bd);
        var (cliente, pedido, linea) = e.SembrarPedidoDeImpresos();
        var devolucionId = (await e.Solicitar(cliente, pedido.PedidoId, linea.LineaId, 1)).Valor.DevolucionId;
        var actor = quien switch
        {
            "cliente-duenio" => ActorActual.DeUsuario(cliente, RolesDeUsuario.Cliente),
            "cliente-ajeno" => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.Cliente),
            "encargado-de-otra-sucursal" => Encargado("arequipa"),
            "encargado-sin-sucursal" => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, null),
            "logistica" => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.PersonalDeLogistica),
            "anonimo" => ActorActual.Anonimo(),
            "encargado-de-la-sucursal-receptora" => Encargado("lima-central"),
            _ => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.AdministradorCentral),
        };

        var resultado = await e.AprobarComo(actor, devolucionId);

        if (errorEsperado is null)
        {
            Assert.True(resultado.EsExito, resultado.Error?.Clave);
            Assert.Empty(e.Denegaciones.Registradas);
            return;
        }

        Assert.False(resultado.EsExito);
        Assert.Equal(errorEsperado, resultado.Error!.Clave);
        Assert.Equal("solicitada", await bd.Consultar<string>($"SELECT estado FROM devoluciones.lineas_de_devolucion WHERE devolucion_id = '{devolucionId:D}'"));
        if (errorEsperado == "no-encontrado")
        {
            Assert.Single(e.Denegaciones.Registradas);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-30")]
    public async Task Una_devolucion_inexistente_responde_igual_que_una_de_otra_sucursal()
    {
        var e = new EscenarioDeDevolucionesConPostgres(bd);
        var (cliente, pedido, linea) = e.SembrarPedidoDeImpresos();
        var existente = (await e.Solicitar(cliente, pedido.PedidoId, linea.LineaId, 1)).Valor.DevolucionId;
        var ajeno = Encargado("arequipa");

        var deOtraSucursal = await e.AprobarComo(ajeno, existente);
        var inexistente = await e.AprobarComo(ajeno, Guid.NewGuid());

        Assert.Equal(deOtraSucursal.Error!.Clave, inexistente.Error!.Clave);
        Assert.Equal(deOtraSucursal.Error.Categoria, inexistente.Error.Categoria);
        Assert.Equal(2, e.Denegaciones.Registradas.Count);
    }
}
