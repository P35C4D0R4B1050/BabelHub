using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Fuentes;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Fuentes;

/// <summary>T11: registrar rutas y consultar el plazo (FUE-12, FUE-13, FUE-14, FUE-17): alta o cambio solo por administración central, plazo positivo, auditoría con el anterior y el nuevo.</summary>
public sealed class GestionDeRutasTests
{
    private static RegistrarRuta Registrar(EntornoDeFuentes e) => new(e.Autorizador, new ValidadorDeRegistrarRuta(), e.Fuentes, e.Rutas, e.Auditoria);

    private static ConsultarPlazoDeRuta Consultar(EntornoDeFuentes e) => new(e.Autorizador, new ValidadorDeConsultarPlazo(), e.Lectura, e.Fuentes, e.Rutas);

    private static ListarRutas Listar(EntornoDeFuentes e) => new(e.Autorizador, e.Lectura, e.Fuentes, e.Rutas);

    // --- Registrar ---

    [Fact]
    [Trait("Criterio", "AC-FUE-10")]
    public async Task Registrar_UnaRutaNueva_QuedaGuardadaConSuPlazoYUnaEntradaDeAuditoria()
    {
        var entorno = new EntornoDeFuentes();

        var resultado = await Registrar(entorno).Ejecutar(new RegistrarRutaEntrada("cusco", "ayacucho", 2));

        Assert.Equal(new RutaDefinida("cusco", "ayacucho", 2, null), resultado.Valor);
        var ruta = Assert.Single(entorno.Rutas.Rutas);
        Assert.Equal((entorno.Cusco.Id, entorno.Ayacucho.Id, 2), (ruta.OrigenId, ruta.DestinoId, ruta.PlazoEnDias));

        var solicitud = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.RutaRegistrada, solicitud.Accion);
        Assert.Equal("ruta", solicitud.ObjetoTipo);
        Assert.Equal("cusco:ayacucho", solicitud.ObjetoId);
        Assert.Equal(entorno.Cusco.Id, solicitud.SucursalOrigen);
        Assert.Equal(entorno.Ayacucho.Id, solicitud.SucursalDestino);
        Assert.Equal("2", solicitud.Detalle["plazoNuevoEnDias"]);
        Assert.DoesNotContain("plazoAnteriorEnDias", solicitud.Detalle.Keys);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-10")]
    public async Task Registrar_ElCambioDeDosATresDias_LaRutaMuestraTresYLaAuditoriaRegistraAmbosValores()
    {
        var entorno = new EntornoDeFuentes();
        var caso = Registrar(entorno);
        await caso.Ejecutar(new RegistrarRutaEntrada("cusco", "ayacucho", 2));

        var cambio = await caso.Ejecutar(new RegistrarRutaEntrada("cusco", "ayacucho", 3));

        Assert.Equal(2, cambio.Valor.DiasAnteriores);
        Assert.Equal(3, Assert.Single(entorno.Rutas.Rutas).PlazoEnDias);
        var solicitud = entorno.Auditoria.Solicitudes[^1];
        Assert.Equal(AccionesAuditables.RutaModificada, solicitud.Accion);
        Assert.Equal("2", solicitud.Detalle["plazoAnteriorEnDias"]);
        Assert.Equal("3", solicitud.Detalle["plazoNuevoEnDias"]);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-10")]
    public async Task Registrar_ElMismoPlazoOtraVez_EsIdempotenteYNoAudita()
    {
        var entorno = new EntornoDeFuentes();
        var caso = Registrar(entorno);
        await caso.Ejecutar(new RegistrarRutaEntrada("cusco", "ayacucho", 2));

        var repetido = await caso.Ejecutar(new RegistrarRutaEntrada("cusco", "ayacucho", 2));

        Assert.True(repetido.EsExito);
        Assert.Single(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-10")]
    public async Task Registrar_LaRutaEsDireccional_AyacuchoACuscoEsOtraRuta()
    {
        var entorno = new EntornoDeFuentes();
        var caso = Registrar(entorno);

        await caso.Ejecutar(new RegistrarRutaEntrada("cusco", "ayacucho", 2));
        var inversa = await caso.Ejecutar(new RegistrarRutaEntrada("ayacucho", "cusco", 4));

        Assert.Null(inversa.Valor.DiasAnteriores);
        Assert.Equal(2, entorno.Rutas.Rutas.Count);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-14")]
    [InlineData("cusco", "ayacucho", 0)]
    [InlineData("cusco", "ayacucho", -3)]
    [InlineData("lima", "lima", 2)]
    [InlineData("lima", "lima", 0)]
    public async Task Registrar_ConPlazoCeroONegativoOUnaRutaASiMisma_SeRechazaComoRutaInvalida(string origen, string destino, int dias)
    {
        var entorno = new EntornoDeFuentes();

        var resultado = await Registrar(entorno).Ejecutar(new RegistrarRutaEntrada(origen, destino, dias));

        Assert.Equal(CategoriaDeError.ReglaDeNegocio, resultado.Error!.Categoria);
        Assert.Equal("ruta-invalida", resultado.Error.Clave);
        Assert.Empty(entorno.Rutas.Rutas);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-14")]
    [InlineData("piura", "ayacucho")]
    [InlineData("cusco", "piura")]
    public async Task Registrar_ConUnaFuenteInexistente_EsNoEncontrada(string origen, string destino)
    {
        var entorno = new EntornoDeFuentes();

        var resultado = await Registrar(entorno).Ejecutar(new RegistrarRutaEntrada(origen, destino, 2));

        Assert.Equal(CategoriaDeError.NoEncontrado, resultado.Error!.Categoria);
        Assert.Empty(entorno.Rutas.Rutas);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-14")]
    [MemberData(nameof(GestionDeFuentesTests.ActoresSinPermisoDeAdministracion), MemberType = typeof(GestionDeFuentesTests))]
    public async Task Registrar_ConCualquierOtroRol_ComoUnEncargado_SeRechazaConSuEventoYSinCambios(string etiqueta, ActorDePrueba actor, CategoriaDeError categoria, string motivo)
    {
        var entorno = new EntornoDeFuentes(actor);

        var resultado = await Registrar(entorno).Ejecutar(new RegistrarRutaEntrada("cusco", "ayacucho", 2));

        Assert.Equal(categoria, resultado.Error!.Categoria);
        Assert.Equal([motivo], entorno.MotivosDeDenegacion);
        Assert.Empty(entorno.Rutas.Definidas);
        Assert.Empty(entorno.Auditoria.Solicitudes);
        Assert.NotNull(etiqueta);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-14")]
    public async Task Registrar_ConCodigosInvalidos_DevuelveLosCampos()
    {
        var entorno = new EntornoDeFuentes();

        var resultado = await Registrar(entorno).Ejecutar(new RegistrarRutaEntrada("Cusco", "", 2));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal(["codigoDeOrigen", "codigoDeDestino"], resultado.Error.Errores.Select(e => e.Campo));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-10")]
    public async Task Registrar_SiLaAuditoriaFalla_LaExcepcionSubeParaQueLaTransaccionRevierta()
    {
        var entorno = new EntornoDeFuentes();
        entorno.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => Registrar(entorno).Ejecutar(new RegistrarRutaEntrada("cusco", "ayacucho", 2)));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-10")]
    public async Task Registrar_ConLaAuditoriaReal_ElCatalogoAceptaLasDosAccionesYSusClaves()
    {
        var entorno = new EntornoDeFuentes();
        var (auditoria, almacen) = entorno.AuditoriaReal();
        var caso = new RegistrarRuta(entorno.Autorizador, new ValidadorDeRegistrarRuta(), entorno.Fuentes, entorno.Rutas, auditoria);

        await caso.Ejecutar(new RegistrarRutaEntrada("cusco", "ayacucho", 2));
        await caso.Ejecutar(new RegistrarRutaEntrada("cusco", "ayacucho", 3));

        Assert.Equal(["RutaRegistrada", "RutaModificada"], almacen.Registros.Select(r => r.Accion));
        var cambio = almacen.Registros[1];
        Assert.Equal("2", cambio.Detalle.Valores["plazoAnteriorEnDias"]);
        Assert.Equal("3", cambio.Detalle.Valores["plazoNuevoEnDias"]);
        Assert.Equal("cusco:ayacucho", cambio.ObjetoId);
        Assert.Equal(entorno.Cusco.Id.ToString("D"), cambio.SucursalOrigen);
        Assert.Equal(entorno.Ayacucho.Id.ToString("D"), cambio.SucursalDestino);
    }

    // --- Consultar el plazo ---

    [Fact]
    [Trait("Criterio", "AC-FUE-11")]
    public async Task Consultar_ConRutaDefinidaResponderLosDias_YSinRutaIndicaQueNoEstaDefinida()
    {
        var entorno = new EntornoDeFuentes();
        entorno.Rutas.Rutas.Add(RutaEntreFuentes.Definir(entorno.Cusco.Id, entorno.Ayacucho.Id, 2));

        var definida = await Consultar(entorno).Ejecutar(new ConsultarPlazoDeRutaEntrada("cusco", "ayacucho"));
        var sinDefinir = await Consultar(entorno).Ejecutar(new ConsultarPlazoDeRutaEntrada("lima", "ayacucho"));
        var inversa = await Consultar(entorno).Ejecutar(new ConsultarPlazoDeRutaEntrada("ayacucho", "cusco"));

        Assert.Equal(2, definida.Valor.Dias);
        Assert.Null(sinDefinir.Valor.Dias);
        Assert.True(sinDefinir.EsExito);
        Assert.Null(inversa.Valor.Dias);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-11")]
    [InlineData("piura", "ayacucho")]
    [InlineData("cusco", "piura")]
    public async Task Consultar_ConUnaFuenteInexistente_EsNoEncontrada(string origen, string destino)
    {
        var entorno = new EntornoDeFuentes();

        var resultado = await Consultar(entorno).Ejecutar(new ConsultarPlazoDeRutaEntrada(origen, destino));

        Assert.Equal(CategoriaDeError.NoEncontrado, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-11")]
    public async Task Consultar_LeeEnUnaSolaInstantanea()
    {
        var entorno = new EntornoDeFuentes();

        await Consultar(entorno).Ejecutar(new ConsultarPlazoDeRutaEntrada("cusco", "ayacucho"));

        Assert.Equal(1, entorno.Lectura.Instantaneas);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-11")]
    public async Task Consultar_ElEncargadoYLaAdministracionPuedenLeer_LogisticaYClienteNo()
    {
        foreach (var permitido in new[] { ActorDePrueba.DeEncargadoDe(Guid.NewGuid()), ActorDePrueba.DeAdministradorCentral() })
        {
            var entorno = new EntornoDeFuentes(permitido);
            Assert.True((await Consultar(entorno).Ejecutar(new ConsultarPlazoDeRutaEntrada("cusco", "ayacucho"))).EsExito);
            Assert.Empty(entorno.Denegaciones.Eventos);
        }

        foreach (var rechazado in new[] { ActorDePrueba.DePersonalDeLogistica(), ActorDePrueba.DeCliente(), ActorDePrueba.Anonimo(), ActorDePrueba.SinContexto() })
        {
            var entorno = new EntornoDeFuentes(rechazado);
            var resultado = await Consultar(entorno).Ejecutar(new ConsultarPlazoDeRutaEntrada("cusco", "ayacucho"));
            Assert.False(resultado.EsExito);
            Assert.Single(entorno.Denegaciones.Eventos);
            Assert.Equal(0, entorno.Lectura.Instantaneas);
        }
    }

    // --- Listar ---

    [Fact]
    [Trait("Criterio", "AC-FUE-11")]
    public async Task Listar_DevuelveLasRutasOrdenadasPorOrigenYDestino_ConSusCodigos()
    {
        var entorno = new EntornoDeFuentes();
        entorno.Rutas.Rutas.Add(RutaEntreFuentes.Definir(entorno.Cusco.Id, entorno.Lima.Id, 1));
        entorno.Rutas.Rutas.Add(RutaEntreFuentes.Definir(entorno.Ayacucho.Id, entorno.Cusco.Id, 4));
        entorno.Rutas.Rutas.Add(RutaEntreFuentes.Definir(entorno.Cusco.Id, entorno.Ayacucho.Id, 2));

        var resultado = await Listar(entorno).Ejecutar(new ListarRutasEntrada());

        Assert.Equal(
            [new RutaListada("ayacucho", "cusco", 4), new RutaListada("cusco", "ayacucho", 2), new RutaListada("cusco", "lima", 1)],
            resultado.Valor);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-14")]
    [InlineData("logistica")]
    [InlineData("cliente")]
    public async Task Listar_LogisticaYCliente_SeRechazanConSuEvento(string rol)
    {
        var entorno = new EntornoDeFuentes(rol == "cliente" ? ActorDePrueba.DeCliente() : ActorDePrueba.DePersonalDeLogistica());

        var resultado = await Listar(entorno).Ejecutar(new ListarRutasEntrada());

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(["fuera-de-rol"], entorno.MotivosDeDenegacion);
    }
}
