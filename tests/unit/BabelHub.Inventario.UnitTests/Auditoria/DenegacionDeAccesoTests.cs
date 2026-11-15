using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Auditoria;
using BabelHub.Inventario.UnitTests.Plataforma;
using BabelHub.Inventario.UnitTests.Soporte;
using TipoDeActor = BabelHub.Inventario.Domain.Auditoria.TipoDeActor;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>
/// <c>DenegarAcceso</c> (T15, AUD-05): un único evento de acceso denegado por solicitud, con la correlación generada por el servidor, escrito en su
/// propia transacción, con el usuario o «anónimo», el rol, la ruta como plantilla, el motivo y el origen. Nunca interrumpe el rechazo (AUD-12).
/// </summary>
public sealed class DenegacionDeAccesoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    private static readonly string[] Plantillas = ["/api/ecommerce/v1/pedidos/{id}", "/api/ecommerce/v1/biblioteca", "/api/inventario/v1/fuentes/{id}"];

    private sealed class EscritorEnMemoria : IEscritorDeDenegaciones
    {
        public List<RegistroDeAuditoria> Registros { get; } = [];

        public Exception? Falla { get; set; }

        public Task Escribir(RegistroDeAuditoria registro, CancellationToken cancelacion = default)
        {
            if (Falla is not null)
            {
                return Task.FromException(Falla);
            }

            Registros.Add(registro);
            return Task.CompletedTask;
        }
    }

    private sealed class PlantillasFijas(params string[] plantillas) : IPlantillasDeRuta
    {
        public IReadOnlyCollection<string> Todas { get; } = plantillas;
    }

    private sealed class Entorno
    {
        public Entorno(ActorDePrueba? actor = null, OrigenDeSolicitud? origen = null)
        {
            Actor = actor ?? ActorDePrueba.DeCliente();
            Reloj = new RelojFijo(Ahora);
            Constructor = new ConstructorDeRegistros(
                ServicioDeOrigen.Ecommerce,
                CatalogoDeAccionesDeAuditoria.Crear(),
                Reloj,
                Actor,
                new OrigenFijo(origen ?? OrigenDeSolicitud.Desconocido));
        }

        public ActorDePrueba Actor { get; }

        public RelojFijo Reloj { get; }

        public ConstructorDeRegistros Constructor { get; }

        public EscritorEnMemoria Escritor { get; } = new();

        public AlertasTecnicasEspia Alertas { get; } = new();

        /// <summary>Una solicitud nueva: su propio contexto, con su propia correlación y su propia bandera.</summary>
        public IDenegacionDeAcceso Solicitud() =>
            new DenegacionDeAccesoDeAplicacion(Constructor, new ContextoDeDenegacion(), new ResolutorDeRutas(new PlantillasFijas(Plantillas)), Escritor, Alertas, Reloj);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-04")]
    public async Task Un_acceso_fuera_de_la_sucursal_queda_como_un_evento_con_usuario_rol_ruta_motivo_y_origen()
    {
        var sucursal = Guid.NewGuid();
        var entorno = new Entorno(ActorDePrueba.DeEncargadoDe(sucursal));

        await entorno.Solicitud().Registrar(new DenegacionDeAcceso("fuera-de-sucursal", "/api/inventario/v1/fuentes/9f3c2a10-aaaa-bbbb-cccc-000000000001?incluir=existencias"));

        var registro = Assert.Single(entorno.Escritor.Registros);
        Assert.Equal("AccesoDenegado", registro.Accion);
        Assert.Equal(TipoDeRegistro.Seguridad, registro.Tipo);
        Assert.Equal(ResultadoDeOperacion.Rechazado, registro.Resultado);
        Assert.Equal(TipoDeActor.Usuario, registro.Actor.Tipo);
        Assert.Equal("encargado-de-sucursal", registro.Actor.Rol);
        Assert.Equal("fuera-de-sucursal", registro.Motivo);
        Assert.Equal("/api/inventario/v1/fuentes/{id}", registro.Ruta);
        Assert.NotNull(registro.Origen);
        Assert.Equal(Ahora, registro.InstanteUtc);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-21")]
    public async Task Quien_no_tiene_sesion_queda_como_anonimo()
    {
        var entorno = new Entorno(ActorDePrueba.Anonimo());

        await entorno.Solicitud().Registrar(new DenegacionDeAcceso("sin-sesion", "/api/ecommerce/v1/biblioteca"));

        Assert.Equal(TipoDeActor.Anonimo, Assert.Single(entorno.Escritor.Registros).Actor.Tipo);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-21")]
    public async Task Sin_contexto_de_actor_el_evento_se_registra_como_anonimo_en_vez_de_perderse()
    {
        var entorno = new Entorno(ActorDePrueba.SinContexto());

        await entorno.Solicitud().Registrar(new DenegacionDeAcceso("sin-sesion", "/api/ecommerce/v1/biblioteca"));

        Assert.Equal(TipoDeActor.Anonimo, Assert.Single(entorno.Escritor.Registros).Actor.Tipo);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-21")]
    public async Task Un_rechazo_que_pasa_por_varios_puntos_de_la_misma_solicitud_cuenta_una_sola_vez()
    {
        var entorno = new Entorno();
        var solicitud = entorno.Solicitud();

        await solicitud.Registrar(new DenegacionDeAcceso("recurso-ajeno", "/api/ecommerce/v1/pedidos/ped-1"));
        await solicitud.Registrar(new DenegacionDeAcceso("fuera-de-rol", "/api/ecommerce/v1/pedidos/ped-1"));
        await solicitud.Registrar(new DenegacionDeAcceso("recurso-ajeno", "/api/ecommerce/v1/pedidos/ped-1"));

        Assert.Single(entorno.Escritor.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-21")]
    public async Task Dos_intentos_distintos_del_mismo_actor_y_ruta_son_dos_eventos_con_correlaciones_distintas()
    {
        var entorno = new Entorno();

        await entorno.Solicitud().Registrar(new DenegacionDeAcceso("recurso-ajeno", "/api/ecommerce/v1/pedidos/ped-1"));
        await entorno.Solicitud().Registrar(new DenegacionDeAcceso("recurso-ajeno", "/api/ecommerce/v1/pedidos/ped-1"));

        Assert.Equal(2, entorno.Escritor.Registros.Count);
        Assert.NotEqual(entorno.Escritor.Registros[0].Correlacion, entorno.Escritor.Registros[1].Correlacion);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-21")]
    public async Task Cien_solicitudes_con_el_mismo_identificador_de_correlacion_del_cliente_son_cien_eventos()
    {
        // El contrato no recibe ninguna correlación del cliente: la genera el servidor por solicitud, así que repetir un identificador no suprime eventos.
        var entorno = new Entorno();

        for (var i = 0; i < 100; i++)
        {
            await entorno.Solicitud().Registrar(new DenegacionDeAcceso("recurso-ajeno", "/api/ecommerce/v1/pedidos/ped-1?x-correlacion=repetida"));
        }

        Assert.Equal(100, entorno.Escritor.Registros.Count);
        Assert.Equal(100, entorno.Escritor.Registros.Select(r => r.Correlacion).Distinct().Count());
        Assert.All(entorno.Escritor.Registros, r => Assert.Matches(@"^[A-Za-z0-9_-]{8,64}$", r.Correlacion!));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-04")]
    public async Task La_ruta_guardada_es_la_plantilla_sin_parametros_ni_tokens_o_sin_coincidencia()
    {
        var entorno = new Entorno();

        await entorno.Solicitud().Registrar(new DenegacionDeAcceso("fuera-de-rol", "/api/ecommerce/v1/pedidos/ped-1;jsessionid=abc?token=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.firma#frag"));
        await entorno.Solicitud().Registrar(new DenegacionDeAcceso("fuera-de-rol", "/ruta/que/nadie-declaro/9f3c2a10"));
        await entorno.Solicitud().Registrar(new DenegacionDeAcceso("fuera-de-rol", null));

        Assert.Equal("/api/ecommerce/v1/pedidos/{id}", entorno.Escritor.Registros[0].Ruta);
        Assert.Equal(PlantillaDeRuta.SinCoincidencia, entorno.Escritor.Registros[1].Ruta);
        Assert.Equal(PlantillaDeRuta.SinCoincidencia, entorno.Escritor.Registros[2].Ruta);
        Assert.All(entorno.Escritor.Registros, r => Assert.DoesNotContain("eyJ", r.Ruta));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-10")]
    public async Task Si_no_se_puede_escribir_el_rechazo_se_mantiene_y_queda_una_alerta_tecnica()
    {
        var entorno = new Entorno();
        entorno.Escritor.Falla = new InvalidOperationException("la base no responde");

        await entorno.Solicitud().Registrar(new DenegacionDeAcceso("recurso-ajeno", "/api/ecommerce/v1/biblioteca"));

        var alerta = Assert.Single(entorno.Alertas.Alertas);
        Assert.Equal(MotivosDeAlertaDeAuditoria.AuditoriaNoDisponible, alerta.Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-10")]
    public async Task Un_motivo_fuera_del_catalogo_no_pierde_el_evento_ni_deja_una_alerta_falsa_y_se_registra_con_el_motivo_general()
    {
        var entorno = new Entorno();

        await entorno.Solicitud().Registrar(new DenegacionDeAcceso("motivo-inventado-por-un-modulo", "/api/ecommerce/v1/biblioteca"));

        var registro = Assert.Single(entorno.Escritor.Registros);
        Assert.Equal(DenegacionDeAccesoDeAplicacion.MotivoPorOmision, registro.Motivo);
        Assert.Empty(entorno.Alertas.Alertas);
    }

    [Fact]
    public async Task El_origen_del_evento_es_la_huella_de_la_conexion_y_nunca_la_direccion_completa()
    {
        var entorno = new Entorno(origen: OrigenDeSolicitud.Desconocido);

        await entorno.Solicitud().Registrar(new DenegacionDeAcceso("sin-sesion", "/api/ecommerce/v1/biblioteca"));

        var origen = Assert.Single(entorno.Escritor.Registros).Origen!;
        Assert.Matches(@"^[A-Za-z0-9:._-]{1,64}$", origen);
    }

    [Fact]
    public void El_contexto_de_denegacion_entrega_el_turno_una_sola_vez_y_una_correlacion_estable_por_solicitud()
    {
        var contexto = new ContextoDeDenegacion();

        Assert.True(contexto.TomarTurno());
        Assert.False(contexto.TomarTurno());
        Assert.Equal(contexto.Correlacion, contexto.Correlacion);
        Assert.NotEqual(new ContextoDeDenegacion().Correlacion, contexto.Correlacion);
    }

    [Fact]
    public async Task El_turno_se_toma_una_sola_vez_aunque_dos_puntos_del_pipeline_lo_pidan_a_la_vez()
    {
        var contexto = new ContextoDeDenegacion();

        var turnos = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(contexto.TomarTurno)));

        Assert.Equal(1, turnos.Count(t => t));
    }
}
