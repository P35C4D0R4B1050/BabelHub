using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// Los registros aislados de Identidad (identidad-seguridad T27 y T36; AC-IDE-43, AC-IDE-51; AUD-04, AUD-12, AUD-17, AUD-19, SEG-09): inserción en la transacción de la operación, evento de seguridad con punto de guardado,
/// conteo por acción en una ventana, paginación como los demás registros y depuración con el rol de depuración, todo sobre <c>identidad.registros</c> con PostgreSQL real.
/// </summary>
public sealed class RegistrosAisladosDeIdentidadTests(PostgresDeIdentidadFixture bd) : IClassFixture<PostgresDeIdentidadFixture>
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    private static RegistroAuditado Registro(string accion = "IngresoFallido", Guid? id = null, Guid? actor = null, DateTimeOffset? instante = null) =>
        new(id ?? Guid.NewGuid(), ServicioAuditado.Ecommerce, TipoDeRegistroAuditado.Seguridad, TipoDeActor.Usuario, actor ?? Guid.NewGuid(), "cliente", instante ?? Ahora, accion, ResultadoAuditado.Rechazado);

    private PostgresAlmacenDeRegistrosAislados Almacen() => new(bd.Unidad, bd.Unidad);

    [Fact]
    [Trait("Criterio", "AC-IDE-43")]
    public async Task UnRegistro_SeInsertaEnLaTransaccionDeLaOperacion_YSeRevierteConEla()
    {
        var almacen = Almacen();
        var confirmado = Guid.NewGuid();
        var revertido = Guid.NewGuid();

        await bd.Unidad.Ejecutar(async ct => { await almacen.Insertar(Registro(id: confirmado), ct); return 0; }, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bd.Unidad.Ejecutar<int>(
            async ct =>
            {
                await almacen.Insertar(Registro(id: revertido), ct);
                throw new InvalidOperationException("La operación falló.");
            },
            CancellationToken.None));

        Assert.Equal(1, await bd.Consultar<long>($"SELECT count(*) FROM identidad.registros WHERE id = '{confirmado}'"));
        Assert.Equal(0, await bd.Consultar<long>($"SELECT count(*) FROM identidad.registros WHERE id = '{revertido}'"));
        Assert.Equal(0, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE id = '{confirmado}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-43")]
    public async Task UnEventoQueFallaConPuntoDeGuardado_NoInvalidaLaTransaccionDeQuienLlama()
    {
        var almacen = Almacen();
        var primero = Guid.NewGuid();
        var duplicado = Guid.NewGuid();
        var despues = Guid.NewGuid();

        await bd.Unidad.Ejecutar(
            async ct =>
            {
                await almacen.InsertarAislandoElFallo(Registro(id: primero), ct);
                await almacen.InsertarAislandoElFallo(Registro(id: duplicado), ct);
                await Assert.ThrowsAnyAsync<Exception>(() => almacen.InsertarAislandoElFallo(Registro(id: duplicado), ct));
                await almacen.InsertarAislandoElFallo(Registro(id: despues), ct);
                return 0;
            },
            CancellationToken.None);

        Assert.Equal(3, await bd.Consultar<long>($"SELECT count(*) FROM identidad.registros WHERE id IN ('{primero}', '{duplicado}', '{despues}')"));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-43")]
    public async Task UnaOperacionQueFallaEnElPuntoDeGuardado_DejaValidaLaTransaccion_YElFalloOriginalSeLanza()
    {
        var antes = Guid.NewGuid();
        var despues = Guid.NewGuid();
        var almacen = Almacen();

        await bd.Unidad.Ejecutar(
            async ct =>
            {
                await almacen.Insertar(Registro(id: antes), ct);
                await Assert.ThrowsAsync<Npgsql.PostgresException>(() => BabelHub.Ecommerce.Infrastructure.Auditoria.EscrituraDeAuditoria.EnLaUnidad(
                    bd.Unidad,
                    bd.Unidad,
                    (c, t, token) => RegistrosAisladosDeIdentidad.ConPuntoDeGuardado(
                        c,
                        t,
                        async () =>
                        {
                            await using var comando = new Npgsql.NpgsqlCommand("SELECT 1 / 0", c, t);
                            return await comando.ExecuteScalarAsync(token);
                        },
                        token),
                    ct,
                    RegistrosAisladosDeIdentidad.Esquema));
                await almacen.Insertar(Registro(id: despues), ct);
                return 0;
            },
            CancellationToken.None);

        Assert.Equal(2, await bd.Consultar<long>($"SELECT count(*) FROM identidad.registros WHERE id IN ('{antes}', '{despues}')"));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-51")]
    public async Task ElConteoPorAccion_VaEnUnaVentana_YSoloDevuelveUnNumero()
    {
        var almacen = Almacen();
        var fuente = new PostgresFuenteDeRegistrosAislados(bd.Unidad, bd.Unidad);
        var accion = AccionesAuditables.IngresoFallido;
        var desde = Ahora.AddYears(10);
        await bd.Unidad.Ejecutar(
            async ct =>
            {
                for (var i = 0; i < 21; i++)
                {
                    await almacen.Insertar(Registro("IngresoFallido", instante: desde.AddSeconds(i)), ct);
                }

                await almacen.Insertar(Registro("IngresoFallido", instante: desde.AddMinutes(10)), ct);
                await almacen.Insertar(Registro("BloqueoTemporalAplicado", instante: desde.AddSeconds(5)), ct);
                return 0;
            },
            CancellationToken.None);

        var contados = await fuente.Contar(new SolicitudDeConteo(accion, desde, desde.AddMinutes(5)));

        Assert.Equal(21, contados);
        Assert.Equal(0, await fuente.Contar(new SolicitudDeConteo(accion, desde.AddHours(1), desde.AddHours(1).AddMinutes(5))));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-51")]
    public async Task LaConsulta_PaginaConElMismoOrdenQueLosDemasRegistros_ConSuCursor()
    {
        var almacen = Almacen();
        var fuente = new PostgresFuenteDeRegistrosAislados(bd.Unidad, bd.Unidad);
        var actor = Guid.NewGuid();
        await bd.Unidad.Ejecutar(
            async ct =>
            {
                for (var i = 0; i < 25; i++)
                {
                    await almacen.Insertar(Registro("SegundoFactorFallido", actor: actor, instante: Ahora.AddYears(20).AddSeconds(i)), ct);
                }

                return 0;
            },
            CancellationToken.None);
        var consulta = new ConsultaDeAuditoria(new FiltroDeAuditoria { Accion = "SegundoFactorFallido", UsuarioId = actor });

        var primera = await fuente.Consultar(consulta);
        var segunda = await fuente.Consultar(consulta with { Cursor = primera.SiguienteCursor });

        Assert.Equal(PaginaDeRegistros.TamanoDePagina, primera.Registros.Count);
        Assert.Equal(5, segunda.Registros.Count);
        Assert.All(primera.Registros.Concat(segunda.Registros), r => Assert.Equal(actor, r.ActorId));
        Assert.Equal(25, primera.Registros.Concat(segunda.Registros).Select(r => r.Id).Distinct().Count());
        Assert.True(primera.Registros[0].InstanteUtc >= primera.Registros[^1].InstanteUtc);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-43")]
    public async Task LaDepuracion_AnonimizaLosRegistrosDeUnaCuentaCancelada_ConElRolDeDepuracion()
    {
        var almacen = Almacen();
        var cuenta = Guid.NewGuid();
        var registro = Guid.NewGuid();
        await bd.Unidad.Ejecutar(async ct => { await almacen.Insertar(Registro("IngresoFallido", id: registro, actor: cuenta), ct); return 0; }, CancellationToken.None);
        await bd.Ejecutar($"INSERT INTO identidad.por_anonimizar (identificador_opaco, tipo, registrado_en) VALUES ('{cuenta}', 'cuenta_cancelada', now() - interval '1 day')");

        var depurados = await new PostgresDepuradorDeRegistrosAislados(bd.Depuracion).Depurar();

        Assert.True(depurados >= 1);
        Assert.Equal(0, await bd.Consultar<long>($"SELECT count(*) FROM identidad.registros WHERE actor_id = '{cuenta}'"));
        Assert.NotNull(await bd.Consultar<object>($"SELECT anonimizado_en FROM identidad.registros WHERE id = '{registro}'"));
    }
}
