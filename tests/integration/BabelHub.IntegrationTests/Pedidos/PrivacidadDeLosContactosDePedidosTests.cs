using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Shared.Errores;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>
/// La privacidad del contacto de entrega con la Aplicación real sobre PostgreSQL real (T35; PED-33, PED-34, PED-37, PED-40; AC-PED-26, AC-PED-28, AC-PED-29, AC-PED-31, AC-PED-33): la depuración a los dos años por fecha
/// de Lima con reloj controlado, repetible y atómica con su auditoría (sin poder auditar no se anonimiza nada); la anonimización por cancelación de la cuenta, de todos sus pedidos y de ninguno ajeno, revertida entera si
/// falla; y que el contacto no aparece en los avisos ni en el detalle de la auditoría. Pagos real y la persistencia real de la auditoría se prueban en sus módulos: aquí son dobles.
/// </summary>
[Collection(ColeccionPostgresDePedidos.Nombre)]
public sealed class PrivacidadDeLosContactosDePedidosTests(PostgresDePedidosFixture bd) : IAsyncLifetime
{
    private const string Nombre = "Ana Pérez";

    private const string Telefono = "987654321";

    private const string Direccion = "Jr. Lima 123";

    private static readonly DateTimeOffset Hace2AnosYUnPoco = new(2024, 11, 10, 15, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Hoy = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

    private static int _contador;

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Numero() => "PQRS2345" + "ABCDEFGHJKLMNPQRSTUVWXYZ"[Interlocked.Increment(ref _contador) % 24] + "P";

    private async Task<Pedido> SembrarDomicilio(Guid cuenta, DateTimeOffset creado)
    {
        var linea = new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Ficciones", null, 1, 50m, 50m, "LIM", false, null, null);
        var contacto = BabelHub.Ecommerce.Domain.Pedidos.ContactoDeEntrega.Crear(Nombre, Telefono, Direccion);
        var pedido = Pedido.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), Numero(), cuenta, TipoDeEntregaDelPedido.Domicilio, null, "ayacucho", 12m, 62m, contacto, new("t-1", creado), new("p-1", creado), null, [linea], creado)).Pedido!;
        Assert.True(await bd.Pedidos.Agregar(pedido));
        return pedido;
    }

    /// <summary>Un pedido a domicilio anulado en <paramref name="cuando"/>: la anulación fija la fecha del estado final desde la que corre la retención.</summary>
    private async Task<Pedido> SembrarAnulado(EscenarioDePedidos escenario, DateTimeOffset cuando, Guid? cuenta = null)
    {
        var pedido = await SembrarDomicilio(cuenta ?? EscenarioDePedidos.Titular, cuando);
        escenario.Reloj.Ahora = cuando;
        var resultado = await escenario.Como(EscenarioDePedidos.DeLaAdministracion(), sp => sp.GetRequiredService<IAdministracionDePedidos>().Anular(new AnularPedidoEntrada(pedido.Id, "otro")));
        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        return pedido;
    }

    private async Task<bool> ConContacto(Guid pedidoId) => (await bd.Pedidos.Obtener(pedidoId, conBloqueo: false))!.Contacto is not null;

    private static Task<Resultado<ResultadoDeLaDepuracion>> Depurar(EscenarioDePedidos escenario) =>
        escenario.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<AnonimizarContactosVencidos>().Depurar());

    [Fact]
    [Trait("Criterio", "AC-PED-29")]
    [Trait("Criterio", "AC-PED-33")]
    public async Task La_depuracion_anonimiza_solo_los_pedidos_de_mas_de_dos_anos_se_audita_con_el_conteo_y_repetirla_no_cambia_nada()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var viejo = await SembrarAnulado(escenario, Hace2AnosYUnPoco);
        var reciente = await SembrarAnulado(escenario, Hoy.AddDays(-1));
        var vivo = await SembrarDomicilio(EscenarioDePedidos.Titular, Hoy.AddDays(-30));
        escenario.Reloj.Ahora = Hoy;

        var primera = await Depurar(escenario);
        var segunda = await Depurar(escenario);

        Assert.True(primera.EsExito, primera.Error?.Clave);
        Assert.Equal(1, primera.Valor.Anonimizados);
        Assert.Equal(0, segunda.Valor.Anonimizados);
        Assert.False(await ConContacto(viejo.Id));
        Assert.True(await ConContacto(reciente.Id));
        Assert.True(await ConContacto(vivo.Id));
        // Lo demás del pedido se conserva (total, líneas, cuenta opaca) y la auditoría de la corrida lleva solo el conteo.
        var conservado = (await bd.Pedidos.Obtener(viejo.Id, conBloqueo: false))!;
        Assert.Equal((viejo.Total, viejo.Lineas.Count, viejo.CuentaId), (conservado.Total, conservado.Lineas.Count, conservado.CuentaId));
        Assert.Contains(escenario.Auditoria.Solicitudes, s => s.Accion.Identificador.Contains("pedidos-depurados", StringComparison.OrdinalIgnoreCase) || s.Detalle.ContainsKey("cantidad"));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-29")]
    public async Task Sin_poder_auditar_la_depuracion_no_anonimiza_nada_y_otro_actor_que_no_es_sistema_no_puede_correrla()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var viejo = await SembrarAnulado(escenario, Hace2AnosYUnPoco);
        escenario.Reloj.Ahora = Hoy;
        escenario.Auditoria.Caida = true;

        var sinAuditoria = await Depurar(escenario);
        escenario.Auditoria.Caida = false;
        var comoAdministrador = await escenario.Como(EscenarioDePedidos.DeLaAdministracion(), sp => sp.GetRequiredService<AnonimizarContactosVencidos>().Depurar());

        Assert.False(sinAuditoria.EsExito);
        Assert.False(comoAdministrador.EsExito);
        Assert.True(await ConContacto(viejo.Id));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-28")]
    [Trait("Criterio", "AC-PED-31")]
    public async Task Cancelar_la_cuenta_anonimiza_todos_sus_pedidos_y_ninguno_ajeno_y_una_falla_lo_revierte_todo()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var otraCuenta = Guid.NewGuid();
        var a = await SembrarDomicilio(EscenarioDePedidos.Titular, Hoy);
        var b = await SembrarDomicilio(EscenarioDePedidos.Titular, Hoy);
        var ajeno = await SembrarDomicilio(otraCuenta, Hoy);
        var aplicador = escenario.Resolver<IEnumerable<IAplicadorDeCancelacion>>().Single(x => x.Modulo == "pedidos");

        // Una falla a mitad de la cancelación (la auditoría cae) revierte la transacción: ningún pedido queda anonimizado a medias.
        escenario.Auditoria.Caida = true;
        await Assert.ThrowsAnyAsync<Exception>(() => bd.Unidad.Ejecutar(async token =>
        {
            await aplicador.Aplicar(EscenarioDePedidos.Titular, Guid.NewGuid(), token);
            return true;
        }));
        Assert.True(await ConContacto(a.Id));
        Assert.True(await ConContacto(b.Id));

        // Sin la falla, la cancelación anonimiza los de la cuenta y respeta al ajeno; reaplicarla no cambia nada.
        escenario.Auditoria.Caida = false;
        await bd.Unidad.Ejecutar(async token =>
        {
            await aplicador.Aplicar(EscenarioDePedidos.Titular, Guid.NewGuid(), token);
            return true;
        });
        await bd.Unidad.Ejecutar(async token =>
        {
            await aplicador.Aplicar(EscenarioDePedidos.Titular, Guid.NewGuid(), token);
            return true;
        });

        Assert.False(await ConContacto(a.Id));
        Assert.False(await ConContacto(b.Id));
        Assert.True(await ConContacto(ajeno.Id));
        var conservado = (await bd.Pedidos.Obtener(a.Id, conBloqueo: false))!;
        Assert.Equal((a.Total, a.CuentaId), (conservado.Total, conservado.CuentaId));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-26")]
    [Trait("Criterio", "AC-PED-34")]
    public async Task El_contacto_no_aparece_en_los_avisos_ni_en_el_detalle_de_la_auditoria()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var pedido = await SembrarDomicilio(EscenarioDePedidos.Titular, Hoy);
        escenario.Reloj.Ahora = Hoy;

        // Operaciones que auditan y avisan: leer el contacto como personal, anular y que el titular lo vea.
        await escenario.Como(EscenarioDePedidos.DeLogistica(), sp => sp.GetRequiredService<IGestionDeDespacho>().Obtener(pedido.Id));
        await escenario.Como(EscenarioDePedidos.DeLaAdministracion(), sp => sp.GetRequiredService<IAdministracionDePedidos>().Anular(new AnularPedidoEntrada(pedido.Id, "otro")));
        await escenario.Como(EscenarioDePedidos.DelTitular(), sp => sp.GetRequiredService<IPedidosDelCliente>().Obtener(pedido.Id));

        var textos = escenario.Avisos.Solicitudes.SelectMany(s => s.Campos.Values).Concat(escenario.Auditoria.Solicitudes.SelectMany(s => s.Detalle.Values.Where(v => v is not null).Select(v => v!))).ToList();
        Assert.NotEmpty(escenario.Auditoria.Solicitudes);
        Assert.DoesNotContain(textos, t => t.Contains(Nombre, StringComparison.Ordinal) || t.Contains(Telefono, StringComparison.Ordinal) || t.Contains(Direccion, StringComparison.Ordinal));
    }
}
