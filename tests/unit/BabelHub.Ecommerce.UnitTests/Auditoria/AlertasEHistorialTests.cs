using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;
using AlertaAgrupada = BabelHub.Ecommerce.Domain.Auditoria.AlertaDeSeguridad;

namespace BabelHub.Ecommerce.UnitTests.Auditoria;

/// <summary>
/// Las alertas de seguridad (T17; AUD-13, AUD-18, AUD-27, AUD-34, AUD-38, AUD-39) y el historial de la sucursal (AUD-08, AUD-27): lista fusionada con el Inventario,
/// marca de revisada que solo se aplica si se pudo auditar y alcance por sucursal comprobado en la Aplicación.
/// </summary>
public sealed class AlertasEHistorialTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.Parse("a0a0a0a0-0000-4000-8000-000000000001");
    private static readonly CatalogoDeMotivosDeAlerta Motivos = CatalogoDeAlertasDeAuditoria.Crear();

    private sealed class Entorno
    {
        public Entorno(ActorDePrueba? actor = null)
        {
            Reloj = new RelojFijo(Ahora);
            Actor = actor ?? ActorDePrueba.DeAdministradorCentral(Admin);
            Consulta = new ConsultaDeAlertasUnificada(Actor, new FirmadorDeCursor(new ClavesDeCursorFalsas(), Reloj), Almacen, Inventario, Auditoria, Unidad, Reloj);
            Historial = new HistorialDeSucursalUnificado(Actor, Inventario);
        }

        public RelojFijo Reloj { get; }

        public ActorDePrueba Actor { get; }

        public AlmacenDeAlertasEnMemoria Almacen { get; } = new();

        public ClienteDelInventarioFalso Inventario { get; } = new();

        public AuditoriaEspia Auditoria { get; } = new();

        public UnidadDeTrabajoDeAuditoria Unidad { get; } = new();

        public ConsultaDeAlertasUnificada Consulta { get; }

        public HistorialDeSucursalUnificado Historial { get; }

        public AlertaAgrupada Guardar(string motivo, int ocurrencias, DateTimeOffset primera, ServicioDeOrigen servicio = ServicioDeOrigen.Ecommerce)
        {
            var resultado = VentanaDeAgrupacion.Registrar(null, Motivos, servicio, motivo, primera, Guid.NewGuid());
            var alerta = resultado.Alerta;
            for (var i = 1; i < ocurrencias; i++)
            {
                alerta = VentanaDeAgrupacion.Registrar(alerta, Motivos, servicio, motivo, primera.AddSeconds(i * 10), Guid.NewGuid()).Alerta;
            }

            Almacen.Alertas.Add(alerta);
            return alerta;
        }
    }

    private static AlertaConsultada DelInventario(string motivo, DateTimeOffset ultima, ClaseDeAlertaConsultada clase = ClaseDeAlertaConsultada.Seguridad) =>
        new($"inv:{Guid.NewGuid():D}", ServicioAuditado.Inventario, new CodigoDeAlerta(motivo), clase, ultima.AddMinutes(-1), ultima, 2, 0);

    // ---------- La lista ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public async Task LaListaMuestraLasDeSeguridadDeAmbosServiciosDeLaMasRecienteALaMasAntigua_SinLasTecnicas()
    {
        var e = new Entorno();
        var propia = e.Guardar(MotivosDeAlerta.AccesosDenegados, 3, Ahora.AddMinutes(-30));
        e.Guardar(MotivosDeAlerta.TrabajoDeFondoDetenido, 5, Ahora.AddMinutes(-2));
        var remota = DelInventario(MotivosDeAlerta.AccesosDenegados, Ahora.AddMinutes(-10));
        var remotaTecnica = DelInventario(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora.AddMinutes(-1), ClaseDeAlertaConsultada.Tecnica);
        e.Inventario.Alertas = [remota, remotaTecnica];

        var resultado = await e.Consulta.Listar(null);

        Assert.Equal([remota.Id, propia.Id], resultado.Valor.Alertas.Select(a => a.Id));
        Assert.All(resultado.Valor.Alertas, a => Assert.Equal(ClaseDeAlertaConsultada.Seguridad, a.Clase));
        Assert.Equal(3, resultado.Valor.Alertas[1].Ocurrencias);
        Assert.False(resultado.Valor.FaltanDelInventario);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-16")]
    public async Task SiElInventarioNoResponde_LaListaTraeLasPropiasEIndicaQueFaltanLasSuyas()
    {
        var e = new Entorno();
        e.Guardar(MotivosDeAlerta.IngresosFallidos, 1, Ahora.AddMinutes(-5));
        e.Inventario.Estado = EstadoDeRespuestaDelInventario.NoDisponible;

        var resultado = await e.Consulta.Listar(null);

        Assert.Single(resultado.Valor.Alertas);
        Assert.True(resultado.Valor.FaltanDelInventario);
    }

    [Fact]
    public async Task LaListaPaginaDeVeinteEnVeinteSinRepetirNiOmitir_YElCursorEstaLigadoAlUsuario()
    {
        var e = new Entorno();
        for (var i = 0; i < 27; i++)
        {
            e.Guardar(MotivosDeAlerta.AccesosDenegados, 1, Ahora.AddMinutes(-i * 20));
        }

        e.Inventario.Alertas = [.. Enumerable.Range(0, 18).Select(i => DelInventario(MotivosDeAlerta.IngresosFallidos, Ahora.AddMinutes(-i * 20 - 7)))];

        var vistas = new List<string>();
        string? cursor = null;
        var paginas = 0;
        do
        {
            var resultado = await e.Consulta.Listar(cursor);
            vistas.AddRange(resultado.Valor.Alertas.Select(a => a.Id));
            cursor = resultado.Valor.SiguienteCursor;
            paginas++;
        }
        while (cursor is not null && paginas < 10);

        Assert.Equal(45, vistas.Count);
        Assert.Equal(45, vistas.Distinct().Count());
        Assert.Equal(3, paginas);

        var primera = await e.Consulta.Listar(null);
        e.Actor.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), "administrador-central"));
        var ajeno = await e.Consulta.Listar(primera.Valor.SiguienteCursor);
        Assert.Equal(ConsultasDeAuditoriaUnificadas.CursorInvalido, ajeno.Error);
        Assert.Equal(ConsultasDeAuditoriaUnificadas.CursorInvalido, (await e.Consulta.Listar("basura")).Error);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-24")]
    public async Task SoloLaAdministracionCentralVeLasAlertasLasLeeLasMarcaOLasNuevasDelInventario()
    {
        foreach (var actor in new[] { ActorDePrueba.DeEncargadoDe(Guid.NewGuid()), ActorDePrueba.DePersonalDeLogistica(), ActorDePrueba.DeCliente(), ActorDePrueba.Anonimo(), ActorDePrueba.SinContexto() })
        {
            var e = new Entorno(actor);
            var alerta = e.Guardar(MotivosDeAlerta.AccesosDenegados, 3, Ahora.AddMinutes(-5));

            Assert.Equal(CategoriaDeError.Prohibido, (await e.Consulta.Listar(null)).Error!.Categoria);
            Assert.Equal(CategoriaDeError.Prohibido, (await e.Consulta.LeerNuevas(Ahora.AddDays(-1))).Error!.Categoria);
            Assert.Equal(CategoriaDeError.Prohibido, (await e.Consulta.MarcarRevisada(alerta.Id, 3)).Error!.Categoria);
            Assert.Equal(0, e.Inventario.LlamadasDeAlertas);
            Assert.Empty(e.Auditoria.Solicitudes);
            Assert.Equal(0, e.Almacen.Alertas.Single().RevisadasHasta);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-30")]
    public async Task ElIntegradorOElSistemaLeenLasAlertasNuevasConSuCredencialDeServicio()
    {
        foreach (var actor in new[] { ActorDePrueba.DeIntegracion("lectura-alertas") })
        {
            var e = new Entorno(actor);
            e.Guardar(MotivosDeAlerta.TrabajoDeFondoDetenido, 1, Ahora.AddMinutes(-3));
            e.Guardar(MotivosDeAlerta.AccesosDenegados, 1, Ahora.AddDays(-2));

            var resultado = await e.Consulta.LeerNuevas(Ahora.AddMinutes(-5));

            var alerta = Assert.Single(resultado.Valor);
            Assert.Equal(ClaseDeAlertaConsultada.Tecnica, alerta.Clase);
        }
    }

    // ---------- Marcar como revisada ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public async Task MarcarRevisada_MarcaLasTresQueVeiaYQuedaPendienteLaCuartaYUnRegistroDeAuditoria()
    {
        var e = new Entorno();
        var alerta = e.Guardar(MotivosDeAlerta.IngresosFallidos, 4, Ahora.AddMinutes(-3));

        var resultado = await e.Consulta.MarcarRevisada(alerta.Id, 3);

        Assert.True(resultado.EsExito);
        Assert.Equal(3, resultado.Valor.RevisadasHasta);
        Assert.Equal(EstadoDeAlertaConsultada.Pendiente, resultado.Valor.Estado);
        Assert.Equal(Admin, resultado.Valor.RevisadaPor);
        var solicitud = Assert.Single(e.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.AlertaMarcadaRevisada, solicitud.Accion);
        Assert.Equal(alerta.Id, solicitud.ObjetoId);
        Assert.Equal("alerta", solicitud.ObjetoTipo);
        Assert.Equal("3", solicitud.Detalle["contadorVisto"]);
        Assert.Equal(3, e.Almacen.Alertas.Single().RevisadasHasta);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public async Task SiNoSePuedeRegistrarLaMarca_LaAlertaSigueConSuEstadoYSeInformaServicioNoDisponible()
    {
        var e = new Entorno();
        var alerta = e.Guardar(MotivosDeAlerta.IngresosFallidos, 4, Ahora.AddMinutes(-3));
        e.Auditoria.Caida = true;

        var resultado = await e.Consulta.MarcarRevisada(alerta.Id, 4);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(0, e.Almacen.Alertas.Single().RevisadasHasta);
        Assert.Equal(EstadoDeAlerta.Pendiente, e.Almacen.Alertas.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public async Task UnaOcurrenciaDentroDeLaVentanaTrasLaMarca_LaVuelveAPendienteSinOtroCorreo()
    {
        var e = new Entorno();
        var alerta = e.Guardar(MotivosDeAlerta.IngresosFallidos, 3, Ahora.AddMinutes(-4));
        await e.Consulta.MarcarRevisada(alerta.Id, 3);

        var revisada = e.Almacen.Alertas.Single();
        var agrupada = VentanaDeAgrupacion.Registrar(revisada, Motivos, ServicioDeOrigen.Ecommerce, MotivosDeAlerta.IngresosFallidos, Ahora.AddMinutes(-1), Guid.NewGuid());

        Assert.Equal(EstadoDeAlerta.Revisada, revisada.Estado);
        Assert.Equal(EstadoDeAlerta.Pendiente, agrupada.Alerta.Estado);
        Assert.False(agrupada.EnviarCorreo);
    }

    [Fact]
    public async Task MarcarDeNuevoHastaUnContadorMenorOIgual_NoCambiaNadaNiVuelveAAuditar()
    {
        var e = new Entorno();
        var alerta = e.Guardar(MotivosDeAlerta.IngresosFallidos, 4, Ahora.AddMinutes(-3));
        await e.Consulta.MarcarRevisada(alerta.Id, 3);

        var otra = await e.Consulta.MarcarRevisada(alerta.Id, 2);

        Assert.True(otra.EsExito);
        Assert.Equal(3, otra.Valor.RevisadasHasta);
        Assert.Single(e.Auditoria.Solicitudes);
    }

    [Fact]
    public async Task MarcarRevisada_RechazaUnContadorMayorQueLasOcurrenciasUnaAlertaInexistenteOTecnicaYUnIdentificadorSinPrefijo()
    {
        var e = new Entorno();
        var alerta = e.Guardar(MotivosDeAlerta.IngresosFallidos, 2, Ahora.AddMinutes(-3));
        var tecnica = e.Guardar(MotivosDeAlerta.TrabajoDeFondoDetenido, 2, Ahora.AddMinutes(-3));

        Assert.Equal("contador-invalido", (await e.Consulta.MarcarRevisada(alerta.Id, 3)).Error!.Clave);
        Assert.Equal("marca-invalida", (await e.Consulta.MarcarRevisada(alerta.Id, 0)).Error!.Clave);
        Assert.Equal("alerta-no-encontrada", (await e.Consulta.MarcarRevisada("eco:" + Guid.NewGuid().ToString("D"), 1)).Error!.Clave);
        Assert.Equal("alerta-no-encontrada", (await e.Consulta.MarcarRevisada(tecnica.Id, 1)).Error!.Clave);
        Assert.Equal("alerta-invalida", (await e.Consulta.MarcarRevisada("otra:1", 1)).Error!.Clave);
        Assert.Empty(e.Auditoria.Solicitudes);
    }

    [Fact]
    public async Task UnaAlertaDelInventarioSeReenvia_ConElContadorVisto_YSusFallasSeTraducen()
    {
        var e = new Entorno();
        var remota = DelInventario(MotivosDeAlerta.AccesosDenegados, Ahora.AddMinutes(-2));
        e.Inventario.AlertaMarcada = remota;

        var resultado = await e.Consulta.MarcarRevisada(remota.Id, 2);

        Assert.Equal(remota, resultado.Valor);
        Assert.Equal((remota.Id, 2), Assert.Single(e.Inventario.Marcas));
        Assert.Empty(e.Auditoria.Solicitudes);

        e.Inventario.Estado = EstadoDeRespuestaDelInventario.NoDisponible;
        Assert.Equal(CategoriaDeError.NoDisponible, (await e.Consulta.MarcarRevisada(remota.Id, 2)).Error!.Categoria);
        e.Inventario.Estado = EstadoDeRespuestaDelInventario.Rechazada;
        Assert.Equal(CategoriaDeError.Prohibido, (await e.Consulta.MarcarRevisada(remota.Id, 2)).Error!.Categoria);
    }

    // ---------- Historial de la sucursal ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-07")]
    public async Task ElEncargadoSinFiltroVeElHistorialDeLaSucursalDeSuCredencial()
    {
        var sucursal = Guid.NewGuid();
        var e = new Entorno(ActorDePrueba.DeEncargadoDe(sucursal));
        e.Inventario.Historial = new PaginaDeRegistros([RegistrosDePrueba.De(ServicioAuditado.Inventario, Ahora, sucursal.ToString("D"), accion: "AjusteRegistrado")], null);

        var resultado = await e.Historial.Consultar(null, null);

        Assert.True(resultado.EsExito);
        Assert.Single(resultado.Valor.Registros);
        Assert.Equal((sucursal, (string?)null), Assert.Single(e.Inventario.HistorialPedido));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-07")]
    public async Task ElEncargadoQuePideLaSucursalPropiaEsAtendido_YSiPideLaDeCusco_SeRechazaSinLlamarAlInventario()
    {
        var ayacucho = Guid.NewGuid();
        var cusco = Guid.NewGuid();
        var e = new Entorno(ActorDePrueba.DeEncargadoDe(ayacucho));

        Assert.True((await e.Historial.Consultar(ayacucho, null)).EsExito);
        var ajena = await e.Historial.Consultar(cusco, null);

        Assert.Equal(CategoriaDeError.Prohibido, ajena.Error!.Categoria);
        Assert.Equal("fuera-de-sucursal", ajena.Error.Clave);
        Assert.Single(e.Inventario.HistorialPedido);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-07")]
    public async Task UnEncargadoSinSucursalEnSuCredencial_NoVeNingunHistorial()
    {
        var e = new Entorno(ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));
        e.Actor.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), "encargado-de-sucursal"));

        var resultado = await e.Historial.Consultar(null, null);

        Assert.Equal("fuera-de-sucursal", resultado.Error!.Clave);
        Assert.Empty(e.Inventario.HistorialPedido);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-24")]
    public async Task ElHistorialEsDelEncargadoYDeLaAdministracionCentral_LosDemasSonRechazados()
    {
        foreach (var actor in new[] { ActorDePrueba.DePersonalDeLogistica(), ActorDePrueba.DeCliente(), ActorDePrueba.Anonimo(), ActorDePrueba.DelSistema(), ActorDePrueba.SinContexto() })
        {
            var e = new Entorno(actor);

            Assert.Equal(CategoriaDeError.Prohibido, (await e.Historial.Consultar(Guid.NewGuid(), null)).Error!.Categoria);
            Assert.Empty(e.Inventario.HistorialPedido);
        }
    }

    [Fact]
    public async Task LaAdministracionCentralIndicaLaSucursalQueQuiereVer()
    {
        var e = new Entorno();
        var sucursal = Guid.NewGuid();

        Assert.Equal("sucursal-requerida", (await e.Historial.Consultar(null, null)).Error!.Clave);
        Assert.True((await e.Historial.Consultar(sucursal, "cursor-del-inventario")).EsExito);
        Assert.Equal((sucursal, (string?)"cursor-del-inventario"), Assert.Single(e.Inventario.HistorialPedido));
    }

    [Fact]
    public async Task ElInventarioCaidoOQueRechaza_SeTraduceYUnCursorDemasiadoLargoNoLlegaAlInventario()
    {
        var sucursal = Guid.NewGuid();
        var e = new Entorno(ActorDePrueba.DeEncargadoDe(sucursal));

        e.Inventario.Estado = EstadoDeRespuestaDelInventario.NoDisponible;
        Assert.Equal(CategoriaDeError.NoDisponible, (await e.Historial.Consultar(null, null)).Error!.Categoria);
        e.Inventario.Estado = EstadoDeRespuestaDelInventario.Rechazada;
        Assert.Equal(CategoriaDeError.Prohibido, (await e.Historial.Consultar(null, null)).Error!.Categoria);

        e.Inventario.HistorialPedido.Clear();
        Assert.Equal(ConsultasDeAuditoriaUnificadas.CursorInvalido, (await e.Historial.Consultar(null, new string('a', 5000))).Error);
        Assert.Empty(e.Inventario.HistorialPedido);
    }
}
