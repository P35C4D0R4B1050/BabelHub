using BabelHub.Ecommerce.Application.Devoluciones.Interno;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;
using BabelHub.Ecommerce.UnitTests.Carrito;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Envios;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>La autorización y el alcance de Devoluciones (T05; DEV-13, DEV-28, AC-DEV-12, AC-DEV-23, AC-DEV-30): el orden rol → textos → alcance, el mismo «no encontrado» con un acceso denegado por intento y los límites.</summary>
public sealed class AutorizacionYAlcanceDeDevolucionesTests
{
    private static readonly Guid Sucursal = Guid.Parse("a1000000-0000-0000-0000-000000000001");

    private static Devolucion Devolucion(TipoDeDevolucion tipo = TipoDeDevolucion.Impreso, OrigenDeDevolucion origen = OrigenDeDevolucion.Solicitud, string? sucursal = "lima-central")
    {
        var motivo = TextoRedactado.Crear("Defecto", 300)!;
        var cliente = ResponsableDelCambio.Cliente(Guid.NewGuid());
        return tipo == TipoDeDevolucion.Impreso
            ? Domain.Devoluciones.Devolucion.Solicitar(Guid.NewGuid(), tipo, origen, sucursal, motivo, null, [new NuevaLineaDeDevolucion(Guid.NewGuid(), Guid.NewGuid(), 1, 50m)], EntornoDeDevoluciones.AhoraInicial, cliente)
            : origen == OrigenDeDevolucion.Reporte
                ? Domain.Devoluciones.Devolucion.Solicitar(Guid.NewGuid(), tipo, origen, null, motivo, TextoRedactado.Crear("Falla", 2000), [new NuevaLineaDeDevolucion(Guid.NewGuid(), Guid.NewGuid(), 1, 30m, Guid.NewGuid())], EntornoDeDevoluciones.AhoraInicial, cliente)
                : Domain.Devoluciones.Devolucion.Solicitar(Guid.NewGuid(), tipo, origen, null, motivo, null, [new NuevaLineaDeDevolucion(Guid.NewGuid(), Guid.NewGuid(), 1, 30m)], EntornoDeDevoluciones.AhoraInicial, cliente);
    }

    private static ActorDePrueba EncargadoSinSucursal()
    {
        var actor = ActorDePrueba.DeCliente();
        actor.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), BabelHub.Shared.Contratos.RolesDeUsuario.EncargadoDeSucursal));
        return actor;
    }

    // ---------- Roles ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public async Task Cada_rol_pasa_solo_por_lo_que_le_corresponde_y_el_resto_es_prohibido_con_un_evento()
    {
        var casos = new (ActorDePrueba Actor, Func<AutorizadorDeDevoluciones, Task<ErrorDeAplicacion?>> Exigir, bool Permitido)[]
        {
            (ActorDePrueba.DeCliente(), a => a.ExigirCliente(default), true),
            (ActorDePrueba.DeCliente(), a => a.ExigirPersonal(default), false),
            (ActorDePrueba.DeCliente(), a => a.ExigirAdministracion(default), false),
            (ActorDePrueba.DeEncargadoDe(Sucursal), a => a.ExigirCliente(default), false),
            (ActorDePrueba.DeEncargadoDe(Sucursal), a => a.ExigirPersonal(default), true),
            (ActorDePrueba.DeEncargadoDe(Sucursal), a => a.ExigirAdministracion(default), false),
            (ActorDePrueba.DeAdministradorCentral(), a => a.ExigirCliente(default), false),
            (ActorDePrueba.DeAdministradorCentral(), a => a.ExigirPersonal(default), true),
            (ActorDePrueba.DeAdministradorCentral(), a => a.ExigirAdministracion(default), true),
            (ActorDePrueba.DePersonalDeLogistica(), a => a.ExigirPersonal(default), false),
        };

        foreach (var (actor, exigir, permitido) in casos)
        {
            var entorno = new EntornoDeDevoluciones(actor);

            var error = await exigir(entorno.Autorizador);

            Assert.Equal(permitido, error is null);
            Assert.Equal(permitido ? 0 : 1, entorno.Denegaciones.Eventos.Count);
            if (!permitido)
            {
                Assert.Equal(CategoriaDeError.Prohibido, error!.Categoria);
                Assert.Equal("ECO-PROHIBIDO", entorno.Denegaciones.Eventos[0].CodigoDeError);
            }
        }
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-12")]
    [InlineData("anonimo")]
    [InlineData("sin-contexto")]
    [InlineData("sistema")]
    public async Task Sin_sesion_o_con_un_actor_que_no_es_una_persona_es_no_autenticado_con_un_evento(string tipo)
    {
        var actor = tipo switch
        {
            "anonimo" => ActorDePrueba.Anonimo(),
            "sistema" => ActorDePrueba.DelSistema(),
            _ => ActorDePrueba.SinContexto(),
        };
        var entorno = new EntornoDeDevoluciones(actor);

        var error = await entorno.Autorizador.ExigirCliente(default);

        Assert.Equal(tipo == "sistema" ? CategoriaDeError.Prohibido : CategoriaDeError.NoAutenticado, error!.Categoria);
        Assert.Single(entorno.Denegaciones.Eventos);
    }

    // ---------- Textos legales pendientes ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-23")]
    public async Task Con_textos_pendientes_se_rechaza_despues_del_rol_y_sin_abrir_nada()
    {
        var entorno = new EntornoDeDevoluciones(ActorDePrueba.DeCliente());
        entorno.Cuentas.TextosPendientes = true;

        var error = await entorno.Autorizador.ExigirCliente(default);

        Assert.Equal(CategoriaDeError.Prohibido, error!.Categoria);
        Assert.Equal("textos-pendientes", error.Clave);
        Assert.Equal(1, entorno.Cuentas.Consultas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-23")]
    public async Task Un_rol_sin_permiso_se_rechaza_antes_de_consultar_a_Identidad()
    {
        var entorno = new EntornoDeDevoluciones(ActorDePrueba.DeEncargadoDe(Sucursal));

        await entorno.Autorizador.ExigirCliente(default);

        Assert.Equal(0, entorno.Cuentas.Consultas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-23")]
    public async Task Una_cuenta_inexistente_o_cancelada_no_es_una_sesion_valida_y_con_Identidad_caida_es_no_disponible()
    {
        var inexistente = new EntornoDeDevoluciones();
        inexistente.Cuentas.NoExiste = true;
        var cancelada = new EntornoDeDevoluciones();
        cancelada.Cuentas.Cancelada = true;
        var caida = new EntornoDeDevoluciones();
        caida.Cuentas.NoResponde = true;

        Assert.Equal(CategoriaDeError.NoAutenticado, (await inexistente.Autorizador.ExigirCliente(default))!.Categoria);
        Assert.Equal(CategoriaDeError.NoAutenticado, (await cancelada.Autorizador.ExigirCliente(default))!.Categoria);
        var error = await caida.Autorizador.ExigirCliente(default);
        Assert.Equal(CategoriaDeError.NoDisponible, error!.Categoria);
        Assert.Empty(caida.Denegaciones.Eventos);
    }

    // ---------- Alcance ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-30")]
    public async Task Un_pedido_ajeno_responde_no_encontrado_con_un_evento_por_cada_intento()
    {
        var cuenta = Guid.NewGuid();
        var entorno = new EntornoDeDevoluciones(ActorDePrueba.DeCliente(cuenta));

        Assert.Null(await entorno.Autorizador.ExigirQueElPedidoEsDelCliente(cuenta, default));
        var primero = await entorno.Autorizador.ExigirQueElPedidoEsDelCliente(Guid.NewGuid(), default);
        var segundo = await entorno.Autorizador.ExigirQueElPedidoEsDelCliente(Guid.NewGuid(), default);

        Assert.Equal(CategoriaDeError.NoEncontrado, primero!.Categoria);
        Assert.Equal("no-encontrado", primero.Clave);
        Assert.Equal(primero, segundo);
        Assert.Equal(2, entorno.Denegaciones.Eventos.Count);
        Assert.All(entorno.Denegaciones.Eventos, e => Assert.Equal("ECO-NO-ENCONTRADO", e.CodigoDeError));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public async Task El_encargado_solo_ve_los_impresos_de_su_sucursal_receptora()
    {
        var entorno = new EntornoDeDevoluciones(ActorDePrueba.DeEncargadoDe(Sucursal));
        entorno.Sucursales.Codigos[Sucursal] = "lima-central";

        Assert.Null(await entorno.Autorizador.ExigirAlcanceDeGestion(Devolucion(sucursal: "lima-central"), default));
        Assert.Empty(entorno.Denegaciones.Eventos);
        var otra = await entorno.Autorizador.ExigirAlcanceDeGestion(Devolucion(sucursal: "arequipa"), default);

        Assert.Equal(CategoriaDeError.NoEncontrado, otra!.Categoria);
        Assert.Equal("fuera-de-sucursal", Assert.Single(entorno.Denegaciones.Eventos).Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-30")]
    public async Task Un_encargado_nunca_ve_un_reporte_ni_un_digital_y_la_respuesta_es_la_misma_que_la_de_otra_sucursal()
    {
        var entorno = new EntornoDeDevoluciones(ActorDePrueba.DeEncargadoDe(Sucursal));
        entorno.Sucursales.Codigos[Sucursal] = "lima-central";

        var reporte = await entorno.Autorizador.ExigirAlcanceDeGestion(Devolucion(TipoDeDevolucion.Digital, OrigenDeDevolucion.Reporte, null), default);
        var digital = await entorno.Autorizador.ExigirAlcanceDeGestion(Devolucion(TipoDeDevolucion.Digital, OrigenDeDevolucion.Solicitud, null), default);
        var ajena = await entorno.Autorizador.ExigirAlcanceDeGestion(Devolucion(sucursal: "arequipa"), default);

        Assert.Equal(ajena, reporte);
        Assert.Equal(ajena, digital);
        Assert.Equal(["alcance-insuficiente", "alcance-insuficiente", "fuera-de-sucursal"], entorno.Denegaciones.Eventos.Select(e => e.Motivo));
        Assert.Equal(3, entorno.Denegaciones.Eventos.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public async Task La_administracion_central_ve_todas_sin_resolver_ninguna_sucursal()
    {
        var entorno = new EntornoDeDevoluciones(ActorDePrueba.DeAdministradorCentral());

        Assert.Null(await entorno.Autorizador.ExigirAlcanceDeGestion(Devolucion(), default));
        Assert.Null(await entorno.Autorizador.ExigirAlcanceDeGestion(Devolucion(TipoDeDevolucion.Digital, OrigenDeDevolucion.Reporte, null), default));
        Assert.Null(await entorno.Autorizador.ExigirSucursalDeLaRuta("cualquiera", default));
        Assert.Equal(0, entorno.Sucursales.Consultas);
        Assert.Empty(entorno.Denegaciones.Eventos);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-30")]
    [InlineData("no-responde")]
    [InlineData("sin-codigo")]
    [InlineData("sin-sucursal-en-el-actor")]
    public async Task Una_sucursal_que_no_se_puede_resolver_se_niega_y_se_registra(string falla)
    {
        var entorno = falla == "sin-sucursal-en-el-actor" ? new EntornoDeDevoluciones(EncargadoSinSucursal()) : new EntornoDeDevoluciones(ActorDePrueba.DeEncargadoDe(Sucursal));
        if (falla == "no-responde")
        {
            entorno.Sucursales.NoResponde = true;
        }

        var error = await entorno.Autorizador.ExigirAlcanceDeGestion(Devolucion(), default);

        Assert.Equal(CategoriaDeError.NoEncontrado, error!.Categoria);
        Assert.Equal("fuera-de-sucursal", Assert.Single(entorno.Denegaciones.Eventos).Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public async Task El_codigo_de_sucursal_de_la_ruta_debe_ser_el_del_encargado_y_si_no_es_prohibido_por_el_parametro_de_la_ruta()
    {
        var entorno = new EntornoDeDevoluciones(ActorDePrueba.DeEncargadoDe(Sucursal));
        entorno.Sucursales.Codigos[Sucursal] = "lima-central";

        Assert.Null(await entorno.Autorizador.ExigirSucursalDeLaRuta("lima-central", default));
        var otra = await entorno.Autorizador.ExigirSucursalDeLaRuta("arequipa", default);

        Assert.Equal(CategoriaDeError.Prohibido, otra!.Categoria);
        var evento = Assert.Single(entorno.Denegaciones.Eventos);
        Assert.Equal("fuera-de-sucursal", evento.Motivo);
        Assert.Equal("ECO-PROHIBIDO", evento.CodigoDeError);
    }

    // ---------- Límites ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public async Task Las_consultas_y_las_operaciones_consumen_su_politica_por_usuario_y_el_exceso_es_el_429_uniforme()
    {
        var entorno = new EntornoDeDevoluciones();
        entorno.Limites.Maximos["consultas-de-devoluciones-por-usuario"] = 1;
        entorno.Limites.Maximos["operaciones-de-devoluciones-por-usuario"] = 0;

        Assert.Null(await entorno.LimitesDeUso.Consulta(default));
        var consulta = await entorno.LimitesDeUso.Consulta(default);
        var operacion = await entorno.LimitesDeUso.Operacion(default);

        Assert.Equal(CategoriaDeError.Limite, consulta!.Categoria);
        Assert.Equal(["espera-en-segundos", "motivo", "disponibleDesde"], consulta.Errores.Select(e => e.Campo));
        Assert.Equal("operaciones-de-devoluciones-por-usuario", operacion!.Errores.Single(e => e.Campo == "motivo").Clave);
        Assert.All(entorno.Limites.Reservas, r => Assert.Equal(((Guid)entorno.Actor.Actual!.UsuarioId!).ToString("N"), r.Sujeto));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public async Task Con_el_almacen_de_limites_caido_se_falla_cerrado_con_servicio_no_disponible()
    {
        var entorno = new EntornoDeDevoluciones();
        entorno.Limites.Caido = true;

        var error = await entorno.LimitesDeUso.Operacion(default);

        Assert.Equal(CategoriaDeError.NoDisponible, error!.Categoria);
        Assert.Equal("servicio-no-disponible", error.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-30")]
    public async Task Un_encargado_sin_sucursal_en_el_actor_es_prohibido_en_la_ruta_con_un_evento()
    {
        var entorno = new EntornoDeDevoluciones(EncargadoSinSucursal());

        var error = await entorno.Autorizador.ExigirSucursalDeLaRuta("lima-central", default);

        Assert.Equal(CategoriaDeError.Prohibido, error!.Categoria);
        Assert.Equal("fuera-de-sucursal", Assert.Single(entorno.Denegaciones.Eventos).Motivo);
        Assert.Equal(0, entorno.Sucursales.Consultas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-30")]
    public async Task El_codigo_de_la_ruta_solo_lo_ve_un_encargado_o_la_administracion_y_logistica_con_sucursal_es_prohibido()
    {
        var entorno = new EntornoDeDevoluciones(ActorDePrueba.DePersonalDeLogistica());

        var error = await entorno.Autorizador.ExigirSucursalDeLaRuta("lima-central", default);

        Assert.Equal(CategoriaDeError.Prohibido, error!.Categoria);
        Assert.Equal("fuera-de-rol", Assert.Single(entorno.Denegaciones.Eventos).Motivo);
        Assert.Equal(0, entorno.Sucursales.Consultas);
    }
}
