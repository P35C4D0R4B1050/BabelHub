using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;
using TipoDeActor = BabelHub.Ecommerce.Domain.Auditoria.TipoDeActor;

namespace BabelHub.Ecommerce.UnitTests.Auditoria;

/// <summary>
/// El contrato de registro de la raíz de <c>Auditoria</c> (T07): <c>IAuditoria</c> (si falla, la operación no se aplica, AUD-04) e
/// <c>IRegistroDeSeguridad</c> (no interrumpe y deja una alerta técnica, AUD-12); cada puerto rechaza la acción de la clase equivocada y el actor sale
/// del contexto verificado, nunca de la solicitud (AUD-23).
/// </summary>
public sealed class ContratoDeRegistroTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    private static readonly AccionDelCatalogo CambioDePrecio = new(
        "CambioDePrecio",
        ClaseDeAccion.ConcedeOCambia,
        TipoDeRegistro.Auditoria,
        false,
        [new("precioAnterior", false), new("precioNuevo", false), new("nombre", true)],
        false,
        false,
        [],
        false);

    private static readonly AccionDelCatalogo AccesoDenegado = new(
        "AccesoDenegado",
        ClaseDeAccion.NoConcede,
        TipoDeRegistro.Seguridad,
        false,
        [],
        false,
        false,
        ["fuera-de-sucursal", "sin-sesion"],
        true);

    private static readonly AccionDelCatalogo CambioDeRol = new(
        "CambioDeRol",
        ClaseDeAccion.ConcedeOCambia,
        TipoDeRegistro.Ambos,
        false,
        [new("rolNuevo", false)],
        true,
        false,
        ["solicitud"],
        true);

    private static readonly AccionDelCatalogo IngresoFallidoAislado = new(
        "IngresoFallido",
        ClaseDeAccion.NoConcede,
        TipoDeRegistro.Seguridad,
        true,
        [],
        false,
        false,
        ["credenciales-invalidas"],
        true);

    private static readonly AccionAuditable PrecioCambiado = new("CambioDePrecio");
    private static readonly AccionAuditable Denegado = new("AccesoDenegado");
    private static readonly AccionAuditable RolCambiado = new("CambioDeRol");

    private sealed class Entorno
    {
        public Entorno(ActorDePrueba? actor = null)
        {
            Actor = actor ?? ActorDePrueba.DeAdministradorCentral();
            Constructor = new ConstructorDeRegistros(
                ServicioDeOrigen.Ecommerce,
                new CatalogoDeAcciones([CambioDePrecio, AccesoDenegado, CambioDeRol, IngresoFallidoAislado]),
                Reloj,
                Actor,
                Origenes);
            Auditoria = new AuditoriaDeAplicacion(Constructor, Almacen);
            Seguridad = new RegistroDeSeguridadDeAplicacion(Constructor, Almacen, Alertas, Reloj);
        }

        public ActorDePrueba Actor { get; }

        public RelojFijo Reloj { get; } = new(Ahora);

        public OrigenFijo Origenes { get; } = new(OrigenDeSolicitud.Desconocido);

        public AlmacenDeRegistrosEnMemoria Almacen { get; } = new();

        public AlertasTecnicasEspia Alertas { get; } = new();

        public ConstructorDeRegistros Constructor { get; }

        public IAuditoria Auditoria { get; }

        public IRegistroDeSeguridad Seguridad { get; }
    }

    private static SolicitudDeRegistro Precio(string? nombre = "Ficción", Guid? sucursal = null) =>
        new(PrecioCambiado)
        {
            ObjetoTipo = "edicion",
            ObjetoId = "ed-123",
            SucursalOrigen = sucursal,
            Detalle = new Dictionary<string, string?> { ["precioAnterior"] = "49.90", ["precioNuevo"] = "59.90", ["nombre"] = nombre },
        };

    private static SolicitudDeRegistro Rechazo(string motivo = "fuera-de-sucursal") =>
        new(Denegado) { Motivo = motivo, Resultado = ResultadoAuditado.Rechazado };

    [Fact]
    [Trait("Criterio", "AC-AUD-03")]
    public async Task IAuditoria_Registra_Con_El_Actor_El_Instante_Y_El_Detalle_Seguro()
    {
        var entorno = new Entorno();
        var sucursal = Guid.NewGuid();

        await entorno.Auditoria.Registrar(Precio(sucursal: sucursal));

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Equal("CambioDePrecio", registro.Accion);
        Assert.Equal(ServicioDeOrigen.Ecommerce, registro.Servicio);
        Assert.Equal(TipoDeRegistro.Auditoria, registro.Tipo);
        Assert.Equal(Ahora, registro.InstanteUtc);
        Assert.Equal(TimeSpan.Zero, registro.InstanteUtc.Offset);
        Assert.Equal(TipoDeActor.Usuario, registro.Actor.Tipo);
        Assert.Equal(entorno.Actor.Actual!.UsuarioId, registro.Actor.Id);
        Assert.Equal(entorno.Actor.Actual.Rol, registro.Actor.Rol);
        Assert.Equal("edicion", registro.ObjetoTipo);
        Assert.Equal("ed-123", registro.ObjetoId);
        Assert.Equal(sucursal.ToString("D"), registro.SucursalOrigen);
        Assert.Equal(ResultadoDeOperacion.Exitoso, registro.Resultado);
        Assert.Equal("49.90", registro.Detalle.Valores["precioAnterior"]);
        Assert.Contains("nombre", registro.Detalle.ClavesPersonales);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-18")]
    public async Task El_actor_sale_del_contexto_para_el_sistema_la_integracion_y_el_anonimo()
    {
        foreach (var (actor, esperado) in new (ActorDePrueba, TipoDeActor)[]
        {
            (ActorDePrueba.DelSistema(), TipoDeActor.Sistema),
            (ActorDePrueba.DeIntegracion("culqi"), TipoDeActor.Integracion),
            (ActorDePrueba.Anonimo(), TipoDeActor.Anonimo),
        })
        {
            var entorno = new Entorno(actor);

            await entorno.Auditoria.Registrar(Precio());

            var registro = Assert.Single(entorno.Almacen.Registros);
            Assert.Equal(esperado, registro.Actor.Tipo);
            Assert.Null(registro.Actor.Id);
            Assert.Null(registro.Actor.Rol);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-18")]
    public async Task Un_actor_enviado_como_dato_de_la_solicitud_se_ignora()
    {
        var entorno = new Entorno(ActorDePrueba.DeCliente());
        var solicitud = Precio() with
        {
            Detalle = new Dictionary<string, string?>
            {
                ["precioAnterior"] = "49.90",
                ["actor"] = "administrador-central",
                ["actorTipo"] = "sistema",
                ["rol"] = "administrador-central",
            },
        };

        await entorno.Auditoria.Registrar(solicitud);

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Equal(TipoDeActor.Usuario, registro.Actor.Tipo);
        Assert.Equal("cliente", registro.Actor.Rol);
        Assert.DoesNotContain("actor", registro.Detalle.Valores.Keys);
        Assert.DoesNotContain("rol", registro.Detalle.Valores.Keys);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-03")]
    public async Task IAuditoria_Rechaza_Una_accion_que_no_concede_y_no_guarda_nada()
    {
        var entorno = new Entorno();

        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Auditoria.Registrar(Rechazo()));

        Assert.Empty(entorno.Almacen.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-10")]
    public async Task IRegistroDeSeguridad_Rechaza_Una_accion_que_concede_o_cambia_y_no_la_traga()
    {
        var entorno = new Entorno();

        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Seguridad.Registrar(Precio()));
        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Seguridad.Registrar(new SolicitudDeRegistro(RolCambiado) { Motivo = "solicitud", Origen = "v1:abc123xyz" }));

        Assert.Empty(entorno.Almacen.Registros);
        Assert.Empty(entorno.Alertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-17")]
    public async Task Un_hecho_que_es_auditoria_y_seguridad_se_registra_una_sola_vez_por_IAuditoria()
    {
        var entorno = new Entorno();
        var solicitud = new SolicitudDeRegistro(RolCambiado) { ObjetoTipo = "usuario", ObjetoId = "u-1", Motivo = "solicitud", Detalle = new Dictionary<string, string?> { ["rolNuevo"] = "encargado-de-sucursal" } };

        await entorno.Auditoria.Registrar(solicitud);

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Equal(TipoDeRegistro.Ambos, registro.Tipo);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-03")]
    public async Task Si_el_almacen_no_responde_IAuditoria_falla_para_que_la_operacion_no_se_aplique()
    {
        var entorno = new Entorno();
        entorno.Almacen.Falla = new InvalidOperationException("la base no responde");

        var falla = await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.Auditoria.Registrar(Precio()));

        Assert.IsType<InvalidOperationException>(falla.InnerException);
        Assert.Empty(entorno.Almacen.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-10")]
    public async Task Si_el_almacen_no_responde_IRegistroDeSeguridad_no_interrumpe_y_deja_una_alerta_tecnica()
    {
        var entorno = new Entorno();
        entorno.Almacen.Falla = new InvalidOperationException("la base no responde");

        await entorno.Seguridad.Registrar(Rechazo());

        var alerta = Assert.Single(entorno.Alertas.Alertas);
        Assert.Equal(MotivosDeAlertaDeAuditoria.AuditoriaNoDisponible, alerta.Motivo);
        Assert.Equal(Ahora, alerta.Instante);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-10")]
    public async Task Si_tambien_falla_la_alerta_tecnica_IRegistroDeSeguridad_sigue_sin_interrumpir()
    {
        var entorno = new Entorno();
        entorno.Almacen.Falla = new InvalidOperationException("la base no responde");
        entorno.Alertas.Falla = new InvalidOperationException("el registro tecnico tampoco");

        await entorno.Seguridad.Registrar(Rechazo());

        Assert.Empty(entorno.Alertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-10")]
    public async Task IRegistroDeSeguridad_Registra_El_evento_con_el_motivo_y_el_origen_de_la_solicitud()
    {
        var entorno = new Entorno(ActorDePrueba.Anonimo());

        await entorno.Seguridad.Registrar(Rechazo("sin-sesion"));

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Equal(TipoDeRegistro.Seguridad, registro.Tipo);
        Assert.Equal("sin-sesion", registro.Motivo);
        Assert.Equal(ResultadoDeOperacion.Rechazado, registro.Resultado);
        Assert.Equal(TipoDeActor.Anonimo, registro.Actor.Tipo);
        Assert.Equal("desconocido", registro.Origen is null ? null : registro.Origen.Split(':')[^1]);
        Assert.Empty(entorno.Alertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-10")]
    public async Task Un_origen_explicito_de_la_solicitud_tiene_prioridad_sobre_el_de_la_conexion()
    {
        var entorno = new Entorno();

        await entorno.Seguridad.Registrar(Rechazo() with { Origen = "v1:huella-propia" });

        Assert.Equal("v1:huella-propia", Assert.Single(entorno.Almacen.Registros).Origen);
    }

    [Fact]
    public async Task IAuditoria_Rechaza_Una_accion_que_el_catalogo_no_declara()
    {
        var entorno = new Entorno();

        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Auditoria.Registrar(new SolicitudDeRegistro(new AccionAuditable("AccionNuevaSinDeclarar"))));
        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Seguridad.Registrar(new SolicitudDeRegistro(new AccionAuditable("AccionNuevaSinDeclarar"))));

        Assert.Empty(entorno.Almacen.Registros);
    }

    [Fact]
    public async Task La_marca_aislada_de_la_accion_debe_coincidir_con_la_del_catalogo()
    {
        var entorno = new Entorno();

        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Seguridad.Registrar(new SolicitudDeRegistro(new AccionAuditable("IngresoFallido", aislada: false)) { Motivo = "credenciales-invalidas" }));
        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Auditoria.Registrar(new SolicitudDeRegistro(new AccionAuditable("CambioDePrecio", aislada: true))));

        await entorno.Seguridad.Registrar(new SolicitudDeRegistro(new AccionAuditable("IngresoFallido", aislada: true)) { Motivo = "credenciales-invalidas", Resultado = ResultadoAuditado.Rechazado });
        Assert.True(Assert.Single(entorno.Almacen.Registros).Aislada);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-03")]
    public async Task Sin_contexto_de_actor_IAuditoria_falla_y_la_seguridad_deja_una_alerta()
    {
        var entorno = new Entorno(ActorDePrueba.SinContexto());

        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.Auditoria.Registrar(Precio()));
        await entorno.Seguridad.Registrar(Rechazo());

        Assert.Empty(entorno.Almacen.Registros);
        Assert.Single(entorno.Alertas.Alertas);
    }

    [Fact]
    public async Task Un_motivo_fuera_del_catalogo_cancela_la_auditoria_y_en_seguridad_solo_alerta()
    {
        var entorno = new Entorno();

        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Auditoria.Registrar(new SolicitudDeRegistro(RolCambiado) { Motivo = "texto libre de la solicitud" }));
        await entorno.Seguridad.Registrar(Rechazo("texto libre de la solicitud"));

        Assert.Empty(entorno.Almacen.Registros);
        Assert.Single(entorno.Alertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-05")]
    public async Task Los_datos_prohibidos_del_detalle_se_redactan_y_no_cancelan_la_operacion()
    {
        var entorno = new Entorno();

        await entorno.Auditoria.Registrar(Precio(nombre: "4111 1111 1111 1111"));

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Equal(DetalleSeguro.ValorRedactado, registro.Detalle.Valores["nombre"]);
    }

    [Fact]
    public async Task Una_cancelacion_en_IAuditoria_no_se_convierte_en_otro_error()
    {
        var entorno = new Entorno();
        entorno.Almacen.Falla = new OperationCanceledException();

        using var cancelada = new CancellationTokenSource();
        await cancelada.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => entorno.Auditoria.Registrar(Precio(), cancelada.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("cambioDePrecio")]
    [InlineData("Ab")]
    [InlineData("Cambio De Precio")]
    [InlineData("Cambio-Precio")]
    [InlineData("CambioDePrecio\n")]
    public void Un_identificador_de_accion_invalido_se_rechaza(string identificador) =>
        Assert.Throws<ArgumentException>(() => new AccionAuditable(identificador));

    [Fact]
    public void El_contrato_publico_de_la_raiz_no_expone_tipos_del_dominio_de_Auditoria()
    {
        var raiz = typeof(IAuditoria).Namespace!;
        var tiposDeLaRaiz = typeof(IAuditoria).Assembly.GetTypes().Where(t => t.Namespace == raiz && t.IsVisible).ToList();
        Assert.NotEmpty(tiposDeLaRaiz);

        foreach (var tipo in tiposDeLaRaiz)
        {
            foreach (var usado in TiposQueExpone(tipo))
            {
                Assert.False(
                    usado.Namespace?.StartsWith("BabelHub.Ecommerce.Domain", StringComparison.Ordinal) ?? false,
                    $"{tipo.Name} expone {usado.FullName} del Dominio: el contrato público usa modelos propios.");
            }
        }
    }

    private static IEnumerable<Type> TiposQueExpone(Type tipo)
    {
        const System.Reflection.BindingFlags Publicos = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
        var tipos = new List<Type>();
        foreach (var metodo in tipo.GetMethods(Publicos))
        {
            tipos.Add(metodo.ReturnType);
            tipos.AddRange(metodo.GetParameters().Select(p => p.ParameterType));
        }

        tipos.AddRange(tipo.GetProperties(Publicos).Select(p => p.PropertyType));
        tipos.AddRange(tipo.GetFields(Publicos).Select(f => f.FieldType));
        tipos.AddRange(tipo.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType));
        return tipos.SelectMany(Desarmar);
    }

    private static IEnumerable<Type> Desarmar(Type tipo)
    {
        if (tipo.HasElementType)
        {
            return Desarmar(tipo.GetElementType()!);
        }

        return tipo.IsGenericType ? tipo.GetGenericArguments().SelectMany(Desarmar).Append(tipo) : [tipo];
    }
}
