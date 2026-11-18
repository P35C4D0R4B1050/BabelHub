using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Domain.Traslados;
using BabelHub.Inventario.UnitTests.Soporte;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>T09: registrar la guía de un traslado aprobado o en tránsito sin cambiar su estado.</summary>
public sealed class RegistrarGuiaDeTrasladoTests
{
    private static (EntornoDeTraslados Entorno, Guid Edicion) Preparar()
    {
        var entorno = new EntornoDeTraslados(ActorDePrueba.DeAdministradorCentral());
        var edicion = entorno.Impresa();
        entorno.Poner(entorno.Cusco, edicion, 8, 0);
        entorno.Como(RolesDeUsuario.PersonalDeLogistica);
        return (entorno, edicion);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-10")]
    [InlineData(EstadoDeTraslado.Aprobado)]
    [InlineData(EstadoDeTraslado.EnTransito)]
    public async Task LogisticaRegistraLaGuia_ElTrasladoLaMuestra_YSuEstadoNoCambia(EstadoDeTraslado estado)
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, estado);

        var resultado = await entorno.Guia().Ejecutar(new RegistrarGuiaDeTrasladoEntrada(traslado.Id, "CA-771204"));

        Assert.Equal(("CA-771204", estado.Identificador()), (resultado.Valor.Guia, resultado.Valor.Estado));
        var guardado = entorno.Traslados.Almacenado(traslado.Id);
        Assert.Equal((estado, "CA-771204"), (guardado.Estado, guardado.Guia));
        var auditoria = Assert.Single(entorno.Base.Auditoria.Solicitudes);
        Assert.Equal((AccionesAuditables.GuiaDeTrasladoRegistrada, "CA-771204", "false"), (auditoria.Accion, auditoria.Detalle["guiaNueva"], auditoria.Detalle["reemplazoOtraGuia"]));
        Assert.Equal((entorno.Cusco, entorno.Ayacucho), (auditoria.SucursalOrigen!.Value, auditoria.SucursalDestino!.Value));
        Assert.Empty(entorno.Buzon.Mensajes);
        Assert.Empty(entorno.Base.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-10")]
    public async Task UnaGuiaDistintaReemplazaALaAnterior_YLaAuditoriaLoRegistra()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.EnTransito, guia: "CA-1111");

        var resultado = await entorno.Guia().Ejecutar(new RegistrarGuiaDeTrasladoEntrada(traslado.Id, "CA-2222"));

        Assert.Equal("CA-2222", resultado.Valor.Guia);
        Assert.Equal("true", Assert.Single(entorno.Base.Auditoria.Solicitudes).Detalle["reemplazoOtraGuia"]);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-10")]
    public async Task LaAdministracionCentralTambienRegistraLaGuia()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.Aprobado);
        entorno.Como(RolesDeUsuario.AdministradorCentral);

        var resultado = await entorno.Guia().Ejecutar(new RegistrarGuiaDeTrasladoEntrada(traslado.Id, "CA-771204"));

        Assert.True(resultado.EsExito);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-10")]
    [InlineData("origen")]
    [InlineData("destino")]
    [InlineData("cliente")]
    public async Task ElEncargadoYElClienteNoRegistranLaGuia_SeRechazaPorRolAntesDeLeerElTraslado(string quien)
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.Aprobado);
        switch (quien)
        {
            case "origen": entorno.ComoEncargadoDe(entorno.Cusco); break;
            case "destino": entorno.ComoEncargadoDe(entorno.Ayacucho); break;
            default: entorno.Como(RolesDeUsuario.Cliente); break;
        }

        var resultado = await entorno.Guia().Ejecutar(new RegistrarGuiaDeTrasladoEntrada(traslado.Id, "CA-771204"));

        Assert.Equal((CategoriaDeError.Prohibido, AutorizadorDeTraslados.ClaveProhibido), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(["fuera-de-rol"], entorno.Base.MotivosDeDenegacion.ToArray());
        Assert.Equal(0, entorno.Traslados.Lecturas);
        Assert.Null(entorno.Traslados.Almacenado(traslado.Id).Guia);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-10")]
    [InlineData(EstadoDeTraslado.Solicitado)]
    [InlineData(EstadoDeTraslado.Recibido)]
    [InlineData(EstadoDeTraslado.Rechazado)]
    [InlineData(EstadoDeTraslado.Cancelado)]
    public async Task SoloSeRegistraLaGuiaDeUnTrasladoAprobadoOEnTransito(EstadoDeTraslado estado)
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, estado);

        var resultado = await entorno.Guia().Ejecutar(new RegistrarGuiaDeTrasladoEntrada(traslado.Id, "CA-771204"));

        Assert.Equal((CategoriaDeError.Conflicto, ClavesDeTraslados.TrasladoEstadoInvalido), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Null(entorno.Traslados.Almacenado(traslado.Id).Guia);
        Assert.Empty(entorno.Base.Auditoria.Solicitudes);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-10")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    [InlineData("CA 771204")]
    [InlineData("CA-771204; DROP")]
    public async Task LaGuiaSeValida_FormaYLargo(string guia)
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.Aprobado);

        var resultado = await entorno.Guia().Ejecutar(new RegistrarGuiaDeTrasladoEntrada(traslado.Id, guia));

        Assert.Equal((CategoriaDeError.Validacion, "guia"), (resultado.Error!.Categoria, resultado.Error.Errores.Single().Campo));
        Assert.Equal(0, entorno.Traslados.Lecturas);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-10")]
    public async Task UnaGuiaDeMasDe40CaracteresSeRechaza()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.Aprobado);

        var resultado = await entorno.Guia().Ejecutar(new RegistrarGuiaDeTrasladoEntrada(traslado.Id, new string('G', 41)));

        Assert.Equal(ConstructorDeValidacion.DemasiadoLargo, resultado.Error!.Errores.Single().Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task LaGuiaViajaSoloEnLaAuditoria_NuncaEnElBuzonNiEnLosAvisosDeLasTransiciones()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.Aprobado);
        await entorno.Guia().Ejecutar(new RegistrarGuiaDeTrasladoEntrada(traslado.Id, "GUIA-CENTINELA-9"));
        entorno.Como(RolesDeUsuario.AdministradorCentral);

        await entorno.Despachar().Ejecutar(new DespacharTrasladoEntrada(traslado.Id));
        await entorno.Recibir().Ejecutar(new RecibirTrasladoEntrada(traslado.Id));

        Assert.Contains(entorno.Base.Auditoria.Solicitudes, s => s.Detalle.Values.Contains("GUIA-CENTINELA-9"));
        Assert.All(entorno.Buzon.Mensajes, m => Assert.DoesNotContain("CENTINELA", m.Carga, StringComparison.Ordinal));
        Assert.DoesNotContain(entorno.Base.Auditoria.Solicitudes.Where(s => s.Accion != AccionesAuditables.GuiaDeTrasladoRegistrada), s => s.Detalle.Values.Contains("GUIA-CENTINELA-9"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task SiNoSePuedeAuditarLaGuia_LaOperacionFalla_ParaQueSeRevierta()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.Aprobado);
        entorno.Base.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.Guia().Ejecutar(new RegistrarGuiaDeTrasladoEntrada(traslado.Id, "CA-771204")));
    }
}
