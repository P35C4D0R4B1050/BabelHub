using System.Text;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Auditoria;

/// <summary>
/// La lista unificada de auditoría (T17; AUD-07, AUD-09, AUD-17, AUD-18, AUD-27, AUD-30): un solo orden entre las tres fuentes, páginas de 20, un cursor firmado
/// ligado al usuario y a los filtros, el rol revalidado en cada página y el Inventario caído sin romper la lista.
/// </summary>
public sealed class ConsultasUnificadasTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.Parse("a0a0a0a0-0000-4000-8000-000000000001");

    private sealed class Entorno
    {
        public Entorno(ActorDePrueba? actor = null)
        {
            Reloj = new RelojFijo(Ahora);
            Actor = actor ?? ActorDePrueba.DeAdministradorCentral(Admin);
            Firmador = new FirmadorDeCursor(new ClavesDeCursorFalsas(), Reloj);
            Identidad.Pagina = new([], null);
            Consulta = new ConsultasDeAuditoriaUnificadas(
                Actor,
                CatalogoDeAccionesDeAuditoria.Crear(),
                Firmador,
                new LocalFalso(Propia),
                new IdentidadFalsa(Aislada),
                Inventario);
        }

        public RelojFijo Reloj { get; }

        public ActorDePrueba Actor { get; }

        public FirmadorDeCursor Firmador { get; }

        public FuentePaginadaFalsa Propia { get; } = new();

        public FuentePaginadaFalsa Aislada { get; } = new();

        public FuenteAisladaFalsa Identidad { get; } = new();

        public ClienteDelInventarioFalso Inventario { get; } = new();

        public ConsultasDeAuditoriaUnificadas Consulta { get; }

        public async Task<List<RegistroAuditado>> TodasLasPaginas(FiltroDeAuditoria? filtro = null)
        {
            var todos = new List<RegistroAuditado>();
            string? cursor = null;
            var paginas = 0;
            do
            {
                var resultado = await Consulta.Consultar(new ConsultaDeAuditoria(filtro ?? new FiltroDeAuditoria(), cursor));
                Assert.True(resultado.EsExito, resultado.Error?.Clave);
                Assert.True(resultado.Valor.Registros.Count <= PaginaDeRegistros.TamanoDePagina);
                todos.AddRange(resultado.Valor.Registros);
                cursor = resultado.Valor.SiguienteCursor;
                Assert.True(++paginas < 50, "la paginación no termina");
            }
            while (cursor is not null);

            return todos;
        }
    }

    private sealed class LocalFalso(FuentePaginadaFalsa fuente) : IConsultaLocalDeRegistros
    {
        public Task<PaginaDeRegistros> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default) => fuente.Consultar(consulta, cancelacion);
    }

    private sealed class IdentidadFalsa(FuentePaginadaFalsa fuente) : IFuenteDeRegistrosAislados
    {
        public Task<PaginaDeRegistros> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default) => fuente.Consultar(consulta, cancelacion);

        public Task<int> Contar(SolicitudDeConteo solicitud, CancellationToken cancelacion = default) => Task.FromResult(0);
    }

    private static void Llenar(Entorno e, int propios, int aislados, int deInventario)
    {
        for (var i = 0; i < propios; i++)
        {
            e.Propia.Agregar(RegistrosDePrueba.De(ServicioAuditado.Ecommerce, Ahora.AddSeconds(-(i * 7))));
        }

        for (var i = 0; i < aislados; i++)
        {
            e.Aislada.Agregar(RegistrosDePrueba.De(ServicioAuditado.Ecommerce, Ahora.AddSeconds(-(i * 5) - 1)));
        }

        for (var i = 0; i < deInventario; i++)
        {
            e.Inventario.Fuente.Agregar(RegistrosDePrueba.De(ServicioAuditado.Inventario, Ahora.AddSeconds(-(i * 3) - 2), sucursal: i % 2 == 0 ? "00000000-0000-4000-8000-000000000001" : null));
        }
    }

    // ---------- Orden y paginación ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-15")]
    public async Task LaListaFusionaLasTresFuentesEnUnSoloOrden_EnPaginasDeVeinte_SinRepetirNiOmitir()
    {
        var e = new Entorno();
        Llenar(e, propios: 37, aislados: 25, deInventario: 41);

        var todos = await e.TodasLasPaginas();

        Assert.Equal(37 + 25 + 41, todos.Count);
        Assert.Equal(todos.Count, todos.Select(r => r.Id).Distinct().Count());
        Assert.Equal(todos.OrderBy(r => r, OrdenDeLaLista.Instancia).Select(r => r.Id), todos.Select(r => r.Id));
        Assert.Contains(todos, r => r.Servicio == ServicioAuditado.Inventario);
        Assert.Contains(todos, r => r.Servicio == ServicioAuditado.Ecommerce);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-15")]
    public async Task AnteIgualFecha_ElDesempateEsServicioSucursalDeOrigenSinSucursalAlFinalEIdentificador()
    {
        var e = new Entorno();
        var a = Guid.Parse("00000000-0000-4000-8000-00000000000a");
        var b = Guid.Parse("00000000-0000-4000-8000-00000000000b");
        var sucursal1 = "11111111-1111-4111-8111-111111111111";
        var sucursal2 = "22222222-2222-4222-8222-222222222222";
        e.Propia.Agregar(RegistrosDePrueba.De(ServicioAuditado.Ecommerce, Ahora, id: b));
        e.Aislada.Agregar(RegistrosDePrueba.De(ServicioAuditado.Ecommerce, Ahora, id: a));
        e.Inventario.Fuente.Agregar(RegistrosDePrueba.De(ServicioAuditado.Inventario, Ahora, sucursal2, Guid.Parse("00000000-0000-4000-8000-000000000001")));
        e.Inventario.Fuente.Agregar(RegistrosDePrueba.De(ServicioAuditado.Inventario, Ahora, sucursal1, Guid.Parse("00000000-0000-4000-8000-000000000002")));
        e.Inventario.Fuente.Agregar(RegistrosDePrueba.De(ServicioAuditado.Inventario, Ahora, null, Guid.Parse("00000000-0000-4000-8000-000000000003")));

        var resultado = await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()));

        Assert.Equal(
            [a, b, Guid.Parse("00000000-0000-4000-8000-000000000002"), Guid.Parse("00000000-0000-4000-8000-000000000001"), Guid.Parse("00000000-0000-4000-8000-000000000003")],
            resultado.Valor.Registros.Select(r => r.Id));
        Assert.Null(resultado.Valor.SiguienteCursor);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-27")]
    public async Task VeinteRegistrosNuevosMientrasSeMiraLaPrimeraPagina_NoRepitenNiOmitenYAparecenAlVolverAAbrir()
    {
        var e = new Entorno();
        Llenar(e, propios: 30, aislados: 10, deInventario: 30);
        var primera = await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()));
        var alAbrir = new HashSet<Guid>(primera.Valor.Registros.Select(r => r.Id));
        var existentes = new List<Guid>();

        // Llegan 20 registros nuevos, más recientes que todo lo anterior, en las tres fuentes.
        var nuevos = new List<Guid>();
        for (var i = 0; i < 20; i++)
        {
            var registro = RegistrosDePrueba.De(i % 3 == 0 ? ServicioAuditado.Inventario : ServicioAuditado.Ecommerce, Ahora.AddMinutes(1).AddSeconds(i));
            nuevos.Add(registro.Id);
            (i % 3 == 0 ? e.Inventario.Fuente : i % 3 == 1 ? e.Propia : e.Aislada).Agregar(registro);
        }

        var vistos = new List<Guid>(alAbrir);
        var cursor = primera.Valor.SiguienteCursor;
        while (cursor is not null)
        {
            var pagina = await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria(), cursor));
            vistos.AddRange(pagina.Valor.Registros.Select(r => r.Id));
            cursor = pagina.Valor.SiguienteCursor;
        }

        Assert.Equal(70, vistos.Count);
        Assert.Equal(70, vistos.Distinct().Count());
        Assert.Empty(vistos.Intersect(nuevos));
        _ = existentes;

        var reabierta = await e.TodasLasPaginas();
        Assert.Equal(90, reabierta.Count);
        Assert.Equal(nuevos.Order(), reabierta.Select(r => r.Id).Intersect(nuevos).Order());
    }

    [Fact]
    public async Task UnaFuenteConMenosDeVeinteRegistros_NoDejaCursorYLaPrimeraPaginaNoRepiteLlamadas()
    {
        var e = new Entorno();
        Llenar(e, propios: 3, aislados: 0, deInventario: 2);

        var resultado = await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()));

        Assert.Equal(5, resultado.Valor.Registros.Count);
        Assert.Null(resultado.Valor.SiguienteCursor);
        Assert.Equal(1, e.Propia.Llamadas);
        Assert.Equal(1, e.Inventario.Fuente.Llamadas);
    }

    [Fact]
    public async Task LosFiltrosValidadosSeEnvianTalCualALasTresFuentes()
    {
        var e = new Entorno();
        var filtro = new FiltroDeAuditoria
        {
            Accion = "PedidoAnulado",
            UsuarioId = Guid.NewGuid(),
            Origen = "v1:abc",
            Desde = Ahora.AddDays(-3),
            Hasta = Ahora,
            Texto = "100%_\\ pedido",
        };

        var resultado = await e.Consulta.Consultar(new ConsultaDeAuditoria(filtro));

        Assert.True(resultado.EsExito);
        Assert.Equal(filtro, Assert.Single(e.Propia.Consultas).Filtro);
        Assert.Equal(filtro, Assert.Single(e.Aislada.Consultas).Filtro);
        Assert.Equal(filtro, Assert.Single(e.Inventario.Fuente.Consultas).Filtro);
    }

    // ---------- Quién consulta ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-24")]
    public async Task SoloLaAdministracionCentralConsulta_ElEncargadoLogisticaClienteYAnonimoSonRechazados()
    {
        foreach (var actor in new[]
        {
            ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
            ActorDePrueba.DePersonalDeLogistica(),
            ActorDePrueba.DeCliente(),
            ActorDePrueba.Anonimo(),
            ActorDePrueba.DelSistema(),
            ActorDePrueba.SinContexto(),
        })
        {
            var e = new Entorno(actor);
            Llenar(e, 3, 3, 3);

            var resultado = await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()));

            Assert.False(resultado.EsExito);
            Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
            Assert.Equal(0, e.Propia.Llamadas + e.Aislada.Llamadas + e.Inventario.Fuente.Llamadas);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-24")]
    public async Task ElRolSeRevalidaEnCadaPagina_UnCursorDeUnAdministradorNoSirveSiYaNoLoEs()
    {
        var e = new Entorno();
        Llenar(e, 30, 0, 0);
        var primera = await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()));
        e.Actor.Establecer(ActorActual.DeUsuario(Admin, "encargado-de-sucursal", Guid.NewGuid()));

        var segunda = await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria(), primera.Valor.SiguienteCursor));

        Assert.Equal(CategoriaDeError.Prohibido, segunda.Error!.Categoria);
    }

    // ---------- Filtros ----------

    [Theory]
    [InlineData("ab")]
    [InlineData("  a ")]
    [InlineData("")]
    public async Task ElTextoDeMenosDeTresCaracteres_EsUnaValidacionInvalida(string texto)
    {
        var e = new Entorno();

        var resultado = await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria { Texto = texto }));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Contains(resultado.Error.Errores, c => c.Campo == "texto");
        Assert.Equal(0, e.Propia.Llamadas);
    }

    [Fact]
    public async Task UnTextoDeTresCaracteres_YUnoConComodinesYBarras_SonValidos()
    {
        var e = new Entorno();

        Assert.True((await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria { Texto = "abc" }))).EsExito);
        Assert.True((await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria { Texto = "%_\\" }))).EsExito);
    }

    [Fact]
    public async Task ElTextoMuyLargoOConCaracteresDeControl_SeRechaza()
    {
        var e = new Entorno();

        Assert.False((await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria { Texto = new string('a', 101) }))).EsExito);
        Assert.False((await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria { Texto = "abc\u0000def" }))).EsExito);
        Assert.False((await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria { Texto = "abc\ndef" }))).EsExito);
    }

    [Fact]
    public async Task LaAccionFueraDelCatalogo_ElOrigenMalFormadoYElRangoInvertidoOExcesivo_SeRechazan()
    {
        var e = new Entorno();

        var accion = await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria { Accion = "AccionInventada" }));
        var origen = await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria { Origen = "10.0.0.1; DROP TABLE" }));
        var invertido = await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria { Desde = Ahora, Hasta = Ahora.AddDays(-1) }));
        var excesivo = await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria { Desde = Ahora.AddDays(-400), Hasta = Ahora }));

        Assert.Contains(accion.Error!.Errores, c => c.Campo == "accion");
        Assert.Contains(origen.Error!.Errores, c => c.Campo == "origen");
        Assert.Contains(invertido.Error!.Errores, c => c.Campo == "rango");
        Assert.Contains(excesivo.Error!.Errores, c => c.Campo == "rango");
        Assert.Equal(0, e.Propia.Llamadas);
    }

    // ---------- El Inventario no responde ----------

    [Theory]
    [Trait("Criterio", "AC-AUD-16")]
    [InlineData(EstadoDeRespuestaDelInventario.NoDisponible)]
    public async Task SiElInventarioNoResponde_LaListaTraeLoDelECommerceEIndicaQueFaltanLosSuyos(EstadoDeRespuestaDelInventario estado)
    {
        var e = new Entorno();
        Llenar(e, 25, 10, 15);
        e.Inventario.Estado = estado;

        var todos = await e.TodasLasPaginas();

        Assert.Equal(35, todos.Count);
        Assert.All(todos, r => Assert.Equal(ServicioAuditado.Ecommerce, r.Servicio));
        var primera = await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()));
        Assert.True(primera.Valor.FaltanDelInventario);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-16")]
    public async Task ConElInventarioDisponible_NoSeIndicaQueFalten()
    {
        var e = new Entorno();
        Llenar(e, 2, 2, 2);

        Assert.False((await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()))).Valor.FaltanDelInventario);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-16")]
    public async Task SiElInventarioRechazaLaConsulta_NoSeDisfrazaDeFaltanRegistros()
    {
        var e = new Entorno();
        e.Inventario.Estado = EstadoDeRespuestaDelInventario.Rechazada;

        var resultado = await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()));

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-16")]
    public async Task UnaRespuestaInvalidaDelInventarioSinCursorDePagina_CuentaComoQueFaltan()
    {
        var e = new Entorno();
        Llenar(e, 2, 0, 0);
        e.Inventario.Fuente.Agregar(RegistrosDePrueba.De(ServicioAuditado.Inventario, Ahora));
        var inventario = new InventarioSinCursorDePagina(e.Inventario);
        var consulta = new ConsultasDeAuditoriaUnificadas(e.Actor, CatalogoDeAccionesDeAuditoria.Crear(), e.Firmador, new LocalFalso(e.Propia), new IdentidadFalsa(e.Aislada), inventario);

        var resultado = await consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()));

        Assert.True(resultado.EsExito);
        Assert.True(resultado.Valor.FaltanDelInventario);
        Assert.Equal(2, resultado.Valor.Registros.Count);
    }

    private sealed class InventarioSinCursorDePagina(ClienteDelInventarioFalso interno) : IClienteDeAuditoriaDelInventario
    {
        public async Task<RespuestaDelInventario<PaginaDeRegistros>> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default)
        {
            var pagina = (await interno.Consultar(consulta, cancelacion)).Valor;
            return RespuestaDelInventario<PaginaDeRegistros>.ConValor(new PaginaDeRegistros(pagina.Registros, pagina.SiguienteCursor));
        }

        public Task<RespuestaDelInventario<IReadOnlyList<AlertaConsultada>>> LeerAlertasNuevas(DateTimeOffset desde, CancellationToken cancelacion = default) => interno.LeerAlertasNuevas(desde, cancelacion);

        public Task<RespuestaDelInventario<AlertaConsultada>> MarcarAlertaRevisada(string alertaId, int contadorVisto, CancellationToken cancelacion = default) => interno.MarcarAlertaRevisada(alertaId, contadorVisto, cancelacion);

        public Task<RespuestaDelInventario<PaginaDeRegistros>> ConsultarHistorial(Guid sucursalPedida, string? cursor, CancellationToken cancelacion = default) => interno.ConsultarHistorial(sucursalPedida, cursor, cancelacion);
    }

    [Fact]
    public async Task SiFallaUnaFuentePropia_LaListaNoEstaDisponible_ConUnErrorGenericoSinDatosInternos()
    {
        var e = new Entorno();
        e.Propia.Falla = new InvalidOperationException("Host=db;Password=Sup3r");

        var resultado = await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()));

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.DoesNotContain("Password", resultado.Error.Clave, StringComparison.Ordinal);

        var f = new Entorno();
        f.Aislada.Falla = new InvalidOperationException("otra");
        Assert.Equal(CategoriaDeError.NoDisponible, (await f.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()))).Error!.Categoria);
    }

    // ---------- El cursor ----------

    private static async Task<string> UnCursor(Entorno e, FiltroDeAuditoria? filtro = null)
    {
        Llenar(e, 30, 10, 30);
        var primera = await e.Consulta.Consultar(new ConsultaDeAuditoria(filtro ?? new FiltroDeAuditoria()));
        return primera.Valor.SiguienteCursor!;
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-27")]
    public async Task ElCursorNoLlevaElRolNiLaSucursalNiElTextoBuscado()
    {
        var e = new Entorno();
        var cursor = await UnCursor(e, new FiltroDeAuditoria { Texto = "texto-secreto-de-busqueda" });

        var partes = cursor.Split('.');
        var carga = Encoding.UTF8.GetString(Convert.FromBase64String(partes[1].Replace('-', '+').Replace('_', '/').PadRight(((partes[1].Length + 3) / 4) * 4, '=')));

        Assert.DoesNotContain("administrador", carga, StringComparison.Ordinal);
        Assert.DoesNotContain("texto-secreto-de-busqueda", carga, StringComparison.Ordinal);
        Assert.StartsWith("v2.", cursor, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-27")]
    public async Task ElCursorValidoSigueEnLaSiguientePagina_ConLaClaveAnteriorTambienDuranteLaRotacion()
    {
        var e = new Entorno();
        var cursor = await UnCursor(e);

        Assert.True((await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria(), cursor))).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-27")]
    public async Task UnCursorAlterado_Truncado_DeOtroUsuario_DeOtroFiltro_Caducado_OConOtraClave_SeRechazaIgual()
    {
        var e = new Entorno();
        var filtro = new FiltroDeAuditoria { Accion = "AccesoDenegado" };
        var cursor = await UnCursor(e, filtro);
        var partes = cursor.Split('.');
        var alterado = partes[0] + "." + partes[1] + "." + (partes[2][0] == 'A' ? "B" : "A") + partes[2][1..];
        var cargaAlterada = partes[0] + "." + (partes[1][5] == 'A' ? partes[1][..5] + "B" + partes[1][6..] : partes[1][..5] + "A" + partes[1][6..]) + "." + partes[2];

        var ajeno = new Entorno(ActorDePrueba.DeAdministradorCentral(Guid.NewGuid()));
        var casos = new (string Nombre, Entorno Entorno, ConsultaDeAuditoria Consulta)[]
        {
            ("alterado", e, new ConsultaDeAuditoria(filtro, alterado)),
            ("carga-alterada", e, new ConsultaDeAuditoria(filtro, cargaAlterada)),
            ("truncado", e, new ConsultaDeAuditoria(filtro, cursor[..^10])),
            ("otro-usuario", ajeno, new ConsultaDeAuditoria(filtro, cursor)),
            ("otro-filtro", e, new ConsultaDeAuditoria(new FiltroDeAuditoria { Accion = "LimiteExcedido" }, cursor)),
            ("sin-filtro", e, new ConsultaDeAuditoria(new FiltroDeAuditoria(), cursor)),
            ("otra-clave", e, new ConsultaDeAuditoria(filtro, "v9." + partes[1] + "." + partes[2])),
            ("otro-algoritmo", e, new ConsultaDeAuditoria(filtro, "none." + partes[1] + ".")),
            ("sin-firma", e, new ConsultaDeAuditoria(filtro, partes[0] + "." + partes[1])),
            ("basura", e, new ConsultaDeAuditoria(filtro, "esto no es un cursor")),
            ("demasiado-largo", e, new ConsultaDeAuditoria(filtro, new string('a', PaginaDeRegistros.MaximoDeCaracteresDelCursor + 1))),
            ("vacio", e, new ConsultaDeAuditoria(filtro, string.Empty)),
        };

        foreach (var caso in casos)
        {
            var resultado = await caso.Entorno.Consulta.Consultar(caso.Consulta);
            Assert.False(resultado.EsExito, caso.Nombre);
            Assert.Equal(ConsultasDeAuditoriaUnificadas.CursorInvalido, resultado.Error);
        }

        e.Reloj.Ahora = Ahora + FirmadorDeCursor.Vigencia + TimeSpan.FromSeconds(1);
        var caducado = await e.Consulta.Consultar(new ConsultaDeAuditoria(filtro, cursor));
        Assert.Equal(ConsultasDeAuditoriaUnificadas.CursorInvalido, caducado.Error);
    }

    [Fact]
    public async Task ElCursorFirmadoConLaClaveAnteriorSeVerificaMientrasDuraLaRotacion()
    {
        var reloj = new RelojFijo(Ahora);
        var usuario = Guid.NewGuid();
        var conAnterior = new FirmadorDeCursor(ClavesDeCursorFalsas.SoloLaAnterior, reloj);
        var cursor = conAnterior.Firmar(usuario, "huella", new PosicionDeFuente("p", 2));

        var vigente = new FirmadorDeCursor(new ClavesDeCursorFalsas(), reloj);

        Assert.Equal(new PosicionDeFuente("p", 2), vigente.Abrir<PosicionDeFuente>(cursor, usuario, "huella"));
        Assert.Null(vigente.Abrir<PosicionDeFuente>(cursor, Guid.NewGuid(), "huella"));
    }

    [Fact]
    public void LaHuellaDelFiltroDependeDeCadaCampoYNoDelTextoSobrante()
    {
        var baseDelFiltro = new FiltroDeAuditoria { Accion = "AccesoDenegado", Texto = "abc" };
        var huella = ValidacionDeFiltroDeAuditoria.Huella(baseDelFiltro);

        Assert.Equal(huella, ValidacionDeFiltroDeAuditoria.Huella(baseDelFiltro with { Texto = "  abc  " }));
        Assert.NotEqual(huella, ValidacionDeFiltroDeAuditoria.Huella(baseDelFiltro with { Texto = "abd" }));
        Assert.NotEqual(huella, ValidacionDeFiltroDeAuditoria.Huella(baseDelFiltro with { Origen = "x" }));
        Assert.NotEqual(huella, ValidacionDeFiltroDeAuditoria.Huella(baseDelFiltro with { UsuarioId = Guid.NewGuid() }));
        Assert.NotEqual(huella, ValidacionDeFiltroDeAuditoria.Huella(baseDelFiltro with { Desde = Ahora }));
        Assert.DoesNotContain("abc", huella, StringComparison.Ordinal);
    }
}
