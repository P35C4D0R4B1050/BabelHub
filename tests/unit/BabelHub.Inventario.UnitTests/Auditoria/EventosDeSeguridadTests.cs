using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Domain.Auditoria;
using BabelHub.Inventario.UnitTests.Plataforma;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>
/// El catálogo de los eventos de seguridad (T15; AUD-10, AUD-28, AUD-33, AUD-37): los que no conceden nada nunca interrumpen y los que conceden o
/// cambian algo no se aplican sin auditoría (AC-AUD-03, AC-AUD-08, AC-AUD-25).
/// </summary>
public sealed class EventosDeSeguridadTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    private static readonly CatalogoDeAcciones Catalogo = CatalogoDeAccionesDeAuditoria.Crear();

    private static readonly string[] SinConceder =
    [
        nameof(AccionesAuditables.AccesoDenegado),
        nameof(AccionesAuditables.LimiteExcedido),
        nameof(AccionesAuditables.PresupuestoDelPersonalExcedido),
        nameof(AccionesAuditables.LlamadaEntreServiciosSinCredencial),
        nameof(AccionesAuditables.OrigenIndeterminable),
        nameof(AccionesAuditables.VerificacionDePersonaFallida),
        nameof(AccionesAuditables.EntradaDeIntegracionRechazada),
        nameof(AccionesAuditables.IngresoFallido),
        nameof(AccionesAuditables.BloqueoTemporalAplicado),
        nameof(AccionesAuditables.RecuperacionDeAccesoSolicitada),
        nameof(AccionesAuditables.SegundoFactorFallido),
        nameof(AccionesAuditables.CambioDeCorreoNoReconocidoRechazado),
        nameof(AccionesAuditables.CambioDeCorreoNoReconocidoVencido),
    ];

    private static readonly string[] ConcedenOCambian =
    [
        nameof(AccionesAuditables.RecuperacionDeAccesoCompletada),
        nameof(AccionesAuditables.CodigoDeRespaldoUsado),
        nameof(AccionesAuditables.ContrasenaCambiada),
        nameof(AccionesAuditables.CorreoCambiado),
        nameof(AccionesAuditables.SegundoFactorActivado),
        nameof(AccionesAuditables.SegundoFactorDesactivado),
        nameof(AccionesAuditables.SegundoFactorRegenerado),
        nameof(AccionesAuditables.CambioDeCorreoNoReconocidoConfirmado),
        nameof(AccionesAuditables.CambioDeRol),
        nameof(AccionesAuditables.ClaveDeFirmaDeclaradaComprometida),
    ];

    public static TheoryData<string> NombresSinConceder => new(SinConceder);

    public static TheoryData<string> NombresQueConceden => new(ConcedenOCambian);

    private sealed class Entorno
    {
        public Entorno()
        {
            var constructor = new ConstructorDeRegistros(ServicioDeOrigen.Ecommerce, Catalogo, new RelojFijo(Ahora), ActorDePrueba.Anonimo(), new OrigenFijo(OrigenDeSolicitud.Desconocido));
            Auditoria = new AuditoriaDeAplicacion(constructor, Almacen);
            Seguridad = new RegistroDeSeguridadDeAplicacion(constructor, Almacen, Alertas, new RelojFijo(Ahora));
        }

        public AlmacenDeRegistrosEnMemoria Almacen { get; } = new();

        public AlertasTecnicasEspia Alertas { get; } = new();

        public IAuditoria Auditoria { get; }

        public IRegistroDeSeguridad Seguridad { get; }
    }

    private static SolicitudDeRegistro Solicitud(string nombre)
    {
        var accion = Catalogo.Obtener(nombre);
        return new SolicitudDeRegistro(new AccionAuditable(nombre, accion.Aislada))
        {
            Motivo = accion.Motivos[0],
            Resultado = accion.Clase == ClaseDeAccion.NoConcede ? ResultadoAuditado.Rechazado : ResultadoAuditado.Exitoso,
        };
    }

    [Theory]
    [MemberData(nameof(NombresSinConceder))]
    [Trait("Criterio", "AC-AUD-03")]
    public async Task Un_evento_que_no_concede_nada_mantiene_su_efecto_y_deja_una_alerta_si_la_auditoria_no_responde(string nombre)
    {
        var entorno = new Entorno();
        entorno.Almacen.Falla = new InvalidOperationException("la base no responde");

        await entorno.Seguridad.Registrar(Solicitud(nombre));

        Assert.Single(entorno.Alertas.Alertas);
        Assert.Equal(ClaseDeAccion.NoConcede, Catalogo.Obtener(nombre).Clase);
        Assert.Equal(TipoDeRegistro.Seguridad, Catalogo.Obtener(nombre).Tipo);
    }

    [Theory]
    [MemberData(nameof(NombresQueConceden))]
    [Trait("Criterio", "AC-AUD-03")]
    public async Task Un_hecho_que_concede_o_cambia_algo_no_se_aplica_si_la_auditoria_no_responde(string nombre)
    {
        var entorno = new Entorno();
        entorno.Almacen.Falla = new InvalidOperationException("la base no responde");

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.Auditoria.Registrar(Solicitud(nombre)));

        Assert.Equal(ClaseDeAccion.ConcedeOCambia, Catalogo.Obtener(nombre).Clase);
        Assert.Equal(TipoDeRegistro.Ambos, Catalogo.Obtener(nombre).Tipo);
    }

    [Theory]
    [MemberData(nameof(NombresSinConceder))]
    [Trait("Criterio", "AC-AUD-08")]
    public async Task Cada_evento_de_seguridad_figura_con_su_fecha_su_origen_y_su_motivo(string nombre)
    {
        var entorno = new Entorno();

        await entorno.Seguridad.Registrar(Solicitud(nombre));

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Equal(Ahora, registro.InstanteUtc);
        Assert.NotNull(registro.Motivo);
        Assert.NotNull(registro.Origen);
        Assert.Equal(ResultadoDeOperacion.Rechazado, registro.Resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-08")]
    public async Task Un_ingreso_fallido_de_una_cuenta_que_no_existe_solo_guarda_el_origen()
    {
        var entorno = new Entorno();

        await entorno.Seguridad.Registrar(Solicitud("IngresoFallido") with { ObjetoTipo = null, ObjetoId = null });

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Null(registro.ObjetoId);
        Assert.NotNull(registro.Origen);
        Assert.True(registro.Aislada);
    }

    [Theory]
    [InlineData(nameof(AccionesAuditables.CodigoDeRespaldoUsado))]
    [InlineData(nameof(AccionesAuditables.ContrasenaCambiada))]
    [InlineData(nameof(AccionesAuditables.RecuperacionDeAccesoCompletada))]
    [InlineData(nameof(AccionesAuditables.IngresoFallido))]
    [InlineData(nameof(AccionesAuditables.SegundoFactorFallido))]
    [Trait("Criterio", "AC-AUD-08")]
    public async Task Ningun_evento_de_ingreso_guarda_contrasenas_ni_codigos(string nombre)
    {
        var entorno = new Entorno();
        var solicitud = Solicitud(nombre) with
        {
            Detalle = new Dictionary<string, string?> { ["contrasena"] = "Clave-Secreta-1", ["codigo"] = "654321", ["codigoDeRespaldo"] = "ABCD-EFGH-IJKL", ["correo"] = "ana@correo.pe" },
        };

        if (Catalogo.Obtener(nombre).Clase == ClaseDeAccion.ConcedeOCambia)
        {
            await entorno.Auditoria.Registrar(solicitud);
        }
        else
        {
            await entorno.Seguridad.Registrar(solicitud);
        }

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Empty(registro.Detalle.Valores);
        Assert.DoesNotContain("Clave-Secreta-1", registro.Detalle.Texto);
        Assert.DoesNotContain("654321", registro.Detalle.Texto);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public async Task Un_limite_excedido_guarda_el_total_la_politica_como_motivo_y_la_direccion_completa_como_origen()
    {
        var entorno = new Entorno();

        await entorno.Seguridad.Registrar(new SolicitudDeRegistro(AccionesAuditables.LimiteExcedido)
        {
            Motivo = "ingresos-fallidos",
            Origen = "2800:200:1234:5678::1",
            Resultado = ResultadoAuditado.Rechazado,
            Detalle = new Dictionary<string, string?> { ["totalDeRechazos"] = "70" },
        });

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Equal("70", registro.Detalle.Valores["totalDeRechazos"]);
        Assert.Equal("2800:200:1234:5678::1", registro.Origen);
        Assert.Equal("ingresos-fallidos", registro.Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public void Todas_las_politicas_de_limite_de_la_plataforma_tienen_su_motivo()
    {
        var politicas = PoliticasDeLimite.Generales.Concat(PoliticasDeLimite.Todas).Select(p => p.Valor).ToHashSet();

        Assert.All(politicas, p => Assert.Contains(p, CatalogoDeAccionesDeAuditoria.MotivosDeLimiteExcedido));
        Assert.Equal(37, CatalogoDeAccionesDeAuditoria.MotivosDeLimiteExcedido.Count);
        Assert.Equal(CatalogoDeAccionesDeAuditoria.MotivosDeLimiteExcedido.Order(StringComparer.Ordinal), Catalogo.Obtener("LimiteExcedido").Motivos.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public void Todos_los_motivos_de_rechazo_de_una_integracion_de_la_plataforma_estan_en_el_catalogo()
    {
        var motivos = new[]
        {
            ProcesarEntradaDeIntegracion.MotivoProveedorDesconocido,
            ProcesarEntradaDeIntegracion.MotivoIdInvalido,
            ProcesarEntradaDeIntegracion.MotivoCuerpoDemasiadoGrande,
            ProcesarEntradaDeIntegracion.MotivoMarcaInvalida,
            ProcesarEntradaDeIntegracion.MotivoMarcaFueraDeTolerancia,
            ProcesarEntradaDeIntegracion.MotivoSinFirma,
            ProcesarEntradaDeIntegracion.MotivoFirmaInvalida,
        };

        Assert.Equal(motivos.Order(StringComparer.Ordinal), Catalogo.Obtener("EntradaDeIntegracionRechazada").Motivos.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-08")]
    public void Los_eventos_de_Identidad_van_al_almacen_aislado_y_los_de_la_plataforma_no()
    {
        foreach (var nombre in SinConceder.Concat(ConcedenOCambian))
        {
            var esDeIdentidad = nombre is not (nameof(AccionesAuditables.AccesoDenegado) or nameof(AccionesAuditables.LimiteExcedido) or nameof(AccionesAuditables.PresupuestoDelPersonalExcedido)
                or nameof(AccionesAuditables.LlamadaEntreServiciosSinCredencial) or nameof(AccionesAuditables.OrigenIndeterminable) or nameof(AccionesAuditables.VerificacionDePersonaFallida)
                or nameof(AccionesAuditables.EntradaDeIntegracionRechazada));

            Assert.Equal(esDeIdentidad, Catalogo.Obtener(nombre).Aislada);
        }
    }
}
