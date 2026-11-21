using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using BabelHub.IntegrationTests.Pedidos;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// Los dispositivos reconocidos con el host real del E-commerce sobre PostgreSQL (IDE-53, IDE-54, IDE-55, IDE-76; AC-IDE-47, AC-IDE-48): el ingreso completo emite la marca (la web la guarda en su cookie), el dispositivo solo cuenta como
/// reconocido pasadas 24 horas y con la región habitual, y el cambio de correo desde un dispositivo reconocido sigue el camino directo mientras que desde otro queda pendiente de confirmación. Reloj de prueba movible; nada de la web: es
/// lo que Identidad hace con la marca que la web le entrega al completar el ingreso.
/// </summary>
[Collection(ColeccionDelEcommerceCompleto.Nombre)]
public sealed class DispositivosReconocidosConElHostRealTests(PostgresDelEcommerceCompletoFixture bd)
{
    private const string Frase = "una frase larga y segura para el cambio de correo";

    private sealed class RelojMovil(DateTimeOffset inicio) : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = inicio;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private static readonly DateTimeOffset Inicio = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);

    private async Task<Guid> SembrarCliente(AnfitrionRealDelEcommerce anfitrion, string correo)
    {
        var id = Guid.NewGuid();
        var resumen = await anfitrion.Como(ActorActual.DelSistema(), async sp =>
        {
            var (contrasena, _) = Contrasena.Crear(Frase, _ => false);
            return await sp.GetRequiredService<IHasherDeContrasenas>().Resumir(contrasena!);
        });
        await bd.Ejecutar(
            $"""
            INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, hash_contrasena, creada_en)
              VALUES ('{id}', 'Ana Pérez', '{correo}', true, 'cliente', true, '{resumen}', DATE '2026-10-01');
            """);
        return id;
    }

    private static ActorActual Titular(Guid id) => ActorActual.DeUsuario(id, BabelHub.Shared.Contratos.RolesDeUsuario.Cliente, null);

    private static ValorSecreto S(string valor) => new(valor);

    private async Task<string?> EstadoDelCambio(Guid id) =>
        await bd.Consultar<string>($"SELECT estado FROM identidad.cambios_de_correo_pendientes WHERE usuario_id = '{id}'");

    [Fact]
    [Trait("Criterio", "AC-IDE-47")]
    public async Task El_ingreso_completo_emite_una_marca_una_sola_vez_y_el_dispositivo_se_reconoce_a_las_24_horas_en_su_region()
    {
        var reloj = new RelojMovil(Inicio);
        await using var anfitrion = bd.Anfitrion(s => s.AddSingleton<IReloj>(reloj));
        var cuenta = await SembrarCliente(anfitrion, $"d{Guid.NewGuid():N}@example.test");

        var primera = await anfitrion.Como(Titular(cuenta), sp => sp.GetRequiredService<IDispositivosDeLaCuenta>().RegistrarElIngresoCompleto(cuenta, null, "PE"));
        var marca = primera.Valor.MarcaNueva!.Revelar();
        var repetida = await anfitrion.Como(Titular(cuenta), sp => sp.GetRequiredService<IDispositivosDeLaCuenta>().RegistrarElIngresoCompleto(cuenta, S(marca), "PE"));
        async Task<bool> Reconoce(string? presentada, string? region) =>
            (await anfitrion.Como(Titular(cuenta), sp => sp.GetRequiredService<IDispositivosDeLaCuenta>().Reconoce(cuenta, presentada is null ? null : S(presentada), region))).Valor;

        Assert.True(primera.EsExito, primera.Error?.Clave);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", marca);
        Assert.Null(repetida.Valor.MarcaNueva);
        Assert.Equal(1, await bd.Consultar<long>($"SELECT count(*) FROM identidad.dispositivos WHERE usuario_id = '{cuenta}'"));
        Assert.DoesNotContain(marca, await bd.Consultar<string>($"SELECT huella_marca FROM identidad.dispositivos WHERE usuario_id = '{cuenta}'") ?? string.Empty, StringComparison.Ordinal);

        // Recién emitido no cuenta: hacen falta 24 horas (IDE-55).
        Assert.False(await Reconoce(marca, "PE"));
        reloj.Ahora = Inicio.AddHours(23);
        Assert.False(await Reconoce(marca, "PE"));
        reloj.Ahora = Inicio.AddHours(25);
        Assert.True(await Reconoce(marca, "PE"));
        Assert.False(await Reconoce(marca, "US"));
        Assert.False(await Reconoce(S("otra-marca").Revelar() + new string('x', 33), "PE"));
        Assert.False(await Reconoce(null, "PE"));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-47")]
    public async Task El_cambio_de_correo_desde_el_dispositivo_reconocido_sigue_el_camino_directo_y_desde_otro_queda_pendiente_de_confirmacion()
    {
        var reloj = new RelojMovil(Inicio);
        await using var anfitrion = bd.Anfitrion(s => s.AddSingleton<IReloj>(reloj));
        var reconocido = await SembrarCliente(anfitrion, $"r{Guid.NewGuid():N}@example.test");
        var desconocido = await SembrarCliente(anfitrion, $"u{Guid.NewGuid():N}@example.test");
        var marca = (await anfitrion.Como(Titular(reconocido), sp => sp.GetRequiredService<IDispositivosDeLaCuenta>().RegistrarElIngresoCompleto(reconocido, null, "PE"))).Valor.MarcaNueva!.Revelar();
        await anfitrion.Como(Titular(desconocido), sp => sp.GetRequiredService<IDispositivosDeLaCuenta>().RegistrarElIngresoCompleto(desconocido, null, "PE"));
        reloj.Ahora = Inicio.AddHours(25);

        var directo = await anfitrion.Como(Titular(reconocido), sp => sp.GetRequiredService<ICambioDeCorreo>().Solicitar(reconocido, $"nuevo-r{Guid.NewGuid():N}@example.test", S(Frase), S(marca), "PE"));
        var sinMarca = await anfitrion.Como(Titular(desconocido), sp => sp.GetRequiredService<ICambioDeCorreo>().Solicitar(desconocido, $"nuevo-u{Guid.NewGuid():N}@example.test", S(Frase), null, "PE"));

        Assert.True(directo.EsExito, directo.Error?.Clave);
        Assert.True(sinMarca.EsExito, sinMarca.Error?.Clave);
        Assert.Equal("esperando-verificacion", await EstadoDelCambio(reconocido));
        Assert.Equal("pendiente-de-confirmacion", await EstadoDelCambio(desconocido));
    }
}
