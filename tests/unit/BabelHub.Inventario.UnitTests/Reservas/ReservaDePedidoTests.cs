using BabelHub.Inventario.Domain.Reservas;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>T02: la máquina de estados de la línea, la expiración de 15 minutos con una sola extensión a 30 y la regla de que una vencida no cuenta como activa (SEL-01, SEL-06, SEL-08, SEL-22, SEL-27 a SEL-30).</summary>
public sealed class ReservaDePedidoTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    internal static LineaDeReserva Linea(EstadoDeLinea estado, CausaDeCierre? causa = null, int cantidad = 2)
    {
        var linea = LineaDeReserva.Pendiente(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), cantidad, Inicio);
        switch (estado)
        {
            case EstadoDeLinea.Confirmada:
                linea.Confirmar(Inicio);
                break;
            case EstadoDeLinea.Cerrada:
                linea.Confirmar(Inicio);
                linea.Cerrar(causa ?? CausaDeCierre.Entrega, Inicio);
                break;
            case EstadoDeLinea.Liberada:
                linea.Liberar(Inicio);
                break;
            case EstadoDeLinea.Expirada:
                linea.Expirar(Inicio);
                break;
        }

        return linea;
    }

    internal static ReservaDePedido Reserva(params LineaDeReserva[] lineas) =>
        ReservaDePedido.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TipoDeEntrega.Recojo, Guid.NewGuid(), null, Inicio, lineas.Length == 0 ? [Linea(EstadoDeLinea.Pendiente)] : lineas);

    // --- la línea ---

    [Fact]
    [Trait("Criterio", "AC-SEL-06")]
    public void Una_linea_nueva_es_pendiente_y_confirmar_la_pasa_a_confirmada_con_su_fecha()
    {
        var linea = Linea(EstadoDeLinea.Pendiente);
        Assert.Equal(EstadoDeLinea.Pendiente, linea.Estado);
        Assert.Null(linea.ConfirmadaEn);

        linea.Confirmar(Inicio.AddMinutes(3));

        Assert.Equal((EstadoDeLinea.Confirmada, (DateTimeOffset?)Inicio.AddMinutes(3)), (linea.Estado, linea.ConfirmadaEn));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-28")]
    [InlineData(EstadoDeLinea.Pendiente, true, true, true, false)]
    [InlineData(EstadoDeLinea.Confirmada, false, true, false, true)]
    [InlineData(EstadoDeLinea.Cerrada, false, false, false, false)]
    [InlineData(EstadoDeLinea.Liberada, false, false, false, false)]
    [InlineData(EstadoDeLinea.Expirada, false, false, false, false)]
    public void Las_transiciones_permitidas_son_exactamente_las_de_la_maquina_de_estados(EstadoDeLinea estado, bool confirma, bool libera, bool expira, bool cierra)
    {
        Assert.Equal(confirma, Intenta(estado, l => l.Confirmar(Inicio)));
        Assert.Equal(libera, Intenta(estado, l => l.Liberar(Inicio)));
        Assert.Equal(expira, Intenta(estado, l => l.Expirar(Inicio)));
        Assert.Equal(cierra, Intenta(estado, l => l.Cerrar(CausaDeCierre.Entrega, Inicio)));
    }

    private static bool Intenta(EstadoDeLinea estado, Action<LineaDeReserva> transicion)
    {
        var linea = Linea(estado);
        try
        {
            transicion(linea);
            return true;
        }
        catch (TransicionDeReservaInvalidaException)
        {
            return false;
        }
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-29")]
    [InlineData(CausaDeCierre.Entrega)]
    [InlineData(CausaDeCierre.Reparto)]
    [InlineData(CausaDeCierre.Traslado)]
    public void Una_linea_confirmada_se_cierra_con_su_causa_y_ya_no_vuelve_a_cerrarse_ni_a_liberarse(CausaDeCierre causa)
    {
        var linea = Linea(EstadoDeLinea.Confirmada);

        linea.Cerrar(causa, Inicio.AddHours(1));

        Assert.Equal((EstadoDeLinea.Cerrada, (CausaDeCierre?)causa), (linea.Estado, linea.CerradaPor));
        Assert.Throws<TransicionDeReservaInvalidaException>(() => linea.Cerrar(causa, Inicio));
        Assert.Throws<TransicionDeReservaInvalidaException>(() => linea.Liberar(Inicio));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-07")]
    public void Anular_solo_vale_para_una_linea_cerrada_por_traslado_y_no_se_repite()
    {
        var porTraslado = Linea(EstadoDeLinea.Cerrada, CausaDeCierre.Traslado);
        var porEntrega = Linea(EstadoDeLinea.Cerrada, CausaDeCierre.Entrega);

        porTraslado.Anular(Inicio.AddHours(2));
        porTraslado.Anular(Inicio.AddHours(3));

        Assert.Equal(Inicio.AddHours(2), porTraslado.AnuladaEn);
        Assert.Throws<TransicionDeReservaInvalidaException>(() => porEntrega.Anular(Inicio));
        Assert.Throws<TransicionDeReservaInvalidaException>(() => Linea(EstadoDeLinea.Confirmada).Anular(Inicio));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-11")]
    public void La_linea_que_un_traslado_recrea_en_el_destino_nace_confirmada_y_marcada_para_no_contar_como_venta()
    {
        var recreada = LineaDeReserva.ConfirmadaPorTraslado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid(), Inicio);

        Assert.Equal((EstadoDeLinea.Confirmada, true), (recreada.Estado, recreada.CreadaPorTraslado));
        Assert.NotNull(recreada.TrasladoId);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-11")]
    public void La_linea_consumida_en_el_origen_se_marca_recreada_en_el_destino_solo_si_se_cerro_por_traslado()
    {
        var origen = Linea(EstadoDeLinea.Cerrada, CausaDeCierre.Traslado);
        Assert.False(origen.RecreadaEnDestino);

        origen.MarcarRecreadaEnDestino();

        Assert.True(origen.RecreadaEnDestino);
        Assert.Throws<TransicionDeReservaInvalidaException>(() => Linea(EstadoDeLinea.Cerrada, CausaDeCierre.Entrega).MarcarRecreadaEnDestino());
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public void Una_linea_exige_cantidad_positiva_e_identificadores_y_solo_la_cerrada_lleva_causa()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LineaDeReserva.Pendiente(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Inicio));
        Assert.Throws<ArgumentException>(() => LineaDeReserva.Pendiente(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Inicio));
        Assert.Throws<ArgumentException>(() => LineaDeReserva.Rehidratar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, EstadoDeLinea.Confirmada, CausaDeCierre.Entrega, null, false, false, null, null, Inicio));
    }

    // --- la expiración ---

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public void La_reserva_expira_a_los_quince_minutos_y_en_el_instante_exacto_ya_esta_vencida()
    {
        var reserva = Reserva();

        Assert.Equal(Inicio.AddMinutes(15), reserva.FechaDeExpiracion);
        Assert.False(reserva.EstaVencida(Inicio.AddMinutes(15).AddTicks(-1)));
        Assert.True(reserva.EstaVencida(Inicio.AddMinutes(15)));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public void Una_pendiente_vencida_que_nadie_barrio_no_cuenta_como_activa_y_una_confirmada_si()
    {
        var pendiente = Linea(EstadoDeLinea.Pendiente);
        var reserva = Reserva(pendiente);

        Assert.True(pendiente.EstaActiva(reserva.FechaDeExpiracion, Inicio.AddMinutes(14)));
        Assert.False(pendiente.EstaActiva(reserva.FechaDeExpiracion, Inicio.AddMinutes(16)));
        Assert.Empty(reserva.LineasActivas(Inicio.AddMinutes(16)));
        Assert.True(Linea(EstadoDeLinea.Confirmada).EstaActiva(null, Inicio.AddDays(30)));
        Assert.False(Linea(EstadoDeLinea.Liberada).EstaActiva(null, Inicio));
        Assert.False(Linea(EstadoDeLinea.Expirada).EstaActiva(null, Inicio));
        Assert.False(Linea(EstadoDeLinea.Cerrada).EstaActiva(null, Inicio));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-24")]
    public void Se_extiende_una_sola_vez_a_treinta_minutos_desde_el_inicio_del_pago()
    {
        var reserva = Reserva();

        var primera = reserva.Extender(Inicio.AddMinutes(10));
        var segunda = reserva.Extender(Inicio.AddMinutes(12));

        Assert.True(primera);
        Assert.False(segunda);
        Assert.Equal(Inicio.AddMinutes(30), reserva.FechaDeExpiracion);
        Assert.True(reserva.Extendida);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-30")]
    [InlineData("vencida")]
    [InlineData("confirmada")]
    [InlineData("liberada")]
    [InlineData("expirada")]
    public void El_aviso_de_pago_pendiente_se_ignora_si_ya_vencio_se_confirmo_o_se_libero(string situacion)
    {
        var estado = situacion switch
        {
            "confirmada" => EstadoDeLinea.Confirmada,
            "liberada" => EstadoDeLinea.Liberada,
            "expirada" => EstadoDeLinea.Expirada,
            _ => EstadoDeLinea.Pendiente,
        };
        var reserva = Reserva(Linea(estado));
        var antes = reserva.FechaDeExpiracion;
        var ahora = situacion == "vencida" ? Inicio.AddMinutes(20) : Inicio.AddMinutes(5);

        var extendida = reserva.Extender(ahora);

        Assert.False(extendida);
        Assert.Equal(antes, reserva.FechaDeExpiracion);
        Assert.False(reserva.Extendida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-24")]
    public void Un_reemplazo_reinicia_a_quince_minutos_pero_hereda_que_ya_se_extendio()
    {
        var anterior = Reserva();
        anterior.Extender(Inicio.AddMinutes(5));

        var reemplazo = ReservaDePedido.Iniciar(
            Guid.NewGuid(), anterior.PedidoId, anterior.ClienteId, TipoDeEntrega.Recojo, anterior.SucursalDeRecojoId, null, Inicio.AddMinutes(8), [Linea(EstadoDeLinea.Pendiente)], reemplazos: 1, extendida: anterior.Extendida);

        Assert.Equal(Inicio.AddMinutes(23), reemplazo.FechaDeExpiracion);
        Assert.False(reemplazo.Extender(Inicio.AddMinutes(9)));
        Assert.Equal(Inicio.AddMinutes(23), reemplazo.FechaDeExpiracion);
    }

    // --- confirmar ---

    [Fact]
    [Trait("Criterio", "AC-SEL-06")]
    public void Confirmar_pasa_las_pendientes_a_confirmadas_y_la_reserva_ya_no_expira()
    {
        var reserva = Reserva(Linea(EstadoDeLinea.Pendiente), Linea(EstadoDeLinea.Pendiente));

        var cambio = reserva.Confirmar(Inicio.AddMinutes(4));

        Assert.True(cambio);
        Assert.All(reserva.Lineas, l => Assert.Equal(EstadoDeLinea.Confirmada, l.Estado));
        Assert.Null(reserva.FechaDeExpiracion);
        Assert.False(reserva.EstaVencida(Inicio.AddDays(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-06")]
    public void Confirmar_otra_vez_es_un_exito_sin_cambios()
    {
        var reserva = Reserva(Linea(EstadoDeLinea.Pendiente));
        reserva.Confirmar(Inicio.AddMinutes(1));

        Assert.False(reserva.Confirmar(Inicio.AddMinutes(2)));
        Assert.All(reserva.Lineas, l => Assert.Equal((DateTimeOffset?)Inicio.AddMinutes(1), l.ConfirmadaEn));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-27")]
    [InlineData("vencida")]
    [InlineData("expirada")]
    [InlineData("liberada")]
    public void Confirmar_una_reserva_vencida_expirada_o_liberada_se_rechaza_sin_cambios(string situacion)
    {
        var estado = situacion switch
        {
            "expirada" => EstadoDeLinea.Expirada,
            "liberada" => EstadoDeLinea.Liberada,
            _ => EstadoDeLinea.Pendiente,
        };
        var reserva = Reserva(Linea(estado));

        Assert.Throws<TransicionDeReservaInvalidaException>(() => reserva.Confirmar(situacion == "vencida" ? Inicio.AddMinutes(16) : Inicio.AddMinutes(1)));
        Assert.DoesNotContain(reserva.Lineas, l => l.Estado == EstadoDeLinea.Confirmada);
    }

    // --- la cabecera ---

    [Fact]
    [Trait("Criterio", "AC-SEL-13")]
    public void Solo_cuenta_como_pendiente_vigente_la_cabecera_con_lineas_pendientes_que_no_vencieron()
    {
        var vigente = Reserva();
        var confirmada = Reserva();
        confirmada.Confirmar(Inicio);
        var liberada = Reserva(Linea(EstadoDeLinea.Liberada));

        Assert.True(vigente.TienePendienteVigente(Inicio.AddMinutes(14)));
        Assert.False(vigente.TienePendienteVigente(Inicio.AddMinutes(15)));
        Assert.False(confirmada.TienePendienteVigente(Inicio));
        Assert.False(liberada.TienePendienteVigente(Inicio));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public void Iniciar_exige_lineas_distintas_la_sucursal_del_recojo_y_la_region_del_domicilio()
    {
        var linea = Linea(EstadoDeLinea.Pendiente);

        Assert.Throws<ArgumentException>(() => ReservaDePedido.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TipoDeEntrega.Recojo, Guid.NewGuid(), null, Inicio, []));
        Assert.Throws<ArgumentException>(() => ReservaDePedido.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TipoDeEntrega.Recojo, Guid.NewGuid(), null, Inicio, [linea, linea]));
        Assert.Throws<ArgumentException>(() => ReservaDePedido.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TipoDeEntrega.Recojo, null, null, Inicio, [Linea(EstadoDeLinea.Pendiente)]));
        Assert.Throws<ArgumentException>(() => ReservaDePedido.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TipoDeEntrega.Domicilio, null, " ", Inicio, [Linea(EstadoDeLinea.Pendiente)]));
        Assert.NotNull(ReservaDePedido.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TipoDeEntrega.Domicilio, null, "cusco", Inicio, [Linea(EstadoDeLinea.Pendiente)]));
    }
}
