using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;
using Npgsql;

namespace BabelHub.IntegrationTests.Web;

/// <summary>Las sesiones web en PostgreSQL real (T10): lo que se guarda, el bloqueo de la fila, la renovación concurrente y la caída entre la llamada y la confirmación.</summary>
[Collection(ColeccionPostgresDeLaWeb.Nombre)]
public sealed class SesionesWebPostgresTests(PostgresDeLaWebFixture bd) : IAsyncLifetime
{
    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static void EnLaCredencialPorVencer(EntornoDeSesiones e) => e.Reloj.Ahora = EntornoDeSesiones.Inicio.AddMinutes(14).AddSeconds(30);

    private Task<string?> Estado(string cookie) =>
        bd.Consultar<string>($"SELECT coalesce(motivo_de_cierre, 'abierta') FROM web.sesiones_web WHERE id_hash = decode('{ValorDeCookieDeSesion.Hash(cookie).Valor}', 'hex')");

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task La_sesion_guarda_el_SHA256_de_la_cookie_y_nunca_la_cookie_ni_el_rol()
    {
        var e = new EntornoDeSesiones(bd);

        var cookie = await e.Abrir(HostWeb.Gestion, RolWeb.Encargado);

        var hash = ValorDeCookieDeSesion.Hash(cookie).Valor;
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM web.sesiones_web WHERE encode(id_hash, 'hex') = '{hash}'"));
        var fila = (await bd.Consultar<string>("SELECT row(t.*)::text FROM web.sesiones_web t"))!;
        Assert.DoesNotContain(cookie, fila, StringComparison.Ordinal);
        Assert.DoesNotContain("Encargado", fila, StringComparison.OrdinalIgnoreCase);
        var columnas = await bd.Consultar<string>("SELECT string_agg(column_name, ',') FROM information_schema.columns WHERE table_schema = 'web' AND table_name = 'sesiones_web'");
        Assert.DoesNotContain("rol", columnas!.Split(','));
        Assert.DoesNotContain("sucursal_id", columnas.Split(','));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task Una_sesion_guardada_se_carga_con_el_mismo_contenido()
    {
        var e = new EntornoDeSesiones(bd);
        var cookie = await e.Abrir(HostWeb.Gestion, RolWeb.Encargado);

        var fila = await e.Sesiones.Cargar(e.Hash(cookie));

        Assert.NotNull(fila);
        Assert.Equal(HostDeSesion.Gestion, fila.Sesion.Host);
        Assert.Equal(VerificadorPorPrefijo.Usuario, fila.Sesion.UsuarioId);
        Assert.Equal(EstadoDeSesion.Abierta, fila.Sesion.Estado);
        Assert.Equal(EntornoDeSesiones.Inicio.AddHours(8), fila.Sesion.ExpiraAbsoluta);
        Assert.Equal(EntornoDeSesiones.Inicio, fila.Sesion.UltimaActividad);
        Assert.NotNull(fila.CredencialCifrada);
        Assert.NotNull(fila.RenovacionCifrada);
        Assert.Equal(EntornoDeSesiones.Inicio.AddMinutes(15), fila.CredencialVenceEn);
        Assert.Null(await e.Sesiones.Cargar(e.Hash(new string('A', 43))));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Cerrar_borra_la_credencial_y_la_renovacion_solo_cuando_Identidad_confirma_su_revocacion()
    {
        var e = new EntornoDeSesiones(bd);
        var confirmada = await e.Abrir();
        var pendiente = await e.Abrir();

        await e.Web.Cerrar(confirmada);
        e.Identidad.ConfirmaRevocaciones = false;
        await e.Web.Cerrar(pendiente);

        var sinConfirmar = await e.Sesiones.Cargar(e.Hash(pendiente));
        Assert.Null(sinConfirmar!.CredencialCifrada);
        Assert.NotNull(sinConfirmar.RenovacionCifrada);
        Assert.True(sinConfirmar.RevocacionPendiente);
        var confirmadaFila = await e.Sesiones.Cargar(e.Hash(confirmada));
        Assert.Null(confirmadaFila!.CredencialCifrada);
        Assert.Null(confirmadaFila.RenovacionCifrada);
        Assert.False(confirmadaFila.RevocacionPendiente);
        Assert.Equal(EstadoDeSesion.Cerrada, confirmadaFila.Sesion.Estado);
        Assert.Equal("voluntario", await Estado(confirmada));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task La_base_rechaza_una_fila_cerrada_que_conserva_la_credencial()
    {
        var e = new EntornoDeSesiones(bd);
        var cookie = await e.Abrir();

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            bd.Ejecutar($"UPDATE web.sesiones_web SET cerrada_en = now(), motivo_de_cierre = 'voluntario' WHERE id_hash = decode('{e.Hash(cookie).Valor}', 'hex')"));

        Assert.Equal("23514", error.SqlState);
        Assert.Equal("sesiones_web_cerrada_sin_credencial", error.ConstraintName);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Un_identificador_cerrado_no_vuelve_a_abrirse_aunque_se_guarde_un_modelo_antiguo()
    {
        var e = new EntornoDeSesiones(bd);
        var cookie = await e.Abrir();
        var antiguo = (await e.Sesiones.Cargar(e.Hash(cookie)))!;
        await e.Web.Cerrar(cookie);

        await e.Unidad.Ejecutar(async ct =>
        {
            await e.Sesiones.Guardar(antiguo, ct);
            return 0;
        });

        Assert.Equal("voluntario", await Estado(cookie));
        Assert.Equal(EstadoDeCarga.Cerrada, (await e.Web.Cargar(cookie, HostWeb.Tienda, OrigenDeLaAccion.AccionDelUsuario)).Estado);
        Assert.Equal(EstadoDeCredencial.SinSesion, (await e.Web.AsegurarCredencial(cookie, HostWeb.Tienda)).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task La_actividad_solo_avanza_y_solo_en_sesiones_abiertas()
    {
        var e = new EntornoDeSesiones(bd);
        var cookie = await e.Abrir();
        var id = e.Hash(cookie);

        await e.Sesiones.ActualizarActividad(id, EntornoDeSesiones.Inicio.AddMinutes(5));
        await e.Sesiones.ActualizarActividad(id, EntornoDeSesiones.Inicio.AddMinutes(2));
        Assert.Equal(EntornoDeSesiones.Inicio.AddMinutes(5), (await e.Sesiones.Cargar(id))!.Sesion.UltimaActividad);

        await e.Web.Cerrar(cookie);
        await e.Sesiones.ActualizarActividad(id, EntornoDeSesiones.Inicio.AddMinutes(9));
        Assert.Equal(EntornoDeSesiones.Inicio.AddMinutes(5), (await e.Sesiones.Cargar(id))!.Sesion.UltimaActividad);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Cargar_para_renovar_con_la_fila_retenida_agota_el_plazo_y_no_bloquea_para_siempre()
    {
        var e = new EntornoDeSesiones(bd, plazoDeBloqueo: TimeSpan.FromMilliseconds(300));
        var cookie = await e.Abrir();
        var id = e.Hash(cookie);
        var retenida = new TaskCompletionSource();
        var liberar = new TaskCompletionSource();
        var quienRetiene = e.Unidad.Ejecutar(async ct =>
        {
            await e.Sesiones.CargarParaRenovar(id, ct);
            retenida.SetResult();
            await liberar.Task;
            return 0;
        });
        await retenida.Task;

        var error = await Record.ExceptionAsync(() => e.Unidad.Ejecutar(async ct => await e.Sesiones.CargarParaRenovar(id, ct), default));

        Assert.IsType<BloqueoDeSesionNoDisponibleException>(error);
        liberar.SetResult();
        await quienRetiene;
        Assert.NotNull(await e.Unidad.Ejecutar(async ct => await e.Sesiones.CargarParaRenovar(id, ct)));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Una_solicitud_que_espera_la_fila_responde_servicio_no_disponible_sin_cerrar_la_sesion()
    {
        var e = new EntornoDeSesiones(bd, plazoDeBloqueo: TimeSpan.FromMilliseconds(300));
        var cookie = await e.Abrir();
        EnLaCredencialPorVencer(e);
        var retenida = new TaskCompletionSource();
        var liberar = new TaskCompletionSource();
        var quienRetiene = e.Unidad.Ejecutar(async ct =>
        {
            await e.Sesiones.CargarParaRenovar(e.Hash(cookie), ct);
            retenida.SetResult();
            await liberar.Task;
            return 0;
        });
        await retenida.Task;

        var resultado = await e.Web.AsegurarCredencial(cookie, HostWeb.Tienda);

        Assert.Equal(EstadoDeCredencial.ServicioNoDisponible, resultado.Estado);
        Assert.Equal(0, e.Identidad.LlamadasARenovar);
        liberar.SetResult();
        await quienRetiene;
        Assert.Equal("abierta", await Estado(cookie));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Veinte_solicitudes_simultaneas_con_la_credencial_por_vencer_renuevan_una_sola_vez()
    {
        var e = new EntornoDeSesiones(bd);
        var cookie = await e.Abrir();
        EnLaCredencialPorVencer(e);
        e.Identidad.Demora = TimeSpan.FromMilliseconds(200);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => e.Web.AsegurarCredencial(cookie, HostWeb.Tienda))));

        Assert.All(resultados, r => Assert.Equal(EstadoDeCredencial.Asegurada, r.Estado));
        Assert.Equal(1, e.Identidad.LlamadasARenovar);
        Assert.All(resultados, r => Assert.Equal("jwt-ok-2", r.Credencial!.Valor));
        var fila = (await e.Sesiones.Cargar(e.Hash(cookie)))!;
        Assert.Equal(e.Reloj.Ahora.AddMinutes(15), fila.CredencialVenceEn);
        Assert.False(fila.RenovacionIncierta);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Veinte_solicitudes_simultaneas_con_la_credencial_invalida_no_se_pisan_y_la_sesion_sigue_abierta()
    {
        var e = new EntornoDeSesiones(bd);
        var cookie = await e.Abrir(credencial: "credencial-invalida");
        e.Identidad.Demora = TimeSpan.FromMilliseconds(100);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => e.Web.AsegurarCredencial(cookie, HostWeb.Tienda))));

        Assert.All(resultados, r => Assert.Equal(EstadoDeCredencial.Asegurada, r.Estado));
        Assert.Equal(1, e.Identidad.LlamadasARenovar);
        Assert.Equal("abierta", await Estado(cookie));
        Assert.False((await e.Sesiones.Cargar(e.Hash(cookie)))!.RenovacionIncierta);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Una_respuesta_perdida_deja_la_renovacion_incierta_y_la_solicitud_siguiente_reintenta_con_la_guardada()
    {
        var e = new EntornoDeSesiones(bd);
        var cookie = await e.Abrir();
        EnLaCredencialPorVencer(e);
        e.Identidad.PerderLaRespuesta = true;

        var primera = await e.Web.AsegurarCredencial(cookie, HostWeb.Tienda);

        Assert.Equal(EstadoDeCredencial.ServicioNoDisponible, primera.Estado);
        var incierta = (await e.Sesiones.Cargar(e.Hash(cookie)))!;
        Assert.True(incierta.RenovacionIncierta);
        Assert.Equal(EstadoDeSesion.Abierta, incierta.Sesion.Estado);

        // Con la tolerancia de Identidad a la renovación inmediatamente anterior (R10) la solicitud siguiente se recupera.
        e.Identidad.PerderLaRespuesta = false;
        e.Identidad.TolerarLaRenovacionAnterior = true;
        var segunda = await e.Web.AsegurarCredencial(cookie, HostWeb.Tienda);

        Assert.Equal(EstadoDeCredencial.Asegurada, segunda.Estado);
        Assert.False((await e.Sesiones.Cargar(e.Hash(cookie)))!.RenovacionIncierta);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Sin_la_tolerancia_de_Identidad_una_respuesta_perdida_acaba_en_rechazo_y_cierre_con_el_borrador_conservable()
    {
        var e = new EntornoDeSesiones(bd);
        var cookie = await e.Abrir();
        EnLaCredencialPorVencer(e);
        e.Identidad.PerderLaRespuesta = true;
        await e.Web.AsegurarCredencial(cookie, HostWeb.Tienda);
        e.Identidad.PerderLaRespuesta = false;

        var segunda = await e.Web.AsegurarCredencial(cookie, HostWeb.Tienda);

        Assert.Equal(EstadoDeCredencial.SesionCerrada, segunda.Estado);
        Assert.Equal("renovacion_rechazada", await Estado(cookie));
        var campos = new Dictionary<string, string> { ["detalle"] = "Mi reclamo" };
        Assert.True(await e.BorradoresDePantalla.Conservar(new ConservarBorrador(cookie, "/reclamaciones/nueva", campos, true)));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task La_caida_entre_la_llamada_y_la_confirmacion_no_deja_la_fila_a_medias()
    {
        AlmacenQueFallaAlGuardar? almacenQueCae = null;
        var e = new EntornoDeSesiones(bd, decorarAlmacen: almacen => almacenQueCae = new AlmacenQueFallaAlGuardar(almacen));
        var cookie = await e.Abrir();
        var antes = (await e.Sesiones.Cargar(e.Hash(cookie)))!;
        EnLaCredencialPorVencer(e);

        // Identidad rota la renovación y el proceso cae antes de confirmar: la transacción se revierte y la fila queda como estaba.
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Web.AsegurarCredencial(cookie, HostWeb.Tienda));

        Assert.Equal(1, e.Identidad.LlamadasARenovar);
        var despues = (await e.Sesiones.Cargar(e.Hash(cookie)))!;
        Assert.Equal(antes.CredencialCifrada, despues.CredencialCifrada);
        Assert.Equal(antes.RenovacionCifrada, despues.RenovacionCifrada);
        Assert.Equal(EstadoDeSesion.Abierta, despues.Sesion.Estado);

        // La solicitud siguiente intenta con la renovación guardada, que Identidad ya rotó: con la tolerancia se recupera.
        almacenQueCae!.Cae = false;
        e.Identidad.TolerarLaRenovacionAnterior = true;
        Assert.Equal(EstadoDeCredencial.Asegurada, (await e.Web.AsegurarCredencial(cookie, HostWeb.Tienda)).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Cerrar_todas_las_sesiones_de_un_usuario_cierra_las_abiertas_con_motivo_voluntario_y_sin_conservar_el_borrador()
    {
        var e = new EntornoDeSesiones(bd);
        var una = await e.Abrir();
        var otra = await e.Abrir();

        await e.Web.CerrarTodasDeUnUsuario(VerificadorPorPrefijo.Usuario);

        Assert.Equal("voluntario", await Estado(una));
        Assert.Equal("voluntario", await Estado(otra));
        Assert.Empty(await e.Unidad.Ejecutar(async ct => await e.Sesiones.CargarAbiertasDeUnUsuario(VerificadorPorPrefijo.Usuario, ct)));
        Assert.False(await e.BorradoresDePantalla.Conservar(new ConservarBorrador(una, "/cuenta/datos", new Dictionary<string, string> { ["nombre"] = "Ana" }, true)));
    }

    [Theory]
    [InlineData("host = 'gestion'")]
    [InlineData("usuario_id = gen_random_uuid()")]
    [InlineData("expira_absoluta = expira_absoluta + interval '30 days'")]
    [InlineData("creada_en = now()")]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task El_rol_del_modulo_no_puede_cambiar_lo_que_el_plan_dice_que_no_cambia(string asignacion)
    {
        var e = new EntornoDeSesiones(bd);
        await e.Abrir();

        await using var conexion = await bd.Administracion.OpenConnectionAsync();
        await using var rol = new NpgsqlCommand("SET ROLE rol_web", conexion);
        await rol.ExecuteNonQueryAsync();
        await using var actualizar = new NpgsqlCommand($"UPDATE web.sesiones_web SET {asignacion}", conexion);
        var error = await Assert.ThrowsAsync<PostgresException>(() => actualizar.ExecuteNonQueryAsync());

        Assert.Equal("42501", error.SqlState);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task La_aplicacion_no_lee_la_tabla_sin_el_rol_del_modulo()
    {
        var e = new EntornoDeSesiones(bd);
        await e.Abrir();

        await using var comando = bd.Acceso.CreateCommand("SELECT count(*) FROM web.sesiones_web");
        var error = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteScalarAsync());

        Assert.Equal("42501", error.SqlState);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM web.sesiones_web"));
    }
}
