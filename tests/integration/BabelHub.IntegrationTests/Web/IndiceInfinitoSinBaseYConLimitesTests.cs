using System.Data.Common;
using System.Net;
using System.Text.Json;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// AC-IND-05 y AC-IND-07 del índice infinito (web-indice-infinito T13), en pruebas separadas y en memoria (sin Docker): (1) cincuenta solicitudes HTTP de un visitante sin sesión con un doble que cuenta cualquier uso de la unidad de trabajo o de la conexión
/// y con Data Protection en memoria <b>no abren ninguna conexión</b> (lo que la Plataforma lee o escribe por su cuenta —el anillo de claves, la sesión de un cliente autenticado— no es del índice y queda fuera); (2) la solicitud 61 de un origen sin sesión
/// recibe <c>ECO-LIMITE-EXCEDIDO</c> en cada una de las tres rutas, contada <b>por origen y no por ruta</b>, con las cabeceras del índice también en el 429. <b>Falta, para el turno de Docker:</b> repetir ambas sobre el host compuesto real
/// (<c>AnfitrionRealDelEcommerce</c>, con Redis y PostgreSQL), que además publica el evento del rechazo en la base.
/// </summary>
public sealed class IndiceInfinitoSinBaseYConLimitesTests
{
    private static readonly byte[] ClaveDeHuella = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private sealed class ContadorDeUsoDeLaBase : IUnidadDeTrabajo, IConexionDeLaUnidadDeTrabajo
    {
        private int _usos;

        public int Usos => Volatile.Read(ref _usos);

        public bool HayTransaccion => Usar<bool>();

        public DbConnection Conexion => Usar<DbConnection>();

        public DbTransaction Transaccion => Usar<DbTransaction>();

        public Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default) => Usar<Task<T>>();

        public Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default) => Usar<Task>();

        private T Usar<T>()
        {
            Interlocked.Increment(ref _usos);
            throw new InvalidOperationException("El índice infinito no debe tocar la base de datos.");
        }
    }

    private sealed class TiempoControlable(DateTimeOffset inicio) : TimeProvider
    {
        public DateTimeOffset Ahora { get; } = inicio;

        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private sealed class RelojDePrueba(TiempoControlable tiempo) : IReloj
    {
        public DateTimeOffset Ahora => tiempo.Ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class EventosEspia : IPublicadorDeEventosDeSeguridad
    {
        public List<EventoDeSeguridad> Eventos { get; } = [];

        public Task Publicar(EventoDeSeguridad evento, CancellationToken cancelacion = default)
        {
            lock (Eventos)
            {
                Eventos.Add(evento);
            }

            return Task.CompletedTask;
        }
    }

    private static readonly string[] Rutas =
    [
        "/indice-infinito/s12-p3-e4-v5-pg100",
        "/indice-infinito",
        "/indice-infinito/acciones/al-azar?desde=s12-p3-e4-v5-pg100",
    ];

    [Fact]
    [Trait("Criterio", "AC-IND-05")]
    public async Task Cincuenta_solicitudes_de_un_visitante_sin_sesion_no_abren_ninguna_conexion()
    {
        var contador = new ContadorDeUsoDeLaBase();
        await using var host = await AnfitrionDelIndice.Iniciar(s =>
        {
            s.AddSingleton<IUnidadDeTrabajo>(contador);
            s.AddSingleton<IConexionDeLaUnidadDeTrabajo>(contador);
            s.AddDataProtection().UseEphemeralDataProtectionProvider();
        });

        for (var i = 0; i < 50; i++)
        {
            var respuesta = await host.Cliente.GetAsync(Rutas[i % Rutas.Length]);

            Assert.True(respuesta.StatusCode is HttpStatusCode.OK or HttpStatusCode.Found, $"Estado {(int)respuesta.StatusCode} en la solicitud {i + 1}.");
        }

        Assert.Equal(0, contador.Usos);
    }

    private static async Task<AnfitrionWeb> IniciarConLimites(EventosEspia eventos)
    {
        var tiempo = new TiempoControlable(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));
        var huellas = new CalculadorDeHuellaHmac(new OpcionesDeHuella("v1:" + Convert.ToBase64String(ClaveDeHuella)));
        var local = new MotorDeLimitesLocal(tiempo: tiempo);
        return await AnfitrionDelIndice.Iniciar(
            s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                s.AgregarLimitesHttp(new OpcionesDeLimitesHttp
                {
                    TopePrevio = PoliticasDeLimite.SolicitudesPreviasPorOrigen,
                    TopePrevioDelOrigenDesconocido = PoliticasDeLimite.SolicitudesDelOrigenDesconocido,
                    SinSesionPorOrigen = PoliticasDeLimite.SolicitudesSinSesionPorOrigen,
                    PorUsuario = PoliticasDeLimite.SolicitudesPorUsuario,
                });
                s.AddSingleton(new CatalogoDePoliticas(PoliticasDeSeguridad.Todas.Concat(PoliticasDeSeguridad.Generales)));
                s.AddSingleton<IReloj>(new RelojDePrueba(tiempo));
                s.AddSingleton<IPublicadorDeEventosDeSeguridad>(eventos);
                s.AddScoped<IControlDeLimites>(proveedor => new EvaluarLimite(
                    proveedor.GetRequiredService<CatalogoDePoliticas>(),
                    local,
                    local,
                    proveedor.GetRequiredService<IOrigenDeSolicitud>(),
                    proveedor.GetRequiredService<IPublicadorDeEventosDeSeguridad>(),
                    proveedor.GetRequiredService<IReloj>()));
            },
            antesDelPipeline: app => app.Use(async (contexto, siguiente) =>
            {
                // El origen de la prueba: lo que luego fija OrigenDelBorde.
                var direccion = contexto.Request.Headers["X-Prueba-Origen"].ToString();
                if (direccion.Length > 0)
                {
                    OrigenDeLaSolicitudHttp.Establecer(contexto, new OrigenDeSolicitud(huellas.DeOrigen(direccion), direccion));
                }

                await siguiente(contexto);
            }),
            despuesDelPipeline: app =>
            {
                app.UsarTopePrevio();
                app.UsarLimitesGenerales();
            });
    }

    private static async Task<HttpResponseMessage> Pedir(AnfitrionWeb host, string ruta, string origen)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        solicitud.Headers.Add("X-Prueba-Origen", origen);
        return await host.Cliente.SendAsync(solicitud);
    }

    [Theory]
    [InlineData(0, "203.0.113.10")]
    [InlineData(1, "203.0.113.11")]
    [InlineData(2, "203.0.113.12")]
    [Trait("Criterio", "AC-IND-07")]
    public async Task La_solicitud_61_de_un_origen_sin_sesion_recibe_el_limite_excedido_en_cada_ruta_del_indice(int indiceDeRuta, string origen)
    {
        var eventos = new EventosEspia();
        await using var host = await IniciarConLimites(eventos);
        var ruta = Rutas[indiceDeRuta];
        for (var i = 0; i < 60; i++)
        {
            var permitida = await Pedir(host, ruta, origen);
            Assert.True(permitida.StatusCode is HttpStatusCode.OK or HttpStatusCode.Found, $"La solicitud {i + 1} de {ruta} debía pasar y dio {(int)permitida.StatusCode}.");
        }

        var rechazada = await Pedir(host, ruta, origen);

        Assert.Equal(HttpStatusCode.TooManyRequests, rechazada.StatusCode);
        var cuerpo = JsonDocument.Parse(await rechazada.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ECO-LIMITE-EXCEDIDO", cuerpo.GetProperty("code").GetString());
        Assert.Equal("solicitudes-sin-sesion-por-origen", cuerpo.GetProperty("motivo").GetString());
        Assert.True(rechazada.Headers.Contains("Retry-After"));
        // El 429 también lleva las cabeceras del índice (D06): ninguna caché compartida lo reutiliza ni se indexa.
        Assert.Equal("noindex, nofollow", rechazada.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Equal("no-store", rechazada.Headers.CacheControl?.ToString());
        Assert.NotEmpty(eventos.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-IND-07")]
    public async Task El_limite_se_cuenta_por_origen_y_no_por_ruta_y_otro_origen_no_se_afecta()
    {
        await using var host = await IniciarConLimites(new EventosEspia());
        for (var i = 0; i < 60; i++)
        {
            Assert.True((await Pedir(host, Rutas[i % Rutas.Length], "203.0.113.20")).StatusCode is HttpStatusCode.OK or HttpStatusCode.Found);
        }

        foreach (var ruta in Rutas)
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, (await Pedir(host, ruta, "203.0.113.20")).StatusCode);
        }

        Assert.True((await Pedir(host, Rutas[0], "198.51.100.21")).StatusCode is HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Criterio", "AC-IND-07")]
    public async Task Otra_pagina_al_azar_cuesta_dos_solicitudes_el_302_y_la_pagina()
    {
        await using var host = await IniciarConLimites(new EventosEspia());

        // 30 veces «otra al azar» (el 302 y la página que sigue) agotan las 60 solicitudes del minuto.
        for (var i = 0; i < 30; i++)
        {
            var redireccion = await Pedir(host, "/indice-infinito/acciones/al-azar?desde=s1-p1-e1-v1-pg1", "203.0.113.30");
            Assert.Equal(HttpStatusCode.Found, redireccion.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Pedir(host, redireccion.Headers.Location!.OriginalString, "203.0.113.30")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Pedir(host, "/indice-infinito/acciones/al-azar", "203.0.113.30")).StatusCode);
    }
}
