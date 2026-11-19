using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Identidad;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// Los adaptadores de Identidad sobre el Inventario (identidad-seguridad T36; AC-IDE-24, AC-IDE-53; IDE-27, IDE-64): la baja del titular solo se da por entregada si el Inventario la aceptó y los pendientes de la cuenta
/// fallan cerrados cuando no se pueden contar (nunca se asume cero).
/// </summary>
public sealed class AdaptadoresDelInventarioTests
{
    private sealed class InventarioFalso : IClienteDelInventario
    {
        public Func<Guid, ResultadoDelInventario> Baja { get; set; } = _ => ResultadoDelInventario.Correcta;

        public Func<Guid, ResultadoDelInventario<PendientesDeTitular>> Pendientes { get; set; } = _ => ResultadoDelInventario<PendientesDeTitular>.ConValor(new PendientesDeTitular(0));

        public List<Guid> BajasPedidas { get; } = [];

        public Task<ResultadoDelInventario> DarDeBajaAlTitular(Guid titularId, CancellationToken cancelacion = default)
        {
            BajasPedidas.Add(titularId);
            return Task.FromResult(Baja(titularId));
        }

        public Task<ResultadoDelInventario<PendientesDeTitular>> ConsultarPendientes(Guid titularId, CancellationToken cancelacion = default) => Task.FromResult(Pendientes(titularId));

        public Task<ResultadoDelInventario> RegistrarEdicion(EdicionARegistrar edicion, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<DisponibilidadDeVariasEdiciones>> ConsultarDisponibilidad(IReadOnlyList<Guid> edicionIds, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<IngresoRegistrado>> RegistrarIngreso(IngresoARegistrar ingreso, string claveDeIdempotencia, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<IReadOnlyList<IngresoRegistrado>>> ListarIngresos(Guid edicionId, int limite, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<RespuestaDeConsultaPrevia>> ConsultarPrevia(ConsultaPrevia consulta, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<ReservaDelInventario>> Reservar(SolicitudDeReservaDeStock solicitud, string claveDeIdempotencia, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<ReservaDelInventario>> Liberar(Guid pedidoId, string motivo, IReadOnlyList<Guid>? lineaIds = null, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<ReservaDelInventario>> ConsultarReserva(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<ExtensionDeLaReserva>> ExtenderReserva(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<ReservaDelInventario>> ConfirmarReserva(Guid pedidoId, string claveDeIdempotencia, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<CierreDeLaReserva>> CerrarReserva(Guid pedidoId, string motivo, IReadOnlyList<Guid> lineaIds, Guid? envioId, string claveDeIdempotencia, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<IReadOnlyList<SucursalPublica>>> ListarSucursales(CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<ContactoDeSucursalDelInventario>> ConsultarContactoDeSucursal(string codigoDeSucursal, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-53")]
    public async Task LaBaja_SeDaPorEntregadaSoloSiElInventarioLaAcepto_ConElIdentificadorOpaco()
    {
        var inventario = new InventarioFalso();
        var entrega = new InventarioEntregaDeBajas(inventario);
        var opaco = Guid.NewGuid();

        Assert.True(await entrega.Entregar(opaco));
        Assert.Equal([opaco], inventario.BajasPedidas);
    }

    [Theory]
    [InlineData("sin-respuesta")]
    [InlineData("limite")]
    [InlineData("rechazo-401")]
    [InlineData("rechazo-500")]
    [Trait("Criterio", "AC-IDE-53")]
    public async Task UnaFallaDelInventario_NoSeDaPorEntregada_NiLanza_ParaReintentar(string caso)
    {
        var inventario = new InventarioFalso
        {
            Baja = _ => caso switch
            {
                "sin-respuesta" => ResultadoDelInventario.SinRespuesta,
                "limite" => ResultadoDelInventario.Limitada("INV-LIMITE-EXCEDIDO"),
                "rechazo-401" => ResultadoDelInventario.Rechazo(401, "INV-NO-AUTENTICADO"),
                _ => ResultadoDelInventario.Rechazo(503, null),
            },
        };

        Assert.False(await new InventarioEntregaDeBajas(inventario).Entregar(Guid.NewGuid()));
    }

    [Fact]
    public async Task LaBajaSinIdentificador_SeRechaza_SinLlamarAlInventario()
    {
        var inventario = new InventarioFalso();

        await Assert.ThrowsAsync<ArgumentException>(() => new InventarioEntregaDeBajas(inventario).Entregar(Guid.Empty));

        Assert.Empty(inventario.BajasPedidas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public async Task LosPendientesDelInventario_SonLasReservasActivasDelTitular()
    {
        var inventario = new InventarioFalso { Pendientes = _ => ResultadoDelInventario<PendientesDeTitular>.ConValor(new PendientesDeTitular(3)) };

        Assert.Equal(3, await new InventarioPendientesDeCuenta(inventario).Contar(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("sin-respuesta")]
    [InlineData("limite")]
    [InlineData("rechazo")]
    [Trait("Criterio", "AC-IDE-24")]
    public async Task SiNoSePuedeContar_LanzaYNuncaSeAsumeCero(string caso)
    {
        var inventario = new InventarioFalso
        {
            Pendientes = _ => caso switch
            {
                "sin-respuesta" => ResultadoDelInventario<PendientesDeTitular>.SinRespuesta,
                "limite" => ResultadoDelInventario<PendientesDeTitular>.Limitada("INV-LIMITE-EXCEDIDO"),
                _ => ResultadoDelInventario<PendientesDeTitular>.Rechazo(403, "INV-PROHIBIDO"),
            },
        };

        await Assert.ThrowsAsync<PendientesNoDisponiblesException>(() => new InventarioPendientesDeCuenta(inventario).Contar(Guid.NewGuid()));
    }

    private sealed class ModuloFijo(int pendientes, bool falla = false) : IPendientesDeCuenta
    {
        public Task<int> Contar(Guid usuarioId, CancellationToken cancelacion = default) =>
            falla ? throw new PendientesNoDisponiblesException() : Task.FromResult(pendientes);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public async Task LosPendientesDeLaCuenta_SumanLosDeCadaModulo_YSinModulosOConUnNegativoLanzan()
    {
        var compuesto = new PendientesDeCuentaCompuestos([new ModuloFijo(2), new ModuloFijo(0), new ModuloFijo(4)]);

        Assert.Equal(6, await compuesto.Contar(Guid.NewGuid()));
        await Assert.ThrowsAsync<PendientesNoDisponiblesException>(() => new PendientesDeCuentaCompuestos([]).Contar(Guid.NewGuid()));
        await Assert.ThrowsAsync<PendientesNoDisponiblesException>(() => new PendientesDeCuentaCompuestos([new ModuloFijo(1), new ModuloFijo(-5)]).Contar(Guid.NewGuid()));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public async Task SiUnModuloNoPuedeContar_ElTotalLanza_AunqueLosDemasDigaCero()
    {
        var compuesto = new PendientesDeCuentaCompuestos([new ModuloFijo(0), new ModuloFijo(0, falla: true), new ModuloFijo(0)]);

        await Assert.ThrowsAsync<PendientesNoDisponiblesException>(() => compuesto.Contar(Guid.NewGuid()));
    }
}
