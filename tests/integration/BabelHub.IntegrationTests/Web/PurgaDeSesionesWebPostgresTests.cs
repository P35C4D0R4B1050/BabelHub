using System.Collections.Concurrent;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;
using BabelHub.Ecommerce.Infrastructure.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.IntegrationTests.Web;

/// <summary>La purga de sesiones y borradores en PostgreSQL real (T13): las filas que elige el SQL, <c>SKIP LOCKED</c> entre réplicas, el trabajo de fondo y su apagado en Ensayo y Restauracion.</summary>
[Collection(ColeccionPostgresDeLaWeb.Nombre)]
public sealed class PurgaDeSesionesWebPostgresTests(PostgresDeLaWebFixture bd) : IAsyncLifetime
{
    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static PurgarSesionesWeb Purga(EntornoDeSesiones e) =>
        new(HostDeSesion.Tienda, e.Sesiones, e.Borradores, e.Cifrador, e.Identidad, e.Seguridad, e.Alertas, e.Unidad, e.Reloj);

    private Task<long> Filas() => bd.Consultar<long>("SELECT count(*) FROM web.sesiones_web");

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task El_SQL_elige_las_vencidas_sin_uso_las_cerradas_de_hace_30_minutos_y_las_de_revocacion_pendiente_y_deja_las_vigentes()
    {
        var e = new EntornoDeSesiones(bd);
        var vencida = await e.Abrir();
        var cerradaReciente = await e.Abrir();
        var vigente = await e.Abrir();
        await e.Web.Cerrar(cerradaReciente);
        e.Reloj.Ahora = EntornoDeSesiones.Inicio.AddMinutes(20);
        await e.Web.Cargar(vigente, HostWeb.Tienda, OrigenDeLaAccion.AccionDelUsuario);
        e.Reloj.Ahora = EntornoDeSesiones.Inicio.AddMinutes(31);

        var resultado = await Purga(e).Ejecutar();

        // «vencida» y «cerradaReciente» tenían 31 y 31 minutos desde su última actividad; la cerrada, a los 31 minutos de cerrada, ya se borra.
        Assert.Equal(1, resultado.Cerradas);
        Assert.Equal(1, resultado.SesionesBorradas);
        Assert.Equal("vencida_por_inactividad", await bd.Consultar<string>($"SELECT motivo_de_cierre FROM web.sesiones_web WHERE id_hash = decode('{ValorDeCookieDeSesion.Hash(vencida).Valor}', 'hex')"));
        Assert.Equal(2L, await Filas());
        Assert.Equal(EstadoDeCarga.Valida, (await e.Web.Cargar(vigente, HostWeb.Tienda, OrigenDeLaAccion.ConsultaAutomatica)).Estado);
        Assert.Equal(2, e.Identidad.Revocaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Una_fila_con_la_revocacion_pendiente_no_se_borra_por_SQL_aunque_lleve_mas_de_30_minutos_cerrada()
    {
        var e = new EntornoDeSesiones(bd);
        e.Identidad.ConfirmaRevocaciones = false;
        var cookie = await e.Abrir();
        await e.Web.Cerrar(cookie);
        e.Reloj.Ahora = EntornoDeSesiones.Inicio.AddHours(2);

        // Directo al almacén: un borrado de una fila con la renovación sin revocar no ocurre nunca.
        await e.Unidad.Ejecutar(async ct =>
        {
            await e.Sesiones.Borrar(e.Hash(cookie), ct);
            return 0;
        });

        Assert.Equal(1L, await Filas());
        var resultado = await Purga(e).Ejecutar();
        Assert.Equal(1, resultado.RevocacionesPendientes);
        Assert.Equal(1L, await Filas());
        Assert.Equal(1, (await e.Sesiones.Cargar(e.Hash(cookie)))!.ReintentosDeRevocacion);

        e.Identidad.ConfirmaRevocaciones = true;
        await Purga(e).Ejecutar();
        await Purga(e).Ejecutar();
        Assert.Equal(0L, await Filas());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Dos_purgas_simultaneas_de_distintas_replicas_no_revocan_dos_veces_la_misma_renovacion()
    {
        var a = new EntornoDeSesiones(bd);
        var b = new EntornoDeSesiones(bd);
        for (var i = 0; i < 10; i++)
        {
            await a.Abrir();
        }

        a.Reloj.Ahora = EntornoDeSesiones.Inicio.AddMinutes(31);
        b.Reloj.Ahora = a.Reloj.Ahora;
        a.Identidad.DemoraAlRevocar = TimeSpan.FromMilliseconds(100);
        b.Identidad.DemoraAlRevocar = TimeSpan.FromMilliseconds(100);

        await Task.WhenAll(Task.Run(() => Purga(a).Ejecutar()), Task.Run(() => Purga(b).Ejecutar()));

        Assert.Equal(10, a.Identidad.Revocaciones + b.Identidad.Revocaciones);
        Assert.Equal(10L, await bd.Consultar<long>("SELECT count(*) FROM web.sesiones_web WHERE motivo_de_cierre = 'vencida_por_inactividad' AND renovacion_cifrada IS NULL AND credencial_cifrada IS NULL"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task La_purga_de_la_tienda_deja_intactas_las_filas_de_la_gestion_cuya_clave_no_tiene()
    {
        const string ClaveTienda = "clave-de-la-tienda-0123456789-abcdefghij";
        const string ClaveGestion = "clave-de-la-gestion-9876543210-jihgfedcba";
        var tienda = new EntornoDeSesiones(bd, cifrador: new AesGcmCifradorDeSecretosDeSesion(ConfiguracionDeSesionesWeb.Crear(HostDeSesion.Tienda, ClaveTienda)));
        var gestion = new EntornoDeSesiones(bd, cifrador: new AesGcmCifradorDeSecretosDeSesion(ConfiguracionDeSesionesWeb.Crear(HostDeSesion.Gestion, ClaveGestion)));
        var deLaTienda = await tienda.Abrir();
        var deLaGestion = await gestion.Abrir(HostWeb.Gestion, RolWeb.Encargado);
        var cerradaDeLaGestion = await gestion.Abrir(HostWeb.Gestion, RolWeb.Encargado);
        gestion.Identidad.ConfirmaRevocaciones = false;
        await gestion.Web.Cerrar(cerradaDeLaGestion);
        tienda.Reloj.Ahora = EntornoDeSesiones.Inicio.AddMinutes(31);

        var purga = new PurgarSesionesWeb(HostDeSesion.Tienda, tienda.Sesiones, tienda.Borradores, tienda.Cifrador, tienda.Identidad, tienda.Seguridad, tienda.Alertas, tienda.Unidad, tienda.Reloj);
        var resultado = await purga.Ejecutar();

        Assert.Equal(1, resultado.Cerradas);
        Assert.Equal(0, resultado.Descartadas + resultado.Revocadas);
        Assert.Empty(tienda.Seguridad.Eventos);
        Assert.Equal("vencida_por_inactividad", await bd.Consultar<string>($"SELECT motivo_de_cierre FROM web.sesiones_web WHERE id_hash = decode('{ValorDeCookieDeSesion.Hash(deLaTienda).Valor}', 'hex')"));
        // Las de la gestión siguen como estaban: la abierta con su renovación y la cerrada con la revocación pendiente.
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM web.sesiones_web WHERE id_hash = decode('{ValorDeCookieDeSesion.Hash(deLaGestion).Valor}', 'hex') AND cerrada_en IS NULL AND renovacion_cifrada IS NOT NULL"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM web.sesiones_web WHERE id_hash = decode('{ValorDeCookieDeSesion.Hash(cerradaDeLaGestion).Valor}', 'hex') AND revocacion_pendiente AND renovacion_cifrada IS NOT NULL"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Veinticinco_revocaciones_imposibles_no_dejan_sin_turno_a_una_vencida_ni_a_los_borrados()
    {
        var e = new EntornoDeSesiones(bd);
        e.Identidad.ConfirmaRevocaciones = false;
        for (var i = 0; i < 25; i++)
        {
            await e.Web.Cerrar(await e.Abrir());
        }

        var vencida = await e.Abrir();
        e.Reloj.Ahora = EntornoDeSesiones.Inicio.AddMinutes(31);

        var resultado = await Purga(e).Ejecutar();

        Assert.Equal(1, resultado.Cerradas);
        Assert.Equal("vencida_por_inactividad", await bd.Consultar<string>($"SELECT motivo_de_cierre FROM web.sesiones_web WHERE id_hash = decode('{ValorDeCookieDeSesion.Hash(vencida).Valor}', 'hex')"));
        // Las de revocación imposible pasan al final de la cola (más reintentos): la siguiente corrida atiende otra primero.
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM web.sesiones_web WHERE reintentos_de_revocacion = 1"));
        await Purga(e).Ejecutar();
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM web.sesiones_web WHERE reintentos_de_revocacion = 1"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Los_borradores_vencidos_se_borran_y_los_vigentes_quedan()
    {
        var e = new EntornoDeSesiones(bd);
        var cookie = await e.Abrir();
        e.Reloj.Ahora = EntornoDeSesiones.Inicio.AddMinutes(31);
        await e.Web.Cargar(cookie, HostWeb.Tienda, OrigenDeLaAccion.AccionDelUsuario);
        Assert.True(await e.BorradoresDePantalla.Conservar(new ConservarBorrador(cookie, "/cuenta/datos", new Dictionary<string, string> { ["nombre"] = "Ana" }, true)));
        e.Reloj.Ahora = EntornoDeSesiones.Inicio.AddMinutes(60);
        var resultado = await Purga(e).Ejecutar();
        Assert.Equal(0, resultado.BorradoresBorrados);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM web.borradores"));

        e.Reloj.Ahora = EntornoDeSesiones.Inicio.AddMinutes(62);
        resultado = await Purga(e).Ejecutar();

        Assert.Equal(1, resultado.BorradoresBorrados);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM web.borradores"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task El_paso_de_restauracion_vacia_sesiones_y_borradores_y_conserva_el_anillo_de_claves()
    {
        var e = new EntornoDeSesiones(bd);
        var cookie = await e.Abrir();
        e.Reloj.Ahora = EntornoDeSesiones.Inicio.AddMinutes(31);
        await e.Web.Cargar(cookie, HostWeb.Tienda, OrigenDeLaAccion.AccionDelUsuario);
        await e.BorradoresDePantalla.Conservar(new ConservarBorrador(cookie, "/cuenta/datos", new Dictionary<string, string> { ["nombre"] = "Ana" }, true));
        await bd.Ejecutar("INSERT INTO web.claves_de_proteccion VALUES (gen_random_uuid(), 'tienda', '\\x00', 1, now())");
        var paso = new PasoDeRestauracionDeSesionesWeb(new VaciadoDeLaWebPostgres(e.Unidad, e.Unidad));

        var resultado = await paso.Ejecutar();

        Assert.Equal(ResultadoDePaso.Correcto, resultado);
        Assert.Equal(0L, await Filas());
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM web.borradores"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM web.claves_de_proteccion"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task El_trabajo_registra_cada_corrida_no_se_detiene_si_falla_y_alerta_tras_tres_fallos_seguidos()
    {
        var e = new EntornoDeSesiones(bd);
        var registro = new RegistroDeTrabajosEspia();
        var purga = new PurgaQueFalla(e);
        var trabajo = new TrabajoDePurgaDeSesionesWeb(purga.Crear(), registro, e.Alertas, e.Reloj);

        Assert.NotNull(await trabajo.EjecutarUnaVez());
        purga.Falla = true;
        for (var i = 0; i < 3; i++)
        {
            Assert.Null(await trabajo.EjecutarUnaVez());
        }

        Assert.Equal([ResultadoDeTrabajo.Exito, ResultadoDeTrabajo.Fallo, ResultadoDeTrabajo.Fallo, ResultadoDeTrabajo.Fallo], registro.Resultados.Select(r => r.Resultado).ToArray());
        Assert.All(registro.Resultados, r => Assert.Equal("purgar-sesiones-web", r.Nombre));
        Assert.Single(e.Alertas.Alertas, a => a.Motivo == TrabajoDePurgaDeSesionesWeb.MotivoDeAlerta);
    }

    [Theory]
    [InlineData(ModoDeArranque.Normal, true)]
    [InlineData(ModoDeArranque.Ensayo, false)]
    [InlineData(ModoDeArranque.Restauracion, false)]
    [Trait("Criterio", "AC-WEB-23")]
    public void La_purga_corre_solo_en_el_modo_normal(ModoDeArranque modo, bool seRegistra)
    {
        var servicios = new ServiceCollection();

        servicios.AgregarPurgaDeSesionesWeb(modo);

        Assert.Equal(seRegistra, servicios.Any(s => s.ServiceType == typeof(IHostedService)));
    }

    private sealed class RegistroDeTrabajosEspia : IRegistroDeTrabajosDeFondo
    {
        public ConcurrentQueue<(string Nombre, ResultadoDeTrabajo Resultado)> ColaDeResultados { get; } = [];

        public IEnumerable<(string Nombre, ResultadoDeTrabajo Resultado)> Resultados => ColaDeResultados;

        public Task Registrar(string trabajo, ResultadoDeTrabajo resultado, DateTimeOffset instante, CancellationToken cancelacion = default)
        {
            ColaDeResultados.Enqueue((trabajo, resultado));
            return Task.CompletedTask;
        }
    }

    /// <summary>Una purga cuyo almacén de borradores falla a voluntad: la corrida entera falla y el trabajo la registra.</summary>
    private sealed class PurgaQueFalla(EntornoDeSesiones e)
    {
        public bool Falla { get; set; }

        public PurgarSesionesWeb Crear() => new(HostDeSesion.Tienda, e.Sesiones, new BorradoresQueFallan(this, e.Borradores), e.Cifrador, e.Identidad, e.Seguridad, e.Alertas, e.Unidad, e.Reloj);
    }

    private sealed class BorradoresQueFallan(PurgaQueFalla purga, IAlmacenDeBorradores interno) : IAlmacenDeBorradores
    {
        public Task<int> Contar(Guid usuarioId, DateTimeOffset ahora, CancellationToken cancelacion = default) => interno.Contar(usuarioId, ahora, cancelacion);

        public Task<BorradorGuardado?> Cargar(Guid usuarioId, string plantillaDeRuta, CancellationToken cancelacion = default) => interno.Cargar(usuarioId, plantillaDeRuta, cancelacion);

        public Task Guardar(BorradorGuardado borrador, CancellationToken cancelacion = default) => interno.Guardar(borrador, cancelacion);

        public Task Borrar(Guid id, CancellationToken cancelacion = default) => interno.Borrar(id, cancelacion);

        public Task<int> BorrarVencidos(DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default) =>
            purga.Falla ? throw new InvalidOperationException("La base no responde.") : interno.BorrarVencidos(ahora, maximo, cancelacion);
    }
}
