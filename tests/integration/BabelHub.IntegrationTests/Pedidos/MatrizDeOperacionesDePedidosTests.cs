using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Shared.Errores;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>
/// La matriz rol × <b>operación de escritura</b> sobre un pedido (T36; PED-08, PED-09, PED-11, PED-13, PED-17, PED-26, PED-28, PED-40, PED-41; AC-PED-25, AC-PED-35), con la Aplicación real sobre PostgreSQL real: cancelar, anular, preparar,
/// marcar listas y entregar en recojo, y preparar y marcar listas a domicilio, por el titular dueño y uno ajeno, el encargado de la sucursal del pedido y el de otra, logística, la administración central y un anónimo. Quien no
/// tiene la capacidad recibe un rechazo de permiso o «no encontrado» (nunca un éxito ni un estado de negocio que revele algo) y el pedido no cambia; quien la tiene, nunca recibe esos rechazos.
/// </summary>
[Collection(ColeccionPostgresDePedidos.Nombre)]
public sealed class MatrizDeOperacionesDePedidosTests(PostgresDePedidosFixture bd) : IAsyncLifetime
{
    private static readonly Guid OtraCuenta = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private static int _contador;

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

    public enum Operacion
    {
        Cancelar,
        Anular,
        PrepararDeRecojo,
        MarcarListasDeRecojo,
        EntregarDeRecojo,
        PrepararADomicilio,
        MarcarListasADomicilio,
    }

    public static TheoryData<Operacion, Quien, bool> Matriz()
    {
        var datos = new TheoryData<Operacion, Quien, bool>();
        foreach (var operacion in Enum.GetValues<Operacion>())
        {
            foreach (var quien in Enum.GetValues<Quien>())
            {
                datos.Add(operacion, quien, TieneLaCapacidad(operacion, quien));
            }
        }

        return datos;
    }

    /// <summary>Quién puede ejecutar cada operación: cancelar, solo el titular dueño; anular, solo la administración; el recojo, el encargado de la sucursal del pedido; el domicilio, logística y la administración (PED-28).</summary>
    private static bool TieneLaCapacidad(Operacion operacion, Quien quien) => operacion switch
    {
        Operacion.Cancelar => quien == Quien.TitularDueno,
        Operacion.Anular => quien == Quien.Administracion,
        Operacion.PrepararDeRecojo or Operacion.MarcarListasDeRecojo or Operacion.EntregarDeRecojo => quien == Quien.EncargadoDeLaSucursal,
        _ => quien is Quien.Logistica or Quien.Administracion,
    };

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Numero() => "HJKL2345" + "ABCDEFGHJKLMNPQRSTUVWXYZ"[Interlocked.Increment(ref _contador) % 24] + "N";

    private async Task<Pedido> Sembrar(bool domicilio)
    {
        var ahora = EscenarioDePedidos.InicioDelDia;
        var linea = new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Ficciones", null, 1, 50m, 50m, "LIM", false, null, null);
        var contacto = domicilio ? BabelHub.Ecommerce.Domain.Pedidos.ContactoDeEntrega.Crear("Ana Pérez", "987654321", "Jr. Lima 123") : null;
        var pedido = Pedido.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), Numero(), EscenarioDePedidos.Titular, domicilio ? TipoDeEntregaDelPedido.Domicilio : TipoDeEntregaDelPedido.Recojo, domicilio ? null : "LIM", domicilio ? "ayacucho" : null, domicilio ? 12m : 0m, domicilio ? 62m : 50m, contacto, new("t-1", ahora), new("p-1", ahora), null, [linea], ahora)).Pedido!;
        Assert.True(await bd.Pedidos.Agregar(pedido));
        return pedido;
    }

    private static ActorActual Actor(Quien quien) => quien switch
    {
        Quien.TitularDueno => EscenarioDePedidos.DelTitular(),
        Quien.TitularAjeno => EscenarioDePedidos.DelTitular(OtraCuenta),
        Quien.EncargadoDeLaSucursal => EscenarioDePedidos.DelEncargado(ResolucionDeSucursalFalsa.Lima),
        Quien.EncargadoDeOtraSucursal => EscenarioDePedidos.DelEncargado(ResolucionDeSucursalFalsa.Ayacucho),
        Quien.Logistica => EscenarioDePedidos.DeLogistica(),
        Quien.Administracion => EscenarioDePedidos.DeLaAdministracion(),
        _ => ActorActual.Anonimo(),
    };

    private static async Task<ErrorDeAplicacion?> Ejecutar(EscenarioDePedidos escenario, Operacion operacion, Quien quien, Pedido pedido)
    {
        var entrada = new OperarLineasEntrada(pedido.Id, [.. pedido.Lineas.Select(l => l.Id)]);
        return await escenario.Como(Actor(quien), async sp =>
        {
            return operacion switch
            {
                Operacion.Cancelar => (await sp.GetRequiredService<IPedidosDelCliente>().Cancelar(pedido.Id)).Error,
                Operacion.Anular => (await sp.GetRequiredService<IAdministracionDePedidos>().Anular(new AnularPedidoEntrada(pedido.Id, "otro"))).Error,
                Operacion.PrepararDeRecojo => (await sp.GetRequiredService<IGestionDeRecojo>().Preparar("LIM", entrada)).Error,
                Operacion.MarcarListasDeRecojo => (await sp.GetRequiredService<IGestionDeRecojo>().MarcarListas("LIM", entrada)).Error,
                Operacion.EntregarDeRecojo => (await sp.GetRequiredService<IGestionDeRecojo>().Entregar("LIM", entrada)).Error,
                Operacion.PrepararADomicilio => (await sp.GetRequiredService<IGestionDeDespacho>().Preparar(entrada)).Error,
                _ => (await sp.GetRequiredService<IGestionDeDespacho>().MarcarListas(entrada)).Error,
            };
        });
    }

    [Theory]
    [MemberData(nameof(Matriz))]
    [Trait("Criterio", "AC-PED-25")]
    [Trait("Criterio", "AC-PED-35")]
    public async Task Cada_rol_solo_ejecuta_las_operaciones_que_le_corresponden_y_el_resto_se_rechaza_sin_cambiar_el_pedido(Operacion operacion, Quien quien, bool tieneLaCapacidad)
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var domicilio = operacion is Operacion.PrepararADomicilio or Operacion.MarcarListasADomicilio;
        var pedido = await Sembrar(domicilio);
        var estadoAntes = (await bd.Pedidos.Obtener(pedido.Id, conBloqueo: false))!.Lineas.Select(l => l.EstadoImpreso).ToList();

        var error = await Ejecutar(escenario, operacion, quien, pedido);

        var contexto = $"{operacion} por {quien}";
        var rechazoDePermiso = error is { Categoria: CategoriaDeError.Prohibido or CategoriaDeError.NoEncontrado or CategoriaDeError.NoAutenticado };
        if (tieneLaCapacidad)
        {
            Assert.False(rechazoDePermiso, $"Quien tiene la capacidad no debe recibir un rechazo de permiso. {contexto}: {error?.Clave}");
        }
        else
        {
            Assert.True(rechazoDePermiso, $"Quien no tiene la capacidad debe recibir un rechazo de permiso o «no encontrado». {contexto}: {error?.Categoria} {error?.Clave}");
            Assert.Equal(estadoAntes, (await bd.Pedidos.Obtener(pedido.Id, conBloqueo: false))!.Lineas.Select(l => l.EstadoImpreso).ToList());
        }
    }
}
