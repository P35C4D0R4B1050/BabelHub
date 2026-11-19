using System.Net;
using System.Text.Json;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Inventario;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>
/// T31: la superficie HTTP de las reservas frente al abuso, con PostgreSQL real y la cadena real de credenciales. Cada rechazo de alcance (credencial de otro alcance con el 401 uniforme, usuario sin alcance con 403) deja su evento
/// <b>antes</b> de mirar la clave de idempotencia, el estado o <c>ya-cerrada</c>, y nunca revela si la reserva existe, está cerrada o tiene un resultado guardado; los cuerpos con campos de más y los topes de entrada se rechazan;
/// <c>operaciones_idempotentes</c> y los registros no guardan datos personales; y la consulta previa, sin token, está sujeta al límite general.
/// </summary>
public sealed class AbusoDeLaApiDeReservasTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private ApiDeReservasHost _api = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _api = await ApiDeReservasHost.Crear(bd);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) => JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();

    private static string Clave() => Guid.NewGuid().ToString("N");

    /// <summary>El cuerpo de un error sin el identificador de traza, que es distinto en cada respuesta.</summary>
    private static string SinTraza(string cuerpo) => System.Text.RegularExpressions.Regex.Replace(cuerpo, "\"traceId\":\"[^\"]*\"", "\"traceId\":\"-\"");

    private static string ReservarJson(Guid pedido, Guid linea, Guid edicion, int cantidad = 1, string tipo = "recojo") =>
        tipo == "recojo"
            ? $$"""{"pedidoId":"{{pedido}}","tipoDeEntrega":"recojo","codigoDeSucursalDeRecojo":"lima","regionDeEntrega":null,"lineas":[{"lineaId":"{{linea}}","edicionId":"{{edicion}}","cantidad":{{cantidad}}}]}"""
            : $$"""{"pedidoId":"{{pedido}}","tipoDeEntrega":"domicilio","codigoDeSucursalDeRecojo":null,"regionDeEntrega":"lima","lineas":[{"lineaId":"{{linea}}","edicionId":"{{edicion}}","cantidad":{{cantidad}}}]}""";

    private Task<HttpResponseMessage> Crear(Guid usuario, string json) =>
        _api.Enviar(HttpMethod.Post, "/reservas", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: usuario, json: json, clave: Clave());

    private Task<HttpResponseMessage> Servicio(string ruta, string? json = null) =>
        _api.Enviar(HttpMethod.Post, ruta, ApiDeReservasHost.CredencialDeExpiracion, json: json, clave: Clave());

    private async Task<(Guid Lima, Guid Libro)> Escenario(int fisico = 50)
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, fisico, 0);
        return (lima, libro);
    }

    /// <summary>Una reserva confirmada: devuelve el pedido y el identificador de su línea.</summary>
    private async Task<(Guid Pedido, Guid Linea)> Confirmada(Guid cliente, Guid libro, int cantidad = 2, string tipo = "recojo")
    {
        var pedido = Guid.NewGuid();
        var linea = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await Crear(cliente, ReservarJson(pedido, linea, libro, cantidad, tipo))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Servicio($"/reservas/{pedido}/confirmar")).StatusCode);
        return (pedido, linea);
    }

    private static string Entrega(Guid linea) => $$"""{"motivo":"entrega","lineaIds":["{{linea}}"],"envioId":null}""";

    private int Rechazos(TipoDeEventoDeSeguridad tipo) => _api.Eventos.Eventos.Count(e => e.Tipo == tipo);

    // ---------- alcance y credencial ----------

    [Fact]
    [Trait("Criterio", "AC-SEL-40")]
    public async Task LaCredencialDeUsuarioEnUnaOperacionDeSistema_YLaDeSistemaEnUnaDeUsuario_SeRechazaConElUniforme401_ConEventoYSinTocarNada()
    {
        var (lima, libro) = await Escenario();
        var cliente = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await Crear(cliente, ReservarJson(pedido, Guid.NewGuid(), libro, 2))).StatusCode);
        var antes = Rechazos(TipoDeEventoDeSeguridad.LlamadaEntreServiciosSinCredencial);
        var intentos = 0;

        // Una operación de sistema (sin token) con la credencial de usuario, aunque lleve un usuario con rol: se rechaza por el alcance.
        foreach (var ruta in new[] { "confirmar", "extender", "liberar" })
        {
            var json = ruta == "liberar" ? """{"motivo":"pedido-anulado"}""" : null;
            var conUsuario = await _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/{ruta}", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.AdministradorCentral, json: json, clave: Clave());
            var comoCliente = await _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/{ruta}", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: cliente, json: json, clave: Clave());
            Assert.Equal(HttpStatusCode.Unauthorized, conUsuario.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, comoCliente.StatusCode);
            intentos += 2;
        }

        // Y al revés: la credencial de las operaciones de sistema en una de usuario.
        var crear = await _api.Enviar(HttpMethod.Post, "/reservas", ApiDeReservasHost.CredencialDeExpiracion, RolesDeUsuario.Cliente, usuario: cliente, json: ReservarJson(Guid.NewGuid(), Guid.NewGuid(), libro), clave: Clave());
        var cerrar = await _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/cerrar", ApiDeReservasHost.CredencialDeExpiracion, RolesDeUsuario.EncargadoDeSucursal, lima, json: Entrega(Guid.NewGuid()), clave: Clave());
        var consultar = await _api.Enviar(HttpMethod.Get, $"/reservas/{pedido}", ApiDeReservasHost.CredencialDeExpiracion, RolesDeUsuario.Cliente, usuario: cliente);
        Assert.Equal(HttpStatusCode.Unauthorized, crear.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, cerrar.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, consultar.StatusCode);
        intentos += 3;

        Assert.Equal(antes + intentos, Rechazos(TipoDeEventoDeSeguridad.LlamadaEntreServiciosSinCredencial));
        Assert.Equal((50, 2), await bd.StockDe(lima, libro));
        Assert.Equal("pendiente", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-40")]
    public async Task UnaCredencialDeOtroAlcanceQueIntentaLiberarOExtender_RecibeLaMismaRespuestaExistaONoLaReserva_ConEventoAntesDeLaClave()
    {
        var (_, libro) = await Escenario();
        var existente = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await Crear(Guid.NewGuid(), ReservarJson(existente, Guid.NewGuid(), libro))).StatusCode);
        var inexistente = Guid.NewGuid();
        var antes = Rechazos(TipoDeEventoDeSeguridad.LlamadaEntreServiciosSinCredencial);

        foreach (var (ruta, json) in new[] { ("liberar", """{"motivo":"pedido-anulado"}"""), ("extender", null) })
        {
            // Sin clave de idempotencia y con la credencial de otro alcance: el rechazo es el del alcance, no un 400 de la clave ni un 404/409 del estado.
            var sobreLaExistente = await _api.Enviar(HttpMethod.Post, $"/reservas/{existente}/{ruta}", ApiDeReservasHost.CredencialDePendientes, json: json);
            var sobreLaInexistente = await _api.Enviar(HttpMethod.Post, $"/reservas/{inexistente}/{ruta}", ApiDeReservasHost.CredencialDePendientes, json: json);
            var conLaDeBaja = await _api.Enviar(HttpMethod.Post, $"/reservas/{existente}/{ruta}", ApiDeReservasHost.CredencialDeBaja, json: json, clave: Clave());

            Assert.Equal(HttpStatusCode.Unauthorized, sobreLaExistente.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, sobreLaInexistente.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, conLaDeBaja.StatusCode);
            Assert.Equal(SinTraza(await sobreLaExistente.Content.ReadAsStringAsync()), SinTraza(await sobreLaInexistente.Content.ReadAsStringAsync()));
        }

        Assert.Equal(antes + 6, Rechazos(TipoDeEventoDeSeguridad.LlamadaEntreServiciosSinCredencial));
        Assert.Equal("pendiente", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{existente}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-40")]
    [Trait("Criterio", "AC-SEL-39")]
    public async Task ElEncargadoDeOtraSucursalNoEntrega_YElDeOtroRolNoPasaAEnReparto_403ConEventoAntesDelEstadoYYaCerrada_SinRevelarElResultado()
    {
        var (lima, libro) = await Escenario();
        var cusco = await bd.AgregarFuente("cusco");
        var cliente = Guid.NewGuid();
        var (recojo, lineaDeRecojo) = await Confirmada(cliente, libro, 2);
        var (domicilio, lineaDeDomicilio) = await Confirmada(cliente, libro, 3, "domicilio");
        var envio = Guid.NewGuid();
        string Reparto(Guid linea) => $$"""{"motivo":"reparto","lineaIds":["{{linea}}"],"envioId":"{{envio}}"}""";
        var denegacionesAntes = _api.Denegaciones.Registros.Count;

        // Entrega: el encargado de Cusco, con una clave válida (el rechazo va antes de mirar la clave guardada y el estado) y sin ella.
        var sinClave = await _api.Enviar(HttpMethod.Post, $"/reservas/{recojo}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.EncargadoDeSucursal, cusco, json: Entrega(lineaDeRecojo));
        var conClave = await _api.Enviar(HttpMethod.Post, $"/reservas/{recojo}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.EncargadoDeSucursal, cusco, json: Entrega(lineaDeRecojo), clave: Clave());
        // Reparto: logística lo hace; el encargado, un cliente y la entrega de un envío a domicilio como si fuera recojo no pasan.
        var encargadoEnReparto = await _api.Enviar(HttpMethod.Post, $"/reservas/{domicilio}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.EncargadoDeSucursal, lima, json: Reparto(lineaDeDomicilio), clave: Clave());
        var clienteEnReparto = await _api.Enviar(HttpMethod.Post, $"/reservas/{domicilio}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: cliente, json: Reparto(lineaDeDomicilio), clave: Clave());
        var sinClaveEnReparto = await _api.Enviar(HttpMethod.Post, $"/reservas/{domicilio}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.EncargadoDeSucursal, lima, json: Reparto(lineaDeDomicilio));

        Assert.All([sinClave, conClave, encargadoEnReparto, clienteEnReparto, sinClaveEnReparto], r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        // SEL-39: el alcance va antes que la forma de la clave, así que sin clave el encargado de otra sucursal recibe el mismo 403, con su evento.
        Assert.Equal(denegacionesAntes + 5, _api.Denegaciones.Registros.Count);
        Assert.Equal((50, 5), await bd.StockDe(lima, libro));

        // Con la reserva ya cerrada por quien sí puede, el encargado de Cusco sigue recibiendo el mismo 403: ni «ya-cerrada» ni el resultado guardado.
        var clave = Clave();
        Assert.Equal(HttpStatusCode.OK, (await _api.Enviar(HttpMethod.Post, $"/reservas/{recojo}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.EncargadoDeSucursal, lima, json: Entrega(lineaDeRecojo), clave: clave)).StatusCode);
        var repetidaPorOtraSucursal = await _api.Enviar(HttpMethod.Post, $"/reservas/{recojo}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.EncargadoDeSucursal, cusco, json: Entrega(lineaDeRecojo), clave: clave);
        var textoDeLaRepetida = await repetidaPorOtraSucursal.Content.ReadAsStringAsync();
        var sobreUnaInexistente = await _api.Enviar(HttpMethod.Post, $"/reservas/{Guid.NewGuid()}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.EncargadoDeSucursal, cusco, json: Entrega(lineaDeRecojo), clave: clave);

        Assert.Equal(HttpStatusCode.Forbidden, repetidaPorOtraSucursal.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sobreUnaInexistente.StatusCode);
        Assert.DoesNotContain("ya-cerrada", textoDeLaRepetida, StringComparison.Ordinal);
        Assert.DoesNotContain(lineaDeRecojo.ToString(), textoDeLaRepetida, StringComparison.Ordinal);
        Assert.Equal((48, 3), await bd.StockDe(lima, libro));
    }

    // ---------- el cuerpo, el cliente y los topes ----------

    [Fact]
    public async Task UnPedidoDeOtroCliente_EsNoEncontradoAlCrearYAlConsultar_ConUnEventoYSinReservarNada()
    {
        var (lima, libro) = await Escenario();
        var duenio = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await Crear(duenio, ReservarJson(pedido, Guid.NewGuid(), libro, 2))).StatusCode);
        var denegacionesAntes = _api.Denegaciones.Registros.Count;

        var otro = Guid.NewGuid();
        var alCrear = await Crear(otro, ReservarJson(pedido, Guid.NewGuid(), libro, 5));
        var alConsultar = await _api.Enviar(HttpMethod.Get, $"/reservas/{pedido}", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: otro);
        var inexistente = await _api.Enviar(HttpMethod.Get, $"/reservas/{Guid.NewGuid()}", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: otro);

        Assert.Equal(HttpStatusCode.NotFound, alCrear.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, alConsultar.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(CodigosDeErrorDelInventario.NoEncontrado, (await Cuerpo(alCrear)).GetProperty("code").GetString());
        Assert.Equal(denegacionesAntes + 2, _api.Denegaciones.Registros.Count);
        Assert.Equal((50, 2), await bd.StockDe(lima, libro));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE pedido_id = '{pedido}'"));
    }

    [Fact]
    public async Task UnClienteIdOUnCampoDeMas_EnCrearLiberarCerrarPendientesYConsultaPrevia_SeRechaza_ConLaAutorizacionYaCumplida()
    {
        var (lima, libro) = await Escenario();
        var cliente = Guid.NewGuid();
        var (pedido, linea) = await Confirmada(cliente, libro);
        var extra = $$""","clienteId":"{{Guid.NewGuid()}}" """;

        var liberar = await _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/liberar", ApiDeReservasHost.CredencialDeExpiracion, json: $$"""{"motivo":"pedido-anulado"{{extra}}}""", clave: Clave());
        var cerrar = await _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.AdministradorCentral, json: $$"""{"motivo":"entrega","lineaIds":["{{linea}}"],"envioId":null{{extra}}}""", clave: Clave());
        var pendientes = await _api.Enviar(HttpMethod.Post, "/titulares/consultas-de-pendientes", ApiDeReservasHost.CredencialDePendientes, json: $$"""{"titularId":"{{cliente}}"{{extra}}}""");
        var previa = await _api.Enviar(HttpMethod.Post, "/reservas/consulta-previa", ApiDeReservasHost.CredencialDeDisponibilidad, json: $$"""{"tipoDeEntrega":"recojo","codigoDeSucursalDeRecojo":"lima","regionDeEntrega":null,"lineas":[{"lineaId":"{{Guid.NewGuid()}}","edicionId":"{{libro}}","cantidad":1}]{{extra}}}""");

        Assert.All([liberar, cerrar, pendientes, previa], r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal((50, 2), await bd.StockDe(lima, libro));
        Assert.Equal("confirmada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}'"));
    }

    [Fact]
    public async Task LosTopesDeEntrada_EnCerrarLiberarYConsultaPrevia_RechazanLoDesmedido_SinTocarNada()
    {
        var (lima, libro) = await Escenario();
        var cliente = Guid.NewGuid();
        var (pedido, _) = await Confirmada(cliente, libro);
        string Ids(int n) => string.Join(",", Enumerable.Range(0, n).Select(_ => $"\"{Guid.NewGuid()}\""));
        string Lineas(int n, int cantidad = 1) => string.Join(",", Enumerable.Range(0, n).Select(_ => $$"""{"lineaId":"{{Guid.NewGuid()}}","edicionId":"{{libro}}","cantidad":{{cantidad}}}"""));
        string Previa(string lineas) => $$"""{"tipoDeEntrega":"recojo","codigoDeSucursalDeRecojo":"lima","regionDeEntrega":null,"lineas":[{{lineas}}]}""";

        var cerrar = await _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.AdministradorCentral, json: $$"""{"motivo":"entrega","lineaIds":[{{Ids(51)}}],"envioId":null}""", clave: Clave());
        var liberar = await _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/liberar", ApiDeReservasHost.CredencialDeExpiracion, json: $$"""{"motivo":"pedido-anulado","lineaIds":[{{Ids(51)}}]}""", clave: Clave());
        var previaConMuchasLineas = await _api.Enviar(HttpMethod.Post, "/reservas/consulta-previa", ApiDeReservasHost.CredencialDeDisponibilidad, json: Previa(Lineas(51)));
        var previaConCantidadDesmedida = await _api.Enviar(HttpMethod.Post, "/reservas/consulta-previa", ApiDeReservasHost.CredencialDeDisponibilidad, json: Previa(Lineas(1, 100_000)));
        var previaSinLineas = await _api.Enviar(HttpMethod.Post, "/reservas/consulta-previa", ApiDeReservasHost.CredencialDeDisponibilidad, json: Previa(string.Empty));

        Assert.All([cerrar, liberar, previaConMuchasLineas, previaConCantidadDesmedida, previaSinLineas], r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal((50, 2), await bd.StockDe(lima, libro));
        Assert.Equal("confirmada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}'"));
    }

    // ---------- datos personales ----------

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task OperacionesIdempotentes_NoGuardaDatosPersonales_NiElClienteNiQuienCerro()
    {
        var (lima, libro) = await Escenario();
        var cliente = Guid.NewGuid();
        var encargado = Guid.NewGuid();
        var (pedido, linea) = await Confirmada(cliente, libro);
        var clave = Clave();
        var respuesta = await _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.EncargadoDeSucursal, lima, encargado, Entrega(linea), clave);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        var columnas = await bd.Consultar<string>("SELECT string_agg(column_name, ',') FROM information_schema.columns WHERE table_schema = 'reservas' AND table_name = 'operaciones_idempotentes'");
        var contenido = await bd.Consultar<string>("SELECT string_agg(row_to_json(o)::text, ' ') FROM reservas.operaciones_idempotentes o");

        Assert.NotNull(contenido);
        Assert.DoesNotContain("cliente", columnas!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("usuario", columnas!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(cliente.ToString(), contenido, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(encargado.ToString(), contenido, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(lima.ToString(), contenido, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task LaBaja_AuditaSoloElIdentificadorAnonimo_YNingunRegistroNiDenegacionGuardaElOriginalNiLaClave()
    {
        var (_, libro) = await Escenario();
        var titular = Guid.NewGuid();
        var (pedido, linea) = await Confirmada(titular, libro);
        var clave = Clave();
        await _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.EncargadoDeSucursal, Guid.NewGuid(), json: Entrega(linea), clave: clave);

        var baja = await _api.Enviar(HttpMethod.Post, "/bajas-de-titular", ApiDeReservasHost.CredencialDeBaja, json: $$"""{"titularId":"{{titular}}"}""");

        Assert.Equal(HttpStatusCode.NoContent, baja.StatusCode);
        var registro = Assert.Single(_api.Auditoria.Registros, r => r.Accion == "BajaDeTitularProcesada");
        Assert.NotEqual(titular.ToString(), registro.ObjetoId);
        var todo = string.Join(" ", _api.Auditoria.Registros.Concat(_api.Denegaciones.Registros).Select(r => $"{r.Accion}|{r.ObjetoId}|{r.Motivo}|{r.Origen}|{r.Ruta}|{r.Detalle.Texto}"));
        Assert.DoesNotContain(titular.ToString(), todo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(clave, todo, StringComparison.Ordinal);
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE cliente_id = '{titular}'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM plataforma.eventos_limite WHERE clave LIKE '%{titular}%'"));
    }

    // ---------- límite general ----------

    [Fact]
    [Trait("Criterio", "AC-SEL-20")]
    public async Task LaConsultaPrevia_SinToken_EstaSujetaAlLimiteGeneral_Responde429UniformeConRetryAfter_YNoReservaNada()
    {
        await _api.DisposeAsync();
        _api = await ApiDeReservasHost.Crear(bd, conTopeGeneral: true);
        var (lima, libro) = await Escenario();
        var json = $$"""{"tipoDeEntrega":"recojo","codigoDeSucursalDeRecojo":"lima","regionDeEntrega":null,"lineas":[{"lineaId":"{{Guid.NewGuid()}}","edicionId":"{{libro}}","cantidad":1}]}""";

        HttpResponseMessage? rechazada = null;
        var aceptadas = 0;
        for (var i = 0; i < 125 && rechazada is null; i++)
        {
            var respuesta = await _api.Enviar(HttpMethod.Post, "/reservas/consulta-previa", ApiDeReservasHost.CredencialDeDisponibilidad, json: json, origenReenviado: "203.0.113.7");
            if (respuesta.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rechazada = respuesta;
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
                aceptadas++;
            }
        }

        Assert.NotNull(rechazada);
        Assert.True(aceptadas >= 100, $"Se aceptaron {aceptadas}");
        var cuerpo = await Cuerpo(rechazada);
        Assert.Equal(CodigosDeErrorDelInventario.LimiteExcedido, cuerpo.GetProperty("code").GetString());
        Assert.NotEmpty(rechazada.Headers.GetValues("Retry-After").Single());
        Assert.Equal((50, 0), await bd.StockDe(lima, libro));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));
    }
}
