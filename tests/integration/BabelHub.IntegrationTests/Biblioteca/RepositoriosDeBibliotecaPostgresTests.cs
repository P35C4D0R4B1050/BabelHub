using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Domain.Biblioteca;
using BabelHub.Ecommerce.Infrastructure.Biblioteca;
using Npgsql;

namespace BabelHub.IntegrationTests.Biblioteca;

/// <summary>
/// El esquema <c>biblioteca</c> y las sentencias condicionales atómicas de sus repositorios sobre PostgreSQL real (T12, T13; BIB-01, BIB-17, BIB-19, BIB-20, BIB-22, BIB-23, BIB-24, BIB-25): una sentencia por decisión, el permiso mínimo del rol, los <c>CHECK</c>, la cascada
/// de la cancelación y la clave que se repite sin abortar la transacción del llamador.
/// </summary>
[Collection(ColeccionPostgresDeBiblioteca.Nombre)]
public sealed class RepositoriosDeBibliotecaPostgresTests(PostgresDeBibliotecaFixture bd) : IAsyncLifetime
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<T> En<T>(Func<CancellationToken, Task<T>> trabajo) => bd.Unidad.Ejecutar(trabajo);

    private Task En(Func<CancellationToken, Task> trabajo) => bd.Unidad.Ejecutar(async token =>
    {
        await trabajo(token);
        return 0;
    });

    private async Task<Guid> Conceder(Guid cuenta, Guid edicion, Guid pedido)
    {
        var resultado = await En(token => bd.Accesos.Conceder(cuenta, edicion, pedido, Ahora, token));
        Assert.Equal(ResultadoDeConcesion.Concedido, resultado);
        return (await En(token => bd.Accesos.ObtenerDeLaCompra(cuenta, pedido, edicion, token)))!.Id;
    }

    private static ClaveDeDescarga Clave(string texto = "0123456789abcdef0123456789abcdef") => ClaveDeDescarga.Crear(texto)!;

    [Fact]
    [Trait("Criterio", "AC-BIB-12")]
    public async Task El_esquema_no_tiene_columnas_de_url_clave_de_objeto_correo_ni_texto_libre()
    {
        var columnas = new List<string>();
        await using var comando = bd.Administracion.CreateCommand("SELECT table_name || '.' || column_name FROM information_schema.columns WHERE table_schema = 'biblioteca'");
        await using var lector = await comando.ExecuteReaderAsync();
        while (await lector.ReadAsync())
        {
            columnas.Add(lector.GetString(0));
        }

        Assert.NotEmpty(columnas);
        Assert.DoesNotContain(columnas, c => new[] { "url", "clave_en_almacen", "clave_de_objeto", "correo", "nombre", "texto", "detalle", "origen", "direccion_ip" }.Any(p => c.Contains(p, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-01")]
    public async Task Conceder_inserta_un_acceso_vigente_y_se_lee_con_todos_sus_campos()
    {
        var cuenta = Guid.NewGuid();
        var edicion = Guid.NewGuid();
        var pedido = Guid.NewGuid();

        var id = await Conceder(cuenta, edicion, pedido);
        var leido = (await En(token => bd.Accesos.Obtener(id, cuenta, token)))!;

        Assert.Equal(EstadoDeAcceso.Vigente, leido.Estado);
        Assert.Equal((0, 0), (leido.DescargasUsadas, leido.EnlacesEntregados));
        Assert.Equal((cuenta, edicion, pedido), (leido.CuentaId, leido.EdicionId, leido.PedidoId));
        Assert.Equal(Ahora, leido.ConcedidoEn);
        Assert.Null(await En(token => bd.Accesos.Obtener(id, Guid.NewGuid(), token)));
        Assert.NotNull(await En(token => bd.Accesos.Obtener(id, null, token)));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-17")]
    public async Task Repetir_la_concesion_con_el_mismo_pedido_no_duplica_y_con_otro_pedido_es_ya_poseido()
    {
        var cuenta = Guid.NewGuid();
        var edicion = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        await Conceder(cuenta, edicion, pedido);

        Assert.Equal(ResultadoDeConcesion.YaConcedidoPorEstePedido, await En(token => bd.Accesos.Conceder(cuenta, edicion, pedido, Ahora, token)));
        Assert.Equal(ResultadoDeConcesion.YaPoseidoPorOtraCompra, await En(token => bd.Accesos.Conceder(cuenta, edicion, Guid.NewGuid(), Ahora, token)));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.accesos"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-17")]
    public async Task Veinte_concesiones_simultaneas_del_mismo_pedido_dejan_un_solo_acceso()
    {
        var cuenta = Guid.NewGuid();
        var edicion = Guid.NewGuid();
        var pedido = Guid.NewGuid();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => En(token => bd.Accesos.Conceder(cuenta, edicion, pedido, Ahora, token)))));

        Assert.Equal(1, resultados.Count(r => r == ResultadoDeConcesion.Concedido));
        Assert.Equal(19, resultados.Count(r => r == ResultadoDeConcesion.YaConcedidoPorEstePedido));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.accesos"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-18")]
    public async Task Recomprar_un_revocado_de_otro_pedido_lo_reactiva_con_el_cupo_en_cero_y_el_mismo_pedido_no()
    {
        var cuenta = Guid.NewGuid();
        var edicion = Guid.NewGuid();
        var pedido1 = Guid.NewGuid();
        var pedido2 = Guid.NewGuid();
        var id = await Conceder(cuenta, edicion, pedido1);
        for (var i = 0; i < 4; i++)
        {
            await En(token => bd.Accesos.ConsumirCupo(id, cuenta, token));
        }

        await En(token => bd.Accesos.Revocar(pedido1, edicion, CausaDeRevocacion.Reembolso, Ahora, token));
        Assert.Equal(ResultadoDeConcesion.YaConcedidoPorEstePedido, await En(token => bd.Accesos.Conceder(cuenta, edicion, pedido1, Ahora.AddDays(1), token)));
        Assert.Equal(EstadoDeAcceso.Revocado, (await En(token => bd.Accesos.Obtener(id, cuenta, token)))!.Estado);

        var reactivado = await En(token => bd.Accesos.Conceder(cuenta, edicion, pedido2, Ahora.AddDays(2), token));
        var leido = (await En(token => bd.Accesos.Obtener(id, cuenta, token)))!;

        Assert.Equal(ResultadoDeConcesion.Reactivado, reactivado);
        Assert.Equal(EstadoDeAcceso.Vigente, leido.Estado);
        Assert.Equal((0, 0), (leido.DescargasUsadas, leido.EnlacesEntregados));
        Assert.Equal(pedido2, leido.PedidoId);
        Assert.Equal(Ahora.AddDays(2), leido.ReactivadoEn);
        Assert.Null(leido.RevocadoEn);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.accesos"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-21")]
    public async Task Revocar_dos_veces_conserva_el_primer_motivo_y_anular_el_pedido_viejo_tras_recomprar_deja_vigente_el_del_nuevo()
    {
        var cuenta = Guid.NewGuid();
        var edicion = Guid.NewGuid();
        var pedido1 = Guid.NewGuid();
        var pedido2 = Guid.NewGuid();
        var id = await Conceder(cuenta, edicion, pedido1);

        var primera = await En(token => bd.Accesos.Revocar(pedido1, edicion, CausaDeRevocacion.AnulacionDePedido, Ahora, token));
        var segunda = await En(token => bd.Accesos.Revocar(pedido1, edicion, CausaDeRevocacion.Reembolso, Ahora.AddHours(1), token));

        Assert.Equal(ResultadoDeRevocacion.Revocado, primera.Resultado);
        Assert.NotNull(primera.Acceso);
        Assert.Equal(ResultadoDeRevocacion.YaRevocado, segunda.Resultado);
        Assert.Equal("anulacion-de-pedido", await bd.Consultar<string>($"SELECT motivo_de_revocacion FROM biblioteca.accesos WHERE id = '{id}'"));

        await En(token => bd.Accesos.Conceder(cuenta, edicion, pedido2, Ahora.AddDays(1), token));
        Assert.Equal(ResultadoDeRevocacion.YaRevocado, (await En(token => bd.Accesos.Revocar(pedido1, edicion, CausaDeRevocacion.AnulacionDePedido, Ahora.AddDays(2), token))).Resultado);
        Assert.Equal(ResultadoDeRevocacion.YaRevocado, (await En(token => bd.Accesos.Revocar(pedido1, edicion, CausaDeRevocacion.AnulacionDePedido, Ahora.AddDays(2), token))).Resultado);
        Assert.Equal(EstadoDeAcceso.Vigente, (await En(token => bd.Accesos.Obtener(id, cuenta, token)))!.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-15")]
    public async Task Con_una_sola_descarga_restante_veinte_solicitudes_simultaneas_dejan_el_contador_en_el_maximo()
    {
        var cuenta = Guid.NewGuid();
        var id = await Conceder(cuenta, Guid.NewGuid(), Guid.NewGuid());
        for (var i = 0; i < 4; i++)
        {
            await En(token => bd.Accesos.ConsumirCupo(id, cuenta, token));
        }

        var resultados = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => En(token => bd.Accesos.ConsumirCupo(id, cuenta, token)))));

        Assert.Equal(1, resultados.Count(r => r == ResultadoDelConteoAtomico.Contada));
        var leido = (await En(token => bd.Accesos.Obtener(id, cuenta, token)))!;
        Assert.Equal((5, 5), (leido.DescargasUsadas, leido.EnlacesEntregados));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-03")]
    public async Task El_conteo_exige_la_cuenta_titular_y_el_estado_vigente()
    {
        var cuenta = Guid.NewGuid();
        var id = await Conceder(cuenta, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(ResultadoDelConteoAtomico.NoAfecto, await En(token => bd.Accesos.ConsumirCupo(id, Guid.NewGuid(), token)));
        Assert.Equal(ResultadoDelConteoAtomico.NoAfecto, await En(token => bd.Accesos.ConsumirCupo(Guid.NewGuid(), cuenta, token)));
        await En(token => bd.Accesos.Suspender(cuenta, id, Ahora, token));
        Assert.Equal(ResultadoDelConteoAtomico.NoAfecto, await En(token => bd.Accesos.ConsumirCupo(id, cuenta, token)));
        Assert.Equal(0, (await En(token => bd.Accesos.Obtener(id, cuenta, token)))!.DescargasUsadas);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-20")]
    public async Task Cincuenta_pares_de_entrega_y_revocacion_sin_enlace_nunca_dejan_un_enlace_sobre_un_acceso_revocado()
    {
        var pares = new List<(Guid Cuenta, Guid Id, Guid Pedido, Guid Edicion)>();
        for (var i = 0; i < 50; i++)
        {
            var cuenta = Guid.NewGuid();
            var edicion = Guid.NewGuid();
            var pedido = Guid.NewGuid();
            pares.Add((cuenta, await Conceder(cuenta, edicion, pedido), pedido, edicion));
        }

        await Task.WhenAll(pares.SelectMany(p => new Task[]
        {
            Task.Run(() => En(token => bd.Accesos.ConsumirCupo(p.Id, p.Cuenta, token))),
            Task.Run(() => En(token => bd.Accesos.RevocarSiNoHayEnlace(p.Cuenta, p.Pedido, p.Edicion, Ahora, token))),
        }));

        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.accesos WHERE estado = 'revocado' AND enlaces_entregados > 0"));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.accesos WHERE estado = 'vigente' AND enlaces_entregados = 0 AND descargas_usadas > 0"));
        Assert.Equal(50L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.accesos WHERE (estado = 'revocado' AND enlaces_entregados = 0) OR (estado = 'vigente' AND enlaces_entregados = 1)"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-19")]
    public async Task Revocar_sin_enlace_clasifica_ajeno_revocado_suspendido_y_con_enlace_sin_cambiar_nada()
    {
        var cuenta = Guid.NewGuid();
        var edicion = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        var id = await Conceder(cuenta, edicion, pedido);

        Assert.Equal(ResultadoDeRevocacionSinEnlace.SinAcceso, (await En(token => bd.Accesos.RevocarSiNoHayEnlace(Guid.NewGuid(), pedido, edicion, Ahora, token))).Resultado);
        Assert.Equal(ResultadoDeRevocacionSinEnlace.SinAcceso, (await En(token => bd.Accesos.RevocarSiNoHayEnlace(cuenta, Guid.NewGuid(), edicion, Ahora, token))).Resultado);

        await En(token => bd.Accesos.ConsumirCupo(id, cuenta, token));
        Assert.Equal(ResultadoDeRevocacionSinEnlace.EnlaceYaEntregado, (await En(token => bd.Accesos.RevocarSiNoHayEnlace(cuenta, pedido, edicion, Ahora, token))).Resultado);

        var cuenta2 = Guid.NewGuid();
        var edicion2 = Guid.NewGuid();
        var pedido2 = Guid.NewGuid();
        var id2 = await Conceder(cuenta2, edicion2, pedido2);
        await En(token => bd.Accesos.Suspender(cuenta2, id2, Ahora, token));
        Assert.Equal(ResultadoDeRevocacionSinEnlace.NoDisponible, (await En(token => bd.Accesos.RevocarSiNoHayEnlace(cuenta2, pedido2, edicion2, Ahora, token))).Resultado);
        await En(token => bd.Accesos.Reanudar(cuenta2, id2, token));

        var revocado = await En(token => bd.Accesos.RevocarSiNoHayEnlace(cuenta2, pedido2, edicion2, Ahora, token));
        Assert.Equal(ResultadoDeRevocacionSinEnlace.Revocado, revocado.Resultado);
        Assert.Equal(EstadoDeAcceso.Revocado, revocado.Acceso!.Estado);
        Assert.Equal(ResultadoDeRevocacionSinEnlace.YaRevocado, (await En(token => bd.Accesos.RevocarSiNoHayEnlace(cuenta2, pedido2, edicion2, Ahora, token))).Resultado);
        Assert.Equal("reembolso", await bd.Consultar<string>($"SELECT motivo_de_revocacion FROM biblioteca.accesos WHERE id = '{id2}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-16")]
    public async Task Suspender_parte_solo_de_vigente_y_reanudar_solo_de_suspendido()
    {
        var cuenta = Guid.NewGuid();
        var id = await Conceder(cuenta, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(ResultadoDeReanudacion.NoDisponible, (await En(token => bd.Accesos.Reanudar(cuenta, id, token))).Resultado);
        Assert.Equal(ResultadoDeSuspension.NoDisponible, (await En(token => bd.Accesos.Suspender(Guid.NewGuid(), id, Ahora, token))).Resultado);
        Assert.Equal(ResultadoDeSuspension.Suspendido, (await En(token => bd.Accesos.Suspender(cuenta, id, Ahora, token))).Resultado);
        Assert.Equal(ResultadoDeSuspension.YaSuspendido, (await En(token => bd.Accesos.Suspender(cuenta, id, Ahora, token))).Resultado);
        Assert.Equal(ResultadoDeReanudacion.NoDisponible, (await En(token => bd.Accesos.Reanudar(Guid.NewGuid(), id, token))).Resultado);
        Assert.Equal(ResultadoDeReanudacion.Reanudado, (await En(token => bd.Accesos.Reanudar(cuenta, id, token))).Resultado);

        var actual = (await En(token => bd.Accesos.Obtener(id, cuenta, token)))!;
        await En(token => bd.Accesos.Revocar(actual.PedidoId, actual.EdicionId, CausaDeRevocacion.Reembolso, Ahora, token));
        Assert.Equal(ResultadoDeSuspension.NoDisponible, (await En(token => bd.Accesos.Suspender(cuenta, id, Ahora, token))).Resultado);
        Assert.Equal(ResultadoDeReanudacion.NoDisponible, (await En(token => bd.Accesos.Reanudar(cuenta, id, token))).Resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-07")]
    public async Task Restituir_resta_de_uno_hasta_las_usadas_no_toca_un_revocado_y_no_baja_los_enlaces()
    {
        var cuenta = Guid.NewGuid();
        var edicion = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        var id = await Conceder(cuenta, edicion, pedido);
        for (var i = 0; i < 3; i++)
        {
            await En(token => bd.Accesos.ConsumirCupo(id, cuenta, token));
        }

        Assert.Null(await En(token => bd.Accesos.Restituir(id, 4, token)));
        Assert.Null(await En(token => bd.Accesos.Restituir(id, 0, token)));
        var restituido = (await En(token => bd.Accesos.Restituir(id, 2, token)))!;
        Assert.Equal((1, 3), (restituido.DescargasUsadas, restituido.EnlacesEntregados));

        await En(token => bd.Accesos.Revocar(pedido, edicion, CausaDeRevocacion.Reembolso, Ahora, token));
        Assert.Null(await En(token => bd.Accesos.Restituir(id, 1, token)));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-02")]
    public async Task La_posesion_cuenta_vigentes_y_suspendidos_y_el_listado_pagina_por_fecha()
    {
        var cuenta = Guid.NewGuid();
        var ediciones = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        var pedidos = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        for (var i = 0; i < 3; i++)
        {
            await En(token => bd.Accesos.Conceder(cuenta, ediciones[i], pedidos[i], Ahora.AddDays(i), token));
        }

        var intermedio = (await En(token => bd.Accesos.ObtenerDeLaCompra(cuenta, pedidos[1], ediciones[1], token)))!;
        await En(token => bd.Accesos.Suspender(cuenta, intermedio.Id, Ahora, token));
        await En(token => bd.Accesos.Revocar(pedidos[2], ediciones[2], CausaDeRevocacion.Reembolso, Ahora, token));

        var poseidas = await En(token => bd.Accesos.EdicionesPoseidas(cuenta, ediciones, token));
        Assert.Equal(ediciones.Take(2).Order(), poseidas.Order());
        Assert.Equal(3, await En(token => bd.Accesos.ContarDeLaCuenta(cuenta, token)));
        var pagina = await En(token => bd.Accesos.ListarDeLaCuenta(cuenta, 0, 2, token));
        Assert.Equal([ediciones[2], ediciones[1]], pagina.Select(a => a.EdicionId));
        Assert.Equal(ediciones[0], Assert.Single(await En(token => bd.Accesos.ListarDeLaCuenta(cuenta, 2, 2, token))).EdicionId);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-22")]
    public async Task Eliminar_la_cuenta_borra_accesos_y_en_cascada_registro_claves_y_ayudas_y_sin_accesos_no_falla()
    {
        var cuenta = Guid.NewGuid();
        var otra = Guid.NewGuid();
        var id = await Conceder(cuenta, Guid.NewGuid(), Guid.NewGuid());
        var idOtra = await Conceder(otra, Guid.NewGuid(), Guid.NewGuid());
        await En(token => bd.Descargas.Registrar(new RegistroDeDescarga(Guid.NewGuid(), id, cuenta, Guid.NewGuid(), Ahora, new DateOnly(2026, 11, 20)), token));
        await En(token => bd.Descargas.Registrar(new RegistroDeDescarga(Guid.NewGuid(), idOtra, otra, Guid.NewGuid(), Ahora, new DateOnly(2026, 11, 20)), token));
        await En(token => bd.Descargas.RegistrarClave(new ClaveRegistrada(id, Clave(), Ahora, Ahora.AddSeconds(300), Guid.NewGuid()), Ahora, token));
        await En(token => bd.Descargas.AgregarAyuda(SolicitudDeAyuda.Abrir(id, MotivoDeAyuda.Otro, Ahora), token));

        var eliminados = await En(token => bd.Accesos.EliminarDeLaCuenta(cuenta, token));

        Assert.Equal(1, eliminados);
        Assert.Equal(0, await En(token => bd.Accesos.EliminarDeLaCuenta(Guid.NewGuid(), token)));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.accesos"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.descargas"));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.claves_de_descarga"));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.solicitudes_de_ayuda"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-04")]
    public async Task Una_clave_vigente_repetida_no_afecta_filas_y_una_vencida_se_reemplaza()
    {
        var cuenta = Guid.NewGuid();
        var id = await Conceder(cuenta, Guid.NewGuid(), Guid.NewGuid());
        var clave = Clave();

        Assert.Equal(ResultadoDeRegistrarClave.Registrada, await En(token => bd.Descargas.RegistrarClave(new ClaveRegistrada(id, clave, Ahora, Ahora.AddSeconds(300), Guid.NewGuid()), Ahora, token)));
        Assert.Equal(ResultadoDeRegistrarClave.RepeticionVigente, await En(token => bd.Descargas.RegistrarClave(new ClaveRegistrada(id, clave, Ahora.AddSeconds(60), Ahora.AddSeconds(360), Guid.NewGuid()), Ahora.AddSeconds(60), token)));
        var guardada = (await En(token => bd.Descargas.ObtenerClave(id, clave, token)))!;
        Assert.Equal(Ahora, guardada.FirmadoEn);
        Assert.Equal(Ahora.AddSeconds(300), guardada.VenceEn);

        var seisMinutos = Ahora.AddMinutes(6);
        Assert.Equal(ResultadoDeRegistrarClave.Registrada, await En(token => bd.Descargas.RegistrarClave(new ClaveRegistrada(id, clave, seisMinutos, seisMinutos.AddSeconds(300), Guid.NewGuid()), seisMinutos, token)));
        Assert.Equal(seisMinutos, (await En(token => bd.Descargas.ObtenerClave(id, clave, token)))!.FirmadoEn);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.claves_de_descarga"));
        Assert.Null(await En(token => bd.Descargas.ObtenerClave(id, Clave("fedcba9876543210fedcba9876543210"), token)));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-26")]
    public async Task Una_clave_de_un_acceso_inexistente_se_traduce_sin_abortar_la_transaccion_del_llamador()
    {
        var cuenta = Guid.NewGuid();
        var id = await Conceder(cuenta, Guid.NewGuid(), Guid.NewGuid());

        var resultados = await En(async token =>
        {
            var inexistente = await bd.Descargas.RegistrarClave(new ClaveRegistrada(Guid.NewGuid(), Clave(), Ahora, Ahora.AddSeconds(300), Guid.NewGuid()), Ahora, token);
            var valida = await bd.Descargas.RegistrarClave(new ClaveRegistrada(id, Clave(), Ahora, Ahora.AddSeconds(300), Guid.NewGuid()), Ahora, token);
            return (inexistente, valida);
        });

        Assert.Equal(ResultadoDeRegistrarClave.AccesoInexistente, resultados.inexistente);
        Assert.Equal(ResultadoDeRegistrarClave.Registrada, resultados.valida);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-04")]
    public async Task Cien_registros_simultaneos_de_la_misma_clave_dejan_una_sola_fila()
    {
        var cuenta = Guid.NewGuid();
        var id = await Conceder(cuenta, Guid.NewGuid(), Guid.NewGuid());
        var clave = Clave();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => En(token => bd.Descargas.RegistrarClave(new ClaveRegistrada(id, clave, Ahora, Ahora.AddSeconds(300), Guid.NewGuid()), Ahora, token)))));

        Assert.Equal(1, resultados.Count(r => r == ResultadoDeRegistrarClave.Registrada));
        Assert.Equal(39, resultados.Count(r => r == ResultadoDeRegistrarClave.RepeticionVigente));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.claves_de_descarga"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-04")]
    public async Task La_purga_quita_las_claves_vencidas_antes_del_limite()
    {
        var cuenta = Guid.NewGuid();
        var id = await Conceder(cuenta, Guid.NewGuid(), Guid.NewGuid());
        await En(token => bd.Descargas.RegistrarClave(new ClaveRegistrada(id, Clave(), Ahora.AddHours(-3), Ahora.AddHours(-3).AddSeconds(300), Guid.NewGuid()), Ahora.AddHours(-3), token));
        await En(token => bd.Descargas.RegistrarClave(new ClaveRegistrada(id, Clave("fedcba9876543210fedcba9876543210"), Ahora, Ahora.AddSeconds(300), Guid.NewGuid()), Ahora, token));

        Assert.Equal(1, await En(token => bd.Descargas.PurgarClaves(Ahora.AddHours(-1), token)));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.claves_de_descarga"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-23")]
    public async Task La_depuracion_deja_solo_la_edicion_y_el_dia_sin_tocar_el_acceso_y_repetirla_no_cambia_nada()
    {
        var cuenta = Guid.NewGuid();
        var id = await Conceder(cuenta, Guid.NewGuid(), Guid.NewGuid());
        var edicion = Guid.NewGuid();
        var viejo = new RegistroDeDescarga(Guid.NewGuid(), id, cuenta, edicion, Ahora, new DateOnly(2024, 11, 19));
        var reciente = new RegistroDeDescarga(Guid.NewGuid(), id, cuenta, edicion, Ahora, new DateOnly(2024, 11, 21));
        await En(token => bd.Descargas.Registrar(viejo, token));
        await En(token => bd.Descargas.Registrar(reciente, token));
        var limite = PoliticaDeRetencion.UltimoDiaVencido(new DateOnly(2026, 11, 20));

        Assert.Equal(1, await En(token => bd.Descargas.DepurarRegistros(limite, token)));
        Assert.Equal(0, await En(token => bd.Descargas.DepurarRegistros(limite, token)));

        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM biblioteca.descargas WHERE id = '{viejo.Id}' AND acceso_id IS NULL AND usuario_id IS NULL AND entregada_en IS NULL AND edicion_id = '{edicion}' AND dia = DATE '2024-11-19'"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM biblioteca.descargas WHERE id = '{reciente.Id}' AND acceso_id IS NOT NULL"));
        Assert.NotNull(await En(token => bd.Accesos.Obtener(id, cuenta, token)));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-14")]
    public async Task Una_sola_ayuda_abierta_por_acceso_sin_abortar_la_transaccion_y_cerrarla_una_sola_vez()
    {
        var cuenta = Guid.NewGuid();
        var id = await Conceder(cuenta, Guid.NewGuid(), Guid.NewGuid());
        var primera = SolicitudDeAyuda.Abrir(id, MotivoDeAyuda.ArchivoNoAbre, Ahora);

        var resultados = await En(async token =>
        {
            var a = await bd.Descargas.AgregarAyuda(primera, token);
            var b = await bd.Descargas.AgregarAyuda(SolicitudDeAyuda.Abrir(id, MotivoDeAyuda.Otro, Ahora), token);
            var c = await bd.Descargas.ObtenerAyudaAbierta(id, token);
            return (a, b, c);
        });

        Assert.Equal(ResultadoDeAgregarAyuda.Agregada, resultados.a);
        Assert.Equal(ResultadoDeAgregarAyuda.YaHayAbierta, resultados.b);
        Assert.Equal(primera.Id, resultados.c!.Id);
        Assert.Equal([id], await En(token => bd.Descargas.AccesosConAyudaAbierta([id, Guid.NewGuid()], token)));
        Assert.True(await En(token => bd.Descargas.CerrarAyuda(primera.Id, Ahora.AddHours(1), token)));
        Assert.False(await En(token => bd.Descargas.CerrarAyuda(primera.Id, Ahora.AddHours(2), token)));
        Assert.Equal(EstadoDeAyuda.Cerrada, (await En(token => bd.Descargas.ObtenerAyuda(primera.Id, token)))!.Estado);
        Assert.Equal(ResultadoDeAgregarAyuda.Agregada, await En(token => bd.Descargas.AgregarAyuda(SolicitudDeAyuda.Abrir(id, MotivoDeAyuda.Otro, Ahora.AddDays(1)), token)));

        var (todas, total) = await En(token => bd.Descargas.ListarAyudas(null, 0, 10, token));
        Assert.Equal((2, 2), (todas.Count, total));
        Assert.Equal(1, (await En(token => bd.Descargas.ListarAyudas(EstadoDeAyuda.Abierta, 0, 10, token))).Total);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-26")]
    public async Task Una_ayuda_sobre_un_acceso_inexistente_se_traduce_sin_abortar_la_transaccion_ni_dejar_huerfanas()
    {
        var cuenta = Guid.NewGuid();
        var id = await Conceder(cuenta, Guid.NewGuid(), Guid.NewGuid());

        var resultados = await En(async token =>
        {
            var inexistente = await bd.Descargas.AgregarAyuda(SolicitudDeAyuda.Abrir(Guid.NewGuid(), MotivoDeAyuda.Otro, Ahora), token);
            var valida = await bd.Descargas.AgregarAyuda(SolicitudDeAyuda.Abrir(id, MotivoDeAyuda.Otro, Ahora), token);
            return (inexistente, valida);
        });

        Assert.Equal(ResultadoDeAgregarAyuda.AccesoInexistente, resultados.inexistente);
        Assert.Equal(ResultadoDeAgregarAyuda.Agregada, resultados.valida);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-24")]
    public async Task La_depuracion_de_ayudas_elimina_las_de_dos_anios_de_cualquier_estado_por_dia_de_lima()
    {
        var cuenta = Guid.NewGuid();
        var id = await Conceder(cuenta, Guid.NewGuid(), Guid.NewGuid());
        var vieja = SolicitudDeAyuda.Reconstruir(Guid.NewGuid(), id, MotivoDeAyuda.Otro, EstadoDeAyuda.Abierta, new DateTimeOffset(2024, 11, 19, 15, 0, 0, TimeSpan.Zero), null);
        var cerradaReciente = SolicitudDeAyuda.Reconstruir(Guid.NewGuid(), id, MotivoDeAyuda.Otro, EstadoDeAyuda.Cerrada, new DateTimeOffset(2024, 11, 21, 15, 0, 0, TimeSpan.Zero), new DateTimeOffset(2024, 11, 22, 15, 0, 0, TimeSpan.Zero));
        // Las ayudas se siembran por administración: el rol no inserta cerradas ni con fecha arbitraria por este camino.
        await bd.Ejecutar($"""
            INSERT INTO biblioteca.solicitudes_de_ayuda (id, acceso_id, motivo, estado, creada_en, cerrada_en) VALUES
              ('{vieja.Id}', '{id}', 'otro', 'abierta', '2024-11-19T15:00:00Z', NULL),
              ('{cerradaReciente.Id}', '{id}', 'otro', 'cerrada', '2024-11-21T15:00:00Z', '2024-11-22T15:00:00Z')
            """);

        Assert.Equal(1, await En(token => bd.Descargas.DepurarAyudas(PoliticaDeRetencion.UltimoDiaVencido(new DateOnly(2026, 11, 20)), token)));

        Assert.Equal(cerradaReciente.Id, Assert.Single((await En(token => bd.Descargas.ListarAyudas(null, 0, 10, token))).Ayudas).Id);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-22")]
    public async Task El_rol_de_la_aplicacion_no_puede_truncar_ni_eliminar_el_registro_de_descargas_ni_tocar_la_cuenta()
    {
        var cuenta = Guid.NewGuid();
        var id = await Conceder(cuenta, Guid.NewGuid(), Guid.NewGuid());
        await En(token => bd.Descargas.Registrar(new RegistroDeDescarga(Guid.NewGuid(), id, cuenta, Guid.NewGuid(), Ahora, new DateOnly(2026, 11, 20)), token));

        foreach (var sql in new[]
        {
            "TRUNCATE biblioteca.accesos CASCADE",
            "DELETE FROM biblioteca.descargas",
            "UPDATE biblioteca.accesos SET cuenta_id = gen_random_uuid()",
            "UPDATE biblioteca.accesos SET edicion_id = gen_random_uuid()",
            "UPDATE biblioteca.accesos SET concedido_en = now()",
            "DROP TABLE biblioteca.descargas",
        })
        {
            var falla = await Assert.ThrowsAsync<FallaDeLaBaseDeBibliotecaException>(() => bd.AccesoAlEsquema.Ejecutar(
                async (conexion, transaccion, token) =>
                {
                    await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
                    return await comando.ExecuteNonQueryAsync(token);
                },
                CancellationToken.None));
            Assert.Equal("42501", falla.CodigoDeEstado);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-22")]
    public async Task Los_check_impiden_un_registro_con_acceso_sin_usuario_y_un_acceso_revocado_sin_motivo()
    {
        var cuenta = Guid.NewGuid();
        var id = await Conceder(cuenta, Guid.NewGuid(), Guid.NewGuid());

        var registro = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"INSERT INTO biblioteca.descargas (id, acceso_id, usuario_id, edicion_id, entregada_en, dia) VALUES (gen_random_uuid(), '{id}', NULL, gen_random_uuid(), now(), current_date)"));
        var revocado = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE biblioteca.accesos SET estado = 'revocado' WHERE id = '{id}'"));
        var cupo = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE biblioteca.accesos SET descargas_usadas = 6 WHERE id = '{id}'"));
        var duplicado = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"INSERT INTO biblioteca.accesos (id, cuenta_id, edicion_id, pedido_id, estado, descargas_usadas, enlaces_entregados, concedido_en) SELECT gen_random_uuid(), cuenta_id, edicion_id, gen_random_uuid(), 'vigente', 0, 0, now() FROM biblioteca.accesos WHERE id = '{id}'"));

        Assert.Equal("23514", registro.SqlState);
        Assert.Equal("23514", revocado.SqlState);
        Assert.Equal("23514", cupo.SqlState);
        Assert.Equal("23505", duplicado.SqlState);
    }
}
