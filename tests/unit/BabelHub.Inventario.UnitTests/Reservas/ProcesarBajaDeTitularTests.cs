using System.Security.Cryptography;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Reservas;

internal sealed class SustitutorDeLimitesDoble : ISustitutorDeSujetoDeLimites
{
    public List<(string Actual, string Nuevo)> Llamadas { get; } = [];

    public Task<int> Sustituir(string sujetoActual, string sujetoNuevo, CancellationToken cancelacion = default)
    {
        Llamadas.Add((sujetoActual, sujetoNuevo));
        return Task.FromResult(0);
    }
}

/// <summary>Un identificador anónimo de prueba: estable por titular, distinto entre titulares y no invertible a simple vista (SHA-256 truncado con una sal de la prueba).</summary>
internal sealed class GeneradorAnonimoDoble : IGeneradorDeIdentificadorAnonimo
{
    public Guid Generar(Guid titularId) => new(SHA256.HashData([.. "sal-de-prueba"u8, .. titularId.ToByteArray()])[..16]);
}

/// <summary>T23: <c>ProcesarBajaDeTitular</c> (SEL-34, SEL-35, AC-SEL-36): una sola transacción que sustituye el titular, anula la huella, audita una vez y avisa, sin rechazar, de más de 20 bajas por minuto.</summary>
public sealed class ProcesarBajaDeTitularTests
{
    private sealed class Contexto
    {
        public Contexto(ActorDePrueba? actor = null)
        {
            Entorno = actor is null ? new EntornoDeReservas(ActorDePrueba.DeIntegracion("baja-de-titular")) : new EntornoDeReservas(actor);
            Volumen = new VolumenDeBajasDeTitular(Entorno.Base.Reloj);
            Caso = new ProcesarBajaDeTitular(
                Entorno.Autorizador,
                new ValidadorDeProcesarBajaDeTitular(),
                Entorno.Bloqueos,
                Entorno.Reservas,
                Limites,
                Generador,
                new AuditoriaDeBajasRecibidas(Entorno.Auditoria, Entorno.Base.Actor),
                Volumen,
                Entorno.Alertas,
                Entorno.Base.Reloj);
        }

        public EntornoDeReservas Entorno { get; }

        public SustitutorDeLimitesDoble Limites { get; } = new();

        public GeneradorAnonimoDoble Generador { get; } = new();

        public VolumenDeBajasDeTitular Volumen { get; }

        public ProcesarBajaDeTitular Caso { get; }

        public Task<Resultado<BajaDeTitularProcesada>> Baja(Guid titular) => Caso.Ejecutar(new ProcesarBajaDeTitularEntrada(titular));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task LaBaja_SustituyeElTitularPorUnAnonimoEstable_YNoTocaLineasNiStock()
    {
        var c = new Contexto();
        var e = c.Entorno;
        var libro = e.Impresa();
        var titular = Guid.NewGuid();
        var otro = Guid.NewGuid();
        var reserva = e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Lima, 2)], clienteId: titular);
        Assert.True(reserva.Confirmar(e.Base.Reloj.Ahora));
        var deOtro = e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Lima, 1)], clienteId: otro);
        e.Base.Existencias.Poner(e.Base.Lima.Id, libro, 10, 3);

        var resultado = await c.Baja(titular);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.False(resultado.Valor.EraRepeticion);
        var anonimo = c.Generador.Generar(titular);
        Assert.NotEqual(titular, anonimo);
        Assert.NotEqual(c.Generador.Generar(otro), anonimo);
        var guardada = e.Reservas.Guardadas.Single(r => r.PedidoId == reserva.PedidoId);
        Assert.Equal(anonimo, guardada.ClienteId);
        Assert.Equal(otro, e.Reservas.Guardadas.Single(r => r.PedidoId == deOtro.PedidoId).ClienteId);
        Assert.Equal([(EstadoDeLinea.Confirmada, 2)], guardada.Lineas.Select(l => (l.Estado, l.Cantidad)));
        Assert.Equal((10, 3), e.Base.Existencias.Filas[(e.Base.Lima.Id, libro)]);
        Assert.Empty(e.Base.Existencias.Movimientos);
        Assert.Equal([(titular.ToString("D"), anonimo.ToString("D"))], c.Limites.Llamadas);
        Assert.Contains(titular, e.Reservas.Bajas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task LaBaja_TomaElBloqueoDelClienteAntesDeEscribir()
    {
        var c = new Contexto();

        await c.Baja(Guid.NewGuid());

        var llamadas = c.Entorno.Base.Bitacora.Llamadas;
        Assert.True(llamadas.IndexOf("bloqueo.Cliente") < llamadas.IndexOf("reservas.RegistrarBaja"));
        Assert.True(llamadas.IndexOf("reservas.RegistrarBaja") < llamadas.IndexOf("reservas.AnonimizarCliente"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task UnaAuditoriaUnicamente_ConElAnonimoComoObjeto_YElActorDeIntegracion()
    {
        var c = new Contexto();
        var titular = Guid.NewGuid();

        await c.Baja(titular);

        var (solicitud, actor) = Assert.Single(c.Entorno.Auditoria.Registros);
        Assert.Equal(AccionesAuditables.BajaDeTitularProcesada, solicitud.Accion);
        Assert.Equal(TipoDeActor.Integracion, actor);
        Assert.Equal(c.Generador.Generar(titular).ToString("D"), solicitud.ObjetoId);
        Assert.DoesNotContain(titular.ToString("D"), solicitud.ObjetoId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task RepetirLaBaja_NoCambiaNada_NiAudita_PeroVuelveASustituir()
    {
        var c = new Contexto();
        var e = c.Entorno;
        var titular = Guid.NewGuid();
        e.Pendiente(Guid.NewGuid(), [(e.Impresa(), e.Base.Lima, 1)], clienteId: titular);

        var primera = await c.Baja(titular);
        var antes = e.Reservas.Guardadas.Select(r => r.ClienteId).ToList();
        var segunda = await c.Baja(titular);

        Assert.False(primera.Valor.EraRepeticion);
        Assert.True(segunda.Valor.EraRepeticion);
        Assert.Equal(antes, e.Reservas.Guardadas.Select(r => r.ClienteId));
        Assert.Single(e.Auditoria.Registros);
        Assert.Equal(2, c.Limites.Llamadas.Count);
        Assert.Single(e.Reservas.BajasEntregadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task UnTitularSinReservas_TerminaSinError_YQuedaRegistrado()
    {
        var c = new Contexto();
        var titular = Guid.NewGuid();

        var resultado = await c.Baja(titular);

        Assert.True(resultado.EsExito);
        Assert.Contains(titular, c.Entorno.Reservas.Bajas);
        Assert.Single(c.Entorno.Auditoria.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task MasDeVeinteBajasPorMinuto_SoloAvisan_LaVeintiunaSeProcesa_YElAvisoSaleUnaVez()
    {
        var c = new Contexto();
        var titulares = Enumerable.Range(0, 22).Select(_ => Guid.NewGuid()).ToList();

        foreach (var titular in titulares.Take(20))
        {
            Assert.True((await c.Baja(titular)).EsExito);
        }

        Assert.Empty(c.Entorno.Alertas.Tecnicas);

        Assert.True((await c.Baja(titulares[20])).EsExito);
        Assert.True((await c.Baja(titulares[21])).EsExito);

        Assert.Equal(["motivo-de-alerta-no-catalogado"], c.Entorno.Alertas.Tecnicas);
        Assert.Equal(22, c.Entorno.Reservas.Bajas.Count);
        Assert.Equal(22, c.Entorno.Auditoria.Registros.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task ConElMotivoYaCatalogado_LaAlertaLlevaElMotivoPropio()
    {
        var c = new Contexto();
        c.Entorno.Alertas.Catalogados.Add(VolumenDeBajasDeTitular.Motivo);

        for (var i = 0; i < 21; i++)
        {
            await c.Baja(Guid.NewGuid());
        }

        Assert.Equal([VolumenDeBajasDeTitular.Motivo], c.Entorno.Alertas.Tecnicas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task ElMinutoSiguienteElContadorEmpiezaDeNuevo()
    {
        var c = new Contexto();
        for (var i = 0; i < 20; i++)
        {
            await c.Baja(Guid.NewGuid());
        }

        c.Entorno.Base.Reloj.Ahora = c.Entorno.Base.Reloj.Ahora.AddMinutes(1);
        for (var i = 0; i < 20; i++)
        {
            await c.Baja(Guid.NewGuid());
        }

        Assert.Empty(c.Entorno.Alertas.Tecnicas);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-36")]
    [InlineData("cliente")]
    [InlineData("otra-integracion")]
    [InlineData("administracion")]
    [InlineData("sin-actor")]
    public async Task SinLaCredencialDeSuAlcance_SeRechaza_SinLeerNiEscribirNada(string quien)
    {
        var actor = quien switch
        {
            "cliente" => ActorDePrueba.DeCliente(),
            "otra-integracion" => ActorDePrueba.DeIntegracion("consulta-de-pendientes"),
            "administracion" => ActorDePrueba.DeAdministradorCentral(),
            _ => ActorDePrueba.SinContexto(),
        };
        var c = new Contexto(actor);

        var resultado = await c.Baja(Guid.NewGuid());

        Assert.Equal(quien == "sin-actor" ? CategoriaDeError.NoAutenticado : CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.DoesNotContain(c.Entorno.Base.Bitacora.Llamadas, l => !l.StartsWith("denegacion.", StringComparison.Ordinal));
        Assert.Empty(c.Entorno.Reservas.Bajas);
        Assert.Empty(c.Limites.Llamadas);
        Assert.Empty(c.Entorno.Auditoria.Registros);
        Assert.Single(c.Entorno.Base.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task UnTitularVacio_EsValidacion()
    {
        var c = new Contexto();

        var resultado = await c.Baja(Guid.Empty);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Empty(c.Entorno.Reservas.Bajas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task SiNoSePuedeAuditar_LaBajaFalla_ParaQueLaTransaccionLaDeshaga()
    {
        var c = new Contexto();
        c.Entorno.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => c.Baja(Guid.NewGuid()));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task LasBajasPendientesSeEntreganAAuditoriaYSeMarcan_UnaSolaVez()
    {
        var c = new Contexto();
        var titulares = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var titular in titulares)
        {
            await c.Baja(titular);
        }

        var bajas = new BajasRecibidasDeReservas(c.Entorno.Reservas, c.Entorno.Base.Reloj);
        var primeras = await bajas.Pendientes(2);
        await bajas.MarcarEntregadas(primeras);
        var resto = await bajas.Pendientes(10);

        Assert.Equal(2, primeras.Count);
        Assert.Equal(titulares.Except(primeras), resto);
        Assert.All(primeras, p => Assert.Equal(c.Entorno.Base.Reloj.Ahora, c.Entorno.Reservas.BajasEntregadas[p]));

        // Repetir la marca no cambia la fecha de las ya entregadas.
        c.Entorno.Base.Reloj.Ahora = c.Entorno.Base.Reloj.Ahora.AddDays(1);
        await bajas.MarcarEntregadas(primeras);
        Assert.All(primeras, p => Assert.NotEqual(c.Entorno.Base.Reloj.Ahora, c.Entorno.Reservas.BajasEntregadas[p]));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public void ElIdentificadorDelTitularNuncaApareceEnLaCadena()
    {
        var titular = Guid.NewGuid();

        Assert.DoesNotContain(titular.ToString(), new ProcesarBajaDeTitularEntrada(titular).ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
