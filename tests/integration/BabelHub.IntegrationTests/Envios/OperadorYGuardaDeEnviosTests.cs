using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Domain.Envios;
using BabelHub.Ecommerce.Infrastructure.Envios;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Envios;

/// <summary>El operador simulado y el nulo, y la guarda de arranque de Envíos (T14; ENV-18, D02): idempotencia por clave, modos de falla y que nunca se publique con el simulador.</summary>
public sealed class OperadorYGuardaDeEnviosTests
{
    private static DestinatarioParaGuia Destinatario() =>
        DestinatarioParaGuia.Validar("Ana Torres", "Av. Perú 123, Ayacucho", "+51 999 111 222").Destinatario!;

    private static OpcionesDeArranqueDeEnvios Opciones(string? operador = "simulador", string entorno = "Development", bool sinLlamadasReales = false) =>
        new(operador, entorno, sinLlamadasReales);

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public async Task El_simulador_devuelve_la_misma_guia_para_la_misma_clave_sin_duplicar()
    {
        var operador = new SimuladorOperadorLogistico();
        var clave = Guid.NewGuid();

        var uno = await operador.CrearOObtenerGuia(clave, Destinatario());
        var dos = await operador.CrearOObtenerGuia(clave, Destinatario());
        var otra = await operador.CrearOObtenerGuia(Guid.NewGuid(), Destinatario());

        var primera = Assert.IsType<ResultadoDelOperador.GuiaCreada>(uno).Numero;
        Assert.Equal(primera, Assert.IsType<ResultadoDelOperador.GuiaCreada>(dos).Numero);
        Assert.NotEqual(primera, Assert.IsType<ResultadoDelOperador.GuiaCreada>(otra).Numero);
        Assert.Equal(2, operador.GuiasCreadas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public async Task El_simulador_con_peticiones_simultaneas_de_la_misma_clave_crea_una_sola_guia()
    {
        var operador = new SimuladorOperadorLogistico();
        var clave = Guid.NewGuid();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => operador.CrearOObtenerGuia(clave, Destinatario()))));

        Assert.Single(resultados.Select(r => Assert.IsType<ResultadoDelOperador.GuiaCreada>(r).Numero).Distinct());
        Assert.Equal(1, operador.GuiasCreadas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-04")]
    public async Task Los_modos_de_falla_del_simulador_no_crean_guia()
    {
        var operador = new SimuladorOperadorLogistico();
        var clave = Guid.NewGuid();

        operador.Modo = ModoDelSimuladorDeOperador.NoResponde;
        Assert.IsType<ResultadoDelOperador.NoDisponible>(await operador.CrearOObtenerGuia(clave, Destinatario()));
        operador.Modo = ModoDelSimuladorDeOperador.Rechaza;
        Assert.IsType<ResultadoDelOperador.Rechazada>(await operador.CrearOObtenerGuia(clave, Destinatario()));
        operador.Modo = ModoDelSimuladorDeOperador.RespuestaInvalida;
        Assert.IsType<ResultadoDelOperador.RespuestaInvalida>(await operador.CrearOObtenerGuia(clave, Destinatario()));

        Assert.Equal(0, operador.GuiasCreadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-37")]
    public async Task El_numero_de_guia_del_simulador_no_se_repite_tras_un_reinicio_ni_entre_instancias_y_es_idempotente()
    {
        var uno = new SimuladorOperadorLogistico();
        var otraInstancia = new SimuladorOperadorLogistico();
        var clave = Guid.NewGuid();

        var primero = Assert.IsType<ResultadoDelOperador.GuiaCreada>(await uno.CrearOObtenerGuia(clave, Destinatario()));
        var repetido = Assert.IsType<ResultadoDelOperador.GuiaCreada>(await uno.CrearOObtenerGuia(clave, Destinatario()));
        var tras = Assert.IsType<ResultadoDelOperador.GuiaCreada>(await otraInstancia.CrearOObtenerGuia(clave, Destinatario()));
        var distinta = Assert.IsType<ResultadoDelOperador.GuiaCreada>(await otraInstancia.CrearOObtenerGuia(Guid.NewGuid(), Destinatario()));

        Assert.Equal(primero.Numero, repetido.Numero);
        Assert.Equal(primero.Numero, tras.Numero);
        Assert.NotEqual(primero.Numero, distinta.Numero);
        Assert.Matches("^SIM-[0-9A-F]{12}$", primero.Numero.Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public async Task Con_la_respuesta_perdida_el_reintento_con_la_misma_clave_recupera_la_misma_guia()
    {
        var operador = new SimuladorOperadorLogistico { Modo = ModoDelSimuladorDeOperador.RespuestaPerdida };
        var clave = Guid.NewGuid();

        var primero = await operador.CrearOObtenerGuia(clave, Destinatario());
        var reintento = await operador.CrearOObtenerGuia(clave, Destinatario());

        Assert.IsType<ResultadoDelOperador.NoDisponible>(primero);
        Assert.IsType<ResultadoDelOperador.GuiaCreada>(reintento);
        Assert.Equal(1, operador.GuiasCreadas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-04")]
    public async Task El_operador_nulo_falla_cerrado_con_no_disponible()
    {
        var resultado = await new NuloOperadorLogistico().CrearOObtenerGuia(Guid.NewGuid(), Destinatario());

        Assert.IsType<ResultadoDelOperador.NoDisponible>(resultado);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-04")]
    [InlineData("Development")]
    [InlineData("Production")]
    [Trait("Criterio", "AC-PAG-37")]
    public void El_simulador_se_acepta_en_desarrollo_pruebas_staging_y_produccion_porque_no_habra_operador_real(string entorno)
    {
        Assert.Equal("simulador", GuardaDeArranqueDeEnvios.Exigir(Opciones(entorno: entorno)));
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-04")]
    [InlineData("")]
    [InlineData("Qa")]
    public void El_simulador_se_rechaza_en_cualquier_otro_entorno_distinto_de_los_cuatro(string entorno)
    {
        var error = Assert.Throws<ConfiguracionDeEnviosInvalidaException>(() => GuardaDeArranqueDeEnvios.Exigir(Opciones(entorno: entorno)));

        Assert.Contains("simulador", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-04")]
    public void Con_un_modo_que_prohibe_llamadas_reales_solo_se_admite_el_nulo()
    {
        Assert.Throws<ConfiguracionDeEnviosInvalidaException>(() => GuardaDeArranqueDeEnvios.Exigir(Opciones(sinLlamadasReales: true)));
        Assert.Equal("nulo", GuardaDeArranqueDeEnvios.Exigir(Opciones(operador: null, entorno: "Production", sinLlamadasReales: true)));
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-04")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("NULO")]
    public void Sin_valor_o_con_el_nulo_el_arranque_elige_el_operador_nulo_en_cualquier_entorno(string? operador)
    {
        Assert.Equal("nulo", GuardaDeArranqueDeEnvios.Exigir(Opciones(operador, "Production")));
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-04")]
    [InlineData("olva")]
    [InlineData("shalom")]
    public void Un_valor_desconocido_o_un_operador_real_inexistente_rechaza_el_arranque_sin_caer_al_nulo(string operador)
    {
        var error = Assert.Throws<ConfiguracionDeEnviosInvalidaException>(() => GuardaDeArranqueDeEnvios.Exigir(Opciones(operador)));

        Assert.DoesNotContain(operador, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-04")]
    public void La_composicion_registra_el_operador_que_decide_la_guarda()
    {
        var nulo = new ServiceCollection().AgregarOperadorLogisticoDeEnvios(Opciones(null, "Production")).BuildServiceProvider();
        var simulador = new ServiceCollection().AgregarOperadorLogisticoDeEnvios(Opciones()).BuildServiceProvider();

        Assert.IsType<NuloOperadorLogistico>(nulo.GetRequiredService<IOperadorLogistico>());
        Assert.IsType<SimuladorOperadorLogistico>(simulador.GetRequiredService<IOperadorLogistico>());
        // El simulador también corre en Production (no hay operador real, DD-22, DD-88); un entorno que no es ninguno de los cuatro sigue rechazándolo.
        Assert.IsType<SimuladorOperadorLogistico>(new ServiceCollection().AgregarOperadorLogisticoDeEnvios(Opciones(entorno: "Production")).BuildServiceProvider().GetRequiredService<IOperadorLogistico>());
        Assert.Throws<ConfiguracionDeEnviosInvalidaException>(() => new ServiceCollection().AgregarOperadorLogisticoDeEnvios(Opciones(entorno: "Qa")));
    }
}
