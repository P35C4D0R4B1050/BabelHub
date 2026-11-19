using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using Microsoft.Extensions.DependencyInjection;
using MedioDelContrato = BabelHub.Ecommerce.Application.Pagos.MedioDePago;
using RolesDeUsuario = BabelHub.Shared.Contratos.RolesDeUsuario;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// T32: los pendientes de una cuenta, el cobro con una cancelación diferida, la liberación del contacto de entrega en sus tres disparadores y a los 29 días y 23 horas, la depuración a los dos años y la matriz de autorización de cada acción para el cliente dueño, uno ajeno, el personal y un visitante,
/// sobre PostgreSQL real (AC-PAG-20, AC-PAG-21, AC-PAG-23, AC-PAG-27). La prueba de privacidad del <c>CHECK</c> forzado vive con los repositorios (<c>RepositoriosDePagosPostgresTests</c>).
/// </summary>
[Collection(ColeccionPostgresDePagos.Nombre)]
public sealed class PendientesYAutorizacionConPostgresTests(PostgresDePagosFixture bd) : IAsyncLifetime
{
    private static readonly Guid CuentaA = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid CuentaB = Guid.Parse("b1000000-0000-0000-0000-000000000002");
    private static readonly Guid CuentaC = Guid.Parse("c1000000-0000-0000-0000-000000000003");

    private EntornoDePagosConPostgres _entorno = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _entorno = EntornoDePagosConPostgres.Crear(bd);
    }

    public async Task DisposeAsync() => await _entorno.DisposeAsync();

    private async Task<Resultado<ResultadoDelCobroDelCliente>> Cobrar(Guid cuenta, Guid pedido, string token)
    {
        _entorno.Actor.ComoCliente(cuenta);
        using var alcance = _entorno.Alcance();
        var pagos = alcance.ServiceProvider.GetRequiredService<IPagos>();
        var clave = await pagos.EmitirClaveDeIntento(new EmitirClaveDeIntentoEntrada(pedido));
        Assert.True(clave.EsExito);
        return await pagos.Cobrar(new CobrarEntrada(pedido, clave.Valor.Token, MedioDelContrato.Tarjeta, TokenDelMedioDePago.Crear(token)));
    }

    private async Task<int> Pendientes(Guid cuenta)
    {
        using var alcance = _entorno.Alcance();
        return await alcance.ServiceProvider.GetServices<IPendientesDeCuenta>().Single(p => p is ContarPendientesDeCuenta).Contar(cuenta);
    }

    private async Task Correr<T>(Func<T, Task> accion)
        where T : notnull
    {
        using var alcance = _entorno.Alcance();
        await accion(alcance.ServiceProvider.GetRequiredService<T>());
    }

    private Task<long> Contar(string tabla, string? donde = null) => bd.Consultar<long>($"SELECT count(*) FROM pagos.{tabla}" + (donde is null ? string.Empty : $" WHERE {donde}"))!;

    [Fact]
    [Trait("Criterio", "AC-PAG-20")]
    public async Task Los_pendientes_cuentan_los_pagos_sin_resolver_y_el_reembolso_de_una_compensacion_y_no_se_mezclan_entre_cuentas()
    {
        // La cuenta A: una compra compensada cuyo reembolso quedó pendiente (la pasarela lo rechaza) y dos pagos que no respondieron.
        var compensada = await ComprasDePrueba.Sembrar(_entorno, cuenta: CuentaA);
        _entorno.Pedidos.Falla = new InvalidOperationException("Pedidos no responde.");
        await Cobrar(CuentaA, compensada, EscenariosDelSimulador.ReembolsoRechazado);
        _entorno.Reloj.Avanzar(TimeSpan.FromMinutes(31));
        await Correr<ReanudarComprasAprobadas>(r => r.Ejecutar(50));
        _entorno.Pedidos.Falla = null;
        foreach (var _ in new[] { 1, 2 })
        {
            var pedido = await ComprasDePrueba.Sembrar(_entorno, cuenta: CuentaA);
            await Cobrar(CuentaA, pedido, EscenariosDelSimulador.SinRespuesta);
        }

        // La cuenta B: todo resuelto. La cuenta C: un intento enviado a la pasarela sin respuesta.
        var resuelto = await ComprasDePrueba.Sembrar(_entorno, cuenta: CuentaB);
        await Cobrar(CuentaB, resuelto, EscenariosDelSimulador.Aprobado);
        var sinResultado = await ComprasDePrueba.Sembrar(_entorno, cuenta: CuentaC);
        await Cobrar(CuentaC, sinResultado, EscenariosDelSimulador.SinRespuesta);

        Assert.Equal(3, await Pendientes(CuentaA));
        Assert.Equal(0, await Pendientes(CuentaB));
        Assert.Equal(1, await Pendientes(CuentaC));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Con_una_cancelacion_diferida_no_se_inicia_un_cobro_nuevo_pero_el_pago_ya_pendiente_se_resuelve()
    {
        var pendiente = await ComprasDePrueba.Sembrar(_entorno, cuenta: CuentaA);
        await Cobrar(CuentaA, pendiente, EscenariosDelSimulador.Lento);
        var nuevo = await ComprasDePrueba.Sembrar(_entorno, cuenta: CuentaA);
        _entorno.Cuenta.Estado = new EstadoDeLaCuentaParaPagar(false, MotivoDeCuentaQueNoPuedePagar.CancelacionDiferida);

        var respuesta = await Cobrar(CuentaA, nuevo, EscenariosDelSimulador.Aprobado);

        Assert.Equal(CategoriaDeError.Prohibido, respuesta.Error!.Categoria);
        Assert.Equal(1, _entorno.Pasarela.LlamadasDeCobrar);
        Assert.Equal(1, await Contar("intentos"));

        _entorno.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        await Correr<ConsultarPagosPendientes>(c => c.Ejecutar(50));
        await Correr<ReanudarComprasAprobadas>(r => r.Ejecutar(50));
        Assert.Equal(1, await Contar("intentos", "estado = 'aprobado'"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-23")]
    public async Task El_contacto_se_libera_cuando_Pedidos_lo_pide_al_resolverse_sin_pedido_y_a_los_treinta_dias_y_no_antes()
    {
        var porPedidos = await ComprasDePrueba.Sembrar(_entorno);
        var sinPedido = await ComprasDePrueba.Sembrar(_entorno);
        var alMes = await ComprasDePrueba.Sembrar(_entorno);
        Assert.Equal(3, await Contar("intenciones", "contacto_nombre IS NOT NULL"));

        // (1) Pedidos guardó su copia y pide liberar el contacto de su pedido.
        await Correr<ILiberacionDeContactoDePago>(l => l.Liberar(porPedidos));
        Assert.Equal(1, await Contar("intenciones", $"id = '{porPedidos:D}' AND contacto_nombre IS NULL AND contacto_liberado_en IS NOT NULL"));

        // (2) La intención de otro vence sin pedido.
        _entorno.Reloj.Avanzar(TimeSpan.FromMinutes(31));
        await Correr<CerrarIntencionesVencidas>(c => c.Ejecutar(50));
        Assert.Equal(0, await Contar("intenciones", "contacto_nombre IS NOT NULL AND estado = 'cerrada_sin_cobro'"));

        // (3) Una intención que sigue abierta conserva el contacto a los 29 días y 23 horas y lo pierde a los 30.
        await bd.Ejecutar($"UPDATE pagos.intenciones SET estado = 'abierta', motivo_de_cierre = NULL, cerrada_en = NULL, vence_en = '2030-01-01', limite_pendiente_en = '2030-01-01', contacto_nombre = 'Ana Pérez Quispe', contacto_telefono = '987654321', contacto_direccion = 'Av. Los Olivos 123', contacto_liberado_en = NULL WHERE id = '{alMes:D}'");
        var creada = await bd.Consultar<DateTimeOffset>($"SELECT creada_en FROM pagos.intenciones WHERE id = '{alMes:D}'");
        _entorno.Reloj.Avanzar(creada + TimeSpan.FromDays(29) + TimeSpan.FromHours(23) - _entorno.Reloj.Ahora);
        await Correr<PurgarContactoDeIntenciones>(p => p.Ejecutar(50));
        Assert.Equal(1, await Contar("intenciones", $"id = '{alMes:D}' AND contacto_nombre IS NOT NULL"));

        _entorno.Reloj.Avanzar(TimeSpan.FromHours(1));
        await Correr<PurgarContactoDeIntenciones>(p => p.Ejecutar(50));
        Assert.Equal(1, await Contar("intenciones", $"id = '{alMes:D}' AND contacto_nombre IS NULL"));
        _ = sinPedido;
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-27")]
    public async Task A_los_dos_anos_de_cerrada_la_intencion_se_desvincula_de_la_persona_y_conserva_importes_y_estados()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno, cuenta: CuentaA);
        await Cobrar(CuentaA, pedido, EscenariosDelSimulador.Aprobado);
        Assert.Equal(1, await Contar("intenciones", "estado = 'completada'"));

        _entorno.Reloj.Avanzar(TimeSpan.FromDays(365 * 2) + TimeSpan.FromDays(1));
        await Correr<DepurarIntencionesAntiguas>(d => d.Ejecutar(50));

        Assert.Equal(1, await Contar("intenciones", "cuenta_id IS NULL AND desvinculada_en IS NOT NULL AND estado = 'completada' AND total = 140"));
        Assert.Equal(0, await Contar("intencion_lineas", "edicion_id IS NOT NULL OR titulo IS NOT NULL OR isbn IS NOT NULL"));
        Assert.Equal(1, await Contar("intentos", "estado = 'aprobado'"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task La_matriz_de_autorizacion_distingue_al_cliente_dueno_a_uno_ajeno_al_personal_y_al_visitante()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno, cuenta: CuentaA);

        async Task<Resultado<EstadoDelPagoVista>> Estado()
        {
            using var alcance = _entorno.Alcance();
            return await alcance.ServiceProvider.GetRequiredService<IPagos>().ObtenerEstado(pedido);
        }

        async Task<Resultado<PaginaDeReembolsos>> Reembolsos()
        {
            using var alcance = _entorno.Alcance();
            return await alcance.ServiceProvider.GetRequiredService<IReembolsos>().ListarPendientes(1, 20);
        }

        _entorno.Actor.ComoCliente(CuentaA);
        Assert.True((await Estado()).EsExito);
        Assert.Equal(CategoriaDeError.Prohibido, (await Reembolsos()).Error!.Categoria);

        _entorno.Actor.ComoCliente(CuentaB);
        Assert.Equal(CategoriaDeError.NoEncontrado, (await Estado()).Error!.Categoria);

        foreach (var rol in new[] { RolesDeUsuario.EncargadoDeSucursal, RolesDeUsuario.PersonalDeLogistica })
        {
            _entorno.Actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), rol, Guid.NewGuid());
            Assert.Equal(CategoriaDeError.NoEncontrado, (await Estado()).Error!.Categoria);
            Assert.Equal(CategoriaDeError.Prohibido, (await Reembolsos()).Error!.Categoria);
        }

        _entorno.Actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.AdministradorCentral, null);
        Assert.True((await Reembolsos()).EsExito);

        _entorno.Actor.Actual = ActorActual.Anonimo();
        Assert.Equal(CategoriaDeError.NoAutenticado, (await Estado()).Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoAutenticado, (await Reembolsos()).Error!.Categoria);
    }
}
