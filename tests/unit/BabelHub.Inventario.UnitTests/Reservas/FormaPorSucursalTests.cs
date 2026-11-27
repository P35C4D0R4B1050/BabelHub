using System.Security.Cryptography;
using System.Text;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.UnitTests.Fuentes;
using static BabelHub.Inventario.UnitTests.Reservas.EntornoDeReservas;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>
/// T36: la forma de una solicitud por sucursal (SEL-42): todas las líneas impresas o ninguna (la sucursal de una digital se ignora), el formato del código, sin sucursal de recojo común, una sucursal que exista, la edición
/// repetida solo cuando se declara la sucursal y la sucursal dentro de la carga de la idempotencia (SEL-31). Todo rechazo ocurre sin apartar stock, en reservar y en la consulta previa.
/// </summary>
public sealed class FormaPorSucursalTests
{
    private static (EntornoDeReservas Entorno, Guid Libro, Guid OtroLibro) Preparar()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        var otro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 8, 0);
        entorno.Base.Existencias.Poner(entorno.Base.Cusco.Id, libro, 8, 0);
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, otro, 8, 0);
        return (entorno, libro, otro);
    }

    /// <summary>La misma solicitud por los dos casos de uso: ambos deben rechazarla con el mismo campo y clave, sin mover stock.</summary>
    private static async Task RechazanAmbos(EntornoDeReservas entorno, ReservarEntrada entrada, string campo, string clave)
    {
        var reserva = await entorno.Reservar().Ejecutar(entrada);
        var consulta = await entorno.ConsultarSeleccion().Ejecutar(new ConsultarSeleccionEntrada(entrada.TipoDeEntrega, entrada.CodigoDeSucursalDeRecojo, entrada.RegionDeEntrega, entrada.Lineas));

        foreach (var error in new[] { reserva.Error, consulta.Error })
        {
            Assert.NotNull(error);
            Assert.Equal(CategoriaDeError.Validacion, error.Categoria);
            Assert.Contains(new ErrorDeCampo(campo, clave), error.Errores);
        }

        Assert.Empty(entorno.Base.Existencias.Movimientos);
        Assert.Empty(entorno.Reservas.Guardadas);
        Assert.DoesNotContain("existencias.GarantizarFilas", entorno.Base.Bitacora.Llamadas);
    }

    private static ReservarEntrada RecojoPorSucursal(params LineaSolicitada[] lineas) =>
        new(Guid.NewGuid(), TipoDeEntregaDeReserva.Recojo, null, null, lineas, ClaveNueva());

    [Fact]
    [Trait("Criterio", "AC-SEL-44")]
    public async Task ConSucursalSoloEnUnaDeLasDosLineas_SeRechaza()
    {
        var (entorno, libro, otro) = Preparar();

        await RechazanAmbos(entorno, RecojoPorSucursal(Linea(libro, sucursal: "lima"), Linea(otro)), "lineas.codigoDeSucursal", "obligatorio");
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-44")]
    public async Task ConUnaSucursalQueNoExiste_SeRechazaAntesDeBloquearElStock()
    {
        var (entorno, libro, otro) = Preparar();

        await RechazanAmbos(entorno, RecojoPorSucursal(Linea(libro, sucursal: "lima"), Linea(otro, sucursal: "tacna-9")), "lineas.codigoDeSucursal", "inexistente");
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-44")]
    [InlineData(TipoDeEntregaDeReserva.Recojo, "lima", null)]
    [InlineData(TipoDeEntregaDeReserva.Domicilio, "lima", "lima")]
    public async Task ConSucursalPorLineaYAdemasUnaSucursalDeRecojoComun_SeRechaza(TipoDeEntregaDeReserva tipo, string comun, string? region)
    {
        var (entorno, libro, _) = Preparar();

        await RechazanAmbos(entorno, new ReservarEntrada(Guid.NewGuid(), tipo, comun, region, [Linea(libro, sucursal: "lima")], ClaveNueva()), "codigoDeSucursalDeRecojo", "formato-invalido");
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-44")]
    [InlineData("")]
    [InlineData("LIMA")]
    [InlineData("a")]
    [InlineData("lima\n")]
    [InlineData("una-sucursal-demasiado-larga")]
    public async Task ConUnCodigoDeSucursalMalFormado_SeRechazaSinRepetirElValor(string codigo)
    {
        var (entorno, libro, _) = Preparar();
        var entrada = RecojoPorSucursal(Linea(libro, sucursal: codigo));

        await RechazanAmbos(entorno, entrada, "lineas.codigoDeSucursal", "formato-invalido");

        var error = (await entorno.Reservar().Ejecutar(entrada)).Error!;
        Assert.All(error.Errores, e => Assert.DoesNotContain("demasiado-larga", e.Campo + e.Clave, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-44")]
    public async Task ADomicilioPorSucursal_LaRegionSigueSiendoObligatoria()
    {
        var (entorno, libro, _) = Preparar();

        await RechazanAmbos(entorno, new ReservarEntrada(Guid.NewGuid(), TipoDeEntregaDeReserva.Domicilio, null, null, [Linea(libro, sucursal: "lima")], ClaveNueva()), "regionDeEntrega", "obligatorio");
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-44")]
    public async Task SinSucursalPorLinea_LaEdicionSigueSinRepetirseYElRecojoSigueExigiendoLaSucursalComun()
    {
        var (entorno, libro, _) = Preparar();

        await RechazanAmbos(entorno, ARecojo("lima", [Linea(libro), Linea(libro)]), "lineas", "duplicadas");
        await RechazanAmbos(entorno, RecojoPorSucursal(Linea(libro)), "codigoDeSucursalDeRecojo", "obligatorio");
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-44")]
    public async Task LaSucursalDeUnaLineaDigitalSeIgnora_LaConsultaPreviaExigeLaFormaDeLasImpresas()
    {
        var (entorno, libro, _) = Preparar();
        var digital = entorno.Digital();

        var sinSucursalComun = await entorno.ConsultarSeleccion()
            .Ejecutar(new ConsultarSeleccionEntrada(TipoDeEntregaDeReserva.Recojo, null, null, [Linea(libro), Linea(digital, sucursal: "lima")]));
        var repetida = await entorno.ConsultarSeleccion()
            .Ejecutar(new ConsultarSeleccionEntrada(TipoDeEntregaDeReserva.Recojo, "lima", null, [Linea(libro), Linea(libro), Linea(digital, sucursal: "lima")]));
        var legado = await entorno.ConsultarSeleccion()
            .Ejecutar(new ConsultarSeleccionEntrada(TipoDeEntregaDeReserva.Recojo, "lima", null, [Linea(libro), Linea(digital, sucursal: "tacna-9")]));
        var soloDigital = await entorno.ConsultarSeleccion()
            .Ejecutar(new ConsultarSeleccionEntrada(TipoDeEntregaDeReserva.Recojo, null, null, [Linea(digital, sucursal: "lima")]));

        Assert.Contains(new ErrorDeCampo("codigoDeSucursalDeRecojo", "obligatorio"), sinSucursalComun.Error!.Errores);
        Assert.Contains(new ErrorDeCampo("lineas", "duplicadas"), repetida.Error!.Errores);
        Assert.Single(legado.Valor.Lineas);
        Assert.Empty(soloDigital.Valor.Lineas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-44")]
    public void LaCargaSinSucursalConservaSuHuella_YLaSucursalDeCadaLineaEntraSoloSiSeDeclara()
    {
        var pedido = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var linea = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        var edicion = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
        var sinSucursal = new ReservarEntrada(pedido, TipoDeEntregaDeReserva.Recojo, "lima", null, [new LineaSolicitada(linea, edicion, 2)], ClaveNueva());
        var enLima = sinSucursal with { CodigoDeSucursalDeRecojo = null, Lineas = [new LineaSolicitada(linea, edicion, 2, "lima")] };
        var enCusco = enLima with { Lineas = [new LineaSolicitada(linea, edicion, 2, "cusco")] };

        // La huella de antes de la venta por sucursal, para que una clave guardada por la versión anterior siga reconociendo su carga.
        var anterior = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"reservar|{pedido:N}|0|lima||{linea:N}:{edicion:N}:2")));

        Assert.Equal(anterior, CargaCanonicaDeReservar.Huella(sinSucursal));
        Assert.NotEqual(CargaCanonicaDeReservar.Huella(enLima), CargaCanonicaDeReservar.Huella(enCusco));
        Assert.NotEqual(CargaCanonicaDeReservar.Huella(enLima), CargaCanonicaDeReservar.Huella(sinSucursal with { CodigoDeSucursalDeRecojo = null }));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-44")]
    public async Task RepetirLaClaveCambiandoSoloLaSucursalDeUnaLinea_EsUnConflicto()
    {
        var (entorno, libro, _) = Preparar();
        var enLima = RecojoPorSucursal(Linea(libro, sucursal: "lima"));
        var previa = entorno.Pendiente(enLima.PedidoId, [(libro, entorno.Base.Lima, 1)], clienteId: entorno.ClienteId);
        entorno.Reservas.Guardadas.Remove(previa);
        entorno.Reservas.Sembrar(previa, new DatosDeLaSolicitud(enLima.ClaveDeIdempotencia, CargaCanonicaDeReservar.Huella(enLima), null));

        var resultado = await entorno.Reservar().Ejecutar(enLima with { Lineas = [enLima.Lineas[0] with { CodigoDeSucursal = "cusco" }] });

        Assert.Equal((CategoriaDeError.Conflicto, "idempotencia-en-conflicto"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Empty(entorno.Base.Existencias.Movimientos);
    }
}
