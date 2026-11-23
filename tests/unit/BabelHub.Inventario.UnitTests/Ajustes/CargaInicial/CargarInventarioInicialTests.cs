using BabelHub.Inventario.Application.Ajustes;
using BabelHub.Inventario.Application.Ajustes.Interno;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.UnitTests.Fuentes;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Ajustes.CargaInicial;

/// <summary>La carga inicial del inventario con dobles de los casos de uso de Fuentes y de Ajustes (CIN-08 a CIN-14): quién llama a qué, con qué clave, y qué cuenta como «ya cargada», pendiente o rechazada.</summary>
public sealed class CargarInventarioInicialTests
{
    private static readonly Guid EdicionA = Guid.Parse("3f2b8c1e-5a47-4d0e-9b6a-0c1d2e3f4a51");

    private static readonly Guid EdicionB = Guid.Parse("7a9d4e20-1c3b-4f58-8e2d-6b5a4c3d2e12");

    private const string Sucursales = "codigo;nombre;region\nlima;Lima Centro;lima\nayacucho;Ayacucho;ayacucho\ncusco;Cusco;cusco\n";

    private const string Rutas = "origen;destino;dias\nlima;ayacucho;3\nayacucho;lima;3\n";

    private const string Plan = """{"version":1,"ediciones":[{"edicionId":"3f2b8c1e-5a47-4d0e-9b6a-0c1d2e3f4a51","isbn":"9790000000018","stock":{"lima":12,"ayacucho":8,"cusco":5}},{"edicionId":"7a9d4e20-1c3b-4f58-8e2d-6b5a4c3d2e12","isbn":"9790000000025","stock":{"lima":25}}]}""";

    private sealed class Doble<TE, TS>(Func<TE, Resultado<TS>> respuesta) : ICasoDeUso<TE, TS>
    {
        public List<TE> Llamadas { get; } = [];

        public Task<Resultado<TS>> Ejecutar(TE entrada, CancellationToken cancelacion = default)
        {
            Llamadas.Add(entrada);
            return Task.FromResult(respuesta(entrada));
        }
    }

    private sealed class DirectorioDoble : IDirectorioDeFuentes
    {
        public Dictionary<string, FuenteDeStock> Fuentes { get; } = new(StringComparer.Ordinal);

        public HashSet<(Guid, Guid)> Rutas { get; } = [];

        public Task<FuenteDeStock?> PorId(Guid fuenteId, CancellationToken cancelacion = default) => Task.FromResult(Fuentes.Values.FirstOrDefault(f => f.Id == fuenteId));

        public Task<FuenteDeStock?> PorCodigo(string codigo, CancellationToken cancelacion = default) => Task.FromResult(Fuentes.GetValueOrDefault(codigo));

        public Task<IReadOnlyList<FuenteDeStock>> Listar(CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<FuenteDeStock>>([.. Fuentes.Values]);

        public Task<FuenteDeStock?> FuenteDeRecepcion(CancellationToken cancelacion = default) => Task.FromResult(Fuentes.Values.FirstOrDefault(f => f.EsRecepcion));

        public Task<IReadOnlyList<FuenteDeStock>> DeLaRegion(string region, CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<FuenteDeStock>>([.. Fuentes.Values.Where(f => f.Region == region)]);

        public Task<int?> PlazoDeRuta(Guid origenId, Guid destinoId, CancellationToken cancelacion = default) => Task.FromResult<int?>(Rutas.Contains((origenId, destinoId)) ? 3 : null);

        public Task<IReadOnlyList<RutaDeTraslado>> RutasHaciaDestino(Guid destinoId, CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<RutaDeTraslado>>([]);
    }

    private sealed class ExistenciasDobles : IExistencias
    {
        public HashSet<(Guid Fuente, Guid Edicion)> ConStock { get; } = [];

        public Task<IReadOnlyList<DisponibilidadDeEdicion>> Disponibilidad(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<DisponibilidadDeEdicion>>([]);

        public Task<PaginaDeExistencias> DeLaFuente(ConsultaDeExistenciasDeFuente consulta, CancellationToken cancelacion = default)
        {
            var filas = (consulta.EdicionIds ?? []).Where(e => ConStock.Contains((consulta.FuenteId, e))).Select(e => new ExistenciaEnFuente(consulta.FuenteId, "x", "x", e, 5, 0, 5, EstadoDeExistencia.Normal)).ToList();
            return Task.FromResult(new PaginaDeExistencias(filas, filas.Count, consulta.Pagina, consulta.Tamano));
        }
    }

    private sealed class Entorno
    {
        public Entorno(ActorDePrueba? actor = null)
        {
            Actor = actor ?? ActorDePrueba.DeAdministradorCentral();
            Denegaciones = new DenegacionesEspia();
            Auditoria = new AuditoriaEspia();
            Fuentes = new Doble<RegistrarFuenteEntrada, DetalleDeFuente>(e =>
            {
                var fuente = new FuenteDeStock(Guid.NewGuid(), e.Codigo, e.Nombre, e.Region, Directorio.Fuentes.Count == 0);
                Directorio.Fuentes[e.Codigo] = fuente;
                return Resultado<DetalleDeFuente>.Exito(new DetalleDeFuente(fuente.Id, e.Codigo, e.Nombre, e.Region, null, fuente.EsRecepcion, 0, 0, 0));
            });
            RutasAlta = new Doble<RegistrarRutaEntrada, RutaDefinida>(e =>
            {
                Directorio.Rutas.Add((Directorio.Fuentes[e.CodigoDeOrigen].Id, Directorio.Fuentes[e.CodigoDeDestino].Id));
                return Resultado<RutaDefinida>.Exito(new RutaDefinida(e.CodigoDeOrigen, e.CodigoDeDestino, e.Dias, null));
            });
            Fichas = new Doble<RegistrarFichaDeEdicionEntrada, FichaDeEdicionGuardada>(e => Resultado<FichaDeEdicionGuardada>.Exito(new FichaDeEdicionGuardada(e.EdicionId, e.Formato, true)));
            Ingresos = new Doble<RegistrarIngresoDeMercaderiaEntrada, IngresoRegistrado>(e =>
                RespuestaDeIngreso?.Invoke(e) ?? Resultado<IngresoRegistrado>.Exito(new IngresoRegistrado(Guid.NewGuid(), Guid.NewGuid(), e.EdicionId, "lima", e.Cantidad, e.Referencia, e.Nota, null, DateTimeOffset.UnixEpoch, 0, e.Cantidad)));
            Ajustes = new Doble<RegistrarAjusteManualEntrada, AjusteRegistrado>(e =>
                RespuestaDeAjuste?.Invoke(e) ?? Resultado<AjusteRegistrado>.Exito(new AjusteRegistrado(Guid.NewGuid(), e.CodigoDeFuente, e.EdicionId, e.Motivo, null, DateTimeOffset.UnixEpoch, 0, e.ConteoReal ?? 0, e.Nota, null)));
            Historial = new Doble<ConsultarHistorialDeAjustesEntrada, PaginaDeAjustes>(e =>
            {
                var elementos = HistorialPorFuente.GetValueOrDefault(e.CodigoDeFuente) ?? [];
                return Resultado<PaginaDeAjustes>.Exito(new PaginaDeAjustes([.. elementos.Select(id => new AjusteRegistrado(Guid.NewGuid(), e.CodigoDeFuente, id, "merma", null, DateTimeOffset.UnixEpoch, 9, 7, null, null))], elementos.Count, e.Pagina, e.Tamano));
            });
            var autorizador = new AutorizadorDeAjustes(Actor, Denegaciones);
            var corrida = new RegistrarCorridaDeCargaInicial(autorizador, Auditoria);
            Caso = new CargarInventarioInicial(autorizador, Directorio, Existencias, Fuentes, RutasAlta, Fichas, Ingresos, Ajustes, Historial, new Doble<CorridaDeCargaInicial, bool>(c => corrida.Ejecutar(c).GetAwaiter().GetResult()));
        }

        public ActorDePrueba Actor { get; }

        public DenegacionesEspia Denegaciones { get; }

        public AuditoriaEspia Auditoria { get; }

        public DirectorioDoble Directorio { get; } = new();

        public ExistenciasDobles Existencias { get; } = new();

        public Dictionary<string, List<Guid>> HistorialPorFuente { get; } = new(StringComparer.Ordinal);

        public Doble<RegistrarFuenteEntrada, DetalleDeFuente> Fuentes { get; }

        public Doble<RegistrarRutaEntrada, RutaDefinida> RutasAlta { get; }

        public Doble<RegistrarFichaDeEdicionEntrada, FichaDeEdicionGuardada> Fichas { get; }

        public Doble<RegistrarIngresoDeMercaderiaEntrada, IngresoRegistrado> Ingresos { get; }

        public Doble<RegistrarAjusteManualEntrada, AjusteRegistrado> Ajustes { get; }

        public Doble<ConsultarHistorialDeAjustesEntrada, PaginaDeAjustes> Historial { get; }

        public Func<RegistrarIngresoDeMercaderiaEntrada, Resultado<IngresoRegistrado>?>? RespuestaDeIngreso { get; set; }

        public Func<RegistrarAjusteManualEntrada, Resultado<AjusteRegistrado>?>? RespuestaDeAjuste { get; set; }

        public CargarInventarioInicial Caso { get; }

        public Task<Resultado<ResumenDeCargaDeInventario>> Cargar(bool esperar = false, Func<TimeSpan, CancellationToken, Task>? espera = null, string plan = Plan) =>
            Caso.Cargar(new SolicitudDeCargaDeInventario(plan, Sucursales, Rutas, esperar, espera));
    }

    private static ErrorDeAplicacion LimiteAgotado(int espera = 600, string desde = "2026-11-23T20:00:00Z") =>
        new(CategoriaDeError.Limite, DetalleDeLimiteDeAjustes.ClaveDeLimiteExcedido, DetalleDeLimiteDeAjustes.Codificar(new RechazoDePresupuestoDelPersonal("ajustes-del-personal", espera, DateTimeOffset.Parse(desde, System.Globalization.CultureInfo.InvariantCulture))));

    [Fact]
    [Trait("Criterio", "AC-CIN-09")]
    public async Task Una_base_vacia_recibe_las_sucursales_las_rutas_las_fichas_y_el_stock_con_las_operaciones_del_Inventario()
    {
        var entorno = new Entorno();

        var resultado = await entorno.Cargar();

        Assert.True(resultado.EsExito);
        var resumen = resultado.Valor;
        Assert.Equal((3, 0, 2, 0, 2, 4, 0, 0), (resumen.SucursalesNuevas, resumen.SucursalesExistentes, resumen.RutasNuevas, resumen.RutasExistentes, resumen.EdicionesRegistradas, resumen.StockCargado, resumen.YaCargado, resumen.Rechazadas));
        Assert.True(resumen.Completa);
        Assert.Equal(["lima", "ayacucho", "cusco"], entorno.Fuentes.Llamadas.Select(f => f.Codigo));
        Assert.All(entorno.Fichas.Llamadas, f => Assert.Equal(FormatoDeEdicionRegistrado.Impreso, f.Formato));

        // La recepción (la primera sucursal registrada) recibe un ingreso de mercadería; las demás, una corrección de conteo con la clave de la carga.
        Assert.Equal([(EdicionA, 12), (EdicionB, 25)], entorno.Ingresos.Llamadas.Select(i => (i.EdicionId, i.Cantidad)));
        Assert.All(entorno.Ingresos.Llamadas, i => Assert.Null(i.CodigoDeFuente));
        Assert.All(entorno.Ingresos.Llamadas, i => Assert.Equal("carga inicial", i.Referencia));
        Assert.Equal([("ayacucho", 8), ("cusco", 5)], entorno.Ajustes.Llamadas.Select(a => (a.CodigoDeFuente, a.ConteoReal!.Value)));
        Assert.All(entorno.Ajustes.Llamadas, a => Assert.Equal("correccion-de-conteo", a.Motivo));
        Assert.Equal($"carga-inicial-{EdicionA:N}-ayacucho", entorno.Ajustes.Llamadas[0].ClaveDeIdempotencia);
        Assert.InRange(entorno.Ajustes.Llamadas[0].ClaveDeIdempotencia.Length, 16, 80);
        Assert.Equal(64, resumen.HuellaDelPlan.Length);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-06")]
    public async Task La_corrida_queda_en_la_auditoria_con_la_huella_del_plan_y_los_conteos_y_sin_datos_personales()
    {
        var entorno = new Entorno();

        var resultado = await entorno.Cargar();

        var registro = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.CargaInicialEjecutada, registro.Accion);
        Assert.Equal(resultado.Valor.CorridaId.ToString("D"), registro.Detalle["corridaId"]);
        Assert.Equal(resultado.Valor.HuellaDelPlan, registro.Detalle["huellaSha256"]);
        Assert.Equal("4", registro.Detalle["cargadas"]);
        Assert.Equal("0", registro.Detalle["rechazadas"]);
        Assert.False(resultado.Valor.RegistroDeLaCorridaFallo);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-06")]
    public async Task Con_la_auditoria_caida_lo_cargado_se_conserva_y_la_carga_lo_informa()
    {
        var entorno = new Entorno();
        entorno.Auditoria.Falla = true;

        var resultado = await entorno.Cargar();

        Assert.True(resultado.EsExito);
        Assert.True(resultado.Valor.RegistroDeLaCorridaFallo);
        Assert.False(resultado.Valor.Completa);
        Assert.Equal(4, resultado.Valor.StockCargado);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-09")]
    public async Task Repetir_la_carga_no_registra_ni_modifica_las_sucursales_ni_las_rutas_existentes()
    {
        var entorno = new Entorno();
        await entorno.Cargar();
        var fuentes = entorno.Fuentes.Llamadas.Count;
        var rutas = entorno.RutasAlta.Llamadas.Count;
        entorno.Existencias.ConStock.UnionWith(entorno.Ingresos.Llamadas.Select(i => (entorno.Directorio.Fuentes["lima"].Id, i.EdicionId)));
        entorno.Existencias.ConStock.UnionWith(entorno.Ajustes.Llamadas.Select(a => (entorno.Directorio.Fuentes[a.CodigoDeFuente].Id, a.EdicionId)));
        entorno.Ingresos.Llamadas.Clear();
        entorno.Ajustes.Llamadas.Clear();

        var resultado = await entorno.Cargar();

        var resumen = resultado.Valor;
        Assert.Equal((0, 3, 0, 2), (resumen.SucursalesNuevas, resumen.SucursalesExistentes, resumen.RutasNuevas, resumen.RutasExistentes));
        Assert.Equal((0, 4), (resumen.StockCargado, resumen.YaCargado));
        Assert.Equal(fuentes, entorno.Fuentes.Llamadas.Count);
        Assert.Equal(rutas, entorno.RutasAlta.Llamadas.Count);
        Assert.Empty(entorno.Ingresos.Llamadas);
        Assert.Empty(entorno.Ajustes.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-10")]
    public async Task Una_sucursal_con_un_ajuste_previo_de_la_edicion_no_se_modifica_y_el_resto_sigue()
    {
        var entorno = new Entorno();
        await entorno.Cargar();
        entorno.Ingresos.Llamadas.Clear();
        entorno.Ajustes.Llamadas.Clear();
        entorno.HistorialPorFuente["ayacucho"] = [EdicionA];

        var resultado = await entorno.Cargar();

        // Ayacucho/A tiene una merma previa (y el doble de existencias dice vacío): no se toca; Lima y Cusco no tienen movimientos en el doble y se cargan.
        Assert.Equal(1, resultado.Valor.YaCargado);
        Assert.DoesNotContain(entorno.Ajustes.Llamadas, a => a.CodigoDeFuente == "ayacucho");
        Assert.Contains(entorno.Ajustes.Llamadas, a => a.CodigoDeFuente == "cusco");
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-10")]
    public async Task Una_sucursal_con_existencias_fisicas_o_reservadas_no_se_modifica()
    {
        var entorno = new Entorno();
        await entorno.Cargar();
        entorno.Ingresos.Llamadas.Clear();
        entorno.Ajustes.Llamadas.Clear();
        entorno.Existencias.ConStock.Add((entorno.Directorio.Fuentes["cusco"].Id, EdicionA));

        var resultado = await entorno.Cargar();

        Assert.DoesNotContain(entorno.Ajustes.Llamadas, a => a.CodigoDeFuente == "cusco");
        Assert.True(resultado.Valor.YaCargado >= 1);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-11")]
    public async Task Con_el_presupuesto_agotado_se_detiene_sin_perder_lo_cargado_e_informa_cuando_continuar()
    {
        var entorno = new Entorno();
        entorno.RespuestaDeAjuste = a => a.CodigoDeFuente == "cusco" ? Resultado<AjusteRegistrado>.Fallo(LimiteAgotado()) : null;

        var resultado = await entorno.Cargar();

        Assert.True(resultado.EsExito);
        var resumen = resultado.Valor;
        Assert.False(resumen.Completa);
        Assert.Equal(DateTimeOffset.Parse("2026-11-23T20:00:00Z", System.Globalization.CultureInfo.InvariantCulture), resumen.ReanudaDesde);
        Assert.Equal(2, resumen.PendientesPorLimite);
        Assert.Equal(0, resumen.Rechazadas);
        Assert.Equal(2, resumen.StockCargado);
        Assert.Single(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-11")]
    public async Task Con_esperar_espera_a_que_se_libere_el_presupuesto_y_continua()
    {
        var entorno = new Entorno();
        var intentos = 0;
        entorno.RespuestaDeAjuste = a => a.CodigoDeFuente == "cusco" && intentos++ == 0 ? Resultado<AjusteRegistrado>.Fallo(LimiteAgotado(espera: 1800)) : null;
        var esperas = new List<TimeSpan>();

        var resultado = await entorno.Cargar(esperar: true, espera: (tiempo, _) =>
        {
            esperas.Add(tiempo);
            return Task.CompletedTask;
        });

        Assert.Equal([TimeSpan.FromMinutes(30)], esperas);
        Assert.True(resultado.Valor.Completa);
        Assert.Equal(4, resultado.Valor.StockCargado);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-11")]
    public async Task Con_esperar_el_total_de_espera_nunca_pasa_de_2_horas()
    {
        var entorno = new Entorno();
        entorno.RespuestaDeAjuste = a => a.CodigoDeFuente == "cusco" ? Resultado<AjusteRegistrado>.Fallo(LimiteAgotado(espera: 3600)) : null;
        var esperas = new List<TimeSpan>();

        var resultado = await entorno.Cargar(esperar: true, espera: (tiempo, _) =>
        {
            esperas.Add(tiempo);
            return Task.CompletedTask;
        });

        Assert.Equal(2, esperas.Count);
        Assert.Equal(TimeSpan.FromHours(2), esperas.Aggregate(TimeSpan.Zero, (a, b) => a + b));
        Assert.Equal(2, resultado.Valor.PendientesPorLimite);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-09")]
    public async Task Un_rechazo_de_una_operacion_no_detiene_las_demas_y_se_informa_solo_con_su_clave()
    {
        var entorno = new Entorno();
        entorno.RespuestaDeAjuste = a => a.CodigoDeFuente == "ayacucho" ? Resultado<AjusteRegistrado>.Fallo(new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, "edicion-no-impresa")) : null;

        var resultado = await entorno.Cargar();

        var resumen = resultado.Valor;
        Assert.Equal(1, resumen.Rechazadas);
        Assert.Equal(3, resumen.StockCargado);
        Assert.Equal(0, resumen.PendientesPorLimite);
        Assert.False(resumen.Completa);
        Assert.Contains("edicion-no-impresa", Assert.Single(resumen.Problemas));
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("sucursales")]
    [InlineData("rutas")]
    [Trait("Criterio", "AC-CIN-08")]
    public async Task Un_archivo_invalido_se_rechaza_antes_de_escribir_nada(string cual)
    {
        var entorno = new Entorno();
        var solicitud = cual switch
        {
            "plan" => new SolicitudDeCargaDeInventario("""{"version":2,"ediciones":[]}""", Sucursales, Rutas, false),
            "sucursales" => new SolicitudDeCargaDeInventario(Plan, "codigo;nombre\nlima;Lima\n", Rutas, false),
            _ => new SolicitudDeCargaDeInventario(Plan, Sucursales, "origen;destino;dias\nlima;tacna;2\n", false),
        };

        var resultado = await entorno.Caso.Cargar(solicitud);

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Empty(entorno.Fuentes.Llamadas);
        Assert.Empty(entorno.RutasAlta.Llamadas);
        Assert.Empty(entorno.Fichas.Llamadas);
        Assert.Empty(entorno.Ingresos.Llamadas);
        Assert.Empty(entorno.Ajustes.Llamadas);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-09")]
    public async Task Quien_no_es_la_administracion_central_se_rechaza_y_no_se_escribe_nada()
    {
        var entorno = new Entorno(ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));

        var resultado = await entorno.Cargar();

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Single(entorno.Denegaciones.Eventos);
        Assert.Empty(entorno.Fuentes.Llamadas);
        Assert.Empty(entorno.Ingresos.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-09")]
    public async Task Sin_actor_la_carga_no_es_autenticada()
    {
        var entorno = new Entorno(ActorDePrueba.SinContexto());

        var resultado = await entorno.Cargar();

        Assert.Equal(CategoriaDeError.NoAutenticado, resultado.Error!.Categoria);
    }
}
