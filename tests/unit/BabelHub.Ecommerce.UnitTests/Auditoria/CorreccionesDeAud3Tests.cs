using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Auditoria;

/// <summary>Las correcciones de la revisión de seguridad y de arquitectura del bloque AUD-3: alcance de la integración, identificadores de alerta, lectura truncada, páginas sin fin y fallos posteriores al envío.</summary>
public sealed class CorreccionesDeAud3Tests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    private static AlertaConsultada DelInventario(Guid id, DateTimeOffset ultima) =>
        new($"inv:{id:D}", ServicioAuditado.Inventario, new CodigoDeAlerta(MotivosDeAlerta.TrabajoDeFondoDetenido), ClaseDeAlertaConsultada.Tecnica, ultima.AddMinutes(-1), ultima, 1, 0);

    // ---------- Alcance de la integración (AUD-23) ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-30")]
    public async Task LeerNuevasSoloLaAceptaLaIntegracionDeAlcanceLecturaDeAlertas_OtraCredencialOElSistemaNo()
    {
        foreach (var actor in new[] { ActorDePrueba.DeIntegracion("historial"), ActorDePrueba.DeIntegracion("ecommerce"), ActorDePrueba.DeIntegracion("baja-de-titular"), ActorDePrueba.DelSistema() })
        {
            var almacen = new AlmacenDeAlertasEnMemoria();
            var consulta = new ConsultaDeAlertasUnificada(actor, new FirmadorDeCursor(new ClavesDeCursorFalsas(), new RelojFijo(Ahora)), almacen, new ClienteDelInventarioFalso(), new AuditoriaEspia(), new UnidadDeTrabajoDeAuditoria(), new RelojFijo(Ahora));

            Assert.Equal(CategoriaDeError.Prohibido, (await consulta.LeerNuevas(Ahora.AddDays(-1))).Error!.Categoria);
        }
    }

    // ---------- Identificadores de alerta ----------

    [Theory]
    [InlineData("inv:../../etc/passwd")]
    [InlineData("inv:11111111-1111-4111-8111-11111111111/")]
    [InlineData("inv:AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA")]
    [InlineData("inv:11111111-1111-4111-8111-111111111111?x=1")]
    [InlineData("eco:11111111111141118111111111111111")]
    [InlineData("eco:00000000-0000-0000-0000-000000000000")]
    [InlineData("INV:11111111-1111-4111-8111-111111111111")]
    public async Task UnIdentificadorDeAlertaConOtraFormaNoLlegaNiAlAlmacenNiAlInventario(string id)
    {
        var inventario = new ClienteDelInventarioFalso { AlertaMarcada = DelInventario(Guid.NewGuid(), Ahora) };
        var almacen = new AlmacenDeAlertasEnMemoria();
        var consulta = new ConsultaDeAlertasUnificada(ActorDePrueba.DeAdministradorCentral(), new FirmadorDeCursor(new ClavesDeCursorFalsas(), new RelojFijo(Ahora)), almacen, inventario, new AuditoriaEspia(), new UnidadDeTrabajoDeAuditoria(), new RelojFijo(Ahora));

        var resultado = await consulta.MarcarRevisada(id, 1);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Empty(inventario.Marcas);
    }

    // ---------- Lectura truncada del Inventario (AUD-38) ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-30")]
    public async Task SiLaLecturaDelInventarioLlegaAlTope_LaMarcaAvanzaSoloHastaLaUltimaLeidaYLasQueNoCupieronSeAtiendenDespues()
    {
        var reloj = new RelojFijo(Ahora);
        var almacen = new AlmacenDeAlertasEnMemoria();
        var marca = new MarcaDeLecturaEnMemoria();
        var yaEnviadas = new AlertasYaEnviadasEnMemoria();
        var inventario = new ClienteDelInventarioFalso { Alertas = [.. Enumerable.Range(0, HorizonteDeAlertas.MaximoPorLectura).Select(i => DelInventario(Guid.NewGuid(), Ahora.AddMinutes(-300 + i)))] };
        var enviar = Crear(reloj, almacen, marca, yaEnviadas, inventario);

        await enviar.Ejecutar();

        Assert.Equal(HorizonteDeAlertas.MaximoPorLectura, yaEnviadas.Ids.Count);
        Assert.Equal(Ahora.AddMinutes(-300 + HorizonteDeAlertas.MaximoPorLectura - 1), marca.Valor);
        Assert.NotEqual(Ahora, marca.Valor);

        // Con menos del tope, la marca vuelve a ser «ahora».
        inventario.Alertas = [DelInventario(Guid.NewGuid(), Ahora.AddMinutes(-1))];
        await enviar.Ejecutar();
        Assert.Equal(Ahora, marca.Valor);
    }

    private static AgruparYEnviarAlertas Crear(RelojFijo reloj, AlmacenDeAlertasEnMemoria almacen, MarcaDeLecturaEnMemoria marca, AlertasYaEnviadasEnMemoria yaEnviadas, ClienteDelInventarioFalso inventario, IEnvioDeCorreoDeAlertas? correo = null)
    {
        var catalogo = CatalogoDeAlertasDeAuditoria.Crear();
        var unidad = new UnidadDeTrabajoDeAuditoria();
        return new AgruparYEnviarAlertas(
            almacen,
            new AlmacenDeCorreoEnMemoria(almacen),
            yaEnviadas,
            marca,
            inventario,
            new DestinatariosFalsos(),
            correo ?? new EnvioDeCorreoFalso(reloj),
            new PublicadorDeAlertas(ServicioDeOrigen.Ecommerce, catalogo, almacen, unidad, reloj),
            catalogo,
            unidad,
            reloj);
    }

    // ---------- Fallos posteriores al envío ----------

    private sealed class CorreoQueRompeElAlmacen(AlmacenDeAlertasEnMemoria almacen, IReloj reloj) : IEnvioDeCorreoDeAlertas
    {
        private readonly EnvioDeCorreoFalso _interno = new(reloj);

        public async Task<ResultadoDeEnvioDeAlerta> Enviar(CorreoDeAlerta correo, IReadOnlyList<DestinatarioDeAlerta> destinatarios, CancellationToken cancelacion = default)
        {
            var resultado = await _interno.Enviar(correo, destinatarios, cancelacion);
            almacen.Falla = new InvalidOperationException("la base dejó de responder");
            return resultado;
        }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-26")]
    public async Task SiElCorreoSalioYFallaAnotarlo_NoCuentaComoFalloDelCorreoNiAbortaLaCorrida()
    {
        var reloj = new RelojFijo(Ahora);
        var almacen = new AlmacenDeAlertasEnMemoria();
        var publicador = new PublicadorDeAlertas(ServicioDeOrigen.Ecommerce, CatalogoDeAlertasDeAuditoria.Crear(), almacen, new UnidadDeTrabajoDeAuditoria(), reloj);
        await ((IPublicadorDeAlertasTecnicas)publicador).Publicar(new AlertaTecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora));
        var enviar = Crear(reloj, almacen, new MarcaDeLecturaEnMemoria(), new AlertasYaEnviadasEnMemoria(), new ClienteDelInventarioFalso(), new CorreoQueRompeElAlmacen(almacen, reloj));

        var resultado = await enviar.Ejecutar();

        Assert.Equal(1, resultado.Enviadas);
        Assert.Equal(0, resultado.Fallidas);
    }

    // ---------- Páginas sin fin ----------

    private sealed class InventarioSinFin : IClienteDeAuditoriaDelInventario
    {
        public int Llamadas { get; private set; }

        public Task<RespuestaDelInventario<PaginaDeRegistros>> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default)
        {
            Llamadas++;
            return Task.FromResult(RespuestaDelInventario<PaginaDeRegistros>.ConValor(new PaginaDeRegistros([], "siempre-el-mismo") { CursorDeLaPagina = "siempre-el-mismo" }));
        }

        public Task<RespuestaDelInventario<IReadOnlyList<AlertaConsultada>>> LeerAlertasNuevas(DateTimeOffset desde, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<RespuestaDelInventario<AlertaConsultada>> MarcarAlertaRevisada(string alertaId, int contadorVisto, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<RespuestaDelInventario<PaginaDeRegistros>> ConsultarHistorial(Guid sucursalPedida, string? cursor, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class LocalVacio : IConsultaLocalDeRegistros
    {
        public Task<PaginaDeRegistros> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default) => Task.FromResult(new PaginaDeRegistros([], null));
    }

    private sealed class IdentidadVacia : IFuenteDeRegistrosAislados
    {
        public Task<PaginaDeRegistros> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default) => Task.FromResult(new PaginaDeRegistros([], null));

        public Task<int> Contar(SolicitudDeConteo solicitud, CancellationToken cancelacion = default) => Task.FromResult(0);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-16")]
    public async Task UnInventarioQueDevuelvePaginasVaciasConElMismoCursor_NoDejaLaSolicitudEnUnBucle_YCuentaComoQueFaltan()
    {
        var inventario = new InventarioSinFin();
        var reloj = new RelojFijo(Ahora);
        var consulta = new ConsultasDeAuditoriaUnificadas(ActorDePrueba.DeAdministradorCentral(), CatalogoDeAccionesDeAuditoria.Crear(), new FirmadorDeCursor(new ClavesDeCursorFalsas(), reloj), new LocalVacio(), new IdentidadVacia(), inventario);

        var resultado = await consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()));

        Assert.True(resultado.EsExito);
        Assert.True(resultado.Valor.FaltanDelInventario);
        Assert.Empty(resultado.Valor.Registros);
        Assert.Null(resultado.Valor.SiguienteCursor);
        Assert.Equal(FuenteFusionadaTope.Paginas, inventario.Llamadas);
    }

    // ---------- Depuración: el estado posterior a la confirmación ----------

    private sealed class EstadoQueNoGuarda(EstadoDeDepuracionEnMemoria interno) : IEstadoDeLaDepuracion
    {
        public Task<EstadoDeLaDepuracion> Obtener(CancellationToken cancelacion = default) => interno.Obtener(cancelacion);

        public Task Guardar(EstadoDeLaDepuracion estado, CancellationToken cancelacion = default) => Task.FromException(new InvalidOperationException("sin base"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task SiNoSePuedeGuardarElEstadoJuntoConElRegistro_LaCorridaFalla_PorqueLaUnidadLosConfirmaOLosRevierteJuntos()
    {
        var reloj = new RelojFijo(Ahora);
        var auditoria = new AuditoriaEspia();
        var anonimizador = new AnonimizadorFalso();
        anonimizador.Respuestas.Enqueue(5);
        var depurar = new DepurarAuditoria([new FuenteDeIdentificadoresFalsa(TipoDePorAnonimizar.CuentaCancelada)], new PorAnonimizarEnMemoria(), anonimizador, [], auditoria, new EstadoQueNoGuarda(new EstadoDeDepuracionEnMemoria()), new BloqueoDeDepuracionFalso(), new AlertasEspia(), ActorDePrueba.DelSistema(), new UnidadDeTrabajoDeAuditoria(), reloj);

        var resultado = await depurar.Ejecutar();

        Assert.Equal(EstadoDeLaCorridaDeDepuracion.Fallida, resultado.Estado);
        Assert.Equal(0, resultado.RegistrosAnonimizados);
    }
}

/// <summary>El tope de páginas por fuente y solicitud, tal como lo declara el fusionador.</summary>
internal static class FuenteFusionadaTope
{
    public const int Paginas = FuenteFusionada.MaximoDePaginasPorSolicitud;
}
