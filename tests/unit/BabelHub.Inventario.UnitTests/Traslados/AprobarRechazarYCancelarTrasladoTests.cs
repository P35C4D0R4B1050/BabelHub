using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Domain.Traslados;
using BabelHub.Inventario.UnitTests.Soporte;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>T06: aprobar, rechazar y cancelar un traslado, con el orden leer, autorizar, bloquear y revalidar, y la auditoría y los mensajes en la misma transacción.</summary>
public sealed class AprobarRechazarYCancelarTrasladoTests
{
    private const string Centinela = "MOTIVO-CENTINELA-456";

    private static (EntornoDeTraslados Entorno, Guid Edicion) Preparar()
    {
        var entorno = new EntornoDeTraslados(ActorDePrueba.DeAdministradorCentral());
        var edicion = entorno.Impresa();
        entorno.Poner(entorno.Cusco, edicion, 12, 0);
        entorno.Poner(entorno.Ayacucho, edicion, 0, 0);
        return (entorno, edicion);
    }

    // ---------- Aprobar: AC-TRA-03 ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task ElEncargadoDelOrigenAprueba_ElTrasladoPasaAAprobado_ConAuditoriaYAvisosEnLaMismaTransaccion()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);
        entorno.ComoEncargadoDe(entorno.Cusco);

        var resultado = await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(traslado.Id));

        Assert.Equal(("aprobado", 1L), (resultado.Valor.Estado, resultado.Valor.Version));
        Assert.Equal((EstadoDeTraslado.Aprobado, 1L), (entorno.Traslados.Almacenado(traslado.Id).Estado, entorno.Traslados.Almacenado(traslado.Id).Version));
        var auditoria = Assert.Single(entorno.Base.Auditoria.Solicitudes);
        Assert.Equal((AccionesAuditables.TrasladoAprobado, entorno.Cusco, entorno.Ayacucho, "solicitado", "aprobado"), (auditoria.Accion, auditoria.SucursalOrigen, auditoria.SucursalDestino, auditoria.Detalle["estadoAnterior"], auditoria.Detalle["estadoNuevo"]));
        Assert.Equal(["ayacucho", "cusco"], entorno.Buzon.Mensajes.Select(m => m.SucursalCodigo).Order().ToArray());
        Assert.All(entorno.Buzon.Mensajes, m => Assert.Equal($"traslado:{traslado.Id:D}:aprobado", m.ClaveDeEvento));
        Assert.Empty(entorno.Base.Denegaciones.Eventos);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-03")]
    [InlineData("lima")]
    [InlineData("ayacucho")]
    public async Task ElEncargadoDeOtraSucursalOElDelDestinoNoAprueba_SeRechazaConUnAccesoDenegado_YElEstadoNoCambia(string sucursal)
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);
        entorno.ComoEncargadoDe(sucursal == "lima" ? entorno.Lima : entorno.Ayacucho);

        var resultado = await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(traslado.Id));

        Assert.Equal((CategoriaDeError.Prohibido, AutorizadorDeTraslados.ClaveProhibido), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(["fuera-de-sucursal"], entorno.Base.MotivosDeDenegacion.ToArray());
        Assert.Equal((EstadoDeTraslado.Solicitado, 0L), (entorno.Traslados.Almacenado(traslado.Id).Estado, entorno.Traslados.Almacenado(traslado.Id).Version));
        Assert.Empty(entorno.Base.Auditoria.Solicitudes);
        Assert.Empty(entorno.Buzon.Mensajes);
        Assert.Equal(0, entorno.Traslados.LecturasBloqueadas);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task LaAdministracionCentralApruebaCualquierTraslado()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);

        var resultado = await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(traslado.Id));

        Assert.Equal("aprobado", resultado.Valor.Estado);
        Assert.Empty(entorno.Base.Denegaciones.Eventos);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-03")]
    [InlineData(RolesDeUsuario.PersonalDeLogistica)]
    [InlineData(RolesDeUsuario.Cliente)]
    public async Task LogisticaYClienteNoOperanElTraslado_YElRechazoLlegaAntesDeLeerlo(string rol)
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.Aprobado);
        entorno.Como(rol);

        var aprobar = await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(traslado.Id));
        var rechazar = await entorno.Rechazar().Ejecutar(new RechazarTrasladoEntrada(traslado.Id, "x"));
        var cancelar = await entorno.Cancelar().Ejecutar(new CancelarTrasladoEntrada(traslado.Id, "x"));

        Assert.All([aprobar.Error!, rechazar.Error!, cancelar.Error!], e => Assert.Equal(CategoriaDeError.Prohibido, e.Categoria));
        Assert.Equal(["fuera-de-rol", "fuera-de-rol", "fuera-de-rol"], entorno.Base.MotivosDeDenegacion.ToArray());
        Assert.Equal(0, entorno.Traslados.Lecturas);
        Assert.Equal(EstadoDeTraslado.Aprobado, entorno.Traslados.Almacenado(traslado.Id).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task ElTrasladoQueNoExiste_ParaElEncargadoEsElMismoRechazoQueUnoAjeno_ParaLaAdministracionEsNoEncontrado()
    {
        var (entorno, edicion) = Preparar();
        var ajeno = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);
        entorno.ComoEncargadoDe(entorno.Lima);

        var inexistente = await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(Guid.NewGuid()));
        var deOtraSucursal = await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(ajeno.Id));
        entorno.Como(RolesDeUsuario.AdministradorCentral);
        var paraLaAdministracion = await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(Guid.NewGuid()));

        Assert.Equal((inexistente.Error!.Categoria, inexistente.Error.Clave), (deOtraSucursal.Error!.Categoria, deOtraSucursal.Error.Clave));
        Assert.Equal((CategoriaDeError.NoEncontrado, ClavesDeTraslados.TrasladoNoEncontrado), (paraLaAdministracion.Error!.Categoria, paraLaAdministracion.Error.Clave));
        Assert.Equal(["fuera-de-sucursal", "fuera-de-sucursal"], entorno.Base.MotivosDeDenegacion.ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task LaLogisticaNoPuedeAprobarAunqueElIdNoExista_NoSeLeRevelaSiElTrasladoExiste()
    {
        var (entorno, _) = Preparar();
        entorno.Como(RolesDeUsuario.PersonalDeLogistica);

        var resultado = await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(Guid.NewGuid()));

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(["fuera-de-rol"], entorno.Base.MotivosDeDenegacion.ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task SinSesionNoSeLeeNiSeBloqueaNada()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);
        entorno.Actor.Establecer(null);

        var resultado = await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(traslado.Id));

        Assert.Equal(AutorizadorDeTraslados.ClaveNoAutenticado, resultado.Error!.Clave);
        Assert.Equal((0, 0), (entorno.Traslados.Lecturas, entorno.Traslados.LecturasBloqueadas));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-02")]
    public async Task UnaSegundaAprobacionSeRechazaPorEstadoInvalido_SinAuditarNiAvisarDeNuevo()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);

        var primera = await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(traslado.Id));
        var segunda = await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(traslado.Id));

        Assert.True(primera.EsExito);
        Assert.Equal((CategoriaDeError.Conflicto, ClavesDeTraslados.TrasladoEstadoInvalido), (segunda.Error!.Categoria, segunda.Error.Clave));
        Assert.Single(entorno.Base.Auditoria.Solicitudes);
        Assert.Equal(2, entorno.Buzon.Mensajes.Count);
        Assert.Equal(1L, entorno.Traslados.Almacenado(traslado.Id).Version);
        Assert.Empty(entorno.Base.Denegaciones.Eventos);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-02")]
    [InlineData(EstadoDeTraslado.Aprobado)]
    [InlineData(EstadoDeTraslado.EnTransito)]
    [InlineData(EstadoDeTraslado.Recibido)]
    [InlineData(EstadoDeTraslado.Rechazado)]
    [InlineData(EstadoDeTraslado.Cancelado)]
    public async Task SoloSeApruebaUnTrasladoSolicitado(EstadoDeTraslado estado)
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, estado);

        var resultado = await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(traslado.Id));

        Assert.Equal(ClavesDeTraslados.TrasladoEstadoInvalido, resultado.Error!.Clave);
        Assert.Equal(estado, entorno.Traslados.Almacenado(traslado.Id).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-14")]
    public async Task UnTrasladoVinculadoSiSeApruebaYSeBloqueaPrimeroElPedido_ConLaFilaDespues()
    {
        var (entorno, edicion) = Preparar();
        var pedido = Guid.NewGuid();
        var traslado = entorno.Vinculado(edicion, entorno.Cusco, entorno.Ayacucho, 1, pedido);

        var resultado = await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(traslado.Id));

        Assert.Equal("aprobado", resultado.Valor.Estado);
        Assert.Equal([pedido], entorno.Bloqueos.Pedidos);
        var orden = entorno.Base.Bitacora.Llamadas.Where(l => l.StartsWith("bloqueo", StringComparison.Ordinal) || l.StartsWith("auditoria", StringComparison.Ordinal) || l.StartsWith("buzon", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["bloqueo.Pedido", "auditoria.TrasladoAprobado", "buzon.Insertar"], orden);
        Assert.Equal(1, entorno.Traslados.LecturasBloqueadas);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-22")]
    public async Task UnLockTimeoutDelPedidoEsServicioNoDisponible_SinCambiarElTraslado()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Vinculado(edicion, entorno.Cusco, entorno.Ayacucho, 1, Guid.NewGuid());
        entorno.Bloqueos.EsperaAgotada = true;

        var resultado = await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(traslado.Id));

        Assert.Equal((CategoriaDeError.NoDisponible, ClavesDeTraslados.BloqueoOcupado), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(EstadoDeTraslado.Solicitado, entorno.Traslados.Almacenado(traslado.Id).Estado);
        Assert.Empty(entorno.Base.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task SiNoSePuedeAuditarLaAprobacion_LaOperacionFalla_ParaQueSeRevierta()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);
        entorno.Base.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(traslado.Id)));

        Assert.Empty(entorno.Buzon.Mensajes);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task LaIdentificacionInvalidaSeRechazaAntesDeLeer()
    {
        var (entorno, _) = Preparar();

        var resultado = await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(Guid.Empty));

        Assert.Equal((CategoriaDeError.Validacion, "trasladoId"), (resultado.Error!.Categoria, resultado.Error.Errores.Single().Campo));
        Assert.Equal(0, entorno.Traslados.Lecturas);
    }

    // ---------- Rechazar: AC-TRA-04, AC-TRA-14 ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-04")]
    public async Task ElOrigenRechazaConUnMotivo_ElTrasladoPasaARechazadoConEseMotivo_YElMotivoNoSale()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);
        entorno.ComoEncargadoDe(entorno.Cusco);

        var resultado = await entorno.Rechazar().Ejecutar(new RechazarTrasladoEntrada(traslado.Id, Centinela));

        Assert.Equal(("rechazado", Centinela), (resultado.Valor.Estado, resultado.Valor.MotivoDeCierre));
        var guardado = entorno.Traslados.Almacenado(traslado.Id);
        Assert.Equal((EstadoDeTraslado.Rechazado, Centinela, EntornoDeTraslados.Ahora), (guardado.Estado, guardado.MotivoDeCierre, guardado.CerradoEn));
        var auditoria = Assert.Single(entorno.Base.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.TrasladoRechazado, auditoria.Accion);
        Assert.DoesNotContain(auditoria.Detalle.Values, v => v?.Contains("CENTINELA", StringComparison.Ordinal) == true);
        Assert.All(entorno.Buzon.Mensajes, m => Assert.DoesNotContain("CENTINELA", m.Carga, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-04")]
    public async Task ElMotivoDelRechazoEsObligatorioYSeValida()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);

        var sinMotivo = await entorno.Rechazar().Ejecutar(new RechazarTrasladoEntrada(traslado.Id, "   "));
        var largo = await entorno.Rechazar().Ejecutar(new RechazarTrasladoEntrada(traslado.Id, new string('m', 201)));

        Assert.Equal(ConstructorDeValidacion.Obligatorio, sinMotivo.Error!.Errores.Single().Clave);
        Assert.Equal(ConstructorDeValidacion.DemasiadoLargo, largo.Error!.Errores.Single().Clave);
        Assert.Equal(EstadoDeTraslado.Solicitado, entorno.Traslados.Almacenado(traslado.Id).Estado);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-14")]
    [InlineData(RolesDeUsuario.EncargadoDeSucursal)]
    [InlineData(RolesDeUsuario.AdministradorCentral)]
    public async Task UnTrasladoVinculadoNoSeRechaza_ConElMensajeDeQueSoloSeApruebaOSeAnulaElPedido_SinEventoDeSeguridad(string rol)
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Vinculado(edicion, entorno.Cusco, entorno.Ayacucho, 1, Guid.NewGuid());
        entorno.Como(rol, entorno.Cusco);

        var resultado = await entorno.Rechazar().Ejecutar(new RechazarTrasladoEntrada(traslado.Id, "sin stock"));

        Assert.Equal((CategoriaDeError.ReglaDeNegocio, ClavesDeTraslados.TrasladoVinculadoNoSeRechaza), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Empty(entorno.Base.Denegaciones.Eventos);
        Assert.Empty(entorno.Base.Auditoria.Solicitudes);
        Assert.Empty(entorno.Buzon.Mensajes);
        Assert.Equal((EstadoDeTraslado.Solicitado, 0L), (entorno.Traslados.Almacenado(traslado.Id).Estado, entorno.Traslados.Almacenado(traslado.Id).Version));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-14")]
    public async Task UnaReposicionSinPedidoSiSePuedeRechazar_PeroUnVinculadoDeOtraSucursalSigueSiendoUnAccesoDenegado()
    {
        var (entorno, edicion) = Preparar();
        var reposicion = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);
        var vinculado = entorno.Vinculado(edicion, entorno.Cusco, entorno.Ayacucho, 1, Guid.NewGuid());
        entorno.ComoEncargadoDe(entorno.Cusco);

        var rechazada = await entorno.Rechazar().Ejecutar(new RechazarTrasladoEntrada(reposicion.Id, "sin stock"));
        entorno.ComoEncargadoDe(entorno.Lima);
        var deOtraSucursal = await entorno.Rechazar().Ejecutar(new RechazarTrasladoEntrada(vinculado.Id, "sin stock"));

        Assert.Equal("rechazado", rechazada.Valor.Estado);
        Assert.Equal((CategoriaDeError.Prohibido, 1), (deOtraSucursal.Error!.Categoria, entorno.Base.Denegaciones.Eventos.Count));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-14")]
    public async Task ElDestinoNoRechazaUnTraslado()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);
        entorno.ComoEncargadoDe(entorno.Ayacucho);

        var resultado = await entorno.Rechazar().Ejecutar(new RechazarTrasladoEntrada(traslado.Id, "no lo quiero"));

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(EstadoDeTraslado.Solicitado, entorno.Traslados.Almacenado(traslado.Id).Estado);
    }

    // ---------- Cancelar: AC-TRA-17 ----------

    [Theory]
    [Trait("Criterio", "AC-TRA-17")]
    [InlineData(EstadoDeTraslado.Solicitado)]
    [InlineData(EstadoDeTraslado.Aprobado)]
    public async Task ElOrigenCancelaUnaReposicionSolicitadaOAprobadaConMotivo(EstadoDeTraslado estado)
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, estado);
        entorno.ComoEncargadoDe(entorno.Cusco);

        var resultado = await entorno.Cancelar().Ejecutar(new CancelarTrasladoEntrada(traslado.Id, "error de pedido"));

        Assert.Equal(("cancelado", "error de pedido"), (resultado.Valor.Estado, resultado.Valor.MotivoDeCierre));
        var auditoria = Assert.Single(entorno.Base.Auditoria.Solicitudes);
        Assert.Equal((AccionesAuditables.TrasladoCancelado, estado == EstadoDeTraslado.Solicitado ? "solicitado" : "aprobado"), (auditoria.Accion, auditoria.Detalle["estadoAnterior"]));
        Assert.Equal(EstadoDeTraslado.Cancelado, entorno.Traslados.Almacenado(traslado.Id).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-17")]
    public async Task ElDestinoNoCancela_SeRechazaConUnAccesoDenegado()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.Aprobado);
        entorno.ComoEncargadoDe(entorno.Ayacucho);

        var resultado = await entorno.Cancelar().Ejecutar(new CancelarTrasladoEntrada(traslado.Id, "error de pedido"));

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(["fuera-de-sucursal"], entorno.Base.MotivosDeDenegacion.ToArray());
        Assert.Equal(EstadoDeTraslado.Aprobado, entorno.Traslados.Almacenado(traslado.Id).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-17")]
    public async Task LaAdministracionCancelaUnaReposicion_PeroUnVinculadoNoSeCancelaAMano()
    {
        var (entorno, edicion) = Preparar();
        var reposicion = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, EstadoDeTraslado.Aprobado);
        var vinculado = entorno.Vinculado(edicion, entorno.Cusco, entorno.Ayacucho, 1, Guid.NewGuid(), EstadoDeTraslado.Aprobado);

        var cancelada = await entorno.Cancelar().Ejecutar(new CancelarTrasladoEntrada(reposicion.Id, "error de pedido"));
        var aMano = await entorno.Cancelar().Ejecutar(new CancelarTrasladoEntrada(vinculado.Id, "error de pedido"));

        Assert.Equal("cancelado", cancelada.Valor.Estado);
        Assert.Equal((CategoriaDeError.ReglaDeNegocio, ClavesDeTraslados.TrasladoVinculadoNoSeRechaza), (aMano.Error!.Categoria, aMano.Error.Clave));
        Assert.Equal(EstadoDeTraslado.Aprobado, entorno.Traslados.Almacenado(vinculado.Id).Estado);
        Assert.Empty(entorno.Base.Denegaciones.Eventos);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-17")]
    [InlineData(EstadoDeTraslado.EnTransito)]
    [InlineData(EstadoDeTraslado.Recibido)]
    [InlineData(EstadoDeTraslado.Rechazado)]
    [InlineData(EstadoDeTraslado.Cancelado)]
    public async Task SoloSeCancelaUnTrasladoSolicitadoOAprobado(EstadoDeTraslado estado)
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4, estado);

        var resultado = await entorno.Cancelar().Ejecutar(new CancelarTrasladoEntrada(traslado.Id, "x"));

        Assert.Equal(ClavesDeTraslados.TrasladoEstadoInvalido, resultado.Error!.Clave);
        Assert.Equal(estado, entorno.Traslados.Almacenado(traslado.Id).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-17")]
    public async Task ElMotivoDeLaCancelacionEsObligatorio()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);

        var resultado = await entorno.Cancelar().Ejecutar(new CancelarTrasladoEntrada(traslado.Id, ""));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal(EstadoDeTraslado.Solicitado, entorno.Traslados.Almacenado(traslado.Id).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task NingunaTransicionConsumeElPresupuestoDelPersonal()
    {
        var (entorno, edicion) = Preparar();
        var uno = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 1);
        var dos = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 1);
        var tres = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 1);

        await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(uno.Id));
        await entorno.Rechazar().Ejecutar(new RechazarTrasladoEntrada(dos.Id, "x"));
        await entorno.Cancelar().Ejecutar(new CancelarTrasladoEntrada(tres.Id, "x"));

        Assert.Empty(entorno.Limites.Reservas);
    }
}
