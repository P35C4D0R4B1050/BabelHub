using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Auditoria;
using BabelHub.Inventario.UnitTests.Plataforma;
using BabelHub.Inventario.UnitTests.Soporte;
using TipoDeActor = BabelHub.Inventario.Domain.Auditoria.TipoDeActor;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>
/// Los eventos de supresión de avisos de seguridad (AUD-43) y de dirección reenviada sin credencial (AUD-44), de T16: sin el contenido del aviso, sin
/// el correo de destino y sin la dirección reenviada.
/// </summary>
public sealed class EventosDeAvisosYOrigenReenviadoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    private static readonly CatalogoDeAcciones Catalogo = CatalogoDeAccionesDeAuditoria.Crear();

    private sealed class Entorno
    {
        public Entorno(ActorDePrueba actor)
        {
            var constructor = new ConstructorDeRegistros(ServicioDeOrigen.Ecommerce, Catalogo, new RelojFijo(Ahora), actor, new OrigenFijo(OrigenDeSolicitud.Desconocido));
            Seguridad = new RegistroDeSeguridadDeAplicacion(constructor, Almacen, Alertas, new RelojFijo(Ahora));
        }

        public AlmacenDeRegistrosEnMemoria Almacen { get; } = new();

        public AlertasTecnicasEspia Alertas { get; } = new();

        public IRegistroDeSeguridad Seguridad { get; }
    }

    [Theory]
    [InlineData("cuota-de-correo")]
    [InlineData("tope-por-cuenta")]
    [InlineData("tope-por-origen")]
    [InlineData("correo-sin-verificar")]
    [Trait("Criterio", "AC-AUD-31")]
    public async Task Un_aviso_suprimido_guarda_la_cuenta_y_el_motivo_y_nunca_el_correo_ni_el_contenido(string motivo)
    {
        var entorno = new Entorno(ActorDePrueba.DelSistema());
        var cuenta = Guid.NewGuid().ToString("D");

        await entorno.Seguridad.Registrar(new SolicitudDeRegistro(AccionesAuditables.AvisoDeSeguridadSuprimido)
        {
            ObjetoTipo = "cuenta",
            ObjetoId = cuenta,
            Motivo = motivo,
            Resultado = ResultadoAuditado.Rechazado,
            Detalle = new Dictionary<string, string?>
            {
                ["correo"] = "ana@correo.pe",
                ["destinatario"] = "ana@correo.pe",
                ["asunto"] = "Cambio de contraseña",
                ["cuerpo"] = "Tu código es 123456",
            },
        });

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Equal(TipoDeActor.Sistema, registro.Actor.Tipo);
        Assert.Equal(cuenta, registro.ObjetoId);
        Assert.Equal(motivo, registro.Motivo);
        Assert.True(registro.Aislada);
        Assert.Empty(registro.Detalle.Valores);
        Assert.DoesNotContain("ana@correo.pe", registro.Detalle.Texto);
        Assert.DoesNotContain("123456", registro.Detalle.Texto);
        Assert.True(Catalogo.Obtener("AvisoDeSeguridadSuprimido").ObjetoPersonal);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-31")]
    public async Task Un_aviso_suprimido_por_un_tope_de_origen_puede_no_tener_cuenta()
    {
        var entorno = new Entorno(ActorDePrueba.DelSistema());

        await entorno.Seguridad.Registrar(new SolicitudDeRegistro(AccionesAuditables.AvisoDeSeguridadSuprimido) { Motivo = "tope-por-origen", Resultado = ResultadoAuditado.Rechazado });

        Assert.Null(Assert.Single(entorno.Almacen.Registros).ObjetoId);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-31")]
    public async Task Un_motivo_de_supresion_fuera_del_catalogo_no_interrumpe_y_deja_una_alerta()
    {
        var entorno = new Entorno(ActorDePrueba.DelSistema());

        await entorno.Seguridad.Registrar(new SolicitudDeRegistro(AccionesAuditables.AvisoDeSeguridadSuprimido) { Motivo = "a pedido de ana@correo.pe" });

        Assert.Empty(entorno.Almacen.Registros);
        Assert.Single(entorno.Alertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-32")]
    public async Task Una_direccion_reenviada_sin_credencial_guarda_solo_la_direccion_directa_del_llamante()
    {
        var entorno = new Entorno(ActorDePrueba.Anonimo());

        await entorno.Seguridad.Registrar(new SolicitudDeRegistro(AccionesAuditables.OrigenReenviadoSinCredencial)
        {
            Motivo = "origen-reenviado-sin-credencial",
            Origen = "198.51.100.7",
            Resultado = ResultadoAuditado.Rechazado,
            Detalle = new Dictionary<string, string?>
            {
                ["direccionReenviada"] = "203.0.113.50",
                ["xOrigenReenviado"] = "203.0.113.50",
                ["x-forwarded-for"] = "203.0.113.50, 10.0.0.1",
            },
        });

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Equal("198.51.100.7", registro.Origen);
        Assert.Empty(registro.Detalle.Valores);
        Assert.DoesNotContain("203.0.113", registro.Detalle.Texto);
        Assert.DoesNotContain("203.0.113", registro.Motivo);
        Assert.False(registro.Aislada);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-32")]
    public async Task El_evento_de_origen_reenviado_nunca_interrumpe_la_llamada_si_la_auditoria_no_responde()
    {
        var entorno = new Entorno(ActorDePrueba.Anonimo());
        entorno.Almacen.Falla = new InvalidOperationException("la base no responde");

        await entorno.Seguridad.Registrar(new SolicitudDeRegistro(AccionesAuditables.OrigenReenviadoSinCredencial) { Motivo = "origen-reenviado-sin-credencial", Origen = "198.51.100.7" });

        Assert.Single(entorno.Alertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-32")]
    public void Ningun_evento_de_origen_declara_una_clave_para_la_direccion_reenviada_ni_para_el_correo()
    {
        foreach (var nombre in new[] { "AvisoDeSeguridadSuprimido", "OrigenReenviadoSinCredencial" })
        {
            Assert.Empty(Catalogo.Obtener(nombre).Detalle);
        }
    }
}
