using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using BabelHub.Ecommerce.Infrastructure.Identidad;
using DominioProposito = BabelHub.Ecommerce.Domain.Identidad.PropositoDeCodigo;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// Los trabajos de fondo de Identidad (identidad-seguridad T37 a T40; AC-IDE-19, AC-IDE-33, AC-IDE-53; IDE-19, IDE-36, IDE-46, IDE-54, IDE-64): la purga SQL de lo vencido con PostgreSQL real, la entrega de bajas con
/// <c>FOR UPDATE SKIP LOCKED</c> entre dos instancias y el ejecutor de trabajos (actor del sistema, registro de cada corrida y alerta tras fallos seguidos).
/// </summary>
public sealed class TrabajosDeFondoDeIdentidadTests(PostgresDeIdentidadFixture bd) : IClassFixture<PostgresDeIdentidadFixture>
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    private async Task<Guid> Cuenta()
    {
        var id = Guid.NewGuid();
        await bd.Ejecutar($"INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, creada_en) VALUES ('{id}', 'Ana', 'p{id:N}@example.com', true, 'cliente', true, now())");
        return id;
    }

    private static string Fecha(DateTimeOffset f) => $"'{f.UtcDateTime:yyyy-MM-dd HH:mm:ss}+00'::timestamptz";

    // ---------------------------------------------------------------- purga SQL (T37, T38)

    [Fact]
    [Trait("Criterio", "AC-IDE-19")]
    public async Task LaPurga_QuitaSoloLoVencidoMasAllaDeSuRetencion_YConservaLoVigente()
    {
        var usuario = await Cuenta();
        var purga = new PostgresPurgaDeVencidos(bd.AccesoAlEsquema);
        string Desafio(Guid id, DateTimeOffset vence) => $"INSERT INTO identidad.desafios_de_ingreso (id, huella, usuario_id, modo, creado_en, vence_en) VALUES ('{id}', '{new string('a', 63)}{id.ToString("N")[0]}', '{usuario}', 'presentar-segundo-factor', {Fecha(vence.AddMinutes(-5))}, {Fecha(vence)})";
        var (viejo, reciente, vigente) = (Guid.Parse("a0000000-0000-0000-0000-000000000001"), Guid.Parse("b0000000-0000-0000-0000-000000000002"), Guid.Parse("c0000000-0000-0000-0000-000000000003"));
        await bd.Ejecutar(Desafio(viejo, Ahora.AddHours(-3)) + ";" + Desafio(reciente, Ahora.AddMinutes(-30)) + ";" + Desafio(vigente, Ahora.AddMinutes(3)));

        var quitados = await purga.DesafiosVencidos(Ahora.AddHours(-1));

        Assert.Equal(1, quitados);
        Assert.Equal(2, await bd.Consultar<long>($"SELECT count(*) FROM identidad.desafios_de_ingreso WHERE usuario_id = '{usuario}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-19")]
    public async Task LaPurgaDeCodigosYRenovaciones_ALos30Dias_ConservaLoRecienteYLoVigente()
    {
        var usuario = await Cuenta();
        var purga = new PostgresPurgaDeVencidos(bd.AccesoAlEsquema);
        var codigos = new PostgresCodigosDeUnSoloUso(bd.AccesoAlEsquema);
        await codigos.Emitir(CodigoDeUnSoloUso.Emitir(usuario, DominioProposito.VerificacionDeCorreo, new string('a', 64), Ahora.AddDays(-40)));
        await codigos.Emitir(CodigoDeUnSoloUso.Emitir(usuario, DominioProposito.RecuperacionDeAcceso, new string('b', 64), Ahora.AddMinutes(-3)));
        string Renovacion(char c, DateTimeOffset expira, DateTimeOffset? revocada = null) =>
            $"INSERT INTO identidad.renovaciones (huella, usuario_id, familia, emitida_en, expira_absoluta, revocada_en) VALUES ('{new string(c, 64)}', '{usuario}', '{Guid.NewGuid()}', {Fecha(expira.AddDays(-60))}, {Fecha(expira)}, {(revocada is { } r ? Fecha(r) : "NULL")})";
        await bd.Ejecutar(
            Renovacion('1', Ahora.AddDays(-40)) + ";" + Renovacion('2', Ahora.AddDays(30), Ahora.AddDays(-35)) + ";" + Renovacion('3', Ahora.AddDays(-5)) + ";" + Renovacion('4', Ahora.AddDays(10)));

        Assert.Equal(1, await purga.CodigosDeUnSoloUsoVencidos(Ahora.AddDays(-30)));
        Assert.Equal(2, await purga.RenovacionesVencidas(Ahora.AddDays(-30)));

        Assert.Equal(1, await bd.Consultar<long>($"SELECT count(*) FROM identidad.codigos_de_un_solo_uso WHERE usuario_id = '{usuario}'"));
        Assert.Equal(["3", "4"], await Huellas(usuario));
    }

    private async Task<List<string>> Huellas(Guid usuario)
    {
        var huellas = new List<string>();
        await using var comando = bd.Administracion.CreateCommand($"SELECT left(huella, 1) FROM identidad.renovaciones WHERE usuario_id = '{usuario}' ORDER BY 1");
        await using var filas = await comando.ExecuteReaderAsync();
        while (await filas.ReadAsync())
        {
            huellas.Add(filas.GetString(0));
        }

        return huellas;
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-48")]
    public async Task ElCambioDeCorreoVencido_SePurgaConSuCorreoNuevo_YElVigenteSeConserva()
    {
        var vencido = await Cuenta();
        var vigente = await Cuenta();
        var purga = new PostgresPurgaDeVencidos(bd.AccesoAlEsquema);
        string Cambio(Guid usuario, DateTimeOffset creado) =>
            $"INSERT INTO identidad.cambios_de_correo_pendientes (id, usuario_id, correo_nuevo, creado_en, vence_en, estado) VALUES ('{Guid.NewGuid()}', '{usuario}', 'nuevo{usuario:N}@example.com', {Fecha(creado)}, {Fecha(creado.AddHours(24))}, 'esperando-verificacion')";
        await bd.Ejecutar(Cambio(vencido, Ahora.AddHours(-30)) + ";" + Cambio(vigente, Ahora.AddHours(-2)));

        var quitados = await purga.CambiosDeCorreoVencidos(Ahora);

        Assert.True(quitados >= 1);
        Assert.Equal(0, await bd.Consultar<long>($"SELECT count(*) FROM identidad.cambios_de_correo_pendientes WHERE usuario_id = '{vencido}'"));
        Assert.Equal(1, await bd.Consultar<long>($"SELECT count(*) FROM identidad.cambios_de_correo_pendientes WHERE usuario_id = '{vigente}'"));
    }

    [Fact]
    public async Task LaPurgaCompleta_DeLaAplicacion_QuitaTodoLoVencido_ConElReloj()
    {
        var usuario = await Cuenta();
        await new PostgresCodigosDeUnSoloUso(bd.AccesoAlEsquema).Emitir(CodigoDeUnSoloUso.Emitir(usuario, DominioProposito.VerificacionDeCorreo, new string('c', 64), Ahora.AddDays(-45)));
        await new PostgresRegistroDeSolicitudesDeCodigo(bd.AccesoAlEsquema).Registrar(new string('d', 64), DominioProposito.RecuperacionDeAcceso, Ahora.AddMinutes(-20));

        var resultado = await new PurgarVencidosDeIdentidad(new PostgresPurgaDeVencidos(bd.AccesoAlEsquema), new RelojDePrueba(Ahora)).Ejecutar();

        Assert.True(resultado.Codigos >= 1);
        Assert.True(resultado.SolicitudesDeCodigo >= 1);
        Assert.Equal(0, await bd.Consultar<long>($"SELECT count(*) FROM identidad.codigos_de_un_solo_uso WHERE usuario_id = '{usuario}'"));
    }

    private sealed class RelojDePrueba(DateTimeOffset ahora) : IReloj
    {
        public DateTimeOffset Ahora { get; } = ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    // ---------------------------------------------------------------- la entrega de bajas con dos instancias (T40)

    [Fact]
    [Trait("Criterio", "AC-IDE-53")]
    public async Task DosInstancias_NoTomanLaMismaBaja_ConSkipLocked_DentroDeSuTransaccion()
    {
        await bd.Ejecutar("DELETE FROM identidad.bajas_titulares");
        var bajas = new PostgresRepositorioDeBajas(bd.AccesoAlEsquema);
        for (var i = 0; i < 6; i++)
        {
            await bajas.Agregar(new BajaDeTitular(Guid.NewGuid(), Ahora.AddMinutes(i)));
        }

        var tomadaPorA = new TaskCompletionSource();
        var liberar = new TaskCompletionSource();
        var instanciaA = bd.Unidad.Ejecutar(
            async ct =>
            {
                var tomadas = await bajas.Pendientes(3, ct);
                tomadaPorA.SetResult();
                await liberar.Task;
                return tomadas;
            },
            CancellationToken.None);
        await tomadaPorA.Task;

        var instanciaB = await bd.Unidad.Ejecutar(ct => bajas.Pendientes(10, ct), CancellationToken.None);
        liberar.SetResult();
        var tomadasA = await instanciaA;

        Assert.Equal(3, tomadasA.Count);
        Assert.Equal(3, instanciaB.Count);
        Assert.Empty(tomadasA.Select(b => b.IdentificadorOpaco).Intersect(instanciaB.Select(b => b.IdentificadorOpaco)));
    }

    // ---------------------------------------------------------------- el ejecutor de trabajos

    private sealed class RegistroDePrueba : IRegistroDeTrabajosDeFondo
    {
        public List<(string Trabajo, ResultadoDeTrabajo Resultado)> Corridas { get; } = [];

        public bool Falla { get; set; }

        public Task Registrar(string trabajo, ResultadoDeTrabajo resultado, DateTimeOffset instante, CancellationToken cancelacion = default)
        {
            Corridas.Add((trabajo, resultado));
            return Falla ? throw new InvalidOperationException("Sin registro.") : Task.CompletedTask;
        }
    }

    private sealed class AlcanceDePrueba
    {
        public int Corridas { get; private set; }

        public int Limpiezas { get; private set; }

        public async Task Ejecutar(Func<CancellationToken, Task> corrida, CancellationToken cancelacion)
        {
            Corridas++;
            try
            {
                await corrida(cancelacion);
            }
            finally
            {
                Limpiezas++;
            }
        }
    }


    private sealed class AlertasDePrueba : IAlertas
    {
        public List<string> Tecnicas { get; } = [];

        public bool Falla { get; set; }

        public Task PublicarTecnica(CodigoDeAlerta motivo, CancellationToken cancelacion = default)
        {
            Tecnicas.Add(motivo.Valor);
            return Falla ? throw new InvalidOperationException("Sin alertas.") : Task.CompletedTask;
        }

        public Task PublicarDeSeguridad(CodigoDeAlerta motivo, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private static (TrabajoDeFondoDeIdentidad Trabajo, RegistroDePrueba Registro, AlcanceDePrueba Actor, AlertasDePrueba Alertas) Trabajo(Func<CancellationToken, Task> corrida)
    {
        var registro = new RegistroDePrueba();
        var actor = new AlcanceDePrueba();
        var alertas = new AlertasDePrueba();
        return (new TrabajoDeFondoDeIdentidad("identidad-prueba", corrida, registro, actor.Ejecutar, alertas, new RelojDePrueba(Ahora), new OpcionesDelTrabajoDeIdentidad(TimeSpan.FromMinutes(1))), registro, actor, alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-19")]
    public async Task UnTrabajo_CorreComoSistema_RegistraElExito_YLimpiaElActor()
    {
        var (trabajo, registro, actor, _) = Trabajo(_ => Task.CompletedTask);

        await trabajo.EjecutarUnaVez();

        Assert.Equal([("identidad-prueba", ResultadoDeTrabajo.Exito)], registro.Corridas);
        Assert.Equal(1, actor.Corridas);
        Assert.Equal(1, actor.Limpiezas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-19")]
    public async Task TrasTresFallosSeguidos_AlertaUnaVez_YSeguirFallandoVuelveAAvisarCadaTres_UnExitoReiniciaLaCuenta()
    {
        var falla = true;
        var (trabajo, registro, _, alertas) = Trabajo(_ => falla ? throw new InvalidOperationException("Se cayó.") : Task.CompletedTask);

        for (var i = 0; i < 6; i++)
        {
            await trabajo.EjecutarUnaVez();
        }

        Assert.Equal(2, alertas.Tecnicas.Count);
        Assert.All(alertas.Tecnicas, motivo => Assert.Equal(TrabajoDeFondoDeIdentidad.MotivoDelTrabajoDetenido, motivo));
        Assert.All(registro.Corridas, c => Assert.Equal(ResultadoDeTrabajo.Fallo, c.Resultado));

        falla = false;
        await trabajo.EjecutarUnaVez();
        falla = true;
        await trabajo.EjecutarUnaVez();
        await trabajo.EjecutarUnaVez();
        Assert.Equal(2, alertas.Tecnicas.Count);
    }

    [Fact]
    public async Task SiElRegistroOLaAlertaFallan_ElTrabajoSigue_SinLanzar()
    {
        var (trabajo, registro, _, alertas) = Trabajo(_ => throw new InvalidOperationException("Se cayó."));
        registro.Falla = true;
        alertas.Falla = true;

        for (var i = 0; i < 3; i++)
        {
            await trabajo.EjecutarUnaVez();
        }

        Assert.Equal(3, registro.Corridas.Count);
        Assert.Single(alertas.Tecnicas);
    }

    [Fact]
    public async Task LaCancelacionDelApagado_SePropaga_YElActorSeLimpia()
    {
        using var origen = new CancellationTokenSource();
        var (trabajo, registro, actor, _) = Trabajo(ct =>
        {
            origen.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => trabajo.EjecutarUnaVez(origen.Token));

        Assert.Empty(registro.Corridas);
        Assert.Equal(1, actor.Limpiezas);
    }

    [Fact]
    public async Task CorrerSeDetieneConLaCancelacion_SinLanzar_DespuesDeLaPrimeraCorrida()
    {
        using var origen = new CancellationTokenSource();
        var corridas = 0;
        var (trabajo, _, _, _) = Trabajo(_ =>
        {
            corridas++;
            origen.Cancel();
            return Task.CompletedTask;
        });

        await trabajo.Correr(origen.Token);

        Assert.Equal(1, corridas);
    }

    [Fact]
    public void LosTrabajosDeIdentidad_TienenNombresEstables_IntervalosYLaRenovacionDeClavesMenorQueLaFoto()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), TrabajosDeIdentidad.IntervaloDeLasBajas.Intervalo);
        Assert.True(TrabajosDeIdentidad.IntervaloDeLaRenovacionDeClaves.Intervalo < TimeSpan.FromMinutes(5));
        Assert.True(TrabajosDeIdentidad.IntervaloDeLaPurga.Intervalo <= TimeSpan.FromMinutes(10));
        Assert.Equal(5, new[] { TrabajosDeIdentidad.PurgaDeVencidos, TrabajosDeIdentidad.DescarteDeCancelaciones, TrabajosDeIdentidad.RotacionDeLaClaveDeFirma, TrabajosDeIdentidad.RenovacionDeLasClavesDeVerificacion, TrabajosDeIdentidad.EntregaDeBajas }.Distinct().Count());
    }
}
