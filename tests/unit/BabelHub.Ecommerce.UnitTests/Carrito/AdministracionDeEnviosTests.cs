using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>T45: los costos de envío por sucursal de la administración central (CAR-46, CAR-47, CAR-48).</summary>
public sealed class AdministracionDeEnviosTests
{
    private readonly SucursalesDeCarritoFalsas _sucursales = new();
    private readonly TarifasDeEnvioEnMemoria _tarifas = new();
    private readonly AuditoriaDelCarritoEspia _auditoria = new();
    private readonly DenegacionesDelCatalogoEspia _denegaciones = new();

    private IAdministracionDeEnvios Administracion(ActorDePrueba? actor = null) => new AdministracionDeEnvios(
        new AutorizadorDelCarrito(actor ?? ActorDePrueba.DeAdministradorCentral(), _denegaciones),
        _sucursales,
        _tarifas,
        new CostoDeEnvioFijo(30m),
        new UnidadDeTrabajoReversible(_tarifas),
        new RelojFijo(EntornoDelCarrito.Inicio),
        _auditoria);

    private Task<Resultado<CostoDeEnvioDeSucursal>> Cambiar(string sucursal, decimal importe, ActorDePrueba? actor = null) =>
        Administracion(actor).Cambiar(new CambiarCostoDeEnvioEntrada(sucursal, importe));

    // ---------- Consultar y cambiar ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-40")]
    public async Task Sin_costos_propios_cada_sucursal_registrada_muestra_el_predeterminado()
    {
        var costos = (await Administracion().Listar()).Valor;

        Assert.Equal(30m, costos.Predeterminado);
        Assert.Equal(
            [(Sucursal.Cusco, "Cusco", 30m, false), (Sucursal.Lima, "Lima", 30m, false), (Sucursal.Principal, "Sede central", 30m, false)],
            costos.Sucursales.Select(s => (s.CodigoDeSucursal, s.Nombre, s.Importe, s.EsPropio)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-40")]
    public async Task Cambiar_una_sucursal_guarda_su_costo_y_audita_el_anterior_y_el_nuevo_sin_tocar_las_demas()
    {
        var primero = await Cambiar(Sucursal.Cusco, 18.50m);
        var segundo = await Cambiar(Sucursal.Cusco, 0m);

        Assert.Equal((Sucursal.Cusco, "Cusco", 18.50m, true), (primero.Valor.CodigoDeSucursal, primero.Valor.Nombre, primero.Valor.Importe, primero.Valor.EsPropio));
        Assert.True(segundo.EsExito, segundo.Error?.Clave);
        Assert.Equal([(Sucursal.Cusco, 30m, 18.50m), (Sucursal.Cusco, 18.50m, 0m)], _auditoria.Tarifas);
        var costos = (await Administracion().Listar()).Valor.Sucursales.ToDictionary(s => s.CodigoDeSucursal);
        Assert.Equal((0m, true), (costos[Sucursal.Cusco].Importe, costos[Sucursal.Cusco].EsPropio));
        Assert.Equal((30m, false), (costos[Sucursal.Lima].Importe, costos[Sucursal.Lima].EsPropio));
        Assert.Empty(_denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-40")]
    public async Task Si_no_se_puede_auditar_el_costo_no_cambia()
    {
        await Cambiar(Sucursal.Cusco, 18.50m);
        _auditoria.Caida = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Cambiar(Sucursal.Cusco, 99m));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Cambiar(Sucursal.Lima, 99m));

        Assert.Equal([Sucursal.Cusco], _tarifas.Propias.Keys);
        Assert.Equal(18.50m, _tarifas.Propias[Sucursal.Cusco].Valor);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("500")]
    [InlineData("499.99")]
    [Trait("Criterio", "AC-CAR-41")]
    public async Task Los_limites_del_rango_se_admiten(string importe)
    {
        var cambiado = await Cambiar(Sucursal.Cusco, decimal.Parse(importe, System.Globalization.CultureInfo.InvariantCulture));

        Assert.True(cambiado.EsExito, cambiado.Error?.Clave);
    }

    [Theory]
    [InlineData("-0.01", "fuera-de-rango")]
    [InlineData("500.01", "fuera-de-rango")]
    [InlineData("10.005", "formato-invalido")]
    [Trait("Criterio", "AC-CAR-41")]
    public async Task Un_importe_negativo_mayor_que_500_o_con_tres_decimales_no_se_guarda(string importe, string clave)
    {
        var rechazado = await Cambiar(Sucursal.Cusco, decimal.Parse(importe, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal((CategoriaDeError.Validacion, "validacion-fallida"), (rechazado.Error!.Categoria, rechazado.Error.Clave));
        Assert.Equal([("importe", clave)], rechazado.Error.Errores.Select(e => (e.Campo, e.Clave)));
        Assert.Empty(_tarifas.Propias);
        Assert.Empty(_auditoria.Tarifas);
    }

    [Theory]
    [InlineData("no-existe", "formato-invalido")]
    [InlineData("", "obligatorio")]
    [Trait("Criterio", "AC-CAR-41")]
    public async Task Una_sucursal_que_no_esta_registrada_no_recibe_un_costo(string sucursal, string clave)
    {
        var rechazado = await Cambiar(sucursal, 10m);

        Assert.Equal(CategoriaDeError.Validacion, rechazado.Error!.Categoria);
        Assert.Equal([("sucursal", clave)], rechazado.Error.Errores.Select(e => (e.Campo, e.Clave)));
        Assert.Empty(_tarifas.Propias);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-41")]
    public async Task Sin_ninguna_lista_de_sucursales_no_se_puede_ver_ni_cambiar()
    {
        _sucursales.SinLista = true;

        var lista = await Administracion().Listar();
        var cambio = await Cambiar(Sucursal.Cusco, 10m);

        Assert.Equal((CategoriaDeError.NoDisponible, "servicio-no-disponible"), (lista.Error!.Categoria, lista.Error.Clave));
        Assert.Equal((CategoriaDeError.NoDisponible, "servicio-no-disponible"), (cambio.Error!.Categoria, cambio.Error.Clave));
        Assert.Empty(_tarifas.Propias);
    }

    // ---------- Alcance (CAR-48) ----------

    public static TheoryData<string> SinPermiso() => new("encargado", "logistica", "cliente", "visitante", "sin-contexto", "sistema", "integracion");

    private static ActorDePrueba Actor(string quien) => quien switch
    {
        "encargado" => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
        "logistica" => ActorDePrueba.DePersonalDeLogistica(),
        "cliente" => ActorDePrueba.DeCliente(),
        "visitante" => ActorDePrueba.Anonimo(),
        "sin-contexto" => ActorDePrueba.SinContexto(),
        "sistema" => ActorDePrueba.DelSistema(),
        _ => ActorDePrueba.DeIntegracion("n8n"),
    };

    [Theory]
    [MemberData(nameof(SinPermiso))]
    [Trait("Criterio", "AC-CAR-41")]
    public async Task Solo_la_administracion_central_ve_y_cambia_los_costos_y_cada_rechazo_queda_registrado(string quien)
    {
        var anonimo = quien is "visitante" or "sin-contexto";
        var esperado = anonimo ? CategoriaDeError.NoAutenticado : CategoriaDeError.Prohibido;

        var lista = await Administracion(Actor(quien)).Listar();
        // Una entrada inválida no cambia la respuesta: el rol se comprueba antes de validar.
        var cambio = await Cambiar("no-existe", -1m, Actor(quien));

        Assert.Equal(esperado, lista.Error!.Categoria);
        Assert.Equal(esperado, cambio.Error!.Categoria);
        Assert.Empty(cambio.Error.Errores);
        Assert.Equal(2, _denegaciones.Eventos.Count);
        Assert.All(_denegaciones.Eventos, e => Assert.Equal(anonimo ? "ECO-NO-AUTENTICADO" : "ECO-PROHIBIDO", e.CodigoDeError));
        Assert.Empty(_tarifas.Propias);
        Assert.Empty(_auditoria.Tarifas);
    }
}
