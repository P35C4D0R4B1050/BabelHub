using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Auditoria;
using BabelHub.Inventario.UnitTests.Plataforma;
using BabelHub.Inventario.UnitTests.Soporte;
using AlertaAgrupada = BabelHub.Inventario.Domain.Auditoria.AlertaDeSeguridad;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>
/// La auditoría, las alertas y el historial que el Inventario entrega al E-commerce (T17; AUD-07, AUD-08, AUD-17, AUD-27, AUD-30, AUD-34, AUD-38): solo la
/// administración central con el token verificable (o el servicio para las alertas nuevas), el cursor firmado y ligado al usuario y a los filtros, el historial por
/// la sucursal de la credencial y la marca de revisada que solo se aplica si se pudo auditar.
/// </summary>
public sealed class ConsultasLocalesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.Parse("a0a0a0a0-0000-4000-8000-000000000001");
    private static readonly CatalogoDeMotivosDeAlerta Motivos = CatalogoDeAlertasDeAuditoria.Crear();

    private sealed class LocalFalso(FuentePaginadaFalsa fuente) : IConsultaLocalDeRegistros
    {
        public Task<PaginaDeRegistros> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default) => fuente.Consultar(consulta, cancelacion);
    }

    private sealed class HistorialFalso(FuentePaginadaFalsa fuente) : IHistorialLocal
    {
        public List<Guid> Sucursales { get; } = [];

        public Task<PaginaDeRegistros> Consultar(Guid sucursal, string? cursor, CancellationToken cancelacion = default)
        {
            Sucursales.Add(sucursal);
            return fuente.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria(), cursor), cancelacion);
        }
    }

    private sealed class Entorno
    {
        public Entorno(ActorDePrueba? actor = null)
        {
            Reloj = new RelojFijo(Ahora);
            Actor = actor ?? ActorDePrueba.DeAdministradorCentral(Admin);
            Firmador = new FirmadorDeCursor(new ClavesDeCursorFalsas(), Reloj);
            Consulta = new ConsultasDeAuditoriaLocales(Actor, CatalogoDeAccionesDeAuditoria.Crear(), Firmador, new LocalFalso(Fuente));
            Alertas = new ConsultaDeAlertasLocales(Actor, Firmador, Almacen, Auditoria, new UnidadDeTrabajoDeAuditoria(), Reloj);
            HistorialLocal = new HistorialFalso(FuenteDelHistorial);
            Historial = new HistorialDeSucursalLocal(Actor, HistorialLocal, Firmador);
        }

        public RelojFijo Reloj { get; }

        public ActorDePrueba Actor { get; }

        public FirmadorDeCursor Firmador { get; }

        public FuentePaginadaFalsa Fuente { get; } = new();

        public FuentePaginadaFalsa FuenteDelHistorial { get; } = new();

        public AlmacenDeAlertasEnMemoria Almacen { get; } = new();

        public AuditoriaEspia Auditoria { get; } = new();

        public ConsultasDeAuditoriaLocales Consulta { get; }

        public ConsultaDeAlertasLocales Alertas { get; }

        public HistorialDeSucursalLocal Historial { get; }

        public HistorialFalso HistorialLocal { get; }

        public AlertaAgrupada Guardar(string motivo, int ocurrencias, DateTimeOffset primera)
        {
            var alerta = VentanaDeAgrupacion.Registrar(null, Motivos, ServicioDeOrigen.Inventario, motivo, primera, Guid.NewGuid()).Alerta;
            for (var i = 1; i < ocurrencias; i++)
            {
                alerta = VentanaDeAgrupacion.Registrar(alerta, Motivos, ServicioDeOrigen.Inventario, motivo, primera.AddSeconds(i * 10), Guid.NewGuid()).Alerta;
            }

            Almacen.Alertas.Add(alerta);
            return alerta;
        }
    }

    // ---------- Auditoría ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-24")]
    public async Task SoloLaAdministracionCentralConConsultaLaAuditoria_LosDemasSonRechazados()
    {
        foreach (var actor in new[] { ActorDePrueba.DeEncargadoDe(Guid.NewGuid()), ActorDePrueba.DePersonalDeLogistica(), ActorDePrueba.DeCliente(), ActorDePrueba.Anonimo(), ActorDePrueba.DeIntegracion("ecommerce"), ActorDePrueba.DelSistema(), ActorDePrueba.SinContexto() })
        {
            var e = new Entorno(actor);

            var resultado = await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()));

            Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
            Assert.Equal(0, e.Fuente.Llamadas);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-27")]
    public async Task LaAuditoriaPaginaDeVeinteEnVeinteConUnCursorFirmadoSinRepetirNiOmitir()
    {
        var e = new Entorno();
        for (var i = 0; i < 47; i++)
        {
            e.Fuente.Agregar(RegistrosDePrueba.De(ServicioAuditado.Inventario, Ahora.AddSeconds(-i)));
        }

        var vistos = new List<Guid>();
        string? cursor = null;
        do
        {
            var pagina = (await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria(), cursor))).Valor;
            vistos.AddRange(pagina.Registros.Select(r => r.Id));
            cursor = pagina.SiguienteCursor;
            Assert.True(cursor is null || cursor.StartsWith("v2.", StringComparison.Ordinal));
        }
        while (cursor is not null);

        Assert.Equal(47, vistos.Count);
        Assert.Equal(47, vistos.Distinct().Count());
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-27")]
    public async Task UnCursorAlteradoDeOtroUsuarioDeOtroFiltroOCaducado_SeRechazaConElMismoError()
    {
        var e = new Entorno();
        for (var i = 0; i < 30; i++)
        {
            e.Fuente.Agregar(RegistrosDePrueba.De(ServicioAuditado.Inventario, Ahora.AddSeconds(-i)));
        }

        var filtro = new FiltroDeAuditoria { Accion = "AccesoDenegado" };
        var cursor = (await e.Consulta.Consultar(new ConsultaDeAuditoria(filtro))).Valor.SiguienteCursor!;
        var partes = cursor.Split('.');

        var ajeno = new Entorno(ActorDePrueba.DeAdministradorCentral(Guid.NewGuid()));
        Assert.Equal(ConsultasDeAuditoriaLocales.CursorInvalido, (await ajeno.Consulta.Consultar(new ConsultaDeAuditoria(filtro, cursor))).Error);
        Assert.Equal(ConsultasDeAuditoriaLocales.CursorInvalido, (await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria(), cursor))).Error);
        Assert.Equal(ConsultasDeAuditoriaLocales.CursorInvalido, (await e.Consulta.Consultar(new ConsultaDeAuditoria(filtro, cursor[..^4]))).Error);
        Assert.Equal(ConsultasDeAuditoriaLocales.CursorInvalido, (await e.Consulta.Consultar(new ConsultaDeAuditoria(filtro, "none." + partes[1] + "."))).Error);
        Assert.Equal(ConsultasDeAuditoriaLocales.CursorInvalido, (await e.Consulta.Consultar(new ConsultaDeAuditoria(filtro, "texto cualquiera"))).Error);

        e.Reloj.Ahora = Ahora + FirmadorDeCursor.Vigencia + TimeSpan.FromSeconds(1);
        Assert.Equal(ConsultasDeAuditoriaLocales.CursorInvalido, (await e.Consulta.Consultar(new ConsultaDeAuditoria(filtro, cursor))).Error);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-06")]
    public async Task LosFiltrosSeValidanYSeEnvianAlAlmacen_ElTextoMenorDeTresCaracteresEsInvalido()
    {
        var e = new Entorno();
        var filtro = new FiltroDeAuditoria { Accion = "AjusteRegistrado", Texto = "100%_\\", Desde = Ahora.AddDays(-2), Hasta = Ahora };

        Assert.True((await e.Consulta.Consultar(new ConsultaDeAuditoria(filtro))).EsExito);
        Assert.Equal(filtro, Assert.Single(e.Fuente.Consultas).Filtro);

        var corto = await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria { Texto = "ab" }));
        Assert.Equal(CategoriaDeError.Validacion, corto.Error!.Categoria);
        Assert.Single(e.Fuente.Consultas);
    }

    [Fact]
    public async Task SiElAlmacenFalla_LaConsultaNoEstaDisponibleSinDatosInternos()
    {
        var e = new Entorno();
        e.Fuente.Falla = new InvalidOperationException("Host=db;Password=Sup3r");

        var resultado = await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()));

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.DoesNotContain("Password", resultado.Error.Clave, StringComparison.Ordinal);
    }

    // ---------- Alertas ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-30")]
    public async Task ElServicioLeeLasAlertasNuevasSinQueSeRechace_ElEncargadoYElClienteNo()
    {
        var e = new Entorno(ActorDePrueba.DeIntegracion("lectura-alertas"));
        e.Guardar(MotivosDeAlerta.TrabajoDeFondoDetenido, 1, Ahora.AddMinutes(-2));
        e.Guardar(MotivosDeAlerta.AccesosDenegados, 3, Ahora.AddDays(-1));

        var resultado = await e.Alertas.LeerNuevas(Ahora.AddMinutes(-5));

        var alerta = Assert.Single(resultado.Valor);
        Assert.StartsWith("inv:", alerta.Id, StringComparison.Ordinal);
        Assert.Equal(ClaseDeAlertaConsultada.Tecnica, alerta.Clase);

        foreach (var actor in new[] { ActorDePrueba.DeEncargadoDe(Guid.NewGuid()), ActorDePrueba.DeCliente(), ActorDePrueba.Anonimo(), ActorDePrueba.SinContexto() })
        {
            Assert.Equal(CategoriaDeError.Prohibido, (await new Entorno(actor).Alertas.LeerNuevas(Ahora.AddDays(-1))).Error!.Categoria);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public async Task MarcarRevisada_MarcaHastaElContadorVistoYAudita_ElEncargadoNoPuede()
    {
        var e = new Entorno();
        var alerta = e.Guardar(MotivosDeAlerta.AccesosDenegados, 4, Ahora.AddMinutes(-3));

        var resultado = await e.Alertas.MarcarRevisada(alerta.Id, 3);

        Assert.Equal(3, resultado.Valor.RevisadasHasta);
        Assert.Equal(EstadoDeAlertaConsultada.Pendiente, resultado.Valor.Estado);
        var solicitud = Assert.Single(e.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.AlertaMarcadaRevisada, solicitud.Accion);
        Assert.Equal("inventario", solicitud.Detalle["servicio"]);

        var encargado = new Entorno(ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));
        var otra = encargado.Guardar(MotivosDeAlerta.AccesosDenegados, 2, Ahora.AddMinutes(-3));
        Assert.Equal(CategoriaDeError.Prohibido, (await encargado.Alertas.MarcarRevisada(otra.Id, 2)).Error!.Categoria);
        Assert.Equal(0, encargado.Almacen.Alertas.Single().RevisadasHasta);
        Assert.Empty(encargado.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public async Task SiLaMarcaNoSePuedeAuditar_LaAlertaSigueConSuEstado()
    {
        var e = new Entorno();
        var alerta = e.Guardar(MotivosDeAlerta.AccesosDenegados, 2, Ahora.AddMinutes(-3));
        e.Auditoria.Caida = true;

        var resultado = await e.Alertas.MarcarRevisada(alerta.Id, 2);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(0, e.Almacen.Alertas.Single().RevisadasHasta);
    }

    [Fact]
    public async Task MarcarRevisada_RechazaIdentificadoresAjenosContadoresInvalidosYAlertasTecnicas()
    {
        var e = new Entorno();
        var alerta = e.Guardar(MotivosDeAlerta.AccesosDenegados, 2, Ahora.AddMinutes(-3));
        var tecnica = e.Guardar(MotivosDeAlerta.TrabajoDeFondoDetenido, 2, Ahora.AddMinutes(-3));

        Assert.Equal("marca-invalida", (await e.Alertas.MarcarRevisada("eco:" + Guid.NewGuid().ToString("D"), 1)).Error!.Clave);
        Assert.Equal("marca-invalida", (await e.Alertas.MarcarRevisada(alerta.Id, 0)).Error!.Clave);
        Assert.Equal("contador-invalido", (await e.Alertas.MarcarRevisada(alerta.Id, 3)).Error!.Clave);
        Assert.Equal("alerta-no-encontrada", (await e.Alertas.MarcarRevisada(tecnica.Id, 1)).Error!.Clave);
        Assert.Equal("alerta-no-encontrada", (await e.Alertas.MarcarRevisada("inv:" + Guid.NewGuid().ToString("D"), 1)).Error!.Clave);
        Assert.Empty(e.Auditoria.Solicitudes);
    }

    [Fact]
    public async Task LaListaDeAlertasDeSeguridadPaginaYNoMuestraLasTecnicas()
    {
        var e = new Entorno();
        for (var i = 0; i < 23; i++)
        {
            e.Guardar(MotivosDeAlerta.AccesosDenegados, 1, Ahora.AddMinutes(-i * 20));
        }

        e.Guardar(MotivosDeAlerta.TrabajoDeFondoDetenido, 1, Ahora);

        var primera = (await e.Alertas.Listar(null)).Valor;
        var segunda = (await e.Alertas.Listar(primera.SiguienteCursor)).Valor;

        Assert.Equal(20, primera.Alertas.Count);
        Assert.Equal(3, segunda.Alertas.Count);
        Assert.Null(segunda.SiguienteCursor);
        Assert.Equal(23, primera.Alertas.Concat(segunda.Alertas).Select(a => a.Id).Distinct().Count());
        Assert.Equal(ConsultasDeAuditoriaLocales.CursorInvalido, (await e.Alertas.Listar("basura")).Error);
    }

    // ---------- Historial ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-07")]
    public async Task ElEncargadoSinFiltroVeElHistorialDeSuSucursalYSiPideLaDeCuscoSeRechaza()
    {
        var ayacucho = Guid.NewGuid();
        var cusco = Guid.NewGuid();
        var e = new Entorno(ActorDePrueba.DeEncargadoDe(ayacucho));
        e.FuenteDelHistorial.Agregar(RegistrosDePrueba.De(ServicioAuditado.Inventario, Ahora, ayacucho.ToString("D"), accion: "AjusteRegistrado"));

        var propio = await e.Historial.Consultar(null, null);
        var ajeno = await e.Historial.Consultar(cusco, null);

        Assert.Single(propio.Valor.Registros);
        Assert.Equal(ayacucho, Assert.Single(e.HistorialLocal.Sucursales));
        Assert.Equal("fuera-de-sucursal", ajeno.Error!.Clave);
        Assert.Equal(CategoriaDeError.Prohibido, ajeno.Error.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-07")]
    public async Task ElCursorDelHistorialEstaLigadoALaSucursalYAlUsuario()
    {
        var ayacucho = Guid.NewGuid();
        var e = new Entorno(ActorDePrueba.DeEncargadoDe(ayacucho));
        for (var i = 0; i < 30; i++)
        {
            e.FuenteDelHistorial.Agregar(RegistrosDePrueba.De(ServicioAuditado.Inventario, Ahora.AddSeconds(-i), ayacucho.ToString("D")));
        }

        var primera = (await e.Historial.Consultar(null, null)).Valor;
        var segunda = (await e.Historial.Consultar(null, primera.SiguienteCursor)).Valor;

        Assert.Equal(20, primera.Registros.Count);
        Assert.Equal(10, segunda.Registros.Count);
        Assert.Empty(primera.Registros.Select(r => r.Id).Intersect(segunda.Registros.Select(r => r.Id)));

        // El mismo cursor en manos de otro encargado (otra sucursal y otro usuario) no sirve.
        var otro = new Entorno(ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));
        Assert.Equal(ConsultasDeAuditoriaLocales.CursorInvalido, (await otro.Historial.Consultar(null, primera.SiguienteCursor)).Error);
        Assert.Equal(ConsultasDeAuditoriaLocales.CursorInvalido, (await e.Historial.Consultar(null, "basura")).Error);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-24")]
    public async Task ElHistorialEsDelEncargadoYDeLaAdministracionCentral_LosDemasYUnEncargadoSinSucursalSonRechazados()
    {
        foreach (var actor in new[] { ActorDePrueba.DePersonalDeLogistica(), ActorDePrueba.DeCliente(), ActorDePrueba.Anonimo(), ActorDePrueba.DelSistema(), ActorDePrueba.SinContexto() })
        {
            var e = new Entorno(actor);
            Assert.Equal(CategoriaDeError.Prohibido, (await e.Historial.Consultar(Guid.NewGuid(), null)).Error!.Categoria);
            Assert.Empty(e.HistorialLocal.Sucursales);
        }

        var sinSucursal = new Entorno(ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));
        sinSucursal.Actor.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), "encargado-de-sucursal"));
        Assert.Equal("fuera-de-sucursal", (await sinSucursal.Historial.Consultar(null, null)).Error!.Clave);

        var admin = new Entorno();
        Assert.Equal("sucursal-requerida", (await admin.Historial.Consultar(null, null)).Error!.Clave);
        Assert.True((await admin.Historial.Consultar(Guid.NewGuid(), null)).EsExito);
    }
}
