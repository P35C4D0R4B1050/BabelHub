using BabelHub.Inventario.Application.Ajustes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Ajustes;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.UnitTests.Ajustes;

/// <summary>T06 a T09: el ingreso de mercadería y su lista, el reingreso por devolución y el historial de ajustes.</summary>
public sealed class IngresosReingresosEHistorialTests
{
    private readonly EntornoDeAjustes _e = new();

    private static RegistrarIngresoDeMercaderiaEntrada Ingreso(Guid edicion, int cantidad, string? fuente = null, string? referencia = null, string? nota = null, string? clave = null) =>
        new(edicion, fuente, cantidad, referencia, nota, clave ?? EntornoDeAjustes.Clave());

    // ---------- Ingreso (AC-AJU-10, AC-AJU-15) ----------

    [Fact]
    [Trait("Criterio", "AC-AJU-10")]
    public async Task UnIngresoDeDiezEnLaRecepcionLlevaElFisicoADiezYOtroDeCincoAQuince_ConUnaSolaAuditoriaCadaUno()
    {
        var libro = _e.Impresa();
        var caso = _e.Ingreso();

        var primero = await caso.Ejecutar(Ingreso(libro, 10, referencia: "GR-001"));
        var segundo = await caso.Ejecutar(Ingreso(libro, 5));

        Assert.True(primero.EsExito, primero.Error?.Clave);
        Assert.Equal((0, 10, "lima", "GR-001"), (primero.Valor.FisicoAnterior, primero.Valor.FisicoPosterior, primero.Valor.CodigoDeFuente, primero.Valor.Referencia));
        Assert.Equal((10, 15), (segundo.Valor.FisicoAnterior, segundo.Valor.FisicoPosterior));
        Assert.Equal((15, 0), _e.Stock(_e.Lima, libro));
        Assert.Equal(2, _e.Ingresos.Guardados.Count);
        Assert.Equal(2, _e.Ajustes.Guardados.Count(a => a.Motivo == MotivoDeAjuste.IngresoDeMercaderia));
        Assert.Equal(["IngresoDeMercaderiaRegistrado", "IngresoDeMercaderiaRegistrado"], _e.Base.Auditoria.Solicitudes.Select(s => s.Accion.Identificador));
        Assert.Equal(2, _e.Bloqueos.Recepciones);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-10")]
    public async Task LaReferenciaYLaNotaNoVanALaAuditoria()
    {
        var libro = _e.Impresa();

        await _e.Ingreso().Ejecutar(Ingreso(libro, 10, referencia: "REF-CENTINELA", nota: "NOTA-CENTINELA"));

        var auditada = Assert.Single(_e.Base.Auditoria.Solicitudes);
        Assert.DoesNotContain(auditada.Detalle.Values, v => v?.Contains("CENTINELA", StringComparison.Ordinal) == true);
        Assert.Equal("ingreso", auditada.ObjetoTipo);
        Assert.Equal(_e.Lima, auditada.SucursalOrigen);
    }

    [Theory]
    [Trait("Criterio", "AC-AJU-10")]
    [InlineData(RolesDeUsuario.EncargadoDeSucursal)]
    [InlineData(RolesDeUsuario.PersonalDeLogistica)]
    [InlineData(RolesDeUsuario.Cliente)]
    public async Task SoloLaAdministracionCentralRegistraIngresos_CualquierOtroQuedaComoAccesoDenegado(string rol)
    {
        var libro = _e.Impresa();
        _e.Como(rol, rol == RolesDeUsuario.EncargadoDeSucursal ? _e.Lima : null);

        var resultado = await _e.Ingreso().Ejecutar(Ingreso(libro, 10));

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(["fuera-de-rol"], _e.Base.MotivosDeDenegacion);
        Assert.Empty(_e.Ingresos.Guardados);
        Assert.Empty(_e.Limites.Reservas);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-15")]
    public async Task UnIngresoEnUnaFuenteQueNoEsLaDeRecepcionSeRechazaConSuMotivo_ElDeCeroOSeisMilTambien()
    {
        var libro = _e.Impresa();

        var enCusco = await _e.Ingreso().Ejecutar(Ingreso(libro, 5, "cusco"));
        var cero = await _e.Ingreso().Ejecutar(Ingreso(libro, 0, "lima"));
        var seisMil = await _e.Ingreso().Ejecutar(Ingreso(libro, 6000));
        var enLima = await _e.Ingreso().Ejecutar(Ingreso(libro, 5, "lima"));

        Assert.Equal((CategoriaDeError.ReglaDeNegocio, "fuente-no-es-de-recepcion"), (enCusco.Error!.Categoria, enCusco.Error.Clave));
        Assert.Equal((CategoriaDeError.ReglaDeNegocio, "cantidad-fuera-de-rango"), (cero.Error!.Categoria, cero.Error.Clave));
        Assert.Equal("cantidad-fuera-de-rango", seisMil.Error!.Clave);
        Assert.True(enLima.EsExito, enLima.Error?.Clave);
        Assert.Equal((5, 0), _e.Stock(_e.Lima, libro));
        Assert.Equal((0, 0), _e.Stock(_e.Cusco, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-15")]
    public async Task UnaReferenciaConCaracteresNoPermitidosOUnaFuenteInexistenteSeRechaza()
    {
        var libro = _e.Impresa();

        var referencia = await _e.Ingreso().Ejecutar(Ingreso(libro, 5, referencia: "guía; DROP"));
        var inexistente = await _e.Ingreso().Ejecutar(Ingreso(libro, 5, "noexiste"));

        Assert.Equal("referencia-invalida", referencia.Error!.Clave);
        Assert.Equal((CategoriaDeError.NoEncontrado, "fuente-no-encontrada"), (inexistente.Error!.Categoria, inexistente.Error.Clave));
        Assert.Empty(_e.Ingresos.Guardados);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-14")]
    public async Task UnIngresoDeUnaEdicionDigitalOSinFichaSeRechaza()
    {
        var digital = await _e.Ingreso().Ejecutar(Ingreso(_e.Digital(), 5));
        var sinFicha = await _e.Ingreso().Ejecutar(Ingreso(Guid.NewGuid(), 5));

        Assert.Equal("edicion-no-impresa", digital.Error!.Clave);
        Assert.Equal("edicion-no-encontrada", sinFicha.Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-18")]
    public async Task ElIngresoRepetidoConLaMismaClaveDevuelveElOriginalSinSumarOtraVez_YConOtraCargaEsConflicto()
    {
        var libro = _e.Impresa();
        var clave = EntornoDeAjustes.Clave();
        var caso = _e.Ingreso();

        var primero = await caso.Ejecutar(Ingreso(libro, 10, referencia: "GR-001", clave: clave));
        var repetido = await caso.Ejecutar(Ingreso(libro, 10, referencia: "GR-001", clave: clave));
        var conflicto = await caso.Ejecutar(Ingreso(libro, 11, referencia: "GR-001", clave: clave));

        Assert.Equal((primero.Valor.IngresoId, primero.Valor.AjusteId), (repetido.Valor.IngresoId, repetido.Valor.AjusteId));
        Assert.Equal("idempotencia-en-conflicto", conflicto.Error!.Clave);
        Assert.Equal((10, 0), _e.Stock(_e.Lima, libro));
        Assert.Single(_e.Ingresos.Guardados);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-10")]
    public async Task ElIngresoSeEsperaAlBloqueoDeRecepcion_YSiSeAgotaLaEsperaResponde503()
    {
        _e.Bloqueos.EsperaAgotada = true;

        var resultado = await _e.Ingreso().Ejecutar(Ingreso(_e.Impresa(), 5));

        Assert.Equal((CategoriaDeError.NoDisponible, "bloqueo-ocupado"), (resultado.Error!.Categoria, resultado.Error.Clave));
    }

    // ---------- Lista de ingresos (AC-AJU-16) ----------

    [Fact]
    [Trait("Criterio", "AC-AJU-16")]
    public async Task LaAdministracionVeLosTresIngresosConEdicionCantidadReferenciaNotaUsuarioYFecha_YUnEncargadoNoPuedeAbrirLaLista()
    {
        var libro = _e.Impresa();
        var inicio = _e.Base.Reloj.Ahora;
        for (var i = 1; i <= 3; i++)
        {
            _e.Base.Reloj.Ahora = inicio.AddMinutes(i);
            Assert.True((await _e.Ingreso().Ejecutar(Ingreso(libro, i, referencia: $"GR-00{i}", nota: $"n{i}"))).EsExito);
        }

        var lista = await _e.ListaDeIngresos().Ejecutar(new ConsultarIngresosEntrada(null, 50));
        _e.ComoEncargadoDe(_e.Lima);
        var deEncargado = await _e.ListaDeIngresos().Ejecutar(new ConsultarIngresosEntrada(null, 50));

        Assert.Equal(3, lista.Valor.Ingresos.Count);
        Assert.All(lista.Valor.Ingresos, i => Assert.Equal((libro, "lima"), (i.EdicionId, i.CodigoDeFuente)));
        // Cada ingreso con su cantidad, su nota y su fecha, del más reciente al más antiguo (AJU-20).
        Assert.Equal(["GR-003", "GR-002", "GR-001"], lista.Valor.Ingresos.Select(i => i.Referencia));
        Assert.Equal([3, 2, 1], lista.Valor.Ingresos.Select(i => i.Cantidad));
        Assert.Equal(["n3", "n2", "n1"], lista.Valor.Ingresos.Select(i => i.Nota));
        Assert.Equal([inicio.AddMinutes(3), inicio.AddMinutes(2), inicio.AddMinutes(1)], lista.Valor.Ingresos.Select(i => i.Instante));
        Assert.All(lista.Valor.Ingresos, i => Assert.NotNull(i.UsuarioId));
        Assert.Equal(CategoriaDeError.Prohibido, deEncargado.Error!.Categoria);
    }

    [Theory]
    [Trait("Criterio", "AC-AJU-16")]
    [InlineData(0)]
    [InlineData(51)]
    public async Task ElLimiteDeLaListaVaDe1a50(int limite)
    {
        var resultado = await _e.ListaDeIngresos().Ejecutar(new ConsultarIngresosEntrada(null, limite));

        Assert.Contains(resultado.Error!.Errores, e => e.Campo == "limite");
    }

    // ---------- Reingreso por devolución (AC-AJU-09, AC-AJU-14) ----------

    private static LineaDeReingreso L(Guid edicion, int cantidad) => new(edicion, cantidad);

    private static ReingresarPorDevolucionEntrada Reingreso(Guid devolucion, Guid pedido, string fuente, params LineaDeReingreso[] lineas) => new(devolucion, pedido, fuente, lineas);

    [Fact]
    [Trait("Criterio", "AC-AJU-09")]
    public async Task UnReingresoSumaAlFisicoDeLaSucursalReceptora_ConElMotivoDevolucionYAuditoria_YRepetirloNoSumaOtraVez()
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Ayacucho, libro, 3, 0);
        _e.ComoEncargadoDe(_e.Ayacucho);
        var devolucion = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        var caso = _e.Reingreso();

        var primero = await caso.Ejecutar(Reingreso(devolucion, pedido, "ayacucho", L(libro, 1)));
        var repetido = await caso.Ejecutar(Reingreso(devolucion, pedido, "ayacucho", L(libro, 1)));

        Assert.True(primero.EsExito, primero.Error?.Clave);
        Assert.False(primero.Valor.YaAplicado);
        Assert.Equal((3, 4, "devolucion"), (primero.Valor.Ajustes[0].FisicoAnterior, primero.Valor.Ajustes[0].FisicoPosterior, primero.Valor.Ajustes[0].Motivo));
        Assert.True(repetido.Valor.YaAplicado);
        Assert.Equal(primero.Valor.Ajustes[0].AjusteId, repetido.Valor.Ajustes[0].AjusteId);
        Assert.Equal((4, 0), _e.Stock(_e.Ayacucho, libro));
        var auditada = Assert.Single(_e.Base.Auditoria.Solicitudes);
        Assert.Equal(("ReingresoPorDevolucion", devolucion.ToString("D")), (auditada.Accion.Identificador, auditada.Detalle["devolucionId"]));
        Assert.Equal(2, _e.Bloqueos.Devoluciones.Count);
        Assert.Empty(_e.Limites.Reservas);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-09")]
    public async Task SiLaRepeticionCambiaLaFuenteLaCantidadOElPedido_EsConflicto()
    {
        var libro = _e.Impresa();
        _e.Como(RolesDeUsuario.AdministradorCentral);
        var devolucion = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        var caso = _e.Reingreso();
        Assert.True((await caso.Ejecutar(Reingreso(devolucion, pedido, "ayacucho", L(libro, 2)))).EsExito);

        var otraFuente = await caso.Ejecutar(Reingreso(devolucion, pedido, "cusco", L(libro, 2)));
        var otraCantidad = await caso.Ejecutar(Reingreso(devolucion, pedido, "ayacucho", L(libro, 3)));
        var otroPedido = await caso.Ejecutar(Reingreso(devolucion, Guid.NewGuid(), "ayacucho", L(libro, 2)));

        Assert.All(new[] { otraFuente, otraCantidad, otroPedido }, r => Assert.Equal((CategoriaDeError.Conflicto, "idempotencia-en-conflicto"), (r.Error!.Categoria, r.Error.Clave)));
        Assert.Equal((2, 0), _e.Stock(_e.Ayacucho, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-09")]
    public async Task SiSoloParteDeLasLineasYaEstabanAplicadas_SeAplicanLasNuevasYYaAplicadoEsFalso()
    {
        var uno = _e.Impresa();
        var dos = _e.Impresa();
        _e.Como(RolesDeUsuario.AdministradorCentral);
        var devolucion = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        var caso = _e.Reingreso();
        Assert.True((await caso.Ejecutar(Reingreso(devolucion, pedido, "lima", L(uno, 1)))).EsExito);

        var completo = await caso.Ejecutar(Reingreso(devolucion, pedido, "lima", L(uno, 1), L(dos, 4)));

        Assert.True(completo.EsExito, completo.Error?.Clave);
        Assert.False(completo.Valor.YaAplicado);
        Assert.Equal(2, completo.Valor.Ajustes.Count);
        Assert.Equal((1, 0), _e.Stock(_e.Lima, uno));
        Assert.Equal((4, 0), _e.Stock(_e.Lima, dos));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-09")]
    public async Task UnaViolacionDeLaUnicidadAlInsertarRespondeEnCurso_NuncaUnErrorDelServidor()
    {
        var libro = _e.Impresa();
        _e.Como(RolesDeUsuario.AdministradorCentral);
        _e.Ajustes.InsertarChocaConUnaConcurrente = true;

        var resultado = await _e.Reingreso().Ejecutar(Reingreso(Guid.NewGuid(), Guid.NewGuid(), "lima", L(libro, 1)));

        Assert.Equal((CategoriaDeError.Conflicto, "idempotencia-en-curso"), (resultado.Error!.Categoria, resultado.Error.Clave));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-14")]
    public async Task UnReingresoDeUnaEdicionDigitalOInexistenteOEnUnaFuenteInexistenteSeRechaza()
    {
        _e.Como(RolesDeUsuario.AdministradorCentral);
        var caso = _e.Reingreso();

        var digital = await caso.Ejecutar(Reingreso(Guid.NewGuid(), Guid.NewGuid(), "lima", L(_e.Digital(), 1)));
        var inexistente = await caso.Ejecutar(Reingreso(Guid.NewGuid(), Guid.NewGuid(), "lima", L(Guid.NewGuid(), 1)));
        var fuente = await caso.Ejecutar(Reingreso(Guid.NewGuid(), Guid.NewGuid(), "noexiste", L(_e.Impresa(), 1)));

        Assert.Equal("edicion-no-impresa", digital.Error!.Clave);
        Assert.Equal("edicion-no-encontrada", inexistente.Error!.Clave);
        Assert.Equal((CategoriaDeError.NoEncontrado, "fuente-no-encontrada"), (fuente.Error!.Categoria, fuente.Error.Clave));
        Assert.Empty(_e.Ajustes.Guardados);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-14")]
    public async Task ElEncargadoAjenoYLogisticaNoReingresan_ConAccesoDenegado()
    {
        var libro = _e.Impresa();
        _e.ComoEncargadoDe(_e.Cusco);
        var ajeno = await _e.Reingreso().Ejecutar(Reingreso(Guid.NewGuid(), Guid.NewGuid(), "ayacucho", L(libro, 1)));
        _e.Como(RolesDeUsuario.PersonalDeLogistica);
        var logistica = await _e.Reingreso().Ejecutar(Reingreso(Guid.NewGuid(), Guid.NewGuid(), "ayacucho", L(libro, 1)));

        Assert.All(new[] { ajeno, logistica }, r => Assert.Equal(CategoriaDeError.Prohibido, r.Error!.Categoria));
        Assert.Equal(["fuera-de-sucursal", "fuera-de-rol"], _e.Base.MotivosDeDenegacion);
        Assert.Empty(_e.Ajustes.Guardados);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-09")]
    public async Task LaFormaDelReingresoExigeElPedidoLineasUnicasHasta20YCantidadDe1a5000()
    {
        _e.Como(RolesDeUsuario.AdministradorCentral);
        var caso = _e.Reingreso();
        var libro = _e.Impresa();

        var sinPedido = await caso.Ejecutar(Reingreso(Guid.NewGuid(), Guid.Empty, "lima", L(libro, 1)));
        var sinLineas = await caso.Ejecutar(Reingreso(Guid.NewGuid(), Guid.NewGuid(), "lima"));
        var duplicadas = await caso.Ejecutar(Reingreso(Guid.NewGuid(), Guid.NewGuid(), "lima", L(libro, 1), L(libro, 2)));
        var veintiuna = await caso.Ejecutar(Reingreso(Guid.NewGuid(), Guid.NewGuid(), "lima", [.. Enumerable.Range(0, 21).Select(_ => new LineaDeReingreso(Guid.NewGuid(), 1))]));
        var cantidad = await caso.Ejecutar(Reingreso(Guid.NewGuid(), Guid.NewGuid(), "lima", L(libro, 5001)));
        var cero = await caso.Ejecutar(Reingreso(Guid.NewGuid(), Guid.NewGuid(), "lima", L(libro, 0)));

        Assert.Contains(sinPedido.Error!.Errores, e => e.Campo == "pedidoId");
        Assert.Contains(sinLineas.Error!.Errores, e => e.Campo == "lineas");
        Assert.Contains(duplicadas.Error!.Errores, e => e.Campo == "lineas");
        Assert.Contains(veintiuna.Error!.Errores, e => e.Campo == "lineas");
        Assert.Contains(cantidad.Error!.Errores, e => e.Campo == "lineas.cantidad");
        Assert.Contains(cero.Error!.Errores, e => e.Campo == "lineas.cantidad");
        Assert.Empty(_e.Ajustes.Guardados);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-09")]
    public async Task LasUnidadesReingresadasPorPedidoYEdicionSeSumanEnTodasLasSucursales()
    {
        var libro = _e.Impresa();
        _e.Como(RolesDeUsuario.AdministradorCentral);
        var pedido = Guid.NewGuid();
        Assert.True((await _e.Reingreso().Ejecutar(Reingreso(Guid.NewGuid(), pedido, "lima", L(libro, 2)))).EsExito);
        Assert.True((await _e.Reingreso().Ejecutar(Reingreso(Guid.NewGuid(), pedido, "cusco", L(libro, 3)))).EsExito);
        Assert.True((await _e.Reingreso().Ejecutar(Reingreso(Guid.NewGuid(), Guid.NewGuid(), "cusco", L(libro, 7)))).EsExito);

        var reingresadas = await _e.ReingresosPorDevolucion().PorPedidos([pedido]);

        Assert.Equal([new BabelHub.Inventario.Application.Fuentes.UnidadesReingresadas(pedido, libro, 5)], reingresadas);
        Assert.Empty(await _e.ReingresosPorDevolucion().PorPedidos([]));
    }

    // ---------- Historial (AC-AJU-08, AC-AJU-13) ----------

    [Fact]
    [Trait("Criterio", "AC-AJU-13")]
    public async Task ElEncargadoDeAyacuchoVeSoloLosAjustesDeAyacuchoConSusDatosEnOrdenDescendente()
    {
        var libro = _e.Impresa();
        _e.Poner(_e.Ayacucho, libro, 20, 0);
        _e.Poner(_e.Cusco, libro, 20, 0);
        var primero = _e.Base.Reloj.Ahora;
        _e.ComoEncargadoDe(_e.Ayacucho);
        Assert.True((await _e.AjusteManual().Ejecutar(new("ayacucho", libro, "merma", 1, null, null, EntornoDeAjustes.Clave()))).EsExito);
        _e.Base.Reloj.Ahora = primero.AddMinutes(5);
        Assert.True((await _e.AjusteManual().Ejecutar(new("ayacucho", libro, "danio", 2, null, "n", EntornoDeAjustes.Clave()))).EsExito);
        _e.Base.Reloj.Ahora = primero.AddMinutes(10);
        _e.Como(RolesDeUsuario.AdministradorCentral);
        Assert.True((await _e.AjusteManual().Ejecutar(new("cusco", libro, "merma", 3, null, null, EntornoDeAjustes.Clave()))).EsExito);
        _e.ComoEncargadoDe(_e.Ayacucho);

        var historial = await _e.Historial().Ejecutar(new ConsultarHistorialDeAjustesEntrada("ayacucho", 1, 50));

        Assert.True(historial.EsExito, historial.Error?.Clave);
        Assert.Equal(2, historial.Valor.Total);
        Assert.All(historial.Valor.Elementos, a => Assert.Equal(("ayacucho", libro), (a.CodigoDeFuente, a.EdicionId)));
        Assert.All(historial.Valor.Elementos, a => Assert.NotNull(a.UsuarioId));
        // AJU-17 no incluye la nota: es texto libre y el encargado no debe ver la de otro usuario.
        Assert.All(historial.Valor.Elementos, a => Assert.Null(a.Nota));
        // Orden descendente por fecha: el más reciente primero, con la fecha y las cantidades de cada ajuste (AJU-17).
        Assert.Equal(["danio", "merma"], historial.Valor.Elementos.Select(a => a.Motivo));
        Assert.Equal([primero.AddMinutes(5), primero], historial.Valor.Elementos.Select(a => a.Instante));
        Assert.Equal([(19, 17), (20, 19)], historial.Valor.Elementos.Select(a => (a.FisicoAnterior, a.FisicoPosterior)));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-08")]
    public async Task ElHistorialDeOtraFuenteLogisticaYClienteSeRechazanConAccesoDenegado()
    {
        _e.ComoEncargadoDe(_e.Ayacucho);
        var otraFuente = await _e.Historial().Ejecutar(new ConsultarHistorialDeAjustesEntrada("cusco", 1, 50));
        _e.Como(RolesDeUsuario.PersonalDeLogistica);
        var logistica = await _e.Historial().Ejecutar(new ConsultarHistorialDeAjustesEntrada("ayacucho", 1, 50));
        _e.Como(RolesDeUsuario.Cliente);
        var cliente = await _e.Historial().Ejecutar(new ConsultarHistorialDeAjustesEntrada("ayacucho", 1, 50));

        Assert.All(new[] { otraFuente, logistica, cliente }, r => Assert.Equal(CategoriaDeError.Prohibido, r.Error!.Categoria));
        Assert.Equal(["fuera-de-sucursal", "fuera-de-rol", "fuera-de-rol"], _e.Base.MotivosDeDenegacion);
    }

    [Theory]
    [Trait("Criterio", "AC-AJU-13")]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task LaPaginacionDelHistorialTieneTopes(int pagina, int tamano)
    {
        var resultado = await _e.Historial().Ejecutar(new ConsultarHistorialDeAjustesEntrada("lima", pagina, tamano));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
    }
}
