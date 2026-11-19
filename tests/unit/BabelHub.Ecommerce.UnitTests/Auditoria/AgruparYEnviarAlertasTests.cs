using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;
using BabelHub.Ecommerce.UnitTests.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Auditoria;

/// <summary>
/// El correo de las alertas (T18; AUD-29, AUD-38, AUD-39, AUD-40, AUD-42): una vez por alerta, agrupado, con el tope diario del puerto de correo, reintento con esperas
/// crecientes, sin duplicar lo del Inventario y sin enviar por correo la alerta que origina el propio correo.
/// </summary>
public sealed class AgruparYEnviarAlertasTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    private sealed class Entorno
    {
        public Entorno()
        {
            Reloj = new RelojFijo(Ahora);
            Pendientes = new AlmacenDeCorreoEnMemoria(Alertas);
            Publicador = new PublicadorDeAlertas(ServicioDeOrigen.Ecommerce, Catalogo, Alertas, Unidad, Reloj);
            Correo = new EnvioDeCorreoFalso(Reloj);
            Enviar = new AgruparYEnviarAlertas(
                Alertas,
                Pendientes,
                YaEnviadas,
                Marca,
                Inventario,
                Destinatarios,
                Correo,
                Publicador,
                Catalogo,
                Unidad,
                Reloj);
        }

        public CatalogoDeMotivosDeAlerta Catalogo { get; } = CatalogoDeAlertasDeAuditoria.Crear();

        public RelojFijo Reloj { get; }

        public UnidadDeTrabajoDeAuditoria Unidad { get; } = new();

        public AlmacenDeAlertasEnMemoria Alertas { get; } = new();

        public AlertasYaEnviadasEnMemoria YaEnviadas { get; } = new();

        public MarcaDeLecturaEnMemoria Marca { get; } = new();

        public ClienteDelInventarioFalso Inventario { get; } = new();

        public DestinatariosFalsos Destinatarios { get; } = new();

        public EnvioDeCorreoFalso Correo { get; private set; }

        public PublicadorDeAlertas Publicador { get; }

        public AgruparYEnviarAlertas Enviar { get; private set; }

        public AlmacenDeCorreoEnMemoria Pendientes { get; }

        public Task Tecnica(string motivo, DateTimeOffset cuando) => ((IPublicadorDeAlertasTecnicas)Publicador).Publicar(new AlertaTecnica(motivo, cuando));
    }

    private static AlertaConsultada DelInventario(string id, string motivo, DateTimeOffset primera, int ocurrencias = 1) =>
        new($"inv:{id}", ServicioAuditado.Inventario, new CodigoDeAlerta(motivo), ClaseDeAlertaConsultada.Tecnica, primera, primera.AddMinutes(1), ocurrencias, 0);

    [Fact]
    [Trait("Criterio", "AC-AUD-26")]
    public async Task TresAlertasDelMismoMotivoEnQuinceMinutosMasUnaDelInventario_SonUnCorreoPorServicioYMotivoConLasRepeticionesAgrupadas()
    {
        var e = new Entorno();
        await e.Tecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora);
        await e.Tecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora.AddMinutes(4));
        await e.Tecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora.AddMinutes(9));
        e.Inventario.Alertas = [DelInventario("11111111-1111-4111-8111-111111111111", MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora.AddMinutes(-2), 3)];

        var resultado = await e.Enviar.Ejecutar();

        Assert.Equal(2, resultado.Enviadas);
        Assert.Equal(2, e.Correo.Enviados.Count);
        var propio = e.Correo.Enviados.Single(c => c.Correo.Servicio == ServicioAuditado.Ecommerce);
        Assert.Equal(3, propio.Correo.Ocurrencias);
        Assert.Equal(Ahora, propio.Correo.PrimeraOcurrencia);
        Assert.Equal(2, propio.Destinatarios);
        var remoto = e.Correo.Enviados.Single(c => c.Correo.Servicio == ServicioAuditado.Inventario);
        Assert.Equal(3, remoto.Correo.Ocurrencias);
        Assert.True(e.Alertas.Alertas.Single().EnviadaPorCorreo);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-26")]
    public async Task UnaSegundaCorridaNoReenviaLoYaEnviado_NiLasOcurrenciasPosterioresDeLaVentana()
    {
        var e = new Entorno();
        await e.Tecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora);
        await e.Enviar.Ejecutar();

        await e.Tecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora.AddMinutes(6));
        var otra = await e.Enviar.Ejecutar();

        Assert.Equal(0, otra.Enviadas);
        Assert.Single(e.Correo.Enviados);
        Assert.Equal(2, e.Alertas.Alertas.Single().Ocurrencias);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-26")]
    public async Task PasadaLaVentanaUnaRepeticionGeneraOtroCorreo_HastaElTopeDiarioYLasDemasQuedanSinEnviar()
    {
        var e = new Entorno();
        for (var i = 0; i < 30; i++)
        {
            e.Reloj.Ahora = Ahora.AddMinutes(i * 16);
            await e.Tecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, e.Reloj.Ahora);
            await e.Enviar.Ejecutar();
        }

        Assert.Equal(30, e.Alertas.Alertas.Count);
        Assert.Equal(EnvioDeCorreoFalso.TopeDiario, e.Correo.Enviados.Count);
        Assert.All(e.Alertas.Alertas, a => Assert.True(a.EnviadaPorCorreo));
        Assert.Equal(30, e.Correo.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public async Task UnaOcurrenciaALosTresMinutosDeLaMarcaVuelveAPendienteSinOtroCorreo_YUnaPosteriorALaVentanaAbreOtraAlertaConSuCorreo()
    {
        var e = new Entorno();
        await e.Tecnica(MotivosDeAlerta.AccesosDenegados, Ahora);
        await e.Enviar.Ejecutar();
        var alerta = e.Alertas.Alertas.Single();
        e.Alertas.Alertas[0] = alerta.MarcarRevisada(1, Guid.NewGuid(), Ahora.AddMinutes(1));

        await e.Tecnica(MotivosDeAlerta.AccesosDenegados, Ahora.AddMinutes(4));
        var dentro = await e.Enviar.Ejecutar();

        Assert.Equal(EstadoDeAlerta.Pendiente, e.Alertas.Alertas.Single().Estado);
        Assert.Equal(0, dentro.Enviadas);

        await e.Tecnica(MotivosDeAlerta.AccesosDenegados, Ahora.AddMinutes(16));
        var fuera = await e.Enviar.Ejecutar();

        Assert.Equal(2, e.Alertas.Alertas.Count);
        Assert.Equal(1, fuera.Enviadas + fuera.Suprimidas);
        Assert.Equal(2, e.Correo.Enviados.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-26")]
    public async Task ConElCorreoCaido_LaAlertaPermaneceEnElRegistroTecnicoYSeReintentaConEsperasCrecientesSinInterrumpirNada()
    {
        var e = new Entorno();
        await e.Tecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora);
        e.Correo.Falla = new InvalidOperationException("Host=smtp;Password=Sup3r");
        var id = e.Alertas.Alertas.Single().Id;

        var primera = await e.Enviar.Ejecutar();
        Assert.Equal(1, primera.Fallidas);
        Assert.False(e.Alertas.Buscar(MotivosDeAlerta.TrabajoDeFondoDetenido)!.EnviadaPorCorreo);
        Assert.Equal((1, Ahora + TimeSpan.FromMinutes(1)), e.Pendientes.Intentos[id]);
        Assert.Contains(e.Alertas.Alertas, a => a.Motivo == MotivosDeAlerta.CorreoDeAlertasNoDisponible);

        // Antes de la espera no se reintenta.
        e.Reloj.Ahora = Ahora.AddSeconds(30);
        Assert.Equal(0, (await e.Enviar.Ejecutar()).Fallidas);
        Assert.Equal(1, e.Correo.Intentos);

        // Cada reintento espera el doble, con tope de una hora.
        var esperados = new[] { 2, 4, 8, 16, 32, 60, 60 };
        var ahora = Ahora.AddMinutes(1);
        for (var n = 0; n < esperados.Length; n++)
        {
            e.Reloj.Ahora = ahora;
            await e.Enviar.Ejecutar();
            var (intentos, proximo) = e.Pendientes.Intentos[id];
            Assert.Equal(n + 2, intentos);
            Assert.Equal(ahora + TimeSpan.FromMinutes(esperados[n]), proximo);
            ahora = proximo;
        }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-26")]
    public async Task LaAlertaOriginadaPorElFalloDelCorreoNoSeEnviaPorCorreoAunqueElCorreoSeRecupere()
    {
        var e = new Entorno();
        await e.Tecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora);
        e.Correo.Falla = new InvalidOperationException("sin SMTP");
        await e.Enviar.Ejecutar();

        e.Correo.Falla = null;
        e.Reloj.Ahora = Ahora.AddMinutes(2);
        var recuperada = await e.Enviar.Ejecutar();

        Assert.Equal(1, recuperada.Enviadas);
        var enviado = Assert.Single(e.Correo.Enviados);
        Assert.Equal(MotivosDeAlerta.TrabajoDeFondoDetenido, enviado.Correo.Motivo.Valor);
        Assert.DoesNotContain(e.Correo.Enviados, c => c.Correo.Motivo.Valor == MotivosDeAlerta.CorreoDeAlertasNoDisponible);
        Assert.True(e.Alertas.Buscar(MotivosDeAlerta.CorreoDeAlertasNoDisponible)!.EnviadaPorCorreo);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-26")]
    public async Task ElCorreoNoLlevaElTextoDeUnaExcepcionNiDatosDeConexion()
    {
        var e = new Entorno();
        await e.Tecnica("falla con Host=db;Password=Sup3r y 4111 1111 1111 1111", Ahora);

        await e.Enviar.Ejecutar();

        var correo = Assert.Single(e.Correo.Enviados).Correo;
        Assert.Equal(MotivosDeAlerta.MotivoNoCatalogado, correo.Motivo.Valor);
        Assert.DoesNotContain("Password", correo.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("4111", correo.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-30")]
    public async Task ConElECommerceCaidoLaAlertaDelInventarioEspera_YAlRecuperarseSaleUnSoloCorreo()
    {
        var e = new Entorno();
        var alerta = DelInventario("22222222-2222-4222-8222-222222222222", MotivosDeAlerta.DependenciaNoNecesariaFallida, Ahora.AddMinutes(-1));
        e.Inventario.Alertas = [alerta];
        e.Correo.Falla = new InvalidOperationException("correo caído");

        var caida = await e.Enviar.Ejecutar();

        Assert.Equal(1, caida.Fallidas);
        Assert.Empty(e.YaEnviadas.Ids);
        Assert.Null(e.Marca.Valor);

        e.Correo.Falla = null;
        e.Reloj.Ahora = Ahora.AddMinutes(10);
        var recuperada = await e.Enviar.Ejecutar();
        var otraVez = await e.Enviar.Ejecutar();

        Assert.Equal(1, recuperada.Enviadas);
        Assert.Equal(0, otraVez.Enviadas);
        Assert.Single(e.Correo.Enviados);
        Assert.Contains(alerta.Id, e.YaEnviadas.Ids);
        Assert.Equal(Ahora.AddMinutes(10), e.Marca.Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-30")]
    public async Task ElInventarioCaido_NoAvanzaLaMarcaYSeAvisaPorElRegistroTecnico_ConLoPropioIntacto()
    {
        var e = new Entorno();
        await e.Tecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora);
        e.Inventario.Estado = EstadoDeRespuestaDelInventario.NoDisponible;

        var resultado = await e.Enviar.Ejecutar();

        Assert.True(resultado.InventarioNoDisponible);
        Assert.Equal(1, resultado.Enviadas);
        Assert.Null(e.Marca.Valor);
        Assert.Contains(e.Alertas.Alertas, a => a.Motivo == MotivosDeAlerta.LecturaDeAlertasDelInventarioNoDisponible);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-30")]
    public async Task LaLecturaDelInventarioRetrocedeLaVentanaDesdeLaMarca_ParaNoPerderUnaAlertaAgrupadaMientrasSeLeia()
    {
        var e = new Entorno();
        e.Marca.Valor = Ahora.AddHours(-1);
        var reciente = DelInventario("33333333-3333-4333-8333-333333333333", MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora.AddMinutes(-70));
        e.Inventario.Alertas = [reciente];

        var resultado = await e.Enviar.Ejecutar();

        Assert.Equal(1, resultado.Enviadas);
        Assert.Contains(reciente.Id, e.YaEnviadas.Ids);
    }

    [Fact]
    public async Task SinAdministradoresActivos_NoSeEnvíaNadaNiSeCuentaComoFalloYLasAlertasSiguenPendientes()
    {
        var e = new Entorno();
        await e.Tecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora);
        e.Destinatarios.Cuentas.Clear();

        var resultado = await e.Enviar.Ejecutar();

        Assert.True(resultado.SinDestinatarios);
        Assert.Equal(0, e.Correo.Intentos);
        Assert.False(e.Alertas.Alertas.Single().EnviadaPorCorreo);
        Assert.Empty(e.Pendientes.Intentos);

        e.Destinatarios.Cuentas.Add(new DestinatarioDeAlerta(Guid.NewGuid()));
        Assert.Equal(1, (await e.Enviar.Ejecutar()).Enviadas);
    }

    [Fact]
    public async Task UnaAlertaDeMasDeVeinticuatroHorasDesdeSuUltimaOcurrencia_YaNoSeReintenta()
    {
        var e = new Entorno();
        await e.Tecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora);
        e.Reloj.Ahora = Ahora.AddHours(25);

        var resultado = await e.Enviar.Ejecutar();

        Assert.Equal(0, resultado.Enviadas);
        Assert.Equal(0, e.Correo.Intentos);
        Assert.True(e.Alertas.Alertas.Single().EnviadaPorCorreo);
    }

    [Fact]
    public void LasEsperasCrecienteDuplicanHastaElTopeDeUnaHora()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), EsperaCrecienteDeCorreo.Para(1));
        Assert.Equal(TimeSpan.FromMinutes(2), EsperaCrecienteDeCorreo.Para(2));
        Assert.Equal(TimeSpan.FromMinutes(32), EsperaCrecienteDeCorreo.Para(6));
        Assert.Equal(TimeSpan.FromMinutes(60), EsperaCrecienteDeCorreo.Para(7));
        Assert.Equal(TimeSpan.FromMinutes(60), EsperaCrecienteDeCorreo.Para(500));
        Assert.Throws<ArgumentOutOfRangeException>(() => EsperaCrecienteDeCorreo.Para(0));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-21")]
    public async Task LaAlertaDeUnTrasladoVinculadoSinAprobar_LlegaALaAdministracionYALogistica_UnaSolaVez()
    {
        var e = new Entorno();
        var logistica = new[] { new DestinatarioDeAlerta(Guid.NewGuid()), new DestinatarioDeAlerta(Guid.NewGuid()), new DestinatarioDeAlerta(Guid.NewGuid()) };
        e.Destinatarios.Logistica.AddRange(logistica);
        e.Inventario.Alertas = [DelInventario("22222222-2222-4222-8222-222222222222", MotivosDeAlerta.TrasladoVinculadoSinAprobar, Ahora.AddMinutes(-2))];

        var resultado = await e.Enviar.Ejecutar();

        Assert.Equal(1, resultado.Enviadas);
        var enviado = Assert.Single(e.Correo.Enviados);
        // Los dos administradores y los tres de logística, sin repetir a nadie.
        Assert.Equal(5, enviado.Destinatarios);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-21")]
    public async Task UnaAlertaQueNoEsDeTraslados_NoLlegaALogistica()
    {
        var e = new Entorno();
        e.Destinatarios.Logistica.Add(new DestinatarioDeAlerta(Guid.NewGuid()));
        await e.Tecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora);

        await e.Enviar.Ejecutar();

        Assert.Equal(2, Assert.Single(e.Correo.Enviados).Destinatarios);
    }

    [Fact]
    public async Task UnaCuentaQueEsAdministradoraYDeLogistica_RecibeUnSoloCorreo()
    {
        var e = new Entorno();
        e.Destinatarios.Logistica.Add(e.Destinatarios.Cuentas[0]);
        e.Inventario.Alertas = [DelInventario("33333333-3333-4333-8333-333333333333", MotivosDeAlerta.TrasladoVinculadoSinAprobar, Ahora.AddMinutes(-2))];

        await e.Enviar.Ejecutar();

        Assert.Equal(2, Assert.Single(e.Correo.Enviados).Destinatarios);
    }

    [Fact]
    public async Task SiNoSePuedeLeerALogistica_LaAlertaSaleIgualALaAdministracion()
    {
        var e = new Entorno();
        e.Destinatarios.LogisticaFalla = true;
        e.Inventario.Alertas = [DelInventario("44444444-4444-4444-8444-444444444444", MotivosDeAlerta.TrasladoVinculadoSinAprobar, Ahora.AddMinutes(-2))];

        var resultado = await e.Enviar.Ejecutar();

        Assert.Equal(1, resultado.Enviadas);
        Assert.Equal(2, Assert.Single(e.Correo.Enviados).Destinatarios);
    }
}
