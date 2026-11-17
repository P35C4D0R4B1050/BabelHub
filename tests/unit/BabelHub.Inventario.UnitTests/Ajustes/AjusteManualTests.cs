using BabelHub.Inventario.Application.Ajustes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.UnitTests.Ajustes;

/// <summary>T03 a T05: el ajuste manual (merma, daño y corrección de conteo) con la autorización, el presupuesto, la idempotencia y la auditoría (AJU-01 a AJU-10, AJU-12, AJU-15, AJU-18, AJU-22).</summary>
public sealed class AjusteManualTests
{
    private readonly EntornoDeAjustes _e = new();

    private static RegistrarAjusteManualEntrada Merma(string fuente, Guid edicion, int cantidad, string? nota = null, string? clave = null, string motivo = "merma") =>
        new(fuente, edicion, motivo, cantidad, null, nota, clave ?? EntornoDeAjustes.Clave());

    private static RegistrarAjusteManualEntrada Conteo(string fuente, Guid edicion, int conteo, string? clave = null) =>
        new(fuente, edicion, "correccion-de-conteo", null, conteo, null, clave ?? EntornoDeAjustes.Clave());

    // ---------- AC-AJU-01, AC-AJU-12 ----------

    [Fact]
    [Trait("Criterio", "AC-AJU-01")]
    public async Task UnaMermaDelEncargadoDeAyacucho_BajaElFisicoDeNueveASiete_GuardaElAjusteConNotaYAuditaEnLaMismaTransaccion()
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Ayacucho, libro, 9, 0);
        _e.ComoEncargadoDe(_e.Ayacucho);

        var resultado = await _e.AjusteManual().Ejecutar(Merma("ayacucho", libro, 2, "caja mojada"));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((9, 7, "merma", "caja mojada", "ayacucho"), (resultado.Valor.FisicoAnterior, resultado.Valor.FisicoPosterior, resultado.Valor.Motivo, resultado.Valor.Nota, resultado.Valor.CodigoDeFuente));
        Assert.Equal((7, 0), _e.Stock(_e.Ayacucho, libro));
        var ajuste = Assert.Single(_e.Ajustes.Guardados);
        Assert.Equal((_e.Actor.Actual!.UsuarioId, "caja mojada", _e.Ayacucho), (ajuste.UsuarioId, ajuste.Nota, ajuste.FuenteId));
        var auditada = Assert.Single(_e.Base.Auditoria.Solicitudes);
        Assert.Equal("AjusteRegistrado", auditada.Accion.Identificador);
        Assert.Equal(("9", "7", "merma"), (auditada.Detalle["cantidadAnterior"], auditada.Detalle["cantidadPosterior"], auditada.Detalle["motivo"]));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-01")]
    public async Task LaNotaNuncaVaALaAuditoria()
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Lima, libro, 9, 0);

        await _e.AjusteManual().Ejecutar(Merma("lima", libro, 1, "NOTA-CENTINELA-123"));

        var auditada = Assert.Single(_e.Base.Auditoria.Solicitudes);
        Assert.DoesNotContain(auditada.Detalle.Values, v => v?.Contains("CENTINELA", StringComparison.Ordinal) == true);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-12")]
    public async Task ElAdministradorCentralAjustaEnCuscoYLaAuditoriaIndicaLaSucursal()
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Cusco, libro, 5, 0);

        var resultado = await _e.AjusteManual().Ejecutar(Merma("cusco", libro, 1));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(_e.Cusco, Assert.Single(_e.Base.Auditoria.Solicitudes).SucursalOrigen);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-10")]
    public async Task SiLaAuditoriaFalla_LaOperacionFalla_ParaQueLaTransaccionSeRevierta()
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Lima, libro, 9, 0);
        _e.Base.Auditoria.Falla = true;

        await Assert.ThrowsAsync<BabelHub.Inventario.Application.Auditoria.AuditoriaNoDisponibleException>(() => _e.AjusteManual().Ejecutar(Merma("lima", libro, 1)));
    }

    // ---------- AC-AJU-02, AC-AJU-03, AC-AJU-04 ----------

    [Theory]
    [Trait("Criterio", "AC-AJU-02")]
    [InlineData("regalo")]
    [InlineData("devolucion")]
    [InlineData("ingreso-de-mercaderia")]
    [InlineData("")]
    public async Task UnMotivoFueraDeLosManualesSeRechazaPorValidacion_YElStockNoCambia(string motivo)
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Lima, libro, 9, 0);

        var resultado = await _e.AjusteManual().Ejecutar(Merma("lima", libro, 1, motivo: motivo));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Contains(resultado.Error.Errores, e => e.Campo == "motivo");
        Assert.Equal((9, 0), _e.Stock(_e.Lima, libro));
        Assert.Empty(_e.Limites.Reservas);
    }

    [Theory]
    [Trait("Criterio", "AC-AJU-03")]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task UnaMermaConCeroONegativoSeRechazaYElStockNoCambia(int cantidad)
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Lima, libro, 9, 0);

        var resultado = await _e.AjusteManual().Ejecutar(Merma("lima", libro, cantidad));

        Assert.Equal((CategoriaDeError.ReglaDeNegocio, "cantidad-no-positiva"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal((9, 0), _e.Stock(_e.Lima, libro));
        Assert.Empty(_e.Ajustes.Guardados);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-04")]
    public async Task UnDanioQueSuperaElVendibleSeRechazaConElFisicoElReservadoYElVendible()
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Lima, libro, 9, 1);

        var resultado = await _e.AjusteManual().Ejecutar(Merma("lima", libro, 9, motivo: "danio"));

        Assert.Equal((CategoriaDeError.Conflicto, "stock-insuficiente"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.True(DetalleDeStockInsuficienteDeAjuste.TryLeer(resultado.Error.Errores, out var detalle));
        Assert.Equal((9, 1, 8), detalle);
        Assert.Equal((9, 1), _e.Stock(_e.Lima, libro));
    }

    // ---------- AC-AJU-05, AC-AJU-06, AC-AJU-07 ----------

    [Fact]
    [Trait("Criterio", "AC-AJU-05")]
    public async Task UnConteoDeSieteConFisicoCincoYReservadoCincoSubeElFisico_ElVendibleQuedaEnDos_YElAjusteRegistraMasDos()
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Lima, libro, 5, 5);

        var resultado = await _e.AjusteManual().Ejecutar(Conteo("lima", libro, 7));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((7, 5), _e.Stock(_e.Lima, libro));
        var ajuste = Assert.Single(_e.Ajustes.Guardados);
        Assert.Equal((5, 7, 7, 2), (ajuste.CantidadAnterior, ajuste.CantidadPosterior, ajuste.ConteoReal, ajuste.Diferencia));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-06")]
    public async Task UnConteoMenorQueLoReservadoSeRechazaConLosPedidos_YUnoIgualSeAceptaYDejaElVendibleEnCero()
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Lima, libro, 8, 5);
        var pedidos = new[] { Guid.NewGuid(), Guid.NewGuid() };
        _e.Base.Reservas.Pedidos = new(pedidos, 2);

        var rechazado = await _e.AjusteManual().Ejecutar(Conteo("lima", libro, 4));

        Assert.Equal((CategoriaDeError.ReglaDeNegocio, "conteo-menor-que-reservas"), (rechazado.Error!.Categoria, rechazado.Error.Clave));
        Assert.True(DetalleDeConteoMenorQueReservas.TryLeer(rechazado.Error.Errores, out var ids, out var total));
        Assert.Equal(pedidos.Order(), ids.Order());
        Assert.Equal(2, total);
        Assert.Equal((8, 5), _e.Stock(_e.Lima, libro));

        var aceptado = await _e.AjusteManual().Ejecutar(Conteo("lima", libro, 5));

        Assert.True(aceptado.EsExito, aceptado.Error?.Clave);
        Assert.Equal((5, 5), _e.Stock(_e.Lima, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-07")]
    public async Task UnConteoIgualAlFisicoSeRechazaSinCambiarNada()
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Lima, libro, 9, 0);

        var resultado = await _e.AjusteManual().Ejecutar(Conteo("lima", libro, 9));

        Assert.Equal((CategoriaDeError.ReglaDeNegocio, "sin-cambios"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal((9, 0), _e.Stock(_e.Lima, libro));
        Assert.Empty(_e.Ajustes.Guardados);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-05")]
    public async Task UnConteoSobreUnaParejaSinFilaLaCreaYDecide()
    {
        var libro = _e.Impresa();

        var resultado = await _e.AjusteManual().Ejecutar(Conteo("lima", libro, 4));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((0, 4), (resultado.Valor.FisicoAnterior, resultado.Valor.FisicoPosterior));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-03")]
    public async Task LaFormaDelMotivoExigeLaCantidadOElConteoSegunCorresponda()
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Lima, libro, 9, 0);

        var mermaSinCantidad = await _e.AjusteManual().Ejecutar(new("lima", libro, "merma", null, null, null, EntornoDeAjustes.Clave()));
        var conteoConCantidad = await _e.AjusteManual().Ejecutar(new("lima", libro, "correccion-de-conteo", 2, 5, null, EntornoDeAjustes.Clave()));
        var conteoSinConteo = await _e.AjusteManual().Ejecutar(new("lima", libro, "correccion-de-conteo", null, null, null, EntornoDeAjustes.Clave()));

        Assert.Contains(mermaSinCantidad.Error!.Errores, e => e.Campo == "cantidad");
        Assert.Contains(conteoConCantidad.Error!.Errores, e => e.Campo == "cantidad");
        Assert.Contains(conteoSinConteo.Error!.Errores, e => e.Campo == "conteoReal");
        Assert.Empty(_e.Ajustes.Guardados);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-03")]
    public async Task LaEntradaConInt32MaxNoDesbordaNada()
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Lima, libro, 9, 0);

        var merma = await _e.AjusteManual().Ejecutar(Merma("lima", libro, int.MaxValue));
        var conteo = await _e.AjusteManual().Ejecutar(Conteo("lima", libro, int.MaxValue));

        Assert.Equal("stock-insuficiente", merma.Error!.Clave);
        Assert.Equal("conteo-fuera-de-rango", conteo.Error!.Clave);
        Assert.Equal((9, 0), _e.Stock(_e.Lima, libro));
    }

    // ---------- AC-AJU-08: alcance y autorización ----------

    [Fact]
    [Trait("Criterio", "AC-AJU-08")]
    public async Task UnEncargadoDeAyacuchoQueAjustaCuscoSeRechazaConAccesoDenegado_Y404NoSeLeAdelanta()
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Cusco, libro, 9, 0);
        _e.ComoEncargadoDe(_e.Ayacucho);

        var enCusco = await _e.AjusteManual().Ejecutar(Merma("cusco", libro, 1));
        var inexistente = await _e.AjusteManual().Ejecutar(Merma("noexiste", libro, 1));

        Assert.Equal(CategoriaDeError.Prohibido, enCusco.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, inexistente.Error!.Categoria);
        Assert.Equal(["fuera-de-sucursal", "fuera-de-sucursal"], _e.Base.MotivosDeDenegacion);
        Assert.Equal((9, 0), _e.Stock(_e.Cusco, libro));
    }

    [Theory]
    [Trait("Criterio", "AC-AJU-08")]
    [InlineData(RolesDeUsuario.PersonalDeLogistica)]
    [InlineData(RolesDeUsuario.Cliente)]
    public async Task LogisticaYClienteNoTienenNingunPermisoDeAjuste_ConAccesoDenegado_YSinConsumirPresupuesto(string rol)
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Lima, libro, 9, 0);
        _e.Como(rol);

        var resultado = await _e.AjusteManual().Ejecutar(Merma("lima", libro, 1));

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(["fuera-de-rol"], _e.Base.MotivosDeDenegacion);
        Assert.Empty(_e.Limites.Reservas);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-08")]
    public async Task SinContextoOUnVisitanteEsNoAutenticado()
    {
        _e.Actor.Establecer(null);

        var resultado = await _e.AjusteManual().Ejecutar(Merma("lima", _e.Impresa(), 1));

        Assert.Equal(CategoriaDeError.NoAutenticado, resultado.Error!.Categoria);
        Assert.Equal(["sin-sesion"], _e.Base.MotivosDeDenegacion);
    }

    // ---------- AC-AJU-14 ----------

    [Fact]
    [Trait("Criterio", "AC-AJU-14")]
    public async Task UnaEdicionDigitalOInexistenteSeRechaza()
    {
        _e.Poner(_e.Lima, _e.Impresa(), 9, 0);

        var digital = await _e.AjusteManual().Ejecutar(Merma("lima", _e.Digital(), 1));
        var inexistente = await _e.AjusteManual().Ejecutar(Merma("lima", Guid.NewGuid(), 1));
        var fuenteInexistente = await _e.AjusteManual().Ejecutar(Merma("noexiste", _e.Impresa(), 1));

        Assert.Equal((CategoriaDeError.ReglaDeNegocio, "edicion-no-impresa"), (digital.Error!.Categoria, digital.Error.Clave));
        Assert.Equal((CategoriaDeError.NoEncontrado, "edicion-no-encontrada"), (inexistente.Error!.Categoria, inexistente.Error.Clave));
        Assert.Equal((CategoriaDeError.NoEncontrado, "fuente-no-encontrada"), (fuenteInexistente.Error!.Categoria, fuenteInexistente.Error.Clave));
    }

    // ---------- presupuesto (SEG-30) ----------

    [Fact]
    [Trait("Criterio", "AC-AJU-08")]
    public async Task ElAjusteConsumeElPresupuestoDelUsuario_AunSiSeRechazaPorUnaReglaDeNegocio()
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Lima, libro, 9, 0);

        await _e.AjusteManual().Ejecutar(Merma("lima", libro, 0));
        await _e.AjusteManual().Ejecutar(Merma("lima", libro, 1));

        Assert.Equal(2, _e.Limites.Reservas.Count);
        Assert.All(_e.Limites.Reservas, r => Assert.Equal(("ajustes-del-personal", _e.Actor.Actual!.UsuarioId!.Value.ToString("D")), r));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-08")]
    public async Task SuperarElPresupuestoDaLaRespuestaUniformeDeLimite_YNoAplicaNada()
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Lima, libro, 9, 0);
        _e.Limites.Forzado = ResultadoDeLimite.Excedido("ajustes-del-personal", 120, _e.Base.Reloj.Ahora.AddSeconds(120));

        var resultado = await _e.AjusteManual().Ejecutar(Merma("lima", libro, 1));

        Assert.Equal((CategoriaDeError.Limite, "limite-excedido"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.True(DetalleDeLimiteDeAjustes.TryLeer(resultado.Error.Errores, out var rechazo));
        Assert.Equal(("ajustes-del-personal", 120), (rechazo!.Motivo, rechazo.EsperaEnSegundos));
        Assert.Equal((9, 0), _e.Stock(_e.Lima, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-08")]
    public async Task ConElAlmacenDeContadoresCaido_FallaCerrado()
    {
        _e.Limites.Forzado = ResultadoDeLimite.NoDisponible();

        var resultado = await _e.AjusteManual().Ejecutar(Merma("lima", _e.Impresa(), 1));

        Assert.Equal((CategoriaDeError.NoDisponible, "limites-no-disponibles"), (resultado.Error!.Categoria, resultado.Error.Clave));
    }

    // ---------- AC-AJU-18: idempotencia ----------

    [Fact]
    [Trait("Criterio", "AC-AJU-18")]
    public async Task LaMismaClaveYCargaDevuelveElOriginalSinReaplicar_YOtraCargaEsConflicto()
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Lima, libro, 9, 0);
        var clave = EntornoDeAjustes.Clave();
        var caso = _e.AjusteManual();

        var primera = await caso.Ejecutar(Merma("lima", libro, 2, "n", clave));
        var repetida = await caso.Ejecutar(Merma("lima", libro, 2, "n", clave));
        var otraCarga = await caso.Ejecutar(Merma("lima", libro, 3, "n", clave));

        Assert.True(primera.EsExito && repetida.EsExito);
        Assert.Equal(primera.Valor.AjusteId, repetida.Valor.AjusteId);
        Assert.Equal((CategoriaDeError.Conflicto, "idempotencia-en-conflicto"), (otraCarga.Error!.Categoria, otraCarga.Error.Clave));
        Assert.Equal((7, 0), _e.Stock(_e.Lima, libro));
        Assert.Single(_e.Ajustes.Guardados);
        Assert.Single(_e.Base.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-18")]
    public async Task QuienRepiteLaClaveSinAlcanceSobreLaFuenteSeRechazaSinRecibirElResultado_YOtroUsuarioConAlcanceHaceUnaOperacionIndependiente()
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Cusco, libro, 9, 0);
        _e.ComoEncargadoDe(_e.Cusco);
        var clave = EntornoDeAjustes.Clave();
        Assert.True((await _e.AjusteManual().Ejecutar(Merma("cusco", libro, 2, null, clave))).EsExito);

        // Otro encargado, de otra sucursal, repite la clave: rechazado por alcance, no recibe el resultado guardado.
        _e.ComoEncargadoDe(_e.Ayacucho);
        var ajeno = await _e.AjusteManual().Ejecutar(Merma("cusco", libro, 2, null, clave));
        Assert.Equal(CategoriaDeError.Prohibido, ajeno.Error!.Categoria);

        // Otro encargado de Cusco con la misma clave: operación independiente (la clave se acota por usuario).
        _e.ComoEncargadoDe(_e.Cusco);
        var otro = await _e.AjusteManual().Ejecutar(Merma("cusco", libro, 2, null, clave));
        Assert.True(otro.EsExito, otro.Error?.Clave);
        Assert.Equal((5, 0), _e.Stock(_e.Cusco, libro));
    }

    [Theory]
    [Trait("Criterio", "AC-AJU-18")]
    [InlineData("")]
    [InlineData("corta")]
    [InlineData("con espacios y simbolos !!!!!!!")]
    public async Task LaClaveDeIdempotenciaDebeTenerFormaYLargo(string clave)
    {
        var libro = _e.Impresa();

        var resultado = await _e.AjusteManual().Ejecutar(Merma("lima", libro, 1, null, clave));

        Assert.Contains(resultado.Error!.Errores, e => e.Campo == "idempotencyKey");
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-01")]
    public async Task UnaNotaDeMasDe200CaracteresOConSaltosDeLineaSeRechazaPorForma()
    {
        var libro = _e.Impresa();

        var larga = await _e.AjusteManual().Ejecutar(Merma("lima", libro, 1, new string('a', 201)));
        var conSalto = await _e.AjusteManual().Ejecutar(Merma("lima", libro, 1, "a\nb"));

        Assert.Contains(larga.Error!.Errores, e => e.Campo == "nota");
        Assert.Contains(conSalto.Error!.Errores, e => e.Campo == "nota");
        // El error de validación nunca repite el valor recibido.
        Assert.DoesNotContain(larga.Error.Errores, e => e.Clave.Length > 40);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-11")]
    public async Task LaSegundaMermaSeValidaContraElStockYaActualizado()
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Lima, libro, 5, 0);
        var caso = _e.AjusteManual();

        var primera = await caso.Ejecutar(Merma("lima", libro, 3));
        var segunda = await caso.Ejecutar(Merma("lima", libro, 3));

        Assert.True(primera.EsExito);
        Assert.Equal("stock-insuficiente", segunda.Error!.Clave);
        Assert.Equal((2, 0), _e.Stock(_e.Lima, libro));
    }
}
