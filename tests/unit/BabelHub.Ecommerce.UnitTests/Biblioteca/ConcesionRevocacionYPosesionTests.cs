using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Envios;
using BabelHub.Ecommerce.UnitTests.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Biblioteca;

/// <summary>Conceder, revocar y poseer (T04; BIB-01, BIB-19, BIB-20, BIB-24, CAR-20).</summary>
public sealed class ConcesionRevocacionYPosesionTests
{
    private static readonly Guid Cuenta = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Edicion = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly Guid Pedido1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Pedido2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly RelojFijo _reloj = new(new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero));
    private readonly RepositorioDeAccesosEnMemoria _accesos = new();
    private readonly AuditoriaDeEnviosEspia _auditoria = new();
    private readonly ConcederAcceso _conceder;
    private readonly RevocarAcceso _revocar;

    public ConcesionRevocacionYPosesionTests()
    {
        _conceder = new ConcederAcceso(_accesos, _reloj);
        _revocar = new RevocarAcceso(_accesos, _auditoria, _reloj);
    }

    private Task<Resultado<ResultadoDeConcederAcceso>> Conceder(Guid pedido) => _conceder.Ejecutar(new ConcederAccesoEntrada(Cuenta, pedido, Edicion));

    private Task<Resultado<ResultadoDeRevocarAcceso>> Revocar(Guid pedido, MotivoDeRevocacionDeAcceso motivo = MotivoDeRevocacionDeAcceso.PedidoAnulado) =>
        _revocar.Ejecutar(new RevocarAccesoEntrada(pedido, Edicion, motivo));

    [Fact]
    [Trait("Criterio", "AC-BIB-01")]
    public async Task Conceder_crea_un_acceso_vigente_a_la_cuenta_sin_tocar_el_inventario()
    {
        var resultado = await Conceder(Pedido1);

        Assert.Equal(ResultadoDeConcederAcceso.Concedido, resultado.Valor);
        var acceso = Assert.Single(_accesos.Todos);
        Assert.Equal(Cuenta, acceso.CuentaId);
        Assert.Equal(Edicion, acceso.EdicionId);
        Assert.Equal(EstadoDeAcceso.Vigente, acceso.Estado);
        Assert.Empty(_auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-17")]
    public async Task Repetir_la_aprobacion_del_pago_no_concede_un_segundo_acceso()
    {
        await Conceder(Pedido1);

        var repeticion = await Conceder(Pedido1);

        Assert.Equal(ResultadoDeConcederAcceso.YaConcedidoPorEstePedido, repeticion.Valor);
        Assert.Single(_accesos.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-18")]
    public async Task Recomprar_un_acceso_vigente_responde_ya_poseido_y_uno_revocado_lo_reactiva()
    {
        await Conceder(Pedido1);
        Assert.Equal(ResultadoDeConcederAcceso.YaPoseidoPorOtraCompra, (await Conceder(Pedido2)).Valor);

        await Revocar(Pedido1, MotivoDeRevocacionDeAcceso.Reembolso);
        var recompra = await Conceder(Pedido2);

        Assert.Equal(ResultadoDeConcederAcceso.Reactivado, recompra.Valor);
        var acceso = Assert.Single(_accesos.Todos);
        Assert.Equal(EstadoDeAcceso.Vigente, acceso.Estado);
        Assert.Equal(0, acceso.DescargasUsadas);
        Assert.Equal(Pedido2, acceso.PedidoId);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-01")]
    public async Task Conceder_con_identificadores_vacios_se_rechaza_sin_escribir()
    {
        var resultado = await _conceder.Ejecutar(new ConcederAccesoEntrada(Guid.Empty, Pedido1, Edicion));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Empty(_accesos.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-01")]
    public async Task Conceder_con_la_base_caida_falla_cerrado()
    {
        _accesos.NoDisponible = true;

        var resultado = await Conceder(Pedido1);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-21")]
    public async Task Revocar_audita_solo_la_primera_vez_con_su_causa_y_la_repeticion_no_cambia_nada()
    {
        await Conceder(Pedido1);

        Assert.Equal(ResultadoDeRevocarAcceso.Revocado, (await Revocar(Pedido1, MotivoDeRevocacionDeAcceso.PedidoAnulado)).Valor);
        Assert.Equal(ResultadoDeRevocarAcceso.YaRevocado, (await Revocar(Pedido1, MotivoDeRevocacionDeAcceso.Reembolso)).Valor);

        var entrada = Assert.Single(_auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.AccesoDigitalRevocado, entrada.Accion);
        Assert.Equal("anulacion-de-pedido", entrada.Detalle["causa"]);
        Assert.Equal(Edicion.ToString(), entrada.Detalle["edicionId"]);
        Assert.Equal("acceso-digital", entrada.ObjetoTipo);
        Assert.Equal("anulacion-de-pedido", _accesos.Todos.Single().CausaDeRevocacion!.Value.Codigo());
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-21")]
    public async Task Anular_el_pedido_viejo_dos_veces_tras_recomprar_deja_vigente_el_acceso_del_nuevo()
    {
        await Conceder(Pedido1);
        await Revocar(Pedido1);
        await Conceder(Pedido2);

        Assert.Equal(ResultadoDeRevocarAcceso.YaRevocado, (await Revocar(Pedido1)).Valor);
        Assert.Equal(ResultadoDeRevocarAcceso.YaRevocado, (await Revocar(Pedido1)).Valor);

        var acceso = Assert.Single(_accesos.Todos);
        Assert.Equal(EstadoDeAcceso.Vigente, acceso.Estado);
        Assert.Equal(Pedido2, acceso.PedidoId);
        Assert.Single(_auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-21")]
    public async Task Revocar_un_pedido_sin_acceso_es_ya_revocado_sin_auditar()
    {
        var resultado = await Revocar(Pedido1);

        Assert.Equal(ResultadoDeRevocarAcceso.YaRevocado, resultado.Valor);
        Assert.Empty(_auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-21")]
    public async Task Si_la_auditoria_cae_la_revocacion_se_revierte_con_la_unidad_de_trabajo()
    {
        await Conceder(Pedido1);
        var unidad = new UnidadDeTrabajoReversible(_accesos, _auditoria);
        _auditoria.Caida = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => unidad.Ejecutar(async ct => (await Revocar(Pedido1)).Valor));

        Assert.Equal(EstadoDeAcceso.Vigente, _accesos.Todos.Single().Estado);
    }

    [Theory]
    [InlineData(MotivoDeRevocacionDeAcceso.PedidoAnulado, "anulacion-de-pedido")]
    [InlineData(MotivoDeRevocacionDeAcceso.Reembolso, "reembolso")]
    [InlineData(MotivoDeRevocacionDeAcceso.ReporteAprobado, "reporte-aprobado")]
    [Trait("Criterio", "AC-BIB-21")]
    public async Task La_causa_auditada_es_el_motivo_del_contrato(MotivoDeRevocacionDeAcceso motivo, string codigo)
    {
        await Conceder(Pedido1);

        await Revocar(Pedido1, motivo);

        Assert.Equal(codigo, Assert.Single(_auditoria.Solicitudes).Detalle["causa"]);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-09")]
    public async Task Posee_cuenta_vigentes_y_suspendidos_y_no_revocados_y_no_consulta_nada_mas()
    {
        var otra = Guid.NewGuid();
        var vigente = _accesos.Sembrar(Cuenta, Edicion);
        var suspendido = _accesos.Sembrar(Cuenta, Guid.NewGuid());
        suspendido.Suspender(_reloj.Ahora);
        var revocado = _accesos.Sembrar(Cuenta, Guid.NewGuid());
        revocado.Revocar(revocado.PedidoId, CausaDeRevocacion.Reembolso, _reloj.Ahora);
        _accesos.Sembrar(Guid.NewGuid(), otra);
        var posesion = new ComprobarPosesion(_accesos);

        var resultado = await posesion.Posee(Cuenta, [vigente.EdicionId, suspendido.EdicionId, revocado.EdicionId, otra]);

        Assert.Equal(EstadoDePosesion.Posee, resultado.Estado);
        Assert.Equal(new[] { vigente.EdicionId, suspendido.EdicionId }.Order(), resultado.Poseidas.Order());
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-18")]
    public async Task Un_revocado_no_cuenta_como_poseido_y_el_vacio_es_no_posee()
    {
        var revocado = _accesos.Sembrar(Cuenta, Edicion);
        revocado.Revocar(revocado.PedidoId, CausaDeRevocacion.Reembolso, _reloj.Ahora);
        var posesion = new ComprobarPosesion(_accesos);

        Assert.Equal(EstadoDePosesion.NoPosee, (await posesion.Posee(Cuenta, [Edicion])).Estado);
        Assert.Equal(EstadoDePosesion.NoPosee, (await posesion.Posee(Cuenta, [])).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-01")]
    public async Task Ante_un_fallo_de_lectura_la_posesion_no_se_puede_confirmar()
    {
        _accesos.NoDisponible = true;
        var posesion = new ComprobarPosesion(_accesos);

        Assert.Equal(EstadoDePosesion.NoSePuedeConfirmar, (await posesion.Posee(Cuenta, [Edicion])).Estado);
        Assert.Equal(EstadoDePosesion.NoSePuedeConfirmar, (await posesion.Posee(Guid.Empty, [Edicion])).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-01")]
    public async Task La_fachada_de_accesos_delega_en_los_casos_de_uso()
    {
        IAccesosDeBiblioteca fachada = new AccesosDeBibliotecaDeLaAplicacion(_conceder, _revocar);

        Assert.Equal(ResultadoDeConcederAcceso.Concedido, (await fachada.Conceder(new ConcederAccesoEntrada(Cuenta, Pedido1, Edicion))).Valor);
        Assert.Equal(ResultadoDeRevocarAcceso.Revocado, (await fachada.Revocar(new RevocarAccesoEntrada(Pedido1, Edicion, MotivoDeRevocacionDeAcceso.PedidoAnulado))).Valor);
    }
}
