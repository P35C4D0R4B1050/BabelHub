using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Fuentes;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Fuentes;

/// <summary>T10: designar la fuente de recepción (FUE-19, FUE-20): un único registro tras el cambio, bajo el bloqueo de recepción, con su auditoría en la misma transacción.</summary>
public sealed class RecepcionDeFuentesTests
{
    private static EntornoDeFuentes ConArequipa(out Fuente arequipa, ActorDePrueba? actor = null)
    {
        var entorno = new EntornoDeFuentes(actor);
        arequipa = entorno.Fuentes.Agregar("arequipa", "arequipa");
        return entorno;
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public async Task Designar_OtraFuente_DejaUnUnicoRegistroDeRecepcion_ConSuAuditoria()
    {
        var entorno = ConArequipa(out var arequipa);

        var resultado = await entorno.DesignarRecepcion().Ejecutar(new DesignarFuenteDeRecepcionEntrada("arequipa"));

        Assert.True(resultado.EsExito);
        Assert.Equal(new RecepcionDesignada("arequipa", Cambio: true), resultado.Valor);
        Assert.True(ReglaDeRecepcion.CumpleElInvariante(entorno.Fuentes.Fuentes));
        Assert.Equal(arequipa.Id, Assert.Single(entorno.Fuentes.Fuentes, f => f.EsRecepcion).Id);

        var solicitud = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.RecepcionDesignada, solicitud.Accion);
        Assert.Equal(arequipa.Id.ToString("D"), solicitud.ObjetoId);
        Assert.Equal(entorno.Lima.Id.ToString("D"), solicitud.Detalle["fuenteAnteriorId"]);
        Assert.Equal(arequipa.Id.ToString("D"), solicitud.Detalle["fuenteNuevaId"]);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public async Task Designar_TomaElBloqueoDeRecepcion_ElCambioSeAudita()
    {
        var entorno = ConArequipa(out _);

        await entorno.DesignarRecepcion().Ejecutar(new DesignarFuenteDeRecepcionEntrada("arequipa"));

        Assert.Equal(["bloqueo.Recepcion", "auditoria.RecepcionDesignada"], entorno.Bitacora.Llamadas.Where(l => l is "bloqueo.Recepcion" or "auditoria.RecepcionDesignada"));
        Assert.Equal(1, entorno.Bloqueos.Recepciones);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public async Task Designar_DosVeces_ElSegundoCambioVeLaRecepcionQueDejoElPrimero()
    {
        var entorno = ConArequipa(out var arequipa);
        var caso = entorno.DesignarRecepcion();

        await caso.Ejecutar(new DesignarFuenteDeRecepcionEntrada("arequipa"));
        var segundo = await caso.Ejecutar(new DesignarFuenteDeRecepcionEntrada("cusco"));

        Assert.True(segundo.Valor.Cambio);
        Assert.True(ReglaDeRecepcion.CumpleElInvariante(entorno.Fuentes.Fuentes));
        Assert.Equal("cusco", Assert.Single(entorno.Fuentes.Fuentes, f => f.EsRecepcion).Codigo);
        Assert.Equal(2, entorno.Auditoria.Solicitudes.Count);
        Assert.Equal(arequipa.Id.ToString("D"), entorno.Auditoria.Solicitudes[^1].Detalle["fuenteAnteriorId"]);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public async Task Designar_LaQueYaEsLaDeRecepcion_NoCambiaNiAudita()
    {
        var entorno = ConArequipa(out _);

        var resultado = await entorno.DesignarRecepcion().Ejecutar(new DesignarFuenteDeRecepcionEntrada("lima"));

        Assert.False(resultado.Valor.Cambio);
        Assert.Empty(entorno.Auditoria.Solicitudes);
        Assert.Equal("lima", Assert.Single(entorno.Fuentes.Fuentes, f => f.EsRecepcion).Codigo);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-16")]
    [MemberData(nameof(GestionDeFuentesTests.ActoresSinPermisoDeAdministracion), MemberType = typeof(GestionDeFuentesTests))]
    public async Task Designar_ConCualquierOtroRol_SeRechazaConSuEvento_YLaRecepcionSigueDondeEstaba(string etiqueta, ActorDePrueba actor, CategoriaDeError categoria, string motivo)
    {
        var entorno = ConArequipa(out _, actor);

        var resultado = await entorno.DesignarRecepcion().Ejecutar(new DesignarFuenteDeRecepcionEntrada("arequipa"));

        Assert.Equal(categoria, resultado.Error!.Categoria);
        Assert.Equal([motivo], entorno.MotivosDeDenegacion);
        Assert.Equal("lima", Assert.Single(entorno.Fuentes.Fuentes, f => f.EsRecepcion).Codigo);
        Assert.Equal(0, entorno.Bloqueos.Recepciones);
        Assert.Empty(entorno.Auditoria.Solicitudes);
        Assert.NotNull(etiqueta);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public async Task Designar_UnaFuenteInexistenteOUnCodigoInvalido_NoCambiaNada()
    {
        var entorno = ConArequipa(out _);

        var inexistente = await entorno.DesignarRecepcion().Ejecutar(new DesignarFuenteDeRecepcionEntrada("piura"));
        var invalido = await entorno.DesignarRecepcion().Ejecutar(new DesignarFuenteDeRecepcionEntrada("Piura!"));

        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, invalido.Error!.Categoria);
        Assert.Equal("lima", Assert.Single(entorno.Fuentes.Fuentes, f => f.EsRecepcion).Codigo);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public async Task Designar_SiElBloqueoSeAgota_NoCambiaNadaYEsNoDisponible()
    {
        var entorno = ConArequipa(out _);
        entorno.Bloqueos.EsperaAgotada = true;

        var resultado = await entorno.DesignarRecepcion().Ejecutar(new DesignarFuenteDeRecepcionEntrada("arequipa"));

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal("lima", Assert.Single(entorno.Fuentes.Fuentes, f => f.EsRecepcion).Codigo);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public async Task Designar_SiLaAuditoriaFalla_LaExcepcionSubeParaQueLaTransaccionRevierta()
    {
        var entorno = ConArequipa(out _);
        entorno.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.DesignarRecepcion().Ejecutar(new DesignarFuenteDeRecepcionEntrada("arequipa")));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public async Task Designar_ConLaAuditoriaReal_ElCatalogoAceptaLaAccionYLosIdentificadores()
    {
        var entorno = ConArequipa(out var arequipa);
        var (auditoria, almacen) = entorno.AuditoriaReal();
        var caso = new DesignarFuenteDeRecepcion(entorno.Autorizador, new ValidadorDeDesignarRecepcion(), entorno.Bloqueos, entorno.Fuentes, auditoria);

        await caso.Ejecutar(new DesignarFuenteDeRecepcionEntrada("arequipa"));

        var registro = Assert.Single(almacen.Registros);
        Assert.Equal("RecepcionDesignada", registro.Accion);
        Assert.Equal(arequipa.Id.ToString("D"), registro.Detalle.Valores["fuenteNuevaId"]);
        Assert.Equal(entorno.Lima.Id.ToString("D"), registro.Detalle.Valores["fuenteAnteriorId"]);
    }
}
