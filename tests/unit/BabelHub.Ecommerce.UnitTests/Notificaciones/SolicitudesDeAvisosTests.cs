using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Notificaciones.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Notificaciones;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.UnitTests.Identidad;
using BabelHub.Ecommerce.UnitTests.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Notificaciones;

/// <summary>
/// Solicitar, cancelar y desactivar avisos (transversal-notificaciones T03 a T07; AC-NOT-01, AC-NOT-02, AC-NOT-04, AC-NOT-06, AC-NOT-07, AC-NOT-08, AC-NOT-13; NOT-01 a NOT-05, NOT-07, NOT-08, NOT-13): el aviso se guarda sin red,
/// con campos cerrados, omitido si no hay consentimiento, una sola vez por evento, cancelado al cambiar el destinatario y el número desactivado se excluye escrito de cualquier forma, con auditoría.
/// </summary>
public sealed class SolicitudesDeAvisosTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------- dobles

    internal sealed class AvisosEnMemoria : IRepositorioDeAvisos
    {
        private readonly object _candado = new();

        public List<Aviso> Guardados { get; } = [];

        public bool Falla { get; set; }

        public int Intentos { get; private set; }

        public Task<bool> InsertarSiNoExiste(Aviso aviso, CancellationToken cancelacion = default)
        {
            lock (_candado)
            {
                Intentos++;
                if (Falla)
                {
                    throw new InvalidOperationException("La base no responde.");
                }

                if (Guardados.Any(a => a.EventoId == aviso.EventoId && a.DestinatarioRef == aviso.DestinatarioRef && a.Plantilla == aviso.Plantilla))
                {
                    return Task.FromResult(false);
                }

                Guardados.Add(aviso);
                return Task.FromResult(true);
            }
        }

        public Task<IReadOnlyList<Aviso>> NoFinalesDe(TipoDeDestinatario tipo, string destinatarioRef, CancellationToken cancelacion = default)
        {
            if (Falla)
            {
                throw new InvalidOperationException("La base no responde.");
            }

            return Task.FromResult<IReadOnlyList<Aviso>>([.. Guardados.Where(a => a.Tipo == tipo && a.DestinatarioRef == destinatarioRef && !a.EsFinal)]);
        }

        public Task Guardar(Aviso aviso, CancellationToken cancelacion = default) => Task.CompletedTask;

        /// <summary>Avisos que «otro» (la cancelación de Identidad) terminó en la base mientras el trabajo los atendía: la escritura condicional no los toca.</summary>
        public HashSet<Guid> TerminadosPorOtro { get; } = [];

        public int EscriturasCondicionales { get; private set; }

        public Task<bool> GuardarSiNoEsFinal(Aviso aviso, CancellationToken cancelacion = default)
        {
            EscriturasCondicionales++;
            return Task.FromResult(!TerminadosPorOtro.Contains(aviso.Id));
        }

        public Task<Aviso?> PorId(Guid id, bool paraActualizar, CancellationToken cancelacion = default) => Task.FromResult(Guardados.FirstOrDefault(a => a.Id == id));

        public Task<IReadOnlyList<Aviso>> TomarParaEnviar(DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default) =>
            Task.FromResult<IReadOnlyList<Aviso>>([.. Guardados
                .Where(a => !a.EsFinal && (ahora >= a.VenceEn || (a.Estado == EstadoDelAviso.Pendiente && ahora >= a.ProximaFecha) || (a.Estado == EstadoDelAviso.Enviando && a.ArrendadoHasta is { } hasta && ahora >= hasta)))
                .OrderBy(a => a.Creado)
                .Take(maximo)]);

        public Task<PaginaDeAvisosAlmacenados> Listar(EstadoDelAviso? estado, int pagina, int tamano, CancellationToken cancelacion = default)
        {
            var filtrados = Guardados.Where(a => estado is null || a.Estado == estado).OrderByDescending(a => a.Creado).ToList();
            return Task.FromResult(new PaginaDeAvisosAlmacenados([.. filtrados.Skip((pagina - 1) * tamano).Take(tamano)], filtrados.Count));
        }

        public Task<int> PurgarFinalesAnterioresA(DateTimeOffset limite, CancellationToken cancelacion = default) =>
            Task.FromResult(Guardados.RemoveAll(a => a.EsFinal && a.FechaFinal < limite));
    }

    internal sealed class EstadoDeCuentaFalso : IEstadoDeCuenta
    {
        public Dictionary<Guid, EstadoDeCuenta> Cuentas { get; } = [];

        public bool Falla { get; set; }

        public int Consultas { get; private set; }

        public Task<EstadoDeCuenta?> Consultar(Guid usuarioId, CancellationToken cancelacion = default)
        {
            Consultas++;
            return Falla ? throw new InvalidOperationException("Identidad no responde.") : Task.FromResult(Cuentas.GetValueOrDefault(usuarioId));
        }

        public Task<ResultadoDeLaAceptacion> AceptarLosTextos(Guid usuarioId, string versionMostrada, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLaAceptacion.NoDisponible);

        public Guid Con(bool consiente, bool cancelada = false)
        {
            var id = Guid.NewGuid();
            Cuentas[id] = new EstadoDeCuenta(id, true, EstadoDeAceptacionDeLosTextos.AlDia, true, cancelada, consiente);
            return id;
        }
    }

    internal sealed class HuellasDePrueba : IHuellasDeNumeros
    {
        public int VersionActual { get; set; } = 2;

        private static HuellaDeNumero De(int version, NumeroDeDestino numero) => new(version, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"v{version}|{numero.E164}"))).ToLowerInvariant());

        public HuellaDeNumero Actual(NumeroDeDestino numero) => De(VersionActual, numero);

        public IReadOnlyList<HuellaDeNumero> Vigentes(NumeroDeDestino numero) => [.. Enumerable.Range(1, VersionActual).Select(v => De(v, numero))];

        public HuellaDeSujeto ParaElTope(NumeroDeDestino numero) => HuellaDeSujeto.Crear(Actual(numero).Valor[..32], "v1");
    }

    internal sealed class DesactivadosEnMemoria : IRepositorioDeNumerosDesactivados
    {
        public List<NumeroDesactivado> Guardados { get; private set; } = [];

        public Task<bool> Agregar(NumeroDesactivado numero, CancellationToken cancelacion = default)
        {
            if (Guardados.Any(g => g.Huella == numero.Huella))
            {
                return Task.FromResult(false);
            }

            Guardados.Add(numero);
            return Task.FromResult(true);
        }

        public Task<bool> ExisteAlguna(IReadOnlyList<HuellaDeNumero> huellas, CancellationToken cancelacion = default) => Task.FromResult(Guardados.Any(g => huellas.Contains(g.Huella)));

        public Task<int> Eliminar(IReadOnlyList<HuellaDeNumero> huellas, CancellationToken cancelacion = default) => Task.FromResult(Guardados.RemoveAll(g => huellas.Contains(g.Huella)));

        public List<NumeroDesactivado> Instantanea() => [.. Guardados];

        public void Restaurar(List<NumeroDesactivado> copia) => Guardados = copia;
    }

    /// <summary>Una unidad de trabajo que revierte el almacén de números si el trabajo lanza (como una transacción).</summary>
    internal sealed class UnidadReversible(DesactivadosEnMemoria desactivados) : IUnidadDeTrabajo
    {
        public async Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default)
        {
            var copia = desactivados.Instantanea();
            try
            {
                return await trabajo(cancelacion);
            }
            catch
            {
                desactivados.Restaurar(copia);
                throw;
            }
        }

        public Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default) => bloque(cancelacion);
    }

    internal sealed class Entorno
    {
        public Entorno()
        {
            Motor = new MotorDeMemoria(Reloj);
            Limites = new EvaluarLimite(new CatalogoDePoliticas(PoliticasDeSeguridad.Todas), Motor, null, new OrigenFijo(OrigenDeSolicitud.Desconocido), EventosAutomaticos, Reloj);
            Solicitar = new SolicitarAviso(Avisos, Cuentas, Alertas, Reloj);
            Cancelar = new CancelarAvisosPendientes(Avisos, Reloj, new TopeDeAvisosPorNumero(Limites, Huellas));
            Desactivar = new DesactivarAvisosDeUnNumero(Actor, Denegaciones, Huellas, Desactivados, new UnidadReversible(Desactivados), Auditoria, Reloj);
        }

        public RelojFijo Reloj { get; } = new(Inicio);

        public MotorDeMemoria Motor { get; }

        public PublicadorDeEventosEspia EventosAutomaticos { get; } = new();

        public EvaluarLimite Limites { get; }

        public AvisosEnMemoria Avisos { get; } = new();

        public EstadoDeCuentaFalso Cuentas { get; } = new();

        public AlertasDeAuditoriaDePrueba Alertas { get; } = new();

        public HuellasDePrueba Huellas { get; } = new();

        public DesactivadosEnMemoria Desactivados { get; } = new();

        public ActorDePrueba Actor { get; } = new();

        public DenegacionesDePrueba Denegaciones { get; } = new();

        public AuditoriaDePrueba Auditoria { get; } = new(new BdIdentidad());

        public SolicitarAviso Solicitar { get; }

        public CancelarAvisosPendientes Cancelar { get; }

        public DesactivarAvisosDeUnNumero Desactivar { get; }
    }

    private static SolicitudDeAviso Listo(Guid cuenta, string evento = "pedido:123:listo-para-recojo", string pedido = "BH-000123") =>
        new(evento, DestinatarioDelAviso.Cliente, cuenta.ToString("D"), "pedido-listo-para-recojo", new Dictionary<string, string> { ["numeroDePedido"] = pedido, ["nombre"] = "María" });

    private static SolicitudDeAviso Traslado(string sucursal, string evento = "traslado:7:aprobado") =>
        new(evento, DestinatarioDelAviso.Sucursal, sucursal, "traslado-cambio-de-estado", new Dictionary<string, string> { ["numeroDeTraslado"] = "7", ["rolDeLaSucursal"] = "destino", ["cantidad"] = "3", ["estado"] = "aprobado" });

    // ---------------------------------------------------------------- solicitar (T04)

    [Fact]
    [Trait("Criterio", "AC-NOT-01")]
    public async Task UnClienteConConsentimiento_RecibeSuAviso_PendienteConSusCampos_SinNumeroYSinRed()
    {
        var e = new Entorno();
        var cuenta = e.Cuentas.Con(consiente: true);

        var resultado = await e.Solicitar.Solicitar(Listo(cuenta));

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.Creado, resultado);
        var aviso = Assert.Single(e.Avisos.Guardados);
        Assert.Equal(EstadoDelAviso.Pendiente, aviso.Estado);
        Assert.Equal(cuenta.ToString("D"), aviso.DestinatarioRef);
        Assert.Equal("BH-000123", aviso.Campos["numeroDePedido"]);
        Assert.Equal(Inicio, aviso.Creado);
        Assert.Empty(e.Alertas.Tecnicas);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-02")]
    public async Task UnClienteSinConsentimiento_QuedaRegistradoComoOmitido_YNoSeEnvia()
    {
        var e = new Entorno();
        var cuenta = e.Cuentas.Con(consiente: false);

        var resultado = await e.Solicitar.Solicitar(Listo(cuenta));

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.OmitidoSinConsentimiento, resultado);
        var aviso = Assert.Single(e.Avisos.Guardados);
        Assert.Equal(EstadoDelAviso.OmitidoSinConsentimiento, aviso.Estado);
        Assert.Equal(Inicio, aviso.FechaFinal);
        Assert.Empty(aviso.Campos);
    }

    [Theory]
    [InlineData("inexistente")]
    [InlineData("cancelada")]
    public async Task UnaCuentaInexistenteOCancelada_NoRecibeAvisos(string caso)
    {
        var e = new Entorno();
        var cuenta = caso == "cancelada" ? e.Cuentas.Con(consiente: true, cancelada: true) : Guid.NewGuid();

        var resultado = await e.Solicitar.Solicitar(Listo(cuenta));

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.OmitidoSinConsentimiento, resultado);
        Assert.Equal(EstadoDelAviso.OmitidoSinConsentimiento, Assert.Single(e.Avisos.Guardados).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-08")]
    public async Task UnaSucursal_NoNecesitaConsentimiento_YSeAvisaALaDeOrigenYALaDeDestino()
    {
        var e = new Entorno();

        var destino = await e.Solicitar.Solicitar(Traslado("AYACUCHO"));
        var origen = await e.Solicitar.Solicitar(Traslado("HUAMANGA"));

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.Creado, destino);
        Assert.Equal(ResultadoDeLaSolicitudDeAviso.Creado, origen);
        Assert.Equal(["AYACUCHO", "HUAMANGA"], e.Avisos.Guardados.Select(a => a.DestinatarioRef).Order());
        Assert.Equal(0, e.Cuentas.Consultas);
        Assert.All(e.Avisos.Guardados, a => Assert.Equal(TipoDeDestinatario.Sucursal, a.Tipo));
    }

    [Theory]
    [InlineData("plantilla-que-no-existe")]
    [InlineData("traslado-cambio-de-estado")]
    [Trait("Criterio", "AC-NOT-06")]
    public async Task UnaPlantillaInexistenteOParaOtroTipoDeDestinatario_SeRechaza_SinTocarLaBaseNiIdentidad(string plantilla)
    {
        var e = new Entorno();
        var cuenta = e.Cuentas.Con(consiente: true);

        var resultado = await e.Solicitar.Solicitar(Listo(cuenta) with { Plantilla = plantilla });

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.Rechazado, resultado);
        Assert.Equal(0, e.Avisos.Intentos);
        Assert.Equal(0, e.Cuentas.Consultas);
    }

    [Theory]
    [InlineData("enlaceDeDescarga", "mi-archivo")]
    [InlineData("numeroDePedido", "https://tienda.example/descarga")]
    [InlineData("numeroDePedido", "4111111111111111")]
    [InlineData("nombre", "María José")]
    [InlineData("nombre", "juan@example.com")]
    [Trait("Criterio", "AC-NOT-06")]
    public async Task UnCampoFueraDelCatalogoOUnValorConEnlaceTarjetaOTextoLibre_SeRechaza_SinGuardarNada(string campo, string valor)
    {
        var e = new Entorno();
        var cuenta = e.Cuentas.Con(consiente: true);
        var solicitud = Listo(cuenta);
        var campos = new Dictionary<string, string>(solicitud.Campos) { [campo] = valor };

        var resultado = await e.Solicitar.Solicitar(solicitud with { Campos = campos });

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.Rechazado, resultado);
        Assert.Empty(e.Avisos.Guardados);
        Assert.Equal(0, e.Avisos.Intentos);
    }

    [Theory]
    [InlineData("no-es-un-evento")]
    [InlineData("pedido:1:listo para recojo")]
    public async Task UnEventoSinElFormatoCerrado_SeRechaza(string evento)
    {
        var e = new Entorno();
        var cuenta = e.Cuentas.Con(consiente: true);

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.Rechazado, await e.Solicitar.Solicitar(Listo(cuenta, evento)));
        Assert.Empty(e.Avisos.Guardados);
    }

    [Theory]
    [InlineData("+51999111222")]
    [InlineData("persona@example.com")]
    [InlineData("no-es-un-guid-de-cuenta")]
    public async Task UnDestinatarioClienteQueNoEsElIdentificadorDeUnaCuenta_SeRechaza_NuncaSeGuardaUnNumero(string referencia)
    {
        var e = new Entorno();
        var cuenta = e.Cuentas.Con(consiente: true);

        var resultado = await e.Solicitar.Solicitar(Listo(cuenta) with { DestinatarioRef = referencia });

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.Rechazado, resultado);
        Assert.Empty(e.Avisos.Guardados);
    }

    [Fact]
    public async Task UnaSolicitudNula_SeRechaza_SinLanzar()
    {
        var e = new Entorno();

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.Rechazado, await e.Solicitar.Solicitar(null!));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-01")]
    public async Task SiLaBaseFalla_NuncaLanza_PublicaLaAlertaTecnica_YDevuelveQueNoSeGuardo()
    {
        var e = new Entorno();
        var cuenta = e.Cuentas.Con(consiente: true);
        e.Avisos.Falla = true;

        var resultado = await e.Solicitar.Solicitar(Listo(cuenta));

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.NoSeGuardo, resultado);
        Assert.Equal(SolicitarAviso.MotivoDeFallaPropia, Assert.Single(e.Alertas.Tecnicas));
    }

    [Fact]
    public async Task SiIdentidadNoResponde_ElAvisoNoSeCreaSinConocerElConsentimiento_YNuncaLanza()
    {
        var e = new Entorno();
        var cuenta = e.Cuentas.Con(consiente: true);
        e.Cuentas.Falla = true;

        var resultado = await e.Solicitar.Solicitar(Listo(cuenta));

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.NoSeGuardo, resultado);
        Assert.Empty(e.Avisos.Guardados);
        Assert.Single(e.Alertas.Tecnicas);
    }

    [Fact]
    public async Task SiLaAlertaTecnicaTambienFalla_NadaLanza()
    {
        var e = new Entorno();
        var cuenta = e.Cuentas.Con(consiente: true);
        e.Avisos.Falla = true;
        e.Alertas.Falla = true;

        // La alerta técnica de prueba solo falla las de seguridad: se simula con una que lanza en las técnicas.
        var solicitar = new SolicitarAviso(e.Avisos, e.Cuentas, new AlertasQueLanzan(), e.Reloj);

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.NoSeGuardo, await solicitar.Solicitar(Listo(cuenta)));
    }

    private sealed class AlertasQueLanzan : IAlertas
    {
        public Task PublicarTecnica(CodigoDeAlerta motivo, CancellationToken cancelacion = default) => throw new InvalidOperationException("Sin registro técnico.");

        public Task PublicarDeSeguridad(CodigoDeAlerta motivo, CancellationToken cancelacion = default) => throw new InvalidOperationException("Sin registro técnico.");
    }

    // ---------------------------------------------------------------- unicidad (T05)

    [Fact]
    [Trait("Criterio", "AC-NOT-04")]
    public async Task ElMismoEventoReportadoDosVeces_DejaUnSoloAviso()
    {
        var e = new Entorno();
        var cuenta = e.Cuentas.Con(consiente: true);

        var primera = await e.Solicitar.Solicitar(Listo(cuenta));
        var segunda = await e.Solicitar.Solicitar(Listo(cuenta));

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.Creado, primera);
        Assert.Equal(ResultadoDeLaSolicitudDeAviso.YaExistia, segunda);
        Assert.Single(e.Avisos.Guardados);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-04")]
    public async Task ElMismoEventoParaOtroDestinatarioOOtraPlantilla_SiGeneraOtroAviso()
    {
        var e = new Entorno();
        var uno = e.Cuentas.Con(consiente: true);
        var otro = e.Cuentas.Con(consiente: true);

        await e.Solicitar.Solicitar(Listo(uno));
        await e.Solicitar.Solicitar(Listo(otro));
        await e.Solicitar.Solicitar(new SolicitudDeAviso("pedido:123:listo-para-recojo", DestinatarioDelAviso.Cliente, uno.ToString("D"), "pedido-confirmado", new Dictionary<string, string> { ["numeroDePedido"] = "BH-000123" }));

        Assert.Equal(3, e.Avisos.Guardados.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-04")]
    public async Task UnClienteSinConsentimiento_ReportadoDosVeces_DejaUnSoloRegistroOmitido()
    {
        var e = new Entorno();
        var cuenta = e.Cuentas.Con(consiente: false);

        await e.Solicitar.Solicitar(Listo(cuenta));
        var segunda = await e.Solicitar.Solicitar(Listo(cuenta));

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.YaExistia, segunda);
        Assert.Single(e.Avisos.Guardados);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-04")]
    public async Task CienSolicitudesSimultaneasDelMismoEvento_DejanUnSoloAviso()
    {
        var e = new Entorno();
        var cuenta = e.Cuentas.Con(consiente: true);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() => e.Solicitar.Solicitar(Listo(cuenta)))));

        Assert.Single(e.Avisos.Guardados);
        Assert.Equal(1, resultados.Count(r => r == ResultadoDeLaSolicitudDeAviso.Creado));
        Assert.Equal(99, resultados.Count(r => r == ResultadoDeLaSolicitudDeAviso.YaExistia));
    }

    [Theory]
    [InlineData("N")]
    [InlineData("D-mayusculas")]
    [Trait("Criterio", "AC-NOT-04")]
    public async Task LaReferenciaDelClienteSeGuardaCanonica_ElMismoEventoEscritoDeOtraFormaNoDuplica_YLaCancelacionLoEncuentra(string forma)
    {
        var e = new Entorno();
        var cuenta = e.Cuentas.Con(consiente: true);
        var escrita = forma switch
        {
            "N" => cuenta.ToString("N"),
            _ => cuenta.ToString("D").ToUpperInvariant(),
        };

        await e.Solicitar.Solicitar(Listo(cuenta));
        var otra = await e.Solicitar.Solicitar(Listo(cuenta) with { DestinatarioRef = escrita });

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.YaExistia, otra);
        Assert.Equal(cuenta.ToString("D"), Assert.Single(e.Avisos.Guardados).DestinatarioRef);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-07")]
    public async Task UnAvisoPedidoConLaCuentaEnMayusculas_QuedaCancelado_AlCancelarLaCuenta()
    {
        var e = new Entorno();
        var cuenta = e.Cuentas.Con(consiente: true);
        await e.Solicitar.Solicitar(Listo(cuenta) with { DestinatarioRef = cuenta.ToString("N").ToUpperInvariant() });

        await e.Cancelar.CancelarLosAvisosDe(cuenta);

        Assert.Equal(EstadoDelAviso.Cancelado, Assert.Single(e.Avisos.Guardados).Estado);
    }

    [Theory]
    [InlineData("numeroDeTraslado", "TR3f2c9a8e0000123456")]
    [Trait("Criterio", "AC-NOT-06")]
    public async Task UnIdentificadorMixtoConMuchosDigitos_NoSeRechazaPorParecerUnaTarjeta(string campo, string valor)
    {
        var e = new Entorno();
        var solicitud = Traslado("AYACUCHO");

        var resultado = await e.Solicitar.Solicitar(solicitud with { Campos = new Dictionary<string, string>(solicitud.Campos) { [campo] = valor } });

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.Creado, resultado);
    }

    [Theory]
    [InlineData("4111111111111111")]
    [InlineData("4111-1111-1111-1111")]
    [Trait("Criterio", "AC-NOT-06")]
    public async Task UnIdentificadorSoloDeDigitosQueParecenUnaTarjeta_SiSeRechaza(string valor)
    {
        var e = new Entorno();
        var solicitud = Traslado("AYACUCHO");

        var resultado = await e.Solicitar.Solicitar(solicitud with { Campos = new Dictionary<string, string>(solicitud.Campos) { ["numeroDeTraslado"] = valor } });

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.Rechazado, resultado);
    }

    [Fact]
    public async Task UnaSolicitudRechazada_DejaUnaAlertaTecnica_PorqueEsUnErrorDelModuloQueLlama()
    {
        var e = new Entorno();

        await e.Solicitar.Solicitar(Listo(Guid.NewGuid()) with { Plantilla = "no-existe" });

        Assert.Equal(SolicitarAviso.MotivoDeFallaPropia, Assert.Single(e.Alertas.Tecnicas));
    }

    // ---------------------------------------------------------------- cancelar (T06)

    [Fact]
    [Trait("Criterio", "AC-NOT-07")]
    public async Task AlCambiarElConsentimientoElNumeroOLaCuenta_LosAvisosPendientesDelClienteQuedanCancelados()
    {
        var e = new Entorno();
        var cuenta = e.Cuentas.Con(consiente: true);
        var otra = e.Cuentas.Con(consiente: true);
        await e.Solicitar.Solicitar(Listo(cuenta, "pedido:1:listo-para-recojo"));
        await e.Solicitar.Solicitar(Listo(cuenta, "pedido:2:listo-para-recojo"));
        await e.Solicitar.Solicitar(Listo(otra));
        await e.Solicitar.Solicitar(Traslado("AYACUCHO"));
        var enEnvio = e.Avisos.Guardados[1];
        enEnvio.IniciarEnvio(Inicio, Inicio.AddMinutes(2));
        e.Reloj.Ahora = Inicio.AddMinutes(1);

        await e.Cancelar.CancelarLosAvisosDe(cuenta);

        Assert.Equal(EstadoDelAviso.Cancelado, e.Avisos.Guardados[0].Estado);
        Assert.Equal(EstadoDelAviso.Cancelado, enEnvio.Estado);
        Assert.Equal(CancelarAvisosPendientes.MotivoDeCancelacion, e.Avisos.Guardados[0].MotivoDeCierre);
        Assert.Equal(e.Reloj.Ahora, e.Avisos.Guardados[0].FechaFinal);
        Assert.Equal(EstadoDelAviso.Pendiente, e.Avisos.Guardados[2].Estado);
        Assert.Equal(EstadoDelAviso.Pendiente, e.Avisos.Guardados[3].Estado);
    }

    [Fact]
    public async Task UnAvisoYaEnviadoOFinal_NoCambiaAlCancelar_YCancelarDosVecesEsIgual()
    {
        var e = new Entorno();
        var cuenta = e.Cuentas.Con(consiente: true);
        await e.Solicitar.Solicitar(Listo(cuenta, "pedido:1:listo-para-recojo"));
        await e.Solicitar.Solicitar(Listo(cuenta, "pedido:2:listo-para-recojo"));
        e.Avisos.Guardados[0].IniciarEnvio(Inicio, Inicio.AddMinutes(2));
        e.Avisos.Guardados[0].MarcarEnviado(Inicio);

        await e.Cancelar.CancelarLosAvisosDe(cuenta);
        await e.Cancelar.CancelarLosAvisosDe(cuenta);

        Assert.Equal(EstadoDelAviso.Enviado, e.Avisos.Guardados[0].Estado);
        Assert.Equal(EstadoDelAviso.Cancelado, e.Avisos.Guardados[1].Estado);
    }

    [Fact]
    public async Task UnClienteSinAvisos_NoHaceNada_YSiLaBaseFallaCancelarLanza_ParaQueIdentidadNoCambieNadaAMedias()
    {
        var e = new Entorno();
        await e.Cancelar.CancelarLosAvisosDe(Guid.NewGuid());
        e.Avisos.Falla = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Cancelar.CancelarLosAvisosDe(Guid.NewGuid()));
    }

    // ---------------------------------------------------------------- desactivar un número (T07)

    private static void ComoAdministrador(Entorno e) => e.Actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), DesactivarAvisosDeUnNumero.RolDeAdministracionCentral);

    [Fact]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task LaAdministracionDesactivaUnNumero_SeGuardaSoloLaHuella_QuedaAuditado_ConUnaHuellaParcial_SinElNumero()
    {
        var e = new Entorno();
        ComoAdministrador(e);

        var resultado = await e.Desactivar.Desactivar("+51 999 111 222", "Lo pidió su titular por haber recibido avisos ajenos");

        Assert.True(resultado.EsExito);
        var guardado = Assert.Single(e.Desactivados.Guardados);
        Assert.Equal(Inicio, guardado.DesactivadoEn);
        Assert.DoesNotContain("999111222", guardado.Huella.Valor, StringComparison.Ordinal);
        var auditada = Assert.Single(e.Auditoria.Registradas);
        Assert.Equal(AccionesAuditables.AvisosDeNumeroDesactivados, auditada.Accion);
        Assert.Equal("numero", auditada.ObjetoTipo);
        Assert.Equal(guardado.Huella.Parcial, auditada.ObjetoId);
        Assert.Equal(DesactivarAvisosDeUnNumero.CausaDeLaDesactivacion, auditada.Detalle["causa"]);
        Assert.DoesNotContain("999", $"{auditada.ObjetoId}{auditada.Motivo}{string.Join('|', auditada.Detalle.Values)}", StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("+51999111222")]
    [InlineData("999111222")]
    [InlineData("999 111 222")]
    [InlineData("0999111222")]
    [InlineData("0051999111222")]
    [InlineData("(+51) 999-111-222")]
    [InlineData("٩٩٩١١١٢٢٢")]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task ElNumeroDesactivado_SeExcluyeEscritoDeCualquierForma(string variante)
    {
        var e = new Entorno();
        ComoAdministrador(e);
        await e.Desactivar.Desactivar("+51999111222", "a pedido del titular");
        NumeroDeDestino.TryNormalizar(variante, out var numero);

        Assert.True(await e.Desactivar.EstaDesactivado(numero));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task UnNumeroDesactivadoConUnaVersionAnteriorDeLaClave_SeReconoceTrasRotarla_YOtroNumeroNo()
    {
        var e = new Entorno { };
        e.Huellas.VersionActual = 1;
        ComoAdministrador(e);
        await e.Desactivar.Desactivar("+51999111222", "a pedido del titular");
        e.Huellas.VersionActual = 2;
        NumeroDeDestino.TryNormalizar("999111222", out var mismo);
        NumeroDeDestino.TryNormalizar("999111223", out var otro);

        Assert.True(await e.Desactivar.EstaDesactivado(mismo));
        Assert.False(await e.Desactivar.EstaDesactivado(otro));
    }

    [Fact]
    public async Task DesactivarDosVeces_EsIdempotente_YNoVuelveAAuditar()
    {
        var e = new Entorno();
        ComoAdministrador(e);

        await e.Desactivar.Desactivar("999111222", "a pedido del titular");
        var otra = await e.Desactivar.Desactivar("+51 999 111 222", "a pedido del titular");

        Assert.True(otra.EsExito);
        Assert.Single(e.Desactivados.Guardados);
        Assert.Single(e.Auditoria.Registradas);
    }

    [Theory]
    [InlineData("cliente")]
    [InlineData("encargado-de-sucursal")]
    [InlineData("personal-de-logistica")]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task QuienNoEsAdministracionCentral_EsProhibido_YQuedaRegistrado_SinDesactivarNada(string rol)
    {
        var e = new Entorno();
        e.Actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), rol);

        var desactivar = await e.Desactivar.Desactivar("999111222", "a pedido del titular");
        var reactivar = await e.Desactivar.Reactivar("999111222");

        Assert.Equal(CategoriaDeError.Prohibido, desactivar.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, reactivar.Error!.Categoria);
        Assert.Equal(2, e.Denegaciones.Registradas.Count);
        Assert.Empty(e.Desactivados.Guardados);
        Assert.Empty(e.Auditoria.Registradas);
    }

    [Fact]
    public async Task SinActorOComoVisitante_EsProhibido()
    {
        var e = new Entorno();

        Assert.Equal(CategoriaDeError.Prohibido, (await e.Desactivar.Desactivar("999111222", "motivo")).Error!.Categoria);
        e.Actor.Actual = ActorActual.Anonimo();
        Assert.Equal(CategoriaDeError.Prohibido, (await e.Desactivar.Desactivar("999111222", "motivo")).Error!.Categoria);
    }

    [Theory]
    [InlineData("no-es-un-numero", "motivo")]
    [InlineData("12345", "motivo")]
    [InlineData("999111222", "")]
    [InlineData("999111222", "   ")]
    [InlineData("999111222", "llamar al 999 111 222 de nuevo")]
    [InlineData("999111222", "ver https://example.com/x")]
    [InlineData("999111222", "llamar al 999.111.222")]
    [InlineData("999111222", "llamar al (999) 111 222")]
    [InlineData("999111222", "al ٩٩٩١١١٢٢٢")]
    [InlineData("999111222", "al 999‑111‑222")]
    [InlineData("999111222", "escribir a titular@example.com")]
    [InlineData("999111222", "escribir a titular＠example.com")]
    public async Task UnNumeroOUnMotivoInvalidos_SeRechazan(string numero, string motivo)
    {
        var e = new Entorno();
        ComoAdministrador(e);

        var resultado = await e.Desactivar.Desactivar(numero, motivo);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Empty(e.Desactivados.Guardados);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task SiLaDesactivacionNoSePuedeAuditar_NoSeAplica()
    {
        var e = new Entorno();
        ComoAdministrador(e);
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => e.Desactivar.Desactivar("999111222", "a pedido del titular"));

        Assert.Empty(e.Desactivados.Guardados);
    }

    [Fact]
    public async Task LaReactivacion_SoloQuitaLaDesactivacion_ConAuditoria_YRepetirlaNoHaceNada()
    {
        var e = new Entorno();
        ComoAdministrador(e);
        await e.Desactivar.Desactivar("999111222", "a pedido del titular");
        e.Auditoria.Registradas.Clear();

        var reactivada = await e.Desactivar.Reactivar("0999 111 222");
        var otra = await e.Desactivar.Reactivar("999111222");

        Assert.True(reactivada.EsExito);
        Assert.True(otra.EsExito);
        Assert.Empty(e.Desactivados.Guardados);
        var auditada = Assert.Single(e.Auditoria.Registradas);
        Assert.Equal(DesactivarAvisosDeUnNumero.CausaDeLaReactivacion, auditada.Detalle["causa"]);
        NumeroDeDestino.TryNormalizar("999111222", out var numero);
        Assert.False(await e.Desactivar.EstaDesactivado(numero));
    }

    [Fact]
    public void LaHuella_NoImprimeElValorCompleto()
    {
        var huella = new HuellaDeNumero(2, new string('a', 64));

        Assert.DoesNotContain("aaaa", huella.ToString(), StringComparison.Ordinal);
        Assert.Equal(8, huella.Parcial.Length);
    }
}
