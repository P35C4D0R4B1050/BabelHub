using BabelHub.Ecommerce.Domain.Biblioteca;

namespace BabelHub.Ecommerce.UnitTests.Biblioteca;

/// <summary>El acceso, sus tres estados, el cupo y las transiciones idempotentes (T01; BIB-06, BIB-08, BIB-10, BIB-19, BIB-20, BIB-24).</summary>
public sealed class AccesoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Pedido1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Pedido2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    internal static Acceso Nuevo(Guid? pedido = null) => Acceso.Conceder(Guid.NewGuid(), Guid.NewGuid(), pedido ?? Pedido1, Ahora);

    private static Acceso ConDescargas(int usadas)
    {
        var acceso = Nuevo();
        for (var i = 0; i < usadas; i++)
        {
            Assert.Equal(ResultadoDeConsumo.Consumida, acceso.ConsumirDescarga());
        }

        return acceso;
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-01")]
    public void Un_acceso_concedido_esta_vigente_sin_descargas_ni_enlaces()
    {
        var acceso = Nuevo();

        Assert.Equal(EstadoDeAcceso.Vigente, acceso.Estado);
        Assert.Equal(0, acceso.DescargasUsadas);
        Assert.Equal(5, acceso.DescargasRestantes);
        Assert.Equal(0, acceso.EnlacesEntregados);
        Assert.Equal(Ahora, acceso.FechaDeCompra);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-01")]
    public void Conceder_con_un_identificador_vacio_se_rechaza()
    {
        Assert.Throws<ArgumentException>(() => Acceso.Conceder(Guid.Empty, Guid.NewGuid(), Pedido1, Ahora));
        Assert.Throws<ArgumentException>(() => Acceso.Conceder(Guid.NewGuid(), Guid.Empty, Pedido1, Ahora));
        Assert.Throws<ArgumentException>(() => Acceso.Conceder(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-05")]
    public void La_sexta_descarga_se_rechaza_por_falta_de_cupo()
    {
        var acceso = ConDescargas(5);

        Assert.Equal(ResultadoDeConsumo.SinCupo, acceso.ConsumirDescarga());
        Assert.Equal(5, acceso.DescargasUsadas);
        Assert.Equal(5, acceso.EnlacesEntregados);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-17")]
    public void Consumir_cuenta_la_descarga_y_el_enlace_a_la_vez()
    {
        var acceso = ConDescargas(2);

        Assert.Equal(ResultadoDeConsumo.Consumida, acceso.ConsumirDescarga());
        Assert.Equal(3, acceso.DescargasUsadas);
        Assert.Equal(3, acceso.EnlacesEntregados);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-09")]
    public void Un_acceso_revocado_o_suspendido_no_consume()
    {
        var revocado = Nuevo();
        revocado.Revocar(Pedido1, CausaDeRevocacion.Reembolso, Ahora);
        var suspendido = Nuevo();
        suspendido.Suspender(Ahora);

        Assert.Equal(ResultadoDeConsumo.NoDisponible, revocado.ConsumirDescarga());
        Assert.Equal(ResultadoDeConsumo.NoDisponible, suspendido.ConsumirDescarga());
        Assert.Equal(0, revocado.DescargasUsadas);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-07")]
    public void Restituir_baja_las_usadas_pero_no_los_enlaces_entregados()
    {
        var acceso = ConDescargas(5);

        Assert.Equal(ResultadoDeRestitucion.Restituida, acceso.Restituir(1));

        Assert.Equal(4, acceso.DescargasUsadas);
        Assert.Equal(5, acceso.EnlacesEntregados);
        Assert.Equal(ResultadoDeConsumo.Consumida, acceso.ConsumirDescarga());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    [Trait("Criterio", "AC-BIB-07")]
    public void Restituir_fuera_de_uno_hasta_las_usadas_se_rechaza(int cantidad)
    {
        var acceso = ConDescargas(2);

        Assert.Equal(ResultadoDeRestitucion.CantidadInvalida, acceso.Restituir(cantidad));
        Assert.Equal(2, acceso.DescargasUsadas);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-07")]
    public void Restituir_un_acceso_revocado_no_cambia_nada()
    {
        var acceso = ConDescargas(2);
        acceso.Revocar(Pedido1, CausaDeRevocacion.Reembolso, Ahora);

        Assert.Equal(ResultadoDeRestitucion.NoDisponible, acceso.Restituir(1));
        Assert.Equal(2, acceso.DescargasUsadas);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-21")]
    public void Revocar_dos_veces_conserva_la_primera_causa_y_fecha()
    {
        var acceso = Nuevo();
        var despues = Ahora.AddHours(1);

        Assert.Equal(ResultadoDeRevocacion.Revocado, acceso.Revocar(Pedido1, CausaDeRevocacion.AnulacionDePedido, Ahora));
        Assert.Equal(ResultadoDeRevocacion.YaRevocado, acceso.Revocar(Pedido1, CausaDeRevocacion.Reembolso, despues));

        Assert.Equal(EstadoDeAcceso.Revocado, acceso.Estado);
        Assert.Equal(CausaDeRevocacion.AnulacionDePedido, acceso.CausaDeRevocacion);
        Assert.Equal(Ahora, acceso.RevocadoEn);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-21")]
    public void Una_orden_tardia_de_un_pedido_viejo_no_toca_el_acceso_reactivado()
    {
        var acceso = Nuevo(Pedido1);
        acceso.Revocar(Pedido1, CausaDeRevocacion.AnulacionDePedido, Ahora);
        Assert.Equal(ResultadoDeConcesion.Reactivado, acceso.Reconceder(Pedido2, Ahora.AddDays(1)));

        Assert.Equal(ResultadoDeRevocacion.YaRevocado, acceso.Revocar(Pedido1, CausaDeRevocacion.AnulacionDePedido, Ahora.AddDays(2)));
        Assert.Equal(ResultadoDeRevocacionSinEnlace.SinAcceso, acceso.RevocarSiNoHayEnlace(Pedido1, CausaDeRevocacion.Reembolso, Ahora.AddDays(2)));

        Assert.Equal(EstadoDeAcceso.Vigente, acceso.Estado);
        Assert.Equal(Pedido2, acceso.PedidoId);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-17")]
    public void Repetir_la_concesion_con_el_mismo_pedido_no_la_duplica_ni_reactiva_un_revocado()
    {
        var acceso = Nuevo(Pedido1);
        Assert.Equal(ResultadoDeConcesion.YaConcedidoPorEstePedido, acceso.Reconceder(Pedido1, Ahora));

        acceso.Revocar(Pedido1, CausaDeRevocacion.Reembolso, Ahora);
        Assert.Equal(ResultadoDeConcesion.YaConcedidoPorEstePedido, acceso.Reconceder(Pedido1, Ahora.AddDays(1)));
        Assert.Equal(EstadoDeAcceso.Revocado, acceso.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-18")]
    public void Recomprar_un_revocado_reinicia_el_cupo_y_los_enlaces()
    {
        var acceso = ConDescargas(4);
        acceso.Revocar(Pedido1, CausaDeRevocacion.Reembolso, Ahora);
        var recompra = Ahora.AddDays(3);

        Assert.Equal(ResultadoDeConcesion.Reactivado, acceso.Reconceder(Pedido2, recompra));

        Assert.Equal(EstadoDeAcceso.Vigente, acceso.Estado);
        Assert.Equal(0, acceso.DescargasUsadas);
        Assert.Equal(0, acceso.EnlacesEntregados);
        Assert.Equal(Pedido2, acceso.PedidoId);
        Assert.Equal(recompra, acceso.FechaDeCompra);
        Assert.Null(acceso.RevocadoEn);
        Assert.Null(acceso.CausaDeRevocacion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Criterio", "AC-BIB-18")]
    public void Recomprar_un_acceso_vigente_o_suspendido_es_ya_poseido_por_otra_compra(bool suspendido)
    {
        var acceso = Nuevo(Pedido1);
        if (suspendido)
        {
            acceso.Suspender(Ahora);
        }

        Assert.Equal(ResultadoDeConcesion.YaPoseidoPorOtraCompra, acceso.Reconceder(Pedido2, Ahora));
        Assert.Equal(Pedido1, acceso.PedidoId);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-20")]
    public void Revocar_sin_enlace_gana_solo_si_no_se_entrego_ninguno()
    {
        var limpio = Nuevo();
        var conEnlace = ConDescargas(1);

        Assert.Equal(ResultadoDeRevocacionSinEnlace.Revocado, limpio.RevocarSiNoHayEnlace(Pedido1, CausaDeRevocacion.Reembolso, Ahora));
        Assert.Equal(ResultadoDeRevocacionSinEnlace.EnlaceYaEntregado, conEnlace.RevocarSiNoHayEnlace(Pedido1, CausaDeRevocacion.Reembolso, Ahora));
        Assert.Equal(EstadoDeAcceso.Vigente, conEnlace.Estado);
        Assert.Equal(ResultadoDeRevocacionSinEnlace.YaRevocado, limpio.RevocarSiNoHayEnlace(Pedido1, CausaDeRevocacion.Reembolso, Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-20")]
    public void Un_enlace_restituido_sigue_contando_como_entregado()
    {
        var acceso = ConDescargas(1);
        acceso.Restituir(1);

        Assert.Equal(ResultadoDeRevocacionSinEnlace.EnlaceYaEntregado, acceso.RevocarSiNoHayEnlace(Pedido1, CausaDeRevocacion.Reembolso, Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-16")]
    public void Un_suspendido_no_se_revoca_sin_enlace_y_se_reanuda_o_se_revoca_con_reporte_aprobado()
    {
        var acceso = Nuevo();
        Assert.Equal(ResultadoDeSuspension.Suspendido, acceso.Suspender(Ahora));
        Assert.Equal(ResultadoDeSuspension.YaSuspendido, acceso.Suspender(Ahora));
        Assert.Equal(ResultadoDeRevocacionSinEnlace.NoDisponible, acceso.RevocarSiNoHayEnlace(Pedido1, CausaDeRevocacion.Reembolso, Ahora));

        Assert.Equal(ResultadoDeReanudacion.Reanudado, acceso.Reanudar());
        Assert.Equal(EstadoDeAcceso.Vigente, acceso.Estado);
        Assert.Equal(ResultadoDeReanudacion.NoDisponible, acceso.Reanudar());

        acceso.Suspender(Ahora);
        Assert.Equal(ResultadoDeRevocacion.Revocado, acceso.Revocar(Pedido1, CausaDeRevocacion.ReporteAprobado, Ahora));
        Assert.Equal(CausaDeRevocacion.ReporteAprobado, acceso.CausaDeRevocacion);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-16")]
    public void Un_revocado_no_pasa_a_suspendido_ni_se_reanuda()
    {
        var acceso = Nuevo();
        acceso.Revocar(Pedido1, CausaDeRevocacion.Reembolso, Ahora);

        Assert.Equal(ResultadoDeSuspension.NoDisponible, acceso.Suspender(Ahora));
        Assert.Equal(ResultadoDeReanudacion.NoDisponible, acceso.Reanudar());
        Assert.Equal(EstadoDeAcceso.Revocado, acceso.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-01")]
    public void Reconstruir_rechaza_estados_incoherentes()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => Acceso.Reconstruir(id, id, id, id, EstadoDeAcceso.Vigente, 6, 0, Ahora, null, null, null, null));
        Assert.Throws<ArgumentException>(() => Acceso.Reconstruir(id, id, id, id, EstadoDeAcceso.Revocado, 0, 0, Ahora, null, null, null, null));
        Assert.Throws<ArgumentException>(() => Acceso.Reconstruir(id, id, id, id, EstadoDeAcceso.Suspendido, 0, 0, Ahora, null, null, null, null));
    }
}
