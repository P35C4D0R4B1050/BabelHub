using System.Globalization;
using BabelHub.Inventario.Application.Ajustes;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.IntegrationTests.Reservas;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.CargaInicial;

/// <summary>
/// La carga inicial del inventario contra PostgreSQL real, los módulos reales de Fuentes y Ajustes y el motor de límites real (transversal-carga-inicial, AC-CIN-09, AC-CIN-10, AC-CIN-11): las sucursales, las rutas, el conocimiento de la
/// edición y el stock por sus propias operaciones, repetir y ejecutar dos veces a la vez sin duplicar, una merma previa respetada y el presupuesto de 60 ajustes por hora real (se detiene, informa cuándo y se reanuda o espera).
/// </summary>
public sealed class CargaInicialDelInventarioPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private const string Sucursales = "codigo;nombre;region\nlima;Lima Centro;lima\nayacucho;Ayacucho;ayacucho\ncusco;Cusco;cusco\n";

    private const string Rutas = "origen;destino;dias\nlima;ayacucho;3\nayacucho;lima;3\nlima;cusco;2\n";

    private static readonly ActorActual Operacion = ActorActual.DeUsuario(Guid.Parse(IdentidadesDeOperacion.CargaInicial), RolesDeUsuario.AdministradorCentral);

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Plan(IEnumerable<(Guid Id, string Isbn, (string Codigo, int Cantidad)[] Stock)> ediciones) =>
        "{\"version\":1,\"ediciones\":[" + string.Join(",", ediciones.Select(e => "{\"edicionId\":\"" + e.Id.ToString("D") + "\",\"isbn\":\"" + e.Isbn + "\",\"stock\":{" + string.Join(",", e.Stock.Select(s => "\"" + s.Codigo + "\":" + s.Cantidad.ToString(CultureInfo.InvariantCulture))) + "}}")) + "]}";

    private static (Guid Id, string Isbn, (string, int)[] Stock) Edicion(int n, params (string, int)[] stock) => (new Guid(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]), "9790000" + n.ToString("D5", CultureInfo.InvariantCulture) + "0", stock);

    private static string PlanBasico() => Plan([Edicion(1, ("lima", 12), ("ayacucho", 8), ("cusco", 5)), Edicion(2, ("lima", 25))]);

    private static Task<Resultado<ResumenDeCargaDeInventario>> Cargar(PilaDeReservas pila, ActorActual actor, SolicitudDeCargaDeInventario solicitud) =>
        pila.EnUnAmbito(actor, sp => sp.GetRequiredService<ICargaInicialDelInventario>().Cargar(solicitud));

    private async Task<int> Fisico(string codigo, Guid edicion) =>
        (int)(await bd.Consultar<int?>($"SELECT s.fisico FROM fuentes.stock s JOIN fuentes.fuentes f ON f.id = s.fuente_id WHERE f.codigo = '{codigo}' AND s.edicion_id = '{edicion}'") ?? -1);

    private async Task<long> Contar(string tabla) => await bd.Consultar<long>($"SELECT count(*) FROM {tabla}");

    [Fact]
    [Trait("Criterio", "AC-CIN-09")]
    public async Task En_una_base_sin_sucursales_deja_las_sucursales_las_rutas_las_fichas_y_el_stock_declarado_y_repetirla_no_cambia_nada()
    {
        using var pila = new PilaDeReservas(bd);
        var solicitud = new SolicitudDeCargaDeInventario(PlanBasico(), Sucursales, Rutas, false);

        var primera = await Cargar(pila, Operacion, solicitud);

        Assert.True(primera.EsExito, primera.Error?.Clave);
        var resumen = primera.Valor;
        Assert.Equal((3, 3, 2, 4, 0, 0), (resumen.SucursalesNuevas, resumen.RutasNuevas, resumen.EdicionesRegistradas, resumen.StockCargado, resumen.YaCargado, resumen.Rechazadas));
        Assert.True(resumen.Completa);
        Assert.Equal(3L, await Contar("fuentes.fuentes"));
        Assert.True(await bd.Consultar<bool>("SELECT es_recepcion FROM fuentes.fuentes WHERE codigo = 'lima'"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.fuentes WHERE es_recepcion"));
        Assert.Equal(3L, await Contar("fuentes.rutas"));
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.ediciones WHERE formato = 'impreso'"));
        var (a, b) = (Edicion(1, ("lima", 1)).Id, Edicion(2, ("lima", 1)).Id);
        Assert.Equal((12, 8, 5, 25), (await Fisico("lima", a), await Fisico("ayacucho", a), await Fisico("cusco", a), await Fisico("lima", b)));

        // El ingreso entra por la recepción y las correcciones de conteo por las demás, cada uno con su entrada de auditoría y la referencia de la carga.
        Assert.Equal(2L, await Contar("ajustes.ingresos"));
        Assert.Equal(4L, await Contar("ajustes.ajustes"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM ajustes.ajustes WHERE motivo = 'correccion-de-conteo' AND fuente_id = (SELECT id FROM fuentes.fuentes WHERE codigo = 'cusco')"));
        Assert.Equal((3, 3, 1), (pila.Auditoria.Registros.Count(r => r.Accion == "FuenteRegistrada"), pila.Auditoria.Registros.Count(r => r.Accion == "RutaRegistrada"), pila.Auditoria.Registros.Count(r => r.Accion == "CargaInicialEjecutada")));

        var segunda = await Cargar(pila, Operacion, solicitud);

        var repetida = segunda.Valor;
        Assert.Equal((0, 3, 0, 3, 0, 0, 4, 0), (repetida.SucursalesNuevas, repetida.SucursalesExistentes, repetida.RutasNuevas, repetida.RutasExistentes, repetida.StockCargado, repetida.EdicionesRegistradas, repetida.YaCargado, repetida.Rechazadas));
        Assert.True(repetida.Completa);
        Assert.Equal((3L, 3L, 2L, 4L), (await Contar("fuentes.fuentes"), await Contar("fuentes.rutas"), await Contar("ajustes.ingresos"), await Contar("ajustes.ajustes")));
        Assert.Equal((12, 8, 5, 25), (await Fisico("lima", a), await Fisico("ayacucho", a), await Fisico("cusco", a), await Fisico("lima", b)));
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-09")]
    public async Task Una_ruta_y_una_sucursal_existentes_con_otros_datos_no_se_modifican()
    {
        using var pila = new PilaDeReservas(bd);
        await bd.AgregarFuente("lima", region: "callao", recepcion: true);
        var lima = PostgresDeFuentesFixtureId("lima");
        var ayacucho = await bd.AgregarFuente("ayacucho");
        await bd.Ejecutar($"INSERT INTO fuentes.rutas (origen_id, destino_id, plazo_dias) VALUES ('{lima}', '{ayacucho}', 9)");

        var resultado = await Cargar(pila, Operacion, new SolicitudDeCargaDeInventario(PlanBasico(), Sucursales, Rutas, false));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((1, 2, 2, 1), (resultado.Valor.SucursalesNuevas, resultado.Valor.SucursalesExistentes, resultado.Valor.RutasNuevas, resultado.Valor.RutasExistentes));
        Assert.Equal("callao", await bd.Consultar<string>("SELECT region FROM fuentes.fuentes WHERE codigo = 'lima'"));
        Assert.Equal(9, await bd.Consultar<int>($"SELECT plazo_dias FROM fuentes.rutas WHERE origen_id = '{lima}' AND destino_id = '{ayacucho}'"));
    }

    private static Guid PostgresDeFuentesFixtureId(string codigo) => BabelHub.IntegrationTests.Fuentes.PostgresDeFuentesFixture.IdDeFuente(codigo);

    [Fact]
    [Trait("Criterio", "AC-CIN-09")]
    public async Task Dos_ejecuciones_a_la_vez_no_duplican_sucursales_rutas_ni_stock()
    {
        using var pila = new PilaDeReservas(bd);
        var solicitud = new SolicitudDeCargaDeInventario(PlanBasico(), Sucursales, Rutas, false);

        var resultados = await Task.WhenAll(Cargar(pila, Operacion, solicitud), Cargar(pila, Operacion, solicitud));

        Assert.All(resultados, r => Assert.True(r.EsExito, r.Error?.Clave));
        Assert.Equal(3L, await Contar("fuentes.fuentes"));
        Assert.Equal(3L, await Contar("fuentes.rutas"));
        var (a, b) = (Edicion(1, ("lima", 1)).Id, Edicion(2, ("lima", 1)).Id);
        Assert.Equal((12, 8, 5, 25), (await Fisico("lima", a), await Fisico("ayacucho", a), await Fisico("cusco", a), await Fisico("lima", b)));
        Assert.Equal(2L, await Contar("ajustes.ingresos"));
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-10")]
    public async Task Una_sucursal_con_una_merma_previa_no_se_modifica_aunque_haya_quedado_en_cero()
    {
        using var pila = new PilaDeReservas(bd);
        var solicitud = new SolicitudDeCargaDeInventario(PlanBasico(), Sucursales, Rutas, false);
        await Cargar(pila, Operacion, solicitud);
        var a = Edicion(1, ("lima", 1)).Id;
        var merma = await pila.Ejecutar<RegistrarAjusteManualEntrada, AjusteRegistrado>(Operacion, new RegistrarAjusteManualEntrada("cusco", a, "merma", 5, null, "caja mojada", Guid.NewGuid().ToString("N")));
        Assert.True(merma.EsExito, merma.Error?.Clave);
        Assert.Equal(0, await Fisico("cusco", a));

        var repetida = await Cargar(pila, Operacion, solicitud);

        Assert.True(repetida.EsExito);
        Assert.Equal(0, await Fisico("cusco", a));
        Assert.Equal(4, repetida.Valor.YaCargado);
        Assert.Equal(0, repetida.Valor.StockCargado);
    }

    private static string PlanDe31() => Plan(Enumerable.Range(1, 31).Select(n => Edicion(n, ("lima", 3 + n), ("cusco", 3 + n))));

    [Fact]
    [Trait("Criterio", "AC-CIN-11")]
    public async Task Con_el_presupuesto_de_60_por_hora_real_se_detiene_informa_cuando_continuar_y_al_repetir_completa_sin_duplicar()
    {
        using var pila = new PilaDeReservas(bd);
        var solicitud = new SolicitudDeCargaDeInventario(PlanDe31(), Sucursales, Rutas, false);

        var primera = await Cargar(pila, Operacion, solicitud);

        Assert.True(primera.EsExito, primera.Error?.Clave);
        Assert.False(primera.Valor.Completa);
        Assert.Equal(60, primera.Valor.StockCargado);
        Assert.Equal(2, primera.Valor.PendientesPorLimite);
        Assert.Equal(0, primera.Valor.Rechazadas);
        Assert.NotNull(primera.Valor.ReanudaDesde);
        Assert.Contains(pila.Eventos.Eventos, e => e.Tipo == TipoDeEventoDeSeguridad.LimiteExcedido);

        // El motor de límites cuenta con el reloj de la base: pasada la hora se simula envejeciendo sus eventos (no se espera una hora real).
        await bd.Ejecutar("UPDATE plataforma.eventos_limite SET instante = instante - interval '2 hours'");
        var segunda = await Cargar(pila, Operacion, solicitud);

        Assert.True(segunda.Valor.Completa, $"pend={segunda.Valor.PendientesPorLimite} rech={segunda.Valor.Rechazadas} cargado={segunda.Valor.StockCargado} ya={segunda.Valor.YaCargado} reg={segunda.Valor.RegistroDeLaCorridaFallo} desde={segunda.Valor.ReanudaDesde} ahora={pila.Reloj.Ahora:O} prob={string.Join(";", segunda.Valor.Problemas)}");
        Assert.True(segunda.Valor.Completa, string.Join(";", segunda.Valor.Problemas));
        Assert.Equal(2, segunda.Valor.StockCargado);
        Assert.Equal(60, segunda.Valor.YaCargado);
        Assert.Equal(31L, await Contar("ajustes.ingresos"));
        Assert.Equal(31L, await Contar("ajustes.ajustes") - 31L);
        Assert.Equal(62L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.stock WHERE fisico > 0"));
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-11")]
    public async Task Con_esperar_aguarda_a_que_se_libere_el_presupuesto_y_termina_en_una_sola_orden()
    {
        using var pila = new PilaDeReservas(bd);
        var esperas = new List<TimeSpan>();
        var solicitud = new SolicitudDeCargaDeInventario(
            PlanDe31(),
            Sucursales,
            Rutas,
            true,
            async (tiempo, _) =>
            {
                esperas.Add(tiempo);
                await bd.Ejecutar("UPDATE plataforma.eventos_limite SET instante = instante - interval '2 hours'");
            });

        var resultado = await Cargar(pila, Operacion, solicitud);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.True(resultado.Valor.Completa, string.Join(";", resultado.Valor.Problemas));
        Assert.Equal(62, resultado.Valor.StockCargado);
        Assert.NotEmpty(esperas);
        Assert.True(esperas.Aggregate(TimeSpan.Zero, (a, b) => a + b) <= TimeSpan.FromHours(2));
        Assert.Equal(62L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.stock WHERE fisico > 0"));
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-09")]
    public async Task Quien_no_es_la_administracion_central_se_rechaza_y_no_se_escribe_nada()
    {
        using var pila = new PilaDeReservas(bd);

        var resultado = await Cargar(pila, PilaDeReservas.Cliente(), new SolicitudDeCargaDeInventario(PlanBasico(), Sucursales, Rutas, false));

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(0L, await Contar("fuentes.fuentes"));
        Assert.Equal(0L, await Contar("ajustes.ingresos"));
    }
}
