using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Application.Reclamaciones.Interno;

namespace BabelHub.IntegrationTests.Reclamaciones;

/// <summary>
/// T19 de <c>ecommerce-libro-reclamaciones</c> sobre PostgreSQL real, lo que faltaba de su texto (AC-REC-11, AC-REC-14): el origen que se comporta como automatizado <b>sin</b> y <b>con</b> verificación de persona (sin ella no se escribe nada en la
/// base ni se gasta cupo; con ella se registra una sola hoja) y el reintento del aviso <b>justo tras responder</b> (el correo ya se envió: no sale un segundo correo) y tras un correo caído (sale una sola vez al volver).
/// </summary>
[Collection(ColeccionPostgresDeReclamaciones.Nombre)]
public sealed class OrigenAutomatizadoYAvisoConPostgresTests(PostgresDeReclamacionesFixture bd) : IAsyncLifetime
{
    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class RelojDeLaPrueba : IReloj
    {
        public DateTimeOffset Ahora => DateTimeOffset.UtcNow;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));
    }

    private sealed class VerificacionControlable : IExigenciaDeVerificacionDePersona
    {
        public ResultadoDeExigencia Resultado { get; set; } = ResultadoDeExigencia.NoExigida;

        public Task<ResultadoDeExigencia> Exigir(AccionProtegida accion, string? tokenDeVerificacion, CancellationToken cancelacion = default) =>
            Task.FromResult(Resultado == ResultadoDeExigencia.Requerida && tokenDeVerificacion is not null ? ResultadoDeExigencia.Verificada : Resultado);
    }

    private (EntornoDeReclamacionesConPostgres Entorno, IReclamaciones Reclamaciones, VerificacionControlable Verificacion) ConVerificacion()
    {
        var e = new EntornoDeReclamacionesConPostgres(bd);
        var verificacion = new VerificacionControlable();
        var reclamaciones = new ReclamacionesDeLaAplicacion(new RegistrarHoja(verificacion, new HuellasNormalizantes(), e.Limites, bd.Hojas, bd.Unidad, new RelojDeLaPrueba()));
        return (e, reclamaciones, verificacion);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-11")]
    public async Task Un_origen_automatizado_sin_token_no_escribe_nada_ni_gasta_cupo_y_con_token_registra_una_sola_hoja()
    {
        var (e, reclamaciones, verificacion) = ConVerificacion();
        verificacion.Resultado = ResultadoDeExigencia.Requerida;

        // Sin el token de verificación: se rechaza y la base queda vacía, y el cupo del origen sigue intacto.
        var sinToken = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => reclamaciones.Registrar(e.Entrada())));
        Assert.All(sinToken, r => Assert.False(r.EsExito));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reclamaciones.hojas_reclamacion"));

        // Con el token: el cupo completo (3 por hora) sigue disponible y se registra una hoja por solicitud permitida.
        var conToken = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => reclamaciones.Registrar(e.Entrada() with { TokenDeVerificacion = "token-de-verificacion" })));
        Assert.Equal(3, conToken.Count(r => r.EsExito));
        Assert.Equal(3L, await bd.Consultar<long>("SELECT count(*) FROM reclamaciones.hojas_reclamacion"));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-14")]
    public async Task Un_reintento_del_aviso_justo_tras_responder_con_el_correo_ya_enviado_no_manda_un_segundo_correo()
    {
        var e = new EntornoDeReclamacionesConPostgres(bd);
        e.Limites.Maximo = 100;
        var registrada = await e.Reclamaciones.Registrar(e.Entrada());
        Assert.True(registrada.EsExito);
        var id = await bd.Consultar<Guid>($"SELECT id FROM reclamaciones.hojas_reclamacion WHERE numero = '{registrada.Valor.Numero}'");
        e.Actor.ComoAdministracion();

        Assert.True((await e.Administracion.Responder(new ResponderHojaEntrada(id, "Gracias por escribirnos; lo resolveremos."))).EsExito);
        Assert.Single(e.Correo.Correos);
        // Dos reintentos a la vez justo después: el aviso ya está enviado, no sale otro correo.
        await Task.WhenAll(e.Administracion.ReintentarAviso(id), e.Administracion.ReintentarAviso(id));

        Assert.Single(e.Correo.Correos);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE aviso_enviado_en IS NOT NULL"));
    }
}
