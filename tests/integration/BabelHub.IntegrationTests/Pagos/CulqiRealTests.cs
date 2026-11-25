using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using Xunit.Abstractions;
using MedioDelDominio = BabelHub.Ecommerce.Domain.Pagos.MedioDePago;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>Se omite salvo que la persona responsable haya puesto en su equipo las llaves de <b>integración</b> de Culqi (variables <c>BABELHUB_CULQI_PK_TEST</c> y <c>BABELHUB_CULQI_SK_TEST</c>): nunca se corre sola ni con llaves de producción.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HechoConLlavesDeIntegracionDeCulqiAttribute : FactAttribute
{
    public const string VariableLlavePublica = "BABELHUB_CULQI_PK_TEST";
    public const string VariableLlavePrivada = "BABELHUB_CULQI_SK_TEST";

    public HechoConLlavesDeIntegracionDeCulqiAttribute()
    {
        var publica = Environment.GetEnvironmentVariable(VariableLlavePublica);
        var privada = Environment.GetEnvironmentVariable(VariableLlavePrivada);
        if (string.IsNullOrWhiteSpace(publica) || string.IsNullOrWhiteSpace(privada))
        {
            Skip = $"Spike T38: define {VariableLlavePublica} y {VariableLlavePrivada} con las llaves de integración de Culqi (guía: specs/ecommerce-pagos/guia-del-spike-culqi.md).";
        }
        else if (!publica.StartsWith("pk_test_", StringComparison.Ordinal) || !privada.StartsWith("sk_test_", StringComparison.Ordinal))
        {
            Skip = "Solo se corre con llaves de integración (pk_test_… y sk_test_…): una llave de producción cobraría de verdad.";
        }
    }
}

/// <summary>
/// <b>Spike T38 — la prueba de humo contra el ambiente de integración de Culqi</b> (no corre sola: se omite sin las llaves de integración de la persona responsable). Ejercita <see cref="CulqiPasarelaDePago"/> contra Culqi de verdad con las tarjetas y el
/// celular de prueba que publica su documentación y <b>escribe en la salida de la prueba lo que observó</b> (formas de las respuestas, si Yape se reembolsa, la clave de idempotencia, los avisos) para pegarlo en <c>progress/log/</c> sin llaves ni tokens.
/// Ninguna llave, token ni dato de tarjeta se escribe en el repositorio ni en la salida. Los tokens se piden directo a la API porque aquí no hay navegador (en integración se admite, con tarjetas de prueba).
/// </summary>
public sealed class CulqiRealTests(ITestOutputHelper salida)
{
    private static readonly HttpClient Seguro = new() { BaseAddress = new Uri("https://secure.culqi.com/v2/"), Timeout = TimeSpan.FromSeconds(20) };

    private static string Publica => Environment.GetEnvironmentVariable(HechoConLlavesDeIntegracionDeCulqiAttribute.VariableLlavePublica)!;

    private static string Privada => Environment.GetEnvironmentVariable(HechoConLlavesDeIntegracionDeCulqiAttribute.VariableLlavePrivada)!;

    private sealed class RelojDeSistema : IReloj
    {
        public DateTimeOffset Ahora => DateTimeOffset.UtcNow;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private CulqiPasarelaDePago Pasarela() =>
        new(new HttpClient { BaseAddress = new Uri(OpcionesDeCulqi.UrlDeLaApi), Timeout = TimeSpan.FromSeconds(20) }, new OpcionesDeCulqi(new ValorSecreto(Privada)), new RelojDeSistema());

    private static async Task<string> TokenDeTarjeta(string numero, string mes, string anio, string cvv)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, "tokens") { Content = new StringContent(JsonSerializer.Serialize(new { card_number = numero, cvv, expiration_month = mes, expiration_year = anio, email = "revision@example.test" }), Encoding.UTF8, "application/json") };
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Publica);
        using var respuesta = await Seguro.SendAsync(pedido);
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.True(respuesta.IsSuccessStatusCode, $"El token de tarjeta no se creó ({(int)respuesta.StatusCode}).");
        return JsonDocument.Parse(texto).RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<string> TokenDeYape(long centimos)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, "tokens/yape") { Content = new StringContent(JsonSerializer.Serialize(new { number_phone = "900000001", otp = "123456", amount = centimos.ToString(System.Globalization.CultureInfo.InvariantCulture) }), Encoding.UTF8, "application/json") };
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Publica);
        using var respuesta = await Seguro.SendAsync(pedido);
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.True(respuesta.IsSuccessStatusCode, $"El token de Yape no se creó ({(int)respuesta.StatusCode}).");
        return JsonDocument.Parse(texto).RootElement.GetProperty("id").GetString()!;
    }

    private static SolicitudDeCobroEnPasarela Solicitud(string token, MedioDelDominio medio, decimal importe = 10m) =>
        new(Guid.NewGuid(), Importe.De(importe), medio, TokenDelMedioDePago.Crear(token), "revision@example.test");

    [HechoConLlavesDeIntegracionDeCulqi]
    public async Task Tarjeta_aprobada_se_consulta_por_la_referencia_y_se_reembolsa_en_dos_partes_sin_duplicar()
    {
        var pasarela = Pasarela();
        var solicitud = Solicitud(await TokenDeTarjeta("4111111111111111", "09", "2030", "123"), MedioDelDominio.Tarjeta);
        var inicio = DateTimeOffset.UtcNow;

        var cobro = await pasarela.Cobrar(solicitud);
        salida.WriteLine($"Cobrar (tarjeta aprobada): {cobro.Tipo}");
        Assert.Equal(TipoDeResultadoDeCobro.Aprobado, cobro.Tipo);
        Assert.Equal(TipoDeResultadoDeCobro.Aprobado, cobro.Validar(IntentoDeCobro.Iniciar(solicitud.ReferenciaOpaca, Guid.NewGuid(), MedioDelDominio.Tarjeta, solicitud.Importe, inicio)).Tipo);

        var consulta = await pasarela.ConsultarEstado(solicitud.ReferenciaOpaca, iniciadoEn: inicio);
        salida.WriteLine($"ConsultarEstado por la referencia opaca (lista por fechas): {consulta.Tipo}");
        Assert.Equal(TipoDeResultadoDeCobro.Aprobado, consulta.Tipo);
        Assert.Equal(cobro.ReferenciaDePasarela, consulta.ReferenciaDePasarela);

        var primero = Guid.NewGuid();
        var parcial = await pasarela.Reembolsar(primero, cobro.ReferenciaDePasarela!, Importe.De(4m));
        salida.WriteLine($"Reembolsar parcial S/ 4,00: {parcial.Tipo}");
        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.Aplicado, parcial.Tipo);

        var repetido = await pasarela.Reembolsar(primero, cobro.ReferenciaDePasarela!, Importe.De(4m));
        salida.WriteLine($"Repetir el mismo reembolso (misma marca): {repetido.Tipo}");
        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.Aplicado, repetido.Tipo);
        Assert.Equal(parcial.Referencia, repetido.Referencia);

        var existentes = await pasarela.ConsultarReembolsos(cobro.ReferenciaDePasarela!);
        salida.WriteLine($"ConsultarReembolsos tras el parcial: {(existentes is null ? "no se pudo" : existentes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))} (la marca se reconoce: {existentes?.Any(e => e.ReembolsoId == primero)})");
        Assert.NotNull(existentes);
        Assert.Contains(existentes, e => e.ReembolsoId == primero && e.Monto == Importe.De(4m));

        var resto = await pasarela.Reembolsar(Guid.NewGuid(), cobro.ReferenciaDePasarela!, Importe.De(6m));
        salida.WriteLine($"Reembolsar el resto S/ 6,00: {resto.Tipo}");
        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.Aplicado, resto.Tipo);

        var demas = await pasarela.Reembolsar(Guid.NewGuid(), cobro.ReferenciaDePasarela!, Importe.De(1m));
        salida.WriteLine($"Reembolsar de más (el cargo ya está devuelto): {demas.Tipo}");
        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.Rechazado, demas.Tipo);
    }

    [HechoConLlavesDeIntegracionDeCulqi]
    public async Task Tarjeta_sin_fondos_y_tarjeta_robada_se_rechazan_con_su_motivo()
    {
        var pasarela = Pasarela();

        var sinFondos = await pasarela.Cobrar(Solicitud(await TokenDeTarjeta("4000040000000008", "03", "2030", "295"), MedioDelDominio.Tarjeta));
        salida.WriteLine($"Cobrar (insufficient_funds): {sinFondos.Tipo} / {sinFondos.Motivo}");
        Assert.Equal((TipoDeResultadoDeCobro.Rechazado, MotivoDeRechazo.FondosInsuficientes), (sinFondos.Tipo, sinFondos.Motivo!.Value));

        var robada = await pasarela.Cobrar(Solicitud(await TokenDeTarjeta("4000020000000000", "10", "2030", "354"), MedioDelDominio.Tarjeta));
        salida.WriteLine($"Cobrar (stolen_card): {robada.Tipo} / {robada.Motivo}");
        Assert.Equal(TipoDeResultadoDeCobro.Rechazado, robada.Tipo);
    }

    [HechoConLlavesDeIntegracionDeCulqi]
    public async Task Yape_se_cobra_y_se_observa_si_Culqi_lo_reembolsa_por_la_API()
    {
        var pasarela = Pasarela();
        var solicitud = Solicitud(await TokenDeYape(1000), MedioDelDominio.Yape);
        var inicio = DateTimeOffset.UtcNow;

        var cobro = await pasarela.Cobrar(solicitud);
        salida.WriteLine($"Cobrar (Yape de prueba 900000001): {cobro.Tipo} / {cobro.Motivo}");
        Assert.Equal(TipoDeResultadoDeCobro.Aprobado, cobro.Tipo);
        var consulta = await pasarela.ConsultarEstado(solicitud.ReferenciaOpaca, iniciadoEn: inicio);
        salida.WriteLine($"ConsultarEstado (Yape): {consulta.Tipo}");
        Assert.Equal(TipoDeResultadoDeCobro.Aprobado, consulta.Tipo);

        // Lo que no se sabe (DD-98): no se afirma nada, solo se registra el resultado para decidir con datos.
        var reembolso = await pasarela.Reembolsar(Guid.NewGuid(), cobro.ReferenciaDePasarela!, Importe.De(10m));
        salida.WriteLine($"OBSERVACIÓN — reembolso de un pago con Yape por la API: {reembolso.Tipo}");
    }

    [HechoConLlavesDeIntegracionDeCulqi]
    public async Task Un_token_que_ya_se_uso_no_cobra_dos_veces()
    {
        var pasarela = Pasarela();
        var token = await TokenDeTarjeta("4111111111111111", "09", "2030", "123");

        var primero = await pasarela.Cobrar(Solicitud(token, MedioDelDominio.Tarjeta, 5m));
        var segundo = await pasarela.Cobrar(Solicitud(token, MedioDelDominio.Tarjeta, 5m));

        salida.WriteLine($"OBSERVACIÓN — mismo token dos veces: primero {primero.Tipo}, segundo {segundo.Tipo} / {segundo.Motivo}");
        Assert.Equal(TipoDeResultadoDeCobro.Aprobado, primero.Tipo);
        Assert.NotEqual(TipoDeResultadoDeCobro.Aprobado, segundo.Tipo);
    }
}
