using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;
using BabelHub.Ecommerce.Infrastructure.Biblioteca;

namespace BabelHub.IntegrationTests.Biblioteca;

/// <summary>
/// Los casos de uso reales de Biblioteca sobre PostgreSQL real con solicitudes simultáneas (T21, T23; BIB-05, BIB-17, BIB-22 a BIB-29, AC-BIB-15, AC-BIB-17, AC-BIB-20 a AC-BIB-26): el cupo con una descarga restante, una misma clave repetida a la vez, la entrega frente a un reembolso automático y frente a la cancelación de
/// la cuenta (sin interbloqueo ni error 500 ni registros con el usuario), la revocación repetida, la cancelación con falla y reintento, las depuraciones y la auditoría caída.
/// </summary>
[Collection(ColeccionPostgresDeBiblioteca.Nombre)]
public sealed class ConcurrenciaYCancelacionDeBibliotecaTests(PostgresDeBibliotecaFixture bd) : IAsyncLifetime
{
    private static readonly string ClaveDelArchivo = new('a', 32);

    private EntornoIntegradoDeBiblioteca _entorno = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _entorno = new EntornoIntegradoDeBiblioteca(bd, new EntregaDeArchivosEnMemoria());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Clave(int n) => $"clave-de-prueba-{n:D6}-0000000000";

    [Fact]
    [Trait("Criterio", "AC-BIB-15")]
    public async Task Con_una_descarga_restante_veinte_solicitudes_simultaneas_dan_un_solo_enlace_y_el_contador_queda_en_cinco()
    {
        var cuenta = Guid.NewGuid();
        var (acceso, _, _) = await _entorno.Comprar(cuenta, ClaveDelArchivo);
        for (var i = 0; i < 4; i++)
        {
            await bd.Unidad.Ejecutar(token => bd.Accesos.ConsumirCupo(acceso, cuenta, token));
        }

        var resultados = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => _entorno.Pedir(cuenta, acceso, Clave(i))));

        Assert.Equal(1, resultados.Count(r => r.EsExito));
        Assert.All(resultados.Where(r => !r.EsExito), r => Assert.Equal(ClavesDeBiblioteca.LimiteDeDescargas, r.Error!.Clave));
        Assert.Equal(5L, await bd.Consultar<long>($"SELECT descargas_usadas::bigint FROM biblioteca.accesos WHERE id = '{acceso}'"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.descargas"));
    }

    [Theory]
    [Trait("Criterio", "AC-BIB-04")]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(0)]
    public async Task Veinte_solicitudes_simultaneas_con_la_misma_clave_cuentan_una_sola_descarga_y_entregan_el_mismo_enlace(int restantesAntes)
    {
        var cuenta = Guid.NewGuid();
        var (acceso, _, _) = await _entorno.Comprar(cuenta, ClaveDelArchivo);
        var previas = 5 - restantesAntes;
        for (var i = 0; i < previas; i++)
        {
            await bd.Unidad.Ejecutar(token => bd.Accesos.ConsumirCupo(acceso, cuenta, token));
        }

        var resultados = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => _entorno.Pedir(cuenta, acceso, "la-misma-clave-0123456789abcdef")));

        if (restantesAntes == 0)
        {
            // Con las cinco usadas y una clave nueva: ninguna recibe el enlace.
            Assert.All(resultados, r => Assert.False(r.EsExito));
            Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.descargas"));
            return;
        }

        Assert.All(resultados, r => Assert.True(r.EsExito, r.Error?.Clave));
        Assert.Single(resultados.Select(r => r.Valor.Url.AbsoluteUri).Distinct());
        Assert.Equal((long)(previas + 1), await bd.Consultar<long>($"SELECT descargas_usadas::bigint FROM biblioteca.accesos WHERE id = '{acceso}'"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.descargas"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.claves_de_descarga"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-05")]
    public async Task La_quinta_descarga_repetida_con_la_misma_clave_devuelve_el_mismo_enlace_aunque_el_cupo_este_agotado()
    {
        var cuenta = Guid.NewGuid();
        var (acceso, _, _) = await _entorno.Comprar(cuenta, ClaveDelArchivo);
        for (var i = 0; i < 4; i++)
        {
            await bd.Unidad.Ejecutar(token => bd.Accesos.ConsumirCupo(acceso, cuenta, token));
        }

        var quinta = await _entorno.Pedir(cuenta, acceso, "la-quinta-clave-0123456789abcdef");
        _entorno.Reloj.Ahora = _entorno.Reloj.Ahora.AddSeconds(90);
        var reintento = await _entorno.Pedir(cuenta, acceso, "la-quinta-clave-0123456789abcdef");
        var otra = await _entorno.Pedir(cuenta, acceso, "una-clave-distinta-0123456789abcd");

        Assert.Equal(quinta.Valor.Url, reintento.Valor.Url);
        Assert.Equal(ClavesDeBiblioteca.LimiteDeDescargas, otra.Error!.Clave);
        Assert.Equal(5L, await bd.Consultar<long>($"SELECT descargas_usadas::bigint FROM biblioteca.accesos WHERE id = '{acceso}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-20")]
    public async Task Cincuenta_pares_de_entrega_y_revocacion_sin_enlace_nunca_dejan_un_enlace_sobre_un_acceso_revocado()
    {
        var compras = new List<(Guid Cuenta, Guid Acceso, Guid Pedido, Guid Edicion)>();
        for (var i = 0; i < 50; i++)
        {
            var cuenta = Guid.NewGuid();
            var (acceso, edicion, pedido) = await _entorno.Comprar(cuenta, ClaveDelArchivo);
            compras.Add((cuenta, acceso, pedido, edicion));
        }

        var entregas = new List<Task<Resultado<EnlaceDeDescarga>>>();
        var revocaciones = new List<Task<Resultado<ResultadoDeRevocarSiNoHayEnlace>>>();
        foreach (var lote in compras.Chunk(10))
        {
            // Lotes de 10 pares (20 conexiones simultáneas): PostgreSQL de pruebas admite 100 y el resto lo usan las demás pruebas.
            var delLote = new List<Task>();
            foreach (var compra in lote)
            {
                var entrega = _entorno.Pedir(compra.Cuenta, compra.Acceso, "clave-del-par-0123456789abcdef");
                var revocacion = Task.Run(() => bd.Unidad.Ejecutar(token => _entorno.ParaDevoluciones.RevocarSiNoHayEnlace(compra.Cuenta, compra.Pedido, compra.Edicion, token)));
                entregas.Add(entrega);
                revocaciones.Add(revocacion);
                delLote.Add(entrega);
                delLote.Add(revocacion);
            }

            await Task.WhenAll(delLote);
        }

        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.accesos WHERE estado = 'revocado' AND enlaces_entregados > 0"));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.accesos WHERE estado = 'vigente' AND enlaces_entregados = 0 AND descargas_usadas > 0"));
        for (var i = 0; i < compras.Count; i++)
        {
            // O recibió el enlace y el reembolso se rechazó, o quedó revocado y la descarga se rechazó: nunca las dos cosas.
            var recibio = (await entregas[i]).EsExito;
            var revocado = (await revocaciones[i]).Valor == ResultadoDeRevocarSiNoHayEnlace.Revocado;
            Assert.NotEqual(recibio, revocado);
        }

        Assert.Equal(compras.Count(_ => true), (int)await bd.Consultar<long>("SELECT count(*) FROM biblioteca.accesos"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-26")]
    public async Task La_entrega_frente_a_la_cancelacion_de_la_cuenta_no_deja_registros_con_el_usuario_ni_interbloqueos()
    {
        var cuentas = new List<(Guid Cuenta, Guid Acceso)>();
        for (var i = 0; i < 30; i++)
        {
            var cuenta = Guid.NewGuid();
            var (acceso, _, _) = await _entorno.Comprar(cuenta, ClaveDelArchivo);
            // Una clave vencida preexistente: la entrega la reemplaza (el ON CONFLICT … DO UPDATE bloquea esa fila hija) mientras la cancelación borra en cascada.
            await bd.Unidad.Ejecutar(token => bd.Descargas.RegistrarClave(
                new ClaveRegistrada(acceso, ClaveDeDescarga.Crear("la-clave-vencida-0123456789abcdef")!, _entorno.Reloj.Ahora.AddHours(-2), _entorno.Reloj.Ahora.AddHours(-2).AddSeconds(300), Guid.NewGuid()),
                _entorno.Reloj.Ahora.AddHours(-2),
                token));
            cuentas.Add((cuenta, acceso));
        }

        var resultados = new List<object>();
        foreach (var lote in cuentas.Chunk(10))
        {
            var tareas = lote.SelectMany(c => new[]
            {
                _entorno.Pedir(c.Cuenta, c.Acceso, "la-clave-vencida-0123456789abcdef").ContinueWith(t => (object)t.Result),
                Task.Run(() => bd.Unidad.Ejecutar(async token =>
                {
                    await _entorno.Cancelacion.Aplicar(c.Cuenta, Guid.NewGuid(), token);
                    return (object)"cancelada";
                })),
            });
            resultados.AddRange(await Task.WhenAll(tareas));
        }

        // Ninguna excepción (un interbloqueo o un 23503 sin traducir habría fallado la tarea) y nada queda con las cuentas canceladas.
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.accesos"));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.descargas WHERE usuario_id IS NOT NULL OR acceso_id IS NOT NULL"));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.claves_de_descarga"));
        foreach (var r in resultados.OfType<Resultado<EnlaceDeDescarga>>().Where(r => !r.EsExito))
        {
            Assert.Equal(ClavesDeBiblioteca.NoEncontrado, r.Error!.Clave);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-21")]
    public async Task Anular_el_pedido_viejo_dos_veces_tras_recomprar_con_el_nuevo_deja_vigente_el_acceso_y_audita_una_sola_revocacion()
    {
        var cuenta = Guid.NewGuid();
        var (acceso, edicion, pedido1) = await _entorno.Comprar(cuenta, ClaveDelArchivo);
        var pedido2 = Guid.NewGuid();

        async Task<ResultadoDeRevocarAcceso> Anular() =>
            (await bd.Unidad.Ejecutar(token => _entorno.Revocar.Ejecutar(new RevocarAccesoEntrada(pedido1, edicion, MotivoDeRevocacionDeAcceso.PedidoAnulado), token))).Valor;

        Assert.Equal(ResultadoDeRevocarAcceso.Revocado, await Anular());
        Assert.Equal(ResultadoDeConcederAcceso.Reactivado, (await bd.Unidad.Ejecutar(token => _entorno.Conceder.Ejecutar(new ConcederAccesoEntrada(cuenta, pedido2, edicion), token))).Valor);
        Assert.Equal(ResultadoDeRevocarAcceso.YaRevocado, await Anular());
        Assert.Equal(ResultadoDeRevocarAcceso.YaRevocado, await Anular());

        Assert.Equal("vigente", await bd.Consultar<string>($"SELECT estado FROM biblioteca.accesos WHERE id = '{acceso}'"));
        Assert.Single(_entorno.Auditoria.Registradas);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-17")]
    public async Task Muchas_rondas_de_aprobaciones_simultaneas_del_mismo_pedido_nunca_fallan_con_la_restriccion_unica_y_dejan_un_acceso_por_ronda()
    {
        // La carrera de 23505 sobre (pedido_id, edicion_id) solo asoma con repeticiones: 15 rondas de 12 concesiones simultáneas con el mismo pedido.
        for (var ronda = 0; ronda < 15; ronda++)
        {
            var cuenta = Guid.NewGuid();
            var edicion = _entorno.Catalogo.Sembrar(ClaveDelArchivo);
            var pedido = Guid.NewGuid();

            var resultados = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => bd.Unidad.Ejecutar(token => _entorno.Conceder.Ejecutar(new ConcederAccesoEntrada(cuenta, pedido, edicion), token)))));

            Assert.Equal(1, resultados.Count(r => r.Valor == ResultadoDeConcederAcceso.Concedido));
            Assert.Equal(11, resultados.Count(r => r.Valor == ResultadoDeConcederAcceso.YaConcedidoPorEstePedido));
        }

        Assert.Equal(15L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.accesos"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-17")]
    public async Task Dos_aprobaciones_simultaneas_del_mismo_pedido_dejan_un_solo_acceso()
    {
        var cuenta = Guid.NewGuid();
        var edicion = _entorno.Catalogo.Sembrar(ClaveDelArchivo);
        var pedido = Guid.NewGuid();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => bd.Unidad.Ejecutar(token => _entorno.Conceder.Ejecutar(new ConcederAccesoEntrada(cuenta, pedido, edicion), token)))));

        Assert.Equal(1, resultados.Count(r => r.Valor == ResultadoDeConcederAcceso.Concedido));
        Assert.Equal(1, resultados.Count(r => r.Valor == ResultadoDeConcederAcceso.YaConcedidoPorEstePedido));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.accesos"));
    }

    // ---------- Cancelación de la cuenta y depuraciones (T23) ----------

    private async Task<(Guid Cuenta, Guid Vigente, Guid Revocado)> CuentaConHistorial()
    {
        var cuenta = Guid.NewGuid();
        var (vigente, _, _) = await _entorno.Comprar(cuenta, ClaveDelArchivo, "Vigente");
        var (revocado, edicion, pedido) = await _entorno.Comprar(cuenta, ClaveDelArchivo, "Revocado");
        await bd.Unidad.Ejecutar(token => _entorno.Revocar.Ejecutar(new RevocarAccesoEntrada(pedido, edicion, MotivoDeRevocacionDeAcceso.Reembolso), token));
        await _entorno.Pedir(cuenta, vigente, "clave-con-historial-0123456789abc");
        await _entorno.ComoCliente(cuenta, () => _entorno.PedirAyuda.Ejecutar(new PedirAyudaEntrada(vigente, "otro")));
        return (cuenta, vigente, revocado);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-22")]
    public async Task Cancelar_elimina_accesos_incluido_el_revocado_registro_ayudas_y_claves_de_esa_cuenta_y_quien_se_registra_luego_ve_la_biblioteca_vacia()
    {
        var (cuenta, _, _) = await CuentaConHistorial();
        var (otra, _, _) = await CuentaConHistorial();
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM biblioteca.descargas WHERE usuario_id = '{cuenta}'"));

        await bd.Unidad.Ejecutar(async token =>
        {
            await _entorno.Cancelacion.Aplicar(cuenta, Guid.NewGuid(), token);
            return 0;
        });

        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM biblioteca.accesos WHERE cuenta_id = '{cuenta}'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM biblioteca.descargas WHERE usuario_id = '{cuenta}'"));
        Assert.Equal(2L, await bd.Consultar<long>($"SELECT count(*) FROM biblioteca.accesos WHERE cuenta_id = '{otra}'"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.solicitudes_de_ayuda"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.claves_de_descarga"));

        var nuevaCuenta = Guid.NewGuid();
        var lista = await _entorno.ComoCliente(nuevaCuenta, () => _entorno.Listar.Ejecutar(new ListarMiBibliotecaEntrada(1, 20)));
        Assert.Equal(0, lista.Valor.Total);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-25")]
    public async Task Si_la_transaccion_de_identidad_falla_despues_de_la_parte_de_biblioteca_todo_se_revierte_y_se_puede_reintentar()
    {
        var (cuenta, _, _) = await CuentaConHistorial();

        await Assert.ThrowsAsync<InvalidOperationException>(() => bd.Unidad.Ejecutar<int>(async token =>
        {
            await _entorno.Cancelacion.Aplicar(cuenta, Guid.NewGuid(), token);

            // Otro módulo falla después: la excepción revierte también lo que Biblioteca ya eliminó.
            throw new InvalidOperationException("falla de otro módulo");
        }));

        Assert.Equal(2L, await bd.Consultar<long>($"SELECT count(*) FROM biblioteca.accesos WHERE cuenta_id = '{cuenta}'"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM biblioteca.descargas WHERE usuario_id = '{cuenta}'"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.solicitudes_de_ayuda"));

        await bd.Unidad.Ejecutar(async token =>
        {
            await _entorno.Cancelacion.Aplicar(cuenta, Guid.NewGuid(), token);
            return 0;
        });
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM biblioteca.accesos WHERE cuenta_id = '{cuenta}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-23")]
    public async Task La_depuracion_de_dos_anios_y_un_dia_frente_a_dos_anios_menos_un_dia_deja_solo_edicion_y_dia_audita_sin_datos_personales_y_repetir_no_cambia_nada()
    {
        var cuenta = Guid.NewGuid();
        var (acceso, edicion, _) = await _entorno.Comprar(cuenta, ClaveDelArchivo);
        // Hoy en Lima: 2026-11-20 (el reloj de la prueba).
        _entorno.Reloj.Ahora = new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
        var viejo = new RegistroDeDescarga(Guid.NewGuid(), acceso, cuenta, edicion, _entorno.Reloj.Ahora.AddYears(-2), new DateOnly(2024, 11, 19));
        var reciente = new RegistroDeDescarga(Guid.NewGuid(), acceso, cuenta, edicion, _entorno.Reloj.Ahora.AddYears(-2), new DateOnly(2024, 11, 21));
        await bd.Unidad.Ejecutar(async token =>
        {
            await bd.Descargas.Registrar(viejo, token);
            await bd.Descargas.Registrar(reciente, token);
            return 0;
        });

        var primera = await _entorno.ComoSistema(() => _entorno.DepurarRegistros.Ejecutar());
        var segunda = await _entorno.ComoSistema(() => _entorno.DepurarRegistros.Ejecutar());

        Assert.Equal(1, primera.Valor);
        Assert.Equal(0, segunda.Valor);
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM biblioteca.descargas WHERE id = '{viejo.Id}' AND acceso_id IS NULL AND usuario_id IS NULL AND entregada_en IS NULL AND dia = DATE '2024-11-19'"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM biblioteca.descargas WHERE id = '{reciente.Id}' AND acceso_id IS NOT NULL"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM biblioteca.accesos WHERE id = '{acceso}' AND estado = 'vigente'"));
        var entrada = Assert.Single(_entorno.Auditoria.Registradas);
        Assert.Equal(["cantidad"], entrada.Detalle.Keys);
        Assert.DoesNotContain(cuenta.ToString(), string.Join('|', entrada.Detalle.Values), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-24")]
    public async Task La_depuracion_de_ayudas_elimina_la_abierta_de_dos_anios_y_un_dia_y_deja_la_cerrada_de_dos_anios_menos_un_dia_y_sin_auditoria_no_hace_nada()
    {
        var cuenta = Guid.NewGuid();
        var (acceso, _, _) = await _entorno.Comprar(cuenta, ClaveDelArchivo);
        _entorno.Reloj.Ahora = new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
        var abierta = Guid.NewGuid();
        var cerrada = Guid.NewGuid();
        await bd.Ejecutar($"""
            INSERT INTO biblioteca.solicitudes_de_ayuda (id, acceso_id, motivo, estado, creada_en, cerrada_en) VALUES
              ('{abierta}', '{acceso}', 'otro', 'abierta', '2024-11-19T15:00:00Z', NULL),
              ('{cerrada}', '{acceso}', 'otro', 'cerrada', '2024-11-21T15:00:00Z', '2024-11-22T15:00:00Z')
            """);

        _entorno.Auditoria.Caida = true;
        var sinAuditoria = await _entorno.ComoSistema(() => _entorno.DepurarAyudas.Ejecutar());
        Assert.False(sinAuditoria.EsExito);
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.solicitudes_de_ayuda"));

        _entorno.Auditoria.Caida = false;
        var depurada = await _entorno.ComoSistema(() => _entorno.DepurarAyudas.Ejecutar());

        Assert.Equal(1, depurada.Valor);
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM biblioteca.solicitudes_de_ayuda WHERE id = '{cerrada}'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM biblioteca.solicitudes_de_ayuda WHERE id = '{abierta}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-22")]
    public async Task Cancelar_antes_de_pedir_ayuda_y_pedirla_a_la_vez_no_deja_ayudas_huerfanas()
    {
        var (cuenta, vigente, _) = await CuentaConHistorial();
        await bd.Ejecutar("DELETE FROM biblioteca.solicitudes_de_ayuda");

        var tareas = new List<Task>
        {
            Task.Run(() => _entorno.ComoCliente(cuenta, () => _entorno.PedirAyuda.Ejecutar(new PedirAyudaEntrada(vigente, "archivo-no-abre")))),
            Task.Run(() => bd.Unidad.Ejecutar(async token =>
            {
                await _entorno.Cancelacion.Aplicar(cuenta, Guid.NewGuid(), token);
                return 0;
            })),
        };

        await Task.WhenAll(tareas);

        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.solicitudes_de_ayuda s WHERE NOT EXISTS (SELECT 1 FROM biblioteca.accesos a WHERE a.id = s.acceso_id)"));
    }
}
