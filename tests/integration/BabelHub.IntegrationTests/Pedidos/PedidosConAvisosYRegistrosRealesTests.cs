using System.Collections.Concurrent;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>
/// Pedidos con los módulos reales (ECO-6g, ronda 3; T33 a T36): el aviso que queda pendiente en el Notificador real (PED-05, PED-06), el filtro «en tránsito» de la administración con un pedido que mezcla un digital y un
/// impreso en traslado (PED-14, AC-PED-12) y el contacto que no llega a los registros técnicos (PED-40, AC-PED-34) sobre PostgreSQL con todos los esquemas migrados. Solo lo externo es doble: el Inventario (reservas) y el
/// reembolso de Pagos.
/// </summary>
[Collection(ColeccionDelEcommerceCompleto.Nombre)]
public sealed class PedidosConAvisosYRegistrosRealesTests(PostgresDelEcommerceCompletoFixture bd)
{
    private const string NombreDelContacto = "Rosa Quispe Ficticia";

    private const string TelefonoDelContacto = "987654321";

    private const string DireccionDelContacto = "Jr. Ficticio 123, Ayacucho";

    private static readonly Guid Administrador = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private sealed class CapturaDeRegistros : ILoggerProvider
    {
        public ConcurrentQueue<string> Mensajes { get; } = new();

        public ILogger CreateLogger(string categoria) => new Registro(Mensajes);

        public void Dispose()
        {
        }

        private sealed class Registro(ConcurrentQueue<string> mensajes) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                mensajes.Enqueue(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
        }
    }

    private readonly ReservasFalsas _reservas = new();

    private readonly ReembolsosFalsos _reembolsos = new();

    private AnfitrionRealDelEcommerce Anfitrion(CapturaDeRegistros? captura = null) => bd.Anfitrion(servicios =>
    {
        servicios.RemoveAll<IReservasDelPedido>();
        servicios.AddSingleton<IReservasDelPedido>(_reservas);
        servicios.RemoveAll<IReembolsos>();
        servicios.AddSingleton<IReembolsos>(_reembolsos);
        if (captura is not null)
        {
            servicios.AddLogging(l => l.SetMinimumLevel(LogLevel.Trace).AddProvider(captura));
        }
    });

    private static ActorActual DelAdministrador() => ActorActual.DeUsuario(Administrador, RolesDeUsuario.AdministradorCentral, null);

    private async Task<Guid> SembrarCuenta()
    {
        var id = Guid.NewGuid();
        await bd.Ejecutar(
            $"""
            INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso) VALUES (1, 'v1', 'v1', 'v1')
              ON CONFLICT (id) DO UPDATE SET terminos = 'v1', privacidad = 'v1', reembolso = 'v1';
            INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, creada_en, acepto_terminos_version, acepto_terminos_en, acepto_privacidad_version, acepto_privacidad_en, acepto_reembolso_version, acepto_reembolso_en)
              VALUES ('{id}', 'Ana Pérez', 'q{id:N}@example.test', true, 'cliente', true, now(), 'v1', now(), 'v1', now(), 'v1', now());
            """);
        return id;
    }

    private static CompraAprobada Compra(Guid pedido, Guid cuenta, Guid edicionImpresa, Guid? traslado, bool conDigital)
    {
        var ahora = DateTimeOffset.UtcNow;
        var lineas = new List<LineaDeCompraAprobada>
        {
            new(Guid.NewGuid(), edicionImpresa, false, "Ficciones", null, 1, 50m, 50m, "lima", traslado is not null, traslado, traslado is null ? null : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3))),
        };
        var aceptaciones = new List<AceptacionDeCompra> { new(TextoLegalDeCompra.Terminos, "v1", ahora), new(TextoLegalDeCompra.Privacidad, "v1", ahora) };
        if (conDigital)
        {
            lineas.Add(new LineaDeCompraAprobada(Guid.NewGuid(), Guid.Parse("b0000000-0000-0000-0000-0000000000d1"), true, "El Aleph (digital)", null, 1, 30m, 30m, null, false, null, null));
            aceptaciones.Add(new AceptacionDeCompra(TextoLegalDeCompra.ReembolsoDigital, "v1", ahora));
        }

        var total = lineas.Sum(l => l.Importe) + 12m;
        return new CompraAprobada(pedido, cuenta, TipoDeEntregaDeCompra.Domicilio, null, "ayacucho", 12m, total, new ContactoDeLaCompra(NombreDelContacto, TelefonoDelContacto, DireccionDelContacto, "ayacucho"), aceptaciones, lineas);
    }

    private async Task PrepararLaEdicionDigital() =>
        await bd.Ejecutar(
            """
            BEGIN;
            INSERT INTO catalogo.obras (id, titulo, autor, editorial, categoria, portada_clave, portada_huella, portada_tipo, estado, version, creada_en)
              VALUES ('a0000000-0000-0000-0000-0000000000d1', 'El Aleph (prueba)', 'Autor de prueba', 'Editorial de prueba', 'Narrativa', 'e2e0000000000000000000000000d1d1', decode(repeat('d1', 32), 'hex'), 'jpeg', 'activa', 1, now())
              ON CONFLICT DO NOTHING;
            INSERT INTO catalogo.ediciones (id, obra_id, tipo, isbn13, isbn_ingresado, precio, estado)
              VALUES ('b0000000-0000-0000-0000-0000000000d1', 'a0000000-0000-0000-0000-0000000000d1', 'digital', '9786123450021', '9786123450021', 30.00, 'activa')
              ON CONFLICT DO NOTHING;
            INSERT INTO catalogo.licencias (edicion_id, tipo, titular, territorios, vigente_hasta, formato)
              VALUES ('b0000000-0000-0000-0000-0000000000d1', 'dominio_publico', 'Titular de prueba', ARRAY['PE'], NULL, 'epub')
              ON CONFLICT DO NOTHING;
            INSERT INTO catalogo.archivos_digitales (edicion_id, clave_en_almacen, tipo_detectado, tamano, huella_sha256, subido_en, subido_por)
              VALUES ('b0000000-0000-0000-0000-0000000000d1', 'f2e000000000000000000000000000d1', 'epub', 1024, decode(repeat('d2', 32), 'hex'), now(), '00000000-0000-0000-0000-000000000000')
              ON CONFLICT DO NOTHING;
            COMMIT;
            """);

    [Fact]
    [Trait("Criterio", "AC-PED-12")]
    public async Task Un_pedido_con_un_digital_disponible_y_un_impreso_en_transito_figura_en_el_filtro_en_transito_de_la_administracion()
    {
        await PrepararLaEdicionDigital();
        await using var anfitrion = Anfitrion();
        var cuenta = await SembrarCuenta();
        var edicion = Guid.NewGuid();
        var traslado = Guid.NewGuid();
        var enTransito = Guid.NewGuid();
        var confirmado = Guid.NewGuid();
        Assert.Equal(ResultadoDeConfirmarCompra.Creado, await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<IConfirmacionDeCompra>().Confirmar(Compra(enTransito, cuenta, edicion, traslado, conDigital: true))));
        Assert.Equal(ResultadoDeConfirmarCompra.Creado, await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<IConfirmacionDeCompra>().Confirmar(Compra(confirmado, cuenta, Guid.NewGuid(), null, conDigital: false))));

        // El Inventario informa que el traslado salió: la línea impresa pasa a en tránsito y el digital sigue disponible.
        var recibido = await anfitrion.Como(
            ActorActual.DeIntegracion(IntegracionesDeServicio.Inventario),
            sp => sp.GetRequiredService<IReceptorDeEstadosDeTraslado>().Recibir(new EstadoDeTrasladoRecibido($"traslado:{traslado:D}:en-transito", traslado, enTransito, edicion, 1, EstadoDelTrasladoInformado.EnTransito, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)))));
        Assert.True(recibido.EsExito, recibido.Error?.Clave);

        var enTransitoVisibles = await anfitrion.Como(DelAdministrador(), sp => sp.GetRequiredService<IAdministracionDePedidos>().Listar(new FiltroDePedidos(EstadoVisibleDelPedido.EnTransito)));
        var confirmadosVisibles = await anfitrion.Como(DelAdministrador(), sp => sp.GetRequiredService<IAdministracionDePedidos>().Listar(new FiltroDePedidos(EstadoVisibleDelPedido.Confirmado)));

        Assert.True(enTransitoVisibles.EsExito, enTransitoVisibles.Error?.Clave);
        Assert.Contains(enTransitoVisibles.Valor.Pedidos, p => p.PedidoId == enTransito);
        Assert.DoesNotContain(enTransitoVisibles.Valor.Pedidos, p => p.PedidoId == confirmado);
        Assert.Contains(confirmadosVisibles.Valor.Pedidos, p => p.PedidoId == confirmado);
        Assert.DoesNotContain(confirmadosVisibles.Valor.Pedidos, p => p.PedidoId == enTransito);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-04")]
    public async Task Un_traslado_que_mueve_una_linea_deja_el_aviso_pendiente_en_el_Notificador_real_sin_cambiar_el_estado_del_pedido()
    {
        await PrepararLaEdicionDigital();
        await using var anfitrion = Anfitrion();
        var cuenta = await SembrarCuenta();
        var edicion = Guid.NewGuid();
        var traslado = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        Assert.Equal(ResultadoDeConfirmarCompra.Creado, await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<IConfirmacionDeCompra>().Confirmar(Compra(pedido, cuenta, edicion, traslado, conDigital: false))));

        var recibido = await anfitrion.Como(
            ActorActual.DeIntegracion(IntegracionesDeServicio.Inventario),
            sp => sp.GetRequiredService<IReceptorDeEstadosDeTraslado>().Recibir(new EstadoDeTrasladoRecibido($"traslado:{traslado:D}:en-transito", traslado, pedido, edicion, 1, EstadoDelTrasladoInformado.EnTransito, null)));

        // PED-05 y PED-06: la operación se completó, el estado se conserva y el aviso queda guardado como pendiente de envío (nadie lo ha enviado): su reintento es del Notificador.
        Assert.True(recibido.EsExito, recibido.Error?.Clave);
        Assert.Equal("en_transito", await bd.Consultar<string>("SELECT estado FROM pedidos.lineas_de_pedido WHERE pedido_id = @p AND NOT es_digital", ("p", pedido)));
        Assert.True(await bd.Consultar<int>("SELECT count(*) FROM notificaciones.avisos WHERE evento_id LIKE @e AND estado = 'pendiente'", ("e", $"%{traslado:D}%".Replace(':', '-'))) >= 1);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-34")]
    public async Task El_contacto_de_un_pedido_a_domicilio_no_llega_a_los_registros_tecnicos_en_su_creacion_su_anulacion_ni_su_lectura()
    {
        var captura = new CapturaDeRegistros();
        await PrepararLaEdicionDigital();
        await using var anfitrion = Anfitrion(captura);
        var cuenta = await SembrarCuenta();
        var pedido = Guid.NewGuid();
        Assert.Equal(ResultadoDeConfirmarCompra.Creado, await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<IConfirmacionDeCompra>().Confirmar(Compra(pedido, cuenta, Guid.NewGuid(), null, conDigital: true))));
        var detalle = await anfitrion.Como(DelAdministrador(), sp => sp.GetRequiredService<IAdministracionDePedidos>().Obtener(pedido));
        var anulado = await anfitrion.Como(DelAdministrador(), sp => sp.GetRequiredService<IAdministracionDePedidos>().Anular(new AnularPedidoEntrada(pedido, "otro")));

        Assert.True(detalle.EsExito, detalle.Error?.Clave);
        Assert.True(anulado.EsExito, anulado.Error?.Clave);
        // La captura está conectada al anfitrión real: un mensaje de control llega y, con todo lo ejecutado, ninguno lleva el contacto.
        anfitrion.Servicios.GetRequiredService<ILoggerFactory>().CreateLogger("control").LogError(new InvalidOperationException("falla de control"), "mensaje de control");
        Assert.Contains(captura.Mensajes, m => m.Contains("mensaje de control", StringComparison.Ordinal));
        Assert.DoesNotContain(captura.Mensajes, m => m.Contains(NombreDelContacto, StringComparison.Ordinal) || m.Contains(TelefonoDelContacto, StringComparison.Ordinal) || m.Contains(DireccionDelContacto, StringComparison.Ordinal));
    }
}
