using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Errores;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>
/// La matriz rol × recurso de la lectura del detalle de un pedido, con la Aplicación real sobre PostgreSQL real (T36; PED-40, PED-41; AC-PED-06, AC-PED-25, AC-PED-35): el titular dueño y uno ajeno, el encargado de su sucursal y el
/// de otra, logística, la administración central y el anónimo, sobre un pedido de recojo y uno a domicilio. Quien no puede ver el pedido recibe <b>exactamente lo mismo que si no existiera</b> (mismo código, categoría y clave) y
/// queda <b>un</b> evento de denegación por intento; el contacto solo llega a quien puede leerlo y esa lectura queda auditada.
/// </summary>
[Collection(ColeccionPostgresDePedidos.Nombre)]
public sealed class MatrizDeAlcanceDePedidosTests(PostgresDePedidosFixture bd) : IAsyncLifetime
{
    private static readonly Guid OtraCuenta = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static int _contador;

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Numero() => "WXYZ2345" + "ABCDEFGHJKLMNPQRSTUVWXYZ"[Interlocked.Increment(ref _contador) % 24] + "M";

    private async Task<Pedido> Sembrar(bool domicilio)
    {
        var ahora = EscenarioDePedidos.InicioDelDia;
        var linea = new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Ficciones", null, 1, 50m, 50m, "LIM", false, null, null);
        var contacto = domicilio ? BabelHub.Ecommerce.Domain.Pedidos.ContactoDeEntrega.Crear("Ana Pérez", "987654321", "Jr. Lima 123") : null;
        var pedido = Pedido.Crear(new DatosDePedidoNuevo(
            Guid.NewGuid(),
            Numero(),
            EscenarioDePedidos.Titular,
            domicilio ? TipoDeEntregaDelPedido.Domicilio : TipoDeEntregaDelPedido.Recojo,
            domicilio ? null : "LIM",
            domicilio ? "ayacucho" : null,
            domicilio ? 12m : 0m,
            domicilio ? 62m : 50m,
            contacto,
            new("t-1", ahora),
            new("p-1", ahora),
            null,
            [linea],
            ahora)).Pedido!;
        Assert.True(await bd.Pedidos.Agregar(pedido));
        return pedido;
    }

    public enum Quien
    {
        TitularDueno,
        TitularAjeno,
        EncargadoDeLaSucursal,
        EncargadoDeOtraSucursal,
        Logistica,
        Administracion,
        Anonimo,
    }

    private static ActorActual? Actor(Quien quien) => quien switch
    {
        Quien.TitularDueno => EscenarioDePedidos.DelTitular(),
        Quien.TitularAjeno => EscenarioDePedidos.DelTitular(OtraCuenta),
        Quien.EncargadoDeLaSucursal => EscenarioDePedidos.DelEncargado(ResolucionDeSucursalFalsa.Lima),
        Quien.EncargadoDeOtraSucursal => EscenarioDePedidos.DelEncargado(ResolucionDeSucursalFalsa.Ayacucho),
        Quien.Logistica => EscenarioDePedidos.DeLogistica(),
        Quien.Administracion => EscenarioDePedidos.DeLaAdministracion(),
        _ => null,
    };

    /// <summary>La lectura del detalle por la capacidad que corresponde al rol: el titular, el encargado (de la sucursal de la ruta), logística o la administración. El anónimo prueba con la del titular.</summary>
    private static async Task<Resultado<bool>> Leer(EscenarioDePedidos escenario, Quien quien, Guid pedidoId)
    {
        var actor = Actor(quien);
        Task<Resultado<bool>> Llamar(IServiceProvider sp) => quien switch
        {
            Quien.EncargadoDeLaSucursal or Quien.EncargadoDeOtraSucursal => Convertir(sp.GetRequiredService<IGestionDeRecojo>().Obtener("LIM", pedidoId)),
            Quien.Logistica => Convertir(sp.GetRequiredService<IGestionDeDespacho>().Obtener(pedidoId)),
            Quien.Administracion => Convertir(sp.GetRequiredService<IAdministracionDePedidos>().Obtener(pedidoId)),
            _ => Convertir(sp.GetRequiredService<IPedidosDelCliente>().Obtener(pedidoId)),
        };

        if (actor is null)
        {
            return await escenario.Como(ActorActual.Anonimo(), Llamar);
        }

        return await escenario.Como(actor, Llamar);
    }

    private static async Task<Resultado<bool>> Convertir<T>(Task<Resultado<T>> tarea)
    {
        var resultado = await tarea;
        return resultado.EsExito ? Resultado<bool>.Exito(true) : Resultado<bool>.Fallo(resultado.Error!);
    }

    [Theory]
    [Trait("Criterio", "AC-PED-06")]
    [Trait("Criterio", "AC-PED-35")]
    [InlineData(Quien.TitularDueno, false, true)]
    [InlineData(Quien.TitularDueno, true, true)]
    [InlineData(Quien.TitularAjeno, false, false)]
    [InlineData(Quien.TitularAjeno, true, false)]
    [InlineData(Quien.EncargadoDeLaSucursal, false, true)]
    [InlineData(Quien.EncargadoDeLaSucursal, true, false)]
    [InlineData(Quien.EncargadoDeOtraSucursal, false, false)]
    [InlineData(Quien.Logistica, true, true)]
    [InlineData(Quien.Logistica, false, false)]
    [InlineData(Quien.Administracion, false, true)]
    [InlineData(Quien.Administracion, true, true)]
    [InlineData(Quien.Anonimo, false, false)]
    public async Task Cada_rol_ve_solo_lo_que_le_corresponde_y_lo_demas_responde_igual_que_un_pedido_inexistente(Quien quien, bool domicilio, bool puedeVer)
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var pedido = await Sembrar(domicilio);

        var real = await Leer(escenario, quien, pedido.Id);
        var denegacionesTrasElReal = escenario.Denegaciones.Registradas.Count;
        var inexistente = await Leer(escenario, quien, Guid.NewGuid());
        var denegacionesTrasElInexistente = escenario.Denegaciones.Registradas.Count - denegacionesTrasElReal;

        Assert.Equal(puedeVer, real.EsExito);
        if (puedeVer)
        {
            return;
        }

        // Uniforme: el pedido que existe pero no es tuyo y el que no existe son indistinguibles (categoría, clave y evento).
        Assert.False(inexistente.EsExito);
        Assert.Equal((inexistente.Error!.Categoria, inexistente.Error.Clave), (real.Error!.Categoria, real.Error.Clave));
        Assert.Equal(denegacionesTrasElInexistente, denegacionesTrasElReal);
        Assert.InRange(denegacionesTrasElReal, 0, 1);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-06")]
    public async Task El_contacto_solo_lo_ve_el_titular_y_el_personal_autorizado_y_cada_lectura_del_personal_se_audita()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var pedido = await Sembrar(domicilio: true);

        var delTitular = await escenario.Como(EscenarioDePedidos.DelTitular(), sp => sp.GetRequiredService<IPedidosDelCliente>().Obtener(pedido.Id));
        var deLogistica = await escenario.Como(EscenarioDePedidos.DeLogistica(), sp => sp.GetRequiredService<IGestionDeDespacho>().Obtener(pedido.Id));
        var deLaAdministracion = await escenario.Como(EscenarioDePedidos.DeLaAdministracion(), sp => sp.GetRequiredService<IAdministracionDePedidos>().Obtener(pedido.Id));

        Assert.Equal("Ana Pérez", delTitular.Valor.Contacto!.Nombre);
        Assert.Equal("Ana Pérez", deLogistica.Valor.Contacto!.Nombre);
        Assert.Equal("Ana Pérez", deLaAdministracion.Valor.Contacto!.Nombre);
        Assert.Equal(2, escenario.AuditoriaDelContacto.Lecturas.Count);
        Assert.Equal(new[] { RolesDeUsuario.AdministradorCentral, RolesDeUsuario.PersonalDeLogistica }.Order(), escenario.AuditoriaDelContacto.Lecturas.Select(l => l.Rol).Order());
    }

    [Fact]
    [Trait("Criterio", "AC-PED-06")]
    public async Task Si_la_lectura_del_contacto_no_se_puede_auditar_se_oculta_solo_el_contacto()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var pedido = await Sembrar(domicilio: true);
        escenario.AuditoriaDelContacto.Caida = true;

        var detalle = await escenario.Como(EscenarioDePedidos.DeLogistica(), sp => sp.GetRequiredService<IGestionDeDespacho>().Obtener(pedido.Id));

        Assert.True(detalle.EsExito);
        Assert.Null(detalle.Valor.Contacto);
        Assert.True(detalle.Valor.ContactoOculto);
        Assert.Single(detalle.Valor.Lineas);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-25")]
    public async Task Las_lineas_de_otro_pedido_no_se_operan_y_responden_como_un_pedido_ajeno()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var propio = await Sembrar(domicilio: false);
        var ajeno = await Sembrar(domicilio: false);

        var resultado = await escenario.Como(
            EscenarioDePedidos.DelEncargado(ResolucionDeSucursalFalsa.Lima),
            sp => sp.GetRequiredService<IGestionDeRecojo>().Preparar("LIM", new OperarLineasEntrada(propio.Id, [ajeno.Lineas[0].Id])));
        var inexistente = await escenario.Como(
            EscenarioDePedidos.DelEncargado(ResolucionDeSucursalFalsa.Lima),
            sp => sp.GetRequiredService<IGestionDeRecojo>().Preparar("LIM", new OperarLineasEntrada(propio.Id, [Guid.NewGuid()])));

        Assert.False(resultado.EsExito);
        Assert.False(inexistente.EsExito);
        Assert.Equal((inexistente.Error!.Categoria, inexistente.Error.Clave), (resultado.Error!.Categoria, resultado.Error.Clave));
        var guardado = (await bd.Pedidos.Obtener(ajeno.Id, conBloqueo: false))!;
        Assert.Equal(EstadoDeLineaImpresa.Confirmado, guardado.Lineas[0].EstadoImpreso);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-03")]
    public async Task Los_estados_de_traslado_repetidos_o_de_otro_pedido_no_cambian_nada_y_uno_inexistente_es_un_error_permanente()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var ahora = EscenarioDePedidos.InicioDelDia;
        var traslado = Guid.NewGuid();
        var linea = new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Rayuela", null, 2, 30m, 60m, "AYA", true, traslado, new DateOnly(2026, 11, 25));
        var pedido = Pedido.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), Numero(), EscenarioDePedidos.Titular, TipoDeEntregaDelPedido.Recojo, "LIM", null, 0m, 60m, null, new("t-1", ahora), new("p-1", ahora), null, [linea], ahora)).Pedido!;
        Assert.True(await bd.Pedidos.Agregar(pedido));
        var edicion = pedido.Lineas[0].EdicionId;
        var enTransito = new EstadoDeTrasladoRecibido($"traslado:{traslado:D}:en-transito", traslado, pedido.Id, edicion, 2, EstadoDelTrasladoInformado.EnTransito, new DateOnly(2026, 11, 26));

        // El mismo estado enviado varias veces a la vez: todos responden bien y solo se aplica una vez.
        var resultados = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => escenario.Como(
            ActorActual.DeIntegracion("inventario"),
            sp => sp.GetRequiredService<IReceptorDeEstadosDeTraslado>().Recibir(enTransito))));
        Assert.All(resultados, r => Assert.True(r.EsExito));
        var guardado = (await bd.Pedidos.Obtener(pedido.Id, conBloqueo: false))!;
        Assert.Equal(EstadoDeLineaImpresa.EnTransito, guardado.Lineas[0].EstadoImpreso);
        Assert.Equal(new DateOnly(2026, 11, 26), guardado.Lineas[0].FechaEstimadaDeLlegada);

        // De otro pedido o con un cruce que no coincide: no se aplica.
        var ajeno = enTransito with { PedidoId = Guid.NewGuid(), EventoId = $"traslado:{traslado:D}:recibido", Estado = EstadoDelTrasladoInformado.Recibido };
        var conOtraCantidad = enTransito with { Cantidad = 9, EventoId = $"traslado:{traslado:D}:recibido", Estado = EstadoDelTrasladoInformado.Recibido };
        var rechazadoAjeno = await escenario.Como(ActorActual.DeIntegracion("inventario"), sp => sp.GetRequiredService<IReceptorDeEstadosDeTraslado>().Recibir(ajeno));
        var rechazadoPorCruce = await escenario.Como(ActorActual.DeIntegracion("inventario"), sp => sp.GetRequiredService<IReceptorDeEstadosDeTraslado>().Recibir(conOtraCantidad));
        Assert.False(rechazadoAjeno.EsExito);
        Assert.False(rechazadoPorCruce.EsExito);
        Assert.Equal(EstadoDeLineaImpresa.EnTransito, (await bd.Pedidos.Obtener(pedido.Id, conBloqueo: false))!.Lineas[0].EstadoImpreso);

        // Fuera de orden: «recibido» llega y luego «en tránsito» ya aplicado se repite sin retroceder.
        var recibido = enTransito with { EventoId = $"traslado:{traslado:D}:recibido", Estado = EstadoDelTrasladoInformado.Recibido };
        Assert.True((await escenario.Como(ActorActual.DeIntegracion("inventario"), sp => sp.GetRequiredService<IReceptorDeEstadosDeTraslado>().Recibir(recibido))).EsExito);
        Assert.True((await escenario.Como(ActorActual.DeIntegracion("inventario"), sp => sp.GetRequiredService<IReceptorDeEstadosDeTraslado>().Recibir(enTransito))).EsExito);
        Assert.Equal(EstadoDeLineaImpresa.Recibido, (await bd.Pedidos.Obtener(pedido.Id, conBloqueo: false))!.Lineas[0].EstadoImpreso);
    }
}
