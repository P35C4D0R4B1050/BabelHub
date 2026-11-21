using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Envios;
using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Envios;
using BabelHub.IntegrationTests.Pedidos;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.IntegrationTests.Envios;

/// <summary>
/// ENV-06 (AC-ENV-08) con el <b>Notificador real</b> y el Pedidos real: la guía generada deja el aviso al cliente guardado como pendiente en la misma transacción (su envío y su reintento son del Notificador), y si el Notificador no puede guardarlo
/// el cambio de estado se conserva (una falla del Notificador nunca interrumpe el caso de uso). Solo lo externo es doble: el Inventario (reservas) y el operador (simulador).
/// </summary>
[Collection(ColeccionDelEcommerceCompleto.Nombre)]
public sealed class EnviosConAvisosRealesTests(PostgresDelEcommerceCompletoFixture bd) : IAsyncLifetime
{
    // Cada simulador numera sus guías desde cero y el número es único por almacén: se parte de un almacén de Envíos vacío.
    public Task InitializeAsync() => bd.Ejecutar("TRUNCATE envios.envio_lineas, envios.envios CASCADE;");

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly Guid Logistica = Guid.Parse("77777777-7777-7777-7777-777777777701");

    private sealed class ReservasDelEnvioDeCorrido : IReservasDelEnvio
    {
        public Task<ResultadoDeCierreDeReservas> Cerrar(Guid pedidoId, Guid envioId, IReadOnlyCollection<Guid> lineaIds, CancellationToken cancelacion = default) =>
            Task.FromResult<ResultadoDeCierreDeReservas>(new ResultadoDeCierreDeReservas.Cerrado());
    }

    private sealed class NotificadorQueFalla : INotificador
    {
        public Task<ResultadoDeLaSolicitudDeAviso> Solicitar(SolicitudDeAviso solicitud, CancellationToken cancelacion = default) => throw new InvalidOperationException("Notificador caído.");
    }

    private AnfitrionRealDelEcommerce Anfitrion(bool notificadorCaido = false) => bd.Anfitrion(servicios =>
    {
        servicios.RemoveAll<IReservasDelPedido>();
        servicios.AddSingleton<IReservasDelPedido>(new ReservasFalsas());
        servicios.RemoveAll<IReservasDelEnvio>();
        servicios.AddSingleton<IReservasDelEnvio>(new ReservasDelEnvioDeCorrido());
        servicios.RemoveAll<IOperadorLogistico>();
        servicios.AddSingleton<IOperadorLogistico>(new SimuladorOperadorLogistico());
        if (notificadorCaido)
        {
            servicios.RemoveAll<INotificador>();
            servicios.AddScoped<INotificador, NotificadorQueFalla>();
        }
    });

    private static ActorActual DeLogistica() => ActorActual.DeUsuario(Logistica, RolesDeUsuario.PersonalDeLogistica, null);

    private async Task<(Guid Pedido, Guid Cuenta)> SembrarUnPedidoListoParaGuia(AnfitrionRealDelEcommerce anfitrion)
    {
        var cuenta = Guid.NewGuid();
        await bd.Ejecutar(
            $"""
            INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso) VALUES (1, 'v1', 'v1', 'v1')
              ON CONFLICT (id) DO UPDATE SET terminos = 'v1', privacidad = 'v1', reembolso = 'v1';
            INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, creada_en, acepto_terminos_version, acepto_terminos_en, acepto_privacidad_version, acepto_privacidad_en, acepto_reembolso_version, acepto_reembolso_en)
              VALUES ('{cuenta}', 'Ana Pérez', 'e{cuenta:N}@example.test', true, 'cliente', true, now(), 'v1', now(), 'v1', now(), 'v1', now());
            UPDATE identidad.usuarios SET numero_whatsapp = '+51999111222', consentimiento_whatsapp = true WHERE id = '{cuenta}';
            INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, creada_en, acepto_terminos_version, acepto_terminos_en, acepto_privacidad_version, acepto_privacidad_en, acepto_reembolso_version, acepto_reembolso_en)
              VALUES ('{Logistica}', 'Logística de prueba', 'logistica-env@example.test', true, 'personal-de-logistica', true, now(), 'v1', now(), 'v1', now(), 'v1', now())
              ON CONFLICT DO NOTHING;
            """);
        var ahora = DateTimeOffset.UtcNow;
        var pedido = Guid.NewGuid();
        var lineas = new List<LineaDeCompraAprobada> { new(Guid.NewGuid(), Guid.NewGuid(), false, "Ficciones", null, 1, 50m, 50m, "lima", false, null, null) };
        var aceptaciones = new List<AceptacionDeCompra> { new(TextoLegalDeCompra.Terminos, "v1", ahora), new(TextoLegalDeCompra.Privacidad, "v1", ahora) };
        var compra = new CompraAprobada(pedido, cuenta, TipoDeEntregaDeCompra.Domicilio, null, "ayacucho", 12m, 62m, new ContactoDeLaCompra("Rosa Quispe Ficticia", "987654321", "Jr. Ficticio 123, Ayacucho", "ayacucho"), aceptaciones, lineas);
        Assert.Equal(ResultadoDeConfirmarCompra.Creado, await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<IConfirmacionDeCompra>().Confirmar(compra)));
        var ids = new List<Guid> { lineas[0].LineaId };
        Assert.True((await anfitrion.Como(DeLogistica(), sp => sp.GetRequiredService<IGestionDeDespacho>().Preparar(new OperarLineasEntrada(pedido, ids)))).EsExito);
        Assert.True((await anfitrion.Como(DeLogistica(), sp => sp.GetRequiredService<IGestionDeDespacho>().MarcarListas(new OperarLineasEntrada(pedido, ids)))).EsExito);
        return (pedido, cuenta);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task La_guia_generada_deja_el_aviso_al_cliente_pendiente_en_el_Notificador_real_en_la_misma_transaccion()
    {
        await using var anfitrion = Anfitrion();
        var (pedido, _) = await SembrarUnPedidoListoParaGuia(anfitrion);
        var envio = await bd.Consultar<Guid>("SELECT id FROM envios.envios WHERE pedido_id = @p", ("p", pedido));

        var guia = await anfitrion.Como(DeLogistica(), sp => sp.GetRequiredService<IEnviosDeLogistica>().SolicitarGuia(envio));

        Assert.True(guia.EsExito, guia.Error?.Clave);
        Assert.Equal("guia_generada", await bd.Consultar<string>("SELECT estado FROM envios.envios WHERE id = @e", ("e", envio)));
        var estados = await bd.Consultar<string>("SELECT string_agg(evento_id || '=' || estado, '; ') FROM notificaciones.avisos WHERE evento_id LIKE @e", ("e", $"%{envio:D}%"));
        Assert.True(await bd.Consultar<long>("SELECT count(*) FROM notificaciones.avisos WHERE evento_id LIKE @e AND tipo_destinatario = 'cliente' AND estado = 'pendiente'", ("e", $"%{envio:D}%")) >= 1, $"Avisos del envío: {(estados ?? "ninguno")}");
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Si_el_Notificador_no_puede_guardar_el_aviso_la_guia_se_conserva_y_el_envio_no_se_interrumpe()
    {
        await using var anfitrion = Anfitrion(notificadorCaido: true);
        var (pedido, _) = await SembrarUnPedidoListoParaGuia(anfitrion);
        var envio = await bd.Consultar<Guid>("SELECT id FROM envios.envios WHERE pedido_id = @p", ("p", pedido));

        var guia = await anfitrion.Como(DeLogistica(), sp => sp.GetRequiredService<IEnviosDeLogistica>().SolicitarGuia(envio));

        Assert.True(guia.EsExito, guia.Error?.Clave);
        Assert.Equal("guia_generada", await bd.Consultar<string>("SELECT estado FROM envios.envios WHERE id = @e", ("e", envio)));
        Assert.NotNull(await bd.Consultar<string>("SELECT numero_de_guia FROM envios.envios WHERE id = @e", ("e", envio)));
    }
}
