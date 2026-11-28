using BabelHub.Ecommerce.Application.Envios;
using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Envios;

/// <summary>
/// La matriz de cada operación de Envíos con titular, ajeno, encargado, logística, administración central y anónimo (T23; AC-ENV-03, AC-ENV-05, AC-ENV-08): quién puede, el rechazo uniforme, <b>un evento de denegación por intento</b>
/// y las transiciones inválidas por estado. Corre sobre los casos de uso reales con repositorios en memoria: la integración con PostgreSQL de las mismas reglas está en <c>RepositorioDeEnviosPostgresTests</c>.
/// </summary>
public sealed class MatrizDeRolesDeEnviosTests
{
    public enum Rol
    {
        Titular,
        Ajeno,
        Encargado,
        Logistica,
        Administracion,
        Anonimo,
    }

    public enum Operacion
    {
        Listar,
        Obtener,
        SolicitarGuia,
        PasarAEnReparto,
        MarcarEntregado,
        EnviosDeMiPedido,
    }

    private static readonly Guid Titular = Guid.Parse("a0000000-0000-0000-0000-000000000001");

    private static ActorDePrueba Actor(Rol rol) => rol switch
    {
        Rol.Titular => ActorDePrueba.DeCliente(Titular),
        Rol.Ajeno => ActorDePrueba.DeCliente(Guid.NewGuid()),
        Rol.Encargado => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
        Rol.Logistica => ActorDePrueba.DePersonalDeLogistica(),
        Rol.Administracion => ActorDePrueba.DeAdministradorCentral(),
        _ => ActorDePrueba.Anonimo(),
    };

    /// <summary>Siembra el envío con el estado que la operación espera para tener éxito (y el pedido del titular).</summary>
    private static Envio Sembrar(EntornoDeEnvios entorno, EstadoDeEnvio estado)
    {
        var pedidoId = Guid.NewGuid();
        var envio = Envio.Crear(Guid.NewGuid(), pedidoId, "lima-central", [Guid.NewGuid()], EntornoDeEnvios.AhoraInicial);
        if (estado != EstadoDeEnvio.PendienteDeGuia && estado != EstadoDeEnvio.Cancelado)
        {
            envio.TomarSolicitudDeGuia(EntornoDeEnvios.AhoraInicial, Guid.NewGuid());
            envio.RegistrarGuia(NumeroDeGuia.Crear("GUIA-000123"), "operador-a", EntornoDeEnvios.AhoraInicial);
        }

        if (estado is EstadoDeEnvio.EnReparto or EstadoDeEnvio.Entregado)
        {
            envio.IniciarDespacho(EntornoDeEnvios.AhoraInicial);
            envio.PasarAEnReparto(EntornoDeEnvios.AhoraInicial);
            envio.RegistrarProgresoEnRepartoInformado(EntornoDeEnvios.AhoraInicial);
        }

        if (estado == EstadoDeEnvio.Entregado)
        {
            envio.MarcarEntregado(EntornoDeEnvios.AhoraInicial);
        }

        if (estado == EstadoDeEnvio.Cancelado)
        {
            envio.Cancelar(EntornoDeEnvios.AhoraInicial);
        }

        entorno.Envios.Sembrar(envio);
        entorno.Contactos.SembrarDisponible(pedidoId);
        entorno.Pedidos.Sembrar(new PedidoParaEnvios(pedidoId, "PED-2026-0001", Titular, true, [new LineaDePedidoParaEnvios(envio.Lineas[0].LineaId, false, "lima-central", true)]));
        return envio;
    }

    private static EstadoDeEnvio EstadoNecesario(Operacion operacion) => operacion switch
    {
        Operacion.PasarAEnReparto => EstadoDeEnvio.GuiaGenerada,
        Operacion.MarcarEntregado => EstadoDeEnvio.EnReparto,
        _ => EstadoDeEnvio.PendienteDeGuia,
    };

    private static async Task<ErrorDeAplicacion?> Ejecutar(EntornoDeEnvios entorno, Operacion operacion, Envio envio)
    {
        switch (operacion)
        {
            case Operacion.Listar:
                return (await entorno.Listar.Ejecutar(new ListarEnviosEntrada())).Error;
            case Operacion.Obtener:
                return (await entorno.Obtener.Ejecutar(envio.Id)).Error;
            case Operacion.SolicitarGuia:
                return (await entorno.SolicitarGuia.Ejecutar(envio.Id)).Error;
            case Operacion.PasarAEnReparto:
                return (await entorno.PasarAEnReparto.Ejecutar(envio.Id)).Error;
            case Operacion.MarcarEntregado:
                return (await entorno.MarcarEntregado.Ejecutar(envio.Id)).Error;
            default:
                return (await entorno.EnviosDelPedido.Ejecutar(envio.PedidoId)).Error;
        }
    }

    /// <summary>Lo que se espera: éxito, o la categoría del rechazo y si deja un evento de denegación.</summary>
    private static (CategoriaDeError? Categoria, int Denegaciones) Esperado(Rol rol, Operacion operacion)
    {
        if (rol == Rol.Anonimo)
        {
            return (CategoriaDeError.NoAutenticado, 1);
        }

        if (operacion == Operacion.EnviosDeMiPedido)
        {
            return rol switch
            {
                Rol.Titular or Rol.Logistica or Rol.Administracion => (null, 0),
                Rol.Ajeno => (CategoriaDeError.NoEncontrado, 1),
                _ => (CategoriaDeError.Prohibido, 1),
            };
        }

        return rol is Rol.Logistica or Rol.Administracion ? (null, 0) : (CategoriaDeError.Prohibido, 1);
    }

    public static TheoryData<Rol, Operacion> Combinaciones()
    {
        var datos = new TheoryData<Rol, Operacion>();
        foreach (var rol in Enum.GetValues<Rol>())
        {
            foreach (var operacion in Enum.GetValues<Operacion>())
            {
                datos.Add(rol, operacion);
            }
        }

        return datos;
    }

    [Theory]
    [MemberData(nameof(Combinaciones))]
    [Trait("Criterio", "AC-ENV-08")]
    [Trait("Criterio", "AC-ENV-05")]
    public async Task Cada_rol_y_cada_operacion_responde_lo_que_la_matriz_declara_con_un_evento_por_denegacion(Rol rol, Operacion operacion)
    {
        var entorno = new EntornoDeEnvios(Actor(rol));
        var envio = Sembrar(entorno, EstadoNecesario(operacion));
        var (categoria, denegaciones) = Esperado(rol, operacion);

        var error = await Ejecutar(entorno, operacion, envio);

        Assert.Equal(categoria, error?.Categoria);
        Assert.Equal(denegaciones, entorno.Denegaciones.Eventos.Count);
        if (categoria is not null)
        {
            // Un rechazo no deja efectos: ni el operador, ni el Inventario, ni el contacto, ni la auditoría.
            Assert.Empty(entorno.Operador.Recibidas);
            Assert.Empty(entorno.Reservas.Llamadas);
            Assert.Equal(0, entorno.Contactos.Lecturas);
            Assert.Empty(entorno.Auditoria.Solicitudes);
        }
    }

    public static TheoryData<Operacion, EstadoDeEnvio> TransicionesInvalidas()
    {
        var datos = new TheoryData<Operacion, EstadoDeEnvio>();
        foreach (var estado in Enum.GetValues<EstadoDeEnvio>())
        {
            if (estado != EstadoDeEnvio.PendienteDeGuia)
            {
                datos.Add(Operacion.SolicitarGuia, estado);
            }

            if (estado != EstadoDeEnvio.GuiaGenerada)
            {
                datos.Add(Operacion.PasarAEnReparto, estado);
            }

            if (estado != EstadoDeEnvio.EnReparto)
            {
                datos.Add(Operacion.MarcarEntregado, estado);
            }
        }

        return datos;
    }

    [Theory]
    [MemberData(nameof(TransicionesInvalidas))]
    [Trait("Criterio", "AC-ENV-03")]
    public async Task Una_operacion_en_un_estado_que_no_le_corresponde_es_un_conflicto_sin_efectos(Operacion operacion, EstadoDeEnvio estado)
    {
        var entorno = new EntornoDeEnvios();
        var envio = Sembrar(entorno, estado);
        var antes = entorno.Envios.Todos.Single().Estado;

        var error = await Ejecutar(entorno, operacion, envio);

        Assert.Equal(CategoriaDeError.Conflicto, error!.Categoria);
        Assert.Equal("estado-invalido", error.Clave);
        Assert.Equal(antes, entorno.Envios.Todos.Single().Estado);
        Assert.Empty(entorno.Operador.Recibidas);
        Assert.Empty(entorno.Reservas.Llamadas);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Theory]
    [InlineData(Operacion.Listar)]
    [InlineData(Operacion.Obtener)]
    [InlineData(Operacion.SolicitarGuia)]
    [InlineData(Operacion.PasarAEnReparto)]
    [InlineData(Operacion.MarcarEntregado)]
    [InlineData(Operacion.EnviosDeMiPedido)]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Excedido_el_limite_de_la_operacion_la_respuesta_es_el_429_uniforme_sin_tocar_nada(Operacion operacion)
    {
        var entorno = new EntornoDeEnvios();
        var envio = Sembrar(entorno, EstadoNecesario(operacion));
        entorno.Limites.Maximos["consultas-de-envios-por-usuario"] = 0;
        entorno.Limites.Maximos["operaciones-de-envios-por-usuario"] = 0;

        var error = await Ejecutar(entorno, operacion, envio);

        Assert.Equal(CategoriaDeError.Limite, error!.Categoria);
        Assert.Equal("limite-excedido", error.Clave);
        Assert.Contains(error.Errores, e => e.Campo == "espera-en-segundos");
        Assert.Contains(error.Errores, e => e.Campo == "motivo");
        Assert.Empty(entorno.Operador.Recibidas);
        Assert.Empty(entorno.Reservas.Llamadas);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }
    [Theory]
    [Trait("Criterio", "AC-ENV-08")]
    [InlineData("sistema")]
    [InlineData("integracion")]
    [InlineData("sin-contexto")]
    public async Task Un_actor_de_sistema_de_integracion_o_sin_contexto_no_opera_envios(string tipo)
    {
        var actor = tipo switch
        {
            "sistema" => ActorDePrueba.DelSistema(),
            "integracion" => ActorDePrueba.DeIntegracion("n8n"),
            _ => ActorDePrueba.SinContexto(),
        };
        var entorno = new EntornoDeEnvios(actor);
        var envio = Sembrar(entorno, EstadoDeEnvio.PendienteDeGuia);

        foreach (var operacion in Enum.GetValues<Operacion>())
        {
            var error = await Ejecutar(entorno, operacion, envio);

            Assert.NotNull(error);
            Assert.True(error.Categoria is CategoriaDeError.Prohibido or CategoriaDeError.NoAutenticado, $"{operacion}: {error.Categoria}");
        }

        Assert.Empty(entorno.Operador.Recibidas);
        Assert.Empty(entorno.Reservas.Llamadas);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-08")]
    [InlineData(Rol.Logistica)]
    [InlineData(Rol.Administracion)]
    public async Task Logistica_y_administracion_sobre_un_envio_inexistente_reciben_no_encontrado_sin_evento_de_denegacion(Rol rol)
    {
        var entorno = new EntornoDeEnvios(Actor(rol));
        var inexistente = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "lima-central", [Guid.NewGuid()], EntornoDeEnvios.AhoraInicial);

        foreach (var operacion in new[] { Operacion.Obtener, Operacion.SolicitarGuia, Operacion.PasarAEnReparto, Operacion.MarcarEntregado })
        {
            var error = await Ejecutar(entorno, operacion, inexistente);

            Assert.Equal(CategoriaDeError.NoEncontrado, error!.Categoria);
        }

        Assert.Empty(entorno.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Cada_operacion_consume_su_propia_politica_y_las_guias_el_tope_global_del_operador()
    {
        var entorno = new EntornoDeEnvios();
        var envio = Sembrar(entorno, EstadoDeEnvio.PendienteDeGuia);

        await entorno.Listar.Ejecutar(new ListarEnviosEntrada());
        await entorno.SolicitarGuia.Ejecutar(envio.Id);

        var politicas = entorno.Limites.Reservas.Select(r => r.Politica).ToList();
        Assert.Contains("consultas-de-envios-por-usuario", politicas);
        Assert.Contains("operaciones-de-envios-por-usuario", politicas);
        Assert.Contains("guias-al-operador", politicas);
    }
}
