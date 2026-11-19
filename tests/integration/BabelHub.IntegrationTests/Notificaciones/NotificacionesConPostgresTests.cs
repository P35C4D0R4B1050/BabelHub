using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Notificaciones.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Notificaciones;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Notificaciones;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Npgsql;

namespace BabelHub.IntegrationTests.Notificaciones;

/// <summary>
/// El esquema y los repositorios de Notificaciones sobre PostgreSQL real con el rol <c>rol_notificaciones</c> (transversal-notificaciones T12, T13, T22; AC-NOT-03, AC-NOT-04, AC-NOT-12, AC-NOT-13): ida y vuelta del aviso, la unicidad por
/// evento, destinatario y plantilla con 100 solicitudes simultáneas, el <c>INSERT</c> en un punto de guardado que deja intacta la operación del módulo, dos trabajos que nunca toman el mismo aviso, la escritura condicional que no
/// revierte una cancelación, la depuración de 90 días y los números desactivados por huella con todas las versiones de la clave.
/// </summary>
[Collection(ColeccionPostgresDeNotificaciones.Nombre)]
public sealed class NotificacionesConPostgresTests(PostgresDeNotificacionesFixture bd) : IAsyncLifetime
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    private static int _secuencia;

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static Aviso Nuevo(string? cuenta = null, DateTimeOffset? creado = null, string? evento = null) =>
        Aviso.Crear(
            Guid.NewGuid(),
            evento ?? $"pedido:{Interlocked.Increment(ref _secuencia)}:listo-para-recojo",
            cuenta ?? Guid.NewGuid().ToString("D"),
            TipoDeDestinatario.Cliente,
            "pedido-listo-para-recojo",
            1,
            new Dictionary<string, string> { ["numeroDePedido"] = "BH-000001", ["nombre"] = "María" },
            creado ?? Inicio);

    private Task<bool> Insertar(Aviso aviso) => bd.Unidad.Ejecutar(ct => bd.Avisos.InsertarSiNoExiste(aviso, ct));

    private Task<Aviso?> Leer(Guid id) => bd.Unidad.Ejecutar(ct => bd.Avisos.PorId(id, paraActualizar: false, ct));

    // ---------------------------------------------------------------- ida y vuelta y unicidad (T12, T05)

    [Fact]
    [Trait("Criterio", "AC-NOT-04")]
    public async Task UnAviso_SeGuardaYSeLeeIgual_ConSusCamposCerrados_SinNingunNumero()
    {
        var aviso = Nuevo();
        Assert.True(await Insertar(aviso));

        var leido = await Leer(aviso.Id);

        Assert.Equivalent(aviso, leido, strict: true);
        Assert.Equal("BH-000001", leido!.Campos["numeroDePedido"]);
        Assert.Equal(0, await bd.Consultar<int>("SELECT count(*) FROM notificaciones.avisos WHERE campos::text ~ '[0-9]{9}' OR destinatario_ref ~ '^[+]?[0-9]{9,}$'"));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-04")]
    public async Task ElMismoEventoDestinatarioYPlantilla_SoloSeGuardaUnaVez()
    {
        var primero = Nuevo(evento: "pedido:500:listo-para-recojo");
        var repetido = Aviso.Crear(Guid.NewGuid(), primero.EventoId, primero.DestinatarioRef, primero.Tipo, primero.Plantilla, 1, primero.Campos, Inicio);

        Assert.True(await Insertar(primero));
        Assert.False(await Insertar(repetido));

        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM notificaciones.avisos"));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-04")]
    public async Task CienSolicitudesSimultaneasDelMismoHecho_DejanUnSoloAviso()
    {
        var cuenta = Guid.NewGuid().ToString("D");
        var tareas = Enumerable.Range(0, 100).Select(_ => Task.Run(() => Insertar(Nuevo(cuenta, evento: "pedido:777:listo-para-recojo")))).ToArray();

        var resultados = await Task.WhenAll(tareas);

        Assert.Equal(1, resultados.Count(r => r));
        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM notificaciones.avisos"));
    }

    // ---------------------------------------------------------------- el punto de guardado (NOT-04)

    [Fact]
    [Trait("Criterio", "AC-NOT-03")]
    public async Task UnErrorForzadoDelInsert_NoAbortaLaOperacionDelModulo_QueSeConfirmaIntacta()
    {
        var invalido = Aviso.Restaurar(Guid.NewGuid(), "pedido:9:listo-para-recojo", Guid.NewGuid().ToString("D"), TipoDeDestinatario.Cliente, "pedido-listo-para-recojo", 1, new Dictionary<string, string>(), EstadoDelAviso.Pendiente, 99, Inicio, null, [], 0, null, Inicio, null);
        Exception? capturada = null;

        await bd.Unidad.Ejecutar(
            async ct =>
            {
                // La operación del módulo que pidió el aviso, en su misma transacción.
                await using (var operacion = new NpgsqlCommand("INSERT INTO public.operaciones_de_prueba (id) VALUES (1)", (NpgsqlConnection)bd.Unidad.Conexion, (NpgsqlTransaction)bd.Unidad.Transaccion))
                {
                    await operacion.ExecuteNonQueryAsync(ct);
                }

                try
                {
                    await bd.Avisos.InsertarSiNoExiste(invalido, ct);
                }
                catch (Exception excepcion)
                {
                    capturada = excepcion;
                }

                // Tras el fallo capturado, la transacción sigue viva: otra escritura del módulo y un aviso válido también se guardan.
                await using (var otra = new NpgsqlCommand("INSERT INTO public.operaciones_de_prueba (id) VALUES (2)", (NpgsqlConnection)bd.Unidad.Conexion, (NpgsqlTransaction)bd.Unidad.Transaccion))
                {
                    await otra.ExecuteNonQueryAsync(ct);
                }

                Assert.True(await bd.Avisos.InsertarSiNoExiste(Nuevo(), ct));
                return 0;
            });

        var falla = Assert.IsType<FallaDeLaBaseDeNotificacionesException>(capturada);
        Assert.Equal("23514", falla.CodigoDeEstado);
        Assert.DoesNotContain("pedido:9", falla.Message, StringComparison.Ordinal);
        Assert.Equal(2, await bd.Consultar<int>("SELECT count(*) FROM public.operaciones_de_prueba"));
        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM notificaciones.avisos"));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-04")]
    public async Task UnAvisoPedidoEnUnaTransaccionQueSeRevierte_NoQuedaGuardado()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => bd.Unidad.Ejecutar<int>(
            async ct =>
            {
                await bd.Avisos.InsertarSiNoExiste(Nuevo(), ct);
                throw new InvalidOperationException("La operación del módulo falló después de pedir el aviso.");
            }));

        Assert.Equal(0, await bd.Consultar<int>("SELECT count(*) FROM notificaciones.avisos"));
    }

    // ---------------------------------------------------------------- dos trabajos, una escritura condicional (T22, T08)

    [Fact]
    [Trait("Criterio", "AC-NOT-12")]
    public async Task DosTrabajosSimultaneos_NuncaTomanElMismoAviso()
    {
        for (var i = 0; i < 6; i++)
        {
            Assert.True(await Insertar(Nuevo(creado: Inicio.AddSeconds(i))));
        }

        var primeroTomo = new TaskCompletionSource();
        var segundoTermino = new TaskCompletionSource();
        var primero = bd.Unidad.Ejecutar(
            async ct =>
            {
                var tomados = await bd.Avisos.TomarParaEnviar(Inicio.AddMinutes(1), 3, ct);
                primeroTomo.SetResult();
                await segundoTermino.Task;
                return tomados.Select(a => a.Id).ToList();
            });
        await primeroTomo.Task;
        var segundo = await Task.Run(() => bd.Unidad.Ejecutar(async ct => (await bd.Avisos.TomarParaEnviar(Inicio.AddMinutes(1), 6, ct)).Select(a => a.Id).ToList()));
        segundoTermino.SetResult();

        var delPrimero = await primero;
        Assert.Equal(3, delPrimero.Count);
        Assert.Equal(3, segundo.Count);
        Assert.Empty(delPrimero.Intersect(segundo));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-12")]
    public async Task TomarParaEnviar_SoloTomaLoPendienteQueLlegoSuFecha_LoArrendadoVencidoYLoVencidoPorEdad()
    {
        var futuro = Nuevo(evento: "pedido:1:entregado");
        var listo = Nuevo(evento: "pedido:2:entregado");
        var enEnvioVigente = Nuevo(evento: "pedido:3:entregado");
        var enEnvioVencido = Nuevo(evento: "pedido:4:entregado");
        var viejo = Nuevo(creado: Inicio.AddHours(-25), evento: "pedido:5:entregado");
        foreach (var aviso in new[] { futuro, listo, enEnvioVigente, enEnvioVencido, viejo })
        {
            await Insertar(aviso);
        }

        futuro.AplazarSinConsumir(Inicio.AddMinutes(10));
        enEnvioVigente.IniciarEnvio(Inicio, Inicio.AddMinutes(2));
        enEnvioVencido.IniciarEnvio(Inicio, Inicio.AddMinutes(-1));
        foreach (var aviso in new[] { futuro, enEnvioVigente, enEnvioVencido })
        {
            await bd.Unidad.Ejecutar(async ct => { await bd.Avisos.Guardar(aviso, ct); return 0; });
        }

        var tomados = await bd.Unidad.Ejecutar(ct => bd.Avisos.TomarParaEnviar(Inicio, 10, ct));

        Assert.Equivalent(new[] { listo.Id, enEnvioVencido.Id, viejo.Id }, tomados.Select(a => a.Id));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-03")]
    public async Task LaEscrituraCondicional_NoRevierteUnaCancelacionQueLlegoMientrasSeAtendia()
    {
        var aviso = Nuevo();
        await Insertar(aviso);
        aviso.IniciarEnvio(Inicio, Inicio.AddMinutes(2));
        await bd.Unidad.Ejecutar(async ct => { await bd.Avisos.Guardar(aviso, ct); return 0; });

        // Identidad cancela por su lado (otra instancia leída de la base) mientras el trabajo sigue con la suya.
        var delCancelador = (await Leer(aviso.Id))!;
        Assert.True(delCancelador.Cancelar("destinatario-cambiado", Inicio.AddSeconds(5)));
        await bd.Unidad.Ejecutar(async ct => { await bd.Avisos.Guardar(delCancelador, ct); return 0; });

        aviso.EsperarLaEntrega(Inicio.AddSeconds(6));
        var guardado = await bd.Unidad.Ejecutar(ct => bd.Avisos.GuardarSiNoEsFinal(aviso, ct));

        Assert.False(guardado);
        Assert.Equal(EstadoDelAviso.Cancelado, (await Leer(aviso.Id))!.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-06")]
    public async Task LaBase_ObligaAVaciarLosCamposYLosCupos_AlTerminarElAviso_YNoDejaCambiarElEstadoSinFecha()
    {
        var aviso = Nuevo();
        await Insertar(aviso);

        var conCamposAlTerminar = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE notificaciones.avisos SET estado = 'enviado', fecha_final = now() WHERE id = '{aviso.Id}'"));
        var sinFecha = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE notificaciones.avisos SET estado = 'cancelado', campos = '{{}}'::jsonb WHERE id = '{aviso.Id}'"));

        Assert.Equal("23514", conCamposAlTerminar.SqlState);
        Assert.Equal("23514", sinFecha.SqlState);
        Assert.Equal(EstadoDelAviso.Pendiente, (await Leer(aviso.Id))!.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-12")]
    public async Task ElCicloCompleto_DeUnAviso_PasaPorPendienteEnvioYEnviado_YSeLeeSinCamposNiCupos()
    {
        var aviso = Nuevo();
        aviso.ReservarLosCupos([new CupoDelTope("avisos-por-numero-por-hora", "n:v1:abc", "id-1", Inicio)]);
        await Insertar(aviso);
        Assert.Single((await Leer(aviso.Id))!.Cupos);

        aviso.IniciarEnvio(Inicio, Inicio.AddMinutes(2));
        aviso.MarcarEnviado(Inicio.AddMinutes(1));
        Assert.True(await bd.Unidad.Ejecutar(ct => bd.Avisos.GuardarSiNoEsFinal(aviso, ct)));

        var leido = (await Leer(aviso.Id))!;
        Assert.Equal(EstadoDelAviso.Enviado, leido.Estado);
        Assert.Empty(leido.Campos);
        Assert.Empty(leido.Cupos);
        Assert.Equal(Inicio.AddMinutes(1), leido.FechaFinal);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-07")]
    public async Task NoFinalesDe_DevuelveSoloLosPendientesYEnEnvioDelDestinatario()
    {
        var cuenta = Guid.NewGuid().ToString("D");
        var pendiente = Nuevo(cuenta);
        var terminado = Nuevo(cuenta);
        var deOtro = Nuevo();
        foreach (var a in new[] { pendiente, terminado, deOtro })
        {
            await Insertar(a);
        }

        terminado.Cancelar("x", Inicio);
        await bd.Unidad.Ejecutar(async ct => { await bd.Avisos.Guardar(terminado, ct); return 0; });

        var no_finales = await bd.Unidad.Ejecutar(ct => bd.Avisos.NoFinalesDe(TipoDeDestinatario.Cliente, cuenta, ct));

        Assert.Equal([pendiente.Id], no_finales.Select(a => a.Id));
    }

    // ---------------------------------------------------------------- consulta y depuración (T21, T12)

    [Fact]
    [Trait("Criterio", "AC-NOT-05")]
    public async Task Listar_PaginaDelMasRecienteAlMasAntiguo_ConElTotalYElFiltroPorEstado()
    {
        var avisos = Enumerable.Range(0, 5).Select(i => Nuevo(creado: Inicio.AddMinutes(i))).ToList();
        foreach (var a in avisos)
        {
            await Insertar(a);
        }

        avisos[0].Cancelar("x", Inicio);
        await bd.Unidad.Ejecutar(async ct => { await bd.Avisos.Guardar(avisos[0], ct); return 0; });

        var pagina = await bd.Unidad.Ejecutar(ct => bd.Avisos.Listar(null, 1, 2, ct));
        var cancelados = await bd.Unidad.Ejecutar(ct => bd.Avisos.Listar(EstadoDelAviso.Cancelado, 1, 20, ct));
        var segunda = await bd.Unidad.Ejecutar(ct => bd.Avisos.Listar(null, 2, 2, ct));

        Assert.Equal(5, pagina.Total);
        Assert.Equal([avisos[4].Id, avisos[3].Id], pagina.Avisos.Select(a => a.Id));
        Assert.Equal([avisos[2].Id, avisos[1].Id], segunda.Avisos.Select(a => a.Id));
        Assert.Equal(1, cancelados.Total);
        Assert.Equal(avisos[0].Id, Assert.Single(cancelados.Avisos).Id);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-04")]
    public async Task LaDepuracion_EliminaLosTerminadosHaceMasDe90Dias_YConservaLosPendientesYLosRecientes()
    {
        var viejo = Nuevo();
        var reciente = Nuevo();
        var pendiente = Nuevo(creado: Inicio.AddDays(-200));
        foreach (var a in new[] { viejo, reciente, pendiente })
        {
            await Insertar(a);
        }

        viejo.Cancelar("x", Inicio.AddDays(-91));
        reciente.Cancelar("x", Inicio.AddDays(-89));
        foreach (var a in new[] { viejo, reciente })
        {
            await bd.Unidad.Ejecutar(async ct => { await bd.Avisos.Guardar(a, ct); return 0; });
        }

        var eliminados = await bd.Unidad.Ejecutar(ct => bd.Avisos.PurgarFinalesAnterioresA(Inicio.AddDays(-90), ct));

        Assert.Equal(1, eliminados);
        Assert.Null(await Leer(viejo.Id));
        Assert.NotNull(await Leer(reciente.Id));
        Assert.NotNull(await Leer(pendiente.Id));
    }

    // ---------------------------------------------------------------- los permisos del rol

    [Fact]
    [Trait("Criterio", "AC-NOT-06")]
    public async Task LaCuentaDeAcceso_NoLeeElEsquema_SinPasarPorElRolDeNotificaciones()
    {
        await using var comando = bd.Acceso.CreateCommand("SELECT count(*) FROM notificaciones.avisos");

        var falla = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteScalarAsync());

        Assert.Equal("42501", falla.SqlState);
    }

    // ---------------------------------------------------------------- los números desactivados (T12, T13, T24)

    private static HuellaDeNumero Huella(int version, string texto = "aaaaaaaaaaaaaaaaaaaaaaaa") => new(version, texto);

    [Fact]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task UnNumeroDesactivado_SeGuardaUnaVez_SeReconoceEnCualquierVersionVigente_YSeReactiva()
    {
        var v1 = Huella(1, "huella-de-la-clave-uno-0000000000");
        var v2 = Huella(2, "huella-de-la-clave-dos-0000000000");

        Assert.True(await bd.Unidad.Ejecutar(ct => bd.Numeros.Agregar(new NumeroDesactivado(v1, Inicio, "a pedido del titular"), ct)));
        Assert.False(await bd.Unidad.Ejecutar(ct => bd.Numeros.Agregar(new NumeroDesactivado(v1, Inicio, "otra vez"), ct)));

        // Tras rotar la clave, las huellas vigentes son la nueva y la vieja: el número sigue excluido.
        Assert.True(await bd.Unidad.Ejecutar(ct => bd.Numeros.ExisteAlguna([v2, v1], ct)));
        Assert.False(await bd.Unidad.Ejecutar(ct => bd.Numeros.ExisteAlguna([v2], ct)));
        Assert.False(await bd.Unidad.Ejecutar(ct => bd.Numeros.ExisteAlguna([], ct)));

        Assert.Equal(1, await bd.Unidad.Ejecutar(ct => bd.Numeros.Eliminar([v2, v1], ct)));
        Assert.False(await bd.Unidad.Ejecutar(ct => bd.Numeros.ExisteAlguna([v1], ct)));
        Assert.Equal(0, await bd.Consultar<int>("SELECT count(*) FROM notificaciones.numeros_desactivados"));
    }

    [Theory]
    [InlineData("987654321")]
    [InlineData("987 654 321")]
    [InlineData("+51 987-654-321")]
    [InlineData("0051987654321")]
    [InlineData("٩٨٧٦٥٤٣٢١")]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task LaDesactivacion_ReconoceElNumeroEscritoDeCualquierForma_YDespuesDeRotarLaClave(string formato)
    {
        var antes = new CalculadorDeHuellaDeNumero(new OpcionesDeHuellaDeNumero(1, Clave('a')), Plataforma);
        Assert.True(NumeroDeDestino.TryNormalizar("+51 987 654 321", out var original));
        await bd.Unidad.Ejecutar(ct => bd.Numeros.Agregar(new NumeroDesactivado(antes.Actual(original), Inicio, "a pedido del titular"), ct));

        var despues = new CalculadorDeHuellaDeNumero(new OpcionesDeHuellaDeNumero(2, Clave('b'), 1, Clave('a')), Plataforma);
        Assert.True(NumeroDeDestino.TryNormalizar(formato, out var escrito));

        Assert.True(await bd.Unidad.Ejecutar(ct => bd.Numeros.ExisteAlguna(despues.Vigentes(escrito), ct)));
        Assert.Equal(2, despues.Vigentes(escrito).Count);
        Assert.NotEqual(antes.Actual(original).Valor, despues.Actual(original).Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task UnNumeroDistinto_NoQuedaDesactivado()
    {
        var calculador = new CalculadorDeHuellaDeNumero(new OpcionesDeHuellaDeNumero(1, Clave('a')), Plataforma);
        Assert.True(NumeroDeDestino.TryNormalizar("987654321", out var uno));
        Assert.True(NumeroDeDestino.TryNormalizar("987654322", out var otro));
        await bd.Unidad.Ejecutar(ct => bd.Numeros.Agregar(new NumeroDesactivado(calculador.Actual(uno), Inicio, "a pedido del titular"), ct));

        Assert.False(await bd.Unidad.Ejecutar(ct => bd.Numeros.ExisteAlguna(calculador.Vigentes(otro), ct)));
        Assert.DoesNotContain("987654321", await bd.Consultar<string>("SELECT string_agg(huella_numero || motivo, ',') FROM notificaciones.numeros_desactivados") ?? string.Empty, StringComparison.Ordinal);
    }

    private static readonly CalculadorDeHuellaHmac Plataforma = new(new OpcionesDeHuella("v1:" + Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray())));

    private static byte[] Clave(char caracter) => System.Text.Encoding.UTF8.GetBytes(new string(caracter, 16) + "0123456789abcdefghij");
}
