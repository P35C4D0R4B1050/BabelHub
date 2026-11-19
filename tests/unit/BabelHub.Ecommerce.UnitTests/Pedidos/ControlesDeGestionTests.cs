using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T10 (PED-21, PED-41, AC-PED-35, AC-PED-17): los límites de uso, los textos pendientes de la cancelación del cliente y el rechazo de una segunda operación mientras hay una en curso.</summary>
public sealed class ControlesDeGestionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Pedido = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly SolicitanteDePedidos Cliente = new(RolesDePedidos.Cliente, Cuenta, null);

    private sealed class LimitadorFalso : ILimitadorDePedidos
    {
        public ResultadoDeLimiteDePedidos Respuesta { get; set; } = ResultadoDeLimiteDePedidos.Permitido;

        public bool Lanza { get; set; }

        public List<string> Llamadas { get; } = [];

        public Task<ResultadoDeLimiteDePedidos> Consultar(Guid usuarioId, CancellationToken cancelacion = default) => Responder("consultar");

        public Task<ResultadoDeLimiteDePedidos> Gestionar(Guid usuarioId, CancellationToken cancelacion = default) => Responder("gestionar");

        public Task<ResultadoDeLimiteDePedidos> LeerContacto(Guid usuarioId, CancellationToken cancelacion = default) => Responder("contacto");

        private Task<ResultadoDeLimiteDePedidos> Responder(string nombre)
        {
            Llamadas.Add(nombre);
            return Lanza ? throw new InvalidOperationException("sin almacén") : Task.FromResult(Respuesta);
        }
    }

    private sealed class CuentasFalsas : IEstadoDeCuenta
    {
        public EstadoDeCuenta? Estado { get; set; } = new(Cuenta, true, EstadoDeAceptacionDeLosTextos.AlDia, true, false, false);

        public bool Lanza { get; set; }

        public Task<EstadoDeCuenta?> Consultar(Guid usuarioId, CancellationToken cancelacion = default) => Lanza ? throw new InvalidOperationException() : Task.FromResult(Estado);

        public Task<ResultadoDeLaAceptacion> AceptarLosTextos(Guid usuarioId, string versionMostrada, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private static (ControlesDeGestionDePedidos Controles, LimitadorFalso Limites, CuentasFalsas Cuentas, RepositorioDePedidosFalso Repositorio) Crear()
    {
        var limites = new LimitadorFalso();
        var cuentas = new CuentasFalsas();
        var repositorio = new RepositorioDePedidosFalso();
        return (new ControlesDeGestionDePedidos(limites, cuentas, repositorio), limites, cuentas, repositorio);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-35")]
    public async Task Un_exceso_de_consultas_o_de_gestion_responde_limite_excedido_con_el_instante_de_reintento()
    {
        var (controles, limites, _, _) = Crear();
        var desde = Ahora.AddSeconds(40);
        limites.Respuesta = new ResultadoDeLimiteDePedidos(DecisionDeLimiteDePedidos.Excedido, desde);

        var consultas = await controles.LimiteDeConsultas(Cliente, default);
        var gestion = await controles.LimiteDeGestion(Cliente, default);

        Assert.All(new[] { consultas, gestion }, e =>
        {
            Assert.Equal((CategoriaDeError.Limite, "limite-excedido"), (e!.Categoria, e.Clave));
            Assert.Equal(desde.ToString("O", System.Globalization.CultureInfo.InvariantCulture), Assert.Single(e.Errores).Clave);
        });
        Assert.Equal(["consultar", "gestionar"], limites.Llamadas);
    }

    [Fact]
    public async Task Dentro_del_limite_no_hay_error_y_un_contador_caido_o_una_excepcion_es_servicio_no_disponible()
    {
        var (controles, limites, _, _) = Crear();

        Assert.Null(await controles.LimiteDeConsultas(Cliente, default));
        limites.Respuesta = ResultadoDeLimiteDePedidos.NoDisponible;
        var caido = await controles.LimiteDeGestion(Cliente, default);
        limites.Lanza = true;
        var lanza = await controles.LimiteDeConsultas(Cliente, default);

        Assert.Equal((CategoriaDeError.NoDisponible, "servicio-no-disponible"), (caido!.Categoria, caido.Clave));
        Assert.Equal(CategoriaDeError.NoDisponible, lanza!.Categoria);
    }

    [Fact]
    public async Task El_presupuesto_de_lecturas_del_contacto_falla_cerrado_y_solo_oculta_el_contacto()
    {
        var (controles, limites, _, _) = Crear();

        Assert.True(await controles.PuedeLeerElContacto(Cliente, default));
        limites.Respuesta = new ResultadoDeLimiteDePedidos(DecisionDeLimiteDePedidos.Excedido, Ahora);
        Assert.False(await controles.PuedeLeerElContacto(Cliente, default));
        limites.Respuesta = ResultadoDeLimiteDePedidos.NoDisponible;
        Assert.False(await controles.PuedeLeerElContacto(Cliente, default));
        limites.Lanza = true;
        Assert.False(await controles.PuedeLeerElContacto(Cliente, default));
    }

    [Fact]
    public async Task La_cancelacion_del_cliente_exige_los_textos_al_dia_y_falla_cerrado_si_Identidad_no_responde()
    {
        var (controles, _, cuentas, _) = Crear();

        Assert.Null(await controles.ExigirTextosAlDia(Cuenta, default));
        cuentas.Estado = new EstadoDeCuenta(Cuenta, false, EstadoDeAceptacionDeLosTextos.Pendiente, true, false, false);
        var pendientes = await controles.ExigirTextosAlDia(Cuenta, default);
        cuentas.Estado = null;
        var inexistente = await controles.ExigirTextosAlDia(Cuenta, default);
        cuentas.Estado = new EstadoDeCuenta(Cuenta, false, EstadoDeAceptacionDeLosTextos.AlDia, true, true, false);
        var cancelada = await controles.ExigirTextosAlDia(Cuenta, default);
        cuentas.Lanza = true;
        var caida = await controles.ExigirTextosAlDia(Cuenta, default);

        Assert.Equal((CategoriaDeError.Prohibido, "textos-pendientes"), (pendientes!.Categoria, pendientes.Clave));
        Assert.Equal("textos-pendientes", inexistente!.Clave);
        Assert.Equal("textos-pendientes", cancelada!.Clave);
        Assert.Equal((CategoriaDeError.NoDisponible, "servicio-no-disponible"), (caida!.Categoria, caida.Clave));
    }

    private static Anulacion AnulacionEnCurso(Guid linea) =>
        Anulacion.Iniciar(Guid.NewGuid(), Pedido, new PlanDeAnulacion(OrigenDeLaAnulacion.Administracion, MotivoDeAnulacion.Otro, [linea], [], false, 50m, []), new IniciadorDeOperacion(Guid.NewGuid(), "administrador-central", null), Ahora);

    [Fact]
    [Trait("Criterio", "AC-PED-17")]
    public async Task Una_segunda_operacion_sobre_una_linea_con_otra_en_curso_se_rechaza_con_operacion_en_curso()
    {
        var (controles, _, _, repositorio) = Crear();
        var linea = Guid.NewGuid();
        repositorio.Anulaciones.Add(AnulacionEnCurso(linea));

        var mismaLinea = await controles.ExigirSinOperacionEnCurso(Pedido, [linea], default);
        var otraLinea = await controles.ExigirSinOperacionEnCurso(Pedido, [Guid.NewGuid()], default);
        var pedidoEntero = await controles.ExigirSinOperacionEnCurso(Pedido, null, default);

        Assert.Equal((CategoriaDeError.Conflicto, "pedido-operacion-en-curso"), (mismaLinea!.Categoria, mismaLinea.Clave));
        Assert.Null(otraLinea);
        Assert.Equal("pedido-operacion-en-curso", pedidoEntero!.Clave);
    }

    [Fact]
    public async Task Una_entrega_en_curso_bloquea_sus_lineas_y_una_fallida_o_descartada_ya_no()
    {
        var (controles, _, _, repositorio) = Crear();
        var linea = Guid.NewGuid();
        var entrega = Entrega.Iniciar(Guid.NewGuid(), Pedido, [linea], new IniciadorDeOperacion(Guid.NewGuid(), "encargado-de-sucursal", Guid.NewGuid()), Ahora);
        repositorio.Entregas.Add(entrega);

        Assert.NotNull(await controles.ExigirSinOperacionEnCurso(Pedido, [linea], default));
        entrega.Fallar(Ahora.AddMinutes(20));
        Assert.Null(await controles.ExigirSinOperacionEnCurso(Pedido, [linea], default));
        var anulacion = AnulacionEnCurso(linea);
        anulacion.Descartar(Ahora);
        repositorio.Anulaciones.Add(anulacion);
        Assert.Null(await controles.ExigirSinOperacionEnCurso(Pedido, [linea], default));
    }
}
