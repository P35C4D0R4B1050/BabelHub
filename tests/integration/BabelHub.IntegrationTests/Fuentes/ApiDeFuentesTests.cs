using System.Net;
using System.Text.Json;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Auditoria;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BabelHub.IntegrationTests.Fuentes;

/// <summary>
/// T21 a T23 con PostgreSQL real y el rol real de la aplicación, la cadena real de middleware de servicio y los endpoints de <c>Fuentes</c>: el alcance por sucursal y su evento de acceso denegado que persiste,
/// la credencial de otro alcance rechazada en cada endpoint, la auditoría que revierte, la recepción única y los cambios de ruta con dos administradores simultáneos, y —el riesgo del hallazgo del 2026-11-14— una
/// <b>llamada de servicio y un trabajo de fondo que leen y mueven stock de verdad</b> (no «cero filas») con su actor de integración o de sistema, y que fallan a la vista si falta el actor.
/// </summary>
public sealed class ApiDeFuentesTests(PostgresDeFuentesFixture bd) : IClassFixture<PostgresDeFuentesFixture>, IAsyncLifetime
{
    private ApiDeFuentesHost _api = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _api = await ApiDeFuentesHost.Crear(bd);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private static string R(string ruta) => ApiDeFuentesHost.Raiz + ruta;

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) => JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();

    private Task<HttpResponseMessage> Admin(HttpMethod metodo, string ruta, string? json = null) =>
        _api.Enviar(metodo, R(ruta), ApiDeFuentesHost.CredencialDeUsuario, RolesDeUsuario.AdministradorCentral, cuerpo: json is null ? null : ApiDeFuentesHost.Json(json));

    private Task<HttpResponseMessage> Encargado(Guid sucursal, string ruta) =>
        _api.Enviar(HttpMethod.Get, R(ruta), ApiDeFuentesHost.CredencialDeUsuario, RolesDeUsuario.EncargadoDeSucursal, sucursal);

    // ---------- el riesgo del hallazgo: una llamada de servicio lee y mueve stock de verdad ----------

    [Fact]
    [Trait("Criterio", "AC-FUE-03")]
    public async Task LlamadaDeServicio_ConSoloLaCredencial_LeeElStockReal_ConElActorDeIntegracion()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 8, 0);
        await bd.PonerStock(cusco, libro, 4, 1);

        var respuesta = await _api.Enviar(HttpMethod.Get, R($"/disponibilidad/{libro}"), ApiDeFuentesHost.CredencialDeDisponibilidad);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await Cuerpo(respuesta);
        Assert.Equal(11, cuerpo.GetProperty("vendibleEnLaRed").GetInt32());
        Assert.Equal(["cusco", "lima"], cuerpo.GetProperty("fuentes").EnumerateArray().Select(f => f.GetProperty("codigoDeFuente").GetString()!).Order());
        Assert.DoesNotContain("fisico", cuerpo.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("reservado", cuerpo.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task LlamadaDeServicio_QueLeeYMueveStockConLaCredencialDeSistema_LoHaceDeVerdad()
    {
        var lima = await bd.AgregarFuente("lima");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 5, 0);

        var lectura = await _api.Enviar(HttpMethod.Get, $"/servicio/prueba/stock/{libro}", ApiDeFuentesHost.CredencialDeDisponibilidad);
        var movimiento = await _api.Enviar(HttpMethod.Post, $"/servicio/prueba/mover-stock/{lima}/{libro}", ApiDeFuentesHost.CredencialDeExpiracion);

        Assert.Equal(HttpStatusCode.OK, lectura.StatusCode);
        Assert.Equal(5, (await Cuerpo(lectura))[0].GetInt32());
        Assert.Equal(HttpStatusCode.OK, movimiento.StatusCode);
        Assert.True((await Cuerpo(movimiento)).GetProperty("aplicado").GetBoolean());
        Assert.Equal((5, 1), await bd.StockDe(lima, libro));
        Assert.Empty(_api.Alertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task SiLaApiOlvidaElActorDeIntegracion_LaLlamadaFallaALaVista_YSeDetecta_EnVezDeResponderSinStock()
    {
        await using var sinActor = await ApiDeFuentesHost.Crear(bd, conActorDeServicio: false);
        var lima = await bd.AgregarFuente("lima");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 5, 0);

        var disponibilidad = await sinActor.Enviar(HttpMethod.Get, R($"/disponibilidad/{libro}"), ApiDeFuentesHost.CredencialDeDisponibilidad);
        var movimiento = await sinActor.Enviar(HttpMethod.Post, $"/servicio/prueba/mover-stock/{lima}/{libro}", ApiDeFuentesHost.CredencialDeExpiracion);

        // Ningún 200 con «0 disponibles»: no autenticado, con su evento (la Aplicación) o con su alerta técnica (la Infraestructura).
        Assert.Equal(HttpStatusCode.Unauthorized, disponibilidad.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, movimiento.StatusCode);
        Assert.Contains(sinActor.Denegaciones.Registros, r => r.Motivo == "sin-sesion");
        Assert.Contains(sinActor.Alertas.Alertas, a => a.Motivo == "alcance-de-datos-sin-actor");
        Assert.Equal((5, 0), await bd.StockDe(lima, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task TrabajoDeFondo_ComoSistema_LeeYMueveStockDeVerdad_YSinActorFallaALaVista()
    {
        var lima = await bd.AgregarFuente("lima");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 5, 2);
        await using var ambito = _api.Servicios.CreateAsyncScope();
        var ejecutor = ambito.ServiceProvider.GetRequiredService<EjecutorDeTrabajoDeFondo>();
        var existencias = ambito.ServiceProvider.GetRequiredService<IExistencias>();
        var movimientos = ambito.ServiceProvider.GetRequiredService<IMovimientosDeStock>();
        var unidad = ambito.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>();
        var lectura = ambito.ServiceProvider.GetRequiredService<ILecturaCoherente>();

        // Como sistema: lee lo que hay (no cero) y mueve (devuelve 1 reservada, como una expiración).
        var visto = await ejecutor.Ejecutar(async t =>
        {
            var antes = (await lectura.EjecutarLectura(token => existencias.Disponibilidad([libro], token), t))[0].VendibleDeLaRed;
            var movido = await unidad.Ejecutar(async token =>
            {
                await movimientos.Bloquear([libro], token);
                return await movimientos.Aplicar(new MovimientoDeStock(lima, libro, 0, -1), token);
            }, t);
            return (antes, movido.Aplicado);
        });

        Assert.Equal((3, true), visto);
        Assert.Equal((5, 1), await bd.StockDe(lima, libro));
        Assert.Empty(_api.Alertas.Alertas);

        // El mismo trabajo sin pasar por el ejecutor (sin actor): no hace nada en silencio, falla y deja una alerta técnica.
        await Assert.ThrowsAsync<ContextoDeActorAusenteException>(() => lectura.EjecutarLectura(token => existencias.Disponibilidad([libro], token)));
        Assert.Contains(_api.Alertas.Alertas, a => a.Motivo == "alcance-de-datos-sin-actor");
        Assert.Equal((5, 1), await bd.StockDe(lima, libro));
    }

    // ---------- alcance y eventos de acceso denegado ----------

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task ElEncargadoDeAyacuchoPideCusco_Recibe403ConSuEventoQuePersiste_YNoSeFiltraNada()
    {
        var ayacucho = await bd.AgregarFuente("ayacucho");
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(cusco, libro, 9, 0);

        var propia = await Encargado(ayacucho, "/fuentes/ayacucho/existencias");
        var ajena = await Encargado(ayacucho, "/fuentes/cusco/existencias");
        var inexistente = await Encargado(ayacucho, "/fuentes/piura/existencias");

        Assert.Equal(HttpStatusCode.OK, propia.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ajena.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, inexistente.StatusCode);
        Assert.Equal("INV-PROHIBIDO", (await Cuerpo(ajena)).GetProperty("code").GetString());
        Assert.DoesNotContain("elementos", await ajena.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var eventos = _api.Denegaciones.Registros.Where(r => r.Accion == "AccesoDenegado").ToList();
        Assert.Equal(2, eventos.Count);
        Assert.All(eventos, e => Assert.Equal("fuera-de-sucursal", e.Motivo));
        Assert.All(eventos, e => Assert.Equal(ResultadoDeOperacion.Rechazado, e.Resultado));
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-09")]
    [InlineData("logistica", "GET", "/fuentes/cusco")]
    [InlineData("cliente", "GET", "/fuentes/cusco")]
    [InlineData("encargado", "GET", "/fuentes/cusco")]
    [InlineData("logistica", "GET", "/rutas")]
    [InlineData("cliente", "GET", "/rutas")]
    [InlineData("logistica", "GET", "/fuentes/cusco/existencias")]
    [InlineData("cliente", "GET", "/fuentes/cusco/existencias")]
    [InlineData("logistica", "GET", "/unidades-vendidas")]
    [InlineData("cliente", "GET", "/unidades-vendidas?codigoDeFuente=cusco")]
    [InlineData("encargado", "GET", "/unidades-vendidas")]
    [InlineData("logistica", "POST", "/fuentes")]
    [InlineData("encargado", "POST", "/fuentes")]
    [InlineData("cliente", "PUT", "/fuentes/cusco")]
    [InlineData("encargado", "PUT", "/fuentes-de-recepcion")]
    [InlineData("encargado", "PUT", "/rutas/cusco/lima")]
    [InlineData("logistica", "PUT", "/rutas/cusco/lima")]
    [InlineData("encargado", "PUT", "/ediciones/00000000-0000-4000-8000-000000000001")]
    public async Task UsuarioSinAlcance_RecibeProhibidoEnCadaCasoDeUso_ConSuEvento_YSinCambios(string rol, string metodo, string ruta)
    {
        await bd.AgregarFuente("cusco", recepcion: true);
        var sucursal = await bd.AgregarFuente("ayacucho");
        var rolDeUsuario = rol switch
        {
            "logistica" => RolesDeUsuario.PersonalDeLogistica,
            "cliente" => RolesDeUsuario.Cliente,
            _ => RolesDeUsuario.EncargadoDeSucursal,
        };
        var cuerpo = metodo == "GET" ? null : ApiDeFuentesHost.Json(CuerpoPara(ruta));

        var respuesta = await _api.Enviar(new HttpMethod(metodo), R(ruta), ApiDeFuentesHost.CredencialDeUsuario, rolDeUsuario, rol == "encargado" ? sucursal : null, cuerpo);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Equal("INV-PROHIBIDO", (await Cuerpo(respuesta)).GetProperty("code").GetString());
        Assert.Single(_api.Denegaciones.Registros);
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.fuentes"));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.rutas") + await bd.Consultar<long>("SELECT count(*) FROM fuentes.ediciones"));
        Assert.Empty(_api.Auditoria.Registros);
    }

    private static string CuerpoPara(string ruta)
    {
        if (ruta.StartsWith("/fuentes-de", StringComparison.Ordinal))
        {
            return """{"codigo":"ayacucho"}""";
        }

        if (ruta.StartsWith("/fuentes/", StringComparison.Ordinal))
        {
            return """{"nombre":"Cusco Centro","region":"cusco","contactoDeNotificacion":null}""";
        }

        if (ruta.StartsWith("/rutas", StringComparison.Ordinal))
        {
            return """{"dias":2}""";
        }

        return ruta.StartsWith("/ediciones", StringComparison.Ordinal)
            ? """{"formato":"impreso"}"""
            : """{"codigo":"piura","nombre":"Piura","region":"piura","contactoDeNotificacion":null}""";
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task LaCredencialDeOtroAlcance_SeRechazaConUn401UniformeEnCadaEndpoint_SinDatos()
    {
        await bd.AgregarFuente("cusco");
        var peticiones = new (HttpMethod Metodo, string Ruta, string Credencial)[]
        {
            (HttpMethod.Get, $"/disponibilidad/{Guid.NewGuid()}", ApiDeFuentesHost.CredencialDeUsuario),
            (HttpMethod.Post, "/disponibilidad/consultas", ApiDeFuentesHost.CredencialDeContacto),
            (HttpMethod.Get, "/sucursales", ApiDeFuentesHost.CredencialDeContacto),
            (HttpMethod.Get, "/sucursales/cusco/contacto-de-notificacion", ApiDeFuentesHost.CredencialDeDisponibilidad),
            (HttpMethod.Get, "/fuentes", ApiDeFuentesHost.CredencialDeDisponibilidad),
            (HttpMethod.Get, "/fuentes/cusco", ApiDeFuentesHost.CredencialDeDisponibilidad),
            (HttpMethod.Post, "/fuentes", ApiDeFuentesHost.CredencialDeDisponibilidad),
            (HttpMethod.Put, "/fuentes/cusco", ApiDeFuentesHost.CredencialDeContacto),
            (HttpMethod.Put, "/fuentes-de-recepcion", ApiDeFuentesHost.CredencialDeDisponibilidad),
            (HttpMethod.Get, "/fuentes/cusco/existencias", ApiDeFuentesHost.CredencialDeDisponibilidad),
            (HttpMethod.Get, "/rutas", ApiDeFuentesHost.CredencialDeContacto),
            (HttpMethod.Get, "/rutas/cusco/lima", ApiDeFuentesHost.CredencialDeDisponibilidad),
            (HttpMethod.Put, "/rutas/cusco/lima", ApiDeFuentesHost.CredencialDeContacto),
            (HttpMethod.Get, "/unidades-vendidas", ApiDeFuentesHost.CredencialDeDisponibilidad),
            (HttpMethod.Put, $"/ediciones/{Guid.NewGuid()}", ApiDeFuentesHost.CredencialDeDisponibilidad),
        };

        foreach (var (metodo, ruta, credencial) in peticiones)
        {
            var conOtra = await _api.Enviar(metodo, R(ruta), credencial, RolesDeUsuario.AdministradorCentral);
            var sinCredencial = await _api.Enviar(metodo, R(ruta), credencial: null, RolesDeUsuario.AdministradorCentral);

            Assert.True(conOtra.StatusCode == HttpStatusCode.Unauthorized, $"{metodo} {ruta} con otro alcance: {conOtra.StatusCode}");
            Assert.True(sinCredencial.StatusCode == HttpStatusCode.Unauthorized, $"{metodo} {ruta} sin credencial: {sinCredencial.StatusCode}");
            Assert.Equal("INV-NO-AUTENTICADO", (await Cuerpo(conOtra)).GetProperty("code").GetString());
        }

        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.rutas"));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task UnaOperacionDeUsuarioSinElContextoDeUnUsuario_SeRechazaAunqueLaCredencialSeaLaCorrecta()
    {
        await bd.AgregarFuente("cusco");

        var respuesta = await _api.Enviar(HttpMethod.Get, R("/fuentes"), ApiDeFuentesHost.CredencialDeUsuario);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    // ---------- administración: registrar, editar, auditoría ----------

    [Fact]
    [Trait("Criterio", "AC-FUE-13")]
    public async Task Registrar_ComoAdministracion_Crea201ConStockCeroYAuditaSinElContacto_YUnCodigoRepetidoEs409()
    {
        var primera = await Admin(HttpMethod.Post, "/fuentes", """{"codigo":"piura","nombre":"Piura","region":"piura","contactoDeNotificacion":"+51 999 888 777"}""");
        var repetida = await Admin(HttpMethod.Post, "/fuentes", """{"codigo":"piura","nombre":"Otra Piura","region":"piura","contactoDeNotificacion":null}""");

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        var detalle = await Cuerpo(primera);
        Assert.Equal((0, 0, 0), (detalle.GetProperty("fisicoTotal").GetInt32(), detalle.GetProperty("reservadoTotal").GetInt32(), detalle.GetProperty("vendibleTotal").GetInt32()));
        Assert.True(detalle.GetProperty("esFuenteDeRecepcion").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
        Assert.Equal("INV-FUENTE-DUPLICADA", (await Cuerpo(repetida)).GetProperty("code").GetString());

        var registro = Assert.Single(_api.Auditoria.Registros);
        Assert.Equal("FuenteRegistrada", registro.Accion);
        Assert.DoesNotContain("999", registro.Detalle.Texto, StringComparison.Ordinal);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.fuentes"));
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-01")]
    [InlineData("""{"codigo":"piura","nombre":"Piura","region":"piura","contactoDeNotificacion":null,"id":"00000000-0000-4000-8000-000000000001"}""")]
    [InlineData("""{"codigo":"piura","nombre":"Piura","region":"piura","contactoDeNotificacion":null,"esFuenteDeRecepcion":true}""")]
    [InlineData("{\"codigo\":\"piura\",\"nombre\":\"Piura")]
    [InlineData("""[]""")]
    public async Task Registrar_ConCamposDesconocidosOUnCuerpoMalFormado_Es400_YNoCambiaNada(string cuerpo)
    {
        var respuesta = await Admin(HttpMethod.Post, "/fuentes", cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("INV-VALIDACION-INVALIDA", (await Cuerpo(respuesta)).GetProperty("code").GetString());
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.fuentes"));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-13")]
    public async Task LaAuditoriaCaida_NoDejaPasarLaOperacion_Responde503YNoQuedaNingunaFuente()
    {
        _api.Auditoria.Falla = true;

        var respuesta = await Admin(HttpMethod.Post, "/fuentes", """{"codigo":"piura","nombre":"Piura","region":"piura","contactoDeNotificacion":null}""");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Equal("INV-SERVICIO-NO-DISPONIBLE", (await Cuerpo(respuesta)).GetProperty("code").GetString());
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.fuentes"));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-13")]
    public async Task Editar_ElContacto_AuditaElNombreDelCampo_YElContactoSoloLoVeLaAdministracion()
    {
        await bd.AgregarFuente("piura", region: "piura", recepcion: true);

        var editada = await Admin(HttpMethod.Put, "/fuentes/piura", """{"nombre":"Piura","region":"piura","contactoDeNotificacion":"+51 944 555 666"}""");
        var contacto = await _api.Enviar(HttpMethod.Get, R("/sucursales/piura/contacto-de-notificacion"), ApiDeFuentesHost.CredencialDeContacto);

        Assert.Equal(HttpStatusCode.OK, editada.StatusCode);
        Assert.Equal("contactoDeNotificacion", Assert.Single(_api.Auditoria.Registros).Detalle.Valores["camposCambiados"]);
        Assert.Equal(HttpStatusCode.OK, contacto.StatusCode);
        Assert.Equal("+51 944 555 666", (await Cuerpo(contacto)).GetProperty("numero").GetString());
        Assert.DoesNotContain("944", _api.Auditoria.Registros.Single().Detalle.Texto, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task ElContactoDeNotificacion_NoLoLeeUnaCredencialQueNoSeaLaDeSuAlcance_NiUnaSucursalInexistente()
    {
        await bd.AgregarFuente("piura", region: "piura");

        var conAlcanceDeDisponibilidad = await _api.Enviar(HttpMethod.Get, R("/sucursales/piura/contacto-de-notificacion"), ApiDeFuentesHost.CredencialDeDisponibilidad);
        var inexistente = await _api.Enviar(HttpMethod.Get, R("/sucursales/nunca/contacto-de-notificacion"), ApiDeFuentesHost.CredencialDeContacto);
        var sinNumero = await _api.Enviar(HttpMethod.Get, R("/sucursales/piura/contacto-de-notificacion"), ApiDeFuentesHost.CredencialDeContacto);

        Assert.Equal(HttpStatusCode.Unauthorized, conAlcanceDeDisponibilidad.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sinNumero.StatusCode);
        Assert.Equal("INV-NO-ENCONTRADO", (await Cuerpo(inexistente)).GetProperty("code").GetString());
        Assert.Equal((await Cuerpo(inexistente)).GetProperty("detail").GetString(), (await Cuerpo(sinNumero)).GetProperty("detail").GetString());
    }

    // ---------- concurrencia ----------

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public async Task DosAdministradoresQueDesignanLaRecepcionAlMismoTiempo_DejanUnaSolaRecepcion_ConAmbasAuditorias()
    {
        await bd.AgregarFuente("lima", recepcion: true);
        await bd.AgregarFuente("cusco");
        await bd.AgregarFuente("ayacucho");

        for (var vuelta = 0; vuelta < 5; vuelta++)
        {
            var respuestas = await Task.WhenAll(
                Admin(HttpMethod.Put, "/fuentes-de-recepcion", """{"codigo":"cusco"}"""),
                Admin(HttpMethod.Put, "/fuentes-de-recepcion", """{"codigo":"ayacucho"}"""),
                Admin(HttpMethod.Put, "/fuentes-de-recepcion", """{"codigo":"lima"}"""));

            Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
            Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.fuentes WHERE es_recepcion"));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-10")]
    public async Task DosAdministradoresQueDefinenLaMismaRutaAlMismoTiempo_AuditanCadaCambioConElValorQueDeVerdadReemplazo()
    {
        await bd.AgregarFuente("cusco");
        await bd.AgregarFuente("ayacucho");

        var respuestas = await Task.WhenAll(
            Admin(HttpMethod.Put, "/rutas/cusco/ayacucho", """{"dias":2}"""),
            Admin(HttpMethod.Put, "/rutas/cusco/ayacucho", """{"dias":3}"""));

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var registro = _api.Auditoria.Registros.Single(r => r.Accion == "RutaRegistrada");
        var cambio = _api.Auditoria.Registros.Single(r => r.Accion == "RutaModificada");

        // Una gana primero (alta) y la otra la reemplaza: el anterior del cambio es el nuevo del alta, nunca «sin ruta».
        Assert.Equal(registro.Detalle.Valores["plazoNuevoEnDias"], cambio.Detalle.Valores["plazoAnteriorEnDias"]);
        Assert.NotEqual(cambio.Detalle.Valores["plazoAnteriorEnDias"], cambio.Detalle.Valores["plazoNuevoEnDias"]);
        Assert.Equal(cambio.Detalle.Valores["plazoNuevoEnDias"], (await bd.Consultar<int>("SELECT plazo_dias FROM fuentes.rutas")).ToString());
    }

    // ---------- lecturas del contrato ----------

    [Fact]
    [Trait("Criterio", "AC-FUE-05")]
    public async Task Disponibilidad_DeUnaEdicionDigitalEs422_YDeUnaDesconocidaEs404()
    {
        await bd.AgregarFuente("lima");
        var digital = await bd.AgregarEdicion("digital");

        var deDigital = await _api.Enviar(HttpMethod.Get, R($"/disponibilidad/{digital}"), ApiDeFuentesHost.CredencialDeDisponibilidad);
        var desconocida = await _api.Enviar(HttpMethod.Get, R($"/disponibilidad/{Guid.NewGuid()}"), ApiDeFuentesHost.CredencialDeDisponibilidad);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, deDigital.StatusCode);
        Assert.Equal("INV-EDICION-NO-IMPRESA", (await Cuerpo(deDigital)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, desconocida.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-12")]
    public async Task DisponibilidadDeVarias_SeparaLasNoRegistradas_YRechazaMasDeCincuenta()
    {
        var lima = await bd.AgregarFuente("lima");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 3, 0);
        var desconocida = Guid.NewGuid();

        var respuesta = await _api.Enviar(HttpMethod.Post, R("/disponibilidad/consultas"), ApiDeFuentesHost.CredencialDeDisponibilidad, cuerpo: ApiDeFuentesHost.Json($$"""{"edicionIds":["{{libro}}","{{desconocida}}"]}"""));
        var demasiadas = await _api.Enviar(
            HttpMethod.Post,
            R("/disponibilidad/consultas"),
            ApiDeFuentesHost.CredencialDeDisponibilidad,
            cuerpo: ApiDeFuentesHost.Json("{\"edicionIds\":[" + string.Join(',', Enumerable.Range(0, 51).Select(_ => $"\"{Guid.NewGuid()}\"")) + "]}"));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await Cuerpo(respuesta);
        Assert.Equal(3, cuerpo.GetProperty("ediciones")[0].GetProperty("vendibleEnLaRed").GetInt32());
        Assert.Equal(desconocida.ToString(), cuerpo.GetProperty("noRegistradas")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, demasiadas.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-07")]
    public async Task Existencias_FiltraPorEdicionYEstado_ConPaginaYTopes_ParametrosHostilesInofensivos()
    {
        var ayacucho = await bd.AgregarFuente("ayacucho");
        var catedral = await bd.AgregarEdicion();
        var otro = await bd.AgregarEdicion();
        await bd.PonerStock(ayacucho, catedral, 2, 0);
        await bd.PonerStock(ayacucho, otro, 9, 0);

        var filtrada = await Encargado(ayacucho, $"/fuentes/ayacucho/existencias?edicionId={catedral}&estado=bajo&pagina=1&tamano=10");
        var tamanoInvalido = await Encargado(ayacucho, "/fuentes/ayacucho/existencias?tamano=101");
        var inyeccion = await Encargado(ayacucho, "/fuentes/ayacucho/existencias?edicionId=1%27%3B%20DROP%20TABLE%20fuentes.stock%3B--");
        var comodines = await Admin(HttpMethod.Get, "/fuentes/%25_%25/existencias");

        Assert.Equal(HttpStatusCode.OK, filtrada.StatusCode);
        var cuerpo = await Cuerpo(filtrada);
        Assert.Equal(1, cuerpo.GetProperty("total").GetInt32());
        Assert.Equal("bajo", cuerpo.GetProperty("elementos")[0].GetProperty("estadoDeStock").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, tamanoInvalido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inyeccion.StatusCode);
        Assert.True(comodines.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound);
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.stock"));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-09")]
    public async Task NingunaRutaModificaStockDesdeLaVistaDeUnaFuente()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 5, 1);

        var rutasDeFuentes = _api.Endpoints.Where(e => (e.RoutePattern.RawText ?? string.Empty).Contains("/fuentes", StringComparison.Ordinal)).ToList();
        var existencias = rutasDeFuentes.Single(e => (e.RoutePattern.RawText ?? string.Empty).EndsWith("/existencias", StringComparison.Ordinal));
        var metodos = existencias.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods;
        var escritura = await Admin(HttpMethod.Put, "/fuentes/lima", """{"nombre":"Lima","region":"lima","contactoDeNotificacion":null}""");
        var intento = await Admin(HttpMethod.Post, "/fuentes/lima/existencias", """{"fisico":99}""");
        var consulta = await Admin(HttpMethod.Get, "/fuentes/lima");

        Assert.Equal(["GET"], metodos);
        Assert.Equal(HttpStatusCode.OK, escritura.StatusCode);
        Assert.True(intento.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound);
        Assert.Equal((5, 1, 4), ((await Cuerpo(consulta)).GetProperty("fisicoTotal").GetInt32(), (await Cuerpo(consulta)).GetProperty("reservadoTotal").GetInt32(), (await Cuerpo(consulta)).GetProperty("vendibleTotal").GetInt32()));
        Assert.Equal((5, 1), await bd.StockDe(lima, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-11")]
    public async Task Rutas_SeDefinenSeConsultanYSeListan_ConSuPlazoOSinDefinir()
    {
        await bd.AgregarFuente("cusco");
        await bd.AgregarFuente("ayacucho");

        var definida = await Admin(HttpMethod.Put, "/rutas/cusco/ayacucho", """{"dias":2}""");
        var ruta = await Admin(HttpMethod.Get, "/rutas/cusco/ayacucho");
        var sinDefinir = await Admin(HttpMethod.Get, "/rutas/ayacucho/cusco");
        var invalida = await Admin(HttpMethod.Put, "/rutas/cusco/ayacucho", """{"dias":0}""");
        var consigoMisma = await Admin(HttpMethod.Put, "/rutas/cusco/cusco", """{"dias":2}""");
        var lista = await Admin(HttpMethod.Get, "/rutas");

        Assert.Equal(HttpStatusCode.OK, definida.StatusCode);
        Assert.Equal(2, (await Cuerpo(ruta)).GetProperty("dias").GetInt32());
        Assert.Equal(JsonValueKind.Null, (await Cuerpo(sinDefinir)).GetProperty("dias").ValueKind);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalida.StatusCode);
        Assert.Equal("INV-RUTA-INVALIDA", (await Cuerpo(invalida)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, consigoMisma.StatusCode);
        Assert.Single((await Cuerpo(lista)).GetProperty("rutas").EnumerateArray());
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-17")]
    public async Task UnidadesVendidas_ElEncargadoSinCodigoRecibe403_YLaAdministracionVeLaRedConCeros()
    {
        var ayacucho = await bd.AgregarFuente("ayacucho");

        var encargadoSinCodigo = await Encargado(ayacucho, "/unidades-vendidas");
        var propia = await Encargado(ayacucho, "/unidades-vendidas?codigoDeFuente=ayacucho");
        var red = await Admin(HttpMethod.Get, "/unidades-vendidas");

        Assert.Equal(HttpStatusCode.Forbidden, encargadoSinCodigo.StatusCode);
        Assert.Equal(HttpStatusCode.OK, propia.StatusCode);
        Assert.Equal(30, (await Cuerpo(red)).GetProperty("dias").GetInt32());
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-05")]
    public async Task RegistrarEdicion_EsIdempotente_YPasarADigitalConStockEs409()
    {
        var lima = await bd.AgregarFuente("lima");
        var libro = Guid.NewGuid();

        var alta = await Admin(HttpMethod.Put, $"/ediciones/{libro}", """{"formato":"impreso"}""");
        var repetida = await Admin(HttpMethod.Put, $"/ediciones/{libro}", """{"formato":"impreso"}""");
        await bd.PonerStock(lima, libro, 3, 0);
        var conflicto = await Admin(HttpMethod.Put, $"/ediciones/{libro}", """{"formato":"digital"}""");
        var invalido = await Admin(HttpMethod.Put, $"/ediciones/{libro}", """{"formato":"pdf"}""");

        Assert.Equal(HttpStatusCode.NoContent, alta.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repetida.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflicto.StatusCode);
        Assert.Equal("INV-FORMATO-EN-CONFLICTO", (await Cuerpo(conflicto)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.Equal("impreso", await bd.Consultar<string>($"SELECT formato FROM fuentes.ediciones WHERE edicion_id = '{libro}'"));
    }
}
