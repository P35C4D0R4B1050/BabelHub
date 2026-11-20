using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Biblioteca;

/// <summary>El contrato de Biblioteca para Devoluciones (T09; BIB-10, BIB-21, BIB-22, BIB-23, BIB-24).</summary>
public sealed class AccesosParaDevolucionesTests
{
    private readonly EntornoDeBiblioteca _entorno = new(ActorDePrueba.DelSistema());
    private readonly Guid _cuenta = Guid.NewGuid();
    private readonly Guid _pedido = Guid.NewGuid();

    private IAccesosParaDevoluciones Contrato => new AccesosParaDevolucionesDeLaAplicacion(_entorno.Accesos, _entorno.Auditoria, _entorno.Reloj);

    private Acceso Sembrar(int descargas = 0)
    {
        var acceso = _entorno.Accesos.Sembrar(_cuenta, Guid.NewGuid(), _pedido);
        for (var i = 0; i < descargas; i++)
        {
            acceso.ConsumirDescarga();
        }

        return acceso;
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-19")]
    public async Task Consultar_informa_si_se_entrego_algun_enlace_aunque_no_se_haya_usado()
    {
        var sinEnlace = Sembrar();
        var conEnlace = Sembrar(1);

        var a = (await Contrato.Consultar(_cuenta, _pedido, sinEnlace.EdicionId)).Valor!;
        var b = (await Contrato.Consultar(_cuenta, _pedido, conEnlace.EdicionId)).Valor!;

        Assert.False(a.EnlaceEntregado);
        Assert.True(b.EnlaceEntregado);
        Assert.Equal(sinEnlace.Id, a.AccesoId);
        Assert.Equal(EstadoDelAccesoParaDevoluciones.Vigente, a.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-19")]
    public async Task Tras_una_reactivacion_por_recompra_solo_cuentan_los_enlaces_posteriores()
    {
        var acceso = Sembrar(2);
        acceso.Revocar(_pedido, CausaDeRevocacion.Reembolso, _entorno.Reloj.Ahora);
        var pedidoNuevo = Guid.NewGuid();
        acceso.Reconceder(pedidoNuevo, _entorno.Reloj.Ahora);

        Assert.Null((await Contrato.Consultar(_cuenta, _pedido, acceso.EdicionId)).Valor);
        var nuevo = (await Contrato.Consultar(_cuenta, pedidoNuevo, acceso.EdicionId)).Valor!;
        Assert.False(nuevo.EnlaceEntregado);

        acceso.ConsumirDescarga();
        Assert.True((await Contrato.Consultar(_cuenta, pedidoNuevo, acceso.EdicionId)).Valor!.EnlaceEntregado);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-19")]
    public async Task Consultar_el_acceso_de_otra_cuenta_o_inexistente_es_sin_acceso()
    {
        var acceso = Sembrar();

        Assert.Null((await Contrato.Consultar(Guid.NewGuid(), _pedido, acceso.EdicionId)).Valor);
        Assert.Null((await Contrato.Consultar(_cuenta, Guid.NewGuid(), acceso.EdicionId)).Valor);
        Assert.Null((await Contrato.Consultar(_cuenta, _pedido, Guid.NewGuid())).Valor);
        Assert.Equal(CategoriaDeError.Validacion, (await Contrato.Consultar(Guid.Empty, _pedido, acceso.EdicionId)).Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-20")]
    public async Task Revocar_sin_enlace_revoca_audita_con_el_motivo_reembolso_y_si_hay_enlace_no_cambia_nada()
    {
        var limpio = Sembrar();
        var conEnlace = Sembrar(1);

        Assert.Equal(ResultadoDeRevocarSiNoHayEnlace.Revocado, (await Contrato.RevocarSiNoHayEnlace(_cuenta, _pedido, limpio.EdicionId)).Valor);
        Assert.Equal(ResultadoDeRevocarSiNoHayEnlace.EnlaceYaEntregado, (await Contrato.RevocarSiNoHayEnlace(_cuenta, _pedido, conEnlace.EdicionId)).Valor);

        Assert.Equal(EstadoDeAcceso.Revocado, limpio.Estado);
        Assert.Equal(EstadoDeAcceso.Vigente, conEnlace.Estado);
        var entrada = Assert.Single(_entorno.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.AccesoDigitalRevocado, entrada.Accion);
        Assert.Equal("reembolso", entrada.Detalle["causa"]);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-21")]
    public async Task Revocar_sin_enlace_dos_veces_audita_una_sola_vez_y_un_pedido_ajeno_o_suspendido_no_cambia()
    {
        var acceso = Sembrar();

        Assert.Equal(ResultadoDeRevocarSiNoHayEnlace.SinAcceso, (await Contrato.RevocarSiNoHayEnlace(_cuenta, Guid.NewGuid(), acceso.EdicionId)).Valor);
        Assert.Equal(ResultadoDeRevocarSiNoHayEnlace.SinAcceso, (await Contrato.RevocarSiNoHayEnlace(Guid.NewGuid(), _pedido, acceso.EdicionId)).Valor);
        Assert.Equal(ResultadoDeRevocarSiNoHayEnlace.Revocado, (await Contrato.RevocarSiNoHayEnlace(_cuenta, _pedido, acceso.EdicionId)).Valor);
        Assert.Equal(ResultadoDeRevocarSiNoHayEnlace.YaRevocado, (await Contrato.RevocarSiNoHayEnlace(_cuenta, _pedido, acceso.EdicionId)).Valor);
        Assert.Single(_entorno.Auditoria.Solicitudes);

        var suspendido = Sembrar();
        suspendido.Suspender(_entorno.Reloj.Ahora);
        Assert.Equal(ResultadoDeRevocarSiNoHayEnlace.NoDisponible, (await Contrato.RevocarSiNoHayEnlace(_cuenta, _pedido, suspendido.EdicionId)).Valor);
        Assert.Equal(EstadoDeAcceso.Suspendido, suspendido.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-20")]
    public async Task Si_la_auditoria_cae_la_revocacion_sin_enlace_se_revierte_con_la_transaccion_del_llamador()
    {
        var acceso = Sembrar();
        _entorno.Auditoria.Caida = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => _entorno.Unidad.Ejecutar(async ct => (await Contrato.RevocarSiNoHayEnlace(_cuenta, _pedido, acceso.EdicionId, ct)).Valor));

        Assert.Equal(EstadoDeAcceso.Vigente, _entorno.Accesos.Buscar(acceso.Id)!.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-16")]
    public async Task Suspender_parte_solo_de_vigente_y_reanudar_solo_de_suspendido_y_audita_la_reanudacion()
    {
        var acceso = Sembrar();

        Assert.Equal(ResultadoDeSuspenderAcceso.Suspendido, (await Contrato.Suspender(_cuenta, acceso.Id)).Valor);
        Assert.Equal(ResultadoDeSuspenderAcceso.YaSuspendido, (await Contrato.Suspender(_cuenta, acceso.Id)).Valor);
        Assert.Equal(ResultadoDeSuspenderAcceso.NoDisponible, (await Contrato.Suspender(Guid.NewGuid(), acceso.Id)).Valor);

        Assert.Equal(ResultadoDeReanudarAcceso.NoDisponible, (await Contrato.Reanudar(Guid.NewGuid(), acceso.Id)).Valor);
        Assert.Equal(ResultadoDeReanudarAcceso.Reanudado, (await Contrato.Reanudar(_cuenta, acceso.Id)).Valor);
        Assert.Equal(ResultadoDeReanudarAcceso.NoDisponible, (await Contrato.Reanudar(_cuenta, acceso.Id)).Valor);

        var entrada = Assert.Single(_entorno.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.ReporteDeArchivoResuelto, entrada.Accion);
        Assert.Equal(acceso.EdicionId.ToString(), entrada.Detalle["edicionId"]);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-16")]
    public async Task Un_revocado_no_se_suspende_ni_se_reanuda_y_un_suspendido_no_entrega_enlace()
    {
        var revocado = Sembrar();
        revocado.Revocar(_pedido, CausaDeRevocacion.Reembolso, _entorno.Reloj.Ahora);

        Assert.Equal(ResultadoDeSuspenderAcceso.NoDisponible, (await Contrato.Suspender(_cuenta, revocado.Id)).Valor);
        Assert.Equal(ResultadoDeReanudarAcceso.NoDisponible, (await Contrato.Reanudar(_cuenta, revocado.Id)).Valor);
        Assert.Equal(EstadoDeAcceso.Revocado, revocado.Estado);

        var suspendido = Sembrar();
        await Contrato.Suspender(_cuenta, suspendido.Id);
        Assert.Equal(ResultadoDeConsumo.NoDisponible, suspendido.ConsumirDescarga());
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-20")]
    public async Task Entrega_y_revocacion_sin_enlace_nunca_dejan_un_enlace_sobre_un_acceso_revocado_ni_un_revocado_con_enlace()
    {
        for (var i = 0; i < 50; i++)
        {
            var acceso = _entorno.Accesos.Sembrar(_cuenta, Guid.NewGuid(), _pedido);
            var primeroLaEntrega = i % 2 == 0;

            // En el repositorio real gana la primera sentencia que confirma; aquí se prueban los dos órdenes posibles de las mismas sentencias condicionales.
            if (primeroLaEntrega)
            {
                acceso.ConsumirDescarga();
                await Contrato.RevocarSiNoHayEnlace(_cuenta, _pedido, acceso.EdicionId);
            }
            else
            {
                await Contrato.RevocarSiNoHayEnlace(_cuenta, _pedido, acceso.EdicionId);
                acceso.ConsumirDescarga();
            }

            Assert.False(acceso.Estado == EstadoDeAcceso.Revocado && acceso.EnlacesEntregados > 0);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-20")]
    public async Task Con_la_base_caida_las_operaciones_fallan_cerradas()
    {
        var acceso = Sembrar();
        _entorno.Accesos.NoDisponible = true;

        Assert.Equal(CategoriaDeError.NoDisponible, (await Contrato.Consultar(_cuenta, _pedido, acceso.EdicionId)).Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, (await Contrato.RevocarSiNoHayEnlace(_cuenta, _pedido, acceso.EdicionId)).Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, (await Contrato.Suspender(_cuenta, acceso.Id)).Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, (await Contrato.Reanudar(_cuenta, acceso.Id)).Error!.Categoria);
    }
}
