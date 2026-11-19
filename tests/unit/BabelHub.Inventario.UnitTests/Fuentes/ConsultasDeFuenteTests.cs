using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Fuentes;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Fuentes;

/// <summary>T14: consultar una fuente, listarlas y sus existencias, con el alcance por sucursal comprobado en la Aplicación (FUE-08, FUE-09, FUE-11, FUE-18).</summary>
public sealed class ConsultasDeFuenteTests
{
    private static ConsultarFuente Consultar(EntornoDeFuentes e) => new(e.Autorizador, new ValidadorDeConsultarFuente(), e.Lectura, e.Fuentes, e.Totales);

    private static ListarFuentes Listar(EntornoDeFuentes e) => new(e.Autorizador, e.Lectura, e.Fuentes);

    private static ConsultarExistenciasDeSucursal Existencias(EntornoDeFuentes e) =>
        new(e.Autorizador, new ValidadorDeConsultarExistencias(), e.Fuentes, new ConsultaDeExistencias(e.Lectura, e.Fuentes, e.Existencias, e.Reservas));

    private static ConsultarExistenciasDeSucursalEntrada De(string codigo, IReadOnlyCollection<Guid>? ediciones = null, EstadoDeExistencia? estado = null, int pagina = 1, int tamano = 20) =>
        new(codigo, ediciones, estado, pagina, tamano);

    // --- ConsultarFuente ---

    [Fact]
    [Trait("Criterio", "AC-FUE-09")]
    public async Task ConsultarFuente_ComoAdministrador_VeSusTotalesDeFisicoReservadoYVendible()
    {
        var entorno = new EntornoDeFuentes();
        var a = entorno.Impresa();
        var b = entorno.Impresa();
        entorno.Existencias.Poner(entorno.Cusco.Id, a, 9, 1);
        entorno.Existencias.Poner(entorno.Cusco.Id, b, 5, 5);
        entorno.Reservas.Vencidas.Add(new UnidadesVencidas(entorno.Cusco.Id, b, 2));
        entorno.Existencias.Poner(entorno.Lima.Id, a, 100, 0);

        var resultado = await Consultar(entorno).Ejecutar(new ConsultarFuenteEntrada("cusco"));

        var detalle = resultado.Valor;
        Assert.Equal(("cusco", entorno.Cusco.Id), (detalle.Codigo, detalle.Id));
        Assert.Equal((14, 6, 10), (detalle.FisicoTotal, detalle.ReservadoTotal, detalle.VendibleTotal));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-09")]
    public async Task ConsultarFuente_NoDependeDeNingunaOperacionQueEscribaStock()
    {
        var parametros = typeof(ConsultarFuente).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();

        Assert.DoesNotContain(parametros, t => t == typeof(IMovimientosDeStock) || t == typeof(IAplicadorDeMovimientos) || t == typeof(IRepositorioDeExistencias));
        Assert.Contains(typeof(TotalesDeFuente), parametros);

        var entorno = new EntornoDeFuentes();
        await Consultar(entorno).Ejecutar(new ConsultarFuenteEntrada("cusco"));
        Assert.Empty(entorno.Existencias.Movimientos);
        Assert.Empty(entorno.Existencias.Liberaciones);
        Assert.DoesNotContain(entorno.Bitacora.Llamadas, l => l.StartsWith("existencias.Aplicar", StringComparison.Ordinal) || l.StartsWith("existencias.Bloquear", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-09")]
    public async Task ConsultarFuente_UnaFuenteInexistente_EsNoEncontradaYElCodigoInvalidoEsValidacion()
    {
        var entorno = new EntornoDeFuentes();

        var inexistente = await Consultar(entorno).Ejecutar(new ConsultarFuenteEntrada("piura"));
        var invalido = await Consultar(entorno).Ejecutar(new ConsultarFuenteEntrada("Cusco"));

        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, invalido.Error!.Categoria);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-09")]
    [MemberData(nameof(GestionDeFuentesTests.ActoresSinPermisoDeAdministracion), MemberType = typeof(GestionDeFuentesTests))]
    public async Task ConsultarFuente_ConCualquierOtroRol_SeRechazaConSuEvento(string etiqueta, ActorDePrueba actor, CategoriaDeError categoria, string motivo)
    {
        var entorno = new EntornoDeFuentes(actor);

        var resultado = await Consultar(entorno).Ejecutar(new ConsultarFuenteEntrada("cusco"));

        Assert.Equal(categoria, resultado.Error!.Categoria);
        Assert.Equal([motivo], entorno.MotivosDeDenegacion);
        Assert.NotNull(etiqueta);
    }

    // --- ListarFuentes ---

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task ListarFuentes_LaAdministracionVeTodas_ElEncargadoSoloLaSuya()
    {
        var admin = new EntornoDeFuentes();
        var encargadoDeCusco = new EntornoDeFuentes(ActorDePrueba.DeEncargadoDe(Fuente.IdentificadorDe("cusco")));
        var encargadoDeOtra = new EntornoDeFuentes(ActorDePrueba.DeEncargadoDe(Fuente.IdentificadorDe("piura")));

        var todas = await Listar(admin).Ejecutar(new ListarFuentesEntrada());
        var suya = await Listar(encargadoDeCusco).Ejecutar(new ListarFuentesEntrada());
        var deOtraSucursal = await Listar(encargadoDeOtra).Ejecutar(new ListarFuentesEntrada());

        Assert.Equal(["ayacucho", "cusco", "lima"], todas.Valor.Select(f => f.Codigo));
        Assert.Equal(["cusco"], suya.Valor.Select(f => f.Codigo));
        Assert.Empty(deOtraSucursal.Valor); // el encargado de una sucursal que no está entre las fuentes no ve ninguna

        // El encargado de una sucursal que sí existe en su entorno ve exactamente la suya.
        var propio = new EntornoDeFuentes();
        var actor = ActorDePrueba.DeEncargadoDe(propio.Ayacucho.Id);
        var ve = await new ListarFuentes(new AutorizadorDeFuentes(actor, propio.Denegaciones), propio.Lectura, propio.Fuentes).Ejecutar(new ListarFuentesEntrada());
        Assert.Equal(["ayacucho"], ve.Valor.Select(f => f.Codigo));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task ListarFuentes_UnEncargadoSinSucursalNoVeNinguna_YLogisticaYClienteSeRechazan()
    {
        var sinSucursal = new EntornoDeFuentes(ActorDePrueba.DeEncargadoDe(Guid.Empty));
        var logistica = new EntornoDeFuentes(ActorDePrueba.DePersonalDeLogistica());
        var cliente = new EntornoDeFuentes(ActorDePrueba.DeCliente());

        Assert.Empty((await Listar(sinSucursal).Ejecutar(new ListarFuentesEntrada())).Valor);
        Assert.Equal(CategoriaDeError.Prohibido, (await Listar(logistica).Ejecutar(new ListarFuentesEntrada())).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, (await Listar(cliente).Ejecutar(new ListarFuentesEntrada())).Error!.Categoria);
        Assert.Equal(["fuera-de-rol"], logistica.MotivosDeDenegacion);
    }

    // --- Existencias de una sucursal ---

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task Existencias_ElEncargadoDeAyacuchoPideCusco_SeRechazaConSuEventoYNoSeLeeNada()
    {
        var entorno = new EntornoDeFuentes();
        var encargado = ActorDePrueba.DeEncargadoDe(entorno.Ayacucho.Id);
        var caso = new ConsultarExistenciasDeSucursal(
            new AutorizadorDeFuentes(encargado, entorno.Denegaciones),
            new ValidadorDeConsultarExistencias(),
            entorno.Fuentes,
            new ConsultaDeExistencias(entorno.Lectura, entorno.Fuentes, entorno.Existencias, entorno.Reservas));

        var resultado = await caso.Ejecutar(De("cusco"));

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(["fuera-de-sucursal"], entorno.MotivosDeDenegacion);
        Assert.Equal(0, entorno.Lectura.Instantaneas);
        Assert.DoesNotContain("existencias.LeerDeLaFuente", entorno.Bitacora.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task Existencias_ElEncargadoPideUnaFuenteQueNoExiste_RecibeElMismoRechazoQueUnaAjena()
    {
        var entorno = new EntornoDeFuentes();
        var caso = new ConsultarExistenciasDeSucursal(
            new AutorizadorDeFuentes(ActorDePrueba.DeEncargadoDe(entorno.Ayacucho.Id), entorno.Denegaciones),
            new ValidadorDeConsultarExistencias(),
            entorno.Fuentes,
            new ConsultaDeExistencias(entorno.Lectura, entorno.Fuentes, entorno.Existencias, entorno.Reservas));

        var inexistente = await caso.Ejecutar(De("piura"));
        var invalido = await caso.Ejecutar(De("Piura!"));
        var ajena = await caso.Ejecutar(De("cusco"));

        Assert.All([inexistente, invalido, ajena], r => Assert.Equal(CategoriaDeError.Prohibido, r.Error!.Categoria));
        Assert.Equal(["fuera-de-sucursal", "fuera-de-sucursal", "fuera-de-sucursal"], entorno.MotivosDeDenegacion);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-06")]
    [InlineData("logistica")]
    [InlineData("cliente")]
    [InlineData("integracion")]
    [InlineData("visitante")]
    [InlineData("sin-contexto")]
    public async Task Existencias_LogisticaClienteYLosQueNoSonDeLaSucursal_SeRechazanConSuEvento(string quien)
    {
        var actor = quien switch
        {
            "logistica" => ActorDePrueba.DePersonalDeLogistica(),
            "cliente" => ActorDePrueba.DeCliente(),
            "integracion" => ActorDePrueba.DeIntegracion("e-commerce"),
            "visitante" => ActorDePrueba.Anonimo(),
            _ => ActorDePrueba.SinContexto(),
        };
        var entorno = new EntornoDeFuentes(actor);

        var resultado = await Existencias(entorno).Ejecutar(De("cusco"));

        Assert.False(resultado.EsExito);
        Assert.Single(entorno.Denegaciones.Eventos);
        Assert.Equal(0, entorno.Lectura.Instantaneas);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task Existencias_UnEncargadoSinSucursalNoEsComodin()
    {
        var entorno = new EntornoDeFuentes(ActorDePrueba.DeEncargadoDe(Guid.Empty));

        var resultado = await Existencias(entorno).Ejecutar(De("cusco"));

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(["fuera-de-sucursal"], entorno.MotivosDeDenegacion);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-07")]
    public async Task Existencias_ElEncargadoDeLaFuente_VeSoloLosTitulosQueElCatalogoResolvio_ConSusCifras()
    {
        var entorno = new EntornoDeFuentes();
        var catedral = entorno.Impresa();
        var otro = entorno.Impresa();
        entorno.Existencias.Poner(entorno.Ayacucho.Id, catedral, 9, 1);
        entorno.Existencias.Poner(entorno.Ayacucho.Id, otro, 4, 0);
        entorno.Existencias.Poner(entorno.Cusco.Id, catedral, 50, 0);
        var caso = new ConsultarExistenciasDeSucursal(
            new AutorizadorDeFuentes(ActorDePrueba.DeEncargadoDe(entorno.Ayacucho.Id), entorno.Denegaciones),
            new ValidadorDeConsultarExistencias(),
            entorno.Fuentes,
            new ConsultaDeExistencias(entorno.Lectura, entorno.Fuentes, entorno.Existencias, entorno.Reservas));

        var resultado = await caso.Ejecutar(De("ayacucho", [catedral, catedral]));

        var existencia = Assert.Single(resultado.Valor.Elementos);
        Assert.Equal((catedral, 9, 1, 8, EstadoDeExistencia.Normal), (existencia.EdicionId, existencia.Fisico, existencia.Reservado, existencia.Vendible, existencia.Estado));
        Assert.Equal(1, resultado.Valor.Total);
        Assert.Empty(entorno.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-15")]
    public async Task Existencias_ElEstadoSegunElVendible_SinStockBajoYNormal_YElFiltroPorEstado()
    {
        var entorno = new EntornoDeFuentes();
        var cero = entorno.Impresa();
        var dos = entorno.Impresa();
        var tres = entorno.Impresa();
        entorno.Existencias.Poner(entorno.Lima.Id, cero, 4, 4);
        entorno.Existencias.Poner(entorno.Lima.Id, dos, 2, 0);
        entorno.Existencias.Poner(entorno.Lima.Id, tres, 3, 0);

        var todas = await Existencias(entorno).Ejecutar(De("lima"));
        var soloBajo = await Existencias(entorno).Ejecutar(De("lima", estado: EstadoDeExistencia.Bajo));

        Assert.Equal(
            [(cero, EstadoDeExistencia.SinStock), (dos, EstadoDeExistencia.Bajo), (tres, EstadoDeExistencia.Normal)],
            todas.Valor.Elementos.OrderBy(e => new[] { cero, dos, tres }.ToList().IndexOf(e.EdicionId)).Select(e => (e.EdicionId, e.Estado)));
        Assert.Equal([dos], soloBajo.Valor.Elementos.Select(e => e.EdicionId));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-07")]
    public async Task Existencias_LaAdministracionPuedeVerCualquierFuente_YUnaInexistenteEsNoEncontrada()
    {
        var entorno = new EntornoDeFuentes();
        var libro = entorno.Impresa();
        entorno.Existencias.Poner(entorno.Cusco.Id, libro, 3, 0);

        var cusco = await Existencias(entorno).Ejecutar(De("cusco"));
        var inexistente = await Existencias(entorno).Ejecutar(De("piura"));

        Assert.Single(cusco.Valor.Elementos);
        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
        Assert.Empty(entorno.Denegaciones.Eventos);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-07")]
    [InlineData(0, 20, "pagina")]
    [InlineData(1, 0, "tamano")]
    [InlineData(1, 101, "tamano")]
    public async Task Existencias_ConPaginaOTamanoFueraDeRango_EsValidacion(int pagina, int tamano, string campo)
    {
        var resultado = await Existencias(new EntornoDeFuentes()).Ejecutar(De("cusco", pagina: pagina, tamano: tamano));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Contains(resultado.Error.Errores, e => e.Campo == campo);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-07")]
    public async Task Existencias_ConMasDeCincuentaEdicionesOUnaVacia_EsValidacion()
    {
        var entorno = new EntornoDeFuentes();

        var muchas = await Existencias(entorno).Ejecutar(De("cusco", [.. Enumerable.Range(0, 51).Select(_ => Guid.NewGuid())]));
        var vacia = await Existencias(entorno).Ejecutar(De("cusco", [Guid.Empty]));

        Assert.Contains(muchas.Error!.Errores, e => e.Campo == "edicionId");
        Assert.Contains(vacia.Error!.Errores, e => e.Campo == "edicionId");
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-07")]
    public async Task Existencias_LaPaginacionTieneTopeYDevuelveElTotal()
    {
        var entorno = new EntornoDeFuentes();
        for (var i = 0; i < 5; i++)
        {
            entorno.Existencias.Poner(entorno.Lima.Id, entorno.Impresa(), 3, 0);
        }

        var segunda = await Existencias(entorno).Ejecutar(De("lima", pagina: 2, tamano: 2));

        Assert.Equal((5, 2, 2), (segunda.Valor.Total, segunda.Valor.Elementos.Count, segunda.Valor.Pagina));
    }
}
