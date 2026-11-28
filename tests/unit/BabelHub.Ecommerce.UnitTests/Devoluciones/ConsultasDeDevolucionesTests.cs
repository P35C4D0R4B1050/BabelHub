using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>Las consultas de Devoluciones (T13; DEV-13, DEV-21, DEV-28; AC-DEV-12, AC-DEV-20, AC-DEV-30): cada rol ve solo lo suyo, paginado con tope de 50 y sin el responsable de ningún cambio.</summary>
public sealed class ConsultasDeDevolucionesTests
{
    private static readonly DateTimeOffset Ahora = EntornoDeDevoluciones.AhoraInicial;

    private static readonly Guid SucursalDeLima = Guid.Parse("a1000000-0000-0000-0000-000000000001");

    private static async Task<Guid> SolicitarImpreso(EntornoDeDevoluciones cliente, string sucursal)
    {
        var linea = EntornoDeDevoluciones.Entregada(Ahora.AddDays(-2), 3, 50m);
        var pedido = cliente.SembrarPedido(linea);
        var solicitada = await cliente.SolicitarImpreso.Ejecutar(new SolicitarDevolucionDeImpresoEntrada(pedido.PedidoId, [new LineaPedidaParaDevolver(linea.LineaId, 2)], new TextoLibre("Defecto"), sucursal));
        Assert.True(solicitada.EsExito);
        return solicitada.Valor.DevolucionId;
    }

    private static async Task<Guid> Reportar(EntornoDeDevoluciones cliente)
    {
        var linea = EntornoDeDevoluciones.Digital();
        var pedido = new PedidoParaDevolucionesParaPruebas(cliente, linea).Pedido;
        cliente.Accesos.Sembrar(pedido.CuentaId, pedido.PedidoId, linea.EdicionId, enlaceEntregado: true);
        var reporte = await cliente.Reportar.Ejecutar(new ReportarArchivoDefectuosoEntrada(pedido.PedidoId, linea.LineaId, new TextoLibre("x"), new TextoLibre("y")));
        Assert.True(reporte.EsExito);
        return reporte.Valor.DevolucionId;
    }

    private static EntornoDeDevoluciones Como(ActorDePrueba actor, EntornoDeDevoluciones datos)
    {
        var entorno = new EntornoDeDevoluciones(actor).ConLosDatosDe(datos);
        entorno.Sucursales.Codigos[SucursalDeLima] = "lima-central";
        return entorno;
    }

    // ---------- Las del cliente ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public async Task El_cliente_lista_solo_las_suyas_y_la_vista_no_trae_responsable_ni_cuenta()
    {
        var a = new EntornoDeDevoluciones();
        var propia = await SolicitarImpreso(a, "lima-central");
        var b = Como(ActorDePrueba.DeCliente(), a);
        await SolicitarImpreso(b, "lima-central");

        var resultado = await a.Consultas.ListarMias(new ListarDevolucionesEntrada());

        Assert.True(resultado.EsExito);
        Assert.Equal(propia, Assert.Single(resultado.Valor.Devoluciones).DevolucionId);
        Assert.Equal(1, resultado.Valor.Total);
        var propiedades = typeof(DevolucionVista).GetProperties().Concat(typeof(LineaDeDevolucionVista).GetProperties()).Select(p => p.Name);
        Assert.DoesNotContain(propiedades, n => n.Contains("Responsable", StringComparison.Ordinal) || n.Contains("Cuenta", StringComparison.Ordinal) || n.Contains("Acceso", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-12")]
    [InlineData(0, 500, 1, 50)]
    [InlineData(-3, 0, 1, 1)]
    [InlineData(2, 20, 2, 20)]
    public async Task La_pagina_y_el_tamano_se_acotan_con_un_tope_de_50(int pagina, int tamano, int paginaEsperada, int tamanoEsperado)
    {
        var cliente = new EntornoDeDevoluciones();
        await SolicitarImpreso(cliente, "lima-central");

        var resultado = await cliente.Consultas.ListarMias(new ListarDevolucionesEntrada(pagina, tamano));

        Assert.Equal(paginaEsperada, resultado.Valor.Pagina);
        Assert.Equal(tamanoEsperado, resultado.Valor.Tamano);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public async Task Obtener_la_propia_funciona_y_una_ajena_responde_lo_mismo_que_una_inexistente_con_un_evento_por_intento()
    {
        var a = new EntornoDeDevoluciones();
        var propia = await SolicitarImpreso(a, "lima-central");
        var b = Como(ActorDePrueba.DeCliente(), a);

        var mia = await a.Consultas.ObtenerMia(propia);
        var ajena = await b.Consultas.ObtenerMia(propia);
        var inexistente = await b.Consultas.ObtenerMia(Guid.NewGuid());

        Assert.True(mia.EsExito);
        Assert.Equal(CategoriaDeError.NoEncontrado, ajena.Error!.Categoria);
        Assert.Equal(ajena.Error.Categoria, inexistente.Error!.Categoria);
        Assert.Equal(ajena.Error.Clave, inexistente.Error.Clave);
        Assert.Equal(2, b.Denegaciones.Eventos.Count);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-12")]
    [InlineData("encargado")]
    [InlineData("administracion")]
    [InlineData("anonimo")]
    public async Task Quien_no_es_cliente_no_usa_las_consultas_del_cliente(string quien)
    {
        var a = new EntornoDeDevoluciones();
        var id = await SolicitarImpreso(a, "lima-central");
        var actor = quien switch
        {
            "encargado" => ActorDePrueba.DeEncargadoDe(SucursalDeLima),
            "administracion" => ActorDePrueba.DeAdministradorCentral(),
            _ => ActorDePrueba.Anonimo(),
        };
        var otro = Como(actor, a);

        var lista = await otro.Consultas.ListarMias(new ListarDevolucionesEntrada());
        var detalle = await otro.Consultas.ObtenerMia(id);

        Assert.True(lista.Error!.Categoria is CategoriaDeError.Prohibido or CategoriaDeError.NoAutenticado);
        Assert.True(detalle.Error!.Categoria is CategoriaDeError.Prohibido or CategoriaDeError.NoAutenticado);
        Assert.Equal(2, otro.Denegaciones.Eventos.Count);
    }

    // ---------- La sucursal receptora ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public async Task El_encargado_lista_solo_los_impresos_de_su_sucursal_receptora_sin_reportes_ni_digitales()
    {
        var cliente = new EntornoDeDevoluciones();
        var deLima = await SolicitarImpreso(cliente, "lima-central");
        await SolicitarImpreso(cliente, "arequipa");
        await Reportar(cliente);
        var encargado = Como(ActorDePrueba.DeEncargadoDe(SucursalDeLima), cliente);

        var resultado = await encargado.Consultas.ListarDeLaSucursal("lima-central", new ListarDevolucionesEntrada());

        Assert.Equal(deLima, Assert.Single(resultado.Valor.Devoluciones).DevolucionId);
        Assert.Equal(1, resultado.Valor.Total);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public async Task Un_codigo_de_ruta_que_no_es_el_del_encargado_es_prohibido_con_un_evento_y_un_codigo_invalido_es_validacion()
    {
        var cliente = new EntornoDeDevoluciones();
        await SolicitarImpreso(cliente, "arequipa");
        var encargado = Como(ActorDePrueba.DeEncargadoDe(SucursalDeLima), cliente);

        var ajena = await encargado.Consultas.ListarDeLaSucursal("arequipa", new ListarDevolucionesEntrada());
        var invalido = await encargado.Consultas.ListarDeLaSucursal(" ", new ListarDevolucionesEntrada());

        Assert.Equal(CategoriaDeError.Prohibido, ajena.Error!.Categoria);
        Assert.Equal("fuera-de-sucursal", Assert.Single(encargado.Denegaciones.Eventos).Motivo);
        Assert.Equal(CategoriaDeError.Validacion, invalido.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public async Task El_encargado_obtiene_una_de_su_sucursal_y_no_una_de_otra_ni_un_reporte_y_la_administracion_ve_el_reporte()
    {
        var cliente = new EntornoDeDevoluciones();
        var propia = await SolicitarImpreso(cliente, "lima-central");
        var deOtra = await SolicitarImpreso(cliente, "arequipa");
        var reporte = await Reportar(cliente);
        var encargado = Como(ActorDePrueba.DeEncargadoDe(SucursalDeLima), cliente);
        var admin = Como(ActorDePrueba.DeAdministradorCentral(), cliente);

        var suya = await encargado.Consultas.Obtener(propia);
        var otra = await encargado.Consultas.Obtener(deOtra);
        var elReporte = await encargado.Consultas.Obtener(reporte);
        var inexistente = await encargado.Consultas.Obtener(Guid.NewGuid());
        var adminVeElReporte = await admin.Consultas.Obtener(reporte);

        Assert.True(suya.EsExito);
        Assert.All(new[] { otra, elReporte, inexistente }, r => Assert.Equal(CategoriaDeError.NoEncontrado, r.Error!.Categoria));
        Assert.Equal(3, encargado.Denegaciones.Eventos.Count);
        Assert.Equal(OrigenDeLaDevolucion.Reporte, adminVeElReporte.Valor.Origen);
    }

    // ---------- La administración ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-20")]
    public async Task La_administracion_ve_todas_con_los_reportes_y_el_reembolso_pendiente_de_cada_linea_y_filtra_por_tipo_y_reporte()
    {
        var cliente = new EntornoDeDevoluciones();
        var impreso = await SolicitarImpreso(cliente, "lima-central");
        var reporte = await Reportar(cliente);
        var admin = Como(ActorDePrueba.DeAdministradorCentral(), cliente);
        admin.Reembolsos.ProcesarResponde = BabelHub.Ecommerce.Application.Pagos.EstadoDelReembolso.Pendiente;
        Assert.True((await admin.Gestion.Aprobar(impreso)).EsExito);
        Assert.True((await admin.Recepcion.Ejecutar(impreso)).EsExito);

        var todas = await admin.Consultas.ListarDeLaAdministracion(new ListarDevolucionesDeLaAdministracionEntrada());
        var soloReportes = await admin.Consultas.ListarDeLaAdministracion(new ListarDevolucionesDeLaAdministracionEntrada(SoloReportes: true));
        var digitales = await admin.Consultas.ListarDeLaAdministracion(new ListarDevolucionesDeLaAdministracionEntrada(Tipo: TipoDeLaDevolucion.Digital));

        Assert.Equal(2, todas.Valor.Total);
        Assert.True(todas.Valor.Devoluciones.Single(d => d.DevolucionId == impreso).Lineas.All(l => l.ReembolsoPendiente));
        Assert.Equal(reporte, Assert.Single(soloReportes.Valor.Devoluciones).DevolucionId);
        Assert.Equal(reporte, Assert.Single(digitales.Valor.Devoluciones).DevolucionId);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-12")]
    [InlineData("encargado")]
    [InlineData("cliente")]
    [InlineData("anonimo")]
    public async Task Solo_la_administracion_central_lista_todas_y_un_tipo_invalido_es_validacion(string quien)
    {
        var a = new EntornoDeDevoluciones();
        await SolicitarImpreso(a, "lima-central");
        var actor = quien switch
        {
            "encargado" => ActorDePrueba.DeEncargadoDe(SucursalDeLima),
            "cliente" => ActorDePrueba.DeCliente(),
            _ => ActorDePrueba.Anonimo(),
        };
        var otro = Como(actor, a);
        var admin = Como(ActorDePrueba.DeAdministradorCentral(), a);

        var resultado = await otro.Consultas.ListarDeLaAdministracion(new ListarDevolucionesDeLaAdministracionEntrada());
        var invalido = await admin.Consultas.ListarDeLaAdministracion(new ListarDevolucionesDeLaAdministracionEntrada(Tipo: (TipoDeLaDevolucion)99));

        Assert.True(resultado.Error!.Categoria is CategoriaDeError.Prohibido or CategoriaDeError.NoAutenticado);
        Assert.Single(otro.Denegaciones.Eventos);
        Assert.Equal(CategoriaDeError.Validacion, invalido.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public async Task El_limite_de_consultas_se_aplica_antes_de_comprobar_la_sucursal_de_la_ruta_y_no_deja_un_evento_por_intento()
    {
        var cliente = new EntornoDeDevoluciones();
        await SolicitarImpreso(cliente, "arequipa");
        var encargado = Como(ActorDePrueba.DeEncargadoDe(SucursalDeLima), cliente);
        encargado.Limites.Maximos["consultas-de-devoluciones-por-usuario"] = 0;

        var resultado = await encargado.Consultas.ListarDeLaSucursal("arequipa", new ListarDevolucionesEntrada());

        Assert.Equal(CategoriaDeError.Limite, resultado.Error!.Categoria);
        Assert.Empty(encargado.Denegaciones.Eventos);
    }
}
