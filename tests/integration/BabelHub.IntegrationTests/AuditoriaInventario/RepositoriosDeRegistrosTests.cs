using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Auditoria;
using BabelHub.Inventario.Infrastructure.Auditoria;
using BabelHub.Inventario.Infrastructure.Plataforma;
using Npgsql;

namespace BabelHub.IntegrationTests.AuditoriaInventario;

/// <summary>
/// Los repositorios de registros contra PostgreSQL real y los roles reales (T25; AUD-03, AUD-04, AUD-05, AUD-07, AUD-09, AUD-12, AUD-30; AC-AUD-03, AC-AUD-06, AC-AUD-21, AC-AUD-27): la auditoría y la
/// operación se confirman o se revierten juntas, un evento que no concede nada sobrevive a la reversión, un acceso denegado es un único evento por correlación y la lista pagina sobre una
/// instantánea sin repetir ni omitir.
/// </summary>
public sealed class RepositoriosDeRegistrosTests(PostgresDeAuditoriaFixture bd) : IClassFixture<PostgresDeAuditoriaFixture>
{
    private static readonly CatalogoDeAcciones Catalogo = PostgresDeAuditoriaFixture.Catalogo;

    private sealed class RelojDePrueba(DateTimeOffset ahora) : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class OrigenDePrueba : IOrigenDeSolicitud
    {
        public OrigenDeSolicitud Actual => OrigenDeSolicitud.Desconocido;
    }

    private sealed class Entorno
    {
        public Entorno(PostgresDeAuditoriaFixture bd, ActorActual? actor = null)
        {
            Unidad = new UnidadDeTrabajoPostgres(bd.Acceso);
            Actores = new ActorDelFlujo();
            var propio = actor ?? ActorActual.DeUsuario(Guid.NewGuid(), "administrador-central");
            UsuarioId = propio.UsuarioId ?? Guid.Empty;
            Actores.Establecer(propio);
            Constructor = new ConstructorDeRegistros(ServicioDeOrigen.Inventario, Catalogo, Reloj, Actores, new OrigenDePrueba());
            Registros = new PostgresAlmacenDeRegistros(Unidad, Unidad);
            Auditoria = new AuditoriaDeAplicacion(Constructor, Registros);
            Limitador = new LimitadorDeEscrituras();
            Eventos = new PostgresAlmacenDeEventosDeSeguridad(bd.Acceso, Limitador);
            Denegaciones = new PostgresEscritorDeDenegaciones(bd.Acceso, Limitador);
            Consulta = new PostgresConsultaLocalDeRegistros(Unidad, Unidad);
        }

        public RelojDePrueba Reloj { get; } = new(DateTimeOffset.UtcNow);

        public Guid UsuarioId { get; private set; }

        public UnidadDeTrabajoPostgres Unidad { get; }

        public ActorDelFlujo Actores { get; }

        public ConstructorDeRegistros Constructor { get; }

        public PostgresAlmacenDeRegistros Registros { get; }

        public AuditoriaDeAplicacion Auditoria { get; }

        public LimitadorDeEscrituras Limitador { get; }

        public PostgresAlmacenDeEventosDeSeguridad Eventos { get; }

        public PostgresEscritorDeDenegaciones Denegaciones { get; }

        public PostgresConsultaLocalDeRegistros Consulta { get; }

        public RegistroDeAuditoria Crear(string accion, DateTimeOffset? instante = null, string? motivo = null, string? correlacion = null, string? sucursal = null, Dictionary<string, string?>? detalle = null)
        {
            var solicitud = new SolicitudDeRegistro(new AccionAuditable(accion))
            {
                Motivo = motivo,
                SucursalOrigen = sucursal is null ? null : Guid.Parse(sucursal),
                Detalle = detalle ?? new Dictionary<string, string?>(),
            };
            var definicion = Constructor.Resolver(solicitud, Catalogo.Obtener(accion).Clase);
            var registro = Constructor.Construir(solicitud, definicion, correlacion: correlacion, anonimoSinContexto: true);
            return instante is null
                ? registro
                : RegistroDeAuditoria.Crear(registro.Id, registro.Servicio, definicion, registro.Actor, instante.Value, registro.ObjetoTipo, registro.ObjetoId, registro.SucursalOrigen, registro.SucursalDestino, registro.Motivo, registro.Origen, null, registro.Detalle, registro.Resultado, registro.Correlacion);
        }
    }

    private async Task<long> Contar(string condicion) => await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE {condicion}");

    // ---------- Auditoría y operación juntas (AUD-04, AC-AUD-03) ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-03")]
    public async Task LaAuditoriaSeConfirmaConLaOperacion_YSeRevierteSiLaOperacionFalla()
    {
        var e = new Entorno(bd);
        var objeto = Guid.NewGuid().ToString("D");
        var solicitud = new SolicitudDeRegistro(AccionesAuditables.AjusteRegistrado) { ObjetoTipo = "ajuste", ObjetoId = objeto, Detalle = new Dictionary<string, string?> { ["cantidadAnterior"] = "5", ["cantidadPosterior"] = "3" } };

        await e.Unidad.Ejecutar(
            async ct =>
            {
                await e.Auditoria.Registrar(solicitud, ct);
                return 0;
            });
        Assert.Equal(1L, await Contar($"objeto_id = '{objeto}'"));

        var revertido = Guid.NewGuid().ToString("D");
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Unidad.Ejecutar<int>(
            async ct =>
            {
                await e.Auditoria.Registrar(solicitud with { ObjetoId = revertido }, ct);
                throw new InvalidOperationException("el ajuste falló después de auditar");
            }));
        Assert.Equal(0L, await Contar($"objeto_id = '{revertido}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-03")]
    public async Task SiLaTablaDeAuditoriaFalla_LaOperacionNoSeAplica_ConAuditoriaNoDisponible()
    {
        var e = new Entorno(bd);
        var objeto = Guid.NewGuid().ToString("D");
        await bd.Ejecutar("CREATE OR REPLACE FUNCTION auditoria.fallar_para_la_prueba() RETURNS trigger LANGUAGE plpgsql AS $f$ BEGIN IF NEW.objeto_id = '" + objeto + "' THEN RAISE EXCEPTION 'tabla caida'; END IF; RETURN NEW; END $f$; DROP TRIGGER IF EXISTS fallar_para_la_prueba ON auditoria.registros; CREATE TRIGGER fallar_para_la_prueba BEFORE INSERT ON auditoria.registros FOR EACH ROW EXECUTE FUNCTION auditoria.fallar_para_la_prueba();");
        try
        {
            var aplicado = false;
            await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => e.Unidad.Ejecutar(
                async ct =>
                {
                    await e.Auditoria.Registrar(new SolicitudDeRegistro(AccionesAuditables.AjusteRegistrado) { ObjetoTipo = "ajuste", ObjetoId = objeto }, ct);
                    aplicado = true;
                    return 0;
                }));

            Assert.False(aplicado);
            Assert.Equal(0L, await Contar($"objeto_id = '{objeto}'"));
        }
        finally
        {
            await bd.Ejecutar("DROP TRIGGER IF EXISTS fallar_para_la_prueba ON auditoria.registros; DROP FUNCTION IF EXISTS auditoria.fallar_para_la_prueba();");
        }
    }

    // ---------- Eventos que no conceden nada (AUD-12) ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-03")]
    public async Task UnEventoDeSeguridadSobreviveALaReversionDeLaOperacionQueLoOriginoYNoDejaAbortadaSuTransaccion()
    {
        var e = new Entorno(bd);
        var registro = e.Crear("OrigenIndeterminable", motivo: "origen-no-determinable");

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Unidad.Ejecutar<int>(
            async ct =>
            {
                await e.Eventos.Insertar(registro, ct);
                throw new InvalidOperationException("la operación se revierte");
            }));

        Assert.Equal(1L, await Contar($"id = '{registro.Id}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-10")]
    public async Task SiElEventoNoSePuedeEscribir_LaTransaccionDelLlamanteSigueValida_YElErrorSeLanzaParaQueSePublique()
    {
        var e = new Entorno(bd);
        var duplicado = e.Crear("OrigenIndeterminable", motivo: "origen-no-determinable");
        await e.Eventos.Insertar(duplicado);

        // Mismo identificador: viola la clave primaria; la transacción del llamante no queda abortada.
        var valor = await e.Unidad.Ejecutar(
            async ct =>
            {
                await Assert.ThrowsAsync<PostgresException>(() => e.Eventos.Insertar(duplicado, ct));
                return await bd.Consultar<int>("SELECT 41 + 1");
            });

        Assert.Equal(42, valor);
    }

    // ---------- Acceso denegado: un único evento por correlación (AUD-05) ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-21")]
    public async Task UnAccesoDenegadoEsUnSoloEventoPorCorrelacionYMotivo_AunqueSeEscribaVariasVecesYEnParalelo()
    {
        var e = new Entorno(bd);
        var correlacion = "corr" + Guid.NewGuid().ToString("N")[..20];

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => e.Denegaciones.Escribir(e.Crear("AccesoDenegado", motivo: "fuera-de-rol", correlacion: correlacion))));

        Assert.Equal(1L, await Contar($"correlacion = '{correlacion}'"));
        await e.Denegaciones.Escribir(e.Crear("AccesoDenegado", motivo: "recurso-ajeno", correlacion: correlacion));
        Assert.Equal(2L, await Contar($"correlacion = '{correlacion}'"));
    }

    // ---------- Tope de concurrencia con cortacircuitos ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public async Task ElLimitadorRechazaSinTocarLaBaseAlAgotarseElTopeYAbreElCortacircuitosTrasFallosSeguidos()
    {
        var limitador = new LimitadorDeEscrituras(concurrenciaMaxima: 2, fallosParaAbrir: 3);
        var liberar = new TaskCompletionSource();
        var ocupadas = new[] { limitador.Ejecutar(async () => { await liberar.Task; return 1; }), limitador.Ejecutar(async () => { await liberar.Task; return 2; }) };

        await Assert.ThrowsAsync<EscrituraSaturadaException>(() => limitador.Ejecutar(() => Task.FromResult(3)));
        liberar.SetResult();
        await Task.WhenAll(ocupadas);

        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => limitador.Ejecutar<int>(() => throw new InvalidOperationException("la base falla")));
        }

        // Con el cortacircuitos abierto, ni siquiera se intenta.
        var intentada = false;
        await Assert.ThrowsAsync<EscrituraSaturadaException>(() => limitador.Ejecutar(() => { intentada = true; return Task.FromResult(4); }));
        Assert.False(intentada);
    }

    // ---------- Lista paginada con instantánea (AUD-07, AUD-09, AUD-30) ----------

    /// <summary>Un instante lejano y propio de cada prueba: la base se comparte entre las pruebas de la clase y los rangos no se pisan.</summary>
    private static DateTimeOffset Unico(int dias) => DateTimeOffset.UtcNow.AddDays(dias * 40).AddMinutes(-Random.Shared.Next(60, 500_000));

    private static string Marca() => "m" + Guid.NewGuid().ToString("N")[..10];

    private async Task<List<Guid>> Sembrar(Entorno e, string marca, int cuantos, DateTimeOffset desde, string accion = "PedidoAnulado")
    {
        var ids = new List<Guid>();
        for (var i = 0; i < cuantos; i++)
        {
            var registro = e.Crear(accion, instante: desde.AddSeconds(-i), detalle: null);
            await e.Registros.Insertar(registro);
            ids.Add(registro.Id);
        }

        _ = marca;
        return ids;
    }

    [Theory]
    [Trait("Criterio", "AC-AUD-06")]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    [InlineData("100%")]
    [InlineData("a_c")]
    public async Task LosComodinesDeLikeEnElTextoBuscadoSeTratanComoTextoLiteral(string texto)
    {
        var e = new Entorno(bd);
        var instante = Unico(-8);
        var literal = e.Crear("PedidoAnulado", instante: instante, detalle: new Dictionary<string, string?> { ["causa"] = "x" + texto + "y" });
        var parecido = e.Crear("PedidoAnulado", instante: instante, detalle: new Dictionary<string, string?> { ["causa"] = "xabcdy" });
        await e.Registros.Insertar(literal);
        await e.Registros.Insertar(parecido);
        var rango = new FiltroDeAuditoria { Accion = "PedidoAnulado", Desde = instante.AddMinutes(-1), Hasta = instante.AddMinutes(1), Texto = texto };

        var resultado = await e.Consulta.Consultar(new ConsultaDeAuditoria(rango));

        Assert.Contains(literal.Id, resultado.Registros.Select(r => r.Id));
        Assert.DoesNotContain(parecido.Id, resultado.Registros.Select(r => r.Id));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-06")]
    public async Task ElTextoBuscadoConComillasYPuntoYComaNoEsSqlInyectable()
    {
        var e = new Entorno(bd);
        var filtro = new FiltroDeAuditoria { Texto = "x'; DROP TABLE auditoria.registros; --" };

        var pagina = await e.Consulta.Consultar(new ConsultaDeAuditoria(filtro));

        Assert.Empty(pagina.Registros);
        Assert.True(await bd.Consultar<bool>("SELECT to_regclass('auditoria.registros') IS NOT NULL"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-06")]
    public async Task ElRegistroSeLeeTalComoSeEscribio_ConSuDetalleYSusClavesPersonales()
    {
        var e = new Entorno(bd);
        var instante = Unico(-7);
        var original = e.Crear("PedidoAnulado", instante: instante, motivo: null, detalle: new Dictionary<string, string?> { ["causa"] = "duplicado" });
        await e.Registros.Insertar(original);

        var leido = (await e.Consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria { Accion = "PedidoAnulado", Desde = instante.AddMinutes(-1), Hasta = instante.AddMinutes(1) }))).Registros.Single(r => r.Id == original.Id);
        var contrato = original.AModeloDeContrato();

        Assert.Equal(contrato.Accion, leido.Accion);
        Assert.Equal(contrato.InstanteUtc.UtcTicks / 10, leido.InstanteUtc.UtcTicks / 10);
        Assert.Equal(contrato.ActorId, leido.ActorId);
        Assert.Equal(contrato.ActorRol, leido.ActorRol);
        Assert.Equal(contrato.Resultado, leido.Resultado);
        Assert.Equal(contrato.Tipo, leido.Tipo);
        Assert.Contains("duplicado", leido.Detalle, StringComparison.Ordinal);
        Assert.Equal(contrato.Servicio, leido.Servicio);
        Assert.Equal(instante.UtcTicks / 10, leido.InstanteUtc.UtcTicks / 10);
    }
}
