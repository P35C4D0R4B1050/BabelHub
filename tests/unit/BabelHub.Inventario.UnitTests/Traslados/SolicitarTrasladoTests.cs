using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Domain.Traslados;
using BabelHub.Inventario.UnitTests.Soporte;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>T05: <c>SolicitarTraslado</c> y <c>ConsultarOrigenesPosibles</c> sin base de datos, con el stock real en memoria.</summary>
public sealed class SolicitarTrasladoTests
{
    private const string Centinela = "MOTIVO-CENTINELA-123";

    private static (EntornoDeTraslados Entorno, Guid Edicion) ComoEncargadoDeAyacucho()
    {
        var entorno = new EntornoDeTraslados(ActorDePrueba.DeAdministradorCentral());
        entorno.ComoEncargadoDe(entorno.Ayacucho);
        var edicion = entorno.Impresa();
        entorno.Poner(entorno.Lima, edicion, 5, 0);
        entorno.Poner(entorno.Cusco, edicion, 8, 2);
        entorno.Poner(entorno.Ayacucho, edicion, 0, 0);
        return (entorno, edicion);
    }

    private static SolicitarTrasladoEntrada Solicitud(Guid edicion, int cantidad = 3, string origen = "cusco", string destino = "ayacucho", string? motivo = "reposición") => new(edicion, cantidad, origen, destino, motivo);

    // ---------- AC-TRA-01: orígenes posibles y solicitud ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-01")]
    public async Task ElEncargadoDeAyacuchoSoloPuedeElegirComoOrigenLasFuentesConVendible_SinVerCantidades()
    {
        var (entorno, edicion) = ComoEncargadoDeAyacucho();
        entorno.Poner(entorno.Lima, edicion, 0, 0);

        var sinLima = (await entorno.Origenes().Ejecutar(new ConsultarOrigenesPosiblesEntrada(edicion, "ayacucho"))).Valor;
        entorno.Poner(entorno.Lima, edicion, 5, 0);
        var conLima = (await entorno.Origenes().Ejecutar(new ConsultarOrigenesPosiblesEntrada(edicion, "ayacucho"))).Valor;

        Assert.Equal(["cusco"], sinLima.Origenes.Select(o => o.Codigo).ToArray());
        Assert.Equal(["cusco", "lima"], conLima.Origenes.Select(o => o.Codigo).ToArray());
        Assert.All(conLima.Origenes, o => Assert.Equal(2, o.GetType().GetProperties().Length));
        Assert.DoesNotContain(conLima.Origenes, o => o.Codigo == "ayacucho");
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-01")]
    public async Task LosOrigenesPosiblesVanPorRutaMasCortaYLuegoPorCodigo_ConLasSinRutaAlFinal()
    {
        var (entorno, edicion) = ComoEncargadoDeAyacucho();
        entorno.Base.Rutas.Rutas.Add(Domain.Fuentes.RutaEntreFuentes.Definir(entorno.Lima, entorno.Ayacucho, 1));
        entorno.Base.Rutas.Rutas.Add(Domain.Fuentes.RutaEntreFuentes.Definir(entorno.Cusco, entorno.Ayacucho, 4));

        var porRuta = (await entorno.Origenes().Ejecutar(new ConsultarOrigenesPosiblesEntrada(edicion, "ayacucho"))).Valor;

        Assert.Equal(["lima", "cusco"], porRuta.Origenes.Select(o => o.Codigo).ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-01")]
    public async Task LosOrigenesPosiblesDeUnaEdicionDigitalOInexistenteSeRechazan_YLosDeOtroDestinoSonProhibidosParaElEncargado()
    {
        var (entorno, edicion) = ComoEncargadoDeAyacucho();

        var digital = await entorno.Origenes().Ejecutar(new ConsultarOrigenesPosiblesEntrada(entorno.Digital(), "ayacucho"));
        var inexistente = await entorno.Origenes().Ejecutar(new ConsultarOrigenesPosiblesEntrada(Guid.NewGuid(), "ayacucho"));
        var ajeno = await entorno.Origenes().Ejecutar(new ConsultarOrigenesPosiblesEntrada(edicion, "cusco"));
        var inventado = await entorno.Origenes().Ejecutar(new ConsultarOrigenesPosiblesEntrada(edicion, "inexistente"));

        Assert.Equal((CategoriaDeError.ReglaDeNegocio, ClavesDeTraslados.EdicionNoImpresa), (digital.Error!.Categoria, digital.Error.Clave));
        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, ajeno.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, inventado.Error!.Categoria);
        Assert.Equal(["fuera-de-sucursal", "fuera-de-sucursal"], entorno.Base.MotivosDeDenegacion.ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-01")]
    public async Task LaSolicitudNaceSolicitada_ConSuNumero_SuResponsable_YAuditaYAvisaALasDosSucursales()
    {
        var (entorno, edicion) = ComoEncargadoDeAyacucho();

        var resultado = await entorno.Solicitar().Ejecutar(Solicitud(edicion, 3, motivo: Centinela));

        var traslado = resultado.Valor;
        Assert.Equal(("solicitado", 1L, "cusco", "ayacucho", 3, false, 0L), (traslado.Estado, traslado.Numero, traslado.CodigoDeOrigen, traslado.CodigoDeDestino, traslado.Cantidad, traslado.EsVinculado, traslado.Version));
        Assert.Equal(entorno.Actor.Actual!.UsuarioId, traslado.SolicitadoPor);
        var guardado = entorno.Traslados.Almacenado(traslado.TrasladoId);
        Assert.Equal((EstadoDeTraslado.Solicitado, Centinela), (guardado.Estado, guardado.Motivo));
        var auditoria = Assert.Single(entorno.Base.Auditoria.Solicitudes);
        Assert.Equal((AccionesAuditables.TrasladoSolicitado, entorno.Cusco, entorno.Ayacucho), (auditoria.Accion, auditoria.SucursalOrigen, auditoria.SucursalDestino));
        Assert.DoesNotContain(auditoria.Detalle.Values, v => v?.Contains("CENTINELA", StringComparison.Ordinal) == true);
        Assert.Equal(["aviso", "aviso"], entorno.Buzon.Mensajes.Select(m => m.Tipo).ToArray());
        Assert.Equal(["ayacucho", "cusco"], entorno.Buzon.Mensajes.Select(m => m.SucursalCodigo).Order().ToArray());
        Assert.All(entorno.Buzon.Mensajes, m => Assert.DoesNotContain("CENTINELA", m.Carga, StringComparison.Ordinal));
        Assert.Equal((8, 2), entorno.Stock(entorno.Cusco, edicion));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-01")]
    public async Task LaAdministracionCentralSolicitaHaciaCualquierDestino()
    {
        var (entorno, edicion) = ComoEncargadoDeAyacucho();
        entorno.Como(RolesDeUsuario.AdministradorCentral);

        var resultado = await entorno.Solicitar().Ejecutar(Solicitud(edicion, 2, origen: "lima", destino: "cusco"));

        Assert.True(resultado.EsExito);
        Assert.Empty(entorno.Base.Denegaciones.Eventos);
    }

    // ---------- AC-TRA-19: las cinco solicitudes inválidas ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-19")]
    public async Task LasCincoSolicitudesInvalidasSeRechazan_SinGuardarNadaYSinCifras()
    {
        var (entorno, edicion) = ComoEncargadoDeAyacucho();
        entorno.Poner(entorno.Ayacucho, edicion, 4, 0);
        var digital = entorno.Digital();

        var mismoOrigenYDestino = await entorno.Solicitar().Ejecutar(Solicitud(edicion, 1, origen: "ayacucho"));
        var cantidadCero = await entorno.Solicitar().Ejecutar(Solicitud(edicion, 0));
        var edicionDigital = await entorno.Solicitar().Ejecutar(Solicitud(digital, 1));
        var mayorQueElVendible = await entorno.Solicitar().Ejecutar(Solicitud(edicion, 7));
        var haciaOtraSucursal = await entorno.Solicitar().Ejecutar(Solicitud(edicion, 1, origen: "lima", destino: "cusco"));

        Assert.Equal((CategoriaDeError.Validacion, "codigoDeDestino"), (mismoOrigenYDestino.Error!.Categoria, mismoOrigenYDestino.Error.Errores.Single().Campo));
        Assert.Equal((CategoriaDeError.Validacion, "cantidad"), (cantidadCero.Error!.Categoria, cantidadCero.Error.Errores.Single().Campo));
        Assert.Equal((CategoriaDeError.ReglaDeNegocio, ClavesDeTraslados.EdicionNoImpresa), (edicionDigital.Error!.Categoria, edicionDigital.Error.Clave));
        Assert.Equal((CategoriaDeError.Conflicto, ClavesDeTraslados.StockInsuficiente), (mayorQueElVendible.Error!.Categoria, mayorQueElVendible.Error.Clave));
        Assert.Equal(CategoriaDeError.Prohibido, haciaOtraSucursal.Error!.Categoria);
        Assert.Empty(mayorQueElVendible.Error.Errores);
        Assert.Empty(entorno.Traslados.Todos);
        Assert.Empty(entorno.Buzon.Mensajes);
        Assert.Empty(entorno.Base.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-19")]
    public async Task UnDestinoAjenoDelEncargadoEsUnaViolacionDeAlcance_ConUnUnicoEventoPorIntento()
    {
        var (entorno, edicion) = ComoEncargadoDeAyacucho();

        await entorno.Solicitar().Ejecutar(Solicitud(edicion, 1, origen: "lima", destino: "cusco"));
        await entorno.Solicitar().Ejecutar(Solicitud(edicion, 1, origen: "lima", destino: "inexistente"));

        Assert.Equal(["fuera-de-sucursal", "fuera-de-sucursal"], entorno.Base.MotivosDeDenegacion.ToArray());
        Assert.All(entorno.Base.Denegaciones.Eventos, e => Assert.Equal("INV-PROHIBIDO", e.CodigoDeError));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-19")]
    public async Task LaFuenteDeOrigenInexistenteSeResuelveDespuesDelAlcance_ConLaAdministracionCentral()
    {
        var (entorno, edicion) = ComoEncargadoDeAyacucho();
        entorno.Como(RolesDeUsuario.AdministradorCentral);

        var origenInexistente = await entorno.Solicitar().Ejecutar(Solicitud(edicion, 1, origen: "inexistente"));
        var destinoInexistente = await entorno.Solicitar().Ejecutar(Solicitud(edicion, 1, destino: "inexistente"));
        var edicionInexistente = await entorno.Solicitar().Ejecutar(Solicitud(Guid.NewGuid(), 1));

        Assert.All([origenInexistente, destinoInexistente], r => Assert.Equal((CategoriaDeError.NoEncontrado, ClavesDeTraslados.FuenteNoEncontrada), (r.Error!.Categoria, r.Error.Clave)));
        Assert.Equal((CategoriaDeError.NoEncontrado, ClavesDeTraslados.EdicionNoEncontrada), (edicionInexistente.Error!.Categoria, edicionInexistente.Error.Clave));
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-19")]
    [InlineData("", "ayacucho")]
    [InlineData("cusco", "")]
    [InlineData("CUSCO", "ayacucho")]
    [InlineData("cusco", "a")]
    public async Task LaFormaDeLosCodigosSeValidaAntesDeConsumirElPresupuesto(string origen, string destino)
    {
        var (entorno, edicion) = ComoEncargadoDeAyacucho();

        var resultado = await entorno.Solicitar().Ejecutar(Solicitud(edicion, 1, origen, destino));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Empty(entorno.Limites.Reservas);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-19")]
    public async Task ElMotivoSeValidaPeroEsOpcional()
    {
        var (entorno, edicion) = ComoEncargadoDeAyacucho();

        var sinMotivo = await entorno.Solicitar().Ejecutar(Solicitud(edicion, 1, motivo: null));
        var largo = await entorno.Solicitar().Ejecutar(Solicitud(edicion, 1, motivo: new string('m', 201)));
        var conControl = await entorno.Solicitar().Ejecutar(Solicitud(edicion, 1, motivo: "uno\ndos"));

        Assert.True(sinMotivo.EsExito);
        Assert.Equal((CategoriaDeError.Validacion, ConstructorDeValidacion.DemasiadoLargo), (largo.Error!.Categoria, largo.Error.Errores.Single().Clave));
        Assert.Equal(ConstructorDeValidacion.FormatoInvalido, conControl.Error!.Errores.Single().Clave);
    }

    // ---------- AC-TRA-03: el rol ----------

    [Theory]
    [Trait("Criterio", "AC-TRA-03")]
    [InlineData(RolesDeUsuario.PersonalDeLogistica)]
    [InlineData(RolesDeUsuario.Cliente)]
    public async Task LogisticaYClienteNoSolicitanNiVenOrigenesPosibles_YDejanUnUnicoEventoCadaVez(string rol)
    {
        var (entorno, edicion) = ComoEncargadoDeAyacucho();
        entorno.Como(rol);

        var solicitud = await entorno.Solicitar().Ejecutar(Solicitud(edicion));
        var origenes = await entorno.Origenes().Ejecutar(new ConsultarOrigenesPosiblesEntrada(edicion, "ayacucho"));

        Assert.All([solicitud.Error!, origenes.Error!], e => Assert.Equal((CategoriaDeError.Prohibido, AutorizadorDeTraslados.ClaveProhibido), (e.Categoria, e.Clave)));
        Assert.Equal(["fuera-de-rol", "fuera-de-rol"], entorno.Base.MotivosDeDenegacion.ToArray());
        Assert.Empty(entorno.Limites.Reservas);
        Assert.Empty(entorno.Traslados.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task SinSesionOConUnVisitanteNoHayPermisoPorOmision()
    {
        var (entorno, edicion) = ComoEncargadoDeAyacucho();
        entorno.Actor.Establecer(null);
        var sinContexto = await entorno.Solicitar().Ejecutar(Solicitud(edicion));
        entorno.Actor.Establecer(ActorActual.Anonimo());
        var visitante = await entorno.Solicitar().Ejecutar(Solicitud(edicion));
        entorno.Actor.Establecer(ActorActual.DelSistema());
        var sistema = await entorno.Solicitar().Ejecutar(Solicitud(edicion));

        Assert.Equal(AutorizadorDeTraslados.ClaveNoAutenticado, sinContexto.Error!.Clave);
        Assert.Equal(AutorizadorDeTraslados.ClaveNoAutenticado, visitante.Error!.Clave);
        Assert.Equal(AutorizadorDeTraslados.ClaveProhibido, sistema.Error!.Clave);
        Assert.Empty(entorno.Traslados.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task UnEncargadoSinSucursalNoEsUnComodin()
    {
        var (entorno, edicion) = ComoEncargadoDeAyacucho();
        entorno.Como(RolesDeUsuario.EncargadoDeSucursal, null);

        var resultado = await entorno.Solicitar().Ejecutar(Solicitud(edicion));

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
    }

    // ---------- Presupuesto traslados-del-personal ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task ElPresupuestoDelPersonalSeConsumeSoloAqui_UnaVezPorIntento_AunqueSeRechace()
    {
        var (entorno, edicion) = ComoEncargadoDeAyacucho();
        var usuario = entorno.Actor.Actual!.UsuarioId!.Value.ToString("D");

        await entorno.Solicitar().Ejecutar(Solicitud(edicion, 1));
        await entorno.Solicitar().Ejecutar(Solicitud(edicion, 100));
        var traslado = entorno.Traslados.Todos.Single();
        await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(traslado.Id));

        Assert.Equal([("traslados-del-personal", usuario), ("traslados-del-personal", usuario)], entorno.Limites.Reservas.ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task ElIntentoQueSuperaElPresupuestoSeRechazaConLaRespuestaUniforme_SinCrearNada()
    {
        var (entorno, edicion) = ComoEncargadoDeAyacucho();
        var desde = EntornoDeTraslados.Ahora.AddMinutes(30);
        entorno.Limites.Forzado = ResultadoDeLimite.Excedido("traslados-del-personal", 1800, desde);

        var resultado = await entorno.Solicitar().Ejecutar(Solicitud(edicion, 1));

        Assert.Equal((CategoriaDeError.Limite, DetalleDeLimiteDeTraslados.ClaveDeLimiteExcedido), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.True(DetalleDeLimiteDeTraslados.TryLeer(resultado.Error.Errores, out var rechazo));
        Assert.Equal(("traslados-del-personal", 1800, desde), (rechazo!.Motivo, rechazo.EsperaEnSegundos, rechazo.DisponibleDesde));
        Assert.Empty(entorno.Traslados.Todos);
        Assert.Empty(entorno.Base.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task ConElAlmacenDeContadoresCaidoFallaCerrado()
    {
        var (entorno, edicion) = ComoEncargadoDeAyacucho();
        entorno.Limites.Forzado = ResultadoDeLimite.NoDisponible();

        var resultado = await entorno.Solicitar().Ejecutar(Solicitud(edicion, 1));

        Assert.Equal((CategoriaDeError.NoDisponible, ClavesDeTraslados.LimitesNoDisponibles), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Empty(entorno.Traslados.Todos);
    }

    // ---------- Auditoría ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task SiNoSePuedeAuditar_LaSolicitudFalla_ParaQueLaTransaccionSeRevierta()
    {
        var (entorno, edicion) = ComoEncargadoDeAyacucho();
        entorno.Base.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.Solicitar().Ejecutar(Solicitud(edicion, 1)));

        Assert.Empty(entorno.Buzon.Mensajes);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task ElOrdenEsAutorizarAntesQueTocarElBuzonYAuditarAntesDeAvisar()
    {
        var (entorno, edicion) = ComoEncargadoDeAyacucho();

        await entorno.Solicitar().Ejecutar(Solicitud(edicion, 1));

        Assert.Equal(["auditoria.TrasladoSolicitado", "buzon.Insertar"], entorno.Base.Bitacora.Llamadas.Where(l => l.StartsWith("auditoria", StringComparison.Ordinal) || l.StartsWith("buzon", StringComparison.Ordinal)).ToArray());
    }
}
